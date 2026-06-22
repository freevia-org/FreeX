namespace Free.Shared.IO;

public static class FileDialogFilterBuilder
{
    public const string AllFilesFilterEntry = "All files (*.*)|*.*";

    public static string BuildOpenFilter(
        IEnumerable<FileDialogFormatDescriptor> formats,
        string allSupportedName = "All supported files")
    {
        var openFormats = formats.Where(format => format.CanOpen).ToList();
        return BuildFilter(openFormats, includeAllSupported: true, includeAllFiles: true, allSupportedName);
    }

    public static string BuildSaveFilter(IEnumerable<FileDialogFormatDescriptor> formats)
    {
        var saveFormats = formats.Where(format => format.CanSave).ToList();
        return BuildFilter(saveFormats, includeAllSupported: false, includeAllFiles: false, allSupportedName: "");
    }

    public static string BuildPerFormatFilter(
        IEnumerable<FileDialogFormatDescriptor> formats,
        bool includeAllFiles = true)
    {
        var formatRows = formats.ToList();
        return BuildFilter(formatRows, includeAllSupported: false, includeAllFiles, allSupportedName: "");
    }

    public static IReadOnlyList<FileDialogPickerTypeDescriptor> BuildOpenPickerTypes(
        IEnumerable<FileDialogFormatDescriptor> formats,
        string allSupportedName = "All supported files")
    {
        var openFormats = formats.Where(format => format.CanOpen).ToList();
        return BuildPickerTypes(openFormats, includeAllSupported: true, allSupportedName);
    }

    public static IReadOnlyList<FileDialogPickerTypeDescriptor> BuildSavePickerTypes(
        IEnumerable<FileDialogFormatDescriptor> formats,
        string? preferredFirstExtension = null)
    {
        var saveFormats = formats.Where(format => format.CanSave).ToList();
        PromotePreferredExtension(saveFormats, preferredFirstExtension);
        return BuildPickerTypes(saveFormats, includeAllSupported: false, allSupportedName: "");
    }

    public static int FindSaveFilterIndex(IEnumerable<FileDialogFormatDescriptor> formats, string extension)
    {
        var normalizedExtension = NormalizeExtension(extension);
        if (normalizedExtension.Length == 0)
            return 1;

        var saveFormats = formats.Where(format => format.CanSave).ToList();
        for (var i = 0; i < saveFormats.Count; i++)
        {
            if (string.Equals(
                NormalizeExtension(saveFormats[i].Extension),
                normalizedExtension,
                StringComparison.OrdinalIgnoreCase))
            {
                return i + 1;
            }
        }

        return 1;
    }

    public static string GetDefaultExtension(IEnumerable<FileDialogFormatDescriptor> formats)
    {
        foreach (var format in formats)
            return NormalizeExtension(format.Extension);

        return "";
    }

    public static string NormalizeExtension(string extension)
    {
        extension = extension.Trim();
        if (extension.Length == 0)
            return "";

        if (extension.StartsWith("*.", StringComparison.Ordinal))
            extension = extension[1..];

        return extension.StartsWith(".", StringComparison.Ordinal)
            ? extension
            : $".{extension}";
    }

    private static string BuildFilter(
        IReadOnlyCollection<FileDialogFormatDescriptor> formats,
        bool includeAllSupported,
        bool includeAllFiles,
        string allSupportedName)
    {
        var parts = new List<string>(formats.Count + 2);

        if (includeAllSupported && formats.Count > 0)
            parts.Add(BuildAllSupportedFilterEntry(formats, allSupportedName));

        parts.AddRange(formats.Select(BuildFormatFilterEntry));

        if (includeAllFiles)
            parts.Add(AllFilesFilterEntry);

        return string.Join('|', parts);
    }

    private static IReadOnlyList<FileDialogPickerTypeDescriptor> BuildPickerTypes(
        IReadOnlyCollection<FileDialogFormatDescriptor> formats,
        bool includeAllSupported,
        string allSupportedName)
    {
        var descriptors = new List<FileDialogPickerTypeDescriptor>(formats.Count + 1);

        if (includeAllSupported && formats.Count > 0)
            descriptors.Add(BuildAllSupportedPickerType(formats, allSupportedName));

        descriptors.AddRange(formats.Select(BuildFormatPickerType));
        return descriptors;
    }

    private static string BuildAllSupportedFilterEntry(
        IEnumerable<FileDialogFormatDescriptor> formats,
        string allSupportedName)
    {
        var allSupported = string.Join(';', BuildDistinctPatterns(formats));
        return $"{allSupportedName} ({allSupported})|{allSupported}";
    }

    private static FileDialogPickerTypeDescriptor BuildAllSupportedPickerType(
        IEnumerable<FileDialogFormatDescriptor> formats,
        string allSupportedName) =>
        new(allSupportedName, BuildDistinctPatterns(formats));

    private static string BuildFormatFilterEntry(FileDialogFormatDescriptor format)
    {
        var extension = NormalizeExtension(format.Extension);
        return $"{format.FormatName} (*{extension})|*{extension}";
    }

    private static FileDialogPickerTypeDescriptor BuildFormatPickerType(FileDialogFormatDescriptor format)
    {
        var extension = NormalizeExtension(format.Extension);
        return new FileDialogPickerTypeDescriptor(format.FormatName, [$"*{extension}"]);
    }

    private static IReadOnlyList<string> BuildDistinctPatterns(IEnumerable<FileDialogFormatDescriptor> formats) =>
        formats
            .Select(format => NormalizeExtension(format.Extension))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(extension => $"*{extension}")
            .ToList();

    private static void PromotePreferredExtension(
        List<FileDialogFormatDescriptor> formats,
        string? preferredFirstExtension)
    {
        var normalizedExtension = NormalizeExtension(preferredFirstExtension ?? "");
        if (normalizedExtension.Length == 0)
            return;

        var index = -1;
        for (var i = 0; i < formats.Count; i++)
        {
            if (!string.Equals(
                    NormalizeExtension(formats[i].Extension),
                    normalizedExtension,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            index = i;
            break;
        }

        if (index <= 0)
            return;

        var preferred = formats[index];
        formats.RemoveAt(index);
        formats.Insert(0, preferred);
    }
}
