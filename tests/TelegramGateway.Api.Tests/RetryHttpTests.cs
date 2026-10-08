using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using TelegramGateway.Api.Contracts;
using TelegramGateway.Core.Enums;
using TelegramGateway.TestInfrastructure.Http;

namespace TelegramGateway.Api.Tests;

public sealed class RetryHttpTests
{
    [Fact]
    public async Task Send_rate_limited_response_honors_retry_after()
    {
        //Arrange
        await using var factory = new GatewayApiFactory();
        factory.TelegramHttp.ResponseFactory = number => number == 1
            ? FakeTelegramHandler.Json(HttpStatusCode.TooManyRequests, "{\"ok\":false,\"error_code\":429,\"parameters\":{\"retry_after\":1}}")
            : FakeTelegramHandler.Json(HttpStatusCode.OK, "{\"ok\":true,\"result\":{\"message_id\":7}}");
        await factory.SendAsync(HttpMethod.Post, "/api/messages", new MessageRequest("Retry test"));
        var watch = Stopwatch.StartNew();
        //Act
        await factory.ProcessOnceAsync();
        watch.Stop();
        //Assert
        Assert.True(watch.Elapsed >= TimeSpan.FromSeconds(1));
        Assert.Equal(2, factory.TelegramHttp.Requests.Count);
        await factory.WithDbAsync(async database =>
        {
            Assert.Equal(DeliveryStatus.Delivered, (await database.Deliveries.SingleAsync(TestContext.Current.CancellationToken)).Status);
            Assert.Equal(2, await database.DeliveryAttempts.CountAsync(TestContext.Current.CancellationToken));
        });
    }

    [Theory]
    [InlineData(400, 1)]
    [InlineData(401, 1)]
    [InlineData(403, 1)]
    [InlineData(500, 3)]
    [InlineData(503, 3)]
    public async Task Send_retries_only_transient_failures_with_bounded_attempts(int statusCode, int expectedAttempts)
    {
        //Arrange
        await using var factory = new GatewayApiFactory();
        factory.TelegramHttp.ResponseFactory = _ => FakeTelegramHandler.Json((HttpStatusCode)statusCode,
            $"{{\"ok\":false,\"error_code\":{statusCode},\"description\":\"secret-token-and-message\"}}");
        var response = await factory.SendAsync(HttpMethod.Post, "/api/messages", new MessageRequest("Retry test"));
        var accepted = await response.Content.ReadFromJsonAsync<DeliveryResponse>(GatewayApiFactory.JsonOptions, TestContext.Current.CancellationToken);
        //Act
        await factory.ProcessOnceAsync();
        var status = await factory.SendAsync(HttpMethod.Get, $"/api/deliveries/{accepted!.Id}");
        //Assert
        Assert.Equal(expectedAttempts, factory.TelegramHttp.Requests.Count);
        Assert.DoesNotContain("secret-token-and-message", await status.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        await factory.WithDbAsync(async database =>
        {
            Assert.Equal(DeliveryStatus.Failed, (await database.Deliveries.SingleAsync(TestContext.Current.CancellationToken)).Status);
            Assert.Equal(expectedAttempts, await database.DeliveryAttempts.CountAsync(TestContext.Current.CancellationToken));
        });
    }

    [Fact]
    public async Task Send_excessive_retry_after_stops_without_early_retry()
    {
        //Arrange
        await using var factory = new GatewayApiFactory();
        factory.TelegramHttp.ResponseFactory = _ => FakeTelegramHandler.Json(HttpStatusCode.TooManyRequests,
            "{\"ok\":false,\"error_code\":429,\"parameters\":{\"retry_after\":3601}}");
        await factory.SendAsync(HttpMethod.Post, "/api/messages", new MessageRequest("Retry test"));
        //Act
        await factory.ProcessOnceAsync();
        await factory.SendAsync(HttpMethod.Post, "/api/messages", new MessageRequest("Next delivery while flood-limited"));
        var secondRun = await factory.ProcessOnceAsync();
        //Assert
        Assert.Single(factory.TelegramHttp.Requests);
        Assert.False(secondRun);
        await factory.WithDbAsync(async database =>
        {
            var failed = await database.Deliveries.SingleAsync(row => row.Status == DeliveryStatus.Failed, TestContext.Current.CancellationToken);
            Assert.True(failed.RetryNotBeforeUtc > failed.UpdatedUtc);
            Assert.Equal(1, await database.Deliveries.CountAsync(row => row.Status == DeliveryStatus.Pending, TestContext.Current.CancellationToken));
        });
    }
}
