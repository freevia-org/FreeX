namespace FreeX.App.Presentation.Backstage;

public enum FreeXBackstageRecentTabId
{
    Recent,
    Pinned
}

public enum FreeXBackstageRecentColumnId
{
    Name,
    DateModified
}

public enum FreeXBackstageRecentFileRowKind
{
    Recent,
    Pinned
}

public enum FreeXBackstageRecentFileCommandId
{
    Pin,
    Unpin
}

public sealed record FreeXBackstageRecentTabDescriptor(
    FreeXBackstageRecentTabId Id,
    string LabelKey,
    string TooltipTitleKey,
    string KeyTip,
    string CommandName);

public sealed record FreeXBackstageRecentSearchDescriptor(
    string AutomationNameKey,
    string AutomationHelpTextKey);

public sealed record FreeXBackstageRecentColumnDescriptor(
    FreeXBackstageRecentColumnId Id,
    string LabelKey);

public sealed record FreeXBackstageRecentFileRowDescriptor(
    FreeXBackstageRecentFileRowKind Kind,
    string AutomationId);

public sealed record FreeXBackstageRecentFileCommandDescriptor(
    FreeXBackstageRecentFileCommandId Id,
    string TooltipTitleKey,
    string TooltipDescriptionKey,
    string AutomationId,
    string ToolTipKey,
    string CommandName,
    string IconCommandName);

public sealed record FreeXBackstageHomePanePlan(
    FreeXBackstageRecentTabDescriptor RecentTab,
    FreeXBackstageRecentTabDescriptor PinnedTab,
    FreeXBackstageRecentSearchDescriptor Search,
    IReadOnlyList<FreeXBackstageRecentColumnDescriptor> Columns,
    IReadOnlyList<FreeXBackstageRecentFileRowDescriptor> Rows,
    IReadOnlyList<FreeXBackstageRecentFileCommandDescriptor> RowCommands);

/// <summary>
/// Owns FreeX-specific Backstage Home/Recent pane descriptors. The generic shared-shell recent planner
/// still shapes MRU data; this planner owns FreeX labels, key tips, automation ids, and command names.
/// </summary>
public static class FreeXBackstageHomePanePlanner
{
    public static FreeXBackstageHomePanePlan Build() =>
        new(
            new FreeXBackstageRecentTabDescriptor(
                FreeXBackstageRecentTabId.Recent,
                "MainWindow_Text_Recent",
                "MainWindow_TooltipTitle_Recent",
                "RC",
                "Recent"),
            new FreeXBackstageRecentTabDescriptor(
                FreeXBackstageRecentTabId.Pinned,
                "MainWindow_Text_Pinned",
                "MainWindow_TooltipTitle_Pinned",
                "PN",
                "Pinned"),
            new FreeXBackstageRecentSearchDescriptor(
                "MainWindow_AutomationName_SearchRecentFiles",
                "MainWindow_AutomationHelpText_FilterRecentAndPinnedFiles"),
            [
                new(FreeXBackstageRecentColumnId.Name, "MainWindow_Text_Name"),
                new(FreeXBackstageRecentColumnId.DateModified, "MainWindow_Text_DateModified"),
            ],
            [
                new(FreeXBackstageRecentFileRowKind.Recent, "BackstageRecentFileItem"),
                new(FreeXBackstageRecentFileRowKind.Pinned, "BackstagePinnedFileItem"),
            ],
            [
                new(
                    FreeXBackstageRecentFileCommandId.Pin,
                    "MainWindow_TooltipTitle_PinFile",
                    "MainWindow_TooltipDescription_PinOrUnpinThisWorkbookInTheRecentFilesList",
                    "BackstageRecentPinButton",
                    "MainWindow_ToolTip_PinToList",
                    "Pin File",
                    "Pin to list"),
                new(
                    FreeXBackstageRecentFileCommandId.Unpin,
                    "MainWindow_TooltipTitle_UnpinFile",
                    "MainWindow_TooltipDescription_RemoveThisWorkbookFromThePinnedFilesList",
                    "BackstagePinnedUnpinButton",
                    "MainWindow_ToolTip_UnpinFromList",
                    "Unpin File",
                    "Unpin from list"),
            ]);
}
