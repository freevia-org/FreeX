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
        IReadOnlyList<IReadOnlyList<string>> rows,
        bool preserveText = false)
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
                var text = rows[rowIndex][colIndex];
                edits.Add((address, Cell.FromValue(preserveText ? new TextValue(text) : ParseClipboardValue(text))));
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
                                ? PasteCommandCellFactory.TransposeDestination(sourceRange, c.Source, targetSheetId, destination)
                                : PasteCommandCellFactory.Shift(
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
                    if (options.ContentKind == PasteSpecialContentKind.ValuesAndNumberFormats)
                        pastedCell.StyleId = sourceCell.StyleId;
                }
                else
                {
                    var destinationAddress = options.Transpose
                        ? PasteCommandCellFactory.TransposeDestination(sourceRange, source, targetSheetId, destination)
                        : PasteCommandCellFactory.Shift(
                            source,
                            targetSheetId,
                            (int)destination.Row - (int)sourceRange.Start.Row,
                            (int)destination.Col - (int)sourceRange.Start.Col);
                    var destinationStyle = PasteCommandCellFactory.GetDestinationStyle(targetSheet, destinationAddress);
                    var pastedRowDelta = (int)destinationAddress.Row - (int)source.Row;
                    var pastedColDelta = (int)destinationAddress.Col - (int)source.Col;
                    var pastedPasteOp = new PasteOffsetOp(pastedRowDelta, pastedColDelta);
                    pastedCell = PasteCommandCellFactory.BuildPastedCell(
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
                    .Select(c => (PasteCommandCellFactory.Shift(c.Source, targetSheetId, rowDelta, colDelta), c.Cell.StyleId))
                    .ToList());
        }

        var edits = new List<(CellAddress Address, Cell Cell)>(sourceCells.Count);
        foreach (var (source, sourceCell) in sourceCells)
        {
            if (options.SkipBlanks && IsBlank(sourceCell))
                continue;

            var destinationAddress = PasteCommandCellFactory.Shift(source, targetSheetId, rowDelta, colDelta);
            var destinationStyle = PasteCommandCellFactory.GetDestinationStyle(targetSheet, destinationAddress);
            var pastedCell = PasteCommandCellFactory.BuildPastedCell(
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

    private static bool IsBlank(Cell cell) =>
        cell.FormulaText is null && cell.Value is BlankValue;

    private static ScalarValue ParseClipboardValue(string text) =>
        double.TryParse(
            text,
            System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture,
            out var number)
            ? new NumberValue(number)
            : new TextValue(text);
}
