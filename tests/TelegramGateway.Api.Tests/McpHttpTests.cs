using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using TelegramGateway.Api.Contracts;
using TelegramGateway.Api.Services.Delivery;
using TelegramGateway.Core.Enums;
using TelegramGateway.TestInfrastructure.Http;

namespace TelegramGateway.Api.Tests;

public sealed class McpHttpTests
{
    private const string BaseUrl = "/mcp";

    [Fact]
    public async Task Tools_list_and_call_use_authenticated_delivery_services()
    {
        //Arrange
        await using var factory = new GatewayApiFactory();
        factory.TelegramHttp.ArmSuccess();
        await using var transport = new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri("http://localhost/mcp"),
            AdditionalHeaders = new Dictionary<string, string> { ["X-Api-Key"] = GatewayApiFactory.ApiKey }
        }, factory.Client);
        await using var client = await McpClient.CreateAsync(transport, cancellationToken: TestContext.Current.CancellationToken);
        //Act
        var listed = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);
        var called = await client.CallToolAsync("send_telegram_message", new Dictionary<string, object?>
        {
            ["text"] = "MCP Україна", ["idempotencyKey"] = "mcp-test"
        }, cancellationToken: TestContext.Current.CancellationToken);
        await factory.ProcessOnceAsync();
        //Assert
        Assert.Contains(listed, tool => tool.Name == "send_telegram_message");
        Assert.Contains(listed, tool => tool.Name == "send_technology_digest");
        Assert.Contains(listed, tool => tool.Name == "get_digest_delivery_status");
        Assert.Contains(listed, tool => tool.Name == "get_delivery_status");
        Assert.False(called.IsError == true);
        Assert.Single(factory.TelegramHttp.Requests);
        await factory.WithDbAsync(async database => Assert.Equal("mcp-test", (await database.Deliveries.SingleAsync(TestContext.Current.CancellationToken)).IdempotencyKey));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Delivery_status_by_id_reports_pending_then_delivered_without_resending(bool digest)
    {
        //Arrange
        await using var factory = new GatewayApiFactory();
        factory.TelegramHttp.ArmSuccess();
        await using var transport = new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri("http://localhost/mcp"),
            AdditionalHeaders = new Dictionary<string, string> { ["X-Api-Key"] = GatewayApiFactory.ApiKey }
        }, factory.Client);
        await using var client = await McpClient.CreateAsync(transport, cancellationToken: TestContext.Current.CancellationToken);
        var arguments = new Dictionary<string, object?> { ["idempotencyKey"] = "status-by-id" };
        if (digest)
        {
            arguments["title"] = "Prepared digest";
            arguments["date"] = "2026-10-08";
            arguments["content"] = "Private notification content";
        }
        else arguments["text"] = "Private notification content";
        //Act
        var submission = ReadResult<SubmitResult>(await client.CallToolAsync(digest ? "send_technology_digest" : "send_telegram_message",
            arguments, cancellationToken: TestContext.Current.CancellationToken));
        var identifier = submission.Delivery!.Id;
        var statusArguments = new Dictionary<string, object?> { ["id"] = identifier.ToString() };
        var pending = ReadResult<DeliveryResponse>(await client.CallToolAsync("get_delivery_status", statusArguments,
            cancellationToken: TestContext.Current.CancellationToken));
        await factory.ProcessOnceAsync();
        var result = await client.CallToolAsync("get_delivery_status", statusArguments, cancellationToken: TestContext.Current.CancellationToken);
        var delivered = ReadResult<DeliveryResponse>(result);
        //Assert
        Assert.Equal(SubmitStatus.Accepted, submission.Status);
        Assert.Equal(DeliveryStatus.Pending, pending.Status);
        Assert.Empty(pending.TelegramMessageIds);
        Assert.Equal(identifier, delivered.Id);
        Assert.Equal(DeliveryStatus.Delivered, delivered.Status);
        Assert.Equal(digest ? new DateOnly(2026, 10, 8) : (DateOnly?)null, delivered.DigestDate);
        Assert.Equal(1, delivered.DeliveredMessageCount);
        Assert.Equal(new long[] { 1 }, delivered.TelegramMessageIds);
        Assert.Equal(AttemptStatus.Delivered, Assert.Single(delivered.Attempts).Status);
        Assert.False(delivered.RequiresReview);
        Assert.Null(delivered.ErrorCode);
        Assert.DoesNotContain("Private notification content", Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text);
        Assert.Single(factory.TelegramHttp.Requests);
        await factory.WithDbAsync(async database => Assert.Equal(DeliveryStatus.Delivered,
            (await database.Deliveries.SingleAsync(TestContext.Current.CancellationToken)).Status));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Delivery_status_by_id_reports_rejection_or_uncertain_outcome_without_resending(bool ambiguous)
    {
        //Arrange
        await using var factory = new GatewayApiFactory();
        factory.TelegramHttp.ResponseFactory = _ => ambiguous
            ? throw new TaskCanceledException("private transport detail")
            : FakeTelegramHandler.Json(HttpStatusCode.BadRequest, "{\"ok\":false,\"error_code\":400,\"description\":\"private provider detail\"}");
        var submission = await factory.SendAsync(HttpMethod.Post, "/api/messages", new MessageRequest("Private notification content"));
        var accepted = (await submission.Content.ReadFromJsonAsync<DeliveryResponse>(GatewayApiFactory.JsonOptions, TestContext.Current.CancellationToken))!;
        await factory.ProcessOnceAsync();
        await using var transport = new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri("http://localhost/mcp"),
            AdditionalHeaders = new Dictionary<string, string> { ["X-Api-Key"] = GatewayApiFactory.ApiKey }
        }, factory.Client);
        await using var client = await McpClient.CreateAsync(transport, cancellationToken: TestContext.Current.CancellationToken);
        //Act
        var result = await client.CallToolAsync("get_delivery_status", new Dictionary<string, object?> { ["id"] = accepted.Id.ToString() },
            cancellationToken: TestContext.Current.CancellationToken);
        var status = ReadResult<DeliveryResponse>(result);
        //Assert
        Assert.Equal(DeliveryStatus.Failed, status.Status);
        Assert.Equal(ambiguous, status.RequiresReview);
        Assert.Equal(ambiguous ? "telegram_outcome_unknown" : "telegram_400", status.ErrorCode);
        Assert.Empty(status.TelegramMessageIds);
        Assert.Equal(0, status.DeliveredMessageCount);
        Assert.Equal(ambiguous ? AttemptStatus.Ambiguous : AttemptStatus.Rejected, Assert.Single(status.Attempts).Status);
        Assert.DoesNotContain("private", Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text, StringComparison.OrdinalIgnoreCase);
        Assert.Single(factory.TelegramHttp.Requests);
        await factory.WithDbAsync(async database =>
        {
            Assert.Equal(DeliveryStatus.Failed, (await database.Deliveries.SingleAsync(TestContext.Current.CancellationToken)).Status);
            Assert.Single(await database.DeliveryAttempts.ToListAsync(TestContext.Current.CancellationToken));
        });
    }

    [Fact]
    public async Task Delivery_status_unknown_id_returns_no_metadata_and_invalid_id_is_an_error()
    {
        //Arrange
        await using var factory = new GatewayApiFactory();
        await using var transport = new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri("http://localhost/mcp"),
            AdditionalHeaders = new Dictionary<string, string> { ["X-Api-Key"] = GatewayApiFactory.ApiKey }
        }, factory.Client);
        await using var client = await McpClient.CreateAsync(transport, cancellationToken: TestContext.Current.CancellationToken);
        //Act
        var missing = await client.CallToolAsync("get_delivery_status", new Dictionary<string, object?> { ["id"] = Guid.NewGuid().ToString() },
            cancellationToken: TestContext.Current.CancellationToken);
        var invalid = await client.CallToolAsync("get_delivery_status", new Dictionary<string, object?> { ["id"] = "invalid" },
            cancellationToken: TestContext.Current.CancellationToken);
        //Assert
        Assert.False(missing.IsError == true);
        Assert.Empty(missing.Content);
        Assert.True(invalid.IsError == true);
        Assert.Empty(factory.TelegramHttp.Requests);
        await factory.WithDbAsync(async database => Assert.Empty(await database.Deliveries.ToListAsync(TestContext.Current.CancellationToken)));
    }

    [Fact]
    public async Task Delivery_status_without_authentication_is_unauthorized()
    {
        //Arrange
        await using var factory = new GatewayApiFactory();
        //Act
        var response = await factory.SendAsync(HttpMethod.Post, BaseUrl, new
        {
            jsonrpc = "2.0", id = 1, method = "tools/call",
            @params = new { name = "get_delivery_status", arguments = new { id = Guid.NewGuid().ToString() } }
        }, authenticated: false);
        //Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(factory.TelegramHttp.Requests);
    }

    private static T ReadResult<T>(CallToolResult result)
    {
        Assert.False(result.IsError == true);
        return JsonSerializer.Deserialize<T>(Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text,
            GatewayApiFactory.JsonOptions) ?? throw new InvalidOperationException("Expected tool metadata.");
    }

    [Fact]
    public async Task Tools_untrusted_origin_is_forbidden()
    {
        //Arrange
        await using var factory = new GatewayApiFactory();
        using var request = new HttpRequestMessage(HttpMethod.Post, BaseUrl);
        request.Headers.Add("X-Api-Key", GatewayApiFactory.ApiKey);
        request.Headers.Add("Origin", "https://attacker.example");
        request.Content = JsonContent.Create(new { jsonrpc = "2.0", id = 1, method = "tools/list" });
        //Act
        var response = await factory.Client.SendAsync(request, TestContext.Current.CancellationToken);
        //Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Tools_disabled_endpoint_is_not_available()
    {
        //Arrange
        await using var factory = new GatewayApiFactory();
        factory.Settings["Gateway:EnableMcp"] = "false";
        //Act
        var response = await factory.SendAsync(HttpMethod.Post, BaseUrl);
        //Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Openapi_authenticated_document_describes_delivery_routes()
    {
        //Arrange
        await using var factory = new GatewayApiFactory();
        //Act
        var response = await factory.SendAsync(HttpMethod.Get, "/openapi/v1.json");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(document.RootElement.GetProperty("paths").TryGetProperty("/api/digests", out _));
        Assert.True(document.RootElement.GetProperty("paths").TryGetProperty("/api/messages", out _));
    }
}
