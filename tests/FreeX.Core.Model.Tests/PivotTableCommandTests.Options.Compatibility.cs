using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed partial class PivotTableCommandTests
{
    [Fact]
    public void ConfigurePivotTableOptionsCommand_PreservesOldOptionalArgumentOrder()
    {
        var workbook = new Workbook("PivotOptionsArgumentOrderTest");
        var sheet = workbook.AddSheet("Data");
        SeedData(sheet);
        var ctx = new TestCommandContext(workbook);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "B3"),
            TargetRange = Range(sheet, "D3", "F8"),
            StyleName = "PivotStyleLight16",
            ErrorCaption = "(preserved)"
        };
        var cache = new PivotCacheModel
        {
            CacheId = 1,
            RefreshOnLoad = false,
            SaveData = true,
            EnableRefresh = true
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Amount", "sum"));
        sheet.PivotTables.Add(pivot);
        workbook.PivotCaches.Add(cache);

        var command = new ConfigurePivotTableOptionsCommand(
            sheet.Id,
            "PivotTable1",
            true,
            true,
            false,
            PivotSubtotalPlacement.Bottom,
            false,
            false,
            "PivotStyleLight16",
            true,
            true,
            false,
            false,
            PivotReportLayout.Tabular,
            null,
            false,
            true,
            false,
            false);

        command.Apply(ctx).Success.Should().BeTrue();

        cache.RefreshOnLoad.Should().BeTrue();
        cache.SaveData.Should().BeFalse();
        cache.EnableRefresh.Should().BeFalse();
        pivot.ErrorCaption.Should().Be("(preserved)");
    }

    [Fact]
    public void ConfigurePivotTableOptionsCommand_PreservesModeledAdvancedOptionsWhenCallerOmitsThem()
    {
        var workbook = new Workbook("PivotCompactIndentCompatibilityTest");
        var sheet = workbook.AddSheet("Data");
        SeedData(sheet);
        var ctx = new TestCommandContext(workbook);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "B3"),
            TargetRange = Range(sheet, "D3", "F8"),
            ReportLayout = PivotReportLayout.Compact,
            CompactRowLabelIndent = 5,
            MergeAndCenterLabels = true,
            PrintTitles = true,
            PrintExpandCollapseButtons = true,
            ShowExpandCollapseButtons = false,
            ShowContextualTooltips = false,
            ShowPropertiesInTooltips = false,
            ShowClassicLayout = true,
            PageOverThenDown = true,
            PageWrap = 3,
            AutofitColumnsOnUpdate = false,
            PreserveFormattingOnUpdate = false,
            AltTextTitle = "Existing title",
            AltTextDescription = "Existing description"
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.RowFields.Add(new PivotFieldModel(1));
        pivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Amount", "sum"));
        sheet.PivotTables.Add(pivot);

        var command = new ConfigurePivotTableOptionsCommand(
            sheet.Id,
            "PivotTable1",
            showRowGrandTotals: true,
            showColumnGrandTotals: true,
            showSubtotals: false,
            subtotalPlacement: PivotSubtotalPlacement.Bottom,
            repeatItemLabels: true,
            blankLineAfterItems: false,
            styleName: "PivotStyleLight16",
            reportLayout: PivotReportLayout.Compact);

        command.Apply(ctx).Success.Should().BeTrue();

        pivot.CompactRowLabelIndent.Should().Be(5);
        pivot.MergeAndCenterLabels.Should().BeTrue();
        pivot.PrintTitles.Should().BeTrue();
        pivot.PrintExpandCollapseButtons.Should().BeTrue();
        pivot.ShowExpandCollapseButtons.Should().BeFalse();
        pivot.ShowContextualTooltips.Should().BeFalse();
        pivot.ShowPropertiesInTooltips.Should().BeFalse();
        pivot.ShowClassicLayout.Should().BeTrue();
        pivot.PageOverThenDown.Should().BeTrue();
        pivot.PageWrap.Should().Be(3);
        pivot.AutofitColumnsOnUpdate.Should().BeFalse();
        pivot.PreserveFormattingOnUpdate.Should().BeFalse();
        pivot.AltTextTitle.Should().Be("Existing title");
        pivot.AltTextDescription.Should().Be("Existing description");
        workbook.GetStyle(sheet.GetCell(Addr(sheet, "D4"))!.StyleId).IndentLevel.Should().Be(5);
    }
}
