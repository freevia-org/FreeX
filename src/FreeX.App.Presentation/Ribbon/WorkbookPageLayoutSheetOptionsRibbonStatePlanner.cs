using Free.Shared.Ribbon;

namespace FreeX.App.Presentation.Ribbon;

/// <summary>
/// Renderer-neutral checked-state projection for Page Layout > Sheet Options.
/// The four canonical controls are independent checkboxes: View state belongs to the
/// workbook-window session, while Print state belongs to the active worksheet.
/// </summary>
public readonly record struct WorkbookPageLayoutSheetOptionsRibbonStatePlan(
    bool ViewGridlinesChecked,
    bool PrintGridlinesChecked,
    bool ViewHeadingsChecked,
    bool PrintHeadingsChecked)
{
    public RibbonCommandState GetCommandState(string commandId) => commandId switch
    {
        "View Gridlines" => new RibbonCommandState(IsChecked: ViewGridlinesChecked),
        "Print Gridlines" => new RibbonCommandState(IsChecked: PrintGridlinesChecked),
        "View Headings" => new RibbonCommandState(IsChecked: ViewHeadingsChecked),
        "Print Headings" => new RibbonCommandState(IsChecked: PrintHeadingsChecked),
        _ => RibbonCommandState.Default,
    };

    public void Publish(IRibbonStateStore stateStore)
    {
        ArgumentNullException.ThrowIfNull(stateStore);

        stateStore.SetChecked("View Gridlines", ViewGridlinesChecked);
        stateStore.SetChecked("Print Gridlines", PrintGridlinesChecked);
        stateStore.SetChecked("View Headings", ViewHeadingsChecked);
        stateStore.SetChecked("Print Headings", PrintHeadingsChecked);
    }
}

public static class WorkbookPageLayoutSheetOptionsRibbonStatePlanner
{
    public static WorkbookPageLayoutSheetOptionsRibbonStatePlan Build(
        bool viewGridlines,
        bool printGridlines,
        bool viewHeadings,
        bool printHeadings) =>
        new(viewGridlines, printGridlines, viewHeadings, printHeadings);
}
