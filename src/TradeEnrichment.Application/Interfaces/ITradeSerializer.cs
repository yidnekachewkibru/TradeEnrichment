using TradeEnrichment.Domain.Entities;

namespace TradeEnrichment.Application.Interfaces;

/// <summary>
/// Serializes enriched trades to an output stream.
/// </summary>
public interface ITradeSerializer
{
    string ContentType { get; }

    Task SerializeAsync(
        IAsyncEnumerable<EnrichedTrade> trades,
        Stream output,
        CancellationToken cancellationToken = default);
}
