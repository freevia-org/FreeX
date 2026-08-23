using FreeW.Core.Model;
using FreeW.App.Presentation.Dialogs;
using FreeW.App.Presentation.DocumentView;
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

        var gridColumn = TableGridProjection.StartColumn(table.Rows[rowIndex], cellIndex);
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

    /// <summary>
    /// Expands renderer-native selection endpoints into canonical row-major model cell addresses.
    /// Grid-spanned cells are emitted once, reversed endpoints are normalized, and cross-table or
    /// invalid endpoint ranges are rejected.
    /// </summary>
    public IReadOnlyList<DocumentTableCellAddress> AddressesInRange(
        DocumentTableCellAddress anchor,
        DocumentTableCellAddress active)
    {
        if (anchor.BlockIndex != active.BlockIndex
            || !TryResolveCell(anchor, out var table, out _)
            || !TryResolveCell(active, out _, out _))
        {
            return Array.Empty<DocumentTableCellAddress>();
        }

        var minRow = Math.Min(anchor.RowIndex, active.RowIndex);
        var maxRow = Math.Max(anchor.RowIndex, active.RowIndex);
        var minColumn = Math.Min(anchor.GridColumn, active.GridColumn);
        var maxColumn = Math.Max(anchor.GridColumn, active.GridColumn);
        var addresses = new List<DocumentTableCellAddress>();

        for (var rowIndex = minRow; rowIndex <= maxRow; rowIndex++)
        {
            foreach (var projected in TableGridProjection.ProjectRow(table.Rows[rowIndex]))
            {
                if (projected.EndColumnExclusive <= minColumn
                    || projected.StartColumn > maxColumn)
                {
                    continue;
                }

                addresses.Add(new DocumentTableCellAddress(
                    anchor.BlockIndex,
                    rowIndex,
                    projected.StartColumn));
            }
        }

        return addresses;
    }

    /// <summary>
    /// Expands a semantic border preset over a renderer-native table selection. Selection endpoints
    /// are normalized in logical-grid space, horizontally merged cells are emitted once, and composite
    /// Outside/Inside presets are reduced to primitive per-cell edges before command construction.
    /// </summary>
    public IReadOnlyList<DocumentTableCellBorderEdit> BorderEditsInRange(
        DocumentTableCellAddress anchor,
        DocumentTableCellAddress active,
        CellBorderEdges edges)
    {
        if (anchor.BlockIndex != active.BlockIndex
            || !TryResolveCell(anchor, out var table, out _)
            || !TryResolveCell(active, out _, out _)
            || TableGridProjection.At(table, anchor.RowIndex, anchor.GridColumn) is not { } anchorCell
            || TableGridProjection.At(table, active.RowIndex, active.GridColumn) is not { } activeCell)
        {
            return Array.Empty<DocumentTableCellBorderEdit>();
        }

        var minRow = Math.Min(anchor.RowIndex, active.RowIndex);
        var maxRow = Math.Max(anchor.RowIndex, active.RowIndex);
        var minColumn = Math.Min(anchorCell.StartColumn, activeCell.StartColumn);
        var maxColumn = Math.Max(anchorCell.EndColumnExclusive, activeCell.EndColumnExclusive) - 1;
        var edits = new List<DocumentTableCellBorderEdit>();

        for (var rowIndex = minRow; rowIndex <= maxRow; rowIndex++)
        {
            foreach (var projected in TableGridProjection.ProjectRow(table.Rows[rowIndex]))
            {
                if (projected.EndColumnExclusive <= minColumn || projected.StartColumn > maxColumn)
                    continue;

                var primitiveEdges = ResolveBorderEdges(
                    edges,
                    rowIndex,
                    projected.StartColumn,
                    projected.EndColumnExclusive - 1,
                    minRow,
                    maxRow,
                    minColumn,
                    maxColumn);
                if (primitiveEdges == CellBorderEdges.None)
                    continue;

                edits.Add(new DocumentTableCellBorderEdit(
                    new DocumentTableCellAddress(anchor.BlockIndex, rowIndex, projected.StartColumn),
                    primitiveEdges));
            }
        }

        return edits;
    }

    private static CellBorderEdges ResolveBorderEdges(
        CellBorderEdges edges,
        int row,
        int firstGridColumn,
        int lastGridColumn,
        int minRow,
        int maxRow,
        int minColumn,
        int maxColumn)
    {
        if ((edges & CellBorderEdges.All) == CellBorderEdges.All)
            return CellBorderEdges.All;

        var result = edges & CellBorderEdges.All;
        if ((edges & CellBorderEdges.Outside) != 0)
        {
            if (row == minRow) result |= CellBorderEdges.Top;
            if (row == maxRow) result |= CellBorderEdges.Bottom;
            if (firstGridColumn == minColumn) result |= CellBorderEdges.Left;
            if (lastGridColumn == maxColumn) result |= CellBorderEdges.Right;
        }

        if ((edges & CellBorderEdges.Inside) != 0)
        {
            if (row < maxRow) result |= CellBorderEdges.Bottom;
            if (lastGridColumn < maxColumn) result |= CellBorderEdges.Right;
        }

        return result;
    }

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
            insertAt += TableGridProjection.NormalizeSpan(cell.GridSpan);
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
            // Rectangular selection spanning multiple rows AND columns: horizontally merge every
            // touched row across the column range first (each row collapses to a single cell that
            // spans the full width), then vertically merge that column range so the whole block
            // becomes one cell, not just its first row.
            var commands = new List<IDocumentCommand>();
            for (var r = firstRow; r <= lastRow; r++)
            {
                var row = table.Rows[r];
                var firstCell = CellIndexAtGridColumn(row, firstGridColumn);
                var lastCell = CellIndexAtGridColumn(row, lastGridColumn);
                if (firstCell < 0 || lastCell < 0)
                    return DocumentTableEditResult.NoChange(anchor);
                if (firstCell != lastCell)
                {
                    commands.Add(new MergeCellsHorizontalCommand(
                        anchor.BlockIndex,
                        r,
                        firstCell,
                        lastCell));
                }
            }

            commands.Add(new MergeCellsVerticalCommand(
                anchor.BlockIndex,
                firstGridColumn,
                firstRow,
                lastRow));

            DocumentUndoGroupExecutor.Execute(_session.Commands, commands, "Merge Cells");
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

    public DocumentTableEditResult SplitCell(
        DocumentTableCellAddress address,
        int rows = 1,
        int columns = 1)
    {
        if (!TryResolveCell(address, out _, out var cellIndex))
            return DocumentTableEditResult.NoChange(address);

        _session.Commands.Execute(new SplitCellCommand(
            address.BlockIndex,
            address.RowIndex,
            cellIndex,
            rows,
            columns));
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

        DocumentUndoGroupExecutor.Execute(
            _session.Commands,
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
        CellTextDirection direction) =>
        SetCellTextDirection([address], direction);

    public DocumentTableEditResult SetCellTextDirection(
        IReadOnlyList<DocumentTableCellAddress> addresses,
        CellTextDirection direction) =>
        ExecuteForCells(
            addresses,
            "Cell Text Direction",
            (address, cellIndex) => new SetCellTextDirectionCommand(
                address.BlockIndex,
                address.RowIndex,
                cellIndex,
                direction));

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
        if (!TryResolveCellParagraph(address, paragraphIndex, out var cellIndex))
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
            command.EffectiveInsertionOffset + command.InsertedTextLength);
    }

    public DocumentTableTextEditResult InsertNote(
        DocumentTableCellAddress address,
        int paragraphIndex,
        int textOffset,
        string? text,
        bool footnote)
    {
        if (!TryResolveCellParagraph(address, paragraphIndex, out var cellIndex))
            return new DocumentTableTextEditResult(false, address, paragraphIndex, textOffset);

        var id = footnote
            ? _session.Document.NextFootnoteId()
            : _session.Document.NextEndnoteId();
        var command = new InsertTableCellNoteCommand(
            id,
            footnote,
            text ?? string.Empty,
            address.BlockIndex,
            address.RowIndex,
            cellIndex,
            paragraphIndex,
            textOffset);
        _session.Commands.Execute(command);
        var markerLength = id.ToString(System.Globalization.CultureInfo.InvariantCulture).Length;
        return new DocumentTableTextEditResult(
            true,
            address,
            paragraphIndex,
            command.EffectiveInsertionOffset + markerLength);
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
        if (!TryResolveCell(address, out var table, out _)
            || table.Rows.Count < 2)
        {
            return DocumentTableEditResult.NoChange(address);
        }

        // Sort by the caret's logical grid column, not its raw cell-list index in the caret's own row:
        // ParagraphSort.SortRows re-projects that grid column through TableGridProjection independently
        // for every row, so rows whose merge layout (GridSpan) differs from the caret's row still read the
        // correct cell instead of an unrelated one (or none, sorting as blank).
        var sorted = ParagraphSort.SortRows(
            table.Rows,
            address.GridColumn,
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
        if (!TryResolveCell(address, out _, out _))
            return DocumentTableEditResult.NoChange(address);
        var plan = DocumentTableConversionMutationPlanner.PlanTableToText(
            _session.Document,
            address.BlockIndex,
            delimiter);
        if (plan is null)
            return DocumentTableEditResult.NoChange(address);
        _session.Commands.Execute(new ReplaceBlocksCommand(plan.StartIndex, plan.RemoveCount, plan.Replacement));
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

    public DocumentTableEditResult SetCellText(
        DocumentTableCellAddress address,
        string text)
    {
        if (!TryResolveCell(address, out var table, out var cellIndex))
            return DocumentTableEditResult.NoChange(address);

        var cell = table.Rows[address.RowIndex].Cells[cellIndex];
        var formatting = cell.Paragraphs.FirstOrDefault()?.Runs.FirstOrDefault()?.Formatting
            ?? RunFormatting.Default;
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run(text, formatting));
        _session.Commands.Execute(new SetTableCellContentCommand(
            address.BlockIndex,
            address.RowIndex,
            cellIndex,
            [paragraph]));
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

        DocumentUndoGroupExecutor.Execute(
            _session.Commands,
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

        cellIndex = TableGridProjection.At(table.Rows[address.RowIndex], address.GridColumn)?.CellIndex ?? -1;
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

    private bool TryResolveCellParagraph(
        DocumentTableCellAddress address,
        int paragraphIndex,
        out int cellIndex)
    {
        if (!TryResolveCell(address, out var table, out cellIndex))
            return false;

        var paragraphs = table.Rows[address.RowIndex].Cells[cellIndex].Paragraphs;
        return paragraphIndex >= 0 && paragraphIndex < paragraphs.Count;
    }

    private static int GridWidth(Table table) =>
        table.Rows.Count == 0
            ? Math.Max(0, table.ColumnCount)
            : TableGridProjection.RowWidth(table.Rows[0]);

    private static int CellIndexAtGridColumn(TableRow row, int gridColumn) =>
        TableGridProjection.At(row, gridColumn)?.CellIndex ?? -1;
}
