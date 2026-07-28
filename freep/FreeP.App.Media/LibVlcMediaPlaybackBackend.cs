using LibVLCSharp.Shared;

namespace FreeP.App.Media;

public sealed class LibVlcMediaPlaybackBackendFactory : IMediaPlaybackBackendFactory
{
    private readonly Func<LibVLC> _libVlcFactory;
    private readonly Func<bool> _initialize;

    public LibVlcMediaPlaybackBackendFactory(
        Func<LibVLC>? libVlcFactory = null,
        Func<bool>? initialize = null)
    {
        _libVlcFactory = libVlcFactory ?? (() => new LibVLC());
        _initialize = initialize ?? DefaultInitialize;
    }

    public MediaPlaybackBackendAvailability Probe()
    {
        if (!TryCreate(out var backend, out var failure))
        {
            return new MediaPlaybackBackendAvailability(
                false,
                LibVlcMediaPlaybackBackend.UnavailableCapabilities(failure?.Message),
                failure?.Message);
        }

        var capabilities = backend!.Capabilities;
        backend.Dispose();
        return new MediaPlaybackBackendAvailability(true, capabilities);
    }

    public bool TryCreate(out IMediaPlaybackBackend? backend, out MediaPlaybackFailure? failure)
    {
        backend = null;
        failure = null;
        try
        {
            if (!_initialize())
            {
                failure = new MediaPlaybackFailure(
                    MediaPlaybackFailureKind.NativeLibraryUnavailable,
                    "LibVLC native libraries are unavailable on this runtime.");
                return false;
            }

            backend = new LibVlcMediaPlaybackBackend(_libVlcFactory());
            return true;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or TypeInitializationException or InvalidOperationException)
        {
            failure = new MediaPlaybackFailure(
                MediaPlaybackFailureKind.NativeLibraryUnavailable,
                "LibVLC could not be initialized on this runtime.",
                ex);
            return false;
        }
    }

    private static bool DefaultInitialize()
    {
        Core.Initialize();
        return true;
    }
}

public sealed class LibVlcMediaPlaybackBackend : IMediaPlaybackBackend
{
    private readonly LibVLC _libVlc;
    private bool _disposed;

    public LibVlcMediaPlaybackBackend(LibVLC libVlc)
    {
        _libVlc = libVlc ?? throw new ArgumentNullException(nameof(libVlc));
        Capabilities = new MediaPlaybackCapabilities(
            Audio: true,
            Video: true,
            VideoSurface: true,
            Seek: true,
            Volume: true,
            BackendName: "LibVLC");
    }

    public MediaPlaybackCapabilities Capabilities { get; }

    public IMediaPlaybackSession CreateSession()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return new LibVlcMediaPlaybackSession(_libVlc, Capabilities);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _libVlc.Dispose();
    }

    internal static MediaPlaybackCapabilities UnavailableCapabilities(string? reason) =>
        new(false, false, false, false, false, "LibVLC", reason);
}

public sealed class LibVlcMediaPlaybackSession : IMediaPlaybackSession
{
    private readonly LibVLC _libVlc;
    private readonly MediaPlaybackCapabilities _capabilities;
    private readonly TempMediaPlaybackSourceStore _sourceStore = new();
    private MediaPlayer? _player;
    private LibVLCSharp.Shared.Media? _media;
    private Uri? _sourceUri;
    private bool _loop;
    private bool _disposed;
    private MediaPlaybackState _state = MediaPlaybackState.Idle;
    private int _volume = 100;

    public LibVlcMediaPlaybackSession(LibVLC libVlc, MediaPlaybackCapabilities capabilities)
    {
        _libVlc = libVlc ?? throw new ArgumentNullException(nameof(libVlc));
        _capabilities = capabilities;
    }

    public event EventHandler? Ended;
    public event EventHandler<MediaPlaybackFailure>? Failed;
    public event EventHandler<MediaPlaybackState>? StateChanged;

    public MediaPlaybackCapabilities Capabilities => _capabilities;
    public MediaPlaybackState State => _state;
    public TimeSpan Position => TimeSpan.FromMilliseconds(Math.Max(0, _player?.Time ?? 0));
    public TimeSpan Duration => TimeSpan.FromMilliseconds(Math.Max(0, _player?.Length ?? 0));
    public int Volume
    {
        get => _volume;
        set
        {
            _volume = Math.Clamp(value, 0, 100);
            if (_player is not null)
                _player.Volume = _volume;
        }
    }

    public MediaPlayer NativePlayer =>
        _player ?? throw new InvalidOperationException("Open a media source before requesting the native player.");

    public void Open(MediaPlaybackSource source)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(source);
        Stop();
        try
        {
            _sourceUri = _sourceStore.Materialize(source);
            _loop = source.Loop;
            _media = new LibVLCSharp.Shared.Media(_libVlc, _sourceUri);
            _player = new MediaPlayer(_media) { Volume = _volume };
            _player.EndReached += OnEndReached;
            _player.EncounteredError += OnEncounteredError;
            _player.Playing += OnPlaying;
            _player.Paused += OnPaused;
            _player.Stopped += OnStopped;
            SetState(MediaPlaybackState.Opening);
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UriFormatException or InvalidOperationException)
        {
            Fail(MediaPlaybackFailureKind.InvalidSource, "LibVLC rejected the media source.", ex);
        }
    }

    public void Play()
    {
        if (_player is null)
        {
            Fail(MediaPlaybackFailureKind.InvalidSource, "No media source is open.");
            return;
        }

        try
        {
            if (!_player.Play())
                Fail(MediaPlaybackFailureKind.EngineError, "LibVLC could not start media playback.");
        }
        catch (Exception ex)
        {
            Fail(MediaPlaybackFailureKind.EngineError, "LibVLC raised an error while starting playback.", ex);
        }
    }

    public void Pause()
    {
        try { _player?.Pause(); }
        catch (Exception ex) { Fail(MediaPlaybackFailureKind.EngineError, "LibVLC raised an error while pausing playback.", ex); }
    }

    public void Stop()
    {
        try { _player?.Stop(); }
        catch (Exception ex) { Fail(MediaPlaybackFailureKind.EngineError, "LibVLC raised an error while stopping playback.", ex); }
        ReleaseNativeMedia();
        if (!_disposed)
            SetState(MediaPlaybackState.Stopped);
    }

    public bool Seek(TimeSpan position)
    {
        if (_player is null || !_capabilities.Seek)
            return false;

        try
        {
            var milliseconds = Math.Max(0, (long)position.TotalMilliseconds);
            _player.Time = milliseconds;
            return true;
        }
        catch (Exception ex)
        {
            Fail(MediaPlaybackFailureKind.EngineError, "LibVLC raised an error while seeking.", ex);
            return false;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        ReleaseNativeMedia();
        _sourceStore.ReleaseAll();
        SetState(MediaPlaybackState.Stopped);
    }

    private void OnEndReached(object? sender, EventArgs e)
    {
        if (MediaPlaybackLoopPolicy.ShouldReplay(_loop, _disposed))
        {
            try
            {
                SetState(MediaPlaybackState.Opening);
                if (!(_player?.Play() ?? false))
                    Fail(MediaPlaybackFailureKind.EngineError, "LibVLC could not restart looping media playback.");
            }
            catch (Exception ex)
            {
                Fail(MediaPlaybackFailureKind.EngineError, "LibVLC raised an error while restarting looping media playback.", ex);
            }

            return;
        }

        SetState(MediaPlaybackState.Ended);
        Ended?.Invoke(this, EventArgs.Empty);
    }

    private void OnEncounteredError(object? sender, EventArgs e) =>
        Fail(MediaPlaybackFailureKind.EngineError, "LibVLC reported a media playback error.");

    private void OnPlaying(object? sender, EventArgs e) => SetState(MediaPlaybackState.Playing);
    private void OnPaused(object? sender, EventArgs e) => SetState(MediaPlaybackState.Paused);
    private void OnStopped(object? sender, EventArgs e) => SetState(MediaPlaybackState.Stopped);

    private void Fail(MediaPlaybackFailureKind kind, string message, Exception? exception = null)
    {
        SetState(MediaPlaybackState.Failed);
        Failed?.Invoke(this, new MediaPlaybackFailure(kind, message, exception));
    }

    private void SetState(MediaPlaybackState state)
    {
        if (_state == state) return;
        _state = state;
        StateChanged?.Invoke(this, state);
    }

    private void ReleaseNativeMedia()
    {
        _loop = false;
        if (_player is not null)
        {
            _player.EndReached -= OnEndReached;
            _player.EncounteredError -= OnEncounteredError;
            _player.Playing -= OnPlaying;
            _player.Paused -= OnPaused;
            _player.Stopped -= OnStopped;
            _player.Dispose();
            _player = null;
        }

        _media?.Dispose();
        _media = null;
        if (_sourceUri is not null)
            _sourceStore.Release(_sourceUri);
        _sourceUri = null;
    }
}
