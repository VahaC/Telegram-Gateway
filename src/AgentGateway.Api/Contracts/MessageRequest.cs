using System.ComponentModel.DataAnnotations;

namespace AgentGateway.Api.Contracts;

public sealed record MessageRequest(
    [Required, MaxLength(100000)] string Text,
    string Format = "plain",
    bool DisableNotification = false,
    [MaxLength(128)] string? IdempotencyKey = null);
