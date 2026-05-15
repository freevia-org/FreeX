namespace Freexcel.Core.Commands;

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
        NumberFormatShortcut.Currency => "$#,##0.00",
        NumberFormatShortcut.Percentage => "0%",
        NumberFormatShortcut.Date => "m/d/yyyy",
        NumberFormatShortcut.Time => "h:mm AM/PM",
        NumberFormatShortcut.Scientific => "0.00E+00",
        _ => "General"
    };
}
