using FreeX.Core.Formula;
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

public sealed class PasteSpecialCellsCommand : IWorkbookCommand, IEstimatesMemory
{
    // R120-commands-undo-byte-budget-2: mirrors PasteCellsCommand's rationale -- the undo snapshot
    // holds a full Cell clone plus style, rich-text runs, hyperlink/metadata and phonetic guide PER
    // DESTINATION CELL (see BuildDestinationCells/Apply below). A tiled paste-special (the source
    // block repeated to fill a larger selection) can produce far more destination cells than
    // _sourceCells alone, so _tiledDestinations.Count -- known at construction time -- is used when
    // present.
    private const int BytesPerCell = 300;

    private readonly SheetId _sheetId;
    private readonly GridRange _sourceRange;
    private readonly IReadOnlyList<(CellAddress Address, Cell Cell)> _sourceCells;
    private readonly CellAddress _destination;
    private readonly IReadOnlyList<(CellAddress Source, CellAddress Destination)>? _tiledDestinations;
    private readonly PasteSpecialOptions _options;
    private readonly IReadOnlyDictionary<CellAddress, IReadOnlyList<CellTextRun>>? _sourceRichTextRuns;
    private readonly IReadOnlyDictionary<CellAddress, string>? _sourceHyperlinks;
    private readonly IReadOnlyDictionary<CellAddress, HyperlinkMetadata>? _sourceHyperlinkMetadata;
    private readonly IReadOnlyDictionary<CellAddress, CellPhoneticGuide>? _sourcePhoneticGuides;
    private List<(CellAddress Address, Cell? OldCell, StyleId? OldStyleOnly, bool HadRichTextRuns, IReadOnlyList<CellTextRun>? OldRichTextRuns, bool HadHyperlink, string? OldHyperlink, bool HadHyperlinkMetadata, HyperlinkMetadata? OldHyperlinkMetadata, bool HadPhoneticGuide, CellPhoneticGuide? OldPhoneticGuide)>? _snapshot;

    public string Label => "Paste Special";

    /// <inheritdoc/>
    public int EstimatedBytes =>
        (int)Math.Min((long)(_tiledDestinations?.Count ?? _sourceCells.Count) * BytesPerCell, int.MaxValue);

    public PasteSpecialCellsCommand(
        SheetId sheetId,
        GridRange sourceRange,
        IReadOnlyList<(CellAddress Address, Cell Cell)> sourceCells,
        CellAddress destination,
        PasteSpecialOptions options,
        IReadOnlyDictionary<CellAddress, IReadOnlyList<CellTextRun>>? sourceRichTextRuns = null,
        IReadOnlyDictionary<CellAddress, string>? sourceHyperlinks = null,
        IReadOnlyDictionary<CellAddress, HyperlinkMetadata>? sourceHyperlinkMetadata = null,
        IReadOnlyDictionary<CellAddress, CellPhoneticGuide>? sourcePhoneticGuides = null)
    {
        _sheetId = sheetId;
        _sourceRange = sourceRange;
        _sourceCells = sourceCells;
        _destination = destination;
        _options = options;
        _sourceRichTextRuns = sourceRichTextRuns;
        _sourceHyperlinks = sourceHyperlinks;
        _sourceHyperlinkMetadata = sourceHyperlinkMetadata;
        _sourcePhoneticGuides = sourcePhoneticGuides;
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

        if (CommandGuards.RejectIfSplitsArray(sheet, cells.Select(c => c.Address), allowDynamicSpillMemberWrite: true) is { } splitsArrayRejection)
            return splitsArrayRejection;

        _snapshot = [];
        var affected = new List<CellAddress>(cells.Count);
        foreach (var (address, cell, sourceAddress) in cells)
        {
            var hadRichTextRuns = sheet.RichTextRuns.TryGetValue(address, out var oldRuns);
            var hadHyperlink = sheet.Hyperlinks.TryGetValue(address, out var oldHyperlink);
            var hadHyperlinkMetadata = sheet.HyperlinkMetadata.TryGetValue(address, out var oldHyperlinkMetadata);
            var hadPhoneticGuide = sheet.CellPhoneticGuides.TryGetValue(address, out var oldPhoneticGuide);
            _snapshot.Add((
                address,
                sheet.GetCell(address)?.Clone(),
                sheet.GetStyleOnly(address.Row, address.Col),
                hadRichTextRuns,
                oldRuns,
                hadHyperlink,
                oldHyperlink,
                hadHyperlinkMetadata,
                oldHyperlinkMetadata,
                hadPhoneticGuide,
                oldPhoneticGuide));

            // A destination cell that is a non-anchor (hidden/covered) member of an existing merged
            // region must stay empty, matching Excel: only the merge's top-left anchor cell ever
            // carries a value. Writing into a covered cell would silently plant a live value that the
            // grid never displays (the merge only renders the anchor), yet formulas like =SUM or
            // unmerging later would suddenly surface it. So skip the mutation entirely for those cells,
            // same guard as PasteCellsCommand.Apply for plain Ctrl+V paste.
            var mergeRegion = sheet.GetMergeRegion(address);
            if (mergeRegion is { } region && !region.Start.Equals(address))
                continue;

            sheet.SetCell(address, cell);

            // An arithmetic Operation paste only changes the destination cell's numeric value (see
            // TryBuildCell) — it must leave whatever hyperlink/rich-text runs already sat at the
            // destination untouched, not clear them (R16-paste-special-matrix-3). A non-Operation
            // paste continues to replace them with the source's (or clear them, when the source has
            // none), same as before.
            if (_options.Operation == PasteSpecialOperation.None)
            {
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

                if (_sourcePhoneticGuides is not null && _sourcePhoneticGuides.TryGetValue(sourceAddress, out var newPhoneticGuide))
                    sheet.CellPhoneticGuides[address] = newPhoneticGuide;
                else
                    sheet.CellPhoneticGuides.Remove(address);
            }

            affected.Add(address);
        }

        return new CommandOutcome(true, AffectedCells: affected);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_snapshot is null)
            return;

        var sheet = ctx.GetSheet(_sheetId);
        foreach (var (address, oldCell, oldStyleOnly, hadRichTextRuns, oldRichTextRuns, hadHyperlink, oldHyperlink, hadHyperlinkMetadata, oldHyperlinkMetadata, hadPhoneticGuide, oldPhoneticGuide) in _snapshot)
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

            if (hadPhoneticGuide && oldPhoneticGuide is not null)
                sheet.CellPhoneticGuides[address] = oldPhoneticGuide;
            else
                sheet.CellPhoneticGuides.Remove(address);
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

                if (TryBuildCell(workbook, sheet, destination, sourceCell, out var cell))
                    yield return (destination, cell, sourceAddress);
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

            if (TryBuildCell(workbook, sheet, destination, sourceCell, out var cell))
                yield return (destination, cell, sourceAddress);
        }
    }

    /// <summary>
    /// Builds the destination cell for an arithmetic Paste Special operation. Returns false (leaving
    /// <paramref name="cell"/> unset) when the operation is a no-op — e.g. the destination is
    /// non-numeric text (Excel leaves it untouched rather than writing #VALUE!) or both source
    /// and destination are blank (Excel leaves it blank rather than writing a literal 0) — so the
    /// caller skips this cell entirely: no value/style/rich-text/hyperlink change. When either operand
    /// is an error value, the destination is instead updated to that error (errors propagate through
    /// arithmetic, unlike text).
    /// </summary>
    private bool TryBuildCell(Workbook workbook, Sheet sheet, CellAddress destination, Cell sourceCell, out Cell cell)
    {
        if (_options.Operation == PasteSpecialOperation.None)
        {
            cell = sourceCell.Clone();
            return true;
        }

        var existingCell = sheet.GetCell(destination);
        var existing = existingCell?.Clone() ?? Cell.FromValue(BlankValue.Instance);
        // Only fall back to the row/column default style-only lookup when the destination has no
        // real cell of its own -- a real cell's own StyleId always wins, matching the precedence
        // PasteCommandCellFactory.GetDestinationStyle uses (GetCell()?.StyleId ?? GetStyleOnly() ??
        // default). GetStyleOnly falls through to whole-row/whole-column default styles regardless
        // of whether a styled cell exists at this address, so applying it unconditionally here was
        // clobbering an existing cell's own explicit formatting with an unrelated row/column default.
        if (existingCell is null)
            existing.StyleId = sheet.GetStyleOnly(destination.Row, destination.Col) ?? existing.StyleId;
        var result = ApplyOperation(existing.Value, sourceCell.Value, _options.Operation, workbook.Uses1904DateSystem);
        if (result is null)
        {
            cell = existing;
            return false;
        }

        existing.Value = result;
        existing.FormulaText = null;
        if (_options.ContentKind == PasteSpecialContentKind.ValuesAndNumberFormats)
            existing.StyleId = MergeNumberFormat(workbook, existing.StyleId, sourceCell.StyleId);

        cell = existing;
        return true;
    }

    private static StyleId MergeNumberFormat(Workbook workbook, StyleId destinationStyleId, StyleId sourceStyleId)
    {
        var style = workbook.GetStyle(destinationStyleId).Clone();
        style.NumberFormat = workbook.GetStyle(sourceStyleId).NumberFormat;
        return workbook.RegisterStyle(style);
    }

    private static bool IsBlank(Cell cell) =>
        cell.FormulaText is null && cell.Value is BlankValue;

    private static ScalarValue? ApplyOperation(ScalarValue destination, ScalarValue source, PasteSpecialOperation operation, bool uses1904DateSystem) =>
        PasteArithmetic.ApplyOperation(destination, source, operation, uses1904DateSystem);
}

/// <summary>
/// Shared arithmetic for Paste Special's Add/Subtract/Multiply/Divide "Operation", used both by
/// <see cref="PasteSpecialCellsCommand"/> (internal-clipboard paste) and by the external-text paste
/// path (<see cref="ExternalTextPasteSpecialCommand"/>) so pasting a plain-text/TSV clipboard with an
/// Operation selected combines with the destination the same way an internal-cells paste does.
/// </summary>
internal static class PasteArithmetic
{
    /// <summary>
    /// Applies the Paste Special arithmetic operation, matching Excel: a non-numeric, non-blank,
    /// non-error operand (text) leaves the destination cell entirely unchanged rather than producing
    /// a #VALUE! error, and a blank source combined with a blank destination stays blank rather than
    /// materializing a literal 0. Errors, unlike text, are "poison" values that propagate through
    /// arithmetic everywhere else in Excel, so either operand being an error makes the result that
    /// same error (destination/left operand checked first, matching Excel's left-to-right evaluation
    /// order) rather than being treated as a no-op. Returns null to signal "leave the destination
    /// cell unchanged".
    /// </summary>
    public static ScalarValue? ApplyOperation(ScalarValue destination, ScalarValue source, PasteSpecialOperation operation, bool uses1904DateSystem)
    {
        if (destination is ErrorValue destinationError)
            return destinationError;
        if (source is ErrorValue sourceError)
            return sourceError;

        if (!TryNumber(destination, uses1904DateSystem, out var left) || !TryNumber(source, uses1904DateSystem, out var right))
            return null;

        if (destination is BlankValue && source is BlankValue)
            return null;

        var result = operation switch
        {
            PasteSpecialOperation.Add => left + right,
            PasteSpecialOperation.Subtract => left - right,
            PasteSpecialOperation.Multiply => left * right,
            // Only an actual zero divisor is #DIV/0! — a tiny but non-zero divisor (e.g. 1e-15) is a
            // legitimate division that yields a (possibly huge) real quotient, matching Excel
            // (R16-paste-special-matrix-2).
            PasteSpecialOperation.Divide when right == 0 => double.NaN,
            PasteSpecialOperation.Divide => left / right,
            _ => double.NaN
        };

        if (double.IsNaN(result))
            return operation == PasteSpecialOperation.Divide && right == 0
                ? ErrorValue.DivByZero
                : source;

        // An overflowing operation (e.g. 1E200 * 1E200) produces +/-Infinity, which Excel reports as
        // #NUM! rather than a literal "Infinity" value (which would otherwise XLSX-save as text).
        if (double.IsInfinity(result))
            return ErrorValue.Num;

        return ShouldPreserveDateValue(destination, source, operation)
            ? new DateTimeValue(result)
            : new NumberValue(result);
    }

    private static bool TryNumber(ScalarValue value, bool uses1904DateSystem, out double number)
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

        // A numeric-looking TextValue (e.g. a Text-formatted "123", or a plain literal "5") is
        // coerced to its number the same way the rest of the app parses text-as-number (Excel's
        // documented "multiply by 1" text-to-number trick, and the reverse: a numeric text operand
        // combined onto a real number), matching ExcelTextNumberParser's Excel-parity parse used by
        // FindReplaceService's own text-to-number coercion. A non-numeric text still fails here,
        // leaving ApplyOperation's caller to treat the whole operation as a no-op.
        if (value is TextValue text && ExcelTextNumberParser.TryParse(text.Value, out var parsed, uses1904DateSystem))
        {
            number = parsed;
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
