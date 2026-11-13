using System.ComponentModel.DataAnnotations;

namespace AgentGateway.Api.Contracts.News;

public sealed record NewsResearchRequest(
    [property: MaxLength(100)] string? Mode,
    int? SearchPasses,
    int? Candidates,
    int? DuplicatesRemoved,
    int? WindowHours);
