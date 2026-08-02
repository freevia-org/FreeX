namespace FreeP.Core.Model;

/// <summary>Axis family edited by the shared chart-axis options workflow.</summary>
public enum ChartAxisKind
{
    Category,
    Value,
    SecondaryValue,
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
    double? CrossesAt = null,
    bool ShowAxis = true,
    ChartCrossBetween? CrossBetween = null,
    ChartLabelAlignment? LabelAlignment = null,
    int? LabelOffsetPercent = null,
    bool? NoMultiLevelLabels = null,
    bool? AutoCrossing = null,
    bool ReverseOrder = false,
    bool MinorGridlines = false,
    ChartTextStyle? TitleStyle = null,
    ChartAxisDisplayUnit DisplayUnit = ChartAxisDisplayUnit.None,
    string? RawDisplayUnitToken = null,
    double? CustomDisplayUnit = null,
    string? RawMajorTickMarkToken = null,
    string? RawMinorTickMarkToken = null,
    string? RawTickLabelPositionToken = null,
    string? RawCrossesToken = null,
    string? RawCrossBetweenToken = null,
    string? RawLabelAlignmentToken = null);
