using TradeEnrichment.Domain.Entities;

namespace TradeEnrichment.Application.Interfaces;

/// <summary>
/// Parses a stream of trade data into a sequence of Trade records.
/// Implementations stream records lazily for memory efficiency.
/// </summary>
public interface ITradeParser
{
    /// <summary>The MIME content-type this parser handles (e.g. "text/csv").</summary>
    string ContentType { get; }

    IAsyncEnumerable<Trade> ParseAsync(Stream stream, CancellationToken cancellationToken = default);
}
