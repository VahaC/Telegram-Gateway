using System.Text.Json.Serialization;

namespace AgentGateway.Api.Contracts.News;

/// <summary>The usual error body plus the exact list the caller has to fix.</summary>
public sealed record NewsRejectionResponse(
    string Error,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<string>? Errors = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<string>? Violations = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<NewsDuplicateResponse>? Duplicates = null);
