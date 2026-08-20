using System.Collections.Concurrent;
using Free.Shared.AppServices;

namespace Free.Shared.Shell;

/// <summary>
/// Caches filesystem existence probes for recent-file paths so that callers who need to re-check
/// the same path repeatedly (e.g. once per keystroke while filtering the Recent Files list, or once
/// per Backstage Home-pane rebuild) never re-hit the filesystem synchronously on the UI thread.
/// </summary>
/// <remarks>
/// This matters because a recent entry can point at an unreachable UNC/mapped-network path (VPN
/// dropped, NAS off, laptop off the office LAN). A plain <c>File.Exists</c> call against such a path
/// blocks for the SMB/TCP connect timeout (commonly 20+ seconds) before returning false. Calling that
/// synchronously from <see cref="BackstageRecentFileListPlanner.Build"/> on every UI-thread keystroke
/// freezes the whole application window for that long, per character typed.
/// <para/>
/// The first probe for a given path runs on a background thread. Until it completes, the path is
/// optimistically reported as existing — an entry is never hidden merely because it hasn't been
/// checked yet — so callers on the UI thread are never blocked waiting on I/O. Once the background
/// probe completes the result is cached and reused for subsequent calls, so the underlying probe
/// (e.g. <c>File.Exists</c>) runs at most once per path until <see cref="Invalidate"/> is called.
/// </remarks>
public sealed class RecentFilePathExistenceCache
{
    private readonly Func<string, bool> _probe;
    private readonly Action<string>? _onProbed;
    // Must match the identity semantics RecentFilesStore uses to keep recent entries distinct
    // (case-insensitive on Windows, case-sensitive on Linux/macOS) so this cache never collapses
    // two genuinely distinct case-differing paths into one probed/cached result.
    private readonly ConcurrentDictionary<string, bool> _results = new(PlatformPathIdentityComparer.Current);
    private readonly ConcurrentDictionary<string, byte> _inFlight = new(PlatformPathIdentityComparer.Current);

    /// <param name="probe">
    /// The (potentially slow/blocking) existence check to run on a background thread. Defaults to
    /// <see cref="File.Exists(string?)"/>. Overridable for tests so the probe's latency and result can
    /// be controlled deterministically.
    /// </param>
    /// <param name="onProbed">
    /// Optional callback invoked (from the background thread) after a path's real result becomes
    /// known for the first time, so a caller can refresh a previously-rendered list that used the
    /// optimistic default. Never invoked synchronously from <see cref="Exists"/> itself.
    /// </param>
    public RecentFilePathExistenceCache(Func<string, bool>? probe = null, Action<string>? onProbed = null)
    {
        _probe = probe ?? File.Exists;
        _onProbed = onProbed;
    }

    /// <summary>
    /// Non-blocking existence check, directly usable as the <c>pathExists</c> delegate for
    /// <see cref="BackstageRecentFileListPlanner.Build"/>. Never performs filesystem I/O on the
    /// calling thread.
    /// </summary>
    public bool Exists(string path)
    {
        if (_results.TryGetValue(path, out var known))
            return known;

        if (_inFlight.TryAdd(path, 0))
        {
            Task.Run(() =>
            {
                bool exists;
                try
                {
                    exists = _probe(path);
                }
                catch
                {
                    // A probe that throws (e.g. an inaccessible/malformed path) is treated as "does
                    // not exist" rather than left unresolved, so it doesn't stay optimistically
                    // visible forever and doesn't retry every call.
                    exists = false;
                }

                _results[path] = exists;
                _inFlight.TryRemove(path, out _);
                _onProbed?.Invoke(path);
            });
        }

        return true;
    }

    /// <summary>
    /// Forgets a cached result, so the next <see cref="Exists"/> call for this path re-probes it in
    /// the background instead of returning a stale cached value.
    /// </summary>
    public void Invalidate(string path) => _results.TryRemove(path, out _);
}
