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

public sealed record TableOfAuthoritiesDialogInput(
    bool? UsePassim,
    bool? KeepOriginalFormatting,
    TableOfAuthoritiesCategoryChoice? CategorySelection,
    TableOfAuthoritiesTabLeaderChoice? TabLeaderSelection);

public enum TableOfAuthoritiesDialogField
{
    Category,
    TabLeader
}

public sealed record TableOfAuthoritiesDialogValidation(
    TableOfAuthoritiesDialogField Field,
    string Message);

public sealed record TableOfAuthoritiesDialogAcceptance(
    ToaOptions? Options,
    TableOfAuthoritiesDialogValidation? Validation)
{
    public bool IsAccepted => Options is not null && Validation is null;
}

public static class TableOfAuthoritiesDialogPlanner
{
    public const string Title = "Table of Authorities";
    public const int DialogWidth = 380;
    public const int OuterMargin = 16;
    public const int ButtonWidth = 80;
    public const string CategoryLabel = "Category:";
    public const string UsePassimLabel = "Use passim";
    public const string KeepOriginalFormattingLabel = "Keep original formatting";
    public const string TabLeaderLabel = "Tab leader:";
    public const string AllCategoriesLabel = "(All)";
    public const string MissingCategoryMessage = "Select a category.";
    public const string MissingTabLeaderMessage = "Select a tab leader.";

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

    public static TableOfAuthoritiesDialogSession CreateSession(ToaOptions options) =>
        new(options);

    public static TableOfAuthoritiesCommitPlan PlanCommit(
        ToaOptions? options,
        bool useDefaultsWhenUnavailable = false) =>
        new(options ?? (useDefaultsWhenUnavailable ? ToaOptions.Default : null));

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

    public static TableOfAuthoritiesDialogAcceptance PlanAcceptance(
        TableOfAuthoritiesDialogInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var category = input.CategorySelection;
        if (category is null
            || category.Category is { } selectedCategory && !Enum.IsDefined(selectedCategory))
        {
            return new TableOfAuthoritiesDialogAcceptance(
                Options: null,
                new TableOfAuthoritiesDialogValidation(
                    TableOfAuthoritiesDialogField.Category,
                    MissingCategoryMessage));
        }

        var tabLeader = input.TabLeaderSelection;
        if (tabLeader is null || !Enum.IsDefined(tabLeader.Leader))
        {
            return new TableOfAuthoritiesDialogAcceptance(
                Options: null,
                new TableOfAuthoritiesDialogValidation(
                    TableOfAuthoritiesDialogField.TabLeader,
                    MissingTabLeaderMessage));
        }

        var state = new TableOfAuthoritiesDialogState(
            input.UsePassim is true,
            input.KeepOriginalFormatting is true,
            category.Category,
            tabLeader.Leader);
        return new TableOfAuthoritiesDialogAcceptance(BuildOptions(state), Validation: null);
    }

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
