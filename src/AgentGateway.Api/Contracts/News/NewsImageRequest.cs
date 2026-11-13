using System.ComponentModel.DataAnnotations;

namespace AgentGateway.Api.Contracts.News;

public sealed record NewsImageRequest(
    [property: Required, MaxLength(2000)] string Url,
    [property: MaxLength(200)] string? Alt);
