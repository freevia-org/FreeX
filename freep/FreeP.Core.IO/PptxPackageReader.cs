using System.Globalization;
using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;
using Free.Shared.Drawing;
using Free.Shared.Opc;
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
    private const string CorePropsRelType     = "http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties";
    private const string TableStylesRelType   = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/tableStyles";
    private const string ChartRelType         = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart";
    private const string NotesSlideRelType    = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/notesSlide";
    private const string HyperlinkRelType     = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/hyperlink";
    private const string SlideHlinkRelType    = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/slide";
    private const string CommentsRelType      = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/comments";
    private const string CommentAuthorsRelType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/commentAuthors";
    private const string VideoRelType         = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/video";
    private const string AudioRelType         = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/audio";
    // Microsoft proprietary media rel type used by newer PowerPoint
    private const string MediaRelType         = "http://schemas.microsoft.com/office/2007/relationships/media";

    // p14 section extension + mc:AlternateContent namespace
    private static readonly XNamespace P14 = "http://schemas.microsoft.com/office/powerpoint/2010/main";
    private static readonly XNamespace MC  = "http://schemas.openxmlformats.org/markup-compatibility/2006";
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

        // Table styles (keyed by style GUID)
        var tableStyles = new Dictionary<string, TableStyleData>(StringComparer.OrdinalIgnoreCase);
        var tableStylesTarget = GetRelTarget(presRels, TableStylesRelType);
        if (tableStylesTarget is not null)
        {
            var tableStylesPath = ResolvePath(presDir, tableStylesTarget);
            ReadTableStyles(archive, tableStylesPath, presentation.Theme.ColorScheme, tableStyles);
        }

        // Slide masters → layouts
        var masterRelEntries = presRels.Where(r => r.type == SlideMasterRelType).ToList();
        bool firstMaster = true;
        foreach (var (masterId, _, masterTarget) in masterRelEntries)
        {
            var masterPath = ResolvePath(presDir, masterTarget);
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

            var masterDir = GetDirectory(masterPath);
            var masterRels = LoadRels(archive, GetRelsPath(masterPath));

            // Use this master's own theme (or fall back to presentation.Theme) for layout parsing.
            var masterColorScheme = (master.Theme ?? presentation.Theme).ColorScheme;
            foreach (var (layoutId, _, layoutTarget) in masterRels.Where(r => r.type == SlideLayoutRelType))
            {
                var layoutPath = ResolvePath(masterDir, layoutTarget);
                var layout = ReadSlideLayout(archive, layoutPath, layoutId, master.Id, masterColorScheme);
                presentation.Layouts.Add(layout);
            }
        }

        // Slides in order from sldIdLst — two-phase so internal hyperlinks can resolve to Slide.Id.
        // Phase 1: collect ordered (rId, slidePath) pairs and create placeholder Slide objects so we
        //           have a complete allSlides list before parsing shapes.
        var slideRelEntries = presRels.ToDictionary(r => r.id, StringComparer.OrdinalIgnoreCase);
        var sldIdList = presRoot.Element(P + "sldIdLst")?.Elements(P + "sldId").ToList() ?? new();

        // Build ordered list of (rId, slidePath) for all valid slide entries.
        var slideInfos = new List<(string rId, string slidePath)>();
        foreach (var sldIdEl in sldIdList)
        {
            var rId = sldIdEl.Attribute(R + "id")?.Value;
            if (string.IsNullOrWhiteSpace(rId) || !slideRelEntries.TryGetValue(rId, out var slideRel))
                continue;
            if (slideRel.type != SlideRelType) continue;
            slideInfos.Add((rId, ResolvePath(presDir, slideRel.target)));
        }

        // Create placeholder slides (with Id set) for the allSlides reference list.
        var allSlides = new List<Slide>(slideInfos.Count);
        foreach (var (rId, _) in slideInfos)
            allSlides.Add(new Slide { Id = rId });

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

        // Comment authors live in a single ppt/commentAuthors.xml part referenced from presRels.
        var cmAuthorsTarget = GetRelTarget(presRels, CommentAuthorsRelType);
        var authorMap = new Dictionary<int, (string name, string initials)>();
        if (cmAuthorsTarget is not null)
        {
            var cmAuthorsPath = ResolvePath(presDir, cmAuthorsTarget);
            authorMap = ReadCommentAuthors(archive, cmAuthorsPath);
        }

        // Re-process each slide's comments now that we have the author map.
        // (Comments were NOT parsed in ReadSlide yet — we do it here so authorMap is available.)
        for (int si = 0; si < presentation.Slides.Count; si++)
        {
            var slide = presentation.Slides[si];
            var rId = sldIdList.Count > si ? sldIdList[si].Attribute(R + "id")?.Value : null;
            if (rId is null) continue;
            if (!slideRelEntries.TryGetValue(rId, out var sr)) continue;
            var slidePath2 = ResolvePath(presDir, sr.target);
            var slideRels2 = LoadRels(archive, GetRelsPath(slidePath2));
            var cmTarget = GetRelTarget(slideRels2, CommentsRelType);
            if (cmTarget is null) continue;
            var cmPath = ResolvePath(GetDirectory(slidePath2), cmTarget);
            ReadSlideComments(archive, cmPath, authorMap, slide.Comments);
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
    private static Dictionary<int, (string name, string initials)> ReadCommentAuthors(
        ZipArchive archive, string path)
    {
        var result = new Dictionary<int, (string name, string initials)>();
        var xml = LoadXml(archive, path);
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
        var xml = LoadXml(archive, path);
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
        var buClrL = lvlEl.Element(A + "buClr");
        if (buClrL is not null)
            level.BulletColor = PptxColorReader.TryReadColor(buClrL, scheme);
        var buSzPctL = lvlEl.Element(A + "buSzPct");
        if (buSzPctL is not null && int.TryParse(buSzPctL.Attribute("val")?.Value, out var szPctL))
            level.BulletSizePct = szPctL;
        var buFontL = lvlEl.Element(A + "buFont");
        if (buFontL is not null)
            level.BulletFontFamily = buFontL.Attribute("typeface")?.Value;

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

        var xml = LoadXml(archive, layoutPath);
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

        var xml = LoadXml(archive, slidePath);
        if (xml?.Root is null) return slide;

        // Layout via rels
        var slideRels = LoadRels(archive, GetRelsPath(slidePath));
        var layoutTarget = GetRelTarget(slideRels, SlideLayoutRelType);
        if (layoutTarget is not null)
        {
            var layoutPath = ResolvePath(GetDirectory(slidePath), layoutTarget);
            // Match with our loaded layouts by exact normalized path (PartPath).
            slide.LayoutId = MatchLayoutIdByPath(layoutPath, layouts);
        }

        var bg = xml.Root.Element(P + "cSld")?.Element(P + "bg");
        if (bg is not null) slide.Background = ReadBackground(bg, scheme);

        var slideDir = GetDirectory(slidePath);
        var spTree = xml.Root.Element(P + "cSld")?.Element(P + "spTree");
        if (spTree is not null)
        {
            foreach (var shape in ReadShapesFromTree(spTree, archive, slidePath, scheme, tableStyles, slideRels, allSlides, slideDir, slidePartPathToId))
                slide.Shapes.Add(shape);
        }

        // Transition — may be a plain p:transition (legacy) or wrapped in mc:AlternateContent (modern).
        // AC1: resolve mc:AlternateContent → use mc:Choice p:transition (has p14:dur); fall back to
        // mc:Fallback p:transition or a bare p:transition for files written without the extension.
        slide.Transition = ResolveTransitionEl(xml.Root);

        // Animations (main sequence only)
        var timingEl = xml.Root.Element(P + "timing");
        if (timingEl is not null)
            ReadAnimations(timingEl, slide);

        // Speaker notes — follow notesSlide relationship if present
        var notesTarget = GetRelTarget(slideRels, NotesSlideRelType);
        if (notesTarget is not null)
        {
            var notesPath = ResolvePath(GetDirectory(slidePath), notesTarget);
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
        var xml = LoadXml(archive, notesPath);
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
        List<(string id, string type, string target)>? slideRels = null,
        List<Slide>? allSlides = null,
        string? slideDir = null,
        IReadOnlyDictionary<string, string>? slidePartPathToId = null)
    {
        foreach (var child in spTree.Elements())
        {
            SlideShape? shape = child.Name.LocalName switch
            {
                "sp"           => ReadSp(child, scheme, slideRels, allSlides, slideDir, slidePartPathToId, archive, partPath),
                "pic"          => ReadPic(child, archive, partPath, scheme),
                "cxnSp"        => ReadCxnSp(child, scheme, slideRels, allSlides, slideDir, slidePartPathToId, archive, partPath),
                "grpSp"        => ReadGrpSp(child, archive, partPath, scheme, tableStyles, slideRels, allSlides, slideDir, slidePartPathToId),
                "graphicFrame" => ReadGraphicFrame(child, archive, partPath, scheme, tableStyles),
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
        XElement hlinkEl,
        List<(string id, string type, string target)>? slideRels,
        List<Slide>? allSlides,
        string? slideDir = null,
        IReadOnlyDictionary<string, string>? slidePartPathToId = null)
    {
        if (slideRels is null) return null;

        var rId     = hlinkEl.Attribute(R + "id")?.Value;
        var action  = hlinkEl.Attribute("action")?.Value;
        var tooltip = hlinkEl.Attribute("tooltip")?.Value;

        // action="ppaction://hlinksldjump" with empty rId is a slide-jump with a rels entry.
        // Also handle action-only with no rId.
        bool isSlideJumpAction = action?.Contains("hlinksldjump", StringComparison.OrdinalIgnoreCase) == true;

        if (!string.IsNullOrEmpty(rId))
        {
            var rel = slideRels.FirstOrDefault(r => r.id == rId);
            if (rel == default) return null;

            if (rel.type == HyperlinkRelType)
            {
                // External hyperlink
                return new Hyperlink { Url = rel.target, Tooltip = tooltip };
            }

            if (rel.type == SlideRelType || rel.type == SlideHlinkRelType || isSlideJumpAction)
            {
                // Internal slide jump: target is a relative path like "../slides/slide3.xml".
                // Resolve by part path → Slide.Id rather than filename digit, so decks where the
                // presentation order does not match slideN.xml filenames navigate correctly.
                if (slidePartPathToId is not null && slideDir is not null)
                {
                    // Resolve the relative target against the slide's directory to get an absolute
                    // OPC part path, then look it up in the part-path→rId map.
                    var absTarget = ResolvePath(slideDir, rel.target);
                    if (slidePartPathToId.TryGetValue(absTarget, out var slideId))
                        return new Hyperlink { TargetSlideId = slideId, Tooltip = tooltip };
                    // absTarget didn't match — fall through to filename-digit fallback below.
                }

                if (allSlides is not null)
                {
                    // Fallback (no map available or path not found): derive slide index from the
                    // numeric suffix of the filename. This is order-sensitive and wrong for reordered
                    // decks, but preserves the pre-fix behaviour when the map is absent.
                    var targetSeg = rel.target.Split('/').Last(); // e.g. "slide2.xml"
                    var numStr = System.Text.RegularExpressions.Regex
                        .Match(targetSeg, @"\d+").Value;
                    if (int.TryParse(numStr, out var num) && num >= 1 && num <= allSlides.Count)
                    {
                        var targetSlide = allSlides[num - 1];
                        return new Hyperlink { TargetSlideId = targetSlide.Id, Tooltip = tooltip };
                    }
                }
                // Last resort: store the target path as the id (round-trip acceptable).
                return new Hyperlink { TargetSlideId = rel.target, Tooltip = tooltip };
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

    // Relationship types for SmartArt diagram parts
    private const string DiagramDataRelType    = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/diagramData";
    private const string DiagramLayoutRelType  = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/diagramLayout";
    private const string DiagramQuickStyleRelType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/diagramQuickStyle";
    private const string DiagramColorsRelType  = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/diagramColors";
    private const string DiagramDrawingRelType = "http://schemas.microsoft.com/office/2007/relationships/diagramDrawing";

    private static SlideShape? ReadGraphicFrame(
        XElement gfEl, ZipArchive archive, string partPath,
        PresentationColorScheme scheme,
        Dictionary<string, TableStyleData>? tableStyles)
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
                Kind = SlideShapeKind.Table,
                OffsetXEmu = offX,
                OffsetYEmu = offY,
                ExtentCxEmu = extCx,
                ExtentCyEmu = extCy,
                Table = tableShape
            };
        }

        // ── Chart ──────────────────────────────────────────────────────────────
        if (string.Equals(uri, DrawingChartUri, StringComparison.OrdinalIgnoreCase))
        {
            var chartRelId = graphicData.Element(CChart + "chart")?.Attribute(R + "id")?.Value;
            if (string.IsNullOrWhiteSpace(chartRelId)) return null;

            // Resolve the chart part path via the slide's rels
            var partRels = LoadRels(archive, GetRelsPath(partPath));
            var chartTarget = partRels
                .FirstOrDefault(r => r.id == chartRelId && r.type == ChartRelType).target;
            if (string.IsNullOrWhiteSpace(chartTarget)) return null;

            var chartPath = ResolvePath(GetDirectory(partPath), chartTarget);
            var chartShape = PptxChartReader.ReadChartPart(archive, chartPath, scheme);
            if (chartShape is null) return null;

            return new SlideShape
            {
                Id = ParseUint(cNvPr?.Attribute("id")?.Value),
                Name = cNvPr?.Attribute("name")?.Value ?? string.Empty,
                Kind = SlideShapeKind.Chart,
                OffsetXEmu = offX,
                OffsetYEmu = offY,
                ExtentCxEmu = extCx,
                ExtentCyEmu = extCy,
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
                Kind = SlideShapeKind.SmartArt,
                OffsetXEmu = offX,
                OffsetYEmu = offY,
                ExtentCxEmu = extCx,
                ExtentCyEmu = extCy,
                SmartArt = smartArt
            };
        }

        // Unknown graphicFrame type — skip for now.
        return null;
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
        var slideRels = LoadRels(archive, GetRelsPath(partPath));
        var slideDir  = GetDirectory(partPath);

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
            var target = slideRels.FirstOrDefault(r => r.id == relId).target;
            if (string.IsNullOrWhiteSpace(target)) continue;

            var absPath = ResolvePath(slideDir, target);
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
            var partRelsPath = GetRelsPath(absPath);
            var partRelsBytes = ReadEntryBytes(archive, partRelsPath);
            if (partRelsBytes is not null)
                smart.PartRels[absPath] = partRelsBytes;

            if (key == "dm") dataPartPath = absPath;
        }

        // Resolve the dsp:drawing part path from the data part's rels.
        if (dataPartPath is not null)
        {
            var dataPartRels = LoadRels(archive, GetRelsPath(dataPartPath));
            var drawingTarget = dataPartRels
                .FirstOrDefault(r => r.type == DiagramDrawingRelType).target;

            if (!string.IsNullOrWhiteSpace(drawingTarget))
            {
                var drawingPath = ResolvePath(GetDirectory(dataPartPath), drawingTarget);
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
                    var drawRelsBytes = ReadEntryBytes(archive, GetRelsPath(drawingPath));
                    if (drawRelsBytes is not null)
                        smart.PartRels[drawingPath] = drawRelsBytes;

                    // Parse dsp:drawing shapes into FallbackShapes
                    try
                    {
                        ReadDspDrawing(drawingBytes, smart, scheme);
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
        }
        catch
        {
            // Graceful degradation: Data stays null, compositor will use cached fallback
        }

        return smart;
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
                layoutDoc = XDocument.Load(ms);

            layoutUniqueId = layoutDoc.Root?.Attribute("uniqueId")?.Value ?? string.Empty;
            family = ClassifySmartArtFamily(layoutUniqueId);
        }

        // ── Parse data1.xml → node tree ────────────────────────────────────────
        XDocument dataDoc;
        using (var ms2 = new MemoryStream(dataEntry.Bytes))
            dataDoc = XDocument.Load(ms2);

        var data = new SmartArtData
        {
            Family        = family,
            LayoutUniqueId = layoutUniqueId
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

            // Extract text from dgm:t/a:p/a:r/a:t  (multiple paragraphs → join with newline)
            var tEl = pt.Element(dgmNsData + "t");
            var sb  = new System.Text.StringBuilder();
            if (tEl is not null)
            {
                foreach (var p in tEl.Elements())
                {
                    // Any namespace p element — extract inner a:r/a:t
                    foreach (var r in p.Descendants())
                    {
                        if (r.Name.LocalName == "t")
                        {
                            var txt = r.Value;
                            if (!string.IsNullOrEmpty(txt))
                            {
                                if (sb.Length > 0) sb.Append(' ');
                                sb.Append(txt);
                            }
                        }
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(modelId))
                points[modelId] = (type, sb.ToString(), type == "asst");
        }

        // Build parent→children map from cxnLst parOf connections
        var childrenOf = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var hasParent  = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (cxnLst is not null)
        {
            foreach (var cxn in cxnLst.Elements(dgmNsData + "cxn"))
            {
                var cxnType = cxn.Attribute("type")?.Value ?? string.Empty;
                if (!string.Equals(cxnType, "parOf", StringComparison.OrdinalIgnoreCase)) continue;

                var srcId  = cxn.Attribute("srcId")?.Value  ?? string.Empty;
                var destId = cxn.Attribute("destId")?.Value ?? string.Empty;
                if (string.IsNullOrWhiteSpace(srcId) || string.IsNullOrWhiteSpace(destId)) continue;

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

        if (uid.Contains("cycle") || uid.Contains("gear") || uid.Contains("radial"))
            return SmartArtFamily.Cycle;

        if (uid.Contains("process") || uid.Contains("arrow") || uid.Contains("chevron")
            || uid.Contains("funnel") || uid.Contains("horiz"))
            return SmartArtFamily.Process;

        if (uid.Contains("list") || uid.Contains("lproc") || uid.Contains("bullet")
            || uid.Contains("pyramid") || uid.Contains("stack"))
            return SmartArtFamily.List;

        return SmartArtFamily.Unknown;
    }

    /// <summary>
    /// Parses a dsp:drawing XML (SmartArt cached render) into FallbackShapes on the SmartArtShape.
    /// dsp:sp elements are structurally like p:sp (spPr + txBody); dsp:grpSp like p:grpSp.
    /// </summary>
    private static void ReadDspDrawing(byte[] bytes, SmartArtShape smart, PresentationColorScheme scheme)
    {
        XDocument doc;
        using (var ms = new MemoryStream(bytes))
            doc = XDocument.Load(ms);

        var root = doc.Root;
        if (root is null) return;

        // dsp:drawing / dsp:spTree
        var spTree = root.Element(Dsp + "spTree");
        if (spTree is null) return;

        foreach (var el in spTree.Elements())
        {
            var shape = ReadDspElement(el, scheme);
            if (shape is not null)
                smart.FallbackShapes.Add(shape);
        }
    }

    /// <summary>
    /// Reads a dsp:sp or dsp:grpSp element into a SlideShape using the existing spPr/txBody helpers.
    /// </summary>
    private static SlideShape? ReadDspElement(XElement el, PresentationColorScheme scheme)
    {
        switch (el.Name.LocalName)
        {
            case "sp":
                return ReadDspSp(el, scheme);
            case "grpSp":
                return ReadDspGrpSp(el, scheme);
            default:
                return null;
        }
    }

    private static SlideShape ReadDspSp(XElement sp, PresentationColorScheme scheme)
    {
        // dsp:sp has dsp:nvSpPr/dsp:cNvPr (id, name), dsp:spPr (a: children), dsp:txBody (a: children)
        var cNvPrEl = sp.Elements().FirstOrDefault(e => e.Name.LocalName == "nvSpPr")
                        ?.Elements().FirstOrDefault(e => e.Name.LocalName == "cNvPr");

        var shape = new SlideShape
        {
            Id   = ParseUint(cNvPrEl?.Attribute("id")?.Value),
            Name = cNvPrEl?.Attribute("name")?.Value ?? string.Empty,
            Kind = SlideShapeKind.AutoShape
        };

        // spPr — same structure as p:spPr with a: children
        var spPrEl = sp.Elements().FirstOrDefault(e => e.Name.LocalName == "spPr");
        if (spPrEl is not null)
        {
            // Build a synthetic a:spPr element so we can reuse ReadSpPr (it uses the A namespace)
            var aSpPr = new XElement(A + "spPr", spPrEl.Attributes(), spPrEl.Elements());
            ReadSpPr(aSpPr, shape, scheme);

            var prst = aSpPr.Element(A + "prstGeom")?.Attribute("prst")?.Value;
            shape.AutoShapeKind = PptxShapeKindMap.FromPreset(prst);
        }

        // txBody
        var txBodyEl = sp.Elements().FirstOrDefault(e => e.Name.LocalName == "txBody");
        if (txBodyEl is not null)
        {
            // dsp:txBody uses a: children — same as p:txBody
            var aTxBody = new XElement(A + "txBody", txBodyEl.Attributes(), txBodyEl.Elements());
            shape.TextBody = ReadTxBody(aTxBody, scheme);
        }

        return shape;
    }

    private static SlideShape ReadDspGrpSp(XElement grpSp, PresentationColorScheme scheme)
    {
        var cNvPrEl = grpSp.Elements().FirstOrDefault(e => e.Name.LocalName == "nvGrpSpPr")
                           ?.Elements().FirstOrDefault(e => e.Name.LocalName == "cNvPr");

        var shape = new SlideShape
        {
            Id   = ParseUint(cNvPrEl?.Attribute("id")?.Value),
            Name = cNvPrEl?.Attribute("name")?.Value ?? string.Empty,
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
            var childShape = ReadDspElement(child, scheme);
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
            if (ParseLongNullable(tcPr.Attribute("marL")?.Value) is { } ml) cell.InsetLeftPt   = ml / 12700.0;
            if (ParseLongNullable(tcPr.Attribute("marR")?.Value) is { } mr) cell.InsetRightPt  = mr / 12700.0;
            if (ParseLongNullable(tcPr.Attribute("marT")?.Value) is { } mt) cell.InsetTopPt    = mt / 12700.0;
            if (ParseLongNullable(tcPr.Attribute("marB")?.Value) is { } mb) cell.InsetBottomPt = mb / 12700.0;

            // Vertical anchor
            cell.Anchor = tcPr.Attribute("anchor")?.Value switch
            {
                "ctr"  => TableCellAnchor.Middle,
                "b"    => TableCellAnchor.Bottom,
                "t"    => TableCellAnchor.Top,
                _      => (TableCellAnchor?)null
            };

            // Explicit fill
            cell.Fill = PptxColorReader.TryReadFill(tcPr, scheme);

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
        List<(string id, string type, string target)>? slideRels = null,
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

        var txBody = sp.Element(P + "txBody");
        if (txBody is not null) shape.TextBody = ReadTxBody(txBody, scheme, slideRels, allSlides, slideDir, slidePartPathToId);

        return shape;
    }

    // ── p:pic ────────────────────────────────────────────────────────────────────

    private static SlideShape ReadPic(XElement pic, ZipArchive archive, string partPath, PresentationColorScheme scheme)
    {
        var cNvPr = pic.Element(P + "nvPicPr")?.Element(P + "cNvPr");
        var nvPr  = pic.Element(P + "nvPicPr")?.Element(P + "nvPr");

        var shape = new SlideShape
        {
            Id = ParseUint(cNvPr?.Attribute("id")?.Value),
            Name = cNvPr?.Attribute("name")?.Value ?? string.Empty,
            Kind = SlideShapeKind.Picture
        };

        var spPr = pic.Element(P + "spPr");
        ReadSpPr(spPr, shape, scheme);
        // P3: also carry the picture's outline (a:ln inside p:spPr) — already handled by ReadSpPr.

        // blipFill → poster / image
        var blipFillEl = pic.Element(P + "blipFill");
        var blip = blipFillEl?.Element(A + "blip");
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
                    var partRels = LoadRels(archive, GetRelsPath(partPath));
                    var mediaRel = partRels.FirstOrDefault(r => r.id == mediaRelId);
                    if (!string.IsNullOrEmpty(mediaRel.target))
                    {
                        if (mediaRel.type == HyperlinkRelType ||
                            mediaRel.target.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                        {
                            // External / link-only
                            mediaInfo.LinkUrl = mediaRel.target;
                        }
                        else
                        {
                            // Embedded
                            var mediaPath  = ResolvePath(GetDirectory(partPath), mediaRel.target);
                            var mediaBytes = ReadEntryBytes(archive, mediaPath);
                            if (mediaBytes is not null)
                            {
                                mediaInfo.Bytes       = mediaBytes;
                                mediaInfo.ContentType = GuessMediaContentType(mediaPath);
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
                        var partRels = LoadRels(archive, GetRelsPath(partPath));
                        foreach (var ext in extLst.Elements(P + "ext"))
                        {
                            var mediaRef = ext.Descendants()
                                .FirstOrDefault(e => e.Attribute(R + "embed") is not null
                                                 || e.Attribute(R + "link") is not null);
                            if (mediaRef is null) continue;
                            var mRelId = mediaRef.Attribute(R + "embed")?.Value
                                      ?? mediaRef.Attribute(R + "link")?.Value;
                            if (string.IsNullOrEmpty(mRelId)) continue;
                            var mRel = partRels.FirstOrDefault(r => r.id == mRelId);
                            if (string.IsNullOrEmpty(mRel.target)) continue;
                            var mPath  = ResolvePath(GetDirectory(partPath), mRel.target);
                            var mBytes = ReadEntryBytes(archive, mPath);
                            if (mBytes is not null)
                            {
                                mediaInfo.Bytes       = mBytes;
                                mediaInfo.ContentType = GuessMediaContentType(mPath);
                            }
                            break;
                        }
                    }
                }

                shape.Media = mediaInfo;
                shape.Kind  = SlideShapeKind.Media;
            }
        }

        return shape;
    }

    private static string GuessMediaContentType(string path)
    {
        var ext = path.Split('.').Last().ToLowerInvariant();
        return ext switch
        {
            "mp4"  => "video/mp4",
            "m4v"  => "video/mp4",
            "mov"  => "video/quicktime",
            "avi"  => "video/x-msvideo",
            "wmv"  => "video/x-ms-wmv",
            "mp3"  => "audio/mpeg",
            "m4a"  => "audio/mp4",
            "wav"  => "audio/wav",
            "wma"  => "audio/x-ms-wma",
            _      => "video/mp4"
        };
    }

    // ── p:cxnSp ──────────────────────────────────────────────────────────────────

    private static SlideShape ReadCxnSp(XElement cxnSp, PresentationColorScheme scheme,
        List<(string id, string type, string target)>? slideRels = null,
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
            Kind = SlideShapeKind.Connector
        };

        // Shape-level hyperlink on connector.
        var shapeHlink = cNvPr?.Element(A + "hlinkClick");
        if (shapeHlink is not null)
            shape.Hyperlink = ResolveHlinkClick(shapeHlink, slideRels, allSlides, slideDir, slidePartPathToId);

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
        List<(string id, string type, string target)>? slideRels = null,
        List<Slide>? allSlides = null,
        string? slideDir = null,
        IReadOnlyDictionary<string, string>? slidePartPathToId = null)
    {
        var cNvPr = grpSp.Element(P + "nvGrpSpPr")?.Element(P + "cNvPr");

        var shape = new SlideShape
        {
            Id = ParseUint(cNvPr?.Attribute("id")?.Value),
            Name = cNvPr?.Attribute("name")?.Value ?? string.Empty,
            Kind = SlideShapeKind.Group
        };

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
            shape.ExtentCxEmu = ParseLong(xfrm.Element(A + "ext")?.Attribute("cx")?.Value);
            shape.ExtentCyEmu = ParseLong(xfrm.Element(A + "ext")?.Attribute("cy")?.Value);

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

    // ── Blip resolver ─────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a delegate that resolves a blip embed rId to (bytes, contentType) using
    /// the slide's image rels and the archive. Used for a:blipFill shape fills.
    /// </summary>
    private static Func<string, (byte[] bytes, string contentType)?> BuildBlipResolver(
        ZipArchive archive,
        List<(string id, string type, string target)> slideRels,
        string partPath)
    {
        return (embedId) =>
        {
            var imageTarget = slideRels
                .FirstOrDefault(r => r.id == embedId && r.type == ImageRelType).target;
            if (string.IsNullOrWhiteSpace(imageTarget)) return null;

            var imagePath = ResolvePath(GetDirectory(partPath), imageTarget);
            var entry = archive.GetEntry(imagePath);
            if (entry is null) return null;

            using var imgStream = entry.Open();
            using var ms = new MemoryStream();
            imgStream.CopyTo(ms);
            return (ms.ToArray(), GuessContentType(imagePath));
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

            if (cgp.Segments.Count > 0)
                shape.CustomGeometry.Add(cgp);
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
        List<(string id, string type, string target)>? slideRels = null,
        List<Slide>? allSlides = null,
        string? slideDir = null,
        IReadOnlyDictionary<string, string>? slidePartPathToId = null)
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

            if (ParseLongNullable(bodyPr.Attribute("lIns")?.Value) is { } li) body.InsetLeftPt = li / 12700.0;
            if (ParseLongNullable(bodyPr.Attribute("rIns")?.Value) is { } ri) body.InsetRightPt = ri / 12700.0;
            if (ParseLongNullable(bodyPr.Attribute("tIns")?.Value) is { } ti) body.InsetTopPt = ti / 12700.0;
            if (ParseLongNullable(bodyPr.Attribute("bIns")?.Value) is { } bi) body.InsetBottomPt = bi / 12700.0;
            body.Wrap = bodyPr.Attribute("wrap")?.Value != "none";
            var normAf = bodyPr.Element(A + "normAutofit");
            var spAf   = bodyPr.Element(A + "spAutoFit");
            body.AutoFit = normAf is not null || spAf is not null;
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
            body.Paragraphs.Add(ReadParagraph(pEl, scheme, slideRels, allSlides, slideDir, slidePartPathToId));

        return body;
    }

    private static Paragraph ReadParagraph(XElement pEl, PresentationColorScheme scheme,
        List<(string id, string type, string target)>? slideRels = null,
        List<Slide>? allSlides = null,
        string? slideDir = null,
        IReadOnlyDictionary<string, string>? slidePartPathToId = null)
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
            else if (pPr.Element(A + "buAutoNum") is { } buAutoNum)
            {
                para.BulletKind = BulletKind.Auto;
                para.AutoNumType = ParseAutoNumType(buAutoNum.Attribute("type")?.Value);
                if (int.TryParse(buAutoNum.Attribute("startAt")?.Value, out var startAt) && startAt >= 1)
                    para.AutoNumStartAt = startAt;
            }

            // Wave 19A: marL/indent/buClr/buSzPct/buFont
            if (ParseLongNullable(pPr.Attribute("marL")?.Value) is { } paraMarL) para.MarginLeftEmu = paraMarL;
            if (ParseLongNullable(pPr.Attribute("indent")?.Value) is { } paraInd) para.IndentEmu = paraInd;
            var buClr = pPr.Element(A + "buClr");
            if (buClr is not null)
                para.BulletColor = PptxColorReader.TryReadColor(buClr, scheme);
            var buSzPct = pPr.Element(A + "buSzPct");
            if (buSzPct is not null && int.TryParse(buSzPct.Attribute("val")?.Value, out var szPct))
                para.BulletSizePct = szPct;
            var buFont = pPr.Element(A + "buFont");
            if (buFont is not null)
                para.BulletFontFamily = buFont.Attribute("typeface")?.Value;

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
        {
            if (child.Name == A + "r")
                para.Runs.Add(ReadRun(child, scheme, slideRels, allSlides, slideDir, slidePartPathToId));
            else if (child.Name == A + "br")
                para.Runs.Add(new Run { Text = "\n" });
            else if (child.Name == A + "fld")
                para.Runs.Add(ReadFieldRun(child, scheme));
        }

        return para;
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
        List<(string id, string type, string target)>? slideRels = null,
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
            if (int.TryParse(rPr.Attribute("sz")?.Value, out var sz) && sz > 0)
                run.FontSizePt = sz / 100.0;
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

            // Wave 16A: text shadow (a:effectLst/a:outerShdw inside a:rPr)
            var outerShdw = rPr.Element(A + "effectLst")?.Element(A + "outerShdw");
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
                if (long.TryParse(outerShdw.Attribute("blurRad")?.Value, out var blurEmu)) blurPt = blurEmu / 12700.0;
                if (long.TryParse(outerShdw.Attribute("dist")?.Value,    out var distEmu)) distPt = distEmu / 12700.0;
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
    private static SlideTransition? ResolveTransitionEl(XElement? root)
    {
        if (root is null) return null;

        // Modern format: mc:AlternateContent > mc:Choice > p:transition (with p14:dur)
        var altContent = root.Element(MC + "AlternateContent");
        if (altContent is not null)
        {
            var choice = altContent.Element(MC + "Choice");
            var choiceTrans = choice?.Element(P + "transition");
            if (choiceTrans is not null)
                return ReadTransition(choiceTrans, preferP14Dur: true);

            // Fallback inside mc:AlternateContent
            var fallback = altContent.Element(MC + "Fallback");
            var fallbackTrans = fallback?.Element(P + "transition");
            if (fallbackTrans is not null)
                return ReadTransition(fallbackTrans, preferP14Dur: false);
        }

        // Legacy: bare p:transition (no mc:AlternateContent wrapper)
        var transEl = root.Element(P + "transition");
        if (transEl is not null)
            return ReadTransition(transEl, preferP14Dur: false);

        return null;
    }

    private static SlideTransition ReadTransition(XElement transEl, bool preferP14Dur)
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

        // Find the effect child element: first P-namespace child (skip any mc: / extension elements).
        var effectEl = transEl.Elements().FirstOrDefault(e => e.Name.Namespace == P);
        if (effectEl is not null)
        {
            t.Kind = PptxAnimationMap.ElementNameToTransitionKind(effectEl.Name.LocalName);
            // Direction: try "dir" first, then "orient"
            var dirAttr = effectEl.Attribute("dir")?.Value ?? effectEl.Attribute("orient")?.Value;
            t.Direction = PptxAnimationMap.AttrToTransitionDirection(dirAttr);
        }

        return t;
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
        }
        catch
        {
            // If we fail to parse the timing tree (complex/unknown structure), skip silently.
        }
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

        // Check for motion path: look for p:animMotion anywhere in descendants.
        var animMotion = buildPar.Descendants(P + "animMotion").FirstOrDefault();
        if (animMotion is not null)
            return ReadMotionBuildItem(animMotion, buildPar, durationMs, innerTrigger, triggerShapeId);

        // Preset entrance/emphasis/exit animation.
        var presetClass = cTn.Attribute("presetClass")?.Value;
        var presetIdStr = cTn.Attribute("presetID")?.Value;
        if (string.IsNullOrEmpty(presetClass)) return null;
        if (!int.TryParse(presetIdStr, out var presetId)) return null;

        var presetSubtype = cTn.Attribute("presetSubtype")?.Value;

        var spTgt = FindSpTgt(buildPar);
        if (spTgt is null) return null;
        if (!uint.TryParse(spTgt.Attribute("spid")?.Value, out var shapeId)) return null;

        var (kind, preset) = PptxAnimationMap.OoxmlToAnimationPreset(presetClass, presetId);
        var direction = PptxAnimationMap.SubtypeToAnimationDirection(presetSubtype);

        return new ShapeAnimation
        {
            ShapeId        = shapeId,
            Kind           = kind,
            Preset         = preset,
            Trigger        = innerTrigger,
            DelayMs        = delayMs,
            DurationMs     = durationMs,
            Direction      = direction,
            TriggerShapeId = triggerShapeId,
        };
    }

    private static ShapeAnimation? ReadMotionBuildItem(
        XElement animMotion, XElement buildPar,
        int durationMs, AnimationTrigger trigger, uint? triggerShapeId)
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

        var motion = ParseMotionPath(pathStr, origin, ptsTypes);

        return new ShapeAnimation
        {
            ShapeId        = shapeId,
            Kind           = AnimationKind.Motion,
            Preset         = AnimationPreset.Appear, // unused for motion
            Trigger        = trigger,
            DelayMs        = delayMs,
            DurationMs     = durationMs,
            Motion         = motion,
            TriggerShapeId = triggerShapeId,
        };
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
        var xml = LoadXml(archive, path);
        if (xml?.Root is null) return;

        foreach (var styleEl in xml.Root.Elements(A + "tblStyle"))
        {
            var styleId = styleEl.Attribute("styleId")?.Value;
            if (string.IsNullOrWhiteSpace(styleId)) continue;

            var data = new TableStyleData { StyleId = styleId };

            data.WholeTbl = ReadTableStyleEntry(styleEl.Element(A + "wholeTbl"), scheme);
            data.FirstRow = ReadTableStyleEntry(styleEl.Element(A + "firstRow"), scheme);
            data.LastRow  = ReadTableStyleEntry(styleEl.Element(A + "lastRow"),  scheme);
            data.FirstCol = ReadTableStyleEntry(styleEl.Element(A + "firstCol"), scheme);
            data.LastCol  = ReadTableStyleEntry(styleEl.Element(A + "lastCol"),  scheme);
            data.Band1H   = ReadTableStyleEntry(styleEl.Element(A + "band1H"),   scheme);
            data.Band2H   = ReadTableStyleEntry(styleEl.Element(A + "band2H"),   scheme);
            data.Band1V   = ReadTableStyleEntry(styleEl.Element(A + "band1V"),   scheme);
            data.Band2V   = ReadTableStyleEntry(styleEl.Element(A + "band2V"),   scheme);

            tableStyles[styleId] = data;
        }
    }

    private static TableStyleEntry? ReadTableStyleEntry(XElement? regionEl, PresentationColorScheme scheme)
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

    // ── OPC / Rels helpers ────────────────────────────────────────────────────────

    /// <summary>Loads and parses a .rels file from the archive. Returns empty list on missing/error.</summary>
    private static List<(string id, string type, string target)> LoadRels(ZipArchive archive, string relsPath)
    {
        var entry = archive.GetEntry(relsPath);
        if (entry is null) return new();

        try
        {
            using var stream = entry.Open();
            using var reader = XmlReader.Create(stream, SecureXmlReaderSettings.Create());
            var doc = XDocument.Load(reader);
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
            using var reader = XmlReader.Create(stream, SecureXmlReaderSettings.Create());
            return XDocument.Load(reader);
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
