using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using TelegramGateway.TestInfrastructure.Http;

namespace TelegramGateway.Api.Tests;

public sealed class ValidationHttpTests
{
    [Theory]
    [InlineData("{\"text\":\"test\",\"chatId\":987654321}")]
    [InlineData("{\"text\":null}")]
    [InlineData("{\"text\":\"test\",\"format\":null}")]
    [InlineData("{\"text\":\"test\",\"idempotencyKey\":\"bad key\"}")]
    [InlineData("not-json")]
    public async Task Post_invalid_or_destination_override_is_rejected(string body)
    {
        //Arrange
        await using var factory = new GatewayApiFactory();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/messages");
        request.Headers.Add("X-Api-Key", GatewayApiFactory.ApiKey);
        request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        //Act
        var response = await factory.Client.SendAsync(request, TestContext.Current.CancellationToken);
        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("error", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Empty(factory.TelegramHttp.Requests);
        await factory.WithDbAsync(async database => Assert.Empty(await database.Deliveries.ToListAsync(TestContext.Current.CancellationToken)));
    }

    [Fact]
    public async Task Post_complete_ukrainian_fixture_is_accepted_and_delivered()
    {
        //Arrange
        await using var factory = new GatewayApiFactory();
        factory.TelegramHttp.ArmSuccess();
        var repository = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var body = await File.ReadAllTextAsync(Path.Combine(repository, "examples/sample-ukrainian-digest.json"), TestContext.Current.CancellationToken);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/digests");
        request.Headers.Add("X-Api-Key", GatewayApiFactory.ApiKey);
        request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        //Act
        var response = await factory.Client.SendAsync(request, TestContext.Current.CancellationToken);
        await factory.ProcessOnceAsync();
        //Assert
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.NotEmpty(factory.TelegramHttp.Requests);
        Assert.All(factory.TelegramHttp.Requests, sent => Assert.True(sent.DisableNotification));
        await factory.WithDbAsync(async database => Assert.Equal(TelegramGateway.Core.Enums.DeliveryStatus.Delivered,
            (await database.Deliveries.SingleAsync(TestContext.Current.CancellationToken)).Status));
    }
}
