using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Xml;
using System.Xml.Linq;
using Free.Shared.Drawing;
using Free.Shared.Opc;
using FreeP.Core.Model;
using static Free.Shared.Opc.OpcPathHelper;

namespace FreeP.Core.IO;

/// <summary>
/// Wave 1C: writes a <see cref="Presentation"/> model to a <c>.pptx</c> OPC package.
/// Produces a minimal-but-valid package that PowerPoint opens without repair.
/// Entry points: <see cref="Write(Presentation, string)"/> / <see cref="Write(Presentation, Stream)"/>.
/// </summary>
public static class PptxPackageWriter
{
    private sealed record ModernCommentAuthorProfile(
        string Name,
        string Initials,
        string Id,
        string UserId,
        string ProviderId,
        bool IsPreserved);

    private sealed record MediaCaptionTrackRelationship(
        string RelationshipId,
        string Language,
        string Label,
        bool IsExternal);

    // ── Namespaces ────────────────────────────────────────────────────────────────
    private static readonly XNamespace P       = "http://schemas.openxmlformats.org/presentationml/2006/main";
    private static readonly XNamespace A       = PptxColorReader.A;
    private static readonly XNamespace R       = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace Adec    = "http://schemas.microsoft.com/office/drawing/2017/decorative";
    private static readonly XNamespace P188    = "http://schemas.microsoft.com/office/powerpoint/2018/8/main";
    private static readonly XNamespace P20Media = "http://schemas.microsoft.com/office/powerpoint/2020/media";
    private static readonly XNamespace FreePRecording = "https://freex.local/freep/recording/2026";
    private static readonly XNamespace FreePText = "https://freex.local/freep/text/2026";
    private const string AutoNumTemplateExtUri = "{2E2E4D2B-4E4E-4A9E-9B3A-7C2BAA5D1B7C}";
    private const string DecorativeExtUri = "{C183D7F6-B498-43B3-948B-1728B52AA6E4}";
    private const string RecordingMediaArtifactsPath = "ppt/media/recordingArtifacts.xml";

    // ── Relationship types ────────────────────────────────────────────────────────
    private const string OfficeDocRelType   = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument";
    private const string CorePropsRelType   = OpcPackageProperties.CorePropertiesRelationshipType;
    private const string SlideRelType       = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/slide";
    private const string SlideMasterRelType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/slideMaster";
    private const string SlideLayoutRelType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/slideLayout";
    private const string ThemeRelType       = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/theme";
    private const string ImageRelType       = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/image";
    private const string PresPropsRelType   = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/presProps";
    private const string ViewPropsRelType   = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/viewProps";
    private const string TableStylesRelType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/tableStyles";
    private const string ChartRelType       = PptxChartWriter.ChartRelType;
    private const string ChartExRelType     = PptxChartWriter.ChartExRelType;
    private const string NotesSlideRelType  = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/notesSlide";
    private const string NotesMasterRelType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/notesMaster";
    private const string HyperlinkRelType   = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/hyperlink";
    private const string CommentsRelType    = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/comments";
    private const string CommentAuthorsRelType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/commentAuthors";
    private const string ModernCommentsRelType = "http://schemas.microsoft.com/office/2018/10/relationships/comments";
    private const string ModernAuthorsRelType = "http://schemas.microsoft.com/office/2018/10/relationships/authors";
    private const string VideoRelType       = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/video";
    private const string AudioRelType       = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/audio";
    private const string CaptionRelType     = "http://schemas.microsoft.com/office/2011/relationships/mediaCaption";

    // OLE relationship types (Theme 21)
    private const string OleObjectRelType =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/oleObject";
    private const string PackageRelType =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/package";

    // SmartArt diagram relationship types (slide rels point to the named sub-parts)
    private const string DiagramDataRelType      = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/diagramData";
    private const string DiagramLayoutRelType    = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/diagramLayout";
    private const string DiagramQuickStyleRelType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/diagramQuickStyle";
    private const string DiagramColorsRelType    = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/diagramColors";
    private const string DiagramDrawingRelType   = "http://schemas.microsoft.com/office/2007/relationships/diagramDrawing";

    private static readonly HashSet<string> RegeneratedRelationshipTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        OfficeDocRelType,
        CorePropsRelType,
        SlideRelType,
        SlideMasterRelType,
        SlideLayoutRelType,
        ThemeRelType,
        ImageRelType,
        PresPropsRelType,
        ViewPropsRelType,
        TableStylesRelType,
        ChartRelType,
        ChartExRelType,
        NotesSlideRelType,
        NotesMasterRelType,
        HyperlinkRelType,
        CommentsRelType,
        CommentAuthorsRelType,
        ModernCommentsRelType,
        ModernAuthorsRelType,
        VideoRelType,
        AudioRelType,
        OleObjectRelType,
        PackageRelType,
        DiagramDataRelType,
        DiagramLayoutRelType,
        DiagramQuickStyleRelType,
        DiagramColorsRelType,
        DiagramDrawingRelType,
    };

    private static readonly string[] WriterOwnedPackagePartPaths =
    [
        "[Content_Types].xml",
        "_rels/.rels",
        OpcPackageProperties.CorePropertiesZipEntry,
        "ppt/presentation.xml",
        "ppt/_rels/presentation.xml.rels",
        "ppt/presProps.xml",
        "ppt/viewProps.xml",
        "ppt/tableStyles.xml",
        "ppt/commentAuthors.xml",
        "ppt/authors/author1.xml",
    ];

    private static readonly string[] WriterOwnedPackagePartPrefixes =
    [
        "ppt/slides/",
        "ppt/slideLayouts/",
        "ppt/slideMasters/",
        "ppt/theme/",
        "ppt/charts/",
        "ppt/media/",
        "ppt/comments/",
        "ppt/authors/",
        "ppt/notesSlides/",
        "ppt/notesMasters/",
        "ppt/embeddings/",
        "ppt/diagrams/",
    ];

    private static readonly OpcPackageRetentionClassifier WriterOwnedPackageClassifier = new(
        WriterOwnedPackagePartPaths,
        WriterOwnedPackagePartPrefixes,
        RegeneratedRelationshipTypes);

    // ── Content types ─────────────────────────────────────────────────────────────
    private const string PresentationCT  = "application/vnd.openxmlformats-officedocument.presentationml.presentation.main+xml";
    private const string MacroEnabledPresentationCT = "application/vnd.ms-powerpoint.presentation.macroEnabled.main+xml";
    private const string TemplateCT       = "application/vnd.openxmlformats-officedocument.presentationml.template.main+xml";
    private const string MacroEnabledTemplateCT = "application/vnd.ms-powerpoint.template.macroEnabled.main+xml";
    private const string SlideShowCT      = "application/vnd.openxmlformats-officedocument.presentationml.slideshow.main+xml";
    private const string MacroEnabledSlideShowCT = "application/vnd.ms-powerpoint.slideshow.macroEnabled.main+xml";
    private const string SlideCT         = "application/vnd.openxmlformats-officedocument.presentationml.slide+xml";
    private const string SlideMasterCT   = "application/vnd.openxmlformats-officedocument.presentationml.slideMaster+xml";
    private const string SlideLayoutCT   = "application/vnd.openxmlformats-officedocument.presentationml.slideLayout+xml";
    private const string ThemeCT         = "application/vnd.openxmlformats-officedocument.theme+xml";
    private const string PresPropsCT     = "application/vnd.openxmlformats-officedocument.presentationml.presProps+xml";
    private const string ViewPropsCT     = "application/vnd.openxmlformats-officedocument.presentationml.viewProps+xml";
    private const string TableStylesCT   = "application/vnd.openxmlformats-officedocument.presentationml.tableStyles+xml";
    private const string RelsCT          = OpcMediaTypes.RelationshipsContentType;
    private const string ChartCT         = PptxChartWriter.ChartCT;
    private const string ChartExCT       = PptxChartWriter.ChartExCT;
    private const string NotesSlideCT    = "application/vnd.openxmlformats-officedocument.presentationml.notesSlide+xml";
    private const string NotesMasterCT   = "application/vnd.openxmlformats-officedocument.presentationml.notesMaster+xml";
    private const string CommentsCT      = "application/vnd.openxmlformats-officedocument.presentationml.comments+xml";
    private const string CommentAuthorsCT = "application/vnd.openxmlformats-officedocument.presentationml.commentAuthors+xml";
    private const string ModernCommentsCT = "application/vnd.ms-powerpoint.comments+xml";
    private const string ModernAuthorsCT = "application/vnd.ms-powerpoint.authors+xml";

    // p14 section extension + mc:AlternateContent
    private static readonly XNamespace P14  = "http://schemas.microsoft.com/office/powerpoint/2010/main";
    private static readonly XNamespace P15  = "http://schemas.microsoft.com/office/powerpoint/2012/main";
    private static readonly XNamespace P159 = "http://schemas.microsoft.com/office/powerpoint/2015/09/main";
    private static readonly XNamespace MC   = "http://schemas.openxmlformats.org/markup-compatibility/2006";
    private const string SectionExtUri = "{521415D9-36F7-43E2-AB2F-B90AF26B5E84}";

    // EB1/EB3: transition kinds that belong in the p14: namespace (not classic p: namespace).
    // Classic p: kinds (ECMA-376 CT_SlideTransition schema): fade, cut, push, wipe, cover, uncover,
    // split, blinds, dissolve, zoom, wheel, randomBar, strips, random.
    // Everything else is a p14:/p159: extension — emitting them as p: causes PowerPoint repair.
    private static readonly HashSet<TransitionKind> P14TransitionKinds = new()
    {
        TransitionKind.Flash, TransitionKind.Reveal,
        TransitionKind.Cube, TransitionKind.Box, TransitionKind.Rotate, TransitionKind.Flip,
        TransitionKind.Gallery, TransitionKind.Conveyor, TransitionKind.Ferris,
        TransitionKind.Flythrough, TransitionKind.Switch, TransitionKind.Orbit,
        TransitionKind.Doors, TransitionKind.Window, TransitionKind.Pan,
        TransitionKind.Honeycomb, TransitionKind.Comb, TransitionKind.Glitter,
        TransitionKind.Vortex, TransitionKind.Shred, TransitionKind.Wind,
        TransitionKind.Ripple, TransitionKind.Warp, TransitionKind.Fracture,
        TransitionKind.Crush, TransitionKind.PeelOff, TransitionKind.PageCurlDouble,
        TransitionKind.PageCurlSingle, TransitionKind.Airplane, TransitionKind.Origami,
        TransitionKind.Prism, TransitionKind.Curtains, TransitionKind.Drape,
        TransitionKind.Prestige, TransitionKind.WheelReverse,
    };

    // ── Public API ────────────────────────────────────────────────────────────────

    /// <summary>Writes a <see cref="Presentation"/> to a .pptx file on disk.</summary>
    public static void Write(Presentation presentation, string path)
    {
        using var stream = File.Create(path);
        Write(presentation, stream, ResolvePackageKind(Path.GetExtension(path), presentation.PackageKind));
    }

    /// <summary>Writes a <see cref="Presentation"/> to any writable stream as a .pptx.</summary>
    public static void Write(Presentation presentation, Stream stream)
        => Write(presentation, stream, packageKindOverride: null);

    /// <summary>
    /// Writes a presentation while optionally selecting the native Office package family.
    /// Unknown package parts, including <c>ppt/vbaProject.bin</c>, continue through the
    /// existing preservation snapshot path.
    /// </summary>
    public static void Write(
        Presentation presentation,
        Stream stream,
        PresentationPackageKind? packageKindOverride)
    {
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true);
        WriteArchive(archive, presentation, packageKindOverride);
    }

    // ── Core archive writing ──────────────────────────────────────────────────────

    private static void WriteArchive(
        ZipArchive archive,
        Presentation presentation,
        PresentationPackageKind? packageKindOverride)
    {
        var packageSnapshot = presentation.PackageSnapshot;
        var packageKind = packageKindOverride ?? presentation.PackageKind;

        // Ensure there is at least one master and one layout.
        var masters = presentation.Masters.Count > 0
            ? presentation.Masters
            : new List<SlideMaster> { new SlideMaster { Id = "rId1" } };

        var layouts = presentation.Layouts.Count > 0
            ? presentation.Layouts
            : new List<SlideLayout>
            {
                new SlideLayout { Id = "rId1", Name = "Blank", LayoutType = SlideLayoutType.Blank, MasterId = masters[0].Id }
            };
        bool hasSomeNotes = presentation.Slides.Any(s => s.Notes is not null);

        // Collect media extensions used across all slides (for [Content_Types].xml Defaults).
        var mediaExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var slide in presentation.Slides)
        {
            // Register transition sound audio extension (if any).
            if (slide.Transition?.Sound?.AudioBytes is { Length: > 0 })
            {
                var sndCt  = slide.Transition.Sound.ContentType ?? "audio/mpeg";
                var sndExt = OpcMediaTypes.GetAudioVideoExtension(sndCt);
                mediaExtensions.Add(sndExt);
            }

            foreach (var shape in AllShapes(slide.Shapes))
            {
                if (shape.Kind == SlideShapeKind.SmartArt && shape.SmartArt is { } smartArt)
                {
                    foreach (var part in smartArt.Parts.Values)
                    {
                        if (part.Bytes.Length > 0 && part.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                            mediaExtensions.Add(OpcMediaTypes.GetDrawingMediaExtension(part.ContentType));
                    }
                }

                if ((shape.Kind == SlideShapeKind.Picture || shape.Kind == SlideShapeKind.Media)
                    && shape.Picture?.Bytes is { Length: > 0 })
                {
                    var ct = shape.Picture.ContentType ?? "image/png";
                    mediaExtensions.Add(OpcMediaTypes.GetDrawingMediaExtension(ct));
                }

                // HH1: also register picture-fill images so their extension gets a Default
                if (shape.Fill is ShapeFill.Picture picFill && picFill.ImageBytes.Length > 0)
                {
                    var ct = picFill.ContentType ?? "image/png";
                    mediaExtensions.Add(OpcMediaTypes.GetDrawingMediaExtension(ct));
                }

                // II1: register audio/video file extensions for Media shapes
                if (shape.Kind == SlideShapeKind.Media && shape.Media?.Bytes is { Length: > 0 } && shape.Media.ContentType is not null)
                {
                    var mediaExt = OpcMediaTypes.GetAudioVideoExtension(shape.Media.ContentType);
                    mediaExtensions.Add(mediaExt);
                }

                // Register caption/subtitle tracks that will be materialized as package parts.
                if (shape.Kind == SlideShapeKind.Media && shape.Media is not null)
                {
                    foreach (var track in shape.Media.CaptionTracks)
                    {
                        if (track.IsExternal)
                            continue;

                        if (TryGetCaptionTrackBytes(track, packageSnapshot, out _))
                            mediaExtensions.Add(GetCaptionTrackExtension(track));
                    }
                }

                // EA1: register the preserved-object fallback image extension so it gets a Default entry.
                // Without this, the .png/.jpg etc. part has no content-type → PowerPoint repair.
                if (shape.PreservedObject is not null && shape.Picture is { Bytes.Length: > 0 } prvPic)
                {
                    var prvExt = OpcMediaTypes.GetDrawingMediaExtension(prvPic.ContentType ?? "image/png");
                    mediaExtensions.Add(prvExt);
                }
            }

            foreach (var paragraph in EnumerateSlideParagraphs(slide))
            {
                if (paragraph.BulletKind == BulletKind.Image &&
                    paragraph.BulletImage?.Bytes is { Length: > 0 } bulletBytes)
                {
                    var ct = paragraph.BulletImage.ContentType ?? "image/png";
                    mediaExtensions.Add(OpcMediaTypes.GetDrawingMediaExtension(ct));
                }
            }
        }

        foreach (var artifact in presentation.RecordingMediaArtifacts)
        {
            if (artifact.PayloadBytes is not { Length: > 0 } ||
                !TryNormalizeRecordingMediaPackagePath(artifact.PackagePath, out var packagePath))
            {
                continue;
            }

            var extension = GetPackagePathExtension(packagePath);
            if (!string.IsNullOrWhiteSpace(extension))
                mediaExtensions.Add(extension);
        }

        // FA1 (was EA2): Pre-scan preserved parts to determine which paths will be reindexed by
        // WriteSlidePreservedObjects, so BuildContentTypesXml can emit Overrides at the
        // WRITTEN (possibly reindexed) path rather than the original path.
        // This must run BEFORE BuildContentTypesXml since CT is written first.
        //
        // CRITICAL: this pre-scan MUST mirror WriteSlidePreservedObjects' reindex logic
        // byte-for-byte, or the Override lands on a path the writer never actually produced
        // (FA1 bug: a single global HashSet/counter + a global "already remapped" guard
        // predicted different paths than the writer's PER-SLIDE writtenPaths/partCounter reset,
        // whenever the same OPC part path was referenced from more than 2 occurrences, or from
        // two shapes on the same slide followed by another slide reusing the path).
        //
        // The remap is keyed by (slideIdx, shapeId, origPath) — NOT origPath alone — because the
        // same origPath can legitimately be reindexed to a DIFFERENT written path on each
        // occurrence (each slide resets its own writtenPaths/partCounter), so a plain
        // origPath -> path dictionary cannot represent that. BuildContentTypesXml below walks
        // slides/shapes in the same order and looks up this same (slideIdx, shapeId, origPath) key.
        var prvPartCtRemaps = new Dictionary<(int slideIdx, uint shapeId, string origPath), string>();
        {
            for (int preSlideIdx = 1; preSlideIdx <= presentation.Slides.Count; preSlideIdx++)
            {
                var slide = presentation.Slides[preSlideIdx - 1];

                // Mirrors WriteSlidePreservedObjects: fresh writtenPaths + partCounter PER SLIDE.
                var preWrittenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                int prePartCounter  = 1;

                foreach (var shape in AllShapes(slide.Shapes))
                {
                    if (shape.PreservedObject is not { } prvInfo) continue;

                    // Mirrors WriteSlidePreservedObjects: fresh pathRemap PER SHAPE.
                    var prePathRemap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var kv in prvInfo.Parts)
                    {
                        var origPath = kv.Key;
                        if (prePathRemap.ContainsKey(origPath)) continue;

                        var ext       = origPath.Contains('.') ? origPath[(origPath.LastIndexOf('.') + 1)..] : "bin";
                        var freshPath = origPath;
                        if (preWrittenPaths.Contains(freshPath))
                            freshPath = $"ppt/media/preserved_{preSlideIdx}_{prePartCounter++}.{ext}";

                        if (!preWrittenPaths.Contains(freshPath))
                            preWrittenPaths.Add(freshPath);
                        prePathRemap[origPath] = freshPath;

                        prvPartCtRemaps[(preSlideIdx, shape.Id, origPath)] = freshPath;
                    }
                }
            }
        }

        // --- 1. [Content_Types].xml ---
        var preservedChartWorkbookPaths = FindPreservedChartWorkbookPaths(packageSnapshot, presentation);
        var preservedChartExPaths = FindPreservedChartExSidecarPaths(packageSnapshot, presentation);
        var preservedContentTypeWriterOwnedPaths = new HashSet<string>(
            preservedChartWorkbookPaths.Concat(preservedChartExPaths),
            StringComparer.OrdinalIgnoreCase);
        foreach (var mediaPath in FindPreservedMediaPackagePaths(packageSnapshot, presentation))
        {
            preservedContentTypeWriterOwnedPaths.Add(mediaPath);
        }

        foreach (var captionPath in FindPreservedCaptionPackagePaths(packageSnapshot, presentation))
        {
            preservedContentTypeWriterOwnedPaths.Add(captionPath);
        }

        var ctXml = BuildContentTypesXml(
            presentation,
            masters,
            layouts,
            mediaExtensions,
            prvPartCtRemaps,
            packageSnapshot,
            preservedContentTypeWriterOwnedPaths,
            packageKind);
        WriteEntry(archive, "[Content_Types].xml", ctXml);

        // --- 2. Root rels ---
        var rootRels = new OpcRelationshipDocument();
        rootRels.Add("rId1", OfficeDocRelType, "ppt/presentation.xml");
        rootRels.Add("rId2", CorePropsRelType, OpcPackageProperties.CorePropertiesZipEntry);
        MergePreservedRelationships(rootRels, packageSnapshot, "_rels/.rels", string.Empty);
        WriteEntry(archive, "_rels/.rels", rootRels.ToXDocument());

        // --- 3. Core properties ---
        WriteEntry(
            archive,
            OpcPackageProperties.CorePropertiesZipEntry,
            OpcDocumentProperties.BuildCorePropertiesDocument(
                presentation.Properties,
                includeEmptyStrings: true,
                includeXmlDeclaration: true));

        // --- 4. Theme(s) — one per master (MM4: multi-master theme fix) ---
        // Build the per-master theme map: master index (0-based) → theme to write.
        // A master uses its own SlideMaster.Theme if set; otherwise falls back to presentation.Theme.
        // theme1.xml always exists (first master); theme2.xml etc. are added for additional masters.
        // Single-master decks still produce exactly one theme1.xml — no regression.
        var masterThemes = new Dictionary<int, PresentationTheme>(); // masterIdx → theme
        for (int mi = 0; mi < masters.Count; mi++)
            masterThemes[mi] = masters[mi].Theme ?? presentation.Theme;
        // Write all theme parts before masters (masters rels reference theme paths).
        for (int mi = 0; mi < masters.Count; mi++)
        {
            var themePath = $"ppt/theme/theme{mi + 1}.xml";
            WriteEntry(archive, themePath, BuildThemeXml(masterThemes[mi]));
        }
        if (hasSomeNotes)
            WriteEntry(archive, "ppt/theme/theme2.xml", BuildThemeXml(presentation.Theme));

        // --- 5. presProps, viewProps, tableStyles ---
        WriteEntry(archive, "ppt/presProps.xml", BuildPresPropsXml(packageSnapshot));
        WriteEntry(archive, "ppt/viewProps.xml", BuildViewPropsXml(packageSnapshot));
        WriteEntry(archive, "ppt/tableStyles.xml", BuildTableStylesXml());

        // --- 6. Layouts ---
        // We map each layout to a sequential number; index within the overall layouts list.
        var layoutPaths = new Dictionary<string, string>(); // layout.Id -> "ppt/slideLayouts/slideLayoutN.xml"
        for (int li = 0; li < layouts.Count; li++)
        {
            var layout = layouts[li];
            var layoutPath = $"ppt/slideLayouts/slideLayout{li + 1}.xml";
            layoutPaths[layout.Id] = layoutPath;

            // Find the master for this layout, use its theme for color scheme resolution.
            var masterIdx = masters.FindIndex(m => m.Id == layout.MasterId);
            if (masterIdx < 0) masterIdx = 0;
            var masterPath = $"ppt/slideMasters/slideMaster{masterIdx + 1}.xml";
            var layoutColorScheme = masterThemes[masterIdx].ColorScheme;

            // Layout xml
            WriteEntry(archive, layoutPath, BuildSlideLayoutXml(layout, layoutColorScheme));

            // Layout rels: -> master
            var layoutRels = new OpcRelationshipDocument();
            layoutRels.Add("rId1", SlideMasterRelType, $"../{masterPath.Replace("ppt/", "")}");
            WriteRels(archive, layoutPath, layoutRels, packageSnapshot);
        }

        // --- 7. Masters ---
        var masterPaths = new Dictionary<string, string>(); // master.Id -> path
        for (int mi = 0; mi < masters.Count; mi++)
        {
            var master = masters[mi];
            var masterPath = $"ppt/slideMasters/slideMaster{mi + 1}.xml";
            masterPaths[master.Id] = masterPath;

            var masterLayouts = layouts
                .Where(l => l.MasterId == master.Id || masters.Count == 1)
                .ToList();

            // Master xml
            var layoutRelIds = masterLayouts
                .Select((l, i) => ($"rId{i + 2}", layoutPaths.TryGetValue(l.Id, out var lp) ? lp : $"ppt/slideLayouts/slideLayout{i+1}.xml"))
                .ToList();

            WriteEntry(archive, masterPath, BuildSlideMasterXml(master, masterThemes[mi].ColorScheme, layoutRelIds));

            // Master rels: rId1=theme (points to THIS master's own theme part), rId2..=layouts
            var masterRels = new OpcRelationshipDocument();
            // Each master references its own theme file (theme1.xml, theme2.xml, …).
            var themeRelTarget = $"../theme/theme{mi + 1}.xml";
            masterRels.Add("rId1", ThemeRelType, themeRelTarget);
            for (int li = 0; li < layoutRelIds.Count; li++)
            {
                var (relId, layoutPath) = layoutRelIds[li];
                // Relative path from master dir: ../slideLayouts/slideLayoutN.xml
                var relTarget = $"../slideLayouts/{layoutPath.Split('/').Last()}";
                masterRels.Add(relId, SlideLayoutRelType, relTarget);
            }
            WriteRels(archive, masterPath, masterRels, packageSnapshot);
        }

        // --- 8. Slides ---
        var presRels = new OpcRelationshipDocument();
        var sldIdElements = new List<XElement>();
        uint sldIdCounter = 256;
        var usedSldIds = new HashSet<uint>();

        // Build the GLOBAL author map once before the slide loop so that every per-slide
        // BuildCommentsXml call uses consistent (globally-assigned) author ids.
        // Keys are (author-name, initials); ids are 0-based in first-encounter order across
        // all slides (same order BuildCommentAuthorsXml would produce).
        var globalAuthorMap = BuildGlobalAuthorMap(presentation.Slides);
        var modernAuthorMap = BuildModernAuthorMap(presentation.Slides);

        // Emit a single minimal notesMaster if any slide has notes.
        if (hasSomeNotes)
        {
            if (presentation.NotesMasterXml is { Length: > 0 } notesMasterXml)
                WriteRawEntry(archive, "ppt/notesMasters/notesMaster1.xml", notesMasterXml);
            else
                WriteEntry(archive, "ppt/notesMasters/notesMaster1.xml", BuildNotesMasterXml());

            // PowerPoint gives the notes master its own theme part.
            var nmRels = new OpcRelationshipDocument();
            nmRels.Add("rId1", ThemeRelType, "../theme/theme2.xml");
            if (presentation.NotesMasterRelsXml is { Length: > 0 } notesMasterRelsXml)
                WriteRawEntry(archive, "ppt/notesMasters/_rels/notesMaster1.xml.rels", notesMasterRelsXml);
            else
                WriteRels(archive, "ppt/notesMasters/notesMaster1.xml", nmRels, packageSnapshot);
        }

        int globalChartIndex = 1; // monotonically increasing across all slides
        var writtenMediaPaths = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        var writtenCaptionPaths = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        for (int si = 0; si < presentation.Slides.Count; si++)
        {
            var slide = presentation.Slides[si];
            var slidePath = $"ppt/slides/slide{si + 1}.xml";
            var slideRelId = $"rId{si + 2}";

            // Find layout and its owning master's theme for color resolution.
            var layout = layouts.FirstOrDefault(l => l.Id == slide.LayoutId) ?? layouts[0];
            var layoutPath = layoutPaths.TryGetValue(layout.Id, out var lp2) ? lp2 : layoutPaths.Values.First();
            var slideMasterIdx = masters.FindIndex(m => m.Id == layout.MasterId);
            if (slideMasterIdx < 0) slideMasterIdx = 0;
            var slideColorScheme = masterThemes[slideMasterIdx].ColorScheme;

            // Write media (images) into the archive, get back rel-id maps
            var (mediaRelIds, fillBlipRelIds) = WriteSlideMedia(archive, slide, si + 1);

            // Write media audio/video files for Media shapes
            var mediaFileRelIds = WriteSlideMediaFiles(archive, slide, si + 1, packageSnapshot, writtenMediaPaths);

            // Write charts into the archive, get back rel-id map
            var chartRelIds = WriteSlideCharts(archive, slide, ref globalChartIndex, packageSnapshot);

            // Write SmartArt diagram parts (verbatim bytes + rels).
            // Collect already-used relIds so the SmartArt allocator avoids them.
            var usedRelIds = new HashSet<string>(StringComparer.Ordinal)
            {
                "rId1", // always reserved for slide layout
            };
            foreach (var (_, mediaRelId, _) in mediaRelIds)      usedRelIds.Add(mediaRelId);
            foreach (var (_, mediaFileRelId, _, _) in mediaFileRelIds) usedRelIds.Add(mediaFileRelId);
            foreach (var (_, fillBlipRelId, _) in fillBlipRelIds) usedRelIds.Add(fillBlipRelId);
            foreach (var (_, chartRelId, _, _) in chartRelIds)       usedRelIds.Add(chartRelId);

            var captionTrackRels = WriteSlideMediaCaptionTracks(archive, slide, si + 1, usedRelIds, packageSnapshot, writtenCaptionPaths);
            var captionTracksByShape = captionTrackRels
                .GroupBy(track => track.shapeId)
                .ToDictionary(
                    group => group.Key,
                    group => (IReadOnlyList<MediaCaptionTrackRelationship>)group.Select(track => track.relationship).ToList());

            var (smartArtSlideRels, smartArtRelIdRemap) = WriteSlideSmartArt(archive, slide, usedRelIds);

            // Theme 21: Write OLE embedded object binaries + fallback images.
            foreach (var kv in smartArtRelIdRemap)
                foreach (var (_, saRelId) in kv.Value)
                    usedRelIds.Add(saRelId);
            // Returns: embRels = (shapeId, embRelId, embRelType, embPath)
            //          imgRels = (shapeId, imgRelId, imgPath)
            var (oleEmbRels, oleImgRels) = WriteSlideOleObjects(archive, slide, si + 1, usedRelIds);

            // Wave 25A: preserved modern objects (zoom / ink / 3D / unknown)
            var (prvRels, _) = WriteSlidePreservedObjects(archive, slide, si + 1, usedRelIds);

            var bulletImageRelIds = WriteSlideBulletImages(archive, slide, si + 1, usedRelIds);

            // Combined shapeId→relId map for shape element building (picture shapes + charts + OLE)
            var mediaById = new Dictionary<uint, string>();
            foreach (var (id, relId, _) in mediaRelIds)  mediaById[id] = relId;
            foreach (var (id, relId, _, _) in chartRelIds)  mediaById[id] = relId;
            // Media file rel IDs use synthetic key: shape.Id | 0x80000000
            foreach (var (id, relId, _, _) in mediaFileRelIds)  mediaById[id | 0x80000000u] = relId;
            // OLE embedded rel IDs keyed by shape.Id (used in BuildOleGraphicFrameEl)
            foreach (var (shapeId, embRelId, _, _) in oleEmbRels)  mediaById[shapeId] = embRelId;
            // OLE fallback image rel IDs use synthetic key: shape.Id | 0x40000000
            foreach (var (shapeId, imgRelId, _) in oleImgRels)     mediaById[shapeId | 0x40000000u] = imgRelId;
            // EA4: preserved object rel-id patch map, keyed by the FULL (shapeId, oldRelId) pair
            // (a value-tuple key has correct structural equality — no collision risk, unlike the
            // old PrvHashRelId packed-uint scheme which only kept the low 8 bits of shapeId).
            var prvRelIdByShapeAndOldId = new Dictionary<(uint shapeId, string oldRelId), string>();
            foreach (var (sid, oldRelId, newRelId, _, _) in prvRels)
                prvRelIdByShapeAndOldId[(sid, oldRelId)] = newRelId;

            // Fill-blip relId map (shapeId -> relId) for ShapeFill.Picture fills
            var fillBlipById = new Dictionary<uint, string>();
            foreach (var (id, relId, _) in fillBlipRelIds)  fillBlipById[id] = relId;

            // Collect hyperlinks from this slide and assign rel IDs.
            // hlinkRelIds maps hyperlink key (url or "slide:"+slideId) to the rels r:id.
            var hlinkRelIds = new Dictionary<string, string>(StringComparer.Ordinal);
            var hlinkRelEntries = new List<(string relId, string relType, string target, bool external)>();
            CollectHyperlinkRels(slide, presentation.Slides, si + 1, hlinkRelIds, hlinkRelEntries);

            // Transition sound: write audio part (if present) and get relId for XML wiring.
            string? transSoundRelId = null;
            (string relId, string contentType, string partPath)? transSoundPart = null;
            if (slide.Transition?.Sound?.AudioBytes is { Length: > 0 })
            {
                transSoundPart = WriteTransitionSoundPart(archive, slide.Transition, si + 1, usedRelIds);
                if (transSoundPart.HasValue)
                {
                    transSoundRelId = transSoundPart.Value.relId;
                    usedRelIds.Add(transSoundRelId);
                }
            }

            // Slide xml — use the owning master's theme color scheme for scheme-color pre-resolution.
            WriteEntry(archive, slidePath, BuildSlideXml(slide, slideColorScheme, mediaById, smartArtRelIdRemap, hlinkRelIds, presentation.Slides, fillBlipById, transSoundRelId, prvRelIdByShapeAndOldId, captionTracksByShape, bulletImageRelIds.ToDictionary(item => item.paragraph, item => item.relId)));

            // Slide rels: rId1=layout, images (picture shapes + fill blips), charts, SmartArt, optional notesSlide
            var slideRels = new OpcRelationshipDocument();
            slideRels.Add("rId1", SlideLayoutRelType, $"../slideLayouts/{layoutPath.Split('/').Last()}");
            foreach (var (_, mediaRelId, mediaPath) in mediaRelIds)
                slideRels.Add(mediaRelId, ImageRelType, $"../media/{mediaPath.Split('/').Last()}");
            foreach (var (_, mediaFileRelId, mediaFilePath, isVideo) in mediaFileRelIds)
                slideRels.Add(mediaFileRelId, isVideo ? VideoRelType : AudioRelType, MakeRelativePath(slidePath, mediaFilePath));
            foreach (var (_, relationship, target, isExternal) in captionTrackRels)
                slideRels.Add(relationship.RelationshipId, CaptionRelType, target, isExternal);
            foreach (var (_, fillBlipRelId, fillBlipPath) in fillBlipRelIds)
                slideRels.Add(fillBlipRelId, ImageRelType, $"../media/{fillBlipPath.Split('/').Last()}");
            foreach (var (_, chartRelId, chartPath, chartRelType) in chartRelIds)
                slideRels.Add(chartRelId, chartRelType, $"../charts/{chartPath.Split('/').Last()}");
            // SmartArt diagram part rels (dm/lo/qs/cs each get their own rel entry in slide rels)
            foreach (var (relId, relType, target) in smartArtSlideRels)
                slideRels.Add(relId, relType, target);
            // Theme 21: OLE embedded object + fallback image rels
            foreach (var (_, embRelId, embRelType, embPath) in oleEmbRels)
                slideRels.Add(embRelId, embRelType, $"../embeddings/{embPath.Split('/').Last()}");
            foreach (var (_, imgRelId, imgPath) in oleImgRels)
                slideRels.Add(imgRelId, ImageRelType, $"../media/{imgPath.Split('/').Last()}");
            foreach (var (_, relId, mediaPath) in bulletImageRelIds)
                slideRels.Add(relId, ImageRelType, $"../media/{mediaPath.Split('/').Last()}");
            // Wave 25A: preserved modern object rels (absolute paths in prvRelIdPatch, relative in rels entry)
            foreach (var (_, _, newRelId, relType, targetPath) in prvRels)
                slideRels.Add(newRelId, relType, MakeRelativePath(slidePath, targetPath));
            // Hyperlink rels (external with TargetMode=External; internal slide rels without)
            foreach (var (hlRelId, hlRelType, hlTarget, isExternal) in hlinkRelEntries)
                slideRels.Add(hlRelId, hlRelType, hlTarget, isExternal);
            // Transition sound audio part rel
            if (transSoundPart.HasValue)
                slideRels.Add(transSoundPart.Value.relId, AudioRelType,
                    $"../media/{transSoundPart.Value.partPath.Split('/').Last()}");

            // Write notes slide and add rel when the slide has speaker notes
            if (slide.Notes is not null)
            {
                var notesPath = $"ppt/notesSlides/notesSlide{si + 1}.xml";
                var notesRelId = $"rIdNotes{si + 1}";
                WriteEntry(archive, notesPath, BuildNotesSlideXml(slide.Notes, slidePath));

                // notesSlide rels: -> slide + notesMaster
                var notesRels = new OpcRelationshipDocument();
                notesRels.Add("rId1", SlideRelType,       $"../slides/slide{si + 1}.xml");
                notesRels.Add("rId2", NotesMasterRelType, "../notesMasters/notesMaster1.xml");
                WriteRels(archive, notesPath, notesRels, packageSnapshot);

                slideRels.Add(notesRelId, NotesSlideRelType, $"../notesSlides/notesSlide{si + 1}.xml");
            }

            // Write comments part and add rel when the slide has comments
            if (slide.Comments.Count > 0)
            {
                var cmPath  = $"ppt/comments/comment{si + 1}.xml";
                var cmRelId = $"rIdCm{si + 1}";
                if (ShouldUseModernComments(slide))
                {
                    WriteEntry(archive, cmPath, BuildModernCommentsXml(slide.Comments, si, modernAuthorMap));
                    slideRels.Add(cmRelId, ModernCommentsRelType, $"../comments/comment{si + 1}.xml");
                }
                else
                {
                    WriteEntry(archive, cmPath, BuildCommentsXml(slide.Comments, globalAuthorMap));
                    slideRels.Add(cmRelId, CommentsRelType, $"../comments/comment{si + 1}.xml");
                }
            }

            WriteRels(archive, slidePath, slideRels, packageSnapshot);

            presRels.Add(slideRelId, SlideRelType, $"slides/slide{si + 1}.xml");
            var numericSlideId = slide.NumericId.GetValueOrDefault();
            if (numericSlideId == 0 || !usedSldIds.Add(numericSlideId))
            {
                do { numericSlideId = sldIdCounter++; }
                while (!usedSldIds.Add(numericSlideId));
            }
            else if (numericSlideId >= sldIdCounter)
            {
                sldIdCounter = numericSlideId + 1;
            }
            slide.NumericId = numericSlideId;
            sldIdElements.Add(new XElement(P + "sldId",
                new XAttribute("id", numericSlideId),
                new XAttribute(R + "id", slideRelId)));
        }

        // --- 8b. commentAuthors.xml (if any slides have comments) ---
        bool hasLegacyComments = presentation.Slides.Any(s => s.Comments.Count > 0 && !ShouldUseModernComments(s));
        bool hasModernComments = presentation.Slides.Any(ShouldUseModernComments);
        if (hasLegacyComments)
            WriteEntry(archive, "ppt/commentAuthors.xml", BuildCommentAuthorsXml(presentation.Slides));
        if (hasModernComments)
            WriteEntry(archive, "ppt/authors/author1.xml", BuildModernAuthorsXml(modernAuthorMap));

        // --- 9. Presentation rels ---
        presRels.Add("rId1", PresPropsRelType, "presProps.xml");
        int masterRelIdStart = presentation.Slides.Count + 2;
        for (int mi = 0; mi < masters.Count; mi++)
        {
            var masterRelId = $"rId{masterRelIdStart + mi}";
            presRels.Add(masterRelId, SlideMasterRelType, $"slideMasters/slideMaster{mi + 1}.xml");
        }
        presRels.Add($"rId{masterRelIdStart + masters.Count}", ViewPropsRelType, "viewProps.xml");
        presRels.Add($"rId{masterRelIdStart + masters.Count + 1}", TableStylesRelType, "tableStyles.xml");
        int extraPresRelOffset = 2; // next available offset after tableStyles
        presRels.Add($"rId{masterRelIdStart + masters.Count + extraPresRelOffset}", ThemeRelType, "theme/theme1.xml");
        extraPresRelOffset++;
        string? notesMasterRelId = null;
        if (hasSomeNotes)
        {
            notesMasterRelId = $"rId{masterRelIdStart + masters.Count + extraPresRelOffset}";
            presRels.Add(notesMasterRelId, NotesMasterRelType, "notesMasters/notesMaster1.xml");
            extraPresRelOffset++;
        }
        if (hasLegacyComments)
        {
            presRels.Add($"rId{masterRelIdStart + masters.Count + extraPresRelOffset}", CommentAuthorsRelType, "commentAuthors.xml");
            extraPresRelOffset++;
        }
        if (hasModernComments)
            presRels.Add($"rId{masterRelIdStart + masters.Count + extraPresRelOffset}", ModernAuthorsRelType, "authors/author1.xml");

        WriteRels(archive, "ppt/presentation.xml", presRels, packageSnapshot);

        // --- 10. presentation.xml (last, so sldIdElements are complete) ---
        var masterRelIds = Enumerable.Range(0, masters.Count)
            .Select(i => ($"rId{masterRelIdStart + i}", $"ppt/slideMasters/slideMaster{i+1}.xml"))
            .ToList();

        WriteEntry(archive, "ppt/presentation.xml",
            BuildPresentationXml(presentation, sldIdElements, masterRelIds, notesMasterRelId));

        if (presentation.RecordingMediaArtifacts.Count > 0)
        {
            WriteRecordingMediaArtifactPayloads(archive, presentation);
            WriteEntry(archive, RecordingMediaArtifactsPath, BuildRecordingMediaArtifactsXml(presentation));
        }

        CopyPreservedPackageEntries(
            archive,
            packageSnapshot,
            preservedChartWorkbookPaths.Concat(preservedChartExPaths).ToHashSet(StringComparer.OrdinalIgnoreCase));
    }

    // ── [Content_Types].xml ───────────────────────────────────────────────────────

    private static XDocument BuildRecordingMediaArtifactsXml(Presentation presentation)
    {
        var root = new XElement(
            FreePRecording + "recordingMediaArtifacts",
            new XAttribute(XNamespace.Xmlns + "freepRec", FreePRecording.NamespaceName));

        foreach (var artifact in presentation.RecordingMediaArtifacts)
        {
            var packagePath = TryNormalizeRecordingMediaPackagePath(artifact.PackagePath, out var normalizedPackagePath)
                ? normalizedPackagePath
                : artifact.PackagePath;

            root.Add(new XElement(
                FreePRecording + "artifact",
                new XAttribute("kind", artifact.Kind.ToString()),
                new XAttribute("slideIndex", artifact.SlideIndex),
                new XAttribute("suggestedFileName", artifact.SuggestedFileName),
                new XAttribute("contentType", artifact.ContentType),
                new XAttribute("packagePath", packagePath),
                new XAttribute("contentLengthBytes", artifact.ContentLengthBytes),
                new XAttribute("contentSha256", artifact.ContentSha256),
                new XAttribute("durationMs", artifact.DurationMs),
                new XAttribute("capturedByHost", artifact.CapturedByHost),
                new XAttribute("statusText", artifact.StatusText)));
        }

        return new XDocument(new XDeclaration("1.0", "utf-8", null), root);
    }

    private static void WriteRecordingMediaArtifactPayloads(
        ZipArchive archive,
        Presentation presentation)
    {
        var writtenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var artifact in presentation.RecordingMediaArtifacts)
        {
            if (artifact.PayloadBytes is not { Length: > 0 } ||
                !TryNormalizeRecordingMediaPackagePath(artifact.PackagePath, out var normalizedPath) ||
                !writtenPaths.Add(normalizedPath))
            {
                continue;
            }

            WriteRawEntry(archive, normalizedPath, artifact.PayloadBytes);
        }
    }

    private static string GetPackagePathExtension(string packagePath)
    {
        var normalized = NormalizeZipPath(packagePath);
        var fileName = normalized.Contains('/')
            ? normalized[(normalized.LastIndexOf('/') + 1)..]
            : normalized;
        var dotIndex = fileName.LastIndexOf('.');
        return dotIndex >= 0 && dotIndex < fileName.Length - 1
            ? fileName[(dotIndex + 1)..]
            : string.Empty;
    }

    private static string NormalizeZipPath(string packagePath) =>
        packagePath.Replace('\\', '/').TrimStart('/');

    private static bool TryNormalizeRecordingMediaPackagePath(string packagePath, out string normalizedPath)
    {
        normalizedPath = NormalizeZipPath(packagePath);
        if (string.IsNullOrWhiteSpace(normalizedPath) ||
            !normalizedPath.StartsWith("ppt/media/", StringComparison.OrdinalIgnoreCase) ||
            normalizedPath.EndsWith("/", StringComparison.Ordinal) ||
            string.Equals(normalizedPath, RecordingMediaArtifactsPath, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return normalizedPath.Split(["/"], StringSplitOptions.None).All(segment =>
            !string.IsNullOrWhiteSpace(segment) &&
            segment != "." &&
            segment != "..");
    }

    private static XDocument BuildContentTypesXml(
        Presentation p, List<SlideMaster> masters, List<SlideLayout> layouts,
        HashSet<string> mediaExtensions,
        Dictionary<(int slideIdx, uint shapeId, string origPath), string>? prvPartPathRemaps = null,
        PptxPackageSnapshot? packageSnapshot = null,
        IReadOnlySet<string>? preservedWriterOwnedPaths = null,
        PresentationPackageKind packageKind = PresentationPackageKind.Presentation)
    {
        var CT = OpcMediaTypes.ContentTypesNamespace;

        var defaults = new List<XElement>
        {
            new XElement(CT + "Default", new XAttribute("Extension", "rels"), new XAttribute("ContentType", RelsCT)),
            new XElement(CT + "Default", new XAttribute("Extension", "xml"),  new XAttribute("ContentType", "application/xml")),
        };

        // Emit a Default entry for every media extension actually written (covers all paths correctly).
        foreach (var ext in mediaExtensions.OrderBy(e => e))
        {
            if (TryGetPackageDefaultContentType(ext, out var contentType))
                defaults.Add(new XElement(CT + "Default",
                    new XAttribute("Extension", ext),
                    new XAttribute("ContentType", contentType)));
        }

        var overrides = new List<XElement>
        {
            Override(CT, "/ppt/presentation.xml", GetPresentationContentType(packageKind)),
            Override(CT, "/ppt/presProps.xml", PresPropsCT),
            Override(CT, "/ppt/viewProps.xml", ViewPropsCT),
            Override(CT, "/ppt/tableStyles.xml", TableStylesCT),
            Override(CT, OpcPackageProperties.CorePropertiesPartName, OpcPackageProperties.CorePropertiesContentType),
        };
        // MM4: one theme Override entry per master (theme1.xml, theme2.xml, …).
        for (int mi = 0; mi < masters.Count; mi++)
            overrides.Add(Override(CT, $"/ppt/theme/theme{mi + 1}.xml", ThemeCT));
        if (p.Slides.Any(s => s.Notes is not null))
            overrides.Add(Override(CT, "/ppt/theme/theme2.xml", ThemeCT));

        for (int mi = 0; mi < masters.Count; mi++)
            overrides.Add(Override(CT, $"/ppt/slideMasters/slideMaster{mi + 1}.xml", SlideMasterCT));

        for (int li = 0; li < layouts.Count; li++)
            overrides.Add(Override(CT, $"/ppt/slideLayouts/slideLayout{li + 1}.xml", SlideLayoutCT));

        for (int si = 0; si < p.Slides.Count; si++)
            overrides.Add(Override(CT, $"/ppt/slides/slide{si + 1}.xml", SlideCT));

        // Collect notes-slide content types (only slides with non-null Notes)
        bool hasSomeNotes = false;
        for (int si = 0; si < p.Slides.Count; si++)
        {
            if (p.Slides[si].Notes is not null)
            {
                overrides.Add(Override(CT, $"/ppt/notesSlides/notesSlide{si + 1}.xml", NotesSlideCT));
                hasSomeNotes = true;
            }
        }
        if (hasSomeNotes)
            overrides.Add(Override(CT, "/ppt/notesMasters/notesMaster1.xml", NotesMasterCT));

        // Collect chart content types
        int chartGlobalIdx = 1;
        foreach (var slide in p.Slides)
        {
            foreach (var shape in AllShapes(slide.Shapes))
            {
                if (shape.Kind == SlideShapeKind.Chart && shape.Chart is not null)
                {
                    overrides.Add(Override(
                        CT,
                        "/" + PptxChartWriter.GetWrittenChartPath(shape.Chart, chartGlobalIdx),
                        shape.Chart.IsChartEx ? ChartExCT : ChartCT));
                    if (shape.Chart.RegenerateWorkbookOnSave)
                    {
                        overrides.Add(Override(
                            CT,
                            "/" + PptxChartWriter.GetRegeneratedWorkbookPath(chartGlobalIdx),
                            PptxChartWriter.ChartWorkbookCT));
                    }

                    chartGlobalIdx++;
                }
            }
        }

        // Comments content types
        bool hasLegacyComments = p.Slides.Any(s => s.Comments.Count > 0 && !ShouldUseModernComments(s));
        bool hasModernComments = p.Slides.Any(ShouldUseModernComments);
        if (hasLegacyComments || hasModernComments)
        {
            if (hasLegacyComments)
                overrides.Add(Override(CT, "/ppt/commentAuthors.xml", CommentAuthorsCT));
            if (hasModernComments)
                overrides.Add(Override(CT, "/ppt/authors/author1.xml", ModernAuthorsCT));

            for (int si = 0; si < p.Slides.Count; si++)
            {
                if (p.Slides[si].Comments.Count > 0)
                {
                    var contentType = ShouldUseModernComments(p.Slides[si])
                        ? ModernCommentsCT
                        : CommentsCT;
                    overrides.Add(Override(CT, $"/ppt/comments/comment{si + 1}.xml", contentType));
                }
            }
        }

        // Theme 21: Collect OLE embedded object content types
        // OLE embedded binaries live in ppt/embeddings/ — each needs an Override entry.
        var seenOleParts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int oleEmbIdx = 1;
        foreach (var slide in p.Slides)
        {
            foreach (var shape in AllShapes(slide.Shapes))
            {
                if (shape.Kind == SlideShapeKind.Ole && shape.OleObject is { } oleObj
                    && oleObj.EmbeddedBytes.Length > 0)
                {
                    var partPath = $"/ppt/embeddings/oleObject{oleEmbIdx}.{oleObj.EmbeddedExtension}";
                    if (seenOleParts.Add(shape.Id.ToString()))
                    {
                        overrides.Add(Override(CT, partPath, oleObj.EmbeddedContentType));
                    }
                    oleEmbIdx++;
                }
            }
        }

        // Collect SmartArt diagram part content types (from the stored DiagramPart objects)
        var seenSmartArtParts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var slide in p.Slides)
        {
            foreach (var shape in AllShapes(slide.Shapes))
            {
                if (shape.Kind == SlideShapeKind.SmartArt && shape.SmartArt is { } sa)
                {
                    foreach (var part in sa.Parts.Values)
                    {
                        if (!string.IsNullOrEmpty(part.PartPath) &&
                            !string.IsNullOrEmpty(part.ContentType) &&
                            seenSmartArtParts.Add(part.PartPath))
                        {
                            overrides.Add(Override(CT, "/" + part.PartPath, part.ContentType));
                        }
                    }
                }
            }
        }

        // Wave 25A: Preserved modern object part content types
        // FA1 (was EA2): apply prvPartPathRemaps so Overrides are emitted at the WRITTEN
        // (possibly reindexed) path, not the original path. Without this, a reindexed part has
        // no Override → repair. The remap is keyed by (slideIdx, shapeId, origPath) — the same
        // granularity WriteSlidePreservedObjects reindexes at — because the same origPath can be
        // reindexed to a DIFFERENT written path on each occurrence (each slide independently
        // resets its own written-paths tracking), so a plain origPath -> path map cannot capture
        // this. We must walk slides/shapes here in the SAME order as the pre-scan and the real
        // writer for the (slideIdx, shapeId) keys to align.
        var seenPrvParts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int si = 0; si < p.Slides.Count; si++)
        {
            var slide = p.Slides[si];
            int slideIdxForRemap = si + 1; // matches preSlideIdx / slideIdx (1-based) elsewhere

            foreach (var shape in AllShapes(slide.Shapes))
            {
                if (shape.PreservedObject is { } info)
                {
                    foreach (var kv in info.PartContentTypes)
                    {
                        if (string.IsNullOrEmpty(kv.Key) || string.IsNullOrEmpty(kv.Value))
                            continue;

                        // FA1: resolve the WRITTEN path (may differ from original if reindexed)
                        var writtenPath = (prvPartPathRemaps is not null &&
                                          prvPartPathRemaps.TryGetValue((slideIdxForRemap, shape.Id, kv.Key), out var rp))
                            ? rp : kv.Key;

                        if (seenPrvParts.Add(writtenPath))
                        {
                            overrides.Add(Override(CT,
                                writtenPath.StartsWith('/') ? writtenPath : "/" + writtenPath,
                                kv.Value));
                        }
                    }
                }
            }
        }

        var doc = new XDocument(
            new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(CT + "Types",
                defaults,
                overrides));

        MergePreservedContentTypes(doc, packageSnapshot, preservedWriterOwnedPaths);
        return doc;
    }

    private static XElement Override(XNamespace ct, string partName, string contentType) =>
        new XElement(ct + "Override",
            new XAttribute("PartName", partName),
            new XAttribute("ContentType", contentType));

    private static string GetPresentationContentType(PresentationPackageKind packageKind) =>
        packageKind switch
        {
            PresentationPackageKind.MacroEnabledPresentation => MacroEnabledPresentationCT,
            PresentationPackageKind.Template => TemplateCT,
            PresentationPackageKind.MacroEnabledTemplate => MacroEnabledTemplateCT,
            PresentationPackageKind.SlideShow => SlideShowCT,
            PresentationPackageKind.MacroEnabledSlideShow => MacroEnabledSlideShowCT,
            _ => PresentationCT,
        };

    private static PresentationPackageKind ResolvePackageKind(
        string extension,
        PresentationPackageKind fallback) =>
        extension.ToLowerInvariant() switch
        {
            ".pptm" => PresentationPackageKind.MacroEnabledPresentation,
            ".potx" => PresentationPackageKind.Template,
            ".potm" => PresentationPackageKind.MacroEnabledTemplate,
            ".ppsx" => PresentationPackageKind.SlideShow,
            ".ppsm" => PresentationPackageKind.MacroEnabledSlideShow,
            ".pptx" => PresentationPackageKind.Presentation,
            _ => fallback,
        };

    // ── presentation.xml ─────────────────────────────────────────────────────────

    private static XDocument BuildPresentationXml(
        Presentation p,
        List<XElement> sldIdElements,
        List<(string relId, string masterPath)> masterRelIds,
        string? notesMasterRelId)
    {
        var slideWidthEmu = p.SlideSizeCxEmu > 0
            ? p.SlideSizeCxEmu
            : DrawingMlUnits.EmuPerInch * 40 / 3;
        var slideHeightEmu = p.SlideSizeCyEmu > 0
            ? p.SlideSizeCyEmu
            : DrawingMlUnits.EmuPerInch * 15 / 2;
        var notesPageWidthEmu = p.NotesPageSizeCxEmu > 0
            ? p.NotesPageSizeCxEmu
            : DrawingMlUnits.EmuPerInch * 15 / 2;
        var notesPageHeightEmu = p.NotesPageSizeCyEmu > 0
            ? p.NotesPageSizeCyEmu
            : DrawingMlUnits.EmuPerInch * 10;

        var presEl = new XElement(P + "presentation",
            NsAttr("p", P), NsAttr("a", A), NsAttr("r", R),
            new XAttribute("saveSubsetFonts", "1"),
            new XElement(P + "sldMasterIdLst",
                masterRelIds.Select((mr, i) =>
                    new XElement(P + "sldMasterId",
                        new XAttribute("id", 2147483648u + (uint)i),
                        new XAttribute(R + "id", mr.relId)))),
            notesMasterRelId is not null
                ? new XElement(P + "notesMasterIdLst",
                    new XElement(P + "notesMasterId",
                        new XAttribute(R + "id", notesMasterRelId)))
                : null,
            new XElement(P + "sldIdLst", sldIdElements),
            new XElement(P + "sldSz",
                new XAttribute("cx", slideWidthEmu),
                new XAttribute("cy", slideHeightEmu),
                new XAttribute("type", "screen16x9")),
            new XElement(P + "notesSz",
                new XAttribute("cx", notesPageWidthEmu),
                new XAttribute("cy", notesPageHeightEmu)),
            BuildDefaultTextStyleEl());

        var slideIdToRelId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < p.Slides.Count && i < sldIdElements.Count; i++)
        {
            var relId = sldIdElements[i].Attribute(R + "id")?.Value;
            if (!string.IsNullOrWhiteSpace(relId))
            {
                slideIdToRelId[p.Slides[i].Id] = relId;
            }
        }

        var customShowElements = BuildCustomShowElements(p.CustomShows, slideIdToRelId).ToList();
        if (customShowElements.Count > 0)
        {
            presEl.Add(new XElement(P + "custShowLst", customShowElements));
        }

        // Emit p14:sectionLst inside p:extLst when sections are present.
        if (p.Sections.Count > 0)
        {
            // Build a map from sldId rId → the numeric id counter (mirroring WriteArchive).
            // sldIdElements were built with ids 256, 257, … in WriteArchive order.
            // We re-derive the mapping by matching the r:id attribute of each sldId element.
            var rIdToNumId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            uint counter = 256;
            foreach (var el in sldIdElements)
            {
                var rId = el.Attribute(R + "id")?.Value;
                if (rId is not null)
                    rIdToNumId[rId] = counter.ToString();
                counter++;
            }

            // Build a map from Slide.Id (rId) → numeric sldId
            // sldIdElements[i] has r:id = "rId{i+2}" (because presRels starts at rId2 for slides)
            // Use Slide order index to match.
            // Actually, sldIdElements[i].Attribute("id") already holds the numeric id.
            var slideRIdToNumId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < sldIdElements.Count; i++)
            {
                var rId   = sldIdElements[i].Attribute(R + "id")?.Value;
                var numId = sldIdElements[i].Attribute("id")?.Value;
                if (rId is not null && numId is not null)
                    slideRIdToNumId[rId] = numId;
            }

            // Build a map from Slide.Id (presentation-level rId) → numeric sldId.
            // Slide.Id is set to the rId during ReadSlide; on new slides it is a GUID.
            // We need Slide index → numeric sldId.
            var sldIdByIndex = new List<string>();
            foreach (var el in sldIdElements)
                sldIdByIndex.Add(el.Attribute("id")?.Value ?? string.Empty);

            // Section membership refers to Slide.Id.  Convert Slide.Id → numeric sldId.
            // Slide.Id = rId during read; but for new slides it is a GUID.
            // Use the robust approach: map Slide.Id to numeric sldId via index.
            var slideIdToNumericSldId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < p.Slides.Count && i < sldIdByIndex.Count; i++)
                slideIdToNumericSldId[p.Slides[i].Id] = sldIdByIndex[i];

            var sectionElements = p.Sections.Select(sec =>
            {
                // Translate each section member's Slide.Id to the freshly-assigned
                // write-time numeric sldId via slideIdToNumericSldId.
                // Members that no longer exist in the presentation are skipped so we
                // never emit a dangling p14:sldId reference.
                var translatedSldIds = sec.SlideIds
                    .Where(slideIdToNumericSldId.ContainsKey)
                    .Select(slideId => slideIdToNumericSldId[slideId]);

                var sldIdLstEl = new XElement(P14 + "sldIdLst",
                    translatedSldIds.Select(assignedId => new XElement(P14 + "sldId",
                        new XAttribute("id", assignedId))));

                return (XElement)new XElement(P14 + "section",
                    new XAttribute("name", sec.Name),
                    new XAttribute("id",   sec.Id),
                    sldIdLstEl);
            }).ToList();

            presEl.Add(new XElement(P + "extLst",
                new XElement(P + "ext",
                    new XAttribute("uri", SectionExtUri),
                    new XElement(P14 + "sectionLst",
                        new XAttribute(XNamespace.Xmlns + "p14", P14.NamespaceName),
                        sectionElements))));
        }

        return new XDocument(new XDeclaration("1.0", "UTF-8", "yes"), presEl);
    }

    // ── slide.xml ────────────────────────────────────────────────────────────────

    private static IEnumerable<XElement> BuildCustomShowElements(
        IReadOnlyList<PresentationCustomShow> customShows,
        IReadOnlyDictionary<string, string> slideIdToRelId)
    {
        var usedIds = new HashSet<uint>();
        foreach (var customShow in customShows)
        {
            var translatedSlideRelIds = customShow.SlideIds
                .Where(slideIdToRelId.ContainsKey)
                .Select(slideId => slideIdToRelId[slideId])
                .ToArray();
            if (translatedSlideRelIds.Length == 0)
            {
                continue;
            }

            var customShowId = customShow.Id;
            while (!usedIds.Add(customShowId))
            {
                customShowId++;
            }

            yield return new XElement(P + "custShow",
                new XAttribute("name", customShow.Name),
                new XAttribute("id", customShowId.ToString(CultureInfo.InvariantCulture)),
                new XElement(P + "sldLst",
                    translatedSlideRelIds.Select(relId => new XElement(P + "sld",
                        new XAttribute(R + "id", relId)))));
        }
    }

    private static XDocument BuildSlideXml(
        Slide slide, PresentationColorScheme scheme,
        Dictionary<uint, string> mediaById,
        Dictionary<uint, Dictionary<string, string>> smartArtRelIdRemap,
        Dictionary<string, string>? hlinkRelIds = null,
        List<Slide>? allSlides = null,
        Dictionary<uint, string>? fillBlipById = null,
        string? transSoundRelId = null,
        Dictionary<(uint shapeId, string oldRelId), string>? prvRelIdByShapeAndOldId = null,
        IReadOnlyDictionary<uint, IReadOnlyList<MediaCaptionTrackRelationship>>? captionTracksByShape = null,
        Dictionary<Paragraph, string>? bulletImageRelIds = null)
    {
        return new XDocument(
            new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(P + "sld",
                NsAttr("p", P), NsAttr("a", A), NsAttr("r", R),
                slide.IsHidden ? new XAttribute("show", "0") : null,
                new XElement(P + "cSld",
                    slide.Background is not null
                        ? new XElement(P + "bg",
                            new XElement(P + "bgPr",
                                BuildFillEl(slide.Background, scheme),
                                new XElement(A + "effectLst")))
                        : null,
                    new XElement(P + "spTree",
                        GrpSpHeader(),
                        slide.Shapes
                            .Select(s => BuildShapeEl(s, scheme, mediaById, smartArtRelIdRemap, hlinkRelIds, allSlides, fillBlipById, prvRelIdByShapeAndOldId, captionTracksByShape, bulletImageRelIds))
                            .OfType<XElement>())),
                // II2: p:hf is NOT valid on p:sld (CT_Slide schema has no hf element);
                // it is only valid on slideMaster/slideLayout/handoutMaster/notesMaster.
                // We intentionally do NOT emit p:hf here to avoid PowerPoint repair.
                // HfVisibility is preserved on the model for read-back from real .pptx files
                // that carry it on the master/layout (which the reader already handles).
                BuildSlideClrMapOvrEl(slide.ColorMapOverride),
                BuildTransitionEl(slide.Transition, transSoundRelId),
                BuildTimingEl(slide)));
    }

    /// <summary>
    /// Builds the <c>p:clrMapOvr</c> element for a slide.
    /// When <paramref name="colorMapOverride"/> is non-null, emits
    /// <c>&lt;p:clrMapOvr&gt;&lt;a:overrideClrMapping .../&gt;&lt;/p:clrMapOvr&gt;</c>
    /// with the stored role→slot attributes.
    /// When null, emits <c>&lt;p:clrMapOvr&gt;&lt;a:masterClrMapping/&gt;&lt;/p:clrMapOvr&gt;</c>.
    /// </summary>
    private static XElement BuildSlideClrMapOvrEl(Dictionary<string, string>? colorMapOverride)
    {
        if (colorMapOverride is { Count: > 0 })
        {
            var overrideEl = new XElement(A + "overrideClrMapping");
            foreach (var (key, val) in colorMapOverride)
                overrideEl.Add(new XAttribute(key, val));
            return new XElement(P + "clrMapOvr", overrideEl);
        }
        return new XElement(P + "clrMapOvr", new XElement(A + "masterClrMapping"));
    }

    // ── notesSlide.xml ────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a minimal but spec-valid ppt/notesSlides/notesSlideN.xml containing the notes
    /// text in the body placeholder (p:ph type="body" idx="1") and a slide-image placeholder
    /// (p:ph type="sldImg" idx="0") required by the schema.
    /// </summary>
    private static XDocument BuildNotesSlideXml(TextBody notes, string _slidePath)
    {
        // Slide-image placeholder (required by the notes slide schema)
        var slideImgSp = new XElement(P + "sp",
            new XElement(P + "nvSpPr",
                new XElement(P + "cNvPr", new XAttribute("id", "2"), new XAttribute("name", "Slide Image Placeholder 1")),
                new XElement(P + "cNvSpPr", new XElement(A + "spLocks", new XAttribute("noGrp", "1"), new XAttribute("noRot", "1"), new XAttribute("noChangeAspect", "1"))),
                new XElement(P + "nvPr", new XElement(P + "ph", new XAttribute("type", "sldImg")))),
            new XElement(P + "spPr"));

        // Body placeholder (carries the notes text)
        var notesTxBody = BuildNotesTxBodyEl(notes);
        var notesBodySp = new XElement(P + "sp",
            new XElement(P + "nvSpPr",
                new XElement(P + "cNvPr", new XAttribute("id", "3"), new XAttribute("name", "Content Placeholder 2")),
                new XElement(P + "cNvSpPr", new XElement(A + "spLocks", new XAttribute("noGrp", "1"))),
                new XElement(P + "nvPr", new XElement(P + "ph", new XAttribute("type", "body"), new XAttribute("idx", "1")))),
            new XElement(P + "spPr"),
            notesTxBody);

        return new XDocument(
            new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(P + "notes",
                NsAttr("p", P), NsAttr("a", A), NsAttr("r", R),
                new XAttribute("showMasterSp", "1"),
                new XElement(P + "cSld",
                    new XElement(P + "spTree",
                        GrpSpHeader(),
                        slideImgSp,
                        notesBodySp)),
                new XElement(P + "clrMapOvr",
                    new XElement(A + "masterClrMapping"))));
    }

    private static XElement BuildDefaultTextStyleEl()
    {
        var defaultTextStyle = new XElement(P + "defaultTextStyle",
            new XElement(A + "defPPr",
                new XElement(A + "defRPr", new XAttribute("lang", "en-US"))));

        for (int level = 1; level <= 9; level++)
        {
            defaultTextStyle.Add(
                new XElement(A + $"lvl{level}pPr",
                    new XAttribute("marL", 457200L * (level - 1)),
                    new XAttribute("algn", "l"),
                    new XAttribute("defTabSz", 914400),
                    new XAttribute("rtl", "0"),
                    new XAttribute("eaLnBrk", "1"),
                    new XAttribute("latinLnBrk", "0"),
                    new XAttribute("hangingPunct", "1"),
                    BuildDefaultTextRunPropertiesEl()));
        }

        return defaultTextStyle;
    }

    private static XElement BuildDefaultTextRunPropertiesEl() =>
        new XElement(A + "defRPr",
            new XAttribute("sz", "1800"),
            new XAttribute("kern", "1200"),
            new XElement(A + "solidFill",
                new XElement(A + "schemeClr", new XAttribute("val", "tx1"))),
            new XElement(A + "latin", new XAttribute("typeface", "+mn-lt")),
            new XElement(A + "ea", new XAttribute("typeface", "+mn-ea")),
            new XElement(A + "cs", new XAttribute("typeface", "+mn-cs")));

    /// <summary>
    /// Converts a <see cref="TextBody"/> into the p:txBody element suitable for a notes placeholder.
    /// Re-uses the existing <see cref="BuildTxBodyEl"/> but wraps it in a P: element (not A:).
    /// </summary>
    private static XElement BuildNotesTxBodyEl(TextBody notes)
    {
        var bodyPr = new XElement(A + "bodyPr");
        return new XElement(P + "txBody",
            bodyPr,
            new XElement(A + "lstStyle"),
            notes.Paragraphs.Select(p => BuildParaEl(p)));
    }

    /// <summary>
    /// Builds the default notes master used by PowerPoint for a new presentation.  The
    /// notes-slide part contains only placeholder identities; the master owns the visible
    /// slide-image, notes, header/footer, and slide-number geometry.
    /// </summary>
    private static XDocument BuildNotesMasterXml() =>
        new XDocument(
            new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(P + "notesMaster",
                NsAttr("p", P), NsAttr("a", A), NsAttr("r", R),
                new XElement(P + "cSld",
                    new XElement(P + "bg",
                        new XElement(P + "bgRef",
                            new XAttribute("idx", "1001"),
                            new XElement(A + "schemeClr", new XAttribute("val", "bg1")))),
                    new XElement(P + "spTree",
                        new XElement(P + "nvGrpSpPr",
                            new XElement(P + "cNvPr", new XAttribute("id", "1"), new XAttribute("name", "")),
                            new XElement(P + "cNvGrpSpPr"),
                            new XElement(P + "nvPr")),
                        new XElement(P + "grpSpPr",
                            new XElement(A + "xfrm",
                                new XElement(A + "off", new XAttribute("x", "0"), new XAttribute("y", "0")),
                                new XElement(A + "ext", new XAttribute("cx", "0"), new XAttribute("cy", "0")),
                                new XElement(A + "chOff", new XAttribute("x", "0"), new XAttribute("y", "0")),
                                new XElement(A + "chExt", new XAttribute("cx", "0"), new XAttribute("cy", "0")))),
                        BuildNotesMasterPlaceholder(
                            id: 2, name: "Header Placeholder 1", type: "hdr", idx: null, size: "quarter",
                            x: 0, y: 0, width: 2971800, height: 458788, paragraphAlignment: "l"),
                        BuildNotesMasterPlaceholder(
                            id: 3, name: "Date Placeholder 2", type: "dt", idx: 1, size: null,
                            x: 3884613, y: 0, width: 2971800, height: 458788, paragraphAlignment: "r"),
                        BuildNotesMasterPlaceholder(
                            id: 4, name: "Slide Image Placeholder 3", type: "sldImg", idx: 2, size: null,
                            x: 685800, y: 1143000, width: 5486400, height: 3086100,
                            paragraphAlignment: null, noFill: true, outlined: true),
                        BuildNotesMasterPlaceholder(
                            id: 5, name: "Notes Placeholder 4", type: "body", idx: 3, size: "quarter",
                            x: 685800, y: 4400550, width: 5486400, height: 3600450, paragraphAlignment: "l"),
                        BuildNotesMasterPlaceholder(
                            id: 6, name: "Footer Placeholder 5", type: "ftr", idx: 4, size: "quarter",
                            x: 0, y: 8685213, width: 2971800, height: 458787, paragraphAlignment: "l",
                            anchor: "b"),
                        BuildNotesMasterPlaceholder(
                            id: 7, name: "Slide Number Placeholder 6", type: "sldNum", idx: 5, size: "quarter",
                            x: 3884613, y: 8685213, width: 2971800, height: 458787, paragraphAlignment: "r",
                            anchor: "b")),
                    new XElement(P + "extLst",
                        new XElement(P + "ext",
                            new XAttribute("uri", "{BB962C8B-B14F-4D97-AF65-F5344CB8AC3E}"),
                            new XElement(P14 + "creationId",
                                new XAttribute(XNamespace.Xmlns + "p14", P14.NamespaceName),
                                new XAttribute("val", "1"))))),
                new XElement(P + "clrMap",
                    new XAttribute("bg1", "lt1"),   new XAttribute("tx1", "dk1"),
                    new XAttribute("bg2", "lt2"),   new XAttribute("tx2", "dk2"),
                    new XAttribute("accent1", "accent1"), new XAttribute("accent2", "accent2"),
                    new XAttribute("accent3", "accent3"), new XAttribute("accent4", "accent4"),
                    new XAttribute("accent5", "accent5"), new XAttribute("accent6", "accent6"),
                    new XAttribute("hlink", "hlink"), new XAttribute("folHlink", "folHlink")),
                BuildNotesStyleEl()));

    private static XElement BuildNotesMasterPlaceholder(
        uint id,
        string name,
        string type,
        int? idx,
        string? size,
        long x,
        long y,
        long width,
        long height,
        string? paragraphAlignment,
        bool noFill = false,
        bool outlined = false,
        string? anchor = null)
    {
        var placeholder = new XElement(P + "ph", new XAttribute("type", type));
        if (size is not null)
            placeholder.Add(new XAttribute("sz", size));
        if (idx.HasValue)
            placeholder.Add(new XAttribute("idx", idx.Value));

        var bodyPr = new XElement(A + "bodyPr",
            new XAttribute("vert", "horz"),
            new XAttribute("lIns", "91440"),
            new XAttribute("tIns", "45720"),
            new XAttribute("rIns", "91440"),
            new XAttribute("bIns", "45720"),
            new XAttribute("rtlCol", "0"));
        if (anchor is not null)
            bodyPr.Add(new XAttribute("anchor", anchor));

        var textBody = new XElement(P + "txBody",
            bodyPr,
            new XElement(A + "lstStyle"),
            new XElement(A + "p",
                paragraphAlignment is null
                    ? null
                    : new XElement(A + "pPr", new XAttribute("algn", paragraphAlignment)),
                new XElement(A + "endParaRPr", new XAttribute("lang", "en-US"))));

        var shapeProperties = new XElement(P + "spPr",
            new XElement(A + "xfrm",
                new XElement(A + "off", new XAttribute("x", x), new XAttribute("y", y)),
                new XElement(A + "ext", new XAttribute("cx", width), new XAttribute("cy", height))),
            new XElement(A + "prstGeom", new XAttribute("prst", "rect"), new XElement(A + "avLst")));
        if (noFill)
            shapeProperties.Add(new XElement(A + "noFill"));
        if (outlined)
        {
            shapeProperties.Add(new XElement(A + "ln",
                new XAttribute("w", "12700"),
                new XElement(A + "solidFill", new XElement(A + "prstClr", new XAttribute("val", "black")))));
        }

        return new XElement(P + "sp",
            new XElement(P + "nvSpPr",
                new XElement(P + "cNvPr", new XAttribute("id", id), new XAttribute("name", name)),
                new XElement(P + "cNvSpPr",
                    new XElement(A + "spLocks",
                        new XAttribute("noGrp", "1"),
                        type == "sldImg" ? new XAttribute("noRot", "1") : null,
                        type == "sldImg" ? new XAttribute("noChangeAspect", "1") : null)),
                new XElement(P + "nvPr", placeholder)),
            shapeProperties,
            textBody);
    }

    private static XElement BuildNotesStyleEl()
    {
        var notesStyle = new XElement(P + "notesStyle");
        for (int level = 1; level <= 9; level++)
        {
            notesStyle.Add(
                new XElement(A + $"lvl{level}pPr",
                    new XAttribute("marL", 457200L * (level - 1)),
                    new XAttribute("algn", "l"),
                    new XAttribute("defTabSz", 914400),
                    new XAttribute("rtl", "0"),
                    new XAttribute("eaLnBrk", "1"),
                    new XAttribute("latinLnBrk", "0"),
                    new XAttribute("hangingPunct", "1"),
                    new XElement(A + "defRPr",
                        new XAttribute("sz", "1200"),
                        new XAttribute("kern", "1200"),
                        new XElement(A + "solidFill",
                            new XElement(A + "schemeClr", new XAttribute("val", "tx1"))),
                        new XElement(A + "latin", new XAttribute("typeface", "+mn-lt")),
                        new XElement(A + "ea", new XAttribute("typeface", "+mn-ea")),
                        new XElement(A + "cs", new XAttribute("typeface", "+mn-cs")))));
        }

        return notesStyle;
    }

    // ── commentAuthors.xml ────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a globally-consistent map from (author-name, initials) → numeric id.
    /// Authors are assigned ids 0, 1, 2 … in first-encounter order across ALL slides.
    /// This single source of truth is shared by both BuildCommentAuthorsXml and
    /// BuildCommentsXml so the two parts always agree on author ids.
    /// </summary>
    private static Dictionary<(string name, string initials), int> BuildGlobalAuthorMap(List<Slide> slides)
    {
        var map    = new Dictionary<(string name, string initials), int>();
        int nextId = 0;
        foreach (var slide in slides)
        {
            foreach (var cm in slide.Comments)
            {
                var key = (cm.Author, cm.Initials);
                if (!map.ContainsKey(key))
                    map[key] = nextId++;
            }
        }
        return map;
    }

    /// <summary>
    /// Builds a ppt/commentAuthors.xml using the pre-built global author map.
    /// Authors are keyed by (name, initials) with ids 0, 1, 2, … in first-encounter order.
    /// </summary>
    private static XDocument BuildCommentAuthorsXml(List<Slide> slides)
    {
        var authorMap = BuildGlobalAuthorMap(slides);

        // Track the highest comment idx seen per author (for the lastIdx attribute).
        var authorLastIdx = new Dictionary<int, int>();
        foreach (var kv in authorMap)
            authorLastIdx[kv.Value] = 0;

        foreach (var slide in slides)
        {
            foreach (var cm in slide.Comments)
            {
                var key = (cm.Author, cm.Initials);
                if (authorMap.TryGetValue(key, out var authorId) && cm.Idx > authorLastIdx[authorId])
                    authorLastIdx[authorId] = cm.Idx;
            }
        }

        var authorElements = authorMap.Select(kv =>
            new XElement(P + "cmAuthor",
                new XAttribute("id",       kv.Value),
                new XAttribute("name",     kv.Key.name),
                new XAttribute("initials", kv.Key.initials),
                new XAttribute("lastIdx",  authorLastIdx[kv.Value]),
                new XAttribute("clrIdx",   kv.Value % 8)));

        return new XDocument(
            new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(P + "cmAuthorLst",
                NsAttr("p", P), NsAttr("a", A), NsAttr("r", R),
                authorElements));
    }

    // ── comments/commentN.xml ─────────────────────────────────────────────────────

    /// <summary>
    /// Builds a ppt/comments/commentN.xml for a single slide's comments.
    /// Uses the GLOBAL author map so authorId values are consistent with commentAuthors.xml.
    /// Comment indices (idx) are always renumbered sequentially (1-based) per slide on write
    /// to prevent duplicate-idx collisions when the model has stale/cloned Idx values.
    /// </summary>
    private static XDocument BuildCommentsXml(
        List<SlideComment> comments,
        Dictionary<(string name, string initials), int> globalAuthorMap)
    {
        var cmElements = comments.Select((cm, i) =>
        {
            // Look up author id in the GLOBAL map (not locally re-derived).
            globalAuthorMap.TryGetValue((cm.Author, cm.Initials), out var authorId);

            var dtStr = cm.DateTime?.ToString("yyyy-MM-ddTHH:mm:ss", System.Globalization.CultureInfo.InvariantCulture)
                        ?? System.DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);

            // Always renumber idx sequentially (1-based) per slide to avoid duplicate-idx
            // collisions when the model contains stale/cloned Idx values (BB5 fix).
            return (XElement)new XElement(P + "cm",
                new XAttribute("authorId", authorId),
                new XAttribute("dt",       dtStr),
                new XAttribute("idx",      i + 1),
                new XElement(P + "pos",
                    new XAttribute("x", cm.Xemu),
                    new XAttribute("y", cm.Yemu)),
                new XElement(P + "text", cm.Text));
        });

        return new XDocument(
            new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(P + "cmLst",
                NsAttr("p", P), NsAttr("a", A), NsAttr("r", R),
                cmElements));
    }

    private static bool ShouldUseModernComments(Slide slide)
        => slide.Comments.Any(comment =>
            comment.UsesModernCommentSchema ||
            comment.IsResolved ||
            comment.Replies.Count > 0);

    private static Dictionary<(string name, string initials), ModernCommentAuthorProfile> BuildModernAuthorMap(List<Slide> slides)
    {
        var map = new Dictionary<(string name, string initials), ModernCommentAuthorProfile>();
        foreach (var comment in slides.SelectMany(slide => slide.Comments))
        {
            AddModernAuthor(
                comment.Author,
                comment.Initials,
                comment.ModernAuthorId,
                comment.ModernAuthorUserId,
                comment.ModernAuthorProviderId);
            foreach (var reply in comment.Replies)
            {
                AddModernAuthor(
                    reply.Author,
                    reply.Initials,
                    reply.ModernAuthorId,
                    reply.ModernAuthorUserId,
                    reply.ModernAuthorProviderId);
            }
        }

        return map;

        void AddModernAuthor(
            string name,
            string initials,
            string? preservedId,
            string? preservedUserId,
            string? preservedProviderId)
        {
            var key = (name, initials);
            var hasPreservedId = !string.IsNullOrWhiteSpace(preservedId);
            var profile = new ModernCommentAuthorProfile(
                name,
                initials,
                hasPreservedId
                    ? preservedId!.Trim()
                    : DeterministicGuidString($"freep-modern-comment-author|{name}|{initials}"),
                string.IsNullOrWhiteSpace(preservedUserId)
                    ? $"{NormalizeModernUserId(name)}::freep"
                    : preservedUserId!.Trim(),
                preservedProviderId ?? string.Empty,
                hasPreservedId);

            if (!map.TryGetValue(key, out var existing) || (!existing.IsPreserved && profile.IsPreserved))
            {
                map[key] = profile;
            }
        }
    }

    private static XDocument BuildModernAuthorsXml(
        Dictionary<(string name, string initials), ModernCommentAuthorProfile> modernAuthorMap)
    {
        var authorElements = modernAuthorMap.Values.Select(profile =>
            new XElement(P188 + "author",
                new XAttribute("id", profile.Id),
                new XAttribute("name", profile.Name),
                new XAttribute("initials", profile.Initials),
                new XAttribute("userId", profile.UserId),
                new XAttribute("providerId", profile.ProviderId)));

        return new XDocument(
            new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(P188 + "authorLst",
                NsAttr("p188", P188),
                authorElements));
    }

    private static XDocument BuildModernCommentsXml(
        List<SlideComment> comments,
        int slideIndex,
        Dictionary<(string name, string initials), ModernCommentAuthorProfile> modernAuthorMap)
    {
        var cmElements = comments.Select((comment, commentIndex) =>
        {
            var authorId = ModernAuthorId(modernAuthorMap, comment.Author, comment.Initials);
            var children = new List<object>
            {
                BuildModernAnchorElement(comment),
                new XElement(P188 + "pos",
                    new XAttribute("x", comment.Xemu),
                    new XAttribute("y", comment.Yemu)),
            };

            if (comment.Replies.Count > 0)
            {
                children.Add(new XElement(P188 + "replyLst",
                    comment.Replies.Select((reply, replyIndex) =>
                    {
                        var replyAuthorId = ModernAuthorId(modernAuthorMap, reply.Author, reply.Initials);
                        return new XElement(P188 + "reply",
                            new XAttribute("id", string.IsNullOrWhiteSpace(reply.ModernReplyId)
                                ? DeterministicGuidString(
                                    $"freep-modern-comment-reply|{slideIndex}|{commentIndex}|{replyIndex}|{replyAuthorId}|{reply.Text}|{FormatModernDate(reply.DateTime)}")
                                : reply.ModernReplyId.Trim()),
                            new XAttribute("authorId", replyAuthorId),
                            new XAttribute("status", "active"),
                            new XAttribute("created", FormatModernDate(reply.DateTime)),
                            BuildModernTextBody(reply.Text));
                    })));
            }

            children.Add(BuildModernTextBody(comment.Text));

            return new XElement(P188 + "cm",
                new XAttribute("id", string.IsNullOrWhiteSpace(comment.ModernCommentId)
                    ? DeterministicGuidString(
                        $"freep-modern-comment|{slideIndex}|{commentIndex}|{authorId}|{comment.Text}|{FormatModernDate(comment.DateTime)}")
                    : comment.ModernCommentId.Trim()),
                new XAttribute("authorId", authorId),
                new XAttribute("status", comment.IsResolved ? "resolved" : "active"),
                new XAttribute("created", FormatModernDate(comment.DateTime)),
                children);
        });

        return new XDocument(
            new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(P188 + "cmLst",
                NsAttr("p188", P188),
                NsAttr("a", A),
                cmElements));
    }

    private static XElement BuildModernAnchorElement(SlideComment comment)
    {
        if (!string.IsNullOrWhiteSpace(comment.ModernAnchorXml))
        {
            try
            {
                var anchor = XElement.Parse(comment.ModernAnchorXml);
                if (IsModernCommentAnchorElement(anchor))
                {
                    return new XElement(anchor);
                }
            }
            catch (XmlException)
            {
                // Fall through to PowerPoint's generic modern comment anchor below.
            }
        }

        return new XElement(P188 + "unknownAnchor");
    }

    private static bool IsModernCommentAnchorElement(XElement element)
    {
        return element.Name.LocalName is
            "unknownAnchor" or
            "sldMkLst" or
            "deMkLst" or
            "txMkLst";
    }

    private static XElement BuildModernTextBody(string text)
        => new(P188 + "txBody",
            new XElement(A + "bodyPr"),
            new XElement(A + "lstStyle"),
            new XElement(A + "p",
                new XElement(A + "r",
                    new XElement(A + "t", text))));

    private static string ModernAuthorId(
        Dictionary<(string name, string initials), ModernCommentAuthorProfile> modernAuthorMap,
        string name,
        string initials)
        => modernAuthorMap.TryGetValue((name, initials), out var profile)
            ? profile.Id
            : DeterministicGuidString($"freep-modern-comment-author|{name}|{initials}");

    private static string FormatModernDate(DateTime? value)
        => (value ?? DateTime.UtcNow).ToString("O", CultureInfo.InvariantCulture);

    private static string DeterministicGuidString(string value)
    {
        var bytes = MD5.HashData(System.Text.Encoding.UTF8.GetBytes(value));
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x30);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return $"{{{new Guid(bytes):D}}}";
    }

    private static string NormalizeModernUserId(string name)
    {
        var normalized = new string((name ?? string.Empty)
            .Trim()
            .Select(ch => char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : '.')
            .ToArray())
            .Trim('.');

        return string.IsNullOrWhiteSpace(normalized)
            ? "reviewer@freep.local"
            : $"{normalized}@freep.local";
    }

    // ── p:transition ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds the mc:AlternateContent wrapping a p:transition element, or re-emits
    /// the verbatim RawXml for unrecognized (Other) transitions.
    /// <paramref name="soundRelId"/> is the r:embed relationship id of the audio part, if any.
    /// </summary>
    private static XElement? BuildTransitionEl(SlideTransition? transition, string? soundRelId = null)
    {
        if (transition is null || transition.Kind == TransitionKind.None)
            return null;

        // ── Other / unrecognized: re-emit the verbatim raw XML ────────────────────
        // This guarantees NO transition is silently dropped on round-trip, even for
        // future or proprietary transition kinds we don't enumerate.
        if (transition.Kind == TransitionKind.Other && transition.RawXml is not null)
        {
            try
            {
                var rawEl = XElement.Parse(transition.RawXml, LoadOptions.PreserveWhitespace);

                // If there's a sound to re-attach and the raw XML doesn't already contain
                // a sndAc element, inject it.
                if (soundRelId is not null && rawEl.Element(P + "sndAc") is null)
                    rawEl.Add(BuildSndAcEl(soundRelId, transition.Sound));

                // Wrap in mc:AlternateContent so modern readers benefit from p14:dur (if present).
                // If the raw XML already has p14:dur we just re-emit as-is inside a wrapper.
                return rawEl;
            }
            catch
            {
                // Malformed raw XML — fall through to synthesize a fade fallback so we don't crash.
                return BuildFadeTransitionEl(transition.DurationMs, transition.AdvanceOnClick,
                    transition.AdvanceAfterMs, soundRelId, transition.Sound);
            }
        }

        var spd = PptxAnimationMap.DurationToSpd(transition.DurationMs);

        // EB1/EB3: determine the namespace for the effect child element.
        // Classic p: kinds are defined in CT_SlideTransition (ECMA-376 2006 schema).
        // Extended kinds (p14:) and morph (p159:) use extension namespaces.
        // Emitting extended kinds as p:-namespace children causes PowerPoint repair.
        var effectName = PptxAnimationMap.TransitionKindToElementName(transition.Kind);
        var dirAttr    = PptxAnimationMap.TransitionDirectionToAttr(transition.Direction);

        bool isMorph    = transition.Kind == TransitionKind.Morph;
        bool isP14Kind  = effectName is not null && P14TransitionKinds.Contains(transition.Kind);

        // Build a classic p: effect element (for classic kinds or as fallback in mc:Fallback).
        XElement? BuildClassicEffectEl()
        {
            if (effectName is null || isMorph || isP14Kind) return null;
            var attrs = new List<object?>();
            if (transition.Kind == TransitionKind.Split)
            {
                var orientation = transition.SplitOrientation
                    ?? (transition.Direction is TransitionDirection.Horizontal or TransitionDirection.Vertical
                        ? transition.Direction
                        : null);
                var orientationAttr = PptxAnimationMap.TransitionDirectionToAttr(orientation);
                if (orientationAttr is not null)
                    attrs.Add(new XAttribute("orient", orientationAttr));
                var splitDirection = transition.Direction is TransitionDirection.In or TransitionDirection.Out
                    ? dirAttr
                    : null;
                if (splitDirection is not null)
                    attrs.Add(new XAttribute("dir", splitDirection));
            }
            else if (dirAttr is not null)
            {
                attrs.Add(new XAttribute("dir", dirAttr));
            }
            if (transition.Kind is TransitionKind.Wheel or TransitionKind.WheelReverse &&
                transition.WheelSpokeCount is > 0)
            {
                attrs.Add(new XAttribute("spokes", transition.WheelSpokeCount.Value));
            }
            return new XElement(P + effectName,
                attrs.Where(x => x is not null).Cast<object>().ToArray());
        }

        // Build the p14:-namespace effect element (for P14TransitionKinds).
        XElement? BuildP14EffectEl()
        {
            if (!isP14Kind || effectName is null) return null;
            var attrs = new List<object?>();
            if (dirAttr is not null) attrs.Add(new XAttribute("dir", dirAttr));
            if (transition.Kind is TransitionKind.Wheel or TransitionKind.WheelReverse &&
                transition.WheelSpokeCount is > 0)
            {
                attrs.Add(new XAttribute("spokes", transition.WheelSpokeCount.Value));
            }
            return new XElement(P14 + effectName,
                attrs.Where(x => x is not null).Cast<object>().ToArray());
        }

        // Build the p159:-namespace morph element (EB3).
        XElement? BuildMorphEl()
        {
            if (!isMorph) return null;
            var attrs = new List<object?>();
            if (transition.MorphOption is not null)
                attrs.Add(new XAttribute("option", transition.MorphOption));
            return new XElement(P159 + "morph",
                attrs.Where(x => x is not null).Cast<object>().ToArray());
        }

        // Build a p:transition element with the given effect child (or fallback p:fade) + optional sound.
        XElement BuildTransEl(IEnumerable<object?> extraAttrs, XElement? effectEl)
        {
            var ch = new List<object?>();
            ch.AddRange(extraAttrs);
            if (effectEl is not null) ch.Add(effectEl);
            if (soundRelId is not null)
                ch.Add(BuildSndAcEl(soundRelId, transition.Sound));
            return new XElement(P + "transition",
                ch.Where(x => x is not null).Cast<object>().ToArray());
        }

        // A classic p:fade is used as the mc:Fallback effect so old readers see SOMETHING.
        var fadeEl = new XElement(P + "fade");

        // Common non-duration attributes (spd, advClick, advTm)
        var commonAttrs = new List<object?>();
        commonAttrs.Add(new XAttribute("spd", spd));
        if (!transition.AdvanceOnClick)
            commonAttrs.Add(new XAttribute("advClick", "0"));
        if (transition.AdvanceAfterMs.HasValue)
            commonAttrs.Add(new XAttribute("advTm", transition.AdvanceAfterMs.Value));

        if (isMorph)
        {
            // EB3: morph → p159:morph inside mc:Choice Requires="p159".
            // Fallback uses p:fade so old readers degrade gracefully.
            var choiceAttrs = new List<object?>(commonAttrs) { new XAttribute(P14 + "dur", transition.DurationMs) };
            return new XElement(MC + "AlternateContent",
                new XAttribute(XNamespace.Xmlns + "mc",  MC.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "p14", P14.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "p159", P159.NamespaceName),
                new XElement(MC + "Choice",
                    new XAttribute("Requires", "p159"),
                    BuildTransEl(choiceAttrs, BuildMorphEl())),
                new XElement(MC + "Fallback",
                    BuildTransEl(commonAttrs, fadeEl)));
        }

        if (isP14Kind)
        {
            // EB1: p14 extended kinds → p14:effectName inside mc:Choice Requires="p14".
            // Fallback uses p:fade so old readers degrade gracefully.
            var choiceAttrs = new List<object?>(commonAttrs) { new XAttribute(P14 + "dur", transition.DurationMs) };
            return new XElement(MC + "AlternateContent",
                new XAttribute(XNamespace.Xmlns + "mc",  MC.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "p14", P14.NamespaceName),
                new XElement(MC + "Choice",
                    new XAttribute("Requires", "p14"),
                    BuildTransEl(choiceAttrs, BuildP14EffectEl())),
                new XElement(MC + "Fallback",
                    BuildTransEl(commonAttrs, fadeEl)));
        }

        // Classic p: kind (fade/cut/push/wipe/etc.) — AC1: wrap in mc:AlternateContent for p14:dur.
        // The mc:Fallback carries the legacy spd-only p:transition so old readers degrade gracefully.
        // This mirrors the pattern PowerPoint itself writes; bare "dur" on p:transition is invalid per
        // CT_SlideTransition (ECMA-376) and is flagged by OpenXmlValidator.
        {
            var classicEff   = BuildClassicEffectEl();
            var choiceAttrs  = new List<object?>(commonAttrs) { new XAttribute(P14 + "dur", transition.DurationMs) };
            return new XElement(MC + "AlternateContent",
                new XAttribute(XNamespace.Xmlns + "mc",  MC.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "p14", P14.NamespaceName),
                new XElement(MC + "Choice",
                    new XAttribute("Requires", "p14"),
                    BuildTransEl(choiceAttrs, classicEff)),
                new XElement(MC + "Fallback",
                    BuildTransEl(commonAttrs, classicEff is not null ? new XElement(classicEff) : null)));
        }
    }

    /// <summary>Builds a Fade mc:AlternateContent — used as last-resort fallback.</summary>
    private static XElement BuildFadeTransitionEl(
        int durationMs, bool advanceOnClick, int? advanceAfterMs,
        string? soundRelId, TransitionSound? sound)
    {
        var spd = PptxAnimationMap.DurationToSpd(durationMs);
        var common = new List<object?> { new XAttribute("spd", spd) };
        if (!advanceOnClick) common.Add(new XAttribute("advClick", "0"));
        if (advanceAfterMs.HasValue) common.Add(new XAttribute("advTm", advanceAfterMs.Value));

        XElement BuildFadeEl(IEnumerable<object?> attrs)
        {
            var ch = new List<object?>(attrs);
            ch.Add(new XElement(P + "fade"));
            if (soundRelId is not null) ch.Add(BuildSndAcEl(soundRelId, sound));
            return new XElement(P + "transition", ch.Where(x => x is not null).Cast<object>().ToArray());
        }

        var choice = new List<object?>(common) { new XAttribute(P14 + "dur", durationMs) };
        return new XElement(MC + "AlternateContent",
            new XAttribute(XNamespace.Xmlns + "mc", MC.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "p14", P14.NamespaceName),
            new XElement(MC + "Choice", new XAttribute("Requires", "p14"), BuildFadeEl(choice)),
            new XElement(MC + "Fallback", BuildFadeEl(common)));
    }

    /// <summary>
    /// Builds the <c>p:sndAc</c> element that carries the transition sound reference.
    /// </summary>
    private static XElement BuildSndAcEl(string soundRelId, TransitionSound? sound)
    {
        var sndEl = new XElement(P + "snd",
            new XAttribute(R + "embed", soundRelId));

        var stSndAttrs = new List<object?> { sndEl };
        if (sound?.Loop == true)
            stSndAttrs.Insert(0, new XAttribute("loop", "1"));

        return new XElement(P + "sndAc",
            new XElement(P + "stSnd", stSndAttrs.Cast<object>().ToArray()));
    }

    /// <summary>
    /// Writes the transition sound audio bytes as an embedded part in the archive,
    /// returning the assigned relId and content-type. Returns null if no sound bytes are available.
    /// </summary>
    private static (string relId, string contentType, string partPath)? WriteTransitionSoundPart(
        ZipArchive archive, SlideTransition transition, int slideIndex, HashSet<string> usedRelIds)
    {
        var sound = transition.Sound;
        if (sound is null || sound.AudioBytes is null || sound.AudioBytes.Length == 0)
            return null;

        // Determine extension from content-type.
        var ct = sound.ContentType ?? "audio/mpeg";
        var ext = ct switch
        {
            "audio/mpeg" or "audio/mp3"  => "mp3",
            "audio/wav"                  => "wav",
            "audio/ogg"                  => "ogg",
            "audio/aac"                  => "aac",
            "audio/x-ms-wma"             => "wma",
            _                            => "mp3"
        };

        var partPath = $"ppt/media/transitionSnd{slideIndex}.{ext}";
        var relId    = $"rIdSnd{slideIndex}";

        // Avoid relId collision.
        int suffix = 1;
        while (usedRelIds.Contains(relId))
            relId = $"rIdSnd{slideIndex}x{suffix++}";

        try
        {
            var entry = archive.CreateEntry(partPath, System.IO.Compression.CompressionLevel.Optimal);
            using var s = entry.Open();
            s.Write(sound.AudioBytes, 0, sound.AudioBytes.Length);
        }
        catch
        {
            return null;
        }

        return (relId, ct, partPath);
    }

    // ── p:timing ─────────────────────────────────────────────────────────────────

    private static XElement? BuildTimingEl(Slide slide)
    {
        var animations = slide.Animations;
        var timedMedia = AllShapes(slide.Shapes)
            .Where(shape => shape.Kind == SlideShapeKind.Media
                && (shape.Media?.PlaybackStartMode == MediaPlaybackStartMode.Automatically
                    || shape.Media?.Loop == true))
            .ToList();
        if (animations.Count == 0 && timedMedia.Count == 0 && string.IsNullOrWhiteSpace(slide.AnimationBuildListXml))
            return null;

        // Split animations into main-sequence and trigger groups.
        var mainAnims    = animations.Where(a => a.TriggerShapeId is null).ToList();
        var triggerAnims = animations
            .Where(a => a.TriggerShapeId is not null)
            .GroupBy(a => a.TriggerShapeId!.Value)
            .ToList();

        uint nodeId = 1;

        // ── Main sequence ─────────────────────────────────────────────────────

        // Build click-groups for the main sequence.
        var clickGroups = new List<List<ShapeAnimation>>();
        foreach (var anim in mainAnims)
        {
            if (anim.Trigger == AnimationTrigger.OnClick || clickGroups.Count == 0)
                clickGroups.Add(new List<ShapeAnimation> { anim });
            else
                clickGroups[^1].Add(anim);
        }

        var seqChildItems = new List<XElement>();
        foreach (var group in clickGroups)
            seqChildItems.Add(BuildClickGroupEl(group, ref nodeId));

        var mainSeqEl = new XElement(P + "seq",
            new XAttribute("concurrent", "1"),
            new XAttribute("nextAc", "seek"),
            new XElement(P + "cTn",
                new XAttribute("id", nodeId++),
                new XAttribute("dur", "indefinite"),
                new XAttribute("nodeType", "mainSeq"),
                new XElement(P + "childTnLst", seqChildItems)));

        var outerParCTn = new XElement(P + "cTn",
            new XAttribute("id", nodeId++),
            new XAttribute("dur", "indefinite"),
            new XAttribute("restart", "whenNotActive"),
            new XAttribute("fill", "hold"),
            new XAttribute("nodeType", "interactiveSeq"),
            new XElement(P + "stCondLst",
                new XElement(P + "cond",
                    new XAttribute("evt", "onBegin"),
                    new XAttribute("delay", "indefinite"),
                    new XElement(P + "tn", new XAttribute("val", "0")))),
            new XElement(P + "childTnLst", mainSeqEl));

        // ── Trigger sequences ─────────────────────────────────────────────────

        var triggerPars = new List<XElement>();
        foreach (var group in triggerAnims)
        {
            var trigSpid = group.Key;
            var trigSeqEl = BuildTriggerSequenceEl(group.ToList(), trigSpid, ref nodeId);
            triggerPars.Add(trigSeqEl);
        }

        var rootChildTnLst = new XElement(P + "childTnLst");
        if (animations.Count > 0)
            rootChildTnLst.Add(new XElement(P + "par", outerParCTn));

        // Triggered sequences must live under the single root p:par. PowerPoint
        // repairs slides that put multiple p:par siblings directly under p:tnLst.
        foreach (var triggerPar in triggerPars)
            rootChildTnLst.Add(triggerPar);

        foreach (var shape in timedMedia)
            rootChildTnLst.Add(BuildMediaTimingEl(shape, ref nodeId));

        var outerPar = new XElement(P + "par",
            new XElement(P + "cTn",
                new XAttribute("id", nodeId++),
                new XAttribute("dur", "indefinite"),
                new XAttribute("restart", "never"),
                new XAttribute("fill", "hold"),
                new XAttribute("nodeType", "tmRoot"),
                new XElement(P + "stCondLst",
                    new XElement(P + "cond", new XAttribute("delay", "0"))),
                rootChildTnLst));

        return new XElement(P + "timing",
            new XElement(P + "tnLst", outerPar),
            BuildAnimationBuildListEl(slide.AnimationBuildListXml));
    }

    private static XElement? BuildAnimationBuildListEl(string? rawXml)
    {
        if (string.IsNullOrWhiteSpace(rawXml))
            return null;

        try
        {
            var element = XElement.Parse(rawXml, LoadOptions.PreserveWhitespace);
            return element.Name == P + "bldLst" ? element : null;
        }
        catch (XmlException)
        {
            return null;
        }
    }

    private static XElement BuildMediaTimingEl(SlideShape shape, ref uint nodeId)
    {
        var mediaElementName = shape.Media?.IsVideo == true ? P + "video" : P + "audio";
        bool automatic = shape.Media?.PlaybackStartMode == MediaPlaybackStartMode.Automatically;
        var condition = automatic
            ? new XElement(P + "cond",
                new XAttribute("evt", "onBegin"),
                new XAttribute("delay", "0"))
            : new XElement(P + "cond",
                new XAttribute("evt", "onClick"),
                new XAttribute("delay", "0"),
                new XElement(P + "tgtEl",
                    new XElement(P + "spTgt",
                        new XAttribute("spid", shape.Id.ToString(CultureInfo.InvariantCulture)))));
        var cTnAttributes = new List<object>
        {
            new XAttribute("id", nodeId++),
            new XAttribute("dur", "indefinite"),
            new XAttribute("fill", "hold"),
            new XAttribute("display", "0"),
        };
        if (shape.Media?.Loop == true)
            cTnAttributes.Add(new XAttribute("repeatCount", "indefinite"));

        return new XElement(mediaElementName,
            new XElement(P + "cMediaNode",
                new XAttribute("vol", "80000"),
                new XElement(P + "cTn",
                    cTnAttributes.Cast<object>().ToArray(),
                    new XElement(P + "stCondLst", condition)),
                new XElement(P + "tgtEl",
                    new XElement(P + "spTgt",
                        new XAttribute("spid", shape.Id.ToString(CultureInfo.InvariantCulture))))));
    }

    /// <summary>
    /// Builds a trigger (interactive) sequence for animations that fire when shapeId is clicked.
    /// Emits a p:par > p:cTn > p:childTnLst > p:seq (with onClick stCond targeting triggerShapeId).
    /// </summary>
    private static XElement BuildTriggerSequenceEl(List<ShapeAnimation> anims, uint triggerShapeId, ref uint nodeId)
    {
        // Build click-groups within this trigger sequence.
        var clickGroups = new List<List<ShapeAnimation>>();
        foreach (var anim in anims)
        {
            if (anim.Trigger == AnimationTrigger.OnClick || clickGroups.Count == 0)
                clickGroups.Add(new List<ShapeAnimation> { anim });
            else
                clickGroups[^1].Add(anim);
        }

        var seqChildItems = new List<XElement>();
        foreach (var group in clickGroups)
            seqChildItems.Add(BuildClickGroupEl(group, ref nodeId));

        // The trigger p:seq has stCondLst/cond evt="onClick" tgtEl/spTgt spid=triggerShapeId.
        var trigSeqEl = new XElement(P + "seq",
            new XAttribute("concurrent", "1"),
            new XAttribute("nextAc", "seek"),
            new XElement(P + "cTn",
                new XAttribute("id", nodeId++),
                new XAttribute("dur", "indefinite"),
                new XAttribute("nodeType", "interactiveSeq"),
                new XElement(P + "stCondLst",
                    new XElement(P + "cond",
                        new XAttribute("evt", "onClick"),
                        new XAttribute("delay", "0"),
                        new XElement(P + "tgtEl",
                            new XElement(P + "spTgt",
                                new XAttribute("spid", triggerShapeId))))),
                new XElement(P + "childTnLst", seqChildItems)));

        return new XElement(P + "par",
            new XElement(P + "cTn",
                new XAttribute("id", nodeId++),
                new XAttribute("fill", "hold"),
                new XElement(P + "stCondLst",
                    new XElement(P + "cond", new XAttribute("delay", "0"))),
                new XElement(P + "childTnLst", trigSeqEl)));
    }

    private static XElement BuildClickGroupEl(List<ShapeAnimation> group, ref uint nodeId)
    {
        var buildItems = new List<XElement>();
        for (int i = 0; i < group.Count; i++)
        {
            var anim = group[i];
            var itemTrigger = i == 0 ? AnimationTrigger.OnClick : anim.Trigger;
            buildItems.Add(BuildBuildItemEl(anim, itemTrigger, ref nodeId));
        }

        return new XElement(P + "par",
            new XElement(P + "cTn",
                new XAttribute("id", nodeId++),
                new XAttribute("fill", "hold"),
                new XElement(P + "stCondLst",
                    new XElement(P + "cond",
                        new XAttribute("evt", "onClick"),
                        new XAttribute("delay", "0"),
                        new XElement(P + "tn", new XAttribute("val", "0")))),
                new XElement(P + "childTnLst", buildItems)));
    }

    private static XElement BuildBuildItemEl(ShapeAnimation anim, AnimationTrigger triggerOverride, ref uint nodeId)
    {
        // Motion-path animation: emit p:animMotion instead of p:set.
        if (anim.Kind == AnimationKind.Motion && anim.Motion is not null)
            return BuildMotionBuildItemEl(anim, triggerOverride, ref nodeId);

        var (mappedPresetClass, mappedPresetId) = PptxAnimationMap.AnimationPresetToOoxml(anim.Preset, anim.Kind);
        bool hasRawPreset = !string.IsNullOrWhiteSpace(anim.RawPresetClass) && anim.RawPresetId.HasValue;
        string presetClass = hasRawPreset ? anim.RawPresetClass! : mappedPresetClass;
        int presetId = hasRawPreset ? anim.RawPresetId!.Value : mappedPresetId;
        var subtypeAttr = hasRawPreset && anim.RawPresetSubtype is not null
            ? anim.RawPresetSubtype
            : anim.Preset is AnimationPreset.Grow or AnimationPreset.Shrink or AnimationPreset.GrowWithColor
                ? "0"
                : AnimationAmountSemantics.IsGrowShrink(anim.Preset)
                    ? anim.EffectSubtype ?? "0"
                : anim.EffectSubtype ?? (anim.Preset == AnimationPreset.Split
                    ? PptxAnimationMap.AnimationDirectionToSubtype(anim.Direction is
                        AnimationDirection.Horizontal or AnimationDirection.Vertical
                        ? anim.Direction
                        : AnimationDirectionSemantics.ResolveSplitDirection(anim))
                    : PptxAnimationMap.AnimationDirectionToSubtype(anim.Direction));

        string delayStr = triggerOverride == AnimationTrigger.OnClick
            ? "indefinite"
            : anim.DelayMs.ToString(System.Globalization.CultureInfo.InvariantCulture);

        var cTnAttrs = new List<object>
        {
            new XAttribute("id", nodeId++),
            new XAttribute("presetClass", presetClass),
            new XAttribute("presetID", presetId),
            new XAttribute("presetSubtype", subtypeAttr ?? "0"),
            new XAttribute("dur", anim.DurationMs),
            new XAttribute("fill", "hold"),
            new XAttribute("grpId", "0"),
            new XAttribute("nodeType", "withEffect"),
        };
        AddRepeatAttributes(cTnAttrs, anim);

        var animEffectEl = BuildWheelSpokeAnimEffectEl(anim, ref nodeId);
        var childTimingItems = new List<object>();
        if (animEffectEl is not null)
            childTimingItems.Add(animEffectEl);
        if (AnimationAmountSemantics.IsGrowShrink(anim.Preset))
            childTimingItems.Add(BuildScaleBehaviorEl(anim, ref nodeId));
        if (anim.Preset is AnimationPreset.ColorPulse
            or AnimationPreset.ChangeColor
            or AnimationPreset.GrowWithColor
            or AnimationPreset.Shimmer)
        {
            var colorBehavior = BuildPreservedColorBehaviorEl(anim, ref nodeId);
            if (colorBehavior is not null)
                childTimingItems.Add(colorBehavior);
        }

        var setEl = new XElement(P + "set",
            new XElement(P + "cBhvr",
                new XElement(P + "cTn",
                    new XAttribute("id", nodeId++),
                    new XAttribute("dur", "1"),
                    new XAttribute("fill", "hold")),
                new XElement(P + "tgtEl",
                    new XElement(P + "spTgt", new XAttribute("spid", anim.ShapeId)))));
        childTimingItems.Add(setEl);

        return new XElement(P + "par",
            new XElement(P + "cTn",
                cTnAttrs.Cast<object>().ToArray(),
                new XElement(P + "stCondLst",
                    new XElement(P + "cond", new XAttribute("delay", delayStr))),
                new XElement(P + "childTnLst",
                    new XElement(P + "par",
                        new XElement(P + "cTn",
                            new XAttribute("id", nodeId++),
                            new XAttribute("fill", "hold"),
                            new XElement(P + "stCondLst",
                                new XElement(P + "cond", new XAttribute("delay", "0"))),
                            new XElement(P + "childTnLst", childTimingItems))))));
    }

    private static XElement? BuildPreservedColorBehaviorEl(ShapeAnimation anim, ref uint nodeId)
    {
        if (string.IsNullOrWhiteSpace(anim.PreservedColorBehaviorXml))
            return null;

        try
        {
            var colorBehavior = XElement.Parse(anim.PreservedColorBehaviorXml, LoadOptions.PreserveWhitespace);
            foreach (var timingNode in colorBehavior.Descendants(P + "cTn"))
                timingNode.SetAttributeValue("id", nodeId++);
            return colorBehavior;
        }
        catch (XmlException)
        {
            return null;
        }
    }

    private static XElement BuildScaleBehaviorEl(ShapeAnimation anim, ref uint nodeId)
    {
        var behavior = anim.ScaleBehavior
            ?? AnimationScaleBehavior.FromTo(anim.Preset == AnimationPreset.Shrink ? 0.8 : 1.2);
        var scale = new XElement(P + "animScale",
            behavior.ZoomContents.HasValue
                ? new XAttribute("zoomContents", behavior.ZoomContents.Value ? "1" : "0")
                : null,
            new XElement(P + "cBhvr",
                new XElement(P + "cTn",
                    new XAttribute("id", nodeId++),
                    new XAttribute("dur", anim.DurationMs),
                    new XAttribute("fill", "hold")),
                new XElement(P + "tgtEl",
                    new XElement(P + "spTgt", new XAttribute("spid", anim.ShapeId))),
                new XElement(P + "attrNameLst",
                    new XElement(P + "attrName", new XAttribute("val", "ScaleX")),
                    new XElement(P + "attrName", new XAttribute("val", "ScaleY")))));

        AddScalePosition(scale, "from", behavior.FromX, behavior.FromY);
        AddScalePosition(scale, "to", behavior.ToX, behavior.ToY);
        AddScalePosition(scale, "by", behavior.ByX, behavior.ByY);
        return scale;
    }

    private static void AddScalePosition(XElement parent, string name, string? x, string? y)
    {
        if (x is null && y is null)
            return;

        parent.Add(new XElement(P + name,
            x is not null ? new XAttribute("x", x) : null,
            y is not null ? new XAttribute("y", y) : null));
    }

    private static XElement? BuildWheelSpokeAnimEffectEl(ShapeAnimation anim, ref uint nodeId)
    {
        if (anim.Preset != AnimationPreset.Wheel || anim.WheelSpokeCount is not > 0)
            return null;

        return new XElement(P + "animEffect",
            new XAttribute("filter", FormattableString.Invariant($"wheel(spokes={anim.WheelSpokeCount.Value})")),
            new XAttribute("transition", anim.Kind == AnimationKind.Exit ? "out" : "in"),
            new XElement(P + "cBhvr",
                new XElement(P + "cTn",
                    new XAttribute("id", nodeId++),
                    new XAttribute("dur", anim.DurationMs),
                    new XElement(P + "stCondLst",
                        new XElement(P + "cond", new XAttribute("delay", "0")))),
                new XElement(P + "tgtEl",
                    new XElement(P + "spTgt", new XAttribute("spid", anim.ShapeId)))));
    }

    /// <summary>
    /// Emits a motion-path build item as a p:par containing p:animMotion.
    /// </summary>
    private static XElement BuildMotionBuildItemEl(ShapeAnimation anim, AnimationTrigger triggerOverride, ref uint nodeId)
    {
        string delayStr = triggerOverride == AnimationTrigger.OnClick
            ? "indefinite"
            : anim.DelayMs.ToString(System.Globalization.CultureInfo.InvariantCulture);

        var pathStr = BuildMotionPathString(anim.Motion!);

        var animMotionEl = new XElement(P + "animMotion",
            new XAttribute("origin", anim.Motion!.Origin),
            new XAttribute("path", pathStr),
            anim.Motion.PtsTypes is not null
                ? new XAttribute("ptsTypes", anim.Motion.PtsTypes)
                : null,
            new XElement(P + "cBhvr",
                new XElement(P + "cTn",
                    new XAttribute("id", nodeId++),
                    new XAttribute("dur", anim.DurationMs),
                    new XElement(P + "stCondLst",
                        new XElement(P + "cond", new XAttribute("delay", "0")))),
                new XElement(P + "tgtEl",
                    new XElement(P + "spTgt", new XAttribute("spid", anim.ShapeId)))));

        return new XElement(P + "par",
            new XElement(P + "cTn",
                new XAttribute("id", nodeId++),
                new XAttribute("presetClass", "path"),
                new XAttribute("presetID", "1"),
                new XAttribute("presetSubtype", "0"),
                new XAttribute("fill", "hold"),
                new XAttribute("grpId", "0"),
                new XAttribute("nodeType", "withEffect"),
                RepeatAttributes(anim),
                AutoReverseAttribute(anim),
                new XElement(P + "stCondLst",
                    new XElement(P + "cond", new XAttribute("delay", delayStr))),
                new XElement(P + "childTnLst",
                    new XElement(P + "par",
                        new XElement(P + "cTn",
                            new XAttribute("id", nodeId++),
                            new XAttribute("fill", "hold"),
                            new XElement(P + "stCondLst",
                                new XElement(P + "cond", new XAttribute("delay", "0"))),
                            new XElement(P + "childTnLst", animMotionEl))))));
    }

    /// <summary>
    /// Serializes a <see cref="MotionPath"/> back to the OOXML path mini-language string.
    /// </summary>
    private static string BuildMotionPathString(MotionPath mp)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var seg in mp.Segments)
        {
            switch (seg.Kind)
            {
                case MotionPathSegmentKind.Move:
                    sb.Append(System.FormattableString.Invariant($"M {seg.X:F6} {seg.Y:F6} "));
                    break;
                case MotionPathSegmentKind.Line:
                    sb.Append(System.FormattableString.Invariant($"L {seg.X:F6} {seg.Y:F6} "));
                    break;
                case MotionPathSegmentKind.Cubic:
                    sb.Append(System.FormattableString.Invariant(
                        $"C {seg.X1:F6} {seg.Y1:F6} {seg.X2:F6} {seg.Y2:F6} {seg.X:F6} {seg.Y:F6} "));
                    break;
                case MotionPathSegmentKind.Close:
                    sb.Append("E ");
                    break;
            }
        }
        return sb.ToString().TrimEnd();
    }

    private static void AddRepeatAttributes(List<object> attributes, ShapeAnimation animation)
    {
        if (animation.RepeatIndefinitely)
            attributes.Add(new XAttribute("repeatCount", "indefinite"));
        else if (animation.RepeatCount is > 1)
            attributes.Add(new XAttribute("repeatCount", animation.RepeatCount.Value));

        if (animation.AutoReverse)
            attributes.Add(new XAttribute("autoRev", "1"));
    }

    private static XAttribute? RepeatAttributes(ShapeAnimation animation)
        => animation.RepeatIndefinitely
            ? new XAttribute("repeatCount", "indefinite")
            : animation.RepeatCount is > 1
                ? new XAttribute("repeatCount", animation.RepeatCount.Value)
                : null;

    private static XAttribute? AutoReverseAttribute(ShapeAnimation animation)
        => animation.AutoReverse ? new XAttribute("autoRev", "1") : null;

    // ── slideLayout.xml ──────────────────────────────────────────────────────────

    private static XDocument BuildSlideLayoutXml(SlideLayout layout, PresentationColorScheme scheme) =>
        new XDocument(
            new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(P + "sldLayout",
                NsAttr("p", P), NsAttr("a", A), NsAttr("r", R),
                new XAttribute("type", ToLayoutTypeStr(layout.LayoutType)),
                new XAttribute("preserve", "1"),
                BuildLayoutCSlotEl(layout, scheme),
                new XElement(P + "clrMapOvr",
                    new XElement(A + "masterClrMapping"))));

    private static XElement BuildLayoutCSlotEl(SlideLayout layout, PresentationColorScheme scheme)
    {
        XElement? bgEl = layout.Background is not null
            ? new XElement(P + "bg",
                new XElement(P + "bgPr",
                    BuildFillEl(layout.Background, scheme),
                    new XElement(A + "effectLst")))
            : null;

        var spTree = new XElement(P + "spTree",
            GrpSpHeader(),
            layout.Placeholders.Select(s => BuildShapeEl(s, scheme, new())).OfType<XElement>());

        return layout.Name is { Length: > 0 }
            ? new XElement(P + "cSld", new XAttribute("name", layout.Name), bgEl, spTree)
            : new XElement(P + "cSld", bgEl, spTree);
    }

    // ── slideMaster.xml ──────────────────────────────────────────────────────────

    private static XDocument BuildSlideMasterXml(
        SlideMaster master, PresentationColorScheme scheme,
        List<(string relId, string layoutPath)> layoutRelIds) =>
        new XDocument(
            new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(P + "sldMaster",
                NsAttr("p", P), NsAttr("a", A), NsAttr("r", R),
                new XElement(P + "cSld",
                    master.Background is not null
                        ? new XElement(P + "bg",
                            new XElement(P + "bgPr",
                                BuildFillEl(master.Background, scheme),
                                new XElement(A + "effectLst")))
                        : null,
                    new XElement(P + "spTree",
                        GrpSpHeader(),
                        master.Placeholders.Select(s => BuildShapeEl(s, scheme, new())).OfType<XElement>())),
                BuildColorMapEl(master.ColorMap),
                new XElement(P + "sldLayoutIdLst",
                    layoutRelIds.Select((lr, i) =>
                        new XElement(P + "sldLayoutId",
                            new XAttribute("id", 2147483649u + (uint)i),
                            new XAttribute(R + "id", lr.relId)))),
                master.TextStyles is not null ? BuildTxStylesEl(master.TextStyles) : null));

    private static XElement BuildColorMapEl(Dictionary<string, string>? colorMap)
    {
        // Use the stored color map if present; otherwise emit the default Office mapping.
        var map = colorMap ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["bg1"] = "lt1", ["tx1"] = "dk1", ["bg2"] = "lt2", ["tx2"] = "dk2",
            ["accent1"] = "accent1", ["accent2"] = "accent2", ["accent3"] = "accent3",
            ["accent4"] = "accent4", ["accent5"] = "accent5", ["accent6"] = "accent6",
            ["hlink"] = "hlink", ["folHlink"] = "folHlink"
        };

        var el = new XElement(P + "clrMap");
        foreach (var (key, val) in map)
            el.Add(new XAttribute(key, val));
        return el;
    }

    // ── p:txStyles builder ────────────────────────────────────────────────────────

    private static XElement BuildTxStylesEl(MasterTextStyles txStyles)
    {
        return new XElement(P + "txStyles",
            BuildTextStyleEl(P + "titleStyle", txStyles.TitleStyle),
            BuildTextStyleEl(P + "bodyStyle",  txStyles.BodyStyle),
            BuildTextStyleEl(P + "otherStyle", txStyles.OtherStyle));
    }

    private static XElement? BuildTextStyleEl(XName elementName, TextStyleLevels levels)
    {
        if (!levels.HasAny) return null;
        var el = new XElement(elementName);
        for (int i = 0; i < 9; i++)
        {
            var level = levels[i];
            if (level is null) continue;
            el.Add(BuildLvlpPrEl($"lvl{i + 1}pPr", level));
        }
        return el;
    }

    private static XElement BuildLvlpPrEl(
        string localName,
        TextStyleLevel level,
        bool? rightToLeftOverride = null)
    {
        var el = new XElement(A + localName);

        if (level.Align.HasValue)
            el.Add(new XAttribute("algn", level.Align.Value switch
            {
                TextAlign.Center => "ctr",
                TextAlign.Right => "r",
                TextAlign.Justify => "just",
                TextAlign.Distributed => "dist",
                _ => "l"
            }));
        var rightToLeft = level.RightToLeft ?? rightToLeftOverride;
        if (rightToLeft.HasValue)
            el.Add(new XAttribute("rtl", rightToLeft.Value ? "1" : "0"));
        if (level.MarginLeftEmu.HasValue) el.Add(new XAttribute("marL", level.MarginLeftEmu.Value));
        if (level.IndentEmu.HasValue)     el.Add(new XAttribute("indent", level.IndentEmu.Value));

        AddBulletTypographyProperties(
            el,
            level.BulletColor,
            level.BulletColorFollowsText,
            level.BulletSizePct,
            level.BulletSizePt,
            level.BulletSizeFollowsText,
            level.BulletFontFamily,
            level.BulletFontFollowsText);

        // Bullet (Wave 19A: extended round-trip)
        switch (level.BulletKind)
        {
            case BulletKind.None:
                el.Add(new XElement(A + "buNone")); break;
            case BulletKind.Char:
                el.Add(new XElement(A + "buChar", new XAttribute("char", level.BulletChar ?? "•"))); break;
            case BulletKind.Auto:
                var lvlAutoNumTypeStr = level.AutoNumType switch
                {
                    AutoNumType.ArabicParenR    => "arabicParenR",
                    AutoNumType.ArabicParenBoth => "arabicParenBoth",
                    AutoNumType.RomanUcPeriod   => "romanUcPeriod",
                    AutoNumType.RomanLcPeriod   => "romanLcPeriod",
                    AutoNumType.RomanUcParenR   => "romanUcParenR",
                    AutoNumType.RomanLcParenR   => "romanLcParenR",
                    AutoNumType.AlphaUcPeriod   => "alphaUcPeriod",
                    AutoNumType.AlphaLcPeriod   => "alphaLcPeriod",
                    AutoNumType.AlphaUcParenR   => "alphaUcParenR",
                    AutoNumType.AlphaLcParenR   => "alphaLcParenR",
                    AutoNumType.AlphaUcParenBoth => "alphaUcParenBoth",
                    AutoNumType.AlphaLcParenBoth => "alphaLcParenBoth",
                    _                           => "arabicPeriod"
                };
                el.Add(new XElement(A + "buAutoNum", new XAttribute("type", lvlAutoNumTypeStr))); break;
        }

        // a:defRPr
        bool hasRPr = level.FontSizePt.HasValue || level.Bold.HasValue || level.Italic.HasValue
                   || level.Color is not null || level.LatinFont is not null;
        if (hasRPr)
        {
            var defRPr = new XElement(A + "defRPr");
            if (level.FontSizePt.HasValue)
                defRPr.Add(new XAttribute("sz", (int)Math.Round(level.FontSizePt.Value * 100)));
            if (level.Bold.HasValue)
                defRPr.Add(new XAttribute("b", level.Bold.Value ? "1" : "0"));
            if (level.Italic.HasValue)
                defRPr.Add(new XAttribute("i", level.Italic.Value ? "1" : "0"));
            if (level.Color is not null)
                defRPr.Add(new XElement(A + "solidFill", BuildColorEl(level.Color)));
            if (level.LatinFont is not null)
                defRPr.Add(new XElement(A + "latin", new XAttribute("typeface", level.LatinFont)));
            el.Add(defRPr);
        }

        return el;
    }

    // ── theme.xml ────────────────────────────────────────────────────────────────

    private static bool AddBulletTypographyProperties(
        XElement parent,
        ThemeAwareColor? color,
        bool colorFollowsText,
        int? sizePct,
        double? sizePt,
        bool sizeFollowsText,
        string? fontFamily,
        bool fontFollowsText)
    {
        var wrote = false;

        if (colorFollowsText)
        {
            parent.Add(new XElement(A + "buClrTx"));
            wrote = true;
        }
        else if (color is not null)
        {
            parent.Add(new XElement(A + "buClr", BuildColorEl(color)));
            wrote = true;
        }

        if (sizeFollowsText)
        {
            parent.Add(new XElement(A + "buSzTx"));
            wrote = true;
        }
        else if (sizePt.HasValue && sizePt.Value > 0)
        {
            parent.Add(new XElement(A + "buSzPts", new XAttribute("val", (int)Math.Round(sizePt.Value * 100))));
            wrote = true;
        }
        else if (sizePct.HasValue)
        {
            parent.Add(new XElement(A + "buSzPct", new XAttribute("val", sizePct.Value)));
            wrote = true;
        }

        if (fontFollowsText)
        {
            parent.Add(new XElement(A + "buFontTx"));
            wrote = true;
        }
        else if (!string.IsNullOrEmpty(fontFamily))
        {
            parent.Add(new XElement(A + "buFont", new XAttribute("typeface", fontFamily)));
            wrote = true;
        }

        return wrote;
    }

    private static XDocument BuildThemeXml(PresentationTheme theme)
    {
        var cs = theme.ColorScheme;
        return new XDocument(
            new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(A + "theme", NsAttr("a", A), new XAttribute("name", theme.Name),
                new XElement(A + "themeElements",
                    new XElement(A + "clrScheme", new XAttribute("name", theme.Name),
                        ColorSlot("dk1", cs[ThemeColorSlot.Dk1]),
                        ColorSlot("lt1", cs[ThemeColorSlot.Lt1]),
                        ColorSlot("dk2", cs[ThemeColorSlot.Dk2]),
                        ColorSlot("lt2", cs[ThemeColorSlot.Lt2]),
                        ColorSlot("accent1", cs[ThemeColorSlot.Accent1]),
                        ColorSlot("accent2", cs[ThemeColorSlot.Accent2]),
                        ColorSlot("accent3", cs[ThemeColorSlot.Accent3]),
                        ColorSlot("accent4", cs[ThemeColorSlot.Accent4]),
                        ColorSlot("accent5", cs[ThemeColorSlot.Accent5]),
                        ColorSlot("accent6", cs[ThemeColorSlot.Accent6]),
                        ColorSlot("hlink", cs[ThemeColorSlot.HLink]),
                        ColorSlot("folHlink", cs[ThemeColorSlot.FolHLink])),
                    new XElement(A + "fontScheme", new XAttribute("name", theme.Name),
                        new XElement(A + "majorFont",
                            new XElement(A + "latin", new XAttribute("typeface", theme.FontScheme.MajorLatinFont)),
                            new XElement(A + "ea", new XAttribute("typeface", string.Empty)),
                            new XElement(A + "cs", new XAttribute("typeface", string.Empty))),
                        new XElement(A + "minorFont",
                            new XElement(A + "latin", new XAttribute("typeface", theme.FontScheme.MinorLatinFont)),
                            new XElement(A + "ea", new XAttribute("typeface", string.Empty)),
                            new XElement(A + "cs", new XAttribute("typeface", string.Empty)))),
                    new XElement(A + "fmtScheme", new XAttribute("name", "Office"),
                        new XElement(A + "fillStyleLst",
                            SolidPhClr(), SolidPhClr(), SolidPhClr()),
                        new XElement(A + "lnStyleLst",
                            LnStyle(0.5), LnStyle(1.0), LnStyle(1.5)),
                        new XElement(A + "effectStyleLst",
                            EffectStyle(), EffectStyle(), EffectStyle()),
                        new XElement(A + "bgFillStyleLst",
                            SolidPhClr(), SolidPhClr(), SolidPhClr())))));
    }

    private static XElement SolidPhClr() =>
        new XElement(A + "solidFill", new XElement(A + "schemeClr", new XAttribute("val", "phClr")));

    private static XElement LnStyle(double widthPt) =>
        new XElement(A + "ln", new XAttribute("w", DrawingMlUnits.PointsToEmu(widthPt)),
            new XElement(A + "solidFill", new XElement(A + "schemeClr", new XAttribute("val", "phClr"))),
            new XElement(A + "prstDash", new XAttribute("val", "solid")));

    private static XElement EffectStyle() =>
        new XElement(A + "effectStyle", new XElement(A + "effectLst"));

    private static XElement ColorSlot(string name, SrgbColor color) =>
        new XElement(A + name,
            new XElement(A + "srgbClr", new XAttribute("val", FmtColor(color))));

    // ── Stub XML parts ────────────────────────────────────────────────────────────

    private static XDocument BuildPresPropsXml(PptxPackageSnapshot? packageSnapshot) =>
        TryReadPreservedXmlPart(packageSnapshot, "ppt/presProps.xml", P + "presentationPr", out var preserved)
            ? preserved
            : new XDocument(
                new XDeclaration("1.0", "UTF-8", "yes"),
                new XElement(P + "presentationPr", NsAttr("p", P), NsAttr("a", A)));

    private static XDocument BuildViewPropsXml(PptxPackageSnapshot? packageSnapshot) =>
        TryReadPreservedXmlPart(packageSnapshot, "ppt/viewProps.xml", P + "viewPr", out var preserved)
            ? preserved
            : new XDocument(
                new XDeclaration("1.0", "UTF-8", "yes"),
                new XElement(P + "viewPr", NsAttr("p", P)));

    private static XDocument BuildTableStylesXml() =>
        new XDocument(
            new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(A + "tblStyleLst", NsAttr("a", A),
                new XAttribute("def", "{5C22544A-7EE6-4342-B048-85BDC9FD1C3A}")));

    // ── Core properties ───────────────────────────────────────────────────────────

    // ── Shape elements ────────────────────────────────────────────────────────────

    private static XElement? BuildShapeEl(
        SlideShape shape, PresentationColorScheme scheme, Dictionary<uint, string> mediaById,
        Dictionary<uint, Dictionary<string, string>>? smartArtRelIdRemap = null,
        Dictionary<string, string>? hlinkRelIds = null,
        List<Slide>? allSlides = null,
        Dictionary<uint, string>? fillBlipById = null,
        Dictionary<(uint shapeId, string oldRelId), string>? prvRelIdByShapeAndOldId = null,
        IReadOnlyDictionary<uint, IReadOnlyList<MediaCaptionTrackRelationship>>? captionTracksByShape = null,
        Dictionary<Paragraph, string>? bulletImageRelIds = null) =>
        shape.Kind switch
        {
            SlideShapeKind.Picture => BuildPicEl(shape, mediaById, hlinkRelIds, allSlides),
            SlideShapeKind.Media   => BuildMediaPicEl(shape, mediaById, captionTracksByShape, hlinkRelIds, allSlides),
            SlideShapeKind.Group => BuildGrpSpEl(shape, scheme, mediaById, smartArtRelIdRemap, hlinkRelIds, allSlides, fillBlipById, prvRelIdByShapeAndOldId, captionTracksByShape, bulletImageRelIds),
            SlideShapeKind.Connector => BuildCxnSpEl(shape, scheme, hlinkRelIds, fillBlipById),
            SlideShapeKind.Table when shape.Table is not null => BuildGraphicFrameEl(shape, scheme, hlinkRelIds, allSlides, bulletImageRelIds),
            SlideShapeKind.Chart when shape.Chart is not null => BuildChartGraphicFrameEl(shape, mediaById, hlinkRelIds, allSlides),
            SlideShapeKind.SmartArt when shape.SmartArt is not null =>
                BuildSmartArtGraphicFrameEl(shape,
                    smartArtRelIdRemap?.GetValueOrDefault(shape.Id), hlinkRelIds, allSlides),
            // Theme 21: OLE embedded objects — emit the verbatim graphicFrame wrapper
            SlideShapeKind.Ole when shape.OleObject is not null =>
                BuildOleGraphicFrameEl(shape, mediaById, hlinkRelIds, allSlides),
            // Wave 25A: preserved modern objects — emit verbatim XML with patched rel IDs
            SlideShapeKind.Zoom or SlideShapeKind.Ink or SlideShapeKind.Model3d
                or SlideShapeKind.PreservedObject when shape.PreservedObject is not null =>
                    BuildPreservedObjectEl(shape, prvRelIdByShapeAndOldId, hlinkRelIds, allSlides),
            _ => BuildSpEl(shape, scheme, hlinkRelIds, allSlides, fillBlipById, bulletImageRelIds)
        };

    private static XElement BuildSpEl(SlideShape shape, PresentationColorScheme scheme,
        Dictionary<string, string>? hlinkRelIds = null,
        List<Slide>? allSlides = null,
        Dictionary<uint, string>? fillBlipById = null,
        Dictionary<Paragraph, string>? bulletImageRelIds = null)
    {
        string? fillBlipRelId = null;
        fillBlipById?.TryGetValue(shape.Id, out fillBlipRelId);
        return new XElement(P + "sp",
            new XElement(P + "nvSpPr",
                CnvPrWithHlink(shape, hlinkRelIds, allSlides),
                new XElement(P + "cNvSpPr"),
                new XElement(P + "nvPr",
                    shape.Placeholder is not null ? BuildPhEl(shape.Placeholder) : null)),
            BuildSpPrEl(shape, scheme, fillBlipRelId: fillBlipRelId),
            shape.TextBody is not null ? BuildTxBodyEl(shape.TextBody, scheme, hlinkRelIds, allSlides, bulletImageRelIds) : null);
    }

    private static XElement BuildCxnSpEl(SlideShape shape, PresentationColorScheme scheme,
        Dictionary<string, string>? hlinkRelIds = null,
        Dictionary<uint, string>? fillBlipById = null)
    {
        string? fillBlipRelId = null;
        fillBlipById?.TryGetValue(shape.Id, out fillBlipRelId);

        // Build cNvCxnSpPr — add stCxn/endCxn when the connector is attached.
        var cNvCxnSpPrEl = new XElement(P + "cNvCxnSpPr");
        if (shape.ConnectionStart is { } cs)
            cNvCxnSpPrEl.Add(new XElement(A + "stCxn",
                new XAttribute("id",  cs.ShapeId),
                new XAttribute("idx", cs.SiteIndex)));
        if (shape.ConnectionEnd is { } ce)
            cNvCxnSpPrEl.Add(new XElement(A + "endCxn",
                new XAttribute("id",  ce.ShapeId),
                new XAttribute("idx", ce.SiteIndex)));

        return new XElement(P + "cxnSp",
            new XElement(P + "nvCxnSpPr",
                CnvPrWithHlink(shape, hlinkRelIds, null),
                cNvCxnSpPrEl,
                new XElement(P + "nvPr")),
            BuildSpPrEl(shape, scheme, fillBlipRelId: fillBlipRelId));
    }

    private static XElement BuildPicEl(SlideShape shape, Dictionary<uint, string> mediaById,
        Dictionary<string, string>? hlinkRelIds = null, List<Slide>? allSlides = null)
    {
        // Look up by shape Id (collision-safe); fall back to a placeholder only if somehow missing.
        mediaById.TryGetValue(shape.Id, out var embedRelId);
        embedRelId ??= "rIdMedia1";

        // 18A: build a:blip with optional colour effects
        var blipEl = BuildBlipEl(embedRelId, shape.PictureFormat);

        // 18A: build a:blipFill with optional a:srcRect (crop)
        var blipFillEl = BuildBlipFillEl(blipEl, shape.PictureFormat);

        // Wave 26: use the stored frame geometry preset (roundRect, ellipse, etc.) if set;
        // otherwise default to "rect" as before.
        var framePrst = !string.IsNullOrEmpty(shape.PictureFrameGeometry)
            ? shape.PictureFrameGeometry
            : "rect";

        return new XElement(P + "pic",
            new XElement(P + "nvPicPr",
                CnvPrWithHlink(shape, hlinkRelIds, allSlides),
                new XElement(P + "cNvPicPr"),
                new XElement(P + "nvPr")),
            blipFillEl,
            BuildSpPrEl(shape, PresentationColorScheme.CreateDefault(), forcePrst: framePrst));
    }

    /// <summary>
    /// 18A: Builds <c>a:blip</c> with the embed rel id and any colour-effect child elements
    /// derived from <paramref name="fmt"/>.
    /// </summary>
    private static XElement BuildBlipEl(string embedRelId, PictureFormat? fmt)
    {
        var blip = new XElement(A + "blip", new XAttribute(R + "embed", embedRelId));
        if (fmt is null) return blip;

        // Colour effects — order matches OOXML schema sequence
        if (fmt.Grayscale)
            blip.Add(new XElement(A + "grayscl"));

        if (fmt.BiLevelThreshold.HasValue)
            blip.Add(new XElement(A + "biLevel",
                new XAttribute("thresh", FormatPercentFraction(fmt.BiLevelThreshold.Value))));

        if (fmt.Brightness.HasValue || fmt.Contrast.HasValue)
        {
            var lum = new XElement(A + "lum");
            if (fmt.Brightness.HasValue)
                lum.Add(new XAttribute("bright", FormatSignedPercentFraction(fmt.Brightness.Value)));
            if (fmt.Contrast.HasValue)
                lum.Add(new XAttribute("contrast", FormatSignedPercentFraction(fmt.Contrast.Value)));
            blip.Add(lum);
        }

        if (fmt.AlphaModPct.HasValue)
            blip.Add(new XElement(A + "alphaModFix",
                new XAttribute("amt", FormatPercentFraction(fmt.AlphaModPct.Value))));

        return blip;
    }

    /// <summary>
    /// 18A: Builds <c>p:blipFill</c> containing the blip element, an optional <c>a:srcRect</c>
    /// for crop, and a standard <c>a:stretch/a:fillRect</c>.
    /// </summary>
    private static XElement BuildBlipFillEl(XElement blipEl, PictureFormat? fmt)
    {
        var blipFill = new XElement(P + "blipFill", blipEl);

        if (fmt is { HasCrop: true })
        {
            blipFill.Add(new XElement(A + "srcRect",
                new XAttribute("l", FormatPercentFraction(fmt.CropLeft)),
                new XAttribute("t", FormatPercentFraction(fmt.CropTop)),
                new XAttribute("r", FormatPercentFraction(fmt.CropRight)),
                new XAttribute("b", FormatPercentFraction(fmt.CropBottom))));
        }

        blipFill.Add(new XElement(A + "stretch", new XElement(A + "fillRect")));
        return blipFill;
    }

    // Converts a 0..1 fraction to OOXML 1/1000-of-a-percent integer (e.g. 0.125 → 12500).
    private static string FormatPercentFraction(double v) =>
        ((long)Math.Round(v * 100_000.0)).ToString(CultureInfo.InvariantCulture);

    // Converts a -1..1 fraction to OOXML 1/1000-of-a-percent signed integer (e.g. -0.1 → -10000).
    private static string FormatSignedPercentFraction(double v) =>
        ((long)Math.Round(v * 100_000.0)).ToString(CultureInfo.InvariantCulture);

    private static XElement BuildMediaPicEl(
        SlideShape shape,
        Dictionary<uint, string> mediaById,
        IReadOnlyDictionary<uint, IReadOnlyList<MediaCaptionTrackRelationship>>? captionTracksByShape = null,
        Dictionary<string, string>? hlinkRelIds = null,
        List<Slide>? allSlides = null)
    {
        // Poster image rel id (written by WriteSlideMedia with the shape's Id key)
        mediaById.TryGetValue(shape.Id, out var posterRelId);
        // II4: do NOT fall back to a hard-coded "rIdMedia1" — that would emit a dangling
        // r:embed reference when no poster was written for this shape, causing repair.
        // If no real poster rel exists, omit the blipFill entirely.

        // Media file rel id (written by WriteSlideMediaFiles using shape.Id | 0x80000000 key)
        mediaById.TryGetValue(shape.Id | 0x80000000u, out var mediaFileRelId);
        mediaFileRelId ??= "rIdVid1";

        bool isVideo = shape.Media?.IsVideo ?? true;
        var mediaFileEl = isVideo
            ? new XElement(A + "videoFile", new XAttribute(R + "link", mediaFileRelId))
            : new XElement(A + "audioFile", new XAttribute(R + "link", mediaFileRelId));

        // KK1: a:blipFill is REQUIRED by CT_Picture (minOccurs=1). When no poster image
        // is available emit a minimal VALID blipFill — just a:stretch/a:fillRect, no a:blip —
        // so there is no dangling r:embed relationship and the element is schema-compliant.
        // (CT_BlipFillProperties: a:blip is optional; a:stretch is valid without it.)
        // When a real poster rel exists, emit the blip with r:embed as before (II4).
        XElement blipFillEl = posterRelId is not null
            ? new XElement(P + "blipFill",
                new XElement(A + "blip", new XAttribute(R + "embed", posterRelId)),
                new XElement(A + "stretch", new XElement(A + "fillRect")))
            : new XElement(P + "blipFill",
                new XElement(A + "stretch", new XElement(A + "fillRect")));

        var nvPrChildren = new List<object> { mediaFileEl };
        if (captionTracksByShape is not null
            && captionTracksByShape.TryGetValue(shape.Id, out var captionTracks)
            && captionTracks.Count > 0)
        {
            nvPrChildren.Add(BuildMediaCaptionExtList(captionTracks));
        }

        return new XElement(P + "pic",
            new XElement(P + "nvPicPr",
                CnvPrWithHlink(shape, hlinkRelIds, allSlides),
                new XElement(P + "cNvPicPr"),
                new XElement(P + "nvPr", nvPrChildren)),
            blipFillEl,
            BuildSpPrEl(shape, PresentationColorScheme.CreateDefault(), forcePrst: "rect"));
    }

    private static XElement BuildMediaCaptionExtList(IReadOnlyList<MediaCaptionTrackRelationship> captionTracks)
    {
        var captionElements = captionTracks.Select(track =>
        {
            var attributes = new List<object>
            {
                new XAttribute(track.IsExternal ? R + "link" : R + "embed", track.RelationshipId)
            };

            if (!string.IsNullOrWhiteSpace(track.Language))
                attributes.Add(new XAttribute("lang", track.Language));
            if (!string.IsNullOrWhiteSpace(track.Label))
                attributes.Add(new XAttribute("label", track.Label));

            return new XElement(P20Media + "caption",
                NsAttr("p20media", P20Media),
                attributes);
        });

        return new XElement(P + "extLst",
            new XElement(P + "ext",
                new XAttribute("uri", "{DAA4B4D4-6D71-4841-9C94-3DE7FCFBFE68}"),
                captionElements));
    }

    private static XElement BuildGrpSpEl(
        SlideShape shape, PresentationColorScheme scheme, Dictionary<uint, string> mediaById,
        Dictionary<uint, Dictionary<string, string>>? smartArtRelIdRemap = null,
        Dictionary<string, string>? hlinkRelIds = null,
        List<Slide>? allSlides = null,
        Dictionary<uint, string>? fillBlipById = null,
        Dictionary<(uint shapeId, string oldRelId), string>? prvRelIdByShapeAndOldId = null,
        IReadOnlyDictionary<uint, IReadOnlyList<MediaCaptionTrackRelationship>>? captionTracksByShape = null,
        Dictionary<Paragraph, string>? bulletImageRelIds = null) =>
        new XElement(P + "grpSp",
            new XElement(P + "nvGrpSpPr",
                CnvPrWithHlink(shape, hlinkRelIds, allSlides),
                new XElement(P + "cNvGrpSpPr"),
                new XElement(P + "nvPr")),
            BuildGrpSpPrEl(shape),
            shape.Children
                .Select(c => BuildShapeEl(c, scheme, mediaById, smartArtRelIdRemap, hlinkRelIds, allSlides, fillBlipById, prvRelIdByShapeAndOldId, captionTracksByShape, bulletImageRelIds))
                .OfType<XElement>());

    /// <summary>
    /// Builds the <c>&lt;p:grpSpPr&gt;</c> required for <c>&lt;p:grpSp&gt;</c>.
    /// CT_GroupShapeProperties requires an a:xfrm with chOff/chExt and must NOT contain a prstGeom.
    ///
    /// FF1 fix: FreeP stores group children with ABSOLUTE slide offsets (the compositor and reader
    /// treat child coords as absolute with no group transform applied).  PowerPoint maps a child's
    /// rendered position as: groupOff + (childOff - chOff) * (ext / chExt).
    /// To make that identity for absolute coords we must emit chOff == off and chExt == ext, so:
    ///   rendered = groupOff + (childAbsOff - groupOff) * 1 = childAbsOff  ✓
    /// The old chOff=(0,0) was wrong: it displaced every child by the group origin in PowerPoint.
    ///
    /// FF3 fix: clamp ext/chExt cx and cy to a minimum of 1 EMU to prevent PowerPoint from
    /// dividing by zero when a degenerate (zero-size) group is encountered.
    /// </summary>
    private static XElement BuildGrpSpPrEl(SlideShape shape)
    {
        var xfrm = new XElement(A + "xfrm");
        if (shape.RotationDeg != 0)
            xfrm.Add(new XAttribute("rot", (long)Math.Round(shape.RotationDeg * 60000)));
        if (shape.FlipH) xfrm.Add(new XAttribute("flipH", "1"));
        if (shape.FlipV) xfrm.Add(new XAttribute("flipV", "1"));

        // FF3: clamp to ≥1 EMU so PowerPoint never divides by chExt=0.
        long extCx = Math.Max(1L, shape.ExtentCxEmu);
        long extCy = Math.Max(1L, shape.ExtentCyEmu);

        xfrm.Add(new XElement(A + "off",   new XAttribute("x",  shape.OffsetXEmu),  new XAttribute("y",  shape.OffsetYEmu)));
        xfrm.Add(new XElement(A + "ext",   new XAttribute("cx", extCx),             new XAttribute("cy", extCy)));
        // FF1: chOff == off so the group→child transform is identity for absolute child coords.
        xfrm.Add(new XElement(A + "chOff", new XAttribute("x",  shape.OffsetXEmu),  new XAttribute("y",  shape.OffsetYEmu)));
        xfrm.Add(new XElement(A + "chExt", new XAttribute("cx", extCx),             new XAttribute("cy", extCy)));

        return new XElement(P + "grpSpPr", xfrm);
    }

    private static XElement BuildSpPrEl(
        SlideShape shape,
        PresentationColorScheme scheme,
        string? forcePrst = null,
        string? fillBlipRelId = null)
    {
        var xfrm = new XElement(A + "xfrm");
        if (shape.RotationDeg != 0)
            xfrm.Add(new XAttribute("rot", (long)Math.Round(shape.RotationDeg * 60000)));
        if (shape.FlipH) xfrm.Add(new XAttribute("flipH", "1"));
        if (shape.FlipV) xfrm.Add(new XAttribute("flipV", "1"));
        xfrm.Add(new XElement(A + "off", new XAttribute("x", shape.OffsetXEmu), new XAttribute("y", shape.OffsetYEmu)));
        xfrm.Add(new XElement(A + "ext", new XAttribute("cx", shape.ExtentCxEmu), new XAttribute("cy", shape.ExtentCyEmu)));

        // Geometry: custom or preset
        XElement geomEl;
        if (forcePrst is null &&
            (shape.CustomGeometry.Count > 0 || shape.CustomConnectionSites.Count > 0))
            geomEl = BuildCustGeomEl(shape.CustomGeometry, shape.CustomConnectionSites);
        else
            geomEl = new XElement(A + "prstGeom",
                new XAttribute("prst", forcePrst ?? PptxShapeKindMap.ToPreset(shape.AutoShapeKind)),
                BuildPresetGeometryAdjustmentsEl(shape.PresetGeometryAdjustments));

        return new XElement(P + "spPr",
            xfrm,
            geomEl,
            shape.Fill is not null ? BuildFillEl(shape.Fill, scheme, fillBlipRelId) : null,
            shape.Outline is not null ? BuildOutlineEl(shape.Outline, includeLineEnds: ShouldWriteLineEnds(shape)) : null,
            shape.Effects is not null ? BuildEffectLstEl(shape.Effects) : null,
            shape.Effects is not null ? BuildScene3dEl(shape.Effects) : null,
            shape.Effects is not null ? BuildSp3dEl(shape.Effects) : null);
    }

    private static XElement BuildPresetGeometryAdjustmentsEl(IReadOnlyDictionary<string, double> adjustments)
    {
        var avLst = new XElement(A + "avLst");
        foreach (var pair in adjustments)
        {
            avLst.Add(new XElement(A + "gd",
                new XAttribute("name", pair.Key),
                new XAttribute("fmla", $"val {pair.Value.ToString("0.########", CultureInfo.InvariantCulture)}")));
        }

        return avLst;
    }

    private static XElement BuildCustGeomEl(
        List<CustomGeometryPath> paths,
        List<CustomGeometryConnectionSite> connectionSites)
    {
        var pathEls = new List<XElement>();
        foreach (var path in paths)
        {
            var pathEl = new XElement(A + "path");
            if (path.PathW > 0) pathEl.Add(new XAttribute("w", path.PathW));
            if (path.PathH > 0) pathEl.Add(new XAttribute("h", path.PathH));
            if (!path.Fill)   pathEl.Add(new XAttribute("fill", "none"));
            if (!path.Stroke) pathEl.Add(new XAttribute("stroke", "0"));

            foreach (var seg in path.Segments)
            {
                switch (seg.Kind)
                {
                    case CustomSegmentKind.MoveTo:
                        pathEl.Add(new XElement(A + "moveTo",
                            new XElement(A + "pt", new XAttribute("x", (long)seg.X), new XAttribute("y", (long)seg.Y))));
                        break;
                    case CustomSegmentKind.LineTo:
                        pathEl.Add(new XElement(A + "lnTo",
                            new XElement(A + "pt", new XAttribute("x", (long)seg.X), new XAttribute("y", (long)seg.Y))));
                        break;
                    case CustomSegmentKind.CubicBezTo:
                        pathEl.Add(new XElement(A + "cubicBezTo",
                            new XElement(A + "pt", new XAttribute("x", (long)seg.X),  new XAttribute("y", (long)seg.Y)),
                            new XElement(A + "pt", new XAttribute("x", (long)seg.X1), new XAttribute("y", (long)seg.Y1)),
                            new XElement(A + "pt", new XAttribute("x", (long)seg.X2), new XAttribute("y", (long)seg.Y2))));
                        break;
                    case CustomSegmentKind.QuadBezTo:
                        pathEl.Add(new XElement(A + "quadBezTo",
                            new XElement(A + "pt", new XAttribute("x", (long)seg.X),  new XAttribute("y", (long)seg.Y)),
                            new XElement(A + "pt", new XAttribute("x", (long)seg.X1), new XAttribute("y", (long)seg.Y1))));
                        break;
                    case CustomSegmentKind.ArcTo:
                        pathEl.Add(new XElement(A + "arcTo",
                            new XAttribute("wR",    (long)seg.WR),
                            new XAttribute("hR",    (long)seg.HR),
                            new XAttribute("stAng", (long)Math.Round(seg.StAng * 60000)),
                            new XAttribute("swAng", (long)Math.Round(seg.SwAng * 60000))));
                        break;
                    case CustomSegmentKind.Close:
                        pathEl.Add(new XElement(A + "close"));
                        break;
                }
            }
            pathEls.Add(pathEl);
        }

        var cxnElements = connectionSites.Select(site =>
        {
            var cxn = new XElement(A + "cxn",
                new XElement(A + "pos",
                    new XAttribute("x", site.X),
                    new XAttribute("y", site.Y)));
            if (!string.IsNullOrWhiteSpace(site.Angle))
                cxn.SetAttributeValue("ang", site.Angle);
            return cxn;
        });

        return new XElement(A + "custGeom",
            new XElement(A + "avLst"),
            new XElement(A + "gdLst"),
            new XElement(A + "ahLst"),
            new XElement(A + "cxnLst", cxnElements),
            new XElement(A + "rect",
                new XAttribute("l", "0"), new XAttribute("t", "0"),
                new XAttribute("r", "r"), new XAttribute("b", "b")),
            new XElement(A + "pathLst", pathEls));
    }

    private static XElement BuildEffectLstEl(ShapeEffects fx)
    {
        var effectLst = new XElement(A + "effectLst");

        if (fx.HasOuterShadow)
        {
            long alpha100k = fx.OuterShadowAlpha * 100000L / 255;
            effectLst.Add(new XElement(A + "outerShdw",
                new XAttribute("blurRad", fx.OuterShadowBlurRadEmu),
                new XAttribute("dist",    fx.OuterShadowDistEmu),
                new XAttribute("dir",     (long)Math.Round(fx.OuterShadowDirDeg * 60000)),
                new XElement(A + "srgbClr",
                    new XAttribute("val", FmtColor(fx.OuterShadowColor)),
                    new XElement(A + "alpha", new XAttribute("val", alpha100k)))));
        }

        if (fx.HasInnerShadow)
        {
            long alpha100k = fx.InnerShadowAlpha * 100000L / 255;
            effectLst.Add(new XElement(A + "innerShdw",
                new XAttribute("blurRad", fx.InnerShadowBlurRadEmu),
                new XAttribute("dist",    fx.InnerShadowDistEmu),
                new XAttribute("dir",     (long)Math.Round(fx.InnerShadowDirDeg * 60000)),
                new XElement(A + "srgbClr",
                    new XAttribute("val", FmtColor(fx.InnerShadowColor)),
                    new XElement(A + "alpha", new XAttribute("val", alpha100k)))));
        }

        if (fx.HasGlow)
        {
            long alpha100k = fx.GlowAlpha * 100000L / 255;
            effectLst.Add(new XElement(A + "glow",
                new XAttribute("rad", fx.GlowRadiusEmu),
                new XElement(A + "srgbClr",
                    new XAttribute("val", FmtColor(fx.GlowColor)),
                    new XElement(A + "alpha", new XAttribute("val", alpha100k)))));
        }

        if (fx.HasSoftEdge)
            effectLst.Add(new XElement(A + "softEdge", new XAttribute("rad", fx.SoftEdgeRadEmu)));

        return effectLst;
    }

    // ── a:sp3d element ────────────────────────────────────────────────────────

    private static XElement? BuildSp3dEl(ShapeEffects fx)
    {
        bool hasSp3d = fx.BevelTop is not null || fx.BevelBottom is not null
            || fx.ExtrusionHeightEmu != 0 || fx.ContourWidthEmu != 0
            || !string.IsNullOrEmpty(fx.PrstMaterial)
            || fx.ExtrusionColor.HasValue || fx.ContourColor.HasValue;

        if (!hasSp3d) return null;

        var sp3d = new XElement(A + "sp3d");

        if (fx.ExtrusionHeightEmu != 0)
            sp3d.Add(new XAttribute("extrusionH", fx.ExtrusionHeightEmu));
        if (fx.ContourWidthEmu != 0)
            sp3d.Add(new XAttribute("contourW", fx.ContourWidthEmu));
        if (!string.IsNullOrEmpty(fx.PrstMaterial))
            sp3d.Add(new XAttribute("prstMaterial", fx.PrstMaterial));

        if (fx.BevelTop is not null)
        {
            var bevelT = new XElement(A + "bevelT");
            // Always emit w/h so an explicit 0 round-trips correctly (omitting matches the 76200 default).
            bevelT.Add(new XAttribute("w", fx.BevelTop.WidthEmu));
            bevelT.Add(new XAttribute("h", fx.BevelTop.HeightEmu));
            if (!string.IsNullOrEmpty(fx.BevelTop.PresetName))
                bevelT.Add(new XAttribute("prst", fx.BevelTop.PresetName));
            sp3d.Add(bevelT);
        }

        if (fx.BevelBottom is not null)
        {
            var bevelB = new XElement(A + "bevelB");
            // Always emit w/h so an explicit 0 round-trips correctly (omitting matches the 76200 default).
            bevelB.Add(new XAttribute("w", fx.BevelBottom.WidthEmu));
            bevelB.Add(new XAttribute("h", fx.BevelBottom.HeightEmu));
            if (!string.IsNullOrEmpty(fx.BevelBottom.PresetName))
                bevelB.Add(new XAttribute("prst", fx.BevelBottom.PresetName));
            sp3d.Add(bevelB);
        }

        if (fx.ExtrusionColor.HasValue)
            sp3d.Add(new XElement(A + "extrusionClr",
                new XElement(A + "srgbClr", new XAttribute("val", FmtColor(fx.ExtrusionColor.Value)))));

        if (fx.ContourColor.HasValue)
            sp3d.Add(new XElement(A + "contourClr",
                new XElement(A + "srgbClr", new XAttribute("val", FmtColor(fx.ContourColor.Value)))));

        return sp3d;
    }

    // ── a:scene3d element ─────────────────────────────────────────────────────

    private static XElement? BuildScene3dEl(ShapeEffects fx)
    {
        if (fx.Scene3d is null) return null;

        var scene3d = new XElement(A + "scene3d");

        // CT_Scene3D requires <a:camera> (minOccurs=1). Always emit one; use the
        // schema-valid default preset when the model has no camera data (e.g. a
        // lightRig-only scene read from an older file).
        var cameraPreset = !string.IsNullOrEmpty(fx.Scene3d.CameraPreset)
            ? fx.Scene3d.CameraPreset
            : "orthographicFront";
        scene3d.Add(new XElement(A + "camera",
            new XAttribute("prst", cameraPreset)));

        // CT_LightRig requires both rig= and dir=. Only emit <a:lightRig> when
        // both attributes are present; a bare <a:lightRig/> is schema-invalid.
        if (!string.IsNullOrEmpty(fx.Scene3d.LightRig) && !string.IsNullOrEmpty(fx.Scene3d.LightRigDir))
        {
            scene3d.Add(new XElement(A + "lightRig",
                new XAttribute("rig", fx.Scene3d.LightRig),
                new XAttribute("dir", fx.Scene3d.LightRigDir)));
        }

        return scene3d;
    }

    // ── Table / graphicFrame elements ─────────────────────────────────────────────

    private const string DrawingTableUri = "http://schemas.openxmlformats.org/drawingml/2006/table";

    private static XElement BuildGraphicFrameEl(
        SlideShape shape,
        PresentationColorScheme scheme,
        Dictionary<string, string>? hlinkRelIds = null,
        List<Slide>? allSlides = null,
        Dictionary<Paragraph, string>? bulletImageRelIds = null)
    {
        var table = shape.Table!;

        // xfrm
        var xfrm = new XElement(P + "xfrm",
            new XElement(A + "off",
                new XAttribute("x", shape.OffsetXEmu),
                new XAttribute("y", shape.OffsetYEmu)),
            new XElement(A + "ext",
                new XAttribute("cx", shape.ExtentCxEmu),
                new XAttribute("cy", shape.ExtentCyEmu)));
        if (shape.RotationDeg != 0)
            xfrm.SetAttributeValue("rot", (long)Math.Round(shape.RotationDeg * 60000));
        if (shape.FlipH) xfrm.SetAttributeValue("flipH", "1");
        if (shape.FlipV) xfrm.SetAttributeValue("flipV", "1");

        return new XElement(P + "graphicFrame",
            new XElement(P + "nvGraphicFramePr",
                CnvPrWithHlink(shape, hlinkRelIds, allSlides),
                new XElement(P + "cNvGraphicFramePr",
                    new XElement(A + "graphicFrameLocks",
                        new XAttribute("noGrp", "1"))),
                new XElement(P + "nvPr")),
            xfrm,
            new XElement(A + "graphic",
                new XElement(A + "graphicData",
                    new XAttribute("uri", DrawingTableUri),
                    BuildTableEl(table, scheme, bulletImageRelIds))));
    }

    // ── Chart / graphicFrame elements ─────────────────────────────────────────────

    private const string DrawingChartUri = "http://schemas.openxmlformats.org/drawingml/2006/chart";
    private const string DrawingChartExUri = "http://schemas.microsoft.com/office/drawing/2014/chartex";
    private static readonly XNamespace CxChart = DrawingChartExUri;
    private static readonly XNamespace CChartNs =
        "http://schemas.openxmlformats.org/drawingml/2006/chart";

    /// <summary>
    /// Builds the p:graphicFrame element for a chart shape.
    /// <paramref name="mediaById"/> carries chart rel IDs added by
    /// <see cref="WriteSlideCharts"/> (keyed by shape.Id).
    /// </summary>
    private static XElement BuildChartGraphicFrameEl(
        SlideShape shape, Dictionary<uint, string> mediaById,
        Dictionary<string, string>? hlinkRelIds = null, List<Slide>? allSlides = null)
    {
        mediaById.TryGetValue(shape.Id, out var chartRelId);
        chartRelId ??= "rIdChart1"; // fallback (should not happen)

        var xfrm = new XElement(P + "xfrm",
            new XElement(A + "off",
                new XAttribute("x", shape.OffsetXEmu),
                new XAttribute("y", shape.OffsetYEmu)),
            new XElement(A + "ext",
                new XAttribute("cx", shape.ExtentCxEmu),
                new XAttribute("cy", shape.ExtentCyEmu)));

        var frame = new XElement(P + "graphicFrame",
            new XElement(P + "nvGraphicFramePr",
                CnvPrWithHlink(shape, hlinkRelIds, allSlides),
                new XElement(P + "cNvGraphicFramePr",
                    new XElement(A + "graphicFrameLocks",
                        new XAttribute("noGrp", "1"))),
                new XElement(P + "nvPr")),
            xfrm,
            new XElement(A + "graphic",
                new XElement(A + "graphicData",
                    new XAttribute("uri", shape.Chart?.IsChartEx == true ? DrawingChartExUri : DrawingChartUri),
                    new XElement(shape.Chart?.IsChartEx == true ? CxChart + "chart" : CChartNs + "chart",
                        // Declare the chart prefix so PowerPoint sees the correct chart family.
                        new XAttribute(XNamespace.Xmlns + (shape.Chart?.IsChartEx == true ? "cx" : "c"),
                            shape.Chart?.IsChartEx == true ? DrawingChartExUri : DrawingChartUri),
                        new XAttribute(R + "id", chartRelId)))));

        if (shape.Chart?.IsChartEx != true)
            return frame;

        return new XElement(MC + "AlternateContent",
            new XAttribute(XNamespace.Xmlns + "cx1", DrawingChartExUri),
            new XElement(MC + "Choice",
                new XAttribute("Requires", "cx1"),
                frame),
            new XElement(MC + "Fallback",
                BuildSpEl(shape, PresentationColorScheme.CreateDefault())));
    }

    // ── SmartArt / graphicFrame elements ──────────────────────────────────────────

    private const string DrawingDiagramUri = "http://schemas.openxmlformats.org/drawingml/2006/diagram";
    private static readonly XNamespace DgmNs =
        "http://schemas.openxmlformats.org/drawingml/2006/diagram";

    /// <summary>
    /// Builds the p:graphicFrame element for a SmartArt shape, referencing the diagram
    /// sub-parts (data/layout/quickStyle/colors) via the rel IDs stored in the model.
    /// </summary>
    /// <summary>
    /// Builds the graphicFrame element for a SmartArt shape.
    /// <paramref name="relIdRemap"/> maps diagram key (dm/lo/qs/cs) to the fresh relId that
    /// was written into the slide rels. Only keys present in the map are emitted as r: attributes.
    /// Returns null if the required data part (dm) has no relId — the shape cannot render and
    /// must be dropped entirely to avoid dangling relationships.
    /// </summary>
    private static XElement? BuildSmartArtGraphicFrameEl(
        SlideShape shape, Dictionary<string, string>? relIdRemap,
        Dictionary<string, string>? hlinkRelIds = null, List<Slide>? allSlides = null)
    {
        // S2: if dm (data) part is absent, the SmartArt can't render — drop the frame.
        if (relIdRemap is null || !relIdRemap.ContainsKey("dm"))
            return null;

        var xfrm = new XElement(P + "xfrm",
            new XElement(A + "off",
                new XAttribute("x", shape.OffsetXEmu),
                new XAttribute("y", shape.OffsetYEmu)),
            new XElement(A + "ext",
                new XAttribute("cx", shape.ExtentCxEmu),
                new XAttribute("cy", shape.ExtentCyEmu)));

        // Build dgm:relIds child with r:dm / r:lo / r:qs / r:cs attributes.
        // S2: only emit attributes for keys whose parts were actually written.
        var relIdsEl = new XElement(DgmNs + "relIds",
            new XAttribute(XNamespace.Xmlns + "dgm", DrawingDiagramUri),
            new XAttribute(XNamespace.Xmlns + "r", R.NamespaceName));

        foreach (var (key, relId) in relIdRemap)
        {
            relIdsEl.Add(new XAttribute(R + key, relId));
        }

        return new XElement(P + "graphicFrame",
            new XElement(P + "nvGraphicFramePr",
                CnvPrWithHlink(shape, hlinkRelIds, allSlides),
                new XElement(P + "cNvGraphicFramePr",
                    new XElement(A + "graphicFrameLocks",
                        new XAttribute("noGrp", "1"))),
                new XElement(P + "nvPr")),
            xfrm,
            new XElement(A + "graphic",
                new XElement(A + "graphicData",
                    new XAttribute("uri", DrawingDiagramUri),
                    relIdsEl)));
    }

    private static XElement BuildTableEl(
        TableShape table,
        PresentationColorScheme scheme,
        Dictionary<Paragraph, string>? bulletImageRelIds = null)
    {
        // tblPr
        var tblPr = new XElement(A + "tblPr");
        if (table.Flags.FirstRow) tblPr.Add(new XAttribute("firstRow", "1"));
        if (table.Flags.LastRow)  tblPr.Add(new XAttribute("lastRow", "1"));
        if (table.Flags.FirstCol) tblPr.Add(new XAttribute("firstCol", "1"));
        if (table.Flags.LastCol)  tblPr.Add(new XAttribute("lastCol", "1"));
        if (table.Flags.BandRow)  tblPr.Add(new XAttribute("bandRow", "1"));
        if (table.Flags.BandCol)  tblPr.Add(new XAttribute("bandCol", "1"));
        if (!string.IsNullOrWhiteSpace(table.TableStyleId))
            tblPr.Add(new XElement(A + "tableStyleId", table.TableStyleId));

        // tblGrid
        var tblGrid = new XElement(A + "tblGrid",
            table.ColumnWidthsEmu.Select(w =>
                new XElement(A + "gridCol", new XAttribute("w", w))));

        // rows
        var rowEls = table.Rows.Select(row => BuildTableRowEl(row, scheme, bulletImageRelIds));

        return new XElement(A + "tbl", tblPr, tblGrid, rowEls);
    }

    private static XElement BuildTableRowEl(
        TableRow row,
        PresentationColorScheme scheme,
        Dictionary<Paragraph, string>? bulletImageRelIds = null) =>
        new XElement(A + "tr",
            new XAttribute("h", row.HeightEmu),
            row.Cells.Select(cell => BuildTableCellEl(cell, scheme, bulletImageRelIds)));

    private static XElement BuildTableCellEl(
        TableCell cell,
        PresentationColorScheme scheme,
        Dictionary<Paragraph, string>? bulletImageRelIds = null)
    {
        // txBody (a:txBody inside a cell uses A namespace directly)
        XElement? txBody = null;
        if (cell.TextBody is not null)
        {
            var bodyPr = new XElement(A + "bodyPr");
            if (cell.TextBody.Anchor.HasValue)
                bodyPr.Add(new XAttribute("anchor", cell.TextBody.Anchor.Value switch
                {
                    VerticalAnchor.Middle => "ctr",
                    VerticalAnchor.Bottom => "b",
                    _ => "t"
                }));

            txBody = new XElement(A + "txBody",
                bodyPr,
                BuildLstStyleEl(
                    cell.TextBody.LstStyle,
                    cell.TextBody.DefaultParaRightToLeft),
                cell.TextBody.Paragraphs.Select(p => BuildParaEl(p, bulletImageRelIds: bulletImageRelIds)));
        }
        else
        {
            // Empty txBody is required by spec.
            txBody = new XElement(A + "txBody",
                new XElement(A + "bodyPr"),
                new XElement(A + "lstStyle"),
                new XElement(A + "p"));
        }

        // tcPr
        var tcPr = new XElement(A + "tcPr");
        if (cell.InsetLeftPt.HasValue)   tcPr.Add(new XAttribute("marL", DrawingMlUnits.PointsToEmu(cell.InsetLeftPt.Value)));
        if (cell.InsetRightPt.HasValue)  tcPr.Add(new XAttribute("marR", DrawingMlUnits.PointsToEmu(cell.InsetRightPt.Value)));
        if (cell.InsetTopPt.HasValue)    tcPr.Add(new XAttribute("marT", DrawingMlUnits.PointsToEmu(cell.InsetTopPt.Value)));
        if (cell.InsetBottomPt.HasValue) tcPr.Add(new XAttribute("marB", DrawingMlUnits.PointsToEmu(cell.InsetBottomPt.Value)));
        if (cell.Anchor.HasValue)
            tcPr.Add(new XAttribute("anchor", cell.Anchor.Value switch
            {
                TableCellAnchor.Middle => "ctr",
                TableCellAnchor.Bottom => "b",
                _ => "t"
            }));

        // Per-side borders
        if (cell.Borders?.Left   is { } bl) tcPr.Add(new XElement(A + "lnL",   BuildBorderAttrs(bl)));
        if (cell.Borders?.Right  is { } br) tcPr.Add(new XElement(A + "lnR",   BuildBorderAttrs(br)));
        if (cell.Borders?.Top    is { } bt) tcPr.Add(new XElement(A + "lnT",   BuildBorderAttrs(bt)));
        if (cell.Borders?.Bottom is { } bb) tcPr.Add(new XElement(A + "lnB",   BuildBorderAttrs(bb)));

        // Explicit fill
        if (cell.Fill is not null)
        {
            var fillEl = BuildFillEl(cell.Fill, scheme);
            if (fillEl is not null)
                tcPr.Add(fillEl);
        }

        // Build the tc element.
        var tc = new XElement(A + "tc");

        // Merge attributes.
        if (cell.GridSpan > 1) tc.Add(new XAttribute("gridSpan", cell.GridSpan));
        if (cell.RowSpan > 1)  tc.Add(new XAttribute("rowSpan", cell.RowSpan));
        if (cell.HMerge) tc.Add(new XAttribute("hMerge", "1"));
        if (cell.VMerge) tc.Add(new XAttribute("vMerge", "1"));

        tc.Add(txBody);
        tc.Add(tcPr);
        return tc;
    }

    private static object[] BuildBorderAttrs(ShapeOutline outline)
    {
        if (outline is ShapeOutline.None)
            return new object[] { new XElement(A + "noFill") };

        if (outline is ShapeOutline.Visible v)
        {
            var children = new List<object>
            {
                new XAttribute("w", DrawingMlUnits.PointsToEmu(v.WidthPt)),
                new XElement(A + "solidFill", BuildColorEl(v.Color))
            };
            if (v.Dash != OutlineDash.Solid)
                children.Add(new XElement(A + "prstDash", new XAttribute("val", ToDashStr(v.Dash))));
            return children.ToArray();
        }

        // Wave 22B: gradient outline border
        if (outline is ShapeOutline.GradientVisible gv)
        {
            var children = new List<object>
            {
                new XAttribute("w", DrawingMlUnits.PointsToEmu(gv.WidthPt)),
                BuildGradFillEl(gv.Gradient)
            };
            if (gv.Dash != OutlineDash.Solid)
                children.Add(new XElement(A + "prstDash", new XAttribute("val", ToDashStr(gv.Dash))));
            return children.ToArray();
        }

        return Array.Empty<object>();
    }

    // ── Fill elements ─────────────────────────────────────────────────────────────

    /// <param name="blipRelId">
    /// When the fill is a <see cref="ShapeFill.Picture"/>, the relationship id to embed
    /// (previously registered in the slide rels). Null means picture fill cannot be written.
    /// </param>
    private static XElement? BuildFillEl(
        ShapeFill fill,
        PresentationColorScheme scheme,
        string? blipRelId = null) =>
        fill switch
        {
            ShapeFill.None => new XElement(A + "noFill"),
            ShapeFill.Solid s => new XElement(A + "solidFill", BuildColorEl(s.Color)),
            ShapeFill.Gradient g => BuildGradFillEl(g),
            ShapeFill.Picture p when blipRelId is not null => BuildBlipFillEl(p, blipRelId),
            ShapeFill.Pattern pat => BuildPattFillEl(pat),
            _ => null
        };

    private static XElement BuildGradFillEl(ShapeFill.Gradient g)
    {
        // HH2: stops MUST be in ascending position order per OOXML CT_GradientStopList.
        // HH3: a:gsLst requires at least 2 stops; synthesise when model has fewer.
        var stops = g.Stops.OrderBy(s => s.Position).ToList();
        if (stops.Count == 0)
        {
            // No stops at all: emit white@0 → black@100k
            stops = new List<GradientStop>
            {
                new GradientStop(0.0, ThemeAwareColor.White),
                new GradientStop(1.0, ThemeAwareColor.Black),
            };
        }
        else if (stops.Count == 1)
        {
            // Duplicate the single stop at position 0 and 100000
            var singleColor = stops[0].Color;
            stops = new List<GradientStop>
            {
                new GradientStop(0.0, singleColor),
                new GradientStop(1.0, singleColor),
            };
        }

        var gsLst = new XElement(A + "gsLst");
        foreach (var stop in stops)
        {
            int pos = (int)Math.Round(stop.Position * 100000);
            // CT_GradientStop: a:gs must contain a color element directly (srgbClr/schemeClr/…),
            // NOT wrapped in a:solidFill — that wrapper is invalid per ECMA-376 schema.
            gsLst.Add(new XElement(A + "gs",
                new XAttribute("pos", pos),
                BuildColorEl(stop.Color)));
        }

        XElement kindEl;
        if (g.Kind == GradientKind.Radial)
        {
            kindEl = new XElement(A + "path",
                new XAttribute("path", "circle"),
                new XElement(A + "fillToRect",
                    new XAttribute("l", "50000"),
                    new XAttribute("t", "50000"),
                    new XAttribute("r", "50000"),
                    new XAttribute("b", "50000")));
        }
        else
        {
            kindEl = new XElement(A + "lin",
                new XAttribute("ang", (long)Math.Round(g.AngleDegrees * 60000)),
                new XAttribute("scaled", "0"));
        }

        return new XElement(A + "gradFill", gsLst, kindEl);
    }

    private static XElement BuildBlipFillEl(ShapeFill.Picture p, string blipRelId)
    {
        var blipFill = new XElement(A + "blipFill",
            new XElement(A + "blip", new XAttribute(R + "embed", blipRelId)));
        if (p.Tile)
            blipFill.Add(new XElement(A + "tile"));
        else
            blipFill.Add(new XElement(A + "stretch", new XElement(A + "fillRect")));
        return blipFill;
    }

    private static XElement BuildPattFillEl(ShapeFill.Pattern pat) =>
        new XElement(A + "pattFill",
            new XAttribute("prst", pat.Preset),
            new XElement(A + "fgClr", BuildColorEl(pat.ForegroundColor)),
            new XElement(A + "bgClr", BuildColorEl(pat.BackgroundColor)));

    private static XElement BuildColorEl(ThemeAwareColor color)
    {
        XElement el;
        if (color.SchemeColor is { } sc)
        {
            el = new XElement(A + "schemeClr",
                new XAttribute("val", PptxColorReader.ToSchemeColorString(sc.Slot)));
            if (Math.Abs(sc.LumMod - 1.0) > 1e-9)
                el.Add(new XElement(A + "lumMod", new XAttribute("val", (long)Math.Round(sc.LumMod * 100000))));
            if (Math.Abs(sc.LumOff) > 1e-9)
                el.Add(new XElement(A + "lumOff", new XAttribute("val", (long)Math.Round(sc.LumOff * 100000))));
            // Tint and shade default to 1.0 (= no modifier); only emit when a modifier is present.
            if (Math.Abs(sc.Tint - 1.0) > 1e-9)
                el.Add(new XElement(A + "tint",  new XAttribute("val", (long)Math.Round(sc.Tint  * 100000))));
            if (Math.Abs(sc.Shade - 1.0) > 1e-9)
                el.Add(new XElement(A + "shade", new XAttribute("val", (long)Math.Round(sc.Shade * 100000))));
        }
        else
        {
            el = new XElement(A + "srgbClr", new XAttribute("val", FmtColor(color.Resolved)));
        }

        AddAlphaEl(el, color.Alpha);
        return el;
    }

    private static void AddAlphaEl(XElement colorEl, byte alpha)
    {
        if (alpha < byte.MaxValue)
            colorEl.Add(new XElement(A + "alpha", new XAttribute("val", (long)Math.Round(alpha / 255.0 * 100000))));
    }

    // ── Outline elements ──────────────────────────────────────────────────────────

    private static XElement BuildOutlineEl(ShapeOutline outline, bool includeLineEnds = false) =>
        outline switch
        {
            ShapeOutline.None => new XElement(A + "ln", new XElement(A + "noFill")),
            ShapeOutline.Visible v => BuildVisibleOutlineEl(v, includeLineEnds),
            // Wave 22B: gradient outline
            ShapeOutline.GradientVisible gv => BuildGradientOutlineEl(gv, includeLineEnds),
            _ => new XElement(A + "ln")
        };

    private static XElement BuildVisibleOutlineEl(ShapeOutline.Visible outline, bool includeLineEnds)
    {
        var children = new List<object?>
        {
            new XAttribute("w", DrawingMlUnits.PointsToEmu(outline.WidthPt)),
            new XElement(A + "solidFill", BuildColorEl(outline.Color)),
            outline.Dash != OutlineDash.Solid
                ? new XElement(A + "prstDash", new XAttribute("val", ToDashStr(outline.Dash)))
                : null
        };
        AddLineEndElements(children, outline.BeginLineEnd, outline.EndLineEnd, includeLineEnds);
        return new XElement(A + "ln", children);
    }

    private static XElement BuildGradientOutlineEl(ShapeOutline.GradientVisible outline, bool includeLineEnds)
    {
        var children = new List<object?>
        {
            new XAttribute("w", DrawingMlUnits.PointsToEmu(outline.WidthPt)),
            BuildGradFillEl(outline.Gradient),
            outline.Dash != OutlineDash.Solid
                ? new XElement(A + "prstDash", new XAttribute("val", ToDashStr(outline.Dash)))
                : null
        };
        AddLineEndElements(children, outline.BeginLineEnd, outline.EndLineEnd, includeLineEnds);
        return new XElement(A + "ln", children);
    }

    private static void AddLineEndElements(
        List<object?> children,
        ShapeLineEnd? beginLineEnd,
        ShapeLineEnd? endLineEnd,
        bool includeLineEnds)
    {
        if (!includeLineEnds)
            return;

        if (endLineEnd is not null)
            children.Add(BuildLineEndEl("headEnd", endLineEnd));
        if (beginLineEnd is not null)
            children.Add(BuildLineEndEl("tailEnd", beginLineEnd));
    }

    private static XElement BuildLineEndEl(string localName, ShapeLineEnd lineEnd) =>
        new(A + localName, new XAttribute("type", ToLineEndType(lineEnd.Kind)));

    private static string ToLineEndType(ShapeLineEndKind kind) =>
        kind switch
        {
            ShapeLineEndKind.Triangle => "triangle",
            _ => "none"
        };

    private static bool ShouldWriteLineEnds(SlideShape shape) =>
        shape.Kind == SlideShapeKind.Connector
        || shape.AutoShapeKind is DrawingShapeKind.Line
            or DrawingShapeKind.ElbowConnector
            or DrawingShapeKind.CurvedConnector;

    // ── a:lstStyle helper ─────────────────────────────────────────────────────────

    /// <summary>
    /// Emits an <c>a:lstStyle</c> element. When <paramref name="levels"/> is null (no explicit list
    /// style) emits an empty element so the XML remains valid. When levels are present each
    /// non-null level is emitted as <c>a:lvlNpPr</c>.
    /// </summary>
    private static XElement BuildLstStyleEl(
        TextStyleLevels? levels,
        bool? defaultParaRightToLeft = null)
    {
        if ((levels is null || !levels.HasAny) && !defaultParaRightToLeft.HasValue)
            return new XElement(A + "lstStyle");

        var el = new XElement(A + "lstStyle");
        for (int i = 0; i < 9; i++)
        {
            var level = levels?[i];
            if (level is null)
            {
                if (i == 0 && defaultParaRightToLeft.HasValue)
                    el.Add(BuildLvlpPrEl("lvl1pPr", new TextStyleLevel(), defaultParaRightToLeft));
                continue;
            }
            el.Add(BuildLvlpPrEl($"lvl{i + 1}pPr", level, i == 0 ? defaultParaRightToLeft : null));
        }
        return el;
    }

    // ── TextBody elements ─────────────────────────────────────────────────────────

    private static XElement BuildTxBodyEl(TextBody body, PresentationColorScheme scheme,
        Dictionary<string, string>? hlinkRelIds = null,
        List<Slide>? allSlides = null,
        Dictionary<Paragraph, string>? bulletImageRelIds = null)
    {
        // Write anchor only when explicitly set; omit when null (inherited from layout/master).
        var bodyPr = new XElement(A + "bodyPr");
        if (body.Anchor.HasValue)
        {
            bodyPr.Add(new XAttribute("anchor", body.Anchor.Value switch
            {
                VerticalAnchor.Middle => "ctr",
                VerticalAnchor.Bottom => "b",
                VerticalAnchor.Top => "t",
                VerticalAnchor.Distributed => "dist",
                _ => "t"
            }));
        }

        // Wave 18B: text vertical orientation (a:bodyPr vert=)
        if (body.VerticalType != TextVerticalType.Horizontal)
        {
            var vertStr = body.VerticalType switch
            {
                TextVerticalType.Vertical           => "vert",
                TextVerticalType.Vertical270        => "vert270",
                TextVerticalType.EastAsianVertical  => "eaVert",
                TextVerticalType.WordArtVertical    => "wordArtVert",
                TextVerticalType.WordArtVerticalRtl => "wordArtVertRtl",
                _                                   => "horz"
            };
            bodyPr.Add(new XAttribute("vert", vertStr));
        }

        if (!body.Wrap) bodyPr.Add(new XAttribute("wrap", "none"));
        if (body.InsetLeftPt.HasValue) bodyPr.Add(new XAttribute("lIns", DrawingMlUnits.PointsToEmu(body.InsetLeftPt.Value)));
        if (body.InsetRightPt.HasValue) bodyPr.Add(new XAttribute("rIns", DrawingMlUnits.PointsToEmu(body.InsetRightPt.Value)));
        if (body.InsetTopPt.HasValue) bodyPr.Add(new XAttribute("tIns", DrawingMlUnits.PointsToEmu(body.InsetTopPt.Value)));
        if (body.InsetBottomPt.HasValue) bodyPr.Add(new XAttribute("bIns", DrawingMlUnits.PointsToEmu(body.InsetBottomPt.Value)));
        // Wave 19A / LA1: re-emit the ORIGINAL autofit element kind so an spAutoFit shape
        // round-trips as spAutoFit (never rewritten as normAutofit) and vice versa.
        switch (body.AutoFitKind)
        {
            case TextAutoFitKind.Normal:
                var nafEl = new XElement(A + "normAutofit");
                if (body.FontScalePPT.HasValue && body.FontScalePPT.Value > 0)
                    nafEl.Add(new XAttribute("fontScale", body.FontScalePPT.Value));
                if (body.LnSpcReductionPPT.HasValue && body.LnSpcReductionPPT.Value > 0)
                    nafEl.Add(new XAttribute("lnSpcReduction", body.LnSpcReductionPPT.Value));
                bodyPr.Add(nafEl);
                break;
            case TextAutoFitKind.Shape:
                bodyPr.Add(new XElement(A + "spAutoFit"));
                break;
            case TextAutoFitKind.None:
            default:
                // No element written for None, matching prior behavior for AutoFit=false.
                break;
        }

        // Wave 16A: warp preset + adjust guides (BA4)
        if (!string.IsNullOrWhiteSpace(body.WarpPreset))
        {
            var avLst = new XElement(A + "avLst");
            foreach (var (name, fmla) in body.WarpAdjusts)
                avLst.Add(new XElement(A + "gd",
                    new XAttribute("name", name),
                    new XAttribute("fmla", fmla)));
            bodyPr.Add(new XElement(A + "prstTxWarp",
                new XAttribute("prst", body.WarpPreset),
                avLst));
        }

        // WordArt 3-D material and lighting live on a:bodyPr, not p:spPr.
        if (body.Text3dEffects is { } textEffects)
        {
            var scene3d = BuildScene3dEl(textEffects);
            var sp3d = BuildSp3dEl(textEffects);
            if (scene3d is not null)
                bodyPr.Add(scene3d);
            if (sp3d is not null)
                bodyPr.Add(sp3d);
        }

        // Wave 22B: text columns (a:bodyPr numCol= spcCol=)
        if (body.ColumnCount > 1)
        {
            bodyPr.Add(new XAttribute("numCol", body.ColumnCount));
            if (body.ColumnSpacingEmu > 0)
                bodyPr.Add(new XAttribute("spcCol", body.ColumnSpacingEmu));
        }

        // In PresentationML, the text body inside p:sp is p:txBody (not a:txBody).
        // Body-level elements use a: namespace, paragraphs/runs use a: namespace.
        return new XElement(P + "txBody",
            bodyPr,
            BuildLstStyleEl(body.LstStyle, body.DefaultParaRightToLeft),
            body.Paragraphs.Select(p => BuildParaEl(p, hlinkRelIds, allSlides, bulletImageRelIds)));
    }

    private static XElement BuildParaEl(Paragraph para,
        Dictionary<string, string>? hlinkRelIds = null,
        List<Slide>? allSlides = null,
        Dictionary<Paragraph, string>? bulletImageRelIds = null)
    {
        var pPr = new XElement(A + "pPr");
        bool hasPPr = false;

        if (para.Align.HasValue)
        {
            pPr.Add(new XAttribute("algn", para.Align.Value switch
            {
                TextAlign.Center => "ctr",
                TextAlign.Right => "r",
                TextAlign.Justify => "just",
                TextAlign.Distributed => "dist",
                _ => "l"
            }));
            hasPPr = true;
        }
        if (para.RightToLeft.HasValue)
        {
            pPr.Add(new XAttribute("rtl", para.RightToLeft.Value ? "1" : "0"));
            hasPPr = true;
        }
        if (para.Level > 0) { pPr.Add(new XAttribute("lvl", para.Level)); hasPPr = true; }

        // Wave 19A: write marL/indent when set
        if (para.MarginLeftEmu.HasValue) { pPr.Add(new XAttribute("marL", para.MarginLeftEmu.Value)); hasPPr = true; }
        if (para.IndentEmu.HasValue)     { pPr.Add(new XAttribute("indent", para.IndentEmu.Value)); hasPPr = true; }

        // BU2: CT_TextParagraphProperties child ORDER per ECMA-376:
        //   lnSpc → spcBef → spcAft → bullet group (buClr/buSz/buFont/buNone/buAutoNum/buChar)
        //   → tabLst → defRPr
        // spcBef/spcAft must come BEFORE the bullet group elements.
        if (para.SpaceBeforePt.HasValue)
        {
            pPr.Add(new XElement(A + "spcBef",
                new XElement(A + "spcPts", new XAttribute("val", (int)Math.Round(para.SpaceBeforePt.Value * 100)))));
            hasPPr = true;
        }
        if (para.SpaceAfterPt.HasValue)
        {
            pPr.Add(new XElement(A + "spcAft",
                new XElement(A + "spcPts", new XAttribute("val", (int)Math.Round(para.SpaceAfterPt.Value * 100)))));
            hasPPr = true;
        }

        if (AddBulletTypographyProperties(
                pPr,
                para.BulletColor,
                para.BulletColorFollowsText,
                para.BulletSizePct,
                para.BulletSizePt,
                para.BulletSizeFollowsText,
                para.BulletFontFamily,
                para.BulletFontFollowsText))
        {
            hasPPr = true;
        }

        switch (para.BulletKind)
        {
            case BulletKind.None:
                pPr.Add(new XElement(A + "buNone")); hasPPr = true; break;
            case BulletKind.Char:
                pPr.Add(new XElement(A + "buChar", new XAttribute("char", para.BulletChar ?? "•"))); hasPPr = true; break;
            case BulletKind.Auto:
                var autoNumTypeStr = para.AutoNumType switch
                {
                    AutoNumType.ArabicParenR    => "arabicParenR",
                    AutoNumType.ArabicParenBoth => "arabicParenBoth",
                    AutoNumType.RomanUcPeriod   => "romanUcPeriod",
                    AutoNumType.RomanLcPeriod   => "romanLcPeriod",
                    AutoNumType.RomanUcParenR   => "romanUcParenR",
                    AutoNumType.RomanLcParenR   => "romanLcParenR",
                    AutoNumType.AlphaUcPeriod   => "alphaUcPeriod",
                    AutoNumType.AlphaLcPeriod   => "alphaLcPeriod",
                    AutoNumType.AlphaUcParenR   => "alphaUcParenR",
                    AutoNumType.AlphaLcParenR   => "alphaLcParenR",
                    AutoNumType.AlphaUcParenBoth => "alphaUcParenBoth",
                    AutoNumType.AlphaLcParenBoth => "alphaLcParenBoth",
                    _                           => "arabicPeriod"
                };
                var autoNumEl = new XElement(A + "buAutoNum", new XAttribute("type", autoNumTypeStr));
                if (para.AutoNumStartAtSpecified || para.AutoNumStartAt != 1)
                    autoNumEl.Add(new XAttribute("startAt", Math.Max(1, para.AutoNumStartAt)));
                pPr.Add(autoNumEl); hasPPr = true; break;
            case BulletKind.Image:
                if (bulletImageRelIds is not null &&
                    bulletImageRelIds.TryGetValue(para, out var bulletImageRelId))
                {
                    pPr.Add(new XElement(A + "buBlip",
                        new XElement(A + "blip", new XAttribute(R + "embed", bulletImageRelId))));
                    hasPPr = true;
                }
                else
                {
                    pPr.Add(new XElement(A + "buNone"));
                    hasPPr = true;
                }
                break;
        }

        // Wave 18B: tab stops (a:tabLst)
        if (para.TabStops.Count > 0)
        {
            var tabLst = new XElement(A + "tabLst");
            foreach (var tab in para.TabStops)
            {
                var algn = tab.Alignment switch
                {
                    TabStopAlignment.Center  => "ctr",
                    TabStopAlignment.Right   => "r",
                    TabStopAlignment.Decimal => "dec",
                    _                        => "l"
                };
                tabLst.Add(new XElement(A + "tab",
                    new XAttribute("pos", tab.PositionEmu),
                    new XAttribute("algn", algn)));
            }
            pPr.Add(tabLst);
            hasPPr = true;
        }

        if (para.BulletKind == BulletKind.Auto
            && !string.IsNullOrWhiteSpace(para.AutoNumTextTemplate))
        {
            pPr.Add(new XElement(A + "extLst",
                new XElement(A + "ext",
                    new XAttribute("uri", AutoNumTemplateExtUri),
                    new XAttribute(FreePText + "autoNumTemplate", para.AutoNumTextTemplate))));
            hasPPr = true;
        }

        return new XElement(A + "p",
            hasPPr ? pPr : null,
            para.Runs.Select(r => BuildRunEl(r, hlinkRelIds, allSlides)));
    }

    private static XElement BuildRunEl(Run run,
        Dictionary<string, string>? hlinkRelIds = null,
        List<Slide>? allSlides = null)
    {
        if (run.Text == "\n") return new XElement(A + "br");

        // Theme 21: math run — re-emit the preserved OMML XML verbatim.
        // The raw XML is the entire element (a14:m or mc:AlternateContent); parse it back
        // to an XElement so it slots cleanly into the parent a:p's element list.
        if (run.Math is { RawXml: { Length: > 0 } rawXml })
        {
            try
            {
                return XElement.Parse(rawXml, LoadOptions.PreserveWhitespace);
            }
            catch
            {
                // If the stored XML is malformed, fall through to emit plain text as a:r
            }
        }

        // Field run: emit a:fld instead of a:r
        if (run.Field is not null)
        {
            var fld = run.Field;
            var fldId = Guid.NewGuid().ToString("B").ToUpperInvariant();
            var fldRPr = BuildFieldRPr(fld);
            return new XElement(A + "fld",
                new XAttribute("id", fldId),
                new XAttribute("type", fld.FieldType),
                fldRPr,
                new XElement(A + "t", run.Text));
        }

        var rPr = new XElement(A + "rPr",
            new XAttribute("lang", "en-US"),
            new XAttribute("dirty", "0"));

        if (run.BoldSet)   rPr.Add(new XAttribute("b", run.Bold   ? "1" : "0"));
        else if (run.Bold) rPr.Add(new XAttribute("b", "1"));
        if (run.ItalicSet)   rPr.Add(new XAttribute("i", run.Italic ? "1" : "0"));
        else if (run.Italic) rPr.Add(new XAttribute("i", "1"));
        if (run.Underline) rPr.Add(new XAttribute("u", "sng"));
        if (run.Strikethrough) rPr.Add(new XAttribute("strike", "sngStrike"));
        if (run.RightToLeft.HasValue)
            rPr.Add(new XAttribute("rtl", run.RightToLeft.Value ? "1" : "0"));
        if (run.Caps != RunTextCaps.None)
            rPr.Add(new XAttribute("cap", run.Caps == RunTextCaps.All ? "all" : "small"));
        if (run.FontSizePt.HasValue)
            rPr.Add(new XAttribute("sz", (int)Math.Round(run.FontSizePt.Value * 100)));
        if (run.BaselineOffset.HasValue)
            rPr.Add(new XAttribute("baseline", run.BaselineOffset.Value));

        // CT_TextCharacterProperties child order (ECMA-376):
        //   a:ln → fill group (noFill/solidFill/gradFill/…) → a:effectLst → a:latin/ea/cs → a:hlinkClick

        // Wave 16A: text outline — a:ln FIRST
        if (run.TextOutline is not null)
            rPr.Add(BuildOutlineEl(run.TextOutline));

        // Fill group: gradient takes precedence; solid color is the fallback
        if (run.TextFill is not null)
        {
            var fillEl = BuildFillEl(run.TextFill, PresentationColorScheme.CreateDefault());
            if (fillEl is not null) rPr.Add(fillEl);
        }
        else if (run.Color is not null)
        {
            rPr.Add(new XElement(A + "solidFill", BuildColorEl(run.Color)));
        }

        // Wave 16A: text effects — a:effectLst AFTER fill group
        if (run.TextShadow is not null ||
            run.TextReflection is not null ||
            run.TextGlow is not null ||
            run.TextSoftEdge is not null)
        {
            var effectLst = new XElement(A + "effectLst");

            if (run.TextGlow is not null)
            {
                var glow = run.TextGlow;
                var glowColorEl = BuildColorEl(glow.Color);
                glowColorEl.Add(new XElement(A + "alpha",
                    new XAttribute("val", (long)Math.Round(glow.Alpha / 255.0 * 100000))));
                effectLst.Add(new XElement(A + "glow",
                    new XAttribute("rad", DrawingMlUnits.PointsToEmu(glow.RadiusPt)),
                    glowColorEl));
            }

            if (run.TextShadow is not null)
            {
                var ts = run.TextShadow;
                var shdwColorEl = BuildColorEl(ts.Color);
                // Embed alpha on the color element only when < fully opaque;
                // omitting a:alpha means 100% opaque in DrawingML.
                if (ts.Alpha < 255)
                    shdwColorEl.Add(new XElement(A + "alpha",
                        new XAttribute("val", (long)Math.Round(ts.Alpha / 255.0 * 100000))));
                effectLst.Add(new XElement(A + "outerShdw",
                    new XAttribute("blurRad", DrawingMlUnits.PointsToEmu(ts.BlurPt)),
                    new XAttribute("dist",    DrawingMlUnits.PointsToEmu(ts.DistPt)),
                    new XAttribute("dir",     (long)Math.Round(ts.DirDeg * 60000)),
                    shdwColorEl));
            }

            if (run.TextReflection is not null)
            {
                var reflection = run.TextReflection;
                effectLst.Add(new XElement(A + "reflection",
                    new XAttribute("blurRad", DrawingMlUnits.PointsToEmu(reflection.BlurPt)),
                    new XAttribute("stA", (long)Math.Round(reflection.Alpha / 255.0 * 100000)),
                    new XAttribute("dist", DrawingMlUnits.PointsToEmu(reflection.DistPt)),
                    new XAttribute("dir", (long)Math.Round(reflection.DirDeg * 60000)),
                    new XAttribute("sy", (long)Math.Round(reflection.ScaleY * 100000)),
                    new XAttribute("endPos", (long)Math.Round(Math.Clamp(reflection.EndPos, 0.0, 1.0) * 100000))));
            }

            if (run.TextSoftEdge is not null)
            {
                effectLst.Add(new XElement(A + "softEdge",
                    new XAttribute("rad", DrawingMlUnits.PointsToEmu(run.TextSoftEdge.RadiusPt))));
            }

            rPr.Add(effectLst);
        }

        // a:latin AFTER a:effectLst
        if (run.FontFamily is not null)
            rPr.Add(new XElement(A + "latin", new XAttribute("typeface", run.FontFamily)));

        // Run-level hyperlink — last
        if (run.Hyperlink is not null)
        {
            var hlinkEl = BuildHlinkClickEl(run.Hyperlink, hlinkRelIds, allSlides);
            if (hlinkEl is not null) rPr.Add(hlinkEl);
        }

        return new XElement(A + "r", rPr, new XElement(A + "t", run.Text));
    }

    private static IEnumerable<XAttribute>? BuildFieldRPrAttrs(FieldRun fld)
    {
        var attrs = new List<XAttribute>();
        if (fld.FontSizePt.HasValue)
            attrs.Add(new XAttribute("sz", (int)Math.Round(fld.FontSizePt.Value * 100)));
        if (fld.Bold)   attrs.Add(new XAttribute("b", "1"));
        if (fld.Italic) attrs.Add(new XAttribute("i", "1"));
        return attrs.Count > 0 ? attrs : null;
    }

    private static XElement? BuildFieldRPr(FieldRun fld)
    {
        var rPr = new XElement(A + "rPr", BuildFieldRPrAttrs(fld));
        if (fld.Color is { } color)
            rPr.Add(new XElement(A + "solidFill", BuildColorEl(new ThemeAwareColor(color))));
        if (!string.IsNullOrWhiteSpace(fld.FontFamily))
            rPr.Add(new XElement(A + "latin", new XAttribute("typeface", fld.FontFamily)));
        return rPr.HasAttributes || rPr.HasElements ? rPr : null;
    }

    private static XElement BuildPhEl(Placeholder ph)
    {
        var typeStr = ph.Type switch
        {
            PlaceholderType.Title => "title",
            PlaceholderType.CenteredTitle => "ctrTitle",
            PlaceholderType.SubTitle => "subTitle",
            PlaceholderType.Body => "body",
            PlaceholderType.DateTime => "dt",
            PlaceholderType.Footer => "ftr",
            PlaceholderType.SlideNumber => "sldNum",
            PlaceholderType.Header => "hdr",
            PlaceholderType.Object => "obj",
            PlaceholderType.Chart => "chart",
            PlaceholderType.Table => "tbl",
            PlaceholderType.ClipArt => "clipArt",
            PlaceholderType.Diagram => "dgm",
            PlaceholderType.Media => "media",
            PlaceholderType.Picture => "pic",
            _ => "body"
        };
        var el = new XElement(P + "ph", new XAttribute("type", typeStr));
        if (ph.Idx > 0) el.Add(new XAttribute("idx", ph.Idx));
        return el;
    }

    // ── Hyperlink rel collection ──────────────────────────────────────────────────

    /// <summary>
    /// Walks all shapes and runs in <paramref name="slide"/>, collects unique hyperlinks, assigns
    /// monotonically-increasing rel IDs ("rIdHlinkN"), and populates:
    /// <list type="bullet">
    ///   <item><paramref name="hlinkRelIds"/> — maps hyperlink key → rel ID (for use in BuildShapeEl/BuildRunEl)</item>
    ///   <item><paramref name="entries"/> — list of (relId, relType, target, isExternal) for the slide rels file</item>
    /// </list>
    /// For external URLs the rel type is the hyperlink rel type with TargetMode=External.
    /// For internal slide jumps the rel type is the slide rel type (no TargetMode).
    /// </summary>
    private static void CollectHyperlinkRels(
        Slide slide, List<Slide> allSlides, int slideIndex,
        Dictionary<string, string> hlinkRelIds,
        List<(string relId, string relType, string target, bool external)> entries)
    {
        int counter = 1;
        foreach (var shape in AllShapes(slide.Shapes))
        {
            if (shape.Hyperlink is not null)
                EnsureHlinkRel(shape.Hyperlink, allSlides, slideIndex, hlinkRelIds, entries, ref counter);

            if (shape.TextBody is not null)
            {
                foreach (var para in shape.TextBody.Paragraphs)
                    foreach (var run in para.Runs)
                        if (run.Hyperlink is not null)
                            EnsureHlinkRel(run.Hyperlink, allSlides, slideIndex, hlinkRelIds, entries, ref counter);
            }
        }
    }

    private static void EnsureHlinkRel(
        Hyperlink hlink, List<Slide> allSlides, int slideIndex,
        Dictionary<string, string> hlinkRelIds,
        List<(string relId, string relType, string target, bool external)> entries,
        ref int counter)
    {
        string key = HlinkKey(hlink, allSlides);
        if (string.IsNullOrEmpty(key)) return;
        if (hlinkRelIds.ContainsKey(key)) return; // already registered

        var relId = $"rIdHlink{counter++}";
        hlinkRelIds[key] = relId;

        if (hlink.Url is not null)
        {
            // External hyperlink — TargetMode="External"
            entries.Add((relId, HyperlinkRelType, hlink.Url, external: true));
        }
        else if (hlink.TargetSlideId is not null)
        {
            // Internal slide jump — find the target slide index.
            int targetIdx = allSlides.FindIndex(s => s.Id == hlink.TargetSlideId);
            string target = targetIdx >= 0
                ? $"../slides/slide{targetIdx + 1}.xml"
                : "../slides/slide1.xml"; // fallback
            entries.Add((relId, SlideRelType, target, external: false));
        }
    }

    // ── Media writing ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Writes all media (picture shapes + picture fill blips) for a slide.
    /// Returns two lists: one for picture shapes, one for fill blips (both as (shapeId, relId, mediaPath)).
    /// </summary>
    private static (
        List<(uint shapeId, string relId, string mediaPath)> pictureShapeMedia,
        List<(uint shapeId, string relId, string mediaPath)> fillBlipMedia)
    WriteSlideMedia(ZipArchive archive, Slide slide, int slideIndex)
    {
        var pictureResult = new List<(uint, string, string)>();
        var fillBlipResult = new List<(uint, string, string)>();
        int mediaIdx = 1;

        foreach (var shape in AllShapes(slide.Shapes))
        {
            // Picture shape OR media-shape poster image.
            if ((shape.Kind == SlideShapeKind.Picture || shape.Kind == SlideShapeKind.Media)
                && shape.Picture?.Bytes is { Length: > 0 } picBytes)
            {
                var ct = shape.Picture.ContentType ?? "image/png";
                var ext = OpcMediaTypes.GetDrawingMediaExtension(ct);
                var mediaPath = $"ppt/media/slide{slideIndex}_media{mediaIdx}.{ext}";

                var entry = archive.CreateEntry(mediaPath, CompressionLevel.Optimal);
                using (var es = entry.Open())
                    es.Write(picBytes);

                var relId = $"rIdMedia{mediaIdx}";
                pictureResult.Add((shape.Id, relId, mediaPath));
                mediaIdx++;
                continue;
            }

            // Picture fill on autoshape/connector
            if (shape.Fill is ShapeFill.Picture picFill && picFill.ImageBytes.Length > 0)
            {
                var ct = picFill.ContentType ?? "image/png";
                var ext = OpcMediaTypes.GetDrawingMediaExtension(ct);
                var mediaPath = $"ppt/media/slide{slideIndex}_media{mediaIdx}.{ext}";

                var entry = archive.CreateEntry(mediaPath, CompressionLevel.Optimal);
                using (var es = entry.Open())
                    es.Write(picFill.ImageBytes);

                var relId = $"rIdMedia{mediaIdx}";
                fillBlipResult.Add((shape.Id, relId, mediaPath));
                mediaIdx++;
            }
        }

        return (pictureResult, fillBlipResult);
    }

    private static List<(Paragraph paragraph, string relId, string mediaPath)> WriteSlideBulletImages(
        ZipArchive archive,
        Slide slide,
        int slideIndex,
        HashSet<string> usedRelIds)
    {
        var result = new List<(Paragraph, string, string)>();
        int mediaIdx = 1;
        int relIdx = 1;

        foreach (var paragraph in EnumerateSlideParagraphs(slide))
        {
            if (paragraph.BulletKind != BulletKind.Image ||
                paragraph.BulletImage?.Bytes is not { Length: > 0 } bytes)
            {
                continue;
            }

            var contentType = paragraph.BulletImage.ContentType ?? "image/png";
            var ext = OpcMediaTypes.GetDrawingMediaExtension(contentType);
            var mediaPath = $"ppt/media/slide{slideIndex}_bullet{mediaIdx}.{ext}";
            WriteRawEntry(archive, mediaPath, bytes);

            string relId;
            do
            {
                relId = $"rIdBulletImg{relIdx++}";
            }
            while (!usedRelIds.Add(relId));

            result.Add((paragraph, relId, mediaPath));
            mediaIdx++;
        }

        return result;
    }

    private static IEnumerable<Paragraph> EnumerateSlideParagraphs(Slide slide)
    {
        foreach (var shape in AllShapes(slide.Shapes))
        {
            if (shape.TextBody is not null)
            {
                foreach (var paragraph in shape.TextBody.Paragraphs)
                    yield return paragraph;
            }

            if (shape.Table is null)
                continue;

            foreach (var row in shape.Table.Rows)
            foreach (var cell in row.Cells)
            {
                if (cell.TextBody is null)
                    continue;

                foreach (var paragraph in cell.TextBody.Paragraphs)
                    yield return paragraph;
            }
        }
    }

    /// <summary>
    /// Writes audio/video bytes for Media shapes. Returns (shapeId, relId, mediaPath, isVideo) tuples.
    /// The relId uses prefix "rIdVid" to avoid collision with the "rIdMedia" image prefix.
    /// </summary>
    private static List<(uint shapeId, string relId, string mediaPath, bool isVideo)> WriteSlideMediaFiles(
        ZipArchive archive,
        Slide slide,
        int slideIndex,
        PptxPackageSnapshot? packageSnapshot,
        Dictionary<string, byte[]> writtenMediaPaths)
    {
        var result = new List<(uint, string, string, bool)>();
        int n = 1;

        foreach (var shape in AllShapes(slide.Shapes))
        {
            if (shape.Kind != SlideShapeKind.Media) continue;
            var media = shape.Media;
            if (media is null || media.Bytes.Length == 0) continue; // link-only: no file to write

            var ext = media.ContentType switch
            {
                "video/mp4"       => "mp4",
                "video/quicktime" => "mov",
                "video/x-msvideo" => "avi",
                "video/x-ms-wmv"  => "wmv",
                "audio/mpeg"      => "mp3",
                "audio/mp4"       => "m4a",
                "audio/wav"       => "wav",
                "audio/x-ms-wma"  => "wma",
                _                 => "mp4"
            };
            var mediaPath = TryGetPreservedMediaPackagePath(media, packageSnapshot, writtenMediaPaths, out var preservedPath)
                ? preservedPath
                : $"ppt/media/slide{slideIndex}_video{n}.{ext}";
            mediaPath = EnsureUniqueMediaPackagePath(mediaPath, writtenMediaPaths, media.Bytes);
            var relId = $"rIdVid{n}";

            if (!writtenMediaPaths.ContainsKey(mediaPath))
            {
                WriteRawEntry(archive, mediaPath, media.Bytes);
                writtenMediaPaths.Add(mediaPath, media.Bytes);
            }
            result.Add((shape.Id, relId, mediaPath, media.IsVideo));
            n++;
        }

        return result;
    }

    private static bool TryGetPreservedMediaPackagePath(
        MediaInfo media,
        PptxPackageSnapshot? packageSnapshot,
        IReadOnlyDictionary<string, byte[]> writtenMediaPaths,
        out string mediaPath)
    {
        mediaPath = string.Empty;
        if (packageSnapshot is null
            || !TryNormalizeInternalMediaPackagePath(media.SourcePackagePath, out var normalizedPath)
            || !packageSnapshot.TryGetEntry(normalizedPath, out var preservedBytes)
            || preservedBytes.Length == 0
            || media.Bytes.Length == 0
            || !media.Bytes.SequenceEqual(preservedBytes))
        {
            return false;
        }

        if (writtenMediaPaths.TryGetValue(normalizedPath, out var writtenBytes) &&
            !writtenBytes.SequenceEqual(preservedBytes))
        {
            return false;
        }

        mediaPath = normalizedPath;
        return true;
    }

    private static string EnsureUniqueMediaPackagePath(
        string mediaPath,
        IReadOnlyDictionary<string, byte[]> writtenMediaPaths,
        byte[] bytes)
    {
        if (!writtenMediaPaths.TryGetValue(mediaPath, out var writtenBytes) ||
            writtenBytes.SequenceEqual(bytes))
        {
            return mediaPath;
        }

        var extension = Path.GetExtension(mediaPath).TrimStart('.');
        if (string.IsNullOrWhiteSpace(extension))
            extension = "bin";

        var directory = GetDirectoryName(mediaPath);
        var fileName = Path.GetFileNameWithoutExtension(mediaPath);
        var suffix = 1;
        string candidate;
        do
        {
            candidate = $"{directory}/{fileName}_{suffix++}.{extension}";
        }
        while (writtenMediaPaths.ContainsKey(candidate));

        return candidate;
    }

    private static List<(uint shapeId, MediaCaptionTrackRelationship relationship, string target, bool isExternal)> WriteSlideMediaCaptionTracks(
        ZipArchive archive,
        Slide slide,
        int slideIndex,
        HashSet<string> usedRelIds,
        PptxPackageSnapshot? packageSnapshot,
        Dictionary<string, byte[]> writtenCaptionPaths)
    {
        var result = new List<(uint shapeId, MediaCaptionTrackRelationship relationship, string target, bool isExternal)>();
        var captionIndex = 1;

        foreach (var shape in AllShapes(slide.Shapes))
        {
            if (shape.Kind != SlideShapeKind.Media || shape.Media is null)
                continue;

            foreach (var track in shape.Media.CaptionTracks)
            {
                var isExternal = IsExternalCaptionTrack(track);
                if (isExternal)
                {
                    if (string.IsNullOrWhiteSpace(track.Source))
                        continue;

                    var externalRelId = ReserveCaptionRelationshipId(track, usedRelIds, captionIndex++);
                    var relationship = new MediaCaptionTrackRelationship(
                        externalRelId,
                        track.Language,
                        track.Label,
                        IsExternal: true);
                    result.Add((shape.Id, relationship, track.Source, isExternal: true));
                    continue;
                }

                if (!TryGetCaptionTrackBytes(track, packageSnapshot, out var bytes))
                    continue;

                var extension = GetCaptionTrackExtension(track);
                var relId = ReserveCaptionRelationshipId(track, usedRelIds, captionIndex);
                var captionPath = TryGetPreservedCaptionPackagePath(track, packageSnapshot, bytes, writtenCaptionPaths, out var preservedPath)
                    ? preservedPath
                    : $"ppt/media/slide{slideIndex}_caption{captionIndex}.{extension}";
                captionPath = EnsureUniqueCaptionPackagePath(captionPath, writtenCaptionPaths, bytes);
                captionIndex++;
                if (!writtenCaptionPaths.ContainsKey(captionPath))
                {
                    WriteRawEntry(archive, captionPath, bytes);
                    writtenCaptionPaths.Add(captionPath, bytes);
                }

                var internalRelationship = new MediaCaptionTrackRelationship(
                    relId,
                    track.Language,
                    track.Label,
                    IsExternal: false);
                result.Add((shape.Id, internalRelationship, MakeRelativePath($"ppt/slides/slide{slideIndex}.xml", captionPath), isExternal: false));
            }
        }

        return result;
    }

    private static string ReserveCaptionRelationshipId(
        MediaCaptionTrackInfo track,
        HashSet<string> usedRelIds,
        int preferredIndex)
    {
        if (!string.IsNullOrWhiteSpace(track.RelationshipId)
            && usedRelIds.Add(track.RelationshipId))
        {
            return track.RelationshipId;
        }

        return NextCaptionRelationshipId(usedRelIds, preferredIndex);
    }

    private static string NextCaptionRelationshipId(HashSet<string> usedRelIds, int preferredIndex)
    {
        var relId = $"rIdCaption{preferredIndex}";
        var suffix = 1;
        while (!usedRelIds.Add(relId))
        {
            relId = $"rIdCaption{preferredIndex}_{suffix++}";
        }

        return relId;
    }

    private static bool TryGetPreservedCaptionPackagePath(
        MediaCaptionTrackInfo track,
        PptxPackageSnapshot? packageSnapshot,
        byte[] bytes,
        IReadOnlyDictionary<string, byte[]> writtenCaptionPaths,
        out string captionPath)
    {
        captionPath = string.Empty;
        if (packageSnapshot is null
            || !TryNormalizeInternalCaptionPackagePath(track.Source, out var normalizedPath)
            || !packageSnapshot.TryGetEntry(normalizedPath, out var preservedBytes)
            || preservedBytes.Length == 0)
        {
            return false;
        }

        if (bytes.Length > 0 && !bytes.SequenceEqual(preservedBytes))
        {
            return false;
        }

        if (writtenCaptionPaths.TryGetValue(normalizedPath, out var writtenBytes) &&
            !writtenBytes.SequenceEqual(preservedBytes))
        {
            return false;
        }

        captionPath = normalizedPath;
        return true;
    }

    private static string EnsureUniqueCaptionPackagePath(
        string captionPath,
        IReadOnlyDictionary<string, byte[]> writtenCaptionPaths,
        byte[] bytes)
    {
        if (!writtenCaptionPaths.TryGetValue(captionPath, out var writtenBytes) ||
            writtenBytes.SequenceEqual(bytes))
        {
            return captionPath;
        }

        var extension = GetCaptionTrackExtension(captionPath);
        var directory = GetDirectoryName(captionPath);
        var fileName = Path.GetFileNameWithoutExtension(captionPath);
        var suffix = 1;
        string candidate;
        do
        {
            candidate = $"{directory}/{fileName}_{suffix++}.{extension}";
        }
        while (writtenCaptionPaths.ContainsKey(candidate));

        return candidate;
    }

    private static bool TryNormalizeInternalCaptionPackagePath(string? source, out string captionPath)
    {
        captionPath = string.Empty;
        if (string.IsNullOrWhiteSpace(source) || IsExternalCaptionTrackSource(source))
            return false;

        var normalized = ToZipEntryPath(source);
        if (string.IsNullOrWhiteSpace(normalized)
            || normalized.Split('/').Any(part => part is "." or "..")
            || !normalized.StartsWith("ppt/media/", StringComparison.OrdinalIgnoreCase)
            || GetCaptionTrackExtension(normalized) is not ("vtt" or "ttml" or "dfxp" or "srt"))
        {
            return false;
        }

        captionPath = normalized;
        return true;
    }

    private static bool TryNormalizeInternalMediaPackagePath(string? source, out string mediaPath)
    {
        mediaPath = string.Empty;
        if (string.IsNullOrWhiteSpace(source) || IsExternalCaptionTrackSource(source))
            return false;

        var normalized = ToZipEntryPath(source);
        if (string.IsNullOrWhiteSpace(normalized)
            || normalized.Split('/').Any(part => part is "." or "..")
            || !normalized.StartsWith("ppt/media/", StringComparison.OrdinalIgnoreCase)
            || GetAudioVideoExtension(normalized) is not ("mp4" or "mov" or "avi" or "wmv" or "mp3" or "m4a" or "wav" or "wma"))
        {
            return false;
        }

        mediaPath = normalized;
        return true;
    }

    private static bool TryGetCaptionTrackBytes(
        MediaCaptionTrackInfo track,
        PptxPackageSnapshot? packageSnapshot,
        out byte[] bytes)
    {
        if (track.Bytes.Length > 0)
        {
            bytes = track.Bytes;
            return true;
        }

        if (!string.IsNullOrWhiteSpace(track.Source)
            && !IsExternalCaptionTrack(track)
            && packageSnapshot is not null
            && packageSnapshot.TryGetEntry(track.Source, out bytes))
        {
            return bytes.Length > 0;
        }

        bytes = Array.Empty<byte>();
        return false;
    }

    private static bool IsExternalCaptionTrack(MediaCaptionTrackInfo track)
        => track.IsExternal
            || IsExternalCaptionTrackSource(track.Source);

    private static bool IsExternalCaptionTrackSource(string? source)
        => !string.IsNullOrWhiteSpace(source)
            && Uri.TryCreate(source, UriKind.Absolute, out var uri)
            && !string.IsNullOrWhiteSpace(uri.Scheme);

    private static string GetCaptionTrackExtension(MediaCaptionTrackInfo track)
    {
        var contentType = track.ContentType.Trim().ToLowerInvariant();
        if (contentType is "text/vtt")
            return "vtt";
        if (contentType is "application/ttml+xml" or "application/ttaf+xml")
            return "ttml";
        if (contentType is "application/x-subrip" or "text/srt")
            return "srt";

        var extension = GetCaptionTrackExtension(track.Source);
        return extension is "vtt" or "ttml" or "dfxp" or "srt"
            ? extension
            : "vtt";
    }

    private static string GetCaptionTrackExtension(string source)
    {
        if (string.IsNullOrWhiteSpace(source))
            return string.Empty;

        var end = source.AsSpan();
        var queryIndex = source.IndexOfAny(['?', '#']);
        if (queryIndex >= 0)
            end = source.AsSpan(0, queryIndex);

        var slashIndex = end.LastIndexOf('/');
        var fileName = slashIndex >= 0 ? end[(slashIndex + 1)..] : end;
        var dotIndex = fileName.LastIndexOf('.');
        return dotIndex >= 0 && dotIndex < fileName.Length - 1
            ? fileName[(dotIndex + 1)..].ToString().ToLowerInvariant()
            : string.Empty;
    }

    private static string GetAudioVideoExtension(string source)
    {
        if (string.IsNullOrWhiteSpace(source))
            return string.Empty;

        var end = source.AsSpan();
        var queryIndex = source.IndexOfAny(['?', '#']);
        if (queryIndex >= 0)
            end = source.AsSpan(0, queryIndex);

        var slashIndex = end.LastIndexOf('/');
        var fileName = slashIndex >= 0 ? end[(slashIndex + 1)..] : end;
        var dotIndex = fileName.LastIndexOf('.');
        return dotIndex >= 0 && dotIndex < fileName.Length - 1
            ? fileName[(dotIndex + 1)..].ToString().ToLowerInvariant()
            : string.Empty;
    }

    private static bool TryGetPackageDefaultContentType(string extension, out string contentType)
    {
        if (OpcMediaTypes.TryGetDefaultContentType(extension, out contentType!))
            return true;

        contentType = extension.TrimStart('.').ToLowerInvariant() switch
        {
            "vtt" => "text/vtt",
            "ttml" or "dfxp" => "application/ttml+xml",
            "srt" => "application/x-subrip",
            _ => string.Empty
        };

        return contentType.Length > 0;
    }

    private static void WriteRawEntry(ZipArchive archive, string path, byte[] bytes)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.NoCompression);
        using var stream = entry.Open();
        stream.Write(bytes, 0, bytes.Length);
    }

    // ── Chart writing ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Writes chart parts for all Chart shapes in the slide. Uses and increments
    /// <paramref name="globalChartIndex"/> so chart file names are unique across slides.
    /// Returns (shapeName, relId, chartPartPath) tuples for wiring into slide rels.
    /// </summary>
    private static List<(uint shapeId, string relId, string chartPath, string chartRelType)> WriteSlideCharts(
        ZipArchive archive,
        Slide slide,
        ref int globalChartIndex,
        PptxPackageSnapshot? packageSnapshot)
    {
        var result = new List<(uint, string, string, string)>();

        foreach (var shape in AllShapes(slide.Shapes))
        {
            if (shape.Kind != SlideShapeKind.Chart || shape.Chart is null)
                continue;

            var chartPath = shape.Chart.IsChartEx
                ? PptxChartWriter.WriteChartExPart(archive, shape.Chart, globalChartIndex, packageSnapshot)
                : PptxChartWriter.WriteChartPart(archive, shape.Chart, globalChartIndex, packageSnapshot);
            var relId = $"rIdChart{globalChartIndex}";
            var chartRelType = shape.Chart.IsChartEx ? ChartExRelType : ChartRelType;
            result.Add((shape.Id, relId, chartPath, chartRelType));
            globalChartIndex++;
        }

        return result;
    }

    // ── SmartArt diagram part writing ─────────────────────────────────────────────

    /// <summary>
    /// Writes all diagram parts (data/layout/quickStyle/colors/drawing) for SmartArt shapes
    /// verbatim from the stored raw bytes. Also writes each part's rels file (if any).
    /// Returns:
    ///   - slideRels: (newRelId, relType, target) tuples for the slide rels file.
    ///   - relIdRemap: per-shape (shape.Id → (key → newRelId)) so BuildSmartArtGraphicFrameEl
    ///     can emit only the r: attributes that have an actual written part (S2),
    ///     using fresh collision-free relIds (S4).
    /// PowerPoint-authored SmartArt stores the drawing cache relationship in slide rels.
    /// </summary>
    private static (
        List<(string relId, string relType, string target)> slideRels,
        Dictionary<uint, Dictionary<string, string>> relIdRemap)
        WriteSlideSmartArt(ZipArchive archive, Slide slide, HashSet<string> usedRelIds)
    {
        var slideRels  = new List<(string, string, string)>();
        var relIdRemap = new Dictionary<uint, Dictionary<string, string>>();

        // Track parts already written (a single part may be referenced by multiple shapes)
        var writtenParts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var relTypeForKey = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["dm"] = DiagramDataRelType,
            ["lo"] = DiagramLayoutRelType,
            ["qs"] = DiagramQuickStyleRelType,
            ["cs"] = DiagramColorsRelType
        };

        // S4: counter for fresh diagram relIds that don't collide with layout/media/chart ids.
        int dgmRelIdCounter = 1;
        string AllocDgmRelId()
        {
            string id;
            do { id = $"rIdDgm{dgmRelIdCounter++}"; } while (usedRelIds.Contains(id));
            usedRelIds.Add(id);
            return id;
        }

        foreach (var shape in AllShapes(slide.Shapes))
        {
            if (shape.Kind != SlideShapeKind.SmartArt || shape.SmartArt is not { } smart)
                continue;

            // Write each raw diagram part that hasn't been written yet
            foreach (var part in smart.Parts.Values)
            {
                if (string.IsNullOrEmpty(part.PartPath) || part.Bytes.Length == 0) continue;
                if (!writtenParts.Add(part.PartPath)) continue; // already written

                var entry = archive.CreateEntry(part.PartPath, CompressionLevel.Optimal);
                using (var es = entry.Open())
                    es.Write(part.Bytes);

                // Write this part's rels file if we have it
                if (smart.PartRels.TryGetValue(part.PartPath, out var relsBytes) && relsBytes.Length > 0)
                {
                    var partDir  = GetDirectoryName(part.PartPath);
                    var partFile = part.PartPath[(part.PartPath.LastIndexOf('/') + 1)..];
                    var relsPath = string.IsNullOrEmpty(partDir)
                        ? $"_rels/{partFile}.rels"
                        : $"{partDir}/_rels/{partFile}.rels";

                    if (!writtenParts.Contains(relsPath))
                    {
                        writtenParts.Add(relsPath);
                        var relsEntry = archive.CreateEntry(relsPath, CompressionLevel.Optimal);
                        using var re = relsEntry.Open();
                        re.Write(relsBytes);
                    }
                }
            }

            // Build slide rels for the four named diagram parts (dm/lo/qs/cs).
            // S2: skip keys whose part was not written (FindDiagramPartPathForKey returns null).
            // S3: compute the correct relative path from ppt/slides/ to the actual part location.
            // S4: allocate a fresh relId to avoid collision with rId1/media/chart ids.
            var shapeRemap = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var (key, _) in smart.DiagramRelIds)
            {
                if (!relTypeForKey.TryGetValue(key, out var relType)) continue;

                var partPath = FindDiagramPartPathForKey(smart, key);
                if (partPath is null) continue; // S2: part missing — skip this key

                // S3: compute rel target as correct relative path from ppt/slides/ to partPath.
                // partPath is like "ppt/diagrams/data1.xml"; slides live in "ppt/slides/".
                // Relative path: go up from ppt/slides/ -> ppt/ (one ".."), then follow partPath
                // relative to ppt/. E.g. "ppt/diagrams/data1.xml" -> "../diagrams/data1.xml".
                var partPathFromPpt = partPath.StartsWith("ppt/", StringComparison.OrdinalIgnoreCase)
                    ? partPath["ppt/".Length..]   // "diagrams/data1.xml"
                    : partPath;
                var target = $"../{partPathFromPpt}";

                // S4: reuse existing relId if this exact part was already registered (shared
                // part across shapes), otherwise allocate a fresh collision-free relId.
                var existing = slideRels.FirstOrDefault(r => r.Item3 == target && r.Item2 == relType);
                string newRelId;
                if (existing != default)
                {
                    newRelId = existing.Item1; // reuse the already-assigned id
                }
                else
                {
                    newRelId = AllocDgmRelId();
                    slideRels.Add((newRelId, relType, target));
                }

                shapeRemap[key] = newRelId;
            }

            if (!string.IsNullOrWhiteSpace(smart.DrawingPartPath)
                && smart.Parts.ContainsKey(smart.DrawingPartPath))
            {
                var partPathFromPpt = smart.DrawingPartPath.StartsWith("ppt/", StringComparison.OrdinalIgnoreCase)
                    ? smart.DrawingPartPath["ppt/".Length..]
                    : smart.DrawingPartPath;
                var target = $"../{partPathFromPpt}";
                var existing = slideRels.FirstOrDefault(r => r.Item3 == target && r.Item2 == DiagramDrawingRelType);

                if (existing == default)
                    slideRels.Add((AllocDgmRelId(), DiagramDrawingRelType, target));
            }

            // S2: only register the shape in the remap if dm is present (required for rendering)
            if (shapeRemap.ContainsKey("dm"))
                relIdRemap[shape.Id] = shapeRemap;
        }

        return (slideRels, relIdRemap);
    }

    /// <summary>
    /// Heuristic: map a diagram key ("dm"/"lo"/"qs"/"cs") to a part path by matching
    /// the part's content type keyword.
    /// </summary>
    private static string? FindDiagramPartPathForKey(SmartArtShape smart, string key)
    {
        var ctKeyword = key switch
        {
            "dm" => "diagramData",
            "lo" => "diagramLayout",
            "qs" => "diagramStyle",
            "cs" => "diagramColors",
            _    => null
        };
        if (ctKeyword is null) return null;

        // Try content-type match first
        var match = smart.Parts.Values
            .FirstOrDefault(p => p.ContentType.Contains(ctKeyword, StringComparison.OrdinalIgnoreCase));
        if (match is not null) return match.PartPath;

        // Fallback: filename keyword match
        var nameKeyword = key switch
        {
            "dm" => "data",
            "lo" => "layout",
            "qs" => "quickStyle",
            "cs" => "colors",
            _    => null
        };
        if (nameKeyword is null) return null;

        return smart.Parts.Keys
            .FirstOrDefault(p => p.Contains(nameKeyword, StringComparison.OrdinalIgnoreCase));
    }

    // ── Wave 25A: Preserved modern objects (zoom / ink / 3D / unknown) ─────────────

    /// <summary>
    /// Builds a relative OPC path from a slide's absolute path to an absolute target part path.
    /// E.g. slide="ppt/slides/slide1.xml", target="ppt/media/foo.glb" → "../media/foo.glb"
    /// </summary>
    private static string MakeRelativePath(string slidePath, string targetPath)
    {
        // Normalize: ensure no leading slash
        slidePath  = slidePath.TrimStart('/');
        targetPath = targetPath.TrimStart('/');

        var slideDir  = GetDirectoryName(slidePath);
        var targetDir = GetDirectoryName(targetPath);
        var fileName  = targetPath[(targetPath.LastIndexOf('/') + 1)..];

        // Count how many levels up we need to go from slideDir
        var slideParts  = slideDir.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var targetParts = targetDir.Split('/', StringSplitOptions.RemoveEmptyEntries);

        // Find common prefix length
        int common = 0;
        while (common < slideParts.Length && common < targetParts.Length &&
               string.Equals(slideParts[common], targetParts[common], StringComparison.OrdinalIgnoreCase))
            common++;

        var ups   = string.Join("/", Enumerable.Repeat("..", slideParts.Length - common));
        var down  = string.Join("/", targetParts[common..]);
        var parts = new[] { ups, down, fileName }.Where(s => !string.IsNullOrEmpty(s));
        return string.Join("/", parts);
    }

    /// <summary>
    /// Writes all OPC parts for preserved modern objects on the slide.
    /// Returns:
    ///   prvRels: (shapeId, oldRelId, newRelId, relType, absoluteTargetPath)
    ///   relIdPatch: unused (patch happens via the collision-free (shapeId, oldRelId) key —
    ///   see BUG EA4: prvRelIdPatch dictionary built by the caller from prvRels)
    /// </summary>
    private static (
        List<(uint shapeId, string oldRelId, string newRelId, string relType, string targetPath)> prvRels,
        bool unused
    ) WriteSlidePreservedObjects(ZipArchive archive, Slide slide, int slideIdx, HashSet<string> usedRelIds)
    {
        var prvRels    = new List<(uint, string, string, string, string)>();
        var relCounter = 1;
        var partCounter = 1;

        string NextRelId()
        {
            string id;
            do { id = $"rIdPrv{relCounter++}"; } while (usedRelIds.Contains(id));
            usedRelIds.Add(id);
            return id;
        }

        // Track written part paths to avoid duplicate zip entries
        var writtenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var shape in AllShapes(slide.Shapes))
        {
            if (shape.PreservedObject is not { } info) continue;

            // Write each referenced OPC part that isn't already in the archive
            var pathRemap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in info.Parts)
            {
                var origPath = kv.Key;
                if (pathRemap.ContainsKey(origPath)) continue;

                var ext       = origPath.Contains('.') ? origPath[(origPath.LastIndexOf('.') + 1)..] : "bin";
                var freshPath = origPath;
                // If the path is already written (e.g. by another slide/shape), reindex
                if (writtenPaths.Contains(freshPath))
                {
                    freshPath = $"ppt/media/preserved_{slideIdx}_{partCounter++}.{ext}";
                }

                if (!writtenPaths.Contains(freshPath))
                {
                    var entry = archive.CreateEntry(freshPath, CompressionLevel.Optimal);
                    using (var s = entry.Open())
                        s.Write(kv.Value, 0, kv.Value.Length);
                    writtenPaths.Add(freshPath);
                }
                pathRemap[origPath] = freshPath;
            }

            // Write part rels files (use writtenPaths to avoid duplicate zip entries in Create mode)
            foreach (var kv in info.PartRels)
            {
                var origPath = kv.Key;
                if (!pathRemap.TryGetValue(origPath, out var freshPath)) freshPath = origPath;
                var relsPath = MakePartRelsPath(freshPath);
                if (!writtenPaths.Contains(relsPath))
                {
                    var rEntry = archive.CreateEntry(relsPath, CompressionLevel.Optimal);
                    using var s = rEntry.Open();
                    s.Write(kv.Value, 0, kv.Value.Length);
                    writtenPaths.Add(relsPath);
                }
            }

            // Allocate fresh slide-rel entries for each SlideRels entry on this shape
            foreach (var kv in info.SlideRels)
            {
                var origPath = kv.Value.TargetPath;
                if (!pathRemap.TryGetValue(origPath, out var freshPath)) freshPath = origPath;
                var newRelId = NextRelId();
                prvRels.Add((shape.Id, kv.Key, newRelId, kv.Value.RelType, freshPath));
            }

            // Write the fallback image (shape.Picture) if present
            if (shape.Picture is { Bytes.Length: > 0 } pic)
            {
                var imgExt    = OpcMediaTypes.GetDrawingMediaExtension(pic.ContentType ?? "image/png");
                var imgPath   = $"ppt/media/preservedImg{slideIdx}_{shape.Id}.{imgExt}";
                if (!writtenPaths.Contains(imgPath))
                {
                    var imgEntry = archive.CreateEntry(imgPath, CompressionLevel.Optimal);
                    using var s  = imgEntry.Open();
                    s.Write(pic.Bytes, 0, pic.Bytes.Length);
                    writtenPaths.Add(imgPath);
                }
                var imgRelId = NextRelId();
                prvRels.Add((shape.Id, $"__img__{shape.Id}", imgRelId, ImageRelType, imgPath));
            }
        }

        return (prvRels, false);
    }

    private static string MakePartRelsPath(string partPath)
    {
        return OpcPathHelper.GetRelationshipPartPath(partPath);
    }

    /// <summary>
    /// Builds the slide element for a preserved modern object by re-emitting RawXml
    /// with rel-id attributes patched to the freshly allocated rIds (via
    /// <paramref name="prvRelIdByShapeAndOldId"/>).
    /// </summary>
    /// <param name="prvRelIdByShapeAndOldId">
    /// BUG EA4 fix: collision-free (shapeId, oldRelId) -&gt; newRelId patch map. The previous
    /// implementation packed shapeId's LOW 8 BITS together with a 21-bit hash of oldRelId into a
    /// single uint key shared with the `mediaById` dictionary; two preserved shapes on the same
    /// slide whose cNvPr ids shared a low byte (e.g. 5 and 261 — 261 &amp; 0xFF == 5) and which both
    /// referenced the same old rId string (e.g. both "rId2", each to a DIFFERENT media part)
    /// collided on that packed key, so the second shape's dictionary write silently overwrote the
    /// first's, cross-wiring one shape's rId to the OTHER shape's media (or leaving a dangling
    /// reference) — both cause a PowerPoint "needs repair" prompt. The fix keys directly on the
    /// real (uint shapeId, string oldRelId) tuple — no hashing, no bit-packing, no collisions.
    /// </param>
    private static XElement? BuildPreservedObjectEl(
        SlideShape shape, Dictionary<(uint shapeId, string oldRelId), string>? prvRelIdByShapeAndOldId,
        Dictionary<string, string>? hlinkRelIds = null, List<Slide>? allSlides = null)
    {
        if (shape.PreservedObject is not { } info) return null;
        if (string.IsNullOrWhiteSpace(info.RawXml)) return null;

        XElement el;
        try { el = XElement.Parse(info.RawXml); }
        catch { return null; }

        // Preserved modern objects keep their native XML, but their position, size,
        // rotation, and flips remain ordinary SlideShape state so canvas transforms
        // can edit them. Synchronize only a recognized root transform; payloads
        // without one remain verbatim by design.
        SynchronizePreservedTransform(el, shape);
        SynchronizePreservedNonVisualProperties(el, shape);

        // Patch r-namespace id attributes to use the fresh relIds
        var rNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        foreach (var attr in el.DescendantsAndSelf()
                                .SelectMany(e => e.Attributes())
                                .Where(a => a.Name.NamespaceName == rNs)
                                .ToList())
        {
            var oldId = attr.Value;
            if (prvRelIdByShapeAndOldId is not null &&
                prvRelIdByShapeAndOldId.TryGetValue((shape.Id, oldId), out var newId))
                attr.SetValue(newId);
        }

        // Preserved graphic frames keep their original cNvPr subtree. Refresh only the
        // shape-level hyperlink so its relationship id follows the current slide rels.
        if (shape.Hyperlink is not null)
        {
            var cNvPr = el.Descendants(P + "cNvPr").FirstOrDefault();
            if (cNvPr is not null)
            {
                cNvPr.Element(A + "hlinkClick")?.Remove();
                var hlink = BuildHlinkClickEl(shape.Hyperlink, hlinkRelIds, allSlides);
                if (hlink is not null)
                    cNvPr.Add(hlink);
            }
        }

        // EA3/FA2: Re-wrap in mc:AlternateContent if the original was wrapped.
        // Use the original Requires token(s) (+ their namespace URI(s)) verbatim — do NOT
        // hardcode "p14". XElement.ToString() drops xmlns declarations for prefixes that appear
        // only in attribute values (like Requires="p14"), so we must explicitly declare
        // xmlns:xxx on the wrapper.
        //
        // FA2: Requires may be a SPACE-SEPARATED list of tokens (mc:AlternateContent permits
        // Requires="p14 p15"). The old code did `new XAttribute(XNamespace.Xmlns + requiresToken, ...)`
        // with the RAW (possibly multi-token) string as the xmlns local-name, which is not a
        // valid XML name when it contains a space -> XmlException on serialization, failing the
        // save entirely. We now split on whitespace and declare one xmlns per token, resolving
        // each token's URI individually via McRequiresNsUris. A token whose URI is unknown does
        // NOT get the p14 URI forced onto it (that would be a wrong binding for a non-p14
        // prefix) — its xmlns declaration is simply omitted; the Requires attribute still lists
        // the token so a reader with knowledge of that prefix elsewhere in the package can still
        // resolve it, but if NO token resolves to a URI we bail out and preserve the original
        // element without re-wrapping (better than emitting a broken/ambiguous wrapper).
        if (info.WasAlternateContent)
        {
            var requiresToken = info.McRequiresToken ?? "p14";
            var tokens = requiresToken.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0) tokens = new[] { requiresToken };

            var choiceAttrs = new List<object> { new XAttribute("Requires", requiresToken) };
            int resolvedCount = 0;
            foreach (var token in tokens)
            {
                string? uri = null;
                if (info.McRequiresNsUris.TryGetValue(token, out var mappedUri))
                    uri = mappedUri;
                else if (tokens.Length == 1 && info.McRequiresNsUri is not null)
                    uri = info.McRequiresNsUri; // single-token back-compat fallback
                else if (KnownMcPrefixNsUris.TryGetValue(token, out var knownUri))
                    uri = knownUri; // last resort: well-known MS prefix table

                if (uri is not null)
                {
                    choiceAttrs.Add(new XAttribute(XNamespace.Xmlns + token, uri));
                    resolvedCount++;
                }
                // else: omit this token's xmlns — do NOT force an unrelated URI onto it.
            }

            if (resolvedCount == 0)
            {
                // No token resolved to any namespace URI — re-wrapping would produce an
                // AlternateContent with an unusable Choice (Requires references prefixes with
                // no xmlns binding at all). Preserve the original element verbatim instead.
                return el;
            }

            XElement clone;
            try
            {
                clone = string.IsNullOrWhiteSpace(info.AlternateContentFallbackXml)
                    ? new XElement(el)
                    : XElement.Parse(info.AlternateContentFallbackXml);
            }
            catch
            {
                clone = new XElement(el);
            }
            SynchronizePreservedTransform(clone, shape);
            SynchronizePreservedNonVisualProperties(clone, shape);
            return new XElement(MC + "AlternateContent",
                new XAttribute(XNamespace.Xmlns + "mc",
                    "http://schemas.openxmlformats.org/markup-compatibility/2006"),
                new XElement(MC + "Choice", choiceAttrs.Cast<object>().ToArray<object>().Concat(new object[] { el }).ToArray()),
                new XElement(MC + "Fallback",
                    clone));
        }

        return el;
    }

    /// <summary>
    /// Synchronizes model-owned non-visual properties on preserved modern objects.
    /// Preserved payloads are retained verbatim, but these cNvPr fields are ordinary
    /// editable shape state just like the transform and hyperlink above. AlternateContent
    /// choice/fallback branches are both updated so the next reader sees the same state.
    /// </summary>
    private static void SynchronizePreservedNonVisualProperties(XElement root, SlideShape shape)
    {
        foreach (var cNvPr in root.Descendants(P + "cNvPr").ToList())
        {
            cNvPr.SetAttributeValue("id", shape.Id);
            cNvPr.SetAttributeValue("name", shape.Name);

            if (shape.IsHidden)
                cNvPr.SetAttributeValue("hidden", "1");
            else
                cNvPr.Attribute("hidden")?.Remove();

            if (!string.IsNullOrWhiteSpace(shape.AlternativeTextTitle))
                cNvPr.SetAttributeValue("title", shape.AlternativeTextTitle.Trim());
            else
                cNvPr.Attribute("title")?.Remove();

            if (!string.IsNullOrWhiteSpace(shape.AlternativeText))
                cNvPr.SetAttributeValue("descr", shape.AlternativeText.Trim());
            else
                cNvPr.Attribute("descr")?.Remove();

            SynchronizePreservedDecorativeState(cNvPr, shape.IsDecorative);
        }
    }

    private static void SynchronizePreservedDecorativeState(XElement cNvPr, bool isDecorative)
    {
        var extLst = cNvPr.Element(A + "extLst");
        if (extLst is not null)
        {
            extLst.Elements(A + "ext")
                .Where(ext => ext.Descendants(Adec + "decorative").Any())
                .Remove();

            if (!extLst.Elements().Any())
                extLst.Remove();
        }

        if (!isDecorative)
            return;

        extLst = cNvPr.Element(A + "extLst");
        if (extLst is null)
        {
            extLst = new XElement(A + "extLst");
            cNvPr.Add(extLst);
        }

        extLst.Add(new XElement(A + "ext",
            new XAttribute("uri", DecorativeExtUri),
            new XElement(Adec + "decorative",
                NsAttr("adec", Adec),
                new XAttribute("val", "1"))));
    }

    private static void SynchronizePreservedTransform(XElement root, SlideShape shape)
    {
        XElement? xfrm = root.Name == P + "graphicFrame"
            ? root.Element(P + "xfrm")
            : root.Name == P + "sp"
                ? root.Element(P + "spPr")?.Element(A + "xfrm")
                : null;
        if (xfrm is null)
            return;

        if (Math.Abs(shape.RotationDeg) > 0.0001)
            xfrm.SetAttributeValue("rot", (long)Math.Round(shape.RotationDeg * 60000));
        else
            xfrm.Attribute("rot")?.Remove();

        if (shape.FlipH)
            xfrm.SetAttributeValue("flipH", "1");
        else
            xfrm.Attribute("flipH")?.Remove();
        if (shape.FlipV)
            xfrm.SetAttributeValue("flipV", "1");
        else
            xfrm.Attribute("flipV")?.Remove();

        var off = xfrm.Element(A + "off") ?? new XElement(A + "off");
        off.SetAttributeValue("x", shape.OffsetXEmu);
        off.SetAttributeValue("y", shape.OffsetYEmu);
        if (off.Parent is null)
            xfrm.AddFirst(off);

        var ext = xfrm.Element(A + "ext") ?? new XElement(A + "ext");
        ext.SetAttributeValue("cx", shape.ExtentCxEmu);
        ext.SetAttributeValue("cy", shape.ExtentCyEmu);
        if (ext.Parent is null)
            xfrm.Add(ext);
    }

    /// <summary>
    /// FA2: well-known mc:AlternateContent Requires prefixes and their namespace URIs, used as a
    /// last-resort fallback when a token's URI could not be resolved from the source document's
    /// xmlns scope (e.g. an older captured PreservedObjectInfo without McRequiresNsUris populated).
    /// Never used to override a URI that WAS captured on read.
    /// </summary>
    private static readonly Dictionary<string, string> KnownMcPrefixNsUris = new(StringComparer.Ordinal)
    {
        ["p14"]  = "http://schemas.microsoft.com/office/powerpoint/2010/main",
        ["p15"]  = "http://schemas.microsoft.com/office/powerpoint/2012/main",
        ["p159"] = "http://schemas.microsoft.com/office/powerpoint/2015/09/main",
        ["p188"] = "http://schemas.microsoft.com/office/powerpoint/2018/8/main",
        ["a14"]  = "http://schemas.microsoft.com/office/drawing/2010/main",
        ["am3d"] = "http://schemas.microsoft.com/office/drawing/2017/model3d",
    };

    // ── OLE embedded object writing (Theme 21) ────────────────────────────────────

    /// <summary>
    /// Writes all OLE embedded object binaries and fallback images for a slide.
    /// Returns two lists:
    ///   embRels: (shapeId, embRelId, embRelType, embPath) — one per embedded binary
    ///   imgRels: (shapeId, imgRelId, imgPath)             — one per fallback image
    /// rel IDs are allocated from a monotonically increasing counter, avoiding conflicts
    /// with the <paramref name="usedRelIds"/> set.
    /// </summary>
    private static (
        List<(uint shapeId, string embRelId, string embRelType, string embPath)> embRels,
        List<(uint shapeId, string imgRelId, string imgPath)> imgRels)
    WriteSlideOleObjects(ZipArchive archive, Slide slide, int slideIdx, HashSet<string> usedRelIds)
    {
        var embRels = new List<(uint, string, string, string)>();
        var imgRels = new List<(uint, string, string)>();

        int embCounter = 1;
        int relCounter = 1;

        string NextRelId()
        {
            string id;
            do { id = $"rIdOle{relCounter++}"; } while (usedRelIds.Contains(id));
            usedRelIds.Add(id);
            return id;
        }

        foreach (var shape in AllShapes(slide.Shapes))
        {
            if (shape.Kind != SlideShapeKind.Ole || shape.OleObject is not { } ole)
                continue;

            // ── Write embedded binary ──────────────────────────────────────────
            if (ole.EmbeddedBytes.Length > 0)
            {
                var ext = string.IsNullOrWhiteSpace(ole.EmbeddedExtension)
                    ? "bin" : ole.EmbeddedExtension;
                var embPath = $"ppt/embeddings/oleObject{embCounter++}.{ext}";
                var embEntry = archive.CreateEntry(embPath, CompressionLevel.Optimal);
                using (var s = embEntry.Open())
                    s.Write(ole.EmbeddedBytes, 0, ole.EmbeddedBytes.Length);

                var relType = string.IsNullOrWhiteSpace(ole.RelType) ? PackageRelType : ole.RelType;
                var embRelId = NextRelId();
                embRels.Add((shape.Id, embRelId, relType, embPath));
            }

            // ── Write fallback image ───────────────────────────────────────────
            if (shape.Picture is { Bytes.Length: > 0 } pic)
            {
                var imgExt = OpcMediaTypes.GetDrawingMediaExtension(pic.ContentType ?? "image/png");
                var imgPath = $"ppt/media/oleImg{slideIdx}_{shape.Id}.{imgExt}";
                var imgEntry = archive.CreateEntry(imgPath, CompressionLevel.Optimal);
                using (var s = imgEntry.Open())
                    s.Write(pic.Bytes, 0, pic.Bytes.Length);

                var imgRelId = NextRelId();
                imgRels.Add((shape.Id, imgRelId, imgPath));
            }
        }

        return (embRels, imgRels);
    }

    // ── OLE graphicFrame element building (Theme 21) ──────────────────────────────

    private const string DrawingOleUri =
        "http://schemas.openxmlformats.org/presentationml/2006/ole";

    /// <summary>
    /// Builds the p:graphicFrame element (or mc:AlternateContent wrapper) for an OLE shape.
    /// Uses:
    ///   mediaById[shape.Id]                — the r:id for the embedded binary
    ///   mediaById[shape.Id | 0x40000000u]  — the r:id for the fallback image
    /// Deserializes the stored OleObjXml verbatim and re-inserts the fallback p:pic child.
    /// </summary>
    private static XElement? BuildOleGraphicFrameEl(
        SlideShape shape, Dictionary<uint, string> mediaById,
        Dictionary<string, string>? hlinkRelIds = null, List<Slide>? allSlides = null)
    {
        if (shape.OleObject is not { } ole) return null;

        mediaById.TryGetValue(shape.Id, out var embRelId);
        mediaById.TryGetValue(shape.Id | 0x40000000u, out var imgRelId);

        // Build the transform (xfrm) element
        var xfrm = new XElement(P + "xfrm",
            new XElement(A + "off",
                new XAttribute("x", shape.OffsetXEmu),
                new XAttribute("y", shape.OffsetYEmu)),
            new XElement(A + "ext",
                new XAttribute("cx", shape.ExtentCxEmu),
                new XAttribute("cy", shape.ExtentCyEmu)));

        // Parse the stored oleObj XML back into an XElement and patch the r:id
        XElement oleObjEl;
        try
        {
            oleObjEl = XElement.Parse(ole.OleObjXml);
        }
        catch
        {
            // Malformed stored XML — build a minimal oleObj
            oleObjEl = new XElement(P + "oleObj",
                new XAttribute("progId", ole.ProgId));
        }

        // Patch (or add) the r:id attribute so it references the freshly-written embedded binary
        if (!string.IsNullOrWhiteSpace(embRelId))
        {
            // Remove any existing r:id to avoid duplicates, then add the fresh one
            oleObjEl.Attribute(XNamespace.Get("http://schemas.openxmlformats.org/officeDocument/2006/relationships") + "id")?.Remove();
            oleObjEl.SetAttributeValue(
                XName.Get("id", "http://schemas.openxmlformats.org/officeDocument/2006/relationships"),
                embRelId);
        }

        // Append the fallback p:pic child (rebuilt from shape.Picture + imgRelId)
        if (!string.IsNullOrWhiteSpace(imgRelId) && shape.Picture is { Bytes.Length: > 0 })
        {
            oleObjEl.Add(BuildOleFallbackPicEl(shape, imgRelId));
        }

        // Build the p:graphicFrame
        var graphicFrame = new XElement(P + "graphicFrame",
            new XElement(P + "nvGraphicFramePr",
                CnvPrWithHlink(shape, hlinkRelIds, allSlides),
                new XElement(P + "cNvGraphicFramePr"),
                new XElement(P + "nvPr")),
            xfrm,
            new XElement(A + "graphic",
                new XElement(A + "graphicData",
                    new XAttribute("uri", DrawingOleUri),
                    oleObjEl)));

        // Re-wrap in mc:AlternateContent if the original was wrapped
        if (ole.WasAlternateContent)
        {
            // XLinq: an XElement can only live in one parent, so the Fallback gets a deep clone.
            return new XElement(MC + "AlternateContent",
                new XAttribute(XNamespace.Xmlns + "mc",
                    "http://schemas.openxmlformats.org/markup-compatibility/2006"),
                new XElement(MC + "Choice",
                    new XAttribute("Requires", "p14"),
                    graphicFrame),
                new XElement(MC + "Fallback",
                    new XElement(graphicFrame)));   // deep clone for the Fallback branch
        }

        return graphicFrame;
    }

    /// <summary>
    /// Builds a minimal p:pic element used as the OLE fallback preview inside p:oleObj.
    /// </summary>
    private static XElement BuildOleFallbackPicEl(SlideShape shape, string imgRelId)
    {
        var R2 = XNamespace.Get("http://schemas.openxmlformats.org/officeDocument/2006/relationships");
        return new XElement(P + "pic",
            new XElement(P + "nvPicPr",
                new XElement(P + "cNvPr",
                    new XAttribute("id", shape.Id + 1u),
                    new XAttribute("name", $"{shape.Name}_img")),
                new XElement(P + "cNvPicPr"),
                new XElement(P + "nvPr")),
            new XElement(P + "blipFill",
                new XElement(A + "blip",
                    new XAttribute(R2 + "embed", imgRelId)),
                new XElement(A + "stretch",
                    new XElement(A + "fillRect"))),
            new XElement(P + "spPr",
                new XElement(A + "xfrm",
                    new XElement(A + "off",
                        new XAttribute("x", shape.OffsetXEmu),
                        new XAttribute("y", shape.OffsetYEmu)),
                    new XElement(A + "ext",
                        new XAttribute("cx", shape.ExtentCxEmu),
                        new XAttribute("cy", shape.ExtentCyEmu))),
                new XElement(A + "prstGeom",
                    new XAttribute("prst", "rect"),
                    new XElement(A + "avLst"))));
    }

    // ── Zip helpers ───────────────────────────────────────────────────────────────

    // OOXML requires UTF-8 WITHOUT BOM. XDocument.Save(Stream) emits a BOM by default;
    // use XmlWriter with explicit UTF8 (no-BOM) encoding to comply.
    private static readonly System.Xml.XmlWriterSettings XmlSettings = new()
    {
        Encoding = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
        Indent = true,
        OmitXmlDeclaration = false,
        CloseOutput = false
    };

    private static void WriteEntry(ZipArchive archive, string path, XDocument doc)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        using var stream = entry.Open();
        using var writer = System.Xml.XmlWriter.Create(stream, XmlSettings);
        doc.Save(writer);
    }

    private static void WriteRels(ZipArchive archive, string partPath, OpcRelationshipDocument rels, PptxPackageSnapshot? packageSnapshot = null)
    {
        var relsPath = OpcPathHelper.GetRelationshipPartPath(partPath);

        MergePreservedRelationships(rels, packageSnapshot, relsPath, partPath);
        WriteEntry(archive, relsPath, rels.ToXDocument());
    }

    private static void MergePreservedContentTypes(
        XDocument contentTypes,
        PptxPackageSnapshot? packageSnapshot,
        IReadOnlySet<string>? preservedWriterOwnedPaths = null)
    {
        if (packageSnapshot is null || !packageSnapshot.TryGetEntry("[Content_Types].xml", out var bytes))
            return;

        var sourceTypes = OpcXml.TryLoadXml(bytes);
        if (sourceTypes is not null)
        {
            OpcMediaTypes.MergePreservedContentTypes(
                contentTypes,
                sourceTypes,
                path => IsWriterOwnedPath(path) && !IsPreservedWriterOwnedPath(path, preservedWriterOwnedPaths));
        }
    }

    private static void MergePreservedRelationships(
        OpcRelationshipDocument rels,
        PptxPackageSnapshot? packageSnapshot,
        string relsPath,
        string sourcePartPath)
    {
        if (packageSnapshot is null || !packageSnapshot.TryGetEntry(relsPath, out var bytes))
            return;

        var sourceRels = OpcXml.TryLoadXml(bytes);
        if (sourceRels is null)
            return;

        if (sourceRels.Root is null)
            return;

        foreach (var rel in OpcRelationships.Load(sourceRels))
        {
            if (string.IsNullOrWhiteSpace(rel.Type) ||
                string.IsNullOrWhiteSpace(rel.Target))
                continue;

            if (IsWriterOwnedRelationship(sourcePartPath, rel.Type, rel.Target, rel.IsExternal))
                continue;

            rels.AddUnique(rel.Id, rel.Type, rel.Target, rel.IsExternal);
        }
    }

    private static void CopyPreservedPackageEntries(
        ZipArchive archive,
        PptxPackageSnapshot? packageSnapshot,
        IReadOnlySet<string>? preservedWriterOwnedPaths = null)
    {
        if (packageSnapshot is null)
            return;

        var copied = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (path, bytes) in packageSnapshot.Entries)
        {
            var normalizedPath = ToZipEntryPath(path);
            if (copied.Contains(normalizedPath) ||
                IsWriterOwnedPath(normalizedPath) && !IsPreservedWriterOwnedPath(normalizedPath, preservedWriterOwnedPaths))
            {
                continue;
            }

            var entry = archive.CreateEntry(normalizedPath, CompressionLevel.Optimal);
            using var stream = entry.Open();
            stream.Write(bytes, 0, bytes.Length);
            copied.Add(normalizedPath);
        }
    }

    private static bool TryReadPreservedXmlPart(
        PptxPackageSnapshot? packageSnapshot,
        string path,
        XName expectedRoot,
        out XDocument document)
    {
        document = new XDocument();
        if (packageSnapshot is null || !packageSnapshot.TryGetEntry(path, out var bytes))
            return false;

        var preserved = OpcXml.TryLoadXml(bytes);
        if (preserved?.Root is null || preserved.Root.Name != expectedRoot)
            return false;

        document = preserved;
        return true;
    }

    private static HashSet<string> FindPreservedChartWorkbookPaths(
        PptxPackageSnapshot? packageSnapshot,
        Presentation presentation)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (packageSnapshot is null)
            return paths;

        var chartIndex = 1;
        foreach (var slide in presentation.Slides)
        {
            foreach (var shape in AllShapes(slide.Shapes))
            {
                if (shape.Kind != SlideShapeKind.Chart || shape.Chart is null)
                    continue;

                if (!shape.Chart.RegenerateWorkbookOnSave)
                {
                    var chartPath = SourceChartPath(shape.Chart, chartIndex);
                    var relsPath = GetRelationshipPartPath(chartPath);
                    if (packageSnapshot.TryGetEntry(relsPath, out var relsBytes))
                    {
                        var relsXml = OpcXml.TryLoadXml(relsBytes);
                        if (relsXml is not null)
                        {
                            foreach (var relationship in OpcRelationships.Load(relsXml))
                            {
                                if (TryResolveChartWorkbookPath(chartPath, relationship, out var workbookPath) &&
                                    packageSnapshot.TryGetEntry(workbookPath, out _))
                                {
                                    paths.Add(ToZipEntryPath(workbookPath));
                                }
                            }
                        }
                    }
                }

                chartIndex++;
            }
        }

        return paths;
    }

    private static HashSet<string> FindPreservedChartExSidecarPaths(
        PptxPackageSnapshot? packageSnapshot,
        Presentation presentation)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (packageSnapshot is null)
            return paths;

        var chartIndex = 1;
        foreach (var slide in presentation.Slides)
        {
            foreach (var shape in AllShapes(slide.Shapes))
            {
                if (shape.Kind != SlideShapeKind.Chart || shape.Chart is not { IsChartEx: true } chart)
                    continue;

                var chartPath = SourceChartPath(chart, chartIndex);
                var relsPath = GetRelationshipPartPath(chartPath);
                if (packageSnapshot.TryGetEntry(relsPath, out var relsBytes) &&
                    OpcXml.TryLoadXml(relsBytes) is { } relsXml)
                {
                    foreach (var relationship in OpcRelationships.Load(relsXml))
                    {
                        if (relationship.IsExternal || string.IsNullOrWhiteSpace(relationship.Target))
                            continue;

                        var targetPath = ResolveRelativeZipPath(GetDirectoryName(chartPath), relationship.Target);
                        if (packageSnapshot.TryGetEntry(targetPath, out var sidecarBytes) && sidecarBytes.Length > 0)
                            paths.Add(ToZipEntryPath(targetPath));
                    }
                }

                chartIndex++;
            }
        }

        return paths;
    }

    private static HashSet<string> FindPreservedCaptionPackagePaths(
        PptxPackageSnapshot? packageSnapshot,
        Presentation presentation)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (packageSnapshot is null)
            return paths;

        foreach (var slide in presentation.Slides)
        {
            foreach (var shape in AllShapes(slide.Shapes))
            {
                if (shape.Kind != SlideShapeKind.Media || shape.Media is null)
                    continue;

                foreach (var track in shape.Media.CaptionTracks)
                {
                    if (!TryNormalizeInternalCaptionPackagePath(track.Source, out var normalizedPath) ||
                        !packageSnapshot.TryGetEntry(normalizedPath, out var preservedBytes) ||
                        preservedBytes.Length == 0 ||
                        !TryGetCaptionTrackBytes(track, packageSnapshot, out var bytes) ||
                        bytes.Length == 0 ||
                        !bytes.SequenceEqual(preservedBytes))
                    {
                        continue;
                    }

                    paths.Add(normalizedPath);
                }
            }
        }

        return paths;
    }

    private static HashSet<string> FindPreservedMediaPackagePaths(
        PptxPackageSnapshot? packageSnapshot,
        Presentation presentation)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (packageSnapshot is null)
            return paths;

        foreach (var slide in presentation.Slides)
        {
            foreach (var shape in AllShapes(slide.Shapes))
            {
                if (shape.Kind != SlideShapeKind.Media
                    || shape.Media is not { } media
                    || !TryNormalizeInternalMediaPackagePath(media.SourcePackagePath, out var normalizedPath)
                    || !packageSnapshot.TryGetEntry(normalizedPath, out var preservedBytes)
                    || preservedBytes.Length == 0
                    || media.Bytes.Length == 0
                    || !media.Bytes.SequenceEqual(preservedBytes))
                {
                    continue;
                }

                paths.Add(normalizedPath);
            }
        }

        return paths;
    }

    internal static string SourceChartPath(ChartShape chart, int chartIndex) =>
        string.IsNullOrWhiteSpace(chart.SourcePartPath)
            ? $"ppt/charts/chart{chartIndex}.xml"
            : ToZipEntryPath(chart.SourcePartPath);

    internal static bool TryResolveChartWorkbookPath(
        string chartPath,
        OpcRelationship relationship,
        out string workbookPath)
    {
        workbookPath = string.Empty;
        if (relationship.IsExternal ||
            string.IsNullOrWhiteSpace(relationship.Target) ||
            !string.Equals(relationship.Type, PackageRelType, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var chartDirectory = GetDirectoryName(chartPath);
        var resolvedPath = ResolveRelativeZipPath(chartDirectory, relationship.Target);
        if (!resolvedPath.StartsWith("ppt/embeddings/", StringComparison.OrdinalIgnoreCase))
            return false;

        workbookPath = resolvedPath;
        return true;
    }

    private static bool IsPreservedWriterOwnedPath(
        string path,
        IReadOnlySet<string>? preservedWriterOwnedPaths) =>
        preservedWriterOwnedPaths is not null &&
        preservedWriterOwnedPaths.Contains(ToZipEntryPath(path));

    private static bool IsWriterOwnedRelationship(string sourcePartPath, string type, string target, bool external) =>
        WriterOwnedPackageClassifier.IsRegeneratedRelationship(sourcePartPath, type, target, external);

    private static bool IsWriterOwnedPath(string path) =>
        WriterOwnedPackageClassifier.IsRegeneratedPart(path);

    // ── Small utilities ───────────────────────────────────────────────────────────

    private static XAttribute NsAttr(string prefix, XNamespace ns) =>
        new XAttribute(XNamespace.Xmlns + prefix, ns.NamespaceName);

    private static XElement CnvPr(SlideShape shape)
    {
        var el = CnvPrBase(shape);
        AddDecorativeExtList(el, shape);
        return el;
    }

    private static XElement CnvPrBase(SlideShape shape)
    {
        var el = new XElement(P + "cNvPr", new XAttribute("id", shape.Id), new XAttribute("name", shape.Name));
        if (shape.IsHidden)
        {
            el.Add(new XAttribute("hidden", "1"));
        }
        if (!string.IsNullOrWhiteSpace(shape.AlternativeTextTitle))
        {
            el.Add(new XAttribute("title", shape.AlternativeTextTitle.Trim()));
        }

        if (!string.IsNullOrWhiteSpace(shape.AlternativeText))
        {
            el.Add(new XAttribute("descr", shape.AlternativeText.Trim()));
        }

        return el;
    }

    private static void AddDecorativeExtList(XElement cNvPr, SlideShape shape)
    {
        if (!shape.IsDecorative)
        {
            return;
        }

        cNvPr.Add(new XElement(A + "extLst",
            new XElement(A + "ext",
                new XAttribute("uri", DecorativeExtUri),
                new XElement(Adec + "decorative",
                    NsAttr("adec", Adec),
                    new XAttribute("val", "1")))));
    }

    /// <summary>
    /// Builds a cNvPr element and, when the shape carries a hyperlink, appends an a:hlinkClick child.
    /// </summary>
    private static XElement CnvPrWithHlink(
        SlideShape shape,
        Dictionary<string, string>? hlinkRelIds,
        List<Slide>? allSlides)
    {
        var el = CnvPrBase(shape);
        if (shape.Hyperlink is not null)
        {
            var hlinkEl = BuildHlinkClickEl(shape.Hyperlink, hlinkRelIds, allSlides);
            if (hlinkEl is not null) el.Add(hlinkEl);
        }
        AddDecorativeExtList(el, shape);
        return el;
    }

    /// <summary>
    /// Builds an <c>a:hlinkClick</c> element for the given hyperlink, resolving to the stored rel ID.
    /// Returns null when no rel ID could be found for the hyperlink target.
    /// </summary>
    private static XElement? BuildHlinkClickEl(Hyperlink hlink,
        Dictionary<string, string>? hlinkRelIds, List<Slide>? allSlides)
    {
        if (hlinkRelIds is null) return null;

        string key = HlinkKey(hlink, allSlides);
        if (!hlinkRelIds.TryGetValue(key, out var relId)) return null;

        var el = new XElement(A + "hlinkClick", new XAttribute(R + "id", relId));
        if (!string.IsNullOrEmpty(hlink.Tooltip))
            el.Add(new XAttribute("tooltip", hlink.Tooltip));

        // Internal slide jump: add the action attribute.
        if (hlink.TargetSlideId is not null)
            el.Add(new XAttribute("action", "ppaction://hlinksldjump"));

        return el;
    }

    /// <summary>Compute the canonical key used in the hlinkRelIds dictionary for a Hyperlink.</summary>
    private static string HlinkKey(Hyperlink h, List<Slide>? allSlides)
    {
        if (h.Url is not null) return "ext:" + h.Url;
        if (h.TargetSlideId is not null) return "slide:" + h.TargetSlideId;
        return string.Empty;
    }

    private static IEnumerable<object?> GrpSpHeader() => new object?[]
    {
        new XElement(P + "nvGrpSpPr",
            new XElement(P + "cNvPr", new XAttribute("id", "1"), new XAttribute("name", "")),
            new XElement(P + "cNvGrpSpPr"),
            new XElement(P + "nvPr")),
        new XElement(P + "grpSpPr",
            new XElement(A + "xfrm",
                new XElement(A + "off", new XAttribute("x", "0"), new XAttribute("y", "0")),
                new XElement(A + "ext", new XAttribute("cx", "0"), new XAttribute("cy", "0")),
                new XElement(A + "chOff", new XAttribute("x", "0"), new XAttribute("y", "0")),
                new XElement(A + "chExt", new XAttribute("cx", "0"), new XAttribute("cy", "0"))))
    };

    private static IEnumerable<SlideShape> AllShapes(IEnumerable<SlideShape> shapes)
    {
        foreach (var s in shapes)
        {
            yield return s;
            foreach (var c in AllShapes(s.Children))
                yield return c;
        }
    }

    private static string FmtColor(SrgbColor c) => new DrawingMlRgbColor(c.R, c.G, c.B).ToHexRgb();

    private static string GetShapeId(SlideShape s) => s.Id.ToString(CultureInfo.InvariantCulture);

    private static string ToLayoutTypeStr(SlideLayoutType type) =>
        type switch
        {
            SlideLayoutType.Title => "title",
            SlideLayoutType.TitleContent => "obj",
            SlideLayoutType.TitleOnly => "titleOnly",
            SlideLayoutType.Blank => "blank",
            SlideLayoutType.TwoContent => "twoObj",
            SlideLayoutType.PictureCaption => "picTx",
            _ => "blank"
        };

    private static string ToDashStr(OutlineDash d) =>
        d switch
        {
            OutlineDash.Dash => "dash",
            OutlineDash.Dot => "dot",
            OutlineDash.DashDot => "dashDot",
            OutlineDash.LongDash => "lgDash",
            OutlineDash.LongDashDot => "lgDashDot",
            OutlineDash.LongDashDotDot => "lgDashDotDot",
            OutlineDash.SystemDash => "sysDash",
            OutlineDash.SystemDot => "sysDot",
            OutlineDash.SystemDashDot => "sysDashDot",
            _ => "solid"
        };

}
