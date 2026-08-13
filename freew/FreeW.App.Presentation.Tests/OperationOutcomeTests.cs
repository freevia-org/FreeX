using Free.Shared.AppServices;

namespace FreeW.App.Presentation.Tests;

public sealed class OperationOutcomeTests
{
    [Fact]
    public void Completed_and_cancelled_outcomes_own_common_defaults()
    {
        var exception = new OperationCanceledException("cancelled");
        var completed = OperationOutcome<string, int, string>.Completed("value", "document.bin");
        var cancelled = OperationOutcome<string, int, string>.Cancel(exception: exception);

        completed.Status.Should().Be(OperationStatus.Completed);
        completed.Succeeded.Should().BeTrue();
        completed.Value.Should().Be("value");
        completed.Path.Should().Be("document.bin");
        completed.Validation.Should().BeNull();
        completed.Error.Should().BeNull();
        completed.Exception.Should().BeNull();

        cancelled.Status.Should().Be(OperationStatus.Cancelled);
        cancelled.Cancelled.Should().BeTrue();
        cancelled.Exception.Should().BeSameAs(exception);
        cancelled.Validation.Should().BeNull();
        cancelled.Error.Should().BeNull();
    }

    [Fact]
    public void Validation_and_failure_outcomes_preserve_typed_details()
    {
        var validationException = new InvalidDataException("invalid");
        var failureException = new IOException("failed");
        var invalid = OperationOutcome<string, int, string>.ValidationFailure(
            42,
            "validation summary",
            validationException,
            "message",
            "invalid.bin");
        var failed = OperationOutcome<string, int, string>.Failure(
            "failure summary",
            failureException,
            84,
            "message",
            "failed.bin");

        invalid.Status.Should().Be(OperationStatus.ValidationFailed);
        invalid.Validation!.Detail.Should().Be(42);
        invalid.Error!.Detail.Should().Be("validation summary");
        invalid.Exception.Should().BeSameAs(validationException);
        invalid.Path.Should().Be("invalid.bin");

        failed.Status.Should().Be(OperationStatus.Failed);
        failed.Validation!.Detail.Should().Be(84);
        failed.Error!.Detail.Should().Be("failure summary");
        failed.Exception.Should().BeSameAs(failureException);
        failed.Path.Should().Be("failed.bin");
    }

    [Fact]
    public void Declined_and_unavailable_are_distinct_from_cancellation_and_validation()
    {
        var declined = OperationOutcome<string, string, string>.Decline("declined");
        var unavailable = OperationOutcome<string, string, string>.Unavailable("unavailable");

        declined.Status.Should().Be(OperationStatus.Declined);
        declined.Cancelled.Should().BeFalse();
        declined.Validation.Should().BeNull();
        unavailable.Status.Should().Be(OperationStatus.Unavailable);
        unavailable.Validation.Should().BeNull();
        unavailable.Error.Should().BeNull();
    }
}
