namespace Free.Shared.Theme;

/// <summary>
/// Platform-neutral application artwork selected with a theme. Hosts use these names for their
/// window/taskbar icon and package exports instead of owning product-specific file literals.
/// </summary>
public sealed record ThemeVisualAssets(
    string IconSetId,
    string ProductGlyph,
    string WindowsIconFileName,
    string ScalableIconFileName,
    string MacOsIconFileName)
{
    public string WindowsResourcePath => $"Resources/{WindowsIconFileName}";

    public string GetWpfPackUri(string assemblyName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assemblyName);
        return $"pack://application:,,,/{assemblyName};component/{WindowsResourcePath}";
    }
}
