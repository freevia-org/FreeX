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
///   <item><c>"Chart"</c> → <see cref="ChartContextKey"/> (Chart Design + Chart Format tabs, green).</item>
///   <item><c>"SmartArt"</c> → <see cref="SmartArtContextKey"/> (SmartArt Design tab, blue).</item>
///   <item>everything else (<c>Shape</c>, <c>WordArt</c>, <c>Group</c>)
///         → <see cref="DrawingContextKey"/> (Drawing Format tab, purple).</item>
/// </list>
/// Exactly one of the keys is active at a time (a single float is selected); all clear on
/// deselect. This mirrors <see cref="TableRibbonContextSource"/> exactly.
/// </para>
/// </summary>
internal sealed class FloatingRibbonContextSource : IRibbonContextSource
{
    /// <summary>Context activation key for the Picture Format tab (selected float is an image).</summary>
    internal const string PictureContextKey = "picture";

    /// <summary>Context activation key for the Drawing Format tab (selected float is a shape/WordArt/group).</summary>
    internal const string DrawingContextKey = "drawing";

    /// <summary>AV-CHARTTAB: Context activation key for the Chart Design + Chart Format tabs (selected float is a chart).</summary>
    internal const string ChartContextKey = "chart";

    /// <summary>AV-CHARTTAB: Context activation key for the SmartArt Design tab (selected float is a SmartArt diagram).</summary>
    internal const string SmartArtContextKey = "smartart";

    private readonly DocumentView _editor;
    private RibbonContextState _current = RibbonContextState.None;
    // Tracks which key (if any) is currently active. Selection changes within the same context still
    // propagate so the renderer can refresh command state for a different shape or grouped child.
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
    /// selected) and raises <see cref="ContextChanged"/> for every selection identity change.
    /// </summary>
    private void Sync()
    {
        var desiredKey = KeyForSelection();
        if (desiredKey != _activeKey)
        {
            _activeKey = desiredKey;
            _current = desiredKey is null
                ? RibbonContextState.None
                : RibbonContextState.None.With(desiredKey);
        }
        ContextChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Returns the activation key for the current selection: <see cref="PictureContextKey"/> for an
    /// image, <see cref="ChartContextKey"/> for a chart, <see cref="SmartArtContextKey"/> for a SmartArt
    /// diagram, <see cref="DrawingContextKey"/> for any other floating kind (shape/WordArt/group), or
    /// <c>null</c> when nothing is selected.
    /// </summary>
    private string? KeyForSelection()
    {
        if (_editor.SelectedDrawingObjectInfo is not { } sel)
            return null;
        return sel.Kind switch
        {
            "Image"    => PictureContextKey,
            "Chart"    => ChartContextKey,
            "SmartArt" => SmartArtContextKey,
            _          => DrawingContextKey,
        };
    }
}
