using Free.Shared.Ribbon;
using FreeW.App.Avalonia.Editing;

namespace FreeW.App.Avalonia.Ribbon;

/// <summary>
/// AV-PICTAB: Adapts <see cref="DocumentView.FloatingSelectionChanged"/> into the
/// <see cref="IRibbonContextSource"/> contract so the shared ribbon renderer can show or hide the
/// Picture Format / Drawing Format contextual tabs whenever a floating object is selected or
/// deselected.
///
/// <para>
/// The selected float's <c>Kind</c> picks which context activates:
/// <list type="bullet">
///   <item><c>"Image"</c> → <see cref="PictureContextKey"/> (Picture Format tab, orange).</item>
///   <item>everything else (<c>Shape</c>, <c>Chart</c>, <c>WordArt</c>, <c>SmartArt</c>, <c>Group</c>)
///         → <see cref="DrawingContextKey"/> (Drawing Format tab, purple).</item>
/// </list>
/// Exactly one of the two keys is active at a time (a single float is selected); both clear on
/// deselect. This mirrors <see cref="TableRibbonContextSource"/> exactly.
/// </para>
/// </summary>
internal sealed class FloatingRibbonContextSource : IRibbonContextSource
{
    /// <summary>Context activation key for the Picture Format tab (selected float is an image).</summary>
    internal const string PictureContextKey = "picture";

    /// <summary>Context activation key for the Drawing Format tab (selected float is a shape/chart/etc.).</summary>
    internal const string DrawingContextKey = "drawing";

    private readonly DocumentView _editor;
    private RibbonContextState _current = RibbonContextState.None;
    // Tracks which key (if any) is currently active so we only raise ContextChanged on a real transition.
    private string? _activeKey;

    public RibbonContextState Current => _current;

    public event EventHandler? ContextChanged;

    public FloatingRibbonContextSource(DocumentView editor)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        _editor.FloatingSelectionChanged += Sync;
        // Initialise immediately — usually nothing selected at startup.
        Sync();
    }

    /// <summary>
    /// Maps the current floating selection to the desired activation key (or null when nothing is
    /// selected) and raises <see cref="ContextChanged"/> when the active key actually changes.
    /// </summary>
    private void Sync()
    {
        var desiredKey = KeyForSelection();
        if (desiredKey == _activeKey)
            return;

        _activeKey = desiredKey;
        _current = desiredKey is null
            ? RibbonContextState.None
            : RibbonContextState.None.With(desiredKey);
        ContextChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Returns the activation key for the current selection: <see cref="PictureContextKey"/> for an
    /// image, <see cref="DrawingContextKey"/> for any other floating kind, or <c>null</c> when nothing
    /// is selected.
    /// </summary>
    private string? KeyForSelection()
    {
        if (_editor.SelectedFloatingInfo is not { } sel)
            return null;
        return sel.Kind == "Image" ? PictureContextKey : DrawingContextKey;
    }
}
