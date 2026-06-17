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
        ReadCustomProperties(archive, document);
        ReadStyles(archive, document);
        var imageRelationships = ReadImageRelationships(archive);
        var hyperlinkRelationships = ReadHyperlinkRelationships(archive);
        var numbering = ReadNumbering(archive);

        var body = documentXml.Root?.Element(W + "body");
        if (body is not null)
        {
            foreach (var element in body.Elements())
            {
                if (element.Name == W + "p")
                    document.Blocks.Add(ReadParagraph(element, archive, imageRelationships, hyperlinkRelationships, numbering));
                else if (element.Name == W + "tbl")
                    document.Blocks.Add(ReadTable(element, archive, imageRelationships, hyperlinkRelationships, numbering));
            }
        }

        if (document.Blocks.Count == 0)
            document.Blocks.Add(new Paragraph());

        ReadHeaderFooter(documentXml, archive, document, imageRelationships, hyperlinkRelationships);
        ReadFootnotes(archive, document, imageRelationships, hyperlinkRelationships);
        ReadEndnotes(archive, document, imageRelationships, hyperlinkRelationships);
        ReadComments(archive, document, imageRelationships, hyperlinkRelationships);
        ReadSettings(archive, document);

        return document;
    }

    /// <summary>
    /// Resolves the settings part (via the officeDocument's "/settings" relationship, falling back to the
    /// conventional word/settings.xml path), loads w:settings, and maps w:documentProtection/@w:edit back
    /// into <see cref="TextDocument.Protection"/> and the w:autoHyphenation toggle into
    /// <see cref="PageSettings.AutoHyphenation"/>. A missing part — or one without an enforced
    /// documentProtection — leaves the document at <see cref="ProtectionMode.None"/>; a missing
    /// autoHyphenation leaves it disabled.
    /// </summary>
    private static void ReadSettings(ZipArchive archive, TextDocument document)
    {
        var settingsXml = LoadPart(archive, ResolveSettingsPartPath(archive) ?? "word/settings.xml");
        var root = settingsXml?.Root;
        if (root is null)
            return;

        // Automatic hyphenation (w:autoHyphenation) is an on/off toggle: present + not explicitly off.
        document.Page.AutoHyphenation = ReadToggle(root, "autoHyphenation");

        var protection = root.Element(W + "documentProtection");
        if (protection is null)
            return;

        // Honour protection only when enforced (w:enforcement on/absent-with-edit); an explicit
        // enforcement="0"/"off"/"false" means the restriction is not active, so treat it as None.
        var enforcement = protection.Attribute(W + "enforcement")?.Value;
        if (enforcement is "0" or "false" or "off")
            return;

        var mode = ProtectionModeFromEditToken(protection.Attribute(W + "edit")?.Value);
        if (mode != ProtectionMode.None)
            document.Protection = new ProtectionSettings(mode);
    }

    /// <summary>
    /// Finds the settings part path from the document relationships (the rel whose Type ends with
    /// "/settings"), resolved relative to the word/ folder. Returns null when no such relationship exists.
    /// </summary>
    private static string? ResolveSettingsPartPath(ZipArchive archive)
    {
        var relsXml = LoadPart(archive, "word/_rels/document.xml.rels");
        var relationships = relsXml?.Root?.Elements(Rel + "Relationship");
        if (relationships is null)
            return null;

        foreach (var rel in relationships)
        {
            var type = rel.Attribute("Type")?.Value;
            if (type is null || !type.EndsWith("/settings", StringComparison.Ordinal))
                continue;
            var target = rel.Attribute("Target")?.Value;
            if (!string.IsNullOrEmpty(target))
                return "word/" + target.TrimStart('/');
        }
        return null;
    }

    /// <summary>
    /// Loads word/comments.xml (if present) into <see cref="TextDocument.Comments"/>, reconstructing
    /// each w:comment's author/initials/date and its paragraphs. Comments referenced by no body range
    /// are still kept; the body range markers are recovered separately in <see cref="ReadParagraph"/>.
    /// </summary>
    private static void ReadComments(
        ZipArchive archive,
        TextDocument document,
        IReadOnlyDictionary<string, string> imageRelationships,
        IReadOnlyDictionary<string, string> hyperlinkRelationships)
    {
        var commentsXml = LoadPart(archive, "word/comments.xml");
        var root = commentsXml?.Root;
        if (root is null)
            return;

        var noNumbering = new Dictionary<int, ListKind>();
        foreach (var element in root.Elements(W + "comment"))
        {
            if (!int.TryParse(element.Attribute(W + "id")?.Value, out var id))
                continue;

            var comment = new Comment(id)
            {
                Author = element.Attribute(W + "author")?.Value ?? string.Empty,
                Initials = element.Attribute(W + "initials")?.Value ?? string.Empty,
                DateXml = element.Attribute(W + "date")?.Value
            };
            foreach (var p in element.Elements(W + "p"))
                comment.Content.Add(ReadParagraph(p, archive, imageRelationships, hyperlinkRelationships, noNumbering));
            if (comment.Content.Count == 0)
                comment.Content.Add(new Paragraph());
            document.Comments[id] = comment;
        }
    }

    /// <summary>
    /// Loads word/footnotes.xml (if present) into <see cref="TextDocument.Footnotes"/>, reconstructing
    /// each w:footnote's paragraphs. The conventional separator footnotes (type separator /
    /// continuationSeparator, ids -1 and 0) are skipped — only real content footnotes are kept.
    /// </summary>
    private static void ReadFootnotes(
        ZipArchive archive,
        TextDocument document,
        IReadOnlyDictionary<string, string> imageRelationships,
        IReadOnlyDictionary<string, string> hyperlinkRelationships)
    {
        var footnotesXml = LoadPart(archive, "word/footnotes.xml");
        var root = footnotesXml?.Root;
        if (root is null)
            return;

        var noNumbering = new Dictionary<int, ListKind>();
        foreach (var element in root.Elements(W + "footnote"))
        {
            var type = element.Attribute(W + "type")?.Value;
            if (type is "separator" or "continuationSeparator")
                continue;
            if (!int.TryParse(element.Attribute(W + "id")?.Value, out var id))
                continue;

            var footnote = new Footnote(id);
            foreach (var p in element.Elements(W + "p"))
                footnote.Content.Add(ReadParagraph(p, archive, imageRelationships, hyperlinkRelationships, noNumbering));
            if (footnote.Content.Count == 0)
                footnote.Content.Add(new Paragraph());
            document.Footnotes[id] = footnote;
        }
    }

    /// <summary>
    /// Loads word/endnotes.xml (if present) into <see cref="TextDocument.Endnotes"/>, reconstructing
    /// each w:endnote's paragraphs. The conventional separator endnotes (type separator /
    /// continuationSeparator, ids -1 and 0) are skipped — only real content endnotes are kept. Mirrors
    /// <see cref="ReadFootnotes"/>.
    /// </summary>
    private static void ReadEndnotes(
        ZipArchive archive,
        TextDocument document,
        IReadOnlyDictionary<string, string> imageRelationships,
        IReadOnlyDictionary<string, string> hyperlinkRelationships)
    {
        var endnotesXml = LoadPart(archive, "word/endnotes.xml");
        var root = endnotesXml?.Root;
        if (root is null)
            return;

        var noNumbering = new Dictionary<int, ListKind>();
        foreach (var element in root.Elements(W + "endnote"))
        {
            var type = element.Attribute(W + "type")?.Value;
            if (type is "separator" or "continuationSeparator")
                continue;
            if (!int.TryParse(element.Attribute(W + "id")?.Value, out var id))
                continue;

            var endnote = new Endnote(id);
            foreach (var p in element.Elements(W + "p"))
                endnote.Content.Add(ReadParagraph(p, archive, imageRelationships, hyperlinkRelationships, noNumbering));
            if (endnote.Content.Count == 0)
                endnote.Content.Add(new Paragraph());
            document.Endnotes[id] = endnote;
        }
    }

    /// <summary>
    /// Resolves the default header/footer references in w:sectPr (r:id → document rels → part path),
    /// loads those parts (w:hdr / w:ftr) and reconstructs <see cref="TextDocument.Header"/> / Footer.
    /// </summary>
    private static void ReadHeaderFooter(
        XDocument documentXml,
        ZipArchive archive,
        TextDocument document,
        IReadOnlyDictionary<string, string> imageRelationships,
        IReadOnlyDictionary<string, string> hyperlinkRelationships)
    {
        var sectPr = documentXml.Root?.Element(W + "body")?.Element(W + "sectPr");
        if (sectPr is null)
            return;

        // Equal-width column layout (w:cols/@w:num + @w:space) lives alongside the header/footer
        // references in w:sectPr, so recover it here while we already hold the section element.
        var cols = sectPr.Element(W + "cols");
        if (cols is not null)
        {
            if (int.TryParse(cols.Attribute(W + "num")?.Value, out var num) && num >= 1)
                document.Page.ColumnCount = num;
            if (cols.Attribute(W + "space") is { } space)
                document.Page.ColumnSpacingPt = DxaToPoints(space.Value);
        }

        // Page border (w:pgBorders) lives in the same w:sectPr; recover it into PageSettings.PageBorder.
        document.Page.PageBorder = ReadPageBorder(sectPr.Element(W + "pgBorders"));

        // Line numbering (w:lnNumType) lives in the same w:sectPr; recover the mode + interval.
        ReadLineNumbering(sectPr.Element(W + "lnNumType"), document.Page);

        // Page vertical alignment (w:vAlign): map the val token back ("both"→Justified); absent → Top.
        document.Page.VerticalAlignment =
            VerticalAlignmentFromToken(sectPr.Element(W + "vAlign")?.Attribute(W + "val")?.Value);

        // "Different first page" (w:titlePg): a bare toggle; absent → false.
        document.Page.DifferentFirstPage = ReadToggle(sectPr, "titlePg");

        var partsById = ReadHeaderFooterRelationships(archive);

        document.Header = ReadHeaderFooterPart(
            sectPr, "headerReference", W + "hdr", partsById, archive, imageRelationships, hyperlinkRelationships);
        document.Footer = ReadHeaderFooterPart(
            sectPr, "footerReference", W + "ftr", partsById, archive, imageRelationships, hyperlinkRelationships);
    }

    private static HeaderFooter? ReadHeaderFooterPart(
        XElement sectPr,
        string referenceName,
        XName rootName,
        IReadOnlyDictionary<string, string> partsById,
        ZipArchive archive,
        IReadOnlyDictionary<string, string> imageRelationships,
        IReadOnlyDictionary<string, string> hyperlinkRelationships)
    {
        // Prefer the default reference; fall back to the first reference of this kind if present.
        var references = sectPr.Elements(W + referenceName).ToList();
        if (references.Count == 0)
            return null;
        var reference = references.FirstOrDefault(r => r.Attribute(W + "type")?.Value == "default") ?? references[0];

        var id = reference.Attribute(R + "id")?.Value;
        if (id is null || !partsById.TryGetValue(id, out var partPath))
            return null;

        var partXml = LoadPart(archive, partPath);
        var root = partXml?.Root;
        if (root is null || root.Name != rootName)
            return null;

        var result = new HeaderFooter();
        // Header/footer paragraphs carry no list numbering context (numbering.xml targets the body).
        var noNumbering = new Dictionary<int, ListKind>();
        foreach (var p in root.Elements(W + "p"))
            result.Paragraphs.Add(ReadParagraph(p, archive, imageRelationships, hyperlinkRelationships, noNumbering));
        return result;
    }

    /// <summary>Maps relationship id → part path for header/footer relationships in document.xml.rels.</summary>
    private static Dictionary<string, string> ReadHeaderFooterRelationships(ZipArchive archive)
    {
        var map = new Dictionary<string, string>();
        var relsXml = LoadPart(archive, "word/_rels/document.xml.rels");
        var relationships = relsXml?.Root?.Elements(Rel + "Relationship");
        if (relationships is null)
            return map;

        foreach (var rel in relationships)
        {
            var type = rel.Attribute("Type")?.Value;
            if (type is null || !(type.EndsWith("/header", StringComparison.Ordinal) || type.EndsWith("/footer", StringComparison.Ordinal)))
                continue;
            var id = rel.Attribute("Id")?.Value;
            var target = rel.Attribute("Target")?.Value;
            if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(target))
                map[id] = "word/" + target.TrimStart('/');
        }
        return map;
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
        IReadOnlyDictionary<string, string> hyperlinkRelationships,
        IReadOnlyDictionary<int, ListKind> numbering)
    {
        var paragraph = new Paragraph();
        var pPr = p.Element(W + "pPr");
        if (pPr is not null)
        {
            paragraph.StyleId = pPr.Element(W + "pStyle")?.Attribute(W + "val")?.Value;
            paragraph.Formatting = ReadParagraphFormatting(pPr, numbering);
        }

        // Iterate in document order so runs nested inside a w:hyperlink keep their position, and so a
        // bookmark's name (w:bookmarkStart, a run sibling) is captured wherever it appears. A
        // w:hyperlink either carries an r:id (external URL, resolved via the rels) or a w:anchor
        // (internal link to a bookmark name).
        //
        // Review comments overlay this: a w:commentRangeStart/End pair brackets the runs it covers,
        // and the trailing w:commentReference run anchors the comment. We track the open range id so
        // every covered run gets its CommentId, and recover the reference run as a textless anchor.
        var activeCommentId = (int?)null;
        foreach (var child in p.Elements())
        {
            if (child.Name == W + "commentRangeStart")
            {
                if (int.TryParse(child.Attribute(W + "id")?.Value, out var startId))
                    activeCommentId = startId;
            }
            else if (child.Name == W + "commentRangeEnd")
            {
                activeCommentId = null;
            }
            else if (child.Name == W + "r")
            {
                // A run carrying a w:commentReference is the textless comment anchor; recover it.
                var commentRef = child.Element(W + "commentReference");
                if (commentRef is not null && int.TryParse(commentRef.Attribute(W + "id")?.Value, out var refId))
                    paragraph.Runs.Add(Run.CommentReference(refId));
                else
                    AddRun(paragraph, child, archive, imageRelationships, hyperlinkUrl: null, hyperlinkAnchor: null, commentId: activeCommentId);
            }
            else if (child.Name == W + "hyperlink")
            {
                var anchor = child.Attribute(W + "anchor")?.Value;
                var id = child.Attribute(R + "id")?.Value;
                var url = id is not null && hyperlinkRelationships.TryGetValue(id, out var target) ? target : null;
                var tooltip = child.Attribute(W + "tooltip")?.Value;
                foreach (var r in child.Elements(W + "r"))
                    AddRun(paragraph, r, archive, imageRelationships, url, url is null ? anchor : null, commentId: activeCommentId, hyperlinkTooltip: tooltip);
            }
            else if (child.Name == W + "ins" || child.Name == W + "del")
            {
                // A tracked insertion (w:ins) or deletion (w:del) wraps one or more runs (and possibly
                // hyperlinks). Recover the revision kind plus author/date and stamp every covered run.
                var kind = child.Name == W + "del" ? RevisionKind.Deleted : RevisionKind.Inserted;
                var author = child.Attribute(W + "author")?.Value;
                var date = child.Attribute(W + "date")?.Value;
                var revision = new RevisionInfo(kind, author, date);

                foreach (var revChild in child.Elements())
                {
                    if (revChild.Name == W + "r")
                        AddRun(paragraph, revChild, archive, imageRelationships, hyperlinkUrl: null, hyperlinkAnchor: null, commentId: activeCommentId, revision: revision);
                    else if (revChild.Name == W + "hyperlink")
                    {
                        var hAnchor = revChild.Attribute(W + "anchor")?.Value;
                        var hId = revChild.Attribute(R + "id")?.Value;
                        var hUrl = hId is not null && hyperlinkRelationships.TryGetValue(hId, out var hTarget) ? hTarget : null;
                        var hTooltip = revChild.Attribute(W + "tooltip")?.Value;
                        foreach (var r in revChild.Elements(W + "r"))
                            AddRun(paragraph, r, archive, imageRelationships, hUrl, hUrl is null ? hAnchor : null, commentId: activeCommentId, revision: revision, hyperlinkTooltip: hTooltip);
                    }
                }
            }
            else if (child.Name == W + "sdt")
            {
                // A content control (structured document tag): w:sdtPr describes the control (tag/alias +
                // kind), w:sdtContent holds the wrapped run(s). Recover the control and stamp every content
                // run with it (one shared instance so the writer re-coalesces them into one w:sdt).
                var control = ReadContentControl(child.Element(W + "sdtPr"));
                var sdtContent = child.Element(W + "sdtContent");
                if (sdtContent is not null)
                {
                    foreach (var sdtChild in sdtContent.Elements(W + "r"))
                        AddRun(paragraph, sdtChild, archive, imageRelationships,
                            hyperlinkUrl: null, hyperlinkAnchor: null, commentId: activeCommentId, control: control);
                }
            }
            else if (child.Name == W + "fldSimple")
            {
                AddSimpleField(paragraph, child);
            }
            else if (child.Name == W + "bookmarkStart")
            {
                // Capture the first non-internal bookmark name on the paragraph. Word emits an
                // implicit "_GoBack" bookmark on the document; skip it so it is not mistaken for a target.
                var name = child.Attribute(W + "name")?.Value;
                if (paragraph.BookmarkName is null && name is { Length: > 0 } && name != "_GoBack")
                    paragraph.BookmarkName = name;
            }
        }

        return paragraph;
    }

    /// <summary>
    /// Reads a w:fldSimple. A recognised field (PAGE, DATE, TIME, FILENAME, AUTHOR, NUMPAGES) becomes a
    /// field run carrying that kind plus its cached display text; the kind is matched off the leading
    /// instruction keyword so formatting switches (e.g. <c>DATE \@ "d MMMM yyyy"</c>) are tolerated. Any
    /// other field is flattened to its cached display text (the text inside the wrapped run) so nothing
    /// is lost.
    /// </summary>
    private static void AddSimpleField(Paragraph paragraph, XElement fldSimple)
    {
        var instruction = fldSimple.Attribute(W + "instr")?.Value ?? string.Empty;
        var inner = fldSimple.Element(W + "r");
        var text = string.Concat(fldSimple.Descendants(W + "t").Select(t => t.Value));
        var formatting = ReadRunFormatting(inner?.Element(W + "rPr"));

        if (FieldKindFor(instruction) is { } kind)
        {
            // PAGE keeps its historic "1" fallback when no cached value was written; the rest are happy
            // with whatever cached text the field carried (possibly empty).
            var fallback = kind == RunFieldKind.PageNumber && text.Length == 0 ? "1" : text;
            paragraph.Runs.Add(new Run(fallback, formatting) { FieldKind = kind });
        }
        else if (text.Length > 0)
        {
            paragraph.Runs.Add(new Run(text, formatting));
        }
    }

    /// <summary>
    /// Maps a w:fldSimple/@w:instr to a <see cref="RunFieldKind"/> by its leading keyword, tolerating
    /// surrounding whitespace and trailing field switches. Returns null for unrecognised fields.
    /// </summary>
    private static RunFieldKind? FieldKindFor(string instruction)
    {
        var keyword = instruction.Trim().Split(' ', '\t', '\\')[0];
        return keyword.ToUpperInvariant() switch
        {
            "PAGE" => RunFieldKind.PageNumber,
            "DATE" => RunFieldKind.Date,
            "TIME" => RunFieldKind.Time,
            "FILENAME" => RunFieldKind.FileName,
            "AUTHOR" => RunFieldKind.Author,
            "NUMPAGES" => RunFieldKind.NumPages,
            _ => null
        };
    }

    /// <summary>
    /// Reads a content control's w:sdtPr into a <see cref="ContentControl"/>: recovers the optional
    /// w:tag / w:alias and the control kind. A w14:checkbox (or w:checkbox) marks a checkbox control,
    /// whose checked state comes from the nested w14:checked/@val ("1"/"true"/"on"); anything else is a
    /// plain-text control. A null/absent w:sdtPr yields a default plain-text control.
    /// </summary>
    private static ContentControl ReadContentControl(XElement? sdtPr)
    {
        var tag = sdtPr?.Element(W + "tag")?.Attribute(W + "val")?.Value;
        var alias = sdtPr?.Element(W + "alias")?.Attribute(W + "val")?.Value;

        var checkbox = sdtPr?.Element(W14 + "checkbox") ?? sdtPr?.Element(W + "checkbox");
        if (checkbox is not null)
        {
            var val = (checkbox.Element(W14 + "checked") ?? checkbox.Element(W + "checked"))
                ?.Attribute(W14 + "val")?.Value
                ?? (checkbox.Element(W14 + "checked") ?? checkbox.Element(W + "checked"))?.Attribute(W + "val")?.Value;
            var isChecked = val is "1" or "true" or "on";
            return new ContentControl(ContentControlKind.CheckBox,
                string.IsNullOrEmpty(tag) ? null : tag,
                string.IsNullOrEmpty(alias) ? null : alias,
                isChecked);
        }

        return new ContentControl(ContentControlKind.PlainText,
            string.IsNullOrEmpty(tag) ? null : tag,
            string.IsNullOrEmpty(alias) ? null : alias);
    }

    /// <summary>Carries a tracked-change kind plus its author/date while reading runs inside a w:ins/w:del.</summary>
    private readonly record struct RevisionInfo(RevisionKind Kind, string? Author, string? DateXml);

    private static void AddRun(
        Paragraph paragraph,
        XElement r,
        ZipArchive archive,
        IReadOnlyDictionary<string, string> imageRelationships,
        string? hyperlinkUrl,
        string? hyperlinkAnchor,
        int? commentId = null,
        RevisionInfo revision = default,
        ContentControl? control = null,
        string? hyperlinkTooltip = null)
    {
        void ApplyRevision(Run run)
        {
            if (revision.Kind == RevisionKind.None)
                return;
            run.Revision = revision.Kind;
            run.RevisionAuthor = revision.Author;
            run.RevisionDateXml = revision.DateXml;
        }

        var image = ReadImage(r, archive, imageRelationships);
        if (image is not null)
        {
            var imageRun = new Run(string.Empty) { Image = image, HyperlinkUrl = hyperlinkUrl, HyperlinkAnchor = hyperlinkAnchor, HyperlinkTooltip = hyperlinkTooltip, CommentId = commentId };
            ApplyRevision(imageRun);
            paragraph.Runs.Add(imageRun);
            return;
        }

        // A run wrapping a w:footnoteReference is a footnote marker; recover its id into the model.
        var footnoteRef = r.Element(W + "footnoteReference");
        if (footnoteRef is not null && int.TryParse(footnoteRef.Attribute(W + "id")?.Value, out var footnoteId))
        {
            var footnoteRun = Run.FootnoteReference(footnoteId, ReadRunFormatting(r.Element(W + "rPr")));
            ApplyRevision(footnoteRun);
            paragraph.Runs.Add(footnoteRun);
            return;
        }

        // A run wrapping a w:endnoteReference is an endnote marker; recover its id into the model.
        var endnoteRef = r.Element(W + "endnoteReference");
        if (endnoteRef is not null && int.TryParse(endnoteRef.Attribute(W + "id")?.Value, out var endnoteId))
        {
            var endnoteRun = Run.EndnoteReference(endnoteId, ReadRunFormatting(r.Element(W + "rPr")));
            ApplyRevision(endnoteRun);
            paragraph.Runs.Add(endnoteRun);
            return;
        }

        // A tracked deletion stores its text in w:delText; ordinary/inserted runs use w:t.
        var text = string.Concat(r.Elements(W + "t").Select(t => t.Value))
            + string.Concat(r.Elements(W + "delText").Select(t => t.Value));
        if (r.Elements(W + "tab").Any())
            text += "\t";
        if (text.Length == 0)
            return;
        var textRun = new Run(text, ReadRunFormatting(r.Element(W + "rPr"))) { HyperlinkUrl = hyperlinkUrl, HyperlinkAnchor = hyperlinkAnchor, HyperlinkTooltip = hyperlinkTooltip, CommentId = commentId, Control = control };
        ApplyRevision(textRun);
        paragraph.Runs.Add(textRun);
    }

    private static Table ReadTable(
        XElement tbl,
        ZipArchive archive,
        IReadOnlyDictionary<string, string> imageRelationships,
        IReadOnlyDictionary<string, string> hyperlinkRelationships,
        IReadOnlyDictionary<int, ListKind> numbering)
    {
        var table = new Table();

        var tblPr = tbl.Element(W + "tblPr");
        var borders = tblPr?.Element(W + "tblBorders");

        // The table-style toggles round-trip via w:tblLook (HeaderRow=firstRow, BandedRows=noHBand="0")
        // and, for RepeatHeaderRow, via w:trPr/w:tblHeader on the first row. See DocxWriter.BuildTable.
        var tblLook = tblPr?.Element(W + "tblLook");
        var headerRow = tblLook?.Attribute(W + "firstRow")?.Value == "1";
        var bandedRows = tblLook?.Attribute(W + "noHBand")?.Value == "0";
        var firstRow = tbl.Elements(W + "tr").FirstOrDefault();
        var repeatHeader = firstRow?.Element(W + "trPr")?.Element(W + "tblHeader") is not null;

        table.Formatting = TableFormatting.Default with
        {
            Borders = ReadBorders(borders),
            HeaderRow = headerRow,
            BandedRows = bandedRows,
            RepeatHeaderRow = repeatHeader
        };

        // The table grid (w:tblGrid/w:gridCol) carries per-column widths in dxa.
        var grid = tbl.Element(W + "tblGrid");
        if (grid is not null)
        {
            foreach (var gridCol in grid.Elements(W + "gridCol"))
                table.ColumnWidthsPt.Add(DxaToPoints(gridCol.Attribute(W + "w")?.Value));
        }

        var rowIndex = 0;
        foreach (var tr in tbl.Elements(W + "tr"))
        {
            var row = new TableRow();
            // Cells in styled rows carry the style fill (header/banded) we wrote; recognise and strip it so
            // it reads back as style-derived shading, not as an explicit per-cell colour.
            var isStyleHeader = headerRow && rowIndex == 0;
            var isStyleBanded = bandedRows && !isStyleHeader && IsBandedBodyRow(rowIndex, headerRow);
            foreach (var tc in tr.Elements(W + "tc"))
            {
                var cell = new TableCell();
                var tcPr = tc.Element(W + "tcPr");
                if (tcPr is not null)
                {
                    var width = tcPr.Element(W + "tcW")?.Attribute(W + "w")?.Value;
                    if (width is not null)
                        cell.WidthPt = DxaToPoints(width);
                    var shading = tcPr.Element(W + "shd")?.Attribute(W + "fill")?.Value;
                    var normalized = shading is null or "auto" ? null : shading.TrimStart('#');
                    // Drop the style-derived header/banded fill so it doesn't masquerade as cell shading.
                    if (normalized is not null
                        && !(isStyleHeader && string.Equals(normalized, StyleHeaderFill, StringComparison.OrdinalIgnoreCase))
                        && !(isStyleBanded && string.Equals(normalized, StyleBandedFill, StringComparison.OrdinalIgnoreCase)))
                        cell.ShadingColorHex = "#" + normalized;

                    // Horizontal merge: w:gridSpan w:val="N". Absent (or <2) means no span.
                    var gridSpan = tcPr.Element(W + "gridSpan")?.Attribute(W + "val")?.Value;
                    if (gridSpan is not null && int.TryParse(gridSpan, out var span) && span > 1)
                        cell.GridSpan = span;

                    // Vertical merge: w:vMerge with w:val="restart" starts a run; a w:vMerge with no
                    // value (or "continue") is absorbed into the restart above it.
                    var vMerge = tcPr.Element(W + "vMerge");
                    if (vMerge is not null)
                    {
                        var vVal = vMerge.Attribute(W + "val")?.Value;
                        cell.VerticalMerge = vVal == "restart"
                            ? VerticalMergeState.Restart
                            : VerticalMergeState.Continue;
                    }
                }
                foreach (var p in tc.Elements(W + "p"))
                    cell.Paragraphs.Add(ReadParagraph(p, archive, imageRelationships, hyperlinkRelationships, numbering));
                if (cell.Paragraphs.Count == 0)
                    cell.Paragraphs.Add(new Paragraph());
                row.Cells.Add(cell);
            }
            table.Rows.Add(row);
            rowIndex++;
        }

        return table;
    }

    // The style fills DocxWriter emits for header / banded rows (RRGGBB, no '#'); recognised on read so
    // they don't read back as explicit per-cell shading.
    private const string StyleHeaderFill = "D9E2F3";
    private const string StyleBandedFill = "F2F2F2";

    /// <summary>Mirror of DocxWriter's banding rule: which body row (2nd, 4th, ...) carries the band fill.</summary>
    private static bool IsBandedBodyRow(int rowIndex, bool hasHeader)
    {
        var bodyIndex = hasHeader ? rowIndex - 1 : rowIndex;
        return bodyIndex >= 0 && bodyIndex % 2 == 1;
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

    internal static ParagraphFormatting ReadParagraphFormatting(XElement pPr) =>
        ReadParagraphFormatting(pPr, new Dictionary<int, ListKind>());

    internal static ParagraphFormatting ReadParagraphFormatting(XElement pPr, IReadOnlyDictionary<int, ListKind> numbering)
    {
        var spacing = pPr.Element(W + "spacing");
        var indent = pPr.Element(W + "ind");
        var jc = pPr.Element(W + "jc")?.Attribute(W + "val")?.Value;
        var shading = pPr.Element(W + "shd")?.Attribute(W + "fill")?.Value;

        // A list paragraph references a numbering definition via pPr/w:numPr (w:numId + w:ilvl).
        // Resolve the numId to a ListKind through numbering.xml; the ilvl becomes the ListLevel.
        var listKind = ListKind.None;
        var listLevel = 0;
        var numPr = pPr.Element(W + "numPr");
        if (numPr is not null)
        {
            var numId = ParseInt(numPr.Element(W + "numId")?.Attribute(W + "val")?.Value);
            if (numbering.TryGetValue(numId, out var kind) && kind != ListKind.None)
            {
                listKind = kind;
                listLevel = ParseInt(numPr.Element(W + "ilvl")?.Attribute(W + "val")?.Value);
            }
        }

        // w:pageBreakBefore is a toggle: present (and not val="false"/"0") means a page break is forced.
        var pageBreakBefore = ReadToggle(pPr, "pageBreakBefore");
        // Flow control toggles read the same way as pageBreakBefore. widowControl is read literally:
        // absent means false (FreeW does not apply Word's implicit default-on), keeping round-trips stable.
        var keepWithNext = ReadToggle(pPr, "keepNext");
        var keepLinesTogether = ReadToggle(pPr, "keepLines");
        var widowControl = ReadToggle(pPr, "widowControl");

        return ParagraphFormatting.Default with
        {
            Border = ReadParagraphBorder(pPr.Element(W + "pBdr")),
            PageBreakBefore = pageBreakBefore,
            KeepWithNext = keepWithNext,
            KeepLinesTogether = keepLinesTogether,
            WidowControl = widowControl,
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
            FirstLineIndentPt = DxaToPoints(indent?.Attribute(W + "firstLine")?.Value),
            ListKind = listKind,
            ListLevel = listLevel,
            TabStops = ReadTabStops(pPr.Element(W + "tabs"))
        };
    }

    /// <summary>
    /// Reads paragraph tab stops (w:tabs) into the model list, one <see cref="TabStop"/> per w:tab.
    /// Positions come from w:pos (dxa -> points); the alignment from w:val; the optional leader fill
    /// from w:leader (absent -> <see cref="TabLeader.None"/>). "clear" stops (which remove an inherited
    /// stop) carry no real position and are skipped. Returns an empty list if absent.
    /// </summary>
    private static IReadOnlyList<TabStop> ReadTabStops(XElement? tabs)
    {
        if (tabs is null)
            return [];
        var stops = new List<TabStop>();
        foreach (var tab in tabs.Elements(W + "tab"))
        {
            var val = tab.Attribute(W + "val")?.Value;
            if (val == "clear")
                continue;
            var alignment = val switch
            {
                "center" => TabStopAlignment.Center,
                "right" or "end" => TabStopAlignment.Right,
                "decimal" => TabStopAlignment.Decimal,
                _ => TabStopAlignment.Left
            };
            var leader = tab.Attribute(W + "leader")?.Value switch
            {
                "dot" => TabLeader.Dots,
                "hyphen" => TabLeader.Dashes,
                "underscore" => TabLeader.Underline,
                _ => TabLeader.None
            };
            stops.Add(new TabStop(DxaToPoints(tab.Attribute(W + "pos")?.Value), alignment, leader));
        }
        return stops;
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

        // A bottom-only rule: the only drawn edge is w:bottom (top/left/right absent or off). This is how
        // CreateHorizontalRule writes itself; recovering the flag keeps the round-trip lossless.
        bool Drawn(string name) =>
            (pBdr.Element(W + name)?.Attribute(W + "val")?.Value ?? "none") is not ("none" or "nil");
        var bottomOnly = Drawn("bottom") && !Drawn("top") && !Drawn("left") && !Drawn("right");

        return new ParagraphBorder(
            color is null or "auto" ? "#000000" : "#" + color.TrimStart('#'),
            width > 0 ? width : 0.5,
            bottomOnly);
    }

    /// <summary>Reads a page border (w:pgBorders) into a <see cref="PageBorder"/>, or null if absent/off.</summary>
    private static PageBorder? ReadPageBorder(XElement? pgBorders)
    {
        if (pgBorders is null)
            return null;
        // Take the first drawn edge (val not none/nil) for colour/width — page borders are a uniform box.
        var edge = pgBorders.Elements().FirstOrDefault(e =>
            (e.Attribute(W + "val")?.Value ?? "single") is not ("none" or "nil"));
        if (edge is null)
            return null;

        var color = edge.Attribute(W + "color")?.Value;
        var width = EighthPointsToPoints(edge.Attribute(W + "sz")?.Value);

        return new PageBorder(
            color is null or "auto" ? "#000000" : "#" + color.TrimStart('#'),
            width > 0 ? width : 1.0);
    }

    /// <summary>
    /// Reads line numbering (w:lnNumType) into <paramref name="page"/>. Absent leaves the default
    /// (<see cref="LineNumberMode.None"/>). @w:restart="newPage" maps to RestartEachPage; anything else
    /// (including the default "continuous") maps to Continuous. @w:countBy sets the interval (min 1).
    /// </summary>
    private static void ReadLineNumbering(XElement? lnNumType, PageSettings page)
    {
        if (lnNumType is null)
            return;

        page.LineNumberMode = lnNumType.Attribute(W + "restart")?.Value == "newPage"
            ? LineNumberMode.RestartEachPage
            : LineNumberMode.Continuous;

        if (int.TryParse(lnNumType.Attribute(W + "countBy")?.Value, out var countBy) && countBy >= 1)
            page.LineNumberCountBy = countBy;
    }

    /// <summary>
    /// Maps a w:vAlign/@w:val token back to a <see cref="PageVerticalAlignment"/> ("both"→Justified).
    /// A null/unknown token (including the absent default and "top") maps to
    /// <see cref="PageVerticalAlignment.Top"/>.
    /// </summary>
    private static PageVerticalAlignment VerticalAlignmentFromToken(string? token) => token switch
    {
        "center" => PageVerticalAlignment.Center,
        "both" => PageVerticalAlignment.Justified,
        "bottom" => PageVerticalAlignment.Bottom,
        _ => PageVerticalAlignment.Top
    };

    /// <summary>
    /// Maps each w:num id in word/numbering.xml to a <see cref="ListKind"/> by following its
    /// abstractNumId to the abstract definition. A level-0 w:numFmt of "bullet" -> Bullet; an outline
    /// definition (w:multiLevelType="multilevel", or whose level-1 lvlText accumulates ancestor
    /// counters like "%1.%2.") -> MultiLevel; anything else (decimal) -> Number.
    /// </summary>
    private static Dictionary<int, ListKind> ReadNumbering(ZipArchive archive)
    {
        var map = new Dictionary<int, ListKind>();
        var numberingXml = LoadPart(archive, "word/numbering.xml");
        var root = numberingXml?.Root;
        if (root is null)
            return map;

        // abstractNumId -> ListKind, taken from the format of its lowest level.
        var abstractKinds = new Dictionary<int, ListKind>();
        foreach (var abstractNum in root.Elements(W + "abstractNum"))
        {
            var abstractNumId = ParseInt(abstractNum.Attribute(W + "abstractNumId")?.Value);
            var levels = abstractNum.Elements(W + "lvl")
                .OrderBy(l => ParseInt(l.Attribute(W + "ilvl")?.Value))
                .ToList();
            var numFmt = levels.FirstOrDefault()?.Element(W + "numFmt")?.Attribute(W + "val")?.Value;
            abstractKinds[abstractNumId] = numFmt == "bullet"
                ? ListKind.Bullet
                : IsMultiLevel(abstractNum, levels) ? ListKind.MultiLevel : ListKind.Number;
        }

        foreach (var num in root.Elements(W + "num"))
        {
            var numId = ParseInt(num.Attribute(W + "numId")?.Value);
            var abstractNumId = ParseInt(num.Element(W + "abstractNumId")?.Attribute(W + "val")?.Value);
            if (abstractKinds.TryGetValue(abstractNumId, out var kind))
                map[numId] = kind;
        }
        return map;
    }

    /// <summary>
    /// Recognizes an outline/legal numbering definition: either it carries
    /// w:multiLevelType="multilevel", or its level-1 lvlText accumulates the ancestor counters (it
    /// references both %1 and %2, as in "%1.%2."), which distinguishes it from a flat decimal list
    /// whose level-1 text is just "%2.".
    /// </summary>
    private static bool IsMultiLevel(XElement abstractNum, IReadOnlyList<XElement> levels)
    {
        if (abstractNum.Attribute(W + "multiLevelType")?.Value == "multilevel")
            return true;

        var level1Text = levels.ElementAtOrDefault(1)?.Element(W + "lvlText")?.Attribute(W + "val")?.Value;
        return level1Text is not null && level1Text.Contains("%1") && level1Text.Contains("%2");
    }

    internal static RunFormatting ReadRunFormatting(XElement? rPr)
    {
        if (rPr is null)
            return RunFormatting.Default;

        var underline = rPr.Element(W + "u");
        var color = rPr.Element(W + "color")?.Attribute(W + "val")?.Value;
        var highlight = rPr.Element(W + "shd")?.Attribute(W + "fill")?.Value;
        var vertAlign = rPr.Element(W + "vertAlign")?.Attribute(W + "val")?.Value;

        return new RunFormatting
        {
            Bold = ReadToggle(rPr, "b"),
            Italic = ReadToggle(rPr, "i"),
            Underline = underline is not null && (underline.Attribute(W + "val")?.Value ?? "single") != "none",
            Strikethrough = ReadToggle(rPr, "strike"),
            SmallCaps = ReadToggle(rPr, "smallCaps"),
            AllCaps = ReadToggle(rPr, "caps"),
            FontFamily = rPr.Element(W + "rFonts")?.Attribute(W + "ascii")?.Value,
            FontSizePt = HalfPointsToPoints(rPr.Element(W + "sz")?.Attribute(W + "val")?.Value),
            ColorHex = color is null or "auto" ? null : "#" + color.TrimStart('#'),
            HighlightColorHex = highlight is null or "auto" ? null : "#" + highlight.TrimStart('#'),
            VerticalAlign = vertAlign switch
            {
                "superscript" => VerticalAlign.Superscript,
                "subscript" => VerticalAlign.Subscript,
                _ => VerticalAlign.Baseline
            }
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

    /// <summary>
    /// Reads the FreeW page watermark from docProps/custom.xml into <see cref="PageSettings.Watermark"/>,
    /// mirroring how the writer persists it as a named custom property. A missing part is fine.
    /// </summary>
    private static void ReadCustomProperties(ZipArchive archive, TextDocument document)
    {
        var customXml = LoadPart(archive, "docProps/custom.xml");
        var root = customXml?.Root;
        if (root is null)
            return;

        var property = root.Elements(CustomProps + "property")
            .FirstOrDefault(p => p.Attribute("name")?.Value == WatermarkPropertyName);
        var text = property?.Element(VtVariant + "lpwstr")?.Value;
        if (!string.IsNullOrEmpty(text))
            document.Page.Watermark = text;
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
