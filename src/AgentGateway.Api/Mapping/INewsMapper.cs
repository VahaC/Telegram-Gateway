using AgentGateway.Api.Contracts;
using AgentGateway.Api.Contracts.News;
using AgentGateway.Core.Entities;

namespace AgentGateway.Api.Mapping;

public interface INewsMapper
{
    /// <summary>Copies the issue fields and creates fresh item, release and alert rows (publication state starts at Pending).</summary>
    void ApplyContent(NewsIssueEntity issue, NewsIssueRequest request, DateTime nowUtc);

    NewsIssueStatusResponse ToResponse(NewsIssueEntity issue, DeliveryResponse? notice);
}
