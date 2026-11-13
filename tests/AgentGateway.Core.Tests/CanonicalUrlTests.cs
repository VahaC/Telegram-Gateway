using AgentGateway.Core.News;

namespace AgentGateway.Core.Tests;

public sealed class CanonicalUrlTests
{
    [Theory]
    [InlineData("https://example.com/news/item", "https://example.com/news/item")]
    [InlineData("HTTPS://Example.COM/News/Item", "https://example.com/News/Item")]
    [InlineData("http://example.com/a", "https://example.com/a")]
    [InlineData("https://www.example.com/a", "https://example.com/a")]
    [InlineData("https://www.Example.com/a", "https://example.com/a")]
    [InlineData("https://example.com:443/a", "https://example.com/a")]
    [InlineData("http://example.com:80/a", "https://example.com/a")]
    [InlineData("https://example.com:8443/a", "https://example.com:8443/a")]
    [InlineData("https://example.com/a#section-2", "https://example.com/a")]
    [InlineData("https://example.com/a/", "https://example.com/a")]
    [InlineData("https://example.com/", "https://example.com/")]
    [InlineData("https://example.com", "https://example.com/")]
    [InlineData("https://example.com/a?utm_source=x&utm_medium=y&utm_campaign=z", "https://example.com/a")]
    [InlineData("https://example.com/a?UTM_Source=x", "https://example.com/a")]
    [InlineData("https://example.com/a?fbclid=1&gclid=2&mc_cid=3&mc_eid=4&ref=5&source=6", "https://example.com/a")]
    [InlineData("https://example.com/a?b=2&a=1", "https://example.com/a?a=1&b=2")]
    [InlineData("https://example.com/a?id=7&utm_source=rss", "https://example.com/a?id=7")]
    [InlineData("https://example.com/a?referrer=1", "https://example.com/a?referrer=1")]
    [InlineData("http://www.example.com:80/a/?utm_source=x&z=1&b=2#top", "https://example.com/a?b=2&z=1")]
    public void Normalize_contract_cases_produce_one_canonical_form(string input, string expected)
    {
        //Arrange
        //Act
        var canonical = CanonicalUrl.Normalize(input);
        //Assert
        Assert.Equal(expected, canonical);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a url")]
    [InlineData("/relative/path")]
    [InlineData("ftp://example.com/file")]
    [InlineData(null)]
    public void Normalize_non_web_urls_returns_null(string? input)
    {
        //Arrange
        //Act
        var canonical = CanonicalUrl.Normalize(input);
        //Assert
        Assert.Null(canonical);
    }

    [Fact]
    public void Normalize_tracking_variants_of_one_article_are_equal()
    {
        //Arrange
        var variants = new[]
        {
            "https://www.proxmox.com/en/news/press-releases/proxmox-ve-9-1",
            "http://proxmox.com/en/news/press-releases/proxmox-ve-9-1/?utm_source=newsletter",
            "https://proxmox.com:443/en/news/press-releases/proxmox-ve-9-1#download"
        };
        //Act
        var canonical = variants.Select(CanonicalUrl.Normalize).Distinct().ToList();
        //Assert
        Assert.Single(canonical);
    }
}
