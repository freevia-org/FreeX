using FreeX.Core.Model;

namespace FreeX.Core.Commands;

public enum NumberFormatShortcut
{
    General,
    Number,
    Currency,
    Percentage,
    Date,
    Time,
    Scientific
}

public static class NumberFormatShortcutService
{
    public static string GetFormat(NumberFormatShortcut shortcut) => shortcut switch
    {
        NumberFormatShortcut.General => "General",
        NumberFormatShortcut.Number => "#,##0.00",
        // Excel's Ctrl+Shift+4 currency shortcut is the red-negative built-in
        // currency format (numFmtId 8), not a custom two-decimal dollar format.
        NumberFormatShortcut.Currency => GetBuiltInFormat(8),
        NumberFormatShortcut.Percentage => "0%",
        NumberFormatShortcut.Date => "m/d/yyyy",
        NumberFormatShortcut.Time => "h:mm AM/PM",
        NumberFormatShortcut.Scientific => "0.00E+00",
        _ => "General"
    };

    private static string GetBuiltInFormat(int numberFormatId)
    {
        if (BuiltInNumberFormatCatalog.TryResolveFormatCode(numberFormatId, out var formatCode))
            return formatCode;

        throw new InvalidOperationException($"Missing built-in number format {numberFormatId}.");
    }
}
