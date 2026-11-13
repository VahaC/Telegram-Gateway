using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using AgentGateway.Api.Contracts.News;
using AgentGateway.Api.Services.News;
using AgentGateway.Core.Enums;
using AgentGateway.TestInfrastructure.Http;
using AgentGateway.TestInfrastructure.News;

namespace AgentGateway.Api.Tests;

public sealed class NewsMcpHttpTests
{
    private static readonly DateOnly Date = new(2026, 11, 14);

    [Fact]
    public async Task Tools_cover_the_whole_news_workflow_with_the_api_key()
    {
        //Arrange
        await using var factory = new GatewayApiFactory();
        await using var client = await ConnectAsync(factory);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var earlier = NewsIssueDataFactory.Issue(today.AddDays(-2), prefix: "earlier");
        await factory.SendAsync(HttpMethod.Post, "/api/news/issues", earlier);
        var issue = NewsIssueDataFactory.Issue(Date);
        //Act
        var tools = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);
        var history = Read<NewsHistoryResponse>(await CallAsync(client, "get_news_history", new() { ["days"] = 30 }));
        var check = Read<NewsCheckResponse>(await CallAsync(client, "check_news_candidates", new()
        {
            ["urls"] = new[] { earlier.Items[0].Source.Url + "?utm_source=mcp" }, ["eventKeys"] = new[] { earlier.Items[1].EventKey, "new-key" }
        }));
        var submitted = Read<NewsSubmitResult>(await CallAsync(client, "submit_news_issue", new() { ["issue"] = Element(issue) }));
        var duplicate = Read<NewsSubmitResult>(await CallAsync(client, "submit_news_issue", new()
        {
            ["issue"] = Element(NewsIssueDataFactory.Issue(Date.AddDays(1)).WithItems(item => item.Number == 6 ? item with { EventKey = issue.Items[0].EventKey } : item))
        }));
        var status = Read<NewsIssueStatusResponse>(await CallAsync(client, "get_news_issue_status", new() { ["date"] = "2026-11-14" }));
        var replaced = Read<NewsSubmitResult>(await CallAsync(client, "replace_news_issue", new()
        {
            ["date"] = "2026-11-14", ["issue"] = Element(issue with { Title = "Техдайджест 14.11.2026 (виправлено)" })
        }));
        //Assert
        Assert.All(NewsMcpToolNames, name => Assert.Contains(tools, tool => tool.Name == name));
        Assert.Contains(tools, tool => tool.Name == "send_telegram_message");
        Assert.Equal(38, history.Items.Count);
        Assert.Equal(2, check.Known.Count);
        Assert.Equal(NewsSubmitStatus.Accepted, submitted.Status);
        Assert.Equal(NewsSubmitStatus.Duplicate, duplicate.Status);
        Assert.Equal("items[5].eventKey", Assert.Single(duplicate.Duplicates!).Path);
        Assert.Equal(NewsPublicationStatus.Pending, status.Status);
        Assert.Equal(NewsSubmitStatus.Accepted, replaced.Status);
        Assert.Equal(2, replaced.Issue!.Revision);
        await factory.WithDbAsync(async database => Assert.Equal(2, await database.NewsIssues.CountAsync(TestContext.Current.CancellationToken)));
    }

    [Fact]
    public async Task Submit_tool_reports_editorial_violations_without_storing()
    {
        //Arrange
        await using var factory = new GatewayApiFactory();
        await using var client = await ConnectAsync(factory);
        //Act
        var result = Read<NewsSubmitResult>(await CallAsync(client, "submit_news_issue", new() { ["issue"] = Element(NewsIssueDataFactory.Issue(Date, count: 20)) }));
        //Assert
        Assert.Equal(NewsSubmitStatus.Rejected, result.Status);
        Assert.StartsWith("min_items:", Assert.Single(result.Violations!));
        await factory.WithDbAsync(async database => Assert.Empty(await database.NewsIssues.ToListAsync(TestContext.Current.CancellationToken)));
    }

    [Fact]
    public async Task News_tools_are_hidden_and_refused_when_the_module_is_disabled()
    {
        //Arrange
        await using var factory = new GatewayApiFactory();
        factory.Settings["News:Enabled"] = "false";
        await using var client = await ConnectAsync(factory);
        //Act
        var tools = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);
        var result = await CallAsync(client, "get_news_history", new() { ["days"] = 30 });
        //Assert
        Assert.DoesNotContain(tools, tool => tool.Name.Contains("news", StringComparison.Ordinal));
        Assert.Contains(tools, tool => tool.Name == "send_telegram_message");
        Assert.True(result.IsError);
    }

    public static readonly string[] NewsMcpToolNames = ["get_news_history", "check_news_candidates", "submit_news_issue", "replace_news_issue", "get_news_issue_status"];

    private static async Task<McpClient> ConnectAsync(GatewayApiFactory factory)
    {
        var transport = new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri("http://localhost/mcp"),
            AdditionalHeaders = new Dictionary<string, string> { ["X-Api-Key"] = GatewayApiFactory.ApiKey }
        }, factory.Client);
        return await McpClient.CreateAsync(transport, cancellationToken: TestContext.Current.CancellationToken);
    }

    private static JsonElement Element<T>(T value) => JsonSerializer.SerializeToElement(value, GatewayApiFactory.JsonOptions);

    private static Task<CallToolResult> CallAsync(McpClient client, string tool, Dictionary<string, object?> arguments)
        => client.CallToolAsync(tool, arguments, cancellationToken: TestContext.Current.CancellationToken).AsTask();

    private static T Read<T>(CallToolResult result)
    {
        Assert.False(result.IsError == true, result.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text);
        return JsonSerializer.Deserialize<T>(Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text, GatewayApiFactory.JsonOptions)!;
    }
}
