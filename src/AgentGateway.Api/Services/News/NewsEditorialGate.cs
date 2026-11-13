using System.Globalization;
using Microsoft.Extensions.Options;
using AgentGateway.Api.Contracts.News;
using AgentGateway.Core.News;
using AgentGateway.Core.Options;

namespace AgentGateway.Api.Services.News;

/// <summary>
/// The editorial rules of an issue (422). Each violation starts with a stable code so a caller can act on it,
/// followed by a sentence naming the values the gateway saw.
/// </summary>
public sealed class NewsEditorialGate(IOptions<NewsOptions> options)
{
    private const int TopCount = 5;

    public IReadOnlyList<string> Check(NewsIssueRequest issue)
    {
        var settings = options.Value;
        var items = issue.Items;
        var violations = new List<string>();
        if (items.Count < settings.MinItems)
            violations.Add($"min_items: The issue has {items.Count} news items; at least {settings.MinItems} are required.");
        if (items.Count > settings.MaxItems)
            violations.Add($"max_items: The issue has {items.Count} news items; at most {settings.MaxItems} are allowed.");
        foreach (var category in NewsCategories.All)
        {
            var count = items.Count(item => item.Category == category);
            if (count < settings.MinPerCategory)
                violations.Add($"category_min: Category '{category}' has {count} items; at least {settings.MinPerCategory} are required.");
        }
        var ai = items.Count(item => item.Category == NewsCategories.Ai);
        // A tiny tolerance keeps an exact 30% from failing on floating-point rounding.
        if (items.Count > 0 && (double)ai / items.Count > settings.MaxAiShare + 1e-9)
            violations.Add(string.Create(CultureInfo.InvariantCulture,
                $"ai_share: {ai} of {items.Count} items ({100.0 * ai / items.Count:0.#}%) are 'ai'; at most {settings.MaxAiShare * 100:0.#}% are allowed."));
        var numbers = items.Select(item => item.Number).Order().ToList();
        if (!numbers.SequenceEqual(Enumerable.Range(1, items.Count)))
            violations.Add($"numbering: Item numbers must be 1..{items.Count} without gaps or repeats; found {Describe(numbers)}.");
        var expectedTop = Math.Min(TopCount, items.Count);
        var top = items.Where(item => item.IsTop).Select(item => item.Number).Order().ToList();
        if (top.Count != expectedTop)
            violations.Add($"top_count: The issue has {top.Count} TOP items; exactly {expectedTop} are required.");
        if (top.Any(number => number > expectedTop))
            violations.Add($"top_numbering: TOP items must be numbers 1..{expectedTop}; found {Describe(top)}.");
        return violations;
    }

    private static string Describe(IReadOnlyCollection<int> numbers) => numbers.Count == 0 ? "none" : string.Join(", ", numbers);
}
