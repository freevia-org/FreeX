using System.IO;
using FreeP.Core.IO;
using FreeP.Core.Model;

namespace FreeP.App.Host.Tests;

/// <summary>
/// Wave 6B: master/layout full round-trip + txStyles tests.
/// Validates that SlideMaster.TextStyles, SlideMaster.ColorMap, layout placeholder count/types,
/// and slide→layout linkage survive a write→read cycle.
/// </summary>
public sealed class MasterLayoutRoundTripTests : IDisposable
{
    private readonly string _tempDir =
        Path.Combine(Path.GetTempPath(), "FreeP.MasterTests", Guid.NewGuid().ToString("N"));

    public MasterLayoutRoundTripTests() => Directory.CreateDirectory(_tempDir);

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best-effort */ }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Corpus round-trip: read 01-title-slide.pptx, write, read back, assert fidelity
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>Returns true and sets <paramref name="path"/> if corpus file exists; otherwise returns false.</summary>
    private static bool TryGetCorpus(string filename, out string path)
    {
        path = TestWorkspaceFileLocator.TryFindFileFromBaseDirectory(
            "tools", "FreeP.RenderCompare", "corpus", filename) ?? string.Empty;
        return path.Length > 0;
    }

    [Fact]
    public void CorpusTitleSlide_HasAtLeastOneMaster()
    {
        if (!TryGetCorpus("01-title-slide.pptx", out var path)) return;

        var pres = PptxPackageReader.Read(path);
        pres.Masters.Should().NotBeEmpty("01-title-slide.pptx must have a slide master");
    }

    [Fact]
    public void CorpusTitleSlide_HasAtLeastOneLayout()
    {
        if (!TryGetCorpus("01-title-slide.pptx", out var path)) return;

        var pres = PptxPackageReader.Read(path);
        pres.Layouts.Should().NotBeEmpty("01-title-slide.pptx must have at least one layout");
    }

    [Fact]
    public void CorpusTitleSlide_TxStylesParsed()
    {
        if (!TryGetCorpus("01-title-slide.pptx", out var path)) return;

        var pres = PptxPackageReader.Read(path);
        var master = pres.Masters.First();
        master.TextStyles.Should().NotBeNull("slideMaster1 should have p:txStyles");

        // Title style level 0 should have a font size set in a real Office deck
        var titleLvl0 = master.TextStyles!.TitleStyle[0];
        titleLvl0.Should().NotBeNull("titleStyle/lvl1pPr should be present");
        titleLvl0!.FontSizePt.Should().BeGreaterThan(0, "title font size should be > 0pt");
    }

    [Fact]
    public void CorpusTitleSlide_ColorMapParsed()
    {
        if (!TryGetCorpus("01-title-slide.pptx", out var path)) return;

        var pres = PptxPackageReader.Read(path);
        var master = pres.Masters.First();
        master.ColorMap.Should().NotBeNull("slideMaster1 must have p:clrMap");
        master.ColorMap.Should().ContainKey("bg1", "standard Office clrMap has bg1");
    }

    [Fact]
    public void CorpusTitleSlide_LayoutLinkagePreserved_AfterRoundTrip()
    {
        if (!TryGetCorpus("01-title-slide.pptx", out var path)) return;

        var pres = PptxPackageReader.Read(path);
        var originalMasterCount = pres.Masters.Count;
        var originalLayoutCount = pres.Layouts.Count;

        // Write and re-read
        var outPath = Path.Combine(_tempDir, "rt-title-slide.pptx");
        PptxPackageWriter.Write(pres, outPath);
        var rt = PptxPackageReader.Read(outPath);

        rt.Masters.Should().HaveCount(originalMasterCount, "master count must be preserved");
        rt.Layouts.Should().HaveCount(originalLayoutCount, "layout count must be preserved");

        // The first slide should still reference a valid layout
        rt.Slides.Should().NotBeEmpty();
        rt.Slides[0].LayoutId.Should().NotBeNullOrEmpty("slide layout linkage must survive round-trip");
        var matchedLayout = rt.Layouts.Find(l => l.Id == rt.Slides[0].LayoutId);
        matchedLayout.Should().NotBeNull("slide's LayoutId must match an existing layout after round-trip");
    }

    [Fact]
    public void CorpusTitleSlide_TxStylesRoundTrip()
    {
        if (!TryGetCorpus("01-title-slide.pptx", out var path)) return;

        var pres = PptxPackageReader.Read(path);
        var originalMaster = pres.Masters.First();
        if (originalMaster.TextStyles is null) return; // no txStyles to round-trip

        var originalTitleLvl0FontSize = originalMaster.TextStyles.TitleStyle[0]?.FontSizePt;

        // Round-trip
        var outPath = Path.Combine(_tempDir, "rt-txstyles.pptx");
        PptxPackageWriter.Write(pres, outPath);
        var rt = PptxPackageReader.Read(outPath);

        rt.Masters.Should().NotBeEmpty();
        var rtMaster = rt.Masters.First();
        rtMaster.TextStyles.Should().NotBeNull("txStyles must survive round-trip");

        var rtTitleLvl0 = rtMaster.TextStyles!.TitleStyle[0];
        if (originalTitleLvl0FontSize.HasValue)
        {
            rtTitleLvl0.Should().NotBeNull("titleStyle/lvl1pPr must survive round-trip");
            rtTitleLvl0!.FontSizePt.Should().Be(originalTitleLvl0FontSize,
                "title font size must be preserved through round-trip");
        }
    }

    [Fact]
    public void CorpusTitleSlide_ColorMapRoundTrip()
    {
        if (!TryGetCorpus("01-title-slide.pptx", out var path)) return;

        var pres = PptxPackageReader.Read(path);
        var originalMaster = pres.Masters.First();
        if (originalMaster.ColorMap is null) return; // no colorMap to round-trip

        var originalBg1 = originalMaster.ColorMap["bg1"];

        var outPath = Path.Combine(_tempDir, "rt-colormap.pptx");
        PptxPackageWriter.Write(pres, outPath);
        var rt = PptxPackageReader.Read(outPath);

        rt.Masters.Should().NotBeEmpty();
        var rtMaster = rt.Masters.First();
        rtMaster.ColorMap.Should().NotBeNull("clrMap must survive round-trip");
        rtMaster.ColorMap!.Should().ContainKey("bg1");
        rtMaster.ColorMap["bg1"].Should().Be(originalBg1, "bg1 mapping must be preserved");
    }

    [Fact]
    public void CorpusTitleSlide_LayoutPlaceholderCountPreserved()
    {
        if (!TryGetCorpus("01-title-slide.pptx", out var path)) return;

        var pres = PptxPackageReader.Read(path);
        // Record placeholder counts per layout by name
        var layoutPhCounts = pres.Layouts
            .ToDictionary(l => l.Name + "_" + l.LayoutType, l => l.Placeholders.Count);

        var outPath = Path.Combine(_tempDir, "rt-layouts.pptx");
        PptxPackageWriter.Write(pres, outPath);
        var rt = PptxPackageReader.Read(outPath);

        foreach (var rtLayout in rt.Layouts)
        {
            var key = rtLayout.Name + "_" + rtLayout.LayoutType;
            if (layoutPhCounts.TryGetValue(key, out var expected))
            {
                rtLayout.Placeholders.Should().HaveCount(expected,
                    $"layout '{rtLayout.Name}' placeholder count must be preserved");
            }
        }
    }

    [Fact]
    public void CorpusTitleSlide_LayoutLstStyleRoundTrip()
    {
        if (!TryGetCorpus("01-title-slide.pptx", out var path)) return;

        var pres = PptxPackageReader.Read(path);

        // Find any layout placeholder that has a non-null LstStyle, record its layout index
        SlideShape? lstStylePh = null;
        int lstStyleLayoutIdx = -1;
        for (int li = 0; li < pres.Layouts.Count; li++)
        {
            var ph = pres.Layouts[li].Placeholders.Find(p => p.TextBody?.LstStyle is { } ls && ls.HasAny);
            if (ph is not null) { lstStylePh = ph; lstStyleLayoutIdx = li; break; }
        }
        if (lstStylePh is null) return; // no layout placeholder with lstStyle in this corpus file

        var origPh = lstStylePh.Placeholder!;
        var originalAlign = lstStylePh.TextBody!.LstStyle![0]?.Align;

        var outPath = Path.Combine(_tempDir, "rt-lstyle.pptx");
        PptxPackageWriter.Write(pres, outPath);
        var rt = PptxPackageReader.Read(outPath);

        // Use same index (layout order is preserved by the writer)
        rt.Layouts.Should().HaveCountGreaterThan(lstStyleLayoutIdx, "layout count must be preserved");
        var rtLayout = rt.Layouts[lstStyleLayoutIdx];

        // Find matching placeholder by type+idx
        var rtPh = rtLayout.Placeholders.Find(p =>
            p.Placeholder?.Type == origPh.Type && p.Placeholder?.Idx == origPh.Idx);
        rtPh.Should().NotBeNull("layout placeholder must survive round-trip");
        rtPh!.TextBody?.LstStyle.Should().NotBeNull("lstStyle must survive round-trip");

        if (originalAlign.HasValue)
            rtPh.TextBody!.LstStyle![0]?.Align.Should().Be(originalAlign,
                "lstStyle level alignment must be preserved");
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Synthetic round-trip: build a presentation with txStyles in code
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Synthetic_TxStyles_Roundtrip()
    {
        var pres = new Presentation();

        var master = new SlideMaster { Id = "rId1" };
        master.TextStyles = new MasterTextStyles();
        master.TextStyles.TitleStyle[0] = new TextStyleLevel
        {
            FontSizePt = 36.0,
            Bold = true,
            Align = TextAlign.Center,
            LatinFont = "+mj-lt"
        };
        master.TextStyles.BodyStyle[0] = new TextStyleLevel
        {
            FontSizePt = 24.0,
            Bold = false,
            BulletKind = BulletKind.Char,
            BulletChar = "•"
        };
        master.TextStyles.OtherStyle[0] = new TextStyleLevel { FontSizePt = 10.0 };
        master.ColorMap = new Dictionary<string, string>
        {
            ["bg1"] = "lt1", ["tx1"] = "dk1", ["bg2"] = "lt2", ["tx2"] = "dk2",
            ["accent1"] = "accent1", ["accent2"] = "accent2",
            ["accent3"] = "accent3", ["accent4"] = "accent4",
            ["accent5"] = "accent5", ["accent6"] = "accent6",
            ["hlink"] = "hlink", ["folHlink"] = "folHlink"
        };
        pres.Masters.Add(master);

        var layout = new SlideLayout { Id = "rIdL1", MasterId = "rId1", Name = "Title Slide", LayoutType = SlideLayoutType.Title };
        // Add a placeholder with lstStyle
        var titlePh = new SlideShape
        {
            Id = 1, Name = "Title 1",
            ExtentCxEmu = 8229600, ExtentCyEmu = 1143000,
            OffsetXEmu = 457200, OffsetYEmu = 274638,
            Placeholder = new Placeholder { Type = PlaceholderType.CenteredTitle, Idx = 0 },
            TextBody = new TextBody
            {
                LstStyle = new TextStyleLevels()
            }
        };
        titlePh.TextBody.LstStyle![0] = new TextStyleLevel { Align = TextAlign.Center, FontSizePt = 44.0 };
        layout.Placeholders.Add(titlePh);
        pres.Layouts.Add(layout);

        var slide = new Slide { LayoutId = "rIdL1" };
        slide.Shapes.Add(new SlideShape
        {
            Id = 2, Name = "Title 1",
            Placeholder = new Placeholder { Type = PlaceholderType.CenteredTitle, Idx = 0 },
            TextBody = new TextBody()
        });
        slide.Shapes[0].TextBody!.Paragraphs.Add(new Paragraph());
        slide.Shapes[0].TextBody!.Paragraphs[0].Runs.Add(new Run { Text = "Hello" });
        pres.Slides.Add(slide);

        var path = Path.Combine(_tempDir, "synthetic-txstyles.pptx");
        PptxPackageWriter.Write(pres, path);

        var rt = PptxPackageReader.Read(path);

        rt.Masters.Should().HaveCount(1);
        var rtMaster = rt.Masters[0];
        rtMaster.TextStyles.Should().NotBeNull();
        rtMaster.TextStyles!.TitleStyle[0].Should().NotBeNull();
        rtMaster.TextStyles.TitleStyle[0]!.FontSizePt.Should().Be(36.0);
        rtMaster.TextStyles.TitleStyle[0]!.Bold.Should().BeTrue();
        rtMaster.TextStyles.TitleStyle[0]!.Align.Should().Be(TextAlign.Center);
        rtMaster.TextStyles.TitleStyle[0]!.LatinFont.Should().Be("+mj-lt");

        rtMaster.TextStyles.BodyStyle[0].Should().NotBeNull();
        rtMaster.TextStyles.BodyStyle[0]!.BulletKind.Should().Be(BulletKind.Char);
        rtMaster.TextStyles.BodyStyle[0]!.BulletChar.Should().Be("•");

        rtMaster.TextStyles.OtherStyle[0].Should().NotBeNull();
        rtMaster.TextStyles.OtherStyle[0]!.FontSizePt.Should().Be(10.0);

        rtMaster.ColorMap.Should().ContainKey("bg1");
        rtMaster.ColorMap["bg1"].Should().Be("lt1");

        rt.Layouts.Should().HaveCount(1);
        var rtLayout = rt.Layouts[0];
        rtLayout.Placeholders.Should().HaveCount(1);
        var rtTitlePh = rtLayout.Placeholders[0];
        rtTitlePh.TextBody?.LstStyle.Should().NotBeNull();
        rtTitlePh.TextBody!.LstStyle![0].Should().NotBeNull();
        rtTitlePh.TextBody.LstStyle[0]!.Align.Should().Be(TextAlign.Center);
        rtTitlePh.TextBody.LstStyle[0]!.FontSizePt.Should().Be(44.0);

        rt.Slides.Should().HaveCount(1);
        rt.Slides[0].LayoutId.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Synthetic_MultipleLayouts_AllPreserved()
    {
        var pres = new Presentation();
        var master = new SlideMaster { Id = "rId1" };
        pres.Masters.Add(master);

        var layouts = new[]
        {
            new SlideLayout { Id = "rIdL1", MasterId = "rId1", Name = "Title Slide", LayoutType = SlideLayoutType.Title },
            new SlideLayout { Id = "rIdL2", MasterId = "rId1", Name = "Title, Content", LayoutType = SlideLayoutType.TitleContent },
            new SlideLayout { Id = "rIdL3", MasterId = "rId1", Name = "Blank", LayoutType = SlideLayoutType.Blank },
        };
        foreach (var l in layouts) pres.Layouts.Add(l);

        var slide1 = new Slide { LayoutId = "rIdL1" };
        var slide2 = new Slide { LayoutId = "rIdL2" };
        pres.Slides.Add(slide1);
        pres.Slides.Add(slide2);

        var path = Path.Combine(_tempDir, "multi-layout.pptx");
        PptxPackageWriter.Write(pres, path);
        var rt = PptxPackageReader.Read(path);

        rt.Layouts.Should().HaveCount(3, "all three layouts must round-trip");
        rt.Layouts.Select(l => l.LayoutType).Should().Contain(SlideLayoutType.Title);
        rt.Layouts.Select(l => l.LayoutType).Should().Contain(SlideLayoutType.TitleContent);
        rt.Layouts.Select(l => l.LayoutType).Should().Contain(SlideLayoutType.Blank);

        rt.Slides[0].LayoutId.Should().NotBeNullOrEmpty();
        rt.Slides[1].LayoutId.Should().NotBeNullOrEmpty();
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // MM5: per-slide p:clrMapOvr / a:overrideClrMapping round-trip
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A slide with a non-null ColorMapOverride must survive a write→read cycle:
    /// the override attributes must be preserved exactly and the masterClrMapping
    /// variant must NOT appear (i.e. a:overrideClrMapping must be emitted).
    /// </summary>
    [Fact]
    public void Synthetic_SlideClrMapOvr_Override_RoundTrips()
    {
        var pres = new Presentation();
        var master = new SlideMaster { Id = "rId1" };
        master.ColorMap = new Dictionary<string, string>
        {
            ["bg1"] = "lt1", ["tx1"] = "dk1", ["bg2"] = "lt2", ["tx2"] = "dk2",
            ["accent1"] = "accent1", ["accent2"] = "accent2", ["accent3"] = "accent3",
            ["accent4"] = "accent4", ["accent5"] = "accent5", ["accent6"] = "accent6",
            ["hlink"] = "hlink", ["folHlink"] = "folHlink"
        };
        pres.Masters.Add(master);

        var layout = new SlideLayout { Id = "rIdL1", MasterId = "rId1", Name = "Title Slide", LayoutType = SlideLayoutType.Title };
        pres.Layouts.Add(layout);

        // Slide with inverted clrMapOvr: tx1→lt1, bg1→dk1
        var invertedOverride = new Dictionary<string, string>
        {
            ["bg1"] = "dk1", ["tx1"] = "lt1", ["bg2"] = "dk2", ["tx2"] = "lt2",
            ["accent1"] = "accent1", ["accent2"] = "accent2", ["accent3"] = "accent3",
            ["accent4"] = "accent4", ["accent5"] = "accent5", ["accent6"] = "accent6",
            ["hlink"] = "hlink", ["folHlink"] = "folHlink"
        };
        var slideWithOvr = new Slide { LayoutId = "rIdL1", ColorMapOverride = invertedOverride };
        pres.Slides.Add(slideWithOvr);

        // Slide without override (uses master map)
        var slideNoOvr = new Slide { LayoutId = "rIdL1" };
        pres.Slides.Add(slideNoOvr);

        var path = Path.Combine(_tempDir, "clrmapovr-roundtrip.pptx");
        PptxPackageWriter.Write(pres, path);
        var rt = PptxPackageReader.Read(path);

        rt.Slides.Should().HaveCount(2);

        // Slide 0: must have the override preserved with inverted values
        var rtSlideOvr = rt.Slides[0];
        rtSlideOvr.ColorMapOverride.Should().NotBeNull(
            "slide with a:overrideClrMapping must have ColorMapOverride after round-trip");
        rtSlideOvr.ColorMapOverride!.Should().ContainKey("tx1");
        rtSlideOvr.ColorMapOverride["tx1"].Should().Be("lt1",
            "tx1→lt1 must survive write→read cycle");
        rtSlideOvr.ColorMapOverride.Should().ContainKey("bg1");
        rtSlideOvr.ColorMapOverride["bg1"].Should().Be("dk1",
            "bg1→dk1 must survive write→read cycle");

        // Slide 1: no override → ColorMapOverride must be null
        var rtSlideNoOvr = rt.Slides[1];
        rtSlideNoOvr.ColorMapOverride.Should().BeNull(
            "slide with a:masterClrMapping must have null ColorMapOverride after round-trip");
    }

    /// <summary>
    /// A slide without a ColorMapOverride must emit a:masterClrMapping on write,
    /// which must round-trip as null ColorMapOverride (inherit from master).
    /// </summary>
    [Fact]
    public void Synthetic_SlideClrMapOvr_MasterClrMapping_RoundTrips_AsNull()
    {
        var pres = new Presentation();
        var master = new SlideMaster { Id = "rId1" };
        pres.Masters.Add(master);
        var layout = new SlideLayout { Id = "rIdL1", MasterId = "rId1", Name = "Blank", LayoutType = SlideLayoutType.Blank };
        pres.Layouts.Add(layout);

        var slide = new Slide { LayoutId = "rIdL1" }; // no ColorMapOverride
        pres.Slides.Add(slide);

        var path = Path.Combine(_tempDir, "masterclrmapping-roundtrip.pptx");
        PptxPackageWriter.Write(pres, path);
        var rt = PptxPackageReader.Read(path);

        rt.Slides.Should().HaveCount(1);
        rt.Slides[0].ColorMapOverride.Should().BeNull(
            "slide without override must round-trip as null ColorMapOverride (a:masterClrMapping)");
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // MM4: multi-master theme ownership tests
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A single-master deck must still produce exactly one theme1.xml and must NOT regress:
    /// the master's Theme property must be set after read, and round-trip preserves the theme name.
    /// </summary>
    [Fact]
    public void SingleMaster_ThemeOwnedByMaster_NoRegression()
    {
        var pres = new Presentation();
        var master = new SlideMaster { Id = "rId1" };
        master.Theme = new PresentationTheme { Name = "Single Theme" };
        master.Theme.ColorScheme[ThemeColorSlot.Accent1] = SrgbColor.FromRgb(0x0000FF); // blue
        pres.Masters.Add(master);
        pres.Theme = master.Theme; // presentation.Theme mirrors master (single-master convention)

        var layout = new SlideLayout { Id = "rIdL1", MasterId = "rId1", Name = "Blank", LayoutType = SlideLayoutType.Blank };
        pres.Layouts.Add(layout);

        var slide = new Slide { LayoutId = "rIdL1" };
        pres.Slides.Add(slide);

        var path = Path.Combine(_tempDir, "single-master-theme.pptx");
        PptxPackageWriter.Write(pres, path);

        // Verify only theme1.xml exists in the archive.
        using var zip = System.IO.Compression.ZipFile.OpenRead(path);
        var themeEntries = zip.Entries.Where(e => e.FullName.StartsWith("ppt/theme/", StringComparison.OrdinalIgnoreCase)).ToList();
        themeEntries.Should().HaveCount(1, "single-master deck must produce exactly one theme part");
        themeEntries[0].FullName.Should().Be("ppt/theme/theme1.xml", "the single theme must be theme1.xml");

        var rt = PptxPackageReader.Read(path);
        rt.Masters.Should().HaveCount(1);
        rt.Masters[0].Theme.Should().NotBeNull("master.Theme must be populated by the reader");
        rt.Masters[0].Theme!.Name.Should().Be("Single Theme", "theme name must round-trip");
        rt.Masters[0].Theme!.ColorScheme[ThemeColorSlot.Accent1].Should().Be(SrgbColor.FromRgb(0x0000FF),
            "accent1 color must survive round-trip");
    }

    /// <summary>
    /// A 2-master deck: master1 accent1=blue, master2 accent1=red.
    /// After write→read each master still has its own distinct theme.
    /// Color resolution for a slide on master1 → blue; slide on master2 → red.
    /// </summary>
    [Fact]
    public void TwoMaster_EachMasterOwnsDistinctTheme_ColorsResolveCorrectly()
    {
        var blue = SrgbColor.FromRgb(0x0000FF);
        var red  = SrgbColor.FromRgb(0xFF0000);

        var pres = new Presentation();

        // Master 1: accent1 = blue
        var master1 = new SlideMaster { Id = "rId1" };
        master1.Theme = new PresentationTheme { Name = "Blue Theme" };
        master1.Theme.ColorScheme[ThemeColorSlot.Accent1] = blue;
        pres.Masters.Add(master1);

        // Master 2: accent1 = red
        var master2 = new SlideMaster { Id = "rId2" };
        master2.Theme = new PresentationTheme { Name = "Red Theme" };
        master2.Theme.ColorScheme[ThemeColorSlot.Accent1] = red;
        pres.Masters.Add(master2);

        // presentation.Theme = first master's theme (backward-compat convention)
        pres.Theme = master1.Theme;

        var layout1 = new SlideLayout { Id = "rIdL1", MasterId = "rId1", Name = "Blank", LayoutType = SlideLayoutType.Blank };
        var layout2 = new SlideLayout { Id = "rIdL2", MasterId = "rId2", Name = "Blank", LayoutType = SlideLayoutType.Blank };
        pres.Layouts.Add(layout1);
        pres.Layouts.Add(layout2);

        var slide1 = new Slide { LayoutId = "rIdL1" }; // on master1 (blue)
        var slide2 = new Slide { LayoutId = "rIdL2" }; // on master2 (red)
        pres.Slides.Add(slide1);
        pres.Slides.Add(slide2);

        var path = Path.Combine(_tempDir, "two-master-themes.pptx");
        PptxPackageWriter.Write(pres, path);

        // Verify theme1.xml AND theme2.xml both exist.
        using (var zip = System.IO.Compression.ZipFile.OpenRead(path))
        {
            var themeEntries = zip.Entries
                .Where(e => e.FullName.StartsWith("ppt/theme/", StringComparison.OrdinalIgnoreCase))
                .OrderBy(e => e.FullName)
                .ToList();
            themeEntries.Should().HaveCount(2, "2-master deck must produce 2 theme parts");
            themeEntries[0].FullName.Should().Be("ppt/theme/theme1.xml");
            themeEntries[1].FullName.Should().Be("ppt/theme/theme2.xml");
        }

        // Round-trip: read back and verify per-master themes.
        var rt = PptxPackageReader.Read(path);
        rt.Masters.Should().HaveCount(2);

        var rtMaster1 = rt.Masters[0];
        var rtMaster2 = rt.Masters[1];

        rtMaster1.Theme.Should().NotBeNull("master1.Theme must be populated");
        rtMaster2.Theme.Should().NotBeNull("master2.Theme must be populated");

        rtMaster1.Theme!.Name.Should().Be("Blue Theme");
        rtMaster2.Theme!.Name.Should().Be("Red Theme");

        rtMaster1.Theme.ColorScheme[ThemeColorSlot.Accent1].Should().Be(blue,
            "master1 accent1 must be blue after round-trip");
        rtMaster2.Theme.ColorScheme[ThemeColorSlot.Accent1].Should().Be(red,
            "master2 accent1 must be red after round-trip");
    }

}
