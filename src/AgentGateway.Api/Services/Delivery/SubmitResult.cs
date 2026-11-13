using AgentGateway.Api.Contracts;

namespace AgentGateway.Api.Services.Delivery;

public sealed record SubmitResult(SubmitStatus Status, DeliveryResponse? Delivery = null, string? Error = null);
