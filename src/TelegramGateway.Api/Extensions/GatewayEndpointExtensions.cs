using System.Globalization;
using Microsoft.EntityFrameworkCore;
using TelegramGateway.Api.Contracts;
using TelegramGateway.Api.Http;
using TelegramGateway.Api.Services.Delivery;
using TelegramGateway.Infrastructure.Persistence;

namespace TelegramGateway.Api.Extensions;

public static class GatewayEndpointExtensions
{
    public static void MapGatewayEndpoints(this WebApplication application)
    {
        application.MapGet("/api/health", () => Results.Ok(new { status = "alive" })).WithName("Health").DisableRateLimiting();
        application.MapGet("/api/ready", async (ApplicationDbContext database, CancellationToken cancellationToken) =>
        {
            try
            {
                // A table read verifies migrations and local storage, without calling Telegram or returning content.
                await database.Deliveries.AsNoTracking().Select(row => row.Id).Take(1).ToListAsync(cancellationToken);
                return Results.Ok(new { status = "ready" });
            }
            catch (Exception) { return ApiErrors.Error(503, "Local storage is unavailable."); }
        }).WithName("Readiness").DisableRateLimiting();
        application.MapPost("/api/messages", async (MessageRequest request, DeliveryService service, CancellationToken cancellationToken)
            => MapSubmit(await service.SubmitMessageAsync(request, cancellationToken)))
            .WithName("SendMessage").WithDescription("Queue a message. Supply X-Api-Key; poll Location until Delivered.")
            .Produces<DeliveryResponse>(202).Produces<DeliveryResponse>(200).Produces<ApiErrorResponse>(400).Produces<ApiErrorResponse>(409);
        application.MapPost("/api/digests", async (DigestRequest request, DeliveryService service, CancellationToken cancellationToken)
            => MapSubmit(await service.SubmitDigestAsync(request, cancellationToken)))
            .WithName("SendDigest").WithDescription("Queue one digest per date, with durable idempotency and partial recovery. Supply X-Api-Key.")
            .Produces<DeliveryResponse>(202).Produces<DeliveryResponse>(200).Produces<ApiErrorResponse>(400).Produces<ApiErrorResponse>(409);
        application.MapGet("/api/digests/{date}", async (string date, DeliveryService service, CancellationToken cancellationToken) =>
        {
            if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
                return ApiErrors.BadRequest("Date must use yyyy-MM-dd.");
            return await service.GetDigestAsync(parsed, cancellationToken) is { } delivery ? Results.Ok(delivery) : Results.NotFound();
        }).WithName("DigestStatus").Produces<DeliveryResponse>();
        application.MapGet("/api/deliveries/{identifier:guid}", async (Guid identifier, DeliveryService service, CancellationToken cancellationToken)
            => await service.GetAsync(identifier, cancellationToken) is { } delivery ? Results.Ok(delivery) : Results.NotFound())
            .WithName("DeliveryStatus").Produces<DeliveryResponse>();
        application.MapOpenApi("/openapi/{documentName}.json");
    }

    private static IResult MapSubmit(SubmitResult result) => result.Status switch
    {
        SubmitStatus.Accepted => Results.Accepted($"/api/deliveries/{result.Delivery!.Id}", result.Delivery),
        SubmitStatus.Existing => Results.Ok(result.Delivery),
        SubmitStatus.Invalid => ApiErrors.BadRequest(result.Error!),
        _ => ApiErrors.Conflict(result.Error!)
    };
}
