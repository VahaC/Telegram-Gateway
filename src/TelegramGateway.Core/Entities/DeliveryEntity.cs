using TelegramGateway.Core.Enums;

namespace TelegramGateway.Core.Entities;

public class DeliveryEntity : AuditableEntity
{
    public string IdempotencyKey { get; set; } = "";
    public string PayloadHash { get; set; } = "";
    public DateOnly? DigestDate { get; set; }
    public DeliveryStatus Status { get; set; }
    public bool DisableNotification { get; set; }
    public bool RequiresReview { get; set; }
    public string? ErrorCode { get; set; }
    public DateTime UpdatedUtc { get; set; }
    public DateTime? RetryNotBeforeUtc { get; set; }
    public List<MessagePartEntity> Parts { get; set; } = [];
}
