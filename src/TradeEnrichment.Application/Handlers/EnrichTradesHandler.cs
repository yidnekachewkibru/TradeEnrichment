using System.Globalization;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using MediatR;
using Microsoft.Extensions.Logging;
using TradeEnrichment.Application.Commands;
using TradeEnrichment.Application.Interfaces;
using TradeEnrichment.Domain.Entities;

namespace TradeEnrichment.Application.Handlers;

public sealed class EnrichTradesHandler
    : IRequestHandler<EnrichTradesCommand, IAsyncEnumerable<EnrichedTrade>>
{
    private const string DateFormat = "yyyyMMdd";
    private const string MissingProductName = "Missing Product Name";

    // Bound chosen to keep ~N rows in flight without saturating memory.
    // Tune based on expected row size and available RAM.
    private const int ChannelCapacity = 4_096;

    private readonly IEnumerable<ITradeParser> _parsers;
    private readonly IProductRepository _products;
    private readonly ILogger<EnrichTradesHandler> _logger;

    public EnrichTradesHandler(
        IEnumerable<ITradeParser> parsers,
        IProductRepository products,
        ILogger<EnrichTradesHandler> logger)
    {
        _parsers = parsers;
        _products = products;
        _logger = logger;
    }

    public Task<IAsyncEnumerable<EnrichedTrade>> Handle(
        EnrichTradesCommand request,
        CancellationToken cancellationToken)
    {
        var parser = ResolveParser(request.ContentType);

        IAsyncEnumerable<EnrichedTrade> result =
            EnrichViaChannelAsync(parser, request.InputStream, cancellationToken);

        return Task.FromResult(result);
    }

    // Private helpers

    private ITradeParser ResolveParser(string contentType)
    {
        // Normalise "text/csv; charset=utf-8" → "text/csv"
        var mediaType = contentType.Split(';')[0].Trim().ToLowerInvariant();

        var parser = _parsers.FirstOrDefault(p =>
            p.ContentType.Equals(mediaType, StringComparison.OrdinalIgnoreCase));

        if (parser is null)
            throw new NotSupportedException(
                $"No parser registered for content-type '{mediaType}'. " +
                $"Supported: {string.Join(", ", _parsers.Select(p => p.ContentType))}");

        return parser;
    }

    /// <summary>
    /// Runs the parser as a Channel producer and yields enriched trades as a consumer.
    /// Both sides run concurrently within the same async state machine.
    /// </summary>
    private async IAsyncEnumerable<EnrichedTrade> EnrichViaChannelAsync(
        ITradeParser parser,
        Stream stream,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var channel = Channel.CreateBounded<Trade>(new BoundedChannelOptions(ChannelCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,   // back-pressure: parser pauses when full
            SingleReader = true,
            SingleWriter = true
        });

        // Producer task: parse input and write raw trades to the channel.
        var producerTask = Task.Run(async () =>
        {
            try
            {
                await foreach (var trade in parser.ParseAsync(stream, cancellationToken))
                    await channel.Writer.WriteAsync(trade, cancellationToken);
            }
            finally
            {
                // Always complete the channel so the consumer doesn't wait forever.
                channel.Writer.Complete();
            }
        }, cancellationToken);

        // Consumer: read from channel, validate, enrich, and yield.
        await foreach (var trade in channel.Reader.ReadAllAsync(cancellationToken))
        {
            var enriched = TryEnrich(trade);
            if (enriched is not null)
                yield return enriched;
        }

        // Propagate any parser exception (e.g. malformed JSON).
        await producerTask;
    }

    /// <summary>
    /// Validates a single trade and looks up its product name.
    /// Returns null if the row should be discarded.
    /// </summary>
    private EnrichedTrade? TryEnrich(Trade trade)
    {
        // --- Date validation ---
        if (!DateTime.TryParseExact(
                trade.Date,
                DateFormat,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out _))
        {
            _logger.LogError(
                "Discarding row: invalid date '{Date}' (expected {Format}). " +
                "ProductId={ProductId}, Currency={Currency}, Price={Price}",
                trade.Date, DateFormat, trade.ProductId, trade.Currency, trade.Price);

            return null;
        }

        // --- Product name lookup ---
        var productName = _products.GetProductName(trade.ProductId);
        if (productName is null)
        {
            _logger.LogWarning(
                "ProductId {ProductId} not found in product catalogue; " +
                "using '{Fallback}'",
                trade.ProductId, MissingProductName);

            productName = MissingProductName;
        }

        return new EnrichedTrade
        {
            Date = trade.Date,
            ProductName = productName,
            Currency = trade.Currency,
            Price = trade.Price
        };
    }
}
