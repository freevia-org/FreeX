using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression coverage for R96-io-autofilter-top10-filterval-1: <see cref="TopBottomFilterCommand"/>
/// (live) correctly implements Excel's tie-inclusive Top-N/Bottom-N boundary semantics -- ties at the
/// Nth-best value are ALL kept visible -- but it never persisted the computed boundary into the
/// worksheet AutoFilter's <c>&lt;top10 filterVal=.../&gt;</c> attribute. On save+reload,
/// <c>XlsxWorksheetAutoFilterMaterializer.BuildTop10KeptRows</c> falls back to a naive
/// <c>OrderBy(-Value).ThenBy(Row).Take(N)</c> when that attribute is absent, which arbitrarily drops
/// tied rows past the Nth position -- silently re-hiding a row that was visible when the filter was
/// first applied, purely from reopening the file.
/// </summary>
public sealed class R96_TopBottomFilterValueRoundTripTests
{
    private sealed class TestCommandContext(Workbook workbook) : ICommandContext
    {
        public Workbook Workbook { get; } = workbook;

        public Sheet GetSheet(SheetId sheetId) =>
            Workbook.GetSheet(sheetId) ?? throw new KeyNotFoundException($"Sheet {sheetId} not found");
    }

    private static Workbook BuildTiedTop2Workbook(out GridRange range)
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");

        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Score"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(100));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(100));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new NumberValue(100));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 1), new NumberValue(50));

        range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 1));
        sheet.AutoFilter = new WorksheetAutoFilterModel(range.ToString(), null);

        // Apply through the REAL command entry point (matches the ribbon's "Top 10... > Top 2 Items"
        // action): TopBottomFilterCommand.SelectBestRows computes boundary=100 and keeps rows 2,3,4
        // (all three tied 100s) visible, hiding only row 5 (50) -- this is the live baseline a
        // save/reload round trip must reproduce.
        var ctx = new TestCommandContext(wb);
        var apply = new TopBottomFilterCommand(sheet.Id, range, filterColOffset: 0, count: 2, top: true);
        apply.Apply(ctx).Success.Should().BeTrue();

        sheet.FilterHiddenRows.Should().BeEquivalentTo([5u]);
        sheet.HiddenRows.Should().BeEmpty();

        return wb;
    }

    private static Workbook SaveAndReload(Workbook workbook)
    {
        var adapter = new XlsxFileAdapter();
        using var ms = new MemoryStream();
        adapter.Save(workbook, ms);
        ms.Position = 0;
        return adapter.Load(ms);
    }

    [Fact]
    public void TopBottomFilterCommand_Apply_PersistsBoundaryAsFilterValue()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");

        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Score"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(100));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(100));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new NumberValue(100));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 1), new NumberValue(50));

        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 1));
        sheet.AutoFilter = new WorksheetAutoFilterModel(range.ToString(), null);

        var ctx = new TestCommandContext(wb);
        var apply = new TopBottomFilterCommand(sheet.Id, range, filterColOffset: 0, count: 2, top: true);
        apply.Apply(ctx).Success.Should().BeTrue();

        var top10 = sheet.AutoFilter!.FilterColumns.Single().Top10;
        top10.Should().NotBeNull();
        // Before the fix this was always null -- the boundary Excel's tie-inclusive Top-N semantics
        // computed was discarded instead of being carried into the persisted criterion.
        top10!.FilterValue.Should().Be(100);
    }

    [Fact]
    public void TopBottomFilterCommand_SaveThenLoad_TiedBoundaryRowStaysVisible()
    {
        var wb = BuildTiedTop2Workbook(out _);

        var reloaded = SaveAndReload(wb);
        var reloadedSheet = reloaded.Sheets[0];

        // Only row 5 (value 50, below the boundary) should be filter-hidden after reload -- rows 2,3,4
        // (all tied at the boundary value 100) must all stay visible, exactly as the live apply left
        // them. Before the fix, the naive Take(2) fallback kept only rows 2 and 3 and newly hid row 4.
        reloadedSheet.FilterHiddenRows.Should().BeEquivalentTo([5u]);
        reloadedSheet.IsRowEffectivelyHidden(2).Should().BeFalse();
        reloadedSheet.IsRowEffectivelyHidden(3).Should().BeFalse();
        reloadedSheet.IsRowEffectivelyHidden(4).Should().BeFalse();
        reloadedSheet.IsRowEffectivelyHidden(5).Should().BeTrue();
    }

    [Fact]
    public void TopBottomFilterCommand_SaveThenLoad_NoRegression_NonTiedTopNStillRanksCorrectly()
    {
        // Sibling case (no ties at the boundary): the existing non-tied round-trip behaviour must be
        // unaffected by now also writing FilterValue.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");

        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Score"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(100));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(90));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new NumberValue(80));

        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 1));
        sheet.AutoFilter = new WorksheetAutoFilterModel(range.ToString(), null);

        var ctx = new TestCommandContext(wb);
        var apply = new TopBottomFilterCommand(sheet.Id, range, filterColOffset: 0, count: 2, top: true);
        apply.Apply(ctx).Success.Should().BeTrue();

        sheet.FilterHiddenRows.Should().BeEquivalentTo([4u]);

        var reloaded = SaveAndReload(wb);
        var reloadedSheet = reloaded.Sheets[0];

        reloadedSheet.FilterHiddenRows.Should().BeEquivalentTo([4u]);
        reloadedSheet.IsRowEffectivelyHidden(2).Should().BeFalse();
        reloadedSheet.IsRowEffectivelyHidden(3).Should().BeFalse();
        reloadedSheet.IsRowEffectivelyHidden(4).Should().BeTrue();

        var reloadedTop10 = reloadedSheet.AutoFilter!.FilterColumns.Single().Top10;
        reloadedTop10.Should().NotBeNull();
        reloadedTop10!.Value.Should().Be(2);
    }

    [Fact]
    public void BottomFilterCommand_SaveThenLoad_TiedBoundaryRowStaysVisible()
    {
        // Sibling path: Bottom-N (not just Top-N) must also persist and honor a tie-inclusive boundary.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");

        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Score"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(50));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(50));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new NumberValue(50));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 1), new NumberValue(100));

        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 1));
        sheet.AutoFilter = new WorksheetAutoFilterModel(range.ToString(), null);

        var ctx = new TestCommandContext(wb);
        var apply = new TopBottomFilterCommand(sheet.Id, range, filterColOffset: 0, count: 2, top: false);
        apply.Apply(ctx).Success.Should().BeTrue();

        sheet.FilterHiddenRows.Should().BeEquivalentTo([5u]);

        var top10 = sheet.AutoFilter!.FilterColumns.Single().Top10;
        top10!.FilterValue.Should().Be(50);

        var reloaded = SaveAndReload(wb);
        var reloadedSheet = reloaded.Sheets[0];

        reloadedSheet.FilterHiddenRows.Should().BeEquivalentTo([5u]);
        reloadedSheet.IsRowEffectivelyHidden(2).Should().BeFalse();
        reloadedSheet.IsRowEffectivelyHidden(3).Should().BeFalse();
        reloadedSheet.IsRowEffectivelyHidden(4).Should().BeFalse();
        reloadedSheet.IsRowEffectivelyHidden(5).Should().BeTrue();
    }
}
