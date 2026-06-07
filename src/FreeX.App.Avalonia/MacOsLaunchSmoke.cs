using Avalonia.Controls.ApplicationLifetimes;
using FreeX.App.Services;

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
    int SheetTabCount,
    int ViewportRowCount,
    int ViewportColumnCount,
    string? OpenedSourcePath,
    bool IsOpening,
    bool HasNewSheetButton,
    bool HasNativeFileMenu,
    bool HasNativeEditMenu,
    bool HasNativeFormatMenu,
    bool HasNativeSheetMenu,
    bool HasNativeHelpMenu,
    bool HasNativeOpenMenuItem,
    bool HasNativeSaveMenuItem,
    bool HasNativeSaveAsMenuItem,
    bool HasNativeNewSheetMenuItem,
    bool HasNativeRenameSheetMenuItem,
    bool HasNativeDuplicateSheetMenuItem,
    bool HasNativeDeleteSheetMenuItem,
    bool HasNativeUndoMenuItem,
    bool HasNativeRedoMenuItem,
    bool HasNativeCutMenuItem,
    bool HasNativeCopyMenuItem,
    bool HasNativePasteMenuItem,
    bool HasNativePasteSpecialMenuItem,
    bool HasNativeClearContentsMenuItem,
    bool HasNativeBoldMenuItem,
    bool HasNativeItalicMenuItem,
    bool HasNativeUnderlineMenuItem,
    bool HasNativeDoubleUnderlineMenuItem,
    bool HasNativeStrikethroughMenuItem,
    bool HasNativeIncreaseFontSizeMenuItem,
    bool HasNativeDecreaseFontSizeMenuItem,
    bool HasNativeFillColorMenuItem,
    bool HasNativeClearFillMenuItem,
    bool HasNativeFontColorMenuItem,
    int NativeFillColorSwatchCount,
    int NativeFontColorSwatchCount,
    bool HasNativeCellStylesMenuItem,
    int NativeCellStylesPresetCount,
    bool HasNativeHorizontalTextMenuItem,
    bool HasNativeAngleCounterclockwiseMenuItem,
    bool HasNativeAngleClockwiseMenuItem,
    bool HasNativeVerticalTextMenuItem,
    bool HasNativeRotateTextUpMenuItem,
    bool HasNativeRotateTextDownMenuItem,
    bool HasNativeCurrencyFormatMenuItem,
    bool HasNativePercentFormatMenuItem,
    bool HasNativeCommaStyleMenuItem,
    bool HasNativeIncreaseDecimalMenuItem,
    bool HasNativeDecreaseDecimalMenuItem,
    bool HasNativeAlignTopMenuItem,
    bool HasNativeAlignMiddleMenuItem,
    bool HasNativeAlignBottomMenuItem,
    bool HasNativeWrapTextMenuItem,
    bool HasNativeDecreaseIndentMenuItem,
    bool HasNativeIncreaseIndentMenuItem,
    bool HasNativeAlignLeftMenuItem,
    bool HasNativeAlignCenterMenuItem,
    bool HasNativeAlignRightMenuItem,
    bool HasNativeHelpOnlineMenuItem,
    bool HasNativeSendFeedbackMenuItem,
    bool HasNativeCheckForUpdatesMenuItem,
    bool HasNativeAboutMenuItem,
    bool HasNativeLegalNoticesMenuItem,
    bool HasNativeQuitMenuItem)
{
    public bool IsPassed =>
        WindowShown &&
        !IsOpening &&
        !string.IsNullOrWhiteSpace(OpenedSourcePath) &&
        SheetTabCount > 0 &&
        ViewportRowCount > 0 &&
        ViewportColumnCount > 0 &&
        HasNewSheetButton &&
        HasNativeFileMenu &&
        HasNativeEditMenu &&
        HasNativeFormatMenu &&
        HasNativeSheetMenu &&
        HasNativeHelpMenu &&
        HasNativeOpenMenuItem &&
        HasNativeSaveMenuItem &&
        HasNativeSaveAsMenuItem &&
        HasNativeNewSheetMenuItem &&
        HasNativeRenameSheetMenuItem &&
        HasNativeDuplicateSheetMenuItem &&
        HasNativeDeleteSheetMenuItem &&
        HasNativeUndoMenuItem &&
        HasNativeRedoMenuItem &&
        HasNativeCutMenuItem &&
        HasNativeCopyMenuItem &&
        HasNativePasteMenuItem &&
        HasNativePasteSpecialMenuItem &&
        HasNativeClearContentsMenuItem &&
        HasNativeBoldMenuItem &&
        HasNativeItalicMenuItem &&
        HasNativeUnderlineMenuItem &&
        HasNativeDoubleUnderlineMenuItem &&
        HasNativeStrikethroughMenuItem &&
        HasNativeIncreaseFontSizeMenuItem &&
        HasNativeDecreaseFontSizeMenuItem &&
        HasNativeFillColorMenuItem &&
        HasNativeClearFillMenuItem &&
        HasNativeFontColorMenuItem &&
        NativeFillColorSwatchCount == CellColorPalettePlanner.BuildDefaultSwatches().Count &&
        NativeFontColorSwatchCount == CellColorPalettePlanner.BuildDefaultSwatches().Count &&
        HasNativeCellStylesMenuItem &&
        NativeCellStylesPresetCount == Enum.GetValues<CellStylePreset>().Length &&
        HasNativeHorizontalTextMenuItem &&
        HasNativeAngleCounterclockwiseMenuItem &&
        HasNativeAngleClockwiseMenuItem &&
        HasNativeVerticalTextMenuItem &&
        HasNativeRotateTextUpMenuItem &&
        HasNativeRotateTextDownMenuItem &&
        HasNativeCurrencyFormatMenuItem &&
        HasNativePercentFormatMenuItem &&
        HasNativeCommaStyleMenuItem &&
        HasNativeIncreaseDecimalMenuItem &&
        HasNativeDecreaseDecimalMenuItem &&
        HasNativeAlignTopMenuItem &&
        HasNativeAlignMiddleMenuItem &&
        HasNativeAlignBottomMenuItem &&
        HasNativeWrapTextMenuItem &&
        HasNativeDecreaseIndentMenuItem &&
        HasNativeIncreaseIndentMenuItem &&
        HasNativeAlignLeftMenuItem &&
        HasNativeAlignCenterMenuItem &&
        HasNativeAlignRightMenuItem &&
        HasNativeHelpOnlineMenuItem &&
        HasNativeSendFeedbackMenuItem &&
        HasNativeCheckForUpdatesMenuItem &&
        HasNativeAboutMenuItem &&
        HasNativeLegalNoticesMenuItem &&
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
                $"sheet_tab_count={snapshot.SheetTabCount}",
                $"viewport_rows={snapshot.ViewportRowCount}",
                $"viewport_columns={snapshot.ViewportColumnCount}",
                $"opened_source_path={snapshot.OpenedSourcePath ?? ""}",
                $"is_opening={FormatBool(snapshot.IsOpening)}",
                $"new_sheet_button={FormatBool(snapshot.HasNewSheetButton)}",
                $"native_file_menu={FormatBool(snapshot.HasNativeFileMenu)}",
                $"native_edit_menu={FormatBool(snapshot.HasNativeEditMenu)}",
                $"native_format_menu={FormatBool(snapshot.HasNativeFormatMenu)}",
                $"native_sheet_menu={FormatBool(snapshot.HasNativeSheetMenu)}",
                $"native_help_menu={FormatBool(snapshot.HasNativeHelpMenu)}",
                $"native_open_menu_item={FormatBool(snapshot.HasNativeOpenMenuItem)}",
                $"native_save_menu_item={FormatBool(snapshot.HasNativeSaveMenuItem)}",
                $"native_save_as_menu_item={FormatBool(snapshot.HasNativeSaveAsMenuItem)}",
                $"native_new_sheet_menu_item={FormatBool(snapshot.HasNativeNewSheetMenuItem)}",
                $"native_rename_sheet_menu_item={FormatBool(snapshot.HasNativeRenameSheetMenuItem)}",
                $"native_duplicate_sheet_menu_item={FormatBool(snapshot.HasNativeDuplicateSheetMenuItem)}",
                $"native_delete_sheet_menu_item={FormatBool(snapshot.HasNativeDeleteSheetMenuItem)}",
                $"native_undo_menu_item={FormatBool(snapshot.HasNativeUndoMenuItem)}",
                $"native_redo_menu_item={FormatBool(snapshot.HasNativeRedoMenuItem)}",
                $"native_cut_menu_item={FormatBool(snapshot.HasNativeCutMenuItem)}",
                $"native_copy_menu_item={FormatBool(snapshot.HasNativeCopyMenuItem)}",
                $"native_paste_menu_item={FormatBool(snapshot.HasNativePasteMenuItem)}",
                $"native_paste_special_menu_item={FormatBool(snapshot.HasNativePasteSpecialMenuItem)}",
                $"native_clear_contents_menu_item={FormatBool(snapshot.HasNativeClearContentsMenuItem)}",
                $"native_bold_menu_item={FormatBool(snapshot.HasNativeBoldMenuItem)}",
                $"native_italic_menu_item={FormatBool(snapshot.HasNativeItalicMenuItem)}",
                $"native_underline_menu_item={FormatBool(snapshot.HasNativeUnderlineMenuItem)}",
                $"native_double_underline_menu_item={FormatBool(snapshot.HasNativeDoubleUnderlineMenuItem)}",
                $"native_strikethrough_menu_item={FormatBool(snapshot.HasNativeStrikethroughMenuItem)}",
                $"native_increase_font_size_menu_item={FormatBool(snapshot.HasNativeIncreaseFontSizeMenuItem)}",
                $"native_decrease_font_size_menu_item={FormatBool(snapshot.HasNativeDecreaseFontSizeMenuItem)}",
                $"native_fill_color_menu_item={FormatBool(snapshot.HasNativeFillColorMenuItem)}",
                $"native_clear_fill_menu_item={FormatBool(snapshot.HasNativeClearFillMenuItem)}",
                $"native_font_color_menu_item={FormatBool(snapshot.HasNativeFontColorMenuItem)}",
                $"native_fill_color_swatch_count={snapshot.NativeFillColorSwatchCount}",
                $"native_font_color_swatch_count={snapshot.NativeFontColorSwatchCount}",
                $"native_cell_styles_menu_item={FormatBool(snapshot.HasNativeCellStylesMenuItem)}",
                $"native_cell_styles_preset_count={snapshot.NativeCellStylesPresetCount}",
                $"native_horizontal_text_menu_item={FormatBool(snapshot.HasNativeHorizontalTextMenuItem)}",
                $"native_angle_counterclockwise_menu_item={FormatBool(snapshot.HasNativeAngleCounterclockwiseMenuItem)}",
                $"native_angle_clockwise_menu_item={FormatBool(snapshot.HasNativeAngleClockwiseMenuItem)}",
                $"native_vertical_text_menu_item={FormatBool(snapshot.HasNativeVerticalTextMenuItem)}",
                $"native_rotate_text_up_menu_item={FormatBool(snapshot.HasNativeRotateTextUpMenuItem)}",
                $"native_rotate_text_down_menu_item={FormatBool(snapshot.HasNativeRotateTextDownMenuItem)}",
                $"native_currency_format_menu_item={FormatBool(snapshot.HasNativeCurrencyFormatMenuItem)}",
                $"native_percent_format_menu_item={FormatBool(snapshot.HasNativePercentFormatMenuItem)}",
                $"native_comma_style_menu_item={FormatBool(snapshot.HasNativeCommaStyleMenuItem)}",
                $"native_increase_decimal_menu_item={FormatBool(snapshot.HasNativeIncreaseDecimalMenuItem)}",
                $"native_decrease_decimal_menu_item={FormatBool(snapshot.HasNativeDecreaseDecimalMenuItem)}",
                $"native_align_top_menu_item={FormatBool(snapshot.HasNativeAlignTopMenuItem)}",
                $"native_align_middle_menu_item={FormatBool(snapshot.HasNativeAlignMiddleMenuItem)}",
                $"native_align_bottom_menu_item={FormatBool(snapshot.HasNativeAlignBottomMenuItem)}",
                $"native_wrap_text_menu_item={FormatBool(snapshot.HasNativeWrapTextMenuItem)}",
                $"native_decrease_indent_menu_item={FormatBool(snapshot.HasNativeDecreaseIndentMenuItem)}",
                $"native_increase_indent_menu_item={FormatBool(snapshot.HasNativeIncreaseIndentMenuItem)}",
                $"native_align_left_menu_item={FormatBool(snapshot.HasNativeAlignLeftMenuItem)}",
                $"native_align_center_menu_item={FormatBool(snapshot.HasNativeAlignCenterMenuItem)}",
                $"native_align_right_menu_item={FormatBool(snapshot.HasNativeAlignRightMenuItem)}",
                $"native_help_online_menu_item={FormatBool(snapshot.HasNativeHelpOnlineMenuItem)}",
                $"native_send_feedback_menu_item={FormatBool(snapshot.HasNativeSendFeedbackMenuItem)}",
                $"native_check_for_updates_menu_item={FormatBool(snapshot.HasNativeCheckForUpdatesMenuItem)}",
                $"native_about_menu_item={FormatBool(snapshot.HasNativeAboutMenuItem)}",
                $"native_legal_notices_menu_item={FormatBool(snapshot.HasNativeLegalNoticesMenuItem)}",
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
