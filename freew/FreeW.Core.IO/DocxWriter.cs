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
    private const string CommentsRelationshipId = "rIdComments";
    private const string HeaderPartName = "word/header1.xml";
    private const string FooterPartName = "word/footer1.xml";

    // Minimal numbering scheme: one abstract num per list kind, mapped 1:1 to a w:num. Bullets use
    // abstractNumId 0 / numId 1; decimal numbering uses abstractNumId 1 / numId 2. Each abstract num
    // defines 9 levels (ilvl 0..8) so ListLevel maps directly to w:ilvl.
    internal const int BulletNumId = 1;
    internal const int NumberNumId = 2;
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

        // A comments part is emitted only when the document actually carries review comments.
        var hasComments = document.Comments.Count > 0;

        using var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true);
        WritePart(archive, "[Content_Types].xml", BuildContentTypes(images.Count > 0, hasLists, hasHeader, hasFooter, hasFootnotes, hasComments));
        WritePart(archive, "_rels/.rels", BuildPackageRels());
        WritePart(archive, "docProps/core.xml", BuildCoreProperties(document.Properties));
        WritePart(archive, "word/_rels/document.xml.rels", BuildDocumentRels(images, hyperlinks, hasLists, hasHeader, hasFooter, hasFootnotes, hasComments));
        WritePart(archive, "word/document.xml", BuildDocument(document, images, hyperlinks, hasHeader, hasFooter));
        WritePart(archive, "word/styles.xml", BuildStyles(document));
        if (hasLists)
            WritePart(archive, "word/numbering.xml", BuildNumbering());
        if (hasHeader)
            WritePart(archive, HeaderPartName, BuildHeaderFooter(W + "hdr", document.Header!));
        if (hasFooter)
            WritePart(archive, FooterPartName, BuildHeaderFooter(W + "ftr", document.Footer!));
        if (hasFootnotes)
            WritePart(archive, FootnotesPartName.TrimStart('/'), BuildFootnotes(document));
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

    private static XDocument BuildContentTypes(bool includePng, bool includeNumbering, bool hasHeader, bool hasFooter, bool hasFootnotes, bool hasComments) => new(
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
            hasComments
                ? new XElement(Ct + "Override", new XAttribute("PartName", CommentsPartName),
                    new XAttribute("ContentType", CommentsContentType))
                : null,
            new XElement(Ct + "Override", new XAttribute("PartName", CorePropertiesPartName),
                new XAttribute("ContentType", CorePropertiesContentType))));

    private static XDocument BuildPackageRels() => new(
        new XElement(Rel + "Relationships",
            new XElement(Rel + "Relationship",
                new XAttribute("Id", "rId1"),
                new XAttribute("Type", OfficeDocumentRel),
                new XAttribute("Target", "word/document.xml")),
            new XElement(Rel + "Relationship",
                new XAttribute("Id", "rIdCore"),
                new XAttribute("Type", CorePropertiesRelType),
                new XAttribute("Target", "docProps/core.xml"))));

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
        bool hasComments)
    {
        var relationships = new XElement(Rel + "Relationships",
            new XElement(Rel + "Relationship",
                new XAttribute("Id", "rId1"),
                new XAttribute("Type", StylesRel),
                new XAttribute("Target", "styles.xml")));
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
        // Reset the document-scoped bookmark id counter so ids start at 1 for each written document.
        System.Threading.Interlocked.Exchange(ref _bookmarkId, 0);

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
        var i = 0;
        var runs = paragraph.Runs;
        var openCommentId = (int?)null;
        while (i < runs.Count)
        {
            // Update the open comment range to match this run before emitting it. The textless
            // reference run does not open/extend a range; it only emits the reference marker below.
            var coveringId = runs[i].IsCommentReference ? null : runs[i].CommentId;
            if (openCommentId != coveringId)
            {
                if (openCommentId is { } closing)
                    p.Add(new XElement(W + "commentRangeEnd", new XAttribute(W + "id", closing)));
                if (coveringId is { } opening)
                    p.Add(new XElement(W + "commentRangeStart", new XAttribute(W + "id", opening)));
                openCommentId = coveringId;
            }

            var url = runs[i].HyperlinkUrl;
            var anchor = runs[i].HyperlinkAnchor;
            if (url is { Length: > 0 } && hyperlinks.TryGetValue(url, out var relationshipId))
            {
                var hyperlink = new XElement(W + "hyperlink", new XAttribute(R + "id", relationshipId));
                while (i < runs.Count && runs[i].HyperlinkUrl == url && (runs[i].IsCommentReference ? null : runs[i].CommentId) == openCommentId)
                    hyperlink.Add(BuildRun(runs[i++], imagesByRun));
                p.Add(hyperlink);
            }
            else if (anchor is { Length: > 0 })
            {
                var hyperlink = new XElement(W + "hyperlink", new XAttribute(W + "anchor", anchor));
                while (i < runs.Count && runs[i].HyperlinkAnchor == anchor && (runs[i].IsCommentReference ? null : runs[i].CommentId) == openCommentId)
                    hyperlink.Add(BuildRun(runs[i++], imagesByRun));
                p.Add(hyperlink);
            }
            else
            {
                p.Add(BuildRun(runs[i++], imagesByRun));
            }
        }

        // Close any still-open comment range at the end of the paragraph.
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
            var numId = f.ListKind == ListKind.Number ? NumberNumId : BulletNumId;
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

    private static XElement BuildRun(Run run, IReadOnlyDictionary<Run, ImagePart> imagesByRun)
    {
        // A page-number field emits a self-contained w:fldSimple wrapping a run; the wrapped run's
        // w:t carries the last-known value as fallback text for field-unaware consumers.
        if (run.FieldKind == RunFieldKind.PageNumber)
            return new XElement(W + "fldSimple",
                new XAttribute(W + "instr", " PAGE "),
                BuildTextRun(run, imagesByRun));

        // A footnote reference is a superscript run carrying a w:footnoteReference (no literal text);
        // the rPr forces vertAlign=superscript so field-unaware viewers still show a raised marker.
        if (run.FootnoteId is { } footnoteId)
            return new XElement(W + "r",
                new XElement(W + "rPr",
                    new XElement(W + "vertAlign", new XAttribute(W + "val", "superscript"))),
                new XElement(W + "footnoteReference", new XAttribute(W + "id", footnoteId)));

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
                new XAttribute(W + "bottom", PointsToDxa(page.MarginBottomPt))));

    /// <summary>
    /// Builds word/numbering.xml: two abstract numbering definitions (bullet + decimal), each with
    /// <see cref="ListLevelCount"/> levels, mapped to w:num ids <see cref="BulletNumId"/>/<see cref="NumberNumId"/>.
    /// </summary>
    private static XDocument BuildNumbering()
    {
        XElement AbstractNum(int abstractNumId, string numFmt, string lvlText) =>
            new(W + "abstractNum", new XAttribute(W + "abstractNumId", abstractNumId),
                Enumerable.Range(0, ListLevelCount).Select(level => new XElement(W + "lvl",
                    new XAttribute(W + "ilvl", level),
                    new XElement(W + "start", new XAttribute(W + "val", 1)),
                    new XElement(W + "numFmt", new XAttribute(W + "val", numFmt)),
                    new XElement(W + "lvlText", new XAttribute(W + "val", lvlText)),
                    new XElement(W + "lvlJc", new XAttribute(W + "val", "left")),
                    new XElement(W + "pPr",
                        new XElement(W + "ind",
                            new XAttribute(W + "left", PointsToDxa(36 + level * 18)),
                            new XAttribute(W + "hanging", PointsToDxa(18)))))));

        XElement Num(int numId, int abstractNumId) =>
            new(W + "num", new XAttribute(W + "numId", numId),
                new XElement(W + "abstractNumId", new XAttribute(W + "val", abstractNumId)));

        var numbering = new XElement(W + "numbering",
            new XAttribute(XNamespace.Xmlns + "w", W.NamespaceName),
            AbstractNum(0, "bullet", "•"),
            AbstractNum(1, "decimal", "%1."),
            Num(BulletNumId, 0),
            Num(NumberNumId, 1));

        return new XDocument(numbering);
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
