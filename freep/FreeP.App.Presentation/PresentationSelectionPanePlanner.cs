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
        var items = slide.Shapes
            .AsEnumerable()
            .Reverse()
            .Select((shape, index) => new PresentationSelectionPaneItemPlan(
                index,
                shape.Id,
                string.IsNullOrWhiteSpace(shape.Name) ? DescribeKind(shape.Kind, shape.Id) : shape.Name,
                shape.Kind,
                DescribeKind(shape.Kind, shape.Id),
                shape.IsHidden,
                selected.Contains(shape.Id)))
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
}

public sealed record PresentationSelectionPaneItemPlan(
    int SelectionIndex,
    uint ShapeId,
    string ShapeName,
    SlideShapeKind ShapeType,
    string ShapeTypeLabel,
    bool IsHidden,
    bool IsSelected);

public sealed record PresentationSelectionPanePlan(
    int SlideIndex,
    bool HasSlide,
    uint? SelectedShapeId,
    IReadOnlyList<PresentationSelectionPaneItemPlan> Items)
{
    public PresentationSelectionPaneItemPlan? SelectedItem =>
        Items.FirstOrDefault(item => item.IsSelected);
}
