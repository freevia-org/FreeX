using Free.Shared.Opc;

namespace FreeP.App.Media;

public enum MediaPlaybackState
{
    Unavailable,
    Idle,
    Opening,
    Playing,
    Paused,
    Stopped,
    Ended,
    Failed,
}

public enum MediaPlaybackFailureKind
{
    NativeLibraryUnavailable,
    InvalidSource,
    EngineError,
}

public sealed record MediaPlaybackCapabilities(
    bool Audio,
    bool Video,
    bool VideoSurface,
    bool Seek,
    bool Volume,
    string BackendName,
    string? UnavailableReason = null);

public sealed record MediaPlaybackFailure(
    MediaPlaybackFailureKind Kind,
    string Message,
    Exception? Exception = null);

public sealed record MediaPlaybackSource(
    Uri? Uri,
    byte[]? EmbeddedBytes,
    string ContentType,
    bool IsVideo,
    bool Loop = false)
{
    public static MediaPlaybackSource FromUri(Uri uri, string contentType, bool isVideo, bool loop = false) =>
        new(uri, null, contentType, isVideo, loop);

    public static MediaPlaybackSource FromBytes(
        byte[] bytes,
        string contentType,
        bool isVideo,
        bool loop = false) =>
        new(null, bytes, contentType, isVideo, loop);
}

public static class MediaPlaybackLoopPolicy
{
    public static bool ShouldReplay(bool loop, bool disposed) => loop && !disposed;
}

public static class MediaPlaybackSourceFactory
{
    public static bool TryCreate(
        byte[]? embeddedBytes,
        string? linkUrl,
        string? contentType,
        bool isVideo,
        out MediaPlaybackSource? source,
        bool loop = false)
    {
        if (embeddedBytes is { Length: > 0 })
        {
            source = MediaPlaybackSource.FromBytes(
                embeddedBytes,
                contentType ?? "application/octet-stream",
                isVideo,
                loop);
            return true;
        }

        if (Uri.TryCreate(linkUrl, UriKind.Absolute, out var uri) &&
            uri.Scheme is "http" or "https")
        {
            source = MediaPlaybackSource.FromUri(
                uri,
                contentType ?? "application/octet-stream",
                isVideo,
                loop);
            return true;
        }

        source = null;
        return false;
    }
}

public interface IMediaPlaybackSourceStore
{
    Uri Materialize(MediaPlaybackSource source);

    void Release(Uri uri);
}

public sealed class TempMediaPlaybackSourceStore : IMediaPlaybackSourceStore
{
    private readonly HashSet<string> _ownedPaths = new(StringComparer.OrdinalIgnoreCase);

    public Uri Materialize(MediaPlaybackSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Uri is not null)
            return source.Uri;

        if (source.EmbeddedBytes is not { Length: > 0 })
            throw new InvalidOperationException("Media playback source has neither a URI nor embedded bytes.");

        var extension = OpcMediaTypes.GetMediaFileExtension(
            source.ContentType,
            OpcMediaExtensionProfile.TemporaryPlaybackMaterialization,
            includeDot: true);
        var path = Path.Combine(Path.GetTempPath(), $"freep_playback_{Guid.NewGuid():N}{extension}");
        File.WriteAllBytes(path, source.EmbeddedBytes);
        _ownedPaths.Add(path);
        return new Uri(path, UriKind.Absolute);
    }

    public void Release(Uri uri)
    {
        if (!uri.IsFile || !_ownedPaths.Remove(uri.LocalPath))
            return;

        try
        {
            File.Delete(uri.LocalPath);
        }
        catch (IOException)
        {
            // The native engine can hold a file briefly after Stop; cleanup is best effort.
        }
        catch (UnauthorizedAccessException)
        {
            // The native engine can hold a file briefly after Stop; cleanup is best effort.
        }
    }

    public void ReleaseAll()
    {
        foreach (var path in _ownedPaths.ToArray())
            Release(new Uri(path, UriKind.Absolute));
    }
}

public interface IMediaPlaybackSession : IDisposable
{
    event EventHandler? Ended;
    event EventHandler<MediaPlaybackFailure>? Failed;
    event EventHandler<MediaPlaybackState>? StateChanged;

    MediaPlaybackCapabilities Capabilities { get; }
    MediaPlaybackState State { get; }
    TimeSpan Position { get; }
    TimeSpan Duration { get; }
    int Volume { get; set; }

    void Open(MediaPlaybackSource source);
    void Play();
    void Pause();
    void Stop();
    bool Seek(TimeSpan position);
}

public interface IMediaPlaybackBackend : IDisposable
{
    MediaPlaybackCapabilities Capabilities { get; }

    IMediaPlaybackSession CreateSession();
}

public sealed record MediaPlaybackBackendAvailability(
    bool IsAvailable,
    MediaPlaybackCapabilities Capabilities,
    string? FailureReason = null);

public interface IMediaPlaybackBackendFactory
{
    MediaPlaybackBackendAvailability Probe();

    bool TryCreate(out IMediaPlaybackBackend? backend, out MediaPlaybackFailure? failure);
}
