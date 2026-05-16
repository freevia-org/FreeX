using FluentAssertions;
using Freexcel.Core.Model;

namespace Freexcel.Core.Model.Tests;

public sealed class WorkbookThemeTests
{
    [Fact]
    public void Workbook_UsesOfficeThemeByDefault()
    {
        var workbook = new Workbook();

        workbook.Theme.Name.Should().Be("Office");
        workbook.Theme.MajorFontName.Should().Be("Aptos Display");
        workbook.Theme.MinorFontName.Should().Be("Aptos");
        workbook.Theme.GetColor(WorkbookThemeColorSlot.Accent1).Should().Be(new CellColor(21, 96, 130));
        workbook.Theme.GetColor(WorkbookThemeColorSlot.Hyperlink).Should().Be(new CellColor(5, 99, 193));
    }

    [Fact]
    public void WorkbookTheme_WithColor_ReplacesOnlyRequestedSlot()
    {
        var theme = WorkbookTheme.Office.WithColor(
            WorkbookThemeColorSlot.Accent2,
            new CellColor(1, 2, 3));

        theme.GetColor(WorkbookThemeColorSlot.Accent2).Should().Be(new CellColor(1, 2, 3));
        theme.GetColor(WorkbookThemeColorSlot.Accent1).Should().Be(WorkbookTheme.Office.GetColor(WorkbookThemeColorSlot.Accent1));
    }

    [Theory]
    [InlineData(100, 150, 200, 0.0, 100, 150, 200)]
    [InlineData(100, 150, 200, 0.5, 178, 202, 228)]
    [InlineData(100, 150, 200, -0.25, 75, 112, 150)]
    [InlineData(100, 150, 200, 2.0, 255, 255, 255)]
    [InlineData(100, 150, 200, -2.0, 0, 0, 0)]
    public void WorkbookTheme_ResolveColor_AppliesExcelTint(
        byte r,
        byte g,
        byte b,
        double tint,
        byte expectedR,
        byte expectedG,
        byte expectedB)
    {
        var theme = WorkbookTheme.Office.WithColor(
            WorkbookThemeColorSlot.Accent1,
            new CellColor(r, g, b));

        theme.ResolveColor(WorkbookThemeColorSlot.Accent1, tint)
            .Should().Be(new CellColor(expectedR, expectedG, expectedB));
    }
}
