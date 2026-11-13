using System.Text.Json.Serialization;

namespace AgentGateway.Api.Contracts.News;

/// <summary>One clash. Path points into the submitted issue; the rest names the item it repeats.</summary>
public sealed record NewsDuplicateResponse(
    string Path,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? CanonicalUrl,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? EventKey,
    DateOnly IssueDate,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? ItemNumber,
    string Title);
