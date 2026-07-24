namespace FreeP.Core.Model;

/// <summary>Undoable chart camera and surface-wireframe settings edited by the 3-D view dialog.</summary>
public sealed record Chart3DViewOptions(
    int? RotationX,
    int? RotationY,
    int? Perspective,
    int? HeightPercent,
    int? DepthPercent,
    bool? RightAngleAxes,
    bool? Wireframe);
