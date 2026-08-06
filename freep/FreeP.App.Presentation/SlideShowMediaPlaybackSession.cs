using FreeP.Core.Model;

namespace FreeP.App.Compositor;

/// <summary>
/// Minimal native playback surface consumed by the shared slideshow media session.
/// Platform adapters continue to own media object creation, source materialization,
/// visual overlays, native events, and disposal.
/// </summary>
public interface IMediaPlaybackPort
{
    bool IsPlaying { get; }
    TimeSpan Position { get; }
    TimeSpan Duration { get; }
    int VolumePercent { set; }

    void Play();
    void Pause();
    void Stop();
    bool Seek(TimeSpan position);
}

public sealed record SlideShowMediaPlaybackSnapshot(
    uint ShapeId,
    bool IsPlaying,
    bool ShowVisual,
    bool UseFullScreen,
    int BaseVolumePercent,
    int EffectiveVolumePercent,
    SlideShowMediaEndAction? EndAction = null);

public sealed class SlideShowMediaPlaybackHandle
{
    internal SlideShowMediaPlaybackHandle(
        uint shapeId,
        MediaInfo media,
        IMediaPlaybackPort port,
        int baseVolumePercent)
    {
        ShapeId = shapeId;
        Media = media;
        Port = port;
        BaseVolumePercent = baseVolumePercent;
        RemainingSlides = Math.Max(1, media.StopAfterSlides);
    }

    public uint ShapeId { get; }
    public MediaInfo Media { get; }
    public IMediaPlaybackPort Port { get; }
    public int BaseVolumePercent { get; internal set; }
    public int RemainingSlides { get; internal set; }
}

public sealed record SlideShowMediaEnterResult(
    bool IsContiguous,
    IReadOnlyList<SlideShowMediaPlaybackHandle> Retained,
    IReadOnlyList<SlideShowMediaPlaybackHandle> Released);

/// <summary>
/// Renderer-neutral slideshow media state. WPF and Avalonia adapt their native
/// players to <see cref="IMediaPlaybackPort"/> and apply returned visual snapshots.
/// </summary>
public sealed class SlideShowMediaPlaybackSession
{
    private readonly List<SlideShowMediaPlaybackHandle> _active = [];
    private int? _activeSlideIndex;

    public IReadOnlyList<SlideShowMediaPlaybackHandle> Active => _active;

    public SlideShowMediaEnterResult EnterSlide(int? presentationSlideIndex)
    {
        var retained = new List<SlideShowMediaPlaybackHandle>();
        var released = new List<SlideShowMediaPlaybackHandle>();
        var continues = _activeSlideIndex is int previous
            && presentationSlideIndex is int current
            && current == previous + 1;

        if (continues)
        {
            for (var index = _active.Count - 1; index >= 0; index--)
            {
                var handle = _active[index];
                if (handle.Media.IsVideo || handle.RemainingSlides <= 1)
                {
                    SafeStop(handle.Port);
                    _active.RemoveAt(index);
                    released.Add(handle);
                    continue;
                }

                handle.RemainingSlides--;
                retained.Add(handle);
            }

            retained.Reverse();
            released.Reverse();
        }
        else
        {
            released.AddRange(ReleaseAll());
        }

        _activeSlideIndex = presentationSlideIndex;
        return new SlideShowMediaEnterResult(continues, retained, released);
    }

    public SlideShowMediaPlaybackHandle Register(
        uint shapeId,
        MediaInfo media,
        IMediaPlaybackPort port)
    {
        ArgumentNullException.ThrowIfNull(media);
        ArgumentNullException.ThrowIfNull(port);

        var handle = new SlideShowMediaPlaybackHandle(
            shapeId,
            media,
            port,
            SlideShowMediaInteractionPlanner.NormalizeVolumePercent(media.VolumePercent));
        _active.Add(handle);
        try
        {
            SeekToTrimStart(handle);
            ApplyFade(handle);
            if (media.PlaybackStartMode == MediaPlaybackStartMode.Automatically)
                StartPlayback(handle);

            return handle;
        }
        catch
        {
            _active.Remove(handle);
            SafeStop(port);
            throw;
        }
    }

    public IReadOnlyList<SlideShowMediaPlaybackHandle> Teardown()
    {
        var released = ReleaseAll();
        _activeSlideIndex = null;
        return released;
    }

    public bool RequiresPeriodicUpdate(SlideShowMediaPlaybackHandle handle)
    {
        ArgumentNullException.ThrowIfNull(handle);
        var media = handle.Media;
        return HasPositiveTiming(media.TrimStartMilliseconds) ||
            HasPositiveTiming(media.TrimEndMilliseconds) ||
            HasPositiveTiming(media.FadeInMilliseconds) ||
            HasPositiveTiming(media.FadeOutMilliseconds);
    }

    public bool TryHandleClick(uint shapeId, out SlideShowMediaPlaybackSnapshot? snapshot)
    {
        var handle = Find(shapeId);
        if (handle is null)
        {
            snapshot = null;
            return false;
        }

        try
        {
            if (handle.Port.IsPlaying)
                handle.Port.Pause();
            else
                StartPlayback(handle);

            snapshot = Snapshot(handle);
            return true;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            snapshot = Snapshot(handle);
            return false;
        }
    }

    public bool TrySeek(
        uint shapeId,
        TimeSpan position,
        out SlideShowMediaPlaybackSnapshot? snapshot)
    {
        var handle = Find(shapeId);
        if (handle is null || position < TimeSpan.Zero)
        {
            snapshot = null;
            return false;
        }

        return TrySeek(handle, position, out snapshot);
    }

    public bool TrySeekToBookmark(
        uint shapeId,
        string bookmarkName,
        out SlideShowMediaPlaybackSnapshot? snapshot)
    {
        var handle = Find(shapeId);
        if (handle is null || !SlideShowMediaInteractionPlanner.TryResolveMediaBookmarkPosition(
                handle.Media,
                bookmarkName,
                handle.Port.Duration,
                out var position))
        {
            snapshot = null;
            return false;
        }

        return TrySeek(handle, position, out snapshot);
    }

    public bool TrySetVolume(
        uint shapeId,
        int volumePercent,
        out SlideShowMediaPlaybackSnapshot? snapshot)
    {
        var handle = Find(shapeId);
        if (handle is null)
        {
            snapshot = null;
            return false;
        }

        handle.BaseVolumePercent = SlideShowMediaInteractionPlanner.NormalizeVolumePercent(volumePercent);
        try
        {
            ApplyFade(handle);
            snapshot = Snapshot(handle);
            return true;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            snapshot = Snapshot(handle);
            return false;
        }
    }

    /// <summary>
    /// Reapplies trim and fade state after a native player reports that metadata is available.
    /// </summary>
    public bool Synchronize(
        SlideShowMediaPlaybackHandle handle,
        out SlideShowMediaPlaybackSnapshot? snapshot)
    {
        ArgumentNullException.ThrowIfNull(handle);
        if (!_active.Contains(handle))
        {
            snapshot = null;
            return false;
        }

        return TrySeek(handle, handle.Port.Position, out snapshot);
    }

    public SlideShowMediaPlaybackSnapshot HandleEnded(SlideShowMediaPlaybackHandle handle)
    {
        ArgumentNullException.ThrowIfNull(handle);
        if (!_active.Contains(handle))
            return Snapshot(handle);

        var action = SlideShowMediaInteractionPlanner.ResolveEndAction(handle.Media);
        try
        {
            switch (action)
            {
                case SlideShowMediaEndAction.Loop:
                    StartPlayback(handle);
                    break;
                case SlideShowMediaEndAction.Rewind:
                    SeekToTrimStart(handle, force: true);
                    ApplyFade(handle);
                    handle.Port.Pause();
                    break;
                default:
                    handle.Port.Pause();
                    break;
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
        }

        return Snapshot(handle, action);
    }

    public IReadOnlyList<SlideShowMediaPlaybackSnapshot> EnforcePlaybackState()
    {
        var updates = new List<SlideShowMediaPlaybackSnapshot>();
        foreach (var handle in _active.ToArray())
        {
            if (!handle.Port.IsPlaying)
                continue;

            try
            {
                ApplyFade(handle);
                if (SlideShowMediaInteractionPlanner.IsAtOrPastTrimEnd(
                        handle.Media,
                        handle.Port.Position,
                        handle.Port.Duration))
                {
                    updates.Add(HandleEnded(handle));
                }
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
            }
        }

        return updates;
    }

    public SlideShowMediaPlaybackSnapshot Snapshot(
        SlideShowMediaPlaybackHandle handle,
        SlideShowMediaEndAction? endAction = null)
    {
        ArgumentNullException.ThrowIfNull(handle);
        var isPlaying = handle.Port.IsPlaying;
        return new SlideShowMediaPlaybackSnapshot(
            handle.ShapeId,
            isPlaying,
            handle.Media.IsVideo && (isPlaying || handle.Media.ShowWhenStopped),
            isPlaying && handle.Media.PlayFullScreen,
            handle.BaseVolumePercent,
            SlideShowMediaInteractionPlanner.ComputeEffectiveVolumePercent(
                handle.Media,
                handle.BaseVolumePercent,
                handle.Port.Position,
                handle.Port.Duration),
            endAction);
    }

    private bool TrySeek(
        SlideShowMediaPlaybackHandle handle,
        TimeSpan position,
        out SlideShowMediaPlaybackSnapshot? snapshot)
    {
        var window = SlideShowMediaInteractionPlanner.ResolveTrimWindow(
            handle.Media,
            handle.Port.Duration);
        var bounded = window.End != TimeSpan.MaxValue && position > window.End
            ? window.End
            : SlideShowMediaInteractionPlanner.ClampToTrimStart(handle.Media, position);

        try
        {
            var didSeek = handle.Port.Seek(bounded);
            if (didSeek)
                ApplyFade(handle);
            snapshot = Snapshot(handle);
            return didSeek;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            snapshot = Snapshot(handle);
            return false;
        }
    }

    private void StartPlayback(SlideShowMediaPlaybackHandle handle)
    {
        SeekToTrimStart(handle);
        ApplyFade(handle);
        handle.Port.Play();
    }

    private static void SeekToTrimStart(SlideShowMediaPlaybackHandle handle, bool force = false)
    {
        var window = SlideShowMediaInteractionPlanner.ResolveTrimWindow(
            handle.Media,
            handle.Port.Duration);
        var position = handle.Port.Position;
        if (force || position < window.Start ||
            (window.End != TimeSpan.MaxValue && position >= window.End))
        {
            handle.Port.Seek(window.Start);
        }
    }

    private static void ApplyFade(SlideShowMediaPlaybackHandle handle)
    {
        handle.Port.VolumePercent = SlideShowMediaInteractionPlanner.ComputeEffectiveVolumePercent(
            handle.Media,
            handle.BaseVolumePercent,
            handle.Port.Position,
            handle.Port.Duration);
    }

    private SlideShowMediaPlaybackHandle? Find(uint shapeId) =>
        _active.FirstOrDefault(candidate => candidate.ShapeId == shapeId);

    private IReadOnlyList<SlideShowMediaPlaybackHandle> ReleaseAll()
    {
        var released = _active.ToArray();
        foreach (var handle in released)
            SafeStop(handle.Port);
        _active.Clear();
        return released;
    }

    private static void SafeStop(IMediaPlaybackPort port)
    {
        try
        {
            port.Stop();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
        }
    }

    private static bool HasPositiveTiming(double value) => value > 0 && double.IsFinite(value);
}
