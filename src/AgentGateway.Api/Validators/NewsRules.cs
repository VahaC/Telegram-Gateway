using System.Text.RegularExpressions;

namespace AgentGateway.Api.Validators;

/// <summary>Field rules of news contract v1 that more than one validator needs.</summary>
public static partial class NewsRules
{
    public static readonly string[] ReleaseStatuses = ["stable", "beta", "preview", "rc", "rfc", "announced", "research", "incident", "other"];
    public static readonly string[] Severities = ["critical", "high", "medium", "low"];

    public static bool IsWebUrl(string? value, bool httpsOnly) => value is { Length: > 0 and <= 2000 }
        && Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.UserInfo.Length == 0 && uri.Host.Length > 0
        && (uri.Scheme == "https" || !httpsOnly && uri.Scheme == "http");

    /// <summary>The theme prints these texts as plain text, so markup would show up literally on the site.</summary>
    public static bool IsPlainText(string? value) => value is null || !Markup().IsMatch(value);

    public static bool IsEventKey(string? value) => value is { Length: >= 3 and <= 100 } && EventKey().IsMatch(value);

    [GeneratedRegex(@"<\s*/?\s*[a-zA-Z!][^>]*>|\]\(|\*\*|__|^\s{0,3}#{1,6}\s", RegexOptions.Multiline)]
    private static partial Regex Markup();

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex EventKey();
}
