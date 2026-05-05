namespace TradeEnrichment.Domain.Entities;

public sealed record EnrichedTrade
{
    public string Date { get; init; } = string.Empty;
    public string ProductName { get; init; } = string.Empty;
    public string Currency { get; init; } = string.Empty;
    public decimal Price { get; init; }
}
