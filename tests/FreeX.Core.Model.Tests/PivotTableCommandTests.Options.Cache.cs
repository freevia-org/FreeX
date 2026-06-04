using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed partial class PivotTableCommandTests
{
    [Fact]
    public void ConfigurePivotTableOptionsCommand_UpdatesPivotCacheDataOptionsAndUndoRestores()
    {
        var workbook = new Workbook("PivotCacheOptionsCommandTest");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(Addr(sheet, "A1"), new TextValue("Region"));
        sheet.SetCell(Addr(sheet, "B1"), new TextValue("Amount"));
        sheet.SetCell(Addr(sheet, "A2"), new TextValue("East"));
        sheet.SetCell(Addr(sheet, "B2"), new NumberValue(10));
        var ctx = new TestCommandContext(workbook);
        var cache = new PivotCacheModel
        {
            CacheId = 7,
            SourceType = PivotCacheSourceType.WorksheetRange,
            SourceSheetName = "Data",
            SourceReference = "A1:B2",
            RefreshOnLoad = false,
            SaveData = true,
            EnableRefresh = true,
            PreserveSourceSortFilter = true,
            MissingItemsLimit = null
        };
        cache.Fields.Add(new PivotCacheFieldModel("Region"));
        cache.Fields.Add(new PivotCacheFieldModel("Amount"));
        workbook.PivotCaches.Add(cache);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 7,
            SourceRange = Range(sheet, "A1", "B2"),
            TargetRange = Range(sheet, "D2", "F5"),
            StyleName = "PivotStyleLight16"
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Amount", "sum"));
        sheet.PivotTables.Add(pivot);

        var command = new ConfigurePivotTableOptionsCommand(
            sheet.Id,
            "PivotTable1",
            showRowGrandTotals: true,
            showColumnGrandTotals: true,
            showSubtotals: false,
            subtotalPlacement: PivotSubtotalPlacement.Bottom,
            repeatItemLabels: false,
            blankLineAfterItems: false,
            styleName: "PivotStyleLight16",
            refreshOnOpen: true,
            saveSourceData: false,
            enableRefresh: false,
            preserveSourceSortFilter: false,
            missingItemsLimit: 0,
            updateMissingItemsLimit: true);

        command.Apply(ctx).Success.Should().BeTrue();

        cache.RefreshOnLoad.Should().BeTrue();
        cache.SaveData.Should().BeFalse();
        cache.EnableRefresh.Should().BeFalse();
        cache.PreserveSourceSortFilter.Should().BeFalse();
        cache.MissingItemsLimit.Should().Be(0);

        command.Revert(ctx);

        cache.RefreshOnLoad.Should().BeFalse();
        cache.SaveData.Should().BeTrue();
        cache.EnableRefresh.Should().BeTrue();
        cache.PreserveSourceSortFilter.Should().BeTrue();
        cache.MissingItemsLimit.Should().BeNull();
    }
}
