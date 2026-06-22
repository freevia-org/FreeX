using FreeX.Core.Model;

namespace FreeX.App.Presentation.QuickAnalysis;

/// <summary>How a host should execute a chosen Quick Analysis item.</summary>
public enum QuickAnalysisCommandKind
{
    /// <summary>Apply or open a conditional-format command.</summary>
    ConditionalFormat,

    /// <summary>Clear conditional formats from the selected range.</summary>
    ClearConditionalFormatting,

    /// <summary>Insert a chart over the selected range.</summary>
    InsertChart,

    /// <summary>Open the host's full chart picker.</summary>
    MoreCharts,

    /// <summary>Insert total formulas beside the selected range.</summary>
    InsertTotalFormula,

    /// <summary>Convert the selection into a structured table.</summary>
    Table,

    /// <summary>Create a PivotTable from the selection.</summary>
    PivotTable,

    /// <summary>Insert sparklines beside the selection.</summary>
    Sparkline,

    /// <summary>The host may surface the item but does not have an execution path for it yet.</summary>
    Deferred,
}

/// <summary>The conditional-format intent behind a Quick Analysis formatting command.</summary>
public enum QuickAnalysisConditionalFormatCommand
{
    DataBar,
    ColorScale,
    IconSet,
    GreaterThan,
    LessThan,
    Between,
    EqualTo,
    TextContains,
    DateOccurring,
    DuplicateValues,
    Top10Items,
    Top10Percent,
    Bottom10Items,
    Bottom10Percent,
    AboveAverage,
    BelowAverage,
}

/// <summary>The shared total-formula families Quick Analysis can insert.</summary>
public enum QuickAnalysisTotalFormulaKind
{
    Aggregate,
    PercentTotal,
    RunningTotal,
}

/// <summary>
/// The neutral execution route for a Quick Analysis option or suggestion. Hosts map this route onto their
/// existing command handlers; no renderer-specific types appear in the shared contract.
/// </summary>
public sealed record QuickAnalysisCommandRoute(
    QuickAnalysisCommandKind Kind,
    QuickAnalysisConditionalFormatCommand? ConditionalFormat = null,
    ChartType? ChartType = null,
    QuickAnalysisTotalFormulaKind? TotalFormulaKind = null,
    string? TotalFunction = null,
    SparklineKind? SparklineKind = null,
    string? DeferredNote = null);

/// <summary>Maps shared Quick Analysis models onto the neutral execution route hosts consume.</summary>
public static class QuickAnalysisCommandRouter
{
    public static QuickAnalysisCommandRoute Route(QuickAnalysisSuggestion suggestion)
    {
        ArgumentNullException.ThrowIfNull(suggestion);

        return QuickAnalysisCatalog.Route(suggestion.Id);
    }

    public static QuickAnalysisCommandRoute Route(QuickAnalysisOption option)
    {
        ArgumentNullException.ThrowIfNull(option);

        return QuickAnalysisCatalog.Route(option.Command);
    }

    public static QuickAnalysisCommandRoute Route(QuickAnalysisDisplayItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        return item.Route;
    }
}
