using System.Text.Json;
using System.Text.RegularExpressions;
using AgentGateway.Core.News;

namespace AgentGateway.Api.Services.News;

/// <summary>
/// Reads and checks the feed catalog. A bad feed is skipped with a reason and the others still load, so one typo in a
/// 250-feed file does not stop the whole collector.
/// </summary>
public static partial class NewsFeedCatalogLoader
{
    public const int SupportedVersion = 1;
    public const int MaxIdLength = 60;

    // Every field is required, so a misspelt field already fails its check; unknown extra fields are ignored rather than
    // refusing an otherwise good catalog. Comments and trailing commas are allowed in a hand-edited file.
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true
    };

    public static NewsFeedCatalogLoadResult Load(string path)
    {
        NewsFeedCatalogDocument? document;
        try
        {
            using var stream = File.OpenRead(path);
            document = JsonSerializer.Deserialize<NewsFeedCatalogDocument>(stream, Options);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            // The JSON message names the path and line of the problem, which is what the owner has to fix.
            return Unreadable($"The feed catalog '{path}' cannot be read: {exception.Message}");
        }
        if (document?.Version != SupportedVersion)
            return Unreadable($"The feed catalog '{path}' has version {document?.Version.ToString() ?? "none"}; this gateway reads version {SupportedVersion}.");
        if (document.Feeds is null) return Unreadable($"The feed catalog '{path}' has no 'feeds' array.");
        var feeds = new List<NewsFeedCatalogFeed>();
        var errors = new List<string>();
        var identifiers = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < document.Feeds.Count; index++)
        {
            var feed = document.Feeds[index];
            var problems = feed is null ? ["the entry is null"] : Check(feed);
            if (feed is not null && problems.Count == 0 && !identifiers.Add(feed.Id!)) problems.Add($"id '{feed.Id}' is already used by an earlier feed");
            if (problems.Count > 0) errors.Add($"feeds[{index}] ({feed?.Id ?? "no id"}): {string.Join("; ", problems)}.");
            else feeds.Add(feed!);
        }
        return new(true, feeds, errors);
    }

    private static List<string> Check(NewsFeedCatalogFeed feed)
    {
        var problems = new List<string>();
        if (feed.Id is null || feed.Id.Length > MaxIdLength || !KebabCase().IsMatch(feed.Id))
            problems.Add($"id must be kebab-case (a-z, 0-9, single hyphens), at most {MaxIdLength} characters");
        if (string.IsNullOrWhiteSpace(feed.Name) || feed.Name.Length > 100) problems.Add("name must have 1 to 100 characters");
        if (!Uri.TryCreate(feed.Url, UriKind.Absolute, out var url) || url.Scheme != Uri.UriSchemeHttps || url.UserInfo.Length > 0
            || feed.Url!.Length > 2000)
            problems.Add("url must be an absolute https URL without credentials");
        if (feed.Categories is not { Count: >= 1 and <= 3 } categories || categories.Distinct().Count() != categories.Count
            || categories.Any(category => !NewsCategories.All.Contains(category)))
            problems.Add($"categories must be 1 to 3 different values of {string.Join(", ", NewsCategories.All)}");
        if (!NewsFeedKinds.All.Contains(feed.Kind)) problems.Add($"kind must be one of {string.Join(", ", NewsFeedKinds.All)}");
        if (feed.Language is null || !LanguageCode().IsMatch(feed.Language)) problems.Add("language must be an ISO 639-1 code such as en or uk");
        return problems;
    }

    private static NewsFeedCatalogLoadResult Unreadable(string reason) => new(false, [], [reason]);

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex KebabCase();

    [GeneratedRegex("^[a-z]{2}$")]
    private static partial Regex LanguageCode();
}
