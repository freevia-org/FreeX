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
}
