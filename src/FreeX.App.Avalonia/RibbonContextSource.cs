using Free.Shared.Ribbon;
using FreeX.App.Presentation.DrawingUI;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia;

/// <summary>
/// The Avalonia shell's <see cref="IRibbonContextSource"/>: it owns what counts as a selection context
/// (a chart/picture/shape is selected, a table/pivot is active) and raises <see cref="ContextChanged"/>
/// whenever that set changes, so the renderer can show/hide the matching contextual ribbon tabs.
/// Activation keys match the shared FreeX ribbon definition (chart.selected / picture.selected /
/// shape.selected / table.active / pivot.active).
/// </summary>
internal sealed partial class AvaloniaRibbonContextSource : IRibbonContextSource
{
    // Drawing-object selection contributes at most one key at a time; table/pivot are independent flags.
    private string? _drawingObjectKey;
    private bool _tableActive;
    private bool _pivotActive;

    public RibbonContextState Current { get; private set; } = RibbonContextState.None;

    public event EventHandler? ContextChanged;

    /// <summary>A drawing object was selected: map its kind to the contextual tab's activation key.</summary>
    public void OnDrawingObjectSelected(SelectionPaneObjectKind kind)
    {
        if (ShouldSuppressOptionalContextMutation())
            return;
        SetDrawingObjectKey(DrawingObjectContextualRibbonPlanner.ResolveActivationKey(kind));
    }

    /// <summary>The active cell entered/left a structured table.</summary>
    public void OnTableActive(bool active)
    {
        if (ShouldSuppressOptionalContextMutation())
            return;
        if (_tableActive == active)
            return;
        _tableActive = active;
        Recompute();
    }

    /// <summary>The active cell entered/left a PivotTable.</summary>
    public void OnPivotActive(bool active)
    {
        if (ShouldSuppressOptionalContextMutation())
            return;
        if (_pivotActive == active)
            return;
        _pivotActive = active;
        Recompute();
    }

    /// <summary>
    /// The drawing selection was cleared. Cell-based contexts are refreshed by the host because this
    /// source does not own the active-cell/table/pivot accessors.
    /// </summary>
    public void OnSelectionCleared()
    {
        if (ShouldSuppressOptionalContextMutation())
            return;
        SetDrawingObjectKey(null);
    }

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
            state = state.With(DrawingObjectContextualRibbonPlanner.TableContextKey);
        if (_pivotActive)
            state = state.With(DrawingObjectContextualRibbonPlanner.PivotContextKey);

        ApplyOptionalContextOverride(ref state);
        Current = state;
        ContextChanged?.Invoke(this, EventArgs.Empty);
    }

    private bool ShouldSuppressOptionalContextMutation()
    {
        var suppress = false;
        ConfigureOptionalContextMutation(ref suppress);
        return suppress;
    }

    partial void ConfigureOptionalContextMutation(ref bool suppress);

    partial void ApplyOptionalContextOverride(ref RibbonContextState state);
}
