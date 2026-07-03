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

public static class TableOfAuthoritiesDialogPlanner
{
    public const string Title = "Table of Authorities";
    public const string CategoryLabel = "Category:";
    public const string UsePassimLabel = "Use passim";
    public const string KeepOriginalFormattingLabel = "Keep original formatting";
    public const string TabLeaderLabel = "Tab leader:";
    public const string AllCategoriesLabel = "(All)";

    public static TableOfAuthoritiesDialogState DefaultState { get; } =
        BuildInitialState(ToaOptions.Default);

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
