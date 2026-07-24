using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R82-meta-1 / R82-services-autosave-recovery-5-2 regression coverage: two fields the native
/// .fxl JSON format silently dropped on a save-then-load round trip through
/// <see cref="NativeJsonAdapter"/>.
///
/// (1) <see cref="CellBorder.ThemeColor"/> (r80's border theme-color reference, see
/// <c>R80_BorderThemeColorTests</c> for the XLSX-side coverage) had no consumer at all in
/// <c>CellBorderDto</c>/<c>ToCellBorder</c>/<c>FromCellBorder</c>, so every .fxl save flattened a
/// themed border to a baked RGB literal.
///
/// (2) <see cref="Workbook.HasVbaProjectPackage"/> had no field on <c>WorkbookDto</c> at all, so a
/// macro-enabled workbook that round-tripped through a .fxl save (e.g. AutosaveService's periodic
/// recovery snapshot, which serializes exclusively via <see cref="NativeJsonAdapter"/>) always
/// reloaded with the flag reset to false.
/// </summary>
public sealed class R82_NativeJsonBorderThemeColorAndVbaProjectRoundTripTests
{
    private const double Tint = 0.4;

    [Fact]
    public void SaveThenLoad_PreservesCellBorderThemeColor()
    {
        var workbook = new Workbook("ThemeBorderNativeRoundTrip");
        var sheet = workbook.AddSheet("Sheet1");
        var address = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(address, new TextValue("Hello"));
        var styleId = workbook.RegisterStyle(new CellStyle
        {
            BorderTop = new CellBorder(
                BorderStyle.Thin,
                workbook.Theme.ResolveColor(WorkbookThemeColorSlot.Accent1, Tint),
                new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent1, Tint)),
        });
        sheet.GetCell(1, 1)!.StyleId = styleId;

        using var stream = new MemoryStream();
        var adapter = new NativeJsonAdapter();
        adapter.Save(workbook, stream);

        stream.Position = 0;
        var reloaded = adapter.Load(stream);
        var reloadedSheet = reloaded.GetSheetAt(0);
        var reloadedCell = reloadedSheet!.GetCell(1, 1);
        var reloadedStyle = reloaded.GetStyle(reloadedCell!.StyleId);

        // Bug case: the theme link must survive a .fxl round trip, not be baked to a flat RGB.
        reloadedStyle.BorderTop.ThemeColor.Should().Be(
            new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent1, Tint),
            "a theme-linked border color must survive a native .fxl save/reload round trip so it " +
            "re-colors correctly if the workbook theme later changes");
        reloadedStyle.BorderTop.Color.Should().Be(workbook.Theme.ResolveColor(WorkbookThemeColorSlot.Accent1, Tint));
    }

    [Fact]
    public void SaveThenLoad_PlainRgbBorder_LeavesThemeColorNull_NoRegression()
    {
        var workbook = new Workbook("PlainRgbBorderNativeRoundTrip");
        var sheet = workbook.AddSheet("Sheet1");
        var address = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(address, new TextValue("Hello"));
        var styleId = workbook.RegisterStyle(new CellStyle
        {
            BorderTop = new CellBorder(BorderStyle.Thin, new CellColor(91, 155, 213)),
        });
        sheet.GetCell(1, 1)!.StyleId = styleId;

        using var stream = new MemoryStream();
        var adapter = new NativeJsonAdapter();
        adapter.Save(workbook, stream);

        stream.Position = 0;
        var reloaded = adapter.Load(stream);
        var reloadedSheet = reloaded.GetSheetAt(0);
        var reloadedCell = reloadedSheet!.GetCell(1, 1);
        var reloadedStyle = reloaded.GetStyle(reloadedCell!.StyleId);

        reloadedStyle.BorderTop.ThemeColor.Should().BeNull(
            "a concrete (non-theme) RGB border color must not fabricate a theme link");
        reloadedStyle.BorderTop.Color.Should().Be(new CellColor(91, 155, 213));
    }

    [Fact]
    public void SaveThenLoad_PreservesHasVbaProjectPackage()
    {
        var workbook = new Workbook("MacroWorkbookNativeRoundTrip")
        {
            HasVbaProjectPackage = true
        };
        workbook.AddSheet("Sheet1");

        using var stream = new MemoryStream();
        var adapter = new NativeJsonAdapter();
        adapter.Save(workbook, stream);

        stream.Position = 0;
        var reloaded = adapter.Load(stream);

        // Bug case: a macro-enabled workbook must still report itself as macro-enabled after a
        // .fxl round trip (e.g. an autosave crash-recovery snapshot), not silently reset to false.
        reloaded.HasVbaProjectPackage.Should().BeTrue();
    }

    [Fact]
    public void SaveThenLoad_LeavesHasVbaProjectPackageFalse_ForPlainWorkbook_NoRegression()
    {
        var workbook = new Workbook("PlainWorkbookNativeRoundTrip");
        workbook.AddSheet("Sheet1");

        using var stream = new MemoryStream();
        var adapter = new NativeJsonAdapter();
        adapter.Save(workbook, stream);

        stream.Position = 0;
        var reloaded = adapter.Load(stream);

        reloaded.HasVbaProjectPackage.Should().BeFalse();
    }
}
