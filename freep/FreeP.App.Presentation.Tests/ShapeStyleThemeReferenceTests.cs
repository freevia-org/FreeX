using System.IO.Compression;
using System.Xml.Linq;
using FreeP.Core.IO;

namespace FreeP.App.Compositor.Tests;

/// <summary>
/// R135 HIGH: <see cref="PptxPackageReader"/> never resolved a shape's <c>p:style</c>
/// <c>a:fillRef</c> / <c>a:effectRef</c> against the theme's format scheme (<c>a:fmtScheme</c>).
/// PowerPoint's built-in Shape Styles gallery encodes a styled shape's fill/effects purely as a
/// fillStyleLst/bgFillStyleLst/effectStyleLst index reference (with an <c>a:schemeClr val="phClr"</c>
/// placeholder color substituted from the reference), with NO explicit <c>spPr</c> fill or
/// effectLst at all. Without resolving <c>p:style</c>, every shape styled from the gallery
/// imported blank.
///
/// <see cref="PptxPackageWriter"/> never emits <c>p:style</c> at all (see
/// <c>PptxPackageReaderSourceTests</c>), so these tests hand-author the <c>p:style</c> /
/// <c>a:fmtScheme</c> XML the way real PowerPoint does, by post-processing a package produced by
/// <see cref="PptxPackageWriter"/> -- the same pattern
/// <c>ChartSeriesOrderTests.RewriteChartSeriesOrder</c> uses for XML shapes the writer doesn't emit.
/// </summary>
public sealed class ShapeStyleThemeReferenceTests
{
    private static readonly XNamespace P = "http://schemas.openxmlformats.org/presentationml/2006/main";
    private static readonly XNamespace A = "http://schemas.openxmlformats.org/drawingml/2006/main";

    // ── Fixtures ─────────────────────────────────────────────────────────────────

    private const string FillStyleLstWithPhClrAtIdx2 =
        "<a:fillStyleLst xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\">" +
        "<a:noFill/>" +
        "<a:solidFill><a:schemeClr val=\"phClr\"/></a:solidFill>" +
        "<a:noFill/>" +
        "</a:fillStyleLst>";

    private const string BgFillStyleLstWithPhClrAtIdx2 =
        "<a:bgFillStyleLst xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\">" +
        "<a:noFill/>" +
        "<a:solidFill><a:schemeClr val=\"phClr\"/></a:solidFill>" +
        "<a:noFill/>" +
        "</a:bgFillStyleLst>";

    private const string EffectStyleLstWithOuterShadowAtIdx2 =
        "<a:effectStyleLst xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\">" +
        "<a:effectStyle><a:effectLst/></a:effectStyle>" +
        "<a:effectStyle><a:effectLst>" +
        "<a:outerShdw blurRad=\"40000\" dist=\"20000\" dir=\"5400000\"><a:srgbClr val=\"000000\"/></a:outerShdw>" +
        "</a:effectLst></a:effectStyle>" +
        "<a:effectStyle><a:effectLst/></a:effectStyle>" +
        "</a:effectStyleLst>";

    // ── 1. fillRef resolves a shape with no explicit fill ───────────────────────

    [Fact]
    public void ReadSp_ResolvesFillRef_WhenShapeHasNoExplicitFill()
    {
        var shape = new SlideShape { Id = 1, Name = "Styled Rect" };
        using var stream = BuildBasePackage(shape);
        InjectStyleAndFormatScheme(stream, "Styled Rect",
            styleInnerXml: "<a:fillRef idx=\"2\"><a:schemeClr val=\"accent1\"/></a:fillRef>",
            fillStyleLstXml: FillStyleLstWithPhClrAtIdx2);

        var reloaded = PptxPackageReader.Read(stream);
        var reloadedShape = reloaded.Slides[0].Shapes.Single();

        var solid = reloadedShape.Fill.Should().BeOfType<ShapeFill.Solid>(
            "the gallery style's fillRef must materialize a fill even though spPr has none").Subject;
        solid.Color.Resolved.Should().Be(PresentationColorScheme.CreateDefault()[ThemeColorSlot.Accent1],
            "the phClr placeholder in fillStyleLst idx=2 must be substituted with the fillRef's accent1 color");
    }

    // ── 2. effectRef resolves a shape with no explicit effects ─────────────────

    [Fact]
    public void ReadSp_ResolvesEffectRef_WhenShapeHasNoExplicitEffects()
    {
        var shape = new SlideShape { Id = 1, Name = "Shadowed Rect" };
        using var stream = BuildBasePackage(shape);
        InjectStyleAndFormatScheme(stream, "Shadowed Rect",
            styleInnerXml: "<a:effectRef idx=\"2\"><a:schemeClr val=\"accent1\"/></a:effectRef>",
            effectStyleLstXml: EffectStyleLstWithOuterShadowAtIdx2);

        var reloaded = PptxPackageReader.Read(stream);
        var reloadedShape = reloaded.Slides[0].Shapes.Single();

        reloadedShape.Effects.Should().NotBeNull(
            "the gallery style's effectRef must materialize the outer shadow even though spPr has no effectLst");
        reloadedShape.Effects!.HasOuterShadow.Should().BeTrue();
        reloadedShape.Effects.OuterShadowBlurRadEmu.Should().Be(40000);
        reloadedShape.Effects.OuterShadowDistEmu.Should().Be(20000);
        reloadedShape.Effects.OuterShadowDirDeg.Should().Be(90);
        reloadedShape.Effects.OuterShadowColor.Should().Be(SrgbColor.Black);
    }

    // ── 3. fillRef idx >= 1000 resolves against bgFillStyleLst (idx - 1000) ─────

    [Fact]
    public void ReadSp_ResolvesFillRef_AgainstBackgroundFillList_WhenIdxIsAtLeast1000()
    {
        var shape = new SlideShape { Id = 1, Name = "Bg Styled Rect" };
        using var stream = BuildBasePackage(shape);
        InjectStyleAndFormatScheme(stream, "Bg Styled Rect",
            styleInnerXml: "<a:fillRef idx=\"1002\"><a:schemeClr val=\"accent2\"/></a:fillRef>",
            bgFillStyleLstXml: BgFillStyleLstWithPhClrAtIdx2);

        var reloaded = PptxPackageReader.Read(stream);
        var reloadedShape = reloaded.Slides[0].Shapes.Single();

        var solid = reloadedShape.Fill.Should().BeOfType<ShapeFill.Solid>(
            "idx=1002 must resolve against bgFillStyleLst entry (1002 - 1000) = 2").Subject;
        solid.Color.Resolved.Should().Be(PresentationColorScheme.CreateDefault()[ThemeColorSlot.Accent2]);
    }

    // ── 4. Sibling no-regression: an explicit spPr fill is never overridden ─────

    [Fact]
    public void ReadSp_DoesNotOverrideFillRef_WhenShapeHasExplicitFill()
    {
        var shape = new SlideShape
        {
            Id = 1,
            Name = "Explicitly Filled Rect",
            Fill = new ShapeFill.Solid(new SrgbColor(0xFF, 0x00, 0x00)),
        };
        using var stream = BuildBasePackage(shape);
        InjectStyleAndFormatScheme(stream, "Explicitly Filled Rect",
            styleInnerXml: "<a:fillRef idx=\"2\"><a:schemeClr val=\"accent1\"/></a:fillRef>",
            fillStyleLstXml: FillStyleLstWithPhClrAtIdx2);

        var reloaded = PptxPackageReader.Read(stream);
        var reloadedShape = reloaded.Slides[0].Shapes.Single();

        var solid = reloadedShape.Fill.Should().BeOfType<ShapeFill.Solid>().Subject;
        solid.Color.Resolved.Should().Be(new SrgbColor(0xFF, 0x00, 0x00),
            "an authored spPr fill must win over the gallery style's fillRef");
    }

    // ── 5. Family sibling: connectors carry the same p:style element ───────────

    [Fact]
    public void ReadCxnSp_ResolvesFillRef_WhenConnectorHasNoExplicitFill()
    {
        var connector = new SlideShape
        {
            Id = 1,
            Name = "Styled Connector",
            Kind = SlideShapeKind.Connector,
        };
        using var stream = BuildBasePackage(connector);
        InjectStyleAndFormatScheme(stream, "Styled Connector",
            styleInnerXml: "<a:fillRef idx=\"2\"><a:schemeClr val=\"accent1\"/></a:fillRef>",
            fillStyleLstXml: FillStyleLstWithPhClrAtIdx2);

        var reloaded = PptxPackageReader.Read(stream);
        var reloadedShape = reloaded.Slides[0].Shapes.Single();

        reloadedShape.Kind.Should().Be(SlideShapeKind.Connector);
        var solid = reloadedShape.Fill.Should().BeOfType<ShapeFill.Solid>(
            "p:cxnSp carries the same p:style element as p:sp (CT_Connector shares CT_ShapeStyle)").Subject;
        solid.Color.Resolved.Should().Be(PresentationColorScheme.CreateDefault()[ThemeColorSlot.Accent1]);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────

    private static MemoryStream BuildBasePackage(SlideShape shape)
    {
        var presentation = new Presentation { SlideSizeCxEmu = 9144000, SlideSizeCyEmu = 6858000 };
        var slide = new Slide { Id = "rId1" };
        slide.Shapes.Add(shape);
        presentation.Slides.Add(slide);

        var stream = new MemoryStream();
        PptxPackageWriter.Write(presentation, stream);
        return stream;
    }

    /// <summary>
    /// Replaces theme1.xml's real <c>a:fmtScheme</c> child list(s) with the given inner XML (when
    /// supplied), and injects a hand-authored <c>&lt;p:style&gt;</c> element -- which
    /// <see cref="PptxPackageWriter"/> never emits -- into the sole slide's shape named
    /// <paramref name="shapeName"/> (a <c>p:sp</c> or <c>p:cxnSp</c>), positioned right after its
    /// <c>spPr</c> to match the declared CT_Shape / CT_Connector element order.
    /// </summary>
    private static void InjectStyleAndFormatScheme(
        MemoryStream stream,
        string shapeName,
        string styleInnerXml,
        string? fillStyleLstXml = null,
        string? bgFillStyleLstXml = null,
        string? effectStyleLstXml = null)
    {
        stream.Position = 0;
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true))
        {
            if (fillStyleLstXml is not null || bgFillStyleLstXml is not null || effectStyleLstXml is not null)
            {
                var themeEntry = archive.Entries.Single(e =>
                    e.FullName.Equals("ppt/theme/theme1.xml", StringComparison.OrdinalIgnoreCase));
                XDocument themeDoc;
                using (var input = themeEntry.Open())
                    themeDoc = XDocument.Load(input);

                var fmtScheme = themeDoc.Descendants(A + "fmtScheme").Single();
                if (fillStyleLstXml is not null)
                    fmtScheme.Element(A + "fillStyleLst")!.ReplaceNodes(XElement.Parse(fillStyleLstXml).Elements());
                if (bgFillStyleLstXml is not null)
                    fmtScheme.Element(A + "bgFillStyleLst")!.ReplaceNodes(XElement.Parse(bgFillStyleLstXml).Elements());
                if (effectStyleLstXml is not null)
                    fmtScheme.Element(A + "effectStyleLst")!.ReplaceNodes(XElement.Parse(effectStyleLstXml).Elements());

                themeEntry.Delete();
                var themeReplacement = archive.CreateEntry(themeEntry.FullName);
                using var themeOutput = themeReplacement.Open();
                themeDoc.Save(themeOutput, SaveOptions.DisableFormatting);
            }

            var slideEntry = archive.Entries.Single(e =>
                e.FullName.StartsWith("ppt/slides/slide", StringComparison.OrdinalIgnoreCase)
                && e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase));
            XDocument slideDoc;
            using (var input = slideEntry.Open())
                slideDoc = XDocument.Load(input);

            var targetShape = slideDoc.Descendants()
                .Where(e => e.Name == P + "sp" || e.Name == P + "cxnSp")
                .Single(e => (string?)e.Descendants().FirstOrDefault(n => n.Name.LocalName == "cNvPr")
                    ?.Attribute("name") == shapeName);

            var spPr = targetShape.Elements().Single(e => e.Name.LocalName == "spPr");
            var styleEl = XElement.Parse(
                $"<p:style xmlns:p=\"{P.NamespaceName}\" xmlns:a=\"{A.NamespaceName}\">{styleInnerXml}</p:style>");
            spPr.AddAfterSelf(styleEl);

            slideEntry.Delete();
            var slideReplacement = archive.CreateEntry(slideEntry.FullName);
            using var slideOutput = slideReplacement.Open();
            slideDoc.Save(slideOutput, SaveOptions.DisableFormatting);
        }

        stream.Position = 0;
    }
}
