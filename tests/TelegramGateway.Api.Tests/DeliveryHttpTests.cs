using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using TelegramGateway.Api.Contracts;
using TelegramGateway.Core.Enums;
using TelegramGateway.TestInfrastructure.Http;

namespace TelegramGateway.Api.Tests;

public sealed class DeliveryHttpTests
{
    private const string BaseUrl = "/api/digests";
    private static DigestRequest Digest(string content = "# TOP-5\n\n1. Український тест — [джерело](https://example.com/?a=1&b=2)", string key = "tech-digest-2026-10-08")
        => new("Технологічний дайджест", new DateOnly(2026, 10, 8), "uk", content, IdempotencyKey: key);

    [Fact]
    public async Task Post_digest_queues_delivers_and_records_metadata()
    {
        //Arrange
        await using var factory = new GatewayApiFactory();
        factory.TelegramHttp.ArmSuccess();
        //Act
        var response = await factory.SendAsync(HttpMethod.Post, BaseUrl, Digest());
        var accepted = await response.Content.ReadFromJsonAsync<DeliveryResponse>(GatewayApiFactory.JsonOptions, TestContext.Current.CancellationToken);
        await factory.ProcessOnceAsync();
        var status = await factory.SendAsync(HttpMethod.Get, BaseUrl + "/2026-10-08");
        var delivered = await status.Content.ReadFromJsonAsync<DeliveryResponse>(GatewayApiFactory.JsonOptions, TestContext.Current.CancellationToken);
        //Assert
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal($"/api/deliveries/{accepted!.Id}", response.Headers.Location!.OriginalString);
        Assert.Equal(DeliveryStatus.Delivered, delivered!.Status);
        Assert.Single(delivered.TelegramMessageIds);
        Assert.Single(delivered.Attempts);
        Assert.Equal(AttemptStatus.Delivered, delivered.Attempts[0].Status);
        var sent = Assert.Single(factory.TelegramHttp.Requests);
        Assert.Equal("123456789", sent.ChatId);
        Assert.Equal("HTML", sent.ParseMode);
        Assert.Contains("<b>TOP-5</b>", sent.Text);
        Assert.Contains("href=\"https://example.com/?a=1&amp;b=2\"", sent.Text);
        Assert.DoesNotContain("Український тест", await status.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Contains("Z\"", await status.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        await factory.WithDbAsync(async database =>
        {
            Assert.Equal(DeliveryStatus.Delivered, (await database.Deliveries.SingleAsync(TestContext.Current.CancellationToken)).Status);
            Assert.Single(await database.DeliveryAttempts.ToListAsync(TestContext.Current.CancellationToken));
        });
    }

    [Theory]
    [InlineData("", "uk", "markdown")]
    [InlineData("text", "unknown language", "markdown")]
    [InlineData("text", "uk", "invalid")]
    [InlineData("<b>bad", "uk", "html")]
    public async Task Post_invalid_digest_is_not_persisted(string content, string language, string format)
    {
        //Arrange
        await using var factory = new GatewayApiFactory();
        //Act
        var response = await factory.SendAsync(HttpMethod.Post, BaseUrl, Digest(content) with { Language = language, Format = format });
        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await factory.WithDbAsync(async database => Assert.Empty(await database.Deliveries.ToListAsync(TestContext.Current.CancellationToken)));
    }

    [Fact]
    public async Task Post_same_key_is_idempotent_and_conflicting_payload_is_rejected()
    {
        //Arrange
        await using var factory = new GatewayApiFactory();
        factory.TelegramHttp.ArmSuccess();
        //Act
        var first = await factory.SendAsync(HttpMethod.Post, BaseUrl, Digest());
        await factory.ProcessOnceAsync();
        var repeated = await factory.SendAsync(HttpMethod.Post, BaseUrl, Digest());
        var conflict = await factory.SendAsync(HttpMethod.Post, BaseUrl, Digest("Changed"));
        var alternate = await factory.SendAsync(HttpMethod.Post, BaseUrl, Digest(key: "another-key"));
        //Assert
        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, repeated.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, alternate.StatusCode);
        Assert.Single(factory.TelegramHttp.Requests);
        await factory.WithDbAsync(async database => Assert.Single(await database.Deliveries.ToListAsync(TestContext.Current.CancellationToken)));
    }

    [Fact]
    public async Task Post_concurrent_duplicate_requests_create_one_delivery()
    {
        //Arrange
        await using var factory = new GatewayApiFactory();
        factory.TelegramHttp.ArmSuccess();
        await factory.SendAsync(HttpMethod.Get, "/api/health");
        //Act
        var responses = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => factory.SendAsync(HttpMethod.Post, BaseUrl, Digest())));
        await factory.ProcessOnceAsync();
        //Assert
        Assert.All(responses, response => Assert.True(response.StatusCode is HttpStatusCode.Accepted or HttpStatusCode.OK));
        Assert.Single(factory.TelegramHttp.Requests);
        await factory.WithDbAsync(async database => Assert.Single(await database.Deliveries.ToListAsync(TestContext.Current.CancellationToken)));
    }

    [Fact]
    public async Task Post_partial_failure_retries_only_undelivered_parts()
    {
        //Arrange
        await using var factory = new GatewayApiFactory();
        var digest = Digest(new string('ї', 10000));
        factory.TelegramHttp.ResponseFactory = number => number == 3
            ? FakeTelegramHandler.Json(HttpStatusCode.BadRequest, "{\"ok\":false,\"error_code\":400,\"description\":\"private secret text\"}")
            : FakeTelegramHandler.Json(HttpStatusCode.OK, $"{{\"ok\":true,\"result\":{{\"message_id\":{number}}}}}");
        //Act
        await factory.SendAsync(HttpMethod.Post, BaseUrl, digest);
        await factory.ProcessOnceAsync();
        var partial = await (await factory.SendAsync(HttpMethod.Get, BaseUrl + "/2026-10-08")).Content
            .ReadFromJsonAsync<DeliveryResponse>(GatewayApiFactory.JsonOptions, TestContext.Current.CancellationToken);
        var resume = await factory.SendAsync(HttpMethod.Post, BaseUrl, digest);
        await factory.ProcessOnceAsync();
        var final = await (await factory.SendAsync(HttpMethod.Get, BaseUrl + "/2026-10-08")).Content
            .ReadFromJsonAsync<DeliveryResponse>(GatewayApiFactory.JsonOptions, TestContext.Current.CancellationToken);
        //Assert
        Assert.Equal(DeliveryStatus.PartiallyDelivered, partial!.Status);
        Assert.Equal(2, partial.DeliveredMessageCount);
        Assert.False(partial.RequiresReview);
        Assert.Equal(HttpStatusCode.Accepted, resume.StatusCode);
        Assert.Equal(DeliveryStatus.Delivered, final!.Status);
        Assert.Equal(3, final.DeliveredMessageCount);
        Assert.Equal(4, factory.TelegramHttp.Requests.Count);
        Assert.Equal(new long[] { 1, 2, 4 }, final.TelegramMessageIds);
        await factory.WithDbAsync(async database => Assert.Equal(4, await database.DeliveryAttempts.CountAsync(TestContext.Current.CancellationToken)));
    }

    [Fact]
    public async Task Post_ambiguous_outcome_blocks_automatic_resubmission()
    {
        //Arrange
        await using var factory = new GatewayApiFactory();
        factory.TelegramHttp.ResponseFactory = _ => throw new TaskCanceledException("token must not escape");
        //Act
        await factory.SendAsync(HttpMethod.Post, BaseUrl, Digest());
        await factory.ProcessOnceAsync();
        var repeated = await factory.SendAsync(HttpMethod.Post, BaseUrl, Digest());
        var status = await (await factory.SendAsync(HttpMethod.Get, BaseUrl + "/2026-10-08")).Content
            .ReadFromJsonAsync<DeliveryResponse>(GatewayApiFactory.JsonOptions, TestContext.Current.CancellationToken);
        //Assert
        Assert.Equal(HttpStatusCode.Conflict, repeated.StatusCode);
        Assert.True(status!.RequiresReview);
        Assert.Equal("telegram_outcome_unknown", status.ErrorCode);
        Assert.Single(factory.TelegramHttp.Requests);
    }

    [Fact]
    public async Task Post_message_silent_notification_uses_configured_chat()
    {
        //Arrange
        await using var factory = new GatewayApiFactory();
        factory.TelegramHttp.ArmSuccess();
        //Act
        var response = await factory.SendAsync(HttpMethod.Post, "/api/messages", new MessageRequest("Тест & <text>", DisableNotification: true));
        await factory.ProcessOnceAsync();
        //Assert
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.True(Assert.Single(factory.TelegramHttp.Requests).DisableNotification);
        await factory.WithDbAsync(async database => Assert.True((await database.Deliveries.SingleAsync(TestContext.Current.CancellationToken)).DisableNotification));
    }

    [Fact]
    public async Task Get_missing_digest_is_bare_not_found()
    {
        //Arrange
        await using var factory = new GatewayApiFactory();
        //Act
        var response = await factory.SendAsync(HttpMethod.Get, BaseUrl + "/2026-10-08");
        var invalid = await factory.SendAsync(HttpMethod.Get, BaseUrl + "/08-10-2026");
        //Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }
}
