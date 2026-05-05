using System.Text.Json;
using TradeEnrichment.Application.Interfaces;
using TradeEnrichment.Domain.Entities;

namespace TradeEnrichment.Infrastructure.Serializers;

/// <summary>
/// Serializes enriched trades as a JSON array, streamed directly to the response.
/// </summary>
public sealed class JsonTradeSerializer : ITradeSerializer
{
    public string ContentType => "application/json";

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public async Task SerializeAsync(
        IAsyncEnumerable<EnrichedTrade> trades,
        Stream output,
        CancellationToken cancellationToken = default)
    {
        await using var writer = new Utf8JsonWriter(output, new JsonWriterOptions { Indented = false });

        writer.WriteStartArray();

        await foreach (var trade in trades.WithCancellation(cancellationToken))
        {
            writer.WriteStartObject();
            writer.WriteString("date", trade.Date);
            writer.WriteString("productName", trade.ProductName);
            writer.WriteString("currency", trade.Currency);
            writer.WriteNumber("price", trade.Price);
            writer.WriteEndObject();

            // Flush periodically to avoid building up a giant buffer.
            await writer.FlushAsync(cancellationToken);
        }

        writer.WriteEndArray();
        await writer.FlushAsync(cancellationToken);
    }
}
