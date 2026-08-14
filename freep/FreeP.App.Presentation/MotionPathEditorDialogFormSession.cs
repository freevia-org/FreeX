namespace FreeP.App.Compositor;

/// <summary>
/// Owns native Motion Path row lifetime and transition dispatch while leaving row controls,
/// validation presentation, and modal close behavior with the renderer.
/// </summary>
public sealed class MotionPathEditorDialogFormSession<TRow>
    where TRow : class
{
    private readonly MotionPathEditorDialogSession _session;
    private readonly Func<MotionPathSegmentEdit, TRow> _createRow;
    private readonly Func<TRow, MotionPathEditorRowInput> _readRow;
    private readonly Action<TRow, int, Action> _renderRow;
    private readonly Action _clearRenderedRows;
    private readonly Action<TRow> _addRenderedRow;
    private readonly Action<string, bool> _showValidation;
    private readonly Action _close;
    private readonly List<TRow> _rows = [];

    public MotionPathEditorDialogFormSession(
        MotionPathEditorDialogSession session,
        Func<MotionPathSegmentEdit, TRow> createRow,
        Func<TRow, MotionPathEditorRowInput> readRow,
        Action<TRow, int, Action> renderRow,
        Action clearRenderedRows,
        Action<TRow> addRenderedRow,
        Action<string, bool> showValidation,
        Action close)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _createRow = createRow ?? throw new ArgumentNullException(nameof(createRow));
        _readRow = readRow ?? throw new ArgumentNullException(nameof(readRow));
        _renderRow = renderRow ?? throw new ArgumentNullException(nameof(renderRow));
        _clearRenderedRows = clearRenderedRows ?? throw new ArgumentNullException(nameof(clearRenderedRows));
        _addRenderedRow = addRenderedRow ?? throw new ArgumentNullException(nameof(addRenderedRow));
        _showValidation = showValidation ?? throw new ArgumentNullException(nameof(showValidation));
        _close = close ?? throw new ArgumentNullException(nameof(close));
        ReplaceRows(session.InitialSegments);
    }

    public int RowCount => _rows.Count;

    public void RenderInitial() => RenderRows();

    public void AddLine() => ApplyTransition(_session.AddLine(ReadRowInputs()));

    public void AddCurve() => ApplyTransition(_session.AddCurve(ReadRowInputs()));

    public void Submit() => ApplyTransition(_session.Submit(ReadRowInputs()));

    private IReadOnlyList<MotionPathEditorRowInput> ReadRowInputs() =>
        _rows.Select(_readRow).ToArray();

    private void Remove(int rowIndex) =>
        ApplyTransition(_session.Remove(ReadRowInputs(), rowIndex));

    private void ApplyTransition(MotionPathEditorDialogTransition transition)
    {
        ArgumentNullException.ThrowIfNull(transition);
        if (transition.ShouldRenderRows)
        {
            ReplaceRows(transition.Segments);
            RenderRows();
        }

        _showValidation(transition.ValidationMessage, transition.Succeeded);
        if (transition.ShouldClose)
            _close();
    }

    private void ReplaceRows(IEnumerable<MotionPathSegmentEdit> segments)
    {
        _rows.Clear();
        foreach (var segment in segments)
            _rows.Add(_createRow(segment));
    }

    private void RenderRows()
    {
        _clearRenderedRows();
        for (var index = 0; index < _rows.Count; index++)
        {
            var row = _rows[index];
            var rowIndex = index;
            _renderRow(row, rowIndex, () => Remove(rowIndex));
            _addRenderedRow(row);
        }
    }
}
