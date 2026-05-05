namespace TradeEnrichment.Domain.Entities;

public sealed record Trade
{
    public string Date { get; init; } = string.Empty;
    public int ProductId { get; init; }
    public string Currency { get; init; } = string.Empty;
    public decimal Price { get; init; }
}
