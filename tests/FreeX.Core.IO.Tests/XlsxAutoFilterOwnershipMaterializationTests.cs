using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression coverage for finding H2: loading an .xlsx AutoFilter with a value-list criterion must
/// rebuild <see cref="Sheet.ActiveValueFilterColumns"/> and <see cref="Sheet.ValueFilterHiddenRows"/>
/// from the parsed AutoFilter XML, not just <see cref="Sheet.FilterHiddenRows"/>. Without this, the
/// ownership pair that FreeX.Core.Commands.FilterCommand.RecomputeHiddenRows relies on to decide which
/// rows it may safely un-hide is left empty after every xlsx round-trip, permanently stranding rows
/// hidden by the loaded value-list filter the next time any column's filter is touched.
/// </summary>
public sealed class XlsxAutoFilterOwnershipMaterializationTests
{
    [Fact]
    public void WorksheetAutoFilter_ValueListCriterion_RebuildsActiveValueFilterColumnsAndValueFilterHiddenRows()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");

        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("East"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("West"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("East"));

        sheet.AutoFilter = new WorksheetAutoFilterModel("A1:A4", null);
        sheet.AutoFilter.FilterColumns.Add(new WorksheetAutoFilterColumnModel(0, ["East"]));

        XlsxWorksheetAutoFilterMaterializer.MaterializeFilters(sheet);

        // Rows failing the loaded value-list filter are hidden, exactly as before the fix.
        sheet.FilterHiddenRows.Should().BeEquivalentTo([3u]);

        // The ownership pair must also be rebuilt so a later, unrelated filter recompute knows this
        // mechanism owns row 3 and may safely un-hide it once it no longer fails any active column.
        sheet.ActiveValueFilterColumns.Should().ContainKey(1);
        sheet.ActiveValueFilterColumns[1].Should().BeEquivalentTo(["East"]);
        sheet.ValueFilterHiddenRows.Should().BeEquivalentTo([3u]);
    }

    [Fact]
    public void WorksheetAutoFilter_ValueListCriterionOnMultipleColumns_OnlyRegistersPlainValueListColumns()
    {
        // Top10/Average-style criteria hide rows without owning them via ActiveValueFilterColumns
        // (see FilterCommand's G7 comments) — the materializer must not mis-register those as
        // value-list ownership, only the plain value-list column.
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");

        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Amount"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("East"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("West"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(2));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("East"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new NumberValue(3));

        sheet.AutoFilter = new WorksheetAutoFilterModel("A1:B4", null);
        sheet.AutoFilter.FilterColumns.Add(new WorksheetAutoFilterColumnModel(0, ["East"]));
        sheet.AutoFilter.FilterColumns.Add(new WorksheetAutoFilterColumnModel(1, [])
        {
            Top10 = new WorksheetAutoFilterTop10Model(Top: true, Percent: false, Value: 1)
        });

        XlsxWorksheetAutoFilterMaterializer.MaterializeFilters(sheet);

        // Top-1 by Amount keeps only row 4 (value 3); rows 2 and 3 fail it. Row 3 additionally fails
        // the Region value-list filter. So rows 2 and 3 are hidden — but only column A (Region) is a
        // plain value-list filter, so only it is registered as owned in ActiveValueFilterColumns.
        sheet.FilterHiddenRows.Should().BeEquivalentTo([2u, 3u]);
        sheet.ActiveValueFilterColumns.Should().ContainKey(1);
        sheet.ActiveValueFilterColumns.Should().NotContainKey(2);
        // Only row 3 fails the value-list filter itself — row 2 is hidden solely by the Top10
        // mechanism on column B, so it must NOT be claimed as owned by the value-filter mechanism.
        sheet.ValueFilterHiddenRows.Should().BeEquivalentTo([3u]);
    }
}
