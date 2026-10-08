using System.ComponentModel;
using System.Globalization;
using ModelContextProtocol.Server;
using TelegramGateway.Api.Contracts;
using TelegramGateway.Api.Services.Delivery;

namespace TelegramGateway.Api.Mcp;

[McpServerToolType]
public sealed class GatewayTools(IServiceScopeFactory scopes)
{
    [McpServerTool(Name = "send_telegram_message", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = true)]
    [Description("Queue a notification to the configured private chat. Use a stable idempotency key to avoid duplicate submissions. Acceptance is not proof of Telegram delivery.")]
    public async Task<SubmitResult> SendMessageAsync(string text, string idempotencyKey, string format = "plain",
        bool disableNotification = false, CancellationToken cancellationToken = default)
    {
        using var scope = scopes.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<DeliveryService>()
            .SubmitMessageAsync(new(text, format, disableNotification, idempotencyKey), cancellationToken);
    }

    [McpServerTool(Name = "send_technology_digest", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Queue an already researched digest; does not research news. Date uses yyyy-MM-dd. Poll get_digest_delivery_status to confirm Delivered.")]
    public async Task<SubmitResult> SendDigestAsync(string title, string date, string content, string idempotencyKey,
        string language = "uk", string format = "markdown", bool disableNotification = false, CancellationToken cancellationToken = default)
    {
        if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            return new(SubmitStatus.Invalid, Error: "Date must use yyyy-MM-dd.");
        using var scope = scopes.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<DeliveryService>()
            .SubmitDigestAsync(new(title, parsed, language, content, format, idempotencyKey, disableNotification), cancellationToken);
    }

    [McpServerTool(Name = "get_digest_delivery_status", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Read delivery metadata for a digest date (yyyy-MM-dd). Delivered confirms recorded Telegram message IDs; RequiresReview needs chat inspection.")]
    public async Task<DeliveryResponse?> GetStatusAsync(string date, CancellationToken cancellationToken = default)
    {
        if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)) return null;
        using var scope = scopes.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<DeliveryService>().GetDigestAsync(parsed, cancellationToken);
    }
}
