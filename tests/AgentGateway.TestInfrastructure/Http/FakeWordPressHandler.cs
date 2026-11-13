using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AgentGateway.TestInfrastructure.Http;

/// <summary>
/// A recording fake of the theme's news routes that behaves like the contract: upserts by key and revision, 201 on create,
/// the stored post unchanged for a repeated revision, image results, trash on DELETE and a day verification read.
/// </summary>
public sealed class FakeWordPressHandler : HttpMessageHandler
{
    public const string BaseUrl = "https://wordpress.test";
    private const string Prefix = "/wp-json/vahac/v1/news/";
    private readonly Lock gate = new();
    private readonly Dictionary<string, int> attempts = [];
    private readonly Dictionary<string, (int Revision, List<string> Order)> days = [];
    private readonly Dictionary<string, (int Revision, string? Image, long PostId)> items = [];
    private long nextPostId = 100;

    public ConcurrentQueue<WordPressRequestCapture> Requests { get; } = new();

    /// <summary>Runs before the response is chosen, e.g. to stop the host mid-run.</summary>
    public Action<WordPressRequestCapture>? OnRequest { get; set; }

    /// <summary>Returns a response to replace the contract behaviour for one call, or null to keep it. May throw to simulate transport failures.</summary>
    public Func<WordPressRequestCapture, HttpResponseMessage?>? Override { get; set; }

    public IReadOnlyList<WordPressRequestCapture> Calls => Requests.ToList();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri!.AbsolutePath;
        if (!path.StartsWith(Prefix, StringComparison.Ordinal)) return Json(HttpStatusCode.NotFound, "{\"code\":\"rest_no_route\"}");
        path = path[Prefix.Length..];
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        int attempt;
        lock (gate) attempt = attempts[request.Method + " " + path] = attempts.GetValueOrDefault(request.Method + " " + path) + 1;
        var capture = new WordPressRequestCapture(request.Method.Method, path, body, request.Headers.Authorization?.ToString(), attempt);
        Requests.Enqueue(capture);
        OnRequest?.Invoke(capture);
        if (Override?.Invoke(capture) is { } custom) return custom;
        lock (gate) return Contract(capture);
    }

    private HttpResponseMessage Contract(WordPressRequestCapture capture)
    {
        var parts = capture.Path.Split('/');
        var key = Uri.UnescapeDataString(parts[1]);
        switch (capture.Method, parts[0])
        {
            case ("PUT", "days"):
            {
                var root = JsonNode.Parse(capture.Body!)!;
                var revision = root["gateway"]!["revision"]!.GetValue<int>();
                var created = !days.TryGetValue(key, out var stored);
                if (created || revision > stored.Revision)
                    days[key] = (revision, root["itemOrder"]!.AsArray().Select(entry => entry!["eventKey"]!.GetValue<string>()).ToList());
                return Json(created ? HttpStatusCode.Created : HttpStatusCode.OK, JsonSerializer.Serialize(new
                {
                    postId = 1, url = $"{BaseUrl}/news/{key}/", status = "private", revision = days[key].Revision
                }));
            }
            case ("PUT", "items"):
            {
                var root = JsonNode.Parse(capture.Body!)!;
                var revision = root["gateway"]!["revision"]!.GetValue<int>();
                var image = root["image"]?["url"]?.GetValue<string>();
                var created = !items.TryGetValue(key, out var stored);
                var result = "unchanged";
                if (created || revision > stored.Revision)
                {
                    result = image is null ? "none" : !created && stored.Image == image ? "unchanged"
                        : image.Contains("broken", StringComparison.Ordinal) ? "failed" : "attached";
                    items[key] = (revision, image, created ? ++nextPostId : stored.PostId);
                }
                var issueDate = root["issueDate"]!.GetValue<string>();
                return Json(created ? HttpStatusCode.Created : HttpStatusCode.OK, JsonSerializer.Serialize(new
                {
                    postId = items[key].PostId, url = $"{BaseUrl}/news/{issueDate}/{key}/", status = "private",
                    revision = items[key].Revision, image = result, imageError = result == "failed" ? "http_404" : null
                }));
            }
            case ("DELETE", "items"):
                return items.Remove(key) ? Json(HttpStatusCode.OK, "{\"deleted\":true}") : Json(HttpStatusCode.NotFound, "{\"code\":\"not_found\"}");
            case ("GET", "days"):
            {
                if (!days.TryGetValue(key, out var day)) return Json(HttpStatusCode.NotFound, "{\"code\":\"not_found\"}");
                var missing = day.Order.Where(eventKey => !items.ContainsKey(eventKey)).ToList();
                return Json(HttpStatusCode.OK, JsonSerializer.Serialize(new
                {
                    postId = 1, url = $"{BaseUrl}/news/{key}/", status = "private", revision = day.Revision,
                    expectedItems = day.Order.Count, publishedItems = day.Order.Count - missing.Count, missing
                }));
            }
            default:
                return Json(HttpStatusCode.NotFound, "{\"code\":\"rest_no_route\"}");
        }
    }

    public static HttpResponseMessage Json(HttpStatusCode status, string json)
        => new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
}
