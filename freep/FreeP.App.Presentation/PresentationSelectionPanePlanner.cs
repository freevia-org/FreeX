using FreeP.Core.Model;
using Free.Shared.Drawing;

namespace FreeP.App.Compositor;

/// <summary>Renderer-neutral projection of PowerPoint's Selection Pane.</summary>
public static class PresentationSelectionPanePlanner
{
    public const string SelectionPaneCommandId = "freep.view.selection-pane";
    public const string TitleText = "Selection Pane";
    public const string EmptyMessage = "Current slide has no selectable objects.";
    public const string ShowActionText = "Show";
    public const string HideActionText = "Hide";
    public const string MoveTowardFrontText = "\u25B2";
    public const string MoveTowardBackText = "\u25BC";

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

    public static PresentationSelectionPaneTransitionPlan PlanRenameCancellation(
        PresentationSelectionPanePlan panePlan)
    {
        ArgumentNullException.ThrowIfNull(panePlan);
        return new(
            PresentationSelectionPaneActionKind.Rename,
            ActionApplied: false,
            ShouldRefreshPane: true,
            RestoreNameText: null,
            panePlan);
    }

    public static PresentationSelectionPaneCommandPlan PlanCommand(
        PresentationSelectionPaneActionKind action,
        PresentationSelectionPanePlan panePlan,
        uint shapeId,
        string? proposedName = null,
        PresentationSelectionPaneMoveDirection? moveDirection = null)
    {
        ArgumentNullException.ThrowIfNull(panePlan);
        var item = panePlan.Items.FirstOrDefault(candidate => candidate.ShapeId == shapeId);

        return action switch
        {
            PresentationSelectionPaneActionKind.Select => new(
                action,
                shapeId,
                item is not null,
                NormalizedName: null,
                ReadingOrderOffset: 0,
                PreviousName: item?.ShapeName),
            PresentationSelectionPaneActionKind.Rename => PlanRenameCommand(
                action,
                shapeId,
                item,
                proposedName),
            PresentationSelectionPaneActionKind.ToggleVisibility => new(
                action,
                shapeId,
                item is not null,
                NormalizedName: null,
                ReadingOrderOffset: 0,
                PreviousName: item?.ShapeName),
            PresentationSelectionPaneActionKind.MoveInReadingOrder => PlanMoveCommand(
                action,
                shapeId,
                item,
                moveDirection),
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, null),
        };
    }

    private static PresentationSelectionPaneCommandPlan PlanRenameCommand(
        PresentationSelectionPaneActionKind action,
        uint shapeId,
        PresentationSelectionPaneItemPlan? item,
        string? proposedName)
    {
        var normalizedName = proposedName?.Trim() ?? string.Empty;
        return new(
            action,
            shapeId,
            item is not null && normalizedName.Length > 0,
            normalizedName.Length > 0 ? normalizedName : null,
            ReadingOrderOffset: 0,
            PreviousName: item?.ShapeName);
    }

    private static PresentationSelectionPaneCommandPlan PlanMoveCommand(
        PresentationSelectionPaneActionKind action,
        uint shapeId,
        PresentationSelectionPaneItemPlan? item,
        PresentationSelectionPaneMoveDirection? moveDirection)
    {
        var offset = moveDirection switch
        {
            PresentationSelectionPaneMoveDirection.TowardFront => 1,
            PresentationSelectionPaneMoveDirection.TowardBack => -1,
            null => 0,
            _ => throw new ArgumentOutOfRangeException(nameof(moveDirection), moveDirection, null),
        };
        var canExecute = item is not null && (moveDirection switch
        {
            PresentationSelectionPaneMoveDirection.TowardFront => item.CanMoveUp,
            PresentationSelectionPaneMoveDirection.TowardBack => item.CanMoveDown,
            null => false,
            _ => throw new ArgumentOutOfRangeException(nameof(moveDirection), moveDirection, null),
        });

        return new(
            action,
            shapeId,
            canExecute,
            NormalizedName: null,
            offset,
            PreviousName: item?.ShapeName);
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

    public PresentationSelectionPaneItemSession CreateItemSession(uint shapeId) =>
        new(this, shapeId);

    public PresentationSelectionPaneTransitionPlan SelectShape(uint shapeId)
    {
        return Execute(PresentationSelectionPanePlanner.PlanCommand(
            PresentationSelectionPaneActionKind.Select,
            CurrentPlan,
            shapeId));
    }

    public PresentationSelectionPaneTransitionPlan RenameShape(uint shapeId, string? name)
    {
        return Execute(PresentationSelectionPanePlanner.PlanCommand(
            PresentationSelectionPaneActionKind.Rename,
            CurrentPlan,
            shapeId,
            proposedName: name));
    }

    public PresentationSelectionPaneTransitionPlan ToggleShapeVisibility(uint shapeId)
    {
        return Execute(PresentationSelectionPanePlanner.PlanCommand(
            PresentationSelectionPaneActionKind.ToggleVisibility,
            CurrentPlan,
            shapeId));
    }

    public PresentationSelectionPaneTransitionPlan MoveShapeInReadingOrder(
        uint shapeId,
        PresentationSelectionPaneMoveDirection direction)
    {
        return Execute(PresentationSelectionPanePlanner.PlanCommand(
            PresentationSelectionPaneActionKind.MoveInReadingOrder,
            CurrentPlan,
            shapeId,
            moveDirection: direction));
    }

    internal PresentationSelectionPaneTransitionPlan CancelRename()
    {
        return PresentationSelectionPanePlanner.PlanRenameCancellation(Refresh());
    }

    internal PresentationSelectionPaneTransitionPlan NoOp(
        PresentationSelectionPaneActionKind action)
    {
        return PresentationSelectionPanePlanner.PlanTransition(
            action,
            actionApplied: false,
            CurrentPlan);
    }

    private PresentationSelectionPaneTransitionPlan Execute(
        PresentationSelectionPaneCommandPlan command)
    {
        ArgumentNullException.ThrowIfNull(command);
        var applied = command.CanExecute && (command.Action switch
        {
            PresentationSelectionPaneActionKind.Select => Select(command.ShapeId),
            PresentationSelectionPaneActionKind.Rename =>
                _editor.SetShapeName(command.ShapeId, command.NormalizedName),
            PresentationSelectionPaneActionKind.ToggleVisibility =>
                _editor.ToggleShapeHidden(command.ShapeId),
            PresentationSelectionPaneActionKind.MoveInReadingOrder =>
                Move(command.ShapeId, command.ReadingOrderOffset),
            _ => throw new ArgumentOutOfRangeException(nameof(command), command.Action, null),
        });
        var panePlan = Refresh();
        return PresentationSelectionPanePlanner.PlanTransition(
            command.Action,
            applied,
            panePlan,
            command.PreviousName);
    }

    private bool Select(uint shapeId)
    {
        _editor.Select(shapeId);
        return _editor.SelectedShapeIds.Count == 1 && _editor.SelectedShapeIds[0] == shapeId;
    }

    private bool Move(uint shapeId, int offset)
    {
        _editor.Select(shapeId);
        return _editor.MoveSelectedShapeInReadingOrder(offset);
    }

    private PresentationSelectionPanePlan BuildCurrentPlan() =>
        PresentationSelectionPanePlanner.Build(
            _editor.CurrentSlide,
            _editor.CurrentSlideIndex,
            _editor.SelectedShapeIds);
}

/// <summary>Owns per-row interaction state while a native Selection Pane row is alive.</summary>
public sealed class PresentationSelectionPaneItemSession
{
    private readonly PresentationSelectionPaneSession _paneSession;
    private readonly uint _shapeId;
    private bool _renameCompleted;

    internal PresentationSelectionPaneItemSession(
        PresentationSelectionPaneSession paneSession,
        uint shapeId)
    {
        _paneSession = paneSession;
        _shapeId = shapeId;
    }

    public PresentationSelectionPaneTransitionPlan Select() =>
        _paneSession.SelectShape(_shapeId);

    public PresentationSelectionPaneTransitionPlan CommitRename(string? proposedName)
    {
        if (_renameCompleted)
            return _paneSession.NoOp(PresentationSelectionPaneActionKind.Rename);

        _renameCompleted = true;
        return _paneSession.RenameShape(_shapeId, proposedName);
    }

    public PresentationSelectionPaneTransitionPlan CancelRename()
    {
        if (_renameCompleted)
            return _paneSession.NoOp(PresentationSelectionPaneActionKind.Rename);

        _renameCompleted = true;
        return _paneSession.CancelRename();
    }

    public PresentationSelectionPaneTransitionPlan ToggleVisibility() =>
        _paneSession.ToggleShapeVisibility(_shapeId);

    public PresentationSelectionPaneTransitionPlan MoveTowardFront() =>
        Move(PresentationSelectionPaneMoveDirection.TowardFront);

    public PresentationSelectionPaneTransitionPlan MoveTowardBack() =>
        Move(PresentationSelectionPaneMoveDirection.TowardBack);

    private PresentationSelectionPaneTransitionPlan Move(
        PresentationSelectionPaneMoveDirection direction) =>
        _paneSession.MoveShapeInReadingOrder(_shapeId, direction);
}

public enum PresentationSelectionPaneActionKind
{
    Select,
    Rename,
    ToggleVisibility,
    MoveInReadingOrder,
}

public enum PresentationSelectionPaneMoveDirection
{
    TowardFront,
    TowardBack,
}

public sealed record PresentationSelectionPaneCommandPlan(
    PresentationSelectionPaneActionKind Action,
    uint ShapeId,
    bool CanExecute,
    string? NormalizedName,
    int ReadingOrderOffset,
    string? PreviousName);

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
    public string SelectText => $"{SelectionIndex + 1}.";

    public string SelectToolTipText => $"Select {ShapeTypeLabel}";

    public const string RenameToolTipText = "Rename object";

    public string VisibilityActionText => IsHidden
        ? PresentationSelectionPanePlanner.ShowActionText
        : PresentationSelectionPanePlanner.HideActionText;

    public string VisibilityToolTipText => IsHidden ? "Show object" : "Hide object";

    public string MoveUpText => PresentationSelectionPanePlanner.MoveTowardFrontText;

    public const string MoveUpToolTipText = "Move toward front";

    public string MoveDownText => PresentationSelectionPanePlanner.MoveTowardBackText;

    public const string MoveDownToolTipText = "Move toward back";

    public string AccessibilityStateText =>
        PresentationPaneAccessibilityPlanner.FormatSelectionState(IsSelected);
}

public sealed record PresentationSelectionPanePlan(
    int SlideIndex,
    bool HasSlide,
    uint? SelectedShapeId,
    IReadOnlyList<PresentationSelectionPaneItemPlan> Items)
{
    public string TitleText => PresentationSelectionPanePlanner.TitleText;

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
