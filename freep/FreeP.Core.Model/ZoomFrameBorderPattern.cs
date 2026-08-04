namespace FreeP.Core.Model;

/// <summary>
/// A bounded native DrawingML pattern used by a Zoom frame border.
/// Colors are explicit RGB values; theme/resource-derived pattern colors remain
/// source-authoritative when they cannot be represented by this projection.
/// </summary>
public sealed record ZoomFrameBorderPattern(
    string Preset,
    string ForegroundColor,
    string BackgroundColor);
