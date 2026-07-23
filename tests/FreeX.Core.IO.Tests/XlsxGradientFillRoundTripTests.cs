using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Round-trip tests for cell gradient fills (linear and path).
/// Because ClosedXML does not model gradientFill, we inject gradient XML directly
/// into the styles.xml of a generated XLSX package, then load it through FreeX and
/// verify that the parsed <see cref="CellGradientFill"/> has the correct values.
/// We also verify that a re-save preserves the gradient (via XlsxStylesheetMetadataPreserver).
/// </summary>
public sealed class XlsxGradientFillRoundTripTests
{
    private static readonly XNamespace MainNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    private sealed record GradientSpec(
        string Type, double Degree,
        double Left, double Right, double Top, double Bottom,
        IReadOnlyList<(double pos, string rgb)> Stops);

    // Convenience: build CellColor from hex literals without needing explicit cast.
    private static CellColor CC(int r, int g, int b) => new((byte)r, (byte)g, (byte)b);

    // ----------------------------------------------------------------
    // Builder: save a trivial skeleton XLSX, then copy it while patching
    // styles.xml to inject gradient fill entries and update cellXf fillIds.
    // ----------------------------------------------------------------
    private static MemoryStream BuildXlsxWithGradients(params GradientSpec[] specs)
    {
        // 1. Save a skeleton workbook with one placeholder solid-fill style per spec.
        // Use distinct fill colors so ClosedXML doesn't merge them into the same fillId.
        var workbook = new Workbook("GradientTest");
        var sheet = workbook.AddSheet("Sheet1");
        for (int row = 1; row <= specs.Length; row++)
        {
            // Each row gets a distinct fill color so ClosedXML emits a separate cellXf.
            byte shade = (byte)(200 - row * 5);
            var style = new CellStyle
            {
                FillColor = new CellColor(shade, shade, shade),
                FillPatternStyle = CellFillPatternStyle.Solid,
            };
            var id = workbook.RegisterStyle(style);
            sheet.SetStyleOnly((uint)row, 1u, id);
        }

        using var skeleton = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, skeleton);
        skeleton.Position = 0;

        // 2. Copy the archive entry-by-entry, patching xl/styles.xml on the way.
        var patched = new MemoryStream();
        using (var src = new ZipArchive(skeleton, ZipArchiveMode.Read, leaveOpen: false))
        using (var dst = new ZipArchive(patched, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var entry in src.Entries)
            {
                var dstEntry = dst.CreateEntry(entry.FullName, CompressionLevel.Optimal);
                using var inStream  = entry.Open();
                using var outStream = dstEntry.Open();

                if (string.Equals(entry.FullName, "xl/styles.xml", StringComparison.OrdinalIgnoreCase))
                {
                    var doc = XDocument.Load(inStream);
                    PatchStylesWithGradients(doc, specs);
                    doc.Save(outStream);
                }
                else
                {
                    inStream.CopyTo(outStream);
                }
            }
        }

        patched.Position = 0;
        return patched;
    }

    private static void PatchStylesWithGradients(XDocument stylesDoc, GradientSpec[] specs)
    {
        var root = stylesDoc.Root!;
        var fillsEl = root.Element(MainNs + "fills")!;
        int existingCount = fillsEl.Elements(MainNs + "fill").Count();

        // Append one <fill><gradientFill .../></fill> per spec.
        for (int i = 0; i < specs.Length; i++)
        {
            var sp = specs[i];
            fillsEl.Add(new XElement(MainNs + "fill",
                BuildGradientFillElement(sp.Type, sp.Degree, sp.Left, sp.Right, sp.Top, sp.Bottom, sp.Stops)));
        }
        fillsEl.SetAttributeValue("count", existingCount + specs.Length);

        // Update cellXfs so each registered style xf points to a gradient fill.
        // xfList[0] is always the default style; our registered ones start at index 1.
        var xfList = root.Element(MainNs + "cellXfs")!
            .Elements(MainNs + "xf").ToList();
        for (int i = 0; i < specs.Length && i + 1 < xfList.Count; i++)
        {
            xfList[i + 1].SetAttributeValue("fillId", existingCount + i);
            xfList[i + 1].SetAttributeValue("applyFill", "1");
        }
    }

    private static XElement BuildGradientFillElement(
        string type, double degree,
        double left, double right, double top, double bottom,
        IReadOnlyList<(double pos, string rgb)> stops)
    {
        var gradEl = new XElement(MainNs + "gradientFill");
        gradEl.SetAttributeValue("type", type);

        if (string.Equals(type, "linear", StringComparison.OrdinalIgnoreCase))
        {
            gradEl.SetAttributeValue("degree", degree.ToString("G"));
        }
        else
        {
            // path — only emit inset attrs when non-zero
            if (left   != 0) gradEl.SetAttributeValue("left",   left.ToString("G"));
            if (right  != 0) gradEl.SetAttributeValue("right",  right.ToString("G"));
            if (top    != 0) gradEl.SetAttributeValue("top",    top.ToString("G"));
            if (bottom != 0) gradEl.SetAttributeValue("bottom", bottom.ToString("G"));
        }

        foreach (var (pos, rgb) in stops)
        {
            gradEl.Add(new XElement(
                MainNs + "stop",
                new XAttribute("position", pos.ToString("G")),
                new XElement(MainNs + "color", new XAttribute("rgb", rgb))));
        }
        return gradEl;
    }

    // ================================================================
    // Tests
    // ================================================================

    [Fact]
    public void XlsxAdapter_LinearGradientFill_RoundTrips_TypeAndDegree()
    {
        var spec = new GradientSpec("linear", 90, 0, 0, 0, 0,
            [(0.0, "FF0070C0"), (1.0, "FFFFFFFF")]);
        using var stream = BuildXlsxWithGradients(spec);

        var wb = new XlsxFileAdapter().Load(stream);
        var style = wb.GetStyle(wb.GetSheetAt(0)!.GetStyleOnly(1u, 1u)!.Value);

        style.GradientFill.Should().NotBeNull("linear gradient must be parsed");
        var gf = style.GradientFill!;
        gf.Type.Should().Be(CellGradientFillType.Linear);
        gf.Degree.Should().BeApproximately(90.0, 0.001);
        gf.Stops.Should().HaveCount(2);
        gf.Stops[0].Position.Should().BeApproximately(0.0, 0.001);
        gf.Stops[0].Color.Should().Be(CC(0x00, 0x70, 0xC0));
        gf.Stops[1].Position.Should().BeApproximately(1.0, 0.001);
        gf.Stops[1].Color.Should().Be(CC(0xFF, 0xFF, 0xFF));
    }

    [Fact]
    public void XlsxAdapter_PathGradientFill_RoundTrips_InsetAndStops()
    {
        var spec = new GradientSpec("path", 0, 0.3, 0.3, 0.3, 0.3,
            [(0.0, "FFFF0000"), (1.0, "FFFFFFFF")]);
        using var stream = BuildXlsxWithGradients(spec);

        var wb = new XlsxFileAdapter().Load(stream);
        var style = wb.GetStyle(wb.GetSheetAt(0)!.GetStyleOnly(1u, 1u)!.Value);

        style.GradientFill.Should().NotBeNull("path gradient must be parsed");
        var gf = style.GradientFill!;
        gf.Type.Should().Be(CellGradientFillType.Path);
        gf.Left.Should().BeApproximately(0.3, 0.001);
        gf.Right.Should().BeApproximately(0.3, 0.001);
        gf.Top.Should().BeApproximately(0.3, 0.001);
        gf.Bottom.Should().BeApproximately(0.3, 0.001);
        gf.Stops.Should().HaveCount(2);
        gf.Stops[0].Color.Should().Be(CC(0xFF, 0x00, 0x00));
        gf.Stops[1].Color.Should().Be(CC(0xFF, 0xFF, 0xFF));
    }

    [Fact]
    public void XlsxAdapter_MultipleGradients_AllParsedCorrectly()
    {
        var spec0 = new GradientSpec("linear", 0, 0, 0, 0, 0,
            [(0.0, "FFFF0000"), (1.0, "FF0000FF")]);
        var spec1 = new GradientSpec("linear", 270, 0, 0, 0, 0,
            [(0.0, "FF00FF00"), (0.5, "FFFFFF00"), (1.0, "FF00FF00")]);

        using var stream = BuildXlsxWithGradients(spec0, spec1);

        var wb = new XlsxFileAdapter().Load(stream);
        var sheet = wb.GetSheetAt(0)!;

        var style0 = wb.GetStyle(sheet.GetStyleOnly(1u, 1u)!.Value);
        var style1 = wb.GetStyle(sheet.GetStyleOnly(2u, 1u)!.Value);

        style0.GradientFill.Should().NotBeNull();
        style0.GradientFill!.Degree.Should().BeApproximately(0.0, 0.001);
        style0.GradientFill!.Stops.Should().HaveCount(2);

        style1.GradientFill.Should().NotBeNull();
        style1.GradientFill!.Degree.Should().BeApproximately(270.0, 0.001);
        style1.GradientFill!.Stops.Should().HaveCount(3);
    }

    [Fact]
    public void XlsxAdapter_GradientFill_SaveAndReload_Preserves()
    {
        var spec = new GradientSpec("linear", 45, 0, 0, 0, 0,
            [(0.0, "FFFF7F00"), (1.0, "FFFFFFFF")]);
        using var initial = BuildXlsxWithGradients(spec);

        var wb1 = new XlsxFileAdapter().Load(initial);

        using var saved = new MemoryStream();
        new XlsxFileAdapter().Save(wb1, saved);
        saved.Position = 0;

        var wb2 = new XlsxFileAdapter().Load(saved);
        var style = wb2.GetStyle(wb2.GetSheetAt(0)!.GetStyleOnly(1u, 1u)!.Value);

        style.GradientFill.Should().NotBeNull("gradient must survive save+reload");
        style.GradientFill!.Type.Should().Be(CellGradientFillType.Linear);
        style.GradientFill!.Degree.Should().BeApproximately(45.0, 0.001);
        style.GradientFill!.Stops.Should().HaveCount(2);
        style.GradientFill!.Stops[0].Color.Should().Be(CC(0xFF, 0x7F, 0x00));
    }

    [Fact]
    public void XlsxAdapter_GradientFill_ClearsFillColorAndPattern()
    {
        var spec = new GradientSpec("linear", 0, 0, 0, 0, 0,
            [(0.0, "FF112233"), (1.0, "FF445566")]);
        using var stream = BuildXlsxWithGradients(spec);

        var wb = new XlsxFileAdapter().Load(stream);
        var style = wb.GetStyle(wb.GetSheetAt(0)!.GetStyleOnly(1u, 1u)!.Value);

        style.GradientFill.Should().NotBeNull();
        style.FillColor.Should().BeNull("gradient fill must clear solid FillColor");
        style.FillPatternStyle.Should().Be(CellFillPatternStyle.None,
            "gradient fill must clear FillPatternStyle");
    }

    [Fact]
    public void XlsxAdapter_TwoDistinctGradientsSharingFirstStop_SaveAndReload_BothStayDistinct()
    {
        // R75-io-styles-fonts-4-1: A1 and B1 have DIFFERENT gradients that happen to share the same
        // first stop (white). ApplyStyle previously stamped both placeholders as the identical solid
        // white fill, so ClosedXML's style cache deduped them into ONE rebuilt <fill> and the merge
        // could only restore one gradient — the other silently inherited it. The fix perturbs each
        // placeholder with a hash of the gradient's FULL content, so the two placeholders differ and
        // both gradients survive a full rebuild save independently.
        var specA = new GradientSpec("linear", 90, 0, 0, 0, 0,
            [(0.0, "FFFFFFFF"), (1.0, "FF0000FF")]); // white -> blue
        var specB = new GradientSpec("linear", 90, 0, 0, 0, 0,
            [(0.0, "FFFFFFFF"), (1.0, "FFFF0000")]); // white -> red (same first stop as specA)

        using var initial = BuildXlsxWithGradients(specA, specB);
        var wb1 = new XlsxFileAdapter().Load(initial);

        using var saved = new MemoryStream();
        new XlsxFileAdapter().Save(wb1, saved);
        saved.Position = 0;

        var wb2 = new XlsxFileAdapter().Load(saved);
        var sheet = wb2.GetSheetAt(0)!;

        var styleA = wb2.GetStyle(sheet.GetStyleOnly(1u, 1u)!.Value);
        var styleB = wb2.GetStyle(sheet.GetStyleOnly(2u, 1u)!.Value);

        styleA.GradientFill.Should().NotBeNull("A1's gradient must survive the full rebuild");
        styleB.GradientFill.Should().NotBeNull("B1's gradient must survive the full rebuild, independently of A1");
        styleA.GradientFill!.Stops.Should().HaveCount(2);
        styleB.GradientFill!.Stops.Should().HaveCount(2);

        styleA.GradientFill!.Stops[1].Color.Should().Be(CC(0x00, 0x00, 0xFF), "A1 must stay white->blue");
        styleB.GradientFill!.Stops[1].Color.Should().Be(CC(0xFF, 0x00, 0x00),
            "B1 must stay white->red, not collapse onto A1's blue via a shared rebuilt fill");
    }

    [Fact]
    public void XlsxAdapter_DegenerateGradient_OnlyOneStop_IsIgnored()
    {
        // A gradient with only one stop is degenerate — reader must drop it silently.
        // The style will still be registered (the placeholder solid fill exists), but
        // GradientFill must be null.
        var spec = new GradientSpec("linear", 90, 0, 0, 0, 0,
            [(0.0, "FFAABBCC")]);
        using var stream = BuildXlsxWithGradients(spec);

        var wb = new XlsxFileAdapter().Load(stream);
        var styleIdOpt = wb.GetSheetAt(0)!.GetStyleOnly(1u, 1u);
        // Cell may have a style or may have fallen back to default — either way, GradientFill is null.
        if (styleIdOpt.HasValue)
        {
            var style = wb.GetStyle(styleIdOpt.Value);
            style.GradientFill.Should().BeNull("degenerate 1-stop gradient should be dropped");
        }
        // else: no explicit style on cell — GradientFill is trivially absent; test passes.
    }
}
