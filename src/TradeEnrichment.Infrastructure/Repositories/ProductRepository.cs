using System.Collections.Frozen;
using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.Extensions.Logging;
using TradeEnrichment.Application.Interfaces;

namespace TradeEnrichment.Infrastructure.Repositories;

/// <summary>
/// Loads product data from a CSV file at startup into a <see cref="FrozenDictionary{TKey,TValue}"/>.
/// </summary>
public sealed class ProductRepository : IProductRepository
{
    private readonly FrozenDictionary<int, string> _products;

    public ProductRepository(string csvFilePath, ILogger<ProductRepository> logger)
    {
        _products = LoadProducts(csvFilePath, logger);
        logger.LogInformation(
            "Product catalogue loaded: {Count} products from '{Path}'",
            _products.Count, csvFilePath);
    }

    public string? GetProductName(int productId) =>
        _products.TryGetValue(productId, out var name) ? name : null;

    private static FrozenDictionary<int, string> LoadProducts(
        string path,
        ILogger logger)
    {
        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            MissingFieldFound = null, // tolerate extra/missing columns
            BadDataFound = ctx =>
                logger.LogWarning("Skipping bad CSV data in product file at row {Row}: {Field}",
                    ctx.Context.Parser.Row, ctx.Field)
        };

        using var reader = new StreamReader(path);
        using var csv = new CsvReader(reader, config);

        var dict = new Dictionary<int, string>();

        csv.Read();
        csv.ReadHeader();

        while (csv.Read())
        {
            if (!csv.TryGetField<int>("productId", out var id)) continue;
            if (!csv.TryGetField<string>("productName", out var name) || string.IsNullOrWhiteSpace(name)) continue;
            dict[id] = name.Trim();
        }

        return dict.ToFrozenDictionary();
    }
}
