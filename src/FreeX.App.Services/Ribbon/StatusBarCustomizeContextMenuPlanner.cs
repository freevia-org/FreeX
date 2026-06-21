namespace FreeX.App.Services.Ribbon;

/// <summary>
/// Platform-neutral planner that describes the status-bar "Customize Status Bar" right-click menu as a
/// declarative command tree, mirroring <see cref="SheetTabContextMenuPlanner"/>. The menu is the Excel-style
/// list of toggles that control which status-bar fields (Cell Mode, aggregates such as Average/Count/Sum,
/// zoom controls, …) are shown. Each toggle carries its persisted-option <see cref="OptionTag"/> (matching
/// the value the host's <c>StatusBarCustomizeMenuItem_Click</c> switch reads from <c>Tag</c>) and its
/// stable <see cref="AutomationId"/>. The host resolves <see cref="ResourceKey"/> to a localized header,
/// applies the explicit <see cref="KeyTip"/>, and renders <see cref="IsCheckable"/> items with the live
/// checked state supplied at open time.
/// </summary>
public static class StatusBarCustomizeContextMenuPlanner
{
    private static readonly IReadOnlyList<StatusBarCustomizeMenuCommand> Commands = BuildCommands();

    public static IReadOnlyList<StatusBarCustomizeMenuCommand> BuildStatusBarCustomizeCommands() => Commands;

    private static IReadOnlyList<StatusBarCustomizeMenuCommand> BuildCommands() =>
        Array.AsReadOnly(new[]
        {
            StatusBarCustomizeMenuCommand.Title(StatusBarCustomizeResourceKeys.CustomizeStatusBar, keyTip: "T", automationId: "StatusBarCustomizeTitleMenuItem"),
            StatusBarCustomizeMenuCommand.Separator,
            Toggle(StatusBarCustomizeResourceKeys.CellMode, keyTip: "M", optionTag: StatusBarOptionTags.CellMode, automationId: "StatusBarCellModeMenuItem"),
            Toggle(StatusBarCustomizeResourceKeys.EndMode, keyTip: "E", optionTag: StatusBarOptionTags.EndMode, automationId: "StatusBarEndModeMenuItem"),
            Toggle(StatusBarCustomizeResourceKeys.SelectionMode, keyTip: "O", optionTag: StatusBarOptionTags.SelectionMode, automationId: "StatusBarSelectionModeMenuItem"),
            Toggle(StatusBarCustomizeResourceKeys.PageNumber, keyTip: "P", optionTag: StatusBarOptionTags.PageNumber, automationId: "StatusBarPageNumberMenuItem"),
            StatusBarCustomizeMenuCommand.Separator,
            Toggle(StatusBarCustomizeResourceKeys.Average, keyTip: "A", optionTag: StatusBarOptionTags.Average, automationId: "StatusBarAverageMenuItem"),
            Toggle(StatusBarCustomizeResourceKeys.Count, keyTip: "C", optionTag: StatusBarOptionTags.Count, automationId: "StatusBarCountMenuItem"),
            Toggle(StatusBarCustomizeResourceKeys.NumericalCount, keyTip: "N", optionTag: StatusBarOptionTags.NumericalCount, automationId: "StatusBarNumericalCountMenuItem"),
            Toggle(StatusBarCustomizeResourceKeys.Minimum, keyTip: "I", optionTag: StatusBarOptionTags.Minimum, automationId: "StatusBarMinimumMenuItem"),
            Toggle(StatusBarCustomizeResourceKeys.Maximum, keyTip: "X", optionTag: StatusBarOptionTags.Maximum, automationId: "StatusBarMaximumMenuItem"),
            Toggle(StatusBarCustomizeResourceKeys.Sum, keyTip: "S", optionTag: StatusBarOptionTags.Sum, automationId: "StatusBarSumMenuItem"),
            StatusBarCustomizeMenuCommand.Separator,
            Toggle(StatusBarCustomizeResourceKeys.ViewShortcuts, keyTip: "V", optionTag: StatusBarOptionTags.ViewShortcuts, automationId: "StatusBarViewShortcutsMenuItem"),
            Toggle(StatusBarCustomizeResourceKeys.Zoom, keyTip: "Z", optionTag: StatusBarOptionTags.Zoom, automationId: "StatusBarZoomMenuItem"),
            Toggle(StatusBarCustomizeResourceKeys.ZoomSlider, keyTip: "L", optionTag: StatusBarOptionTags.ZoomSlider, automationId: "StatusBarZoomSliderMenuItem"),
        });

    private static StatusBarCustomizeMenuCommand Toggle(string resourceKey, string keyTip, string optionTag, string automationId) =>
        new(resourceKey, IsCheckable: true, KeyTip: keyTip, OptionTag: optionTag, AutomationId: automationId);
}

public sealed record StatusBarCustomizeMenuCommand(
    string ResourceKey,
    bool IsSeparator = false,
    bool IsCheckable = false,
    bool IsEnabled = true,
    string? KeyTip = null,
    string? OptionTag = null,
    string? AutomationId = null)
{
    public static StatusBarCustomizeMenuCommand Separator { get; } =
        new("", IsSeparator: true, IsEnabled: false);

    public static StatusBarCustomizeMenuCommand Title(string resourceKey, string keyTip, string automationId) =>
        new(resourceKey, IsEnabled: false, KeyTip: keyTip, AutomationId: automationId);

    public string KeyTip { get; init; } = KeyTip ?? "";

    public string OptionTag { get; init; } = OptionTag ?? "";

    public string AutomationId { get; init; } = AutomationId ?? "";
}
