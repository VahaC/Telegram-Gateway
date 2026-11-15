using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;
using AgentGateway.Api.Services.News;

namespace AgentGateway.TestInfrastructure.News;

/// <summary>Small fixture feeds and catalogs, so tests never depend on the real 700-feed catalog or the network.</summary>
public static class NewsFeedFixtures
{
    private static readonly XNamespace Media = "http://search.yahoo.com/mrss/";
    private static readonly XNamespace AtomNamespace = "http://www.w3.org/2005/Atom";

    public static NewsFeedCatalogFeed Feed(string id, string url, params string[] categories)
        => new(id, $"Feed {id}", url, categories.Length == 0 ? ["hardware"] : categories, "news", "en");

    /// <summary>Writes <c>{ "version": 1, "feeds": [...] }</c> into the directory and returns the file path.</summary>
    public static string WriteCatalog(string directory, params NewsFeedCatalogFeed[] feeds)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "news-feeds.json");
        File.WriteAllText(path, JsonSerializer.Serialize(new { version = 1, feeds }, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        return path;
    }

    /// <summary>RSS 2.0 with RFC 822 dates and the picture as media:content.</summary>
    public static string Rss(params FeedItemFixture[] items) => new XDocument(new XElement("rss", new XAttribute("version", "2.0"),
        new XAttribute(XNamespace.Xmlns + "media", Media),
        new XElement("channel", new XElement("title", "Fixture feed"), new XElement("link", "https://fixture.example/"),
            items.Select(item => new XElement("item",
                new XElement("title", item.Title),
                new XElement("link", item.Url),
                item.Published is { } published ? new XElement("pubDate", published.ToString("r", CultureInfo.InvariantCulture)) : null,
                new XElement("description", item.Description),
                item.ImageUrl is { } image ? new XElement(Media + "content", new XAttribute("url", image), new XAttribute("medium", "image")) : null)))))
        .ToString();

    /// <summary>Atom 1.0 with ISO 8601 dates and HTML content.</summary>
    public static string Atom(params FeedItemFixture[] items) => new XDocument(new XElement(AtomNamespace + "feed",
        new XElement(AtomNamespace + "title", "Fixture atom"), new XElement(AtomNamespace + "id", "urn:fixture"),
        items.Select(item => new XElement(AtomNamespace + "entry",
            new XElement(AtomNamespace + "title", item.Title),
            new XElement(AtomNamespace + "link", new XAttribute("rel", "alternate"), new XAttribute("type", "text/html"), new XAttribute("href", item.Url)),
            new XElement(AtomNamespace + "id", item.Url),
            item.Published is { } published ? new XElement(AtomNamespace + "published", published.ToString("o", CultureInfo.InvariantCulture)) : null,
            new XElement(AtomNamespace + "content", new XAttribute("type", "html"),
                item.Description + (item.ImageUrl is { } image ? $"<p><img src=\"{image}\" alt=\"\"></p>" : ""))))))
        .ToString();
}
