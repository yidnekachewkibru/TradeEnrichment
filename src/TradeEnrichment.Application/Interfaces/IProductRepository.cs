namespace TradeEnrichment.Application.Interfaces;

/// <summary>
/// Provides access to the product name lookup dictionary.
/// Implementations must be thread-safe; the dictionary is built once at startup.
/// </summary>
public interface IProductRepository
{
    /// <summary>
    /// Returns the product name for the given id,
    /// or null if no mapping exists.
    /// </summary>
    string? GetProductName(int productId);
}
