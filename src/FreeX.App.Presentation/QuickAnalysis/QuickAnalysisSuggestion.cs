namespace FreeX.App.Presentation.QuickAnalysis;

/// <summary>
/// A single Quick Analysis suggestion: a stable id, the group it belongs to, a display label, and the
/// concrete action descriptor a host needs to execute it. Exactly one action descriptor is populated,
/// selected by <see cref="ActionKind"/>.
/// </summary>
public sealed record QuickAnalysisSuggestion
{
    private QuickAnalysisSuggestion(
        string id,
        QuickAnalysisGroup group,
        string label,
        QuickAnalysisActionKind actionKind)
    {
        Id = id;
        Group = group;
        Label = label;
        ActionKind = actionKind;
    }

    /// <summary>Stable identifier, unique within a model and stable across selections (e.g. "format.databars").</summary>
    public string Id { get; }

    /// <summary>The group this suggestion belongs to.</summary>
    public QuickAnalysisGroup Group { get; }

    /// <summary>Human-readable label for the suggestion.</summary>
    public string Label { get; }

    /// <summary>Which action descriptor is populated and how a host should execute it.</summary>
    public QuickAnalysisActionKind ActionKind { get; }

    /// <summary>Populated when <see cref="ActionKind"/> is <see cref="QuickAnalysisActionKind.ConditionalFormat"/>.</summary>
    public QuickAnalysisConditionalFormatAction? ConditionalFormat { get; private init; }

    /// <summary>Populated when <see cref="ActionKind"/> is <see cref="QuickAnalysisActionKind.InsertChart"/>.</summary>
    public QuickAnalysisChartAction? Chart { get; private init; }

    /// <summary>Populated when <see cref="ActionKind"/> is <see cref="QuickAnalysisActionKind.InsertTotals"/>.</summary>
    public QuickAnalysisTotalAction? Total { get; private init; }

    /// <summary>Populated when <see cref="ActionKind"/> is <see cref="QuickAnalysisActionKind.Table"/>.</summary>
    public QuickAnalysisTableAction? Table { get; private init; }

    /// <summary>Populated when <see cref="ActionKind"/> is <see cref="QuickAnalysisActionKind.InsertSparklines"/>.</summary>
    public QuickAnalysisSparklineAction? Sparkline { get; private init; }

    /// <summary>Builds the renderer-facing display item for this suggestion from the shared catalog.</summary>
    public QuickAnalysisDisplayItem ToDisplayItem() => QuickAnalysisCatalog.BuildDisplayItem(Id);

    internal static QuickAnalysisSuggestion Formatting(
        string id,
        string label,
        QuickAnalysisConditionalFormatAction action) =>
        new(id, QuickAnalysisGroup.Formatting, label, QuickAnalysisActionKind.ConditionalFormat)
        {
            ConditionalFormat = action
        };

    internal static QuickAnalysisSuggestion ChartSuggestion(
        string id,
        string label,
        QuickAnalysisChartAction action) =>
        new(id, QuickAnalysisGroup.Charts, label, QuickAnalysisActionKind.InsertChart)
        {
            Chart = action
        };

    internal static QuickAnalysisSuggestion TotalSuggestion(
        string id,
        string label,
        QuickAnalysisTotalAction action) =>
        new(id, QuickAnalysisGroup.Totals, label, QuickAnalysisActionKind.InsertTotals)
        {
            Total = action
        };

    internal static QuickAnalysisSuggestion TableSuggestion(
        string id,
        string label,
        QuickAnalysisTableAction action) =>
        new(id, QuickAnalysisGroup.Tables, label, QuickAnalysisActionKind.Table)
        {
            Table = action
        };

    internal static QuickAnalysisSuggestion SparklineSuggestion(
        string id,
        string label,
        QuickAnalysisSparklineAction action) =>
        new(id, QuickAnalysisGroup.Sparklines, label, QuickAnalysisActionKind.InsertSparklines)
        {
            Sparkline = action
        };
}

/// <summary>A named group of suggestions, in display order.</summary>
public sealed record QuickAnalysisSuggestionGroup(
    QuickAnalysisGroup Group,
    IReadOnlyList<QuickAnalysisSuggestion> Suggestions);

/// <summary>
/// The result of analysing a selection: the suggestion groups that apply, in display order. When the
/// selection is degenerate (single cell or empty) the model is empty.
/// </summary>
public sealed record QuickAnalysisModel(IReadOnlyList<QuickAnalysisSuggestionGroup> Groups)
{
    /// <summary>An empty model with no groups, returned for degenerate selections.</summary>
    public static QuickAnalysisModel Empty { get; } = new([]);

    /// <summary>True when no suggestions apply to the selection.</summary>
    public bool IsEmpty => Groups.Count == 0;

    /// <summary>Every suggestion across all groups, in display order.</summary>
    public IEnumerable<QuickAnalysisSuggestion> AllSuggestions()
    {
        foreach (var group in Groups)
        {
            foreach (var suggestion in group.Suggestions)
                yield return suggestion;
        }
    }

    /// <summary>The suggestions in the given group, or an empty list when the group is absent.</summary>
    public IReadOnlyList<QuickAnalysisSuggestion> SuggestionsFor(QuickAnalysisGroup group)
    {
        foreach (var entry in Groups)
        {
            if (entry.Group == group)
                return entry.Suggestions;
        }

        return [];
    }

    /// <summary>True when the model contains the given group.</summary>
    public bool HasGroup(QuickAnalysisGroup group) => SuggestionsFor(group).Count > 0;

    /// <summary>Builds the renderer-facing display model for the suggestions in this model.</summary>
    public QuickAnalysisDisplayModel ToDisplayModel()
    {
        if (IsEmpty)
            return QuickAnalysisDisplayModel.Empty;

        var groups = new List<QuickAnalysisDisplayGroup>(Groups.Count);
        foreach (var group in Groups)
        {
            var items = group.Suggestions.Select(suggestion => suggestion.ToDisplayItem()).ToArray();
            if (items.Length > 0)
                groups.Add(new QuickAnalysisDisplayGroup(group.Group, items));
        }

        return groups.Count == 0 ? QuickAnalysisDisplayModel.Empty : new QuickAnalysisDisplayModel(groups);
    }
}
