using System.Globalization;
using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

/// <summary>Reads and writes structured DrawingML shape text without exposing raw XML to model callers.</summary>
internal static class XlsxShapeTextDocumentCodec
{
    private static readonly XNamespace A = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private static readonly XNamespace Xdr = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";
    private static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    internal static DrawingShapeTextDocument? Read(XElement? body,
        IReadOnlyDictionary<string, (string Target, string? TargetMode)>? relationships = null)
    {
        if (body is null) return null;
        if (body.Name != Xdr + "txBody") throw new InvalidOperationException("Invalid preserved shape text body.");
        var paragraphs = new List<DrawingShapeTextParagraph>();
        foreach (var paragraph in body.Elements(A + "p"))
        {
            var inlines = new List<DrawingShapeTextInline>();
            foreach (var element in paragraph.Elements())
            {
                var kind = element.Name == A + "r" ? DrawingShapeTextInlineKind.Run :
                    element.Name == A + "fld" ? DrawingShapeTextInlineKind.Field :
                    element.Name == A + "br" ? DrawingShapeTextInlineKind.Break : (DrawingShapeTextInlineKind?)null;
                if (kind is null) continue;
                var style = ReadStyle(element.Element(A + "rPr"), relationships);
                inlines.Add(new DrawingShapeTextInline(kind.Value, kind == DrawingShapeTextInlineKind.Break ? null : element.Element(A + "t")?.Value,
                    kind == DrawingShapeTextInlineKind.Field ? element.Attribute("id")?.Value : null,
                    kind == DrawingShapeTextInlineKind.Field ? element.Attribute("type")?.Value : null, style)
                    { SourceXml = element.ToString(SaveOptions.DisableFormatting) });
            }
            var alignment = paragraph.Element(A + "pPr")?.Attribute("algn")?.Value switch
            {
                "l" => DrawingShapeTextHAlign.Left, "ctr" => DrawingShapeTextHAlign.Center,
                "r" => DrawingShapeTextHAlign.Right, _ => (DrawingShapeTextHAlign?)null
            };
            paragraphs.Add(new DrawingShapeTextParagraph(inlines, alignment)
                { SourceXml = paragraph.ToString(SaveOptions.DisableFormatting) });
        }
        return new DrawingShapeTextDocument(paragraphs) { SourceXml = body.ToString(SaveOptions.DisableFormatting) };
    }

    private static DrawingShapeTextRunStyle? ReadStyle(XElement? properties,
        IReadOnlyDictionary<string, (string Target, string? TargetMode)>? relationships)
    {
        if (properties is null) return null;
        static bool? Flag(XElement e, string name) => e.Attribute(name)?.Value switch { "1" or "true" => true, "0" or "false" => false, _ => null };
        static int? Int(XElement e, string name) => int.TryParse(e.Attribute(name)?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;
        var font = properties.Element(A + "latin")?.Attribute("typeface")?.Value;
        var fill = properties.Element(A + "solidFill");
        CellColor? color = null;
        var hex = fill?.Element(A + "srgbClr")?.Attribute("val")?.Value;
        if (hex is { Length: 6 } && int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
            color = new CellColor((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
        WorkbookThemeColorReference? theme = fill is not null && XlsxDrawingColorReader.TryReadThemeColorReference(fill, A, out var reference) ? reference : null;
        var click = properties.Element(A + "hlinkClick");
        var id = click?.Attribute(R + "id")?.Value;
        DrawingObjectHyperlink? link = id is not null && relationships is not null && relationships.TryGetValue(id, out var target)
            ? new DrawingObjectHyperlink(target.Target, target.TargetMode, click?.Attribute("tooltip")?.Value) : null;
        return new DrawingShapeTextRunStyle(font?.StartsWith('+') == true ? null : font,
            font?.StartsWith('+') == true ? font : null, Int(properties, "sz") is { } size ? size / 100d : null,
            Flag(properties, "b"), Flag(properties, "i"), properties.Attribute("u")?.Value,
            Int(properties, "baseline"), color, theme, properties.Attribute("lang")?.Value, link);
    }

    internal static Dictionary<string, DrawingObjectHyperlink> SourceRelationships(DrawingShapeTextDocument document)
    {
        return BuildRelationships(document).Targets;
    }

    private static (Dictionary<string, DrawingObjectHyperlink> Targets, Dictionary<DrawingShapeTextInline, string> AuthoredIds)
        BuildRelationships(DrawingShapeTextDocument document)
    {
        var result = new Dictionary<string, DrawingObjectHyperlink>(StringComparer.Ordinal);
        var authored = new Dictionary<DrawingShapeTextInline, string>();
        var usedIds = new HashSet<string>(StringComparer.Ordinal);
        void Reserve(string? xml)
        {
            if (xml is null) return;
            foreach (var attribute in XElement.Parse(xml).DescendantsAndSelf().Attributes().Where(attribute => attribute.Name.Namespace == R))
                usedIds.Add(attribute.Value);
        }
        Reserve(document.SourceXml);
        foreach (var paragraph in document.Paragraphs)
        {
            Reserve(paragraph.SourceXml);
            foreach (var inline in paragraph.Inlines) Reserve(inline.SourceXml);
        }
        foreach (var inline in document.Paragraphs.SelectMany(paragraph => paragraph.Inlines))
        {
            if (inline.Style?.Hyperlink is not { } hyperlink) continue;
            if (inline.SourceXml is not { } xml)
            {
                if (authored.ContainsKey(inline)) continue;
                var index = authored.Count + 1;
                string newId;
                do { newId = "rIdFreeXRichText" + index++; } while (!usedIds.Add(newId));
                authored.Add(inline, newId);
                result.Add(newId, hyperlink);
                continue;
            }
            var id = XElement.Parse(xml).Element(A + "rPr")?.Element(A + "hlinkClick")?.Attribute(R + "id")?.Value;
            if (id is null) continue;
            if (result.TryGetValue(id, out var previous) &&
                (previous.Target != hyperlink.Target || previous.TargetMode != hyperlink.TargetMode))
                throw new InvalidOperationException("A shape text relationship ID has conflicting targets.");
            result[id] = hyperlink;
        }
        return (result, authored);
    }

    internal static string? Fingerprint(DrawingShapeTextDocument? document, bool includeOpaqueOwner = true) => document is null ? null :
        System.Text.Json.JsonSerializer.Serialize(new
        {
            Xml = Write(document, allowSourceRelationships: true).ToString(SaveOptions.DisableFormatting),
            OpaqueOwner = includeOpaqueOwner && HasOpaqueRelationships(document) ? document.SourceOwnershipId.ToString() : null,
            Relationships = SourceRelationships(document).OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => new { Id = pair.Key, pair.Value.Target, pair.Value.TargetMode }).ToArray()
        });

    internal static bool HasOpaqueRelationships(DrawingShapeTextDocument document)
    {
        var known = SourceRelationships(document);
        return Write(document, allowSourceRelationships: true).DescendantsAndSelf().Attributes()
            .Any(attribute => attribute.Name.Namespace == R &&
                (attribute.Name != R + "id" || attribute.Parent?.Name != A + "hlinkClick" || !known.ContainsKey(attribute.Value)));
    }

    internal static XElement Write(DrawingShapeTextDocument document, bool allowSourceRelationships,
        Func<DrawingObjectHyperlink, string>? bindRelationship = null)
    {
        var (relationships, authoredIds) = BuildRelationships(document);
        XElement result;
        if (document.SourceXml is { } xml)
        {
            result = XElement.Parse(xml, LoadOptions.PreserveWhitespace);
            if (result.Name != Xdr + "txBody") throw new InvalidOperationException("Invalid preserved shape text body.");
        }
        else
        {
            result = new XElement(Xdr + "txBody", new XElement(A + "bodyPr"), new XElement(A + "lstStyle"));
            foreach (var paragraph in document.Paragraphs)
            {
                if (paragraph.SourceXml is { } paragraphXml) { result.Add(XElement.Parse(paragraphXml, LoadOptions.PreserveWhitespace)); continue; }
                var p = new XElement(A + "p");
                if (paragraph.Alignment is { } align) p.Add(new XElement(A + "pPr", new XAttribute("algn", align switch
                { DrawingShapeTextHAlign.Center => "ctr", DrawingShapeTextHAlign.Right => "r", _ => "l" })));
                foreach (var inline in paragraph.Inlines) p.Add(WriteInline(inline, authoredIds.GetValueOrDefault(inline)));
                result.Add(p);
            }
        }
        // DrawingML CT_TextBody requires at least one paragraph, even for empty text.
        if (!result.Elements(A + "p").Any()) result.Add(new XElement(A + "p"));
        if (!allowSourceRelationships)
        {
            foreach (var attribute in result.DescendantsAndSelf().Attributes().Where(attribute => attribute.Name.Namespace == R))
            {
                if (attribute.Name != R + "id" || attribute.Parent?.Name != A + "hlinkClick" ||
                    bindRelationship is null || !relationships.TryGetValue(attribute.Value, out var link))
                    throw new InvalidOperationException("Shape rich text contains drawing relationships that cannot be safely regenerated.");
                attribute.Value = bindRelationship(link);
            }
        }
        return result;
    }

    private static XElement WriteInline(DrawingShapeTextInline inline, string? relationshipId)
    {
        if (inline.SourceXml is { } xml) return XElement.Parse(xml, LoadOptions.PreserveWhitespace);
        var element = new XElement(A + (inline.Kind switch { DrawingShapeTextInlineKind.Field => "fld", DrawingShapeTextInlineKind.Break => "br", _ => "r" }));
        if (inline.Kind == DrawingShapeTextInlineKind.Field)
        {
            if (string.IsNullOrWhiteSpace(inline.FieldId)) throw new InvalidOperationException("Shape text fields require an ID for XLSX regeneration.");
            if (inline.FieldId is not null) element.SetAttributeValue("id", inline.FieldId);
            if (inline.FieldType is not null) element.SetAttributeValue("type", inline.FieldType);
        }
        if (inline.Style is { } style)
        {
            var p = new XElement(A + "rPr");
            if (style.FontSizePoints is { } size) p.SetAttributeValue("sz", Math.Round(size * 100).ToString(CultureInfo.InvariantCulture));
            if (style.Bold is { } bold) p.SetAttributeValue("b", bold ? "1" : "0");
            if (style.Italic is { } italic) p.SetAttributeValue("i", italic ? "1" : "0");
            if (style.Underline is not null) p.SetAttributeValue("u", style.Underline);
            if (style.Baseline is { } baseline) p.SetAttributeValue("baseline", baseline.ToString(CultureInfo.InvariantCulture));
            if (style.Language is not null) p.SetAttributeValue("lang", style.Language);
            var fill = XlsxDrawingColorWriter.ToSolidFill(style.ThemeColor, style.Color, A);
            if (fill is not null) p.Add(fill);
            if ((style.ThemeFont ?? style.FontFamily) is { } font) p.Add(new XElement(A + "latin", new XAttribute("typeface", font)));
            if (style.Hyperlink is { } hyperlink)
                p.Add(new XElement(A + "hlinkClick", new XAttribute(R + "id", relationshipId ?? throw new InvalidOperationException("Missing shape text hyperlink relationship.")),
                    hyperlink.Tooltip is null ? null : new XAttribute("tooltip", hyperlink.Tooltip)));
            element.Add(p);
        }
        if (inline.Kind != DrawingShapeTextInlineKind.Break) element.Add(new XElement(A + "t", inline.Text));
        return element;
    }
}
