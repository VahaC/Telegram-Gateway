using System.ComponentModel;

namespace AgentGateway.Api.Contracts.News;

public sealed record NewsCheckRequest(
    [property: Description("Candidate source URLs, up to 200.")] IReadOnlyList<string>? Urls,
    [property: Description("Candidate event keys, up to 200.")] IReadOnlyList<string>? EventKeys);
