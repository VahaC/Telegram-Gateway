using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace AgentGateway.TestInfrastructure.Http;

/// <summary>
/// A recording fake of the publishers' feed servers: each URL gets its own answer, an unknown URL gets 503. It also
/// records how many requests to one host were in flight at once, to prove the poller's per-host politeness.
/// </summary>
public sealed class FakeFeedHandler : HttpMessageHandler
{
    private readonly ConcurrentDictionary<string, Func<FeedRequestCapture, HttpResponseMessage>> routes = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, int> inFlight = new(StringComparer.Ordinal);
    private int maxInFlightPerHost;

    public ConcurrentQueue<FeedRequestCapture> Requests { get; } = new();

    /// <summary>Keeps every request open this long, so overlapping requests to one host would be visible.</summary>
    public TimeSpan Delay { get; set; }

    public int MaxInFlightPerHost => Volatile.Read(ref maxInFlightPerHost);

    public void Serve(string url, Func<FeedRequestCapture, HttpResponseMessage> respond) => routes[url] = respond;

    /// <summary>Serves the XML with an ETag, and answers 304 when the request carries that ETag back.</summary>
    public void ServeXml(string url, string xml, string? eTag = null) => Serve(url, request => eTag is not null && request.IfNoneMatch == eTag
        ? new HttpResponseMessage(HttpStatusCode.NotModified) : Xml(xml, eTag));

    public void ServeStatus(string url, HttpStatusCode status) => Serve(url, _ => new HttpResponseMessage(status) { Content = new StringContent("refused") });

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var url = request.RequestUri!.AbsoluteUri;
        var capture = new FeedRequestCapture(url, request.Headers.UserAgent.ToString(),
            request.Headers.TryGetValues("If-None-Match", out var tags) ? string.Join(",", tags) : null,
            request.Headers.TryGetValues("If-Modified-Since", out var dates) ? string.Join(",", dates) : null);
        Requests.Enqueue(capture);
        var host = request.RequestUri.Host;
        var current = inFlight.AddOrUpdate(host, 1, (_, count) => count + 1);
        lock (inFlight) maxInFlightPerHost = Math.Max(maxInFlightPerHost, current);
        try
        {
            if (Delay > TimeSpan.Zero) await Task.Delay(Delay, cancellationToken);
            var response = routes.TryGetValue(url, out var respond) ? respond(capture) : new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            response.RequestMessage = request;
            return response;
        }
        finally
        {
            inFlight.AddOrUpdate(host, 0, (_, count) => count - 1);
        }
    }

    public static HttpResponseMessage Xml(string xml, string? eTag = null)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(xml, Encoding.UTF8, "application/rss+xml") };
        if (eTag is not null) response.Headers.ETag = EntityTagHeaderValue.Parse(eTag);
        return response;
    }
}
