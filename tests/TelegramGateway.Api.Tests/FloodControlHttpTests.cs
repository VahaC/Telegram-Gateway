using System.Net;
using System.Net.Http.Json;
using TelegramGateway.Api.Contracts;
using TelegramGateway.Core.Enums;
using TelegramGateway.TestInfrastructure.Http;
using TelegramGateway.TestInfrastructure.Time;

namespace TelegramGateway.Api.Tests;

public sealed class FloodControlHttpTests
{
    [Fact]
    public async Task Restart_retry_after_persists_and_blocks_other_deliveries_until_due()
    {
        //Arrange
        var path = Path.Combine(Path.GetTempPath(), $"telegram-gateway-flood-{Guid.NewGuid():N}");
        var time = new MutableTimeProvider();
        await using var first = new GatewayApiFactory(path) { Time = time };
        first.Settings["Gateway:MaxRetryAfterSeconds"] = "1";
        first.TelegramHttp.ResponseFactory = _ => FakeTelegramHandler.Json(HttpStatusCode.TooManyRequests,
            "{\"ok\":false,\"error_code\":429,\"parameters\":{\"retry_after\":120}}");
        await first.SendAsync(HttpMethod.Post, "/api/messages", new MessageRequest("Rate limited", IdempotencyKey: "flood-first"));
        await first.ProcessOnceAsync();
        var submitted = await first.SendAsync(HttpMethod.Post, "/api/messages", new MessageRequest("Pending after flood", IdempotencyKey: "flood-second"));
        var accepted = await submitted.Content.ReadFromJsonAsync<DeliveryResponse>(GatewayApiFactory.JsonOptions, TestContext.Current.CancellationToken);
        await first.DisposeAsync();
        await using var restarted = new GatewayApiFactory(path) { Time = time };
        restarted.TelegramHttp.ArmSuccess();
        //Act
        var blocked = await restarted.ProcessOnceAsync();
        time.Advance(TimeSpan.FromSeconds(121));
        var processed = await restarted.ProcessOnceAsync();
        var status = await restarted.SendAsync(HttpMethod.Get, $"/api/deliveries/{accepted!.Id}");
        var metadata = await status.Content.ReadFromJsonAsync<DeliveryResponse>(GatewayApiFactory.JsonOptions, TestContext.Current.CancellationToken);
        //Assert
        Assert.False(blocked);
        Assert.True(processed);
        Assert.Single(restarted.TelegramHttp.Requests);
        Assert.Equal(DeliveryStatus.Delivered, metadata!.Status);
        await restarted.DisposeAsync();
        Directory.Delete(path, true);
    }
}
