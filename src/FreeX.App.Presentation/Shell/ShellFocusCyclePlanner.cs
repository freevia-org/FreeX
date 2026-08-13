namespace FreeX.App.Presentation.Shell;

public static class ShellFocusCyclePlanner
{
    private static readonly ShellFocusTarget[] Cycle =
    [
        ShellFocusTarget.Worksheet,
        ShellFocusTarget.Ribbon,
        ShellFocusTarget.FormulaBar,
        ShellFocusTarget.SheetTabs,
        ShellFocusTarget.TaskPane,
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

    public static ShellFocusTarget GetNextAvailable(
        ShellFocusTarget current,
        bool reverse,
        Predicate<ShellFocusTarget> isAvailable)
    {
        for (var attempt = 0; attempt < Cycle.Length; attempt++)
        {
            current = GetNext(current, reverse);
            if (isAvailable(current))
                return current;
        }

        return ShellFocusTarget.Worksheet;
    }

    public static bool TryFocusNextAvailable(
        ShellFocusTarget current,
        bool reverse,
        Predicate<ShellFocusTarget> isAvailable,
        Predicate<ShellFocusTarget> tryFocus)
    {
        ArgumentNullException.ThrowIfNull(isAvailable);
        ArgumentNullException.ThrowIfNull(tryFocus);

        for (var attempt = 0; attempt < Cycle.Length; attempt++)
        {
            current = GetNextAvailable(current, reverse, isAvailable);
            if (tryFocus(current))
                return true;
        }

        return false;
    }
}

public enum ShellFocusTarget
{
    Worksheet,
    Ribbon,
    FormulaBar,
    SheetTabs,
    TaskPane,
    StatusBar
}
