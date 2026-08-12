using Avalonia.Input;
using Free.Shared.Shell.Avalonia;
using FreeX.App.Presentation.Editing;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    private static readonly PlatformClipboardFormat HtmlClipboardFormat =
        new("text/html", PlatformClipboardDataKind.Text);
    private static readonly PlatformClipboardFormat HtmlWindowsClipboardFormat =
        new("HTML Format", PlatformClipboardDataKind.Text);
    private static readonly DataFormat<string> HtmlPlatformFormat =
        AvaloniaPlatformClipboard.CreateStringFormat(HtmlClipboardFormat);
    private static readonly DataFormat<string> HtmlWindowsPlatformFormat =
        AvaloniaPlatformClipboard.CreateStringFormat(HtmlWindowsClipboardFormat);

    // R72-services-clipboard-interop-4-2: the WPF host (MainWindow.ClipboardCommands.cs) places a
    // comma-delimited "CSV" clipboard format alongside Text/HTML on every cell-range copy (R57), so a
    // destination that specifically enumerates for CSV (skipping plain text) still gets a payload.
    // "text/csv" is the cross-platform (Linux/macOS) MIME name; "Csv" mirrors Windows'
    // System.Windows.DataFormats.CommaSeparatedValue clipboard format name for parity with the WPF host
    // when this shell runs on Windows.
    private static readonly DataFormat<string> CsvPlatformFormat = DataFormat.CreateStringPlatformFormat("text/csv");
    private static readonly DataFormat<string> CsvWindowsPlatformFormat = DataFormat.CreateStringPlatformFormat("Csv");

    private static readonly PlatformClipboardFormat CsvClipboardFormat =
        new("text/csv", PlatformClipboardDataKind.Text);
    private static readonly PlatformClipboardFormat CsvWindowsClipboardFormat =
        new("Csv", PlatformClipboardDataKind.Text);

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

    private static PlatformClipboardContent BuildClipboardTextAndHtmlContent(
        string text,
        ViewportModel viewport,
        Sheet? sheet,
        GridRange range,
        WorkbookTheme theme)
    {
        var custom = new List<PlatformClipboardData>();
        var csv = ClipboardCsvTextRenderer.Render(text);
        if (!string.IsNullOrEmpty(csv))
        {
            custom.Add(PlatformClipboardData.FromText(CsvClipboardFormat.Name, csv));
            custom.Add(PlatformClipboardData.FromText(CsvWindowsClipboardFormat.Name, csv));
        }

        var html = ClipboardHtmlSerializer.Serialize(viewport, sheet, range, theme);
        if (html is not null)
        {
            custom.Add(PlatformClipboardData.FromText(HtmlClipboardFormat.Name, html.Fragment));
            custom.Add(PlatformClipboardData.FromText(HtmlWindowsClipboardFormat.Name, html.CfHtml));
        }

        return new PlatformClipboardContent(Text: text, CustomData: custom);
    }

}
