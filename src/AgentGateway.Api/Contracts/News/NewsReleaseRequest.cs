using System.ComponentModel.DataAnnotations;

namespace AgentGateway.Api.Contracts.News;

public sealed record NewsReleaseRequest(
    [property: Required, MaxLength(100)] string Name,
    [property: Required, MaxLength(50)] string Version,
    DateOnly Date,
    [property: Required, MaxLength(2000)] string Url,
    [property: MaxLength(300)] string? Note);
