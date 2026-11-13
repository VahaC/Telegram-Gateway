using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using AgentGateway.Core.Abstractions;
using AgentGateway.Core.Options;

namespace AgentGateway.Infrastructure.WordPress;

internal sealed class WordPressNewsClient(IHttpClientFactory clients, IOptions<NewsOptions> options) : IWordPressNewsClient
{
    public const string HttpClientName = "WordPress";
    // Day, delete and verify calls are small; item calls make WordPress download the image first.
    private static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(30);

    public Task<WordPressCallResult<WordPressPostResult>> PutDayAsync(DateOnly date, string body, CancellationToken cancellationToken)
        => SendAsync(HttpMethod.Put, "days/" + date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), body, CallTimeout, ReadPost, cancellationToken);

    public Task<WordPressCallResult<WordPressPostResult>> PutItemAsync(string eventKey, string body, CancellationToken cancellationToken)
        => SendAsync(HttpMethod.Put, "items/" + Uri.EscapeDataString(eventKey), body,
            TimeSpan.FromSeconds(options.Value.ItemTimeoutSeconds), ReadPost, cancellationToken);

    public async Task<WordPressCallResult<bool>> DeleteItemAsync(string eventKey, string body, CancellationToken cancellationToken)
    {
        var result = await SendAsync(HttpMethod.Delete, "items/" + Uri.EscapeDataString(eventKey), body, CallTimeout,
            root => root.TryGetProperty("deleted", out var deleted) && deleted.ValueKind == JsonValueKind.True ? "deleted" : null, cancellationToken);
        // The contract: a post that is already gone is a finished removal.
        return result.Succeeded || result.ErrorCode == "wp_404"
            ? new(WordPressCallOutcome.Succeeded, true)
            : new(result.Outcome, false, result.ErrorCode);
    }

    public Task<WordPressCallResult<WordPressDayStatus>> GetDayAsync(DateOnly date, CancellationToken cancellationToken)
        => SendAsync(HttpMethod.Get, "days/" + date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), null, CallTimeout, ReadDay, cancellationToken);

    private async Task<WordPressCallResult<T>> SendAsync<T>(HttpMethod method, string path, string? body, TimeSpan timeout,
        Func<JsonElement, T?> read, CancellationToken cancellationToken) where T : class
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        try
        {
            var settings = options.Value;
            using var request = new HttpRequestMessage(method, path);
            request.Headers.Authorization = new("Basic",
                Convert.ToBase64String(Encoding.UTF8.GetBytes(settings.WordPressUser + ":" + settings.WordPressAppPassword)));
            if (body is not null) request.Content = new StringContent(body, Encoding.UTF8, "application/json");
            using var response = await clients.CreateClient(HttpClientName).SendAsync(request, timeoutSource.Token);
            var status = (int)response.StatusCode;
            if (status >= 500) return new(WordPressCallOutcome.Retryable, ErrorCode: "wp_5xx");
            // 408 and 429 are the only 4xx answers that can change on their own; every other 4xx repeats forever.
            if (status is 408 or 429) return new(WordPressCallOutcome.Retryable, ErrorCode: $"wp_{status}");
            if (status >= 400) return new(WordPressCallOutcome.PermanentFailure, ErrorCode: $"wp_{status}");
            if (response.StatusCode is not (HttpStatusCode.OK or HttpStatusCode.Created))
                return new(WordPressCallOutcome.Retryable, ErrorCode: "wp_invalid_response");
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeoutSource.Token));
            var value = document.RootElement.ValueKind == JsonValueKind.Object ? read(document.RootElement) : null;
            // The writes are upserts, so a malformed success answer is retried rather than trusted.
            return value is null ? new(WordPressCallOutcome.Retryable, ErrorCode: "wp_invalid_response")
                : new(WordPressCallOutcome.Succeeded, value);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new(WordPressCallOutcome.Retryable, ErrorCode: "wp_timeout");
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or InvalidOperationException)
        {
            // Never keep the exception text: it can carry the request URI or vendor details.
            return new(WordPressCallOutcome.Retryable, ErrorCode: exception is JsonException ? "wp_invalid_response" : "wp_network");
        }
    }

    private static WordPressPostResult? ReadPost(JsonElement root)
    {
        if (!TryGetInt64(root, "postId", out var postId) || !TryGetUrl(root, "url", out var url)
            || !TryGetInt32(root, "revision", out var revision))
            return null;
        return new(postId, url, GetString(root, "status") ?? "", revision, GetString(root, "image"), GetString(root, "imageError"));
    }

    private static WordPressDayStatus? ReadDay(JsonElement root)
    {
        if (!TryGetInt64(root, "postId", out var postId) || !TryGetUrl(root, "url", out var url)
            || !TryGetInt32(root, "revision", out var revision) || !TryGetInt32(root, "expectedItems", out var expected)
            || !TryGetInt32(root, "publishedItems", out var published)
            || !root.TryGetProperty("missing", out var list) || list.ValueKind != JsonValueKind.Array)
            return null;
        var missing = list.EnumerateArray().Where(entry => entry.ValueKind == JsonValueKind.String)
            .Select(entry => entry.GetString()!).ToList();
        return new(postId, url, GetString(root, "status") ?? "", revision, expected, published, missing);
    }

    private static bool TryGetInt64(JsonElement root, string name, out long value)
    {
        value = 0;
        return root.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.Number && property.TryGetInt64(out value);
    }

    private static bool TryGetInt32(JsonElement root, string name, out int value)
    {
        value = 0;
        return root.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out value);
    }

    private static bool TryGetUrl(JsonElement root, string name, out string value)
    {
        // The day URL goes into a Telegram message verbatim, so only an absolute web URL without credentials is accepted.
        value = GetString(root, name) ?? "";
        return value.Length is > 0 and <= 2000 && Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && uri.Scheme is "https" or "http" && uri.UserInfo.Length == 0;
    }

    private static string? GetString(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var property) || property.ValueKind != JsonValueKind.String) return null;
        var text = property.GetString()!;
        return text.Length > 2000 ? text[..2000] : text;
    }
}
