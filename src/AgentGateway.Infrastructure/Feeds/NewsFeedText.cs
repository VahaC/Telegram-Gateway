using System.Net;
using System.Text.RegularExpressions;

namespace AgentGateway.Infrastructure.Feeds;

/// <summary>Turns the HTML that feeds carry in titles, descriptions and content into plain text, and finds an https picture in it.</summary>
internal static partial class NewsFeedText
{
    public static string ToPlainText(string? html, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(html)) return "";
        var text = Hidden().Replace(html, " ");
        // Block tags separate words; inline tags sit inside words (<b>Pro</b>xmox) and must not add a space.
        text = BlockTag().Replace(text, " ");
        text = AnyTag().Replace(text, "");
        text = WebUtility.HtmlDecode(text);
        // Some feeds escape their HTML twice; after one decode the markup is visible text and goes the same way.
        text = AnyTag().Replace(text, "");
        text = Whitespace().Replace(text, " ").Trim();
        return Truncate(text, maxLength);
    }

    /// <summary>Cuts at a character boundary (never inside a surrogate pair) and marks the cut with an ellipsis.</summary>
    public static string Truncate(string text, int maxLength)
    {
        if (text.Length <= maxLength) return text;
        var length = maxLength - 1;
        if (char.IsHighSurrogate(text[length - 1])) length--;
        return text[..length].TrimEnd() + "…";
    }

    /// <summary>The first img in the markup that is not a tracking pixel, resolved against <paramref name="baseUri"/>.</summary>
    public static string? FirstImage(string? html, Uri baseUri)
    {
        if (string.IsNullOrEmpty(html)) return null;
        foreach (Match match in Image().Matches(html))
        {
            if (Pixel().IsMatch(match.Value)) continue;
            if (HttpsUrl(WebUtility.HtmlDecode(match.Groups["src"].Value), baseUri) is { } url) return url;
        }
        return null;
    }

    /// <summary>
    /// An absolute https URL without credentials, or null. Plain http is refused: the agent passes the picture on to the
    /// site, which only loads https images.
    /// </summary>
    public static string? HttpsUrl(string? value, Uri baseUri)
    {
        if (string.IsNullOrWhiteSpace(value) || !Uri.TryCreate(baseUri, value.Trim(), out var uri)) return null;
        return uri.Scheme == Uri.UriSchemeHttps && uri.UserInfo.Length == 0 && uri.AbsoluteUri.Length <= 2000 ? uri.AbsoluteUri : null;
    }

    [GeneratedRegex(@"<(script|style)\b[^>]*>.*?</\1\s*>|<!--.*?-->", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex Hidden();

    [GeneratedRegex(@"</?(p|div|br|hr|li|ul|ol|h[1-6]|blockquote|pre|table|tr|td|th|section|article|figure|figcaption|header|footer)\b[^>]*>",
        RegexOptions.IgnoreCase)]
    private static partial Regex BlockTag();

    // A tag name starts right after '<', so text such as "a < b" survives.
    [GeneratedRegex(@"</?[a-zA-Z][^<>]*>")]
    private static partial Regex AnyTag();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"<img\b[^>]*?\bsrc\s*=\s*(?:""(?<src>[^""]*)""|'(?<src>[^']*)'|(?<src>[^\s>]+))[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex Image();

    [GeneratedRegex(@"\b(width|height)\s*=\s*[""']?1[""'\s/>]", RegexOptions.IgnoreCase)]
    private static partial Regex Pixel();
}
