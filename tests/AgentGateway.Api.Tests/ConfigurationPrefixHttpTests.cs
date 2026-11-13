using System.Net;
using AgentGateway.TestInfrastructure.Http;
using AgentGateway.TestInfrastructure.Logging;

namespace AgentGateway.Api.Tests;

[Collection(typeof(EnvironmentVariableCollection))]
public sealed class ConfigurationPrefixHttpTests
{
    private const string LegacyHost = "legacy-host.example";
    private const string CurrentHost = "current-host.example";
    private const string Setting = "Gateway__AllowedHosts__1";

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Startup_reads_old_and_new_prefixes_and_the_new_one_wins(bool legacy, bool current)
    {
        //Arrange
        var logs = new CapturingLoggerProvider();
        try
        {
            if (legacy) Environment.SetEnvironmentVariable("TELEGRAMGATEWAY_" + Setting, LegacyHost);
            if (current) Environment.SetEnvironmentVariable("AGENTGATEWAY_" + Setting, CurrentHost);
            await using var factory = new GatewayApiFactory { LoggerProvider = logs };
            //Act
            var legacyResponse = await HealthAsync(factory, LegacyHost);
            var currentResponse = await HealthAsync(factory, CurrentHost);
            var messages = logs.Messages.ToList();
            //Assert
            Assert.Equal(legacy && !current ? HttpStatusCode.OK : HttpStatusCode.BadRequest, legacyResponse.StatusCode);
            Assert.Equal(current ? HttpStatusCode.OK : HttpStatusCode.BadRequest, currentResponse.StatusCode);
            var warning = messages.SingleOrDefault(message => message.Contains("old TELEGRAMGATEWAY_ prefix", StringComparison.Ordinal));
            if (legacy)
            {
                Assert.NotNull(warning);
                Assert.Contains("TELEGRAMGATEWAY_" + Setting, warning);
                Assert.DoesNotContain(LegacyHost, warning); // Names only, never values.
            }
            else Assert.Null(warning);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TELEGRAMGATEWAY_" + Setting, null);
            Environment.SetEnvironmentVariable("AGENTGATEWAY_" + Setting, null);
        }
    }

    private static async Task<HttpResponseMessage> HealthAsync(GatewayApiFactory factory, string host)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/health");
        request.Headers.Host = host;
        return await factory.Client.SendAsync(request, TestContext.Current.CancellationToken);
    }
}
