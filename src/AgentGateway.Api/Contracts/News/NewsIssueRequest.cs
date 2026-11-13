using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace AgentGateway.Api.Contracts.News;

/// <summary>News issue contract v1, shared with the WordPress theme (docs/prompts/news-module.md).</summary>
public sealed record NewsIssueRequest(
    [property: Description("Always 1.")] int ContractVersion,
    [property: Description("Issue date, yyyy-MM-dd, the Europe/Kyiv calendar day. One issue per date.")] DateOnly Date,
    [property: Required, MaxLength(200)] string Title,
    [property: Required, MaxLength(16), Description("Language code, e.g. uk.")] string Language,
    [property: MaxLength(1000), Description("Optional plain-text intro.")] string? Intro,
    [property: Required, Description("1..80 news items numbered 1..N; the TOP-5 are numbers 1..5.")] IReadOnlyList<NewsItemRequest> Items,
    IReadOnlyList<NewsReleaseRequest>? Releases = null,
    IReadOnlyList<NewsSecurityAlertRequest>? SecurityAlerts = null,
    NewsResearchRequest? Research = null);
