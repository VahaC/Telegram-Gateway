using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Markdig;
using TelegramGateway.Core.Abstractions;

namespace TelegramGateway.Infrastructure.Formatting;

internal sealed partial class TelegramMessageFormatter : IMessageFormatter
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder().DisableHtml().UseAutoLinks().Build();
    private static readonly HashSet<string> InlineTags = ["b", "strong", "i", "em", "u", "ins", "s", "strike", "del", "code", "pre", "blockquote", "tg-spoiler"];

    public FormattingResult Format(string text, string format, int messageLength, int maxParts)
    {
        if (string.IsNullOrWhiteSpace(text)) return new([], "Message text must contain visible content.");
        if (messageLength is < 16 or > 4096) return new([], "Message length must be between 16 and 4096.");
        try
        {
            var html = format.ToLowerInvariant() switch
            {
                "plain" => WebUtility.HtmlEncode(text),
                "markdown" => Markdown.ToHtml(text, Pipeline),
                "html" => text,
                _ => null
            };
            if (html is null) return new([], "Format must be plain, html, or markdown.");
            // XML parsing deliberately accepts a strict HTML subset. DTDs and external entities stay disabled.
            html = NamedEntity().Replace(html, match =>
            {
                var decoded = WebUtility.HtmlDecode(match.Value);
                if (decoded == match.Value) throw new FormatException("Unknown entity.");
                return string.Concat(decoded.EnumerateRunes().Select(rune => $"&#{rune.Value};"));
            });
            using var reader = XmlReader.Create(new StringReader($"<root>{html}</root>"), new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 512 * 1024
            });
            var root = XElement.Load(reader, LoadOptions.PreserveWhitespace);
            var atoms = new List<HtmlAtom>();
            foreach (var node in root.Nodes()) Append(node, atoms, format == "markdown");
            if (!atoms.Any(atom => atom.VisibleLength > 0 && !string.IsNullOrWhiteSpace(WebUtility.HtmlDecode(atom.Html))))
                return new([], "Message text must contain visible content.");
            var parts = Split(atoms, messageLength, maxParts);
            return new(parts);
        }
        catch (Exception exception) when (exception is XmlException or FormatException or ArgumentException)
        {
            return new([], "Content contains invalid or unsupported Telegram HTML, a link, or an oversized text element.");
        }
    }

    private static void Append(XNode node, List<HtmlAtom> atoms, bool markdown)
    {
        if (node is XText text)
        {
            var elements = StringInfo.GetTextElementEnumerator(text.Value);
            while (elements.MoveNext())
            {
                var element = elements.GetTextElement();
                atoms.Add(new(WebUtility.HtmlEncode(element), element.Length));
            }
            return;
        }
        if (node is not XElement elementNode || elementNode.Name.Namespace != XNamespace.None)
            throw new FormatException("Unsupported node.");
        if (elementNode.Ancestors().Take(33).Count() > 32)
            throw new FormatException("Formatting nesting is too deep.");
        var name = elementNode.Name.LocalName;
        if (name == "a" && elementNode.Ancestors("a").Any()
            || name == "blockquote" && elementNode.Ancestors("blockquote").Any()
            || elementNode.Ancestors().Any(ancestor => ancestor.Name.LocalName is "code" or "pre")
                && !(name == "code" && elementNode.Parent?.Name.LocalName == "pre"))
            throw new FormatException("Unsupported entity nesting.");
        if (markdown && name is "p" or "h1" or "h2" or "h3" or "h4" or "h5" or "h6" or "ul" or "ol" or "li" or "hr" or "br")
        {
            if (name is "hr" or "br") { Append(new XText("\n"), atoms, true); return; }
            var heading = name.StartsWith('h');
            if (heading) atoms.Add(new("<b>", 0, "b", true));
            if (name == "li")
            {
                var parent = elementNode.Parent!;
                var start = int.TryParse(parent.Attribute("start")?.Value, out var number) ? number : 1;
                var index = parent.Elements("li").TakeWhile(sibling => sibling != elementNode).Count() + start;
                Append(new XText(parent.Name.LocalName == "ol" ? $"{index}. " : "• "), atoms, true);
            }
            foreach (var child in elementNode.Nodes()) Append(child, atoms, true);
            if (heading) atoms.Add(new("</b>", 0, "b", false));
            if (name is "p" or "li" || heading) Append(new XText("\n"), atoms, true);
            return;
        }
        if (name == "a")
        {
            var href = elementNode.Attribute("href")?.Value;
            if (!Uri.TryCreate(href, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http")
                || !string.IsNullOrEmpty(uri.UserInfo) || href!.Length > 2048)
                throw new FormatException("Unsafe URL.");
            if (!markdown && elementNode.Attributes().Any(attribute => attribute.Name != "href"))
                throw new FormatException("Unsupported attribute.");
            atoms.Add(new($"<a href=\"{WebUtility.HtmlEncode(href)}\">", 0, "a", true));
        }
        else if (InlineTags.Contains(name))
        {
            if (elementNode.Attributes().Any() && !(markdown && name == "code"))
                throw new FormatException("Unsupported attribute.");
            atoms.Add(new($"<{name}>", 0, name, true));
        }
        else throw new FormatException("Unsupported tag.");
        foreach (var child in elementNode.Nodes()) Append(child, atoms, markdown);
        atoms.Add(new($"</{name}>", 0, name, false));
    }

    private static IReadOnlyList<string> Split(List<HtmlAtom> atoms, int limit, int maxParts)
    {
        var parts = new List<string>();
        var stack = new List<HtmlAtom>();
        var builder = new StringBuilder();
        var length = 0;
        void Finish()
        {
            if (length == 0) return;
            foreach (var tag in stack.AsEnumerable().Reverse()) builder.Append($"</{tag.Tag}>");
            parts.Add(builder.ToString());
            if (parts.Count > maxParts) throw new FormatException("Too many messages.");
            builder.Clear();
            foreach (var tag in stack) builder.Append(tag.Html);
            length = 0;
        }
        foreach (var atom in atoms)
        {
            if (atom.VisibleLength > limit) throw new FormatException("Oversized grapheme.");
            if (length + atom.VisibleLength > limit) Finish();
            builder.Append(atom.Html);
            if (atom.Tag is not null)
            {
                if (atom.Opening) stack.Add(atom);
                else stack.RemoveAt(stack.Count - 1);
            }
            length += atom.VisibleLength;
            // Prefer a paragraph/list boundary when the chunk is already mostly full.
            if (atom.Html == "\n" && length >= limit * 0.8) Finish();
        }
        Finish();
        return parts;
    }

    [GeneratedRegex("&[A-Za-z][A-Za-z0-9]+;")]
    private static partial Regex NamedEntity();

    private sealed record HtmlAtom(string Html, int VisibleLength, string? Tag = null, bool Opening = false);
}
