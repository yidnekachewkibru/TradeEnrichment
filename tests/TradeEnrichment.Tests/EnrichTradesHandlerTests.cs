using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TradeEnrichment.Application.Commands;
using TradeEnrichment.Application.Interfaces;
using TradeEnrichment.Application.Handlers;
using TradeEnrichment.Domain.Entities;
using TradeEnrichment.Infrastructure.Parsers;
using Xunit;

namespace TradeEnrichment.Tests;

public sealed class EnrichTradesHandlerTests
{

    private static EnrichTradesHandler BuildHandler(
        IProductRepository? repo = null,
        ITradeParser? parser = null)
    {
        parser ??= new CsvTradeParser(NullLogger<CsvTradeParser>.Instance);
        repo ??= MockRepo(new Dictionary<int, string> { [1] = "Treasury Bills Domestic", [2] = "Corporate Bonds Domestic" });

        return new EnrichTradesHandler(
            new[] { parser },
            repo,
            NullLogger<EnrichTradesHandler>.Instance);
    }

    private static IProductRepository MockRepo(Dictionary<int, string> map)
    {
        var mock = new Mock<IProductRepository>();
        mock.Setup(r => r.GetProductName(It.IsAny<int>()))
            .Returns<int>(id => map.TryGetValue(id, out var name) ? name : null);
        return mock.Object;
    }

    private static Stream CsvStream(string content) =>
        new MemoryStream(Encoding.UTF8.GetBytes(content));


    [Fact]
    public async Task Handle_ValidCsv_ReturnsEnrichedTrades()
    {
        var handler = BuildHandler();
        var csv = "date,productId,currency,price\n20160101,1,EUR,10\n20160101,2,EUR,20.1";

        var result = await handler.Handle(
            new EnrichTradesCommand(CsvStream(csv), "text/csv"),
            CancellationToken.None);

        var list = await result.ToListAsync();

        list.Should().HaveCount(2);
        list[0].ProductName.Should().Be("Treasury Bills Domestic");
        list[0].Date.Should().Be("20160101");
        list[0].Currency.Should().Be("EUR");
        list[0].Price.Should().Be(10m);
        list[1].ProductName.Should().Be("Corporate Bonds Domestic");
    }

    [Fact]
    public async Task Handle_UnknownProductId_UsesMissingProductName()
    {
        var handler = BuildHandler(MockRepo(new())); // empty repo
        var csv = "date,productId,currency,price\n20160101,99,EUR,35.34";

        var result = await handler.Handle(
            new EnrichTradesCommand(CsvStream(csv), "text/csv"),
            CancellationToken.None);

        var list = await result.ToListAsync();

        list.Should().HaveCount(1);
        list[0].ProductName.Should().Be("Missing Product Name");
    }

    [Fact]
    public async Task Handle_InvalidDate_RowIsDiscarded()
    {
        var handler = BuildHandler();
        var csv = "date,productId,currency,price\nINVALID,1,EUR,10\n20160101,1,EUR,10";

        var result = await handler.Handle(
            new EnrichTradesCommand(CsvStream(csv), "text/csv"),
            CancellationToken.None);

        var list = await result.ToListAsync();

        // Only the valid row should appear.
        list.Should().HaveCount(1);
        list[0].Date.Should().Be("20160101");
    }

    [Fact]
    public async Task Handle_EmptyCsv_ReturnsEmpty()
    {
        var handler = BuildHandler();
        var csv = "date,productId,currency,price\n";

        var result = await handler.Handle(
            new EnrichTradesCommand(CsvStream(csv), "text/csv"),
            CancellationToken.None);

        var list = await result.ToListAsync();
        list.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_UnsupportedContentType_ThrowsNotSupportedException()
    {
        var handler = BuildHandler();

        var act = async () => await handler.Handle(
            new EnrichTradesCommand(CsvStream(""), "application/pdf"),
            CancellationToken.None);

        await act.Should().ThrowAsync<NotSupportedException>();
    }

    [Fact]
    public async Task Handle_MultipleValidRows_AllEnriched()
    {
        var repo = MockRepo(new() { [1] = "A", [2] = "B", [3] = "C" });
        var handler = BuildHandler(repo);

        var lines = string.Join("\n", Enumerable.Range(1, 3)
            .Select(i => $"2016010{i},{i},EUR,{i * 10}"));
        var csv = "date,productId,currency,price\n" + lines;

        var result = await handler.Handle(
            new EnrichTradesCommand(CsvStream(csv), "text/csv"),
            CancellationToken.None);

        var list = await result.ToListAsync();
        list.Should().HaveCount(3);
        list.Select(t => t.ProductName).Should().BeEquivalentTo("A", "B", "C");
    }

    [Fact]
    public async Task Handle_MixedValidAndInvalidDates_OnlyValidReturned()
    {
        var handler = BuildHandler();
        var csv = """
            date,productId,currency,price
            20160101,1,EUR,10
            not-a-date,1,EUR,20
            20160103,1,EUR,30
            """;

        var result = await handler.Handle(
            new EnrichTradesCommand(CsvStream(csv), "text/csv"),
            CancellationToken.None);

        var list = await result.ToListAsync();
        list.Should().HaveCount(2);
        list.Select(t => t.Date).Should().ContainInOrder("20160101", "20160103");
    }
}

file static class AsyncEnumerableExtensions
{
    public static async Task<List<T>> ToListAsync<T>(this IAsyncEnumerable<T> source)
    {
        var list = new List<T>();
        await foreach (var item in source) list.Add(item);
        return list;
    }
}
