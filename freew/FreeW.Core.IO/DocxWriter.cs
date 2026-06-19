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

    // Header/footer part names + relationship ids are now generated per part (see CollectHeaderFooterParts):
    // the legacy header1/footer1/header2/footer2 + rIdHeader1/rIdFooter1/rIdHeader2/rIdFooter2 are reproduced
    // for the final section so single-section documents stay byte-equivalent.
    private const string FootnotesRelationshipId = "rIdFootnotes";
    private const string EndnotesRelationshipId = "rIdEndnotes";
    private const string CommentsRelationshipId = "rIdComments";
    private const string CommentsExtendedRelationshipId = "rIdCommentsExtended";
    private const string SettingsRelationshipId = "rIdSettings";
    private const string FontTableRelationshipId = "rIdFontTable";
    private const string BibliographyRelationshipId = "rIdBibliography";
    private const string ThemeRelationshipId = "rIdTheme1";

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
        // Assign a relationship + part name to every inline chart the same way (charts are a separate XML
        // part referenced from the run drawing by r:id, mirroring how images add a media part + r:embed).
        var charts = CollectCharts(document);
        // Assign a relationship + binary part name to every inline embedded OLE object the same way. Each
        // object's presentation icon is collected as an extra ImagePart appended to `images`, so the icon
        // media part + relationship + png content-type flow through the existing image plumbing untouched.
        var embeddedObjects = CollectEmbeddedObjects(document, images);
        // Assign four relationship ids + four part names to every inline SmartArt diagram the same way
        // (a diagram is four separate XML parts referenced from the run drawing by dgm:relIds).
        var smartArts = CollectSmartArts(document);
        // Assign an external relationship id to every distinct hyperlink target the same way.
        var hyperlinks = CollectHyperlinks(document);
        // Emit a numbering part only when at least one paragraph is decorated as a list.
        var hasLists = EnumerateParagraphs(document).Any(p => p.Formatting.ListKind != ListKind.None);

        // Preserved numbering FreeW does not model: when the source carried a numbering.xml AND at least one
        // paragraph (or paragraph STYLE) kept its original w:numPr (because FreeW did not map it to a ListKind),
        // build a merge plan
        // that re-emits the ORIGINAL abstractNum/num definitions alongside FreeW's own under a DISJOINT id range
        // (originals remapped to abstractNumId>=3 / numId>=4, clear of FreeW's fixed 0..2 / 1..3). The plan also
        // maps each original numId to its output numId so the preserved paragraphs' w:numPr re-emit consistently.
        // Null for an authored-from-scratch / FreeW-only-lists document, so such a document is unaffected.
        var preservedNumbering = BuildPreservedNumberingPlan(document);

        // A numbering part is emitted when FreeW authored a list OR preserved numbering must be re-emitted.
        var emitNumbering = hasLists || preservedNumbering is not null;

        // Header/footer parts are now modelled per-section: one part per (section × header/footer × type)
        // slot that carries visible content, each with its OWN image relationships. The final/body-level
        // section owns the legacy header1/footer1/header2/footer2 names so single-section documents stay
        // byte-equivalent (see CollectHeaderFooterParts). Even/first parts are only included when the owning
        // section's page settings turn on different-odd/even or different-first-page respectively.
        var headerFooterParts = CollectHeaderFooterParts(document);

        // A footnotes part is emitted only when the document actually carries footnotes.
        var hasFootnotes = document.Footnotes.Count > 0;

        // An endnotes part is emitted only when the document actually carries endnotes.
        var hasEndnotes = document.Endnotes.Count > 0;

        // A comments part is emitted only when the document actually carries review comments.
        var hasComments = document.Comments.Count > 0;

        // Inline images carried by comment paragraphs (e.g. a pasted picture in a comment). Each becomes a
        // part-local media file + a relationship in word/_rels/comments.xml.rels, so comment-part images
        // round-trip referenced rather than orphaned. Empty for text-only comments.
        var commentImages = hasComments ? CollectCommentImages(document) : new List<ImagePart>();

        // The watermark text is persisted best-effort as a custom document property (docProps/custom.xml),
        // emitted only when a watermark is set.
        var hasWatermark = !string.IsNullOrEmpty(document.Page.Watermark);

        // A word/settings.xml part is emitted only when something needs it — document protection
        // (w:documentProtection), automatic hyphenation (w:autoHyphenation), the different-odd/even-headers
        // toggle (w:evenAndOddHeaders) and/or a page background to display (w:displayBackgroundShape) — so
        // documents that need none round-trip exactly as before (no settings part).
        var hasProtection = document.Protection.IsProtected;
        var hasBackground = !string.IsNullOrEmpty(document.Page.BackgroundColorHex);

        // Embedded fonts: each EmbeddedFont family contributes one obfuscated .odttf part per embedded style,
        // collected up front so word/fontTable.xml, its rels and the font parts all agree on rId/fontN.odttf.
        // When any font is embedded a settings part is forced (w:embedTrueTypeFonts must live in settings.xml).
        var embeddedFonts = CollectEmbeddedFonts(document);
        var hasEmbeddedFonts = embeddedFonts.Count > 0;

        // A preserved original settings part (captured on read) forces a settings part too, so unmodelled
        // settings survive the round-trip even when none of FreeW's own settings-triggering features are on.
        var hasPreservedSettings = document.Preserved.OriginalSettings is not null;

        var hasSettings = hasProtection
            || document.Page.AutoHyphenation
            || document.Page.DifferentOddEvenPages
            || hasBackground
            || hasEmbeddedFonts
            || hasPreservedSettings;

        // The bibliography part (word/bibliography/sources.xml) persists the document's citation Sources and
        // selected BibliographyStyle. Emitted whenever there are sources or a non-default style to record, so a
        // document that has never touched citations round-trips exactly as before (no bibliography part).
        var hasBibliography = document.Sources.Count > 0 || document.BibliographyStyle != CitationStyle.Apa;

        // A word/theme/theme1.xml part is always emitted (real Word documents always carry one); it
        // serialises the document's DocumentTheme as a real clrScheme/fontScheme/fmtScheme.
        // [Content_Types].xml needs a Default for every image extension actually used by ANY part — body
        // images or header/footer images — so non-PNG pictures (jpeg/gif/bmp/tiff/emf/wmf) are declared too.
        // Ordered (png first) for deterministic output; only extensions present are emitted.
        var imageExtensions = images
            .Concat(headerFooterParts.SelectMany(p => p.Images))
            .Concat(commentImages)
            .Select(p => InlineImage.ExtensionFor(p.Image.Format))
            .Distinct()
            .OrderBy(ext => ext, StringComparer.Ordinal)
            .ToList();

        using var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true);
        WritePart(archive, "[Content_Types].xml", BuildContentTypes(imageExtensions, emitNumbering, headerFooterParts, hasFootnotes, hasEndnotes, hasComments, hasWatermark, hasSettings, hasBibliography, charts, embeddedObjects.Count > 0, smartArts, hasEmbeddedFonts, document.Preserved.Parts, document.Preserved.ContentTypeDefaults));
        WritePart(archive, "_rels/.rels", BuildPackageRels(hasWatermark));
        WritePart(archive, "docProps/core.xml", BuildCoreProperties(document.Properties));
        if (hasWatermark)
            WritePart(archive, "docProps/custom.xml", BuildCustomProperties(document.Page.Watermark!));
        WritePart(archive, "word/_rels/document.xml.rels", BuildDocumentRels(images, hyperlinks, emitNumbering, headerFooterParts, hasFootnotes, hasEndnotes, hasComments, hasSettings, hasBibliography, charts, embeddedObjects, smartArts, hasEmbeddedFonts, document.Preserved.Parts));
        WritePart(archive, "word/document.xml", BuildDocument(document, images, charts, embeddedObjects, smartArts, hyperlinks, headerFooterParts, preservedNumbering));
        WritePart(archive, "word/styles.xml", BuildStyles(document, preservedNumbering));
        WritePart(archive, ThemePartName.TrimStart('/'), BuildTheme(document.Theme));
        if (hasSettings)
            WritePart(archive, SettingsPartName.TrimStart('/'), BuildSettings(document.Protection, document.Page, hasBackground, hasEmbeddedFonts, document.Preserved.OriginalSettings));
        if (hasBibliography)
            WritePart(archive, BibliographyPartName.TrimStart('/'), BuildBibliographySources(document));
        // Embedded fonts: word/fontTable.xml + its rels + one obfuscated .odttf per embedded style.
        if (hasEmbeddedFonts)
        {
            WritePart(archive, FontTablePartName.TrimStart('/'), BuildFontTable(embeddedFonts));
            WritePart(archive, "word/_rels/fontTable.xml.rels", BuildFontTableRels(embeddedFonts));
            foreach (var part in embeddedFonts.SelectMany(f => f.Parts))
                WriteBinaryPart(archive, "word/fonts/" + part.FileName, ObfuscateFont(part.FontBytes, part.FontKey));
        }
        if (emitNumbering)
            WritePart(archive, "word/numbering.xml", BuildNumbering(hasLists, preservedNumbering));
        // One part per (section × header/footer × type) slot with content. Each part XML carries its inline
        // images via PART-LOCAL r:embed ids resolved against its own word/_rels/<part>.xml.rels, and its
        // image media bytes go under word/media/.
        foreach (var part in headerFooterParts)
        {
            WritePart(archive, "word/" + part.FileName,
                BuildHeaderFooter(part.IsHeader ? W + "hdr" : W + "ftr", part));
            if (part.Images.Count > 0)
            {
                WritePart(archive, "word/_rels/" + part.FileName + ".rels", BuildHeaderFooterRels(part));
                foreach (var image in part.Images)
                    WriteBinaryPart(archive, "word/media/" + image.FileName, image.Image.Bytes);
            }
        }
        if (hasFootnotes)
            WritePart(archive, FootnotesPartName.TrimStart('/'), BuildFootnotes(document));
        if (hasEndnotes)
            WritePart(archive, EndnotesPartName.TrimStart('/'), BuildEndnotes(document));
        if (hasComments)
        {
            WritePart(archive, CommentsPartName.TrimStart('/'), BuildComments(document, commentImages));
            // word/commentsExtended.xml threads replies + carries resolved state. Always emitted alongside the
            // comments part (it has an entry per comment even for a flat, single-comment document) so modern
            // Word treats every comment as a thread root and the reply/resolve plumbing round-trips.
            WritePart(archive, CommentsExtendedPartName.TrimStart('/'), BuildCommentsExtended(document));
            // Comment media + the comments part's own _rels (only when a comment carries an image).
            if (commentImages.Count > 0)
            {
                WritePart(archive, "word/_rels/comments.xml.rels", BuildCommentsRels(commentImages));
                foreach (var image in commentImages)
                    WriteBinaryPart(archive, "word/media/" + image.FileName, image.Image.Bytes);
            }
        }
        foreach (var image in images)
            WriteBinaryPart(archive, "word/media/" + image.FileName, image.Image.Bytes);
        foreach (var chart in charts)
        {
            WritePart(archive, "word/charts/" + chart.FileName, BuildChartSpace(chart));
            // F1: each chart gets an editable companion workbook + a part-local _rels referencing it (the
            // "package" relationship Word's "Edit Data" follows). The chart XML's c:externalData points here.
            WriteBinaryPart(archive, "word/embeddings/" + chart.EmbeddingFileName, BuildChartWorkbook(chart.Chart));
            WritePart(archive, "word/charts/_rels/" + chart.FileName + ".rels", BuildChartRels(chart));
        }
        // Each embedded OLE object's native payload is written verbatim as a binary part. Its presentation
        // icon (if any) was appended to `images` by CollectEmbeddedObjects and is emitted in the media loop.
        foreach (var embedded in embeddedObjects)
            WriteBinaryPart(archive, "word/embeddings/" + embedded.FileName, embedded.EmbeddedObject.Payload);
        foreach (var smartArt in smartArts)
        {
            // F2: the data part carries a dgm:dataModelExt pointing (via DrawingRelationshipId) at the
            // rendered-geometry drawing part, whose relationship is declared in the data part's own .rels.
            WritePart(archive, "word/diagrams/" + smartArt.DataFileName, BuildDiagramData(smartArt.SmartArt, smartArt.DrawingRelationshipId));
            WritePart(archive, "word/diagrams/_rels/" + smartArt.DataFileName + ".rels", BuildDiagramDataRels(smartArt));
            WritePart(archive, "word/diagrams/" + smartArt.LayoutFileName, BuildDiagramLayout(smartArt.SmartArt.Kind));
            WritePart(archive, "word/diagrams/" + smartArt.QuickStyleFileName, BuildDiagramQuickStyle());
            WritePart(archive, "word/diagrams/" + smartArt.ColorsFileName, BuildDiagramColors());
            WritePart(archive, "word/diagrams/" + smartArt.DrawingFileName, BuildDiagramDrawing(smartArt.SmartArt));
        }
        // Unmodelled-but-preserved parts (customXml/*, word/webSettings.xml): re-emitted byte-for-byte. Their
        // content-type Overrides and document relationships are added by BuildContentTypes / BuildDocumentRels;
        // their own _rels (e.g. customXml item rels) are themselves preserved parts, so the whole satellite set
        // round-trips. Authored-from-scratch documents have none, so nothing extra is written.
        foreach (var part in document.Preserved.Parts)
            WriteBinaryPart(archive, part.PartName.TrimStart('/'), part.Bytes);
    }

    /// <summary>An inline image paired with the relationship id, media file name and a unique drawing id.</summary>
    private sealed record ImagePart(InlineImage Image, string RelationshipId, string FileName, uint DrawingId);

    /// <summary>
    /// An inline chart paired with the document relationship id, chart part file name (relative to
    /// <c>word/charts/</c>) and a unique drawing id, mirroring <see cref="ImagePart"/>. <see cref="EmbeddingFileName"/>
    /// is the companion editable-data workbook (relative to <c>word/embeddings/</c>); the chart part's own
    /// <c>_rels</c> references it under <see cref="ExternalDataRelId"/> (a part-local rId) so Word's "Edit Data"
    /// can reopen it (F1).
    /// </summary>
    private sealed record ChartPart(Chart Chart, string RelationshipId, string FileName, uint DrawingId, string EmbeddingFileName, string ExternalDataRelId);

    private static List<ChartPart> CollectCharts(TextDocument document)
    {
        var charts = new List<ChartPart>();
        foreach (var paragraph in EnumerateParagraphs(document))
            foreach (var run in paragraph.Runs)
                if (run.Chart is { } chart)
                {
                    var index = charts.Count + 1;
                    // The external-data rId is part-LOCAL (it lives in word/charts/_rels/chartN.xml.rels), so a
                    // fixed "rId1" per chart is fine and never collides with the document-level ids above.
                    charts.Add(new ChartPart(chart, $"rIdChart{index}", $"chart{index}.xml", (uint)index, $"Microsoft_Excel_Worksheet{index}.xlsx", "rId1"));
                }
        return charts;
    }

    /// <summary>
    /// An inline embedded OLE object paired with its document relationship id (to the .bin part), the binary
    /// part file name (relative to <c>word/embeddings/</c>), the VML shape id, and — when the object carries
    /// a presentation icon — the <see cref="ImagePart"/> emitting that icon as a media part. Mirrors
    /// <see cref="ChartPart"/>; the icon part is shared with the ordinary image plumbing.
    /// </summary>
    private sealed record EmbeddedObjectPart(
        EmbeddedObject EmbeddedObject,
        string RelationshipId,
        string FileName,
        string ShapeId,
        ImagePart? IconPart);

    /// <summary>
    /// Assigns each inline embedded OLE object a relationship id (rIdOleN), a binary part name
    /// (oleObjectN.bin) and a VML shape id. When the object has a presentation icon, an extra
    /// <see cref="ImagePart"/> is appended to <paramref name="images"/> so the icon's media part, document
    /// relationship and png content-type flow through the existing image plumbing unchanged. The walk order
    /// matches <see cref="EnumerateParagraphs"/> so document.xml and the rels agree on which ids belong to
    /// which run (replayed in <see cref="BuildDocument"/>).
    /// </summary>
    private static List<EmbeddedObjectPart> CollectEmbeddedObjects(TextDocument document, List<ImagePart> images)
    {
        var embedded = new List<EmbeddedObjectPart>();
        foreach (var paragraph in EnumerateParagraphs(document))
            foreach (var run in paragraph.Runs)
                if (run.EmbeddedObject is { } obj)
                {
                    var index = embedded.Count + 1;
                    ImagePart? iconPart = null;
                    if (obj.Icon is { } icon)
                    {
                        // Continue the image numbering so the icon media file name never clashes with a body
                        // image; the appended part is emitted by the ordinary media/rel/content-type loops.
                        var imageIndex = images.Count + 1;
                        iconPart = new ImagePart(icon, $"rIdImg{imageIndex}", $"image{imageIndex}.{InlineImage.ExtensionFor(icon.Format)}", (uint)imageIndex);
                        images.Add(iconPart);
                    }
                    embedded.Add(new EmbeddedObjectPart(obj, $"rIdOle{index}", $"oleObject{index}.bin", $"_oleObj{index}", iconPart));
                }
        return embedded;
    }

    /// <summary>
    /// An inline SmartArt diagram paired with its four document relationship ids and four part file names
    /// (relative to <c>word/diagrams/</c>) plus a unique drawing id. The diagram is four separate XML parts
    /// — data (the node text/structure), layout, quickStyle and colors — referenced together from the run
    /// drawing's <c>dgm:relIds</c>. Mirrors <see cref="ChartPart"/>.
    /// F2: a FIFTH part — the rendered-geometry drawing (<c>drawingN.xml</c>, dsp:drawing) — is also emitted.
    /// It is referenced from the DATA part (not the run drawing) via <see cref="DrawingRelationshipId"/> in
    /// <c>word/diagrams/_rels/dataN.xml.rels</c> plus a <c>dgm:dataModelExt</c> inside the data part, so a
    /// viewer can show positioned shapes without re-running SmartArt auto-layout.
    /// </summary>
    private sealed record SmartArtPart(
        SmartArt SmartArt,
        string DataRelationshipId,
        string LayoutRelationshipId,
        string QuickStyleRelationshipId,
        string ColorsRelationshipId,
        string DataFileName,
        string LayoutFileName,
        string QuickStyleFileName,
        string ColorsFileName,
        string DrawingRelationshipId,
        string DrawingFileName,
        uint DrawingId);

    private static List<SmartArtPart> CollectSmartArts(TextDocument document)
    {
        var smartArts = new List<SmartArtPart>();
        foreach (var paragraph in EnumerateParagraphs(document))
            foreach (var run in paragraph.Runs)
                if (run.SmartArt is { } smartArt)
                {
                    var index = smartArts.Count + 1;
                    smartArts.Add(new SmartArtPart(
                        smartArt,
                        $"rIdDgmData{index}",
                        $"rIdDgmLayout{index}",
                        $"rIdDgmStyle{index}",
                        $"rIdDgmColors{index}",
                        $"data{index}.xml",
                        $"layout{index}.xml",
                        $"quickStyle{index}.xml",
                        $"colors{index}.xml",
                        // The drawing rel is data-part-relative (lives in word/diagrams/_rels/dataN.xml.rels),
                        // so a plain "rId1" is fine and clash-free per data part.
                        "rId1",
                        $"drawing{index}.xml",
                        (uint)index));
                }
        return smartArts;
    }

    /// <summary>
    /// One obfuscated font part: the style slot (regular/bold/italic/boldItalic), its document-unique
    /// relationship id (in the fontTable's rels), the file name (relative to <c>word/fonts/</c>), the
    /// deterministically derived fontKey GUID, and the original (de-obfuscated) font bytes. The writer
    /// obfuscates <see cref="FontBytes"/> with <see cref="FontKey"/> when emitting the .odttf part.
    /// </summary>
    private sealed record FontStylePart(string Slot, string RelationshipId, string FileName, string FontKey, byte[] FontBytes);

    /// <summary>An embedded font family paired with the obfuscated parts for each embedded style.</summary>
    private sealed record FontTablePart(EmbeddedFont Font, IReadOnlyList<FontStylePart> Parts);

    /// <summary>
    /// Assigns each embedded font style a relationship id (rIdFontN), a part name (fontN.odttf) and a
    /// deterministic fontKey GUID (derived from family+style, never random). Styles are walked in a fixed
    /// order (regular, bold, italic, boldItalic) so the ids are stable. Families carrying no embedded style
    /// are skipped, so an empty/blank EmbeddedFonts list yields no parts (no fontTable emitted).
    /// </summary>
    private static List<FontTablePart> CollectEmbeddedFonts(TextDocument document)
    {
        var families = new List<FontTablePart>();
        var index = 0;
        foreach (var font in document.EmbeddedFonts)
        {
            var parts = new List<FontStylePart>();
            void Add(string slot, byte[]? bytes)
            {
                if (bytes is not { Length: > 0 })
                    return;
                index++;
                var key = DeterministicFontKey(font.Family + "/" + slot);
                parts.Add(new FontStylePart(slot, $"rIdFont{index}", $"font{index}.odttf", key, bytes));
            }
            Add("embedRegular", font.Regular);
            Add("embedBold", font.Bold);
            Add("embedItalic", font.Italic);
            Add("embedBoldItalic", font.BoldItalic);
            if (parts.Count > 0)
                families.Add(new FontTablePart(font, parts));
        }
        return families;
    }

    private static List<ImagePart> CollectImages(TextDocument document)
    {
        var images = new List<ImagePart>();
        foreach (var paragraph in EnumerateParagraphs(document))
            foreach (var run in paragraph.Runs)
                if (run.Image is { } image)
                {
                    var index = images.Count + 1;
                    images.Add(new ImagePart(image, $"rIdImg{index}", $"image{index}.{InlineImage.ExtensionFor(image.Format)}", (uint)index));
                }
        return images;
    }

    /// <summary>
    /// The header/footer "type" a part fills (w:headerReference/w:footerReference @w:type), independent of
    /// whether it is a header or a footer. <see cref="Default"/> is the all-pages (or odd-pages) header/footer;
    /// <see cref="Even"/> is the even-page one (different odd/even pages); <see cref="First"/> is the first-page
    /// one (different first page).
    /// </summary>
    private enum HeaderFooterType { Default, Even, First }

    /// <summary>
    /// One emitted header or footer part for one section: the part file name (e.g. <c>header3.xml</c>), the
    /// document relationship id that the owning section's w:sectPr references, the root element name
    /// (<c>w:hdr</c>/<c>w:ftr</c>), the reference element name (<c>headerReference</c>/<c>footerReference</c>),
    /// the @w:type token, the model content, and the inline images the part's runs carry (each with a
    /// PART-LOCAL relationship id resolved against this part's own <c>word/_rels/headerN.xml.rels</c>). The
    /// <see cref="Section"/> the part belongs to (null for the final/body-level section) lets the per-section
    /// w:sectPr emit the matching reference.
    /// </summary>
    private sealed record HeaderFooterPart(
        Section? Section,
        HeaderFooterType Type,
        bool IsHeader,
        HeaderFooter Content,
        string FileName,
        string RelationshipId,
        IReadOnlyList<ImagePart> Images);

    /// <summary>
    /// Walks every section (the final/body-level section first, then each non-final section in document
    /// order) and assigns one <see cref="HeaderFooterPart"/> per (section × header/footer × type) slot that
    /// carries visible content. Part numbering preserves the legacy layout exactly so single-section
    /// documents stay byte-equivalent: the final section's default header is <c>header1.xml</c>, its even
    /// header <c>header2.xml</c>, default footer <c>footer1.xml</c>, even footer <c>footer2.xml</c> (the
    /// historical names/ids), and all remaining parts (first-page, and every non-final section's parts)
    /// continue the header/footer counters from there. Even parts are only emitted when the section's page
    /// settings turn on different-odd/even; first parts only when different-first-page is on. Each part also
    /// collects the inline images in its runs and assigns them part-local relationship ids.
    /// </summary>
    private static List<HeaderFooterPart> CollectHeaderFooterParts(TextDocument document)
    {
        var parts = new List<HeaderFooterPart>();
        // Header/footer part-name counters. They are seeded so the legacy final-section parts reuse the exact
        // historical names (header1/header2, footer1/footer2); see AddSectionParts for the seeding scheme.
        var headerCount = 0;
        var footerCount = 0;

        void AddSlot(Section? section, SectionHeadersFooters hf, PageSettings page, bool legacyFinal)
        {
            // For the legacy final section we must reproduce header1=default, header2=even, footer1=default,
            // footer2=even regardless of which exist, so the counters can't simply auto-increment in
            // discovery order. We therefore reserve indices 1 (default) and 2 (even) for the final section.
            void AddPart(bool isHeader, HeaderFooterType type, HeaderFooter? content)
            {
                if (content is null || content.IsEmpty)
                    return;
                int index;
                if (legacyFinal && type == HeaderFooterType.Default)
                    index = 1;
                else if (legacyFinal && type == HeaderFooterType.Even)
                    index = 2;
                else
                    index = (isHeader ? headerCount : footerCount) + 1;

                if (isHeader)
                    headerCount = Math.Max(headerCount, index);
                else
                    footerCount = Math.Max(footerCount, index);

                var fileName = (isHeader ? "header" : "footer") + index + ".xml";
                var relationshipId = (isHeader ? "rIdHeader" : "rIdFooter") + index;
                var images = CollectHeaderFooterImages(content, fileName);
                parts.Add(new HeaderFooterPart(
                    section,
                    type,
                    isHeader,
                    content,
                    fileName,
                    relationshipId,
                    images));
            }

            // Parts are added header-then-footer per type (default, even, first) so the legacy single-section
            // emission order — header1, footer1, header2(even), footer2(even) — is reproduced exactly,
            // keeping content-type Overrides and document relationships byte-equivalent. Even parts only when
            // different-odd/even is on; first parts only when different-first-page is on.
            AddPart(isHeader: true, HeaderFooterType.Default, hf.Header);
            AddPart(isHeader: false, HeaderFooterType.Default, hf.Footer);
            if (page.DifferentOddEvenPages)
            {
                AddPart(isHeader: true, HeaderFooterType.Even, hf.EvenHeader);
                AddPart(isHeader: false, HeaderFooterType.Even, hf.EvenFooter);
            }
            if (page.DifferentFirstPage)
            {
                AddPart(isHeader: true, HeaderFooterType.First, hf.FirstHeader);
                AddPart(isHeader: false, HeaderFooterType.First, hf.FirstFooter);
            }
        }

        // The final (body-level) section first, so it owns the legacy header1/footer1/header2/footer2 names.
        AddSlot(null, document.FinalSectionHeadersFooters, document.Page, legacyFinal: true);
        // Then each non-final section, in document order (the order their sectPr appear in the body).
        foreach (var block in document.Blocks)
            if (block is Paragraph { SectionBreak: { } section })
                AddSlot(section, section.HeadersFooters, section.Page, legacyFinal: false);

        return parts;
    }

    /// <summary>
    /// Collects the inline images carried by a header/footer part's runs and assigns each a PART-LOCAL
    /// relationship id (<c>rIdImgN</c>) plus a media file name unique to this part (derived from the part's
    /// file name, e.g. <c>header3_image1.png</c>) so it never clashes with body media or another part's media.
    /// The walk order matches <see cref="BuildHeaderFooterImagesByRun"/> so the part XML and its rels agree on
    /// which rId belongs to which run.
    /// </summary>
    private static List<ImagePart> CollectHeaderFooterImages(HeaderFooter content, string partFileName)
    {
        var stem = partFileName.EndsWith(".xml", StringComparison.Ordinal)
            ? partFileName[..^4]
            : partFileName;
        var images = new List<ImagePart>();
        foreach (var paragraph in content.Paragraphs)
            foreach (var run in paragraph.Runs)
                if (run.Image is { } image)
                {
                    var index = images.Count + 1;
                    // The rId is part-local (lives in word/_rels/<part>.xml.rels), so a plain rIdImgN never
                    // collides with the document-level image ids. The media file name embeds the part stem so
                    // each part's media files are unique within word/media/, and carries the image's real
                    // extension so non-PNG header/footer images round-trip too.
                    images.Add(new ImagePart(image, $"rIdImg{index}", $"{stem}_image{index}.{InlineImage.ExtensionFor(image.Format)}", (uint)index));
                }
        return images;
    }

    /// <summary>Maps each image run in a header/footer part to its <see cref="ImagePart"/> (same walk as collection).</summary>
    private static Dictionary<Run, ImagePart> BuildHeaderFooterImagesByRun(HeaderFooterPart part)
    {
        var map = new Dictionary<Run, ImagePart>();
        var next = 0;
        foreach (var paragraph in part.Content.Paragraphs)
            foreach (var run in paragraph.Runs)
                if (run.Image is not null && next < part.Images.Count)
                    map[run] = part.Images[next++];
        return map;
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

    private static XDocument BuildContentTypes(IReadOnlyList<string> imageExtensions, bool includeNumbering, IReadOnlyList<HeaderFooterPart> headerFooterParts, bool hasFootnotes, bool hasEndnotes, bool hasComments, bool hasWatermark, bool hasSettings, bool hasBibliography, IReadOnlyList<ChartPart> charts, bool hasEmbeddedObjects, IReadOnlyList<SmartArtPart> smartArts, bool hasEmbeddedFonts, IReadOnlyList<PreservedPart> preservedParts, IReadOnlyDictionary<string, string> preservedContentTypeDefaults) => new(
        new XElement(Ct + "Types",
            new XElement(Ct + "Default", new XAttribute("Extension", "rels"),
                new XAttribute("ContentType", "application/vnd.openxmlformats-package.relationships+xml")),
            new XElement(Ct + "Default", new XAttribute("Extension", "xml"),
                new XAttribute("ContentType", "application/xml")),
            // One image Default per extension a body or header/footer part actually carries (png/jpeg/gif/
            // bmp/tiff/emf/wmf). A PNG-only document emits exactly the single png Default as before.
            imageExtensions.Select(ext => new XElement(Ct + "Default",
                new XAttribute("Extension", ext),
                new XAttribute("ContentType", ImageContentTypeForExtension(ext)))),
            // One Default per extension a verbatim-preserved part needs that FreeW would not otherwise declare
            // (e.g. the png/emf media of a preserved chart, when the body carries no image of that kind). Skips
            // extensions FreeW already emits above (rels/xml/image/bin/xlsx/odttf) so nothing is duplicated.
            preservedContentTypeDefaults
                .Where(kvp => kvp.Key is not ("rels" or "xml" or "bin" or "xlsx" or "odttf")
                    && !imageExtensions.Contains(kvp.Key))
                .OrderBy(kvp => kvp.Key, StringComparer.Ordinal)
                .Select(kvp => new XElement(Ct + "Default",
                    new XAttribute("Extension", kvp.Key),
                    new XAttribute("ContentType", kvp.Value))),
            // A single Default for the bin extension covers every embedded OLE payload part.
            hasEmbeddedObjects
                ? new XElement(Ct + "Default", new XAttribute("Extension", "bin"),
                    new XAttribute("ContentType", OleObjectContentType))
                : null,
            // A single Default for the xlsx extension covers every chart's embedded companion workbook (F1).
            charts.Count > 0
                ? new XElement(Ct + "Default", new XAttribute("Extension", "xlsx"),
                    new XAttribute("ContentType", SpreadsheetContentType))
                : null,
            // A single Default for the odttf extension covers every obfuscated embedded-font part.
            hasEmbeddedFonts
                ? new XElement(Ct + "Default", new XAttribute("Extension", "odttf"),
                    new XAttribute("ContentType", ObfuscatedFontContentType))
                : null,
            new XElement(Ct + "Override", new XAttribute("PartName", "/word/document.xml"),
                new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml")),
            new XElement(Ct + "Override", new XAttribute("PartName", "/word/styles.xml"),
                new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml")),
            includeNumbering
                ? new XElement(Ct + "Override", new XAttribute("PartName", NumberingPartName),
                    new XAttribute("ContentType", NumberingContentType))
                : null,
            // One Override per emitted header/footer part (in collection order, which reproduces the legacy
            // header1, footer1, header2, footer2 ordering for single-section documents).
            headerFooterParts.Select(part => new XElement(Ct + "Override",
                new XAttribute("PartName", "/word/" + part.FileName),
                new XAttribute("ContentType", part.IsHeader ? HeaderContentType : FooterContentType))),
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
            // word/commentsExtended.xml (reply threading + resolved state) is emitted whenever comments are.
            hasComments
                ? new XElement(Ct + "Override", new XAttribute("PartName", CommentsExtendedPartName),
                    new XAttribute("ContentType", CommentsExtendedContentType))
                : null,
            hasSettings
                ? new XElement(Ct + "Override", new XAttribute("PartName", SettingsPartName),
                    new XAttribute("ContentType", SettingsContentType))
                : null,
            // word/bibliography/sources.xml declares the citation-sources store (b:Sources/@SelectedStyle).
            hasBibliography
                ? new XElement(Ct + "Override", new XAttribute("PartName", BibliographyPartName),
                    new XAttribute("ContentType", BibliographyContentType))
                : null,
            // word/fontTable.xml declares the embedded-font table (the .odttf parts use the odttf Default above).
            hasEmbeddedFonts
                ? new XElement(Ct + "Override", new XAttribute("PartName", FontTablePartName),
                    new XAttribute("ContentType", FontTableContentType))
                : null,
            // The theme part is always present (one per document).
            new XElement(Ct + "Override", new XAttribute("PartName", ThemePartName),
                new XAttribute("ContentType", ThemeContentType)),
            new XElement(Ct + "Override", new XAttribute("PartName", CorePropertiesPartName),
                new XAttribute("ContentType", CorePropertiesContentType)),
            hasWatermark
                ? new XElement(Ct + "Override", new XAttribute("PartName", CustomPropertiesPartName),
                    new XAttribute("ContentType", CustomPropertiesContentType))
                : null,
            // One Override per chart part declares the DrawingML chart content type.
            charts.Select(chart => new XElement(Ct + "Override",
                new XAttribute("PartName", "/word/charts/" + chart.FileName),
                new XAttribute("ContentType", ChartContentType))),
            // Five Overrides per SmartArt diagram declare the data / layout / quickStyle / colors content
            // types plus (F2) the rendered-geometry drawing part's content type.
            smartArts.SelectMany(s => new[]
            {
                new XElement(Ct + "Override",
                    new XAttribute("PartName", "/word/diagrams/" + s.DataFileName),
                    new XAttribute("ContentType", DiagramDataContentType)),
                new XElement(Ct + "Override",
                    new XAttribute("PartName", "/word/diagrams/" + s.LayoutFileName),
                    new XAttribute("ContentType", DiagramLayoutContentType)),
                new XElement(Ct + "Override",
                    new XAttribute("PartName", "/word/diagrams/" + s.QuickStyleFileName),
                    new XAttribute("ContentType", DiagramStyleContentType)),
                new XElement(Ct + "Override",
                    new XAttribute("PartName", "/word/diagrams/" + s.ColorsFileName),
                    new XAttribute("ContentType", DiagramColorsContentType)),
                new XElement(Ct + "Override",
                    new XAttribute("PartName", "/word/diagrams/" + s.DrawingFileName),
                    new XAttribute("ContentType", DiagramDrawingContentType))
            }),
            // One Override per preserved part that declares an Override content type (customXml items, their
            // props and webSettings). Parts covered by a Default (e.g. customXml/_rels/*.rels via the rels
            // Default) carry no Override, so they are skipped. Authored-from-scratch documents add none.
            preservedParts
                .Where(p => p.ContentTypeOverride is not null)
                .Select(p => new XElement(Ct + "Override",
                    new XAttribute("PartName", p.PartName),
                    new XAttribute("ContentType", p.ContentTypeOverride!)))));

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
        IReadOnlyList<HeaderFooterPart> headerFooterParts,
        bool hasFootnotes,
        bool hasEndnotes,
        bool hasComments,
        bool hasSettings,
        bool hasBibliography,
        IReadOnlyList<ChartPart> charts,
        IReadOnlyList<EmbeddedObjectPart> embeddedObjects,
        IReadOnlyList<SmartArtPart> smartArts,
        bool hasEmbeddedFonts,
        IReadOnlyList<PreservedPart> preservedParts)
    {
        var relationships = new XElement(Rel + "Relationships",
            new XElement(Rel + "Relationship",
                new XAttribute("Id", "rId1"),
                new XAttribute("Type", StylesRel),
                new XAttribute("Target", "styles.xml")));
        if (hasSettings)
            relationships.Add(new XElement(Rel + "Relationship",
                new XAttribute("Id", SettingsRelationshipId),
                new XAttribute("Type", SettingsRelType),
                new XAttribute("Target", "settings.xml")));
        // The document→bibliography relationship (word/bibliography/sources.xml, target relative to word/).
        if (hasBibliography)
            relationships.Add(new XElement(Rel + "Relationship",
                new XAttribute("Id", BibliographyRelationshipId),
                new XAttribute("Type", BibliographyRelType),
                new XAttribute("Target", "bibliography/sources.xml")));
        // The document→fontTable relationship (the fontTable's own rels reference the .odttf font parts).
        if (hasEmbeddedFonts)
            relationships.Add(new XElement(Rel + "Relationship",
                new XAttribute("Id", FontTableRelationshipId),
                new XAttribute("Type", FontTableRelType),
                new XAttribute("Target", "fontTable.xml")));
        // The theme part is always present, so its relationship is unconditional.
        relationships.Add(new XElement(Rel + "Relationship",
            new XAttribute("Id", ThemeRelationshipId),
            new XAttribute("Type", ThemeRelType),
            new XAttribute("Target", "theme/theme1.xml")));
        if (includeNumbering)
            relationships.Add(new XElement(Rel + "Relationship",
                new XAttribute("Id", "rIdNumbering"),
                new XAttribute("Type", NumberingRelType),
                new XAttribute("Target", "numbering.xml")));
        // One document relationship per emitted header/footer part (in collection order — which reproduces
        // the legacy header1, footer1, header2, footer2 ordering and rel ids for single-section documents).
        // The owning section's w:sectPr references the part by this relationship id.
        foreach (var part in headerFooterParts)
            relationships.Add(new XElement(Rel + "Relationship",
                new XAttribute("Id", part.RelationshipId),
                new XAttribute("Type", part.IsHeader ? HeaderRel : FooterRel),
                new XAttribute("Target", part.FileName)));
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
        {
            relationships.Add(new XElement(Rel + "Relationship",
                new XAttribute("Id", CommentsRelationshipId),
                new XAttribute("Type", CommentsRelType),
                new XAttribute("Target", "comments.xml")));
            relationships.Add(new XElement(Rel + "Relationship",
                new XAttribute("Id", CommentsExtendedRelationshipId),
                new XAttribute("Type", CommentsExtendedRelType),
                new XAttribute("Target", "commentsExtended.xml")));
        }
        foreach (var image in images)
            relationships.Add(new XElement(Rel + "Relationship",
                new XAttribute("Id", image.RelationshipId),
                new XAttribute("Type", ImageRel),
                new XAttribute("Target", "media/" + image.FileName)));
        foreach (var chart in charts)
            relationships.Add(new XElement(Rel + "Relationship",
                new XAttribute("Id", chart.RelationshipId),
                new XAttribute("Type", ChartRelType),
                new XAttribute("Target", "charts/" + chart.FileName)));
        // The embedded OLE payload relationship (the icon's image relationship is emitted in the images loop).
        foreach (var embedded in embeddedObjects)
            relationships.Add(new XElement(Rel + "Relationship",
                new XAttribute("Id", embedded.RelationshipId),
                new XAttribute("Type", OleObjectRelType),
                new XAttribute("Target", "embeddings/" + embedded.FileName)));
        // Each SmartArt diagram contributes four relationships (data / layout / quickStyle / colors), all
        // referenced together by the inline drawing's dgm:relIds.
        foreach (var s in smartArts)
        {
            relationships.Add(new XElement(Rel + "Relationship",
                new XAttribute("Id", s.DataRelationshipId),
                new XAttribute("Type", DiagramDataRelType),
                new XAttribute("Target", "diagrams/" + s.DataFileName)));
            relationships.Add(new XElement(Rel + "Relationship",
                new XAttribute("Id", s.LayoutRelationshipId),
                new XAttribute("Type", DiagramLayoutRelType),
                new XAttribute("Target", "diagrams/" + s.LayoutFileName)));
            relationships.Add(new XElement(Rel + "Relationship",
                new XAttribute("Id", s.QuickStyleRelationshipId),
                new XAttribute("Type", DiagramStyleRelType),
                new XAttribute("Target", "diagrams/" + s.QuickStyleFileName)));
            relationships.Add(new XElement(Rel + "Relationship",
                new XAttribute("Id", s.ColorsRelationshipId),
                new XAttribute("Type", DiagramColorsRelType),
                new XAttribute("Target", "diagrams/" + s.ColorsFileName)));
        }
        foreach (var (url, relationshipId) in hyperlinks)
            relationships.Add(new XElement(Rel + "Relationship",
                new XAttribute("Id", relationshipId),
                new XAttribute("Type", HyperlinkRel),
                new XAttribute("Target", url),
                new XAttribute("TargetMode", "External")));
        // One document relationship per preserved part that the document references directly (customXml items,
        // webSettings and unmodelled chart/chartex parts carry a RelationshipType; their props/_rels/media do
        // not, being referenced from the part's own _rels instead). The Target is reconstructed relative to
        // word/ (where document.xml lives), so a /word/* part targets its bare file name and a /customXml/* part
        // targets "../customXml/…". The id assigned here must match PreservedPartRelIds (consumed by the inline
        // drawing rewrite) — both replay the same order, so a shared helper keeps them in lock-step.
        foreach (var (part, relId) in PreservedPartRelIds(preservedParts))
            relationships.Add(new XElement(Rel + "Relationship",
                new XAttribute("Id", relId),
                new XAttribute("Type", part.RelationshipType!),
                new XAttribute("Target", DocumentRelativeTarget(part.PartName))));
        return new XDocument(relationships);
    }

    /// <summary>
    /// Assigns each document-referenced preserved part (those carrying a <see cref="PreservedPart.RelationshipType"/>)
    /// a deterministic <c>rIdPreserved{N}</c> in capture order, yielding (part, relId) pairs. Used by both
    /// <see cref="BuildDocumentRels"/> (to emit the relationship) and the inline preserved-drawing rewrite (to
    /// re-point the drawing's r:id at the same id), so the two never drift.
    /// </summary>
    private static IEnumerable<(PreservedPart Part, string RelId)> PreservedPartRelIds(IReadOnlyList<PreservedPart> preservedParts)
    {
        var index = 0;
        foreach (var part in preservedParts)
        {
            if (part.RelationshipType is null)
                continue;
            index++;
            yield return (part, $"rIdPreserved{index}");
        }
    }

    /// <summary>
    /// Reconstructs a preserved part's document-relationship Target (relative to <c>word/</c>, where
    /// document.xml and its rels live) from its absolute part name: a <c>/word/&lt;file&gt;</c> part targets its
    /// bare file name (e.g. <c>webSettings.xml</c>); any other part targets a path stepping up out of word/
    /// (e.g. <c>/customXml/item1.xml</c> → <c>../customXml/item1.xml</c>). Mirrors how the reader keyed the
    /// captured relationship type by the original Target.
    /// </summary>
    private static string DocumentRelativeTarget(string partName)
    {
        var path = partName.TrimStart('/');
        return path.StartsWith("word/", StringComparison.Ordinal)
            ? path["word/".Length..]
            : "../" + path;
    }

    private static XDocument BuildDocument(
        TextDocument document,
        IReadOnlyList<ImagePart> images,
        IReadOnlyList<ChartPart> charts,
        IReadOnlyList<EmbeddedObjectPart> embeddedObjects,
        IReadOnlyList<SmartArtPart> smartArts,
        IReadOnlyDictionary<string, string> hyperlinks,
        IReadOnlyList<HeaderFooterPart> headerFooterParts,
        PreservedNumberingPlan? preservedNumbering)
    {
        // Group header/footer parts by their owning section: the final/body-level section (Section == null)
        // feeds the body-level w:sectPr; each non-final section feeds its paragraph-level w:sectPr.
        var finalSectionParts = headerFooterParts.Where(p => p.Section is null).ToList();
        var partsBySection = headerFooterParts
            .Where(p => p.Section is not null)
            .GroupBy(p => p.Section!)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<HeaderFooterPart>)g.ToList());

        // Per-write id state, local to this BuildDocument invocation so concurrent writes never race.
        // Bookmark + revision ids start at 1; the shape docPr counter is seeded just above the image
        // drawing ids (1..imageCount) so the two id spaces never overlap. Each shape BuildRun emits takes
        // the next id.
        var ids = new IdAllocator(shapeDrawingSeed: images.Count);

        // Map each image/chart run to its assigned part by replaying the same walk order CollectImages /
        // CollectCharts used, so document.xml and the rels agree on which rId belongs to which run.
        var imagesByRun = new Dictionary<Run, ImagePart>();
        var chartsByRun = new Dictionary<Run, ChartPart>();
        var embeddedByRun = new Dictionary<Run, EmbeddedObjectPart>();
        var smartArtsByRun = new Dictionary<Run, SmartArtPart>();
        var nextImage = 0;
        var nextChart = 0;
        var nextEmbedded = 0;
        var nextSmartArt = 0;
        foreach (var paragraph in EnumerateParagraphs(document))
            foreach (var run in paragraph.Runs)
            {
                if (run.Image is not null)
                    imagesByRun[run] = images[nextImage++];
                if (run.Chart is not null)
                    chartsByRun[run] = charts[nextChart++];
                if (run.EmbeddedObject is not null)
                    embeddedByRun[run] = embeddedObjects[nextEmbedded++];
                if (run.SmartArt is not null)
                    smartArtsByRun[run] = smartArts[nextSmartArt++];
            }

        // Map each document-referenced preserved part to its assigned rIdPreserved{N} (same order/ids as
        // BuildDocumentRels), so a verbatim-preserved inline drawing can re-point its chart r:id at the
        // re-emitted relationship. Empty for documents with no preserved drawings.
        var preservedDrawingRelIds = PreservedPartRelIds(document.Preserved.Parts)
            .ToDictionary(pair => pair.Part.PartName, pair => pair.RelId, StringComparer.Ordinal);

        var drawings = new RunDrawings(imagesByRun, chartsByRun, embeddedByRun, smartArtsByRun, ids, preservedDrawingRelIds);

        var body = new XElement(W + "body");
        foreach (var block in document.Blocks)
            body.Add(BuildBlock(block, drawings, hyperlinks, partsBySection, preservedNumbering));
        body.Add(BuildSectionProperties(document.Page, finalSectionParts));

        // Page background colour (w:background): it is positionally the FIRST child of w:document, before
        // w:body. Emitted only when a background colour is set so existing documents are unaffected.
        var background = document.Page.BackgroundColorHex is { Length: > 0 } bg
            ? new XElement(W + "background", new XAttribute(W + "color", bg.TrimStart('#')))
            : null;

        return new XDocument(
            new XElement(W + "document",
                new XAttribute(XNamespace.Xmlns + "w", W.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "r", R.NamespaceName),
                // w14 carries the checkbox content control element (w14:checkbox in a w:sdtPr).
                new XAttribute(XNamespace.Xmlns + "w14", W14.NamespaceName),
                // m carries inline equations (m:oMath and its children).
                new XAttribute(XNamespace.Xmlns + "m", M.NamespaceName),
                // wp/a/wps carry inline DrawingML shapes & text boxes (w:drawing/wp:inline/.../wps:wsp).
                new XAttribute(XNamespace.Xmlns + "wp", Wp.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "a", A.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "wps", Wps.NamespaceName),
                // v/o carry a classic embedded OLE object's VML presentation (w:object/v:shape/o:OLEObject).
                new XAttribute(XNamespace.Xmlns + "v", V.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "o", O.NamespaceName),
                // dgm carries the SmartArt diagram reference (w:drawing/.../a:graphicData/dgm:relIds).
                new XAttribute(XNamespace.Xmlns + "dgm", Dgm.NamespaceName),
                // w:background (page background colour) must precede w:body in the document schema order.
                background,
                body));
    }

    /// <summary>
    /// Per-write id state, created once at the top of each <see cref="BuildDocument"/> (and once per
    /// independent part such as a header/footer/footnote/text-box) so concurrent <see cref="Write(TextDocument, Stream)"/>
    /// calls are fully isolated — there is NO shared mutable static counter state. Bookmark and revision
    /// (w:ins/w:del) ids are document-scoped monotonic counters starting at 1; the inline-shape wp:docPr
    /// counter is seeded just above the image drawing ids (1..imageCount) so shape ids never collide with
    /// image ids. All three were previously static fields shared across writes (the concurrency defect).
    /// </summary>
    private sealed class IdAllocator
    {
        private int _bookmarkId;
        private int _revisionId;
        private int _shapeDrawingId;

        public IdAllocator(int shapeDrawingSeed = 0) => _shapeDrawingId = shapeDrawingSeed;

        public int NextBookmarkId() => ++_bookmarkId;
        public int NextRevisionId() => ++_revisionId;
        public int NextShapeDrawingId() => ++_shapeDrawingId;
    }

    /// <summary>
    /// Bundles the per-run image and chart part maps plus the per-write <see cref="IdAllocator"/> so the
    /// run builders can resolve any inline drawing and allocate document-scoped ids from one parameter
    /// (rather than threading dictionaries and counters through every helper). <see cref="Empty"/> builds a
    /// fresh instance — with its own allocator — for header/footer/footnote/text-box paragraphs whose runs
    /// never carry body drawings; it is a factory (not a shared static) so those builds stay isolated too.
    /// </summary>
    private sealed record RunDrawings(
        IReadOnlyDictionary<Run, ImagePart> Images,
        IReadOnlyDictionary<Run, ChartPart> Charts,
        IReadOnlyDictionary<Run, EmbeddedObjectPart> EmbeddedObjects,
        IReadOnlyDictionary<Run, SmartArtPart> SmartArts,
        IdAllocator Ids,
        IReadOnlyDictionary<string, string>? PreservedDrawingRelIds = null)
    {
        public static RunDrawings Empty() => new(
            new Dictionary<Run, ImagePart>(),
            new Dictionary<Run, ChartPart>(),
            new Dictionary<Run, EmbeddedObjectPart>(),
            new Dictionary<Run, SmartArtPart>(),
            new IdAllocator());
    }

    /// <summary>
    /// Builds a header (w:hdr) or footer (w:ftr) part from its model paragraphs. When the part carries inline
    /// images, each image run emits a w:drawing whose r:embed references a PART-LOCAL relationship id (resolved
    /// against the part's own word/_rels/&lt;part&gt;.xml.rels, see <see cref="BuildHeaderFooterRels"/>) and the
    /// drawing namespaces (wp/a/pic) are declared on the root. An image-less header/footer declares only w/r
    /// (so it stays byte-equivalent to the historical output).
    /// </summary>
    private static XDocument BuildHeaderFooter(XName rootName, HeaderFooterPart part)
    {
        var root = new XElement(rootName,
            new XAttribute(XNamespace.Xmlns + "w", W.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "r", R.NamespaceName));

        // Header/footer runs may carry inline images; their part-local image parts are mapped here. They never
        // carry charts/embedded objects/SmartArt/document hyperlinks (those walks target the body), so those
        // maps stay empty. The IdAllocator is part-local so drawing ids are isolated from the body.
        var hasImages = part.Images.Count > 0;
        if (hasImages)
        {
            // The picture drawing uses the wp/a/pic namespaces; declare them on the root when images exist.
            root.Add(new XAttribute(XNamespace.Xmlns + "wp", Wp.NamespaceName));
            root.Add(new XAttribute(XNamespace.Xmlns + "a", A.NamespaceName));
            root.Add(new XAttribute(XNamespace.Xmlns + "pic", Pic.NamespaceName));
        }

        var imagesByRun = hasImages ? BuildHeaderFooterImagesByRun(part) : new Dictionary<Run, ImagePart>();
        var drawings = RunDrawings.Empty() with { Images = imagesByRun };
        var noHyperlinks = new Dictionary<string, string>(StringComparer.Ordinal);

        if (part.Content.Paragraphs.Count == 0)
            root.Add(new XElement(W + "p"));
        else
            foreach (var paragraph in part.Content.Paragraphs)
                root.Add(BuildParagraph(paragraph, drawings, noHyperlinks));

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
        var noDrawings = RunDrawings.Empty();
        var noHyperlinks = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var footnote in document.Footnotes.Values.OrderBy(f => f.Id))
        {
            var element = new XElement(W + "footnote", new XAttribute(W + "id", footnote.Id));
            if (footnote.Content.Count == 0)
                element.Add(new XElement(W + "p"));
            else
                foreach (var paragraph in footnote.Content)
                    element.Add(BuildParagraph(paragraph, noDrawings, noHyperlinks));
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
        var noDrawings = RunDrawings.Empty();
        var noHyperlinks = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var endnote in document.Endnotes.Values.OrderBy(e => e.Id))
        {
            var element = new XElement(W + "endnote", new XAttribute(W + "id", endnote.Id));
            if (endnote.Content.Count == 0)
                element.Add(new XElement(W + "p"));
            else
                foreach (var paragraph in endnote.Content)
                    element.Add(BuildParagraph(paragraph, noDrawings, noHyperlinks));
            endnotes.Add(element);
        }

        return new XDocument(endnotes);
    }

    /// <summary>
    /// Collects the inline images carried by ALL comment paragraphs (in comment-id then paragraph then run
    /// order), assigning each a PART-LOCAL relationship id (<c>rIdImgN</c>, resolved against
    /// <c>word/_rels/comments.xml.rels</c>) and a media file name (<c>comment_imageN.ext</c>) so comment media
    /// never clashes with body/header/footer media. Mirrors <see cref="CollectHeaderFooterImages"/>. Empty when
    /// no comment carries an image — so a text-only-comment document emits no comment media or rels.
    /// </summary>
    private static List<ImagePart> CollectCommentImages(TextDocument document)
    {
        var images = new List<ImagePart>();
        foreach (var comment in FlattenComments(document))
            foreach (var paragraph in comment.Content)
                foreach (var run in paragraph.Runs)
                    if (run.Image is { } image)
                    {
                        var index = images.Count + 1;
                        images.Add(new ImagePart(image, $"rIdImg{index}", $"comment_image{index}.{InlineImage.ExtensionFor(image.Format)}", (uint)index));
                    }
        return images;
    }

    /// <summary>Maps each image run across all comment paragraphs to its <see cref="ImagePart"/> (same walk as collection).</summary>
    private static Dictionary<Run, ImagePart> BuildCommentImagesByRun(TextDocument document, IReadOnlyList<ImagePart> commentImages)
    {
        var map = new Dictionary<Run, ImagePart>();
        var next = 0;
        foreach (var comment in FlattenComments(document))
            foreach (var paragraph in comment.Content)
                foreach (var run in paragraph.Runs)
                    if (run.Image is not null && next < commentImages.Count)
                        map[run] = commentImages[next++];
        return map;
    }

    /// <summary>
    /// Flattens the document's comments — every top-level comment followed by its replies — in a stable
    /// (ascending top-level id, then thread order) order. This is the single walk that comments.xml,
    /// commentsExtended.xml and the comment-image collection all share, so every flat <c>w:comment</c>
    /// entry, its w15:commentEx and any comment media stay in agreement. Replies serialise as ordinary
    /// flat comments; their thread shape lives only in commentsExtended.xml.
    /// </summary>
    private static IEnumerable<Comment> FlattenComments(TextDocument document) =>
        document.Comments.Values.OrderBy(c => c.Id).SelectMany(c => c.ThreadInOrder());

    /// <summary>
    /// A stable 8-hex-digit w14:paraId for a comment's last paragraph, derived deterministically from the
    /// comment id (so the writer never stamps random ids). Comment paraIds live in their own high range
    /// (0x10000000+) clear of any body paraId, and are the values commentsExtended.xml threads on.
    /// </summary>
    private static string CommentParaId(int commentId) =>
        (0x10000000 + commentId).ToString("X8", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// Builds word/comments.xml (w:comments): one w:comment w:id="N" per modelled comment (ascending
    /// id), each carrying w:author / w:initials and — when set — an explicit w:date, plus the comment's
    /// paragraphs. The date is only emitted when the model carries one, keeping the writer deterministic.
    /// Any inline images a comment carries are emitted as part-local <c>w:drawing</c>s resolved against
    /// <paramref name="commentImages"/> (their media + <c>comments.xml.rels</c> are written by <see cref="Write(TextDocument, Stream)"/>),
    /// so comment-part images round-trip referenced rather than orphaned.
    /// </summary>
    private static XDocument BuildComments(TextDocument document, IReadOnlyList<ImagePart> commentImages)
    {
        var comments = new XElement(W + "comments",
            new XAttribute(XNamespace.Xmlns + "w", W.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "r", R.NamespaceName));

        // Comment images map their runs → part-local image parts; the picture drawing uses the wp/a/pic
        // namespaces, declared on the root only when a comment actually carries an image (so a text-only
        // comments part stays byte-equivalent to the historical output). Comments carry no hyperlinks.
        if (commentImages.Count > 0)
        {
            comments.Add(new XAttribute(XNamespace.Xmlns + "wp", Wp.NamespaceName));
            comments.Add(new XAttribute(XNamespace.Xmlns + "a", A.NamespaceName));
            comments.Add(new XAttribute(XNamespace.Xmlns + "pic", Pic.NamespaceName));
        }
        var imagesByRun = commentImages.Count > 0
            ? BuildCommentImagesByRun(document, commentImages)
            : new Dictionary<Run, ImagePart>();
        var drawings = RunDrawings.Empty() with { Images = imagesByRun };
        var noHyperlinks = new Dictionary<string, string>(StringComparer.Ordinal);

        // The w14 namespace is declared on the root only when a comment paragraph carries a paraId (i.e.
        // always, since every comment's last paragraph is stamped for commentsExtended threading). Mirrors
        // how the wp/a/pic namespaces are added only when needed.
        comments.Add(new XAttribute(XNamespace.Xmlns + "w14", W14.NamespaceName));

        // Parent and replies serialise as flat w:comment entries (FlattenComments order). Each comment's
        // LAST paragraph is stamped with a w14:paraId — the value commentsExtended.xml threads on.
        foreach (var comment in FlattenComments(document))
        {
            var element = new XElement(W + "comment",
                new XAttribute(W + "id", comment.Id),
                new XAttribute(W + "author", comment.Author),
                new XAttribute(W + "initials", comment.Initials));
            if (comment.DateXml is { Length: > 0 } date)
                element.Add(new XAttribute(W + "date", date));

            var paraId = CommentParaId(comment.Id);
            if (comment.Content.Count == 0)
                element.Add(new XElement(W + "p", new XAttribute(W14 + "paraId", paraId)));
            else
                for (var pi = 0; pi < comment.Content.Count; pi++)
                {
                    var built = BuildParagraph(comment.Content[pi], drawings, noHyperlinks);
                    // commentsExtended.xml references the comment's LAST paragraph; stamp paraId there.
                    if (pi == comment.Content.Count - 1)
                        built.SetAttributeValue(W14 + "paraId", paraId);
                    element.Add(built);
                }
            comments.Add(element);
        }

        return new XDocument(comments);
    }

    /// <summary>
    /// Builds word/commentsExtended.xml (w15:commentsEx): one w15:commentEx per flat comment (FlattenComments
    /// order), each carrying its w15:paraId (the comment's last-paragraph paraId). A reply also carries
    /// w15:paraIdParent (its parent comment's paraId), threading it under the parent; a resolved top-level
    /// comment carries w15:done="1". This is the part Word reads to reconstruct reply threads + resolved state.
    /// </summary>
    private static XDocument BuildCommentsExtended(TextDocument document)
    {
        var root = new XElement(W15 + "commentsEx",
            new XAttribute(XNamespace.Xmlns + "w15", W15.NamespaceName));

        foreach (var parent in document.Comments.Values.OrderBy(c => c.Id))
        {
            var parentParaId = CommentParaId(parent.Id);
            var parentEx = new XElement(W15 + "commentEx",
                new XAttribute(W15 + "paraId", parentParaId));
            if (parent.Resolved)
                parentEx.Add(new XAttribute(W15 + "done", "1"));
            root.Add(parentEx);

            foreach (var reply in parent.Replies)
            {
                var replyEx = new XElement(W15 + "commentEx",
                    new XAttribute(W15 + "paraId", CommentParaId(reply.Id)),
                    new XAttribute(W15 + "paraIdParent", parentParaId));
                // A resolved thread marks done on every entry in the thread (Word's behaviour).
                if (parent.Resolved)
                    replyEx.Add(new XAttribute(W15 + "done", "1"));
                root.Add(replyEx);
            }
        }

        return new XDocument(root);
    }

    /// <summary>Builds word/_rels/comments.xml.rels: one image relationship per comment media part.</summary>
    private static XDocument BuildCommentsRels(IReadOnlyList<ImagePart> commentImages)
    {
        var relationships = new XElement(Rel + "Relationships");
        foreach (var image in commentImages)
            relationships.Add(new XElement(Rel + "Relationship",
                new XAttribute("Id", image.RelationshipId),
                new XAttribute("Type", ImageRel),
                new XAttribute("Target", "media/" + image.FileName)));
        return new XDocument(relationships);
    }

    private static XElement BuildBlock(
        Block block,
        RunDrawings drawings,
        IReadOnlyDictionary<string, string> hyperlinks,
        IReadOnlyDictionary<Section, IReadOnlyList<HeaderFooterPart>> partsBySection,
        PreservedNumberingPlan? preservedNumbering = null) => block switch
    {
        Table table => BuildTable(table, drawings, hyperlinks, preservedNumbering),
        // Only top-level body paragraphs can end a non-final section, so the per-section header/footer map
        // is threaded here (and nowhere else); table-cell/header/footer/footnote paragraphs pass no map.
        Paragraph paragraph => BuildParagraph(paragraph, drawings, hyperlinks, partsBySection, preservedNumbering),
        _ => new XElement(W + "p")
    };

    // Light fills used by the table-style toggles: a blue-grey header fill and a grey banded-row fill.
    // These are emitted as cell shading on write so the styled docx renders correctly in Word; the
    // HeaderRow/BandedRows flags themselves round-trip via w:tblLook (see BuildTableProperties).
    private const string HeaderFill = "D9E2F3";
    private const string BandedFill = "F2F2F2";

    private static XElement BuildTable(Table table, RunDrawings drawings, IReadOnlyDictionary<string, string> hyperlinks, PreservedNumberingPlan? preservedNumbering = null)
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

        var fmt = table.Formatting;
        for (var rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
        {
            var row = table.Rows[rowIndex];
            var isHeaderRow = fmt.HeaderRow && rowIndex == 0;
            // Banded rows shade alternate body rows. With a header, body banding starts below the header,
            // so we band every other body row (the second body row, fourth, ...).
            var bandedShade = fmt.BandedRows && !isHeaderRow && IsBandedBodyRow(rowIndex, fmt.HeaderRow);

            var tr = new XElement(W + "tr");
            // Repeat the header row across page breaks (w:trPr/w:tblHeader) when requested.
            if (isHeaderRow && fmt.RepeatHeaderRow)
                tr.Add(new XElement(W + "trPr", new XElement(W + "tblHeader")));

            foreach (var cell in row.Cells)
            {
                var tc = new XElement(W + "tc");
                // The cell's own shading wins; header/banded fills only apply to otherwise-unshaded cells.
                var effectiveShade = cell.ShadingColorHex is { Length: > 0 }
                    ? null
                    : isHeaderRow ? HeaderFill : bandedShade ? BandedFill : null;
                var tcPr = BuildCellProperties(cell, effectiveShade);
                if (tcPr is not null)
                    tc.Add(tcPr);
                if (cell.Paragraphs.Count == 0)
                    tc.Add(new XElement(W + "p"));
                else
                    foreach (var paragraph in cell.Paragraphs)
                        tc.Add(BuildParagraph(isHeaderRow ? BoldHeaderParagraph(paragraph) : paragraph, drawings, hyperlinks, preservedNumbering: preservedNumbering));
                tr.Add(tc);
            }
            tbl.Add(tr);
        }
        return tbl;
    }

    /// <summary>
    /// True when the body row at <paramref name="rowIndex"/> should be banded (shaded). Body rows are
    /// counted from the first non-header row; every other body row (the 2nd, 4th, ...) is shaded, so the
    /// header (or first row) stays unshaded and banding alternates beneath it.
    /// </summary>
    private static bool IsBandedBodyRow(int rowIndex, bool hasHeader)
    {
        var bodyIndex = hasHeader ? rowIndex - 1 : rowIndex;
        return bodyIndex >= 0 && bodyIndex % 2 == 1;
    }

    /// <summary>
    /// Returns a copy of <paramref name="paragraph"/> with every run forced bold, used to render a
    /// header-row cell's text bold without mutating the model. Non-text runs (images/fields) are copied
    /// with their marks preserved; only the run formatting's Bold flag is overridden.
    ///
    /// Runs that carry an inline drawing (image/chart/embedded object/SmartArt/preserved drawing) are kept
    /// as the ORIGINAL instance, not cloned: <see cref="BuildRun"/> resolves a drawing's media part from the
    /// per-write maps (imagesByRun/chartsByRun/…) keyed by run REFERENCE, so a clone would miss the lookup
    /// and the drawing would be silently dropped. Bolding is meaningless for a drawing-only run anyway, so
    /// preserving identity here costs nothing and keeps a header-row cell image (etc.) round-tripping.
    /// </summary>
    private static Paragraph BoldHeaderParagraph(Paragraph paragraph)
    {
        var copy = new Paragraph
        {
            Formatting = paragraph.Formatting,
            StyleId = paragraph.StyleId,
            BookmarkName = paragraph.BookmarkName
        };
        foreach (var run in paragraph.Runs)
        {
            if (run.Image is not null || run.Chart is not null || run.EmbeddedObject is not null
                || run.SmartArt is not null || run.PreservedDrawing is not null)
            {
                copy.Runs.Add(run);
                continue;
            }
            copy.Runs.Add(new Run(run.Text, run.Formatting with { Bold = true })
            {
                Image = run.Image,
                Equation = run.Equation,
                Chart = run.Chart,
                HyperlinkUrl = run.HyperlinkUrl,
                HyperlinkAnchor = run.HyperlinkAnchor,
                HyperlinkTooltip = run.HyperlinkTooltip,
                FieldKind = run.FieldKind,
                FootnoteId = run.FootnoteId,
                EndnoteId = run.EndnoteId,
                CommentId = run.CommentId,
                IsCommentReference = run.IsCommentReference,
                Revision = run.Revision,
                RevisionAuthor = run.RevisionAuthor,
                RevisionDateXml = run.RevisionDateXml,
                Control = run.Control
            });
        }
        return copy;
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

        // w:tblLook carries the table-style toggles so they round-trip without a full table-style part:
        // w:firstRow="1" persists HeaderRow; w:noHBand="0" persists BandedRows (banding on). The flags are
        // recovered on read from these attributes (see DocxReader.ReadTable). Only emitted when a toggle
        // is set, so plain tables stay unchanged.
        var fmt = table.Formatting;
        if (fmt.HeaderRow || fmt.BandedRows)
        {
            tblPr.Add(new XElement(W + "tblLook",
                new XAttribute(W + "firstRow", fmt.HeaderRow ? "1" : "0"),
                new XAttribute(W + "lastRow", "0"),
                new XAttribute(W + "firstColumn", "0"),
                new XAttribute(W + "lastColumn", "0"),
                new XAttribute(W + "noHBand", fmt.BandedRows ? "0" : "1"),
                new XAttribute(W + "noVBand", "1")));
        }
        return tblPr;
    }

    // Cell properties (w:tcPr): emitted only when the cell has an explicit width, span, vertical-merge
    // state and/or shading, so plain cells stay unchanged. Width is w:tcW (dxa); horizontal merge is
    // w:gridSpan; vertical merge is w:vMerge ("restart" on the top cell, "continue" below); shading
    // mirrors paragraph w:shd (fill colour). Child order follows the CT_TcPr schema sequence.
    // <paramref name="overrideShade"/> is a header/banded fill (RRGGBB, no '#') applied when the cell has
    // no shading of its own; the cell's explicit ShadingColorHex always takes precedence.
    private static XElement? BuildCellProperties(TableCell cell, string? overrideShade = null)
    {
        var tcPr = new XElement(W + "tcPr");
        if (cell.WidthPt is { } widthPt)
            tcPr.Add(new XElement(W + "tcW",
                new XAttribute(W + "w", PointsToDxa(widthPt)),
                new XAttribute(W + "type", "dxa")));
        if (cell.GridSpan > 1)
            tcPr.Add(new XElement(W + "gridSpan", new XAttribute(W + "val", cell.GridSpan)));
        if (cell.VerticalMerge == VerticalMergeState.Restart)
            tcPr.Add(new XElement(W + "vMerge", new XAttribute(W + "val", "restart")));
        else if (cell.VerticalMerge == VerticalMergeState.Continue)
            tcPr.Add(new XElement(W + "vMerge", new XAttribute(W + "val", "continue")));
        var fill = cell.ShadingColorHex is { Length: > 0 } shading ? shading.TrimStart('#') : overrideShade;
        if (fill is { Length: > 0 })
            tcPr.Add(new XElement(W + "shd",
                new XAttribute(W + "val", "clear"),
                new XAttribute(W + "color", "auto"),
                new XAttribute(W + "fill", fill)));
        return tcPr.HasElements ? tcPr : null;
    }

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
    private static XElement NewRevisionWrapper(Run run, IdAllocator ids)
    {
        var name = run.Revision == RevisionKind.Deleted ? "del" : "ins";
        var wrapper = new XElement(W + name,
            new XAttribute(W + "id", ids.NextRevisionId()));
        if (run.RevisionAuthor is { Length: > 0 } author)
            wrapper.Add(new XAttribute(W + "author", author));
        if (run.RevisionDateXml is { Length: > 0 } date)
            wrapper.Add(new XAttribute(W + "date", date));
        return wrapper;
    }

    /// <summary>
    /// Builds the w:sdtPr (content-control properties) for a content control. Emits w:tag / w:alias when
    /// set, then the control-kind element: w:text for a plain-text control; a w14:checkbox carrying the
    /// checked state (w14:checked val="1"/"0") for a checkbox; w:richText for a rich-text control; a
    /// w:date carrying the w:dateFormat for a date picker; or a w:dropDownList / w:comboBox carrying a
    /// w:listItem (w:displayText/w:value) per choice for a list control. This is the minimal valid shape
    /// FreeW's own reader recovers (see <see cref="DocxReader"/>).
    /// </summary>
    private static XElement BuildSdtProperties(ContentControl control)
    {
        var sdtPr = new XElement(W + "sdtPr");
        if (control.Alias is { Length: > 0 } alias)
            sdtPr.Add(new XElement(W + "alias", new XAttribute(W + "val", alias)));
        if (control.Tag is { Length: > 0 } tag)
            sdtPr.Add(new XElement(W + "tag", new XAttribute(W + "val", tag)));
        switch (control.Kind)
        {
            case ContentControlKind.CheckBox:
                sdtPr.Add(new XElement(W14 + "checkbox",
                    new XElement(W14 + "checked", new XAttribute(W14 + "val", control.Checked ? "1" : "0"))));
                break;
            case ContentControlKind.RichText:
                sdtPr.Add(new XElement(W + "richText"));
                break;
            case ContentControlKind.DatePicker:
                sdtPr.Add(new XElement(W + "date",
                    new XElement(W + "dateFormat",
                        new XAttribute(W + "val", control.DateFormat ?? ContentControl.DefaultDateFormat))));
                break;
            case ContentControlKind.DropDownList:
                sdtPr.Add(BuildListElement(W + "dropDownList", control.Items));
                break;
            case ContentControlKind.ComboBox:
                sdtPr.Add(BuildListElement(W + "comboBox", control.Items));
                break;
            default:
                sdtPr.Add(new XElement(W + "text"));
                break;
        }
        return sdtPr;
    }

    /// <summary>
    /// Builds the w:dropDownList / w:comboBox element for a list content control: a w:listItem
    /// (w:displayText + w:value) for each <paramref name="items"/> choice.
    /// </summary>
    private static XElement BuildListElement(XName listName, IReadOnlyList<ContentControlListItem> items)
    {
        var list = new XElement(listName);
        foreach (var item in items)
            list.Add(new XElement(W + "listItem",
                new XAttribute(W + "displayText", item.DisplayText),
                new XAttribute(W + "value", item.Value)));
        return list;
    }

    private static XElement BuildParagraph(
        Paragraph paragraph,
        RunDrawings drawings,
        IReadOnlyDictionary<string, string> hyperlinks,
        IReadOnlyDictionary<Section, IReadOnlyList<HeaderFooterPart>>? partsBySection = null,
        PreservedNumberingPlan? preservedNumbering = null)
    {
        var p = new XElement(W + "p");
        var pPr = BuildParagraphProperties(paragraph, partsBySection, preservedNumbering);
        if (pPr is not null)
            p.Add(pPr);

        // A bookmarked paragraph is bracketed by a w:bookmarkStart/w:bookmarkEnd pair (siblings of the
        // runs) sharing one w:id; the start also carries the bookmark's w:name.
        var bookmarkId = -1;
        if (paragraph.BookmarkName is { Length: > 0 } bookmarkName)
        {
            bookmarkId = drawings.Ids.NextBookmarkId();
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
                revisionWrapper = NewRevisionWrapper(run, drawings.Ids);
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
                    content.Add(BuildRun(runs[i++], drawings));
                var sdt = new XElement(W + "sdt", BuildSdtProperties(control), content);
                Content(head, sdt);
                continue;
            }

            var url = runs[i].HyperlinkUrl;
            var anchor = runs[i].HyperlinkAnchor;
            var tooltip = runs[i].HyperlinkTooltip;
            if (url is { Length: > 0 } && hyperlinks.TryGetValue(url, out var relationshipId))
            {
                var hyperlink = new XElement(W + "hyperlink", new XAttribute(R + "id", relationshipId));
                if (tooltip is { Length: > 0 })
                    hyperlink.Add(new XAttribute(W + "tooltip", tooltip));
                var head = runs[i];
                while (i < runs.Count && runs[i].HyperlinkUrl == url && runs[i].HyperlinkTooltip == tooltip && (runs[i].IsCommentReference ? null : runs[i].CommentId) == openCommentId && SameRevision(head, runs[i]))
                    hyperlink.Add(BuildRun(runs[i++], drawings));
                Content(head, hyperlink);
            }
            else if (anchor is { Length: > 0 })
            {
                var hyperlink = new XElement(W + "hyperlink", new XAttribute(W + "anchor", anchor));
                if (tooltip is { Length: > 0 })
                    hyperlink.Add(new XAttribute(W + "tooltip", tooltip));
                var head = runs[i];
                while (i < runs.Count && runs[i].HyperlinkAnchor == anchor && runs[i].HyperlinkTooltip == tooltip && (runs[i].IsCommentReference ? null : runs[i].CommentId) == openCommentId && SameRevision(head, runs[i]))
                    hyperlink.Add(BuildRun(runs[i++], drawings));
                Content(head, hyperlink);
            }
            else
            {
                var run = runs[i++];
                Content(run, BuildRun(run, drawings));
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

    private static XElement? BuildParagraphProperties(
        Paragraph paragraph,
        IReadOnlyDictionary<Section, IReadOnlyList<HeaderFooterPart>>? partsBySection = null,
        PreservedNumberingPlan? preservedNumbering = null)
    {
        var pPr = new XElement(W + "pPr");
        if (!string.IsNullOrEmpty(paragraph.StyleId))
            pPr.Add(new XElement(W + "pStyle", new XAttribute(W + "val", paragraph.StyleId)));

        var f = paragraph.Formatting;
        // Flow control toggles, in CT_PPr schema order: keepNext, keepLines, pageBreakBefore,
        // widowControl. Each is a toggle element emitted only when its model flag is set, mirroring how
        // w:pageBreakBefore round-trips.
        // Keep this paragraph on the same page as the next (w:keepNext).
        if (f.KeepWithNext)
            pPr.Add(new XElement(W + "keepNext"));
        // Keep all lines of this paragraph together on one page (w:keepLines).
        if (f.KeepLinesTogether)
            pPr.Add(new XElement(W + "keepLines"));
        // Force a page break before this paragraph (w:pageBreakBefore); Word honours it when paginating.
        if (f.PageBreakBefore)
            pPr.Add(new XElement(W + "pageBreakBefore"));
        // Widow/orphan control (w:widowControl); only emitted when enabled (FreeW defaults it off).
        if (f.WidowControl)
            pPr.Add(new XElement(W + "widowControl"));
        // Right-to-left paragraph direction (w:bidi); emitted only when set, like the other pPr toggles.
        if (f.Rtl)
            pPr.Add(new XElement(W + "bidi"));
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
        // FreeW did not model this paragraph as a list, but it carried an original w:numPr against a
        // numbering definition FreeW could not represent. Re-emit it pointing at the preserved definition's
        // REMAPPED numId (disjoint from FreeW's fixed ids), keeping the original ilvl. Only when a merge plan
        // exists and it actually remapped this numId (a numPr referencing a missing w:num is dropped, as before).
        else if (preservedNumbering is not null
            && paragraph.PreservedNumbering is { } pn
            && preservedNumbering.NumIdRemap.TryGetValue(pn.NumId, out var mappedNumId))
        {
            pPr.Add(new XElement(W + "numPr",
                new XElement(W + "ilvl", new XAttribute(W + "val", pn.Ilvl)),
                new XElement(W + "numId", new XAttribute(W + "val", mappedNumId))));
        }
        if (f.Alignment != TextAlignment.Left)
            pPr.Add(new XElement(W + "jc", new XAttribute(W + "val", f.Alignment switch
            {
                TextAlignment.Center => "center",
                TextAlignment.Right => "right",
                TextAlignment.Justify => "both",
                _ => "left"
            })));
        // Tab stops (w:tabs): one w:tab per stop, position in dxa, alignment via w:val, and an
        // optional w:leader fill. Mirrors how w:ind/w:spacing carry their dxa values.
        if (f.TabStops.Count > 0)
            pPr.Add(new XElement(W + "tabs",
                f.TabStops.Select(BuildTabStop)));
        // Suppress automatic hyphenation for this paragraph (w:suppressAutoHyphens); emitted only when set,
        // like the other pPr toggles. In CT_PPr schema order it follows w:tabs.
        if (f.SuppressAutoHyphens)
            pPr.Add(new XElement(W + "suppressAutoHyphens"));
        // w:spacing carries before/after AND line spacing. Line spacing is emitted only when it differs
        // from the model default (a multiple of 1.15), so paragraphs with inherited/default spacing stay
        // byte-unchanged; explicit single/1.5/double (auto) and exact/atLeast heights round-trip.
        var hasLineSpacing = f.LineRule != LineSpacingRule.Multiple
            || System.Math.Abs(f.LineSpacing - ParagraphFormatting.Default.LineSpacing) > 0.0001;
        if (f.SpaceBeforePt > 0 || f.SpaceAfterPt > 0 || hasLineSpacing)
        {
            var spacingEl = new XElement(W + "spacing");
            if (f.SpaceBeforePt > 0 || f.SpaceAfterPt > 0)
            {
                spacingEl.Add(new XAttribute(W + "before", PointsToDxa(f.SpaceBeforePt)));
                spacingEl.Add(new XAttribute(W + "after", PointsToDxa(f.SpaceAfterPt)));
            }
            if (hasLineSpacing)
            {
                var (line, rule) = f.LineRule switch
                {
                    LineSpacingRule.Exact => ((int)System.Math.Round(f.LineHeightPt * 20), "exact"),
                    LineSpacingRule.AtLeast => ((int)System.Math.Round(f.LineHeightPt * 20), "atLeast"),
                    _ => ((int)System.Math.Round(f.LineSpacing * 240), "auto")
                };
                spacingEl.Add(new XAttribute(W + "line", line));
                spacingEl.Add(new XAttribute(W + "lineRule", rule));
            }
            pPr.Add(spacingEl);
        }
        if (f.IndentLeftPt > 0 || f.IndentRightPt > 0 || f.FirstLineIndentPt > 0)
            pPr.Add(new XElement(W + "ind",
                new XAttribute(W + "left", PointsToDxa(f.IndentLeftPt)),
                new XAttribute(W + "right", PointsToDxa(f.IndentRightPt)),
                new XAttribute(W + "firstLine", PointsToDxa(f.FirstLineIndentPt))));
        // Paragraph border (w:pBdr): a box whose drawn edges are selected by the per-edge flags (all four =
        // a box) with one shared colour/width/line-style, analogous to w:tblBorders. A horizontal rule is the
        // bottom-only case. Each drawn edge carries the model's line style (w:val).
        if (f.Border is { } border)
        {
            var styleToken = BorderLineStyles.ToToken(border.LineStyle);
            XElement Edge(string name) => new(W + name,
                new XAttribute(W + "val", styleToken),
                new XAttribute(W + "sz", PointsToEighthPoints(border.WidthPt)),
                new XAttribute(W + "space", 0),
                new XAttribute(W + "color", border.ColorHex.TrimStart('#')));
            // BottomOnly forces a bottom-only rule (the horizontal-rule case); otherwise honour the per-edge
            // flags. An edge that is off is omitted entirely (a null is dropped by XElement) so the round-trip
            // reads it back as off.
            var drawBottom = border.BottomOnly || border.Bottom;
            var drawTop = !border.BottomOnly && border.Top;
            var drawLeft = !border.BottomOnly && border.Left;
            var drawRight = !border.BottomOnly && border.Right;
            if (drawTop || drawLeft || drawBottom || drawRight)
                pPr.Add(new XElement(W + "pBdr",
                    drawTop ? Edge("top") : null,
                    drawLeft ? Edge("left") : null,
                    drawBottom ? Edge("bottom") : null,
                    drawRight ? Edge("right") : null));
        }
        // Paragraph shading (background fill), mirroring run-level w:shd highlight. The pattern (w:val) comes
        // from the model; the default Clear preserves the byte-unchanged round-trip of existing documents.
        if (f.ShadingColorHex is { Length: > 0 } shading)
            pPr.Add(new XElement(W + "shd",
                new XAttribute(W + "val", ShadingPatterns.ToToken(f.ShadingPattern)),
                new XAttribute(W + "color", "auto"),
                new XAttribute(W + "fill", shading.TrimStart('#'))));

        // A section break carried by this paragraph: the section's w:sectPr is the LAST child of w:pPr
        // (schema order), marking this paragraph as the end of a non-final section. Each non-final section
        // now references its OWN header/footer parts (via partsBySection), so multi-section documents keep
        // page-specific headers/footers. Reuses the shared sectPr builder so per-section properties are
        // emitted from one code path.
        if (paragraph.SectionBreak is { } section)
        {
            var sectionParts = partsBySection is not null && partsBySection.TryGetValue(section, out var p)
                ? p
                : (IReadOnlyList<HeaderFooterPart>)[];
            pPr.Add(BuildSectionProperties(section.Page, sectionParts, breakKind: section.BreakKind));
        }

        return pPr.HasElements ? pPr : null;
    }

    /// <summary>
    /// Builds one <c>w:tab</c> for a paragraph tab stop: alignment in <c>w:val</c>, position in
    /// <c>w:pos</c> (dxa), and an optional <c>w:leader</c> fill emitted only when the stop carries
    /// one (so leaderless stops round-trip byte-for-byte as before).
    /// </summary>
    private static XElement BuildTabStop(TabStop stop)
    {
        var tab = new XElement(W + "tab",
            new XAttribute(W + "val", stop.Alignment switch
            {
                TabStopAlignment.Center => "center",
                TabStopAlignment.Right => "right",
                TabStopAlignment.Decimal => "decimal",
                _ => "left"
            }),
            new XAttribute(W + "pos", PointsToDxa(stop.PositionPt)));
        if (stop.Leader != TabLeader.None)
            tab.Add(new XAttribute(W + "leader", stop.Leader switch
            {
                TabLeader.Dots => "dot",
                TabLeader.Dashes => "hyphen",
                TabLeader.Underline => "underscore",
                _ => "none"
            }));
        return tab;
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

    /// <summary>
    /// Builds the <c>w:fldSimple/@w:instr</c> for a table-cell formula field: the formula expression
    /// (with a leading <c>=</c>) plus, when a number format is set, a <c>\#</c> numeric-picture switch with
    /// the quoted format — e.g. <c> =SUM(ABOVE) \# "#,##0.00" </c>. The surrounding spaces match how Word
    /// writes field instructions.
    /// </summary>
    private static string TableFormulaInstruction(TableFormulaField formula)
    {
        var expression = formula.Expression.TrimStart().StartsWith('=')
            ? formula.Expression.Trim()
            : "=" + formula.Expression.Trim();
        var instr = " " + expression + " ";
        if (formula.NumberFormat is { Length: > 0 } format)
            instr += "\\# \"" + format + "\" ";
        return instr;
    }

    /// <summary>
    /// Builds the <c>w:fldSimple/@w:instr</c> for a Mark Citation (TA) field: <c> TA \l "long" \s "short"
    /// \c N </c>, where <c>\l</c> is the full citation, <c>\s</c> the short form (omitted when blank) and
    /// <c>\c</c> Word's numeric category. The surrounding spaces match how Word writes field instructions.
    /// Embedded double-quotes in the citation text are dropped so the instruction stays well-formed.
    /// </summary>
    private static string CitationInstruction(Citation citation)
    {
        static string Clean(string value) => value.Replace("\"", string.Empty);
        var instr = " TA \\l \"" + Clean(citation.LongCitation) + "\"";
        if (citation.ShortCitation.Length > 0)
            instr += " \\s \"" + Clean(citation.ShortCitation) + "\"";
        instr += " \\c " + (int)citation.Category + " ";
        return instr;
    }

    private static XElement BuildRun(Run run, RunDrawings drawings)
    {
        // An inline equation serialises as an m:oMath emitted in place of the run (a paragraph-level
        // sibling of w:r, never wrapped in one), carrying its math fragments as m:r/m:sSup/m:f.
        if (run.Equation is { } equation)
            return BuildOMath(equation);

        // An inline shape / text box serialises as a w:r wrapping a w:drawing/wp:inline/.../wps:wsp.
        if (run.Shape is { } shape)
        {
            var sr = new XElement(W + "r");
            var rPr = BuildRunProperties(run.Formatting);
            if (rPr is not null)
                sr.Add(rPr);
            sr.Add(BuildShapeDrawing(shape, drawings.Ids));
            return sr;
        }

        // Inline WordArt serialises as a w:r wrapping a w:drawing/wp:inline/.../wps:wsp text box whose run
        // carries DrawingML text effects (chosen by the style preset) on its a:rPr.
        if (run.WordArt is { } wordArt)
        {
            var wr = new XElement(W + "r");
            var rPr = BuildRunProperties(run.Formatting);
            if (rPr is not null)
                wr.Add(rPr);
            wr.Add(BuildWordArtDrawing(wordArt, drawings.Ids));
            return wr;
        }

        // A Mark Citation (TA) field emits a hidden w:fldSimple whose w:instr is the TA instruction
        // (" TA \l "long" \s "short" \c N "). It wraps an empty run so it produces no visible glyph, matching
        // Word's hidden citation mark. The reader recovers the Citation from the instruction.
        if (run.Citation is { } citation)
            return new XElement(W + "fldSimple",
                new XAttribute(W + "instr", CitationInstruction(citation)),
                new XElement(W + "r"));

        // A table-cell formula field (Word's Table > Data > Formula) emits a w:fldSimple whose w:instr is
        // the formula plus an optional number-format switch (e.g. " =SUM(ABOVE) \# "#,##0.00" "), wrapping a
        // run whose w:t is the cached computed result. The reader recovers the formula + format from the
        // instruction and the cached result from the wrapped run.
        if (run.TableFormula is { } formula)
            return new XElement(W + "fldSimple",
                new XAttribute(W + "instr", TableFormulaInstruction(formula)),
                BuildTextRun(run, drawings));

        // A document field emits a self-contained w:fldSimple wrapping a run; the wrapped run's w:t
        // carries the last-known/cached value as fallback text for field-unaware consumers. The
        // w:instr keyword identifies the field kind (PAGE, DATE, TIME, FILENAME, AUTHOR, NUMPAGES).
        if (FieldInstruction(run.FieldKind) is { } instruction)
            return new XElement(W + "fldSimple",
                new XAttribute(W + "instr", instruction),
                BuildTextRun(run, drawings));

        // A footnote reference is a superscript run carrying a w:footnoteReference (no literal text).
        // Carry the run's real formatting (forcing vertAlign=superscript) so a bold/coloured/sized
        // marker is preserved rather than discarded.
        if (run.FootnoteId is { } footnoteId)
            return MarkerRun(run, new XElement(W + "footnoteReference", new XAttribute(W + "id", footnoteId)));

        // An endnote reference is a superscript run carrying a w:endnoteReference (no literal text).
        if (run.EndnoteId is { } endnoteId)
            return MarkerRun(run, new XElement(W + "endnoteReference", new XAttribute(W + "id", endnoteId)));

        // The textless comment anchor run carries the w:commentReference for its id (no literal text).
        if (run is { IsCommentReference: true, CommentId: { } commentRefId })
            return new XElement(W + "r",
                new XElement(W + "commentReference", new XAttribute(W + "id", commentRefId)));

        // A manual page break serialises as a (text-less) run wrapping w:br w:type="page".
        if (run.IsPageBreak)
        {
            var br = new XElement(W + "r");
            var brPr = BuildRunProperties(run.Formatting);
            if (brPr is not null)
                br.Add(brPr);
            br.Add(new XElement(W + "br", new XAttribute(W + "type", "page")));
            return br;
        }

        return BuildTextRun(run, drawings);
    }

    // A textless marker run (footnote/endnote reference): carries the run's own formatting forced to
    // superscript, then the marker element. Preserves bold/colour/size that a caller put on the marker.
    private static XElement MarkerRun(Run run, XElement marker)
    {
        var r = new XElement(W + "r");
        var rPr = BuildRunProperties(run.Formatting with { VerticalAlign = VerticalAlign.Superscript });
        if (rPr is not null)
            r.Add(rPr);
        r.Add(marker);
        return r;
    }

    private static XElement BuildTextRun(Run run, RunDrawings drawings)
    {
        var r = new XElement(W + "r");
        var rPr = BuildRunProperties(run.Formatting);
        if (rPr is not null)
            r.Add(rPr);
        if (run.PreservedDrawing is { } preservedDrawing)
            r.Add(BuildPreservedDrawing(preservedDrawing, drawings.PreservedDrawingRelIds));
        else if (run.Image is not null && drawings.Images.TryGetValue(run, out var imagePart))
            r.Add(BuildDrawing(imagePart));
        else if (run.Chart is not null && drawings.Charts.TryGetValue(run, out var chartPart))
            r.Add(BuildChartDrawing(chartPart));
        else if (run.EmbeddedObject is not null && drawings.EmbeddedObjects.TryGetValue(run, out var embeddedPart))
            r.Add(BuildEmbeddedObject(embeddedPart));
        else if (run.SmartArt is not null && drawings.SmartArts.TryGetValue(run, out var smartArtPart))
            r.Add(BuildSmartArtDrawing(smartArtPart));
        else
        {
            // A tracked deletion stores its text in w:delText (so Word renders it as deleted content);
            // all other runs use the ordinary w:t element.
            var textElement = run.Revision == RevisionKind.Deleted ? "delText" : "t";
            r.Add(new XElement(W + textElement, new XAttribute(XNamespace.Xml + "space", "preserve"), run.Text));
        }
        return r;
    }

    /// <summary>
    /// Builds an inline OMML equation (m:oMath) from an <see cref="Equation"/>. Each fragment maps to its
    /// OMML element: plain text → m:r/m:t, superscript → m:sSup (m:e base, m:sup exponent), fraction →
    /// m:f (m:num numerator, m:den denominator). This is the minimal valid shape FreeW's own reader
    /// recovers (see <see cref="DocxReader"/>).
    /// </summary>
    private static XElement BuildOMath(Equation equation)
    {
        var oMath = new XElement(M + "oMath");
        foreach (var run in equation.Runs)
            oMath.Add(BuildMathRun(run));
        return oMath;
    }

    /// <summary>Builds the OMML element for a single math fragment (m:r, m:sSup or m:f).</summary>
    private static XElement BuildMathRun(MathRun run) => run.Kind switch
    {
        MathRunKind.Superscript => new XElement(M + "sSup",
            new XElement(M + "e", MathText(run.Base)),
            new XElement(M + "sup", MathText(run.Sup))),
        MathRunKind.Fraction => new XElement(M + "f",
            new XElement(M + "num", MathText(run.Numerator)),
            new XElement(M + "den", MathText(run.Denominator))),
        _ => MathText(run.Text)
    };

    /// <summary>Builds an m:r run carrying <paramref name="text"/> in an m:t (xml:space preserved).</summary>
    private static XElement MathText(string text) =>
        new(M + "r",
            new XElement(M + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), text));

    /// <summary>
    /// Builds the picture drawing for an image part. An inline image (the default) emits
    /// <c>w:drawing/wp:inline</c> exactly as before; a floating image (<see cref="InlineImage.Wrapping"/>
    /// not <see cref="ImageWrapping.Inline"/>) emits <c>w:drawing/wp:anchor</c> with the position + the
    /// matching wrap element. Both paths share the same <c>a:graphic/pic:pic</c> payload (see
    /// <see cref="BuildPicGraphic"/>).
    /// </summary>
    private static XElement BuildDrawing(ImagePart part) =>
        part.Image.IsFloating ? BuildAnchorDrawing(part) : BuildInlineDrawing(part);

    /// <summary>Builds an inline picture: w:drawing/wp:inline/a:graphic/pic:pic referencing the blip.</summary>
    private static XElement BuildInlineDrawing(ImagePart part)
    {
        var cx = PointsToEmu(part.Image.WidthPt);
        var cy = PointsToEmu(part.Image.HeightPt);

        return new XElement(W + "drawing",
            new XElement(Wp + "inline",
                new XAttribute(XNamespace.Xmlns + "wp", Wp.NamespaceName),
                new XAttribute("distT", 0), new XAttribute("distB", 0),
                new XAttribute("distL", 0), new XAttribute("distR", 0),
                new XElement(Wp + "extent", new XAttribute("cx", cx), new XAttribute("cy", cy)),
                new XElement(Wp + "effectExtent",
                    new XAttribute("l", 0), new XAttribute("t", 0),
                    new XAttribute("r", 0), new XAttribute("b", 0)),
                BuildDocPr(part),
                BuildPicGraphic(part, cx, cy)));
    }

    /// <summary>
    /// Builds a floating picture: w:drawing/wp:anchor with @behindDoc, wp:simplePos, wp:positionH/V
    /// (relativeFrom + posOffset), wp:extent, the single wrap element matching
    /// <see cref="InlineImage.Wrapping"/>, then the same wp:docPr + a:graphic/pic:pic payload as the inline
    /// path. wp:wrapTight is emitted without a wrapPolygon (a deliberate simplification — Word fills one in).
    /// </summary>
    private static XElement BuildAnchorDrawing(ImagePart part)
    {
        var image = part.Image;
        var cx = PointsToEmu(image.WidthPt);
        var cy = PointsToEmu(image.HeightPt);
        var behindDoc = image.Wrapping == ImageWrapping.Behind ? 1 : 0;

        return new XElement(W + "drawing",
            new XElement(Wp + "anchor",
                new XAttribute(XNamespace.Xmlns + "wp", Wp.NamespaceName),
                new XAttribute("distT", 0), new XAttribute("distB", 0),
                new XAttribute("distL", 0), new XAttribute("distR", 0),
                new XAttribute("simplePos", 0),
                new XAttribute("relativeHeight", 0),
                new XAttribute("behindDoc", behindDoc),
                new XAttribute("locked", 0),
                new XAttribute("layoutInCell", 1),
                new XAttribute("allowOverlap", 1),
                new XElement(Wp + "simplePos", new XAttribute("x", 0), new XAttribute("y", 0)),
                new XElement(Wp + "positionH",
                    new XAttribute("relativeFrom", HorizontalAnchorToken(image.HorizontalAnchor)),
                    new XElement(Wp + "posOffset", PointsToEmu(image.HorizontalOffsetPt))),
                new XElement(Wp + "positionV",
                    new XAttribute("relativeFrom", VerticalAnchorToken(image.VerticalAnchor)),
                    new XElement(Wp + "posOffset", PointsToEmu(image.VerticalOffsetPt))),
                new XElement(Wp + "extent", new XAttribute("cx", cx), new XAttribute("cy", cy)),
                new XElement(Wp + "effectExtent",
                    new XAttribute("l", 0), new XAttribute("t", 0),
                    new XAttribute("r", 0), new XAttribute("b", 0)),
                BuildWrap(image.Wrapping),
                BuildDocPr(part),
                BuildPicGraphic(part, cx, cy)));
    }

    /// <summary>The wp:positionH/@relativeFrom token for a horizontal anchor.</summary>
    private static string HorizontalAnchorToken(HorizontalAnchor anchor) => anchor switch
    {
        HorizontalAnchor.Margin => "margin",
        HorizontalAnchor.Page => "page",
        _ => "column",
    };

    /// <summary>The wp:positionV/@relativeFrom token for a vertical anchor.</summary>
    private static string VerticalAnchorToken(VerticalAnchor anchor) => anchor switch
    {
        VerticalAnchor.Margin => "margin",
        VerticalAnchor.Page => "page",
        _ => "paragraph",
    };

    /// <summary>
    /// The single wrap element for a floating wrapping mode: wp:wrapSquare (square), wp:wrapTight (tight,
    /// no wrapPolygon — a simplification), wp:wrapTopAndBottom, or wp:wrapNone for the front/behind modes.
    /// </summary>
    private static XElement BuildWrap(ImageWrapping wrapping) => wrapping switch
    {
        ImageWrapping.Square => new XElement(Wp + "wrapSquare", new XAttribute("wrapText", "bothSides")),
        ImageWrapping.Tight => new XElement(Wp + "wrapTight", new XAttribute("wrapText", "bothSides")),
        ImageWrapping.TopAndBottom => new XElement(Wp + "wrapTopAndBottom"),
        _ => new XElement(Wp + "wrapNone"), // Behind / InFront both wrap none (distinguished by @behindDoc).
    };

    /// <summary>
    /// Builds the wp:docPr for an image, carrying accessibility alt text on @descr when set (omitted
    /// otherwise so images without alt text serialise exactly as before). Shared by both drawing paths.
    /// </summary>
    private static XElement BuildDocPr(ImagePart part)
    {
        var docPr = new XElement(Wp + "docPr", new XAttribute("id", part.DrawingId), new XAttribute("name", part.FileName));
        if (!string.IsNullOrEmpty(part.Image.AltText))
            docPr.Add(new XAttribute("descr", part.Image.AltText));
        return docPr;
    }

    /// <summary>
    /// Builds the shared a:graphic/a:graphicData(uri=pic)/pic:pic payload referencing the blip, used by
    /// both the inline (<see cref="BuildInlineDrawing"/>) and floating (<see cref="BuildAnchorDrawing"/>)
    /// drawing paths so the picture markup is not duplicated.
    /// </summary>
    private static XElement BuildPicGraphic(ImagePart part, long cx, long cy) =>
        new(A + "graphic",
            new XAttribute(XNamespace.Xmlns + "a", A.NamespaceName),
            new XElement(A + "graphicData",
                new XAttribute("uri", Pic.NamespaceName),
                new XElement(Pic + "pic",
                    new XAttribute(XNamespace.Xmlns + "pic", Pic.NamespaceName),
                    new XElement(Pic + "nvPicPr",
                        new XElement(Pic + "cNvPr", new XAttribute("id", (uint)part.DrawingId), new XAttribute("name", part.FileName)),
                        new XElement(Pic + "cNvPicPr")),
                    new XElement(Pic + "blipFill",
                        new XElement(A + "blip", new XAttribute(R + "embed", part.RelationshipId)),
                        new XElement(A + "stretch", new XElement(A + "fillRect"))),
                    new XElement(Pic + "spPr",
                        new XElement(A + "xfrm",
                            new XElement(A + "off", new XAttribute("x", 0), new XAttribute("y", 0)),
                            new XElement(A + "ext", new XAttribute("cx", cx), new XAttribute("cy", cy))),
                        new XElement(A + "prstGeom", new XAttribute("prst", "rect"),
                            new XElement(A + "avLst"))))));

    /// <summary>The DrawingML preset-geometry token (a:prstGeom/@prst) for a shape kind.</summary>
    private static string PresetGeometry(ShapeKind kind) => kind switch
    {
        ShapeKind.RoundedRectangle => "roundRect",
        ShapeKind.Ellipse => "ellipse",
        _ => "rect", // Rectangle and TextBox both use a plain rectangle geometry.
    };

    /// <summary>
    /// Builds an inline DrawingML shape / text box: w:drawing/wp:inline/a:graphic/a:graphicData[uri=wps]/
    /// wps:wsp, carrying a wps:spPr (preset geometry + optional a:solidFill) and, for a text box, a
    /// wps:txbx/w:txbxContent holding the body paragraphs. The shape's wp:docPr id comes from the
    /// per-write shape docPr counter so it never collides with image drawing ids.
    /// </summary>
    private static XElement BuildShapeDrawing(Shape shape, IdAllocator ids)
    {
        var cx = PointsToEmu(shape.WidthPt);
        var cy = PointsToEmu(shape.HeightPt);
        var docPrId = ids.NextShapeDrawingId();
        var name = $"{shape.Kind}{(uint)docPrId}";

        // wps:spPr: position/size (a:xfrm), preset geometry, then optional solid fill.
        var spPr = new XElement(Wps + "spPr",
            new XElement(A + "xfrm",
                new XElement(A + "off", new XAttribute("x", 0), new XAttribute("y", 0)),
                new XElement(A + "ext", new XAttribute("cx", cx), new XAttribute("cy", cy))),
            new XElement(A + "prstGeom", new XAttribute("prst", PresetGeometry(shape.Kind)),
                new XElement(A + "avLst")));
        if (shape.FillColorHex is { Length: > 0 } fill)
            spPr.Add(new XElement(A + "solidFill",
                new XElement(A + "srgbClr", new XAttribute("val", fill.TrimStart('#')))));

        var wsp = new XElement(Wps + "wsp",
            new XElement(Wps + "cNvSpPr"),
            spPr);

        // A text box carries its body paragraphs in wps:txbx/w:txbxContent. Body paragraphs do not carry
        // inline images or document hyperlinks, so they build against empty maps — but they DO share the
        // surrounding write's IdAllocator so nested bookmark/revision/shape ids continue the same sequence.
        if (shape.HasText)
        {
            var txbxContent = new XElement(W + "txbxContent");
            var nested = RunDrawings.Empty() with { Ids = ids };
            foreach (var paragraph in shape.TextParagraphs)
                txbxContent.Add(BuildParagraph(paragraph, nested, EmptyHyperlinks));
            wsp.Add(new XElement(Wps + "txbx", txbxContent));
        }

        // wps:bodyPr is required by the schema for a valid wsp; defaults are fine.
        wsp.Add(new XElement(Wps + "bodyPr"));

        return new XElement(W + "drawing",
            new XElement(Wp + "inline",
                new XAttribute("distT", 0), new XAttribute("distB", 0),
                new XAttribute("distL", 0), new XAttribute("distR", 0),
                new XElement(Wp + "extent", new XAttribute("cx", cx), new XAttribute("cy", cy)),
                new XElement(Wp + "effectExtent",
                    new XAttribute("l", 0), new XAttribute("t", 0),
                    new XAttribute("r", 0), new XAttribute("b", 0)),
                new XElement(Wp + "docPr", new XAttribute("id", (uint)docPrId), new XAttribute("name", name)),
                new XElement(A + "graphic",
                    new XElement(A + "graphicData",
                        new XAttribute("uri", Wps.NamespaceName),
                        wsp))));
    }

    /// <summary>Empty hyperlink map for building text-box body paragraphs (they carry no document rels).</summary>
    private static readonly Dictionary<string, string> EmptyHyperlinks = new();

    // The fixed colours a WordArt preset paints with (kept simple and deterministic so the reader can infer
    // the preset back from which effect elements are present, not from exact colour values).
    private const string WordArtFillColor = "1F4E79";        // a deep blue text fill
    private const string WordArtGradientStart = "4472C4";    // gradient stop 0
    private const string WordArtGradientEnd = "ED7D31";      // gradient stop 1
    private const string WordArtOutlineColor = "2E2E2E";     // outline / shadow colour

    /// <summary>
    /// Builds inline WordArt: a w:drawing/wp:inline/.../wps:wsp text box (exactly like a shape's text box)
    /// whose single text run carries DrawingML text effects on its a:rPr. The effects are chosen by the
    /// WordArt style preset: a solid or gradient text fill (a:solidFill/a:gradFill), an outline (a:ln),
    /// and/or an outer shadow (a:effectLst/a:outerShdw). The wp:docPr id comes from the document-scoped
    /// per-write shape docPr counter (shared with shapes) so it never collides with image ids.
    /// </summary>
    private static XElement BuildWordArtDrawing(WordArt wordArt, IdAllocator ids)
    {
        // WordArt has no intrinsic geometry size in the FreeW model; derive a sensible text-box extent from
        // the font size and text length so the inline drawing has a non-zero extent (Word recomputes it).
        var heightPt = wordArt.FontSizePt * 1.6;
        var widthPt = Math.Max(1, wordArt.Text.Length) * wordArt.FontSizePt * 0.62;
        var cx = PointsToEmu(widthPt);
        var cy = PointsToEmu(heightPt);
        var docPrId = ids.NextShapeDrawingId();
        var name = $"WordArt{(uint)docPrId}";

        // A plain text-box rect carries the WordArt; the decorative effects live on the run's a:rPr.
        var spPr = new XElement(Wps + "spPr",
            new XElement(A + "xfrm",
                new XElement(A + "off", new XAttribute("x", 0), new XAttribute("y", 0)),
                new XElement(A + "ext", new XAttribute("cx", cx), new XAttribute("cy", cy))),
            new XElement(A + "prstGeom", new XAttribute("prst", "rect"),
                new XElement(A + "avLst")));

        var wsp = new XElement(Wps + "wsp",
            new XElement(Wps + "cNvSpPr"),
            spPr,
            new XElement(Wps + "txbx",
                new XElement(W + "txbxContent", BuildWordArtParagraph(wordArt))),
            new XElement(Wps + "bodyPr"));

        return new XElement(W + "drawing",
            new XElement(Wp + "inline",
                new XAttribute("distT", 0), new XAttribute("distB", 0),
                new XAttribute("distL", 0), new XAttribute("distR", 0),
                new XElement(Wp + "extent", new XAttribute("cx", cx), new XAttribute("cy", cy)),
                new XElement(Wp + "effectExtent",
                    new XAttribute("l", 0), new XAttribute("t", 0),
                    new XAttribute("r", 0), new XAttribute("b", 0)),
                new XElement(Wp + "docPr", new XAttribute("id", (uint)docPrId), new XAttribute("name", name)),
                new XElement(A + "graphic",
                    new XElement(A + "graphicData",
                        new XAttribute("uri", Wps.NamespaceName),
                        wsp))));
    }

    /// <summary>
    /// Builds the single w:p inside a WordArt text box: a w:r whose w:rPr carries the font size (w:sz, in
    /// half-points) plus the DrawingML text effects (a:solidFill/a:gradFill/a:ln/a:effectLst) selected by the
    /// style preset, followed by the w:t text. The DrawingML effect elements sit directly under w:rPr exactly
    /// as Word emits WordArt text properties.
    /// </summary>
    private static XElement BuildWordArtParagraph(WordArt wordArt)
    {
        var rPr = new XElement(W + "rPr",
            new XElement(W + "sz", new XAttribute(W + "val", PointsToHalfPoints(wordArt.FontSizePt))),
            new XElement(W + "szCs", new XAttribute(W + "val", PointsToHalfPoints(wordArt.FontSizePt))));
        foreach (var effect in WordArtEffects(wordArt.Style))
            rPr.Add(effect);

        var run = new XElement(W + "r",
            rPr,
            new XElement(W + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), wordArt.Text));

        return new XElement(W + "p", run);
    }

    /// <summary>
    /// Expands a <see cref="WordArtStyle"/> preset into the DrawingML text-effect elements placed on the
    /// WordArt run's w:rPr. The reader infers the preset back from which of these are present:
    /// gradient → GradientFill, outline (a:ln) → Outline, shadow (a:effectLst) → Shadow, else FillBlue.
    /// </summary>
    private static IEnumerable<XElement> WordArtEffects(WordArtStyle style)
    {
        switch (style)
        {
            case WordArtStyle.GradientFill:
                yield return new XElement(A + "gradFill",
                    new XElement(A + "gsLst",
                        new XElement(A + "gs", new XAttribute("pos", 0),
                            new XElement(A + "srgbClr", new XAttribute("val", WordArtGradientStart))),
                        new XElement(A + "gs", new XAttribute("pos", 100000),
                            new XElement(A + "srgbClr", new XAttribute("val", WordArtGradientEnd)))),
                    new XElement(A + "lin", new XAttribute("ang", 5400000), new XAttribute("scaled", 1)));
                break;

            case WordArtStyle.Outline:
                yield return SolidFill(WordArtFillColor);
                yield return new XElement(A + "ln", new XAttribute("w", 9525),
                    SolidFill(WordArtOutlineColor));
                break;

            case WordArtStyle.Shadow:
                yield return SolidFill(WordArtFillColor);
                yield return new XElement(A + "effectLst",
                    new XElement(A + "outerShdw",
                        new XAttribute("blurRad", 50800),
                        new XAttribute("dist", 38100),
                        new XAttribute("dir", 2700000),
                        new XAttribute("algn", "tl"),
                        new XElement(A + "srgbClr", new XAttribute("val", WordArtOutlineColor),
                            new XElement(A + "alpha", new XAttribute("val", 40000)))));
                break;

            default: // FillBlue
                yield return SolidFill(WordArtFillColor);
                break;
        }
    }

    /// <summary>Builds an a:solidFill wrapping an a:srgbClr of the given RRGGBB hex value.</summary>
    private static XElement SolidFill(string hex) =>
        new(A + "solidFill", new XElement(A + "srgbClr", new XAttribute("val", hex)));

    /// <summary>
    /// Builds the inline chart drawing: w:drawing/wp:inline/a:graphic/a:graphicData(uri=chart)/c:chart
    /// referencing the chart part by relationship id (r:id). Mirrors <see cref="BuildDrawing"/> for images,
    /// but the graphicData wraps a c:chart reference rather than a pic:pic. The c namespace is declared on
    /// the c:chart element so the reference is self-describing.
    /// </summary>
    private static XElement BuildChartDrawing(ChartPart part)
    {
        var cx = PointsToEmu(part.Chart.WidthPt);
        var cy = PointsToEmu(part.Chart.HeightPt);
        var name = $"Chart {part.DrawingId}";

        return new XElement(W + "drawing",
            new XElement(Wp + "inline",
                new XAttribute(XNamespace.Xmlns + "wp", Wp.NamespaceName),
                new XAttribute("distT", 0), new XAttribute("distB", 0),
                new XAttribute("distL", 0), new XAttribute("distR", 0),
                new XElement(Wp + "extent", new XAttribute("cx", cx), new XAttribute("cy", cy)),
                new XElement(Wp + "effectExtent",
                    new XAttribute("l", 0), new XAttribute("t", 0),
                    new XAttribute("r", 0), new XAttribute("b", 0)),
                new XElement(Wp + "docPr", new XAttribute("id", part.DrawingId), new XAttribute("name", name)),
                new XElement(A + "graphic",
                    new XAttribute(XNamespace.Xmlns + "a", A.NamespaceName),
                    new XElement(A + "graphicData",
                        new XAttribute("uri", ChartGraphicDataUri),
                        new XElement(C + "chart",
                            new XAttribute(XNamespace.Xmlns + "c", C.NamespaceName),
                            new XAttribute(R + "id", part.RelationshipId))))));
    }

    /// <summary>
    /// Re-emits a verbatim-preserved inline drawing (an unmodelled chart/chartex <c>w:drawing</c> captured on
    /// read): the stored XML is reparsed and each reference's relationship id (the original <c>r:id</c>/
    /// <c>r:embed</c> the drawing used) is re-pointed at the document relationship the writer freshly assigned to
    /// the preserved chart part (<paramref name="preservedDrawingRelIds"/>: preserved part name →
    /// <c>rIdPreserved{N}</c>). The chart part's own satellites (media, colours, styles) keep their original
    /// part-local rels — those parts are preserved byte-for-byte — so only the document→chart hop is rewritten.
    /// </summary>
    private static XElement BuildPreservedDrawing(
        Model.PreservedDrawing preservedDrawing,
        IReadOnlyDictionary<string, string>? preservedDrawingRelIds)
    {
        var drawing = XElement.Parse(preservedDrawing.Xml, LoadOptions.PreserveWhitespace);

        foreach (var reference in preservedDrawing.References)
        {
            if (preservedDrawingRelIds is null
                || !preservedDrawingRelIds.TryGetValue(reference.PreservedPartName, out var newRelId))
                continue;
            foreach (var element in drawing.DescendantsAndSelf())
            {
                if (element.Attribute(R + "id")?.Value == reference.OriginalRelId)
                    element.SetAttributeValue(R + "id", newRelId);
                if (element.Attribute(R + "embed")?.Value == reference.OriginalRelId)
                    element.SetAttributeValue(R + "embed", newRelId);
            }
        }

        return drawing;
    }

    /// <summary>
    /// Builds a classic embedded OLE object as a <c>w:object</c> wrapping the VML presentation: a
    /// <c>v:shape</c> sized in points carrying an <c>o:OLEObject</c> (Type="Embed", the model's ProgID, the
    /// shape id, and an <c>r:id</c> to the embedded <c>.bin</c> part) and — when the object has an icon — a
    /// <c>v:imagedata</c> whose <c>r:id</c> points at the icon media part. The VML namespaces (v/o) are
    /// declared on the document root (see <see cref="BuildDocument"/>).
    /// SIMPLIFICATION (Y2): the VML presentation is minimised to a single v:shape (+ optional v:imagedata);
    /// only embedded (not linked) objects are emitted, and no live OLE activation data is written.
    /// </summary>
    private static XElement BuildEmbeddedObject(EmbeddedObjectPart part)
    {
        // VML shapes size in points via a CSS-style @style; width/height map directly from the model.
        var style = $"width:{FormatPt(part.EmbeddedObject.WidthPt)}pt;height:{FormatPt(part.EmbeddedObject.HeightPt)}pt";

        var shape = new XElement(V + "shape",
            new XAttribute("id", part.ShapeId),
            new XAttribute("type", "#_oleObjType"),
            new XAttribute("style", style));
        // The on-page presentation: v:imagedata references the icon media part by relationship id.
        if (part.IconPart is { } icon)
            shape.Add(new XElement(V + "imagedata",
                new XAttribute(R + "id", icon.RelationshipId),
                new XAttribute(O + "title", "")));

        var ole = new XElement(O + "OLEObject",
            new XAttribute("Type", "Embed"),
            new XAttribute("ProgID", part.EmbeddedObject.ProgId),
            new XAttribute("ShapeID", part.ShapeId),
            new XAttribute("DrawAspect", "Icon"),
            new XAttribute("ObjectID", part.ShapeId),
            new XAttribute(R + "id", part.RelationshipId));

        return new XElement(W + "object", shape, ole);
    }

    /// <summary>Formats a point measure for a VML CSS @style value (invariant, trimmed of trailing zeros).</summary>
    private static string FormatPt(double points) =>
        points.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// Builds the inline w:drawing for a SmartArt diagram: an a:graphicData[uri=diagram] whose body is a
    /// dgm:relIds carrying the four relationship ids (r:dm=data, r:lo=layout, r:qs=quickStyle, r:cs=colors).
    /// Mirrors <see cref="BuildChartDrawing"/> but references four parts instead of one.
    /// </summary>
    private static XElement BuildSmartArtDrawing(SmartArtPart part)
    {
        var cx = PointsToEmu(part.SmartArt.WidthPt);
        var cy = PointsToEmu(part.SmartArt.HeightPt);
        var name = $"Diagram {part.DrawingId}";

        return new XElement(W + "drawing",
            new XElement(Wp + "inline",
                new XAttribute(XNamespace.Xmlns + "wp", Wp.NamespaceName),
                new XAttribute("distT", 0), new XAttribute("distB", 0),
                new XAttribute("distL", 0), new XAttribute("distR", 0),
                new XElement(Wp + "extent", new XAttribute("cx", cx), new XAttribute("cy", cy)),
                new XElement(Wp + "effectExtent",
                    new XAttribute("l", 0), new XAttribute("t", 0),
                    new XAttribute("r", 0), new XAttribute("b", 0)),
                new XElement(Wp + "docPr", new XAttribute("id", part.DrawingId), new XAttribute("name", name)),
                new XElement(A + "graphic",
                    new XAttribute(XNamespace.Xmlns + "a", A.NamespaceName),
                    new XElement(A + "graphicData",
                        new XAttribute("uri", DiagramGraphicDataUri),
                        new XElement(Dgm + "relIds",
                            new XAttribute(XNamespace.Xmlns + "dgm", Dgm.NamespaceName),
                            new XAttribute(XNamespace.Xmlns + "r", R.NamespaceName),
                            new XAttribute(R + "dm", part.DataRelationshipId),
                            new XAttribute(R + "lo", part.LayoutRelationshipId),
                            new XAttribute(R + "qs", part.QuickStyleRelationshipId),
                            new XAttribute(R + "cs", part.ColorsRelationshipId))))));
    }

    /// <summary>
    /// Builds the SmartArt DATA part (word/diagrams/dataN.xml — dgm:dataModel). This is the only diagram
    /// part with real content: a dgm:ptLst holding one document point (type="doc") and one node point per
    /// model node (each carrying its text in a dgm:t/a:p/a:r/a:t body), plus a dgm:cxnLst whose parOf
    /// connections record the parent→child structure (used to recover the Hierarchy tree on read). Node ids
    /// are deterministic ("node0", "node1", …) in a stable pre-order walk so write/read agree.
    /// SIMPLIFICATION (Y1): no presentation-layer points (type="pres") are emitted. F2 adds a
    /// <c>dgm:dataModelExt</c> at the end of the data model referencing (via <paramref name="drawingRelId"/>)
    /// the rendered-geometry drawing part, so a viewer can show positioned shapes without re-running
    /// auto-layout. The node text + structure here remains the round-trip unit (the reader ignores the ext).
    /// </summary>
    private static XDocument BuildDiagramData(SmartArt smartArt, string drawingRelId)
    {
        const string docId = "doc0";
        var ptLst = new XElement(Dgm + "ptLst",
            new XElement(Dgm + "pt",
                new XAttribute("modelId", docId),
                new XAttribute("type", "doc")));
        var cxnLst = new XElement(Dgm + "cxnLst");

        var nextId = 0;
        var nextCxn = 0;
        // Pre-order walk: emit a node point + a parOf connection from its parent, then recurse children.
        void Emit(SmartArtNode node, string parentId)
        {
            var id = $"node{nextId++}";
            ptLst.Add(new XElement(Dgm + "pt",
                new XAttribute("modelId", id),
                new XElement(Dgm + "t",
                    new XElement(A + "bodyPr"),
                    new XElement(A + "lstStyle"),
                    new XElement(A + "p",
                        new XElement(A + "r",
                            new XElement(A + "t", node.Text))))));
            cxnLst.Add(new XElement(Dgm + "cxn",
                new XAttribute("modelId", $"cxn{nextCxn++}"),
                new XAttribute("type", "parOf"),
                new XAttribute("srcId", parentId),
                new XAttribute("destId", id)));
            foreach (var child in node.Children)
                Emit(child, id);
        }

        foreach (var node in smartArt.Nodes)
            Emit(node, docId);

        return new XDocument(
            new XElement(Dgm + "dataModel",
                new XAttribute(XNamespace.Xmlns + "dgm", Dgm.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "a", A.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "r", R.NamespaceName),
                ptLst,
                cxnLst,
                new XElement(Dgm + "bg"),
                new XElement(Dgm + "whole"),
                // F2: point the data model at the rendered-geometry drawing part. The reader reads only
                // ptLst/cxnLst, so this trailing ext is ignored gracefully on round-trip.
                new XElement(Dgm + "dataModelExt",
                    new XAttribute("relId", drawingRelId),
                    new XAttribute("minVer", "http://schemas.openxmlformats.org/drawingml/2006/diagram"))));
    }

    /// <summary>
    /// Builds the SmartArt DATA part's relationships (word/diagrams/_rels/dataN.xml.rels). F2: a single
    /// diagramDrawing relationship from the data part to its rendered-geometry drawing part (drawingN.xml),
    /// which the data part's dgm:dataModelExt/@relId references. Targets are data-part-relative.
    /// </summary>
    private static XDocument BuildDiagramDataRels(SmartArtPart part) => new(
        new XElement(Rel + "Relationships",
            new XElement(Rel + "Relationship",
                new XAttribute("Id", part.DrawingRelationshipId),
                new XAttribute("Type", DiagramDrawingRelType),
                new XAttribute("Target", part.DrawingFileName))));

    /// <summary>
    /// Builds the SmartArt rendered-geometry part (word/diagrams/drawingN.xml — dsp:drawing). F2: computes a
    /// SIMPLE deterministic layout in EMU keyed off the diagram kind and emits one dsp:sp per node, each
    /// carrying its text (dsp:txBody/a:p/a:r/a:t) and an a:xfrm (a:off x/y + a:ext cx/cy), all inside one
    /// dsp:spTree. The layout is heuristic (fixed box size + spacing): List = vertical stack, Process =
    /// horizontal row, Hierarchy = root row centered over a row of descendants. This is presentation only —
    /// the model reconstructs from the data part, so the geometry is never read back.
    /// </summary>
    private static XDocument BuildDiagramDrawing(SmartArt smartArt)
    {
        // Flatten the node tree in pre-order so every node (incl. nested Hierarchy children) gets a shape.
        var nodes = new List<(SmartArtNode Node, int Depth)>();
        void Flatten(SmartArtNode node, int depth)
        {
            nodes.Add((node, depth));
            foreach (var child in node.Children)
                Flatten(child, depth + 1);
        }
        foreach (var node in smartArt.Nodes)
            Flatten(node, 0);

        // Fixed heuristic box geometry (EMU). 1 in = 914400 EMU.
        const long boxW = 1828800;  // 2.0 in
        const long boxH = 685800;   // 0.75 in
        const long gap = 228600;    // 0.25 in

        var spTree = new XElement(Dsp + "spTree");

        for (var i = 0; i < nodes.Count; i++)
        {
            var (node, depth) = nodes[i];
            long x, y;
            switch (smartArt.Kind)
            {
                case SmartArtKind.Process:
                    // Horizontal row of boxes (with a gap acting as the arrow space between steps).
                    x = i * (boxW + gap);
                    y = 0;
                    break;
                case SmartArtKind.Hierarchy:
                    // Simple top-down tree: indent by depth (x) and stack by emission order (y) so children
                    // sit below and to the right of their parent — deterministic and never overlapping.
                    x = depth * (boxW + gap);
                    y = i * (boxH + gap);
                    break;
                default: // List: vertical stack of boxes.
                    x = 0;
                    y = i * (boxH + gap);
                    break;
            }

            spTree.Add(new XElement(Dsp + "sp",
                new XElement(Dsp + "nvSpPr",
                    new XElement(Dsp + "cNvPr",
                        new XAttribute("id", i),
                        new XAttribute("name", $"Node {i}")),
                    new XElement(Dsp + "cNvSpPr"),
                    new XElement(Dsp + "nvPr")),
                new XElement(Dsp + "spPr",
                    new XElement(A + "xfrm",
                        new XElement(A + "off", new XAttribute("x", x), new XAttribute("y", y)),
                        new XElement(A + "ext", new XAttribute("cx", boxW), new XAttribute("cy", boxH))),
                    new XElement(A + "prstGeom", new XAttribute("prst", "rect"),
                        new XElement(A + "avLst"))),
                new XElement(Dsp + "txBody",
                    new XElement(A + "bodyPr"),
                    new XElement(A + "lstStyle"),
                    new XElement(A + "p",
                        new XElement(A + "r",
                            new XElement(A + "t", node.Text))))));
        }

        return new XDocument(
            new XElement(Dsp + "drawing",
                new XAttribute(XNamespace.Xmlns + "dsp", Dsp.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "a", A.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "r", R.NamespaceName),
                spTree));
    }

    /// <summary>
    /// Builds a minimal-but-valid SmartArt LAYOUT part (word/diagrams/layoutN.xml — dgm:layoutDef). The
    /// uniqueId records which stock layout the diagram intends (list / process / hierarchy); the layout body
    /// is intentionally near-empty (Word substitutes the built-in layout for the known uniqueId). The node
    /// text never lives here, so an empty layout does not lose data.
    /// </summary>
    private static XDocument BuildDiagramLayout(SmartArtKind kind)
    {
        var uniqueId = kind switch
        {
            SmartArtKind.Process => "urn:microsoft.com/office/officeart/2005/8/layout/process1",
            SmartArtKind.Hierarchy => "urn:microsoft.com/office/officeart/2005/8/layout/hierarchy1",
            _ => "urn:microsoft.com/office/officeart/2005/8/layout/list1"
        };
        return new XDocument(
            new XElement(Dgm + "layoutDef",
                new XAttribute(XNamespace.Xmlns + "dgm", Dgm.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "a", A.NamespaceName),
                new XAttribute("uniqueId", uniqueId),
                new XElement(Dgm + "title", new XAttribute("val", string.Empty)),
                new XElement(Dgm + "desc", new XAttribute("val", string.Empty)),
                new XElement(Dgm + "catLst"),
                new XElement(Dgm + "sampData"),
                new XElement(Dgm + "styleData"),
                new XElement(Dgm + "clrData"),
                new XElement(Dgm + "layoutNode",
                    new XAttribute("name", "diagram"))));
    }

    /// <summary>
    /// Builds a minimal-but-valid SmartArt QUICKSTYLE part (word/diagrams/quickStyleN.xml — dgm:styleDef).
    /// Stock/near-empty: carries no node data, so an empty style does not lose round-trip content.
    /// </summary>
    private static XDocument BuildDiagramQuickStyle() => new(
        new XElement(Dgm + "styleDef",
            new XAttribute(XNamespace.Xmlns + "dgm", Dgm.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "a", A.NamespaceName),
            new XAttribute("uniqueId", "urn:microsoft.com/office/officeart/2005/8/quickstyle/simple1"),
            new XElement(Dgm + "title", new XAttribute("val", string.Empty)),
            new XElement(Dgm + "desc", new XAttribute("val", string.Empty)),
            new XElement(Dgm + "catLst"),
            new XElement(Dgm + "scene3d",
                new XElement(A + "camera", new XAttribute("prst", "orthographicFront")),
                new XElement(A + "lightRig", new XAttribute("rig", "threePt"), new XAttribute("dir", "t"))),
            new XElement(Dgm + "style")));

    /// <summary>
    /// Builds a minimal-but-valid SmartArt COLORS part (word/diagrams/colorsN.xml — dgm:colorsDef).
    /// Stock/near-empty: carries no node data, so an empty colour set does not lose round-trip content.
    /// </summary>
    private static XDocument BuildDiagramColors() => new(
        new XElement(Dgm + "colorsDef",
            new XAttribute(XNamespace.Xmlns + "dgm", Dgm.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "a", A.NamespaceName),
            new XAttribute("uniqueId", "urn:microsoft.com/office/officeart/2005/8/colors/accent0_1"),
            new XElement(Dgm + "title", new XAttribute("val", string.Empty)),
            new XElement(Dgm + "desc", new XAttribute("val", string.Empty)),
            new XElement(Dgm + "catLst"),
            new XElement(Dgm + "styleLbl", new XAttribute("name", "node0"),
                new XElement(Dgm + "fillClrLst", new XAttribute("meth", "repeat"),
                    new XElement(A + "schemeClr", new XAttribute("val", "accent1"))),
                new XElement(Dgm + "linClrLst", new XAttribute("meth", "repeat"),
                    new XElement(A + "schemeClr", new XAttribute("val", "accent1"))),
                new XElement(Dgm + "txFillClrLst", new XAttribute("meth", "repeat"),
                    new XElement(A + "schemeClr", new XAttribute("val", "lt1"))),
                new XElement(Dgm + "txLinClrLst", new XAttribute("meth", "repeat"),
                    new XElement(A + "schemeClr", new XAttribute("val", "lt1"))))));

    /// <summary>
    /// Builds a DrawingML chart part (c:chartSpace) for <paramref name="part"/>. Emits one plot area holding a
    /// single chart type (c:barChart for column/bar, c:lineChart for line, c:areaChart for area, c:pieChart for
    /// pie, c:doughnutChart for doughnut, c:scatterChart for scatter) with the document's series, plus a
    /// category-axis / value-axis pair for the cartesian kinds. Category labels and series values are embedded
    /// as literal caches (c:strCache / c:numCache) so the chart renders without opening the workbook.
    /// F1: the caches remain the display/round-trip source of truth, but a c:externalData r:id now references
    /// the chart part's companion editable workbook (word/embeddings/…xlsx, wired via the part's own _rels) so
    /// Word's "Edit Data" works. An optional c:legend and per-axis c:title are emitted when the model sets them.
    /// </summary>
    private static XDocument BuildChartSpace(ChartPart part)
    {
        var chart = part.Chart;
        // Stable axis ids referenced by the plot's series-holding chart element (cartesian kinds only).
        const long catAxisId = 111111111L;
        const long valAxisId = 222222222L;

        var hasAxes = chart.Kind is not (ChartKind.Pie or ChartKind.Doughnut);
        var plotContent = chart.Kind switch
        {
            ChartKind.Pie => BuildPieChart(chart, doughnut: false),
            ChartKind.Doughnut => BuildPieChart(chart, doughnut: true),
            ChartKind.Scatter => BuildScatterChart(chart, catAxisId, valAxisId),
            _ => BuildCartesianChart(chart, catAxisId, valAxisId),
        };

        var plotArea = new XElement(C + "plotArea",
            new XElement(C + "layout"),
            plotContent);
        if (hasAxes)
        {
            // Scatter uses a value axis for x (so x-values plot numerically); the other cartesian kinds use a
            // category axis for x. The category-axis title doubles as the scatter x-axis title.
            plotArea.Add(chart.Kind == ChartKind.Scatter
                ? BuildValueAxis(catAxisId, valAxisId, "b", chart.CategoryAxisTitle)
                : BuildCategoryAxis(catAxisId, valAxisId, chart.CategoryAxisTitle));
            plotArea.Add(BuildValueAxis(valAxisId, catAxisId, "l", chart.ValueAxisTitle));
        }

        var chartElement = new XElement(C + "chart");
        if (chart.Title is { Length: > 0 } title)
        {
            chartElement.Add(BuildChartTitle(title));
            chartElement.Add(new XElement(C + "autoTitleDeleted", new XAttribute(C + "val", "0")));
        }
        else
        {
            chartElement.Add(new XElement(C + "autoTitleDeleted", new XAttribute(C + "val", "1")));
        }
        chartElement.Add(plotArea);
        if (chart.ShowLegend)
            chartElement.Add(new XElement(C + "legend",
                new XElement(C + "legendPos", new XAttribute(C + "val", "b")),
                new XElement(C + "overlay", new XAttribute(C + "val", "0"))));
        chartElement.Add(new XElement(C + "plotVisOnly", new XAttribute(C + "val", "1")));

        return new XDocument(
            new XElement(C + "chartSpace",
                new XAttribute(XNamespace.Xmlns + "c", C.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "a", A.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "r", R.NamespaceName),
                chartElement,
                // c:externalData ties the chart to its editable companion workbook (resolved via the chart
                // part's own _rels). autoUpdate=0 keeps the literal caches authoritative for display.
                new XElement(C + "externalData",
                    new XAttribute(R + "id", part.ExternalDataRelId),
                    new XElement(C + "autoUpdate", new XAttribute(C + "val", "0")))));
    }

    /// <summary>
    /// Builds the chart part's own relationships part (word/charts/_rels/chartN.xml.rels): a single "package"
    /// relationship from the chart to its embedded editable workbook (../embeddings/…xlsx). This is what Word's
    /// "Edit Data" follows from the chart, and it is what c:externalData/@r:id resolves against. F1.
    /// </summary>
    private static XDocument BuildChartRels(ChartPart part) => new(
        new XElement(Rel + "Relationships",
            new XElement(Rel + "Relationship",
                new XAttribute("Id", part.ExternalDataRelId),
                new XAttribute("Type", ExternalDataRelType),
                new XAttribute("Target", "../embeddings/" + part.EmbeddingFileName))));

    /// <summary>
    /// Builds a minimal, self-contained xlsx (OPC ZIP) holding the chart's data so Word's "Edit Data" has a
    /// workbook to open (F1). Layout matches the chart's c:f formula refs: row 1 is the header (A1 empty, then
    /// one series name per column B, C, …), rows 2.. are the data (column A = category label, columns B+ = the
    /// aligned series values). A tiny hand-built package — [Content_Types].xml, _rels/.rels, xl/workbook.xml,
    /// xl/_rels/workbook.xml.rels and xl/worksheets/sheet1.xml — with NO dependency on FreeX's xlsx writer.
    /// </summary>
    private static byte[] BuildChartWorkbook(Chart chart)
    {
        XNamespace s = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace sr = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

        // sheetData: one header row (cell A1 blank, B1.. = series names) followed by one row per category.
        var sheetData = new XElement(s + "sheetData");

        var headerRow = new XElement(s + "row", new XAttribute("r", 1));
        for (var c = 0; c < chart.Series.Count; c++)
        {
            var name = chart.Series[c].Name;
            if (!string.IsNullOrEmpty(name))
                headerRow.Add(InlineStringCell(s, $"{ColumnLetter(c + 1)}1", name));
        }
        if (headerRow.HasElements)
            sheetData.Add(headerRow);

        for (var rowIdx = 0; rowIdx < chart.Categories.Count; rowIdx++)
        {
            var rowNumber = rowIdx + 2; // data starts on row 2
            var row = new XElement(s + "row", new XAttribute("r", rowNumber));
            row.Add(InlineStringCell(s, $"A{rowNumber}", chart.Categories[rowIdx]));
            for (var c = 0; c < chart.Series.Count; c++)
            {
                var values = chart.Series[c].Values;
                if (rowIdx < values.Count)
                    row.Add(new XElement(s + "c",
                        new XAttribute("r", $"{ColumnLetter(c + 1)}{rowNumber}"),
                        new XElement(s + "v", values[rowIdx].ToString(System.Globalization.CultureInfo.InvariantCulture))));
            }
            sheetData.Add(row);
        }

        var sheet = new XDocument(
            new XElement(s + "worksheet",
                new XAttribute(XNamespace.Xmlns + "r", sr.NamespaceName),
                sheetData));

        var workbook = new XDocument(
            new XElement(s + "workbook",
                new XAttribute(XNamespace.Xmlns + "r", sr.NamespaceName),
                new XElement(s + "sheets",
                    new XElement(s + "sheet",
                        new XAttribute("name", "Sheet1"),
                        new XAttribute("sheetId", "1"),
                        new XAttribute(sr + "id", "rId1")))));

        var workbookRels = new XDocument(
            new XElement(Rel + "Relationships",
                new XElement(Rel + "Relationship",
                    new XAttribute("Id", "rId1"),
                    new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet"),
                    new XAttribute("Target", "worksheets/sheet1.xml"))));

        var contentTypes = new XDocument(
            new XElement(Ct + "Types",
                new XElement(Ct + "Default", new XAttribute("Extension", "rels"),
                    new XAttribute("ContentType", "application/vnd.openxmlformats-package.relationships+xml")),
                new XElement(Ct + "Default", new XAttribute("Extension", "xml"),
                    new XAttribute("ContentType", "application/xml")),
                new XElement(Ct + "Override", new XAttribute("PartName", "/xl/workbook.xml"),
                    new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml")),
                new XElement(Ct + "Override", new XAttribute("PartName", "/xl/worksheets/sheet1.xml"),
                    new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"))));

        var packageRels = new XDocument(
            new XElement(Rel + "Relationships",
                new XElement(Rel + "Relationship",
                    new XAttribute("Id", "rId1"),
                    new XAttribute("Type", OfficeDocumentRel),
                    new XAttribute("Target", "xl/workbook.xml"))));

        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            WritePart(zip, "[Content_Types].xml", contentTypes);
            WritePart(zip, "_rels/.rels", packageRels);
            WritePart(zip, "xl/workbook.xml", workbook);
            WritePart(zip, "xl/_rels/workbook.xml.rels", workbookRels);
            WritePart(zip, "xl/worksheets/sheet1.xml", sheet);
        }
        return buffer.ToArray();
    }

    /// <summary>Builds an inline-string worksheet cell (t="inlineStr") so the workbook needs no shared-strings part.</summary>
    private static XElement InlineStringCell(XNamespace s, string reference, string text) =>
        new(s + "c",
            new XAttribute("r", reference),
            new XAttribute("t", "inlineStr"),
            new XElement(s + "is", new XElement(s + "t", text)));

    /// <summary>Builds a c:title carrying a single rich-text run with the chart title text.</summary>
    private static XElement BuildChartTitle(string title) =>
        new(C + "title",
            new XElement(C + "tx",
                new XElement(C + "rich",
                    new XElement(A + "bodyPr"),
                    new XElement(A + "lstStyle"),
                    new XElement(A + "p",
                        new XElement(A + "r",
                            new XElement(A + "t", title))))),
            new XElement(C + "overlay", new XAttribute(C + "val", "0")));

    /// <summary>
    /// Builds the c:barChart (column or bar), c:lineChart or c:areaChart element holding the chart's series and
    /// the axis-id back-references. barDir distinguishes column (vertical bars) from bar (horizontal).
    /// </summary>
    private static XElement BuildCartesianChart(Chart chart, long catAxisId, long valAxisId)
    {
        XElement root;
        if (chart.Kind == ChartKind.Line)
        {
            root = new XElement(C + "lineChart",
                new XElement(C + "grouping", new XAttribute(C + "val", "standard")));
        }
        else if (chart.Kind == ChartKind.Area)
        {
            root = new XElement(C + "areaChart",
                new XElement(C + "grouping", new XAttribute(C + "val", "standard")));
        }
        else
        {
            root = new XElement(C + "barChart",
                new XElement(C + "barDir", new XAttribute(C + "val", chart.Kind == ChartKind.Bar ? "bar" : "col")),
                new XElement(C + "grouping", new XAttribute(C + "val", "clustered")));
        }

        for (var i = 0; i < chart.Series.Count; i++)
            root.Add(BuildSeries(chart, chart.Series[i], i));

        root.Add(new XElement(C + "axId", new XAttribute(C + "val", catAxisId)));
        root.Add(new XElement(C + "axId", new XAttribute(C + "val", valAxisId)));
        return root;
    }

    /// <summary>
    /// Builds the c:scatterChart element: each series carries c:xVal (the shared categories parsed as numbers,
    /// or their 1-based ordinal when non-numeric) and c:yVal (the series values). Scatter has axes (both value
    /// axes), referenced by axId back-references like the other cartesian kinds.
    /// </summary>
    private static XElement BuildScatterChart(Chart chart, long xAxisId, long yAxisId)
    {
        var root = new XElement(C + "scatterChart",
            new XElement(C + "scatterStyle", new XAttribute(C + "val", "lineMarker")));
        for (var i = 0; i < chart.Series.Count; i++)
            root.Add(BuildScatterSeries(chart, chart.Series[i], i));
        root.Add(new XElement(C + "axId", new XAttribute(C + "val", xAxisId)));
        root.Add(new XElement(C + "axId", new XAttribute(C + "val", yAxisId)));
        return root;
    }

    /// <summary>Builds the c:pieChart / c:doughnutChart element holding the chart's first series (no axes).</summary>
    private static XElement BuildPieChart(Chart chart, bool doughnut)
    {
        var pie = new XElement(C + (doughnut ? "doughnutChart" : "pieChart"),
            new XElement(C + "varyColors", new XAttribute(C + "val", "1")));
        if (chart.Series.Count > 0)
            pie.Add(BuildSeries(chart, chart.Series[0], 0));
        if (doughnut)
            pie.Add(new XElement(C + "holeSize", new XAttribute(C + "val", "50")));
        return pie;
    }

    /// <summary>
    /// Builds one c:ser: its index/order, an optional c:tx (series name) string cache, the shared category
    /// labels (c:cat → c:strRef/c:strCache) and the numeric values (c:val → c:numRef/c:numCache). The caches
    /// embed the data literally for display/round-trip; the c:f formula refs (Sheet1!…) line up with the
    /// companion workbook (data starts on row 2; series index i lives in column B+i) so Word's "Edit Data"
    /// maps each series to the right column.
    /// </summary>
    private static XElement BuildSeries(Chart chart, ChartSeries series, int index)
    {
        var ser = new XElement(C + "ser",
            new XElement(C + "idx", new XAttribute(C + "val", index)),
            new XElement(C + "order", new XAttribute(C + "val", index)));

        ser.Add(BuildSeriesName(series, index));
        ser.Add(BuildCategoryCache(chart.Categories));
        ser.Add(BuildValueCache(series.Values, index));
        return ser;
    }

    /// <summary>
    /// Builds one c:ser for a scatter chart: c:xVal carries the categories parsed as numbers (1-based ordinal
    /// when a label is non-numeric, so the points still spread along x) and c:yVal carries the series values.
    /// Formula refs line up with the companion workbook (x = column A, y = column B+i, data from row 2).
    /// </summary>
    private static XElement BuildScatterSeries(Chart chart, ChartSeries series, int index)
    {
        var ser = new XElement(C + "ser",
            new XElement(C + "idx", new XAttribute(C + "val", index)),
            new XElement(C + "order", new XAttribute(C + "val", index)));

        ser.Add(BuildSeriesName(series, index));

        var xValues = new List<double>(chart.Categories.Count);
        for (var i = 0; i < chart.Categories.Count; i++)
            xValues.Add(double.TryParse(chart.Categories[i], System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var x) ? x : i + 1);

        ser.Add(new XElement(C + "xVal",
            new XElement(C + "numRef",
                new XElement(C + "f", $"Sheet1!$A$2:$A${xValues.Count + 1}"),
                BuildNumCache(xValues))));
        ser.Add(new XElement(C + "yVal",
            new XElement(C + "numRef",
                new XElement(C + "f", $"Sheet1!${ColumnLetter(index + 1)}$2:${ColumnLetter(index + 1)}${series.Values.Count + 1}"),
                BuildNumCache(series.Values))));
        return ser;
    }

    /// <summary>Builds the optional c:tx (series display name) backed by a single-cell string cache + workbook ref.</summary>
    private static XElement? BuildSeriesName(ChartSeries series, int index)
    {
        if (series.Name is not { Length: > 0 } name)
            return null;
        return new XElement(C + "tx",
            new XElement(C + "strRef",
                new XElement(C + "f", $"Sheet1!${ColumnLetter(index + 1)}$1"),
                new XElement(C + "strCache",
                    new XElement(C + "ptCount", new XAttribute(C + "val", 1)),
                    new XElement(C + "pt", new XAttribute(C + "idx", 0),
                        new XElement(C + "v", name)))));
    }

    /// <summary>Builds c:cat → c:strRef/c:strCache: the shared category labels as a literal string cache.</summary>
    private static XElement BuildCategoryCache(IReadOnlyList<string> categories)
    {
        var cache = new XElement(C + "strCache",
            new XElement(C + "ptCount", new XAttribute(C + "val", categories.Count)));
        for (var i = 0; i < categories.Count; i++)
            cache.Add(new XElement(C + "pt", new XAttribute(C + "idx", i),
                new XElement(C + "v", categories[i])));

        return new XElement(C + "cat",
            new XElement(C + "strRef",
                new XElement(C + "f", $"Sheet1!$A$2:$A${Math.Max(2, categories.Count + 1)}"),
                cache));
    }

    /// <summary>Builds c:val → c:numRef/c:numCache: the series values as a literal number cache for column B+index.</summary>
    private static XElement BuildValueCache(IReadOnlyList<double> values, int index)
    {
        var column = ColumnLetter(index + 1);
        return new XElement(C + "val",
            new XElement(C + "numRef",
                new XElement(C + "f", $"Sheet1!${column}$2:${column}${Math.Max(2, values.Count + 1)}"),
                BuildNumCache(values)));
    }

    /// <summary>Builds a bare c:numCache (formatCode + index-addressed c:pt values) shared by c:val/c:xVal/c:yVal.</summary>
    private static XElement BuildNumCache(IReadOnlyList<double> values)
    {
        var cache = new XElement(C + "numCache",
            new XElement(C + "formatCode", "General"),
            new XElement(C + "ptCount", new XAttribute(C + "val", values.Count)));
        for (var i = 0; i < values.Count; i++)
            cache.Add(new XElement(C + "pt", new XAttribute(C + "idx", i),
                new XElement(C + "v", values[i].ToString(System.Globalization.CultureInfo.InvariantCulture))));
        return cache;
    }

    /// <summary>Maps a 0-based column index to its spreadsheet letter (0→A, 1→B, …, 26→AA) for c:f refs + the workbook.</summary>
    private static string ColumnLetter(int index)
    {
        var letters = string.Empty;
        for (var n = index; n >= 0; n = n / 26 - 1)
            letters = (char)('A' + n % 26) + letters;
        return letters;
    }

    /// <summary>
    /// Builds the c:catAx (category axis) referencing its own id and cross-referencing the value axis, with an
    /// optional axis <paramref name="title"/>.
    /// </summary>
    private static XElement BuildCategoryAxis(long axisId, long crossAxisId, string? title)
    {
        var ax = new XElement(C + "catAx",
            new XElement(C + "axId", new XAttribute(C + "val", axisId)),
            new XElement(C + "scaling", new XElement(C + "orientation", new XAttribute(C + "val", "minMax"))),
            new XElement(C + "delete", new XAttribute(C + "val", "0")),
            new XElement(C + "axPos", new XAttribute(C + "val", "b")));
        if (title is { Length: > 0 } t)
            ax.Add(BuildChartTitle(t));
        ax.Add(new XElement(C + "crossAx", new XAttribute(C + "val", crossAxisId)));
        return ax;
    }

    /// <summary>
    /// Builds the c:valAx (value axis) at the given <paramref name="position"/> referencing its own id and
    /// cross-referencing <paramref name="crossAxisId"/>, with an optional axis <paramref name="title"/>. Reused
    /// for the scatter chart's numeric x-axis (position "b").
    /// </summary>
    private static XElement BuildValueAxis(long axisId, long crossAxisId, string position, string? title)
    {
        var ax = new XElement(C + "valAx",
            new XElement(C + "axId", new XAttribute(C + "val", axisId)),
            new XElement(C + "scaling", new XElement(C + "orientation", new XAttribute(C + "val", "minMax"))),
            new XElement(C + "delete", new XAttribute(C + "val", "0")),
            new XElement(C + "axPos", new XAttribute(C + "val", position)));
        if (title is { Length: > 0 } t)
            ax.Add(BuildChartTitle(t));
        ax.Add(new XElement(C + "crossAx", new XAttribute(C + "val", crossAxisId)));
        return ax;
    }

    private static XElement? BuildRunProperties(RunFormatting f)
    {
        // Children MUST follow the CT_RPr (EG_RPrBase) schema sequence, otherwise Word's strict
        // validator rejects the run. The relevant slots, in order, are:
        //   rFonts, b, i, caps, smallCaps, strike, color, spacing, kern, position, sz, szCs, u, shd,
        //   vertAlign, <w14 extension region>.
        // The advanced-typography elements added for Z1 occupy these slots:
        //   * w:spacing / w:kern / w:position are core EG_RPrBase elements that sit AFTER w:color and
        //     BEFORE w:sz (this exact order — spacing, then kern, then position — is the schema sequence).
        //   * w14:ligatures / w14:numForm / w14:numSpacing / w14:stylisticSets are Office-2010 extension
        //     elements that have no slot in the base CT_RPr sequence; Word emits them at the END of the run
        //     properties (after the core elements above). We follow that placement, emitting them last.
        // (FreeW's own reader is order-independent, so order bugs only surface in Word's strict validator.)
        var rPr = new XElement(W + "rPr");
        if (f.FontFamily is { Length: > 0 } family)
            rPr.Add(new XElement(W + "rFonts", new XAttribute(W + "ascii", family), new XAttribute(W + "hAnsi", family)));
        if (f.Bold)
            rPr.Add(new XElement(W + "b"));
        if (f.Italic)
            rPr.Add(new XElement(W + "i"));
        if (f.AllCaps)
            rPr.Add(new XElement(W + "caps"));
        if (f.SmallCaps)
            rPr.Add(new XElement(W + "smallCaps"));
        if (f.Strikethrough)
            rPr.Add(new XElement(W + "strike"));
        if (f.ColorHex is { Length: > 0 } color)
            rPr.Add(new XElement(W + "color", new XAttribute(W + "val", color.TrimStart('#'))));
        // w:spacing (character spacing, expand/condense) — value in twentieths of a point (dxa), signed.
        // Emitted only when non-zero so default runs round-trip byte-unchanged.
        if (f.CharacterSpacingPt != 0)
            rPr.Add(new XElement(W + "spacing", new XAttribute(W + "val", PointsToDxa(f.CharacterSpacingPt))));
        // w:kern (kerning minimum font size) — value in half-points. Emitted only when a positive
        // threshold is set.
        if (f.KerningMinSizePt is { } kern && kern > 0)
            rPr.Add(new XElement(W + "kern", new XAttribute(W + "val", PointsToHalfPoints(kern))));
        // w:position (raised/lowered baseline) — value in half-points, signed (positive raised). Emitted
        // only when non-zero.
        if (f.PositionPt != 0)
            rPr.Add(new XElement(W + "position", new XAttribute(W + "val", PointsToHalfPoints(f.PositionPt))));
        if (f.FontSizePt is { } size)
        {
            var halfPoints = PointsToHalfPoints(size);
            rPr.Add(new XElement(W + "sz", new XAttribute(W + "val", halfPoints)));
            rPr.Add(new XElement(W + "szCs", new XAttribute(W + "val", halfPoints)));
        }
        if (f.Underline)
            rPr.Add(new XElement(W + "u", new XAttribute(W + "val", "single")));
        if (f.HighlightColorHex is { Length: > 0 } highlight)
            rPr.Add(new XElement(W + "shd",
                new XAttribute(W + "val", "clear"),
                new XAttribute(W + "color", "auto"),
                new XAttribute(W + "fill", highlight.TrimStart('#'))));
        if (f.VerticalAlign is VerticalAlign.Superscript or VerticalAlign.Subscript)
            rPr.Add(new XElement(W + "vertAlign",
                new XAttribute(W + "val", f.VerticalAlign == VerticalAlign.Superscript ? "superscript" : "subscript")));
        // w:rtl (right-to-left run direction) — a toggle that sits after w:vertAlign in EG_RPrBase, before
        // the w14 extension region. Emitted only when set so default runs round-trip byte-unchanged.
        if (f.Rtl)
            rPr.Add(new XElement(W + "rtl"));

        // --- w14 OpenType extension region (after the core EG_RPrBase elements) ---
        // w14:ligatures
        if (LigaturesToken(f.Ligatures) is { } ligatures)
            rPr.Add(new XElement(W14 + "ligatures", new XAttribute(W14 + "val", ligatures)));
        // w14:numForm
        if (NumberFormToken(f.NumberForm) is { } numForm)
            rPr.Add(new XElement(W14 + "numForm", new XAttribute(W14 + "val", numForm)));
        // w14:numSpacing
        if (NumberSpacingToken(f.NumberSpacing) is { } numSpacing)
            rPr.Add(new XElement(W14 + "numSpacing", new XAttribute(W14 + "val", numSpacing)));
        // w14:stylisticSets — a container of w14:styleSet entries; we model a single optional set id.
        if (f.StylisticSet is { } styleSetId)
            rPr.Add(new XElement(W14 + "stylisticSets",
                new XElement(W14 + "styleSet", new XAttribute(W14 + "id", styleSetId))));

        return rPr.HasElements ? rPr : null;
    }

    /// <summary>
    /// Builds a w:sectPr for one section's <paramref name="page"/> settings. Used for both the final
    /// (body-level) section and each non-final section (whose sectPr lives in its last paragraph's pPr),
    /// so the per-section properties are emitted from one place rather than duplicated. The section's own
    /// <paramref name="headerFooterParts"/> wire its w:headerReference/w:footerReference elements (default /
    /// even / first), each referencing the part's relationship id — so multi-section and page-specific
    /// (first-page) headers/footers round-trip. <paramref name="breakKind"/>, when non-null, emits the
    /// section's w:type (the break kind that begins it); the body-level final section passes null.
    /// </summary>
    private static XElement BuildSectionProperties(
        PageSettings page,
        IReadOnlyList<HeaderFooterPart> headerFooterParts,
        SectionBreakKind? breakKind = null) =>
        new(W + "sectPr",
            // Header/footer references must precede pgSz/pgMar in the sectPr schema order. Headers are emitted
            // before footers, each in default→even→first order, reproducing the legacy single-section
            // emission (default header, even header, default footer, even footer) byte-for-byte.
            HeaderFooterReference(headerFooterParts, isHeader: true, HeaderFooterType.Default),
            HeaderFooterReference(headerFooterParts, isHeader: true, HeaderFooterType.Even),
            HeaderFooterReference(headerFooterParts, isHeader: true, HeaderFooterType.First),
            HeaderFooterReference(headerFooterParts, isHeader: false, HeaderFooterType.Default),
            HeaderFooterReference(headerFooterParts, isHeader: false, HeaderFooterType.Even),
            HeaderFooterReference(headerFooterParts, isHeader: false, HeaderFooterType.First),
            // The section break kind (w:type) precedes pgSz in the schema. "nextPage" is Word's default and
            // is emitted explicitly only for non-final sections (the body-level final section passes null).
            breakKind is { } kind
                ? new XElement(W + "type", new XAttribute(W + "val", SectionBreakToken(kind)))
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
            // Columns: w:cols carries the count (w:num) and inter-column gap (w:space, dxa). Emitted
            // unconditionally; w:num="1" is harmless and keeps the section shape stable. @w:sep draws a
            // line between columns; explicit per-column widths (Left/Right presets) switch to
            // @w:equalWidth="0" with one w:col per column (w:w + trailing w:space).
            BuildColumns(page),
            // Vertical alignment of the page content (w:vAlign): emitted only when not Top, so existing
            // documents round-trip unchanged. Schema order places it after w:cols. Justified maps to "both".
            page.VerticalAlignment != PageVerticalAlignment.Top
                ? new XElement(W + "vAlign", new XAttribute(W + "val", VerticalAlignmentToken(page.VerticalAlignment)))
                : null,
            // "Different first page" (w:titlePg): a toggle emitted only when set, after w:vAlign. When set,
            // the section may also carry a distinct first-page header/footer part (w:type="first" above).
            page.DifferentFirstPage ? new XElement(W + "titlePg") : null);

    /// <summary>
    /// Builds the w:headerReference/w:footerReference for the part of the requested kind+type in
    /// <paramref name="parts"/>, or null when this section carries no such part. The @w:type token is
    /// "default"/"even"/"first"; the @r:id references the part's document relationship id.
    /// </summary>
    private static XElement? HeaderFooterReference(
        IReadOnlyList<HeaderFooterPart> parts,
        bool isHeader,
        HeaderFooterType type)
    {
        var part = parts.FirstOrDefault(p => p.IsHeader == isHeader && p.Type == type);
        if (part is null)
            return null;
        var token = type switch
        {
            HeaderFooterType.Even => "even",
            HeaderFooterType.First => "first",
            _ => "default"
        };
        return new XElement(W + (isHeader ? "headerReference" : "footerReference"),
            new XAttribute(W + "type", token),
            new XAttribute(R + "id", part.RelationshipId));
    }

    /// <summary>
    /// Builds a header/footer part's own relationships part (word/_rels/&lt;part&gt;.xml.rels), declaring one
    /// image relationship per inline image the part carries. The image r:embed ids inside the part XML are
    /// PART-LOCAL and resolve against THIS rels file (not document.xml.rels), so header/footer images survive.
    /// </summary>
    private static XDocument BuildHeaderFooterRels(HeaderFooterPart part)
    {
        var relationships = new XElement(Rel + "Relationships");
        foreach (var image in part.Images)
            relationships.Add(new XElement(Rel + "Relationship",
                new XAttribute("Id", image.RelationshipId),
                new XAttribute("Type", ImageRel),
                new XAttribute("Target", "media/" + image.FileName)));
        return new XDocument(relationships);
    }

    /// <summary>Maps a <see cref="SectionBreakKind"/> to its w:sectPr/w:type w:val token.</summary>
    private static string SectionBreakToken(SectionBreakKind kind) => kind switch
    {
        SectionBreakKind.Continuous => "continuous",
        SectionBreakKind.EvenPage => "evenPage",
        SectionBreakKind.OddPage => "oddPage",
        _ => "nextPage"
    };

    /// <summary>Maps a <see cref="PageVerticalAlignment"/> to its w:vAlign w:val token (Justified→"both").</summary>
    private static string VerticalAlignmentToken(PageVerticalAlignment alignment) => alignment switch
    {
        PageVerticalAlignment.Center => "center",
        PageVerticalAlignment.Justified => "both",
        PageVerticalAlignment.Bottom => "bottom",
        _ => "top"
    };

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
    /// Builds the w:cols element (column layout). Always emitted so the section shape stays stable.
    /// For equal-width columns it carries @w:num + @w:space; for explicit unequal widths
    /// (<see cref="PageSettings.ColumnWidthsPt"/>) it carries @w:equalWidth="0" with one w:col per column
    /// (each @w:w plus a trailing @w:space, except the last). @w:sep is added when a line is drawn between
    /// columns. All measurements are dxa (twentieths of a point).
    /// </summary>
    private static XElement BuildColumns(PageSettings page)
    {
        var cols = new XElement(W + "cols", new XAttribute(W + "num", Math.Max(1, page.ColumnCount)));
        if (page.ColumnsLineBetween)
            cols.Add(new XAttribute(W + "sep", "1"));

        var widths = page.ColumnWidthsPt;
        if (widths is { Count: > 1 } && widths.Count == Math.Max(1, page.ColumnCount))
        {
            // Explicit unequal columns (Left/Right presets): equalWidth off + per-column w:col children.
            cols.Add(new XAttribute(W + "equalWidth", "0"));
            cols.Add(new XAttribute(W + "space", PointsToDxa(page.ColumnSpacingPt)));
            for (var i = 0; i < widths.Count; i++)
            {
                var col = new XElement(W + "col", new XAttribute(W + "w", PointsToDxa(widths[i])));
                if (i < widths.Count - 1)
                    col.Add(new XAttribute(W + "space", PointsToDxa(page.ColumnSpacingPt)));
                cols.Add(col);
            }
        }
        else
        {
            cols.Add(new XAttribute(W + "space", PointsToDxa(page.ColumnSpacingPt)));
        }

        return cols;
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

        var styleToken = BorderLineStyles.ToToken(border.LineStyle);
        XElement Edge(string name) => new(W + name,
            new XAttribute(W + "val", styleToken),
            new XAttribute(W + "sz", PointsToEighthPoints(border.WidthPt)),
            new XAttribute(W + "space", 24),
            new XAttribute(W + "color", border.ColorHex.TrimStart('#')));

        return new XElement(W + "pgBorders",
            new XAttribute(W + "offsetFrom", "page"),
            Edge("top"), Edge("left"), Edge("bottom"), Edge("right"));
    }

    // Preserved-numbering merge plan: the smallest disjoint id above FreeW's fixed reservations. FreeW's own
    // abstractNumIds occupy 0..2 and numIds 1..3, so the remapped originals start at abstractNumId 3 / numId 4
    // and increment from there — they can therefore never collide with FreeW's ids regardless of whether FreeW
    // authored a list, and regardless of the original ids' values.
    private const int PreservedAbstractNumIdStart = 3;
    private const int PreservedNumIdStart = 4;

    /// <summary>
    /// The plan for merging a document's preserved (FreeW-unmodelled) numbering alongside FreeW's own under a
    /// disjoint id space. <see cref="AbstractNums"/> / <see cref="Nums"/> are the original
    /// <c>w:abstractNum</c> / <c>w:num</c> elements with their <c>abstractNumId</c> / <c>numId</c> (and the
    /// <c>num→abstractNum</c> reference) rewritten into the reserved high range, preserving all rich formatting
    /// (multilevel, custom start/format/text/indent) verbatim. <see cref="NumIdRemap"/> maps each ORIGINAL
    /// <c>numId</c> to its rewritten value so the body paragraphs' preserved <c>w:numPr</c> point at the right
    /// (re-emitted) <c>w:num</c>.
    /// </summary>
    private sealed record PreservedNumberingPlan(
        IReadOnlyList<XElement> AbstractNums,
        IReadOnlyList<XElement> Nums,
        IReadOnlyDictionary<int, int> NumIdRemap,
        IReadOnlyList<XAttribute> NamespaceDeclarations);

    /// <summary>
    /// Builds a <see cref="PreservedNumberingPlan"/> from the original <c>word/numbering.xml</c>
    /// (<see cref="PreservedParts.OriginalNumbering"/>) when at least one paragraph kept a
    /// <see cref="Paragraph.PreservedNumbering"/> OR at least one paragraph STYLE kept a
    /// <see cref="DocumentStyle.PreservedNumbering"/> (i.e. FreeW did not model its numbering). Every original
    /// <c>w:abstractNum</c> and <c>w:num</c> is cloned and re-id'd into the disjoint high range
    /// (abstractNumId&gt;=<see cref="PreservedAbstractNumIdStart"/>, numId&gt;=<see cref="PreservedNumIdStart"/>),
    /// with each <c>w:num</c>'s <c>w:abstractNumId</c> reference rewritten to its definition's new id, so the
    /// preserved definitions stay internally consistent and never collide with FreeW's fixed ids. Returns null
    /// when there is no original numbering or no paragraph preserved a <c>numPr</c> (so an authored-from-scratch
    /// or FreeW-only-lists document gets no plan and is unaffected).
    /// </summary>
    private static PreservedNumberingPlan? BuildPreservedNumberingPlan(TextDocument document)
    {
        var original = document.Preserved.OriginalNumbering;
        if (original is null)
            return null;
        // Trigger when EITHER a body paragraph kept an original numPr OR a paragraph STYLE definition carries
        // one (style-level numbering FreeW does not model). Both re-emit against the preserved numbering.xml
        // under the same disjoint-id remap, so a document that only has style-level numbering still builds a
        // plan. Authored-from-scratch / FreeW-only-lists documents have neither, so the plan stays null.
        var hasParagraphPreserved = EnumerateParagraphs(document).Any(p => p.PreservedNumbering is not null);
        var hasStylePreserved = document.Styles.Values.Any(s => s.PreservedNumbering is not null);
        if (!hasParagraphPreserved && !hasStylePreserved)
            return null;

        // Remap every original abstractNumId → a fresh disjoint id, in document order, so num→abstract
        // references can be rewritten consistently.
        var abstractRemap = new Dictionary<int, int>();
        var nextAbstractId = PreservedAbstractNumIdStart;
        foreach (var abstractNum in original.Elements(W + "abstractNum"))
        {
            var id = ParseInt(abstractNum.Attribute(W + "abstractNumId")?.Value);
            if (!abstractRemap.ContainsKey(id))
                abstractRemap[id] = nextAbstractId++;
        }

        // Remap every original numId → a fresh disjoint id and rewrite its abstractNumId reference.
        var numRemap = new Dictionary<int, int>();
        var nextNumId = PreservedNumIdStart;
        var remappedNums = new List<XElement>();
        foreach (var num in original.Elements(W + "num"))
        {
            var id = ParseInt(num.Attribute(W + "numId")?.Value);
            if (numRemap.ContainsKey(id))
                continue;
            var newNumId = nextNumId++;
            numRemap[id] = newNumId;

            var clone = new XElement(num);
            clone.SetAttributeValue(W + "numId", newNumId);
            var abstractRef = clone.Element(W + "abstractNumId");
            var refId = ParseInt(abstractRef?.Attribute(W + "val")?.Value);
            if (abstractRef is not null && abstractRemap.TryGetValue(refId, out var newAbstractId))
                abstractRef.SetAttributeValue(W + "val", newAbstractId);
            remappedNums.Add(clone);
        }

        // Clone and re-id the abstract definitions (formatting otherwise verbatim).
        var remappedAbstracts = new List<XElement>();
        foreach (var abstractNum in original.Elements(W + "abstractNum"))
        {
            var id = ParseInt(abstractNum.Attribute(W + "abstractNumId")?.Value);
            var clone = new XElement(abstractNum);
            clone.SetAttributeValue(W + "abstractNumId", abstractRemap[id]);
            remappedAbstracts.Add(clone);
        }

        // Carry the original root's namespace declarations (other than the default w, which the merged root
        // already declares) so any extension-prefixed content inside the preserved definitions — e.g.
        // w15:restartNumberingAfterBreak, mc:Ignorable, w14:* — keeps a valid prefix when re-emitted.
        var namespaceDeclarations = original.Attributes()
            .Where(a => a.IsNamespaceDeclaration && a.Value != W.NamespaceName)
            .Select(a => new XAttribute(a.Name, a.Value))
            .ToList();

        return new PreservedNumberingPlan(remappedAbstracts, remappedNums, numRemap, namespaceDeclarations);
    }

    /// <summary>
    /// Builds word/numbering.xml: three abstract numbering definitions — bullet (abstractNumId 0),
    /// decimal (abstractNumId 1) and a multilevel/legal outline (abstractNumId 2) — each with
    /// <see cref="ListLevelCount"/> levels, mapped to w:num ids <see cref="BulletNumId"/>/
    /// <see cref="NumberNumId"/>/<see cref="MultiLevelNumId"/>. When <paramref name="includeFreeWNumbering"/>
    /// is false (a preserved-only document) FreeW's definitions are omitted; the <paramref name="preserved"/>
    /// plan, when present, contributes the merged original definitions under a disjoint id range.
    /// </summary>
    /// <remarks>
    /// The bullet and decimal definitions reuse one fixed lvlText across every level. The multilevel
    /// definition instead gives each level its own lvlText that accumulates the ancestor counters —
    /// level 0 = <c>%1.</c>, level 1 = <c>%1.%2.</c>, level 2 = <c>%1.%2.%3.</c>, … — so Word renders
    /// the familiar outline form (1, 1.1, 1.1.1). Every multilevel level is w:numFmt="decimal" and the
    /// indent grows one step (18pt) per level.
    /// </remarks>
    private static XDocument BuildNumbering(bool includeFreeWNumbering, PreservedNumberingPlan? preserved)
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
            new XAttribute(XNamespace.Xmlns + "w", W.NamespaceName));

        // Re-declare any extra namespaces the preserved definitions use (so extension-prefixed children keep
        // a valid prefix). The default w namespace is already declared above and is filtered out of the list.
        if (preserved is not null)
            foreach (var ns in preserved.NamespaceDeclarations)
                numbering.Add(new XAttribute(ns.Name, ns.Value));

        // w:abstractNum elements must precede every w:num (CT_Numbering schema order), so emit all abstract
        // definitions first (FreeW's, then the preserved-and-remapped ones) and all w:num after.
        // FreeW's own abstract definitions use the historical fixed ids 0/1/2, emitted only when FreeW
        // actually authored a list — so a preserved-only document carries just the original definitions.
        if (includeFreeWNumbering)
        {
            numbering.Add(
                AbstractNum(0, "bullet", "•"),
                AbstractNum(1, "decimal", "%1."),
                MultiLevelAbstractNum(2));
        }
        // The preserved original abstractNum definitions, already remapped to a disjoint id range
        // (abstractNumId>=3) by BuildPreservedNumberingPlan, re-emitted verbatim with their rich formatting.
        if (preserved is not null)
            numbering.Add(preserved.AbstractNums.Select(a => new XElement(a)));

        if (includeFreeWNumbering)
        {
            numbering.Add(
                Num(BulletNumId, 0),
                Num(NumberNumId, 1),
                Num(MultiLevelNumId, 2));
        }
        // The preserved w:num instances, already remapped (numId>=4 → remapped abstractNumId) so they never
        // collide with FreeW's fixed 1/2/3 and the body paragraphs' re-emitted numPr resolve to a valid w:num.
        if (preserved is not null)
            numbering.Add(preserved.Nums.Select(n => new XElement(n)));

        return new XDocument(numbering);
    }

    /// <summary>
    /// The subsequence of the CT_Settings (w:settings) child schema order that FreeW's modelled toggles occupy,
    /// used to place each overlaid element at its correct position relative to a preserved settings part's
    /// existing (unmodelled) children. Only the names FreeW emits — plus the immediate neighbours that matter
    /// for ordering — need listing; any unmodelled element not here keeps its relative position because the
    /// overlay only inserts FreeW's elements and never reorders the originals. The full schema order
    /// (ISO/IEC 29500 §17.15.1.78) places these as: displayBackgroundShape … embedTrueTypeFonts …
    /// documentProtection … autoHyphenation … evenAndOddHeaders.
    /// </summary>
    private static readonly string[] CtSettingsOrder =
    [
        "writeProtection", "view", "zoom", "removePersonalInformation", "doNotDisplayPageBoundaries",
        "displayBackgroundShape", "printPostScriptOverText", "printFractionalCharacterWidth", "printFormsData",
        "embedTrueTypeFonts", "embedSystemFonts", "saveSubsetFonts", "saveFormsData", "mirrorMargins",
        "alignBordersAndEdges", "bordersDoNotSurroundHeader", "bordersDoNotSurroundFooter", "gutterAtTop",
        "hideSpellingErrors", "hideGrammaticalErrors", "activeWritingStyle", "proofState", "formsDesign",
        "attachedTemplate", "linkStyles", "stylePaneFormatFilter", "stylePaneSortMethod", "documentType",
        "mailMerge", "revisionView", "trackChanges", "doNotTrackMoves", "doNotTrackFormatting",
        "documentProtection", "autoFormatOverride", "styleLockTheme", "styleLockQFSet", "defaultTabStop",
        "autoHyphenation", "consecutiveHyphenLimit", "hyphenationZone", "doNotHyphenateCaps", "showEnvelope",
        "summaryLength", "clickAndTypeStyle", "defaultTableStyle", "evenAndOddHeaders"
    ];

    /// <summary>
    /// Builds word/settings.xml (w:settings) carrying any combination of: the page-background display
    /// toggle (w:displayBackgroundShape, when <paramref name="displayBackground"/> so Word paints the
    /// w:background), the automatic-hyphenation toggle (w:autoHyphenation), the different-odd/even-headers
    /// toggle (w:evenAndOddHeaders, when <paramref name="differentOddEvenPages"/>), the embed-TrueType-fonts
    /// toggle (w:embedTrueTypeFonts) and the document-protection element (w:documentProtection: w:edit +
    /// w:enforcement="1").
    ///
    /// <para>
    /// When <paramref name="original"/> is null (an authored-from-scratch document) a FRESH minimal part is
    /// emitted with exactly FreeW's modelled children in CT_Settings schema order — byte-equivalent to before.
    /// When <paramref name="original"/> is the preserved settings element captured on read, FreeW's modelled
    /// elements are OVERLAID onto it — each removed then re-inserted at its CT_Settings schema position — so the
    /// document's unmodelled settings (compat flags, default tab stop, rsids, proofing, …) survive while
    /// FreeW's features still apply.
    /// </para>
    /// </summary>
    private static XDocument BuildSettings(ProtectionSettings protection, PageSettings page, bool displayBackground, bool embedTrueTypeFonts, XElement? original)
    {
        var autoHyphenation = page.AutoHyphenation;
        var differentOddEvenPages = page.DifferentOddEvenPages;
        // The hyphenation sub-options only apply when automatic hyphenation is on, and each is emitted only
        // when it differs from Word's default (zone 0 = default, limit 0 = no limit, caps hyphenated): a
        // limit > 0, an explicit zone > 0, and the do-not-hyphenate-caps toggle.
        var consecutiveLimit = autoHyphenation && page.ConsecutiveHyphenLimit > 0
            ? new XElement(W + "consecutiveHyphenLimit", new XAttribute(W + "val", page.ConsecutiveHyphenLimit))
            : null;
        var hyphenationZone = autoHyphenation && page.HyphenationZonePt > 0
            ? new XElement(W + "hyphenationZone", new XAttribute(W + "val", PointsToDxa(page.HyphenationZonePt)))
            : null;
        var doNotHyphenateCaps = autoHyphenation && page.DoNotHyphenateCaps
            ? new XElement(W + "doNotHyphenateCaps")
            : null;

        // Authored-from-scratch (no preserved settings): emit a fresh minimal part with exactly FreeW's modelled
        // children in the historical emission order, byte-for-byte as before — no overlay machinery involved.
        if (original is null)
        {
            var fresh = new XElement(W + "settings",
                new XAttribute(XNamespace.Xmlns + "w", W.NamespaceName));
            if (embedTrueTypeFonts)
                fresh.Add(new XElement(W + "embedTrueTypeFonts"));
            if (displayBackground)
                fresh.Add(new XElement(W + "displayBackgroundShape"));
            if (autoHyphenation)
                fresh.Add(new XElement(W + "autoHyphenation"));
            // Hyphenation sub-options follow autoHyphenation in CT_Settings schema order.
            if (consecutiveLimit is not null)
                fresh.Add(consecutiveLimit);
            if (hyphenationZone is not null)
                fresh.Add(hyphenationZone);
            if (doNotHyphenateCaps is not null)
                fresh.Add(doNotHyphenateCaps);
            if (differentOddEvenPages)
                fresh.Add(new XElement(W + "evenAndOddHeaders"));
            if (ProtectionEditToken(protection.Mode) is { } freshEdit)
                fresh.Add(new XElement(W + "documentProtection",
                    new XAttribute(W + "edit", freshEdit),
                    new XAttribute(W + "enforcement", "1")));
            return new XDocument(fresh);
        }

        // Preserved settings: overlay each modelled element onto a clone of the original (never mutating the
        // model). Each is removed (so we replace, not duplicate) then re-inserted at its CT_Settings schema
        // position; an element whose feature is off is simply removed, since the modelled value — not the
        // preserved one — is authoritative for these. Unmodelled settings keep their place.
        var settings = new XElement(original);
        OverlaySetting(settings, "embedTrueTypeFonts", embedTrueTypeFonts ? new XElement(W + "embedTrueTypeFonts") : null);
        OverlaySetting(settings, "displayBackgroundShape", displayBackground ? new XElement(W + "displayBackgroundShape") : null);
        OverlaySetting(settings, "autoHyphenation", autoHyphenation ? new XElement(W + "autoHyphenation") : null);
        OverlaySetting(settings, "consecutiveHyphenLimit", consecutiveLimit);
        OverlaySetting(settings, "hyphenationZone", hyphenationZone);
        OverlaySetting(settings, "doNotHyphenateCaps", doNotHyphenateCaps);
        OverlaySetting(settings, "evenAndOddHeaders", differentOddEvenPages ? new XElement(W + "evenAndOddHeaders") : null);
        OverlaySetting(settings, "documentProtection",
            ProtectionEditToken(protection.Mode) is { } edit
                ? new XElement(W + "documentProtection",
                    new XAttribute(W + "edit", edit),
                    new XAttribute(W + "enforcement", "1"))
                : null);
        return new XDocument(settings);
    }

    /// <summary>
    /// Replaces (or removes) the w:&lt;localName&gt; child of a w:settings element with
    /// <paramref name="replacement"/>: any existing element of that name is removed first, then — when a
    /// replacement is supplied — it is inserted at the element's CT_Settings schema position (after the last
    /// existing child that sorts at or before it, mirroring the schema sequence). Unmodelled children keep their
    /// relative order because only FreeW's modelled elements are inserted.
    /// </summary>
    private static void OverlaySetting(XElement settings, string localName, XElement? replacement)
    {
        settings.Elements(W + localName).Remove();
        if (replacement is null)
            return;

        var targetIndex = Array.IndexOf(CtSettingsOrder, localName);
        // With no known schema index, append (degrade gracefully rather than mis-order); with one, insert after
        // the last existing child whose own schema index is <= the target (unknown children sort as "after all
        // known", so they never displace a known insertion point).
        XElement? insertAfter = null;
        if (targetIndex >= 0)
            foreach (var child in settings.Elements())
            {
                var childIndex = child.Name.Namespace == W
                    ? Array.IndexOf(CtSettingsOrder, child.Name.LocalName)
                    : -1;
                if (childIndex >= 0 && childIndex <= targetIndex)
                    insertAfter = child;
            }

        if (insertAfter is null)
            settings.AddFirst(replacement);
        else
            insertAfter.AddAfterSelf(replacement);
    }

    // The b:SourceType token Word uses for each FreeW SourceType.
    private static string BibliographySourceTypeName(SourceType type) => type switch
    {
        SourceType.JournalArticle => "JournalArticle",
        SourceType.WebSite => "DocumentFromInternetSite",
        _ => "Book",
    };

    /// <summary>
    /// Builds word/bibliography/sources.xml: a b:Sources element carrying the document's selected
    /// <see cref="TextDocument.BibliographyStyle"/> (its <c>SelectedStyle</c> attribute as the style name)
    /// and one b:Source per <see cref="Source"/>. Each source records its tag, type and populated fields —
    /// the author as a single corporate author (b:Author/b:Author/b:Corporate) since FreeW models author as
    /// one string — so both the chosen style and every source field round-trip via
    /// <c>DocxReader.ReadBibliography</c>. Only non-empty fields are emitted.
    /// </summary>
    private static XDocument BuildBibliographySources(TextDocument document)
    {
        var sources = new XElement(B + "Sources",
            new XAttribute(XNamespace.Xmlns + "b", B.NamespaceName),
            new XAttribute("SelectedStyle", Citations.StyleName(document.BibliographyStyle)));

        foreach (var source in document.Sources)
        {
            var element = new XElement(B + "Source",
                new XElement(B + "Tag", source.Tag),
                new XElement(B + "SourceType", BibliographySourceTypeName(source.Type)));

            if (!string.IsNullOrEmpty(source.Author))
                element.Add(new XElement(B + "Author",
                    new XElement(B + "Author",
                        new XElement(B + "Corporate", source.Author))));

            AddBibliographyField(element, "Title", source.Title);
            AddBibliographyField(element, "Year", source.Year);
            AddBibliographyField(element, "Publisher", source.Publisher);
            AddBibliographyField(element, "JournalName", source.Journal);
            AddBibliographyField(element, "Volume", source.Volume);
            AddBibliographyField(element, "Issue", source.Issue);
            AddBibliographyField(element, "Pages", source.Pages);
            AddBibliographyField(element, "URL", source.Url);
            AddBibliographyField(element, "YearAccessed", source.Accessed);

            sources.Add(element);
        }

        return new XDocument(sources);

        static void AddBibliographyField(XElement parent, string localName, string? value)
        {
            if (!string.IsNullOrEmpty(value))
                parent.Add(new XElement(B + localName, value));
        }
    }

    /// <summary>
    /// Builds word/fontTable.xml (w:fonts): one w:font w:name="&lt;Family&gt;" per embedded family, each
    /// carrying a w:embedRegular/w:embedBold/w:embedItalic/w:embedBoldItalic child per embedded style whose
    /// r:id points at the obfuscated .odttf part (in the fontTable's own rels) and whose w:fontKey is the
    /// deterministic GUID used to obfuscate that part.
    /// </summary>
    private static XDocument BuildFontTable(IReadOnlyList<FontTablePart> embeddedFonts)
    {
        var fonts = new XElement(W + "fonts",
            new XAttribute(XNamespace.Xmlns + "w", W.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "r", R.NamespaceName));
        foreach (var family in embeddedFonts)
        {
            var font = new XElement(W + "font", new XAttribute(W + "name", family.Font.Family));
            foreach (var part in family.Parts)
                font.Add(new XElement(W + part.Slot,
                    new XAttribute(R + "id", part.RelationshipId),
                    new XAttribute(W + "fontKey", part.FontKey)));
            fonts.Add(font);
        }
        return new XDocument(fonts);
    }

    /// <summary>
    /// Builds word/_rels/fontTable.xml.rels: one font relationship (rIdFontN → fonts/fontN.odttf) per
    /// embedded font style, matching the r:id referenced from each w:embed* in word/fontTable.xml.
    /// </summary>
    private static XDocument BuildFontTableRels(IReadOnlyList<FontTablePart> embeddedFonts)
    {
        var relationships = new XElement(Rel + "Relationships");
        foreach (var part in embeddedFonts.SelectMany(f => f.Parts))
            relationships.Add(new XElement(Rel + "Relationship",
                new XAttribute("Id", part.RelationshipId),
                new XAttribute("Type", FontRelType),
                new XAttribute("Target", "fonts/" + part.FileName)));
        return new XDocument(relationships);
    }

    /// <summary>
    /// Builds word/theme/theme1.xml (a:theme): the document's <see cref="DocumentTheme"/> serialised as a
    /// real DrawingML theme — an a:clrScheme (the twelve <see cref="ThemeColorScheme"/> colour slots), an
    /// a:fontScheme (major = the theme's heading font, minor = its body font) and a minimal-but-valid
    /// a:fmtScheme (one fill/line/effect/bg-fill entry each, as Word requires at least one of each). The
    /// reader recovers the colour + font scheme and infers the preset (see DocxReader.ReadTheme).
    /// </summary>
    private static XDocument BuildTheme(DocumentTheme theme)
    {
        var scheme = theme.ColorScheme;

        XElement Srgb(string slot, string hex) =>
            new(A + slot, new XElement(A + "srgbClr", new XAttribute("val", hex)));

        var clrScheme = new XElement(A + "clrScheme",
            new XAttribute("name", theme.Name),
            // dk1/lt1 are conventionally emitted as window/windowText sysClr with an srgb lastClr; using a
            // plain srgbClr is equally valid and keeps the reader's parse uniform across all twelve slots.
            Srgb("dk1", scheme.Dark1),
            Srgb("lt1", scheme.Light1),
            Srgb("dk2", scheme.Dark2),
            Srgb("lt2", scheme.Light2),
            Srgb("accent1", scheme.Accent1),
            Srgb("accent2", scheme.Accent2),
            Srgb("accent3", scheme.Accent3),
            Srgb("accent4", scheme.Accent4),
            Srgb("accent5", scheme.Accent5),
            Srgb("accent6", scheme.Accent6),
            Srgb("hlink", scheme.Hyperlink),
            Srgb("folHlink", scheme.FollowedHyperlink));

        XElement Font(string element, string typeface) =>
            new(A + element,
                new XElement(A + "latin", new XAttribute("typeface", typeface)),
                new XElement(A + "ea", new XAttribute("typeface", string.Empty)),
                new XElement(A + "cs", new XAttribute("typeface", string.Empty)));

        var fontScheme = new XElement(A + "fontScheme",
            new XAttribute("name", theme.Name),
            new XElement(A + "majorFont", Font("latin", theme.HeadingFont).Elements()),
            new XElement(A + "minorFont", Font("latin", theme.BodyFont).Elements()));

        // A minimal-but-valid format scheme: one of each style the schema requires at least one of
        // (fill / line / effect / background fill). A flat solid fill keyed to phClr is the simplest
        // shape Word accepts; the document never reads these back (only the clr/font schemes round-trip).
        XElement SolidPhClr() => new(A + "solidFill", new XElement(A + "schemeClr", new XAttribute("val", "phClr")));

        var fmtScheme = new XElement(A + "fmtScheme",
            new XAttribute("name", theme.Name),
            new XElement(A + "fillStyleLst", SolidPhClr(), SolidPhClr(), SolidPhClr()),
            new XElement(A + "lnStyleLst",
                Line(), Line(), Line()),
            new XElement(A + "effectStyleLst",
                new XElement(A + "effectStyle", new XElement(A + "effectLst")),
                new XElement(A + "effectStyle", new XElement(A + "effectLst")),
                new XElement(A + "effectStyle", new XElement(A + "effectLst"))),
            new XElement(A + "bgFillStyleLst", SolidPhClr(), SolidPhClr(), SolidPhClr()));

        var themeElements = new XElement(A + "themeElements", clrScheme, fontScheme, fmtScheme);

        return new XDocument(
            new XElement(A + "theme",
                new XAttribute(XNamespace.Xmlns + "a", A.NamespaceName),
                new XAttribute("name", theme.Name),
                themeElements,
                new XElement(A + "objectDefaults"),
                new XElement(A + "extraClrSchemeLst")));

        static XElement Line() => new(A + "ln",
            new XAttribute("w", 6350),
            new XAttribute("cap", "flat"),
            new XAttribute("cmpd", "sng"),
            new XAttribute("algn", "ctr"),
            new XElement(A + "solidFill", new XElement(A + "schemeClr", new XAttribute("val", "phClr"))),
            new XElement(A + "prstDash", new XAttribute("val", "solid")));
    }

    private static XDocument BuildStyles(TextDocument document, PreservedNumberingPlan? preservedNumbering = null)
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
            // Style-level numbering FreeW does not model: when the style carried an original w:numPr and the
            // merge plan remapped that numId (a definition exists in the preserved numbering.xml), re-emit a
            // w:pPr/w:numPr pointing at the REMAPPED numId (disjoint from FreeW's fixed ids), keeping the
            // original ilvl. w:pPr precedes w:rPr in CT_Style schema order. A numPr whose numId the plan did
            // not remap (no matching w:num) is dropped, exactly like a paragraph's preserved numPr.
            if (preservedNumbering is not null
                && style.PreservedNumbering is { } sn
                && preservedNumbering.NumIdRemap.TryGetValue(sn.NumId, out var mappedNumId))
            {
                element.Add(new XElement(W + "pPr",
                    new XElement(W + "numPr",
                        new XElement(W + "ilvl", new XAttribute(W + "val", sn.Ilvl)),
                        new XElement(W + "numId", new XAttribute(W + "val", mappedNumId)))));
            }
            var rPr = BuildRunProperties(style.Run);
            if (rPr is not null)
                element.Add(rPr);
            styles.Add(element);
        }

        return new XDocument(styles);
    }
}
