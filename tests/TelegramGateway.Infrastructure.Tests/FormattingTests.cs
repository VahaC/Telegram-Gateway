using System.Net;
using System.Xml.Linq;
using TelegramGateway.Infrastructure.Formatting;

namespace TelegramGateway.Infrastructure.Tests;

public sealed class FormattingTests
{
    [Fact]
    public void Format_markdown_preserves_ukrainian_headings_numbering_and_urls()
    {
        //Arrange
        var formatter = new TelegramMessageFormatter();
        var markdown = "# Технології\n\n1. **Новина** — [джерело](https://example.com/news?a=1&b=2)\n2. Україна 🚀";
        //Act
        var result = formatter.Format(markdown, "markdown", 4096, 64);
        //Assert
        Assert.True(result.IsSuccess, result.Error);
        var html = Assert.Single(result.Parts);
        Assert.Contains("<b>Технології</b>", html);
        Assert.Contains("1. ", html);
        Assert.Contains("2. ", html);
        Assert.Contains("href=\"https://example.com/news?a=1&amp;b=2\"", html);
        Assert.Contains("Україна", WebUtility.HtmlDecode(html));
        Assert.Contains("🚀", WebUtility.HtmlDecode(html));
    }

    [Fact]
    public void Format_plain_escapes_html_and_preserves_unicode()
    {
        //Arrange
        var formatter = new TelegramMessageFormatter();
        var text = "<script>Україна & ґ і ї є</script>";
        //Act
        var result = formatter.Format(text, "plain", 4096, 64);
        //Assert
        Assert.Equal(text, WebUtility.HtmlDecode(Assert.Single(result.Parts)));
        Assert.DoesNotContain("<script>", result.Parts[0]);
    }

    [Theory]
    [InlineData("<b>unclosed")]
    [InlineData("<script>bad</script>")]
    [InlineData("<a href=\"javascript:alert(1)\">link</a>")]
    [InlineData("<b onclick=\"x\">bad</b>")]
    [InlineData("<!DOCTYPE x [<!ENTITY y SYSTEM 'file:///etc/passwd'>]>&y;")]
    [InlineData("<a href=\"https://user:secret@example.com\">link</a>")]
    public void Format_unsafe_html_is_rejected(string html)
    {
        //Arrange
        var formatter = new TelegramMessageFormatter();
        //Act
        var result = formatter.Format(html, "html", 4096, 64);
        //Assert
        Assert.False(result.IsSuccess);
        Assert.Empty(result.Parts);
    }

    [Fact]
    public void Format_long_nested_html_preserves_entities_tags_and_graphemes()
    {
        //Arrange
        var formatter = new TelegramMessageFormatter();
        var text = string.Concat(Enumerable.Repeat("Україна & 🚀 👨‍👩‍👧‍👦 е́ ", 700));
        var html = $"<b><i><a href=\"https://example.com/?a=1&amp;b=2\">{WebUtility.HtmlEncode(text)}</a></i></b>";
        //Act
        var result = formatter.Format(html, "html", 4096, 64);
        //Assert
        Assert.True(result.IsSuccess, result.Error);
        Assert.True(result.Parts.Count > 2);
        var joined = "";
        foreach (var part in result.Parts)
        {
            var parsed = XElement.Parse("<root>" + part + "</root>");
            Assert.InRange(parsed.Value.Length, 1, 4096);
            Assert.Contains("href=\"https://example.com/?a=1&amp;b=2\"", part);
            Assert.False(char.IsHighSurrogate(parsed.Value[^1]));
            Assert.DoesNotContain("\uFFFD", parsed.Value);
            joined += parsed.Value;
        }
        Assert.Equal(text, joined);
    }

    [Fact]
    public void Format_markdown_raw_html_is_visible_text()
    {
        //Arrange
        var formatter = new TelegramMessageFormatter();
        //Act
        var result = formatter.Format("<script>alert(1)</script>", "markdown", 4096, 64);
        //Assert
        Assert.True(result.IsSuccess, result.Error);
        Assert.DoesNotContain("<script>", string.Concat(result.Parts));
    }

    [Fact]
    public void Format_too_many_parts_is_rejected()
    {
        //Arrange
        var formatter = new TelegramMessageFormatter();
        //Act
        var result = formatter.Format(new string('ї', 100), "plain", 16, 2);
        //Assert
        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void Format_deeply_nested_html_is_rejected_without_recursing_unboundedly()
    {
        //Arrange
        var formatter = new TelegramMessageFormatter();
        var html = string.Concat(Enumerable.Repeat("<b>", 1000)) + "Text" + string.Concat(Enumerable.Repeat("</b>", 1000));
        //Act
        var result = formatter.Format(html, "html", 4096, 64);
        //Assert
        Assert.False(result.IsSuccess);
    }
}
