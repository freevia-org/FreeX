using FreeX.Core.Model;

namespace FreeX.App.Services.Ribbon;

/// <summary>
/// Formats the clipboard payload for the sheet-tab "Link to this Sheet" command.
/// The payload is an internal Excel/FreeX hyperlink target, so pasting it into a hyperlink
/// dialog or formula navigates to the first cell of the referenced sheet.
/// </summary>
public static class SheetTabLinkFormatter
{
    public static string BuildClipboardText(string sheetName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sheetName);
        return $"#{SheetNameFormatter.QuoteIfNeeded(sheetName)}!A1";
    }
}
