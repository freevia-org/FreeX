using FreeW.Core.Model;

namespace FreeW.App.Presentation.Dialogs;

public sealed record ScreenClipCapture(
    byte[] PngBytes,
    int PixelWidth,
    int PixelHeight);

public enum ScreenClipWorkflowOutcome
{
    Cancelled,
    Inserted,
    Failed,
}

public sealed record ScreenClipWorkflowResult(
    ScreenClipWorkflowOutcome Outcome,
    int PixelWidth = 0,
    int PixelHeight = 0,
    string? FailureMessage = null);

/// <summary>
/// Owns the renderer-neutral capture-to-insertion lifecycle while hosts retain native capture,
/// focus, window lifetime, and feedback presentation.
/// </summary>
public sealed class ScreenClipWorkflowCoordinator
{
    public ScreenClipWorkflowResult Execute(
        Func<ScreenClipCapture?> capture,
        Action<InlineImage> insert)
    {
        ArgumentNullException.ThrowIfNull(capture);
        ArgumentNullException.ThrowIfNull(insert);

        try
        {
            return Complete(capture(), insert);
        }
        catch (OperationCanceledException)
        {
            return Cancelled();
        }
        catch (Exception ex)
        {
            return Failed(ex);
        }
    }

    public async Task<ScreenClipWorkflowResult> ExecuteAsync(
        Func<CancellationToken, Task<ScreenClipCapture?>> capture,
        Action<InlineImage> insert,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(capture);
        ArgumentNullException.ThrowIfNull(insert);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var captured = await capture(cancellationToken).ConfigureAwait(true);
            cancellationToken.ThrowIfCancellationRequested();
            return Complete(captured, insert);
        }
        catch (OperationCanceledException)
        {
            return Cancelled();
        }
        catch (Exception ex)
        {
            return Failed(ex);
        }
    }

    private static ScreenClipWorkflowResult Complete(
        ScreenClipCapture? capture,
        Action<InlineImage> insert)
    {
        if (capture is null)
            return Cancelled();

        var image = ScreenClipImageFactory.Create(
            capture.PngBytes,
            capture.PixelWidth,
            capture.PixelHeight);
        insert(image);
        return new ScreenClipWorkflowResult(
            ScreenClipWorkflowOutcome.Inserted,
            capture.PixelWidth,
            capture.PixelHeight);
    }

    private static ScreenClipWorkflowResult Cancelled() =>
        new(ScreenClipWorkflowOutcome.Cancelled);

    private static ScreenClipWorkflowResult Failed(Exception exception) =>
        new(ScreenClipWorkflowOutcome.Failed, FailureMessage: exception.Message);
}
