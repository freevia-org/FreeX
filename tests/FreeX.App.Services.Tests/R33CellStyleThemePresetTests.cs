using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

/// <summary>
/// Regression tests for R33-commands-cellstyles-themes-1 (themed accent tint presets must
/// carry a live theme reference so they recascade on a workbook theme change) and
/// R33-commands-cellstyles-themes-3 (the "Heading 1" preset must not bake an opaque fill).
/// </summary>
public sealed class R33CellStyleThemePresetTests
{
    [Fact]
    public void AccentTintPreset_AppliedThenThemeChanges_FillRecascadesToNewThemeColor()
    {
        var officeTheme = WorkbookTheme.Office.WithColor(WorkbookThemeColorSlot.Accent1, new CellColor(10, 20, 30));
        var facetTheme = WorkbookTheme.Office.WithColor(WorkbookThemeColorSlot.Accent1, new CellColor(200, 210, 220));

        // Applying "20% - Accent 1" bakes the diff into a cell's style, exactly as
        // ApplyStyleCommand does when a user picks the preset from the Cell Styles gallery.
        var diff = CellStyleDiffPlanner.GetCellStylePresetDiff(CellStylePreset.Accent1_20, officeTheme);
        var cellStyle = diff.ApplyTo(CellStyle.Default);

        // The diff must carry a theme reference, not a resolved literal, so the fill can
        // still be re-resolved after SetWorkbookThemeCommand swaps ctx.Workbook.Theme.
        cellStyle.FillThemeColor.Should().Be(new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent1, 0.8));
        cellStyle.FillColor.Should().BeNull();

        cellStyle.ResolveFillColor(officeTheme).Should().Be(officeTheme.ResolveColor(WorkbookThemeColorSlot.Accent1, 0.8));

        // Switching the workbook theme (without re-applying the style) must re-tint the cell,
        // matching Excel's live theme cascade for theme-linked cell styles.
        cellStyle.ResolveFillColor(facetTheme).Should().Be(facetTheme.ResolveColor(WorkbookThemeColorSlot.Accent1, 0.8));
        cellStyle.ResolveFillColor(facetTheme).Should().NotBe(cellStyle.ResolveFillColor(officeTheme));
    }

    [Fact]
    public void StatusPreset_StillBakesALiteralFillColor_UnaffectedByThemeChange()
    {
        // Sibling case: non-themed presets (e.g. "Good") intentionally bake a literal
        // CellColor and must keep doing so - they are not theme-linked in Excel either.
        var diff = CellStyleDiffPlanner.GetCellStylePresetDiff(CellStylePreset.Good);
        var cellStyle = diff.ApplyTo(CellStyle.Default);

        cellStyle.FillThemeColor.Should().BeNull();
        cellStyle.FillColor.Should().Be(new CellColor(198, 239, 206));

        var otherTheme = WorkbookTheme.Office.WithColor(WorkbookThemeColorSlot.Accent1, new CellColor(1, 2, 3));
        cellStyle.ResolveFillColor(otherTheme).Should().Be(new CellColor(198, 239, 206));
    }

    [Fact]
    public void Heading1Preset_AppliedOverAnAlreadyFilledCell_ClearsTheFillEntirely()
    {
        // A cell that already has an opaque fill (e.g. from a previous style or manual paint).
        var prefilled = new CellStyle
        {
            FillColor = new CellColor(31, 115, 70),
            FontColor = CellColor.White
        };

        var diff = CellStyleDiffPlanner.GetCellStylePresetDiff(CellStylePreset.Heading1);
        var result = diff.ApplyTo(prefilled);

        // Excel's "Heading 1" has no fill: applying it must remove any pre-existing fill
        // rather than leaving the old opaque color behind or introducing a new baked one.
        result.FillColor.Should().BeNull();
        result.FillThemeColor.Should().BeNull();
        result.Bold.Should().BeTrue();
        result.FontSize.Should().Be(15);
        result.BorderBottom.Style.Should().Be(BorderStyle.Medium);
    }
}
