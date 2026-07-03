using Free.Shared.Ribbon;
using FreeW.App.Avalonia.Editing;

namespace FreeW.App.Avalonia.Ribbon;

/// <summary>
/// Activates the Header & Footer Tools contextual tab while the Avalonia editor caret is inside
/// an editable header or footer region.
/// </summary>
internal sealed class HeaderFooterRibbonContextSource : IRibbonContextSource
{
    internal const string HeaderFooterContextKey = "header-footer";

    private RibbonContextState _current = RibbonContextState.None;
    private bool _active;

    public RibbonContextState Current => _current;

    public event EventHandler? ContextChanged;

    public HeaderFooterRibbonContextSource(DocumentView editor)
    {
        ArgumentNullException.ThrowIfNull(editor);
        editor.CaretMoved += () => Sync(editor.IsHeaderFooterCaretActive);
        editor.DocumentChanged += () => Sync(editor.IsHeaderFooterCaretActive);
        Sync(editor.IsHeaderFooterCaretActive);
    }

    private void Sync(bool active)
    {
        if (active == _active)
            return;

        _active = active;
        _current = active
            ? RibbonContextState.None.With(HeaderFooterContextKey)
            : RibbonContextState.None;
        ContextChanged?.Invoke(this, EventArgs.Empty);
    }
}
