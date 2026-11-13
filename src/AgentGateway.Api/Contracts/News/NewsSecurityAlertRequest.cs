using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace AgentGateway.Api.Contracts.News;

public sealed record NewsSecurityAlertRequest(
    [property: Required, MaxLength(50), Description("For example CVE-2026-12345.")] string Id,
    [property: Required, MaxLength(100)] string Product,
    [property: Required, Description("critical, high, medium or low.")] string Severity,
    double? Cvss,
    DateOnly Date,
    [property: Required, MaxLength(2000)] string Url,
    [property: MaxLength(300)] string? Note);
