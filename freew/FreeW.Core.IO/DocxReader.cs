using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using Free.Shared.Drawing;
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
    private static readonly XNamespace Mc = "http://schemas.openxmlformats.org/markup-compatibility/2006";
    private const string FreeWChartDesignExtensionUri = "urn:freew:chart-design:2026";
    private const string LegacyFreeWChartDesignExtensionUri = "{FW-ChartDesign-2026}";

    private sealed class DuplicateDrawingIdentityMarker
    {
        public static readonly DuplicateDrawingIdentityMarker Instance = new();
    }

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

        var document = new TextDocument
        {
            // Word applies its application paragraph default when the package cascade contains no
            // w:spacing/@w:line token. Keep that import provenance separate from model-authored documents,
            // whose implicit paragraph default intentionally follows the host's natural single-line box.
            UseWordApplicationDefaultLineSpacing = true,
            UseWordApplicationDefaultRunFormatting = true,
            // Current Word resolves a package with no w:rPrDefault size to a 12-point application
            // default. Keep model-authored FreeW documents at their existing 11-point default; an
            // explicit package size read below still overrides this import fallback.
            DefaultRun = RunFormatting.Default with { FontFamily = "Calibri", FontSizePt = 12 }
        };
        var corePropertiesXml = LoadPart(archive, OpcPackageProperties.CorePropertiesZipEntry);
        if (corePropertiesXml?.Root is { } corePropertiesRoot
            && corePropertiesRoot.Elements().Any(element =>
                !OpcDocumentProperties.ModeledCorePropertyElementNames.Contains(element.Name)))
        {
            document.Preserved.OriginalCoreProperties = new XElement(corePropertiesRoot);
        }
        document.Properties.ApplyCoreProperties(
            OpcDocumentProperties.ReadCoreProperties(corePropertiesXml),
            emptyStringsAsNull: true);
        ReadCustomProperties(archive, document);
        ReadStyles(archive, document);
        var imageRelationships = ReadImageRelationships(archive);
        var hyperlinkRelationships = ReadHyperlinkRelationships(archive);
        var altChunkRelationships = ReadAltChunkRelationships(archive);
        var subDocumentRelationships = ReadSubDocumentRelationships(archive);
        var (numbering, startOverrides) = ReadNumbering(archive, document);

        MarkDuplicateDrawingIdentities(documentXml.Root);

        var body = documentXml.Root?.Element(W + "body");
        if (body is not null)
        {
            // Word suppresses automatic paragraph spacing (w:before/afterAutospacing) BETWEEN two
            // consecutive auto-spaced paragraphs — a block (e.g. an HTML-paste list) reads as tight, with
            // the auto space only before the first and after the last. Track the flags across the body so
            // the between-space can be dropped; otherwise FreeW spaces every item and runs much looser than
            // Word down the page.
            Paragraph? prevPara = null;
            var prevAfterAuto = false;
            foreach (var element in body.Elements())
                AddBodyBlock(element, document, archive, imageRelationships, hyperlinkRelationships, altChunkRelationships, subDocumentRelationships, numbering, startOverrides, ref prevPara, ref prevAfterAuto);
        }

        if (document.Blocks.Count == 0)
            document.Blocks.Add(new Paragraph());

        ReadHeaderFooter(documentXml, archive, document, hyperlinkRelationships, numbering);
        ReadNativeVmlWatermark(archive, document);
        ReadFootnotes(archive, document, imageRelationships, hyperlinkRelationships, numbering);
        ReadEndnotes(archive, document, imageRelationships, hyperlinkRelationships, numbering);
        ReadComments(archive, document, hyperlinkRelationships, numbering);
        ReadSettings(archive, document);
        // w:evenAndOddHeaders and w:mirrorMargins are document-global toggles stored in settings.xml, read
        // into document.Page by ReadSettings. Non-final sections' PageSettings are constructed earlier (during
        // body parsing), before ReadSettings runs, so their DifferentOddEvenPages/MirrorMargins stay false.
        // Propagate the document-wide values now so the writer's per-section even-part emission gate
        // (which keys off section.Page.DifferentOddEvenPages) correctly emits even header/footer parts for
        // every non-final section whose sectPr carried even header/footer references.
        if (document.Page.DifferentOddEvenPages || document.Page.MirrorMargins)
        {
            foreach (var block in document.Blocks)
            {
                if (block is Paragraph { SectionBreak: { } section })
                {
                    if (document.Page.DifferentOddEvenPages)
                        section.Page.DifferentOddEvenPages = true;
                    if (document.Page.MirrorMargins)
                        section.Page.MirrorMargins = true;
                }
            }
        }
        ReadBibliography(archive, document);
        ReadTheme(archive, document);
        ReadEmbeddedFonts(archive, document);
        ReadPreservedParts(archive, document, documentXml);

        return document;
    }

    private static void MarkDuplicateDrawingIdentities(XElement? root)
    {
        if (root is null)
            return;

        var claimedIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var docPr in root.Descendants(Wp + "docPr"))
        {
            var id = docPr.Attribute("id")?.Value;
            if (string.IsNullOrWhiteSpace(id) || claimedIds.Add(id))
                continue;

            docPr.Parent?.AddAnnotation(DuplicateDrawingIdentityMarker.Instance);
        }
    }

    /// <summary>
    /// Resolves and parses word/theme/theme1.xml (via the document's "/theme" relationship, falling back
    /// to the conventional path), recovering the a:clrScheme colours and the a:fontScheme major/minor
    /// fonts and the a:fmtScheme name, then inferring the closest <see cref="DocumentTheme"/> preset (see
    /// <see cref="DocumentTheme.InferPreset"/>). A missing or unparseable theme part leaves the document
    /// at <see cref="DocumentTheme.Default"/> ("Office"). Inference is best-effort: a theme whose accent
    /// colours / fonts match no FreeW preset falls back to "Office".
    /// </summary>
    private static void ReadTheme(ZipArchive archive, TextDocument document)
    {
        var sharedTheme = DrawingMlThemeReader.TryReadThemePart(archive, "word/document.xml", "word/theme/theme1.xml");
        if (sharedTheme is null)
            return;

        string Slot(DrawingMlThemeColorSlot slot) =>
            sharedTheme.ColorScheme[slot] is { } color
                ? color.ResolvedColor.ToHexRgb()
                : string.Empty;

        var scheme = new ThemeColorScheme(
            Slot(DrawingMlThemeColorSlot.Dark1), Slot(DrawingMlThemeColorSlot.Light1),
            Slot(DrawingMlThemeColorSlot.Dark2), Slot(DrawingMlThemeColorSlot.Light2),
            Slot(DrawingMlThemeColorSlot.Accent1), Slot(DrawingMlThemeColorSlot.Accent2),
            Slot(DrawingMlThemeColorSlot.Accent3), Slot(DrawingMlThemeColorSlot.Accent4),
            Slot(DrawingMlThemeColorSlot.Accent5), Slot(DrawingMlThemeColorSlot.Accent6),
            Slot(DrawingMlThemeColorSlot.Hyperlink), Slot(DrawingMlThemeColorSlot.FollowedHyperlink));

        document.Theme = DocumentTheme.InferPreset(
            scheme,
            sharedTheme.FontScheme.MajorLatinTypeface ?? string.Empty,
            sharedTheme.FontScheme.MinorLatinTypeface ?? string.Empty,
            sharedTheme.FormatSchemeName);
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
        // Hyphenation sub-options: w:consecutiveHyphenLimit/@w:val (max consecutive hyphenated lines),
        // w:hyphenationZone/@w:val (zone in twips) and the w:doNotHyphenateCaps toggle. Each is read whether
        // or not autoHyphenation is on (so the value round-trips), defaulting to off/0 when absent.
        if (int.TryParse(root.Element(W + "consecutiveHyphenLimit")?.Attribute(W + "val")?.Value,
                System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var hyphenLimit) && hyphenLimit > 0)
            document.Page.ConsecutiveHyphenLimit = hyphenLimit;
        if (int.TryParse(root.Element(W + "hyphenationZone")?.Attribute(W + "val")?.Value,
                System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var hyphenZone) && hyphenZone > 0)
            document.Page.HyphenationZonePt = hyphenZone / 20.0;
        document.Page.DoNotHyphenateCaps = ReadToggle(root, "doNotHyphenateCaps");
        if (int.TryParse(root.Element(W + "defaultTabStop")?.Attribute(W + "val")?.Value,
                System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var defaultTabStop) && defaultTabStop > 0)
            document.Page.DefaultTabStopPt = defaultTabStop / 20.0;

        // Different odd/even page headers/footers (w:evenAndOddHeaders): an on/off toggle. When set, the
        // even header/footer references in w:sectPr are honoured (see ReadHeaderFooter).
        document.Page.DifferentOddEvenPages = ReadToggle(root, "evenAndOddHeaders");

        // Mirror margins (w:mirrorMargins): an on/off toggle for double-sided printing (inside/outside margins).
        document.Page.MirrorMargins = ReadToggle(root, "mirrorMargins");

        // Footnote numbering options (w:footnotePr in settings.xml): number format, start-at, restart.
        if (root.Element(W + "footnotePr") is { } footnotePr)
            ReadNoteNumberingOptions(footnotePr, document.FootnoteNumbering);

        // Endnote numbering options (w:endnotePr in settings.xml): mirrors footnote options.
        if (root.Element(W + "endnotePr") is { } endnotePr)
            ReadNoteNumberingOptions(endnotePr, document.EndnoteNumbering);

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
        {
            // Read optional password hash attributes (OOXML legacy hash format: w:hash + w:salt + w:cryptSpinCount).
            var hash = protection.Attribute(W + "hash")?.Value;
            var salt = protection.Attribute(W + "salt")?.Value;
            var spinCountStr = protection.Attribute(W + "cryptSpinCount")?.Value;
            var spinCount = int.TryParse(spinCountStr, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var sc) && sc > 0 ? sc : 50000;

            document.Protection = new ProtectionSettings(mode)
            {
                PasswordHash = string.IsNullOrEmpty(hash) ? null : hash,
                PasswordSalt = string.IsNullOrEmpty(salt) ? null : salt,
                SpinCount = spinCount
            };
        }
    }

    /// <summary>
    /// Reads footnote/endnote numbering properties (w:numFmt, w:numStart, w:numRestart) from a
    /// w:footnotePr or w:endnotePr element into <paramref name="options"/>.
    /// </summary>
    private static void ReadNoteNumberingOptions(XElement pr, NoteNumberingOptions options)
    {
        if (pr.Element(W + "numFmt") is { } numFmt)
            options.NumberFormat = NoteNumberFormatFromOoxml(numFmt.Attribute(W + "val")?.Value);
        if (pr.Element(W + "numStart") is { } numStart &&
            int.TryParse(numStart.Attribute(W + "val")?.Value,
                System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var startVal) && startVal >= 1)
            options.StartAt = startVal;
        if (pr.Element(W + "numRestart") is { } numRestart)
            options.NumberRestart = NoteNumberRestartFromOoxml(numRestart.Attribute(W + "val")?.Value);
    }

    private static NoteNumberFormat NoteNumberFormatFromOoxml(string? val) => val switch
    {
        "lowerRoman" => NoteNumberFormat.LowerRoman,
        "upperRoman" => NoteNumberFormat.UpperRoman,
        "lowerLetter" => NoteNumberFormat.LowerLetter,
        "upperLetter" => NoteNumberFormat.UpperLetter,
        "chicago" => NoteNumberFormat.Chicago,
        _ => NoteNumberFormat.Decimal   // "decimal" and anything unrecognised → default
    };

    private static NoteNumberRestart NoteNumberRestartFromOoxml(string? val) => val switch
    {
        "eachSect" => NoteNumberRestart.EachSection,
        "eachPage" => NoteNumberRestart.EachPage,
        _ => NoteNumberRestart.Continuous
    };

    /// <summary>
    /// Captures the package parts FreeW does not model but preserves verbatim (preserve-and-re-emit):
    /// <c>word/webSettings.xml</c>, every <c>customXml/*</c> part (each item, its props, and the item's own
    /// <c>_rels</c>), the local relationship graph of preserved <c>word/settings.xml</c>, and
    /// <c>word/glossary/*</c> building-block parts. Each captured part records its raw bytes plus — when it has them — its
    /// <c>[Content_Types].xml</c> Override and the document→part relationship type, so the writer can re-emit
    /// the part, its content type and its relationship unchanged. A document with none of these parts (authored
    /// from scratch) captures nothing, so it round-trips byte-equivalently to before.
    /// </summary>
    private static void ReadPreservedParts(ZipArchive archive, TextDocument document, XDocument documentXml)
    {
        // Map each part name → its content-type Override (so a re-emitted part keeps its declared type), and
        // each document-relationship Target → its Type (so a re-emitted part keeps its document relationship).
        var overrides = ReadContentTypeOverrides(archive);
        var contentTypeDefaults = ReadContentTypeDefaults(archive);
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

        // Word 2013+ stores supplemental style-effect and comment-author parts beside FreeW's modeled content.
        // Preserve their package graphs so Word can rehydrate richer style rendering and author identity after
        // a FreeW save.
        foreach (var relationship in ReadDocumentRelationships(archive).Values)
        {
            if (relationship.Type is not (StylesWithEffectsRelType or PeopleRelType or CommentsIdsRelType or CommentsExtensibleRelType or KeyMapCustomizationRelType or DocumentTasksRelType))
                continue;

            var partName = OpcPathHelper.ResolveAbsolutePartName("/word", relationship.Target);
            if (partName is not null && CapturePreservedPart(
                    archive,
                    document,
                    partName,
                    overrides,
                    contentTypeDefaults,
                    relationship.Type))
            {
                CaptureReferencedParts(archive, document, partName, overrides, contentTypeDefaults);
            }
        }

        // w:settings itself is overlaid from OriginalSettings on write, but its local relationship graph is
        // not modelled. In particular, w:attachedTemplate/@r:id depends on settings.xml.rels. Preserve that
        // graph verbatim so a Word-attached template remains connected after FreeW saves the document.
        if (document.Preserved.OriginalSettings is not null)
            CaptureReferencedParts(archive, document, SettingsPartName, overrides, contentTypeDefaults);

        // Body-level altChunk imports are unresolved source payloads (HTML, RTF, or a nested Word package) that
        // Word imports on open. Keep the body marker plus its payload and any local relationship graph intact.
        foreach (var altChunk in document.Blocks.OfType<AltChunkBlock>())
        {
            CapturePreservedPart(
                archive,
                document,
                altChunk.PreservedPartName,
                overrides,
                contentTypeDefaults,
                AltChunkRelType);
            CaptureReferencedParts(
                archive,
                document,
                altChunk.PreservedPartName,
                overrides,
                contentTypeDefaults);
        }

        // word/glossary/*: Word building blocks / AutoText live in a glossary document part plus optional
        // glossary-local rels, styles, media and other satellites. Preserve the glossary subtree as package
        // inventory and only mark the glossary document itself as document-referenced.
        var glossaryDocumentParts = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var rel in ReadDocumentRelationships(archive).Values)
        {
            if (rel.Type != GlossaryDocumentRelType)
                continue;
            var partName = OpcPathHelper.ResolveAbsolutePartName("/word", rel.Target);
            if (partName is not null)
                glossaryDocumentParts[partName] = rel.Type;
        }
        if (archive.GetEntry(GlossaryDocumentPartName.TrimStart('/')) is not null)
            glossaryDocumentParts.TryAdd(
                GlossaryDocumentPartName,
                docRelTypesByTarget.GetValueOrDefault("glossary/document.xml") ?? GlossaryDocumentRelType);

        foreach (var entry in archive.Entries)
        {
            var name = entry.FullName;
            if (!name.StartsWith("word/glossary/", StringComparison.Ordinal) || name.EndsWith("/", StringComparison.Ordinal))
                continue;
            var partName = "/" + name;
            CapturePreservedPart(
                archive,
                document,
                partName,
                overrides,
                contentTypeDefaults,
                glossaryDocumentParts.GetValueOrDefault(partName));
        }

        // Package-level extended properties (docProps/app.xml) are not modelled by FreeW, but Word-authored
        // documents commonly use them for application/company/template metadata.
        if (archive.GetEntry(OpcPackageProperties.ExtendedPropertiesZipEntry) is not null)
            Capture(OpcPackageProperties.ExtendedPropertiesPartName, relationshipType: null);

        // VBA macro project (.docm/.dotm): word/vbaProject.bin, its optional word/vbaData.xml, and the
        // part-local word/_rels/vbaProject.bin.rels. Preserved verbatim and NEVER executed/deserialized. The
        // content type is forced to a per-part Override (valid OPC, and wins over any source Default-by-
        // extension) so a macro-only document re-emits a typed part even though FreeW emits no bin/vbaData
        // Default. The writer drops these again for non-macro variants (.docx/.dotx) via DocxWriteOptions.
        if (archive.GetEntry("word/vbaProject.bin") is not null)
        {
            var vbaProject = LoadMedia(archive, "word/vbaProject.bin");
            if (vbaProject is not null)
            {
                document.Preserved.Parts.Add(new PreservedPart(
                    "/word/vbaProject.bin",
                    vbaProject,
                    VbaProjectContentType,
                    docRelTypesByTarget.GetValueOrDefault("vbaProject.bin") ?? VbaProjectRelType));

                var vbaData = LoadMedia(archive, "word/vbaData.xml");
                if (vbaData is not null)
                    document.Preserved.Parts.Add(new PreservedPart("/word/vbaData.xml", vbaData, VbaDataContentType, null));

                var vbaRels = LoadMedia(archive, "word/_rels/vbaProject.bin.rels");
                if (vbaRels is not null)
                    document.Preserved.Parts.Add(new PreservedPart("/word/_rels/vbaProject.bin.rels", vbaRels, null, null));
            }
        }

        // Some Word features are rooted from the package relationship part rather than document.xml.rels:
        // custom Ribbons, thumbnails, and extension-specific package parts. Preserve every root-owned part
        // FreeW does not write itself with its local relationship graph, so Word can rehydrate it after save.
        foreach (var relationship in OpcRelationships.Load(archive, "_rels/.rels"))
        {
            if (relationship.IsExternal
                || string.IsNullOrEmpty(relationship.Target)
                || IsWriterOwnedPackageRelationship(relationship.Type))
                continue;

            var partName = OpcPathHelper.ResolveAbsolutePartName("/", relationship.Target);
            if (partName is null)
                continue;

            if (CapturePreservedPart(
                    archive,
                    document,
                    partName,
                    overrides,
                    contentTypeDefaults,
                    relationshipType: null,
                    packageRelationshipType: relationship.Type))
            {
                CaptureReferencedParts(archive, document, partName, overrides, contentTypeDefaults);
            }
        }

        // Word task-pane add-ins place their document-level marker in w:webExtensions. The marker's r:id
        // resolves to word/webextensions/taskpanes.xml, which in turn owns the extension payload graph.
        // Preserve the marker and remap its document relationship when FreeW writes a fresh package.
        if (documentXml.Root?.Element(W + "webExtensions") is { } webExtensions)
        {
            var documentRelationships = ReadDocumentRelationships(archive);
            var references = new List<PreservedDocumentReference>();
            var complete = true;
            foreach (var relationshipId in webExtensions.DescendantsAndSelf()
                         .Attributes(R + "id")
                         .Select(attribute => attribute.Value)
                         .Distinct(StringComparer.Ordinal))
            {
                if (!documentRelationships.TryGetValue(relationshipId, out var relationship))
                {
                    complete = false;
                    break;
                }

                var partName = OpcPathHelper.ResolveAbsolutePartName("/word", relationship.Target);
                if (partName is null || !CapturePreservedPart(
                        archive,
                        document,
                        partName,
                        overrides,
                        contentTypeDefaults,
                        relationship.Type))
                {
                    complete = false;
                    break;
                }

                CaptureReferencedParts(archive, document, partName, overrides, contentTypeDefaults);
                references.Add(new PreservedDocumentReference(relationshipId, partName));
            }

            if (complete)
            {
                document.Preserved.WebExtensions = new PreservedWebExtensions(
                    webExtensions.ToString(SaveOptions.DisableFormatting),
                    references);
            }
        }

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
        => OpcMediaTypes.ReadOverrideContentTypes(archive);

    /// <summary>
    /// Reads <c>word/_rels/document.xml.rels</c>, mapping each relationship Target → its Type. Targets are kept
    /// exactly as written (e.g. "webSettings.xml", "../customXml/item1.xml") so a preserved part can recover the
    /// relationship type the document used to reference it. Returns an empty map when the rels part is absent.
    /// </summary>
    private static Dictionary<string, string> ReadDocumentRelationshipTypesByTarget(ZipArchive archive)
        => OpcRelationships.LoadTypeByTargetMap(archive, "word/_rels/document.xml.rels");

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
    private static Dictionary<string, string> ReadFontTableRelationships(ZipArchive archive) =>
        OpcRelationships.LoadTargetMap(
            archive,
            "word/_rels/fontTable.xml.rels",
            relationship => OpcPathHelper.ResolveRelativeZipPath("word", relationship.Target),
            relationship => !relationship.IsExternal);

    /// <summary>
    /// Finds a document-relationship target by the suffix of its Type (e.g. "/fontTable"), resolved
    /// relative to the word/ folder. Returns null when no such relationship exists. Generalises the
    /// settings/theme resolvers.
    /// </summary>
    private static string? ResolveDocumentRelPartPath(ZipArchive archive, string typeSuffix)
        => ResolveDocumentRelationshipPartPath(
            archive,
            relationship => relationship.Type.EndsWith(typeSuffix, StringComparison.Ordinal));

    private static string? ResolveDocumentRelationshipPartPath(
        ZipArchive archive,
        Func<OpcRelationship, bool> predicate)
    {
        foreach (var relationship in OpcRelationships.Load(archive, "word/_rels/document.xml.rels"))
        {
            if (relationship.IsExternal ||
                string.IsNullOrEmpty(relationship.Target) ||
                !predicate(relationship))
            {
                continue;
            }

            return OpcPathHelper.ResolveRelativeZipPath("word", relationship.Target);
        }

        return null;
    }

    /// <summary>
    /// Finds the settings part path from the document relationships (the rel whose Type ends with
    /// "/settings"), resolved relative to the word/ folder. Returns null when no such relationship exists.
    /// </summary>
    private static string? ResolveSettingsPartPath(ZipArchive archive)
        => ResolveDocumentRelPartPath(archive, "/settings");

    /// <summary>
    /// Loads Word's current b:Sources custom XML item, falling back to word/bibliography/sources.xml, into
    /// <see cref="TextDocument.Sources"/> and <see cref="TextDocument.BibliographyStyle"/>: the
    /// b:Sources/@StyleName or @SelectedStyle attribute restores the chosen citation style (via
    /// <see cref="Citations.ParseStyle"/>), and each b:Source restores a
    /// <see cref="Source"/> with its tag, type and fields (the author read from the single corporate-author
    /// the writer emits). A missing part leaves the document at its defaults (APA, no sources). Inverse of
    /// <c>DocxWriter.BuildBibliographySources</c>.
    /// </summary>
    private static void ReadBibliography(ZipArchive archive, TextDocument document)
    {
        var xml = LoadPart(archive, ResolveCurrentBibliographyPartPath(archive)
            ?? ResolveBibliographyPartPath(archive)
            ?? "word/bibliography/sources.xml");
        var root = xml?.Root;
        if (root is null || root.Name != B + "Sources")
            return;

        document.BibliographyStyle = Citations.ParseStyle(
            root.Attribute("StyleName")?.Value
            ?? root.Attribute("SelectedStyle")?.Value);

        foreach (var element in root.Elements(B + "Source"))
        {
            var author = ReadBibliographyAuthor(element);
            var editors = ReadBibliographyPersonalContributors(element, "Editor");
            var translators = ReadBibliographyPersonalContributors(element, "Translator");
            var inventor = ReadBibliographyContributorDisplay(element, "Inventor");
            var interviewee = ReadBibliographyContributorDisplay(element, "Interviewee");
            var interviewer = ReadBibliographyContributorDisplay(element, "Interviewer");
            var artist = ReadBibliographyContributorDisplay(element, "Artist");
            var composer = ReadBibliographyContributorDisplay(element, "Composer");
            var conductor = ReadBibliographyContributorDisplay(element, "Conductor");
            var director = ReadBibliographyContributorDisplay(element, "Director");
            var performer = ReadBibliographyContributorDisplay(element, "Performer");
            var producerName = ReadBibliographyContributorDisplay(element, "ProducerName");
            var writer = ReadBibliographyContributorDisplay(element, "Writer");
            var dayAccessed = Field(element, "DayAccessed");
            var monthAccessed = Field(element, "MonthAccessed");
            var yearAccessed = Field(element, "YearAccessed");
            var hasStructuredAccessedDate = dayAccessed is not null || monthAccessed is not null;

            document.Sources.Add(new Source
            {
                Tag = Field(element, "Tag") ?? string.Empty,
                Type = ParseSourceType(Field(element, "SourceType")),
                Author = author.DisplayText,
                PersonalAuthors = author.PersonalAuthors,
                CorporateAuthor = author.CorporateAuthor,
                Editors = editors,
                Translators = translators,
                Title = Field(element, "Title") ?? string.Empty,
                BookTitle = Field(element, "BookTitle"),
                ConferenceName = Field(element, "ConferenceName"),
                Inventor = inventor,
                Interviewee = interviewee,
                Interviewer = interviewer,
                Artist = artist,
                Composer = composer,
                Conductor = conductor,
                Director = director,
                Performer = performer,
                ProducerName = producerName,
                Writer = writer,
                Year = Field(element, "Year") ?? string.Empty,
                Month = Field(element, "Month"),
                Day = Field(element, "Day"),
                Institution = Field(element, "Institution"),
                Publisher = Field(element, "Publisher"),
                City = Field(element, "City"),
                Edition = Field(element, "Edition"),
                StandardNumber = Field(element, "StandardNumber"),
                ChapterNumber = Field(element, "ChapterNumber"),
                PatentNumber = Field(element, "PatentNumber"),
                CaseNumber = Field(element, "CaseNumber"),
                Court = Field(element, "Court"),
                Reporter = Field(element, "Reporter"),
                CountryRegion = Field(element, "CountryRegion"),
                StateProvince = Field(element, "StateProvince"),
                Medium = Field(element, "Medium"),
                SourceKind = Field(element, "Type"),
                AlbumTitle = Field(element, "AlbumTitle"),
                ProductionCompany = Field(element, "ProductionCompany"),
                RecordingNumber = Field(element, "RecordingNumber"),
                Theater = Field(element, "Theater"),
                ShortTitle = Field(element, "ShortTitle"),
                Comments = Field(element, "Comments"),
                Journal = Field(element, "JournalName"),
                Volume = Field(element, "Volume"),
                Issue = Field(element, "Issue"),
                Pages = Field(element, "Pages"),
                Url = Field(element, "URL"),
                Accessed = hasStructuredAccessedDate ? null : yearAccessed,
                AccessedDay = dayAccessed,
                AccessedMonth = monthAccessed,
                AccessedYear = yearAccessed,
            });
        }

        static string? Field(XElement source, string localName)
        {
            var value = source.Element(B + localName)?.Value;
            return string.IsNullOrEmpty(value) ? null : value;
        }
    }

    private static BibliographyAuthorInfo ReadBibliographyAuthor(XElement source)
    {
        var role = source.Element(B + "Author")?.Element(B + "Author");
        if (role is null)
            return BibliographyAuthorInfo.Empty;

        var corporate = role.Element(B + "Corporate")?.Value;
        if (!string.IsNullOrWhiteSpace(corporate))
        {
            var trimmed = corporate.Trim();
            return new BibliographyAuthorInfo(trimmed, [], trimmed);
        }

        var people = ReadPeople(role)
            .ToList();
        if (people.Count > 0)
            return new BibliographyAuthorInfo(SourceAuthorPerson.FormatDisplayText(people), people, CorporateAuthor: null);

        return new BibliographyAuthorInfo((role.Value ?? string.Empty).Trim(), [], CorporateAuthor: null);
    }

    private static IReadOnlyList<SourceAuthorPerson> ReadBibliographyPersonalContributors(
        XElement source,
        string roleName)
    {
        var role = source.Element(B + "Author")?.Element(B + roleName);
        if (role is null)
            return [];

        return ReadPeople(role).ToList();
    }

    private static string? ReadBibliographyContributorDisplay(XElement source, string roleName)
    {
        var role = source.Element(B + "Author")?.Element(B + roleName);
        if (role is null)
            return null;

        var corporate = role.Element(B + "Corporate")?.Value;
        if (!string.IsNullOrWhiteSpace(corporate))
            return corporate.Trim();

        var people = ReadPeople(role).ToList();
        if (people.Count > 0)
            return SourceAuthorPerson.FormatDisplayText(people);

        var value = (role.Value ?? string.Empty).Trim();
        return value.Length == 0 ? null : value;
    }

    private static IEnumerable<SourceAuthorPerson> ReadPeople(XElement role) =>
        role.Element(B + "NameList")?
            .Elements(B + "Person")
            .Select(Person)
            .Where(person => !person.IsEmpty)
        ?? [];

    private static SourceAuthorPerson Person(XElement person) =>
        SourceAuthorPerson.Create(
            person.Element(B + "First")?.Value,
            person.Element(B + "Middle")?.Value,
            person.Element(B + "Last")?.Value);

    private sealed record BibliographyAuthorInfo(
        string DisplayText,
        IReadOnlyList<SourceAuthorPerson> PersonalAuthors,
        string? CorporateAuthor)
    {
        public static readonly BibliographyAuthorInfo Empty = new(string.Empty, [], null);
    }

    // Maps Word's b:SourceType token back to a FreeW SourceType; unknown / missing -> Book.
    private static SourceType ParseSourceType(string? token) => token switch
    {
        "JournalArticle" => SourceType.JournalArticle,
        "DocumentFromInternetSite" => SourceType.WebSite,
        "Report" => SourceType.Report,
        "BookSection" => SourceType.BookSection,
        "ConferenceProceedings" => SourceType.ConferenceProceedings,
        "ArticleInAPeriodical" => SourceType.ArticleInPeriodical,
        "ElectronicSource" => SourceType.ElectronicSource,
        "Patent" => SourceType.Patent,
        "Interview" => SourceType.Interview,
        "Misc" => SourceType.Misc,
        "Film" => SourceType.Film,
        "SoundRecording" => SourceType.SoundRecording,
        "Art" => SourceType.Art,
        "InternetSite" => SourceType.InternetSite,
        "Performance" => SourceType.Performance,
        "Case" => SourceType.Case,
        _ => SourceType.Book,
    };

    /// <summary>
    /// Finds the bibliography part path from the document relationships (the rel whose Target ends with
    /// "bibliography/sources.xml"), resolved relative to the word/ folder. Returns null when no such
    /// relationship exists so the caller can fall back to the conventional path.
    /// </summary>
    private static string? ResolveBibliographyPartPath(ZipArchive archive)
        => ResolveDocumentRelationshipPartPath(
            archive,
            relationship => relationship.Target.EndsWith("bibliography/sources.xml", StringComparison.Ordinal));

    /// <summary>
    /// Finds Word's current document bibliography store. Word reads citation sources from a bibliography
    /// <c>b:Sources</c> custom XML item, not from the legacy <c>word/bibliography/sources.xml</c> mirror.
    /// The item name is not fixed, so inspect custom XML item roots rather than assuming item1.xml.
    /// </summary>
    private static string? ResolveCurrentBibliographyPartPath(ZipArchive archive)
    {
        foreach (var entry in archive.Entries)
        {
            if (!entry.FullName.StartsWith("customXml/item", StringComparison.Ordinal)
                || !entry.FullName.EndsWith(".xml", StringComparison.Ordinal)
                || entry.FullName.Contains("itemProps", StringComparison.Ordinal))
            {
                continue;
            }

            var xml = LoadPart(archive, entry.FullName);
            if (xml?.Root?.Name == B + "Sources")
                return entry.FullName;
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
        IReadOnlyDictionary<string, string> hyperlinkRelationships,
        IReadOnlyDictionary<int, ListKind> numbering)
    {
        var commentsXml = LoadPart(archive, "word/comments.xml");
        var root = commentsXml?.Root;
        if (root is null)
            return;

        // Comment-part images are referenced from word/_rels/comments.xml.rels (NOT document.xml.rels), so a
        // comment image's r:embed resolves only against the comment part's own relationships. Read that map and
        // use it (in place of the body's image relationships) so an image inside a comment becomes a real
        // Run.Image — which the writer re-emits as a comment media part + comments.xml.rels (see BuildComments).
        var commentRelationships = ReadPartRelationships(archive, "word/comments.xml");
        var commentHyperlinks = ReadPartHyperlinkRelationships(archive, "word/comments.xml");

        // Modern (threaded) comments: word/commentsExtended.xml threads replies via w15:paraId /
        // w15:paraIdParent and marks resolved threads with w15:done. Parse it first so the comment loop
        // can place each flat w:comment as either a top-level comment or a reply under its parent, and
        // recover the resolved flag. Absent (classic comments) → every comment is treated as top-level.
        var extended = ReadCommentsExtended(archive);

        // First pass: build every flat comment and remember its last-paragraph paraId (the value
        // commentsExtended threads on). Keep insertion order so reply threads keep their authored order.
        var flat = new List<(Comment Comment, string? ParaId)>();
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
            string? paraId = null;
            foreach (var p in ReadStoryParagraphs(element))
            {
                comment.Content.Add(ReadParagraph(
                    p,
                    archive,
                    commentRelationships,
                    commentHyperlinks,
                    numbering,
                    capturePreservedNumbering: true,
                    preservedDrawingTarget: document,
                    preservedDrawingRelationshipTargets: commentRelationships));
                // The last paragraph's w14:paraId is what commentsExtended references for this comment.
                if (p.Attribute(W14 + "paraId")?.Value is { Length: > 0 } pid)
                    paraId = pid;
            }
            if (comment.Content.Count == 0)
                comment.Content.Add(new Paragraph());
            flat.Add((comment, paraId));
        }

        // Second pass: thread. A flat comment whose paraId has a paraIdParent in commentsExtended is a
        // reply — append it to the parent's Replies (matched by the parent's paraId); otherwise it is a
        // top-level comment keyed in document.Comments. A top-level comment is resolved when its own
        // commentEx is marked done. Comments whose parent cannot be resolved fall back to top-level so
        // nothing is lost.
        var byParaId = new Dictionary<string, Comment>(StringComparer.Ordinal);
        foreach (var (comment, paraId) in flat)
            if (paraId is { Length: > 0 })
                byParaId[paraId] = comment;

        foreach (var (comment, paraId) in flat)
        {
            var ex = paraId is { Length: > 0 } && extended.TryGetValue(paraId, out var e) ? e : default;
            if (ex.ParentParaId is { Length: > 0 } parentParaId
                && byParaId.TryGetValue(parentParaId, out var parent)
                && !ReferenceEquals(parent, comment))
            {
                parent.Replies.Add(comment);
            }
            else
            {
                comment.Resolved = ex.Done;
                document.Comments[comment.Id] = comment;
            }
        }
    }

    /// <summary>
    /// Loads word/commentsExtended.xml (if present), returning a map from each comment's w15:paraId to its
    /// thread info: the w15:paraIdParent (null for a top-level comment) and whether w15:done marks it
    /// resolved. Empty when the part is absent (a classic, non-threaded comments document).
    /// </summary>
    private static Dictionary<string, (string? ParentParaId, bool Done)> ReadCommentsExtended(ZipArchive archive)
    {
        var map = new Dictionary<string, (string?, bool)>(StringComparer.Ordinal);
        var root = LoadPart(archive, "word/commentsExtended.xml")?.Root;
        if (root is null)
            return map;

        foreach (var ex in root.Elements(W15 + "commentEx"))
        {
            if (ex.Attribute(W15 + "paraId")?.Value is not { Length: > 0 } paraId)
                continue;
            var parent = ex.Attribute(W15 + "paraIdParent")?.Value;
            var done = ex.Attribute(W15 + "done")?.Value is "1" or "true";
            map[paraId] = (string.IsNullOrEmpty(parent) ? null : parent, done);
        }
        return map;
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
        IReadOnlyDictionary<string, string> hyperlinkRelationships,
        IReadOnlyDictionary<int, ListKind> numbering)
    {
        var footnotesXml = LoadPart(archive, "word/footnotes.xml");
        var root = footnotesXml?.Root;
        if (root is null)
            return;

        var noteRelationships = ReadPartRelationships(archive, "word/footnotes.xml");
        var noteHyperlinks = ReadPartHyperlinkRelationships(archive, "word/footnotes.xml");
        foreach (var element in root.Elements(W + "footnote"))
        {
            var type = element.Attribute(W + "type")?.Value;
            if (type is "separator" or "continuationSeparator")
                continue;
            if (!int.TryParse(element.Attribute(W + "id")?.Value, out var id))
                continue;

            var footnote = new Footnote(id);
            foreach (var p in ReadStoryParagraphs(element))
                footnote.Content.Add(ReadParagraph(
                    p,
                    archive,
                    noteRelationships,
                    noteHyperlinks,
                    numbering,
                    capturePreservedNumbering: true,
                    preservedDrawingTarget: document,
                    preservedDrawingRelationshipTargets: noteRelationships));
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
        IReadOnlyDictionary<string, string> hyperlinkRelationships,
        IReadOnlyDictionary<int, ListKind> numbering)
    {
        var endnotesXml = LoadPart(archive, "word/endnotes.xml");
        var root = endnotesXml?.Root;
        if (root is null)
            return;

        var noteRelationships = ReadPartRelationships(archive, "word/endnotes.xml");
        var noteHyperlinks = ReadPartHyperlinkRelationships(archive, "word/endnotes.xml");
        foreach (var element in root.Elements(W + "endnote"))
        {
            var type = element.Attribute(W + "type")?.Value;
            if (type is "separator" or "continuationSeparator")
                continue;
            if (!int.TryParse(element.Attribute(W + "id")?.Value, out var id))
                continue;

            var endnote = new Endnote(id);
            foreach (var p in ReadStoryParagraphs(element))
                endnote.Content.Add(ReadParagraph(
                    p,
                    archive,
                    noteRelationships,
                    noteHyperlinks,
                    numbering,
                    capturePreservedNumbering: true,
                    preservedDrawingTarget: document,
                    preservedDrawingRelationshipTargets: noteRelationships));
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
        IReadOnlyDictionary<string, string> hyperlinkRelationships,
        IReadOnlyDictionary<int, ListKind> numbering)
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
            sectPr, document.FinalSectionHeadersFooters, archive, document, hyperlinkRelationships, numbering);
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
        TextDocument document,
        IReadOnlyDictionary<string, string> hyperlinkRelationships,
        IReadOnlyDictionary<int, ListKind> numbering)
    {
        var partsById = ReadHeaderFooterRelationships(archive);

        hf.Header = ReadHeaderFooterPart(
            sectPr, "headerReference", "default", W + "hdr", partsById, archive, document, hyperlinkRelationships, numbering);
        hf.Footer = ReadHeaderFooterPart(
            sectPr, "footerReference", "default", W + "ftr", partsById, archive, document, hyperlinkRelationships, numbering);
        // Even-page header/footer (w:type="even") for "different odd/even pages". Present only when the
        // section carried the even references + parts; null otherwise so single-header sections are unaffected.
        hf.EvenHeader = ReadHeaderFooterPart(
            sectPr, "headerReference", "even", W + "hdr", partsById, archive, document, hyperlinkRelationships, numbering);
        hf.EvenFooter = ReadHeaderFooterPart(
            sectPr, "footerReference", "even", W + "ftr", partsById, archive, document, hyperlinkRelationships, numbering);
        // First-page header/footer (w:type="first") for "different first page". Present only when the section
        // carried the first references + parts.
        hf.FirstHeader = ReadHeaderFooterPart(
            sectPr, "headerReference", "first", W + "hdr", partsById, archive, document, hyperlinkRelationships, numbering);
        hf.FirstFooter = ReadHeaderFooterPart(
            sectPr, "footerReference", "first", W + "ftr", partsById, archive, document, hyperlinkRelationships, numbering);
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
            page.Landscape = pgSz.Attribute(W + "orient")?.Value == "landscape"
                || page.WidthPt > page.HeightPt;
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
            // Header/footer distance from the page edge (@w:header / @w:footer) and the binding gutter
            // (@w:gutter). Each is read only when present, leaving the model's "unspecified" 0 default otherwise.
            if (pgMar.Attribute(W + "header") is { } header)
                page.HeaderDistancePt = DxaToPoints(header.Value);
            if (pgMar.Attribute(W + "footer") is { } footer)
                page.FooterDistancePt = DxaToPoints(footer.Value);
            if (pgMar.Attribute(W + "gutter") is { } gutter)
                page.GutterPt = DxaToPoints(gutter.Value);
        }

        // Column layout (w:cols): @w:num + @w:space (equal-width), @w:sep (line between), and optional
        // explicit per-column w:col children (@w:equalWidth="0", Left/Right presets / custom widths).
        var cols = sectPr.Element(W + "cols");
        if (cols is not null)
        {
            if (int.TryParse(cols.Attribute(W + "num")?.Value, out var num) && num >= 1)
                page.ColumnCount = num;
            if (cols.Attribute(W + "space") is { } space)
                page.ColumnSpacingPt = DxaToPoints(space.Value);
            // @w:sep is an on/off attribute: present and not explicitly "0"/"false"/"off" → draw the line.
            page.ColumnsLineBetween = cols.Attribute(W + "sep")?.Value is "1" or "true" or "on";

            var colElements = cols.Elements(W + "col").ToList();
            var equalWidthOff = cols.Attribute(W + "equalWidth")?.Value is "0" or "false" or "off";
            // A single w:col child with an explicit width is valid (e.g. a one-column section with an
            // explicit w:col w:w="..."), so the guard is >= 1 (not > 1) when equalWidth is off.
            if (equalWidthOff && colElements.Count >= 1
                && colElements.All(c => c.Attribute(W + "w") is not null))
            {
                var widths = colElements.Select(c => DxaToPoints(c.Attribute(W + "w")!.Value)).ToList();
                page.ColumnCount = widths.Count;
                page.ColumnWidthsPt = widths;
            }
        }

        // Page border (w:pgBorders) → PageSettings.PageBorder (null when absent/off).
        page.PageBorder = ReadPageBorder(sectPr.Element(W + "pgBorders"));

        // Line numbering (w:lnNumType): recover the mode + interval.
        ReadLineNumbering(sectPr.Element(W + "lnNumType"), page);

        // Page numbering (w:pgNumType): recover section PAGE field style + optional start-at value.
        ReadPageNumbering(sectPr.Element(W + "pgNumType"), page);

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
        IReadOnlyDictionary<string, string> hyperlinkRelationships,
        IReadOnlyDictionary<int, ListKind> numbering,
        TextDocument? document)
    {
        var sectPr = pPr?.Element(W + "sectPr");
        if (sectPr is null)
            return null;

        var page = new PageSettings();
        ReadPageSettings(sectPr, page);
        var breakKind = SectionBreakFromToken(sectPr.Element(W + "type")?.Attribute(W + "val")?.Value);
        var section = new Section(page, breakKind);
        // Each non-final section references its own header/footer parts; recover them into the section.
        if (document is not null)
            ReadSectionHeadersFooters(sectPr, section.HeadersFooters, archive, document, hyperlinkRelationships, numbering);
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
        TextDocument document,
        IReadOnlyDictionary<string, string> hyperlinkRelationships,
        IReadOnlyDictionary<int, ListKind> numbering)
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
        var partRelationships = ReadPartRelationships(archive, partPath);
        var partHyperlinks = ReadPartHyperlinkRelationships(archive, partPath);

        var result = new HeaderFooter();
        // Use Descendants, not direct children: real Word headers/footers routinely wrap their content in a
        // w:tbl (and/or w:sdt content controls), so the visible text lives in paragraphs NESTED inside table
        // cells / SDTs rather than as direct children of w:hdr/w:ftr. Reading only direct-child w:p (as before)
        // recovered just the trailing empty paragraph, making the part IsEmpty so the writer dropped it — the
        // "headers dropped on round-trip" bug. Paragraphs never nest inside paragraphs in OOXML, so Descendants
        // yields each content paragraph exactly once, in document order. A DrawingML text box contains its own
        // w:p descendants, but ReadShape owns those paragraphs; flattening them here would duplicate the text
        // when the header is saved again. We still flatten table/SDT structure to the model's paragraph list
        // (the model carries no per-header table) and leave legacy VML text boxes on their existing path.
        foreach (var p in ReadStoryParagraphs(root))
        {
            // FreeW's page watermark is stored in a VML-only paragraph in the header. It is also
            // represented by custom document properties. Remove only the known watermark run: Word
            // commonly appends it to a real header paragraph, so dropping the whole paragraph loses
            // visible header text.
            var preservedParagraph = new XElement(p);
            var watermarkRuns = preservedParagraph.Descendants(W + "r")
                .Where(run => run.Descendants(V + "shape")
                    .Any(shape => shape.Attribute("id")?.Value is
                        "PowerPlusWaterMarkObject" or "PowerPlusPictureWaterMarkObject")
                    || run.Descendants(A + "blip")
                        .Any(blip => blip.Attribute(R + "embed")?.Value == "rIdWatermarkImage"))
                .ToList();
            foreach (var run in watermarkRuns)
                run.Remove();
            if (!preservedParagraph.Descendants(W + "r").Any())
                continue;

            result.Paragraphs.Add(ReadParagraph(
                preservedParagraph,
                archive,
                partRelationships,
                partHyperlinks,
                numbering,
                capturePreservedNumbering: true,
                preservedDrawingTarget: document,
                preservedDrawingRelationshipTargets: partRelationships));
        }
        return result;
    }

    private static IEnumerable<XElement> ReadStoryParagraphs(XElement container) =>
        container.Descendants(W + "p").Where(paragraph =>
            !paragraph.Ancestors(W + "txbxContent")
                .Any(textBoxContent => textBoxContent.Parent?.Name == Wps + "txbx"));

    /// <summary>
    /// Reads the image relationships of an arbitrary part (e.g. a header/footer part) from its own
    /// <c>&lt;dir&gt;/_rels/&lt;file&gt;.rels</c>, mapping each image relationship id → media part path
    /// (resolved relative to the part's directory). Returns an empty map when the part has no rels file (the
    /// common case — an image-less header), so image-less headers/footers cost nothing extra.
    /// </summary>
    private static Dictionary<string, string> ReadPartImageRelationships(ZipArchive archive, string partPath)
    {
        var dir = OpcPathHelper.GetDirectoryName(partPath);
        var relsPath = OpcPathHelper.GetRelationshipPartPath(partPath);

        return OpcRelationships.LoadTargetMap(
            archive,
            relsPath,
            relationship => relationship.IsExternal
                ? relationship.Target
                : OpcPathHelper.ResolveRelativeZipPath(dir, relationship.Target),
            relationship => relationship.Type.EndsWith("/image", StringComparison.Ordinal));
    }

    private static Dictionary<string, string> ReadPartRelationships(ZipArchive archive, string partPath)
    {
        var directory = OpcPathHelper.GetDirectoryName(partPath);
        return OpcRelationships.LoadTargetMap(
            archive,
            OpcPathHelper.GetRelationshipPartPath(partPath),
            relationship => relationship.IsExternal
                ? relationship.Target
                : OpcPathHelper.ResolveRelativeZipPath(directory, relationship.Target),
            relationship =>
                !relationship.IsExternal ||
                relationship.Type.EndsWith("/image", StringComparison.Ordinal));
    }

    private static Dictionary<string, string> ReadPartHyperlinkRelationships(ZipArchive archive, string partPath) =>
        OpcRelationships.LoadTargetMap(
            archive,
            OpcPathHelper.GetRelationshipPartPath(partPath),
            relationship => relationship.Target,
            relationship =>
                relationship.IsExternal &&
                relationship.Type.EndsWith("/hyperlink", StringComparison.Ordinal));

    /// <summary>Maps relationship id → part path for header/footer relationships in document.xml.rels.</summary>
    private static Dictionary<string, string> ReadHeaderFooterRelationships(ZipArchive archive) =>
        OpcRelationships.LoadTargetMap(
            archive,
            "word/_rels/document.xml.rels",
            relationship => OpcPathHelper.ResolveRelativeZipPath("word", relationship.Target),
            relationship =>
                !relationship.IsExternal &&
                (relationship.Type.EndsWith("/header", StringComparison.Ordinal) ||
                 relationship.Type.EndsWith("/footer", StringComparison.Ordinal)));

    private static XDocument? LoadPart(ZipArchive archive, string entryPath)
        => OpcXml.LoadXmlOrNull(archive, entryPath);

    private static Paragraph ReadParagraph(
        XElement p,
        ZipArchive archive,
        IReadOnlyDictionary<string, string> imageRelationships,
        IReadOnlyDictionary<string, string> hyperlinkRelationships,
        IReadOnlyDictionary<int, ListKind> numbering,
        bool capturePreservedNumbering = false,
        TextDocument? preservedDrawingTarget = null,
        IReadOnlyDictionary<string, string>? preservedDrawingRelationshipTargets = null,
        ContentControl? inheritedControl = null,
        IReadOnlyDictionary<(int NumId, int Level), int>? startOverrides = null,
        IReadOnlyDictionary<string, string>? subDocumentRelationships = null)
    {
        var paragraph = new Paragraph();
        var pPr = p.Element(W + "pPr");
        // Body/table/header/footer paragraphs share the document default spacing (w:docDefaults); note and
        // comment paragraphs still use the neutral fallback because they are read outside the document flow.
        var docDefaults = preservedDrawingTarget?.DefaultParagraph;
        if (pPr is not null)
        {
            paragraph.StyleId = pPr.Element(W + "pStyle")?.Attribute(W + "val")?.Value;
            paragraph.Formatting = ReadParagraphFormatting(pPr, numbering, docDefaults, startOverrides);
            paragraph.DropCap = ReadDropCapIntent(pPr);
            // A paragraph whose formatting was changed under Track Changes carries a w:pPrChange as the
            // last child of its w:pPr; parse the author/date and the nested previous w:pPr into the model.
            ApplyParagraphFormatRevision(paragraph, pPr);
            // When the paragraph carries a w:numPr that FreeW did NOT map to one of its own ListKinds, keep
            // the original numId+ilvl so the writer can re-emit it against the preserved numbering.xml (only
            // for every Word story that has an ordinary paragraph modelled here).
            if (capturePreservedNumbering && paragraph.Formatting.ListKind == ListKind.None)
                paragraph.PreservedNumbering = ReadPreservedNumbering(pPr);
            // A paragraph carrying a w:pPr/w:sectPr ends a non-final section; recover that section's page
            // setup + break kind + own header/footer references onto the paragraph (the body-level final
            // section is read elsewhere).
            paragraph.SectionBreak = ReadSectionBreak(pPr, archive, hyperlinkRelationships, numbering, preservedDrawingTarget);
        }
        else if (docDefaults is not null)
        {
            // A paragraph with no w:pPr still inherits the document default spacing.
            paragraph.Formatting = docDefaults;
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

        // Complex-field accumulator (Word's w:fldChar begin / w:instrText / separate / result / end run
        // sequence). When a begin fldChar is seen we collect the instruction (instrText) and the result
        // text (runs after the separate) until the matching end, then collapse the whole span into one
        // ComplexField run. Nesting is tracked by depth so an inner field's begin/end does not close the
        // outer one. The instruction's leading keyword is parsed (see ReadParagraphRun for the AddRun side).
        var fieldDepth = 0;
        var fieldInstr = new System.Text.StringBuilder();
        var fieldResult = new System.Text.StringBuilder();
        var fieldPastSeparate = false;
        XElement? fieldFormattingSource = null;

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
            else if (fieldDepth > 0 && child.Name == W + "r")
            {
                // Inside a complex field: consume this run into the accumulator instead of emitting it.
                var fldChar = child.Element(W + "fldChar")?.Attribute(W + "fldCharType")?.Value;
                if (fldChar == "begin")
                {
                    fieldDepth++; // a nested field; its instruction/result text still feeds the accumulator
                    // (begin char carries no text; nothing to accumulate here)
                }
                else if (fldChar == "separate")
                {
                    fieldPastSeparate = true;
                }
                else if (fldChar == "end")
                {
                    fieldDepth--;
                    if (fieldDepth == 0)
                    {
                        // The outermost field closed: collapse to a single ComplexField run, mapping a
                        // recognised leading keyword (PAGE/DATE/…) to the existing RunFieldKind so update
                        // and rendering reuse that path, otherwise preserving the raw instruction.
                        var instruction = fieldInstr.ToString();
                        var result = fieldResult.ToString();
                        var formatting = ReadRunFormatting(fieldFormattingSource?.Element(W + "rPr"));
                        if (CitationFor(instruction) is { } citation)
                        {
                            var citationRun = Run.CitationMark(citation);
                            citationRun.CommentId = activeCommentId;
                            citationRun.Control = inheritedControl;
                            paragraph.Runs.Add(citationRun);
                        }
                        else
                        {
                            var complexField = Run.ComplexFieldRun(instruction, result, showCode: false, formatting);
                            complexField.CommentId = activeCommentId;
                            complexField.Control = inheritedControl;
                            paragraph.Runs.Add(complexField);
                        }
                        fieldInstr.Clear();
                        fieldResult.Clear();
                        fieldPastSeparate = false;
                        fieldFormattingSource = null;
                    }
                    // A nested field's end fldChar just decrements the depth (no text to accumulate).
                }
                else if (!fieldPastSeparate)
                {
                    // Before the separate: accumulate the instruction text (w:instrText, occasionally w:t).
                    fieldInstr.Append(string.Concat(child.Elements(W + "instrText").Select(t => t.Value)));
                    fieldInstr.Append(string.Concat(child.Elements(W + "t").Select(t => t.Value)));
                }
                else
                {
                    // After the separate: accumulate the cached result text and remember its formatting.
                    // Include w:tab ("\t") and w:br ("\n") alongside w:t so that TOC entries whose
                    // result contains a tab leader (heading … <tab> … page#) keep their structure.
                    fieldFormattingSource ??= child;
                    AppendRunResultText(fieldResult, child);
                }
            }
            else if (fieldDepth > 0 && child.Name == W + "hyperlink")
            {
                // Inside a complex field, a w:hyperlink wraps the RESULT runs (TOC/INDEX/HYPERLINK fields
                // always put their cached result text in a hyperlink element). Route all w:r descendants
                // through the same field-accumulation path instead of emitting them as content outside the
                // field (which would leave the field's result empty and break TOC rendering).
                foreach (var hlRun in child.Elements(W + "r"))
                {
                    var fldChar = hlRun.Element(W + "fldChar")?.Attribute(W + "fldCharType")?.Value;
                    if (fldChar == "begin")
                    {
                        fieldDepth++;
                    }
                    else if (fldChar == "separate")
                    {
                        fieldPastSeparate = true;
                    }
                    else if (fldChar == "end")
                    {
                        fieldDepth--;
                        if (fieldDepth == 0)
                        {
                            var instruction = fieldInstr.ToString();
                            var result = fieldResult.ToString();
                            var formatting = ReadRunFormatting(fieldFormattingSource?.Element(W + "rPr"));
                            if (CitationFor(instruction) is { } citation)
                            {
                                var citationRun = Run.CitationMark(citation);
                                citationRun.CommentId = activeCommentId;
                                citationRun.Control = inheritedControl;
                                paragraph.Runs.Add(citationRun);
                            }
                            else
                            {
                                var complexField = Run.ComplexFieldRun(instruction, result, showCode: false, formatting);
                                complexField.CommentId = activeCommentId;
                                complexField.Control = inheritedControl;
                                paragraph.Runs.Add(complexField);
                            }
                            fieldInstr.Clear();
                            fieldResult.Clear();
                            fieldPastSeparate = false;
                            fieldFormattingSource = null;
                        }
                    }
                    else if (!fieldPastSeparate)
                    {
                        fieldInstr.Append(string.Concat(hlRun.Elements(W + "instrText").Select(t => t.Value)));
                        fieldInstr.Append(string.Concat(hlRun.Elements(W + "t").Select(t => t.Value)));
                    }
                    else
                    {
                        fieldFormattingSource ??= hlRun;
                        AppendRunResultText(fieldResult, hlRun);
                    }
                }
            }
            else if (child.Name == W + "r" && child.Element(W + "fldChar")?.Attribute(W + "fldCharType")?.Value == "begin"
                && child.Element(W + "fldChar")?.Element(W + "ffData") is null)
            {
                // A complex field begins here (and it is not a legacy form field, which AddRun handles).
                // Open the accumulator; subsequent runs feed it until the matching end fldChar.
                fieldDepth = 1;
                fieldPastSeparate = false;
                fieldInstr.Clear();
                fieldResult.Clear();
                fieldFormattingSource = null;
            }
            else
            {
                AddParagraphContentElement(
                    paragraph,
                    child,
                    archive,
                    imageRelationships,
                    hyperlinkRelationships,
                    numbering,
                    activeCommentId,
                    revision: default,
                    control: inheritedControl,
                    hyperlinkUrl: null,
                    hyperlinkAnchor: null,
                    hyperlinkTooltip: null,
                    preservedDrawingTarget,
                    preservedDrawingRelationshipTargets,
                    subDocumentRelationships);
            }
        }

        if (paragraph.DropCap is { } dropCap
            && paragraph.Runs.FirstOrDefault(run => run.Text.Length > 0)?.Formatting.FontSizePt is { } sizePt)
            paragraph.DropCap = dropCap with { SizePt = sizePt };

        return paragraph;
    }

    private static DropCapLayoutIntent? ReadDropCapIntent(XElement pPr)
    {
        var framePr = pPr.Element(W + "framePr");
        var token = framePr?.Attribute(W + "dropCap")?.Value;
        var position = token switch
        {
            "drop" => DropCapPosition.Dropped,
            "margin" => DropCapPosition.InMargin,
            _ => (DropCapPosition?)null
        };
        if (position is null)
            return null;

        var lineSpan = framePr!.Attribute(W + "lines") is { } lines
            ? Math.Max(1, ParseInt(lines.Value))
            : DropCap.DefaultLineSpan;
        var distancePt = framePr.Attribute(W + "hSpace") is { } hSpace
            ? DxaToPoints(hSpace.Value)
            : DropCap.DefaultDistanceFromTextPt;
        return new DropCapLayoutIntent(
            position.Value,
            lineSpan,
            DropCap.DefaultSizePt,
            Math.Max(0, distancePt));
    }

    /// <summary>
    /// Appends the text content of a field-result run element to <paramref name="sb"/>. Handles:
    /// <list type="bullet">
    /// <item><c>w:t</c> — literal run text (normal characters).</item>
    /// <item><c>w:tab</c> — a tab character ('\t'). TOC entries use tabs between the heading title and
    ///   the page number; without this the tab leader is lost in the cached result text.</item>
    /// <item><c>w:br</c> — a line break ('\n'). Preserves soft return structure in multi-line fields.</item>
    /// </list>
    /// All other children (e.g. drawing runs, footnote references) are ignored in the result accumulation.
    /// </summary>
    private static void AppendRunResultText(System.Text.StringBuilder sb, XElement run)
    {
        foreach (var child in run.Elements())
        {
            if (child.Name == W + "t")
                sb.Append(child.Value);
            else if (child.Name == W + "tab")
                sb.Append('\t');
            else if (child.Name == W + "br")
                sb.Append('\n');
        }
    }

    private static void AddBodyBlock(
        XElement element,
        TextDocument document,
        ZipArchive archive,
        IReadOnlyDictionary<string, string> imageRelationships,
        IReadOnlyDictionary<string, string> hyperlinkRelationships,
        IReadOnlyDictionary<string, string> altChunkRelationships,
        IReadOnlyDictionary<string, string> subDocumentRelationships,
        IReadOnlyDictionary<int, ListKind> numbering,
        IReadOnlyDictionary<(int NumId, int Level), int> startOverrides,
        ref Paragraph? prevPara,
        ref bool prevAfterAuto,
        ContentControl? inheritedControl = null,
        BlockContentControl? inheritedBlockContentControl = null,
        BlockCustomXml? inheritedBlockCustomXml = null)
    {
        if (element.Name == W + "p")
        {
            var para = ReadParagraph(
                element,
                archive,
                imageRelationships,
                hyperlinkRelationships,
                numbering,
                capturePreservedNumbering: true,
                preservedDrawingTarget: document,
                inheritedControl: inheritedControl,
                startOverrides: startOverrides,
                subDocumentRelationships: subDocumentRelationships);
            para.BlockContentControl = inheritedBlockContentControl;
            para.BlockCustomXml = inheritedBlockCustomXml;
            document.Blocks.Add(para);
            var sp = element.Element(W + "pPr")?.Element(W + "spacing");
            var beforeAuto = sp?.Attribute(W + "beforeAutospacing")?.Value is "1" or "true" or "on";
            if (prevPara is not null && prevAfterAuto && beforeAuto)
            {
                prevPara.Formatting = prevPara.Formatting with { SpaceAfterPt = 0 };
                para.Formatting = para.Formatting with { SpaceBeforePt = 0 };
            }
            prevPara = para;
            prevAfterAuto = sp?.Attribute(W + "afterAutospacing")?.Value is "1" or "true" or "on";
        }
        else if (element.Name == W + "tbl")
        {
            var table = ReadTable(
                element,
                archive,
                imageRelationships,
                hyperlinkRelationships,
                numbering,
                startOverrides,
                document,
                inheritedControl,
                subDocumentRelationships);
            table.BlockContentControl = inheritedBlockContentControl;
            table.BlockCustomXml = inheritedBlockCustomXml;
            document.Blocks.Add(table);
            prevPara = null;
            prevAfterAuto = false;
        }
        else if (element.Name == W + "altChunk")
        {
            var relationshipId = element.Attribute(R + "id")?.Value;
            if (relationshipId is not null && altChunkRelationships.TryGetValue(relationshipId, out var partName))
            {
                if (TryMaterializeAltChunk(archive, document, partName, out var importedBlocks))
                {
                    foreach (var importedBlock in importedBlocks)
                    {
                        importedBlock.BlockContentControl = inheritedBlockContentControl;
                        importedBlock.BlockCustomXml = inheritedBlockCustomXml;
                        document.Blocks.Add(importedBlock);
                    }
                }
                else
                {
                    var altChunk = new AltChunkBlock(partName)
                    {
                        BlockContentControl = inheritedBlockContentControl,
                        BlockCustomXml = inheritedBlockCustomXml
                    };
                    document.Blocks.Add(altChunk);
                }
            }
            prevPara = null;
            prevAfterAuto = false;
        }
        else if (element.Name == W + "sdt")
        {
            var blockControl = ReadBlockContentControl(element.Element(W + "sdtPr"));
            if (inheritedBlockContentControl is not null)
                blockControl = blockControl with { Parent = inheritedBlockContentControl };
            foreach (var child in element.Element(W + "sdtContent")?.Elements() ?? [])
            {
                AddBodyBlock(
                    child,
                    document,
                    archive,
                    imageRelationships,
                    hyperlinkRelationships,
                    altChunkRelationships,
                    subDocumentRelationships,
                    numbering,
                    startOverrides,
                    ref prevPara,
                    ref prevAfterAuto,
                    inheritedControl,
                    blockControl,
                    inheritedBlockCustomXml);
            }
        }
        else if (element.Name == W + "customXml")
        {
            var blockCustomXml = new BlockCustomXml(
                element.Attribute(W + "element")?.Value,
                element.Attribute(W + "uri")?.Value,
                element.Element(W + "customXmlPr")?.ToString(SaveOptions.DisableFormatting));
            foreach (var child in element.Elements().Where(child => child.Name != W + "customXmlPr"))
            {
                AddBodyBlock(
                    child,
                    document,
                    archive,
                    imageRelationships,
                    hyperlinkRelationships,
                    altChunkRelationships,
                    subDocumentRelationships,
                    numbering,
                    startOverrides,
                    ref prevPara,
                    ref prevAfterAuto,
                    inheritedControl,
                    inheritedBlockContentControl,
                    blockCustomXml);
            }
        }
    }

    /// <summary>
    /// Word resolves supported body-level altChunks into ordinary editable blocks when the document opens.
    /// Materialize package-local HTML, MHTML, RTF, and self-contained nested Word-package payloads.
    /// Malformed content, packages with document-global references, and unknown chunk types remain represented
    /// by <see cref="AltChunkBlock"/> so their payload graph is retained verbatim on save.
    /// </summary>
    private static bool TryMaterializeAltChunk(
        ZipArchive archive,
        TextDocument target,
        string partName,
        out IReadOnlyList<Block> blocks)
    {
        blocks = [];
        var partPath = partName.TrimStart('/');
        var extension = Path.GetExtension(partPath).TrimStart('.').ToLowerInvariant();
        var overrides = ReadContentTypeOverrides(archive);
        var defaults = ReadContentTypeDefaults(archive);
        var contentType = overrides.GetValueOrDefault(partName)
            ?? defaults.GetValueOrDefault(extension);
        if (LoadMedia(archive, partPath) is not { } bytes)
            return false;

        try
        {
            if (IsNestedWordPackageContentType(contentType))
            {
                using var nestedStream = new MemoryStream(bytes, writable: false);
                var nested = Read(nestedStream);
                return TryMergeNestedWordPackage(target, nested, out blocks);
            }

            if (string.Equals(contentType, "message/rfc822", StringComparison.OrdinalIgnoreCase))
            {
                using var mhtmlStream = new MemoryStream(bytes, writable: false);
                blocks = new MhtmlFileAdapter().Load(mhtmlStream).Blocks.ToList();
                return true;
            }

            if (string.Equals(contentType, "application/rtf", StringComparison.OrdinalIgnoreCase)
                || string.Equals(contentType, "text/rtf", StringComparison.OrdinalIgnoreCase))
            {
                using var rtfStream = new MemoryStream(bytes, writable: false);
                blocks = new RtfFileAdapter().Load(rtfStream).Blocks.ToList();
                return true;
            }

            if (!string.Equals(contentType, "text/html", StringComparison.OrdinalIgnoreCase))
                return false;

            using var stream = new MemoryStream(bytes, writable: false);
            using var reader = new StreamReader(
                stream,
                Encoding.UTF8,
                detectEncodingFromByteOrderMarks: true,
                bufferSize: 4096,
                leaveOpen: false);

            var partDirectory = OpcPathHelper.GetDirectoryName(partPath);
            var imagesByRelationshipId = ReadPartImageRelationships(archive, partPath);
            InlineImage? ResolveImage(string source)
            {
                if (imagesByRelationshipId.TryGetValue(source, out var relatedPath))
                    return CreateImage(relatedPath);

                if (Uri.TryCreate(source, UriKind.Absolute, out _))
                    return null;

                return CreateImage(OpcPathHelper.ResolveRelativeZipPath(partDirectory, source));
            }

            InlineImage? CreateImage(string path) => LoadMedia(archive, path) is { } imageBytes
                ? new InlineImage(imageBytes, 72, 72, ResolveImageFormat(path, imageBytes))
                : null;

            blocks = HtmlFileAdapter.LoadHtml(reader.ReadToEnd(), ResolveImage).Blocks.ToList();
            return true;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException
            or FormatException or NotSupportedException or MimeKit.ParseException)
        {
            return false;
        }
    }

    private static bool IsNestedWordPackageContentType(string? contentType) =>
        string.Equals(contentType, "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml", StringComparison.OrdinalIgnoreCase)
        || string.Equals(contentType, "application/vnd.ms-word.document.macroEnabled.main+xml", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// A nested Word altChunk imports its body into the host document, not its package-level state.  Only
    /// materialize the self-contained subset whose formatting can be carried by body blocks and styles;
    /// retaining the remaining packages intact is safer than creating dangling note, numbering, or relationship
    /// references in the host package.
    /// </summary>
    private static bool TryMergeNestedWordPackage(
        TextDocument target,
        TextDocument nested,
        out IReadOnlyList<Block> blocks)
    {
        blocks = [];
        if (nested.Footnotes.Count != 0
            || nested.Endnotes.Count != 0
            || nested.Comments.Count != 0
            || nested.Preserved.Parts.Count != 0
            || nested.Preserved.WebExtensions is not null
            || nested.Blocks.Any(ContainsPackageBoundContent)
            || nested.Styles.Values.Any(style => style.PreservedNumbering is not null))
        {
            return false;
        }

        var (styleMap, defaultStyleId) = CopyNestedStyles(target, nested);
        foreach (var block in nested.Blocks)
            RemapStyleIds(block, styleMap, defaultStyleId);

        blocks = nested.Blocks;
        return true;
    }

    private static bool ContainsPackageBoundContent(Block block) => block switch
    {
        AltChunkBlock => true,
        Paragraph paragraph => paragraph.SectionBreak is not null
            || paragraph.PreservedNumbering is not null
            || paragraph.BookmarkNames.Count != 0
            || paragraph.BookmarkBoundaries.Count != 0
            || paragraph.Runs.Any(run => run.FootnoteId is not null
                || run.EndnoteId is not null
                || run.CommentId is not null
                || run.IsCommentReference
                || run.PreservedDrawing is not null),
        Table table => table.Rows.SelectMany(row => row.Cells)
            .SelectMany(cell => cell.Paragraphs)
            .Any(paragraph => ContainsPackageBoundContent(paragraph)),
        _ => true
    };

    private static (Dictionary<string, string> StyleMap, string DefaultStyleId) CopyNestedStyles(
        TextDocument target,
        TextDocument nested)
    {
        var prefix = "AltChunk";
        var sequence = 1;
        while (target.Styles.Keys.Any(id => id.StartsWith(prefix + sequence + "_", StringComparison.Ordinal)))
            sequence++;
        prefix += sequence + "_";
        var defaultStyleId = prefix + "DocumentDefaults";

        var styleMap = nested.Styles.Keys.ToDictionary(
            id => id,
            id => target.Styles.ContainsKey(id) ? prefix + id : id,
            StringComparer.Ordinal);
        target.Styles[defaultStyleId] = new DocumentStyle
        {
            Id = defaultStyleId,
            Name = "Imported document defaults",
            Run = nested.DefaultRun,
            Paragraph = nested.DefaultParagraph
        };
        foreach (var style in nested.Styles.Values)
        {
            var id = styleMap[style.Id];
            var (effectiveRun, effectiveParagraph) = ResolveNestedStyle(nested, style.Id);
            target.Styles[id] = new DocumentStyle
            {
                Id = id,
                Name = style.Name,
                Type = style.Type,
                // Style inheritance is flattened against the nested document's defaults. The host's
                // defaults must not leak into materialized altChunk content.
                BasedOnStyleId = null,
                NextStyleId = RemapStyleId(style.NextStyleId, styleMap),
                OutlineLevel = style.OutlineLevel,
                Run = effectiveRun,
                Paragraph = effectiveParagraph,
                TableBorders = style.TableBorders
            };
        }

        return (styleMap, defaultStyleId);
    }

    private static (RunFormatting Run, ParagraphFormatting Paragraph) ResolveNestedStyle(TextDocument nested, string styleId)
    {
        var run = nested.DefaultRun;
        var paragraph = nested.DefaultParagraph;
        var chain = new Stack<DocumentStyle>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        for (var id = styleId; id is not null && visited.Add(id) && nested.Styles.TryGetValue(id, out var style); id = style.BasedOnStyleId)
            chain.Push(style);
        while (chain.TryPop(out var style))
        {
            run = OverlayRunFormatting(run, style.Run);
            paragraph = OverlayParagraphFormatting(paragraph, style.Paragraph);
        }
        return (run, paragraph);
    }

    private static RunFormatting OverlayRunFormatting(RunFormatting inherited, RunFormatting direct) => direct with
    {
        Bold = direct.Bold || inherited.Bold,
        Italic = direct.Italic || inherited.Italic,
        Underline = direct.Underline || inherited.Underline,
        Strikethrough = direct.Strikethrough || inherited.Strikethrough,
        SmallCaps = direct.SmallCaps || inherited.SmallCaps,
        AllCaps = direct.AllCaps || inherited.AllCaps,
        Rtl = direct.Rtl || inherited.Rtl,
        VerticalAlign = direct.VerticalAlign != VerticalAlign.Baseline ? direct.VerticalAlign : inherited.VerticalAlign,
        FontFamily = direct.FontFamily ?? inherited.FontFamily,
        FontSizePt = direct.FontSizePt ?? inherited.FontSizePt,
        ColorHex = direct.ColorHex ?? inherited.ColorHex,
        HighlightColorHex = direct.HighlightColorHex ?? inherited.HighlightColorHex,
        CharacterBorder = direct.CharacterBorder ?? inherited.CharacterBorder,
        CharacterShadingHex = direct.CharacterShadingHex ?? inherited.CharacterShadingHex,
        CharacterShadingPattern = direct.CharacterShadingHex is not null
            ? direct.CharacterShadingPattern
            : inherited.CharacterShadingPattern,
        LanguageTag = direct.LanguageTag ?? inherited.LanguageTag
    };

    private static ParagraphFormatting OverlayParagraphFormatting(ParagraphFormatting inherited, ParagraphFormatting direct)
    {
        var defaults = ParagraphFormatting.Default;
        var line = direct.LineSpacingIsSet ? direct : inherited;
        return direct with
        {
            Alignment = direct.Alignment != defaults.Alignment ? direct.Alignment : inherited.Alignment,
            Rtl = direct.Rtl || inherited.Rtl,
            SpaceBeforePt = direct.SpaceBeforeIsSet ? direct.SpaceBeforePt : inherited.SpaceBeforePt,
            SpaceAfterPt = direct.SpaceAfterIsSet ? direct.SpaceAfterPt : inherited.SpaceAfterPt,
            BeforeAutoSpacing = direct.SpaceBeforeIsSet ? direct.BeforeAutoSpacing : inherited.BeforeAutoSpacing,
            AfterAutoSpacing = direct.SpaceAfterIsSet ? direct.AfterAutoSpacing : inherited.AfterAutoSpacing,
            ContextualSpacing = direct.ContextualSpacing ?? inherited.ContextualSpacing,
            SpaceBeforeIsSet = direct.SpaceBeforeIsSet || inherited.SpaceBeforeIsSet,
            SpaceAfterIsSet = direct.SpaceAfterIsSet || inherited.SpaceAfterIsSet,
            LineSpacing = line.LineSpacing,
            LineRule = line.LineRule,
            LineHeightPt = line.LineHeightPt,
            LineSpacingIsSet = direct.LineSpacingIsSet || inherited.LineSpacingIsSet,
            IndentLeftPt = direct.IndentLeftPt != defaults.IndentLeftPt ? direct.IndentLeftPt : inherited.IndentLeftPt,
            IndentRightPt = direct.IndentRightPt != defaults.IndentRightPt ? direct.IndentRightPt : inherited.IndentRightPt,
            FirstLineIndentPt = direct.FirstLineIndentPt != defaults.FirstLineIndentPt ? direct.FirstLineIndentPt : inherited.FirstLineIndentPt,
            Border = direct.Border ?? inherited.Border,
            ShadingColorHex = direct.ShadingColorHex ?? inherited.ShadingColorHex,
            ShadingPattern = direct.ShadingColorHex is not null ? direct.ShadingPattern : inherited.ShadingPattern
        };
    }

    private static void RemapStyleIds(
        Block block,
        IReadOnlyDictionary<string, string> styleMap,
        string defaultStyleId)
    {
        switch (block)
        {
            case Paragraph paragraph:
                paragraph.StyleId = RemapStyleId(paragraph.StyleId, styleMap) ?? defaultStyleId;
                break;
            case Table table:
                table.TableStyleId = RemapStyleId(table.TableStyleId, styleMap);
                foreach (var paragraph in table.Rows.SelectMany(row => row.Cells).SelectMany(cell => cell.Paragraphs))
                    paragraph.StyleId = RemapStyleId(paragraph.StyleId, styleMap) ?? defaultStyleId;
                break;
        }
    }

    private static string? RemapStyleId(string? styleId, IReadOnlyDictionary<string, string> styleMap) =>
        styleId is not null && styleMap.TryGetValue(styleId, out var mapped) ? mapped : styleId;

    private static void AddParagraphRuns(
        Paragraph paragraph,
        XElement container,
        ZipArchive archive,
        IReadOnlyDictionary<string, string> imageRelationships,
        IReadOnlyDictionary<string, string> hyperlinkRelationships,
        IReadOnlyDictionary<int, ListKind> numbering,
        int? commentId,
        RevisionInfo revision,
        ContentControl? control,
        string? hyperlinkUrl,
        string? hyperlinkAnchor,
        string? hyperlinkTooltip,
        TextDocument? preservedDrawingTarget,
        IReadOnlyDictionary<string, string>? preservedDrawingRelationshipTargets,
        IReadOnlyDictionary<string, string>? subDocumentRelationships = null)
    {
        var fieldDepth = 0;
        var fieldInstr = new System.Text.StringBuilder();
        var fieldResult = new System.Text.StringBuilder();
        var fieldPastSeparate = false;
        XElement? fieldFormattingSource = null;

        foreach (var child in container.Elements())
        {
            if (fieldDepth > 0 && child.Name == W + "r")
            {
                var fldChar = child.Element(W + "fldChar")?.Attribute(W + "fldCharType")?.Value;
                if (fldChar == "begin")
                {
                    fieldDepth++;
                }
                else if (fldChar == "separate")
                {
                    fieldPastSeparate = true;
                }
                else if (fldChar == "end")
                {
                    fieldDepth--;
                    if (fieldDepth == 0)
                    {
                        AddComplexFieldRun(
                            paragraph,
                            fieldInstr.ToString(),
                            fieldResult.ToString(),
                            ReadRunFormatting(fieldFormattingSource?.Element(W + "rPr")),
                            commentId,
                            revision,
                            control,
                            hyperlinkUrl,
                            hyperlinkAnchor,
                            hyperlinkTooltip);
                        fieldInstr.Clear();
                        fieldResult.Clear();
                        fieldPastSeparate = false;
                        fieldFormattingSource = null;
                    }
                }
                else if (!fieldPastSeparate)
                {
                    fieldInstr.Append(string.Concat(child.Elements(W + "instrText").Select(t => t.Value)));
                    fieldInstr.Append(string.Concat(child.Elements(W + "t").Select(t => t.Value)));
                }
                else
                {
                    fieldFormattingSource ??= child;
                    fieldResult.Append(string.Concat(child.Elements(W + "t").Select(t => t.Value)));
                }

                continue;
            }

            if (child.Name == W + "r" && child.Element(W + "fldChar")?.Attribute(W + "fldCharType")?.Value == "begin"
                && child.Element(W + "fldChar")?.Element(W + "ffData") is null)
            {
                fieldDepth = 1;
                fieldPastSeparate = false;
                fieldInstr.Clear();
                fieldResult.Clear();
                fieldFormattingSource = null;
                continue;
            }

            AddParagraphContentElement(paragraph, child, archive, imageRelationships, hyperlinkRelationships, numbering, commentId, revision, control, hyperlinkUrl, hyperlinkAnchor, hyperlinkTooltip, preservedDrawingTarget, preservedDrawingRelationshipTargets, subDocumentRelationships);
        }
    }

    private static void AddComplexFieldRun(
        Paragraph paragraph,
        string instruction,
        string result,
        RunFormatting? formatting,
        int? commentId,
        RevisionInfo revision,
        ContentControl? control,
        string? hyperlinkUrl,
        string? hyperlinkAnchor,
        string? hyperlinkTooltip)
    {
        if (CitationFor(instruction) is { } citation)
        {
            var citationRun = Run.CitationMark(citation);
            citationRun.CommentId = commentId;
            citationRun.Control = control;
            citationRun.HyperlinkUrl = hyperlinkUrl;
            citationRun.HyperlinkAnchor = hyperlinkAnchor;
            citationRun.HyperlinkTooltip = hyperlinkTooltip;
            if (revision.Kind != RevisionKind.None)
            {
                citationRun.Revision = revision.Kind;
                citationRun.RevisionAuthor = revision.Author;
                citationRun.RevisionDateXml = revision.DateXml;
                citationRun.MoveRevisionId = revision.MoveId;
            }

            paragraph.Runs.Add(citationRun);
            return;
        }

        var run = Run.ComplexFieldRun(instruction, result, showCode: false, formatting);
        run.CommentId = commentId;
        run.Control = control;
        run.HyperlinkUrl = hyperlinkUrl;
        run.HyperlinkAnchor = hyperlinkAnchor;
        run.HyperlinkTooltip = hyperlinkTooltip;
        if (revision.Kind != RevisionKind.None)
        {
            run.Revision = revision.Kind;
            run.RevisionAuthor = revision.Author;
            run.RevisionDateXml = revision.DateXml;
            run.MoveRevisionId = revision.MoveId;
        }

        paragraph.Runs.Add(run);
    }

    private static void AddParagraphContentElement(
        Paragraph paragraph,
        XElement child,
        ZipArchive archive,
        IReadOnlyDictionary<string, string> imageRelationships,
        IReadOnlyDictionary<string, string> hyperlinkRelationships,
        IReadOnlyDictionary<int, ListKind> numbering,
        int? commentId,
        RevisionInfo revision,
        ContentControl? control,
        string? hyperlinkUrl,
        string? hyperlinkAnchor,
        string? hyperlinkTooltip,
        TextDocument? preservedDrawingTarget,
        IReadOnlyDictionary<string, string>? preservedDrawingRelationshipTargets,
        IReadOnlyDictionary<string, string>? subDocumentRelationships = null)
    {
        if (child.Name == W + "r")
        {
            // A run carrying a w:commentReference is the textless comment anchor; recover it.
            var commentRef = child.Element(W + "commentReference");
            if (commentRef is not null && int.TryParse(commentRef.Attribute(W + "id")?.Value, out var refId))
            {
                var referenceRun = Run.CommentReference(refId);
                if (revision.Kind != RevisionKind.None)
                {
                    referenceRun.Revision = revision.Kind;
                    referenceRun.RevisionAuthor = revision.Author;
                    referenceRun.RevisionDateXml = revision.DateXml;
                    referenceRun.MoveRevisionId = revision.MoveId;
                }
                paragraph.Runs.Add(referenceRun);
            }
            else
                AddRun(paragraph, child, archive, imageRelationships, hyperlinkRelationships, numbering, hyperlinkUrl, hyperlinkAnchor, commentId, revision, control, hyperlinkTooltip, preservedDrawingTarget, preservedDrawingRelationshipTargets);
        }
        else if (child.Name == W + "subDoc")
        {
            var relationshipId = child.Attribute(R + "id")?.Value;
            if (relationshipId is not null
                && subDocumentRelationships?.TryGetValue(relationshipId, out var target) == true)
            {
                var subDocumentRun = Run.FromSubDocument(target);
                subDocumentRun.CommentId = commentId;
                subDocumentRun.Control = control;
                subDocumentRun.HyperlinkUrl = hyperlinkUrl;
                subDocumentRun.HyperlinkAnchor = hyperlinkAnchor;
                subDocumentRun.HyperlinkTooltip = hyperlinkTooltip;
                if (revision.Kind != RevisionKind.None)
                {
                    subDocumentRun.Revision = revision.Kind;
                    subDocumentRun.RevisionAuthor = revision.Author;
                    subDocumentRun.RevisionDateXml = revision.DateXml;
                    subDocumentRun.MoveRevisionId = revision.MoveId;
                }
                paragraph.Runs.Add(subDocumentRun);
            }
        }
        else if (child.Name == W + "hyperlink")
        {
            var anchor = child.Attribute(W + "anchor")?.Value;
            var id = child.Attribute(R + "id")?.Value;
            var url = id is not null && hyperlinkRelationships.TryGetValue(id, out var target) ? target : null;
            var tooltip = child.Attribute(W + "tooltip")?.Value;
            AddParagraphRuns(paragraph, child, archive, imageRelationships, hyperlinkRelationships, numbering, commentId, revision, control, url, url is null ? anchor : null, tooltip, preservedDrawingTarget, preservedDrawingRelationshipTargets, subDocumentRelationships);
        }
        else if (child.Name == W + "ins" || child.Name == W + "del" || child.Name == W + "moveTo" || child.Name == W + "moveFrom")
        {
            // Tracked moves use the same visible rendering categories as deletions/insertions, while their
            // shared w:id preserves the move relationship for an exact package round-trip.
            var kind = child.Name == W + "del" || child.Name == W + "moveFrom"
                ? RevisionKind.Deleted
                : RevisionKind.Inserted;
            var isMove = child.Name == W + "moveFrom" || child.Name == W + "moveTo";
            var childRevision = new RevisionInfo(
                kind,
                child.Attribute(W + "author")?.Value,
                child.Attribute(W + "date")?.Value,
                isMove && int.TryParse(child.Attribute(W + "id")?.Value, out var moveId) ? moveId : null);
            AddParagraphRuns(paragraph, child, archive, imageRelationships, hyperlinkRelationships, numbering, commentId, childRevision, control, hyperlinkUrl, hyperlinkAnchor, hyperlinkTooltip, preservedDrawingTarget, preservedDrawingRelationshipTargets, subDocumentRelationships);
        }
        else if (child.Name == W + "sdt")
        {
            AddContentControlRuns(paragraph, child, archive, imageRelationships, hyperlinkRelationships, numbering, commentId, revision, preservedDrawingTarget, preservedDrawingRelationshipTargets, control, hyperlinkUrl, hyperlinkAnchor, hyperlinkTooltip, subDocumentRelationships);
        }
        else if (child.Name == W + "smartTag" || child.Name == W + "customXml")
        {
            // Legacy Word smart tags and custom-XML ranges annotate inline content. The model preserves the
            // package custom-XML parts but not inline wrapper metadata, so retain the visible child runs.
            AddParagraphRuns(paragraph, child, archive, imageRelationships, hyperlinkRelationships, numbering, commentId, revision, control, hyperlinkUrl, hyperlinkAnchor, hyperlinkTooltip, preservedDrawingTarget, preservedDrawingRelationshipTargets, subDocumentRelationships);
        }
        else if (child.Name == W + "dir" || child.Name == W + "bdo")
        {
            // Inline bidirectional containers wrap ordinary paragraph content. FreeW's run-level RTL property
            // is the closest editable equivalent, so retain every child run and apply it to an RTL scope.
            // LTR is already the model default and needs no synthetic run property.
            var firstRun = paragraph.Runs.Count;
            AddParagraphRuns(paragraph, child, archive, imageRelationships, hyperlinkRelationships, numbering, commentId, revision, control, hyperlinkUrl, hyperlinkAnchor, hyperlinkTooltip, preservedDrawingTarget, preservedDrawingRelationshipTargets, subDocumentRelationships);
            if (string.Equals(child.Attribute(W + "val")?.Value, "rtl", StringComparison.OrdinalIgnoreCase))
            {
                for (var index = firstRun; index < paragraph.Runs.Count; index++)
                    paragraph.Runs[index].Formatting = paragraph.Runs[index].Formatting with { Rtl = true };
            }
        }
        else if (child.Name == W + "ruby")
        {
            AddRuby(paragraph, child, commentId, revision, control, hyperlinkUrl, hyperlinkAnchor, hyperlinkTooltip);
        }
        else if (child.Name == W + "fldSimple")
        {
            var firstRun = paragraph.Runs.Count;
            AddSimpleField(paragraph, child);
            if (revision.Kind != RevisionKind.None)
            {
                for (var index = firstRun; index < paragraph.Runs.Count; index++)
                {
                    paragraph.Runs[index].Revision = revision.Kind;
                    paragraph.Runs[index].RevisionAuthor = revision.Author;
                    paragraph.Runs[index].RevisionDateXml = revision.DateXml;
                    paragraph.Runs[index].MoveRevisionId = revision.MoveId;
                }
            }
        }
        else if (child.Name == M + "oMath")
        {
            // An inline equation: parse the OMML m:oMath into an Equation carried by a run.
            var run = Run.FromEquation(ReadOMath(child));
            run.Control = control;
            run.HyperlinkUrl = hyperlinkUrl;
            run.HyperlinkAnchor = hyperlinkAnchor;
            run.HyperlinkTooltip = hyperlinkTooltip;
            if (revision.Kind != RevisionKind.None)
            {
                run.Revision = revision.Kind;
                run.RevisionAuthor = revision.Author;
                run.RevisionDateXml = revision.DateXml;
                run.MoveRevisionId = revision.MoveId;
            }
            paragraph.Runs.Add(run);
        }
        else if (child.Name == W + "bookmarkStart")
        {
            // Keep the exact invisible boundary at the current run index. _GoBack remains hidden from
            // the public navigation-name list, but its package boundary is retained for Word round-trip.
            var pairKey = child.Attribute(W + "id")?.Value;
            var name = child.Attribute(W + "name")?.Value;
            if (pairKey is { Length: > 0 })
            {
                paragraph.BookmarkBoundaries.Add(new BookmarkBoundary(
                    pairKey,
                    BookmarkBoundaryKind.Start,
                    paragraph.Runs.Count,
                    name,
                    ParseNullableInt(child.Attribute(W + "colFirst")?.Value),
                    ParseNullableInt(child.Attribute(W + "colLast")?.Value),
                    child.Attribute(W + "displacedByCustomXml")?.Value,
                    control));
            }
            if (name is { Length: > 0 } && name != "_GoBack" && !paragraph.BookmarkNames.Contains(name))
                paragraph.BookmarkNames.Add(name);
        }
        else if (child.Name == W + "bookmarkEnd")
        {
            var pairKey = child.Attribute(W + "id")?.Value;
            if (pairKey is { Length: > 0 })
            {
                paragraph.BookmarkBoundaries.Add(new BookmarkBoundary(
                    pairKey,
                    BookmarkBoundaryKind.End,
                    paragraph.Runs.Count,
                    DisplacedByCustomXml: child.Attribute(W + "displacedByCustomXml")?.Value,
                    OwnerControl: control));
            }
        }
    }

    private static int? ParseNullableInt(string? value) =>
        int.TryParse(value, out var result) ? result : null;

    /// <summary>
    /// Reads a Word phonetic guide (<c>w:ruby</c>). The base characters remain the run's fallback text so
    /// non-ruby-aware consumers keep the visible document content, while the fragment payload round-trips.
    /// </summary>
    private static void AddRuby(
        Paragraph paragraph,
        XElement ruby,
        int? commentId,
        RevisionInfo revision,
        ContentControl? control,
        string? hyperlinkUrl,
        string? hyperlinkAnchor,
        string? hyperlinkTooltip)
    {
        var rubyPr = ruby.Element(W + "rubyPr");
        var annotation = new RubyAnnotation
        {
            Alignment = ReadRubyAlignment(rubyPr?.Element(W + "rubyAlign")?.Attribute(W + "val")?.Value),
            PhoneticSizeHalfPoints = ReadRubyHalfPoints(rubyPr?.Element(W + "hps")?.Attribute(W + "val")?.Value),
            RaiseHalfPoints = ReadRubyHalfPoints(rubyPr?.Element(W + "hpsRaise")?.Attribute(W + "val")?.Value)
        };

        AddRubyFragments(ruby.Element(W + "rubyBase"), annotation.BaseFragments);
        AddRubyFragments(ruby.Element(W + "rt"), annotation.PhoneticFragments);

        var run = Run.FromRuby(annotation);
        run.CommentId = commentId;
        run.Control = control;
        run.HyperlinkUrl = hyperlinkUrl;
        run.HyperlinkAnchor = hyperlinkAnchor;
        run.HyperlinkTooltip = hyperlinkTooltip;
        if (revision.Kind != RevisionKind.None)
        {
            run.Revision = revision.Kind;
            run.RevisionAuthor = revision.Author;
            run.RevisionDateXml = revision.DateXml;
            run.MoveRevisionId = revision.MoveId;
        }

        paragraph.Runs.Add(run);
    }

    private static void AddRubyFragments(XElement? container, List<RubyTextFragment> fragments)
    {
        if (container is null)
            return;

        foreach (var sourceRun in container.Elements(W + "r"))
        {
            var text = string.Concat(sourceRun.Elements(W + "t").Select(textElement => textElement.Value))
                + string.Concat(sourceRun.Elements(W + "delText").Select(textElement => textElement.Value));
            if (sourceRun.Elements(W + "tab").Any())
                text += "\t";
            if (text.Length > 0)
                fragments.Add(new RubyTextFragment(text, ReadRunFormatting(sourceRun.Element(W + "rPr"))));
        }
    }

    private static RubyAlignment ReadRubyAlignment(string? value) => value switch
    {
        "distributeLetter" => RubyAlignment.DistributeLetter,
        "distributeSpace" => RubyAlignment.DistributeSpace,
        "left" => RubyAlignment.Left,
        "right" => RubyAlignment.Right,
        _ => RubyAlignment.Center
    };

    private static int? ReadRubyHalfPoints(string? value) =>
        int.TryParse(value, out var halfPoints) ? halfPoints : null;

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

        // A Mark Citation (TA) field: the instruction's leading keyword is "TA". Recover the long/short
        // forms and category from its switches and re-add the hidden citation mark run (no visible text).
        if (CitationFor(instruction) is { } citation)
        {
            paragraph.Runs.Add(Run.CitationMark(citation));
            return;
        }

        // A table-cell formula field: the instruction starts with '=' (e.g. " =SUM(ABOVE) \# "#,##0.00" ").
        // Recover the formula expression + optional number-format switch and the cached result (the run text).
        if (TableFormulaFor(instruction) is { } formula)
        {
            paragraph.Runs.Add(Run.TableFormulaFieldRun(formula, text, formatting));
            return;
        }

        // A cross-reference field: the leading keyword is REF, PAGEREF or NOTEREF (e.g. " REF _Ref1 \h ").
        // Recover the field kind, target (bookmark name / note id), insert-as switch and \h hyperlink flag,
        // and the cached resolved text (the wrapped run's text).
        if (CrossReferenceFor(instruction) is { } crossReference)
        {
            paragraph.Runs.Add(Run.CrossReferenceFieldRun(crossReference, text, formatting));
            return;
        }

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
    /// text), m:sSup / m:sSub / m:sSubSup (scripts), m:f (fraction), m:rad (radical), m:nary (n-ary),
    /// m:acc (accent), m:bar (over/under-bar), m:d (delimiter) and m:m (matrix); any other top-level child
    /// degrades to the plain text of its
    /// descendant m:t runs so nothing is lost or throws. Mirrors how the writer emits these (see
    /// <c>DocxWriter.BuildMathRun</c>).
    /// </summary>
    private static Equation ReadOMath(XElement oMath)
    {
        var equation = new Equation();
        foreach (var run in ReadMathRuns(oMath.Elements()))
            equation.Runs.Add(run);
        return equation;
    }

    private static IEnumerable<MathRun> ReadMathRuns(IEnumerable<XElement> elements)
    {
        foreach (var child in elements)
        {
            if (child.Name == M + "r")
                yield return MathRun.PlainText(MathTextOf(child));
            else if (child.Name == M + "sSup")
                yield return ReadSuperscript(child);
            else if (child.Name == M + "sSub")
                yield return ReadSubscript(child);
            else if (child.Name == M + "sSubSup")
                yield return ReadSubSuperscript(child);
            else if (child.Name == M + "f")
                yield return ReadFraction(child);
            else if (child.Name == M + "rad")
                yield return ReadRadical(child);
            else if (child.Name == M + "nary")
                yield return ReadNAry(child);
            else if (child.Name == M + "acc")
                yield return ReadAccent(child);
            else if (child.Name == M + "bar")
                yield return ReadBar(child);
            else if (child.Name == M + "d")
                yield return ReadDelimiter(child);
            else if (child.Name == M + "m")
                yield return MathRun.MatrixOf(ReadMatrix(child));
            else if (child.Name == M + "eqArr")
                yield return MathRun.EquationArrayOf(ReadEquationArray(child));
            else if (child.Name == M + "func")
                yield return ReadFunctionApply(child);
            else if (child.Name == M + "groupChr")
                yield return ReadGroupChar(child);
            else
            {
                // Unknown OMML construct: keep its text so the equation degrades rather than disappears.
                var fallback = MathTextOf(child);
                if (fallback.Length > 0)
                    yield return MathRun.PlainText(fallback);
            }
        }
    }

    private static MathRun ReadSuperscript(XElement script)
    {
        var baseSlot = script.Element(M + "e");
        var sup = script.Element(M + "sup");
        var baseText = MathTextOf(baseSlot);
        var supText = MathTextOf(sup);
        var hasNestedBase = HasStructuredMathSlot(baseSlot);
        var hasNestedSup = HasStructuredMathSlot(sup);

        return hasNestedBase || hasNestedSup
            ? new MathRun
            {
                Kind = MathRunKind.Superscript,
                Base = baseText,
                Sup = supText,
                ScriptBaseEquation = hasNestedBase ? ReadMathSlot(baseSlot) : null,
                ScriptSupEquation = hasNestedSup ? ReadMathSlot(sup) : null
            }
            : MathRun.Superscript(baseText, supText);
    }

    private static MathRun ReadSubscript(XElement script)
    {
        var baseSlot = script.Element(M + "e");
        var sub = script.Element(M + "sub");
        var baseText = MathTextOf(baseSlot);
        var subText = MathTextOf(sub);
        var hasNestedBase = HasStructuredMathSlot(baseSlot);
        var hasNestedSub = HasStructuredMathSlot(sub);

        return hasNestedBase || hasNestedSub
            ? new MathRun
            {
                Kind = MathRunKind.Subscript,
                Base = baseText,
                Sub = subText,
                ScriptBaseEquation = hasNestedBase ? ReadMathSlot(baseSlot) : null,
                ScriptSubEquation = hasNestedSub ? ReadMathSlot(sub) : null
            }
            : MathRun.Subscript(baseText, subText);
    }

    private static MathRun ReadSubSuperscript(XElement script)
    {
        var baseSlot = script.Element(M + "e");
        var sub = script.Element(M + "sub");
        var sup = script.Element(M + "sup");
        var baseText = MathTextOf(baseSlot);
        var subText = MathTextOf(sub);
        var supText = MathTextOf(sup);
        var hasNestedBase = HasStructuredMathSlot(baseSlot);
        var hasNestedSub = HasStructuredMathSlot(sub);
        var hasNestedSup = HasStructuredMathSlot(sup);

        return hasNestedBase || hasNestedSub || hasNestedSup
            ? new MathRun
            {
                Kind = MathRunKind.SubSuperscript,
                Base = baseText,
                Sub = subText,
                Sup = supText,
                ScriptBaseEquation = hasNestedBase ? ReadMathSlot(baseSlot) : null,
                ScriptSubEquation = hasNestedSub ? ReadMathSlot(sub) : null,
                ScriptSupEquation = hasNestedSup ? ReadMathSlot(sup) : null
            }
            : MathRun.SubSuperscript(baseText, subText, supText);
    }

    private static MathRun ReadFraction(XElement fraction)
    {
        var numerator = fraction.Element(M + "num");
        var denominator = fraction.Element(M + "den");
        var numeratorText = MathTextOf(numerator);
        var denominatorText = MathTextOf(denominator);
        var hasNestedNumerator = HasStructuredMathSlot(numerator);
        var hasNestedDenominator = HasStructuredMathSlot(denominator);

        return hasNestedNumerator || hasNestedDenominator
            ? new MathRun
            {
                Kind = MathRunKind.Fraction,
                Numerator = numeratorText,
                Denominator = denominatorText,
                NumeratorEquation = hasNestedNumerator ? ReadMathSlot(numerator) : null,
                DenominatorEquation = hasNestedDenominator ? ReadMathSlot(denominator) : null
            }
            : MathRun.Fraction(numeratorText, denominatorText);
    }

    private static Equation ReadMathSlot(XElement? slot) =>
        slot is null ? new Equation() : new Equation(ReadMathRuns(slot.Elements()));

    private static bool HasStructuredMathSlot(XElement? slot) =>
        slot is not null && slot.Elements().Any(IsStructuredMathElement);

    private static bool IsStructuredMathElement(XElement element) =>
        element.Name == M + "sSup" ||
        element.Name == M + "sSub" ||
        element.Name == M + "sSubSup" ||
        element.Name == M + "f" ||
        element.Name == M + "rad" ||
        element.Name == M + "nary" ||
        element.Name == M + "acc" ||
        element.Name == M + "bar" ||
        element.Name == M + "d" ||
        element.Name == M + "m" ||
        element.Name == M + "eqArr" ||
        element.Name == M + "func" ||
        element.Name == M + "groupChr";

    /// <summary>
    /// Reads a radical (m:rad). When m:radPr/m:degHide is "1" (or m:deg is empty) it is a square root
    /// (empty degree); otherwise m:deg is the nth-root degree. Mirrors <c>DocxWriter.BuildRadical</c>.
    /// </summary>
    private static MathRun ReadRadical(XElement rad)
    {
        var degHide = rad.Element(M + "radPr")?.Element(M + "degHide")?.Attribute(M + "val")?.Value;
        var degreeSlot = rad.Element(M + "deg");
        var degText = MathTextOf(degreeSlot);
        var degree = degHide == "1" ? string.Empty : degText;
        var radicand = rad.Element(M + "e");
        var radicandText = MathTextOf(radicand);
        var hasNestedDegree = degHide != "1" && HasStructuredMathSlot(degreeSlot);
        var hasNestedRadicand = HasStructuredMathSlot(radicand);
        return hasNestedDegree || hasNestedRadicand
            ? new MathRun
            {
                Kind = MathRunKind.Radical,
                Base = radicandText,
                Degree = degree,
                RadicandEquation = hasNestedRadicand ? ReadMathSlot(radicand) : null,
                DegreeEquation = hasNestedDegree ? ReadMathSlot(degreeSlot) : null
            }
            : MathRun.Radical(radicandText, degree);
    }

    /// <summary>
    /// Reads an n-ary operator (m:nary): the operator glyph from m:naryPr/m:chr (default ∑), the lower/
    /// upper limits from m:sub / m:sup and the operand from m:e. Mirrors <c>DocxWriter.BuildNAry</c>.
    /// </summary>
    private static MathRun ReadNAry(XElement nary)
    {
        var chr = nary.Element(M + "naryPr")?.Element(M + "chr")?.Attribute(M + "val")?.Value;
        var sub = nary.Element(M + "sub");
        var sup = nary.Element(M + "sup");
        var operand = nary.Element(M + "e");
        var subText = MathTextOf(sub);
        var supText = MathTextOf(sup);
        var operandText = MathTextOf(operand);
        var hasNestedSub = HasStructuredMathSlot(sub);
        var hasNestedSup = HasStructuredMathSlot(sup);
        var hasNestedOperand = HasStructuredMathSlot(operand);
        var op = string.IsNullOrEmpty(chr) ? "∑" : chr;

        return hasNestedSub || hasNestedSup || hasNestedOperand
            ? new MathRun
            {
                Kind = MathRunKind.NAry,
                Operator = op,
                Sub = subText,
                Sup = supText,
                Base = operandText,
                NAryLowerLimitEquation = hasNestedSub ? ReadMathSlot(sub) : null,
                NAryUpperLimitEquation = hasNestedSup ? ReadMathSlot(sup) : null,
                NAryOperandEquation = hasNestedOperand ? ReadMathSlot(operand) : null
            }
            : MathRun.NAry(op, subText, supText, operandText);
    }

    /// <summary>
    /// Reads an accent (m:acc): the accent glyph from m:accPr/m:chr (default a combining circumflex/hat)
    /// and the accented base from m:e. Mirrors <c>DocxWriter.BuildAccent</c>.
    /// </summary>
    private static MathRun ReadAccent(XElement acc)
    {
        var chr = acc.Element(M + "accPr")?.Element(M + "chr")?.Attribute(M + "val")?.Value;
        var baseSlot = acc.Element(M + "e");
        var baseText = MathTextOf(baseSlot);
        return HasStructuredMathSlot(baseSlot)
            ? new MathRun
            {
                Kind = MathRunKind.Accent,
                Base = baseText,
                Accent = string.IsNullOrEmpty(chr) ? "̂" : chr,
                DecoratorBaseEquation = ReadMathSlot(baseSlot)
            }
            : MathRun.AccentOf(baseText, string.IsNullOrEmpty(chr) ? "̂" : chr);
    }

    /// <summary>
    /// Reads a bar (m:bar): m:barPr/m:pos "bot" is an underbar (top = false); anything else (including the
    /// default "top" or an absent m:pos) is an overbar. The barred base comes from m:e. Mirrors
    /// <c>DocxWriter.BuildBar</c>.
    /// </summary>
    private static MathRun ReadBar(XElement bar)
    {
        var pos = bar.Element(M + "barPr")?.Element(M + "pos")?.Attribute(M + "val")?.Value;
        var baseSlot = bar.Element(M + "e");
        var baseText = MathTextOf(baseSlot);
        return HasStructuredMathSlot(baseSlot)
            ? new MathRun
            {
                Kind = MathRunKind.Bar,
                Base = baseText,
                BarTop = pos != "bot",
                DecoratorBaseEquation = ReadMathSlot(baseSlot)
            }
            : MathRun.BarOf(baseText, top: pos != "bot");
    }

    /// <summary>
    /// Reads a delimiter (m:d): the begin/end glyphs from m:dPr/m:begChr / m:endChr (default round
    /// brackets) and the content from the first m:e. Mirrors <c>DocxWriter.BuildDelimiter</c>.
    /// </summary>
    private static MathRun ReadDelimiter(XElement d)
    {
        var dPr = d.Element(M + "dPr");
        var open = dPr?.Element(M + "begChr")?.Attribute(M + "val")?.Value;
        var close = dPr?.Element(M + "endChr")?.Attribute(M + "val")?.Value;
        var content = d.Element(M + "e");
        var contentText = MathTextOf(content);
        return HasStructuredMathSlot(content)
            ? new MathRun
            {
                Kind = MathRunKind.Delimiter,
                Base = contentText,
                OpenChar = string.IsNullOrEmpty(open) ? "(" : open,
                CloseChar = string.IsNullOrEmpty(close) ? ")" : close,
                DelimiterContentEquation = ReadMathSlot(content)
            }
            : MathRun.Delimiter(
                contentText,
                string.IsNullOrEmpty(open) ? "(" : open,
                string.IsNullOrEmpty(close) ? ")" : close);
    }

    /// <summary>
    /// Reads a matrix (m:m) into a <see cref="MathMatrix"/>: one row per m:mr, one cell per m:e,
    /// preserving structured cell equations when a cell contains child OMML runs.
    /// Mirrors <c>DocxWriter.BuildMatrix</c>.
    /// </summary>
    private static MathMatrix ReadMatrix(XElement m)
    {
        var matrix = new MathMatrix();
        foreach (var mr in m.Elements(M + "mr"))
        {
            var textRow = new List<string>();
            var equationRow = new List<Equation?>();
            foreach (var cell in mr.Elements(M + "e"))
            {
                textRow.Add(MathTextOf(cell));
                equationRow.Add(HasStructuredMathSlot(cell) ? ReadMathSlot(cell) : null);
            }

            matrix.Rows.Add(textRow);
            matrix.CellEquations.Add(equationRow);
        }
        return matrix;
    }

    /// <summary>
    /// Reads an equation array (m:eqArr) into matrix-like one-cell rows, preserving structured m:e cells.
    /// Mirrors <c>DocxWriter.BuildEquationArray</c>.
    /// </summary>
    private static MathMatrix ReadEquationArray(XElement eqArr)
    {
        var array = new MathMatrix();
        foreach (var cell in eqArr.Elements(M + "e"))
        {
            array.Rows.Add([MathTextOf(cell)]);
            array.CellEquations.Add([HasStructuredMathSlot(cell) ? ReadMathSlot(cell) : null]);
        }

        return array;
    }

    /// <summary>
    /// Reads a function-apply element (m:func): the function name from the first m:r/m:t text under
    /// m:fName, and the argument from m:e. Mirrors <c>DocxWriter.BuildFunctionApply</c>.
    /// </summary>
    private static MathRun ReadFunctionApply(XElement func)
    {
        var funcName = MathTextOf(func.Element(M + "fName"));
        var argument = func.Element(M + "e");
        var argumentText = MathTextOf(argument);
        return HasStructuredMathSlot(argument)
            ? new MathRun
            {
                Kind = MathRunKind.FunctionApply,
                FuncName = funcName,
                Base = argumentText,
                FunctionArgumentEquation = ReadMathSlot(argument)
            }
            : MathRun.FunctionApply(funcName, argumentText);
    }

    /// <summary>
    /// Reads a group-character element (m:groupChr): the spanning glyph from m:groupChrPr/m:chr
    /// (default over-brace U+23DE when absent) and the position from m:groupChrPr/m:pos (default "top").
    /// Mirrors <c>DocxWriter.BuildGroupChar</c>.
    /// </summary>
    private static MathRun ReadGroupChar(XElement groupChr)
    {
        var pr = groupChr.Element(M + "groupChrPr");
        var chr = pr?.Element(M + "chr")?.Attribute(M + "val")?.Value;
        var pos = pr?.Element(M + "pos")?.Attribute(M + "val")?.Value;
        var baseSlot = groupChr.Element(M + "e");
        var baseText = MathTextOf(baseSlot);
        return HasStructuredMathSlot(baseSlot)
            ? new MathRun
            {
                Kind = MathRunKind.GroupChar,
                Base = baseText,
                GroupChr = string.IsNullOrEmpty(chr) ? "⏞" : chr,
                GroupChrPos = string.IsNullOrEmpty(pos) ? "top" : pos,
                DecoratorBaseEquation = ReadMathSlot(baseSlot)
            }
            : MathRun.GroupCharOf(
                baseText,
                string.IsNullOrEmpty(chr) ? "⏞" : chr,
                string.IsNullOrEmpty(pos) ? "top" : pos);
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
            "PAGE"     => RunFieldKind.PageNumber,
            "DATE"     => RunFieldKind.Date,
            "TIME"     => RunFieldKind.Time,
            "FILENAME" => RunFieldKind.FileName,
            "AUTHOR"   => RunFieldKind.Author,
            "NUMPAGES" => RunFieldKind.NumPages,
            "TITLE"    => RunFieldKind.Title,
            "SUBJECT"  => RunFieldKind.Subject,
            "KEYWORDS" => RunFieldKind.Keywords,
            "COMMENTS" => RunFieldKind.DocComments,
            _ => null
        };
    }

    /// <summary>
    /// Parses a table-cell formula field instruction (one that starts with <c>=</c>) into a
    /// <see cref="TableFormulaField"/>: the formula expression up to an optional <c>\#</c> numeric-picture
    /// switch, and the quoted format from that switch. Returns null for any non-formula field.
    /// </summary>
    private static TableFormulaField? TableFormulaFor(string instruction)
    {
        var trimmed = instruction.Trim();
        if (!trimmed.StartsWith('='))
            return null;

        string? format = null;
        var switchIndex = trimmed.IndexOf("\\#", StringComparison.Ordinal);
        var expression = trimmed;
        if (switchIndex >= 0)
        {
            expression = trimmed[..switchIndex].Trim();
            var rest = trimmed[(switchIndex + 2)..].Trim();
            // The format follows the \# switch, optionally double-quoted (Word quotes pictures with spaces).
            if (rest.StartsWith('"'))
            {
                var close = rest.IndexOf('"', 1);
                format = close > 0 ? rest[1..close] : rest[1..];
            }
            else if (rest.Length > 0)
            {
                format = rest.Split(' ', '\t')[0];
            }
        }

        return new TableFormulaField(expression, string.IsNullOrWhiteSpace(format) ? null : format);
    }

    /// <summary>
    /// Parses a cross-reference field instruction (leading keyword <c>REF</c>/<c>PAGEREF</c>/<c>NOTEREF</c>)
    /// into a <see cref="CrossReferenceField"/>: the field kind, the target (the first token after the
    /// keyword — a bookmark name or note id), the "insert reference to" switch (<c>\w</c> heading number,
    /// <c>\n</c> paragraph number, <c>\p</c> above/below; otherwise text/page) and the <c>\h</c> hyperlink
    /// flag. Returns null for any other field. A target-less instruction yields an empty target so nothing
    /// throws.
    /// </summary>
    private static CrossReferenceField? CrossReferenceFor(string instruction)
    {
        var trimmed = instruction.Trim();
        var tokens = trimmed.Split(' ', '\t');
        if (tokens.Length == 0)
            return null;

        var kind = tokens[0].ToUpperInvariant() switch
        {
            "REF" => CrossRefFieldKind.Ref,
            "PAGEREF" => CrossRefFieldKind.PageRef,
            "NOTEREF" => CrossRefFieldKind.NoteRef,
            _ => (CrossRefFieldKind?)null
        };
        if (kind is not { } fieldKind)
            return null;

        // The target is the first token after the keyword that is not a switch (does not start with '\').
        var target = string.Empty;
        for (var i = 1; i < tokens.Length; i++)
        {
            if (tokens[i].Length == 0 || tokens[i].StartsWith('\\'))
                continue;
            target = tokens[i];
            break;
        }

        var insertAs = CrossRefInsertAs.Text;
        if (HasSwitch(trimmed, 'w'))
            insertAs = CrossRefInsertAs.HeadingNumber;
        else if (HasSwitch(trimmed, 'n'))
            insertAs = CrossRefInsertAs.ParagraphNumber;
        else if (HasSwitch(trimmed, 'p'))
            insertAs = CrossRefInsertAs.AboveBelow;
        else if (fieldKind == CrossRefFieldKind.PageRef)
            insertAs = CrossRefInsertAs.PageNumber;

        return new CrossReferenceField(fieldKind, target, insertAs, HasSwitch(trimmed, 'h'));
    }

    // True when the field instruction carries a "\<letter>" switch (e.g. "\h"), matched case-insensitively
    // as a whitespace-delimited token so it is not confused with a letter inside the bookmark name.
    private static bool HasSwitch(string instruction, char switchLetter)
    {
        foreach (var token in instruction.Split(' ', '\t'))
        {
            if (token.Length == 2 && token[0] == '\\'
                && char.ToLowerInvariant(token[1]) == char.ToLowerInvariant(switchLetter))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Parses a Mark Citation (TA) field instruction (one whose leading keyword is <c>TA</c>) into a
    /// <see cref="Citation"/>: the long form from the <c>\l</c> switch, the short form from <c>\s</c>, and
    /// the category from the numeric <c>\c</c> switch (defaulting to <see cref="CitationCategory.Cases"/>
    /// when absent or unrecognised). Returns null for any non-TA field. A TA field with no <c>\l</c> form is
    /// treated as having an empty long citation so nothing is lost.
    /// </summary>
    private static Citation? CitationFor(string instruction)
    {
        var trimmed = instruction.Trim();
        var keyword = trimmed.Split(' ', '\t', '\\')[0];
        if (!string.Equals(keyword, "TA", StringComparison.OrdinalIgnoreCase))
            return null;

        var longForm = SwitchValue(trimmed, 'l') ?? string.Empty;
        var shortForm = SwitchValue(trimmed, 's');
        var category = CitationCategory.Cases;
        if (SwitchValue(trimmed, 'c') is { } categoryText
            && int.TryParse(categoryText, out var categoryNumber)
            && Enum.IsDefined(typeof(CitationCategory), categoryNumber))
        {
            category = (CitationCategory)categoryNumber;
        }

        return new Citation(longForm, category, shortForm);
    }

    /// <summary>
    /// Extracts the value following a <c>\</c><paramref name="switchLetter"/> switch in a field
    /// instruction: the double-quoted run when quoted (e.g. <c>\l "Brown v. Board"</c>), otherwise the next
    /// whitespace-delimited token (e.g. <c>\c 1</c>). Returns null when the switch is absent.
    /// </summary>
    private static string? SwitchValue(string instruction, char switchLetter)
    {
        var token = "\\" + switchLetter;
        var at = instruction.IndexOf(token, StringComparison.Ordinal);
        if (at < 0)
            return null;

        var rest = instruction[(at + token.Length)..].TrimStart();
        if (rest.StartsWith('"'))
        {
            var close = rest.IndexOf('"', 1);
            return close > 0 ? rest[1..close] : rest[1..];
        }
        var end = rest.IndexOfAny(new[] { ' ', '\t', '\\' });
        return end >= 0 ? rest[..end] : rest;
    }

    /// <summary>
    /// Reads a content control's w:sdtPr into a <see cref="ContentControl"/>: recovers the optional
    /// w:tag / w:alias and the control kind. A w14:checkbox (or w:checkbox) marks a checkbox control,
    /// whose checked state comes from the nested w14:checked/@val ("1"/"true"/"on"); a w:date marks a
    /// date picker (recovering its w:dateFormat); a w:dropDownList / w:comboBox marks a list control
    /// (recovering its w:listItem choices); a w:picture marks a picture control; a w:richText marks a
    /// rich-text control; anything else is a plain-text control. A null/absent w:sdtPr yields a default
    /// plain-text control.
    /// </summary>
    private static void AddContentControlRuns(
        Paragraph paragraph,
        XElement sdt,
        ZipArchive archive,
        IReadOnlyDictionary<string, string> imageRelationships,
        IReadOnlyDictionary<string, string> hyperlinkRelationships,
        IReadOnlyDictionary<int, ListKind> numbering,
        int? commentId,
        RevisionInfo revision,
        TextDocument? preservedDrawingTarget,
        IReadOnlyDictionary<string, string>? preservedDrawingRelationshipTargets,
        ContentControl? inheritedControl = null,
        string? hyperlinkUrl = null,
        string? hyperlinkAnchor = null,
        string? hyperlinkTooltip = null,
        IReadOnlyDictionary<string, string>? subDocumentRelationships = null)
    {
        var sdtPr = sdt.Element(W + "sdtPr");
        var sdtContent = sdt.Element(W + "sdtContent");
        if (sdtContent is null)
            return;

        var control = ReadContentControl(sdtPr);

        AddParagraphRuns(
            paragraph,
            sdtContent,
            archive,
            imageRelationships,
            hyperlinkRelationships,
            numbering,
            commentId,
            revision,
            control ?? inheritedControl,
            hyperlinkUrl,
            hyperlinkAnchor,
            hyperlinkTooltip,
            preservedDrawingTarget,
            preservedDrawingRelationshipTargets,
            subDocumentRelationships);
    }

    private static BlockContentControl ReadBlockContentControl(XElement? sdtPr)
    {
        var tag = sdtPr?.Element(W + "tag")?.Attribute(W + "val")?.Value;
        var alias = sdtPr?.Element(W + "alias")?.Attribute(W + "val")?.Value;
        var docPart = sdtPr?.Element(W + "docPartObj");
        var gallery = docPart?.Element(W + "docPartGallery")?.Attribute(W + "val")?.Value;
        var category = docPart?.Element(W + "docPartCategory")?.Attribute(W + "val")?.Value;
        var hasDocPartUnique = ReadOnOffElement(docPart?.Element(W + "docPartUnique"));
        var lockMode = ReadContentControlLock(sdtPr);
        var wordMetadata = ReadContentControlWordMetadata(sdtPr);

        var repeatingSection = sdtPr?.Element(W15 + "repeatingSection");
        var kind = repeatingSection is not null
            ? BlockContentControlKind.RepeatingSection
            : sdtPr?.Element(W15 + "repeatingSectionItem") is not null
                ? BlockContentControlKind.RepeatingSectionItem
                : sdtPr?.Element(W + "group") is not null
                    ? BlockContentControlKind.Group
                    : sdtPr?.Element(W + "citation") is not null
                        ? BlockContentControlKind.Citation
                        : gallery is not null
                          && string.Equals(gallery, BlockContentControl.BibliographyGallery, StringComparison.OrdinalIgnoreCase)
                            ? BlockContentControlKind.Bibliography
                            : docPart is not null
                                ? BlockContentControlKind.BuildingBlockGallery
                                : sdtPr?.Element(W + "text") is not null
                                    ? BlockContentControlKind.PlainText
                                    : BlockContentControlKind.RichText;

        return new BlockContentControl(
            kind,
            string.IsNullOrEmpty(tag) ? null : tag,
            string.IsNullOrEmpty(alias) ? null : alias,
            string.IsNullOrEmpty(gallery) ? null : gallery,
            string.IsNullOrEmpty(category) ? null : category,
            hasDocPartUnique,
            lockMode,
            wordMetadata,
            RepeatingSectionTitle: repeatingSection?.Element(W15 + "sectionTitle")
                ?.Attribute(W + "val")?.Value,
            DoNotAllowInsertDeleteSection: ReadOnOffElement(
                repeatingSection?.Element(W15 + "doNotAllowInsertDeleteSection")));
    }

    private static ContentControl ReadContentControl(XElement? sdtPr)
    {
        var tag = sdtPr?.Element(W + "tag")?.Attribute(W + "val")?.Value;
        var alias = sdtPr?.Element(W + "alias")?.Attribute(W + "val")?.Value;
        var normTag = string.IsNullOrEmpty(tag) ? null : tag;
        var normAlias = string.IsNullOrEmpty(alias) ? null : alias;
        var lockMode = ReadContentControlLock(sdtPr);
        var wordMetadata = ReadContentControlWordMetadata(sdtPr);

        var docPart = sdtPr?.Element(W + "docPartObj");
        if (docPart is not null)
        {
            var gallery = docPart.Element(W + "docPartGallery")?.Attribute(W + "val")?.Value;
            var category = docPart.Element(W + "docPartCategory")?.Attribute(W + "val")?.Value;
            return new ContentControl(
                ContentControlKind.BuildingBlockGallery,
                normTag,
                normAlias,
                LockMode: lockMode,
                WordMetadata: wordMetadata,
                DocPartGallery: string.IsNullOrEmpty(gallery) ? null : gallery,
                DocPartCategory: string.IsNullOrEmpty(category) ? null : category,
                DocPartUnique: ReadOnOffElement(docPart.Element(W + "docPartUnique")));
        }

        var checkbox = sdtPr?.Element(W14 + "checkbox") ?? sdtPr?.Element(W + "checkbox");
        if (checkbox is not null)
        {
            var val = (checkbox.Element(W14 + "checked") ?? checkbox.Element(W + "checked"))
                ?.Attribute(W14 + "val")?.Value
                ?? (checkbox.Element(W14 + "checked") ?? checkbox.Element(W + "checked"))?.Attribute(W + "val")?.Value;
            var isChecked = val is "1" or "true" or "on";
            return new ContentControl(ContentControlKind.CheckBox, normTag, normAlias, isChecked,
                LockMode: lockMode, WordMetadata: wordMetadata);
        }

        var date = sdtPr?.Element(W + "date");
        if (date is not null)
        {
            var format = date.Element(W + "dateFormat")?.Attribute(W + "val")?.Value;
            return new ContentControl(ContentControlKind.DatePicker, normTag, normAlias,
                DateFormat: string.IsNullOrEmpty(format) ? ContentControl.DefaultDateFormat : format,
                LockMode: lockMode,
                WordMetadata: wordMetadata);
        }

        var dropDown = sdtPr?.Element(W + "dropDownList");
        if (dropDown is not null)
            return new ContentControl(ContentControlKind.DropDownList, normTag, normAlias,
                ListItems: ReadListItems(dropDown), LockMode: lockMode, WordMetadata: wordMetadata);

        var combo = sdtPr?.Element(W + "comboBox");
        if (combo is not null)
            return new ContentControl(ContentControlKind.ComboBox, normTag, normAlias,
                ListItems: ReadListItems(combo), LockMode: lockMode, WordMetadata: wordMetadata);

        if (sdtPr?.Element(W + "picture") is not null)
            return new ContentControl(ContentControlKind.Picture, normTag, normAlias,
                LockMode: lockMode, WordMetadata: wordMetadata);

        if (sdtPr?.Element(W + "citation") is not null)
            return new ContentControl(ContentControlKind.Citation, normTag, normAlias,
                LockMode: lockMode, WordMetadata: wordMetadata);

        if (sdtPr?.Element(W + "group") is not null)
            return new ContentControl(ContentControlKind.Group, normTag, normAlias,
                LockMode: lockMode, WordMetadata: wordMetadata);

        if (sdtPr?.Element(W + "richText") is not null)
            return new ContentControl(ContentControlKind.RichText, normTag, normAlias,
                LockMode: lockMode, WordMetadata: wordMetadata);

        return new ContentControl(ContentControlKind.PlainText, normTag, normAlias,
            LockMode: lockMode, WordMetadata: wordMetadata);
    }

    private static ContentControlWordMetadata? ReadContentControlWordMetadata(XElement? sdtPr)
    {
        if (sdtPr is null)
            return null;

        var binding = sdtPr.Element(W + "dataBinding");
        var dataBinding = binding is null
            ? null
            : new ContentControlDataBinding(
                binding.Attribute(W + "storeItemID")?.Value,
                binding.Attribute(W + "xpath")?.Value,
                binding.Attribute(W + "prefixMappings")?.Value);
        var metadata = new ContentControlWordMetadata(
            Id: sdtPr.Element(W + "id")?.Attribute(W + "val")?.Value,
            DataBinding: dataBinding,
            PlaceholderDocPart: sdtPr.Element(W + "placeholder")?.Element(W + "docPart")
                ?.Attribute(W + "val")?.Value,
            ShowingPlaceholder: sdtPr.Element(W + "showingPlcHdr") is not null,
            Temporary: sdtPr.Element(W + "temporary") is not null,
            Appearance: sdtPr.Element(W15 + "appearance")?.Attribute(W15 + "val")?.Value,
            Color: sdtPr.Element(W15 + "color")?.Attribute(W + "val")?.Value
                ?? sdtPr.Element(W15 + "color")?.Attribute(W15 + "val")?.Value);

        return metadata == new ContentControlWordMetadata() ? null : metadata;
    }

    private static ContentControlLockMode ReadContentControlLock(XElement? sdtPr) =>
        sdtPr?.Element(W + "lock")?.Attribute(W + "val")?.Value switch
        {
            "unlocked" => ContentControlLockMode.Unlocked,
            "contentLocked" => ContentControlLockMode.ContentLocked,
            "sdtLocked" => ContentControlLockMode.ControlLocked,
            "sdtContentLocked" => ContentControlLockMode.ControlAndContentLocked,
            _ => ContentControlLockMode.NotSpecified,
        };

    private static bool ReadOnOffElement(XElement? element)
    {
        if (element is null)
            return false;

        var value = element.Attribute(W + "val")?.Value
            ?? element.Attribute(W15 + "val")?.Value;
        return value is null or "1" or "true" or "on";
    }

    /// <summary>
    /// Reads the w:listItem choices (w:displayText / w:value) of a w:dropDownList / w:comboBox element
    /// into <see cref="ContentControlListItem"/>s. A listItem with only a w:value uses it for both fields;
    /// one with only a w:displayText mirrors it into the value.
    /// </summary>
    private static IReadOnlyList<ContentControlListItem> ReadListItems(XElement list)
    {
        var items = new List<ContentControlListItem>();
        foreach (var li in list.Elements(W + "listItem"))
        {
            var display = li.Attribute(W + "displayText")?.Value;
            var value = li.Attribute(W + "value")?.Value;
            if (display is null && value is null)
                continue;
            items.Add(new ContentControlListItem(display ?? value!, value ?? display!));
        }
        return items;
    }

    /// <summary>Carries a tracked-change kind plus its author/date and optional move id while reading revisions.</summary>
    private readonly record struct RevisionInfo(RevisionKind Kind, string? Author, string? DateXml, int? MoveId = null);

    private static void AddRun(
        Paragraph paragraph,
        XElement r,
        ZipArchive archive,
        IReadOnlyDictionary<string, string> imageRelationships,
        IReadOnlyDictionary<string, string> hyperlinkRelationships,
        IReadOnlyDictionary<int, ListKind> numbering,
        string? hyperlinkUrl,
        string? hyperlinkAnchor,
        int? commentId = null,
        RevisionInfo revision = default,
        ContentControl? control = null,
        string? hyperlinkTooltip = null,
        TextDocument? preservedDrawingTarget = null,
        IReadOnlyDictionary<string, string>? preservedDrawingRelationshipTargets = null)
    {
        ResolveAlternateContent(r);

        void ApplyRevision(Run run)
        {
            if (revision.Kind == RevisionKind.None)
                return;
            run.Revision = revision.Kind;
            run.RevisionAuthor = revision.Author;
            run.RevisionDateXml = revision.DateXml;
            run.MoveRevisionId = revision.MoveId;
        }

        // A w:drawing whose anchor references a wpg:wgp group element is a floating drawing group.
        // Must be checked before every generic drawing decoder: native groups can now contain pic:pic,
        // charts, SmartArt and wps:wsp children that would otherwise claim the enclosing run.
        // Grouped drawings can carry charts, SmartArt, and images whose relationships belong to a header/footer,
        // comment, or note part. The modelled group writer only owns document-level child relationships, so retain
        // a local group verbatim when it has a relationship-backed child graph.
        if (preservedDrawingTarget is not null
            && preservedDrawingRelationshipTargets is not null
            && CapturePartLocalDrawingGroup(r, archive, preservedDrawingTarget, preservedDrawingRelationshipTargets) is { } localDrawingGroup)
        {
            var groupRun = new Run(string.Empty) { PreservedDrawing = localDrawingGroup, HyperlinkUrl = hyperlinkUrl, HyperlinkAnchor = hyperlinkAnchor, HyperlinkTooltip = hyperlinkTooltip, CommentId = commentId };
            ApplyRevision(groupRun);
            paragraph.Runs.Add(groupRun);
            return;
        }

        var drawingGroup = ReadDrawingGroup(r, archive, imageRelationships, hyperlinkRelationships, numbering);
        if (drawingGroup is not null)
        {
            var groupRun = Run.FromDrawingGroup(drawingGroup);
            groupRun.HyperlinkUrl = hyperlinkUrl;
            groupRun.HyperlinkAnchor = hyperlinkAnchor;
            ApplyRevision(groupRun);
            paragraph.Runs.Add(groupRun);
            return;
        }

        var image = ReadImage(r, archive, imageRelationships);
        if (image is not null)
        {
            var imageRun = new Run(string.Empty) { Image = image, HyperlinkUrl = hyperlinkUrl, HyperlinkAnchor = hyperlinkAnchor, HyperlinkTooltip = hyperlinkTooltip, CommentId = commentId, Control = control };
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
        var shape = ReadShape(r, archive, imageRelationships, hyperlinkRelationships, numbering);
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

        // Header/footer/comment/note charts resolve through their owning story part, while the modelled Chart
        // writer only owns document-level chart relationships. Preserve those local drawings verbatim before
        // the generic chart reader claims them, so their source part and part-local relationship survive.
        if (preservedDrawingTarget is not null
            && preservedDrawingRelationshipTargets is not null
            && CaptureUnmodelledChartDrawing(r, archive, preservedDrawingTarget, preservedDrawingRelationshipTargets) is { } localPreservedDrawing)
        {
            var drawingRun = new Run(string.Empty) { PreservedDrawing = localPreservedDrawing, HyperlinkUrl = hyperlinkUrl, HyperlinkAnchor = hyperlinkAnchor, HyperlinkTooltip = hyperlinkTooltip, CommentId = commentId };
            ApplyRevision(drawingRun);
            paragraph.Runs.Add(drawingRun);
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

        // Header/footer/comment/note OLE objects resolve through their owning story part, while the modelled OLE
        // writer only owns document-level relationships. Preserve both its binary payload and optional VML icon
        // before the generic document-level reader claims it.
        if (preservedDrawingTarget is not null
            && preservedDrawingRelationshipTargets is not null
            && CapturePartLocalEmbeddedObject(r, archive, preservedDrawingTarget, preservedDrawingRelationshipTargets) is { } localEmbeddedObject)
        {
            var objectRun = new Run(string.Empty) { PreservedDrawing = localEmbeddedObject, HyperlinkUrl = hyperlinkUrl, HyperlinkAnchor = hyperlinkAnchor, HyperlinkTooltip = hyperlinkTooltip, CommentId = commentId };
            ApplyRevision(objectRun);
            paragraph.Runs.Add(objectRun);
            return;
        }

        // A body run wrapping a w:object (VML v:shape + o:OLEObject) becomes a modelled embedded OLE object.
        var embedded = ReadEmbeddedObject(r, archive, imageRelationships);
        if (embedded is not null)
        {
            var embeddedRun = new Run(string.Empty) { EmbeddedObject = embedded, HyperlinkUrl = hyperlinkUrl, HyperlinkAnchor = hyperlinkAnchor, HyperlinkTooltip = hyperlinkTooltip, CommentId = commentId };
            ApplyRevision(embeddedRun);
            paragraph.Runs.Add(embeddedRun);
            return;
        }

        // Header/footer/comment/note SmartArt resolves through the owning story part. The modelled SmartArt
        // writer only emits document-level diagram relationships, so preserve the local diagram graph verbatim
        // before the generic reader claims it. This includes the cached diagram-drawing relationship referenced
        // from the data model, which must retain its original story-part relationship id.
        if (preservedDrawingTarget is not null
            && preservedDrawingRelationshipTargets is not null
            && CapturePartLocalSmartArtDrawing(r, archive, preservedDrawingTarget, preservedDrawingRelationshipTargets) is { } localSmartArtDrawing)
        {
            var drawingRun = new Run(string.Empty) { PreservedDrawing = localSmartArtDrawing, HyperlinkUrl = hyperlinkUrl, HyperlinkAnchor = hyperlinkAnchor, HyperlinkTooltip = hyperlinkTooltip, CommentId = commentId };
            ApplyRevision(drawingRun);
            paragraph.Runs.Add(drawingRun);
            return;
        }

        // A body/table run whose w:drawing references a SmartArt diagram becomes a modelled SmartArt run.
        var smartArt = ReadSmartArt(r, archive, imageRelationships);
        if (smartArt is not null)
        {
            var smartArtRun = new Run(string.Empty) { SmartArt = smartArt, HyperlinkUrl = hyperlinkUrl, HyperlinkAnchor = hyperlinkAnchor, HyperlinkTooltip = hyperlinkTooltip, CommentId = commentId };
            if (smartArt.IsWordSuppressedByDuplicateDrawingId
                && preservedDrawingTarget is not null
                && CapturePartLocalSmartArtDrawing(r, archive, preservedDrawingTarget, imageRelationships, documentRelationships: true) is { } preservedSmartArtDrawing)
            {
                // The model keeps the source inline extent for pagination, while the preserved payload owns
                // serialization. Word keeps this malformed duplicate-id drawing in the package but suppresses
                // its visual surface.
                smartArtRun.PreservedDrawing = preservedSmartArtDrawing;
            }
            else
            {
                smartArt.IsWordSuppressedByDuplicateDrawingId = false;
            }
            ApplyRevision(smartArtRun);
            paragraph.Runs.Add(smartArtRun);
            return;
        }

        // A body/table run whose w:drawing references a chart (or chartex) part FreeW did NOT model into a
        // Run.Chart above (e.g. chartex / an unrecognised chart structure) is preserved VERBATIM: the whole
        // drawing XML is captured into the run, and the chart part(s) + their _rels + the media they reference
        // travel as PreservedParts so the unread chart round-trips instead of vanishing. Header/footer callers
        // supply their part-local relationship map; body/table callers retain document-level ownership.
        if (preservedDrawingTarget is not null
            && preservedDrawingRelationshipTargets is null
            && CaptureUnmodelledChartDrawing(r, archive, preservedDrawingTarget, preservedDrawingRelationshipTargets) is { } preservedDrawing)
        {
            var drawingRun = new Run(string.Empty) { PreservedDrawing = preservedDrawing, HyperlinkUrl = hyperlinkUrl, HyperlinkAnchor = hyperlinkAnchor, HyperlinkTooltip = hyperlinkTooltip, CommentId = commentId };
            ApplyRevision(drawingRun);
            paragraph.Runs.Add(drawingRun);
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

        // A legacy form-field checkbox (FORMCHECKBOX): w:fldChar(begin)/w:ffData/w:checkBox. Map it to a
        // checkbox content control so it renders and round-trips as a checkbox; the field's other runs
        // (the FORMCHECKBOX instrText and the separate/end fldChar) carry no visible text and are dropped.
        // Checked state = the w:checked toggle when present, else the w:default toggle.
        var ffCheckBox = r.Element(W + "fldChar")?.Element(W + "ffData")?.Element(W + "checkBox");
        if (ffCheckBox is not null)
        {
            static bool Toggle(XElement? e) =>
                e is not null && e.Attribute(W + "val")?.Value is null or "1" or "true" or "on";
            var isChecked = ffCheckBox.Element(W + "checked") is { } checkedEl
                ? Toggle(checkedEl)
                : Toggle(ffCheckBox.Element(W + "default"));
            var name = r.Element(W + "fldChar")?.Element(W + "ffData")?.Element(W + "name")?.Attribute(W + "val")?.Value;
            var checkboxRun = Run.CheckBoxControl(isChecked, name);
            checkboxRun.Formatting = ReadRunFormatting(r.Element(W + "rPr"));
            checkboxRun.HyperlinkUrl = hyperlinkUrl;
            checkboxRun.HyperlinkAnchor = hyperlinkAnchor;
            checkboxRun.CommentId = commentId;
            ApplyRevision(checkboxRun);
            paragraph.Runs.Add(checkboxRun);
            return;
        }

        // A manual page break (w:br w:type="page") forces the following content onto a new page. It is
        // emitted as its own break-only run; recover it as a page-break run (otherwise it — and any
        // text-less run holding it — would be dropped, making FreeW under-paginate versus Word).
        if (r.Elements(W + "br").Any(b => b.Attribute(W + "type")?.Value == "page"))
        {
            var breakRun = Run.PageBreak();
            breakRun.Formatting = ReadRunFormatting(r.Element(W + "rPr"));
            ApplyRevision(breakRun);
            paragraph.Runs.Add(breakRun);
        }

        // A manual column break is distinct from a page break: in a multi-column section it advances to
        // the next column, while in a one-column section the next column begins on the following page.
        if (r.Elements(W + "br").Any(b => b.Attribute(W + "type")?.Value == "column"))
        {
            var breakRun = Run.ColumnBreak();
            breakRun.Formatting = ReadRunFormatting(r.Element(W + "rPr"));
            ApplyRevision(breakRun);
            paragraph.Runs.Add(breakRun);
        }

        // Preserve the authored child order: Word stores a manual break opportunity as w:softHyphen
        // between adjacent w:t/w:delText fragments. Reconstruct it as U+00AD in the model so later text
        // edits can keep the break at its exact character position. Tabs are likewise read in place.
        var text = ReadRunTextContent(r);
        if (text.Length == 0)
            return;
        var rPr = r.Element(W + "rPr");
        var textRun = new Run(text, ReadRunFormatting(rPr)) { HyperlinkUrl = hyperlinkUrl, HyperlinkAnchor = hyperlinkAnchor, HyperlinkTooltip = hyperlinkTooltip, CommentId = commentId, Control = control };
        ApplyRevision(textRun);
        ApplyFormatRevision(textRun, rPr);
        paragraph.Runs.Add(textRun);
    }

    private static string ReadRunTextContent(XElement run)
    {
        var text = new StringBuilder();
        foreach (var child in run.Elements())
        {
            if (child.Name == W + "t" || child.Name == W + "delText")
                text.Append(child.Value);
            else if (child.Name == W + "softHyphen")
                text.Append(Hyphenator.SoftHyphen);
            else if (child.Name == W + "tab")
                text.Append('\t');
        }
        return text.ToString();
    }

    private static void ResolveAlternateContent(XElement run)
    {
        foreach (var alternateContent in run.Elements(Mc + "AlternateContent").ToList())
        {
            var replacement = alternateContent.Elements(Mc + "Choice").FirstOrDefault()?.Nodes().ToList()
                ?? alternateContent.Element(Mc + "Fallback")?.Nodes().ToList();
            if (replacement is not null)
                alternateContent.ReplaceWith(replacement);
        }
    }

    private static Table ReadTable(
        XElement tbl,
        ZipArchive archive,
        IReadOnlyDictionary<string, string> imageRelationships,
        IReadOnlyDictionary<string, string> hyperlinkRelationships,
        IReadOnlyDictionary<int, ListKind> numbering,
        IReadOnlyDictionary<(int NumId, int Level), int> startOverrides,
        TextDocument? preservedDrawingTarget = null,
        ContentControl? inheritedControl = null,
        IReadOnlyDictionary<string, string>? subDocumentRelationships = null)
    {
        var table = new Table();

        var tblPr = tbl.Element(W + "tblPr");
        var borders = tblPr?.Element(W + "tblBorders");
        // Borders can come from the referenced table style (w:tblStyle, e.g. the default TableGrid) rather
        // than an explicit tblBorders; resolve that so styled-but-not-explicitly-bordered tables still draw.
        var tblStyleId = tblPr?.Element(W + "tblStyle")?.Attribute(W + "val")?.Value;
        var styleBorders = tblStyleId is not null
            && preservedDrawingTarget is not null
            && preservedDrawingTarget.Styles.TryGetValue(tblStyleId, out var tblStyleDef)
            && tblStyleDef.TableBorders;

        // Keep the authored table-style id even when it is not in FreeW's built-in gallery. The imported
        // style definition is retained in DocumentStyle, allowing the writer to restore custom Word styles
        // and their conditional bands instead of silently detaching the table on save.
        var catalogStyle = tblStyleId is not null ? DocumentTableStyle.FindById(tblStyleId) : null;
        table.TableStyleId = tblStyleId;
        table.Borders = ReadTableBorders(borders);

        // A catalog-styled table inherits borders from the catalog definition unless explicit tblBorders
        // override them. Merge into styleBorders so the Borders flag is set correctly.
        var catalogBorders = catalogStyle?.Borders ?? false;

        // The table-style toggles round-trip via w:tblLook (HeaderRow=firstRow, BandedRows=noHBand="0")
        // and, for RepeatHeaderRow, via the w:trPr/w:tblHeader on/off toggle on the first row. See
        // DocxWriter.BuildTable. An explicit w:val="0" disables repetition just as it does for other
        // WordprocessingML boolean properties.
        var tblLook = tblPr?.Element(W + "tblLook");
        var headerRow = ReadOnOffValue(tblLook?.Attribute(W + "firstRow")?.Value);
        var lastRow = ReadOnOffValue(tblLook?.Attribute(W + "lastRow")?.Value);
        var firstColumn = ReadOnOffValue(tblLook?.Attribute(W + "firstColumn")?.Value);
        var lastColumn = ReadOnOffValue(tblLook?.Attribute(W + "lastColumn")?.Value);
        // Keep the existing absent attribute behavior (no banding); the no*Band attributes invert the flag.
        var bandedRows = !ReadOnOffValue(tblLook?.Attribute(W + "noHBand")?.Value, defaultValue: true);
        var bandedColumns = !ReadOnOffValue(tblLook?.Attribute(W + "noVBand")?.Value, defaultValue: true);
        var firstRow = tbl.Elements(W + "tr").FirstOrDefault();
        var repeatHeader = ReadToggle(firstRow?.Element(W + "trPr"), "tblHeader");

        table.Formatting = TableFormatting.Default with
        {
            Borders = ReadBorders(borders) || styleBorders || catalogBorders,
            HeaderRow = headerRow,
            LastRow = lastRow,
            FirstColumn = firstColumn,
            LastColumn = lastColumn,
            BandedRows = bandedRows,
            BandedColumns = bandedColumns,
            RepeatHeaderRow = repeatHeader
        };

        // Preferred table width (w:tblW type="dxa"); "auto"/absent stays null (automatic width).
        var tblW = tblPr?.Element(W + "tblW");
        if (tblW?.Attribute(W + "type")?.Value == "dxa")
            table.PreferredWidthPt = DxaToPoints(tblW.Attribute(W + "w")?.Value);

        // Word uses auto-fit when tblLayout is absent. FreeW's model default is fixed, so preserve
        // the explicit OOXML mode when present and retain the historical fixed default otherwise.
        if (string.Equals(tblPr?.Element(W + "tblLayout")?.Attribute(W + "type")?.Value, "autofit", StringComparison.OrdinalIgnoreCase))
            table.AutoFit = AutoFitMode.Contents;

        // Table alignment (w:jc); absent → Left.
        table.Alignment = (tblPr?.Element(W + "jc")?.Attribute(W + "val")?.Value) switch
        {
            "center" => TableAlignment.Center,
            "right" or "end" => TableAlignment.Right,
            _ => TableAlignment.Left
        };

        // Indent from the left margin (w:tblInd, dxa); absent → null.
        var tblInd = tblPr?.Element(W + "tblInd");
        if (tblInd is not null)
            table.IndentFromLeftPt = DxaToPoints(tblInd.Attribute(W + "w")?.Value);

        // Spacing between cells (w:tblCellSpacing, dxa); absent → null.
        var tblCellSpacing = tblPr?.Element(W + "tblCellSpacing");
        if (tblCellSpacing is not null)
            table.CellSpacingPt = DxaToPoints(tblCellSpacing.Attribute(W + "w")?.Value);

        // Floating table (text wrapping around) is signalled by a w:tblpPr position element.
        table.TextWrapping = tblPr?.Element(W + "tblpPr") is not null;

        // Default cell margins (w:tblCellMar); absent → null (use the implicit docx default).
        table.DefaultCellMargins = ReadCellMargins(tblPr?.Element(W + "tblCellMar"));

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

            // Row properties (w:trPr): explicit height + rule (w:trHeight) and the cant-split on/off toggle.
            var trPr = tr.Element(W + "trPr");
            if (trPr is not null)
            {
                row.AllowBreakAcrossPages = !ReadToggle(trPr, "cantSplit");
                var trHeight = trPr.Element(W + "trHeight");
                if (trHeight is not null)
                {
                    var hVal = trHeight.Attribute(W + "val")?.Value;
                    if (hVal is not null)
                        row.HeightPt = DxaToPoints(hVal);
                    row.HeightRule = (trHeight.Attribute(W + "hRule")?.Value) switch
                    {
                        "exact" => TableRowHeightRule.Exact,
                        "atLeast" => TableRowHeightRule.AtLeast,
                        _ => TableRowHeightRule.Auto
                    };
                }
            }

            // The writer emits legacy light fills as explicit cell shading so Word honours them even when a
            // named table style is also present. Explicit OOXML shading must win over the catalog style on
            // read, so only strip those legacy values for tables without a named catalog style.
            var isStyleHeader = headerRow && rowIndex == 0;
            var isStyleBanded = bandedRows
                && !isStyleHeader
                && TableBanding.IsBandedBodyRow(rowIndex, headerRow);
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
                    // A named style does not make an explicit w:shd style-derived; Word applies explicit
                    // cell shading after the table style and the rendered fill must preserve that precedence.
                    if (normalized is not null
                        && !(catalogStyle is null
                            && ((isStyleHeader && string.Equals(normalized, StyleHeaderFill, StringComparison.OrdinalIgnoreCase))
                                || (isStyleBanded && string.Equals(normalized, StyleBandedFill, StringComparison.OrdinalIgnoreCase)))))
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

                    // Vertical alignment of the cell content (w:vAlign); absent → Top.
                    cell.VerticalAlignment = (tcPr.Element(W + "vAlign")?.Attribute(W + "val")?.Value) switch
                    {
                        "center" => TableCellVerticalAlignment.Center,
                        "bottom" => TableCellVerticalAlignment.Bottom,
                        _ => TableCellVerticalAlignment.Top
                    };

                    // Per-cell border override (w:tcBorders); absent → null (inherit table-level borders).
                    cell.Borders = ReadCellBorders(tcPr.Element(W + "tcBorders"));

                    // Per-cell margin override (w:tcMar); absent → null (inherit table default).
                    cell.Margins = ReadCellMargins(tcPr.Element(W + "tcMar"));

                    // Text direction (w:textDirection w:val); absent → Horizontal.
                    cell.TextDirection = (tcPr.Element(W + "textDirection")?.Attribute(W + "val")?.Value) switch
                    {
                        "btLr" => CellTextDirection.Rotate90,
                        "tbRl" => CellTextDirection.Rotate270,
                        _ => CellTextDirection.Horizontal
                    };
                }
                foreach (var child in tc.Elements())
                {
                    if (child.Name == W + "p")
                    {
                        cell.Paragraphs.Add(ReadParagraph(
                            child,
                            archive,
                            imageRelationships,
                            hyperlinkRelationships,
                            numbering,
                            capturePreservedNumbering: true,
                            preservedDrawingTarget: preservedDrawingTarget,
                            inheritedControl: inheritedControl,
                            startOverrides: startOverrides,
                            subDocumentRelationships: subDocumentRelationships));
                    }
                    else if (child.Name == W + "sdt")
                    {
                        var control = ReadContentControl(child.Element(W + "sdtPr"));
                        foreach (var sdtChild in child.Element(W + "sdtContent")?.Elements(W + "p") ?? [])
                        {
                            cell.Paragraphs.Add(ReadParagraph(
                                sdtChild,
                                archive,
                                imageRelationships,
                                hyperlinkRelationships,
                                numbering,
                                capturePreservedNumbering: true,
                                preservedDrawingTarget: preservedDrawingTarget,
                                inheritedControl: control,
                                startOverrides: startOverrides,
                                subDocumentRelationships: subDocumentRelationships));
                        }
                    }
                }
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

    // Reads a cell-margins container (w:tblCellMar or w:tcMar) into a TableCellMargins, or null when the
    // element is absent. Each edge defaults to the model's default if missing from the element.
    private static TableCellMargins? ReadCellMargins(XElement? container)
    {
        if (container is null)
            return null;
        double Edge(string edge, double fallback)
        {
            var w = container.Element(W + edge)?.Attribute(W + "w")?.Value;
            return w is null ? fallback : DxaToPoints(w);
        }
        return new TableCellMargins(
            TopPt: Edge("top", TableCellMargins.Default.TopPt),
            LeftPt: Edge("left", TableCellMargins.Default.LeftPt),
            BottomPt: Edge("bottom", TableCellMargins.Default.BottomPt),
            RightPt: Edge("right", TableCellMargins.Default.RightPt));
    }

    // Reads a w:tcBorders element into a CellBorders, or null when absent.
    // Each named edge (top/left/bottom/right) maps to a CellBorderEdge when present and not "none"/"nil".
    // Unknown or absent edges are left null so the cell inherits the table-level border for that edge.
    private static CellBorders? ReadCellBorders(XElement? tcBorders)
    {
        if (tcBorders is null) return null;
        CellBorderEdge? ReadEdge(string name)
        {
            var el = tcBorders.Element(W + name);
            if (el is null) return null;
            var val = el.Attribute(W + "val")?.Value;
            if (val is "none" or "nil") return null;
            var style = BorderLineStyles.FromToken(val);
            var colorRaw = el.Attribute(W + "color")?.Value;
            var colorHex = colorRaw is null or "auto" ? "#000000" : "#" + colorRaw.TrimStart('#');
            var szRaw = el.Attribute(W + "sz")?.Value;
            var widthPt = szRaw is not null && int.TryParse(szRaw, out var sz) ? sz / 8.0 : 0.5;
            return new CellBorderEdge(style, colorHex, widthPt);
        }
        var top = ReadEdge("top");
        var left = ReadEdge("left");
        var bottom = ReadEdge("bottom");
        var right = ReadEdge("right");
        if (top is null && left is null && bottom is null && right is null) return null;
        return new CellBorders { Top = top, Left = left, Bottom = bottom, Right = right };
    }

    private static bool ReadBorders(XElement? tblBorders)
    {
        if (tblBorders is null)
            return false;
        // Borders are "on" unless every edge is explicitly "none"/"nil".
        var edges = tblBorders.Elements();
        return edges.Any(e => (e.Attribute(W + "val")?.Value ?? "single") is not ("none" or "nil"));
    }

    private static TableBorders? ReadTableBorders(XElement? tblBorders)
    {
        if (tblBorders is null) return null;
        TableBorderEdge? ReadEdge(string name)
        {
            var el = tblBorders.Element(W + name);
            if (el is null || el.Attribute(W + "val")?.Value is "none" or "nil") return null;
            var widthPt = int.TryParse(el.Attribute(W + "sz")?.Value, out var sz) ? sz / 8.0 : 0.5;
            return new TableBorderEdge(
                BorderLineStyles.FromToken(el.Attribute(W + "val")?.Value),
                el.Attribute(W + "color")?.Value ?? "auto",
                widthPt);
        }
        var result = new TableBorders
        {
            Top = ReadEdge("top"), Left = ReadEdge("left"), Bottom = ReadEdge("bottom"), Right = ReadEdge("right"),
            InsideHorizontal = ReadEdge("insideH"), InsideVertical = ReadEdge("insideV")
        };
        return result.IsEmpty ? null : result;
    }

    /// <summary>
    /// Reads a picture (w:drawing) from a run into an <see cref="InlineImage"/>, if present. Handles both
    /// the inline form (wp:inline, read back as <see cref="ImageWrapping.Inline"/>) and the floating form
    /// (wp:anchor), recovering the wrapping mode, the position offsets, and the horizontal/vertical anchors.
    /// Returns null when the drawing is not a picture (e.g. a shape or chart) so those paths keep working —
    /// a picture is identified by an a:blip whose r:embed resolves to a media part and/or whose r:link
    /// resolves to an external image relationship.
    /// </summary>
    private static InlineImage? ReadImage(XElement run, ZipArchive archive, IReadOnlyDictionary<string, string> imageRelationships)
    {
        var drawing = run.Element(W + "drawing");
        var container = drawing?.Element(Wp + "inline") ?? drawing?.Element(Wp + "anchor");
        if (container is null)
            // No DrawingML picture: try the legacy VML form (w:pict/v:shape/v:imagedata), used by older
            // Word documents. Returns null when the run carries neither.
            return ReadVmlImage(run, archive, imageRelationships);

        var blip = container.Descendants(A + "blip").FirstOrDefault();
        var embeddedRelationshipId = blip?.Attribute(R + "embed")?.Value;
        var linkedRelationshipId = blip?.Attribute(R + "link")?.Value;

        string? embeddedTarget = null;
        byte[]? bytes = null;
        if (embeddedRelationshipId is not null
            && imageRelationships.TryGetValue(embeddedRelationshipId, out embeddedTarget))
        {
            bytes = LoadMedia(archive, embeddedTarget);
        }

        string? linkedTarget = null;
        if (linkedRelationshipId is not null)
            imageRelationships.TryGetValue(linkedRelationshipId, out linkedTarget);

        if (bytes is null && string.IsNullOrWhiteSpace(linkedTarget))
            return null;

        var extent = container.Element(Wp + "extent");
        var widthPt = EmuToPoints(extent?.Attribute("cx")?.Value);
        var heightPt = EmuToPoints(extent?.Attribute("cy")?.Value);

        // Recover the image's original format so non-PNG pictures round-trip verbatim. Prefer the media
        // part's extension (the relationship target carries the real extension), falling back to the bytes'
        // magic number when the extension is unknown/absent.
        var formatTarget = embeddedTarget ?? linkedTarget ?? string.Empty;
        var format = ResolveImageFormat(formatTarget, bytes ?? []);

        // Restore accessibility alt text from wp:docPr/@descr; absent attribute leaves AltText null.
        var descr = container.Element(Wp + "docPr")?.Attribute("descr")?.Value;
        var image = new InlineImage(bytes ?? [], widthPt, heightPt, format)
        {
            AltText = string.IsNullOrEmpty(descr) ? null : descr,
            LinkedImageTarget = linkedTarget,
        };

        // A wp:anchor is a floating image: recover wrapping mode, offsets and anchors. A wp:inline reads
        // back as ImageWrapping.Inline with default position fields, exactly as before.
        if (container.Name == Wp + "anchor")
            ApplyFloatingPosition(container, image);

        // Recover rotation/flip, crop, and picture border from the pic:pic payload.
        var picPic = container.Descendants(Pic + "pic").FirstOrDefault();
        if (picPic is not null)
            ApplyPictureFormat(picPic, image);

        return image;
    }

    /// <summary>
    /// Reads a legacy VML picture (<c>w:pict/v:shape|v:rect/v:imagedata[@r:id]</c>) into an
    /// <see cref="InlineImage"/>, if present. Older Word documents embed images this way instead of
    /// DrawingML; the media resolves through the same relationship map and the size comes from the VML
    /// shape's CSS <c>style</c> (width/height). Returns null when the run carries no VML image.
    /// </summary>
    private static InlineImage? ReadVmlImage(XElement run, ZipArchive archive, IReadOnlyDictionary<string, string> imageRelationships)
    {
        var pict = run.Element(W + "pict");
        if (pict is null)
            return null;

        // v:shape is the common case; v:rect (and other VML shapes) can also carry a v:imagedata fill.
        var shape = pict.Elements(V + "shape").FirstOrDefault(s => s.Element(V + "imagedata") is not null)
            ?? pict.Elements(V + "rect").FirstOrDefault(s => s.Element(V + "imagedata") is not null)
            ?? pict.Descendants(V + "imagedata").FirstOrDefault()?.Parent;
        var relationshipId = shape?.Element(V + "imagedata")?.Attribute(R + "id")?.Value
            ?? shape?.Element(V + "imagedata")?.Attribute(O + "relid")?.Value;
        if (relationshipId is null || !imageRelationships.TryGetValue(relationshipId, out var target))
            return null;

        var bytes = LoadMedia(archive, target);
        if (bytes is null)
            return null;

        var (widthPt, heightPt) = ParseVmlShapeSize(shape?.Attribute("style")?.Value);
        var format = ResolveImageFormat(target, bytes);
        var alt = shape?.Attribute(O + "title")?.Value ?? shape?.Element(V + "imagedata")?.Attribute(O + "title")?.Value;
        return new InlineImage(bytes, widthPt, heightPt, format)
        {
            AltText = string.IsNullOrEmpty(alt) ? null : alt,
        };
    }

    /// <summary>
    /// Recovers a floating image's wrapping mode + position from a wp:anchor: the wrap element selects the
    /// <see cref="ImageWrapping"/> (wp:wrapNone disambiguated by @behindDoc into Behind / InFront), and
    /// wp:positionH/V supply the anchors (@relativeFrom) and offsets (wp:posOffset, EMU → points).
    /// </summary>
    /// <summary>
    /// Reads a floating object's position/wrapping from a <c>wp:anchor</c> element into a
    /// <see cref="FloatingPlacement"/>. Mirrors <see cref="ApplyFloatingPosition"/> for images.
    /// </summary>
    private static void ApplyFloatingPlacement(XElement anchor, FloatingPlacement placement)
    {
        placement.Wrapping = ReadWrapping(anchor);
        placement.WrapTextSide = ReadWrapTextSide(anchor);

        if (anchor.Attribute("relativeHeight")?.Value is { } relH
            && int.TryParse(relH, out var zOrder))
            placement.ZOrderIndex = zOrder;

        var positionH = anchor.Element(Wp + "positionH");
        placement.HorizontalAnchor = ReadHorizontalAnchor(positionH?.Attribute("relativeFrom")?.Value);
        placement.HorizontalOffsetPt = EmuToPoints(positionH?.Element(Wp + "posOffset")?.Value);

        var positionV = anchor.Element(Wp + "positionV");
        placement.VerticalAnchor = ReadVerticalAnchor(positionV?.Attribute("relativeFrom")?.Value);
        placement.VerticalOffsetPt = EmuToPoints(positionV?.Element(Wp + "posOffset")?.Value);
    }

    private static void ApplyFloatingPosition(XElement anchor, InlineImage image)
    {
        image.Wrapping = ReadWrapping(anchor);
        image.WrapTextSide = ReadWrapTextSide(anchor);

        // Z-order: relativeHeight is an integer on wp:anchor; default 0 when absent or unparseable.
        if (anchor.Attribute("relativeHeight")?.Value is { } relH
            && int.TryParse(relH, out var zOrder))
            image.ZOrderIndex = zOrder;

        var positionH = anchor.Element(Wp + "positionH");
        image.HorizontalAnchor = ReadHorizontalAnchor(positionH?.Attribute("relativeFrom")?.Value);
        image.HorizontalOffsetPt = EmuToPoints(positionH?.Element(Wp + "posOffset")?.Value);

        var positionV = anchor.Element(Wp + "positionV");
        image.VerticalAnchor = ReadVerticalAnchor(positionV?.Attribute("relativeFrom")?.Value);
        image.VerticalOffsetPt = EmuToPoints(positionV?.Element(Wp + "posOffset")?.Value);
    }

    /// <summary>
    /// Recovers rotation/flip, crop and picture border from a <c>pic:pic</c> element inside a drawing
    /// container. All fields are optional — absent attributes leave the model defaults (0 / false / null).
    /// </summary>
    private static void ApplyPictureFormat(XElement picPic, InlineImage image)
    {
        // a:xfrm: rotation (degrees, stored as integer × 60000), flipH, flipV.
        var xfrm = picPic.Descendants(A + "xfrm").FirstOrDefault();
        if (xfrm is not null)
        {
            if (xfrm.Attribute("rot")?.Value is { } rotStr && long.TryParse(rotStr, out var rotEmu))
                image.RotationAngle = rotEmu / 60000.0;
            image.FlipH = xfrm.Attribute("flipH")?.Value is "1" or "true";
            image.FlipV = xfrm.Attribute("flipV")?.Value is "1" or "true";
        }

        // a:srcRect: crop fractions encoded as per-mille integers (×100000).
        var srcRect = picPic.Descendants(A + "srcRect").FirstOrDefault();
        if (srcRect is not null)
        {
            static double PerMille(string? val) =>
                long.TryParse(val, out var v) ? v / 100000.0 : 0;
            image.CropLeft   = PerMille(srcRect.Attribute("l")?.Value);
            image.CropRight  = PerMille(srcRect.Attribute("r")?.Value);
            image.CropTop    = PerMille(srcRect.Attribute("t")?.Value);
            image.CropBottom = PerMille(srcRect.Attribute("b")?.Value);
        }

        // a:blip children: lum/satMod/alphaModFix (adjustments) + grayscl/duotone (recolor) + colorTemp ext.
        var blip = picPic.Descendants(A + "blip").FirstOrDefault();
        if (blip is not null)
        {
            var FreeWExt = XNamespace.Get("http://schemas.freew.app/2024/ext");

            // Recolor detection (checked before lum/satMod since recolor modes override some adjustments).
            var hasGrayscl = blip.Element(A + "grayscl") is not null;
            var duotone    = blip.Element(A + "duotone");
            // Duotone with brown first srgbClr → Sepia preset; other duotones are unknown/passthrough.
            var firstDuotoneHex = duotone?.Elements(A + "srgbClr").FirstOrDefault()?.Attribute("val")?.Value;
            if (hasGrayscl)
            {
                // BlackWhite: grayscl + lum with large positive contrast.
                var lumEl = blip.Element(A + "lum");
                if (lumEl is not null
                    && long.TryParse(lumEl.Attribute("contrast")?.Value, out var bwContrast)
                    && bwContrast >= 90000)
                {
                    image.RecolorMode = ImageRecolorMode.BlackWhite;
                    // BrightnessPct from the lum @bright attr.
                    if (long.TryParse(lumEl.Attribute("bright")?.Value, out var bwBright))
                        image.BrightnessPct = bwBright / 1000.0;
                }
                else
                {
                    image.RecolorMode = ImageRecolorMode.Grayscale;
                }
            }
            else if (firstDuotoneHex is not null
                     && firstDuotoneHex.Equals("7B4012", StringComparison.OrdinalIgnoreCase))
            {
                image.RecolorMode = ImageRecolorMode.Sepia;
            }
            else
            {
                // Washout: alphaModFix @amt=50000 combined with lum @bright>=40000 (and no other recolor).
                var alphaFixEl = blip.Element(A + "alphaModFix");
                var lumWashEl  = blip.Element(A + "lum");
                if (alphaFixEl is not null
                    && long.TryParse(alphaFixEl.Attribute("amt")?.Value, out var washAmt) && washAmt == 50000
                    && lumWashEl is not null
                    && long.TryParse(lumWashEl.Attribute("bright")?.Value, out var washBright) && washBright >= 40000)
                {
                    image.RecolorMode = ImageRecolorMode.Washout;
                    image.BrightnessPct = (washBright - 40000) / 1000.0;
                    long.TryParse(lumWashEl.Attribute("contrast")?.Value, out var washContrast);
                    image.ContrastPct = washContrast / 1000.0;
                }
            }

            // FreeW private image extensions. Older packages used direct freew:* attributes on a:blip;
            // current packages use a:extLst so Word's schema validator accepts the document.
            var tempRaw = blip.Attribute(FreeWExt + "colorTemp")?.Value
                ?? ReadFreeWBlipExtensionValue(blip, "colorTemp");
            if (tempRaw is not null && long.TryParse(tempRaw, out var tempVal))
                image.ColorTemperature = tempVal / 1000.0;

            var artisticRaw = blip.Attribute(FreeWExt + "artisticEffect")?.Value
                ?? ReadFreeWBlipExtensionValue(blip, "artisticEffect");
            if (artisticRaw is not null && int.TryParse(artisticRaw, out var artisticId)
                && Enum.IsDefined(typeof(ImageArtisticEffect), artisticId))
                image.ArtisticEffect = (ImageArtisticEffect)artisticId;

            // Standard adjustments — only when no recolor mode already consumed lum/alphaModFix.
            if (image.RecolorMode == ImageRecolorMode.None)
            {
                var lum = blip.Element(A + "lum");
                if (lum is not null)
                {
                    static double PerMillePct(string? val) =>
                        long.TryParse(val, out var v) ? v / 1000.0 : 0;
                    image.BrightnessPct = PerMillePct(lum.Attribute("bright")?.Value);
                    image.ContrastPct   = PerMillePct(lum.Attribute("contrast")?.Value);
                }

                // a:alphaModFix: opacity per-mille = (100 - transparencyPct) × 1000.
                var alphaFix = blip.Element(A + "alphaModFix");
                if (alphaFix is not null && long.TryParse(alphaFix.Attribute("amt")?.Value, out var amt))
                    image.TransparencyPct = 100.0 - amt / 1000.0;
            }

            // a:satMod: saturation modifier per-mille (100 % = 100000; neutral = omitted).
            var satMod = blip.Element(A + "satMod");
            if (satMod is not null && long.TryParse(satMod.Attribute("val")?.Value, out var satVal))
                image.SaturationPct = satVal / 1000.0;
        }

        // a:effectLst (inside pic:spPr): shadow / glow / reflection / softEdge / bevel.
        var spPr = picPic.Descendants(Pic + "spPr").FirstOrDefault();
        var effectLst = spPr?.Element(A + "effectLst");
        if (effectLst is not null)
        {
            // a:outerShdw → ShadowPreset. Map by blurRad: ≤4pt→1, ≤6pt→2, ≤8pt→3, dir=270→4, else→5.
            var outerShdw = effectLst.Element(A + "outerShdw");
            if (outerShdw is not null)
            {
                if (long.TryParse(outerShdw.Attribute("dir")?.Value, out var shdwDir)
                    && shdwDir == 270 * 60000)
                    image.ShadowPreset = 4;
                else if (long.TryParse(outerShdw.Attribute("blurRad")?.Value, out var shdwBlur))
                    image.ShadowPreset = shdwBlur <= 4 * 12700 ? 1
                                        : shdwBlur <= 6 * 12700 ? 2
                                        : shdwBlur <= 8 * 12700 ? 3 : 5;
                else
                    image.ShadowPreset = 1;
            }

            // a:glow → GlowSizePt + GlowColorHex.
            var glow = effectLst.Element(A + "glow");
            if (glow is not null)
            {
                if (long.TryParse(glow.Attribute("rad")?.Value, out var glowRad))
                    image.GlowSizePt = glowRad / 12700.0;
                image.GlowColorHex = glow.Descendants(A + "srgbClr").FirstOrDefault()?.Attribute("val")?.Value;
            }

            // a:reflection → ReflectionPreset. Distinguish by dist and stA.
            var reflection = effectLst.Element(A + "reflection");
            if (reflection is not null)
            {
                long.TryParse(reflection.Attribute("dist")?.Value, out var refDist);
                long.TryParse(reflection.Attribute("stA")?.Value,  out var refStA);
                image.ReflectionPreset =
                    refStA < 60000 && refDist < 1000 ? 1   // tight, touching
                    : refStA < 60000 && refDist < 6 * 12700 ? 2  // tight, 4pt
                    : refStA < 60000 ? 3                         // tight, 8pt
                    : refDist < 1000 ? 4                         // half, touching
                    : 5;                                          // half, 4pt
            }

            // a:softEdge → SoftEdgePt.
            var softEdge = effectLst.Element(A + "softEdge");
            if (softEdge is not null && long.TryParse(softEdge.Attribute("rad")?.Value, out var seRad))
                image.SoftEdgePt = seRad / 12700.0;

            // a:innerShdw → BevelPreset (approximation; @dir encodes preset 1-4).
            var innerShdw = effectLst.Element(A + "innerShdw");
            if (innerShdw is not null && long.TryParse(innerShdw.Attribute("dir")?.Value, out var bevelDir))
                image.BevelPreset = (int)(bevelDir / (90 * 60000)) + 1;

            var importedEffects = new ShapeEffectLst();
            if (outerShdw is not null)
            {
                importedEffects.HasShadow = true;
                if (int.TryParse(outerShdw.Attribute("blurRad")?.Value, out var blurRad)) importedEffects.ShadowBlurRad = blurRad;
                if (int.TryParse(outerShdw.Attribute("dist")?.Value, out var distance)) importedEffects.ShadowDist = distance;
                if (int.TryParse(outerShdw.Attribute("dir")?.Value, out var direction)) importedEffects.ShadowDir = direction;
                var color = outerShdw.Descendants(A + "srgbClr").FirstOrDefault()?.Attribute("val")?.Value;
                if (!string.IsNullOrWhiteSpace(color)) importedEffects.ShadowColorHex = color;
                if (int.TryParse(outerShdw.Descendants(A + "alpha").FirstOrDefault()?.Attribute("val")?.Value, out var alpha)) importedEffects.ShadowAlpha = alpha;
            }
            if (glow is not null)
            {
                importedEffects.HasGlow = true;
                if (int.TryParse(glow.Attribute("rad")?.Value, out var radius)) importedEffects.GlowRad = radius;
                var color = glow.Descendants(A + "srgbClr").FirstOrDefault()?.Attribute("val")?.Value;
                if (!string.IsNullOrWhiteSpace(color)) importedEffects.GlowColorHex = color;
                if (int.TryParse(glow.Descendants(A + "alpha").FirstOrDefault()?.Attribute("val")?.Value, out var alpha)) importedEffects.GlowAlpha = alpha;
            }
            if (reflection is not null)
            {
                importedEffects.HasReflection = true;
                if (int.TryParse(reflection.Attribute("blurRad")?.Value, out var blurRad)) importedEffects.ReflectionBlurRad = blurRad;
                if (int.TryParse(reflection.Attribute("stA")?.Value ?? reflection.Attribute("alpha")?.Value, out var startAlpha)) importedEffects.ReflectionStartAlpha = startAlpha;
                if (int.TryParse(reflection.Attribute("stPos")?.Value, out var startPosition)) importedEffects.ReflectionStartPosition = startPosition;
                if (int.TryParse(reflection.Attribute("endA")?.Value, out var endAlpha)) importedEffects.ReflectionEndAlpha = endAlpha;
                if (int.TryParse(reflection.Attribute("endPos")?.Value, out var endPosition)) importedEffects.ReflectionEndPosition = endPosition;
                if (int.TryParse(reflection.Attribute("dist")?.Value, out var distance)) importedEffects.ReflectionDist = distance;
                if (int.TryParse(reflection.Attribute("dir")?.Value, out var direction)) importedEffects.ReflectionDir = direction;
                if (int.TryParse(reflection.Attribute("fadeDir")?.Value, out var fadeDirection)) importedEffects.ReflectionFadeDir = fadeDirection;
                if (int.TryParse(reflection.Attribute("sx")?.Value, out var scaleX)) importedEffects.ReflectionScaleX = scaleX;
                if (int.TryParse(reflection.Attribute("sy")?.Value, out var scaleY)) importedEffects.ReflectionScaleY = scaleY;
                if (int.TryParse(reflection.Attribute("kx")?.Value, out var skewX)) importedEffects.ReflectionSkewX = skewX;
                if (int.TryParse(reflection.Attribute("ky")?.Value, out var skewY)) importedEffects.ReflectionSkewY = skewY;
                importedEffects.ReflectionAlignment = reflection.Attribute("algn")?.Value ?? importedEffects.ReflectionAlignment;
                if (int.TryParse(reflection.Attribute("rotWithShape")?.Value, out var rotateWithShape)) importedEffects.ReflectionRotWithShape = rotateWithShape != 0;
            }
            if (softEdge is not null && int.TryParse(softEdge.Attribute("rad")?.Value, out var softEdgeRadius))
            {
                importedEffects.HasSoftEdge = true;
                importedEffects.SoftEdgeRad = softEdgeRadius;
            }
            if (importedEffects.HasAny)
                image.ImportedEffects = importedEffects;
        }

        // a:ln (inside pic:spPr): border width, solid-fill color, dash.
        var ln = picPic.Descendants(A + "ln").FirstOrDefault();
        if (ln is not null)
        {
            // Width: @w in EMU (1 pt = 12700 EMU).
            if (ln.Attribute("w")?.Value is { } wStr && long.TryParse(wStr, out var wEmu))
                image.BorderWidthPt = wEmu / 12700.0;

            // Color: a:solidFill/a:srgbClr/@val.
            var hex = ln.Descendants(A + "srgbClr").FirstOrDefault()?.Attribute("val")?.Value;
            if (!string.IsNullOrEmpty(hex))
                image.BorderColorHex = hex.ToUpperInvariant();

            // Dash: a:prstDash/@val.
            var dash = ln.Element(A + "prstDash")?.Attribute("val")?.Value;
            if (!string.IsNullOrEmpty(dash) && dash != "solid")
                image.BorderDash = dash;
        }
    }

    private static string? ReadFreeWBlipExtensionValue(XElement blip, string localName)
    {
        XNamespace freeWExt = "http://schemas.freew.app/2024/ext";
        return blip.Element(A + "extLst")
            ?.Elements(A + "ext")
            .Elements(freeWExt + localName)
            .FirstOrDefault()
            ?.Attribute("val")
            ?.Value;
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

    private static FloatingWrapTextSide ReadWrapTextSide(XElement anchor)
    {
        var wrapText = anchor.Element(Wp + "wrapSquare")?.Attribute("wrapText")?.Value
            ?? anchor.Element(Wp + "wrapTight")?.Attribute("wrapText")?.Value;
        return wrapText switch
        {
            "left" => FloatingWrapTextSide.Left,
            "right" => FloatingWrapTextSide.Right,
            "largest" => FloatingWrapTextSide.Largest,
            _ => FloatingWrapTextSide.BothSides
        };
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
    /// the <see cref="WordArtStyle"/> preset from which effect is present. A native DrawingML text warp is
    /// itself a WordArt marker even when Word has no gallery fill/effect payload, so a warped text box is
    /// recovered as the default style instead of being downgraded to an ordinary shape. Returns null when the
    /// drawing is a plain, unwarped shape or not a wsp at all, so the ordinary shape/image paths keep working.
    ///
    /// SIMPLIFICATION: the preset is inferred from the *kind* of effect present (gradient → GradientFill,
    /// outline → Outline, shadow → Shadow, else FillBlue), not from exact colour values — colours are fixed
    /// per preset by the writer, so this is lossless for FreeW-authored WordArt.
    /// </summary>
    private static WordArt? ReadWordArt(XElement run)
    {
        var drawing = run.Element(W + "drawing");
        var inline = drawing?.Element(Wp + "inline");
        var anchor = drawing?.Element(Wp + "anchor");
        var container = inline ?? anchor;
        var wsp = container?.Descendants(Wps + "wsp").FirstOrDefault();
        var txbxContent = wsp?.Element(Wps + "txbx")?.Element(W + "txbxContent");
        if (txbxContent is null)
            return null;

        // Older FreeW-authored packages used illegal DrawingML effect children under the text run's w:rPr.
        // Current packages put those effects on wps:spPr, which is where Word's schema allows them.
        var rPr = txbxContent.Descendants(W + "r").FirstOrDefault()?.Element(W + "rPr");
        var docPrName = container!.Element(Wp + "docPr")?.Attribute("name")?.Value
            ?? wsp!.Element(Wps + "cNvPr")?.Attribute("name")?.Value
            ?? string.Empty;
        var bodyPrEl = wsp!.Element(Wps + "bodyPr");
        var warpToken = bodyPrEl?.Element(A + "prstTxWarp")?.Attribute("prst")?.Value;
        var hasTextWarp = !string.IsNullOrWhiteSpace(warpToken);
        var hasWordArtMarker = docPrName.StartsWith("WordArt", StringComparison.Ordinal)
            || docPrName.StartsWith("GroupChild:WordArt:", StringComparison.Ordinal)
            || hasTextWarp;
        var style = rPr is null ? null : InferWordArtStyle(rPr);
        if (style is null && hasWordArtMarker && wsp!.Element(Wps + "spPr") is { } spPr)
            style = InferWordArtStyle(spPr);
        style ??= hasTextWarp ? WordArtStyle.FillBlue : null;
        if (style is null)
            return null;

        var text = string.Concat(txbxContent.Descendants(W + "t").Select(t => t.Value));
        var fontSizePt = HalfPointsToPoints(rPr?.Element(W + "sz")?.Attribute(W + "val")?.Value) ?? 36;

        var wordArt = new WordArt(text, style.Value, fontSizePt)
        {
            FontFamily = rPr?.Element(W + "rFonts")?.Attribute(W + "ascii")?.Value
                ?? rPr?.Element(W + "rFonts")?.Attribute(W + "hAnsi")?.Value,
            Bold = ReadToggle(rPr, "b")
        };
        var wordArtExtent = container.Element(Wp + "extent");
        wordArt.WidthPt = EmuToPoints(wordArtExtent?.Attribute("cx")?.Value);
        wordArt.HeightPt = EmuToPoints(wordArtExtent?.Attribute("cy")?.Value);

        var wordArtXfrm = wsp!.Element(Wps + "spPr")?.Element(A + "xfrm");
        if (wordArtXfrm is not null)
        {
            if (wordArtXfrm.Attribute("rot")?.Value is { } rotStr && long.TryParse(rotStr, out var rotEmu))
                wordArt.RotationAngle = rotEmu / 60000.0;
            wordArt.FlipH = wordArtXfrm.Attribute("flipH")?.Value is "1" or "true";
            wordArt.FlipV = wordArtXfrm.Attribute("flipV")?.Value is "1" or "true";
        }

        // Alt text: wp:docPr/@descr on the inline or anchor drawing.
        var waDocPrDescr = container!.Element(Wp + "docPr")?.Attribute("descr")?.Value;
        if (!string.IsNullOrEmpty(waDocPrDescr))
            wordArt.AltText = waDocPrDescr;

        // Warp: a:prstTxWarp/@prst inside wps:bodyPr (W24).
        wordArt.Warp = WarpFromToken(warpToken);
        wordArt.TextFitMode = WordArtTextFitModeFromBodyPr(bodyPrEl);
        var normalAutoFit = bodyPrEl?.Element(A + "normAutofit");
        if (int.TryParse(normalAutoFit?.Attribute("fontScale")?.Value, out var fontScale))
            wordArt.NormalAutoFitFontScale = fontScale;
        if (int.TryParse(normalAutoFit?.Attribute("lnSpcReduction")?.Value, out var lineSpacingReduction))
            wordArt.NormalAutoFitLineSpacingReduction = lineSpacingReduction;

        if (anchor is not null)
        {
            wordArt.Placement = new FloatingPlacement();
            ApplyFloatingPlacement(anchor, wordArt.Placement);
        }
        return wordArt;
    }

    private static WordArtTextFitMode WordArtTextFitModeFromBodyPr(XElement? bodyPr) =>
        bodyPr?.Element(A + "noAutofit") is not null ? WordArtTextFitMode.NoAutoFit :
        bodyPr?.Element(A + "spAutoFit") is not null ? WordArtTextFitMode.ShapeAutoFit :
        bodyPr?.Element(A + "normAutofit") is not null ? WordArtTextFitMode.NormalAutoFit :
        WordArtTextFitMode.Unspecified;

    /// <summary>
    /// Infers a <see cref="WordArtStyle"/> from the DrawingML text effects under a WordArt run's w:rPr, or
    /// null when none are present (so the element is a plain shape, not WordArt). The inference order matches
    /// the writer's discriminators — see DocxWriter.WordArtEffects for the per-style signatures.
    /// </summary>
    private static WordArtStyle? InferWordArtStyle(XElement rPr)
    {
        var hasGradFill    = rPr.Element(A + "gradFill") is not null;
        var hasNoFill      = rPr.Element(A + "noFill")   is not null;
        var hasLn          = rPr.Element(A + "ln")       is not null;
        var hasEffectLst   = rPr.Element(A + "effectLst") is not null;
        var hasSolidFill   = rPr.Element(A + "solidFill") is not null;
        var hasPattFill    = rPr.Element(A + "pattFill")  is not null;
        var hasSp3d        = rPr.Element(A + "sp3d")      is not null;

        // Pattern fill is unambiguous
        if (hasPattFill) return WordArtStyle.PatternFill;

        // No fill + thick outline → ChromeOne
        if (hasNoFill && hasLn) return WordArtStyle.ChromeOne;

        // Gradient fills — differentiate by stop count
        if (hasGradFill)
        {
            var stops = rPr.Element(A + "gradFill")!.Descendants(A + "gs").Count();
            if (stops >= 3) return WordArtStyle.GradFillMulti;
            // 2-stop: distinguish FillGold (gold start) from GradientFill (blue start)
            var firstStopColor = rPr.Element(A + "gradFill")!.Descendants(A + "gs")
                .FirstOrDefault()?.Descendants(A + "srgbClr").FirstOrDefault()?.Attribute("val")?.Value ?? "";
            if (firstStopColor.StartsWith("C09", StringComparison.OrdinalIgnoreCase) ||
                firstStopColor.StartsWith("c09", StringComparison.OrdinalIgnoreCase))
                return WordArtStyle.FillGold;
            return WordArtStyle.GradientFill;
        }

        // Solid fill variants
        if (hasSolidFill)
        {
            var solidColor = rPr.Element(A + "solidFill")!.Descendants(A + "srgbClr")
                .FirstOrDefault()?.Attribute("val")?.Value ?? "";

            // White solidFill
            if (solidColor.Equals("FFFFFF", StringComparison.OrdinalIgnoreCase))
            {
                // ChromeTwo has white + ln + effectLst with outerShdw children; FillWhite has an empty effectLst marker.
                bool effectLstHasChildren = hasEffectLst &&
                    rPr.Element(A + "effectLst")!.HasElements;
                if (hasLn && effectLstHasChildren) return WordArtStyle.ChromeTwo;  // white+ln+shadow
                if (hasLn)                         return WordArtStyle.FillWhite;   // white+thin ln (or empty effectLst)
                return WordArtStyle.FillWhite;
            }

            // Dark fill
            bool isDark = solidColor.Equals("242424", StringComparison.OrdinalIgnoreCase) ||
                          solidColor.Equals("404040", StringComparison.OrdinalIgnoreCase);
            if (isDark && hasEffectLst)
            {
                var glow = rPr.Element(A + "effectLst")?.Element(A + "glow");
                if (glow is not null)
                {
                    var glowColor = glow.Descendants(A + "srgbClr").FirstOrDefault()?.Attribute("val")?.Value ?? "";
                    return glowColor.StartsWith("C09", StringComparison.OrdinalIgnoreCase)
                        ? WordArtStyle.GlowGold : WordArtStyle.GlowBlue;
                }
            }

            // Orange fill
            bool isOrange = solidColor.Equals("ED7D31", StringComparison.OrdinalIgnoreCase);
            if (isOrange)
            {
                if (hasSp3d) return WordArtStyle.Bevel;
                if (hasEffectLst) return WordArtStyle.ShadowOrange;
                return WordArtStyle.FillGold; // fallback
            }

            // Blue fill (WordArtFillColor = 1F4E79)
            if (hasLn && hasEffectLst) return WordArtStyle.ChromeTwo;
            if (hasLn)                 return WordArtStyle.Outline;
            if (hasEffectLst)
            {
                var refl = rPr.Element(A + "effectLst")?.Element(A + "reflection");
                if (refl is not null) return WordArtStyle.Reflection;
                return WordArtStyle.Shadow;
            }
            return WordArtStyle.FillBlue;
        }

        return null; // not WordArt
    }

    private static WordArtWarp WarpFromToken(string? token) => token switch
    {
        "textArchUp"          => WordArtWarp.ArchUp,
        "textArchDown"        => WordArtWarp.ArchDown,
        "textCircle"          => WordArtWarp.Circle,
        "textButton"          => WordArtWarp.Button,
        "textWave1"           => WordArtWarp.Wave1,
        "textWave2"           => WordArtWarp.Wave2,
        "textInflate"         => WordArtWarp.Inflate,
        "textDeflate"         => WordArtWarp.Deflate,
        "textInflateBottom"   => WordArtWarp.InflateBottom,
        "textChevron"         => WordArtWarp.ChevronUp,
        "textChevronInverted" => WordArtWarp.ChevronDown,
        "textFadeRight"       => WordArtWarp.FadeRight,
        "textFadeLeft"        => WordArtWarp.FadeLeft,
        "textSlantUp"         => WordArtWarp.SlantUp,
        "textSlantDown"       => WordArtWarp.SlantDown,
        _                     => WordArtWarp.None,
    };

    /// <summary>
    /// Reads an inline DrawingML shape / text box (w:drawing → wp:inline → a:graphic/a:graphicData → wps:wsp)
    /// from a run into a <see cref="Shape"/>, if present. Recovers the preset geometry kind (a:prstGeom/@prst),
    /// the EMU extent (size in points), the optional solid fill (a:solidFill/a:srgbClr), and any text-box body
    /// paragraphs (wps:txbx/w:txbxContent). Returns null for a non-shape drawing (e.g. a picture) so the image
    /// path keeps working. Mirrors how the writer emits these (see <c>DocxWriter.BuildShapeDrawing</c>).
    /// </summary>
    private static Shape? ReadShape(
        XElement run,
        ZipArchive archive,
        IReadOnlyDictionary<string, string> imageRelationships,
        IReadOnlyDictionary<string, string> hyperlinkRelationships,
        IReadOnlyDictionary<int, ListKind> numbering)
    {
        var drawing = run.Element(W + "drawing");
        var inline = drawing?.Element(Wp + "inline");
        var anchor = drawing?.Element(Wp + "anchor");
        var container = inline ?? anchor;
        var wsp = container?.Descendants(Wps + "wsp").FirstOrDefault();
        if (wsp is null)
            return null;

        var extent = container!.Element(Wp + "extent");
        var widthPt = EmuToPoints(extent?.Attribute("cx")?.Value);
        var heightPt = EmuToPoints(extent?.Attribute("cy")?.Value);

        var spPr = wsp.Element(Wps + "spPr");
        var preset = spPr?.Element(A + "prstGeom")?.Attribute("prst")?.Value;
        var custGeomEl = spPr?.Element(A + "custGeom");
        var hasTextBody = wsp.Element(Wps + "txbx")?.Element(W + "txbxContent") is not null;
        var kind = ShapeKindFromPreset(preset, hasTextBody);

        var shape = new Shape(kind, widthPt, heightPt);

        // a:xfrm: rotation (degrees, stored as integer × 60000), flipH, flipV.
        var shapeXfrm = spPr?.Element(A + "xfrm");
        if (shapeXfrm is not null)
        {
            if (shapeXfrm.Attribute("rot")?.Value is { } rotStr && long.TryParse(rotStr, out var rotEmu))
                shape.RotationAngle = rotEmu / 60000.0;
            shape.FlipH = shapeXfrm.Attribute("flipH")?.Value is "1" or "true";
            shape.FlipV = shapeXfrm.Attribute("flipV")?.Value is "1" or "true";
        }

        // Custom geometry (a:custGeom): recover freeform polygon segments.
        if (custGeomEl is not null)
        {
            var custGeo = new CustomGeometry();
            var pathEl = custGeomEl.Descendants(A + "path").FirstOrDefault();
            if (pathEl is not null)
            {
                if (long.TryParse(pathEl.Attribute("w")?.Value, out var cgW) && cgW > 0) custGeo.Width = cgW;
                if (long.TryParse(pathEl.Attribute("h")?.Value, out var cgH) && cgH > 0) custGeo.Height = cgH;
                foreach (var seg in pathEl.Elements())
                {
                    if (seg.Name == A + "moveTo")
                    {
                        var pt = seg.Element(A + "pt");
                        if (pt is not null
                            && long.TryParse(pt.Attribute("x")?.Value, out var mx)
                            && long.TryParse(pt.Attribute("y")?.Value, out var my))
                            custGeo.Segments.Add(new CustomSegment(CustomSegmentKind.MoveTo, new CustomPoint(mx, my)));
                    }
                    else if (seg.Name == A + "lnTo")
                    {
                        var pt = seg.Element(A + "pt");
                        if (pt is not null
                            && long.TryParse(pt.Attribute("x")?.Value, out var lx)
                            && long.TryParse(pt.Attribute("y")?.Value, out var ly))
                            custGeo.Segments.Add(new CustomSegment(CustomSegmentKind.LineTo, new CustomPoint(lx, ly)));
                    }
                    else if (seg.Name == A + "cubicBezTo")
                    {
                        var points = seg.Elements(A + "pt")
                            .Select(pt =>
                            {
                                if (long.TryParse(pt.Attribute("x")?.Value, out var x)
                                    && long.TryParse(pt.Attribute("y")?.Value, out var y))
                                {
                                    return new CustomPoint(x, y);
                                }

                                return null;
                            })
                            .ToList();
                        if (points.Count == 3 && points.All(point => point is not null))
                        {
                            custGeo.Segments.Add(new CustomSegment(
                                CustomSegmentKind.CubicBezierTo,
                                points[2]!,
                                points[0]!,
                                points[1]!));
                        }
                    }
                    else if (seg.Name == A + "close")
                    {
                        custGeo.Segments.Add(new CustomSegment(CustomSegmentKind.Close));
                    }
                }
            }
            if (custGeo.Segments.Count > 0)
                shape.CustomGeometry = custGeo;
        }

        // Fill: extended fills (gradient / pattern / no-fill) take priority over solid.
        var solidFillEl = spPr?.Element(A + "solidFill");
        var gradFillEl  = spPr?.Element(A + "gradFill");
        var pattFillEl  = spPr?.Element(A + "pattFill");
        var noFillEl    = spPr?.Element(A + "noFill");

        if (noFillEl is not null)
        {
            shape.ExtendedFill = ShapeFill.NoFill();
        }
        else if (gradFillEl is not null)
        {
            var gradFill = new ShapeFill { Kind = ShapeFillKind.Gradient };
            var angAttr = gradFillEl.Element(A + "lin")?.Attribute("ang")?.Value;
            if (int.TryParse(angAttr, out var ang)) gradFill.GradientAngle = ang;
            foreach (var gs in gradFillEl.Descendants(A + "gs"))
            {
                var pos = int.TryParse(gs.Attribute("pos")?.Value, out var p) ? p : 0;
                var c   = gs.Descendants(A + "srgbClr").FirstOrDefault()?.Attribute("val")?.Value ?? "000000";
                gradFill.GradientStops.Add(new GradientStop(pos, "#" + c.TrimStart('#')));
            }
            shape.ExtendedFill = gradFill;
        }
        else if (pattFillEl is not null)
        {
            var pattFill = new ShapeFill { Kind = ShapeFillKind.Pattern };
            pattFill.PatternPreset = pattFillEl.Attribute("prst")?.Value;
            var fgHex = pattFillEl.Element(A + "fgClr")?.Descendants(A + "srgbClr").FirstOrDefault()?.Attribute("val")?.Value;
            if (!string.IsNullOrEmpty(fgHex)) pattFill.PatternFgColorHex = "#" + fgHex.TrimStart('#');
            var bgHex = pattFillEl.Element(A + "bgClr")?.Descendants(A + "srgbClr").FirstOrDefault()?.Attribute("val")?.Value;
            if (!string.IsNullOrEmpty(bgHex)) pattFill.PatternBgColorHex = "#" + bgHex.TrimStart('#');
            shape.ExtendedFill = pattFill;
        }
        else if (solidFillEl is not null)
        {
            var fill = solidFillEl.Element(A + "srgbClr")?.Attribute("val")?.Value;
            if (!string.IsNullOrEmpty(fill) && !string.Equals(fill, "auto", StringComparison.Ordinal))
                shape.FillColorHex = "#" + fill.TrimStart('#');
        }

        // Outline: a:ln/@w (EMU) + a:solidFill/a:srgbClr/@val + optional a:prstDash/@val.
        var ln = spPr?.Element(A + "ln");
        if (ln is not null)
        {
            var outlineFill = ln.Element(A + "solidFill")?.Element(A + "srgbClr")?.Attribute("val")?.Value;
            if (!string.IsNullOrEmpty(outlineFill))
            {
                shape.OutlineColorHex = "#" + outlineFill.TrimStart('#');
                if (long.TryParse(ln.Attribute("w")?.Value, out var widthEmu) && widthEmu > 0)
                    shape.OutlineWidthPt = widthEmu / 12700.0;
                shape.OutlineDash = ln.Element(A + "prstDash")?.Attribute("val")?.Value;
            }
        }

        // Effects: a:effectLst (shadow / glow / soft-edge / reflection) and a:sp3d (bevel).
        var effectLstEl = spPr?.Element(A + "effectLst");
        var sp3dEl      = spPr?.Element(A + "sp3d");
        if (effectLstEl is not null || sp3dEl is not null)
        {
            var fx = new ShapeEffectLst();
            if (effectLstEl?.Element(A + "outerShdw") is { } shdw)
            {
                fx.HasShadow = true;
                if (int.TryParse(shdw.Attribute("blurRad")?.Value, out var br)) fx.ShadowBlurRad = br;
                if (int.TryParse(shdw.Attribute("dist")?.Value, out var dist)) fx.ShadowDist = dist;
                if (int.TryParse(shdw.Attribute("dir")?.Value, out var dir)) fx.ShadowDir = dir;
                var sc = shdw.Descendants(A + "srgbClr").FirstOrDefault()?.Attribute("val")?.Value;
                if (!string.IsNullOrEmpty(sc)) fx.ShadowColorHex = sc;
                var sa = shdw.Descendants(A + "alpha").FirstOrDefault()?.Attribute("val")?.Value;
                if (int.TryParse(sa, out var salpha)) fx.ShadowAlpha = salpha;
            }
            if (effectLstEl?.Element(A + "glow") is { } glow)
            {
                fx.HasGlow = true;
                if (int.TryParse(glow.Attribute("rad")?.Value, out var gr)) fx.GlowRad = gr;
                var gc = glow.Descendants(A + "srgbClr").FirstOrDefault()?.Attribute("val")?.Value;
                if (!string.IsNullOrEmpty(gc)) fx.GlowColorHex = gc;
                var ga = glow.Descendants(A + "alpha").FirstOrDefault()?.Attribute("val")?.Value;
                if (int.TryParse(ga, out var galpha)) fx.GlowAlpha = galpha;
            }
            if (effectLstEl?.Element(A + "softEdge") is { } softEdge)
            {
                fx.HasSoftEdge = true;
                if (int.TryParse(softEdge.Attribute("rad")?.Value, out var ser)) fx.SoftEdgeRad = ser;
            }
            if (effectLstEl?.Element(A + "reflection") is { } refl)
            {
                fx.HasReflection = true;
                if (int.TryParse(refl.Attribute("blurRad")?.Value, out var rbr)) fx.ReflectionBlurRad = rbr;
                if (int.TryParse(refl.Attribute("stA")?.Value ?? refl.Attribute("alpha")?.Value, out var ra)) fx.ReflectionStartAlpha = ra;
                if (int.TryParse(refl.Attribute("stPos")?.Value, out var rsp)) fx.ReflectionStartPosition = rsp;
                if (int.TryParse(refl.Attribute("endA")?.Value, out var rea)) fx.ReflectionEndAlpha = rea;
                if (int.TryParse(refl.Attribute("endPos")?.Value, out var rep)) fx.ReflectionEndPosition = rep;
                if (int.TryParse(refl.Attribute("dir")?.Value, out var rd)) fx.ReflectionDir = rd;
                if (int.TryParse(refl.Attribute("fadeDir")?.Value, out var rfd)) fx.ReflectionFadeDir = rfd;
                if (int.TryParse(refl.Attribute("sx")?.Value, out var rsx)) fx.ReflectionScaleX = rsx;
                if (int.TryParse(refl.Attribute("sy")?.Value, out var rsy)) fx.ReflectionScaleY = rsy;
                if (int.TryParse(refl.Attribute("kx")?.Value, out var rkx)) fx.ReflectionSkewX = rkx;
                if (int.TryParse(refl.Attribute("ky")?.Value, out var rky)) fx.ReflectionSkewY = rky;
                fx.ReflectionAlignment = refl.Attribute("algn")?.Value ?? fx.ReflectionAlignment;
                if (int.TryParse(refl.Attribute("rotWithShape")?.Value, out var rrws)) fx.ReflectionRotWithShape = rrws != 0;
                if (int.TryParse(refl.Attribute("dist")?.Value, out var rdist)) fx.ReflectionDist = rdist;
            }
            if (sp3dEl?.Element(A + "bevelT") is { } bevel)
            {
                fx.HasBevel = true;
                if (int.TryParse(bevel.Attribute("w")?.Value, out var bw)) fx.BevelW = bw;
                if (int.TryParse(bevel.Attribute("h")?.Value, out var bh)) fx.BevelH = bh;
                fx.BevelPresetType = bevel.Attribute("prst")?.Value ?? "circle";
            }
            if (fx.HasShadow || fx.HasGlow || fx.HasSoftEdge || fx.HasReflection || fx.HasBevel)
                shape.Effects = fx;
        }

        // Alt text: wp:docPr/@descr on the inline or anchor drawing.
        var docPrDescr = container!.Element(Wp + "docPr")?.Attribute("descr")?.Value;
        if (!string.IsNullOrEmpty(docPrDescr))
            shape.AltText = docPrDescr;

        // Text-box body paragraphs share the owning document's numbering.xml and story-local hyperlink
        // relationships, just like ordinary paragraphs in that story.
        var txbxContent = wsp.Element(Wps + "txbx")?.Element(W + "txbxContent");
        if (txbxContent is not null)
        {
            foreach (var p in txbxContent.Elements(W + "p"))
                shape.TextParagraphs.Add(ReadParagraph(
                    p,
                    archive,
                    imageRelationships,
                    hyperlinkRelationships,
                    numbering,
                    capturePreservedNumbering: true));
        }

        // Text direction: wps:bodyPr/@vert + @rot.
        var bodyPr = wsp.Element(Wps + "bodyPr");
        var vert = bodyPr?.Attribute("vert")?.Value;
        if (string.Equals(vert, "eaVert", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(bodyPr?.Attribute("rot")?.Value, out var rotVal))
        {
            shape.TextDirection = rotVal > 0 ? ShapeTextDirection.Rotate90 : ShapeTextDirection.Rotate270;
        }

        if (anchor is not null)
        {
            shape.Placement = new FloatingPlacement();
            ApplyFloatingPlacement(anchor, shape.Placement);
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
    /// A linked object retains its external relationship target without opening or activating that source.
    /// The icon's size becomes the object's size when present.
    /// </summary>
    private static EmbeddedObject? ReadEmbeddedObject(XElement run, ZipArchive archive, IReadOnlyDictionary<string, string> relationships)
    {
        var obj = run.Element(W + "object");
        var ole = obj?.Element(O + "OLEObject");
        if (ole is null)
            return null;

        var relationshipId = ole.Attribute(R + "id")?.Value;
        if (relationshipId is null || !relationships.TryGetValue(relationshipId, out var partPath))
            return null;

        var progId = ole.Attribute("ProgID")?.Value ?? string.Empty;
        EmbeddedObject embedded;
        if (string.Equals(ole.Attribute("Type")?.Value, "Link", StringComparison.OrdinalIgnoreCase))
        {
            embedded = EmbeddedObject.CreateLinked(partPath, progId);
        }
        else
        {
            var payload = LoadMedia(archive, partPath);
            if (payload is null)
                return null;
            embedded = new EmbeddedObject(payload, progId);
        }

        // The VML v:shape supplies the on-page icon (v:imagedata r:id → media part) and the size (CSS @style).
        var shape = obj!.Element(V + "shape");
        var imagedataRel = shape?.Element(V + "imagedata")?.Attribute(R + "id")?.Value;
        if (imagedataRel is not null
            && relationships.TryGetValue(imagedataRel, out var iconPath)
            && LoadMedia(archive, iconPath) is { } iconBytes)
        {
            var (iconWidthPt, iconHeightPt) = ParseVmlShapeSize(shape!.Attribute("style")?.Value);
            embedded.Icon = new InlineImage(iconBytes, iconWidthPt, iconHeightPt, ResolveImageFormat(iconPath, iconBytes));
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
        var drawing = run.Element(W + "drawing");
        var inline = drawing?.Element(Wp + "inline");
        var anchor = drawing?.Element(Wp + "anchor");
        var container = inline ?? anchor;
        if (container is null)
            return null;

        var chartRef = container.Descendants(C + "chart").FirstOrDefault(e => e.Attribute(R + "id") is not null);
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

        // Size: the inline or anchor extent (EMU) maps back to points.
        var extent = container.Element(Wp + "extent");
        chart.WidthPt = EmuToPoints(extent?.Attribute("cx")?.Value);
        chart.HeightPt = EmuToPoints(extent?.Attribute("cy")?.Value);

        if (anchor is not null)
        {
            chart.Placement = new FloatingPlacement();
            ApplyFloatingPlacement(anchor, chart.Placement);
        }

        // c:style — chart style id (persisted by BuildChartSpace; default 0 = unset).
        // chartXml is non-null here because chartElement (derived from chartXml.Root) passed the null guard above.
        var chartSpace = chartXml!.Root!;
        if (int.TryParse(chartSpace.Element(C + "style")?.Attribute("val")?.Value, out var styleId) && styleId > 0)
            chart.StyleId = styleId;

        // Native c:style ids are family-specific themes. Preserve the concrete visual elements so
        // an imported chart does not inherit FreeW's similarly numbered gallery approximation.
        chart.NativeVisualSettings = new ChartNativeVisualSettings(
            ShowGridlines: plotArea.Descendants(C + "majorGridlines").Any(),
            HasPlotAreaFill: plotArea.Element(C + "spPr")?.Elements()
                .Any(element => element.Name == A + "solidFill"
                    || element.Name == A + "gradFill"
                    || element.Name == A + "pattFill"
                    || element.Name == A + "blipFill") == true,
            ShowDataLabels: typeElement.Element(C + "dLbls") is not null,
            ScatterConnectsPoints: kind == ChartKind.Scatter
                && string.Equals(typeElement.Element(C + "scatterStyle")?.Attribute("val")?.Value, "lineMarker", StringComparison.OrdinalIgnoreCase));

        // Current packages store the gallery ids in the extension URI, which is valid chart XML.
        // Keep reading the old private child payload so existing FreeW files retain their settings.
        XNamespace freew = "http://schemas.freew.dev/chart-design/2026";
        var freewExt = chartSpace.Descendants(C + "ext")
            .FirstOrDefault(e =>
            {
                var uri = e.Attribute("uri")?.Value;
                return uri == LegacyFreeWChartDesignExtensionUri
                    || uri?.StartsWith(FreeWChartDesignExtensionUri, StringComparison.Ordinal) == true;
            });
        if (freewExt is not null)
        {
            var uri = freewExt.Attribute("uri")?.Value;
            if (uri?.StartsWith(FreeWChartDesignExtensionUri, StringComparison.Ordinal) == true)
            {
                ApplyFreeWChartDesignExtensionUri(uri, chart);
            }
            else
            {
                var colorSchemeId = freewExt.Element(freew + "colorScheme")?.Attribute("id")?.Value;
                if (!string.IsNullOrEmpty(colorSchemeId))
                    chart.ColorSchemeId = colorSchemeId;
                if (int.TryParse(freewExt.Element(freew + "quickLayout")?.Attribute("id")?.Value, out var qlId) && qlId > 0)
                    chart.QuickLayoutId = qlId;
            }
        }

        return chart;
    }

    private static void ApplyFreeWChartDesignExtensionUri(string uri, Chart chart)
    {
        var fragmentIndex = uri.IndexOf('#');
        if (fragmentIndex < 0 || fragmentIndex == uri.Length - 1)
            return;

        foreach (var token in uri[(fragmentIndex + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = token.IndexOf('=');
            if (separator <= 0 || separator == token.Length - 1)
                continue;

            var key = token[..separator];
            var value = Uri.UnescapeDataString(token[(separator + 1)..]);
            if (key.Equals("colorScheme", StringComparison.OrdinalIgnoreCase))
                chart.ColorSchemeId = value;
            else if (key.Equals("quickLayout", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(value, out var quickLayoutId)
                && quickLayoutId > 0)
                chart.QuickLayoutId = quickLayoutId;
        }
    }

    /// <summary>
    /// Captures a run's inline <c>w:drawing</c> VERBATIM when it references a chart (or <c>chartex</c>) part
    /// that FreeW did NOT model into a <see cref="Chart"/> above. Body/table relationships resolve against
    /// <c>document.xml.rels</c>; header/footer callers supply their part-local relationship map. For each, the
    /// chart part, its own <c>_rels</c> and the media those rels point at are added to
    /// <see cref="TextDocument.Preserved"/> (deduped by part name, carrying their content-type Overrides). The
    /// returned <see cref="PreservedDrawing"/> carries the drawing XML plus the reference (original rId →
    /// preserved chart part) the writer rewrites to a relationship in the owning part.
    /// Returns null when the drawing references no chart-typed relationship (so the ordinary paths are unaffected).
    /// </summary>
    private static PreservedDrawing? CaptureUnmodelledChartDrawing(
        XElement run,
        ZipArchive archive,
        TextDocument document,
        IReadOnlyDictionary<string, string>? partRelationshipTargets = null)
    {
        var drawing = run.Element(W + "drawing");
        if (drawing is null)
            return null;

        // Body drawings resolve against document.xml.rels. Header/footer drawings pass their own complete
        // relationship map and identify chart ownership from the DrawingML payload.
        var docRels = partRelationshipTargets is null ? ReadDocumentRelationships(archive) : null;
        var contentTypeOverrides = ReadContentTypeOverrides(archive);
        var contentTypeDefaults = ReadContentTypeDefaults(archive);

        var references = new List<PreservedDrawingReference>();
        foreach (var descendant in drawing.DescendantsAndSelf())
        {
            var relId = descendant.Attribute(R + "id")?.Value ?? descendant.Attribute(R + "embed")?.Value;
            if (relId is null)
                continue;
            var chartEx = descendant.Name == Cx + "chart";
            var chart = descendant.Name == C + "chart";
            if (!chartEx && !chart)
                continue;

            string? partName;
            string? relationshipType;
            if (partRelationshipTargets is not null)
            {
                partName = partRelationshipTargets.TryGetValue(relId, out var localPartPath)
                    ? "/" + localPartPath.TrimStart('/')
                    : null;
                relationshipType = null;
            }
            else if (docRels!.TryGetValue(relId, out var rel)
                && rel.Type is ChartRelType or ChartExRelType)
            {
                partName = OpcPathHelper.ResolveAbsolutePartName("/word", rel.Target);
                relationshipType = rel.Type;
            }
            else
            {
                continue;
            }
            if (partName is null)
                continue;

            // A body chart keeps its document relationship type; a header/footer chart deliberately does not,
            // so the writer emits the rewritten relationship only in the owning part's _rels file.
            if (CapturePreservedPart(archive, document, partName, contentTypeOverrides, contentTypeDefaults, relationshipType))
                CaptureReferencedParts(archive, document, partName, contentTypeOverrides, contentTypeDefaults);
            references.Add(new PreservedDrawingReference(relId, partName));
        }

        if (references.Count == 0)
            return null;

        return new PreservedDrawing(drawing.ToString(SaveOptions.DisableFormatting), references);
    }

    /// <summary>
    /// Captures a SmartArt drawing from a non-document story part (header/footer/comment/note) verbatim.
    /// Diagram data, layout, quick-style and colours are direct <c>dgm:relIds</c> relationships; the cached
    /// rendered drawing is referenced from the data model using a second relationship in the owning story part.
    /// All of these relationships retain their source ids because the data model itself carries the cached-drawing
    /// id and is preserved byte-for-byte.
    /// </summary>
    private static PreservedDrawing? CapturePartLocalSmartArtDrawing(
        XElement run,
        ZipArchive archive,
        TextDocument document,
        IReadOnlyDictionary<string, string> partRelationshipTargets,
        bool documentRelationships = false)
    {
        var drawing = run.Element(W + "drawing");
        var relIds = drawing?.Descendants(Dgm + "relIds").FirstOrDefault();
        if (drawing is null || relIds is null)
            return null;

        var contentTypeOverrides = ReadContentTypeOverrides(archive);
        var contentTypeDefaults = ReadContentTypeDefaults(archive);
        var references = new List<PreservedDrawingReference>();

        string? RelationshipTypeFor(string partName)
        {
            if (!contentTypeOverrides.TryGetValue(partName, out var contentType))
            {
                var extension = partName.Contains('.') ? partName[(partName.LastIndexOf('.') + 1)..] : string.Empty;
                contentTypeDefaults.TryGetValue(extension, out contentType);
            }

            return contentType switch
            {
                DiagramDataContentType => DiagramDataRelType,
                DiagramLayoutContentType => DiagramLayoutRelType,
                DiagramStyleContentType => DiagramStyleRelType,
                DiagramColorsContentType => DiagramColorsRelType,
                DiagramDrawingContentType => DiagramDrawingRelType,
                _ => null
            };
        }

        void CaptureLocalRelationship(string relationshipId)
        {
            if (!partRelationshipTargets.TryGetValue(relationshipId, out var localPartPath))
                return;
            var partName = "/" + localPartPath.TrimStart('/');
            var relationshipType = RelationshipTypeFor(partName);
            if (!CapturePreservedPart(
                    archive,
                    document,
                    partName,
                    contentTypeOverrides,
                    contentTypeDefaults,
                    relationshipType: documentRelationships ? relationshipType : null))
                return;
            CaptureReferencedParts(archive, document, partName, contentTypeOverrides, contentTypeDefaults);
            if (!references.Any(reference => reference.OriginalRelId == relationshipId))
                references.Add(new PreservedDrawingReference(relationshipId, partName, relationshipType));
        }

        var dataRelationshipId = relIds.Attribute(R + "dm")?.Value;
        foreach (var relationshipId in relIds.Attributes()
                     .Where(attribute => attribute.Name.Namespace == R)
                     .Select(attribute => attribute.Value)
                     .Distinct(StringComparer.Ordinal))
            CaptureLocalRelationship(relationshipId);

        // Word stores the cached dsp:drawing's relationship on the document/story part, not under dataN.xml.rels.
        // Its id is carried in dgm:dataModelExt/@relId, so capture that extra story-local hop as well.
        if (dataRelationshipId is not null
            && partRelationshipTargets.TryGetValue(dataRelationshipId, out var dataPartPath)
            && LoadPart(archive, dataPartPath) is { } dataPart)
            foreach (var relationshipId in dataPart.Descendants(Dsp + "dataModelExt")
                         .Select(element => element.Attribute("relId")?.Value)
                         .Where(relationshipId => relationshipId is not null)
                         .Cast<string>()
                         .Distinct(StringComparer.Ordinal))
                CaptureLocalRelationship(relationshipId);

        return references.Count == 0
            ? null
            : new PreservedDrawing(drawing.ToString(SaveOptions.DisableFormatting), references);
    }

    /// <summary>
    /// Captures a VML <c>w:object</c> from a non-document story part. Its embedded OLE binary and optional
    /// presentation icon are both relationship-owned by that part, so treating the object as a document-level
    /// modelled run would leave the re-emitted header/footer/comment/note with dangling references.
    /// </summary>
    private static PreservedDrawing? CapturePartLocalEmbeddedObject(
        XElement run,
        ZipArchive archive,
        TextDocument document,
        IReadOnlyDictionary<string, string> partRelationshipTargets)
    {
        var obj = run.Element(W + "object");
        var oleRelationshipId = obj?.Element(O + "OLEObject")?.Attribute(R + "id")?.Value;
        if (obj is null || oleRelationshipId is null)
            return null;

        var contentTypeOverrides = ReadContentTypeOverrides(archive);
        var contentTypeDefaults = ReadContentTypeDefaults(archive);
        var references = new List<PreservedDrawingReference>();

        void CaptureLocalRelationship(string relationshipId, string relationshipType)
        {
            if (!partRelationshipTargets.TryGetValue(relationshipId, out var localPartPath))
                return;
            var partName = "/" + localPartPath.TrimStart('/');
            if (!CapturePreservedPart(archive, document, partName, contentTypeOverrides, contentTypeDefaults, relationshipType: null))
                return;
            CaptureReferencedParts(archive, document, partName, contentTypeOverrides, contentTypeDefaults);
            references.Add(new PreservedDrawingReference(relationshipId, partName, relationshipType));
        }

        CaptureLocalRelationship(oleRelationshipId, OleObjectRelType);
        var imageRelationshipId = obj.Descendants(V + "imagedata").Select(image => image.Attribute(R + "id")?.Value)
            .FirstOrDefault(relationshipId => relationshipId is not null);
        if (imageRelationshipId is not null)
            CaptureLocalRelationship(imageRelationshipId, ImageRelType);

        return references.Count == 0
            ? null
            : new PreservedDrawing(obj.ToString(SaveOptions.DisableFormatting), references);
    }

    /// <summary>
    /// Preserves a relationship-backed WordprocessingDrawing group in a non-document story part. A native group
    /// may mix pictures, charts, and SmartArt frames; all resolve through the enclosing story part rather than
    /// document.xml.rels. Groups with only shape/text children remain modelled normally.
    /// </summary>
    private static PreservedDrawing? CapturePartLocalDrawingGroup(
        XElement run,
        ZipArchive archive,
        TextDocument document,
        IReadOnlyDictionary<string, string> partRelationshipTargets)
    {
        var drawing = run.Element(W + "drawing");
        if (drawing?.Descendants(Wpg + "wgp").Any() != true)
            return null;

        var contentTypeOverrides = ReadContentTypeOverrides(archive);
        var contentTypeDefaults = ReadContentTypeDefaults(archive);
        var references = new List<PreservedDrawingReference>();
        var diagramDataPartPaths = new List<string>();

        string? RelationshipTypeFor(string partName)
        {
            if (!contentTypeOverrides.TryGetValue(partName, out var contentType))
            {
                var extension = partName.Contains('.') ? partName[(partName.LastIndexOf('.') + 1)..] : string.Empty;
                contentTypeDefaults.TryGetValue(extension, out contentType);
            }

            return contentType switch
            {
                ChartContentType => ChartRelType,
                ChartExContentType => ChartExRelType,
                DiagramDataContentType => DiagramDataRelType,
                DiagramLayoutContentType => DiagramLayoutRelType,
                DiagramStyleContentType => DiagramStyleRelType,
                DiagramColorsContentType => DiagramColorsRelType,
                DiagramDrawingContentType => DiagramDrawingRelType,
                OleObjectContentType => OleObjectRelType,
                { } value when value.StartsWith("image/", StringComparison.OrdinalIgnoreCase) => ImageRelType,
                _ => null
            };
        }

        void CaptureLocalRelationship(string relationshipId)
        {
            if (references.Any(reference => reference.OriginalRelId == relationshipId)
                || !partRelationshipTargets.TryGetValue(relationshipId, out var localPartPath))
                return;

            var partName = "/" + localPartPath.TrimStart('/');
            var relationshipType = RelationshipTypeFor(partName);
            if (relationshipType is null
                || !CapturePreservedPart(archive, document, partName, contentTypeOverrides, contentTypeDefaults, relationshipType: null))
                return;

            CaptureReferencedParts(archive, document, partName, contentTypeOverrides, contentTypeDefaults);
            references.Add(new PreservedDrawingReference(relationshipId, partName, relationshipType));
            if (relationshipType == DiagramDataRelType)
                diagramDataPartPaths.Add(localPartPath);
        }

        foreach (var relationshipId in drawing.DescendantsAndSelf()
                     .Attributes()
                     .Where(attribute => attribute.Name.Namespace == R)
                     .Select(attribute => attribute.Value)
                     .Distinct(StringComparer.Ordinal))
            CaptureLocalRelationship(relationshipId);

        // The rendered dsp:drawing is named by dgm:dataModelExt/@relId in dataN.xml but is still related from
        // the enclosing document/story part. Capture this otherwise hidden local relationship for every group
        // SmartArt frame.
        foreach (var dataPartPath in diagramDataPartPaths)
            if (LoadPart(archive, dataPartPath) is { } dataPart)
                foreach (var relationshipId in dataPart.Descendants(Dsp + "dataModelExt")
                             .Select(element => element.Attribute("relId")?.Value)
                             .Where(relationshipId => relationshipId is not null)
                             .Cast<string>()
                             .Distinct(StringComparer.Ordinal))
                    CaptureLocalRelationship(relationshipId);

        return references.Count == 0
            ? null
            : new PreservedDrawing(drawing.ToString(SaveOptions.DisableFormatting), references);
    }

    /// <summary>
    /// Captures a single package part into <see cref="TextDocument.Preserved"/> (byte-for-byte, with its
    /// content-type Override and optional document relationship type), skipping parts already captured (dedup by
    /// name). Returns true when the part exists (whether freshly captured or already present), false when absent.
    /// </summary>
    private static bool CapturePreservedPart(
        ZipArchive archive,
        TextDocument document,
        string partName,
        IReadOnlyDictionary<string, string> contentTypeOverrides,
        IReadOnlyDictionary<string, string> contentTypeDefaults,
        string? relationshipType,
        string? packageRelationshipType = null)
    {
        var existingIndex = document.Preserved.Parts.FindIndex(p => p.PartName == partName);
        if (existingIndex >= 0)
        {
            var existing = document.Preserved.Parts[existingIndex];
            if (packageRelationshipType is not null && existing.PackageRelationshipType is null)
                document.Preserved.Parts[existingIndex] = existing with { PackageRelationshipType = packageRelationshipType };
            return true;
        }
        var bytes = LoadMedia(archive, partName.TrimStart('/'));
        if (bytes is null)
            return false;
        contentTypeOverrides.TryGetValue(partName, out var contentType);
        document.Preserved.Parts.Add(new PreservedPart(partName, bytes, contentType, relationshipType, packageRelationshipType));

        // A part covered only by a [Content_Types] Default (by extension — e.g. a chart's png/emf media) needs
        // that Default re-emitted, since FreeW only declares image Defaults for body/header/footer/comment media.
        if (contentType is null)
        {
            var extension = partName.Contains('.') ? partName[(partName.LastIndexOf('.') + 1)..] : null;
            if (!string.IsNullOrEmpty(extension)
                && contentTypeDefaults.TryGetValue(extension, out var defaultType)
                && !document.Preserved.ContentTypeDefaults.ContainsKey(extension))
                document.Preserved.ContentTypeDefaults[extension] = defaultType;
        }
        return true;
    }

    private static bool IsWriterOwnedPackageRelationship(string relationshipType) =>
        relationshipType is
            "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument"
            or OpcPackageProperties.CorePropertiesRelationshipType
            or OpcPackageProperties.CustomPropertiesRelationshipType
            or OpcPackageProperties.ExtendedPropertiesRelationshipType;

    /// <summary>
    /// Follows a part's own <c>_rels</c> (e.g. <c>word/charts/_rels/chart1.xml.rels</c>), capturing the rels
    /// part itself plus every part it targets (media, colour/style parts, embedded workbooks, …) into
    /// <see cref="TextDocument.Preserved"/>. Those satellite parts are NOT document-referenced (their rels live in
    /// the chart part's own _rels, captured verbatim), so they carry no document relationship type. Recurses one
    /// level into any captured part's own _rels so a chart→media→(no further rels) chain is fully preserved.
    /// </summary>
    private static void CaptureReferencedParts(
        ZipArchive archive,
        TextDocument document,
        string partName,
        IReadOnlyDictionary<string, string> contentTypeOverrides,
        IReadOnlyDictionary<string, string> contentTypeDefaults)
    {
        var relsPartName = OpcPathHelper.GetRelationshipPartName(partName);
        var relationships = OpcRelationships.Load(archive, relsPartName.TrimStart('/'));
        if (relationships.Count == 0)
            return;

        // The part's own _rels is itself preserved (covered by the rels Default content type, so no Override).
        CapturePreservedPart(archive, document, relsPartName, contentTypeOverrides, contentTypeDefaults, relationshipType: null);

        var baseFolder = OpcPathHelper.GetPartDirectoryName(partName);
        foreach (var rel in relationships)
        {
            // External targets (TargetMode="External") have no package part to capture.
            if (rel.IsExternal)
                continue;
            if (string.IsNullOrEmpty(rel.Target))
                continue;
            var targetPartName = OpcPathHelper.ResolveAbsolutePartName(baseFolder, rel.Target);
            if (targetPartName is null || document.Preserved.Parts.Any(p => p.PartName == targetPartName))
                continue;
            if (CapturePreservedPart(archive, document, targetPartName, contentTypeOverrides, contentTypeDefaults, relationshipType: null))
                CaptureReferencedParts(archive, document, targetPartName, contentTypeOverrides, contentTypeDefaults);
        }
    }

    /// <summary>
    /// Reads <c>[Content_Types].xml</c>, mapping each Default's Extension (lower-cased) → ContentType. Used to
    /// re-emit the Default a verbatim-preserved part relies on (e.g. a chart media part's png/emf Default).
    /// </summary>
    private static Dictionary<string, string> ReadContentTypeDefaults(ZipArchive archive)
        => OpcMediaTypes.ReadDefaultContentTypes(archive);

    /// <summary>Reads document.xml.rels as id → (Type, Target). Empty when the rels part is absent.</summary>
    private static Dictionary<string, (string Type, string Target)> ReadDocumentRelationships(ZipArchive archive) =>
        OpcRelationships.LoadById(archive, "word/_rels/document.xml.rels")
            .ToDictionary(
                relationship => relationship.Key,
                relationship => (Type: relationship.Value.Type, Target: relationship.Value.Target),
                StringComparer.Ordinal);

    /// <summary>
    /// Finds the plot area's single chart-type element and maps it to a <see cref="ChartKind"/>:
    /// c:barChart → Column/Bar (by c:barDir), c:lineChart → Line, c:areaChart → Area, c:pieChart → Pie,
    /// c:doughnutChart → Doughnut, c:scatterChart → Scatter. Returns (null, Column) when none is present.
    /// </summary>
    private static (XElement? Element, ChartKind Kind) ResolveChartType(XElement plotArea)
    {
        if (plotArea.Element(C + "barChart") is { } bar)
        {
            var dir = bar.Element(C + "barDir")?.Attribute("val")?.Value;
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
            var position = axis.Element(C + "axPos")?.Attribute("val")?.Value;
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
        var drawing = run.Element(W + "drawing");
        var inline = drawing?.Element(Wp + "inline");
        var anchor = drawing?.Element(Wp + "anchor");
        var container = inline ?? anchor;
        if (container is null)
            return null;
        var isWordSuppressedByDuplicateDrawingId = container.Annotation<DuplicateDrawingIdentityMarker>() is not null;

        var relIds = container.Descendants(Dgm + "relIds").FirstOrDefault();
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

        // Index semantic node points by modelId. Word also emits parTrans/sibTrans points in ptLst;
        // those are layout bookkeeping and must not become authored SmartArt nodes on read.
        var textById = new Dictionary<string, string>(StringComparer.Ordinal);
        var nodeById = new Dictionary<string, SmartArtNode>(StringComparer.Ordinal);
        var orderedIds = new List<string>();
        foreach (var pt in ptLst.Elements(Dgm + "pt"))
        {
            var type = pt.Attribute("type")?.Value;
            if (type is "doc" or "pres" or "parTrans" or "sibTrans")
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
            var connectionType = cxn.Attribute("type")?.Value;
            if (connectionType is not null && connectionType is not "parOf")
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

        var kind = ReadSmartArtKind(relIds, relationships, archive);
        var smartArt = new SmartArt
        {
            Kind = kind,
            IsWordSuppressedByDuplicateDrawingId = isWordSuppressedByDuplicateDrawingId
        };
        ReadSmartArtGalleryIds(relIds, relationships, archive, smartArt);
        // Word's flat List/Process galleries may use a presentation scaffold that represents the visual
        // flow as a parent chain. The authored model remains a flat node list, so recover that shape here;
        // hierarchy diagrams keep their semantic tree intact.
        if (kind is SmartArtKind.List or SmartArtKind.Process)
        {
            var flattened = new List<SmartArtNode>();
            void Flatten(IEnumerable<SmartArtNode> nodes)
            {
                foreach (var node in nodes)
                {
                    flattened.Add(new SmartArtNode(node.Text));
                    Flatten(node.Children);
                }
            }

            Flatten(topLevel);
            smartArt.Nodes.AddRange(flattened);
        }
        else
        {
            smartArt.Nodes.AddRange(topLevel);
        }

        // Size: the inline or anchor extent (EMU) maps back to points.
        var extent = container.Element(Wp + "extent");
        smartArt.WidthPt = EmuToPoints(extent?.Attribute("cx")?.Value);
        smartArt.HeightPt = EmuToPoints(extent?.Attribute("cy")?.Value);

        if (anchor is not null)
        {
            smartArt.Placement = new FloatingPlacement();
            ApplyFloatingPlacement(anchor, smartArt.Placement);
        }

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
        if (uniqueId.Contains("hierarchy", StringComparison.OrdinalIgnoreCase)
            || uniqueId.Contains("orgChart", StringComparison.OrdinalIgnoreCase))
            return SmartArtKind.Hierarchy;
        return SmartArtKind.List;
    }

    /// <summary>
    /// Recovers the FreeW gallery preset ids (LayoutId / ColorSchemeId / StyleId) from the three diagram
    /// parts (layout / colors / quickStyle). FreeW stores color and style IDs in a schema-valid extension list;
    /// legacy root attributes remain readable. Otherwise the uniqueId suffix is used as a best-effort fallback.
    /// </summary>
    private static void ReadSmartArtGalleryIds(
        XElement? relIds,
        IReadOnlyDictionary<string, string> relationships,
        ZipArchive archive,
        SmartArt target)
    {
        // Layout id ─────────────────────────────────────────────────────────────────────────────────
        var layoutRelId = relIds?.Attribute(R + "lo")?.Value;
        if (layoutRelId is not null && relationships.TryGetValue(layoutRelId, out var layoutPath))
        {
            var layoutRoot = LoadPart(archive, layoutPath)?.Root;
            if (layoutRoot is not null)
            {
                // Prefer the FreeW extension attribute; fall back to the uniqueId suffix.
                var freewId = layoutRoot.Attribute("freewLayoutId")?.Value;
                if (freewId is not null)
                    target.LayoutId = freewId;
                else
                {
                    var uniqueId = layoutRoot.Attribute("uniqueId")?.Value ?? string.Empty;
                    var lastSlash = uniqueId.LastIndexOf('/');
                    if (lastSlash >= 0 && lastSlash < uniqueId.Length - 1)
                        target.LayoutId = uniqueId[(lastSlash + 1)..];
                }
            }
        }

        // QuickStyle id ─────────────────────────────────────────────────────────────────────────────
        var qsRelId = relIds?.Attribute(R + "qs")?.Value;
        if (qsRelId is not null && relationships.TryGetValue(qsRelId, out var qsPath))
        {
            var qsRoot = LoadPart(archive, qsPath)?.Root;
            if (qsRoot is not null)
            {
                var freewId = ReadFreeWSmartArtGalleryId(qsRoot, "style")
                    ?? qsRoot.Attribute("freewStyleId")?.Value;
                if (freewId is not null)
                    target.StyleId = freewId;
                else
                {
                    var uniqueId = qsRoot.Attribute("uniqueId")?.Value ?? string.Empty;
                    var lastSlash = uniqueId.LastIndexOf('/');
                    if (lastSlash >= 0 && lastSlash < uniqueId.Length - 1)
                        target.StyleId = uniqueId[(lastSlash + 1)..];
                }
            }
        }

        // Colors id ─────────────────────────────────────────────────────────────────────────────────
        var csRelId = relIds?.Attribute(R + "cs")?.Value;
        if (csRelId is not null && relationships.TryGetValue(csRelId, out var csPath))
        {
            var csRoot = LoadPart(archive, csPath)?.Root;
            if (csRoot is not null)
            {
                var freewId = ReadFreeWSmartArtGalleryId(csRoot, "colorScheme")
                    ?? csRoot.Attribute("freewColorId")?.Value;
                if (freewId is not null)
                    target.ColorSchemeId = freewId;
                else
                {
                    var uniqueId = csRoot.Attribute("uniqueId")?.Value ?? string.Empty;
                    var lastSlash = uniqueId.LastIndexOf('/');
                    if (lastSlash >= 0 && lastSlash < uniqueId.Length - 1)
                        target.ColorSchemeId = uniqueId[(lastSlash + 1)..];
                }
            }
        }

        // Word's native package may omit the optional quickStyle/colors relIds. FreeW records those
        // catalog ids in the document point's prSet, so recover them from the data part in that form.
        if (target.StyleId is null || target.ColorSchemeId is null)
        {
            var dataRelId = relIds?.Attribute(R + "dm")?.Value;
            if (dataRelId is not null && relationships.TryGetValue(dataRelId, out var dataPath))
            {
                var docPoint = LoadPart(archive, dataPath)?.Root?
                    .Element(Dgm + "ptLst")?.Elements(Dgm + "pt")
                    .FirstOrDefault(pt => pt.Attribute("type")?.Value == "doc");
                var prSet = docPoint?.Element(Dgm + "prSet");
                if (target.StyleId is null)
                    target.StyleId = GallerySuffix(prSet?.Attribute("qsTypeId")?.Value);
                if (target.ColorSchemeId is null)
                    target.ColorSchemeId = GallerySuffix(prSet?.Attribute("csTypeId")?.Value);
            }
        }
    }

    private static string? GallerySuffix(string? uniqueId)
    {
        if (string.IsNullOrEmpty(uniqueId))
            return null;
        var lastSlash = uniqueId.LastIndexOf('/');
        return lastSlash >= 0 && lastSlash < uniqueId.Length - 1
            ? uniqueId[(lastSlash + 1)..]
            : null;
    }

    private static string? ReadFreeWSmartArtGalleryId(XElement root, string elementName)
    {
        XNamespace freew = "http://schemas.freew.dev/smartart-gallery/2026";
        return root.Element(Dgm + "extLst")?
            .Elements(A + "ext")
            .Elements(freew + elementName)
            .Select(element => element.Attribute("id")?.Value)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
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
                Idx: int.TryParse(pt.Attribute("idx")?.Value, out var idx) ? idx : 0,
                Value: pt.Element(C + "v")?.Value ?? string.Empty))
            .OrderBy(p => p.Idx);
    }

    /// <summary>Maps relationship id -> media part path from word/_rels/document.xml.rels.</summary>
    private static Dictionary<string, string> ReadImageRelationships(ZipArchive archive) =>
        OpcRelationships.LoadTargetMap(
            archive,
            "word/_rels/document.xml.rels",
            relationship => relationship.IsExternal
                ? relationship.Target
                : "word/" + relationship.Target.TrimStart('/'));

    /// <summary>
    /// Maps relationship id → archive entry path for a satellite part's own <c>_rels</c> (e.g.
    /// <c>word/_rels/comments.xml.rels</c>), resolving each Target relative to <paramref name="baseFolder"/>
    /// (the folder the relationships are relative to, e.g. <c>word/</c>). External image targets are retained
    /// verbatim for DrawingML <c>r:link</c>. Returns
    /// an empty map when the rels part is absent — so a comments part with no image relationships behaves exactly
    /// as before. Mirrors <see cref="ReadImageRelationships"/> for non-document parts.
    /// </summary>
    private static Dictionary<string, string> ReadPartImageRelationships(ZipArchive archive, string relsPath, string baseFolder) =>
        OpcRelationships.LoadTargetMap(
            archive,
            relsPath,
            relationship => relationship.IsExternal
                ? relationship.Target
                : OpcPathHelper.ResolveAbsolutePartName(
                    "/" + baseFolder.Trim('/'),
                    relationship.Target)?.TrimStart('/'),
            relationship => relationship.Type.EndsWith("/image", StringComparison.Ordinal));

    /// <summary>Maps relationship id -> external hyperlink target (URL) from document.xml.rels.</summary>
    private static Dictionary<string, string> ReadHyperlinkRelationships(ZipArchive archive) =>
        OpcRelationships.LoadTargetMap(
            archive,
            "word/_rels/document.xml.rels",
            relationship => relationship.Target,
            relationship => relationship.Type.EndsWith("/hyperlink", StringComparison.Ordinal));

    /// <summary>
    /// Maps a conforming master-document anchor relationship id to its exact external target. Word requires
    /// subdocument relationships to use the dedicated type and <c>TargetMode="External"</c>; package-local
    /// or differently typed relationships are deliberately not promoted into the editable model.
    /// </summary>
    private static Dictionary<string, string> ReadSubDocumentRelationships(ZipArchive archive) =>
        OpcRelationships.LoadTargetMap(
            archive,
            "word/_rels/document.xml.rels",
            relationship => relationship.Target,
            relationship => relationship.IsExternal && relationship.Type == SubDocumentRelType);

    /// <summary>Maps a body <c>w:altChunk/@r:id</c> to its package-local source payload.</summary>
    private static Dictionary<string, string> ReadAltChunkRelationships(ZipArchive archive) =>
        OpcRelationships.LoadTargetMap(
            archive,
            "word/_rels/document.xml.rels",
            relationship => "/" + OpcPathHelper.ResolveRelativeZipPath("word", relationship.Target),
            relationship =>
                !relationship.IsExternal &&
                relationship.Type == AltChunkRelType);

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

    /// <summary>
    /// Resolves an image's <see cref="ImageFormat"/> from its media-part path and bytes. The part name (the
    /// relationship target) carries the real extension, so it is preferred; when the extension is unknown or
    /// absent the bytes' magic number is used (see <see cref="InlineImage.DetectFormat"/>), which also gives
    /// a usable default for empty data.
    /// </summary>
    private static ImageFormat ResolveImageFormat(string partPath, byte[] bytes)
    {
        var dot = partPath.LastIndexOf('.');
        var ext = dot >= 0 ? partPath[(dot + 1)..] : null;
        return InlineImage.FormatForExtension(ext) ?? InlineImage.DetectFormat(bytes);
    }

    /// <summary>
    /// Reads the document default paragraph spacing from <c>w:docDefaults/w:pPrDefault/w:pPr</c> (space
    /// before/after + line spacing). Omitted components fall back to no extra space / 1.15 multiple —
    /// FreeW's prior behaviour for documents without docDefaults.
    /// </summary>
    private static ParagraphFormatting ReadDocDefaultParagraph(XElement ddPr)
    {
        var spacing = ddPr.Element(W + "spacing");
        var contextualSpacingElement = ddPr.Element(W + "contextualSpacing");
        var contextualSpacing = contextualSpacingElement is null
            ? (bool?)null
            : ReadToggle(ddPr, "contextualSpacing");
        var beforeAuto = spacing?.Attribute(W + "beforeAutospacing")?.Value is "1" or "true" or "on";
        var afterAuto = spacing?.Attribute(W + "afterAutospacing")?.Value is "1" or "true" or "on";
        const double autoSpacingPt = 14.0;
        var before = beforeAuto ? autoSpacingPt : spacing?.Attribute(W + "before") is { } b ? DxaToPoints(b.Value) : 0.0;
        var after = afterAuto ? autoSpacingPt : spacing?.Attribute(W + "after") is { } a ? DxaToPoints(a.Value) : 0.0;
        var rule = LineSpacingRule.Multiple;
        var ls = ParagraphFormatting.Default.LineSpacing;
        var lh = 0.0;
        var lineVal = spacing?.Attribute(W + "line")?.Value;
        if (lineVal is not null && double.TryParse(lineVal, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var lineRaw))
        {
            switch (spacing?.Attribute(W + "lineRule")?.Value)
            {
                case "exact": rule = LineSpacingRule.Exact; lh = lineRaw / 20.0; break;
                case "atLeast": rule = LineSpacingRule.AtLeast; lh = lineRaw / 20.0; break;
                default: rule = LineSpacingRule.Multiple; ls = lineRaw / 240.0; break;
            }
        }
        return ParagraphFormatting.Default with
        {
            SpaceBeforePt = before,
            SpaceAfterPt = after,
            BeforeAutoSpacing = beforeAuto,
            AfterAutoSpacing = afterAuto,
            ContextualSpacing = contextualSpacing,
            LineRule = rule,
            LineSpacing = ls,
            LineHeightPt = lh,
            LineSpacingIsSet = lineVal is not null,
        };
    }

    internal static ParagraphFormatting ReadParagraphFormatting(XElement pPr) =>
        ReadParagraphFormatting(pPr, new Dictionary<int, ListKind>());

    internal static ParagraphFormatting ReadParagraphFormatting(XElement pPr, IReadOnlyDictionary<int, ListKind> numbering)
        => ReadParagraphFormatting(pPr, numbering, null);

    /// <summary>
    /// Reads paragraph formatting. <paramref name="docDefaults"/> (the document's w:docDefaults paragraph
    /// spacing) supplies space-before/after and line spacing for properties the paragraph does not set;
    /// null falls back to no extra space / 1.15 line (used for style definitions and header/footer/note
    /// paragraphs).
    /// </summary>
    internal static ParagraphFormatting ReadParagraphFormatting(
        XElement pPr,
        IReadOnlyDictionary<int, ListKind> numbering,
        ParagraphFormatting? docDefaults,
        IReadOnlyDictionary<(int NumId, int Level), int>? startOverrides = null)
    {
        var spacing = pPr.Element(W + "spacing");
        var indent = pPr.Element(W + "ind");
        var jc = pPr.Element(W + "jc")?.Attribute(W + "val")?.Value;
        var shd = pPr.Element(W + "shd");
        var shading = shd?.Attribute(W + "fill")?.Value;
        var shadingPattern = ShadingPatterns.FromToken(shd?.Attribute(W + "val")?.Value);

        // A list paragraph references a numbering definition via pPr/w:numPr (w:numId + w:ilvl).
        // Resolve the numId to a ListKind through numbering.xml; the ilvl becomes the ListLevel.
        var listKind = ListKind.None;
        var listLevel = 0;
        int? listStartOverride = null;
        var numPr = pPr.Element(W + "numPr");
        if (numPr is not null)
        {
            var numId = ParseInt(numPr.Element(W + "numId")?.Attribute(W + "val")?.Value);
            if (numbering.TryGetValue(numId, out var kind) && kind != ListKind.None)
            {
                listKind = kind;
                listLevel = ParseInt(numPr.Element(W + "ilvl")?.Attribute(W + "val")?.Value);
                if (startOverrides is not null && startOverrides.TryGetValue((numId, listLevel), out var startAt))
                    listStartOverride = startAt;
            }
        }

        // w:pageBreakBefore is a toggle: present (and not val="false"/"0") means a page break is forced.
        var pageBreakBefore = ReadToggle(pPr, "pageBreakBefore");
        // Flow control toggles read the same way as pageBreakBefore. Preserve presence for widowControl:
        // Word treats an absent token as enabled, while an explicit val="0" remains disabled.
        var keepWithNext = ReadToggle(pPr, "keepNext");
        var keepLinesTogether = ReadToggle(pPr, "keepLines");
        var contextualSpacingElement = pPr.Element(W + "contextualSpacing");
        var contextualSpacing = contextualSpacingElement is null
            ? (bool?)null
            : ReadToggle(pPr, "contextualSpacing");
        var widowControl = ReadToggle(pPr, "widowControl");
        var widowControlIsSet = pPr.Element(W + "widowControl") is not null;
        // Suppress automatic hyphenation for this paragraph (w:suppressAutoHyphens), read as a toggle.
        var suppressAutoHyphens = ReadToggle(pPr, "suppressAutoHyphens");
        // Suppress line-number glyphs for this paragraph while retaining its sequence position.
        // Keep presence separately so an explicit val="0" can override a suppressing paragraph style.
        var suppressLineNumbers = ReadToggle(pPr, "suppressLineNumbers");
        var suppressLineNumbersIsSet = pPr.Element(W + "suppressLineNumbers") is not null;
        // Right-to-left paragraph direction (w:bidi), read as a toggle like the flow-control flags.
        var rtl = ReadToggle(pPr, "bidi");

        // Line spacing (w:spacing/@w:line + @w:lineRule). When w:line is absent the paragraph inherits the
        // document default (w:docDefaults), falling back to FreeW's 1.15 multiple when there is none.
        var lineRuleAttr = spacing?.Attribute(W + "lineRule")?.Value;
        var lineVal = spacing?.Attribute(W + "line")?.Value;
        var lineRule = docDefaults?.LineRule ?? ParagraphFormatting.Default.LineRule;
        var lineSpacing = docDefaults?.LineSpacing ?? ParagraphFormatting.Default.LineSpacing;
        var lineHeightPt = docDefaults?.LineHeightPt ?? 0.0;
        if (lineVal is not null && double.TryParse(lineVal, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var lineRaw))
        {
            switch (lineRuleAttr)
            {
                case "exact":
                    lineRule = LineSpacingRule.Exact;
                    lineHeightPt = lineRaw / 20.0; // twentieths of a point -> points
                    break;
                case "atLeast":
                    lineRule = LineSpacingRule.AtLeast;
                    lineHeightPt = lineRaw / 20.0;
                    break;
                default: // "auto" or absent -> a multiple, value in 240ths of a line
                    lineRule = LineSpacingRule.Multiple;
                    lineSpacing = lineRaw / 240.0;
                    break;
            }
        }

        // w:beforeAutospacing / w:afterAutospacing (ubiquitous in HTML-paste / web content): Word ignores
        // the explicit before/after value and applies automatic spacing of about one line. FreeW used to
        // take the literal value (often a tiny 100 dxa = 5pt), packing such paragraphs too tightly. Use an
        // auto approximation; else the paragraph's own value; else the document default.
        const double autoSpacingPt = 14.0;
        var beforeAuto = spacing?.Attribute(W + "beforeAutospacing")?.Value is "1" or "true" or "on";
        var afterAuto = spacing?.Attribute(W + "afterAutospacing")?.Value is "1" or "true" or "on";
        var spaceBeforePt = beforeAuto
            ? autoSpacingPt
            : spacing?.Attribute(W + "before") is { } sbAttr ? DxaToPoints(sbAttr.Value) : docDefaults?.SpaceBeforePt ?? 0;
        var spaceAfterPt = afterAuto
            ? autoSpacingPt
            : spacing?.Attribute(W + "after") is { } saAttr ? DxaToPoints(saAttr.Value) : docDefaults?.SpaceAfterPt ?? 0;

        return ParagraphFormatting.Default with
        {
            Border = ReadParagraphBorder(pPr.Element(W + "pBdr")),
            PageBreakBefore = pageBreakBefore,
            KeepWithNext = keepWithNext,
            KeepLinesTogether = keepLinesTogether,
            WidowControl = widowControl,
            WidowControlIsSet = widowControlIsSet,
            SuppressAutoHyphens = suppressAutoHyphens,
            SuppressLineNumbers = suppressLineNumbers,
            SuppressLineNumbersIsSet = suppressLineNumbersIsSet,
            Rtl = rtl,
            LineRule = lineRule,
            LineSpacing = lineSpacing,
            LineHeightPt = lineHeightPt,
            // Explicit only when this pPr carries its own w:line — an inherited docDefault value (baked above)
            // leaves it unset so the render cascade can prefer the paragraph's style instead.
            LineSpacingIsSet = lineVal is not null,
            SpaceBeforePt = spaceBeforePt,
            SpaceAfterPt = spaceAfterPt,
            BeforeAutoSpacing = beforeAuto,
            AfterAutoSpacing = afterAuto,
            ContextualSpacing = contextualSpacing,
            // As for line spacing: explicit only when this pPr sets its own before/after (or an autospacing
            // toggle). Otherwise the render cascade inherits the paragraph's style rather than 0/docDefault.
            SpaceBeforeIsSet = beforeAuto || spacing?.Attribute(W + "before") is not null,
            SpaceAfterIsSet = afterAuto || spacing?.Attribute(W + "after") is not null,
            ShadingColorHex = shading is null or "auto" ? null : "#" + shading.TrimStart('#'),
            ShadingPattern = shadingPattern,
            Alignment = jc switch
            {
                "center" => TextAlignment.Center,
                "right" or "end" => TextAlignment.Right,
                "both" or "justify" => TextAlignment.Justify,
                _ => TextAlignment.Left
            },
            IndentLeftPt = DxaToPoints(indent?.Attribute(W + "left")?.Value ?? indent?.Attribute(W + "start")?.Value),
            IndentRightPt = DxaToPoints(indent?.Attribute(W + "right")?.Value ?? indent?.Attribute(W + "end")?.Value),
            // w:hanging (a positive twips value) is mutually exclusive with w:firstLine in OOXML; the model
            // represents a hanging indent as a NEGATIVE FirstLineIndentPt so callers can distinguish the two.
            FirstLineIndentPt = indent?.Attribute(W + "hanging") is { } hangAttr
                ? -DxaToPoints(hangAttr.Value)
                : DxaToPoints(indent?.Attribute(W + "firstLine")?.Value),
            ListKind = listKind,
            ListLevel = listLevel,
            ListStartOverride = listStartOverride,
            TabStops = ReadTabStops(pPr.Element(W + "tabs"))
        };
    }

    /// <summary>
    /// Reads paragraph tab stops (w:tabs) into the model list, one <see cref="TabStop"/> per w:tab.
    /// Positions come from w:pos (dxa -> points); the alignment from w:val; the optional leader fill
    /// from w:leader (absent -> <see cref="TabLeader.None"/>). A "clear" operation retains its position
    /// and removes an inherited stop at that position. Returns an empty list if absent.
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
            {
                stops.Add(new TabStop(
                    DxaToPoints(tab.Attribute(W + "pos")?.Value),
                    IsClear: true));
                continue;
            }
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
        var lineStyle = BorderLineStyles.FromToken(edge.Attribute(W + "val")?.Value);
        bool Drawn(string name) =>
            (pBdr.Element(W + name)?.Attribute(W + "val")?.Value ?? "none") is not ("none" or "nil");
        var top = Drawn("top");
        var left = Drawn("left");
        var bottom = Drawn("bottom");
        var right = Drawn("right");

        // A bottom-only rule: the only drawn edge is w:bottom (top/left/right absent or off). This is how
        // CreateHorizontalRule writes itself; recovering the flag keeps the round-trip lossless.
        var bottomOnly = bottom && !top && !left && !right;

        return new ParagraphBorder(
            color is null or "auto" ? "#000000" : "#" + color.TrimStart('#'),
            width > 0 ? width : 0.5,
            bottomOnly)
        {
            LineStyle = lineStyle,
            Top = top,
            Left = left,
            Bottom = bottom,
            Right = right,
        };
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
        var styleToken = edge.Attribute(W + "val")?.Value;
        var hasCanonicalArt = PageBorderArtStyles.TryGetByToken(styleToken, out var artStyle);
        var lineStyle = hasCanonicalArt
            ? BorderLineStyle.Single
            : BorderLineStyles.FromToken(styleToken);
        var space = double.TryParse(edge.Attribute(W + "space")?.Value,
            System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture, out var parsedSpace)
            ? parsedSpace
            : 24.0;
        var offsetFrom = string.Equals(pgBorders.Attribute(W + "offsetFrom")?.Value, "text",
            StringComparison.OrdinalIgnoreCase)
            ? PageBorderOffsetFrom.Text
            : PageBorderOffsetFrom.Page;
        var display = pgBorders.Attribute(W + "display")?.Value switch
        {
            "firstPage" => PageBorderDisplay.FirstPage,
            "notFirstPage" => PageBorderDisplay.NotFirstPage,
            _ => PageBorderDisplay.AllPages,
        };
        var zOrder = string.Equals(pgBorders.Attribute(W + "zOrder")?.Value, "behind",
            StringComparison.OrdinalIgnoreCase)
            ? PageBorderZOrder.Behind
            : PageBorderZOrder.Front;

        // Older FreeW packages used a non-schema @w:art attribute. Read it as a compatibility fallback,
        // while canonical WordprocessingML stores the decorative design directly in @w:val.
        var artStr = edge.Attribute(W + "art")?.Value;
        var legacyArtId = int.TryParse(artStr, System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;
        var artId = hasCanonicalArt ? artStyle.ArtId : legacyArtId;

        return new PageBorder(
            color is null or "auto" ? "#000000" : "#" + color.TrimStart('#'),
            width > 0 ? width : 1.0)
        {
            OffsetFrom = offsetFrom,
            SpacePt = Math.Max(0, space),
            LineStyle = lineStyle,
            ArtId = artId,
            Display = display,
            ZOrder = zOrder,
        };
    }

    /// <summary>
    /// Reads line numbering (w:lnNumType) into <paramref name="page"/>. Absent leaves the default
    /// (<see cref="LineNumberMode.None"/>). @w:restart maps newPage/newSection to their corresponding
    /// restart modes; anything else (including the default "continuous") maps to Continuous.
    /// </summary>
    private static void ReadLineNumbering(XElement? lnNumType, PageSettings page)
    {
        if (lnNumType is null)
            return;

        page.LineNumberMode = lnNumType.Attribute(W + "restart")?.Value switch
        {
            "newPage" => LineNumberMode.RestartEachPage,
            "newSection" => LineNumberMode.RestartEachSection,
            _ => LineNumberMode.Continuous,
        };

        if (int.TryParse(lnNumType.Attribute(W + "countBy")?.Value, out var countBy) && countBy >= 1)
            page.LineNumberCountBy = countBy;

        if (int.TryParse(lnNumType.Attribute(W + "start")?.Value, out var startAt) && startAt >= 1)
            page.LineNumberStartAt = startAt;
    }

    private static void ReadPageNumbering(XElement? pgNumType, PageSettings page)
    {
        if (pgNumType is null)
            return;

        page.PageNumberFormat = PageNumberFormatFromToken(pgNumType.Attribute(W + "fmt")?.Value);
        page.PageNumberStartAt = int.TryParse(pgNumType.Attribute(W + "start")?.Value, out var startAt)
            && startAt >= 1
                ? startAt
                : null;
        page.PageNumberChapterStyleLevel = int.TryParse(pgNumType.Attribute(W + "chapStyle")?.Value, out var chapterStyle)
            && chapterStyle is >= 1 and <= 9
                ? chapterStyle
                : null;
        page.PageNumberChapterSeparator = PageNumberChapterSeparatorFromToken(
            pgNumType.Attribute(W + "chapSep")?.Value);
    }

    private static PageNumberFormat PageNumberFormatFromToken(string? token) => token switch
    {
        "lowerRoman" => PageNumberFormat.LowerRoman,
        "upperRoman" => PageNumberFormat.UpperRoman,
        "lowerLetter" => PageNumberFormat.LowerLetter,
        "upperLetter" => PageNumberFormat.UpperLetter,
        _ => PageNumberFormat.Decimal
    };

    private static PageNumberChapterSeparator PageNumberChapterSeparatorFromToken(string? token) => token switch
    {
        "period" => PageNumberChapterSeparator.Period,
        "colon" => PageNumberChapterSeparator.Colon,
        "emDash" => PageNumberChapterSeparator.EmDash,
        "enDash" => PageNumberChapterSeparator.EnDash,
        _ => PageNumberChapterSeparator.Hyphen
    };

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
    /// <summary>
    /// Recovers a paragraph's original <c>w:numPr</c> (numId + ilvl) as a <see cref="PreservedNumbering"/>,
    /// or null when the paragraph has no <c>w:numPr</c> (so a non-list paragraph preserves nothing). The ilvl
    /// defaults to 0 when absent, matching how Word treats a level-less numPr.
    /// </summary>
    private static PreservedNumbering? ReadPreservedNumbering(XElement pPr)
    {
        var numPr = pPr.Element(W + "numPr");
        if (numPr is null)
            return null;
        var numId = ParseInt(numPr.Element(W + "numId")?.Attribute(W + "val")?.Value);
        var ilvl = ParseInt(numPr.Element(W + "ilvl")?.Attribute(W + "val")?.Value);
        return new PreservedNumbering(numId, ilvl);
    }

    private static (Dictionary<int, ListKind> KindByNumId, Dictionary<(int NumId, int Level), int> StartOverrideByNumIdLevel) ReadNumbering(
        ZipArchive archive, TextDocument document)
    {
        var map = new Dictionary<int, ListKind>();
        var startOverrides = new Dictionary<(int, int), int>();
        var numberingXml = LoadPart(archive, "word/numbering.xml");
        var root = numberingXml?.Root;
        if (root is null)
            return (map, startOverrides);

        // Preserve the ORIGINAL numbering element so the writer can merge its definitions alongside FreeW's
        // own (under a disjoint numId range) and re-emit the paragraphs' w:numPr that FreeW does not model.
        // Cloned so later edits can't leak back. A document with no numbering part preserves nothing here.
        document.Preserved.OriginalNumbering = new XElement(root);

        // abstractNumId -> ListKind, taken from the format of its lowest level.
        var abstractKinds = new Dictionary<int, ListKind>();
        var abstractMultiLevelFormats = new Dictionary<int, IReadOnlyList<ListNumberFormat>>();
        foreach (var abstractNum in root.Elements(W + "abstractNum"))
        {
            var abstractNumId = ParseInt(abstractNum.Attribute(W + "abstractNumId")?.Value);
            var levels = abstractNum.Elements(W + "lvl")
                .OrderBy(l => ParseInt(l.Attribute(W + "ilvl")?.Value))
                .ToList();
            // Evaluate IsMultiLevel before inspecting level-0's numFmt: a "fancy" multilevel template
            // whose level-0 happens to be a bullet (e.g. Word's List Bullet Multilevel style) is
            // MultiLevel, not Bullet — the deeper levels carry decimal/letter formats that the model
            // must expose for correct display/editing of sub-levels.
            var numFmt = levels.FirstOrDefault()?.Element(W + "numFmt")?.Attribute(W + "val")?.Value;
            var isMultiLevel = IsMultiLevel(abstractNum, levels);
            if (isMultiLevel)
                abstractMultiLevelFormats[abstractNumId] = ReadMultiLevelNumberFormats(levels);
            abstractKinds[abstractNumId] = isMultiLevel
                ? ListKind.MultiLevel
                : numFmt == "bullet" ? ListKind.Bullet : ListKind.Number;
        }

        var appliedMultiLevelFormats = false;
        foreach (var num in root.Elements(W + "num"))
        {
            var numId = ParseInt(num.Attribute(W + "numId")?.Value);
            var abstractNumId = ParseInt(num.Element(W + "abstractNumId")?.Attribute(W + "val")?.Value);
            if (abstractKinds.TryGetValue(abstractNumId, out var kind))
            {
                map[numId] = kind;
                if (!appliedMultiLevelFormats
                    && kind == ListKind.MultiLevel
                    && abstractMultiLevelFormats.TryGetValue(abstractNumId, out var numberFormats))
                {
                    document.MultiLevelList.SetNumberFormats(numberFormats);
                    appliedMultiLevelFormats = true;
                }
            }

            // Detect restart-override w:num elements: each w:lvlOverride/w:startOverride pair is a
            // counter-reset override for one list level, emitted by FreeW or by Word for the same purpose.
            foreach (var lvlOverride in num.Elements(W + "lvlOverride"))
            {
                var startOverrideEl = lvlOverride.Element(W + "startOverride");
                if (startOverrideEl is null)
                    continue;
                var level = ParseInt(lvlOverride.Attribute(W + "ilvl")?.Value);
                var startVal = ParseInt(startOverrideEl.Attribute(W + "val")?.Value);
                startOverrides[(numId, level)] = startVal;
            }
        }
        return (map, startOverrides);
    }

    /// <summary>
    /// Recognizes an outline/legal numbering definition: either it carries
    /// w:multiLevelType="multilevel" (as a child element per OOXML spec, or as an attribute in FreeW's own
    /// emitted format), or its level-1 lvlText accumulates the ancestor counters (it references both %1 and
    /// %2, as in "%1.%2."), which distinguishes it from a flat decimal list whose level-1 text is just "%2.".
    /// </summary>
    private static bool IsMultiLevel(XElement abstractNum, IReadOnlyList<XElement> levels)
    {
        // FreeW writes multiLevelType as an attribute on abstractNum; real Word XML emits it as a child
        // element <w:multiLevelType w:val="multilevel"/>. Check both forms.
        if (abstractNum.Attribute(W + "multiLevelType")?.Value == "multilevel")
            return true;
        if (abstractNum.Element(W + "multiLevelType")?.Attribute(W + "val")?.Value == "multilevel")
            return true;

        var level1Text = levels.ElementAtOrDefault(1)?.Element(W + "lvlText")?.Attribute(W + "val")?.Value;
        return level1Text is not null && level1Text.Contains("%1") && level1Text.Contains("%2");
    }

    private static IReadOnlyList<ListNumberFormat> ReadMultiLevelNumberFormats(IReadOnlyList<XElement> levels)
    {
        var formats = Enumerable.Repeat(ListNumberFormat.Decimal, MultiLevelListFormat.LevelCount).ToArray();
        foreach (var level in levels)
        {
            var index = ParseInt(level.Attribute(W + "ilvl")?.Value);
            if (index < 0 || index >= formats.Length)
                continue;

            formats[index] = MultiLevelListMarkerFormatter.FromOoxmlToken(
                level.Element(W + "numFmt")?.Attribute(W + "val")?.Value);
        }
        return formats;
    }

    /// <summary>
    /// Reads a tracked formatting change (w:rPrChange) from a run's <paramref name="rPr"/> and stamps it
    /// onto <paramref name="run"/> as a <see cref="FormatRevision"/>. The rPrChange carries the run's
    /// <em>previous</em> formatting in a nested w:rPr plus the w:author/w:date of the change. A run with no
    /// rPrChange is left untouched.
    /// </summary>
    private static void ApplyFormatRevision(Run run, XElement? rPr)
    {
        var rPrChange = rPr?.Element(W + "rPrChange");
        if (rPrChange is null)
            return;
        var previous = ReadRunFormatting(rPrChange.Element(W + "rPr"));
        var author = rPrChange.Attribute(W + "author")?.Value;
        var date = rPrChange.Attribute(W + "date")?.Value;
        run.FormatRevision = new FormatRevision(previous, author, date);
    }

    /// <summary>
    /// Reads a tracked paragraph-formatting change (w:pPrChange) from a paragraph's <paramref name="pPr"/>
    /// and stamps it onto <paramref name="paragraph"/> as a <see cref="ParagraphFormatRevision"/>. The
    /// pPrChange carries the paragraph's <em>previous</em> formatting in a nested w:pPr plus the
    /// w:author/w:date of the change. A paragraph with no pPrChange is left untouched. Mirrors
    /// <see cref="ApplyFormatRevision"/> for run-level changes.
    /// </summary>
    private static void ApplyParagraphFormatRevision(Paragraph paragraph, XElement? pPr)
    {
        var pPrChange = pPr?.Element(W + "pPrChange");
        if (pPrChange is null)
            return;
        // The nested w:pPr inside w:pPrChange holds the previous paragraph formatting. Parse it using
        // the same path as the current pPr, without numbering resolution (list state is not tracked
        // in pPrChange) and without document defaults (the previous snapshot is self-contained).
        var previousPPr = pPrChange.Element(W + "pPr");
        var previous = previousPPr is not null
            ? ReadParagraphFormatting(previousPPr)
            : ParagraphFormatting.Default;
        var author = pPrChange.Attribute(W + "author")?.Value;
        var date = pPrChange.Attribute(W + "date")?.Value;
        paragraph.ParagraphFormatRevision = new ParagraphFormatRevision(previous, author, date);
    }

    internal static RunFormatting ReadRunFormatting(XElement? rPr)
    {
        if (rPr is null)
            return RunFormatting.Default;

        var underline = rPr.Element(W + "u");
        var color = rPr.Element(W + "color")?.Attribute(W + "val")?.Value;
        var shdEl = rPr.Element(W + "shd");
        var highlight = shdEl?.Attribute(W + "fill")?.Value;
        var shdVal = shdEl?.Attribute(W + "val")?.Value;
        var vertAlign = rPr.Element(W + "vertAlign")?.Attribute(W + "val")?.Value;

        // w:highlight — Word's standard highlighter element, e.g. <w:highlight w:val="yellow"/>.
        // Takes precedence over w:shd when both are present, since w:highlight is the canonical Word marker.
        var highlightNamedToken = rPr.Element(W + "highlight")?.Attribute(W + "val")?.Value;

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

        // w:shd on a run: when val is "clear" (or absent, which defaults to clear) it is a solid highlight;
        // any other val token means a pattern-based character shading. Map the two cases to distinct fields.
        // Keep this WordprocessingML color boundary local: "auto", named w:highlight tokens, and nullable
        // model "#RRGGBB" fields are not the same contract as strict DrawingML srgbClr/theme helpers.
        var shdPattern = ShadingPatterns.FromToken(shdVal);
        var isSolidHighlight = shdVal is null or "clear";
        var fillHex = highlight is null or "auto" ? null : "#" + highlight.TrimStart('#');
        string? highlightHex = isSolidHighlight ? fillHex : null;
        string? charShadingHex = !isSolidHighlight ? fillHex : null;

        // w:highlight takes precedence over w:shd for the highlight field (but does not affect charShadingHex).
        if (highlightNamedToken is not null && highlightNamedToken != "none")
            highlightHex = HighlightTokenToHex(highlightNamedToken);

        // w:bdr (character border) — same edge encoding as w:pBdr. ReadParagraphBorder reuses the same
        // structure so we delegate to it directly.
        var charBorder = ReadParagraphBorder(rPr.Element(W + "bdr"));

        // w:lang (proofing language) — recover the val attribute (BCP-47 tag). @w:eastAsia and @w:bidi are
        // also written but the main @w:val is authoritative for spell-check; we use it as the canonical tag.
        var langTag = rPr.Element(W + "lang")?.Attribute(W + "val")?.Value;

        return new RunFormatting
        {
            Bold = ReadToggle(rPr, "b"),
            Italic = ReadToggle(rPr, "i"),
            Underline = underline is not null && (underline.Attribute(W + "val")?.Value ?? "single") != "none",
            Strikethrough = ReadToggle(rPr, "strike"),
            SmallCaps = ReadToggle(rPr, "smallCaps"),
            AllCaps = ReadToggle(rPr, "caps"),
            Rtl = ReadToggle(rPr, "rtl"),
            FontFamily = rPr.Element(W + "rFonts")?.Attribute(W + "ascii")?.Value,
            FontSizePt = HalfPointsToPoints(rPr.Element(W + "sz")?.Attribute(W + "val")?.Value),
            ColorHex = color is null or "auto" ? null : "#" + color.TrimStart('#'),
            HighlightColorHex = highlightHex,
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
                : int.TryParse(styleSetId, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var id) ? id : null,
            CharacterBorder = charBorder,
            CharacterShadingHex = charShadingHex,
            CharacterShadingPattern = charShadingHex is not null ? shdPattern : ShadingPattern.Clear,
            LanguageTag = string.IsNullOrEmpty(langTag) ? null : langTag,
        };
    }

    /// <summary>
    /// Reads FreeW custom document properties from docProps/custom.xml: the page watermark into
    /// <see cref="PageSettings.WatermarkOptions"/> (or the legacy <see cref="PageSettings.Watermark"/>
    /// string), and Word's "Mark as Final" flag (<c>_MarkAsFinal</c>) into
    /// <see cref="TextDocument.MarkedAsFinal"/>, mirroring how the writer persists them.
    /// A missing part is fine.
    /// </summary>
    private static void ReadCustomProperties(ZipArchive archive, TextDocument document)
    {
        var customXml = LoadPart(archive, OpcPackageProperties.CustomPropertiesZipEntry);
        var root = customXml?.Root;
        if (root is null)
            return;
        document.Preserved.OriginalCustomProperties = new XElement(root);

        var customProperties = OpcCustomDocumentProperties.FromRoot(root);

        var text = customProperties.GetString(WatermarkPropertyName);
        // Accept empty text for picture watermarks: check for any WatermarkOptions property presence.
        var imageBase64Check = customProperties.GetString(WatermarkImagePropertyName);
        if (!string.IsNullOrEmpty(text) || !string.IsNullOrEmpty(imageBase64Check))
        {
            // Check if full WatermarkOptions properties are present (written by the new writer).
            var font = customProperties.GetString(WatermarkFontFamilyPropertyName);
            var color = customProperties.GetString(WatermarkColorPropertyName);
            var layoutStr = customProperties.GetString(WatermarkLayoutPropertyName);
            var opacity = customProperties.GetDouble(WatermarkOpacityPropertyName) ?? 0.3;

            if (font is not null || color is not null || layoutStr is not null
                || customProperties.Contains(WatermarkOpacityPropertyName)
                || !string.IsNullOrEmpty(imageBase64Check))
            {
                var layout = layoutStr is "Horizontal" ? WatermarkLayout.Horizontal : WatermarkLayout.Diagonal;

                // Picture watermark: image bytes encoded as base-64 (pre-read above).
                byte[]? imageBytes = null;
                if (!string.IsNullOrEmpty(imageBase64Check))
                {
                    try { imageBytes = Convert.FromBase64String(imageBase64Check); }
                    catch { /* corrupt base-64 → treat as no image */ }
                }
                var scaleStr = customProperties.GetString(WatermarkScalePropertyName);
                int.TryParse(scaleStr, System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture, out var scalePct);

                document.Page.WatermarkOptions = new WatermarkOptions(text ?? string.Empty)
                {
                    FontFamily = font ?? "Calibri",
                    FontColorHex = color ?? "#808080",
                    Layout = layout,
                    Opacity = System.Math.Clamp(opacity, 0.0, 1.0),
                    ImageBytes = imageBytes,
                    ScalePct = scalePct,
                };
            }
            else
            {
                // Legacy: only the plain text property was written — migrate to legacy field.
                document.Page.Watermark = text;
            }
        }

        if (customProperties.GetBoolean(MarkAsFinalPropertyName) == true)
            document.MarkedAsFinal = true;
    }

    /// <summary>
    /// Recovers Word's native VML watermark when a document was not produced by FreeW and therefore
    /// has no FreeW watermark custom properties. Word serializes canonical text and picture watermarks
    /// in a header as <c>PowerPlusWaterMarkObject</c> and <c>PowerPlusPictureWaterMarkObject</c>.
    /// The visible VML payload is the authority for rendering: FreeW text metadata without that
    /// payload remains editable, but must not create a watermark Word itself does not show.
    /// </summary>
    private static void ReadNativeVmlWatermark(ZipArchive archive, TextDocument document)
    {
        var customTextWatermark = document.Page.WatermarkOptions is { IsPicture: false };
        if (document.Page.WatermarkOptions is { IsPicture: true } existingPictureWatermark)
        {
            ApplyNativeVmlPictureGeometry(archive, document, existingPictureWatermark);
            return;
        }

        foreach (var entry in archive.Entries
                     .Where(entry => entry.FullName.StartsWith("word/header", StringComparison.OrdinalIgnoreCase)
                         && entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)))
        {
            var root = LoadPart(archive, entry.FullName)?.Root;
            var textShape = root?.Descendants(W + "sdt")
                .Where(IsWordWatermarkContentControl)
                .SelectMany(control => control.Descendants(V + "shape"))
                .FirstOrDefault(shape => shape.Attribute("id")?.Value?.StartsWith("PowerPlusWaterMarkObject", StringComparison.Ordinal) == true
                    && shape.Element(V + "textpath") is not null);
            var textPath = textShape?.Element(V + "textpath");
            var text = textPath?.Attribute("string")?.Value;
            if (!string.IsNullOrWhiteSpace(text))
            {
                var fill = textShape!.Element(V + "fill");
                var color = NormalizeVmlColor(fill?.Attribute("color")?.Value ?? textShape.Attribute("fillcolor")?.Value);
                var rotation = ParseVmlStyleNumber(textShape.Attribute("style")?.Value, "rotation");
                var (textWidthPt, textHeightPt) = ParseVmlShapeSize(textShape.Attribute("style")?.Value);
                var fitShape = ParseVmlBoolean(textPath?.Attribute("fitshape")?.Value);
                var textPathEnabled = ParseVmlBoolean(textPath?.Attribute("on")?.Value);
                var shapeTypeXml = ReadNativeVmlTextShapeTypeXml(root, textShape);
                var textPathXml = textPath?.ToString(SaveOptions.DisableFormatting);
                if (document.Page.WatermarkOptions is { IsPicture: false } existingTextWatermark)
                {
                    document.Page.WatermarkOptions = existingTextWatermark with
                    {
                        NativeVmlTextWidthPt = textWidthPt > 0 ? textWidthPt : null,
                        NativeVmlTextHeightPt = textHeightPt > 0 ? textHeightPt : null,
                        NativeVmlTextFitShape = fitShape,
                        NativeVmlTextRotationDegrees = rotation,
                        NativeVmlTextPathXml = textPathXml,
                        NativeVmlTextPathEnabled = textPathEnabled,
                        NativeVmlTextShapeTypeXml = shapeTypeXml
                    };
                    return;
                }

                if (document.Page.EffectiveWatermark is not null)
                    return;

                document.Page.WatermarkOptions = new WatermarkOptions(text)
                {
                    FontFamily = ParseVmlStyleValue(textPath!.Attribute("style")?.Value, "font-family") ?? "Calibri",
                    FontColorHex = color ?? "#808080",
                    Layout = rotation is { } value && Math.Abs(value) < 0.01
                        ? WatermarkLayout.Horizontal
                        : WatermarkLayout.Diagonal,
                    Opacity = ParseVmlOpacity(fill?.Attribute("opacity")?.Value),
                    NativeVmlTextWidthPt = textWidthPt > 0 ? textWidthPt : null,
                    NativeVmlTextHeightPt = textHeightPt > 0 ? textHeightPt : null,
                    NativeVmlTextFitShape = fitShape,
                    NativeVmlTextRotationDegrees = rotation,
                    NativeVmlTextPathXml = textPathXml,
                    NativeVmlTextPathEnabled = textPathEnabled,
                    NativeVmlTextShapeTypeXml = shapeTypeXml
                };
                return;
            }

            var pictureShape = root?.Descendants(V + "shape")
                .FirstOrDefault(shape => shape.Attribute("id")?.Value == "PowerPlusPictureWaterMarkObject");
            if (pictureShape is null)
                continue;

            var pictureFill = pictureShape.Element(V + "fill");
            var relationshipId = pictureFill?.Attribute(R + "id")?.Value
                ?? pictureShape.Descendants(V + "imagedata")
                    .Select(imageData => imageData.Attribute(R + "id")?.Value)
                    .FirstOrDefault(id => !string.IsNullOrWhiteSpace(id));
            if (string.IsNullOrWhiteSpace(relationshipId))
                continue;

            var imageRelationships = ReadPartImageRelationships(archive, entry.FullName);
            if (!imageRelationships.TryGetValue(relationshipId, out var imagePath)
                || LoadMedia(archive, imagePath) is not { } imageBytes)
            {
                continue;
            }

            var pictureRotation = ParseVmlStyleNumber(pictureShape.Attribute("style")?.Value, "rotation");
            var (pictureWidthPt, pictureHeightPt) = ParseVmlShapeSize(pictureShape.Attribute("style")?.Value);
            document.Page.WatermarkOptions = new WatermarkOptions(string.Empty)
            {
                ImageBytes = imageBytes,
                NativeVmlPictureWidthPt = pictureWidthPt > 0 ? pictureWidthPt : null,
                NativeVmlPictureHeightPt = pictureHeightPt > 0 ? pictureHeightPt : null,
                NativeVmlPictureRecolor = ParseVmlBoolean(pictureFill?.Attribute("recolor")?.Value),
                Layout = pictureRotation is { } pictureRotationValue && Math.Abs(pictureRotationValue) < 0.01
                    ? WatermarkLayout.Horizontal
                    : WatermarkLayout.Diagonal,
                Opacity = ParseVmlOpacity(pictureFill?.Attribute("opacity")?.Value)
            };
            return;
        }

        // A FreeW custom-property payload is not a Word-visible watermark on its own.
        // Preserve it for editing/round-trip, but prevent the host from synthesizing a text path
        // unless the package also supplied Word's canonical VML shape above.
        if (customTextWatermark
            && document.Page.WatermarkOptions is { IsPicture: false, NativeVmlTextPathEnabled: null } textWatermark)
        {
            document.Page.WatermarkOptions = textWatermark with { NativeVmlTextPathEnabled = false };
        }
    }

    private static bool IsWordWatermarkContentControl(XElement control) =>
        string.Equals(
            control.Element(W + "sdtPr")?.Element(W + "docPartObj")?.Element(W + "docPartGallery")?.Attribute(W + "val")?.Value,
            "Watermarks",
            StringComparison.Ordinal);

    private static string? ReadNativeVmlTextShapeTypeXml(XElement? root, XElement shape)
    {
        var shapeTypeId = shape.Attribute("type")?.Value.Trim().TrimStart('#');
        if (string.IsNullOrWhiteSpace(shapeTypeId))
            return null;

        return root?.Descendants(V + "shapetype")
            .FirstOrDefault(shapeType => shapeType.Attribute("id")?.Value == shapeTypeId)
            ?.ToString(SaveOptions.DisableFormatting);
    }

    // FreeW metadata remains authoritative for picture content and editing semantics. VML adds
    // supplemental source geometry that is not represented by the legacy custom properties.
    private static void ApplyNativeVmlPictureGeometry(
        ZipArchive archive,
        TextDocument document,
        WatermarkOptions existingPictureWatermark)
    {
        foreach (var entry in archive.Entries
                     .Where(entry => entry.FullName.StartsWith("word/header", StringComparison.OrdinalIgnoreCase)
                         && entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)))
        {
            var pictureShape = LoadPart(archive, entry.FullName)?.Root?
                .Descendants(V + "shape")
                .FirstOrDefault(shape => shape.Attribute("id")?.Value == "PowerPlusPictureWaterMarkObject");
            if (pictureShape is null)
                continue;

            var (widthPt, heightPt) = ParseVmlShapeSize(pictureShape.Attribute("style")?.Value);
            if (widthPt <= 0 || heightPt <= 0)
                continue;

            document.Page.WatermarkOptions = existingPictureWatermark with
            {
                NativeVmlPictureWidthPt = widthPt,
                NativeVmlPictureHeightPt = heightPt,
                NativeVmlPictureRecolor = ParseVmlBoolean(pictureShape.Element(V + "fill")?.Attribute("recolor")?.Value)
            };
            return;
        }
    }

    private static string? ParseVmlStyleValue(string? style, string name)
    {
        if (string.IsNullOrWhiteSpace(style))
            return null;

        foreach (var part in style.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separator = part.IndexOf(':');
            if (separator <= 0 || !part[..separator].Trim().Equals(name, StringComparison.OrdinalIgnoreCase))
                continue;

            var value = part[(separator + 1)..].Trim().Trim('"', '\'');
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }

        return null;
    }

    private static double? ParseVmlStyleNumber(string? style, string name) =>
        double.TryParse(
            ParseVmlStyleValue(style, name),
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            out var value)
                ? value
                : null;

    private static double ParseVmlOpacity(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return 1;

        var normalized = value.Trim();
        var isPercent = normalized.EndsWith('%');
        if (isPercent)
            normalized = normalized[..^1];
        return double.TryParse(
            normalized,
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            out var opacity)
                ? Math.Clamp(isPercent ? opacity / 100 : opacity, 0, 1)
                : 1;
    }

    private static bool? ParseVmlBoolean(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "t" or "true" or "1" => true,
        "f" or "false" or "0" => false,
        _ => null
    };

    private static string? NormalizeVmlColor(string? value)
    {
        var normalized = value?.Trim();
        var hex = normalized?.TrimStart('#');
        return hex is { Length: 6 } && hex.All(Uri.IsHexDigit)
            ? "#" + hex.ToUpperInvariant()
            : normalized?.ToLowerInvariant() switch
            {
                "black" => "#000000",
                "blue" => "#0000FF",
                "fuchsia" or "magenta" => "#FF00FF",
                "gray" or "grey" => "#808080",
                "green" => "#008000",
                "lime" => "#00FF00",
                "maroon" => "#800000",
                "navy" => "#000080",
                "olive" => "#808000",
                "purple" => "#800080",
                "red" => "#FF0000",
                "silver" => "#C0C0C0",
                "teal" => "#008080",
                "white" => "#FFFFFF",
                "yellow" => "#FFFF00",
                _ => null
            };
    }

    private static void ReadStyles(ZipArchive archive, TextDocument document)
    {
        // Baseline document default paragraph spacing: no extra space, 1.15 line — the behaviour for
        // documents without w:docDefaults (and FreeW's previous hardcoded behaviour). Real docs override
        // it below.
        document.DefaultParagraph = ParagraphFormatting.Default with { SpaceBeforePt = 0, SpaceAfterPt = 0 };

        var stylesXml = LoadPart(archive, "word/styles.xml");
        if (stylesXml is null)
            return;

        // w:docDefaults/w:pPrDefault/w:pPr is the document's default paragraph spacing, applied to any
        // paragraph that does not set its own (Word's cascade root). FreeW ignored it, so every paragraph
        // rendered at 0 space-after / 1.15 line regardless of the document — drifting vs Word down the page.
        var docDefaults = stylesXml.Root?.Element(W + "docDefaults");
        var ddPr = docDefaults?.Element(W + "pPrDefault")?.Element(W + "pPr");
        if (ddPr is not null)
        {
            // A package-authored paragraph-default root is authoritative even when it omits w:line:
            // Word then uses the font's natural single-line box rather than the application/template
            // fallback used by packages with no paragraph defaults at all.
            document.UseWordApplicationDefaultLineSpacing = false;
            document.DefaultParagraph = ReadDocDefaultParagraph(ddPr);
        }

        // w:docDefaults/w:rPrDefault/w:rPr carries the document default run properties (default font
        // family, size, color, language). Word blank documents store their body font (e.g. Calibri 11pt /
        // Aptos 11pt) ONLY here; most runs carry no explicit w:rFonts. Without reading this, FreeW renders
        // the correct font at display time (document.DefaultRun is used by the renderer) but loses it on
        // save because the writer never re-emits w:docDefaults — causing Word to fall back to Times New
        // Roman after a round-trip.
        var ddRPr = docDefaults?.Element(W + "rPrDefault")?.Element(W + "rPr");
        if (ddRPr is not null)
        {
            document.UseWordApplicationDefaultRunFormatting = false;
            var defaultRun = ReadRunFormatting(ddRPr);
            // Merge: keep the existing DefaultRun value for any field that docDefaults does not override.
            document.DefaultRun = document.DefaultRun with
            {
                FontFamily = defaultRun.FontFamily ?? document.DefaultRun.FontFamily,
                FontSizePt = defaultRun.FontSizePt ?? document.DefaultRun.FontSizePt,
                ColorHex = defaultRun.ColorHex ?? document.DefaultRun.ColorHex,
                LanguageTag = defaultRun.LanguageTag ?? document.DefaultRun.LanguageTag,
                Bold = defaultRun.Bold,
                Italic = defaultRun.Italic,
            };
        }

        var styles = stylesXml.Root?.Elements(W + "style");
        if (styles is null)
            return;

        foreach (var s in styles)
        {
            var id = s.Attribute(W + "styleId")?.Value;
            if (string.IsNullOrEmpty(id))
                continue;
            var rPr = s.Element(W + "rPr");
            var pPr = s.Element(W + "pPr");
            var styleType = s.Attribute(W + "type")?.Value switch
            {
                "character" => StyleType.Character,
                "table" => StyleType.Table,
                "numbering" => StyleType.Numbering,
                _ => StyleType.Paragraph
            };
            document.Styles[id] = new DocumentStyle
            {
                Id = id,
                Name = s.Element(W + "name")?.Attribute(W + "val")?.Value ?? id,
                Type = styleType,
                BasedOnStyleId = s.Element(W + "basedOn")?.Attribute(W + "val")?.Value,
                // The "Style for following paragraph" (w:next): the style applied to the paragraph created
                // when Enter is pressed at the end of one carrying this style (e.g. Heading1 -> Normal).
                NextStyleId = s.Element(W + "next")?.Attribute(W + "val")?.Value,
                OutlineLevel = ReadOutlineLevel(pPr),
                Run = rPr is null ? RunFormatting.Default : ReadRunFormatting(rPr),
                Paragraph = pPr is null ? ParagraphFormatting.Default : ReadParagraphFormatting(pPr),
                // A table style (e.g. the built-in TableGrid) defines its cell borders in w:tblPr/w:tblBorders;
                // capture whether they are visible so a table referencing this style draws them even without
                // its own tblBorders.
                TableBorders = ReadBorders(s.Element(W + "tblPr")?.Element(W + "tblBorders")),
                PreservedTableStyleXml = styleType == StyleType.Table
                    ? s.ToString(SaveOptions.DisableFormatting)
                    : null,
                // A style definition can carry numbering via w:pPr/w:numPr (numId + ilvl). FreeW does not model
                // numbering on a style, so capture the original numPr so the writer can re-emit it against the
                // preserved numbering.xml (under the same disjoint-id remap as paragraph-level preserved
                // numbering). Whether it survives the round-trip depends on the merge plan finding a matching
                // w:num — a numId with no definition is dropped, exactly like a paragraph's preserved numPr.
                PreservedNumbering = pPr is null ? null : ReadPreservedNumbering(pPr)
            };
        }
    }

    // ── DrawingGroup read (Phase 4) ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Reads a <c>wpg:wgp</c> drawing group from a run's <c>w:drawing/wp:anchor</c>, reconstructing a
    /// <see cref="DrawingGroup"/> with its floating placement and native child payloads decoded from
    /// <c>wps:wsp</c>, <c>pic:pic</c>, and <c>wpg:graphicFrame</c> children. Returns null when the run
    /// does not carry a wpg:wgp element.
    /// </summary>
    private static int? ReadOutlineLevel(XElement? pPr)
    {
        var value = pPr?.Element(W + "outlineLvl")?.Attribute(W + "val")?.Value;
        return int.TryParse(value, System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture, out var level)
            && level is >= 0 and <= 8
                ? level
                : null;
    }

    private static DrawingGroup? ReadDrawingGroup(
        XElement run,
        ZipArchive archive,
        IReadOnlyDictionary<string, string> imageRelationships,
        IReadOnlyDictionary<string, string> hyperlinkRelationships,
        IReadOnlyDictionary<int, ListKind> numbering)
    {
        var drawing = run.Element(W + "drawing");
        var anchor = drawing?.Element(Wp + "anchor");
        if (anchor is null) return null;

        var wgp = anchor.Descendants(Wpg + "wgp").FirstOrDefault();
        if (wgp is null) return null;

        var group = new DrawingGroup();
        ApplyFloatingPlacement(anchor, group.Placement);

        // Overall extent from wp:extent.
        var extent = anchor.Element(Wp + "extent");
        if (extent is not null)
        {
            group.WidthPt = EmuToPoints(extent.Attribute("cx")?.Value ?? "0");
            group.HeightPt = EmuToPoints(extent.Attribute("cy")?.Value ?? "0");
        }

        var groupXfrm = wgp.Element(Wpg + "grpSpPr")?.Element(A + "xfrm");
        if (groupXfrm is not null)
        {
            if (groupXfrm.Attribute("rot")?.Value is { } rotStr && long.TryParse(rotStr, out var rotEmu))
                group.RotationAngle = rotEmu / 60000.0;
            group.FlipH = groupXfrm.Attribute("flipH")?.Value is "1" or "true";
            group.FlipV = groupXfrm.Attribute("flipV")?.Value is "1" or "true";
        }

        // Child offsets/extents use the group xfrm's child-coordinate space. Flatten that space into
        // the rendered group bounds so the model's child offsets remain directly renderable.
        var childOriginX = EmuToPoints(groupXfrm?.Element(A + "chOff")?.Attribute("x")?.Value ?? "0");
        var childOriginY = EmuToPoints(groupXfrm?.Element(A + "chOff")?.Attribute("y")?.Value ?? "0");
        var childExtentX = EmuToPoints(groupXfrm?.Element(A + "chExt")?.Attribute("cx")?.Value ?? "0");
        var childExtentY = EmuToPoints(groupXfrm?.Element(A + "chExt")?.Attribute("cy")?.Value ?? "0");
        var childScaleX = childExtentX > 0 ? group.WidthPt / childExtentX : 1;
        var childScaleY = childExtentY > 0 ? group.HeightPt / childExtentY : 1;

        // wpg:wgp permits shape, picture, graphic-frame, and nested group children. The latter three retain
        // their real relationship-bearing payload instead of reducing a rich child to a placeholder.
        foreach (var groupChild in wgp.Elements().Where(element =>
            element.Name == Wps + "wsp"
            || element.Name == Pic + "pic"
            || element.Name == Wpg + "graphicFrame"
            || element.Name == Wpg + "wgp"))
        {
            var isShape = groupChild.Name == Wps + "wsp";
            var isPicture = groupChild.Name == Pic + "pic";
            var isNestedGroup = groupChild.Name == Wpg + "wgp";
            var childDocPr = isShape
                ? groupChild.Element(Wps + "cNvPr") ?? groupChild.Element(Wp + "docPr")
                : isPicture
                    ? groupChild.Element(Pic + "nvPicPr")?.Element(Pic + "cNvPr")
                    : groupChild.Element(Wpg + "cNvPr");
            var name = childDocPr?.Attribute("name")?.Value ?? string.Empty;

            var xfrm = isNestedGroup
                ? groupChild.Element(Wpg + "grpSpPr")?.Element(A + "xfrm")
                : isShape
                ? groupChild.Element(Wps + "spPr")?.Element(A + "xfrm")
                : isPicture
                    ? groupChild.Element(Pic + "spPr")?.Element(A + "xfrm")
                    : groupChild.Element(Wpg + "xfrm") ?? groupChild.Element(A + "xfrm");
            var off = xfrm?.Element(A + "off");
            var ext = xfrm?.Element(A + "ext");
            var ox = (EmuToPoints(off?.Attribute("x")?.Value ?? "0") - childOriginX) * childScaleX;
            var oy = (EmuToPoints(off?.Attribute("y")?.Value ?? "0") - childOriginY) * childScaleY;
            var cw = EmuToPoints(ext?.Attribute("cx")?.Value ?? "36") * childScaleX;
            var ch = EmuToPoints(ext?.Attribute("cy")?.Value ?? "36") * childScaleY;

            var fakeRun = isNestedGroup ? null : BuildGroupChildRun(groupChild, childDocPr, cw, ch);
            object? child = isNestedGroup
                ? ReadNestedDrawingGroup(groupChild, cw, ch, archive, imageRelationships, hyperlinkRelationships, numbering)
                : isPicture
                ? ReadImage(fakeRun!, archive, imageRelationships)
                : groupChild.Name == Wpg + "graphicFrame"
                    ? ReadChart(fakeRun!, archive, imageRelationships) ?? (object?)ReadSmartArt(fakeRun!, archive, imageRelationships)
                    : null;
            if (child is null && name.StartsWith("GroupChild:Image", StringComparison.Ordinal))
            {
                // Backward compatibility for marker-only payloads written before native group pictures.
                child = new InlineImage([], cw, ch);
            }
            else if (child is null && name.StartsWith("GroupChild:Shape:", StringComparison.Ordinal))
            {
                child = ReadShape(fakeRun!, archive, imageRelationships, hyperlinkRelationships, numbering);
                if (child is null)
                {
                    var kindStr = name["GroupChild:Shape:".Length..];
                    var kind = Enum.TryParse<ShapeKind>(kindStr, out var k) ? k : ShapeKind.Rectangle;
                    child = new Shape(kind, cw, ch);
                }
            }
            else if (child is null && name.StartsWith("GroupChild:Chart:", StringComparison.Ordinal))
            {
                var kindStr = name["GroupChild:Chart:".Length..];
                var kind = Enum.TryParse<ChartKind>(kindStr, out var k) ? k : ChartKind.Column;
                child = new Chart { Kind = kind, WidthPt = cw, HeightPt = ch };
            }
            else if (child is null && name.StartsWith("GroupChild:SmartArt", StringComparison.Ordinal))
            {
                child = new SmartArt { WidthPt = cw, HeightPt = ch };
            }
            else if (child is null && name.StartsWith("GroupChild:WordArt:", StringComparison.Ordinal))
            {
                var styleStr = name["GroupChild:WordArt:".Length..];
                var style = Enum.TryParse<WordArtStyle>(styleStr, out var s) ? s : WordArtStyle.FillBlue;
                child = ReadWordArt(fakeRun!) ?? new WordArt { Style = style, Text = "WordArt", FontSizePt = 36 };
            }
            else if (child is null)
            {
                // Unknown child type — try the rich shape/WordArt readers before falling back to a rectangle.
                child = ReadWordArt(fakeRun!)
                    ?? (object?)ReadShape(fakeRun!, archive, imageRelationships, hyperlinkRelationships, numbering)
                    ?? new Shape(ShapeKind.Rectangle, cw, ch);
            }

            if (child is not null && !isNestedGroup)
            {
                var angle = long.TryParse(xfrm?.Attribute("rot")?.Value, out var rotEmu)
                    ? rotEmu / 60000.0 : 0;
                var flipH = xfrm?.Attribute("flipH")?.Value is "1" or "true";
                var flipV = xfrm?.Attribute("flipV")?.Value is "1" or "true";
                switch (child)
                {
                    case InlineImage image: image.RotationAngle = angle; image.FlipH = flipH; image.FlipV = flipV; break;
                    case Shape shape: shape.RotationAngle = angle; shape.FlipH = flipH; shape.FlipV = flipV; break;
                    case Chart chart: chart.RotationAngle = angle; chart.FlipH = flipH; chart.FlipV = flipV; break;
                    case SmartArt smartArt: smartArt.RotationAngle = angle; smartArt.FlipH = flipH; smartArt.FlipV = flipV; break;
                    case WordArt wordArt: wordArt.RotationAngle = angle; wordArt.FlipH = flipH; wordArt.FlipV = flipV; break;
                }
            }

            if (child is not null)
            {
                group.Children.Add(child);
                group.ChildOffsets.Add((ox, oy));
            }
        }

        return group.Children.Count >= 2 ? group : null;
    }

    private static XElement BuildGroupChildRun(XElement groupChild, XElement? childDocPr, double widthPt, double heightPt)
    {
        XElement graphic;
        if (groupChild.Name == Wps + "wsp")
        {
            graphic = new XElement(A + "graphic",
                new XElement(A + "graphicData",
                    new XAttribute("uri", Wps.NamespaceName),
                    new XElement(groupChild)));
        }
        else if (groupChild.Name == Pic + "pic")
        {
            graphic = new XElement(A + "graphic",
                new XElement(A + "graphicData",
                    new XAttribute("uri", Pic.NamespaceName),
                    new XElement(groupChild)));
        }
        else
        {
            graphic = new XElement(groupChild.Element(A + "graphic")!);
        }

        return new XElement(W + "r",
            new XElement(W + "drawing",
                new XElement(Wp + "inline",
                    new XElement(Wp + "extent",
                        new XAttribute("cx", PointsToEmu(widthPt)),
                        new XAttribute("cy", PointsToEmu(heightPt))),
                    childDocPr is null ? null : new XElement(Wp + "docPr",
                        childDocPr.Attributes().Where(attribute => !attribute.IsNamespaceDeclaration)),
                    graphic)));
    }

    private static DrawingGroup? ReadNestedDrawingGroup(
        XElement group,
        double widthPt,
        double heightPt,
        ZipArchive archive,
        IReadOnlyDictionary<string, string> imageRelationships,
        IReadOnlyDictionary<string, string> hyperlinkRelationships,
        IReadOnlyDictionary<int, ListKind> numbering)
    {
        var run = new XElement(W + "r",
            new XElement(W + "drawing",
                new XElement(Wp + "anchor",
                    new XElement(Wp + "extent",
                        new XAttribute("cx", PointsToEmu(widthPt)),
                        new XAttribute("cy", PointsToEmu(heightPt))),
                    new XElement(A + "graphic",
                        new XElement(A + "graphicData",
                            new XAttribute("uri", GroupGraphicDataUri),
                            new XElement(group))))));
        return ReadDrawingGroup(run, archive, imageRelationships, hyperlinkRelationships, numbering);
    }

    /// <summary>
    /// Maps Word's <c>w:highlight/@w:val</c> named color token to a <c>#RRGGBB</c> hex string, or
    /// <c>null</c> for unrecognised/none tokens. The colors are the fixed sRGB values Word uses for its
    /// highlight gallery (same across all themes).
    /// </summary>
    internal static string? HighlightTokenToHex(string? token) => token switch
    {
        "yellow"      => "#FFFF00",
        "green"       => "#00FF00",
        "cyan"        => "#00FFFF",
        "magenta"     => "#FF00FF",
        "blue"        => "#0000FF",
        "red"         => "#FF0000",
        "darkBlue"    => "#000080",
        "darkCyan"    => "#008080",
        "darkGreen"   => "#008000",
        "darkMagenta" => "#800080",
        "darkRed"     => "#800000",
        "darkYellow"  => "#808000",
        "darkGray"    => "#808080",
        "lightGray"   => "#C0C0C0",
        "black"       => "#000000",
        "white"       => "#FFFFFF",
        _ => null
    };
}
