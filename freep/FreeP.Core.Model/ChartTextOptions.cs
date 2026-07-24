namespace FreeP.Core.Model;

/// <summary>Undoable chart-wide default text formatting edited by the chart text dialog.</summary>
public sealed record ChartTextOptions(
    string? FontFamily,
    double? FontSizePt,
    bool? Bold,
    bool? Italic,
    ThemeAwareColor? Color);
