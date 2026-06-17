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
    private const string HyperlinkRel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/hyperlink";
    private const string HeaderRel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/header";
    private const string FooterRel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/footer";

    private const string HeaderContentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.header+xml";
    private const string FooterContentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.footer+xml";

    private const string HeaderRelationshipId = "rIdHeader1";
    private const string FooterRelationshipId = "rIdFooter1";
    private const string FootnotesRelationshipId = "rIdFootnotes";
    private const string EndnotesRelationshipId = "rIdEndnotes";
    private const string CommentsRelationshipId = "rIdComments";
    private const string SettingsRelationshipId = "rIdSettings";
    private const string HeaderPartName = "word/header1.xml";
    private const string FooterPartName = "word/footer1.xml";

    // Minimal numbering scheme: one abstract num per list kind, mapped 1:1 to a w:num. Bullets use
    // abstractNumId 0 / numId 1; decimal numbering uses abstractNumId 1 / numId 2; multilevel (legal
    // outline) numbering uses abstractNumId 2 / numId 3. Each abstract num defines 9 levels (ilvl 0..8)
    // so ListLevel maps directly to w:ilvl.
    internal const int BulletNumId = 1;
    internal const int NumberNumId = 2;
    internal const int MultiLevelNumId = 3;
    private const int ListLevelCount = 9;

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
        // Assign an external relationship id to every distinct hyperlink target the same way.
        var hyperlinks = CollectHyperlinks(document);
        // Emit a numbering part only when at least one paragraph is decorated as a list.
        var hasLists = EnumerateParagraphs(document).Any(p => p.Formatting.ListKind != ListKind.None);

        // A header/footer is only emitted as a part when it carries visible content.
        var hasHeader = document.Header is { IsEmpty: false };
        var hasFooter = document.Footer is { IsEmpty: false };

        // A footnotes part is emitted only when the document actually carries footnotes.
        var hasFootnotes = document.Footnotes.Count > 0;

        // An endnotes part is emitted only when the document actually carries endnotes.
        var hasEndnotes = document.Endnotes.Count > 0;

        // A comments part is emitted only when the document actually carries review comments.
        var hasComments = document.Comments.Count > 0;

        // The watermark text is persisted best-effort as a custom document property (docProps/custom.xml),
        // emitted only when a watermark is set.
        var hasWatermark = !string.IsNullOrEmpty(document.Page.Watermark);

        // A word/settings.xml part (carrying w:documentProtection) is emitted only when the document is
        // protected, so unprotected documents round-trip exactly as before (no settings part).
        var hasProtection = document.Protection.IsProtected;

        using var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true);
        WritePart(archive, "[Content_Types].xml", BuildContentTypes(images.Count > 0, hasLists, hasHeader, hasFooter, hasFootnotes, hasEndnotes, hasComments, hasWatermark, hasProtection));
        WritePart(archive, "_rels/.rels", BuildPackageRels(hasWatermark));
        WritePart(archive, "docProps/core.xml", BuildCoreProperties(document.Properties));
        if (hasWatermark)
            WritePart(archive, "docProps/custom.xml", BuildCustomProperties(document.Page.Watermark!));
        WritePart(archive, "word/_rels/document.xml.rels", BuildDocumentRels(images, hyperlinks, hasLists, hasHeader, hasFooter, hasFootnotes, hasEndnotes, hasComments, hasProtection));
        WritePart(archive, "word/document.xml", BuildDocument(document, images, hyperlinks, hasHeader, hasFooter));
        WritePart(archive, "word/styles.xml", BuildStyles(document));
        if (hasProtection)
            WritePart(archive, SettingsPartName.TrimStart('/'), BuildSettings(document.Protection));
        if (hasLists)
            WritePart(archive, "word/numbering.xml", BuildNumbering());
        if (hasHeader)
            WritePart(archive, HeaderPartName, BuildHeaderFooter(W + "hdr", document.Header!));
        if (hasFooter)
            WritePart(archive, FooterPartName, BuildHeaderFooter(W + "ftr", document.Footer!));
        if (hasFootnotes)
            WritePart(archive, FootnotesPartName.TrimStart('/'), BuildFootnotes(document));
        if (hasEndnotes)
            WritePart(archive, EndnotesPartName.TrimStart('/'), BuildEndnotes(document));
        if (hasComments)
            WritePart(archive, CommentsPartName.TrimStart('/'), BuildComments(document));
        foreach (var image in images)
            WriteBinaryPart(archive, "word/media/" + image.FileName, image.Image.PngBytes);
    }

    /// <summary>An inline image paired with the relationship id, media file name and a unique drawing id.</summary>
    private sealed record ImagePart(InlineImage Image, string RelationshipId, string FileName, uint DrawingId);

    private static List<ImagePart> CollectImages(TextDocument document)
    {
        var images = new List<ImagePart>();
        foreach (var paragraph in EnumerateParagraphs(document))
            foreach (var run in paragraph.Runs)
                if (run.Image is { } image)
                {
                    var index = images.Count + 1;
                    images.Add(new ImagePart(image, $"rIdImg{index}", $"image{index}.png", (uint)index));
                }
        return images;
    }

    /// <summary>Maps each distinct hyperlink URL to one external relationship id (rIdLinkN).</summary>
    private static Dictionary<string, string> CollectHyperlinks(TextDocument document)
    {
        var byUrl = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var paragraph in EnumerateParagraphs(document))
            foreach (var run in paragraph.Runs)
                if (run.HyperlinkUrl is { Length: > 0 } url && !byUrl.ContainsKey(url))
                    byUrl[url] = $"rIdLink{byUrl.Count + 1}";
        return byUrl;
    }

    /// <summary>All paragraphs, including those nested inside table cells (where runs can also live).</summary>
    private static IEnumerable<Paragraph> EnumerateParagraphs(TextDocument document)
    {
        foreach (var block in document.Blocks)
        {
            if (block is Paragraph paragraph)
                yield return paragraph;
            else if (block is Table table)
                foreach (var row in table.Rows)
                    foreach (var cell in row.Cells)
                        foreach (var cellParagraph in cell.Paragraphs)
                            yield return cellParagraph;
        }
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

    private static XDocument BuildContentTypes(bool includePng, bool includeNumbering, bool hasHeader, bool hasFooter, bool hasFootnotes, bool hasEndnotes, bool hasComments, bool hasWatermark, bool hasProtection) => new(
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
                new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml")),
            includeNumbering
                ? new XElement(Ct + "Override", new XAttribute("PartName", NumberingPartName),
                    new XAttribute("ContentType", NumberingContentType))
                : null,
            hasHeader
                ? new XElement(Ct + "Override", new XAttribute("PartName", "/" + HeaderPartName),
                    new XAttribute("ContentType", HeaderContentType))
                : null,
            hasFooter
                ? new XElement(Ct + "Override", new XAttribute("PartName", "/" + FooterPartName),
                    new XAttribute("ContentType", FooterContentType))
                : null,
            hasFootnotes
                ? new XElement(Ct + "Override", new XAttribute("PartName", FootnotesPartName),
                    new XAttribute("ContentType", FootnotesContentType))
                : null,
            hasEndnotes
                ? new XElement(Ct + "Override", new XAttribute("PartName", EndnotesPartName),
                    new XAttribute("ContentType", EndnotesContentType))
                : null,
            hasComments
                ? new XElement(Ct + "Override", new XAttribute("PartName", CommentsPartName),
                    new XAttribute("ContentType", CommentsContentType))
                : null,
            hasProtection
                ? new XElement(Ct + "Override", new XAttribute("PartName", SettingsPartName),
                    new XAttribute("ContentType", SettingsContentType))
                : null,
            new XElement(Ct + "Override", new XAttribute("PartName", CorePropertiesPartName),
                new XAttribute("ContentType", CorePropertiesContentType)),
            hasWatermark
                ? new XElement(Ct + "Override", new XAttribute("PartName", CustomPropertiesPartName),
                    new XAttribute("ContentType", CustomPropertiesContentType))
                : null));

    private static XDocument BuildPackageRels(bool hasWatermark) => new(
        new XElement(Rel + "Relationships",
            new XElement(Rel + "Relationship",
                new XAttribute("Id", "rId1"),
                new XAttribute("Type", OfficeDocumentRel),
                new XAttribute("Target", "word/document.xml")),
            new XElement(Rel + "Relationship",
                new XAttribute("Id", "rIdCore"),
                new XAttribute("Type", CorePropertiesRelType),
                new XAttribute("Target", "docProps/core.xml")),
            hasWatermark
                ? new XElement(Rel + "Relationship",
                    new XAttribute("Id", "rIdCustom"),
                    new XAttribute("Type", CustomPropertiesRelType),
                    new XAttribute("Target", "docProps/custom.xml"))
                : null));

    /// <summary>
    /// Builds docProps/custom.xml carrying the page watermark text as a single named custom property
    /// (<see cref="WatermarkPropertyName"/>). This is a standards-compliant OPC custom-properties part,
    /// so the watermark text round-trips even though it is not a true Word VML watermark.
    /// </summary>
    private static XDocument BuildCustomProperties(string watermark) => new(
        new XElement(CustomProps + "Properties",
            new XAttribute(XNamespace.Xmlns + "vt", VtVariant.NamespaceName),
            new XElement(CustomProps + "property",
                new XAttribute("fmtid", "{D5CDD505-2E9C-101B-9397-08002B2CF9AE}"),
                new XAttribute("pid", "2"),
                new XAttribute("name", WatermarkPropertyName),
                new XElement(VtVariant + "lpwstr", watermark))));

    /// <summary>Builds docProps/core.xml from <see cref="DocumentProperties"/>, emitting only set values.</summary>
    private static XDocument BuildCoreProperties(DocumentProperties properties)
    {
        var core = new XElement(Cp + "coreProperties",
            new XAttribute(XNamespace.Xmlns + "cp", Cp.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "dc", Dc.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "dcterms", DcTerms.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "dcmitype", DcmiType.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "xsi", Xsi.NamespaceName));

        AddIfSet(core, Dc + "title", properties.Title);
        AddIfSet(core, Dc + "creator", properties.Author);
        AddIfSet(core, Dc + "subject", properties.Subject);
        AddIfSet(core, Cp + "keywords", properties.Keywords);
        AddIfSet(core, Dc + "description", properties.Comments);
        AddIfSet(core, Cp + "lastModifiedBy", properties.LastModifiedBy);
        AddTimestamp(core, DcTerms + "created", properties.Created);
        AddTimestamp(core, DcTerms + "modified", properties.Modified);

        return new XDocument(core);

        static void AddIfSet(XElement parent, XName name, string? value)
        {
            if (!string.IsNullOrEmpty(value))
                parent.Add(new XElement(name, value));
        }

        static void AddTimestamp(XElement parent, XName name, DateTimeOffset? value)
        {
            if (value is { } v)
                parent.Add(new XElement(name,
                    new XAttribute(Xsi + "type", "dcterms:W3CDTF"),
                    ToW3CDtf(v)));
        }
    }

    private static XDocument BuildDocumentRels(
        IReadOnlyList<ImagePart> images,
        IReadOnlyDictionary<string, string> hyperlinks,
        bool includeNumbering,
        bool hasHeader,
        bool hasFooter,
        bool hasFootnotes,
        bool hasEndnotes,
        bool hasComments,
        bool hasProtection)
    {
        var relationships = new XElement(Rel + "Relationships",
            new XElement(Rel + "Relationship",
                new XAttribute("Id", "rId1"),
                new XAttribute("Type", StylesRel),
                new XAttribute("Target", "styles.xml")));
        if (hasProtection)
            relationships.Add(new XElement(Rel + "Relationship",
                new XAttribute("Id", SettingsRelationshipId),
                new XAttribute("Type", SettingsRelType),
                new XAttribute("Target", "settings.xml")));
        if (includeNumbering)
            relationships.Add(new XElement(Rel + "Relationship",
                new XAttribute("Id", "rIdNumbering"),
                new XAttribute("Type", NumberingRelType),
                new XAttribute("Target", "numbering.xml")));
        if (hasHeader)
            relationships.Add(new XElement(Rel + "Relationship",
                new XAttribute("Id", HeaderRelationshipId),
                new XAttribute("Type", HeaderRel),
                new XAttribute("Target", "header1.xml")));
        if (hasFooter)
            relationships.Add(new XElement(Rel + "Relationship",
                new XAttribute("Id", FooterRelationshipId),
                new XAttribute("Type", FooterRel),
                new XAttribute("Target", "footer1.xml")));
        if (hasFootnotes)
            relationships.Add(new XElement(Rel + "Relationship",
                new XAttribute("Id", FootnotesRelationshipId),
                new XAttribute("Type", FootnotesRelType),
                new XAttribute("Target", "footnotes.xml")));
        if (hasEndnotes)
            relationships.Add(new XElement(Rel + "Relationship",
                new XAttribute("Id", EndnotesRelationshipId),
                new XAttribute("Type", EndnotesRelType),
                new XAttribute("Target", "endnotes.xml")));
        if (hasComments)
            relationships.Add(new XElement(Rel + "Relationship",
                new XAttribute("Id", CommentsRelationshipId),
                new XAttribute("Type", CommentsRelType),
                new XAttribute("Target", "comments.xml")));
        foreach (var image in images)
            relationships.Add(new XElement(Rel + "Relationship",
                new XAttribute("Id", image.RelationshipId),
                new XAttribute("Type", ImageRel),
                new XAttribute("Target", "media/" + image.FileName)));
        foreach (var (url, relationshipId) in hyperlinks)
            relationships.Add(new XElement(Rel + "Relationship",
                new XAttribute("Id", relationshipId),
                new XAttribute("Type", HyperlinkRel),
                new XAttribute("Target", url),
                new XAttribute("TargetMode", "External")));
        return new XDocument(relationships);
    }

    private static XDocument BuildDocument(
        TextDocument document,
        IReadOnlyList<ImagePart> images,
        IReadOnlyDictionary<string, string> hyperlinks,
        bool hasHeader,
        bool hasFooter)
    {
        // Reset the document-scoped bookmark + revision id counters so ids start at 1 each write.
        System.Threading.Interlocked.Exchange(ref _bookmarkId, 0);
        System.Threading.Interlocked.Exchange(ref _revisionId, 0);

        // Map each image run to its assigned relationship id by replaying the same walk order.
        var imagesByRun = new Dictionary<Run, ImagePart>();
        var next = 0;
        foreach (var paragraph in EnumerateParagraphs(document))
            foreach (var run in paragraph.Runs)
                if (run.Image is not null)
                    imagesByRun[run] = images[next++];

        var body = new XElement(W + "body");
        foreach (var block in document.Blocks)
            body.Add(BuildBlock(block, imagesByRun, hyperlinks));
        body.Add(BuildSectionProperties(document.Page, hasHeader, hasFooter));

        return new XDocument(
            new XElement(W + "document",
                new XAttribute(XNamespace.Xmlns + "w", W.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "r", R.NamespaceName),
                // w14 carries the checkbox content control element (w14:checkbox in a w:sdtPr).
                new XAttribute(XNamespace.Xmlns + "w14", W14.NamespaceName),
                body));
    }

    /// <summary>Builds a header (w:hdr) or footer (w:ftr) part from its model paragraphs.</summary>
    private static XDocument BuildHeaderFooter(XName rootName, HeaderFooter content)
    {
        var root = new XElement(rootName,
            new XAttribute(XNamespace.Xmlns + "w", W.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "r", R.NamespaceName));

        // Header/footer runs do not carry inline images (the body image walk does not reach them).
        var noImages = new Dictionary<Run, ImagePart>();
        var noHyperlinks = new Dictionary<string, string>(StringComparer.Ordinal);

        if (content.Paragraphs.Count == 0)
            root.Add(new XElement(W + "p"));
        else
            foreach (var paragraph in content.Paragraphs)
                root.Add(BuildParagraph(paragraph, noImages, noHyperlinks));

        return new XDocument(root);
    }

    /// <summary>
    /// Builds word/footnotes.xml (w:footnotes). Emits the two conventional separator footnotes
    /// (w:footnoteSeparator id=-1, w:continuationSeparator id=0) for Word-friendliness, then one
    /// w:footnote w:id="N" per modelled footnote (ascending id), each holding its paragraphs.
    /// </summary>
    private static XDocument BuildFootnotes(TextDocument document)
    {
        var footnotes = new XElement(W + "footnotes",
            new XAttribute(XNamespace.Xmlns + "w", W.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "r", R.NamespaceName));

        XElement Separator(int id, string type) =>
            new(W + "footnote",
                new XAttribute(W + "type", type),
                new XAttribute(W + "id", id),
                new XElement(W + "p",
                    new XElement(W + "r", new XElement(W + type))));

        footnotes.Add(Separator(-1, "separator"));
        footnotes.Add(Separator(0, "continuationSeparator"));

        // Footnote paragraphs carry no inline images or hyperlinks (those walks target the body).
        var noImages = new Dictionary<Run, ImagePart>();
        var noHyperlinks = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var footnote in document.Footnotes.Values.OrderBy(f => f.Id))
        {
            var element = new XElement(W + "footnote", new XAttribute(W + "id", footnote.Id));
            if (footnote.Content.Count == 0)
                element.Add(new XElement(W + "p"));
            else
                foreach (var paragraph in footnote.Content)
                    element.Add(BuildParagraph(paragraph, noImages, noHyperlinks));
            footnotes.Add(element);
        }

        return new XDocument(footnotes);
    }

    /// <summary>
    /// Builds word/endnotes.xml (w:endnotes). Emits the two conventional separator endnotes
    /// (w:endnoteSeparator id=-1, w:continuationSeparator id=0) for Word-friendliness, then one
    /// w:endnote w:id="N" per modelled endnote (ascending id), each holding its paragraphs. Mirrors
    /// <see cref="BuildFootnotes"/>.
    /// </summary>
    private static XDocument BuildEndnotes(TextDocument document)
    {
        var endnotes = new XElement(W + "endnotes",
            new XAttribute(XNamespace.Xmlns + "w", W.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "r", R.NamespaceName));

        XElement Separator(int id, string type) =>
            new(W + "endnote",
                new XAttribute(W + "type", type),
                new XAttribute(W + "id", id),
                new XElement(W + "p",
                    new XElement(W + "r", new XElement(W + type))));

        endnotes.Add(Separator(-1, "separator"));
        endnotes.Add(Separator(0, "continuationSeparator"));

        // Endnote paragraphs carry no inline images or hyperlinks (those walks target the body).
        var noImages = new Dictionary<Run, ImagePart>();
        var noHyperlinks = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var endnote in document.Endnotes.Values.OrderBy(e => e.Id))
        {
            var element = new XElement(W + "endnote", new XAttribute(W + "id", endnote.Id));
            if (endnote.Content.Count == 0)
                element.Add(new XElement(W + "p"));
            else
                foreach (var paragraph in endnote.Content)
                    element.Add(BuildParagraph(paragraph, noImages, noHyperlinks));
            endnotes.Add(element);
        }

        return new XDocument(endnotes);
    }

    /// <summary>
    /// Builds word/comments.xml (w:comments): one w:comment w:id="N" per modelled comment (ascending
    /// id), each carrying w:author / w:initials and — when set — an explicit w:date, plus the comment's
    /// paragraphs. The date is only emitted when the model carries one, keeping the writer deterministic.
    /// </summary>
    private static XDocument BuildComments(TextDocument document)
    {
        var comments = new XElement(W + "comments",
            new XAttribute(XNamespace.Xmlns + "w", W.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "r", R.NamespaceName));

        // Comment paragraphs carry no inline images or hyperlinks (those walks target the body).
        var noImages = new Dictionary<Run, ImagePart>();
        var noHyperlinks = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var comment in document.Comments.Values.OrderBy(c => c.Id))
        {
            var element = new XElement(W + "comment",
                new XAttribute(W + "id", comment.Id),
                new XAttribute(W + "author", comment.Author),
                new XAttribute(W + "initials", comment.Initials));
            if (comment.DateXml is { Length: > 0 } date)
                element.Add(new XAttribute(W + "date", date));

            if (comment.Content.Count == 0)
                element.Add(new XElement(W + "p"));
            else
                foreach (var paragraph in comment.Content)
                    element.Add(BuildParagraph(paragraph, noImages, noHyperlinks));
            comments.Add(element);
        }

        return new XDocument(comments);
    }

    private static XElement BuildBlock(Block block, IReadOnlyDictionary<Run, ImagePart> imagesByRun, IReadOnlyDictionary<string, string> hyperlinks) => block switch
    {
        Table table => BuildTable(table, imagesByRun, hyperlinks),
        Paragraph paragraph => BuildParagraph(paragraph, imagesByRun, hyperlinks),
        _ => new XElement(W + "p")
    };

    private static XElement BuildTable(Table table, IReadOnlyDictionary<Run, ImagePart> imagesByRun, IReadOnlyDictionary<string, string> hyperlinks)
    {
        var tbl = new XElement(W + "tbl", BuildTableProperties(table));

        // The table grid (one w:gridCol per column) follows w:tblPr when explicit widths are known.
        if (table.ColumnWidthsPt.Count > 0)
        {
            var grid = new XElement(W + "tblGrid");
            foreach (var widthPt in table.ColumnWidthsPt)
                grid.Add(new XElement(W + "gridCol", new XAttribute(W + "w", PointsToDxa(widthPt))));
            tbl.Add(grid);
        }

        foreach (var row in table.Rows)
        {
            var tr = new XElement(W + "tr");
            foreach (var cell in row.Cells)
            {
                var tc = new XElement(W + "tc");
                var tcPr = BuildCellProperties(cell);
                if (tcPr is not null)
                    tc.Add(tcPr);
                if (cell.Paragraphs.Count == 0)
                    tc.Add(new XElement(W + "p"));
                else
                    foreach (var paragraph in cell.Paragraphs)
                        tc.Add(BuildParagraph(paragraph, imagesByRun, hyperlinks));
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

    // Cell properties (w:tcPr): emitted only when the cell has an explicit width and/or shading, so
    // plain cells stay unchanged. Width is w:tcW (dxa); shading mirrors paragraph w:shd (fill colour).
    private static XElement? BuildCellProperties(TableCell cell)
    {
        var tcPr = new XElement(W + "tcPr");
        if (cell.WidthPt is { } widthPt)
            tcPr.Add(new XElement(W + "tcW",
                new XAttribute(W + "w", PointsToDxa(widthPt)),
                new XAttribute(W + "type", "dxa")));
        if (cell.ShadingColorHex is { Length: > 0 } shading)
            tcPr.Add(new XElement(W + "shd",
                new XAttribute(W + "val", "clear"),
                new XAttribute(W + "color", "auto"),
                new XAttribute(W + "fill", shading.TrimStart('#'))));
        return tcPr.HasElements ? tcPr : null;
    }

    // Bookmark ids are scoped to the whole document; one monotonically increasing counter keeps
    // every w:bookmarkStart/w:bookmarkEnd pair's w:id unique across all paragraphs.
    private static int _bookmarkId;

    // Revision (w:ins/w:del) ids are scoped to the whole document; this counter keeps each wrapper's
    // w:id unique across all paragraphs. Reset alongside _bookmarkId at the start of each document.
    private static int _revisionId;

    /// <summary>True when two runs carry the same tracked-change kind, author and date (so they coalesce).</summary>
    private static bool SameRevision(Run a, Run b) =>
        a.Revision == b.Revision
        && string.Equals(a.RevisionAuthor, b.RevisionAuthor, StringComparison.Ordinal)
        && string.Equals(a.RevisionDateXml, b.RevisionDateXml, StringComparison.Ordinal);

    /// <summary>
    /// Builds an empty w:ins (insertion) or w:del (deletion) wrapper carrying a unique w:id plus the
    /// run's author/date attributes. The caller fills it with the wrapped run/hyperlink elements. The
    /// run is assumed to carry a non-None revision.
    /// </summary>
    private static XElement NewRevisionWrapper(Run run)
    {
        var name = run.Revision == RevisionKind.Deleted ? "del" : "ins";
        var wrapper = new XElement(W + name,
            new XAttribute(W + "id", System.Threading.Interlocked.Increment(ref _revisionId)));
        if (run.RevisionAuthor is { Length: > 0 } author)
            wrapper.Add(new XAttribute(W + "author", author));
        if (run.RevisionDateXml is { Length: > 0 } date)
            wrapper.Add(new XAttribute(W + "date", date));
        return wrapper;
    }

    /// <summary>
    /// Builds the w:sdtPr (content-control properties) for a content control. Emits w:tag / w:alias when
    /// set, then the control-kind element: w:text for a plain-text control, or a w14:checkbox carrying the
    /// checked state (w14:checked val="1"/"0") for a checkbox. This is the minimal valid shape FreeW's own
    /// reader recovers (see <see cref="DocxReader"/>).
    /// </summary>
    private static XElement BuildSdtProperties(ContentControl control)
    {
        var sdtPr = new XElement(W + "sdtPr");
        if (control.Alias is { Length: > 0 } alias)
            sdtPr.Add(new XElement(W + "alias", new XAttribute(W + "val", alias)));
        if (control.Tag is { Length: > 0 } tag)
            sdtPr.Add(new XElement(W + "tag", new XAttribute(W + "val", tag)));
        if (control.Kind == ContentControlKind.CheckBox)
            sdtPr.Add(new XElement(W14 + "checkbox",
                new XElement(W14 + "checked", new XAttribute(W14 + "val", control.Checked ? "1" : "0"))));
        else
            sdtPr.Add(new XElement(W + "text"));
        return sdtPr;
    }

    private static XElement BuildParagraph(Paragraph paragraph, IReadOnlyDictionary<Run, ImagePart> imagesByRun, IReadOnlyDictionary<string, string> hyperlinks)
    {
        var p = new XElement(W + "p");
        var pPr = BuildParagraphProperties(paragraph);
        if (pPr is not null)
            p.Add(pPr);

        // A bookmarked paragraph is bracketed by a w:bookmarkStart/w:bookmarkEnd pair (siblings of the
        // runs) sharing one w:id; the start also carries the bookmark's w:name.
        var bookmarkId = -1;
        if (paragraph.BookmarkName is { Length: > 0 } bookmarkName)
        {
            bookmarkId = System.Threading.Interlocked.Increment(ref _bookmarkId);
            p.Add(new XElement(W + "bookmarkStart",
                new XAttribute(W + "id", bookmarkId),
                new XAttribute(W + "name", bookmarkName)));
        }

        // Wrap maximal spans of consecutive runs sharing the same hyperlink target in a single
        // w:hyperlink. External links reference the URL's relationship id (r:id); internal links
        // reference a bookmark name via w:anchor (no relationship).
        //
        // Review comments overlay this: a run carrying a CommentId (other than the textless reference
        // run) is bracketed by a w:commentRangeStart/End pair sharing that id, emitted as siblings of
        // the runs. The textless reference run (IsCommentReference) serialises as a w:commentReference
        // run placed just after the matching range end. Comment-covered runs are not also hyperlinks in
        // the editor, so the two wrappings do not interleave in practice.
        //
        // Tracked changes overlay this again: a run carrying Revision != None is wrapped in a w:ins
        // (insertion) or w:del (deletion) element carrying the author/date attributes; the wrapped run's
        // text serialises as w:delText (not w:t) inside a w:del so Word treats it as deleted content.
        // Consecutive runs sharing the same revision kind/author/date coalesce into one wrapper. The
        // wrapper sits between the paragraph (or hyperlink) and the run elements, while comment-range and
        // bookmark markers stay as paragraph-level siblings.
        var i = 0;
        var runs = paragraph.Runs;
        var openCommentId = (int?)null;

        // The current open revision wrapper (w:ins/w:del) and the run it was opened for; run-level
        // elements are added through Content(...) so they land inside the wrapper when one is open.
        XElement? revisionWrapper = null;
        Run? revisionKey = null;

        void FlushRevision()
        {
            if (revisionWrapper is not null)
            {
                p.Add(revisionWrapper);
                revisionWrapper = null;
                revisionKey = null;
            }
        }

        // Route one run-level element (a w:r or w:hyperlink) through the active revision wrapper,
        // (re)opening or closing it to match the run's revision mark before adding the element.
        void Content(Run run, XElement element)
        {
            if (run.Revision == RevisionKind.None)
            {
                FlushRevision();
                p.Add(element);
                return;
            }
            if (revisionKey is null || !SameRevision(revisionKey, run))
            {
                FlushRevision();
                revisionWrapper = NewRevisionWrapper(run);
                revisionKey = run;
            }
            revisionWrapper!.Add(element);
        }

        while (i < runs.Count)
        {
            // Update the open comment range to match this run before emitting it. The textless
            // reference run does not open/extend a range; it only emits the reference marker below.
            var coveringId = runs[i].IsCommentReference ? null : runs[i].CommentId;
            if (openCommentId != coveringId)
            {
                // Comment range markers are paragraph-level siblings, not revision content.
                FlushRevision();
                if (openCommentId is { } closing)
                    p.Add(new XElement(W + "commentRangeEnd", new XAttribute(W + "id", closing)));
                if (coveringId is { } opening)
                    p.Add(new XElement(W + "commentRangeStart", new XAttribute(W + "id", opening)));
                openCommentId = coveringId;
            }

            // A content control (w:sdt) wraps the maximal span of consecutive runs sharing the same
            // ContentControl instance. The wrapped run(s) keep their ordinary w:r form inside w:sdtContent;
            // the sdt itself still routes through the revision wrapper so a control can sit inside a
            // tracked change. Content controls are not also hyperlinks/comments in practice.
            var control = runs[i].Control;
            if (control is not null)
            {
                var head = runs[i];
                var content = new XElement(W + "sdtContent");
                while (i < runs.Count && ReferenceEquals(runs[i].Control, control)
                    && (runs[i].IsCommentReference ? null : runs[i].CommentId) == openCommentId
                    && SameRevision(head, runs[i]))
                    content.Add(BuildRun(runs[i++], imagesByRun));
                var sdt = new XElement(W + "sdt", BuildSdtProperties(control), content);
                Content(head, sdt);
                continue;
            }

            var url = runs[i].HyperlinkUrl;
            var anchor = runs[i].HyperlinkAnchor;
            if (url is { Length: > 0 } && hyperlinks.TryGetValue(url, out var relationshipId))
            {
                var hyperlink = new XElement(W + "hyperlink", new XAttribute(R + "id", relationshipId));
                var head = runs[i];
                while (i < runs.Count && runs[i].HyperlinkUrl == url && (runs[i].IsCommentReference ? null : runs[i].CommentId) == openCommentId && SameRevision(head, runs[i]))
                    hyperlink.Add(BuildRun(runs[i++], imagesByRun));
                Content(head, hyperlink);
            }
            else if (anchor is { Length: > 0 })
            {
                var hyperlink = new XElement(W + "hyperlink", new XAttribute(W + "anchor", anchor));
                var head = runs[i];
                while (i < runs.Count && runs[i].HyperlinkAnchor == anchor && (runs[i].IsCommentReference ? null : runs[i].CommentId) == openCommentId && SameRevision(head, runs[i]))
                    hyperlink.Add(BuildRun(runs[i++], imagesByRun));
                Content(head, hyperlink);
            }
            else
            {
                var run = runs[i++];
                Content(run, BuildRun(run, imagesByRun));
            }
        }

        // Close any still-open revision wrapper, then any still-open comment range, at paragraph end.
        FlushRevision();
        if (openCommentId is { } trailing)
            p.Add(new XElement(W + "commentRangeEnd", new XAttribute(W + "id", trailing)));

        if (bookmarkId >= 0)
            p.Add(new XElement(W + "bookmarkEnd", new XAttribute(W + "id", bookmarkId)));

        return p;
    }

    private static XElement? BuildParagraphProperties(Paragraph paragraph)
    {
        var pPr = new XElement(W + "pPr");
        if (!string.IsNullOrEmpty(paragraph.StyleId))
            pPr.Add(new XElement(W + "pStyle", new XAttribute(W + "val", paragraph.StyleId)));

        var f = paragraph.Formatting;
        // Force a page break before this paragraph (w:pageBreakBefore); Word honours it when paginating.
        if (f.PageBreakBefore)
            pPr.Add(new XElement(W + "pageBreakBefore"));
        if (f.ListKind != ListKind.None)
        {
            var numId = f.ListKind switch
            {
                ListKind.Number => NumberNumId,
                ListKind.MultiLevel => MultiLevelNumId,
                _ => BulletNumId
            };
            var level = Math.Clamp(f.ListLevel, 0, ListLevelCount - 1);
            pPr.Add(new XElement(W + "numPr",
                new XElement(W + "ilvl", new XAttribute(W + "val", level)),
                new XElement(W + "numId", new XAttribute(W + "val", numId))));
        }
        if (f.Alignment != TextAlignment.Left)
            pPr.Add(new XElement(W + "jc", new XAttribute(W + "val", f.Alignment switch
            {
                TextAlignment.Center => "center",
                TextAlignment.Right => "right",
                TextAlignment.Justify => "both",
                _ => "left"
            })));
        // Tab stops (w:tabs): one w:tab per stop, position in dxa, alignment via w:val. Mirrors how
        // w:ind/w:spacing carry their dxa values.
        if (f.TabStops.Count > 0)
            pPr.Add(new XElement(W + "tabs",
                f.TabStops.Select(t => new XElement(W + "tab",
                    new XAttribute(W + "val", t.Alignment switch
                    {
                        TabStopAlignment.Center => "center",
                        TabStopAlignment.Right => "right",
                        TabStopAlignment.Decimal => "decimal",
                        _ => "left"
                    }),
                    new XAttribute(W + "pos", PointsToDxa(t.PositionPt))))));
        if (f.SpaceBeforePt > 0 || f.SpaceAfterPt > 0)
            pPr.Add(new XElement(W + "spacing",
                new XAttribute(W + "before", PointsToDxa(f.SpaceBeforePt)),
                new XAttribute(W + "after", PointsToDxa(f.SpaceAfterPt))));
        if (f.IndentLeftPt > 0 || f.IndentRightPt > 0 || f.FirstLineIndentPt > 0)
            pPr.Add(new XElement(W + "ind",
                new XAttribute(W + "left", PointsToDxa(f.IndentLeftPt)),
                new XAttribute(W + "right", PointsToDxa(f.IndentRightPt)),
                new XAttribute(W + "firstLine", PointsToDxa(f.FirstLineIndentPt))));
        // Paragraph border (w:pBdr): a uniform box (all four edges) by default, or a bottom-only edge
        // when the border is a horizontal rule. Each edge shares one colour/width, analogous to w:tblBorders.
        if (f.Border is { } border)
        {
            XElement Edge(string name) => new(W + name,
                new XAttribute(W + "val", "single"),
                new XAttribute(W + "sz", PointsToEighthPoints(border.WidthPt)),
                new XAttribute(W + "space", 0),
                new XAttribute(W + "color", border.ColorHex.TrimStart('#')));
            pPr.Add(border.BottomOnly
                ? new XElement(W + "pBdr", Edge("bottom"))
                : new XElement(W + "pBdr", Edge("top"), Edge("left"), Edge("bottom"), Edge("right")));
        }
        // Paragraph shading (background fill), mirroring run-level w:shd highlight.
        if (f.ShadingColorHex is { Length: > 0 } shading)
            pPr.Add(new XElement(W + "shd",
                new XAttribute(W + "val", "clear"),
                new XAttribute(W + "color", "auto"),
                new XAttribute(W + "fill", shading.TrimStart('#'))));

        return pPr.HasElements ? pPr : null;
    }

    /// <summary>
    /// Maps a field kind to the WordprocessingML w:fldSimple/@w:instr keyword (with the surrounding
    /// spaces Word writes). Returns null for <see cref="RunFieldKind.None"/> (an ordinary text run).
    /// </summary>
    private static string? FieldInstruction(RunFieldKind kind) => kind switch
    {
        RunFieldKind.PageNumber => " PAGE ",
        RunFieldKind.Date => " DATE ",
        RunFieldKind.Time => " TIME ",
        RunFieldKind.FileName => " FILENAME ",
        RunFieldKind.Author => " AUTHOR ",
        RunFieldKind.NumPages => " NUMPAGES ",
        _ => null
    };

    private static XElement BuildRun(Run run, IReadOnlyDictionary<Run, ImagePart> imagesByRun)
    {
        // A document field emits a self-contained w:fldSimple wrapping a run; the wrapped run's w:t
        // carries the last-known/cached value as fallback text for field-unaware consumers. The
        // w:instr keyword identifies the field kind (PAGE, DATE, TIME, FILENAME, AUTHOR, NUMPAGES).
        if (FieldInstruction(run.FieldKind) is { } instruction)
            return new XElement(W + "fldSimple",
                new XAttribute(W + "instr", instruction),
                BuildTextRun(run, imagesByRun));

        // A footnote reference is a superscript run carrying a w:footnoteReference (no literal text);
        // the rPr forces vertAlign=superscript so field-unaware viewers still show a raised marker.
        if (run.FootnoteId is { } footnoteId)
            return new XElement(W + "r",
                new XElement(W + "rPr",
                    new XElement(W + "vertAlign", new XAttribute(W + "val", "superscript"))),
                new XElement(W + "footnoteReference", new XAttribute(W + "id", footnoteId)));

        // An endnote reference is a superscript run carrying a w:endnoteReference (no literal text);
        // the rPr forces vertAlign=superscript so field-unaware viewers still show a raised marker.
        if (run.EndnoteId is { } endnoteId)
            return new XElement(W + "r",
                new XElement(W + "rPr",
                    new XElement(W + "vertAlign", new XAttribute(W + "val", "superscript"))),
                new XElement(W + "endnoteReference", new XAttribute(W + "id", endnoteId)));

        // The textless comment anchor run carries the w:commentReference for its id (no literal text).
        if (run is { IsCommentReference: true, CommentId: { } commentRefId })
            return new XElement(W + "r",
                new XElement(W + "commentReference", new XAttribute(W + "id", commentRefId)));

        return BuildTextRun(run, imagesByRun);
    }

    private static XElement BuildTextRun(Run run, IReadOnlyDictionary<Run, ImagePart> imagesByRun)
    {
        var r = new XElement(W + "r");
        var rPr = BuildRunProperties(run.Formatting);
        if (rPr is not null)
            r.Add(rPr);
        if (run.Image is not null && imagesByRun.TryGetValue(run, out var part))
            r.Add(BuildDrawing(part));
        else
        {
            // A tracked deletion stores its text in w:delText (so Word renders it as deleted content);
            // all other runs use the ordinary w:t element.
            var textElement = run.Revision == RevisionKind.Deleted ? "delText" : "t";
            r.Add(new XElement(W + textElement, new XAttribute(XNamespace.Xml + "space", "preserve"), run.Text));
        }
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
        if (f.SmallCaps)
            rPr.Add(new XElement(W + "smallCaps"));
        if (f.AllCaps)
            rPr.Add(new XElement(W + "caps"));
        if (f.Underline)
            rPr.Add(new XElement(W + "u", new XAttribute(W + "val", "single")));
        if (f.ColorHex is { Length: > 0 } color)
            rPr.Add(new XElement(W + "color", new XAttribute(W + "val", color.TrimStart('#'))));
        if (f.VerticalAlign is VerticalAlign.Superscript or VerticalAlign.Subscript)
            rPr.Add(new XElement(W + "vertAlign",
                new XAttribute(W + "val", f.VerticalAlign == VerticalAlign.Superscript ? "superscript" : "subscript")));
        if (f.HighlightColorHex is { Length: > 0 } highlight)
            rPr.Add(new XElement(W + "shd",
                new XAttribute(W + "val", "clear"),
                new XAttribute(W + "color", "auto"),
                new XAttribute(W + "fill", highlight.TrimStart('#'))));
        if (f.FontSizePt is { } size)
        {
            var halfPoints = PointsToHalfPoints(size);
            rPr.Add(new XElement(W + "sz", new XAttribute(W + "val", halfPoints)));
            rPr.Add(new XElement(W + "szCs", new XAttribute(W + "val", halfPoints)));
        }

        return rPr.HasElements ? rPr : null;
    }

    private static XElement BuildSectionProperties(PageSettings page, bool hasHeader, bool hasFooter) =>
        new(W + "sectPr",
            // Header/footer references must precede pgSz/pgMar in the sectPr schema order.
            hasHeader
                ? new XElement(W + "headerReference",
                    new XAttribute(W + "type", "default"),
                    new XAttribute(R + "id", HeaderRelationshipId))
                : null,
            hasFooter
                ? new XElement(W + "footerReference",
                    new XAttribute(W + "type", "default"),
                    new XAttribute(R + "id", FooterRelationshipId))
                : null,
            new XElement(W + "pgSz",
                new XAttribute(W + "w", PointsToDxa(page.WidthPt)),
                new XAttribute(W + "h", PointsToDxa(page.HeightPt)),
                page.Landscape ? new XAttribute(W + "orient", "landscape") : null),
            new XElement(W + "pgMar",
                new XAttribute(W + "left", PointsToDxa(page.MarginLeftPt)),
                new XAttribute(W + "right", PointsToDxa(page.MarginRightPt)),
                new XAttribute(W + "top", PointsToDxa(page.MarginTopPt)),
                new XAttribute(W + "bottom", PointsToDxa(page.MarginBottomPt))),
            // Page border (w:pgBorders): a uniform box on all four edges, offset from the page edge.
            // Emitted only when set; w:sz is in eighths of a point, matching w:pBdr edges.
            BuildPageBorders(page.PageBorder),
            // Line numbering (w:lnNumType): emitted only when enabled. Schema order places it after
            // pgBorders and before cols. @w:countBy is the numbering interval; @w:restart is
            // "continuous" (across pages) or "newPage" (restart each page).
            BuildLineNumbering(page),
            // Equal-width columns: w:cols carries the count (w:num) and inter-column gap (w:space, dxa).
            // Emitted unconditionally; w:num="1" is harmless and keeps the section shape stable.
            new XElement(W + "cols",
                new XAttribute(W + "num", Math.Max(1, page.ColumnCount)),
                new XAttribute(W + "space", PointsToDxa(page.ColumnSpacingPt))));

    /// <summary>
    /// Builds the w:lnNumType element (line numbering in the page margin), or null when line numbering
    /// is off (<see cref="LineNumberMode.None"/>). @w:countBy is the interval (every Nth line numbered),
    /// @w:restart maps the mode to "continuous" (across pages) or "newPage" (restart per page).
    /// </summary>
    private static XElement? BuildLineNumbering(PageSettings page)
    {
        if (page.LineNumberMode == LineNumberMode.None)
            return null;

        var restart = page.LineNumberMode == LineNumberMode.RestartEachPage ? "newPage" : "continuous";
        return new XElement(W + "lnNumType",
            new XAttribute(W + "countBy", Math.Max(1, page.LineNumberCountBy)),
            new XAttribute(W + "restart", restart),
            new XAttribute(W + "start", 1));
    }

    /// <summary>
    /// Builds the w:pgBorders element (a uniform box on all four edges) for a page border, or null when
    /// no page border is set. w:offsetFrom="page" with w:space="24" places the border 24pt off the page
    /// edge — Word's default. Edge widths (w:sz) are in eighths of a point, like w:pBdr.
    /// </summary>
    private static XElement? BuildPageBorders(PageBorder? border)
    {
        if (border is null)
            return null;

        XElement Edge(string name) => new(W + name,
            new XAttribute(W + "val", "single"),
            new XAttribute(W + "sz", PointsToEighthPoints(border.WidthPt)),
            new XAttribute(W + "space", 24),
            new XAttribute(W + "color", border.ColorHex.TrimStart('#')));

        return new XElement(W + "pgBorders",
            new XAttribute(W + "offsetFrom", "page"),
            Edge("top"), Edge("left"), Edge("bottom"), Edge("right"));
    }

    /// <summary>
    /// Builds word/numbering.xml: three abstract numbering definitions — bullet (abstractNumId 0),
    /// decimal (abstractNumId 1) and a multilevel/legal outline (abstractNumId 2) — each with
    /// <see cref="ListLevelCount"/> levels, mapped to w:num ids <see cref="BulletNumId"/>/
    /// <see cref="NumberNumId"/>/<see cref="MultiLevelNumId"/>.
    /// </summary>
    /// <remarks>
    /// The bullet and decimal definitions reuse one fixed lvlText across every level. The multilevel
    /// definition instead gives each level its own lvlText that accumulates the ancestor counters —
    /// level 0 = <c>%1.</c>, level 1 = <c>%1.%2.</c>, level 2 = <c>%1.%2.%3.</c>, … — so Word renders
    /// the familiar outline form (1, 1.1, 1.1.1). Every multilevel level is w:numFmt="decimal" and the
    /// indent grows one step (18pt) per level.
    /// </remarks>
    private static XDocument BuildNumbering()
    {
        XElement Lvl(int level, string numFmt, string lvlText) =>
            new(W + "lvl",
                new XAttribute(W + "ilvl", level),
                new XElement(W + "start", new XAttribute(W + "val", 1)),
                new XElement(W + "numFmt", new XAttribute(W + "val", numFmt)),
                new XElement(W + "lvlText", new XAttribute(W + "val", lvlText)),
                new XElement(W + "lvlJc", new XAttribute(W + "val", "left")),
                new XElement(W + "pPr",
                    new XElement(W + "ind",
                        new XAttribute(W + "left", PointsToDxa(36 + level * 18)),
                        new XAttribute(W + "hanging", PointsToDxa(18)))));

        XElement AbstractNum(int abstractNumId, string numFmt, string lvlText) =>
            new(W + "abstractNum", new XAttribute(W + "abstractNumId", abstractNumId),
                Enumerable.Range(0, ListLevelCount).Select(level => Lvl(level, numFmt, lvlText)));

        // Legal/outline numbering: level n's text is "%1.%2.…%(n+1)." — the dotted run of all ancestor
        // counters. e.g. level 0 -> "%1.", level 2 -> "%1.%2.%3.".
        XElement MultiLevelAbstractNum(int abstractNumId) =>
            new(W + "abstractNum", new XAttribute(W + "abstractNumId", abstractNumId),
                new XAttribute(W + "multiLevelType", "multilevel"),
                Enumerable.Range(0, ListLevelCount).Select(level => Lvl(level, "decimal",
                    string.Concat(Enumerable.Range(1, level + 1).Select(n => $"%{n}.")))));

        XElement Num(int numId, int abstractNumId) =>
            new(W + "num", new XAttribute(W + "numId", numId),
                new XElement(W + "abstractNumId", new XAttribute(W + "val", abstractNumId)));

        var numbering = new XElement(W + "numbering",
            new XAttribute(XNamespace.Xmlns + "w", W.NamespaceName),
            AbstractNum(0, "bullet", "•"),
            AbstractNum(1, "decimal", "%1."),
            MultiLevelAbstractNum(2),
            Num(BulletNumId, 0),
            Num(NumberNumId, 1),
            Num(MultiLevelNumId, 2));

        return new XDocument(numbering);
    }

    /// <summary>
    /// Builds word/settings.xml (w:settings) carrying the document-protection element. The caller only
    /// emits this part when the document is protected, so <paramref name="protection"/> is assumed to be
    /// a non-None mode; the w:documentProtection records w:edit (the mode token) and w:enforcement="1".
    /// </summary>
    private static XDocument BuildSettings(ProtectionSettings protection)
    {
        var settings = new XElement(W + "settings",
            new XAttribute(XNamespace.Xmlns + "w", W.NamespaceName));
        if (ProtectionEditToken(protection.Mode) is { } edit)
            settings.Add(new XElement(W + "documentProtection",
                new XAttribute(W + "edit", edit),
                new XAttribute(W + "enforcement", "1")));
        return new XDocument(settings);
    }

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
