using System.Net;
using System.Text;
using TelegramGateway.TestInfrastructure.Http;

namespace TelegramGateway.Api.Tests;

public sealed class AuthenticationHttpTests
{
    [Theory]
    [InlineData("/api/messages", "POST")]
    [InlineData("/api/digests", "POST")]
    [InlineData("/api/digests/2026-10-08", "GET")]
    [InlineData("/api/deliveries/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", "GET")]
    [InlineData("/openapi/v1.json", "GET")]
    [InlineData("/mcp", "POST")]
    public async Task Request_without_key_is_unauthorized(string path, string method)
    {
        //Arrange
        await using var factory = new GatewayApiFactory();
        //Act
        var response = await factory.SendAsync(new HttpMethod(method), path, authenticated: false);
        //Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("{\"error\":\"Authentication required.\"}", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Empty(factory.TelegramHttp.Requests);
    }

    [Theory]
    [InlineData("wrong")]
    [InlineData("TEST-api-key-never-for-production-0123456789")]
    public async Task Request_wrong_key_has_same_unauthorized_response(string key)
    {
        //Arrange
        await using var factory = new GatewayApiFactory();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/digests/2026-10-08");
        request.Headers.Add("X-Api-Key", key);
        //Act
        var response = await factory.Client.SendAsync(request, TestContext.Current.CancellationToken);
        //Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("{\"error\":\"Authentication required.\"}", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("/api/health")]
    [InlineData("/api/ready")]
    public async Task Health_anonymous_returns_only_health_metadata(string path)
    {
        //Arrange
        await using var factory = new GatewayApiFactory();
        //Act
        var response = await factory.SendAsync(HttpMethod.Get, path, authenticated: false);
        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.DoesNotContain("test-token", text);
        Assert.DoesNotContain("123456789", text);
        Assert.DoesNotContain("text", text);
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
    }

    [Fact]
    public async Task Request_oversized_body_is_rejected_before_persistence()
    {
        //Arrange
        await using var factory = new GatewayApiFactory();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/messages");
        request.Headers.Add("X-Api-Key", GatewayApiFactory.ApiKey);
        request.Content = new StringContent(new string('x', 129 * 1024), Encoding.UTF8, "application/json");
        //Act
        var response = await factory.Client.SendAsync(request, TestContext.Current.CancellationToken);
        //Assert
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Empty(factory.TelegramHttp.Requests);
    }

    [Fact]
    public async Task Request_untrusted_forwarded_ip_cannot_bypass_rate_limit()
    {
        //Arrange
        await using var factory = new GatewayApiFactory();
        factory.Settings["Gateway:RequestLimitPerMinute"] = "2";
        var responses = new List<HttpResponseMessage>();
        //Act
        for (var index = 0; index < 3; index++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/api/digests/2026-10-08");
            request.Headers.Add("X-Api-Key", GatewayApiFactory.ApiKey);
            request.Headers.Add("X-Forwarded-For", $"203.0.113.{index + 1}");
            responses.Add(await factory.Client.SendAsync(request, TestContext.Current.CancellationToken));
        }
        //Assert
        Assert.Equal(HttpStatusCode.NotFound, responses[0].StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, responses[1].StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, responses[2].StatusCode);
        Assert.NotNull(responses[2].Headers.RetryAfter);
    }

    [Fact]
    public async Task Request_wrong_host_is_rejected()
    {
        //Arrange
        await using var factory = new GatewayApiFactory();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/health");
        request.Headers.Host = "attacker.example";
        //Act
        var response = await factory.Client.SendAsync(request, TestContext.Current.CancellationToken);
        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
