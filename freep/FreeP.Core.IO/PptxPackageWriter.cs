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

        // --- 1. [Content_Types].xml ---
        var ctXml = BuildContentTypesXml(presentation, masters, layouts);
        WriteEntry(archive, "[Content_Types].xml", ctXml);

        // --- 2. Root rels ---
        var rootRels = new RelsDoc();
        rootRels.Add("rId1", OfficeDocRelType, "ppt/presentation.xml");
        rootRels.Add("rId2", CorePropsRelType, "docProps/core.xml");
        WriteEntry(archive, "_rels/.rels", rootRels.ToXDocument());

        // --- 3. Core properties ---
        WriteEntry(archive, "docProps/core.xml", BuildCorePropsXml(presentation.Properties));

        // --- 4. Theme ---
        WriteEntry(archive, "ppt/theme/theme1.xml", BuildThemeXml(presentation.Theme));

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

            // Find the master path for this layout's master
            var masterIdx = masters.FindIndex(m => m.Id == layout.MasterId);
            if (masterIdx < 0) masterIdx = 0;
            var masterPath = $"ppt/slideMasters/slideMaster{masterIdx + 1}.xml";

            // Layout xml
            WriteEntry(archive, layoutPath, BuildSlideLayoutXml(layout, presentation.Theme.ColorScheme));

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

            WriteEntry(archive, masterPath, BuildSlideMasterXml(master, presentation.Theme.ColorScheme, layoutRelIds));

            // Master rels: rId1=theme, rId2..=layouts
            var masterRels = new RelsDoc();
            masterRels.Add("rId1", ThemeRelType, "../theme/theme1.xml");
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

        for (int si = 0; si < presentation.Slides.Count; si++)
        {
            var slide = presentation.Slides[si];
            var slidePath = $"ppt/slides/slide{si + 1}.xml";
            var slideRelId = $"rId{si + 2}";

            // Find layout
            var layout = layouts.FirstOrDefault(l => l.Id == slide.LayoutId) ?? layouts[0];
            var layoutPath = layoutPaths.TryGetValue(layout.Id, out var lp2) ? lp2 : layoutPaths.Values.First();

            // Write media (images) into the archive, get back rel-id map
            var mediaRelIds = WriteSlideMedia(archive, slide, si + 1);

            // Slide xml
            WriteEntry(archive, slidePath, BuildSlideXml(slide, presentation.Theme.ColorScheme, mediaRelIds));

            // Slide rels: rId1=layout, rIdMedia1..=images
            var slideRels = new RelsDoc();
            slideRels.Add("rId1", SlideLayoutRelType, $"../slideLayouts/{layoutPath.Split('/').Last()}");
            foreach (var (shapeName, mediaRelId, mediaPath) in mediaRelIds)
                slideRels.Add(mediaRelId, ImageRelType, $"../media/{mediaPath.Split('/').Last()}");
            WriteRels(archive, slidePath, slideRels);

            presRels.Add(slideRelId, SlideRelType, $"slides/slide{si + 1}.xml");
            sldIdElements.Add(new XElement(P + "sldId",
                new XAttribute("id", sldIdCounter++),
                new XAttribute(R + "id", slideRelId)));
        }

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

        WriteRels(archive, "ppt/presentation.xml", presRels);

        // --- 10. presentation.xml (last, so sldIdElements are complete) ---
        var masterRelIds = Enumerable.Range(0, masters.Count)
            .Select(i => ($"rId{masterRelIdStart + i}", $"ppt/slideMasters/slideMaster{i+1}.xml"))
            .ToList();

        WriteEntry(archive, "ppt/presentation.xml",
            BuildPresentationXml(presentation, sldIdElements, masterRelIds));
    }

    // ── [Content_Types].xml ───────────────────────────────────────────────────────

    private static XDocument BuildContentTypesXml(
        Presentation p, List<SlideMaster> masters, List<SlideLayout> layouts)
    {
        var CT = XNamespace.Get("http://schemas.openxmlformats.org/package/2006/content-types");

        var overrides = new List<XElement>
        {
            Override(CT, "/ppt/presentation.xml", PresentationCT),
            Override(CT, "/ppt/theme/theme1.xml", ThemeCT),
            Override(CT, "/ppt/presProps.xml", PresPropsCT),
            Override(CT, "/ppt/viewProps.xml", ViewPropsCT),
            Override(CT, "/ppt/tableStyles.xml", TableStylesCT),
            Override(CT, "/docProps/core.xml", CorePropsCT),
        };

        for (int mi = 0; mi < masters.Count; mi++)
            overrides.Add(Override(CT, $"/ppt/slideMasters/slideMaster{mi + 1}.xml", SlideMasterCT));

        for (int li = 0; li < layouts.Count; li++)
            overrides.Add(Override(CT, $"/ppt/slideLayouts/slideLayout{li + 1}.xml", SlideLayoutCT));

        for (int si = 0; si < p.Slides.Count; si++)
            overrides.Add(Override(CT, $"/ppt/slides/slide{si + 1}.xml", SlideCT));

        // Collect image content types
        foreach (var slide in p.Slides)
        {
            foreach (var shape in AllShapes(slide.Shapes))
            {
                if (shape.Kind == SlideShapeKind.Picture && shape.Picture?.Bytes is { Length: > 0 })
                {
                    var ext = ContentTypeToExtension(shape.Picture.ContentType ?? "image/png");
                    var ct = shape.Picture.ContentType ?? "image/png";
                    // add defaults as needed
                    overrides.Add(Override(CT, $"/ppt/media/media_{GetShapeId(shape)}.{ext}", ct));
                }
            }
        }

        return new XDocument(
            new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(CT + "Types",
                new XElement(CT + "Default", new XAttribute("Extension", "rels"), new XAttribute("ContentType", RelsCT)),
                new XElement(CT + "Default", new XAttribute("Extension", "xml"), new XAttribute("ContentType", "application/xml")),
                new XElement(CT + "Default", new XAttribute("Extension", "png"), new XAttribute("ContentType", "image/png")),
                new XElement(CT + "Default", new XAttribute("Extension", "jpg"), new XAttribute("ContentType", "image/jpeg")),
                new XElement(CT + "Default", new XAttribute("Extension", "jpeg"), new XAttribute("ContentType", "image/jpeg")),
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
        List<(string relId, string masterPath)> masterRelIds) =>
        new XDocument(
            new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(P + "presentation",
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
                    new XAttribute("cy", 9144000))));

    // ── slide.xml ────────────────────────────────────────────────────────────────

    private static XDocument BuildSlideXml(
        Slide slide, PresentationColorScheme scheme,
        List<(string shapeName, string relId, string mediaPath)> mediaRelIds)
    {
        var mediaByName = mediaRelIds.ToDictionary(m => m.shapeName, m => m.relId);
        return new XDocument(
            new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(P + "sld",
                NsAttr("p", P), NsAttr("a", A), NsAttr("r", R),
                slide.Background is not null
                    ? new XElement(P + "bg",
                        new XElement(P + "bgPr",
                            BuildFillEl(slide.Background, scheme),
                            new XElement(A + "effectLst")))
                    : null,
                new XElement(P + "cSld",
                    new XElement(P + "spTree",
                        GrpSpHeader(),
                        slide.Shapes.Select(s => BuildShapeEl(s, scheme, mediaByName))))));
    }

    // ── slideLayout.xml ──────────────────────────────────────────────────────────

    private static XDocument BuildSlideLayoutXml(SlideLayout layout, PresentationColorScheme scheme) =>
        new XDocument(
            new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(P + "sldLayout",
                NsAttr("p", P), NsAttr("a", A), NsAttr("r", R),
                new XAttribute("type", ToLayoutTypeStr(layout.LayoutType)),
                new XAttribute("preserve", "1"),
                new XElement(P + "cSld",
                    new XAttribute("name", layout.Name),
                    new XElement(P + "spTree",
                        GrpSpHeader(),
                        layout.Placeholders.Select(s => BuildShapeEl(s, scheme, new())))),
                new XElement(P + "clrMapOvr",
                    new XElement(A + "masterClrMapping"))));

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
                        master.Placeholders.Select(s => BuildShapeEl(s, scheme, new())))),
                BuildColorMap(),
                new XElement(P + "sldLayoutIdLst",
                    layoutRelIds.Select((lr, i) =>
                        new XElement(P + "sldLayoutId",
                            new XAttribute("id", 2147483649u + (uint)i),
                            new XAttribute(R + "id", lr.relId))))));

    private static XElement BuildColorMap() =>
        new XElement(P + "clrMap",
            new XAttribute("bg1", "lt1"), new XAttribute("tx1", "dk1"),
            new XAttribute("bg2", "lt2"), new XAttribute("tx2", "dk2"),
            new XAttribute("accent1", "accent1"), new XAttribute("accent2", "accent2"),
            new XAttribute("accent3", "accent3"), new XAttribute("accent4", "accent4"),
            new XAttribute("accent5", "accent5"), new XAttribute("accent6", "accent6"),
            new XAttribute("hlink", "hlink"), new XAttribute("folHlink", "folHlink"));

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

    private static XElement BuildShapeEl(
        SlideShape shape, PresentationColorScheme scheme, Dictionary<string, string> mediaByName) =>
        shape.Kind switch
        {
            SlideShapeKind.Picture => BuildPicEl(shape, mediaByName),
            SlideShapeKind.Group => BuildGrpSpEl(shape, scheme, mediaByName),
            SlideShapeKind.Connector => BuildCxnSpEl(shape, scheme),
            _ => BuildSpEl(shape, scheme)
        };

    private static XElement BuildSpEl(SlideShape shape, PresentationColorScheme scheme) =>
        new XElement(P + "sp",
            new XElement(P + "nvSpPr",
                CnvPr(shape.Id, shape.Name),
                new XElement(P + "cNvSpPr"),
                new XElement(P + "nvPr",
                    shape.Placeholder is not null ? BuildPhEl(shape.Placeholder) : null)),
            BuildSpPrEl(shape, scheme),
            shape.TextBody is not null ? BuildTxBodyEl(shape.TextBody, scheme) : null);

    private static XElement BuildCxnSpEl(SlideShape shape, PresentationColorScheme scheme) =>
        new XElement(P + "cxnSp",
            new XElement(P + "nvCxnSpPr",
                CnvPr(shape.Id, shape.Name),
                new XElement(P + "cNvCxnSpPr"),
                new XElement(P + "nvPr")),
            BuildSpPrEl(shape, scheme));

    private static XElement BuildPicEl(SlideShape shape, Dictionary<string, string> mediaByName)
    {
        mediaByName.TryGetValue(shape.Name, out var embedRelId);
        embedRelId ??= mediaByName.Values.FirstOrDefault() ?? "rIdMedia1";

        return new XElement(P + "pic",
            new XElement(P + "nvPicPr",
                CnvPr(shape.Id, shape.Name),
                new XElement(P + "cNvPicPr"),
                new XElement(P + "nvPr")),
            new XElement(P + "blipFill",
                new XElement(A + "blip", new XAttribute(R + "embed", embedRelId)),
                new XElement(A + "stretch", new XElement(A + "fillRect"))),
            BuildSpPrEl(shape, PresentationColorScheme.CreateDefault(), forcePrst: "rect"));
    }

    private static XElement BuildGrpSpEl(
        SlideShape shape, PresentationColorScheme scheme, Dictionary<string, string> mediaByName) =>
        new XElement(P + "grpSp",
            new XElement(P + "nvGrpSpPr",
                CnvPr(shape.Id, shape.Name),
                new XElement(P + "cNvGrpSpPr"),
                new XElement(P + "nvPr")),
            BuildSpPrEl(shape, scheme),
            shape.Children.Select(c => BuildShapeEl(c, scheme, mediaByName)));

    private static XElement BuildSpPrEl(SlideShape shape, PresentationColorScheme scheme, string? forcePrst = null)
    {
        var xfrm = new XElement(A + "xfrm");
        if (shape.RotationDeg != 0)
            xfrm.Add(new XAttribute("rot", (long)Math.Round(shape.RotationDeg * 60000)));
        if (shape.FlipH) xfrm.Add(new XAttribute("flipH", "1"));
        if (shape.FlipV) xfrm.Add(new XAttribute("flipV", "1"));
        xfrm.Add(new XElement(A + "off", new XAttribute("x", shape.OffsetXEmu), new XAttribute("y", shape.OffsetYEmu)));
        xfrm.Add(new XElement(A + "ext", new XAttribute("cx", shape.ExtentCxEmu), new XAttribute("cy", shape.ExtentCyEmu)));

        return new XElement(P + "spPr",
            xfrm,
            new XElement(A + "prstGeom",
                new XAttribute("prst", forcePrst ?? PptxShapeKindMap.ToPreset(shape.AutoShapeKind)),
                new XElement(A + "avLst")),
            shape.Fill is not null ? BuildFillEl(shape.Fill, scheme) : null,
            shape.Outline is not null ? BuildOutlineEl(shape.Outline) : null);
    }

    // ── Fill elements ─────────────────────────────────────────────────────────────

    private static XElement? BuildFillEl(ShapeFill fill, PresentationColorScheme scheme) =>
        fill switch
        {
            ShapeFill.None => new XElement(A + "noFill"),
            ShapeFill.Solid s => new XElement(A + "solidFill", BuildColorEl(s.Color)),
            ShapeFill.Gradient g => BuildGradFillEl(g),
            _ => null
        };

    private static XElement BuildGradFillEl(ShapeFill.Gradient g) =>
        new XElement(A + "gradFill",
            new XElement(A + "gsLst",
                new XElement(A + "gs", new XAttribute("pos", "0"),
                    new XElement(A + "solidFill", BuildColorEl(g.StartColor))),
                new XElement(A + "gs", new XAttribute("pos", "100000"),
                    new XElement(A + "solidFill", BuildColorEl(g.EndColor)))),
            new XElement(A + "lin",
                new XAttribute("ang", (long)Math.Round(g.AngleDegrees * 60000)),
                new XAttribute("scaled", "0")));

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
            _ => new XElement(A + "ln")
        };

    // ── TextBody elements ─────────────────────────────────────────────────────────

    private static XElement BuildTxBodyEl(TextBody body, PresentationColorScheme scheme)
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

        if (!body.Wrap) bodyPr.Add(new XAttribute("wrap", "none"));
        if (body.InsetLeftPt.HasValue) bodyPr.Add(new XAttribute("lIns", (long)Math.Round(body.InsetLeftPt.Value * 12700)));
        if (body.InsetRightPt.HasValue) bodyPr.Add(new XAttribute("rIns", (long)Math.Round(body.InsetRightPt.Value * 12700)));
        if (body.InsetTopPt.HasValue) bodyPr.Add(new XAttribute("tIns", (long)Math.Round(body.InsetTopPt.Value * 12700)));
        if (body.InsetBottomPt.HasValue) bodyPr.Add(new XAttribute("bIns", (long)Math.Round(body.InsetBottomPt.Value * 12700)));
        if (body.AutoFit) bodyPr.Add(new XElement(A + "normAutofit"));

        // In PresentationML, the text body inside p:sp is p:txBody (not a:txBody).
        // Body-level elements use a: namespace, paragraphs/runs use a: namespace.
        return new XElement(P + "txBody",
            bodyPr,
            new XElement(A + "lstStyle"),
            body.Paragraphs.Select(p => BuildParaEl(p)));
    }

    private static XElement BuildParaEl(Paragraph para)
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

        switch (para.BulletKind)
        {
            case BulletKind.None:
                pPr.Add(new XElement(A + "buNone")); hasPPr = true; break;
            case BulletKind.Char:
                pPr.Add(new XElement(A + "buChar", new XAttribute("char", para.BulletChar ?? "•"))); hasPPr = true; break;
            case BulletKind.Auto:
                pPr.Add(new XElement(A + "buAutoNum", new XAttribute("type", "arabicPeriod"))); hasPPr = true; break;
        }

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

        return new XElement(A + "p",
            hasPPr ? pPr : null,
            para.Runs.Select(BuildRunEl));
    }

    private static XElement BuildRunEl(Run run)
    {
        if (run.Text == "\n") return new XElement(A + "br");

        var rPr = new XElement(A + "rPr",
            new XAttribute("lang", "en-US"),
            new XAttribute("dirty", "0"));

        if (run.Bold) rPr.Add(new XAttribute("b", "1"));
        if (run.Italic) rPr.Add(new XAttribute("i", "1"));
        if (run.Underline) rPr.Add(new XAttribute("u", "sng"));
        if (run.Strikethrough) rPr.Add(new XAttribute("strike", "sngStrike"));
        if (run.FontSizePt.HasValue)
            rPr.Add(new XAttribute("sz", (int)Math.Round(run.FontSizePt.Value * 100)));
        if (run.Color is not null)
            rPr.Add(new XElement(A + "solidFill", BuildColorEl(run.Color)));
        if (run.FontFamily is not null)
            rPr.Add(new XElement(A + "latin", new XAttribute("typeface", run.FontFamily)));

        return new XElement(A + "r", rPr, new XElement(A + "t", run.Text));
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

    // ── Media writing ─────────────────────────────────────────────────────────────

    private static List<(string shapeName, string relId, string mediaPath)> WriteSlideMedia(
        ZipArchive archive, Slide slide, int slideIndex)
    {
        var result = new List<(string, string, string)>();
        int mediaIdx = 1;

        foreach (var shape in AllShapes(slide.Shapes))
        {
            if (shape.Kind != SlideShapeKind.Picture || shape.Picture?.Bytes is not { Length: > 0 } bytes)
                continue;

            var ct = shape.Picture.ContentType ?? "image/png";
            var ext = ContentTypeToExtension(ct);
            var mediaPath = $"ppt/media/slide{slideIndex}_media{mediaIdx}.{ext}";

            var entry = archive.CreateEntry(mediaPath, CompressionLevel.Optimal);
            using (var es = entry.Open())
                es.Write(bytes);

            var relId = $"rIdMedia{mediaIdx}";
            result.Add((shape.Name, relId, mediaPath));
            mediaIdx++;
        }

        return result;
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
        private readonly List<(string id, string type, string target)> _rels = new();

        public void Add(string id, string type, string target) => _rels.Add((id, type, target));

        public XDocument ToXDocument() =>
            new XDocument(
                new XDeclaration("1.0", "UTF-8", "yes"),
                new XElement(PkgRels + "Relationships",
                    new XAttribute(XNamespace.Xmlns + "r", PkgRels),
                    _rels.Select(r =>
                        new XElement(PkgRels + "Relationship",
                            new XAttribute("Id", r.id),
                            new XAttribute("Type", r.type),
                            new XAttribute("Target", r.target)))));

        // Re-expose PkgRels from outer class
        private static readonly XNamespace PkgRels = PptxPackageWriter.PkgRels;
    }
}
