namespace AgentGateway.TestInfrastructure.Http;

/// <summary>One call the gateway made to the news routes. Path is relative to /wp-json/vahac/v1/news/, e.g. items/some-key.</summary>
public sealed record WordPressRequestCapture(string Method, string Path, string? Body, string? Authorization, int Attempt)
{
    public string Call => $"{Method} {Path}";
}
