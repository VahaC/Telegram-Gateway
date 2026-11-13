using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace AgentGateway.Api.Contracts.News;

public sealed record NewsItemRequest(
    [property: Description("1..N, contiguous and unique in the issue.")] int Number,
    [property: Required, Description("One of dotnet, homelab, smarthome, hardware, security, ai.")] string Category,
    [property: Description("True for exactly the 5 TOP items, numbered 1..5.")] bool IsTop,
    [property: Description("1..5.")] int Importance,
    [property: Required, MaxLength(200)] string Title,
    [property: Required, MaxLength(2000), Description("Plain text; a blank line separates paragraphs. No HTML or Markdown.")] string Summary,
    [property: Required, MaxLength(600), Description("Plain text: why it matters.")] string WhyItMatters,
    [property: Description("Publication date of the source, yyyy-MM-dd.")] DateOnly PublishedOn,
    [property: Description("When the event happened, if it differs, yyyy-MM-dd.")] DateOnly? EventOn,
    [property: Description("stable, beta, preview, rc, rfc, announced, research, incident or other.")] string? ReleaseStatus,
    [property: MaxLength(50)] string? Version,
    [property: Required, MaxLength(100), Description("Names the event, e.g. proxmox-ve-9-1-release; the same event from two sites gets the same key.")] string EventKey,
    [property: Required, Description("The primary, direct URL of the material; never a home, tag or archive page.")] NewsSourceRequest Source,
    [property: Description("0..3 independent confirmations.")] IReadOnlyList<NewsSourceRequest>? ExtraSources,
    [property: Description("The picture of the original material (og:image, twitter:image or the lead image).")] NewsImageRequest? Image,
    [property: Description("0..8 tags, each up to 40 characters.")] IReadOnlyList<string>? Tags);
