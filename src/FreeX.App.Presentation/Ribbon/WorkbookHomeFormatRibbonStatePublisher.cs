using Free.Shared.Ribbon;
using CellHAlign = FreeX.Core.Model.HorizontalAlignment;
using CellVAlign = FreeX.Core.Model.VerticalAlignment;

namespace FreeX.App.Presentation.Ribbon;

/// <summary>
/// Publishes the active-cell formatting projection consumed by the Home ribbon. The mapping from model
/// formatting to canonical command ids is renderer-neutral so WPF and Avalonia cannot drift on which
/// alignment, font, or wrapping controls appear selected.
/// </summary>
public static class WorkbookHomeFormatRibbonStatePublisher
{
    public static void Publish(IRibbonStateStore stateStore, ToolbarVisualState state)
    {
        ArgumentNullException.ThrowIfNull(stateStore);
        ArgumentNullException.ThrowIfNull(state);

        stateStore.SetChecked("Bold", state.Bold);
        stateStore.SetChecked("Italic", state.Italic);
        stateStore.SetChecked("Underline", state.Underline);
        stateStore.SetChecked("Strikethrough", state.Strikethrough);
        stateStore.SetChecked("Top Align", state.VerticalAlignment == CellVAlign.Top);
        stateStore.SetChecked("Middle Align", state.VerticalAlignment == CellVAlign.Center);
        stateStore.SetChecked("Bottom Align", state.VerticalAlignment == CellVAlign.Bottom);
        stateStore.SetChecked("Align Left", state.HorizontalAlignment == CellHAlign.Left);
        stateStore.SetChecked("Center", state.HorizontalAlignment == CellHAlign.Center);
        stateStore.SetChecked("Align Right", state.HorizontalAlignment == CellHAlign.Right);
        stateStore.SetChecked("Wrap Text", state.WrapText);
        stateStore.SetValue("Font", state.FontName);
        stateStore.SetValue("Font Size", state.FontSizeText);
    }
}
