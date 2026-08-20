using System.IO.Compression;
using System.Xml.Linq;
using FreeP.Core.IO;

namespace FreeP.App.Compositor.Tests;

/// <summary>
/// R156 HIGH: <see cref="PptxPackageReader"/>'s <c>ReadBackground</c> never resolved a
/// <c>p:bg/p:bgRef</c> (PowerPoint's Design &gt; Variants &gt; "Background Styles" gallery, and
/// Format Background's theme gradient/pattern presets) against the theme's format scheme
/// (<c>a:bgFillStyleLst</c>). It only read whatever color child happened to be nested inside the
/// <c>bgRef</c> element and returned a flat solid, even though all 12 of PowerPoint's built-in
/// Background Styles are gradients built from <c>bgFillStyleLst</c>. The fix threads the
/// <c>PresentationTheme?</c> already live at every caller into <c>ReadBackground</c> and resolves
/// the reference through the same <c>ResolveStyleMatrixFill</c> algorithm already used for a
/// shape's <c>p:style/a:fillRef</c>.
///
/// <see cref="PptxPackageWriter"/> never emits <c>p:bgRef</c> for a slide/layout/master background
/// (only <c>p:bgPr</c> with an explicit fill), so these tests hand-author the <c>p:bg/p:bgRef</c> /
/// <c>a:fmtScheme</c> XML the way real PowerPoint does, by post-processing a package produced by
/// <see cref="PptxPackageWriter"/> -- the same pattern <c>ShapeStyleThemeReferenceTests</c> uses
/// for the sibling <c>p:style/a:fillRef</c> case.
/// </summary>
public sealed class SlideBackgroundBgRefThemeReferenceTests
{
    private static readonly XNamespace P = "http://schemas.openxmlformats.org/presentationml/2006/main";
    private static readonly XNamespace A = "http://schemas.openxmlformats.org/drawingml/2006/main";

    private const string BgFillStyleLstWithGradientAtIdx2 =
        "<a:bgFillStyleLst xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\">" +
        "<a:solidFill><a:schemeClr val=\"phClr\"/></a:solidFill>" +
        "<a:gradFill><a:gsLst>" +
        "<a:gs pos=\"0\"><a:schemeClr val=\"phClr\"><a:tint val=\"50000\"/></a:schemeClr></a:gs>" +
        "<a:gs pos=\"100000\"><a:schemeClr val=\"phClr\"/></a:gs>" +
        "</a:gsLst><a:lin ang=\"5400000\" scaled=\"0\"/></a:gradFill>" +
        "<a:solidFill><a:schemeClr val=\"phClr\"/></a:solidFill>" +
        "</a:bgFillStyleLst>";

    // ── 1. Slide bgRef resolves against the theme's bgFillStyleLst (the reported gesture) ─────

    [Fact]
    public void ReadSlide_ResolvesBgRef_AgainstBackgroundFillStyleList_WhenPresent()
    {
        using var stream = BuildBasePackage();
        InjectSlideBgRefAndFormatScheme(stream, "<a:schemeClr val=\"accent2\"/>", idx: 1002);

        var reloaded = PptxPackageReader.Read(stream);
        var background = reloaded.Slides[0].Background;

        var gradient = background.Should().BeOfType<ShapeFill.Gradient>(
            "idx=1002 (1002 - 1000 = entry 2, a gradFill) must resolve against bgFillStyleLst, " +
            "not fall back to a flat solid guess").Subject;
        gradient.Stops.Should().HaveCount(2);
        gradient.Stops[0].Color.Resolved.Should().Be(
            ThemeColorTransform.ApplyTint(PresentationColorScheme.CreateDefault()[ThemeColorSlot.Accent2], 0.5),
            "the phClr placeholder in the first gradient stop must be substituted with the bgRef's accent2 color, " +
            "preserving the format-scheme entry's own 50% tint modifier");
        gradient.Stops[1].Color.Resolved.Should().Be(
            PresentationColorScheme.CreateDefault()[ThemeColorSlot.Accent2]);
    }

    // ── 2. Sibling: slide master bgRef also resolves (theme threading at the master call site) ─

    [Fact]
    public void ReadSlideMaster_ResolvesBgRef_AgainstBackgroundFillStyleList_WhenPresent()
    {
        using var stream = BuildBasePackage();
        InjectMasterBgRefAndFormatScheme(stream, "<a:schemeClr val=\"accent2\"/>", idx: 1002);

        var reloaded = PptxPackageReader.Read(stream);
        var background = reloaded.Masters[0].Background;

        var gradient = background.Should().BeOfType<ShapeFill.Gradient>(
            "the slide master's own ReadSlideMaster call site must also thread theme into ReadBackground")
            .Subject;
        gradient.Stops.Should().HaveCount(2);
    }

    // ── 3. Sibling no-regression: an explicit p:bgPr fill is unaffected by the bgRef fix ───────

    [Fact]
    public void ReadSlide_ExplicitBgPrFill_IsUnaffectedByBgRefResolution()
    {
        var presentation = new Presentation { SlideSizeCxEmu = 9144000, SlideSizeCyEmu = 6858000 };
        var slide = new Slide
        {
            Id = "rId1",
            Background = new ShapeFill.Solid(new SrgbColor(0x12, 0x34, 0x56)),
        };
        presentation.Slides.Add(slide);

        using var stream = new MemoryStream();
        PptxPackageWriter.Write(presentation, stream);
        stream.Position = 0;

        var reloaded = PptxPackageReader.Read(stream);
        var solid = reloaded.Slides[0].Background.Should().BeOfType<ShapeFill.Solid>(
            "the writer emits p:bgPr for an explicit background; bgRef resolution must not run").Subject;
        solid.Color.Resolved.Should().Be(new SrgbColor(0x12, 0x34, 0x56));
    }

    // ── 4. Sibling no-regression: no matching bgFillStyleLst entry falls back to prior behaviour ─

    [Fact]
    public void ReadSlide_BgRefWithNoMatchingFormatSchemeEntry_FallsBackToOwnColor()
    {
        using var stream = BuildBasePackage();
        // idx 1099 - 1000 = 99, far past the 3-entry bgFillStyleLst -- no format-scheme match.
        InjectSlideBgRefAndFormatScheme(stream, "<a:schemeClr val=\"accent3\"/>", idx: 1099);

        var reloaded = PptxPackageReader.Read(stream);
        var background = reloaded.Slides[0].Background;

        var solid = background.Should().BeOfType<ShapeFill.Solid>(
            "an idx with no bgFillStyleLst entry must fall back to the bgRef's own color child, " +
            "matching the pre-fix behaviour for unresolvable references").Subject;
        solid.Color.Resolved.Should().Be(PresentationColorScheme.CreateDefault()[ThemeColorSlot.Accent3]);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────

    private static MemoryStream BuildBasePackage()
    {
        var presentation = new Presentation { SlideSizeCxEmu = 9144000, SlideSizeCyEmu = 6858000 };
        var slide = new Slide { Id = "rId1" };
        presentation.Slides.Add(slide);

        var stream = new MemoryStream();
        PptxPackageWriter.Write(presentation, stream);
        return stream;
    }

    private static void InjectSlideBgRefAndFormatScheme(MemoryStream stream, string colorInnerXml, int idx)
        => InjectBgRefAndFormatScheme(stream, entryName: e =>
            e.FullName.StartsWith("ppt/slides/slide", StringComparison.OrdinalIgnoreCase)
            && e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase),
            colorInnerXml, idx);

    private static void InjectMasterBgRefAndFormatScheme(MemoryStream stream, string colorInnerXml, int idx)
        => InjectBgRefAndFormatScheme(stream, entryName: e =>
            e.FullName.StartsWith("ppt/slideMasters/slideMaster", StringComparison.OrdinalIgnoreCase)
            && e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase),
            colorInnerXml, idx);

    /// <summary>
    /// Replaces theme1.xml's real <c>a:bgFillStyleLst</c> with a 3-entry list whose 2nd entry is a
    /// gradient, and injects a hand-authored <c>&lt;p:bg&gt;&lt;p:bgRef idx="..."&gt;...&lt;/p:bgRef&gt;&lt;/p:bg&gt;</c>
    /// as the first child of <c>p:cSld</c> for the target part -- which <see cref="PptxPackageWriter"/>
    /// never emits for a slide/master background.
    /// </summary>
    private static void InjectBgRefAndFormatScheme(
        MemoryStream stream, Func<ZipArchiveEntry, bool> entryName, string colorInnerXml, int idx)
    {
        stream.Position = 0;
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true))
        {
            var themeEntry = archive.Entries.Single(e =>
                e.FullName.Equals("ppt/theme/theme1.xml", StringComparison.OrdinalIgnoreCase));
            XDocument themeDoc;
            using (var input = themeEntry.Open())
                themeDoc = XDocument.Load(input);

            var fmtScheme = themeDoc.Descendants(A + "fmtScheme").Single();
            fmtScheme.Element(A + "bgFillStyleLst")!.ReplaceNodes(
                XElement.Parse(BgFillStyleLstWithGradientAtIdx2).Elements());

            themeEntry.Delete();
            var themeReplacement = archive.CreateEntry(themeEntry.FullName);
            using (var themeOutput = themeReplacement.Open())
                themeDoc.Save(themeOutput, SaveOptions.DisableFormatting);

            var partEntry = archive.Entries.Single(entryName);
            XDocument partDoc;
            using (var input = partEntry.Open())
                partDoc = XDocument.Load(input);

            var cSld = partDoc.Root!.Element(P + "cSld")!;
            var bgEl = XElement.Parse(
                $"<p:bg xmlns:p=\"{P.NamespaceName}\" xmlns:a=\"{A.NamespaceName}\">" +
                $"<p:bgRef idx=\"{idx}\">{colorInnerXml}</p:bgRef></p:bg>");
            cSld.AddFirst(bgEl);

            partEntry.Delete();
            var partReplacement = archive.CreateEntry(partEntry.FullName);
            using (var partOutput = partReplacement.Open())
                partDoc.Save(partOutput, SaveOptions.DisableFormatting);
        }

        stream.Position = 0;
    }
}
