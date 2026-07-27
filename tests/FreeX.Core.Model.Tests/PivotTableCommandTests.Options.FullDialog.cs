using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed partial class PivotTableCommandTests
{
    [Fact]
    public void ConfigurePivotTableOptionsCommand_AppliesFullDialogValuesAcrossTabsAndUndoRestores()
    {
        var workbook = new Workbook("PivotFullOptionsCommandTest");
        var sheet = workbook.AddSheet("Data");
        SeedData(sheet);
        var cache = new PivotCacheModel
        {
            CacheId = 1,
            RefreshOnLoad = false,
            SaveData = true,
            EnableRefresh = true,
            PreserveSourceSortFilter = true,
            MissingItemsLimit = null
        };
        cache.Fields.Add(new PivotCacheFieldModel("Category"));
        cache.Fields.Add(new PivotCacheFieldModel("Amount"));
        workbook.PivotCaches.Add(cache);

        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "B3"),
            TargetRange = Range(sheet, "D3", "F8"),
            StyleName = "PivotStyleLight16",
            EmptyValueText = null,
            ErrorCaption = null,
            AltTextTitle = "Old title",
            AltTextDescription = "Old description",
            ShowRowHeaders = true,
            ShowColumnHeaders = true,
            ShowFieldHeaders = true,
            ShowContextualTooltips = true,
            ShowPropertiesInTooltips = true,
            ShowClassicLayout = false,
            ShowItemsWithNoDataOnRows = false,
            ShowItemsWithNoDataOnColumns = false,
            ShowRowStripes = false,
            ShowColumnStripes = false,
            PrintTitles = false,
            PrintExpandCollapseButtons = false,
            ShowExpandCollapseButtons = true,
            AutofitColumnsOnUpdate = true,
            PreserveFormattingOnUpdate = true,
            EnableDrill = true
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Amount", "sum"));
        sheet.PivotTables.Add(pivot);

        var ctx = new TestCommandContext(workbook);
        var command = new ConfigurePivotTableOptionsCommand(
            sheet.Id,
            pivot.Name,
            showRowGrandTotals: false,
            showColumnGrandTotals: false,
            showSubtotals: false,
            subtotalPlacement: PivotSubtotalPlacement.Bottom,
            repeatItemLabels: false,
            blankLineAfterItems: true,
            styleName: "PivotStyleMedium9",
            showRowHeaders: false,
            showColumnHeaders: false,
            showRowStripes: true,
            showColumnStripes: true,
            reportLayout: PivotReportLayout.Tabular,
            emptyValueText: "N/A",
            updateEmptyValueText: true,
            refreshOnOpen: true,
            saveSourceData: false,
            enableRefresh: false,
            preserveSourceSortFilter: false,
            missingItemsLimit: 0,
            updateMissingItemsLimit: true,
            printTitles: true,
            printExpandCollapseButtons: true,
            altTextTitle: "Sales pivot",
            altTextDescription: "Quarterly sales summary",
            compactRowLabelIndent: 6,
            updateAltText: true,
            showExpandCollapseButtons: false,
            autofitColumnsOnUpdate: false,
            preserveFormattingOnUpdate: false,
            showFieldHeaders: false,
            showContextualTooltips: false,
            showPropertiesInTooltips: false,
            showClassicLayout: true,
            mergeAndCenterLabels: true,
            showItemsWithNoDataOnRows: true,
            showItemsWithNoDataOnColumns: true,
            pageOverThenDown: true,
            pageWrap: 4,
            errorCaption: "#VALUE!",
            updateErrorCaption: true,
            enableDrill: false);

        command.Apply(ctx).Success.Should().BeTrue();

        pivot.StyleName.Should().Be("PivotStyleMedium9");
        pivot.EmptyValueText.Should().Be("N/A");
        pivot.ErrorCaption.Should().Be("#VALUE!");
        pivot.ShowRowHeaders.Should().BeFalse();
        pivot.ShowColumnHeaders.Should().BeFalse();
        pivot.ShowFieldHeaders.Should().BeFalse();
        pivot.ShowContextualTooltips.Should().BeFalse();
        pivot.ShowPropertiesInTooltips.Should().BeFalse();
        pivot.ShowClassicLayout.Should().BeTrue();
        pivot.ShowItemsWithNoDataOnRows.Should().BeTrue();
        pivot.ShowItemsWithNoDataOnColumns.Should().BeTrue();
        pivot.ShowRowStripes.Should().BeTrue();
        pivot.ShowColumnStripes.Should().BeTrue();
        pivot.PrintTitles.Should().BeTrue();
        pivot.PrintExpandCollapseButtons.Should().BeTrue();
        pivot.ShowExpandCollapseButtons.Should().BeFalse();
        pivot.AutofitColumnsOnUpdate.Should().BeFalse();
        pivot.PreserveFormattingOnUpdate.Should().BeFalse();
        pivot.AltTextTitle.Should().Be("Sales pivot");
        pivot.AltTextDescription.Should().Be("Quarterly sales summary");
        pivot.EnableDrill.Should().BeFalse();
        cache.RefreshOnLoad.Should().BeTrue();
        cache.SaveData.Should().BeFalse();
        cache.EnableRefresh.Should().BeFalse();
        cache.PreserveSourceSortFilter.Should().BeFalse();
        cache.MissingItemsLimit.Should().Be(0);

        command.Revert(ctx);

        pivot.StyleName.Should().Be("PivotStyleLight16");
        pivot.EmptyValueText.Should().BeNull();
        pivot.ErrorCaption.Should().BeNull();
        pivot.ShowRowHeaders.Should().BeTrue();
        pivot.ShowColumnHeaders.Should().BeTrue();
        pivot.ShowFieldHeaders.Should().BeTrue();
        pivot.ShowContextualTooltips.Should().BeTrue();
        pivot.ShowPropertiesInTooltips.Should().BeTrue();
        pivot.ShowClassicLayout.Should().BeFalse();
        pivot.ShowItemsWithNoDataOnRows.Should().BeFalse();
        pivot.ShowItemsWithNoDataOnColumns.Should().BeFalse();
        pivot.ShowRowStripes.Should().BeFalse();
        pivot.ShowColumnStripes.Should().BeFalse();
        pivot.PrintTitles.Should().BeFalse();
        pivot.PrintExpandCollapseButtons.Should().BeFalse();
        pivot.ShowExpandCollapseButtons.Should().BeTrue();
        pivot.AutofitColumnsOnUpdate.Should().BeTrue();
        pivot.PreserveFormattingOnUpdate.Should().BeTrue();
        pivot.AltTextTitle.Should().Be("Old title");
        pivot.AltTextDescription.Should().Be("Old description");
        pivot.EnableDrill.Should().BeTrue();
        cache.RefreshOnLoad.Should().BeFalse();
        cache.SaveData.Should().BeTrue();
        cache.EnableRefresh.Should().BeTrue();
        cache.PreserveSourceSortFilter.Should().BeTrue();
        cache.MissingItemsLimit.Should().BeNull();
    }
}
