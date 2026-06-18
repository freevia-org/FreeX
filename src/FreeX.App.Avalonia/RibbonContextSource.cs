using Free.Shared.Ribbon;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia;

/// <summary>
/// The Avalonia shell's <see cref="IRibbonContextSource"/>: it owns what counts as a selection context
/// (a chart/picture/shape is selected, a table/pivot is active) and raises <see cref="ContextChanged"/>
/// whenever that set changes, so the renderer can show/hide the matching contextual ribbon tabs.
/// Activation keys match the shared FreeX ribbon definition (chart.selected / picture.selected /
/// shape.selected / table.active / pivot.active).
/// </summary>
internal sealed class AvaloniaRibbonContextSource : IRibbonContextSource
{
    // Drawing-object selection contributes at most one key at a time; table/pivot are independent flags.
    private string? _drawingObjectKey;
    private bool _tableActive;
    private bool _pivotActive;

    public RibbonContextState Current { get; private set; } = RibbonContextState.None;

    public event EventHandler? ContextChanged;

    /// <summary>A drawing object was selected: map its kind to the contextual tab's activation key.</summary>
    public void OnDrawingObjectSelected(SelectionPaneObjectKind kind)
        => SetDrawingObjectKey(MapDrawingObjectKey(kind));

    /// <summary>The active cell entered/left a structured table.</summary>
    public void OnTableActive(bool active)
    {
        if (_tableActive == active)
            return;
        _tableActive = active;
        Recompute();
    }

    /// <summary>The active cell entered/left a PivotTable.</summary>
    public void OnPivotActive(bool active)
    {
        if (_pivotActive == active)
            return;
        _pivotActive = active;
        Recompute();
    }

    /// <summary>The selection was cleared: drop any drawing-object context.</summary>
    public void OnSelectionCleared() => SetDrawingObjectKey(null);

    private static string MapDrawingObjectKey(SelectionPaneObjectKind kind) => kind switch
    {
        SelectionPaneObjectKind.Chart => "chart.selected",
        SelectionPaneObjectKind.Picture => "picture.selected",
        SelectionPaneObjectKind.Shape => "shape.selected",
        // Text boxes are shapes for ribbon purposes (the shared definition has no textbox.selected tab).
        SelectionPaneObjectKind.TextBox => "shape.selected",
        _ => "shape.selected",
    };

    private void SetDrawingObjectKey(string? key)
    {
        if (string.Equals(_drawingObjectKey, key, StringComparison.Ordinal))
            return;
        _drawingObjectKey = key;
        Recompute();
    }

    private void Recompute()
    {
        var state = RibbonContextState.None;
        if (_drawingObjectKey is not null)
            state = state.With(_drawingObjectKey);
        if (_tableActive)
            state = state.With("table.active");
        if (_pivotActive)
            state = state.With("pivot.active");

        Current = state;
        ContextChanged?.Invoke(this, EventArgs.Empty);
    }
}
