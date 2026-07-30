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

    public static NameBoxDropdownPairContractResult Validate(
        CaptureManifest windows,
        CaptureManifest linux,
        string? windowsDirectory,
        string? linuxDirectory)
    {
        var failures = new List<string>();
        ValidateSide("Windows", windows, windowsDirectory, failures);
        ValidateSide("Linux", linux, linuxDirectory, failures);
        return new NameBoxDropdownPairContractResult(failures.Count == 0, failures);
    }

    private static void ValidateSide(
        string side,
        CaptureManifest manifest,
        string? directory,
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

public sealed record NameBoxDropdownPairContractResult(
    bool IsValid,
    IReadOnlyList<string> Failures);
