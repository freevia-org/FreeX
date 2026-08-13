using System.Windows.Controls;
using FreeP.App.Media;

namespace FreeP.App.Host;

internal sealed class WpfMediaPlaybackSourceStore : IMediaPlaybackSourceStore
{
    private readonly ITempMediaFileWriter _fileWriter;
    private readonly Dictionary<Uri, string> _ownedFiles = [];

    public WpfMediaPlaybackSourceStore(ITempMediaFileWriter fileWriter)
    {
        _fileWriter = fileWriter ?? throw new ArgumentNullException(nameof(fileWriter));
    }

    public Uri Materialize(MediaPlaybackSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Uri is not null)
            return source.Uri;

        if (source.EmbeddedBytes is not { Length: > 0 })
            throw new InvalidOperationException("Media playback source has neither a URI nor embedded bytes.");

        var path = _fileWriter.Write(source.EmbeddedBytes, source.ContentType);
        if (!Uri.TryCreate(path, UriKind.Absolute, out var uri))
        {
            _fileWriter.Delete(path);
            throw new UriFormatException("The materialized WPF media path is not an absolute URI.");
        }

        try
        {
            _ownedFiles.Add(uri, path);
            return uri;
        }
        catch
        {
            _fileWriter.Delete(path);
            throw;
        }
    }

    public void Release(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        if (_ownedFiles.Remove(uri, out var path))
            _fileWriter.Delete(path);
    }
}

/// <summary>
/// WPF realization of the portable media playback session. The slideshow host
/// remains responsible for the MediaElement surface and native event routing.
/// </summary>
internal sealed class WpfMediaPlaybackSession : IMediaPlaybackSession
{
    private static readonly MediaPlaybackCapabilities WpfCapabilities = new(
        Audio: true,
        Video: true,
        VideoSurface: true,
        Seek: true,
        Volume: true,
        BackendName: "WPF MediaElement");

    private readonly MediaElement _element;
    private readonly IMediaPlaybackSourceStore _sourceStore;
    private Uri? _sourceUri;
    private TimeSpan? _pendingPosition;
    private MediaPlaybackState _state = MediaPlaybackState.Idle;
    private int _volume = 100;
    private bool _loop;
    private bool _disposed;

    public WpfMediaPlaybackSession(
        MediaElement element,
        IMediaPlaybackSourceStore sourceStore)
    {
        _element = element ?? throw new ArgumentNullException(nameof(element));
        _sourceStore = sourceStore ?? throw new ArgumentNullException(nameof(sourceStore));
    }

    public event EventHandler? Ended;
    public event EventHandler<MediaPlaybackFailure>? Failed;
    public event EventHandler<MediaPlaybackState>? StateChanged;

    public MediaPlaybackCapabilities Capabilities => WpfCapabilities;
    public MediaPlaybackState State => _state;
    public TimeSpan Position => _pendingPosition ?? ReadPosition();
    public TimeSpan Duration => _element.NaturalDuration.HasTimeSpan
        ? _element.NaturalDuration.TimeSpan
        : TimeSpan.Zero;
    public int Volume
    {
        get => _volume;
        set
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _volume = Math.Clamp(value, 0, 100);
            _element.Volume = _volume / 100d;
        }
    }

    public void Open(MediaPlaybackSource source)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(source);

        ReleaseSource(stopPlayback: true);
        try
        {
            _sourceUri = _sourceStore.Materialize(source);
            _loop = source.Loop;
            _element.Source = _sourceUri;
            SetState(MediaPlaybackState.Opening);
        }
        catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException))
        {
            ReleaseSource(stopPlayback: false);
            Fail(MediaPlaybackFailureKind.InvalidSource, "WPF rejected the media source.", ex);
        }
    }

    public void Play()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_sourceUri is null)
        {
            Fail(MediaPlaybackFailureKind.InvalidSource, "No media source is open.");
            return;
        }

        try
        {
            _element.Play();
            SetState(MediaPlaybackState.Playing);
        }
        catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException))
        {
            Fail(MediaPlaybackFailureKind.EngineError, "WPF could not start media playback.", ex);
        }
    }

    public void Pause()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        try
        {
            _element.Pause();
            SetState(MediaPlaybackState.Paused);
        }
        catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException))
        {
            Fail(MediaPlaybackFailureKind.EngineError, "WPF could not pause media playback.", ex);
        }
    }

    public void Stop()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        try
        {
            _element.Stop();
            _pendingPosition = null;
            SetState(MediaPlaybackState.Stopped);
        }
        catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException))
        {
            Fail(MediaPlaybackFailureKind.EngineError, "WPF could not stop media playback.", ex);
        }
    }

    public bool Seek(TimeSpan position)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_sourceUri is null || position < TimeSpan.Zero)
            return false;

        try
        {
            _element.Position = position;
            _pendingPosition = _element.NaturalDuration.HasTimeSpan ? null : position;
            return true;
        }
        catch (InvalidOperationException)
        {
            _pendingPosition = position;
            return false;
        }
        catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException))
        {
            Fail(MediaPlaybackFailureKind.EngineError, "WPF could not seek media playback.", ex);
            return false;
        }
    }

    internal void HandleMediaOpened()
    {
        if (_disposed)
            return;

        if (_pendingPosition is { } position)
        {
            _element.Position = position;
            _pendingPosition = null;
        }

        if (_state == MediaPlaybackState.Opening)
            SetState(MediaPlaybackState.Stopped);
    }

    internal void HandleMediaEnded()
    {
        if (_disposed)
            return;

        if (MediaPlaybackLoopPolicy.ShouldReplay(_loop, _disposed))
        {
            Seek(TimeSpan.Zero);
            Play();
            return;
        }

        SetState(MediaPlaybackState.Ended);
        Ended?.Invoke(this, EventArgs.Empty);
    }

    internal void HandleMediaFailed(Exception? exception)
    {
        if (_disposed)
            return;

        Fail(MediaPlaybackFailureKind.EngineError, "WPF reported a media playback error.", exception);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        try
        {
            _element.Stop();
        }
        catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException))
        {
        }

        _pendingPosition = null;
        SetState(MediaPlaybackState.Stopped);
        ReleaseSource(stopPlayback: false);
        _disposed = true;
    }

    private void ReleaseSource(bool stopPlayback)
    {
        if (stopPlayback && _sourceUri is not null)
        {
            try
            {
                _element.Stop();
                SetState(MediaPlaybackState.Stopped);
            }
            catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException))
            {
            }
        }

        _loop = false;
        _pendingPosition = null;
        var sourceUri = _sourceUri;
        _sourceUri = null;
        if (sourceUri is null)
            return;

        try
        {
            _element.Source = null;
        }
        finally
        {
            _sourceStore.Release(sourceUri);
        }
    }

    private TimeSpan ReadPosition()
    {
        try
        {
            return _element.Position;
        }
        catch (InvalidOperationException)
        {
            return TimeSpan.Zero;
        }
    }

    private void Fail(
        MediaPlaybackFailureKind kind,
        string message,
        Exception? exception = null)
    {
        SetState(MediaPlaybackState.Failed);
        Failed?.Invoke(this, new MediaPlaybackFailure(kind, message, exception));
    }

    private void SetState(MediaPlaybackState state)
    {
        if (_state == state)
            return;

        _state = state;
        StateChanged?.Invoke(this, state);
    }
}
