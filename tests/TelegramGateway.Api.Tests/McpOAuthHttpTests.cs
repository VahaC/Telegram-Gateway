using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Web;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using TelegramGateway.Api.Contracts;
using TelegramGateway.Api.Services.Delivery;
using TelegramGateway.Core.Enums;
using TelegramGateway.TestInfrastructure.Http;
using TelegramGateway.TestInfrastructure.Time;

namespace TelegramGateway.Api.Tests;

public sealed class McpOAuthHttpTests
{
    private const string ClientSecret = "test-oauth-client-secret-not-real-0123456789";
    private const string Redirect = "https://client.example/callback";
    private const string Verifier = "test-verifier-0123456789-abcdefghijklmnopqrstuvwxyz-0123456789";

    [Fact]
    public async Task Discovery_and_challenge_are_public_but_tools_require_authentication()
    {
        //Arrange
        await using var factory = CreateFactory();
        using var client = CreateClient(factory);
        //Act
        var metadata = await client.GetFromJsonAsync<JsonElement>("/.well-known/oauth-protected-resource", TestContext.Current.CancellationToken);
        var authorization = await client.GetFromJsonAsync<JsonElement>("/.well-known/openid-configuration", TestContext.Current.CancellationToken);
        var response = await client.PostAsJsonAsync("/mcp", new { jsonrpc = "2.0", id = 1, method = "tools/list" }, TestContext.Current.CancellationToken);
        //Assert
        Assert.Equal("https://localhost/mcp", metadata.GetProperty("resource").GetString());
        Assert.Equal("https://localhost/", authorization.GetProperty("issuer").GetString());
        Assert.Equal("https://localhost/oauth/authorize", authorization.GetProperty("authorization_endpoint").GetString());
        Assert.True(authorization.GetProperty("authorization_response_iss_parameter_supported").GetBoolean());
        var keys = await client.GetAsync(authorization.GetProperty("jwks_uri").GetString(), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, keys.StatusCode);
        Assert.Contains(authorization.GetProperty("code_challenge_methods_supported").EnumerateArray(), value => value.GetString() == "S256");
        Assert.DoesNotContain(authorization.GetProperty("code_challenge_methods_supported").EnumerateArray(), value => value.GetString() == "plain");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("resource_metadata=\"https://localhost/.well-known/oauth-protected-resource\"", response.Headers.WwwAuthenticate.ToString());
        Assert.Empty(factory.TelegramHttp.Requests);
    }

    [Fact]
    public async Task Owner_consent_code_exchange_and_mcp_delivery_work_without_exposing_gateway_key_to_client()
    {
        //Arrange
        await using var factory = CreateFactory();
        factory.TelegramHttp.ArmSuccess();
        using var http = CreateClient(factory);
        //Act
        var code = await AuthorizeAsync(http);
        var token = await ExchangeAsync(http, code);
        await using var transport = new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri("https://localhost/mcp"),
            AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = "Bearer " + token.GetProperty("access_token").GetString() }
        }, http);
        await using var mcp = await McpClient.CreateAsync(transport, cancellationToken: TestContext.Current.CancellationToken);
        var tools = await mcp.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);
        var result = await mcp.CallToolAsync("send_technology_digest", new Dictionary<string, object?>
        {
            ["title"] = "Integration test", ["date"] = "2026-10-08", ["content"] = "OAuth notification",
            ["idempotencyKey"] = "oauth-integration"
        }, cancellationToken: TestContext.Current.CancellationToken);
        await factory.ProcessOnceAsync();
        var submission = JsonSerializer.Deserialize<SubmitResult>(Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text,
            GatewayApiFactory.JsonOptions)!;
        var statusResult = await mcp.CallToolAsync("get_delivery_status", new Dictionary<string, object?>
        {
            ["id"] = submission.Delivery!.Id.ToString()
        }, cancellationToken: TestContext.Current.CancellationToken);
        var delivered = JsonSerializer.Deserialize<DeliveryResponse>(Assert.IsType<TextContentBlock>(Assert.Single(statusResult.Content)).Text,
            GatewayApiFactory.JsonOptions)!;
        //Assert
        Assert.Contains(tools, tool => tool.Name == "send_technology_digest");
        Assert.Contains(tools, tool => tool.Name == "get_delivery_status");
        Assert.False(result.IsError == true);
        Assert.False(statusResult.IsError == true);
        Assert.Equal(DeliveryStatus.Delivered, delivered.Status);
        Assert.Equal(new long[] { 1 }, delivered.TelegramMessageIds);
        Assert.Single(factory.TelegramHttp.Requests);
        Assert.DoesNotContain(GatewayApiFactory.ApiKey, token.ToString());
        Assert.True(token.TryGetProperty("refresh_token", out _));
        await factory.WithDbAsync(async db => Assert.Single(await db.Deliveries.Where(row => row.IdempotencyKey == "oauth-integration").ToListAsync(TestContext.Current.CancellationToken)));
    }

    [Theory]
    [InlineData("redirect_uri", "https://attacker.example/callback")]
    [InlineData("code_challenge_method", "plain")]
    [InlineData("resource", "https://attacker.example/mcp")]
    [InlineData("scope", "offline_access")]
    public async Task Authorization_rejects_unregistered_redirect_or_invalid_pkce_resource_scope(string parameter, string value)
    {
        //Arrange
        await using var factory = CreateFactory();
        using var client = CreateClient(factory);
        var query = AuthorizationQuery();
        query[parameter] = value;
        //Act
        var response = await client.GetAsync(QueryPath(query), TestContext.Current.CancellationToken);
        //Assert
        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
        if (response.Headers.Location is { } location)
        {
            Assert.Equal("client.example", location.Host);
            Assert.Contains("error=", location.Query);
            Assert.DoesNotContain("code=", location.Query);
        }
        Assert.Empty(factory.TelegramHttp.Requests);
    }

    [Fact]
    public async Task Owner_consent_policy_allows_only_requested_callback_and_openid_code_exchange()
    {
        //Arrange
        await using var factory = CreateFactory();
        factory.Settings["McpOAuth:RedirectUris:1"] = "https://another-client.example/callback";
        using var client = CreateClient(factory);
        var query = AuthorizationQuery();
        query["scope"] = "openid offline_access telegram:send";
        var path = QueryPath(query);
        //Act
        var page = await client.GetAsync(path, TestContext.Current.CancellationToken);
        var form = ConsentForm(await page.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), GatewayApiFactory.ApiKey);
        form["scope"] = query["scope"];
        var consent = await client.PostAsync(path, new FormUrlEncodedContent(form), TestContext.Current.CancellationToken);
        //Assert
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Equal("default-src 'none'; form-action 'self' " + Redirect + "; frame-ancestors 'none'; base-uri 'none'",
            Assert.Single(page.Headers.GetValues("Content-Security-Policy")));
        Assert.Equal(HttpStatusCode.Redirect, consent.StatusCode);
        Assert.Equal(Redirect, consent.Headers.Location!.GetLeftPart(UriPartial.Path));
        var code = HttpUtility.ParseQueryString(consent.Headers.Location.Query)["code"]!;
        var tokens = await ExchangeAsync(client, code);
        Assert.True(tokens.TryGetProperty("id_token", out _));
        Assert.True(tokens.TryGetProperty("refresh_token", out _));
        Assert.Empty(factory.TelegramHttp.Requests);
    }

    [Theory]
    [InlineData(false, "invalid-key")]
    [InlineData(true, "invalid-key")]
    public async Task Owner_consent_requires_antiforgery_and_correct_gateway_key(bool csrf, string key)
    {
        //Arrange
        await using var factory = CreateFactory();
        using var client = CreateClient(factory);
        var path = QueryPath(AuthorizationQuery());
        var page = await client.GetStringAsync(path, TestContext.Current.CancellationToken);
        var form = ConsentForm(page, key);
        if (!csrf) form.Remove("__RequestVerificationToken");
        //Act
        var response = await client.PostAsync(path, new FormUrlEncodedContent(form), TestContext.Current.CancellationToken);
        //Assert
        Assert.Equal(csrf ? HttpStatusCode.Unauthorized : HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(response.Headers.Location);
    }

    [Fact]
    public async Task Token_exchange_rejects_wrong_verifier_client_secret_and_code_replay()
    {
        //Arrange
        await using var factory = CreateFactory();
        using var client = CreateClient(factory);
        var code = await AuthorizeAsync(client);
        //Act
        var badSecret = await client.PostAsync("/oauth/token", new FormUrlEncodedContent(TokenForm(code, secret: "incorrect")), TestContext.Current.CancellationToken);
        var badVerifier = await client.PostAsync("/oauth/token", new FormUrlEncodedContent(TokenForm(code, verifier: new string('x', 64))), TestContext.Current.CancellationToken);
        //Assert
        Assert.NotEqual(HttpStatusCode.OK, badSecret.StatusCode);
        Assert.NotEqual(HttpStatusCode.OK, badVerifier.StatusCode);
        // A failed PKCE exchange can invalidate its authorization code; start a fresh consent.
        var freshCode = await AuthorizeAsync(client);
        await ExchangeAsync(client, freshCode);
        var replay = await client.PostAsync("/oauth/token", new FormUrlEncodedContent(TokenForm(freshCode)), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, replay.StatusCode);
    }

    [Fact]
    public async Task Refresh_tokens_rotate_and_revoked_or_tampered_access_tokens_cannot_use_tools()
    {
        //Arrange
        await using var factory = CreateFactory();
        using var client = CreateClient(factory);
        var token = await ExchangeAsync(client, await AuthorizeAsync(client));
        var refresh = token.GetProperty("refresh_token").GetString()!;
        //Act
        var refreshed = await client.PostAsync("/oauth/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token", ["refresh_token"] = refresh,
            ["client_id"] = "telegram-gateway", ["client_secret"] = ClientSecret, ["resource"] = "https://localhost/mcp"
        }), TestContext.Current.CancellationToken);
        //Assert
        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);
        var next = await refreshed.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.NotEqual(refresh, next.GetProperty("refresh_token").GetString());
        using var scope = factory.Services.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<IOpenIddictTokenManager>();
        await foreach (var entry in manager.FindBySubjectAsync("gateway-owner", TestContext.Current.CancellationToken))
            await manager.TryRevokeAsync(entry, TestContext.Current.CancellationToken);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", next.GetProperty("access_token").GetString());
        var revoked = await client.PostAsJsonAsync("/mcp", new { jsonrpc = "2.0", id = 1, method = "tools/list" }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, revoked.StatusCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "tampered-token");
        var tampered = await client.PostAsJsonAsync("/mcp", new { jsonrpc = "2.0", id = 1, method = "tools/list" }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, tampered.StatusCode);
    }

    [Fact]
    public async Task OAuth_disabled_does_not_expose_discovery_or_login()
    {
        //Arrange
        await using var factory = new GatewayApiFactory();
        //Act
        var response = await factory.SendAsync(HttpMethod.Get, "/.well-known/oauth-protected-resource", authenticated: false);
        //Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Expired_access_token_is_rejected()
    {
        //Arrange
        await using var factory = CreateFactory();
        var time = new MutableTimeProvider();
        factory.Time = time;
        using var client = CreateClient(factory);
        var token = await ExchangeAsync(client, await AuthorizeAsync(client));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.GetProperty("access_token").GetString());
        //Act
        time.Advance(TimeSpan.FromMinutes(25));
        var response = await client.PostAsJsonAsync("/mcp", new { jsonrpc = "2.0", id = 1, method = "tools/list" }, TestContext.Current.CancellationToken);
        //Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(factory.TelegramHttp.Requests);
    }

    [Fact]
    public async Task Tokens_survive_restart_and_gateway_key_rotation_revokes_owner_access()
    {
        //Arrange
        var path = Path.Combine(Path.GetTempPath(), "telegram-gateway-oauth-restart-" + Guid.NewGuid().ToString("N"));
        try
        {
            string access;
            await using (var first = CreateFactory(path))
            {
                using var client = CreateClient(first);
                access = (await ExchangeAsync(client, await AuthorizeAsync(client))).GetProperty("access_token").GetString()!;
            }
            //Act
            await using (var restarted = CreateFactory(path))
            {
                using var http = CreateClient(restarted);
                await using var transport = new HttpClientTransport(new HttpClientTransportOptions
                {
                    Endpoint = new Uri("https://localhost/mcp"), AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = "Bearer " + access }
                }, http);
                await using var client = await McpClient.CreateAsync(transport, cancellationToken: TestContext.Current.CancellationToken);
                //Assert
                Assert.Contains(await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken), tool => tool.Name == "send_technology_digest");
            }
            await using var rotated = CreateFactory(path);
            rotated.Settings["API_KEY"] = "rotated-test-key-not-real-01234567890123456789";
            using var rotatedClient = CreateClient(rotated);
            rotatedClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", access);
            var rejected = await rotatedClient.PostAsJsonAsync("/mcp", new { jsonrpc = "2.0", id = 1, method = "tools/list" }, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.Unauthorized, rejected.StatusCode);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (!Path.GetFullPath(path).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Unexpected test path.");
            if (Directory.Exists(path)) Directory.Delete(path, true);
        }
    }

    [Theory]
    [InlineData("audience")]
    [InlineData("issuer")]
    [InlineData("scope")]
    public async Task Foreign_audience_issuer_or_scope_is_rejected_even_when_signed_with_gateway_key(string invalid)
    {
        //Arrange
        await using var factory = CreateFactory();
        using var client = CreateClient(factory);
        var issued = await ExchangeAsync(client, await AuthorizeAsync(client));
        var options = factory.Services.GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<OpenIddict.Server.OpenIddictServerOptions>>().CurrentValue;
        var handler = new Microsoft.IdentityModel.JsonWebTokens.JsonWebTokenHandler();
        var validated = await handler.ValidateTokenAsync(issued.GetProperty("access_token").GetString(), new Microsoft.IdentityModel.Tokens.TokenValidationParameters
        {
            ValidIssuer = "https://localhost/", ValidAudience = "https://localhost/mcp",
            IssuerSigningKeys = options.SigningCredentials.Select(credential => credential.Key),
            TokenDecryptionKeys = options.EncryptionCredentials.Select(credential => credential.Key)
        });
        Assert.True(validated.IsValid);
        var descriptor = new Microsoft.IdentityModel.Tokens.SecurityTokenDescriptor
        {
            Issuer = "https://localhost/", Audience = "https://localhost/mcp", Expires = DateTime.UtcNow.AddMinutes(15),
            SigningCredentials = options.SigningCredentials[0], EncryptingCredentials = options.EncryptionCredentials[0], TokenType = "at+jwt",
            Subject = new System.Security.Claims.ClaimsIdentity(validated.ClaimsIdentity.Claims.Where(claim => claim.Type is not ("iss" or "aud" or "exp" or "iat" or "nbf")))
        };
        // Prove the reconstructed token is accepted before changing its audience.
        await using (var transport = new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri("https://localhost/mcp"), AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = "Bearer " + handler.CreateToken(descriptor) }
        }, client))
        {
            await using var mcp = await McpClient.CreateAsync(transport, cancellationToken: TestContext.Current.CancellationToken);
            Assert.NotEmpty(await mcp.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken));
        }
        if (invalid == "audience") descriptor.Audience = "https://foreign.example/mcp";
        if (invalid == "issuer") descriptor.Issuer = "https://foreign.example/";
        if (invalid == "scope")
        {
            foreach (var claim in descriptor.Subject.FindAll("scope").Concat(descriptor.Subject.FindAll("oi_scp")).ToArray()) descriptor.Subject.RemoveClaim(claim);
            descriptor.Subject.AddClaim(new System.Security.Claims.Claim("scope", "other:write"));
        }
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", handler.CreateToken(descriptor));
        //Act
        var response = await client.PostAsJsonAsync("/mcp", new { jsonrpc = "2.0", id = 1, method = "tools/list" }, TestContext.Current.CancellationToken);
        //Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static GatewayApiFactory CreateFactory(string? path = null)
    {
        var factory = new GatewayApiFactory(path);
        factory.Settings["McpOAuth:Enabled"] = "true";
        factory.Settings["McpOAuth:PublicUrl"] = "https://localhost";
        factory.Settings["McpOAuth:ClientSecret"] = ClientSecret;
        factory.Settings["McpOAuth:RedirectUris:0"] = Redirect;
        return factory;
    }

    private static HttpClient CreateClient(GatewayApiFactory factory) => factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false
    });

    private static Dictionary<string, string> AuthorizationQuery() => new()
    {
        ["response_type"] = "code", ["client_id"] = "telegram-gateway", ["redirect_uri"] = Redirect,
        ["scope"] = "telegram:send offline_access", ["state"] = "test-state", ["resource"] = "https://localhost/mcp",
        ["code_challenge"] = Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(Verifier))).TrimEnd('=').Replace('+', '-').Replace('/', '_'),
        ["code_challenge_method"] = "S256"
    };

    private static string QueryPath(Dictionary<string, string> query) => "/oauth/authorize?"
        + string.Join('&', query.Select(pair => Uri.EscapeDataString(pair.Key) + "=" + Uri.EscapeDataString(pair.Value)));

    private static Dictionary<string, string> ConsentForm(string page, string key)
    {
        var form = AuthorizationQuery();
        form["__RequestVerificationToken"] = WebUtility.HtmlDecode(Regex.Match(page, "name=\"__RequestVerificationToken\" value=\"([^\"]+)\"").Groups[1].Value);
        form["accessKey"] = key;
        form["action"] = "allow";
        return form;
    }

    private static async Task<string> AuthorizeAsync(HttpClient client)
    {
        var path = QueryPath(AuthorizationQuery());
        var page = await client.GetAsync(path, TestContext.Current.CancellationToken);
        Assert.True(page.StatusCode == HttpStatusCode.OK, await page.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var consent = await client.PostAsync(path, new FormUrlEncodedContent(ConsentForm(await page.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), GatewayApiFactory.ApiKey)), TestContext.Current.CancellationToken);
        Assert.True(consent.StatusCode == HttpStatusCode.Redirect, await consent.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.NotNull(consent.Headers.Location);
        Assert.Equal("https://localhost/", HttpUtility.ParseQueryString(consent.Headers.Location.Query)["iss"]);
        return HttpUtility.ParseQueryString(consent.Headers.Location.Query)["code"] ?? throw new InvalidOperationException("Authorization returned no code.");
    }

    private static Dictionary<string, string> TokenForm(string code, string secret = ClientSecret, string verifier = Verifier) => new()
    {
        ["grant_type"] = "authorization_code", ["code"] = code, ["redirect_uri"] = Redirect,
        ["client_id"] = "telegram-gateway", ["client_secret"] = secret, ["code_verifier"] = verifier, ["resource"] = "https://localhost/mcp"
    };

    private static async Task<JsonElement> ExchangeAsync(HttpClient client, string code)
    {
        var response = await client.PostAsync("/oauth/token", new FormUrlEncodedContent(TokenForm(code)), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
    }
}
