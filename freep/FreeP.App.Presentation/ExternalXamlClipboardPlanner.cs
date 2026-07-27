using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;
using FreeP.Core.Model;

namespace FreeP.App.Compositor;

/// <summary>
/// Converts the bounded FlowDocument subset commonly carried by WPF XamlPackage clipboard data
/// into the renderer-neutral rich-text payload. Package resources and unsupported controls are
/// deliberately ignored; callers can continue to RTF or plain-text fallback.
/// </summary>
public static class ExternalXamlClipboardPlanner
{
    public const int MaxPackageBytes = 8 * 1024 * 1024;
    public const int MaxXmlBytes = 8 * 1024 * 1024;
    public const int MaxOutputCharacters = 1_000_000;
    private const long EmuPerDip = 9525;

    public static InCanvasRichClipboardPayload? TryParseXamlPackage(byte[]? bytes)
    {
        if (bytes is not { Length: > 0 } || bytes.Length > MaxPackageBytes)
            return null;

        try
        {
            using var package = new ZipArchive(
                new MemoryStream(bytes, writable: false),
                ZipArchiveMode.Read,
                leaveOpen: false);
            foreach (var entry in package.Entries
                         .Where(entry => entry.FullName.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase))
                         .OrderBy(entry => entry.FullName.Length))
            {
                if (entry.Length <= 0 || entry.Length > MaxXmlBytes)
                    continue;

                using var stream = entry.Open();
                using var reader = new StreamReader(stream, detectEncodingFromByteOrderMarks: true);
                var xml = reader.ReadToEnd();
                var payload = TryParseXaml(xml);
                if (payload is not null)
                    return payload;
            }
        }
        catch
        {
            // Clipboard data is untrusted. A malformed package must not interrupt paste.
        }

        return null;
    }

    internal static InCanvasRichClipboardPayload? TryParseXaml(string? xml)
    {
        if (string.IsNullOrWhiteSpace(xml) || xml.Length > MaxXmlBytes)
            return null;

        try
        {
            var document = XDocument.Parse(xml, LoadOptions.PreserveWhitespace);
            var paragraphElements = document
                .Descendants()
                .Where(element => element.Name.LocalName == "Paragraph")
                .ToArray();
            if (paragraphElements.Length == 0)
                return null;

            var body = new TextBody();
            var outputCharacters = 0;
            foreach (var element in paragraphElements)
            {
                var paragraph = new Paragraph();
                var style = ReadStyle(element, default);
                ApplyParagraphProperties(element, paragraph);
                ReadInlineNodes(element, paragraph, style, ref outputCharacters);
                body.Paragraphs.Add(paragraph);
                if (outputCharacters > MaxOutputCharacters)
                    return null;
            }

            if (body.Paragraphs.All(static paragraph => paragraph.Runs.Count == 0))
                return null;

            return new InCanvasRichClipboardPayload(
                body,
                InCanvasTextEditPlanner.ExtractPlainText(body));
        }
        catch
        {
            return null;
        }
    }

    private static void ReadInlineNodes(
        XElement element,
        Paragraph paragraph,
        XamlTextStyle inherited,
        ref int outputCharacters)
    {
        var style = ReadStyle(element, inherited);
        foreach (var node in element.Nodes())
        {
            if (node is XText text)
            {
                AddText(text.Value, paragraph, style, ref outputCharacters);
                continue;
            }

            if (node is not XElement child)
                continue;

            switch (child.Name.LocalName)
            {
                case "LineBreak":
                    AddRun("\n", paragraph, style, ref outputCharacters);
                    break;
                case "Paragraph":
                    // Nested paragraphs belong to a different block and are handled by the
                    // outer document walk; never duplicate their text in the parent.
                    break;
                default:
                    ReadInlineElement(child, paragraph, style, ref outputCharacters);
                    break;
            }
        }
    }

    private static void ReadInlineElement(
        XElement element,
        Paragraph paragraph,
        XamlTextStyle inherited,
        ref int outputCharacters)
    {
        var style = ReadStyle(element, inherited);
        if (element.Name.LocalName == "Run"
            && element.Attribute("Text") is { } textAttribute)
        {
            AddText(textAttribute.Value, paragraph, style, ref outputCharacters);
        }

        if (element.Name.LocalName == "LineBreak")
        {
            AddRun("\n", paragraph, style, ref outputCharacters);
            return;
        }

        foreach (var node in element.Nodes())
        {
            if (node is XText text)
                AddText(text.Value, paragraph, style, ref outputCharacters);
            else if (node is XElement child && child.Name.LocalName != "Paragraph")
                ReadInlineElement(child, paragraph, style, ref outputCharacters);
        }
    }

    private static void AddText(
        string text,
        Paragraph paragraph,
        XamlTextStyle style,
        ref int outputCharacters)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrWhiteSpace(text))
            return;

        AddRun(text, paragraph, style, ref outputCharacters);
    }

    private static void AddRun(
        string text,
        Paragraph paragraph,
        XamlTextStyle style,
        ref int outputCharacters)
    {
        if (text.Length == 0)
            return;

        outputCharacters = checked(outputCharacters + text.Length);
        paragraph.Runs.Add(new Run
        {
            Text = text,
            FontFamily = style.FontFamily,
            FontSizePt = style.FontSizePt,
            Bold = style.Bold,
            BoldSet = style.BoldSet,
            Italic = style.Italic,
            ItalicSet = style.ItalicSet,
            Underline = style.Underline,
            Strikethrough = style.Strikethrough,
            Color = style.Color,
        });
    }

    private static XamlTextStyle ReadStyle(XElement element, XamlTextStyle inherited)
    {
        var style = inherited;
        var family = AttributeValue(element, "FontFamily");
        if (!string.IsNullOrWhiteSpace(family))
            style = style with { FontFamily = family.Trim() };

        if (TryReadDouble(element, "FontSize", out var dipSize) && dipSize > 0)
            style = style with { FontSizePt = dipSize * 0.75 };

        var weight = AttributeValue(element, "FontWeight");
        if (!string.IsNullOrWhiteSpace(weight))
        {
            var bold = weight.Equals("Bold", StringComparison.OrdinalIgnoreCase)
                || (int.TryParse(weight, NumberStyles.Integer, CultureInfo.InvariantCulture, out var weightValue)
                    && weightValue >= 700);
            style = style with { Bold = bold, BoldSet = true };
        }

        var fontStyle = AttributeValue(element, "FontStyle");
        if (!string.IsNullOrWhiteSpace(fontStyle))
            style = style with
            {
                Italic = fontStyle.Equals("Italic", StringComparison.OrdinalIgnoreCase)
                    || fontStyle.Equals("Oblique", StringComparison.OrdinalIgnoreCase),
                ItalicSet = true,
            };

        var decorations = AttributeValue(element, "TextDecorations");
        if (!string.IsNullOrWhiteSpace(decorations))
            style = style with
            {
                Underline = decorations.Contains("Underline", StringComparison.OrdinalIgnoreCase),
                Strikethrough = decorations.Contains("Strikethrough", StringComparison.OrdinalIgnoreCase),
            };

        var localName = element.Name.LocalName;
        if (localName.Equals("Bold", StringComparison.OrdinalIgnoreCase))
            style = style with { Bold = true, BoldSet = true };
        if (localName.Equals("Italic", StringComparison.OrdinalIgnoreCase))
            style = style with { Italic = true, ItalicSet = true };
        if (localName.Equals("Underline", StringComparison.OrdinalIgnoreCase))
            style = style with { Underline = true };

        var foreground = AttributeValue(element, "Foreground");
        if (TryParseColor(foreground, out var color))
            style = style with { Color = color };

        return style;
    }

    private static void ApplyParagraphProperties(XElement element, Paragraph paragraph)
    {
        var alignment = AttributeValue(element, "TextAlignment");
        paragraph.Align = alignment?.ToLowerInvariant() switch
        {
            "center" => TextAlign.Center,
            "right" => TextAlign.Right,
            "justify" => TextAlign.Justify,
            _ when string.Equals(alignment, "left", StringComparison.OrdinalIgnoreCase) => TextAlign.Left,
            _ => null,
        };

        var margin = AttributeValue(element, "Margin")?.Split(',', StringSplitOptions.TrimEntries);
        if (margin is { Length: 4 })
        {
            if (TryParseDip(margin[0], out var left))
                paragraph.MarginLeftEmu = checked((long)Math.Round(left * EmuPerDip));
            if (TryParseDip(margin[1], out var top))
                paragraph.SpaceBeforePt = top * 0.75;
            if (TryParseDip(margin[3], out var bottom))
                paragraph.SpaceAfterPt = bottom * 0.75;
        }
    }

    private static string? AttributeValue(XElement element, string name) =>
        element.Attributes().FirstOrDefault(attribute =>
            attribute.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase))?.Value;

    private static bool TryReadDouble(XElement element, string name, out double value) =>
        TryParseDip(AttributeValue(element, name), out value);

    private static bool TryParseDip(string? value, out double result) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result)
        && double.IsFinite(result);

    private static bool TryParseColor(string? value, out ThemeAwareColor? color)
    {
        color = null;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var text = value.Trim();
        if (!text.StartsWith('#'))
            return false;

        var hex = text[1..];
        if (hex.Length == 8
            && byte.TryParse(hex[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var alpha)
            && int.TryParse(hex[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
        {
            color = new ThemeAwareColor(SrgbColor.FromRgb(rgb), alpha);
            return true;
        }

        if (hex.Length == 6
            && int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var opaqueRgb))
        {
            color = new ThemeAwareColor(SrgbColor.FromRgb(opaqueRgb));
            return true;
        }

        return false;
    }

    private readonly record struct XamlTextStyle(
        string? FontFamily,
        double? FontSizePt,
        bool Bold,
        bool Italic,
        bool Underline,
        bool Strikethrough,
        bool BoldSet,
        bool ItalicSet,
        ThemeAwareColor? Color);
}
