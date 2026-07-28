using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class DataToolDialogPlannerSourceGuardTests
{
    [Fact]
    public void HostDataToolPlanningFacades_DelegatePortableLogicToSharedPlanners()
    {
        var advancedFilter = DialogSourceTestSupport.ReadHostSources("AdvancedFilterDialog.Planning.cs");
        advancedFilter.Should().Contain("SharedAdvancedFilterPlanner.CreatePlan(");
        advancedFilter.Should().Contain("SharedAdvancedFilterOutputMode");
        advancedFilter.Should().NotContain("ServicesAdvancedFilterPlanner");
        advancedFilter.Should().NotContain("WorkbookReferenceNavigator");

        var consolidate = DialogSourceTestSupport.ReadHostSources("ConsolidateDialog.Planning.cs");
        consolidate.Should().Contain("SharedConsolidateDialogPlanner.TryAddReference(");
        consolidate.Should().Contain("SharedConsolidateDialogPlanner.TryParse(");
        consolidate.Should().Contain("SharedConsolidateDialogPlanner.CreateRangeSelectionRequest(");
        consolidate.Should().NotContain("WorkbookRangeTextCodec.TryParse");

        var parityCapture = DialogSourceTestSupport.ReadHostSources("ParityCapture.cs");
        parityCapture.Should().Contain("ConsolidateParityFixture.SourceReference");
        parityCapture.Should().Contain("ConsolidateParityFixture.DestinationReference");
        parityCapture.Should().Contain("requireForeground: true");

        var screenshotTour = DialogSourceTestSupport.ReadHostSources("MainWindow.ScreenshotTour.cs");
        screenshotTour.Should().Contain("ConsolidateParityFixture.CreateSourceRange(sheet.Id)");
        screenshotTour.Should().Contain("ConsolidateParityFixture.SourceReference");
        screenshotTour.Should().Contain("ConsolidateParityFixture.DestinationReference");

        var dataValidation = DialogSourceTestSupport.ReadHostSources("DataValidationDialog.Planning.cs");
        dataValidation.Should().Contain("DataValidationDialogPlanner.ValidateCriteria(");
        dataValidation.Should().Contain("DataValidationDialogPlanner.FocusTargetForInvalidCriteria(");
        dataValidation.Should().Contain("DataValidationDialogPlanner.CreateRangeSelectionRequest(");
        dataValidation.Should().NotContain("DataValidationDialogModel.ForType");

        var removeDuplicates = DialogSourceTestSupport.ReadHostSources("RemoveDuplicatesDialog.Planning.cs");
        removeDuplicates.Should().Contain("ServicesRemoveDuplicatesPlanner.SelectAll(");
        removeDuplicates.Should().Contain("ServicesRemoveDuplicatesPlanner.ClearAll(");
        removeDuplicates.Should().Contain("ServicesRemoveDuplicatesPlanner.GetSelectedColumnOffsets(");
        removeDuplicates.Should().Contain("ServicesRemoveDuplicatesPlanner.BuildColumnChoices(");
        removeDuplicates.Should().Contain("ServicesRemoveDuplicatesPlanner.GuessHasHeaders(");
        removeDuplicates.Should().Contain("ServicesRemoveDuplicatesPlanner.ExcludeHeaderRow(");
        removeDuplicates.Should().NotContain("SpreadsheetDisplayFormatter");
        removeDuplicates.Should().NotContain("ScalarValue?");
        removeDuplicates.Should().NotContain("NumberValue or DateTimeValue or BoolValue");

        var sort = DialogSourceTestSupport.ReadHostSources("SortDialog.Planning.cs");
        sort.Should().Contain("SortDialogPlanner.BuildSortKeys(levels, PlannerText)");
        sort.Should().Contain("SortDialogPlanner.BuildColumnChoices(sheet, range, hasHeaders, PlannerText)");
        sort.Should().Contain("SortDialogPlanner.ExcludeHeaderRow(range, hasHeaders)");

        var subtotal = DialogSourceTestSupport.ReadHostSources("SubtotalDialog.cs");
        subtotal.Should().Contain("SharedSubtotalDialogPlanner.BuildColumnChoices(sheet, range, PlannerText)");
        subtotal.Should().Contain("SharedSubtotalDialogPlanner.CreateFunctionChoices(PlannerText)");
        subtotal.Should().Contain("SharedSubtotalDialogPlanner.TryCreateResult(");
        subtotal.Should().Contain("SharedSubtotalDialogPlanner.CreateRemoveAllResult()");
        subtotal.Should().Contain("SubtotalFunctionService.TryParse");
        subtotal.Should().NotContain("SpreadsheetDisplayFormatter.FormatCellValue");

        var selectDataSource = DialogSourceTestSupport.ReadHostSources("SelectDataSourceDialog.Planning.cs");
        selectDataSource.Should().Contain("SelectDataSourcePlanner.CreateResult(");
        selectDataSource.Should().Contain("SelectDataSourcePlanner.InferPreviewEntries(");
        selectDataSource.Should().Contain("SelectDataSourcePlanner.CreateRangeSelectionRequest(");
        selectDataSource.Should().NotContain("TryParseCellRef");
    }
}
