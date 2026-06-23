using System.Windows;
using Free.Shared.IO;
using Microsoft.Win32;

namespace Free.Shared.Shell;

public sealed record WpfOpenFileDialogResult(string? FileName, IReadOnlyList<string> FileNames)
{
    public static WpfOpenFileDialogResult Cancelled { get; } = new(null, Array.Empty<string>());

    public bool Chosen => !string.IsNullOrWhiteSpace(FileName);
}

public sealed record WpfSaveFileDialogResult(string? FileName, int FilterIndex)
{
    public static WpfSaveFileDialogResult Cancelled { get; } = new(null, 0);

    public bool Chosen => !string.IsNullOrWhiteSpace(FileName);
}

/// <summary>
/// WPF native-file-dialog renderer for shared file dialog plans.
/// </summary>
public static class WpfFileDialogService
{
    public static WpfOpenFileDialogResult ShowOpenDialog(
        Window owner,
        FileOpenDialogPlan plan,
        bool checkFileExists = true,
        bool multiselect = false,
        string? title = null)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(plan);

        var dialog = new OpenFileDialog
        {
            Filter = plan.Filter,
            DefaultExt = plan.DefaultExtensionWithDot,
            CheckFileExists = checkFileExists,
            Multiselect = multiselect
        };
        if (!string.IsNullOrWhiteSpace(title))
            dialog.Title = title;

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
        Window owner,
        FileSaveDialogPlan plan,
        string? title = null)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(plan);

        var dialog = new SaveFileDialog
        {
            Filter = plan.Filter,
            FilterIndex = plan.FilterIndex,
            DefaultExt = plan.DefaultExtensionWithDot,
            AddExtension = true,
            OverwritePrompt = true,
            FileName = plan.SuggestedFileName
        };
        if (!string.IsNullOrWhiteSpace(title))
            dialog.Title = title;

        return dialog.ShowDialog(owner) == true
            ? new WpfSaveFileDialogResult(dialog.FileName, dialog.FilterIndex)
            : WpfSaveFileDialogResult.Cancelled;
    }
}
