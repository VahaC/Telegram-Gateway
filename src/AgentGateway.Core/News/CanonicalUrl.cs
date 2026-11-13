namespace AgentGateway.Core.News;

/// <summary>The dedup key of the news contract: the same material under tracking parameters, www or http is one URL.</summary>
public static class CanonicalUrl
{
    private static readonly HashSet<string> DroppedParameters = new(StringComparer.OrdinalIgnoreCase)
    {
        "fbclid", "gclid", "mc_cid", "mc_eid", "ref", "source"
    };

    /// <summary>Returns the canonical form, or null when the value is not an absolute http(s) URL.</summary>
    public static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || !Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https") || uri.Host.Length == 0)
            return null;
        var host = uri.IdnHost.ToLowerInvariant();
        if (host.StartsWith("www.", StringComparison.Ordinal)) host = host[4..];
        // http and https name the same material, so the canonical form always uses https.
        var port = uri.IsDefaultPort ? "" : ":" + uri.Port;
        var path = uri.AbsolutePath;
        if (path.Length > 1 && path.EndsWith('/')) path = path.TrimEnd('/');
        if (path.Length == 0) path = "/";
        var parameters = uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Where(pair => !IsTracking(pair.Split('=', 2)[0]))
            .Order(StringComparer.Ordinal)
            .ToList();
        var query = parameters.Count == 0 ? "" : "?" + string.Join('&', parameters);
        return $"https://{host}{port}{path}{query}";
    }

    private static bool IsTracking(string name)
    {
        var decoded = Uri.UnescapeDataString(name);
        return decoded.StartsWith("utm_", StringComparison.OrdinalIgnoreCase) || DroppedParameters.Contains(decoded);
    }
}
