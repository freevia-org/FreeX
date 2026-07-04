using FreeX.Core.Model;

namespace FreeX.Core.Commands;

public enum PasteSpecialOperation
{
    None,
    Add,
    Subtract,
    Multiply,
    Divide
}

public enum PasteSpecialContentKind
{
    Default,
    AllUsingSourceTheme,
    AllExceptBorders,
    AllMergingConditionalFormats,
    ValuesAndNumberFormats,
    ValuesAndSourceFormatting,
    FormulasAndNumberFormats
}

public readonly record struct PasteSpecialOptions(
    bool Transpose = false,
    PasteSpecialOperation Operation = PasteSpecialOperation.None,
    bool SkipBlanks = false,
    PasteSpecialContentKind ContentKind = PasteSpecialContentKind.Default);

public sealed class PasteSpecialCellsCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly GridRange _sourceRange;
    private readonly IReadOnlyList<(CellAddress Address, Cell Cell)> _sourceCells;
    private readonly CellAddress _destination;
    private readonly IReadOnlyList<(CellAddress Source, CellAddress Destination)>? _tiledDestinations;
    private readonly PasteSpecialOptions _options;
    private readonly IReadOnlyDictionary<CellAddress, IReadOnlyList<CellTextRun>>? _sourceRichTextRuns;
    private readonly IReadOnlyDictionary<CellAddress, string>? _sourceHyperlinks;
    private readonly IReadOnlyDictionary<CellAddress, HyperlinkMetadata>? _sourceHyperlinkMetadata;
    private List<(CellAddress Address, Cell? OldCell, StyleId? OldStyleOnly, bool HadRichTextRuns, IReadOnlyList<CellTextRun>? OldRichTextRuns, bool HadHyperlink, string? OldHyperlink, bool HadHyperlinkMetadata, HyperlinkMetadata? OldHyperlinkMetadata)>? _snapshot;

    public string Label => "Paste Special";

    public PasteSpecialCellsCommand(
        SheetId sheetId,
        GridRange sourceRange,
        IReadOnlyList<(CellAddress Address, Cell Cell)> sourceCells,
        CellAddress destination,
        PasteSpecialOptions options,
        IReadOnlyDictionary<CellAddress, IReadOnlyList<CellTextRun>>? sourceRichTextRuns = null,
        IReadOnlyDictionary<CellAddress, string>? sourceHyperlinks = null,
        IReadOnlyDictionary<CellAddress, HyperlinkMetadata>? sourceHyperlinkMetadata = null)
    {
        _sheetId = sheetId;
        _sourceRange = sourceRange;
        _sourceCells = sourceCells;
        _destination = destination;
        _options = options;
        _sourceRichTextRuns = sourceRichTextRuns;
        _sourceHyperlinks = sourceHyperlinks;
        _sourceHyperlinkMetadata = sourceHyperlinkMetadata;
    }

    /// <summary>
    /// Constructs a paste-special command that tiles an arithmetic operation across an explicit
    /// set of source-to-destination pairs, mirroring how plain paste tiles a smaller copied block
    /// across a larger selected destination (see PasteCommandFactory.CreateTiledInternalPasteCommand).
    /// </summary>
    public PasteSpecialCellsCommand(
        SheetId sheetId,
        IReadOnlyList<(CellAddress Address, Cell Cell)> sourceCells,
        IReadOnlyList<(CellAddress Source, CellAddress Destination)> tiledDestinations,
        PasteSpecialOptions options)
    {
        _sheetId = sheetId;
        _sourceRange = default;
        _sourceCells = sourceCells;
        _destination = default;
        _tiledDestinations = tiledDestinations;
        _options = options;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        if (!Enum.IsDefined(_options.Operation))
            return new CommandOutcome(false, "Paste Special operation is not supported.");
        if (_tiledDestinations is null)
        {
            if (_destination.Sheet != _sheetId)
                return new CommandOutcome(false, "Paste destination must be on the target sheet.");
            if (PasteCommandValidator.ValidateInternalPaste(
                    _sheetId,
                    _sourceRange,
                    _sourceCells.Select(c => c.Address),
                    _destination,
                    _options.Transpose) is { } validationError)
            {
                return new CommandOutcome(false, validationError);
            }
        }

        var sheet = ctx.GetSheet(_sheetId);
        var cells = BuildDestinationCells(ctx.Workbook, sheet).ToList();
        if (sheet.IsProtected)
        {
            foreach (var (address, _, _) in cells)
                if (!CommandGuards.CanEditCell(ctx.Workbook, sheet, address))
                    return CommandGuards.RejectSheetProtected();
        }

        _snapshot = [];
        foreach (var (address, cell, sourceAddress) in cells)
        {
            var hadRichTextRuns = sheet.RichTextRuns.TryGetValue(address, out var oldRuns);
            var hadHyperlink = sheet.Hyperlinks.TryGetValue(address, out var oldHyperlink);
            var hadHyperlinkMetadata = sheet.HyperlinkMetadata.TryGetValue(address, out var oldHyperlinkMetadata);
            _snapshot.Add((
                address,
                sheet.GetCell(address)?.Clone(),
                sheet.GetStyleOnly(address.Row, address.Col),
                hadRichTextRuns,
                oldRuns,
                hadHyperlink,
                oldHyperlink,
                hadHyperlinkMetadata,
                oldHyperlinkMetadata));
            sheet.SetCell(address, cell);

            if (_sourceRichTextRuns is not null && _sourceRichTextRuns.TryGetValue(sourceAddress, out var newRuns))
                sheet.RichTextRuns[address] = newRuns;
            else
                sheet.RichTextRuns.Remove(address);

            if (_sourceHyperlinks is not null && _sourceHyperlinks.TryGetValue(sourceAddress, out var newHyperlink))
                sheet.Hyperlinks[address] = newHyperlink;
            else
                sheet.Hyperlinks.Remove(address);

            if (_sourceHyperlinkMetadata is not null && _sourceHyperlinkMetadata.TryGetValue(sourceAddress, out var newHyperlinkMetadata))
                sheet.HyperlinkMetadata[address] = newHyperlinkMetadata;
            else
                sheet.HyperlinkMetadata.Remove(address);
        }

        return new CommandOutcome(true, AffectedCells: cells.Select(c => c.Address).ToList());
    }

    public void Revert(ICommandContext ctx)
    {
        if (_snapshot is null)
            return;

        var sheet = ctx.GetSheet(_sheetId);
        foreach (var (address, oldCell, oldStyleOnly, hadRichTextRuns, oldRichTextRuns, hadHyperlink, oldHyperlink, hadHyperlinkMetadata, oldHyperlinkMetadata) in _snapshot)
        {
            if (oldCell is null)
            {
                sheet.ClearCell(address);
                if (oldStyleOnly.HasValue)
                    sheet.SetStyleOnly(address.Row, address.Col, oldStyleOnly.Value);
                else
                    sheet.ClearStyleOnly(address.Row, address.Col);
            }
            else
            {
                sheet.SetCell(address, oldCell.Clone());
            }

            if (hadRichTextRuns && oldRichTextRuns is not null)
                sheet.RichTextRuns[address] = oldRichTextRuns;
            else
                sheet.RichTextRuns.Remove(address);

            if (hadHyperlink && oldHyperlink is not null)
                sheet.Hyperlinks[address] = oldHyperlink;
            else
                sheet.Hyperlinks.Remove(address);

            if (hadHyperlinkMetadata && oldHyperlinkMetadata is not null)
                sheet.HyperlinkMetadata[address] = oldHyperlinkMetadata;
            else
                sheet.HyperlinkMetadata.Remove(address);
        }
    }

    private IEnumerable<(CellAddress Address, Cell Cell, CellAddress SourceAddress)> BuildDestinationCells(Workbook workbook, Sheet sheet)
    {
        if (_tiledDestinations is not null)
        {
            var sourceLookup = _sourceCells.ToDictionary(c => c.Address, c => c.Cell);
            foreach (var (sourceAddress, destination) in _tiledDestinations)
            {
                if (!sourceLookup.TryGetValue(sourceAddress, out var sourceCell) ||
                    _options.SkipBlanks && IsBlank(sourceCell))
                {
                    continue;
                }

                yield return (destination, BuildCell(workbook, sheet, destination, sourceCell), sourceAddress);
            }

            yield break;
        }

        foreach (var (sourceAddress, sourceCell) in _sourceCells)
        {
            if (_options.SkipBlanks && IsBlank(sourceCell))
                continue;

            var destination = _options.Transpose
                ? PasteCommandCellFactory.TransposeDestination(_sourceRange, sourceAddress, _sheetId, _destination)
                : PasteCommandCellFactory.Shift(
                    sourceAddress,
                    _sheetId,
                    (int)_destination.Row - (int)_sourceRange.Start.Row,
                    (int)_destination.Col - (int)_sourceRange.Start.Col);

            yield return (destination, BuildCell(workbook, sheet, destination, sourceCell), sourceAddress);
        }
    }

    private Cell BuildCell(Workbook workbook, Sheet sheet, CellAddress destination, Cell sourceCell)
    {
        var cell = sourceCell.Clone();
        if (_options.Operation != PasteSpecialOperation.None)
        {
            var existing = sheet.GetCell(destination)?.Clone() ?? Cell.FromValue(BlankValue.Instance);
            existing.StyleId = sheet.GetStyleOnly(destination.Row, destination.Col) ?? existing.StyleId;
            cell = existing;
            cell.Value = ApplyOperation(existing.Value, sourceCell.Value, _options.Operation);
            cell.FormulaText = null;
            if (_options.ContentKind == PasteSpecialContentKind.ValuesAndNumberFormats)
                cell.StyleId = MergeNumberFormat(workbook, existing.StyleId, sourceCell.StyleId);
        }

        return cell;
    }

    private static StyleId MergeNumberFormat(Workbook workbook, StyleId destinationStyleId, StyleId sourceStyleId)
    {
        var style = workbook.GetStyle(destinationStyleId).Clone();
        style.NumberFormat = workbook.GetStyle(sourceStyleId).NumberFormat;
        return workbook.RegisterStyle(style);
    }

    private static bool IsBlank(Cell cell) =>
        cell.FormulaText is null && cell.Value is BlankValue;

    private static ScalarValue ApplyOperation(ScalarValue destination, ScalarValue source, PasteSpecialOperation operation)
    {
        if (!TryNumber(destination, out var left) || !TryNumber(source, out var right))
            return ErrorValue.Value;

        var result = operation switch
        {
            PasteSpecialOperation.Add => left + right,
            PasteSpecialOperation.Subtract => left - right,
            PasteSpecialOperation.Multiply => left * right,
            PasteSpecialOperation.Divide when Math.Abs(right) < 0.000000000001 => double.NaN,
            PasteSpecialOperation.Divide => left / right,
            _ => double.NaN
        };

        if (double.IsNaN(result))
            return operation == PasteSpecialOperation.Divide && Math.Abs(right) < 0.000000000001
                ? ErrorValue.DivByZero
                : source;

        return ShouldPreserveDateValue(destination, source, operation)
            ? new DateTimeValue(result)
            : new NumberValue(result);
    }

    private static bool TryNumber(ScalarValue value, out double number)
    {
        if (value is NumberValue n)
        {
            number = n.Value;
            return true;
        }

        if (value is DateTimeValue dateTime)
        {
            number = dateTime.Value;
            return true;
        }

        if (value is BoolValue boolean)
        {
            number = boolean.Value ? 1 : 0;
            return true;
        }

        if (value is BlankValue)
        {
            number = 0;
            return true;
        }

        number = 0;
        return false;
    }

    private static bool ShouldPreserveDateValue(
        ScalarValue destination,
        ScalarValue source,
        PasteSpecialOperation operation)
    {
        if (destination is not DateTimeValue)
            return false;

        return operation is PasteSpecialOperation.Add or PasteSpecialOperation.Subtract &&
            source is not DateTimeValue;
    }
}
