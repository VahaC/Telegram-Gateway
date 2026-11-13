namespace AgentGateway.Core.Enums;

/// <summary>State of one tracked WordPress call: an item upsert, or a pending post removal.</summary>
public enum NewsItemStatus
{
    Pending = 0,
    Published = 1,
    Failed = 2
}
