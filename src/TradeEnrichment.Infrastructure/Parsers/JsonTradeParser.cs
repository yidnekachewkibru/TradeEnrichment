using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using TradeEnrichment.Application.Interfaces;
using TradeEnrichment.Domain.Entities;

namespace TradeEnrichment.Infrastructure.Parsers;

public sealed class JsonTradeParser : ITradeParser
{
    public string ContentType => "application/json";

    private readonly ILogger<JsonTradeParser> _logger;

    public JsonTradeParser(ILogger<JsonTradeParser> logger) => _logger = logger;

    public async IAsyncEnumerable<Trade> ParseAsync(
        Stream stream,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        List<JsonTrade>? trades = null;

        try
        {
            // System.Text.Json's DeserializeAsync streams from the body.
            trades = await JsonSerializer.DeserializeAsync<List<JsonTrade>>(
                stream, options, cancellationToken);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to deserialize JSON trade payload");
            yield break;
        }

        if (trades is null) yield break;

        foreach (var t in trades)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return new Trade
            {
                Date = t.Date ?? string.Empty,
                ProductId = t.ProductId,
                Currency = t.Currency ?? string.Empty,
                Price = t.Price
            };
        }
    }

    // Internal DTO used only for deserialization.
    private sealed class JsonTrade
    {
        public string? Date { get; init; }
        public int ProductId { get; init; }
        public string? Currency { get; init; }
        public decimal Price { get; init; }
    }
}
