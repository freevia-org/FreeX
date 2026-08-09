namespace FreeW.App.Presentation.DocumentView;

public sealed record DocumentFloatingDragUpdate(
    DocumentFloatRect BaseRect,
    DocumentFloatRect Rect,
    DocumentFloatingHandle Handle,
    bool IsGroupChild,
    bool HasModelChange)
{
    public bool IsMove => Handle == DocumentFloatingHandle.Body;
    public double DeltaXDip => Rect.XDip - BaseRect.XDip;
    public double DeltaYDip => Rect.YDip - BaseRect.YDip;
}

/// <summary>
/// Owns renderer-neutral floating move/resize lifecycle and geometry. Renderers retain pointer capture,
/// selection realization, model command routing, and redraw.
/// </summary>
public sealed class DocumentFloatingDragSession
{
    private DragState? _state;

    public bool IsActive => _state is not null;

    public DocumentFloatRect? BaseRect => _state?.BaseRect;

    public bool Begin(
        DocumentFloatPoint pointerDown,
        DocumentFloatRect baseRect,
        DocumentFloatingHandle handle,
        double rotationAngle = 0,
        bool flipH = false,
        bool flipV = false,
        IReadOnlyList<DocumentFloatTransform>? parentTransforms = null)
    {
        if (handle == DocumentFloatingHandle.None)
            return false;

        _state = new DragState(
            pointerDown,
            baseRect,
            handle,
            rotationAngle,
            flipH,
            flipV,
            parentTransforms);
        return true;
    }

    public DocumentFloatingDragUpdate? Update(
        DocumentFloatPoint pointer,
        bool preserveAspect,
        double minimumSizeDip)
    {
        if (_state is not { } state)
            return null;

        return new DocumentFloatingDragUpdate(
            state.BaseRect,
            BuildRect(state, pointer, preserveAspect, minimumSizeDip),
            state.Handle,
            IsGroupChild: state.ParentTransforms is not null,
            HasModelChange: false);
    }

    public DocumentFloatingDragUpdate? Complete(
        DocumentFloatPoint pointer,
        bool preserveAspect,
        double minimumSizeDip,
        double minimumMoveDip,
        double minimumResizeChangeDip)
    {
        if (_state is not { } state)
            return null;

        var rect = BuildRect(state, pointer, preserveAspect, minimumSizeDip);
        _state = null;
        var changed = state.Handle == DocumentFloatingHandle.Body
            ? Math.Abs(rect.XDip - state.BaseRect.XDip) >= minimumMoveDip
              || Math.Abs(rect.YDip - state.BaseRect.YDip) >= minimumMoveDip
            : Math.Abs(rect.WidthDip - state.BaseRect.WidthDip) >= minimumResizeChangeDip
              || Math.Abs(rect.HeightDip - state.BaseRect.HeightDip) >= minimumResizeChangeDip
              || state.ParentTransforms is not null
              && (Math.Abs(rect.XDip - state.BaseRect.XDip) >= minimumResizeChangeDip
                  || Math.Abs(rect.YDip - state.BaseRect.YDip) >= minimumResizeChangeDip);
        return new DocumentFloatingDragUpdate(
            state.BaseRect,
            rect,
            state.Handle,
            IsGroupChild: state.ParentTransforms is not null,
            HasModelChange: changed);
    }

    public bool Cancel(out DocumentFloatRect baseRect)
    {
        if (_state is not { } state)
        {
            baseRect = null!;
            return false;
        }

        baseRect = state.BaseRect;
        _state = null;
        return true;
    }

    public void Reset() => _state = null;

    private static DocumentFloatRect BuildRect(
        DragState state,
        DocumentFloatPoint pointer,
        bool preserveAspect,
        double minimumSizeDip)
    {
        if (state.ParentTransforms is { } parentTransforms)
        {
            return state.Handle == DocumentFloatingHandle.Body
                ? DocumentViewLayoutPlanner.BuildFloatingGroupChildMoveRectThroughGroupChain(
                    state.BaseRect,
                    state.PointerDown,
                    pointer,
                    parentTransforms)
                : DocumentViewLayoutPlanner.BuildFloatingGroupChildResizeRectThroughGroupChain(
                    state.BaseRect,
                    state.Handle,
                    pointer,
                    preserveAspect,
                    minimumSizeDip,
                    state.RotationAngle,
                    state.FlipH,
                    state.FlipV,
                    parentTransforms);
        }

        return state.Handle == DocumentFloatingHandle.Body
            ? DocumentViewLayoutPlanner.BuildFloatingMoveRect(
                state.BaseRect,
                state.PointerDown,
                pointer)
            : DocumentViewLayoutPlanner.BuildFloatingResizeRect(
                state.BaseRect,
                state.Handle,
                pointer,
                preserveAspect,
                minimumSizeDip,
                state.RotationAngle,
                state.FlipH,
                state.FlipV);
    }

    private sealed record DragState(
        DocumentFloatPoint PointerDown,
        DocumentFloatRect BaseRect,
        DocumentFloatingHandle Handle,
        double RotationAngle,
        bool FlipH,
        bool FlipV,
        IReadOnlyList<DocumentFloatTransform>? ParentTransforms);
}
