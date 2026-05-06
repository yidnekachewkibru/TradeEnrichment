using MediatR;
using Microsoft.AspNetCore.Mvc;
using TradeEnrichment.Application.Commands;
using TradeEnrichment.Application.Interfaces;

namespace TradeEnrichment.API.Controllers;

[ApiController]
[Route("api/v1")]
public sealed class TradeController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly IEnumerable<ITradeSerializer> _serializers;
    private readonly ILogger<TradeController> _logger;

    public TradeController(
        IMediator mediator,
        IEnumerable<ITradeSerializer> serializers,
        ILogger<TradeController> logger)
    {
        _mediator = mediator;
        _serializers = serializers;
        _logger = logger;
    }

    [HttpPost("enrich")]
    [DisableRequestSizeLimit]
    [RequestFormLimits(MultipartBodyLengthLimit = long.MaxValue, ValueLengthLimit = int.MaxValue)]
    public async Task EnrichAsync(CancellationToken cancellationToken)
    {
        var requestContentType = Request.ContentType ?? "text/csv";

        // Determine response format: honour Accept header, fall back to request type.
        var responseContentType = NegotiateResponseType(requestContentType);
        var serializer = _serializers.FirstOrDefault(s =>
            s.ContentType.Equals(responseContentType, StringComparison.OrdinalIgnoreCase));

        if (serializer is null)
        {
            Response.StatusCode = StatusCodes.Status415UnsupportedMediaType;
            return;
        }

        IAsyncEnumerable<Domain.Entities.EnrichedTrade> enriched;
        try
        {
            enriched = await _mediator.Send(
    new EnrichTradesCommand(Request.Body, requestContentType),
    cancellationToken);
        }
        catch (NotSupportedException ex)
        {
            _logger.LogWarning(ex, "Unsupported content-type: {ContentType}", requestContentType);
            Response.StatusCode = StatusCodes.Status415UnsupportedMediaType;
            return;
        }

        Response.StatusCode = StatusCodes.Status200OK;
        Response.ContentType = responseContentType;

        // Stream the response directly — no buffering of the full result set.
        await serializer.SerializeAsync(enriched, Response.Body, cancellationToken);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private string NegotiateResponseType(string requestContentType)
    {
        var accept = Request.Headers.Accept.FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(accept))
        {
            foreach (var token in accept.Split(','))
            {
                var mediaType = token.Split(';')[0].Trim();
                if (_serializers.Any(s => s.ContentType.Equals(mediaType, StringComparison.OrdinalIgnoreCase)))
                    return mediaType;
            }
        }

        // Default: respond in the same format as the request.
        var reqMedia = requestContentType.Split(';')[0].Trim().ToLowerInvariant();
        return _serializers.Any(s => s.ContentType.Equals(reqMedia, StringComparison.OrdinalIgnoreCase))
            ? reqMedia
            : "text/csv";
    }
}
