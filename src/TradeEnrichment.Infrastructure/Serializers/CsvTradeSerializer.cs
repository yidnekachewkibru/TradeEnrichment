using System.Globalization;
using System.Text;
using TradeEnrichment.Application.Interfaces;
using TradeEnrichment.Domain.Entities;

namespace TradeEnrichment.Infrastructure.Serializers;

/// <summary>
/// Writes enriched trades as CSV directly to the response stream.
/// Uses a StreamWriter with a 64 KB buffer for efficient I/O.
/// </summary>
public sealed class CsvTradeSerializer : ITradeSerializer
{
    public string ContentType => "text/csv";

    public async Task SerializeAsync(
        IAsyncEnumerable<EnrichedTrade> trades,
        Stream output,
        CancellationToken cancellationToken = default)
    {
        // UTF-8 without BOM; 64 KB write buffer to reduce syscall frequency.
        await using var writer = new StreamWriter(output, new UTF8Encoding(false), 65_536, leaveOpen: true);

        await writer.WriteLineAsync("date,productName,currency,price");

        await foreach (var trade in trades.WithCancellation(cancellationToken))
        {
            await writer.WriteLineAsync(
                $"{trade.Date},{EscapeCsv(trade.ProductName)},{trade.Currency}," +
                $"{trade.Price.ToString(CultureInfo.InvariantCulture)}");
        }

        await writer.FlushAsync(cancellationToken);
    }

    /// <summary>Wraps a field in quotes if it contains a comma, quote, or newline.</summary>
    private static string EscapeCsv(string value)
    {
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
            return $"\"{value.Replace("\"", "\"\"")}\"";
        return value;
    }
}
