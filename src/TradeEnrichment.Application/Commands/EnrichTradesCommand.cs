using MediatR;
using TradeEnrichment.Domain.Entities;

namespace TradeEnrichment.Application.Commands;

public sealed record EnrichTradesCommand(
    Stream InputStream,
    string ContentType
) : IRequest<IAsyncEnumerable<EnrichedTrade>>;