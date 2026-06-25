using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;
using Free.Shared.Drawing;
using FreeP.Core.Model;

namespace FreeP.Core.IO;

/// <summary>
/// Wave 1B: reads a <c>.pptx</c> OPC package and returns a <see cref="Presentation"/> model.
/// Entry point: <see cref="Read(string)"/> or <see cref="Read(Stream)"/>.
/// </summary>
public static class PptxPackageReader
{
    // ── OOXML namespaces ─────────────────────────────────────────────────────────
    private static readonly XNamespace P   = "http://schemas.openxmlformats.org/presentationml/2006/main";
    private static readonly XNamespace A   = PptxColorReader.A;
    private static readonly XNamespace R   = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace Pkgr = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace Dc  = "http://purl.org/dc/elements/1.1/";
    private static readonly XNamespace Cp  = "http://schemas.openxmlformats.org/package/2006/metadata/core-properties";

    // ── Relationship type constants ───────────────────────────────────────────────
    private const string OfficeDocRelType   = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument";
    private const string SlideRelType       = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/slide";
    private const string SlideMasterRelType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/slideMaster";
    private const string SlideLayoutRelType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/slideLayout";
    private const string ThemeRelType       = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/theme";
    private const string ImageRelType       = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/image";
    private const string CorePropsRelType   = "http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties";

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
        return ReadArchive(archive);
    }

    // ── Core archive reading ──────────────────────────────────────────────────────

    private static Presentation ReadArchive(ZipArchive archive)
    {
        var presentation = new Presentation();

        // Parse root rels to find presentation.xml path
        var rootRels = LoadRels(archive, "_rels/.rels");
        var presPath = GetRelTarget(rootRels, OfficeDocRelType);
        if (presPath is null) return presentation;

        // Normalize path (remove leading /)
        presPath = NormalizePath(presPath);

        // Core properties
        var corePropsPath = GetRelTarget(rootRels, CorePropsRelType);
        if (corePropsPath is not null)
            ReadCoreProperties(archive, NormalizePath(corePropsPath), presentation.Properties);

        // Parse presentation.xml
        var presXml = LoadXml(archive, presPath);
        if (presXml?.Root is null) return presentation;

        var presRoot = presXml.Root;
        var presDir = GetDirectory(presPath);

        // Slide size
        var sldSz = presRoot.Element(P + "sldSz");
        if (sldSz is not null)
        {
            if (long.TryParse(sldSz.Attribute("cx")?.Value, out var cx) && cx > 0)
                presentation.SlideSizeCxEmu = cx;
            if (long.TryParse(sldSz.Attribute("cy")?.Value, out var cy) && cy > 0)
                presentation.SlideSizeCyEmu = cy;
        }

        // Rels for presentation.xml
        var presRels = LoadRels(archive, GetRelsPath(presPath));

        // Slide masters → layouts
        var masterRelEntries = presRels.Where(r => r.type == SlideMasterRelType).ToList();
        foreach (var (masterId, _, masterTarget) in masterRelEntries)
        {
            var masterPath = ResolvePath(presDir, masterTarget);
            var (master, theme) = ReadSlideMaster(archive, masterPath, masterId);
            presentation.Masters.Add(master);
            if (theme is not null)
                presentation.Theme = theme;

            var masterDir = GetDirectory(masterPath);
            var masterRels = LoadRels(archive, GetRelsPath(masterPath));

            foreach (var (layoutId, _, layoutTarget) in masterRels.Where(r => r.type == SlideLayoutRelType))
            {
                var layoutPath = ResolvePath(masterDir, layoutTarget);
                var layout = ReadSlideLayout(archive, layoutPath, layoutId, master.Id, presentation.Theme.ColorScheme);
                presentation.Layouts.Add(layout);
            }
        }

        // Slides in order from sldIdLst
        var slideRelEntries = presRels.ToDictionary(r => r.id, StringComparer.OrdinalIgnoreCase);
        var sldIdList = presRoot.Element(P + "sldIdLst")?.Elements(P + "sldId").ToList() ?? new();

        foreach (var sldIdEl in sldIdList)
        {
            var rId = sldIdEl.Attribute(R + "id")?.Value;
            if (string.IsNullOrWhiteSpace(rId) || !slideRelEntries.TryGetValue(rId, out var slideRel))
                continue;
            if (slideRel.type != SlideRelType) continue;

            var slidePath = ResolvePath(presDir, slideRel.target);
            var slide = ReadSlide(archive, slidePath, rId, presentation.Theme.ColorScheme, presentation.Layouts);
            presentation.Slides.Add(slide);
        }

        return presentation;
    }

    // ── Core properties ──────────────────────────────────────────────────────────

    private static void ReadCoreProperties(ZipArchive archive, string path, PresentationProperties props)
    {
        var xml = LoadXml(archive, path);
        if (xml?.Root is null) return;

        props.Title = xml.Root.Element(Dc + "title")?.Value;
        props.Author = xml.Root.Element(Dc + "creator")?.Value;
        props.Subject = xml.Root.Element(Dc + "subject")?.Value;
        props.Keywords = xml.Root.Element(Cp + "keywords")?.Value;
        props.Comments = xml.Root.Element(Dc + "description")?.Value;
    }

    // ── Theme ────────────────────────────────────────────────────────────────────

    private static PresentationTheme? ReadTheme(ZipArchive archive, string masterPath)
    {
        var masterDir = GetDirectory(masterPath);
        var masterRels = LoadRels(archive, GetRelsPath(masterPath));
        var themeTarget = GetRelTarget(masterRels, ThemeRelType);
        if (themeTarget is null) return null;

        var themePath = ResolvePath(masterDir, themeTarget);
        var xml = LoadXml(archive, themePath);
        if (xml?.Root is null) return null;

        var theme = new PresentationTheme
        {
            Name = xml.Root.Attribute("name")?.Value ?? "Office Theme"
        };

        var clrScheme = xml.Root.Element(A + "themeElements")?.Element(A + "clrScheme");
        if (clrScheme is not null)
            ReadColorScheme(clrScheme, theme.ColorScheme);

        var fontScheme = xml.Root.Element(A + "themeElements")?.Element(A + "fontScheme");
        if (fontScheme is not null)
        {
            theme.FontScheme.MajorLatinFont = fontScheme
                .Element(A + "majorFont")?.Element(A + "latin")?.Attribute("typeface")?.Value ?? "Calibri Light";
            theme.FontScheme.MinorLatinFont = fontScheme
                .Element(A + "minorFont")?.Element(A + "latin")?.Attribute("typeface")?.Value ?? "Calibri";
        }

        return theme;
    }

    private static void ReadColorScheme(XElement clrScheme, PresentationColorScheme scheme)
    {
        ReadColorSlot(clrScheme, "dk1", ThemeColorSlot.Dk1, scheme);
        ReadColorSlot(clrScheme, "lt1", ThemeColorSlot.Lt1, scheme);
        ReadColorSlot(clrScheme, "dk2", ThemeColorSlot.Dk2, scheme);
        ReadColorSlot(clrScheme, "lt2", ThemeColorSlot.Lt2, scheme);
        ReadColorSlot(clrScheme, "accent1", ThemeColorSlot.Accent1, scheme);
        ReadColorSlot(clrScheme, "accent2", ThemeColorSlot.Accent2, scheme);
        ReadColorSlot(clrScheme, "accent3", ThemeColorSlot.Accent3, scheme);
        ReadColorSlot(clrScheme, "accent4", ThemeColorSlot.Accent4, scheme);
        ReadColorSlot(clrScheme, "accent5", ThemeColorSlot.Accent5, scheme);
        ReadColorSlot(clrScheme, "accent6", ThemeColorSlot.Accent6, scheme);
        ReadColorSlot(clrScheme, "hlink", ThemeColorSlot.HLink, scheme);
        ReadColorSlot(clrScheme, "folHlink", ThemeColorSlot.FolHLink, scheme);
    }

    private static void ReadColorSlot(XElement clrScheme, string elName, ThemeColorSlot slot, PresentationColorScheme scheme)
    {
        var el = clrScheme.Element(A + elName);
        if (el is null) return;

        var srgb = el.Element(A + "srgbClr")?.Attribute("val")?.Value;
        if (!string.IsNullOrWhiteSpace(srgb) && TryParseHex6(srgb, out var rgb)) { scheme[slot] = rgb; return; }

        var last = el.Element(A + "sysClr")?.Attribute("lastClr")?.Value;
        if (!string.IsNullOrWhiteSpace(last) && TryParseHex6(last, out var sysRgb)) scheme[slot] = sysRgb;
    }

    // ── Slide Master ─────────────────────────────────────────────────────────────

    private static (SlideMaster master, PresentationTheme? theme) ReadSlideMaster(
        ZipArchive archive, string masterPath, string masterId)
    {
        var master = new SlideMaster { Id = masterId };
        var theme = ReadTheme(archive, masterPath);

        var xml = LoadXml(archive, masterPath);
        if (xml?.Root is null) return (master, theme);

        var scheme = theme?.ColorScheme ?? PresentationColorScheme.CreateDefault();

        var bg = xml.Root.Element(P + "bg");
        if (bg is not null) master.Background = ReadBackground(bg, scheme);

        var spTree = xml.Root.Element(P + "cSld")?.Element(P + "spTree");
        if (spTree is not null)
        {
            foreach (var shape in ReadShapesFromTree(spTree, archive, masterPath, scheme))
                master.Placeholders.Add(shape);
        }

        return (master, theme);
    }

    // ── Slide Layout ─────────────────────────────────────────────────────────────

    private static SlideLayout ReadSlideLayout(
        ZipArchive archive, string layoutPath, string layoutId, string masterId, PresentationColorScheme scheme)
    {
        var layout = new SlideLayout { Id = layoutId, MasterId = masterId };

        var xml = LoadXml(archive, layoutPath);
        if (xml?.Root is null) return layout;

        layout.Name = xml.Root.Element(P + "cSld")?.Attribute("name")?.Value ?? string.Empty;
        layout.LayoutType = MapLayoutType(xml.Root.Attribute("type")?.Value);

        var bg = xml.Root.Element(P + "bg");
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
        PresentationColorScheme scheme, List<SlideLayout> layouts)
    {
        var slide = new Slide { Id = slideId };

        var xml = LoadXml(archive, slidePath);
        if (xml?.Root is null) return slide;

        // Layout via rels
        var slideRels = LoadRels(archive, GetRelsPath(slidePath));
        var layoutTarget = GetRelTarget(slideRels, SlideLayoutRelType);
        if (layoutTarget is not null)
        {
            var layoutPath = ResolvePath(GetDirectory(slidePath), layoutTarget);
            // Match with our loaded layouts by path-suffix heuristic
            slide.LayoutId = MatchLayoutId(layoutPath, layouts);
        }

        var bg = xml.Root.Element(P + "bg");
        if (bg is not null) slide.Background = ReadBackground(bg, scheme);

        var spTree = xml.Root.Element(P + "cSld")?.Element(P + "spTree");
        if (spTree is not null)
        {
            foreach (var shape in ReadShapesFromTree(spTree, archive, slidePath, scheme))
                slide.Shapes.Add(shape);
        }

        return slide;
    }

    private static string? MatchLayoutId(string layoutPath, List<SlideLayout> layouts)
    {
        if (layouts.Count == 0) return null;
        // Match by last segment of path (e.g. "slideLayout1.xml")
        var seg = layoutPath.Split('/').Last();
        for (int i = 0; i < layouts.Count; i++)
        {
            // Layouts are loaded in order; try to infer index from path name
            if (seg.Contains((i + 1).ToString()))
                return layouts[i].Id;
        }
        return layouts[0].Id;
    }

    // ── Shape tree ───────────────────────────────────────────────────────────────

    private static IEnumerable<SlideShape> ReadShapesFromTree(
        XElement spTree, ZipArchive archive, string partPath, PresentationColorScheme scheme)
    {
        foreach (var child in spTree.Elements())
        {
            SlideShape? shape = child.Name.LocalName switch
            {
                "sp" => ReadSp(child, scheme),
                "pic" => ReadPic(child, archive, partPath, scheme),
                "cxnSp" => ReadCxnSp(child, scheme),
                "grpSp" => ReadGrpSp(child, archive, partPath, scheme),
                _ => null
            };

            if (shape is not null)
                yield return shape;
        }
    }

    // ── p:sp ─────────────────────────────────────────────────────────────────────

    private static SlideShape ReadSp(XElement sp, PresentationColorScheme scheme)
    {
        var cNvPr = sp.Element(P + "nvSpPr")?.Element(P + "cNvPr");
        var nvPr = sp.Element(P + "nvSpPr")?.Element(P + "nvPr");

        var shape = new SlideShape
        {
            Id = ParseUint(cNvPr?.Attribute("id")?.Value),
            Name = cNvPr?.Attribute("name")?.Value ?? string.Empty,
            Kind = SlideShapeKind.AutoShape
        };

        var ph = nvPr?.Element(P + "ph");
        if (ph is not null) shape.Placeholder = ReadPlaceholder(ph);

        var spPr = sp.Element(P + "spPr");
        ReadSpPr(spPr, shape, scheme);

        var prst = spPr?.Element(A + "prstGeom")?.Attribute("prst")?.Value;
        shape.AutoShapeKind = PptxShapeKindMap.FromPreset(prst);

        var txBody = sp.Element(P + "txBody");
        if (txBody is not null) shape.TextBody = ReadTxBody(txBody, scheme);

        return shape;
    }

    // ── p:pic ────────────────────────────────────────────────────────────────────

    private static SlideShape ReadPic(XElement pic, ZipArchive archive, string partPath, PresentationColorScheme scheme)
    {
        var cNvPr = pic.Element(P + "nvPicPr")?.Element(P + "cNvPr");

        var shape = new SlideShape
        {
            Id = ParseUint(cNvPr?.Attribute("id")?.Value),
            Name = cNvPr?.Attribute("name")?.Value ?? string.Empty,
            Kind = SlideShapeKind.Picture
        };

        var spPr = pic.Element(P + "spPr");
        ReadSpPr(spPr, shape, scheme);

        // blipFill → image
        var blip = pic.Element(P + "blipFill")?.Element(A + "blip");
        var embedId = blip?.Attribute(R + "embed")?.Value;
        if (!string.IsNullOrWhiteSpace(embedId))
        {
            var partRels = LoadRels(archive, GetRelsPath(partPath));
            var imageTarget = partRels.FirstOrDefault(r => r.id == embedId && r.type == ImageRelType).target;
            if (!string.IsNullOrWhiteSpace(imageTarget))
            {
                var imagePath = ResolvePath(GetDirectory(partPath), imageTarget);
                var entry = archive.GetEntry(imagePath);
                if (entry is not null)
                {
                    using var imgStream = entry.Open();
                    using var ms = new MemoryStream();
                    imgStream.CopyTo(ms);
                    shape.Picture = new ImagePart
                    {
                        Bytes = ms.ToArray(),
                        ContentType = GuessContentType(imagePath)
                    };
                }
            }
        }

        return shape;
    }

    // ── p:cxnSp ──────────────────────────────────────────────────────────────────

    private static SlideShape ReadCxnSp(XElement cxnSp, PresentationColorScheme scheme)
    {
        var cNvPr = cxnSp.Element(P + "nvCxnSpPr")?.Element(P + "cNvPr");

        var shape = new SlideShape
        {
            Id = ParseUint(cNvPr?.Attribute("id")?.Value),
            Name = cNvPr?.Attribute("name")?.Value ?? string.Empty,
            Kind = SlideShapeKind.Connector
        };

        var spPr = cxnSp.Element(P + "spPr");
        ReadSpPr(spPr, shape, scheme);

        var prst = spPr?.Element(A + "prstGeom")?.Attribute("prst")?.Value;
        shape.AutoShapeKind = PptxShapeKindMap.FromPreset(prst);

        return shape;
    }

    // ── p:grpSp ──────────────────────────────────────────────────────────────────

    private static SlideShape ReadGrpSp(XElement grpSp, ZipArchive archive, string partPath, PresentationColorScheme scheme)
    {
        var cNvPr = grpSp.Element(P + "nvGrpSpPr")?.Element(P + "cNvPr");

        var shape = new SlideShape
        {
            Id = ParseUint(cNvPr?.Attribute("id")?.Value),
            Name = cNvPr?.Attribute("name")?.Value ?? string.Empty,
            Kind = SlideShapeKind.Group
        };

        ReadSpPr(grpSp.Element(P + "grpSpPr"), shape, scheme);

        foreach (var child in ReadShapesFromTree(grpSp, archive, partPath, scheme))
            shape.Children.Add(child);

        return shape;
    }

    // ── spPr ─────────────────────────────────────────────────────────────────────

    private static void ReadSpPr(XElement? spPr, SlideShape shape, PresentationColorScheme scheme)
    {
        if (spPr is null) return;

        var xfrm = spPr.Element(A + "xfrm");
        if (xfrm is not null)
        {
            shape.OffsetXEmu = ParseLong(xfrm.Element(A + "off")?.Attribute("x")?.Value);
            shape.OffsetYEmu = ParseLong(xfrm.Element(A + "off")?.Attribute("y")?.Value);
            shape.ExtentCxEmu = ParseLong(xfrm.Element(A + "ext")?.Attribute("cx")?.Value);
            shape.ExtentCyEmu = ParseLong(xfrm.Element(A + "ext")?.Attribute("cy")?.Value);

            if (long.TryParse(xfrm.Attribute("rot")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var rotRaw))
                shape.RotationDeg = rotRaw / 60000.0;

            shape.FlipH = xfrm.Attribute("flipH")?.Value is "1" or "true";
            shape.FlipV = xfrm.Attribute("flipV")?.Value is "1" or "true";
        }

        shape.Fill = PptxColorReader.TryReadFill(spPr, scheme);
        shape.Outline = PptxColorReader.TryReadOutline(spPr.Element(A + "ln"), scheme);
    }

    // ── TextBody ─────────────────────────────────────────────────────────────────

    private static TextBody ReadTxBody(XElement txBody, PresentationColorScheme scheme)
    {
        var body = new TextBody();

        var bodyPr = txBody.Element(A + "bodyPr");
        if (bodyPr is not null)
        {
            body.Anchor = bodyPr.Attribute("anchor")?.Value switch
            {
                "ctr" => VerticalAnchor.Middle,
                "b" => VerticalAnchor.Bottom,
                "dist" => VerticalAnchor.Distributed,
                _ => VerticalAnchor.Top
            };

            if (ParseLongNullable(bodyPr.Attribute("lIns")?.Value) is { } li) body.InsetLeftPt = li / 12700.0;
            if (ParseLongNullable(bodyPr.Attribute("rIns")?.Value) is { } ri) body.InsetRightPt = ri / 12700.0;
            if (ParseLongNullable(bodyPr.Attribute("tIns")?.Value) is { } ti) body.InsetTopPt = ti / 12700.0;
            if (ParseLongNullable(bodyPr.Attribute("bIns")?.Value) is { } bi) body.InsetBottomPt = bi / 12700.0;
            body.Wrap = bodyPr.Attribute("wrap")?.Value != "none";
            body.AutoFit = bodyPr.Element(A + "normAutofit") is not null || bodyPr.Element(A + "spAutoFit") is not null;
        }

        foreach (var pEl in txBody.Elements(A + "p"))
            body.Paragraphs.Add(ReadParagraph(pEl, scheme));

        return body;
    }

    private static Paragraph ReadParagraph(XElement pEl, PresentationColorScheme scheme)
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

            if (int.TryParse(pPr.Attribute("lvl")?.Value, out var lvl)) para.Level = lvl;

            if (pPr.Element(A + "buNone") is not null)
                para.BulletKind = BulletKind.None;
            else if (pPr.Element(A + "buChar") is { } buChar)
            {
                para.BulletKind = BulletKind.Char;
                para.BulletChar = buChar.Attribute("char")?.Value ?? "•";
            }
            else if (pPr.Element(A + "buAutoNum") is not null)
                para.BulletKind = BulletKind.Auto;

            var spcBef = pPr.Element(A + "spcBef")?.Element(A + "spcPts")?.Attribute("val")?.Value;
            if (!string.IsNullOrWhiteSpace(spcBef) && int.TryParse(spcBef, out var sb))
                para.SpaceBeforePt = sb / 100.0;

            var spcAft = pPr.Element(A + "spcAft")?.Element(A + "spcPts")?.Attribute("val")?.Value;
            if (!string.IsNullOrWhiteSpace(spcAft) && int.TryParse(spcAft, out var sa))
                para.SpaceAfterPt = sa / 100.0;
        }

        foreach (var child in pEl.Elements())
        {
            if (child.Name == A + "r") para.Runs.Add(ReadRun(child, scheme));
            else if (child.Name == A + "br") para.Runs.Add(new Run { Text = "\n" });
        }

        return para;
    }

    private static Run ReadRun(XElement rEl, PresentationColorScheme scheme)
    {
        var run = new Run { Text = rEl.Element(A + "t")?.Value ?? string.Empty };
        var rPr = rEl.Element(A + "rPr");
        if (rPr is not null)
        {
            run.Bold = rPr.Attribute("b")?.Value is "1" or "true";
            run.Italic = rPr.Attribute("i")?.Value is "1" or "true";
            run.Underline = rPr.Attribute("u")?.Value is not null and not "none";
            run.Strikethrough = rPr.Attribute("strike")?.Value is "sngStrike" or "dblStrike";
            if (int.TryParse(rPr.Attribute("sz")?.Value, out var sz) && sz > 0)
                run.FontSizePt = sz / 100.0;
            run.FontFamily = rPr.Element(A + "latin")?.Attribute("typeface")?.Value;
            var solidFill = rPr.Element(A + "solidFill");
            if (solidFill is not null)
                run.Color = PptxColorReader.TryReadColor(solidFill, scheme);
        }
        return run;
    }

    // ── Background ───────────────────────────────────────────────────────────────

    private static ShapeFill? ReadBackground(XElement bg, PresentationColorScheme scheme)
    {
        var bgPr = bg.Element(P + "bgPr");
        return bgPr is not null ? PptxColorReader.TryReadFill(bgPr, scheme) : null;
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

    // ── OPC / Rels helpers ────────────────────────────────────────────────────────

    /// <summary>Loads and parses a .rels file from the archive. Returns empty list on missing/error.</summary>
    private static List<(string id, string type, string target)> LoadRels(ZipArchive archive, string relsPath)
    {
        var entry = archive.GetEntry(relsPath);
        if (entry is null) return new();

        try
        {
            using var stream = entry.Open();
            var doc = XDocument.Load(stream);
            return doc.Root?
                .Elements(Pkgr + "Relationship")
                .Select(r => (
                    r.Attribute("Id")?.Value ?? string.Empty,
                    r.Attribute("Type")?.Value ?? string.Empty,
                    r.Attribute("Target")?.Value ?? string.Empty))
                .Where(t => !string.IsNullOrEmpty(t.Item1))
                .ToList() ?? new();
        }
        catch { return new(); }
    }

    private static string? GetRelTarget(List<(string id, string type, string target)> rels, string relType) =>
        rels.FirstOrDefault(r => r.type == relType).target is { Length: > 0 } t ? t : null;

    private static XDocument? LoadXml(ZipArchive archive, string path)
    {
        var entry = archive.GetEntry(path);
        if (entry is null) return null;
        try
        {
            using var stream = entry.Open();
            return XDocument.Load(stream);
        }
        catch { return null; }
    }

    // ── Path helpers ──────────────────────────────────────────────────────────────

    private static string NormalizePath(string path) =>
        path.TrimStart('/');

    private static string GetDirectory(string path)
    {
        var lastSlash = path.LastIndexOf('/');
        return lastSlash < 0 ? string.Empty : path[..lastSlash];
    }

    private static string GetRelsPath(string partPath)
    {
        var dir = GetDirectory(partPath);
        var file = partPath[(partPath.LastIndexOf('/') + 1)..];
        return string.IsNullOrEmpty(dir)
            ? $"_rels/{file}.rels"
            : $"{dir}/_rels/{file}.rels";
    }

    private static string ResolvePath(string baseDir, string target)
    {
        if (target.StartsWith('/')) return NormalizePath(target);

        // Resolve relative path
        var parts = (string.IsNullOrEmpty(baseDir) ? target : $"{baseDir}/{target}").Split('/');
        var resolved = new List<string>();
        foreach (var part in parts)
        {
            if (part == "..") { if (resolved.Count > 0) resolved.RemoveAt(resolved.Count - 1); }
            else if (part != ".") resolved.Add(part);
        }
        return string.Join("/", resolved);
    }

    // ── Content-type guessing ────────────────────────────────────────────────────

    private static string GuessContentType(string path)
    {
        var ext = path.Split('.').Last().ToLowerInvariant();
        return ext switch
        {
            "jpg" or "jpeg" => "image/jpeg",
            "gif" => "image/gif",
            "bmp" => "image/bmp",
            "tif" or "tiff" => "image/tiff",
            "svg" => "image/svg+xml",
            "wmf" => "image/x-wmf",
            "emf" => "image/x-emf",
            _ => "image/png"
        };
    }

    // ── Value parsers ─────────────────────────────────────────────────────────────

    private static uint ParseUint(string? value) =>
        uint.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;

    private static long ParseLong(string? value) =>
        long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;

    private static long? ParseLongNullable(string? value) =>
        long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : null;

    private static bool TryParseHex6(string hex, out SrgbColor color)
    {
        color = default;
        var s = hex.Trim().TrimStart('#');
        if (s.Length != 6) return false;
        if (!byte.TryParse(s[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var r)) return false;
        if (!byte.TryParse(s[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var g)) return false;
        if (!byte.TryParse(s[4..6], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b)) return false;
        color = new SrgbColor(r, g, b);
        return true;
    }
}
