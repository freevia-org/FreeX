using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

/// <summary>
/// R142-shared-theme-colors-F1: the ribbon Font/Fill Color pickers (both shells) baked a Theme
/// Colors gallery swatch to its resolved flat RGB, never attaching a
/// <see cref="WorkbookThemeColorReference"/> the way the Cell Styles gallery's Accent presets do
/// (<c>CellStyleDiffPlanner.AccentDepth</c>). A cell filled/colored from a theme swatch therefore
/// stayed frozen at the color the theme resolved to at pick-time instead of following a later
/// workbook theme change, unlike real Excel and unlike this app's own Cell Styles gallery.
/// <para>
/// Fixed by: (1) <see cref="CellColorSwatch"/> now carries the swatch's originating theme slot/tint
/// for an Accent1-6 column swatch (<see cref="CellColorPalettePlanner.BuildThemePalette"/>); (2)
/// new <see cref="WorkbookSession.SetSelectedRangeFontColor(CellColor,WorkbookThemeColorReference?)"/>/
/// <see cref="WorkbookSession.SetSelectedRangeFillColor(CellColor,WorkbookThemeColorReference?)"/>
/// overloads attach that reference to the style (mirroring <c>CellStyleDiffPlanner.AccentDepth</c>)
/// instead of only the flat RGB.
/// </para>
/// </summary>
public sealed class R142_ThemeColorTrackingTests
{
    [Fact]
    public void BuildThemePalette_AccentColumnSwatches_CarryTheirThemeSlotAndTint()
    {
        // Fails before the fix: every CellColorSwatch.ThemeColor was implicitly null (the record
        // had no such member at all), so no caller could ever recover which theme slot/tint a
        // Theme Colors swatch came from once it resolved to a flat CellColor.
        var theme = WorkbookTheme.Office;

        var columns = CellColorPalettePlanner.BuildThemePalette(theme);
        var accent2Column = columns.Single(c => c.Name == "Accent 2");
        // Row 0 is the base Accent 2 color (tint 0); row 4 is "Darker 25%" (tint -0.25) per
        // CellColorPalettePlanner.ThemeAccentShadeTints.
        var baseSwatch = accent2Column.Shades[0];
        var darker25Swatch = accent2Column.Shades[4];

        baseSwatch.ThemeColor.Should().Be(new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent2, 0d));
        baseSwatch.Color.Should().Be(theme.ResolveColor(WorkbookThemeColorSlot.Accent2, 0d));
        darker25Swatch.ThemeColor.Should().Be(new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent2, -0.25d));
        darker25Swatch.Color.Should().Be(theme.ResolveColor(WorkbookThemeColorSlot.Accent2, -0.25d));
    }

    [Fact]
    public void BuildStandardSwatches_NeverCarryAThemeColor()
    {
        // No-regression sibling: Standard/Custom-spectrum swatches are plain colors with no theme
        // identity -- a caller applying one must never accidentally attach a theme link.
        CellColorPalettePlanner.BuildStandardSwatches()
            .Should().OnlyContain(swatch => swatch.ThemeColor == null);
        CellColorPalettePlanner.BuildCustomSpectrumSwatches()
            .Should().OnlyContain(swatch => swatch.ThemeColor == null);
    }

    [Fact]
    public void SetSelectedRangeFillColor_WithThemeColor_TracksTheWorkbookThemeAcrossAChange()
    {
        // Fails before the fix: SetSelectedRangeFillColor had no themeColor overload at all, so a
        // ribbon Fill Color pick from the Theme Colors gallery always applied StyleDiff(FillColor:
        // flatResolvedRgb) -- a cell.StyleId whose FillThemeColor stays null forever, unable to
        // follow a later WorkbookTheme change.
        var (session, workbook, sheet) = CreateSessionWithThemedWorkbook();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        session.SelectCell(a1);

        var result = session.SetSelectedRangeFillColor(
            fillColor: workbook.Theme.ResolveColor(WorkbookThemeColorSlot.Accent2, -0.25d),
            themeColor: new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent2, -0.25d));

        result.Success.Should().BeTrue(result.ErrorMessage);
        var style = workbook.GetStyle(sheet.GetCell(a1)!.StyleId);
        style.FillColor.Should().BeNull("a theme-linked fill must not also carry a competing flat FillColor");
        style.FillThemeColor.Should().Be(new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent2, -0.25d));
        style.ResolveFillColor(workbook.Theme).Should().Be(workbook.Theme.ResolveColor(WorkbookThemeColorSlot.Accent2, -0.25d));

        // The whole point: changing the workbook theme re-tints the cell with no further edit,
        // exactly like the Cell Styles gallery's Accent presets already do (R33-commands-
        // cellstyles-themes-1).
        workbook.Theme = workbook.Theme.WithColor(WorkbookThemeColorSlot.Accent2, new CellColor(9, 99, 199));
        style.ResolveFillColor(workbook.Theme).Should().Be(workbook.Theme.ResolveColor(WorkbookThemeColorSlot.Accent2, -0.25d));
    }

    [Fact]
    public void SetSelectedRangeFontColor_WithThemeColor_TracksTheWorkbookThemeAcrossAChange()
    {
        var (session, workbook, sheet) = CreateSessionWithThemedWorkbook();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        session.SelectCell(a1);

        var result = session.SetSelectedRangeFontColor(
            fontColor: workbook.Theme.ResolveColor(WorkbookThemeColorSlot.Accent3, 0d),
            themeColor: new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent3, 0d));

        result.Success.Should().BeTrue(result.ErrorMessage);
        var style = workbook.GetStyle(sheet.GetCell(a1)!.StyleId);
        style.FontThemeColor.Should().Be(new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent3, 0d));

        workbook.Theme = workbook.Theme.WithColor(WorkbookThemeColorSlot.Accent3, new CellColor(1, 2, 3));
        style.ResolveFontColor(workbook.Theme).Should().Be(new CellColor(1, 2, 3));
    }

    [Fact]
    public void SetSelectedRangeFillColor_NullThemeColor_NoRegressionBehavesExactlyLikeTheFlatColorOverload()
    {
        // No-regression sibling: a Standard/Recent/Custom color pick (themeColor: null, the
        // overload's default) must apply a plain flat fill exactly as the pre-existing single-arg
        // overload always has -- no theme link attached, and it must NOT track a later theme change.
        var (session, workbook, sheet) = CreateSessionWithThemedWorkbook();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        session.SelectCell(a1);
        var flatColor = new CellColor(11, 22, 33);

        var result = session.SetSelectedRangeFillColor(flatColor, themeColor: null);

        result.Success.Should().BeTrue(result.ErrorMessage);
        var style = workbook.GetStyle(sheet.GetCell(a1)!.StyleId);
        style.FillColor.Should().Be(flatColor);
        style.FillThemeColor.Should().BeNull();

        workbook.Theme = workbook.Theme.WithColor(WorkbookThemeColorSlot.Accent2, new CellColor(255, 0, 0));
        style.ResolveFillColor(workbook.Theme).Should().Be(flatColor, "a flat (non-theme) fill must never drift when the theme changes");
    }

    private static (WorkbookSession Session, Workbook Workbook, Sheet Sheet) CreateSessionWithThemedWorkbook()
    {
        var workbook = new Workbook("Book");
        workbook.Theme = WorkbookTheme.Office;
        var sheet = workbook.AddSheet("Sheet1");
        workbook.ActiveSheetIndex = 0;
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("themed"));
        var session = new WorkbookSessionFactory().Create(
            new StartupWorkbookLoadResult(workbook, "Book.fxl", "Opened .fxl.", IsFallback: false),
            viewportHeight: 240,
            viewportWidth: 320);
        return (session, workbook, sheet);
    }
}
