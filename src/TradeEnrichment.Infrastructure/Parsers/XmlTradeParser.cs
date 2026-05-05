using System.Runtime.CompilerServices;
using System.Xml;
using Microsoft.Extensions.Logging;
using TradeEnrichment.Application.Interfaces;
using TradeEnrichment.Domain.Entities;

namespace TradeEnrichment.Infrastructure.Parsers;

/// <summary>
/// Streams Trade records from an XML payload using <see cref="XmlReader"/>
/// so the document is processed node-by-node without full DOM allocation.
///
/// Expected format:
/// &lt;trades&gt;
///   &lt;trade&gt;
///     &lt;date&gt;20160101&lt;/date&gt;
///     &lt;productId&gt;1&lt;/productId&gt;
///     &lt;currency&gt;EUR&lt;/currency&gt;
///     &lt;price&gt;10.0&lt;/price&gt;
///   &lt;/trade&gt;
/// &lt;/trades&gt;
/// </summary>
public sealed class XmlTradeParser : ITradeParser
{
    public string ContentType => "application/xml";

    private readonly ILogger<XmlTradeParser> _logger;

    public XmlTradeParser(ILogger<XmlTradeParser> logger) => _logger = logger;

    public async IAsyncEnumerable<Trade> ParseAsync(
        Stream stream,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var settings = new XmlReaderSettings
        {
            Async = true,
            IgnoreWhitespace = true,
            IgnoreComments = true
        };

        using var reader = XmlReader.Create(stream, settings);

        while (await reader.ReadAsync() && reader.Name != "trade") { }

        while (!reader.EOF)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (reader.NodeType == XmlNodeType.Element && reader.Name == "trade")
            {
                var trade = await ReadTradeElementAsync(reader);
                if (trade is not null)
                    yield return trade;
            }
            else
            {
                await reader.ReadAsync();
            }
        }
    }

    private async Task<Trade?> ReadTradeElementAsync(XmlReader reader)
    {
        string? date = null, currency = null;
        int productId = 0;
        decimal price = 0;

        // Read inner elements of <trade>…</trade>
        await reader.ReadAsync(); // move inside <trade>

        while (!(reader.NodeType == XmlNodeType.EndElement && reader.Name == "trade"))
        {
            if (reader.NodeType == XmlNodeType.Element)
            {
                var name = reader.Name;
                var value = await reader.ReadElementContentAsStringAsync();

                switch (name.ToLowerInvariant())
                {
                    case "date":      date = value; break;
                    case "productid": int.TryParse(value, out productId); break;
                    case "currency":  currency = value; break;
                    case "price":     decimal.TryParse(value, System.Globalization.NumberStyles.Any,
                                          System.Globalization.CultureInfo.InvariantCulture, out price);
                                      break;
                    default:
                        _logger.LogDebug("Unknown XML element <{Name}> ignored", name);
                        break;
                }
            }
            else
            {
                await reader.ReadAsync();
            }
        }

        await reader.ReadAsync(); // move past </trade>

        return new Trade
        {
            Date = date ?? string.Empty,
            ProductId = productId,
            Currency = currency ?? string.Empty,
            Price = price
        };
    }
}
