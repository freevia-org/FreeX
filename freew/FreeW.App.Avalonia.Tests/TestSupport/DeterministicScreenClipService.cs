using Avalonia.Controls;
using FreeW.App.Avalonia.Editing;
using FreeW.App.Presentation.Dialogs;

namespace FreeW.App.Avalonia.Tests.TestSupport;

internal sealed class DeterministicScreenClipService(ScreenClipCapture? capture) : IScreenClipService
{
    public int CallCount { get; private set; }

    public Task<ScreenClipCapture?> CaptureAsync(
        Window owner,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(owner);
        cancellationToken.ThrowIfCancellationRequested();
        CallCount++;
        return Task.FromResult(capture);
    }
}
