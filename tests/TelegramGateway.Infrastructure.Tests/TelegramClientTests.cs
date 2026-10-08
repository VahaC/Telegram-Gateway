using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using Microsoft.EntityFrameworkCore;
using TelegramGateway.Api.Contracts;
using TelegramGateway.Core.Enums;
using TelegramGateway.TestInfrastructure.Http;

namespace TelegramGateway.Infrastructure.Tests;

public sealed class TelegramClientTests
{
    [Fact]
    public async Task Send_name_resolution_failure_can_retry_before_sending()
    {
        //Arrange
        await using var factory = new GatewayApiFactory();
        factory.TelegramHttp.ResponseFactory = number => number == 1
            ? throw new HttpRequestException(HttpRequestError.NameResolutionError, "private-token-url")
            : FakeTelegramHandler.Json(HttpStatusCode.OK, "{\"ok\":true,\"result\":{\"message_id\":5}}");
        await factory.SendAsync(HttpMethod.Post, "/api/messages", new MessageRequest("Connection test"));
        //Act
        await factory.ProcessOnceAsync();
        //Assert
        Assert.Equal(2, factory.TelegramHttp.Requests.Count);
        await factory.WithDbAsync(async database => Assert.Equal(DeliveryStatus.Delivered,
            (await database.Deliveries.SingleAsync(TestContext.Current.CancellationToken)).Status));
    }

    [Fact]
    public async Task Send_connection_reset_has_uncertain_outcome_and_is_not_retried()
    {
        //Arrange
        await using var factory = new GatewayApiFactory();
        factory.TelegramHttp.ResponseFactory = _ => throw new HttpRequestException(HttpRequestError.ConnectionError,
            "private-token-url", new SocketException((int)SocketError.ConnectionReset));
        await factory.SendAsync(HttpMethod.Post, "/api/messages", new MessageRequest("Reset test"));
        //Act
        await factory.ProcessOnceAsync();
        //Assert
        Assert.Single(factory.TelegramHttp.Requests);
        await factory.WithDbAsync(async database => Assert.True(
            (await database.Deliveries.SingleAsync(TestContext.Current.CancellationToken)).RequiresReview));
    }

    [Theory]
    [InlineData("not JSON")]
    [InlineData("[]")]
    [InlineData("{\"ok\":true,\"result\":{}}")]
    public async Task Send_malformed_success_is_ambiguous(string body)
    {
        //Arrange
        await using var factory = new GatewayApiFactory();
        factory.TelegramHttp.ResponseFactory = _ => FakeTelegramHandler.Json(HttpStatusCode.OK, body);
        await factory.SendAsync(HttpMethod.Post, "/api/messages", new MessageRequest("Malformed test"));
        //Act
        await factory.ProcessOnceAsync();
        //Assert
        Assert.Single(factory.TelegramHttp.Requests);
        await factory.WithDbAsync(async database => Assert.True(
            (await database.Deliveries.SingleAsync(TestContext.Current.CancellationToken)).RequiresReview));
    }

    [Fact]
    public async Task Send_non_json_server_error_uses_bounded_retries()
    {
        //Arrange
        await using var factory = new GatewayApiFactory();
        factory.TelegramHttp.ResponseFactory = _ => FakeTelegramHandler.Json(HttpStatusCode.BadGateway, "<html>Upstream error</html>");
        await factory.SendAsync(HttpMethod.Post, "/api/messages", new MessageRequest("Non-JSON failure test"));
        //Act
        await factory.ProcessOnceAsync();
        //Assert
        Assert.Equal(3, factory.TelegramHttp.Requests.Count);
        await factory.WithDbAsync(async database => Assert.Equal("telegram_server_error",
            (await database.Deliveries.SingleAsync(TestContext.Current.CancellationToken)).ErrorCode));
    }
}
