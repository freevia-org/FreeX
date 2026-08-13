using Avalonia.Input;
using Free.Shared.Shell.Avalonia;
using FreeX.App.Presentation.Editing;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    internal static string? BuildHtmlClipboardFragmentForTest(
        ViewportModel viewport, Sheet? sheet, GridRange range, WorkbookTheme theme) =>
        ClipboardHtmlSerializer.Serialize(viewport, sheet, range, theme)?.Fragment;

    internal static string WrapAsCfHtmlForTest(string fragment) =>
        ClipboardHtmlSerializer.WrapAsCfHtml(fragment);

    internal static DataFormat<string> CsvPlatformFormatForTest => CsvPlatformFormat;

    internal static DataFormat<string> CsvWindowsPlatformFormatForTest => CsvWindowsPlatformFormat;

    internal static void AddClipboardTextAndHtmlForTest(
        DataTransfer transfer,
        string text,
        ViewportModel viewport,
        Sheet? sheet,
        GridRange range,
        WorkbookTheme theme) =>
        AddClipboardTextAndHtml(transfer, text, viewport, sheet, range, theme);

}
