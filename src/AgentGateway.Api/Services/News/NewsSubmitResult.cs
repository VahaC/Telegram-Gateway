using AgentGateway.Api.Contracts.News;

namespace AgentGateway.Api.Services.News;

/// <summary>
/// Outcome of a news write. Invalid lists structural Errors (400), Rejected lists editorial Violations (422),
/// Duplicate lists registry Duplicates (409); Issue is the stored issue's status when there is one.
/// </summary>
public sealed record NewsSubmitResult(
    NewsSubmitStatus Status,
    NewsIssueStatusResponse? Issue = null,
    string? Error = null,
    IReadOnlyList<string>? Errors = null,
    IReadOnlyList<string>? Violations = null,
    IReadOnlyList<NewsDuplicateResponse>? Duplicates = null);
