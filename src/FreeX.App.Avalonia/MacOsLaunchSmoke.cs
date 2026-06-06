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
    bool HasNativeOpenMenuItem,
    bool HasNativeSaveMenuItem,
    bool HasNativeSaveAsMenuItem,
    bool HasNativeUndoMenuItem,
    bool HasNativeRedoMenuItem,
    bool HasNativeCutMenuItem,
    bool HasNativeCopyMenuItem,
    bool HasNativePasteMenuItem,
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
        HasNativeOpenMenuItem &&
        HasNativeSaveMenuItem &&
        HasNativeSaveAsMenuItem &&
        HasNativeUndoMenuItem &&
        HasNativeRedoMenuItem &&
        HasNativeCutMenuItem &&
        HasNativeCopyMenuItem &&
        HasNativePasteMenuItem &&
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
                $"native_open_menu_item={FormatBool(snapshot.HasNativeOpenMenuItem)}",
                $"native_save_menu_item={FormatBool(snapshot.HasNativeSaveMenuItem)}",
                $"native_save_as_menu_item={FormatBool(snapshot.HasNativeSaveAsMenuItem)}",
                $"native_undo_menu_item={FormatBool(snapshot.HasNativeUndoMenuItem)}",
                $"native_redo_menu_item={FormatBool(snapshot.HasNativeRedoMenuItem)}",
                $"native_cut_menu_item={FormatBool(snapshot.HasNativeCutMenuItem)}",
                $"native_copy_menu_item={FormatBool(snapshot.HasNativeCopyMenuItem)}",
                $"native_paste_menu_item={FormatBool(snapshot.HasNativePasteMenuItem)}",
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
