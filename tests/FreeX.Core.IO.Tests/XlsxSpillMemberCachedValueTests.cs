using System.IO;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R61-io-shared-formula-6-1: full-save must write a cached value for every non-anchor
/// dynamic-array spill member cell, not just the anchor. Excel always writes a cached &lt;v&gt;
/// for every spilled cell so it displays immediately on open, even without an intervening
/// recalculation. Before the fix, <see cref="Sheet.GetOccupiedCellMap"/> never visits spill
/// member cells (they live in the sheet's separate spill-value store, populated by
/// <see cref="Sheet.SetSpillRange"/>, which removes them from <c>_cells</c>), so the per-cell
/// write loop in XlsxFileAdapter.Save.cs skips them entirely, and the anchor's array-range write
/// carries only the anchor's own formula/value — the members silently round-tripped as Blank.
/// </summary>
public sealed class XlsxSpillMemberCachedValueTests
{
    private static Workbook CreateDynamicArraySpillWorkbook()
    {
        var workbook = new Workbook("SpillMemberCachedValue");
        var sheet = workbook.AddSheet("Data");

        var anchor = new CellAddress(sheet.Id, 3, 1);
        sheet.SetFormula(anchor, "SEQUENCE(1,3)");

        // Cached spill output 1,2,3 across A3:C3 — mirrors what RecalcEngine leaves behind after a
        // normal recalculation pass, matching real Excel's Manual-calc-mode on-disk state.
        var cells = new ScalarValue[1, 3]
        {
            { new NumberValue(1), new NumberValue(2), new NumberValue(3) }
        };
        sheet.GetCell(anchor)!.Value = new NumberValue(1);
        sheet.SetSpillRange(anchor, new RangeValue(cells, anchor.Row, anchor.Col));

        return workbook;
    }

    [Fact]
    public void FullSave_DynamicArraySpillMembers_SurviveReloadWithoutRecalculation()
    {
        var adapter = new XlsxFileAdapter();
        var workbook = CreateDynamicArraySpillWorkbook();

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        saved.Position = 0;
        var reloaded = adapter.Load(saved);
        var sheet = reloaded.GetSheetAt(0);

        // No recalculation happens between save and this assertion — these values must come
        // straight from the saved cache, exactly as real Excel would display them on open.
        sheet.GetValue(3, 1).Should().Be(new NumberValue(1), "the anchor's own cached value must survive");
        sheet.GetValue(3, 2).Should().Be(new NumberValue(2), "spill member B3 must keep its cached value, not go Blank");
        sheet.GetValue(3, 3).Should().Be(new NumberValue(3), "spill member C3 must keep its cached value, not go Blank");
    }

    /// <summary>Sibling no-regression check: a plain (non-spilling) formula's own cached value is
    /// unaffected by the new spill-member patch pass.</summary>
    [Fact]
    public void FullSave_PlainFormulaCell_StillCachesItsOwnValue()
    {
        var adapter = new XlsxFileAdapter();
        var workbook = new Workbook("PlainFormulaCache");
        var sheet = workbook.AddSheet("Data");

        var addr = new CellAddress(sheet.Id, 1, 1);
        sheet.SetFormula(addr, "1+1");
        sheet.GetCell(addr)!.Value = new NumberValue(2);

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        saved.Position = 0;
        var reloaded = adapter.Load(saved);
        var reloadedSheet = reloaded.GetSheetAt(0);

        reloadedSheet.GetValue(1, 1).Should().Be(new NumberValue(2));
    }
}
