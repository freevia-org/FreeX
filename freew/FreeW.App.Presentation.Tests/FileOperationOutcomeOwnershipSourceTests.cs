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

    private static string ReadSource(params string[] parts)
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        return File.ReadAllText(Path.Combine([root, .. parts]));
    }
}
