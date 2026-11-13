using System.Globalization;
using AgentGateway.Api.Contracts.News;

namespace AgentGateway.TestInfrastructure.News;

/// <summary>Valid issues for the default editorial gate: every category present, AI well under 30%, TOP-5 numbered 1..5.</summary>
public static class NewsIssueDataFactory
{
    private static readonly string[] Categories = ["dotnet", "homelab", "smarthome", "hardware", "security", "ai"];

    public static NewsIssueRequest Issue(DateOnly date, int count = 38, string prefix = "story") => new(
        1, date, $"Техдайджест {date.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture)}", "uk", "Тестовий випуск.",
        Enumerable.Range(1, count).Select(number => Item(date, number, prefix)).ToList(),
        [
            new($"Release {prefix}", "1.0.0", date, $"https://releases.example/{prefix}/{date:yyyyMMdd}/1.0.0", "Виправлення."),
            new($"Second {prefix}", "2.0.0", date, $"https://releases.example/{prefix}/{date:yyyyMMdd}/2.0.0", null)
        ],
        [new($"CVE-{date:yyyy}-{date.DayOfYear:0000}{prefix.Length}", "Product", "high", 8.1, date, $"https://cve.example/{prefix}/{date:yyyyMMdd}", "Оновіть.")],
        new("test", 3, 50, 4, 48));

    public static NewsItemRequest Item(DateOnly date, int number, string prefix = "story") => new(
        number, Categories[(number - 1) % Categories.Length], number <= 5, number <= 5 ? 5 : 3,
        $"Новина {number}", $"Перший абзац новини {number}.\n\nДругий абзац.", "Бо це цікаво.", date, null,
        "stable", "1.0", $"{prefix}-{date:yyyyMMdd}-{number}",
        new($"https://news.example/{prefix}/{date:yyyyMMdd}/{number}", "News Example"),
        [new($"https://confirm.example/{prefix}/{date:yyyyMMdd}/{number}", "Confirm Example")],
        new($"https://images.example/{prefix}/{number}.jpg", $"Ілюстрація {number}"),
        ["test", Categories[(number - 1) % Categories.Length]]);

    public static NewsIssueRequest WithItems(this NewsIssueRequest issue, Func<NewsItemRequest, NewsItemRequest> change)
        => issue with { Items = issue.Items.Select(change).ToList() };
}
