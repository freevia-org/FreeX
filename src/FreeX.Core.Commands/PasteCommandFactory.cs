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
        bool preserveText = false) =>
        CreateExternalTextPasteCommand(targetSheetId, destinationRange, rows, preserveText, default);

    /// <summary>
    /// Same as the <paramref name="preserveText"/>-only overload, but also honors Paste Special's
    /// Transpose / Skip Blanks / Operation for an EXTERNAL (non-FreeX) clipboard paste. Excel applies
    /// these three options to a plain-text paste exactly as it does to an internal-cells paste; before
    /// this overload existed, the WPF host's external-clipboard fallback silently dropped them
    /// (review P46 — pasting a copied external TSV block with Transpose ticked pasted un-transposed
    /// with no warning).
    /// </summary>
    public static IWorkbookCommand CreateExternalTextPasteCommand(
        SheetId targetSheetId,
        GridRange destinationRange,
        IReadOnlyList<IReadOnlyList<string>> rows,
        bool preserveText,
        PasteSpecialOptions options)
    {
        var destination = destinationRange.Start;
        if (destination.Sheet != targetSheetId)
            return new RejectedWorkbookCommand("Paste", "Paste destination must be on the target sheet.");
        if (destinationRange.End.Sheet != targetSheetId)
            return new RejectedWorkbookCommand("Paste", "Paste destination range must be on the target sheet.");

        var sourceRowCount = (ulong)rows.Count;
        var sourceColCount = 0UL;
        foreach (var row in rows)
            sourceColCount = Math.Max(sourceColCount, (ulong)row.Count);

        // Transpose swaps which source axis (rows vs. columns) tiles against the destination range,
        // matching PasteCommandFactory.CreateInternalPasteCommand's treatment of sourceRange.RowCount
        // vs ColCount under Transpose.
        var pasteRowCount = options.Transpose ? sourceColCount : sourceRowCount;
        var pasteColCount = options.Transpose ? sourceRowCount : sourceColCount;
        var targetRowCount = pasteRowCount == 0 ? 0 : Math.Max(pasteRowCount, destinationRange.RowCount);
        var targetColCount = pasteColCount == 0 ? 0 : Math.Max(pasteColCount, destinationRange.ColCount);

        if (targetRowCount > 0 &&
            targetColCount > 0 &&
            !WorksheetBounds.TryGetRectangleEnd(destination, targetRowCount, targetColCount, out _))
        {
            return new RejectedWorkbookCommand("Paste", "Paste destination range is outside the worksheet bounds.");
        }

        var edits = new List<(CellAddress Address, string Text)>();
        for (var rowOffset = 0UL; rowOffset < targetRowCount; rowOffset++)
        {
            for (var colOffset = 0UL; colOffset < targetColCount; colOffset++)
            {
                // Mod by the SOURCE's own axis counts (sourceRowCount/sourceColCount), matching
                // EnumerateTiledAddresses' `colOffset % sourceRange.RowCount` / `rowOffset % sourceRange.ColCount`.
                // pasteRowCount/pasteColCount are the (possibly transposed) PASTE geometry, not the source's —
                // modding by them here inverted the wrap period for non-square blocks (review R12-clipboard-interop-1).
                var (sourceRowIndex, sourceColIndex) = options.Transpose
                    ? (colOffset % sourceRowCount, rowOffset % sourceColCount)
                    : (rowOffset % sourceRowCount, colOffset % sourceColCount);

                var sourceRow = rows[(int)sourceRowIndex];
                if ((int)sourceColIndex >= sourceRow.Count)
                    continue;

                var text = sourceRow[(int)sourceColIndex];
                if (options.SkipBlanks && text.Length == 0)
                    continue;

                var address = new CellAddress(
                    targetSheetId,
                    destination.Row + (uint)rowOffset,
                    destination.Col + (uint)colOffset);
                edits.Add((address, text));
            }
        }

        if (options.Operation != PasteSpecialOperation.None)
            return new ExternalTextPasteSpecialCommand(targetSheetId, edits, options.Operation);

        return new ExternalTextPasteValuesCommand(targetSheetId, edits, preserveText);
    }

    public static IWorkbookCommand CreateInternalPasteCommand(
        Workbook workbook,
        SheetId targetSheetId,
        GridRange sourceRange,
        IReadOnlyList<(CellAddress Source, Cell Cell)> sourceCells,
        CellAddress destination,
        PasteCellsMode mode,
        PasteSpecialOptions options,
        IReadOnlyList<GridRange>? sourceAreas = null) =>
        CreateInternalPasteCommand(
            workbook,
            targetSheetId,
            sourceRange,
            sourceCells,
            new GridRange(destination, destination),
            mode,
            options,
            sourceAreas);

    // R108-paste-datavalidation-multiarea-1: sourceAreas records every individually Ctrl+clicked
    // area of a multi-area (Ctrl+click) source selection, mirroring InternalClipboard.SourceAreas
    // in MainWindow.ClipboardCommands.cs and the identical parameter already threaded from there
    // to the dedicated Paste-Special-Validation/Format-Painter call sites (WorkbookSession.cs,
    // MainWindow.ClipboardCommands.cs -- "R78-commands-paste-special-5-1/-3/-4: forward
    // clip.SourceAreas"). sourceRange remains only the BOUNDING BOX of those areas, so without
    // this, the r107 plain-Ctrl+V CF/data-validation carry logic below would treat a rule that
    // only overlaps the untouched GAP between disjoint copied areas as "copied" and clone it onto
    // the destination -- exactly the hazard PasteDataValidationCommand's own sourceAreas
    // constructor parameter (R78-commands-paste-special-5-4) exists to prevent, but which this
    // factory had no way to forward before this parameter existed. Callers that don't have a
    // multi-area source (or don't yet forward it) simply pass null/omit it, which preserves the
    // prior single-bounding-box behavior exactly (PasteDataValidationCommand's own constructor
    // treats sourceAreas with 0 or 1 entries as "no areas supplied").
    public static IWorkbookCommand CreateInternalPasteCommand(
        Workbook workbook,
        SheetId targetSheetId,
        GridRange sourceRange,
        IReadOnlyList<(CellAddress Source, Cell Cell)> sourceCells,
        GridRange destinationRange,
        PasteCellsMode mode,
        PasteSpecialOptions options,
        IReadOnlyList<GridRange>? sourceAreas = null,
        // R108-commands-paste-conditional-formats-clear-1: internal-only signal, set true ONLY by
        // this method's own AllMergingConditionalFormats recursive call just below. That branch
        // rewrites options.ContentKind to Default before recursing (so the generic formatting-carry
        // logic further down builds the pasted cell content identically to an ordinary paste), which
        // means by the time execution reaches the CF-carry call sites in the special-options/plain
        // branches, options.ContentKind can no longer distinguish "this was really an ADD-alongside-
        // existing-CF merge action" from "this was an ordinary SUPERSEDE-existing-CF paste" -- both
        // now read Default. This flag survives that rewrite so those two call sites can still pass
        // the correct `merge` value to PasteConditionalFormatsCommand. External callers must never
        // pass true here; the default (false) gives every ordinary paste the correct supersede
        // behavior automatically.
        bool mergeConditionalFormats = false)
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

        var shouldTileDestinationRange = targetRows > pasteRows || targetCols > pasteCols;
        if (shouldTileDestinationRange)
        {
            // "All merging conditional formats" tiles its copied values/formats exactly like every
            // other Paste Special content kind (R25-clipboard-paste-remaining-2); the conditional-format
            // rule itself is still merged once, anchored at the destination's start, matching the
            // non-tiled branch immediately below.
            //
            // R107-paste-conditional-formats-1: CreateTiledInternalPasteCommand's own carriesFormatting
            // gate now carries the CF rule along for every content kind that carries full formatting --
            // including AllMergingConditionalFormats, since ContentKindCarriesRichTextRuns already
            // returns true for it and options.ContentKind is passed through unchanged below. This method
            // must therefore NOT also add its own PasteConditionalFormatsCommand for that content kind
            // (it did prior to R107) -- doing both would paste the rule twice.
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
                options,
                sourceAreas);
        }

        if (options.ContentKind == PasteSpecialContentKind.AllMergingConditionalFormats)
        {
            // R107-paste-conditional-formats-1: delegating to ContentKind=Default now automatically
            // carries the source's conditional-format rules along (see the carriesFormatting /
            // specialCarriesFormatting gates below, which return true for Default). This recursive call
            // used to be wrapped in an extra explicit PasteConditionalFormatsCommand here; keeping that
            // wrapper after R107 would double-add the pasted rule, since the recursive call itself now
            // also adds one.
            //
            // R108-commands-paste-conditional-formats-clear-1: the recursive target must be called
            // with a GridRange destination (not the bare CellAddress local), or overload resolution
            // silently picks the OTHER public CreateInternalPasteCommand overload (the CellAddress-
            // destination one a few lines up, which just forwards to this one with
            // mergeConditionalFormats defaulted back to false) -- discarding mergeConditionalFormats:
            // true below and re-introducing the exact clear-vs-merge bug this fix exists to prevent,
            // just for the "All merging conditional formats" NON-tiled path instead.
            return CreateInternalPasteCommand(
                workbook,
                targetSheetId,
                sourceRange,
                sourceCells,
                new GridRange(destination, destination),
                mode,
                options with { ContentKind = PasteSpecialContentKind.Default },
                sourceAreas,
                mergeConditionalFormats: true);
        }

        if (options.Transpose ||
            options.Operation != PasteSpecialOperation.None ||
            options.SkipBlanks ||
            options.ContentKind != PasteSpecialContentKind.Default)
        {
            // Paste Special "Formats" mode always ignores Operation (like Comments/Validation/
            // ColumnWidths modes below), the same way it ignores it in the tiled branch and the
            // plain-options branch further down -- Formats+Add must copy formatting only, never
            // silently combine values with no format applied (R30-clipboard-paste-special-ops-1).
            if (mode == PasteCellsMode.Formats)
            {
                var specialFormatsCommand = new PasteFormatsCommand(
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

                // A Formats-only paste must carry the source's merged-region structure to the
                // destination just like the tiled path (BuildTiledMergedRegionCommands below) and the
                // mode==All Paste Special path (specialCarriesFormatting a few lines down) already do --
                // otherwise the exact same user action (Paste Special > Formats on a copied merged
                // cell) merges or doesn't merge the destination purely depending on whether the
                // destination happens to be a whole-number tile of the source (R103-paste-formats-merge-1).
                if (options.Operation == PasteSpecialOperation.None &&
                    sourceSheet is not null && sourceSheet.MergedRegions.Any(region => region.Overlaps(sourceRange)))
                {
                    return new CompositeWorkbookCommand(
                        "Paste Special",
                        [specialFormatsCommand, new PasteMergedRegionsCommand(targetSheetId, sourceRange, destination, options.Transpose)]);
                }

                return specialFormatsCommand;
            }

            var specialCells = new List<(CellAddress Source, Cell Cell)>(sourceCells.Count);
            List<(CellAddress Address, StyleId StyleId)>? operationFormatEdits = null;
            foreach (var (source, sourceCell) in sourceCells)
            {
                if (options.SkipBlanks && IsBlank(sourceCell))
                    continue;

                Cell pastedCell;
                if (options.Operation != PasteSpecialOperation.None)
                {
                    pastedCell = Cell.FromValue(sourceCell.Value);
                    // Preserve whether the source cell had a formula. PasteSpecialCellsCommand's own
                    // Skip-Blanks check (BuildDestinationCells) re-applies IsBlank to THIS collapsed
                    // cell; if we dropped FormulaText here, a formula that currently evaluates to
                    // blank/0 (FormulaText non-null, Value=BlankValue) would look truly-empty
                    // downstream and get skipped a second time, silently leaving the destination
                    // unchanged instead of applying the operation with the computed value treated as
                    // 0 (R20-paste-special-operations-2). Excel's Skip Blanks only skips a source cell
                    // with no content at all, not a formula whose result happens to be blank.
                    pastedCell.FormulaText = sourceCell.FormulaText;
                    if (options.ContentKind == PasteSpecialContentKind.ValuesAndNumberFormats)
                        pastedCell.StyleId = sourceCell.StyleId;

                    // "All except borders"/"Values and source formatting"/"Formulas and number
                    // formats" have a dedicated BuildPastedCell branch that inherits source
                    // formatting during a plain paste; PasteSpecialCellsCommand's arithmetic
                    // Operation handling only special-cases ValuesAndNumberFormats, so those other
                    // kinds silently kept no formatting at all once an Operation was picked. Queue a
                    // follow-up format edit here -- mirroring BuildPastedCell's per-kind formatting --
                    // for the destination cells the operation actually touches (R26-paste-special-
                    // operation-deep-3). "All using Source theme" is NOT one of these: it has no
                    // dedicated BuildPastedCell branch and falls through to the same generic path as
                    // Default, so it must be excluded here too, matching Default
                    // (R100-paste-special-all-using-theme-operation-1).
                    var operationDestination = options.Transpose
                        ? PasteCommandCellFactory.TransposeDestination(sourceRange, source, targetSheetId, destination)
                        : PasteCommandCellFactory.Shift(
                            source,
                            targetSheetId,
                            (int)destination.Row - (int)sourceRange.Start.Row,
                            (int)destination.Col - (int)sourceRange.Start.Col);
                    if (TryComputeOperationFormatEdit(
                            workbook,
                            targetSheet,
                            options.ContentKind,
                            options.Operation,
                            sourceCell,
                            operationDestination,
                            out var operationStyleId))
                    {
                        (operationFormatEdits ??= []).Add((operationDestination, operationStyleId));
                    }
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
                    // Transpose swaps each relative reference's own (row,col) offset from the copied
                    // block's anchor onto the destination block's anchor -- it is NOT the uniform
                    // per-cell translation PasteOffsetOp applies (which would just shift every
                    // reference by this host cell's own delta, producing garbage references for
                    // anything other than a self-reference) (R56-commands-paste-special-5-1).
                    RewriteOperation pastedPasteOp = options.Transpose
                        ? new PasteTransposeOp(sourceRange.Start.Row, sourceRange.Start.Col, destination.Row, destination.Col)
                        : new PasteOffsetOp(pastedRowDelta, pastedColDelta);
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

            // Only a mode==All paste ("Paste Special > All"/"All except borders"/etc.) carries the
            // source's rich-text runs, hyperlinks, and merged regions -- a Values-only or
            // Formulas-only paste (mode==Values/Formulas) must strip them just like it strips the
            // source's cell style, matching the identical mode gate the non-special paste path below
            // already applies (ContentKindCarriesRichTextRuns callsite for the plain/no-options case)
            // (R26-paste-special-operation-deep-1).
            var specialCarriesFormatting = mode == PasteCellsMode.All && options.Operation == PasteSpecialOperation.None && ContentKindCarriesRichTextRuns(options.ContentKind);
            var specialRichTextRuns = specialCarriesFormatting ? sourceSheet?.RichTextRuns : null;
            var specialHyperlinks = specialCarriesFormatting ? sourceSheet?.Hyperlinks : null;
            var specialHyperlinkMetadata = specialCarriesFormatting ? sourceSheet?.HyperlinkMetadata : null;
            var specialPhoneticGuides = specialCarriesFormatting ? sourceSheet?.CellPhoneticGuides : null;

            var pasteSpecialCommand = new PasteSpecialCellsCommand(
                targetSheetId,
                sourceRange,
                specialCells,
                destination,
                options,
                specialRichTextRuns,
                specialHyperlinks,
                specialHyperlinkMetadata,
                specialPhoneticGuides);

            var specialExtraCommands = new List<IWorkbookCommand>();
            if (specialCarriesFormatting && sourceSheet is not null && sourceSheet.MergedRegions.Any(region => region.Overlaps(sourceRange)))
                specialExtraCommands.Add(new PasteMergedRegionsCommand(targetSheetId, sourceRange, destination, options.Transpose));
            if (operationFormatEdits is { Count: > 0 })
                specialExtraCommands.Add(new PasteFormatsCommand(targetSheetId, operationFormatEdits));

            // R96-paste-special-floating-objects: a Paste Special (mode All + Transpose/SkipBlanks/a
            // non-Default ContentKind) with a single-cell destination must carry comments and any
            // anchored picture/shape/textbox/chart exactly like a plain Ctrl+V and the tiled-destination
            // branch already do -- Skip Blanks/Transpose only change how the cell grid is filled, not
            // whether comments or floating objects travel with the paste (real Excel ground truth).
            // Previously this branch only ever emitted PasteMergedRegionsCommand/PasteFormatsCommand,
            // silently dropping comments and floating objects whenever any Paste Special option was set.
            if (specialCarriesFormatting)
            {
                var specialFootprint = new GridRange(
                    destination,
                    new CellAddress(targetSheetId, destination.Row + pasteRows - 1, destination.Col + pasteCols - 1));

                // R107-paste-conditional-formats-1: a Paste Special content kind that already carries
                // full formatting (Default/AllUsingSourceTheme/AllExceptBorders/ValuesAndSourceFormatting)
                // must carry the source's conditional-format rules along exactly like it already carries
                // merged regions/comments/pictures/shapes/textboxes/charts just below -- matching real
                // Excel, which brings a cell's CF rule along on any ordinary formatting-carrying paste,
                // not only the dedicated "All merging conditional formats" content kind (which reaches
                // this method's ContentKind==AllMergingConditionalFormats branch above and returns before
                // ever reaching here, so this cannot double-add).
                //
                // R108-commands-paste-conditional-formats-clear-1: an ordinary formatting-carrying
                // paste must SUPERSEDE (clear/shrink) any pre-existing destination CF rule the paste
                // footprint overlaps, matching real Excel and the sibling data-validation carry just
                // below -- PasteConditionalFormatsCommand's merge:false default handles this. The one
                // exception is when this call is itself the recursive continuation of the dedicated
                // "All merging conditional formats" action (mergeConditionalFormats:true, set only by
                // the ContentKind==AllMergingConditionalFormats branch above), which must keep adding
                // alongside existing destination rules instead of superseding them.
                if (sourceSheet is not null && sourceSheet.ConditionalFormats.Any(rule => rule.AllRanges.Any(range => range.Overlaps(sourceRange))))
                {
                    specialExtraCommands.Add(new PasteConditionalFormatsCommand(
                        targetSheetId, sourceRange, destination, options.Transpose, merge: mergeConditionalFormats, sourceAreas: sourceAreas));
                }

                // R107-paste-data-validation-1: a formatting-carrying Paste Special content kind
                // must also carry the source's data-validation rule(s) along, exactly like it
                // already carries conditional-format rules just above -- matching real Excel, which
                // brings a cell's validation rule (e.g. a dropdown list) along on any ordinary
                // formatting-carrying paste, not only the dedicated "Paste Special > Validation"
                // action (PasteSpecialAction.Validation, which never reaches this factory at all --
                // it is wired directly to PasteDataValidationCommand from
                // WorkbookSession.PasteDataValidationFromClipboardAtActiveCell -- so this cannot
                // double-add).
                if (SourceHasOverlappingDataValidation(sourceSheet, sourceRange))
                    specialExtraCommands.Add(new PasteDataValidationCommand(targetSheetId, sourceRange, destination, options.Transpose, sourceAreas));

                if (ShouldCarryComments(sourceSheet, sourceRange, targetSheet, specialFootprint))
                {
                    specialExtraCommands.AddRange(BuildCommentCarryCommands(
                        targetSheetId, sourceRange, destination, specialFootprint, options.Transpose, sourceAreas));
                }

                var picturesToCarry = FindPicturesAnchoredIn(sourceSheet, sourceRange);
                if (picturesToCarry.Count > 0)
                    specialExtraCommands.Add(new PastePicturesCommand(targetSheetId, sourceRange, destination, picturesToCarry, options.Transpose));

                var shapesToCarry = FindShapesAnchoredIn(sourceSheet, sourceRange);
                if (shapesToCarry.Count > 0)
                    specialExtraCommands.Add(new PasteShapesCommand(targetSheetId, sourceRange, destination, shapesToCarry, options.Transpose));

                var textBoxesToCarry = FindTextBoxesAnchoredIn(sourceSheet, sourceRange);
                if (textBoxesToCarry.Count > 0)
                    specialExtraCommands.Add(new PasteTextBoxesCommand(targetSheetId, sourceRange, destination, textBoxesToCarry, options.Transpose));

                var chartsToCarry = FindChartsAnchoredIn(sourceSheet, sourceRange);
                if (chartsToCarry.Count > 0)
                {
                    specialExtraCommands.Add(new PasteChartsCommand(
                        sourceRange.Start.Sheet, targetSheetId, sourceRange, destination, chartsToCarry, options.Transpose));
                }
            }

            return specialExtraCommands.Count == 0
                ? pasteSpecialCommand
                : new CompositeWorkbookCommand("Paste Special", [pasteSpecialCommand, .. specialExtraCommands]);
        }

        var rowDelta = (int)destination.Row - (int)sourceRange.Start.Row;
        var colDelta = (int)destination.Col - (int)sourceRange.Start.Col;
        var pasteOp = new PasteOffsetOp(rowDelta, colDelta);

        if (mode == PasteCellsMode.Formats)
        {
            var plainFormatsCommand = new PasteFormatsCommand(
                targetSheetId,
                sourceCells
                    .Where(c => !options.SkipBlanks || !IsBlank(c.Cell))
                    .Select(c => (PasteCommandCellFactory.Shift(c.Source, targetSheetId, rowDelta, colDelta), c.Cell.StyleId))
                    .ToList());

            // Same merge carry-over as the special-options Formats branch above and the tiled path
            // below -- see R103-paste-formats-merge-1. This is the common case (plain Paste Special >
            // Formats onto a same-size destination), so it is the primary fix for the reported
            // inconsistency: copying a merged cell and pasting Formats-only onto another single cell
            // must merge the destination exactly as the tiled/mode==All paths already do.
            if (options.Operation == PasteSpecialOperation.None &&
                sourceSheet is not null && sourceSheet.MergedRegions.Any(region => region.Overlaps(sourceRange)))
            {
                return new CompositeWorkbookCommand(
                    "Paste",
                    [plainFormatsCommand, new PasteMergedRegionsCommand(targetSheetId, sourceRange, destination, options.Transpose)]);
            }

            return plainFormatsCommand;
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
        var pasteFootprint = new GridRange(
            destination,
            new CellAddress(targetSheetId, destination.Row + pasteRows - 1, destination.Col + pasteCols - 1));
        var extraCommands = new List<IWorkbookCommand>(2);
        if (sourceSheet is not null && sourceSheet.MergedRegions.Any(region => region.Overlaps(sourceRange)))
            extraCommands.Add(new PasteMergedRegionsCommand(targetSheetId, sourceRange, destination, transpose: false));
        if (carriesFormatting && ShouldCarryComments(sourceSheet, sourceRange, targetSheet, pasteFootprint))
            extraCommands.AddRange(BuildCommentCarryCommands(targetSheetId, sourceRange, destination, pasteFootprint, transpose: false, sourceAreas));
        // R91-io-clipboard-image-formats-5-2: a plain Ctrl+V (mode All, no Paste Special options)
        // must bring along any picture anchored inside the copied range, exactly as it brings along
        // the cell values/formats themselves -- matching real Excel.
        // R92-cmd-paste-floating-objects: generalized to every anchored-object kind Excel carries
        // this way, not just pictures -- a Chart, DrawingShape (incl. WordArt), or TextBox anchored
        // inside the copied range must travel with the paste exactly like a Picture does.
        if (carriesFormatting)
        {
            // R107-paste-conditional-formats-1: a plain Ctrl+V (mode All, no Paste Special options)
            // must bring along the source's conditional-format rules exactly as it brings along the
            // merged regions/comments/pictures/shapes/textboxes/charts around it -- see the identical
            // comment on the specialCarriesFormatting branch above for why this can never double-add
            // with the dedicated AllMergingConditionalFormats branch.
            //
            // R108-commands-paste-conditional-formats-clear-1: supersede (merge:false, the default)
            // any pre-existing overlapping destination CF rule for an ordinary plain paste, except
            // when this call is the recursive continuation of the dedicated "All merging conditional
            // formats" action (mergeConditionalFormats:true) -- see the identical reasoning on the
            // specialCarriesFormatting branch above.
            if (sourceSheet is not null && sourceSheet.ConditionalFormats.Any(rule => rule.AllRanges.Any(range => range.Overlaps(sourceRange))))
            {
                extraCommands.Add(new PasteConditionalFormatsCommand(
                    targetSheetId, sourceRange, destination, transpose: false, merge: mergeConditionalFormats, sourceAreas: sourceAreas));
            }

            // R107-paste-data-validation-1: a plain Ctrl+V (mode All, no Paste Special options)
            // must bring along the source's data-validation rule(s) exactly as it brings along the
            // conditional-format rules/merged regions/comments/pictures/shapes/textboxes/charts
            // around it -- see the identical comment on the specialCarriesFormatting branch above
            // for why this can never double-add with the dedicated Paste Special > Validation
            // action.
            if (SourceHasOverlappingDataValidation(sourceSheet, sourceRange))
                extraCommands.Add(new PasteDataValidationCommand(targetSheetId, sourceRange, destination, transpose: false, sourceAreas));

            var picturesToCarry = FindPicturesAnchoredIn(sourceSheet, sourceRange);
            if (picturesToCarry.Count > 0)
                extraCommands.Add(new PastePicturesCommand(targetSheetId, sourceRange, destination, picturesToCarry, transpose: false));

            var shapesToCarry = FindShapesAnchoredIn(sourceSheet, sourceRange);
            if (shapesToCarry.Count > 0)
                extraCommands.Add(new PasteShapesCommand(targetSheetId, sourceRange, destination, shapesToCarry, transpose: false));

            var textBoxesToCarry = FindTextBoxesAnchoredIn(sourceSheet, sourceRange);
            if (textBoxesToCarry.Count > 0)
                extraCommands.Add(new PasteTextBoxesCommand(targetSheetId, sourceRange, destination, textBoxesToCarry, transpose: false));

            var chartsToCarry = FindChartsAnchoredIn(sourceSheet, sourceRange);
            if (chartsToCarry.Count > 0)
            {
                extraCommands.Add(new PasteChartsCommand(
                    sourceRange.Start.Sheet, targetSheetId, sourceRange, destination, chartsToCarry, transpose: false));
            }
        }

        return extraCommands.Count == 0
            ? pasteAllCommand
            : new CompositeWorkbookCommand("Paste", [pasteAllCommand, .. extraCommands]);
    }

    private static bool IsBlank(Cell cell) =>
        cell.FormulaText is null && cell.Value is BlankValue;

    // R107-paste-data-validation-1: a data-validation rule can be anchored purely by an
    // AdditionalRanges entry (AppliesTo elsewhere, or vice versa) -- mirrors
    // PasteDataValidationCommand.EnumerateRuleRanges/ClearOverlappingValidationRanges checking both,
    // and ConditionalFormat.AllRanges's identical treatment just above.
    private static bool SourceHasOverlappingDataValidation(Sheet? sourceSheet, GridRange sourceRange) =>
        sourceSheet is not null && sourceSheet.DataValidations.Any(rule =>
            rule.AppliesTo.Overlaps(sourceRange) || rule.AdditionalRanges.Any(range => range.Overlaps(sourceRange)));

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

    /// <summary>
    /// Computes the destination style edit an arithmetic Paste Special "Operation" (Add/Subtract/
    /// Multiply/Divide) should carry alongside the combined value, for the content kinds whose
    /// non-Operation behavior (see <see cref="PasteCommandCellFactory.BuildPastedCell"/>) inherits
    /// source formatting. PasteSpecialCellsCommand's own Operation handling
    /// (<c>TryBuildCell</c>) only special-cases <see cref="PasteSpecialContentKind.ValuesAndNumberFormats"/>;
    /// the other content kinds here would otherwise silently collapse to plain-value-only formatting
    /// once an Operation was picked (R26-paste-special-operation-deep-3). Returns false (leaving
    /// <paramref name="styleId"/> unset) when the content kind doesn't need a format edit, or when the
    /// arithmetic itself would be a no-op (non-numeric destination, or blank combined with blank) --
    /// matching TryBuildCell's own no-op skip, which leaves the destination's value AND format
    /// entirely untouched in that case.
    ///
    /// <see cref="PasteSpecialContentKind.AllUsingSourceTheme"/> is deliberately NOT in the eligible
    /// list, even though it looks like a sibling of ValuesAndSourceFormatting: with Operation==None,
    /// BuildPastedCell has no dedicated branch for it at all -- it falls through to the exact same
    /// generic "mode==All" path (BuildAllCell) that <see cref="PasteSpecialContentKind.Default"/>
    /// uses (the theme distinction only matters cross-workbook), and Default is intentionally excluded
    /// here (see DefaultContentKind_WithOperation_StaysPlainValueOnly_NoRegression). Same-workbook
    /// "All using Source theme" + an Operation must therefore also leave the destination's own
    /// formatting untouched, matching Default, not get the wholesale source-style overwrite
    /// (R100-paste-special-all-using-theme-operation-1).
    /// </summary>
    private static bool TryComputeOperationFormatEdit(
        Workbook workbook,
        Sheet? targetSheet,
        PasteSpecialContentKind contentKind,
        PasteSpecialOperation operation,
        Cell sourceCell,
        CellAddress destinationAddress,
        out StyleId styleId)
    {
        if (contentKind is not (PasteSpecialContentKind.AllExceptBorders
            or PasteSpecialContentKind.ValuesAndSourceFormatting
            or PasteSpecialContentKind.FormulasAndNumberFormats))
        {
            styleId = default;
            return false;
        }

        var destinationValue = targetSheet?.GetCell(destinationAddress)?.Value ?? BlankValue.Instance;
        if (PasteArithmetic.ApplyOperation(destinationValue, sourceCell.Value, operation, workbook.Uses1904DateSystem) is null)
        {
            styleId = default;
            return false;
        }

        var destinationStyle = PasteCommandCellFactory.GetDestinationStyle(targetSheet, destinationAddress);
        styleId = contentKind switch
        {
            PasteSpecialContentKind.AllExceptBorders => MergeAllExceptBorders(workbook, sourceCell.StyleId, destinationStyle),
            PasteSpecialContentKind.FormulasAndNumberFormats => MergeNumberFormat(workbook, destinationStyle, sourceCell.StyleId),
            // ValuesAndSourceFormatting: BuildPastedCell applies the source's style wholesale (no
            // merge with the destination) when Operation is None; same here.
            _ => sourceCell.StyleId
        };
        return true;
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
        style.BorderDiagonalDown = destinationStyle.BorderDiagonalDown;
        style.BorderDiagonalUp = destinationStyle.BorderDiagonalUp;
        return workbook.RegisterStyle(style);
    }

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
        PasteSpecialOptions options,
        IReadOnlyList<GridRange>? sourceAreas)
    {
        var sourceLookup = sourceCells.ToDictionary(c => c.Source, c => c.Cell);
        // An arithmetic Operation paste only combines the destination cell's numeric value (see
        // PasteSpecialCellsCommand.TryBuildCell) and must leave destination merge structure alone,
        // matching the non-tiled path's identical Operation==None gate on merged-region carry-over
        // a few lines above (R26-paste-special-operation-deep-2).
        var mergedRegionCommands = options.Operation == PasteSpecialOperation.None &&
            sourceSheet is not null && sourceSheet.MergedRegions.Any(region => region.Overlaps(sourceRange))
            ? BuildTiledMergedRegionCommands(targetSheetId, sourceRange, destination, targetRows, targetCols, options.Transpose)
            : null;

        // Same Formats-always-ignores-Operation rule as the non-tiled branch above
        // (R30-clipboard-paste-special-ops-1).
        if (mode == PasteCellsMode.Formats)
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
            List<(CellAddress Address, StyleId StyleId)>? operationFormatEdits = null;
            foreach (var (sourceAddress, destinationAddress) in EnumerateTiledAddresses(
                sourceRange,
                targetSheetId,
                destination,
                targetRows,
                targetCols,
                options.Transpose))
            {
                if (!sourceLookup.TryGetValue(sourceAddress, out var sourceCell))
                    continue;

                tiledPairs.Add((sourceAddress, destinationAddress));

                // See the non-tiled path's identical R26-paste-special-operation-deep-3 handling:
                // these content kinds inherit source formatting even under a tiled arithmetic
                // Operation paste.
                if ((!options.SkipBlanks || !IsBlank(sourceCell)) &&
                    TryComputeOperationFormatEdit(
                        workbook,
                        targetSheet,
                        options.ContentKind,
                        options.Operation,
                        sourceCell,
                        destinationAddress,
                        out var operationStyleId))
                {
                    (operationFormatEdits ??= []).Add((destinationAddress, operationStyleId));
                }
            }

            var specialCommand = new PasteSpecialCellsCommand(targetSheetId, sourceCells, tiledPairs, options);
            var operationExtraCommands = new List<IWorkbookCommand>();
            if (mergedRegionCommands is not null)
                operationExtraCommands.AddRange(mergedRegionCommands);
            if (operationFormatEdits is { Count: > 0 })
                operationExtraCommands.Add(new PasteFormatsCommand(targetSheetId, operationFormatEdits));

            return operationExtraCommands.Count == 0
                ? specialCommand
                : new CompositeWorkbookCommand("Paste Special", [specialCommand, .. operationExtraCommands]);
        }

        var edits = new List<(CellAddress Address, Cell Cell)>((int)Math.Min(int.MaxValue, (long)targetRows * targetCols));
        var carriesFormatting = mode == PasteCellsMode.All && ContentKindCarriesRichTextRuns(options.ContentKind);
        Dictionary<CellAddress, IReadOnlyList<CellTextRun>>? richTextRuns = carriesFormatting ? [] : null;
        Dictionary<CellAddress, string>? hyperlinks = carriesFormatting ? [] : null;
        Dictionary<CellAddress, HyperlinkMetadata>? hyperlinkMetadata = carriesFormatting ? [] : null;
        // A tiled transpose paste (destination an exact multiple of the transposed block's size)
        // replicates the source block once per tile; each replica tile must transpose its formulas
        // against its OWN destination-block origin, not the overall (tile-1) destination start,
        // else every replica beyond the first copies tile-1's rewritten formula verbatim (R57-meta-1).
        // Transposing swaps which source axis maps to which destination tile period.
        var transposeTileRowPeriod = options.Transpose ? sourceRange.ColCount : 0U;
        var transposeTileColPeriod = options.Transpose ? sourceRange.RowCount : 0U;
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
            RewriteOperation pastedPasteOp = options.Transpose
                ? new PasteTransposeOp(
                    sourceRange.Start.Row,
                    sourceRange.Start.Col,
                    destination.Row + (destinationAddress.Row - destination.Row) / transposeTileRowPeriod * transposeTileRowPeriod,
                    destination.Col + (destinationAddress.Col - destination.Col) / transposeTileColPeriod * transposeTileColPeriod)
                : new PasteOffsetOp(pastedRowDelta, pastedColDelta);
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

        var tiledExtraCommands = new List<IWorkbookCommand>();
        if (mergedRegionCommands is not null)
            tiledExtraCommands.AddRange(mergedRegionCommands);

        if (carriesFormatting)
        {
            var tiledFootprint = new GridRange(
                destination,
                new CellAddress(targetSheetId, destination.Row + targetRows - 1, destination.Col + targetCols - 1));

            // R107-paste-conditional-formats-1: tiled counterpart of the non-tiled CF carry above --
            // a plain/formatting-carrying paste onto a multi-cell (tiled) destination must also bring
            // the source's conditional-format rules along. Anchored once at the untiled sourceRange/
            // destination pair (not tiledFootprint), mirroring how the ContentKind==AllMergingConditionalFormats
            // branch in CreateInternalPasteCommand deliberately pastes the CF rule once rather than
            // once per tile (see that branch's comment).
            //
            // R108-commands-paste-conditional-formats-clear-1: this is the ONE call site in this
            // file that can still see ContentKind==AllMergingConditionalFormats directly -- the
            // non-tiled branch above always intercepts that content kind before shouldTileDestinationRange
            // is even checked (see the ContentKind==AllMergingConditionalFormats branch further up,
            // which recurses with ContentKind rewritten to Default), so by the time either non-tiled
            // CF-carry call site is reached ContentKind can never be AllMergingConditionalFormats and
            // both may safely rely on PasteConditionalFormatsCommand's merge:false default (supersede).
            // Here it can, so pass merge explicitly: true only for the dedicated "All merging
            // conditional formats" action (add alongside existing destination rules), false (the
            // default) for every other formatting-carrying content kind tiled onto a larger
            // destination (supersede, matching real Excel's ordinary paste-with-formatting).
            if (sourceSheet is not null && sourceSheet.ConditionalFormats.Any(rule => rule.AllRanges.Any(range => range.Overlaps(sourceRange))))
            {
                tiledExtraCommands.Add(new PasteConditionalFormatsCommand(
                    targetSheetId,
                    sourceRange,
                    destination,
                    options.Transpose,
                    merge: options.ContentKind == PasteSpecialContentKind.AllMergingConditionalFormats,
                    sourceAreas: sourceAreas));
            }

            // R107-paste-data-validation-1: tiled counterpart of the non-tiled data-validation carry
            // above -- a plain/formatting-carrying paste onto a multi-cell (tiled) destination must
            // also bring the source's validation rule(s) along. Anchored once at the untiled
            // sourceRange/destination pair (not tiledFootprint), mirroring how the conditional-format
            // carry just above pastes its rule once rather than once per tile.
            if (SourceHasOverlappingDataValidation(sourceSheet, sourceRange))
                tiledExtraCommands.Add(new PasteDataValidationCommand(targetSheetId, sourceRange, destination, options.Transpose, sourceAreas));

            if (ShouldCarryComments(sourceSheet, sourceRange, targetSheet, tiledFootprint))
            {
                tiledExtraCommands.AddRange(BuildTiledCommentCarryCommands(
                    targetSheetId,
                    sourceRange,
                    destination,
                    targetRows,
                    targetCols,
                    tiledFootprint,
                    options.Transpose,
                    sourceAreas));
            }

            // R91-io-clipboard-image-formats-5-2: tiled counterpart of the non-tiled picture carry
            // above -- a picture anchored inside the copied source range is re-created at every
            // whole repeated tile of the destination selection, mirroring how merged regions and
            // comments are already tiled just above.
            // R92-cmd-paste-floating-objects: tiled counterparts for the other anchored-object kinds,
            // mirroring the non-tiled generalization above.
            var tiledPicturesToCarry = FindPicturesAnchoredIn(sourceSheet, sourceRange);
            if (tiledPicturesToCarry.Count > 0)
            {
                tiledExtraCommands.Add(new PastePicturesCommand(
                    targetSheetId,
                    sourceRange,
                    tiledFootprint,
                    tiledPicturesToCarry,
                    options.Transpose));
            }

            var tiledShapesToCarry = FindShapesAnchoredIn(sourceSheet, sourceRange);
            if (tiledShapesToCarry.Count > 0)
            {
                tiledExtraCommands.Add(new PasteShapesCommand(
                    targetSheetId,
                    sourceRange,
                    tiledFootprint,
                    tiledShapesToCarry,
                    options.Transpose));
            }

            var tiledTextBoxesToCarry = FindTextBoxesAnchoredIn(sourceSheet, sourceRange);
            if (tiledTextBoxesToCarry.Count > 0)
            {
                tiledExtraCommands.Add(new PasteTextBoxesCommand(
                    targetSheetId,
                    sourceRange,
                    tiledFootprint,
                    tiledTextBoxesToCarry,
                    options.Transpose));
            }

            var tiledChartsToCarry = FindChartsAnchoredIn(sourceSheet, sourceRange);
            if (tiledChartsToCarry.Count > 0)
            {
                tiledExtraCommands.Add(new PasteChartsCommand(
                    sourceRange.Start.Sheet,
                    targetSheetId,
                    sourceRange,
                    tiledFootprint,
                    tiledChartsToCarry,
                    options.Transpose));
            }
        }

        return tiledExtraCommands.Count == 0
            ? tiledCommand
            : new CompositeWorkbookCommand(mode == PasteCellsMode.All ? "Paste" : "Paste Special", [tiledCommand, .. tiledExtraCommands]);
    }

    /// <summary>
    /// Builds one <see cref="PasteMergedRegionsCommand"/> per repeated tile of the source range
    /// within the tiled destination, so a merged region in the copied source is recreated at every
    /// tile offset (mirroring the non-tiled paste path's single-offset merge recreation). Each
    /// command uses the same source-range-relative mapping and destination-collision skip as the
    /// non-tiled path; only the per-tile destination anchor differs.
    ///
    /// Only whole tiles that fit entirely within the tiled destination footprint are recreated: when
    /// the destination selection is not an exact multiple of the source range's size, a trailing
    /// partial tile is skipped rather than anchoring a merge that would span past the last selected
    /// row/column (R27-merged-cells-deep-1). Excel never creates a merge that overhangs the pasted
    /// destination; the per-cell value/format tiling in <see cref="EnumerateTiledAddresses"/> is
    /// already bounded to <paramref name="targetRows"/>/<paramref name="targetCols"/> the same way.
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
        for (var rowOffset = 0U; rowOffset + rowPeriod <= targetRows; rowOffset += rowPeriod)
        {
            for (var colOffset = 0U; colOffset + colPeriod <= targetCols; colOffset += colPeriod)
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

    /// <summary>
    /// Whether a plain (mode All, default-options) paste should carry legacy notes/threaded comments
    /// along with the pasted cells, matching Excel: an ordinary Ctrl+V paste brings a copied cell's
    /// comment along and overwrites/clears whatever comment previously sat at the destination, and
    /// comments are only excluded when the user explicitly invokes Paste Special without picking the
    /// dedicated "Comments" option (that path never reaches this code — see the tiled/non-tiled
    /// Paste Special branches earlier in this file).
    /// </summary>
    private static bool ShouldCarryComments(
        Sheet? sourceSheet,
        GridRange sourceRange,
        Sheet? targetSheet,
        GridRange destinationFootprint)
    {
        // Fast path: skip the per-cell range scan entirely when neither sheet has any comments at
        // all, which is the overwhelmingly common case and avoids walking a huge (e.g. full-column)
        // paste footprint just to learn there is nothing to carry or clear.
        if (HasAnyComments(sourceSheet) &&
            sourceRange.AllCells().Any(address =>
                sourceSheet!.Comments.ContainsKey(address) || sourceSheet!.ThreadedComments.ContainsKey(address)))
        {
            return true;
        }

        // Even when the source has no comments, a stale comment left over at the destination must
        // still be cleared so the destination ends up matching the source exactly, like Excel.
        return HasAnyComments(targetSheet) &&
            destinationFootprint.AllCells().Any(address =>
                targetSheet!.Comments.ContainsKey(address) || targetSheet!.ThreadedComments.ContainsKey(address));
    }

    private static bool HasAnyComments(Sheet? sheet) =>
        sheet is not null && (sheet.Comments.Count > 0 || sheet.ThreadedComments.Count > 0);

    /// <summary>
    /// R91-io-clipboard-image-formats-5-2: the pictures whose <see cref="PictureModel.Anchor"/>
    /// falls inside <paramref name="sourceRange"/> -- i.e. the pictures a plain Ctrl+V paste must
    /// carry along with the copied cells/formats, matching real Excel's "copy the object anchored
    /// in the selection" behavior.
    /// </summary>
    private static List<PictureModel> FindPicturesAnchoredIn(Sheet? sheet, GridRange sourceRange) =>
        sheet is null
            ? []
            : sheet.Pictures.Where(picture => sourceRange.Contains(picture.Anchor)).ToList();

    /// <summary>
    /// R92-cmd-paste-floating-objects: DrawingShape (rectangle/arrow/connector/WordArt/etc) analogue
    /// of <see cref="FindPicturesAnchoredIn"/> -- DrawingShapeModel carries the same cell-anchored
    /// <c>Anchor</c> shape as PictureModel, so the same containment check applies unchanged.
    /// </summary>
    private static List<DrawingShapeModel> FindShapesAnchoredIn(Sheet? sheet, GridRange sourceRange) =>
        sheet is null
            ? []
            : sheet.DrawingShapes.Where(shape => sourceRange.Contains(shape.Anchor)).ToList();

    /// <summary>
    /// R92-cmd-paste-floating-objects: TextBox analogue of <see cref="FindPicturesAnchoredIn"/>.
    /// </summary>
    private static List<TextBoxModel> FindTextBoxesAnchoredIn(Sheet? sheet, GridRange sourceRange) =>
        sheet is null
            ? []
            : sheet.TextBoxes.Where(textBox => sourceRange.Contains(textBox.Anchor)).ToList();

    /// <summary>
    /// R92-cmd-paste-floating-objects: Chart analogue of <see cref="FindPicturesAnchoredIn"/>. Unlike
    /// Picture/DrawingShape/TextBox, ChartModel has no cell-anchored <c>Anchor</c> -- its position is
    /// an absolute pixel Left/Top on the sheet's drawing canvas -- so containment is decided via
    /// <see cref="PasteChartsCommand.IsAnchoredIn"/>'s pixel bounding-box check instead of
    /// <c>GridRange.Contains</c>.
    /// </summary>
    private static List<ChartModel> FindChartsAnchoredIn(Sheet? sheet, GridRange sourceRange) =>
        sheet is null
            ? []
            : sheet.Charts.Where(chart => PasteChartsCommand.IsAnchoredIn(sheet, chart, sourceRange)).ToList();

    /// <summary>
    /// Builds the command pair that makes a plain paste's destination comment/note state exactly
    /// mirror the source: first clear every legacy note/threaded comment in the pasted footprint,
    /// then re-apply the source's comments at their mapped destinations. Clearing first (rather than
    /// only overwriting cells that have a source comment) is what makes a destination cell's stale
    /// comment disappear when the corresponding source cell has none, matching Excel's default paste.
    /// </summary>
    private static IEnumerable<IWorkbookCommand> BuildCommentCarryCommands(
        SheetId targetSheetId,
        GridRange sourceRange,
        CellAddress destination,
        GridRange destinationFootprint,
        bool transpose,
        IReadOnlyList<GridRange>? sourceAreas = null)
    {
        yield return new ClearCommentsCommand(targetSheetId, destinationFootprint);
        yield return new PasteCommentsCommand(targetSheetId, sourceRange, destination, transpose, sourceAreas);
    }

    /// <summary>
    /// Tiled-paste counterpart of <see cref="BuildCommentCarryCommands"/>: clears the whole tiled
    /// destination footprint once, then recreates the source's comments at every repeated tile
    /// offset (mirroring <see cref="BuildTiledMergedRegionCommands"/>'s per-tile recreation).
    /// </summary>
    private static IEnumerable<IWorkbookCommand> BuildTiledCommentCarryCommands(
        SheetId targetSheetId,
        GridRange sourceRange,
        CellAddress destination,
        uint targetRows,
        uint targetCols,
        GridRange destinationFootprint,
        bool transpose,
        IReadOnlyList<GridRange>? sourceAreas = null)
    {
        yield return new ClearCommentsCommand(targetSheetId, destinationFootprint);

        var rowPeriod = transpose ? sourceRange.ColCount : sourceRange.RowCount;
        var colPeriod = transpose ? sourceRange.RowCount : sourceRange.ColCount;
        for (var rowOffset = 0U; rowOffset < targetRows; rowOffset += rowPeriod)
        {
            for (var colOffset = 0U; colOffset < targetCols; colOffset += colPeriod)
            {
                var tileDestination = new CellAddress(
                    targetSheetId,
                    destination.Row + rowOffset,
                    destination.Col + colOffset);
                yield return new PasteCommentsCommand(targetSheetId, sourceRange, tileDestination, transpose, sourceAreas);
            }
        }
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

    // Matches a number whose comma grouping is correct: each group to the left of the decimal (or
    // end) is exactly 3 digits, except the first group which may be 1-3 digits. Optional leading
    // sign, optional leading/trailing currency symbol, optional decimal fraction, and optional
    // wrapping parentheses (Excel/accounting negative, e.g. "(1,234.56)"). Based on
    // ExcelTextNumberParser.ValidGroupingRegex (the formula engine's established Excel-parity
    // text-to-number grouping check), extended with parenthesis support so a thousands-grouped
    // accounting negative round-trips correctly instead of being rejected as malformed grouping.
    // Examples that pass: 1,234  $1,234.50  -1,234,567  1,234,567.5  (1,234.56)
    // Examples that fail: 1,2  12,34  1,2345  1,234,5
    private static readonly System.Text.RegularExpressions.Regex ValidGroupingRegex = new(
        @"^\(?[+-]?\$?\d{1,3}(,\d{3})*(\.\d*)?\$?[+-]?\)?$",
        System.Text.RegularExpressions.RegexOptions.None);

    private static readonly System.Globalization.CultureInfo UsCulture =
        System.Globalization.CultureInfo.GetCultureInfo("en-US");

    // Mirrors CellEntryParser.CreateCell's formula recognition (a leading '=' makes a typed value a
    // live formula) for the external-clipboard paste path. Core.Commands cannot reference
    // FreeX.App.Services (App.Services depends on Core.Commands, not the reverse), so this
    // replicates the same rule directly rather than sharing the implementation. Excel's
    // leading-apostrophe text escape takes priority, exactly as it does in ParseClipboardValue
    // above -- pasting "'=1+1" keeps the literal text "=1+1", never a formula.
    internal static bool TryGetPasteFormula(string text, out string formula)
    {
        formula = "";
        if (text.StartsWith('\'') || !text.StartsWith("=", StringComparison.Ordinal))
            return false;

        formula = text[1..];
        return true;
    }

    // Locale-aware counterpart to the en-US-only ValidGroupingRegex/TryParseExcelPasteNumber pair
    // below: validates that <paramref name="text"/> uses <paramref name="culture"/>'s own thousands
    // grouping shape -- per culture.NumberFormat.NumberGroupSizes, not a hardcoded 3 -- before
    // allowing AllowThousands parsing, so a malformed grouping is still rejected as text rather than
    // silently misparsed -- the same Excel-parity precaution TryParseExcelPasteNumber applies for
    // en-US. Most cultures group uniformly by 3 (NumberGroupSizes = {3}), but some (e.g. en-IN/hi-IN
    // Indian numbering) group the innermost 3 digits then repeat groups of 2 further left ({3,2}),
    // e.g. "1,23,456"; validating against a fixed \d{3} per group would wrongly reject that as text.
    private static bool TryParseCultureGroupedNumber(string text, System.Globalization.CultureInfo culture, out double number)
    {
        number = 0;
        var groupSeparator = culture.NumberFormat.NumberGroupSeparator;
        if (string.IsNullOrEmpty(groupSeparator) || !text.Contains(groupSeparator, StringComparison.Ordinal))
            return false;

        var decimalSeparator = culture.NumberFormat.NumberDecimalSeparator;
        var groupPattern = System.Text.RegularExpressions.Regex.Escape(groupSeparator);
        var decimalPattern = System.Text.RegularExpressions.Regex.Escape(decimalSeparator);
        if (!TryBuildCultureGroupingPattern(culture.NumberFormat.NumberGroupSizes, groupPattern, decimalPattern, out var pattern))
            return false;

        var groupingRegex = new System.Text.RegularExpressions.Regex(pattern);
        if (!groupingRegex.IsMatch(text))
            return false;

        const System.Globalization.NumberStyles groupedStyles =
            System.Globalization.NumberStyles.AllowLeadingSign |
            System.Globalization.NumberStyles.AllowTrailingSign |
            System.Globalization.NumberStyles.AllowParentheses |
            System.Globalization.NumberStyles.AllowDecimalPoint |
            System.Globalization.NumberStyles.AllowThousands;

        return double.TryParse(text, groupedStyles, culture, out number) && double.IsFinite(number);
    }

    // Builds the grouping-shape regex from the culture's actual NumberGroupSizes rather than a
    // hardcoded 3. Per NumberFormatInfo.NumberGroupSizes semantics, groupSizes[0] is the size of the
    // group nearest the decimal point, each successive element is the next group leftward, and the
    // LAST element repeats indefinitely for every remaining (more significant) group -- including the
    // partial leftmost group, which may have 1..lastSize digits. E.g. en-US {3} -> every group
    // (including the one next to the decimal) repeats at size 3: "1,234,567". Indian numbering {3,2}
    // -> the group next to the decimal is fixed at size 3, then every group further left repeats at
    // size 2: "1,23,456". A group size of 0 signals "stop grouping" (rare); such cultures are not
    // supported by this shape check and fall through to the other parse paths instead.
    private static bool TryBuildCultureGroupingPattern(
        int[] groupSizes, string groupPattern, string decimalPattern, out string pattern)
    {
        pattern = "";
        if (groupSizes.Length == 0 || Array.Exists(groupSizes, static s => s <= 0))
            return false;

        var lastSize = groupSizes[^1];
        var sb = new System.Text.StringBuilder();
        sb.Append(@"^\(?[+-]?\d{1,").Append(lastSize).Append('}'); // partial leftmost group
        sb.Append('(').Append(groupPattern).Append(@"\d{").Append(lastSize).Append("})*"); // repeating groups
        // Remaining distinct sizes (nearest-decimal first) each occur exactly once, closest to the
        // decimal point last, e.g. for {3,2} this appends the fixed size-3 group next to the decimal.
        for (var i = groupSizes.Length - 2; i >= 0; i--)
        {
            sb.Append(groupPattern).Append(@"\d{").Append(groupSizes[i]).Append('}');
        }

        sb.Append('(').Append(decimalPattern).Append(@"\d*)?[+-]?\)?$");
        pattern = sb.ToString();
        return true;
    }

    // NumberStyles without AllowThousands — used for the first Excel-parity parse attempt so that
    // comma-separated inputs with bad grouping do not silently succeed.
    private const System.Globalization.NumberStyles StylesWithoutThousands =
        System.Globalization.NumberStyles.AllowLeadingSign |
        System.Globalization.NumberStyles.AllowTrailingSign |
        System.Globalization.NumberStyles.AllowParentheses |
        System.Globalization.NumberStyles.AllowDecimalPoint |
        System.Globalization.NumberStyles.AllowExponent |
        System.Globalization.NumberStyles.AllowCurrencySymbol;

    internal static ScalarValue ParseClipboardValue(string text)
    {
        // Excel's text-escape convention: a leading apostrophe forces the pasted field to be kept
        // as text (apostrophe stripped), exactly like typing '123 into a cell. This must be checked
        // before any numeric/boolean coercion below.
        if (text.StartsWith('\''))
            return new TextValue(text[1..]);

        // Culture-safe plain decimal, matching the same first pass CellEntryParser uses for typed
        // entry (NumberStyles.Float against the current culture) so a user whose locale writes
        // decimals with a comma (e.g. "1,5") still gets a number both when typed and when pasted,
        // and thousands-grouped/parenthesized input isn't misread before the dedicated checks below.
        if (double.TryParse(
                text,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.CurrentCulture,
                out var cultureNumber) &&
            double.IsFinite(cultureNumber))
        {
            return new NumberValue(cultureNumber);
        }

        // Locale-aware thousands-grouping parse, matching CellEntryParser's NumberEntryStyles
        // (NumberStyles.Float | AllowThousands against CurrentCulture) so a pasted grouped number
        // in the user's own locale (e.g. de-DE "1.234,56" -> 1234.56, using '.' as the group
        // separator and ',' as the decimal separator) is recognized exactly like typed entry,
        // instead of falling through to the date-candidate check below (which would otherwise
        // misread de-DE's '.' grouping as its own date separator) or ending up as literal text.
        // Grouping shape is validated first (mirroring TryParseExcelPasteNumber's en-US-specific
        // ValidGroupingRegex gate below) so a malformed grouping like "1.23,4" is still rejected.
        if (TryParseCultureGroupedNumber(text, System.Globalization.CultureInfo.CurrentCulture, out var groupedCultureNumber))
        {
            return new NumberValue(groupedCultureNumber);
        }

        // Excel-parity coercion for the accounting/thousands/parenthesized forms Excel recognizes on
        // paste (e.g. "(1,234.56)" -> -1234.56, "1,234" -> 1234, "5-" -> -5), gated by the same
        // grouping-validation shape used elsewhere for Excel text-to-number parity so malformed
        // groupings like "1,2345" are correctly rejected as text rather than silently misparsed.
        if (TryParseExcelPasteNumber(text, out var excelNumber) && double.IsFinite(excelNumber))
        {
            return new NumberValue(excelNumber);
        }

        if (text.Equals("TRUE", StringComparison.OrdinalIgnoreCase) ||
            text.Equals("FALSE", StringComparison.OrdinalIgnoreCase))
        {
            return new BoolValue(text.Equals("TRUE", StringComparison.OrdinalIgnoreCase));
        }

        // Excel auto-converts a percent literal and a recognizable date literal on paste exactly as
        // it would for typed entry (see FreeX.App.Services.CellEntryParser.TryParsePercent /
        // TryParseCurrentCultureDate, which this mirrors); external clipboard paste previously had no
        // equivalent for either, so e.g. "45%" or "6/15/2026" copied from Notepad landed as literal
        // text instead of the number/date Excel would store.
        if (TryParsePastePercent(text, out var percentValue))
        {
            return new NumberValue(percentValue);
        }

        if (TryParsePasteDate(text, out var pasteDate))
        {
            return DateTimeValue.FromDateTime(pasteDate);
        }

        return new TextValue(text);
    }

    /// <summary>Returns true when <paramref name="text"/> would be coerced into something other than
    /// a literal <see cref="TextValue"/> by <see cref="ParseClipboardValue"/> -- i.e. every coercion
    /// branch ParseClipboardValue tries after its leading-apostrophe check (culture float, culture
    /// thousands-grouping, en-US accounting/thousands-grouping, TRUE/FALSE, percent, date). This is
    /// the single source of truth both ParseClipboardValue's own dispatch above and
    /// ClipboardSerializer.RequiresLeadingApostropheEscape build on, so the write-side "does this
    /// Text cell need a protective leading apostrophe before it hits the OS clipboard" decision can
    /// never drift from the read-side coercions that will actually run on the way back in. Before this
    /// was factored out, RequiresLeadingApostropheEscape only mirrored the first three (numeric/
    /// boolean) branches and had no knowledge of the percent/date branches added here, so a
    /// Text-formatted cell whose display text looked like "45%" or "3/4" round-tripped through the
    /// external clipboard silently coerced into a number or date.</summary>
    internal static bool WouldClipboardTextCoerceToNonTextValue(string text)
    {
        if (double.TryParse(
                text,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.CurrentCulture,
                out var cultureNumber) &&
            double.IsFinite(cultureNumber))
        {
            return true;
        }

        if (TryParseCultureGroupedNumber(text, System.Globalization.CultureInfo.CurrentCulture, out _))
            return true;

        if (TryParseExcelPasteNumber(text, out var excelNumber) && double.IsFinite(excelNumber))
            return true;

        if (text.Equals("TRUE", StringComparison.OrdinalIgnoreCase) ||
            text.Equals("FALSE", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (TryParsePastePercent(text, out _))
            return true;

        if (TryParsePasteDate(text, out _))
            return true;

        return false;
    }

    // Trailing '%' (e.g. "45%") -> Excel stores the underlying fraction (0.45), not the literal 45.
    private static bool TryParsePastePercent(string text, out double value)
    {
        value = default;
        if (text.Length < 2 || text[^1] != '%')
            return false;

        if (!double.TryParse(
                text[..^1],
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.CurrentCulture,
                out var number) ||
            !double.IsFinite(number))
        {
            return false;
        }

        value = number / 100d;
        return true;
    }

    // Only attempt a date parse when the text already "looks like" a date (at least two digit
    // groups, plus either a recognized date separator with 3+ groups or a letter, e.g. a month
    // name) - otherwise DateTime.TryParse is lenient enough to misread plain numbers/text (matching
    // CellEntryParser.LooksLikeDateCandidate's same reasoning for typed entry).
    private static bool TryParsePasteDate(string text, out DateTime dateTime)
    {
        dateTime = default;
        if (string.IsNullOrEmpty(System.Globalization.CultureInfo.CurrentCulture.Name) ||
            !LooksLikePasteDateCandidate(text))
        {
            return false;
        }

        return DateTime.TryParse(
                text,
                System.Globalization.CultureInfo.CurrentCulture,
                System.Globalization.DateTimeStyles.NoCurrentDateDefault,
                out dateTime) &&
            dateTime.Date != DateTime.MinValue.Date;
    }

    private static bool LooksLikePasteDateCandidate(string text)
    {
        // '/' and '-' are universally treated by Excel as date separators regardless of locale;
        // '.' only counts when it is the current culture's own actual date separator, otherwise a
        // plain decimal-looking string would be misread as a date instead of staying text.
        var cultureDateSeparator = System.Globalization.CultureInfo.CurrentCulture.DateTimeFormat.DateSeparator;

        var digitGroups = 0;
        var inDigitGroup = false;
        var hasDateSeparator = false;
        var hasLetter = false;

        foreach (var c in text)
        {
            if (char.IsDigit(c))
            {
                if (!inDigitGroup)
                {
                    digitGroups++;
                    inDigitGroup = true;
                }

                continue;
            }

            inDigitGroup = false;
            hasDateSeparator |= c is '/' or '-' ||
                (cultureDateSeparator.Length == 1 && c == cultureDateSeparator[0]);
            hasLetter |= char.IsLetter(c);
        }

        if (digitGroups < 2)
        {
            return false;
        }

        // A '/'/'-'-separated candidate with just 2 digit groups (no explicit year, e.g. "3/4" or
        // "12/25") is exactly Excel's well-known M/D-with-no-year paste/typed-entry behavior --
        // Excel defaults the missing year to the current year rather than requiring one. Only a
        // bare digit-groups-only string with NO separator and no letter (e.g. a lone "3") stays text;
        // digitGroups is already guaranteed >= 2 here, so this simplifies to "has a date separator".
        return hasDateSeparator || hasLetter;
    }

    /// <summary>
    /// Parses accounting/thousands/parenthesized numeric text the way Excel does, mirroring
    /// ExcelTextNumberParser.TryParseNumericStrict's two-phase structure (try without thousands
    /// grouping first, then validate comma-grouping shape before allowing it) without that parser's
    /// date-fallback behavior, which is out of scope for typed/pasted scalar coercion here.
    /// </summary>
    private static bool TryParseExcelPasteNumber(string text, out double number)
    {
        // Fast path: no comma -> no grouping issue, parse without AllowThousands.
        if (!text.Contains(','))
            return double.TryParse(text, StylesWithoutThousands, UsCulture, out number);

        // Has commas: first try without AllowThousands (rejects commas in en-US).
        if (double.TryParse(text, StylesWithoutThousands, UsCulture, out number))
            return true;

        // Commas present and didn't parse without AllowThousands. Validate grouping shape before
        // allowing thousands parsing, so malformed groupings like "1,2345" are rejected as text.
        if (!ValidGroupingRegex.IsMatch(text))
        {
            number = 0;
            return false;
        }

        return double.TryParse(text, System.Globalization.NumberStyles.Any, UsCulture, out number);
    }
}

/// <summary>
/// Pastes external (non-FreeX) clipboard text as plain values, honoring the destination cell's
/// existing Text (@) number format the way Excel does — a cell pre-formatted as Text (the standard
/// technique for protecting zip codes/IDs from losing leading zeros, e.g. pasting "00501") keeps a
/// pasted numeric-looking field as literal text instead of being coerced to a number. The
/// destination's format is only knowable once the sheet is reachable via <see cref="ICommandContext"/>,
/// so (unlike a precomputed <see cref="EditCellsCommand"/>) the actual cell values are resolved inside
/// <see cref="Apply"/> and then handed to a real <see cref="EditCellsCommand"/>, which does the rest
/// of the edit (undo snapshot, table auto-expand, rich text/hyperlink clearing, etc.) unchanged.
/// </summary>
internal sealed class ExternalTextPasteValuesCommand : IWorkbookCommand, IAffectedCellsCommand
{
    private readonly SheetId _sheetId;
    private readonly IReadOnlyList<(CellAddress Address, string Text)> _edits;
    private readonly bool _preserveText;
    private readonly IReadOnlyList<CellAddress> _affectedCells;
    private EditCellsCommand? _inner;

    public string Label => _edits.Count == 1 ? "Edit Cell" : $"Edit {_edits.Count} Cells";

    public IReadOnlyList<CellAddress> AffectedCells => _affectedCells;

    public ExternalTextPasteValuesCommand(
        SheetId sheetId,
        IReadOnlyList<(CellAddress Address, string Text)> edits,
        bool preserveText)
    {
        _sheetId = sheetId;
        _edits = edits;
        _preserveText = preserveText;
        _affectedCells = edits.Select(e => e.Address).ToList();
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        var plainEdits = new List<(CellAddress Address, Cell NewCell)>(_edits.Count);
        foreach (var (address, text) in _edits)
        {
            Cell newCell;
            if (_preserveText || IsDestinationTextFormatted(ctx, sheet, address))
            {
                // A destination pre-formatted as Text (or an explicit paste-as-text request) keeps
                // the pasted field as a literal string exactly as Excel does, even when it looks
                // like a formula -- so the leading-'=' formula check below is skipped entirely.
                newCell = Cell.FromValue(new TextValue(text));
            }
            else if (PasteCommandFactory.TryGetPasteFormula(text, out var formula))
            {
                // Real Excel (and FreeX's own typed cell entry) treats a leading '=' in a pasted
                // plain-text/CSV/HTML field exactly like keyboard entry: the field becomes a live
                // formula, not a literal string (R39-io-external-clipboard-2-1).
                newCell = Cell.FromFormula(formula);
            }
            else
            {
                newCell = Cell.FromValue(PasteCommandFactory.ParseClipboardValue(text));
            }

            plainEdits.Add((address, newCell));
        }

        _inner = new EditCellsCommand(_sheetId, plainEdits);
        return _inner.Apply(ctx);
    }

    public void Revert(ICommandContext ctx) => _inner?.Revert(ctx);

    private static bool IsDestinationTextFormatted(ICommandContext ctx, Sheet sheet, CellAddress address)
    {
        var styleId = sheet.GetCell(address)?.StyleId ??
            sheet.GetStyleOnly(address.Row, address.Col) ??
            StyleId.Default;
        return ctx.Workbook.GetStyle(styleId).NumberFormat == "@";
    }
}

/// <summary>
/// Pastes external (non-FreeX) clipboard text combined with the existing destination cell via
/// Paste Special's Add/Subtract/Multiply/Divide "Operation" — the external-clipboard counterpart of
/// <see cref="PasteSpecialCellsCommand"/>'s Operation handling. Unlike a plain
/// <see cref="EditCellsCommand"/>, the pasted value cannot be precomputed when the command is built:
/// it depends on the CURRENT destination cell value, which is only available inside <see cref="Apply"/>
/// (review P46 — the WPF host's external-clipboard Paste Special fallback silently ignored Operation).
/// </summary>
internal sealed class ExternalTextPasteSpecialCommand : IWorkbookCommand, IAffectedCellsCommand
{
    private readonly SheetId _sheetId;
    private readonly IReadOnlyList<(CellAddress Address, string Text)> _edits;
    private readonly PasteSpecialOperation _operation;
    private readonly IReadOnlyList<CellAddress> _affectedCells;
    private List<(CellAddress Address, Cell? OldCell, StyleId? OldStyleOnly)>? _snapshot;

    public string Label => "Paste Special";

    public IReadOnlyList<CellAddress> AffectedCells => _affectedCells;

    public ExternalTextPasteSpecialCommand(
        SheetId sheetId,
        IReadOnlyList<(CellAddress Address, string Text)> edits,
        PasteSpecialOperation operation)
    {
        _sheetId = sheetId;
        _edits = edits;
        _operation = operation;
        _affectedCells = edits.Select(e => e.Address).ToList();
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        if (!Enum.IsDefined(_operation))
            return new CommandOutcome(false, "Paste Special operation is not supported.");

        var sheet = ctx.GetSheet(_sheetId);
        if (sheet.IsProtected)
        {
            foreach (var (address, _) in _edits)
                if (!CommandGuards.CanEditCell(ctx.Workbook, sheet, address))
                    return CommandGuards.RejectSheetProtected();
        }

        if (CommandGuards.RejectIfSplitsArray(sheet, _edits.Select(e => e.Address)) is { } splitsArrayRejection)
            return splitsArrayRejection;

        _snapshot = [];
        foreach (var (address, text) in _edits)
        {
            _snapshot.Add((address, sheet.GetCell(address)?.Clone(), sheet.GetStyleOnly(address.Row, address.Col)));

            // An arithmetic Operation always needs the pasted text parsed numerically -- forcing it
            // to TextValue here (as the no-Operation values-only paste does for a Text/UnicodeText
            // clipboard source) makes PasteArithmetic.ApplyOperation's TryNumber check fail for every
            // cell, silently skipping the whole paste (e.g. 10 + "5" produced no change instead of 15)
            // (R30-clipboard-paste-special-ops-3).
            var sourceValue = PasteCommandFactory.ParseClipboardValue(text);
            var existing = sheet.GetCell(address)?.Clone() ?? Cell.FromValue(BlankValue.Instance);
            existing.StyleId = sheet.GetStyleOnly(address.Row, address.Col) ?? existing.StyleId;
            var result = PasteArithmetic.ApplyOperation(existing.Value, sourceValue, _operation, ctx.Workbook.Uses1904DateSystem);
            if (result is null)
                continue;

            existing.Value = result;
            existing.FormulaText = null;
            sheet.SetCell(address, existing);
        }

        return new CommandOutcome(true, AffectedCells: _affectedCells);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_snapshot is null)
            return;

        var sheet = ctx.GetSheet(_sheetId);
        foreach (var (address, oldCell, oldStyleOnly) in _snapshot)
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
        }
    }
}
