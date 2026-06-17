using FreeX.Core.Model;

namespace FreeX.App.Presentation.QuickAnalysis;

/// <summary>The five Quick Analysis groups, matching the tabs the desktop hosts surface.</summary>
public enum QuickAnalysisGroup
{
    Formatting,
    Charts,
    Totals,
    Tables,
    Sparklines
}

/// <summary>
/// The concrete action a Quick Analysis suggestion maps to. A host inspects the action kind, then
/// reads the matching descriptor to execute it. This is a closed, portable contract: no host or
/// renderer types appear here.
/// </summary>
public enum QuickAnalysisActionKind
{
    /// <summary>Apply a conditional-format rule (see <see cref="QuickAnalysisSuggestion.ConditionalFormat"/>).</summary>
    ConditionalFormat,

    /// <summary>Insert a chart (see <see cref="QuickAnalysisSuggestion.Chart"/>).</summary>
    InsertChart,

    /// <summary>Insert total formulas (see <see cref="QuickAnalysisSuggestion.Total"/>).</summary>
    InsertTotals,

    /// <summary>Convert the selection to a table (see <see cref="QuickAnalysisSuggestion.Table"/>).</summary>
    Table,

    /// <summary>Insert sparklines (see <see cref="QuickAnalysisSuggestion.Sparkline"/>).</summary>
    InsertSparklines
}

/// <summary>The conditional-format suggestion kinds the Formatting group offers.</summary>
public enum QuickAnalysisFormatKind
{
    DataBars,
    ColorScale,
    IconSet,
    GreaterThan,
    Top10
}

/// <summary>The aggregate a Totals suggestion inserts.</summary>
public enum QuickAnalysisTotalFunction
{
    Sum,
    Average,
    Count,
    PercentTotal,
    RunningTotal
}

/// <summary>Whether a Totals suggestion inserts a total row beneath the data or a total column beside it.</summary>
public enum QuickAnalysisTotalOrientation
{
    Column,
    Row
}

/// <summary>The table-creation suggestion kinds the Tables group offers.</summary>
public enum QuickAnalysisTableKind
{
    Table,
    PivotTable
}

/// <summary>The sparkline suggestion kinds the Sparklines group offers.</summary>
public enum QuickAnalysisSparklineKind
{
    Line,
    Column,
    WinLoss
}

/// <summary>
/// A Formatting action: apply a conditional-format rule of the given kind. <see cref="RuleType"/>
/// is the underlying Core rule type a host hands to the conditional-format engine.
/// </summary>
public sealed record QuickAnalysisConditionalFormatAction(
    QuickAnalysisFormatKind FormatKind,
    CfRuleType RuleType);

/// <summary>A Charts action: insert a chart of the given Core <see cref="ChartType"/>.</summary>
public sealed record QuickAnalysisChartAction(ChartType ChartType);

/// <summary>
/// A Totals action: insert the given aggregate, oriented as a total row or a total column.
/// </summary>
public sealed record QuickAnalysisTotalAction(
    QuickAnalysisTotalFunction Function,
    QuickAnalysisTotalOrientation Orientation);

/// <summary>A Tables action: convert the selection into a table or a PivotTable.</summary>
public sealed record QuickAnalysisTableAction(QuickAnalysisTableKind TableKind);

/// <summary>A Sparklines action: insert sparklines of the given kind.</summary>
public sealed record QuickAnalysisSparklineAction(QuickAnalysisSparklineKind SparklineKind);
