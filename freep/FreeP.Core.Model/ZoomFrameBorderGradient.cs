namespace FreeP.Core.Model;

/// <summary>
/// The supported two-stop linear gradient used by a native Zoom frame border.
/// Colors are normalized six-digit RGB values and angle is DrawingML's 60000ths
/// of a degree representation.
/// </summary>
public sealed record ZoomFrameBorderGradient(
    string StartColor,
    string EndColor,
    int Angle = 0);
