using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class ReviewProtectionMatrixScreenshotTourTests
{
    [Fact]
    public void MainWindowScreenshotTour_CapturesReviewProtectionMatrixEvidence()
    {
        var dispatcherSource = DialogSourceTestSupport.ReadHostSourceFile("MainWindow.ScreenshotTour.cs");
        var tourSource = DialogSourceTestSupport.ReadHostSourceFile("MainWindow.ScreenshotTour.ReviewProtectionMatrix.cs");
        var catalog = WorkspaceFileLocator.ReadAllText("docs", "testing", "ui-test-catalog.md");

        dispatcherSource.Should().Contain("FREEX_REVIEW_PROTECTION_MATRIX_TOUR");
        dispatcherSource.Should().Contain("ReviewProtectionMatrixTourOutputDirectoryName = \"review-protection-matrix-tour\"");
        dispatcherSource.Should().Contain("CaptureReviewProtectionMatrixTourAsync(Path.Combine(outputDir, ReviewProtectionMatrixTourOutputDirectoryName))");
        dispatcherSource.Should().Contain("[JsonSerializable(typeof(ReviewProtectionMatrixTourManifest))]");

        tourSource.Should().Contain("new PasswordProtectionDialog(");
        tourSource.Should().Contain("LocalizeReviewProtectionPermission(SheetProtectionPermission.SelectUnlockedCells)");
        tourSource.Should().Contain("LocalizeReviewProtectionPermission(SheetProtectionPermission.Sort)");
        tourSource.Should().Contain("LocalizeReviewProtectionPermission(SheetProtectionPermission.UseAutoFilter)");
        tourSource.Should().Contain("ProtectionWorkflowSession.CreateSheetCommandPlan(");
        tourSource.Should().Contain("ProtectionWorkflowSession.CreateWorkbookCommandPlan(_workbook, context.Password)");
        tourSource.Should().Contain("new AllowEditRangeCommand(sheet.Id, allowEditRange)");
        tourSource.Should().Contain("EditCellsCommand.ForValue(context.Sheet.Id, context.LockedCell");
        tourSource.Should().Contain("new UnprotectSheetCommand(context.Sheet.Id, \"wrong-password\")");
        tourSource.Should().Contain("SaveWorkbookToTargetAsync(new FileSaveTarget(savedWorkbookPath, adapter))");
        tourSource.Should().Contain("OpenFileAsync(savedWorkbookPath)");
        tourSource.Should().Contain("PlannedCaptures:");
        tourSource.Should().Contain("Wrong-password evidence is recorded as an UnprotectSheetCommand failure outcome");
        tourSource.Should().Contain("Permissions button behavior in Allow Edit Ranges remains disabled/guarded");
        tourSource.Should().Contain("RibbonScreenshotTourManifestJsonContext.Default.ReviewProtectionMatrixTourManifest");

        tourSource.Should().Contain("freex_review_protection_matrix_protect_sheet_permissions");
        tourSource.Should().Contain("freex_review_protection_matrix_protected_disabled_state");
        tourSource.Should().Contain("freex_review_protection_matrix_locked_cell_blocked");
        tourSource.Should().Contain("freex_review_protection_matrix_unlocked_cell_allowed");
        tourSource.Should().Contain("freex_review_protection_matrix_allow_range_allowed");
        tourSource.Should().Contain("freex_review_protection_matrix_unprotect_password_dialog");
        tourSource.Should().Contain("freex_review_protection_matrix_after_unprotect");
        tourSource.Should().Contain("freex_review_protection_matrix_protect_workbook_structure");
        tourSource.Should().Contain("freex_review_protection_matrix_reopened_persistence");

        catalog.Should().Contain("FREEX_REVIEW_PROTECTION_MATRIX_TOUR=1");
        catalog.Should().Contain("screenshots/review-protection-matrix-tour/");
        catalog.Should().Contain("review_protection_matrix_tour_manifest.json");
        catalog.Should().Contain("freex_review_protection_matrix_protected_disabled_state.png");
        catalog.Should().Contain("freex_review_protection_matrix_reopened_persistence.png");
    }
}
