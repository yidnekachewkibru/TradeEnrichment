using TradeEnrichment.Application.Interfaces;
using TradeEnrichment.Infrastructure.Parsers;
using TradeEnrichment.Infrastructure.Repositories;
using TradeEnrichment.Infrastructure.Serializers;
using TradeEnrichment.Application.Handlers;
using TradeEnrichment.API.Middleware;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddConsole();

builder.Services.AddMediatR(cfg =>
    cfg.RegisterServicesFromAssemblyContaining<EnrichTradesHandler>());

builder.Services.AddSingleton<ITradeParser, CsvTradeParser>();
builder.Services.AddSingleton<ITradeParser, JsonTradeParser>();
builder.Services.AddSingleton<ITradeParser, XmlTradeParser>();

builder.Services.AddSingleton<ITradeSerializer, CsvTradeSerializer>();
builder.Services.AddSingleton<ITradeSerializer, JsonTradeSerializer>();

var productCsvPath = builder.Configuration["ProductCsvPath"]
    ?? Path.Combine(AppContext.BaseDirectory, "data", "product.csv");

builder.Services.AddSingleton<IProductRepository>(sp =>
    new ProductRepository(
        productCsvPath,
        sp.GetRequiredService<ILogger<ProductRepository>>()));

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "Trade Enrichment API", Version = "v1" });
});

builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(o =>
{
    o.MultipartBodyLengthLimit = long.MaxValue;
});
builder.WebHost.ConfigureKestrel(o =>
{
    o.Limits.MaxRequestBodySize = null; // unlimited
});

var app = builder.Build();

app.UseMiddleware<ErrorHandlingMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapControllers();

app.Run();

// Expose Program for integration tests
public partial class Program { }
