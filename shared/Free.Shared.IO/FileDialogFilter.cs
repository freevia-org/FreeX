namespace Free.Shared.IO;

/// <summary>
/// Describes one file format choice shown in an open/save dialog.
/// </summary>
/// <param name="Label">The human-readable group label, e.g. <c>"Word documents"</c>.</param>
/// <param name="Extension">The extension with leading dot, e.g. <c>".docx"</c>.</param>
public sealed record FileFormatChoice(string Label, string Extension)
{
    public string Label { get; init; } = Normalize(Label, nameof(Label));

    public string Extension { get; init; } = NormalizeExtension(Extension);

    private static string Normalize(string value, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, name);
        return value.Trim();
    }

    private static string NormalizeExtension(string extension)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(extension);
        return FileDialogFilterBuilder.NormalizeExtension(extension);
    }

    /// <summary>The wildcard pattern for this choice, e.g. <c>"*.docx"</c>.</summary>
    public string Pattern => "*" + Extension;
}

/// <summary>
/// Composes Windows-style <c>"Label (*.ext)|*.ext|...|All files (*.*)|*.*"</c> filter strings and
/// the matching default extension from a list of <see cref="FileFormatChoice"/>.
/// </summary>
public static class FileDialogFilter
{
    /// <summary>The trailing "all files" filter segment appended to open/save filters.</summary>
    public const string AllFilesSegment = FileDialogFilterBuilder.AllFilesFilterEntry;

    /// <summary>
    /// Builds a dialog filter string from the given format choices, appending an "All files" segment.
    /// </summary>
    public static string Build(IReadOnlyList<FileFormatChoice> choices, bool includeAllFiles = true)
    {
        ArgumentNullException.ThrowIfNull(choices);
        return FileDialogFilterBuilder.BuildPerFormatFilter(ToSharedDescriptors(choices), includeAllFiles);
    }

    /// <summary>The default extension to seed the dialog with: the first choice's extension, or "".</summary>
    public static string DefaultExtension(IReadOnlyList<FileFormatChoice> choices)
    {
        ArgumentNullException.ThrowIfNull(choices);
        return FileDialogFilterBuilder.GetDefaultExtension(ToSharedDescriptors(choices));
    }

    private static IEnumerable<FileDialogFormatDescriptor> ToSharedDescriptors(IEnumerable<FileFormatChoice> choices) =>
        choices.Select(choice => new FileDialogFormatDescriptor(choice.Extension, choice.Label));
}
