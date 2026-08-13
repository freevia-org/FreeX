using FreeW.Core.Model;

namespace FreeW.App.Presentation.Ribbon;

public sealed record TableOfAuthoritiesDialogControlState(
    int CategoryIndex,
    bool UsePassim,
    bool KeepOriginalFormatting,
    int TabLeaderIndex);

public sealed record TableOfAuthoritiesCommitPlan(ToaOptions? Options)
{
    public bool ShouldInsert => Options is not null;
}

/// <summary>
/// Owns the option catalogs, editable state, and acceptance projection for both native renderers.
/// </summary>
public sealed class TableOfAuthoritiesDialogSession
{
    public TableOfAuthoritiesDialogSession(ToaOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        Categories = TableOfAuthoritiesDialogPlanner.BuildCategoryChoices();
        TabLeaders = TableOfAuthoritiesDialogPlanner.BuildTabLeaderChoices();
        var initial = TableOfAuthoritiesDialogPlanner.BuildInitialState(options);
        State = new TableOfAuthoritiesDialogControlState(
            TableOfAuthoritiesDialogPlanner.SelectCategoryIndex(Categories, initial.CategoryFilter),
            initial.UsePassim,
            initial.KeepOriginalFormatting,
            TableOfAuthoritiesDialogPlanner.SelectTabLeaderIndex(TabLeaders, initial.TabLeader));
    }

    public IReadOnlyList<TableOfAuthoritiesCategoryChoice> Categories { get; }

    public IReadOnlyList<TableOfAuthoritiesTabLeaderChoice> TabLeaders { get; }

    public TableOfAuthoritiesDialogControlState State { get; private set; }

    public void UpdateCategory(int selectedIndex) =>
        State = State with { CategoryIndex = selectedIndex };

    public void UpdateUsePassim(bool value) =>
        State = State with { UsePassim = value };

    public void UpdateKeepOriginalFormatting(bool value) =>
        State = State with { KeepOriginalFormatting = value };

    public void UpdateTabLeader(int selectedIndex) =>
        State = State with { TabLeaderIndex = selectedIndex };

    public TableOfAuthoritiesDialogAcceptance PlanAcceptance() =>
        TableOfAuthoritiesDialogPlanner.PlanAcceptance(
            new TableOfAuthoritiesDialogInput(
                State.UsePassim,
                State.KeepOriginalFormatting,
                ChoiceAt(Categories, State.CategoryIndex),
                ChoiceAt(TabLeaders, State.TabLeaderIndex)));

    private static TChoice? ChoiceAt<TChoice>(IReadOnlyList<TChoice> choices, int index)
        where TChoice : class =>
        index >= 0 && index < choices.Count ? choices[index] : null;
}
