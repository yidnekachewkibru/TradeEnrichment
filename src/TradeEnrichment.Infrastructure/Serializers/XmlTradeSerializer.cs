using System.Xml;
using TradeEnrichment.Application.Interfaces;
using TradeEnrichment.Domain.Entities;

namespace TradeEnrichment.Infrastructure.Serializers;

/// <summary>
/// Serializes enriched trades as XML, streamed directly to the response.
/// </summary>
public sealed class XmlTradeSerializer : ITradeSerializer
{
    public string ContentType => "application/xml";

    public async Task SerializeAsync(
        IAsyncEnumerable<EnrichedTrade> trades,
        Stream output,
        CancellationToken cancellationToken = default)
    {
        var settings = new XmlWriterSettings
        {
            Async = true,
            Indent = true,
            Encoding = new System.Text.UTF8Encoding(false) // UTF-8 without BOM
        };

        await using var writer = XmlWriter.Create(output, settings);

        await writer.WriteStartDocumentAsync();
        await writer.WriteStartElementAsync(null, "trades", null);

        await foreach (var trade in trades.WithCancellation(cancellationToken))
        {
            await writer.WriteStartElementAsync(null, "trade", null);

            await writer.WriteElementStringAsync(null, "date",        null, trade.Date);
            await writer.WriteElementStringAsync(null, "productName", null, trade.ProductName);
            await writer.WriteElementStringAsync(null, "currency",    null, trade.Currency);
            await writer.WriteElementStringAsync(null, "price",       null,
                trade.Price.ToString(System.Globalization.CultureInfo.InvariantCulture));

            await writer.WriteEndElementAsync(); // </trade>

            await writer.FlushAsync();
        }

        await writer.WriteEndElementAsync(); // </trades>
        await writer.FlushAsync();
    }
}