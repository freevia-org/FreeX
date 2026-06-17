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

        ReadHeaderFooter(documentXml, archive, document, hyperlinkRelationships);
        ReadFootnotes(archive, document, imageRelationships, hyperlinkRelationships);
        ReadEndnotes(archive, document, imageRelationships, hyperlinkRelationships);
        ReadComments(archive, document, imageRelationships, hyperlinkRelationships);
        ReadSettings(archive, document);
        ReadTheme(archive, document);
        ReadEmbeddedFonts(archive, document);
        ReadPreservedParts(archive, document);

        return document;
    }

    /// <summary>
    /// Resolves and parses word/theme/theme1.xml (via the document's "/theme" relationship, falling back
    /// to the conventional path), recovering the a:clrScheme colours and the a:fontScheme major/minor
    /// fonts, then inferring the closest <see cref="DocumentTheme"/> preset (see
    /// <see cref="DocumentTheme.InferPreset"/>). A missing or unparseable theme part leaves the document
    /// at <see cref="DocumentTheme.Default"/> ("Office"). Inference is best-effort: a theme whose accent
    /// colours / fonts match no FreeW preset falls back to "Office".
    /// </summary>
    private static void ReadTheme(ZipArchive archive, TextDocument document)
    {
        var themeXml = LoadPart(archive, ResolveThemePartPath(archive) ?? "word/theme/theme1.xml");
        var elements = themeXml?.Root?.Element(A + "themeElements");
        if (elements is null)
            return;

        var clr = elements.Element(A + "clrScheme");
        var fonts = elements.Element(A + "fontScheme");
        if (clr is null || fonts is null)
            return;

        // Each clrScheme slot wraps a single colour element; recover its RRGGBB (srgbClr/@val) or, for a
        // sysClr (e.g. windowText/window), its lastClr fallback. Anything else is treated as absent.
        string Slot(string name)
        {
            var slot = clr.Element(A + name);
            var srgb = slot?.Element(A + "srgbClr")?.Attribute("val")?.Value;
            if (!string.IsNullOrEmpty(srgb))
                return srgb.ToUpperInvariant();
            var sys = slot?.Element(A + "sysClr")?.Attribute("lastClr")?.Value;
            return string.IsNullOrEmpty(sys) ? string.Empty : sys.ToUpperInvariant();
        }

        var scheme = new ThemeColorScheme(
            Slot("dk1"), Slot("lt1"), Slot("dk2"), Slot("lt2"),
            Slot("accent1"), Slot("accent2"), Slot("accent3"),
            Slot("accent4"), Slot("accent5"), Slot("accent6"),
            Slot("hlink"), Slot("folHlink"));

        string LatinFont(string fontElement) =>
            fonts.Element(A + fontElement)?.Element(A + "latin")?.Attribute("typeface")?.Value ?? string.Empty;

        document.Theme = DocumentTheme.InferPreset(scheme, LatinFont("majorFont"), LatinFont("minorFont"));
    }

    /// <summary>
    /// Finds the theme part path from the document relationships (the rel whose Type ends with "/theme"),
    /// resolved relative to the word/ folder. Returns null when no such relationship exists.
    /// </summary>
    private static string? ResolveThemePartPath(ZipArchive archive)
    {
        var relsXml = LoadPart(archive, "word/_rels/document.xml.rels");
        var relationships = relsXml?.Root?.Elements(Rel + "Relationship");
        if (relationships is null)
            return null;

        foreach (var rel in relationships)
        {
            var type = rel.Attribute("Type")?.Value;
            if (type is null || !type.EndsWith("/theme", StringComparison.Ordinal))
                continue;
            var target = rel.Attribute("Target")?.Value;
            if (!string.IsNullOrEmpty(target))
                return "word/" + target.TrimStart('/');
        }
        return null;
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

        // Preserve the ORIGINAL settings element so the writer can overlay FreeW's modelled toggles onto it
        // (rather than emitting a fresh minimal part), keeping unmodelled settings — compat flags, default tab
        // stop, rsid table, proofing, … — across the round-trip. Cloned so later in-place edits can't leak back.
        document.Preserved.OriginalSettings = new XElement(root);

        // Automatic hyphenation (w:autoHyphenation) is an on/off toggle: present + not explicitly off.
        document.Page.AutoHyphenation = ReadToggle(root, "autoHyphenation");

        // Different odd/even page headers/footers (w:evenAndOddHeaders): an on/off toggle. When set, the
        // even header/footer references in w:sectPr are honoured (see ReadHeaderFooter).
        document.Page.DifferentOddEvenPages = ReadToggle(root, "evenAndOddHeaders");

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
    /// Captures the package parts FreeW does not model but preserves verbatim (preserve-and-re-emit):
    /// <c>word/webSettings.xml</c> and every <c>customXml/*</c> part (each item, its props, and the item's own
    /// <c>_rels</c>). Each captured part records its raw bytes plus — when it has them — its
    /// <c>[Content_Types].xml</c> Override and the document→part relationship type, so the writer can re-emit
    /// the part, its content type and its relationship unchanged. A document with none of these parts (authored
    /// from scratch) captures nothing, so it round-trips byte-equivalently to before.
    /// </summary>
    private static void ReadPreservedParts(ZipArchive archive, TextDocument document)
    {
        // Map each part name → its content-type Override (so a re-emitted part keeps its declared type), and
        // each document-relationship Target → its Type (so a re-emitted part keeps its document relationship).
        var overrides = ReadContentTypeOverrides(archive);
        var docRelTypesByTarget = ReadDocumentRelationshipTypesByTarget(archive);

        void Capture(string partName, string? relationshipType)
        {
            var entryPath = partName.TrimStart('/');
            var bytes = LoadMedia(archive, entryPath);
            if (bytes is null)
                return;
            overrides.TryGetValue(partName, out var contentType);
            document.Preserved.Parts.Add(new PreservedPart(partName, bytes, contentType, relationshipType));
        }

        // word/webSettings.xml: one optional part referenced from document.xml.rels (webSettings rel type).
        if (archive.GetEntry("word/webSettings.xml") is not null)
            Capture("/word/webSettings.xml",
                docRelTypesByTarget.GetValueOrDefault("webSettings.xml") ?? WebSettingsRelType);

        // customXml/* — items, their props and the items' own _rels. The document→item relationships live in
        // document.xml.rels with a customXml-relative Target (e.g. "../customXml/item1.xml"); item→props
        // relationships live in each item's own _rels (not document.xml.rels), so those parts carry no document
        // relationship. We walk the package entries so any number of items (and their satellite parts) survive.
        foreach (var entry in archive.Entries)
        {
            var name = entry.FullName;
            if (!name.StartsWith("customXml/", StringComparison.Ordinal) || name.EndsWith("/", StringComparison.Ordinal))
                continue;
            var partName = "/" + name;
            // An item part (customXml/itemN.xml, not under _rels) is the one document.xml.rels points at; its
            // props and _rels are pulled in by the same walk but carry no document relationship.
            var isItem = !name.StartsWith("customXml/_rels/", StringComparison.Ordinal)
                && name.StartsWith("customXml/item", StringComparison.Ordinal)
                && !name.Contains("itemProps", StringComparison.Ordinal);
            var relationshipType = isItem
                ? docRelTypesByTarget.GetValueOrDefault("../" + name) ?? CustomXmlRelType
                : null;
            Capture(partName, relationshipType);
        }
    }

    /// <summary>
    /// Reads <c>[Content_Types].xml</c>, mapping each Override's PartName → ContentType. Returns an empty map
    /// when the part is absent. Used to re-emit a preserved part's content-type Override unchanged.
    /// </summary>
    private static Dictionary<string, string> ReadContentTypeOverrides(ZipArchive archive)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        var ctXml = LoadPart(archive, "[Content_Types].xml");
        var overrides = ctXml?.Root?.Elements(Ct + "Override");
        if (overrides is null)
            return map;
        foreach (var ov in overrides)
        {
            var partName = ov.Attribute("PartName")?.Value;
            var contentType = ov.Attribute("ContentType")?.Value;
            if (!string.IsNullOrEmpty(partName) && !string.IsNullOrEmpty(contentType))
                map[partName] = contentType;
        }
        return map;
    }

    /// <summary>
    /// Reads <c>word/_rels/document.xml.rels</c>, mapping each relationship Target → its Type. Targets are kept
    /// exactly as written (e.g. "webSettings.xml", "../customXml/item1.xml") so a preserved part can recover the
    /// relationship type the document used to reference it. Returns an empty map when the rels part is absent.
    /// </summary>
    private static Dictionary<string, string> ReadDocumentRelationshipTypesByTarget(ZipArchive archive)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        var relsXml = LoadPart(archive, "word/_rels/document.xml.rels");
        var relationships = relsXml?.Root?.Elements(Rel + "Relationship");
        if (relationships is null)
            return map;
        foreach (var rel in relationships)
        {
            var target = rel.Attribute("Target")?.Value;
            var type = rel.Attribute("Type")?.Value;
            if (!string.IsNullOrEmpty(target) && !string.IsNullOrEmpty(type))
                map[target] = type;
        }
        return map;
    }

    /// <summary>
    /// Resolves word/fontTable.xml (via the document's "/fontTable" relationship, falling back to the
    /// conventional path), parses each w:font and de-obfuscates its embedded .odttf parts back into the
    /// original font bytes (the ODTTF XOR is self-inverse, keyed by each w:embed*'s w:fontKey GUID). Each
    /// recovered family is added to <see cref="TextDocument.EmbeddedFonts"/>. A missing fontTable — or a
    /// font with no recoverable styles — leaves the list empty, so a document without embedded fonts is
    /// unaffected.
    /// </summary>
    private static void ReadEmbeddedFonts(ZipArchive archive, TextDocument document)
    {
        var fontTableXml = LoadPart(archive, ResolveDocumentRelPartPath(archive, "/fontTable") ?? "word/fontTable.xml");
        var root = fontTableXml?.Root;
        if (root is null)
            return;

        // The fontTable's own relationships map each w:embed*'s r:id to its .odttf part (under word/).
        var fontRels = ReadFontTableRelationships(archive);

        byte[]? Recover(XElement? embed)
        {
            if (embed is null)
                return null;
            var id = embed.Attribute(R + "id")?.Value;
            var fontKey = embed.Attribute(W + "fontKey")?.Value;
            if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(fontKey) || !fontRels.TryGetValue(id, out var path))
                return null;
            var obfuscated = LoadMedia(archive, path);
            // De-obfuscation is the same XOR transform applied on write (it is its own inverse).
            return obfuscated is null ? null : ObfuscateFont(obfuscated, fontKey);
        }

        foreach (var font in root.Elements(W + "font"))
        {
            var family = font.Attribute(W + "name")?.Value;
            if (string.IsNullOrEmpty(family))
                continue;
            var recovered = new EmbeddedFont(
                family,
                Recover(font.Element(W + "embedRegular")),
                Recover(font.Element(W + "embedBold")),
                Recover(font.Element(W + "embedItalic")),
                Recover(font.Element(W + "embedBoldItalic")));
            if (recovered.HasAnyStyle)
                document.EmbeddedFonts.Add(recovered);
        }
    }

    /// <summary>
    /// Reads word/_rels/fontTable.xml.rels mapping each font relationship id to its part path (under
    /// word/). Returns an empty map when the rels part is absent.
    /// </summary>
    private static Dictionary<string, string> ReadFontTableRelationships(ZipArchive archive)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        var relsXml = LoadPart(archive, "word/_rels/fontTable.xml.rels");
        var relationships = relsXml?.Root?.Elements(Rel + "Relationship");
        if (relationships is null)
            return map;
        foreach (var rel in relationships)
        {
            var id = rel.Attribute("Id")?.Value;
            var target = rel.Attribute("Target")?.Value;
            if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(target))
                map[id] = "word/" + target.TrimStart('/');
        }
        return map;
    }

    /// <summary>
    /// Finds a document-relationship target by the suffix of its Type (e.g. "/fontTable"), resolved
    /// relative to the word/ folder. Returns null when no such relationship exists. Generalises the
    /// settings/theme resolvers.
    /// </summary>
    private static string? ResolveDocumentRelPartPath(ZipArchive archive, string typeSuffix)
    {
        var relsXml = LoadPart(archive, "word/_rels/document.xml.rels");
        var relationships = relsXml?.Root?.Elements(Rel + "Relationship");
        if (relationships is null)
            return null;
        foreach (var rel in relationships)
        {
            var type = rel.Attribute("Type")?.Value;
            if (type is null || !type.EndsWith(typeSuffix, StringComparison.Ordinal))
                continue;
            var target = rel.Attribute("Target")?.Value;
            if (!string.IsNullOrEmpty(target))
                return "word/" + target.TrimStart('/');
        }
        return null;
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
    /// Resolves the body-level (final section) header/footer references in w:sectPr (r:id → document rels →
    /// part path), loads those parts (w:hdr / w:ftr) and reconstructs the final section's
    /// <see cref="TextDocument.Header"/> / Footer / EvenHeader / EvenFooter / FirstHeader / FirstFooter. The
    /// non-final sections' header/footer references are read separately into each section's storage (see
    /// <see cref="ReadSectionBreak"/>), so every section keeps its own page-specific headers/footers.
    /// </summary>
    private static void ReadHeaderFooter(
        XDocument documentXml,
        ZipArchive archive,
        TextDocument document,
        IReadOnlyDictionary<string, string> hyperlinkRelationships)
    {
        var sectPr = documentXml.Root?.Element(W + "body")?.Element(W + "sectPr");
        if (sectPr is null)
            return;

        // The body-level w:sectPr is the final/only section: recover its page geometry + layout into
        // document.Page. The same parse feeds non-final sections (see ReadSectionBreak in ReadParagraph).
        ReadPageSettings(sectPr, document.Page);

        // Page background colour (w:document/w:background/@w:color): a body-level (document-wide) setting,
        // null when absent. Restored with a '#' prefix to mirror the model's hex convention.
        var backgroundColor = documentXml.Root?.Element(W + "background")?.Attribute(W + "color")?.Value;
        document.Page.BackgroundColorHex = backgroundColor is { Length: > 0 } ? "#" + backgroundColor : null;

        ReadSectionHeadersFooters(
            sectPr, document.FinalSectionHeadersFooters, archive, hyperlinkRelationships);
    }

    /// <summary>
    /// Resolves every header/footer reference (default/even/first) in one <paramref name="sectPr"/> and loads
    /// the referenced parts into <paramref name="hf"/>. Shared by the body-level final section
    /// (<see cref="ReadHeaderFooter"/>) and each non-final section break (<see cref="ReadSectionBreak"/>) so
    /// all sections recover their own headers/footers from one code path. Each part's inline-image r:embed ids
    /// resolve against THAT part's own _rels, so images inside headers/footers survive.
    /// </summary>
    private static void ReadSectionHeadersFooters(
        XElement sectPr,
        SectionHeadersFooters hf,
        ZipArchive archive,
        IReadOnlyDictionary<string, string> hyperlinkRelationships)
    {
        var partsById = ReadHeaderFooterRelationships(archive);

        hf.Header = ReadHeaderFooterPart(
            sectPr, "headerReference", "default", W + "hdr", partsById, archive, hyperlinkRelationships);
        hf.Footer = ReadHeaderFooterPart(
            sectPr, "footerReference", "default", W + "ftr", partsById, archive, hyperlinkRelationships);
        // Even-page header/footer (w:type="even") for "different odd/even pages". Present only when the
        // section carried the even references + parts; null otherwise so single-header sections are unaffected.
        hf.EvenHeader = ReadHeaderFooterPart(
            sectPr, "headerReference", "even", W + "hdr", partsById, archive, hyperlinkRelationships);
        hf.EvenFooter = ReadHeaderFooterPart(
            sectPr, "footerReference", "even", W + "ftr", partsById, archive, hyperlinkRelationships);
        // First-page header/footer (w:type="first") for "different first page". Present only when the section
        // carried the first references + parts.
        hf.FirstHeader = ReadHeaderFooterPart(
            sectPr, "headerReference", "first", W + "hdr", partsById, archive, hyperlinkRelationships);
        hf.FirstFooter = ReadHeaderFooterPart(
            sectPr, "footerReference", "first", W + "ftr", partsById, archive, hyperlinkRelationships);
    }

    /// <summary>
    /// Reads one w:sectPr's page geometry + layout into <paramref name="page"/>. Shared by the body-level
    /// final section (<see cref="ReadHeaderFooter"/>) and each non-final paragraph-level section break
    /// (<see cref="ReadSectionBreak"/>), so all per-section properties are parsed in one place. Recovers
    /// page size + orientation (w:pgSz), margins (w:pgMar), columns (w:cols), page borders (w:pgBorders),
    /// line numbering (w:lnNumType), vertical alignment (w:vAlign) and the different-first-page toggle
    /// (w:titlePg). Each property is only applied when present, so absent properties keep the defaults.
    /// </summary>
    private static void ReadPageSettings(XElement sectPr, PageSettings page)
    {
        // Page size + orientation (w:pgSz). w:orient="landscape" sets the flag; width/height carry the
        // already-oriented dimensions Word writes.
        var pgSz = sectPr.Element(W + "pgSz");
        if (pgSz is not null)
        {
            if (pgSz.Attribute(W + "w") is { } w)
                page.WidthPt = DxaToPoints(w.Value);
            if (pgSz.Attribute(W + "h") is { } h)
                page.HeightPt = DxaToPoints(h.Value);
            page.Landscape = pgSz.Attribute(W + "orient")?.Value == "landscape";
        }

        // Page margins (w:pgMar).
        var pgMar = sectPr.Element(W + "pgMar");
        if (pgMar is not null)
        {
            if (pgMar.Attribute(W + "left") is { } left)
                page.MarginLeftPt = DxaToPoints(left.Value);
            if (pgMar.Attribute(W + "right") is { } right)
                page.MarginRightPt = DxaToPoints(right.Value);
            if (pgMar.Attribute(W + "top") is { } top)
                page.MarginTopPt = DxaToPoints(top.Value);
            if (pgMar.Attribute(W + "bottom") is { } bottom)
                page.MarginBottomPt = DxaToPoints(bottom.Value);
        }

        // Equal-width column layout (w:cols/@w:num + @w:space).
        var cols = sectPr.Element(W + "cols");
        if (cols is not null)
        {
            if (int.TryParse(cols.Attribute(W + "num")?.Value, out var num) && num >= 1)
                page.ColumnCount = num;
            if (cols.Attribute(W + "space") is { } space)
                page.ColumnSpacingPt = DxaToPoints(space.Value);
        }

        // Page border (w:pgBorders) → PageSettings.PageBorder (null when absent/off).
        page.PageBorder = ReadPageBorder(sectPr.Element(W + "pgBorders"));

        // Line numbering (w:lnNumType): recover the mode + interval.
        ReadLineNumbering(sectPr.Element(W + "lnNumType"), page);

        // Page vertical alignment (w:vAlign): map the val token back ("both"→Justified); absent → Top.
        page.VerticalAlignment =
            VerticalAlignmentFromToken(sectPr.Element(W + "vAlign")?.Attribute(W + "val")?.Value);

        // "Different first page" (w:titlePg): a bare toggle; absent → false.
        page.DifferentFirstPage = ReadToggle(sectPr, "titlePg");
    }

    /// <summary>
    /// Reads a non-final section break from a paragraph's w:pPr/w:sectPr into a <see cref="Section"/>:
    /// the section's page settings (via <see cref="ReadPageSettings"/>), its break kind (w:type) and its own
    /// header/footer references (default/even/first, via <see cref="ReadSectionHeadersFooters"/>), or null
    /// when the paragraph carries no section break. The body-level final section is read separately into
    /// <see cref="TextDocument.Page"/> + <see cref="TextDocument.FinalSectionHeadersFooters"/> (see
    /// <see cref="ReadHeaderFooter"/>).
    /// </summary>
    private static Section? ReadSectionBreak(
        XElement? pPr,
        ZipArchive archive,
        IReadOnlyDictionary<string, string> hyperlinkRelationships)
    {
        var sectPr = pPr?.Element(W + "sectPr");
        if (sectPr is null)
            return null;

        var page = new PageSettings();
        ReadPageSettings(sectPr, page);
        var breakKind = SectionBreakFromToken(sectPr.Element(W + "type")?.Attribute(W + "val")?.Value);
        var section = new Section(page, breakKind);
        // Each non-final section references its own header/footer parts; recover them into the section.
        ReadSectionHeadersFooters(sectPr, section.HeadersFooters, archive, hyperlinkRelationships);
        return section;
    }

    /// <summary>
    /// Maps a w:sectPr/w:type/@w:val token to a <see cref="SectionBreakKind"/>. A null/unknown token
    /// (including the absent default) maps to <see cref="SectionBreakKind.NextPage"/>, Word's default.
    /// </summary>
    private static SectionBreakKind SectionBreakFromToken(string? token) => token switch
    {
        "continuous" => SectionBreakKind.Continuous,
        "evenPage" => SectionBreakKind.EvenPage,
        "oddPage" => SectionBreakKind.OddPage,
        _ => SectionBreakKind.NextPage
    };

    private static HeaderFooter? ReadHeaderFooterPart(
        XElement sectPr,
        string referenceName,
        string referenceType,
        XName rootName,
        IReadOnlyDictionary<string, string> partsById,
        ZipArchive archive,
        IReadOnlyDictionary<string, string> hyperlinkRelationships)
    {
        // Select the reference of the requested type ("default"/"even"/"first"). For the default type a
        // type-less reference also counts (Word treats an absent w:type as "default"); the "even"/"first"
        // types must match explicitly so a default-only document does not pick up the default header as its
        // even/first header.
        var references = sectPr.Elements(W + referenceName).ToList();
        if (references.Count == 0)
            return null;
        var reference = referenceType == "default"
            ? references.FirstOrDefault(r => (r.Attribute(W + "type")?.Value ?? "default") == "default")
            : references.FirstOrDefault(r => r.Attribute(W + "type")?.Value == referenceType);
        if (reference is null)
            return null;

        var id = reference.Attribute(R + "id")?.Value;
        if (id is null || !partsById.TryGetValue(id, out var partPath))
            return null;

        var partXml = LoadPart(archive, partPath);
        var root = partXml?.Root;
        if (root is null || root.Name != rootName)
            return null;

        // Images inside a header/footer resolve their r:embed against the PART's own _rels (e.g.
        // word/_rels/header3.xml.rels), not document.xml.rels — so build a part-local image-relationship map.
        var partImageRelationships = ReadPartImageRelationships(archive, partPath);

        var result = new HeaderFooter();
        // Header/footer paragraphs carry no list numbering context (numbering.xml targets the body).
        var noNumbering = new Dictionary<int, ListKind>();
        // Use Descendants, not direct children: real Word headers/footers routinely wrap their content in a
        // w:tbl (and/or w:sdt content controls), so the visible text lives in paragraphs NESTED inside table
        // cells / SDTs rather than as direct children of w:hdr/w:ftr. Reading only direct-child w:p (as before)
        // recovered just the trailing empty paragraph, making the part IsEmpty so the writer dropped it — the
        // "headers dropped on round-trip" bug. Paragraphs never nest inside paragraphs in OOXML, so Descendants
        // yields each content paragraph exactly once, in document order; we flatten any table/SDT structure to
        // the model's paragraph list (the model carries no per-header table) but preserve all text + runs.
        foreach (var p in root.Descendants(W + "p"))
            result.Paragraphs.Add(ReadParagraph(p, archive, partImageRelationships, hyperlinkRelationships, noNumbering));
        return result;
    }

    /// <summary>
    /// Reads the image relationships of an arbitrary part (e.g. a header/footer part) from its own
    /// <c>&lt;dir&gt;/_rels/&lt;file&gt;.rels</c>, mapping each image relationship id → media part path
    /// (resolved relative to the part's directory). Returns an empty map when the part has no rels file (the
    /// common case — an image-less header), so image-less headers/footers cost nothing extra.
    /// </summary>
    private static Dictionary<string, string> ReadPartImageRelationships(ZipArchive archive, string partPath)
    {
        var map = new Dictionary<string, string>();
        var lastSlash = partPath.LastIndexOf('/');
        var dir = lastSlash >= 0 ? partPath[..lastSlash] : string.Empty;
        var file = lastSlash >= 0 ? partPath[(lastSlash + 1)..] : partPath;
        var relsPath = (dir.Length > 0 ? dir + "/" : string.Empty) + "_rels/" + file + ".rels";

        var relsXml = LoadPart(archive, relsPath);
        var relationships = relsXml?.Root?.Elements(Rel + "Relationship");
        if (relationships is null)
            return map;

        foreach (var rel in relationships)
        {
            var type = rel.Attribute("Type")?.Value;
            if (type is null || !type.EndsWith("/image", StringComparison.Ordinal))
                continue;
            var id = rel.Attribute("Id")?.Value;
            var target = rel.Attribute("Target")?.Value;
            if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(target))
                continue;
            // Targets are relative to the part's directory (e.g. "media/header3_image1.png" under word/).
            map[id] = (dir.Length > 0 ? dir + "/" : string.Empty) + target.TrimStart('/');
        }
        return map;
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
            // A paragraph carrying a w:pPr/w:sectPr ends a non-final section; recover that section's page
            // setup + break kind + own header/footer references onto the paragraph (the body-level final
            // section is read elsewhere).
            paragraph.SectionBreak = ReadSectionBreak(pPr, archive, hyperlinkRelationships);
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
            else if (child.Name == M + "oMath")
            {
                // An inline equation: parse the OMML m:oMath into an Equation carried by a run.
                paragraph.Runs.Add(Run.FromEquation(ReadOMath(child)));
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
    /// Parses an inline OMML equation (m:oMath) into an <see cref="Equation"/>. Recognises m:r (plain
    /// text), m:sSup (superscript) and m:f (fraction); any other top-level child degrades to the plain
    /// text of its descendant m:t runs so nothing is lost or throws. Mirrors how the writer emits these
    /// (see <c>DocxWriter.BuildOMath</c>).
    /// </summary>
    private static Equation ReadOMath(XElement oMath)
    {
        var equation = new Equation();
        foreach (var child in oMath.Elements())
        {
            if (child.Name == M + "r")
                equation.Runs.Add(MathRun.PlainText(MathTextOf(child)));
            else if (child.Name == M + "sSup")
                equation.Runs.Add(MathRun.Superscript(
                    MathTextOf(child.Element(M + "e")),
                    MathTextOf(child.Element(M + "sup"))));
            else if (child.Name == M + "f")
                equation.Runs.Add(MathRun.Fraction(
                    MathTextOf(child.Element(M + "num")),
                    MathTextOf(child.Element(M + "den"))));
            else
            {
                // Unknown OMML construct: keep its text so the equation degrades rather than disappears.
                var fallback = MathTextOf(child);
                if (fallback.Length > 0)
                    equation.Runs.Add(MathRun.PlainText(fallback));
            }
        }
        return equation;
    }

    /// <summary>The concatenated text of all descendant m:t runs under <paramref name="element"/> (empty if null).</summary>
    private static string MathTextOf(XElement? element) =>
        element is null ? string.Empty : string.Concat(element.Descendants(M + "t").Select(t => t.Value));

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

        // A w:drawing wrapping a wps:wsp text box whose run a:rPr carries DrawingML text effects is WordArt.
        // Checked BEFORE ReadShape because a WordArt text box is also a wps:wsp (so the shape reader would
        // otherwise claim it); the text effects on the run's a:rPr are what distinguish the two.
        var wordArt = ReadWordArt(r);
        if (wordArt is not null)
        {
            var wordArtRun = Run.FromWordArt(wordArt);
            wordArtRun.HyperlinkUrl = hyperlinkUrl;
            wordArtRun.HyperlinkAnchor = hyperlinkAnchor;
            wordArtRun.HyperlinkTooltip = hyperlinkTooltip;
            wordArtRun.CommentId = commentId;
            ApplyRevision(wordArtRun);
            paragraph.Runs.Add(wordArtRun);
            return;
        }

        // A w:drawing wrapping a wps:wsp (not a pic:pic) is an inline shape / text box.
        var shape = ReadShape(r, archive, imageRelationships);
        if (shape is not null)
        {
            var shapeRun = Run.FromShape(shape);
            shapeRun.HyperlinkUrl = hyperlinkUrl;
            shapeRun.HyperlinkAnchor = hyperlinkAnchor;
            shapeRun.HyperlinkTooltip = hyperlinkTooltip;
            shapeRun.CommentId = commentId;
            ApplyRevision(shapeRun);
            paragraph.Runs.Add(shapeRun);
            return;
        }

        // A run whose w:drawing references a chart part (a:graphicData/c:chart) becomes a chart run.
        // imageRelationships maps EVERY document relationship id → part path (it is not filtered to
        // images), so the chart part resolves through it just like a media part.
        var chart = ReadChart(r, archive, imageRelationships);
        if (chart is not null)
        {
            var chartRun = new Run(string.Empty) { Chart = chart, HyperlinkUrl = hyperlinkUrl, HyperlinkAnchor = hyperlinkAnchor, HyperlinkTooltip = hyperlinkTooltip, CommentId = commentId };
            ApplyRevision(chartRun);
            paragraph.Runs.Add(chartRun);
            return;
        }

        // A run wrapping a w:object (VML v:shape + o:OLEObject) is an embedded OLE object. imageRelationships
        // is the all-parts map (id → part path), so both the .bin payload and the icon media part resolve.
        var embedded = ReadEmbeddedObject(r, archive, imageRelationships);
        if (embedded is not null)
        {
            var embeddedRun = new Run(string.Empty) { EmbeddedObject = embedded, HyperlinkUrl = hyperlinkUrl, HyperlinkAnchor = hyperlinkAnchor, HyperlinkTooltip = hyperlinkTooltip, CommentId = commentId };
            ApplyRevision(embeddedRun);
            paragraph.Runs.Add(embeddedRun);
            return;
        }

        // A run whose w:drawing references a SmartArt diagram (a:graphicData/dgm:relIds) becomes a SmartArt
        // run. imageRelationships maps EVERY document relationship id → part path, so the diagram data part
        // resolves through it via the dgm:relIds/@r:dm id.
        var smartArt = ReadSmartArt(r, archive, imageRelationships);
        if (smartArt is not null)
        {
            var smartArtRun = new Run(string.Empty) { SmartArt = smartArt, HyperlinkUrl = hyperlinkUrl, HyperlinkAnchor = hyperlinkAnchor, HyperlinkTooltip = hyperlinkTooltip, CommentId = commentId };
            ApplyRevision(smartArtRun);
            paragraph.Runs.Add(smartArtRun);
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

    /// <summary>
    /// Reads a picture (w:drawing) from a run into an <see cref="InlineImage"/>, if present. Handles both
    /// the inline form (wp:inline, read back as <see cref="ImageWrapping.Inline"/>) and the floating form
    /// (wp:anchor), recovering the wrapping mode, the position offsets, and the horizontal/vertical anchors.
    /// Returns null when the drawing is not a picture (e.g. a shape or chart) so those paths keep working —
    /// a picture is identified by an a:blip whose r:embed resolves to a media part.
    /// </summary>
    private static InlineImage? ReadImage(XElement run, ZipArchive archive, IReadOnlyDictionary<string, string> imageRelationships)
    {
        var drawing = run.Element(W + "drawing");
        var container = drawing?.Element(Wp + "inline") ?? drawing?.Element(Wp + "anchor");
        if (container is null)
            return null;

        var blip = container.Descendants(A + "blip").FirstOrDefault();
        var relationshipId = blip?.Attribute(R + "embed")?.Value;
        if (relationshipId is null || !imageRelationships.TryGetValue(relationshipId, out var target))
            return null;

        var bytes = LoadMedia(archive, target);
        if (bytes is null)
            return null;

        var extent = container.Element(Wp + "extent");
        var widthPt = EmuToPoints(extent?.Attribute("cx")?.Value);
        var heightPt = EmuToPoints(extent?.Attribute("cy")?.Value);

        // Restore accessibility alt text from wp:docPr/@descr; absent attribute leaves AltText null.
        var descr = container.Element(Wp + "docPr")?.Attribute("descr")?.Value;
        var image = new InlineImage(bytes, widthPt, heightPt)
        {
            AltText = string.IsNullOrEmpty(descr) ? null : descr,
        };

        // A wp:anchor is a floating image: recover wrapping mode, offsets and anchors. A wp:inline reads
        // back as ImageWrapping.Inline with default position fields, exactly as before.
        if (container.Name == Wp + "anchor")
            ApplyFloatingPosition(container, image);

        return image;
    }

    /// <summary>
    /// Recovers a floating image's wrapping mode + position from a wp:anchor: the wrap element selects the
    /// <see cref="ImageWrapping"/> (wp:wrapNone disambiguated by @behindDoc into Behind / InFront), and
    /// wp:positionH/V supply the anchors (@relativeFrom) and offsets (wp:posOffset, EMU → points).
    /// </summary>
    private static void ApplyFloatingPosition(XElement anchor, InlineImage image)
    {
        image.Wrapping = ReadWrapping(anchor);

        var positionH = anchor.Element(Wp + "positionH");
        image.HorizontalAnchor = ReadHorizontalAnchor(positionH?.Attribute("relativeFrom")?.Value);
        image.HorizontalOffsetPt = EmuToPoints(positionH?.Element(Wp + "posOffset")?.Value);

        var positionV = anchor.Element(Wp + "positionV");
        image.VerticalAnchor = ReadVerticalAnchor(positionV?.Attribute("relativeFrom")?.Value);
        image.VerticalOffsetPt = EmuToPoints(positionV?.Element(Wp + "posOffset")?.Value);
    }

    /// <summary>Maps a wp:anchor's wrap element back to an <see cref="ImageWrapping"/> mode.</summary>
    private static ImageWrapping ReadWrapping(XElement anchor)
    {
        if (anchor.Element(Wp + "wrapSquare") is not null)
            return ImageWrapping.Square;
        if (anchor.Element(Wp + "wrapTight") is not null)
            return ImageWrapping.Tight;
        if (anchor.Element(Wp + "wrapTopAndBottom") is not null)
            return ImageWrapping.TopAndBottom;
        // wp:wrapNone (or an unexpected/missing wrap) is a front/behind image, disambiguated by @behindDoc.
        var behindDoc = anchor.Attribute("behindDoc")?.Value;
        return behindDoc is "1" or "true" ? ImageWrapping.Behind : ImageWrapping.InFront;
    }

    /// <summary>Maps a wp:positionH/@relativeFrom token to a <see cref="HorizontalAnchor"/> (default Column).</summary>
    private static HorizontalAnchor ReadHorizontalAnchor(string? relativeFrom) => relativeFrom switch
    {
        "margin" => HorizontalAnchor.Margin,
        "page" => HorizontalAnchor.Page,
        _ => HorizontalAnchor.Column,
    };

    /// <summary>Maps a wp:positionV/@relativeFrom token to a <see cref="VerticalAnchor"/> (default Paragraph).</summary>
    private static VerticalAnchor ReadVerticalAnchor(string? relativeFrom) => relativeFrom switch
    {
        "margin" => VerticalAnchor.Margin,
        "page" => VerticalAnchor.Page,
        _ => VerticalAnchor.Paragraph,
    };

    /// <summary>
    /// Reads inline WordArt from a run, if present: a w:drawing/wp:inline/.../wps:wsp text box whose single
    /// run's a:rPr (== w:rPr, since the same w:r is used) carries DrawingML text effects (a:solidFill,
    /// a:gradFill, a:ln or a:effectLst). Recovers the text, the font size (w:sz in half-points) and infers
    /// the <see cref="WordArtStyle"/> preset from which effect is present. Returns null when the drawing is a
    /// plain shape (no text effects) or not a wsp at all, so the ordinary shape/image paths keep working.
    ///
    /// SIMPLIFICATION: the preset is inferred from the *kind* of effect present (gradient → GradientFill,
    /// outline → Outline, shadow → Shadow, else FillBlue), not from exact colour values — colours are fixed
    /// per preset by the writer, so this is lossless for FreeW-authored WordArt.
    /// </summary>
    private static WordArt? ReadWordArt(XElement run)
    {
        var inline = run.Element(W + "drawing")?.Element(Wp + "inline");
        var wsp = inline?.Descendants(Wps + "wsp").FirstOrDefault();
        var txbxContent = wsp?.Element(Wps + "txbx")?.Element(W + "txbxContent");
        if (txbxContent is null)
            return null;

        // The WordArt run's properties: the first w:r/w:rPr inside the text box. WordArt is identified by a
        // DrawingML text effect sitting directly under that w:rPr.
        var rPr = txbxContent.Descendants(W + "r").FirstOrDefault()?.Element(W + "rPr");
        if (rPr is null || InferWordArtStyle(rPr) is not { } style)
            return null;

        var text = string.Concat(txbxContent.Descendants(W + "t").Select(t => t.Value));
        var fontSizePt = HalfPointsToPoints(rPr.Element(W + "sz")?.Attribute(W + "val")?.Value) ?? 36;

        return new WordArt(text, style, fontSizePt);
    }

    /// <summary>
    /// Infers a <see cref="WordArtStyle"/> from the DrawingML text effects under a WordArt run's w:rPr, or
    /// null when none are present (so the element is a plain shape, not WordArt). Gradient fill → GradientFill,
    /// solid fill + outline → Outline, solid fill + shadow → Shadow, plain solid fill → FillBlue.
    /// </summary>
    private static WordArtStyle? InferWordArtStyle(XElement rPr)
    {
        if (rPr.Element(A + "gradFill") is not null)
            return WordArtStyle.GradientFill;
        if (rPr.Element(A + "ln") is not null)
            return WordArtStyle.Outline;
        if (rPr.Element(A + "effectLst") is not null)
            return WordArtStyle.Shadow;
        if (rPr.Element(A + "solidFill") is not null)
            return WordArtStyle.FillBlue;
        return null;
    }

    /// <summary>
    /// Reads an inline DrawingML shape / text box (w:drawing → wp:inline → a:graphic/a:graphicData → wps:wsp)
    /// from a run into a <see cref="Shape"/>, if present. Recovers the preset geometry kind (a:prstGeom/@prst),
    /// the EMU extent (size in points), the optional solid fill (a:solidFill/a:srgbClr), and any text-box body
    /// paragraphs (wps:txbx/w:txbxContent). Returns null for a non-shape drawing (e.g. a picture) so the image
    /// path keeps working. Mirrors how the writer emits these (see <c>DocxWriter.BuildShapeDrawing</c>).
    /// </summary>
    private static Shape? ReadShape(XElement run, ZipArchive archive, IReadOnlyDictionary<string, string> imageRelationships)
    {
        var inline = run.Element(W + "drawing")?.Element(Wp + "inline");
        var wsp = inline?.Descendants(Wps + "wsp").FirstOrDefault();
        if (wsp is null)
            return null;

        var extent = inline!.Element(Wp + "extent");
        var widthPt = EmuToPoints(extent?.Attribute("cx")?.Value);
        var heightPt = EmuToPoints(extent?.Attribute("cy")?.Value);

        var spPr = wsp.Element(Wps + "spPr");
        var preset = spPr?.Element(A + "prstGeom")?.Attribute("prst")?.Value;
        var hasTextBody = wsp.Element(Wps + "txbx")?.Element(W + "txbxContent") is not null;
        var kind = ShapeKindFromPreset(preset, hasTextBody);

        var shape = new Shape(kind, widthPt, heightPt);

        var fill = spPr?.Element(A + "solidFill")?.Element(A + "srgbClr")?.Attribute("val")?.Value;
        if (!string.IsNullOrEmpty(fill) && !string.Equals(fill, "auto", StringComparison.Ordinal))
            shape.FillColorHex = "#" + fill.TrimStart('#');

        // Text-box body: parse each w:p inside w:txbxContent with the ordinary paragraph reader. Bodies do
        // not carry hyperlink relationships or list numbering, so build them against empty maps (mirrors the
        // writer, which emits txbx paragraphs without those).
        var txbxContent = wsp.Element(Wps + "txbx")?.Element(W + "txbxContent");
        if (txbxContent is not null)
        {
            var noHyperlinks = new Dictionary<string, string>();
            var noNumbering = new Dictionary<int, ListKind>();
            foreach (var p in txbxContent.Elements(W + "p"))
                shape.TextParagraphs.Add(ReadParagraph(p, archive, imageRelationships, noHyperlinks, noNumbering));
        }

        return shape;
    }

    /// <summary>
    /// Maps an a:prstGeom/@prst token back to a <see cref="ShapeKind"/>. "roundRect" → RoundedRectangle,
    /// "ellipse" → Ellipse; a plain "rect" (or unknown) is a TextBox when it has a text body, otherwise a
    /// Rectangle — mirroring the writer, which serialises both Rectangle and TextBox as "rect".
    /// </summary>
    private static ShapeKind ShapeKindFromPreset(string? preset, bool hasTextBody) => preset switch
    {
        "roundRect" => ShapeKind.RoundedRectangle,
        "ellipse" => ShapeKind.Ellipse,
        _ => hasTextBody ? ShapeKind.TextBox : ShapeKind.Rectangle,
    };

    /// <summary>
    /// Reads an embedded OLE object (a w:object wrapping a VML v:shape + o:OLEObject) from a run into an
    /// <see cref="EmbeddedObject"/>, if present. Resolves the o:OLEObject/@r:id to the embedded .bin part via
    /// <paramref name="relationships"/> (the all-parts map) to recover the payload bytes, reads the ProgID
    /// from o:OLEObject/@ProgID, and — when the v:shape carries a v:imagedata — loads the icon media part
    /// into the object's presentation image. Returns null when the run carries no embedded object.
    ///
    /// SIMPLIFICATION (Y2): only embedded objects (Type other than "Link") are recovered; a linked object
    /// (no embedded .bin relationship) yields null. The icon's size becomes the object's size when present.
    /// </summary>
    private static EmbeddedObject? ReadEmbeddedObject(XElement run, ZipArchive archive, IReadOnlyDictionary<string, string> relationships)
    {
        var obj = run.Element(W + "object");
        var ole = obj?.Element(O + "OLEObject");
        if (ole is null)
            return null;

        // A linked object references its data externally (Type="Link") rather than via an embedded part.
        if (string.Equals(ole.Attribute("Type")?.Value, "Link", StringComparison.OrdinalIgnoreCase))
            return null;

        var relationshipId = ole.Attribute(R + "id")?.Value;
        if (relationshipId is null || !relationships.TryGetValue(relationshipId, out var partPath))
            return null;

        var payload = LoadMedia(archive, partPath);
        if (payload is null)
            return null;

        var progId = ole.Attribute("ProgID")?.Value ?? string.Empty;
        var embedded = new EmbeddedObject(payload, progId);

        // The VML v:shape supplies the on-page icon (v:imagedata r:id → media part) and the size (CSS @style).
        var shape = obj!.Element(V + "shape");
        var imagedataRel = shape?.Element(V + "imagedata")?.Attribute(R + "id")?.Value;
        if (imagedataRel is not null
            && relationships.TryGetValue(imagedataRel, out var iconPath)
            && LoadMedia(archive, iconPath) is { } iconBytes)
        {
            var (iconWidthPt, iconHeightPt) = ParseVmlShapeSize(shape!.Attribute("style")?.Value);
            embedded.Icon = new InlineImage(iconBytes, iconWidthPt, iconHeightPt);
            embedded.WidthPt = iconWidthPt;
            embedded.HeightPt = iconHeightPt;
        }
        else if (ParseVmlShapeSize(shape?.Attribute("style")?.Value) is var (w, h) && (w > 0 || h > 0))
        {
            embedded.WidthPt = w;
            embedded.HeightPt = h;
        }

        return embedded;
    }

    /// <summary>
    /// Parses a VML shape @style ("width:96pt;height:96pt") into its width/height in points. Missing or
    /// unparseable dimensions read back as 0 (the caller keeps the model default in that case).
    /// </summary>
    private static (double WidthPt, double HeightPt) ParseVmlShapeSize(string? style)
    {
        if (string.IsNullOrEmpty(style))
            return (0, 0);
        double width = 0, height = 0;
        foreach (var part in style.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var colon = part.IndexOf(':');
            if (colon <= 0)
                continue;
            var key = part[..colon].Trim();
            var value = part[(colon + 1)..].Trim();
            if (value.EndsWith("pt", StringComparison.OrdinalIgnoreCase))
                value = value[..^2].Trim();
            if (!double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var pt))
                continue;
            if (key.Equals("width", StringComparison.OrdinalIgnoreCase))
                width = pt;
            else if (key.Equals("height", StringComparison.OrdinalIgnoreCase))
                height = pt;
        }
        return (width, height);
    }

    /// <summary>
    /// Reads an inline chart (w:drawing/wp:inline/a:graphic/a:graphicData[uri=chart]/c:chart) from a run
    /// into a <see cref="Chart"/>, if present. Resolves the c:chart/@r:id to the chart part via
    /// <paramref name="relationships"/> (the all-parts map), loads it and parses its kind, title, category
    /// labels and series values back out of the literal caches. Returns null when the run carries no chart.
    /// </summary>
    private static Chart? ReadChart(XElement run, ZipArchive archive, IReadOnlyDictionary<string, string> relationships)
    {
        var inline = run.Element(W + "drawing")?.Element(Wp + "inline");
        if (inline is null)
            return null;

        var chartRef = inline.Descendants(C + "chart").FirstOrDefault(e => e.Attribute(R + "id") is not null);
        var relationshipId = chartRef?.Attribute(R + "id")?.Value;
        if (relationshipId is null || !relationships.TryGetValue(relationshipId, out var partPath))
            return null;

        var chartXml = LoadPart(archive, partPath);
        var chartElement = chartXml?.Root?.Element(C + "chart");
        if (chartElement is null)
            return null;

        var plotArea = chartElement.Element(C + "plotArea");
        if (plotArea is null)
            return null;

        // Find the single chart-type element and map it to a ChartKind. barChart's c:barDir distinguishes
        // column (vertical) from bar (horizontal); anything else falls back to Column.
        var (typeElement, kind) = ResolveChartType(plotArea);
        if (typeElement is null)
            return null;

        var chart = new Chart { Kind = kind };

        // Title: the first c:title's concatenated a:t text (when present and not auto-deleted).
        var titleText = string.Concat(
            chartElement.Element(C + "title")?.Descendants(A + "t").Select(t => t.Value) ?? []);
        if (titleText.Length > 0)
            chart.Title = titleText;

        // A legend element (regardless of position) maps back to ShowLegend.
        chart.ShowLegend = chartElement.Element(C + "legend") is not null;

        // Axis titles: the c:catAx / c:valAx title text (the scatter x-axis is a value axis at axPos="b", so
        // it carries the CategoryAxisTitle). Read by axis position to stay kind-agnostic.
        var (categoryAxisTitle, valueAxisTitle) = ReadAxisTitles(plotArea);
        chart.CategoryAxisTitle = categoryAxisTitle;
        chart.ValueAxisTitle = valueAxisTitle;

        // Categories: read once from the first series, shared across series. Scatter has no c:cat — its
        // categories live in c:xVal (a number cache), so read those as their invariant string form.
        var firstSeries = typeElement.Elements(C + "ser").FirstOrDefault();
        if (firstSeries is not null)
        {
            if (kind == ChartKind.Scatter)
                foreach (var x in ReadNumberCache(firstSeries.Element(C + "xVal")))
                    chart.Categories.Add(x.ToString(System.Globalization.CultureInfo.InvariantCulture));
            else
                foreach (var value in ReadStringCache(firstSeries.Element(C + "cat")))
                    chart.Categories.Add(value);
        }

        // Series: name (c:tx string cache) + values (c:val number cache, or c:yVal for scatter).
        var valueElementName = kind == ChartKind.Scatter ? "yVal" : "val";
        foreach (var ser in typeElement.Elements(C + "ser"))
        {
            var name = ReadStringCache(ser.Element(C + "tx")).FirstOrDefault();
            var series = new ChartSeries { Name = string.IsNullOrEmpty(name) ? null : name };
            series.Values.AddRange(ReadNumberCache(ser.Element(C + valueElementName)));
            chart.Series.Add(series);
        }

        // Size: the inline extent (EMU) maps back to points.
        var extent = inline.Element(Wp + "extent");
        chart.WidthPt = EmuToPoints(extent?.Attribute("cx")?.Value);
        chart.HeightPt = EmuToPoints(extent?.Attribute("cy")?.Value);

        return chart;
    }

    /// <summary>
    /// Finds the plot area's single chart-type element and maps it to a <see cref="ChartKind"/>:
    /// c:barChart → Column/Bar (by c:barDir), c:lineChart → Line, c:areaChart → Area, c:pieChart → Pie,
    /// c:doughnutChart → Doughnut, c:scatterChart → Scatter. Returns (null, Column) when none is present.
    /// </summary>
    private static (XElement? Element, ChartKind Kind) ResolveChartType(XElement plotArea)
    {
        if (plotArea.Element(C + "barChart") is { } bar)
        {
            var dir = bar.Element(C + "barDir")?.Attribute(C + "val")?.Value;
            return (bar, dir == "bar" ? ChartKind.Bar : ChartKind.Column);
        }
        if (plotArea.Element(C + "lineChart") is { } line)
            return (line, ChartKind.Line);
        if (plotArea.Element(C + "areaChart") is { } area)
            return (area, ChartKind.Area);
        if (plotArea.Element(C + "pieChart") is { } pie)
            return (pie, ChartKind.Pie);
        if (plotArea.Element(C + "doughnutChart") is { } doughnut)
            return (doughnut, ChartKind.Doughnut);
        if (plotArea.Element(C + "scatterChart") is { } scatter)
            return (scatter, ChartKind.Scatter);
        return (null, ChartKind.Column);
    }

    /// <summary>
    /// Reads the bottom (category/x) and left (value/y) axis titles from a plot area by axis position. The
    /// category-axis title is taken from the bottom-positioned axis (axPos="b" — a c:catAx for the cartesian
    /// kinds, or a c:valAx for scatter) and the value-axis title from the left axis (axPos="l"). Returns nulls
    /// when an axis has no title.
    /// </summary>
    private static (string? Category, string? Value) ReadAxisTitles(XElement plotArea)
    {
        string? category = null;
        string? value = null;
        foreach (var axis in plotArea.Elements().Where(e => e.Name == C + "catAx" || e.Name == C + "valAx"))
        {
            var position = axis.Element(C + "axPos")?.Attribute(C + "val")?.Value;
            var title = string.Concat(axis.Element(C + "title")?.Descendants(A + "t").Select(t => t.Value) ?? []);
            if (title.Length == 0)
                continue;
            if (position == "b")
                category = title;
            else if (position == "l")
                value = title;
        }
        return (category, value);
    }

    /// <summary>
    /// Reads an inline SmartArt diagram (w:drawing/wp:inline/a:graphic/a:graphicData[uri=diagram]/dgm:relIds)
    /// from a run into a <see cref="SmartArt"/>, if present. Resolves the dgm:relIds/@r:dm id to the diagram
    /// DATA part via <paramref name="relationships"/> (the all-parts map), then parses the dgm:dataModel:
    /// each non-doc dgm:pt's text body (a:t) is a node, and the dgm:cxnLst parOf connections rebuild the
    /// parent→child tree. The diagram KIND is inferred from the data part's sibling layout part's uniqueId
    /// (process / hierarchy / list). Returns null when the run carries no diagram.
    /// </summary>
    private static SmartArt? ReadSmartArt(XElement run, ZipArchive archive, IReadOnlyDictionary<string, string> relationships)
    {
        var inline = run.Element(W + "drawing")?.Element(Wp + "inline");
        if (inline is null)
            return null;

        var relIds = inline.Descendants(Dgm + "relIds").FirstOrDefault();
        var dataRelId = relIds?.Attribute(R + "dm")?.Value;
        if (dataRelId is null || !relationships.TryGetValue(dataRelId, out var dataPath))
            return null;

        var dataXml = LoadPart(archive, dataPath);
        var dataModel = dataXml?.Root;
        if (dataModel is null || dataModel.Name != Dgm + "dataModel")
            return null;

        var ptLst = dataModel.Element(Dgm + "ptLst");
        if (ptLst is null)
            return null;

        // Index every node point (skip the type="doc"/"pres" presentation points) by its modelId, capturing
        // its text from the first a:t in its dgm:t body.
        var textById = new Dictionary<string, string>(StringComparer.Ordinal);
        var nodeById = new Dictionary<string, SmartArtNode>(StringComparer.Ordinal);
        var orderedIds = new List<string>();
        foreach (var pt in ptLst.Elements(Dgm + "pt"))
        {
            var type = pt.Attribute("type")?.Value;
            if (type is "doc" or "pres")
                continue;
            var modelId = pt.Attribute("modelId")?.Value;
            if (modelId is null)
                continue;
            var text = string.Concat(pt.Element(Dgm + "t")?.Descendants(A + "t").Select(t => t.Value) ?? []);
            textById[modelId] = text;
            nodeById[modelId] = new SmartArtNode(text);
            orderedIds.Add(modelId);
        }

        // Rebuild the tree from the parOf connections: each connection's destId is a child of its srcId.
        // Connections whose srcId is not a node id (e.g. the document point) mark a top-level node.
        var childIds = new HashSet<string>(StringComparer.Ordinal);
        var topLevel = new List<SmartArtNode>();
        var parentOrder = new List<(string Src, string Dest)>();
        foreach (var cxn in dataModel.Element(Dgm + "cxnLst")?.Elements(Dgm + "cxn") ?? [])
        {
            if (cxn.Attribute("type")?.Value != "parOf")
                continue;
            var src = cxn.Attribute("srcId")?.Value;
            var dest = cxn.Attribute("destId")?.Value;
            if (src is null || dest is null || !nodeById.ContainsKey(dest))
                continue;
            parentOrder.Add((src, dest));
            if (nodeById.ContainsKey(src))
                childIds.Add(dest);
        }
        foreach (var (src, dest) in parentOrder)
            if (nodeById.TryGetValue(src, out var parent))
                parent.Children.Add(nodeById[dest]);
        foreach (var id in orderedIds)
            if (!childIds.Contains(id))
                topLevel.Add(nodeById[id]);

        var smartArt = new SmartArt { Kind = ReadSmartArtKind(relIds, relationships, archive) };
        smartArt.Nodes.AddRange(topLevel);

        // Size: the inline extent (EMU) maps back to points.
        var extent = inline.Element(Wp + "extent");
        smartArt.WidthPt = EmuToPoints(extent?.Attribute("cx")?.Value);
        smartArt.HeightPt = EmuToPoints(extent?.Attribute("cy")?.Value);

        return smartArt;
    }

    /// <summary>
    /// Infers the <see cref="SmartArtKind"/> from the layout part's uniqueId (resolved via the relIds/@r:lo
    /// id). Falls back to <see cref="SmartArtKind.List"/> when the layout part or its id is absent/unknown.
    /// </summary>
    private static SmartArtKind ReadSmartArtKind(XElement? relIds, IReadOnlyDictionary<string, string> relationships, ZipArchive archive)
    {
        var layoutRelId = relIds?.Attribute(R + "lo")?.Value;
        if (layoutRelId is null || !relationships.TryGetValue(layoutRelId, out var layoutPath))
            return SmartArtKind.List;
        var uniqueId = LoadPart(archive, layoutPath)?.Root?.Attribute("uniqueId")?.Value ?? string.Empty;
        if (uniqueId.Contains("process", StringComparison.OrdinalIgnoreCase))
            return SmartArtKind.Process;
        if (uniqueId.Contains("hierarchy", StringComparison.OrdinalIgnoreCase))
            return SmartArtKind.Hierarchy;
        return SmartArtKind.List;
    }

    /// <summary>
    /// Reads the literal string cache (c:strRef/c:strCache or a bare c:strCache) under <paramref name="parent"/>
    /// into an ordered list of values by c:pt/@idx. Returns an empty list when the parent or cache is absent.
    /// </summary>
    private static List<string> ReadStringCache(XElement? parent)
    {
        var cache = parent?.Descendants(C + "strCache").FirstOrDefault();
        return ReadCachePoints(cache).Select(p => p.Value).ToList();
    }

    /// <summary>Reads the literal number cache (c:numRef/c:numCache) under <paramref name="parent"/> into ordered doubles.</summary>
    private static List<double> ReadNumberCache(XElement? parent)
    {
        var cache = parent?.Descendants(C + "numCache").FirstOrDefault();
        return ReadCachePoints(cache)
            .Select(p => double.TryParse(p.Value, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : 0)
            .ToList();
    }

    /// <summary>
    /// Returns a chart cache's points ordered by their c:pt/@idx (the OOXML cache is index-addressed, so we
    /// sort to be robust against out-of-order or sparse points). Each point's text is its c:v value.
    /// </summary>
    private static IEnumerable<(int Idx, string Value)> ReadCachePoints(XElement? cache)
    {
        if (cache is null)
            return [];
        return cache.Elements(C + "pt")
            .Select(pt => (
                Idx: int.TryParse(pt.Attribute(C + "idx")?.Value, out var idx) ? idx : 0,
                Value: pt.Element(C + "v")?.Value ?? string.Empty))
            .OrderBy(p => p.Idx);
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

        // Advanced typography (Z1). The three core elements use the standard unit conversions; the
        // w14:* extension elements use the shared token maps. Each is optional and maps a missing
        // element back to the model default so default runs read back unchanged.
        var spacing = rPr.Element(W + "spacing")?.Attribute(W + "val")?.Value;
        var kern = rPr.Element(W + "kern")?.Attribute(W + "val")?.Value;
        var position = rPr.Element(W + "position")?.Attribute(W + "val")?.Value;
        var ligatures = rPr.Element(W14 + "ligatures")?.Attribute(W14 + "val")?.Value;
        var numForm = rPr.Element(W14 + "numForm")?.Attribute(W14 + "val")?.Value;
        var numSpacing = rPr.Element(W14 + "numSpacing")?.Attribute(W14 + "val")?.Value;
        // A stylistic set is the first w14:styleSet inside w14:stylisticSets (the common single-set case).
        var styleSetId = rPr.Element(W14 + "stylisticSets")?.Element(W14 + "styleSet")?.Attribute(W14 + "id")?.Value;

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
            },
            // w:spacing is in dxa (twentieths of a point); 0 / absent means no advanced spacing.
            CharacterSpacingPt = spacing is null ? 0 : DxaToPoints(spacing),
            // w:kern is in half-points; absent means no kerning threshold (null).
            KerningMinSizePt = kern is null ? null : HalfPointsToPoints(kern),
            // w:position is in half-points, signed; 0 / absent means baseline.
            PositionPt = position is null ? 0 : (ParseInt(position) / 2.0),
            Ligatures = LigatureModeFromToken(ligatures),
            NumberForm = NumberFormFromToken(numForm),
            NumberSpacing = NumberSpacingFromToken(numSpacing),
            StylisticSet = styleSetId is null ? null
                : int.TryParse(styleSetId, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var id) ? id : null
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
