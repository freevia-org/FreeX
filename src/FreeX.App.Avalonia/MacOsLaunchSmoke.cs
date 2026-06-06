using Avalonia.Controls.ApplicationLifetimes;

namespace FreeX.App.Avalonia;

internal sealed record MacOsLaunchSmokeOptions(string ReportPath)
{
    public const string Argument = "--macos-launch-smoke";

    public static bool TryParse(
        IReadOnlyList<string> args,
        out MacOsLaunchSmokeOptions? options,
        out string[] startupArguments,
        out string error)
    {
        ArgumentNullException.ThrowIfNull(args);

        options = null;
        error = "";
        var filteredArguments = new List<string>();
        for (var index = 0; index < args.Count; index++)
        {
            var argument = args[index];
            if (!string.Equals(argument, Argument, StringComparison.OrdinalIgnoreCase))
            {
                filteredArguments.Add(argument);
                continue;
            }

            if (options is not null)
            {
                startupArguments = [];
                error = $"{Argument} was specified more than once.";
                return false;
            }

            if (index + 1 >= args.Count)
            {
                startupArguments = [];
                error = $"{Argument} requires a report path.";
                return false;
            }

            var reportPath = args[++index];
            if (string.IsNullOrWhiteSpace(reportPath))
            {
                startupArguments = [];
                error = $"{Argument} requires a non-empty report path.";
                return false;
            }

            options = new MacOsLaunchSmokeOptions(reportPath);
        }

        startupArguments = filteredArguments.ToArray();
        return true;
    }
}

internal sealed record MacOsLaunchSmokeSnapshot(
    bool WindowShown,
    string WindowTitle,
    string DisplayName,
    string ActiveSheetName,
    int ViewportRowCount,
    int ViewportColumnCount,
    string? OpenedSourcePath,
    bool IsOpening,
    bool HasNativeFileMenu,
    bool HasNativeEditMenu,
    bool HasNativeFormatMenu,
    bool HasNativeOpenMenuItem,
    bool HasNativeSaveMenuItem,
    bool HasNativeSaveAsMenuItem,
    bool HasNativeUndoMenuItem,
    bool HasNativeRedoMenuItem,
    bool HasNativeCutMenuItem,
    bool HasNativeCopyMenuItem,
    bool HasNativePasteMenuItem,
    bool HasNativeClearContentsMenuItem,
    bool HasNativeBoldMenuItem,
    bool HasNativeItalicMenuItem,
    bool HasNativeUnderlineMenuItem,
    bool HasNativeDoubleUnderlineMenuItem,
    bool HasNativeStrikethroughMenuItem,
    bool HasNativeAlignTopMenuItem,
    bool HasNativeAlignMiddleMenuItem,
    bool HasNativeAlignBottomMenuItem,
    bool HasNativeWrapTextMenuItem,
    bool HasNativeDecreaseIndentMenuItem,
    bool HasNativeIncreaseIndentMenuItem,
    bool HasNativeAlignLeftMenuItem,
    bool HasNativeAlignCenterMenuItem,
    bool HasNativeAlignRightMenuItem,
    bool HasNativeQuitMenuItem)
{
    public bool IsPassed =>
        WindowShown &&
        !IsOpening &&
        !string.IsNullOrWhiteSpace(OpenedSourcePath) &&
        ViewportRowCount > 0 &&
        ViewportColumnCount > 0 &&
        HasNativeFileMenu &&
        HasNativeEditMenu &&
        HasNativeFormatMenu &&
        HasNativeOpenMenuItem &&
        HasNativeSaveMenuItem &&
        HasNativeSaveAsMenuItem &&
        HasNativeUndoMenuItem &&
        HasNativeRedoMenuItem &&
        HasNativeCutMenuItem &&
        HasNativeCopyMenuItem &&
        HasNativePasteMenuItem &&
        HasNativeClearContentsMenuItem &&
        HasNativeBoldMenuItem &&
        HasNativeItalicMenuItem &&
        HasNativeUnderlineMenuItem &&
        HasNativeDoubleUnderlineMenuItem &&
        HasNativeStrikethroughMenuItem &&
        HasNativeAlignTopMenuItem &&
        HasNativeAlignMiddleMenuItem &&
        HasNativeAlignBottomMenuItem &&
        HasNativeWrapTextMenuItem &&
        HasNativeDecreaseIndentMenuItem &&
        HasNativeIncreaseIndentMenuItem &&
        HasNativeAlignLeftMenuItem &&
        HasNativeAlignCenterMenuItem &&
        HasNativeAlignRightMenuItem &&
        HasNativeQuitMenuItem;
}

internal static class MacOsLaunchSmokeCoordinator
{
    private const int MaxWaitMilliseconds = 15000;
    private const int PollDelayMilliseconds = 250;

    public static void Start(MainWindow mainWindow, MacOsLaunchSmokeOptions options)
    {
        ArgumentNullException.ThrowIfNull(mainWindow);
        ArgumentNullException.ThrowIfNull(options);

        mainWindow.Opened += async (_, _) => await RunAsync(mainWindow, options);
    }

    private static async Task RunAsync(MainWindow mainWindow, MacOsLaunchSmokeOptions options)
    {
        var deadline = DateTimeOffset.UtcNow.AddMilliseconds(MaxWaitMilliseconds);
        var snapshot = mainWindow.CreateLaunchSmokeSnapshot();
        try
        {
            while (!snapshot.IsPassed && DateTimeOffset.UtcNow < deadline)
            {
                await Task.Delay(PollDelayMilliseconds);
                snapshot = mainWindow.CreateLaunchSmokeSnapshot();
            }

            WriteReport(options.ReportPath, snapshot);
            Shutdown(snapshot.IsPassed ? 0 : 1);
        }
        catch (Exception ex)
        {
            WriteFailureReport(options.ReportPath, snapshot, ex);
            Shutdown(1);
        }
    }

    private static void WriteReport(string reportPath, MacOsLaunchSmokeSnapshot snapshot)
    {
        var directory = Path.GetDirectoryName(reportPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        File.WriteAllLines(
            reportPath,
            [
                $"macos_launch_smoke={(snapshot.IsPassed ? "passed" : "failed")}",
                $"window_shown={FormatBool(snapshot.WindowShown)}",
                $"window_title={snapshot.WindowTitle}",
                $"display_name={snapshot.DisplayName}",
                $"active_sheet={snapshot.ActiveSheetName}",
                $"viewport_rows={snapshot.ViewportRowCount}",
                $"viewport_columns={snapshot.ViewportColumnCount}",
                $"opened_source_path={snapshot.OpenedSourcePath ?? ""}",
                $"is_opening={FormatBool(snapshot.IsOpening)}",
                $"native_file_menu={FormatBool(snapshot.HasNativeFileMenu)}",
                $"native_edit_menu={FormatBool(snapshot.HasNativeEditMenu)}",
                $"native_format_menu={FormatBool(snapshot.HasNativeFormatMenu)}",
                $"native_open_menu_item={FormatBool(snapshot.HasNativeOpenMenuItem)}",
                $"native_save_menu_item={FormatBool(snapshot.HasNativeSaveMenuItem)}",
                $"native_save_as_menu_item={FormatBool(snapshot.HasNativeSaveAsMenuItem)}",
                $"native_undo_menu_item={FormatBool(snapshot.HasNativeUndoMenuItem)}",
                $"native_redo_menu_item={FormatBool(snapshot.HasNativeRedoMenuItem)}",
                $"native_cut_menu_item={FormatBool(snapshot.HasNativeCutMenuItem)}",
                $"native_copy_menu_item={FormatBool(snapshot.HasNativeCopyMenuItem)}",
                $"native_paste_menu_item={FormatBool(snapshot.HasNativePasteMenuItem)}",
                $"native_clear_contents_menu_item={FormatBool(snapshot.HasNativeClearContentsMenuItem)}",
                $"native_bold_menu_item={FormatBool(snapshot.HasNativeBoldMenuItem)}",
                $"native_italic_menu_item={FormatBool(snapshot.HasNativeItalicMenuItem)}",
                $"native_underline_menu_item={FormatBool(snapshot.HasNativeUnderlineMenuItem)}",
                $"native_double_underline_menu_item={FormatBool(snapshot.HasNativeDoubleUnderlineMenuItem)}",
                $"native_strikethrough_menu_item={FormatBool(snapshot.HasNativeStrikethroughMenuItem)}",
                $"native_align_top_menu_item={FormatBool(snapshot.HasNativeAlignTopMenuItem)}",
                $"native_align_middle_menu_item={FormatBool(snapshot.HasNativeAlignMiddleMenuItem)}",
                $"native_align_bottom_menu_item={FormatBool(snapshot.HasNativeAlignBottomMenuItem)}",
                $"native_wrap_text_menu_item={FormatBool(snapshot.HasNativeWrapTextMenuItem)}",
                $"native_decrease_indent_menu_item={FormatBool(snapshot.HasNativeDecreaseIndentMenuItem)}",
                $"native_increase_indent_menu_item={FormatBool(snapshot.HasNativeIncreaseIndentMenuItem)}",
                $"native_align_left_menu_item={FormatBool(snapshot.HasNativeAlignLeftMenuItem)}",
                $"native_align_center_menu_item={FormatBool(snapshot.HasNativeAlignCenterMenuItem)}",
                $"native_align_right_menu_item={FormatBool(snapshot.HasNativeAlignRightMenuItem)}",
                $"native_quit_menu_item={FormatBool(snapshot.HasNativeQuitMenuItem)}",
            ]);
    }

    private static void WriteFailureReport(
        string reportPath,
        MacOsLaunchSmokeSnapshot snapshot,
        Exception exception)
    {
        WriteReport(reportPath, snapshot);
        File.AppendAllLines(reportPath, [$"error={exception.GetType().Name}: {exception.Message}"]);
    }

    private static void Shutdown(int exitCode)
    {
        if (global::Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.TryShutdown(exitCode);
        }
    }

    private static string FormatBool(bool value) => value ? "true" : "false";
}
