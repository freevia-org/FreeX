namespace FreeP.App.Compositor;

/// <summary>
/// Applies shared media playback commands and forwards resulting visual snapshots
/// to the native media surface.
/// </summary>
public sealed class SlideShowMediaPlaybackCommandCoordinator
{
    private readonly Action<SlideShowMediaPlaybackSnapshot> _applySnapshot;

    public SlideShowMediaPlaybackCommandCoordinator(
        Action<SlideShowMediaPlaybackSnapshot> applySnapshot,
        SlideShowMediaPlaybackSession? session = null)
    {
        _applySnapshot = applySnapshot ?? throw new ArgumentNullException(nameof(applySnapshot));
        Session = session ?? new SlideShowMediaPlaybackSession();
    }

    public SlideShowMediaPlaybackSession Session { get; }

    public bool TrySeek(uint shapeId, TimeSpan position) =>
        Apply(Session.TrySeek(shapeId, position, out var snapshot), snapshot);

    public bool TrySeekToBookmark(uint shapeId, string bookmarkName) =>
        Apply(Session.TrySeekToBookmark(shapeId, bookmarkName, out var snapshot), snapshot);

    public bool TrySetVolume(uint shapeId, int volumePercent) =>
        Apply(Session.TrySetVolume(shapeId, volumePercent, out var snapshot), snapshot);

    public void EnforcePlaybackState()
    {
        foreach (var snapshot in Session.EnforcePlaybackState())
            _applySnapshot(snapshot);
    }

    private bool Apply(bool result, SlideShowMediaPlaybackSnapshot? snapshot)
    {
        if (snapshot is not null)
            _applySnapshot(snapshot);
        return result;
    }
}
