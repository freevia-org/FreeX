using Avalonia.Input;
using FreeX.App.Presentation.Editing;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    private static readonly DataFormat<string> HtmlPlatformFormat = DataFormat.CreateStringPlatformFormat("text/html");
    private static readonly DataFormat<string> HtmlWindowsPlatformFormat = DataFormat.CreateStringPlatformFormat("HTML Format");

    // R72-services-clipboard-interop-4-2: the WPF host (MainWindow.ClipboardCommands.cs) places a
    // comma-delimited "CSV" clipboard format alongside Text/HTML on every cell-range copy (R57), so a
    // destination that specifically enumerates for CSV (skipping plain text) still gets a payload.
    // "text/csv" is the cross-platform (Linux/macOS) MIME name; "Csv" mirrors Windows'
    // System.Windows.DataFormats.CommaSeparatedValue clipboard format name for parity with the WPF host
    // when this shell runs on Windows.
    private static readonly DataFormat<string> CsvPlatformFormat = DataFormat.CreateStringPlatformFormat("text/csv");
    private static readonly DataFormat<string> CsvWindowsPlatformFormat = DataFormat.CreateStringPlatformFormat("Csv");

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

    private static void AddClipboardTextAndHtml(
        DataTransfer transfer,
        string text,
        ViewportModel viewport,
        Sheet? sheet,
        GridRange range,
        WorkbookTheme theme)
    {
        transfer.Add(DataTransferItem.CreateText(text));

        var csv = ClipboardCsvTextRenderer.Render(text);
        if (!string.IsNullOrEmpty(csv))
        {
            transfer.Add(DataTransferItem.Create(CsvPlatformFormat, csv));
            transfer.Add(DataTransferItem.Create(CsvWindowsPlatformFormat, csv));
        }

        var html = ClipboardHtmlSerializer.Serialize(viewport, sheet, range, theme);
        if (html is null)
            return;

        transfer.Add(DataTransferItem.Create(HtmlPlatformFormat, html.Fragment));
        transfer.Add(DataTransferItem.Create(HtmlWindowsPlatformFormat, html.CfHtml));
    }

}
