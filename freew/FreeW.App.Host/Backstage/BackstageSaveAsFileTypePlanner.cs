using System.IO;
using Free.Shared.Shell.Wpf;
using FreeW.Core.IO;

namespace FreeW.App.Host.Backstage;

internal static class BackstageSaveAsFileTypePlanner
{
    private const string DefaultSaveExtension = ".docx";

    public static IReadOnlyList<BackstageActionGroup> Build(
        IEnumerable<FileFormatDescriptor> formats,
        Action<string> saveAsExtension)
    {
        ArgumentNullException.ThrowIfNull(formats);
        ArgumentNullException.ThrowIfNull(saveAsExtension);

        var rows = Collapse(formats.Where(format => format.CanSave)).ToList();

        return
        [
            new("Word Documents", BuildRows(rows, SaveAsFileTypeCategory.Word, saveAsExtension)),
            new("Web Pages", BuildRows(rows, SaveAsFileTypeCategory.Web, saveAsExtension)),
            new("Other Formats", BuildRows(rows, SaveAsFileTypeCategory.Other, saveAsExtension)),
        ];
    }

    public static BackstageSaveAsInlinePlan BuildInlinePlan(
        IEnumerable<FileFormatDescriptor> formats,
        string displayName,
        string? currentPath)
    {
        ArgumentNullException.ThrowIfNull(formats);

        var rows = Collapse(formats.Where(format => format.CanSave)).ToList();
        var choices = rows
            .Select(row => new BackstageSaveAsFileTypeChoice(row.Label, row.PrimaryExtension))
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

    private static IReadOnlyList<BackstageActionRow> BuildRows(
        IEnumerable<SaveAsFileTypeRow> rows,
        SaveAsFileTypeCategory category,
        Action<string> saveAsExtension) =>
        rows
            .Where(row => row.Category == category)
            .Select(row => new BackstageActionRow(
                row.Label,
                row.Description,
                () => saveAsExtension(row.PrimaryExtension)))
            .ToArray();

    private static IEnumerable<SaveAsFileTypeRow> Collapse(IEnumerable<FileFormatDescriptor> formats)
    {
        var pending = formats
            .Select(format => format with { Extension = DocumentFileFormatResolver.NormalizeExtension(format.Extension) })
            .Where(format => format.Extension.Length > 0)
            .ToList();

        if (Take(pending, ".docx") is { } docx)
            yield return Row(SaveAsFileTypeCategory.Word, "Word Document", [docx], "Save as FreeW's default editable Word document format.");
        if (Take(pending, ".docm") is { } docm)
            yield return Row(SaveAsFileTypeCategory.Word, "Word Macro-Enabled Document", [docm], "Save an editable macro-enabled Word document package.");
        if (Take(pending, ".dotx") is { } dotx)
            yield return Row(SaveAsFileTypeCategory.Word, "Word Template", [dotx], "Save a reusable Word template.");
        if (Take(pending, ".dotm") is { } dotm)
            yield return Row(SaveAsFileTypeCategory.Word, "Word Macro-Enabled Template", [dotm], "Save a reusable macro-enabled Word template.");
        if (Take(pending, ".xml") is { } xml)
            yield return Row(SaveAsFileTypeCategory.Word, "Word XML Document", [xml], "Save as Word's Flat OPC XML document format.");

        var html = TakeMany(pending, ".htm", ".html");
        if (html.Count > 0)
            yield return Row(SaveAsFileTypeCategory.Web, "Web Page", html, "Save as an editable HTML web page.");
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
            yield return Row(SaveAsFileTypeCategory.Other, format.FormatName, [format], $"Save as {format.FormatName}.");
    }

    private static FileFormatDescriptor? Take(List<FileFormatDescriptor> formats, string extension)
    {
        var index = formats.FindIndex(format => string.Equals(format.Extension, extension, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
            return null;

        var format = formats[index];
        formats.RemoveAt(index);
        return format;
    }

    private static IReadOnlyList<FileFormatDescriptor> TakeMany(List<FileFormatDescriptor> formats, params string[] extensions)
    {
        var rows = new List<FileFormatDescriptor>();
        foreach (var extension in extensions)
        {
            if (Take(formats, extension) is { } format)
                rows.Add(format);
        }

        return rows;
    }

    private static SaveAsFileTypeRow Row(
        SaveAsFileTypeCategory category,
        string displayName,
        IReadOnlyList<FileFormatDescriptor> formats,
        string description)
    {
        var extensions = formats.Select(format => "*" + format.Extension).ToArray();
        return new SaveAsFileTypeRow(
            category,
            formats[0].Extension,
            $"{displayName} ({string.Join(", ", extensions)})",
            description);
    }

    private sealed record SaveAsFileTypeRow(
        SaveAsFileTypeCategory Category,
        string PrimaryExtension,
        string Label,
        string Description);

    private enum SaveAsFileTypeCategory
    {
        Word,
        Web,
        Other
    }
}

internal sealed record BackstageSaveAsInlinePlan(
    string SuggestedFileName,
    string SelectedExtension,
    IReadOnlyList<BackstageSaveAsFileTypeChoice> FileTypes);

internal sealed record BackstageSaveAsFileTypeChoice(
    string Label,
    string PrimaryExtension);
