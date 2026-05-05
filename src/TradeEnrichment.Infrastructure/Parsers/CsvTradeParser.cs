using System.Globalization;
using System.Runtime.CompilerServices;
using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.Extensions.Logging;
using TradeEnrichment.Application.Interfaces;
using TradeEnrichment.Domain.Entities;

namespace TradeEnrichment.Infrastructure.Parsers;

public sealed class CsvTradeParser : ITradeParser
{
    public string ContentType => "text/csv";

    private readonly ILogger<CsvTradeParser> _logger;

    public CsvTradeParser(ILogger<CsvTradeParser> logger) => _logger = logger;

    public async IAsyncEnumerable<Trade> ParseAsync(
        Stream stream,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            MissingFieldFound = null,
            BadDataFound = ctx =>
                _logger.LogWarning(
                    "Skipping bad CSV field at row {Row}: {Field}",
                    ctx.Context.Parser.Row, ctx.Field)
        };

        using var reader = new StreamReader(stream, leaveOpen: true);
        using var csv = new CsvReader(reader, config);

        await csv.ReadAsync();
        csv.ReadHeader();

        while (await csv.ReadAsync())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!TryReadRow(csv, out var trade))
                continue;

            yield return trade!;
        }
    }

    private bool TryReadRow(CsvReader csv, out Trade? trade)
    {
        trade = null;

        try
        {
            var date     = csv.GetField<string>("date")?.Trim() ?? string.Empty;
            var currency = csv.GetField<string>("currency")?.Trim() ?? string.Empty;

            if (!csv.TryGetField<int>("productId", out var productId))
            {
                _logger.LogWarning("Row {Row}: non-integer productId — skipping", csv.CurrentIndex);
                return false;
            }

            if (!csv.TryGetField<decimal>("price", out var price))
            {
                _logger.LogWarning("Row {Row}: non-decimal price — skipping", csv.CurrentIndex);
                return false;
            }

            trade = new Trade
            {
                Date = date,
                ProductId = productId,
                Currency = currency,
                Price = price
            };
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Row {Row}: unexpected error while parsing — skipping", csv.CurrentIndex);
            return false;
        }
    }
}
