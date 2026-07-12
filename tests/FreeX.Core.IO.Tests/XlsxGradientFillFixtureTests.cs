using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Loads the committed Excel-authored gradient-fill parity fixture
/// (<c>Fixtures/CellGradientFillLinear.xlsx</c>) and verifies that FreeX parses
/// the three gradient blocks (2-stop deg 0, 2-stop deg 90, 3-stop deg 0) with the
/// exact degrees, stop counts, and endpoint colors that Excel reads back.
/// This guards the binary fixture used for the WPF visual-render verification.
/// </summary>
public sealed class XlsxGradientFillFixtureTests
{
    private static Workbook LoadFixture()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "CellGradientFillLinear.xlsx");
        File.Exists(path).Should().BeTrue($"gradient fixture must be copied to output: {path}");
        using var stream = File.OpenRead(path);
        return new XlsxFileAdapter().Load(stream);
    }

    private static CellGradientFill GradientAt(Workbook wb, Sheet sheet, uint row, uint col)
    {
        var styleId = sheet.GetStyleOnly(row, col);
        styleId.Should().NotBeNull($"cell R{row}C{col} must carry a style");
        var style = wb.GetStyle(styleId!.Value);
        style.GradientFill.Should().NotBeNull($"cell R{row}C{col} must have a gradient fill");
        return style.GradientFill!;
    }

    [Fact]
    public void Fixture_TwoStopDegreeZero_BlueToOrange()
    {
        var wb = LoadFixture();
        var sheet = wb.GetSheetAt(0)!;

        // B2 = row 2, col 2
        var gf = GradientAt(wb, sheet, 2u, 2u);
        gf.Type.Should().Be(CellGradientFillType.Linear);
        gf.Degree.Should().BeApproximately(0.0, 0.001);
        gf.Stops.Should().HaveCount(2);
        gf.Stops[0].Position.Should().BeApproximately(0.0, 0.001);
        gf.Stops[0].Color.Should().Be(new CellColor(0, 70, 200));   // blue
        gf.Stops[1].Position.Should().BeApproximately(1.0, 0.001);
        gf.Stops[1].Color.Should().Be(new CellColor(255, 140, 0));  // orange
    }

    [Fact]
    public void Fixture_TwoStopDegreeNinety_GreenToWhite()
    {
        var wb = LoadFixture();
        var sheet = wb.GetSheetAt(0)!;

        // B7 = row 7, col 2
        var gf = GradientAt(wb, sheet, 7u, 2u);
        gf.Type.Should().Be(CellGradientFillType.Linear);
        gf.Degree.Should().BeApproximately(90.0, 0.001);
        gf.Stops.Should().HaveCount(2);
        gf.Stops[0].Color.Should().Be(new CellColor(0, 160, 0));     // green
        gf.Stops[1].Color.Should().Be(new CellColor(255, 255, 255)); // white
    }

    [Fact]
    public void Fixture_ThreeStopDegreeZero_RedYellowBlue()
    {
        var wb = LoadFixture();
        var sheet = wb.GetSheetAt(0)!;

        // B12 = row 12, col 2
        var gf = GradientAt(wb, sheet, 12u, 2u);
        gf.Type.Should().Be(CellGradientFillType.Linear);
        gf.Degree.Should().BeApproximately(0.0, 0.001);
        gf.Stops.Should().HaveCount(3);
        gf.Stops[0].Position.Should().BeApproximately(0.0, 0.001);
        gf.Stops[0].Color.Should().Be(new CellColor(220, 30, 30));   // red
        gf.Stops[1].Position.Should().BeApproximately(0.5, 0.001);
        gf.Stops[1].Color.Should().Be(new CellColor(255, 230, 0));   // yellow
        gf.Stops[2].Position.Should().BeApproximately(1.0, 0.001);
        gf.Stops[2].Color.Should().Be(new CellColor(30, 60, 220));   // blue
    }

    /// <summary>
    /// Loads the real Excel-authored fixture, forces a FULL ClosedXML rebuild (by registering and
    /// applying an unrelated new style), saves, and reloads — then asserts every gradient's full
    /// CONTENT (type, degree, stops), not just a surviving solid placeholder fill.
    ///
    /// Regression for the <see cref="XlsxStylesheetMetadataPreserver"/> gradient-restore bug: a full
    /// rebuild renumbers/reshapes styles.xml so completely that the old raw-attribute cellXf
    /// "signature" of a minimally-authored source xf (which is what real Excel files emit) never
    /// matched ClosedXML's fully-expanded rebuilt xf. The source-to-target lookup always missed, so
    /// every real-world gradient-only cell silently kept only its solid placeholder colour and lost
    /// its true gradient across a rebuild save/reload. The synthetic round-trip fixtures did not
    /// catch this because their spliced cellXfs already matched ClosedXML's emission conventions.
    /// </summary>
    [Fact]
    public void Fixture_AllGradients_SurviveFullRebuildSaveReload_WithContentNotJustPlaceholder()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "CellGradientFillLinear.xlsx");
        File.Exists(path).Should().BeTrue($"gradient fixture must be copied to output: {path}");

        var wb = new XlsxFileAdapter().Load(File.OpenRead(path));
        var sheet = wb.GetSheetAt(0)!;

        // Force a full rebuild (defeats both the source-copy fast path and the value-only patch-save
        // path) by applying a brand-new style to an unrelated, far-away cell.
        var boldId = wb.RegisterStyle(new CellStyle { Bold = true });
        sheet.SetStyleOnly(30u, 10u, boldId);

        using var saved = new MemoryStream();
        new XlsxFileAdapter().Save(wb, saved);
        saved.Position = 0;

        var reloaded = new XlsxFileAdapter().Load(saved);
        var reloadedSheet = reloaded.GetSheetAt(0)!;

        // B2 block: 2-stop degree 0, blue -> orange.
        var g1 = GradientAt(reloaded, reloadedSheet, 2u, 2u);
        g1.Type.Should().Be(CellGradientFillType.Linear);
        g1.Degree.Should().BeApproximately(0.0, 0.001);
        g1.Stops.Should().HaveCount(2, "the blue->orange gradient content, not just a placeholder, must survive");
        g1.Stops[0].Color.Should().Be(new CellColor(0, 70, 200));   // blue
        g1.Stops[1].Color.Should().Be(new CellColor(255, 140, 0));  // orange

        // B7 block: 2-stop degree 90, green -> white.
        var g2 = GradientAt(reloaded, reloadedSheet, 7u, 2u);
        g2.Type.Should().Be(CellGradientFillType.Linear);
        g2.Degree.Should().BeApproximately(90.0, 0.001);
        g2.Stops.Should().HaveCount(2);
        g2.Stops[0].Color.Should().Be(new CellColor(0, 160, 0));     // green
        g2.Stops[1].Color.Should().Be(new CellColor(255, 255, 255)); // white

        // B12 block: 3-stop degree 0, red -> yellow -> blue.
        var g3 = GradientAt(reloaded, reloadedSheet, 12u, 2u);
        g3.Type.Should().Be(CellGradientFillType.Linear);
        g3.Degree.Should().BeApproximately(0.0, 0.001);
        g3.Stops.Should().HaveCount(3);
        g3.Stops[0].Color.Should().Be(new CellColor(220, 30, 30));   // red
        g3.Stops[1].Color.Should().Be(new CellColor(255, 230, 0));   // yellow
        g3.Stops[2].Color.Should().Be(new CellColor(30, 60, 220));   // blue
    }
}
