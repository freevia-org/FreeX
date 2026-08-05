using Free.Shared.IO;

namespace FreeW.App.Presentation.Dialogs;

/// <summary>
/// Loads the portable Insert Icon catalog from the shared runtime resource layout.
/// </summary>
public static class IconPickerCatalog
{
    private static readonly StringComparer PathNameComparer = StringComparer.OrdinalIgnoreCase;

    public static IReadOnlyList<IconPickerEntry> LoadFromBaseDirectory(string baseDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);

        return Load(Path.Combine(baseDirectory, "Resources", "ContentIconsSvg"));
    }

    public static IReadOnlyList<IconPickerEntry> Load(string iconRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(iconRoot);

        if (!Directory.Exists(iconRoot))
            return [];

        return Directory.EnumerateDirectories(iconRoot)
            .OrderBy(Path.GetFileName, PathNameComparer)
            .ThenBy(Path.GetFileName, StringComparer.Ordinal)
            .SelectMany(categoryPath => Directory.EnumerateFiles(categoryPath)
                .Where(path => string.Equals(
                    FilePathPolicy.GetExtensionOrEmpty(path),
                    ".svg",
                    StringComparison.OrdinalIgnoreCase))
                .OrderBy(Path.GetFileName, PathNameComparer)
                .ThenBy(Path.GetFileName, StringComparer.Ordinal)
                .Select(path => CreateEntry(categoryPath, path)))
            .ToArray();
    }

    private static IconPickerEntry CreateEntry(string categoryPath, string path)
    {
        var category = TitleCase(Path.GetFileName(categoryPath));
        var name = TitleCase(Path.GetFileNameWithoutExtension(path).Replace('-', ' '));
        return new IconPickerEntry(
            name,
            category,
            $"{name} {category}".ToLowerInvariant(),
            path);
    }

    private static string TitleCase(string value) =>
        string.Join(' ', value.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(word => char.ToUpperInvariant(word[0]) + word[1..]));
}
