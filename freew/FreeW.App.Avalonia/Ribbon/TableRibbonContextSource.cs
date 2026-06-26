using Free.Shared.Ribbon;
using FreeW.App.Avalonia.Editing;

namespace FreeW.App.Avalonia.Ribbon;

/// <summary>
/// Adapts <see cref="DocumentView.CaretMoved"/> into the <see cref="IRibbonContextSource"/>
/// contract so the shared ribbon renderer can show or hide the Table contextual tabs (activation
/// key <c>"table"</c>) whenever the caret enters or leaves a table cell.
///
/// <para>
/// Subscribe once (during ribbon construction) and let the renderer drive tab visibility via
/// <see cref="ContextChanged"/>. The source is lightweight: it only fires the event when the
/// in-table state actually changes, not on every caret move.
/// </para>
/// </summary>
internal sealed class TableRibbonContextSource : IRibbonContextSource
{
    /// <summary>Context activation key shared with the contextual tab definitions.</summary>
    internal const string TableContextKey = "table";

    private RibbonContextState _current = RibbonContextState.None;
    private bool _tableActive;

    public RibbonContextState Current => _current;

    public event EventHandler? ContextChanged;

    public TableRibbonContextSource(DocumentView editor)
    {
        ArgumentNullException.ThrowIfNull(editor);
        // Subscribe to both CaretMoved and DocumentChanged — either can transition in/out of a cell.
        // LoadDocument clears the cell caret but fires DocumentChanged (not CaretMoved).
        editor.CaretMoved      += () => Sync(editor.CellCaretInfo is not null);
        editor.DocumentChanged += () => Sync(editor.CellCaretInfo is not null);
        // Initialise immediately — usually not in a table at startup.
        Sync(editor.CellCaretInfo is not null);
    }

    private void Sync(bool inTable)
    {
        if (inTable == _tableActive)
            return;
        _tableActive = inTable;
        _current = inTable
            ? RibbonContextState.None.With(TableContextKey)
            : RibbonContextState.None;
        ContextChanged?.Invoke(this, EventArgs.Empty);
    }
}
