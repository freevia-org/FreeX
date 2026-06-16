using System.IO;
using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;
using Free.Shared.Opc;
using FreeW.Core.Model;
using static FreeW.Core.IO.Ooxml;

namespace FreeW.Core.IO;

/// <summary>
/// Reads a WordprocessingML (.docx) package into a <see cref="TextDocument"/>. Uses ZipArchive for
/// the OPC container and the shared <see cref="SecureXmlReaderSettings"/> for hardened XML parsing.
/// Covers the common subset: paragraphs/runs, tables (w:tbl/w:tr/w:tc with paragraph cell content),
/// run formatting (bold/italic/underline/strike, size, colour, font), paragraph formatting
/// (alignment, spacing, indents, style ref) and styles.xml.
/// </summary>
public static class DocxReader
{
    public static TextDocument Read(string path)
    {
        using var stream = File.OpenRead(path);
        return Read(stream);
    }

    public static TextDocument Read(Stream stream)
    {
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        var documentXml = LoadPart(archive, "word/document.xml")
            ?? throw new InvalidDataException("Not a Word document: word/document.xml is missing.");

        var document = new TextDocument();
        ReadCoreProperties(archive, document);
        ReadStyles(archive, document);
        var imageRelationships = ReadImageRelationships(archive);
        var hyperlinkRelationships = ReadHyperlinkRelationships(archive);

        var body = documentXml.Root?.Element(W + "body");
        if (body is not null)
        {
            foreach (var element in body.Elements())
            {
                if (element.Name == W + "p")
                    document.Blocks.Add(ReadParagraph(element, archive, imageRelationships, hyperlinkRelationships));
                else if (element.Name == W + "tbl")
                    document.Blocks.Add(ReadTable(element, archive, imageRelationships, hyperlinkRelationships));
            }
        }

        if (document.Blocks.Count == 0)
            document.Blocks.Add(new Paragraph());

        return document;
    }

    private static XDocument? LoadPart(ZipArchive archive, string entryPath)
    {
        var entry = archive.GetEntry(entryPath);
        if (entry is null)
            return null;
        using var entryStream = entry.Open();
        using var reader = XmlReader.Create(entryStream, SecureXmlReaderSettings.Create());
        return XDocument.Load(reader);
    }

    private static Paragraph ReadParagraph(
        XElement p,
        ZipArchive archive,
        IReadOnlyDictionary<string, string> imageRelationships,
        IReadOnlyDictionary<string, string> hyperlinkRelationships)
    {
        var paragraph = new Paragraph();
        var pPr = p.Element(W + "pPr");
        if (pPr is not null)
        {
            paragraph.StyleId = pPr.Element(W + "pStyle")?.Attribute(W + "val")?.Value;
            paragraph.Formatting = ReadParagraphFormatting(pPr);
        }

        // Iterate in document order so runs nested inside a w:hyperlink keep their position; a
        // w:hyperlink carries an r:id resolving (via the rels) to the external URL its runs link to.
        foreach (var child in p.Elements())
        {
            if (child.Name == W + "r")
            {
                AddRun(paragraph, child, archive, imageRelationships, hyperlinkUrl: null);
            }
            else if (child.Name == W + "hyperlink")
            {
                var id = child.Attribute(R + "id")?.Value;
                var url = id is not null && hyperlinkRelationships.TryGetValue(id, out var target) ? target : null;
                foreach (var r in child.Elements(W + "r"))
                    AddRun(paragraph, r, archive, imageRelationships, url);
            }
        }

        return paragraph;
    }

    private static void AddRun(
        Paragraph paragraph,
        XElement r,
        ZipArchive archive,
        IReadOnlyDictionary<string, string> imageRelationships,
        string? hyperlinkUrl)
    {
        var image = ReadImage(r, archive, imageRelationships);
        if (image is not null)
        {
            paragraph.Runs.Add(new Run(string.Empty) { Image = image, HyperlinkUrl = hyperlinkUrl });
            return;
        }

        var text = string.Concat(r.Elements(W + "t").Select(t => t.Value));
        if (r.Elements(W + "tab").Any())
            text += "\t";
        if (text.Length == 0)
            return;
        paragraph.Runs.Add(new Run(text, ReadRunFormatting(r.Element(W + "rPr"))) { HyperlinkUrl = hyperlinkUrl });
    }

    private static Table ReadTable(
        XElement tbl,
        ZipArchive archive,
        IReadOnlyDictionary<string, string> imageRelationships,
        IReadOnlyDictionary<string, string> hyperlinkRelationships)
    {
        var table = new Table();

        var borders = tbl.Element(W + "tblPr")?.Element(W + "tblBorders");
        table.Formatting = TableFormatting.Default with { Borders = ReadBorders(borders) };

        foreach (var tr in tbl.Elements(W + "tr"))
        {
            var row = new TableRow();
            foreach (var tc in tr.Elements(W + "tc"))
            {
                var cell = new TableCell();
                foreach (var p in tc.Elements(W + "p"))
                    cell.Paragraphs.Add(ReadParagraph(p, archive, imageRelationships, hyperlinkRelationships));
                if (cell.Paragraphs.Count == 0)
                    cell.Paragraphs.Add(new Paragraph());
                row.Cells.Add(cell);
            }
            table.Rows.Add(row);
        }

        return table;
    }

    private static bool ReadBorders(XElement? tblBorders)
    {
        if (tblBorders is null)
            return false;
        // Borders are "on" unless every edge is explicitly "none"/"nil".
        var edges = tblBorders.Elements();
        return edges.Any(e => (e.Attribute(W + "val")?.Value ?? "single") is not ("none" or "nil"));
    }

    /// <summary>Reads an inline picture (w:drawing) from a run into an <see cref="InlineImage"/>, if present.</summary>
    private static InlineImage? ReadImage(XElement run, ZipArchive archive, IReadOnlyDictionary<string, string> imageRelationships)
    {
        var inline = run.Element(W + "drawing")?.Element(Wp + "inline");
        if (inline is null)
            return null;

        var blip = inline.Descendants(A + "blip").FirstOrDefault();
        var relationshipId = blip?.Attribute(R + "embed")?.Value;
        if (relationshipId is null || !imageRelationships.TryGetValue(relationshipId, out var target))
            return null;

        var bytes = LoadMedia(archive, target);
        if (bytes is null)
            return null;

        var extent = inline.Element(Wp + "extent");
        var widthPt = EmuToPoints(extent?.Attribute("cx")?.Value);
        var heightPt = EmuToPoints(extent?.Attribute("cy")?.Value);
        return new InlineImage(bytes, widthPt, heightPt);
    }

    /// <summary>Maps relationship id -> media part path from word/_rels/document.xml.rels.</summary>
    private static Dictionary<string, string> ReadImageRelationships(ZipArchive archive)
    {
        var map = new Dictionary<string, string>();
        var relsXml = LoadPart(archive, "word/_rels/document.xml.rels");
        var relationships = relsXml?.Root?.Elements(Rel + "Relationship");
        if (relationships is null)
            return map;

        foreach (var rel in relationships)
        {
            var id = rel.Attribute("Id")?.Value;
            var target = rel.Attribute("Target")?.Value;
            if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(target))
                continue;
            // Targets in document rels are relative to the word/ folder.
            map[id] = "word/" + target.TrimStart('/');
        }
        return map;
    }

    /// <summary>Maps relationship id -> external hyperlink target (URL) from document.xml.rels.</summary>
    private static Dictionary<string, string> ReadHyperlinkRelationships(ZipArchive archive)
    {
        var map = new Dictionary<string, string>();
        var relsXml = LoadPart(archive, "word/_rels/document.xml.rels");
        var relationships = relsXml?.Root?.Elements(Rel + "Relationship");
        if (relationships is null)
            return map;

        foreach (var rel in relationships)
        {
            if (!rel.Attribute("Type")?.Value.EndsWith("/hyperlink", StringComparison.Ordinal) ?? true)
                continue;
            var id = rel.Attribute("Id")?.Value;
            var target = rel.Attribute("Target")?.Value;
            if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(target))
                map[id] = target; // external targets are stored verbatim (TargetMode="External")
        }
        return map;
    }

    private static byte[]? LoadMedia(ZipArchive archive, string entryPath)
    {
        var entry = archive.GetEntry(entryPath);
        if (entry is null)
            return null;
        using var entryStream = entry.Open();
        using var buffer = new MemoryStream();
        entryStream.CopyTo(buffer);
        return buffer.ToArray();
    }

    internal static ParagraphFormatting ReadParagraphFormatting(XElement pPr)
    {
        var spacing = pPr.Element(W + "spacing");
        var indent = pPr.Element(W + "ind");
        var jc = pPr.Element(W + "jc")?.Attribute(W + "val")?.Value;
        var shading = pPr.Element(W + "shd")?.Attribute(W + "fill")?.Value;

        return ParagraphFormatting.Default with
        {
            Border = ReadParagraphBorder(pPr.Element(W + "pBdr")),
            ShadingColorHex = shading is null or "auto" ? null : "#" + shading.TrimStart('#'),
            Alignment = jc switch
            {
                "center" => TextAlignment.Center,
                "right" or "end" => TextAlignment.Right,
                "both" or "justify" => TextAlignment.Justify,
                _ => TextAlignment.Left
            },
            SpaceBeforePt = DxaToPoints(spacing?.Attribute(W + "before")?.Value),
            SpaceAfterPt = DxaToPoints(spacing?.Attribute(W + "after")?.Value),
            IndentLeftPt = DxaToPoints(indent?.Attribute(W + "left")?.Value ?? indent?.Attribute(W + "start")?.Value),
            IndentRightPt = DxaToPoints(indent?.Attribute(W + "right")?.Value ?? indent?.Attribute(W + "end")?.Value),
            FirstLineIndentPt = DxaToPoints(indent?.Attribute(W + "firstLine")?.Value)
        };
    }

    /// <summary>Reads a paragraph box border (w:pBdr) into a <see cref="ParagraphBorder"/>, or null if absent/off.</summary>
    private static ParagraphBorder? ReadParagraphBorder(XElement? pBdr)
    {
        if (pBdr is null)
            return null;
        // Take the first edge that is actually drawn (val not none/nil); paragraphs use a uniform box.
        var edge = pBdr.Elements().FirstOrDefault(e =>
            (e.Attribute(W + "val")?.Value ?? "single") is not ("none" or "nil"));
        if (edge is null)
            return null;

        var color = edge.Attribute(W + "color")?.Value;
        var width = EighthPointsToPoints(edge.Attribute(W + "sz")?.Value);
        return new ParagraphBorder(
            color is null or "auto" ? "#000000" : "#" + color.TrimStart('#'),
            width > 0 ? width : 0.5);
    }

    internal static RunFormatting ReadRunFormatting(XElement? rPr)
    {
        if (rPr is null)
            return RunFormatting.Default;

        var underline = rPr.Element(W + "u");
        var color = rPr.Element(W + "color")?.Attribute(W + "val")?.Value;
        var highlight = rPr.Element(W + "shd")?.Attribute(W + "fill")?.Value;

        return new RunFormatting
        {
            Bold = ReadToggle(rPr, "b"),
            Italic = ReadToggle(rPr, "i"),
            Underline = underline is not null && (underline.Attribute(W + "val")?.Value ?? "single") != "none",
            Strikethrough = ReadToggle(rPr, "strike"),
            FontFamily = rPr.Element(W + "rFonts")?.Attribute(W + "ascii")?.Value,
            FontSizePt = HalfPointsToPoints(rPr.Element(W + "sz")?.Attribute(W + "val")?.Value),
            ColorHex = color is null or "auto" ? null : "#" + color.TrimStart('#'),
            HighlightColorHex = highlight is null or "auto" ? null : "#" + highlight.TrimStart('#')
        };
    }

    /// <summary>Parses docProps/core.xml into <see cref="TextDocument.Properties"/>; a missing part is fine.</summary>
    private static void ReadCoreProperties(ZipArchive archive, TextDocument document)
    {
        var coreXml = LoadPart(archive, "docProps/core.xml");
        var root = coreXml?.Root;
        if (root is null)
            return;

        var properties = document.Properties;
        properties.Title = Trimmed(root.Element(Dc + "title")?.Value);
        properties.Author = Trimmed(root.Element(Dc + "creator")?.Value);
        properties.Subject = Trimmed(root.Element(Dc + "subject")?.Value);
        properties.Keywords = Trimmed(root.Element(Cp + "keywords")?.Value);
        properties.Comments = Trimmed(root.Element(Dc + "description")?.Value);
        properties.LastModifiedBy = Trimmed(root.Element(Cp + "lastModifiedBy")?.Value);
        properties.Created = ParseW3CDtf(root.Element(DcTerms + "created")?.Value);
        properties.Modified = ParseW3CDtf(root.Element(DcTerms + "modified")?.Value);

        static string? Trimmed(string? value) => string.IsNullOrEmpty(value) ? null : value;
    }

    private static void ReadStyles(ZipArchive archive, TextDocument document)
    {
        var stylesXml = LoadPart(archive, "word/styles.xml");
        var styles = stylesXml?.Root?.Elements(W + "style");
        if (styles is null)
            return;

        foreach (var s in styles)
        {
            var id = s.Attribute(W + "styleId")?.Value;
            if (string.IsNullOrEmpty(id))
                continue;
            var rPr = s.Element(W + "rPr");
            var pPr = s.Element(W + "pPr");
            document.Styles[id] = new DocumentStyle
            {
                Id = id,
                Name = s.Element(W + "name")?.Attribute(W + "val")?.Value ?? id,
                Type = s.Attribute(W + "type")?.Value == "character" ? StyleType.Character : StyleType.Paragraph,
                BasedOnStyleId = s.Element(W + "basedOn")?.Attribute(W + "val")?.Value,
                Run = rPr is null ? RunFormatting.Default : ReadRunFormatting(rPr),
                Paragraph = pPr is null ? ParagraphFormatting.Default : ReadParagraphFormatting(pPr)
            };
        }
    }
}
