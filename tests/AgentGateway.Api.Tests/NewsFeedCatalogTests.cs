using AgentGateway.Api.Services.News;
using AgentGateway.Core.Options;
using AgentGateway.TestInfrastructure.News;

namespace AgentGateway.Api.Tests;

/// <summary>The catalog loader (a file parser, not an endpoint) and the real catalog that ships with the gateway.</summary>
public sealed class NewsFeedCatalogTests
{
    [Fact]
    public void Load_real_catalog_passes_validation_and_ships_with_the_build()
    {
        //Arrange
        var path = RepositoryFiles.PathOf("src/AgentGateway.Api/NewsFeeds/news-feeds.json");
        //Act
        var catalog = NewsFeedCatalogLoader.Load(path);
        //Assert
        Assert.True(catalog.Readable, string.Join(Environment.NewLine, catalog.Errors));
        Assert.True(catalog.Errors.Count == 0, string.Join(Environment.NewLine, catalog.Errors));
        Assert.NotEmpty(catalog.Feeds);
        // The default relative path resolves into the build output, the same place the Docker image has it (/app/NewsFeeds).
        Assert.True(File.Exists(NewsFeedCatalogSync.ResolvePath(new NewsOptions().FeedCatalogPath)));
    }

    [Fact]
    public void Load_reports_every_invalid_feed_and_keeps_the_valid_ones()
    {
        //Arrange
        var directory = Path.Combine(Path.GetTempPath(), "agent-gateway-catalog-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "news-feeds.json");
        Directory.CreateDirectory(directory);
        File.WriteAllText(path, """
            {
              "version": 1,
              // Comments and trailing commas are allowed in the hand-edited file.
              "feeds": [
                { "id": "good-feed", "name": "Good", "url": "https://good.example/feed", "categories": ["hardware", "smarthome"], "kind": "news", "language": "en" },
                { "id": "Bad_Id", "name": "Bad id", "url": "https://bad.example/feed", "categories": ["ai"], "kind": "blog", "language": "en" },
                { "id": "plain-http", "name": "Plain", "url": "http://plain.example/feed", "categories": ["ai"], "kind": "blog", "language": "en" },
                { "id": "too-many", "name": "Many", "url": "https://many.example/feed", "categories": ["ai", "dotnet", "homelab", "security"], "kind": "news", "language": "en" },
                { "id": "unknown-category", "name": "Gaming", "url": "https://gaming.example/feed", "categories": ["gaming"], "kind": "news", "language": "en" },
                { "id": "bad-kind", "name": "Kind", "url": "https://kind.example/feed", "categories": ["ai"], "kind": "podcast", "language": "eng" },
                { "id": "good-feed", "name": "Copy", "url": "https://copy.example/feed", "categories": ["ai"], "kind": "news", "language": "en" },
              ]
            }
            """);
        try
        {
            //Act
            var catalog = NewsFeedCatalogLoader.Load(path);
            //Assert
            Assert.True(catalog.Readable);
            Assert.Equal("good-feed", Assert.Single(catalog.Feeds).Id);
            Assert.Equal(6, catalog.Errors.Count);
            Assert.Contains(catalog.Errors, error => error.StartsWith("feeds[1] (Bad_Id): id must be kebab-case", StringComparison.Ordinal));
            Assert.Contains(catalog.Errors, error => error.StartsWith("feeds[2] (plain-http): url must be an absolute https URL", StringComparison.Ordinal));
            Assert.Contains(catalog.Errors, error => error.StartsWith("feeds[3] (too-many): categories must be 1 to 3", StringComparison.Ordinal));
            Assert.Contains(catalog.Errors, error => error.StartsWith("feeds[4] (unknown-category): categories", StringComparison.Ordinal));
            Assert.Contains(catalog.Errors, error => error.StartsWith("feeds[5] (bad-kind): kind must be one of", StringComparison.Ordinal)
                && error.Contains("language must be an ISO 639-1 code", StringComparison.Ordinal));
            Assert.Contains(catalog.Errors, error => error == "feeds[6] (good-feed): id 'good-feed' is already used by an earlier feed.");
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Theory]
    [InlineData(null, "cannot be read")]
    [InlineData("{ \"version\": 1, \"feeds\": [ ", "cannot be read")]
    [InlineData("{ \"version\": 2, \"feeds\": [] }", "has version 2; this gateway reads version 1")]
    [InlineData("{ \"version\": 1 }", "has no 'feeds' array")]
    public void Load_missing_malformed_or_foreign_file_is_unreadable(string? content, string reason)
    {
        //Arrange
        var directory = Path.Combine(Path.GetTempPath(), "agent-gateway-catalog-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "news-feeds.json");
        Directory.CreateDirectory(directory);
        if (content is not null) File.WriteAllText(path, content);
        try
        {
            //Act
            var catalog = NewsFeedCatalogLoader.Load(path);
            //Assert
            Assert.False(catalog.Readable);
            Assert.Empty(catalog.Feeds);
            Assert.Contains(reason, Assert.Single(catalog.Errors));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}
