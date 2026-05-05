using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using TradeEnrichment.Application.Interfaces;
using Xunit;

namespace TradeEnrichment.Tests;

public sealed class TradeControllerIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public TradeControllerIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                // Remove the real ProductRepository registered in Program.cs.
                var descriptor = services.SingleOrDefault(
                    d => d.ServiceType == typeof(IProductRepository));
                if (descriptor is not null) services.Remove(descriptor);

                var mock = new Mock<IProductRepository>();
                mock.Setup(r => r.GetProductName(1)).Returns("Treasury Bills Domestic");
                mock.Setup(r => r.GetProductName(2)).Returns("Corporate Bonds Domestic");
                mock.Setup(r => r.GetProductName(It.IsNotIn(1, 2))).Returns((string?)null);
                services.AddSingleton(mock.Object);
            });
        });
    }

    [Fact]
    public async Task Post_Enrich_WithValidCsv_Returns200AndCsvBody()
    {
        var client = _factory.CreateClient();
        var csv = "date,productId,currency,price\n20160101,1,EUR,10\n20160101,2,EUR,20.1";

        var content = new StringContent(csv, Encoding.UTF8, "text/csv");
        var response = await client.PostAsync("/api/v1/enrich", content);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/csv");

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("date,productName,currency,price");
        body.Should().Contain("Treasury Bills Domestic");
        body.Should().Contain("Corporate Bonds Domestic");
    }

    [Fact]
    public async Task Post_Enrich_InvalidDate_RowOmittedFromResponse()
    {
        var client = _factory.CreateClient();
        var csv = "date,productId,currency,price\nBADDATE,1,EUR,10\n20160101,1,EUR,99.99";

        var content = new StringContent(csv, Encoding.UTF8, "text/csv");
        var response = await client.PostAsync("/api/v1/enrich", content);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("99.99");

        var lines = body.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        lines.Should().HaveCount(2); // header + 1 valid row
    }

    [Fact]
    public async Task Post_Enrich_MissingProductId_ReturnsMissingProductName()
    {
        var client = _factory.CreateClient();
        var csv = "date,productId,currency,price\n20160101,999,EUR,35.34";

        var content = new StringContent(csv, Encoding.UTF8, "text/csv");
        var response = await client.PostAsync("/api/v1/enrich", content);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("Missing Product Name");
    }

    [Fact]
    public async Task Post_Enrich_WithJsonContentType_Returns200AndJsonBody()
    {
        var client = _factory.CreateClient();
        var json = """[{"date":"20160101","productId":1,"currency":"EUR","price":10}]""";

        var content = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await client.PostAsync("/api/v1/enrich", content);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("Treasury Bills Domestic");
    }

    [Fact]
    public async Task Post_Enrich_UnsupportedContentType_Returns415()
    {
        var client = _factory.CreateClient();
        var content = new StringContent("irrelevant", Encoding.UTF8, "application/pdf");
        var response = await client.PostAsync("/api/v1/enrich", content);

        response.StatusCode.Should().Be(HttpStatusCode.UnsupportedMediaType);
    }
}
