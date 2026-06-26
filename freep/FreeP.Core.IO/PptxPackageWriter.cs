using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;
using Free.Shared.Drawing;
using FreeP.Core.Model;

namespace FreeP.Core.IO;

/// <summary>
/// Wave 1C: writes a <see cref="Presentation"/> model to a <c>.pptx</c> OPC package.
/// Produces a minimal-but-valid package that PowerPoint opens without repair.
/// Entry points: <see cref="Write(Presentation, string)"/> / <see cref="Write(Presentation, Stream)"/>.
/// </summary>
public static class PptxPackageWriter
{
    // ── Namespaces ────────────────────────────────────────────────────────────────
    private static readonly XNamespace P       = "http://schemas.openxmlformats.org/presentationml/2006/main";
    private static readonly XNamespace A       = PptxColorReader.A;
    private static readonly XNamespace R       = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PkgRels = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace Dc      = "http://purl.org/dc/elements/1.1/";
    private static readonly XNamespace Cp      = "http://schemas.openxmlformats.org/package/2006/metadata/core-properties";
    private static readonly XNamespace Dcterms = "http://purl.org/dc/terms/";
    private static readonly XNamespace Xsi     = XNamespace.Get("http://www.w3.org/2001/XMLSchema-instance");

    // ── Relationship types ────────────────────────────────────────────────────────
    private const string OfficeDocRelType   = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument";
    private const string CorePropsRelType   = "http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties";
    private const string SlideRelType       = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/slide";
    private const string SlideMasterRelType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/slideMaster";
    private const string SlideLayoutRelType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/slideLayout";
    private const string ThemeRelType       = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/theme";
    private const string ImageRelType       = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/image";
    private const string PresPropsRelType   = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/presProps";
    private const string ViewPropsRelType   = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/viewProps";
    private const string TableStylesRelType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/tableStyles";
    private const string ChartRelType       = PptxChartWriter.ChartRelType;
    private const string NotesSlideRelType  = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/notesSlide";
    private const string NotesMasterRelType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/notesMaster";
    private const string HyperlinkRelType   = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/hyperlink";
    private const string CommentsRelType    = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/comments";
    private const string CommentAuthorsRelType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/commentAuthors";
    private const string VideoRelType       = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/video";
    private const string AudioRelType       = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/audio";

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

    // ── Content types ─────────────────────────────────────────────────────────────
    private const string PresentationCT  = "application/vnd.openxmlformats-officedocument.presentationml.presentation.main+xml";
    private const string SlideCT         = "application/vnd.openxmlformats-officedocument.presentationml.slide+xml";
    private const string SlideMasterCT   = "application/vnd.openxmlformats-officedocument.presentationml.slideMaster+xml";
    private const string SlideLayoutCT   = "application/vnd.openxmlformats-officedocument.presentationml.slideLayout+xml";
    private const string ThemeCT         = "application/vnd.openxmlformats-officedocument.theme+xml";
    private const string PresPropsCT     = "application/vnd.openxmlformats-officedocument.presentationml.presProps+xml";
    private const string ViewPropsCT     = "application/vnd.openxmlformats-officedocument.presentationml.viewProps+xml";
    private const string TableStylesCT   = "application/vnd.openxmlformats-officedocument.presentationml.tableStyles+xml";
    private const string CorePropsCT     = "application/vnd.openxmlformats-package.core-properties+xml";
    private const string RelsCT          = "application/vnd.openxmlformats-package.relationships+xml";
    private const string ChartCT         = PptxChartWriter.ChartCT;
    private const string NotesSlideCT    = "application/vnd.openxmlformats-officedocument.presentationml.notesSlide+xml";
    private const string NotesMasterCT   = "application/vnd.openxmlformats-officedocument.presentationml.notesMaster+xml";
    private const string CommentsCT      = "application/vnd.openxmlformats-officedocument.presentationml.comments+xml";
    private const string CommentAuthorsCT = "application/vnd.openxmlformats-officedocument.presentationml.commentAuthors+xml";

    // p14 section extension + mc:AlternateContent
    private static readonly XNamespace P14 = "http://schemas.microsoft.com/office/powerpoint/2010/main";
    private static readonly XNamespace MC  = "http://schemas.openxmlformats.org/markup-compatibility/2006";
    private const string SectionExtUri = "{521415D9-36F7-43E2-AB2F-B90AF26B5E84}";

    // ── Public API ────────────────────────────────────────────────────────────────

    /// <summary>Writes a <see cref="Presentation"/> to a .pptx file on disk.</summary>
    public static void Write(Presentation presentation, string path)
    {
        using var stream = File.Create(path);
        Write(presentation, stream);
    }

    /// <summary>Writes a <see cref="Presentation"/> to any writable stream as a .pptx.</summary>
    public static void Write(Presentation presentation, Stream stream)
    {
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true);
        WriteArchive(archive, presentation);
    }

    // ── Core archive writing ──────────────────────────────────────────────────────

    private static void WriteArchive(ZipArchive archive, Presentation presentation)
    {
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

        // Collect media extensions used across all slides (for [Content_Types].xml Defaults).
        var mediaExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var slide in presentation.Slides)
        {
            foreach (var shape in AllShapes(slide.Shapes))
            {
                if ((shape.Kind == SlideShapeKind.Picture || shape.Kind == SlideShapeKind.Media)
                    && shape.Picture?.Bytes is { Length: > 0 })
                {
                    var ct = shape.Picture.ContentType ?? "image/png";
                    mediaExtensions.Add(ContentTypeToExtension(ct));
                }

                // HH1: also register picture-fill images so their extension gets a Default
                if (shape.Fill is ShapeFill.Picture picFill && picFill.ImageBytes.Length > 0)
                {
                    var ct = picFill.ContentType ?? "image/png";
                    mediaExtensions.Add(ContentTypeToExtension(ct));
                }

                // II1: register audio/video file extensions for Media shapes
                if (shape.Kind == SlideShapeKind.Media && shape.Media?.Bytes is { Length: > 0 } && shape.Media.ContentType is not null)
                {
                    var mediaExt = MediaContentTypeToExtension(shape.Media.ContentType);
                    mediaExtensions.Add(mediaExt);
                }
            }
        }

        // --- 1. [Content_Types].xml ---
        var ctXml = BuildContentTypesXml(presentation, masters, layouts, mediaExtensions);
        WriteEntry(archive, "[Content_Types].xml", ctXml);

        // --- 2. Root rels ---
        var rootRels = new RelsDoc();
        rootRels.Add("rId1", OfficeDocRelType, "ppt/presentation.xml");
        rootRels.Add("rId2", CorePropsRelType, "docProps/core.xml");
        WriteEntry(archive, "_rels/.rels", rootRels.ToXDocument());

        // --- 3. Core properties ---
        WriteEntry(archive, "docProps/core.xml", BuildCorePropsXml(presentation.Properties));

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

        // --- 5. presProps, viewProps, tableStyles ---
        WriteEntry(archive, "ppt/presProps.xml", BuildPresPropsXml());
        WriteEntry(archive, "ppt/viewProps.xml", BuildViewPropsXml());
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
            var layoutRels = new RelsDoc();
            layoutRels.Add("rId1", SlideMasterRelType, $"../{masterPath.Replace("ppt/", "")}");
            WriteRels(archive, layoutPath, layoutRels);
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
            var masterRels = new RelsDoc();
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
            WriteRels(archive, masterPath, masterRels);
        }

        // --- 8. Slides ---
        var presRels = new RelsDoc();
        var sldIdElements = new List<XElement>();
        uint sldIdCounter = 256;

        // Build the GLOBAL author map once before the slide loop so that every per-slide
        // BuildCommentsXml call uses consistent (globally-assigned) author ids.
        // Keys are (author-name, initials); ids are 0-based in first-encounter order across
        // all slides (same order BuildCommentAuthorsXml would produce).
        var globalAuthorMap = BuildGlobalAuthorMap(presentation.Slides);

        // Emit a single minimal notesMaster if any slide has notes.
        bool hasSomeNotes = presentation.Slides.Any(s => s.Notes is not null);
        if (hasSomeNotes)
        {
            WriteEntry(archive, "ppt/notesMasters/notesMaster1.xml", BuildNotesMasterXml());
            // notesMaster rels: -> theme (reuse the same theme1.xml)
            var nmRels = new RelsDoc();
            nmRels.Add("rId1", ThemeRelType, "../theme/theme1.xml");
            WriteRels(archive, "ppt/notesMasters/notesMaster1.xml", nmRels);
        }

        int globalChartIndex = 1; // monotonically increasing across all slides
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
            var mediaFileRelIds = WriteSlideMediaFiles(archive, slide, si + 1);

            // Write charts into the archive, get back rel-id map
            var chartRelIds = WriteSlideCharts(archive, slide, ref globalChartIndex);

            // Write SmartArt diagram parts (verbatim bytes + rels).
            // Collect already-used relIds so the SmartArt allocator avoids them.
            var usedRelIds = new HashSet<string>(StringComparer.Ordinal)
            {
                "rId1", // always reserved for slide layout
            };
            foreach (var (_, mediaRelId, _) in mediaRelIds)      usedRelIds.Add(mediaRelId);
            foreach (var (_, mediaFileRelId, _, _) in mediaFileRelIds) usedRelIds.Add(mediaFileRelId);
            foreach (var (_, fillBlipRelId, _) in fillBlipRelIds) usedRelIds.Add(fillBlipRelId);
            foreach (var (_, chartRelId, _) in chartRelIds)       usedRelIds.Add(chartRelId);

            var (smartArtSlideRels, smartArtRelIdRemap) = WriteSlideSmartArt(archive, slide, usedRelIds);

            // Theme 21: Write OLE embedded object binaries + fallback images.
            foreach (var kv in smartArtRelIdRemap)
                foreach (var (_, saRelId) in kv.Value)
                    usedRelIds.Add(saRelId);
            // Returns: embRels = (shapeId, embRelId, embRelType, embPath)
            //          imgRels = (shapeId, imgRelId, imgPath)
            var (oleEmbRels, oleImgRels) = WriteSlideOleObjects(archive, slide, si + 1, usedRelIds);

            // Combined shapeId→relId map for shape element building (picture shapes + charts + OLE)
            var mediaById = new Dictionary<uint, string>();
            foreach (var (id, relId, _) in mediaRelIds)  mediaById[id] = relId;
            foreach (var (id, relId, _) in chartRelIds)  mediaById[id] = relId;
            // Media file rel IDs use synthetic key: shape.Id | 0x80000000
            foreach (var (id, relId, _, _) in mediaFileRelIds)  mediaById[id | 0x80000000u] = relId;
            // OLE embedded rel IDs keyed by shape.Id (used in BuildOleGraphicFrameEl)
            foreach (var (shapeId, embRelId, _, _) in oleEmbRels)  mediaById[shapeId] = embRelId;
            // OLE fallback image rel IDs use synthetic key: shape.Id | 0x40000000
            foreach (var (shapeId, imgRelId, _) in oleImgRels)     mediaById[shapeId | 0x40000000u] = imgRelId;

            // Fill-blip relId map (shapeId -> relId) for ShapeFill.Picture fills
            var fillBlipById = new Dictionary<uint, string>();
            foreach (var (id, relId, _) in fillBlipRelIds)  fillBlipById[id] = relId;

            // Collect hyperlinks from this slide and assign rel IDs.
            // hlinkRelIds maps hyperlink key (url or "slide:"+slideId) to the rels r:id.
            var hlinkRelIds = new Dictionary<string, string>(StringComparer.Ordinal);
            var hlinkRelEntries = new List<(string relId, string relType, string target, bool external)>();
            CollectHyperlinkRels(slide, presentation.Slides, si + 1, hlinkRelIds, hlinkRelEntries);

            // Slide xml — use the owning master's theme color scheme for scheme-color pre-resolution.
            WriteEntry(archive, slidePath, BuildSlideXml(slide, slideColorScheme, mediaById, smartArtRelIdRemap, hlinkRelIds, presentation.Slides, fillBlipById));

            // Slide rels: rId1=layout, images (picture shapes + fill blips), charts, SmartArt, optional notesSlide
            var slideRels = new RelsDoc();
            slideRels.Add("rId1", SlideLayoutRelType, $"../slideLayouts/{layoutPath.Split('/').Last()}");
            foreach (var (_, mediaRelId, mediaPath) in mediaRelIds)
                slideRels.Add(mediaRelId, ImageRelType, $"../media/{mediaPath.Split('/').Last()}");
            foreach (var (_, mediaFileRelId, mediaFilePath, isVideo) in mediaFileRelIds)
                slideRels.Add(mediaFileRelId, isVideo ? VideoRelType : AudioRelType, $"../media/{mediaFilePath.Split('/').Last()}");
            foreach (var (_, fillBlipRelId, fillBlipPath) in fillBlipRelIds)
                slideRels.Add(fillBlipRelId, ImageRelType, $"../media/{fillBlipPath.Split('/').Last()}");
            foreach (var (_, chartRelId, chartPath) in chartRelIds)
                slideRels.Add(chartRelId, ChartRelType, $"../charts/{chartPath.Split('/').Last()}");
            // SmartArt diagram part rels (dm/lo/qs/cs each get their own rel entry in slide rels)
            foreach (var (relId, relType, target) in smartArtSlideRels)
                slideRels.Add(relId, relType, target);
            // Theme 21: OLE embedded object + fallback image rels
            foreach (var (_, embRelId, embRelType, embPath) in oleEmbRels)
                slideRels.Add(embRelId, embRelType, $"../embeddings/{embPath.Split('/').Last()}");
            foreach (var (_, imgRelId, imgPath) in oleImgRels)
                slideRels.Add(imgRelId, ImageRelType, $"../media/{imgPath.Split('/').Last()}");
            // Hyperlink rels (external with TargetMode=External; internal slide rels without)
            foreach (var (hlRelId, hlRelType, hlTarget, isExternal) in hlinkRelEntries)
                slideRels.Add(hlRelId, hlRelType, hlTarget, isExternal);

            // Write notes slide and add rel when the slide has speaker notes
            if (slide.Notes is not null)
            {
                var notesPath = $"ppt/notesSlides/notesSlide{si + 1}.xml";
                var notesRelId = $"rIdNotes{si + 1}";
                WriteEntry(archive, notesPath, BuildNotesSlideXml(slide.Notes, slidePath));

                // notesSlide rels: -> slide + notesMaster
                var notesRels = new RelsDoc();
                notesRels.Add("rId1", SlideRelType,       $"../slides/slide{si + 1}.xml");
                notesRels.Add("rId2", NotesMasterRelType, "../notesMasters/notesMaster1.xml");
                WriteRels(archive, notesPath, notesRels);

                slideRels.Add(notesRelId, NotesSlideRelType, $"../notesSlides/notesSlide{si + 1}.xml");
            }

            // Write comments part and add rel when the slide has comments
            if (slide.Comments.Count > 0)
            {
                var cmPath  = $"ppt/comments/comment{si + 1}.xml";
                var cmRelId = $"rIdCm{si + 1}";
                WriteEntry(archive, cmPath, BuildCommentsXml(slide.Comments, globalAuthorMap));
                slideRels.Add(cmRelId, CommentsRelType, $"../comments/comment{si + 1}.xml");
            }

            WriteRels(archive, slidePath, slideRels);

            presRels.Add(slideRelId, SlideRelType, $"slides/slide{si + 1}.xml");
            sldIdElements.Add(new XElement(P + "sldId",
                new XAttribute("id", sldIdCounter++),
                new XAttribute(R + "id", slideRelId)));
        }

        // --- 8b. commentAuthors.xml (if any slides have comments) ---
        bool hasSomeComments = presentation.Slides.Any(s => s.Comments.Count > 0);
        if (hasSomeComments)
            WriteEntry(archive, "ppt/commentAuthors.xml", BuildCommentAuthorsXml(presentation.Slides));

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
        if (hasSomeNotes)
        {
            presRels.Add($"rId{masterRelIdStart + masters.Count + extraPresRelOffset}", NotesMasterRelType, "notesMasters/notesMaster1.xml");
            extraPresRelOffset++;
        }
        if (hasSomeComments)
            presRels.Add($"rId{masterRelIdStart + masters.Count + extraPresRelOffset}", CommentAuthorsRelType, "commentAuthors.xml");

        WriteRels(archive, "ppt/presentation.xml", presRels);

        // --- 10. presentation.xml (last, so sldIdElements are complete) ---
        var masterRelIds = Enumerable.Range(0, masters.Count)
            .Select(i => ($"rId{masterRelIdStart + i}", $"ppt/slideMasters/slideMaster{i+1}.xml"))
            .ToList();

        WriteEntry(archive, "ppt/presentation.xml",
            BuildPresentationXml(presentation, sldIdElements, masterRelIds));
    }

    // ── [Content_Types].xml ───────────────────────────────────────────────────────

    // Maps a file extension to its IANA media type for [Content_Types].xml Default entries.
    private static readonly Dictionary<string, string> ExtensionToContentType =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["png"]  = "image/png",
            ["jpg"]  = "image/jpeg",
            ["jpeg"] = "image/jpeg",
            ["gif"]  = "image/gif",
            ["bmp"]  = "image/bmp",
            ["tiff"] = "image/tiff",
            ["svg"]  = "image/svg+xml",
            ["wmf"]  = "image/x-wmf",
            ["emf"]  = "image/x-emf",
            // Audio/video types (II1: required so media parts have a declared content type)
            ["mp4"]  = "video/mp4",
            ["mov"]  = "video/quicktime",
            ["avi"]  = "video/x-msvideo",
            ["wmv"]  = "video/x-ms-wmv",
            ["mp3"]  = "audio/mpeg",
            ["m4a"]  = "audio/mp4",
            ["wav"]  = "audio/wav",
            ["wma"]  = "audio/x-ms-wma",
        };

    private static XDocument BuildContentTypesXml(
        Presentation p, List<SlideMaster> masters, List<SlideLayout> layouts,
        HashSet<string> mediaExtensions)
    {
        var CT = XNamespace.Get("http://schemas.openxmlformats.org/package/2006/content-types");

        var defaults = new List<XElement>
        {
            new XElement(CT + "Default", new XAttribute("Extension", "rels"), new XAttribute("ContentType", RelsCT)),
            new XElement(CT + "Default", new XAttribute("Extension", "xml"),  new XAttribute("ContentType", "application/xml")),
        };

        // Emit a Default entry for every media extension actually written (covers all paths correctly).
        foreach (var ext in mediaExtensions.OrderBy(e => e))
        {
            if (ExtensionToContentType.TryGetValue(ext, out var imgCt))
                defaults.Add(new XElement(CT + "Default",
                    new XAttribute("Extension", ext),
                    new XAttribute("ContentType", imgCt)));
        }

        var overrides = new List<XElement>
        {
            Override(CT, "/ppt/presentation.xml", PresentationCT),
            Override(CT, "/ppt/presProps.xml", PresPropsCT),
            Override(CT, "/ppt/viewProps.xml", ViewPropsCT),
            Override(CT, "/ppt/tableStyles.xml", TableStylesCT),
            Override(CT, "/docProps/core.xml", CorePropsCT),
        };
        // MM4: one theme Override entry per master (theme1.xml, theme2.xml, …).
        for (int mi = 0; mi < masters.Count; mi++)
            overrides.Add(Override(CT, $"/ppt/theme/theme{mi + 1}.xml", ThemeCT));

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
                    overrides.Add(Override(CT, $"/ppt/charts/chart{chartGlobalIdx}.xml", ChartCT));
                    chartGlobalIdx++;
                }
            }
        }

        // Comments content types
        bool hasSomeComments = p.Slides.Any(s => s.Comments.Count > 0);
        if (hasSomeComments)
        {
            overrides.Add(Override(CT, "/ppt/commentAuthors.xml", CommentAuthorsCT));
            for (int si = 0; si < p.Slides.Count; si++)
            {
                if (p.Slides[si].Comments.Count > 0)
                    overrides.Add(Override(CT, $"/ppt/comments/comment{si + 1}.xml", CommentsCT));
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

        return new XDocument(
            new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(CT + "Types",
                defaults,
                overrides));
    }

    private static XElement Override(XNamespace ct, string partName, string contentType) =>
        new XElement(ct + "Override",
            new XAttribute("PartName", partName),
            new XAttribute("ContentType", contentType));

    // ── presentation.xml ─────────────────────────────────────────────────────────

    private static XDocument BuildPresentationXml(
        Presentation p,
        List<XElement> sldIdElements,
        List<(string relId, string masterPath)> masterRelIds)
    {
        var presEl = new XElement(P + "presentation",
            NsAttr("p", P), NsAttr("a", A), NsAttr("r", R),
            new XAttribute("saveSubsetFonts", "1"),
            new XElement(P + "sldMasterIdLst",
                masterRelIds.Select((mr, i) =>
                    new XElement(P + "sldMasterId",
                        new XAttribute("id", 2147483648u + (uint)i),
                        new XAttribute(R + "id", mr.relId)))),
            new XElement(P + "sldIdLst", sldIdElements),
            new XElement(P + "sldSz",
                new XAttribute("cx", p.SlideSizeCxEmu),
                new XAttribute("cy", p.SlideSizeCyEmu),
                new XAttribute("type", "screen16x9")),
            new XElement(P + "notesSz",
                new XAttribute("cx", 6858000),
                new XAttribute("cy", 9144000)));

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

    private static XDocument BuildSlideXml(
        Slide slide, PresentationColorScheme scheme,
        Dictionary<uint, string> mediaById,
        Dictionary<uint, Dictionary<string, string>> smartArtRelIdRemap,
        Dictionary<string, string>? hlinkRelIds = null,
        List<Slide>? allSlides = null,
        Dictionary<uint, string>? fillBlipById = null)
    {
        return new XDocument(
            new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(P + "sld",
                NsAttr("p", P), NsAttr("a", A), NsAttr("r", R),
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
                            .Select(s => BuildShapeEl(s, scheme, mediaById, smartArtRelIdRemap, hlinkRelIds, allSlides, fillBlipById))
                            .OfType<XElement>())),
                // II2: p:hf is NOT valid on p:sld (CT_Slide schema has no hf element);
                // it is only valid on slideMaster/slideLayout/handoutMaster/notesMaster.
                // We intentionally do NOT emit p:hf here to avoid PowerPoint repair.
                // HfVisibility is preserved on the model for read-back from real .pptx files
                // that carry it on the master/layout (which the reader already handles).
                BuildSlideClrMapOvrEl(slide.ColorMapOverride),
                BuildTransitionEl(slide.Transition),
                BuildTimingEl(slide.Animations)));
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
                new XElement(P + "cNvPr", new XAttribute("id", "1"), new XAttribute("name", "Slide Image Placeholder 1")),
                new XElement(P + "cNvSpPr", new XElement(A + "spLocks", new XAttribute("noGrp", "1"), new XAttribute("noRot", "1"), new XAttribute("noChangeAspect", "1"))),
                new XElement(P + "nvPr", new XElement(P + "ph", new XAttribute("type", "sldImg")))),
            new XElement(P + "spPr"));

        // Body placeholder (carries the notes text)
        var notesTxBody = BuildNotesTxBodyEl(notes);
        var notesBodySp = new XElement(P + "sp",
            new XElement(P + "nvSpPr",
                new XElement(P + "cNvPr", new XAttribute("id", "2"), new XAttribute("name", "Content Placeholder 2")),
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
                        notesBodySp))));
    }

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
    /// Builds a minimal ppt/notesMasters/notesMaster1.xml. Contains no shapes — PowerPoint
    /// accepts a virtually empty master and derives defaults from the theme.
    /// </summary>
    private static XDocument BuildNotesMasterXml() =>
        new XDocument(
            new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(P + "notesMaster",
                NsAttr("p", P), NsAttr("a", A), NsAttr("r", R),
                new XElement(P + "cSld",
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
                                new XElement(A + "chExt", new XAttribute("cx", "0"), new XAttribute("cy", "0")))))),
                new XElement(P + "clrMap",
                    new XAttribute("bg1", "lt1"),   new XAttribute("tx1", "dk1"),
                    new XAttribute("bg2", "lt2"),   new XAttribute("tx2", "dk2"),
                    new XAttribute("accent1", "accent1"), new XAttribute("accent2", "accent2"),
                    new XAttribute("accent3", "accent3"), new XAttribute("accent4", "accent4"),
                    new XAttribute("accent5", "accent5"), new XAttribute("accent6", "accent6"),
                    new XAttribute("hlink", "hlink"), new XAttribute("folHlink", "folHlink"))));

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

    // ── p:transition ─────────────────────────────────────────────────────────────

    private static XElement? BuildTransitionEl(SlideTransition? transition)
    {
        if (transition is null || transition.Kind == TransitionKind.None)
            return null;

        var spd = PptxAnimationMap.DurationToSpd(transition.DurationMs);

        // Effect child element (shared between Choice and Fallback)
        var effectName = PptxAnimationMap.TransitionKindToElementName(transition.Kind);
        var dirAttr = PptxAnimationMap.TransitionDirectionToAttr(transition.Direction);

        XElement BuildEffectEl() =>
            effectName is not null
                ? new XElement(P + effectName,
                    dirAttr is not null ? new XAttribute("dir", dirAttr) : null!)
                : null!;

        // Build a p:transition element with the given attrs + effect child.
        XElement BuildTransEl(IEnumerable<object?> extraAttrs)
        {
            var ch = new List<object?>();
            ch.AddRange(extraAttrs);
            var eff = BuildEffectEl();
            if (eff is not null) ch.Add(eff);
            return new XElement(P + "transition",
                ch.Where(x => x is not null).Cast<object>().ToArray());
        }

        // Common non-duration attributes
        var commonAttrs = new List<object?>();
        commonAttrs.Add(new XAttribute("spd", spd));
        if (!transition.AdvanceOnClick)
            commonAttrs.Add(new XAttribute("advClick", "0"));
        if (transition.AdvanceAfterMs.HasValue)
            commonAttrs.Add(new XAttribute("advTm", transition.AdvanceAfterMs.Value));

        // AC1: Emit precise duration as p14:dur inside mc:AlternateContent/mc:Choice Requires="p14".
        // The mc:Fallback carries the legacy spd-only p:transition so old readers degrade gracefully.
        // This mirrors the pattern PowerPoint itself writes; bare "dur" on p:transition is invalid per
        // CT_SlideTransition (ECMA-376) and is flagged by OpenXmlValidator.
        var choiceAttrs = new List<object?>(commonAttrs)
        {
            new XAttribute(P14 + "dur", transition.DurationMs),
        };
        // In the Choice the spd is already present; p14:dur wins for modern readers.

        return new XElement(MC + "AlternateContent",
            new XAttribute(XNamespace.Xmlns + "mc", MC.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "p14", P14.NamespaceName),
            new XElement(MC + "Choice",
                new XAttribute("Requires", "p14"),
                BuildTransEl(choiceAttrs)),
            new XElement(MC + "Fallback",
                BuildTransEl(commonAttrs)));
    }

    // ── p:timing ─────────────────────────────────────────────────────────────────

    private static XElement? BuildTimingEl(List<ShapeAnimation> animations)
    {
        if (animations.Count == 0) return null;

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

        var outerPar = new XElement(P + "par",
            new XElement(P + "cTn",
                new XAttribute("id", nodeId++),
                new XAttribute("fill", "hold"),
                new XElement(P + "stCondLst",
                    new XElement(P + "cond", new XAttribute("delay", "0"))),
                new XElement(P + "childTnLst",
                    new XElement(P + "par", outerParCTn))));

        // ── Trigger sequences ─────────────────────────────────────────────────

        var triggerPars = new List<XElement>();
        foreach (var group in triggerAnims)
        {
            var trigSpid = group.Key;
            var trigSeqEl = BuildTriggerSequenceEl(group.ToList(), trigSpid, ref nodeId);
            triggerPars.Add(trigSeqEl);
        }

        // Combine all top-level children into tnLst.
        var tnLstChildren = new List<XElement> { outerPar };
        tnLstChildren.AddRange(triggerPars);

        return new XElement(P + "timing",
            new XElement(P + "tnLst", tnLstChildren));
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

        var (presetClass, presetId) = PptxAnimationMap.AnimationPresetToOoxml(anim.Preset, anim.Kind);
        var subtypeAttr = PptxAnimationMap.AnimationDirectionToSubtype(anim.Direction);

        string delayStr = triggerOverride == AnimationTrigger.OnClick
            ? "indefinite"
            : anim.DelayMs.ToString(System.Globalization.CultureInfo.InvariantCulture);

        var cTnAttrs = new List<object>
        {
            new XAttribute("id", nodeId++),
            new XAttribute("presetClass", presetClass),
            new XAttribute("presetID", presetId),
            new XAttribute("presetSubtype", subtypeAttr ?? "0"),
            new XAttribute("fill", "hold"),
            new XAttribute("grpId", "0"),
            new XAttribute("nodeType", "withEffect"),
        };

        var animCTn = new XElement(P + "cTn",
            new XAttribute("id", nodeId++),
            new XAttribute("dur", anim.DurationMs),
            new XElement(P + "stCondLst",
                new XElement(P + "cond", new XAttribute("delay", "0"))));

        var setEl = new XElement(P + "set",
            new XElement(P + "cBhvr",
                new XElement(P + "cTn",
                    new XAttribute("id", nodeId++),
                    new XAttribute("dur", "1"),
                    new XAttribute("fill", "hold")),
                new XElement(P + "tgtEl",
                    new XElement(P + "spTgt", new XAttribute("spid", anim.ShapeId)))));

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
                            new XElement(P + "childTnLst", animCTn, setEl))))));
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
                master.TextStyles is not null ? BuildTxStylesEl(master.TextStyles) : null,
                new XElement(P + "sldLayoutIdLst",
                    layoutRelIds.Select((lr, i) =>
                        new XElement(P + "sldLayoutId",
                            new XAttribute("id", 2147483649u + (uint)i),
                            new XAttribute(R + "id", lr.relId))))));

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

    private static XElement BuildLvlpPrEl(string localName, TextStyleLevel level)
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
        if (level.MarginLeftEmu.HasValue) el.Add(new XAttribute("marL", level.MarginLeftEmu.Value));
        if (level.IndentEmu.HasValue)     el.Add(new XAttribute("indent", level.IndentEmu.Value));

        // Bullet (Wave 19A: extended round-trip)
        switch (level.BulletKind)
        {
            case BulletKind.None:
                el.Add(new XElement(A + "buNone")); break;
            case BulletKind.Char:
                if (level.BulletColor is not null)
                {
                    // Preserve the original scheme/sRGB color reference via the shared helper
                    // (BU4: previously always emitted srgbClr, losing schemeClr theme linkage).
                    el.Add(new XElement(A + "buClr", BuildColorEl(level.BulletColor)));
                }
                if (level.BulletSizePct.HasValue) el.Add(new XElement(A + "buSzPct", new XAttribute("val", level.BulletSizePct.Value)));
                if (!string.IsNullOrEmpty(level.BulletFontFamily)) el.Add(new XElement(A + "buFont", new XAttribute("typeface", level.BulletFontFamily)));
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
                            new XElement(A + "latin", new XAttribute("typeface", theme.FontScheme.MajorLatinFont))),
                        new XElement(A + "minorFont",
                            new XElement(A + "latin", new XAttribute("typeface", theme.FontScheme.MinorLatinFont)))),
                    new XElement(A + "fmtScheme", new XAttribute("name", "Office"),
                        new XElement(A + "fillStyleLst",
                            SolidPhClr(), SolidPhClr(), SolidPhClr()),
                        new XElement(A + "lnStyleLst",
                            LnStyle("6350"), LnStyle("12700"), LnStyle("19050")),
                        new XElement(A + "effectStyleLst",
                            EffectStyle(), EffectStyle(), EffectStyle()),
                        new XElement(A + "bgFillStyleLst",
                            SolidPhClr(), SolidPhClr(), SolidPhClr())))));
    }

    private static XElement SolidPhClr() =>
        new XElement(A + "solidFill", new XElement(A + "schemeClr", new XAttribute("val", "phClr")));

    private static XElement LnStyle(string w) =>
        new XElement(A + "ln", new XAttribute("w", w),
            new XElement(A + "solidFill", new XElement(A + "schemeClr", new XAttribute("val", "phClr"))),
            new XElement(A + "prstDash", new XAttribute("val", "solid")));

    private static XElement EffectStyle() =>
        new XElement(A + "effectStyle", new XElement(A + "effectLst"));

    private static XElement ColorSlot(string name, SrgbColor color) =>
        new XElement(A + name,
            new XElement(A + "srgbClr", new XAttribute("val", FmtColor(color))));

    // ── Stub XML parts ────────────────────────────────────────────────────────────

    private static XDocument BuildPresPropsXml() =>
        new XDocument(
            new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(P + "presentationPr", NsAttr("p", P), NsAttr("a", A)));

    private static XDocument BuildViewPropsXml() =>
        new XDocument(
            new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(P + "viewPr", NsAttr("p", P)));

    private static XDocument BuildTableStylesXml() =>
        new XDocument(
            new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(P + "tblStyleLst", NsAttr("p", P),
                new XAttribute("def", "{5C22544A-7EE6-4342-B048-85BDC9FD1C3A}")));

    // ── Core properties ───────────────────────────────────────────────────────────

    private static XDocument BuildCorePropsXml(PresentationProperties props) =>
        new XDocument(
            new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(Cp + "coreProperties",
                NsAttr("cp", Cp), NsAttr("dc", Dc), NsAttr("dcterms", Dcterms), NsAttr("xsi", Xsi),
                props.Title is not null ? new XElement(Dc + "title", props.Title) : null,
                props.Author is not null ? new XElement(Dc + "creator", props.Author) : null,
                props.Subject is not null ? new XElement(Dc + "subject", props.Subject) : null,
                props.Keywords is not null ? new XElement(Cp + "keywords", props.Keywords) : null,
                props.Comments is not null ? new XElement(Dc + "description", props.Comments) : null));

    // ── Shape elements ────────────────────────────────────────────────────────────

    private static XElement? BuildShapeEl(
        SlideShape shape, PresentationColorScheme scheme, Dictionary<uint, string> mediaById,
        Dictionary<uint, Dictionary<string, string>>? smartArtRelIdRemap = null,
        Dictionary<string, string>? hlinkRelIds = null,
        List<Slide>? allSlides = null,
        Dictionary<uint, string>? fillBlipById = null) =>
        shape.Kind switch
        {
            SlideShapeKind.Picture => BuildPicEl(shape, mediaById),
            SlideShapeKind.Media   => BuildMediaPicEl(shape, mediaById),
            SlideShapeKind.Group => BuildGrpSpEl(shape, scheme, mediaById, smartArtRelIdRemap, hlinkRelIds, allSlides, fillBlipById),
            SlideShapeKind.Connector => BuildCxnSpEl(shape, scheme, hlinkRelIds, fillBlipById),
            SlideShapeKind.Table when shape.Table is not null => BuildGraphicFrameEl(shape, scheme),
            SlideShapeKind.Chart when shape.Chart is not null => BuildChartGraphicFrameEl(shape, mediaById),
            SlideShapeKind.SmartArt when shape.SmartArt is not null =>
                BuildSmartArtGraphicFrameEl(shape,
                    smartArtRelIdRemap?.GetValueOrDefault(shape.Id)),
            // Theme 21: OLE embedded objects — emit the verbatim graphicFrame wrapper
            SlideShapeKind.Ole when shape.OleObject is not null =>
                BuildOleGraphicFrameEl(shape, mediaById),
            _ => BuildSpEl(shape, scheme, hlinkRelIds, allSlides, fillBlipById)
        };

    private static XElement BuildSpEl(SlideShape shape, PresentationColorScheme scheme,
        Dictionary<string, string>? hlinkRelIds = null,
        List<Slide>? allSlides = null,
        Dictionary<uint, string>? fillBlipById = null)
    {
        string? fillBlipRelId = null;
        fillBlipById?.TryGetValue(shape.Id, out fillBlipRelId);
        return new XElement(P + "sp",
            new XElement(P + "nvSpPr",
                CnvPrWithHlink(shape.Id, shape.Name, shape.Hyperlink, hlinkRelIds, allSlides),
                new XElement(P + "cNvSpPr"),
                new XElement(P + "nvPr",
                    shape.Placeholder is not null ? BuildPhEl(shape.Placeholder) : null)),
            BuildSpPrEl(shape, scheme, fillBlipRelId: fillBlipRelId),
            shape.TextBody is not null ? BuildTxBodyEl(shape.TextBody, scheme, hlinkRelIds, allSlides) : null);
    }

    private static XElement BuildCxnSpEl(SlideShape shape, PresentationColorScheme scheme,
        Dictionary<string, string>? hlinkRelIds = null,
        Dictionary<uint, string>? fillBlipById = null)
    {
        string? fillBlipRelId = null;
        fillBlipById?.TryGetValue(shape.Id, out fillBlipRelId);
        return new XElement(P + "cxnSp",
            new XElement(P + "nvCxnSpPr",
                CnvPrWithHlink(shape.Id, shape.Name, shape.Hyperlink, hlinkRelIds, null),
                new XElement(P + "cNvCxnSpPr"),
                new XElement(P + "nvPr")),
            BuildSpPrEl(shape, scheme, fillBlipRelId: fillBlipRelId));
    }

    private static XElement BuildPicEl(SlideShape shape, Dictionary<uint, string> mediaById)
    {
        // Look up by shape Id (collision-safe); fall back to a placeholder only if somehow missing.
        mediaById.TryGetValue(shape.Id, out var embedRelId);
        embedRelId ??= "rIdMedia1";

        // 18A: build a:blip with optional colour effects
        var blipEl = BuildBlipEl(embedRelId, shape.PictureFormat);

        // 18A: build a:blipFill with optional a:srcRect (crop)
        var blipFillEl = BuildBlipFillEl(blipEl, shape.PictureFormat);

        return new XElement(P + "pic",
            new XElement(P + "nvPicPr",
                CnvPr(shape.Id, shape.Name),
                new XElement(P + "cNvPicPr"),
                new XElement(P + "nvPr")),
            blipFillEl,
            BuildSpPrEl(shape, PresentationColorScheme.CreateDefault(), forcePrst: "rect"));
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

    private static XElement BuildMediaPicEl(SlideShape shape, Dictionary<uint, string> mediaById)
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

        return new XElement(P + "pic",
            new XElement(P + "nvPicPr",
                CnvPr(shape.Id, shape.Name),
                new XElement(P + "cNvPicPr"),
                new XElement(P + "nvPr",
                    mediaFileEl)),
            blipFillEl,
            BuildSpPrEl(shape, PresentationColorScheme.CreateDefault(), forcePrst: "rect"));
    }

    private static XElement BuildGrpSpEl(
        SlideShape shape, PresentationColorScheme scheme, Dictionary<uint, string> mediaById,
        Dictionary<uint, Dictionary<string, string>>? smartArtRelIdRemap = null,
        Dictionary<string, string>? hlinkRelIds = null,
        List<Slide>? allSlides = null,
        Dictionary<uint, string>? fillBlipById = null) =>
        new XElement(P + "grpSp",
            new XElement(P + "nvGrpSpPr",
                CnvPr(shape.Id, shape.Name),
                new XElement(P + "cNvGrpSpPr"),
                new XElement(P + "nvPr")),
            BuildGrpSpPrEl(shape),
            shape.Children
                .Select(c => BuildShapeEl(c, scheme, mediaById, smartArtRelIdRemap, hlinkRelIds, allSlides, fillBlipById))
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
        if (forcePrst is null && shape.CustomGeometry.Count > 0)
            geomEl = BuildCustGeomEl(shape.CustomGeometry);
        else
            geomEl = new XElement(A + "prstGeom",
                new XAttribute("prst", forcePrst ?? PptxShapeKindMap.ToPreset(shape.AutoShapeKind)),
                new XElement(A + "avLst"));

        return new XElement(P + "spPr",
            xfrm,
            geomEl,
            shape.Fill is not null ? BuildFillEl(shape.Fill, scheme, fillBlipRelId) : null,
            shape.Outline is not null ? BuildOutlineEl(shape.Outline) : null,
            shape.Effects is not null ? BuildEffectLstEl(shape.Effects) : null,
            shape.Effects is not null ? BuildScene3dEl(shape.Effects) : null,
            shape.Effects is not null ? BuildSp3dEl(shape.Effects) : null);
    }

    private static XElement BuildCustGeomEl(List<CustomGeometryPath> paths)
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

        return new XElement(A + "custGeom",
            new XElement(A + "avLst"),
            new XElement(A + "gdLst"),
            new XElement(A + "ahLst"),
            new XElement(A + "cxnLst"),
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

    private static XElement BuildGraphicFrameEl(SlideShape shape, PresentationColorScheme scheme)
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

        return new XElement(P + "graphicFrame",
            new XElement(P + "nvGraphicFramePr",
                new XElement(P + "cNvPr",
                    new XAttribute("id", shape.Id),
                    new XAttribute("name", shape.Name)),
                new XElement(P + "cNvGraphicFramePr",
                    new XElement(A + "graphicFrameLocks",
                        new XAttribute("noGrp", "1"))),
                new XElement(P + "nvPr")),
            xfrm,
            new XElement(A + "graphic",
                new XElement(A + "graphicData",
                    new XAttribute("uri", DrawingTableUri),
                    BuildTableEl(table, scheme))));
    }

    // ── Chart / graphicFrame elements ─────────────────────────────────────────────

    private const string DrawingChartUri = "http://schemas.openxmlformats.org/drawingml/2006/chart";
    private static readonly XNamespace CChartNs =
        "http://schemas.openxmlformats.org/drawingml/2006/chart";

    /// <summary>
    /// Builds the p:graphicFrame element for a chart shape.
    /// <paramref name="mediaById"/> carries chart rel IDs added by
    /// <see cref="WriteSlideCharts"/> (keyed by shape.Id).
    /// </summary>
    private static XElement BuildChartGraphicFrameEl(
        SlideShape shape, Dictionary<uint, string> mediaById)
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

        return new XElement(P + "graphicFrame",
            new XElement(P + "nvGraphicFramePr",
                new XElement(P + "cNvPr",
                    new XAttribute("id", shape.Id),
                    new XAttribute("name", shape.Name)),
                new XElement(P + "cNvGraphicFramePr",
                    new XElement(A + "graphicFrameLocks",
                        new XAttribute("noGrp", "1"))),
                new XElement(P + "nvPr")),
            xfrm,
            new XElement(A + "graphic",
                new XElement(A + "graphicData",
                    new XAttribute("uri", DrawingChartUri),
                    new XElement(CChartNs + "chart",
                        // Declare the c: prefix so PowerPoint sees <c:chart .../>
                        new XAttribute(XNamespace.Xmlns + "c", DrawingChartUri),
                        new XAttribute(R + "id", chartRelId)))));
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
        SlideShape shape, Dictionary<string, string>? relIdRemap)
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
                new XElement(P + "cNvPr",
                    new XAttribute("id", shape.Id),
                    new XAttribute("name", shape.Name)),
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

    private static XElement BuildTableEl(TableShape table, PresentationColorScheme scheme)
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
        var rowEls = table.Rows.Select(row => BuildTableRowEl(row, scheme));

        return new XElement(A + "tbl", tblPr, tblGrid, rowEls);
    }

    private static XElement BuildTableRowEl(TableRow row, PresentationColorScheme scheme) =>
        new XElement(A + "tr",
            new XAttribute("h", row.HeightEmu),
            row.Cells.Select(cell => BuildTableCellEl(cell, scheme)));

    private static XElement BuildTableCellEl(TableCell cell, PresentationColorScheme scheme)
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
                BuildLstStyleEl(cell.TextBody.LstStyle),
                cell.TextBody.Paragraphs.Select(p => BuildParaEl(p)));
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
        if (cell.InsetLeftPt.HasValue)   tcPr.Add(new XAttribute("marL", (long)Math.Round(cell.InsetLeftPt.Value * 12700)));
        if (cell.InsetRightPt.HasValue)  tcPr.Add(new XAttribute("marR", (long)Math.Round(cell.InsetRightPt.Value * 12700)));
        if (cell.InsetTopPt.HasValue)    tcPr.Add(new XAttribute("marT", (long)Math.Round(cell.InsetTopPt.Value * 12700)));
        if (cell.InsetBottomPt.HasValue) tcPr.Add(new XAttribute("marB", (long)Math.Round(cell.InsetBottomPt.Value * 12700)));
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
                tcPr.Add(new XElement(A + "fill", fillEl));
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
                new XAttribute("w", (long)Math.Round(v.WidthPt * 12700)),
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
                new XAttribute("w", (long)Math.Round(gv.WidthPt * 12700)),
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
        if (color.SchemeColor is { } sc)
        {
            var el = new XElement(A + "schemeClr",
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
            return el;
        }
        return new XElement(A + "srgbClr", new XAttribute("val", FmtColor(color.Resolved)));
    }

    // ── Outline elements ──────────────────────────────────────────────────────────

    private static XElement BuildOutlineEl(ShapeOutline outline) =>
        outline switch
        {
            ShapeOutline.None => new XElement(A + "ln", new XElement(A + "noFill")),
            ShapeOutline.Visible v => new XElement(A + "ln",
                new XAttribute("w", (long)Math.Round(v.WidthPt * 12700)),
                new XElement(A + "solidFill", BuildColorEl(v.Color)),
                v.Dash != OutlineDash.Solid
                    ? new XElement(A + "prstDash", new XAttribute("val", ToDashStr(v.Dash)))
                    : null),
            // Wave 22B: gradient outline
            ShapeOutline.GradientVisible gv => new XElement(A + "ln",
                new XAttribute("w", (long)Math.Round(gv.WidthPt * 12700)),
                BuildGradFillEl(gv.Gradient),
                gv.Dash != OutlineDash.Solid
                    ? new XElement(A + "prstDash", new XAttribute("val", ToDashStr(gv.Dash)))
                    : null),
            _ => new XElement(A + "ln")
        };

    // ── a:lstStyle helper ─────────────────────────────────────────────────────────

    /// <summary>
    /// Emits an <c>a:lstStyle</c> element. When <paramref name="levels"/> is null (no explicit list
    /// style) emits an empty element so the XML remains valid. When levels are present each
    /// non-null level is emitted as <c>a:lvlNpPr</c>.
    /// </summary>
    private static XElement BuildLstStyleEl(TextStyleLevels? levels)
    {
        if (levels is null || !levels.HasAny)
            return new XElement(A + "lstStyle");

        var el = new XElement(A + "lstStyle");
        for (int i = 0; i < 9; i++)
        {
            var level = levels[i];
            if (level is null) continue;
            el.Add(BuildLvlpPrEl($"lvl{i + 1}pPr", level));
        }
        return el;
    }

    // ── TextBody elements ─────────────────────────────────────────────────────────

    private static XElement BuildTxBodyEl(TextBody body, PresentationColorScheme scheme,
        Dictionary<string, string>? hlinkRelIds = null,
        List<Slide>? allSlides = null)
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
        if (body.InsetLeftPt.HasValue) bodyPr.Add(new XAttribute("lIns", (long)Math.Round(body.InsetLeftPt.Value * 12700)));
        if (body.InsetRightPt.HasValue) bodyPr.Add(new XAttribute("rIns", (long)Math.Round(body.InsetRightPt.Value * 12700)));
        if (body.InsetTopPt.HasValue) bodyPr.Add(new XAttribute("tIns", (long)Math.Round(body.InsetTopPt.Value * 12700)));
        if (body.InsetBottomPt.HasValue) bodyPr.Add(new XAttribute("bIns", (long)Math.Round(body.InsetBottomPt.Value * 12700)));
        // Wave 19A: write normAutofit with cached fontScale/lnSpcReduction
        if (body.AutoFit)
        {
            var nafEl = new XElement(A + "normAutofit");
            if (body.FontScalePPT.HasValue && body.FontScalePPT.Value > 0)
                nafEl.Add(new XAttribute("fontScale", body.FontScalePPT.Value));
            if (body.LnSpcReductionPPT.HasValue && body.LnSpcReductionPPT.Value > 0)
                nafEl.Add(new XAttribute("lnSpcReduction", body.LnSpcReductionPPT.Value));
            bodyPr.Add(nafEl);
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
            BuildLstStyleEl(body.LstStyle),
            body.Paragraphs.Select(p => BuildParaEl(p, hlinkRelIds, allSlides)));
    }

    private static XElement BuildParaEl(Paragraph para,
        Dictionary<string, string>? hlinkRelIds = null,
        List<Slide>? allSlides = null)
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

        switch (para.BulletKind)
        {
            case BulletKind.None:
                pPr.Add(new XElement(A + "buNone")); hasPPr = true; break;
            case BulletKind.Char:
                // Write buClr/buSzPct/buFont when set (Wave 19A)
                if (para.BulletColor is not null)
                {
                    // Preserve the original scheme/sRGB color reference via the shared helper
                    // (BU4: previously always emitted srgbClr, losing schemeClr theme linkage).
                    pPr.Add(new XElement(A + "buClr", BuildColorEl(para.BulletColor)));
                    hasPPr = true;
                }
                if (para.BulletSizePct.HasValue) { pPr.Add(new XElement(A + "buSzPct", new XAttribute("val", para.BulletSizePct.Value))); hasPPr = true; }
                if (!string.IsNullOrEmpty(para.BulletFontFamily)) { pPr.Add(new XElement(A + "buFont", new XAttribute("typeface", para.BulletFontFamily))); hasPPr = true; }
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
                if (para.AutoNumStartAt != 1) autoNumEl.Add(new XAttribute("startAt", para.AutoNumStartAt));
                pPr.Add(autoNumEl); hasPPr = true; break;
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
            var fldRPrAttrs = BuildFieldRPrAttrs(fld);
            return new XElement(A + "fld",
                new XAttribute("id", fldId),
                new XAttribute("type", fld.FieldType),
                fldRPrAttrs is not null ? new XElement(A + "rPr", fldRPrAttrs) : null,
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
        if (run.FontSizePt.HasValue)
            rPr.Add(new XAttribute("sz", (int)Math.Round(run.FontSizePt.Value * 100)));

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

        // Wave 16A: text shadow — a:effectLst AFTER fill group
        if (run.TextShadow is not null)
        {
            var ts = run.TextShadow;
            var shdwColorEl = BuildColorEl(ts.Color);
            // Embed alpha on the color element only when < fully opaque;
            // omitting a:alpha means 100% opaque in DrawingML.
            if (ts.Alpha < 255)
                shdwColorEl.Add(new XElement(A + "alpha",
                    new XAttribute("val", (long)Math.Round(ts.Alpha / 255.0 * 100000))));
            rPr.Add(new XElement(A + "effectLst",
                new XElement(A + "outerShdw",
                    new XAttribute("blurRad", (long)Math.Round(ts.BlurPt * 12700)),
                    new XAttribute("dist",    (long)Math.Round(ts.DistPt * 12700)),
                    new XAttribute("dir",     (long)Math.Round(ts.DirDeg * 60000)),
                    shdwColorEl)));
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
                var ext = ContentTypeToExtension(ct);
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
                var ext = ContentTypeToExtension(ct);
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

    /// <summary>
    /// Writes audio/video bytes for Media shapes. Returns (shapeId, relId, mediaPath, isVideo) tuples.
    /// The relId uses prefix "rIdVid" to avoid collision with the "rIdMedia" image prefix.
    /// </summary>
    private static List<(uint shapeId, string relId, string mediaPath, bool isVideo)> WriteSlideMediaFiles(
        ZipArchive archive, Slide slide, int slideIndex)
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
            var mediaPath = $"ppt/media/slide{slideIndex}_video{n}.{ext}";
            var relId     = $"rIdVid{n}";

            WriteRawEntry(archive, mediaPath, media.Bytes);
            result.Add((shape.Id, relId, mediaPath, media.IsVideo));
            n++;
        }

        return result;
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
    private static List<(uint shapeId, string relId, string chartPath)> WriteSlideCharts(
        ZipArchive archive, Slide slide, ref int globalChartIndex)
    {
        var result = new List<(uint, string, string)>();

        foreach (var shape in AllShapes(slide.Shapes))
        {
            if (shape.Kind != SlideShapeKind.Chart || shape.Chart is null)
                continue;

            var chartPath = PptxChartWriter.WriteChartPart(archive, shape.Chart, globalChartIndex);
            var relId = $"rIdChart{globalChartIndex}";
            result.Add((shape.Id, relId, chartPath));
            globalChartIndex++;
        }

        return result;
    }

    // ── SmartArt diagram part writing ─────────────────────────────────────────────

    /// <summary>
    /// Writes all diagram parts (data/layout/quickStyle/colors/drawing) for SmartArt shapes
    /// verbatim from the stored raw bytes. Also writes each part's rels file (if any).
    /// Returns:
    ///   - slideRels: (newRelId, relType, target) tuples for the slide rels file (one per dm/lo/qs/cs key).
    ///   - relIdRemap: per-shape (shape.Id → (key → newRelId)) so BuildSmartArtGraphicFrameEl
    ///     can emit only the r: attributes that have an actual written part (S2),
    ///     using fresh collision-free relIds (S4).
    /// The drawing part rels are internal to the data part's rels — not in the slide rels.
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
                    var partDir  = GetDirectory(part.PartPath);
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
                var imgExt = ContentTypeToExtension(pic.ContentType ?? "image/png");
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
        SlideShape shape, Dictionary<uint, string> mediaById)
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
                new XElement(P + "cNvPr",
                    new XAttribute("id", shape.Id),
                    new XAttribute("name", shape.Name)),
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

    private static void WriteRels(ZipArchive archive, string partPath, RelsDoc rels)
    {
        var dir = GetDirectory(partPath);
        var file = partPath[(partPath.LastIndexOf('/') + 1)..];
        var relsPath = string.IsNullOrEmpty(dir)
            ? $"_rels/{file}.rels"
            : $"{dir}/_rels/{file}.rels";

        WriteEntry(archive, relsPath, rels.ToXDocument());
    }

    private static string GetDirectory(string path)
    {
        var i = path.LastIndexOf('/');
        return i < 0 ? string.Empty : path[..i];
    }

    // ── Small utilities ───────────────────────────────────────────────────────────

    private static XAttribute NsAttr(string prefix, XNamespace ns) =>
        new XAttribute(XNamespace.Xmlns + prefix, ns.NamespaceName);

    private static XElement CnvPr(uint id, string name) =>
        new XElement(P + "cNvPr", new XAttribute("id", id), new XAttribute("name", name));

    /// <summary>
    /// Builds a cNvPr element and, when the shape carries a hyperlink, appends an a:hlinkClick child.
    /// </summary>
    private static XElement CnvPrWithHlink(uint id, string name, Hyperlink? hlink,
        Dictionary<string, string>? hlinkRelIds, List<Slide>? allSlides)
    {
        var el = new XElement(P + "cNvPr", new XAttribute("id", id), new XAttribute("name", name));
        if (hlink is not null)
        {
            var hlinkEl = BuildHlinkClickEl(hlink, hlinkRelIds, allSlides);
            if (hlinkEl is not null) el.Add(hlinkEl);
        }
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

    private static string FmtColor(SrgbColor c) => $"{c.R:X2}{c.G:X2}{c.B:X2}";

    private static string GetShapeId(SlideShape s) => s.Id.ToString(CultureInfo.InvariantCulture);

    private static string ContentTypeToExtension(string ct) =>
        ct.ToLowerInvariant() switch
        {
            "image/jpeg" or "image/jpg" => "jpg",
            "image/gif" => "gif",
            "image/bmp" => "bmp",
            "image/tiff" => "tiff",
            "image/svg+xml" => "svg",
            "image/x-wmf" or "image/wmf" => "wmf",
            "image/x-emf" or "image/emf" => "emf",
            _ => "png"
        };

    // II1: maps audio/video content types to their file extensions (mirrors WriteSlideMediaFiles switch)
    private static string MediaContentTypeToExtension(string ct) =>
        ct.ToLowerInvariant() switch
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

    // ── RelsDoc helper ────────────────────────────────────────────────────────────

    private sealed class RelsDoc
    {
        private readonly List<(string id, string type, string target, bool external)> _rels = new();

        public void Add(string id, string type, string target, bool external = false)
            => _rels.Add((id, type, target, external));

        public XDocument ToXDocument() =>
            new XDocument(
                new XDeclaration("1.0", "UTF-8", "yes"),
                // OPC spec §9.3: Relationships element MUST use the default namespace,
                // not a prefixed namespace (PowerPoint rejects r:Relationships).
                new XElement(PkgRels + "Relationships",
                    _rels.Select(r =>
                    {
                        var el = new XElement(PkgRels + "Relationship",
                            new XAttribute("Id", r.id),
                            new XAttribute("Type", r.type),
                            new XAttribute("Target", r.target));
                        if (r.external)
                            el.Add(new XAttribute("TargetMode", "External"));
                        return el;
                    })));

        // Re-expose PkgRels from outer class
        private static readonly XNamespace PkgRels = PptxPackageWriter.PkgRels;
    }
}
