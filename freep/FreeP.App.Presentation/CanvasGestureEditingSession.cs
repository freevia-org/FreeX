using FreeP.Core.Model;

namespace FreeP.App.Compositor;

/// <summary>
/// The narrow editing contract consumed by the renderer-neutral canvas gesture router.
/// Keeping this separate from <see cref="EditingSession"/> lets Slide Master view use the same
/// selection, move, resize, rotate, marquee, and keyboard behavior without pretending its
/// placeholders are normal slide shapes.
/// </summary>
public interface ICanvasGestureEditingSession
{
    Presentation Presentation { get; }
    Slide? CurrentSlide { get; }
    IReadOnlyList<uint> SelectedShapeIds { get; }
    bool IsFormatPainterActive { get; }

    event EventHandler? SelectionChanged;
    event EventHandler? CurrentSlideChanged;
    event Action? Changed;

    void Select(uint shapeId, bool addToSelection = false);
    void ClearSelection();
    void SelectSlide(int index);
    void MoveSelected(long dxEmu, long dyEmu);
    void ResizeShape(uint shapeId, long newOffsetX, long newOffsetY, long newCx, long newCy);
    void RotateShape(uint shapeId, double newRotationDeg);
    bool ApplySelectedTransforms(IEnumerable<CanvasShapeTransform> transforms);
    void DeleteSelected();

    bool BeginFormatPainter();
    void CancelFormatPainter();
    bool TryApplyFormatPainterToShape(uint targetShapeId);
    bool SetPictureCrop(uint shapeId, PictureCropValues values);
    void SetShapeGeometryAdjustment(uint shapeId, string name, double? value);
    void SetCustomGeometryPoint(uint shapeId, int pathIndex, int segmentIndex, double x, double y,
        CustomGeometryPointSlot slot = CustomGeometryPointSlot.Endpoint);
    void SetCustomGeometryArcPoint(uint shapeId, int pathIndex, int segmentIndex, double value,
        CustomGeometryArcPointSlot slot);
    bool TryInsertCustomGeometryPoint(uint shapeId, string handleName);
    bool TryDeleteCustomGeometryPoint(uint shapeId, string handleName);
}
