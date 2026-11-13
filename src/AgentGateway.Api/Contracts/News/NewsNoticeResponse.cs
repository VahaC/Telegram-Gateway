using AgentGateway.Core.Enums;

namespace AgentGateway.Api.Contracts.News;

/// <summary>The one-line Telegram notice. Its delivery keeps the ADR 0001 semantics of every Telegram message.</summary>
public sealed record NewsNoticeResponse(Guid DeliveryId, int Revision, DeliveryStatus Status, bool RequiresReview);
