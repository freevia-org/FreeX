using FreeW.App.Presentation.Dialogs;

namespace FreeW.App.Presentation.Tests;

public sealed class ScreenClipWorkflowCoordinatorTests
{
    private readonly ScreenClipWorkflowCoordinator _coordinator = new();

    [Fact]
    public void ExecuteBuildsAndInsertsImageBeforeReportingSuccess()
    {
        var calls = new List<string>();
        var capture = new ScreenClipCapture([137, 80, 78, 71], 1600, 900);
        InlineImage? inserted = null;

        var result = _coordinator.Execute(
            () =>
            {
                calls.Add("capture");
                return capture;
            },
            image =>
            {
                calls.Add("insert");
                inserted = image;
            });

        calls.Should().Equal("capture", "insert");
        result.Should().Be(new ScreenClipWorkflowResult(
            ScreenClipWorkflowOutcome.Inserted,
            1600,
            900));
        inserted.Should().NotBeNull();
        inserted!.Bytes.Should().BeSameAs(capture.PngBytes);
        inserted.WidthPt.Should().Be(400);
        inserted.HeightPt.Should().Be(225);
    }

    [Fact]
    public void ExecuteTreatsMissingCaptureAndCancellationAsNoOp()
    {
        var insertions = 0;

        var missing = _coordinator.Execute(
            () => null,
            _ => insertions++);
        var cancelled = _coordinator.Execute(
            () => throw new OperationCanceledException(),
            _ => insertions++);

        missing.Outcome.Should().Be(ScreenClipWorkflowOutcome.Cancelled);
        cancelled.Outcome.Should().Be(ScreenClipWorkflowOutcome.Cancelled);
        insertions.Should().Be(0);
    }

    [Fact]
    public void ExecuteMapsCaptureValidationAndInsertionFailuresWithoutThrowing()
    {
        var invalid = _coordinator.Execute(
            () => new ScreenClipCapture([], 10, 10),
            _ => throw new InvalidOperationException("must not insert"));
        var captureFailure = _coordinator.Execute(
            () => throw new InvalidOperationException("capture failed"),
            _ => throw new InvalidOperationException("must not insert"));
        var insertionFailure = _coordinator.Execute(
            () => new ScreenClipCapture([1], 10, 10),
            _ => throw new InvalidOperationException("insert failed"));

        invalid.Outcome.Should().Be(ScreenClipWorkflowOutcome.Failed);
        invalid.FailureMessage.Should().Contain("Screenshot bytes are empty.");
        captureFailure.Should().Be(new ScreenClipWorkflowResult(
            ScreenClipWorkflowOutcome.Failed,
            FailureMessage: "capture failed"));
        insertionFailure.Should().Be(new ScreenClipWorkflowResult(
            ScreenClipWorkflowOutcome.Failed,
            FailureMessage: "insert failed"));
    }

    [Fact]
    public async Task ExecuteAsyncPropagatesTokenAndReportsInsertionMetadata()
    {
        using var cancellation = new CancellationTokenSource();
        CancellationToken observedToken = default;

        var result = await _coordinator.ExecuteAsync(
            cancellationToken =>
            {
                observedToken = cancellationToken;
                return Task.FromResult<ScreenClipCapture?>(
                    new ScreenClipCapture([1], 96, 48));
            },
            _ => { },
            cancellation.Token);

        observedToken.Should().Be(cancellation.Token);
        result.Should().Be(new ScreenClipWorkflowResult(
            ScreenClipWorkflowOutcome.Inserted,
            96,
            48));
    }

    [Fact]
    public async Task ExecuteAsyncDoesNotCaptureWhenAlreadyCancelled()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var captured = false;

        var result = await _coordinator.ExecuteAsync(
            _ =>
            {
                captured = true;
                return Task.FromResult<ScreenClipCapture?>(null);
            },
            _ => throw new InvalidOperationException("must not insert"),
            cancellation.Token);

        result.Outcome.Should().Be(ScreenClipWorkflowOutcome.Cancelled);
        captured.Should().BeFalse();
    }
}
