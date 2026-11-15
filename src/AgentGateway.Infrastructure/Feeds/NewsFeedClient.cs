using System.Net;
using AgentGateway.Core.Abstractions;

namespace AgentGateway.Infrastructure.Feeds;

/// <summary>
/// Adapter over publishers' feeds (ADR 0004). One plain GET per feed and poll, conditional on the last validators; any
/// non-success answer is a recorded failure. The URLs come from the committed catalog, not from callers, so no SSRF guard
/// is needed here.
/// </summary>
internal sealed class NewsFeedClient(IHttpClientFactory clients) : INewsFeedClient
{
    public const string HttpClientName = "NewsFeed";
    /// <summary>
    /// Most feeds are tens to hundreds of KiB, but full-history changelogs run to several MiB (Cloudflare's about 9, CISA's 5.5);
    /// 16 MiB keeps those and still stops a wrong URL early.
    /// </summary>
    public const int MaxResponseBytes = 16 * 1024 * 1024;
    /// <summary>Covers the headers and the body; a feed that needs longer is treated as down for this poll.</summary>
    private static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(20);

    public async Task<NewsFeedFetchResult> FetchAsync(string url, string? eTag, string? lastModified, CancellationToken cancellationToken)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(CallTimeout);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            // The validators go back exactly as the publisher sent them; a parsed and re-printed ETag may no longer match.
            if (eTag is not null) request.Headers.TryAddWithoutValidation("If-None-Match", eTag);
            if (lastModified is not null) request.Headers.TryAddWithoutValidation("If-Modified-Since", lastModified);
            using var response = await clients.CreateClient(HttpClientName)
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeoutSource.Token);
            if (response.StatusCode == HttpStatusCode.NotModified) return NewsFeedFetchResult.NotModified;
            // 402/403/429 included: a refusal is the publisher's decision, recorded and never retried in another way.
            if (!response.IsSuccessStatusCode) return NewsFeedFetchResult.Failed($"http_{(int)response.StatusCode}");
            if (response.Content.Headers.ContentLength > MaxResponseBytes) return NewsFeedFetchResult.Failed("too_large");
            using var body = await ReadCappedAsync(response.Content, timeoutSource.Token);
            if (body is null) return NewsFeedFetchResult.Failed("too_large");
            var (entries, error) = NewsFeedParser.Parse(body, response.RequestMessage?.RequestUri ?? new Uri(url));
            if (error is not null) return NewsFeedFetchResult.Failed(error);
            var newTag = response.Headers.ETag?.ToString();
            return new(NewsFeedFetchStatus.Fetched, entries, newTag is { Length: <= 200 } ? newTag : null,
                response.Content.Headers.LastModified?.ToString("R"));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return NewsFeedFetchResult.Failed("timeout");
        }
        catch (HttpRequestException exception)
        {
            // The category (name resolution, TLS, connection) is enough to act on; the exception text is not stored.
            return NewsFeedFetchResult.Failed($"network ({exception.HttpRequestError})");
        }
    }

    /// <summary>Reads at most <see cref="MaxResponseBytes"/>; null when the body is larger (a missing or false Content-Length is not trusted).</summary>
    private static async Task<MemoryStream?> ReadCappedAsync(HttpContent content, CancellationToken cancellationToken)
    {
        await using var source = await content.ReadAsStreamAsync(cancellationToken);
        var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await source.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > MaxResponseBytes)
            {
                await buffer.DisposeAsync();
                return null;
            }
            buffer.Write(chunk, 0, read);
        }
        buffer.Position = 0;
        return buffer;
    }
}
