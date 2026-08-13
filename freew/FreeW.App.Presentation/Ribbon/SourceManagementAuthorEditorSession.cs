namespace FreeW.App.Presentation.Ribbon;

public sealed record SourceManagementAuthorEditorPlan(
    SourceManagementAuthorEditorMode Mode,
    IReadOnlyList<SourceManagementAuthorPersonRow> PersonalRows,
    string CorporateAuthor,
    bool PersonalAuthorFieldsEnabled,
    bool CorporateAuthorFieldEnabled);

public sealed class SourceManagementAuthorRowCollection<TNativeRow>
{
    private readonly Func<SourceManagementAuthorPersonRow, TNativeRow> _createRow;
    private readonly Func<TNativeRow, SourceManagementAuthorPersonRow> _readRow;
    private readonly Action<TNativeRow> _addRow;
    private readonly Action _clearRows;
    private readonly List<TNativeRow> _rows = [];

    public SourceManagementAuthorRowCollection(
        Func<SourceManagementAuthorPersonRow, TNativeRow> createRow,
        Func<TNativeRow, SourceManagementAuthorPersonRow> readRow,
        Action<TNativeRow> addRow,
        Action clearRows)
    {
        _createRow = createRow ?? throw new ArgumentNullException(nameof(createRow));
        _readRow = readRow ?? throw new ArgumentNullException(nameof(readRow));
        _addRow = addRow ?? throw new ArgumentNullException(nameof(addRow));
        _clearRows = clearRows ?? throw new ArgumentNullException(nameof(clearRows));
    }

    public IReadOnlyList<SourceManagementAuthorPersonRow> Read() =>
        _rows.Select(_readRow).ToArray();

    public void Render(IReadOnlyList<SourceManagementAuthorPersonRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        _clearRows();
        _rows.Clear();

        foreach (var row in rows)
        {
            var nativeRow = _createRow(row);
            _addRow(nativeRow);
            _rows.Add(nativeRow);
        }
    }
}

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
