using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using AgentGateway.Api.Contracts;
using AgentGateway.Core.Entities;
using AgentGateway.Core.Enums;
using AgentGateway.TestInfrastructure.Http;

namespace AgentGateway.Api.Tests;

public sealed class PersistenceHttpTests
{
    [Fact]
    public async Task Restart_delivered_digest_remains_idempotent()
    {
        //Arrange
        await using var first = new GatewayApiFactory();
        first.TelegramHttp.ArmSuccess();
        var digest = new DigestRequest("Тест", new DateOnly(2026, 10, 8), "uk", "Україна", IdempotencyKey: "restart-test");
        await first.SendAsync(HttpMethod.Post, "/api/digests", digest);
        await first.ProcessOnceAsync();
        var databasePath = Path.Combine(Path.GetTempPath(), $"agent-gateway-restart-{Guid.NewGuid():N}");
        Directory.CreateDirectory(databasePath);
        await first.WithDbAsync(async database =>
        {
            await database.Database.ExecuteSqlRawAsync("PRAGMA wal_checkpoint(TRUNCATE);", TestContext.Current.CancellationToken);
        });
        File.Copy(Path.Combine(first.DataPath, "gateway.db"), Path.Combine(databasePath, "gateway.db"));
        await using var restarted = new GatewayApiFactory(databasePath);
        restarted.TelegramHttp.ArmSuccess();
        //Act
        var response = await restarted.SendAsync(HttpMethod.Post, "/api/digests", digest);
        var metadata = await response.Content.ReadFromJsonAsync<DeliveryResponse>(GatewayApiFactory.JsonOptions, TestContext.Current.CancellationToken);
        var ran = await restarted.ProcessOnceAsync();
        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(DeliveryStatus.Delivered, metadata!.Status);
        Assert.Single(metadata.TelegramMessageIds);
        Assert.False(ran);
        Assert.Empty(restarted.TelegramHttp.Requests);
        await restarted.DisposeAsync();
        Directory.Delete(databasePath, true);
    }

    [Fact]
    public async Task Upgrade_of_an_existing_ledger_adds_news_tables_and_keeps_every_delivery()
    {
        //Arrange
        var path = Path.Combine(Path.GetTempPath(), $"agent-gateway-upgrade-{Guid.NewGuid():N}");
        Guid delivery;
        try
        {
            await using (var before = new GatewayApiFactory(path))
            {
                var response = await before.SendAsync(HttpMethod.Post, "/api/messages", new MessageRequest("Before the rename", IdempotencyKey: "pre-upgrade"));
                delivery = (await response.Content.ReadFromJsonAsync<DeliveryResponse>(GatewayApiFactory.JsonOptions, TestContext.Current.CancellationToken))!.Id;
                // Put the database back into the shape of the TelegramGateway release: only the delivery migrations applied.
                await before.WithDbAsync(async database =>
                {
                    await database.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>()
                        .MigrateAsync("20261008142356_PersistTelegramFloodControl", TestContext.Current.CancellationToken);
                    await database.Database.ExecuteSqlRawAsync("PRAGMA wal_checkpoint(TRUNCATE);", TestContext.Current.CancellationToken);
                });
            }
            //Act
            await using var upgraded = new GatewayApiFactory(path);
            var status = await upgraded.SendAsync(HttpMethod.Get, $"/api/deliveries/{delivery}");
            var news = await upgraded.SendAsync(HttpMethod.Post, "/api/news/issues", AgentGateway.TestInfrastructure.News.NewsIssueDataFactory.Issue(new DateOnly(2026, 11, 14)));
            //Assert
            Assert.Equal(HttpStatusCode.OK, status.StatusCode);
            Assert.Equal("pre-upgrade", (await status.Content.ReadFromJsonAsync<DeliveryResponse>(GatewayApiFactory.JsonOptions, TestContext.Current.CancellationToken))!.IdempotencyKey);
            Assert.Equal(HttpStatusCode.Accepted, news.StatusCode);
            await upgraded.WithDbAsync(async database =>
            {
                Assert.Contains("20261008140453_InitialDeliveryLedger", await database.Database.GetAppliedMigrationsAsync(TestContext.Current.CancellationToken));
                Assert.Single(await database.Deliveries.ToListAsync(TestContext.Current.CancellationToken));
            });
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(path)) Directory.Delete(path, true);
        }
    }

    [Fact]
    public async Task Restart_pending_digest_is_resumed_and_inflight_attempt_requires_review()
    {
        //Arrange
        await using var first = new GatewayApiFactory();
        var digest = new DigestRequest("Тест", new DateOnly(2026, 10, 8), "uk", "Україна", IdempotencyKey: "interrupted-test");
        await first.SendAsync(HttpMethod.Post, "/api/digests", digest);
        await first.WithDbAsync(async database =>
        {
            var delivery = await database.Deliveries.Include(row => row.Parts).SingleAsync(TestContext.Current.CancellationToken);
            delivery.Status = DeliveryStatus.Sending;
            database.DeliveryAttempts.Add(new DeliveryAttemptEntity
            {
                MessagePartId = delivery.Parts[0].Id, Status = AttemptStatus.Started, CreatedUtc = delivery.CreatedUtc
            });
            await database.SaveChangesAsync(TestContext.Current.CancellationToken);
            await database.Database.ExecuteSqlRawAsync("PRAGMA wal_checkpoint(TRUNCATE);", TestContext.Current.CancellationToken);
        });
        var databasePath = Path.Combine(Path.GetTempPath(), $"agent-gateway-restart-{Guid.NewGuid():N}");
        Directory.CreateDirectory(databasePath);
        File.Copy(Path.Combine(first.DataPath, "gateway.db"), Path.Combine(databasePath, "gateway.db"));
        await using var restarted = new GatewayApiFactory(databasePath);
        //Act
        var response = await restarted.SendAsync(HttpMethod.Get, "/api/digests/2026-10-08");
        var metadata = await response.Content.ReadFromJsonAsync<DeliveryResponse>(GatewayApiFactory.JsonOptions, TestContext.Current.CancellationToken);
        var repeated = await restarted.SendAsync(HttpMethod.Post, "/api/digests", digest);
        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(metadata!.RequiresReview);
        Assert.Equal(DeliveryStatus.Failed, metadata.Status);
        Assert.Equal(AttemptStatus.Ambiguous, Assert.Single(metadata.Attempts).Status);
        Assert.Equal(HttpStatusCode.Conflict, repeated.StatusCode);
        Assert.Empty(restarted.TelegramHttp.Requests);
        await restarted.DisposeAsync();
        Directory.Delete(databasePath, true);
    }
}
