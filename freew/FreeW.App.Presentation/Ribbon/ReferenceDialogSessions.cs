using FreeW.Core.Model;

namespace FreeW.App.Presentation.Ribbon;

public sealed record CrossReferenceDialogState(
    int TypeIndex,
    int InsertAsIndex,
    int TargetIndex,
    bool Hyperlink);

public sealed record CrossReferenceDialogAcceptance(
    CrossReferenceDialogChoice? Result,
    string? ValidationMessage)
{
    public bool IsAccepted => Result is not null;
}

public sealed class CrossReferenceDialogSession
{
    private readonly TextDocument _document;

    public CrossReferenceDialogSession(TextDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        _document = document;
        TypeChoices = CrossReferenceDialogPlanner.BuildTypeChoices();
        State = new CrossReferenceDialogState(0, 0, 0, Hyperlink: true);
        RebuildChoices(previousInsertAs: null);
    }

    public IReadOnlyList<CrossReferenceTypeChoice> TypeChoices { get; }

    public IReadOnlyList<CrossReferenceInsertAsChoice> InsertAsChoices { get; private set; } = [];

    public IReadOnlyList<CrossReferenceTargetChoice> TargetChoices { get; private set; } = [];

    public CrossReferenceDialogState State { get; private set; }

    public CrossReferenceDialogState UpdateType(int index)
    {
        var previousInsertAs = SelectedInsertAs;
        State = State with { TypeIndex = ClampIndex(index, TypeChoices.Count) };
        RebuildChoices(previousInsertAs);
        return State;
    }

    public CrossReferenceDialogState UpdateInsertAs(int index)
    {
        State = State with
        {
            InsertAsIndex = ClampIndex(index, InsertAsChoices.Count),
            TargetIndex = TargetChoices.Count > 0 ? 0 : -1,
        };
        return State;
    }

    public CrossReferenceDialogState UpdateTarget(int index)
    {
        State = State with { TargetIndex = ClampIndex(index, TargetChoices.Count) };
        return State;
    }

    public CrossReferenceDialogState UpdateHyperlink(bool hyperlink)
    {
        State = State with { Hyperlink = hyperlink };
        return State;
    }

    public CrossReferenceDialogAcceptance PlanAcceptance()
    {
        if (State.TargetIndex < 0 || State.TargetIndex >= TargetChoices.Count)
            return new CrossReferenceDialogAcceptance(null, CrossReferenceDialogPlanner.MissingTargetMessage);

        return new CrossReferenceDialogAcceptance(
            CrossReferenceDialogPlanner.CreateChoice(
                SelectedType,
                TargetChoices[State.TargetIndex].Target,
                SelectedInsertAs,
                State.Hyperlink),
            ValidationMessage: null);
    }

    private CrossRefType SelectedType =>
        TypeChoices[ClampIndex(State.TypeIndex, TypeChoices.Count)].Type;

    private CrossRefInsertAs SelectedInsertAs =>
        InsertAsChoices.Count == 0
            ? CrossRefInsertAs.Text
            : InsertAsChoices[ClampIndex(State.InsertAsIndex, InsertAsChoices.Count)].InsertAs;

    private void RebuildChoices(CrossRefInsertAs? previousInsertAs)
    {
        InsertAsChoices = CrossReferenceDialogPlanner.BuildInsertAsChoices(SelectedType);
        TargetChoices = CrossReferenceDialogPlanner.BuildTargetChoices(_document, SelectedType);
        State = State with
        {
            InsertAsIndex = CrossReferenceDialogPlanner.PreserveInsertAsSelection(
                InsertAsChoices,
                previousInsertAs),
            TargetIndex = TargetChoices.Count > 0 ? 0 : -1,
        };
    }

    private static int ClampIndex(int index, int count) =>
        count == 0 ? -1 : Math.Clamp(index, 0, count - 1);
}

public sealed record MarkIndexEntryDialogEnabledState(
    bool BookmarkSelectorEnabled,
    bool CrossReferenceEnabled,
    bool PageNumberFormattingEnabled,
    bool MarkAllEnabled);

public sealed record MarkIndexEntryDialogResult(IndexMark Mark, bool MarkAll);

public sealed record MarkIndexEntryDialogAcceptance(
    MarkIndexEntryDialogResult? Result,
    MarkIndexEntryValidation? Validation)
{
    public bool IsAccepted => Result is not null;
}

public sealed class MarkIndexEntryDialogSession
{
    private readonly string _selectedText;

    public MarkIndexEntryDialogSession(
        string? selectedText,
        IReadOnlyList<string>? bookmarkNames = null)
        : this(MarkIndexEntryDialogPlanner.BuildInitialState(selectedText), bookmarkNames)
    {
    }

    public MarkIndexEntryDialogSession(
        MarkIndexEntryDialogState initialState,
        IReadOnlyList<string>? bookmarkNames = null)
    {
        ArgumentNullException.ThrowIfNull(initialState);
        _selectedText = initialState.MainEntry;
        InitialState = initialState;
        BookmarkNames = (bookmarkNames ?? []).ToArray();
    }

    public MarkIndexEntryDialogState InitialState { get; }

    public IReadOnlyList<string> BookmarkNames { get; }

    public MarkIndexEntryDialogEnabledState PlanEnabledState(IndexEntryReferenceKind referenceKind) =>
        new(
            BookmarkSelectorEnabled: referenceKind == IndexEntryReferenceKind.PageRange,
            CrossReferenceEnabled: referenceKind == IndexEntryReferenceKind.CrossReference,
            PageNumberFormattingEnabled: referenceKind != IndexEntryReferenceKind.CrossReference,
            MarkAllEnabled: MarkIndexEntryDialogPlanner.CanMarkAll(_selectedText, referenceKind));

    public MarkIndexEntryDialogAcceptance PlanAcceptance(
        MarkIndexEntryDialogState state,
        bool markAll)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (markAll && !PlanEnabledState(state.ReferenceKind).MarkAllEnabled)
            return new MarkIndexEntryDialogAcceptance(null, Validation: null);

        if (!MarkIndexEntryDialogPlanner.TryBuildMark(state, out var mark, out var validation))
            return new MarkIndexEntryDialogAcceptance(null, validation);

        return new MarkIndexEntryDialogAcceptance(
            new MarkIndexEntryDialogResult(mark!, markAll),
            Validation: null);
    }
}

public sealed record MarkCitationDialogResult(Citation Citation);

public sealed record MarkCitationDialogAcceptance(
    MarkCitationDialogResult? Result,
    MarkCitationValidation? Validation)
{
    public bool IsAccepted => Result is not null;
}

public sealed class MarkCitationDialogSession
{
    public MarkCitationDialogSession(string? seedLongCitation)
    {
        CategoryChoices = MarkCitationDialogPlanner.BuildCategoryChoices();
        InitialState = MarkCitationDialogPlanner.BuildInitialState(seedLongCitation);
    }

    public IReadOnlyList<MarkCitationCategoryChoice> CategoryChoices { get; }

    public MarkCitationDialogState InitialState { get; }

    public int CategoryIndex(CitationCategory category) =>
        MarkCitationDialogPlanner.SelectCategoryIndex(CategoryChoices, category);

    public MarkCitationDialogAcceptance PlanAcceptance(MarkCitationDialogState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (!MarkCitationDialogPlanner.TryBuildCitation(state, out var citation, out var validation))
            return new MarkCitationDialogAcceptance(null, validation);

        return new MarkCitationDialogAcceptance(
            new MarkCitationDialogResult(citation!),
            Validation: null);
    }
}
