using FreeP.Core.Model;
using Free.Shared.Drawing;

namespace FreeP.App.Compositor;

/// <summary>Renderer-neutral projection of PowerPoint's Selection Pane.</summary>
public static class PresentationSelectionPanePlanner
{
    public const string SelectionPaneCommandId = "freep.view.selection-pane";
    public const string EmptyMessage = "Current slide has no selectable objects.";

    public static PresentationSelectionPanePlan Build(
        Slide? slide,
        int slideIndex,
        IReadOnlyList<uint>? selectedShapeIds = null)
    {
        var selected = selectedShapeIds ?? Array.Empty<uint>();
        if (slide is null)
            return new(slideIndex, false, null, []);

        // PowerPoint presents the front-most object first, while the model stores painter order.
        // Keep group children in the same local order directly beneath their group so the pane
        // can address the real editable objects instead of treating a group as an opaque leaf.
        var items = EnumerateShapesFrontToBack(slide.Shapes)
            .Select((entry, index) => new PresentationSelectionPaneItemPlan(
                index,
                entry.Shape.Id,
                string.IsNullOrWhiteSpace(entry.Shape.Name)
                    ? DescribeKind(entry.Shape.Kind, entry.Shape.Id)
                    : entry.Shape.Name,
                entry.Shape.Kind,
                DescribeKind(entry.Shape.Kind, entry.Shape.Id),
                entry.Shape.IsHidden,
                selected.Contains(entry.Shape.Id),
                entry.Depth,
                CanMoveUp: entry.SiblingIndex < entry.SiblingCount - 1,
                CanMoveDown: entry.SiblingIndex > 0))
            .ToArray();

        return new(
            slideIndex,
            true,
            selected.Count == 1 ? selected[0] : null,
            items);
    }

    private static string DescribeKind(SlideShapeKind kind, uint id) =>
        kind switch
        {
            SlideShapeKind.AutoShape => "Shape",
            SlideShapeKind.Picture => "Picture",
            SlideShapeKind.Media => "Media",
            SlideShapeKind.Table => "Table",
            SlideShapeKind.Chart => "Chart",
            SlideShapeKind.SmartArt => "SmartArt",
            SlideShapeKind.Group => "Group",
            SlideShapeKind.Connector => "Connector",
            SlideShapeKind.Ole => "Embedded object",
            _ => $"Object {id}",
        };

    private static IEnumerable<(SlideShape Shape, int Depth, int SiblingIndex, int SiblingCount)> EnumerateShapesFrontToBack(
        IEnumerable<SlideShape> shapes,
        int depth = 0)
    {
        var siblingList = shapes.ToList();
        for (var siblingIndex = siblingList.Count - 1; siblingIndex >= 0; siblingIndex--)
        {
            var shape = siblingList[siblingIndex];
            yield return (shape, depth, siblingIndex, siblingList.Count);
            if (shape.Children.Count > 0)
            {
                foreach (var child in EnumerateShapesFrontToBack(shape.Children, depth + 1))
                    yield return child;
            }
        }
    }
}

public sealed record PresentationSelectionPaneItemPlan(
    int SelectionIndex,
    uint ShapeId,
    string ShapeName,
    SlideShapeKind ShapeType,
    string ShapeTypeLabel,
    bool IsHidden,
    bool IsSelected,
    int NestingDepth = 0,
    bool CanMoveUp = false,
    bool CanMoveDown = false);

public sealed record PresentationSelectionPanePlan(
    int SlideIndex,
    bool HasSlide,
    uint? SelectedShapeId,
    IReadOnlyList<PresentationSelectionPaneItemPlan> Items)
{
    public PresentationSelectionPaneItemPlan? SelectedItem =>
        Items.FirstOrDefault(item => item.IsSelected);
}
