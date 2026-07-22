using Avalonia.Input;
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

    internal static string BuildCsvClipboardTextForTest(string tsvText) =>
        BuildCsvClipboardText(tsvText);

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

        var csv = BuildCsvClipboardText(text);
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

    /// <summary>
    /// R72-services-clipboard-interop-4-2: re-delimits the tab/CRLF-separated <paramref name="tsvText"/>
    /// (as produced by <see cref="ClipboardSerializer.Serialize"/>) into RFC4180-quoted comma-separated
    /// text, mirroring <c>FreeX.App.Host.MainWindow.BuildCsvClipboardText</c> (R57-services-clipboard-
    /// formats-5-3) so both shells expose an identical CSV clipboard payload. Re-parses via
    /// <see cref="ClipboardSerializer.Deserialize"/> (the same reader the paste path already relies on)
    /// rather than re-implementing TSV parsing here.
    /// </summary>
    private static string BuildCsvClipboardText(string tsvText)
    {
        if (string.IsNullOrEmpty(tsvText))
            return string.Empty;

        var rows = ClipboardSerializer.Deserialize(tsvText);
        var sb = new System.Text.StringBuilder(tsvText.Length + 16);
        for (var r = 0; r < rows.Length; r++)
        {
            if (r > 0)
                sb.Append("\r\n");

            var row = rows[r];
            for (var c = 0; c < row.Length; c++)
            {
                if (c > 0)
                    sb.Append(',');

                AppendCsvField(sb, row[c]);
            }
        }

        return sb.ToString();
    }

    private static void AppendCsvField(System.Text.StringBuilder sb, string field)
    {
        var requiresQuoting = false;
        foreach (var ch in field)
        {
            if (ch is ',' or '"' or '\r' or '\n')
            {
                requiresQuoting = true;
                break;
            }
        }

        if (!requiresQuoting)
        {
            sb.Append(field);
            return;
        }

        sb.Append('"');
        foreach (var ch in field)
        {
            if (ch == '"')
                sb.Append("\"\"");
            else
                sb.Append(ch);
        }

        sb.Append('"');
    }
}
