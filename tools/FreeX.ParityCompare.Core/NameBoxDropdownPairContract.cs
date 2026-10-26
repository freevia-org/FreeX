using System.Text.Json;

namespace FreeX.ParityCompare.Core;

/// <summary>
/// Fail-closed contract for the Wave69 open Name Box popup pair. Unlike the visual diff threshold, this
/// contract rejects missing captures, wrong surface classification, and clipped/mismatched PNG dimensions.
/// </summary>
public static class NameBoxDropdownPairContract
{
    public const string SurfaceId = "popup.nameBoxDropdown";
    public const string Kind = "overlay";
    public const int Width = 208;
    public const int Height = 136;
    public const string WindowsProvenance = "wpf-production-popup-render-target";
    public const string LinuxProvenance = "native-x11-root-crop";

    public static NameBoxDropdownPairContractResult Validate(
        CaptureManifest windows,
        CaptureManifest linux,
        string? windowsDirectory,
        string? linuxDirectory)
    {
        var failures = new List<string>();
        ValidateSide("Windows", windows, windowsDirectory, WindowsProvenance, requireNativeSources: false, failures);
        ValidateSide("Linux", linux, linuxDirectory, LinuxProvenance, requireNativeSources: true, failures);
        return new NameBoxDropdownPairContractResult(failures.Count == 0, failures);
    }

    private static void ValidateSide(
        string side,
        CaptureManifest manifest,
        string? directory,
        string expectedProvenance,
        bool requireNativeSources,
        List<string> failures)
    {
        var surfaces = manifest.Surfaces
            .Where(surface => string.Equals(surface.Id, SurfaceId, StringComparison.Ordinal))
            .ToList();
        if (surfaces.Count != 1)
        {
            failures.Add($"{side}: expected exactly one '{SurfaceId}' surface, found {surfaces.Count}.");
            return;
        }

        var surface = surfaces[0];
        if (!surface.Captured)
            failures.Add($"{side}: '{SurfaceId}' is marked captured:false ({surface.Note ?? "no note"}).");
        if (!string.Equals(surface.Kind, Kind, StringComparison.OrdinalIgnoreCase))
            failures.Add($"{side}: '{SurfaceId}' kind must be '{Kind}', was '{surface.Kind ?? "missing"}'.");
        if (surface.Width != Width || surface.Height != Height)
            failures.Add($"{side}: '{SurfaceId}' manifest dimensions must be {Width}x{Height}, were {surface.Width?.ToString() ?? "missing"}x{surface.Height?.ToString() ?? "missing"}.");
        if (!string.Equals(surface.EvidenceProvenance, expectedProvenance, StringComparison.Ordinal))
        {
            failures.Add(
                $"{side}: '{SurfaceId}' evidence provenance must be '{expectedProvenance}', " +
                $"was '{surface.EvidenceProvenance ?? "missing"}'; managed/synthetic popup evidence is non-authoritative.");
        }
        if (requireNativeSources)
            ValidateNativeSources(surface, directory, failures);

        if (surface.Png is not { Length: > 0 })
        {
            failures.Add($"{side}: '{SurfaceId}' has no PNG path.");
            return;
        }

        var path = Path.IsPathRooted(surface.Png) || directory is null
            ? surface.Png
            : Path.Combine(directory, surface.Png);
        if (!File.Exists(path))
        {
            failures.Add($"{side}: popup PNG does not exist at '{path}'.");
            return;
        }

        try
        {
            var image = PngCodec.DecodeFile(path);
            if (image.Width != Width || image.Height != Height)
                failures.Add($"{side}: popup PNG pixels must be {Width}x{Height}, were {image.Width}x{image.Height}.");
            else if (!HasVisiblePopupPixels(image))
                failures.Add($"{side}: popup PNG is uniformly white/transparent and contains no rendered popup chrome or text.");
        }
        catch (Exception ex)
        {
            failures.Add($"{side}: popup PNG could not be decoded ({ex.GetType().Name}: {ex.Message}).");
        }
    }

    private static void ValidateNativeSources(
        CapturedSurface surface,
        string? directory,
        List<string> failures)
    {
        var sourcePath = ResolveRelativeEvidencePath(
            "Linux",
            "source PNG",
            surface.SourcePng,
            directory,
            failures);
        var geometryPath = ResolveRelativeEvidencePath(
            "Linux",
            "geometry evidence",
            surface.GeometryEvidence,
            directory,
            failures);

        if (surface.SourceX is null || surface.SourceY is null ||
            surface.SourceWidth is null || surface.SourceHeight is null)
        {
            failures.Add("Linux: native popup source bounds are incomplete.");
        }
        else if (surface.SourceX < 0 || surface.SourceY < 0 ||
                 surface.SourceWidth < Width || surface.SourceHeight < Height)
        {
            failures.Add(
                $"Linux: native popup source bounds must contain the {Width}x{Height} crop, " +
                $"were ({surface.SourceX},{surface.SourceY}) {surface.SourceWidth}x{surface.SourceHeight}.");
        }

        if (sourcePath is not null && !File.Exists(sourcePath))
            failures.Add($"Linux: native popup root screenshot does not exist at '{sourcePath}'.");
        else if (sourcePath is not null &&
                 surface.SourceX is not null && surface.SourceY is not null &&
                 surface.SourceWidth is not null && surface.SourceHeight is not null)
            ValidateRootCropPixels(surface, directory, sourcePath, failures);
        if (geometryPath is null || !File.Exists(geometryPath))
        {
            if (geometryPath is not null)
                failures.Add($"Linux: native popup geometry evidence does not exist at '{geometryPath}'.");
            return;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(geometryPath));
            var root = document.RootElement;
            var source = root.GetProperty("sourceWindow");
            var crop = root.GetProperty("crop");
            var windowId = source.GetProperty("id").GetString();
            if (root.GetProperty("schemaVersion").GetInt32() != 1 ||
                root.GetProperty("captured").ValueKind != JsonValueKind.True ||
                root.GetProperty("surfaceId").GetString() != SurfaceId ||
                root.GetProperty("evidenceProvenance").GetString() != LinuxProvenance ||
                root.GetProperty("sourcePng").GetString() != surface.SourcePng ||
                source.GetProperty("x").GetInt32() != surface.SourceX ||
                source.GetProperty("y").GetInt32() != surface.SourceY ||
                source.GetProperty("width").GetInt32() != surface.SourceWidth ||
                source.GetProperty("height").GetInt32() != surface.SourceHeight ||
                crop.GetProperty("x").GetInt32() != surface.SourceX ||
                crop.GetProperty("y").GetInt32() != surface.SourceY ||
                crop.GetProperty("width").GetInt32() != Width ||
                crop.GetProperty("height").GetInt32() != Height)
            {
                failures.Add("Linux: native popup geometry evidence does not match the manifest and fixed crop contract.");
            }

            var beforeInventory = root.GetProperty("windowInventoryBefore").GetString();
            var openInventory = root.GetProperty("windowInventoryOpen").GetString();
            var beforePath = ResolveRelativeEvidencePath(
                "Linux", "before-window inventory", beforeInventory, directory, failures);
            var openPath = ResolveRelativeEvidencePath(
                "Linux", "open-window inventory", openInventory, directory, failures);
            if (string.IsNullOrWhiteSpace(windowId))
            {
                failures.Add("Linux: native popup geometry evidence has no X11 window id.");
            }
            else
            {
                if (beforePath is null || !File.Exists(beforePath))
                {
                    failures.Add("Linux: before-window X11 inventory is missing.");
                }
                else if (InventoryContainsWindow(beforePath, windowId, out _))
                {
                    failures.Add($"Linux: popup X11 window '{windowId}' was already present before the popup opened.");
                }
                if (openPath is null || !File.Exists(openPath))
                {
                    failures.Add("Linux: open-window X11 inventory is missing.");
                }
                else if (!InventoryContainsWindow(openPath, windowId, out var openLine) ||
                         !openLine.Contains($"X={surface.SourceX} ", StringComparison.Ordinal) ||
                         !openLine.Contains($"Y={surface.SourceY} ", StringComparison.Ordinal) ||
                         !openLine.Contains($"WIDTH={surface.SourceWidth} ", StringComparison.Ordinal) ||
                         !openLine.Contains($"HEIGHT={surface.SourceHeight} ", StringComparison.Ordinal))
                {
                    failures.Add("Linux: popup X11 window is absent from the open-window inventory or its geometry differs.");
                }
            }
        }
        catch (Exception ex)
        {
            failures.Add($"Linux: native popup geometry evidence could not be validated ({ex.GetType().Name}: {ex.Message}).");
        }
    }

    private static void ValidateRootCropPixels(
        CapturedSurface surface,
        string? directory,
        string sourcePath,
        List<string> failures)
    {
        var cropPath = ResolveRelativeEvidencePath(
            "Linux", "popup PNG", surface.Png, directory, failures);
        if (cropPath is null || !File.Exists(cropPath))
            return;

        try
        {
            var source = PngCodec.DecodeFile(sourcePath);
            var crop = PngCodec.DecodeFile(cropPath);
            var sourceX = surface.SourceX!.Value;
            var sourceY = surface.SourceY!.Value;
            if (sourceX + Width > source.Width || sourceY + Height > source.Height)
            {
                failures.Add(
                    $"Linux: native crop bounds exceed the {source.Width}x{source.Height} root screenshot.");
                return;
            }
            if (crop.Width != Width || crop.Height != Height)
                return;

            for (var y = 0; y < Height; y++)
            {
                var sourceOffset = ((sourceY + y) * source.Width + sourceX) * 4;
                var cropOffset = y * crop.Width * 4;
                if (!source.Pixels.AsSpan(sourceOffset, Width * 4)
                        .SequenceEqual(crop.Pixels.AsSpan(cropOffset, Width * 4)))
                {
                    failures.Add(
                        "Linux: popup PNG pixels do not exactly match the declared native root-screenshot crop.");
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            failures.Add($"Linux: native root/crop provenance could not be decoded ({ex.GetType().Name}: {ex.Message}).");
        }
    }

    private static bool InventoryContainsWindow(string path, string windowId, out string line)
    {
        var prefix = windowId + "|";
        line = File.ReadLines(path)
            .FirstOrDefault(candidate => candidate.StartsWith(prefix, StringComparison.Ordinal)) ?? "";
        return line.Length > 0;
    }

    private static string? ResolveRelativeEvidencePath(
        string side,
        string label,
        string? relativePath,
        string? directory,
        List<string> failures)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            failures.Add($"{side}: '{SurfaceId}' has no {label} path.");
            return null;
        }
        if (directory is null || Path.IsPathRooted(relativePath))
        {
            failures.Add($"{side}: '{SurfaceId}' {label} must be relative to a capture directory.");
            return null;
        }

        var root = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var candidate = Path.GetFullPath(Path.Combine(root, relativePath));
        if (!candidate.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            failures.Add($"{side}: '{SurfaceId}' {label} escapes its capture directory.");
            return null;
        }

        return candidate;
    }

    private static bool HasVisiblePopupPixels(PixelImage image)
    {
        for (var i = 0; i < image.Pixels.Length; i += 4)
        {
            if (image.Pixels[i + 3] != 0 &&
                (image.Pixels[i] != 255 || image.Pixels[i + 1] != 255 || image.Pixels[i + 2] != 255))
                return true;
        }

        return false;
    }
}

/// <summary>
/// r380: <paramref name="WasEvaluated"/> distinguishes "the contract ran and passed" from "the
/// contract never ran". A single-side run (--win-only/--linux-only) cannot evaluate a PAIR
/// contract at all, and used to be reported as a plain PASS -- a gate announcing success for work
/// it did not do, which is the same shape as the assertions r353 found and the backend probe r360
/// fixed. Defaults to true so the real Validate path is unchanged.
/// </summary>
public sealed record NameBoxDropdownPairContractResult(
    bool IsValid,
    IReadOnlyList<string> Failures,
    bool WasEvaluated = true);
