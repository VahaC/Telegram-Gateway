using TelegramGateway.Api.Contracts;

namespace TelegramGateway.Api.Services.Delivery;

public sealed record SubmitResult(SubmitStatus Status, DeliveryResponse? Delivery = null, string? Error = null);
