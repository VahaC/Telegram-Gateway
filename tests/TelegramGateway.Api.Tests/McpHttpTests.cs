using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Client;
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
        Assert.False(called.IsError == true);
        Assert.Single(factory.TelegramHttp.Requests);
        await factory.WithDbAsync(async database => Assert.Equal("mcp-test", (await database.Deliveries.SingleAsync(TestContext.Current.CancellationToken)).IdempotencyKey));
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
