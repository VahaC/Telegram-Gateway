using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;

namespace TelegramGateway.TestInfrastructure.Http;

public sealed class FakeTelegramHandler : HttpMessageHandler
{
    public ConcurrentQueue<TelegramRequestCapture> Requests { get; } = new();
    public Func<int, HttpResponseMessage>? ResponseFactory { get; set; }
    private int requestCount;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
        var root = body.RootElement;
        Requests.Enqueue(new(root.GetProperty("chat_id").GetString()!, root.GetProperty("text").GetString()!,
            root.GetProperty("parse_mode").GetString()!, root.GetProperty("disable_notification").GetBoolean(), request.RequestUri!));
        var number = Interlocked.Increment(ref requestCount);
        return ResponseFactory?.Invoke(number) ?? Json(HttpStatusCode.ServiceUnavailable, "{\"ok\":false,\"error_code\":503}");
    }

    public void ArmSuccess() => ResponseFactory = number => Json(HttpStatusCode.OK, $"{{\"ok\":true,\"result\":{{\"message_id\":{number}}}}}");

    public static HttpResponseMessage Json(HttpStatusCode status, string json)
        => new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
}
