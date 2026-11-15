using System.Xml;
using System.Xml.Linq;
using AgentGateway.Core.Abstractions;

namespace AgentGateway.Infrastructure.Feeds;

/// <summary>
/// Reads RSS 2.0, RSS 1.0 (RDF) and Atom 1.0 into plain entries. Everything comes from the feed document itself; article
/// pages are never fetched. An item without a usable link is skipped, the rest of the feed is kept.
/// </summary>
internal static class NewsFeedParser
{
    public const int MaxTitleLength = 500;
    public const int MaxSummaryLength = 4000;
    /// <summary>Some feeds carry hundreds of archive items; the first 200 cover more than a week of any publisher in the catalog.</summary>
    public const int MaxEntries = 200;

    private static readonly XNamespace Atom = "http://www.w3.org/2005/Atom";
    private static readonly XNamespace Rdf = "http://www.w3.org/1999/02/22-rdf-syntax-ns#";
    private static readonly XNamespace Content = "http://purl.org/rss/1.0/modules/content/";
    private static readonly XNamespace DublinCore = "http://purl.org/dc/elements/1.1/";
    private static readonly XNamespace Media = "http://search.yahoo.com/mrss/";

    // A DTD is refused outright and nothing external is resolved: DTDs carry the entity-expansion and external-entity
    // attacks, and no feed in the catalog needs one. Character references to control characters (&#11;) are let through
    // like the raw ones that Clean replaces; the text cleanup turns them into spaces.
    private static readonly XmlReaderSettings Settings = new()
    {
        DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, IgnoreComments = true, IgnoreProcessingInstructions = true,
        CloseInput = false, CheckCharacters = false
    };

    // Prefixes publishers use without declaring them (HackerNoon writes media:content with no xmlns:media). Declared
    // prefixes still win, so a feed that binds one of these names to another namespace is read as written.
    private static readonly (string Prefix, string Namespace)[] CommonPrefixes =
    [
        ("media", Media.NamespaceName), ("content", Content.NamespaceName), ("dc", DublinCore.NamespaceName), ("atom", Atom.NamespaceName),
        ("rdf", Rdf.NamespaceName), ("itunes", "http://www.itunes.com/dtds/podcast-1.0.dtd"), ("slash", "http://purl.org/rss/1.0/modules/slash/"),
        ("wfw", "http://wellformedweb.org/CommentAPI/"), ("sy", "http://purl.org/rss/1.0/modules/syndication/")
    ];

    /// <summary>The entries, or an error code (xml_invalid, not_a_feed) when the document is not a feed at all.</summary>
    public static (IReadOnlyList<NewsFeedDocumentEntry> Entries, string? Error) Parse(Stream stream, Uri baseUri)
    {
        XElement root;
        try
        {
            using var document = new MemoryStream(Clean(stream));
            using var reader = XmlReader.Create(document, Settings, CommonPrefixContext());
            root = XDocument.Load(reader).Root!;
        }
        catch (XmlException exception)
        {
            return ([], $"xml_invalid (line {exception.LineNumber})");
        }
        var items = root.Name.LocalName switch
        {
            "rss" => root.Element(root.Name.Namespace + "channel") is { } channel
                ? channel.Elements(channel.Name.Namespace + "item").Select(item => ReadRss(item, baseUri)) : [],
            // RSS 1.0 keeps its items next to the channel, in the RSS 1.0 (or 0.90) namespace.
            "RDF" when root.Name.Namespace == Rdf => root.Elements().Where(element => element.Name.LocalName == "item")
                .Select(item => ReadRss(item, baseUri)),
            "feed" when root.Name.Namespace == Atom => root.Elements(Atom + "entry").Select(entry => ReadAtom(entry, baseUri)),
            _ => null
        };
        if (items is null)
            return ([], $"not_a_feed (root <{NewsFeedText.Truncate(root.Name.LocalName, 40)}>)");
        return (items.Take(MaxEntries).OfType<NewsFeedDocumentEntry>().ToList(), null);
    }

    private static XmlParserContext CommonPrefixContext()
    {
        var names = new XmlNamespaceManager(new NameTable());
        foreach (var (prefix, name) in CommonPrefixes) names.AddNamespace(prefix, name);
        return new XmlParserContext(null, names, null, XmlSpace.None);
    }

    /// <summary>
    /// Two faults that would otherwise cost whole publishers: a blank line before the XML declaration (a WordPress plugin habit,
    /// KitGuru) makes the declaration illegal, and raw control characters such as U+000B (Rapid7) are illegal anywhere in XML,
    /// even with CheckCharacters off. Leading whitespace is dropped and those characters become spaces. Bytes below 0x20 never
    /// occur inside a UTF-8 or single-byte character, so the swap is safe there; UTF-16 documents (a zero byte or a UTF-16
    /// byte-order mark at the start) are left as they are.
    /// </summary>
    private static byte[] Clean(Stream stream)
    {
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        var bytes = copy.ToArray();
        var start = 0;
        while (start < bytes.Length && bytes[start] is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n') start++;
        var utf16 = bytes.Length - start >= 2 && (bytes[start] == 0 || bytes[start + 1] == 0
            || (bytes[start] == 0xFF && bytes[start + 1] == 0xFE) || (bytes[start] == 0xFE && bytes[start + 1] == 0xFF));
        if (!utf16)
            for (var index = start; index < bytes.Length; index++)
                if (bytes[index] < 0x20 && bytes[index] is not ((byte)'\t' or (byte)'\n' or (byte)'\r')) bytes[index] = (byte)' ';
        return start == 0 ? bytes : bytes[start..];
    }

    private static NewsFeedDocumentEntry? ReadRss(XElement item, Uri baseUri)
    {
        // RSS 2.0 fields have no namespace and RSS 1.0 fields share the item's; an atom:link in the same item is not the link.
        XElement? Field(string name) => item.Element(item.Name.Namespace + name);
        var link = Field("link")?.Value.Trim();
        if (string.IsNullOrEmpty(link)) link = item.Attribute(Rdf + "about")?.Value;
        if (string.IsNullOrEmpty(link) && Field("guid") is { } guid && (string?)guid.Attribute("isPermaLink") != "false") link = guid.Value;
        if (WebUrl(link, baseUri) is not { } url) return null;
        var article = new Uri(url);
        var description = Field("description")?.Value;
        var encoded = item.Element(Content + "encoded")?.Value;
        var published = NewsFeedDates.Parse(Field("pubDate")?.Value) ?? NewsFeedDates.Parse(item.Element(DublinCore + "date")?.Value)
            ?? NewsFeedDates.Parse(item.Element(Atom + "updated")?.Value);
        var image = MediaImage(item, article)
            ?? item.Elements(item.Name.Namespace + "enclosure").Where(IsImageType).Select(enclosure => NewsFeedText.HttpsUrl((string?)enclosure.Attribute("url"), article))
                .FirstOrDefault(found => found is not null)
            ?? NewsFeedText.FirstImage(encoded, article) ?? NewsFeedText.FirstImage(description, article);
        return Entry(Field("title")?.Value, url, published, encoded, description, image);
    }

    private static NewsFeedDocumentEntry? ReadAtom(XElement entry, Uri baseUri)
    {
        // The article is the alternate link (rel absent means alternate); prefer the HTML one when several exist.
        var link = entry.Elements(Atom + "link")
            .Where(element => (string?)element.Attribute("rel") is null or "alternate")
            .OrderBy(element => (string?)element.Attribute("type") is null or "text/html" ? 0 : 1)
            .Select(element => (string?)element.Attribute("href"))
            .FirstOrDefault(href => !string.IsNullOrWhiteSpace(href));
        if (WebUrl(link, baseUri) is not { } url) return null;
        var article = new Uri(url);
        var content = Markup(entry.Element(Atom + "content"));
        var summary = Markup(entry.Element(Atom + "summary"));
        var published = NewsFeedDates.Parse(entry.Element(Atom + "published")?.Value) ?? NewsFeedDates.Parse(entry.Element(Atom + "updated")?.Value);
        var image = MediaImage(entry, article)
            ?? entry.Elements(Atom + "link").Where(element => (string?)element.Attribute("rel") == "enclosure" && IsImageType(element))
                .Select(element => NewsFeedText.HttpsUrl((string?)element.Attribute("href"), article)).FirstOrDefault(found => found is not null)
            ?? NewsFeedText.FirstImage(content, article) ?? NewsFeedText.FirstImage(summary, article);
        return Entry(Markup(entry.Element(Atom + "title")), url, published, content, summary, image);
    }

    /// <summary>The longer of the two texts is the summary: content:encoded or Atom content is often the whole article, a description only a teaser.</summary>
    private static NewsFeedDocumentEntry? Entry(string? titleMarkup, string url, DateTime? published, string? first, string? second, string? image)
    {
        var one = NewsFeedText.ToPlainText(first, MaxSummaryLength);
        var two = NewsFeedText.ToPlainText(second, MaxSummaryLength);
        var summary = one.Length >= two.Length ? one : two;
        var title = NewsFeedText.ToPlainText(titleMarkup, MaxTitleLength);
        if (title.Length == 0) title = NewsFeedText.Truncate(summary, 120);
        // Neither a title nor a text leaves the agent nothing to judge the lead by.
        return title.Length == 0 ? null : new(title, url, published, summary, image);
    }

    /// <summary>media:content (an image) first, then media:thumbnail, also inside media:group.</summary>
    private static string? MediaImage(XElement item, Uri article)
    {
        var groups = item.Elements(Media + "group").ToList();
        var contents = item.Elements(Media + "content").Concat(groups.Elements(Media + "content"))
            .Where(element => (string?)element.Attribute("medium") is { } medium ? medium == "image" : (string?)element.Attribute("type") is null || IsImageType(element));
        var thumbnails = item.Elements(Media + "thumbnail").Concat(groups.Elements(Media + "thumbnail"));
        return contents.Concat(thumbnails).Select(element => NewsFeedText.HttpsUrl((string?)element.Attribute("url"), article))
            .FirstOrDefault(found => found is not null);
    }

    private static bool IsImageType(XElement element)
        => ((string?)element.Attribute("type"))?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>Atom text constructs: xhtml content is markup inside the element, html and text are its text value.</summary>
    private static string? Markup(XElement? element) => element is null ? null
        : (string?)element.Attribute("type") == "xhtml" ? string.Concat(element.Nodes()) : element.Value;

    private static string? WebUrl(string? value, Uri baseUri)
    {
        if (string.IsNullOrWhiteSpace(value) || !Uri.TryCreate(baseUri, value.Trim(), out var uri)) return null;
        return uri.Scheme is "http" or "https" && uri.UserInfo.Length == 0 && uri.AbsoluteUri.Length <= 2000 ? uri.AbsoluteUri : null;
    }
}
