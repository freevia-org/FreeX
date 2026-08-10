using Free.ToolsShared;

namespace FreeX.ParityCompare.Core;

/// <summary>Top-level result of a parity comparison run, ready for report emission.</summary>
public sealed class ParityComparison
{
    public required string WindowsPlatform { get; init; }
    public required string WindowsShell { get; init; }
    public required string LinuxPlatform { get; init; }
    public required string LinuxShell { get; init; }
    public required double HardThreshold { get; init; }
    public required IReadOnlyList<SurfaceComparison> Surfaces { get; init; }
    public DateTimeOffset GeneratedAt { get; init; } = DateTimeOffset.Now;

    public int TotalSurfaces => Surfaces.Count;
    public int BothCount => Surfaces.Count(s => s.Presence == SurfacePresence.Both);
    public int WindowsOnlyCount => Surfaces.Count(s => s.Presence == SurfacePresence.WindowsOnly);
    public int LinuxOnlyCount => Surfaces.Count(s => s.Presence == SurfacePresence.LinuxOnly);
    public int HardSurfaceCount => Surfaces.Count(s => s.Severity == DiffSeverity.Hard);
    public int ChromeSurfaceCount => Surfaces.Count(s => s.Severity == DiffSeverity.Chrome);
    public int ScreenSurfaceCount => Surfaces.Count(s => s.Kind.Equals("screen", StringComparison.OrdinalIgnoreCase));
    public int StaticTabSurfaceCount => Surfaces.Count(s => s.Kind.Equals("static-tab", StringComparison.OrdinalIgnoreCase));
    public int ContextualTabSurfaceCount => Surfaces.Count(s => s.Kind.Equals("contextual-tab", StringComparison.OrdinalIgnoreCase));
    public int OverlaySurfaceCount => Surfaces.Count(s =>
        s.Kind.Equals("backstage", StringComparison.OrdinalIgnoreCase)
        || s.Kind.Equals("overlay", StringComparison.OrdinalIgnoreCase));
    public int DialogSurfaceCount => Surfaces.Count(s => s.Kind.Equals("dialog", StringComparison.OrdinalIgnoreCase));
    public IReadOnlyList<SurfaceComparison> HardRegressions =>
        Surfaces.Where(s => s.IsHardRegression(HardThreshold)).ToList();
    public IReadOnlyList<SurfaceComparison> LargeChromeDiffs =>
        Surfaces.Where(s => s.IsLargeChromeDiff()).ToList();
    public bool Passed => HardRegressions.Count == 0;
}

/// <summary>
/// Drives a full comparison: pair surfaces, resolve PNG paths against each capture dir,
/// copy both images into the report dir, and compute the mean-pixel-diff for paired surfaces.
/// File IO is injectable so tests can run against an in-memory layout if desired; by default
/// it uses the real filesystem and <see cref="PngCodec"/>.
/// </summary>
public sealed class ParityComparisonEngine
{
    private readonly Func<string, PixelImage> _decode;
    private readonly Action<string, string> _copy;
    private readonly Func<string, bool> _exists;

    public ParityComparisonEngine(
        Func<string, PixelImage>? decode = null,
        Action<string, string>? copy = null,
        Func<string, bool>? exists = null)
    {
        _decode = decode ?? PngCodec.DecodeFile;
        _exists = exists ?? File.Exists;
        _copy = copy ?? ((src, dst) => File.Copy(src, dst, overwrite: true));
    }

    /// <summary>
    /// Compare two captures. <paramref name="winDir"/>/<paramref name="linDir"/> are the capture
    /// directories (containing manifest.json + PNGs). <paramref name="imagesDir"/> is where paired
    /// PNGs are copied (created by the caller); pass null to skip copying (pure-metric mode).
    /// </summary>
    public ParityComparison Compare(
        CaptureManifest windows, CaptureManifest linux,
        string? winDir, string? linDir, string? imagesDir,
        double hardThreshold = SurfaceComparer.DefaultHardThreshold)
    {
        var pairs = SurfaceComparer.Pair(windows, linux);
        var results = new List<SurfaceComparison>(pairs.Count);

        foreach (var pair in pairs)
        {
            var cmp = new SurfaceComparison
            {
                Id = pair.Id,
                Kind = pair.Kind,
                Presence = pair.Presence,
                Severity = pair.Severity,
                WindowsNote = pair.Windows?.Note,
                LinuxNote = pair.Linux?.Note,
            };

            string? winPng = ResolvePng(pair.Windows, winDir);
            string? linPng = ResolvePng(pair.Linux, linDir);

            // Copy the source PNGs into the report image dir (stable per-surface names).
            string safe = SafeName(pair.Id);
            if (imagesDir != null)
            {
                if (winPng != null && _exists(winPng))
                {
                    string dst = Path.Combine(imagesDir, $"{safe}.win.png");
                    TryCopy(winPng, dst, ref cmp); cmp.WindowsImage = dst;
                }
                if (linPng != null && _exists(linPng))
                {
                    string dst = Path.Combine(imagesDir, $"{safe}.lin.png");
                    TryCopy(linPng, dst, ref cmp); cmp.LinuxImage = dst;
                }
            }
            else
            {
                cmp.WindowsImage = winPng != null && _exists(winPng) ? winPng : null;
                cmp.LinuxImage = linPng != null && _exists(linPng) ? linPng : null;
            }

            double? diff = null;
            if (pair.Presence == SurfacePresence.Both
                && winPng != null && linPng != null
                && _exists(winPng) && _exists(linPng))
            {
                try
                {
                    diff = ImageDiff.MeanPixelDiffPercent(_decode(winPng), _decode(linPng));
                }
                catch (Exception ex)
                {
                    cmp.Error = $"diff failed: {ex.Message}";
                }
            }

            results.Add(new SurfaceComparison
            {
                Id = cmp.Id,
                Kind = cmp.Kind,
                Presence = cmp.Presence,
                Severity = cmp.Severity,
                WindowsNote = cmp.WindowsNote,
                LinuxNote = cmp.LinuxNote,
                DiffPercent = diff,
                WindowsImage = cmp.WindowsImage,
                LinuxImage = cmp.LinuxImage,
                Error = cmp.Error,
            });
        }

        return new ParityComparison
        {
            WindowsPlatform = string.IsNullOrEmpty(windows.Platform) ? "windows" : windows.Platform,
            WindowsShell = string.IsNullOrEmpty(windows.Shell) ? "wpf" : windows.Shell,
            LinuxPlatform = string.IsNullOrEmpty(linux.Platform) ? "linux" : linux.Platform,
            LinuxShell = string.IsNullOrEmpty(linux.Shell) ? "avalonia" : linux.Shell,
            HardThreshold = hardThreshold,
            Surfaces = results
                .OrderByDescending(s => s.DiffPercent ?? -1)
                .ThenBy(s => s.Id, StringComparer.Ordinal)
                .ToList(),
        };
    }

    private string? ResolvePng(CapturedSurface? surface, string? dir)
    {
        if (surface?.Png is not { Length: > 0 } png) return null;
        if (Path.IsPathRooted(png)) return png;
        return dir != null ? Path.Combine(dir, png) : png;
    }

    private void TryCopy(string src, string dst, ref SurfaceComparison cmp)
    {
        try { _copy(src, dst); }
        catch (Exception ex) { cmp.Error = $"copy failed: {ex.Message}"; }
    }

    /// <summary>Make a surface id safe for use as a file name.</summary>
    public static string SafeName(string id) =>
        VisualEvidenceTextPolicy.ToAlphaNumericSafeArtifactName(id);
}
