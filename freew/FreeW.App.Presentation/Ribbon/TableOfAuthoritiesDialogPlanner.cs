using FreeW.Core.Model;

namespace FreeW.App.Presentation.Ribbon;

public sealed record TableOfAuthoritiesCategoryChoice(CitationCategory? Category, string Label)
{
    public override string ToString() => Label;
}

public sealed record TableOfAuthoritiesTabLeaderChoice(ToaTabLeader Leader, string Label)
{
    public override string ToString() => Label;
}

public sealed record TableOfAuthoritiesDialogState(
    bool UsePassim,
    bool KeepOriginalFormatting,
    CitationCategory? CategoryFilter,
    ToaTabLeader TabLeader);

public readonly record struct TableOfAuthoritiesDialogVisualMetrics(
    double DialogWidth,
    double OuterInset,
    double LabelBottomMargin,
    double ComboBoxHeight,
    double ComboBottomMargin,
    double PassimBottomMargin,
    double KeepFormattingBottomMargin,
    double ActionTopMargin,
    double ActionButtonWidth,
    double ActionSpacing,
    double AvaloniaComboBoxHeightCompensation,
    double AvaloniaOuterRightCompensation,
    double AvaloniaActionTopCompensation);

public static class TableOfAuthoritiesDialogPlanner
{
    public const string Title = "Table of Authorities";
    public const string CategoryLabel = "Category:";
    public const string UsePassimLabel = "Use passim";
    public const string KeepOriginalFormattingLabel = "Keep original formatting";
    public const string TabLeaderLabel = "Tab leader:";
    public const string AllCategoriesLabel = "(All)";

    /// <summary>
    /// WPF-authority geometry at the dialog harness's 96-DPI logical coordinate space. The
    /// Avalonia compensation values describe measured template paint offsets, not alternate
    /// product layout: its combo template paints the same compact field two pixels shorter, its
    /// content needs one additional right inset, and its action row needs a one-pixel downward
    /// offset to occupy the same painted bounds as WPF.
    /// </summary>
    public static TableOfAuthoritiesDialogVisualMetrics VisualMetrics { get; } = new(
        DialogWidth: 380,
        OuterInset: 16,
        LabelBottomMargin: 4,
        ComboBoxHeight: 24,
        ComboBottomMargin: 8,
        PassimBottomMargin: 6,
        KeepFormattingBottomMargin: 8,
        ActionTopMargin: 12,
        ActionButtonWidth: 80,
        ActionSpacing: 14,
        AvaloniaComboBoxHeightCompensation: -2,
        AvaloniaOuterRightCompensation: 1,
        AvaloniaActionTopCompensation: 1);

    public static TableOfAuthoritiesDialogState DefaultState { get; } =
        BuildInitialState(ToaOptions.Default);

    /// <summary>
    /// Deterministic non-default state used by the paired dialog evidence harness. Keeping this
    /// state in the shared planner prevents WPF and Avalonia from rendering different populated
    /// examples while exercising the same dialog contract.
    /// </summary>
    public static TableOfAuthoritiesDialogState RepresentativePopulatedState { get; } =
        new(
            UsePassim: true,
            KeepOriginalFormatting: true,
            CategoryFilter: CitationCategory.Statutes,
            TabLeader: ToaTabLeader.Dashes);

    /// <summary>
    /// Returns the shared options seed for a dialog evidence state. The validation-error route has
    /// no invalid input in this dialog (only categorical choices and checkboxes), so it intentionally
    /// retains the default state rather than inventing an error that the product cannot produce.
    /// </summary>
    public static ToaOptions BuildEvidenceOptions(string state)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(state);
        return BuildOptions(state.Equals("populated", StringComparison.OrdinalIgnoreCase)
            ? RepresentativePopulatedState
            : DefaultState);
    }

    public static IReadOnlyList<TableOfAuthoritiesCategoryChoice> BuildCategoryChoices()
    {
        var choices = new List<TableOfAuthoritiesCategoryChoice>
        {
            new(null, AllCategoriesLabel)
        };

        choices.AddRange(Enum.GetValues<CitationCategory>()
            .Select(category => new TableOfAuthoritiesCategoryChoice(
                category,
                TableOfAuthorities.CategoryHeading(category))));

        return choices;
    }

    public static IReadOnlyList<TableOfAuthoritiesTabLeaderChoice> BuildTabLeaderChoices() =>
    [
        new(ToaTabLeader.Dots, "Dots ......"),
        new(ToaTabLeader.Dashes, "Dashes ------"),
        new(ToaTabLeader.Underline, "Underline ______"),
        new(ToaTabLeader.None, "(None)")
    ];

    public static TableOfAuthoritiesDialogState BuildInitialState(ToaOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new TableOfAuthoritiesDialogState(
            options.UsePassim,
            options.KeepOriginalFormatting,
            options.CategoryFilter,
            options.TabLeader);
    }

    public static ToaOptions BuildOptions(TableOfAuthoritiesDialogState state) =>
        new()
        {
            UsePassim = state.UsePassim,
            KeepOriginalFormatting = state.KeepOriginalFormatting,
            CategoryFilter = state.CategoryFilter,
            TabLeader = state.TabLeader
        };

    public static int SelectCategoryIndex(
        IReadOnlyList<TableOfAuthoritiesCategoryChoice> choices,
        CitationCategory? category)
    {
        ArgumentNullException.ThrowIfNull(choices);

        for (var i = 0; i < choices.Count; i++)
            if (choices[i].Category == category)
                return i;

        return 0;
    }

    public static int SelectTabLeaderIndex(
        IReadOnlyList<TableOfAuthoritiesTabLeaderChoice> choices,
        ToaTabLeader leader)
    {
        ArgumentNullException.ThrowIfNull(choices);

        for (var i = 0; i < choices.Count; i++)
            if (choices[i].Leader == leader)
                return i;

        return 0;
    }
}
