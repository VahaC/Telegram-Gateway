namespace AgentGateway.Api.Services.News;

public enum NewsSubmitStatus
{
    Accepted = 0,
    Existing = 1,
    Invalid = 2,
    Rejected = 3,
    Duplicate = 4,
    Conflict = 5,
    NotFound = 6
}
