using Avalonia.Controls;
using Avalonia.LogicalTree;
using Free.Shared.Ribbon;
using Free.Shared.Ribbon.Avalonia;
using FreeX.App.Avalonia.Ribbon;
using FreeX.App.Localization;
using FreeX.App.Presentation.Protection;
using FreeX.Core.Model;
using FreeX.Ribbon.Definitions;

namespace FreeX.App.Avalonia;

// WPF updates a few rendered ribbon buttons whose label or tooltip is not part of RibbonCommandState:
// Protect Sheet / Protect Workbook toggle between Protect and Unprotect (label, tooltip title and
// description), and every View > Window command carries a context-specific description (an explanation
// of why it is unavailable). This mirrors MainWindow.WorkbookUiState.RefreshSheetProtectionUi,
// RefreshWorkbookProtectionUi and MainWindow.ViewCommands.ApplyLiveWindowCommandState.
public sealed partial class MainWindow
{
    private static readonly Lazy<IReadOnlyDictionary<string, RibbonControl>> RibbonControlsByCommandId = new(() =>
    {
        var map = new Dictionary<string, RibbonControl>(StringComparer.Ordinal);
        foreach (var tab in AvaloniaRibbonComposition.BuildDefinition().Tabs)
        foreach (var group in tab.Groups)
        foreach (var control in group.Controls)
            map.TryAdd(control.CommandId.Value, control);
        return map;
    });

    internal RibbonCommandState GetArrangeAllRibbonState(WorkbookWindowArrangement arrangement) =>
        new(IsChecked: _session.Workbook.WindowArrangement == arrangement);

    internal void ApplyDynamicRibbonChrome()
    {
        if (_ribbonControl is null)
            return;

        var sheetPlan = ProtectionWorkflowSession.CreateSheetChromePlan(_session.ActiveSheet);
        ApplyRibbonButtonChrome(FreeXRibbonCommandIds.ReviewProtectSheet, sheetPlan);
        ApplyRibbonButtonChrome("Protect Workbook", ProtectionWorkflowSession.CreateWorkbookChromePlan(_session.Workbook));

        var visibleCount = WindowRegistry.VisibleCount;
        ApplyRibbonButtonDescription("New Window", UiText.Get("MainWindow_TooltipDescription_OpenAnotherLiveWindowForThisWorkbook"));

        var multiple = visibleCount > 1;
        ApplyRibbonButtonDescription("Switch Windows", UiText.Get(multiple
            ? "MainWindow_TooltipDescription_SwitchToAnotherVisibleWorkbookWindow"
            : "MainWindow_TooltipDescription_UnavailableSwitchWindowsRequiresSecondVisibleWindow"));
        ApplyRibbonButtonDescription("Hide", UiText.Get(multiple
            ? "MainWindow_TooltipDescription_HideThisWorkbookWindowFromView"
            : "MainWindow_TooltipDescription_UnavailableHideRequiresSecondVisibleWindow"));
        var canUnhide = WindowRegistry.HiddenWindows.Count > 0;
        ApplyRibbonButtonDescription("Unhide", UiText.Get(canUnhide
            ? "MainWindow_TooltipDescription_RestoreAHiddenWorkbookWindow"
            : "MainWindow_TooltipDescription_UnavailableUnhideRequiresAHiddenWindow"));

        var sideBySide = GetSideBySideCommandStatePlan();
        ApplyRibbonButtonDescription("Reset Window Position",
            UiText.Get("MainWindow_TooltipDescription_ResetThisWindowToAStandardSizeAndPosition"));
        ApplyRibbonButtonDescription("View Side by Side", UiText.Get(sideBySide.ViewSideBySideEnabled
            ? "MainWindow_TooltipDescription_TileThisWindowAndAnotherSideBySideToCompareThem"
            : "MainWindow_TooltipDescription_UnavailableViewSideBySideRequiresSecondVisibleWindow"));
        ApplyRibbonButtonDescription("Synchronous Scrolling", UiText.Get(sideBySide.SynchronousScrollingEnabled
            ? "MainWindow_TooltipDescription_ScrollBothSideBySideWindowsTogether"
            : "MainWindow_TooltipDescription_UnavailableSynchronousScrollingRequiresViewSideBySide"));
    }

    private void ApplyRibbonButtonChrome(string commandId, ProtectionChromePlan plan)
    {
        var label = UiText.Get(plan.ButtonContentResourceKey);
        foreach (var button in FindRibbonButtons(commandId))
        {
            var caption = button.GetLogicalDescendants().OfType<TextBlock>()
                .FirstOrDefault(text => !string.IsNullOrEmpty(text.Text));
            if (caption is not null && !string.Equals(caption.Text, label, StringComparison.Ordinal))
                caption.Text = label;
            SetRibbonButtonTooltip(button, commandId, UiText.Get(plan.TooltipTitleResourceKey), UiText.Get(plan.TooltipDescriptionResourceKey));
        }
    }

    private void ApplyRibbonButtonDescription(string commandId, string description)
    {
        foreach (var button in FindRibbonButtons(commandId))
            SetRibbonButtonTooltip(button, commandId, title: null, description);
    }

    internal IEnumerable<Button> FindRibbonButtons(string commandId) =>
        (_ribbonControl is { } ribbon ? EnumerateRibbonControls(ribbon) : []).OfType<Button>()
            .Where(button => string.Equals(button.Tag as string, commandId, StringComparison.Ordinal));

    private static void SetRibbonButtonTooltip(Button button, string commandId, string? title, string description)
    {
        RibbonControlsByCommandId.Value.TryGetValue(commandId, out var control);
        var resolvedTitle = title
            ?? (!string.IsNullOrEmpty(control?.TooltipTitle) ? control!.TooltipTitle : control?.Label)
            ?? commandId;
        ToolTip.SetTip(button, AvaloniaRibbonRenderer.CreateRibbonTooltipContent(resolvedTitle, description, control?.KeyTip));
    }
}
