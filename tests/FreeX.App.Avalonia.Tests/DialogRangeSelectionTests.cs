using System.IO;

using FreeX.App.Presentation.InteractionValidation;

namespace FreeX.App.Avalonia.Tests;

public sealed class DialogRangeSelectionTests
{
    private static readonly string[] ExpectedTargetIds =
    [
        "range.create-table.range",
        "range.sparklines.data-range",
        "range.sparklines.location-range",
        "range.consolidate.reference",
        "range.consolidate.destination-cell",
        "range.advanced-filter.list-range",
        "range.advanced-filter.criteria-range",
        "range.advanced-filter.copy-to",
        "range.goal-seek.set-cell",
        "range.goal-seek.changing-cell",
        "range.chart-data-source.range",
        "range.data-table.row-input-cell",
        "range.data-table.column-input-cell",
        "range.data-validation.formula-1",
        "range.data-validation.formula-2",
        "range.page-setup.print-area",
        "range.page-setup.rows-to-repeat",
        "range.page-setup.columns-to-repeat",
        "range.allow-edit-range.range",
        "range.text-to-columns.destination",
        "range.resize-table.range",
        "range.named-ranges.selected-refers-to",
        "range.named-ranges.definition-refers-to",
        "range.pivot-create.source",
        "range.pivot-create.destination",
        "range.scenario-manager.changing-cells",
        "range.scenario-manager.result-cells",
        "range.function-argument.reference",
        "range.conditional-format.applies-to",
        "range.move-pivot.destination",
        "range.pivot-data-source.range",
    ];

    [Fact]
    public void InteractiveValidationRangeTargetIds_ExactlyMatchAllWiredInventoryTargets()
    {
        MainWindow.InteractiveValidationRangeTargetIds.Should().BeEquivalentTo(ExpectedTargetIds);
        MainWindow.InteractiveValidationRangeTargetIds.Should().HaveCount(31);
        InteractiveValidationInventory.WorksheetRangeTargets
            .Where(target => MainWindow.InteractiveValidationRangeTargetIds.Contains(target.Id))
            .Select(target => target.Id)
            .Should().BeEquivalentTo(ExpectedTargetIds);
    }

    [Fact]
    public void NeutralInventoryDialogBuilders_WireAllTenRangePickersToTheSharedSession()
    {
        var registrations = ReadSource("MainWindow.DialogRangeSelection.cs");
        var pageLayout = ReadSource("MainWindow.PageLayout.cs");
        var allowEditRange = ReadSource("MainWindow.AllowEditRange.cs");
        var textToColumns = ReadSource("MainWindow.TextToColumns.cs");
        var tableResize = ReadSource("MainWindow.TableResize.cs");

        registrations.Should().Contain("new(\"range.data-table.row-input-cell\", \"DataTableCompactDialog\", \"DataTableRowInputCellPickerButton\", \"DataTableRowInputCellBox\", DialogRangeSelectionFormat.StartCell, CreatePickerWhenMissing: true)");
        registrations.Should().Contain("new(\"range.data-table.column-input-cell\", \"DataTableCompactDialog\", \"DataTableColumnInputCellPickerButton\", \"DataTableColumnInputCellBox\", DialogRangeSelectionFormat.StartCell, CreatePickerWhenMissing: true)");
        registrations.Should().Contain("new(\"range.data-validation.formula-1\", \"DataValidationCompactDialog\", \"DataValidationSourcePickerButton\", \"DataValidationFormula1Box\", DialogRangeSelectionFormat.DataValidationFormula, CreatePickerWhenMissing: true)");
        registrations.Should().Contain("new(\"range.data-validation.formula-2\", \"DataValidationCompactDialog\", \"DataValidationSourcePicker2Button\", \"DataValidationFormula2Box\", DialogRangeSelectionFormat.DataValidationFormula, CreatePickerWhenMissing: true)");
        pageLayout.Should().Contain("AttachDialogRangePicker(dialog, printAreaPicker, printAreaBox, \"range.page-setup.print-area\");");
        pageLayout.Should().Contain("AttachDialogRangePicker(dialog, repeatRowsPicker, repeatRowsBox, \"range.page-setup.rows-to-repeat\");");
        pageLayout.Should().Contain("AttachDialogRangePicker(dialog, repeatColumnsPicker, repeatColumnsBox, \"range.page-setup.columns-to-repeat\");");
        allowEditRange.Should().Contain("AttachDialogRangePicker(dialog, rangePicker, rangeBox, \"range.allow-edit-range.range\");");
        textToColumns.Should().Contain("AttachDialogRangePicker(dialog, destinationPicker, destinationBox, \"range.text-to-columns.destination\");");
        tableResize.Should().Contain("AttachDialogRangePicker(dialog, rangePicker, rangeBox, \"range.resize-table.range\");");
    }

    [Fact]
    public void NeutralInventoryFormatting_UsesTheWpfBehavioralFormatters()
    {
        var registrations = ReadSource("MainWindow.DialogRangeSelection.cs");
        var formatter = ReadPresentationSource("Dialogs", "DialogRangeSelectionFormatter.cs");
        var textToColumns = ReadSource("MainWindow.TextToColumns.cs");

        registrations.Should().Contain("DialogRangeSelectionFormatter.Format(");
        registrations.Should().NotContain("format switch");
        formatter.Should().Contain("DataValidationService.FormatListSourceRange(");
        formatter.Should().Contain("PageSetupRangeSelectionTarget.PrintArea");
        formatter.Should().Contain("PageSetupRangeSelectionTarget.RepeatRows");
        formatter.Should().Contain("PageSetupRangeSelectionTarget.RepeatColumns");
        textToColumns.Should().Contain("TextToColumnsDialogPlanner.TryParseDestination(");
        textToColumns.Should().Contain("TextToColumnsApplyPlanner.MapResultToEdits(");
        textToColumns.Should().Contain("destination,");
        textToColumns.Should().Contain("destinationBox.TextChanged += (_, _) => overwriteConfirmed = false;");
        textToColumns.Should().NotContain("warningText.IsVisible = false;\n            overwriteConfirmed = false;");
    }

    [Fact]
    public void OwnedDialogBuilders_DelegateTheirSixPickersToTheSharedSession()
    {
        var insertObjects = ReadSource("MainWindow.InsertObjects.cs");
        var sparklines = ReadSource("MainWindow.Sparklines.cs");
        var consolidate = ReadSource("MainWindow.Consolidate.cs");
        var chartTabs = ReadSource("MainWindow.ChartTabs.cs");

        insertObjects.Should().Contain("AttachDialogRangePicker(dialog, rangePicker, rangeBox, \"range.create-table.range\");");
        sparklines.Should().Contain("AttachDialogRangePicker(dialog, selectDataRangeButton, dataRangeBox, \"range.sparklines.data-range\");");
        sparklines.Should().Contain("AttachDialogRangePicker(dialog, selectLocationRangeButton, locationBox, \"range.sparklines.location-range\");");
        consolidate.Should().Contain("AttachDialogRangePicker(dialog, browseButton, referenceBox, \"range.consolidate.reference\");");
        consolidate.Should().Contain("AttachDialogRangePicker(dialog, destinationBrowseButton, destinationBox, \"range.consolidate.destination-cell\");");
        chartTabs.Should().Contain("AttachDialogRangePicker(dialog, rangePickButton, rangeBox, \"range.chart-data-source.range\");");
    }

    [Fact]
    public void ConsolidateCapture_UsesExplicitFixtureStateWhileProductionRemainsSelectionDerived()
    {
        var consolidate = ReadSource("MainWindow.Consolidate.cs");
        var parityCapture = ReadSource("MainWindow.ParityCapture.cs");

        consolidate.Should().Contain("ConsolidateDialogInitialState? initialState = null");
        consolidate.Should().Contain("initialState?.SourceReference ?? FormatRangeReference(selectedRange)");
        consolidate.Should().Contain("initialState?.DestinationReference ?? FormatCellReference(selectedRange.Start)");
        consolidate.Should().Contain("ConsolidateDeleteReferenceButton");
        consolidate.Should().Contain("ConsolidateDialogPlanner.HasPendingReferenceText(references, referenceBox.Text)");
        consolidate.Should().Contain("rejectDuplicateReferences: false");
        parityCapture.Should().Contain("ConsolidateParityFixture.CreateDialogInitialState()");
    }

    [Fact]
    public void SharedSession_CoversAcceptCancelRestoreAndCloseCleanup()
    {
        var source = ReadSource("MainWindow.DialogRangeSelection.cs");

        source.Should().Contain("Window.OwnerProperty.Changed.AddClassHandler<Window>(DialogRangePickerOwnerChanged);");
        source.Should().Contain("DialogRangePickerPointerReleased");
        source.Should().Contain("_dialogRangeSelectionController.HandleKey(");
        source.Should().Contain("DialogRangeSelectionGeometryPlanner.ResolveDimension(");
        source.Should().Contain("DialogRangeSelectionKey.Escape");
        source.Should().Contain("DialogRangeSelectionKey.Enter");
        source.Should().Contain("state.Context.Target.Text = state.OriginalText");
        source.Should().Contain("RestoreDialogAfterRangeSelection);");
        source.Should().Contain("context.Dialog.Activate();");
        source.Should().Contain("dialog.Closed += DialogRangePickerDialogClosed;");
        source.Should().Contain("CancelDialogRangeSelection(restoreDialog: false, restoreOriginalText: false);");
        source.Should().Contain("DialogRangeSelectionFormatter.Format(");
        source.Should().Contain("SetPlatformWindowEnabledMethod?.Invoke(platformImpl, [isEnabled]);");
    }

    private static string ReadSource(string fileName) =>
        File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", fileName));

    private static string ReadPresentationSource(params string[] parts) =>
        File.ReadAllText(RepoFile(["src", "FreeX.App.Presentation", .. parts]));

    private static string RepoFile(params string[] parts) =>
        TestWorkspaceFileLocator.FindFileFromBaseDirectory(parts);
}
