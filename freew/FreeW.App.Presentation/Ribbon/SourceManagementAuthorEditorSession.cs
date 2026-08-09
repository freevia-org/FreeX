namespace FreeW.App.Presentation.Ribbon;

public sealed record SourceManagementAuthorEditorPlan(
    SourceManagementAuthorEditorMode Mode,
    IReadOnlyList<SourceManagementAuthorPersonRow> PersonalRows,
    string CorporateAuthor,
    bool PersonalAuthorFieldsEnabled,
    bool CorporateAuthorFieldEnabled);

public sealed class SourceManagementAuthorEditorSession
{
    private static readonly SourceManagementAuthorPersonRow EmptyPersonRow = new(
        string.Empty,
        string.Empty,
        string.Empty);

    private SourceManagementAuthorEditorState _state;

    public SourceManagementAuthorEditorSession(SourceManagementSourceEntry entry)
        : this(SourceManagementDialogPlanner.ProjectPrimaryAuthorEditorState(entry))
    {
    }

    public SourceManagementAuthorEditorSession(SourceManagementAuthorEditorState initialState)
    {
        ArgumentNullException.ThrowIfNull(initialState);
        _state = Snapshot(initialState);
    }

    public SourceManagementAuthorEditorPlan CurrentPlan => BuildPlan();

    public SourceManagementAuthorEditorPlan SelectMode(
        SourceManagementAuthorEditorMode mode,
        IReadOnlyList<SourceManagementAuthorPersonRow> personalRows,
        string? corporateAuthor)
    {
        CaptureInputs(personalRows, corporateAuthor);
        _state = _state with { Mode = mode };
        return BuildPlan();
    }

    public SourceManagementAuthorEditorPlan AddPersonalAuthorRow(
        IReadOnlyList<SourceManagementAuthorPersonRow> personalRows,
        string? corporateAuthor)
    {
        CaptureInputs(personalRows, corporateAuthor);
        _state = _state with { PersonalRows = [.. _state.PersonalRows, EmptyPersonRow] };
        return BuildPlan();
    }

    public SourceManagementAuthorEditorPlan RemoveFinalPersonalAuthorRow(
        IReadOnlyList<SourceManagementAuthorPersonRow> personalRows,
        string? corporateAuthor)
    {
        CaptureInputs(personalRows, corporateAuthor);
        _state = _state with
        {
            PersonalRows = _state.PersonalRows.Count <= 1
                ? [EmptyPersonRow]
                : _state.PersonalRows.Take(_state.PersonalRows.Count - 1).ToArray()
        };
        return BuildPlan();
    }

    public SourceManagementAuthorEditorState Accept(
        IReadOnlyList<SourceManagementAuthorPersonRow> personalRows,
        string? corporateAuthor)
    {
        CaptureInputs(personalRows, corporateAuthor);
        _state = SourceManagementDialogPlanner.NormalizePrimaryAuthorEditorState(_state);
        return _state;
    }

    private void CaptureInputs(
        IReadOnlyList<SourceManagementAuthorPersonRow> personalRows,
        string? corporateAuthor)
    {
        ArgumentNullException.ThrowIfNull(personalRows);
        _state = _state with
        {
            PersonalRows = [.. personalRows],
            CorporateAuthor = corporateAuthor ?? string.Empty
        };
    }

    private SourceManagementAuthorEditorPlan BuildPlan()
    {
        var personal = _state.Mode == SourceManagementAuthorEditorMode.Personal;
        IReadOnlyList<SourceManagementAuthorPersonRow> rows = _state.PersonalRows.Count == 0
            ? [EmptyPersonRow]
            : [.. _state.PersonalRows];

        return new SourceManagementAuthorEditorPlan(
            _state.Mode,
            rows,
            _state.CorporateAuthor,
            PersonalAuthorFieldsEnabled: personal,
            CorporateAuthorFieldEnabled: !personal);
    }

    private static SourceManagementAuthorEditorState Snapshot(
        SourceManagementAuthorEditorState state) =>
        state with
        {
            PersonalRows = [.. state.PersonalRows],
            CorporateAuthor = state.CorporateAuthor ?? string.Empty
        };
}
