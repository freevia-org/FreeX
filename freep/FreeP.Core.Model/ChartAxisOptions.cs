namespace FreeP.Core.Model;

/// <summary>Axis family edited by the shared chart-axis options workflow.</summary>
public enum ChartAxisKind
{
    Category,
    Value,
}

/// <summary>Working values for a PowerPoint-style chart axis options edit.</summary>
public sealed record ChartAxisOptions(
    ChartAxisKind Axis,
    string? Title,
    double? Minimum,
    double? Maximum,
    double? MajorUnit,
    double? MinorUnit,
    string? NumberFormatCode,
    bool MajorGridlines,
    ChartTickMark? MajorTickMark = null,
    ChartTickMark? MinorTickMark = null,
    ChartTickLabelPosition? TickLabelPosition = null,
    ChartAxisCrossing? Crosses = null,
    double? CrossesAt = null);
