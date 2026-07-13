namespace FreeX.App.Presentation.Shell;

public enum WorkbookShortcutRoute
{
    NewWorkbook,
    OpenWorkbook,
    SaveWorkbook,
    Copy,
    Cut,
    Paste,
    PasteSpecial,
    Undo,
    Redo,
    ToggleBold,
    ToggleItalic,
    ToggleUnderline,
    ToggleStrikethrough,
    FillDown,
    FillRight,
    FlashFill,
    ToggleShowFormulas,
    OpenFormatCells,
    NumberFormatGeneral,
    NumberFormatNumber,
    NumberFormatTime,
    NumberFormatDate,
    NumberFormatCurrency,
    NumberFormatPercentage,
    NumberFormatScientific,
    Find,
    Replace,
    GoTo,
    InsertFunction,
    AutoSum,
    WorkbookStatistics,
    InsertWorksheet
}

public enum WorkbookShortcutKey
{
    A,
    Back,
    B,
    C,
    D,
    D1,
    D2,
    D3,
    D4,
    D5,
    D6,
    Delete,
    E,
    F,
    F3,
    F5,
    F11,
    F12,
    G,
    H,
    I,
    Insert,
    N,
    O,
    Oem3,
    OemPlus,
    P,
    R,
    S,
    U,
    V,
    X,
    Y,
    Z
}

[Flags]
public enum WorkbookShortcutModifiers
{
    None = 0,
    Control = 1,
    Alt = 2,
    Shift = 4,
    Meta = 8
}

public readonly record struct WorkbookShortcutChord(
    WorkbookShortcutKey Key,
    WorkbookShortcutModifiers Modifiers = WorkbookShortcutModifiers.None);

public sealed record WorkbookShortcutRouteRule(
    WorkbookShortcutRoute Route,
    WorkbookShortcutChord WindowsChord,
    WorkbookShortcutChord? NativeMenuChord = null);

public static class WorkbookKeyboardShortcutCatalog
{
    public static IReadOnlyList<WorkbookShortcutRouteRule> Rules { get; } =
    [
        new(
            WorkbookShortcutRoute.NewWorkbook,
            new WorkbookShortcutChord(WorkbookShortcutKey.N, WorkbookShortcutModifiers.Control),
            new WorkbookShortcutChord(WorkbookShortcutKey.N, WorkbookShortcutModifiers.Meta)),
        new(
            WorkbookShortcutRoute.OpenWorkbook,
            new WorkbookShortcutChord(WorkbookShortcutKey.O, WorkbookShortcutModifiers.Control),
            new WorkbookShortcutChord(WorkbookShortcutKey.O, WorkbookShortcutModifiers.Meta)),
        new(WorkbookShortcutRoute.OpenWorkbook, new WorkbookShortcutChord(WorkbookShortcutKey.F12, WorkbookShortcutModifiers.Control)),
        new(
            WorkbookShortcutRoute.SaveWorkbook,
            new WorkbookShortcutChord(WorkbookShortcutKey.S, WorkbookShortcutModifiers.Control),
            new WorkbookShortcutChord(WorkbookShortcutKey.S, WorkbookShortcutModifiers.Meta)),
        new(WorkbookShortcutRoute.SaveWorkbook, new WorkbookShortcutChord(WorkbookShortcutKey.F12, WorkbookShortcutModifiers.Shift)),
        new(
            WorkbookShortcutRoute.Copy,
            new WorkbookShortcutChord(WorkbookShortcutKey.C, WorkbookShortcutModifiers.Control),
            new WorkbookShortcutChord(WorkbookShortcutKey.C, WorkbookShortcutModifiers.Meta)),
        new(WorkbookShortcutRoute.Copy, new WorkbookShortcutChord(WorkbookShortcutKey.Insert, WorkbookShortcutModifiers.Control)),
        new(
            WorkbookShortcutRoute.Cut,
            new WorkbookShortcutChord(WorkbookShortcutKey.X, WorkbookShortcutModifiers.Control),
            new WorkbookShortcutChord(WorkbookShortcutKey.X, WorkbookShortcutModifiers.Meta)),
        new(WorkbookShortcutRoute.Cut, new WorkbookShortcutChord(WorkbookShortcutKey.Delete, WorkbookShortcutModifiers.Shift)),
        new(
            WorkbookShortcutRoute.Paste,
            new WorkbookShortcutChord(WorkbookShortcutKey.V, WorkbookShortcutModifiers.Control),
            new WorkbookShortcutChord(WorkbookShortcutKey.V, WorkbookShortcutModifiers.Meta)),
        new(WorkbookShortcutRoute.Paste, new WorkbookShortcutChord(WorkbookShortcutKey.Insert, WorkbookShortcutModifiers.Shift)),
        new(
            WorkbookShortcutRoute.PasteSpecial,
            new WorkbookShortcutChord(WorkbookShortcutKey.V, WorkbookShortcutModifiers.Control | WorkbookShortcutModifiers.Alt),
            new WorkbookShortcutChord(WorkbookShortcutKey.V, WorkbookShortcutModifiers.Meta | WorkbookShortcutModifiers.Alt)),
        new(
            WorkbookShortcutRoute.Undo,
            new WorkbookShortcutChord(WorkbookShortcutKey.Z, WorkbookShortcutModifiers.Control),
            new WorkbookShortcutChord(WorkbookShortcutKey.Z, WorkbookShortcutModifiers.Meta)),
        new(WorkbookShortcutRoute.Undo, new WorkbookShortcutChord(WorkbookShortcutKey.Back, WorkbookShortcutModifiers.Alt)),
        new(WorkbookShortcutRoute.Redo, new WorkbookShortcutChord(WorkbookShortcutKey.Y, WorkbookShortcutModifiers.Control)),
        new(
            WorkbookShortcutRoute.Redo,
            new WorkbookShortcutChord(WorkbookShortcutKey.Z, WorkbookShortcutModifiers.Control | WorkbookShortcutModifiers.Shift),
            new WorkbookShortcutChord(WorkbookShortcutKey.Z, WorkbookShortcutModifiers.Meta | WorkbookShortcutModifiers.Shift)),
        new(
            WorkbookShortcutRoute.ToggleBold,
            new WorkbookShortcutChord(WorkbookShortcutKey.B, WorkbookShortcutModifiers.Control),
            new WorkbookShortcutChord(WorkbookShortcutKey.B, WorkbookShortcutModifiers.Meta)),
        new(WorkbookShortcutRoute.ToggleBold, new WorkbookShortcutChord(WorkbookShortcutKey.D2, WorkbookShortcutModifiers.Control)),
        new(
            WorkbookShortcutRoute.ToggleItalic,
            new WorkbookShortcutChord(WorkbookShortcutKey.I, WorkbookShortcutModifiers.Control),
            new WorkbookShortcutChord(WorkbookShortcutKey.I, WorkbookShortcutModifiers.Meta)),
        new(WorkbookShortcutRoute.ToggleItalic, new WorkbookShortcutChord(WorkbookShortcutKey.D3, WorkbookShortcutModifiers.Control)),
        new(
            WorkbookShortcutRoute.ToggleUnderline,
            new WorkbookShortcutChord(WorkbookShortcutKey.U, WorkbookShortcutModifiers.Control),
            new WorkbookShortcutChord(WorkbookShortcutKey.U, WorkbookShortcutModifiers.Meta)),
        new(WorkbookShortcutRoute.ToggleUnderline, new WorkbookShortcutChord(WorkbookShortcutKey.D4, WorkbookShortcutModifiers.Control)),
        new(
            WorkbookShortcutRoute.ToggleStrikethrough,
            new WorkbookShortcutChord(WorkbookShortcutKey.D5, WorkbookShortcutModifiers.Control),
            new WorkbookShortcutChord(WorkbookShortcutKey.D5, WorkbookShortcutModifiers.Control)),
        new(
            WorkbookShortcutRoute.FillDown,
            new WorkbookShortcutChord(WorkbookShortcutKey.D, WorkbookShortcutModifiers.Control),
            new WorkbookShortcutChord(WorkbookShortcutKey.D, WorkbookShortcutModifiers.Control)),
        new(
            WorkbookShortcutRoute.FillRight,
            new WorkbookShortcutChord(WorkbookShortcutKey.R, WorkbookShortcutModifiers.Control),
            new WorkbookShortcutChord(WorkbookShortcutKey.R, WorkbookShortcutModifiers.Control)),
        new(
            WorkbookShortcutRoute.FlashFill,
            new WorkbookShortcutChord(WorkbookShortcutKey.E, WorkbookShortcutModifiers.Control),
            new WorkbookShortcutChord(WorkbookShortcutKey.E, WorkbookShortcutModifiers.Control)),
        new(
            WorkbookShortcutRoute.ToggleShowFormulas,
            new WorkbookShortcutChord(WorkbookShortcutKey.Oem3, WorkbookShortcutModifiers.Control),
            new WorkbookShortcutChord(WorkbookShortcutKey.Oem3, WorkbookShortcutModifiers.Control)),
        new(
            WorkbookShortcutRoute.OpenFormatCells,
            new WorkbookShortcutChord(WorkbookShortcutKey.D1, WorkbookShortcutModifiers.Control),
            new WorkbookShortcutChord(WorkbookShortcutKey.D1, WorkbookShortcutModifiers.Meta)),
        new(
            WorkbookShortcutRoute.NumberFormatGeneral,
            new WorkbookShortcutChord(WorkbookShortcutKey.Oem3, WorkbookShortcutModifiers.Control | WorkbookShortcutModifiers.Shift),
            new WorkbookShortcutChord(WorkbookShortcutKey.Oem3, WorkbookShortcutModifiers.Meta | WorkbookShortcutModifiers.Shift)),
        new(
            WorkbookShortcutRoute.NumberFormatNumber,
            new WorkbookShortcutChord(WorkbookShortcutKey.D1, WorkbookShortcutModifiers.Control | WorkbookShortcutModifiers.Shift),
            new WorkbookShortcutChord(WorkbookShortcutKey.D1, WorkbookShortcutModifiers.Meta | WorkbookShortcutModifiers.Shift)),
        new(
            WorkbookShortcutRoute.NumberFormatTime,
            new WorkbookShortcutChord(WorkbookShortcutKey.D2, WorkbookShortcutModifiers.Control | WorkbookShortcutModifiers.Shift),
            new WorkbookShortcutChord(WorkbookShortcutKey.D2, WorkbookShortcutModifiers.Meta | WorkbookShortcutModifiers.Shift)),
        new(
            WorkbookShortcutRoute.NumberFormatDate,
            new WorkbookShortcutChord(WorkbookShortcutKey.D3, WorkbookShortcutModifiers.Control | WorkbookShortcutModifiers.Shift),
            new WorkbookShortcutChord(WorkbookShortcutKey.D3, WorkbookShortcutModifiers.Meta | WorkbookShortcutModifiers.Shift)),
        new(
            WorkbookShortcutRoute.NumberFormatCurrency,
            new WorkbookShortcutChord(WorkbookShortcutKey.D4, WorkbookShortcutModifiers.Control | WorkbookShortcutModifiers.Shift),
            new WorkbookShortcutChord(WorkbookShortcutKey.D4, WorkbookShortcutModifiers.Meta | WorkbookShortcutModifiers.Shift)),
        new(
            WorkbookShortcutRoute.NumberFormatPercentage,
            new WorkbookShortcutChord(WorkbookShortcutKey.D5, WorkbookShortcutModifiers.Control | WorkbookShortcutModifiers.Shift),
            new WorkbookShortcutChord(WorkbookShortcutKey.D5, WorkbookShortcutModifiers.Meta | WorkbookShortcutModifiers.Shift)),
        new(
            WorkbookShortcutRoute.NumberFormatScientific,
            new WorkbookShortcutChord(WorkbookShortcutKey.D6, WorkbookShortcutModifiers.Control | WorkbookShortcutModifiers.Shift),
            new WorkbookShortcutChord(WorkbookShortcutKey.D6, WorkbookShortcutModifiers.Meta | WorkbookShortcutModifiers.Shift)),
        new(
            WorkbookShortcutRoute.Find,
            new WorkbookShortcutChord(WorkbookShortcutKey.F, WorkbookShortcutModifiers.Control),
            new WorkbookShortcutChord(WorkbookShortcutKey.F, WorkbookShortcutModifiers.Meta)),
        new(
            WorkbookShortcutRoute.Replace,
            new WorkbookShortcutChord(WorkbookShortcutKey.H, WorkbookShortcutModifiers.Control),
            new WorkbookShortcutChord(WorkbookShortcutKey.H, WorkbookShortcutModifiers.Control)),
        new(
            WorkbookShortcutRoute.GoTo,
            new WorkbookShortcutChord(WorkbookShortcutKey.G, WorkbookShortcutModifiers.Control),
            new WorkbookShortcutChord(WorkbookShortcutKey.G, WorkbookShortcutModifiers.Control)),
        new(WorkbookShortcutRoute.GoTo, new WorkbookShortcutChord(WorkbookShortcutKey.F5)),
        new(
            WorkbookShortcutRoute.InsertFunction,
            new WorkbookShortcutChord(WorkbookShortcutKey.F3, WorkbookShortcutModifiers.Shift),
            new WorkbookShortcutChord(WorkbookShortcutKey.F3, WorkbookShortcutModifiers.Shift)),
        new(
            WorkbookShortcutRoute.AutoSum,
            new WorkbookShortcutChord(WorkbookShortcutKey.OemPlus, WorkbookShortcutModifiers.Alt),
            new WorkbookShortcutChord(WorkbookShortcutKey.OemPlus, WorkbookShortcutModifiers.Alt)),
        new(
            WorkbookShortcutRoute.WorkbookStatistics,
            new WorkbookShortcutChord(WorkbookShortcutKey.G, WorkbookShortcutModifiers.Control | WorkbookShortcutModifiers.Shift),
            new WorkbookShortcutChord(WorkbookShortcutKey.G, WorkbookShortcutModifiers.Control | WorkbookShortcutModifiers.Shift)),
        new(
            WorkbookShortcutRoute.InsertWorksheet,
            new WorkbookShortcutChord(WorkbookShortcutKey.F11, WorkbookShortcutModifiers.Shift),
            new WorkbookShortcutChord(WorkbookShortcutKey.F11, WorkbookShortcutModifiers.Shift))
    ];

    public static bool TryGetWindowsRoute(
        WorkbookShortcutKey key,
        WorkbookShortcutModifiers modifiers,
        out WorkbookShortcutRoute route)
    {
        foreach (var rule in Rules)
        {
            if (rule.WindowsChord.Key != key || rule.WindowsChord.Modifiers != modifiers)
                continue;

            route = rule.Route;
            return true;
        }

        route = default;
        return false;
    }

    public static bool TryGetNativeMenuRoute(
        WorkbookShortcutKey key,
        WorkbookShortcutModifiers modifiers,
        out WorkbookShortcutRoute route)
    {
        foreach (var rule in Rules)
        {
            if (rule.NativeMenuChord is not { } chord ||
                chord.Key != key ||
                chord.Modifiers != modifiers)
            {
                continue;
            }

            route = rule.Route;
            return true;
        }

        route = default;
        return false;
    }

    public static WorkbookShortcutChord GetNativeMenuChord(WorkbookShortcutRoute route) =>
        Rules
            .Where(rule => rule.Route == route && rule.NativeMenuChord is not null)
            .Select(rule => rule.NativeMenuChord!.Value)
            .Single();
}
