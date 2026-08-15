namespace FreeP.App.Compositor;

/// <summary>
/// Owns the renderer-neutral transaction boundary for cell-owned commands issued while a native
/// rich cell editor is active. Pending child text must be committed before fill, geometry, border,
/// anchor, or text-direction mutations can safely read and replace the cell model.
/// </summary>
public static class PresentationTableCellOwnedActionDispatcher
{
    public static bool TryExecute(
        TableCellEditState state,
        uint editingShapeId,
        Action commitPendingCellEdit,
        Func<bool> applyCellMutation)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(commitPendingCellEdit);
        ArgumentNullException.ThrowIfNull(applyCellMutation);

        if (!state.HasActiveCell || state.ShapeId != editingShapeId)
            return false;

        commitPendingCellEdit();
        return applyCellMutation();
    }
}

/// <summary>
/// Owns the renderer-neutral transaction boundary for table structure commands issued while
/// a native rich cell editor is active. The native editor supplies the pending-text commit;
/// structural validation and mutation remain in Presentation.
/// </summary>
public static class PresentationTableStructureActionDispatcher
{
    public static bool IsSupported(PresentationDomainContextActionKind kind) => kind is
        PresentationDomainContextActionKind.DistributeTableRows
        or PresentationDomainContextActionKind.DistributeTableColumns
        or PresentationDomainContextActionKind.InsertTableRowAbove
        or PresentationDomainContextActionKind.InsertTableRowBelow
        or PresentationDomainContextActionKind.InsertTableColumnLeft
        or PresentationDomainContextActionKind.InsertTableColumnRight
        or PresentationDomainContextActionKind.DeleteTableRow
        or PresentationDomainContextActionKind.DeleteTableColumn
        or PresentationDomainContextActionKind.MergeTableCell
        or PresentationDomainContextActionKind.SplitTableCell;

    public static bool CanExecute(
        PresentationDomainContextActionKind kind,
        TableCellEditState state) => kind switch
    {
        PresentationDomainContextActionKind.DistributeTableRows
            or PresentationDomainContextActionKind.DistributeTableColumns => state.HasActiveCell,
        PresentationDomainContextActionKind.InsertTableRowAbove
            or PresentationDomainContextActionKind.InsertTableRowBelow => state.CanInsertRow,
        PresentationDomainContextActionKind.InsertTableColumnLeft
            or PresentationDomainContextActionKind.InsertTableColumnRight => state.CanInsertColumn,
        PresentationDomainContextActionKind.DeleteTableRow => state.CanDeleteRow,
        PresentationDomainContextActionKind.DeleteTableColumn => state.CanDeleteColumn,
        PresentationDomainContextActionKind.MergeTableCell =>
            state.CanMergeWithRight || state.CanMergeWithBelow,
        PresentationDomainContextActionKind.SplitTableCell => state.CanSplitCell,
        _ => false,
    };

    public static bool TryExecute(
        PresentationDomainContextActionKind kind,
        TableCellEditState state,
        uint editingShapeId,
        Action commitPendingCellEdit,
        EditingSession editor)
    {
        ArgumentNullException.ThrowIfNull(commitPendingCellEdit);
        ArgumentNullException.ThrowIfNull(editor);

        if (state.ShapeId != editingShapeId || !CanExecute(kind, state))
            return false;

        // The renderer must release its stale native document before the table structure changes.
        commitPendingCellEdit();

        return kind switch
        {
            PresentationDomainContextActionKind.DistributeTableRows =>
                editor.TryDistributeActiveTableRows(),
            PresentationDomainContextActionKind.DistributeTableColumns =>
                editor.TryDistributeActiveTableColumns(),
            PresentationDomainContextActionKind.InsertTableRowAbove =>
                editor.TryInsertActiveTableRowAbove(),
            PresentationDomainContextActionKind.InsertTableRowBelow =>
                editor.TryInsertActiveTableRowBelow(),
            PresentationDomainContextActionKind.InsertTableColumnLeft =>
                editor.TryInsertActiveTableColumnLeft(),
            PresentationDomainContextActionKind.InsertTableColumnRight =>
                editor.TryInsertActiveTableColumnRight(),
            PresentationDomainContextActionKind.DeleteTableRow =>
                editor.TryDeleteActiveTableRow(),
            PresentationDomainContextActionKind.DeleteTableColumn =>
                editor.TryDeleteActiveTableColumn(),
            PresentationDomainContextActionKind.MergeTableCell =>
                editor.TryMergeActiveTableCell(),
            PresentationDomainContextActionKind.SplitTableCell =>
                editor.TrySplitActiveTableCell(),
            _ => false,
        };
    }
}
