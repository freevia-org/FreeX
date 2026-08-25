using Free.Shared.Ribbon;
using Free.Shared.Drawing;
using FreeP.Core.Model;

namespace FreeP.App.Compositor;

/// <summary>
/// Maps the presentation selection to the contextual tabs declared by the shared
/// FreeP ribbon definition. Both platform hosts consume this planner so selection
/// precedence cannot drift between WPF and Avalonia.
/// </summary>
public static class PresentationRibbonContextPlanner
{
    public const string TextContextKey = "text";
    public const string TableContextKey = "table";
    public const string SmartArtContextKey = "smartart";

    public static RibbonContextState Build(EditingSession editor)
    {
        ArgumentNullException.ThrowIfNull(editor);

        var state = RibbonContextState.None;
        if (editor.CurrentSlide is not { } slide)
            return state;

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

    public static bool AreEquivalent(RibbonContextState left, RibbonContextState right) =>
        left.IsActive(TextContextKey) == right.IsActive(TextContextKey) &&
        left.IsActive(TableContextKey) == right.IsActive(TableContextKey) &&
        left.IsActive(SmartArtContextKey) == right.IsActive(SmartArtContextKey);
}
