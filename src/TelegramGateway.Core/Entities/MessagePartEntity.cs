namespace TelegramGateway.Core.Entities;

public class MessagePartEntity : AuditableEntity
{
    public Guid DeliveryId { get; set; }
    public int Position { get; set; }
    public string Html { get; set; } = "";
    public long? TelegramMessageId { get; set; }
    public List<DeliveryAttemptEntity> Attempts { get; set; } = [];
}
