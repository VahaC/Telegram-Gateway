using TelegramGateway.Core.Enums;

namespace TelegramGateway.Core.Entities;

public class DeliveryAttemptEntity : AuditableEntity
{
    public Guid MessagePartId { get; set; }
    public AttemptStatus Status { get; set; }
    public DateTime? CompletedUtc { get; set; }
    public string? ErrorCode { get; set; }
    public long? TelegramMessageId { get; set; }
}
