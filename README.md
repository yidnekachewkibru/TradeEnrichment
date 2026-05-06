# Trade Enrichment Service

A microservice that enriches trade records by resolving `productId` → `productName` from a static catalogue, with full validation, streaming I/O, and async processing.

---

## Table of Contents

1. [How to Run](#how-to-run)
2. [API Usage](#api-usage)
3. [Architecture & Design Decisions](#architecture--design-decisions)
4. [Performance Characteristics](#performance-characteristics)
5. [Limitations](#limitations)
6. [Ideas for Improvement](#ideas-for-improvement)

---

## How to Run

### Prerequisites

| Tool | Version |
|------|---------|
| [.NET SDK](https://dotnet.microsoft.com/download) | 8.0+ |
| Git | any |

### Quick Start

```bash
# 1. Clone the repository
git clone https://github.com/yidnekachewkibru/TradeEnrichment.git && cd TradeEnrichment

# 2. Restore NuGet packages
dotnet restore

# 3. Run the API
dotnet run --project src/TradeEnrichment.API
```

Once running, the service will output something like:

```
info: Microsoft.Hosting.Lifetime[14]
      Now listening on: https://localhost:62180
info: Microsoft.Hosting.Lifetime[14]
      Now listening on: http://localhost:62181
info: Microsoft.Hosting.Lifetime[0]
      Application started. Press Ctrl+C to shut down.
```

> **Note:** The port is assigned dynamically in Development mode. Use the port shown in your console output.  
> To pin a port, set it in `appsettings.json`:
> ```json
> "Kestrel": {
>   "Endpoints": {
>     "Http": { "Url": "http://localhost:8080" }
>   }
> }
> ```

### Run Tests

```bash
dotnet test
```

---

## API Usage

### Endpoint

```
POST /api/v1/enrich
```

The service responds in the **same format** as the request (content negotiation via `Content-Type`).  
You may override the response format with the `Accept` header.

> Replace `62181` in the examples below with the port shown in your console.

---

### CSV (default)

```bash
curl -X POST http://localhost:62181/api/v1/enrich \
     -H "Content-Type: text/csv" \
     --data "date,productId,currency,price
20160101,1,EUR,10
20160101,2,EUR,20.1
20160101,3,EUR,30.34
20160101,99,EUR,35.34"
```

**Response (`text/csv`):**
```
date,productName,currency,price
20160101,Treasury Bills Domestic,EUR,10
20160101,Corporate Bonds Domestic,EUR,20.1
20160101,REPO Domestic,EUR,30.34
20160101,Missing Product Name,EUR,35.34
```

**Upload from file:**
```bash
curl -X POST http://localhost:62181/api/v1/enrich \
     -H "Content-Type: text/csv" \
     --data-binary @"src/TradeEnrichment.API/data/trade.csv"
```

---

### JSON

```bash
curl -X POST http://localhost:62181/api/v1/enrich \
     -H "Content-Type: application/json" \
     -d "[{\"date\":\"20160101\",\"productId\":1,\"currency\":\"EUR\",\"price\":10}]"
```

**Response (`application/json`):**
```json
[{"date":"20160101","productName":"Treasury Bills Domestic","currency":"EUR","price":10}]
```

---

### XML

```bash
curl -X POST http://localhost:62181/api/v1/enrich \
     -H "Content-Type: application/xml" \
     -d "<trades><trade><date>20160101</date><productId>1</productId><currency>EUR</currency><price>10</price></trade></trades>"
```

---

### Cross-format: send CSV, receive JSON

```bash
curl -X POST http://localhost:62181/api/v1/enrich \
     -H "Content-Type: text/csv" \
     -H "Accept: application/json" \
     --data "date,productId,currency,price
20160101,1,EUR,10"
```

---

### PowerShell (Windows alternative to curl)

```powershell
Invoke-RestMethod -Method Post `
  -Uri "http://localhost:62181/api/v1/enrich" `
  -ContentType "text/csv" `
  -Body "date,productId,currency,price`n20160101,1,EUR,10`n20160101,2,EUR,20.1"
```

---

### Swagger UI (development only)

```
http://localhost:62181/swagger
```

---

## Architecture & Design Decisions

### Domain-Driven Design with MediatR

The project follows a layered DDD structure:

```
TradeEnrichment.Domain          Pure domain entities (Trade, EnrichedTrade)
TradeEnrichment.Application     Use-case layer: MediatR command + handler, interfaces
TradeEnrichment.Infrastructure  Concrete I/O: parsers, serializers, product repository
TradeEnrichment.API             ASP.NET controllers, middleware, DI composition root
TradeEnrichment.Tests           Unit + integration tests
```

**Why MediatR?**  
The controller is kept to a single responsibility (HTTP plumbing); all business logic lives in `EnrichTradesHandler`. Adding a new use-case means adding a new command/handler pair without touching existing code.

---

### Async Pipeline with `System.Threading.Channels`

```
HTTP body stream
      │
      ▼
  [ITradeParser]  ── producer ──►  Channel<Trade> (bounded, 4096)
                                          │
                                          ▼ consumer
                                   [EnrichTradesHandler]
                                          │
                                          ▼
                               IAsyncEnumerable<EnrichedTrade>
                                          │
                                          ▼
                                  [ITradeSerializer]
                                          │
                                          ▼
                                   HTTP response stream
```

**Key properties:**

- **Memory-bounded**: the `BoundedChannel` with `FullMode = Wait` means the parser pauses when the consumer is busy. Only ~4 096 rows are ever in flight simultaneously, regardless of file size.
- **Concurrent I/O + CPU**: the parser task runs on the thread pool while the main async iterator processes enriched rows — both work at the same time.
- **Backpressure from client**: the serializer writes directly to the HTTP response stream; ASP.NET's flow control propagates back-pressure all the way to the parser.
- **No buffering of the full result**: responses start streaming immediately.

---

### Product Repository — `FrozenDictionary`

`FrozenDictionary<int, string>` (introduced in .NET 8) is:

- **Immutable** — built once at startup, never mutated → inherently thread-safe with zero locking overhead.
- **~20–30 % faster lookups** than a regular `Dictionary` due to its optimised hash algorithm.
- At 100 k products × ~50 bytes/entry ≈ **5 MB** resident — negligible.

---

### Multi-format Support

| Content-Type     | Parser              | Serializer            |
|------------------|---------------------|-----------------------|
| `text/csv`       | `CsvTradeParser`    | `CsvTradeSerializer`  |
| `application/json` | `JsonTradeParser` | `JsonTradeSerializer` |
| `application/xml`  | `XmlTradeParser`  | *(falls back to CSV)* |

All parsers implement `ITradeParser` and are registered as `IEnumerable<ITradeParser>` in DI. The handler resolves the correct one at request time by matching `Content-Type`. Adding a new format is a single file + one DI registration.

---

## Performance Characteristics

| Scenario | Expected behaviour |
|----------|-------------------|
| 1 M rows × 4 columns | Streams without OOM; throughput ≈ disk/network I/O bound |
| 100 k products in catalogue | ~5 MB RAM; O(1) lookup per row |
| Concurrent requests | Each request gets its own Channel; `IProductRepository` singleton shared safely |
| Client disconnects mid-stream | `CancellationToken` propagated through every `await foreach`; resources cleaned up immediately |

---

## Limitations

1. **Product catalogue is static** — changes require a service restart; a hot-reload mechanism would be needed for live updates.
2. **XML serializer not implemented** — XML input is supported but responses always fall back to CSV or JSON.
3. **No authentication or rate limiting** — not suitable for public exposure without API key middleware or a gateway.

---

## Ideas for Improvement

| Area | Idea |
|------|------|
| **Fixed port** | Set `ASPNETCORE_URLS=http://+:8080` or configure Kestrel endpoints in `appsettings.json` |
| **Hot reload** | Watch `product.csv` for changes and rebuild the `FrozenDictionary` atomically via `Interlocked.Exchange` |
| **Database backend** | Implement `IProductRepository` against PostgreSQL or Redis for distributed deployments |
| **Auth** | Add JWT / API key middleware and per-client rate limiting |
| **CI/CD** | GitHub Actions pipeline: `dotnet test`, coverage gate, Docker build & push |