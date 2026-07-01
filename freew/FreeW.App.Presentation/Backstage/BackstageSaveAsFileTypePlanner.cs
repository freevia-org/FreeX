using System.IO;
using Free.Shared.Shell;
using FreeW.Core.IO;

namespace FreeW.App.Presentation.Backstage;

public static class BackstageSaveAsFileTypePlanner
{
    private const string DefaultSaveExtension = ".docx";
    private static readonly IReadOnlyList<BackstageFileTypeActionGroupSpec<SaveAsFileTypeCategory>> FileTypeGroups =
    [
        new(SaveAsFileTypeCategory.Word, "Word Documents"),
        new(SaveAsFileTypeCategory.Web, "Web Pages"),
        new(SaveAsFileTypeCategory.Other, "Other Formats"),
    ];

    public static IReadOnlyList<BackstageActionGroup> Build(
        IEnumerable<FileFormatDescriptor> formats,
        Action<string> saveAsExtension) =>
        Build(formats, (extension, _) => saveAsExtension(extension));

    public static IReadOnlyList<BackstageActionGroup> Build(
        IEnumerable<FileFormatDescriptor> formats,
        Action<string, int> saveAsFormat)
    {
        ArgumentNullException.ThrowIfNull(formats);
        ArgumentNullException.ThrowIfNull(saveAsFormat);

        var rows = BuildRows(formats);
        return BackstageFileTypeActionPlanner.BuildGroups(rows, FileTypeGroups, saveAsFormat);
    }

    public static BackstageSaveAsInlinePlan BuildInlinePlan(
        IEnumerable<FileFormatDescriptor> formats,
        string displayName,
        string? currentPath)
    {
        ArgumentNullException.ThrowIfNull(formats);

        var rows = BuildRows(formats);
        var choices = BackstageFileTypeActionPlanner
            .BuildChoices(rows)
            .Select(choice => new BackstageSaveAsFileTypeChoice(choice.Label, choice.PrimaryExtension, choice.SaveFilterIndex))
            .ToArray();

        var currentExtension = DocumentFileFormatResolver.NormalizeExtension(
            string.IsNullOrWhiteSpace(currentPath) ? string.Empty : Path.GetExtension(currentPath));
        var selectedExtension = choices.Any(choice => string.Equals(choice.PrimaryExtension, currentExtension, StringComparison.OrdinalIgnoreCase))
            ? currentExtension
            : choices.Any(choice => string.Equals(choice.PrimaryExtension, DefaultSaveExtension, StringComparison.OrdinalIgnoreCase))
                ? DefaultSaveExtension
                : choices.FirstOrDefault()?.PrimaryExtension ?? DefaultSaveExtension;

        var suggestedFileName = Free.Shared.IO.FileDialogRequestPlanner.BuildSuggestedSaveAsFileName(
            displayName,
            "Document",
            selectedExtension);

        return new BackstageSaveAsInlinePlan(suggestedFileName, selectedExtension, choices);
    }

    internal static IReadOnlyList<BackstageFileTypeActionRow<SaveAsFileTypeCategory>> BuildRows(
        IEnumerable<FileFormatDescriptor> formats)
    {
        ArgumentNullException.ThrowIfNull(formats);

        var saveFormats = formats
            .Where(format => format.CanSave)
            .Select((format, index) => new IndexedSaveFormat(
                format with { Extension = DocumentFileFormatResolver.NormalizeExtension(format.Extension) },
                index + 1))
            .Where(format => format.Format.Extension.Length > 0)
            .ToArray();

        return Collapse(saveFormats.Where(format => !format.Format.IsLegacy)).ToArray();
    }

    private static IEnumerable<BackstageFileTypeActionRow<SaveAsFileTypeCategory>> Collapse(
        IEnumerable<IndexedSaveFormat> formats)
    {
        var pending = formats.ToList();

        if (Take(pending, ".docx") is { } docx)
            yield return Row(SaveAsFileTypeCategory.Word, "Word Document", [docx], "Save as FreeW's default editable Word document format.");
        while (Take(pending, ".docx") is { } duplicateDocx)
            yield return Row(SaveAsFileTypeCategory.Word, duplicateDocx.Format.FormatName, [duplicateDocx], $"Save as {duplicateDocx.Format.FormatName}.");
        if (Take(pending, ".docm") is { } docm)
            yield return Row(SaveAsFileTypeCategory.Word, "Word Macro-Enabled Document", [docm], "Save an editable macro-enabled Word document package.");
        if (Take(pending, ".dotx") is { } dotx)
            yield return Row(SaveAsFileTypeCategory.Word, "Word Template", [dotx], "Save a reusable Word template.");
        if (Take(pending, ".dotm") is { } dotm)
            yield return Row(SaveAsFileTypeCategory.Word, "Word Macro-Enabled Template", [dotm], "Save a reusable macro-enabled Word template.");
        if (Take(pending, ".xml") is { } xml)
            yield return Row(SaveAsFileTypeCategory.Word, "Word XML Document", [xml], "Save as Word's Flat OPC XML document format.");
        while (Take(pending, ".xml") is { } duplicateXml)
            yield return Row(SaveAsFileTypeCategory.Word, duplicateXml.Format.FormatName, [duplicateXml], $"Save as {duplicateXml.Format.FormatName}.");

        var filteredHtml = TakeMany(pending, ".htm", ".html");
        if (filteredHtml.Count > 0)
            yield return Row(SaveAsFileTypeCategory.Web, filteredHtml[0].Format.FormatName, filteredHtml, "Save as clean, filtered HTML.");
        var fullHtml = TakeMany(pending, ".htm", ".html");
        if (fullHtml.Count > 0)
            yield return Row(SaveAsFileTypeCategory.Web, fullHtml[0].Format.FormatName, fullHtml, "Save as full HTML with Office round-trip markup.");
        var mhtml = TakeMany(pending, ".mht", ".mhtml");
        if (mhtml.Count > 0)
            yield return Row(SaveAsFileTypeCategory.Web, "Single File Web Page", mhtml, "Save as a single-file MHTML web page.");

        if (Take(pending, ".rtf") is { } rtf)
            yield return Row(SaveAsFileTypeCategory.Other, "Rich Text Format", [rtf], "Save as an editable rich text document.");
        var text = TakeMany(pending, ".txt", ".text");
        if (text.Count > 0)
            yield return Row(SaveAsFileTypeCategory.Other, "Plain Text", text, "Save only document text and paragraph breaks.");
        if (Take(pending, ".log") is { } log)
            yield return Row(SaveAsFileTypeCategory.Other, "Log File", [log], "Save as plain text with a .log extension.");

        foreach (var format in pending)
            yield return Row(SaveAsFileTypeCategory.Other, format.Format.FormatName, [format], $"Save as {format.Format.FormatName}.");
    }

    private static IndexedSaveFormat? Take(List<IndexedSaveFormat> formats, string extension)
    {
        var index = formats.FindIndex(format => string.Equals(format.Format.Extension, extension, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
            return null;

        var format = formats[index];
        formats.RemoveAt(index);
        return format;
    }

    private static IReadOnlyList<IndexedSaveFormat> TakeMany(List<IndexedSaveFormat> formats, params string[] extensions)
    {
        var rows = new List<IndexedSaveFormat>();
        foreach (var extension in extensions)
        {
            if (Take(formats, extension) is { } format)
                rows.Add(format);
        }

        return rows;
    }

    private static BackstageFileTypeActionRow<SaveAsFileTypeCategory> Row(
        SaveAsFileTypeCategory category,
        string displayName,
        IReadOnlyList<IndexedSaveFormat> formats,
        string description)
    {
        var extensions = formats.Select(format => "*" + format.Format.Extension).ToArray();
        return new BackstageFileTypeActionRow<SaveAsFileTypeCategory>(
            category,
            formats[0].Format.Extension,
            $"{displayName} ({string.Join(", ", extensions)})",
            description,
            formats[0].SaveFilterIndex);
    }

    internal enum SaveAsFileTypeCategory
    {
        Word,
        Web,
        Other
    }

    private sealed record IndexedSaveFormat(FileFormatDescriptor Format, int SaveFilterIndex);
}

public sealed record BackstageSaveAsInlinePlan(
    string SuggestedFileName,
    string SelectedExtension,
    IReadOnlyList<BackstageSaveAsFileTypeChoice> FileTypes);

public sealed record BackstageSaveAsFileTypeChoice(
    string Label,
    string PrimaryExtension,
    int SaveFilterIndex);
