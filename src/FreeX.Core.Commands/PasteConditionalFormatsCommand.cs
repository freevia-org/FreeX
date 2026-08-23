using FreeX.Core.Formula;
using FreeX.Core.Model;

namespace FreeX.Core.Commands;

public sealed class PasteConditionalFormatsCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly GridRange _sourceRange;
    private readonly CellAddress _destination;
    private readonly bool _transpose;
    private readonly bool _merge;
    private readonly IReadOnlyList<GridRange>? _sourceAreas;
    private List<ConditionalFormat>? _previousRules;

    public string Label => "Paste Conditional Formats";

    // R108-commands-paste-conditional-formats-clear-1: `merge` defaults to false (supersede), which
    // matches real Excel's ordinary paste-with-formatting behavior -- a normal Ctrl+V/Paste Special
    // > All (and Format Painter, which shares this command from FormatPainterCommandFactory) REPLACES
    // whatever conditional formatting already sat on the destination cells, exactly like
    // PasteDataValidationCommand.ClearOverlappingValidationRanges already does for the sibling
    // Data Validation paste (R52-commands-data-validation-apply-3-1/-3-2). Only the dedicated
    // "Paste Special > All merging conditional formats" content kind (PasteSpecialContentKind.
    // AllMergingConditionalFormats) passes merge:true, since that action's entire purpose -- per its
    // own name and Microsoft's documentation -- is to ADD the copied rule alongside whatever the
    // destination already has, never to clear it. Defaulting to false (rather than requiring every
    // call site to opt in) means the fix reaches every existing call site --
    // PasteCommandFactory.cs's plain/tiled/Paste-Special-options CF-carry branches AND
    // FormatPainterCommandFactory.cs's two call sites -- for free; only the one call site that
    // actually implements AllMergingConditionalFormats needs to pass merge:true explicitly.
    // R108-commands-paste-conditional-formats-multiarea-1: `sourceAreas`, when supplied with more
    // than one area, records every individually Ctrl+clicked area of a multi-area source selection
    // (mirroring InternalClipboard.SourceAreas in MainWindow.ClipboardCommands.cs and the identical
    // parameter PasteDataValidationCommand already has -- R78-commands-paste-special-5-4).
    // `sourceRange` remains only the BOUNDING BOX of those areas, so without this, a conditional
    // format rule that only overlaps the gap between disjoint areas (never part of the selection)
    // would still be treated as "copied" and cloned onto the destination.
    public PasteConditionalFormatsCommand(SheetId sheetId, GridRange sourceRange, CellAddress destination, bool transpose, bool merge = false, IReadOnlyList<GridRange>? sourceAreas = null)
    {
        _sheetId = sheetId;
        _sourceRange = sourceRange;
        _destination = destination;
        _transpose = transpose;
        _merge = merge;
        _sourceAreas = sourceAreas is { Count: > 1 } ? sourceAreas : null;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        if (_sourceRange.Start.Sheet != _sourceRange.End.Sheet || _destination.Sheet != _sheetId)
            return new CommandOutcome(false, "Paste conditional formats source range or destination is invalid.");

        var sourceSheet = ctx.GetSheet(_sourceRange.Start.Sheet);
        var targetSheet = ctx.GetSheet(_sheetId);
        if (CommandGuards.RejectIfProtectedWithoutPermission(targetSheet, SheetProtectionPermission.FormatCells) is { } protectedOutcome)
            return protectedOutcome;

        // A rule can be anchored purely by an AdditionalRanges entry (AppliesTo elsewhere, or vice
        // versa -- see ApplyConditionalFormatCommand, which populates AdditionalRanges for any
        // ordinary multi-area/Ctrl+click CF application), so every range the rule covers
        // (rule.AllRanges = AppliesTo + AdditionalRanges) must be checked against the copied source,
        // not just the primary AppliesTo range. Each overlapping fragment becomes its own pasted
        // rule with a fresh AppliesTo and no stale AdditionalRanges copied along, mirroring
        // PasteDataValidationCommand's EnumerateRuleRanges/IntersectWithSource handling of the
        // identical multi-area shape for Data Validation (R78-commands-paste-special-5-4).
        // R108-commands-paste-conditional-formats-multiarea-1: when _sourceAreas records a
        // multi-area (Ctrl+click) source, intersect each of the rule's ranges against every
        // ACTUAL copied area individually (IntersectWithSource) rather than against the whole
        // _sourceRange bounding box, so a rule that only touches the gap between disjoint areas
        // is correctly excluded. Mirrors PasteDataValidationCommand.IntersectWithSource.
        var pastedRules = sourceSheet.ConditionalFormats
            .SelectMany(rule => rule.AllRanges
                .SelectMany(IntersectWithSource)
                .Select(range => CloneRuleForDestination(rule, range, targetSheet.Name)))
            .ToList();

        _previousRules = [.. targetSheet.ConditionalFormats];

        // R108-commands-paste-conditional-formats-clear-1: a real Excel paste only supersedes
        // conditional formatting on the destination cells themselves -- a pre-existing destination
        // rule whose AppliesTo (or AdditionalRanges) merely overlaps the paste footprint must be
        // shrunk to its surviving (non-overlapping) portion(s), not deleted wholesale, or cells
        // outside the paste destination silently lose CF they were never part of pasting over.
        // Mirrors PasteDataValidationCommand.ClearOverlappingValidationRanges. Skipped entirely for
        // the dedicated "All merging conditional formats" action (_merge:true), whose whole point is
        // to add alongside existing destination CF rather than replace them.
        if (!_merge)
        {
            var footprint = GetDestinationFootprint();
            ClearOverlappingConditionalFormatRanges(targetSheet, footprint);
        }

        // Give each pasted rule a fresh slot in the destination sheet's priority sequence instead of
        // trusting the source rule's Priority verbatim (CloneRuleForDestination copies it as-is).
        // Excel's paste-with-formatting never leaves two active rules tied at the same priority number,
        // so renumber the pasted rules to start after whatever priority the destination sheet already
        // holds (computed AFTER the clear/shrink step above, so a rule that was dropped entirely by
        // the clear doesn't keep reserving a priority slot it no longer occupies). This only assigns
        // priorities to the newly pasted rules -- it never rewrites the existing rules already on
        // targetSheet, so it cannot affect ManageConditionalFormatsPlanner.ApplyRuleRange/MoveRule/
        // Reprioritize (the Manage Rules dialog always replaces the whole rule list itself via
        // ReplaceAllConditionalFormatsCommand).
        var nextPriority = targetSheet.ConditionalFormats.Count > 0
            ? targetSheet.ConditionalFormats.Max(f => f.Priority) + 1
            : 1;
        foreach (var pasted in pastedRules)
            pasted.Priority = nextPriority++;

        targetSheet.ConditionalFormats.AddRange(pastedRules);

        return new CommandOutcome(true, AffectedCells: pastedRules.SelectMany(rule => rule.AppliesTo.AllCells()).Distinct().ToList());
    }

    public void Revert(ICommandContext ctx)
    {
        if (_previousRules is null)
            return;

        var targetSheet = ctx.GetSheet(_sheetId);
        targetSheet.ConditionalFormats.Clear();
        targetSheet.ConditionalFormats.AddRange(_previousRules);
        _previousRules = null;
    }

    private ConditionalFormat CloneRuleForDestination(ConditionalFormat source, GridRange ruleRange, string hostSheetName)
    {
        // Clip the rule to the copied source range before mapping. Rules are selected by Overlaps (not
        // Contains), so a rule that starts above/left of the source range would otherwise make
        // MapDestination compute a negative offset that underflows the uint cell coordinate into a
        // multi-billion-row garbage range (hang/OOM in AllCells()). Mirrors PasteDataValidationCommand.
        // ruleRange is whichever of source.AllRanges (AppliesTo or one AdditionalRanges entry)
        // overlapped _sourceRange -- clipping against that specific range (not always AppliesTo)
        // is what lets a rule anchored purely via AdditionalRanges paste correctly.
        var clipped = GridRange.TryIntersect(ruleRange, _sourceRange, out var intersection)
            ? intersection
            : ruleRange;
        var start = MapDestination(clipped.Start);
        var end = MapDestination(clipped.End);

        // A "Formula is" rule is evaluated per-cell by shifting FormulaText relative to the rule's own
        // AppliesTo.Start anchor (ViewportConditionalFormatEvaluator.GetShiftedConditionalFormatFormula),
        // so once the anchor moves to the pasted destination the formula text itself must be rewritten
        // by the same offset — otherwise the shifted-at-evaluation-time formula still points at the
        // original (now unrelated) source cells. Mirrors PasteDataValidationCommand/DataValidationCopySupport,
        // which rewrites Formula1/Formula2 the same way for the identical destination-anchor scenario.
        var rowDelta = (int)start.Row - (int)clipped.Start.Row;
        var colDelta = (int)start.Col - (int)clipped.Start.Col;
        // Transpose swaps each relative reference's own (row,col) offset from the rule's own
        // AppliesTo anchor onto the pasted rule's new anchor -- it is NOT the uniform per-cell
        // translation PasteOffsetOp applies. Mirrors PasteCommandFactory.cs's pastedPasteOp
        // selection for ordinary cell-formula transpose pastes (R56-commands-paste-special-5-1),
        // using the rule's own clipped source anchor / mapped destination anchor as the
        // transpose's source/dest anchors so a rule that only partially overlaps the copied
        // range still transposes relative to its own AppliesTo.Start rather than the whole
        // copied block's corner.
        RewriteOperation pasteOp = _transpose
            ? new PasteTransposeOp(clipped.Start.Row, clipped.Start.Col, start.Row, start.Col)
            : new PasteOffsetOp(rowDelta, colDelta);
        var clone = new ConditionalFormat
        {
            AppliesTo = new GridRange(start, end),
            Priority = source.Priority,
            RuleType = source.RuleType,
            Operator = source.Operator,
            Value1 = source.Value1,
            Value2 = source.Value2,
            FormatIfTrue = source.FormatIfTrue?.Clone(),
            MinColor = source.MinColor,
            MidColor = source.MidColor,
            MaxColor = source.MaxColor,
            MinColorSource = source.MinColorSource,
            MidColorSource = source.MidColorSource,
            MaxColorSource = source.MaxColorSource,
            UseThreeColorScale = source.UseThreeColorScale,
            MinThresholdType = source.MinThresholdType,
            MinThresholdValue = RewriteThresholdValue(source.MinThresholdType, source.MinThresholdValue, hostSheetName, pasteOp),
            MinThresholdGreaterThanOrEqual = source.MinThresholdGreaterThanOrEqual,
            MidThresholdType = source.MidThresholdType,
            MidThresholdValue = RewriteThresholdValue(source.MidThresholdType, source.MidThresholdValue, hostSheetName, pasteOp),
            MidThresholdGreaterThanOrEqual = source.MidThresholdGreaterThanOrEqual,
            MaxThresholdType = source.MaxThresholdType,
            MaxThresholdValue = RewriteThresholdValue(source.MaxThresholdType, source.MaxThresholdValue, hostSheetName, pasteOp),
            MaxThresholdGreaterThanOrEqual = source.MaxThresholdGreaterThanOrEqual,
            DataBarColor = source.DataBarColor,
            DataBarColorSource = source.DataBarColorSource,
            DataBarMinThresholdType = source.DataBarMinThresholdType,
            DataBarMinThresholdValue = RewriteThresholdValue(source.DataBarMinThresholdType, source.DataBarMinThresholdValue, hostSheetName, pasteOp),
            DataBarMaxThresholdType = source.DataBarMaxThresholdType,
            DataBarMaxThresholdValue = RewriteThresholdValue(source.DataBarMaxThresholdType, source.DataBarMaxThresholdValue, hostSheetName, pasteOp),
            DataBarShowValue = source.DataBarShowValue,
            DataBarMinLength = source.DataBarMinLength,
            DataBarMaxLength = source.DataBarMaxLength,
            DataBarGradient = source.DataBarGradient,
            DataBarBorder = source.DataBarBorder,
            DataBarBorderColor = source.DataBarBorderColor,
            DataBarAxisPosition = source.DataBarAxisPosition,
            DataBarAxisColor = source.DataBarAxisColor,
            DataBarNegativeFillColor = source.DataBarNegativeFillColor,
            DataBarNegativeBorderColor = source.DataBarNegativeBorderColor,
            AboveAverage = source.AboveAverage,
            EqualAverage = source.EqualAverage,
            StdDevCount = source.StdDevCount,
            FormulaText = RewriteFormulaText(source.FormulaText, hostSheetName, pasteOp),
            IconSetStyle = source.IconSetStyle,
            IconSetShowValue = source.IconSetShowValue,
            IconSetReverse = source.IconSetReverse,
            TopBottomRank = source.TopBottomRank,
            TopBottomPercent = source.TopBottomPercent,
            TextRuleText = source.TextRuleText,
            DateOccurringPeriod = source.DateOccurringPeriod,
            StopIfTrue = source.StopIfTrue,
            NativeAttributes = source.NativeAttributes,
            NativeChildXmls = ConditionalFormatNativeMetadata.RemoveX14IdNativeChildXmls(source.NativeChildXmls),
            NativePayloadAttributes = source.NativePayloadAttributes,
            NativePayloadChildXmls = source.NativePayloadChildXmls,
            NativeContainerAttributes = source.NativeContainerAttributes,
            NativeContainerChildXmls = source.NativeContainerChildXmls
        };
        // Mirrors RowColumnShiftHelpers.Rules.cs's iconSet-threshold loop: a Formula-type iconSet
        // cfvo threshold holds a relative cell reference just like the colorScale/dataBar thresholds
        // above and must be shifted by the same paste offset. Number/Percent/Percentile thresholds
        // hold literal values and are copied verbatim.
        foreach (var threshold in source.IconSetThresholds)
        {
            clone.IconSetThresholds.Add(threshold.Type == CfThresholdType.Formula
                ? threshold with { Value = RewriteThresholdValue(threshold.Type, threshold.Value, hostSheetName, pasteOp) }
                : threshold);
        }
        clone.IconOverrides.AddRange(source.IconOverrides);
        return clone;
    }

    // Mirrors DataValidationCopySupport.RewriteValidationFormula, minus the leading-'=' handling:
    // unlike DataValidation.Formula1/Formula2, ConditionalFormat.FormulaText is documented as stored
    // "without leading =", so the raw text is handed straight to FormulaRewriter.
    //
    // pasteOp is a PasteTransposeOp (axis-swapping) when the paste is a transpose paste and a
    // PasteOffsetOp (uniform per-cell translation) otherwise -- see the pasteOp selection comment
    // in CloneRuleForDestination, which mirrors PasteCommandFactory.cs's pastedPasteOp switch for
    // ordinary cell-formula transpose pastes (R56-commands-paste-special-5-1).
    private static string? RewriteFormulaText(string? formulaText, string hostSheetName, RewriteOperation pasteOp)
    {
        if (string.IsNullOrWhiteSpace(formulaText))
            return formulaText;
        if (pasteOp is PasteOffsetOp { RowDelta: 0, ColDelta: 0 })
            return formulaText;

        var rewritten = FormulaRewriter.Rewrite(formulaText, pasteOp, hostSheetName);
        return rewritten ?? formulaText;
    }

    // Mirrors RowColumnShiftHelpers.Rules.cs's RewriteThreshold: a colorScale/dataBar cfvo threshold
    // whose ThresholdType is CfThresholdType.Formula holds a relative cell reference (e.g. "B1") that
    // must be shifted/transposed by the same paste operation as the rule's own FormulaText. Non-Formula
    // thresholds (Number/Percent/Percentile/Min/Max) hold literal values and must never be run through
    // the formula rewriter.
    private static string? RewriteThresholdValue(CfThresholdType type, string? value, string hostSheetName, RewriteOperation pasteOp)
    {
        if (type != CfThresholdType.Formula)
            return value;

        return RewriteFormulaText(value, hostSheetName, pasteOp);
    }

    // R108-commands-paste-conditional-formats-multiarea-1: mirrors
    // PasteDataValidationCommand.IntersectWithSource -- with no (or a single) area recorded, this
    // is unchanged from intersecting against the whole bounding box.
    private IEnumerable<GridRange> IntersectWithSource(GridRange ruleRange)
    {
        if (_sourceAreas is not { } areas)
        {
            if (GridRange.TryIntersect(ruleRange, _sourceRange, out var intersection))
                yield return intersection;
            yield break;
        }

        foreach (var area in areas)
        {
            if (GridRange.TryIntersect(ruleRange, area, out var intersection))
                yield return intersection;
        }
    }

    private CellAddress MapDestination(CellAddress source)
    {
        var rowOffset = source.Row - _sourceRange.Start.Row;
        var colOffset = source.Col - _sourceRange.Start.Col;
        return _transpose
            ? new CellAddress(_sheetId, _destination.Row + colOffset, _destination.Col + rowOffset)
            : new CellAddress(_sheetId, _destination.Row + rowOffset, _destination.Col + colOffset);
    }

    // Mirrors PasteDataValidationCommand.GetDestinationRange: the rectangle actually covered by this
    // paste (source range remapped onto the destination anchor, swapping dimensions when transposed).
    private GridRange GetDestinationFootprint()
    {
        var rowCount = _transpose ? _sourceRange.ColCount : _sourceRange.RowCount;
        var colCount = _transpose ? _sourceRange.RowCount : _sourceRange.ColCount;
        return new GridRange(
            _destination,
            new CellAddress(_destination.Sheet, _destination.Row + rowCount - 1, _destination.Col + colCount - 1));
    }

    // R108-commands-paste-conditional-formats-clear-1: mirrors ClearConditionalFormatsCommand.Apply's
    // subtract-and-replace loop (ApplyConditionalFormatCommand.cs) -- checking AppliesTo AND
    // AdditionalRanges for overlap and, for any rule that overlaps, replacing it with a single clone
    // whose AppliesTo/AdditionalRanges cover only the surviving (non-overlapping) remainder, instead
    // of deleting the whole rule just because part of it touches the paste footprint.
    private static void ClearOverlappingConditionalFormatRanges(Sheet sheet, GridRange footprint)
    {
        var newRules = new List<ConditionalFormat>(sheet.ConditionalFormats.Count);
        foreach (var rule in sheet.ConditionalFormats)
        {
            var allRanges = rule.AllRanges.ToArray();
            if (!allRanges.Any(range => range.Overlaps(footprint)))
            {
                newRules.Add(rule);
                continue;
            }

            var remaining = new List<GridRange>();
            foreach (var range in allRanges)
                remaining.AddRange(GridRangeSubtraction.Subtract(range, footprint));

            if (remaining.Count == 0)
                continue; // whole rule range is inside the paste footprint -- drop the rule entirely

            var shrunk = rule.Clone();
            shrunk.AppliesTo = remaining[0];
            shrunk.AdditionalRanges = remaining.Count > 1 ? remaining.Skip(1).ToList() : null;
            newRules.Add(shrunk);
        }

        sheet.ConditionalFormats.Clear();
        sheet.ConditionalFormats.AddRange(newRules);
    }

}

