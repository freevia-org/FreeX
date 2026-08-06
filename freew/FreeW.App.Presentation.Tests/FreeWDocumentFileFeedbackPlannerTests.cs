using FreeW.App.Presentation.Shell;

namespace FreeW.App.Presentation.Tests;

public sealed class FreeWDocumentFileFeedbackPlannerTests
{
    [Fact]
    public void OpenUnsupported_ProducesReusableMessageAndDialogPlan()
    {
        var execution = new DocumentOpenWorkflowResult(DocumentFileExecutionOutcome.UnsupportedFormat);

        var feedback = FreeWDocumentFileFeedbackPlanner.PlanOpen(execution, "document.zzz");

        feedback.Succeeded.Should().BeFalse();
        feedback.Message.Should().Contain(".zzz");
        feedback.ErrorSummary.Should().Be("Unrecognized file type");
        feedback.ShouldShowError.Should().BeTrue();
    }

    [Fact]
    public void ImportFailure_UsesSameDetailForStatusAndNativeDialog()
    {
        var error = new IOException("locked");
        var execution = new DocumentImportWorkflowResult(
            DocumentFileExecutionOutcome.Failed,
            Exception: error);

        var feedback = FreeWDocumentFileFeedbackPlanner.PlanImport(execution, "source.pdf");

        feedback.Message.Should().Contain("locked");
        feedback.ErrorSummary.Should().Be("Could not import PDF text");
        feedback.Exception.Should().BeSameAs(error);
    }

    [Fact]
    public void SaveCopySuccess_LabelsCopyWithoutChangingRendererState()
    {
        var execution = new DocumentSaveWorkflowResult(
            DocumentFileExecutionOutcome.Succeeded,
            Target: null);

        var feedback = FreeWDocumentFileFeedbackPlanner.PlanSave(
            execution,
            DocumentSaveExecutionKind.SaveCopy,
            "copy.docx");

        feedback.Succeeded.Should().BeTrue();
        feedback.Message.Should().Contain("copy.docx").And.EndWith("(copy)");
    }

    [Fact]
    public void SaveCompatibilityDeclined_DoesNotRequestErrorDialog()
    {
        var execution = new DocumentSaveWorkflowResult(
            DocumentFileExecutionOutcome.CompatibilityDeclined);

        var feedback = FreeWDocumentFileFeedbackPlanner.PlanSave(
            execution,
            DocumentSaveExecutionKind.Save,
            "document.docx");

        feedback.Message.Should().Be("Save canceled.");
        feedback.ShouldShowError.Should().BeFalse();
    }

    [Fact]
    public void CurrentReadOnlyTarget_RequestsSaveAsWithoutError()
    {
        var execution = new DocumentSaveWorkflowResult(DocumentFileExecutionOutcome.SaveAsRequired);

        var feedback = FreeWDocumentFileFeedbackPlanner.PlanSave(
            execution,
            DocumentSaveExecutionKind.Save,
            "legacy.doc");

        feedback.RequiresSaveAs.Should().BeTrue();
        feedback.ShouldShowError.Should().BeFalse();
    }
}
