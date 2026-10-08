using System.ComponentModel.DataAnnotations;

namespace TelegramGateway.Api.Contracts;

public sealed record DigestRequest(
    [Required, MaxLength(200)] string Title,
    DateOnly Date,
    [Required, MaxLength(16)] string Language,
    [Required, MaxLength(100000)] string Content,
    string Format = "markdown",
    [MaxLength(128)] string? IdempotencyKey = null,
    bool DisableNotification = false);
