using Free.Shared.Ribbon;
using Free.Shared.Drawing;
using FreeP.Core.Model;

namespace FreeP.App.Compositor;

/// <summary>
/// Owns the renderer-neutral activation keys and selection projection for FreeP contextual ribbon tabs.
/// Display labels remain in the ribbon catalog; hosts consume only these stable context identifiers.
/// </summary>
public static class PresentationContextualRibbonPlanner
{
    public const string TextContextKey = "text";
    public const string TableContextKey = "table";
    public const string SmartArtContextKey = "smartart";

    public static RibbonContextState BuildContext(EditingSession editor)
    {
        ArgumentNullException.ThrowIfNull(editor);

        if (editor.CurrentSlide is not { } slide)
            return RibbonContextState.None;

        var state = RibbonContextState.None;
        foreach (var shapeId in editor.SelectedShapeIds)
        {
            var shape = SlideShapeTraversal.FindById(slide, shapeId);
            if (shape?.Table is not null)
            {
                state = state.With(TableContextKey);
                continue;
            }

            if (shape?.Kind == SlideShapeKind.SmartArt && shape.SmartArt is not null)
            {
                state = state.With(SmartArtContextKey);
                continue;
            }

            if (shape?.TextBody is not null)
                state = state.With(TextContextKey);
        }

        return state;
    }
}
