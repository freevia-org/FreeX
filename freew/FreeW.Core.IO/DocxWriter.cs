using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using Free.Shared.Opc;
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
    private const string FreeWChartDesignExtensionUri = "urn:freew:chart-design:2026";
    private static readonly XNamespace Mc = "http://schemas.openxmlformats.org/markup-compatibility/2006";

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

    public static void Write(TextDocument document, Stream stream) =>
        Write(document, stream, DocxWriteOptions.Docx);

    public static void Write(TextDocument document, Stream stream, DocxWriteOptions options)
    {
        // Preserve-pass-through parts are emitted later, but their names must be reserved before assigning
        // modelled media/chart names so a read-then-edited package cannot produce duplicate OPC entries.
        var preservedParts = options.IncludeMacroParts
            ? (IReadOnlyList<PreservedPart>)document.Preserved.Parts
            : document.Preserved.Parts.Where(p => !DocxWriteOptions.IsMacroPart(p.PartName)).ToList();
        preservedParts = RemoveCurrentBibliographyCustomXml(preservedParts);
        var usedPartNames = CreateUsedPartNameSet(preservedParts);

        // Assign a relationship + media id to every inline image up front so document.xml, the
        // document relationships and the media parts all agree on rId/imageN.png.
        var images = CollectImages(document, usedPartNames);
        // Assign a relationship + part name to every inline chart the same way (charts are a separate XML
        // part referenced from the run drawing by r:id, mirroring how images add a media part + r:embed).
        // Assign a relationship + binary part name to every inline embedded OLE object the same way. Each
        // object's presentation icon is collected as an extra ImagePart appended to `images`, so the icon
        // media part + relationship + png content-type flow through the existing image plumbing untouched.
        var embeddedObjects = CollectEmbeddedObjects(document, images, usedPartNames);
        // Charts and SmartArt share the document-wide wp:docPr id space with ordinary images and shapes.
        // Collect embedded-object icons first so every subsequent drawing family can reserve a disjoint range.
        var charts = CollectCharts(document, usedPartNames, drawingIdSeed: images.Count);
        // Assign four relationship ids + four part names to every inline SmartArt diagram the same way
        // (a diagram is four separate XML parts referenced from the run drawing by dgm:relIds).
        var smartArts = CollectSmartArts(document, drawingIdSeed: images.Count + charts.Count);
        // Assign an external relationship id to every distinct hyperlink target the same way.
        var hyperlinks = CollectHyperlinks(document);
        // Master-document anchors are ordered run content backed by their own external relationship type.
        var subDocuments = CollectSubDocuments(document);
        // Chapter-prefixed PAGE fields require Word's linked heading-list metadata even when the
        // heading paragraph did not otherwise carry a list instance.
        var chapterPageNumberingLevels = document.Sections
            .Select(section => section.Page.PageNumberChapterStyleLevel)
            .Where(level => level is >= 1 and <= 9)
            .Select(level => level!.Value)
            .ToHashSet();

        // Emit a numbering part when a paragraph is decorated as a list or a chapter-numbered
        // section needs the linked heading-list definition.
        var hasLists = EnumerateStoryParagraphs(document).Any(p => p.Formatting.ListKind != ListKind.None);
        var includeFreeWNumbering = hasLists || chapterPageNumberingLevels.Count > 0;

        // Preserved numbering FreeW does not model: when the source carried a numbering.xml AND at least one
        // paragraph (or paragraph STYLE) kept its original w:numPr (because FreeW did not map it to a ListKind),
        // build a merge plan
        // that re-emits the ORIGINAL abstractNum/num definitions alongside FreeW's own under a DISJOINT id range
        // (originals remapped to abstractNumId>=3 / numId>=4, clear of FreeW's fixed 0..2 / 1..3). The plan also
        // maps each original numId to its output numId so the preserved paragraphs' w:numPr re-emit consistently.
        // Null for an authored-from-scratch / FreeW-only-lists document, so such a document is unaffected.
        var preservedNumbering = BuildPreservedNumberingPlan(document);

        // List restart overrides: each distinct (ListKind, ListLevel, StartAt) tuple on a paragraph with
        // ListStartOverride set gets a dedicated w:num that clones the base abstractNumId and adds a
        // w:lvlOverride/@w:startOverride. The override numIds occupy the range immediately above the preserved
        // numIds (so they remain disjoint from both FreeW's fixed 1/2/3 and the preserved 4..N). BuildNumbering
        // emits the extra w:num elements; BuildParagraphProperties references them instead of the base numId.
        var restartOverrides = BuildRestartOverrides(document, preservedNumbering);

        // A numbering part is emitted when FreeW authored a list OR preserved numbering must be re-emitted.
        var emitNumbering = includeFreeWNumbering || preservedNumbering is not null;

        // Header/footer parts are now modelled per-section: one part per (section × header/footer × type)
        // slot that carries visible content, each with its OWN image relationships. The final/body-level
        // section owns the legacy header1/footer1/header2/footer2 names so single-section documents stay
        // byte-equivalent (see CollectHeaderFooterParts). Even/first parts are only included when the owning
        // section's page settings turn on different-odd/even or different-first-page respectively.
        var headerFooterParts = CollectHeaderFooterParts(document, usedPartNames);

        // A footnotes part is emitted only when the document actually carries footnotes.
        var hasFootnotes = document.Footnotes.Count > 0;

        // An endnotes part is emitted only when the document actually carries endnotes.
        var hasEndnotes = document.Endnotes.Count > 0;

        // A comments part is emitted only when the document actually carries review comments.
        var hasComments = document.Comments.Count > 0;

        // Inline images carried by comment paragraphs (e.g. a pasted picture in a comment). Each becomes a
        // part-local media file + a relationship in word/_rels/comments.xml.rels, so comment-part images
        // round-trip referenced rather than orphaned. Empty for text-only comments.
        var commentImages = hasComments ? CollectCommentImages(document, usedPartNames) : new List<ImagePart>();
        var footnoteImages = hasFootnotes
            ? CollectNoteImages(document.Footnotes.Values.OrderBy(note => note.Id).SelectMany(note => note.Content), "footnote", usedPartNames)
            : new List<ImagePart>();
        var endnoteImages = hasEndnotes
            ? CollectNoteImages(document.Endnotes.Values.OrderBy(note => note.Id).SelectMany(note => note.Content), "endnote", usedPartNames)
            : new List<ImagePart>();
        var commentPreservedDrawings = hasComments
            ? CollectPartLocalPreservedDrawings(FlattenComments(document).SelectMany(comment => comment.Content), preservedParts)
            : [];
        var footnotePreservedDrawings = hasFootnotes
            ? CollectPartLocalPreservedDrawings(document.Footnotes.Values.SelectMany(note => note.Content), preservedParts)
            : [];
        var endnotePreservedDrawings = hasEndnotes
            ? CollectPartLocalPreservedDrawings(document.Endnotes.Values.SelectMany(note => note.Content), preservedParts)
            : [];
        var footnoteHyperlinks = hasFootnotes
            ? CollectHyperlinks(document.Footnotes.Values.OrderBy(note => note.Id).SelectMany(note => note.Content))
            : new Dictionary<string, string>(StringComparer.Ordinal);
        var endnoteHyperlinks = hasEndnotes
            ? CollectHyperlinks(document.Endnotes.Values.OrderBy(note => note.Id).SelectMany(note => note.Content))
            : new Dictionary<string, string>(StringComparer.Ordinal);
        var commentHyperlinks = hasComments
            ? CollectHyperlinks(FlattenComments(document).SelectMany(comment => comment.Content))
            : new Dictionary<string, string>(StringComparer.Ordinal);

        // The watermark options (or legacy text) are persisted as custom document properties
        // (docProps/custom.xml). WatermarkOptions takes precedence; a legacy Watermark text is used
        // as a fallback (migrated on load to EffectiveWatermark).
        var hasWatermark = document.Page.WatermarkOptions is not null
            || !string.IsNullOrEmpty(document.Page.Watermark);

        // A docProps/custom.xml part is emitted when the watermark, Word's "Mark as Final" flag, or
        // source-package custom properties need to round-trip; all ride in the same custom-properties part.
        var hasPreservedCustomProps = document.Preserved.OriginalCustomProperties is not null;
        var hasCustomProps = hasWatermark || document.MarkedAsFinal || hasPreservedCustomProps;

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

        // The w:evenAndOddHeaders toggle is document-global: it must be set whenever ANY section (final or
        // non-final) has DifferentOddEvenPages on, because even-page header/footer parts emitted by a
        // non-final section are silently ignored by Word when the global toggle is absent.
        var anyDifferentOddEvenPages = document.Page.DifferentOddEvenPages
            || document.Blocks.OfType<Paragraph>().Any(p => p.SectionBreak?.Page.DifferentOddEvenPages == true);

        var hasSettings = hasProtection
            || document.Page.AutoHyphenation
            || anyDifferentOddEvenPages
            || document.Page.MirrorMargins
            || HasCustomDefaultTabStop(document.Page)
            || hasBackground
            || hasEmbeddedFonts
            || hasPreservedSettings
            || !document.FootnoteNumbering.IsDefault
            || !document.EndnoteNumbering.IsDefault;

        // The bibliography part (word/bibliography/sources.xml) persists the document's citation Sources and
        // selected BibliographyStyle. Emitted whenever there are sources or a non-default style to record, so a
        // document that has never touched citations round-trips exactly as before (no bibliography part).
        var hasBibliography = document.Sources.Count > 0 || document.BibliographyStyle != CitationStyle.Apa;
        if (hasBibliography)
        {
            var itemName = NextCustomXmlItemName(preservedParts);
            preservedParts = preservedParts
                .Concat(BuildCurrentBibliographyParts(document, itemName))
                .ToList();
        }

        // A word/theme/theme1.xml part is always emitted (real Word documents always carry one); it
        // serialises the document's DocumentTheme as a real clrScheme/fontScheme/fmtScheme.
        // [Content_Types].xml needs a Default for every image extension actually used by ANY part — body
        // images or header/footer images — so non-PNG pictures (jpeg/gif/bmp/tiff/emf/wmf) are declared too.
        // Ordered (png first) for deterministic output; only extensions present are emitted.
        var imageExtensions = images
            .Concat(headerFooterParts.SelectMany(p => p.Images))
            .Concat(commentImages)
            .Concat(footnoteImages)
            .Concat(endnoteImages)
            .Where(p => p.HasEmbeddedPayload)
            .Select(p => InlineImage.ExtensionFor(p.Image.Format))
            .Distinct()
            .OrderBy(ext => ext, StringComparer.Ordinal)
            .ToList();

        // Macro parts (vbaProject.bin + vbaData.xml + the part-local rels) are preserved verbatim on read but
        // only re-emitted for macro-enabled targets (.docm/.dotm); a .docx/.dotx must not carry them. Filtered
        // once here and used for the content types, document rels, the inline-drawing rel ids and the byte parts
        // so the four stay in lock-step.
        var hasExtendedProps = preservedParts.Any(p => p.PartName == OpcPackageProperties.ExtendedPropertiesPartName);

        using var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true);
        WritePart(archive, "[Content_Types].xml", BuildContentTypes(imageExtensions, emitNumbering, headerFooterParts, hasFootnotes, hasEndnotes, hasComments, hasCustomProps, hasSettings, hasBibliography, charts, embeddedObjects.Any(part => !part.EmbeddedObject.IsLinked), smartArts, hasEmbeddedFonts, preservedParts, document.Preserved.ContentTypeDefaults, options.MainDocumentContentType));
        WritePart(archive, "_rels/.rels", BuildPackageRels(hasCustomProps, hasExtendedProps, preservedParts));
        WritePart(
            archive,
            OpcPackageProperties.CorePropertiesZipEntry,
            BuildCoreProperties(document));
        if (hasCustomProps)
            WritePart(archive, OpcPackageProperties.CustomPropertiesZipEntry, BuildCustomProperties(document.Preserved.OriginalCustomProperties, document.Page.WatermarkOptions, document.Page.Watermark, document.MarkedAsFinal));
        WritePart(archive, "word/_rels/document.xml.rels", BuildDocumentRels(images, hyperlinks, subDocuments, emitNumbering, headerFooterParts, hasFootnotes, hasEndnotes, hasComments, hasSettings, hasBibliography, charts, embeddedObjects, smartArts, hasEmbeddedFonts, preservedParts));
        WritePart(archive, "word/document.xml", BuildDocument(document, images, charts, embeddedObjects, smartArts, hyperlinks, subDocuments, headerFooterParts, preservedNumbering, restartOverrides, preservedParts));
        WritePart(archive, "word/styles.xml", BuildStyles(document, preservedNumbering));
        WritePart(archive, ThemePartName.TrimStart('/'), BuildTheme(document.Theme));
        if (hasSettings)
            WritePart(archive, SettingsPartName.TrimStart('/'), BuildSettings(document.Protection, document.Page, hasBackground, hasEmbeddedFonts, document.FootnoteNumbering, document.EndnoteNumbering, document.Preserved.OriginalSettings, anyDifferentOddEvenPages));
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
            WritePart(archive, "word/numbering.xml", BuildNumbering(
                includeFreeWNumbering,
                preservedNumbering,
                document.MultiLevelList.NumberFormats,
                restartOverrides,
                chapterPageNumberingLevels.Count > 0));
        // One part per (section × header/footer × type) slot with content. Each part XML carries its inline
        // images via PART-LOCAL r:embed ids resolved against its own word/_rels/<part>.xml.rels, and its
        // image media bytes go under word/media/.
        foreach (var part in headerFooterParts)
        {
            WritePart(archive, "word/" + part.FileName,
                BuildHeaderFooter(part.IsHeader ? W + "hdr" : W + "ftr", part, preservedNumbering, restartOverrides));
            if (part.Images.Count > 0 || part.PreservedDrawings.Count > 0 || part.Hyperlinks.Count > 0)
            {
                WritePart(archive, "word/_rels/" + part.FileName + ".rels", BuildHeaderFooterRels(part));
                foreach (var image in part.Images)
                    if (image.HasEmbeddedPayload)
                        WriteBinaryPart(archive, "word/media/" + image.FileName, image.Image.Bytes);
            }
        }
        if (hasFootnotes)
        {
            WritePart(archive, FootnotesPartName.TrimStart('/'), BuildFootnotes(document, footnoteImages, footnotePreservedDrawings, footnoteHyperlinks, preservedNumbering, restartOverrides));
            if (footnoteImages.Count > 0 || footnotePreservedDrawings.Count > 0 || footnoteHyperlinks.Count > 0)
            {
                WritePart(archive, "word/_rels/footnotes.xml.rels", BuildNoteRels(footnoteImages, footnotePreservedDrawings, footnoteHyperlinks));
                foreach (var image in footnoteImages)
                    if (image.HasEmbeddedPayload)
                        WriteBinaryPart(archive, "word/media/" + image.FileName, image.Image.Bytes);
            }
        }
        if (hasEndnotes)
        {
            WritePart(archive, EndnotesPartName.TrimStart('/'), BuildEndnotes(document, endnoteImages, endnotePreservedDrawings, endnoteHyperlinks, preservedNumbering, restartOverrides));
            if (endnoteImages.Count > 0 || endnotePreservedDrawings.Count > 0 || endnoteHyperlinks.Count > 0)
            {
                WritePart(archive, "word/_rels/endnotes.xml.rels", BuildNoteRels(endnoteImages, endnotePreservedDrawings, endnoteHyperlinks));
                foreach (var image in endnoteImages)
                    if (image.HasEmbeddedPayload)
                        WriteBinaryPart(archive, "word/media/" + image.FileName, image.Image.Bytes);
            }
        }
        if (hasComments)
        {
            WritePart(archive, CommentsPartName.TrimStart('/'), BuildComments(document, commentImages, commentPreservedDrawings, commentHyperlinks, preservedNumbering, restartOverrides));
            // word/commentsExtended.xml threads replies + carries resolved state. Always emitted alongside the
            // comments part (it has an entry per comment even for a flat, single-comment document) so modern
            // Word treats every comment as a thread root and the reply/resolve plumbing round-trips.
            WritePart(archive, CommentsExtendedPartName.TrimStart('/'), BuildCommentsExtended(document));
            // Comment media and preserved charts resolve through comments.xml.rels.
            if (commentImages.Count > 0 || commentPreservedDrawings.Count > 0 || commentHyperlinks.Count > 0)
            {
                WritePart(archive, "word/_rels/comments.xml.rels", BuildCommentsRels(commentImages, commentPreservedDrawings, commentHyperlinks));
                foreach (var image in commentImages)
                    if (image.HasEmbeddedPayload)
                        WriteBinaryPart(archive, "word/media/" + image.FileName, image.Image.Bytes);
            }
        }
        foreach (var image in images)
            if (image.HasEmbeddedPayload)
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
            if (embedded.FileName is { } fileName)
                WriteBinaryPart(archive, "word/embeddings/" + fileName, embedded.EmbeddedObject.Payload);
        foreach (var smartArt in smartArts)
        {
            // F2: the data part carries a dgm:dataModelExt pointing at the rendered-geometry drawing part.
            // Word resolves that relationship against document.xml.rels, matching native Word packages.
            WritePart(archive, "word/diagrams/" + smartArt.DataFileName, BuildDiagramData(smartArt.SmartArt, smartArt.DrawingRelationshipId));
            WritePart(archive, "word/diagrams/" + smartArt.LayoutFileName, BuildDiagramLayout(smartArt.SmartArt));
            WritePart(archive, "word/diagrams/" + smartArt.QuickStyleFileName, BuildDiagramQuickStyle(smartArt.SmartArt));
            WritePart(archive, "word/diagrams/" + smartArt.ColorsFileName, BuildDiagramColors(smartArt.SmartArt));
            WritePart(archive, "word/diagrams/" + smartArt.DrawingFileName, BuildDiagramDrawing(smartArt.SmartArt));
        }
        // Unmodelled-but-preserved parts (customXml/*, word/webSettings.xml): re-emitted byte-for-byte. Their
        // content-type Overrides and document relationships are added by BuildContentTypes / BuildDocumentRels;
        // their own _rels (e.g. customXml item rels) are themselves preserved parts, so the whole satellite set
        // round-trips. Authored-from-scratch documents have none, so nothing extra is written.
        foreach (var part in preservedParts)
            WriteBinaryPart(archive, part.PartName.TrimStart('/'), part.Bytes);
    }

    private static XDocument BuildCoreProperties(TextDocument document)
    {
        var result = OpcDocumentProperties.BuildCorePropertiesDocument(
            document.Properties,
            includeDcmiTypeNamespace: true);
        if (document.Preserved.OriginalCoreProperties is not { } sourceRoot
            || result.Root is not { } targetRoot)
        {
            return result;
        }

        var unmodeledNames = sourceRoot.Elements()
            .Select(element => element.Name)
            .Where(name => !OpcDocumentProperties.ModeledCorePropertyElementNames.Contains(name))
            .Distinct()
            .ToArray();
        OpcDocumentProperties.PreservePropertyElements(sourceRoot, targetRoot, unmodeledNames);
        return result;
    }

    /// <summary>An inline image paired with its embedded/link relationship ids, media file name and drawing id.</summary>
    private sealed record ImagePart(InlineImage Image, string RelationshipId, string FileName, uint DrawingId)
    {
        public bool HasEmbeddedPayload => Image.Bytes.Length > 0 || string.IsNullOrWhiteSpace(Image.LinkedImageTarget);

        public string? LinkRelationshipId => string.IsNullOrWhiteSpace(Image.LinkedImageTarget)
            ? null
            : RelationshipId.StartsWith("rId", StringComparison.Ordinal)
                ? "rIdLinked" + RelationshipId[3..]
                : RelationshipId + "Linked";
    }

    /// <summary>
    /// An inline chart paired with the document relationship id, chart part file name (relative to
    /// <c>word/charts/</c>) and a unique drawing id, mirroring <see cref="ImagePart"/>. <see cref="EmbeddingFileName"/>
    /// is the companion editable-data workbook (relative to <c>word/embeddings/</c>); the chart part's own
    /// <c>_rels</c> references it under <see cref="ExternalDataRelId"/> (a part-local rId) so Word's "Edit Data"
    /// can reopen it (F1).
    /// </summary>
    private sealed record ChartPart(Chart Chart, string RelationshipId, string FileName, uint DrawingId, string EmbeddingFileName, string ExternalDataRelId);

    private static List<ChartPart> CollectCharts(
        TextDocument document,
        HashSet<string> usedPartNames,
        int drawingIdSeed = 0)
    {
        var charts = new List<ChartPart>();
        foreach (var paragraph in EnumerateParagraphs(document))
            foreach (var run in paragraph.Runs)
            {
                if (run.Chart is { } chart)
                    Add(chart);
                if (run.DrawingGroup is { } group)
                    foreach (var groupedChart in EnumerateDrawingGroupChildren(group).OfType<Chart>())
                        Add(groupedChart);
            }

        void Add(Chart chart)
        {
            var index = charts.Count + 1;
            var chartFileName = NextAvailableChartFileName(usedPartNames);
            var embeddingFileName = NextAvailablePartFileName(
                usedPartNames,
                "word/embeddings",
                "Microsoft_Excel_Worksheet",
                "xlsx");
            // The external-data rId is part-LOCAL (it lives in word/charts/_rels/chartN.xml.rels), so a
            // fixed "rId1" per chart is fine and never collides with the document-level ids above.
            charts.Add(new ChartPart(
                chart,
                $"rIdChart{index}",
                chartFileName,
                checked((uint)(drawingIdSeed + index)),
                embeddingFileName,
                "rId1"));
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
        string? FileName,
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
    private static List<EmbeddedObjectPart> CollectEmbeddedObjects(TextDocument document, List<ImagePart> images, HashSet<string> usedPartNames)
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
                        iconPart = new ImagePart(
                            icon,
                            $"rIdImg{imageIndex}",
                            NextAvailablePartFileName(usedPartNames, "word/media", "image", InlineImage.ExtensionFor(icon.Format)),
                            (uint)imageIndex);
                        images.Add(iconPart);
                    }
                    embedded.Add(new EmbeddedObjectPart(
                        obj,
                        $"rIdOle{index}",
                        obj.IsLinked
                            ? null
                            : NextAvailablePartFileName(usedPartNames, "word/embeddings", "oleObject", "bin"),
                        $"_oleObj{index}",
                        iconPart));
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

    private static List<SmartArtPart> CollectSmartArts(TextDocument document, int drawingIdSeed = 0)
    {
        var smartArts = new List<SmartArtPart>();
        foreach (var paragraph in EnumerateParagraphs(document))
            foreach (var run in paragraph.Runs)
            {
                if (run.PreservedDrawing is null && run.SmartArt is { } smartArt)
                    Add(smartArt);
                if (run.DrawingGroup is { } group)
                    foreach (var groupedSmartArt in EnumerateDrawingGroupChildren(group).OfType<SmartArt>())
                        Add(groupedSmartArt);
            }

        void Add(SmartArt smartArt)
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
                // Word resolves dgm:dataModelExt/@relId against the document-level relationship
                // set, matching the native package.
                $"rIdDgmDrawing{index}",
                $"drawing{index}.xml",
                checked((uint)(drawingIdSeed + index))));
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

    private static List<ImagePart> CollectImages(TextDocument document, HashSet<string> usedPartNames)
    {
        var images = new List<ImagePart>();
        foreach (var paragraph in EnumerateParagraphs(document))
            foreach (var run in paragraph.Runs)
            {
                if (run.Image is { } image)
                    Add(image);
                if (run.DrawingGroup is { } group)
                    foreach (var groupedImage in EnumerateDrawingGroupChildren(group).OfType<InlineImage>())
                        Add(groupedImage);
            }

        void Add(InlineImage image)
        {
            var index = images.Count + 1;
            images.Add(new ImagePart(
                image,
                $"rIdImg{index}",
                NextAvailablePartFileName(usedPartNames, "word/media", "image", InlineImage.ExtensionFor(image.Format)),
                (uint)index));
        }
        return images;
    }

    private static IEnumerable<object> EnumerateDrawingGroupChildren(DrawingGroup group)
    {
        foreach (var child in group.Children)
        {
            yield return child;
            if (child is DrawingGroup nestedGroup)
                foreach (var nestedChild in EnumerateDrawingGroupChildren(nestedGroup))
                    yield return nestedChild;
        }
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
        IReadOnlyList<ImagePart> Images,
        IReadOnlyList<PartLocalPreservedDrawingPart> PreservedDrawings,
        IReadOnlyDictionary<string, string> Hyperlinks,
        WatermarkOptions? Watermark,
        ImagePart? WatermarkImage);

    /// <summary>A story-part-local relationship for a preserved chart drawing.</summary>
    private sealed record PartLocalPreservedDrawingPart(
        string PartName,
        string RelationshipId,
        string RelationshipType);

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
    private static List<HeaderFooterPart> CollectHeaderFooterParts(TextDocument document, HashSet<string> usedPartNames)
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
                var watermark = isHeader ? ResolveWordVisibleWatermark(page) : null;
                var hasWatermark = watermark is not null;
                if ((content is null || content.IsEmpty) && !hasWatermark)
                    return;
                content ??= new HeaderFooter();
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
                var images = CollectHeaderFooterImages(content, fileName, usedPartNames);
                var preservedDrawings = CollectPartLocalPreservedDrawings(content.Paragraphs, document.Preserved.Parts);
                var hyperlinks = CollectHyperlinks(content.Paragraphs);
                ImagePart? watermarkImage = null;
                if (watermark?.IsPicture == true)
                {
                    var image = new InlineImage(
                        watermark.ImageBytes!,
                        widthPt: 468,
                        heightPt: 117,
                        InlineImage.DetectFormat(watermark.ImageBytes!));
                    watermarkImage = new ImagePart(
                        image,
                        "rIdWatermarkImage",
                        NextAvailablePartFileName(
                            usedPartNames,
                            "word/media",
                            $"{fileName[..^4]}_watermark",
                            InlineImage.ExtensionFor(image.Format)),
                        (uint)(images.Count + 1));
                    images.Add(watermarkImage);
                }
                parts.Add(new HeaderFooterPart(
                    section,
                    type,
                    isHeader,
                    content,
                    fileName,
                    relationshipId,
                    images,
                    preservedDrawings,
                    hyperlinks,
                    watermark,
                    watermarkImage));
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

    // A custom FreeW text-watermark property is editable metadata, not a Word-visible watermark by
    // itself. Keep the distinction on save: an imported package with no VML shape must not gain one.
    // Explicit disabled VML remains materialized because its retained source payload is authoritative.
    private static WatermarkOptions? ResolveWordVisibleWatermark(PageSettings page)
    {
        var watermark = page.EffectiveWatermark;
        if (watermark is not { IsPicture: false, NativeVmlTextPathEnabled: false })
            return watermark;

        return !string.IsNullOrWhiteSpace(watermark.NativeVmlTextPathXml)
            || !string.IsNullOrWhiteSpace(watermark.NativeVmlTextShapeTypeXml)
            ? watermark
            : null;
    }

    /// <summary>
    /// Collects the inline images carried by a header/footer part's runs and assigns each a PART-LOCAL
    /// relationship id (<c>rIdImgN</c>) plus a media file name unique to this part (derived from the part's
    /// file name, e.g. <c>header3_image1.png</c>) so it never clashes with body media or another part's media.
    /// The walk order matches <see cref="BuildHeaderFooterImagesByRun"/> so the part XML and its rels agree on
    /// which rId belongs to which run.
    /// </summary>
    private static List<ImagePart> CollectHeaderFooterImages(HeaderFooter content, string partFileName, HashSet<string> usedPartNames)
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
                    images.Add(new ImagePart(
                        image,
                        $"rIdImg{index}",
                        NextAvailablePartFileName(usedPartNames, "word/media", $"{stem}_image", InlineImage.ExtensionFor(image.Format)),
                        (uint)index));
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

    /// <summary>
    /// Assigns a story-part-local relationship to each preserved drawing part referenced by its runs. Preserved
    /// body drawings keep their document-level relationships; header/footer/note drawings resolve against the
    /// owning part's _rels file. Original relationship ids are retained: a preserved SmartArt data model refers
    /// to its cached diagram drawing by that story-part id.
    /// </summary>
    private static IReadOnlyList<PartLocalPreservedDrawingPart> CollectPartLocalPreservedDrawings(
        IEnumerable<Paragraph> paragraphs,
        IReadOnlyList<PreservedPart> preservedParts)
    {
        var preservedByName = preservedParts.ToDictionary(part => part.PartName, StringComparer.Ordinal);
        var result = new List<PartLocalPreservedDrawingPart>();
        foreach (var paragraph in paragraphs)
            foreach (var run in paragraph.Runs)
                if (run.PreservedDrawing is { } drawing)
                    foreach (var reference in drawing.References)
                    {
                        if (result.Any(part => part.PartName == reference.PreservedPartName)
                            || !preservedByName.TryGetValue(reference.PreservedPartName, out var preservedPart))
                            continue;
                        var relationshipType = reference.RelationshipType
                            ?? PreservedDrawingRelationshipType(preservedPart.ContentTypeOverride);
                        result.Add(new PartLocalPreservedDrawingPart(
                            reference.PreservedPartName,
                            reference.OriginalRelId,
                            relationshipType));
                    }
        return result;
    }

    private static string PreservedDrawingRelationshipType(string? contentType) => contentType switch
    {
        ChartExContentType => ChartExRelType,
        DiagramDataContentType => DiagramDataRelType,
        DiagramLayoutContentType => DiagramLayoutRelType,
        DiagramStyleContentType => DiagramStyleRelType,
        DiagramColorsContentType => DiagramColorsRelType,
        DiagramDrawingContentType => DiagramDrawingRelType,
        _ => ChartRelType
    };

    /// <summary>Maps each distinct hyperlink URL to one external relationship id (rIdLinkN).</summary>
    private static Dictionary<string, string> CollectHyperlinks(TextDocument document) =>
        CollectHyperlinks(EnumerateParagraphs(document));

    private static Dictionary<string, string> CollectHyperlinks(IEnumerable<Paragraph> paragraphs)
    {
        var byUrl = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var paragraph in EnumerateHyperlinkParagraphs(paragraphs))
            foreach (var run in paragraph.Runs)
                if (run.HyperlinkUrl is { Length: > 0 } url && !byUrl.ContainsKey(url))
                    byUrl[url] = $"rIdLink{byUrl.Count + 1}";
        return byUrl;
    }

    /// <summary>Maps each distinct external subdocument target to one deterministic relationship id.</summary>
    private static Dictionary<string, string> CollectSubDocuments(TextDocument document)
    {
        var byTarget = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var paragraph in EnumerateParagraphs(document))
        {
            foreach (var run in paragraph.Runs)
            {
                if (run.SubDocument?.Target is { Length: > 0 } target && !byTarget.ContainsKey(target))
                    byTarget[target] = $"rIdSubDocument{byTarget.Count + 1}";
            }
        }
        return byTarget;
    }

    private static IEnumerable<Paragraph> EnumerateHyperlinkParagraphs(IEnumerable<Paragraph> paragraphs)
    {
        foreach (var paragraph in paragraphs)
        {
            yield return paragraph;
            foreach (var run in paragraph.Runs)
            {
                if (run.Shape is { } shape)
                    foreach (var nested in EnumerateHyperlinkParagraphs(shape.TextParagraphs))
                        yield return nested;

                if (run.DrawingGroup is { } group)
                    foreach (var shapeChild in group.Children.OfType<Shape>())
                        foreach (var nested in EnumerateHyperlinkParagraphs(shapeChild.TextParagraphs))
                            yield return nested;
            }
        }
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

    private static IEnumerable<Paragraph> EnumerateStoryParagraphs(TextDocument document)
    {
        foreach (var paragraph in EnumerateHyperlinkParagraphs(EnumerateParagraphs(document)))
            yield return paragraph;

        foreach (var headersFooters in document.Sections.Select(section => section.HeadersFooters))
            foreach (var content in new[]
            {
                headersFooters.Header, headersFooters.Footer,
                headersFooters.EvenHeader, headersFooters.EvenFooter,
                headersFooters.FirstHeader, headersFooters.FirstFooter
            })
                if (content is not null)
                    foreach (var paragraph in EnumerateHyperlinkParagraphs(content.Paragraphs))
                        yield return paragraph;

        foreach (var paragraph in EnumerateHyperlinkParagraphs(document.Footnotes.Values.SelectMany(note => note.Content)))
            yield return paragraph;
        foreach (var paragraph in EnumerateHyperlinkParagraphs(document.Endnotes.Values.SelectMany(note => note.Content)))
            yield return paragraph;
        foreach (var paragraph in EnumerateHyperlinkParagraphs(FlattenComments(document).SelectMany(comment => comment.Content)))
            yield return paragraph;
    }

    private static void WritePart(ZipArchive archive, string entryPath, XDocument content)
        => OpcXml.WriteXmlEntry(archive, entryPath, content);

    private static void WriteBinaryPart(ZipArchive archive, string entryPath, byte[] content)
    {
        var entry = archive.CreateEntry(entryPath, CompressionLevel.Optimal);
        using var entryStream = entry.Open();
        entryStream.Write(content, 0, content.Length);
    }

    private static HashSet<string> CreateUsedPartNameSet(IEnumerable<PreservedPart> preservedParts) =>
        preservedParts
            .Select(part => NormalizePartName(part.PartName))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static string NextAvailablePartFileName(
        HashSet<string> usedPartNames,
        string folder,
        string prefix,
        string extension)
    {
        // Word numbers media parts sequentially as <prefix>N regardless of extension (image1.png, image2.jpeg,
        // image3.gif — not image1.png/image1.jpeg/image1.gif), so a number is reserved across ALL extensions.
        // The extension-less stem sentinel claims a number; the full path keeps avoiding preserved-part clashes.
        for (var index = 1;; index++)
        {
            var stem = NormalizePartName($"{folder}/{prefix}{index}");
            var fileName = $"{prefix}{index}.{extension}";
            if (!usedPartNames.Contains(stem)
                && usedPartNames.Add(NormalizePartName($"{folder}/{fileName}")))
            {
                usedPartNames.Add(stem);
                return fileName;
            }
        }
    }

    private static string NextAvailableChartFileName(HashSet<string> usedPartNames)
    {
        for (var index = 1;; index++)
        {
            var fileName = $"chart{index}.xml";
            var chartPartName = NormalizePartName("word/charts/" + fileName);
            var chartRelsPartName = NormalizePartName("word/charts/_rels/" + fileName + ".rels");
            if (usedPartNames.Contains(chartPartName) || usedPartNames.Contains(chartRelsPartName))
                continue;

            usedPartNames.Add(chartPartName);
            usedPartNames.Add(chartRelsPartName);
            return fileName;
        }
    }

    private static string NormalizePartName(string partName) =>
        partName.TrimStart('/').Replace('\\', '/');

    private static XDocument BuildContentTypes(IReadOnlyList<string> imageExtensions, bool includeNumbering, IReadOnlyList<HeaderFooterPart> headerFooterParts, bool hasFootnotes, bool hasEndnotes, bool hasComments, bool hasCustomProps, bool hasSettings, bool hasBibliography, IReadOnlyList<ChartPart> charts, bool hasEmbeddedObjects, IReadOnlyList<SmartArtPart> smartArts, bool hasEmbeddedFonts, IReadOnlyList<PreservedPart> preservedParts, IReadOnlyDictionary<string, string> preservedContentTypeDefaults, string mainDocumentContentType) => new(
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
                new XAttribute("ContentType", mainDocumentContentType)),
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
            new XElement(Ct + "Override", new XAttribute("PartName", OpcPackageProperties.CorePropertiesPartName),
                new XAttribute("ContentType", OpcPackageProperties.CorePropertiesContentType)),
            hasCustomProps
                ? new XElement(Ct + "Override", new XAttribute("PartName", OpcPackageProperties.CustomPropertiesPartName),
                    new XAttribute("ContentType", OpcPackageProperties.CustomPropertiesContentType))
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

    private static XDocument BuildPackageRels(bool hasCustomProps, bool hasExtendedProps, IReadOnlyList<PreservedPart> preservedParts) => new(
        OpcRelationships.CreateRoot(
            OpcRelationships.CreateRelationship("rId1", OfficeDocumentRel, "word/document.xml"),
            OpcRelationships.CreateRelationship(
                "rIdCore",
                OpcPackageProperties.CorePropertiesRelationshipType,
                OpcPackageProperties.CorePropertiesZipEntry),
            hasCustomProps
                ? OpcRelationships.CreateRelationship(
                    "rIdCustom",
                    OpcPackageProperties.CustomPropertiesRelationshipType,
                    OpcPackageProperties.CustomPropertiesZipEntry)
                : null,
            hasExtendedProps
                ? OpcRelationships.CreateRelationship(
                    "rIdExtended",
                    OpcPackageProperties.ExtendedPropertiesRelationshipType,
                    OpcPackageProperties.ExtendedPropertiesZipEntry)
                : null,
            preservedParts
                .Where(part => part.PackageRelationshipType is not null)
                .Select((part, index) => OpcRelationships.CreateRelationship(
                    $"rIdPreservedPackage{index + 1}",
                    part.PackageRelationshipType!,
                    part.PartName.TrimStart('/')))));

    /// <summary>
    /// Builds docProps/custom.xml carrying the FreeW watermark properties and/or Word's "Mark as Final"
    /// flag over any source-package custom properties.
    /// </summary>
    private static XDocument BuildCustomProperties(XElement? originalProperties, WatermarkOptions? watermarkOptions, string? legacyWatermark, bool markedAsFinal)
    {
        string[] freeWPropertyNames =
        [
            WatermarkPropertyName,
            WatermarkFontFamilyPropertyName,
            WatermarkColorPropertyName,
            WatermarkLayoutPropertyName,
            WatermarkOpacityPropertyName,
            WatermarkImagePropertyName,
            WatermarkScalePropertyName,
            MarkAsFinalPropertyName
        ];

        var properties = OpcCustomDocumentProperties.FromRoot(originalProperties);
        var activePropertyNames = new HashSet<string>(StringComparer.Ordinal);

        // Write full WatermarkOptions if present; otherwise fall back to legacy plain text.
        if (watermarkOptions is not null)
        {
            activePropertyNames.Add(WatermarkPropertyName);
            activePropertyNames.Add(WatermarkFontFamilyPropertyName);
            activePropertyNames.Add(WatermarkColorPropertyName);
            activePropertyNames.Add(WatermarkLayoutPropertyName);
            activePropertyNames.Add(WatermarkOpacityPropertyName);

            if (watermarkOptions.IsPicture)
            {
                activePropertyNames.Add(WatermarkImagePropertyName);
                activePropertyNames.Add(WatermarkScalePropertyName);
            }
        }
        else if (!string.IsNullOrEmpty(legacyWatermark))
        {
            activePropertyNames.Add(WatermarkPropertyName);
        }

        if (markedAsFinal)
            activePropertyNames.Add(MarkAsFinalPropertyName);

        properties.RemoveRange(freeWPropertyNames.Where(name => !activePropertyNames.Contains(name)));

        if (watermarkOptions is not null)
        {
            // Always emit the text (the primary property that the legacy reader also picks up).
            properties.SetString(WatermarkPropertyName, watermarkOptions.Text);
            properties.SetString(WatermarkFontFamilyPropertyName, watermarkOptions.FontFamily);
            properties.SetString(WatermarkColorPropertyName, watermarkOptions.FontColorHex);
            properties.SetString(WatermarkLayoutPropertyName, watermarkOptions.Layout.ToString());
            properties.SetDouble(WatermarkOpacityPropertyName, watermarkOptions.Opacity);
            // Picture watermark: persist image bytes as base-64 and scale.
            if (watermarkOptions.IsPicture)
            {
                properties.SetString(WatermarkImagePropertyName, Convert.ToBase64String(watermarkOptions.ImageBytes!));
                properties.SetString(
                    WatermarkScalePropertyName,
                    watermarkOptions.ScalePct.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
        }
        else if (!string.IsNullOrEmpty(legacyWatermark))
        {
            properties.SetString(WatermarkPropertyName, legacyWatermark);
        }

        if (markedAsFinal)
            properties.SetBoolean(MarkAsFinalPropertyName, true);
        return properties.ToXDocument();
    }

    private static XDocument BuildDocumentRels(
        IReadOnlyList<ImagePart> images,
        IReadOnlyDictionary<string, string> hyperlinks,
        IReadOnlyDictionary<string, string> subDocuments,
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
        static XElement Relationship(string id, string type, string target, bool external = false) =>
            OpcRelationships.CreateRelationship(id, type, target, external);

        var relationships = OpcRelationships.CreateRoot(
            Relationship("rId1", StylesRel, "styles.xml"));
        if (hasSettings)
            relationships.Add(Relationship(SettingsRelationshipId, SettingsRelType, "settings.xml"));
        // The document→bibliography relationship (word/bibliography/sources.xml, target relative to word/).
        if (hasBibliography)
            relationships.Add(Relationship(BibliographyRelationshipId, BibliographyRelType, "bibliography/sources.xml"));
        // The document→fontTable relationship (the fontTable's own rels reference the .odttf font parts).
        if (hasEmbeddedFonts)
            relationships.Add(Relationship(FontTableRelationshipId, FontTableRelType, "fontTable.xml"));
        // The theme part is always present, so its relationship is unconditional.
        relationships.Add(Relationship(ThemeRelationshipId, ThemeRelType, "theme/theme1.xml"));
        if (includeNumbering)
            relationships.Add(Relationship("rIdNumbering", NumberingRelType, "numbering.xml"));
        // One document relationship per emitted header/footer part (in collection order — which reproduces
        // the legacy header1, footer1, header2, footer2 ordering and rel ids for single-section documents).
        // The owning section's w:sectPr references the part by this relationship id.
        foreach (var part in headerFooterParts)
            relationships.Add(Relationship(part.RelationshipId, part.IsHeader ? HeaderRel : FooterRel, part.FileName));
        if (hasFootnotes)
            relationships.Add(Relationship(FootnotesRelationshipId, FootnotesRelType, "footnotes.xml"));
        if (hasEndnotes)
            relationships.Add(Relationship(EndnotesRelationshipId, EndnotesRelType, "endnotes.xml"));
        if (hasComments)
        {
            relationships.Add(Relationship(CommentsRelationshipId, CommentsRelType, "comments.xml"));
            relationships.Add(Relationship(CommentsExtendedRelationshipId, CommentsExtendedRelType, "commentsExtended.xml"));
        }
        foreach (var image in images)
        {
            if (image.HasEmbeddedPayload)
                relationships.Add(Relationship(image.RelationshipId, ImageRel, "media/" + image.FileName));
            if (image.LinkRelationshipId is { } linkRelationshipId)
                relationships.Add(Relationship(linkRelationshipId, ImageRel, image.Image.LinkedImageTarget!, external: true));
        }
        foreach (var chart in charts)
            relationships.Add(Relationship(chart.RelationshipId, ChartRelType, "charts/" + chart.FileName));
        // The embedded OLE payload relationship (the icon's image relationship is emitted in the images loop).
        foreach (var embedded in embeddedObjects)
            relationships.Add(embedded.EmbeddedObject.LinkedTarget is { } linkedTarget
                ? Relationship(embedded.RelationshipId, OleObjectRelType, linkedTarget, external: true)
                : Relationship(embedded.RelationshipId, OleObjectRelType, "embeddings/" + embedded.FileName));
        // Each SmartArt diagram contributes four relationships (data / layout / quickStyle / colors), all
        // referenced together by the inline drawing's dgm:relIds.
        foreach (var s in smartArts)
        {
            relationships.Add(Relationship(s.DataRelationshipId, DiagramDataRelType, "diagrams/" + s.DataFileName));
            relationships.Add(Relationship(s.LayoutRelationshipId, DiagramLayoutRelType, "diagrams/" + s.LayoutFileName));
            relationships.Add(Relationship(s.QuickStyleRelationshipId, DiagramStyleRelType, "diagrams/" + s.QuickStyleFileName));
            relationships.Add(Relationship(s.ColorsRelationshipId, DiagramColorsRelType, "diagrams/" + s.ColorsFileName));
            relationships.Add(Relationship(s.DrawingRelationshipId, DiagramDrawingRelType, "diagrams/" + s.DrawingFileName));
        }
        foreach (var (url, relationshipId) in hyperlinks)
            relationships.Add(Relationship(relationshipId, HyperlinkRel, url, external: true));
        foreach (var (target, relationshipId) in subDocuments)
            relationships.Add(Relationship(relationshipId, SubDocumentRelType, target, external: true));
        // One document relationship per preserved part that the document references directly (customXml items,
        // webSettings and unmodelled chart/chartex parts carry a RelationshipType; their props/_rels/media do
        // not, being referenced from the part's own _rels instead). The Target is reconstructed relative to
        // word/ (where document.xml lives), so a /word/* part targets its bare file name and a /customXml/* part
        // targets "../customXml/…". The id assigned here must match PreservedPartRelIds (consumed by the inline
        // drawing rewrite) — both replay the same order, so a shared helper keeps them in lock-step.
        foreach (var (part, relId) in PreservedPartRelIds(preservedParts))
            relationships.Add(Relationship(relId, part.RelationshipType!, DocumentRelativeTarget(part.PartName)));
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
        IReadOnlyDictionary<string, string> subDocuments,
        IReadOnlyList<HeaderFooterPart> headerFooterParts,
        PreservedNumberingPlan? preservedNumbering,
        IReadOnlyDictionary<(ListKind Kind, int Level, int StartAt), int> restartOverrides,
        IReadOnlyList<PreservedPart> preservedParts)
    {
        // Group header/footer parts by their owning section: the final/body-level section (Section == null)
        // feeds the body-level w:sectPr; each non-final section feeds its paragraph-level w:sectPr.
        var finalSectionParts = headerFooterParts.Where(p => p.Section is null).ToList();
        var partsBySection = headerFooterParts
            .Where(p => p.Section is not null)
            .GroupBy(p => p.Section!)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<HeaderFooterPart>)g.ToList());

        // Per-write id state, local to this BuildDocument invocation so concurrent writes never race.
        // Bookmark + revision ids start at 1; the shape docPr counter is seeded just above every
        // pre-assigned image, chart, and SmartArt drawing id so document-level drawing identities never
        // overlap. Each shape BuildRun emits takes the next id.
        var ids = new IdAllocator(
            shapeDrawingSeed: images.Count + charts.Count + smartArts.Count,
            reservedRevisionIds: EnumerateStoryParagraphs(document)
                .SelectMany(paragraph => paragraph.Runs)
                .Where(run => run.MoveRevisionId is not null)
                .Select(run => run.MoveRevisionId!.Value));

        // Map each image/chart run to its assigned part by replaying the same walk order CollectImages /
        // CollectCharts used, so document.xml and the rels agree on which rId belongs to which run.
        var imagesByRun = new Dictionary<Run, ImagePart>();
        var chartsByRun = new Dictionary<Run, ChartPart>();
        var embeddedByRun = new Dictionary<Run, EmbeddedObjectPart>();
        var smartArtsByRun = new Dictionary<Run, SmartArtPart>();
        var imagesByGroupChild = new Dictionary<InlineImage, ImagePart>();
        var chartsByGroupChild = new Dictionary<Chart, ChartPart>();
        var smartArtsByGroupChild = new Dictionary<SmartArt, SmartArtPart>();
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
                if (run.PreservedDrawing is null && run.SmartArt is not null)
                    smartArtsByRun[run] = smartArts[nextSmartArt++];
                if (run.DrawingGroup is { } group)
                    foreach (var child in EnumerateDrawingGroupChildren(group))
                    {
                        if (child is InlineImage image)
                            imagesByGroupChild[image] = images[nextImage++];
                        else if (child is Chart chart)
                            chartsByGroupChild[chart] = charts[nextChart++];
                        else if (child is SmartArt smartArt)
                            smartArtsByGroupChild[smartArt] = smartArts[nextSmartArt++];
                    }
            }

        // Map each document-referenced preserved part to its assigned rIdPreserved{N} (same order/ids as
        // BuildDocumentRels), so a verbatim-preserved inline drawing can re-point its chart r:id at the
        // re-emitted relationship. Empty for documents with no preserved drawings.
        var preservedDrawingRelIds = PreservedPartRelIds(preservedParts)
            .ToDictionary(pair => pair.Part.PartName, pair => pair.RelId, StringComparer.Ordinal);

        var drawings = new RunDrawings(
            imagesByRun, chartsByRun, embeddedByRun, smartArtsByRun, ids, preservedDrawingRelIds,
            subDocuments, imagesByGroupChild, chartsByGroupChild, smartArtsByGroupChild);

        var body = new XElement(W + "body");
        static IReadOnlyList<BlockContentControl> ContentControlChain(Block block)
        {
            var chain = new List<BlockContentControl>();
            for (var control = block.BlockContentControl; control is not null; control = control.Parent)
                chain.Add(control);
            chain.Reverse();
            return chain;
        }

        void AddContentControlGroups(XElement parent, int start, int end, int depth = 0)
        {
            for (var index = start; index < end;)
            {
                var chain = ContentControlChain(document.Blocks[index]);
                if (depth >= chain.Count)
                {
                    parent.Add(BuildBlock(document.Blocks[index], drawings, hyperlinks, partsBySection, preservedNumbering, restartOverrides));
                    index++;
                    continue;
                }

                var control = chain[depth];
                var groupEnd = index + 1;
                while (groupEnd < end)
                {
                    var candidateChain = ContentControlChain(document.Blocks[groupEnd]);
                    if (depth >= candidateChain.Count || !ReferenceEquals(candidateChain[depth], control))
                        break;
                    groupEnd++;
                }

                var content = new XElement(W + "sdtContent");
                AddContentControlGroups(content, index, groupEnd, depth + 1);

                parent.Add(new XElement(W + "sdt", BuildBlockSdtProperties(control), content));
                index = groupEnd;
            }
        }

        for (var i = 0; i < document.Blocks.Count;)
        {
            var customXml = document.Blocks[i].BlockCustomXml;
            var end = i + 1;
            while (end < document.Blocks.Count && ReferenceEquals(document.Blocks[end].BlockCustomXml, customXml))
                end++;

            if (customXml is null)
            {
                AddContentControlGroups(body, i, end);
            }
            else
            {
                var wrapper = new XElement(W + "customXml",
                    string.IsNullOrEmpty(customXml.Element) ? null : new XAttribute(W + "element", customXml.Element),
                    string.IsNullOrEmpty(customXml.Uri) ? null : new XAttribute(W + "uri", customXml.Uri),
                    string.IsNullOrEmpty(customXml.PropertiesXml)
                        ? null
                        : XElement.Parse(customXml.PropertiesXml, LoadOptions.PreserveWhitespace));
                AddContentControlGroups(wrapper, i, end);
                body.Add(wrapper);
            }

            i = end;
        }
        body.Add(BuildSectionProperties(document.Page, finalSectionParts));

        // Page background colour (w:background): it is positionally the FIRST child of w:document, before
        // w:body. Emitted only when a background colour is set so existing documents are unaffected.
        var background = document.Page.BackgroundColorHex is { Length: > 0 } bg
            ? new XElement(W + "background", new XAttribute(W + "color", bg.TrimStart('#')))
            : null;
        var webExtensions = BuildPreservedWebExtensions(document.Preserved.WebExtensions, preservedDrawingRelIds);
        var hasModernContentControlMetadata = document.Blocks.Any(block =>
                ContentControlChain(block).Any(control =>
                    control.Kind is BlockContentControlKind.RepeatingSection or BlockContentControlKind.RepeatingSectionItem
                    || control.WordMetadata is { Appearance: not null } or { Color: not null }))
            || EnumerateStoryParagraphs(document).SelectMany(paragraph => paragraph.Runs).Any(run =>
                run.Control?.WordMetadata is { Appearance: not null } or { Color: not null });

        return new XDocument(
            new XElement(W + "document",
                new XAttribute(XNamespace.Xmlns + "w", W.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "r", R.NamespaceName),
                hasModernContentControlMetadata
                    ? new XAttribute(XNamespace.Xmlns + "w15", W15.NamespaceName)
                    : null,
                hasModernContentControlMetadata
                    ? new XAttribute(XNamespace.Xmlns + "mc", Mc.NamespaceName)
                    : null,
                hasModernContentControlMetadata
                    ? new XAttribute(Mc + "Ignorable", "w15")
                    : null,
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
                body,
                webExtensions));
    }

    private static XElement? BuildPreservedWebExtensions(
        PreservedWebExtensions? webExtensions,
        IReadOnlyDictionary<string, string> preservedPartRelIds)
    {
        if (webExtensions is null)
            return null;

        var element = XElement.Parse(webExtensions.Xml, LoadOptions.PreserveWhitespace);
        foreach (var reference in webExtensions.References)
        {
            if (!preservedPartRelIds.TryGetValue(reference.PreservedPartName, out var newRelId))
                continue;
            foreach (var attribute in element.DescendantsAndSelf()
                         .Attributes(R + "id")
                         .Where(attribute => attribute.Value == reference.OriginalRelId))
            {
                attribute.Value = newRelId;
            }
        }

        return element;
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
        private int _contentControlId;
        private readonly Dictionary<string, Stack<int>> _openBookmarkIds = new(StringComparer.Ordinal);

        private readonly HashSet<int> _reservedRevisionIds;

        public IdAllocator(int shapeDrawingSeed = 0, IEnumerable<int>? reservedRevisionIds = null)
        {
            _shapeDrawingId = shapeDrawingSeed;
            _reservedRevisionIds = reservedRevisionIds is null ? [] : [.. reservedRevisionIds];
        }

        public int NextBookmarkId() => ++_bookmarkId;
        public int BeginBookmark(string pairKey)
        {
            var id = NextBookmarkId();
            if (!_openBookmarkIds.TryGetValue(pairKey, out var ids))
            {
                ids = new Stack<int>();
                _openBookmarkIds[pairKey] = ids;
            }
            ids.Push(id);
            return id;
        }

        public bool TryEndBookmark(string pairKey, out int id)
        {
            if (_openBookmarkIds.TryGetValue(pairKey, out var ids) && ids.Count > 0)
            {
                id = ids.Pop();
                if (ids.Count == 0)
                    _openBookmarkIds.Remove(pairKey);
                return true;
            }
            id = 0;
            return false;
        }
        public int NextRevisionId()
        {
            do
            {
                _revisionId++;
            }
            while (_reservedRevisionIds.Contains(_revisionId));
            return _revisionId;
        }
        public int NextShapeDrawingId() => ++_shapeDrawingId;
        public int NextContentControlId() => ++_contentControlId;
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
        IReadOnlyDictionary<string, string>? PreservedDrawingRelIds = null,
        IReadOnlyDictionary<string, string>? SubDocuments = null,
        IReadOnlyDictionary<InlineImage, ImagePart>? GroupImages = null,
        IReadOnlyDictionary<Chart, ChartPart>? GroupCharts = null,
        IReadOnlyDictionary<SmartArt, SmartArtPart>? GroupSmartArts = null)
    {
        public static RunDrawings Empty() => new(
            new Dictionary<Run, ImagePart>(),
            new Dictionary<Run, ChartPart>(),
            new Dictionary<Run, EmbeddedObjectPart>(),
            new Dictionary<Run, SmartArtPart>(),
            new IdAllocator(),
            null,
            null,
            new Dictionary<InlineImage, ImagePart>(),
            new Dictionary<Chart, ChartPart>(),
            new Dictionary<SmartArt, SmartArtPart>());
    }

    /// <summary>
    /// Builds a header (w:hdr) or footer (w:ftr) part from its model paragraphs. When the part carries inline
    /// images, each image run emits a w:drawing whose r:embed references a PART-LOCAL relationship id (resolved
    /// against the part's own word/_rels/&lt;part&gt;.xml.rels, see <see cref="BuildHeaderFooterRels"/>) and the
    /// drawing namespaces (wp/a/pic) are declared on the root. An image-less header/footer declares only w/r
    /// (so it stays byte-equivalent to the historical output).
    /// </summary>
    private static XDocument BuildHeaderFooter(
        XName rootName,
        HeaderFooterPart part,
        PreservedNumberingPlan? preservedNumbering,
        IReadOnlyDictionary<(ListKind Kind, int Level, int StartAt), int> restartOverrides)
    {
        var root = new XElement(rootName,
            new XAttribute(XNamespace.Xmlns + "w", W.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "r", R.NamespaceName));

        // Header/footer runs may carry inline images and verbatim-preserved charts; each resolves through the
        // header/footer's local relationships. Other drawing maps remain empty. The IdAllocator is part-local.
        var hasImages = part.Images.Count > 0;
        if (hasImages)
        {
            // The picture drawing uses the wp/a/pic namespaces; declare them on the root when images exist.
            root.Add(new XAttribute(XNamespace.Xmlns + "wp", Wp.NamespaceName));
            root.Add(new XAttribute(XNamespace.Xmlns + "a", A.NamespaceName));
            root.Add(new XAttribute(XNamespace.Xmlns + "pic", Pic.NamespaceName));
        }
        if (part.Watermark is not null)
        {
            root.Add(new XAttribute(XNamespace.Xmlns + "v", V.NamespaceName));
            root.Add(new XAttribute(XNamespace.Xmlns + "o", O.NamespaceName));
            root.Add(new XAttribute(XNamespace.Xmlns + "w10", W10.NamespaceName));
        }

        var imagesByRun = hasImages ? BuildHeaderFooterImagesByRun(part) : new Dictionary<Run, ImagePart>();
        var preservedDrawingRelIds = part.PreservedDrawings
            .ToDictionary(drawing => drawing.PartName, drawing => drawing.RelationshipId, StringComparer.Ordinal);
        var drawings = RunDrawings.Empty() with
        {
            Images = imagesByRun,
            PreservedDrawingRelIds = preservedDrawingRelIds
        };

        if (part.Watermark is not null)
            root.Add(BuildWatermarkParagraph(part.Watermark, part.WatermarkImage));

        if (part.Content.Paragraphs.Count == 0 && part.Watermark is null)
            root.Add(new XElement(W + "p"));
        else
            foreach (var paragraph in part.Content.Paragraphs)
                root.Add(BuildParagraph(paragraph, drawings, part.Hyperlinks, preservedNumbering: preservedNumbering, restartOverrides: restartOverrides));

        return new XDocument(root);
    }

    private static XElement BuildWatermarkParagraph(WatermarkOptions options, ImagePart? watermarkImage)
    {
        var color = NormalizeHex(options.FontColorHex, "808080");
        var opacity = Math.Clamp(options.Opacity, 0, 1)
            .ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        var rotation = options.NativeVmlTextRotationDegrees is { } nativeRotation
            && double.IsFinite(nativeRotation)
            ? nativeRotation.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)
            : options.Layout == WatermarkLayout.Diagonal ? "315" : "0";
        var shapeId = watermarkImage is null ? "PowerPlusWaterMarkObject" : "PowerPlusPictureWaterMarkObject";
        var nativeTextShapeType = watermarkImage is null
            ? TryParseNativeVmlTextShapeType(options.NativeVmlTextShapeTypeXml)
            : null;
        var nativeTextPath = watermarkImage is null
            ? TryParseNativeVmlTextPath(options.NativeVmlTextPathXml)
            : null;
        var shapeSize = watermarkImage is null
            ? options.NativeVmlTextWidthPt is > 0 and var textWidthPt
                && options.NativeVmlTextHeightPt is > 0 and var textHeightPt
                ? $"width:{FormatPt(textWidthPt)}pt;height:{FormatPt(textHeightPt)}pt"
                : "width:468pt;height:117pt"
            : options.NativeVmlPictureWidthPt is > 0 and var pictureWidthPt
                && options.NativeVmlPictureHeightPt is > 0 and var pictureHeightPt
                ? $"width:{FormatPt(pictureWidthPt)}pt;height:{FormatPt(pictureHeightPt)}pt"
                : "width:468pt;height:281pt";

        var shapeType = nativeTextShapeType ?? new XElement(V + "shapetype",
            new XAttribute("id", "_x0000_t136"),
            new XAttribute("coordsize", "21600,21600"),
            new XAttribute(O + "spt", "136"),
            new XAttribute("adj", "10800"),
            new XAttribute("path", "m@7,l@8,m@5,21600l@6,21600e"),
            new XElement(V + "formulas",
                new XElement(V + "f", new XAttribute("eqn", "sum #0 0 10800")),
                new XElement(V + "f", new XAttribute("eqn", "prod #0 2 1")),
                new XElement(V + "f", new XAttribute("eqn", "sum 21600 0 @1")),
                new XElement(V + "f", new XAttribute("eqn", "sum 0 0 @2")),
                new XElement(V + "f", new XAttribute("eqn", "sum 21600 0 @3")),
                new XElement(V + "f", new XAttribute("eqn", "if @0 @3 0")),
                new XElement(V + "f", new XAttribute("eqn", "if @0 21600 @1")),
                new XElement(V + "f", new XAttribute("eqn", "if @0 0 @2")),
                new XElement(V + "f", new XAttribute("eqn", "if @0 @3 21600")),
                new XElement(V + "f", new XAttribute("eqn", "mid @5 @6")),
                new XElement(V + "f", new XAttribute("eqn", "mid @8 @5")),
                new XElement(V + "f", new XAttribute("eqn", "mid @7 @8")),
                new XElement(V + "f", new XAttribute("eqn", "mid @6 @7")),
                new XElement(V + "f", new XAttribute("eqn", "sum @6 0 @5"))),
            new XElement(V + "path",
                new XAttribute("textpathok", "t"),
                new XAttribute(O + "connecttype", "custom"),
                new XAttribute(O + "connectlocs", "@9,0;@10,10800;@11,21600;@12,10800"),
                new XAttribute(O + "connectangles", "270,180,90,0")),
            new XElement(V + "textpath", new XAttribute("on", "t"), new XAttribute("fitshape", "t")),
            new XElement(V + "handles",
                new XElement(V + "h",
                    new XAttribute("position", "#0,bottomRight"),
                    new XAttribute("xrange", "6629,14971"))),
            new XElement(O + "lock",
                new XAttribute(V + "ext", "edit"),
                new XAttribute("text", "t"),
                new XAttribute("shapetype", "t")));
        var shapeTypeId = shapeType.Attribute("id")?.Value;
        if (string.IsNullOrWhiteSpace(shapeTypeId))
            shapeTypeId = "_x0000_t136";

        var fill = watermarkImage is null
            ? new XElement(V + "fill",
                new XAttribute("color", color),
                new XAttribute("opacity", opacity))
            : new XElement(V + "fill",
                new XAttribute(R + "id", watermarkImage.RelationshipId),
                new XAttribute("type", "frame"),
                new XAttribute("color2", "FFFFFF"),
                new XAttribute("recolor", options.NativeVmlPictureRecolor is false ? "f" : "t"),
                new XAttribute("opacity", opacity));

        var shape = new XElement(V + "shape",
            new XAttribute("id", shapeId),
            new XAttribute("type", "#" + shapeTypeId),
            new XAttribute("style", $"position:absolute;margin-left:0;margin-top:0;{shapeSize};rotation:{rotation};z-index:-251654144;mso-position-horizontal:center;mso-position-horizontal-relative:margin;mso-position-vertical:center;mso-position-vertical-relative:margin"),
            new XAttribute(O + "allowincell", "f"),
            new XAttribute("stroked", "f"),
            watermarkImage is null ? new XAttribute("fillcolor", color) : null,
            fill,
            watermarkImage is null
                ? BuildVmlTextPath(options, nativeTextPath)
                : null,
            new XElement(W10 + "wrap", new XAttribute("anchorx", "margin"), new XAttribute("anchory", "margin")));

        return new XElement(W + "p",
            new XElement(W + "pPr",
                new XElement(W + "pStyle", new XAttribute(W + "val", "Header"))),
            new XElement(W + "sdt",
                new XElement(W + "sdtPr",
                    new XElement(W + "id", new XAttribute(W + "val", "-824503837")),
                    new XElement(W + "docPartObj",
                        new XElement(W + "docPartGallery", new XAttribute(W + "val", "Watermarks")),
                        new XElement(W + "docPartUnique"))),
                new XElement(W + "sdtContent",
                    new XElement(W + "r",
                        new XElement(W + "rPr", new XElement(W + "noProof")),
                        new XElement(W + "pict",
                            shapeType,
                            shape)))));
    }

    private static XElement? TryParseNativeVmlTextShapeType(string? xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
            return null;

        try
        {
            var shapeType = XElement.Parse(xml, LoadOptions.PreserveWhitespace);
            return shapeType.Name == V + "shapetype"
                && !string.IsNullOrWhiteSpace(shapeType.Attribute("id")?.Value)
                ? shapeType
                : null;
        }
        catch (System.Xml.XmlException)
        {
            return null;
        }
    }

    private static XElement BuildVmlTextPath(WatermarkOptions options, XElement? nativeTextPath)
    {
        var textPath = nativeTextPath is null
            ? new XElement(V + "textpath")
            : new XElement(nativeTextPath);
        textPath.SetAttributeValue("on", options.NativeVmlTextPathEnabled == false ? "f" : "t");
        textPath.SetAttributeValue("fitshape", options.NativeVmlTextFitShape == false ? "f" : "t");
        textPath.SetAttributeValue("style", UpdateVmlTextPathStyle(
            textPath.Attribute("style")?.Value,
            options.FontFamily));
        textPath.SetAttributeValue("string", options.Text);
        return textPath;
    }

    private static XElement? TryParseNativeVmlTextPath(string? xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
            return null;

        try
        {
            var textPath = XElement.Parse(xml, LoadOptions.PreserveWhitespace);
            return textPath.Name == V + "textpath" ? textPath : null;
        }
        catch (System.Xml.XmlException)
        {
            return null;
        }
    }

    private static string UpdateVmlTextPathStyle(string? style, string fontFamily)
    {
        var properties = (style ?? string.Empty)
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(property => !property.StartsWith("font-family:", StringComparison.OrdinalIgnoreCase)
                && !property.StartsWith("font-size:", StringComparison.OrdinalIgnoreCase)
                && !property.StartsWith("font:", StringComparison.OrdinalIgnoreCase))
            .ToList();
        properties.Insert(0, $"font-size:1pt");
        properties.Insert(0, $"font-family:{fontFamily}");
        return string.Join(';', properties);
    }

    private static string NormalizeHex(string? value, string fallback)
    {
        var hex = value?.Trim().TrimStart('#');
        return hex is { Length: 6 } && hex.All(Uri.IsHexDigit) ? hex : fallback;
    }

    /// <summary>
    /// Builds word/footnotes.xml (w:footnotes). Emits the two conventional separator footnotes
    /// (w:footnoteSeparator id=-1, w:continuationSeparator id=0) for Word-friendliness, then one
    /// w:footnote w:id="N" per modelled footnote (ascending id), each holding its paragraphs.
    /// </summary>
    private static XDocument BuildFootnotes(
        TextDocument document,
        IReadOnlyList<ImagePart> images,
        IReadOnlyList<PartLocalPreservedDrawingPart> preservedDrawings,
        IReadOnlyDictionary<string, string> hyperlinks,
        PreservedNumberingPlan? preservedNumbering,
        IReadOnlyDictionary<(ListKind Kind, int Level, int StartAt), int> restartOverrides)
    {
        var footnotes = new XElement(W + "footnotes",
            new XAttribute(XNamespace.Xmlns + "w", W.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "r", R.NamespaceName));
        if (images.Count > 0)
        {
            footnotes.Add(new XAttribute(XNamespace.Xmlns + "wp", Wp.NamespaceName));
            footnotes.Add(new XAttribute(XNamespace.Xmlns + "a", A.NamespaceName));
            footnotes.Add(new XAttribute(XNamespace.Xmlns + "pic", Pic.NamespaceName));
        }

        XElement Separator(int id, string type) =>
            new(W + "footnote",
                new XAttribute(W + "type", type),
                new XAttribute(W + "id", id),
                new XElement(W + "p",
                    new XElement(W + "r", new XElement(W + type))));

        footnotes.Add(Separator(-1, "separator"));
        footnotes.Add(Separator(0, "continuationSeparator"));

        // Footnote chart references resolve against word/_rels/footnotes.xml.rels.
        var noteDrawings = RunDrawings.Empty() with
        {
            Images = BuildNoteImagesByRun(document.Footnotes.Values.OrderBy(note => note.Id).SelectMany(note => note.Content), images),
            PreservedDrawingRelIds = preservedDrawings
                .ToDictionary(drawing => drawing.PartName, drawing => drawing.RelationshipId, StringComparer.Ordinal)
        };
        foreach (var footnote in document.Footnotes.Values.OrderBy(f => f.Id))
        {
            var element = new XElement(W + "footnote", new XAttribute(W + "id", footnote.Id));
            foreach (var paragraph in BuildNoteContent(
                footnote.Content, noteDrawings, hyperlinks, "footnoteRef", preservedNumbering, restartOverrides))
                element.Add(paragraph);
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
    private static XDocument BuildEndnotes(
        TextDocument document,
        IReadOnlyList<ImagePart> images,
        IReadOnlyList<PartLocalPreservedDrawingPart> preservedDrawings,
        IReadOnlyDictionary<string, string> hyperlinks,
        PreservedNumberingPlan? preservedNumbering,
        IReadOnlyDictionary<(ListKind Kind, int Level, int StartAt), int> restartOverrides)
    {
        var endnotes = new XElement(W + "endnotes",
            new XAttribute(XNamespace.Xmlns + "w", W.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "r", R.NamespaceName));
        if (images.Count > 0)
        {
            endnotes.Add(new XAttribute(XNamespace.Xmlns + "wp", Wp.NamespaceName));
            endnotes.Add(new XAttribute(XNamespace.Xmlns + "a", A.NamespaceName));
            endnotes.Add(new XAttribute(XNamespace.Xmlns + "pic", Pic.NamespaceName));
        }

        XElement Separator(int id, string type) =>
            new(W + "endnote",
                new XAttribute(W + "type", type),
                new XAttribute(W + "id", id),
                new XElement(W + "p",
                    new XElement(W + "r", new XElement(W + type))));

        endnotes.Add(Separator(-1, "separator"));
        endnotes.Add(Separator(0, "continuationSeparator"));

        // Endnote chart references resolve against word/_rels/endnotes.xml.rels.
        var noteDrawings = RunDrawings.Empty() with
        {
            Images = BuildNoteImagesByRun(document.Endnotes.Values.OrderBy(note => note.Id).SelectMany(note => note.Content), images),
            PreservedDrawingRelIds = preservedDrawings
                .ToDictionary(drawing => drawing.PartName, drawing => drawing.RelationshipId, StringComparer.Ordinal)
        };
        foreach (var endnote in document.Endnotes.Values.OrderBy(e => e.Id))
        {
            var element = new XElement(W + "endnote", new XAttribute(W + "id", endnote.Id));
            foreach (var paragraph in BuildNoteContent(
                endnote.Content, noteDrawings, hyperlinks, "endnoteRef", preservedNumbering, restartOverrides))
                element.Add(paragraph);
            endnotes.Add(element);
        }

        return new XDocument(endnotes);
    }

    private static IReadOnlyList<XElement> BuildNoteContent(
        IReadOnlyList<Paragraph> content,
        RunDrawings drawings,
        IReadOnlyDictionary<string, string> hyperlinks,
        string referenceElementName,
        PreservedNumberingPlan? preservedNumbering,
        IReadOnlyDictionary<(ListKind Kind, int Level, int StartAt), int> restartOverrides)
    {
        var paragraphs = content.Count == 0
            ? new List<XElement> { new XElement(W + "p") }
            : content.Select(paragraph => BuildParagraph(paragraph, drawings, hyperlinks, preservedNumbering: preservedNumbering, restartOverrides: restartOverrides)).ToList();

        // Word requires a footnoteRef/endnoteRef marker in the note body to display its automatic number.
        // Keep it after pPr so the paragraph remains schema-valid and before the authored note text.
        var referenceRun = new XElement(W + "r", new XElement(W + referenceElementName));
        var properties = paragraphs[0].Element(W + "pPr");
        if (properties is null)
            paragraphs[0].AddFirst(referenceRun);
        else
            properties.AddAfterSelf(referenceRun);

        return paragraphs;
    }

    /// <summary>
    /// Collects the inline images carried by ALL comment paragraphs (in comment-id then paragraph then run
    /// order), assigning each a PART-LOCAL relationship id (<c>rIdImgN</c>, resolved against
    /// <c>word/_rels/comments.xml.rels</c>) and a media file name (<c>comment_imageN.ext</c>) so comment media
    /// never clashes with body/header/footer media. Mirrors <see cref="CollectHeaderFooterImages"/>. Empty when
    /// no comment carries an image — so a text-only-comment document emits no comment media or rels.
    /// </summary>
    private static List<ImagePart> CollectCommentImages(TextDocument document, HashSet<string> usedPartNames)
    {
        var images = new List<ImagePart>();
        foreach (var comment in FlattenComments(document))
            foreach (var paragraph in comment.Content)
                foreach (var run in paragraph.Runs)
                    if (run.Image is { } image)
                    {
                        var index = images.Count + 1;
                        images.Add(new ImagePart(
                            image,
                            $"rIdImg{index}",
                            NextAvailablePartFileName(usedPartNames, "word/media", "comment_image", InlineImage.ExtensionFor(image.Format)),
                            (uint)index));
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
    /// Collects inline images for one note story (footnotes or endnotes). The owning XML part has its own rels
    /// file, so note media must never reuse document-level image relationship ids.
    /// </summary>
    private static List<ImagePart> CollectNoteImages(
        IEnumerable<Paragraph> paragraphs,
        string partPrefix,
        HashSet<string> usedPartNames)
    {
        var images = new List<ImagePart>();
        foreach (var paragraph in paragraphs)
            foreach (var run in paragraph.Runs)
                if (run.Image is { } image)
                {
                    var index = images.Count + 1;
                    images.Add(new ImagePart(
                        image,
                        $"rIdImg{index}",
                        NextAvailablePartFileName(usedPartNames, "word/media", partPrefix + "_image", InlineImage.ExtensionFor(image.Format)),
                        (uint)index));
                }
        return images;
    }

    private static Dictionary<Run, ImagePart> BuildNoteImagesByRun(
        IEnumerable<Paragraph> paragraphs,
        IReadOnlyList<ImagePart> images)
    {
        var map = new Dictionary<Run, ImagePart>();
        var next = 0;
        foreach (var paragraph in paragraphs)
            foreach (var run in paragraph.Runs)
                if (run.Image is not null && next < images.Count)
                    map[run] = images[next++];
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
    private static XDocument BuildComments(
        TextDocument document,
        IReadOnlyList<ImagePart> commentImages,
        IReadOnlyList<PartLocalPreservedDrawingPart> preservedDrawings,
        IReadOnlyDictionary<string, string> hyperlinks,
        PreservedNumberingPlan? preservedNumbering,
        IReadOnlyDictionary<(ListKind Kind, int Level, int StartAt), int> restartOverrides)
    {
        var comments = new XElement(W + "comments",
            new XAttribute(XNamespace.Xmlns + "w", W.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "r", R.NamespaceName));

        // Comment images map their runs → part-local image parts; the picture drawing uses the wp/a/pic
        // namespaces, declared on the root only when a comment actually carries an image (so a text-only
        // comments part stays byte-equivalent to the historical output).
        if (commentImages.Count > 0)
        {
            comments.Add(new XAttribute(XNamespace.Xmlns + "wp", Wp.NamespaceName));
            comments.Add(new XAttribute(XNamespace.Xmlns + "a", A.NamespaceName));
            comments.Add(new XAttribute(XNamespace.Xmlns + "pic", Pic.NamespaceName));
        }
        var imagesByRun = commentImages.Count > 0
            ? BuildCommentImagesByRun(document, commentImages)
            : new Dictionary<Run, ImagePart>();
        var drawings = RunDrawings.Empty() with
        {
            Images = imagesByRun,
            PreservedDrawingRelIds = preservedDrawings
                .ToDictionary(drawing => drawing.PartName, drawing => drawing.RelationshipId, StringComparer.Ordinal)
        };
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
                    var built = BuildParagraph(comment.Content[pi], drawings, hyperlinks, preservedNumbering: preservedNumbering, restartOverrides: restartOverrides);
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

    /// <summary>Builds word/_rels/comments.xml.rels for comment media and preserved charts.</summary>
    private static XDocument BuildCommentsRels(
        IReadOnlyList<ImagePart> commentImages,
        IReadOnlyList<PartLocalPreservedDrawingPart> preservedDrawings,
        IReadOnlyDictionary<string, string> hyperlinks)
    {
        var relationships = OpcRelationships.CreateRoot();
        foreach (var image in commentImages)
        {
            if (image.HasEmbeddedPayload)
                relationships.Add(OpcRelationships.CreateRelationship(
                    image.RelationshipId,
                    ImageRel,
                    "media/" + image.FileName));
            if (image.LinkRelationshipId is { } linkRelationshipId)
                relationships.Add(OpcRelationships.CreateRelationship(
                    linkRelationshipId,
                    ImageRel,
                    image.Image.LinkedImageTarget!,
                    external: true));
        }
        foreach (var drawing in preservedDrawings)
            relationships.Add(OpcRelationships.CreateRelationship(
                drawing.RelationshipId,
                drawing.RelationshipType,
                DocumentRelativeTarget(drawing.PartName)));
        foreach (var (url, relationshipId) in hyperlinks)
            relationships.Add(OpcRelationships.CreateRelationship(relationshipId, HyperlinkRel, url, external: true));
        return new XDocument(relationships);
    }

    private static XElement BuildBlock(
        Block block,
        RunDrawings drawings,
        IReadOnlyDictionary<string, string> hyperlinks,
        IReadOnlyDictionary<Section, IReadOnlyList<HeaderFooterPart>> partsBySection,
        PreservedNumberingPlan? preservedNumbering = null,
        IReadOnlyDictionary<(ListKind Kind, int Level, int StartAt), int>? restartOverrides = null) => block switch
    {
        Table table => BuildTable(table, drawings, hyperlinks, preservedNumbering, restartOverrides),
        AltChunkBlock altChunk when drawings.PreservedDrawingRelIds is { } preservedRelIds
            && preservedRelIds.TryGetValue(altChunk.PreservedPartName, out var relationshipId)
            => new XElement(W + "altChunk", new XAttribute(R + "id", relationshipId)),
        // Only top-level body paragraphs can end a non-final section, so the per-section header/footer map
        // is threaded here (and nowhere else); table-cell/header/footer/footnote paragraphs pass no map.
        Paragraph paragraph => BuildParagraph(paragraph, drawings, hyperlinks, partsBySection, preservedNumbering, restartOverrides),
        _ => new XElement(W + "p")
    };

    // Light fills used by the table-style toggles: a blue-grey header fill and a grey banded-row fill.
    // These are emitted as cell shading on write so the styled docx renders correctly in Word; the
    // HeaderRow/BandedRows flags themselves round-trip via w:tblLook (see BuildTableProperties).
    private const string HeaderFill = "D9E2F3";
    private const string BandedFill = "F2F2F2";

    private static XElement BuildTable(Table table, RunDrawings drawings, IReadOnlyDictionary<string, string> hyperlinks, PreservedNumberingPlan? preservedNumbering = null, IReadOnlyDictionary<(ListKind Kind, int Level, int StartAt), int>? restartOverrides = null)
    {
        var tbl = new XElement(W + "tbl", BuildTableProperties(table));

        // The table grid (one w:gridCol per column) follows w:tblPr when explicit widths are known.
        // Reconcile against the actual grid-column total (max over rows of summed GridSpans) to keep the
        // file valid even when ColumnWidthsPt has drifted out of sync with the row contents (H4 fix):
        // pad with the last known width when the model has fewer entries, truncate when it has more.
        if (table.ColumnWidthsPt.Count > 0)
        {
            var actualGridCols = table.Rows.Count == 0 ? table.ColumnWidthsPt.Count
                : table.Rows.Max(r => r.Cells.Sum(c => Math.Max(1, c.GridSpan)));
            var widths = table.ColumnWidthsPt;
            var grid = new XElement(W + "tblGrid");
            for (var col = 0; col < actualGridCols; col++)
            {
                // Use the stored width for this column; if widths are shorter, repeat the last entry
                // so Word at least has a plausible grid rather than zero-width phantom columns.
                var w = col < widths.Count ? widths[col] : widths[^1];
                grid.Add(new XElement(W + "gridCol", new XAttribute(W + "w", PointsToDxa(w))));
            }
            tbl.Add(grid);
        }

        var fmt = table.Formatting;
        for (var rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
        {
            var row = table.Rows[rowIndex];
            var isHeaderRow = fmt.HeaderRow && rowIndex == 0;
            // Banded rows shade alternate body rows. Word's band 1 starts on the first body row.
            var bandedShade = fmt.BandedRows
                && !isHeaderRow
                && TableBanding.IsBandedBodyRow(rowIndex, fmt.HeaderRow);

            var tr = new XElement(W + "tr");
            // Row properties (w:trPr): cantSplit / trHeight / tblHeader, in CT_TrPr schema order. Emitted
            // only when a non-default row property is set, so plain rows stay unchanged.
            var trPr = new XElement(W + "trPr");
            if (!row.AllowBreakAcrossPages)
                trPr.Add(new XElement(W + "cantSplit"));
            if (row.HeightPt is { } heightPt)
                trPr.Add(new XElement(W + "trHeight",
                    new XAttribute(W + "val", PointsToDxa(heightPt)),
                    new XAttribute(W + "hRule", row.HeightRule switch
                    {
                        TableRowHeightRule.Exact => "exact",
                        TableRowHeightRule.AtLeast => "atLeast",
                        _ => "auto"
                    })));
            // Repeat the header row across page breaks (w:trPr/w:tblHeader) when requested.
            if (isHeaderRow && fmt.RepeatHeaderRow)
                trPr.Add(new XElement(W + "tblHeader"));
            if (trPr.HasElements)
                tr.Add(trPr);

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
                        tc.Add(BuildParagraph(isHeaderRow ? BoldHeaderParagraph(paragraph) : paragraph, drawings, hyperlinks, preservedNumbering: preservedNumbering, restartOverrides: restartOverrides));
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
            BlockContentControl = paragraph.BlockContentControl,
            Formatting = paragraph.Formatting,
            StyleId = paragraph.StyleId,
        };
        copy.BookmarkNames.AddRange(paragraph.BookmarkNames);
        copy.BookmarkBoundaries.AddRange(paragraph.BookmarkBoundaries);
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
                SubDocument = run.SubDocument,
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
        // Children must follow the CT_TblPr schema order, else Word's strict validator rejects the table:
        // tblStyle, tblpPr, tblW, jc, tblCellSpacing, tblInd, tblBorders, tblLayout, tblCellMar, tblLook.
        var tblPr = new XElement(W + "tblPr");

        // Named table style (w:tblStyle), placed first in CT_TblPr. Emitted when TableStyleId is set so
        // the applied catalog style round-trips through Word. The corresponding w:style definition is
        // written into styles.xml by BuildStyles (see BuildTableStyleElement).
        if (table.TableStyleId is { Length: > 0 } styleId)
            tblPr.Add(new XElement(W + "tblStyle", new XAttribute(W + "val", styleId)));

        // Floating-table position (w:tblpPr): a minimal anchor so Word treats the table as floating
        // ("Text wrapping: Around"). Emitted only when text wrapping is on.
        if (table.TextWrapping)
            tblPr.Add(new XElement(W + "tblpPr",
                new XAttribute(W + "leftFromText", 180),
                new XAttribute(W + "rightFromText", 180),
                new XAttribute(W + "vertAnchor", "text"),
                new XAttribute(W + "horzAnchor", "text"),
                new XAttribute(W + "tblpY", 1)));

        // Preferred table width (w:tblW): a fixed dxa width when set, else automatic (the historical default).
        tblPr.Add(table.PreferredWidthPt is { } widthPt
            ? new XElement(W + "tblW", new XAttribute(W + "w", PointsToDxa(widthPt)), new XAttribute(W + "type", "dxa"))
            : new XElement(W + "tblW", new XAttribute(W + "w", 0), new XAttribute(W + "type", "auto")));

        // Table alignment (w:jc): emitted only when not Left (the default).
        if (table.Alignment != TableAlignment.Left)
            tblPr.Add(new XElement(W + "jc", new XAttribute(W + "val",
                table.Alignment == TableAlignment.Center ? "center" : "right")));

        // Spacing between cells (w:tblCellSpacing), emitted only when set.
        if (table.CellSpacingPt is { } spacingPt)
            tblPr.Add(new XElement(W + "tblCellSpacing",
                new XAttribute(W + "w", PointsToDxa(spacingPt)), new XAttribute(W + "type", "dxa")));

        // Indent from the left margin (w:tblInd), emitted only when set.
        if (table.IndentFromLeftPt is { } indentPt)
            tblPr.Add(new XElement(W + "tblInd",
                new XAttribute(W + "w", PointsToDxa(indentPt)), new XAttribute(W + "type", "dxa")));

        if (table.Formatting.Borders)
        {
            XElement Border(string name, TableBorderEdge? edge) => new(W + name,
                new XAttribute(W + "val", BorderLineStyles.ToToken(edge?.Style ?? BorderLineStyle.Single)),
                new XAttribute(W + "sz", (int)Math.Round((edge?.WidthPt ?? 0.5) * 8)),
                new XAttribute(W + "space", 0),
                new XAttribute(W + "color", edge?.ColorToken ?? "auto"));
            var borders = table.Borders;
            var tableBorders = new XElement(W + "tblBorders");
            if (borders is null)
            {
                tableBorders.Add(
                    Border("top", null), Border("left", null), Border("bottom", null), Border("right", null),
                    Border("insideH", null), Border("insideV", null));
            }
            else
            {
                if (borders.Top is { } top) tableBorders.Add(Border("top", top));
                if (borders.Left is { } left) tableBorders.Add(Border("left", left));
                if (borders.Bottom is { } bottom) tableBorders.Add(Border("bottom", bottom));
                if (borders.Right is { } right) tableBorders.Add(Border("right", right));
                if (borders.InsideHorizontal is { } insideHorizontal) tableBorders.Add(Border("insideH", insideHorizontal));
                if (borders.InsideVertical is { } insideVertical) tableBorders.Add(Border("insideV", insideVertical));
            }
            tblPr.Add(tableBorders);
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

        // Word auto-fits a table when tblLayout is absent, even when tblGrid has explicit widths.
        // Emit the model's fixed default explicitly so authored grid widths survive in Word.
        tblPr.Add(new XElement(W + "tblLayout", new XAttribute(W + "type",
            table.AutoFit == AutoFitMode.Fixed ? "fixed" : "autofit")));

        // Default cell margins (w:tblCellMar): inside padding applied to cells with no own override.
        // Emitted only when set, so plain tables stay unchanged. Follows tblBorders in CT_TblPr order.
        if (table.DefaultCellMargins is { } cellMar)
            tblPr.Add(BuildCellMarginsElement("tblCellMar", cellMar));

        // w:tblLook carries the table-style toggles so they round-trip without a full table-style part:
        // w:firstRow="1" persists HeaderRow; w:noHBand="0" persists BandedRows (banding on). The flags are
        // recovered on read from these attributes (see DocxReader.ReadTable). Only emitted when a toggle
        // is set, so plain tables stay unchanged.
        var fmt = table.Formatting;
        if (fmt.HeaderRow || fmt.LastRow || fmt.FirstColumn || fmt.LastColumn || fmt.BandedRows || fmt.BandedColumns)
        {
            tblPr.Add(new XElement(W + "tblLook",
                new XAttribute(W + "firstRow", fmt.HeaderRow ? "1" : "0"),
                new XAttribute(W + "lastRow", fmt.LastRow ? "1" : "0"),
                new XAttribute(W + "firstColumn", fmt.FirstColumn ? "1" : "0"),
                new XAttribute(W + "lastColumn", fmt.LastColumn ? "1" : "0"),
                new XAttribute(W + "noHBand", fmt.BandedRows ? "0" : "1"),
                new XAttribute(W + "noVBand", fmt.BandedColumns ? "0" : "1")));
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
        // Per-cell border override (w:tcBorders): emitted only when the cell has at least one explicit edge
        // so plain cells inherit table-level borders unchanged. Edge order follows CT_TcBorders schema.
        if (cell.Borders is { IsEmpty: false } borders)
            tcPr.Add(BuildCellBordersElement(borders));
        // Per-cell margin override (w:tcMar) and vertical alignment (w:vAlign), in CT_TcPr schema order
        // (tcMar before vAlign, both after tcBorders). Emitted only when set, so plain cells stay unchanged.
        if (cell.Margins is { } margins)
            tcPr.Add(BuildCellMarginsElement("tcMar", margins));
        if (cell.VerticalAlignment != TableCellVerticalAlignment.Top)
            tcPr.Add(new XElement(W + "vAlign", new XAttribute(W + "val",
                cell.VerticalAlignment == TableCellVerticalAlignment.Center ? "center" : "bottom")));
        // Text direction (w:textDirection): emitted only when not the default horizontal direction so
        // existing cells round-trip unchanged. Maps to the same btLr/tbRl tokens Word uses.
        if (cell.TextDirection != CellTextDirection.Horizontal)
            tcPr.Add(new XElement(W + "textDirection", new XAttribute(W + "val",
                cell.TextDirection == CellTextDirection.Rotate90 ? "btLr" : "tbRl")));
        return tcPr.HasElements ? tcPr : null;
    }

    // Builds a w:tcBorders element with the four per-edge child elements, each carrying w:val/w:sz/w:color.
    // Only non-null edges are emitted so the element only carries explicitly overridden edges.
    private static XElement BuildCellBordersElement(CellBorders borders)
    {
        var tcBorders = new XElement(W + "tcBorders");
        void Edge(string name, CellBorderEdge? edge)
        {
            if (edge is null) return;
            tcBorders.Add(new XElement(W + name,
                new XAttribute(W + "val", BorderLineStyles.ToToken(edge.Style)),
                new XAttribute(W + "sz", (int)Math.Round(edge.WidthPt * 8)),
                new XAttribute(W + "space", 0),
                new XAttribute(W + "color", edge.ColorHex.TrimStart('#'))));
        }
        Edge("top", borders.Top);
        Edge("left", borders.Left);
        Edge("bottom", borders.Bottom);
        Edge("right", borders.Right);
        return tcBorders;
    }

    // Builds a cell-margins container (w:tblCellMar for the table default, or w:tcMar for a per-cell
    // override) with the four edge elements in top/left/bottom/right order, each a dxa width.
    private static XElement BuildCellMarginsElement(string name, TableCellMargins margins)
    {
        XElement Edge(string edge, double pt) => new(W + edge,
            new XAttribute(W + "w", PointsToDxa(pt)), new XAttribute(W + "type", "dxa"));
        return new XElement(W + name,
            Edge("top", margins.TopPt),
            Edge("left", margins.LeftPt),
            Edge("bottom", margins.BottomPt),
            Edge("right", margins.RightPt));
    }

    /// <summary>True when two runs carry the same tracked-change identity (so they coalesce).</summary>
    private static bool SameRevision(Run a, Run b) =>
        a.Revision == b.Revision
        && string.Equals(a.RevisionAuthor, b.RevisionAuthor, StringComparison.Ordinal)
        && string.Equals(a.RevisionDateXml, b.RevisionDateXml, StringComparison.Ordinal)
        && a.MoveRevisionId == b.MoveRevisionId;

    /// <summary>
    /// Builds a tracked-change wrapper carrying its id and author/date. Move revisions preserve their source
    /// w:moveFrom/w:moveTo wrapper and shared id; ordinary revisions use fresh w:ins/w:del ids.
    /// </summary>
    private static XElement NewRevisionWrapper(Run run, IdAllocator ids)
    {
        var isMove = run.MoveRevisionId is not null;
        var name = isMove
            ? run.Revision == RevisionKind.Deleted ? "moveFrom" : "moveTo"
            : run.Revision == RevisionKind.Deleted ? "del" : "ins";
        var wrapper = new XElement(W + name,
            new XAttribute(W + "id", run.MoveRevisionId ?? ids.NextRevisionId()));
        if (run.RevisionAuthor is { Length: > 0 } author)
            wrapper.Add(new XAttribute(W + "author", author));
        if (run.RevisionDateXml is { Length: > 0 } date)
            wrapper.Add(new XAttribute(W + "date", date));
        return wrapper;
    }

    /// <summary>
    /// Builds a w:rPrChange (tracked formatting change) carrying a unique w:id plus the author/date, with a
    /// nested w:rPr holding the run's <em>previous</em> formatting. The nested w:rPr is always present (even
    /// when empty) because that empty element is how Word records "the run previously had default
    /// formatting". This element is the last child of the run's w:rPr.
    /// </summary>
    private static XElement BuildRprChange(FormatRevision revision, IdAllocator ids)
    {
        var change = new XElement(W + "rPrChange",
            new XAttribute(W + "id", ids.NextRevisionId()));
        if (revision.Author is { Length: > 0 } author)
            change.Add(new XAttribute(W + "author", author));
        if (revision.DateXml is { Length: > 0 } date)
            change.Add(new XAttribute(W + "date", date));
        change.Add(BuildRunProperties(revision.PreviousFormatting) ?? new XElement(W + "rPr"));
        return change;
    }

    /// <summary>
    /// Builds a w:pPrChange (tracked paragraph-formatting change) carrying a unique w:id plus the
    /// author/date, with a nested w:pPr holding the paragraph's <em>previous</em> formatting. The nested
    /// w:pPr is always present (even when empty) because that empty element is how Word records "the
    /// paragraph previously had default formatting". This element is the LAST child of the paragraph's
    /// w:pPr, after w:sectPr, mirroring how w:rPrChange is the last child of w:rPr.
    /// </summary>
    private static XElement BuildPPrChange(ParagraphFormatRevision revision, IdAllocator ids)
    {
        var change = new XElement(W + "pPrChange",
            new XAttribute(W + "id", ids.NextRevisionId()));
        if (revision.Author is { Length: > 0 } author)
            change.Add(new XAttribute(W + "author", author));
        if (revision.DateXml is { Length: > 0 } date)
            change.Add(new XAttribute(W + "date", date));
        // The nested w:pPr captures the previous paragraph formatting. Use the style-scoped builder
        // (alignment, indents, spacing) since pPrChange carries only formatting, never list/section
        // instance concerns. Always emit the element even when empty to signal "previously default".
        change.Add(BuildStyleParagraphProperties(revision.PreviousParagraphFormatting) ?? new XElement(W + "pPr"));
        return change;
    }

    private static XElement BuildBlockSdtProperties(BlockContentControl control)
    {
        var sdtPr = new XElement(W + "sdtPr");
        if (control.Alias is { Length: > 0 } alias)
            sdtPr.Add(new XElement(W + "alias", new XAttribute(W + "val", alias)));
        if (ContentControlLockToken(control.LockMode) is { } lockToken)
            sdtPr.Add(new XElement(W + "lock", new XAttribute(W + "val", lockToken)));
        AddContentControlWordMetadata(sdtPr, control.WordMetadata);
        if (control.Tag is { Length: > 0 } tag)
            sdtPr.Add(new XElement(W + "tag", new XAttribute(W + "val", tag)));

        var gallery = control.DocPartGallery;
        if (control.Kind == BlockContentControlKind.Bibliography && string.IsNullOrWhiteSpace(gallery))
            gallery = BlockContentControl.BibliographyGallery;

        if (control.Kind == BlockContentControlKind.DocumentPart)
            sdtPr.Add(BuildDocPartList(
                gallery,
                control.DocPartCategory,
                control.DocPartUnique));

        var hasDocPartObject = control.Kind is BlockContentControlKind.Bibliography
            or BlockContentControlKind.BuildingBlockGallery
            || !string.IsNullOrWhiteSpace(gallery)
            || !string.IsNullOrWhiteSpace(control.DocPartCategory)
            || control.DocPartUnique;
        if (control.Kind != BlockContentControlKind.DocumentPart && hasDocPartObject)
            sdtPr.Add(BuildDocPartObject(
                gallery,
                control.DocPartCategory,
                control.DocPartUnique || control.Kind == BlockContentControlKind.Bibliography));

        if (control.Kind == BlockContentControlKind.RichText)
            sdtPr.Add(new XElement(W + "richText"));
        else if (control.Kind == BlockContentControlKind.PlainText)
            sdtPr.Add(new XElement(W + "text"));
        else if (control.Kind == BlockContentControlKind.Group)
            sdtPr.Add(new XElement(W + "group"));
        else if (control.Kind == BlockContentControlKind.Citation)
            sdtPr.Add(new XElement(W + "citation"));
        else if (control.Kind == BlockContentControlKind.RepeatingSection)
        {
            var repeatingSection = new XElement(W15 + "repeatingSection");
            if (control.RepeatingSectionTitle is { Length: > 0 } title)
                repeatingSection.Add(new XElement(W15 + "sectionTitle", new XAttribute(W + "val", title)));
            if (control.DoNotAllowInsertDeleteSection)
                repeatingSection.Add(new XElement(W15 + "doNotAllowInsertDeleteSection"));
            sdtPr.Add(repeatingSection);
        }
        else if (control.Kind == BlockContentControlKind.RepeatingSectionItem)
            sdtPr.Add(new XElement(W15 + "repeatingSectionItem"));

        return sdtPr;
    }

    private static XElement BuildDocPartObject(string? gallery, string? category, bool unique)
        => BuildDocPart(W + "docPartObj", gallery, category, unique);

    private static XElement BuildDocPartList(string? gallery, string? category, bool unique)
        => BuildDocPart(W + "docPartList", gallery, category, unique);

    private static XElement BuildDocPart(XName elementName, string? gallery, string? category, bool unique)
    {
        var docPart = new XElement(elementName);
        if (!string.IsNullOrWhiteSpace(gallery))
            docPart.Add(new XElement(W + "docPartGallery", new XAttribute(W + "val", gallery)));
        if (!string.IsNullOrWhiteSpace(category))
            docPart.Add(new XElement(W + "docPartCategory", new XAttribute(W + "val", category)));
        if (unique)
            docPart.Add(new XElement(W + "docPartUnique"));
        return docPart;
    }

    /// <summary>
    /// Builds the w:sdtPr (content-control properties) for a content control. Emits w:tag / w:alias when
    /// set, then the control-kind element: w:text for a plain-text control; a w14:checkbox carrying the
    /// checked state (w14:checked val="1"/"0") for a checkbox; w:richText for a rich-text control; a
    /// w:date carrying date-picker format and metadata; a w:dropDownList / w:comboBox carrying a
    /// w:listItem (w:displayText/w:value) per choice for a list control; an empty w:picture for a picture
    /// control; w:docPartList for a document-part list; w:docPartObj for a building-block gallery;
    /// w:group for a Group control; or w:citation for a Citation control. This is the minimal valid shape
    /// FreeW's own reader recovers (see <see cref="DocxReader"/>).
    /// </summary>
    private static XElement BuildSdtProperties(ContentControl control)
    {
        var sdtPr = new XElement(W + "sdtPr");
        if (control.Alias is { Length: > 0 } alias)
            sdtPr.Add(new XElement(W + "alias", new XAttribute(W + "val", alias)));
        if (ContentControlLockToken(control.LockMode) is { } lockToken)
            sdtPr.Add(new XElement(W + "lock", new XAttribute(W + "val", lockToken)));
        AddContentControlWordMetadata(sdtPr, control.WordMetadata);
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
                sdtPr.Add(BuildDateElement(control));
                break;
            case ContentControlKind.DropDownList:
                sdtPr.Add(BuildListElement(W + "dropDownList", control.Items));
                break;
            case ContentControlKind.ComboBox:
                sdtPr.Add(BuildListElement(W + "comboBox", control.Items));
                break;
            case ContentControlKind.Picture:
                sdtPr.Add(new XElement(W + "picture"));
                break;
            case ContentControlKind.DocumentPart:
                sdtPr.Add(BuildDocPartList(
                    control.DocPartGallery,
                    control.DocPartCategory,
                    control.DocPartUnique));
                break;
            case ContentControlKind.BuildingBlockGallery:
                sdtPr.Add(BuildDocPartObject(
                    control.DocPartGallery,
                    control.DocPartCategory,
                    control.DocPartUnique));
                break;
            case ContentControlKind.Group:
                sdtPr.Add(new XElement(W + "group"));
                break;
            case ContentControlKind.Citation:
                sdtPr.Add(new XElement(W + "citation"));
                break;
            default:
                sdtPr.Add(new XElement(W + "text"));
                break;
        }
        return sdtPr;
    }

    private static XElement BuildDateElement(ContentControl control)
    {
        var date = new XElement(W + "date");
        if (control.DateMetadata?.FullDate is { Length: > 0 } fullDate)
            date.Add(new XAttribute(W + "fullDate", fullDate));

        date.Add(new XElement(W + "dateFormat",
            new XAttribute(W + "val", control.DateFormat ?? ContentControl.DefaultDateFormat)));
        if (control.DateMetadata?.LanguageId is { Length: > 0 } languageId)
            date.Add(new XElement(W + "lid", new XAttribute(W + "val", languageId)));
        if (control.DateMetadata?.StoreMappedDataAs is { Length: > 0 } storeMappedDataAs)
            date.Add(new XElement(W + "storeMappedDataAs", new XAttribute(W + "val", storeMappedDataAs)));
        if (control.DateMetadata?.Calendar is { Length: > 0 } calendar)
            date.Add(new XElement(W + "calendar", new XAttribute(W + "val", calendar)));
        return date;
    }

    private static void AddContentControlWordMetadata(XElement sdtPr, ContentControlWordMetadata? metadata)
    {
        if (metadata is null)
            return;

        if (metadata.PlaceholderDocPart is { Length: > 0 } placeholder)
            sdtPr.Add(new XElement(W + "placeholder",
                new XElement(W + "docPart", new XAttribute(W + "val", placeholder))));
        if (metadata.ShowingPlaceholder)
            sdtPr.Add(new XElement(W + "showingPlcHdr"));
        if (metadata.DataBinding is { } binding)
        {
            var element = new XElement(W + "dataBinding");
            if (binding.PrefixMappings is { Length: > 0 } prefixMappings)
                element.Add(new XAttribute(W + "prefixMappings", prefixMappings));
            if (binding.XPath is { Length: > 0 } xpath)
                element.Add(new XAttribute(W + "xpath", xpath));
            if (binding.StoreItemId is { Length: > 0 } storeItemId)
                element.Add(new XAttribute(W + "storeItemID", storeItemId));
            sdtPr.Add(element);
        }
        if (metadata.Temporary)
            sdtPr.Add(new XElement(W + "temporary"));
        if (metadata.Id is { Length: > 0 } id)
            sdtPr.Add(new XElement(W + "id", new XAttribute(W + "val", id)));
        if (metadata.Color is { Length: > 0 } color)
            sdtPr.Add(new XElement(W15 + "color", new XAttribute(W + "val", color)));
        if (metadata.Appearance is { Length: > 0 } appearance)
            sdtPr.Add(new XElement(W15 + "appearance", new XAttribute(W15 + "val", appearance)));
    }

    private static string? ContentControlLockToken(ContentControlLockMode mode) => mode switch
    {
        ContentControlLockMode.Unlocked => "unlocked",
        ContentControlLockMode.ContentLocked => "contentLocked",
        ContentControlLockMode.ControlLocked => "sdtLocked",
        ContentControlLockMode.ControlAndContentLocked => "sdtContentLocked",
        _ => null,
    };

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
        PreservedNumberingPlan? preservedNumbering = null,
        IReadOnlyDictionary<(ListKind Kind, int Level, int StartAt), int>? restartOverrides = null)
    {
        var p = new XElement(W + "p");
        var pPr = BuildParagraphProperties(paragraph, partsBySection, preservedNumbering, restartOverrides, drawings.Ids);
        if (pPr is not null)
            p.Add(pPr);

        // Imported boundaries retain their exact run-relative positions below. Names with no imported
        // boundary are FreeW-authored bookmarks and keep the historical whole-paragraph behavior.
        var publicBookmarkNames = paragraph.BookmarkNames
            .Where(name => !string.IsNullOrEmpty(name))
            .ToHashSet(StringComparer.Ordinal);
        var boundaryBookmarkNames = paragraph.BookmarkBoundaries
            .Where(boundary => boundary.Kind == BookmarkBoundaryKind.Start
                && boundary.Name is { Length: > 0 } name
                && (name == "_GoBack" || publicBookmarkNames.Contains(name)))
            .Select(boundary => boundary.Name!)
            .ToHashSet(StringComparer.Ordinal);
        var wholeParagraphBookmarkIds = new List<int>();
        foreach (var bookmarkName in paragraph.BookmarkNames)
        {
            if (string.IsNullOrEmpty(bookmarkName) || boundaryBookmarkNames.Contains(bookmarkName))
                continue;
            var bId = drawings.Ids.NextBookmarkId();
            wholeParagraphBookmarkIds.Add(bId);
            p.Add(new XElement(W + "bookmarkStart",
                new XAttribute(W + "id", bId),
                new XAttribute(W + "name", bookmarkName)));
        }

        var bookmarkBoundariesByRunIndex = paragraph.BookmarkBoundaries
            .Select(boundary => boundary with { RunIndex = Math.Clamp(boundary.RunIndex, 0, paragraph.Runs.Count) })
            .GroupBy(boundary => boundary.RunIndex)
            .ToDictionary(group => group.Key, group => group.ToList());

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

        bool IsActiveBookmarkBoundary(BookmarkBoundary boundary) =>
            boundary.Kind == BookmarkBoundaryKind.End
            || boundary.Name is { Length: > 0 } name
                && (name == "_GoBack" || publicBookmarkNames.Contains(name));

        bool HasParagraphBookmarkBoundaryAt(int runIndex) =>
            bookmarkBoundariesByRunIndex.TryGetValue(runIndex, out var boundaries)
            && boundaries.Any(boundary => boundary.OwnerControl is null && IsActiveBookmarkBoundary(boundary));

        void EmitBookmarkBoundariesAt(int runIndex, XElement target, ContentControl? ownerControl)
        {
            if (!bookmarkBoundariesByRunIndex.TryGetValue(runIndex, out var boundaries))
                return;

            foreach (var boundary in boundaries)
            {
                if (!ReferenceEquals(boundary.OwnerControl, ownerControl))
                    continue;

                if (boundary.Kind == BookmarkBoundaryKind.Start)
                {
                    if (boundary.Name is not { Length: > 0 } name
                        || (name != "_GoBack" && !publicBookmarkNames.Contains(name)))
                    {
                        continue;
                    }

                    if (ReferenceEquals(target, p))
                        FlushRevision();
                    var start = new XElement(W + "bookmarkStart",
                        new XAttribute(W + "id", drawings.Ids.BeginBookmark(boundary.PairKey)),
                        new XAttribute(W + "name", name));
                    if (boundary.ColumnFirst is { } columnFirst)
                        start.Add(new XAttribute(W + "colFirst", columnFirst));
                    if (boundary.ColumnLast is { } columnLast)
                        start.Add(new XAttribute(W + "colLast", columnLast));
                    if (boundary.DisplacedByCustomXml is { Length: > 0 } displaced)
                        start.Add(new XAttribute(W + "displacedByCustomXml", displaced));
                    target.Add(start);
                }
                else if (drawings.Ids.TryEndBookmark(boundary.PairKey, out var id))
                {
                    if (ReferenceEquals(target, p))
                        FlushRevision();
                    var end = new XElement(W + "bookmarkEnd", new XAttribute(W + "id", id));
                    if (boundary.DisplacedByCustomXml is { Length: > 0 } displaced)
                        end.Add(new XAttribute(W + "displacedByCustomXml", displaced));
                    target.Add(end);
                }
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
            EmitBookmarkBoundariesAt(i, p, ownerControl: null);

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

            // A Mark Citation (TA) is an instruction-only complex field in native Word packages.
            // It must be emitted as three paragraph siblings rather than a w:fldSimple.
            if (runs[i].Citation is { } citation)
            {
                var fieldRun = runs[i++];
                var rPr = BuildRunProperties(fieldRun.Formatting);
                XElement WithProps(params object[] children)
                {
                    var r = new XElement(W + "r");
                    if (rPr is not null)
                        r.Add(new XElement(rPr));
                    r.Add(children);
                    return r;
                }

                Content(fieldRun, WithProps(new XElement(W + "fldChar", new XAttribute(W + "fldCharType", "begin"))));
                Content(fieldRun, WithProps(new XElement(W + "instrText",
                    new XAttribute(XNamespace.Xml + "space", "preserve"), CitationInstruction(citation))));
                Content(fieldRun, WithProps(new XElement(W + "fldChar", new XAttribute(W + "fldCharType", "end"))));
                continue;
            }

            // A content control (w:sdt) wraps the maximal span of consecutive runs sharing the same
            // ContentControl instance. The wrapped run(s) keep their ordinary w:r form inside w:sdtContent;
            // the sdt itself still routes through the revision wrapper so a control can sit inside a
            // tracked change. Content controls are not also hyperlinks/comments in practice.
            var control = runs[i].Control;
            if (control is not null
                && (control.Kind != ContentControlKind.Citation || runs[i].ComplexField is null))
            {
                var head = runs[i];
                var content = new XElement(W + "sdtContent");
                while (i < runs.Count && ReferenceEquals(runs[i].Control, control)
                    && (runs[i].IsCommentReference ? null : runs[i].CommentId) == openCommentId
                    && SameRevision(head, runs[i]))
                {
                    EmitBookmarkBoundariesAt(i, content, control);
                    content.Add(BuildRun(runs[i++], drawings, hyperlinks, preservedNumbering, restartOverrides));
                }
                EmitBookmarkBoundariesAt(i, content, control);
                var sdt = new XElement(W + "sdt", BuildSdtProperties(control), content);
                Content(head, sdt);
                continue;
            }

            // A complex field (Word's w:fldChar begin / w:instrText / separate / result / end sequence)
            // emits FIVE runs rather than one: a begin fldChar, an instrText run carrying the raw field
            // instruction, a separate fldChar, the cached-result run, then an end fldChar. The reader
            // collapses this sequence back into a single ComplexField run. Routed through Content so it
            // sits correctly inside an open revision/comment context, like any other run.
            var complex = runs[i].ComplexField;
            if (complex is not null)
            {
                var fieldRun = runs[i++];
                var rPr = BuildRunProperties(fieldRun.Formatting);
                XElement WithProps(params object[] children)
                {
                    var r = new XElement(W + "r");
                    if (rPr is not null)
                        r.Add(new XElement(rPr));
                    r.Add(children);
                    return r;
                }

                var fieldElements = new List<XElement>
                {
                    WithProps(new XElement(W + "fldChar", new XAttribute(W + "fldCharType", "begin"))),
                    WithProps(new XElement(W + "instrText",
                        new XAttribute(XNamespace.Xml + "space", "preserve"), SanitizeXmlText(complex.Instruction))),
                    WithProps(new XElement(W + "fldChar", new XAttribute(W + "fldCharType", "separate")))
                };
                if (fieldRun.Text.Length > 0)
                    fieldElements.Add(WithProps(new XElement(W + "t",
                        new XAttribute(XNamespace.Xml + "space", "preserve"), fieldRun.Text)));
                fieldElements.Add(WithProps(new XElement(W + "fldChar", new XAttribute(W + "fldCharType", "end"))));

                if (complex.Keyword == "CITATION")
                {
                    // Word stores a CITATION field inside a citation content control. Without this wrapper,
                    // Word may treat adjacent citation fields as one malformed field and hide later results.
                    // Imported explicit Citation controls retain their common SDT metadata; newly-created
                    // fields receive only a generated Word content-control identity.
                    var citationProperties = fieldRun.Control is { Kind: ContentControlKind.Citation } citationControl
                        ? BuildSdtProperties(citationControl)
                        : BuildSdtProperties(new ContentControl(
                            ContentControlKind.Citation,
                            WordMetadata: new ContentControlWordMetadata(
                                Id: drawings.Ids.NextContentControlId().ToString(
                                    System.Globalization.CultureInfo.InvariantCulture))));
                    var citationSdt = new XElement(W + "sdt",
                        citationProperties,
                        new XElement(W + "sdtContent", fieldElements));
                    Content(fieldRun, citationSdt);
                }
                else
                {
                    foreach (var fieldElement in fieldElements)
                        Content(fieldRun, fieldElement);
                }
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
                var firstHyperlinkRun = i;
                while (i < runs.Count && runs[i].HyperlinkUrl == url && runs[i].HyperlinkTooltip == tooltip && (runs[i].IsCommentReference ? null : runs[i].CommentId) == openCommentId && SameRevision(head, runs[i])
                    && (i == firstHyperlinkRun || !HasParagraphBookmarkBoundaryAt(i)))
                    hyperlink.Add(BuildRun(runs[i++], drawings, hyperlinks, preservedNumbering, restartOverrides));
                Content(head, hyperlink);
            }
            else if (anchor is { Length: > 0 })
            {
                var hyperlink = new XElement(W + "hyperlink", new XAttribute(W + "anchor", anchor));
                if (tooltip is { Length: > 0 })
                    hyperlink.Add(new XAttribute(W + "tooltip", tooltip));
                var head = runs[i];
                var firstHyperlinkRun = i;
                while (i < runs.Count && runs[i].HyperlinkAnchor == anchor && runs[i].HyperlinkTooltip == tooltip && (runs[i].IsCommentReference ? null : runs[i].CommentId) == openCommentId && SameRevision(head, runs[i])
                    && (i == firstHyperlinkRun || !HasParagraphBookmarkBoundaryAt(i)))
                    hyperlink.Add(BuildRun(runs[i++], drawings, hyperlinks, preservedNumbering, restartOverrides));
                Content(head, hyperlink);
            }
            else
            {
                var run = runs[i++];
                Content(run, BuildRun(run, drawings, hyperlinks, preservedNumbering, restartOverrides));
            }
        }

        EmitBookmarkBoundariesAt(runs.Count, p, ownerControl: null);

        // Close any still-open revision wrapper, then any still-open comment range, at paragraph end.
        FlushRevision();
        if (openCommentId is { } trailing)
            p.Add(new XElement(W + "commentRangeEnd", new XAttribute(W + "id", trailing)));

        foreach (var bId in wholeParagraphBookmarkIds)
            p.Add(new XElement(W + "bookmarkEnd", new XAttribute(W + "id", bId)));

        return p;
    }

    private static XElement? BuildParagraphProperties(
        Paragraph paragraph,
        IReadOnlyDictionary<Section, IReadOnlyList<HeaderFooterPart>>? partsBySection = null,
        PreservedNumberingPlan? preservedNumbering = null,
        IReadOnlyDictionary<(ListKind Kind, int Level, int StartAt), int>? restartOverrides = null,
        IdAllocator? ids = null)
    {
        var pPr = new XElement(W + "pPr");
        if (!string.IsNullOrEmpty(paragraph.StyleId))
            pPr.Add(new XElement(W + "pStyle", new XAttribute(W + "val", paragraph.StyleId)));

        var f = paragraph.Formatting;
        // Children MUST follow the CT_PPr schema sequence, otherwise Word's strict validator
        // rejects the paragraph. The relevant slots, in order (subset emitted here), are:
        //   pStyle, keepNext, keepLines, pageBreakBefore, framePr, widowControl, numPr,
        //   suppressLineNumbers, pBdr, shd, tabs, suppressAutoHyphens, kinsoku, wordWrap,
        //   overflowPunct, topLinePunct, autoSpaceDE, autoSpaceDN, bidi, adjustRightInd,
        //   snapToGrid, spacing, ind, contextualSpacing, mirrorIndents, suppressOverlap,
        //   jc, textDirection, textAlignment, textboxTightWrap, outlineLvl, divId, cnfStyle,
        //   rPr, sectPr, pPrChange.
        // w:pPrChange is always the LAST child of w:pPr (after sectPr), carrying the paragraph's
        // previous formatting snapshot when the paragraph's properties were changed under Track Changes.

        // Flow control toggles: keepNext, keepLines, pageBreakBefore, widowControl.
        if (f.KeepWithNext)
            pPr.Add(new XElement(W + "keepNext"));
        if (f.KeepLinesTogether)
            pPr.Add(new XElement(W + "keepLines"));
        // Force a page break before this paragraph (w:pageBreakBefore); Word honours it when paginating.
        if (f.PageBreakBefore)
            pPr.Add(new XElement(W + "pageBreakBefore"));
        // Word persists drop caps as paragraph frame properties (w:framePr), after pageBreakBefore
        // and before widowControl in CT_PPr order. Size still lives on the leading run's rPr.
        if (paragraph.DropCap is { } dropCap)
            pPr.Add(BuildDropCapFrameProperties(dropCap));
        // Widow/orphan control: preserve explicit off independently from Word's omitted/default-on form.
        if (f.WidowControl)
            pPr.Add(new XElement(W + "widowControl"));
        else if (f.WidowControlIsSet)
            pPr.Add(new XElement(W + "widowControl", new XAttribute(W + "val", "0")));
        // numPr — list numbering (CT_PPrBase order: after widowControl, before suppressLineNumbers).
        if (f.ListKind != ListKind.None)
        {
            var baseNumId = f.ListKind switch
            {
                ListKind.Number => NumberNumId,
                ListKind.MultiLevel => MultiLevelNumId,
                _ => BulletNumId
            };
            var level = Math.Clamp(f.ListLevel, 0, ListLevelCount - 1);
            // When the paragraph carries a list restart override (ListStartOverride != null), look up the
            // dedicated override w:num emitted by BuildNumbering; fall back to the base numId when the map
            // does not contain this combination (e.g. bullets where override is ignored).
            var numId = baseNumId;
            if (f.ListStartOverride.HasValue
                && restartOverrides is not null
                && restartOverrides.TryGetValue((f.ListKind, level, f.ListStartOverride.Value), out var overrideId))
                numId = overrideId;
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
        // An explicit off token is significant: it cancels a suppressing paragraph style.
        if (f.SuppressLineNumbers)
            pPr.Add(new XElement(W + "suppressLineNumbers"));
        else if (f.SuppressLineNumbersIsSet)
            pPr.Add(new XElement(W + "suppressLineNumbers", new XAttribute(W + "val", "0")));
        // Paragraph border (w:pBdr) — CT_PPrBase order: after numPr, before shd.
        // A box whose drawn edges are selected by the per-edge flags (all four = a box) with one shared
        // colour/width/line-style, analogous to w:tblBorders. A horizontal rule is the bottom-only case.
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
        // Paragraph shading (background fill) — CT_PPrBase order: after pBdr, before tabs.
        if (f.ShadingColorHex is { Length: > 0 } shading)
            pPr.Add(new XElement(W + "shd",
                new XAttribute(W + "val", ShadingPatterns.ToToken(f.ShadingPattern)),
                new XAttribute(W + "color", "auto"),
                new XAttribute(W + "fill", shading.TrimStart('#'))));
        // Tab stops (w:tabs) — CT_PPrBase order: after shd, before suppressAutoHyphens.
        if (f.TabStops.Count > 0)
            pPr.Add(new XElement(W + "tabs",
                f.TabStops.Select(BuildTabStop)));
        // Suppress automatic hyphenation for this paragraph (w:suppressAutoHyphens) — after w:tabs.
        if (f.SuppressAutoHyphens)
            pPr.Add(new XElement(W + "suppressAutoHyphens"));
        // Right-to-left paragraph direction (w:bidi) — CT_PPrBase order: after suppressAutoHyphens
        // (and several non-modelled toggles), before spacing/ind.
        if (f.Rtl)
            pPr.Add(new XElement(W + "bidi"));
        // w:spacing carries before/after AND line spacing — CT_PPrBase order: after bidi, before ind.
        // Line spacing is emitted only when it differs from the model default (a multiple of 1.15), so
        // paragraphs with inherited/default spacing stay byte-unchanged.
        var hasLineSpacing = f.LineRule != LineSpacingRule.Multiple
            || System.Math.Abs(f.LineSpacing - ParagraphFormatting.Default.LineSpacing) > 0.0001;
        if (f.SpaceBeforePt > 0 || f.SpaceAfterPt > 0 || f.BeforeAutoSpacing || f.AfterAutoSpacing || hasLineSpacing)
        {
            var spacingEl = new XElement(W + "spacing");
            var hasNumericSpacing = !f.BeforeAutoSpacing && f.SpaceBeforePt > 0
                || !f.AfterAutoSpacing && f.SpaceAfterPt > 0;
            if (f.BeforeAutoSpacing)
                spacingEl.Add(new XAttribute(W + "beforeAutospacing", "1"));
            else if (hasNumericSpacing)
                spacingEl.Add(new XAttribute(W + "before", PointsToDxa(f.SpaceBeforePt)));
            if (f.AfterAutoSpacing)
                spacingEl.Add(new XAttribute(W + "afterAutospacing", "1"));
            else if (hasNumericSpacing)
                spacingEl.Add(new XAttribute(W + "after", PointsToDxa(f.SpaceAfterPt)));
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
        // w:ind (indents) — CT_PPrBase order: after spacing, before contextualSpacing/jc.
        // Negative FirstLineIndentPt models a hanging indent; emit w:hanging (unsigned) in that case.
        // w:hanging and w:firstLine are mutually exclusive in OOXML; w:firstLine is unsigned, so a
        // negative value (as Word would see it) is clamped/ignored — we must use w:hanging instead.
        if (f.IndentLeftPt > 0 || f.IndentRightPt > 0 || f.FirstLineIndentPt != 0)
        {
            var indEl = new XElement(W + "ind",
                new XAttribute(W + "left", PointsToDxa(f.IndentLeftPt)),
                new XAttribute(W + "right", PointsToDxa(f.IndentRightPt)));
            if (f.FirstLineIndentPt < 0)
                indEl.Add(new XAttribute(W + "hanging", PointsToDxa(-f.FirstLineIndentPt)));
            else if (f.FirstLineIndentPt > 0)
                indEl.Add(new XAttribute(W + "firstLine", PointsToDxa(f.FirstLineIndentPt)));
            pPr.Add(indEl);
        }
        // w:jc (alignment) — CT_PPrBase order: after ind, before textDirection.
        if (f.ContextualSpacing is { } contextualSpacing)
            pPr.Add(new XElement(W + "contextualSpacing",
                contextualSpacing ? null : new XAttribute(W + "val", "0")));
        if (f.Alignment != TextAlignment.Left)
            pPr.Add(new XElement(W + "jc", new XAttribute(W + "val", f.Alignment switch
            {
                TextAlignment.Center => "center",
                TextAlignment.Right => "right",
                TextAlignment.Justify => "both",
                _ => "left"
            })));

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

        // w:pPrChange (tracked paragraph-formatting change) — LAST child of w:pPr, after sectPr.
        // When the paragraph's properties were changed under Track Changes, emit the change marker with a
        // unique w:id, author/date, and a nested w:pPr holding the previous (pre-change) formatting.
        // The nested w:pPr is built via BuildStyleParagraphProperties (the common subset: alignment,
        // indents, spacing) because the previous snapshot is a formatting-only pPr, not a full paragraph.
        // An empty nested w:pPr is always emitted (signals "previous default formatting"), mirroring how
        // w:rPrChange always carries a nested w:rPr even when empty.
        if (paragraph.ParagraphFormatRevision is { } pPrRevision && ids is not null)
            pPr.Add(BuildPPrChange(pPrRevision, ids));

        return pPr.HasElements ? pPr : null;
    }

    private static XElement BuildDropCapFrameProperties(DropCapLayoutIntent dropCap) =>
        new(W + "framePr",
            new XAttribute(W + "dropCap", dropCap.Position == DropCapPosition.InMargin ? "margin" : "drop"),
            new XAttribute(W + "lines", Math.Max(1, dropCap.LineSpan)),
            dropCap.DistanceFromTextPt > 0
                ? new XAttribute(W + "hSpace", PointsToDxa(dropCap.DistanceFromTextPt))
                : null);

    /// <summary>
    /// Builds one <c>w:tab</c> for a paragraph tab stop: alignment in <c>w:val</c>, position in
    /// <c>w:pos</c> (dxa), and an optional <c>w:leader</c> fill emitted only when the stop carries
    /// one (so leaderless stops round-trip byte-for-byte as before). Clear operations emit only
    /// <c>w:val="clear"</c> plus their position.
    /// </summary>
    private static XElement BuildTabStop(TabStop stop)
    {
        var tab = new XElement(W + "tab",
            new XAttribute(W + "val", stop.IsClear ? "clear" : stop.Alignment switch
            {
                TabStopAlignment.Center => "center",
                TabStopAlignment.Right => "right",
                TabStopAlignment.Decimal => "decimal",
                _ => "left"
            }),
            new XAttribute(W + "pos", PointsToDxa(stop.PositionPt)));
        if (!stop.IsClear && stop.Leader != TabLeader.None)
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
        RunFieldKind.PageNumber  => " PAGE ",
        RunFieldKind.Date        => " DATE ",
        RunFieldKind.Time        => " TIME ",
        RunFieldKind.FileName    => " FILENAME ",
        RunFieldKind.Author      => " AUTHOR ",
        RunFieldKind.NumPages    => " NUMPAGES ",
        RunFieldKind.Title       => " TITLE ",
        RunFieldKind.Subject     => " SUBJECT ",
        RunFieldKind.Keywords    => " KEYWORDS ",
        RunFieldKind.DocComments => " COMMENTS ",
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

    /// <summary>
    /// Builds the <c>w:fldSimple/@w:instr</c> for a cross-reference field: the keyword
    /// (<c>REF</c>/<c>PAGEREF</c>/<c>NOTEREF</c>) and target (a bookmark name or note id), plus the
    /// "insert reference to" switch (<c>\w</c> heading number, <c>\n</c> paragraph number, <c>\p</c>
    /// above/below) and a trailing <c>\h</c> when the reference is a hyperlink — e.g.
    /// <c> REF _Ref1 \w \h </c>. The surrounding spaces match how Word writes field instructions.
    /// </summary>
    private static string CrossReferenceInstruction(CrossReferenceField field)
    {
        var keyword = field.Kind switch
        {
            CrossRefFieldKind.PageRef => "PAGEREF",
            CrossRefFieldKind.NoteRef => "NOTEREF",
            _ => "REF"
        };
        var builder = new System.Text.StringBuilder(" ");
        builder.Append(keyword).Append(' ').Append(field.Target);
        switch (field.InsertAs)
        {
            case CrossRefInsertAs.HeadingNumber:
                builder.Append(" \\w");
                break;
            case CrossRefInsertAs.ParagraphNumber:
                builder.Append(" \\n");
                break;
            case CrossRefInsertAs.AboveBelow:
                builder.Append(" \\p");
                break;
        }
        if (field.Hyperlink)
            builder.Append(" \\h");
        builder.Append(' ');
        return builder.ToString();
    }

    private static XElement BuildRun(
        Run run,
        RunDrawings drawings,
        IReadOnlyDictionary<string, string> hyperlinks,
        PreservedNumberingPlan? preservedNumbering = null,
        IReadOnlyDictionary<(ListKind Kind, int Level, int StartAt), int>? restartOverrides = null)
    {
        if (run.SubDocument?.Target is { Length: > 0 } subDocumentTarget
            && drawings.SubDocuments?.TryGetValue(subDocumentTarget, out var subDocumentRelationshipId) == true)
        {
            return new XElement(W + "subDoc", new XAttribute(R + "id", subDocumentRelationshipId));
        }

        // An inline equation serialises as an m:oMath emitted in place of the run (a paragraph-level
        // sibling of w:r, never wrapped in one), carrying its math fragments as m:r/m:sSup/m:f.
        if (run.Equation is { } equation)
            return BuildOMath(equation);

        // A ruby annotation is a paragraph-level sibling of w:r. Its base text remains mirrored in Run.Text
        // for fallback consumers, but the emitted w:ruby preserves Word's phonetic-guide payload.
        if (run.Ruby is { } ruby)
            return BuildRubyRun(ruby);

        // An inline shape / text box serialises as a w:r wrapping a w:drawing/wp:inline/.../wps:wsp.
        if (run.Shape is { } shape)
        {
            var sr = new XElement(W + "r");
            var rPr = BuildRunProperties(run.Formatting);
            if (rPr is not null)
                sr.Add(rPr);
            sr.Add(BuildShapeDrawing(shape, drawings.Ids, hyperlinks, preservedNumbering, restartOverrides));
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

        // A cross-reference field (Word's References > Cross-reference) emits a w:fldSimple whose w:instr is
        // a REF/PAGEREF/NOTEREF instruction over a bookmark name or note id, with optional \w/\n/\p and \h
        // switches (e.g. " REF _Ref1 \h "), wrapping a run whose w:t is the cached resolved display text. The
        // reader recovers the field from the instruction and the cached text from the wrapped run.
        if (run.CrossReference is { } crossReference)
            return new XElement(W + "fldSimple",
                new XAttribute(W + "instr", CrossReferenceInstruction(crossReference)),
                BuildTextRun(run, drawings, hyperlinks, preservedNumbering, restartOverrides));

        // A table-cell formula field (Word's Table > Data > Formula) emits a w:fldSimple whose w:instr is
        // the formula plus an optional number-format switch (e.g. " =SUM(ABOVE) \# "#,##0.00" "), wrapping a
        // run whose w:t is the cached computed result. The reader recovers the formula + format from the
        // instruction and the cached result from the wrapped run.
        if (run.TableFormula is { } formula)
            return new XElement(W + "fldSimple",
                new XAttribute(W + "instr", TableFormulaInstruction(formula)),
                BuildTextRun(run, drawings, hyperlinks, preservedNumbering, restartOverrides));

        // A document field emits a self-contained w:fldSimple wrapping a run; the wrapped run's w:t
        // carries the last-known/cached value as fallback text for field-unaware consumers. The
        // w:instr keyword identifies the field kind (PAGE, DATE, TIME, FILENAME, AUTHOR, NUMPAGES).
        if (FieldInstruction(run.FieldKind) is { } instruction)
            return new XElement(W + "fldSimple",
                new XAttribute(W + "instr", instruction),
                BuildTextRun(run, drawings, hyperlinks, preservedNumbering, restartOverrides));

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

        // A manual column break serialises independently from a page break.
        if (run.IsColumnBreak)
        {
            var br = new XElement(W + "r");
            var brPr = BuildRunProperties(run.Formatting);
            if (brPr is not null)
                br.Add(brPr);
            br.Add(new XElement(W + "br", new XAttribute(W + "type", "column")));
            return br;
        }

        return BuildTextRun(run, drawings, hyperlinks, preservedNumbering, restartOverrides);
    }

    private static XElement BuildRubyRun(RubyAnnotation ruby)
    {
        var rubyPr = new XElement(W + "rubyPr");
        if (ruby.Alignment != RubyAlignment.Center)
            rubyPr.Add(new XElement(W + "rubyAlign", new XAttribute(W + "val", RubyAlignmentValue(ruby.Alignment))));
        if (ruby.PhoneticSizeHalfPoints is { } phoneticSize)
            rubyPr.Add(new XElement(W + "hps", new XAttribute(W + "val", phoneticSize)));
        if (ruby.RaiseHalfPoints is { } raise)
            rubyPr.Add(new XElement(W + "hpsRaise", new XAttribute(W + "val", raise)));

        var result = new XElement(W + "ruby");
        if (rubyPr.HasElements)
            result.Add(rubyPr);
        result.Add(BuildRubyText("rt", ruby.PhoneticFragments));
        result.Add(BuildRubyText("rubyBase", ruby.BaseFragments));
        return result;
    }

    private static XElement BuildRubyText(string elementName, IReadOnlyList<RubyTextFragment> fragments)
    {
        var container = new XElement(W + elementName);
        foreach (var fragment in fragments)
        {
            var run = new XElement(W + "r");
            var properties = BuildRunProperties(fragment.Formatting);
            if (properties is not null)
                run.Add(properties);
            run.Add(new XElement(W + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), SanitizeXmlText(fragment.Text)));
            container.Add(run);
        }
        return container;
    }

    private static string RubyAlignmentValue(RubyAlignment alignment) => alignment switch
    {
        RubyAlignment.DistributeLetter => "distributeLetter",
        RubyAlignment.DistributeSpace => "distributeSpace",
        RubyAlignment.Left => "left",
        RubyAlignment.Right => "right",
        _ => "center"
    };

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

    private static XElement BuildTextRun(
        Run run,
        RunDrawings drawings,
        IReadOnlyDictionary<string, string> hyperlinks,
        PreservedNumberingPlan? preservedNumbering = null,
        IReadOnlyDictionary<(ListKind Kind, int Level, int StartAt), int>? restartOverrides = null)
    {
        var r = new XElement(W + "r");
        var rPr = BuildRunProperties(run.Formatting);
        // A tracked formatting change (w:rPrChange) is the LAST child of the run's run-properties and
        // carries a nested w:rPr of the run's *previous* formatting (what reject restores). When the run
        // has a format revision but no other run properties, an empty w:rPr must still be created to host
        // it (an rPr that is null/empty would otherwise be dropped).
        if (run.FormatRevision is { } formatRevision)
        {
            rPr ??= new XElement(W + "rPr");
            rPr.Add(BuildRprChange(formatRevision, drawings.Ids));
        }
        if (rPr is not null)
            r.Add(rPr);
        if (run.PreservedDrawing is { } preservedDrawing)
            r.Add(BuildPreservedDrawing(preservedDrawing, drawings.PreservedDrawingRelIds));
        else if (run.DrawingGroup is not null)
            r.Add(BuildDrawingGroupDrawing(run.DrawingGroup, drawings, hyperlinks, preservedNumbering, restartOverrides));
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
            r.Add(BuildRunTextContent(run.Text, textElement));
        }
        return r;
    }

    private static IEnumerable<XElement> BuildRunTextContent(string text, string textElement)
    {
        if (text.Length == 0)
        {
            yield return new XElement(
                W + textElement,
                new XAttribute(XNamespace.Xml + "space", "preserve"),
                string.Empty);
            yield break;
        }

        var start = 0;
        for (var index = 0; index <= text.Length; index++)
        {
            if (index < text.Length && text[index] != Hyphenator.SoftHyphen)
                continue;

            if (index > start)
                yield return new XElement(
                    W + textElement,
                    new XAttribute(XNamespace.Xml + "space", "preserve"),
                    SanitizeXmlText(text[start..index]));

            if (index < text.Length)
                yield return new XElement(W + "softHyphen");
            start = index + 1;
        }
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
            oMath.Add(BuildMathRun(run, depth: 0));
        return oMath;
    }

    /// <summary>
    /// Builds the OMML element for a single math fragment: m:sSup / m:sSub / m:sSubSup / m:f / m:rad /
    /// m:nary / m:acc / m:bar / m:d / m:m, or a plain m:r for text. Mirrors the reader (see
    /// <c>DocxReader.ReadOMath</c>).
    /// </summary>
    private static XElement BuildMathRun(MathRun run, int depth) => run.Kind switch
    {
        MathRunKind.Superscript => new XElement(M + "sSup",
            BuildMathSlot(M + "e", run.ScriptBaseEquation, run.Base, depth),
            BuildMathSlot(M + "sup", run.ScriptSupEquation, run.Sup, depth)),
        MathRunKind.Subscript => new XElement(M + "sSub",
            BuildMathSlot(M + "e", run.ScriptBaseEquation, run.Base, depth),
            BuildMathSlot(M + "sub", run.ScriptSubEquation, run.Sub, depth)),
        MathRunKind.SubSuperscript => new XElement(M + "sSubSup",
            BuildMathSlot(M + "e", run.ScriptBaseEquation, run.Base, depth),
            BuildMathSlot(M + "sub", run.ScriptSubEquation, run.Sub, depth),
            BuildMathSlot(M + "sup", run.ScriptSupEquation, run.Sup, depth)),
        MathRunKind.Fraction => BuildFraction(run, depth),
        MathRunKind.Radical => BuildRadical(run, depth),
        MathRunKind.NAry => BuildNAry(run, depth),
        MathRunKind.Accent => BuildAccent(run, depth),
        MathRunKind.Bar => BuildBar(run, depth),
        MathRunKind.Delimiter => BuildDelimiter(run, depth),
        MathRunKind.Matrix => BuildMatrix(run.Matrix, depth),
        MathRunKind.EquationArray => BuildEquationArray(run.Matrix, depth),
        MathRunKind.FunctionApply => BuildFunctionApply(run, depth),
        MathRunKind.GroupChar => BuildGroupChar(run, depth),
        _ => MathText(run.Text)
    };

    private static XElement BuildFraction(MathRun run, int depth) =>
        new(M + "f",
            BuildMathSlot(M + "num", run.NumeratorEquation, run.Numerator, depth),
            BuildMathSlot(M + "den", run.DenominatorEquation, run.Denominator, depth));

    private static XElement BuildMathSlot(XName slotName, Equation? equation, string fallback, int depth)
    {
        var slot = new XElement(slotName);
        if (equation is not null && depth < MathRun.MaxNestedEquationDepth)
        {
            foreach (var childRun in equation.Runs)
                slot.Add(BuildMathRun(childRun, depth + 1));
        }
        else
        {
            slot.Add(MathText(fallback));
        }

        return slot;
    }

    /// <summary>
    /// Builds a radical (m:rad). A square root sets m:radPr/m:degHide and emits an empty m:deg; an nth
    /// root carries the degree in m:deg. The radicand is the m:e element. The reader keys off m:degHide
    /// and the m:deg slot to recover <see cref="MathRun.Degree"/>.
    /// </summary>
    private static XElement BuildRadical(MathRun run, int depth)
    {
        var isSquare = run.DegreeEquation is null && string.IsNullOrEmpty(run.Degree);
        var deg = isSquare
            ? new XElement(M + "deg")
            : BuildMathSlot(M + "deg", run.DegreeEquation, run.Degree, depth);
        return new XElement(M + "rad",
            new XElement(M + "radPr",
                new XElement(M + "degHide", new XAttribute(M + "val", isSquare ? "1" : "0"))),
            deg,
            BuildMathSlot(M + "e", run.RadicandEquation, run.Base, depth));
    }

    /// <summary>
    /// Builds an n-ary operator (m:nary): m:naryPr carries the operator glyph (m:chr) plus subscript/
    /// superscript-limit visibility; m:sub / m:sup hold the limits and m:e the operand.
    /// </summary>
    private static XElement BuildNAry(MathRun run, int depth)
    {
        var pr = new XElement(M + "naryPr");
        if (!string.IsNullOrEmpty(run.Operator))
            pr.Add(new XElement(M + "chr", new XAttribute(M + "val", run.Operator)));
        pr.Add(new XElement(M + "subHide", new XAttribute(M + "val", IsMathSlotHidden(run.NAryLowerLimitEquation, run.Sub, depth) ? "1" : "0")));
        pr.Add(new XElement(M + "supHide", new XAttribute(M + "val", IsMathSlotHidden(run.NAryUpperLimitEquation, run.Sup, depth) ? "1" : "0")));
        return new XElement(M + "nary",
            pr,
            BuildMathSlot(M + "sub", run.NAryLowerLimitEquation, run.Sub, depth),
            BuildMathSlot(M + "sup", run.NAryUpperLimitEquation, run.Sup, depth),
            BuildMathSlot(M + "e", run.NAryOperandEquation, run.Base, depth));
    }

    private static bool IsMathSlotHidden(Equation? equation, string fallback, int depth) =>
        string.IsNullOrEmpty(equation is not null && depth < MathRun.MaxNestedEquationDepth
            ? equation.LinearText
            : fallback);

    /// <summary>
    /// Builds an accent (m:acc): m:accPr/m:chr carries the accent glyph (hat/bar/vec/dot/tilde); the
    /// accented base is the m:e element. The reader keys off m:accPr/m:chr to recover
    /// <see cref="MathRun.Accent"/>. Mirrors <c>DocxReader.ReadAccent</c>.
    /// </summary>
    private static XElement BuildAccent(MathRun run, int depth)
    {
        var pr = new XElement(M + "accPr");
        if (!string.IsNullOrEmpty(run.Accent))
            pr.Add(new XElement(M + "chr", new XAttribute(M + "val", run.Accent)));
        return new XElement(M + "acc",
            pr,
            BuildMathSlot(M + "e", run.DecoratorBaseEquation, run.Base, depth));
    }

    /// <summary>
    /// Builds a bar (m:bar): m:barPr/m:pos carries "top" (overbar) or "bot" (underbar); the barred base
    /// is the m:e element. Mirrors <c>DocxReader.ReadBar</c>.
    /// </summary>
    private static XElement BuildBar(MathRun run, int depth) =>
        new(M + "bar",
            new XElement(M + "barPr",
                new XElement(M + "pos", new XAttribute(M + "val", run.BarTop ? "top" : "bot"))),
            BuildMathSlot(M + "e", run.DecoratorBaseEquation, run.Base, depth));

    /// <summary>
    /// Builds a delimiter (m:d): m:dPr carries the begin/end glyphs (m:begChr / m:endChr); a single
    /// m:e holds the bracketed content.
    /// </summary>
    private static XElement BuildDelimiter(MathRun run, int depth) =>
        new(M + "d",
            new XElement(M + "dPr",
                new XElement(M + "begChr", new XAttribute(M + "val", run.OpenChar)),
                new XElement(M + "endChr", new XAttribute(M + "val", run.CloseChar))),
            BuildMathSlot(M + "e", run.DelimiterContentEquation, run.Base, depth));

    /// <summary>
    /// Builds a matrix (m:m): one m:mr per row, each holding one m:e (cell) per column. An absent/empty
    /// matrix degrades to an empty math run so nothing is lost.
    /// </summary>
    private static XElement BuildMatrix(MathMatrix? matrix, int depth)
    {
        var m = new XElement(M + "m");
        if (matrix is not null)
            for (var rowIndex = 0; rowIndex < matrix.RowCount; rowIndex++)
            {
                var mr = new XElement(M + "mr");
                var columnCount = matrix.Rows.Count > rowIndex
                    ? Math.Max(matrix.Rows[rowIndex].Count, matrix.CellEquations.Count > rowIndex ? matrix.CellEquations[rowIndex].Count : 0)
                    : matrix.CellEquations.Count > rowIndex ? matrix.CellEquations[rowIndex].Count : 0;
                for (var columnIndex = 0; columnIndex < columnCount; columnIndex++)
                    mr.Add(BuildMathSlot(
                        M + "e",
                        matrix.CellEquationAt(rowIndex, columnIndex),
                        matrix.CellTextAt(rowIndex, columnIndex),
                        depth));
                m.Add(mr);
            }
        return m;
    }

    /// <summary>
    /// Builds an equation array (m:eqArr): one m:e per row/cell, preserving nested child math runs.
    /// </summary>
    private static XElement BuildEquationArray(MathMatrix? array, int depth)
    {
        var eqArr = new XElement(M + "eqArr");
        if (array is not null)
            for (var rowIndex = 0; rowIndex < array.RowCount; rowIndex++)
            {
                var columnCount = array.Rows.Count > rowIndex
                    ? Math.Max(array.Rows[rowIndex].Count, array.CellEquations.Count > rowIndex ? array.CellEquations[rowIndex].Count : 0)
                    : array.CellEquations.Count > rowIndex ? array.CellEquations[rowIndex].Count : 0;
                for (var columnIndex = 0; columnIndex < columnCount; columnIndex++)
                    eqArr.Add(BuildMathSlot(
                        M + "e",
                        array.CellEquationAt(rowIndex, columnIndex),
                        array.CellTextAt(rowIndex, columnIndex),
                        depth));
            }

        return eqArr;
    }

    /// <summary>
    /// Builds a function-apply element (m:func): m:fName holds a plain text run with the function name
    /// and m:e holds the argument. Mirrors <c>DocxReader.ReadFunctionApply</c>.
    /// </summary>
    private static XElement BuildFunctionApply(MathRun run, int depth) =>
        new(M + "func",
            new XElement(M + "fName", MathText(run.FuncName)),
            BuildMathSlot(M + "e", run.FunctionArgumentEquation, run.Base, depth));

    /// <summary>
    /// Builds a group-character element (m:groupChr): m:groupChrPr carries the spanning glyph
    /// (m:chr/@m:val) and its position (m:pos/@m:val, "top" or "bot"); m:e holds the base.
    /// Mirrors <c>DocxReader.ReadGroupChar</c>.
    /// </summary>
    private static XElement BuildGroupChar(MathRun run, int depth) =>
        new(M + "groupChr",
            new XElement(M + "groupChrPr",
                new XElement(M + "chr", new XAttribute(M + "val", run.GroupChr)),
                new XElement(M + "pos", new XAttribute(M + "val", run.GroupChrPos))),
            BuildMathSlot(M + "e", run.DecoratorBaseEquation, run.Base, depth));

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
    /// path. wp:wrapTight includes the wrapPolygon Word requires to open the document without repair.
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
                new XAttribute("relativeHeight", image.ZOrderIndex),
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
                BuildWrap(image.Wrapping, image.WrapTextSide),
                BuildDocPr(part),
                BuildPicGraphic(part, cx, cy)));
    }

    /// <summary>
    /// Shared helper: wraps <paramref name="graphic"/> in a <c>w:drawing/wp:anchor</c> container, using
    /// the floating-position state from <paramref name="placement"/>. Called by BuildShapeDrawing,
    /// BuildWordArtDrawing, BuildChartDrawing and BuildSmartArtDrawing when their object is floating.
    /// The <c>wp:anchor</c> attributes and position children mirror <see cref="BuildAnchorDrawing"/> exactly.
    /// </summary>
    private static XElement BuildAnchorContainer(
        long cx, long cy,
        XElement docPr,
        XElement graphic,
        FloatingPlacement placement)
    {
        var behindDoc = placement.Wrapping == ImageWrapping.Behind ? 1 : 0;
        return new XElement(W + "drawing",
            new XElement(Wp + "anchor",
                new XAttribute(XNamespace.Xmlns + "wp", Wp.NamespaceName),
                new XAttribute("distT", 0), new XAttribute("distB", 0),
                new XAttribute("distL", 0), new XAttribute("distR", 0),
                new XAttribute("simplePos", 0),
                new XAttribute("relativeHeight", placement.ZOrderIndex),
                new XAttribute("behindDoc", behindDoc),
                new XAttribute("locked", 0),
                new XAttribute("layoutInCell", 1),
                new XAttribute("allowOverlap", 1),
                new XElement(Wp + "simplePos", new XAttribute("x", 0), new XAttribute("y", 0)),
                new XElement(Wp + "positionH",
                    new XAttribute("relativeFrom", HorizontalAnchorToken(placement.HorizontalAnchor)),
                    new XElement(Wp + "posOffset", PointsToEmu(placement.HorizontalOffsetPt))),
                new XElement(Wp + "positionV",
                    new XAttribute("relativeFrom", VerticalAnchorToken(placement.VerticalAnchor)),
                    new XElement(Wp + "posOffset", PointsToEmu(placement.VerticalOffsetPt))),
                new XElement(Wp + "extent", new XAttribute("cx", cx), new XAttribute("cy", cy)),
                new XElement(Wp + "effectExtent",
                    new XAttribute("l", 0), new XAttribute("t", 0),
                    new XAttribute("r", 0), new XAttribute("b", 0)),
                BuildWrap(placement.Wrapping, placement.WrapTextSide),
                docPr,
                graphic));
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
    /// The single wrap element for a floating wrapping mode: wp:wrapSquare (square), wp:wrapTight (tight
    /// with a rectangular wrap polygon), wp:wrapTopAndBottom, or wp:wrapNone for the front/behind modes.
    /// </summary>
    private static XElement BuildWrap(ImageWrapping wrapping, FloatingWrapTextSide wrapTextSide) => wrapping switch
    {
        ImageWrapping.Square => new XElement(Wp + "wrapSquare", new XAttribute("wrapText", WrapTextSideToken(wrapTextSide))),
        ImageWrapping.Tight => new XElement(
            Wp + "wrapTight",
            new XAttribute("wrapText", WrapTextSideToken(wrapTextSide)),
            BuildRectangularWrapPolygon()),
        ImageWrapping.TopAndBottom => new XElement(Wp + "wrapTopAndBottom"),
        _ => new XElement(Wp + "wrapNone"), // Behind / InFront both wrap none (distinguished by @behindDoc).
    };

    private static string WrapTextSideToken(FloatingWrapTextSide side) => side switch
    {
        FloatingWrapTextSide.Left => "left",
        FloatingWrapTextSide.Right => "right",
        FloatingWrapTextSide.Largest => "largest",
        _ => "bothSides"
    };

    private static XElement BuildRectangularWrapPolygon() =>
        new(
            Wp + "wrapPolygon",
            new XAttribute("edited", "0"),
            new XElement(Wp + "start", new XAttribute("x", "0"), new XAttribute("y", "0")),
            new XElement(Wp + "lineTo", new XAttribute("x", "0"), new XAttribute("y", "21600")),
            new XElement(Wp + "lineTo", new XAttribute("x", "21600"), new XAttribute("y", "21600")),
            new XElement(Wp + "lineTo", new XAttribute("x", "21600"), new XAttribute("y", "0")),
            new XElement(Wp + "lineTo", new XAttribute("x", "0"), new XAttribute("y", "0")));

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
    /// drawing paths so the picture markup is not duplicated. Emits rotation/flip (<c>a:xfrm</c>
    /// attributes), crop (<c>a:srcRect</c>), and picture border (<c>a:ln</c>) when those fields are set.
    /// </summary>
    private static XElement BuildPicGraphic(ImagePart part, long cx, long cy)
    {
        var image = part.Image;

        // a:xfrm: always present; carry @rot/@flipH/@flipV only when non-default.
        var xfrm = new XElement(A + "xfrm");
        if (image.RotationAngle != 0)
            xfrm.Add(new XAttribute("rot", (long)Math.Round(image.RotationAngle * 60000)));
        if (image.FlipH)
            xfrm.Add(new XAttribute("flipH", 1));
        if (image.FlipV)
            xfrm.Add(new XAttribute("flipV", 1));
        xfrm.Add(new XElement(A + "off", new XAttribute("x", 0), new XAttribute("y", 0)));
        xfrm.Add(new XElement(A + "ext", new XAttribute("cx", cx), new XAttribute("cy", cy)));

        // a:blipFill: blip (with optional lum/satMod/alphaModFix/grayscl/duotone children) + stretch/fillRect.
        // Brightness/contrast emit as a:lum @bright / @contrast (per-mille: value × 1000).
        // Saturation emits as a:satMod @val (per-mille: satPct × 1000; 100 % = 100000 = omitted).
        // Transparency emits as a:alphaModFix @amt (per-mille of opacity: (100-transPct) × 1000).
        // Recolor:
        //   Grayscale  → a:grayscl (empty element)
        //   Sepia      → a:duotone with fixed brown (#7B4012) + white tones
        //   Washout    → a:lum @bright=40000 + a:alphaModFix @amt=50000
        //   BlackWhite → a:grayscl + a:lum @contrast=100000
        //   ColorTemperature → freew:colorTemp attribute on a:blip (extension)
        var FreeWExt = XNamespace.Get("http://schemas.freew.app/2024/ext");
        var blip = new XElement(A + "blip");
        if (part.HasEmbeddedPayload)
            blip.Add(new XAttribute(R + "embed", part.RelationshipId));
        if (part.LinkRelationshipId is { } linkRelationshipId)
            blip.Add(new XAttribute(R + "link", linkRelationshipId));

        // Recolor — emitted before other blip children so processors see it first.
        switch (image.RecolorMode)
        {
            case ImageRecolorMode.Grayscale:
                blip.Add(new XElement(A + "grayscl"));
                break;
            case ImageRecolorMode.Sepia:
                // a:duotone with dark brown (#7B4012) and near-white (#FDF0E0) fixed tones.
                blip.Add(new XElement(A + "duotone",
                    new XElement(A + "srgbClr", new XAttribute("val", "7B4012")),
                    new XElement(A + "srgbClr", new XAttribute("val", "FDF0E0"))));
                break;
            case ImageRecolorMode.Washout:
                // Washout: high brightness + semi-transparency. Combine with existing adjustments below.
                blip.Add(new XElement(A + "lum",
                    new XAttribute("bright", 40000 + (long)Math.Round(image.BrightnessPct * 1000)),
                    new XAttribute("contrast", (long)Math.Round(image.ContrastPct * 1000))));
                blip.Add(new XElement(A + "alphaModFix", new XAttribute("amt", 50000)));
                break;
            case ImageRecolorMode.BlackWhite:
                blip.Add(new XElement(A + "grayscl"));
                blip.Add(new XElement(A + "lum",
                    new XAttribute("bright", (long)Math.Round(image.BrightnessPct * 1000)),
                    new XAttribute("contrast", 100000 + (long)Math.Round(image.ContrastPct * 1000))));
                break;
        }

        var colorTemperature = image.ColorTemperature != 0 && image.RecolorMode == ImageRecolorMode.None
            ? (long?)Math.Round(image.ColorTemperature * 1000)
            : null;
        var artisticEffect = image.ArtisticEffect != ImageArtisticEffect.None
            ? (int?)image.ArtisticEffect
            : null;

        // Standard blip adjustments — omitted when Washout/BlackWhite recolor has already emitted lum.
        if (image.RecolorMode is not (ImageRecolorMode.Washout or ImageRecolorMode.BlackWhite))
        {
            if (image.BrightnessPct != 0 || image.ContrastPct != 0)
            {
                blip.Add(new XElement(A + "lum",
                    new XAttribute("bright", (long)Math.Round(image.BrightnessPct * 1000)),
                    new XAttribute("contrast", (long)Math.Round(image.ContrastPct * 1000))));
            }
        }
        if (image.SaturationPct != 100)
        {
            blip.Add(new XElement(A + "satMod",
                new XAttribute("val", (long)Math.Round(image.SaturationPct * 1000))));
        }
        if (image.TransparencyPct != 0 && image.RecolorMode != ImageRecolorMode.Washout)
        {
            // alphaModFix amt = opacity per-mille = (100 - transparencyPct) × 1000.
            var opacityPermille = (long)Math.Round((100 - image.TransparencyPct) * 1000);
            blip.Add(new XElement(A + "alphaModFix",
                new XAttribute("amt", opacityPermille)));
        }
        AddFreeWBlipExtensions(blip, colorTemperature, artisticEffect);
        var blipFill = new XElement(Pic + "blipFill",
            blip,
            new XElement(A + "stretch", new XElement(A + "fillRect")));
        if (image.HasCrop)
        {
            // DrawingML srcRect uses per-mille (×100000) integer percentages for each edge.
            static long ToPerMille(double fraction) => (long)Math.Round(fraction * 100000);
            blipFill.Add(new XElement(A + "srcRect",
                new XAttribute("l", ToPerMille(image.CropLeft)),
                new XAttribute("r", ToPerMille(image.CropRight)),
                new XAttribute("t", ToPerMille(image.CropTop)),
                new XAttribute("b", ToPerMille(image.CropBottom))));
        }

        // pic:spPr: xfrm + preset geometry + optional a:ln border + optional a:effectLst.
        var spPr = new XElement(Pic + "spPr",
            xfrm,
            new XElement(A + "prstGeom", new XAttribute("prst", "rect"), new XElement(A + "avLst")));
        if (image.HasBorder)
        {
            var widthEmu = (long)Math.Round(Math.Max(image.BorderWidthPt, 0.75) * 12700); // 1 pt = 12700 EMU
            var ln = new XElement(A + "ln", new XAttribute("w", widthEmu),
                new XElement(A + "solidFill",
                    new XElement(A + "srgbClr",
                        new XAttribute("val", image.BorderColorHex!.TrimStart('#').ToUpperInvariant()))));
            var dash = string.IsNullOrEmpty(image.BorderDash) ? "solid" : image.BorderDash;
            ln.Add(new XElement(A + "prstDash", new XAttribute("val", dash)));
            spPr.Add(ln);
        }

        // a:effectLst: shadow / glow / reflection / softEdge / bevel (innerShdw approximation).
        // Emitted as direct child of pic:spPr per DrawingML spec (CT_ShapeProperties).
        if (image.ImportedEffects is { HasAny: true } importedEffects)
        {
            spPr.Add(BuildShapeEffects(importedEffects));
        }
        else if (image.HasEffects)
        {
            var effectLst = new XElement(A + "effectLst");

            // Shadow: outer shadow presets 1-5. EMU units for blurRad and dist.
            if (image.ShadowPreset > 0)
            {
                // Preset parameters: [blurRad(pt), dist(pt), dir(degrees), opacity(0-100)]
                var shadowParams = image.ShadowPreset switch
                {
                    1 => (blur: 4.0, dist: 3.0, dir: 315, opacity: 50),   // Offset Diagonal Bottom Right
                    2 => (blur: 6.0, dist: 5.0, dir: 315, opacity: 55),   // Offset Diagonal (medium)
                    3 => (blur: 8.0, dist: 7.0, dir: 315, opacity: 60),   // Perspective Diagonal (large)
                    4 => (blur: 4.0, dist: 4.0, dir: 270, opacity: 50),   // Offset Bottom
                    _ => (blur: 10.0, dist: 10.0, dir: 315, opacity: 65), // Large shadow
                };
                var outerShdw = new XElement(A + "outerShdw",
                    new XAttribute("blurRad", (long)(shadowParams.blur * 12700)),
                    new XAttribute("dist",    (long)(shadowParams.dist * 12700)),
                    new XAttribute("dir",     (long)(shadowParams.dir * 60000)), // degrees to 1/60000 deg
                    new XAttribute("algn",    "tl"),
                    new XAttribute("rotWithShape", 0),
                    new XElement(A + "srgbClr", new XAttribute("val", "000000"),
                        new XElement(A + "alpha", new XAttribute("val", (long)(shadowParams.opacity * 1000)))));
                effectLst.Add(outerShdw);
            }

            // Glow: color + radius in pts.
            if (image.GlowSizePt > 0)
            {
                var glowColor = !string.IsNullOrEmpty(image.GlowColorHex)
                    ? image.GlowColorHex.TrimStart('#').ToUpperInvariant()
                    : "4472C4"; // default blue accent
                var glow = new XElement(A + "glow",
                    new XAttribute("rad", (long)(image.GlowSizePt * 12700)),
                    new XElement(A + "srgbClr", new XAttribute("val", glowColor),
                        new XElement(A + "alpha", new XAttribute("val", 60000)))); // 60% opacity
                effectLst.Add(glow);
            }

            // Reflection presets 1-5.
            if (image.ReflectionPreset > 0)
            {
                // Presets: [blurRad(pt), stA(opacity%), endA(opacity%), dist(pt)]
                var refParams = image.ReflectionPreset switch
                {
                    1 => (blur: 0.5, stA: 50, endA: 0, dist: 0.0),   // Tight reflection, touching
                    2 => (blur: 0.5, stA: 50, endA: 0, dist: 4.0),   // Tight reflection, 4pt offset
                    3 => (blur: 0.5, stA: 50, endA: 0, dist: 8.0),   // Tight reflection, 8pt offset
                    4 => (blur: 0.5, stA: 100, endA: 0, dist: 0.0),  // Half reflection, touching
                    _ => (blur: 0.5, stA: 100, endA: 0, dist: 4.0),  // Half reflection, 4pt offset
                };
                var reflection = new XElement(A + "reflection",
                    new XAttribute("blurRad",  (long)(refParams.blur * 12700)),
                    new XAttribute("stA",      (long)(refParams.stA  * 1000)),
                    new XAttribute("stPos",    0),
                    new XAttribute("endA",     (long)(refParams.endA * 1000)),
                    new XAttribute("endPos",   100000),
                    new XAttribute("dist",     (long)(refParams.dist * 12700)),
                    new XAttribute("dir",      5400000), // 90 degrees (flip downward)
                    new XAttribute("fadeDir",  5400000),
                    new XAttribute("sx",       100000),
                    new XAttribute("sy",       -100000), // vertical flip
                    new XAttribute("kx",       0),
                    new XAttribute("ky",       0),
                    new XAttribute("algn",     "bl"),
                    new XAttribute("rotWithShape", 0));
                effectLst.Add(reflection);
            }

            // Soft edge: radius in pts.
            if (image.SoftEdgePt > 0)
            {
                effectLst.Add(new XElement(A + "softEdge",
                    new XAttribute("rad", (long)(image.SoftEdgePt * 12700))));
            }

            // Bevel: approximated as inner shadow with a distinguishing @dir encoding the preset.
            // dir values 0-3 represent bevel presets 1-4 (circle, relaxed, cross, cool slant).
            if (image.BevelPreset > 0)
            {
                // Encode bevel as innerShdw with distinguishing @dir: preset 1→0°, 2→90°, 3→180°, 4→270°.
                var bevelDir = (image.BevelPreset - 1) * 90 * 60000; // degrees to 1/60000 deg
                var bevelBlur = image.BevelPreset switch { 1 => 3.0, 2 => 5.0, 3 => 3.0, _ => 6.0 };
                effectLst.Add(new XElement(A + "innerShdw",
                    new XAttribute("blurRad", (long)(bevelBlur * 12700)),
                    new XAttribute("dist",    0),
                    new XAttribute("dir",     bevelDir),
                    new XElement(A + "srgbClr", new XAttribute("val", "FFFFFF"),
                        new XElement(A + "alpha", new XAttribute("val", 40000)))));
            }

            SortEffectList(effectLst);
            spPr.Add(effectLst);
        }

        return new XElement(A + "graphic",
            new XAttribute(XNamespace.Xmlns + "a", A.NamespaceName),
            new XElement(A + "graphicData",
                new XAttribute("uri", Pic.NamespaceName),
                new XElement(Pic + "pic",
                    new XAttribute(XNamespace.Xmlns + "pic", Pic.NamespaceName),
                    new XElement(Pic + "nvPicPr",
                        new XElement(Pic + "cNvPr", new XAttribute("id", (uint)part.DrawingId), new XAttribute("name", part.FileName)),
                        new XElement(Pic + "cNvPicPr")),
                    blipFill,
                    spPr)));
    }

    private static void AddFreeWBlipExtensions(XElement blip, long? colorTemperature, int? artisticEffect)
    {
        if (colorTemperature is null && artisticEffect is null)
            return;

        var ext = new XElement(A + "ext",
            new XAttribute("uri", "{FREEW-BLIP-EXT-2024}"));
        XNamespace freeWExt = "http://schemas.freew.app/2024/ext";
        if (colorTemperature is { } temp)
            ext.Add(new XElement(freeWExt + "colorTemp", new XAttribute("val", temp)));
        if (artisticEffect is { } effect)
            ext.Add(new XElement(freeWExt + "artisticEffect", new XAttribute("val", effect)));
        blip.Add(new XElement(A + "extLst", ext));
    }

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
    private static XElement BuildShapeDrawing(
        Shape shape,
        IdAllocator ids,
        IReadOnlyDictionary<string, string> hyperlinks,
        PreservedNumberingPlan? preservedNumbering = null,
        IReadOnlyDictionary<(ListKind Kind, int Level, int StartAt), int>? restartOverrides = null)
    {
        var cx = PointsToEmu(shape.WidthPt);
        var cy = PointsToEmu(shape.HeightPt);
        var docPrId = ids.NextShapeDrawingId();
        var name = $"{shape.Kind}{(uint)docPrId}";

        // wps:spPr: position/size (a:xfrm), geometry (custGeom or prstGeom), then fill, outline and effects.
        XElement geometryElement;
        if (shape.HasCustomGeometry)
        {
            var cg = shape.CustomGeometry!;
            // Build a:custGeom/a:pathLst/a:path with the freeform polygon segments.
            var path = new XElement(A + "path",
                new XAttribute("w", cg.Width),
                new XAttribute("h", cg.Height));
            foreach (var seg in cg.Segments)
            {
                switch (seg.Kind)
                {
                    case CustomSegmentKind.MoveTo when seg.Point is not null:
                        path.Add(new XElement(A + "moveTo",
                            new XElement(A + "pt",
                                new XAttribute("x", seg.Point.X),
                                new XAttribute("y", seg.Point.Y))));
                        break;
                    case CustomSegmentKind.LineTo when seg.Point is not null:
                        path.Add(new XElement(A + "lnTo",
                            new XElement(A + "pt",
                                new XAttribute("x", seg.Point.X),
                                new XAttribute("y", seg.Point.Y))));
                        break;
                    case CustomSegmentKind.CubicBezierTo when seg.Point is not null
                        && seg.ControlPoint1 is not null && seg.ControlPoint2 is not null:
                        path.Add(new XElement(A + "cubicBezTo",
                            new XElement(A + "pt",
                                new XAttribute("x", seg.ControlPoint1.X),
                                new XAttribute("y", seg.ControlPoint1.Y)),
                            new XElement(A + "pt",
                                new XAttribute("x", seg.ControlPoint2.X),
                                new XAttribute("y", seg.ControlPoint2.Y)),
                            new XElement(A + "pt",
                                new XAttribute("x", seg.Point.X),
                                new XAttribute("y", seg.Point.Y))));
                        break;
                    case CustomSegmentKind.Close:
                        path.Add(new XElement(A + "close"));
                        break;
                }
            }
            geometryElement = new XElement(A + "custGeom",
                new XElement(A + "avLst"),
                new XElement(A + "gdLst"),
                new XElement(A + "ahLst"),
                new XElement(A + "cxnLst"),
                new XElement(A + "rect",
                    new XAttribute("l", "0"), new XAttribute("t", "0"),
                    new XAttribute("r", cg.Width.ToString()), new XAttribute("b", cg.Height.ToString())),
                new XElement(A + "pathLst", path));
        }
        else
        {
            geometryElement = new XElement(A + "prstGeom",
                new XAttribute("prst", PresetGeometry(shape.Kind)),
                new XElement(A + "avLst"));
        }

        // a:xfrm: carry @rot/@flipH/@flipV only when non-default (mirrors picture xfrm handling).
        var shapeXfrm = new XElement(A + "xfrm");
        if (shape.RotationAngle != 0)
            shapeXfrm.Add(new XAttribute("rot", (long)Math.Round(shape.RotationAngle * 60000)));
        if (shape.FlipH)
            shapeXfrm.Add(new XAttribute("flipH", 1));
        if (shape.FlipV)
            shapeXfrm.Add(new XAttribute("flipV", 1));
        shapeXfrm.Add(new XElement(A + "off", new XAttribute("x", 0), new XAttribute("y", 0)));
        shapeXfrm.Add(new XElement(A + "ext", new XAttribute("cx", cx), new XAttribute("cy", cy)));

        var spPr = new XElement(Wps + "spPr",
            shapeXfrm,
            geometryElement);

        // Fill: extended fill takes priority over simple solid-colour FillColorHex.
        if (shape.ExtendedFill is { } extFill)
            spPr.Add(BuildShapeFillElement(extFill));
        else if (shape.FillColorHex is { Length: > 0 } fill)
            spPr.Add(new XElement(A + "solidFill",
                new XElement(A + "srgbClr", new XAttribute("val", fill.TrimStart('#')))));

        // Outline: a:ln carries the stroke width (in EMU) and, inside, a:solidFill + optional a:prstDash.
        if (shape.OutlineColorHex is { Length: > 0 } outlineColor)
        {
            var lnAttrs = new List<object>();
            if (shape.OutlineWidthPt > 0)
                lnAttrs.Add(new XAttribute("w", (long)(shape.OutlineWidthPt * 12700)));
            var ln = new XElement(A + "ln", lnAttrs);
            ln.Add(new XElement(A + "solidFill",
                new XElement(A + "srgbClr", new XAttribute("val", outlineColor.TrimStart('#')))));
            if (shape.OutlineDash is { Length: > 0 } dash)
                ln.Add(new XElement(A + "prstDash", new XAttribute("val", dash)));
            spPr.Add(ln);
        }

        // Effects: a:effectLst (shadow / glow / soft-edge / reflection) and a:sp3d (bevel).
        if (shape.Effects is { } fx)
            spPr.Add(BuildShapeEffects(fx));

        var wsp = new XElement(Wps + "wsp",
            new XElement(Wps + "cNvSpPr"),
            spPr);

        // A text box carries its body paragraphs in wps:txbx/w:txbxContent. They share the owning story's
        // hyperlinks, numbering plan, and IdAllocator so nested ids continue the same sequence.
        if (shape.HasText)
        {
            var txbxContent = new XElement(W + "txbxContent");
            var nested = RunDrawings.Empty() with { Ids = ids };
            foreach (var paragraph in shape.TextParagraphs)
                txbxContent.Add(BuildParagraph(paragraph, nested, hyperlinks, preservedNumbering: preservedNumbering, restartOverrides: restartOverrides));
            wsp.Add(new XElement(Wps + "txbx", txbxContent));
        }

        // wps:bodyPr: required by the schema; carries text-direction attributes for text-box shapes.
        var bodyPr = new XElement(Wps + "bodyPr");
        if (shape.HasText && shape.TextDirection != ShapeTextDirection.Horizontal)
        {
            // vert="eaVert" + rot: rot is in 1/60000-degree units (5400000 = 90°, -5400000 = 270°).
            bodyPr.Add(new XAttribute("vert", "eaVert"));
            var rot = shape.TextDirection == ShapeTextDirection.Rotate90 ? 5400000 : -5400000;
            bodyPr.Add(new XAttribute("rot", rot));
        }
        wsp.Add(bodyPr);

        // wp:docPr carries the accessibility description (@descr) when AltText is set.
        var docPr = new XElement(Wp + "docPr",
            new XAttribute("id", (uint)docPrId),
            new XAttribute("name", name));
        if (shape.AltText is { Length: > 0 } altText)
            docPr.Add(new XAttribute("descr", altText));

        var graphic = new XElement(A + "graphic",
            new XElement(A + "graphicData",
                new XAttribute("uri", Wps.NamespaceName),
                wsp));

        if (shape.IsFloating && shape.Placement is { } placement)
            return BuildAnchorContainer(cx, cy, docPr, graphic, placement);

        return new XElement(W + "drawing",
            new XElement(Wp + "inline",
                new XAttribute("distT", 0), new XAttribute("distB", 0),
                new XAttribute("distL", 0), new XAttribute("distR", 0),
                new XElement(Wp + "extent", new XAttribute("cx", cx), new XAttribute("cy", cy)),
                new XElement(Wp + "effectExtent",
                    new XAttribute("l", 0), new XAttribute("t", 0),
                    new XAttribute("r", 0), new XAttribute("b", 0)),
                docPr,
                graphic));
    }

    // ── Shape fill / effect helpers (W24) ────────────────────────────────────────────────────────

    /// <summary>
    /// Converts a <see cref="ShapeFill"/> to the appropriate DrawingML fill element
    /// (a:noFill / a:gradFill / a:pattFill). Solid fills are handled by the caller via FillColorHex.
    /// </summary>
    private static XElement BuildShapeFillElement(ShapeFill fill) => fill.Kind switch
    {
        ShapeFillKind.NoFill => new XElement(A + "noFill"),

        ShapeFillKind.Gradient => new XElement(A + "gradFill",
            new XElement(A + "gsLst",
                fill.GradientStops.Select(s =>
                    new XElement(A + "gs", new XAttribute("pos", s.Position),
                        new XElement(A + "srgbClr", new XAttribute("val", s.ColorHex.TrimStart('#')))))),
            new XElement(A + "lin",
                new XAttribute("ang", fill.GradientAngle),
                new XAttribute("scaled", 0))),

        ShapeFillKind.Pattern => BuildPatternFill(fill),

        _ => new XElement(A + "noFill"), // fallback
    };

    private static XElement BuildPatternFill(ShapeFill fill)
    {
        var el = new XElement(A + "pattFill");
        if (fill.PatternPreset is { Length: > 0 } prst)
            el.Add(new XAttribute("prst", prst));
        if (fill.PatternFgColorHex is { Length: > 0 } fg)
            el.Add(new XElement(A + "fgClr",
                new XElement(A + "srgbClr", new XAttribute("val", fg.TrimStart('#')))));
        if (fill.PatternBgColorHex is { Length: > 0 } bg)
            el.Add(new XElement(A + "bgClr",
                new XElement(A + "srgbClr", new XAttribute("val", bg.TrimStart('#')))));
        return el;
    }

    /// <summary>
    /// Emits a:effectLst (shadow / glow / soft-edge / reflection) and, when bevel is set, a:sp3d.
    /// Returns only the non-empty elements so callers can add them directly to spPr.
    /// </summary>
    private static IEnumerable<XElement> BuildShapeEffects(ShapeEffectLst fx)
    {
        var effectLst = new XElement(A + "effectLst");

        if (fx.HasGlow)
            effectLst.Add(new XElement(A + "glow", new XAttribute("rad", fx.GlowRad),
                new XElement(A + "srgbClr", new XAttribute("val", fx.GlowColorHex.TrimStart('#')),
                    new XElement(A + "alpha", new XAttribute("val", fx.GlowAlpha)))));

        if (fx.HasShadow)
            effectLst.Add(new XElement(A + "outerShdw",
                new XAttribute("blurRad", fx.ShadowBlurRad),
                new XAttribute("dist",    fx.ShadowDist),
                new XAttribute("dir",     fx.ShadowDir),
                new XElement(A + "srgbClr", new XAttribute("val", fx.ShadowColorHex.TrimStart('#')),
                    new XElement(A + "alpha", new XAttribute("val", fx.ShadowAlpha)))));

        if (fx.HasReflection)
            effectLst.Add(new XElement(A + "reflection",
                new XAttribute("blurRad", fx.ReflectionBlurRad),
                new XAttribute("stA", fx.ReflectionStartAlpha),
                new XAttribute("stPos", fx.ReflectionStartPosition),
                new XAttribute("endA", fx.ReflectionEndAlpha),
                new XAttribute("endPos", fx.ReflectionEndPosition),
                new XAttribute("dir", fx.ReflectionDir),
                new XAttribute("fadeDir", fx.ReflectionFadeDir),
                new XAttribute("sx", fx.ReflectionScaleX),
                new XAttribute("sy", fx.ReflectionScaleY),
                new XAttribute("kx", fx.ReflectionSkewX),
                new XAttribute("ky", fx.ReflectionSkewY),
                new XAttribute("algn", fx.ReflectionAlignment),
                new XAttribute("dist", fx.ReflectionDist),
                new XAttribute("rotWithShape", fx.ReflectionRotWithShape ? 1 : 0)));

        if (fx.HasSoftEdge)
            effectLst.Add(new XElement(A + "softEdge", new XAttribute("rad", fx.SoftEdgeRad)));

        if (effectLst.HasElements)
        {
            SortEffectList(effectLst);
            yield return effectLst;
        }

        if (fx.HasBevel)
            yield return new XElement(A + "sp3d",
                new XElement(A + "bevelT",
                    new XAttribute("w", fx.BevelW),
                    new XAttribute("h", fx.BevelH),
                    new XAttribute("prst", fx.BevelPresetType)));
    }

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
        // Preserve imported text-box geometry. New WordArt still falls back to a sensible extent based
        // on its font size and text length.
        var heightPt = wordArt.HeightPt is > 0 ? wordArt.HeightPt.Value : wordArt.FontSizePt * 1.6;
        var widthPt = wordArt.WidthPt is > 0 ? wordArt.WidthPt.Value : Math.Max(1, wordArt.Text.Length) * wordArt.FontSizePt * 0.62;
        var cx = PointsToEmu(widthPt);
        var cy = PointsToEmu(heightPt);
        var docPrId = ids.NextShapeDrawingId();
        var name = $"WordArt{(uint)docPrId}";

        // A plain text-box rect carries the WordArt; the decorative effects live on the run's a:rPr.
        var wordArtXfrm = new XElement(A + "xfrm",
                new XElement(A + "off", new XAttribute("x", 0), new XAttribute("y", 0)),
                new XElement(A + "ext", new XAttribute("cx", cx), new XAttribute("cy", cy)));
        if (wordArt.RotationAngle != 0)
            wordArtXfrm.Add(new XAttribute("rot", (long)Math.Round(wordArt.RotationAngle * 60000)));
        if (wordArt.FlipH)
            wordArtXfrm.Add(new XAttribute("flipH", "1"));
        if (wordArt.FlipV)
            wordArtXfrm.Add(new XAttribute("flipV", "1"));
        var spPr = new XElement(Wps + "spPr",
            wordArtXfrm,
            new XElement(A + "prstGeom", new XAttribute("prst", "rect"),
                new XElement(A + "avLst")));
        foreach (var effect in WordArtShapeProperties(wordArt.Style))
            spPr.Add(effect);

        // wps:bodyPr: required; carries the optional warp and explicit text-fit semantics.
        var wordArtBodyPr = new XElement(Wps + "bodyPr");
        if (wordArt.Warp != WordArtWarp.None)
            wordArtBodyPr.Add(new XElement(A + "prstTxWarp",
                new XAttribute("prst", WordArtWarpToken(wordArt.Warp)),
                new XElement(A + "avLst")));
        switch (wordArt.TextFitMode)
        {
            case WordArtTextFitMode.NoAutoFit:
                wordArtBodyPr.Add(new XElement(A + "noAutofit"));
                break;
            case WordArtTextFitMode.ShapeAutoFit:
                wordArtBodyPr.Add(new XElement(A + "spAutoFit"));
                break;
            case WordArtTextFitMode.NormalAutoFit:
                var normalAutoFit = new XElement(A + "normAutofit");
                if (wordArt.NormalAutoFitFontScale is { } fontScale)
                    normalAutoFit.Add(new XAttribute("fontScale", fontScale));
                if (wordArt.NormalAutoFitLineSpacingReduction is { } lineSpacingReduction)
                    normalAutoFit.Add(new XAttribute("lnSpcReduction", lineSpacingReduction));
                wordArtBodyPr.Add(normalAutoFit);
                break;
        }

        var wsp = new XElement(Wps + "wsp",
            new XElement(Wps + "cNvSpPr"),
            spPr,
            new XElement(Wps + "txbx",
                new XElement(W + "txbxContent", BuildWordArtParagraph(wordArt))),
            wordArtBodyPr);

        // wp:docPr carries the accessibility description (@descr) when AltText is set.
        var wordArtDocPr = new XElement(Wp + "docPr",
            new XAttribute("id", (uint)docPrId),
            new XAttribute("name", name));
        if (wordArt.AltText is { Length: > 0 } wordArtAltText)
            wordArtDocPr.Add(new XAttribute("descr", wordArtAltText));

        var wordArtGraphic = new XElement(A + "graphic",
            new XElement(A + "graphicData",
                new XAttribute("uri", Wps.NamespaceName),
                wsp));

        if (wordArt.IsFloating && wordArt.Placement is { } wordArtPlacement)
            return BuildAnchorContainer(cx, cy, wordArtDocPr, wordArtGraphic, wordArtPlacement);

        return new XElement(W + "drawing",
            new XElement(Wp + "inline",
                new XAttribute("distT", 0), new XAttribute("distB", 0),
                new XAttribute("distL", 0), new XAttribute("distR", 0),
                new XElement(Wp + "extent", new XAttribute("cx", cx), new XAttribute("cy", cy)),
                new XElement(Wp + "effectExtent",
                    new XAttribute("l", 0), new XAttribute("t", 0),
                    new XAttribute("r", 0), new XAttribute("b", 0)),
                wordArtDocPr,
                wordArtGraphic));
    }

    /// <summary>
    /// Builds the single w:p inside a WordArt text box
    /// <summary>
    /// Builds the single w:p inside a WordArt text box: a w:r whose w:rPr carries the font size (w:sz, in
    /// half-points), followed by the w:t text. The visual WordArt effects live on the surrounding wps:spPr;
    /// w:rPr cannot legally contain DrawingML fill/line/effect children in a DOCX Word can open.
    /// </summary>
    private static XElement BuildWordArtParagraph(WordArt wordArt)
    {
        var rPr = new XElement(W + "rPr");
        if (!string.IsNullOrWhiteSpace(wordArt.FontFamily))
        {
            rPr.Add(new XElement(W + "rFonts",
                new XAttribute(W + "ascii", wordArt.FontFamily),
                new XAttribute(W + "hAnsi", wordArt.FontFamily),
                new XAttribute(W + "cs", wordArt.FontFamily)));
        }
        if (wordArt.Bold)
            rPr.Add(new XElement(W + "b"));
        rPr.Add(
            new XElement(W + "sz", new XAttribute(W + "val", PointsToHalfPoints(wordArt.FontSizePt))),
            new XElement(W + "szCs", new XAttribute(W + "val", PointsToHalfPoints(wordArt.FontSizePt))));

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
    // Additional WordArt colour constants for the expanded style set.
    private const string WordArtGoldColor    = "C09000";  // gold / dark-yellow fill
    private const string WordArtWhiteColor   = "FFFFFF";  // white fill
    private const string WordArtOrangeColor  = "ED7D31";  // orange accent
    private const string WordArtDarkColor    = "242424";  // near-black
    private const string WordArtGlowBlue     = "2E75B6";  // glow blue
    private const string WordArtGlowGold     = "C09000";  // glow gold/amber

    private static IEnumerable<XElement> WordArtEffects(WordArtStyle style)
    {
        switch (style)
        {
            // ── Original four ─────────────────────────────────────────────────────────────────
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
                yield return OuterShadow(WordArtOutlineColor, 50800, 38100, 2700000, 40000);
                break;

            // ── Extended set ──────────────────────────────────────────────────────────────────
            case WordArtStyle.FillGold:
                // discriminator: gradFill with 2-stop gold gradient (start == gold, end == gold → solid-ish)
                yield return new XElement(A + "gradFill",
                    new XElement(A + "gsLst",
                        new XElement(A + "gs", new XAttribute("pos", 0),
                            new XElement(A + "srgbClr", new XAttribute("val", WordArtGoldColor))),
                        new XElement(A + "gs", new XAttribute("pos", 100000),
                            new XElement(A + "srgbClr", new XAttribute("val", "8B6200")))),
                    new XElement(A + "lin", new XAttribute("ang", 5400000), new XAttribute("scaled", 0)));
                break;

            case WordArtStyle.FillWhite:
                // discriminator: solidFill white + thin dark outline (ln w=9525 val=dark)
                yield return SolidFill(WordArtWhiteColor);
                yield return new XElement(A + "ln", new XAttribute("w", 9525),
                    SolidFill(WordArtDarkColor));
                yield return new XElement(A + "effectLst"); // present but empty → FillWhite marker
                break;

            case WordArtStyle.GradFillMulti:
                // discriminator: 3-stop gradient (orange→red→purple)
                yield return new XElement(A + "gradFill",
                    new XElement(A + "gsLst",
                        new XElement(A + "gs", new XAttribute("pos", 0),
                            new XElement(A + "srgbClr", new XAttribute("val", "FF6000"))),
                        new XElement(A + "gs", new XAttribute("pos", 50000),
                            new XElement(A + "srgbClr", new XAttribute("val", "C00000"))),
                        new XElement(A + "gs", new XAttribute("pos", 100000),
                            new XElement(A + "srgbClr", new XAttribute("val", "7030A0")))),
                    new XElement(A + "lin", new XAttribute("ang", 5400000), new XAttribute("scaled", 0)));
                break;

            case WordArtStyle.ChromeOne:
                // discriminator: noFill + dark thick outline (no solidFill before a:ln)
                yield return new XElement(A + "noFill");
                yield return new XElement(A + "ln", new XAttribute("w", 19050),
                    SolidFill(WordArtDarkColor));
                break;

            case WordArtStyle.ChromeTwo:
                // discriminator: white solidFill + coloured ln + outerShdw (triple combo)
                yield return SolidFill(WordArtWhiteColor);
                yield return new XElement(A + "ln", new XAttribute("w", 12700),
                    SolidFill(WordArtFillColor));
                yield return OuterShadow(WordArtFillColor, 38100, 25400, 2700000, 30000);
                break;

            case WordArtStyle.ShadowOrange:
                // discriminator: orange solidFill + outerShdw orange
                yield return SolidFill(WordArtOrangeColor);
                yield return OuterShadow(WordArtOrangeColor, 50800, 38100, 2700000, 50000);
                break;

            case WordArtStyle.GlowBlue:
                // discriminator: dark solidFill + glow blue (a:effectLst/a:glow)
                yield return SolidFill(WordArtDarkColor);
                yield return new XElement(A + "effectLst",
                    new XElement(A + "glow", new XAttribute("rad", 101600),
                        new XElement(A + "srgbClr", new XAttribute("val", WordArtGlowBlue),
                            new XElement(A + "alpha", new XAttribute("val", 60000)))));
                break;

            case WordArtStyle.GlowGold:
                // discriminator: dark solidFill + glow gold
                yield return SolidFill(WordArtDarkColor);
                yield return new XElement(A + "effectLst",
                    new XElement(A + "glow", new XAttribute("rad", 101600),
                        new XElement(A + "srgbClr", new XAttribute("val", WordArtGlowGold),
                            new XElement(A + "alpha", new XAttribute("val", 60000)))));
                break;

            case WordArtStyle.Reflection:
                // discriminator: blue solidFill + reflection (a:effectLst/a:reflection)
                yield return SolidFill(WordArtFillColor);
                yield return new XElement(A + "effectLst",
                    new XElement(A + "reflection",
                        new XAttribute("blurRad", 6350),
                        new XAttribute("stA", 55000),
                        new XAttribute("endA", 300),
                        new XAttribute("endPos", 90000),
                        new XAttribute("dir", 5400000),
                        new XAttribute("sy", -100000),
                        new XAttribute("algn", "bl"),
                        new XAttribute("rotWithShape", 0)));
                break;

            case WordArtStyle.Bevel:
                // discriminator: orange solidFill + sp3d bevel in rPr (using effectLst marker)
                yield return SolidFill(WordArtOrangeColor);
                yield return new XElement(A + "effectLst");      // marker
                yield return new XElement(A + "latin", new XAttribute("typeface", "+mj-lt"));
                // Bevel is on the textBody's a:sp3d; the reader uses glow-free effectLst + sp3d absence here
                // as discriminator. We emit sp3d on the wsp:spPr in BuildWordArtParagraph caller context;
                // for now emit a custom attribute that survives round-trip as a:sp3d child marker.
                yield return new XElement(A + "sp3d",
                    new XElement(A + "bevelT",
                        new XAttribute("w", 63500),
                        new XAttribute("h", 63500),
                        new XAttribute("prst", "circle")));
                break;

            case WordArtStyle.PatternFill:
                // discriminator: pattFill + dark outline
                yield return new XElement(A + "pattFill", new XAttribute("prst", "diagCross"),
                    new XElement(A + "fgClr",
                        new XElement(A + "srgbClr", new XAttribute("val", WordArtFillColor))),
                    new XElement(A + "bgClr",
                        new XElement(A + "srgbClr", new XAttribute("val", WordArtWhiteColor))));
                yield return new XElement(A + "ln", new XAttribute("w", 9525),
                    SolidFill(WordArtFillColor));
                break;

            default: // FillBlue
                yield return SolidFill(WordArtFillColor);
                break;
        }
    }

    private static IEnumerable<XElement> WordArtShapeProperties(WordArtStyle style)
    {
        foreach (var effect in WordArtEffects(style))
        {
            if (effect.Name == A + "latin")
                continue;
            if (effect.Name == A + "effectLst")
            {
                if (!effect.HasElements)
                    continue;
                SortEffectList(effect);
            }
            yield return effect;
        }
    }

    private static void SortEffectList(XElement effectLst)
    {
        var ordered = effectLst.Elements()
            .OrderBy(EffectListOrder)
            .Select(element => new XElement(element))
            .ToList();
        effectLst.RemoveNodes();
        effectLst.Add(ordered);
    }

    private static int EffectListOrder(XElement element)
    {
        if (element.Name == A + "blur") return 0;
        if (element.Name == A + "fillOverlay") return 1;
        if (element.Name == A + "glow") return 2;
        if (element.Name == A + "innerShdw") return 3;
        if (element.Name == A + "outerShdw") return 4;
        if (element.Name == A + "prstShdw") return 5;
        if (element.Name == A + "reflection") return 6;
        if (element.Name == A + "softEdge") return 7;
        return 100;
    }

    private static XElement OuterShadow(string colorHex, int blurRad, int dist, int dir, int alpha) =>
        new(A + "effectLst",
            new XElement(A + "outerShdw",
                new XAttribute("blurRad", blurRad),
                new XAttribute("dist",    dist),
                new XAttribute("dir",     dir),
                new XAttribute("algn",    "tl"),
                new XElement(A + "srgbClr", new XAttribute("val", colorHex.TrimStart('#')),
                    new XElement(A + "alpha", new XAttribute("val", alpha)))));

    /// <summary>Maps a <see cref="WordArtWarp"/> enum value to the DrawingML <c>a:prstTxWarp/@prst</c> token.</summary>
    private static string WordArtWarpToken(WordArtWarp warp) => warp switch
    {
        WordArtWarp.ArchUp        => "textArchUp",
        WordArtWarp.ArchDown      => "textArchDown",
        WordArtWarp.Circle        => "textCircle",
        WordArtWarp.Button        => "textButton",
        WordArtWarp.Wave1         => "textWave1",
        WordArtWarp.Wave2         => "textWave2",
        WordArtWarp.Inflate       => "textInflate",
        WordArtWarp.Deflate       => "textDeflate",
        WordArtWarp.InflateBottom => "textInflateBottom",
        WordArtWarp.ChevronUp     => "textChevron",
        WordArtWarp.ChevronDown   => "textChevronInverted",
        WordArtWarp.FadeRight     => "textFadeRight",
        WordArtWarp.FadeLeft      => "textFadeLeft",
        WordArtWarp.SlantUp       => "textSlantUp",
        WordArtWarp.SlantDown     => "textSlantDown",
        _                         => "textNoShape",
    };

    /// <summary>Parses a <c>a:prstTxWarp/@prst</c> token back to a <see cref="WordArtWarp"/> enum value.</summary>
    private static WordArtWarp WordArtWarpFromToken(string? token) => token switch
    {
        "textArchUp"         => WordArtWarp.ArchUp,
        "textArchDown"       => WordArtWarp.ArchDown,
        "textCircle"         => WordArtWarp.Circle,
        "textButton"         => WordArtWarp.Button,
        "textWave1"          => WordArtWarp.Wave1,
        "textWave2"          => WordArtWarp.Wave2,
        "textInflate"        => WordArtWarp.Inflate,
        "textDeflate"        => WordArtWarp.Deflate,
        "textInflateBottom"  => WordArtWarp.InflateBottom,
        "textChevron"        => WordArtWarp.ChevronUp,
        "textChevronInverted"=> WordArtWarp.ChevronDown,
        "textFadeRight"      => WordArtWarp.FadeRight,
        "textFadeLeft"       => WordArtWarp.FadeLeft,
        "textSlantUp"        => WordArtWarp.SlantUp,
        "textSlantDown"      => WordArtWarp.SlantDown,
        _                    => WordArtWarp.None,
    };

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

        var chartDocPr = new XElement(Wp + "docPr", new XAttribute("id", part.DrawingId), new XAttribute("name", name));
        var chartGraphic = BuildChartGraphic(part);

        if (part.Chart.IsFloating && part.Chart.Placement is { } chartPlacement)
            return BuildAnchorContainer(cx, cy, chartDocPr, chartGraphic, chartPlacement);

        return new XElement(W + "drawing",
            new XElement(Wp + "inline",
                new XAttribute(XNamespace.Xmlns + "wp", Wp.NamespaceName),
                new XAttribute("distT", 0), new XAttribute("distB", 0),
                new XAttribute("distL", 0), new XAttribute("distR", 0),
                new XElement(Wp + "extent", new XAttribute("cx", cx), new XAttribute("cy", cy)),
                new XElement(Wp + "effectExtent",
                    new XAttribute("l", 0), new XAttribute("t", 0),
                    new XAttribute("r", 0), new XAttribute("b", 0)),
                chartDocPr,
                chartGraphic));
    }

    private static XElement BuildChartGraphic(ChartPart part) =>
        new(A + "graphic",
            new XAttribute(XNamespace.Xmlns + "a", A.NamespaceName),
            new XElement(A + "graphicData",
                new XAttribute("uri", ChartGraphicDataUri),
                new XElement(C + "chart",
                    new XAttribute(XNamespace.Xmlns + "c", C.NamespaceName),
                    new XAttribute(R + "id", part.RelationshipId))));

    /// <summary>
    /// Re-emits a verbatim-preserved inline drawing (an unmodelled chart/chartex <c>w:drawing</c> captured on
    /// read): the stored XML is reparsed and each Office relationship attribute that used the source id
    /// (for example <c>r:id</c>, <c>r:embed</c>, or SmartArt's <c>r:dm</c>/<c>r:lo</c>) is re-pointed at
    /// the document relationship the writer freshly assigned to the preserved part (<paramref name="preservedDrawingRelIds"/>: preserved part name →
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
                foreach (var attribute in element.Attributes().Where(attribute =>
                             attribute.Name.Namespace == R && attribute.Value == reference.OriginalRelId))
                    attribute.Value = newRelId;
        }

        return drawing;
    }

    /// <summary>
    /// Builds a classic embedded or linked OLE object as a <c>w:object</c> wrapping the VML presentation: a
    /// <c>v:shape</c> sized in points carrying an <c>o:OLEObject</c>, the model's ProgID, shape id, and an
    /// <c>r:id</c>. The relationship targets either an embedded binary part or an external source. A
    /// <c>v:imagedata</c>
    /// whose <c>r:id</c> points at the icon media part carries the presentation. The VML namespaces (v/o) are
    /// declared on the document root (see <see cref="BuildDocument"/>).
    /// SIMPLIFICATION (Y2): the VML presentation is minimised to a single v:shape (+ optional v:imagedata),
    /// and no live OLE activation or external-source update is performed.
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
            new XAttribute("Type", part.EmbeddedObject.IsLinked ? "Link" : "Embed"),
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
        XNamespace wp14 = "http://schemas.microsoft.com/office/word/2010/wordprocessingDrawing";
        var anchorId = ((uint)(0x10000000 + part.DrawingId)).ToString("X8", System.Globalization.CultureInfo.InvariantCulture);
        var editId = ((uint)(0x20000000 + part.DrawingId)).ToString("X8", System.Globalization.CultureInfo.InvariantCulture);

        var smartArtDocPr = new XElement(Wp + "docPr", new XAttribute("id", part.DrawingId), new XAttribute("name", name));
        var smartArtGraphic = BuildSmartArtGraphic(part);

        if (part.SmartArt.IsFloating && part.SmartArt.Placement is { } smartArtPlacement)
            return BuildAnchorContainer(cx, cy, smartArtDocPr, smartArtGraphic, smartArtPlacement);

        return new XElement(W + "drawing",
            new XElement(Wp + "inline",
                new XAttribute(XNamespace.Xmlns + "wp", Wp.NamespaceName),
                new XAttribute(wp14 + "anchorId", anchorId),
                new XAttribute(wp14 + "editId", editId),
                new XAttribute("distT", 0), new XAttribute("distB", 0),
                new XAttribute("distL", 0), new XAttribute("distR", 0),
                new XElement(Wp + "extent", new XAttribute("cx", cx), new XAttribute("cy", cy)),
                new XElement(Wp + "effectExtent",
                    new XAttribute("l", 19050), new XAttribute("t", 38100),
                    new XAttribute("r", 38100), new XAttribute("b", 57150)),
                smartArtDocPr,
                new XElement(Wp + "cNvGraphicFramePr"),
                smartArtGraphic));
    }

    private static XElement BuildSmartArtGraphic(SmartArtPart part) =>
        new(A + "graphic",
            new XAttribute(XNamespace.Xmlns + "a", A.NamespaceName),
            new XElement(A + "graphicData",
                new XAttribute("uri", DiagramGraphicDataUri),
                new XElement(Dgm + "relIds",
                    new XAttribute(XNamespace.Xmlns + "dgm", Dgm.NamespaceName),
                    new XAttribute(XNamespace.Xmlns + "r", R.NamespaceName),
                    new XAttribute(R + "dm", part.DataRelationshipId),
                    new XAttribute(R + "lo", part.LayoutRelationshipId),
                    new XAttribute(R + "qs", part.QuickStyleRelationshipId),
                    new XAttribute(R + "cs", part.ColorsRelationshipId))));

    /// <summary>
    /// Builds the <c>w:drawing/wp:anchor</c> for a <see cref="DrawingGroup"/>, emitting the group as
    /// <c>wpg:wgp</c> inside <c>a:graphicData[uri=wpg]</c>. Shapes and WordArt use <c>wps:wsp</c>; images use
    /// their native <c>pic:pic</c> payload; charts and SmartArt use native <c>wpg:graphicFrame</c> children
    /// that retain their document-level package relationships. Groups float.
    /// </summary>
    private static XElement BuildDrawingGroupDrawing(
        DrawingGroup group,
        RunDrawings drawings,
        IReadOnlyDictionary<string, string> hyperlinks,
        PreservedNumberingPlan? preservedNumbering = null,
        IReadOnlyDictionary<(ListKind Kind, int Level, int StartAt), int>? restartOverrides = null)
    {
        var ids = drawings.Ids;
        var cx = PointsToEmu(group.WidthPt);
        var cy = PointsToEmu(group.HeightPt);

        var groupDocPrId = ids.NextShapeDrawingId();
        var docPr = new XElement(Wp + "docPr",
            new XAttribute("id", groupDocPrId),
            new XAttribute("name", "Group " + groupDocPrId));

        // Build child elements inside wpg:wgp.
        var children = new List<XElement>();
        for (var i = 0; i < group.Children.Count; i++)
        {
            var child = group.Children[i];
            var (ox, oy) = i < group.ChildOffsets.Count ? group.ChildOffsets[i] : (0.0, 0.0);
            var childW = group.ChildWidthPt(i);
            var childH = group.ChildHeightPt(i);

            var xfrm = new XElement(A + "xfrm",
                new XElement(A + "off",
                    new XAttribute("x", PointsToEmu(ox)),
                    new XAttribute("y", PointsToEmu(oy))),
                new XElement(A + "ext",
                    new XAttribute("cx", PointsToEmu(childW)),
                    new XAttribute("cy", PointsToEmu(childH))));
            ApplyGroupChildTransform(xfrm, child);

            // Encode child metadata in the name attribute so the reader can reconstruct the type.
            var childName = child switch
            {
                InlineImage => "GroupChild:Image",
                Shape s => "GroupChild:Shape:" + s.Kind,
                Chart c => "GroupChild:Chart:" + c.Kind,
                SmartArt => "GroupChild:SmartArt",
                WordArt wa => "GroupChild:WordArt:" + wa.Style,
                DrawingGroup => "GroupChild:Group",
                _ => "GroupChild:Unknown"
            };

            children.Add(child switch
            {
                Shape shape => BuildDrawingGroupShapeChild(shape, xfrm, childName, ids, hyperlinks, preservedNumbering, restartOverrides),
                WordArt wordArt => BuildDrawingGroupWordArtChild(wordArt, xfrm, childName, ids),
                InlineImage image when drawings.GroupImages?.TryGetValue(image, out var imagePart) == true
                    => BuildDrawingGroupImageChild(imagePart, xfrm, childName),
                Chart chart when drawings.GroupCharts?.TryGetValue(chart, out var chartPart) == true
                    => BuildDrawingGroupGraphicFrameChild(BuildChartGraphic(chartPart), xfrm, childName, ids),
                SmartArt smartArt when drawings.GroupSmartArts?.TryGetValue(smartArt, out var smartArtPart) == true
                    => BuildDrawingGroupGraphicFrameChild(BuildSmartArtGraphic(smartArtPart), xfrm, childName, ids),
                DrawingGroup nestedGroup => BuildNestedDrawingGroupChild(
                    nestedGroup, xfrm, drawings, hyperlinks, preservedNumbering, restartOverrides),
                _ => BuildDrawingGroupPlaceholderChild(xfrm, new XElement(Wp + "docPr",
                    new XAttribute("id", ids.NextShapeDrawingId()),
                    new XAttribute("name", childName)))
            });
        }

        var groupXfrm = new XElement(A + "xfrm",
                new XElement(A + "off",
                    new XAttribute("x", 0), new XAttribute("y", 0)),
                new XElement(A + "ext",
                    new XAttribute("cx", cx), new XAttribute("cy", cy)),
                new XElement(A + "chOff",
                    new XAttribute("x", 0), new XAttribute("y", 0)),
                new XElement(A + "chExt",
                    new XAttribute("cx", cx), new XAttribute("cy", cy)));
        if (group.RotationAngle != 0)
            groupXfrm.Add(new XAttribute("rot", (long)Math.Round(group.RotationAngle * 60000)));
        if (group.FlipH)
            groupXfrm.Add(new XAttribute("flipH", "1"));
        if (group.FlipV)
            groupXfrm.Add(new XAttribute("flipV", "1"));

        var grpSpPr = new XElement(Wpg + "grpSpPr", groupXfrm);

        var wgp = new XElement(Wpg + "wgp",
            new XAttribute(XNamespace.Xmlns + "wpg", Wpg.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "wps", Wps.NamespaceName),
            new XElement(Wpg + "cNvPr",
                new XAttribute("id", groupDocPrId),
                new XAttribute("name", "Group " + groupDocPrId)),
            new XElement(Wpg + "cNvGrpSpPr"),
            grpSpPr);
        foreach (var child in children) wgp.Add(child);

        var graphic = new XElement(A + "graphic",
            new XAttribute(XNamespace.Xmlns + "a", A.NamespaceName),
            new XElement(A + "graphicData",
                new XAttribute("uri", GroupGraphicDataUri),
                wgp));

        return BuildAnchorContainer(cx, cy, docPr, graphic, group.Placement);
    }

    private static XElement BuildNestedDrawingGroupChild(
        DrawingGroup group,
        XElement xfrm,
        RunDrawings drawings,
        IReadOnlyDictionary<string, string> hyperlinks,
        PreservedNumberingPlan? preservedNumbering,
        IReadOnlyDictionary<(ListKind Kind, int Level, int StartAt), int>? restartOverrides)
    {
        // Nested groups share the normal group child payload pipeline; only their group transform is local
        // to the parent rather than being a document anchor.
        var nested = new XElement(BuildDrawingGroupDrawing(group, drawings, hyperlinks, preservedNumbering, restartOverrides)
            .Descendants(Wpg + "wgp")
            .Single());
        var nestedXfrm = nested.Element(Wpg + "grpSpPr")?.Element(A + "xfrm");
        if (nestedXfrm is null)
            return nested;

        var localOff = xfrm.Element(A + "off");
        var localExt = xfrm.Element(A + "ext");
        nestedXfrm.Element(A + "off")?.ReplaceWith(localOff is null ? new XElement(A + "off") : new XElement(localOff));
        nestedXfrm.Element(A + "ext")?.ReplaceWith(localExt is null ? new XElement(A + "ext") : new XElement(localExt));
        foreach (var name in new[] { "rot", "flipH", "flipV" })
        {
            var attribute = xfrm.Attribute(name);
            if (attribute is not null)
                nestedXfrm.SetAttributeValue(name, attribute.Value);
            else
                nestedXfrm.Attribute(name)?.Remove();
        }
        return nested;
    }

    private static void ApplyGroupChildTransform(XElement xfrm, object child)
    {
        var (rotation, flipH, flipV) = child switch
        {
            InlineImage image => (image.RotationAngle, image.FlipH, image.FlipV),
            Shape shape => (shape.RotationAngle, shape.FlipH, shape.FlipV),
            Chart chart => (chart.RotationAngle, chart.FlipH, chart.FlipV),
            SmartArt smartArt => (smartArt.RotationAngle, smartArt.FlipH, smartArt.FlipV),
            WordArt wordArt => (wordArt.RotationAngle, wordArt.FlipH, wordArt.FlipV),
            DrawingGroup group => (group.RotationAngle, group.FlipH, group.FlipV),
            _ => (0d, false, false)
        };

        if (rotation != 0)
            xfrm.SetAttributeValue("rot", (long)Math.Round(rotation * 60000));
        if (flipH)
            xfrm.SetAttributeValue("flipH", "1");
        if (flipV)
            xfrm.SetAttributeValue("flipV", "1");
    }

    private static XElement BuildDrawingGroupShapeChild(
        Shape shape,
        XElement xfrm,
        string childName,
        IdAllocator ids,
        IReadOnlyDictionary<string, string> hyperlinks,
        PreservedNumberingPlan? preservedNumbering,
        IReadOnlyDictionary<(ListKind Kind, int Level, int StartAt), int>? restartOverrides)
    {
        var drawing = BuildShapeDrawing(shape, ids, hyperlinks, preservedNumbering, restartOverrides);
        return BuildDrawingGroupRichWspChild(drawing, xfrm, childName);
    }

    private static XElement BuildDrawingGroupWordArtChild(
        WordArt wordArt,
        XElement xfrm,
        string childName,
        IdAllocator ids)
    {
        var drawing = BuildWordArtDrawing(wordArt, ids);
        return BuildDrawingGroupRichWspChild(drawing, xfrm, childName);
    }

    private static XElement BuildDrawingGroupImageChild(
        ImagePart part,
        XElement xfrm,
        string childName)
    {
        var drawing = BuildDrawing(part);
        var pic = new XElement(drawing.Descendants(Pic + "pic").First());
        var cNvPr = pic.Element(Pic + "nvPicPr")?.Element(Pic + "cNvPr");
        if (cNvPr is not null)
            cNvPr.SetAttributeValue("name", childName);
        var spPr = pic.Element(Pic + "spPr");
        spPr?.Element(A + "xfrm")?.ReplaceWith(new XElement(xfrm));
        return pic;
    }

    private static XElement BuildDrawingGroupGraphicFrameChild(
        XElement graphic,
        XElement xfrm,
        string childName,
        IdAllocator ids)
    {
        var groupTransform = new XElement(Wpg + "xfrm",
            xfrm.Attributes().Select(attribute => new XAttribute(attribute)),
            xfrm.Elements().Select(element => new XElement(element)));
        return new XElement(Wpg + "graphicFrame",
            new XElement(Wpg + "cNvPr",
                new XAttribute("id", ids.NextShapeDrawingId()),
                new XAttribute("name", childName)),
            new XElement(Wpg + "cNvFrPr"),
            groupTransform,
            graphic);
    }

    private static XElement BuildDrawingGroupRichWspChild(
        XElement drawing,
        XElement xfrm,
        string childName)
    {
        var wsp = new XElement(drawing.Descendants(Wps + "wsp").First());
        var childDocPr = new XElement(drawing.Descendants(Wp + "docPr").First());
        childDocPr.Name = Wps + "cNvPr";
        childDocPr.SetAttributeValue("name", childName);

        ReplaceChildTransform(wsp, xfrm);
        wsp.AddFirst(childDocPr);
        return wsp;
    }

    private static XElement BuildDrawingGroupPlaceholderChild(XElement xfrm, XElement childDocPr)
    {
        var spPr = new XElement(Wps + "spPr",
            xfrm,
            new XElement(A + "prstGeom",
                new XAttribute("prst", "rect"),
                new XElement(A + "avLst")),
            new XElement(A + "solidFill",
                new XElement(A + "srgbClr",
                    new XAttribute("val", "C0C0C0"))));

        return new XElement(Wps + "wsp",
            new XAttribute(XNamespace.Xmlns + "wps", Wps.NamespaceName),
            new XElement(Wps + "cNvPr",
                childDocPr.Attributes().Where(attribute => !attribute.IsNamespaceDeclaration)),
            new XElement(Wps + "cNvSpPr",
                new XElement(A + "spLocks",
                    new XAttribute("noChangeArrowheads", 1))),
            spPr,
            new XElement(Wps + "bodyPr"));
    }

    private static void ReplaceChildTransform(XElement wsp, XElement xfrm)
    {
        var spPr = wsp.Element(Wps + "spPr");
        if (spPr is null)
            return;

        var current = spPr.Element(A + "xfrm");
        if (current is null)
            spPr.AddFirst(new XElement(xfrm));
        else
            current.ReplaceWith(new XElement(xfrm));
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
        var docId = SmartArtModelId(0);
        var isProcessLayout = smartArt.Kind == SmartArtKind.Process
            && string.Equals(SmartArtLayoutSuffix(smartArt), "process1", StringComparison.OrdinalIgnoreCase);
        var isHierarchyLayout = string.Equals(SmartArtLayoutCategory(smartArt), "hierarchy", StringComparison.Ordinal)
            || UsesFlatSmartArtGallery(smartArt) && !isProcessLayout;
        var isPyramidLayout = string.Equals(SmartArtLayoutCategory(smartArt), "pyramid", StringComparison.Ordinal);
        var ptLst = new XElement(Dgm + "ptLst",
            new XElement(Dgm + "pt",
                new XAttribute("modelId", docId),
                new XAttribute("type", "doc"),
                new XElement(Dgm + "prSet",
                    new XAttribute("loTypeId", SmartArtLayoutUrn(smartArt)),
                    new XAttribute("loCatId", SmartArtLayoutCategory(smartArt)),
                    new XAttribute("qsTypeId", "urn:microsoft.com/office/officeart/2005/8/quickstyle/" + SmartArtQuickStyleSuffix(smartArt)),
                    new XAttribute("qsCatId", "simple"),
                    new XAttribute("csTypeId", "urn:microsoft.com/office/officeart/2005/8/colors/" + SmartArtColorGallerySuffix(smartArt)),
                    new XAttribute("csCatId", SmartArtColorCategory(smartArt.ColorSchemeId)),
                    new XAttribute("phldr", "0")),
                new XElement(Dgm + "spPr"),
                DiagramText(string.Empty)));
        var cxnLst = new XElement(Dgm + "cxnLst");
        var semanticNodes = new List<(string Id, string PresentationId, string ParentId, int Ordinal, int Depth)>();
        var presentationIds = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [docId] = SmartArtModelId(3000)
        };
        var parentTransitionPresentationIds = new Dictionary<string, string>(StringComparer.Ordinal);
        var transitionPresentationIds = new Dictionary<string, string>(StringComparer.Ordinal);
        var transitionDepths = new Dictionary<string, int>(StringComparer.Ordinal);

        var nextId = 0;
        var nextCxn = 0;
        string Emit(SmartArtNode node, string parentId, int ordinal, int depth)
        {
            var id = SmartArtModelId(++nextId);
            var presentationId = SmartArtModelId(3000 + nextId);
            presentationIds[id] = presentationId;
            semanticNodes.Add((id, presentationId, parentId, ordinal, depth));
            ptLst.Add(new XElement(Dgm + "pt",
                new XAttribute("modelId", id),
                new XElement(Dgm + "prSet", new XAttribute("phldrT", "[Text]")),
                new XElement(Dgm + "spPr"),
                DiagramText(node.Text)));

            var connectionId = SmartArtModelId(1000 + nextCxn);
            if (isHierarchyLayout || isPyramidLayout || isProcessLayout)
            {
                var parTransId = SmartArtModelId(4000 + nextCxn);
                var sibTransId = SmartArtModelId(5000 + nextCxn);
                parentTransitionPresentationIds[parTransId] = SmartArtModelId(7500 + nextCxn);
                transitionPresentationIds[sibTransId] = SmartArtModelId(8000 + nextCxn);
                transitionDepths[sibTransId] = depth;
                AddTransitionPoint(ptLst, parTransId, "parTrans", connectionId);
                AddTransitionPoint(ptLst, sibTransId, "sibTrans", connectionId);
                cxnLst.Add(new XElement(Dgm + "cxn",
                    new XAttribute("modelId", connectionId),
                    new XAttribute("srcId", parentId),
                    new XAttribute("destId", id),
                    new XAttribute("srcOrd", ordinal),
                    new XAttribute("destOrd", 0),
                    new XAttribute("parTransId", parTransId),
                    new XAttribute("sibTransId", sibTransId)));
            }
            else
            {
                cxnLst.Add(new XElement(Dgm + "cxn",
                    new XAttribute("modelId", connectionId),
                    new XAttribute("type", "parOf"),
                    new XAttribute("srcId", parentId),
                    new XAttribute("destId", id),
                    new XAttribute("srcOrd", 0),
                    new XAttribute("destOrd", 0)));
            }
            nextCxn++;
            for (var i = 0; i < node.Children.Count; i++)
                Emit(node.Children[i], id, i, depth + 1);
            return id;
        }

        var flatParentId = docId;
        for (var i = 0; i < smartArt.Nodes.Count; i++)
        {
            var node = smartArt.Nodes[i];
            var parentId = UsesFlatSmartArtGallery(smartArt) && smartArt.Kind != SmartArtKind.Process
                ? flatParentId
                : docId;
            var emittedId = Emit(node, parentId, i, parentId == docId ? 0 : i);
            if (UsesFlatSmartArtGallery(smartArt) && smartArt.Kind != SmartArtKind.Process)
                flatParentId = emittedId;
        }

        // Word's Organization Chart gallery uses a different presentation graph from the generic
        // hierarchy gallery. Preserve the native three-level chain used by the Word parity corpus;
        // the generic graph remains the fallback for other hierarchy shapes.
        if (string.Equals(SmartArtLayoutSuffix(smartArt), "orgChart1", StringComparison.Ordinal)
            && semanticNodes.Count == 3
            && semanticNodes[0].Depth == 0
            && semanticNodes[1].Depth == 1
            && semanticNodes[2].Depth == 2)
            return BuildNativeOrgChartChainData(smartArt, semanticNodes, drawingRelId);

        var layoutUrn = SmartArtLayoutUrn(smartArt);

        void AddPresentationPoint(
            string id,
            string assocId,
            string name,
            string? styleLabel = null,
            int? styleIndex = null,
            int styleCount = 0,
            XElement? presentationLayoutVars = null,
            bool includeTextBody = false)
        {
            var prSet = new XElement(Dgm + "prSet",
                new XAttribute("presAssocID", assocId),
                new XAttribute("presName", name),
                new XAttribute("presStyleCnt", styleCount));
            if (!string.IsNullOrEmpty(styleLabel))
                prSet.Add(new XAttribute("presStyleLbl", styleLabel));
            if (styleIndex is not null)
                prSet.Add(new XAttribute("presStyleIdx", styleIndex.Value));
            if (presentationLayoutVars is not null)
                prSet.Add(presentationLayoutVars);
            ptLst.Add(new XElement(Dgm + "pt",
                new XAttribute("modelId", id),
                new XAttribute("type", "pres"),
                prSet,
                new XElement(Dgm + "spPr"),
                includeTextBody ? DiagramText(string.Empty) : null));
        }

        void AddPresentationConnection(string type, string sourceId, string destinationId, int sourceOrdinal = 0)
        {
            cxnLst.Add(new XElement(Dgm + "cxn",
                new XAttribute("modelId", SmartArtModelId(6000 + cxnLst.Elements().Count())),
                new XAttribute("type", type),
                new XAttribute("srcId", sourceId),
                new XAttribute("destId", destinationId),
                new XAttribute("srcOrd", sourceOrdinal),
                new XAttribute("destOrd", 0),
                new XAttribute("presId", layoutUrn)));
        }

        if (isProcessLayout)
        {
            AddPresentationPoint(
                presentationIds[docId],
                docId,
                "Name0",
                presentationLayoutVars: new XElement(Dgm + "presLayoutVars",
                    new XElement(Dgm + "dir"),
                    new XElement(Dgm + "resizeHandles", new XAttribute("val", "exact"))));
            AddPresentationConnection("presOf", docId, presentationIds[docId]);

            var processNodePresentationIds = new List<string>();
            var processTransitionPresentationIds = new List<(string Transition, string ConnectorText)>();
            for (var i = 0; i < semanticNodes.Count; i++)
            {
                var node = semanticNodes[i];
                var nodePresentationId = SmartArtModelId(9000 + i * 3);
                processNodePresentationIds.Add(nodePresentationId);
                AddPresentationPoint(
                    nodePresentationId,
                    node.Id,
                    "node",
                    "node1",
                    i,
                    6,
                    new XElement(Dgm + "presLayoutVars",
                        new XElement(Dgm + "bulletEnabled", new XAttribute("val", "1"))));
                AddPresentationConnection("presOf", node.Id, nodePresentationId);

                if (i + 1 >= semanticNodes.Count)
                    continue;

                var transitionId = SmartArtModelId(5000 + i);
                var transitionPresentationId = SmartArtModelId(8000 + i);
                var connectorTextPresentationId = SmartArtModelId(8500 + i);
                processTransitionPresentationIds.Add((transitionPresentationId, connectorTextPresentationId));
                AddPresentationPoint(transitionPresentationId, transitionId, "sibTrans", "sibTrans2D1", i, 5);
                AddPresentationPoint(connectorTextPresentationId, transitionId, "connectorText", "sibTrans2D1", i, 5);
                AddPresentationConnection("presOf", transitionId, transitionPresentationId);
                AddPresentationConnection("presOf", transitionId, connectorTextPresentationId, 1);
            }

            for (var i = 0; i < processNodePresentationIds.Count; i++)
            {
                AddPresentationConnection("presParOf", presentationIds[docId], processNodePresentationIds[i], i * 2);
                if (i >= processTransitionPresentationIds.Count)
                    continue;

                var transition = processTransitionPresentationIds[i];
                AddPresentationConnection("presParOf", presentationIds[docId], transition.Transition, i * 2 + 1);
                AddPresentationConnection("presParOf", transition.Transition, transition.ConnectorText);
            }
        }
        else if (isHierarchyLayout || isPyramidLayout)
        {
            var containerIds = new Dictionary<string, string>(StringComparer.Ordinal);
            var rootIds = new Dictionary<string, string>(StringComparer.Ordinal);
            var nodePresentationId = 9000;

            var documentName = isHierarchyLayout ? "hierChild1" : "Name0";
            AddPresentationPoint(
                presentationIds[docId],
                docId,
                documentName,
                presentationLayoutVars: isHierarchyLayout
                    ? new XElement(Dgm + "presLayoutVars",
                        new XElement(Dgm + "chPref", new XAttribute("val", "1")),
                        new XElement(Dgm + "dir"),
                        new XElement(Dgm + "animOne", new XAttribute("val", "branch")),
                        new XElement(Dgm + "animLvl", new XAttribute("val", "lvl")),
                        new XElement(Dgm + "resizeHandles"))
                    : isPyramidLayout
                        ? new XElement(Dgm + "presLayoutVars",
                            new XElement(Dgm + "dir"),
                            new XElement(Dgm + "animLvl", new XAttribute("val", "lvl")),
                            new XElement(Dgm + "resizeHandles", new XAttribute("val", "exact")))
                        : null);
            AddPresentationConnection("presOf", docId, presentationIds[docId]);

            foreach (var node in semanticNodes)
            {
                if (isPyramidLayout)
                {
                    var containerId = SmartArtModelId(nodePresentationId++);
                    var levelId = SmartArtModelId(nodePresentationId++);
                    var levelTextId = SmartArtModelId(nodePresentationId++);
                    containerIds[node.Id] = containerId;
                    AddPresentationPoint(containerId, node.Id, "Name8");
                    var pyramidPresentationLayoutVars = new XElement(Dgm + "presLayoutVars",
                        new XElement(Dgm + "chMax", new XAttribute("val", "1")),
                        new XElement(Dgm + "bulletEnabled", new XAttribute("val", "1")));
                    AddPresentationPoint(
                        levelId,
                        node.Id,
                        "level",
                        "node1",
                        node.Ordinal,
                        semanticNodes.Count,
                        new XElement(pyramidPresentationLayoutVars));
                    AddPresentationPoint(
                        levelTextId,
                        node.Id,
                        "levelTx",
                        "revTx",
                        0,
                        presentationLayoutVars: new XElement(pyramidPresentationLayoutVars));
                    AddPresentationConnection("presOf", node.Id, levelId);
                    AddPresentationConnection("presOf", node.Id, levelTextId, 1);
                    AddPresentationConnection("presParOf", containerId, levelId);
                    AddPresentationConnection("presParOf", containerId, levelTextId, 1);
                    continue;
                }

                var depth = node.Depth;
                var rootId = SmartArtModelId(nodePresentationId++);
                var compositeId = SmartArtModelId(nodePresentationId++);
                var backgroundId = SmartArtModelId(nodePresentationId++);
                var textId = SmartArtModelId(nodePresentationId++);
                var childId = SmartArtModelId(nodePresentationId++);
                rootIds[node.Id] = rootId;
                containerIds[node.Id] = childId;
                var suffix = depth == 0 ? string.Empty : (depth + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
                var hierarchyStyleIndex = depth == 0 ? 0 : depth + 1;
                var hierarchyStyleCount = depth + 1;
                AddPresentationPoint(rootId, node.Id, "hierRoot" + (depth + 1).ToString(System.Globalization.CultureInfo.InvariantCulture));
                AddPresentationPoint(compositeId, node.Id, "composite" + suffix);
                AddPresentationPoint(
                    backgroundId,
                    node.Id,
                    "background" + suffix,
                    "node" + hierarchyStyleIndex.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    0,
                    hierarchyStyleCount);
                AddPresentationPoint(
                    textId,
                    node.Id,
                    "text" + suffix,
                    "fgAcc" + hierarchyStyleIndex.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    0,
                    hierarchyStyleCount,
                    new XElement(Dgm + "presLayoutVars",
                        new XElement(Dgm + "chPref", new XAttribute("val", "3"))));
                AddPresentationPoint(childId, node.Id, "hierChild" + (depth + 2).ToString(System.Globalization.CultureInfo.InvariantCulture));
                AddPresentationConnection("presOf", node.Id, textId);
                AddPresentationConnection("presParOf", rootId, compositeId);
                AddPresentationConnection("presParOf", compositeId, backgroundId);
                AddPresentationConnection("presParOf", compositeId, textId, 1);
                AddPresentationConnection("presParOf", compositeId, childId, 2);
            }

            if (isPyramidLayout)
            {
                foreach (var node in semanticNodes.OrderBy(n => n.Ordinal))
                    AddPresentationConnection("presParOf", presentationIds[docId], containerIds[node.Id], node.Ordinal);
            }
            else
            {
                foreach (var group in semanticNodes.GroupBy(node => node.ParentId, StringComparer.Ordinal))
                {
                    var sourceId = group.Key == docId ? presentationIds[docId] : containerIds[group.Key];
                    var children = group.OrderBy(node => node.Ordinal).ToList();
                    for (var i = 0; i < children.Count; i++)
                    {
                        var child = children[i];
                        var childRoot = rootIds[child.Id];
                        if (group.Key == docId)
                        {
                            AddPresentationConnection("presParOf", sourceId, childRoot, i);
                        }
                        else
                        {
                            var parentTransitionId = SmartArtModelId(4000 + semanticNodes.IndexOf(child));
                            var parentTransitionPresentationId = parentTransitionPresentationIds[parentTransitionId];
                            var transitionName = "Name" + (10 + Math.Max(0, child.Depth - 1) * 7).ToString(System.Globalization.CultureInfo.InvariantCulture);
                            var parentTransitionStyleCount = Math.Max(2, child.Depth + 1);
                            AddPresentationPoint(
                                parentTransitionPresentationId,
                                parentTransitionId,
                                transitionName,
                                "parChTrans1D" + parentTransitionStyleCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
                                0,
                                parentTransitionStyleCount);
                            AddPresentationConnection("presOf", parentTransitionId, parentTransitionPresentationId);
                            AddPresentationConnection("presParOf", sourceId, parentTransitionPresentationId, i * 2);
                            AddPresentationConnection("presParOf", parentTransitionPresentationId, childRoot);
                        }
                        if (i + 1 < children.Count)
                        {
                            var transitionId = SmartArtModelId(5000 + semanticNodes.IndexOf(child));
                            var transitionPresentationId = transitionPresentationIds[transitionId];
                            var transitionName = "Name" + (10 + transitionDepths[transitionId] * 7).ToString(System.Globalization.CultureInfo.InvariantCulture);
                            var siblingTransitionStyleCount = transitionDepths[transitionId] + 2;
                            AddPresentationPoint(
                                transitionPresentationId,
                                transitionId,
                                transitionName,
                                "parChTrans1D" + siblingTransitionStyleCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
                                0,
                                siblingTransitionStyleCount);
                            AddPresentationConnection("presOf", transitionId, transitionPresentationId);
                            AddPresentationConnection("presParOf", sourceId, transitionPresentationId, i * 2 + 1);
                            AddPresentationConnection("presParOf", transitionPresentationId, rootIds[children[i + 1].Id]);
                        }
                    }
                }
            }
        }
        else
        {
            AddPresentationPoint(presentationIds[docId], docId, "diagram");
            AddPresentationConnection("presOf", docId, presentationIds[docId]);
            foreach (var node in semanticNodes)
            {
                AddPresentationPoint(node.PresentationId, node.Id, "node", "node0", 0, 1, includeTextBody: true);
                AddPresentationConnection("presOf", node.Id, node.PresentationId);
                AddPresentationConnection("presParOf", presentationIds[docId], node.PresentationId, node.Ordinal);
            }
        }

        return new XDocument(
            new XElement(Dgm + "dataModel",
                new XAttribute(XNamespace.Xmlns + "dgm", Dgm.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "a", A.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "r", R.NamespaceName),
                ptLst,
                cxnLst,
                new XElement(Dgm + "bg"),
                new XElement(Dgm + "whole"),
                new XElement(Dgm + "extLst",
                    new XElement(A + "ext",
                        new XAttribute("uri", Dsp.NamespaceName),
                        new XElement(Dsp + "dataModelExt",
                            new XAttribute("relId", drawingRelId),
                            new XAttribute("minVer", Dgm.NamespaceName))))));
    }

    private static XDocument BuildNativeOrgChartChainData(
        SmartArt smartArt,
        IReadOnlyList<(string Id, string PresentationId, string ParentId, int Ordinal, int Depth)> semanticNodes,
        string drawingRelId)
    {
        var resourceName = "FreeW.Core.IO.SmartArtLayoutTemplates.orgChart1-data-chain.xml";
        using var stream = typeof(DocxWriter).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException("Embedded SmartArt data template not found: " + resourceName);
        var data = XDocument.Load(stream);
        var root = data.Root ?? throw new InvalidOperationException("SmartArt data template has no root: " + resourceName);
        var ptLst = root.Element(Dgm + "ptLst")
            ?? throw new InvalidOperationException("SmartArt data template has no point list: " + resourceName);
        var points = ptLst.Elements(Dgm + "pt").ToList();
        var semanticPoints = points
            .Where(point => point.Element(Dgm + "prSet")?.Attribute("phldrT")?.Value == "[Text]")
            .ToList();
        if (semanticPoints.Count != semanticNodes.Count)
            throw new InvalidOperationException("Native Organization Chart data template does not match the semantic chain.");

        var semanticTexts = new List<string>();
        void Flatten(SmartArtNode node)
        {
            semanticTexts.Add(node.Text);
            foreach (var child in node.Children)
                Flatten(child);
        }
        foreach (var node in smartArt.Nodes)
            Flatten(node);
        if (semanticTexts.Count != semanticNodes.Count)
            throw new InvalidOperationException("Native Organization Chart data template requires a complete semantic chain.");

        var idMap = new Dictionary<string, string>(StringComparer.Ordinal);
        var semanticPointIndexes = semanticPoints
            .Select((point, index) => (Id: point.Attribute("modelId")!.Value, Index: index))
            .ToDictionary(item => item.Id, item => item.Index, StringComparer.Ordinal);
        var parentTransitionIndexes = new Dictionary<string, int>(StringComparer.Ordinal);
        var semanticIndex = 0;
        var transitionIndex = 0;
        foreach (var point in points)
        {
            var oldId = point.Attribute("modelId")?.Value;
            if (string.IsNullOrEmpty(oldId))
                continue;

            var type = point.Attribute("type")?.Value;
            if (type == "parTrans")
                parentTransitionIndexes[oldId] = transitionIndex;
            var newId = type switch
            {
                "doc" => SmartArtModelId(0),
                "parTrans" => SmartArtModelId(4000 + transitionIndex),
                "sibTrans" => SmartArtModelId(5000 + transitionIndex),
                _ when point.Element(Dgm + "prSet")?.Attribute("phldrT")?.Value == "[Text]"
                    => semanticNodes[semanticIndex++].Id,
                _ => null
            };
            if (newId is null)
                continue;

            idMap[oldId] = newId;
            point.SetAttributeValue("modelId", newId);
            if (type is "parTrans" or "sibTrans")
                transitionIndex += type == "sibTrans" ? 1 : 0;
        }

        var semanticPointIndex = 0;
        foreach (var point in semanticPoints)
        {
            point.SetAttributeValue("type", semanticPointIndex == 1 ? "asst" : null);
            point.Element(Dgm + "t")?.ReplaceWith(DiagramText(semanticTexts[semanticPointIndex]));
            semanticPointIndex++;
        }

        var connections = root.Element(Dgm + "cxnLst")?.Elements(Dgm + "cxn")
            ?? Enumerable.Empty<XElement>();
        var semanticConnectionIndex = 0;
        foreach (var connection in connections)
        {
            foreach (var attributeName in new[] { "srcId", "destId", "parTransId", "sibTransId" })
            {
                var attribute = connection.Attribute(attributeName);
                if (attribute is not null && idMap.TryGetValue(attribute.Value, out var mappedId))
                    attribute.Value = mappedId;
            }

            if (connection.Attribute("type") is null)
            {
                connection.SetAttributeValue("modelId", SmartArtModelId(1000 + semanticConnectionIndex));
                semanticConnectionIndex++;
            }
        }

        foreach (var point in points.Where(point => point.Attribute("type")?.Value == "pres"))
        {
            var assoc = point.Element(Dgm + "prSet")?.Attribute("presAssocID");
            var originalAssocId = assoc?.Value;
            if (assoc is not null && idMap.TryGetValue(assoc.Value, out var mappedAssoc))
                assoc.Value = mappedAssoc;

            var presentationName = point.Element(Dgm + "prSet")?.Attribute("presName")?.Value;
            if (presentationName?.StartsWith("rootText", StringComparison.Ordinal) == true
                && originalAssocId is not null
                && semanticPointIndexes.TryGetValue(originalAssocId, out var nodeIndex))
            {
                point.SetAttributeValue("modelId", SmartArtModelId(9000 + nodeIndex * 5 + 3));
            }
            else if (originalAssocId is not null
                     && parentTransitionIndexes.TryGetValue(originalAssocId, out var parentTransitionIndex))
            {
                point.SetAttributeValue("modelId", SmartArtModelId(7500 + parentTransitionIndex));
            }
        }

        var docPrSet = points.Single(point => point.Attribute("type")?.Value == "doc")
            .Element(Dgm + "prSet")!;
        docPrSet.SetAttributeValue("loTypeId", SmartArtLayoutUrn(smartArt));
        docPrSet.SetAttributeValue("loCatId", SmartArtLayoutCategory(smartArt));
        docPrSet.SetAttributeValue("qsTypeId", "urn:microsoft.com/office/officeart/2005/8/quickstyle/" + SmartArtQuickStyleSuffix(smartArt));
        docPrSet.SetAttributeValue("qsCatId", "simple");
        docPrSet.SetAttributeValue("csTypeId", "urn:microsoft.com/office/officeart/2005/8/colors/" + SmartArtColorGallerySuffix(smartArt));
        docPrSet.SetAttributeValue("csCatId", SmartArtColorCategory(smartArt.ColorSchemeId));
        docPrSet.SetAttributeValue("phldr", "0");

        root.Descendants(Dsp + "dataModelExt").Single().SetAttributeValue("relId", drawingRelId);
        return data;
    }

    private static XElement DiagramText(string text)
    {
        var paragraph = string.IsNullOrEmpty(text)
            ? new XElement(A + "p", new XElement(A + "endParaRPr", new XAttribute("lang", "en-US")))
            : new XElement(A + "p",
                new XElement(A + "r",
                    new XElement(A + "rPr", new XAttribute("lang", "en-US")),
                    new XElement(A + "t", text)));
        return new XElement(Dgm + "t",
            new XElement(A + "bodyPr"),
            new XElement(A + "lstStyle"),
            paragraph);
    }

    private static void AddTransitionPoint(XElement ptLst, string id, string type, string connectionId) =>
        ptLst.Add(new XElement(Dgm + "pt",
            new XAttribute("modelId", id),
            new XAttribute("type", type),
            new XAttribute("cxnId", connectionId),
            new XElement(Dgm + "prSet"),
            new XElement(Dgm + "spPr"),
            DiagramText(string.Empty)));

    private static string SmartArtLayoutUrn(SmartArt smartArt)
    {
        var suffix = SmartArtLayoutSuffix(smartArt);
        return "urn:microsoft.com/office/officeart/2005/8/layout/" + suffix;
    }

    private static string SmartArtLayoutSuffix(SmartArt smartArt)
    {
        var suffix = smartArt.LayoutId ?? smartArt.Kind switch
        {
            SmartArtKind.Process => "process1",
            SmartArtKind.Hierarchy => "hierarchy1",
            _ => "default"
        };

        // Word's stock layout ids are case-sensitive. Keep accepting the historical FreeW spelling
        // while emitting the identifier used by Word's SmartArtLayouts collection.
        return string.Equals(suffix, "orgchart1", StringComparison.OrdinalIgnoreCase)
            ? "orgChart1"
            : suffix;
    }

    private static string SmartArtLayoutCategory(SmartArt smartArt)
    {
        var suffix = SmartArtLayoutSuffix(smartArt);
        if (smartArt.Kind == SmartArtKind.Hierarchy ||
            suffix.StartsWith("hierarchy", StringComparison.OrdinalIgnoreCase) ||
            suffix.StartsWith("orgChart", StringComparison.OrdinalIgnoreCase))
            return "hierarchy";

        if (suffix.StartsWith("pyramid", StringComparison.OrdinalIgnoreCase))
            return "pyramid";

        return smartArt.Kind == SmartArtKind.Process ? "process" : "list";
    }

    private static string SmartArtColorGallerySuffix(string? colorId)
    {
        return colorId?.ToLowerInvariant() switch
        {
            "accent1" => "accent1_2",
            "accent1g" => "accent1_2",
            "accent2" => "accent2_1",
            "accent3" => "accent3_1",
            _ => colorId ?? "accent1_2"
        };
    }

    private static string SmartArtColorGallerySuffix(SmartArt smartArt) =>
        UsesFlatSmartArtGallery(smartArt) && smartArt.ColorSchemeId is null
            ? "accent0_1"
            : SmartArtColorGallerySuffix(smartArt.ColorSchemeId);

    private static string SmartArtColorCategory(string? colorId)
    {
        var suffix = colorId ?? "accent1_2";
        return suffix.StartsWith("accent1", StringComparison.OrdinalIgnoreCase) ? "accent1"
            : suffix.StartsWith("accent2", StringComparison.OrdinalIgnoreCase) ? "accent2"
            : suffix.StartsWith("accent3", StringComparison.OrdinalIgnoreCase) ? "accent3"
            : suffix.StartsWith("accent", StringComparison.OrdinalIgnoreCase) ? "accent1"
            : suffix.StartsWith("colorful", StringComparison.OrdinalIgnoreCase) ? "colorful" : "accent1";
    }

    private static string SmartArtModelId(int index) =>
        "{4F4B0000-0000-4000-8000-" + index.ToString("D12", System.Globalization.CultureInfo.InvariantCulture) + "}";

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

        // Keep the cached drawing inside the authored SmartArt frame. Word consumes this drawing through
        // dsp:dataModelExt, so fixed two-inch boxes make a four-node process wrap outside its frame.
        var frameW = Math.Max(1L, PointsToEmu(smartArt.WidthPt));
        var frameH = Math.Max(1L, PointsToEmu(smartArt.HeightPt));
        var margin = Math.Max(1L, PointsToEmu(6));
        var gap = Math.Max(1L, PointsToEmu(8));
        var nodeCount = Math.Max(1, nodes.Count);
        var isProcess = smartArt.Kind == SmartArtKind.Process
            || string.Equals(smartArt.LayoutId, "process1", StringComparison.OrdinalIgnoreCase)
            || string.Equals(smartArt.LayoutId, "continuousBlockProcess", StringComparison.OrdinalIgnoreCase);
        var drawingCategory = SmartArtLayoutCategory(smartArt);
        var isHierarchy = string.Equals(drawingCategory, "hierarchy", StringComparison.Ordinal);
        var isPyramid = string.Equals(drawingCategory, "pyramid", StringComparison.Ordinal);
        var boxW = isProcess
            ? Math.Max(1L, (frameW - 2 * margin - Math.Max(0, nodeCount - 1) * gap) / nodeCount)
            : Math.Max(1L, frameW - 2 * margin);
        var boxH = isProcess
            ? Math.Max(1L, Math.Min(PointsToEmu(30), frameH - 2 * margin))
            : Math.Max(1L, (frameH - 2 * margin - Math.Max(0, nodeCount - 1) * gap) / nodeCount);
        var colorScheme = SmartArtColorScheme.FindById(smartArt.ColorSchemeId ?? string.Empty)
            ?? SmartArtColorScheme.Default;
        var style = SmartArtStyle.FindById(smartArt.StyleId ?? string.Empty)
            ?? SmartArtStyle.Default;
        var textHex = colorScheme.TextHex.TrimStart('#').ToUpperInvariant();
        var hierarchyAccentHex = (smartArt.ColorSchemeId is null
                ? "156082"
                : colorScheme.FillHexAt(0))
            .TrimStart('#')
            .ToUpperInvariant();

        XElement SchemeSolidFill(string scheme, params (string Name, string Value)[] modifiers) =>
            new(A + "solidFill",
                new XElement(A + "schemeClr",
                    new XAttribute("val", scheme),
                    modifiers.Select(modifier => new XElement(A + modifier.Name, new XAttribute("val", modifier.Value)))));

        var spTree = new XElement(Dsp + "spTree",
            new XElement(Dsp + "nvGrpSpPr",
                new XElement(Dsp + "cNvPr",
                    new XAttribute("id", 0),
                    new XAttribute("name", "Diagram")),
                new XElement(Dsp + "cNvGrpSpPr")),
            new XElement(Dsp + "grpSpPr",
                new XElement(A + "xfrm",
                    new XElement(A + "off", new XAttribute("x", 0), new XAttribute("y", 0)),
                    new XElement(A + "ext", new XAttribute("cx", frameW), new XAttribute("cy", frameH)),
                    new XElement(A + "chOff", new XAttribute("x", 0), new XAttribute("y", 0)),
                    new XElement(A + "chExt", new XAttribute("cx", frameW), new XAttribute("cy", frameH)))));

        void AddCachedShape(
            string modelId,
            long x,
            long y,
            long width,
            long height,
            string shapeKind,
            string fillHex,
            bool noFill = false,
            string? text = null,
            string? fillScheme = null,
            string? fillAlpha = null,
            string? lineScheme = null,
            string? lineShade = null,
            string? lineHex = null)
        {
            var line = new XElement(A + "ln",
                new XAttribute("w", Math.Max(1L, PointsToEmu(0.75))),
                lineHex is not null
                    ? SolidFill(lineHex)
                    : lineScheme is null
                        ? SolidFill("808080")
                        : SchemeSolidFill(
                            lineScheme,
                            lineShade is null ? Array.Empty<(string, string)>() : new[] { ("shade", lineShade) }));
            var geometryAdjustments = shapeKind switch
            {
                "trapezoid" => new XElement(A + "avLst",
                    new XElement(A + "gd",
                        new XAttribute("name", "adj"),
                        new XAttribute("fmla", "val 68182"))),
                "roundRect" => new XElement(A + "avLst",
                    new XElement(A + "gd",
                        new XAttribute("name", "adj"),
                        new XAttribute("fmla", "val 10000"))),
                "rightArrow" => new XElement(A + "avLst",
                    new XElement(A + "gd",
                        new XAttribute("name", "adj1"),
                        new XAttribute("fmla", "val 60000")),
                    new XElement(A + "gd",
                        new XAttribute("name", "adj2"),
                        new XAttribute("fmla", "val 50000"))),
                _ => new XElement(A + "avLst")
            };
            var spPr = new XElement(Dsp + "spPr",
                new XElement(A + "xfrm",
                    new XElement(A + "off", new XAttribute("x", x), new XAttribute("y", y)),
                    new XElement(A + "ext", new XAttribute("cx", width), new XAttribute("cy", height))),
                new XElement(A + "prstGeom", new XAttribute("prst", shapeKind), geometryAdjustments),
                noFill
                    ? new XElement(A + "noFill")
                    : fillScheme is null
                        ? SolidFill(fillHex)
                        : SchemeSolidFill(
                            fillScheme,
                            fillAlpha is null ? Array.Empty<(string, string)>() : new[] { ("alpha", fillAlpha) }),
                line,
                new XElement(A + "effectLst"));
            var textSize = shapeKind == "trapezoid"
                ? Math.Clamp((int)Math.Round(height / 12700d * 56d), 900, 3100)
                : 1100;
            var textBody = new XElement(Dsp + "txBody",
                new XElement(A + "bodyPr",
                    new XAttribute("spcFirstLastPara", 0),
                    new XAttribute("vert", "horz"),
                    new XAttribute("wrap", "square"),
                    new XAttribute("lIns", 39370),
                    new XAttribute("tIns", 39370),
                    new XAttribute("rIns", 39370),
                    new XAttribute("bIns", 39370),
                    new XAttribute("numCol", 1),
                    new XAttribute("spcCol", 1270),
                    new XAttribute("anchor", "ctr"),
                    new XAttribute("anchorCtr", 0),
                    new XElement(A + "noAutofit")),
                new XElement(A + "lstStyle"),
                string.IsNullOrEmpty(text)
                    ? new XElement(A + "p",
                        new XElement(A + "endParaRPr", new XAttribute("lang", "en-US")))
                    : new XElement(A + "p",
                        new XAttribute("algn", "ctr"),
                        new XElement(A + "pPr",
                            new XAttribute("marL", 0),
                            new XAttribute("lvl", 0),
                            new XAttribute("indent", 0),
                            new XAttribute("algn", "ctr"),
                            new XAttribute("defTabSz", 1377950),
                            new XElement(A + "lnSpc", new XElement(A + "spcPct", new XAttribute("val", 90000))),
                            new XElement(A + "spcBef", new XElement(A + "spcPct", new XAttribute("val", 0))),
                            new XElement(A + "spcAft", new XElement(A + "spcPct", new XAttribute("val", 35000))),
                            new XElement(A + "buNone")),
                        new XElement(A + "r",
                            new XElement(A + "rPr",
                                new XAttribute("lang", "en-US"),
                                new XAttribute("sz", textSize),
                                new XAttribute("kern", 1200)),
                            new XElement(A + "t", text))));

            spTree.Add(new XElement(Dsp + "sp",
                new XAttribute("modelId", modelId),
                new XElement(Dsp + "nvSpPr",
                    new XElement(Dsp + "cNvPr", new XAttribute("id", spTree.Elements(Dsp + "sp").Count() + 1), new XAttribute("name", "SmartArt")),
                    new XElement(Dsp + "cNvSpPr")),
                spPr,
                new XElement(Dsp + "style",
                    new XElement(A + "lnRef",
                        new XAttribute("idx", 2),
                        new XElement(A + "scrgbClr",
                            new XAttribute("r", 0),
                            new XAttribute("g", 0),
                            new XAttribute("b", 0))),
                    new XElement(A + "fillRef",
                        new XAttribute("idx", 1),
                        new XElement(A + "scrgbClr",
                            new XAttribute("r", 0),
                            new XAttribute("g", 0),
                            new XAttribute("b", 0))),
                    new XElement(A + "effectRef",
                        new XAttribute("idx", 0),
                        new XElement(A + "scrgbClr",
                            new XAttribute("r", 0),
                            new XAttribute("g", 0),
                            new XAttribute("b", 0))),
                    new XElement(A + "fontRef",
                        new XAttribute("idx", "minor"),
                        new XElement(A + "schemeClr", new XAttribute("val", "lt1")))),
                textBody,
                new XElement(Dsp + "txXfrm",
                    new XElement(A + "off", new XAttribute("x", x), new XAttribute("y", y)),
                    new XElement(A + "ext", new XAttribute("cx", width), new XAttribute("cy", height)))));
        }

        if (isHierarchy || isPyramid)
        {
            if (isHierarchy)
            {
                var levelGap = Math.Max(1L, PointsToEmu(18));
                var hierarchyBoxW = Math.Max(1L, Math.Min(PointsToEmu(112), frameW - 2 * margin));
                var hierarchyBoxH = Math.Max(1L, Math.Min(PointsToEmu(28), frameH - 2 * margin));
                var positions = new Dictionary<int, (long X, long Y)>();

                for (var i = 0; i < nodes.Count; i++)
                {
                    var depth = nodes[i].Depth;
                    var siblings = nodes.Select((item, index) => (item, index))
                        .Where(pair => pair.item.Depth == depth)
                        .ToList();
                    var slot = siblings.FindIndex(pair => pair.index == i);
                    var rowWidth = siblings.Count * hierarchyBoxW + Math.Max(0, siblings.Count - 1) * gap;
                    var x = Math.Max(margin, (frameW - rowWidth) / 2 + slot * (hierarchyBoxW + gap));
                    var y = Math.Min(frameH - margin - hierarchyBoxH, margin + depth * (hierarchyBoxH + levelGap));
                    positions[i] = (x, y);
                }

                for (var i = 0; i < nodes.Count; i++)
                {
                    var parentIndex = nodes.FindIndex(candidate => candidate.Node.Children.Contains(nodes[i].Node));
                    if (parentIndex < 0)
                        continue;

                    var parent = positions[parentIndex];
                    var child = positions[i];
                    var x = Math.Min(parent.X + hierarchyBoxW / 2, child.X + hierarchyBoxW / 2);
                    var y = Math.Min(parent.Y + hierarchyBoxH, child.Y);
                    var width = Math.Max(1L, Math.Abs((parent.X + hierarchyBoxW / 2) - (child.X + hierarchyBoxW / 2)));
                    var height = Math.Max(1L, Math.Abs(child.Y - (parent.Y + hierarchyBoxH)));
                    AddCachedShape(
                        SmartArtModelId(7500 + i),
                        x,
                        y,
                        width,
                        height,
                        "line",
                        "808080",
                        true,
                        lineHex: hierarchyAccentHex);
                }

                for (var i = 0; i < nodes.Count; i++)
                {
                    var position = positions[i];
                    var fillHex = colorScheme.FillHexAt(i).TrimStart('#').ToUpperInvariant();
                    // The cache shape must identify the presentation graph's text node. Word then keeps
                    // the cached geometry instead of replacing it with a disconnected fallback shape.
                    AddCachedShape(
                        SmartArtModelId(9000 + i * 5 + 3),
                        position.X,
                        position.Y,
                        hierarchyBoxW,
                        hierarchyBoxH,
                        "rect",
                        hierarchyAccentHex,
                        text: nodes[i].Node.Text,
                        lineHex: hierarchyAccentHex);
                }
            }
            else
            {
                var layerGap = Math.Max(1L, PointsToEmu(2));
                var usableH = Math.Max(1L, frameH - 2 * margin);
                var pyramidBoxH = Math.Max(1L,
                    (usableH - layerGap * Math.Max(0, nodes.Count - 1)) / Math.Max(1, nodes.Count));
                var usableW = Math.Max(1L, frameW - 2 * margin);
                for (var i = 0; i < nodes.Count; i++)
                {
                    var width = Math.Max(1L, usableW * (i + 1) / Math.Max(1, nodes.Count));
                    var x = margin + (usableW - width) / 2;
                    var y = margin + i * (pyramidBoxH + layerGap);
                    var fillHex = colorScheme.FillHexAt(i).TrimStart('#').ToUpperInvariant();
                    AddCachedShape(SmartArtModelId(9000 + i * 3 + 1), x, y, width, pyramidBoxH, "trapezoid", fillHex, text: nodes[i].Node.Text);
                }
            }

            return new XDocument(
                new XElement(Dsp + "drawing",
                    new XAttribute(XNamespace.Xmlns + "dsp", Dsp.NamespaceName),
                    new XAttribute(XNamespace.Xmlns + "a", A.NamespaceName),
                    new XAttribute(XNamespace.Xmlns + "r", R.NamespaceName),
                    spTree));
        }

        if (isProcess && nodes.Count > 1)
        {
            var arrowHeight = Math.Max(1L, Math.Min(PointsToEmu(8), boxH / 2));
            for (var i = 1; i < nodes.Count; i++)
            {
                var arrowX = margin + (i - 1) * (boxW + gap) + boxW;
                var arrowY = (frameH - arrowHeight) / 2;
                AddCachedShape(
                    SmartArtModelId(8000 + i - 1),
                    arrowX,
                    arrowY,
                    Math.Max(1L, gap),
                    arrowHeight,
                    "rightArrow",
                    hierarchyAccentHex,
                    lineHex: hierarchyAccentHex);
            }
        }

        for (var i = 0; i < nodes.Count; i++)
        {
            var (node, depth) = nodes[i];
            long x, y;
            switch (isProcess ? SmartArtKind.Process : smartArt.Kind)
            {
                case SmartArtKind.Process:
                    // Horizontal row of boxes (with a gap acting as the arrow space between steps).
                    x = margin + i * (boxW + gap);
                    y = (frameH - boxH) / 2;
                    break;
                case SmartArtKind.Hierarchy:
                    // Simple top-down tree: indent by depth (x) and stack by emission order (y) so children
                    // sit below and to the right of their parent — deterministic and never overlapping.
                    x = Math.Min(frameW - margin - boxW, margin + depth * (boxW / 3 + gap));
                    y = Math.Min(frameH - margin - boxH, margin + i * (boxH + gap));
                    break;
                default: // List: vertical stack of boxes.
                    x = margin;
                    y = Math.Min(frameH - margin - boxH, margin + i * (boxH + gap));
                    break;
            }

            var fillHex = colorScheme.FillHexAt(i).TrimStart('#').ToUpperInvariant();
            var shapeKind = style.CornerRadius > 0 ? "roundRect" : "rect";
            var line = style.BorderThickness > 0
                ? new XElement(A + "ln",
                    new XAttribute("w", Math.Max(1L, PointsToEmu(style.BorderThickness))),
                    SolidFill(fillHex))
                : new XElement(A + "ln", new XElement(A + "noFill"));

            spTree.Add(new XElement(Dsp + "sp",
                new XAttribute("modelId", SmartArtModelId(3001 + i)),
                new XElement(Dsp + "nvSpPr",
                    new XElement(Dsp + "cNvPr",
                        new XAttribute("id", spTree.Elements(Dsp + "sp").Count() + 1),
                        new XAttribute("name", $"Node {i}")),
                    new XElement(Dsp + "cNvSpPr")),
                new XElement(Dsp + "spPr",
                    new XElement(A + "xfrm",
                        new XElement(A + "off", new XAttribute("x", x), new XAttribute("y", y)),
                    new XElement(A + "ext", new XAttribute("cx", boxW), new XAttribute("cy", boxH))),
                    new XElement(A + "prstGeom", new XAttribute("prst", shapeKind),
                        new XElement(A + "avLst")),
                    SolidFill(fillHex),
                    line),
                new XElement(Dsp + "txBody",
                    new XElement(A + "bodyPr"),
                    new XElement(A + "lstStyle"),
                    new XElement(A + "p",
                        new XElement(A + "r",
                            new XElement(A + "rPr",
                                new XAttribute("lang", "en-US"),
                                new XAttribute("sz", 1100),
                                SolidFill(textHex)),
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
    /// Builds a SmartArt LAYOUT part (word/diagrams/layoutN.xml — dgm:layoutDef). Stock hierarchy/pyramid
    /// templates are also used as the known Word presentation scaffold for flat galleries so Word materialises
    /// visible nodes instead of falling back to the diagram's semantic text only.
    /// </summary>
    private static XDocument BuildDiagramLayout(SmartArt smartArt)
    {
        var layoutUrn = SmartArtLayoutUrn(smartArt);
        var category = SmartArtLayoutCategory(smartArt);
        var isHierarchy = string.Equals(category, "hierarchy", StringComparison.Ordinal);
        var isPyramid = string.Equals(category, "pyramid", StringComparison.Ordinal);
        if (smartArt.Kind == SmartArtKind.Process && string.Equals(SmartArtLayoutSuffix(smartArt), "process1", StringComparison.OrdinalIgnoreCase))
            return LoadSmartArtLayoutTemplate("process1.xml", layoutUrn, category);
        if (isHierarchy || isPyramid || UsesFlatSmartArtGallery(smartArt))
        {
            var template = string.Equals(SmartArtLayoutSuffix(smartArt), "orgChart1", StringComparison.Ordinal)
                ? "orgChart1.xml"
                : isHierarchy || UsesFlatSmartArtGallery(smartArt) ? "hierarchy1.xml" : "pyramid1.xml";
            return LoadSmartArtLayoutTemplate(template, layoutUrn, category);
        }
        var rootAlgorithm = isHierarchy
            ? "hierRoot"
            : isPyramid
                ? "pyra"
                : smartArt.Kind == SmartArtKind.Process ? "lin" : "snake";
        var topLevelCount = Math.Max(1, smartArt.Nodes.Count);
        var stylePoints = Enumerable.Range(0, topLevelCount)
            .Select(i => new XElement(Dgm + "pt", new XAttribute("modelId", SmartArtModelId(i + 1))))
            .ToArray();
        var styleConnections = Enumerable.Range(0, Math.Max(0, topLevelCount))
            .Select(i => new XElement(Dgm + "cxn",
                new XAttribute("modelId", SmartArtModelId(100 + i)),
                new XAttribute("srcId", SmartArtModelId(0)),
                new XAttribute("destId", SmartArtModelId(i + 1)),
                new XAttribute("srcOrd", i),
                new XAttribute("destOrd", 0)))
            .ToArray();
        var colorPoints = Enumerable.Range(0, topLevelCount + 1)
            .Select(i => new XElement(Dgm + "pt", new XAttribute("modelId", SmartArtModelId(i))))
            .ToArray();
        var colorConnections = Enumerable.Range(0, topLevelCount)
            .Select(i => new XElement(Dgm + "cxn",
                new XAttribute("modelId", SmartArtModelId(200 + i)),
                new XAttribute("srcId", SmartArtModelId(0)),
                new XAttribute("destId", SmartArtModelId(i + 1)),
                new XAttribute("srcOrd", i),
                new XAttribute("destOrd", 0)))
            .ToArray();

        var elem = new XElement(Dgm + "layoutDef",
            new XAttribute(XNamespace.Xmlns + "dgm", Dgm.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "a", A.NamespaceName),
            new XAttribute("uniqueId", layoutUrn),
            new XElement(Dgm + "title", new XAttribute("val", string.Empty)),
            new XElement(Dgm + "desc", new XAttribute("val", string.Empty)),
            new XElement(Dgm + "catLst", new XElement(Dgm + "cat", new XAttribute("type", category), new XAttribute("pri", 400))),
            new XElement(Dgm + "styleData",
                new XElement(Dgm + "dataModel",
                    new XElement(Dgm + "ptLst", new XElement(Dgm + "pt", new XAttribute("modelId", SmartArtModelId(0)), new XAttribute("type", "doc")), stylePoints),
                    new XElement(Dgm + "cxnLst", styleConnections),
                    new XElement(Dgm + "bg"),
                    new XElement(Dgm + "whole"))),
            new XElement(Dgm + "clrData",
                new XElement(Dgm + "dataModel",
                    new XElement(Dgm + "ptLst", colorPoints),
                    new XElement(Dgm + "cxnLst", colorConnections),
                    new XElement(Dgm + "bg"),
                    new XElement(Dgm + "whole"))),
            new XElement(Dgm + "layoutNode",
                new XAttribute("name", "diagram"),
                new XElement(Dgm + "varLst",
                    new XElement(Dgm + "dir"),
                    new XElement(Dgm + "resizeHandles", new XAttribute("val", "exact"))),
                new XElement(Dgm + "alg",
                    new XAttribute("type", rootAlgorithm),
                    new XElement(Dgm + "param", new XAttribute("type", "grDir"), new XAttribute("val", "tL")),
                    new XElement(Dgm + "param", new XAttribute("type", "flowDir"), new XAttribute("val", "row")),
                    new XElement(Dgm + "param", new XAttribute("type", "contDir"), new XAttribute("val", "sameDir")),
                    new XElement(Dgm + "param", new XAttribute("type", "off"), new XAttribute("val", "ctr"))),
                new XElement(Dgm + "shape",
                    new XAttribute(XNamespace.Xmlns + "r", R.NamespaceName),
                    new XAttribute(R + "blip", string.Empty),
                    new XElement(Dgm + "adjLst")),
                new XElement(Dgm + "presOf"),
                new XElement(Dgm + "constrLst",
                    new XElement(Dgm + "constr", new XAttribute("type", "w"), new XAttribute("for", "ch"), new XAttribute("forName", "node"), new XAttribute("refType", "w")),
                    new XElement(Dgm + "constr", new XAttribute("type", "h"), new XAttribute("for", "ch"), new XAttribute("forName", "node"), new XAttribute("refType", "w"), new XAttribute("refFor", "ch"), new XAttribute("refForName", "node"), new XAttribute("fact", "0.6")),
                    new XElement(Dgm + "constr", new XAttribute("type", "w"), new XAttribute("for", "ch"), new XAttribute("forName", "sibTrans"), new XAttribute("refType", "w"), new XAttribute("refFor", "ch"), new XAttribute("refForName", "node"), new XAttribute("fact", "0.1")),
                    new XElement(Dgm + "constr", new XAttribute("type", "sp"), new XAttribute("refType", "w"), new XAttribute("refFor", "ch"), new XAttribute("refForName", "sibTrans")),
                    new XElement(Dgm + "constr", new XAttribute("type", "primFontSz"), new XAttribute("for", "ch"), new XAttribute("forName", "node"), new XAttribute("op", "equ"), new XAttribute("val", 65))),
                new XElement(Dgm + "ruleLst"),
                new XElement(Dgm + "forEach",
                    new XAttribute("name", "children"),
                    new XAttribute("axis", "ch"),
                    new XAttribute("ptType", "node"),
                    new XElement(Dgm + "layoutNode",
                        new XAttribute("name", "node"),
                        new XElement(Dgm + "varLst", new XElement(Dgm + "bulletEnabled", new XAttribute("val", 1))),
                        new XElement(Dgm + "alg", new XAttribute("type", isHierarchy ? "hierChild" : "tx")),
                        new XElement(Dgm + "shape",
                            new XAttribute("type", isPyramid ? "trapezoid" : "rect"),
                            new XAttribute(XNamespace.Xmlns + "r", R.NamespaceName),
                            new XAttribute(R + "blip", string.Empty),
                            new XElement(Dgm + "adjLst")),
                        new XElement(Dgm + "presOf", new XAttribute("axis", "desOrSelf"), new XAttribute("ptType", "node")),
                        new XElement(Dgm + "constrLst",
                            new XElement(Dgm + "constr", new XAttribute("type", "lMarg"), new XAttribute("refType", "primFontSz"), new XAttribute("fact", "0.3")),
                            new XElement(Dgm + "constr", new XAttribute("type", "rMarg"), new XAttribute("refType", "primFontSz"), new XAttribute("fact", "0.3")),
                            new XElement(Dgm + "constr", new XAttribute("type", "tMarg"), new XAttribute("refType", "primFontSz"), new XAttribute("fact", "0.3")),
                            new XElement(Dgm + "constr", new XAttribute("type", "bMarg"), new XAttribute("refType", "primFontSz"), new XAttribute("fact", "0.3"))),
                        new XElement(Dgm + "ruleLst", new XElement(Dgm + "rule", new XAttribute("type", "primFontSz"), new XAttribute("val", 5), new XAttribute("fact", "NaN"), new XAttribute("max", "NaN")))),
                    smartArt.Kind == SmartArtKind.Process
                        ? new XElement(Dgm + "forEach",
                            new XAttribute("name", "connections"),
                            new XAttribute("axis", "ch"),
                            new XAttribute("ptType", "parTrans"),
                            new XAttribute("cnt", 1),
                            new XElement(Dgm + "layoutNode",
                                new XAttribute("name", "connector"),
                                new XElement(Dgm + "alg",
                                    new XAttribute("type", "conn"),
                                    new XElement(Dgm + "param", new XAttribute("type", "connRout"), new XAttribute("val", "straight")),
                                    new XElement(Dgm + "param", new XAttribute("type", "begPts"), new XAttribute("val", "rect")),
                                    new XElement(Dgm + "param", new XAttribute("type", "endPts"), new XAttribute("val", "rect")),
                                    new XElement(Dgm + "param", new XAttribute("type", "endSty"), new XAttribute("val", "arr"))),
                                new XElement(Dgm + "shape",
                                    new XAttribute("type", "conn"),
                                    new XAttribute(XNamespace.Xmlns + "r", R.NamespaceName),
                                    new XAttribute(R + "blip", string.Empty),
                                    new XElement(Dgm + "adjLst")),
                                new XElement(Dgm + "presOf"),
                                new XElement(Dgm + "constrLst"),
                                new XElement(Dgm + "ruleLst")))
                        : null,
                    new XElement(Dgm + "forEach",
                        new XAttribute("name", "siblings"),
                        new XAttribute("axis", "followSib"),
                        new XAttribute("ptType", "sibTrans"),
                        new XAttribute("cnt", 1),
                        new XElement(Dgm + "layoutNode",
                            new XAttribute("name", "sibTrans"),
                            new XElement(Dgm + "alg", new XAttribute("type", "sp")),
                            new XElement(Dgm + "shape", new XAttribute(XNamespace.Xmlns + "r", R.NamespaceName), new XAttribute(R + "blip", string.Empty), new XElement(Dgm + "adjLst")),
                            new XElement(Dgm + "presOf"),
                            new XElement(Dgm + "constrLst"),
                            new XElement(Dgm + "ruleLst"))))));
        return new XDocument(elem);
    }

    private static XDocument LoadSmartArtLayoutTemplate(string fileName, string layoutUrn, string category)
    {
        var resourceName = "FreeW.Core.IO.SmartArtLayoutTemplates." + fileName;
        using var stream = typeof(DocxWriter).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException("Embedded SmartArt layout template not found: " + resourceName);
        var layout = XDocument.Load(stream);
        var root = layout.Root ?? throw new InvalidOperationException("SmartArt layout template has no root: " + resourceName);
        root.SetAttributeValue("uniqueId", layoutUrn);
        root.Element(Dgm + "catLst")?.Element(Dgm + "cat")?.SetAttributeValue("type", category);
        return layout;
    }

    /// <summary>
    /// Builds a SmartArt QUICKSTYLE part (word/diagrams/quickStyleN.xml — dgm:styleDef).
    /// Persists the FreeW <see cref="SmartArt.StyleId"/> in a schema-valid extension list so the reader can
    /// recover the exact catalog entry on round-trip.
    /// </summary>
    private static readonly string[] SmartArtGalleryStyleLabels =
    [
        "node0", "lnNode1", "vennNode1", "alignNode1", "node1", "node2", "node3", "node4",
        "fgImgPlace1", "alignImgPlace1", "bgImgPlace1", "sibTrans2D1", "fgSibTrans2D1",
        "bgSibTrans2D1", "sibTrans1D1", "callout", "asst0", "asst1", "asst2", "asst3", "asst4",
        "parChTrans2D1", "parChTrans2D2", "parChTrans2D3", "parChTrans2D4", "parChTrans1D1",
        "parChTrans1D2", "parChTrans1D3", "parChTrans1D4", "fgAcc1", "conFgAcc1", "alignAcc1",
        "trAlignAcc1", "bgAcc1", "solidFgAcc1", "solidAlignAcc1", "solidBgAcc1",
        "fgAccFollowNode1", "alignAccFollowNode1", "bgAccFollowNode1", "fgAcc0", "fgAcc2", "fgAcc3",
        "fgAcc4", "bgShp", "dkBgShp", "trBgShp", "fgShp", "revTx"
    ];

    private static string SmartArtQuickStyleSuffix(SmartArt smartArt) => "simple1";

    private static bool UsesFlatSmartArtGallery(SmartArt smartArt) =>
        SmartArtLayoutCategory(smartArt) is "list" or "process";

    private static XDocument BuildDiagramQuickStyle(SmartArt smartArt)
    {
        XNamespace freew = "http://schemas.freew.dev/smartart-gallery/2026";
        var labels = SmartArtGalleryStyleLabels;
        var usesFlatGallery = false;
        const string BaseUrn = "urn:microsoft.com/office/officeart/2005/8/quickstyle/";
        var uniqueId = BaseUrn + SmartArtQuickStyleSuffix(smartArt);
        var styleLabels = usesFlatGallery
            ? new List<XElement> { new XElement(Dgm + "styleLbl", new XAttribute("name", "node0")) }
            : labels.Select(name =>
        {
            var lineIndex = name is "sibTrans2D1" or "fgSibTrans2D1" or "bgSibTrans2D1"
                ? 0
                : name is "sibTrans1D1" or "trAlignAcc1"
                    ? 1
                    : name is "bgShp" or "dkBgShp" or "trBgShp" or "revTx"
                        ? 0
                        : 2;
            var fillIndex = name is "sibTrans1D1" or "parChTrans1D1" or "parChTrans1D2" or "parChTrans1D3" or "parChTrans1D4" or "revTx"
                ? 0
                : 1;
            var fontValue = name == "vennNode1"
                ? "tx1"
                : name is "node0" or "lnNode1" or "alignNode1" or "node1" or "node2" or "node3" or "node4"
                    or "sibTrans2D1" or "fgSibTrans2D1" or "bgSibTrans2D1"
                    or "asst0" or "asst1" or "asst2" or "asst3" or "asst4"
                    or "parChTrans2D1" or "parChTrans2D2" or "parChTrans2D3" or "parChTrans2D4"
                    ? "lt1"
                    : null;
            return new XElement(Dgm + "styleLbl",
                new XAttribute("name", name),
                new XElement(Dgm + "scene3d",
                    new XElement(A + "camera", new XAttribute("prst", "orthographicFront")),
                    new XElement(A + "lightRig", new XAttribute("rig", "threePt"), new XAttribute("dir", "t"))),
                new XElement(Dgm + "sp3d"),
                new XElement(Dgm + "txPr"),
                new XElement(Dgm + "style",
                    new XElement(A + "lnRef", new XAttribute("idx", lineIndex),
                        new XElement(A + "scrgbClr", new XAttribute("r", 0), new XAttribute("g", 0), new XAttribute("b", 0))),
                    new XElement(A + "fillRef", new XAttribute("idx", fillIndex),
                        new XElement(A + "scrgbClr", new XAttribute("r", 0), new XAttribute("g", 0), new XAttribute("b", 0))),
                    new XElement(A + "effectRef", new XAttribute("idx", 0),
                        new XElement(A + "scrgbClr", new XAttribute("r", 0), new XAttribute("g", 0), new XAttribute("b", 0))),
                    new XElement(A + "fontRef",
                        new XAttribute("idx", "minor"),
                        fontValue is null ? null : new XElement(A + "schemeClr", new XAttribute("val", fontValue)))));
        }).ToList();
        var elem = new XElement(Dgm + "styleDef",
            new XAttribute(XNamespace.Xmlns + "dgm", Dgm.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "a", A.NamespaceName),
            new XAttribute("uniqueId", uniqueId),
            new XElement(Dgm + "title", new XAttribute("val", string.Empty)),
            new XElement(Dgm + "desc", new XAttribute("val", string.Empty)),
            usesFlatGallery
                ? new XElement(Dgm + "catLst")
                : new XElement(Dgm + "catLst",
                    new XElement(Dgm + "cat", new XAttribute("type", "simple"), new XAttribute("pri", 10100))),
            new XElement(Dgm + "scene3d",
                new XElement(A + "camera", new XAttribute("prst", "orthographicFront")),
                new XElement(A + "lightRig", new XAttribute("rig", "threePt"), new XAttribute("dir", "t"))),
            styleLabels,
            smartArt.StyleId is null
                ? null
                : new XElement(Dgm + "extLst",
                    new XElement(A + "ext",
                        new XAttribute("uri", "{FW-SmartArtGallery-2026}"),
                        new XElement(freew + "style", new XAttribute("id", smartArt.StyleId)))));
        return new XDocument(elem);
    }

    /// <summary>
    /// Builds a SmartArt COLORS part (word/diagrams/colorsN.xml — dgm:colorsDef).
    /// Persists the FreeW <see cref="SmartArt.ColorSchemeId"/> in a schema-valid extension list so the reader
    /// can recover the exact catalog entry on round-trip.
    /// </summary>
    private static XDocument BuildDiagramColors(SmartArt smartArt)
    {
        XNamespace freew = "http://schemas.freew.dev/smartart-gallery/2026";
        const string BaseUrn = "urn:microsoft.com/office/officeart/2005/8/colors/";
        var colorId = smartArt.ColorSchemeId ?? "accent1_2";
        var uniqueId = BaseUrn + SmartArtColorGallerySuffix(smartArt);
        var usesFlatGallery = false;
        var category = SmartArtColorCategory(colorId);
        var colorScheme = SmartArtColorScheme.FindById(smartArt.ColorSchemeId ?? string.Empty)
            ?? SmartArtColorScheme.Default;
        var accentHex = (smartArt.ColorSchemeId is null ? "156082" : colorScheme.FillHexAt(0))
            .TrimStart('#')
            .ToUpperInvariant();
        XElement Scheme(string value, params (string Name, string Value)[] modifiers) =>
            new(A + "schemeClr",
                new XAttribute("val", value),
                modifiers.Select(modifier => new XElement(A + modifier.Name, new XAttribute("val", modifier.Value))));

        XElement ColorList(string name, string value, params (string Name, string Value)[] modifiers) =>
            new(Dgm + name,
                new XAttribute("meth", "repeat"),
                !usesFlatGallery && string.Equals(value, category, StringComparison.OrdinalIgnoreCase)
                    ? new XElement(A + "srgbClr",
                        new XAttribute("val", accentHex),
                        modifiers.Select(modifier => new XElement(A + modifier.Name, new XAttribute("val", modifier.Value))))
                    : Scheme(value, modifiers));

        XElement EmptyList(string name) => new(Dgm + name);

        IEnumerable<(string Value, (string Name, string Value)[] Modifiers)> ColorRule(string name)
        {
            var fill = (Value: category, Modifiers: Array.Empty<(string Name, string Value)>());
            var line = (Value: "lt1", Modifiers: Array.Empty<(string Name, string Value)>());
            var textFill = (Value: (string?)null, Modifiers: Array.Empty<(string Name, string Value)>());

            if (usesFlatGallery && name == "node0")
            {
                line = (category, Array.Empty<(string, string)>());
                textFill = ("lt1", Array.Empty<(string, string)>());
            }
            else if (!usesFlatGallery && name is "node0" or "node1" or "node2" or "node3" or "node4")
            {
                line = (category, Array.Empty<(string, string)>());
            }
            else if (name == "vennNode1")
                fill = (category, new[] { ("alpha", "50000") });
            else if (name is "fgImgPlace1" or "alignImgPlace1" or "bgImgPlace1")
            {
                fill = (category, new[] { ("tint", "50000") });
                textFill = ("lt1", Array.Empty<(string, string)>());
            }
            else if (name is "sibTrans2D1" or "fgSibTrans2D1" or "bgSibTrans2D1")
            {
                fill = (category, new[] { ("tint", "60000") });
                line = (category, new[] { ("tint", "60000") });
            }
            else if (name == "sibTrans1D1")
            {
                line = (category, new[] { ("shade", "60000") });
                textFill = ("tx1", Array.Empty<(string, string)>());
            }
            else if (name == "callout")
            {
                line = (category, new[] { ("tint", "50000") });
                textFill = ("tx1", Array.Empty<(string, string)>());
            }
            else if (name == "parChTrans2D1")
            {
                fill = (category, new[] { ("tint", "60000") });
                line = (category, new[] { ("tint", "60000") });
                textFill = ("lt1", Array.Empty<(string, string)>());
            }
            else if (name is "parChTrans2D2" or "parChTrans2D3" or "parChTrans2D4")
            {
                line = (category, Array.Empty<(string, string)>());
                textFill = ("lt1", Array.Empty<(string, string)>());
            }
            else if (name is "parChTrans1D1" or "parChTrans1D2")
            {
                line = (category, new[] { ("shade", "60000") });
                textFill = ("tx1", Array.Empty<(string, string)>());
            }
            else if (name is "parChTrans1D3" or "parChTrans1D4")
            {
                line = (category, new[] { ("shade", "80000") });
                textFill = ("tx1", Array.Empty<(string, string)>());
            }
            else if (name is "fgAcc1" or "conFgAcc1" or "alignAcc1" or "bgAcc1" or "fgAcc0" or "fgAcc2" or "fgAcc3" or "fgAcc4")
            {
                fill = ("lt1", new[] { ("alpha", "90000") });
                line = (category, Array.Empty<(string, string)>());
                textFill = ("dk1", Array.Empty<(string, string)>());
            }
            else if (name == "trAlignAcc1")
            {
                fill = ("lt1", new[] { ("alpha", "40000") });
                line = (category, Array.Empty<(string, string)>());
                textFill = ("dk1", Array.Empty<(string, string)>());
            }
            else if (name is "solidFgAcc1" or "solidAlignAcc1" or "solidBgAcc1")
            {
                fill = ("lt1", Array.Empty<(string, string)>());
                line = (category, Array.Empty<(string, string)>());
                textFill = ("dk1", Array.Empty<(string, string)>());
            }
            else if (name is "fgAccFollowNode1" or "alignAccFollowNode1" or "bgAccFollowNode1")
            {
                fill = (category, new[] { ("alpha", "90000"), ("tint", "40000") });
                line = (category, new[] { ("alpha", "90000"), ("tint", "40000") });
                textFill = ("dk1", Array.Empty<(string, string)>());
            }
            else if (name == "bgShp")
            {
                fill = (category, new[] { ("tint", "40000") });
                line = (category, Array.Empty<(string, string)>());
                textFill = ("dk1", Array.Empty<(string, string)>());
            }
            else if (name == "dkBgShp")
            {
                fill = (category, new[] { ("shade", "80000") });
                line = (category, Array.Empty<(string, string)>());
                textFill = ("lt1", Array.Empty<(string, string)>());
            }
            else if (name == "trBgShp")
            {
                fill = (category, new[] { ("tint", "50000"), ("alpha", "40000") });
                line = (category, Array.Empty<(string, string)>());
                textFill = ("lt1", Array.Empty<(string, string)>());
            }
            else if (name == "fgShp")
            {
                fill = (category, new[] { ("tint", "60000") });
                textFill = ("dk1", Array.Empty<(string, string)>());
            }
            else if (name == "revTx")
            {
                fill = ("lt1", new[] { ("alpha", "0") });
                line = ("dk1", new[] { ("alpha", "0") });
                textFill = ("tx1", Array.Empty<(string, string)>());
            }

            yield return (fill.Value, fill.Modifiers);
            yield return (line.Value, line.Modifiers);
            yield return (textFill.Value ?? string.Empty, textFill.Modifiers);
        }

        var styleLabels = (usesFlatGallery ? new[] { "node0" } : SmartArtGalleryStyleLabels).Select(name =>
        {
            var rules = ColorRule(name).ToArray();
            var fill = rules[0];
            var line = rules[1];
            var textFill = rules[2];
            return new XElement(Dgm + "styleLbl", new XAttribute("name", name),
                ColorList("fillClrLst", fill.Value, fill.Modifiers),
                ColorList("linClrLst", line.Value, line.Modifiers),
                EmptyList("effectClrLst"),
                EmptyList("txLinClrLst"),
                string.IsNullOrEmpty(textFill.Value) ? EmptyList("txFillClrLst") : ColorList("txFillClrLst", textFill.Value, textFill.Modifiers),
                EmptyList("txEffectClrLst"));
        }).ToList();
        var elem = new XElement(Dgm + "colorsDef",
            new XAttribute(XNamespace.Xmlns + "dgm", Dgm.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "a", A.NamespaceName),
            new XAttribute("uniqueId", uniqueId),
            new XElement(Dgm + "title", new XAttribute("val", string.Empty)),
            new XElement(Dgm + "desc", new XAttribute("val", string.Empty)),
            usesFlatGallery
                ? new XElement(Dgm + "catLst")
                : new XElement(Dgm + "catLst",
                    new XElement(Dgm + "cat", new XAttribute("type", category), new XAttribute("pri", 11200))),
            styleLabels,
            smartArt.ColorSchemeId is null
                ? null
                : new XElement(Dgm + "extLst",
                    new XElement(A + "ext",
                        new XAttribute("uri", "{FW-SmartArtGallery-2026}"),
                        new XElement(freew + "colorScheme", new XAttribute("id", smartArt.ColorSchemeId)))));
        return new XDocument(elem);
    }

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
        var quickLayout = chart.QuickLayoutId > 0
            ? ChartQuickLayout.FindById(chart.QuickLayoutId)
            : null;
        var style = chart.StyleId > 0
            ? ChartStyle.FindById(chart.StyleId)
            : ChartStyle.Default;
        var showTitle = quickLayout?.ShowTitle ?? true;
        var showLegend = quickLayout?.ShowLegend ?? chart.ShowLegend;
        var showAxisTitles = quickLayout?.ShowAxisTitles ?? true;
        var showGridlines = chart.NativeVisualSettings?.ShowGridlines
            ?? quickLayout?.ShowGridlines
            ?? style?.ShowGridlines
            ?? ChartStyle.Default.ShowGridlines;
        var categoryAxisTitle = showAxisTitles ? chart.CategoryAxisTitle : null;
        var valueAxisTitle = showAxisTitles ? chart.ValueAxisTitle : null;
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
                ? BuildValueAxis(catAxisId, valAxisId, "b", categoryAxisTitle, showGridlines: false)
                : BuildCategoryAxis(catAxisId, valAxisId, categoryAxisTitle));
            plotArea.Add(BuildValueAxis(valAxisId, catAxisId, "l", valueAxisTitle, showGridlines));
        }
        if (ChartHasPlotAreaFill(chart))
            plotArea.Add(BuildChartPlotAreaProperties());

        var chartElement = new XElement(C + "chart");
        if (showTitle && chart.Title is { Length: > 0 } title)
        {
            chartElement.Add(BuildChartTitle(title));
            chartElement.Add(new XElement(C + "autoTitleDeleted", new XAttribute("val", "0")));
        }
        else
        {
            chartElement.Add(new XElement(C + "autoTitleDeleted", new XAttribute("val", "1")));
        }
        chartElement.Add(plotArea);
        if (showLegend)
            chartElement.Add(new XElement(C + "legend",
                new XElement(C + "legendPos", new XAttribute("val", "b")),
                new XElement(C + "overlay", new XAttribute("val", "0"))));
        chartElement.Add(new XElement(C + "plotVisOnly", new XAttribute("val", "1")));

        // c:style — persists the selected ChartStyle id so Word and the reader can recover it.
        // Omitted when StyleId == 0 (default — no explicit style chosen).
        XElement? styleElement = chart.StyleId > 0
            ? new XElement(C + "style", new XAttribute("val", chart.StyleId))
            : null;

        // c:ext accepts a private URI, but not arbitrary child XML according to the Office chart
        // schema. Keep the FreeW gallery state in that URI so Word can open the chart without repair.
        XElement? extLst = null;
        if (!string.IsNullOrEmpty(chart.ColorSchemeId) || chart.QuickLayoutId > 0)
        {
            extLst = new XElement(C + "extLst",
                new XElement(C + "ext",
                    new XAttribute("uri", BuildFreeWChartDesignExtensionUri(chart))));
        }

        return new XDocument(
            new XElement(C + "chartSpace",
                new XAttribute(XNamespace.Xmlns + "c", C.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "a", A.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "r", R.NamespaceName),
                styleElement,
                chartElement,
                // c:externalData ties the chart to its editable companion workbook (resolved via the chart
                // part's own _rels). autoUpdate=0 keeps the literal caches authoritative for display.
                new XElement(C + "externalData",
                    new XAttribute(R + "id", part.ExternalDataRelId),
                    new XElement(C + "autoUpdate", new XAttribute("val", "0"))),
                extLst));
    }

    private static string BuildFreeWChartDesignExtensionUri(Chart chart)
    {
        var values = new List<string>();
        if (!string.IsNullOrEmpty(chart.ColorSchemeId))
            values.Add("colorScheme=" + Uri.EscapeDataString(chart.ColorSchemeId));
        if (chart.QuickLayoutId > 0)
            values.Add("quickLayout=" + chart.QuickLayoutId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return values.Count == 0
            ? FreeWChartDesignExtensionUri
            : FreeWChartDesignExtensionUri + "#" + string.Join("&", values);
    }

    /// <summary>
    /// Builds the chart part's own relationships part (word/charts/_rels/chartN.xml.rels): a single "package"
    /// relationship from the chart to its embedded editable workbook (../embeddings/…xlsx). This is what Word's
    /// "Edit Data" follows from the chart, and it is what c:externalData/@r:id resolves against. F1.
    /// </summary>
    private static XDocument BuildChartRels(ChartPart part) => new(
        OpcRelationships.CreateRoot(
            OpcRelationships.CreateRelationship(
                part.ExternalDataRelId,
                ExternalDataRelType,
                "../embeddings/" + part.EmbeddingFileName)));

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
            OpcRelationships.CreateRoot(
                OpcRelationships.CreateRelationship(
                    "rId1",
                    "http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet",
                    "worksheets/sheet1.xml")));

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
            OpcRelationships.CreateRoot(
                OpcRelationships.CreateRelationship(
                    "rId1",
                    OfficeDocumentRel,
                    "xl/workbook.xml")));

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
            new XElement(C + "overlay", new XAttribute("val", "0")));

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
                new XElement(C + "grouping", new XAttribute("val", "standard")));
        }
        else if (chart.Kind == ChartKind.Area)
        {
            root = new XElement(C + "areaChart",
                new XElement(C + "grouping", new XAttribute("val", "standard")));
        }
        else
        {
            root = new XElement(C + "barChart",
                new XElement(C + "barDir", new XAttribute("val", chart.Kind == ChartKind.Bar ? "bar" : "col")),
                new XElement(C + "grouping", new XAttribute("val", "clustered")));
        }

        for (var i = 0; i < chart.Series.Count; i++)
            root.Add(BuildSeries(chart, chart.Series[i], i));

        if (ChartShowsDataLabels(chart))
            root.Add(BuildChartDataLabels());

        root.Add(new XElement(C + "axId", new XAttribute("val", catAxisId)));
        root.Add(new XElement(C + "axId", new XAttribute("val", valAxisId)));
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
            new XElement(C + "scatterStyle", new XAttribute("val",
                chart.NativeVisualSettings?.ScatterConnectsPoints == true ? "lineMarker" : "marker")));
        for (var i = 0; i < chart.Series.Count; i++)
            root.Add(BuildScatterSeries(chart, chart.Series[i], i));
        if (ChartShowsDataLabels(chart))
            root.Add(BuildChartDataLabels());
        root.Add(new XElement(C + "axId", new XAttribute("val", xAxisId)));
        root.Add(new XElement(C + "axId", new XAttribute("val", yAxisId)));
        return root;
    }

    /// <summary>Builds the c:pieChart / c:doughnutChart element holding the chart's first series (no axes).</summary>
    private static XElement BuildPieChart(Chart chart, bool doughnut)
    {
        var pie = new XElement(C + (doughnut ? "doughnutChart" : "pieChart"),
            new XElement(C + "varyColors", new XAttribute("val", "1")));
        if (chart.Series.Count > 0)
            pie.Add(BuildSeries(chart, chart.Series[0], 0));
        if (ChartShowsDataLabels(chart))
            pie.Add(BuildChartDataLabels());
        if (doughnut)
            pie.Add(new XElement(C + "holeSize", new XAttribute("val", "50")));
        return pie;
    }

    private static bool ChartShowsDataLabels(Chart chart)
    {
        if (chart.NativeVisualSettings is not null)
            return chart.NativeVisualSettings.ShowDataLabels;

        if (chart.QuickLayoutId > 0)
            return ChartQuickLayout.FindById(chart.QuickLayoutId)?.ShowDataLabels == true;

        return chart.StyleId > 0
            && ChartStyle.FindById(chart.StyleId)?.ShowDataLabels == true;
    }

    private static XElement BuildChartDataLabels() =>
        new(C + "dLbls",
            new XElement(C + "showLegendKey", new XAttribute("val", "0")),
            new XElement(C + "showVal", new XAttribute("val", "1")),
            new XElement(C + "showCatName", new XAttribute("val", "0")),
            new XElement(C + "showSerName", new XAttribute("val", "0")));

    private static bool ChartHasPlotAreaFill(Chart chart) =>
        chart.NativeVisualSettings?.HasPlotAreaFill
        ?? (chart.StyleId > 0 && ChartStyle.FindById(chart.StyleId)?.PlotAreaFill == true);

    private static XElement BuildChartPlotAreaProperties() =>
        new(C + "spPr",
            new XElement(A + "solidFill",
                new XElement(A + "srgbClr", new XAttribute("val", "D9E2F3"))),
            new XElement(A + "ln", new XElement(A + "noFill")));

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
            new XElement(C + "idx", new XAttribute("val", index)),
            new XElement(C + "order", new XAttribute("val", index)));

        ser.Add(BuildSeriesName(series, index));
        if (UsesPerPointPalette(chart))
            ser.Add(BuildDataPointProperties(chart, series));
        else
            ser.Add(BuildSeriesProperties(chart, index));
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
            new XElement(C + "idx", new XAttribute("val", index)),
            new XElement(C + "order", new XAttribute("val", index)));

        ser.Add(BuildSeriesName(series, index));
        if (UsesPerPointPalette(chart))
            ser.Add(BuildDataPointProperties(chart, series));
        else
            ser.Add(BuildSeriesProperties(chart, index));

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

    private static bool UsesPerPointPalette(Chart chart) =>
        chart.Series.Count == 1
        && chart.Kind is ChartKind.Column or ChartKind.Bar or ChartKind.Scatter or ChartKind.Pie or ChartKind.Doughnut;

    private static IReadOnlyList<XElement> BuildDataPointProperties(Chart chart, ChartSeries series)
    {
        var schemeId = chart.ColorSchemeId?.Trim();
        var scheme = string.IsNullOrEmpty(schemeId) ? null : ChartColorScheme.FindById(schemeId);
        if (scheme is null || scheme.Colors.Count == 0)
            return [];

        var points = new List<XElement>();
        for (var index = 0; index < series.Values.Count; index++)
        {
            var color = scheme.Colors[index % scheme.Colors.Count].Trim().TrimStart('#');
            points.Add(
                new XElement(C + "dPt",
                    new XElement(C + "idx", new XAttribute("val", index)),
                    new XElement(C + "spPr",
                        new XElement(A + "solidFill",
                            new XElement(A + "srgbClr", new XAttribute("val", color))))));
        }

        return points;
    }

    /// <summary>
    /// Persists an explicitly selected FreeW chart colour scheme in standard DrawingML. The extension remains
    /// the lossless FreeW round-trip marker, but Word can only honour the palette when the series also carries
    /// a normal c:spPr fill.
    /// </summary>
    private static XElement? BuildSeriesProperties(Chart chart, int index)
    {
        var schemeId = chart.ColorSchemeId?.Trim();
        var scheme = string.IsNullOrEmpty(schemeId) ? null : ChartColorScheme.FindById(schemeId);
        if (scheme is null || scheme.Colors.Count == 0)
            return null;

        var color = scheme.Colors[index % scheme.Colors.Count].Trim().TrimStart('#');
        return new XElement(C + "spPr",
            new XElement(A + "solidFill",
                new XElement(A + "srgbClr", new XAttribute("val", color))));
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
                    new XElement(C + "ptCount", new XAttribute("val", 1)),
                    new XElement(C + "pt", new XAttribute("idx", 0),
                        new XElement(C + "v", name)))));
    }

    /// <summary>Builds c:cat → c:strRef/c:strCache: the shared category labels as a literal string cache.</summary>
    private static XElement BuildCategoryCache(IReadOnlyList<string> categories)
    {
        var cache = new XElement(C + "strCache",
            new XElement(C + "ptCount", new XAttribute("val", categories.Count)));
        for (var i = 0; i < categories.Count; i++)
            cache.Add(new XElement(C + "pt", new XAttribute("idx", i),
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
            new XElement(C + "ptCount", new XAttribute("val", values.Count)));
        for (var i = 0; i < values.Count; i++)
            cache.Add(new XElement(C + "pt", new XAttribute("idx", i),
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
            new XElement(C + "axId", new XAttribute("val", axisId)),
            new XElement(C + "scaling", new XElement(C + "orientation", new XAttribute("val", "minMax"))),
            new XElement(C + "delete", new XAttribute("val", "0")),
            new XElement(C + "axPos", new XAttribute("val", "b")));
        if (title is { Length: > 0 } t)
            ax.Add(BuildChartTitle(t));
        ax.Add(new XElement(C + "crossAx", new XAttribute("val", crossAxisId)));
        return ax;
    }

    /// <summary>
    /// Builds the c:valAx (value axis) at the given <paramref name="position"/> referencing its own id and
    /// cross-referencing <paramref name="crossAxisId"/>, with an optional axis <paramref name="title"/>. Reused
    /// for the scatter chart's numeric x-axis (position "b").
    /// </summary>
    private static XElement BuildValueAxis(
        long axisId,
        long crossAxisId,
        string position,
        string? title,
        bool showGridlines = false)
    {
        var ax = new XElement(C + "valAx",
            new XElement(C + "axId", new XAttribute("val", axisId)),
            new XElement(C + "scaling", new XElement(C + "orientation", new XAttribute("val", "minMax"))),
            new XElement(C + "delete", new XAttribute("val", "0")),
            new XElement(C + "axPos", new XAttribute("val", position)));
        if (showGridlines)
            ax.Add(new XElement(C + "majorGridlines"));
        if (title is { Length: > 0 } t)
            ax.Add(BuildChartTitle(t));
        ax.Add(new XElement(C + "crossAx", new XAttribute("val", crossAxisId)));
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
        // w:highlight (Word's highlighter) precedes w:u in CT_RPr (EG_RPrBase). Emitted only for a
        // HighlightColorHex that maps to a named gallery token, and only when CharacterShadingHex (which
        // owns the single w:shd slot) is not set — Word's highlight gallery only recognises named tokens.
        // Keep this WordprocessingML color boundary local: named highlight tokens, w:shd fallback, "auto"
        // attributes, and nullable model "#RRGGBB" fields are not strict DrawingML srgbClr normalization.
        if (f.CharacterShadingHex is not { Length: > 0 }
            && f.HighlightColorHex is { Length: > 0 } highlightToken
            && HexToHighlightToken(highlightToken) is { } namedHighlight)
            rPr.Add(new XElement(W + "highlight", new XAttribute(W + "val", namedHighlight)));
        if (f.Underline)
            rPr.Add(new XElement(W + "u", new XAttribute(W + "val", "single")));
        // w:bdr (character border) — a box around the run's glyphs (rPr/w:bdr). EG_RPrBase schema order
        // places w:bdr BEFORE w:shd (and after w:u/w:effect), so we emit it here. Emitted only when set so
        // existing runs round-trip byte-unchanged. Reuses the same edge encoding as w:pBdr (per-edge
        // flags, w:sz in eighths of a point, w:space=0, w:color as RRGGBB).
        if (f.CharacterBorder is { } charBdr)
        {
            var styleToken = BorderLineStyles.ToToken(charBdr.LineStyle);
            XElement BdrEdge(string name) => new(W + name,
                new XAttribute(W + "val", styleToken),
                new XAttribute(W + "sz", PointsToEighthPoints(charBdr.WidthPt)),
                new XAttribute(W + "space", 0),
                new XAttribute(W + "color", charBdr.ColorHex.TrimStart('#')));
            var drawBottom = charBdr.BottomOnly || charBdr.Bottom;
            var drawTop = !charBdr.BottomOnly && charBdr.Top;
            var drawLeft = !charBdr.BottomOnly && charBdr.Left;
            var drawRight = !charBdr.BottomOnly && charBdr.Right;
            if (drawTop || drawLeft || drawBottom || drawRight)
                rPr.Add(new XElement(W + "bdr",
                    drawTop ? BdrEdge("top") : null,
                    drawLeft ? BdrEdge("left") : null,
                    drawBottom ? BdrEdge("bottom") : null,
                    drawRight ? BdrEdge("right") : null));
        }
        // w:shd on a run: CharacterShadingHex (pattern-aware, takes precedence) or HighlightColorHex
        // (legacy solid-fill highlight, w:val="clear"). Both share the single w:shd slot in CT_RPr; when
        // CharacterShadingHex is set it wins so its pattern is preserved in the round-trip.
        if (f.CharacterShadingHex is { Length: > 0 } charShading)
            rPr.Add(new XElement(W + "shd",
                new XAttribute(W + "val", ShadingPatterns.ToToken(f.CharacterShadingPattern)),
                new XAttribute(W + "color", "auto"),
                new XAttribute(W + "fill", charShading.TrimStart('#'))));
        else if (f.HighlightColorHex is { Length: > 0 } highlight)
        {
            // Backward compatibility: also emit w:shd (w:val="clear") so FreeW's own reader round-trips
            // the highlight even when the color has no named token. The matching w:highlight (when the
            // color maps to a named token) is emitted before w:u above to satisfy CT_RPr ordering.
            rPr.Add(new XElement(W + "shd",
                new XAttribute(W + "val", "clear"),
                new XAttribute(W + "color", "auto"),
                new XAttribute(W + "fill", highlight.TrimStart('#'))));
        }
        if (f.VerticalAlign is VerticalAlign.Superscript or VerticalAlign.Subscript)
            rPr.Add(new XElement(W + "vertAlign",
                new XAttribute(W + "val", f.VerticalAlign == VerticalAlign.Superscript ? "superscript" : "subscript")));
        // w:rtl (right-to-left run direction) — a toggle that sits after w:vertAlign in EG_RPrBase, before
        // the w14 extension region. Emitted only when set so default runs round-trip byte-unchanged.
        if (f.Rtl)
            rPr.Add(new XElement(W + "rtl"));
        // w:lang (proofing language) — a BCP-47 tag that sets the spell-check language for the run.
        // Schema order: w:lang sits after w:rtl in EG_RPrBase, before the w14 extension region. Emitted
        // only when set so existing runs round-trip byte-unchanged.
        if (f.LanguageTag is { Length: > 0 } lang)
            rPr.Add(new XElement(W + "lang",
                new XAttribute(W + "val", lang),
                new XAttribute(W + "eastAsia", lang),
                new XAttribute(W + "bidi", lang)));

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
                // Word requires the orientation token for wide page dimensions. Some generated
                // fixtures set the dimensions directly instead of toggling PageSettings.Landscape.
                page.Landscape || page.WidthPt > page.HeightPt
                    ? new XAttribute(W + "orient", "landscape")
                    : null),
            new XElement(W + "pgMar",
                new XAttribute(W + "left", PointsToDxa(page.MarginLeftPt)),
                new XAttribute(W + "right", PointsToDxa(page.MarginRightPt)),
                new XAttribute(W + "top", PointsToDxa(page.MarginTopPt)),
                new XAttribute(W + "bottom", PointsToDxa(page.MarginBottomPt)),
                // Header/footer distance from the page edge (@w:header / @w:footer) and the binding gutter
                // (@w:gutter) are emitted only when set (> 0), so documents that never touched them round-trip
                // byte-unchanged. @w:header/@w:footer carry the schema attribute order (after bottom, before gutter).
                page.HeaderDistancePt > 0 ? new XAttribute(W + "header", PointsToDxa(page.HeaderDistancePt)) : null,
                page.FooterDistancePt > 0 ? new XAttribute(W + "footer", PointsToDxa(page.FooterDistancePt)) : null,
                page.GutterPt > 0 ? new XAttribute(W + "gutter", PointsToDxa(page.GutterPt)) : null),
            // Page border (w:pgBorders): a uniform box on all four edges, offset from the page edge.
            // Emitted only when set; w:sz is in eighths of a point, matching w:pBdr edges.
            BuildPageBorders(page.PageBorder),
            // Line numbering (w:lnNumType): emitted only when enabled. Schema order places it after
            // pgBorders and before cols. @w:countBy is the numbering interval; @w:restart is
            // "continuous" (across pages), "newPage" or "newSection".
            BuildLineNumbering(page),
            // Page numbering (w:pgNumType): emitted only when a section overrides Word's default
            // decimal/continue behaviour. Schema order places it after lnNumType and before cols.
            BuildPageNumbering(page),
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
        var relationships = OpcRelationships.CreateRoot();
        foreach (var image in part.Images)
        {
            if (image.HasEmbeddedPayload)
                relationships.Add(OpcRelationships.CreateRelationship(
                    image.RelationshipId,
                    ImageRel,
                    "media/" + image.FileName));
            if (image.LinkRelationshipId is { } linkRelationshipId)
                relationships.Add(OpcRelationships.CreateRelationship(
                    linkRelationshipId,
                    ImageRel,
                    image.Image.LinkedImageTarget!,
                    external: true));
        }
        foreach (var drawing in part.PreservedDrawings)
            relationships.Add(OpcRelationships.CreateRelationship(
                drawing.RelationshipId,
                drawing.RelationshipType,
                DocumentRelativeTarget(drawing.PartName)));
        foreach (var (url, relationshipId) in part.Hyperlinks)
            relationships.Add(OpcRelationships.CreateRelationship(relationshipId, HyperlinkRel, url, external: true));
        return new XDocument(relationships);
    }

    private static XDocument BuildPartLocalPreservedDrawingRels(
        IReadOnlyList<PartLocalPreservedDrawingPart> preservedDrawings)
    {
        var relationships = OpcRelationships.CreateRoot();
        foreach (var drawing in preservedDrawings)
            relationships.Add(OpcRelationships.CreateRelationship(
                drawing.RelationshipId,
                drawing.RelationshipType,
                DocumentRelativeTarget(drawing.PartName)));
        return new XDocument(relationships);
    }

    private static XDocument BuildNoteRels(
        IReadOnlyList<ImagePart> images,
        IReadOnlyList<PartLocalPreservedDrawingPart> preservedDrawings,
        IReadOnlyDictionary<string, string> hyperlinks)
    {
        var relationships = OpcRelationships.CreateRoot();
        foreach (var image in images)
        {
            if (image.HasEmbeddedPayload)
                relationships.Add(OpcRelationships.CreateRelationship(
                    image.RelationshipId,
                    ImageRel,
                    "media/" + image.FileName));
            if (image.LinkRelationshipId is { } linkRelationshipId)
                relationships.Add(OpcRelationships.CreateRelationship(
                    linkRelationshipId,
                    ImageRel,
                    image.Image.LinkedImageTarget!,
                    external: true));
        }
        foreach (var drawing in preservedDrawings)
            relationships.Add(OpcRelationships.CreateRelationship(
                drawing.RelationshipId,
                drawing.RelationshipType,
                DocumentRelativeTarget(drawing.PartName)));
        foreach (var (url, relationshipId) in hyperlinks)
            relationships.Add(OpcRelationships.CreateRelationship(relationshipId, HyperlinkRel, url, external: true));
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
    /// @w:restart maps the mode to "continuous", "newPage" or "newSection".
    /// </summary>
    private static XElement? BuildLineNumbering(PageSettings page)
    {
        if (page.LineNumberMode == LineNumberMode.None)
            return null;

        var restart = page.LineNumberMode switch
        {
            LineNumberMode.RestartEachPage => "newPage",
            LineNumberMode.RestartEachSection => "newSection",
            _ => "continuous",
        };
        return new XElement(W + "lnNumType",
            new XAttribute(W + "countBy", Math.Max(1, page.LineNumberCountBy)),
            new XAttribute(W + "restart", restart),
            new XAttribute(W + "start", Math.Max(1, page.LineNumberStartAt)));
    }

    private static XElement? BuildPageNumbering(PageSettings page)
    {
        var hasFormat = page.PageNumberFormat != PageNumberFormat.Decimal;
        var hasStart = page.PageNumberStartAt is > 0;
        var hasChapter = page.PageNumberChapterStyleLevel is >= 1 and <= 9;
        if (!hasFormat && !hasStart && !hasChapter)
            return null;

        return new XElement(W + "pgNumType",
            hasFormat ? new XAttribute(W + "fmt", PageNumberFormatToken(page.PageNumberFormat)) : null,
            hasStart ? new XAttribute(W + "start", page.PageNumberStartAt!.Value) : null,
            hasChapter ? new XAttribute(W + "chapStyle", page.PageNumberChapterStyleLevel!.Value) : null,
            hasChapter ? new XAttribute(W + "chapSep", PageNumberChapterSeparatorToken(page.PageNumberChapterSeparator)) : null);
    }

    private static string PageNumberFormatToken(PageNumberFormat format) => format switch
    {
        PageNumberFormat.LowerRoman => "lowerRoman",
        PageNumberFormat.UpperRoman => "upperRoman",
        PageNumberFormat.LowerLetter => "lowerLetter",
        PageNumberFormat.UpperLetter => "upperLetter",
        _ => "decimal"
    };

    private static string PageNumberChapterSeparatorToken(PageNumberChapterSeparator separator) => separator switch
    {
        PageNumberChapterSeparator.Period => "period",
        PageNumberChapterSeparator.Colon => "colon",
        PageNumberChapterSeparator.EmDash => "emDash",
        PageNumberChapterSeparator.EnDash => "enDash",
        _ => "hyphen"
    };

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

        var offsetFrom = border.OffsetFrom == PageBorderOffsetFrom.Text ? "text" : "page";
        var space = Math.Max(0, (int)Math.Round(border.SpacePt, MidpointRounding.AwayFromZero));

        XElement Frame(params XElement[] edges)
        {
            var frame = new XElement(W + "pgBorders",
                new XAttribute(W + "offsetFrom", offsetFrom));
            if (border.Display != PageBorderDisplay.AllPages)
            {
                frame.Add(new XAttribute(W + "display",
                    border.Display == PageBorderDisplay.FirstPage ? "firstPage" : "notFirstPage"));
            }
            if (border.ZOrder == PageBorderZOrder.Behind)
                frame.Add(new XAttribute(W + "zOrder", "behind"));
            frame.Add(edges);
            return frame;
        }

        if (border.ArtId > 0)
        {
            var artToken = PageBorderArtStyles.TryGetById(border.ArtId, out var artStyle)
                ? artStyle.Token
                : "single";
            XElement ArtEdge(string name) => new(W + name,
                new XAttribute(W + "val", artToken),
                new XAttribute(W + "sz", PointsToEighthPoints(border.WidthPt)),
                new XAttribute(W + "space", space),
                new XAttribute(W + "color", border.ColorHex.TrimStart('#')));
            return Frame(ArtEdge("top"), ArtEdge("left"), ArtEdge("bottom"), ArtEdge("right"));
        }

        var styleToken = BorderLineStyles.ToToken(border.LineStyle);
        XElement Edge(string name) => new(W + name,
            new XAttribute(W + "val", styleToken),
            new XAttribute(W + "sz", PointsToEighthPoints(border.WidthPt)),
            new XAttribute(W + "space", space),
            new XAttribute(W + "color", border.ColorHex.TrimStart('#')));

        return Frame(Edge("top"), Edge("left"), Edge("bottom"), Edge("right"));
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
        // Trigger when any story paragraph kept an original numPr OR a paragraph STYLE definition carries
        // one (style-level numbering FreeW does not model). Both re-emit against the preserved numbering.xml
        // under the same disjoint-id remap, so a document that only has style-level numbering still builds a
        // plan. Authored-from-scratch / FreeW-only-lists documents have neither, so the plan stays null.
        var hasParagraphPreserved = EnumerateStoryParagraphs(document).Any(p => p.PreservedNumbering is not null);
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
    /// Builds the restart-override map: for every paragraph that carries a non-null
    /// <see cref="ParagraphFormatting.ListStartOverride"/> on a Number or MultiLevel list, assigns a
    /// unique <c>w:numId</c> (above the preserved block) so <c>BuildNumbering</c> can emit the matching
    /// <c>w:num</c> with <c>w:lvlOverride/w:startOverride</c>, and <c>BuildParagraphProperties</c> can
    /// reference it. Returns an empty dictionary (never null) so callers always have a valid lookup.
    /// </summary>
    private static IReadOnlyDictionary<(ListKind Kind, int Level, int StartAt), int> BuildRestartOverrides(
        TextDocument document, PreservedNumberingPlan? preserved)
    {
        var result = new Dictionary<(ListKind, int, int), int>();
        // Override numIds must be clear of FreeW's fixed 1/2/3 AND the preserved range (4..4+preserved.Nums.Count-1).
        var nextOverrideNumId = PreservedNumIdStart + (preserved?.Nums.Count ?? 0);
        foreach (var paragraph in EnumerateStoryParagraphs(document))
        {
            var f = paragraph.Formatting;
            if (f.ListKind is not (ListKind.Number or ListKind.MultiLevel))
                continue;
            if (!f.ListStartOverride.HasValue)
                continue;
            var level = Math.Clamp(f.ListLevel, 0, ListLevelCount - 1);
            var key = (f.ListKind, level, f.ListStartOverride.Value);
            if (!result.ContainsKey(key))
                result[key] = nextOverrideNumId++;
        }
        return result;
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
    private static XDocument BuildNumbering(
        bool includeFreeWNumbering,
        PreservedNumberingPlan? preserved,
        IReadOnlyList<ListNumberFormat> multiLevelNumberFormats,
        IReadOnlyDictionary<(ListKind Kind, int Level, int StartAt), int>? restartOverrides = null,
        bool linkMultiLevelHeadingStyles = false)
    {
        XElement Lvl(int level, string numFmt, string lvlText, string? paragraphStyleId = null) =>
            new(W + "lvl",
                new XAttribute(W + "ilvl", level),
                paragraphStyleId is null ? null : new XElement(W + "pStyle", new XAttribute(W + "val", paragraphStyleId)),
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

        // Legal/outline numbering: level n's text is "%1.%2....%(n+1)." - the dotted run of all ancestor
        // counters. e.g. level 0 -> "%1.", level 2 -> "%1.%2.%3.". Each level's own counter can use a
        // modelled decimal/letter/Roman number style.
        XElement MultiLevelAbstractNum(int abstractNumId) =>
            new(W + "abstractNum", new XAttribute(W + "abstractNumId", abstractNumId),
                new XAttribute(W + "multiLevelType", "multilevel"),
                Enumerable.Range(0, ListLevelCount).Select(level => Lvl(level,
                    MultiLevelListMarkerFormatter.ToOoxmlToken(GetMultiLevelNumberFormat(level)),
                    string.Concat(Enumerable.Range(1, level + 1).Select(n => $"%{n}.")),
                    linkMultiLevelHeadingStyles ? $"Heading{level + 1}" : null)));

        ListNumberFormat GetMultiLevelNumberFormat(int level) =>
            level < multiLevelNumberFormats.Count ? multiLevelNumberFormats[level] : ListNumberFormat.Decimal;

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

        // Restart-override w:num elements: one per distinct (ListKind, level, startAt) group, each
        // referencing the same abstractNumId as the base kind (0=bullet, 1=decimal, 2=multilevel) but adding
        // a w:lvlOverride/@startOverride so Word resets the counter at that paragraph. Word requires that
        // these extra w:num elements reference an existing w:abstractNum — FreeW's fixed 0/1/2 always exist
        // when includeFreeWNumbering is true (i.e. when there are any FreeW-authored lists), which is the
        // only time a paragraph can carry ListStartOverride. Bullets are excluded by BuildRestartOverrides.
        if (restartOverrides is not null && includeFreeWNumbering)
        {
            foreach (var ((kind, level, startAt), overrideNumId) in restartOverrides)
            {
                var abstractNumId = kind switch
                {
                    ListKind.MultiLevel => 2,
                    _ => 1  // Number → decimal abstractNum
                };
                numbering.Add(new XElement(W + "num",
                    new XAttribute(W + "numId", overrideNumId),
                    new XElement(W + "abstractNumId", new XAttribute(W + "val", abstractNumId)),
                    new XElement(W + "lvlOverride",
                        new XAttribute(W + "ilvl", level),
                        new XElement(W + "startOverride", new XAttribute(W + "val", startAt)))));
            }
        }

        return new XDocument(numbering);
    }

    /// <summary>
    /// The subsequence of the CT_Settings (w:settings) child schema order that FreeW's modelled toggles occupy,
    /// used to place each overlaid element at its correct position relative to a preserved settings part's
    /// existing (unmodelled) children. Only the names FreeW emits — plus the immediate neighbours that matter
    /// for ordering — need listing; any unmodelled element not here keeps its relative position because the
    /// overlay only inserts FreeW's elements and never reorders the originals. The full schema order
    /// (ISO/IEC 29500 §17.15.1.78) places these as: displayBackgroundShape … embedTrueTypeFonts …
    /// documentProtection … autoHyphenation … evenAndOddHeaders … footnotePr … endnotePr.
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
        "summaryLength", "clickAndTypeStyle", "defaultTableStyle", "evenAndOddHeaders",
        "footnotePr", "endnotePr"
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
    private static XDocument BuildSettings(ProtectionSettings protection, PageSettings page, bool displayBackground, bool embedTrueTypeFonts, NoteNumberingOptions footnoteNumbering, NoteNumberingOptions endnoteNumbering, XElement? original, bool anyDifferentOddEvenPages = false)
    {
        var autoHyphenation = page.AutoHyphenation;
        // Use the caller-supplied flag (any-section OR) instead of just the final section's flag, so a
        // non-final section with DifferentOddEvenPages=true still sets the document-global toggle.
        var differentOddEvenPages = anyDifferentOddEvenPages || page.DifferentOddEvenPages;
        var mirrorMargins = page.MirrorMargins;
        var defaultTabStop = HasCustomDefaultTabStop(page)
            ? new XElement(W + "defaultTabStop", new XAttribute(W + "val", PointsToDxa(page.DefaultTabStopPt)))
            : null;
        // Fresh documents keep the historical minimal-settings behavior: dormant hyphenation sub-options do
        // not create a settings part by themselves. Preserved settings are different: Word-authored documents
        // can keep these values while auto hyphenation is off, and the reader captures them so they survive.
        var preserveDormantHyphenationOptions = original is not null;
        var writeHyphenationOptions = autoHyphenation || preserveDormantHyphenationOptions;
        var consecutiveLimit = writeHyphenationOptions && page.ConsecutiveHyphenLimit > 0
            ? new XElement(W + "consecutiveHyphenLimit", new XAttribute(W + "val", page.ConsecutiveHyphenLimit))
            : null;
        var hyphenationZone = writeHyphenationOptions && page.HyphenationZonePt > 0
            ? new XElement(W + "hyphenationZone", new XAttribute(W + "val", PointsToDxa(page.HyphenationZonePt)))
            : null;
        var doNotHyphenateCaps = writeHyphenationOptions && page.DoNotHyphenateCaps
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
            // Mirror margins (w:mirrorMargins) sits after the font-embedding region and before autoHyphenation
            // in CT_Settings schema order.
            if (mirrorMargins)
                fresh.Add(new XElement(W + "mirrorMargins"));
            if (defaultTabStop is not null)
                fresh.Add(defaultTabStop);
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
                fresh.Add(BuildDocumentProtectionElement(protection, freshEdit));
            // Footnote/endnote numbering options (w:footnotePr / w:endnotePr) follow evenAndOddHeaders in
            // CT_Settings schema order. Only emit when non-default (keeps a freshly authored document minimal).
            if (BuildNotePr(footnoteNumbering, "footnotePr") is { } freshFootnotePr)
                fresh.Add(freshFootnotePr);
            if (BuildNotePr(endnoteNumbering, "endnotePr") is { } freshEndnotePr)
                fresh.Add(freshEndnotePr);
            return new XDocument(fresh);
        }

        // Preserved settings: overlay each modelled element onto a clone of the original (never mutating the
        // model). Each is removed (so we replace, not duplicate) then re-inserted at its CT_Settings schema
        // position; an element whose feature is off is simply removed, since the modelled value — not the
        // preserved one — is authoritative for these. Unmodelled settings keep their place.
        var settings = new XElement(original);
        OverlaySetting(settings, "embedTrueTypeFonts", embedTrueTypeFonts ? new XElement(W + "embedTrueTypeFonts") : null);
        OverlaySetting(settings, "displayBackgroundShape", displayBackground ? new XElement(W + "displayBackgroundShape") : null);
        OverlaySetting(settings, "mirrorMargins", mirrorMargins ? new XElement(W + "mirrorMargins") : null);
        OverlaySetting(settings, "defaultTabStop", defaultTabStop);
        OverlaySetting(settings, "autoHyphenation", autoHyphenation ? new XElement(W + "autoHyphenation") : null);
        OverlaySetting(settings, "consecutiveHyphenLimit", consecutiveLimit);
        OverlaySetting(settings, "hyphenationZone", hyphenationZone);
        OverlaySetting(settings, "doNotHyphenateCaps", doNotHyphenateCaps);
        OverlaySetting(settings, "evenAndOddHeaders", differentOddEvenPages ? new XElement(W + "evenAndOddHeaders") : null);
        OverlaySetting(settings, "documentProtection",
            ProtectionEditToken(protection.Mode) is { } edit
                ? BuildDocumentProtectionElement(protection, edit)
                : null);
        // Overlay footnote/endnote numbering: non-default options replace any existing w:footnotePr /
        // w:endnotePr from the preserved settings; default values remove the element (FreeW owns it now).
        OverlaySetting(settings, "footnotePr", BuildNotePr(footnoteNumbering, "footnotePr"));
        OverlaySetting(settings, "endnotePr", BuildNotePr(endnoteNumbering, "endnotePr"));
        return new XDocument(settings);
    }

    private static bool HasCustomDefaultTabStop(PageSettings page) =>
        Math.Abs(page.DefaultTabStopPt - PageSettings.WordDefaultTabStopPt) > 0.01;

    /// <summary>
    /// Builds a w:documentProtection element for word/settings.xml from the given
    /// <paramref name="protection"/> settings and resolved <paramref name="editToken"/>. When the
    /// settings carry a password hash the OOXML legacy attributes (w:cryptProviderType, w:cryptAlgorithmSid,
    /// w:cryptSpinCount, w:hash, w:salt) are also emitted so Microsoft Word honours the password.
    /// </summary>
    private static XElement BuildDocumentProtectionElement(ProtectionSettings protection, string editToken)
    {
        var el = new XElement(W + "documentProtection",
            new XAttribute(W + "edit", editToken),
            new XAttribute(W + "enforcement", "1"));
        if (protection.HasPassword)
        {
            el.Add(new XAttribute(W + "cryptProviderType", "rsaAES"));
            el.Add(new XAttribute(W + "cryptAlgorithmClass", "hash"));
            el.Add(new XAttribute(W + "cryptAlgorithmType", "typeAny"));
            el.Add(new XAttribute(W + "cryptAlgorithmSid", "4")); // SHA-1
            el.Add(new XAttribute(W + "cryptSpinCount", protection.SpinCount.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            el.Add(new XAttribute(W + "hash", protection.PasswordHash!));
            el.Add(new XAttribute(W + "salt", protection.PasswordSalt!));
        }
        return el;
    }

    /// <summary>
    /// Builds a w:footnotePr or w:endnotePr child element for word/settings.xml from the given
    /// <paramref name="options"/>. Returns null when all options are at their Word defaults (no element needed).
    /// The <paramref name="localName"/> ("footnotePr" or "endnotePr") determines the element name.
    /// </summary>
    private static XElement? BuildNotePr(NoteNumberingOptions options, string localName = "footnotePr")
    {
        if (options.IsDefault)
            return null;

        var pr = new XElement(W + localName);
        if (options.NumberFormat != NoteNumberFormat.Decimal)
            pr.Add(new XElement(W + "numFmt", new XAttribute(W + "val", NoteNumberFormatToOoxml(options.NumberFormat))));
        if (options.StartAt != 1)
            pr.Add(new XElement(W + "numStart", new XAttribute(W + "val", options.StartAt)));
        if (options.NumberRestart != NoteNumberRestart.Continuous)
            pr.Add(new XElement(W + "numRestart", new XAttribute(W + "val", NoteNumberRestartToOoxml(options.NumberRestart))));
        return pr;
    }

    private static string NoteNumberFormatToOoxml(NoteNumberFormat fmt) => fmt switch
    {
        NoteNumberFormat.LowerRoman => "lowerRoman",
        NoteNumberFormat.UpperRoman => "upperRoman",
        NoteNumberFormat.LowerLetter => "lowerLetter",
        NoteNumberFormat.UpperLetter => "upperLetter",
        NoteNumberFormat.Chicago => "chicago",
        _ => "decimal"
    };

    private static string NoteNumberRestartToOoxml(NoteNumberRestart restart) => restart switch
    {
        NoteNumberRestart.EachSection => "eachSect",
        NoteNumberRestart.EachPage => "eachPage",
        _ => "continuous"
    };

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
        SourceType.Report => "Report",
        SourceType.BookSection => "BookSection",
        SourceType.ConferenceProceedings => "ConferenceProceedings",
        SourceType.ArticleInPeriodical => "ArticleInAPeriodical",
        SourceType.ElectronicSource => "ElectronicSource",
        SourceType.Patent => "Patent",
        SourceType.Interview => "Interview",
        SourceType.Misc => "Misc",
        SourceType.Film => "Film",
        SourceType.SoundRecording => "SoundRecording",
        SourceType.Art => "Art",
        SourceType.InternetSite => "InternetSite",
        SourceType.Performance => "Performance",
        SourceType.Case => "Case",
        _ => "Book",
    };

    private static IReadOnlyList<PreservedPart> RemoveCurrentBibliographyCustomXml(
        IReadOnlyList<PreservedPart> preservedParts)
    {
        var bibliographyItems = preservedParts
            .Where(IsBibliographyCustomXmlItem)
            .Select(GetCustomXmlItemNumber)
            .Where(number => number is not null)
            .Select(number => number!.Value)
            .ToHashSet();

        if (bibliographyItems.Count == 0)
            return preservedParts;

        return preservedParts
            .Where(part => !BelongsToCustomXmlItem(part, bibliographyItems))
            .ToList();
    }

    private static bool IsBibliographyCustomXmlItem(PreservedPart part)
    {
        var number = GetCustomXmlItemNumber(part);
        if (number is null)
            return false;

        try
        {
            using var stream = new MemoryStream(part.Bytes, writable: false);
            return XDocument.Load(stream).Root?.Name == B + "Sources";
        }
        catch (System.Xml.XmlException)
        {
            return false;
        }
    }

    private static int? GetCustomXmlItemNumber(PreservedPart part)
    {
        var path = NormalizePartName(part.PartName);
        if (!path.StartsWith("customXml/", StringComparison.Ordinal))
        {
            return null;
        }

        var fileName = path["customXml/".Length..];
        if (!fileName.StartsWith("item", StringComparison.Ordinal)
            || fileName.Contains("/", StringComparison.Ordinal)
            || !fileName.EndsWith(".xml", StringComparison.Ordinal))
        {
            return null;
        }

        var digits = fileName["item".Length..^".xml".Length];
        return int.TryParse(digits, out var number) && number > 0 ? number : null;
    }

    private static bool BelongsToCustomXmlItem(PreservedPart part, IReadOnlySet<int> itemNumbers)
    {
        var path = NormalizePartName(part.PartName);
        foreach (var number in itemNumbers)
        {
            if (path == $"customXml/item{number}.xml"
                || path == $"customXml/itemProps{number}.xml"
                || path == $"customXml/_rels/item{number}.xml.rels")
            {
                return true;
            }
        }

        return false;
    }

    private static string NextCustomXmlItemName(IReadOnlyList<PreservedPart> preservedParts)
    {
        var existing = preservedParts
            .Select(GetCustomXmlItemNumber)
            .Where(number => number is not null)
            .Select(number => number!.Value)
            .ToHashSet();

        for (var number = 1; ; number++)
        {
            if (!existing.Contains(number))
                return $"/customXml/item{number}.xml";
        }
    }

    private static IReadOnlyList<PreservedPart> BuildCurrentBibliographyParts(
        TextDocument document,
        string itemName)
    {
        var itemNumber = GetCustomXmlItemNumber(new PreservedPart(itemName, []))
            ?? throw new InvalidOperationException($"Invalid custom XML item name: {itemName}");
        var itemFileName = $"item{itemNumber}.xml";
        var itemPropsName = $"/customXml/itemProps{itemNumber}.xml";
        var itemRelsName = $"/customXml/_rels/{itemFileName}.rels";

        var itemRoot = new XElement(BuildBibliographySources(document).Root!);
        var styleName = Citations.StyleName(document.BibliographyStyle);
        itemRoot.SetAttributeValue("SelectedStyle", $"\\{styleName}.XSL");
        itemRoot.SetAttributeValue("StyleName", styleName);
        itemRoot.SetAttributeValue(
            "URI",
            $"http://schemas.openxmlformats.org/bibliographicStyle/{styleName}");
        itemRoot.SetAttributeValue("xmlns", B.NamespaceName);

        var itemProps = new XDocument(
            new XElement(CustomXmlDataStore + "dataStoreItem",
                new XAttribute(CustomXmlDataStore + "itemID", "{00000000-0000-0000-0000-000000000001}"),
                new XElement(CustomXmlDataStore + "schemaRefs",
                    new XElement(CustomXmlDataStore + "schemaRef",
                        new XAttribute(CustomXmlDataStore + "uri", B.NamespaceName)))));

        var itemRels = new XDocument(
            new XElement(Rel + "Relationships",
                new XElement(Rel + "Relationship",
                    new XAttribute("Id", "rId1"),
                    new XAttribute("Type", CustomXmlPropsRelType),
                    new XAttribute("Target", $"itemProps{itemNumber}.xml"))));

        return
        [
            new PreservedPart(itemName, XmlBytes(new XDocument(itemRoot)), CustomXmlItemContentType, CustomXmlRelType),
            new PreservedPart(itemPropsName, XmlBytes(itemProps), CustomXmlPropsContentType),
            new PreservedPart(itemRelsName, XmlBytes(itemRels))
        ];

        static byte[] XmlBytes(XDocument document) =>
            Encoding.UTF8.GetBytes(document.ToString(SaveOptions.DisableFormatting));
    }

    /// <summary>
    /// Builds word/bibliography/sources.xml: a b:Sources element carrying the document's selected
    /// <see cref="TextDocument.BibliographyStyle"/> (its <c>SelectedStyle</c> attribute as the style name)
    /// and one b:Source per <see cref="Source"/>. Each source records its tag, type and populated fields.
    /// Structured personal authors/editors/translators are emitted as Word contributor role blocks with
    /// <c>b:NameList/b:Person</c> rows; corporate and legacy string-only authors are emitted as
    /// <c>b:Corporate</c>. Only non-empty fields are emitted.
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

            var authorElement = BuildBibliographyAuthor(source);
            if (authorElement is not null)
                element.Add(authorElement);

            AddBibliographyField(element, "Title", source.Title);
            AddBibliographyField(element, "BookTitle", source.BookTitle);
            AddBibliographyField(element, "ConferenceName", source.ConferenceName);
            AddBibliographyField(element, "Year", source.Year);
            AddBibliographyField(element, "Month", source.Month);
            AddBibliographyField(element, "Day", source.Day);
            AddBibliographyField(element, "Institution", source.Institution);
            AddBibliographyField(element, "Publisher", source.Publisher);
            AddBibliographyField(element, "City", source.City);
            AddBibliographyField(element, "Edition", source.Edition);
            AddBibliographyField(element, "StandardNumber", source.StandardNumber);
            AddBibliographyField(element, "ChapterNumber", source.ChapterNumber);
            AddBibliographyField(element, "PatentNumber", source.PatentNumber);
            AddBibliographyField(element, "CaseNumber", source.CaseNumber);
            AddBibliographyField(element, "Court", source.Court);
            AddBibliographyField(element, "Reporter", source.Reporter);
            AddBibliographyField(element, "CountryRegion", source.CountryRegion);
            AddBibliographyField(element, "StateProvince", source.StateProvince);
            AddBibliographyField(element, "Medium", source.Medium);
            AddBibliographyField(element, "Type", source.SourceKind);
            AddBibliographyField(element, "AlbumTitle", source.AlbumTitle);
            AddBibliographyField(element, "ProductionCompany", source.ProductionCompany);
            AddBibliographyField(element, "RecordingNumber", source.RecordingNumber);
            AddBibliographyField(element, "Theater", source.Theater);
            AddBibliographyField(element, "ShortTitle", source.ShortTitle);
            AddBibliographyField(element, "Comments", source.Comments);
            AddBibliographyField(element, "JournalName", source.Journal);
            AddBibliographyField(element, "Volume", source.Volume);
            AddBibliographyField(element, "Issue", source.Issue);
            AddBibliographyField(element, "Pages", source.Pages);
            AddBibliographyField(element, "URL", source.Url);
            if (HasStructuredAccessedDate(source))
            {
                AddBibliographyField(element, "DayAccessed", source.AccessedDay);
                AddBibliographyField(element, "MonthAccessed", source.AccessedMonth);
                AddBibliographyField(element, "YearAccessed", source.AccessedYear);
            }
            else
            {
                AddBibliographyField(element, "YearAccessed", source.Accessed);
            }

            sources.Add(element);
        }

        return new XDocument(sources);

        static void AddBibliographyField(XElement parent, string localName, string? value)
        {
            if (!string.IsNullOrEmpty(value))
                parent.Add(new XElement(B + localName, value));
        }

        static bool HasStructuredAccessedDate(Source source) =>
            !string.IsNullOrEmpty(source.AccessedDay)
            || !string.IsNullOrEmpty(source.AccessedMonth)
            || !string.IsNullOrEmpty(source.AccessedYear);
    }

    private static XElement? BuildBibliographyAuthor(Source source)
    {
        var roles = new List<XElement>();

        var corporate = string.IsNullOrWhiteSpace(source.CorporateAuthor)
            ? source.Author
            : source.CorporateAuthor;

        AddRole(roles, "Author", source.PersonalAuthors, corporate);
        AddRole(roles, "Editor", source.Editors, corporate: null);
        AddRole(roles, "Translator", source.Translators, corporate: null);
        AddRole(roles, "Inventor", [], source.Inventor);
        AddRole(roles, "Interviewee", [], source.Interviewee);
        AddRole(roles, "Interviewer", [], source.Interviewer);
        AddRole(roles, "Artist", [], source.Artist);
        AddRole(roles, "Composer", [], source.Composer);
        AddRole(roles, "Conductor", [], source.Conductor);
        AddRole(roles, "Director", [], source.Director);
        AddRole(roles, "Performer", [], source.Performer);
        AddRole(roles, "ProducerName", [], source.ProducerName);
        AddRole(roles, "Writer", [], source.Writer);

        return roles.Count == 0 ? null : new XElement(B + "Author", roles);

        static void AddRole(
            List<XElement> roles,
            string roleName,
            IEnumerable<SourceAuthorPerson> people,
            string? corporate)
        {
            var role = BuildPersonalRole(roleName, people) ?? BuildCorporateRole(roleName, corporate);
            if (role is not null)
                roles.Add(role);
        }

        static XElement? BuildPersonalRole(string roleName, IEnumerable<SourceAuthorPerson> people)
        {
            var personElements = people
                .Where(person => person is not null && !person.IsEmpty)
                .Select(BuildPerson)
                .ToList();
            if (personElements.Count == 0)
                return null;

            return new XElement(B + roleName,
                new XElement(B + "NameList", personElements));
        }

        static XElement? BuildCorporateRole(string roleName, string? corporate)
        {
            if (string.IsNullOrWhiteSpace(corporate))
                return null;

            return new XElement(B + roleName,
                new XElement(B + "Corporate", corporate.Trim()));
        }

        static XElement BuildPerson(SourceAuthorPerson person)
        {
            var element = new XElement(B + "Person");
            AddPersonPart(element, "Last", person.Last);
            AddPersonPart(element, "First", person.First);
            AddPersonPart(element, "Middle", person.Middle);
            return element;
        }

        static void AddPersonPart(XElement person, string localName, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
                person.Add(new XElement(B + localName, value.Trim()));
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
        var relationships = OpcRelationships.CreateRoot();
        foreach (var part in embeddedFonts.SelectMany(f => f.Parts))
            relationships.Add(OpcRelationships.CreateRelationship(
                part.RelationshipId,
                FontRelType,
                "fonts/" + part.FileName));
        return new XDocument(relationships);
    }

    /// <summary>
    /// Builds word/theme/theme1.xml (a:theme): the document's <see cref="DocumentTheme"/> serialised as a
    /// real DrawingML theme — an a:clrScheme (the twelve <see cref="ThemeColorScheme"/> colour slots), an
    /// a:fontScheme (major = the theme's heading font, minor = its body font) and a backed
    /// a:fmtScheme from <see cref="DocumentEffectSet"/> (three fill/line/effect/bg-fill entries, matching
    /// Word's required structure). The reader recovers the colour/font/effect scheme and infers the preset
    /// (see DocxReader.ReadTheme).
    /// </summary>
    private static XDocument BuildTheme(DocumentTheme theme)
    {
        var scheme = theme.ColorScheme;
        var effects = DocumentEffectSet.FromTheme(theme);

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

        // A Word-style format scheme: three fills, lines, effects, and background fills. The selected
        // effect-set name is the durable part FreeW reads back; the line/effect entries make the theme
        // visibly meaningful to downstream shape/SmartArt/WordArt consumers.
        XElement SolidPhClr() => new(A + "solidFill", new XElement(A + "schemeClr", new XAttribute("val", "phClr")));

        var fmtScheme = new XElement(A + "fmtScheme",
            new XAttribute("name", effects.Name),
            new XElement(A + "fillStyleLst", SolidPhClr(), SolidPhClr(), SolidPhClr()),
            new XElement(A + "lnStyleLst",
                Line(effects.LineWidthEmu),
                Line(effects.LineWidthEmu * 2),
                Line(effects.LineWidthEmu * 3)),
            new XElement(A + "effectStyleLst",
                EffectStyle(effects, 1),
                EffectStyle(effects, 2),
                EffectStyle(effects, 3)),
            new XElement(A + "bgFillStyleLst", SolidPhClr(), SolidPhClr(), SolidPhClr()));

        var themeElements = new XElement(A + "themeElements", clrScheme, fontScheme, fmtScheme);

        return new XDocument(
            new XElement(A + "theme",
                new XAttribute(XNamespace.Xmlns + "a", A.NamespaceName),
                new XAttribute("name", theme.Name),
                themeElements,
                new XElement(A + "objectDefaults"),
                new XElement(A + "extraClrSchemeLst")));

        static XElement Line(int widthEmu) => new(A + "ln",
            new XAttribute("w", widthEmu),
            new XAttribute("cap", "flat"),
            new XAttribute("cmpd", "sng"),
            new XAttribute("algn", "ctr"),
            new XElement(A + "solidFill", new XElement(A + "schemeClr", new XAttribute("val", "phClr"))),
            new XElement(A + "prstDash", new XAttribute("val", "solid")));

        static XElement EffectStyle(DocumentEffectSet set, int depth)
        {
            var effectList = new XElement(A + "effectLst");
            if (set.OuterShadow)
            {
                effectList.Add(new XElement(A + "outerShdw",
                    new XAttribute("blurRad", 40000 * depth),
                    new XAttribute("dist", 20000 * depth),
                    new XAttribute("dir", 5400000),
                    new XAttribute("algn", "ctr"),
                    new XAttribute("rotWithShape", 0),
                    new XElement(A + "srgbClr",
                        new XAttribute("val", "000000"),
                        new XElement(A + "alpha", new XAttribute("val", Math.Max(18000, 42000 - (depth * 6000)))))));
            }
            if (set.SoftEdges)
                effectList.Add(new XElement(A + "softEdge", new XAttribute("rad", 12000 * depth)));
            return new XElement(A + "effectStyle", effectList);
        }
    }

    private static XDocument BuildStyles(TextDocument document, PreservedNumberingPlan? preservedNumbering = null)
    {
        var styles = new XElement(W + "styles", new XAttribute(XNamespace.Xmlns + "w", W.NamespaceName));
        var chapterPageNumberingStyleIds = document.Sections
            .Select(section => section.Page.PageNumberChapterStyleLevel)
            .Where(level => level is >= 1 and <= 9)
            .Select(level => $"Heading{level!.Value}")
            .ToHashSet(StringComparer.Ordinal);

        // w:docDefaults is the FIRST child of w:styles (schema order mandated by CT_Styles). It carries the
        // document-level default run and paragraph properties — in particular the body font (e.g. Calibri
        // 11pt) stored in w:rPrDefault/w:rPr. Without re-emitting this, Word falls back to Times New Roman
        // after a round-trip because runs typically carry no explicit w:rFonts.
        {
            var ddRPr = BuildDocDefaultRunProperties(document.DefaultRun);
            var ddPPr = BuildDocDefaultParagraphProperties(document.DefaultParagraph);
            if (ddRPr is not null || ddPPr is not null)
            {
                var docDefaults = new XElement(W + "docDefaults");
                if (ddRPr is not null)
                    docDefaults.Add(new XElement(W + "rPrDefault", ddRPr));
                if (ddPPr is not null)
                    docDefaults.Add(new XElement(W + "pPrDefault", ddPPr));
                styles.Add(docDefaults);
            }
        }

        foreach (var style in document.Styles.Values)
        {
            if (style.Type == StyleType.Table && style.PreservedTableStyleXml is { Length: > 0 } preservedTableStyleXml)
            {
                styles.Add(XElement.Parse(preservedTableStyleXml, LoadOptions.PreserveWhitespace));
                continue;
            }

            var element = new XElement(W + "style",
                new XAttribute(W + "type", style.Type switch
                {
                    StyleType.Character => "character",
                    StyleType.Table => "table",
                    StyleType.Numbering => "numbering",
                    _ => "paragraph"
                }),
                new XAttribute(W + "styleId", style.Id),
                new XElement(W + "name", new XAttribute(W + "val", style.Name)));
            if (!string.IsNullOrEmpty(style.BasedOnStyleId))
                element.Add(new XElement(W + "basedOn", new XAttribute(W + "val", style.BasedOnStyleId)));
            // The "Style for following paragraph" (w:next, after w:basedOn in CT_Style order): which style
            // the paragraph after this one takes (e.g. a Heading's body-text follow-on). Emitted only for
            // paragraph styles that specify one.
            if (style.Type != StyleType.Character && !string.IsNullOrEmpty(style.NextStyleId))
                element.Add(new XElement(W + "next", new XAttribute(W + "val", style.NextStyleId)));
            // A single w:pPr (which precedes w:rPr in CT_Style order) carrying the style's paragraph
            // formatting (alignment / indents / spacing) and, for a preserved style-level list, its numPr.
            // Built only for paragraph styles, and only when there is something to emit, so character styles
            // and formatting-only paragraph styles are unaffected.
            if (style.Type != StyleType.Character)
            {
                var pPr = BuildStyleParagraphProperties(style.Paragraph);
                if (chapterPageNumberingStyleIds.Contains(style.Id))
                {
                    pPr ??= new XElement(W + "pPr");
                    // A PAGE field with w:pgNumType/@chapStyle is resolved by Word only when the
                    // requested Heading style is associated with the same linked multilevel num.
                    pPr.AddFirst(new XElement(W + "numPr",
                        new XElement(W + "ilvl", new XAttribute(W + "val", style.OutlineLevel ?? 0)),
                        new XElement(W + "numId", new XAttribute(W + "val", MultiLevelNumId))));
                }
                if (style.OutlineLevel is { } outlineLevel)
                {
                    pPr ??= new XElement(W + "pPr");
                    pPr.Add(new XElement(W + "outlineLvl",
                        new XAttribute(W + "val", Math.Clamp(outlineLevel, 0, 8))));
                }
                // Style-level numbering FreeW does not model: when the style carried an original w:numPr and
                // the merge plan remapped that numId (a definition exists in the preserved numbering.xml),
                // re-emit a numPr pointing at the REMAPPED numId (disjoint from FreeW's fixed ids), keeping
                // the original ilvl. A numPr whose numId the plan did not remap (no matching w:num) is
                // dropped, exactly like a paragraph's preserved numPr.
                if (!chapterPageNumberingStyleIds.Contains(style.Id)
                    && preservedNumbering is not null
                    && style.PreservedNumbering is { } sn
                    && preservedNumbering.NumIdRemap.TryGetValue(sn.NumId, out var mappedNumId))
                {
                    pPr ??= new XElement(W + "pPr");
                    pPr.Add(new XElement(W + "numPr",
                        new XElement(W + "ilvl", new XAttribute(W + "val", sn.Ilvl)),
                        new XElement(W + "numId", new XAttribute(W + "val", mappedNumId))));
                }
                if (pPr is not null)
                    element.Add(pPr);
            }
            var rPr = BuildRunProperties(style.Run);
            if (rPr is not null)
                element.Add(rPr);
            styles.Add(element);
        }

        // Emit a minimal w:style type="table" for every DocumentTableStyle catalog entry referenced by any
        // table in the document. The style definition carries the catalog's border + fill intent so the docx
        // round-trips losslessly within FreeW and renders correctly in Word. The w:tblStylePr conditional-
        // format approach is used: a tblStylePr per active band (whole-table, firstRow, band1H, band2H, …).
        var usedTableStyleIds = CollectTableStyleIds(document.Blocks);
        foreach (var styleId in usedTableStyleIds)
        {
            if (document.Styles.ContainsKey(styleId))
                continue;
            var catalogEntry = DocumentTableStyle.FindById(styleId);
            if (catalogEntry is not null)
                styles.Add(BuildTableStyleElement(catalogEntry));
        }

        return new XDocument(styles);
    }

    /// <summary>
    /// Collects all distinct <see cref="Table.TableStyleId"/> values referenced by any table in
    /// <paramref name="blocks"/> (including nested tables inside table cells), in encounter order.
    /// </summary>
    private static IEnumerable<string> CollectTableStyleIds(IEnumerable<Block> blocks)
    {
        var seen = new System.Collections.Generic.HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        foreach (var block in blocks)
        {
            if (block is Table table)
            {
                if (table.TableStyleId is { Length: > 0 } sid && seen.Add(sid))
                    yield return sid;
                foreach (var row in table.Rows)
                    foreach (var cell in row.Cells)
                        foreach (var nestedId in CollectTableStyleIds(cell.Paragraphs.SelectMany<Paragraph, Block>(_ => [])))
                        {
                            // Nested tables in cells are Paragraphs, not Blocks, in the model; skip for now.
                        }
            }
        }
    }

    /// <summary>
    /// Builds a minimal <c>w:style w:type="table"</c> element for a catalog <see cref="DocumentTableStyle"/>.
    /// Uses <c>w:tblStylePr</c> conditional-format bands to carry the header / banded fills so Word's own
    /// table-style machinery renders the look correctly. FreeW's reader recognises the catalog id and maps the
    /// catalog's visual intent back directly (no parsing of tblStylePr needed for FreeW-authored docx).
    /// </summary>
    private static XElement BuildTableStyleElement(DocumentTableStyle style)
    {
        var element = new XElement(W + "style",
            new XAttribute(W + "type", "table"),
            new XAttribute(W + "styleId", style.WordStyleId),
            new XElement(W + "name", new XAttribute(W + "val", style.Name)));

        // Whole-table tblPr: outer borders when the style has them.
        if (style.Borders)
        {
            var borderColor = style.BorderColorHex ?? "auto";
            XElement Border(string name) => new(W + name,
                new XAttribute(W + "val", "single"),
                new XAttribute(W + "sz", 4),
                new XAttribute(W + "space", 0),
                new XAttribute(W + "color", borderColor));
            element.Add(new XElement(W + "tblPr",
                new XElement(W + "tblBorders",
                    Border("top"), Border("left"), Border("bottom"), Border("right"),
                    Border("insideH"), Border("insideV"))));
        }

        // tblStylePr bands: emit one per non-null region.
        if (style.HeaderBand is { } header)
            element.Add(BuildTblStylePr("firstRow", header));
        if (style.LastRowBand is { } lastRow)
            element.Add(BuildTblStylePr("lastRow", lastRow));
        if (style.FirstColumnBand is { } firstCol)
            element.Add(BuildTblStylePr("firstCol", firstCol));
        if (style.LastColumnBand is { } lastCol)
            element.Add(BuildTblStylePr("lastCol", lastCol));
        if (style.BandedRowOdd is { } band1)
            element.Add(BuildTblStylePr("band1H", band1));
        if (style.BandedRowEven is { } band2)
            element.Add(BuildTblStylePr("band2H", band2));

        return element;
    }

    /// <summary>Builds a single <c>w:tblStylePr</c> conditional-format band carrying a fill and/or bold run.</summary>
    private static XElement BuildTblStylePr(string condType, TableStyleBand band)
    {
        var pr = new XElement(W + "tblStylePr", new XAttribute(W + "type", condType));
        if (band.FillHex is { Length: > 0 } fill)
        {
            pr.Add(new XElement(W + "tcPr",
                new XElement(W + "shd",
                    new XAttribute(W + "val", "clear"),
                    new XAttribute(W + "color", "auto"),
                    new XAttribute(W + "fill", fill))));
        }
        if (band.Bold)
        {
            pr.Add(new XElement(W + "rPr",
                new XElement(W + "b"),
                new XElement(W + "bCs")));
        }
        return pr;
    }

    /// <summary>
    /// Builds the <c>w:rPr</c> inside <c>w:docDefaults/w:rPrDefault</c>. Emits only the core fields that
    /// carry meaningful document-default run formatting (font family/size, colour, language, bold/italic).
    /// Returns null when the default run is indistinguishable from a no-op so that documents with no
    /// meaningful run defaults do not gain a spurious w:rPrDefault element.
    /// </summary>
    private static XElement? BuildDocDefaultRunProperties(RunFormatting f)
    {
        var rPr = new XElement(W + "rPr");
        if (f.FontFamily is { Length: > 0 } family)
            rPr.Add(new XElement(W + "rFonts",
                new XAttribute(W + "ascii", family),
                new XAttribute(W + "hAnsi", family),
                new XAttribute(W + "eastAsia", family),
                new XAttribute(W + "cs", family)));
        if (f.Bold)
            rPr.Add(new XElement(W + "b"));
        if (f.Italic)
            rPr.Add(new XElement(W + "i"));
        if (f.ColorHex is { Length: > 0 } color)
            rPr.Add(new XElement(W + "color", new XAttribute(W + "val", color.TrimStart('#'))));
        if (f.FontSizePt is { } size)
        {
            var halfPoints = PointsToHalfPoints(size);
            rPr.Add(new XElement(W + "sz", new XAttribute(W + "val", halfPoints)));
            rPr.Add(new XElement(W + "szCs", new XAttribute(W + "val", halfPoints)));
        }
        if (f.LanguageTag is { Length: > 0 } lang)
            rPr.Add(new XElement(W + "lang",
                new XAttribute(W + "val", lang),
                new XAttribute(W + "eastAsia", lang),
                new XAttribute(W + "bidi", lang)));
        return rPr.HasElements ? rPr : null;
    }

    /// <summary>
    /// Builds the <c>w:pPr</c> inside <c>w:docDefaults/w:pPrDefault</c>. Emits the default paragraph
    /// spacing when it deviates from the absolute minimum (zero before/after, 1.0 multiple line). Returns
    /// null for documents where paragraph defaults are implicit so no spurious element is emitted.
    /// </summary>
    private static XElement? BuildDocDefaultParagraphProperties(ParagraphFormatting f)
    {
        var hasLineSpacing = f.LineRule != LineSpacingRule.Multiple
            || System.Math.Abs(f.LineSpacing - ParagraphFormatting.Default.LineSpacing) > 0.0001;
        if (f.SpaceBeforePt <= 0 && f.SpaceAfterPt <= 0 && !f.BeforeAutoSpacing && !f.AfterAutoSpacing
            && !f.ContextualSpacing.HasValue && !hasLineSpacing)
            return null;
        var pPr = new XElement(W + "pPr");
        var hasSpacing = f.SpaceBeforePt > 0 || f.SpaceAfterPt > 0 || f.BeforeAutoSpacing || f.AfterAutoSpacing || hasLineSpacing;
        if (hasSpacing)
        {
            var spacing = new XElement(W + "spacing");
            var hasNumericSpacing = !f.BeforeAutoSpacing && f.SpaceBeforePt > 0
                || !f.AfterAutoSpacing && f.SpaceAfterPt > 0;
            if (f.BeforeAutoSpacing)
                spacing.Add(new XAttribute(W + "beforeAutospacing", "1"));
            else if (hasNumericSpacing)
                spacing.Add(new XAttribute(W + "before", PointsToDxa(f.SpaceBeforePt)));
            if (f.AfterAutoSpacing)
                spacing.Add(new XAttribute(W + "afterAutospacing", "1"));
            else if (hasNumericSpacing)
                spacing.Add(new XAttribute(W + "after", PointsToDxa(f.SpaceAfterPt)));
            if (hasLineSpacing)
            {
                var (line, rule) = f.LineRule switch
                {
                    LineSpacingRule.Exact => ((int)System.Math.Round(f.LineHeightPt * 20), "exact"),
                    LineSpacingRule.AtLeast => ((int)System.Math.Round(f.LineHeightPt * 20), "atLeast"),
                    _ => ((int)System.Math.Round(f.LineSpacing * 240), "auto")
                };
                spacing.Add(new XAttribute(W + "line", line));
                spacing.Add(new XAttribute(W + "lineRule", rule));
            }
            pPr.Add(spacing);
        }
        if (f.ContextualSpacing is { } contextualSpacing)
            pPr.Add(new XElement(W + "contextualSpacing",
                contextualSpacing ? null : new XAttribute(W + "val", "0")));
        return pPr;
    }

    /// <summary>
    /// Build a style-scope <c>w:pPr</c> carrying only the paragraph formatting a custom style can define
    /// (alignment, left/right/first-line indents, space-before/after, line spacing). Returns null when the
    /// style's paragraph formatting is the default (nothing to emit), so a formatting-only or run-only style
    /// adds no empty <c>w:pPr</c>. This is deliberately narrower than the per-paragraph
    /// <see cref="BuildParagraphProperties"/>, which also handles instance-only concerns (pStyle, section
    /// breaks, FreeW's modelled lists) that have no place on a style definition.
    /// </summary>
    private static XElement? BuildStyleParagraphProperties(ParagraphFormatting f)
    {
        var pPr = new XElement(W + "pPr");

        // Children MUST follow the CT_PPr / EG_PPrBase schema sequence, matching the order used by the
        // main BuildParagraphProperties. The relevant subset emitted here, in schema order, is:
        //   ... w:spacing, w:ind, ... w:jc, ...
        // The original code emitted w:jc FIRST (before w:spacing and w:ind), which is out of order and
        // triggers Word's strict validator ("unreadable content / repair") whenever a tracked paragraph-
        // format revision (w:pPrChange) or a style definition carries a non-Left alignment together with
        // indent or spacing values.

        // Style tab stops are part of EG_PPrBase too. Preserve both concrete stops and clear operations;
        // a paragraph can use the latter to cancel a stop inherited from this style.
        if (f.TabStops.Count > 0)
            pPr.Add(new XElement(W + "tabs",
                f.TabStops.Select(BuildTabStop)));

        // w:spacing carries before/after and line spacing — CT_PPrBase order: after bidi, before ind.
        // before/after emitted only when non-zero; line spacing only when it differs from the model
        // default (a multiple of 1.15), mirroring the per-paragraph writer.
        var hasLineSpacing = f.LineRule != LineSpacingRule.Multiple
            || System.Math.Abs(f.LineSpacing - ParagraphFormatting.Default.LineSpacing) > 0.0001;
        if (f.SpaceBeforePt > 0 || f.SpaceAfterPt > 0 || f.BeforeAutoSpacing || f.AfterAutoSpacing || hasLineSpacing)
        {
            var spacing = new XElement(W + "spacing");
            var hasNumericSpacing = !f.BeforeAutoSpacing && f.SpaceBeforePt > 0
                || !f.AfterAutoSpacing && f.SpaceAfterPt > 0;
            if (f.BeforeAutoSpacing)
                spacing.Add(new XAttribute(W + "beforeAutospacing", "1"));
            else if (hasNumericSpacing)
                spacing.Add(new XAttribute(W + "before", PointsToDxa(f.SpaceBeforePt)));
            if (f.AfterAutoSpacing)
                spacing.Add(new XAttribute(W + "afterAutospacing", "1"));
            else if (hasNumericSpacing)
                spacing.Add(new XAttribute(W + "after", PointsToDxa(f.SpaceAfterPt)));
            if (hasLineSpacing)
            {
                var (line, rule) = f.LineRule switch
                {
                    LineSpacingRule.Exact => ((int)System.Math.Round(f.LineHeightPt * 20), "exact"),
                    LineSpacingRule.AtLeast => ((int)System.Math.Round(f.LineHeightPt * 20), "atLeast"),
                    _ => ((int)System.Math.Round(f.LineSpacing * 240), "auto")
                };
                spacing.Add(new XAttribute(W + "line", line));
                spacing.Add(new XAttribute(W + "lineRule", rule));
            }
            pPr.Add(spacing);
        }

        // w:ind (indents) — CT_PPrBase order: after spacing, before contextualSpacing/jc.
        // Negative FirstLineIndentPt is a hanging indent → emit w:hanging (unsigned).
        if (f.IndentLeftPt > 0 || f.IndentRightPt > 0 || f.FirstLineIndentPt != 0)
        {
            var indEl = new XElement(W + "ind",
                new XAttribute(W + "left", PointsToDxa(f.IndentLeftPt)),
                new XAttribute(W + "right", PointsToDxa(f.IndentRightPt)));
            if (f.FirstLineIndentPt < 0)
                indEl.Add(new XAttribute(W + "hanging", PointsToDxa(-f.FirstLineIndentPt)));
            else if (f.FirstLineIndentPt > 0)
                indEl.Add(new XAttribute(W + "firstLine", PointsToDxa(f.FirstLineIndentPt)));
            pPr.Add(indEl);
        }

        // w:jc (alignment) — CT_PPrBase order: after ind, before textDirection.
        if (f.ContextualSpacing is { } contextualSpacing)
            pPr.Add(new XElement(W + "contextualSpacing",
                contextualSpacing ? null : new XAttribute(W + "val", "0")));
        if (f.Alignment != TextAlignment.Left)
            pPr.Add(new XElement(W + "jc", new XAttribute(W + "val", f.Alignment switch
            {
                TextAlignment.Center => "center",
                TextAlignment.Right => "right",
                TextAlignment.Justify => "both",
                _ => "left"
            })));

        return pPr.HasElements ? pPr : null;
    }

    /// <summary>
    /// Strips XML-1.0-illegal characters from <paramref name="text"/> before it is written into a
    /// <c>w:t</c>, <c>w:delText</c>, or <c>w:instrText</c> element. A control character in run text (e.g.
    /// U+0001, which can arrive from RTF import) causes <see cref="XDocument.Save"/> to throw
    /// <see cref="ArgumentException"/>, producing no file at all. This sanitizer removes the illegal code
    /// points (U+0000–U+0008, U+000B, U+000C, U+000E–U+001F, U+FFFE, U+FFFF) and lone/unpaired surrogates,
    /// while preserving tab (U+0009), LF (U+000A), CR (U+000D), and all valid BMP and supplementary
    /// characters. Null and empty inputs are returned as-is.
    /// </summary>
    private static string SanitizeXmlText(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return text ?? string.Empty;

        // Fast path: scan for any illegal character; return original when none found.
        var needsSanitize = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (IsXmlIllegal(c, text, ref i))
            {
                needsSanitize = true;
                break;
            }
        }
        if (!needsSanitize)
            return text;

        var sb = new System.Text.StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (char.IsHighSurrogate(c))
            {
                // Keep a valid surrogate pair; drop a lone high surrogate.
                if (i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                {
                    sb.Append(c);
                    sb.Append(text[++i]);
                }
                // else: lone high surrogate — drop it
            }
            else if (!char.IsLowSurrogate(c) && !IsXml10IllegalChar(c))
            {
                // Lone low surrogates are also illegal in XML; skip them.
                sb.Append(c);
            }
            // else: illegal char (C0/C1 control or lone surrogate) — drop it
        }
        return sb.ToString();

        // Returns true when position i contains an illegal XML 1.0 character (lone surrogate or C0/C1 control).
        // The ref i allows advancing past the second char of a surrogate pair on the fast-path scan.
        static bool IsXmlIllegal(char c, string s, ref int i)
        {
            if (char.IsHighSurrogate(c))
            {
                if (i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
                {
                    i++; // valid pair — skip
                    return false;
                }
                return true; // lone high surrogate
            }
            if (char.IsLowSurrogate(c))
                return true; // lone low surrogate
            return IsXml10IllegalChar(c);
        }

        static bool IsXml10IllegalChar(char c) =>
            // XML 1.0 legal: #x9 | #xA | #xD | [#x20-#xD7FF] | [#xE000-#xFFFD]
            c != '\t' && c != '\n' && c != '\r' && (c < ' ' || c == '￾' || c == '￿');
    }

    /// <summary>
    /// Maps a <c>#RRGGBB</c> hex color to the <c>w:highlight/@w:val</c> named token used by Word's
    /// highlight gallery, or <c>null</c> when the color has no named equivalent. Comparison is
    /// case-insensitive against the canonical uppercase hex values.
    /// </summary>
    private static string? HexToHighlightToken(string hex)
    {
        var normalized = hex.TrimStart('#').ToUpperInvariant();
        return normalized switch
        {
            "FFFF00" => "yellow",
            "00FF00" => "green",
            "00FFFF" => "cyan",
            "FF00FF" => "magenta",
            "0000FF" => "blue",
            "FF0000" => "red",
            "000080" => "darkBlue",
            "008080" => "darkCyan",
            "008000" => "darkGreen",
            "800080" => "darkMagenta",
            "800000" => "darkRed",
            "808000" => "darkYellow",
            "808080" => "darkGray",
            "C0C0C0" => "lightGray",
            "000000" => "black",
            "FFFFFF" => "white",
            _ => null
        };
    }
}
