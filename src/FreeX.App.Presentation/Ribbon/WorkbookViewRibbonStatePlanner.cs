using Free.Shared.Ribbon;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Ribbon;

/// <summary>
/// Renderer-neutral checked/enabled projection for worksheet View and Page Layout ribbon controls.
/// A workbook can be open in multiple windows, so callers must pass the effective state for the
/// current window rather than reading the shared <see cref="Sheet"/> fields directly.
/// </summary>
public readonly record struct WorkbookViewRibbonStatePlan(
    bool GridlinesChecked,
    bool HeadingsChecked,
    bool RulerChecked,
    bool RulerEnabled,
    bool ShowFormulasChecked,
    bool SplitChecked,
    bool NormalChecked,
    bool PageLayoutChecked,
    bool PageBreakPreviewChecked)
{
    public RibbonCommandState GetCommandState(string commandId) => commandId switch
    {
        "Gridlines" or "View Gridlines" => new RibbonCommandState(IsChecked: GridlinesChecked),
        "Headings" or "View Headings" => new RibbonCommandState(IsChecked: HeadingsChecked),
        "Ruler" => new RibbonCommandState(IsEnabled: RulerEnabled, IsChecked: RulerChecked),
        "Show Formulas" => new RibbonCommandState(IsChecked: ShowFormulasChecked),
        "Split" => new RibbonCommandState(IsChecked: SplitChecked),
        "Normal" => new RibbonCommandState(IsChecked: NormalChecked),
        "Page Layout" => new RibbonCommandState(IsChecked: PageLayoutChecked),
        "Page Break Preview" => new RibbonCommandState(IsChecked: PageBreakPreviewChecked),
        _ => RibbonCommandState.Default,
    };

    public void Publish(IRibbonStateStore stateStore)
    {
        ArgumentNullException.ThrowIfNull(stateStore);

        Publish(stateStore, "Gridlines");
        Publish(stateStore, "View Gridlines");
        Publish(stateStore, "Headings");
        Publish(stateStore, "View Headings");
        Publish(stateStore, "Ruler");
        Publish(stateStore, "Show Formulas");
        Publish(stateStore, "Split");
        Publish(stateStore, "Normal");
        Publish(stateStore, "Page Layout");
        Publish(stateStore, "Page Break Preview");
    }

    private void Publish(IRibbonStateStore stateStore, string commandId)
    {
        var state = GetCommandState(commandId);
        if (commandId == "Ruler")
            stateStore.SetEnabled(commandId, state.IsEnabled);
        stateStore.SetChecked(commandId, state.IsChecked);
    }
}

public static class WorkbookViewRibbonStatePlanner
{
    public static WorkbookViewRibbonStatePlan Build(
        WorksheetViewMode viewMode,
        bool showGridlines,
        bool showHeadings,
        bool showRulers,
        bool showFormulas,
        bool isSplit) =>
        new(
            GridlinesChecked: showGridlines,
            HeadingsChecked: showHeadings,
            RulerChecked: showRulers,
            RulerEnabled: viewMode == WorksheetViewMode.PageLayout,
            ShowFormulasChecked: showFormulas,
            SplitChecked: isSplit,
            NormalChecked: viewMode == WorksheetViewMode.Normal,
            PageLayoutChecked: viewMode == WorksheetViewMode.PageLayout,
            PageBreakPreviewChecked: viewMode == WorksheetViewMode.PageBreakPreview);
}
