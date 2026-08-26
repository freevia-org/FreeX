using System.Globalization;
using Free.Shared.AppServices;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Deterministic clipboard for UI tests whose subject is FreeX behavior rather than Windows OLE.
/// Keeping these tests off the process-global Windows clipboard prevents unrelated test hosts,
/// remote-session clipboard services, and desktop applications from changing their outcome.
/// </summary>
internal sealed class InMemoryPlatformClipboard : IPlatformClipboard
{
    private readonly object _sync = new();
    private PlatformClipboardContent? _content;
    private long _sequence;

    public ValueTask<PlatformClipboardReadResult<PlatformClipboardContent>> ReadAsync(
        PlatformClipboardReadRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_sync)
        {
            if (_content is null)
                return ValueTask.FromResult(PlatformClipboardReadResult<PlatformClipboardContent>.Empty());

            var content = new PlatformClipboardContent(
                Text: request.IncludeText ? _content.Text : null,
                FilePaths: request.IncludeFiles ? _content.FilePaths : [],
                Image: request.IncludeImage ? _content.Image : null,
                CustomData: request.CustomFormats.Count == 0
                    ? []
                    : _content.CustomData.Where(item => request.CustomFormats.Contains(item.Format)).ToArray());

            return ValueTask.FromResult(content.IsEmpty
                ? PlatformClipboardReadResult<PlatformClipboardContent>.Empty()
                : PlatformClipboardReadResult<PlatformClipboardContent>.Success(content));
        }
    }

    public ValueTask<PlatformClipboardWriteResult> WriteAsync(
        PlatformClipboardContent content,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_sync)
        {
            _content = content;
            _sequence++;
        }

        return ValueTask.FromResult(PlatformClipboardWriteResult.Success());
    }

    public ValueTask<PlatformClipboardWriteResult> ClearAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync)
        {
            _content = null;
            _sequence++;
        }

        return ValueTask.FromResult(PlatformClipboardWriteResult.Success());
    }

    public string TryGetChangeIdentity()
    {
        lock (_sync)
            return _sequence.ToString(CultureInfo.InvariantCulture);
    }
}
