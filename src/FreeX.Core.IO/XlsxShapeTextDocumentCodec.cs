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
            var properties = paragraph.Element(A + "pPr");
            var level = int.TryParse(properties?.Attribute("lvl")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedLevel)
                && parsedLevel is >= 0 and <= 8 ? parsedLevel : (int?)null;
            paragraphs.Add(new DrawingShapeTextParagraph(inlines, ReadAlignment(properties),
                ReadStyle(properties?.Element(A + "defRPr"), relationships), level)
                { SourceXml = paragraph.ToString(SaveOptions.DisableFormatting) });
        }
        var listStyle = body.Element(A + "lstStyle");
        var defaultParagraph = ReadDefaults(listStyle?.Element(A + "defPPr"), relationships);
        var levels = Enumerable.Range(0, 9)
            .Select(level => (Level: level, Defaults: ReadDefaults(listStyle?.Element(A + $"lvl{level + 1}pPr"), relationships)))
            .Where(item => item.Defaults is not null)
            .Select(item => new DrawingShapeTextListLevel(item.Level, item.Defaults!));
        return new DrawingShapeTextDocument(paragraphs, defaultParagraph, levels) { SourceXml = body.ToString(SaveOptions.DisableFormatting) };
    }

    private static DrawingShapeTextHAlign? ReadAlignment(XElement? properties) => properties?.Attribute("algn")?.Value switch
    {
        "l" => DrawingShapeTextHAlign.Left, "ctr" => DrawingShapeTextHAlign.Center,
        "r" => DrawingShapeTextHAlign.Right, _ => null
    };

    private static DrawingShapeTextParagraphDefaults? ReadDefaults(XElement? properties,
        IReadOnlyDictionary<string, (string Target, string? TargetMode)>? relationships) => properties is null ? null :
        new DrawingShapeTextParagraphDefaults(ReadAlignment(properties), ReadStyle(properties.Element(A + "defRPr"), relationships));

    private static DrawingShapeTextRunStyle? ReadStyle(XElement? properties,
        IReadOnlyDictionary<string, (string Target, string? TargetMode)>? relationships)
    {
        if (properties is null) return null;
        static bool? Flag(XElement e, string name) => e.Attribute(name)?.Value switch { "1" or "true" => true, "0" or "false" => false, _ => null };
        static int? Int(XElement e, string name) => int.TryParse(e.Attribute(name)?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;
        var font = properties.Element(A + "latin")?.Attribute("typeface")?.Value;
        var fill = properties.Elements().FirstOrDefault(IsTextFill);
        CellColor? color = null;
        var solid = fill?.Name == A + "solidFill" && IsSupportedSolidFill(fill) ? fill : null;
        var hex = solid?.Element(A + "srgbClr")?.Attribute("val")?.Value;
        if (hex is { Length: 6 } && int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
            color = new CellColor((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
        WorkbookThemeColorReference? theme = solid is not null && XlsxDrawingColorReader.TryReadThemeColorReference(solid, A, out var reference) ? reference : null;
        var fillOverride = fill is null ? null : fill.Name == A + "noFill"
            ? new DrawingShapeTextFillOverride(DrawingShapeTextFillKind.NoFill,
                fill.Nodes().Any() || fill.Attributes().Any(attribute => !attribute.IsNamespaceDeclaration) ? fill.ToString(SaveOptions.DisableFormatting) : null)
            : solid is null || (color is null && theme is null)
                ? new DrawingShapeTextFillOverride(DrawingShapeTextFillKind.Unsupported, fill.ToString(SaveOptions.DisableFormatting)) : null;
        var click = properties.Element(A + "hlinkClick");
        var id = click?.Attribute(R + "id")?.Value;
        DrawingObjectHyperlink? link = id is not null && relationships is not null && relationships.TryGetValue(id, out var target)
            ? new DrawingObjectHyperlink(target.Target, target.TargetMode, click?.Attribute("tooltip")?.Value) : null;
        return new DrawingShapeTextRunStyle(font?.StartsWith('+') == true ? null : font,
            font?.StartsWith('+') == true ? font : null, Int(properties, "sz") is { } size ? size / 100d : null,
            Flag(properties, "b"), Flag(properties, "i"), properties.Attribute("u")?.Value,
            Int(properties, "baseline"), color, theme, properties.Attribute("lang")?.Value, link) { FillOverride = fillOverride };
    }

    private static bool IsTextFill(XElement element) => element.Name.Namespace == A &&
        element.Name.LocalName is "noFill" or "solidFill" or "gradFill" or "blipFill" or "pattFill" or "grpFill";

    // Only claim a resolved solid when its color and transformations are representable by
    // WorkbookThemeColorReference. Alpha, unsupported colors and other transforms stay opaque.
    private static bool IsSupportedSolidFill(XElement fill)
    {
        var children = fill.Elements().ToArray();
        if (children.Length != 1 || fill.Attributes().Any(attribute => !attribute.IsNamespaceDeclaration)) return false;
        var color = children[0];
        if (color.Attributes().Any(attribute => !attribute.IsNamespaceDeclaration && attribute.Name != "val")) return false;
        if (color.Name == A + "srgbClr") return !color.Elements().Any();
        if (color.Name != A + "schemeClr") return false;
        var transforms = color.Elements().ToArray();
        if (transforms.Length == 0) return true;
        if (transforms.Any(element => element.Attributes().Count(attribute => !attribute.IsNamespaceDeclaration) != 1 ||
            !int.TryParse(element.Attribute("val")?.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var value) || value is < 0 or > 100000 || element.HasElements)) return false;
        if (transforms.Length == 1)
            return (transforms[0].Name == A + "lumMod" || transforms[0].Name == A + "tint" || transforms[0].Name == A + "shade") &&
                int.Parse(transforms[0].Attribute("val")!.Value, CultureInfo.InvariantCulture) != 0;
        if (transforms.Length != 2 || transforms[0].Name != A + "lumMod" || transforms[1].Name != A + "lumOff") return false;
        return int.Parse(transforms[0].Attribute("val")!.Value, CultureInfo.InvariantCulture) +
            int.Parse(transforms[1].Attribute("val")!.Value, CultureInfo.InvariantCulture) == 100000;
    }

    internal static Dictionary<string, DrawingObjectHyperlink> SourceRelationships(DrawingShapeTextDocument document)
    {
        return BuildRelationships(document).Targets;
    }

    private static (Dictionary<string, DrawingObjectHyperlink> Targets, Dictionary<object, string> AuthoredIds)
        BuildRelationships(DrawingShapeTextDocument document)
    {
        var result = new Dictionary<string, DrawingObjectHyperlink>(StringComparer.Ordinal);
        var authored = new Dictionary<object, string>(ReferenceEqualityComparer.Instance);
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
        var hasListLinks = document.DefaultParagraph?.RunStyle?.Hyperlink is not null ||
            document.ListLevels.Any(level => level.Defaults.RunStyle?.Hyperlink is not null);
        var sourceBody = hasListLinks && document.SourceXml is { } sourceXml ? XElement.Parse(sourceXml) : null;
        var sourceList = sourceBody?.Element(A + "lstStyle");
        Add(document, document.DefaultParagraph?.RunStyle, sourceList?.Element(A + "defPPr")?.Element(A + "defRPr"));
        foreach (var level in document.ListLevels)
            Add(level, level.Defaults.RunStyle, sourceList?.Element(A + $"lvl{level.Level + 1}pPr")?.Element(A + "defRPr"));
        foreach (var paragraph in document.Paragraphs)
        {
            var sourceParagraph = paragraph.DefaultRunStyle?.Hyperlink is not null && paragraph.SourceXml is { } paragraphXml
                ? XElement.Parse(paragraphXml) : null;
            Add(paragraph, paragraph.DefaultRunStyle, sourceParagraph?.Element(A + "pPr")?.Element(A + "defRPr"));
            foreach (var inline in paragraph.Inlines)
            {
                var sourceInline = inline.Style?.Hyperlink is not null && inline.SourceXml is { } inlineXml
                    ? XElement.Parse(inlineXml) : null;
                Add(inline, inline.Style, sourceInline?.Element(A + "rPr"));
            }
        }
        return (result, authored);

        void Add(object owner, DrawingShapeTextRunStyle? style, XElement? sourceProperties)
        {
            if (style?.Hyperlink is not { } hyperlink) return;
            if (sourceProperties is null)
            {
                if (authored.ContainsKey(owner)) return;
                var index = authored.Count + 1;
                string newId;
                do { newId = "rIdFreeXRichText" + index++; } while (!usedIds.Add(newId));
                authored.Add(owner, newId);
                result.Add(newId, hyperlink);
                return;
            }
            var id = sourceProperties.Element(A + "hlinkClick")?.Attribute(R + "id")?.Value;
            if (id is null) return;
            if (result.TryGetValue(id, out var previous) &&
                (previous.Target != hyperlink.Target || previous.TargetMode != hyperlink.TargetMode))
                throw new InvalidOperationException("A shape text relationship ID has conflicting targets.");
            result[id] = hyperlink;
        }
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
        Func<DrawingObjectHyperlink, string>? bindRelationship = null, bool preserveOpaqueRelationships = false)
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
            var list = new XElement(A + "lstStyle");
            if (document.DefaultParagraph is { } defaults) list.Add(WriteDefaults("defPPr", defaults, authoredIds.GetValueOrDefault(document)));
            foreach (var level in document.ListLevels.OrderBy(level => level.Level))
                list.Add(WriteDefaults($"lvl{level.Level + 1}pPr", level.Defaults, authoredIds.GetValueOrDefault(level)));
            result = new XElement(Xdr + "txBody", new XElement(A + "bodyPr"), list);
            foreach (var paragraph in document.Paragraphs)
            {
                if (paragraph.SourceXml is { } paragraphXml) { result.Add(XElement.Parse(paragraphXml, LoadOptions.PreserveWhitespace)); continue; }
                var p = new XElement(A + "p");
                if (paragraph.Alignment is not null || paragraph.DefaultRunStyle is not null || paragraph.Level is not null)
                {
                    var properties = WriteDefaults("pPr", new DrawingShapeTextParagraphDefaults(paragraph.Alignment, paragraph.DefaultRunStyle), authoredIds.GetValueOrDefault(paragraph));
                    if (paragraph.Level is { } level) properties.SetAttributeValue("lvl", level);
                    p.Add(properties);
                }
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
                    !relationships.TryGetValue(attribute.Value, out var link))
                {
                    if (preserveOpaqueRelationships) continue;
                    throw new InvalidOperationException("Shape rich text contains drawing relationships that cannot be safely regenerated.");
                }
                if (bindRelationship is null)
                    throw new InvalidOperationException("Shape rich text hyperlinks require a relationship binder.");
                attribute.Value = bindRelationship(link);
            }
        }
        return result;
    }

    private static XElement WriteDefaults(string name, DrawingShapeTextParagraphDefaults defaults, string? relationshipId)
    {
        var properties = new XElement(A + name);
        if (defaults.Alignment is { } align) properties.SetAttributeValue("algn", align switch
        { DrawingShapeTextHAlign.Center => "ctr", DrawingShapeTextHAlign.Right => "r", _ => "l" });
        if (defaults.RunStyle is { } style)
            properties.Add(WriteStyle("defRPr", style, relationshipId));
        return properties;
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
            element.Add(WriteStyle("rPr", style, relationshipId));
        }
        if (inline.Kind != DrawingShapeTextInlineKind.Break) element.Add(new XElement(A + "t", inline.Text));
        return element;
    }

    private static XElement WriteStyle(string name, DrawingShapeTextRunStyle style, string? relationshipId)
    {
        var p = new XElement(A + name);
        if (style.FontSizePoints is { } size) p.SetAttributeValue("sz", Math.Round(size * 100).ToString(CultureInfo.InvariantCulture));
        if (style.Bold is { } bold) p.SetAttributeValue("b", bold ? "1" : "0");
        if (style.Italic is { } italic) p.SetAttributeValue("i", italic ? "1" : "0");
        if (style.Underline is not null) p.SetAttributeValue("u", style.Underline);
        if (style.Baseline is { } baseline) p.SetAttributeValue("baseline", baseline.ToString(CultureInfo.InvariantCulture));
        if (style.Language is not null) p.SetAttributeValue("lang", style.Language);
        if (style.FillOverride is not null && (style.Color is not null || style.ThemeColor is not null))
            throw new InvalidOperationException("A shape text fill override cannot be combined with a solid color.");
        var fill = style.FillOverride is { } fillOverride ? WriteFillOverride(fillOverride) :
            XlsxDrawingColorWriter.ToSolidFill(style.ThemeColor, style.Color, A);
        if (fill is not null) p.Add(fill);
        if ((style.ThemeFont ?? style.FontFamily) is { } font) p.Add(new XElement(A + "latin", new XAttribute("typeface", font)));
        if (style.Hyperlink is { } hyperlink)
            p.Add(new XElement(A + "hlinkClick", new XAttribute(R + "id", relationshipId ?? throw new InvalidOperationException("Missing shape text hyperlink relationship.")),
                hyperlink.Tooltip is null ? null : new XAttribute("tooltip", hyperlink.Tooltip)));
        return p;
    }

    private static XElement WriteFillOverride(DrawingShapeTextFillOverride fill)
    {
        if (!Enum.IsDefined(fill.Kind)) throw new InvalidOperationException("Invalid shape text fill kind.");
        if (fill.Kind == DrawingShapeTextFillKind.NoFill && fill.SourceXml is null) return new XElement(A + "noFill");
        if (fill.SourceXml is null) throw new InvalidOperationException("Unsupported shape text fill requires its source XML.");
        var element = XElement.Parse(fill.SourceXml, LoadOptions.PreserveWhitespace);
        if (!IsTextFill(element) || (fill.Kind == DrawingShapeTextFillKind.NoFill) != (element.Name == A + "noFill"))
            throw new InvalidOperationException("Invalid shape text fill XML.");
        return element;
    }
}
