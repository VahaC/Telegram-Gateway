using Microsoft.EntityFrameworkCore;
using AgentGateway.Core.Enums;
using AgentGateway.Infrastructure.Persistence;

namespace AgentGateway.Api.Services.News;

/// <summary>
/// An issue left in Publishing by a crash goes back to the queue. This is safe only because every WordPress call is an
/// upsert by key and revision (ADR 0003); Telegram deliveries are reconciled very differently (ADR 0001).
/// </summary>
public sealed class NewsPublicationReconciler(ApplicationDbContext database, TimeProvider time)
{
    public Task<int> ReconcileAsync(CancellationToken cancellationToken) => database.NewsIssues
        .Where(row => row.Status == NewsPublicationStatus.Publishing)
        .ExecuteUpdateAsync(update => update.SetProperty(row => row.Status, NewsPublicationStatus.Pending)
            .SetProperty(row => row.UpdatedUtc, time.GetUtcNow().UtcDateTime), cancellationToken);
}
