namespace FreeW.App.Presentation.Tests;

public sealed class FileOperationOutcomeOwnershipSourceTests
{
    [Fact]
    public void Shared_app_services_owns_operation_status_validation_error_and_payload_defaults()
    {
        var shared = ReadSource("shared", "Free.Shared.AppServices", "OperationOutcome.cs");
        var freeW = ReadSource(
            "freew", "FreeW.App.Presentation", "Shell", "DocumentFileExecutionCoordinator.cs");
        var freeP = ReadSource(
            "freep", "FreeP.App.Presentation", "PresentationFileCommandSession.cs");

        shared.Should().Contain("public enum OperationStatus")
            .And.Contain("public sealed record OperationValidation<TDetail>")
            .And.Contain("public sealed record OperationError<TDetail>")
            .And.Contain("public sealed record OperationOutcome<TValue, TValidationDetail, TErrorDetail>")
            .And.NotContain("FreeW")
            .And.NotContain("FreeP");
        freeW.Should().Contain(
                "OperationOutcome<DocumentOpenResult, DocumentFileExecutionOutcome, DocumentFileExecutionOutcome>")
            .And.Contain("public bool Succeeded => Operation.Succeeded;")
            .And.NotContain("new(DocumentFileExecutionOutcome.Succeeded, result, Exception: null)");
        freeP.Should().Contain("public OperationOutcome<string, string, string> Operation { get; }")
            .And.Contain("PresentationFileCommandValidation.FromOperation(Operation.Validation)")
            .And.Contain("PresentationFileCommandError.FromOperation(Operation.Error)")
            .And.NotContain("new(command, PresentationFileCommandStatus.Succeeded");
    }

    [Fact]
    public void Shared_app_services_owns_picker_state_machines()
    {
        var shared = ReadSource("shared", "Free.Shared.AppServices", "PickerOutcome.cs");
        var freeP = ReadSource(
            "freep", "FreeP.App.Presentation", "PresentationFileCommandSession.cs");
        var freeWPicture = ReadSource(
            "freew",
            "FreeW.App.Presentation",
            "DocumentFragments",
            "FreeWPictureImportWorkflow.cs");
        var freeWFragment = ReadSource(
            "freew",
            "FreeW.App.Presentation",
            "DocumentFragments",
            "FreeWDocumentFragmentImportWorkflow.cs");

        shared.Should().Contain("public sealed record PickerOutcome<TSelection>")
            .And.Contain("public OperationStatus Status => Operation.Status;")
            .And.Contain("public static PickerOutcome<TSelection> Cancelled")
            .And.NotContain("FreeW")
            .And.NotContain("FreeP");
        freeP.Should().Contain("public PickerOutcome<string> Outcome { get; }")
            .And.NotContain("enum PresentationFilePickerStatus")
            .And.NotContain("MapPicker(");
        freeWPicture.Should().Contain(
                "public PickerOutcome<FreeWPictureImportSelection> Outcome { get; }")
            .And.NotContain("enum FreeWPictureImportPickerStatus");
        freeWFragment.Should().Contain(
                "public PickerOutcome<FreeWDocumentFragmentImportSelection> Outcome { get; }")
            .And.NotContain("enum FreeWDocumentFragmentPickerStatus");
    }

    private static string ReadSource(params string[] parts)
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        return File.ReadAllText(Path.Combine([root, .. parts]));
    }
}
