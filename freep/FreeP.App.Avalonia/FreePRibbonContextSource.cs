using Free.Shared.Ribbon;
using Free.Shared.Drawing;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Avalonia;

/// <summary>
/// Maps the current FreeP shape selection to the activation keys declared by the
/// contextual presentation ribbon tabs.
/// </summary>
internal sealed class FreePRibbonContextSource : IRibbonContextSource
{
    private bool _textActive;
    private bool _tableActive;
    private bool _smartArtActive;

    public RibbonContextState Current { get; private set; } = RibbonContextState.None;

    public event EventHandler? ContextChanged;

    internal void Refresh(EditingSession editor)
    {
        ArgumentNullException.ThrowIfNull(editor);

        var textActive = false;
        var tableActive = false;
        var smartArtActive = false;
        if (editor.CurrentSlide is { } slide)
        {
            foreach (var shapeId in editor.SelectedShapeIds)
            {
                var shape = SlideShapeTraversal.FindById(slide, shapeId);
                if (shape?.Table is not null)
                {
                    tableActive = true;
                    continue;
                }

                if (shape?.Kind == SlideShapeKind.SmartArt && shape.SmartArt is not null)
                {
                    smartArtActive = true;
                    continue;
                }

                textActive |= shape?.TextBody is not null;
            }
        }

        if (_textActive == textActive && _tableActive == tableActive && _smartArtActive == smartArtActive)
            return;

        _textActive = textActive;
        _tableActive = tableActive;
        _smartArtActive = smartArtActive;

        var state = RibbonContextState.None;
        if (_textActive)
            state = state.With("text");
        if (_tableActive)
            state = state.With("table");
        if (_smartArtActive)
            state = state.With("smartart");
        Current = state;
        ContextChanged?.Invoke(this, EventArgs.Empty);
    }
}
