using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using TradeEnrichment.Infrastructure.Parsers;
using Xunit;

namespace TradeEnrichment.Tests;

public sealed class CsvTradeParserTests
{
    private static Stream ToStream(string content) =>
        new MemoryStream(Encoding.UTF8.GetBytes(content));

    [Fact]
    public async Task ParseAsync_ValidCsv_ReturnsTrades()
    {
        var parser = new CsvTradeParser(NullLogger<CsvTradeParser>.Instance);
        var csv = "date,productId,currency,price\n20160101,1,EUR,10.50\n20160102,2,GBP,20.00";

        var trades = await CollectAsync(parser.ParseAsync(ToStream(csv)));

        trades.Should().HaveCount(2);
        trades[0].Date.Should().Be("20160101");
        trades[0].ProductId.Should().Be(1);
        trades[0].Currency.Should().Be("EUR");
        trades[0].Price.Should().Be(10.50m);
    }

    [Fact]
    public async Task ParseAsync_EmptyBody_ReturnsNoTrades()
    {
        var parser = new CsvTradeParser(NullLogger<CsvTradeParser>.Instance);
        var trades = await CollectAsync(parser.ParseAsync(ToStream("date,productId,currency,price\n")));
        trades.Should().BeEmpty();
    }

    [Fact]
    public async Task ParseAsync_NonIntegerProductId_SkipsRow()
    {
        var parser = new CsvTradeParser(NullLogger<CsvTradeParser>.Instance);
        var csv = "date,productId,currency,price\n20160101,ABC,EUR,10\n20160101,2,EUR,20";

        var trades = await CollectAsync(parser.ParseAsync(ToStream(csv)));

        // Only the valid row with integer productId=2 should be returned.
        trades.Should().HaveCount(1);
        trades[0].ProductId.Should().Be(2);
    }

    private static async Task<List<T>> CollectAsync<T>(IAsyncEnumerable<T> source)
    {
        var list = new List<T>();
        await foreach (var item in source) list.Add(item);
        return list;
    }
}

public sealed class JsonTradeParserTests
{
    private static Stream ToStream(string content) =>
        new MemoryStream(Encoding.UTF8.GetBytes(content));

    [Fact]
    public async Task ParseAsync_ValidJson_ReturnsTrades()
    {
        var parser = new JsonTradeParser(NullLogger<JsonTradeParser>.Instance);
        var json = """[{"date":"20160101","productId":1,"currency":"EUR","price":10.5}]""";

        var trades = new List<TradeEnrichment.Domain.Entities.Trade>();
        await foreach (var t in parser.ParseAsync(ToStream(json)))
            trades.Add(t);

        trades.Should().HaveCount(1);
        trades[0].ProductId.Should().Be(1);
        trades[0].Price.Should().Be(10.5m);
    }

    [Fact]
    public async Task ParseAsync_MalformedJson_ReturnsEmpty()
    {
        var parser = new JsonTradeParser(NullLogger<JsonTradeParser>.Instance);

        var trades = new List<TradeEnrichment.Domain.Entities.Trade>();
        await foreach (var t in parser.ParseAsync(ToStream("not json")))
            trades.Add(t);

        trades.Should().BeEmpty();
    }
}
