using System.IO;
using System.Windows;
using Free.Shared.AppServices;
using Free.Shared.IO;
using Microsoft.Win32;

namespace Free.Shared.Shell;

public sealed record WpfOpenFileDialogResult(string? FileName, IReadOnlyList<string> FileNames)
{
    public static WpfOpenFileDialogResult Cancelled { get; } = new(null, Array.Empty<string>());

    public FileDialogSelection Selection => new(FileName);

    public bool Chosen => Selection.Chosen;
}

public sealed record WpfSaveFileDialogResult(string? FileName, int FilterIndex)
{
    public static WpfSaveFileDialogResult Cancelled { get; } = new(null, 0);

    public FileDialogSelection Selection => new(FileName);

    public bool Chosen => Selection.Chosen;
}

/// <summary>
/// WPF native-file-dialog renderer for shared file dialog plans.
/// </summary>
public static class WpfFileDialogService
{
    public static WpfOpenFileDialogResult ShowOpenDialog(
        Window? owner,
        FileOpenDialogPlan plan,
        bool checkFileExists = true,
        bool multiselect = false,
        string? title = null,
        string? initialDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(plan);

        return ShowOpenDialog(
            owner,
            plan.Filter,
            plan.DefaultExtensionWithDot,
            checkFileExists,
            multiselect,
            title,
            initialDirectory);
    }

    public static WpfOpenFileDialogResult ShowOpenDialog(
        Window? owner,
        string filter,
        string defaultExtensionWithDot = "",
        bool checkFileExists = true,
        bool multiselect = false,
        string? title = null,
        string? initialDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var dialog = new OpenFileDialog
        {
            Filter = filter,
            DefaultExt = defaultExtensionWithDot,
            CheckFileExists = checkFileExists,
            Multiselect = multiselect
        };
        if (!string.IsNullOrWhiteSpace(title))
            dialog.Title = title;
        if (!string.IsNullOrWhiteSpace(initialDirectory) && Directory.Exists(initialDirectory))
            dialog.InitialDirectory = initialDirectory;

        if (dialog.ShowDialog(owner) != true)
            return WpfOpenFileDialogResult.Cancelled;

        var fileNames = multiselect
            ? dialog.FileNames
            : string.IsNullOrWhiteSpace(dialog.FileName)
                ? Array.Empty<string>()
                : [dialog.FileName];
        return new WpfOpenFileDialogResult(dialog.FileName, fileNames);
    }

    public static WpfSaveFileDialogResult ShowSaveDialog(
        Window? owner,
        FileSaveDialogPlan plan,
        string? title = null)
    {
        ArgumentNullException.ThrowIfNull(plan);

        return ShowSaveDialog(
            owner,
            plan.Filter,
            plan.SuggestedFileName,
            plan.DefaultExtensionWithDot,
            plan.FilterIndex,
            title);
    }

    public static WpfSaveFileDialogResult ShowSaveDialog(
        Window? owner,
        string filter,
        string suggestedFileName,
        string defaultExtensionWithDot,
        int filterIndex,
        string? title = null)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(suggestedFileName);
        ArgumentNullException.ThrowIfNull(defaultExtensionWithDot);

        var dialog = new SaveFileDialog
        {
            Filter = filter,
            FilterIndex = filterIndex,
            DefaultExt = defaultExtensionWithDot,
            AddExtension = true,
            OverwritePrompt = true,
            FileName = suggestedFileName
        };
        if (!string.IsNullOrWhiteSpace(title))
            dialog.Title = title;

        return dialog.ShowDialog(owner) == true
            ? new WpfSaveFileDialogResult(dialog.FileName, dialog.FilterIndex)
            : WpfSaveFileDialogResult.Cancelled;
    }
}
