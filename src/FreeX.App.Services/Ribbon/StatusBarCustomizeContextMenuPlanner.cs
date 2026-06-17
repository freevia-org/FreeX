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
            StatusBarCustomizeMenuCommand.Title("StatusBar_CustomizeStatusBar", keyTip: "T", automationId: "StatusBarCustomizeTitleMenuItem"),
            StatusBarCustomizeMenuCommand.Separator,
            Toggle("StatusBar_CellMode", keyTip: "M", optionTag: "CellMode", automationId: "StatusBarCellModeMenuItem"),
            Toggle("StatusBar_EndMode", keyTip: "E", optionTag: "EndMode", automationId: "StatusBarEndModeMenuItem"),
            Toggle("StatusBar_SelectionMode", keyTip: "O", optionTag: "SelectionMode", automationId: "StatusBarSelectionModeMenuItem"),
            Toggle("StatusBar_PageNumber", keyTip: "P", optionTag: "PageNumber", automationId: "StatusBarPageNumberMenuItem"),
            StatusBarCustomizeMenuCommand.Separator,
            Toggle("StatusBar_Average", keyTip: "A", optionTag: "Average", automationId: "StatusBarAverageMenuItem"),
            Toggle("StatusBar_Count", keyTip: "C", optionTag: "Count", automationId: "StatusBarCountMenuItem"),
            Toggle("StatusBar_NumericalCount", keyTip: "N", optionTag: "NumericalCount", automationId: "StatusBarNumericalCountMenuItem"),
            Toggle("StatusBar_Minimum", keyTip: "I", optionTag: "Minimum", automationId: "StatusBarMinimumMenuItem"),
            Toggle("StatusBar_Maximum", keyTip: "X", optionTag: "Maximum", automationId: "StatusBarMaximumMenuItem"),
            Toggle("StatusBar_Sum", keyTip: "S", optionTag: "Sum", automationId: "StatusBarSumMenuItem"),
            StatusBarCustomizeMenuCommand.Separator,
            Toggle("StatusBar_ViewShortcuts", keyTip: "V", optionTag: "ViewShortcuts", automationId: "StatusBarViewShortcutsMenuItem"),
            Toggle("StatusBar_Zoom", keyTip: "Z", optionTag: "Zoom", automationId: "StatusBarZoomMenuItem"),
            Toggle("StatusBar_ZoomSlider", keyTip: "L", optionTag: "ZoomSlider", automationId: "StatusBarZoomSliderMenuItem"),
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
