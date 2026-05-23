namespace Freexcel.App.Host;

public static class ShellFocusCyclePlanner
{
    private static readonly ShellFocusTarget[] Cycle =
    [
        ShellFocusTarget.Worksheet,
        ShellFocusTarget.Ribbon,
        ShellFocusTarget.FormulaBar,
        ShellFocusTarget.SheetTabs,
        ShellFocusTarget.StatusBar
    ];

    public static ShellFocusTarget GetNext(ShellFocusTarget current, bool reverse)
    {
        var index = Array.IndexOf(Cycle, current);
        if (index < 0)
            index = 0;

        var offset = reverse ? -1 : 1;
        var nextIndex = (index + offset + Cycle.Length) % Cycle.Length;
        return Cycle[nextIndex];
    }
}

public enum ShellFocusTarget
{
    Worksheet,
    Ribbon,
    FormulaBar,
    SheetTabs,
    StatusBar
}
