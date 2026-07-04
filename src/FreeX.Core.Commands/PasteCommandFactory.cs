using FreeX.Core.Formula;
using FreeX.Core.Model;

namespace FreeX.Core.Commands;

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
        bool preserveText = false) =>
        CreateExternalTextPasteCommand(
            targetSheetId,
            new GridRange(destination, destination),
            rows,
            preserveText);

    public static IWorkbookCommand CreateExternalTextPasteCommand(
        SheetId targetSheetId,
        GridRange destinationRange,
        IReadOnlyList<IReadOnlyList<string>> rows,
        bool preserveText = false)
    {
        var destination = destinationRange.Start;
        if (destination.Sheet != targetSheetId)
            return new RejectedWorkbookCommand("Paste", "Paste destination must be on the target sheet.");
        if (destinationRange.End.Sheet != targetSheetId)
            return new RejectedWorkbookCommand("Paste", "Paste destination range must be on the target sheet.");

        var rowCount = (ulong)rows.Count;
        var colCount = 0UL;
        foreach (var row in rows)
            colCount = Math.Max(colCount, (ulong)row.Count);
        var targetRowCount = rowCount == 0 ? 0 : Math.Max(rowCount, destinationRange.RowCount);
        var targetColCount = colCount == 0 ? 0 : Math.Max(colCount, destinationRange.ColCount);

        if (targetRowCount > 0 &&
            targetColCount > 0 &&
            !WorksheetBounds.TryGetRectangleEnd(destination, targetRowCount, targetColCount, out _))
        {
            return new RejectedWorkbookCommand("Paste", "Paste destination range is outside the worksheet bounds.");
        }

        var edits = new List<(CellAddress Address, Cell Cell)>();
        for (var rowOffset = 0UL; rowOffset < targetRowCount; rowOffset++)
        {
            var sourceRow = rows[(int)(rowOffset % rowCount)];
            if (sourceRow.Count == 0)
                continue;

            for (var colOffset = 0UL; colOffset < targetColCount; colOffset++)
            {
                var sourceColIndex = (int)(colOffset % colCount);
                if (sourceColIndex >= sourceRow.Count)
                    continue;

                var address = new CellAddress(
                    targetSheetId,
                    destination.Row + (uint)rowOffset,
                    destination.Col + (uint)colOffset);
                var text = sourceRow[sourceColIndex];
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
        PasteSpecialOptions options) =>
        CreateInternalPasteCommand(
            workbook,
            targetSheetId,
            sourceRange,
            sourceCells,
            new GridRange(destination, destination),
            mode,
            options);

    public static IWorkbookCommand CreateInternalPasteCommand(
        Workbook workbook,
        SheetId targetSheetId,
        GridRange sourceRange,
        IReadOnlyList<(CellAddress Source, Cell Cell)> sourceCells,
        GridRange destinationRange,
        PasteCellsMode mode,
        PasteSpecialOptions options)
    {
        var destination = destinationRange.Start;
        var validationError = PasteCommandValidator.ValidateInternalPaste(
            targetSheetId,
            sourceRange,
            sourceCells.Select(c => c.Source),
            destination,
            options.Transpose);
        if (validationError is not null)
            return new RejectedWorkbookCommand("Paste", validationError);

        var pasteRows = options.Transpose ? sourceRange.ColCount : sourceRange.RowCount;
        var pasteCols = options.Transpose ? sourceRange.RowCount : sourceRange.ColCount;
        var targetRows = Math.Max(pasteRows, destinationRange.RowCount);
        var targetCols = Math.Max(pasteCols, destinationRange.ColCount);
        if (destinationRange.End.Sheet != targetSheetId ||
            !WorksheetBounds.IsValidAddress(destinationRange.End) ||
            !WorksheetBounds.TryGetRectangleEnd(destination, targetRows, targetCols, out _))
        {
            return new RejectedWorkbookCommand("Paste", "Paste destination range is outside the worksheet bounds.");
        }

        var targetSheet = workbook.GetSheet(targetSheetId);
        var activeSheetName = targetSheet?.Name ?? "";
        var sourceSheet = workbook.GetSheet(sourceRange.Start.Sheet);

        var shouldTileDestinationRange =
            (targetRows > pasteRows || targetCols > pasteCols) &&
            options.ContentKind != PasteSpecialContentKind.AllMergingConditionalFormats;
        if (shouldTileDestinationRange)
        {
            return CreateTiledInternalPasteCommand(
                workbook,
                targetSheetId,
                targetSheet,
                sourceSheet,
                activeSheetName,
                sourceRange,
                sourceCells,
                destination,
                targetRows,
                targetCols,
                mode,
                options);
        }

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

            var specialCarriesFormatting = options.Operation == PasteSpecialOperation.None && ContentKindCarriesRichTextRuns(options.ContentKind);
            var specialRichTextRuns = specialCarriesFormatting ? sourceSheet?.RichTextRuns : null;
            var specialHyperlinks = specialCarriesFormatting ? sourceSheet?.Hyperlinks : null;
            var specialHyperlinkMetadata = specialCarriesFormatting ? sourceSheet?.HyperlinkMetadata : null;

            var pasteSpecialCommand = new PasteSpecialCellsCommand(
                targetSheetId,
                sourceRange,
                specialCells,
                destination,
                options,
                specialRichTextRuns,
                specialHyperlinks,
                specialHyperlinkMetadata);

            return specialCarriesFormatting && sourceSheet is not null && sourceSheet.MergedRegions.Any(region => region.Overlaps(sourceRange))
                ? new CompositeWorkbookCommand(
                    "Paste Special",
                    [pasteSpecialCommand, new PasteMergedRegionsCommand(targetSheetId, sourceRange, destination, options.Transpose)])
                : pasteSpecialCommand;
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
        var carriesFormatting = mode == PasteCellsMode.All && ContentKindCarriesRichTextRuns(options.ContentKind);
        Dictionary<CellAddress, IReadOnlyList<CellTextRun>>? richTextRuns = carriesFormatting ? [] : null;
        Dictionary<CellAddress, string>? hyperlinks = carriesFormatting ? [] : null;
        Dictionary<CellAddress, HyperlinkMetadata>? hyperlinkMetadata = carriesFormatting ? [] : null;
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

            if (richTextRuns is not null &&
                sourceSheet is not null &&
                sourceSheet.RichTextRuns.TryGetValue(source, out var sourceRuns))
            {
                richTextRuns[destinationAddress] = sourceRuns;
            }

            if (hyperlinks is not null &&
                sourceSheet is not null &&
                sourceSheet.Hyperlinks.TryGetValue(source, out var sourceHyperlink))
            {
                hyperlinks[destinationAddress] = sourceHyperlink;
            }

            if (hyperlinkMetadata is not null &&
                sourceSheet is not null &&
                sourceSheet.HyperlinkMetadata.TryGetValue(source, out var sourceHyperlinkMetadata))
            {
                hyperlinkMetadata[destinationAddress] = sourceHyperlinkMetadata;
            }
        }

        if (mode != PasteCellsMode.All)
            return new EditCellsCommand(targetSheetId, edits);

        var pasteAllCommand = new PasteCellsCommand(targetSheetId, edits, richTextRuns, hyperlinks, hyperlinkMetadata);
        return sourceSheet is not null && sourceSheet.MergedRegions.Any(region => region.Overlaps(sourceRange))
            ? new CompositeWorkbookCommand(
                "Paste",
                [pasteAllCommand, new PasteMergedRegionsCommand(targetSheetId, sourceRange, destination, transpose: false)])
            : pasteAllCommand;
    }

    private static bool IsBlank(Cell cell) =>
        cell.FormulaText is null && cell.Value is BlankValue;

    /// <summary>
    /// Whether a Paste Special content kind copies full cell formatting (and therefore should
    /// also carry per-run rich-text formatting), as opposed to a "values only" / number-format-only
    /// variant that intentionally drops the source's rich-text runs.
    /// </summary>
    private static bool ContentKindCarriesRichTextRuns(PasteSpecialContentKind contentKind) => contentKind switch
    {
        PasteSpecialContentKind.Default => true,
        PasteSpecialContentKind.AllUsingSourceTheme => true,
        PasteSpecialContentKind.AllExceptBorders => true,
        PasteSpecialContentKind.ValuesAndSourceFormatting => true,
        PasteSpecialContentKind.ValuesAndNumberFormats => false,
        PasteSpecialContentKind.FormulasAndNumberFormats => false,
        PasteSpecialContentKind.AllMergingConditionalFormats => true,
        _ => false
    };

    private static IWorkbookCommand CreateTiledInternalPasteCommand(
        Workbook workbook,
        SheetId targetSheetId,
        Sheet? targetSheet,
        Sheet? sourceSheet,
        string activeSheetName,
        GridRange sourceRange,
        IReadOnlyList<(CellAddress Source, Cell Cell)> sourceCells,
        CellAddress destination,
        uint targetRows,
        uint targetCols,
        PasteCellsMode mode,
        PasteSpecialOptions options)
    {
        var sourceLookup = sourceCells.ToDictionary(c => c.Source, c => c.Cell);
        var mergedRegionCommands = sourceSheet is not null && sourceSheet.MergedRegions.Any(region => region.Overlaps(sourceRange))
            ? BuildTiledMergedRegionCommands(targetSheetId, sourceRange, destination, targetRows, targetCols, options.Transpose)
            : null;

        if (mode == PasteCellsMode.Formats && options.Operation == PasteSpecialOperation.None)
        {
            var formats = new List<(CellAddress Address, StyleId StyleId)>((int)Math.Min(int.MaxValue, (long)targetRows * targetCols));
            foreach (var (sourceAddress, destinationAddress) in EnumerateTiledAddresses(
                sourceRange,
                targetSheetId,
                destination,
                targetRows,
                targetCols,
                options.Transpose))
            {
                if (!sourceLookup.TryGetValue(sourceAddress, out var sourceCell) ||
                    options.SkipBlanks && IsBlank(sourceCell))
                {
                    continue;
                }

                formats.Add((destinationAddress, sourceCell.StyleId));
            }

            var formatsCommand = new PasteFormatsCommand(targetSheetId, formats);
            return mergedRegionCommands is null
                ? formatsCommand
                : new CompositeWorkbookCommand("Paste", [formatsCommand, .. mergedRegionCommands]);
        }

        if (options.Operation != PasteSpecialOperation.None)
        {
            var tiledPairs = new List<(CellAddress Source, CellAddress Destination)>(
                (int)Math.Min(int.MaxValue, (long)targetRows * targetCols));
            foreach (var (sourceAddress, destinationAddress) in EnumerateTiledAddresses(
                sourceRange,
                targetSheetId,
                destination,
                targetRows,
                targetCols,
                options.Transpose))
            {
                if (!sourceLookup.ContainsKey(sourceAddress))
                    continue;

                tiledPairs.Add((sourceAddress, destinationAddress));
            }

            var specialCommand = new PasteSpecialCellsCommand(targetSheetId, sourceCells, tiledPairs, options);
            return mergedRegionCommands is null
                ? specialCommand
                : new CompositeWorkbookCommand("Paste Special", [specialCommand, .. mergedRegionCommands]);
        }

        var edits = new List<(CellAddress Address, Cell Cell)>((int)Math.Min(int.MaxValue, (long)targetRows * targetCols));
        var carriesFormatting = mode == PasteCellsMode.All && ContentKindCarriesRichTextRuns(options.ContentKind);
        Dictionary<CellAddress, IReadOnlyList<CellTextRun>>? richTextRuns = carriesFormatting ? [] : null;
        Dictionary<CellAddress, string>? hyperlinks = carriesFormatting ? [] : null;
        Dictionary<CellAddress, HyperlinkMetadata>? hyperlinkMetadata = carriesFormatting ? [] : null;
        foreach (var (sourceAddress, destinationAddress) in EnumerateTiledAddresses(
            sourceRange,
            targetSheetId,
            destination,
            targetRows,
            targetCols,
            options.Transpose))
        {
            if (!sourceLookup.TryGetValue(sourceAddress, out var sourceCell) ||
                options.SkipBlanks && IsBlank(sourceCell))
            {
                continue;
            }

            var destinationStyle = PasteCommandCellFactory.GetDestinationStyle(targetSheet, destinationAddress);
            var pastedRowDelta = (int)destinationAddress.Row - (int)sourceAddress.Row;
            var pastedColDelta = (int)destinationAddress.Col - (int)sourceAddress.Col;
            var pastedPasteOp = new PasteOffsetOp(pastedRowDelta, pastedColDelta);
            var pastedCell = PasteCommandCellFactory.BuildPastedCell(
                workbook,
                sourceCell,
                mode,
                options.ContentKind,
                pastedPasteOp,
                activeSheetName,
                pastedRowDelta,
                pastedColDelta,
                destinationStyle);
            edits.Add((destinationAddress, pastedCell));

            if (richTextRuns is not null &&
                sourceSheet is not null &&
                sourceSheet.RichTextRuns.TryGetValue(sourceAddress, out var sourceRuns))
            {
                richTextRuns[destinationAddress] = sourceRuns;
            }

            if (hyperlinks is not null &&
                sourceSheet is not null &&
                sourceSheet.Hyperlinks.TryGetValue(sourceAddress, out var sourceHyperlink))
            {
                hyperlinks[destinationAddress] = sourceHyperlink;
            }

            if (hyperlinkMetadata is not null &&
                sourceSheet is not null &&
                sourceSheet.HyperlinkMetadata.TryGetValue(sourceAddress, out var sourceHyperlinkMetadata))
            {
                hyperlinkMetadata[destinationAddress] = sourceHyperlinkMetadata;
            }
        }

        IWorkbookCommand tiledCommand = mode == PasteCellsMode.All
            ? new PasteCellsCommand(targetSheetId, edits, richTextRuns, hyperlinks, hyperlinkMetadata)
            : new EditCellsCommand(targetSheetId, edits);

        return mergedRegionCommands is null
            ? tiledCommand
            : new CompositeWorkbookCommand(mode == PasteCellsMode.All ? "Paste" : "Paste Special", [tiledCommand, .. mergedRegionCommands]);
    }

    /// <summary>
    /// Builds one <see cref="PasteMergedRegionsCommand"/> per repeated tile of the source range
    /// within the tiled destination, so a merged region in the copied source is recreated at every
    /// tile offset (mirroring the non-tiled paste path's single-offset merge recreation). Each
    /// command uses the same source-range-relative mapping and destination-collision skip as the
    /// non-tiled path; only the per-tile destination anchor differs.
    /// </summary>
    private static List<IWorkbookCommand> BuildTiledMergedRegionCommands(
        SheetId targetSheetId,
        GridRange sourceRange,
        CellAddress destination,
        uint targetRows,
        uint targetCols,
        bool transpose)
    {
        var rowPeriod = transpose ? sourceRange.ColCount : sourceRange.RowCount;
        var colPeriod = transpose ? sourceRange.RowCount : sourceRange.ColCount;

        var commands = new List<IWorkbookCommand>();
        for (var rowOffset = 0U; rowOffset < targetRows; rowOffset += rowPeriod)
        {
            for (var colOffset = 0U; colOffset < targetCols; colOffset += colPeriod)
            {
                var tileDestination = new CellAddress(
                    targetSheetId,
                    destination.Row + rowOffset,
                    destination.Col + colOffset);
                commands.Add(new PasteMergedRegionsCommand(targetSheetId, sourceRange, tileDestination, transpose));
            }
        }

        return commands;
    }

    private static IEnumerable<(CellAddress Source, CellAddress Destination)> EnumerateTiledAddresses(
        GridRange sourceRange,
        SheetId targetSheetId,
        CellAddress destination,
        uint targetRows,
        uint targetCols,
        bool transpose)
    {
        for (var rowOffset = 0U; rowOffset < targetRows; rowOffset++)
        {
            for (var colOffset = 0U; colOffset < targetCols; colOffset++)
            {
                var sourceRowOffset = transpose
                    ? colOffset % sourceRange.RowCount
                    : rowOffset % sourceRange.RowCount;
                var sourceColOffset = transpose
                    ? rowOffset % sourceRange.ColCount
                    : colOffset % sourceRange.ColCount;
                var sourceAddress = new CellAddress(
                    sourceRange.Start.Sheet,
                    sourceRange.Start.Row + sourceRowOffset,
                    sourceRange.Start.Col + sourceColOffset);
                var destinationAddress = new CellAddress(
                    targetSheetId,
                    destination.Row + rowOffset,
                    destination.Col + colOffset);

                yield return (sourceAddress, destinationAddress);
            }
        }
    }

    private static ScalarValue ParseClipboardValue(string text)
    {
        if (double.TryParse(
                text,
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture,
                out var number) &&
            double.IsFinite(number))
        {
            return new NumberValue(number);
        }

        return new TextValue(text);
    }
}
