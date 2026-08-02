using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;
using Free.Shared.Drawing;
using Free.Shared.Opc;
using FreeP.Core.Model;
using static Free.Shared.Opc.OpcPathHelper;

namespace FreeP.Core.IO;

/// <summary>
/// Wave 1B: reads a <c>.pptx</c> OPC package and returns a <see cref="Presentation"/> model.
/// Entry point: <see cref="Read(string)"/> or <see cref="Read(Stream)"/>.
/// </summary>
public static class PptxPackageReader
{
    private sealed record ModernCommentAuthor(
        string Id,
        string Name,
        string Initials,
        string UserId,
        string ProviderId);

    // ── OOXML namespaces ─────────────────────────────────────────────────────────
    private static readonly XNamespace P   = "http://schemas.openxmlformats.org/presentationml/2006/main";
    private static readonly XNamespace A   = PptxColorReader.A;
    private static readonly XNamespace R   = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace Adec = "http://schemas.microsoft.com/office/drawing/2017/decorative";
    private static readonly XNamespace FreePRecording = "https://freex.local/freep/recording/2026";
    private static readonly XNamespace FreePText = "https://freex.local/freep/text/2026";
    private const string AutoNumTemplateExtUri = "{2E2E4D2B-4E4E-4A9E-9B3A-7C2BAA5D1B7C}";
    private const string RecordingMediaArtifactsPath = "ppt/media/recordingArtifacts.xml";

    // ── Relationship type constants ───────────────────────────────────────────────
    private const string OfficeDocRelType   = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument";
    private const string SlideRelType       = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/slide";
    private const string SlideMasterRelType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/slideMaster";
    private const string SlideLayoutRelType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/slideLayout";
    private const string ThemeRelType       = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/theme";
    private const string ImageRelType       = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/image";
    private const string CorePropsRelType     = OpcPackageProperties.CorePropertiesRelationshipType;
    private const string SettingsRelType      = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/settings";
    private const string TableStylesRelType   = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/tableStyles";
    private const string ChartRelType         = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart";
    private const string NotesSlideRelType    = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/notesSlide";
    private const string NotesMasterRelType   = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/notesMaster";
    private const string HyperlinkRelType     = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/hyperlink";
    private const string SlideHlinkRelType    = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/slide";
    private const string CommentsRelType      = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/comments";
    private const string CommentAuthorsRelType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/commentAuthors";
    private const string ModernCommentsRelType = "http://schemas.microsoft.com/office/2018/10/relationships/comments";
    private const string ModernAuthorsRelType = "http://schemas.microsoft.com/office/2018/10/relationships/authors";
    private const string VideoRelType         = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/video";
    private const string AudioRelType         = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/audio";
    private const string CaptionRelType       = "http://schemas.microsoft.com/office/2011/relationships/mediaCaption";
    // Microsoft proprietary media rel type used by newer PowerPoint
    private const string MediaRelType         = "http://schemas.microsoft.com/office/2007/relationships/media";

    // p14 section extension + mc:AlternateContent namespace
    private static readonly XNamespace P14  = "http://schemas.microsoft.com/office/powerpoint/2010/main";
    private static readonly XNamespace P15  = "http://schemas.microsoft.com/office/powerpoint/2012/main";
    private static readonly XNamespace P159 = "http://schemas.microsoft.com/office/powerpoint/2015/09/main";
    private static readonly XNamespace P188 = "http://schemas.microsoft.com/office/powerpoint/2018/8/main";
    private static readonly XNamespace MC   = "http://schemas.openxmlformats.org/markup-compatibility/2006";
    private const string SectionExtUri = "{521415D9-36F7-43E2-AB2F-B90AF26B5E84}";

    // ── Public API ───────────────────────────────────────────────────────────────

    /// <summary>Opens a .pptx file from disk and returns a populated <see cref="Presentation"/>.</summary>
    public static Presentation Read(string path)
    {
        using var stream = File.OpenRead(path);
        return Read(stream);
    }

    /// <summary>Reads a .pptx from any stream and returns a populated <see cref="Presentation"/>.</summary>
    public static Presentation Read(Stream stream)
    {
        // Copy to MemoryStream so ZipArchive can seek
        var ms = new MemoryStream();
        stream.CopyTo(ms);
        ms.Position = 0;

        using var archive = new ZipArchive(ms, ZipArchiveMode.Read, leaveOpen: false);
        var snapshot = CapturePackageSnapshot(archive);
        var presentation = ReadArchive(archive);
        presentation.PackageSnapshot = snapshot;
        presentation.PackageKind = DetectPackageKind(snapshot);
        return presentation;
    }

    private static PresentationPackageKind DetectPackageKind(PptxPackageSnapshot snapshot)
    {
        if (!snapshot.TryGetEntry("[Content_Types].xml", out var bytes))
            return PresentationPackageKind.Presentation;

        var contentTypes = OpcXml.TryLoadXml(bytes);
        var contentType = contentTypes?.Root?
            .Elements(OpcMediaTypes.ContentTypesNamespace + "Override")
            .FirstOrDefault(overrideElement =>
                string.Equals(overrideElement.Attribute("PartName")?.Value, "/ppt/presentation.xml", StringComparison.OrdinalIgnoreCase))
            ?.Attribute("ContentType")?.Value;

        return contentType switch
        {
            "application/vnd.ms-powerpoint.presentation.macroEnabled.main+xml" => PresentationPackageKind.MacroEnabledPresentation,
            "application/vnd.openxmlformats-officedocument.presentationml.template.main+xml" => PresentationPackageKind.Template,
            "application/vnd.ms-powerpoint.template.macroEnabled.main+xml" => PresentationPackageKind.MacroEnabledTemplate,
            "application/vnd.openxmlformats-officedocument.presentationml.slideshow.main+xml" => PresentationPackageKind.SlideShow,
            "application/vnd.ms-powerpoint.slideshow.macroEnabled.main+xml" => PresentationPackageKind.MacroEnabledSlideShow,
            _ => PresentationPackageKind.Presentation,
        };
    }

    // ── Core archive reading ──────────────────────────────────────────────────────

    private static Presentation ReadArchive(ZipArchive archive)
    {
        var presentation = new Presentation();

        // Parse root rels to find presentation.xml path
        var rootRels = OpcRelationships.LoadTargets(archive, "_rels/.rels");
        var presPath = OpcRelationships.FirstTargetByType(rootRels, OfficeDocRelType);
        if (presPath is null) return presentation;

        // Normalize path (remove leading /)
        presPath = ToZipEntryPath(presPath);

        // Core properties
        var corePropsPath = OpcRelationships.FirstTargetByType(rootRels, CorePropsRelType);
        if (corePropsPath is not null)
            OpcDocumentProperties.ReadCoreProperties(
                archive,
                presentation.Properties,
                ToZipEntryPath(corePropsPath));

        // Parse presentation.xml
        var presXml = OpcXml.TryLoadXml(archive, presPath);
        if (presXml?.Root is null) return presentation;

        var presRoot = presXml.Root;
        var presDir = GetDirectoryName(presPath);

        // Slide size
        var sldSz = presRoot.Element(P + "sldSz");
        if (sldSz is not null)
        {
            if (long.TryParse(sldSz.Attribute("cx")?.Value, out var cx) && cx > 0)
                presentation.SlideSizeCxEmu = cx;
            if (long.TryParse(sldSz.Attribute("cy")?.Value, out var cy) && cy > 0)
                presentation.SlideSizeCyEmu = cy;
        }

        var notesSz = presRoot.Element(P + "notesSz");
        if (notesSz is not null)
        {
            if (long.TryParse(notesSz.Attribute("cx")?.Value, out var cx) && cx > 0)
                presentation.NotesPageSizeCxEmu = cx;
            if (long.TryParse(notesSz.Attribute("cy")?.Value, out var cy) && cy > 0)
                presentation.NotesPageSizeCyEmu = cy;
        }

        // Rels for presentation.xml
        var presRels = OpcRelationships.LoadTargets(archive, GetRelationshipPartPath(presPath));
        presentation.DocumentMathProperties = ReadDocumentMathProperties(archive, presRels, presDir);

        // Table styles (keyed by style GUID)
        var tableStyles = new Dictionary<string, TableStyleData>(StringComparer.OrdinalIgnoreCase);
        var tableStylesTarget = OpcRelationships.FirstTargetByType(presRels, TableStylesRelType);
        if (tableStylesTarget is not null)
        {
            var tableStylesPath = ResolveRelativeZipPath(presDir, tableStylesTarget);
            ReadTableStyles(archive, tableStylesPath, presentation.Theme.ColorScheme, tableStyles);
        }

        // Slide masters → layouts
        var masterRelEntries = presRels.Where(r => r.Type == SlideMasterRelType).ToList();
        bool firstMaster = true;
        foreach (var (masterId, _, masterTarget) in masterRelEntries)
        {
            var masterPath = ResolveRelativeZipPath(presDir, masterTarget);
            var (master, theme) = ReadSlideMaster(archive, masterPath, masterId);
            // MM4: assign the theme to its OWNING master instead of clobbering presentation.Theme.
            // presentation.Theme stays as the first master's theme for backward-compatibility with
            // any consumer that uses it as a fallback (e.g. single-master decks, table styles).
            if (theme is not null)
            {
                master.Theme = theme;
                if (firstMaster)
                    presentation.Theme = theme;
            }
            firstMaster = false;
            presentation.Masters.Add(master);

            var masterDir = GetDirectoryName(masterPath);
            var masterRels = OpcRelationships.LoadTargets(archive, GetRelationshipPartPath(masterPath));

            // Use this master's own theme (or fall back to presentation.Theme) for layout parsing.
            var masterColorScheme = (master.Theme ?? presentation.Theme).ColorScheme;
            foreach (var (layoutId, _, layoutTarget) in masterRels.Where(r => r.Type == SlideLayoutRelType))
            {
                var layoutPath = ResolveRelativeZipPath(masterDir, layoutTarget);
                var layout = ReadSlideLayout(archive, layoutPath, layoutId, master.Id, masterColorScheme);
                presentation.Layouts.Add(layout);
            }
        }

        // Slides in order from sldIdLst — two-phase so internal hyperlinks can resolve to Slide.Id.
        // Phase 1: collect ordered (rId, slidePath) pairs and create placeholder Slide objects so we
        //           have a complete allSlides list before parsing shapes.
        // Notes master: retain the native part and expose its placeholder geometry/styles to the
        // shared notes-page planner.  This runs after slide-master themes are loaded so
        // theme-dependent notes styles resolve against the presentation's actual first theme.
        var notesMasterTarget = OpcRelationships.FirstTargetByType(presRels, NotesMasterRelType);
        if (notesMasterTarget is not null)
        {
            var notesMasterPath = ResolveRelativeZipPath(presDir, notesMasterTarget);
            if (TryReadPackageEntry(archive, notesMasterPath, out var notesMasterBytes))
            {
                presentation.NotesMasterXml = notesMasterBytes;
                var notesMasterXml = OpcXml.TryLoadXml(notesMasterBytes);
                if (notesMasterXml?.Root is { } notesMasterRoot)
                {
                    var notesMasterScheme = presentation.Theme.ColorScheme;
                    var spTree = notesMasterRoot.Element(P + "cSld")?.Element(P + "spTree");
                    if (spTree is not null)
                    {
                        foreach (var shape in ReadShapesFromTree(spTree, archive, notesMasterPath, notesMasterScheme))
                            presentation.NotesMasterPlaceholders.Add(shape);
                    }

                    var notesStyle = notesMasterRoot.Element(P + "notesStyle");
                    if (notesStyle is not null)
                    {
                        presentation.NotesMasterTextStyles = new MasterTextStyles();
                        ReadTextStyleLevels(notesStyle, presentation.NotesMasterTextStyles.BodyStyle, notesMasterScheme);
                    }
                }
            }

            var notesMasterRelsPath = GetRelationshipPartPath(notesMasterPath);
            if (TryReadPackageEntry(archive, notesMasterRelsPath, out var notesMasterRelsBytes))
                presentation.NotesMasterRelsXml = notesMasterRelsBytes;
        }

        var slideRelEntries = presRels.ToDictionary(r => r.Id, StringComparer.OrdinalIgnoreCase);
        var sldIdList = presRoot.Element(P + "sldIdLst")?.Elements(P + "sldId").ToList() ?? new();

        // Build ordered list of (rId, slidePath) for all valid slide entries.
        var slideInfos = new List<(string rId, string slidePath)>();
        foreach (var sldIdEl in sldIdList)
        {
            var rId = sldIdEl.Attribute(R + "id")?.Value;
            if (string.IsNullOrWhiteSpace(rId) || !slideRelEntries.TryGetValue(rId, out var slideRel))
                continue;
            if (slideRel.Type != SlideRelType) continue;
            slideInfos.Add((rId, ResolveRelativeZipPath(presDir, slideRel.Target)));
        }

        // Create placeholder slides (with Id set) for the allSlides reference list.
        var allSlides = new List<Slide>(slideInfos.Count);
        foreach (var (rId, _) in slideInfos)
        {
            var numericId = sldIdList
                .FirstOrDefault(el => string.Equals(el.Attribute(R + "id")?.Value, rId, StringComparison.OrdinalIgnoreCase))
                ?.Attribute("id")?.Value;
            allSlides.Add(new Slide
            {
                Id = rId,
                NumericId = uint.TryParse(numericId, out var parsedId) ? parsedId : null,
            });
        }

        // Build a map from normalized slide part path → Slide.Id (= presentation-level rId).
        // This is used by ResolveHlinkClick to resolve internal slide-jump hyperlinks by part
        // path rather than by filename digit, so reordered decks resolve to the correct slide.
        var slidePartPathToId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (rId, slidePath) in slideInfos)
            slidePartPathToId[slidePath] = rId;

        // Phase 2: read each slide, replacing the placeholder with the fully parsed slide.
        for (int si = 0; si < slideInfos.Count; si++)
        {
            var (rId, slidePath) = slideInfos[si];
            var slide = ReadSlide(archive, slidePath, rId, presentation.Theme.ColorScheme, presentation.Layouts, tableStyles, allSlides, slidePartPathToId);
            slide.NumericId = allSlides[si].NumericId;
            // Replace the placeholder so hyperlinks referencing this slide still get the same object.
            allSlides[si] = slide;
            presentation.Slides.Add(slide);
        }

        // Build a mapping from sldId integer → rId so we can resolve section membership.
        // sldIdList was built above from p:sldIdLst elements.
        var sldIdToRId = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var sldIdEl in sldIdList)
        {
            var numId = sldIdEl.Attribute("id")?.Value;
            var rId2  = sldIdEl.Attribute(R + "id")?.Value;
            if (!string.IsNullOrWhiteSpace(numId) && !string.IsNullOrWhiteSpace(rId2))
                sldIdToRId[numId] = rId2;
        }

        // Sections from p:extLst / p:ext[@uri="{521415D9-…}"] / p14:sectionLst
        ReadSections(presRoot, sldIdToRId, presentation);
        ReadCustomShows(presRoot, sldIdToRId, presentation);
        ReadRecordingMediaArtifacts(archive, presentation);

        // Comment authors live in a single ppt/commentAuthors.xml part referenced from presRels.
        var cmAuthorsTarget = OpcRelationships.FirstTargetByType(presRels, CommentAuthorsRelType);
        var authorMap = new Dictionary<int, (string name, string initials)>();
        if (cmAuthorsTarget is not null)
        {
            var cmAuthorsPath = ResolveRelativeZipPath(presDir, cmAuthorsTarget);
            authorMap = ReadCommentAuthors(archive, cmAuthorsPath);
        }

        var modernAuthorsTarget = OpcRelationships.FirstTargetByType(presRels, ModernAuthorsRelType);
        var modernAuthorMap = new Dictionary<string, ModernCommentAuthor>(StringComparer.OrdinalIgnoreCase);
        if (modernAuthorsTarget is not null)
        {
            var modernAuthorsPath = ResolveRelativeZipPath(presDir, modernAuthorsTarget);
            modernAuthorMap = ReadModernCommentAuthors(archive, modernAuthorsPath);
        }

        // Re-process each slide's comments now that we have the author map.
        // (Comments were NOT parsed in ReadSlide yet — we do it here so authorMap is available.)
        for (int si = 0; si < presentation.Slides.Count; si++)
        {
            var slide = presentation.Slides[si];
            var rId = sldIdList.Count > si ? sldIdList[si].Attribute(R + "id")?.Value : null;
            if (rId is null) continue;
            if (!slideRelEntries.TryGetValue(rId, out var sr)) continue;
            var slidePath2 = ResolveRelativeZipPath(presDir, sr.Target);
            var slideRels2 = OpcRelationships.LoadTargets(archive, GetRelationshipPartPath(slidePath2));
            var cmTarget = OpcRelationships.FirstTargetByType(slideRels2, CommentsRelType);
            if (cmTarget is not null)
            {
                var cmPath = ResolveRelativeZipPath(GetDirectoryName(slidePath2), cmTarget);
                ReadSlideComments(archive, cmPath, authorMap, slide.Comments);
            }

            var modernCmTarget = OpcRelationships.FirstTargetByType(slideRels2, ModernCommentsRelType);
            if (modernCmTarget is not null)
            {
                var modernCmPath = ResolveRelativeZipPath(GetDirectoryName(slidePath2), modernCmTarget);
                ReadModernSlideComments(archive, modernCmPath, modernAuthorMap, slide.Comments);
            }
        }

        return presentation;
    }

    /// <summary>
    /// Reads document-level OMML defaults only from a related settings part.
    /// PresentationML normally has no settings relationship; in that normal
    /// case returning null is intentional and preserves Office's authored
    /// source boundary instead of inventing a Cambria Math default.
    /// </summary>
    private static OmmlMathProperties? ReadDocumentMathProperties(
        ZipArchive archive,
        IReadOnlyList<OpcRelationshipTarget> presentationRelationships,
        string presentationDirectory)
    {
        var settingsTarget = presentationRelationships
            .FirstOrDefault(relationship =>
                string.Equals(relationship.Type, SettingsRelType, StringComparison.OrdinalIgnoreCase))
            .Target;
        if (string.IsNullOrWhiteSpace(settingsTarget))
            return null;

        var settingsPath = ResolveRelativeZipPath(presentationDirectory, settingsTarget);
        var settingsXml = OpcXml.TryLoadXml(archive, settingsPath);
        return settingsXml?.Root is { } root
            ? ReadOmmlMathProperties(root.Element(M + "mathPr"))
            : null;
    }

    private static void ReadRecordingMediaArtifacts(ZipArchive archive, Presentation presentation)
    {
        var document = OpcXml.TryLoadXml(archive, RecordingMediaArtifactsPath);
        if (document?.Root is null || document.Root.Name != FreePRecording + "recordingMediaArtifacts")
        {
            return;
        }

        foreach (var element in document.Root.Elements(FreePRecording + "artifact"))
        {
            if (!Enum.TryParse<PresentationRecordingMediaArtifactKind>(
                    element.Attribute("kind")?.Value,
                    ignoreCase: true,
                    out var kind) ||
                !int.TryParse(element.Attribute("slideIndex")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var slideIndex) ||
                !long.TryParse(element.Attribute("contentLengthBytes")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var length) ||
                !int.TryParse(element.Attribute("durationMs")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var durationMs))
            {
                continue;
            }

            var packagePath = element.Attribute("packagePath")?.Value ?? string.Empty;
            var normalizedPackagePath = NormalizeZipPath(packagePath);
            var payloadBytes = string.IsNullOrWhiteSpace(normalizedPackagePath)
                ? null
                : ReadEntryBytes(archive, normalizedPackagePath);

            presentation.RecordingMediaArtifacts.Add(new PresentationRecordingMediaArtifact(
                kind,
                slideIndex,
                element.Attribute("suggestedFileName")?.Value ?? string.Empty,
                element.Attribute("contentType")?.Value ?? string.Empty,
                normalizedPackagePath,
                length,
                element.Attribute("contentSha256")?.Value ?? string.Empty,
                durationMs,
                element.Attribute("capturedByHost")?.Value ?? string.Empty,
                element.Attribute("statusText")?.Value ?? string.Empty,
                payloadBytes));
        }
    }

    private static string NormalizeZipPath(string packagePath) =>
        packagePath.Replace('\\', '/').TrimStart('/');

    private static PptxPackageSnapshot CapturePackageSnapshot(ZipArchive archive)
    {
        var entries = new List<KeyValuePair<string, byte[]>>();
        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrWhiteSpace(entry.FullName) || entry.FullName.EndsWith('/'))
                continue;

            try
            {
                using var source = entry.Open();
                using var ms = new MemoryStream();
                source.CopyTo(ms);
                entries.Add(new KeyValuePair<string, byte[]>(entry.FullName, ms.ToArray()));
            }
            catch
            {
                // Preserve-bag capture should not prevent semantic import.
            }
        }

        return new PptxPackageSnapshot(entries);
    }

    private static bool TryReadPackageEntry(ZipArchive archive, string path, out byte[] bytes)
    {
        bytes = Array.Empty<byte>();
        var entry = archive.GetEntry(path);
        if (entry is null)
            return false;

        using var source = entry.Open();
        using var ms = new MemoryStream();
        source.CopyTo(ms);
        bytes = ms.ToArray();
        return bytes.Length > 0;
    }

    // ── Sections ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Parses p14:sectionLst from the presentation.xml extLst and populates
    /// <see cref="Presentation.Sections"/>.  <paramref name="sldIdToRId"/> maps the numeric
    /// sldId integer (as string) to the relationship id so sections can reference Slide.Id.
    /// </summary>
    private static void ReadSections(
        XElement presRoot,
        Dictionary<string, string> sldIdToRId,
        Presentation presentation)
    {
        var extLst = presRoot.Element(P + "extLst");
        if (extLst is null) return;

        foreach (var ext in extLst.Elements(P + "ext"))
        {
            if (ext.Attribute("uri")?.Value != SectionExtUri) continue;

            var sectionLst = ext.Element(P14 + "sectionLst");
            if (sectionLst is null) continue;

            foreach (var sectionEl in sectionLst.Elements(P14 + "section"))
            {
                var section = new PresentationSection
                {
                    Name = sectionEl.Attribute("name")?.Value ?? string.Empty,
                    Id   = sectionEl.Attribute("id")?.Value   ?? Guid.NewGuid().ToString("B").ToUpperInvariant(),
                };

                // p14:sldIdLst holds the slide ids belonging to this section.
                var sldIdLstEl = sectionEl.Element(P14 + "sldIdLst");
                if (sldIdLstEl is not null)
                {
                    foreach (var sldIdEl in sldIdLstEl.Elements(P14 + "sldId"))
                    {
                        // The id= attribute is the numeric sldId integer (same as p:sldId id=).
                        // Translate it to the rId (Slide.Id key space) via the map built from
                        // p:sldIdLst so that section.SlideIds values match Slide.Id exactly.
                        // Dangling references (numeric id not in any p:sldId) are dropped.
                        var numId = sldIdEl.Attribute("id")?.Value;
                        if (!string.IsNullOrWhiteSpace(numId) &&
                            sldIdToRId.TryGetValue(numId, out var rId))
                            section.SlideIds.Add(rId);
                    }
                }

                presentation.Sections.Add(section);
            }
            break; // only one sectionLst ext expected
        }
    }

    // ── Comment authors ──────────────────────────────────────────────────────────

    /// <summary>
    /// Reads ppt/commentAuthors.xml and returns a map of authorId → (name, initials).
    /// </summary>
    private static void ReadCustomShows(
        XElement presRoot,
        Dictionary<string, string> sldIdToRId,
        Presentation presentation)
    {
        var customShowList = presRoot.Element(P + "custShowLst");
        if (customShowList is null)
        {
            return;
        }

        var validSlideIds = sldIdToRId.Values.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var customShowEl in customShowList.Elements(P + "custShow"))
        {
            var customShow = new PresentationCustomShow
            {
                Name = customShowEl.Attribute("name")?.Value ?? string.Empty,
                Id = uint.TryParse(
                    customShowEl.Attribute("id")?.Value,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var parsedId)
                    ? parsedId
                    : (uint)presentation.CustomShows.Count,
            };

            var slideListEl = customShowEl.Element(P + "sldLst");
            if (slideListEl is not null)
            {
                foreach (var slideEl in slideListEl.Elements(P + "sld"))
                {
                    var resolvedSlideId = ResolveCustomShowSlideId(slideEl, sldIdToRId, validSlideIds);
                    if (resolvedSlideId is not null)
                    {
                        customShow.SlideIds.Add(resolvedSlideId);
                    }
                }
            }

            presentation.CustomShows.Add(customShow);
        }
    }

    private static string? ResolveCustomShowSlideId(
        XElement slideEl,
        Dictionary<string, string> sldIdToRId,
        HashSet<string> validSlideIds)
    {
        var relId = slideEl.Attribute(R + "id")?.Value;
        if (!string.IsNullOrWhiteSpace(relId) && validSlideIds.Contains(relId))
        {
            return relId;
        }

        var numericId = slideEl.Attribute("id")?.Value;
        if (!string.IsNullOrWhiteSpace(numericId) && sldIdToRId.TryGetValue(numericId, out var mappedRelId))
        {
            return mappedRelId;
        }

        return null;
    }

    private static Dictionary<int, (string name, string initials)> ReadCommentAuthors(
        ZipArchive archive, string path)
    {
        var result = new Dictionary<int, (string name, string initials)>();
        var xml = OpcXml.TryLoadXml(archive, path);
        if (xml?.Root is null) return result;

        foreach (var cmAuthorEl in xml.Root.Elements(P + "cmAuthor"))
        {
            if (!int.TryParse(cmAuthorEl.Attribute("id")?.Value, out var id)) continue;
            var name     = cmAuthorEl.Attribute("name")?.Value     ?? string.Empty;
            var initials = cmAuthorEl.Attribute("initials")?.Value ?? string.Empty;
            result[id] = (name, initials);
        }

        return result;
    }

    private static Dictionary<string, ModernCommentAuthor> ReadModernCommentAuthors(
        ZipArchive archive, string path)
    {
        var result = new Dictionary<string, ModernCommentAuthor>(StringComparer.OrdinalIgnoreCase);
        var xml = OpcXml.TryLoadXml(archive, path);
        if (xml?.Root is null) return result;

        foreach (var authorEl in xml.Root.Elements(P188 + "author"))
        {
            var id = authorEl.Attribute("id")?.Value;
            if (string.IsNullOrWhiteSpace(id)) continue;

            var name = authorEl.Attribute("name")?.Value ?? string.Empty;
            var initials = authorEl.Attribute("initials")?.Value ?? string.Empty;
            var userId = authorEl.Attribute("userId")?.Value ?? string.Empty;
            var providerId = authorEl.Attribute("providerId")?.Value ?? string.Empty;
            result[id] = new ModernCommentAuthor(id, name, initials, userId, providerId);
        }

        return result;
    }

    // ── Slide comments ───────────────────────────────────────────────────────────

    /// <summary>
    /// Reads a ppt/comments/commentN.xml part and appends parsed comments to
    /// <paramref name="comments"/>.  Author names/initials are resolved from
    /// <paramref name="authorMap"/>.
    /// </summary>
    private static void ReadSlideComments(
        ZipArchive archive,
        string path,
        Dictionary<int, (string name, string initials)> authorMap,
        List<SlideComment> comments)
    {
        var xml = OpcXml.TryLoadXml(archive, path);
        if (xml?.Root is null) return;

        foreach (var cmEl in xml.Root.Elements(P + "cm"))
        {
            if (!int.TryParse(cmEl.Attribute("authorId")?.Value, out var authorId)) authorId = 0;
            if (!int.TryParse(cmEl.Attribute("idx")?.Value, out var idx)) idx = 0;

            // BB6: don't silently fabricate an empty author when authorId is not in the map.
            // Preserve the numeric id as a placeholder so identity is not destroyed on round-trip.
            if (!authorMap.TryGetValue(authorId, out var author))
                author = ($"Author {authorId}", $"A{authorId}");

            DateTime? dt = null;
            var dtStr = cmEl.Attribute("dt")?.Value;
            if (!string.IsNullOrWhiteSpace(dtStr) &&
                System.DateTime.TryParse(dtStr, null, System.Globalization.DateTimeStyles.RoundtripKind, out var parsed))
                dt = parsed;

            var posEl = cmEl.Element(P + "pos");
            long x = ParseLong(posEl?.Attribute("x")?.Value);
            long y = ParseLong(posEl?.Attribute("y")?.Value);

            var text = cmEl.Element(P + "text")?.Value ?? string.Empty;

            comments.Add(new SlideComment
            {
                AuthorId = authorId,
                Author   = author.name,
                Initials = author.initials,
                Text     = text,
                DateTime = dt,
                Xemu     = x,
                Yemu     = y,
                Idx      = idx,
            });
        }
    }

    private static void ReadModernSlideComments(
        ZipArchive archive,
        string path,
        Dictionary<string, ModernCommentAuthor> authorMap,
        List<SlideComment> comments)
    {
        var xml = OpcXml.TryLoadXml(archive, path);
        if (xml?.Root is null) return;

        foreach (var cmEl in xml.Root.Elements(P188 + "cm"))
        {
            var authorId = cmEl.Attribute("authorId")?.Value ?? string.Empty;
            var author = ResolveModernAuthor(authorMap, authorId);
            var created = ParseDateTime(cmEl.Attribute("created")?.Value);
            var anchorEl = ReadModernAnchorElement(cmEl);
            var posEl = cmEl.Element(P188 + "pos");
            var comment = new SlideComment
            {
                Author = author.Name,
                Initials = author.Initials,
                Text = ReadModernCommentText(cmEl),
                DateTime = created,
                IsResolved = string.Equals(cmEl.Attribute("status")?.Value, "resolved", StringComparison.OrdinalIgnoreCase),
                Xemu = ParseLong(posEl?.Attribute("x")?.Value),
                Yemu = ParseLong(posEl?.Attribute("y")?.Value),
                Idx = comments.Count + 1,
                UsesModernCommentSchema = true,
                ModernCommentId = cmEl.Attribute("id")?.Value ?? string.Empty,
                ModernAuthorId = author.Id,
                ModernAuthorUserId = author.UserId,
                ModernAuthorProviderId = author.ProviderId,
                ModernAnchorKind = anchorEl?.Name.LocalName ?? string.Empty,
                ModernAnchorXml = anchorEl?.ToString(SaveOptions.DisableFormatting) ?? string.Empty,
            };

            foreach (var replyEl in cmEl.Element(P188 + "replyLst")?.Elements(P188 + "reply") ?? [])
            {
                var replyAuthorId = replyEl.Attribute("authorId")?.Value ?? string.Empty;
                var replyAuthor = ResolveModernAuthor(authorMap, replyAuthorId);
                comment.Replies.Add(new SlideCommentReply
                {
                    ModernReplyId = replyEl.Attribute("id")?.Value ?? string.Empty,
                    ModernAuthorId = replyAuthor.Id,
                    ModernAuthorUserId = replyAuthor.UserId,
                    ModernAuthorProviderId = replyAuthor.ProviderId,
                    Author = replyAuthor.Name,
                    Initials = replyAuthor.Initials,
                    Text = ReadModernCommentText(replyEl),
                    DateTime = ParseDateTime(replyEl.Attribute("created")?.Value),
                });
            }

            comments.Add(comment);
        }
    }

    private static XElement? ReadModernAnchorElement(XElement commentEl)
        => commentEl.Elements()
            .FirstOrDefault(IsModernCommentAnchorElement);

    private static bool IsModernCommentAnchorElement(XElement element)
        => element.Name.LocalName is
            "unknownAnchor" or
            "sldMkLst" or
            "deMkLst" or
            "txMkLst";

    private static ModernCommentAuthor ResolveModernAuthor(
        Dictionary<string, ModernCommentAuthor> authorMap,
        string authorId)
    {
        if (!string.IsNullOrWhiteSpace(authorId) && authorMap.TryGetValue(authorId, out var author))
            return author;

        var suffix = string.IsNullOrWhiteSpace(authorId) ? "unknown" : authorId.Trim('{', '}');
        return new ModernCommentAuthor(authorId, $"Author {suffix}", "A", string.Empty, string.Empty);
    }

    private static string ReadModernCommentText(XElement commentEl)
        => string.Concat(commentEl
            .Element(P188 + "txBody")?
            .Descendants(A + "t")
            .Select(text => text.Value) ?? []);

    private static DateTime? ParseDateTime(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return DateTime.TryParse(
            value,
            null,
            DateTimeStyles.RoundtripKind,
            out var parsed)
            ? parsed
            : null;
    }

    // ── Theme ────────────────────────────────────────────────────────────────────

    private static PresentationTheme? ReadTheme(ZipArchive archive, string masterPath)
    {
        var sharedTheme = DrawingMlThemeReader.TryReadThemePart(archive, masterPath, "ppt/theme/theme1.xml");
        if (sharedTheme is null)
            return null;

        var theme = new PresentationTheme
        {
            Name = sharedTheme.Name ?? "Office Theme"
        };

        foreach (var (slot, _) in DrawingMlThemeColorSlotMapper.ColorSchemeElements)
        {
            if (sharedTheme.ColorScheme[slot] is { } color)
                theme.ColorScheme[ToPresentationSlot(slot)] = new SrgbColor(
                    color.ResolvedColor.R,
                    color.ResolvedColor.G,
                    color.ResolvedColor.B);
        }

        theme.FontScheme.MajorLatinFont = sharedTheme.FontScheme.MajorLatinTypeface ?? theme.FontScheme.MajorLatinFont;
        theme.FontScheme.MinorLatinFont = sharedTheme.FontScheme.MinorLatinTypeface ?? theme.FontScheme.MinorLatinFont;
        return theme;
    }

    private static ThemeColorSlot ToPresentationSlot(DrawingMlThemeColorSlot slot) =>
        slot switch
        {
            DrawingMlThemeColorSlot.Dark1 => ThemeColorSlot.Dk1,
            DrawingMlThemeColorSlot.Light1 => ThemeColorSlot.Lt1,
            DrawingMlThemeColorSlot.Dark2 => ThemeColorSlot.Dk2,
            DrawingMlThemeColorSlot.Light2 => ThemeColorSlot.Lt2,
            DrawingMlThemeColorSlot.Accent1 => ThemeColorSlot.Accent1,
            DrawingMlThemeColorSlot.Accent2 => ThemeColorSlot.Accent2,
            DrawingMlThemeColorSlot.Accent3 => ThemeColorSlot.Accent3,
            DrawingMlThemeColorSlot.Accent4 => ThemeColorSlot.Accent4,
            DrawingMlThemeColorSlot.Accent5 => ThemeColorSlot.Accent5,
            DrawingMlThemeColorSlot.Accent6 => ThemeColorSlot.Accent6,
            DrawingMlThemeColorSlot.Hyperlink => ThemeColorSlot.HLink,
            DrawingMlThemeColorSlot.FollowedHyperlink => ThemeColorSlot.FolHLink,
            _ => ThemeColorSlot.Dk1
        };

    // ── Slide Master ─────────────────────────────────────────────────────────────

    private static (SlideMaster master, PresentationTheme? theme) ReadSlideMaster(
        ZipArchive archive, string masterPath, string masterId)
    {
        var master = new SlideMaster { Id = masterId };
        var theme = ReadTheme(archive, masterPath);

        var xml = OpcXml.TryLoadXml(archive, masterPath);
        if (xml?.Root is null) return (master, theme);

        var scheme = theme?.ColorScheme ?? PresentationColorScheme.CreateDefault();

        var bg = xml.Root.Element(P + "cSld")?.Element(P + "bg");
        if (bg is not null) master.Background = ReadBackground(bg, scheme);

        var spTree = xml.Root.Element(P + "cSld")?.Element(P + "spTree");
        if (spTree is not null)
        {
            foreach (var shape in ReadShapesFromTree(spTree, archive, masterPath, scheme))
                master.Placeholders.Add(shape);
        }

        // p:txStyles — master default text styles per placeholder category
        var txStyles = xml.Root.Element(P + "txStyles");
        if (txStyles is not null)
        {
            master.TextStyles = new MasterTextStyles();
            ReadTextStyleLevels(txStyles.Element(P + "titleStyle"), master.TextStyles.TitleStyle, scheme);
            ReadTextStyleLevels(txStyles.Element(P + "bodyStyle"),  master.TextStyles.BodyStyle,  scheme);
            ReadTextStyleLevels(txStyles.Element(P + "otherStyle"), master.TextStyles.OtherStyle, scheme);
        }

        // p:clrMap — master color role mapping
        var clrMap = xml.Root.Element(P + "clrMap");
        if (clrMap is not null)
        {
            master.ColorMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var attr in clrMap.Attributes())
            {
                if (attr.IsNamespaceDeclaration) continue;
                master.ColorMap[attr.Name.LocalName] = attr.Value;
            }
        }

        return (master, theme);
    }

    // ── p:txStyles parsing ────────────────────────────────────────────────────────

    /// <summary>
    /// Parses a p:titleStyle / p:bodyStyle / p:otherStyle element (or any element that holds
    /// a:lvl1pPr .. a:lvl9pPr) into a <see cref="TextStyleLevels"/> instance.
    /// </summary>
    private static void ReadTextStyleLevels(XElement? styleEl, TextStyleLevels levels, PresentationColorScheme scheme)
    {
        if (styleEl is null) return;
        for (int i = 1; i <= 9; i++)
        {
            var lvlEl = styleEl.Element(A + $"lvl{i}pPr");
            if (lvlEl is null) continue;
            levels[i - 1] = ReadTextStyleLevel(lvlEl, scheme);
        }
    }

    private static TextStyleLevel ReadTextStyleLevel(XElement lvlEl, PresentationColorScheme scheme)
    {
        var level = new TextStyleLevel();

        // Paragraph-level attributes
        var algnStr = lvlEl.Attribute("algn")?.Value;
        if (!string.IsNullOrWhiteSpace(algnStr))
            level.Align = algnStr switch
            {
                "ctr"  => TextAlign.Center,
                "r"    => TextAlign.Right,
                "just" => TextAlign.Justify,
                "dist" => TextAlign.Distributed,
                "l"    => TextAlign.Left,
                _      => (TextAlign?)null
            };

        level.RightToLeft = ParseNullableBoolean(lvlEl.Attribute("rtl")?.Value);

        if (ParseLongNullable(lvlEl.Attribute("marL")?.Value) is { } ml)  level.MarginLeftEmu = ml;
        if (ParseLongNullable(lvlEl.Attribute("indent")?.Value) is { } ind) level.IndentEmu    = ind;

        // Bullet
        if (lvlEl.Element(A + "buNone") is not null)
            level.BulletKind = BulletKind.None;
        else if (lvlEl.Element(A + "buChar") is { } buChar)
        {
            level.BulletKind = BulletKind.Char;
            level.BulletChar = buChar.Attribute("char")?.Value ?? "•";
        }
        else if (lvlEl.Element(A + "buAutoNum") is { } buAutoNum2)
        {
            level.BulletKind = BulletKind.Auto;
            level.AutoNumType = ParseAutoNumType(buAutoNum2.Attribute("type")?.Value);
        }

        // Wave 19A: extended bullet style fields
        if (lvlEl.Element(A + "buClrTx") is not null)
        {
            level.BulletColorFollowsText = true;
            level.BulletColor = null;
        }
        else if (lvlEl.Element(A + "buClr") is { } buClrL)
        {
            level.BulletColor = PptxColorReader.TryReadColor(buClrL, scheme);
        }

        if (lvlEl.Element(A + "buSzTx") is not null)
        {
            level.BulletSizeFollowsText = true;
            level.BulletSizePt = null;
            level.BulletSizePct = null;
        }
        else if (lvlEl.Element(A + "buSzPts") is { } buSzPtsL &&
                 int.TryParse(buSzPtsL.Attribute("val")?.Value, out var szPtsL) &&
                 szPtsL > 0)
        {
            level.BulletSizePt = szPtsL / 100.0;
            level.BulletSizePct = null;
        }
        else if (lvlEl.Element(A + "buSzPct") is { } buSzPctL &&
                 int.TryParse(buSzPctL.Attribute("val")?.Value, out var szPctL))
        {
            level.BulletSizePct = szPctL;
        }

        if (lvlEl.Element(A + "buFontTx") is not null)
        {
            level.BulletFontFollowsText = true;
            level.BulletFontFamily = null;
        }
        else if (lvlEl.Element(A + "buFont") is { } buFontL)
        {
            level.BulletFontFamily = buFontL.Attribute("typeface")?.Value;
        }

        // a:defRPr — default run properties
        var defRPr = lvlEl.Element(A + "defRPr");
        if (defRPr is not null)
        {
            if (int.TryParse(defRPr.Attribute("sz")?.Value,
                    System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture, out var sz) && sz > 0)
                level.FontSizePt = sz / 100.0;

            var bVal = defRPr.Attribute("b")?.Value;
            if (bVal is "1" or "true")  level.Bold = true;
            else if (bVal is "0" or "false") level.Bold = false;

            var iVal = defRPr.Attribute("i")?.Value;
            if (iVal is "1" or "true")  level.Italic = true;
            else if (iVal is "0" or "false") level.Italic = false;

            var solidFill = defRPr.Element(A + "solidFill");
            if (solidFill is not null)
                level.Color = PptxColorReader.TryReadColor(solidFill, scheme);

            level.LatinFont = defRPr.Element(A + "latin")?.Attribute("typeface")?.Value;
        }

        return level;
    }

    // ── Slide Layout ─────────────────────────────────────────────────────────────

    private static SlideLayout ReadSlideLayout(
        ZipArchive archive, string layoutPath, string layoutId, string masterId, PresentationColorScheme scheme)
    {
        var layout = new SlideLayout { Id = layoutId, MasterId = masterId, PartPath = layoutPath };

        var xml = OpcXml.TryLoadXml(archive, layoutPath);
        if (xml?.Root is null) return layout;

        layout.Name = xml.Root.Element(P + "cSld")?.Attribute("name")?.Value ?? string.Empty;
        layout.LayoutType = MapLayoutType(xml.Root.Attribute("type")?.Value);

        var bg = xml.Root.Element(P + "cSld")?.Element(P + "bg");
        if (bg is not null) layout.Background = ReadBackground(bg, scheme);

        var spTree = xml.Root.Element(P + "cSld")?.Element(P + "spTree");
        if (spTree is not null)
        {
            foreach (var shape in ReadShapesFromTree(spTree, archive, layoutPath, scheme))
                layout.Placeholders.Add(shape);
        }

        return layout;
    }

    // ── Slide ────────────────────────────────────────────────────────────────────

    private static Slide ReadSlide(
        ZipArchive archive, string slidePath, string slideId,
        PresentationColorScheme scheme, List<SlideLayout> layouts,
        Dictionary<string, TableStyleData>? tableStyles = null,
        List<Slide>? allSlides = null,
        IReadOnlyDictionary<string, string>? slidePartPathToId = null)
    {
        var slide = new Slide { Id = slideId };

        var xml = OpcXml.TryLoadXml(archive, slidePath);
        if (xml?.Root is null) return slide;

        slide.IsHidden = xml.Root.Attribute("show")?.Value is { } show &&
            (show == "0" || string.Equals(show, "false", StringComparison.OrdinalIgnoreCase));

        // Layout via rels
        var slideRels = OpcRelationships.LoadTargets(archive, GetRelationshipPartPath(slidePath));
        var layoutTarget = OpcRelationships.FirstTargetByType(slideRels, SlideLayoutRelType);
        if (layoutTarget is not null)
        {
            var layoutPath = ResolveRelativeZipPath(GetDirectoryName(slidePath), layoutTarget);
            // Match with our loaded layouts by exact normalized path (PartPath).
            slide.LayoutId = MatchLayoutIdByPath(layoutPath, layouts);
        }

        var bg = xml.Root.Element(P + "cSld")?.Element(P + "bg");
        if (bg is not null) slide.Background = ReadBackground(bg, scheme);

        var slideDir = GetDirectoryName(slidePath);
        var spTree = xml.Root.Element(P + "cSld")?.Element(P + "spTree");
        if (spTree is not null)
        {
            foreach (var shape in ReadShapesFromTree(spTree, archive, slidePath, scheme, tableStyles, slideRels, allSlides, slideDir, slidePartPathToId))
                slide.Shapes.Add(shape);
        }

        // Transition — may be a plain p:transition (legacy) or wrapped in mc:AlternateContent (modern).
        // AC1: resolve mc:AlternateContent → use mc:Choice p:transition (has p14:dur); fall back to
        // mc:Fallback p:transition or a bare p:transition for files written without the extension.
        slide.Transition = ResolveTransitionEl(xml.Root, archive, slideRels, slidePath);

        // Animations (main sequence only)
        var timingEl = xml.Root.Element(P + "timing");
        if (timingEl is not null)
        {
            ReadAnimations(timingEl, slide);
            slide.AnimationBuildListXml = timingEl.Element(P + "bldLst")?.ToString(SaveOptions.DisableFormatting);
        }

        // Speaker notes — follow notesSlide relationship if present
        var notesTarget = OpcRelationships.FirstTargetByType(slideRels, NotesSlideRelType);
        if (notesTarget is not null)
        {
            var notesPath = ResolveRelativeZipPath(GetDirectoryName(slidePath), notesTarget);
            slide.Notes = ReadNotesSlide(archive, notesPath, scheme);
        }

        // p:hf — header/footer visibility flags
        var hfEl = xml.Root.Element(P + "hf");
        if (hfEl is not null)
        {
            slide.HfVisibility = new HfFlags
            {
                ShowFooter   = hfEl.Attribute("ftr")?.Value    is not "0",
                ShowDate     = hfEl.Attribute("dt")?.Value     is not "0",
                ShowSlideNum = hfEl.Attribute("sldNum")?.Value is not "0",
                ShowHeader   = hfEl.Attribute("hdr")?.Value    is "1" or "true",
            };
        }

        // p:clrMapOvr — per-slide color map override
        // <p:clrMapOvr><a:overrideClrMapping .../></p:clrMapOvr>  → override map
        // <p:clrMapOvr><a:masterClrMapping/></p:clrMapOvr>        → use master (null)
        var clrMapOvrEl = xml.Root.Element(P + "clrMapOvr");
        if (clrMapOvrEl is not null)
        {
            var overrideEl = clrMapOvrEl.Element(A + "overrideClrMapping");
            if (overrideEl is not null)
            {
                slide.ColorMapOverride = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var attr in overrideEl.Attributes())
                {
                    if (attr.IsNamespaceDeclaration) continue;
                    slide.ColorMapOverride[attr.Name.LocalName] = attr.Value;
                }
            }
            // <a:masterClrMapping/> → leave ColorMapOverride null (inherit master map).
        }

        return slide;
    }

    // ── Notes slide ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Reads the body placeholder txBody from a ppt/notesSlides/notesSlideN.xml part.
    /// Returns null when the part is missing or contains no body placeholder.
    /// Tolerates slides with no notes by returning null without throwing.
    /// </summary>
    private static TextBody? ReadNotesSlide(ZipArchive archive, string notesPath, PresentationColorScheme scheme)
    {
        var xml = OpcXml.TryLoadXml(archive, notesPath);
        if (xml?.Root is null) return null;

        // p:notes/p:cSld/p:spTree contains shape elements.
        // The body placeholder (p:ph type="body") holds the notes text.
        var spTree = xml.Root.Element(P + "cSld")?.Element(P + "spTree");
        if (spTree is null) return null;

        foreach (var spEl in spTree.Elements(P + "sp"))
        {
            var ph = spEl.Element(P + "nvSpPr")?.Element(P + "nvPr")?.Element(P + "ph");
            if (ph is null) continue;

            var phType = ph.Attribute("type")?.Value;
            // body placeholder: type="body" or omitted (idx defaults to body placeholder when idx > 0 is absent)
            // We match explicitly on "body" or absent-type with idx != 0 (slide-image is usually idx=0).
            if (phType is null or "body")
            {
                // Skip the slide-image placeholder (idx="0" without type or type="sldImg").
                var idxStr = ph.Attribute("idx")?.Value;
                if (string.IsNullOrEmpty(idxStr) && phType is null) continue; // slide-image: no type, no idx or idx=0

                var txBody = spEl.Element(P + "txBody");
                if (txBody is null) continue;

                var body = ReadTxBody(txBody, scheme);
                // Only treat as notes if there is actual text (ignore fully empty bodies).
                if (body.Paragraphs.Count == 0 ||
                    body.Paragraphs.All(para => para.Runs.Count == 0 || para.Runs.All(r => string.IsNullOrEmpty(r.Text))))
                    return null;

                return body;
            }
        }

        return null;
    }

    /// <summary>
    /// Matches a slide to its layout by exact normalized OPC part path.
    /// Falls back to the first layout if no exact match (should not happen in well-formed packages).
    /// </summary>
    private static string? MatchLayoutIdByPath(string layoutPath, List<SlideLayout> layouts)
    {
        if (layouts.Count == 0) return null;
        // Primary: exact path match against the stored PartPath.
        var match = layouts.Find(l => string.Equals(l.PartPath, layoutPath, StringComparison.OrdinalIgnoreCase));
        if (match is not null) return match.Id;
        // Fallback: match by file name only (handles minor path normalization differences).
        var seg = layoutPath.Split('/').Last();
        match = layouts.Find(l => string.Equals(l.PartPath.Split('/').Last(), seg, StringComparison.OrdinalIgnoreCase));
        if (match is not null) return match.Id;
        return layouts[0].Id;
    }

    // ── Shape tree ───────────────────────────────────────────────────────────────

    private static IEnumerable<SlideShape> ReadShapesFromTree(
        XElement spTree, ZipArchive archive, string partPath, PresentationColorScheme scheme,
        Dictionary<string, TableStyleData>? tableStyles = null,
        IReadOnlyList<OpcRelationshipTarget>? slideRels = null,
        List<Slide>? allSlides = null,
        string? slideDir = null,
        IReadOnlyDictionary<string, string>? slidePartPathToId = null)
    {
        foreach (var child in spTree.Elements())
        {
            // Theme 21: mc:AlternateContent at shape-tree level wraps OLE objects (and some
            // modern elements that older PowerPoint cannot read).  We look for the first
            // recognisable element in mc:Choice; if none found we fall back to mc:Fallback.
            var effectiveEl = child;
            XElement? mcChoiceEl = null;   // EA3: capture mc:Choice for Requires token
            XElement? mcFallbackEl = null;
            if (child.Name == MC + "AlternateContent")
            {
                mcChoiceEl = child.Element(MC + "Choice");
                mcFallbackEl = child.Element(MC + "Fallback")?.Elements().FirstOrDefault();
                var choiceChild = mcChoiceEl?.Elements().FirstOrDefault();
                if (choiceChild is not null)
                    effectiveEl = choiceChild;
                else
                {
                    var fallbackChild = child.Element(MC + "Fallback")?.Elements().FirstOrDefault();
                    if (fallbackChild is not null)
                        effectiveEl = fallbackChild;
                }
            }

            SlideShape? shape = effectiveEl.Name.LocalName switch
            {
                "sp"           => ReadSp(effectiveEl, scheme, slideRels, allSlides, slideDir, slidePartPathToId, archive, partPath),
                "pic"          => ReadPic(effectiveEl, archive, partPath, scheme,
                                      slideRels, allSlides, slideDir, slidePartPathToId),
                "cxnSp"        => ReadCxnSp(effectiveEl, scheme, slideRels, allSlides, slideDir, slidePartPathToId, archive, partPath),
                "grpSp"        => ReadGrpSp(effectiveEl, archive, partPath, scheme, tableStyles, slideRels, allSlides, slideDir, slidePartPathToId),
                // EA3: pass mcChoiceEl so ReadGraphicFrame can capture the Requires token
                "graphicFrame" => ReadGraphicFrame(effectiveEl, archive, partPath, scheme, tableStyles,
                                      slideRels, allSlides, slideDir, slidePartPathToId,
                                      wasAlternateContent: child != effectiveEl, mcChoiceEl: mcChoiceEl,
                                      alternateContentFallbackXml: mcFallbackEl?.ToString(SaveOptions.DisableFormatting)),
                // Wave 25A: ink annotations arrive as p:contentPart (possibly inside mc:AlternateContent)
                "contentPart"  => ReadContentPartInk(child, effectiveEl, archive, partPath, mcChoiceEl),
                _ => null
            };

            if (shape is not null)
                yield return shape;
        }
    }

    // ── Hyperlink resolution from slide rels ─────────────────────────────────────

    /// <summary>
    /// Resolves an <c>a:hlinkClick</c> element from a slide's relationship list into a
    /// <see cref="Hyperlink"/> model object.
    /// External hyperlinks use TargetMode="External" and rel type .../hyperlink.
    /// Internal slide jumps use rel type .../slide (with an optional action attribute).
    /// Returns null when the rId is missing or the rels list is null.
    /// </summary>
    /// <param name="slideDir">
    /// Directory of the slide part (e.g. "ppt/slides") used to resolve relative rel targets
    /// to absolute OPC part paths when <paramref name="slidePartPathToId"/> is provided.
    /// </param>
    /// <param name="slidePartPathToId">
    /// Maps absolute normalized OPC part paths (e.g. "ppt/slides/slide3.xml") to their
    /// Slide.Id (= presentation-level rId). When supplied, internal slide-jump targets are
    /// resolved by part path rather than by filename digit, which is order-independent and
    /// correct for reordered decks (where slideN.xml filename ≠ presentation order).
    /// </param>
    private static Hyperlink? ResolveHlinkClick(
        XElement? hlinkEl,
        IReadOnlyList<OpcRelationshipTarget>? slideRels,
        List<Slide>? allSlides,
        string? slideDir = null,
        IReadOnlyDictionary<string, string>? slidePartPathToId = null)
    {
        if (hlinkEl is null || slideRels is null) return null;

        var rId     = hlinkEl.Attribute(R + "id")?.Value;
        var action  = hlinkEl.Attribute("action")?.Value;
        var tooltip = hlinkEl.Attribute("tooltip")?.Value;

        // action="ppaction://hlinksldjump" with empty rId is a slide-jump with a rels entry.
        // Also handle action-only with no rId.
        bool isSlideJumpAction = action?.Contains("hlinksldjump", StringComparison.OrdinalIgnoreCase) == true;

        if (!string.IsNullOrEmpty(rId))
        {
            var rel = slideRels.FirstOrDefault(r => r.Id == rId);
            if (rel == default) return null;

            if (rel.Type == HyperlinkRelType)
            {
                // External hyperlink
                return new Hyperlink { Url = rel.Target, Tooltip = tooltip };
            }

            if (rel.Type == SlideRelType || rel.Type == SlideHlinkRelType || isSlideJumpAction)
            {
                // Internal slide jump: target is a relative path like "../slides/slide3.xml".
                // Resolve by part path → Slide.Id rather than filename digit, so decks where the
                // presentation order does not match slideN.xml filenames navigate correctly.
                if (slidePartPathToId is not null && slideDir is not null)
                {
                    // Resolve the relative target against the slide's directory to get an absolute
                    // OPC part path, then look it up in the part-path→rId map.
                    var absTarget = ResolveRelativeZipPath(slideDir, rel.Target);
                    if (slidePartPathToId.TryGetValue(absTarget, out var slideId))
                        return new Hyperlink { TargetSlideId = slideId, Tooltip = tooltip };
                    // absTarget didn't match — fall through to filename-digit fallback below.
                }

                if (allSlides is not null)
                {
                    // Fallback (no map available or path not found): derive slide index from the
                    // numeric suffix of the filename. This is order-sensitive and wrong for reordered
                    // decks, but preserves the pre-fix behaviour when the map is absent.
                    var targetSeg = rel.Target.Split('/').Last(); // e.g. "slide2.xml"
                    var numStr = System.Text.RegularExpressions.Regex
                        .Match(targetSeg, @"\d+").Value;
                    if (int.TryParse(numStr, out var num) && num >= 1 && num <= allSlides.Count)
                    {
                        var targetSlide = allSlides[num - 1];
                        return new Hyperlink { TargetSlideId = targetSlide.Id, Tooltip = tooltip };
                    }
                }
                // Last resort: store the target path as the id (round-trip acceptable).
                return new Hyperlink { TargetSlideId = rel.Target, Tooltip = tooltip };
            }
        }
        else if (isSlideJumpAction)
        {
            // action with no rId — some tools write slide jumps this way; can't resolve without rId.
            // Return null; the hyperlink will be dropped rather than producing garbage.
            return null;
        }

        return null;
    }

    // ── p:graphicFrame (table, chart, etc.) ───────────────────────────────────────

    private const string DrawingTableUri =
        "http://schemas.openxmlformats.org/drawingml/2006/table";

    private const string DrawingChartUri =
        "http://schemas.openxmlformats.org/drawingml/2006/chart";

    private const string DrawingDiagramUri =
        "http://schemas.openxmlformats.org/drawingml/2006/diagram";

    // c: namespace for c:chart element inside graphicData
    private static readonly XNamespace CChart =
        "http://schemas.openxmlformats.org/drawingml/2006/chart";

    // dgm: namespace for SmartArt relId attributes inside graphicData
    private static readonly XNamespace Dgm =
        "http://schemas.openxmlformats.org/drawingml/2006/diagram";

    // dsp: namespace for dsp:drawing (SmartArt cached render)
    private static readonly XNamespace Dsp =
        "http://schemas.microsoft.com/office/drawing/2008/diagram";

    // ── OLE / math namespaces (Theme 21) ──────────────────────────────────────

    // OLE graphicData URI
    private const string DrawingOleUri =
        "http://schemas.openxmlformats.org/presentationml/2006/ole";

    // a14: namespace — hosts a14:m math element
    private static readonly XNamespace A14 =
        "http://schemas.microsoft.com/office/drawing/2010/main";

    // m: namespace — OOXML OMML (Office Math Markup Language)
    private static readonly XNamespace M =
        "http://schemas.openxmlformats.org/officeDocument/2006/math";

    // OLE relationship types
    private const string OleObjectRelType =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/oleObject";
    private const string PackageRelType =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/package";
    // MS-proprietary OLE image rel type used by some encoders
    private const string OleImageRelType =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/image";

    // Relationship types for SmartArt diagram parts
    private const string DiagramDataRelType    = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/diagramData";
    private const string DiagramLayoutRelType  = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/diagramLayout";
    private const string DiagramQuickStyleRelType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/diagramQuickStyle";
    private const string DiagramColorsRelType  = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/diagramColors";
    private const string DiagramDrawingRelType = "http://schemas.microsoft.com/office/2007/relationships/diagramDrawing";

    // ── Wave 25A: Zoom, 3D-model, and ink URIs / rel-types ───────────────────
    // InkML relationship type
    private const string InkRelType = "http://schemas.microsoft.com/office/2016/05/19/relationships/ink";

    private static SlideShape? ReadGraphicFrame(
        XElement gfEl, ZipArchive archive, string partPath,
        PresentationColorScheme scheme,
        Dictionary<string, TableStyleData>? tableStyles,
        IReadOnlyList<OpcRelationshipTarget>? slideRels = null,
        List<Slide>? allSlides = null,
        string? slideDir = null,
        IReadOnlyDictionary<string, string>? slidePartPathToId = null,
        bool wasAlternateContent = false, XElement? mcChoiceEl = null,
        string? alternateContentFallbackXml = null)
    {
        var cNvPr = gfEl.Element(P + "nvGraphicFramePr")?.Element(P + "cNvPr");

        // Read xfrm for position/size.
        var xfrmEl = gfEl.Element(P + "xfrm");
        long offX = ParseLong(xfrmEl?.Element(A + "off")?.Attribute("x")?.Value);
        long offY = ParseLong(xfrmEl?.Element(A + "off")?.Attribute("y")?.Value);
        long extCx = ParseLong(xfrmEl?.Element(A + "ext")?.Attribute("cx")?.Value);
        long extCy = ParseLong(xfrmEl?.Element(A + "ext")?.Attribute("cy")?.Value);

        // Detect graphic type from URI.
        var graphicData = gfEl
            .Element(A + "graphic")
            ?.Element(A + "graphicData");

        if (graphicData is null)
            return null;

        var uri = graphicData.Attribute("uri")?.Value;

        // ── Table ──────────────────────────────────────────────────────────────
        if (string.Equals(uri, DrawingTableUri, StringComparison.OrdinalIgnoreCase))
        {
            var tblEl = graphicData.Element(A + "tbl");
            if (tblEl is null) return null;

            var tableShape = ReadTable(tblEl, scheme, tableStyles);

            return new SlideShape
            {
                Id = ParseUint(cNvPr?.Attribute("id")?.Value),
                Name = cNvPr?.Attribute("name")?.Value ?? string.Empty,
                AlternativeTextTitle = ReadAlternativeTextTitle(cNvPr),
                AlternativeText = ReadAlternativeText(cNvPr),
                IsDecorative = ReadDecorative(cNvPr),
                IsHidden = ReadHidden(cNvPr),
                Kind = SlideShapeKind.Table,
                OffsetXEmu = offX,
                OffsetYEmu = offY,
                ExtentCxEmu = extCx,
                ExtentCyEmu = extCy,
                RotationDeg = ParseLong(xfrmEl?.Attribute("rot")?.Value) / 60000.0,
                FlipH = xfrmEl?.Attribute("flipH")?.Value is "1" or "true",
                FlipV = xfrmEl?.Attribute("flipV")?.Value is "1" or "true",
                Hyperlink = ResolveHlinkClick(cNvPr?.Element(A + "hlinkClick"), slideRels, allSlides, slideDir, slidePartPathToId),
                Table = tableShape
            };
        }

        // ── Chart ──────────────────────────────────────────────────────────────
        if (string.Equals(uri, DrawingChartUri, StringComparison.OrdinalIgnoreCase))
        {
            var chartRelId = graphicData.Element(CChart + "chart")?.Attribute(R + "id")?.Value;
            if (string.IsNullOrWhiteSpace(chartRelId)) return null;

            // Resolve the chart part path via the slide's rels
            var partRels = OpcRelationships.LoadTargets(archive, GetRelationshipPartPath(partPath));
            var chartTarget = partRels
                .FirstOrDefault(r => r.Id == chartRelId && r.Type == ChartRelType).Target;
            if (string.IsNullOrWhiteSpace(chartTarget)) return null;

            var chartPath = ResolveRelativeZipPath(GetDirectoryName(partPath), chartTarget);
            var chartShape = PptxChartReader.ReadChartPart(archive, chartPath, scheme);
            if (chartShape is null) return null;
            chartShape.SourcePartPath = chartPath;

            return new SlideShape
            {
                Id = ParseUint(cNvPr?.Attribute("id")?.Value),
                Name = cNvPr?.Attribute("name")?.Value ?? string.Empty,
                AlternativeTextTitle = ReadAlternativeTextTitle(cNvPr),
                AlternativeText = ReadAlternativeText(cNvPr),
                IsDecorative = ReadDecorative(cNvPr),
                IsHidden = ReadHidden(cNvPr),
                Kind = SlideShapeKind.Chart,
                OffsetXEmu = offX,
                OffsetYEmu = offY,
                ExtentCxEmu = extCx,
                ExtentCyEmu = extCy,
                Hyperlink = ResolveHlinkClick(cNvPr?.Element(A + "hlinkClick"), slideRels, allSlides, slideDir, slidePartPathToId),
                Chart = chartShape
            };
        }

        // ── SmartArt diagram ──────────────────────────────────────────────────
        if (string.Equals(uri, DrawingDiagramUri, StringComparison.OrdinalIgnoreCase))
        {
            var smartArt = ReadSmartArt(graphicData, archive, partPath, scheme);
            return new SlideShape
            {
                Id = ParseUint(cNvPr?.Attribute("id")?.Value),
                Name = cNvPr?.Attribute("name")?.Value ?? string.Empty,
                AlternativeTextTitle = ReadAlternativeTextTitle(cNvPr),
                AlternativeText = ReadAlternativeText(cNvPr),
                IsDecorative = ReadDecorative(cNvPr),
                IsHidden = ReadHidden(cNvPr),
                Kind = SlideShapeKind.SmartArt,
                OffsetXEmu = offX,
                OffsetYEmu = offY,
                ExtentCxEmu = extCx,
                ExtentCyEmu = extCy,
                Hyperlink = ResolveHlinkClick(cNvPr?.Element(A + "hlinkClick"), slideRels, allSlides, slideDir, slidePartPathToId),
                SmartArt = smartArt
            };
        }

        // ── OLE embedded object ────────────────────────────────────────────────
        // p:graphicFrame with uri=".../ole" or any graphicFrame containing p:oleObj
        if (string.Equals(uri, DrawingOleUri, StringComparison.OrdinalIgnoreCase)
            || graphicData.Descendants(P + "oleObj").Any())
        {
            // Find the p:oleObj element — it may be a direct child of graphicData or nested
            var oleObjEl = graphicData.Descendants(P + "oleObj").FirstOrDefault();
            if (oleObjEl is not null)
            {
                var oleShape = ReadOleObject(oleObjEl, gfEl, archive, partPath, scheme,
                    wasAlternateContent);
                if (oleShape is not null)
                {
                    oleShape.Id = ParseUint(cNvPr?.Attribute("id")?.Value);
                    oleShape.Name = cNvPr?.Attribute("name")?.Value ?? string.Empty;
                    oleShape.AlternativeTextTitle = ReadAlternativeTextTitle(cNvPr);
                    oleShape.AlternativeText = ReadAlternativeText(cNvPr);
                    oleShape.IsDecorative = ReadDecorative(cNvPr);
                    oleShape.IsHidden = ReadHidden(cNvPr);
                    oleShape.OffsetXEmu = offX;
                    oleShape.OffsetYEmu = offY;
                    oleShape.ExtentCxEmu = extCx;
                    oleShape.ExtentCyEmu = extCy;
                    var oleHlink = cNvPr?.Element(A + "hlinkClick");
                    if (oleHlink is not null)
                        oleShape.Hyperlink = ResolveHlinkClick(oleHlink, slideRels, allSlides, slideDir, slidePartPathToId);
                    return oleShape;
                }
            }
        }

        // Unknown graphicFrame type — preserve verbatim (Wave 25A: no-silent-loss guarantee).
        // EA3: pass mcChoiceEl so ReadPreservedGraphicFrame can capture the Requires token.
        var preserved = ReadPreservedGraphicFrame(gfEl, graphicData, uri, cNvPr, offX, offY, extCx, extCy,
            archive, partPath, wasAlternateContent, mcChoiceEl, alternateContentFallbackXml);
        var preservedHlink = cNvPr?.Element(A + "hlinkClick");
        if (preservedHlink is not null)
            preserved.Hyperlink = ResolveHlinkClick(preservedHlink, slideRels, allSlides, slideDir, slidePartPathToId);
        return preserved;
    }

    // ── Wave 25A: Preserved modern objects (zoom / 3D / unknown graphicFrame) ─────────

    private static bool IsZoomUri(string? uri) =>
        uri is not null && (
            uri.Contains("zoom", StringComparison.OrdinalIgnoreCase) ||
            uri.StartsWith("http://schemas.microsoft.com/office/powerpoint/2010", StringComparison.OrdinalIgnoreCase) ||
            uri.StartsWith("http://schemas.microsoft.com/office/powerpoint/2011", StringComparison.OrdinalIgnoreCase) ||
            uri.StartsWith("http://schemas.microsoft.com/office/powerpoint/2012", StringComparison.OrdinalIgnoreCase) ||
            uri.StartsWith("http://schemas.microsoft.com/office/powerpoint/2013", StringComparison.OrdinalIgnoreCase) ||
            uri.StartsWith("http://schemas.microsoft.com/office/powerpoint/2014", StringComparison.OrdinalIgnoreCase) ||
            uri.StartsWith("http://schemas.microsoft.com/office/powerpoint/2015", StringComparison.OrdinalIgnoreCase) ||
            uri.StartsWith("http://schemas.microsoft.com/office/powerpoint/2016", StringComparison.OrdinalIgnoreCase) ||
            uri.StartsWith("http://schemas.microsoft.com/office/powerpoint/2017", StringComparison.OrdinalIgnoreCase));

    private static bool Is3dModelUri(string? uri) =>
        uri is not null && uri.Contains("model3d", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// FA2: resolves the namespace URI for EACH whitespace-separated token in a (possibly
    /// multi-token) mc:Choice Requires value, e.g. "p14 p15" -> {"p14": uri1, "p15": uri2}.
    /// mc:AlternateContent permits Requires to be a space-separated list of prefixes; treating
    /// the whole raw string as a single xmlns prefix (as the old code did via
    /// GetNamespaceOfPrefix(rawRequiresValue)) silently fails for every multi-token value.
    /// A token whose xmlns declaration cannot be found (not in scope on the Choice element or
    /// its ancestors) is OMITTED from the result — callers must not guess/substitute a URI for it.
    /// </summary>
    private static Dictionary<string, string> ResolveMcRequiresNsUris(XElement mcChoiceEl, string? requiresValue)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(requiresValue)) return result;

        foreach (var token in requiresValue.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            var uri = mcChoiceEl.GetNamespaceOfPrefix(token)?.NamespaceName;
            if (!string.IsNullOrEmpty(uri))
                result[token] = uri;
        }
        return result;
    }

    /// <summary>
    /// Preserves an unknown or modern graphicFrame verbatim, capturing any referenced parts.
    /// </summary>
    private static SlideShape ReadPreservedGraphicFrame(
        XElement gfEl, XElement graphicData, string? uri,
        XElement? cNvPr, long offX, long offY, long extCx, long extCy,
        ZipArchive archive, string partPath, bool wasAlternateContent,
        XElement? mcChoiceEl = null, string? alternateContentFallbackXml = null)
    {
        var kind = IsZoomUri(uri) ? PreservedObjectKind.Zoom
                 : Is3dModelUri(uri) ? PreservedObjectKind.Model3d
                 : PreservedObjectKind.Unknown;

        var slideShapeKind = kind switch
        {
            PreservedObjectKind.Zoom    => SlideShapeKind.Zoom,
            PreservedObjectKind.Model3d => SlideShapeKind.Model3d,
            _                           => SlideShapeKind.PreservedObject,
        };

        // EA3/FA2: capture the original mc:Choice Requires token(s) and their namespace URI(s) so
        // the writer can re-emit them verbatim (not hardcode "p14"). Requires may be a
        // space-separated list of tokens (e.g. "p14 p15") — resolve each one individually.
        string? mcRequiresToken = null;
        string? mcRequiresNsUri = null;
        Dictionary<string, string>? mcRequiresNsUris = null;
        if (wasAlternateContent && mcChoiceEl is not null)
        {
            mcRequiresToken = mcChoiceEl.Attribute("Requires")?.Value;
            if (mcRequiresToken is not null)
            {
                mcRequiresNsUris = ResolveMcRequiresNsUris(mcChoiceEl, mcRequiresToken);
                // Back-compat single-value fallback: only meaningful for a genuinely single token.
                var tokenParts = mcRequiresToken.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (tokenParts.Length == 1)
                    mcRequiresNsUris.TryGetValue(tokenParts[0], out mcRequiresNsUri);
            }
        }

        var info = new PreservedObjectInfo
        {
            ObjectKind          = kind,
            ZoomTargetSlideNumericId = kind == PreservedObjectKind.Zoom
                ? ReadZoomTargetSlideNumericId(gfEl)
                : null,
            ZoomTargetSectionId = kind == PreservedObjectKind.Zoom
                ? ReadZoomTargetSectionId(gfEl)
                : null,
            ZoomProperties       = kind == PreservedObjectKind.Zoom
                ? ReadZoomObjectProperties(gfEl)
                : null,
            RawXml              = gfEl.ToString(SaveOptions.DisableFormatting),
            AlternateContentFallbackXml = alternateContentFallbackXml,
            WasAlternateContent = wasAlternateContent,
            McRequiresToken     = mcRequiresToken,
            McRequiresNsUri     = mcRequiresNsUri,
        };
        if (mcRequiresNsUris is not null)
            foreach (var kv in mcRequiresNsUris)
                info.McRequiresNsUris[kv.Key] = kv.Value;
        if (kind == PreservedObjectKind.Zoom)
            info.SummaryZoomTargets.AddRange(ReadSummaryZoomTargets(gfEl));

        // Capture all referenced parts via the slide's rels
        var slideRels2 = OpcRelationships.LoadTargets(archive, GetRelationshipPartPath(partPath));
        CaptureReferencedParts(gfEl, slideRels2, archive, partPath, info);

        // Extract fallback preview image if present (a:blip or p:pic inside graphicData or nearby)
        var fallbackImage = ExtractPreservedFallbackImage(gfEl, graphicData, slideRels2, archive, partPath);

        return new SlideShape
        {
            Id              = ParseUint(cNvPr?.Attribute("id")?.Value),
            Name            = cNvPr?.Attribute("name")?.Value ?? string.Empty,
            AlternativeTextTitle = ReadAlternativeTextTitle(cNvPr),
            AlternativeText = ReadAlternativeText(cNvPr),
            IsDecorative    = ReadDecorative(cNvPr),
            IsHidden        = ReadHidden(cNvPr),
            Kind            = slideShapeKind,
            OffsetXEmu      = offX,
            OffsetYEmu      = offY,
            ExtentCxEmu     = extCx,
            ExtentCyEmu     = extCy,
            RotationDeg     = ParseLong(gfEl.Element(P + "xfrm")?.Attribute("rot")?.Value) / 60000.0,
            FlipH           = gfEl.Element(P + "xfrm")?.Attribute("flipH")?.Value is "1" or "true",
            FlipV           = gfEl.Element(P + "xfrm")?.Attribute("flipV")?.Value is "1" or "true",
            Picture         = fallbackImage,
            PreservedObject = info,
        };
    }

    private static uint? ReadZoomTargetSlideNumericId(XElement graphicFrame)
    {
        var target = graphicFrame.Descendants()
            .FirstOrDefault(element => string.Equals(element.Name.LocalName, "sldZmObj", StringComparison.OrdinalIgnoreCase))
            ?.Attribute("sldId")?.Value;
        return uint.TryParse(target, out var slideId) ? slideId : null;
    }

    private static string? ReadZoomTargetSectionId(XElement graphicFrame) =>
        graphicFrame.Descendants()
            .FirstOrDefault(element => string.Equals(
                element.Name.LocalName, "sectionZmObj", StringComparison.OrdinalIgnoreCase))
            ?.Attribute("sectionId")?.Value;

    private static ZoomObjectProperties? ReadZoomObjectProperties(XElement graphicFrame)
    {
        var properties = graphicFrame.Descendants()
            .FirstOrDefault(element => string.Equals(element.Name.LocalName, "zmPr",
                StringComparison.OrdinalIgnoreCase));
        if (properties is null)
            return null;

        var value = new ZoomObjectProperties(
            ParseNullableBoolean(properties.Attribute("returnToParent")?.Value),
            properties.Attribute("imageType")?.Value,
            properties.Attribute("transitionDur")?.Value,
            ParseNullableBoolean(properties.Attribute("showBg")?.Value),
            ParseNullableInt(properties.Descendants().FirstOrDefault(element =>
                element.Name.LocalName == "srcRect")?.Attribute("l")?.Value),
            ParseNullableInt(properties.Descendants().FirstOrDefault(element =>
                element.Name.LocalName == "srcRect")?.Attribute("t")?.Value),
            ParseNullableInt(properties.Descendants().FirstOrDefault(element =>
                element.Name.LocalName == "srcRect")?.Attribute("r")?.Value),
            ParseNullableInt(properties.Descendants().FirstOrDefault(element =>
                element.Name.LocalName == "srcRect")?.Attribute("b")?.Value));
        return value.IsEmpty ? null : value;
    }

    private static IEnumerable<SummaryZoomTarget> ReadSummaryZoomTargets(XElement graphicFrame)
    {
        foreach (var element in graphicFrame.Descendants().Where(candidate =>
                     string.Equals(candidate.Name.LocalName, "summaryZmObj", StringComparison.OrdinalIgnoreCase)))
        {
            var sectionId = element.Attribute("sectionId")?.Value;
            if (string.IsNullOrWhiteSpace(sectionId))
                continue;

            yield return new SummaryZoomTarget(
                sectionId,
                element.Attribute("title")?.Value ?? string.Empty,
                element.Attribute("descr")?.Value ?? string.Empty,
                ParseNullableInt(element.Attribute("offsetFactorX")?.Value) ?? 0,
                ParseNullableInt(element.Attribute("offsetFactorY")?.Value) ?? 0,
                ParseNullableInt(element.Attribute("scaleFactorX")?.Value) ?? 100000,
                ParseNullableInt(element.Attribute("scaleFactorY")?.Value) ?? 100000);
        }
    }

    /// <summary>
    /// Reads a p:contentPart element (ink annotation). May be wrapped in mc:AlternateContent.
    /// The mc:Fallback branch often contains a picture fallback image.
    /// </summary>
    private static SlideShape? ReadContentPartInk(
        XElement originalEl, XElement contentPartEl,
        ZipArchive archive, string partPath,
        XElement? mcChoiceEl = null)
    {
        // Extract cNvPr from nvContentPartPr if present
        var cNvPr = contentPartEl
            .Elements()
            .FirstOrDefault(element => element.Name.LocalName == "nvContentPartPr")
            ?.Elements()
            .FirstOrDefault(element => element.Name.LocalName == "cNvPr");

        // Get xfrm from p:xfrm with a:off/a:ext
        var xfrmEl = contentPartEl.Elements()
            .FirstOrDefault(element => element.Name.LocalName == "xfrm")
                  ?? contentPartEl.Descendants()
                      .FirstOrDefault(element => element.Name.LocalName == "xfrm");
        long offX  = ParseLong(xfrmEl?.Elements().FirstOrDefault(element => element.Name.LocalName == "off")?.Attribute("x")?.Value);
        long offY  = ParseLong(xfrmEl?.Elements().FirstOrDefault(element => element.Name.LocalName == "off")?.Attribute("y")?.Value);
        long extCx = ParseLong(xfrmEl?.Elements().FirstOrDefault(element => element.Name.LocalName == "ext")?.Attribute("cx")?.Value);
        long extCy = ParseLong(xfrmEl?.Elements().FirstOrDefault(element => element.Name.LocalName == "ext")?.Attribute("cy")?.Value);

        var slideRels2 = OpcRelationships.LoadTargets(archive, GetRelationshipPartPath(partPath));

        // EA3/FA2: capture original mc:Choice Requires token(s) for round-trip fidelity.
        // Requires may be a space-separated list of tokens (e.g. "p14 p15") — resolve each
        // one individually rather than treating the whole raw string as a single prefix.
        bool wasAc = originalEl != contentPartEl;
        string? mcRequiresToken = null;
        string? mcRequiresNsUri = null;
        Dictionary<string, string>? mcRequiresNsUris = null;
        if (wasAc && mcChoiceEl is not null)
        {
            mcRequiresToken = mcChoiceEl.Attribute("Requires")?.Value;
            if (mcRequiresToken is not null)
            {
                mcRequiresNsUris = ResolveMcRequiresNsUris(mcChoiceEl, mcRequiresToken);
                var tokenParts = mcRequiresToken.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (tokenParts.Length == 1)
                    mcRequiresNsUris.TryGetValue(tokenParts[0], out mcRequiresNsUri);
            }
        }

        var info = new PreservedObjectInfo
        {
            ObjectKind          = PreservedObjectKind.Ink,
            RawXml              = contentPartEl.ToString(SaveOptions.DisableFormatting),
            WasAlternateContent = wasAc,
            McRequiresToken     = mcRequiresToken,
            McRequiresNsUri     = mcRequiresNsUri,
        };
        if (mcRequiresNsUris is not null)
            foreach (var kv in mcRequiresNsUris)
                info.McRequiresNsUris[kv.Key] = kv.Value;

        // Follow r:id to the InkML part and capture its bytes
        var rId = contentPartEl.Attribute(R + "id")?.Value;
        if (!string.IsNullOrEmpty(rId))
        {
            var rel = slideRels2.FirstOrDefault(r => r.Id == rId);
            if (rel != default)
            {
                var inkPath = ResolveRelativeZipPath(GetDirectoryName(partPath), rel.Target);
                info.SlideRels[rId] = (rel.Type, inkPath);
                CapturePartBytes(inkPath, archive, info);
            }
        }

        // Try to extract fallback image from mc:Fallback
        ImagePart? fallback = null;
        if (originalEl != contentPartEl)
        {
            // originalEl is the mc:AlternateContent — look for a pic in mc:Fallback
            var fallbackEl = originalEl.Element(MC + "Fallback");
            if (fallbackEl is not null)
            {
                var blipEl = fallbackEl.Descendants(A + "blip").FirstOrDefault();
                var imgRelId = blipEl?.Attribute(R + "embed")?.Value;
                if (!string.IsNullOrEmpty(imgRelId))
                {
                    var imgRel = slideRels2.FirstOrDefault(r => r.Id == imgRelId);
                    if (imgRel != default)
                    {
                        var imgPath = ResolveRelativeZipPath(GetDirectoryName(partPath), imgRel.Target);
                        var imgBytes = ReadEntryBytes(archive, imgPath);
                        if (imgBytes is not null)
                        {
                            fallback = new ImagePart
                            {
                                Bytes       = imgBytes,
                                ContentType = GuessPreservedContentType(imgPath),
                            };
                        }
                    }
                }
            }
        }

        return new SlideShape
        {
            Id              = ParseUint(cNvPr?.Attribute("id")?.Value),
            Name            = cNvPr?.Attribute("name")?.Value ?? string.Empty,
            AlternativeTextTitle = ReadAlternativeTextTitle(cNvPr),
            AlternativeText = ReadAlternativeText(cNvPr),
            IsDecorative    = ReadDecorative(cNvPr),
            IsHidden        = ReadHidden(cNvPr),
            Kind            = SlideShapeKind.Ink,
            OffsetXEmu      = offX,
            OffsetYEmu      = offY,
            ExtentCxEmu     = extCx,
            ExtentCyEmu     = extCy,
            Picture         = fallback,
            PreservedObject = info,
        };
    }

    /// <summary>
    /// Walks all r:id / r:embed / r:link attributes in <paramref name="el"/> and captures
    /// the referenced OPC parts into <paramref name="info"/>.
    /// </summary>
    private static void CaptureReferencedParts(
        XElement el,
        IReadOnlyList<OpcRelationshipTarget> slideRels2,
        ZipArchive archive, string partPath,
        PreservedObjectInfo info)
    {
        var rNs = R.NamespaceName;
        foreach (var attr in el.Descendants()
                                .SelectMany(e => e.Attributes())
                                .Where(a => a.Name.NamespaceName == rNs)
                                .ToList())
        {
            var rId = attr.Value;
            var rel = slideRels2.FirstOrDefault(r => r.Id == rId);
            if (rel == default) continue;
            var targetPath = ResolveRelativeZipPath(GetDirectoryName(partPath), rel.Target);
            if (info.SlideRels.ContainsKey(rId)) continue;
            info.SlideRels[rId] = (rel.Type, targetPath);
            CapturePartBytes(targetPath, archive, info);
        }
    }

    /// <summary>
    /// Captures a preserved/unknown OPC part's bytes (+ its own .rels, if any) into
    /// <paramref name="info"/>.
    ///
    /// BUG EA5 fix: capture is now TRANSITIVE. A preserved part (e.g. a 3D-model part,
    /// "ppt/embeddings/model3d/model1.glb") can itself declare a .rels file pointing at a
    /// SECONDARY part (e.g. an embedded texture/thumbnail). The old implementation captured only
    /// the part's own bytes and its .rels bytes VERBATIM but never followed the relationship
    /// targets declared inside that .rels — so the secondary part was never captured, and the
    /// writer re-emitted the (verbatim) .rels file referencing a target that was never written to
    /// the output zip. PowerPoint sees the dangling relationship target when it tries to load the
    /// model part and reports "needs repair".
    ///
    /// The fix parses the just-captured .rels XML, resolves each internal (non-External) relationship
    /// Target relative to this part's own directory (matching OPC part-path resolution: targets in a
    /// part's .rels are relative to that PART's directory, not the root or the referencing slide),
    /// and recursively captures each target that hasn't already been captured — walking arbitrarily
    /// deep (e.g. texture -> texture's own .rels -> further sub-parts) via the same recursive call.
    /// TargetMode="External" relationships are skipped: they point outside the package (e.g. a URL)
    /// and are not OPC parts to capture — <see cref="OpcRelationships.Load"/> (not the External-blind
    /// <c>LoadTargets</c> projection) is used here specifically so IsExternal is available.
    ///
    /// <paramref name="visited"/> guards against relationship cycles (a part whose .rels — directly
    /// or transitively — points back at itself or an ancestor) causing infinite recursion; it is
    /// seeded with the current part path on entry and shared across the whole recursive walk for a
    /// given top-level capture.
    /// </summary>
    private static void CapturePartBytes(
        string partPath2, ZipArchive archive, PreservedObjectInfo info,
        HashSet<string>? visited = null)
    {
        visited ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!visited.Add(partPath2)) return; // already visited this path in this walk (cycle guard)

        if (info.Parts.ContainsKey(partPath2)) return;
        var bytes = ReadEntryBytes(archive, partPath2);
        if (bytes is null) return;
        info.Parts[partPath2]            = bytes;
        info.PartContentTypes[partPath2] = GuessPreservedContentType(partPath2);

        // Capture rels file for this part if it exists
        var relsPath2 = GetRelationshipPartPath(partPath2);
        byte[]? relsBytes = null;
        if (!info.PartRels.ContainsKey(partPath2))
        {
            relsBytes = ReadEntryBytes(archive, relsPath2);
            if (relsBytes is not null)
                info.PartRels[partPath2] = relsBytes;
        }
        else
        {
            relsBytes = info.PartRels[partPath2];
        }

        // EA5: recursively follow this part's own .rels targets so transitively-referenced
        // secondary parts (e.g. a 3D model's texture) are captured too, not just the part itself.
        // Uses the shared hardened OpcXml.TryLoadXml (DTD/XXE-safe, bounded) rather than a raw
        // XDocument.Load, consistent with every other XML parse in this reader.
        if (relsBytes is not null)
        {
            var relsDoc = OpcXml.TryLoadXml(relsBytes);
            if (relsDoc is null) return; // malformed .rels — nothing more we can safely walk

            var partDir = GetDirectoryName(partPath2);
            foreach (var rel in OpcRelationships.Load(relsDoc))
            {
                if (rel.IsExternal) continue; // external targets aren't packaged parts
                if (string.IsNullOrEmpty(rel.Target)) continue;

                var childPath = ResolveRelativeZipPath(partDir, rel.Target);
                CapturePartBytes(childPath, archive, info, visited);
            }
        }
    }

    private static ImagePart? ExtractPreservedFallbackImage(
        XElement gfEl, XElement graphicData,
        IReadOnlyList<OpcRelationshipTarget> slideRels2,
        ZipArchive archive, string partPath)
    {
        // Look for an a:blip with r:embed inside the graphicData (common for 3D model preview)
        var blipEl = graphicData.Descendants(A + "blip").FirstOrDefault();
        var imgRelId = blipEl?.Attribute(R + "embed")?.Value;
        if (string.IsNullOrEmpty(imgRelId))
        {
            // Also check the top-level graphicFrame (some zoom variants embed preview at frame level)
            imgRelId = gfEl.Descendants(A + "blip").FirstOrDefault()
                           ?.Attribute(R + "embed")?.Value;
        }
        if (string.IsNullOrEmpty(imgRelId)) return null;

        var imgRel = slideRels2.FirstOrDefault(r => r.Id == imgRelId);
        if (imgRel == default) return null;

        var imgPath = ResolveRelativeZipPath(GetDirectoryName(partPath), imgRel.Target);
        var imgBytes = ReadEntryBytes(archive, imgPath);
        if (imgBytes is null) return null;

        return new ImagePart
        {
            Bytes       = imgBytes,
            ContentType = GuessPreservedContentType(imgPath),
        };
    }

    private static string GuessPreservedContentType(string path)
    {
        var ext = path.Contains('.') ? path[(path.LastIndexOf('.') + 1)..].ToLowerInvariant() : "";
        if (ext is "png" or "jpg" or "jpeg" or "gif" or "svg" or "wmf" or "emf" &&
            OpcMediaTypes.TryGetDefaultContentType(ext, out var contentType))
        {
            return contentType;
        }

        return ext switch
        {
            "glb" or "gltf" => "model/gltf-binary",
            "xml"  => "application/xml",
            _      => "application/octet-stream",
        };
    }

    // ── SmartArt diagram parsing ──────────────────────────────────────────────────

    private static SmartArtShape ReadSmartArt(
        XElement graphicData, ZipArchive archive, string partPath, PresentationColorScheme scheme)
    {
        var smart = new SmartArtShape();

        // The graphicData element holds a dgm:relIds child (or attributes directly) with
        // r:dm / r:lo / r:qs / r:cs pointing to the diagram sub-parts via slide rels.
        // Some encoders put them directly on the graphicData element; others use dgm:relIds.
        var relIdsEl = graphicData.Element(Dgm + "relIds") ?? graphicData;

        var dmRelId = relIdsEl.Attribute(R + "dm")?.Value;
        var loRelId = relIdsEl.Attribute(R + "lo")?.Value;
        var qsRelId = relIdsEl.Attribute(R + "qs")?.Value;
        var csRelId = relIdsEl.Attribute(R + "cs")?.Value;

        if (dmRelId is not null) smart.DiagramRelIds["dm"] = dmRelId;
        if (loRelId is not null) smart.DiagramRelIds["lo"] = loRelId;
        if (qsRelId is not null) smart.DiagramRelIds["qs"] = qsRelId;
        if (csRelId is not null) smart.DiagramRelIds["cs"] = csRelId;

        // Resolve each rel id -> part path via slide rels.
        var slideRels = OpcRelationships.LoadTargets(archive, GetRelationshipPartPath(partPath));
        var slideDir  = GetDirectoryName(partPath);

        var relTypeForKey = new Dictionary<string, string>
        {
            ["dm"] = DiagramDataRelType,
            ["lo"] = DiagramLayoutRelType,
            ["qs"] = DiagramQuickStyleRelType,
            ["cs"] = DiagramColorsRelType
        };

        var contentTypeForKey = new Dictionary<string, string>
        {
            ["dm"] = "application/vnd.openxmlformats-officedocument.drawingml.diagramData+xml",
            ["lo"] = "application/vnd.openxmlformats-officedocument.drawingml.diagramLayout+xml",
            ["qs"] = "application/vnd.openxmlformats-officedocument.drawingml.diagramStyle+xml",
            ["cs"] = "application/vnd.openxmlformats-officedocument.drawingml.diagramColors+xml"
        };

        string? dataPartPath = null;

        foreach (var (key, relId) in smart.DiagramRelIds)
        {
            if (!relTypeForKey.TryGetValue(key, out var relType)) continue;

            // Find target by relId (type not always set correctly — match by id first)
            var target = slideRels.FirstOrDefault(r => r.Id == relId).Target;
            if (string.IsNullOrWhiteSpace(target)) continue;

            var absPath = ResolveRelativeZipPath(slideDir, target);
            var bytes = ReadEntryBytes(archive, absPath);
            if (bytes is null) continue;

            contentTypeForKey.TryGetValue(key, out var ct);
            smart.Parts[absPath] = new DiagramPart
            {
                ContentType = ct ?? "application/xml",
                PartPath    = absPath,
                Bytes       = bytes
            };

            // Capture and store rels for this part
            var partRelsPath = GetRelationshipPartPath(absPath);
            var partRelsBytes = ReadEntryBytes(archive, partRelsPath);
            if (partRelsBytes is not null)
                smart.PartRels[absPath] = partRelsBytes;

            if (key == "dm") dataPartPath = absPath;
        }

        // Resolve the dsp:drawing part path from the data part's rels.
        if (dataPartPath is not null)
        {
            var dataPartRels = OpcRelationships.LoadTargets(archive, GetRelationshipPartPath(dataPartPath));
            var drawingTarget = dataPartRels
                .FirstOrDefault(r => r.Type == DiagramDrawingRelType).Target;

            var drawingPath = !string.IsNullOrWhiteSpace(drawingTarget)
                ? ResolveRelativeZipPath(GetDirectoryName(dataPartPath), drawingTarget)
                : InferSiblingDiagramDrawingPath(archive, dataPartPath);

            if (!string.IsNullOrWhiteSpace(drawingPath))
            {
                smart.DrawingPartPath = drawingPath;

                var drawingBytes = ReadEntryBytes(archive, drawingPath);
                if (drawingBytes is not null)
                {
                    smart.Parts[drawingPath] = new DiagramPart
                    {
                        ContentType = "application/vnd.ms-office.drawingml.diagramDrawing+xml",
                        PartPath    = drawingPath,
                        Bytes       = drawingBytes
                    };

                    // Also capture rels for drawing part if present
                    var drawRelsBytes = ReadEntryBytes(archive, GetRelationshipPartPath(drawingPath));
                    if (drawRelsBytes is not null)
                    {
                        smart.PartRels[drawingPath] = drawRelsBytes;

                        // SmartArt picture layouts store their media behind the diagram
                        // drawing part rather than in the slide's normal picture table.
                        // Rehydrate those media parts into the same raw-part bag so the
                        // writer can preserve them and authoring can replace one node image
                        // without dangling the dsp:drawing relationship.
                        var drawingRels = OpcXml.TryLoadXml(drawRelsBytes);
                        if (drawingRels is not null)
                        {
                            foreach (var relationship in OpcRelationships.Load(drawingRels))
                            {
                                if (relationship.IsExternal ||
                                    !relationship.Type.EndsWith("/image", StringComparison.OrdinalIgnoreCase) ||
                                    string.IsNullOrWhiteSpace(relationship.Target))
                                    continue;

                                var mediaPath = ResolveRelativeZipPath(
                                    GetDirectoryName(drawingPath),
                                    relationship.Target);
                                var mediaBytes = ReadEntryBytes(archive, mediaPath);
                                if (mediaBytes is null)
                                    continue;

                                smart.Parts[mediaPath] = new DiagramPart
                                {
                                    ContentType = GuessPreservedContentType(mediaPath),
                                    PartPath = mediaPath,
                                    Bytes = mediaBytes,
                                };
                            }
                        }
                    }

                    // Parse dsp:drawing shapes into FallbackShapes
                    try
                    {
                        ReadDspDrawing(drawingBytes, smart, scheme, archive, drawingPath);
                    }
                    catch
                    {
                        // Graceful degradation: if dsp parsing fails, FallbackShapes stays empty
                    }
                }
            }
        }

        // Theme 17: Parse SmartArtData (node tree + family) from data1.xml + layout1.xml
        try
        {
            smart.Data = ReadSmartArtData(smart);
            TryAttachPictureNodePictures(smart, archive);
        }
        catch
        {
            // Graceful degradation: Data stays null, compositor will use cached fallback
        }

        try
        {
            smart.QuickStyle = ReadSmartArtQuickStyleMetadata(smart);
            smart.Colors = ReadSmartArtColorMetadata(smart, scheme);
        }
        catch
        {
            // Style/color metadata is advisory only; raw diagram parts stay preserved.
        }

        return smart;
    }

    private static string? InferSiblingDiagramDrawingPath(ZipArchive archive, string dataPartPath)
    {
        var slash = dataPartPath.LastIndexOf('/');
        var directory = slash >= 0 ? dataPartPath[..slash] : string.Empty;
        var fileName = slash >= 0 ? dataPartPath[(slash + 1)..] : dataPartPath;

        if (!fileName.StartsWith("data", StringComparison.OrdinalIgnoreCase))
            return null;

        var candidateName = "drawing" + fileName["data".Length..];
        var candidatePath = string.IsNullOrEmpty(directory)
            ? candidateName
            : $"{directory}/{candidateName}";

        return archive.GetEntry(candidatePath) is null ? null : candidatePath;
    }

    // ── OLE embedded object parsing (Theme 21) ────────────────────────────────────

    /// <summary>
    /// Reads an OLE embedded-object frame.
    ///
    /// Structure (both forms accepted):
    ///   (A) p:graphicFrame / a:graphic / a:graphicData[@uri="…/ole"] / p:oleObj
    ///   (B) mc:AlternateContent / mc:Choice / p:graphicFrame / … (same nested structure)
    ///
    /// The p:oleObj element carries:
    ///   - @r:id → relationship to the embedded binary (xlsx/bin/…)
    ///   - @progId → e.g. "Excel.Sheet.12"
    ///   - p:pic child → the fallback preview image (has its own r:id → an image rel)
    ///
    /// We store:
    ///   - shape.OleObject  : progId, embedded bytes, rel type, verbatim oleObj XML
    ///   - shape.Picture    : fallback image bytes (so the compositor can render it)
    /// </summary>
    private static SlideShape? ReadOleObject(
        XElement oleObjEl,
        XElement gfEl,
        ZipArchive archive,
        string partPath,
        PresentationColorScheme scheme,
        bool wasAlternateContent)
    {
        var slideRels = OpcRelationships.LoadTargets(archive, GetRelationshipPartPath(partPath));
        var slideDir  = GetDirectoryName(partPath);

        var ole = new OleObjectInfo
        {
            ProgId              = oleObjEl.Attribute("progId")?.Value ?? string.Empty,
            WasAlternateContent = wasAlternateContent,
        };

        // ── Load embedded binary ───────────────────────────────────────────────
        var embRelId = oleObjEl.Attribute(R + "id")?.Value;
        if (!string.IsNullOrWhiteSpace(embRelId))
        {
            var embRel = slideRels.FirstOrDefault(r => r.Id == embRelId);
            if (!string.IsNullOrWhiteSpace(embRel.Target))
            {
                var embPath = ResolveRelativeZipPath(slideDir, embRel.Target);
                var embBytes = ReadEntryBytes(archive, embPath);
                if (embBytes is not null)
                    ole.EmbeddedBytes = embBytes;

                // Infer content type and extension from path
                var ext = embRel.Target.Split('.').LastOrDefault() ?? "bin";
                ole.EmbeddedExtension = ext;
                ole.EmbeddedContentType = OleExtensionToContentType(ext);

                // Capture the rel type for round-trip (package vs oleObject vs other)
                ole.RelType = string.IsNullOrWhiteSpace(embRel.Type)
                    ? PackageRelType
                    : embRel.Type;
            }
        }

        // ── Store verbatim oleObj XML for round-trip ───────────────────────────
        // Strip the p:pic child — we will rebuild it from shape.Picture on write.
        var oleObjCopy = new XElement(oleObjEl);
        oleObjCopy.Elements(P + "pic").Remove();
        // Also strip any sub-shape picture picker (mc:AlternateContent inside oleObj)
        oleObjCopy.Descendants(MC + "AlternateContent").ToList().ForEach(e => e.Remove());
        using (var sw = new System.IO.StringWriter())
        {
            oleObjCopy.Save(sw, SaveOptions.DisableFormatting);
            ole.OleObjXml = sw.ToString();
        }

        // ── Load fallback preview image ────────────────────────────────────────
        // The fallback image may be:
        //   (a) p:oleObj/p:pic/p:blipFill/a:blip r:embed
        //   (b) a:blip r:embed directly under oleObj
        ImagePart? fallbackImage = null;
        var picEl = oleObjEl.Element(P + "pic");
        if (picEl is not null)
        {
            var blip = picEl.Descendants(A + "blip").FirstOrDefault();
            fallbackImage = LoadImageFromBlip(blip, slideRels, slideDir, archive);
        }
        if (fallbackImage is null)
        {
            // Fallback: look for any a:blip directly under oleObj
            var blip = oleObjEl.Descendants(A + "blip").FirstOrDefault();
            fallbackImage = LoadImageFromBlip(blip, slideRels, slideDir, archive);
        }

        var shape = new SlideShape
        {
            Kind      = SlideShapeKind.Ole,
            OleObject = ole,
            Picture   = fallbackImage,
        };
        return shape;
    }

    /// <summary>
    /// Loads an image referenced by an a:blip element via r:embed in the slide rels.
    /// Returns null when the blip or image is absent.
    /// </summary>
    private static ImagePart? LoadImageFromBlip(
        XElement? blip,
        IReadOnlyList<OpcRelationshipTarget> slideRels,
        string slideDir,
        ZipArchive archive)
    {
        if (blip is null) return null;
        var embedId = blip.Attribute(R + "embed")?.Value;
        if (string.IsNullOrWhiteSpace(embedId)) return null;

        var rel = slideRels.FirstOrDefault(r => r.Id == embedId);
        if (string.IsNullOrWhiteSpace(rel.Target)) return null;

        var imgPath = ResolveRelativeZipPath(slideDir, rel.Target);
        var bytes = ReadEntryBytes(archive, imgPath);
        if (bytes is null) return null;

        return new ImagePart
        {
            Bytes = bytes,
            ContentType = OpcMediaTypes.GetDrawingMediaContentType(imgPath)
        };
    }

    /// <summary>Derives an IANA content type from an embedded-object file extension.</summary>
    private static string OleExtensionToContentType(string ext) =>
        ext.ToLowerInvariant() switch
        {
            "xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "xlsm" => "application/vnd.ms-excel.sheet.macroEnabled.12",
            "docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            "pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
            "bin"  => "application/vnd.ms-office.activeX+xml",
            _      => "application/octet-stream"
        };

    private static SmartArtQuickStyleMetadata? ReadSmartArtQuickStyleMetadata(SmartArtShape smart)
    {
        var entry = smart.Parts.Values.FirstOrDefault(p =>
            p.ContentType.Contains("diagramStyle", StringComparison.OrdinalIgnoreCase)
            || p.PartPath.Contains("quickStyle", StringComparison.OrdinalIgnoreCase));
        if (entry is null) return null;

        using var ms = new MemoryStream(entry.Bytes);
        var doc = OpcXml.LoadXml(ms);
        var root = doc.Root;
        if (root is null) return null;

        var metadata = new SmartArtQuickStyleMetadata
        {
            UniqueId = root.Attribute("uniqueId")?.Value ?? string.Empty,
            Title = ReadDiagramTitle(root),
            Category = ReadDiagramCategory(root)
        };

        foreach (var label in root.Descendants().Where(e => e.Name.LocalName == "styleLbl"))
        {
            var name = label.Attribute("name")?.Value;
            if (string.IsNullOrWhiteSpace(name))
                continue;

            if (!metadata.StyleLabels.Contains(name, StringComparer.OrdinalIgnoreCase))
                metadata.StyleLabels.Add(name);

            var lineReference = label.Elements().FirstOrDefault(e => e.Name.LocalName == "lnRef");
            var fillReference = label.Elements().FirstOrDefault(e => e.Name.LocalName == "fillRef");
            var effectReference = label.Elements().FirstOrDefault(e => e.Name.LocalName == "effectRef");
            var fontReference = label.Elements().FirstOrDefault(e => e.Name.LocalName == "fontRef");
            metadata.StyleLabelMetadata.Add(new SmartArtQuickStyleLabelMetadata
            {
                Name = name,
                LineReferenceIndex = ParseNullableInt(lineReference?.Attribute("idx")?.Value),
                FillReferenceIndex = ParseNullableInt(fillReference?.Attribute("idx")?.Value),
                EffectReferenceIndex = ParseNullableInt(effectReference?.Attribute("idx")?.Value),
                FontReferenceIndex = fontReference?.Attribute("idx")?.Value,
            });
        }

        return metadata;
    }

    private static SmartArtColorMetadata? ReadSmartArtColorMetadata(SmartArtShape smart, PresentationColorScheme scheme)
    {
        var entry = smart.Parts.Values.FirstOrDefault(p =>
            p.ContentType.Contains("diagramColors", StringComparison.OrdinalIgnoreCase)
            || p.PartPath.Contains("colors", StringComparison.OrdinalIgnoreCase));
        if (entry is null) return null;

        using var ms = new MemoryStream(entry.Bytes);
        var doc = OpcXml.LoadXml(ms);
        var root = doc.Root;
        if (root is null) return null;

        var metadata = new SmartArtColorMetadata
        {
            UniqueId = root.Attribute("uniqueId")?.Value ?? string.Empty,
            Title = ReadDiagramTitle(root),
            Category = ReadDiagramCategory(root)
        };

        var styleLabels = root.Elements().Where(e => e.Name.LocalName == "styleLbl").ToList();
        foreach (var label in styleLabels)
        {
            var name = label.Attribute("name")?.Value;
            if (!string.IsNullOrWhiteSpace(name)
                && !metadata.ColorLabels.Contains(name, StringComparer.OrdinalIgnoreCase))
                metadata.ColorLabels.Add(name);
        }

        // KB1 fix: the node FILL cycle must come from the fillClrLst of the node styleLbl
        // (e.g. name="node0"/"node1"), NOT a flatten of every color in the part. A real
        // colorsDef also carries linClrLst/txFillClrLst/txLinClrLst/bgFillClrLst under
        // node and non-node styleLbls (e.g. "lnNode", "trans1D", "bg") — those must never
        // leak into the node fill palette, even when their resolved RGB happens to be
        // identical to a fill color's un-shaded siblings (the pre-existing dedup key was
        // RoleName+Resolved, which is not enough on its own to keep lists separated).
        var nodeFillList = SelectNodeFillColorList(styleLabels);
        if (nodeFillList is not null)
        {
            foreach (var colorEl in nodeFillList.Elements().Where(e =>
                e.Name.LocalName is "schemeClr" or "srgbClr" or "sysClr"))
            {
                var color = TryReadSmartArtPaletteColor(colorEl, scheme);
                if (color is null) continue;

                if (!metadata.Palette.Any(existing =>
                    string.Equals(existing.SchemeColor?.RoleName, color.SchemeColor?.RoleName, StringComparison.OrdinalIgnoreCase)
                    && existing.Resolved == color.Resolved))
                    metadata.Palette.Add(color);

                if (metadata.Palette.Count >= 12) break;
            }
        }

        return metadata;
    }

    /// <summary>
    /// Picks the fillClrLst that supplies the node FILL color cycle for SmartArt shapes.
    /// PowerPoint's colorsDef labels the diagram-node style with a styleLbl whose name
    /// starts with "node" (commonly "node0", sometimes "node1" for diagrams with two node
    /// families). That styleLbl's dgm:fillClrLst is the accent cycle used for node fills;
    /// linClrLst/txFillClrLst/txLinClrLst/bgFillClrLst on that same (or any other) styleLbl
    /// are unrelated (line, text, background) and must be ignored here.
    /// Falls back to the first styleLbl carrying a fillClrLst at all (mirrors PowerPoint's
    /// own leniency for non-standard/older colorsDef parts), then to null when none exists.
    /// </summary>
    private static XElement? SelectNodeFillColorList(IReadOnlyList<XElement> styleLabels)
    {
        XElement? FillListOf(XElement label) =>
            label.Elements().FirstOrDefault(e => e.Name.LocalName == "fillClrLst");

        // Prefer an exact "node0" label (the overwhelmingly common case), then any other
        // "node*" label (e.g. "node1" for two-family diagrams), then fall back to whatever
        // styleLbl happens to carry a fillClrLst.
        var node0 = styleLabels.FirstOrDefault(l =>
            string.Equals(l.Attribute("name")?.Value, "node0", StringComparison.OrdinalIgnoreCase));
        if (node0 is not null && FillListOf(node0) is { } node0Fill) return node0Fill;

        var otherNode = styleLabels.FirstOrDefault(l =>
            (l.Attribute("name")?.Value ?? string.Empty).StartsWith("node", StringComparison.OrdinalIgnoreCase)
            && FillListOf(l) is not null);
        if (otherNode is not null) return FillListOf(otherNode);

        return styleLabels.FirstOrDefault(l => FillListOf(l) is not null) is { } anyLabel
            ? FillListOf(anyLabel)
            : null;
    }

    private static string ReadDiagramTitle(XElement root) =>
        root.Descendants()
            .FirstOrDefault(e => e.Name.LocalName == "title")
            ?.Attribute("val")?.Value
        ?? string.Empty;

    private static string ReadDiagramCategory(XElement root) =>
        root.Descendants()
            .FirstOrDefault(e => e.Name.LocalName == "cat")
            ?.Attribute("type")?.Value
        ?? string.Empty;

    private static ThemeAwareColor? TryReadSmartArtPaletteColor(XElement colorEl, PresentationColorScheme scheme)
    {
        if (colorEl.Name.LocalName == "srgbClr")
        {
            var rgb = ParseHexColor(colorEl.Attribute("val")?.Value);
            return rgb.HasValue ? new ThemeAwareColor(rgb.Value) : null;
        }

        if (colorEl.Name.LocalName == "sysClr")
        {
            var rgb = ParseHexColor(colorEl.Attribute("lastClr")?.Value);
            return rgb.HasValue ? new ThemeAwareColor(rgb.Value) : null;
        }

        if (colorEl.Name.LocalName != "schemeClr") return null;

        var roleName = colorEl.Attribute("val")?.Value;
        if (!PptxColorReader.TryMapSchemeColor(roleName, out var slot))
            return null;

        var lumMod = ReadDiagramColorPercentage(colorEl, "lumMod") ?? 1.0;
        var lumOff = ReadDiagramColorPercentage(colorEl, "lumOff") ?? 0.0;
        var tint = ReadDiagramColorPercentage(colorEl, "tint") ?? 1.0;
        var shade = ReadDiagramColorPercentage(colorEl, "shade") ?? 1.0;

        var resolved = ThemeColorTransform.Apply(scheme[slot], lumMod, lumOff, tint, shade);
        return new ThemeAwareColor(resolved, new SchemeColorRef
        {
            RoleName = roleName?.Trim(),
            Slot = slot,
            LumMod = lumMod,
            LumOff = lumOff,
            Tint = tint,
            Shade = shade
        });
    }

    private static double? ReadDiagramColorPercentage(XElement colorEl, string localName)
    {
        var raw = colorEl.Elements().FirstOrDefault(e => e.Name.LocalName == localName)?.Attribute("val")?.Value;
        if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            return null;
        return Math.Clamp(value / 100000.0, 0.0, 1.0);
    }

    private static SrgbColor? ParseHexColor(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return null;
        hex = hex.Trim().TrimStart('#');
        if (hex.Length != 6) return null;

        if (!byte.TryParse(hex[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var r)) return null;
        if (!byte.TryParse(hex.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var g)) return null;
        if (!byte.TryParse(hex.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b)) return null;
        return new SrgbColor(r, g, b);
    }

    /// <summary>
    /// Theme 17: Parses the SmartArtData model (node tree + family) from the verbatim
    /// part bytes already loaded into <paramref name="smart"/>.Parts.
    /// Returns null when the data or layout part is absent or unreadable.
    /// </summary>
    private static SmartArtData? ReadSmartArtData(SmartArtShape smart)
    {
        // Find data part (ends with data*.xml)
        var dataEntry = smart.Parts.Values
            .FirstOrDefault(p => p.ContentType.Contains("diagramData", StringComparison.OrdinalIgnoreCase));
        var layoutEntry = smart.Parts.Values
            .FirstOrDefault(p => p.ContentType.Contains("diagramLayout", StringComparison.OrdinalIgnoreCase));

        if (dataEntry is null) return null;

        // ── Parse layout1.xml → family ─────────────────────────────────────────
        var family = SmartArtFamily.Unknown;
        string layoutUniqueId = string.Empty;

        if (layoutEntry is not null)
        {
            XDocument layoutDoc;
            using (var ms = new MemoryStream(layoutEntry.Bytes))
                layoutDoc = OpcXml.LoadXml(ms);

            layoutUniqueId = layoutDoc.Root?.Attribute("uniqueId")?.Value ?? string.Empty;
            family = ClassifySmartArtFamily(layoutUniqueId);
        }

        // ── Parse data1.xml → node tree ────────────────────────────────────────
        XDocument dataDoc;
        using (var ms2 = new MemoryStream(dataEntry.Bytes))
            dataDoc = OpcXml.LoadXml(ms2);

        var isLiveLayoutSupported = IsLiveSmartArtLayoutSupported(layoutUniqueId, family);
        // PowerPoint's hierarchy3 cache is the authoritative imported drawing. The
        // bounded live hierarchy plan remains available for authoring paths, which
        // explicitly regenerate the cache before re-enabling live consumption.
        if (layoutUniqueId.EndsWith("/hierarchy3", StringComparison.OrdinalIgnoreCase)
            && smart.FallbackShapes.Count > 0)
        {
            isLiveLayoutSupported = false;
        }
        if (IsPictureNodeLayout(layoutUniqueId))
        {
            // A valid picture layout may intentionally contain no images yet: PowerPoint
            // exposes those nodes as editable "Add picture" placeholders. Relationship
            // validation below still rejects ambiguous partial media mappings.
            isLiveLayoutSupported = true;
        }

        var data = new SmartArtData
        {
            Family        = family,
            LayoutUniqueId = layoutUniqueId,
            IsLiveLayoutSupported = isLiveLayoutSupported
        };

        // dgm: namespace in data1.xml
        var dgmNsData = XNamespace.Get("http://schemas.openxmlformats.org/drawingml/2006/diagram");
        var aNsData   = XNamespace.Get("http://schemas.openxmlformats.org/drawingml/2006/main");

        var ptLst  = dataDoc.Root?.Element(dgmNsData + "ptLst");
        var cxnLst = dataDoc.Root?.Element(dgmNsData + "cxnLst");

        if (ptLst is null) return data; // empty but valid

        // Build a dict: modelId → (type, text)
        var points = new Dictionary<string, (string type, string text, bool isAsst)>(StringComparer.OrdinalIgnoreCase);
        foreach (var pt in ptLst.Elements(dgmNsData + "pt"))
        {
            var modelId = pt.Attribute("modelId")?.Value ?? string.Empty;
            var type    = pt.Attribute("type")?.Value ?? "node";

            // Extract text from dgm:t/a:p/a:r/a:t while preserving paragraph and break
            // boundaries. SmartArt name-and-title nodes commonly use two authored a:p
            // elements; flattening them into a space loses editing and layout semantics.
            var tEl = pt.Element(dgmNsData + "t");
            var paragraphTexts = tEl?.Elements(aNsData + "p")
                .Select(ReadSmartArtParagraphText)
                .ToArray() ?? [];
            var text = paragraphTexts.Length > 0
                ? string.Join("\n", paragraphTexts)
                : tEl is null
                    ? string.Empty
                    : string.Concat(tEl.Descendants(aNsData + "t").Select(element => element.Value));

            if (!string.IsNullOrWhiteSpace(modelId))
                points[modelId] = (type, text, type == "asst");
        }

        // Build parent→children map from cxnLst parOf connections
        var childrenOf = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var hasParent  = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (cxnLst is not null)
        {
            foreach (var cxn in cxnLst.Elements(dgmNsData + "cxn"))
            {
                var cxnType = cxn.Attribute("type")?.Value ?? string.Empty;
                // DiagramML defaults an untyped connection to parOf. PowerPoint omits
                // @type on ordinary parent links, while presOf/presParOf are explicit.
                if (!string.IsNullOrWhiteSpace(cxnType)
                    && !string.Equals(cxnType, "parOf", StringComparison.OrdinalIgnoreCase))
                    continue;

                var srcId  = cxn.Attribute("srcId")?.Value  ?? string.Empty;
                var destId = cxn.Attribute("destId")?.Value ?? string.Empty;
                if (string.IsNullOrWhiteSpace(srcId) || string.IsNullOrWhiteSpace(destId)) continue;

                if (points.TryGetValue(srcId, out var sourcePoint)
                    && string.Equals(sourcePoint.type, "doc", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!childrenOf.TryGetValue(srcId, out var kids))
                    childrenOf[srcId] = kids = new List<string>();
                kids.Add(destId);
                hasParent.Add(destId);
            }
        }

        // Collect root node-type points (not a child of any other node, type == node or asst)
        var roots = points
            .Where(kv =>
            {
                var t = kv.Value.type;
                return (t == "node" || t == "asst") && !hasParent.Contains(kv.Key);
            })
            .Select(kv => kv.Key)
            .ToList();

        // Recursively build tree
        SmartArtNode BuildNode(string id, int level)
        {
            var (_, text, isAsst) = points.TryGetValue(id, out var info) ? info : ("node", id, false);
            var node = new SmartArtNode
            {
                ModelId    = id,
                Text       = text,
                Level      = level,
                IsAssistant = isAsst
            };
            if (childrenOf.TryGetValue(id, out var kids))
            {
                foreach (var kid in kids)
                {
                    if (points.ContainsKey(kid))
                        node.Children.Add(BuildNode(kid, level + 1));
                }
            }
            return node;
        }

        foreach (var rootId in roots)
            data.Nodes.Add(BuildNode(rootId, 0));

        return data;

        string ReadSmartArtParagraphText(XElement paragraph)
        {
            var text = new System.Text.StringBuilder();
            foreach (var node in paragraph.DescendantNodes())
            {
                if (node is not XElement element)
                    continue;

                if (element.Name == aNsData + "t")
                    text.Append(element.Value);
                else if (element.Name == aNsData + "br")
                    text.Append('\n');
            }

            return text.ToString();
        }
    }

    private static void TryAttachPictureNodePictures(SmartArtShape smart, ZipArchive archive)
    {
        var data = smart.Data;
        if (data is null || !IsPictureNodeLayout(data.LayoutUniqueId))
            return;

        var nodes = FlattenSmartArtNodes(data);
        if (nodes.Count == 0)
        {
            data.IsLiveLayoutSupported = false;
            return;
        }

        var pictures = ReadSmartArtDrawingPictures(smart, archive)
            .Where(p => p.Picture.Bytes.Length > 0)
            .ToList();

        // An empty drawing is the valid placeholder-only state. Tagged pictures can be mapped
        // by model identity, while an entirely untagged complete drawing retains the legacy
        // document-order mapping. Any ambiguous partial identity remains on the cached fallback.
        if (pictures.Count == 0)
        {
            data.IsLiveLayoutSupported = true;
            return;
        }

        var taggedPictures = pictures
            .Where(p => !string.IsNullOrWhiteSpace(p.ModelId))
            .ToList();
        if (taggedPictures.Count == pictures.Count)
        {
            var nodeById = new Dictionary<string, SmartArtNode>(StringComparer.OrdinalIgnoreCase);
            var identitiesAreValid = true;
            foreach (var node in nodes)
            {
                if (string.IsNullOrWhiteSpace(node.ModelId) || !nodeById.TryAdd(node.ModelId!, node))
                {
                    identitiesAreValid = false;
                    break;
                }
            }
            var mappedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var mappings = new List<(SmartArtNode Node, ImagePart Picture)>();
            if (identitiesAreValid)
            {
                foreach (var (modelId, picture) in taggedPictures)
                {
                    if (modelId is null || !nodeById.TryGetValue(modelId, out var node) || !mappedIds.Add(modelId))
                    {
                        identitiesAreValid = false;
                        break;
                    }

                    mappings.Add((node, picture));
                }
            }

            if (identitiesAreValid)
            {
                foreach (var (node, picture) in mappings)
                    node.Picture = picture;
                data.IsLiveLayoutSupported = true;
                return;
            }
        }

        if (pictures.Count != nodes.Count)
        {
            data.IsLiveLayoutSupported = false;
            return;
        }

        for (var i = 0; i < nodes.Count; i++)
            nodes[i].Picture = pictures[i].Picture;

        data.IsLiveLayoutSupported = true;
    }

    private static List<SmartArtNode> FlattenSmartArtNodes(SmartArtData data)
    {
        var nodes = new List<SmartArtNode>();
        foreach (var root in data.Nodes)
            Collect(root);
        return nodes;

        void Collect(SmartArtNode node)
        {
            nodes.Add(node);
            foreach (var child in node.Children)
                Collect(child);
        }
    }

    private static List<(string? ModelId, ImagePart Picture)> ReadSmartArtDrawingPictures(SmartArtShape smart, ZipArchive archive)
    {
        var pictures = new List<(string? ModelId, ImagePart Picture)>();
        if (string.IsNullOrWhiteSpace(smart.DrawingPartPath))
            return pictures;

        if (!smart.Parts.TryGetValue(smart.DrawingPartPath, out var drawingPart))
            return pictures;

        XDocument doc;
        using (var ms = new MemoryStream(drawingPart.Bytes))
            doc = OpcXml.LoadXml(ms);

        var root = doc.Root;
        if (root is null) return pictures;

        var rels = OpcRelationships.LoadTargets(archive, GetRelationshipPartPath(smart.DrawingPartPath));
        var drawingDir = GetDirectoryName(smart.DrawingPartPath);

        foreach (var el in root.Descendants().Where(e => e.Name.LocalName is "pic" or "sp"))
        {
            var blipFill = el.Elements().FirstOrDefault(e => e.Name.LocalName == "blipFill")
                ?? el.Elements().FirstOrDefault(e => e.Name.LocalName == "spPr")
                    ?.Elements().FirstOrDefault(e => e.Name.LocalName == "blipFill");
            var blip = blipFill?.Descendants().FirstOrDefault(e => e.Name == A + "blip" || e.Name.LocalName == "blip");
            var image = LoadImageFromBlip(blip, rels, drawingDir, archive);
            if (image is not null)
            {
                var modelId = (string?)el.Attribute("modelId")
                    ?? el.Elements().FirstOrDefault(e => e.Name.LocalName is "nvSpPr" or "nvPicPr")
                        ?.Elements().FirstOrDefault(e => e.Name.LocalName is "cNvPr")
                        ?.Attribute("modelId")?.Value;
                pictures.Add((modelId, image));
            }
        }

        return pictures;
    }

    private static bool IsPictureNodeLayout(string uniqueId)
    {
        if (string.IsNullOrWhiteSpace(uniqueId))
            return false;

        var id = uniqueId.Replace('\\', '/').Trim().ToLowerInvariant();
        return id.Split('/').Last() is "picturecaptionlist" or "pictureaccentlist" or "picturestack" or "picturelineup" or "continuouspicturelist" or "picturegrid";
    }

    /// <summary>
    /// Classifies a layoutDef @uniqueId string into a <see cref="SmartArtFamily"/>.
    /// The uniqueId is a URN like "urn:microsoft.com/office/officeart/2005/8/layout/process1".
    /// </summary>
    private static SmartArtFamily ClassifySmartArtFamily(string uniqueId)
    {
        if (string.IsNullOrWhiteSpace(uniqueId)) return SmartArtFamily.Unknown;

        // Normalise to lowercase for matching
        var uid = uniqueId.ToLowerInvariant();

        // Order matters: check more-specific patterns first
        if (uid.Contains("hierarchy") || uid.Contains("orgchart") || uid.Contains("org-chart")
            || uid.Contains("verticalbullet") || uid.Contains("vert") && uid.Contains("tree"))
            return SmartArtFamily.Hierarchy;

        if (uid.Contains("matrix"))
            return SmartArtFamily.Matrix;

        if (uid.Contains("venn") || uid.Contains("relationship") || uid.Contains("target") || uid.Contains("opposingideas") || uid.Contains("convergingradial") || uid.Contains("divergingradial") || uid.Contains("interlockingrings"))
            return SmartArtFamily.Relationship;

        if (uid.Contains("cycle") || uid.Contains("gear") || uid.Contains("radial"))
            return SmartArtFamily.Cycle;

        if (uid.Contains("horizontalbulletlist") || uid.Contains("horizontalblocklist") || uid.Contains("verticalchevronlist") || uid.Contains("verticalarrowlist") || uid.Contains("verticalblocklist"))
            return SmartArtFamily.List;

        if (uid.Contains("process") || uid.Contains("timeline") || uid.Contains("arrow") || uid.Contains("chevron")
            || uid.Contains("funnel") || uid.Contains("horiz"))
            return SmartArtFamily.Process;

        if (uid.Contains("list") || uid.Contains("lproc") || uid.Contains("bullet")
            || uid.Contains("pyramid") || uid.Contains("stack") || uid.Contains("picturegrid") || uid.Contains("pictureaccentlist") || uid.Contains("picturestack") || uid.Contains("picturelineup") || uid.Contains("picturestrips") || uid.Contains("continuouspicturelist"))
            return SmartArtFamily.List;

        return SmartArtFamily.Unknown;
    }

    /// <summary>
    /// Bounded live-layout allow-list. Broader family classification still lets the
    /// model describe richer layouts, but rendering should use the cached
    /// dsp:drawing until a specific layout geometry is implemented.
    /// </summary>
    private static bool IsLiveSmartArtLayoutSupported(string uniqueId, SmartArtFamily family)
    {
        if (family == SmartArtFamily.Unknown || string.IsNullOrWhiteSpace(uniqueId))
            return false;

        var id = uniqueId.Replace('\\', '/').Trim().ToLowerInvariant();
        var layoutId = id.Split('/').Last();

        return family switch
        {
            SmartArtFamily.Process => layoutId is "process1" or "basicprocess" or "accentprocess" or "ascendingprocess" or "descendingprocess" or "basictimeline" or "phasedprocess" or "circleaccenttimeline" or "stepdownprocess" or "continuousblockprocess" or "segmentedprocess" or "chevronprocess" or "basicchevronprocess" or "closedchevronprocess" or "bendingprocess" or "alternatingprocess" or "arrowribbon" or "circleprocess" or "circlearrowprocess" or "funnelprocess" or "verticalprocess",
            SmartArtFamily.List => layoutId is "list1" or "list2" or "basicblocklist" or "verticalboxlist" or "verticalblocklist" or "verticalchevronlist" or "verticalarrowlist" or "stackedlist" or "descendingblocklist" or "basicpyramid" or "pyramidlist" or "invertedpyramid" or "horizontalbulletlist" or "horizontalblocklist" or "trapezoidlist" or "picturecaptionlist" or "pictureaccentlist" or "picturestack" or "picturelineup" or "picturestrips" or "continuouspicturelist" or "picturegrid",
            SmartArtFamily.Cycle => layoutId is "cycle1" or "cycle2" or "radial1" or "basiccycle" or "multidirectionalcycle" or "radialcycle" or "radialcluster" or "radiallist" or "gearcycle" or "textcycle" or "blockcycle" or "nondirectionalcycle" or "continuouscycle",
            SmartArtFamily.Hierarchy => layoutId is "hierarchy1" or "hierarchy3" or "basichierarchy" or "horizontalhierarchy" or "labeledhierarchy" or "tablehierarchy" or "verticalbulletlist" or "orgchart" or "nameandtitleorgchart",
            SmartArtFamily.Matrix => layoutId is "matrix1" or "basicmatrix" or "titledmatrix" or "gridmatrix",
            SmartArtFamily.Relationship => layoutId is "relationship1" or "opposingideas" or "convergingradial" or "divergingradial" or "basicvenn" or "radialvenn" or "targetlist" or "stackedvenn" or "interlockingrings",
            _ => false
        };
    }

    /// <summary>
    /// Parses a dsp:drawing XML (SmartArt cached render) into FallbackShapes on the SmartArtShape.
    /// dsp:sp elements are structurally like p:sp (spPr + txBody); dsp:grpSp like p:grpSp.
    /// </summary>
    private static void ReadDspDrawing(
        byte[] bytes,
        SmartArtShape smart,
        PresentationColorScheme scheme,
        ZipArchive? archive = null,
        string? drawingPartPath = null)
    {
        XDocument doc;
        using (var ms = new MemoryStream(bytes))
            doc = OpcXml.LoadXml(ms);

        var root = doc.Root;
        if (root is null) return;

        // dsp:drawing / dsp:spTree
        var spTree = root.Element(Dsp + "spTree");
        if (spTree is null) return;

        foreach (var el in spTree.Elements())
        {
            var shape = ReadDspElement(el, scheme, archive, drawingPartPath);
            if (shape is not null)
                smart.FallbackShapes.Add(shape);
        }
    }

    /// <summary>
    /// Reads a dsp:sp or dsp:grpSp element into a SlideShape using the existing spPr/txBody helpers.
    /// </summary>
    private static SlideShape? ReadDspElement(
        XElement el,
        PresentationColorScheme scheme,
        ZipArchive? archive = null,
        string? drawingPartPath = null)
    {
        switch (el.Name.LocalName)
        {
            case "sp":
                return ReadDspSp(el, scheme, archive, drawingPartPath);
            case "cxnSp":
                return ReadDspCxnSp(el, scheme);
            case "pic":
                return ReadDspPic(el, scheme, archive, drawingPartPath);
            case "grpSp":
                return ReadDspGrpSp(el, scheme, archive, drawingPartPath);
            default:
                return null;
        }
    }

    private static SlideShape ReadDspSp(
        XElement sp,
        PresentationColorScheme scheme,
        ZipArchive? archive = null,
        string? drawingPartPath = null)
    {
        // dsp:sp has dsp:nvSpPr/dsp:cNvPr (id, name), dsp:spPr (a: children), dsp:txBody (a: children)
        var cNvPrEl = sp.Elements().FirstOrDefault(e => e.Name.LocalName == "nvSpPr")
                        ?.Elements().FirstOrDefault(e => e.Name.LocalName == "cNvPr");

        var shape = new SlideShape
        {
            Id   = ParseUint(cNvPrEl?.Attribute("id")?.Value),
            Name = cNvPrEl?.Attribute("name")?.Value ?? string.Empty,
            AlternativeTextTitle = ReadAlternativeTextTitle(cNvPrEl),
            AlternativeText = ReadAlternativeText(cNvPrEl),
            IsDecorative = ReadDecorative(cNvPrEl),
            IsHidden = ReadHidden(cNvPrEl),
            Kind = SlideShapeKind.AutoShape
        };

        // spPr — same structure as p:spPr with a: children
        var spPrEl = sp.Elements().FirstOrDefault(e => e.Name.LocalName == "spPr");
        if (spPrEl is not null)
        {
            // Build a synthetic a:spPr element so we can reuse ReadSpPr (it uses the A namespace)
            var aSpPr = new XElement(A + "spPr", spPrEl.Attributes(), spPrEl.Elements());
            var blipResolver = (archive is not null && !string.IsNullOrWhiteSpace(drawingPartPath))
                ? BuildBlipResolver(archive, OpcRelationships.LoadTargets(archive, GetRelationshipPartPath(drawingPartPath)), drawingPartPath)
                : null;
            ReadSpPr(aSpPr, shape, scheme, blipResolver);

            var prst = aSpPr.Element(A + "prstGeom")?.Attribute("prst")?.Value;
            shape.AutoShapeKind = PptxShapeKindMap.FromPreset(prst);
            ReadPresetGeometryAdjustments(aSpPr, shape);

            // PowerPoint's cached hierarchy SmartArt drawing represents connector
            // segments as empty dsp:sp elements with a line and an xfrm, but no
            // preset geometry.  Treating those as the default rectangle paints
            // the connector bounding box instead of its thin line.  Keep this
            // scoped to geometry-less, textless cached shapes so ordinary
            // geometry-less text boxes and normal slide shapes retain their
            // existing fallback behavior.
            if (string.IsNullOrWhiteSpace(prst) && string.IsNullOrWhiteSpace(shape.PlainText))
                shape.AutoShapeKind = DrawingShapeKind.Line;
        }

        // txBody
        var txBodyEl = sp.Elements().FirstOrDefault(e => e.Name.LocalName == "txBody");
        if (txBodyEl is not null)
        {
            // dsp:txBody uses a: children — same as p:txBody
            var aTxBody = new XElement(A + "txBody", txBodyEl.Attributes(), txBodyEl.Elements());
            shape.TextBody = ReadTxBody(aTxBody, scheme);

            // SmartArt's cached drawing can carry its default foreground through
            // dsp:style/a:fontRef instead of individual a:rPr elements.
            var fontRef = sp.Elements().FirstOrDefault(e => e.Name.LocalName == "style")
                ?.Element(A + "fontRef");
            var fontColor = PptxColorReader.TryReadColor(fontRef, scheme);
            if (fontColor is not null)
            {
                foreach (var paragraph in shape.TextBody.Paragraphs)
                {
                    foreach (var run in paragraph.Runs.Where(run => run.Color is null))
                        run.Color = fontColor;
                }
            }
        }

        return shape;
    }

    private static SlideShape ReadDspCxnSp(
        XElement cxnSp,
        PresentationColorScheme scheme)
    {
        // Some Office producers use a native connector element in the SmartArt
        // drawing cache instead of the more common line-shaped dsp:sp form.  Keep
        // it as a connector fallback rather than silently dropping the cached edge.
        var cNvPrEl = cxnSp.Elements()
            .FirstOrDefault(element => element.Name.LocalName == "nvCxnSpPr")
            ?.Elements()
            .FirstOrDefault(element => element.Name.LocalName == "cNvPr");

        var shape = new SlideShape
        {
            Id = ParseUint(cNvPrEl?.Attribute("id")?.Value),
            Name = cNvPrEl?.Attribute("name")?.Value ?? string.Empty,
            AlternativeTextTitle = ReadAlternativeTextTitle(cNvPrEl),
            AlternativeText = ReadAlternativeText(cNvPrEl),
            IsDecorative = ReadDecorative(cNvPrEl),
            IsHidden = ReadHidden(cNvPrEl),
            Kind = SlideShapeKind.Connector,
        };

        var connectionProperties = cxnSp.Elements()
            .FirstOrDefault(element => element.Name.LocalName == "nvCxnSpPr")
            ?.Elements()
            .FirstOrDefault(element => element.Name.LocalName == "cNvCxnSpPr");
        if (connectionProperties is not null)
        {
            shape.ConnectionStart = ReadDspConnectorAttachment(
                connectionProperties.Elements().FirstOrDefault(element => element.Name.LocalName == "stCxn"));
            shape.ConnectionEnd = ReadDspConnectorAttachment(
                connectionProperties.Elements().FirstOrDefault(element => element.Name.LocalName == "endCxn"));
        }

        var spPr = cxnSp.Elements().FirstOrDefault(element => element.Name.LocalName == "spPr");
        if (spPr is not null)
        {
            var aSpPr = new XElement(A + "spPr", spPr.Attributes(), spPr.Elements());
            ReadSpPr(aSpPr, shape, scheme);
            var preset = aSpPr.Element(A + "prstGeom")?.Attribute("prst")?.Value;
            shape.AutoShapeKind = PptxShapeKindMap.FromPreset(preset);
        }

        return shape;
    }

    private static ConnectorAttachment? ReadDspConnectorAttachment(XElement? element)
    {
        if (element is null ||
            !uint.TryParse(element.Attribute("id")?.Value, out var shapeId) ||
            !int.TryParse(element.Attribute("idx")?.Value, out var siteIndex))
        {
            return null;
        }

        return new ConnectorAttachment { ShapeId = shapeId, SiteIndex = siteIndex };
    }

    private static SlideShape ReadDspPic(
        XElement pic,
        PresentationColorScheme scheme,
        ZipArchive? archive = null,
        string? drawingPartPath = null)
    {
        var cNvPrEl = pic.Elements().FirstOrDefault(e => e.Name.LocalName == "nvPicPr")
                        ?.Elements().FirstOrDefault(e => e.Name.LocalName == "cNvPr");

        var shape = new SlideShape
        {
            Id = ParseUint(cNvPrEl?.Attribute("id")?.Value),
            Name = cNvPrEl?.Attribute("name")?.Value ?? string.Empty,
            AlternativeTextTitle = ReadAlternativeTextTitle(cNvPrEl),
            AlternativeText = ReadAlternativeText(cNvPrEl),
            IsDecorative = ReadDecorative(cNvPrEl),
            IsHidden = ReadHidden(cNvPrEl),
            Kind = SlideShapeKind.Picture
        };

        var spPrEl = pic.Elements().FirstOrDefault(e => e.Name.LocalName == "spPr");
        if (spPrEl is not null)
        {
            var aSpPr = new XElement(A + "spPr", spPrEl.Attributes(), spPrEl.Elements());
            ReadSpPr(aSpPr, shape, scheme);

            var prst = aSpPr.Element(A + "prstGeom")?.Attribute("prst")?.Value;
            if (!string.IsNullOrEmpty(prst) && prst != "rect")
                shape.PictureFrameGeometry = prst;
        }

        var blipFillEl = pic.Elements().FirstOrDefault(e => e.Name.LocalName == "blipFill");
        var blip = blipFillEl?.Elements().FirstOrDefault(e => e.Name == A + "blip" || e.Name.LocalName == "blip");
        if (archive is not null && !string.IsNullOrWhiteSpace(drawingPartPath))
        {
            var rels = OpcRelationships.LoadTargets(archive, GetRelationshipPartPath(drawingPartPath));
            shape.Picture = LoadImageFromBlip(blip, rels, GetDirectoryName(drawingPartPath), archive);
        }

        if (blipFillEl is not null || blip is not null)
            shape.PictureFormat = ReadPictureFormat(blipFillEl, blip);

        return shape;
    }

    private static SlideShape ReadDspGrpSp(
        XElement grpSp,
        PresentationColorScheme scheme,
        ZipArchive? archive = null,
        string? drawingPartPath = null)
    {
        var cNvPrEl = grpSp.Elements().FirstOrDefault(e => e.Name.LocalName == "nvGrpSpPr")
                           ?.Elements().FirstOrDefault(e => e.Name.LocalName == "cNvPr");

        var shape = new SlideShape
        {
            Id   = ParseUint(cNvPrEl?.Attribute("id")?.Value),
            Name = cNvPrEl?.Attribute("name")?.Value ?? string.Empty,
            AlternativeTextTitle = ReadAlternativeTextTitle(cNvPrEl),
            AlternativeText = ReadAlternativeText(cNvPrEl),
            IsDecorative = ReadDecorative(cNvPrEl),
            IsHidden = ReadHidden(cNvPrEl),
            Kind = SlideShapeKind.Group
        };

        var grpSpPrEl = grpSp.Elements().FirstOrDefault(e => e.Name.LocalName == "grpSpPr");
        if (grpSpPrEl is not null)
        {
            var aGrpSpPr = new XElement(A + "spPr", grpSpPrEl.Attributes(), grpSpPrEl.Elements());
            ReadSpPr(aGrpSpPr, shape, scheme);
        }

        // Recurse children
        var spTreeEl = grpSp.Elements().FirstOrDefault(e => e.Name.LocalName == "spTree")
                     ?? grpSp; // some encoders put children directly inside grpSp
        foreach (var child in spTreeEl.Elements())
        {
            var childShape = ReadDspElement(child, scheme, archive, drawingPartPath);
            if (childShape is not null)
                shape.Children.Add(childShape);
        }

        return shape;
    }

    // ── a:tbl table parsing ───────────────────────────────────────────────────────

    private static TableShape ReadTable(
        XElement tblEl, PresentationColorScheme scheme,
        Dictionary<string, TableStyleData>? tableStyles)
    {
        var table = new TableShape();

        // tblPr — flags + styleId
        var tblPr = tblEl.Element(A + "tblPr");
        if (tblPr is not null)
        {
            table.Flags.FirstRow = tblPr.Attribute("firstRow")?.Value is "1" or "true";
            table.Flags.LastRow  = tblPr.Attribute("lastRow")?.Value  is "1" or "true";
            table.Flags.FirstCol = tblPr.Attribute("firstCol")?.Value is "1" or "true";
            table.Flags.LastCol  = tblPr.Attribute("lastCol")?.Value  is "1" or "true";
            // OOXML default for bandRow/bandCol is false; treat absent attribute as false.
            table.Flags.BandRow  = tblPr.Attribute("bandRow")?.Value  is "1" or "true";
            table.Flags.BandCol  = tblPr.Attribute("bandCol")?.Value  is "1" or "true";

            var styleId = tblPr.Element(A + "tableStyleId")?.Value?.Trim();
            if (!string.IsNullOrWhiteSpace(styleId))
            {
                table.TableStyleId = styleId;
                if (tableStyles?.TryGetValue(styleId, out var styleData) == true)
                    table.StyleData = styleData;
            }
        }

        // tblGrid — column widths
        foreach (var gridCol in tblEl.Element(A + "tblGrid")?.Elements(A + "gridCol") ?? Enumerable.Empty<XElement>())
        {
            if (long.TryParse(gridCol.Attribute("w")?.Value,
                    System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture, out var colW))
                table.ColumnWidthsEmu.Add(colW);
        }

        // tr — rows
        foreach (var trEl in tblEl.Elements(A + "tr"))
        {
            long rowH = ParseLong(trEl.Attribute("h")?.Value);
            var row = new TableRow { HeightEmu = rowH };

            foreach (var tcEl in trEl.Elements(A + "tc"))
                row.Cells.Add(ReadTableCell(tcEl, scheme));

            table.Rows.Add(row);
        }

        return table;
    }

    private static TableCell ReadTableCell(XElement tcEl, PresentationColorScheme scheme)
    {
        var cell = new TableCell();

        // Merge attributes.
        if (int.TryParse(tcEl.Attribute("gridSpan")?.Value, out var gs) && gs > 1)
            cell.GridSpan = gs;
        if (int.TryParse(tcEl.Attribute("rowSpan")?.Value, out var rs) && rs > 1)
            cell.RowSpan = rs;
        cell.HMerge = tcEl.Attribute("hMerge")?.Value is "1" or "true";
        cell.VMerge = tcEl.Attribute("vMerge")?.Value is "1" or "true";

        // txBody
        var txBody = tcEl.Element(A + "txBody");
        if (txBody is not null)
            cell.TextBody = ReadTxBodyFromA(txBody, scheme);

        // tcPr
        var tcPr = tcEl.Element(A + "tcPr");
        if (tcPr is not null)
        {
            // Insets (EMU -> points)
            if (ParseLongNullable(tcPr.Attribute("marL")?.Value) is { } ml) cell.InsetLeftPt   = DrawingMlUnits.EmuToPoints(ml);
            if (ParseLongNullable(tcPr.Attribute("marR")?.Value) is { } mr) cell.InsetRightPt  = DrawingMlUnits.EmuToPoints(mr);
            if (ParseLongNullable(tcPr.Attribute("marT")?.Value) is { } mt) cell.InsetTopPt    = DrawingMlUnits.EmuToPoints(mt);
            if (ParseLongNullable(tcPr.Attribute("marB")?.Value) is { } mb) cell.InsetBottomPt = DrawingMlUnits.EmuToPoints(mb);

            // Vertical anchor
            cell.Anchor = tcPr.Attribute("anchor")?.Value switch
            {
                "ctr"  => TableCellAnchor.Middle,
                "b"    => TableCellAnchor.Bottom,
                "t"    => TableCellAnchor.Top,
                _      => (TableCellAnchor?)null
            };

            // Explicit fill. The schema stores fill properties directly under tcPr;
            // accept the older nested form as well so existing FreeP files remain readable.
            var fillOwner = tcPr.Element(A + "fill") ?? tcPr;
            cell.Fill = PptxColorReader.TryReadFill(fillOwner, scheme);

            // Per-side borders
            var borders = new TableCellBorders
            {
                Left   = PptxColorReader.TryReadOutline(tcPr.Element(A + "lnL"), scheme),
                Right  = PptxColorReader.TryReadOutline(tcPr.Element(A + "lnR"), scheme),
                Top    = PptxColorReader.TryReadOutline(tcPr.Element(A + "lnT"), scheme),
                Bottom = PptxColorReader.TryReadOutline(tcPr.Element(A + "lnB"), scheme)
            };

            if (borders.Left is not null || borders.Right is not null ||
                borders.Top is not null  || borders.Bottom is not null)
                cell.Borders = borders;
        }

        return cell;
    }

    // Reads a:txBody (used inside table cells — same element structure as p:txBody but already in A: namespace).
    private static TextBody ReadTxBodyFromA(XElement txBody, PresentationColorScheme scheme)
    {
        // Reuse the existing ReadTxBody but it expects the inner A: elements which is exactly what a:txBody has.
        return ReadTxBody(txBody, scheme);
    }

    // ── p:sp ─────────────────────────────────────────────────────────────────────

    private static SlideShape ReadSp(XElement sp, PresentationColorScheme scheme,
        IReadOnlyList<OpcRelationshipTarget>? slideRels = null,
        List<Slide>? allSlides = null,
        string? slideDir = null,
        IReadOnlyDictionary<string, string>? slidePartPathToId = null,
        ZipArchive? archive = null,
        string? partPath = null)
    {
        var cNvPr = sp.Element(P + "nvSpPr")?.Element(P + "cNvPr");
        var nvPr = sp.Element(P + "nvSpPr")?.Element(P + "nvPr");

        var shape = new SlideShape
        {
            Id = ParseUint(cNvPr?.Attribute("id")?.Value),
            Name = cNvPr?.Attribute("name")?.Value ?? string.Empty,
            AlternativeTextTitle = ReadAlternativeTextTitle(cNvPr),
            AlternativeText = ReadAlternativeText(cNvPr),
            IsDecorative = ReadDecorative(cNvPr),
            IsHidden = ReadHidden(cNvPr),
            Kind = SlideShapeKind.AutoShape
        };

        // Shape-level hyperlink: a:hlinkClick inside cNvPr.
        var shapeHlink = cNvPr?.Element(A + "hlinkClick");
        if (shapeHlink is not null)
            shape.Hyperlink = ResolveHlinkClick(shapeHlink, slideRels, allSlides, slideDir, slidePartPathToId);

        var ph = nvPr?.Element(P + "ph");
        if (ph is not null) shape.Placeholder = ReadPlaceholder(ph);

        var spPr = sp.Element(P + "spPr");
        var blipResolver = (archive is not null && slideRels is not null && partPath is not null)
            ? BuildBlipResolver(archive, slideRels, partPath)
            : null;
        ReadSpPr(spPr, shape, scheme, blipResolver);

        var prst = spPr?.Element(A + "prstGeom")?.Attribute("prst")?.Value;
        shape.AutoShapeKind = PptxShapeKindMap.FromPreset(prst);
        ReadPresetGeometryAdjustments(spPr, shape);

        var txBody = sp.Element(P + "txBody");
        if (txBody is not null) shape.TextBody = ReadTxBody(txBody, scheme, slideRels, allSlides, slideDir, slidePartPathToId, archive, partPath);

        ApplyShapeStyleReferences(sp.Element(P + "style"), shape, scheme);

        return shape;
    }

    // ── p:pic ────────────────────────────────────────────────────────────────────

    private static SlideShape ReadPic(XElement pic, ZipArchive archive, string partPath, PresentationColorScheme scheme,
        IReadOnlyList<OpcRelationshipTarget>? slideRels = null,
        List<Slide>? allSlides = null,
        string? slideDir = null,
        IReadOnlyDictionary<string, string>? slidePartPathToId = null)
    {
        var cNvPr = pic.Element(P + "nvPicPr")?.Element(P + "cNvPr");
        var nvPr  = pic.Element(P + "nvPicPr")?.Element(P + "nvPr");

        var shape = new SlideShape
        {
            Id = ParseUint(cNvPr?.Attribute("id")?.Value),
            Name = cNvPr?.Attribute("name")?.Value ?? string.Empty,
            AlternativeTextTitle = ReadAlternativeTextTitle(cNvPr),
            AlternativeText = ReadAlternativeText(cNvPr),
            IsDecorative = ReadDecorative(cNvPr),
            IsHidden = ReadHidden(cNvPr),
            Kind = SlideShapeKind.Picture
        };

        var spPr = pic.Element(P + "spPr");
        ReadSpPr(spPr, shape, scheme);
        // P3: also carry the picture's outline (a:ln inside p:spPr) — already handled by ReadSpPr.

        // Wave 26: read picture frame geometry (prstGeom prst= from p:spPr) so the renderer
        // can clip the image to a rounded-rect or ellipse.
        var picPrst = spPr?.Element(A + "prstGeom")?.Attribute("prst")?.Value;
        if (!string.IsNullOrEmpty(picPrst) && picPrst != "rect")
            shape.PictureFrameGeometry = picPrst;

        // blipFill → poster / image
        var blipFillEl = pic.Element(P + "blipFill");
        var blip = blipFillEl?.Element(A + "blip");
        var embedId = blip?.Attribute(R + "embed")?.Value;
        if (!string.IsNullOrWhiteSpace(embedId))
        {
            var partRels = OpcRelationships.LoadTargets(archive, GetRelationshipPartPath(partPath));
            var imageTarget = partRels.FirstOrDefault(r => r.Id == embedId && r.Type == ImageRelType).Target;
            if (!string.IsNullOrWhiteSpace(imageTarget))
            {
                var imagePath = ResolveRelativeZipPath(GetDirectoryName(partPath), imageTarget);
                var entry = archive.GetEntry(imagePath);
                if (entry is not null)
                {
                    using var imgStream = entry.Open();
                    using var ms = new MemoryStream();
                    imgStream.CopyTo(ms);
                    shape.Picture = new ImagePart
                    {
                        Bytes = ms.ToArray(),
                        ContentType = OpcMediaTypes.GetDrawingMediaContentType(imagePath)
                    };
                }
            }
        }

        // 18A: parse crop (a:srcRect) and blip colour effects
        if (blipFillEl is not null || blip is not null)
            shape.PictureFormat = ReadPictureFormat(blipFillEl, blip);

        // Detect media (audio/video) — a:videoFile or a:audioFile inside p:nvPr
        if (nvPr is not null)
        {
            var videoFileEl = nvPr.Element(A + "videoFile");
            var audioFileEl = nvPr.Element(A + "audioFile");
            var mediaEl     = videoFileEl ?? audioFileEl;
            if (mediaEl is not null)
            {
                bool isVideo  = videoFileEl is not null;
                var  mediaRelId = mediaEl.Attribute(R + "link")?.Value
                               ?? mediaEl.Attribute(R + "embed")?.Value;

                var mediaInfo = new MediaInfo { IsVideo = isVideo };

                if (!string.IsNullOrWhiteSpace(mediaRelId))
                {
                    var partRels = OpcRelationships.LoadTargets(archive, GetRelationshipPartPath(partPath));
                    var mediaRel = partRels.FirstOrDefault(r => r.Id == mediaRelId);
                    if (!string.IsNullOrEmpty(mediaRel.Target))
                    {
                        if (mediaRel.Type == HyperlinkRelType ||
                            mediaRel.Target.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                        {
                            // External / link-only
                            mediaInfo.LinkUrl = mediaRel.Target;
                        }
                        else
                        {
                            // Embedded
                            var mediaPath  = ResolveRelativeZipPath(GetDirectoryName(partPath), mediaRel.Target);
                            var mediaBytes = ReadEntryBytes(archive, mediaPath);
                            if (mediaBytes is not null)
                            {
                                mediaInfo.Bytes       = mediaBytes;
                                mediaInfo.ContentType = OpcMediaTypes.GetAudioVideoContentType(mediaPath);
                                mediaInfo.SourcePackagePath = mediaPath;
                            }
                        }
                    }
                }

                // Also try p:extLst media references (newer PowerPoint embeds via p:media)
                if (mediaInfo.Bytes.Length == 0 && string.IsNullOrEmpty(mediaInfo.LinkUrl))
                {
                    var extLst = nvPr.Element(P + "extLst");
                    if (extLst is not null)
                    {
                        var partRels = OpcRelationships.LoadTargets(archive, GetRelationshipPartPath(partPath));
                        foreach (var ext in extLst.Elements(P + "ext"))
                        {
                            var mediaRef = ext.Descendants()
                                .FirstOrDefault(e => e.Attribute(R + "embed") is not null
                                                 || e.Attribute(R + "link") is not null);
                            if (mediaRef is null) continue;
                            var mRelId = mediaRef.Attribute(R + "embed")?.Value
                                      ?? mediaRef.Attribute(R + "link")?.Value;
                            if (string.IsNullOrEmpty(mRelId)) continue;
                            var mRel = partRels.FirstOrDefault(r => r.Id == mRelId);
                            if (string.IsNullOrEmpty(mRel.Target)) continue;
                            var mPath  = ResolveRelativeZipPath(GetDirectoryName(partPath), mRel.Target);
                            var mBytes = ReadEntryBytes(archive, mPath);
                            if (mBytes is not null)
                            {
                                mediaInfo.Bytes       = mBytes;
                                mediaInfo.ContentType = OpcMediaTypes.GetAudioVideoContentType(mPath);
                                mediaInfo.SourcePackagePath = mPath;
                            }
                            break;
                        }
                    }
                }

                foreach (var track in ReadMediaCaptionTracks(archive, partPath, nvPr, mediaRelId))
                {
                    mediaInfo.CaptionTracks.Add(track);
                }

                shape.Media = mediaInfo;
                shape.Kind  = SlideShapeKind.Media;
            }
        }

        var shapeHlink = cNvPr?.Element(A + "hlinkClick");
        if (shapeHlink is not null)
            shape.Hyperlink = ResolveHlinkClick(shapeHlink, slideRels, allSlides, slideDir, slidePartPathToId);

        return shape;
    }

    // ── p:cxnSp ──────────────────────────────────────────────────────────────────

    private static IReadOnlyList<MediaCaptionTrackInfo> ReadMediaCaptionTracks(
        ZipArchive archive,
        string partPath,
        XElement nvPr,
        string? primaryMediaRelId)
    {
        var rels = OpcRelationships.LoadTargets(archive, GetRelationshipPartPath(partPath));
        if (rels.Count == 0)
        {
            return [];
        }

        var relById = rels
            .GroupBy(rel => rel.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var defaultContentTypes = OpcMediaTypes.ReadDefaultContentTypes(archive);
        var overrideContentTypes = OpcMediaTypes.ReadOverrideContentTypes(archive);
        var candidates = new List<(OpcRelationshipTarget Rel, XElement? Metadata)>();

        foreach (var element in nvPr.DescendantsAndSelf())
        {
            foreach (var attribute in element.Attributes())
            {
                if (attribute.Name.Namespace != R
                    || (attribute.Name.LocalName != "embed"
                        && attribute.Name.LocalName != "link"
                        && attribute.Name.LocalName != "id"))
                {
                    continue;
                }

                var relId = attribute.Value;
                if (string.IsNullOrWhiteSpace(relId)
                    || string.Equals(relId, primaryMediaRelId, StringComparison.Ordinal)
                    || !relById.TryGetValue(relId, out var rel))
                {
                    continue;
                }

                if (IsCaptionTrackRelationship(rel)
                    || IsCaptionTrackTarget(rel.Target)
                    || IsCaptionTrackElement(element))
                {
                    candidates.Add((rel, element));
                }
            }
        }

        var tracks = new List<MediaCaptionTrackInfo>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (rel, metadata) in candidates)
        {
            var key = string.IsNullOrWhiteSpace(rel.Id) ? rel.Target : rel.Id;
            if (string.IsNullOrWhiteSpace(key) || !seen.Add(key))
            {
                continue;
            }

            var target = rel.Target.Trim();
            var isExternal = rel.IsExternal || IsExternalCaptionTrackTarget(target);
            var source = isExternal
                ? target
                : ResolveRelativeZipPath(GetDirectoryName(partPath), target);
            var captionBytes = isExternal
                ? Array.Empty<byte>()
                : ReadEntryBytes(archive, source) ?? Array.Empty<byte>();

            tracks.Add(new MediaCaptionTrackInfo
            {
                RelationshipId = rel.Id,
                Source = source,
                Bytes = captionBytes,
                ContentType = GetCaptionTrackContentType(source, isExternal, defaultContentTypes, overrideContentTypes),
                Language = ReadCaptionTrackLanguage(metadata),
                Label = ReadCaptionTrackLabel(metadata, source),
                IsExternal = isExternal
            });
        }

        return tracks;
    }

    private static bool IsCaptionTrackRelationship(OpcRelationshipTarget rel)
        => rel.Type.Contains("caption", StringComparison.OrdinalIgnoreCase)
            || rel.Type.Contains("subtitle", StringComparison.OrdinalIgnoreCase)
            || rel.Type.Contains("timedText", StringComparison.OrdinalIgnoreCase)
            || string.Equals(rel.Type, CaptionRelType, StringComparison.OrdinalIgnoreCase);

    private static bool IsCaptionTrackElement(XElement element)
    {
        var localName = element.Name.LocalName;
        return localName.Contains("caption", StringComparison.OrdinalIgnoreCase)
            || localName.Contains("subtitle", StringComparison.OrdinalIgnoreCase)
            || localName.Contains("timedText", StringComparison.OrdinalIgnoreCase)
            || string.Equals(localName, "track", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsCaptionTrackTarget(string? target)
    {
        if (string.IsNullOrWhiteSpace(target))
        {
            return false;
        }

        var normalized = target.Replace('\\', '/');
        if (normalized.Contains("caption", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("subtitle", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("timedtext", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return GetCaptionTrackExtension(normalized) is "vtt" or "ttml" or "dfxp" or "srt";
    }

    private static bool IsExternalCaptionTrackTarget(string target)
        => Uri.TryCreate(target, UriKind.Absolute, out var uri)
            && !string.IsNullOrWhiteSpace(uri.Scheme);

    private static string GetCaptionTrackContentType(
        string source,
        bool isExternal,
        IReadOnlyDictionary<string, string> defaultContentTypes,
        IReadOnlyDictionary<string, string> overrideContentTypes)
    {
        if (!isExternal)
        {
            var partName = "/" + source.TrimStart('/');
            if (overrideContentTypes.TryGetValue(partName, out var overrideContentType) &&
                !string.IsNullOrWhiteSpace(overrideContentType))
            {
                return overrideContentType;
            }
        }

        var extension = GetCaptionTrackExtension(source);
        if (extension.Length > 0 &&
            defaultContentTypes.TryGetValue(extension, out var defaultContentType) &&
            !string.IsNullOrWhiteSpace(defaultContentType))
        {
            return defaultContentType;
        }

        return extension switch
        {
            "vtt" => "text/vtt",
            "ttml" or "dfxp" => "application/ttml+xml",
            "srt" => "application/x-subrip",
            _ => string.Empty
        };
    }

    private static string GetCaptionTrackExtension(string source)
    {
        var end = source.AsSpan();
        var queryIndex = source.IndexOfAny(['?', '#']);
        if (queryIndex >= 0)
        {
            end = source.AsSpan(0, queryIndex);
        }

        var slashIndex = end.LastIndexOf('/');
        var fileName = slashIndex >= 0 ? end[(slashIndex + 1)..] : end;
        var dotIndex = fileName.LastIndexOf('.');
        return dotIndex >= 0 && dotIndex < fileName.Length - 1
            ? fileName[(dotIndex + 1)..].ToString().ToLowerInvariant()
            : string.Empty;
    }

    private static string ReadCaptionTrackLanguage(XElement? metadata)
        => ReadAttributeByLocalName(metadata, "lang")
            ?? ReadAttributeByLocalName(metadata, "language")
            ?? ReadAttributeByLocalName(metadata, "srclang")
            ?? string.Empty;

    private static string ReadCaptionTrackLabel(XElement? metadata, string source)
        => ReadAttributeByLocalName(metadata, "label")
            ?? ReadAttributeByLocalName(metadata, "name")
            ?? ReadAttributeByLocalName(metadata, "title")
            ?? ReadCaptionTrackLabelFromSource(source);

    private static string? ReadAttributeByLocalName(XElement? element, string localName)
        => element?.Attributes()
            .FirstOrDefault(attribute => string.Equals(
                attribute.Name.LocalName,
                localName,
                StringComparison.OrdinalIgnoreCase))
            ?.Value
            .Trim();

    private static string ReadCaptionTrackLabelFromSource(string source)
    {
        var normalized = source.Replace('\\', '/');
        var queryIndex = normalized.IndexOfAny(['?', '#']);
        if (queryIndex >= 0)
        {
            normalized = normalized[..queryIndex];
        }

        var slashIndex = normalized.LastIndexOf('/');
        var fileName = slashIndex >= 0 ? normalized[(slashIndex + 1)..] : normalized;
        return string.IsNullOrWhiteSpace(fileName) ? "Caption track" : fileName;
    }

    private static SlideShape ReadCxnSp(XElement cxnSp, PresentationColorScheme scheme,
        IReadOnlyList<OpcRelationshipTarget>? slideRels = null,
        List<Slide>? allSlides = null,
        string? slideDir = null,
        IReadOnlyDictionary<string, string>? slidePartPathToId = null,
        ZipArchive? archive = null,
        string? partPath = null)
    {
        var cNvPr = cxnSp.Element(P + "nvCxnSpPr")?.Element(P + "cNvPr");

        var shape = new SlideShape
        {
            Id = ParseUint(cNvPr?.Attribute("id")?.Value),
            Name = cNvPr?.Attribute("name")?.Value ?? string.Empty,
            AlternativeTextTitle = ReadAlternativeTextTitle(cNvPr),
            AlternativeText = ReadAlternativeText(cNvPr),
            IsDecorative = ReadDecorative(cNvPr),
            IsHidden = ReadHidden(cNvPr),
            Kind = SlideShapeKind.Connector
        };

        // Shape-level hyperlink on connector.
        var shapeHlink = cNvPr?.Element(A + "hlinkClick");
        if (shapeHlink is not null)
            shape.Hyperlink = ResolveHlinkClick(shapeHlink, slideRels, allSlides, slideDir, slidePartPathToId);

        // Connector attachment: a:stCxn / a:endCxn inside p:nvCxnSpPr/p:cNvCxnSpPr.
        var cNvCxnSpPr = cxnSp.Element(P + "nvCxnSpPr")?.Element(P + "cNvCxnSpPr");
        if (cNvCxnSpPr is not null)
        {
            var stCxnEl = cNvCxnSpPr.Element(A + "stCxn");
            if (stCxnEl is not null &&
                uint.TryParse(stCxnEl.Attribute("id")?.Value, out var stId) &&
                int.TryParse(stCxnEl.Attribute("idx")?.Value, out var stIdx))
            {
                shape.ConnectionStart = new ConnectorAttachment { ShapeId = stId, SiteIndex = stIdx };
            }

            var endCxnEl = cNvCxnSpPr.Element(A + "endCxn");
            if (endCxnEl is not null &&
                uint.TryParse(endCxnEl.Attribute("id")?.Value, out var endId) &&
                int.TryParse(endCxnEl.Attribute("idx")?.Value, out var endIdx))
            {
                shape.ConnectionEnd = new ConnectorAttachment { ShapeId = endId, SiteIndex = endIdx };
            }
        }

        var spPr = cxnSp.Element(P + "spPr");
        var blipResolver = (archive is not null && slideRels is not null && partPath is not null)
            ? BuildBlipResolver(archive, slideRels, partPath)
            : null;
        ReadSpPr(spPr, shape, scheme, blipResolver);

        var prst = spPr?.Element(A + "prstGeom")?.Attribute("prst")?.Value;
        shape.AutoShapeKind = PptxShapeKindMap.FromPreset(prst);

        return shape;
    }

    // ── p:grpSp ──────────────────────────────────────────────────────────────────

    private static SlideShape ReadGrpSp(XElement grpSp, ZipArchive archive, string partPath,
        PresentationColorScheme scheme, Dictionary<string, TableStyleData>? tableStyles = null,
        IReadOnlyList<OpcRelationshipTarget>? slideRels = null,
        List<Slide>? allSlides = null,
        string? slideDir = null,
        IReadOnlyDictionary<string, string>? slidePartPathToId = null)
    {
        var cNvPr = grpSp.Element(P + "nvGrpSpPr")?.Element(P + "cNvPr");

        var shape = new SlideShape
        {
            Id = ParseUint(cNvPr?.Attribute("id")?.Value),
            Name = cNvPr?.Attribute("name")?.Value ?? string.Empty,
            AlternativeTextTitle = ReadAlternativeTextTitle(cNvPr),
            AlternativeText = ReadAlternativeText(cNvPr),
            IsDecorative = ReadDecorative(cNvPr),
            IsHidden = ReadHidden(cNvPr),
            Kind = SlideShapeKind.Group
        };

        var shapeHlink = cNvPr?.Element(A + "hlinkClick");
        if (shapeHlink is not null)
            shape.Hyperlink = ResolveHlinkClick(shapeHlink, slideRels, allSlides, slideDir, slidePartPathToId);

        ReadSpPr(grpSp.Element(P + "grpSpPr"), shape, scheme);

        foreach (var child in ReadShapesFromTree(grpSp, archive, partPath, scheme, tableStyles, slideRels, allSlides, slideDir, slidePartPathToId))
            shape.Children.Add(child);

        return shape;
    }

    // ── spPr ─────────────────────────────────────────────────────────────────────

    private static void ReadSpPr(
        XElement? spPr,
        SlideShape shape,
        PresentationColorScheme scheme,
        Func<string, (byte[] bytes, string contentType)?>? resolveBlip = null)
    {
        if (spPr is null) return;

        var xfrm = spPr.Element(A + "xfrm");
        if (xfrm is not null)
        {
            shape.OffsetXEmu = ParseLong(xfrm.Element(A + "off")?.Attribute("x")?.Value);
            shape.OffsetYEmu = ParseLong(xfrm.Element(A + "off")?.Attribute("y")?.Value);
            var ext = xfrm.Element(A + "ext");
            shape.ExtentCxEmu = ParseLong(ext?.Attribute("cx")?.Value);
            shape.ExtentCyEmu = ParseLong(ext?.Attribute("cy")?.Value);
            shape.HasExplicitZeroExtentTransform = ext is not null &&
                shape.ExtentCxEmu == 0 && shape.ExtentCyEmu == 0;

            if (long.TryParse(xfrm.Attribute("rot")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var rotRaw))
                shape.RotationDeg = rotRaw / 60000.0;

            shape.FlipH = xfrm.Attribute("flipH")?.Value is "1" or "true";
            shape.FlipV = xfrm.Attribute("flipV")?.Value is "1" or "true";
        }

        shape.Fill = PptxColorReader.TryReadFill(spPr, scheme, resolveBlip);
        shape.Outline = PptxColorReader.TryReadOutline(spPr.Element(A + "ln"), scheme);

        // Custom geometry
        var custGeom = spPr.Element(A + "custGeom");
        if (custGeom is not null)
            ReadCustGeom(custGeom, shape);

        // Shape effects (effectLst, sp3d, scene3d all go into ShapeEffects)
        var effectLst = spPr.Element(A + "effectLst");
        var sp3d      = spPr.Element(A + "sp3d");
        var scene3d   = spPr.Element(A + "scene3d");

        if (effectLst is not null || sp3d is not null || scene3d is not null)
        {
            var fx = effectLst is not null
                ? (ReadEffectLst(effectLst, scheme) ?? new ShapeEffects())
                : new ShapeEffects();

            if (sp3d is not null)
                ReadSp3d(sp3d, fx, scheme);

            if (scene3d is not null)
                ReadScene3d(scene3d, fx);

            // Only store if there's actually something
            bool hasSomething = fx.HasOuterShadow || fx.HasInnerShadow || fx.HasGlow
                || fx.HasSoftEdge || fx.BevelTop is not null || fx.BevelBottom is not null
                || fx.ExtrusionHeightEmu != 0 || fx.ContourWidthEmu != 0
                || fx.Scene3d is not null;
            if (hasSomething)
                shape.Effects = fx;
        }
    }

    private static void ApplyShapeStyleReferences(
        XElement? style,
        SlideShape shape,
        PresentationColorScheme scheme)
    {
        if (style is null) return;

        // p:style references theme format-matrix entries. The reference color is
        // supplied alongside the index, so materialize the common inherited line
        // and font color only when the shape did not author an explicit override.
        if (shape.Outline is null)
        {
            var lineReference = style.Element(A + "lnRef");
            var lineColor = PptxColorReader.TryReadColor(lineReference, scheme);
            if (lineColor is not null)
            {
                var index = int.TryParse(lineReference?.Attribute("idx")?.Value,
                    NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedIndex)
                    ? parsedIndex
                    : 0;
                var widthPt = index switch
                {
                    1 => 1.0,
                    2 => 1.5,
                    3 => 2.0,
                    _ => 0.75
                };
                shape.Outline = new ShapeOutline.Visible(lineColor, widthPt, OutlineDash.Solid);
            }
        }

        if (shape.TextBody is null) return;

        var fontColor = PptxColorReader.TryReadColor(style.Element(A + "fontRef"), scheme);
        if (fontColor is null) return;

        foreach (var paragraph in shape.TextBody.Paragraphs)
        foreach (var run in paragraph.Runs.Where(run => run.Color is null))
            run.Color = fontColor;
    }

    // ── Blip resolver ─────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a delegate that resolves a blip embed rId to (bytes, contentType) using
    /// the slide's image rels and the archive. Used for a:blipFill shape fills.
    /// </summary>
    private static Func<string, (byte[] bytes, string contentType)?> BuildBlipResolver(
        ZipArchive archive,
        IReadOnlyList<OpcRelationshipTarget> slideRels,
        string partPath)
    {
        return (embedId) =>
        {
            var imageTarget = slideRels
                .FirstOrDefault(r => r.Id == embedId && r.Type == ImageRelType).Target;
            if (string.IsNullOrWhiteSpace(imageTarget)) return null;

            var imagePath = ResolveRelativeZipPath(GetDirectoryName(partPath), imageTarget);
            var entry = archive.GetEntry(imagePath);
            if (entry is null) return null;

            using var imgStream = entry.Open();
            using var ms = new MemoryStream();
            imgStream.CopyTo(ms);
            return (ms.ToArray(), OpcMediaTypes.GetDrawingMediaContentType(imagePath));
        };
    }

    // ── a:sp3d ───────────────────────────────────────────────────────────────

    private static void ReadSp3d(XElement sp3d, ShapeEffects fx, PresentationColorScheme scheme)
    {
        fx.ExtrusionHeightEmu = ParseLong(sp3d.Attribute("extrusionH")?.Value);
        fx.ContourWidthEmu    = ParseLong(sp3d.Attribute("contourW")?.Value);
        fx.PrstMaterial       = sp3d.Attribute("prstMaterial")?.Value ?? string.Empty;

        // a:bevelT
        var bevelT = sp3d.Element(A + "bevelT");
        if (bevelT is not null)
        {
            fx.BevelTop = new BevelInfo
            {
                WidthEmu   = ParseLongOrDefault(bevelT.Attribute("w")?.Value,  76200),
                HeightEmu  = ParseLongOrDefault(bevelT.Attribute("h")?.Value,  76200),
                PresetName = bevelT.Attribute("prst")?.Value ?? string.Empty
            };
        }

        // a:bevelB
        var bevelB = sp3d.Element(A + "bevelB");
        if (bevelB is not null)
        {
            fx.BevelBottom = new BevelInfo
            {
                WidthEmu   = ParseLongOrDefault(bevelB.Attribute("w")?.Value,  76200),
                HeightEmu  = ParseLongOrDefault(bevelB.Attribute("h")?.Value,  76200),
                PresetName = bevelB.Attribute("prst")?.Value ?? string.Empty
            };
        }

        // a:extrusionClr
        var extClr = sp3d.Element(A + "extrusionClr");
        if (extClr is not null)
        {
            var tac = PptxColorReader.TryReadColor(extClr, scheme);
            if (tac is not null) fx.ExtrusionColor = tac.Resolved;
        }

        // a:contourClr
        var ctrClr = sp3d.Element(A + "contourClr");
        if (ctrClr is not null)
        {
            var tac = PptxColorReader.TryReadColor(ctrClr, scheme);
            if (tac is not null) fx.ContourColor = tac.Resolved;
        }
    }

    // ── a:scene3d ────────────────────────────────────────────────────────────

    private static void ReadScene3d(XElement scene3d, ShapeEffects fx)
    {
        var camera   = scene3d.Element(A + "camera");
        var lightRig = scene3d.Element(A + "lightRig");

        if (camera is null && lightRig is null) return;

        fx.Scene3d = new Scene3dInfo
        {
            CameraPreset = camera?.Attribute("prst")?.Value   ?? string.Empty,
            LightRig     = lightRig?.Attribute("rig")?.Value  ?? string.Empty,
            LightRigDir  = lightRig?.Attribute("dir")?.Value  ?? string.Empty
        };
    }

    private static long ParseLongOrDefault(string? value, long defaultValue)
    {
        if (value is null) return defaultValue;
        return long.TryParse(value, System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : defaultValue;
    }

    private static void ReadCustGeom(XElement custGeom, SlideShape shape)
    {
        var pathLst = custGeom.Element(A + "pathLst");
        if (pathLst is null) return;

        foreach (var pathEl in pathLst.Elements(A + "path"))
        {
            var cgp = new CustomGeometryPath
            {
                PathW  = ParseLong(pathEl.Attribute("w")?.Value),
                PathH  = ParseLong(pathEl.Attribute("h")?.Value),
                Fill   = pathEl.Attribute("fill")?.Value   is not ("none" or "0"),
                Stroke = pathEl.Attribute("stroke")?.Value is not ("0" or "false")
            };

            foreach (var segEl in pathEl.Elements())
            {
                switch (segEl.Name.LocalName)
                {
                    case "moveTo":
                    {
                        var pt = segEl.Element(A + "pt");
                        cgp.Segments.Add(new CustomSegment(CustomSegmentKind.MoveTo,
                            X: ParseLong(pt?.Attribute("x")?.Value),
                            Y: ParseLong(pt?.Attribute("y")?.Value)));
                        break;
                    }
                    case "lnTo":
                    {
                        var pt = segEl.Element(A + "pt");
                        cgp.Segments.Add(new CustomSegment(CustomSegmentKind.LineTo,
                            X: ParseLong(pt?.Attribute("x")?.Value),
                            Y: ParseLong(pt?.Attribute("y")?.Value)));
                        break;
                    }
                    case "cubicBezTo":
                    {
                        var pts = segEl.Elements(A + "pt").ToList();
                        if (pts.Count >= 3)
                            cgp.Segments.Add(new CustomSegment(CustomSegmentKind.CubicBezTo,
                                X:  ParseLong(pts[0].Attribute("x")?.Value),
                                Y:  ParseLong(pts[0].Attribute("y")?.Value),
                                X1: ParseLong(pts[1].Attribute("x")?.Value),
                                Y1: ParseLong(pts[1].Attribute("y")?.Value),
                                X2: ParseLong(pts[2].Attribute("x")?.Value),
                                Y2: ParseLong(pts[2].Attribute("y")?.Value)));
                        break;
                    }
                    case "quadBezTo":
                    {
                        var pts = segEl.Elements(A + "pt").ToList();
                        if (pts.Count >= 2)
                            cgp.Segments.Add(new CustomSegment(CustomSegmentKind.QuadBezTo,
                                X:  ParseLong(pts[0].Attribute("x")?.Value),
                                Y:  ParseLong(pts[0].Attribute("y")?.Value),
                                X1: ParseLong(pts[1].Attribute("x")?.Value),
                                Y1: ParseLong(pts[1].Attribute("y")?.Value)));
                        break;
                    }
                    case "arcTo":
                    {
                        // arcTo attributes wR, hR, stAng, swAng are in 1/60000 degrees
                        cgp.Segments.Add(new CustomSegment(CustomSegmentKind.ArcTo,
                            WR:    ParseDouble(segEl.Attribute("wR")?.Value),
                            HR:    ParseDouble(segEl.Attribute("hR")?.Value),
                            StAng: ParseDouble(segEl.Attribute("stAng")?.Value) / 60000.0,
                            SwAng: ParseDouble(segEl.Attribute("swAng")?.Value) / 60000.0));
                        break;
                    }
                    case "close":
                        cgp.Segments.Add(new CustomSegment(CustomSegmentKind.Close));
                        break;
                }
            }

            // Cached SmartArt connector paths may omit a:path/@w and @h even
            // though their points use a local coordinate space. Infer the
            // authored path extent before the compositor maps the path into
            // the shape bounds; otherwise a long horizontal branch is scaled
            // as though its source units were the already-rendered DIP box.
            if (cgp.PathW <= 0 && cgp.Segments.Count > 0)
            {
                var maxX = cgp.Segments.Max(segment => Math.Max(segment.X,
                    Math.Max(segment.X1, Math.Max(segment.X2, segment.X3))));
                cgp.PathW = Math.Max(1, (long)Math.Ceiling(maxX));
            }

            if (cgp.PathH <= 0 && cgp.Segments.Count > 0)
            {
                var maxY = cgp.Segments.Max(segment => Math.Max(segment.Y,
                    Math.Max(segment.Y1, Math.Max(segment.Y2, segment.Y3))));
                cgp.PathH = Math.Max(1, (long)Math.Ceiling(maxY));
            }

            if (cgp.Segments.Count > 0)
                shape.CustomGeometry.Add(cgp);
        }
    }

    private static void ReadPresetGeometryAdjustments(XElement? spPr, SlideShape shape)
    {
        var avLst = spPr?.Element(A + "prstGeom")?.Element(A + "avLst");
        if (avLst is null)
            return;

        foreach (var guide in avLst.Elements(A + "gd"))
        {
            var name = guide.Attribute("name")?.Value;
            var formula = guide.Attribute("fmla")?.Value;
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(formula))
                continue;

            var valueText = formula.StartsWith("val ", StringComparison.OrdinalIgnoreCase)
                ? formula[4..].Trim()
                : null;
            if (valueText is not null && double.TryParse(
                    valueText,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var value))
            {
                shape.PresetGeometryAdjustments[name] = value;
            }
        }
    }

    private static ShapeEffects? ReadEffectLst(XElement effectLst, PresentationColorScheme scheme)
    {
        var fx = new ShapeEffects();
        bool any = false;

        // a:outerShdw
        var outerShdw = effectLst.Element(A + "outerShdw");
        if (outerShdw is not null)
        {
            fx.HasOuterShadow = true; any = true;
            fx.OuterShadowBlurRadEmu = ParseLong(outerShdw.Attribute("blurRad")?.Value);
            fx.OuterShadowDistEmu    = ParseLong(outerShdw.Attribute("dist")?.Value);
            fx.OuterShadowDirDeg     = ParseDouble(outerShdw.Attribute("dir")?.Value) / 60000.0;
            var colorEl = outerShdw.Elements().FirstOrDefault();
            if (colorEl is not null)
            {
                var tac = PptxColorReader.TryReadColor(outerShdw, scheme);
                if (tac is not null)
                {
                    fx.OuterShadowColor = tac.Resolved;
                    fx.OuterShadowAlpha = ReadAlphaFromColorEl(colorEl);
                }
            }
        }

        // a:innerShdw
        var innerShdw = effectLst.Element(A + "innerShdw");
        if (innerShdw is not null)
        {
            fx.HasInnerShadow = true; any = true;
            fx.InnerShadowBlurRadEmu = ParseLong(innerShdw.Attribute("blurRad")?.Value);
            fx.InnerShadowDistEmu    = ParseLong(innerShdw.Attribute("dist")?.Value);
            fx.InnerShadowDirDeg     = ParseDouble(innerShdw.Attribute("dir")?.Value) / 60000.0;
            var colorEl = innerShdw.Elements().FirstOrDefault();
            if (colorEl is not null)
            {
                var tac = PptxColorReader.TryReadColor(innerShdw, scheme);
                if (tac is not null)
                {
                    fx.InnerShadowColor = tac.Resolved;
                    fx.InnerShadowAlpha = ReadAlphaFromColorEl(colorEl);
                }
            }
        }

        // a:glow
        var glow = effectLst.Element(A + "glow");
        if (glow is not null)
        {
            fx.HasGlow = true; any = true;
            fx.GlowRadiusEmu = ParseLong(glow.Attribute("rad")?.Value);
            var colorEl = glow.Elements().FirstOrDefault();
            if (colorEl is not null)
            {
                var tac = PptxColorReader.TryReadColor(glow, scheme);
                if (tac is not null)
                {
                    fx.GlowColor = tac.Resolved;
                    fx.GlowAlpha = ReadAlphaFromColorEl(colorEl);
                }
            }
        }

        // a:softEdge
        var softEdge = effectLst.Element(A + "softEdge");
        if (softEdge is not null)
        {
            fx.HasSoftEdge = true; any = true;
            fx.SoftEdgeRadEmu = ParseLong(softEdge.Attribute("rad")?.Value);
        }

        return any ? fx : null;
    }

    private static byte ReadAlphaFromColorEl(XElement colorEl)
    {
        // Look for a:alpha child directly on the color element
        var alphaEl = colorEl.Element(A + "alpha");
        if (alphaEl is not null && long.TryParse(alphaEl.Attribute("val")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var alpha100k))
            return (byte)(alpha100k * 255 / 100000);
        return 0x80; // default ~50% opacity
    }

    private static double ParseDouble(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return 0;
        return double.TryParse(value, NumberStyles.Float,
            CultureInfo.InvariantCulture, out var v) ? v : 0;
    }

    // ── TextBody ─────────────────────────────────────────────────────────────────

    private static TextBody ReadTxBody(XElement txBody, PresentationColorScheme scheme,
        IReadOnlyList<OpcRelationshipTarget>? slideRels = null,
        List<Slide>? allSlides = null,
        string? slideDir = null,
        IReadOnlyDictionary<string, string>? slidePartPathToId = null,
        ZipArchive? archive = null,
        string? partPath = null)
    {
        var body = new TextBody();

        var bodyPr = txBody.Element(A + "bodyPr");
        if (bodyPr is not null)
        {
            // Parse anchor only when explicitly present (null = inherit from layout/master).
            body.Anchor = bodyPr.Attribute("anchor")?.Value switch
            {
                "ctr" => VerticalAnchor.Middle,
                "b" => VerticalAnchor.Bottom,
                "t" => VerticalAnchor.Top,
                "dist" => VerticalAnchor.Distributed,
                _ => (VerticalAnchor?)null
            };

            if (ParseLongNullable(bodyPr.Attribute("lIns")?.Value) is { } li) body.InsetLeftPt = DrawingMlUnits.EmuToPoints(li);
            if (ParseLongNullable(bodyPr.Attribute("rIns")?.Value) is { } ri) body.InsetRightPt = DrawingMlUnits.EmuToPoints(ri);
            if (ParseLongNullable(bodyPr.Attribute("tIns")?.Value) is { } ti) body.InsetTopPt = DrawingMlUnits.EmuToPoints(ti);
            if (ParseLongNullable(bodyPr.Attribute("bIns")?.Value) is { } bi) body.InsetBottomPt = DrawingMlUnits.EmuToPoints(bi);
            body.Wrap = bodyPr.Attribute("wrap")?.Value != "none";
            var normAf = bodyPr.Element(A + "normAutofit");
            var spAf   = bodyPr.Element(A + "spAutoFit");
            body.AutoFitKind = normAf is not null
                ? TextAutoFitKind.Normal
                : spAf is not null
                    ? TextAutoFitKind.Shape
                    : TextAutoFitKind.None;
            // Wave 19A: parse cached normAutofit scaling values
            if (normAf is not null)
            {
                if (int.TryParse(normAf.Attribute("fontScale")?.Value, out var fs) && fs > 0)
                    body.FontScalePPT = fs;
                if (int.TryParse(normAf.Attribute("lnSpcReduction")?.Value, out var lsr) && lsr > 0)
                    body.LnSpcReductionPPT = lsr;
            }

            // Wave 18B: text vertical orientation (a:bodyPr vert=)
            body.VerticalType = bodyPr.Attribute("vert")?.Value switch
            {
                "vert"           => TextVerticalType.Vertical,
                "vert270"        => TextVerticalType.Vertical270,
                "eaVert"         => TextVerticalType.EastAsianVertical,
                "wordArtVert"    => TextVerticalType.WordArtVertical,
                "wordArtVertRtl" => TextVerticalType.WordArtVerticalRtl,
                _                => TextVerticalType.Horizontal
            };

            // Wave 16A: warp preset + adjust guides (BA4)
            var prstTxWarp = bodyPr.Element(A + "prstTxWarp");
            if (prstTxWarp is not null)
            {
                body.WarpPreset = prstTxWarp.Attribute("prst")?.Value;
                foreach (var gd in prstTxWarp.Element(A + "avLst")?.Elements(A + "gd")
                                   ?? Enumerable.Empty<XElement>())
                {
                    var gdName = gd.Attribute("name")?.Value;
                    var gdFmla = gd.Attribute("fmla")?.Value;
                    if (gdName is not null && gdFmla is not null)
                        body.WarpAdjusts.Add((gdName, gdFmla));
                }
            }

            // WordArt 3-D data is stored on a:bodyPr rather than p:spPr.
            var textSp3d = bodyPr.Element(A + "sp3d");
            var textScene3d = bodyPr.Element(A + "scene3d");
            if (textSp3d is not null || textScene3d is not null)
            {
                var textEffects = new ShapeEffects();
                if (textSp3d is not null)
                    ReadSp3d(textSp3d, textEffects, scheme);
                if (textScene3d is not null)
                    ReadScene3d(textScene3d, textEffects);
                body.Text3dEffects = textEffects;
            }

            // Wave 22B: text columns (a:bodyPr numCol= spcCol=)
            if (int.TryParse(bodyPr.Attribute("numCol")?.Value, out var numCol) && numCol > 1)
                body.ColumnCount = numCol;
            if (ParseLongNullable(bodyPr.Attribute("spcCol")?.Value) is { } spcColEmu)
                body.ColumnSpacingEmu = spcColEmu;
        }

        // Parse a:lstStyle — full per-level paragraph/run defaults.
        var lstStyle = txBody.Element(A + "lstStyle");
        if (lstStyle is not null)
        {
            // Quick compat: keep existing DefaultParaAlign from lvl1pPr algn
            var lvl1Algn = lstStyle.Element(A + "lvl1pPr")?.Attribute("algn")?.Value;
            body.DefaultParaAlign = lvl1Algn switch
            {
                "ctr" => TextAlign.Center,
                "r" => TextAlign.Right,
                "just" => TextAlign.Justify,
                "dist" => TextAlign.Distributed,
                "l" => TextAlign.Left,
                _ => (TextAlign?)null
            };
            body.DefaultParaRightToLeft = ParseNullableBoolean(
                lstStyle.Element(A + "lvl1pPr")?.Attribute("rtl")?.Value);

            // Full lstStyle — read all 9 levels if any are present
            bool hasAny = false;
            var levels = new TextStyleLevels();
            for (int i = 1; i <= 9; i++)
            {
                var lvlEl = lstStyle.Element(A + $"lvl{i}pPr");
                if (lvlEl is null) continue;
                levels[i - 1] = ReadTextStyleLevel(lvlEl, scheme);
                hasAny = true;
            }
            if (hasAny) body.LstStyle = levels;
        }

        foreach (var pEl in txBody.Elements(A + "p"))
            body.Paragraphs.Add(ReadParagraph(pEl, scheme, slideRels, allSlides, slideDir, slidePartPathToId, archive, partPath));

        return body;
    }

    private static Paragraph ReadParagraph(XElement pEl, PresentationColorScheme scheme,
        IReadOnlyList<OpcRelationshipTarget>? slideRels = null,
        List<Slide>? allSlides = null,
        string? slideDir = null,
        IReadOnlyDictionary<string, string>? slidePartPathToId = null,
        ZipArchive? archive = null,
        string? partPath = null)
    {
        var para = new Paragraph();
        var pPr = pEl.Element(A + "pPr");
        if (pPr is not null)
        {
            para.Align = pPr.Attribute("algn")?.Value switch
            {
                "ctr" => TextAlign.Center,
                "r" => TextAlign.Right,
                "just" => TextAlign.Justify,
                "dist" => TextAlign.Distributed,
                "l" => TextAlign.Left,
                _ => (TextAlign?)null
            };
            para.RightToLeft = ParseNullableBoolean(pPr.Attribute("rtl")?.Value);

            if (int.TryParse(pPr.Attribute("lvl")?.Value, out var lvl)) para.Level = Math.Clamp(lvl, 0, 8); // BU3: clamp to valid array range [0,8]

            if (pPr.Element(A + "buNone") is not null)
            {
                para.BulletKind = BulletKind.None;
                para.BulletSuppressed = true;   // BU1: explicit <a:buNone/> — suppress inheritance
            }
            else if (pPr.Element(A + "buChar") is { } buChar)
            {
                para.BulletKind = BulletKind.Char;
                para.BulletChar = buChar.Attribute("char")?.Value ?? "•";
            }
            else if (pPr.Element(A + "buAutoNum") is { } buAutoNum)
            {
                para.BulletKind = BulletKind.Auto;
                para.AutoNumType = ParseAutoNumType(buAutoNum.Attribute("type")?.Value);
                if (int.TryParse(buAutoNum.Attribute("startAt")?.Value, out var startAt) && startAt >= 1)
                {
                    para.AutoNumStartAt = startAt;
                    para.AutoNumStartAtSpecified = true;
                }

                var templateExtension = pPr.Element(A + "extLst")?
                    .Elements(A + "ext")
                    .FirstOrDefault(extension =>
                        string.Equals(extension.Attribute("uri")?.Value, AutoNumTemplateExtUri, StringComparison.Ordinal));
                para.AutoNumTextTemplate = templateExtension?
                    .Attribute(FreePText + "autoNumTemplate")?.Value;
            }
            else if (pPr.Element(A + "buBlip") is { } buBlip)
            {
                para.BulletImage = ReadBulletImage(buBlip, slideRels, archive, partPath);
                if (para.BulletImage is not null)
                    para.BulletKind = BulletKind.Image;
            }

            // Wave 19A: marL/indent/buClr/buSzPct/buFont
            if (ParseLongNullable(pPr.Attribute("marL")?.Value) is { } paraMarL) para.MarginLeftEmu = paraMarL;
            if (ParseLongNullable(pPr.Attribute("indent")?.Value) is { } paraInd) para.IndentEmu = paraInd;
            if (pPr.Element(A + "buClrTx") is not null)
            {
                para.BulletColorFollowsText = true;
                para.BulletColor = null;
            }
            else if (pPr.Element(A + "buClr") is { } buClr)
            {
                para.BulletColor = PptxColorReader.TryReadColor(buClr, scheme);
            }

            if (pPr.Element(A + "buSzTx") is not null)
            {
                para.BulletSizeFollowsText = true;
                para.BulletSizePt = null;
                para.BulletSizePct = null;
            }
            else if (pPr.Element(A + "buSzPts") is { } buSzPts &&
                     int.TryParse(buSzPts.Attribute("val")?.Value, out var szPts) &&
                     szPts > 0)
            {
                para.BulletSizePt = szPts / 100.0;
                para.BulletSizePct = null;
            }
            else if (pPr.Element(A + "buSzPct") is { } buSzPct &&
                     int.TryParse(buSzPct.Attribute("val")?.Value, out var szPct))
            {
                para.BulletSizePct = szPct;
            }

            if (pPr.Element(A + "buFontTx") is not null)
            {
                para.BulletFontFollowsText = true;
                para.BulletFontFamily = null;
            }
            else if (pPr.Element(A + "buFont") is { } buFont)
            {
                para.BulletFontFamily = buFont.Attribute("typeface")?.Value;
            }

            var spcBef = pPr.Element(A + "spcBef")?.Element(A + "spcPts")?.Attribute("val")?.Value;
            if (!string.IsNullOrWhiteSpace(spcBef) && int.TryParse(spcBef, out var sb))
                para.SpaceBeforePt = sb / 100.0;

            var spcAft = pPr.Element(A + "spcAft")?.Element(A + "spcPts")?.Attribute("val")?.Value;
            if (!string.IsNullOrWhiteSpace(spcAft) && int.TryParse(spcAft, out var sa))
                para.SpaceAfterPt = sa / 100.0;

            // Wave 18B: tab stop list (a:tabLst)
            var tabLst = pPr.Element(A + "tabLst");
            if (tabLst is not null)
            {
                foreach (var tabEl in tabLst.Elements(A + "tab"))
                {
                    if (!long.TryParse(tabEl.Attribute("pos")?.Value, out var tabPos)) continue;
                    var tabAlgn = tabEl.Attribute("algn")?.Value switch
                    {
                        "ctr"  => TabStopAlignment.Center,
                        "r"    => TabStopAlignment.Right,
                        "dec"  => TabStopAlignment.Decimal,
                        _      => TabStopAlignment.Left
                    };
                    para.TabStops.Add(new TabStop { PositionEmu = tabPos, Alignment = tabAlgn });
                }
            }
        }

        foreach (var child in pEl.Elements())
            AppendParagraphContent(
                para,
                child,
                scheme,
                slideRels,
                allSlides,
                slideDir,
                slidePartPathToId);

        return para;
    }

    private static void AppendParagraphContent(
        Paragraph para,
        XElement child,
        PresentationColorScheme scheme,
        IReadOnlyList<OpcRelationshipTarget>? slideRels,
        List<Slide>? allSlides,
        string? slideDir,
        IReadOnlyDictionary<string, string>? slidePartPathToId)
    {
        if (child.Name == A + "r")
        {
            para.Runs.Add(ReadRun(child, scheme, slideRels, allSlides, slideDir, slidePartPathToId));
            return;
        }

        if (child.Name == A + "br")
        {
            para.Runs.Add(new Run { Text = "\n" });
            return;
        }

        if (child.Name == A + "fld")
        {
            para.Runs.Add(ReadFieldRun(child, scheme));
            return;
        }

        if (child.Name == A14 + "m")
        {
            para.Runs.Add(ReadMathRun(child, isAlternateContent: false));
            return;
        }

        if (child.Name != MC + "AlternateContent")
            return;

        // Theme 21: OMML math — mc:AlternateContent wraps the full
        // m:oMathPara form with a plain-text mc:Fallback. Keep math as one
        // structured run; ordinary alternate content is handled below.
        var hasMath = child.Descendants(M + "oMath").Any()
                   || child.Descendants(M + "oMathPara").Any()
                   || child.Descendants(A14 + "m").Any();
        if (hasMath)
        {
            para.Runs.Add(ReadMathRun(child, isAlternateContent: true));
            return;
        }

        // PowerPoint uses AlternateContent for extension text as well as
        // math. If the Choice does not contain a paragraph construct that
        // FreeP understands, consume the visible Fallback instead of
        // silently dropping the paragraph content.
        var branch = SelectParagraphAlternateBranch(child);
        if (branch is null)
            return;

        foreach (var branchChild in branch.Elements())
            AppendParagraphContent(
                para,
                branchChild,
                scheme,
                slideRels,
                allSlides,
                slideDir,
                slidePartPathToId);
    }

    private static XElement? SelectParagraphAlternateBranch(XElement alternateContent)
    {
        var choice = alternateContent.Element(MC + "Choice");
        if (choice is not null && ContainsSupportedParagraphContent(choice))
            return choice;

        return alternateContent.Element(MC + "Fallback");
    }

    private static bool ContainsSupportedParagraphContent(XElement branch) =>
        branch.DescendantsAndSelf().Any(element =>
            element.Name == A + "r" ||
            element.Name == A + "br" ||
            element.Name == A + "fld" ||
            element.Name == A14 + "m" ||
            element.Name == MC + "AlternateContent");

    private static ImagePart? ReadBulletImage(
        XElement buBlip,
        IReadOnlyList<OpcRelationshipTarget>? slideRels,
        ZipArchive? archive,
        string? partPath)
    {
        var embedId = buBlip.Element(A + "blip")?.Attribute(R + "embed")?.Value
            ?? buBlip.Descendants(A + "blip").FirstOrDefault()?.Attribute(R + "embed")?.Value;
        if (string.IsNullOrWhiteSpace(embedId) || slideRels is null || archive is null || partPath is null)
            return null;

        var imageTarget = slideRels.FirstOrDefault(r => r.Id == embedId && r.Type == ImageRelType).Target;
        if (string.IsNullOrWhiteSpace(imageTarget))
            return null;

        var imagePath = ResolveRelativeZipPath(GetDirectoryName(partPath), imageTarget);
        var bytes = ReadEntryBytes(archive, imagePath);
        return bytes is null
            ? null
            : new ImagePart
            {
                Bytes = bytes,
                ContentType = OpcMediaTypes.GetDrawingMediaContentType(imagePath)
            };
    }

    // ── OMML math run parsing (Theme 21) ─────────────────────────────────────────

    /// <summary>
    /// Reads an OMML math element into a Run with MathRunInfo.
    /// The run's Text is the flattened m:t plain text (used as the render fallback).
    /// RawXml is the verbatim serialization of the element (re-emitted on write).
    /// </summary>
    private static Run ReadMathRun(XElement mathEl, bool isAlternateContent)
    {
        // Flatten all m:t text nodes for the plain-text fallback used by the compositor.
        var plainTextSb = new System.Text.StringBuilder();
        foreach (var tEl in mathEl.Descendants(M + "t"))
            plainTextSb.Append(tEl.Value);

        // Also capture mc:Fallback plain text when present and m:t gave nothing
        if (plainTextSb.Length == 0 && isAlternateContent)
        {
            var fallbackEl = mathEl.Element(MC + "Fallback");
            if (fallbackEl is not null)
                foreach (var rEl in fallbackEl.Descendants(A + "r"))
                    plainTextSb.Append(rEl.Element(A + "t")?.Value ?? string.Empty);
        }

        // Serialize the element verbatim for round-trip preservation.
        string rawXml;
        using (var sw = new System.IO.StringWriter())
        {
            mathEl.Save(sw, SaveOptions.DisableFormatting);
            rawXml = sw.ToString();
        }

        return new Run
        {
            Text = plainTextSb.ToString(),
            Math = new MathRunInfo
            {
                RawXml              = rawXml,
                IsAlternateContent  = isAlternateContent,
                ContainingProperties = ReadContainingMathProperties(mathEl),
            }
        };
    }

    /// <summary>
    /// Captures the valid PresentationML containing-part form:
    /// <c>a:graphicData/m:mathPr</c>. The element is kept separate from
    /// <see cref="MathRunInfo.RawXml"/> so round-trip output remains byte-faithful
    /// and the parser can apply the required precedence below package defaults
    /// but above the raw equation wrapper.
    /// </summary>
    private static OmmlMathProperties? ReadContainingMathProperties(XElement mathElement)
    {
        foreach (var ancestor in mathElement.Ancestors())
        {
            if (ancestor.Name != A + "graphicData")
                continue;

            return ReadOmmlMathProperties(ancestor.Element(M + "mathPr"));
        }

        return null;
    }

    private static OmmlMathProperties? ReadOmmlMathProperties(XElement? mathProperties)
    {
        if (mathProperties is null)
            return null;

        static string? ReadValue(XElement? element)
        {
            var value = element?.Attribute(M + "val")?.Value
                ?? element?.Attribute("val")?.Value
                ?? element?.Value;
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        return new OmmlMathProperties(
            BinaryBreak: ReadValue(mathProperties.Element(M + "brkBin")),
            BinarySubtraction: ReadValue(mathProperties.Element(M + "brkBinSub")),
            MathFontFamily: ReadValue(mathProperties.Element(M + "mathFont")));
    }

    private static Run ReadFieldRun(XElement fldEl, PresentationColorScheme scheme)
    {
        var fieldType  = fldEl.Attribute("type")?.Value ?? string.Empty;
        var cachedText = fldEl.Element(A + "t")?.Value ?? string.Empty;

        var fld = new FieldRun
        {
            FieldType  = fieldType,
            CachedText = cachedText,
        };

        var rPr = fldEl.Element(A + "rPr");
        if (rPr is not null)
        {
            if (int.TryParse(rPr.Attribute("sz")?.Value, out var sz) && sz > 0)
                fld.FontSizePt = sz / 100.0;
            fld.FontFamily = rPr.Element(A + "latin")?.Attribute("typeface")?.Value;
            fld.Bold   = rPr.Attribute("b")?.Value is "1" or "true";
            fld.Italic = rPr.Attribute("i")?.Value is "1" or "true";
            var solidFill = rPr.Element(A + "solidFill");
            if (solidFill is not null)
            {
                var tac = PptxColorReader.TryReadColor(solidFill, scheme);
                if (tac is not null) fld.Color = tac.Resolved;
            }
        }

        return new Run
        {
            Text  = cachedText,
            Field = fld,
        };
    }

    private static Run ReadRun(XElement rEl, PresentationColorScheme scheme,
        IReadOnlyList<OpcRelationshipTarget>? slideRels = null,
        List<Slide>? allSlides = null,
        string? slideDir = null,
        IReadOnlyDictionary<string, string>? slidePartPathToId = null)
    {
        var run = new Run { Text = rEl.Element(A + "t")?.Value ?? string.Empty };
        var rPr = rEl.Element(A + "rPr");
        if (rPr is not null)
        {
            var bAttr = rPr.Attribute("b");
            if (bAttr is not null) { run.BoldSet = true;   run.Bold   = bAttr.Value is "1" or "true"; }
            var iAttr = rPr.Attribute("i");
            if (iAttr is not null) { run.ItalicSet = true; run.Italic = iAttr.Value is "1" or "true"; }
            run.Underline = rPr.Attribute("u")?.Value is not null and not "none";
            run.Strikethrough = rPr.Attribute("strike")?.Value is "sngStrike" or "dblStrike";
            run.RightToLeft = ParseNullableBoolean(rPr.Attribute("rtl")?.Value);
            run.Caps = rPr.Attribute("cap")?.Value.ToLowerInvariant() switch
            {
                "all" => RunTextCaps.All,
                "small" => RunTextCaps.Small,
                _ => RunTextCaps.None,
            };
            if (int.TryParse(rPr.Attribute("sz")?.Value, out var sz) && sz > 0)
                run.FontSizePt = sz / 100.0;
            if (int.TryParse(rPr.Attribute("baseline")?.Value, out var baseline))
                run.BaselineOffset = baseline;
            run.FontFamily = rPr.Element(A + "latin")?.Attribute("typeface")?.Value;

            // Simple solid run color
            var solidFill = rPr.Element(A + "solidFill");
            if (solidFill is not null)
                run.Color = PptxColorReader.TryReadColor(solidFill, scheme);

            // Wave 16A: gradient text fill — stored as TextFill when present
            var gradFill = rPr.Element(A + "gradFill");
            if (gradFill is not null)
            {
                var grad = PptxColorReader.TryReadGradFill(gradFill, scheme);
                if (grad is not null) run.TextFill = grad;
            }

            // Wave 16A: text outline (a:ln inside a:rPr)
            var lnEl = rPr.Element(A + "ln");
            if (lnEl is not null)
                run.TextOutline = PptxColorReader.TryReadOutline(lnEl, scheme);

            var runEffectLst = rPr.Element(A + "effectLst");

            // Wave 16A: text shadow (a:effectLst/a:outerShdw inside a:rPr)
            var outerShdw = runEffectLst?.Element(A + "outerShdw");
            if (outerShdw is not null)
            {
                var shdwColor = PptxColorReader.TryReadColor(outerShdw, scheme);
                // DrawingML: absent a:alpha on the color element = fully opaque (255).
                // Default was previously 128 (50%), which made opaque shadows half-transparent on read.
                byte alpha = 255;
                // a:outerShdw may have alpha on its color element
                var schemeClrEl = outerShdw.Element(A + "schemeClr") ?? outerShdw.Element(A + "srgbClr");
                if (schemeClrEl is not null)
                {
                    var alphaEl = schemeClrEl.Element(A + "alpha");
                    if (alphaEl is not null &&
                        long.TryParse(alphaEl.Attribute("val")?.Value,
                            System.Globalization.NumberStyles.Integer,
                            System.Globalization.CultureInfo.InvariantCulture, out var av))
                        alpha = (byte)Math.Clamp((int)Math.Round(av / 100000.0 * 255), 0, 255);
                }
                double blurPt = 2.0, distPt = 2.0, dirDeg = 45.0;
                if (long.TryParse(outerShdw.Attribute("blurRad")?.Value, out var blurEmu)) blurPt = DrawingMlUnits.EmuToPoints(blurEmu);
                if (long.TryParse(outerShdw.Attribute("dist")?.Value,    out var distEmu)) distPt = DrawingMlUnits.EmuToPoints(distEmu);
                if (long.TryParse(outerShdw.Attribute("dir")?.Value,     out var dirRaw))  dirDeg = dirRaw  / 60000.0;
                run.TextShadow = new RunTextShadow
                {
                    Color  = shdwColor ?? new ThemeAwareColor(new SrgbColor(0, 0, 0)),
                    Alpha  = alpha,
                    BlurPt = blurPt,
                    DistPt = distPt,
                    DirDeg = dirDeg,
                };
            }

            // WordArt text reflection (a:effectLst/a:reflection inside a:rPr).
            var reflection = runEffectLst?.Element(A + "reflection");
            if (reflection is not null)
            {
                byte alpha = 128;
                if (long.TryParse(
                        reflection.Attribute("stA")?.Value,
                        System.Globalization.NumberStyles.Integer,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out var stA))
                    alpha = (byte)Math.Clamp((int)Math.Round(stA / 100000.0 * 255), 0, 255);

                double blurPt = 0, distPt = 0, dirDeg = 90.0, scaleY = -1.0, endPos = 1.0;
                if (long.TryParse(reflection.Attribute("blurRad")?.Value, out var blurEmu)) blurPt = DrawingMlUnits.EmuToPoints(blurEmu);
                if (long.TryParse(reflection.Attribute("dist")?.Value, out var distEmu)) distPt = DrawingMlUnits.EmuToPoints(distEmu);
                if (long.TryParse(reflection.Attribute("dir")?.Value, out var dirRaw)) dirDeg = dirRaw / 60000.0;
                if (long.TryParse(reflection.Attribute("sy")?.Value, out var syRaw) && syRaw != 0) scaleY = syRaw / 100000.0;
                if (long.TryParse(reflection.Attribute("endPos")?.Value, out var endPosRaw)) endPos = Math.Clamp(endPosRaw / 100000.0, 0.0, 1.0);

                run.TextReflection = new RunTextReflection
                {
                    Alpha = alpha,
                    BlurPt = blurPt,
                    DistPt = distPt,
                    DirDeg = dirDeg,
                    ScaleY = scaleY,
                    EndPos = endPos,
                };
            }

            // WordArt text glow (a:effectLst/a:glow inside a:rPr).
            var glow = runEffectLst?.Element(A + "glow");
            if (glow is not null)
            {
                double radiusPt = 0;
                if (long.TryParse(glow.Attribute("rad")?.Value, out var radEmu))
                    radiusPt = DrawingMlUnits.EmuToPoints(radEmu);

                byte alpha = 0xA0;
                var glowColorEl = glow.Elements().FirstOrDefault();
                if (glowColorEl is not null)
                    alpha = ReadAlphaFromColorEl(glowColorEl);

                run.TextGlow = new RunTextGlow
                {
                    Color = PptxColorReader.TryReadColor(glow, scheme)
                        ?? new ThemeAwareColor(new SrgbColor(0, 0, 0)),
                    Alpha = alpha,
                    RadiusPt = radiusPt,
                };
            }

            // WordArt text soft edge (a:effectLst/a:softEdge inside a:rPr).
            var softEdge = runEffectLst?.Element(A + "softEdge");
            if (softEdge is not null)
            {
                double radiusPt = 0;
                if (long.TryParse(softEdge.Attribute("rad")?.Value, out var radEmu))
                    radiusPt = DrawingMlUnits.EmuToPoints(radEmu);

                run.TextSoftEdge = new RunTextSoftEdge
                {
                    RadiusPt = radiusPt,
                };
            }

            // Run-level hyperlink: a:hlinkClick inside a:rPr.
            var runHlink = rPr.Element(A + "hlinkClick");
            if (runHlink is not null)
                run.Hyperlink = ResolveHlinkClick(runHlink, slideRels, allSlides, slideDir, slidePartPathToId);
        }
        return run;
    }

    // ── p:transition ─────────────────────────────────────────────────────────────

    /// <summary>
    /// AC1: Resolve the transition element from a slide root, handling both:
    /// (a) mc:AlternateContent wrapper (written by FreeP and real PowerPoint for p14:dur precision)
    /// (b) bare p:transition (legacy files / files without p14 extension)
    /// When an mc:AlternateContent is present, use the mc:Choice p:transition (which carries p14:dur);
    /// if no Choice exists, fall back to mc:Fallback's p:transition.
    /// </summary>
    private static SlideTransition? ResolveTransitionEl(
        XElement? root,
        ZipArchive? archive = null,
        IReadOnlyList<OpcRelationshipTarget>? slideRels = null,
        string? slidePath = null)
    {
        if (root is null) return null;

        // Modern format: mc:AlternateContent > mc:Choice > p:transition (with p14:dur)
        var altContent = root.Element(MC + "AlternateContent");
        if (altContent is not null)
        {
            var choice = altContent.Element(MC + "Choice");
            var choiceTrans = choice?.Element(P + "transition");
            if (choiceTrans is not null)
                return ReadTransition(choiceTrans, preferP14Dur: true, archive, slideRels, slidePath);

            // Fallback inside mc:AlternateContent
            var fallback = altContent.Element(MC + "Fallback");
            var fallbackTrans = fallback?.Element(P + "transition");
            if (fallbackTrans is not null)
                return ReadTransition(fallbackTrans, preferP14Dur: false, archive, slideRels, slidePath);
        }

        // Legacy: bare p:transition (no mc:AlternateContent wrapper)
        var transEl = root.Element(P + "transition");
        if (transEl is not null)
            return ReadTransition(transEl, preferP14Dur: false, archive, slideRels, slidePath);

        return null;
    }

    private static SlideTransition ReadTransition(
        XElement transEl,
        bool preferP14Dur,
        ZipArchive? archive = null,
        IReadOnlyList<OpcRelationshipTarget>? slideRels = null,
        string? slidePath = null)
    {
        var t = new SlideTransition();

        // Duration: spd gives quantized fallback; p14:dur (namespaced) gives precise ms.
        // AC1: when preferP14Dur=true (reading from mc:Choice) try p14:dur first.
        var spd = transEl.Attribute("spd")?.Value;
        if (!string.IsNullOrEmpty(spd))
            t.DurationMs = PptxAnimationMap.SpdToDuration(spd);

        if (preferP14Dur)
        {
            // p14:dur is the namespaced attribute on p:transition inside mc:Choice
            if (int.TryParse(transEl.Attribute(P14 + "dur")?.Value, out var p14Dur) && p14Dur > 0)
                t.DurationMs = p14Dur;
        }
        // (No bare "dur" fallback — bare dur on p:transition is invalid and was the bug we're fixing.)

        // advClick
        t.AdvanceOnClick = transEl.Attribute("advClick")?.Value != "0";

        // advTm (auto-advance)
        if (int.TryParse(transEl.Attribute("advTm")?.Value, out var advTm) && advTm > 0)
            t.AdvanceAfterMs = advTm;

        // EB2: Find the effect child element across P, P14, and P159 namespaces.
        // Real-PowerPoint extended transitions (p14:cube / p14:glitter / p159:morph) live inside
        // mc:Choice as direct p:transition children (we're already inside the Choice p:transition
        // when ReadTransition is called from ResolveTransitionEl). We search all non-sndAc/extLst
        // children regardless of namespace so that p14:/p159: effects are not silently dropped.
        // "sndAc" and "extLst" are the only legitimate non-effect P-namespace children.
        var effectEl = transEl.Elements()
            .FirstOrDefault(e => (e.Name.Namespace == P ||
                                  e.Name.Namespace == P14 ||
                                  e.Name.Namespace == P15 ||
                                  e.Name.Namespace == P159)
                                 && e.Name.LocalName != "sndAc"
                                 && e.Name.LocalName != "extLst");

        if (effectEl is not null)
        {
            t.Kind = PptxAnimationMap.ElementNameToTransitionKind(effectEl.Name.LocalName);

            // Split carries two independent modifiers: orient=horz|vert and
            // dir=in|out. Preserve both; other transitions use dir (or the
            // legacy orient fallback) as their single direction.
            var dirValue = effectEl.Attribute("dir")?.Value;
            var orientValue = effectEl.Attribute("orient")?.Value;
            if (t.Kind == TransitionKind.Split)
            {
                t.SplitOrientation = PptxAnimationMap.AttrToTransitionDirection(orientValue);
                t.Direction = PptxAnimationMap.AttrToTransitionDirection(dirValue)
                    ?? PptxAnimationMap.AttrToTransitionDirection(orientValue);
            }
            else
            {
                t.Direction = PptxAnimationMap.AttrToTransitionDirection(dirValue ?? orientValue);
            }

            // Morph option (p159:morph carries option="byWord"/"byChar"/"byObject")
            if (t.Kind == TransitionKind.Morph)
                t.MorphOption = effectEl.Attribute("option")?.Value;

            if (t.Kind is TransitionKind.Wheel or TransitionKind.WheelReverse)
                t.WheelSpokeCount = ReadPositiveInt(effectEl.Attribute("spokes")?.Value);

            // For unrecognized (Other) transitions, capture the entire p:transition element verbatim
            // so the writer can re-emit it byte-faithfully — ensuring NO transition is silently dropped.
            // EB2: also captures transitions with p14:/p159: effect children that fall through to Other.
            if (t.Kind == TransitionKind.Other)
                t.RawXml = transEl.ToString(SaveOptions.DisableFormatting);
        }

        // p:sndAc / p:stSnd — transition sound
        var sndAcEl = transEl.Element(P + "sndAc");
        if (sndAcEl is not null)
            t.Sound = ReadTransitionSound(sndAcEl, archive, slideRels, slidePath);

        return t;
    }

    /// <summary>
    /// Parses a <c>p:sndAc</c> element and resolves the referenced audio part bytes.
    /// </summary>
    private static TransitionSound? ReadTransitionSound(
        XElement sndAcEl,
        ZipArchive? archive,
        IReadOnlyList<OpcRelationshipTarget>? slideRels,
        string? slidePath)
    {
        // p:sndAc > p:stSnd > p:snd  (snd has r:embed or r:link)
        var stSnd = sndAcEl.Element(P + "stSnd");
        if (stSnd is null) return null;

        var sndEl = stSnd.Element(P + "snd");
        if (sndEl is null) return null;

        var sound = new TransitionSound();
        sound.Loop      = stSnd.Attribute("loop")?.Value == "1";
        sound.RelId     = sndEl.Attribute(R + "embed")?.Value
                       ?? sndEl.Attribute(R + "link")?.Value;

        // Try to resolve the audio part from the slide's relationships.
        if (sound.RelId is not null && slideRels is not null && archive is not null && slidePath is not null)
        {
            var slideDir = GetDirectoryName(slidePath);
            var audioTarget = slideRels.FirstOrDefault(r => r.Id == sound.RelId).Target;
            if (!string.IsNullOrEmpty(audioTarget))
            {
                var audioPath = ResolveRelativeZipPath(slideDir, audioTarget);
                sound.PartPath = audioPath;

                // Try to load the audio bytes.
                try
                {
                    var entry = archive.GetEntry(audioPath) ?? archive.GetEntry(audioPath.TrimStart('/'));
                    if (entry is not null)
                    {
                        using var audioStream = entry.Open();
                        using var ms = new MemoryStream();
                        audioStream.CopyTo(ms);
                        sound.AudioBytes = ms.ToArray();
                    }
                }
                catch
                {
                    // Audio part missing or unreadable — still preserve relId for re-emit.
                }
            }
        }

        return sound;
    }

    // ── p:timing (main sequence + trigger sequences) ─────────────────────────────

    private static void ReadAnimations(XElement timingEl, Slide slide)
    {
        // Walk: p:timing > p:tnLst > p:par (interactive) > p:cTn > p:childTnLst > p:seq (main seq)
        // > p:cTn > p:childTnLst > p:par (build step) > p:cTn > p:childTnLst > p:par > p:cTn
        // > ... > p:set | p:animEffect | p:animMotion (target shape).
        //
        // Additionally: trigger sequences live as sibling p:seq elements whose p:cTn/p:stCondLst/p:cond
        // has evt="onClick" and tgtEl/p:spTgt pointing to the trigger shape.

        try
        {
            var tnLst = timingEl.Element(P + "tnLst");
            if (tnLst is null) return;

            // Find the main sequence: p:seq with nodeType="mainSeq"
            var mainSeq = FindSequence(tnLst, "mainSeq");

            if (mainSeq is not null)
            {
                var seqChildTnLst = mainSeq.Element(P + "cTn")?.Element(P + "childTnLst");
                if (seqChildTnLst is not null)
                {
                    foreach (var clickGroup in seqChildTnLst.Elements(P + "par"))
                        ReadClickGroup(clickGroup, slide, triggerShapeId: null);
                }
            }

            // Find all trigger (interactive) sequences: p:seq with stCondLst/cond evt="onClick" tgtEl/spTgt
            foreach (var triggerSeq in FindTriggerSequences(tnLst))
            {
                var trigSpid = GetTriggerShapeId(triggerSeq);
                if (trigSpid is null) continue;

                var seqChild = triggerSeq.Element(P + "cTn")?.Element(P + "childTnLst");
                if (seqChild is null) continue;

                foreach (var clickGroup in seqChild.Elements(P + "par"))
                    ReadClickGroup(clickGroup, slide, triggerShapeId: trigSpid);
            }

            ReadMediaPlaybackStartModes(timingEl, slide);
        }
        catch
        {
            // If we fail to parse the timing tree (complex/unknown structure), skip silently.
        }
    }

    private static void ReadMediaPlaybackStartModes(XElement timingEl, Slide slide)
    {
        foreach (var mediaNode in timingEl.Descendants()
                     .Where(element => element.Name == P + "video" || element.Name == P + "audio")
                     .Select(element => element.Element(P + "cMediaNode"))
                     .OfType<XElement>())
        {
            var shapeIdText = mediaNode.Element(P + "tgtEl")?.Element(P + "spTgt")?.Attribute("spid")?.Value;
            if (!uint.TryParse(shapeIdText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var shapeId))
                continue;

            var shape = FindShapeRecursive(slide.Shapes, shapeId);
            if (shape?.Media is null)
                continue;

            var cTn = mediaNode.Element(P + "cTn");
            var repeatCount = cTn?.Attribute("repeatCount")?.Value;
            shape.Media.Loop = string.Equals(repeatCount, "indefinite", StringComparison.OrdinalIgnoreCase)
                || (int.TryParse(repeatCount, NumberStyles.Integer, CultureInfo.InvariantCulture, out var count)
                    && count > 1);

            var conditions = cTn?.Element(P + "stCondLst")?.Elements(P + "cond")
                ?? Enumerable.Empty<XElement>();
            if (conditions.Any(condition =>
                    condition.Attribute("evt")?.Value == "onBegin" &&
                    (condition.Attribute("delay")?.Value is null or "0")))
            {
                shape.Media.PlaybackStartMode = MediaPlaybackStartMode.Automatically;
            }
            else
            {
                shape.Media.PlaybackStartMode = MediaPlaybackStartMode.InClickSequence;
            }
        }
    }

    private static SlideShape? FindShapeRecursive(IEnumerable<SlideShape> shapes, uint shapeId)
    {
        foreach (var shape in shapes)
        {
            if (shape.Id == shapeId)
                return shape;

            if (shape.Children.Count > 0 && FindShapeRecursive(shape.Children, shapeId) is { } child)
                return child;
        }

        return null;
    }

    private static XElement? FindSequence(XElement tnLst, string nodeType)
    {
        return tnLst.Descendants(P + "seq")
            .FirstOrDefault(s => s.Element(P + "cTn")?.Attribute("nodeType")?.Value == nodeType);
    }

    /// <summary>
    /// Finds all p:seq elements whose stCondLst has a cond with evt="onClick" and a spTgt target.
    /// These are the interactive-trigger sequences.
    /// </summary>
    private static IEnumerable<XElement> FindTriggerSequences(XElement tnLst)
    {
        foreach (var seq in tnLst.Descendants(P + "seq"))
        {
            var nodeType = seq.Element(P + "cTn")?.Attribute("nodeType")?.Value;
            // Skip the main sequence itself.
            if (nodeType == "mainSeq") continue;

            var condLst = seq.Element(P + "cTn")?.Element(P + "stCondLst");
            if (condLst is null) continue;

            foreach (var cond in condLst.Elements(P + "cond"))
            {
                if (cond.Attribute("evt")?.Value == "onClick" &&
                    cond.Descendants(P + "spTgt").Any())
                {
                    yield return seq;
                    break;
                }
            }
        }
    }

    private static uint? GetTriggerShapeId(XElement triggerSeq)
    {
        var condLst = triggerSeq.Element(P + "cTn")?.Element(P + "stCondLst");
        if (condLst is null) return null;
        foreach (var cond in condLst.Elements(P + "cond"))
        {
            if (cond.Attribute("evt")?.Value == "onClick")
            {
                var spTgt = cond.Descendants(P + "spTgt").FirstOrDefault();
                if (spTgt is not null && uint.TryParse(spTgt.Attribute("spid")?.Value, out var spid))
                    return spid;
            }
        }
        return null;
    }

    private static void ReadClickGroup(XElement clickGroup, Slide slide, uint? triggerShapeId)
    {
        var innerTnLst = clickGroup.Element(P + "cTn")?.Element(P + "childTnLst");
        if (innerTnLst is null) return;

        // Determine trigger from the click group's stCondLst
        var trigger = GetTrigger(clickGroup.Element(P + "cTn")?.Element(P + "stCondLst"));

        foreach (var buildItem in innerTnLst.Elements(P + "par"))
        {
            var anim = ReadBuildItem(buildItem, trigger, triggerShapeId);
            if (anim is not null)
                slide.Animations.Add(anim);
        }
    }

    private static AnimationTrigger GetTrigger(XElement? stCondLst)
    {
        if (stCondLst is null) return AnimationTrigger.OnClick;
        var cond = stCondLst.Element(P + "cond");
        var delay = cond?.Attribute("delay")?.Value;
        if (delay == "indefinite") return AnimationTrigger.OnClick;
        if (delay == "0") return AnimationTrigger.WithPrevious;
        return AnimationTrigger.AfterPrevious;
    }

    private static ShapeAnimation? ReadBuildItem(XElement buildPar, AnimationTrigger outerTrigger, uint? triggerShapeId)
    {
        var cTn = buildPar.Element(P + "cTn");
        if (cTn is null) return null;

        // AC2/AC3/AF1: Read duration from the structural level the writer uses — the animCTn
        // which is the p:cTn direct child of childTnLst of the inner p:par (sibling of p:set).
        // This avoids picking a sub-behavior's dur (AC2) and naturally excludes the p:set
        // sentinel cTn dur="1" (AC3) because it is inside p:set, not a bare childTnLst child.
        // Writer structure (BuildBuildItemEl):
        //   p:par > p:cTn[presetClass/ID] > p:childTnLst >
        //     p:par > p:cTn[fill=hold] > p:childTnLst >
        //       animCTn (p:cTn with dur=anim.DurationMs)  ← read from here (FreeP round-trip)
        //       p:set > p:cBhvr > p:cTn (dur="1" sentinel) ← NOT touched
        //
        // AF1: Real PowerPoint emits p:animEffect/p:anim/p:set as children of innerChildTnLst,
        // NOT a bare p:cTn. The actual duration lives on the p:cTn inside p:cBhvr under those
        // elements. When the primary structural nav finds no direct p:cTn child, fall back to a
        // bounded descendant search within innerChildTnLst, excluding any p:cTn that has a p:set
        // ancestor (which would be the sentinel dur="1" on the p:set behavior element).
        int durationMs = 500;
        if (int.TryParse(cTn.Attribute("dur")?.Value, out var d) && d > 0)
        {
            durationMs = d;
        }
        else
        {
            // Navigate structurally to the animCTn level (mirrors BuildBuildItemEl nesting).
            var innerPar = cTn.Element(P + "childTnLst")?.Element(P + "par");
            var innerParCTn = innerPar?.Element(P + "cTn");
            var innerChildTnLst = innerParCTn?.Element(P + "childTnLst");
            if (innerChildTnLst is not null)
            {
                // Primary: animCTn is a direct p:cTn child of innerChildTnLst (FreeP's own form).
                var animCTn = innerChildTnLst.Elements(P + "cTn").FirstOrDefault();
                if (animCTn is not null &&
                    int.TryParse(animCTn.Attribute("dur")?.Value, out var animDur) && animDur >= 1)
                {
                    durationMs = animDur;
                }
                else
                {
                    // AF1 fallback: real PowerPoint nesting — dur is on a p:cTn inside p:cBhvr under
                    // p:animEffect/p:anim/p:set. Search descendants of innerChildTnLst for a p:cTn
                    // that has a dur attribute and is NOT inside a p:set element (sentinel exclusion).
                    var fallbackCTn = innerChildTnLst
                        .Descendants(P + "cTn")
                        .FirstOrDefault(c =>
                            c.Attribute("dur") != null &&
                            !c.Ancestors(P + "set").Any());
                    if (fallbackCTn is not null &&
                        int.TryParse(fallbackCTn.Attribute("dur")?.Value, out var fbDur) && fbDur >= 1)
                        durationMs = fbDur;
                }
            }
        }

        // Delay and inner trigger
        int delayMs = 0;
        var stCondLst = cTn.Element(P + "stCondLst");
        var innerTrigger = outerTrigger;
        if (stCondLst is not null)
        {
            var cond = stCondLst.Element(P + "cond");
            var delay = cond?.Attribute("delay")?.Value;
            if (delay == "indefinite")
                innerTrigger = AnimationTrigger.OnClick;
            else if (delay != null && int.TryParse(delay, out var delayVal))
            {
                delayMs = delayVal;
                innerTrigger = delayVal == 0 ? AnimationTrigger.WithPrevious : AnimationTrigger.AfterPrevious;
            }
        }

        if (TryReadBehaviorDelay(buildPar, out var behaviorDelayMs))
            delayMs = behaviorDelayMs;

        // Check for motion path: look for p:animMotion anywhere in descendants.
        var animMotion = buildPar.Descendants(P + "animMotion").FirstOrDefault();
        if (animMotion is not null)
        {
            var motionRepeat = ReadRepeat(cTn);
            return ReadMotionBuildItem(
                animMotion,
                buildPar,
                durationMs,
                innerTrigger,
                triggerShapeId,
                motionRepeat.Count,
                motionRepeat.Indefinite,
                ReadBoolean(cTn.Attribute("autoRev")?.Value));
        }

        // Preset entrance/emphasis/exit animation.
        var presetClass = cTn.Attribute("presetClass")?.Value;
        var presetIdStr = cTn.Attribute("presetID")?.Value;
        if (string.IsNullOrEmpty(presetClass)) return null;
        if (!int.TryParse(presetIdStr, out var presetId)) return null;

        var presetSubtype = cTn.Attribute("presetSubtype")?.Value;
        var scaleBehavior = ReadScaleBehavior(
            buildPar.Descendants(P + "animScale").FirstOrDefault());

        var repeatInfo = ReadRepeat(cTn);
        var autoReverse = ReadBoolean(cTn.Attribute("autoRev")?.Value);

        var spTgt = FindSpTgt(buildPar);
        if (spTgt is null) return null;
        if (!uint.TryParse(spTgt.Attribute("spid")?.Value, out var shapeId)) return null;

        var (kind, preset) = PptxAnimationMap.OoxmlToAnimationPreset(presetClass, presetId);
        bool knownPreset = PptxAnimationMap.IsKnownOoxmlPreset(presetClass, presetId);
        if (preset == AnimationPreset.Grow)
            preset = AnimationAmountSemantics.ResolvePreset(preset, scaleBehavior);
        var direction = preset is AnimationPreset.Grow or AnimationPreset.Shrink
            ? null
            : PptxAnimationMap.SubtypeToAnimationDirection(presetSubtype, preset);
        var wheelSpokeCount = preset == AnimationPreset.Wheel
            ? ReadWheelSpokeCount(buildPar, cTn)
            : null;
        var authoredEffectSubtype = direction is null
            && preset is not (AnimationPreset.Grow or AnimationPreset.Shrink)
            && !string.IsNullOrWhiteSpace(presetSubtype)
            && !StringComparer.Ordinal.Equals(presetSubtype, "0")
                ? presetSubtype
                : null;

        return new ShapeAnimation
        {
            ShapeId        = shapeId,
            Kind           = kind,
            Preset         = preset,
            Trigger        = innerTrigger,
            DelayMs        = delayMs,
            DurationMs     = durationMs,
            RepeatCount    = repeatInfo.Count,
            RepeatIndefinitely = repeatInfo.Indefinite,
            AutoReverse    = autoReverse,
            Direction      = direction,
            WheelSpokeCount = wheelSpokeCount,
            EffectSubtype  = authoredEffectSubtype,
            ScaleBehavior = scaleBehavior,
            TriggerShapeId = triggerShapeId,
            RawPresetClass = knownPreset ? null : presetClass,
            RawPresetId = knownPreset ? null : presetId,
            RawPresetSubtype = knownPreset ? null : presetSubtype,
        };
    }

    private static AnimationScaleBehavior? ReadScaleBehavior(XElement? animScale)
    {
        if (animScale is null)
            return null;

        var from = animScale.Element(P + "from");
        var to = animScale.Element(P + "to");
        var by = animScale.Element(P + "by");
        return new AnimationScaleBehavior
        {
            FromX = from?.Attribute("x")?.Value,
            FromY = from?.Attribute("y")?.Value,
            ToX = to?.Attribute("x")?.Value,
            ToY = to?.Attribute("y")?.Value,
            ByX = by?.Attribute("x")?.Value,
            ByY = by?.Attribute("y")?.Value,
            ZoomContents = animScale.Attribute("zoomContents") is { } zoom
                ? ReadBoolean(zoom.Value)
                : null,
        };
    }

    private static int? ReadWheelSpokeCount(XElement buildPar, XElement cTn)
    {
        foreach (var animEffect in buildPar.Descendants(P + "animEffect"))
        {
            var fromFilter = ReadWheelSpokeCountFromFilter(animEffect.Attribute("filter")?.Value);
            if (fromFilter is not null)
                return fromFilter;

            var fromAttr = ReadPositiveInt(animEffect.Attribute("spokes")?.Value);
            if (fromAttr is not null)
                return fromAttr;
        }

        var cTnSpokes = ReadPositiveInt(cTn.Attribute("spokes")?.Value);
        if (cTnSpokes is not null)
            return cTnSpokes;

        return buildPar
            .Descendants(P + "wheel")
            .Select(wheel => ReadPositiveInt(wheel.Attribute("spokes")?.Value))
            .FirstOrDefault(spokes => spokes is not null);
    }

    private static (int? Count, bool Indefinite) ReadRepeat(XElement cTn)
    {
        var value = cTn.Attribute("repeatCount")?.Value;
        if (string.Equals(value, "indefinite", StringComparison.OrdinalIgnoreCase))
            return (null, true);

        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var count)
            && count > 1
            ? (count, false)
            : (null, false);
    }

    private static bool ReadBoolean(string? value)
        => value is "1" or "true" or "on";

    private static int? ReadWheelSpokeCountFromFilter(string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
            return null;

        var spokesIndex = filter.IndexOf("spokes", StringComparison.OrdinalIgnoreCase);
        if (spokesIndex < 0)
            return null;

        var equalsIndex = filter.IndexOf('=', spokesIndex);
        if (equalsIndex < 0)
            return null;

        var start = equalsIndex + 1;
        while (start < filter.Length && char.IsWhiteSpace(filter[start]))
            start++;

        var end = start;
        while (end < filter.Length && char.IsDigit(filter[end]))
            end++;

        return end > start
            ? ReadPositiveInt(filter[start..end])
            : null;
    }

    private static int? ReadPositiveInt(string? value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed > 0
            ? parsed
            : null;

    private static ShapeAnimation? ReadMotionBuildItem(
        XElement animMotion, XElement buildPar,
        int durationMs, AnimationTrigger trigger, uint? triggerShapeId,
        int? repeatCount, bool repeatIndefinitely, bool autoReverse)
    {
        // p:animMotion has: path attr (mini-language), origin attr, cBhvr child with spTgt.
        var pathStr = animMotion.Attribute("path")?.Value ?? string.Empty;
        var origin  = animMotion.Attribute("origin")?.Value ?? "parent";
        var ptsTypes = animMotion.Attribute("ptsTypes")?.Value;

        // Target shape from p:cBhvr/p:tgtEl/p:spTgt
        var cBhvr = animMotion.Element(P + "cBhvr");
        var spTgt = cBhvr?.Element(P + "tgtEl")?.Element(P + "spTgt")
                 ?? FindSpTgt(buildPar);
        if (spTgt is null) return null;
        if (!uint.TryParse(spTgt.Attribute("spid")?.Value, out var shapeId)) return null;

        // Duration from animMotion/cBhvr/cTn
        var cTnDur = cBhvr?.Element(P + "cTn")?.Attribute("dur")?.Value;
        if (cTnDur != null && int.TryParse(cTnDur, out var d) && d > 0)
            durationMs = d;

        // Delay: read from the outer buildPar cTn/stCondLst/cond/@delay, mirroring
        // ReadBuildItem. The writer emits the delay there for non-OnClick timing.
        int delayMs = 0;
        var outerStCondLst = buildPar.Element(P + "cTn")?.Element(P + "stCondLst");
        if (outerStCondLst is not null)
        {
            var delayCond = outerStCondLst.Element(P + "cond");
            var delayVal = delayCond?.Attribute("delay")?.Value;
            if (delayVal != null && delayVal != "indefinite" && int.TryParse(delayVal, out var delayParsed))
            {
                delayMs = delayParsed;
                if (trigger != AnimationTrigger.OnClick)
                    trigger = delayParsed == 0 ? AnimationTrigger.WithPrevious : AnimationTrigger.AfterPrevious;
            }
        }

        // FreeP writes the motion build delay on the outer withEffect cTn and
        // leaves the animMotion/cBhvr condition at 0. Keep a non-zero outer
        // delay instead of letting that inner sentinel erase it, while still
        // honoring non-zero behavior delays used by real PowerPoint files.
        if (TryReadBehaviorDelay(animMotion, out var behaviorDelayMs)
            && (behaviorDelayMs != 0 || delayMs == 0))
            delayMs = behaviorDelayMs;

        var motion = ParseMotionPath(pathStr, origin, ptsTypes);

        return new ShapeAnimation
        {
            ShapeId        = shapeId,
            Kind           = AnimationKind.Motion,
            Preset         = AnimationPreset.Appear, // unused for motion
            Trigger        = trigger,
            DelayMs        = delayMs,
            DurationMs     = durationMs,
            RepeatCount    = repeatCount,
            RepeatIndefinitely = repeatIndefinitely,
            AutoReverse    = autoReverse,
            Motion         = motion,
            TriggerShapeId = triggerShapeId,
        };
    }

    private static bool TryReadBehaviorDelay(XElement timingRoot, out int delayMs)
    {
        delayMs = 0;

        var behaviorCTn = timingRoot
            .Descendants(P + "cBhvr")
            .Where(cBhvr => !cBhvr.Ancestors(P + "set").Any())
            .Select(cBhvr => cBhvr.Element(P + "cTn"))
            .FirstOrDefault(cTn => cTn is not null);

        var delay = behaviorCTn?
            .Element(P + "stCondLst")?
            .Element(P + "cond")?
            .Attribute("delay")?
            .Value;

        return delay is not null
            && delay != "indefinite"
            && int.TryParse(delay, NumberStyles.Integer, CultureInfo.InvariantCulture, out delayMs)
            && delayMs >= 0;
    }

    /// <summary>
    /// Parses the OOXML motion-path mini-language into a <see cref="MotionPath"/>.
    /// Grammar: (M x,y | L x,y | C x1,y1 x2,y2 x,y | Z | E)*
    /// Coordinates are fractions of slide size (0..1), origin at shape center.
    /// Handles both spaced ("M 0 0") and packed ("M0 0") PowerPoint output.
    /// </summary>
    private static MotionPath ParseMotionPath(string pathStr, string origin, string? ptsTypes)
    {
        var mp = new MotionPath { Origin = origin, PtsTypes = ptsTypes };
        if (string.IsNullOrWhiteSpace(pathStr)) return mp;

        // Tokenise: split on whitespace + commas, then further split any token that
        // starts with a command letter immediately followed by a digit/sign (packed
        // form like "M0" → ["M", "0"] or "L-0.5" → ["L", "-0.5"]).
        // PowerPoint emits both spaced and packed strings depending on version.
        var rawTokens = pathStr
            .Replace(',', ' ')
            .Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

        var tokenList = new List<string>(rawTokens.Length * 2);
        foreach (var raw in rawTokens)
        {
            // A command letter is one of: M L C Z E (case-insensitive).
            // If the first char is a letter and is followed by more chars, split it off.
            if (raw.Length > 1 && char.IsLetter(raw[0]))
            {
                tokenList.Add(raw[0].ToString());
                var rest = raw.Substring(1);
                if (!string.IsNullOrEmpty(rest))
                    tokenList.Add(rest);
            }
            else
            {
                tokenList.Add(raw);
            }
        }

        var tokens = tokenList;
        int count = tokens.Count;

        int i = 0;
        while (i < count)
        {
            var cmd = tokens[i++];
            switch (cmd.ToUpperInvariant())
            {
                case "M":
                {
                    if (i + 1 >= count) break;
                    double x = ParsePathDouble(tokens[i++]);
                    double y = ParsePathDouble(tokens[i++]);
                    mp.Segments.Add(MotionPathSegment.MoveTo(x, y));
                    break;
                }
                case "L":
                {
                    if (i + 1 >= count) break;
                    double x = ParsePathDouble(tokens[i++]);
                    double y = ParsePathDouble(tokens[i++]);
                    mp.Segments.Add(MotionPathSegment.LineTo(x, y));
                    break;
                }
                case "C":
                {
                    if (i + 5 >= count) break;
                    double x1 = ParsePathDouble(tokens[i++]);
                    double y1 = ParsePathDouble(tokens[i++]);
                    double x2 = ParsePathDouble(tokens[i++]);
                    double y2 = ParsePathDouble(tokens[i++]);
                    double x  = ParsePathDouble(tokens[i++]);
                    double y  = ParsePathDouble(tokens[i++]);
                    mp.Segments.Add(MotionPathSegment.CubicTo(x1, y1, x2, y2, x, y));
                    break;
                }
                case "Z":
                case "E":
                    mp.Segments.Add(MotionPathSegment.Close());
                    break;
                // Silently skip unknown commands.
            }
        }

        return mp;
    }

    private static double ParsePathDouble(string s)
    {
        if (double.TryParse(s.TrimEnd('f'), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var v))
            return v;
        return 0;
    }

    private static XElement? FindSpTgt(XElement root)
    {
        foreach (var el in root.Descendants(P + "spTgt"))
            return el;
        return null;
    }

    // ── Background ───────────────────────────────────────────────────────────────

    private static ShapeFill? ReadBackground(XElement bg, PresentationColorScheme scheme)
    {
        // p:bgPr — explicit fill (solid/gradient/blip/pattern).
        var bgPr = bg.Element(P + "bgPr");
        if (bgPr is not null)
            return PptxColorReader.TryReadFill(bgPr, scheme);

        // Wave 18B: p:bgRef — reference to a theme background-fill style.
        // The idx attribute maps to theme fill-style-list entries (1001+ = background fills).
        // We approximate it: read the color child (schemeClr or srgbClr) and return a solid fill.
        var bgRef = bg.Element(P + "bgRef");
        if (bgRef is not null)
        {
            var tac = PptxColorReader.TryReadColor(bgRef, scheme);
            if (tac is not null)
                return new ShapeFill.Solid(tac);
            // No explicit color — use idx to approximate: idx 1001 = bg1, 1002+ = accent shades.
            if (int.TryParse(bgRef.Attribute("idx")?.Value, out var idx))
            {
                // Approximate: use the theme background color (dk1 for odd indices, lt1 otherwise).
                var approxColor = (idx % 2 == 0)
                    ? scheme[ThemeColorSlot.Lt1]
                    : scheme[ThemeColorSlot.Dk1];
                return new ShapeFill.Solid(new ThemeAwareColor(approxColor));
            }
        }

        return null;
    }

    // ── Placeholder ───────────────────────────────────────────────────────────────

    private static Placeholder ReadPlaceholder(XElement ph)
    {
        var type = (ph.Attribute("type")?.Value ?? "body").ToLowerInvariant() switch
        {
            "title" => PlaceholderType.Title,
            "ctrtitle" => PlaceholderType.CenteredTitle,
            "subtitle" => PlaceholderType.SubTitle,
            "body" => PlaceholderType.Body,
            "dt" => PlaceholderType.DateTime,
            "ftr" => PlaceholderType.Footer,
            "sldnum" => PlaceholderType.SlideNumber,
            "hdr" => PlaceholderType.Header,
            "obj" => PlaceholderType.Object,
            "chart" => PlaceholderType.Chart,
            "tbl" => PlaceholderType.Table,
            "clipart" => PlaceholderType.ClipArt,
            "dgm" => PlaceholderType.Diagram,
            "media" => PlaceholderType.Media,
            "pic" => PlaceholderType.Picture,
            // Notes masters use an explicit sldImg placeholder for the slide thumbnail.
            // Keep it out of the Body lookup used for the notes text region.
            "sldimg" => PlaceholderType.Picture,
            _ => PlaceholderType.Body
        };

        int.TryParse(ph.Attribute("idx")?.Value, out var idx);
        return new Placeholder { Type = type, Idx = idx };
    }

    // ── Layout type ───────────────────────────────────────────────────────────────

    private static SlideLayoutType MapLayoutType(string? typeStr) =>
        typeStr?.ToLowerInvariant() switch
        {
            "title" => SlideLayoutType.Title,
            "obj" or "tx" => SlideLayoutType.TitleContent,
            "titleonly" => SlideLayoutType.TitleOnly,
            "blank" => SlideLayoutType.Blank,
            "twocol" or "twoobj" or "twocontent" => SlideLayoutType.TwoContent,
            "pictx" or "picandcaption" => SlideLayoutType.PictureCaption,
            _ => SlideLayoutType.Custom
        };

    // ── tableStyles.xml reading ───────────────────────────────────────────────────

    /// <summary>
    /// Reads ppt/tableStyles.xml and populates the <paramref name="tableStyles"/> dictionary
    /// keyed by style GUID string (e.g. "{5C22544A-7EE6-4342-B048-85BDC9FD1C3A}").
    /// Only fills/borders/text-color from the regions we care about for rendering are captured.
    /// </summary>
    private static void ReadTableStyles(
        ZipArchive archive, string path, PresentationColorScheme scheme,
        Dictionary<string, TableStyleData> tableStyles)
    {
        var xml = OpcXml.TryLoadXml(archive, path);
        if (xml?.Root is null) return;

        foreach (var styleEl in xml.Root.Elements(A + "tblStyle"))
        {
            var styleId = styleEl.Attribute("styleId")?.Value;
            if (string.IsNullOrWhiteSpace(styleId)) continue;

            var data = new TableStyleData { StyleId = styleId };

            data.WholeTbl = ReadTableStyleEntry(styleEl.Element(A + "wholeTbl"), scheme, styleId, TableStyleRegion.Whole);
            data.FirstRow = ReadTableStyleEntry(styleEl.Element(A + "firstRow"), scheme, styleId, TableStyleRegion.FirstRow);
            data.LastRow  = ReadTableStyleEntry(styleEl.Element(A + "lastRow"),  scheme, styleId, TableStyleRegion.LastRow);
            data.FirstCol = ReadTableStyleEntry(styleEl.Element(A + "firstCol"), scheme, styleId, TableStyleRegion.FirstCol);
            data.LastCol  = ReadTableStyleEntry(styleEl.Element(A + "lastCol"),  scheme, styleId, TableStyleRegion.LastCol);
            data.Band1H   = ReadTableStyleEntry(styleEl.Element(A + "band1H"),   scheme, styleId, TableStyleRegion.Band1H);
            data.Band2H   = ReadTableStyleEntry(styleEl.Element(A + "band2H"),   scheme, styleId, TableStyleRegion.Band2H);
            data.Band1V   = ReadTableStyleEntry(styleEl.Element(A + "band1V"),   scheme, styleId, TableStyleRegion.Band1V);
            data.Band2V   = ReadTableStyleEntry(styleEl.Element(A + "band2V"),   scheme, styleId, TableStyleRegion.Band2V);

            tableStyles[styleId] = data;
        }
    }

    private enum TableStyleRegion
    {
        Whole,
        FirstRow,
        LastRow,
        FirstCol,
        LastCol,
        Band1H,
        Band2H,
        Band1V,
        Band2V
    }

    private static TableStyleEntry? ReadTableStyleEntry(
        XElement? regionEl,
        PresentationColorScheme scheme,
        string styleId,
        TableStyleRegion region)
    {
        if (regionEl is null) return null;

        var tcStyle = regionEl.Element(A + "tcStyle");
        var tcTxStyle = regionEl.Element(A + "tcTxStyle");

        ShapeFill? fill = null;
        ShapeOutline? border = null;
        ThemeAwareColor? textColor = null;

        if (tcStyle is not null)
        {
            // Fill comes from tcStyle/fill or tcStyle/fillRef (theme fill reference).
            var fillEl = tcStyle.Element(A + "fill");
            if (fillEl is not null)
                fill = PptxColorReader.TryReadFill(fillEl, scheme);

            fill = ApplyPowerPointBuiltInTableBandFillCompatibility(fill, scheme, styleId, region);

            // Border: use tcBdr/insideH and insideV for interior, or lnB for bottom etc.
            // Each side element (a:bottom etc.) wraps an a:ln child — pass the ln to TryReadOutline.
            var tcBdr = tcStyle.Element(A + "tcBdr");
            if (tcBdr is not null)
            {
                // Try common border elements: insideH/insideV for interior grid, then outer sides.
                // Each side is structured as: a:bottom/a:ln, a:left/a:ln, etc.
                static XElement? Ln(XElement? side) => side?.Element(
                    XName.Get("ln", "http://schemas.openxmlformats.org/drawingml/2006/main"));

                border = PptxColorReader.TryReadOutline(Ln(tcBdr.Element(A + "insideH")), scheme)
                      ?? PptxColorReader.TryReadOutline(Ln(tcBdr.Element(A + "insideV")), scheme)
                      ?? PptxColorReader.TryReadOutline(Ln(tcBdr.Element(A + "bottom")), scheme)
                      ?? PptxColorReader.TryReadOutline(Ln(tcBdr.Element(A + "left")),   scheme)
                      ?? PptxColorReader.TryReadOutline(Ln(tcBdr.Element(A + "top")),    scheme)
                      ?? PptxColorReader.TryReadOutline(Ln(tcBdr.Element(A + "right")),  scheme);
            }
        }

        if (tcTxStyle is not null)
        {
            // Text color: first try solidFill wrapper, then direct schemeClr/srgbClr child
            // (DrawingML allows direct color child of tcTxStyle without a solidFill wrapper).
            var solidFill = tcTxStyle.Element(A + "solidFill");
            if (solidFill is not null)
                textColor = PptxColorReader.TryReadColor(solidFill, scheme);
            else
                textColor = PptxColorReader.TryReadColor(tcTxStyle, scheme);
        }

        if (fill is null && border is null && textColor is null)
            return null;

        return new TableStyleEntry { Fill = fill, BorderOutline = border, TextColor = textColor };
    }

    private static ShapeFill? ApplyPowerPointBuiltInTableBandFillCompatibility(
        ShapeFill? fill,
        PresentationColorScheme scheme,
        string styleId,
        TableStyleRegion region)
    {
        const string mediumStyle2Accent1 = "{5C22544A-7EE6-4342-B048-85BDC9FD1C3A}";
        if (!styleId.Equals(mediumStyle2Accent1, StringComparison.OrdinalIgnoreCase)
            || region is not (TableStyleRegion.Whole or TableStyleRegion.Band1H or TableStyleRegion.Band1V)
            || fill is not ShapeFill.Solid solid
            || solid.Color.SchemeColor is not { Slot: ThemeColorSlot.Accent1 } accent1)
        {
            return fill;
        }

        // PowerPoint keeps the built-in style's Accent 1 header, but renders its body
        // bands from Dark 2 with half the authored tint. Keep this compatibility rule
        // table-scoped so ordinary DrawingML tint consumers retain their existing meaning.
        var bodyTint = Math.Clamp(accent1.Tint * 0.5, 0.0, 1.0);
        var bodyRef = new SchemeColorRef
        {
            RoleName = "dk2",
            Slot = ThemeColorSlot.Dk2,
            LumMod = accent1.LumMod,
            LumOff = accent1.LumOff,
            Tint = bodyTint,
            Shade = accent1.Shade
        };
        var resolved = ThemeColorTransform.Apply(
            scheme[ThemeColorSlot.Dk2],
            bodyRef.LumMod,
            bodyRef.LumOff,
            bodyRef.Tint,
            bodyRef.Shade);
        return new ShapeFill.Solid(new ThemeAwareColor(resolved, bodyRef, solid.Color.Alpha));
    }

    // ── 18A: picture crop + colour effects ───────────────────────────────────────────

    /// <summary>
    /// Parses <c>a:srcRect</c> (crop) from <paramref name="blipFillEl"/> and colour-effect
    /// child elements of <paramref name="blip"/> into a <see cref="PictureFormat"/>.
    /// Returns null when neither crop nor any effect is present.
    /// </summary>
    private static PictureFormat? ReadPictureFormat(XElement? blipFillEl, XElement? blip)
    {
        var fmt = new PictureFormat();

        // ── Crop — a:blipFill/a:srcRect ──────────────────────────────────────────
        // l/t/r/b are in 1/1000 of a percent (100000 = 100%).  Divide by 100000 to get 0..1.
        var srcRect = blipFillEl?.Element(A + "srcRect");
        if (srcRect is not null)
        {
            fmt.CropLeft   = ParsePercentFraction(srcRect.Attribute("l")?.Value);
            fmt.CropTop    = ParsePercentFraction(srcRect.Attribute("t")?.Value);
            fmt.CropRight  = ParsePercentFraction(srcRect.Attribute("r")?.Value);
            fmt.CropBottom = ParsePercentFraction(srcRect.Attribute("b")?.Value);
        }

        // ── Colour effects — children of a:blip ──────────────────────────────────
        if (blip is not null)
        {
            // a:grayscl (no attributes)
            if (blip.Element(A + "grayscl") is not null)
                fmt.Grayscale = true;

            // a:biLevel thresh="50000"  (1/1000 %)
            var biLevel = blip.Element(A + "biLevel");
            if (biLevel is not null)
                fmt.BiLevelThreshold = ParsePercentFraction(biLevel.Attribute("thresh")?.Value);

            // a:lum bright="-10000" contrast="20000"
            var lum = blip.Element(A + "lum");
            if (lum is not null)
            {
                var bStr = lum.Attribute("bright")?.Value;
                var cStr = lum.Attribute("contrast")?.Value;
                if (bStr is not null)
                    fmt.Brightness = ParseSignedPercentFraction(bStr);
                if (cStr is not null)
                    fmt.Contrast = ParseSignedPercentFraction(cStr);
            }

            // a:alphaModFix amt="75000" (1/1000 %; 100000 = fully opaque = 1.0)
            var alphaFix = blip.Element(A + "alphaModFix");
            if (alphaFix is not null)
                fmt.AlphaModPct = ParsePercentFraction(alphaFix.Attribute("amt")?.Value);
        }

        // Return null when nothing was set so the caller can use null as "no format".
        return (fmt.HasCrop || fmt.HasColorEffect) ? fmt : null;
    }

    /// <summary>Parses an OOXML 1/1000-of-a-percent string to a 0..1 fraction (unsigned).</summary>
    private static double ParsePercentFraction(string? value)
    {
        if (!long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v))
            return 0;
        return v / 100_000.0;
    }

    /// <summary>Parses an OOXML 1/1000-of-a-percent string to a -1..1 fraction (signed, e.g. lum bright=).</summary>
    private static double ParseSignedPercentFraction(string? value)
    {
        if (!long.TryParse(value, NumberStyles.Integer | NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture, out var v))
            return 0;
        return v / 100_000.0;
    }

    // ── Value parsers ─────────────────────────────────────────────────────────────

    /// <summary>Maps OOXML a:buAutoNum type= string to the <see cref="AutoNumType"/> enum.</summary>
    private static AutoNumType ParseAutoNumType(string? typeStr) => typeStr switch
    {
        "arabicPeriod"     => AutoNumType.ArabicPeriod,
        "arabicParenR"     => AutoNumType.ArabicParenR,
        "arabicParenBoth"  => AutoNumType.ArabicParenBoth,
        "romanUcPeriod"    => AutoNumType.RomanUcPeriod,
        "romanLcPeriod"    => AutoNumType.RomanLcPeriod,
        "romanUcParenR"    => AutoNumType.RomanUcParenR,
        "romanLcParenR"    => AutoNumType.RomanLcParenR,
        "alphaUcPeriod"    => AutoNumType.AlphaUcPeriod,
        "alphaLcPeriod"    => AutoNumType.AlphaLcPeriod,
        "alphaUcParenR"    => AutoNumType.AlphaUcParenR,
        "alphaLcParenR"    => AutoNumType.AlphaLcParenR,
        "alphaUcParenBoth" => AutoNumType.AlphaUcParenBoth,
        "alphaLcParenBoth" => AutoNumType.AlphaLcParenBoth,
        _                  => AutoNumType.ArabicPeriod
    };

    private static uint ParseUint(string? value) =>
        uint.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;

    private static long ParseLong(string? value) =>
        long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;

    private static long? ParseLongNullable(string? value) =>
        long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : null;

    private static string ReadAlternativeText(XElement? cNvPr)
        => cNvPr?.Attribute("descr")?.Value ?? string.Empty;

    private static string ReadAlternativeTextTitle(XElement? cNvPr)
        => cNvPr?.Attribute("title")?.Value ?? string.Empty;

    private static bool ReadDecorative(XElement? cNvPr)
    {
        var decorative = cNvPr?
            .Element(A + "extLst")
            ?.Elements(A + "ext")
            .Elements(Adec + "decorative")
            .FirstOrDefault();
        var value = decorative?.Attribute("val")?.Value;
        return decorative is not null && (value is null || ParseBoolean(value));
    }

    private static bool ReadHidden(XElement? cNvPr) =>
        cNvPr is not null && ParseBoolean(cNvPr.Attribute("hidden")?.Value);

    private static bool ParseBoolean(string? value)
        => value is "1"
            || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);

    private static bool? ParseNullableBoolean(string? value) =>
        value is null ? null : ParseBoolean(value);

    private static int? ParseNullableInt(string? value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;

    // Kept as the product-model adapter for non-theme DrawingML color helpers and source contracts.
    private static bool TryParseHex6(string? hex, out SrgbColor color)
    {
        color = default;
        if (!DrawingMlRgbColor.TryParseHexRgb(hex, out var rgb))
            return false;

        color = new SrgbColor(rgb.R, rgb.G, rgb.B);
        return true;
    }

    /// <summary>
    /// Reads the raw bytes of a zip entry. Returns null when the entry does not exist.
    /// </summary>
    private static byte[]? ReadEntryBytes(ZipArchive archive, string path)
    {
        var entry = archive.GetEntry(path);
        if (entry is null) return null;
        try
        {
            using var stream = entry.Open();
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            return ms.ToArray();
        }
        catch { return null; }
    }
}
