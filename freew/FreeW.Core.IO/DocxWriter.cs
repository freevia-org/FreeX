using System.IO;
using System.IO.Compression;
using System.Xml.Linq;
using FreeW.Core.Model;
using static FreeW.Core.IO.Ooxml;

namespace FreeW.Core.IO;

/// <summary>
/// Writes a <see cref="TextDocument"/> as a minimal-but-valid WordprocessingML (.docx) package:
/// [Content_Types].xml, package + document relationships, word/document.xml and word/styles.xml.
/// Round-trips with <see cref="DocxReader"/> over the supported formatting subset.
/// </summary>
public static class DocxWriter
{
    private const string OfficeDocumentRel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument";
    private const string StylesRel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles";
    private const string ImageRel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/image";

    public static void Write(TextDocument document, string path)
    {
        using var stream = File.Create(path);
        Write(document, stream);
    }

    public static void Write(TextDocument document, Stream stream)
    {
        // Assign a relationship + media id to every inline image up front so document.xml, the
        // document relationships and the media parts all agree on rId/imageN.png.
        var images = CollectImages(document);

        using var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true);
        WritePart(archive, "[Content_Types].xml", BuildContentTypes(images.Count > 0));
        WritePart(archive, "_rels/.rels", BuildPackageRels());
        WritePart(archive, "word/_rels/document.xml.rels", BuildDocumentRels(images));
        WritePart(archive, "word/document.xml", BuildDocument(document, images));
        WritePart(archive, "word/styles.xml", BuildStyles(document));
        foreach (var image in images)
            WriteBinaryPart(archive, "word/media/" + image.FileName, image.Image.PngBytes);
    }

    /// <summary>An inline image paired with the relationship id, media file name and a unique drawing id.</summary>
    private sealed record ImagePart(InlineImage Image, string RelationshipId, string FileName, uint DrawingId);

    private static List<ImagePart> CollectImages(TextDocument document)
    {
        var images = new List<ImagePart>();
        foreach (var paragraph in document.Paragraphs)
            foreach (var run in paragraph.Runs)
                if (run.Image is { } image)
                {
                    var index = images.Count + 1;
                    images.Add(new ImagePart(image, $"rIdImg{index}", $"image{index}.png", (uint)index));
                }
        return images;
    }

    private static void WritePart(ZipArchive archive, string entryPath, XDocument content)
    {
        var entry = archive.CreateEntry(entryPath, CompressionLevel.Optimal);
        using var entryStream = entry.Open();
        content.Save(entryStream);
    }

    private static void WriteBinaryPart(ZipArchive archive, string entryPath, byte[] content)
    {
        var entry = archive.CreateEntry(entryPath, CompressionLevel.Optimal);
        using var entryStream = entry.Open();
        entryStream.Write(content, 0, content.Length);
    }

    private static XDocument BuildContentTypes(bool includePng) => new(
        new XElement(Ct + "Types",
            new XElement(Ct + "Default", new XAttribute("Extension", "rels"),
                new XAttribute("ContentType", "application/vnd.openxmlformats-package.relationships+xml")),
            new XElement(Ct + "Default", new XAttribute("Extension", "xml"),
                new XAttribute("ContentType", "application/xml")),
            includePng
                ? new XElement(Ct + "Default", new XAttribute("Extension", "png"),
                    new XAttribute("ContentType", "image/png"))
                : null,
            new XElement(Ct + "Override", new XAttribute("PartName", "/word/document.xml"),
                new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml")),
            new XElement(Ct + "Override", new XAttribute("PartName", "/word/styles.xml"),
                new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml"))));

    private static XDocument BuildPackageRels() => new(
        new XElement(Rel + "Relationships",
            new XElement(Rel + "Relationship",
                new XAttribute("Id", "rId1"),
                new XAttribute("Type", OfficeDocumentRel),
                new XAttribute("Target", "word/document.xml"))));

    private static XDocument BuildDocumentRels(IReadOnlyList<ImagePart> images)
    {
        var relationships = new XElement(Rel + "Relationships",
            new XElement(Rel + "Relationship",
                new XAttribute("Id", "rId1"),
                new XAttribute("Type", StylesRel),
                new XAttribute("Target", "styles.xml")));
        foreach (var image in images)
            relationships.Add(new XElement(Rel + "Relationship",
                new XAttribute("Id", image.RelationshipId),
                new XAttribute("Type", ImageRel),
                new XAttribute("Target", "media/" + image.FileName)));
        return new XDocument(relationships);
    }

    private static XDocument BuildDocument(TextDocument document, IReadOnlyList<ImagePart> images)
    {
        // Map each image run to its assigned relationship id by replaying the same walk order.
        var imagesByRun = new Dictionary<Run, ImagePart>();
        var next = 0;
        foreach (var paragraph in document.Paragraphs)
            foreach (var run in paragraph.Runs)
                if (run.Image is not null)
                    imagesByRun[run] = images[next++];

        var body = new XElement(W + "body");
        foreach (var block in document.Blocks)
            body.Add(BuildBlock(block, imagesByRun));
        body.Add(BuildSectionProperties(document.Page));

        return new XDocument(
            new XElement(W + "document",
                new XAttribute(XNamespace.Xmlns + "w", W.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "r", R.NamespaceName),
                body));
    }

    private static XElement BuildBlock(Block block, IReadOnlyDictionary<Run, ImagePart> imagesByRun) => block switch
    {
        Table table => BuildTable(table, imagesByRun),
        Paragraph paragraph => BuildParagraph(paragraph, imagesByRun),
        _ => new XElement(W + "p")
    };

    private static XElement BuildTable(Table table, IReadOnlyDictionary<Run, ImagePart> imagesByRun)
    {
        var tbl = new XElement(W + "tbl", BuildTableProperties(table));
        foreach (var row in table.Rows)
        {
            var tr = new XElement(W + "tr");
            foreach (var cell in row.Cells)
            {
                var tc = new XElement(W + "tc");
                if (cell.Paragraphs.Count == 0)
                    tc.Add(new XElement(W + "p"));
                else
                    foreach (var paragraph in cell.Paragraphs)
                        tc.Add(BuildParagraph(paragraph, imagesByRun));
                tr.Add(tc);
            }
            tbl.Add(tr);
        }
        return tbl;
    }

    private static XElement BuildTableProperties(Table table)
    {
        var tblPr = new XElement(W + "tblPr",
            new XElement(W + "tblW", new XAttribute(W + "w", 0), new XAttribute(W + "type", "auto")));
        if (table.Formatting.Borders)
        {
            XElement Border(string name) => new(W + name,
                new XAttribute(W + "val", "single"),
                new XAttribute(W + "sz", 4),
                new XAttribute(W + "space", 0),
                new XAttribute(W + "color", "auto"));
            tblPr.Add(new XElement(W + "tblBorders",
                Border("top"), Border("left"), Border("bottom"), Border("right"),
                Border("insideH"), Border("insideV")));
        }
        else
        {
            tblPr.Add(new XElement(W + "tblBorders",
                new XElement(W + "top", new XAttribute(W + "val", "none")),
                new XElement(W + "left", new XAttribute(W + "val", "none")),
                new XElement(W + "bottom", new XAttribute(W + "val", "none")),
                new XElement(W + "right", new XAttribute(W + "val", "none")),
                new XElement(W + "insideH", new XAttribute(W + "val", "none")),
                new XElement(W + "insideV", new XAttribute(W + "val", "none"))));
        }
        return tblPr;
    }

    private static XElement BuildParagraph(Paragraph paragraph, IReadOnlyDictionary<Run, ImagePart> imagesByRun)
    {
        var p = new XElement(W + "p");
        var pPr = BuildParagraphProperties(paragraph);
        if (pPr is not null)
            p.Add(pPr);
        foreach (var run in paragraph.Runs)
            p.Add(BuildRun(run, imagesByRun));
        return p;
    }

    private static XElement? BuildParagraphProperties(Paragraph paragraph)
    {
        var pPr = new XElement(W + "pPr");
        if (!string.IsNullOrEmpty(paragraph.StyleId))
            pPr.Add(new XElement(W + "pStyle", new XAttribute(W + "val", paragraph.StyleId)));

        var f = paragraph.Formatting;
        if (f.Alignment != TextAlignment.Left)
            pPr.Add(new XElement(W + "jc", new XAttribute(W + "val", f.Alignment switch
            {
                TextAlignment.Center => "center",
                TextAlignment.Right => "right",
                TextAlignment.Justify => "both",
                _ => "left"
            })));
        if (f.SpaceBeforePt > 0 || f.SpaceAfterPt > 0)
            pPr.Add(new XElement(W + "spacing",
                new XAttribute(W + "before", PointsToDxa(f.SpaceBeforePt)),
                new XAttribute(W + "after", PointsToDxa(f.SpaceAfterPt))));
        if (f.IndentLeftPt > 0 || f.IndentRightPt > 0 || f.FirstLineIndentPt > 0)
            pPr.Add(new XElement(W + "ind",
                new XAttribute(W + "left", PointsToDxa(f.IndentLeftPt)),
                new XAttribute(W + "right", PointsToDxa(f.IndentRightPt)),
                new XAttribute(W + "firstLine", PointsToDxa(f.FirstLineIndentPt))));

        return pPr.HasElements ? pPr : null;
    }

    private static XElement BuildRun(Run run, IReadOnlyDictionary<Run, ImagePart> imagesByRun)
    {
        var r = new XElement(W + "r");
        var rPr = BuildRunProperties(run.Formatting);
        if (rPr is not null)
            r.Add(rPr);
        if (run.Image is not null && imagesByRun.TryGetValue(run, out var part))
            r.Add(BuildDrawing(part));
        else
            r.Add(new XElement(W + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), run.Text));
        return r;
    }

    /// <summary>Builds an inline picture: w:drawing/wp:inline/a:graphic/pic:pic referencing the blip.</summary>
    private static XElement BuildDrawing(ImagePart part)
    {
        var cx = PointsToEmu(part.Image.WidthPt);
        var cy = PointsToEmu(part.Image.HeightPt);
        var docPrId = part.DrawingId;

        return new XElement(W + "drawing",
            new XElement(Wp + "inline",
                new XAttribute(XNamespace.Xmlns + "wp", Wp.NamespaceName),
                new XAttribute("distT", 0), new XAttribute("distB", 0),
                new XAttribute("distL", 0), new XAttribute("distR", 0),
                new XElement(Wp + "extent", new XAttribute("cx", cx), new XAttribute("cy", cy)),
                new XElement(Wp + "effectExtent",
                    new XAttribute("l", 0), new XAttribute("t", 0),
                    new XAttribute("r", 0), new XAttribute("b", 0)),
                new XElement(Wp + "docPr", new XAttribute("id", docPrId), new XAttribute("name", part.FileName)),
                new XElement(A + "graphic",
                    new XAttribute(XNamespace.Xmlns + "a", A.NamespaceName),
                    new XElement(A + "graphicData",
                        new XAttribute("uri", Pic.NamespaceName),
                        new XElement(Pic + "pic",
                            new XAttribute(XNamespace.Xmlns + "pic", Pic.NamespaceName),
                            new XElement(Pic + "nvPicPr",
                                new XElement(Pic + "cNvPr", new XAttribute("id", 0u), new XAttribute("name", part.FileName)),
                                new XElement(Pic + "cNvPicPr")),
                            new XElement(Pic + "blipFill",
                                new XElement(A + "blip", new XAttribute(R + "embed", part.RelationshipId)),
                                new XElement(A + "stretch", new XElement(A + "fillRect"))),
                            new XElement(Pic + "spPr",
                                new XElement(A + "xfrm",
                                    new XElement(A + "off", new XAttribute("x", 0), new XAttribute("y", 0)),
                                    new XElement(A + "ext", new XAttribute("cx", cx), new XAttribute("cy", cy))),
                                new XElement(A + "prstGeom", new XAttribute("prst", "rect"),
                                    new XElement(A + "avLst"))))))));
    }

    private static XElement? BuildRunProperties(RunFormatting f)
    {
        var rPr = new XElement(W + "rPr");
        if (f.FontFamily is { Length: > 0 } family)
            rPr.Add(new XElement(W + "rFonts", new XAttribute(W + "ascii", family), new XAttribute(W + "hAnsi", family)));
        if (f.Bold)
            rPr.Add(new XElement(W + "b"));
        if (f.Italic)
            rPr.Add(new XElement(W + "i"));
        if (f.Strikethrough)
            rPr.Add(new XElement(W + "strike"));
        if (f.Underline)
            rPr.Add(new XElement(W + "u", new XAttribute(W + "val", "single")));
        if (f.ColorHex is { Length: > 0 } color)
            rPr.Add(new XElement(W + "color", new XAttribute(W + "val", color.TrimStart('#'))));
        if (f.FontSizePt is { } size)
        {
            var halfPoints = PointsToHalfPoints(size);
            rPr.Add(new XElement(W + "sz", new XAttribute(W + "val", halfPoints)));
            rPr.Add(new XElement(W + "szCs", new XAttribute(W + "val", halfPoints)));
        }

        return rPr.HasElements ? rPr : null;
    }

    private static XElement BuildSectionProperties(PageSettings page) =>
        new(W + "sectPr",
            new XElement(W + "pgSz",
                new XAttribute(W + "w", PointsToDxa(page.WidthPt)),
                new XAttribute(W + "h", PointsToDxa(page.HeightPt)),
                page.Landscape ? new XAttribute(W + "orient", "landscape") : null),
            new XElement(W + "pgMar",
                new XAttribute(W + "left", PointsToDxa(page.MarginLeftPt)),
                new XAttribute(W + "right", PointsToDxa(page.MarginRightPt)),
                new XAttribute(W + "top", PointsToDxa(page.MarginTopPt)),
                new XAttribute(W + "bottom", PointsToDxa(page.MarginBottomPt))));

    private static XDocument BuildStyles(TextDocument document)
    {
        var styles = new XElement(W + "styles", new XAttribute(XNamespace.Xmlns + "w", W.NamespaceName));
        foreach (var style in document.Styles.Values)
        {
            var element = new XElement(W + "style",
                new XAttribute(W + "type", style.Type == StyleType.Character ? "character" : "paragraph"),
                new XAttribute(W + "styleId", style.Id),
                new XElement(W + "name", new XAttribute(W + "val", style.Name)));
            if (!string.IsNullOrEmpty(style.BasedOnStyleId))
                element.Add(new XElement(W + "basedOn", new XAttribute(W + "val", style.BasedOnStyleId)));
            var rPr = BuildRunProperties(style.Run);
            if (rPr is not null)
                element.Add(rPr);
            styles.Add(element);
        }

        return new XDocument(styles);
    }
}
