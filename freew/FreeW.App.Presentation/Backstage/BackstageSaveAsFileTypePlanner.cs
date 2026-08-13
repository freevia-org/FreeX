using Free.Shared.IO;
using Free.Shared.Shell;
using FreeW.Core.IO;
using FreeW.App.Presentation.Shell;

namespace FreeW.App.Presentation.Backstage;

public static class BackstageSaveAsFileTypePlanner
{
    private const string DefaultSaveExtension = ".docx";
    private static readonly IReadOnlyList<BackstageFileTypeActionGroupSpec<SaveAsFileTypeCategory>> FileTypeGroups =
    [
        new(SaveAsFileTypeCategory.Word, "Word Documents"),
        new(SaveAsFileTypeCategory.Web, "Web Pages"),
        new(SaveAsFileTypeCategory.Other, "Other Formats"),
        new(SaveAsFileTypeCategory.Compatibility, "Compatibility Formats"),
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
            FilePathPolicy.GetExtensionOrEmpty(currentPath));
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

    public static string ReplaceFileNameExtension(string? fileName, string extension)
    {
        var normalized = DocumentFileFormatResolver.NormalizeExtension(extension);
        var baseName = Path.GetFileNameWithoutExtension(fileName);
        if (string.IsNullOrWhiteSpace(baseName))
            baseName = DocumentPersistenceWorkflow.DefaultFallbackDisplayName;

        return baseName + normalized;
    }

    internal static IReadOnlyList<BackstageFileTypeActionRow<SaveAsFileTypeCategory>> BuildRows(
        IEnumerable<FileFormatDescriptor> formats)
    {
        ArgumentNullException.ThrowIfNull(formats);

        return DocumentFormatCapabilityPlanner
            .BuildSaveRows(formats)
            .Select(row => new BackstageFileTypeActionRow<SaveAsFileTypeCategory>(
                CategoryOf(row),
                row.PrimaryExtension,
                row.Label,
                row.Description,
                row.SaveFilterIndex))
            .ToArray();
    }

    private static SaveAsFileTypeCategory CategoryOf(DocumentFormatCapabilityRow row) =>
        row.Family switch
        {
            DocumentFormatCapabilityFamily.Word => SaveAsFileTypeCategory.Word,
            DocumentFormatCapabilityFamily.Web => SaveAsFileTypeCategory.Web,
            DocumentFormatCapabilityFamily.Compatibility => SaveAsFileTypeCategory.Compatibility,
            _ => SaveAsFileTypeCategory.Other,
        };

    internal enum SaveAsFileTypeCategory
    {
        Word,
        Web,
        Other,
        Compatibility
    }
}

public sealed record BackstageSaveAsInlinePlan(
    string SuggestedFileName,
    string SelectedExtension,
    IReadOnlyList<BackstageSaveAsFileTypeChoice> FileTypes);

public sealed record BackstageSaveAsFileTypeChoice(
    string Label,
    string PrimaryExtension,
    int SaveFilterIndex);
