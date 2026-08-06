using FreeW.Core.Model;
using FreeW.App.Presentation.Dialogs;
using FreeW.App.Presentation.Ribbon;

namespace FreeW.App.Presentation.Editing;

public readonly record struct DocumentTableCellAddress(
    int BlockIndex,
    int RowIndex,
    int GridColumn);

public readonly record struct DocumentTableCellBorderEdit(
    DocumentTableCellAddress Address,
    CellBorderEdges Edges);

public readonly record struct DocumentTableTextEditResult(
    bool Applied,
    DocumentTableCellAddress Caret,
    int ParagraphIndex,
    int TextOffset);

public readonly record struct DocumentTableEditResult(
    bool Applied,
    DocumentTableCellAddress Caret,
    bool InvalidatesNativeSelection)
{
    internal static DocumentTableEditResult NoChange(DocumentTableCellAddress address) =>
        new(false, address, false);

    internal static DocumentTableEditResult Changed(
        DocumentTableCellAddress address,
        bool invalidatesNativeSelection = false) =>
        new(true, address, invalidatesNativeSelection);
}

public enum DocumentTableDeleteMode
{
    RemoveBlock,
    ReplaceWithEmptyParagraph,
}

/// <summary>
/// Owns portable table command construction, model-coordinate normalization, undo grouping, and
/// post-edit caret outcomes. Renderers retain native table/cell discovery and visual projection.
/// </summary>
public sealed class DocumentTableEditingCoordinator
{
    private readonly DocumentEditingSession _session;

    internal DocumentTableEditingCoordinator(DocumentEditingSession session) => _session = session;

    public DocumentTableCellAddress? AddressFromCellIndex(
        int blockIndex,
        int rowIndex,
        int cellIndex)
    {
        if (!TryGetTable(blockIndex, out var table)
            || rowIndex < 0
            || rowIndex >= table.Rows.Count
            || cellIndex < 0
            || cellIndex >= table.Rows[rowIndex].Cells.Count)
        {
            return null;
        }

        var gridColumn = 0;
        for (var index = 0; index < cellIndex; index++)
            gridColumn += Math.Max(1, table.Rows[rowIndex].Cells[index].GridSpan);
        return new DocumentTableCellAddress(blockIndex, rowIndex, gridColumn);
    }

    public DocumentTableCellAddress? AddressFromGridColumn(
        int blockIndex,
        int rowIndex,
        int gridColumn) =>
        TryResolveCell(
            new DocumentTableCellAddress(blockIndex, rowIndex, gridColumn),
            out _,
            out _)
            ? new DocumentTableCellAddress(blockIndex, rowIndex, gridColumn)
            : null;

    public DocumentTableEditResult InsertRow(DocumentTableCellAddress address, bool after)
    {
        if (!TryResolveCell(address, out var table, out _))
            return DocumentTableEditResult.NoChange(address);

        var insertAt = Math.Clamp(address.RowIndex + (after ? 1 : 0), 0, table.Rows.Count);
        _session.Commands.Execute(new InsertTableRowCommand(address.BlockIndex, insertAt));
        var caretRow = Math.Clamp(insertAt, 0, table.Rows.Count - 1);
        return DocumentTableEditResult.Changed(
            address with { RowIndex = caretRow },
            invalidatesNativeSelection: true);
    }

    public DocumentTableEditResult DeleteRow(DocumentTableCellAddress address)
    {
        if (!TryResolveCell(address, out var table, out _) || table.Rows.Count <= 1)
            return DocumentTableEditResult.NoChange(address);

        _session.Commands.Execute(new DeleteTableRowCommand(address.BlockIndex, address.RowIndex));
        return DocumentTableEditResult.Changed(
            address with { RowIndex = Math.Min(address.RowIndex, table.Rows.Count - 1) },
            invalidatesNativeSelection: true);
    }

    public DocumentTableEditResult InsertColumn(DocumentTableCellAddress address, bool after)
    {
        if (!TryResolveCell(address, out _, out var cellIndex))
            return DocumentTableEditResult.NoChange(address);

        var insertAt = address.GridColumn;
        if (after && TryGetCell(address, cellIndex, out var cell))
            insertAt += Math.Max(1, cell.GridSpan);
        _session.Commands.Execute(new InsertTableColumnCommand(address.BlockIndex, insertAt));
        return DocumentTableEditResult.Changed(
            address with { GridColumn = insertAt },
            invalidatesNativeSelection: true);
    }

    public DocumentTableEditResult DeleteColumn(DocumentTableCellAddress address)
    {
        if (!TryResolveCell(address, out var table, out _)
            || GridWidth(table) <= 1)
        {
            return DocumentTableEditResult.NoChange(address);
        }

        _session.Commands.Execute(new DeleteTableColumnCommand(address.BlockIndex, address.GridColumn));
        return DocumentTableEditResult.Changed(
            address with { GridColumn = Math.Min(address.GridColumn, GridWidth(table) - 1) },
            invalidatesNativeSelection: true);
    }

    public DocumentTableEditResult DeleteTable(
        int blockIndex,
        DocumentTableDeleteMode mode)
    {
        var address = new DocumentTableCellAddress(blockIndex, 0, 0);
        if (!TryGetTable(blockIndex, out _))
            return DocumentTableEditResult.NoChange(address);

        IReadOnlyList<Block> replacement = mode == DocumentTableDeleteMode.ReplaceWithEmptyParagraph
            ? [new Paragraph(string.Empty)]
            : Array.Empty<Block>();
        _session.Commands.Execute(new ReplaceBlocksCommand(blockIndex, 1, replacement));
        var caretBlock = Math.Clamp(blockIndex, 0, Math.Max(0, _session.Document.Blocks.Count - 1));
        return DocumentTableEditResult.Changed(
            new DocumentTableCellAddress(caretBlock, 0, 0),
            invalidatesNativeSelection: true);
    }

    public DocumentTableEditResult SplitTable(DocumentTableCellAddress address)
    {
        if (!TryResolveCell(address, out var table, out _)
            || !TableLayoutOperations.TryBuildSplitReplacement(
                table,
                address.RowIndex,
                out var replacement))
        {
            return DocumentTableEditResult.NoChange(address);
        }

        _session.Commands.Execute(new ReplaceBlocksCommand(address.BlockIndex, 1, replacement));
        return DocumentTableEditResult.Changed(
            new DocumentTableCellAddress(address.BlockIndex + 2, 0, address.GridColumn),
            invalidatesNativeSelection: true);
    }

    public DocumentTableEditResult MergeCells(
        DocumentTableCellAddress anchor,
        DocumentTableCellAddress active)
    {
        if (anchor.BlockIndex != active.BlockIndex
            || !TryResolveCell(anchor, out var table, out _)
            || !TryResolveCell(active, out _, out _))
        {
            return DocumentTableEditResult.NoChange(anchor);
        }

        var firstRow = Math.Min(anchor.RowIndex, active.RowIndex);
        var lastRow = Math.Max(anchor.RowIndex, active.RowIndex);
        var firstGridColumn = Math.Min(anchor.GridColumn, active.GridColumn);
        var lastGridColumn = Math.Max(anchor.GridColumn, active.GridColumn);
        if (firstRow == lastRow)
        {
            var row = table.Rows[firstRow];
            var firstCell = CellIndexAtGridColumn(row, firstGridColumn);
            var lastCell = CellIndexAtGridColumn(row, lastGridColumn);
            if (firstCell < 0 || lastCell < 0 || firstCell == lastCell)
                return DocumentTableEditResult.NoChange(anchor);
            _session.Commands.Execute(new MergeCellsHorizontalCommand(
                anchor.BlockIndex,
                firstRow,
                firstCell,
                lastCell));
        }
        else if (firstGridColumn == lastGridColumn)
        {
            _session.Commands.Execute(new MergeCellsVerticalCommand(
                anchor.BlockIndex,
                firstGridColumn,
                firstRow,
                lastRow));
        }
        else
        {
            var row = table.Rows[firstRow];
            var firstCell = CellIndexAtGridColumn(row, firstGridColumn);
            var lastCell = CellIndexAtGridColumn(row, lastGridColumn);
            if (firstCell < 0 || lastCell < 0 || firstCell == lastCell)
                return DocumentTableEditResult.NoChange(anchor);
            _session.Commands.Execute(new MergeCellsHorizontalCommand(
                anchor.BlockIndex,
                firstRow,
                firstCell,
                lastCell));
        }

        return DocumentTableEditResult.Changed(
            new DocumentTableCellAddress(anchor.BlockIndex, firstRow, firstGridColumn),
            invalidatesNativeSelection: true);
    }

    public DocumentTableEditResult EraseBorderAt(DocumentTableCellAddress address)
    {
        if (!TryResolveCell(address, out var table, out _)
            || TableEraserCommandPlanner.PlanByGridColumn(
                table,
                address.RowIndex,
                address.GridColumn) is not { } plan)
        {
            return DocumentTableEditResult.NoChange(address);
        }

        _session.Commands.Execute(new MergeCellsHorizontalCommand(
            address.BlockIndex,
            plan.RowIndex,
            plan.FirstCellIndex,
            plan.LastCellIndex));
        return DocumentTableEditResult.Changed(address, invalidatesNativeSelection: true);
    }

    public DocumentTableEditResult SplitCell(DocumentTableCellAddress address)
    {
        if (!TryResolveCell(address, out _, out var cellIndex))
            return DocumentTableEditResult.NoChange(address);

        _session.Commands.Execute(new SplitCellCommand(
            address.BlockIndex,
            address.RowIndex,
            cellIndex));
        return DocumentTableEditResult.Changed(address, invalidatesNativeSelection: true);
    }

    public DocumentTableEditResult SetCellShading(
        IReadOnlyList<DocumentTableCellAddress> addresses,
        string? colorHex) =>
        ExecuteForCells(
            addresses,
            "Cell Shading",
            (address, cellIndex) => new SetCellShadingCommand(
                address.BlockIndex,
                address.RowIndex,
                cellIndex,
                colorHex));

    public DocumentTableEditResult SetCellAlignment(
        IReadOnlyList<DocumentTableCellAddress> addresses,
        TableCellVerticalAlignment verticalAlignment,
        TextAlignment horizontalAlignment) =>
        ExecuteForCells(
            addresses,
            "Cell Alignment",
            (address, cellIndex) => new SetCellAlignmentCommand(
                address.BlockIndex,
                address.RowIndex,
                cellIndex,
                verticalAlignment,
                horizontalAlignment));

    public DocumentTableEditResult SetCellBorders(
        DocumentTableCellAddress address,
        CellBorders? borders)
    {
        if (!TryResolveCell(address, out _, out var cellIndex))
            return DocumentTableEditResult.NoChange(address);
        _session.Commands.Execute(new SetCellBorderPayloadCommand(
            address.BlockIndex,
            address.RowIndex,
            cellIndex,
            borders));
        return DocumentTableEditResult.Changed(address);
    }

    public DocumentTableEditResult SetCellBorderEdges(
        IReadOnlyList<DocumentTableCellBorderEdit> edits,
        BorderLineStyle style,
        string colorHex,
        double widthPt,
        bool clearEdges)
    {
        ArgumentNullException.ThrowIfNull(edits);
        var targets = edits
            .Select(edit => TryResolveCell(edit.Address, out _, out var cellIndex)
                ? (Edit: edit, CellIndex: cellIndex)
                : ((DocumentTableCellBorderEdit Edit, int CellIndex)?)null)
            .Where(target => target.HasValue)
            .Select(target => target!.Value)
            .GroupBy(target => (
                target.Edit.Address.BlockIndex,
                target.Edit.Address.RowIndex,
                target.CellIndex))
            .Select(group => group.First())
            .ToArray();
        if (targets.Length == 0)
            return DocumentTableEditResult.NoChange(default);

        ExecuteGroup(
            targets.Select(target => (IDocumentCommand)new SetCellBordersCommand(
                target.Edit.Address.BlockIndex,
                target.Edit.Address.RowIndex,
                target.CellIndex,
                target.Edit.Edges,
                style,
                colorHex,
                widthPt,
                clearEdges)).ToArray(),
            "Cell Borders");
        return DocumentTableEditResult.Changed(targets[0].Edit.Address);
    }

    public DocumentTableEditResult SetCellTextDirection(
        DocumentTableCellAddress address,
        CellTextDirection direction)
    {
        if (!TryResolveCell(address, out _, out var cellIndex))
            return DocumentTableEditResult.NoChange(address);
        _session.Commands.Execute(new SetCellTextDirectionCommand(
            address.BlockIndex,
            address.RowIndex,
            cellIndex,
            direction));
        return DocumentTableEditResult.Changed(address);
    }

    public DocumentTableEditResult ApplyStyle(
        DocumentTableCellAddress address,
        DocumentTableStyle style)
    {
        ArgumentNullException.ThrowIfNull(style);
        if (!TryGetTable(address.BlockIndex, out _))
            return DocumentTableEditResult.NoChange(address);
        _session.Commands.Execute(new ApplyTableStyleCommand(address.BlockIndex, style));
        return DocumentTableEditResult.Changed(address);
    }

    public DocumentTableEditResult ApplyProperties(
        DocumentTableCellAddress address,
        TablePropertiesValues values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (!TryResolveCell(address, out _, out var cellIndex))
            return DocumentTableEditResult.NoChange(address);
        _session.Commands.Execute(new ApplyTablePropertiesCommand(
            address.BlockIndex,
            address.RowIndex,
            cellIndex,
            values));
        return DocumentTableEditResult.Changed(address);
    }

    public DocumentTableTextEditResult InsertFormula(
        DocumentTableCellAddress address,
        int paragraphIndex,
        int textOffset,
        TableFormulaField formula)
    {
        ArgumentNullException.ThrowIfNull(formula);
        if (!TryResolveCell(address, out _, out var cellIndex))
            return new DocumentTableTextEditResult(false, address, paragraphIndex, textOffset);

        var command = new InsertTableCellFormulaCommand(
            address.BlockIndex,
            address.RowIndex,
            cellIndex,
            paragraphIndex,
            textOffset,
            formula);
        _session.Commands.Execute(command);
        return new DocumentTableTextEditResult(
            true,
            address,
            paragraphIndex,
            textOffset + command.InsertedTextLength);
    }

    public DocumentTableTextEditResult InsertNote(
        DocumentTableCellAddress address,
        int paragraphIndex,
        int textOffset,
        string? text,
        bool footnote)
    {
        if (!TryResolveCell(address, out _, out var cellIndex))
            return new DocumentTableTextEditResult(false, address, paragraphIndex, textOffset);

        var id = footnote
            ? _session.Document.NextFootnoteId()
            : _session.Document.NextEndnoteId();
        _session.Commands.Execute(new InsertTableCellNoteCommand(
            id,
            footnote,
            text ?? string.Empty,
            address.BlockIndex,
            address.RowIndex,
            cellIndex,
            paragraphIndex,
            textOffset));
        var markerLength = id.ToString(System.Globalization.CultureInfo.InvariantCulture).Length;
        return new DocumentTableTextEditResult(
            true,
            address,
            paragraphIndex,
            textOffset + markerLength);
    }

    public DocumentTableEditResult UpdateFormatting(
        DocumentTableCellAddress address,
        Func<TableFormatting, TableFormatting> update)
    {
        ArgumentNullException.ThrowIfNull(update);
        if (!TryResolveCell(address, out var table, out _))
            return DocumentTableEditResult.NoChange(address);
        _session.Commands.Execute(new SetTableFormattingCommand(
            address.BlockIndex,
            update(table.Formatting)));
        return DocumentTableEditResult.Changed(address);
    }

    public DocumentTableEditResult DistributeRows(DocumentTableCellAddress address) =>
        ExecuteForTable(address, new DistributeTableRowsCommand(address.BlockIndex));

    public DocumentTableEditResult DistributeColumns(DocumentTableCellAddress address) =>
        ExecuteForTable(address, new DistributeTableColumnsCommand(address.BlockIndex));

    public DocumentTableEditResult SetAutoFit(
        DocumentTableCellAddress address,
        AutoFitMode mode) =>
        ExecuteForTable(address, new SetTableAutoFitCommand(address.BlockIndex, mode));

    public DocumentTableEditResult SortRows(
        DocumentTableCellAddress address,
        SortKind kind,
        bool ascending,
        bool caseSensitive,
        bool hasHeaderRow)
    {
        if (!TryResolveCell(address, out var table, out var cellIndex)
            || table.Rows.Count < 2)
        {
            return DocumentTableEditResult.NoChange(address);
        }

        var sorted = ParagraphSort.SortRows(
            table.Rows,
            cellIndex,
            kind,
            ascending,
            caseSensitive,
            hasHeaderRow);
        var replacement = TableLayoutOperations.CopyTableWithRows(table, sorted);
        _session.Commands.Execute(new ReplaceBlocksCommand(address.BlockIndex, 1, [replacement]));
        return DocumentTableEditResult.Changed(address, invalidatesNativeSelection: true);
    }

    public DocumentTableEditResult ConvertToText(
        DocumentTableCellAddress address,
        char delimiter)
    {
        if (!TryResolveCell(address, out var table, out _))
            return DocumentTableEditResult.NoChange(address);
        var paragraphs = TextTableConvert.TableToText(table, delimiter);
        _session.Commands.Execute(new ReplaceBlocksCommand(address.BlockIndex, 1, [.. paragraphs]));
        return DocumentTableEditResult.Changed(
            address with { RowIndex = 0, GridColumn = 0 },
            invalidatesNativeSelection: true);
    }

    public DocumentTableEditResult SetCellContent(
        DocumentTableCellAddress address,
        IReadOnlyList<Paragraph> paragraphs)
    {
        ArgumentNullException.ThrowIfNull(paragraphs);
        if (!TryResolveCell(address, out _, out var cellIndex))
            return DocumentTableEditResult.NoChange(address);
        _session.Commands.Execute(new SetTableCellContentCommand(
            address.BlockIndex,
            address.RowIndex,
            cellIndex,
            paragraphs));
        return DocumentTableEditResult.Changed(address);
    }

    private DocumentTableEditResult ExecuteForCells(
        IReadOnlyList<DocumentTableCellAddress> addresses,
        string undoLabel,
        Func<DocumentTableCellAddress, int, IDocumentCommand> build)
    {
        ArgumentNullException.ThrowIfNull(addresses);
        ArgumentNullException.ThrowIfNull(build);
        var targets = addresses
            .Distinct()
            .Select(address => TryResolveCell(address, out _, out var cellIndex)
                ? (Address: address, CellIndex: cellIndex)
                : ((DocumentTableCellAddress Address, int CellIndex)?)null)
            .Where(target => target.HasValue)
            .Select(target => target!.Value)
            .GroupBy(target => (
                target.Address.BlockIndex,
                target.Address.RowIndex,
                target.CellIndex))
            .Select(group => group.First())
            .ToArray();
        if (targets.Length == 0)
            return DocumentTableEditResult.NoChange(default);

        ExecuteGroup(
            targets.Select(target => build(target.Address, target.CellIndex)).ToArray(),
            undoLabel);
        return DocumentTableEditResult.Changed(targets[0].Address);
    }

    private DocumentTableEditResult ExecuteForTable(
        DocumentTableCellAddress address,
        IDocumentCommand command)
    {
        if (!TryResolveCell(address, out _, out _))
            return DocumentTableEditResult.NoChange(address);
        _session.Commands.Execute(command);
        return DocumentTableEditResult.Changed(address);
    }

    private void ExecuteGroup(IReadOnlyList<IDocumentCommand> commands, string undoLabel)
    {
        if (commands.Count == 1)
        {
            _session.Commands.Execute(commands[0]);
            return;
        }

        _session.Commands.BeginUndoGroup();
        try
        {
            foreach (var command in commands)
                _session.Commands.Execute(command);
            _session.Commands.CommitUndoGroup(undoLabel);
        }
        catch
        {
            _session.Commands.AbortUndoGroup();
            throw;
        }
    }

    private bool TryGetTable(int blockIndex, out Table table)
    {
        table = null!;
        if (blockIndex < 0
            || blockIndex >= _session.Document.Blocks.Count
            || _session.Document.Blocks[blockIndex] is not Table resolved)
        {
            return false;
        }

        table = resolved;
        return true;
    }

    private bool TryResolveCell(
        DocumentTableCellAddress address,
        out Table table,
        out int cellIndex)
    {
        cellIndex = -1;
        if (!TryGetTable(address.BlockIndex, out table)
            || address.RowIndex < 0
            || address.RowIndex >= table.Rows.Count)
        {
            return false;
        }

        cellIndex = CellIndexAtGridColumn(table.Rows[address.RowIndex], address.GridColumn);
        return cellIndex >= 0;
    }

    private bool TryGetCell(
        DocumentTableCellAddress address,
        int cellIndex,
        out TableCell cell)
    {
        cell = null!;
        if (!TryGetTable(address.BlockIndex, out var table)
            || address.RowIndex < 0
            || address.RowIndex >= table.Rows.Count
            || cellIndex < 0
            || cellIndex >= table.Rows[address.RowIndex].Cells.Count)
        {
            return false;
        }

        cell = table.Rows[address.RowIndex].Cells[cellIndex];
        return true;
    }

    private static int CellIndexAtGridColumn(TableRow row, int gridColumn)
    {
        if (gridColumn < 0)
            return -1;
        var gridPosition = 0;
        for (var index = 0; index < row.Cells.Count; index++)
        {
            gridPosition += Math.Max(1, row.Cells[index].GridSpan);
            if (gridColumn < gridPosition)
                return index;
        }
        return -1;
    }

    private static int GridWidth(Table table) =>
        table.Rows.Count == 0
            ? Math.Max(0, table.ColumnCount)
            : table.Rows[0].Cells.Sum(cell => Math.Max(1, cell.GridSpan));
}
