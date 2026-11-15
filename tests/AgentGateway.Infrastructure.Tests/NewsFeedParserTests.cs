using System.Globalization;
using System.Text;
using AgentGateway.Infrastructure.Feeds;

namespace AgentGateway.Infrastructure.Tests;

/// <summary>The feed parser, its date reader and its HTML-to-text rules: pure functions over feed documents.</summary>
public sealed class NewsFeedParserTests
{
    private static readonly Uri FeedUri = new("https://publisher.example/feed/");

    [Fact]
    public void Parse_rss_2_reads_links_rfc822_dates_the_longer_text_and_the_feed_picture()
    {
        //Arrange
        const string xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <rss version="2.0" xmlns:content="http://purl.org/rss/1.0/modules/content/" xmlns:media="http://search.yahoo.com/mrss/"
                 xmlns:atom="http://www.w3.org/2005/Atom">
              <channel>
                <title>Publisher</title>
                <item>
                  <title>Proxmox VE 9.1 &amp;amp; ZFS</title>
                  <atom:link rel="self" href="https://publisher.example/not-the-article"/>
                  <link>https://publisher.example/proxmox-9-1/</link>
                  <pubDate>Sat, 14 Nov 2026 10:30:00 +0200</pubDate>
                  <description><![CDATA[<p>Short teaser.</p>]]></description>
                  <content:encoded><![CDATA[<p>The <b>full</b> article&nbsp;text.</p><script>alert(1)</script><p>Second   paragraph.</p>
                    <img src="https://publisher.example/inline.jpg">]]></content:encoded>
                  <media:content url="https://publisher.example/cover.jpg" medium="image"/>
                </item>
                <item>
                  <title>Relative link and enclosure</title>
                  <link>/relative-story</link>
                  <pubDate>Fri, 13 Nov 26 08:00 GMT</pubDate>
                  <enclosure url="https://publisher.example/enclosure.png" type="image/png" length="1"/>
                </item>
                <item>
                  <title>Guid permalink, inline picture</title>
                  <guid isPermaLink="true">https://publisher.example/guid-story</guid>
                  <description><![CDATA[<img src="https://publisher.example/pixel.gif" width="1" height="1"><img src='/images/lead.webp'> Text.]]></description>
                </item>
              </channel>
            </rss>
            """;
        //Act
        var (entries, error) = Parse(xml);
        //Assert
        Assert.Null(error);
        Assert.Equal(3, entries.Count);
        Assert.Equal("Proxmox VE 9.1 & ZFS", entries[0].Title);
        Assert.Equal("https://publisher.example/proxmox-9-1/", entries[0].Url);
        Assert.Equal(new DateTime(2026, 11, 14, 8, 30, 0, DateTimeKind.Utc), entries[0].PublishedUtc);
        Assert.Equal("The full article text. Second paragraph.", entries[0].Summary);
        Assert.Equal("https://publisher.example/cover.jpg", entries[0].ImageUrl);
        Assert.Equal("https://publisher.example/relative-story", entries[1].Url);
        Assert.Equal(new DateTime(2026, 11, 13, 8, 0, 0, DateTimeKind.Utc), entries[1].PublishedUtc);
        Assert.Equal("https://publisher.example/enclosure.png", entries[1].ImageUrl);
        Assert.Equal("https://publisher.example/guid-story", entries[2].Url);
        Assert.Null(entries[2].PublishedUtc);
        Assert.Equal("https://publisher.example/images/lead.webp", entries[2].ImageUrl);
    }

    [Fact]
    public void Parse_rss_1_rdf_reads_items_beside_the_channel_with_dublin_core_dates()
    {
        //Arrange
        const string xml = """
            <rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#" xmlns="http://purl.org/rss/1.0/"
                     xmlns:dc="http://purl.org/dc/elements/1.1/">
              <channel rdf:about="https://publisher.example/"><title>Publisher</title></channel>
              <item rdf:about="https://publisher.example/rdf-story">
                <title>RDF story</title>
                <link>https://publisher.example/rdf-story</link>
                <description>Plain description.</description>
                <dc:date>2026-11-14T09:15:00+01:00</dc:date>
              </item>
            </rdf:RDF>
            """;
        //Act
        var (entries, error) = Parse(xml);
        //Assert
        Assert.Null(error);
        var entry = Assert.Single(entries);
        Assert.Equal("RDF story", entry.Title);
        Assert.Equal(new DateTime(2026, 11, 14, 8, 15, 0, DateTimeKind.Utc), entry.PublishedUtc);
        Assert.Equal("Plain description.", entry.Summary);
        Assert.Null(entry.ImageUrl);
    }

    [Fact]
    public void Parse_atom_reads_the_alternate_link_published_or_updated_xhtml_content_and_thumbnails()
    {
        //Arrange
        const string xml = """
            <feed xmlns="http://www.w3.org/2005/Atom" xmlns:media="http://search.yahoo.com/mrss/">
              <title>Releases</title>
              <entry>
                <title type="html">v2.0 &lt;b&gt;stable&lt;/b&gt;</title>
                <link rel="enclosure" type="image/jpeg" href="https://publisher.example/enclosure.jpg"/>
                <link rel="alternate" type="text/html" href="https://github.com/owner/project/releases/tag/v2.0"/>
                <updated>2026-11-14T12:00:00Z</updated>
                <published>2026-11-14T11:00:00Z</published>
                <content type="xhtml"><div xmlns="http://www.w3.org/1999/xhtml"><p>Release <em>notes</em>.</p></div></content>
                <media:thumbnail url="https://publisher.example/thumb.png"/>
              </entry>
              <entry>
                <title>Only updated</title>
                <link href="https://publisher.example/only-updated"/>
                <updated>2026-11-13T07:00:00.123-05:00</updated>
                <summary>Summary text.</summary>
              </entry>
            </feed>
            """;
        //Act
        var (entries, error) = Parse(xml);
        //Assert
        Assert.Null(error);
        Assert.Equal(2, entries.Count);
        Assert.Equal("v2.0 stable", entries[0].Title);
        Assert.Equal("https://github.com/owner/project/releases/tag/v2.0", entries[0].Url);
        Assert.Equal(new DateTime(2026, 11, 14, 11, 0, 0, DateTimeKind.Utc), entries[0].PublishedUtc);
        Assert.Equal("Release notes.", entries[0].Summary);
        Assert.Equal("https://publisher.example/thumb.png", entries[0].ImageUrl);
        Assert.Equal(new DateTime(2026, 11, 13, 12, 0, 0, 123, DateTimeKind.Utc), entries[1].PublishedUtc);
        Assert.Equal("Summary text.", entries[1].Summary);
    }

    [Theory]
    [InlineData("<?xml version=\"1.0\"?><!DOCTYPE rss [<!ENTITY lol \"lol\">]><rss><channel><item><title>&lol;</title></item></channel></rss>", "xml_invalid")]
    [InlineData("<html><head><title>Just a moment...</title></head><body/></html>", "not_a_feed (root <html>)")]
    [InlineData("<rss><channel><item>unclosed</channel></rss>", "xml_invalid")]
    [InlineData("Access denied", "xml_invalid")]
    public void Parse_refuses_a_dtd_broken_xml_and_a_page_that_is_no_feed(string xml, string expected)
    {
        //Arrange
        //Act
        var (entries, error) = Parse(xml);
        //Assert
        Assert.Empty(entries);
        Assert.StartsWith(expected, error);
    }

    [Fact]
    public void Parse_tolerates_undeclared_common_prefixes_a_blank_line_before_the_declaration_and_control_characters()
    {
        //Arrange
        const string xml = "\r\n\n<?xml version=\"1.0\" encoding=\"UTF-8\"?><rss version=\"2.0\"><channel><item>"
            + "<title>Cookie\u000b\u000bflags</title><link>https://publisher.example/cookies</link>"
            + "<pubDate>Thu, 12 Nov 2026 07:14:00 GMT</pubDate><dc:creator>Author</dc:creator>"
            + "<media:content url=\"https://publisher.example/cover.jpg\" medium=\"image\"/></item></channel></rss>";
        //Act
        var (entries, error) = Parse(xml);
        //Assert
        Assert.Null(error);
        var entry = Assert.Single(entries);
        Assert.Equal("Cookie flags", entry.Title);
        Assert.Equal(new DateTime(2026, 11, 12, 7, 14, 0, DateTimeKind.Utc), entry.PublishedUtc);
        Assert.Equal("https://publisher.example/cover.jpg", entry.ImageUrl);
    }

    [Fact]
    public void Parse_skips_items_without_a_usable_link_and_keeps_at_most_200()
    {
        //Arrange
        var items = new StringBuilder();
        items.Append("<item><title>No link</title></item><item><title>Script link</title><link>javascript:alert(1)</link></item>");
        for (var number = 0; number < 250; number++) items.Append($"<item><title>Story {number}</title><link>https://publisher.example/{number}</link></item>");
        //Act
        var (entries, error) = Parse($"<rss><channel>{items}</channel></rss>");
        //Assert
        Assert.Null(error);
        Assert.Equal(198, entries.Count); // The first 200 items, two of them unusable.
        Assert.Equal("Story 0", entries[0].Title);
    }

    [Theory]
    [InlineData("Sat, 14 Nov 2026 10:30:00 GMT", "2026-11-14T10:30:00")]
    [InlineData("Sat, 14 Nov 2026 10:30:00 +0000", "2026-11-14T10:30:00")]
    [InlineData("14 Nov 2026 10:30 EST", "2026-11-14T15:30:00")]
    [InlineData("Sat,  4 Nov 2026 01:02:03 PDT", "2026-11-04T08:02:03")]
    [InlineData("Sat, 14 Nov 26 10:30:00 Z", "2026-11-14T10:30:00")]
    [InlineData("Saturday, 14 November 2026 10:30:00 +01:00", "2026-11-14T09:30:00")]
    [InlineData("Sat, 14 Nov 2026 10:30:00 XYZ", "2026-11-14T10:30:00")]
    [InlineData("2026-11-14T10:30:00Z", "2026-11-14T10:30:00")]
    [InlineData("2026-11-14T12:30:00.5+02:00", "2026-11-14T10:30:00.5")]
    [InlineData("2026-11-14", "2026-11-14T00:00:00")]
    public void Dates_common_feed_formats_become_utc(string value, string expected)
    {
        //Arrange
        //Act
        var parsed = NewsFeedDates.Parse(value);
        //Assert
        Assert.Equal(DateTime.Parse(expected, CultureInfo.InvariantCulture), parsed);
        Assert.Equal(DateTimeKind.Utc, parsed!.Value.Kind);
    }

    [Theory]
    [InlineData("")]
    [InlineData("yesterday")]
    [InlineData("Sat, 31 Feb 2026 10:00:00 GMT")]
    public void Dates_unknown_or_impossible_values_are_null(string value)
    {
        //Arrange
        //Act
        var parsed = NewsFeedDates.Parse(value);
        //Assert
        Assert.Null(parsed);
    }

    [Theory]
    [InlineData("<p>One</p><p>Two</p>", "One Two")]
    [InlineData("Pro<b>xmox</b> &amp; ZFS", "Proxmox & ZFS")]
    [InlineData("&lt;p&gt;Escaped twice&lt;/p&gt;", "Escaped twice")]
    [InlineData("a < b and c > d", "a < b and c > d")]
    [InlineData("<style>.x{}</style><!-- note -->  Text \n\t here ", "Text here")]
    public void Text_html_becomes_one_line_of_plain_text(string html, string expected)
    {
        //Arrange
        //Act
        var text = NewsFeedText.ToPlainText(html, 4000);
        //Assert
        Assert.Equal(expected, text);
    }

    [Fact]
    public void Text_is_cut_at_the_limit_without_splitting_a_surrogate_pair()
    {
        //Arrange
        var text = new string('a', 8) + "😀😀";
        //Act
        var cut = NewsFeedText.ToPlainText(text, 10);
        //Assert
        Assert.Equal(new string('a', 8) + "…", cut);
    }

    [Theory]
    [InlineData("<img src=\"http://publisher.example/plain.jpg\">", null)]
    [InlineData("<img src=\"https://user:secret@publisher.example/a.jpg\">", null)]
    [InlineData("<img width=\"1\" src=\"https://publisher.example/pixel.gif\"><img src=\"images/b.jpg?w=800&amp;h=400\">", "https://publisher.example/feed/images/b.jpg?w=800&h=400")]
    public void Text_first_image_is_an_https_picture_and_never_a_tracking_pixel(string html, string? expected)
    {
        //Arrange
        //Act
        var image = NewsFeedText.FirstImage(html, FeedUri);
        //Assert
        Assert.Equal(expected, image);
    }

    private static (IReadOnlyList<AgentGateway.Core.Abstractions.NewsFeedDocumentEntry> Entries, string? Error) Parse(string xml)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));
        return NewsFeedParser.Parse(stream, FeedUri);
    }
}
