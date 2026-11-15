namespace AgentGateway.Api.Services.News;

public enum NewsFeedQueryStatus
{
    /// <summary>The value is attached.</summary>
    Ok = 0,
    /// <summary>A parameter is out of range or the cursor is not one the gateway issued; Error names it.</summary>
    Invalid = 1,
    /// <summary>The feed collector is switched off (News:FeedsEnabled=false).</summary>
    Disabled = 2,
    /// <summary>No entry with that id (entries leave the table 14 days after the gateway first saw them).</summary>
    NotFound = 3
}
