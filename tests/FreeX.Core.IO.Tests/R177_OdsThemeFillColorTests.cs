using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// freex-theme-fill-color-F1: <c>OdsStyleRegistry.BuildCellStyle</c> gated the exported
/// <c>fo:background-color</c> on <c>style.FillColor</c> while the font color beside it already went
/// through <c>ResolveFontColor(_workbook.Theme)</c>. That is not merely a stale-color bug:
/// <c>StyleDiff.Apply</c> sets <see cref="CellStyle.FillThemeColor"/> WITHOUT baking
/// <see cref="CellStyle.FillColor"/> (see <c>CellStyle.Apply</c>), which is exactly what the ribbon's
/// Theme Colors fill picker produces — so for those cells <c>FillColor</c> is null and the fill was
/// dropped from the exported .ods entirely.
/// </summary>
public sealed class R177_OdsThemeFillColorTests
{
    // Deliberately unlike the stock Office Accent1 (21, 96, 130) so "followed the theme" and "kept a
    // load-time baked color" can never be confused for one another.
    private static readonly CellColor SwappedAccent1 = new(7, 200, 111);

    [Fact]
    public void ThemeOnlyFill_IsExported_NotDroppedEntirely()
    {
        // The ribbon Theme Colors picker path: FillThemeColor set, FillColor left null.
        var style = new CellStyle { FillThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent1) };
        style.FillColor.Should().BeNull(
            "this test only proves anything if the theme-only fill really has no baked FillColor");

        var workbook = WorkbookWithStyle(style);
        var expected = style.ResolveFillColor(workbook.Theme);
        expected.Should().NotBeNull();

        var reloaded = StyleOfA1(RoundTrip(workbook));

        reloaded.FillColor.Should().Be(expected,
            "a theme-backed fill must be flattened into the .ods, not omitted because no RGB was baked");
    }

    [Fact]
    public void ThemeBackedFill_FollowsASwappedTheme()
    {
        var style = new CellStyle { FillThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent1) };
        var workbook = WorkbookWithStyle(style);

        var before = style.ResolveFillColor(workbook.Theme);
        workbook.Theme = workbook.Theme.WithColor(WorkbookThemeColorSlot.Accent1, SwappedAccent1);
        var expected = style.ResolveFillColor(workbook.Theme);
        expected.Should().NotBe(before, "the swapped accent must actually change the resolved fill");

        var reloaded = StyleOfA1(RoundTrip(workbook));

        reloaded.FillColor.Should().Be(expected,
            "the exported .ods must carry the color the cell currently displays");
        reloaded.FillColor.Should().NotBe(before);
    }

    [Fact]
    public void ExplicitRgbFill_KeepsItsOwnColorAcrossThemeChange()
    {
        // No-regression sibling: an explicitly filled cell is pinned to its authored RGB.
        var explicitFill = new CellColor(200, 0, 0);
        var workbook = WorkbookWithStyle(new CellStyle { FillColor = explicitFill });

        workbook.Theme = workbook.Theme.WithColor(WorkbookThemeColorSlot.Accent1, SwappedAccent1);

        StyleOfA1(RoundTrip(workbook)).FillColor.Should().Be(explicitFill);
    }

    private static Workbook WorkbookWithStyle(CellStyle style)
    {
        var workbook = new Workbook("Untitled");
        var sheet = workbook.AddSheet("Sheet1");
        var cell = Cell.FromValue(new TextValue("Filled"));
        cell.StyleId = workbook.RegisterStyle(style);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), cell);
        return workbook;
    }

    private static Workbook RoundTrip(Workbook workbook)
    {
        var adapter = new OdsFileAdapter();
        using var stream = new MemoryStream();
        adapter.Save(workbook, stream);
        stream.Position = 0;
        return adapter.Load(stream);
    }

    private static CellStyle StyleOfA1(Workbook workbook)
    {
        var sheet = workbook.Sheets.Single();
        var cell = sheet.GetCell(1, 1);
        var id = cell?.StyleId ?? sheet.GetStyleOnly(1, 1);
        return id is { } styleId ? workbook.GetStyle(styleId) : CellStyle.Default;
    }
}
