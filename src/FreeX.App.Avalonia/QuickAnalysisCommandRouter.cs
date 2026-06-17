using FreeX.App.Avalonia.Dialogs;
using FreeX.App.Presentation.QuickAnalysis;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia;

/// <summary>How the shell should execute a chosen Quick Analysis suggestion.</summary>
public enum QuickAnalysisCommandKind
{
    /// <summary>Apply a quick conditional-format preset through the existing preset command path.</summary>
    ConditionalFormatPreset,

    /// <summary>Insert an AutoSum-style aggregate through the existing AutoSum command path.</summary>
    AutoSum,

    /// <summary>Insert sparklines through the existing <c>AddSparklineCommand</c> path.</summary>
    Sparkline,

    /// <summary>Insert a chart over the selection through the <c>AddChartCommand</c> path.</summary>
    InsertChart,

    /// <summary>Convert the selection into a structured table through the <c>CreateStructuredTableCommand</c> path.</summary>
    Table,

    /// <summary>No command path exists in the shell yet; surface the suggestion but no-op with a note.</summary>
    Deferred,
}

/// <summary>
/// Maps a chosen <see cref="QuickAnalysisSuggestion"/> onto the command path the Avalonia shell already
/// has. Pure and UI-free so the mapping is unit testable without a running shell: the shell reads the
/// returned <see cref="QuickAnalysisCommandRoute"/> and dispatches to the matching existing handler.
/// </summary>
public static class QuickAnalysisCommandRouter
{
    /// <summary>
    /// Resolves the shell action for <paramref name="suggestion"/>. Formatting maps to a conditional-format
    /// preset, Totals to an AutoSum function, Sparklines to the sparkline insert; Charts and Tables have no
    /// shell insert path yet and resolve to <see cref="QuickAnalysisCommandKind.Deferred"/>.
    /// </summary>
    public static QuickAnalysisCommandRoute Route(QuickAnalysisSuggestion suggestion)
    {
        ArgumentNullException.ThrowIfNull(suggestion);

        return suggestion.ActionKind switch
        {
            QuickAnalysisActionKind.ConditionalFormat => RouteFormatting(suggestion),
            QuickAnalysisActionKind.InsertTotals => RouteTotals(suggestion),
            QuickAnalysisActionKind.InsertSparklines => RouteSparkline(suggestion),
            QuickAnalysisActionKind.InsertChart => RouteChart(suggestion),
            QuickAnalysisActionKind.Table => RouteTable(suggestion),
            _ => Deferred("This Quick Analysis suggestion is not yet available on macOS."),
        };
    }

    private static QuickAnalysisCommandRoute RouteFormatting(QuickAnalysisSuggestion suggestion)
    {
        var action = suggestion.ConditionalFormat
            ?? throw new ArgumentException("Formatting suggestion has no conditional-format action.", nameof(suggestion));

        var preset = action.FormatKind switch
        {
            QuickAnalysisFormatKind.DataBars => ConditionalFormatPreset.DataBar,
            QuickAnalysisFormatKind.ColorScale => ConditionalFormatPreset.ColorScale,
            QuickAnalysisFormatKind.IconSet => ConditionalFormatPreset.IconSet,
            QuickAnalysisFormatKind.GreaterThan => ConditionalFormatPreset.HighlightGreaterThan,
            QuickAnalysisFormatKind.Top10 => ConditionalFormatPreset.Top10,
            _ => ConditionalFormatPreset.DataBar,
        };

        return QuickAnalysisCommandRoute.ForConditionalFormat(preset);
    }

    private static QuickAnalysisCommandRoute RouteTotals(QuickAnalysisSuggestion suggestion)
    {
        var action = suggestion.Total
            ?? throw new ArgumentException("Totals suggestion has no total action.", nameof(suggestion));

        // Only sum/average/count have an AutoSum analogue; the running/percent variants have no shell path.
        var function = action.Function switch
        {
            QuickAnalysisTotalFunction.Sum => "SUM",
            QuickAnalysisTotalFunction.Average => "AVERAGE",
            QuickAnalysisTotalFunction.Count => "COUNT",
            _ => null,
        };

        return function is null
            ? Deferred("This total is not yet available on macOS.")
            : QuickAnalysisCommandRoute.ForAutoSum(function);
    }

    private static QuickAnalysisCommandRoute RouteSparkline(QuickAnalysisSuggestion suggestion)
    {
        var action = suggestion.Sparkline
            ?? throw new ArgumentException("Sparkline suggestion has no sparkline action.", nameof(suggestion));

        var kind = action.SparklineKind switch
        {
            QuickAnalysisSparklineKind.Line => SparklineKind.Line,
            QuickAnalysisSparklineKind.Column => SparklineKind.Column,
            QuickAnalysisSparklineKind.WinLoss => SparklineKind.WinLoss,
            _ => SparklineKind.Line,
        };

        return QuickAnalysisCommandRoute.ForSparkline(kind);
    }

    private static QuickAnalysisCommandRoute RouteChart(QuickAnalysisSuggestion suggestion)
    {
        var action = suggestion.Chart
            ?? throw new ArgumentException("Chart suggestion has no chart action.", nameof(suggestion));

        return QuickAnalysisCommandRoute.ForInsertChart(action.ChartType);
    }

    private static QuickAnalysisCommandRoute RouteTable(QuickAnalysisSuggestion suggestion)
    {
        var action = suggestion.Table
            ?? throw new ArgumentException("Table suggestion has no table action.", nameof(suggestion));

        // Only the plain table has a shell create path; the PivotTable variant has no shell path yet.
        return action.TableKind == QuickAnalysisTableKind.Table
            ? QuickAnalysisCommandRoute.ForTable()
            : Deferred("Converting to a PivotTable is not yet available on macOS.");
    }

    private static QuickAnalysisCommandRoute Deferred(string note) =>
        new(QuickAnalysisCommandKind.Deferred, DeferredNote: note);
}

/// <summary>
/// The resolved shell action for a suggestion. Exactly one descriptor is populated, selected by
/// <see cref="Kind"/>: a conditional-format preset, an AutoSum function name, a sparkline kind, or a
/// deferred note explaining why no command runs.
/// </summary>
public sealed record QuickAnalysisCommandRoute(
    QuickAnalysisCommandKind Kind,
    ConditionalFormatPreset? Preset = null,
    string? AutoSumFunction = null,
    SparklineKind? SparklineKind = null,
    ChartType? ChartType = null,
    string? DeferredNote = null)
{
    internal static QuickAnalysisCommandRoute ForConditionalFormat(ConditionalFormatPreset preset) =>
        new(QuickAnalysisCommandKind.ConditionalFormatPreset, Preset: preset);

    internal static QuickAnalysisCommandRoute ForAutoSum(string function) =>
        new(QuickAnalysisCommandKind.AutoSum, AutoSumFunction: function);

    internal static QuickAnalysisCommandRoute ForSparkline(SparklineKind kind) =>
        new(QuickAnalysisCommandKind.Sparkline, SparklineKind: kind);

    internal static QuickAnalysisCommandRoute ForInsertChart(ChartType chartType) =>
        new(QuickAnalysisCommandKind.InsertChart, ChartType: chartType);

    internal static QuickAnalysisCommandRoute ForTable() =>
        new(QuickAnalysisCommandKind.Table);
}
