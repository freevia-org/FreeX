namespace FreeX.App.Presentation.Shell;

public enum NativeMenuTopLevelId
{
    File,
    Home,
    Insert,
    PageLayout,
    Formulas,
    Data,
    Review,
    View,
    Sheet,
    Window,
    Help
}

public enum NativeFileMenuItemId
{
    NewWorkbook,
    Open,
    OpenRecent,
    ShareWorkbook,
    BackstageInfo,
    Save,
    SaveAs,
    Print,
    PrintPreview,
    BackstageExport,
    ExportPdf,
    WorkbookStatistics,
    PageSetup,
    CloseWorkbook,
    BackstageAccount,
    Options,
    Quit
}

public enum NativeMenuEntryKind
{
    Item,
    Separator
}

public enum NativeMenuGestureKey
{
    N,
    O,
    S,
    P,
    G,
    W,
    Q,
    OemComma
}

[Flags]
public enum NativeMenuGestureModifiers
{
    None = 0,
    Control = 1,
    Alt = 2,
    Shift = 4,
    Meta = 8
}

public sealed record NativeMenuTopLevelPlan(
    NativeMenuTopLevelId Id,
    string Header);

public sealed record NativeMenuGesturePlan(
    NativeMenuGestureKey Key,
    NativeMenuGestureModifiers Modifiers = NativeMenuGestureModifiers.None);

public sealed record NativeFileMenuItemPlan(
    NativeFileMenuItemId Id,
    string Label,
    NativeMenuGesturePlan? Gesture = null,
    bool UsesResourceKey = true,
    bool RequiresGestureInSmoke = true);

public sealed record NativeFileMenuEntryPlan(
    NativeMenuEntryKind Kind,
    NativeFileMenuItemPlan? Item)
{
    public static NativeFileMenuEntryPlan ForItem(NativeFileMenuItemPlan item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return new NativeFileMenuEntryPlan(NativeMenuEntryKind.Item, item);
    }

    public static NativeFileMenuEntryPlan Separator { get; } =
        new(NativeMenuEntryKind.Separator, Item: null);
}

public sealed record NativeFileMenuAvailabilityContext(
    bool IsIdle,
    bool CanOpen,
    bool CanSave,
    bool CanSaveAs,
    bool CanSaveThroughStorageProvider);

public sealed record NativeFileMenuAvailabilityItem(
    NativeFileMenuItemId Id,
    bool IsEnabled);

public sealed record NativeFileMenuAvailabilityPlan(
    IReadOnlyList<NativeFileMenuAvailabilityItem> Items)
{
    public bool IsEnabled(NativeFileMenuItemId id) =>
        Items.First(item => item.Id == id).IsEnabled;
}

public static class NativeMenuCatalog
{
    private static readonly NativeFileMenuItemPlan[] FileMenuItems =
    [
        new(
            NativeFileMenuItemId.NewWorkbook,
            "AvaloniaNativeMenu_NewWorkbook",
            new NativeMenuGesturePlan(NativeMenuGestureKey.N, NativeMenuGestureModifiers.Meta)),
        new(
            NativeFileMenuItemId.Open,
            "AvaloniaNativeMenu_Open",
            new NativeMenuGesturePlan(NativeMenuGestureKey.O, NativeMenuGestureModifiers.Meta)),
        new(
            NativeFileMenuItemId.OpenRecent,
            "AvaloniaNativeMenu_OpenRecent",
            Gesture: null,
            RequiresGestureInSmoke: false),
        new(
            NativeFileMenuItemId.ShareWorkbook,
            "AvaloniaNativeMenu_ShareWorkbook",
            Gesture: null,
            RequiresGestureInSmoke: false),
        new(
            NativeFileMenuItemId.BackstageInfo,
            "Backstage_Info_MenuItem",
            Gesture: null,
            RequiresGestureInSmoke: false),
        new(
            NativeFileMenuItemId.Save,
            "AvaloniaNativeMenu_Save",
            new NativeMenuGesturePlan(NativeMenuGestureKey.S, NativeMenuGestureModifiers.Meta)),
        new(
            NativeFileMenuItemId.SaveAs,
            "AvaloniaNativeMenu_SaveAs",
            new NativeMenuGesturePlan(
                NativeMenuGestureKey.S,
                NativeMenuGestureModifiers.Meta | NativeMenuGestureModifiers.Shift)),
        new(
            NativeFileMenuItemId.Print,
            "Print_MenuItem",
            new NativeMenuGesturePlan(NativeMenuGestureKey.P, NativeMenuGestureModifiers.Meta)),
        new(
            NativeFileMenuItemId.PrintPreview,
            "AvaloniaNativeMenu_PrintPreview",
            new NativeMenuGesturePlan(
                NativeMenuGestureKey.P,
                NativeMenuGestureModifiers.Meta | NativeMenuGestureModifiers.Shift)),
        new(
            NativeFileMenuItemId.BackstageExport,
            "Backstage_Export_MenuItem",
            Gesture: null,
            RequiresGestureInSmoke: false),
        new(
            NativeFileMenuItemId.ExportPdf,
            "AvaloniaNativeMenu_ExportPdf",
            Gesture: null,
            RequiresGestureInSmoke: false),
        new(
            NativeFileMenuItemId.WorkbookStatistics,
            "AvaloniaNativeMenu_WorkbookStatistics",
            new NativeMenuGesturePlan(
                NativeMenuGestureKey.G,
                NativeMenuGestureModifiers.Control | NativeMenuGestureModifiers.Shift)),
        new(
            NativeFileMenuItemId.PageSetup,
            "AvaloniaNativeMenu_PageSetup",
            Gesture: null,
            RequiresGestureInSmoke: false),
        new(
            NativeFileMenuItemId.CloseWorkbook,
            "AvaloniaNativeMenu_CloseWorkbook",
            new NativeMenuGesturePlan(NativeMenuGestureKey.W, NativeMenuGestureModifiers.Meta)),
        new(
            NativeFileMenuItemId.BackstageAccount,
            "Backstage_Account_MenuItem",
            Gesture: null,
            RequiresGestureInSmoke: false),
        new(
            NativeFileMenuItemId.Options,
            "Options_Title",
            new NativeMenuGesturePlan(NativeMenuGestureKey.OemComma, NativeMenuGestureModifiers.Meta)),
        new(
            NativeFileMenuItemId.Quit,
            "Quit FreeX",
            new NativeMenuGesturePlan(NativeMenuGestureKey.Q, NativeMenuGestureModifiers.Meta),
            UsesResourceKey: false)
    ];

    public static IReadOnlyList<NativeMenuTopLevelPlan> TopLevelMenus { get; } =
    [
        new(NativeMenuTopLevelId.File, "File"),
        new(NativeMenuTopLevelId.Home, "Home"),
        new(NativeMenuTopLevelId.Insert, "Insert"),
        new(NativeMenuTopLevelId.PageLayout, "Page Layout"),
        new(NativeMenuTopLevelId.Formulas, "Formulas"),
        new(NativeMenuTopLevelId.Data, "Data"),
        new(NativeMenuTopLevelId.Review, "Review"),
        new(NativeMenuTopLevelId.View, "View"),
        new(NativeMenuTopLevelId.Sheet, "Sheet"),
        new(NativeMenuTopLevelId.Window, "Window"),
        new(NativeMenuTopLevelId.Help, "Help")
    ];

    public static IReadOnlyList<NativeFileMenuEntryPlan> FileMenuEntries { get; } =
    [
        FileItem(NativeFileMenuItemId.NewWorkbook),
        FileItem(NativeFileMenuItemId.Open),
        FileItem(NativeFileMenuItemId.OpenRecent),
        FileItem(NativeFileMenuItemId.ShareWorkbook),
        NativeFileMenuEntryPlan.Separator,
        FileItem(NativeFileMenuItemId.BackstageInfo),
        FileItem(NativeFileMenuItemId.Save),
        FileItem(NativeFileMenuItemId.SaveAs),
        NativeFileMenuEntryPlan.Separator,
        FileItem(NativeFileMenuItemId.Print),
        FileItem(NativeFileMenuItemId.PrintPreview),
        FileItem(NativeFileMenuItemId.BackstageExport),
        FileItem(NativeFileMenuItemId.ExportPdf),
        FileItem(NativeFileMenuItemId.WorkbookStatistics),
        FileItem(NativeFileMenuItemId.PageSetup),
        NativeFileMenuEntryPlan.Separator,
        FileItem(NativeFileMenuItemId.CloseWorkbook),
        NativeFileMenuEntryPlan.Separator,
        FileItem(NativeFileMenuItemId.BackstageAccount),
        FileItem(NativeFileMenuItemId.Options),
        NativeFileMenuEntryPlan.Separator,
        FileItem(NativeFileMenuItemId.Quit)
    ];

    public static NativeFileMenuItemPlan GetFileMenuItem(NativeFileMenuItemId id) =>
        FileMenuItems.First(item => item.Id == id);

    public static NativeFileMenuAvailabilityPlan PlanFileMenuAvailability(
        NativeFileMenuAvailabilityContext context) =>
        new(
        [
            new(NativeFileMenuItemId.NewWorkbook, context.IsIdle),
            new(NativeFileMenuItemId.Open, context.CanOpen),
            new(NativeFileMenuItemId.OpenRecent, context.IsIdle),
            new(NativeFileMenuItemId.ShareWorkbook, context.IsIdle),
            new(NativeFileMenuItemId.BackstageInfo, context.IsIdle),
            new(NativeFileMenuItemId.Save, context.CanSave),
            new(NativeFileMenuItemId.SaveAs, context.CanSaveAs),
            new(NativeFileMenuItemId.Print, context.IsIdle),
            new(NativeFileMenuItemId.PrintPreview, context.IsIdle),
            new(NativeFileMenuItemId.BackstageExport, context.IsIdle && context.CanSaveThroughStorageProvider),
            new(NativeFileMenuItemId.ExportPdf, context.IsIdle && context.CanSaveThroughStorageProvider),
            new(NativeFileMenuItemId.WorkbookStatistics, context.IsIdle),
            new(NativeFileMenuItemId.PageSetup, context.IsIdle),
            new(NativeFileMenuItemId.CloseWorkbook, context.IsIdle),
            new(NativeFileMenuItemId.BackstageAccount, context.IsIdle),
            new(NativeFileMenuItemId.Options, true),
            new(NativeFileMenuItemId.Quit, true)
        ]);

    private static NativeFileMenuEntryPlan FileItem(NativeFileMenuItemId id) =>
        NativeFileMenuEntryPlan.ForItem(GetFileMenuItem(id));
}
