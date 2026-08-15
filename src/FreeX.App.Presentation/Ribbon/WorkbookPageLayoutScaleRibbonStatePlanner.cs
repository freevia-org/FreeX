using Free.Shared.Ribbon;
using FreeX.App.Presentation.PageLayout;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Ribbon;

/// <summary>
/// Renderer-neutral value projection for the three Page Layout Scale to Fit ribbon controls.
/// Keeping the display values together prevents WPF's native ComboBoxes and Avalonia's value
/// commands from drifting when the active sheet or scaling mode changes.
/// </summary>
public readonly record struct WorkbookPageLayoutScaleRibbonStatePlan(
    string WidthValue,
    string HeightValue,
    string PercentValue)
{
    public RibbonCommandState GetCommandState(string commandId) => commandId switch
    {
        "Scale Width" => new RibbonCommandState(Value: WidthValue),
        "Scale Height" => new RibbonCommandState(Value: HeightValue),
        "Scale Percent" => new RibbonCommandState(Value: PercentValue),
        _ => RibbonCommandState.Default,
    };

    public void Publish(IRibbonStateStore stateStore)
    {
        ArgumentNullException.ThrowIfNull(stateStore);

        stateStore.SetValue("Scale Width", WidthValue);
        stateStore.SetValue("Scale Height", HeightValue);
        stateStore.SetValue("Scale Percent", PercentValue);
    }
}

public static class WorkbookPageLayoutScaleRibbonStatePlanner
{
    public static WorkbookPageLayoutScaleRibbonStatePlan Build(WorksheetScaleToFit? scaleToFit)
    {
        var effective = scaleToFit ?? WorksheetScaleToFit.Default;
        return new WorkbookPageLayoutScaleRibbonStatePlan(
            PageLayoutInputParser.FormatScalePages(effective.FitToPagesWide),
            PageLayoutInputParser.FormatScalePages(effective.FitToPagesTall),
            PageLayoutInputParser.FormatScalePercent(effective.ScalePercent));
    }
}
