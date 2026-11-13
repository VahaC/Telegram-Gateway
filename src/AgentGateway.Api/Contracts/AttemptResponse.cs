using AgentGateway.Core.Enums;

namespace AgentGateway.Api.Contracts;

public sealed record AttemptResponse(
    int Part, AttemptStatus Status, DateTime CreatedUtc, DateTime? CompletedUtc,
    string? ErrorCode, long? TelegramMessageId);
