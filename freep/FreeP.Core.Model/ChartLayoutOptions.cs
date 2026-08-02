namespace FreeP.Core.Model;

public enum ChartLayoutTarget { PlotArea, Legend }

/// <summary>Editable manual-layout payload for a chart plot area or legend.</summary>
public sealed record ChartLayoutOptions(
    ChartLayoutTarget Target,
    string? LayoutTarget,
    ChartManualLayoutMode XMode,
    ChartManualLayoutMode YMode,
    ChartManualLayoutMode WidthMode,
    ChartManualLayoutMode HeightMode,
    double? X,
    double? Y,
    double? Width,
    double? Height,
    string? RawXModeToken = null,
    string? RawYModeToken = null,
    string? RawWidthModeToken = null,
    string? RawHeightModeToken = null);
