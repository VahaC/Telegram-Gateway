namespace AgentGateway.Core.Abstractions;

/// <summary>Adapter for the theme's news REST routes. Every write is an upsert keyed by date/eventKey and revision, so repeating one is safe.</summary>
public interface IWordPressNewsClient
{
    Task<WordPressCallResult<WordPressPostResult>> PutDayAsync(DateOnly date, string body, CancellationToken cancellationToken);
    Task<WordPressCallResult<WordPressPostResult>> PutItemAsync(string eventKey, string body, CancellationToken cancellationToken);
    Task<WordPressCallResult<bool>> DeleteItemAsync(string eventKey, string body, CancellationToken cancellationToken);
    Task<WordPressCallResult<WordPressDayStatus>> GetDayAsync(DateOnly date, CancellationToken cancellationToken);
}
