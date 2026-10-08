using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentValidation;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TelegramGateway.Api.Contracts;
using TelegramGateway.Api.Mapping;
using TelegramGateway.Core.Abstractions;
using TelegramGateway.Core.Entities;
using TelegramGateway.Core.Enums;
using TelegramGateway.Core.Options;
using TelegramGateway.Infrastructure.Persistence;

namespace TelegramGateway.Api.Services.Delivery;

public sealed class DeliveryService(
    ApplicationDbContext database, IMessageFormatter formatter, IDeliveryMapper mapper,
    IValidator<MessageRequest> messageValidator, IValidator<DigestRequest> digestValidator,
    IOptions<GatewayOptions> options, TimeProvider time)
{
    public async Task<SubmitResult> SubmitMessageAsync(MessageRequest request, CancellationToken cancellationToken)
    {
        var validation = await messageValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid) return new(SubmitStatus.Invalid, Error: string.Join(" ", validation.Errors.Select(error => error.ErrorMessage)));
        var formatted = formatter.Format(request.Text, request.Format, options.Value.MessageLength, options.Value.MaxParts);
        if (!formatted.IsSuccess) return new(SubmitStatus.Invalid, Error: formatted.Error);
        return await SubmitAsync(request.IdempotencyKey ?? $"message-{Guid.NewGuid():N}", null,
            Hash(request with { IdempotencyKey = null }), formatted.Parts, request.DisableNotification, cancellationToken);
    }

    public async Task<SubmitResult> SubmitDigestAsync(DigestRequest request, CancellationToken cancellationToken)
    {
        var validation = await digestValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid) return new(SubmitStatus.Invalid, Error: string.Join(" ", validation.Errors.Select(error => error.ErrorMessage)));
        var content = formatter.Format(request.Content, request.Format, options.Value.MessageLength, options.Value.MaxParts);
        if (!content.IsSuccess) return new(SubmitStatus.Invalid, Error: content.Error);
        var header = $"<b>{WebUtility.HtmlEncode(request.Title)}</b>\n{request.Date:yyyy-MM-dd}\n\n";
        var combined = formatter.Format(header + string.Concat(content.Parts), "html", options.Value.MessageLength, options.Value.MaxParts);
        if (!combined.IsSuccess) return new(SubmitStatus.Invalid, Error: combined.Error);
        return await SubmitAsync(request.IdempotencyKey ?? $"digest-{request.Date:yyyy-MM-dd}", request.Date,
            Hash(request with { IdempotencyKey = null }), combined.Parts, request.DisableNotification, cancellationToken);
    }

    public async Task<DeliveryResponse?> GetAsync(Guid identifier, CancellationToken cancellationToken)
    {
        var delivery = await Query().SingleOrDefaultAsync(row => row.Id == identifier, cancellationToken);
        return delivery is null ? null : mapper.ToResponse(delivery);
    }

    public async Task<DeliveryResponse?> GetDigestAsync(DateOnly date, CancellationToken cancellationToken)
    {
        var delivery = await Query().SingleOrDefaultAsync(row => row.DigestDate == date, cancellationToken);
        return delivery is null ? null : mapper.ToResponse(delivery);
    }

    private IQueryable<DeliveryEntity> Query() => database.Deliveries.AsNoTracking().Include(row => row.Parts).ThenInclude(part => part.Attempts);

    private async Task<SubmitResult> SubmitAsync(string key, DateOnly? date, string hash,
        IReadOnlyList<string> parts, bool disableNotification, CancellationToken cancellationToken)
    {
        var existing = await Query().SingleOrDefaultAsync(row => row.IdempotencyKey == key, cancellationToken);
        if (existing is not null) return await ExistingAsync(existing, hash, cancellationToken);
        var now = time.GetUtcNow().UtcDateTime;
        var delivery = new DeliveryEntity
        {
            IdempotencyKey = key, DigestDate = date, PayloadHash = hash, Status = DeliveryStatus.Pending,
            DisableNotification = disableNotification, CreatedUtc = now, UpdatedUtc = now,
            Parts = parts.Select((html, position) => new MessagePartEntity { Position = position, Html = html, CreatedUtc = now }).ToList()
        };
        database.Deliveries.Add(delivery);
        try { await database.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateException exception) when (exception.InnerException is SqliteException { SqliteErrorCode: 19 })
        {
            database.ChangeTracker.Clear();
            var concurrent = await Query().SingleOrDefaultAsync(row => row.IdempotencyKey == key, cancellationToken);
            return concurrent is null
                ? new(SubmitStatus.Conflict, Error: "A digest already exists for this date with another idempotency key.")
                : await ExistingAsync(concurrent, hash, cancellationToken);
        }
        return new(SubmitStatus.Accepted, mapper.ToResponse(delivery));
    }

    private async Task<SubmitResult> ExistingAsync(DeliveryEntity existing, string hash, CancellationToken cancellationToken)
    {
        if (existing.PayloadHash != hash) return new(SubmitStatus.Conflict, Error: "The idempotency key is already bound to a different payload.");
        if (existing.RequiresReview) return new(SubmitStatus.Conflict, Error: "Delivery has an uncertain Telegram outcome. Inspect the private chat before taking further action.");
        if (existing.Status is DeliveryStatus.Failed or DeliveryStatus.PartiallyDelivered)
        {
            await database.Deliveries.Where(row => row.Id == existing.Id && !row.RequiresReview
                && (row.Status == DeliveryStatus.Failed || row.Status == DeliveryStatus.PartiallyDelivered))
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.Status, DeliveryStatus.Pending)
                    .SetProperty(row => row.ErrorCode, (string?)null).SetProperty(row => row.UpdatedUtc, time.GetUtcNow().UtcDateTime), cancellationToken);
            return new(SubmitStatus.Accepted, await GetAsync(existing.Id, cancellationToken));
        }
        return new(SubmitStatus.Existing, mapper.ToResponse(existing));
    }

    private static string Hash<T>(T value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value))));
}
