using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Free.Shared.Opc;
using FreeW.Core.Model;

namespace FreeW.Core.IO;

/// <summary>
/// Native OpenDocument Text reader/writer (per the file-formats plan §5.4): an OASIS ODF package — a ZIP of
/// XML parts (<c>content.xml</c>, <c>styles.xml</c>, <c>meta.xml</c>, <c>Pictures/</c>) — handled directly
/// with <see cref="System.IO.Compression"/> + hardened <see cref="SecureXmlReaderSettings"/>, mirroring the
/// <see cref="DocxReader"/>/<see cref="DocxWriter"/> stack. One adapter exposes two formats:
/// <c>.odt</c> (open+save) and the template <c>.ott</c> (open+save, <see cref="FileFormatDescriptor.OpensAsTemplate"/>).
///
/// <para>
/// Scope is the modelled text core: <c>text:p</c>/<c>text:h</c> → <see cref="Paragraph"/> (heading via
/// <c>text:outline-level</c>), <c>text:span</c> → <see cref="Run"/>, <c>text:a</c> → hyperlink,
/// <c>table:table</c> → <see cref="Table"/>, <c>draw:frame</c>+<c>draw:image</c> → <see cref="InlineImage"/>
/// (bytes from <c>Pictures/</c>), <c>text:note</c> → <see cref="Footnote"/>/<see cref="Endnote"/>,
/// <c>office:annotation</c> → <see cref="Comment"/>, <c>style:page-layout</c> → <see cref="PageSettings"/>,
/// and <c>meta.xml</c> → <see cref="DocumentProperties"/>. Styles are flattened onto runs/paragraphs on read
/// and generated with dedup on write. Unmodelled constructs (SmartArt/charts/OLE/shapes/content controls)
/// are skipped — never silently mis-mapped.
/// </para>
///
/// <para>
/// CRITICAL packaging on write: the <c>mimetype</c> entry is the FIRST zip entry and is stored UNCOMPRESSED
/// (<see cref="CompressionLevel.NoCompression"/>) with the exact content
/// <c>application/vnd.oasis.opendocument.text</c>, as the ODF spec requires for magic-number sniffing.
/// </para>
/// </summary>
public sealed class OdtFileAdapter : IDocumentFileAdapter
{
    /// <summary>The ODF text package media type, required verbatim as the (uncompressed, first) mimetype entry.</summary>
    public const string MimeType = "application/vnd.oasis.opendocument.text";

    // ODF / OpenDocument namespaces (the subset this adapter touches).
    private static readonly XNamespace Office = "urn:oasis:names:tc:opendocument:xmlns:office:1.0";
    private static readonly XNamespace Text = "urn:oasis:names:tc:opendocument:xmlns:text:1.0";
    private static readonly XNamespace Style = "urn:oasis:names:tc:opendocument:xmlns:style:1.0";
    private static readonly XNamespace Fo = "urn:oasis:names:tc:opendocument:xmlns:xsl-fo-compatible:1.0";
    private static readonly XNamespace TableNs = "urn:oasis:names:tc:opendocument:xmlns:table:1.0";
    private static readonly XNamespace Draw = "urn:oasis:names:tc:opendocument:xmlns:drawing:1.0";
    private static readonly XNamespace Svg = "urn:oasis:names:tc:opendocument:xmlns:svg-compatible:1.0";
    private static readonly XNamespace Xlink = "http://www.w3.org/1999/xlink";
    private static readonly XNamespace Meta = "urn:oasis:names:tc:opendocument:xmlns:meta:1.0";
    private static readonly XNamespace Dc = "http://purl.org/dc/elements/1.1/";
    private static readonly XNamespace Manifest = "urn:oasis:names:tc:opendocument:xmlns:manifest:1.0";

    private readonly bool _opensAsTemplate;

    public string Extension { get; }
    public string FormatName { get; }

    private OdtFileAdapter(string extension, string formatName, bool opensAsTemplate)
    {
        Extension = extension;
        FormatName = formatName;
        _opensAsTemplate = opensAsTemplate;
    }

    /// <summary>The plain <c>.odt</c> OpenDocument Text document (default).</summary>
    public OdtFileAdapter() : this(".odt", "OpenDocument Text", opensAsTemplate: false) { }

    public static OdtFileAdapter Odt() => new(".odt", "OpenDocument Text", opensAsTemplate: false);
    public static OdtFileAdapter Ott() => new(".ott", "OpenDocument Text Template", opensAsTemplate: true);

    public IReadOnlyList<FileFormatDescriptor> Formats =>
        [new FileFormatDescriptor(Extension, FormatName, CanOpen: true, CanSave: true, OpensAsTemplate: _opensAsTemplate)];

    // ----------------------------------------------------------------------------------------------------
    // Load
    // ----------------------------------------------------------------------------------------------------

    public TextDocument Load(Stream stream)
    {
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        // Reject zip-bomb / oversized packages before any decompression-heavy reads (same guard xlsx uses).
        WorkbookOpenSizeGuard.EnsureArchiveWithinLimits(archive);

        var content = ReadXmlPart(archive, "content.xml")
            ?? throw new InvalidDataException("Not an OpenDocument Text package: missing content.xml.");
        var stylesPart = ReadXmlPart(archive, "styles.xml");
        var metaPart = ReadXmlPart(archive, "meta.xml");

        var doc = new TextDocument();

        // Build the flattened style lookup from both the document content auto-styles and styles.xml.
        var styles = new OdtStyleTable();
        styles.Collect(content.Root);
        styles.Collect(stylesPart?.Root);

        var body = content.Root?.Element(Office + "body")?.Element(Office + "text");
        if (body is null)
            throw new InvalidDataException("OpenDocument Text content.xml has no <office:body><office:text>.");

        // Page geometry comes from the (first) page-layout in styles.xml referenced by the standard master page.
        ReadPageLayout(stylesPart, styles, doc.Page);

        ReadMeta(metaPart, doc.Properties);

        var pictures = new OdtPictureStore(archive);
        var ctx = new ReadContext(doc, styles, pictures);

        foreach (var block in ReadBlocks(body, ctx))
            doc.Blocks.Add(block);

        if (doc.Blocks.Count == 0)
            doc.Blocks.Add(new Paragraph());

        return doc;
    }

    private sealed class ReadContext(TextDocument document, OdtStyleTable styles, OdtPictureStore pictures)
    {
        public TextDocument Document { get; } = document;
        public OdtStyleTable Styles { get; } = styles;
        public OdtPictureStore Pictures { get; } = pictures;
    }

    private IEnumerable<Block> ReadBlocks(XElement container, ReadContext ctx)
    {
        foreach (var element in container.Elements())
        {
            if (element.Name == Text + "p" || element.Name == Text + "h")
            {
                yield return ReadParagraph(element, ctx);
            }
            else if (element.Name == Text + "list")
            {
                foreach (var p in ReadList(element, ctx, level: 0))
                    yield return p;
            }
            else if (element.Name == TableNs + "table")
            {
                yield return ReadTable(element, ctx);
            }
            // Unmodelled blocks (text:section, draw:frame at body level, …) are skipped by design.
        }
    }

    private IEnumerable<Paragraph> ReadList(XElement list, ReadContext ctx, int level)
    {
        var kind = ListKindOf(list, ctx);
        var styleName = (string?)list.Attribute(Text + "style-name");
        // meta F3 (round 162): the referenced text:list-style's own level captures the actual bullet
        // glyph (text:bullet-char) / number format (style:num-format) this list uses, instead of
        // silently normalizing every foreign list to FreeW's own default marker.
        var markerText = kind == ListKind.Bullet ? ctx.Styles.BulletCharAt(styleName, level) : null;
        var numberFormat = kind == ListKind.Number ? ctx.Styles.NumberFormatAt(styleName, level) : ListNumberFormat.Decimal;
        foreach (var item in list.Elements(Text + "list-item"))
        {
            foreach (var child in item.Elements())
            {
                if (child.Name == Text + "p" || child.Name == Text + "h")
                {
                    var p = ReadParagraph(child, ctx);
                    p.Formatting = p.Formatting with
                    {
                        ListKind = kind,
                        ListLevel = level,
                        ListMarkerText = markerText,
                        ListNumberFormat = numberFormat,
                    };
                    yield return p;
                }
                else if (child.Name == Text + "list")
                {
                    foreach (var nested in ReadList(child, ctx, level + 1))
                        yield return nested;
                }
            }
        }
    }

    private ListKind ListKindOf(XElement list, ReadContext ctx)
    {
        // The list style governs bullet vs number vs multi-level; default to bullet when unknown.
        var styleName = (string?)list.Attribute(Text + "style-name");
        if (ctx.Styles.IsMultiLevelList(styleName))
            return ListKind.MultiLevel;
        return ctx.Styles.IsNumberedList(styleName) ? ListKind.Number : ListKind.Bullet;
    }

    private Paragraph ReadParagraph(XElement element, ReadContext ctx)
    {
        var paragraph = new Paragraph();
        var styleName = (string?)element.Attribute(Text + "style-name");
        var resolved = ctx.Styles.Resolve(styleName);

        paragraph.Formatting = resolved.Paragraph;

        // text:h with an outline level maps onto a FreeW heading style (HeadingN, N in 1..3 supported here).
        if (element.Name == Text + "h")
        {
            var levelAttr = (string?)element.Attribute(Text + "outline-level");
            var outline = int.TryParse(levelAttr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 1;
            paragraph.StyleId = outline switch
            {
                <= 1 => "Heading1",
                2 => "Heading2",
                _ => "Heading3"
            };
        }
        else if (!string.IsNullOrEmpty(resolved.MappedStyleId))
        {
            paragraph.StyleId = resolved.MappedStyleId;
        }

        ReadInline(element, paragraph, ctx, resolved.Run);
        return paragraph;
    }

    private void ReadInline(XElement container, Paragraph paragraph, ReadContext ctx, RunFormatting inherited)
    {
        foreach (var node in container.Nodes())
        {
            switch (node)
            {
                case XText t:
                    if (t.Value.Length > 0)
                        paragraph.Runs.Add(new Run(t.Value, inherited));
                    break;

                case XElement e when e.Name == Text + "span":
                {
                    var spanFmt = ctx.Styles.Resolve((string?)e.Attribute(Text + "style-name")).Run;
                    var merged = MergeRunFormatting(inherited, spanFmt);
                    ReadInline(e, paragraph, ctx, merged);
                    break;
                }

                case XElement e when e.Name == Text + "a":
                {
                    var href = (string?)e.Attribute(Xlink + "href");
                    var before = paragraph.Runs.Count;
                    ReadInline(e, paragraph, ctx, inherited);
                    if (!string.IsNullOrEmpty(href))
                        for (var i = before; i < paragraph.Runs.Count; i++)
                            paragraph.Runs[i].HyperlinkUrl = href;
                    break;
                }

                case XElement e when e.Name == Text + "s":
                {
                    // text:s is a run of N spaces (default 1).
                    var count = (int?)e.Attribute(Text + "c") ?? 1;
                    if (count > 0)
                        paragraph.Runs.Add(new Run(new string(' ', count), inherited));
                    break;
                }

                case XElement e when e.Name == Text + "tab":
                    paragraph.Runs.Add(new Run("\t", inherited));
                    break;

                case XElement e when e.Name == Text + "line-break":
                    paragraph.Runs.Add(new Run("\n", inherited));
                    break;

                case XElement e when e.Name == Draw + "frame":
                {
                    var image = ReadImageFrame(e, ctx);
                    if (image is not null)
                        paragraph.Runs.Add(Run.FromImage(image));
                    break;
                }

                case XElement e when e.Name == Text + "note":
                {
                    var refRun = ReadNote(e, ctx);
                    if (refRun is not null)
                        paragraph.Runs.Add(refRun);
                    break;
                }

                case XElement e when e.Name == Office + "annotation":
                {
                    ReadAnnotation(e, paragraph, ctx);
                    break;
                }

                // Unmodelled inline content is skipped by design.
            }
        }
    }

    private InlineImage? ReadImageFrame(XElement frame, ReadContext ctx)
    {
        var image = frame.Element(Draw + "image");
        if (image is null)
            return null;

        byte[]? bytes = null;
        var href = (string?)image.Attribute(Xlink + "href");
        if (!string.IsNullOrEmpty(href))
            bytes = ctx.Pictures.Read(href);

        // Alternatively the image bytes may be embedded as a base64 office:binary-data child.
        if (bytes is null)
        {
            var binary = image.Element(Office + "binary-data");
            if (binary is not null && !string.IsNullOrWhiteSpace(binary.Value))
                bytes = Convert.FromBase64String(binary.Value.Trim());
        }

        if (bytes is null || bytes.Length == 0)
            return null;

        var width = ParseLength((string?)frame.Attribute(Svg + "width")) ?? 0;
        var height = ParseLength((string?)frame.Attribute(Svg + "height")) ?? 0;

        var inline = new InlineImage(bytes, width, height, InlineImage.DetectFormat(bytes));

        var desc = frame.Element(Svg + "desc")?.Value ?? frame.Element(Svg + "title")?.Value;
        if (!string.IsNullOrEmpty(desc))
            inline.AltText = desc;

        return inline;
    }

    private Run? ReadNote(XElement note, ReadContext ctx)
    {
        var noteClass = (string?)note.Attribute(Text + "note-class");
        var bodyEl = note.Element(Text + "note-body");
        var paragraphs = new List<Paragraph>();
        // Reuse ReadBlocks (not a bare text:p/text:h scan) so list-formatted footnote/endnote body
        // paragraphs — written as text:list/text:list-item by WriteNote — round-trip their
        // ListKind/ListLevel instead of being silently dropped.
        if (bodyEl is not null)
            foreach (var block in ReadBlocks(bodyEl, ctx))
                if (block is Paragraph p)
                    paragraphs.Add(p);

        if (paragraphs.Count == 0)
            paragraphs.Add(new Paragraph());

        if (string.Equals(noteClass, "endnote", StringComparison.OrdinalIgnoreCase))
        {
            var id = ctx.Document.NextEndnoteId();
            var endnote = new Endnote(id);
            endnote.Content.AddRange(paragraphs);
            ctx.Document.Endnotes[id] = endnote;
            return Run.EndnoteReference(id);
        }
        else
        {
            var id = ctx.Document.NextFootnoteId();
            var footnote = new Footnote(id);
            footnote.Content.AddRange(paragraphs);
            ctx.Document.Footnotes[id] = footnote;
            return Run.FootnoteReference(id);
        }
    }

    private void ReadAnnotation(XElement annotation, Paragraph paragraph, ReadContext ctx)
    {
        var id = ctx.Document.NextCommentId();
        var comment = new Comment(id)
        {
            Author = annotation.Element(Dc + "creator")?.Value ?? string.Empty
        };
        var date = annotation.Element(Dc + "date")?.Value;
        if (!string.IsNullOrEmpty(date))
            comment.DateXml = date;

        // Reuse ReadBlocks so list-formatted comment-body paragraphs — written as
        // text:list/text:list-item by WriteAnnotation — round-trip their ListKind/ListLevel
        // instead of being silently dropped (a bare text:p/text:h scan would skip them).
        foreach (var block in ReadBlocks(annotation, ctx))
            if (block is Paragraph p)
                comment.Content.Add(p);
        if (comment.Content.Count == 0)
            comment.Content.Add(new Paragraph());

        ctx.Document.Comments[id] = comment;
        // Anchor the comment on a zero-width reference run so it survives the round-trip.
        paragraph.Runs.Add(Run.CommentReference(id));
    }

    private Table ReadTable(XElement table, ReadContext ctx)
    {
        var result = new Table();

        // Expand table:table-column/@table:number-columns-repeated into per-column widths where available.
        foreach (var col in table.Elements(TableNs + "table-column"))
        {
            var repeat = (int?)col.Attribute(TableNs + "number-columns-repeated") ?? 1;
            var colStyle = ctx.Styles.ColumnWidthPt((string?)col.Attribute(TableNs + "style-name"));
            for (var i = 0; i < repeat; i++)
                if (colStyle is { } w)
                    result.ColumnWidthsPt.Add(w);
        }

        // Vertical merges (table:number-rows-spanned on the top cell) leave a table:covered-table-cell
        // placeholder in each row below, one per grid column the merge occupies. Track them keyed by the
        // column they started at so those placeholders can be told apart from a covered-table-cell that
        // merely covers a cell spanned horizontally earlier in the SAME row (which carries no state here
        // and is dropped, same as before — GridSpan on the real cell already accounts for it).
        var activeVerticalSpans = new Dictionary<int, (int Width, int RemainingRows)>();

        // Only direct table-row children: table.Descendants would also walk into the rows of any table
        // NESTED inside a cell, splicing the inner table's rows into this outer table as bogus rows.
        foreach (var rowEl in table.Elements(TableNs + "table-row"))
        {
            var row = new TableRow();
            var col = 0;
            var pendingGroupCols = 0;
            foreach (var cellEl in rowEl.Elements())
            {
                if (cellEl.Name == TableNs + "table-cell")
                {
                    var cell = new TableCell();
                    var span = (int?)cellEl.Attribute(TableNs + "number-columns-spanned") ?? 1;
                    if (span > 1)
                        cell.GridSpan = span;

                    foreach (var block in ReadBlocks(cellEl, ctx))
                    {
                        if (block is Paragraph p)
                            cell.Paragraphs.Add(p);
                        else if (block is Table nested)
                            cell.NestedTables.Add(nested);
                    }
                    if (cell.Paragraphs.Count == 0)
                        cell.Paragraphs.Add(new Paragraph());
                    row.Cells.Add(cell);

                    var rowSpan = (int?)cellEl.Attribute(TableNs + "number-rows-spanned") ?? 1;
                    if (rowSpan > 1)
                    {
                        cell.VerticalMerge = VerticalMergeState.Restart;
                        activeVerticalSpans[col] = (span, rowSpan - 1);
                    }

                    col += span;
                }
                else if (cellEl.Name == TableNs + "covered-table-cell")
                {
                    if (pendingGroupCols > 0)
                    {
                        // Additional column of a vertical-merge group already materialised below.
                        pendingGroupCols--;
                    }
                    else if (activeVerticalSpans.TryGetValue(col, out var vspan))
                    {
                        // The first covered column of a vertical merge continuing from a row above:
                        // materialise one TableCell (spanning the same width as the restart cell) so this
                        // row keeps a slot per merge region and later real cells don't shift left,
                        // mirroring how DocxReader models w:vMerge continue cells.
                        var continued = new TableCell();
                        continued.Paragraphs.Add(new Paragraph());
                        if (vspan.Width > 1)
                            continued.GridSpan = vspan.Width;
                        continued.VerticalMerge = VerticalMergeState.Continue;
                        row.Cells.Add(continued);

                        pendingGroupCols = vspan.Width - 1;
                        var remaining = vspan.RemainingRows - 1;
                        if (remaining > 0)
                            activeVerticalSpans[col] = (vspan.Width, remaining);
                        else
                            activeVerticalSpans.Remove(col);
                    }
                    // Otherwise this covers a horizontally spanned cell earlier in this same row; GridSpan
                    // on that cell already accounts for it, so no separate TableCell slot is needed here.

                    col += 1;
                }
            }
            result.Rows.Add(row);
        }

        return result;
    }

    private void ReadPageLayout(XDocument? styles, OdtStyleTable styleTable, PageSettings page)
    {
        var layout = styles?.Root?
            .Element(Office + "automatic-styles")?
            .Elements(Style + "page-layout")
            .FirstOrDefault();
        var props = layout?.Element(Style + "page-layout-properties");
        if (props is null)
            return;

        var width = ParseLength((string?)props.Attribute(Fo + "page-width"));
        var height = ParseLength((string?)props.Attribute(Fo + "page-height"));
        if (width is { } w) page.WidthPt = w;
        if (height is { } h) page.HeightPt = h;

        if (ParseLength((string?)props.Attribute(Fo + "margin-left")) is { } ml) page.MarginLeftPt = ml;
        if (ParseLength((string?)props.Attribute(Fo + "margin-right")) is { } mr) page.MarginRightPt = mr;
        if (ParseLength((string?)props.Attribute(Fo + "margin-top")) is { } mt) page.MarginTopPt = mt;
        if (ParseLength((string?)props.Attribute(Fo + "margin-bottom")) is { } mb) page.MarginBottomPt = mb;

        var orientation = (string?)props.Attribute(Style + "print-orientation");
        page.Landscape = string.Equals(orientation, "landscape", StringComparison.OrdinalIgnoreCase);
    }

    private static void ReadMeta(XDocument? meta, DocumentProperties props)
    {
        var metaEl = meta?.Root?.Element(Office + "meta");
        if (metaEl is null)
            return;

        props.Title = metaEl.Element(Dc + "title")?.Value ?? props.Title;
        props.Subject = metaEl.Element(Dc + "subject")?.Value ?? props.Subject;
        props.Author = metaEl.Element(Meta + "initial-creator")?.Value
            ?? metaEl.Element(Dc + "creator")?.Value ?? props.Author;
        props.LastModifiedBy = metaEl.Element(Dc + "creator")?.Value ?? props.LastModifiedBy;
        props.Comments = metaEl.Element(Dc + "description")?.Value ?? props.Comments;

        var keywords = metaEl.Elements(Meta + "keyword").Select(k => k.Value).Where(s => s.Length > 0).ToList();
        if (keywords.Count > 0)
            props.Keywords = string.Join(", ", keywords);

        if (DateTimeOffset.TryParse(metaEl.Element(Meta + "creation-date")?.Value,
                CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var created))
            props.Created = created;
        if (DateTimeOffset.TryParse(metaEl.Element(Dc + "date")?.Value,
                CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var modified))
            props.Modified = modified;
    }

    private static XDocument? ReadXmlPart(ZipArchive archive, string entryName)
    {
        var entry = archive.GetEntry(entryName);
        if (entry is null)
            return null;
        using var es = entry.Open();
        using var reader = XmlReader.Create(es, SecureXmlReaderSettings.Create());
        return XDocument.Load(reader);
    }

    internal static RunFormatting MergeRunFormatting(RunFormatting baseFmt, RunFormatting overlay) => baseFmt with
    {
        Bold = overlay.Bold || baseFmt.Bold,
        Italic = overlay.Italic || baseFmt.Italic,
        Underline = overlay.Underline || baseFmt.Underline,
        Strikethrough = overlay.Strikethrough || baseFmt.Strikethrough,
        DoubleStrikethrough = overlay.DoubleStrikethrough || baseFmt.DoubleStrikethrough,
        NoProof = overlay.NoProof || baseFmt.NoProof,
        Hidden = overlay.Hidden || baseFmt.Hidden,
        WebHidden = overlay.WebHidden || baseFmt.WebHidden,
        FontFamily = overlay.FontFamily ?? baseFmt.FontFamily,
        FontSizePt = overlay.FontSizePt ?? baseFmt.FontSizePt,
        ColorHex = overlay.ColorHex ?? baseFmt.ColorHex,
        HighlightColorHex = overlay.HighlightColorHex ?? baseFmt.HighlightColorHex,
        VerticalAlign = overlay.VerticalAlign != VerticalAlign.Baseline ? overlay.VerticalAlign : baseFmt.VerticalAlign
    };

    // ----------------------------------------------------------------------------------------------------
    // Save
    // ----------------------------------------------------------------------------------------------------

    public void Save(TextDocument document, Stream stream)
    {
        var styleWriter = new OdtStyleWriter();
        var pictureWriter = new OdtPictureWriter();

        // Build content.xml body first so the auto-style table is fully populated before serialising styles.
        var bodyText = new XElement(Office + "text");
        WriteBlocksWithLists(document.Blocks, bodyText, document, styleWriter, pictureWriter);
        if (!bodyText.HasElements)
            bodyText.Add(new XElement(Text + "p"));

        var contentDoc = BuildContentXml(bodyText, styleWriter);
        var stylesDoc = BuildStylesXml(document, styleWriter);
        var metaDoc = BuildMetaXml(document);

        using var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true);

        // CRITICAL: mimetype must be the FIRST entry and stored UNCOMPRESSED.
        WriteMimeType(archive);

        WriteXmlEntry(archive, "content.xml", contentDoc);
        WriteXmlEntry(archive, "styles.xml", stylesDoc);
        WriteXmlEntry(archive, "meta.xml", metaDoc);

        foreach (var (name, bytes) in pictureWriter.Pictures)
        {
            var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
            using var es = entry.Open();
            es.Write(bytes, 0, bytes.Length);
        }

        WriteXmlEntry(archive, "META-INF/manifest.xml", BuildManifest(pictureWriter));
    }

    private static void WriteMimeType(ZipArchive archive)
    {
        var entry = archive.CreateEntry("mimetype", CompressionLevel.NoCompression);
        using var es = entry.Open();
        var bytes = Encoding.ASCII.GetBytes(MimeType);
        es.Write(bytes, 0, bytes.Length);
    }

    /// <summary>
    /// Writes a mixed sequence of body blocks (paragraphs + tables), batching consecutive
    /// list-formatted paragraphs (<see cref="ParagraphFormatting.ListKind"/> != None) into
    /// proper <c>text:list</c>/<c>text:list-item</c> structures so list formatting round-trips
    /// through <see cref="OdtFileAdapter.ReadList"/> instead of being flattened to plain paragraphs.
    /// </summary>
    private void WriteBlocksWithLists(
        IReadOnlyList<Block> blocks, XElement parent, TextDocument document, OdtStyleWriter styles, OdtPictureWriter pictures)
    {
        var run = new List<Paragraph>();
        foreach (var block in blocks)
        {
            if (block is Paragraph p)
            {
                run.Add(p);
                continue;
            }

            if (run.Count > 0)
            {
                WriteParagraphRun(run, parent, document, styles, pictures);
                run.Clear();
            }

            if (block is Table t)
                WriteTable(t, parent, document, styles, pictures);
        }

        if (run.Count > 0)
            WriteParagraphRun(run, parent, document, styles, pictures);
    }

    /// <summary>One open <c>text:list</c> level while folding a flat (kind, level) paragraph run into nested lists.</summary>
    private sealed class ListFrame
    {
        public required int Level;
        public required ListKind Kind;
        public required XElement ListEl;
        public XElement? LastItem;
    }

    /// <summary>
    /// Writes a contiguous run of paragraphs (table-cell content or a body run between non-paragraph
    /// blocks), grouping consecutive list-formatted paragraphs into nested <c>text:list</c> structures
    /// keyed by <see cref="ParagraphFormatting.ListKind"/>/<see cref="ParagraphFormatting.ListLevel"/>,
    /// mirroring the nesting <see cref="ReadList"/> understands on read-back.
    /// </summary>
    private void WriteParagraphRun(
        IReadOnlyList<Paragraph> paragraphs, XElement parent, TextDocument document, OdtStyleWriter styles, OdtPictureWriter pictures)
    {
        var frames = new List<ListFrame>();

        foreach (var p in paragraphs)
        {
            var kind = p.Formatting.ListKind;
            if (kind == ListKind.None)
            {
                frames.Clear();
                WriteParagraph(p, parent, document, styles, pictures);
                continue;
            }

            var level = Math.Clamp(p.Formatting.ListLevel, 0, MaxOdtListLevel);

            // Close any open levels deeper than the one this paragraph belongs to.
            while (frames.Count > 0 && frames[^1].Level > level)
                frames.RemoveAt(frames.Count - 1);

            if (frames.Count == 0 || frames[^1].Level < level)
            {
                // Open new nested list(s) down to `level`, each hosted inside the previous level's last item.
                var startLevel = frames.Count == 0 ? 0 : frames[^1].Level + 1;
                for (var lv = startLevel; lv <= level; lv++)
                    frames.Add(OpenListFrame(frames.Count == 0 ? null : frames[^1], lv, kind, parent, styles));
            }
            else if (frames[^1].Kind != kind)
            {
                // Same level, but the bullet/number kind changed: start a sibling list at this level.
                var closed = frames[^1];
                frames.RemoveAt(frames.Count - 1);
                frames.Add(OpenListFrame(frames.Count == 0 ? null : frames[^1], closed.Level, kind, parent, styles));
            }

            var top = frames[^1];
            var itemEl = new XElement(Text + "list-item");
            WriteParagraph(p, itemEl, document, styles, pictures);
            top.ListEl.Add(itemEl);
            top.LastItem = itemEl;
        }
    }

    // Mirrors the Avalonia editor's MaxListDepth-1 clamp (levels 0..8, 9 nesting levels).
    private const int MaxOdtListLevel = 8;

    private static ListFrame OpenListFrame(ListFrame? outer, int level, ListKind kind, XElement bodyParent, OdtStyleWriter styles)
    {
        // Root-level lists attach directly to the surrounding body/cell; nested lists must live inside
        // the enclosing level's current list-item (synthesizing a pass-through one if none exists yet,
        // e.g. when a paragraph jumps straight from level 0 to level 2 in one step).
        var container = outer is null ? bodyParent : EnsureLastItem(outer);
        var listEl = new XElement(Text + "list", new XAttribute(Text + "style-name", styles.ListStyleName(kind)));
        container.Add(listEl);
        return new ListFrame { Level = level, Kind = kind, ListEl = listEl, LastItem = null };
    }

    /// <summary>
    /// Returns the frame's current last <c>text:list-item</c> to host a deeper nested list, synthesizing
    /// an empty one if none exists yet. The synthesized item deliberately carries NO <c>text:p</c>/<c>text:h</c>
    /// child — ODF's <c>text:list-item</c> content model is zero-or-more of (h|p|list|soft-page-break), so an
    /// item containing only a nested <c>text:list</c> is valid and renders with no bullet/number/paragraph of
    /// its own (the standard ODF "pass-through container" idiom for skipped intermediate levels). Giving it a
    /// bare <c>text:p</c> instead would materialise a real, visible, empty bullet for every intermediate level
    /// on the way down to a deeper-level paragraph — e.g. two phantom bullets before a document's first list
    /// paragraph if that paragraph starts at level 2 (0-based) — and <see cref="ReadList"/> would read it back
    /// as a genuine empty paragraph, corrupting the round-trip.
    /// </summary>
    private static XElement EnsureLastItem(ListFrame frame)
    {
        if (frame.LastItem is not null)
            return frame.LastItem;

        var itemEl = new XElement(Text + "list-item");
        frame.ListEl.Add(itemEl);
        frame.LastItem = itemEl;
        return itemEl;
    }

    private void WriteParagraph(
        Paragraph p, XElement parent, TextDocument document, OdtStyleWriter styles, OdtPictureWriter pictures)
    {
        var headingLevel = HeadingLevel(p.StyleId);
        var paragraphFmt = ResolveParagraphFormatting(p, document);
        var styleName = styles.ParagraphStyle(paragraphFmt);

        var el = new XElement(headingLevel > 0 ? Text + "h" : Text + "p",
            new XAttribute(Text + "style-name", styleName));
        if (headingLevel > 0)
            el.Add(new XAttribute(Text + "outline-level", headingLevel));

        WriteRuns(p, el, document, styles, pictures);
        parent.Add(el);
    }

    private void WriteRuns(
        Paragraph p, XElement parent, TextDocument document, OdtStyleWriter styles, OdtPictureWriter pictures)
    {
        foreach (var run in p.Runs)
        {
            if (run.Image is { } image)
            {
                parent.Add(WriteImageFrame(image, pictures));
                continue;
            }

            if (run.FootnoteId is { } fid && document.Footnotes.TryGetValue(fid, out var footnote))
            {
                parent.Add(WriteNote(footnote.Content, "footnote", "ftn" + fid, document, styles, pictures));
                continue;
            }

            if (run.EndnoteId is { } eid && document.Endnotes.TryGetValue(eid, out var endnote))
            {
                parent.Add(WriteNote(endnote.Content, "endnote", "edn" + eid, document, styles, pictures));
                continue;
            }

            if (run.IsCommentReference && run.CommentId is { } cid
                && document.Comments.TryGetValue(cid, out var comment))
            {
                parent.Add(WriteAnnotation(comment, document, styles, pictures));
                continue;
            }

            // Reference-only / textless marks (e.g. page breaks) carry no ODT text representation here.
            if (run.Text.Length == 0)
                continue;

            var spanStyle = styles.RunStyle(run.Formatting);

            // Decide the wrapping BEFORE appending any text:s/text:tab/text:line-break structural nodes:
            // AppendText can emit several sibling nodes (not just a single XText) for runs containing tabs,
            // newlines, or 2+ spaces, so building into `parent` and later relocating only XText nodes would
            // strand those structural nodes outside <text:a>, duplicating content on the next read.
            if (!string.IsNullOrEmpty(run.HyperlinkUrl))
            {
                var anchor = new XElement(Text + "a", new XAttribute(Xlink + "href", run.HyperlinkUrl));
                if (spanStyle is null)
                {
                    AppendText(anchor, run.Text);
                }
                else
                {
                    var span = new XElement(Text + "span", new XAttribute(Text + "style-name", spanStyle));
                    AppendText(span, run.Text);
                    anchor.Add(span);
                }
                parent.Add(anchor);
            }
            else if (spanStyle is null)
            {
                AppendText(parent, run.Text);
            }
            else
            {
                var span = new XElement(Text + "span", new XAttribute(Text + "style-name", spanStyle));
                AppendText(span, run.Text);
                parent.Add(span);
            }
        }
    }

    private static void AppendText(XElement parent, string text)
    {
        // Preserve leading/trailing/multiple spaces and tabs/newlines using ODF's text:s/text:tab/text:line-break.
        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            if (c == '\t')
            {
                parent.Add(new XElement(Text + "tab"));
                i++;
            }
            else if (c == '\n' || c == '\r')
            {
                parent.Add(new XElement(Text + "line-break"));
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                i++;
            }
            else if (c == ' ')
            {
                var run = 0;
                while (i < text.Length && text[i] == ' ') { run++; i++; }
                if (run == 1)
                {
                    parent.Add(new XText(" "));
                }
                else
                {
                    // First space as literal, the remainder as text:s c="run-1".
                    parent.Add(new XText(" "));
                    parent.Add(new XElement(Text + "s", new XAttribute(Text + "c", run - 1)));
                }
            }
            else
            {
                var start = i;
                while (i < text.Length && text[i] != '\t' && text[i] != '\n' && text[i] != '\r' && text[i] != ' ')
                    i++;
                parent.Add(new XText(text[start..i]));
            }
        }
    }

    private XElement WriteImageFrame(InlineImage image, OdtPictureWriter pictures)
    {
        var href = pictures.Add(image);
        var frame = new XElement(Draw + "frame",
            new XAttribute(Draw + "name", href),
            new XAttribute(Text + "anchor-type", "as-char"),
            new XAttribute(Svg + "width", FormatLength(image.WidthPt)),
            new XAttribute(Svg + "height", FormatLength(image.HeightPt)),
            new XElement(Draw + "image",
                new XAttribute(Xlink + "href", href),
                new XAttribute(Xlink + "type", "simple"),
                new XAttribute(Xlink + "show", "embed"),
                new XAttribute(Xlink + "actuate", "onLoad")));
        if (!string.IsNullOrEmpty(image.AltText))
            frame.Add(new XElement(Svg + "desc", image.AltText));
        return frame;
    }

    private XElement WriteNote(
        IReadOnlyList<Paragraph> content, string noteClass, string id,
        TextDocument document, OdtStyleWriter styles, OdtPictureWriter pictures)
    {
        var bodyEl = new XElement(Text + "note-body");
        WriteParagraphRun(content, bodyEl, document, styles, pictures);
        if (!bodyEl.HasElements)
            bodyEl.Add(new XElement(Text + "p"));

        return new XElement(Text + "note",
            new XAttribute(Text + "note-class", noteClass),
            new XAttribute(Text + "id", id),
            new XElement(Text + "note-citation"),
            bodyEl);
    }

    private XElement WriteAnnotation(
        Comment comment, TextDocument document, OdtStyleWriter styles, OdtPictureWriter pictures)
    {
        var el = new XElement(Office + "annotation");
        if (!string.IsNullOrEmpty(comment.Author))
            el.Add(new XElement(Dc + "creator", comment.Author));
        if (!string.IsNullOrEmpty(comment.DateXml))
            el.Add(new XElement(Dc + "date", comment.DateXml));
        WriteParagraphRun(comment.Content, el, document, styles, pictures);
        if (comment.Content.Count == 0)
            el.Add(new XElement(Text + "p"));
        return el;
    }

    private void WriteTable(
        Table table, XElement parent, TextDocument document, OdtStyleWriter styles, OdtPictureWriter pictures)
    {
        var tableName = styles.TableName();
        var el = new XElement(TableNs + "table", new XAttribute(TableNs + "name", tableName));

        var columns = table.ColumnCount;
        for (var c = 0; c < columns; c++)
            el.Add(new XElement(TableNs + "table-column"));

        for (var rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
        {
            var row = table.Rows[rowIndex];
            var rowEl = new XElement(TableNs + "table-row");
            for (var cellIndex = 0; cellIndex < row.Cells.Count; cellIndex++)
            {
                var cell = row.Cells[cellIndex];

                if (cell.VerticalMerge == VerticalMergeState.Continue)
                {
                    // Covered by a vertically merged cell above; that cell's own number-rows-spanned
                    // already accounts for this row, so just emit one covered-table-cell placeholder per
                    // grid column it occupies so the row keeps the same column count as its siblings.
                    for (var s = 0; s < Math.Max(1, cell.GridSpan); s++)
                        rowEl.Add(new XElement(TableNs + "covered-table-cell"));
                    continue;
                }

                var cellEl = new XElement(TableNs + "table-cell",
                    new XAttribute(Office + "value-type", "string"));
                if (cell.GridSpan > 1)
                    cellEl.Add(new XAttribute(TableNs + "number-columns-spanned", cell.GridSpan));

                if (cell.VerticalMerge == VerticalMergeState.Restart)
                {
                    var rowSpan = CountVerticalMergeRows(table, rowIndex, cellIndex);
                    if (rowSpan > 1)
                        cellEl.Add(new XAttribute(TableNs + "number-rows-spanned", rowSpan));
                }

                // Nested tables are written before the cell's own paragraphs, mirroring DocxWriter: a
                // table cell must always end with a paragraph (cell.Paragraphs guarantees at least one
                // entry below, even when empty), so this ordering keeps the output schema-valid.
                foreach (var nested in cell.NestedTables)
                    WriteTable(nested, cellEl, document, styles, pictures);

                WriteParagraphRun(cell.Paragraphs, cellEl, document, styles, pictures);
                if (cell.Paragraphs.Count == 0)
                    cellEl.Add(new XElement(Text + "p"));
                rowEl.Add(cellEl);

                // Emit covered cells for a horizontal span so the column count balances.
                for (var s = 1; s < cell.GridSpan; s++)
                    rowEl.Add(new XElement(TableNs + "covered-table-cell"));
            }
            el.Add(rowEl);
        }

        parent.Add(el);
    }

    /// <summary>
    /// How many consecutive rows (including <paramref name="rowIndex"/> itself) the vertically-merged
    /// cell at <paramref name="cellIndex"/> spans, found by walking down the grid column it starts at
    /// (via <see cref="TableGridProjection"/>, so horizontal merges elsewhere in the table don't throw
    /// off the column alignment) while the same column keeps producing a <see cref="VerticalMergeState.Continue"/>
    /// cell.
    /// </summary>
    private static int CountVerticalMergeRows(Table table, int rowIndex, int cellIndex)
    {
        var startColumn = TableGridProjection.StartColumn(table.Rows[rowIndex], cellIndex);
        if (startColumn < 0)
            return 1;

        var count = 1;
        for (var r = rowIndex + 1; r < table.Rows.Count; r++)
        {
            var projected = TableGridProjection.StartingAt(table.Rows[r], startColumn);
            if (projected is not { Cell.VerticalMerge: VerticalMergeState.Continue })
                break;
            count++;
        }
        return count;
    }

    private XDocument BuildContentXml(XElement bodyText, OdtStyleWriter styles)
    {
        var root = new XElement(Office + "document-content",
            new XAttribute(XNamespace.Xmlns + "office", Office.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "text", Text.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "style", Style.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "fo", Fo.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "table", TableNs.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "draw", Draw.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "svg", Svg.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "xlink", Xlink.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "dc", Dc.NamespaceName),
            new XAttribute(Office + "version", "1.3"),
            styles.BuildContentAutoStyles(),
            new XElement(Office + "body", bodyText));
        return new XDocument(new XDeclaration("1.0", "UTF-8", null), root);
    }

    private XDocument BuildStylesXml(TextDocument document, OdtStyleWriter styles)
    {
        var pageLayout = BuildPageLayout(document.Page);
        var masterStyles = new XElement(Office + "master-styles",
            new XElement(Style + "master-page",
                new XAttribute(Style + "name", "Standard"),
                new XAttribute(Style + "page-layout-name", "PL1")));

        var root = new XElement(Office + "document-styles",
            new XAttribute(XNamespace.Xmlns + "office", Office.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "text", Text.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "style", Style.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "fo", Fo.NamespaceName),
            new XAttribute(Office + "version", "1.3"),
            styles.BuildNamedStyles(document),
            new XElement(Office + "automatic-styles", pageLayout),
            masterStyles);
        return new XDocument(new XDeclaration("1.0", "UTF-8", null), root);
    }

    private XElement BuildPageLayout(PageSettings page)
    {
        var props = new XElement(Style + "page-layout-properties",
            new XAttribute(Fo + "page-width", FormatLength(page.WidthPt)),
            new XAttribute(Fo + "page-height", FormatLength(page.HeightPt)),
            new XAttribute(Fo + "margin-left", FormatLength(page.MarginLeftPt)),
            new XAttribute(Fo + "margin-right", FormatLength(page.MarginRightPt)),
            new XAttribute(Fo + "margin-top", FormatLength(page.MarginTopPt)),
            new XAttribute(Fo + "margin-bottom", FormatLength(page.MarginBottomPt)),
            new XAttribute(Style + "print-orientation", page.Landscape ? "landscape" : "portrait"));
        return new XElement(Style + "page-layout",
            new XAttribute(Style + "name", "PL1"),
            props);
    }

    private XDocument BuildMetaXml(TextDocument document)
    {
        var props = document.Properties;
        var meta = new XElement(Office + "meta");
        if (!string.IsNullOrEmpty(props.Title)) meta.Add(new XElement(Dc + "title", props.Title));
        if (!string.IsNullOrEmpty(props.Subject)) meta.Add(new XElement(Dc + "subject", props.Subject));
        if (!string.IsNullOrEmpty(props.Author)) meta.Add(new XElement(Meta + "initial-creator", props.Author));
        if (!string.IsNullOrEmpty(props.LastModifiedBy)) meta.Add(new XElement(Dc + "creator", props.LastModifiedBy));
        if (!string.IsNullOrEmpty(props.Comments)) meta.Add(new XElement(Dc + "description", props.Comments));
        if (!string.IsNullOrEmpty(props.Keywords))
            foreach (var kw in props.Keywords.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                meta.Add(new XElement(Meta + "keyword", kw));
        if (props.Created is { } created)
            meta.Add(new XElement(Meta + "creation-date", created.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture)));
        if (props.Modified is { } modified)
            meta.Add(new XElement(Dc + "date", modified.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture)));

        var root = new XElement(Office + "document-meta",
            new XAttribute(XNamespace.Xmlns + "office", Office.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "meta", Meta.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "dc", Dc.NamespaceName),
            new XAttribute(Office + "version", "1.3"),
            meta);
        return new XDocument(new XDeclaration("1.0", "UTF-8", null), root);
    }

    private XDocument BuildManifest(OdtPictureWriter pictures)
    {
        var root = new XElement(Manifest + "manifest",
            new XAttribute(XNamespace.Xmlns + "manifest", Manifest.NamespaceName),
            new XAttribute(Manifest + "version", "1.3"),
            ManifestEntry("/", MimeType),
            ManifestEntry("content.xml", "text/xml"),
            ManifestEntry("styles.xml", "text/xml"),
            ManifestEntry("meta.xml", "text/xml"));
        foreach (var (name, mediaType) in pictures.MediaTypes)
            root.Add(ManifestEntry(name, mediaType));
        return new XDocument(new XDeclaration("1.0", "UTF-8", null), root);
    }

    private XElement ManifestEntry(string path, string mediaType) =>
        new(Manifest + "file-entry",
            new XAttribute(Manifest + "full-path", path),
            new XAttribute(Manifest + "media-type", mediaType));

    private static void WriteXmlEntry(ZipArchive archive, string name, XDocument doc)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        using var es = entry.Open();
        using var xw = XmlWriter.Create(es, new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(false),
            CloseOutput = false,
            Indent = false
        });
        doc.Save(xw);
    }

    private static ParagraphFormatting ResolveParagraphFormatting(Paragraph p, TextDocument document)
    {
        // Direct formatting on the paragraph wins; otherwise inherit the referenced style's paragraph block.
        if (p.StyleId is { } styleId && document.Styles.TryGetValue(styleId, out var style))
        {
            // Merge: take direct alignment/indents but fall back to the style for unset values is non-trivial;
            // for the modelled subset we emit the paragraph's own formatting (already carries style cascade).
            _ = style;
        }
        return p.Formatting;
    }

    private static int HeadingLevel(string? styleId) => styleId switch
    {
        "Heading1" or "Title" => 1,
        "Heading2" or "Subtitle" => 2,
        "Heading3" => 3,
        _ => 0
    };

    // ----------------------------------------------------------------------------------------------------
    // Unit / colour helpers
    // ----------------------------------------------------------------------------------------------------

    /// <summary>
    /// Parses an ODF length (e.g. <c>"2.54cm"</c>, <c>"1in"</c>, <c>"12pt"</c>, <c>"10mm"</c>, <c>"1pc"</c>)
    /// into points. Returns null for an empty/unparseable value so the caller can keep its default.
    /// </summary>
    internal static double? ParseLength(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        value = value.Trim();
        var unitStart = value.Length;
        while (unitStart > 0 && (char.IsLetter(value[unitStart - 1]) || value[unitStart - 1] == '%'))
            unitStart--;

        var numberPart = value[..unitStart];
        var unit = value[unitStart..].ToLowerInvariant();

        if (!double.TryParse(numberPart, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
            return null;

        return unit switch
        {
            "pt" => number,
            "in" => number * 72.0,
            "cm" => number * 72.0 / 2.54,
            "mm" => number * 72.0 / 25.4,
            "pc" => number * 12.0,
            "px" => number * 72.0 / 96.0,
            "" => number,
            _ => null
        };
    }

    /// <summary>Formats a length in points as an ODF centimetre length (ODF's preferred unit), e.g. <c>"2.54cm"</c>.</summary>
    internal static string FormatLength(double points)
    {
        var cm = points * 2.54 / 72.0;
        return cm.ToString("0.###", CultureInfo.InvariantCulture) + "cm";
    }

    /// <summary>Normalises an ODF colour (<c>#rrggbb</c>) to FreeW's <c>#RRGGBB</c>, or null when absent/invalid.</summary>
    internal static string? ParseColor(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        value = value.Trim();
        if (value.StartsWith('#') && value.Length == 7)
            return value.ToUpperInvariant();
        return null;
    }

    /// <summary>Formats a FreeW colour hex (<c>#RRGGBB</c>) as an ODF colour (<c>#rrggbb</c>).</summary>
    internal static string FormatColor(string colorHex)
    {
        var v = colorHex.StartsWith('#') ? colorHex : "#" + colorHex;
        return v.ToLowerInvariant();
    }

    // ====================================================================================================
    // Helpers — read side (OdtStyleTable / OdtPictureStore) and write side (OdtStyleWriter / OdtPictureWriter).
    // Nested so they reuse the adapter's ODF namespace constants and unit/colour helpers by simple name.
    // ====================================================================================================

    /// <summary>The run + paragraph formatting (and any mapped FreeW style id) flattened from an ODF style.</summary>
    private sealed record ResolvedStyle(ParagraphFormatting Paragraph, RunFormatting Run, string? MappedStyleId);

    /// <summary>
    /// Flattened ODF style lookup built on read from content.xml + styles.xml: resolves a style name (walking
    /// its parent chain) to FreeW run/paragraph formatting, tells bullet vs numbered lists apart, and exposes
    /// table-column widths.
    /// </summary>
    private sealed class OdtStyleTable
    {
        private sealed record Entry(string? Parent, RunFormatting Run, ParagraphFormatting Paragraph, string? DisplayName);

        private static readonly ResolvedStyle DefaultResolved =
            new(ParagraphFormatting.Default, RunFormatting.Default, null);

        private readonly Dictionary<string, Entry> _styles = new(StringComparer.Ordinal);
        private readonly Dictionary<string, bool> _listNumbered = new(StringComparer.Ordinal);
        private readonly Dictionary<string, bool> _listMultiLevel = new(StringComparer.Ordinal);
        private readonly Dictionary<string, double> _columnWidths = new(StringComparer.Ordinal);
        private readonly Dictionary<(string StyleName, int Level), string?> _bulletChars = new();
        private readonly Dictionary<(string StyleName, int Level), ListNumberFormat> _numberFormats = new();

        public void Collect(XElement? root)
        {
            if (root is null)
                return;

            var containers = root.Descendants(Office + "automatic-styles")
                .Concat(root.Descendants(Office + "styles"));
            foreach (var container in containers)
            {
                foreach (var s in container.Elements(Style + "style"))
                {
                    var name = (string?)s.Attribute(Style + "name");
                    if (string.IsNullOrEmpty(name))
                        continue;

                    _styles[name] = new Entry(
                        (string?)s.Attribute(Style + "parent-style-name"),
                        ParseRun(s.Element(Style + "text-properties")),
                        ParseParagraph(s.Element(Style + "paragraph-properties")),
                        (string?)s.Attribute(Style + "display-name"));

                    if ((string?)s.Attribute(Style + "family") == "table-column"
                        && ParseLength((string?)s.Element(Style + "table-column-properties")?.Attribute(Style + "column-width")) is { } w)
                        _columnWidths[name] = w;
                }

                foreach (var ls in container.Elements(Text + "list-style"))
                {
                    var name = (string?)ls.Attribute(Style + "name");
                    if (!string.IsNullOrEmpty(name))
                    {
                        _listNumbered[name] = ls.Element(Text + "list-level-style-number") is not null;

                        // MultiLevel (outline/legal) numbering accumulates ancestor counters (1, 1.1, 1.1.1),
                        // which ODF expresses via text:display-levels > 1 on a list-level-style-number.
                        _listMultiLevel[name] = ls.Elements(Text + "list-level-style-number")
                            .Any(lvl => ((int?)lvl.Attribute(Text + "display-levels") ?? 1) > 1);

                        // meta F3 (round 162): capture each level's actual bullet-char / num-format so a
                        // foreign list's real marker survives instead of being silently normalized to
                        // FreeW's own default ('•' bullet / decimal numbering).
                        foreach (var bulletLevel in ls.Elements(Text + "list-level-style-bullet"))
                        {
                            var lvl = ((int?)bulletLevel.Attribute(Text + "level") ?? 1) - 1;
                            var bulletChar = (string?)bulletLevel.Attribute(Text + "bullet-char");
                            _bulletChars[(name, lvl)] = bulletChar is null or "•" ? null : bulletChar;
                        }
                        foreach (var numberLevel in ls.Elements(Text + "list-level-style-number"))
                        {
                            var lvl = ((int?)numberLevel.Attribute(Text + "level") ?? 1) - 1;
                            _numberFormats[(name, lvl)] = MapOdtNumFormat((string?)numberLevel.Attribute(Style + "num-format"));
                        }
                    }
                }
            }
        }

        public ResolvedStyle Resolve(string? styleName)
        {
            if (string.IsNullOrEmpty(styleName) || !_styles.ContainsKey(styleName))
                return DefaultResolved;

            // Collect the parent chain (most-derived first), then fold base -> derived so derived wins.
            var chain = new List<Entry>();
            var name = styleName;
            var guard = 0;
            while (name is not null && _styles.TryGetValue(name, out var entry) && guard++ < 32)
            {
                chain.Add(entry);
                name = entry.Parent;
            }
            chain.Reverse();

            var run = RunFormatting.Default;
            var para = ParagraphFormatting.Default;
            string? mapped = null;
            foreach (var entry in chain)
            {
                run = OdtFileAdapter.MergeRunFormatting(run, entry.Run);
                para = MergeParaOverlay(para, entry.Paragraph);
                if (MapStyleId(entry.DisplayName) is { } m)
                    mapped = m;
            }
            return new ResolvedStyle(para, run, mapped);
        }

        public bool IsNumberedList(string? styleName) =>
            !string.IsNullOrEmpty(styleName) && _listNumbered.GetValueOrDefault(styleName);

        public bool IsMultiLevelList(string? styleName) =>
            !string.IsNullOrEmpty(styleName) && _listMultiLevel.GetValueOrDefault(styleName);

        public double? ColumnWidthPt(string? styleName) =>
            !string.IsNullOrEmpty(styleName) && _columnWidths.TryGetValue(styleName, out var w) ? w : null;

        /// <summary>The captured <c>text:bullet-char</c> at the given (0-based) level, or null for FreeW's
        /// own default marker ('•') -- see <see cref="ParagraphFormatting.ListMarkerText"/>.</summary>
        public string? BulletCharAt(string? styleName, int level) =>
            !string.IsNullOrEmpty(styleName) && _bulletChars.TryGetValue((styleName, level), out var c) ? c : null;

        /// <summary>The captured <c>style:num-format</c> at the given (0-based) level, defaulting to
        /// <see cref="ListNumberFormat.Decimal"/> when unrecorded.</summary>
        public ListNumberFormat NumberFormatAt(string? styleName, int level) =>
            !string.IsNullOrEmpty(styleName) && _numberFormats.TryGetValue((styleName, level), out var f) ? f : ListNumberFormat.Decimal;

        private static ListNumberFormat MapOdtNumFormat(string? numFormat) => numFormat switch
        {
            "a" => ListNumberFormat.LowerLetter,
            "A" => ListNumberFormat.UpperLetter,
            "i" => ListNumberFormat.LowerRoman,
            "I" => ListNumberFormat.UpperRoman,
            _ => ListNumberFormat.Decimal,
        };

        private static RunFormatting ParseRun(XElement? tp)
        {
            if (tp is null)
                return RunFormatting.Default;

            var fmt = RunFormatting.Default;
            if ((string?)tp.Attribute(Fo + "font-weight") is { } weight)
                fmt = fmt with { Bold = weight is "bold" or "bolder" || (int.TryParse(weight, out var w) && w >= 600) };
            if ((string?)tp.Attribute(Fo + "font-style") is { } style)
                fmt = fmt with { Italic = style is "italic" or "oblique" };
            if ((string?)tp.Attribute(Style + "text-underline-style") is { } ul)
                fmt = fmt with { Underline = !string.Equals(ul, "none", StringComparison.OrdinalIgnoreCase) };
            if ((string?)tp.Attribute(Style + "text-line-through-style") is { } lt)
            {
                var enabled = !string.Equals(lt, "none", StringComparison.OrdinalIgnoreCase);
                var isDouble = string.Equals(
                    (string?)tp.Attribute(Style + "text-line-through-type"),
                    "double",
                    StringComparison.OrdinalIgnoreCase);
                fmt = fmt with
                {
                    Strikethrough = enabled && !isDouble,
                    DoubleStrikethrough = enabled && isDouble
                };
            }
            if (ParseLength((string?)tp.Attribute(Fo + "font-size")) is { } size)
                fmt = fmt with { FontSizePt = size };
            if (ParseColor((string?)tp.Attribute(Fo + "color")) is { } color)
                fmt = fmt with { ColorHex = color };
            var font = (string?)tp.Attribute(Style + "font-name") ?? (string?)tp.Attribute(Fo + "font-family");
            if (!string.IsNullOrEmpty(font))
                fmt = fmt with { FontFamily = font };
            if (ParseColor((string?)tp.Attribute(Fo + "background-color")) is { } hl)
                fmt = fmt with { HighlightColorHex = hl };
            if ((string?)tp.Attribute(Style + "text-position") is { } pos)
            {
                if (pos.StartsWith("super", StringComparison.OrdinalIgnoreCase))
                    fmt = fmt with { VerticalAlign = VerticalAlign.Superscript };
                else if (pos.StartsWith("sub", StringComparison.OrdinalIgnoreCase) || pos.StartsWith('-'))
                    fmt = fmt with { VerticalAlign = VerticalAlign.Subscript };
            }
            return fmt;
        }

        private static ParagraphFormatting ParseParagraph(XElement? pp)
        {
            if (pp is null)
                return ParagraphFormatting.Default;

            var fmt = ParagraphFormatting.Default;
            if ((string?)pp.Attribute(Fo + "text-align") is { } align)
                fmt = fmt with
                {
                    Alignment = align switch
                    {
                        "center" => TextAlignment.Center,
                        "end" or "right" => TextAlignment.Right,
                        "justify" => TextAlignment.Justify,
                        _ => TextAlignment.Left
                    }
                };
            if (ParseLength((string?)pp.Attribute(Fo + "margin-left")) is { } ml)
                fmt = fmt with { IndentLeftPt = ml };
            if (ParseLength((string?)pp.Attribute(Fo + "margin-right")) is { } mr)
                fmt = fmt with { IndentRightPt = mr };
            if (ParseLength((string?)pp.Attribute(Fo + "text-indent")) is { } ti)
                fmt = fmt with { FirstLineIndentPt = ti };
            if (ParseLength((string?)pp.Attribute(Fo + "margin-top")) is { } mt)
                fmt = fmt with { SpaceBeforePt = mt };
            if (ParseLength((string?)pp.Attribute(Fo + "margin-bottom")) is { } mb)
                fmt = fmt with { SpaceAfterPt = mb };
            return fmt;
        }

        private static ParagraphFormatting MergeParaOverlay(ParagraphFormatting b, ParagraphFormatting o) => b with
        {
            Alignment = o.Alignment != TextAlignment.Left ? o.Alignment : b.Alignment,
            IndentLeftPt = o.IndentLeftPt != 0 ? o.IndentLeftPt : b.IndentLeftPt,
            IndentRightPt = o.IndentRightPt != 0 ? o.IndentRightPt : b.IndentRightPt,
            FirstLineIndentPt = o.FirstLineIndentPt != 0 ? o.FirstLineIndentPt : b.FirstLineIndentPt,
            SpaceBeforePt = o.SpaceBeforePt != 0 ? o.SpaceBeforePt : b.SpaceBeforePt,
            SpaceAfterPt = Math.Abs(o.SpaceAfterPt - 8) > double.Epsilon ? o.SpaceAfterPt : b.SpaceAfterPt
        };

        private static string? MapStyleId(string? displayName) => displayName switch
        {
            "Heading 1" => "Heading1",
            "Heading 2" => "Heading2",
            "Heading 3" => "Heading3",
            _ => null
        };
    }

    /// <summary>Reads picture bytes from the package's <c>Pictures/</c> folder by xlink href.</summary>
    private sealed class OdtPictureStore(ZipArchive archive)
    {
        public byte[]? Read(string href)
        {
            var path = href.StartsWith("./", StringComparison.Ordinal) ? href[2..] : href;
            var entry = archive.GetEntry(path);
            if (entry is null)
                return null;
            using var es = entry.Open();
            using var ms = new MemoryStream();
            es.CopyTo(ms);
            return ms.ToArray();
        }
    }

    /// <summary>
    /// Generates ODF auto-styles on write with dedup: identical run/paragraph formatting collapses to one
    /// <c>style:style</c> (named <c>P{n}</c>/<c>T{n}</c>). Default run formatting needs no span style (returns
    /// null). Headings round-trip via <c>text:h</c>/<c>text:outline-level</c>, so named styles stay minimal.
    /// </summary>
    private sealed class OdtStyleWriter
    {
        private readonly Dictionary<string, string> _paraNames = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _runNames = new(StringComparer.Ordinal);
        private readonly List<XElement> _paraStyles = new();
        private readonly List<XElement> _runStyles = new();
        private readonly Dictionary<ListKind, string> _listStyleNames = new();
        private readonly List<XElement> _listStyles = new();
        private int _tableCounter;

        public string ParagraphStyle(ParagraphFormatting f)
        {
            var key = ParaKey(f);
            if (_paraNames.TryGetValue(key, out var existing))
                return existing;
            var name = "P" + (_paraNames.Count + 1);
            _paraNames[key] = name;
            _paraStyles.Add(BuildParaStyle(name, f));
            return name;
        }

        public string? RunStyle(RunFormatting f)
        {
            if (IsDefaultRun(f))
                return null;
            var key = RunKey(f);
            if (_runNames.TryGetValue(key, out var existing))
                return existing;
            var name = "T" + (_runNames.Count + 1);
            _runNames[key] = name;
            _runStyles.Add(BuildRunStyle(name, f));
            return name;
        }

        public string TableName() => "Table" + (++_tableCounter);

        /// <summary>
        /// Returns the (lazily-created) <c>text:list-style</c> name for a bullet, numbered, or multi-level
        /// (outline/legal) list, defining levels 1..9 (matching the editor's <c>MaxListDepth</c>) so nested
        /// lists at any supported depth resolve to a valid style. <see cref="ListKind.MultiLevel"/> gets its
        /// own style (rather than sharing <see cref="ListKind.Number"/>'s) so <see cref="OdtStyleTable.IsMultiLevelList"/>
        /// can tell it apart from a plain numbered list on read via its per-level <c>text:display-levels</c>.
        /// </summary>
        public string ListStyleName(ListKind kind)
        {
            var key = kind switch
            {
                ListKind.Bullet => ListKind.Bullet,
                ListKind.MultiLevel => ListKind.MultiLevel,
                _ => ListKind.Number
            };
            if (_listStyleNames.TryGetValue(key, out var existing))
                return existing;

            var name = key switch
            {
                ListKind.Bullet => "LB1",
                ListKind.MultiLevel => "LM1",
                _ => "LN1"
            };
            _listStyleNames[key] = name;
            _listStyles.Add(BuildListStyle(name, key));
            return name;
        }

        public XElement BuildContentAutoStyles() =>
            new(Office + "automatic-styles", _paraStyles.Concat(_runStyles).Concat(_listStyles));

        public XElement BuildNamedStyles(TextDocument document)
        {
            _ = document; // headings/text styles are emitted as content auto-styles; named styles stay minimal.
            return new XElement(Office + "styles");
        }

        private static bool IsDefaultRun(RunFormatting f) =>
            !f.Bold && !f.Italic && !f.Underline && !f.Strikethrough && !f.DoubleStrikethrough
            && f.FontSizePt is null && f.ColorHex is null && string.IsNullOrEmpty(f.FontFamily)
            && f.HighlightColorHex is null && f.VerticalAlign == VerticalAlign.Baseline;

        private static string ParaKey(ParagraphFormatting f) => string.Create(CultureInfo.InvariantCulture,
            $"{(int)f.Alignment}|{f.IndentLeftPt}|{f.IndentRightPt}|{f.FirstLineIndentPt}|{f.SpaceBeforePt}|{f.SpaceAfterPt}");

        private static string RunKey(RunFormatting f) => string.Create(CultureInfo.InvariantCulture,
            $"{f.Bold}|{f.Italic}|{f.Underline}|{f.Strikethrough}|{f.DoubleStrikethrough}|{f.FontSizePt}|{f.ColorHex}|{f.FontFamily}|{f.HighlightColorHex}|{(int)f.VerticalAlign}");

        private static XElement BuildParaStyle(string name, ParagraphFormatting f)
        {
            var props = new XElement(Style + "paragraph-properties",
                new XAttribute(Fo + "text-align", f.Alignment switch
                {
                    TextAlignment.Center => "center",
                    TextAlignment.Right => "end",
                    TextAlignment.Justify => "justify",
                    _ => "start"
                }),
                new XAttribute(Fo + "margin-top", FormatLength(f.SpaceBeforePt)),
                new XAttribute(Fo + "margin-bottom", FormatLength(f.SpaceAfterPt)));
            if (f.IndentLeftPt != 0) props.Add(new XAttribute(Fo + "margin-left", FormatLength(f.IndentLeftPt)));
            if (f.IndentRightPt != 0) props.Add(new XAttribute(Fo + "margin-right", FormatLength(f.IndentRightPt)));
            if (f.FirstLineIndentPt != 0) props.Add(new XAttribute(Fo + "text-indent", FormatLength(f.FirstLineIndentPt)));
            return new XElement(Style + "style",
                new XAttribute(Style + "name", name),
                new XAttribute(Style + "family", "paragraph"),
                props);
        }

        private static XElement BuildRunStyle(string name, RunFormatting f)
        {
            var props = new XElement(Style + "text-properties");
            if (f.Bold) props.Add(new XAttribute(Fo + "font-weight", "bold"));
            if (f.Italic) props.Add(new XAttribute(Fo + "font-style", "italic"));
            if (f.Underline) props.Add(new XAttribute(Style + "text-underline-style", "solid"));
            if (f.Strikethrough) props.Add(new XAttribute(Style + "text-line-through-style", "solid"));
            if (f.DoubleStrikethrough)
            {
                props.SetAttributeValue(Style + "text-line-through-style", "solid");
                props.SetAttributeValue(Style + "text-line-through-type", "double");
            }
            if (f.FontSizePt is { } sz) props.Add(new XAttribute(Fo + "font-size", sz.ToString("0.##", CultureInfo.InvariantCulture) + "pt"));
            if (f.ColorHex is { } c) props.Add(new XAttribute(Fo + "color", FormatColor(c)));
            if (!string.IsNullOrEmpty(f.FontFamily)) props.Add(new XAttribute(Fo + "font-family", f.FontFamily));
            if (f.HighlightColorHex is { } h) props.Add(new XAttribute(Fo + "background-color", FormatColor(h)));
            if (f.VerticalAlign == VerticalAlign.Superscript) props.Add(new XAttribute(Style + "text-position", "super 58%"));
            else if (f.VerticalAlign == VerticalAlign.Subscript) props.Add(new XAttribute(Style + "text-position", "sub 58%"));
            return new XElement(Style + "style",
                new XAttribute(Style + "name", name),
                new XAttribute(Style + "family", "text"),
                props);
        }

        private const int ListStyleLevels = 9; // matches the Avalonia editor's MaxListDepth.

        private static XElement BuildListStyle(string name, ListKind kind)
        {
            var numbered = kind != ListKind.Bullet;
            var listStyle = new XElement(Text + "list-style", new XAttribute(Style + "name", name));
            for (var level = 1; level <= ListStyleLevels; level++)
            {
                var labelIndent = FormatLength(0.25 * 72 * (level - 1));
                var levelProps = new XElement(Style + "list-level-properties",
                    new XAttribute(Text + "space-before", labelIndent),
                    new XAttribute(Text + "min-label-width", FormatLength(0.25 * 72)));

                XElement levelStyle;
                if (numbered)
                {
                    levelStyle = new XElement(Text + "list-level-style-number",
                        new XAttribute(Text + "level", level),
                        new XAttribute(Style + "num-format", "1"),
                        new XAttribute(Style + "num-suffix", "."),
                        levelProps);

                    // MultiLevel is an outline/legal numbering whose level text accumulates the ancestors'
                    // counters (1, 1.1, 1.1.1, …); ODF expresses that via text:display-levels = the level
                    // depth itself. A plain Number list omits the attribute (defaults to 1: no accumulation),
                    // which is exactly what OdtStyleTable.IsMultiLevelList inspects to tell them apart on read.
                    if (kind == ListKind.MultiLevel)
                        levelStyle.Add(new XAttribute(Text + "display-levels", level));
                }
                else
                {
                    levelStyle = new XElement(Text + "list-level-style-bullet",
                        new XAttribute(Text + "level", level),
                        new XAttribute(Text + "bullet-char", "•"),
                        levelProps);
                }

                listStyle.Add(levelStyle);
            }
            return listStyle;
        }
    }

    /// <summary>Collects images on write into the package's <c>Pictures/</c> folder + manifest media types.</summary>
    private sealed class OdtPictureWriter
    {
        private readonly List<(string Name, byte[] Bytes, string MediaType)> _items = new();

        public string Add(InlineImage image)
        {
            var ext = InlineImage.ExtensionFor(image.Format);
            var name = $"Pictures/image{_items.Count + 1}.{ext}";
            _items.Add((name, image.Bytes, MediaTypeFor(ext)));
            return name;
        }

        public IEnumerable<(string Name, byte[] Bytes)> Pictures => _items.Select(i => (i.Name, i.Bytes));

        public IEnumerable<(string Name, string MediaType)> MediaTypes => _items.Select(i => (i.Name, i.MediaType));

        private static string MediaTypeFor(string ext) => ext switch
        {
            "png" => "image/png",
            "jpeg" or "jpg" => "image/jpeg",
            "gif" => "image/gif",
            "bmp" => "image/bmp",
            "tiff" => "image/tiff",
            "emf" => "image/x-emf",
            "wmf" => "image/x-wmf",
            _ => "application/octet-stream"
        };
    }
}
