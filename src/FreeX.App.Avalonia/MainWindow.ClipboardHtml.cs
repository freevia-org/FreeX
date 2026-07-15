using Avalonia.Input;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    private static readonly DataFormat<string> HtmlPlatformFormat = DataFormat.CreateStringPlatformFormat("text/html");
    private static readonly DataFormat<string> HtmlWindowsPlatformFormat = DataFormat.CreateStringPlatformFormat("HTML Format");

    internal static string? BuildHtmlClipboardFragmentForTest(
        ViewportModel viewport, Sheet? sheet, GridRange range, WorkbookTheme theme) =>
        ClipboardHtmlSerializer.Serialize(viewport, sheet, range, theme)?.Fragment;

    internal static string WrapAsCfHtmlForTest(string fragment) =>
        ClipboardHtmlSerializer.WrapAsCfHtml(fragment);

    private static void AddClipboardTextAndHtml(
        DataTransfer transfer,
        string text,
        ViewportModel viewport,
        Sheet? sheet,
        GridRange range,
        WorkbookTheme theme)
    {
        transfer.Add(DataTransferItem.CreateText(text));
        var html = ClipboardHtmlSerializer.Serialize(viewport, sheet, range, theme);
        if (html is null)
            return;

        transfer.Add(DataTransferItem.Create(HtmlPlatformFormat, html.Fragment));
        transfer.Add(DataTransferItem.Create(HtmlWindowsPlatformFormat, html.CfHtml));
    }
}
