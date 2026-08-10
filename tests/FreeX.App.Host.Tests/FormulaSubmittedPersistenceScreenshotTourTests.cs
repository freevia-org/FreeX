using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class FormulaSubmittedPersistenceScreenshotTourTests
{
    [Fact]
    public void MainWindowScreenshotTour_CapturesFormulaSubmittedPersistenceEvidence()
    {
        var dispatcherSource = DialogSourceTestSupport.ReadHostSourceFile("MainWindow.ScreenshotTour.cs");
        var tourSource = DialogSourceTestSupport.ReadHostSourceFile("MainWindow.ScreenshotTour.FormulaSubmittedPersistence.cs");
        var catalog = WorkspaceFileLocator.ReadAllText("docs", "testing", "ui-test-catalog.md");

        dispatcherSource.Should().Contain("FREEX_FORMULA_SUBMITTED_PERSISTENCE_TOUR");
        dispatcherSource.Should().Contain("FormulaSubmittedPersistenceTourOutputDirectoryName = \"formula-submitted-persistence-tour\"");
        dispatcherSource.Should().Contain("FormulaSubmittedPersistenceTourSavedWorkbookFileName = \"freex_formula_submitted_persistence_saved.fxl\"");
        dispatcherSource.Should().Contain("CaptureFormulaSubmittedPersistenceTourAsync");
        dispatcherSource.Should().Contain("[JsonSerializable(typeof(FormulaSubmittedPersistenceTourManifest))]");

        tourSource.Should().Contain("EnsureFormulaSubmittedPersistenceTourContext");
        tourSource.Should().Contain("new DefineNamedRangeCommand(");
        tourSource.Should().Contain("new CreateNamedRangesFromSelectionCommand(context.AuthoringRange");
        tourSource.Should().Contain("EditCellsCommand.ForFormula(context.Sheet.Id, context.NamedInsertionCell");
        tourSource.Should().Contain("new EditCellsCommand(context.Sheet.Id, formulaEdits)");
        tourSource.Should().NotContain("RecalculateIfAutomatic(formulaOutcome.AffectedCells");
        tourSource.Should().Contain("BeginFormulaBarFormulaEdit(\"=\")");
        tourSource.Should().Contain("InsertDefinedNameIntoFormula(\"TourRevenue\")");
        tourSource.Should().Contain("UseInFormulaBtn_Click(button, new RoutedEventArgs(ButtonBase.ClickEvent, button))");
        tourSource.Should().Contain("new NamedRangeDialog(_workbook, ExecuteDialogCommandPreservingSelection, context.AuthoringRange)");
        tourSource.Should().Contain("SaveWorkbookToTargetAsync(new FileSaveTarget(savedWorkbookPath, adapter))");
        tourSource.Should().Contain("OpenFileAsync(savedWorkbookPath)");
        tourSource.Should().Contain("RibbonScreenshotTourManifestJsonContext.Default.FormulaSubmittedPersistenceTourManifest");

        tourSource.Should().Contain("freex_formula_submitted_persistence_seeded_before_submit");
        tourSource.Should().Contain("freex_formula_submitted_persistence_formula_results");
        tourSource.Should().Contain("freex_formula_submitted_persistence_use_in_formula_inserted_reference");
        tourSource.Should().Contain("freex_formula_submitted_persistence_use_in_formula_menu");
        tourSource.Should().Contain("freex_formula_submitted_persistence_name_manager_submitted");
        tourSource.Should().Contain("freex_formula_submitted_persistence_saved_native_workbook");
        tourSource.Should().Contain("freex_formula_submitted_persistence_reopened_grid");
        tourSource.Should().Contain("freex_formula_submitted_persistence_name_manager_reopened");

        tourSource.Should().Contain("UI-CAT-FORMULAS-001");
        tourSource.Should().Contain("UI-CMD-FORM-001");
        tourSource.Should().Contain("UI-CMD-FORM-002");
        tourSource.Should().Contain("RenderTargetBitmap; it is not foreground CopyFromScreen proof");
        tourSource.Should().Contain("No physical mouse, keytip, Shift+F3, or UIA invocation is synthesized");
        tourSource.Should().Contain("Persistence is proven for the native FreeX .fxl adapter");

        catalog.Should().Contain("FREEX_FORMULA_SUBMITTED_PERSISTENCE_TOUR=1");
        catalog.Should().Contain("screenshots/formula-submitted-persistence-tour/");
        catalog.Should().Contain("formula_submitted_persistence_tour_manifest.json");
        catalog.Should().Contain("freex_formula_submitted_persistence_reopened_grid.png");
        catalog.Should().Contain("freex_formula_submitted_persistence_saved.fxl");
    }
}
