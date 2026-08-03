namespace FreeX.ParityCompare.Core;

/// <summary>Presence of a surface in a given shell's capture.</summary>
public enum SurfacePresence
{
    Both,
    WindowsOnly,
    LinuxOnly,
}

/// <summary>
/// How a surface's visual diff should be interpreted.
/// <c>grid.*</c> surfaces are a hard fidelity metric (both shells render the same document
/// model the same way) — both production capture paths (WPF <c>ParityCapture.cs</c> and Avalonia
/// <c>MainWindow.ParityCapture.cs</c>) tag these with the literal kind <c>"screen"</c> (a
/// whole-window screenshot of the live shell over the seeded demo workbook), so kind
/// <c>"screen"</c> is also Hard — it is content-bearing, not chrome. <c>tab.*</c> and
/// <c>backstage.*</c> are chrome — expected to differ between WPF and Avalonia by design
/// (different title-bar, Linux compact toolbar row, backstage rail vs dialog layout) — compared
/// and shown, never gate-failing. Dialog surfaces are informational: diff is shown but not
/// gate-failing either.
/// </summary>
public enum DiffSeverity
{
    /// <summary>grid.* (kind "grid" or "screen") — diff is a real fidelity signal; exceeding the threshold fails the gate.</summary>
    Hard,
    /// <summary>tab.* and backstage.* — chrome differences expected by design; informational only.</summary>
    Chrome,
    /// <summary>dialog.* and other non-grid surfaces — diff shown for reference, never gate-failing.</summary>
    Informational,
}

/// <summary>Result of comparing one surface id across the two shells.</summary>
public sealed class SurfaceComparison
{
    public required string Id { get; init; }
    public required string Kind { get; init; }
    public SurfacePresence Presence { get; init; }
    public DiffSeverity Severity { get; init; }

    /// <summary>Mean-pixel-diff % when both PNGs were available; null otherwise.</summary>
    public double? DiffPercent { get; init; }

    /// <summary>Windows PNG path copied into the report dir (null if missing).</summary>
    public string? WindowsImage { get; set; }
    /// <summary>Linux PNG path copied into the report dir (null if missing).</summary>
    public string? LinuxImage { get; set; }

    public string? WindowsNote { get; init; }
    public string? LinuxNote { get; init; }

    /// <summary>A non-fatal reason the diff could not be computed (e.g. decode failure).</summary>
    public string? Error { get; set; }

    /// <summary>
    /// True when this surface represents a hard fidelity failure:
    /// <list type="bullet">
    /// <item>a Hard (grid.*) surface whose pixel diff exceeds <paramref name="threshold"/> when present on both shells, OR</item>
    /// <item>a Hard (grid.*) surface that is present on one shell but absent on the other — the most
    ///       severe possible fidelity defect (the grid did not render at all on one platform).</item>
    /// </list>
    /// </summary>
    public bool IsHardRegression(double threshold) =>
        Severity == DiffSeverity.Hard &&
        (
            // Both sides rendered: diff exceeds threshold.
            (Presence == SurfacePresence.Both && DiffPercent is { } d && d > threshold)
            // One side rendered, the other did not: always a hard failure regardless of threshold.
            || Presence == SurfacePresence.WindowsOnly
            || Presence == SurfacePresence.LinuxOnly
        );

    /// <summary>
    /// True when this is a chrome surface whose diff exceeds the informational high-water mark —
    /// worth reviewing but never gate-failing.
    /// </summary>
    public bool IsLargeChromeDiff(double highWaterMark = SurfaceComparer.ChromeHighWaterMark) =>
        Severity == DiffSeverity.Chrome && Presence == SurfacePresence.Both
        && DiffPercent is { } d && d > highWaterMark;
}

/// <summary>
/// Pairs surfaces from two capture manifests by id and classifies presence + severity.
/// Pure logic — no file IO, no image decoding — so it is trivially unit-testable.
/// Actual pixel diffs are filled in by the orchestrator via <see cref="SurfacePair"/>.
/// </summary>
public static class SurfaceComparer
{
    /// <summary>
    /// Default hard-fidelity fail threshold for grid surfaces (mean-pixel-diff %).
    /// Set to 5% rather than 2% because whole-window captures include the outer shell chrome; the
    /// Avalonia shell adds a compact toolbar row that shifts the grid down, contributing ~2–4% to the
    /// whole-window mean-pixel-diff even when cell rendering is pixel-perfect. Genuine cell-rendering
    /// defects are expected to produce diffs well above this band.
    /// </summary>
    public const double DefaultHardThreshold = 5.0;

    /// <summary>
    /// Informational upper bound for chrome surfaces (tab.* / backstage.*). Surfaces above this
    /// level are annotated in the report as a large chrome difference — not gate-failing, but
    /// worth reviewing when the shells are being aligned.
    /// </summary>
    public const double ChromeHighWaterMark = 20.0;

    /// <summary>Derive a surface kind from its id prefix when the manifest omits it.</summary>
    public static string KindOf(CapturedSurface s)
    {
        if (!string.IsNullOrWhiteSpace(s.Kind)) return s.Kind!;
        int dot = s.Id.IndexOf('.');
        return dot > 0 ? s.Id[..dot] : "other";
    }

    public static DiffSeverity SeverityOf(string kind) =>
        kind.Equals("grid", StringComparison.OrdinalIgnoreCase)
            // "screen" is the kind literal both production capture paths (WPF ParityCapture.cs and
            // Avalonia MainWindow.ParityCapture.cs) actually emit for grid.demo/grid.sheetTabsOverflow
            // — the whole-window captures of the live shell over the seeded demo workbook. It is
            // content-bearing, not decorative chrome: it must classify as Hard alongside "grid" or the
            // fidelity gate can never fail on a real grid-rendering regression (see R120 regression test).
            || kind.Equals("screen", StringComparison.OrdinalIgnoreCase)
            ? DiffSeverity.Hard
            : kind.Equals("tab", StringComparison.OrdinalIgnoreCase)
              || kind.Equals("ribbon-tab", StringComparison.OrdinalIgnoreCase)
              || kind.Equals("static-tab", StringComparison.OrdinalIgnoreCase)
              || kind.Equals("contextual-tab", StringComparison.OrdinalIgnoreCase)
              || kind.Equals("overlay", StringComparison.OrdinalIgnoreCase)
              || kind.Equals("backstage", StringComparison.OrdinalIgnoreCase)
                ? DiffSeverity.Chrome
                : DiffSeverity.Informational;

    /// <summary>
    /// Build the (unfilled-diff) comparison rows by pairing the two manifests' surfaces by id.
    /// "Windows" = WPF manifest, "Linux" = Avalonia manifest. A surface counts as present only
    /// when its entry exists AND <see cref="CapturedSurface.Captured"/> is true.
    /// </summary>
    public static IReadOnlyList<SurfacePair> Pair(CaptureManifest windows, CaptureManifest linux)
    {
        var win = Index(windows);
        var lin = Index(linux);

        var allIds = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var k in win.Keys) allIds.Add(k);
        foreach (var k in lin.Keys) allIds.Add(k);

        var result = new List<SurfacePair>(allIds.Count);
        foreach (var id in allIds)
        {
            win.TryGetValue(id, out var w);
            lin.TryGetValue(id, out var l);

            bool wHas = w is { Captured: true };
            bool lHas = l is { Captured: true };

            var presence = (wHas, lHas) switch
            {
                (true, true) => SurfacePresence.Both,
                (true, false) => SurfacePresence.WindowsOnly,
                (false, true) => SurfacePresence.LinuxOnly,
                // neither captured (both entries present but uncaptured) — treat as
                // present-in-both-but-empty so it surfaces in the report rather than vanishing.
                _ => l != null && w == null ? SurfacePresence.LinuxOnly
                   : w != null && l == null ? SurfacePresence.WindowsOnly
                   : SurfacePresence.Both,
            };

            string kind = w is not null ? KindOf(w) : KindOf(l!);

            result.Add(new SurfacePair
            {
                Id = id,
                Kind = kind,
                Severity = SeverityOf(kind),
                Presence = presence,
                Windows = w,
                Linux = l,
            });
        }
        return result;
    }

    private static Dictionary<string, CapturedSurface> Index(CaptureManifest m)
    {
        var d = new Dictionary<string, CapturedSurface>(StringComparer.Ordinal);
        foreach (var s in m.Surfaces)
            if (!string.IsNullOrEmpty(s.Id))
                d[s.Id] = s; // last-wins on duplicate ids
        return d;
    }
}

/// <summary>A paired surface before pixel-diff is computed.</summary>
public sealed class SurfacePair
{
    public required string Id { get; init; }
    public required string Kind { get; init; }
    public DiffSeverity Severity { get; init; }
    public SurfacePresence Presence { get; init; }
    public CapturedSurface? Windows { get; init; }
    public CapturedSurface? Linux { get; init; }
}
