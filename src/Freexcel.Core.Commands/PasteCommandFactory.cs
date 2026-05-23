using Freexcel.Core.Formula;
using Freexcel.Core.Model;

namespace Freexcel.Core.Commands;

public enum PasteCellsMode
{
    All,
    Values,
    Formulas,
    Formats
}

public static class PasteCommandFactory
{
    public static IWorkbookCommand CreateExternalTextPasteCommand(
        SheetId targetSheetId,
        CellAddress destination,
        IReadOnlyList<IReadOnlyList<string>> rows)
    {
        var edits = new List<(CellAddress Address, Cell Cell)>();
        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            for (var colIndex = 0; colIndex < rows[rowIndex].Count; colIndex++)
            {
                var address = new CellAddress(
                    targetSheetId,
                    destination.Row + (uint)rowIndex,
                    destination.Col + (uint)colIndex);
                edits.Add((address, Cell.FromValue(ParseClipboardValue(rows[rowIndex][colIndex]))));
            }
        }

        return new EditCellsCommand(targetSheetId, edits);
    }

    public static IWorkbookCommand CreateInternalPasteCommand(
        Workbook workbook,
        SheetId targetSheetId,
        GridRange sourceRange,
        IReadOnlyList<(CellAddress Source, Cell Cell)> sourceCells,
        CellAddress destination,
        PasteCellsMode mode,
        PasteSpecialOptions options)
    {
        var targetSheet = workbook.GetSheet(targetSheetId);
        var activeSheetName = targetSheet?.Name ?? "";

        if (options.ContentKind == PasteSpecialContentKind.AllMergingConditionalFormats)
        {
            var pasteCommand = CreateInternalPasteCommand(
                workbook,
                targetSheetId,
                sourceRange,
                sourceCells,
                destination,
                mode,
                options with { ContentKind = PasteSpecialContentKind.Default });

            return new CompositeWorkbookCommand(
                "Paste Special",
                [
                    pasteCommand,
                    new PasteConditionalFormatsCommand(targetSheetId, sourceRange, destination, options.Transpose)
                ]);
        }

        if (options.Transpose ||
            options.Operation != PasteSpecialOperation.None ||
            options.SkipBlanks ||
            options.ContentKind != PasteSpecialContentKind.Default)
        {
            if (mode == PasteCellsMode.Formats && options.Operation == PasteSpecialOperation.None)
            {
                return new PasteFormatsCommand(
                    targetSheetId,
                    sourceCells
                        .Where(c => !options.SkipBlanks || !IsBlank(c.Cell))
                        .Select(c => (
                            options.Transpose
                                ? TransposeDestination(sourceRange, c.Source, targetSheetId, destination)
                                : Shift(
                                    c.Source,
                                    targetSheetId,
                                    (int)destination.Row - (int)sourceRange.Start.Row,
                                    (int)destination.Col - (int)sourceRange.Start.Col),
                            c.Cell.StyleId))
                        .ToList());
            }

            var specialCells = new List<(CellAddress Source, Cell Cell)>(sourceCells.Count);
            foreach (var (source, sourceCell) in sourceCells)
            {
                if (options.SkipBlanks && IsBlank(sourceCell))
                    continue;

                Cell pastedCell;
                if (options.Operation != PasteSpecialOperation.None)
                {
                    pastedCell = Cell.FromValue(sourceCell.Value);
                }
                else
                {
                    var destinationAddress = options.Transpose
                        ? TransposeDestination(sourceRange, source, targetSheetId, destination)
                        : Shift(
                            source,
                            targetSheetId,
                            (int)destination.Row - (int)sourceRange.Start.Row,
                            (int)destination.Col - (int)sourceRange.Start.Col);
                    var destinationStyle = GetDestinationStyle(targetSheet, destinationAddress);
                    var pastedRowDelta = (int)destinationAddress.Row - (int)source.Row;
                    var pastedColDelta = (int)destinationAddress.Col - (int)source.Col;
                    var pastedPasteOp = new PasteOffsetOp(pastedRowDelta, pastedColDelta);
                    pastedCell = BuildPastedCell(
                        workbook,
                        sourceCell,
                        mode,
                        options.ContentKind,
                        pastedPasteOp,
                        activeSheetName,
                        pastedRowDelta,
                        pastedColDelta,
                        destinationStyle);
                }

                specialCells.Add((source, pastedCell));
            }

            return new PasteSpecialCellsCommand(
                targetSheetId,
                sourceRange,
                specialCells,
                destination,
                options);
        }

        var rowDelta = (int)destination.Row - (int)sourceRange.Start.Row;
        var colDelta = (int)destination.Col - (int)sourceRange.Start.Col;
        var pasteOp = new PasteOffsetOp(rowDelta, colDelta);

        if (mode == PasteCellsMode.Formats)
        {
            return new PasteFormatsCommand(
                targetSheetId,
                sourceCells
                    .Where(c => !options.SkipBlanks || !IsBlank(c.Cell))
                    .Select(c => (Shift(c.Source, targetSheetId, rowDelta, colDelta), c.Cell.StyleId))
                    .ToList());
        }

        var edits = new List<(CellAddress Address, Cell Cell)>(sourceCells.Count);
        foreach (var (source, sourceCell) in sourceCells)
        {
            if (options.SkipBlanks && IsBlank(sourceCell))
                continue;

            var destinationAddress = Shift(source, targetSheetId, rowDelta, colDelta);
            var destinationStyle = GetDestinationStyle(targetSheet, destinationAddress);
            var pastedCell = BuildPastedCell(
                workbook,
                sourceCell,
                mode,
                options.ContentKind,
                pasteOp,
                activeSheetName,
                rowDelta,
                colDelta,
                destinationStyle);
            edits.Add((destinationAddress, pastedCell));
        }

        return mode == PasteCellsMode.All
            ? new PasteCellsCommand(targetSheetId, edits)
            : new EditCellsCommand(targetSheetId, edits);
    }

    private static Cell BuildPastedCell(
        Workbook workbook,
        Cell sourceCell,
        PasteCellsMode mode,
        PasteSpecialContentKind contentKind,
        PasteOffsetOp pasteOp,
        string activeSheetName,
        int rowDelta,
        int colDelta,
        StyleId destinationStyle)
    {
        if (contentKind == PasteSpecialContentKind.ValuesAndNumberFormats)
        {
            var valueCell = Cell.FromValue(sourceCell.Value);
            valueCell.StyleId = MergeNumberFormat(workbook, destinationStyle, sourceCell.StyleId);
            return valueCell;
        }

        if (contentKind == PasteSpecialContentKind.ValuesAndSourceFormatting)
        {
            var valueCell = Cell.FromValue(sourceCell.Value);
            valueCell.StyleId = sourceCell.StyleId;
            return valueCell;
        }

        if (contentKind == PasteSpecialContentKind.FormulasAndNumberFormats)
        {
            var formulaCell = BuildFormulaOrValueCell(
                sourceCell,
                pasteOp,
                activeSheetName,
                rowDelta,
                colDelta,
                destinationStyle);
            formulaCell.StyleId = MergeNumberFormat(workbook, destinationStyle, sourceCell.StyleId);
            return formulaCell;
        }

        if (contentKind == PasteSpecialContentKind.AllExceptBorders)
        {
            var pastedCell = BuildAllCell(sourceCell, pasteOp, activeSheetName, rowDelta, colDelta);
            pastedCell.StyleId = MergeAllExceptBorders(workbook, sourceCell.StyleId, destinationStyle);
            return pastedCell;
        }

        if (mode == PasteCellsMode.Values)
        {
            var valueCell = Cell.FromValue(sourceCell.Value);
            valueCell.StyleId = destinationStyle;
            return valueCell;
        }

        if (mode == PasteCellsMode.Formulas)
            return BuildFormulaOrValueCell(sourceCell, pasteOp, activeSheetName, rowDelta, colDelta, destinationStyle);

        return BuildAllCell(sourceCell, pasteOp, activeSheetName, rowDelta, colDelta);
    }

    private static Cell BuildFormulaOrValueCell(
        Cell sourceCell,
        PasteOffsetOp pasteOp,
        string activeSheetName,
        int rowDelta,
        int colDelta,
        StyleId destinationStyle)
    {
        var pastedCell = sourceCell.Clone();
        if (pastedCell.FormulaText is not null && (rowDelta != 0 || colDelta != 0))
        {
            pastedCell.FormulaText =
                FormulaRewriter.Rewrite(pastedCell.FormulaText, pasteOp, activeSheetName)
                ?? pastedCell.FormulaText;
        }

        if (!pastedCell.HasFormula)
        {
            var valueCell = Cell.FromValue(sourceCell.Value);
            valueCell.StyleId = destinationStyle;
            return valueCell;
        }

        pastedCell.StyleId = destinationStyle;

        return pastedCell;
    }

    private static Cell BuildAllCell(
        Cell sourceCell,
        PasteOffsetOp pasteOp,
        string activeSheetName,
        int rowDelta,
        int colDelta)
    {
        var pastedCell = sourceCell.Clone();
        if (pastedCell.FormulaText is not null && (rowDelta != 0 || colDelta != 0))
        {
            pastedCell.FormulaText =
                FormulaRewriter.Rewrite(pastedCell.FormulaText, pasteOp, activeSheetName)
                ?? pastedCell.FormulaText;
        }

        return pastedCell;
    }

    private static StyleId MergeNumberFormat(Workbook workbook, StyleId destinationStyleId, StyleId sourceStyleId)
    {
        var style = workbook.GetStyle(destinationStyleId).Clone();
        style.NumberFormat = workbook.GetStyle(sourceStyleId).NumberFormat;
        return workbook.RegisterStyle(style);
    }

    private static StyleId MergeAllExceptBorders(Workbook workbook, StyleId sourceStyleId, StyleId destinationStyleId)
    {
        var style = workbook.GetStyle(sourceStyleId).Clone();
        var destinationStyle = workbook.GetStyle(destinationStyleId);
        style.BorderTop = destinationStyle.BorderTop;
        style.BorderRight = destinationStyle.BorderRight;
        style.BorderBottom = destinationStyle.BorderBottom;
        style.BorderLeft = destinationStyle.BorderLeft;
        return workbook.RegisterStyle(style);
    }

    private static CellAddress TransposeDestination(
        GridRange sourceRange,
        CellAddress source,
        SheetId targetSheetId,
        CellAddress destination)
    {
        var rowOffset = source.Row - sourceRange.Start.Row;
        var colOffset = source.Col - sourceRange.Start.Col;
        return new CellAddress(targetSheetId, destination.Row + colOffset, destination.Col + rowOffset);
    }

    private static StyleId GetDestinationStyle(Sheet? targetSheet, CellAddress destinationAddress) =>
        targetSheet?.GetCell(destinationAddress)?.StyleId
        ?? targetSheet?.GetStyleOnly(destinationAddress.Row, destinationAddress.Col)
        ?? StyleId.Default;

    private static bool IsBlank(Cell cell) =>
        cell.FormulaText is null && cell.Value is BlankValue;

    private static CellAddress Shift(CellAddress source, SheetId targetSheetId, int rowDelta, int colDelta)
    {
        int newRow = (int)source.Row + rowDelta;
        int newCol = (int)source.Col + colDelta;
        if (newRow < 1) newRow = 1;
        if (newCol < 1) newCol = 1;
        if (newRow > (int)CellAddress.MaxRow) newRow = (int)CellAddress.MaxRow;
        if (newCol > (int)CellAddress.MaxCol) newCol = (int)CellAddress.MaxCol;
        return new(targetSheetId, (uint)newRow, (uint)newCol);
    }

    private static ScalarValue ParseClipboardValue(string text) =>
        double.TryParse(
            text,
            System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture,
            out var number)
            ? new NumberValue(number)
            : new TextValue(text);
}
