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

    public static PresentationSelectionPaneTransitionPlan PlanTransition(
        PresentationSelectionPaneActionKind action,
        bool actionApplied,
        PresentationSelectionPanePlan panePlan,
        string? previousName = null)
    {
        ArgumentNullException.ThrowIfNull(panePlan);

        return new(
            action,
            actionApplied,
            ShouldRefreshPane: actionApplied && action is
                PresentationSelectionPaneActionKind.ToggleVisibility or
                PresentationSelectionPaneActionKind.MoveInReadingOrder,
            RestoreNameText: action == PresentationSelectionPaneActionKind.Rename && !actionApplied
                ? previousName
                : null,
            panePlan);
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

/// <summary>Renderer-neutral state and command orchestration for the Selection Pane.</summary>
public sealed class PresentationSelectionPaneSession
{
    private EditingSession _editor;

    public PresentationSelectionPaneSession(EditingSession editor)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        CurrentPlan = BuildCurrentPlan();
    }

    public PresentationSelectionPanePlan CurrentPlan { get; private set; }

    public PresentationSelectionPanePlan SetEditor(EditingSession editor)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        return Refresh();
    }

    public PresentationSelectionPanePlan Refresh()
    {
        CurrentPlan = BuildCurrentPlan();
        return CurrentPlan;
    }

    public PresentationSelectionPaneTransitionPlan SelectShape(uint shapeId)
    {
        _editor.Select(shapeId);
        var panePlan = Refresh();
        return PresentationSelectionPanePlanner.PlanTransition(
            PresentationSelectionPaneActionKind.Select,
            panePlan.Items.Any(item => item.ShapeId == shapeId && item.IsSelected),
            panePlan);
    }

    public PresentationSelectionPaneTransitionPlan RenameShape(uint shapeId, string? name)
    {
        var previousName = CurrentPlan.Items
            .FirstOrDefault(item => item.ShapeId == shapeId)
            ?.ShapeName;
        var applied = _editor.SetShapeName(shapeId, name);
        var panePlan = Refresh();
        return PresentationSelectionPanePlanner.PlanTransition(
            PresentationSelectionPaneActionKind.Rename,
            applied,
            panePlan,
            previousName);
    }

    public PresentationSelectionPaneTransitionPlan ToggleShapeVisibility(uint shapeId)
    {
        var applied = _editor.ToggleShapeHidden(shapeId);
        var panePlan = Refresh();
        return PresentationSelectionPanePlanner.PlanTransition(
            PresentationSelectionPaneActionKind.ToggleVisibility,
            applied,
            panePlan);
    }

    public PresentationSelectionPaneTransitionPlan MoveShapeInReadingOrder(uint shapeId, int offset)
    {
        _editor.Select(shapeId);
        var applied = _editor.MoveSelectedShapeInReadingOrder(offset);
        var panePlan = Refresh();
        return PresentationSelectionPanePlanner.PlanTransition(
            PresentationSelectionPaneActionKind.MoveInReadingOrder,
            applied,
            panePlan);
    }

    private PresentationSelectionPanePlan BuildCurrentPlan() =>
        PresentationSelectionPanePlanner.Build(
            _editor.CurrentSlide,
            _editor.CurrentSlideIndex,
            _editor.SelectedShapeIds);
}

public enum PresentationSelectionPaneActionKind
{
    Select,
    Rename,
    ToggleVisibility,
    MoveInReadingOrder,
}

public sealed record PresentationSelectionPaneTransitionPlan(
    PresentationSelectionPaneActionKind Action,
    bool ActionApplied,
    bool ShouldRefreshPane,
    string? RestoreNameText,
    PresentationSelectionPanePlan PanePlan);

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
    bool CanMoveDown = false)
{
    public string SelectToolTipText => $"Select {ShapeTypeLabel}";

    public const string RenameToolTipText = "Rename object";

    public string VisibilityToolTipText => IsHidden ? "Show object" : "Hide object";

    public const string MoveUpToolTipText = "Move toward front";

    public const string MoveDownToolTipText = "Move toward back";
}

public sealed record PresentationSelectionPanePlan(
    int SlideIndex,
    bool HasSlide,
    uint? SelectedShapeId,
    IReadOnlyList<PresentationSelectionPaneItemPlan> Items)
{
    public PresentationSelectionPaneItemPlan? SelectedItem =>
        Items.FirstOrDefault(item => item.IsSelected);

    public int SelectedItemIndex
    {
        get
        {
            for (var index = 0; index < Items.Count; index++)
            {
                if (Items[index].IsSelected)
                    return index;
            }

            return -1;
        }
    }

    public string StatusText => HasSlide
        ? $"Slide {SlideIndex + 1} ({Items.Count} objects)"
        : PresentationSelectionPanePlanner.EmptyMessage;
}
