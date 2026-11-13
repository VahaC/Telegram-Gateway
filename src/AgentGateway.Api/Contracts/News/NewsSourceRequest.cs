using System.ComponentModel.DataAnnotations;

namespace AgentGateway.Api.Contracts.News;

public sealed record NewsSourceRequest(
    [property: Required, MaxLength(2000)] string Url,
    [property: Required, MaxLength(100)] string Name);
