using System.Globalization;
using System.Xml.Linq;
using FreeX.Core.Formula;
using FreeX.Core.Model;

namespace FreeX.Core.Commands;

/// <summary>Command to add a new sheet to the workbook.</summary>
public sealed class AddSheetCommand : IWorkbookCommand, IWholeWorkbookRecalcCommand
{
    private readonly string _name;
    // R84-calc-crosssheet-3d-5-3: null means "append at the end" (the historical, still-default
    // behavior -- matches the sheet-tab '+' button, which always adds after the last sheet, just
    // like real Excel's own New Sheet button). A non-null value inserts BEFORE that workbook
    // position instead, so a sheet inserted from the tab context menu (or the ribbon's Insert
    // Sheet) can land inside an existing 3-D span reference (e.g. Sheet1:Sheet3), matching Excel's
    // "insert before the acted-on sheet" placement.
    private readonly int? _insertIndex;
    private SheetId? _addedSheetId;
    // R83-io-vba-macro-5-1: the codeName assigned below on first Apply, cached and reused on
    // redo (mirrors _addedSheetId's R16 redo-stability fix) so a redone Apply doesn't mint a
    // second, different codeName for what is otherwise the same logical sheet.
    private string? _assignedCodeName;

    public string Label => $"Add Sheet '{_name}'";

    public AddSheetCommand(string name, int? insertIndex = null)
    {
        _name = name;
        _insertIndex = insertIndex;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        if (CommandGuards.RejectIfWorkbookStructureProtected(ctx.Workbook) is { } protectedOutcome)
            return protectedOutcome;

        var validationError = ctx.Workbook.ValidateSheetName(_name);
        if (validationError is not null)
            return new CommandOutcome(false, validationError);

        // Clamp defensively: a redo re-runs this after other commands may have changed the sheet
        // count, and an out-of-range index would otherwise throw from List<T>.Insert.
        var targetIndex = Math.Clamp(_insertIndex ?? ctx.Workbook.Sheets.Count, 0, ctx.Workbook.Sheets.Count);

        Sheet sheet;
        if (_addedSheetId is { } existingId)
        {
            // R16: redo. Workbook.AddSheet always mints a brand-new SheetId, which would give
            // the re-created sheet a DIFFERENT id than the first Apply produced — breaking any
            // later redo-stack command that captured the original id. Re-create with the SAME
            // id captured below instead, via the "reinsert an existing sheet instance" overload.
            sheet = new Sheet(existingId, _name);
            ctx.Workbook.InsertSheet(targetIndex, sheet);
        }
        else
        {
            sheet = ctx.Workbook.InsertSheet(targetIndex, _name);
            _addedSheetId = sheet.Id;
        }
        sheet.ResetViewStateToA1();

        // R83-io-vba-macro-5-1: Workbook.AddSheet/InsertSheet never assign a CodeName (it
        // defaults to null), so a sheet added to a macro-enabled workbook would otherwise be the
        // only worksheet with no sheetPr/@codeName at all -- an inconsistency real Excel's own
        // Insert Sheet never produces once a workbook carries a VBA project. Assign a fresh,
        // workbook-unique codeName here, mirroring DuplicateSheetCommand's codeName regeneration
        // for the same reason (see DuplicateSheetCodeNameGenerator).
        if (ctx.Workbook.HasVbaProjectPackage)
            sheet.CodeName = _assignedCodeName ??= DuplicateSheetCodeNameGenerator.GenerateUniqueCodeName(ctx.Workbook);

        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_addedSheetId.HasValue)
            ctx.Workbook.RemoveSheet(_addedSheetId.Value);
    }
}

/// <summary>
/// R107-drawing-object-hyperlink-sheet-identity: shared helper for RenameSheetCommand/
/// RemoveSheetCommand -- rewrites a drawing object's internal ('Place in This Document')
/// hyperlink target (<see cref="DrawingObjectHyperlink.Target"/>) the same way FormulaRewriter
/// already rewrites a FormControlModel.LinkedCell/ListFillRange bare-ref "formula" for both sheet-
/// identity operations (a <c>RenameSheetOp</c> retargets onto the new name; a <c>DeleteSheetOp</c>
/// converts to #REF!). Mirrors DuplicateSheetDrawingCloner.RewriteSameSheetHyperlinkTarget's R106
/// fix for the Duplicate Sheet path, which already established that this field needs the same
/// treatment as the cell-hyperlink fields (Sheet.Hyperlinks / Sheet.HyperlinkMetadata.Bookmark)
/// rewritten by the O25/P113/R95 blocks below -- but neither Rename nor Delete Sheet touched it
/// until now. An external ("Existing File or Web Page") hyperlink -- TargetMode == "External" --
/// is left completely untouched: only an internal target (TargetMode null) can possibly be a
/// sheet-qualified reference at all.
/// </summary>
file static class DrawingObjectHyperlinkRewriter
{
    public static DrawingObjectHyperlink? Rewrite(DrawingObjectHyperlink? hyperlink, RewriteOperation op, string hostSheetName)
    {
        if (hyperlink is null || hyperlink.TargetMode is not null)
            return hyperlink;

        var rewritten = FormulaRewriter.Rewrite(hyperlink.Target, op, hostSheetName);
        return rewritten is null || rewritten == hyperlink.Target ? hyperlink : hyperlink with { Target = rewritten };
    }
}

/// <summary>Command to rename a sheet.</summary>
// R107: renaming a sheet can change which sheets fall inside a 3-D span reference (e.g.
// =SUM(Sheet1:Sheet3!A1)) purely by the name change, not by editing any cell of its own, so
// Apply reports no AffectedCells. On the forward path this gap is covered by an explicit
// RecalculateWorkbook() call in WorkbookSession.RenameActiveSheet, but CommandBus.Undo/Redo
// call straight into the command bus and never reach that wrapper -- implementing this marker
// (like AddSheetCommand/RemoveSheetCommand/MoveSheetCommand/DuplicateSheetCommand already do)
// is what makes Undo/Redo of a rename force a full recalc too. See IWholeWorkbookRecalcCommand's
// own doc comment, which already named "Rename Sheet" as one of the five operations needing
// this marker before it was actually added here.
public sealed class RenameSheetCommand : IWorkbookCommand, IWholeWorkbookRecalcCommand
{
    private readonly SheetId _sheetId;
    private readonly string _newName;
    private string? _oldName;
    private readonly Dictionary<CellAddress, string> _formulaSnapshot = [];
    private Dictionary<string, string>? _namedFormulaSnapshot;
    private Dictionary<(string Name, SheetId Sheet), string>? _scopedNamedFormulaSnapshot;
    // T6: string sheet-name refs on model objects
    private List<(PivotCacheModel Cache, string OldValue)>? _pivotCacheNameSnapshot;
    private List<(ChartModel Chart, string OldValue)>? _chartPivotSourceNameSnapshot;
    private List<(SlicerModel Slicer, string OldValue)>? _slicerNameSnapshot;
    // P84: TimelineModel.SourceSheetName is the timeline-object twin of SlicerModel.SourceSheetName
    // above (same "which sheet hosts this object's anchor" role) and must be rewritten on rename
    // the same way, or SlicerTimelinePanePlanner.IsAnchoredOnSheet keeps comparing against the
    // stale old sheet name and the timeline silently stops rendering.
    private List<(TimelineModel Timeline, string OldValue)>? _timelineNameSnapshot;
    private List<(PictureModel Picture, string OldValue)>? _pictureNameSnapshot;
    // P81: FormControlModel.LinkedCell/ListFillRange hold sheet-qualified string refs (e.g.
    // "Sheet1!$D$3", Excel's fmlaLink) just like the string refs above and must be rewritten
    // the same way on rename, or a loaded checkbox/list-box's linked cell/fill range goes stale
    // (FormControlInteractionService.TryResolveLinkedCell then fails to resolve the sheet).
    private List<(FormControlModel Control, string? OldLinkedCell, string? OldListFillRange)>? _formControlNameSnapshot;
    // T7: CF/DV formula rewrites across ALL sheets for the rename
    private List<(Guid RuleId, string? OldValue, SheetId Sheet)>? _cfFormulaRenameSnapshot;
    // R102: colorScale/dataBar/iconSet cfvo threshold values whose ThresholdType is
    // CfThresholdType.Formula (e.g. a Color Scale Formula-type minimum of "=Sheet2!$B$1") --
    // mirrors _cfFormulaRenameSnapshot but for the six threshold slots RewriteRuleFormulas
    // tracks (see RowColumnShiftHelpers.Rules.cs Slot* constants), which the pre-existing hand-
    // rolled cf.FormulaText-only pass never touched, unlike RenameStructuredTableCommand's R100
    // rewrite of the same rules via the shared RewriteRuleFormulas/RestoreRuleFormulas helper.
    private List<(Guid RuleId, int Slot, string? OldValue, SheetId Sheet)>? _cfThresholdRenameSnapshot;
    private List<(Guid RuleId, int Slot, string? OldValue, SheetId Sheet)>? _dvFormulaRenameSnapshot;
    // K16: chart verbatim series/data-label formulas (multi-area unions and "value from
    // cells" data labels) hold sheet-qualified text refs just like CF/DV formulas above and
    // must be rewritten the same way, or they keep saying the OLD sheet name after rename.
    private List<RowColumnShiftHelpers.ChartVerbatimWorkbookSnapshot>? _chartVerbatimRenameSnapshot;
    // O25: 'Place in This Document' hyperlink bookmarks across ALL sheets (not just the
    // renamed one — a hyperlink on Sheet1 can target "Sheet2!A1") must be rewritten to the
    // new sheet name, or the link silently breaks (stale sheet name after rename).
    private List<(SheetId Sheet, CellAddress Address, string OldBookmark)>? _hyperlinkBookmarkRenameSnapshot;
    // P113: when a 'Place in This Document' hyperlink has no Bookmark set (FreeX's own Insert
    // Hyperlink dialog stores the target ref directly on sheet.Hyperlinks and leaves Bookmark
    // empty — Bookmark is only populated via the separate Bookmark picker), the sheet-qualified
    // ref lives in sheet.Hyperlinks[addr] instead and must be rewritten there, or the link goes
    // stale (HyperlinkNavigationPlanner/CreateXlsxHyperlink both fall back to that raw target
    // whenever Bookmark is empty).
    private List<(SheetId Sheet, CellAddress Address, string OldTarget)>? _hyperlinkTargetRenameSnapshot;
    // R107-drawing-object-hyperlink-sheet-identity: DrawingShapeModel/TextBoxModel/PictureModel/
    // ChartModel.Hyperlink carries the same kind of 'Place in This Document' sheet-qualified
    // reference as the O25/P113 cell-hyperlink fields above (verbatim, per DrawingObjectHyperlink's
    // own doc comment) but was never rewritten on rename -- see DrawingObjectHyperlinkRewriter.
    private List<(DrawingShapeModel Shape, DrawingObjectHyperlink? OldValue)>? _drawingShapeHyperlinkRenameSnapshot;
    private List<(TextBoxModel TextBox, DrawingObjectHyperlink? OldValue)>? _textBoxHyperlinkRenameSnapshot;
    private List<(PictureModel Picture, DrawingObjectHyperlink? OldValue)>? _pictureHyperlinkRenameSnapshot;
    private List<(ChartModel Chart, DrawingObjectHyperlink? OldValue)>? _chartHyperlinkRenameSnapshot;

    public string Label => $"Rename Sheet to '{_newName}'";

    public RenameSheetCommand(SheetId sheetId, string newName)
    {
        _sheetId = sheetId;
        _newName = newName;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        if (CommandGuards.RejectIfWorkbookStructureProtected(ctx.Workbook) is { } protectedOutcome)
            return protectedOutcome;

        var sheet = ctx.GetSheet(_sheetId);
        // R139-workbook-protection: a sheet protected individually (Protect Sheet, no workbook
        // structure protection) must refuse renaming its own tab, mirroring the tab-context-menu
        // gate real Excel applies -- previously this command only checked workbook-level structure
        // protection, so an individually-protected sheet could be silently renamed.
        if (CommandGuards.RejectIfProtected(sheet) is { } sheetProtectedOutcome)
            return sheetProtectedOutcome;

        var validationError = ctx.Workbook.ValidateSheetName(_newName, _sheetId);
        if (validationError is not null)
            return new CommandOutcome(false, validationError);

        _oldName = sheet.Name;
        sheet.Name = _newName;
        _formulaSnapshot.Clear();
        RowColumnShiftHelpers.RewriteAllFormulas(
            ctx.Workbook, new RenameSheetOp(_oldName, _newName), _formulaSnapshot);
        _namedFormulaSnapshot = [];
        _scopedNamedFormulaSnapshot = [];
        RowColumnShiftHelpers.RewriteNamedFormulas(
            ctx.Workbook, new RenameSheetOp(_oldName, _newName), _namedFormulaSnapshot, _scopedNamedFormulaSnapshot);

        // T6: update string sheet-name refs on model objects
        _pivotCacheNameSnapshot = [];
        foreach (var cache in ctx.Workbook.PivotCaches)
        {
            if (cache.SourceSheetName is not null &&
                string.Equals(cache.SourceSheetName, _oldName, StringComparison.OrdinalIgnoreCase))
            {
                _pivotCacheNameSnapshot.Add((cache, cache.SourceSheetName));
                cache.SourceSheetName = _newName;
            }
        }

        _chartPivotSourceNameSnapshot = [];
        foreach (var s in ctx.Workbook.Sheets)
        {
            foreach (var chart in s.Charts)
            {
                if (chart.PivotSourceSheetName is not null &&
                    string.Equals(chart.PivotSourceSheetName, _oldName, StringComparison.OrdinalIgnoreCase))
                {
                    _chartPivotSourceNameSnapshot.Add((chart, chart.PivotSourceSheetName));
                    chart.PivotSourceSheetName = _newName;
                }
            }
        }

        _slicerNameSnapshot = [];
        foreach (var slicer in ctx.Workbook.Slicers)
        {
            if (slicer.SourceSheetName is not null &&
                string.Equals(slicer.SourceSheetName, _oldName, StringComparison.OrdinalIgnoreCase))
            {
                _slicerNameSnapshot.Add((slicer, slicer.SourceSheetName));
                slicer.SourceSheetName = _newName;
            }
        }

        _timelineNameSnapshot = [];
        foreach (var timeline in ctx.Workbook.Timelines)
        {
            if (timeline.SourceSheetName is not null &&
                string.Equals(timeline.SourceSheetName, _oldName, StringComparison.OrdinalIgnoreCase))
            {
                _timelineNameSnapshot.Add((timeline, timeline.SourceSheetName));
                timeline.SourceSheetName = _newName;
            }
        }

        _pictureNameSnapshot = [];
        foreach (var s in ctx.Workbook.Sheets)
        {
            foreach (var pic in s.Pictures)
            {
                if (pic.LinkedSourceSheetName is not null &&
                    string.Equals(pic.LinkedSourceSheetName, _oldName, StringComparison.OrdinalIgnoreCase))
                {
                    _pictureNameSnapshot.Add((pic, pic.LinkedSourceSheetName));
                    pic.LinkedSourceSheetName = _newName;
                }
            }
        }

        // T7: rewrite CF FormulaText and DV Formula1/Formula2 across all sheets with RenameSheetOp
        var renameOp = new RenameSheetOp(_oldName, _newName);

        // P81: rewrite FormControlModel.LinkedCell/ListFillRange across ALL sheets — a control
        // on any sheet can hold a cross-sheet ref (e.g. a checkbox on Sheet2 linked to
        // "Sheet1!$D$3"), mirroring the CF/DV pass below via the same FormulaRewriter path
        // (both are bare single-ref "formulas", not full '='-prefixed expressions).
        _formControlNameSnapshot = [];
        foreach (var s in ctx.Workbook.Sheets)
        {
            foreach (var control in s.FormControls)
            {
                string? newLinkedCell = control.LinkedCell;
                string? newListFillRange = control.ListFillRange;
                bool changed = false;

                if (control.LinkedCell is { } linkedCell)
                {
                    var rewritten = FormulaRewriter.Rewrite(linkedCell, renameOp, s.Name);
                    if (rewritten is not null && rewritten != linkedCell)
                    {
                        newLinkedCell = rewritten;
                        changed = true;
                    }
                }

                if (control.ListFillRange is { } listFillRange)
                {
                    var rewritten = FormulaRewriter.Rewrite(listFillRange, renameOp, s.Name);
                    if (rewritten is not null && rewritten != listFillRange)
                    {
                        newListFillRange = rewritten;
                        changed = true;
                    }
                }

                if (!changed)
                    continue;

                _formControlNameSnapshot.Add((control, control.LinkedCell, control.ListFillRange));
                control.LinkedCell = newLinkedCell;
                control.ListFillRange = newListFillRange;
            }
        }

        // R102: use the shared RewriteRuleFormulas helper (the same primitive
        // RenameStructuredTableCommand's R100 fix uses) instead of a hand-rolled loop, so a
        // colorScale/dataBar/iconSet Formula-type threshold (MinThresholdValue/MidThresholdValue/
        // MaxThresholdValue/DataBarMinThresholdValue/DataBarMaxThresholdValue/IconSetThresholds[i]
        // .Value) referencing the renamed sheet (e.g. "=Sheet2!$B$1") gets rewritten too, not just
        // cf.FormulaText and dv.Formula1/Formula2.
        _cfFormulaRenameSnapshot = [];
        _cfThresholdRenameSnapshot = [];
        _dvFormulaRenameSnapshot = [];
        foreach (var s in ctx.Workbook.Sheets)
        {
            var cfSnap = new Dictionary<Guid, string?>();
            var cfThresholdSnap = new Dictionary<(Guid Id, int Slot), string?>();
            var dvSnap = new Dictionary<(Guid Id, int Slot), string?>();
            RowColumnShiftHelpers.RewriteRuleFormulas(s, renameOp, cfSnap, cfThresholdSnap, dvSnap);

            foreach (var (ruleId, oldValue) in cfSnap)
                _cfFormulaRenameSnapshot.Add((ruleId, oldValue, s.Id));
            foreach (var (key, oldValue) in cfThresholdSnap)
                _cfThresholdRenameSnapshot.Add((key.Id, key.Slot, oldValue, s.Id));
            foreach (var (key, oldValue) in dvSnap)
                _dvFormulaRenameSnapshot.Add((key.Id, key.Slot, oldValue, s.Id));

            // The CF viewport context cache is keyed on (sheet.Id, sheet.ContentVersion,
            // sheet.ConditionalFormats.Version) and caches a precompiled AST per CF object
            // reference, so mutating cf.FormulaText/threshold values in place above never
            // invalidates it — bump Version explicitly so a stale cache hit doesn't keep
            // evaluating the old sheet name after the rename.
            if (cfSnap.Count > 0 || cfThresholdSnap.Count > 0)
                s.ConditionalFormats.NotifyRulesChanged();
        }

        // K16: rewrite chart verbatim series/data-label formulas across ALL sheets so any
        // chart (same-sheet or cross-sheet) whose text refs name the renamed sheet keep
        // pointing at it under its new name — mirrors the T7 CF/DV pass above.
        _chartVerbatimRenameSnapshot = RowColumnShiftHelpers.CaptureChartVerbatimFormulas(ctx.Workbook);
        RowColumnShiftHelpers.RewriteChartVerbatimFormulas(ctx.Workbook, renameOp);

        // O25/P113: rewrite 'Place in This Document' hyperlink bookmarks/targets across ALL
        // sheets that reference the renamed sheet — a hyperlink lives on whichever sheet it was
        // inserted on, which may differ from the sheet being renamed. When Bookmark is empty
        // (FreeX's own Insert Hyperlink dialog stores the ref straight into sheet.Hyperlinks and
        // leaves Bookmark unset unless the separate Bookmark picker was used), fall back to
        // rewriting sheet.Hyperlinks[addr] instead — that's the string every consumer
        // (HyperlinkNavigationPlanner, CreateXlsxHyperlink) actually reads when Bookmark is empty.
        _hyperlinkBookmarkRenameSnapshot = [];
        _hyperlinkTargetRenameSnapshot = [];
        foreach (var s in ctx.Workbook.Sheets)
        {
            List<KeyValuePair<CellAddress, HyperlinkMetadata>>? changed = null;
            List<KeyValuePair<CellAddress, string>>? targetChanged = null;
            foreach (var pair in s.HyperlinkMetadata)
            {
                var meta = pair.Value;
                if (meta.LinkType != HyperlinkTargetKind.PlaceInThisDocument)
                    continue;

                var bookmark = meta.Bookmark;
                if (string.IsNullOrEmpty(bookmark))
                {
                    // No bookmark recorded — the sheet-qualified ref lives directly on
                    // sheet.Hyperlinks[addr] instead (see SetHyperlinkCommand).
                    if (!s.Hyperlinks.TryGetValue(pair.Key, out var target) || string.IsNullOrEmpty(target))
                        continue;

                    var tBangIndex = target.IndexOf('!', StringComparison.Ordinal);
                    if (tBangIndex < 0)
                        continue;

                    var tRawSheetPart = target[..tBangIndex].Trim('\'');
                    var tSheetPart = tRawSheetPart.Contains("''", StringComparison.Ordinal)
                        ? tRawSheetPart.Replace("''", "'", StringComparison.Ordinal)
                        : tRawSheetPart;
                    if (!string.Equals(tSheetPart, _oldName, StringComparison.OrdinalIgnoreCase))
                        continue;

                    var rewrittenTarget = FormulaRewriter.Rewrite(target, renameOp, _oldName);
                    if (rewrittenTarget is null || rewrittenTarget == target)
                        continue;

                    (targetChanged ??= []).Add(new KeyValuePair<CellAddress, string>(pair.Key, rewrittenTarget));
                    continue;
                }

                var bangIndex = bookmark.IndexOf('!', StringComparison.Ordinal);
                if (bangIndex < 0)
                    continue;

                // O27: unescape doubled single-quotes ('' -> ') in a quoted sheet name (Excel's
                // escaping for an embedded apostrophe, e.g. 'Bob''s Sheet'!A1) before comparing
                // against _oldName, or a sheet name containing an apostrophe never matches here.
                var rawSheetPart = bookmark[..bangIndex].Trim('\'');
                var sheetPart = rawSheetPart.Contains("''", StringComparison.Ordinal)
                    ? rawSheetPart.Replace("''", "'", StringComparison.Ordinal)
                    : rawSheetPart;
                if (!string.Equals(sheetPart, _oldName, StringComparison.OrdinalIgnoreCase))
                    continue;

                var rewritten = FormulaRewriter.Rewrite(bookmark, renameOp, _oldName);
                if (rewritten is null || rewritten == bookmark)
                    continue;

                (changed ??= []).Add(new KeyValuePair<CellAddress, HyperlinkMetadata>(pair.Key, meta with { Bookmark = rewritten }));
            }

            if (changed is not null)
            {
                foreach (var (addr, newMeta) in changed)
                {
                    _hyperlinkBookmarkRenameSnapshot.Add((s.Id, addr, s.HyperlinkMetadata[addr].Bookmark));
                    s.HyperlinkMetadata[addr] = newMeta;
                }
            }

            if (targetChanged is not null)
            {
                foreach (var (addr, newTarget) in targetChanged)
                {
                    _hyperlinkTargetRenameSnapshot.Add((s.Id, addr, s.Hyperlinks[addr]));
                    s.Hyperlinks[addr] = newTarget;
                }
            }
        }

        // R107-drawing-object-hyperlink-sheet-identity: rewrite drawing-object 'Place in This
        // Document' hyperlinks across ALL sheets the same way the O25/P113 cell-hyperlink pass
        // above does -- a shape/text box/picture/chart's Hyperlink.Target can reference the
        // renamed sheet too (e.g. "Sheet2!A1"), and DuplicateSheetDrawingCloner already proves the
        // codebase treats this as the same class of reference for the Duplicate Sheet path.
        _drawingShapeHyperlinkRenameSnapshot = [];
        _textBoxHyperlinkRenameSnapshot = [];
        _pictureHyperlinkRenameSnapshot = [];
        _chartHyperlinkRenameSnapshot = [];
        foreach (var s in ctx.Workbook.Sheets)
        {
            foreach (var shape in s.DrawingShapes)
            {
                // R108-drawing-object-hyperlink-patch-safety-guard-allowlist: named distinctively
                // (not a bare "rewritten") so the R101 patch-safety guard's RHS allowlist regex
                // can target this exact call shape narrowly -- see
                // AllowedDrawingObjectHyperlinkRewriterRhs in R101_DrawingChartHyperlinkPatchSafetyGuardTests.
                var rewrittenDrawingObjectHyperlink = DrawingObjectHyperlinkRewriter.Rewrite(shape.Hyperlink, renameOp, s.Name);
                if (rewrittenDrawingObjectHyperlink != shape.Hyperlink)
                {
                    _drawingShapeHyperlinkRenameSnapshot.Add((shape, shape.Hyperlink));
                    shape.Hyperlink = rewrittenDrawingObjectHyperlink;
                }
            }

            foreach (var textBox in s.TextBoxes)
            {
                var rewrittenDrawingObjectHyperlink = DrawingObjectHyperlinkRewriter.Rewrite(textBox.Hyperlink, renameOp, s.Name);
                if (rewrittenDrawingObjectHyperlink != textBox.Hyperlink)
                {
                    _textBoxHyperlinkRenameSnapshot.Add((textBox, textBox.Hyperlink));
                    textBox.Hyperlink = rewrittenDrawingObjectHyperlink;
                }
            }

            foreach (var pic in s.Pictures)
            {
                var rewrittenDrawingObjectHyperlink = DrawingObjectHyperlinkRewriter.Rewrite(pic.Hyperlink, renameOp, s.Name);
                if (rewrittenDrawingObjectHyperlink != pic.Hyperlink)
                {
                    _pictureHyperlinkRenameSnapshot.Add((pic, pic.Hyperlink));
                    pic.Hyperlink = rewrittenDrawingObjectHyperlink;
                }
            }

            foreach (var chart in s.Charts)
            {
                var rewrittenDrawingObjectHyperlink = DrawingObjectHyperlinkRewriter.Rewrite(chart.Hyperlink, renameOp, s.Name);
                if (rewrittenDrawingObjectHyperlink != chart.Hyperlink)
                {
                    _chartHyperlinkRenameSnapshot.Add((chart, chart.Hyperlink));
                    chart.Hyperlink = rewrittenDrawingObjectHyperlink;
                }
            }
        }

        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_oldName is not null)
        {
            var s = ctx.GetSheet(_sheetId);
            s.Name = _oldName;
            RowColumnShiftHelpers.RestoreFormulas(ctx.Workbook, _formulaSnapshot);
            RowColumnShiftHelpers.RestoreNamedFormulas(ctx.Workbook, _namedFormulaSnapshot, _scopedNamedFormulaSnapshot);
            RowColumnShiftHelpers.RestoreChartVerbatimFormulas(ctx.Workbook, _chartVerbatimRenameSnapshot);

            // T6 restore: string sheet-name refs
            if (_pivotCacheNameSnapshot is not null)
                foreach (var (cache, oldValue) in _pivotCacheNameSnapshot)
                    cache.SourceSheetName = oldValue;

            if (_chartPivotSourceNameSnapshot is not null)
                foreach (var (chart, oldValue) in _chartPivotSourceNameSnapshot)
                    chart.PivotSourceSheetName = oldValue;

            if (_slicerNameSnapshot is not null)
                foreach (var (slicer, oldValue) in _slicerNameSnapshot)
                    slicer.SourceSheetName = oldValue;

            if (_timelineNameSnapshot is not null)
                foreach (var (timeline, oldValue) in _timelineNameSnapshot)
                    timeline.SourceSheetName = oldValue;

            if (_pictureNameSnapshot is not null)
                foreach (var (pic, oldValue) in _pictureNameSnapshot)
                    pic.LinkedSourceSheetName = oldValue;

            // P81 restore: FormControl LinkedCell/ListFillRange
            if (_formControlNameSnapshot is not null)
                foreach (var (control, oldLinkedCell, oldListFillRange) in _formControlNameSnapshot)
                {
                    control.LinkedCell = oldLinkedCell;
                    control.ListFillRange = oldListFillRange;
                }

            // T7/R102 restore: CF FormulaText + colorScale/dataBar/iconSet Formula-type
            // thresholds, and DV Formula1/Formula2 — via the shared RestoreRuleFormulas helper
            // (mirrors RenameStructuredTableCommand's R100 restore) so the threshold slots get
            // undone symmetrically with the Apply-side RewriteRuleFormulas call above.
            if (_cfFormulaRenameSnapshot is not null || _cfThresholdRenameSnapshot is not null || _dvFormulaRenameSnapshot is not null)
            {
                var cfSnap = new Dictionary<Guid, string?>();
                if (_cfFormulaRenameSnapshot is not null)
                    foreach (var (ruleId, oldValue, _) in _cfFormulaRenameSnapshot)
                        cfSnap[ruleId] = oldValue;

                var cfThresholdSnap = new Dictionary<(Guid Id, int Slot), string?>();
                if (_cfThresholdRenameSnapshot is not null)
                    foreach (var (ruleId, slot, oldValue, _) in _cfThresholdRenameSnapshot)
                        cfThresholdSnap[(ruleId, slot)] = oldValue;

                var dvSnap = new Dictionary<(Guid Id, int Slot), string?>();
                if (_dvFormulaRenameSnapshot is not null)
                    foreach (var (ruleId, slot, oldValue, _) in _dvFormulaRenameSnapshot)
                        dvSnap[(ruleId, slot)] = oldValue;

                var cfSheetsToNotify = new HashSet<SheetId>();
                if (_cfFormulaRenameSnapshot is not null)
                    foreach (var (_, _, sheetId) in _cfFormulaRenameSnapshot)
                        cfSheetsToNotify.Add(sheetId);
                if (_cfThresholdRenameSnapshot is not null)
                    foreach (var (_, _, _, sheetId) in _cfThresholdRenameSnapshot)
                        cfSheetsToNotify.Add(sheetId);

                foreach (var sh in ctx.Workbook.Sheets)
                    RowColumnShiftHelpers.RestoreRuleFormulas(sh, cfSnap, cfThresholdSnap, dvSnap);

                // Mirror the Do-path cache invalidation (see the comment above the forward
                // rewrite loop): restoring cf.FormulaText/threshold values in place does not by
                // itself bump ConditionalFormats.Version, so the viewport CF cache would keep
                // serving the stale post-rename precompiled AST after Undo unless we notify here.
                foreach (var sheetId in cfSheetsToNotify)
                    ctx.Workbook.GetSheet(sheetId)?.ConditionalFormats.NotifyRulesChanged();
            }

            // O25 restore: hyperlink bookmarks
            if (_hyperlinkBookmarkRenameSnapshot is not null)
            {
                foreach (var (sheetId, addr, oldBookmark) in _hyperlinkBookmarkRenameSnapshot)
                {
                    var sh = ctx.Workbook.GetSheet(sheetId);
                    if (sh is null) continue;
                    if (sh.HyperlinkMetadata.TryGetValue(addr, out var meta))
                        sh.HyperlinkMetadata[addr] = meta with { Bookmark = oldBookmark };
                }
            }

            // P113 restore: hyperlink raw targets (bookmark-less 'Place in This Document' links)
            if (_hyperlinkTargetRenameSnapshot is not null)
            {
                foreach (var (sheetId, addr, oldTarget) in _hyperlinkTargetRenameSnapshot)
                {
                    var sh = ctx.Workbook.GetSheet(sheetId);
                    if (sh is null) continue;
                    if (sh.Hyperlinks.ContainsKey(addr))
                        sh.Hyperlinks[addr] = oldTarget;
                }
            }

            // R107 restore: drawing-object hyperlinks rewritten by the Apply-side pass above.
            // R108-drawing-object-hyperlink-patch-safety-guard-allowlist: the deconstructed
            // tuple-element name is spelled out (not a bare "oldValue") so the R101 guard's RHS
            // allowlist regex can target this exact restore shape narrowly -- see
            // AllowedDrawingObjectHyperlinkUndoRestoreRhs in R101_DrawingChartHyperlinkPatchSafetyGuardTests.
            if (_drawingShapeHyperlinkRenameSnapshot is not null)
                foreach (var (shape, savedDrawingObjectHyperlink) in _drawingShapeHyperlinkRenameSnapshot)
                    shape.Hyperlink = savedDrawingObjectHyperlink;

            if (_textBoxHyperlinkRenameSnapshot is not null)
                foreach (var (textBox, savedDrawingObjectHyperlink) in _textBoxHyperlinkRenameSnapshot)
                    textBox.Hyperlink = savedDrawingObjectHyperlink;

            if (_pictureHyperlinkRenameSnapshot is not null)
                foreach (var (pic, savedDrawingObjectHyperlink) in _pictureHyperlinkRenameSnapshot)
                    pic.Hyperlink = savedDrawingObjectHyperlink;

            if (_chartHyperlinkRenameSnapshot is not null)
                foreach (var (chart, savedDrawingObjectHyperlink) in _chartHyperlinkRenameSnapshot)
                    chart.Hyperlink = savedDrawingObjectHyperlink;
        }
    }
}

/// <summary>Command to delete a sheet from the workbook.</summary>
public sealed class RemoveSheetCommand : IWorkbookCommand, IWholeWorkbookRecalcCommand, IEstimatesMemory
{
    // R119-commands-undo-byte-budget-1: _removedSheet below retains the ENTIRE deleted Sheet
    // object (every cell, style, drawing, etc.) so Undo can restore it -- the single biggest
    // possible per-command retention in the codebase. Estimate from its occupied-cell count so
    // deleting a large populated sheet actually counts against CommandBus's 50 MB undo
    // byte-budget instead of the flat 200-byte IEstimatesMemory default.
    private const int BytesPerCell = 200;

    private readonly SheetId _sheetId;
    private Sheet? _removedSheet;
    private int _removedIndex;
    private Dictionary<string, NamedRangeSnapshot>? _namedRangeSnapshot;
    private Dictionary<(string Name, SheetId Sheet), (GridRange Range, NamedRangeMetadata Metadata)>? _scopedNamedRangeSnapshot;
    private Dictionary<string, string>? _namedFormulaSnapshot;
    private Dictionary<(string Name, SheetId Sheet), string>? _scopedNamedFormulaSnapshot;
    // N3: sheet-scoped named formulas on SURVIVING sheets whose text referenced the deleted
    // sheet, rewritten to #REF! — distinct from _scopedNamedFormulaSnapshot above, which is the
    // full pre-purge snapshot used to restore the deleted sheet's OWN scoped formulas on undo.
    private Dictionary<(string Name, SheetId Sheet), string>? _survivingScopedNamedFormulaRewriteSnapshot;
    private readonly Dictionary<CellAddress, string> _formulaSnapshot = [];
    // X3: CF/DV formula rewrites across surviving sheets for the deleted-sheet #REF! pass
    private List<(Guid RuleId, string? OldValue, SheetId Sheet)>? _cfFormulaDeleteSnapshot;
    // R102: mirrors RenameSheetCommand's _cfThresholdRenameSnapshot — colorScale/dataBar/iconSet
    // Formula-type cfvo thresholds referencing the deleted sheet must become #REF! too, not just
    // cf.FormulaText, or they keep dangling text naming a sheet that no longer exists.
    private List<(Guid RuleId, int Slot, string? OldValue, SheetId Sheet)>? _cfThresholdDeleteSnapshot;
    private List<(Guid RuleId, int Slot, string? OldValue, SheetId Sheet)>? _dvFormulaDeleteSnapshot;
    // R26: FormControlModel.LinkedCell/ListFillRange hold sheet-qualified string "formulas" just
    // like CF/DV above (mirrors RenameSheetCommand's P81 block, same fields, same FormulaRewriter
    // path) and must be rewritten to #REF! across ALL surviving sheets when they reference the
    // deleted sheet — otherwise a checkbox/spinner/list-box keeps saying the stale sheet name
    // forever and can silently reattach to an unrelated sheet later re-created/renamed with the
    // same name (FormControlInteractionService.TryResolveLinkedCell would then resolve it).
    private List<(FormControlModel Control, string? OldLinkedCell, string? OldListFillRange)>? _formControlDeleteSnapshot;
    // Charts (on surviving sheets) whose DataRange pointed at the deleted sheet — remapped onto
    // their own host sheet so no dangling deleted-sheet reference remains.
    private List<(ChartModel Chart, GridRange OldValue)>? _chartDataRangeDeleteSnapshot;
    // R102: PivotTableModel.SourceRange (on surviving sheets) that pointed at the deleted sheet —
    // remapped onto the pivot's own host sheet, mirroring the chart DataRange block immediately
    // above. Unlike PivotCacheModel.SourceSheetName (a nullable string cleared outright below),
    // GridRange cannot express a sheetless/"cleared" reference, so this uses the same
    // remap-onto-host-sheet fallback as _chartDataRangeDeleteSnapshot. Without this, a surviving
    // PivotTableModel keeps a SourceRange naming a SheetId that no longer resolves via
    // Workbook.GetSheet, which NativeJsonAdapter.Pivot.cs's ToPivotTableDto treats as "drop this
    // pivot table from native-format save" (mirrors PivotTableRefreshService.Refresh /
    // PivotSourceContext.ReadHeaders / SlicerTimelineSourceReader all resolving
    // pivotTable.SourceRange.Start.Sheet the same way) — real Excel instead keeps the pivot table
    // in place showing its last-cached values after the source sheet disappears.
    private List<(PivotTableModel Pivot, GridRange OldValue)>? _pivotSourceRangeDeleteSnapshot;
    // R114: SparklineModel.DataRange (and the optional group-level DateAxisRange) are the same
    // shape of field as ChartModel.DataRange/PivotTableModel.SourceRange above — a GridRange that
    // can legitimately point at a different sheet than its host sheet (Excel's Sparkline "Edit
    // Data" dialog allows a cross-sheet source range; see XlsxSparklineMapper.cs) — but were never
    // remapped here. Left dangling, XlsxSparklineMapper.Save's validSparklines filter requires
    // ResolveSheetName(...) to succeed for DataRange.Start.Sheet, so a cross-sheet sparkline whose
    // data-source sheet was deleted was silently dropped ENTIRELY on the next save — not merely
    // losing its data reference like a chart/pivot would, since sparklines have no "broken but
    // still present" representation in the writer once ResolveSheetName returns null — whereas
    // real Excel keeps the sparkline in place with a stale/broken reference. Mirrors the chart/
    // pivot remap-onto-host-sheet fallback immediately above for the same "GridRange cannot
    // express a sheetless/cleared reference" reason.
    private List<(SparklineModel Sparkline, GridRange OldDataRange)>? _sparklineDataRangeDeleteSnapshot;
    private List<(SparklineModel Sparkline, GridRange OldDateAxisRange)>? _sparklineDateAxisRangeDeleteSnapshot;
    // String sheet-name refs on model objects that named the deleted sheet — cleared so no
    // dangling deleted-sheet reference remains (mirrors RenameSheetCommand's T6 block, but the
    // sheet has no new name to rewrite onto, so these are nulled instead of renamed).
    private List<(PivotCacheModel Cache, string OldValue)>? _pivotCacheNameDeleteSnapshot;
    // R96: WorksheetRange/Table pivot caches whose records we captured into RawRecordsXml because
    // their source sheet is about to disappear -- see TryCapturePivotCacheRecordsXml for why.
    private List<(PivotCacheModel Cache, string? OldValue)>? _pivotCacheRawRecordsDeleteSnapshot;
    // R107: table-backed pivot caches whose SourceTableId we pinned because their source table
    // lived on the deleted sheet and had never been refreshed (SourceTableId still null) — mirrors
    // ConvertStructuredTableToRangeCommand's R106 _orphanedPivotCaches list. The pinned id is
    // always a fresh assignment from null, so restoring on undo is just nulling it back out.
    private List<PivotCacheModel>? _pivotCacheTableIdDeleteSnapshot;
    // R108: a slicer/timeline's DrawingML anchor physically lives inside its SourceSheetName
    // sheet's drawing part -- exactly like real Excel, deleting that sheet must delete the widget
    // itself, not merely null the back-reference. Nulling alone left the SlicerModel/TimelineModel
    // instance behind in ctx.Workbook.Slicers/Timelines, homeless but alive, and every downstream
    // consumer (SlicerTimelinePanePlanner's IsConnectedToPivotOnSheet fallback, XlsxSlicerTimeline
    // Writer.ResolveWorksheetPath's sheet1 fallback) would then silently reattach it to an
    // unrelated surviving sheet on the very next render/save. Removed instances (and their
    // original list index, so Revert can splice them back in place rather than merely appending)
    // are captured here so Undo restores both the SourceSheetName and the list membership.
    private List<(SlicerModel Slicer, string OldValue, int Index)>? _slicerNameDeleteSnapshot;
    // P84/R108: mirrors _slicerNameDeleteSnapshot above for TimelineModel.SourceSheetName — a
    // timeline anchored on the deleted sheet must be removed from ctx.Workbook.Timelines outright
    // (not merely have its dangling sheet-name ref cleared), or it can silently reattach to an
    // unrelated sheet later re-created/renamed with the same name.
    private List<(TimelineModel Timeline, string OldValue, int Index)>? _timelineNameDeleteSnapshot;
    private List<(PictureModel Picture, string OldValue)>? _pictureNameDeleteSnapshot;
    // R100: mirrors _pivotCacheNameDeleteSnapshot above for ChartModel.PivotSourceSheetName — a
    // pivot chart's recorded "where does my PivotTable actually live" sheet name must be cleared
    // when that sheet is deleted, or XlsxChartXmlWriter keeps emitting a <c:pivotSource><c:name>
    // naming a worksheet absent from the workbook forever (mirrors RenameSheetCommand's T6 block,
    // which uses this same field).
    private List<(ChartModel Chart, string OldValue)>? _chartPivotSourceNameDeleteSnapshot;
    // K16: chart verbatim series/data-label formulas that reference the deleted sheet must
    // become #REF! just like ordinary cell/CF/DV formulas do via DeleteSheetOp — otherwise
    // they keep dangling text naming a sheet that no longer exists.
    private List<RowColumnShiftHelpers.ChartVerbatimWorkbookSnapshot>? _chartVerbatimDeleteSnapshot;
    // R95: Sheet.Hyperlinks / Sheet.HyperlinkMetadata.Bookmark carry the same kind of
    // sheet-qualified string reference as CF/DV/FormControl above (mirrors RenameSheetCommand's
    // O25/P113 blocks, same two fields) and must be rewritten to #REF! across ALL surviving
    // sheets whose 'Place in This Document' hyperlink names the deleted sheet — otherwise the
    // stale target text survives the delete and can silently reattach to an unrelated sheet
    // later re-created/renamed with the same name (same failure mode the T6/P84 string-ref
    // clears above guard against for PivotCache/Slicer/Picture/Timeline).
    private List<(SheetId Sheet, CellAddress Address, string OldBookmark)>? _hyperlinkBookmarkDeleteSnapshot;
    private List<(SheetId Sheet, CellAddress Address, string OldTarget)>? _hyperlinkTargetDeleteSnapshot;
    // R107-drawing-object-hyperlink-sheet-identity: mirrors RenameSheetCommand's equivalent
    // snapshot -- DrawingShapeModel/TextBoxModel/PictureModel/ChartModel.Hyperlink carries the
    // same kind of 'Place in This Document' sheet-qualified reference as the R95 cell-hyperlink
    // fields above but was never rewritten to #REF! on delete -- see DrawingObjectHyperlinkRewriter.
    private List<(DrawingShapeModel Shape, DrawingObjectHyperlink? OldValue)>? _drawingShapeHyperlinkDeleteSnapshot;
    private List<(TextBoxModel TextBox, DrawingObjectHyperlink? OldValue)>? _textBoxHyperlinkDeleteSnapshot;
    private List<(PictureModel Picture, DrawingObjectHyperlink? OldValue)>? _pictureHyperlinkDeleteSnapshot;
    private List<(ChartModel Chart, DrawingObjectHyperlink? OldValue)>? _chartHyperlinkDeleteSnapshot;

    public string Label => "Delete Sheet";

    /// <inheritdoc/>
    /// <remarks>
    /// Estimated from the removed sheet's occupied-cell count once Apply has run (the sheet is
    /// null before that, in which case CommandBus never actually queries this -- EstimateBytes is
    /// only called after Apply pushes the command). Falls back to 0 in that unreached case rather
    /// than an arbitrary constant, since a genuinely empty/never-applied removal retains nothing.
    /// </remarks>
    public int EstimatedBytes => _removedSheet is null
        ? 0
        : (int)Math.Min((long)_removedSheet.GetOccupiedCells().Count * BytesPerCell, int.MaxValue);

    public RemoveSheetCommand(SheetId sheetId) => _sheetId = sheetId;

    public CommandOutcome Apply(ICommandContext ctx)
    {
        if (CommandGuards.RejectIfWorkbookStructureProtected(ctx.Workbook) is { } protectedOutcome)
            return protectedOutcome;

        if (ctx.Workbook.Sheets.Count <= 1)
            return new CommandOutcome(false, "Cannot delete the only sheet.");

        // R98: Real Excel refuses to delete a sheet if doing so would leave the workbook with
        // zero visible sheets, even when other (hidden/very-hidden) sheets remain -- a workbook
        // must always retain at least one visible sheet (mirrors SetSheetHiddenCommand's
        // symmetric "Cannot hide the only visible sheet." guard above, and the same invariant
        // XlsxWorkbookMetadataWriter.ClampToVisibleSheetIndex assumes on write). Checking here in
        // Core.Commands guards every caller (WPF ribbon, WPF tab context menu, Avalonia) at once.
        if (!ctx.Workbook.Sheets.Any(s => s.Id != _sheetId && !s.IsHidden && !s.IsVeryHidden))
            return new CommandOutcome(false, "Cannot delete the only visible sheet.");

        var sheet = ctx.GetSheet(_sheetId);
        // R139-workbook-protection: an individually-protected sheet must refuse Delete of its own
        // tab even when the workbook's structure is not protected -- see RenameSheetCommand's
        // matching comment above.
        if (CommandGuards.RejectIfProtected(sheet) is { } sheetProtectedOutcome)
            return sheetProtectedOutcome;

        _removedSheet = sheet;
        var sheets = ctx.Workbook.Sheets;
        for (int i = 0; i < sheets.Count; i++)
            if (sheets[i].Id == _sheetId) { _removedIndex = i; break; }
        _namedRangeSnapshot = RowColumnShiftHelpers.CaptureNamedRanges(ctx.Workbook);
        _scopedNamedRangeSnapshot = RowColumnShiftHelpers.CaptureScopedNamedRanges(ctx.Workbook);
        // R51-meta-1 (r50 self-regression shadow): do NOT bare-remove global named ranges that
        // target the deleted sheet here. ctx.Workbook.RemoveSheet below runs
        // Workbook.RemoveNamedRangesForSheet, which keeps the name in the Name Manager and
        // rewrites its RefersTo to "#REF!" (matching real Excel) instead of dropping it outright.
        // A separate pre-loop that called the bare RemoveNamedRange first would remove the entry
        // before that #REF!-converting pass ever saw it, silently dropping the name — exactly the
        // bug the #REF! conversion was added to fix. Capture scoped named formulas BEFORE RemoveSheet purges them.
        _scopedNamedFormulaSnapshot = ctx.Workbook.ScopedNamedFormulas
            .ToDictionary(p => p.Key, p => p.Value);
        var deletedSheetName = sheet.Name;
        // R27: structured tables hosted on the deleted sheet no longer exist anywhere in the
        // workbook, so cross-sheet Table[...] references to them must become #REF! — pass the
        // captured names through to every DeleteSheetOp below (see DeleteSheetOp.DeletedTableNames).
        var deletedTableNames = sheet.StructuredTables.Select(t => t.Name).ToList();
        ctx.Workbook.RemoveSheet(_sheetId);
        _formulaSnapshot.Clear();
        RowColumnShiftHelpers.RewriteAllFormulas(
            ctx.Workbook, new DeleteSheetOp(deletedSheetName, deletedTableNames), _formulaSnapshot);
        // Defined names whose refers-to is a formula expression are not covered by the named-range
        // pass above; rewrite their sheet-qualified references to the deleted sheet to #REF! too.
        _namedFormulaSnapshot = RewriteNamedFormulasForDeletedSheet(ctx.Workbook, deletedSheetName, deletedTableNames);
        // N3: sheet-scoped named formulas living on SURVIVING sheets can still reference the
        // deleted sheet in their text (e.g. Sheet1-scoped 'Foo' = '=Sheet2!A1*2' when Sheet2 is
        // deleted) — symmetric with the named-range pass above, which already rewrites scoped
        // named RANGES. RemoveSheet only purged the deleted sheet's OWN scoped formulas, so this
        // must run separately over what's left.
        _survivingScopedNamedFormulaRewriteSnapshot =
            RewriteScopedNamedFormulasForDeletedSheet(ctx.Workbook, deletedSheetName, deletedTableNames);

        // X3/R102: rewrite CF FormulaText, colorScale/dataBar/iconSet Formula-type thresholds,
        // and DV Formula1/Formula2 on all surviving sheets that reference the deleted sheet,
        // producing #REF! — mirrors RenameSheetCommand's T7/R102 pass via the same shared
        // RewriteRuleFormulas helper (the primitive RenameStructuredTableCommand's R100 fix uses).
        var deleteOp = new DeleteSheetOp(deletedSheetName, deletedTableNames);
        _cfFormulaDeleteSnapshot = [];
        _cfThresholdDeleteSnapshot = [];
        _dvFormulaDeleteSnapshot = [];
        foreach (var s in ctx.Workbook.Sheets)
        {
            var cfSnap = new Dictionary<Guid, string?>();
            var cfThresholdSnap = new Dictionary<(Guid Id, int Slot), string?>();
            var dvSnap = new Dictionary<(Guid Id, int Slot), string?>();
            RowColumnShiftHelpers.RewriteRuleFormulas(s, deleteOp, cfSnap, cfThresholdSnap, dvSnap);

            foreach (var (ruleId, oldValue) in cfSnap)
                _cfFormulaDeleteSnapshot.Add((ruleId, oldValue, s.Id));
            foreach (var (key, oldValue) in cfThresholdSnap)
                _cfThresholdDeleteSnapshot.Add((key.Id, key.Slot, oldValue, s.Id));
            foreach (var (key, oldValue) in dvSnap)
                _dvFormulaDeleteSnapshot.Add((key.Id, key.Slot, oldValue, s.Id));

            // See RenameSheetCommand's T7/R102 pass: mutating cf.FormulaText/threshold values in
            // place never invalidates the (sheet.Id, ContentVersion, ConditionalFormats.Version)-
            // keyed CF viewport cache on its own, so bump Version explicitly for surviving sheets
            // whose rules were rewritten to #REF!.
            if (cfSnap.Count > 0 || cfThresholdSnap.Count > 0)
                s.ConditionalFormats.NotifyRulesChanged();
        }

        // R26: rewrite FormControlModel.LinkedCell/ListFillRange across all surviving sheets
        // that reference the deleted sheet, producing #REF! — mirrors RenameSheetCommand's P81
        // block and the X3 CF/DV pass above (same FormulaRewriter path, same bare-ref fields).
        _formControlDeleteSnapshot = [];
        foreach (var s in ctx.Workbook.Sheets)
        {
            foreach (var control in s.FormControls)
            {
                string? newLinkedCell = control.LinkedCell;
                string? newListFillRange = control.ListFillRange;
                bool changed = false;

                if (control.LinkedCell is { } linkedCell)
                {
                    var rewritten = FormulaRewriter.Rewrite(linkedCell, deleteOp, s.Name);
                    if (rewritten is not null && rewritten != linkedCell)
                    {
                        newLinkedCell = rewritten;
                        changed = true;
                    }
                }

                if (control.ListFillRange is { } listFillRange)
                {
                    var rewritten = FormulaRewriter.Rewrite(listFillRange, deleteOp, s.Name);
                    if (rewritten is not null && rewritten != listFillRange)
                    {
                        newListFillRange = rewritten;
                        changed = true;
                    }
                }

                if (!changed)
                    continue;

                _formControlDeleteSnapshot.Add((control, control.LinkedCell, control.ListFillRange));
                control.LinkedCell = newLinkedCell;
                control.ListFillRange = newListFillRange;
            }
        }

        // K16: rewrite chart verbatim series/data-label formulas across all surviving sheets
        // that reference the deleted sheet, producing #REF! — mirrors the X3 CF/DV pass above.
        _chartVerbatimDeleteSnapshot = RowColumnShiftHelpers.CaptureChartVerbatimFormulas(ctx.Workbook);
        RowColumnShiftHelpers.RewriteChartVerbatimFormulas(ctx.Workbook, deleteOp);

        // Charts on surviving sheets whose DataRange sources from the deleted sheet: GridRange
        // cannot express a sheetless/"cleared" reference (unlike the nullable string refs on
        // PivotCacheModel.SourceSheetName / SlicerModel.SourceSheetName / PictureModel
        // LinkedSourceSheetName), so remap the dangling DataRange onto the chart's own host sheet
        // — mirroring the same "no dangling deleted-sheet ref" outcome as those string refs.
        _chartDataRangeDeleteSnapshot = [];
        foreach (var s in ctx.Workbook.Sheets)
        {
            foreach (var chart in s.Charts)
            {
                if (chart.DataRange.Start.Sheet == _sheetId)
                {
                    _chartDataRangeDeleteSnapshot.Add((chart, chart.DataRange));
                    var anchor = new CellAddress(s.Id, 1, 1);
                    chart.DataRange = new GridRange(anchor, anchor);
                }
            }
        }

        // R102: PivotTableModel.SourceRange (on surviving sheets, possibly hosting the pivot
        // table itself elsewhere) whose Start.Sheet names the deleted sheet — remap onto the
        // pivot's own host sheet, exactly mirroring the chart DataRange pass immediately above.
        // See _pivotSourceRangeDeleteSnapshot's declaration for why GridRange (unlike the nullable
        // PivotCacheModel.SourceSheetName string cleared below) needs a "remap" fallback instead of
        // a clear.
        _pivotSourceRangeDeleteSnapshot = [];
        foreach (var s in ctx.Workbook.Sheets)
        {
            foreach (var pivot in s.PivotTables)
            {
                if (pivot.SourceRange.Start.Sheet == _sheetId)
                {
                    _pivotSourceRangeDeleteSnapshot.Add((pivot, pivot.SourceRange));
                    var anchor = new CellAddress(s.Id, 1, 1);
                    pivot.SourceRange = new GridRange(anchor, anchor);
                }
            }
        }

        // R114: mirrors the chart DataRange / pivot SourceRange passes above for
        // SparklineModel.DataRange and its optional group-level DateAxisRange — see the field doc
        // comments above for why sparklines need the same remap-onto-host-sheet fallback (unlike
        // charts/pivots, a dangling sparkline data-source sheet reference causes the ENTIRE
        // sparkline to be dropped on save, not just the reference).
        _sparklineDataRangeDeleteSnapshot = [];
        _sparklineDateAxisRangeDeleteSnapshot = [];
        foreach (var s in ctx.Workbook.Sheets)
        {
            var anchor = new CellAddress(s.Id, 1, 1);
            foreach (var sparkline in s.Sparklines)
            {
                if (sparkline.DataRange.Start.Sheet == _sheetId)
                {
                    _sparklineDataRangeDeleteSnapshot.Add((sparkline, sparkline.DataRange));
                    sparkline.DataRange = new GridRange(anchor, anchor);
                }

                if (sparkline.DateAxisRange is { } dateAxisRange && dateAxisRange.Start.Sheet == _sheetId)
                {
                    _sparklineDateAxisRangeDeleteSnapshot.Add((sparkline, dateAxisRange));
                    sparkline.DateAxisRange = new GridRange(anchor, anchor);
                }
            }
        }

        // String sheet-name refs (PivotCacheModel.SourceSheetName / SlicerModel.SourceSheetName /
        // PictureModel.LinkedSourceSheetName) that named the deleted sheet: clear them so they can
        // never silently reattach to an unrelated sheet later re-created/renamed with the same name
        // — mirrors RenameSheetCommand's T6 block, which uses the same three fields.
        _pivotCacheNameDeleteSnapshot = [];
        _pivotCacheRawRecordsDeleteSnapshot = [];
        foreach (var cache in ctx.Workbook.PivotCaches)
        {
            if (cache.SourceSheetName is not null &&
                string.Equals(cache.SourceSheetName, deletedSheetName, StringComparison.OrdinalIgnoreCase))
            {
                _pivotCacheNameDeleteSnapshot.Add((cache, cache.SourceSheetName));

                // R96: a WorksheetRange/Table cache's <pivotCacheRecords> are always regenerated
                // live from its source range on save (XlsxPivotTableWriter.Cache.cs). Once
                // SourceSheetName is nulled below, that live regeneration can never resolve a range
                // again, and the writer's only fallback (RawRecordsXml -- previously populated only
                // for External/Consolidation/Scenario sources) is blank for this cache, so the
                // pivot table's cached records would silently truncate to
                // <pivotCacheRecords count="0"/> on the very next save. Snapshot the cache's CURRENT
                // records now, while `sheet`'s cell data is still live (it has already been unlinked
                // from ctx.Workbook.Sheets above, but the in-memory Sheet object itself is untouched),
                // into RawRecordsXml so that fallback has real data to serve -- matching real Excel,
                // which keeps a pivot table's last-refreshed cache after its source sheet disappears
                // (only a subsequent manual Refresh against the missing source then fails).
                if (string.IsNullOrWhiteSpace(cache.RawRecordsXml) &&
                    TryCapturePivotCacheRecordsXml(cache, sheet, out var capturedRecordsXml))
                {
                    _pivotCacheRawRecordsDeleteSnapshot.Add((cache, cache.RawRecordsXml));
                    cache.RawRecordsXml = capturedRecordsXml;
                }

                cache.SourceSheetName = null;
            }
        }

        // R107 (consolidated R107-round2 into CommandGuards.PinOrphanedPivotCacheSourceTableIds, the
        // shared "table name about to be freed" guard used by every command that removes a table --
        // see its doc comment for the full hazard/rationale). Deleting the sheet frees every table
        // that lived on it at once, so pin once per surviving-in-memory table (the `sheet` object --
        // and its StructuredTables -- is still intact even though it has been unlinked from
        // ctx.Workbook.Sheets, exactly like the RawRecordsXml capture above relies on).
        _pivotCacheTableIdDeleteSnapshot = [];
        foreach (var removedTable in sheet.StructuredTables)
            _pivotCacheTableIdDeleteSnapshot.AddRange(
                CommandGuards.PinOrphanedPivotCacheSourceTableIds(ctx.Workbook, removedTable));

        // R108: a slicer anchored on the deleted sheet must be removed from
        // ctx.Workbook.Slicers outright, not merely have SourceSheetName nulled -- see the
        // field doc comment above for why leaving the instance behind resurrects it elsewhere on
        // the next render/save. Walk by index (descending on removal) so the list can be mutated
        // safely while iterating, and capture each removed slicer's original index so Revert can
        // splice it back into the same slot instead of merely appending it to the end.
        _slicerNameDeleteSnapshot = [];
        for (var i = ctx.Workbook.Slicers.Count - 1; i >= 0; i--)
        {
            var slicer = ctx.Workbook.Slicers[i];
            if (slicer.SourceSheetName is not null &&
                string.Equals(slicer.SourceSheetName, deletedSheetName, StringComparison.OrdinalIgnoreCase))
            {
                _slicerNameDeleteSnapshot.Add((slicer, slicer.SourceSheetName, i));
                slicer.SourceSheetName = null;
                ctx.Workbook.Slicers.RemoveAt(i);
            }
        }

        _pictureNameDeleteSnapshot = [];
        foreach (var s in ctx.Workbook.Sheets)
        {
            foreach (var pic in s.Pictures)
            {
                if (pic.LinkedSourceSheetName is not null &&
                    string.Equals(pic.LinkedSourceSheetName, deletedSheetName, StringComparison.OrdinalIgnoreCase))
                {
                    _pictureNameDeleteSnapshot.Add((pic, pic.LinkedSourceSheetName));
                    pic.LinkedSourceSheetName = null;
                }
            }
        }

        // R108: mirrors the slicer removal block above -- a timeline anchored on the deleted
        // sheet must be removed from ctx.Workbook.Timelines outright, walked descending by index
        // for the same safe-mutate-while-iterating + original-slot-restore reasons.
        _timelineNameDeleteSnapshot = [];
        for (var i = ctx.Workbook.Timelines.Count - 1; i >= 0; i--)
        {
            var timeline = ctx.Workbook.Timelines[i];
            if (timeline.SourceSheetName is not null &&
                string.Equals(timeline.SourceSheetName, deletedSheetName, StringComparison.OrdinalIgnoreCase))
            {
                _timelineNameDeleteSnapshot.Add((timeline, timeline.SourceSheetName, i));
                timeline.SourceSheetName = null;
                ctx.Workbook.Timelines.RemoveAt(i);
            }
        }

        // R100: pivot charts (on surviving sheets) whose PivotSourceSheetName named the deleted
        // sheet — clear so XlsxChartXmlWriter's <c:pivotSource><c:name> never keeps naming a
        // worksheet absent from the workbook and can never silently reattach to an unrelated sheet
        // later re-created/renamed with the same name — mirrors the PivotCache/Slicer/Picture/
        // Timeline clears immediately above (same field RenameSheetCommand's T6 block rewrites).
        _chartPivotSourceNameDeleteSnapshot = [];
        foreach (var s in ctx.Workbook.Sheets)
        {
            foreach (var chart in s.Charts)
            {
                if (chart.PivotSourceSheetName is not null &&
                    string.Equals(chart.PivotSourceSheetName, deletedSheetName, StringComparison.OrdinalIgnoreCase))
                {
                    _chartPivotSourceNameDeleteSnapshot.Add((chart, chart.PivotSourceSheetName));
                    chart.PivotSourceSheetName = null;
                }
            }
        }

        // R95: rewrite 'Place in This Document' hyperlink bookmarks/targets across ALL surviving
        // sheets that reference the deleted sheet, producing #REF! — mirrors RenameSheetCommand's
        // O25/P113 pass (same two fields, same bookmark-vs-bare-target split), but using deleteOp
        // so FormulaRewriter converts the sheet-qualified ref to #REF! instead of a new name.
        _hyperlinkBookmarkDeleteSnapshot = [];
        _hyperlinkTargetDeleteSnapshot = [];
        foreach (var s in ctx.Workbook.Sheets)
        {
            List<KeyValuePair<CellAddress, HyperlinkMetadata>>? changed = null;
            List<KeyValuePair<CellAddress, string>>? targetChanged = null;
            foreach (var pair in s.HyperlinkMetadata)
            {
                var meta = pair.Value;
                if (meta.LinkType != HyperlinkTargetKind.PlaceInThisDocument)
                    continue;

                var bookmark = meta.Bookmark;
                if (string.IsNullOrEmpty(bookmark))
                {
                    // No bookmark recorded — the sheet-qualified ref lives directly on
                    // sheet.Hyperlinks[addr] instead (see SetHyperlinkCommand).
                    if (!s.Hyperlinks.TryGetValue(pair.Key, out var target) || string.IsNullOrEmpty(target))
                        continue;

                    var tBangIndex = target.IndexOf('!', StringComparison.Ordinal);
                    if (tBangIndex < 0)
                        continue;

                    var tRawSheetPart = target[..tBangIndex].Trim('\'');
                    var tSheetPart = tRawSheetPart.Contains("''", StringComparison.Ordinal)
                        ? tRawSheetPart.Replace("''", "'", StringComparison.Ordinal)
                        : tRawSheetPart;
                    if (!string.Equals(tSheetPart, deletedSheetName, StringComparison.OrdinalIgnoreCase))
                        continue;

                    var rewrittenTarget = FormulaRewriter.Rewrite(target, deleteOp, s.Name);
                    if (rewrittenTarget is null || rewrittenTarget == target)
                        continue;

                    (targetChanged ??= []).Add(new KeyValuePair<CellAddress, string>(pair.Key, rewrittenTarget));
                    continue;
                }

                var bangIndex = bookmark.IndexOf('!', StringComparison.Ordinal);
                if (bangIndex < 0)
                    continue;

                // Unescape doubled single-quotes ('' -> ') in a quoted sheet name before comparing
                // against deletedSheetName, mirroring RenameSheetCommand's O27 handling.
                var rawSheetPart = bookmark[..bangIndex].Trim('\'');
                var sheetPart = rawSheetPart.Contains("''", StringComparison.Ordinal)
                    ? rawSheetPart.Replace("''", "'", StringComparison.Ordinal)
                    : rawSheetPart;
                if (!string.Equals(sheetPart, deletedSheetName, StringComparison.OrdinalIgnoreCase))
                    continue;

                var rewritten = FormulaRewriter.Rewrite(bookmark, deleteOp, s.Name);
                if (rewritten is null || rewritten == bookmark)
                    continue;

                (changed ??= []).Add(new KeyValuePair<CellAddress, HyperlinkMetadata>(pair.Key, meta with { Bookmark = rewritten }));
            }

            if (changed is not null)
            {
                foreach (var (addr, newMeta) in changed)
                {
                    _hyperlinkBookmarkDeleteSnapshot.Add((s.Id, addr, s.HyperlinkMetadata[addr].Bookmark));
                    s.HyperlinkMetadata[addr] = newMeta;
                }
            }

            if (targetChanged is not null)
            {
                foreach (var (addr, newTarget) in targetChanged)
                {
                    _hyperlinkTargetDeleteSnapshot.Add((s.Id, addr, s.Hyperlinks[addr]));
                    s.Hyperlinks[addr] = newTarget;
                }
            }
        }

        // R107-drawing-object-hyperlink-sheet-identity: rewrite drawing-object 'Place in This
        // Document' hyperlinks across ALL surviving sheets that reference the deleted sheet,
        // producing #REF! -- mirrors the R95 cell-hyperlink pass above and RenameSheetCommand's
        // equivalent pass, using deleteOp so FormulaRewriter converts the sheet-qualified ref to
        // #REF! instead of a new name.
        _drawingShapeHyperlinkDeleteSnapshot = [];
        _textBoxHyperlinkDeleteSnapshot = [];
        _pictureHyperlinkDeleteSnapshot = [];
        _chartHyperlinkDeleteSnapshot = [];
        foreach (var s in ctx.Workbook.Sheets)
        {
            foreach (var shape in s.DrawingShapes)
            {
                // R108-drawing-object-hyperlink-patch-safety-guard-allowlist: see the matching
                // comment on RenameSheetCommand's equivalent pass above.
                var rewrittenDrawingObjectHyperlink = DrawingObjectHyperlinkRewriter.Rewrite(shape.Hyperlink, deleteOp, s.Name);
                if (rewrittenDrawingObjectHyperlink != shape.Hyperlink)
                {
                    _drawingShapeHyperlinkDeleteSnapshot.Add((shape, shape.Hyperlink));
                    shape.Hyperlink = rewrittenDrawingObjectHyperlink;
                }
            }

            foreach (var textBox in s.TextBoxes)
            {
                var rewrittenDrawingObjectHyperlink = DrawingObjectHyperlinkRewriter.Rewrite(textBox.Hyperlink, deleteOp, s.Name);
                if (rewrittenDrawingObjectHyperlink != textBox.Hyperlink)
                {
                    _textBoxHyperlinkDeleteSnapshot.Add((textBox, textBox.Hyperlink));
                    textBox.Hyperlink = rewrittenDrawingObjectHyperlink;
                }
            }

            foreach (var pic in s.Pictures)
            {
                var rewrittenDrawingObjectHyperlink = DrawingObjectHyperlinkRewriter.Rewrite(pic.Hyperlink, deleteOp, s.Name);
                if (rewrittenDrawingObjectHyperlink != pic.Hyperlink)
                {
                    _pictureHyperlinkDeleteSnapshot.Add((pic, pic.Hyperlink));
                    pic.Hyperlink = rewrittenDrawingObjectHyperlink;
                }
            }

            foreach (var chart in s.Charts)
            {
                var rewrittenDrawingObjectHyperlink = DrawingObjectHyperlinkRewriter.Rewrite(chart.Hyperlink, deleteOp, s.Name);
                if (rewrittenDrawingObjectHyperlink != chart.Hyperlink)
                {
                    _chartHyperlinkDeleteSnapshot.Add((chart, chart.Hyperlink));
                    chart.Hyperlink = rewrittenDrawingObjectHyperlink;
                }
            }
        }

        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_removedSheet is not null)
        {
            RowColumnShiftHelpers.RestoreFormulas(ctx.Workbook, _formulaSnapshot);
            ctx.Workbook.InsertSheet(_removedIndex, _removedSheet);
            RowColumnShiftHelpers.RestoreNamedRanges(ctx.Workbook, _namedRangeSnapshot);
            RowColumnShiftHelpers.RestoreScopedNamedRanges(ctx.Workbook, _scopedNamedRangeSnapshot);
            RestoreNamedFormulas(ctx.Workbook, _namedFormulaSnapshot);
            RestoreScopedNamedFormulas(ctx.Workbook, _scopedNamedFormulaSnapshot);
            RestoreScopedNamedFormulas(ctx.Workbook, _survivingScopedNamedFormulaRewriteSnapshot);
            RowColumnShiftHelpers.RestoreChartVerbatimFormulas(ctx.Workbook, _chartVerbatimDeleteSnapshot);

            // X3/R102 restore: CF FormulaText + colorScale/dataBar/iconSet Formula-type
            // thresholds, and DV Formula1/Formula2 rewritten to #REF! must be restored — via the
            // shared RestoreRuleFormulas helper (mirrors RenameSheetCommand's T7/R102 restore).
            if (_cfFormulaDeleteSnapshot is not null || _cfThresholdDeleteSnapshot is not null || _dvFormulaDeleteSnapshot is not null)
            {
                var cfSnap = new Dictionary<Guid, string?>();
                if (_cfFormulaDeleteSnapshot is not null)
                    foreach (var (ruleId, oldValue, _) in _cfFormulaDeleteSnapshot)
                        cfSnap[ruleId] = oldValue;

                var cfThresholdSnap = new Dictionary<(Guid Id, int Slot), string?>();
                if (_cfThresholdDeleteSnapshot is not null)
                    foreach (var (ruleId, slot, oldValue, _) in _cfThresholdDeleteSnapshot)
                        cfThresholdSnap[(ruleId, slot)] = oldValue;

                var dvSnap = new Dictionary<(Guid Id, int Slot), string?>();
                if (_dvFormulaDeleteSnapshot is not null)
                    foreach (var (ruleId, slot, oldValue, _) in _dvFormulaDeleteSnapshot)
                        dvSnap[(ruleId, slot)] = oldValue;

                var cfSheetsToNotify = new HashSet<SheetId>();
                if (_cfFormulaDeleteSnapshot is not null)
                    foreach (var (_, _, sheetId) in _cfFormulaDeleteSnapshot)
                        cfSheetsToNotify.Add(sheetId);
                if (_cfThresholdDeleteSnapshot is not null)
                    foreach (var (_, _, _, sheetId) in _cfThresholdDeleteSnapshot)
                        cfSheetsToNotify.Add(sheetId);

                foreach (var sh in ctx.Workbook.Sheets)
                    RowColumnShiftHelpers.RestoreRuleFormulas(sh, cfSnap, cfThresholdSnap, dvSnap);

                // Mirror the Do-path cache invalidation above (X3/R102 pass): restoring
                // cf.FormulaText/threshold values in place does not bump ConditionalFormats.
                // Version on its own, so the viewport CF cache would keep serving the stale
                // #REF!-rewritten precompiled AST after Undo unless we notify here too.
                foreach (var sheetId in cfSheetsToNotify)
                    ctx.Workbook.GetSheet(sheetId)?.ConditionalFormats.NotifyRulesChanged();
            }

            if (_formControlDeleteSnapshot is not null)
                foreach (var (control, oldLinkedCell, oldListFillRange) in _formControlDeleteSnapshot)
                {
                    control.LinkedCell = oldLinkedCell;
                    control.ListFillRange = oldListFillRange;
                }

            if (_chartDataRangeDeleteSnapshot is not null)
                foreach (var (chart, oldValue) in _chartDataRangeDeleteSnapshot)
                    chart.DataRange = oldValue;

            if (_pivotSourceRangeDeleteSnapshot is not null)
                foreach (var (pivot, oldValue) in _pivotSourceRangeDeleteSnapshot)
                    pivot.SourceRange = oldValue;

            // R114 restore: undo the DataRange/DateAxisRange remap from the Apply-side pass above
            // (mirrors the chart DataRange / pivot SourceRange restores immediately above it).
            if (_sparklineDataRangeDeleteSnapshot is not null)
                foreach (var (sparkline, oldValue) in _sparklineDataRangeDeleteSnapshot)
                    sparkline.DataRange = oldValue;

            if (_sparklineDateAxisRangeDeleteSnapshot is not null)
                foreach (var (sparkline, oldValue) in _sparklineDateAxisRangeDeleteSnapshot)
                    sparkline.DateAxisRange = oldValue;

            if (_pivotCacheNameDeleteSnapshot is not null)
                foreach (var (cache, oldValue) in _pivotCacheNameDeleteSnapshot)
                    cache.SourceSheetName = oldValue;

            // R96 restore: undo the RawRecordsXml capture from the Apply-side T6 pass above so a
            // redo/undo cycle doesn't leave a cache carrying preserved records it never had before
            // the delete (matches the SourceSheetName restore immediately above it).
            if (_pivotCacheRawRecordsDeleteSnapshot is not null)
                foreach (var (cache, oldValue) in _pivotCacheRawRecordsDeleteSnapshot)
                    cache.RawRecordsXml = oldValue;

            // R107 restore: undo the SourceTableId pin from the Apply-side pass above so a
            // redo/undo cycle doesn't leave a cache carrying an id it never had before the delete
            // (matches the SourceSheetName/RawRecordsXml restores immediately above it).
            if (_pivotCacheTableIdDeleteSnapshot is not null)
                CommandGuards.UnpinOrphanedPivotCacheSourceTableIds(_pivotCacheTableIdDeleteSnapshot);

            // R108 restore: re-insert each removed slicer at its original list index (not just
            // append) and restore its SourceSheetName. The Apply-side pass appended in descending
            // index order, so walking this snapshot list in reverse re-inserts in ascending index
            // order, reproducing the original Slicers list layout.
            if (_slicerNameDeleteSnapshot is not null)
                for (var k = _slicerNameDeleteSnapshot.Count - 1; k >= 0; k--)
                {
                    var (slicer, oldValue, index) = _slicerNameDeleteSnapshot[k];
                    slicer.SourceSheetName = oldValue;
                    ctx.Workbook.Slicers.Insert(Math.Min(index, ctx.Workbook.Slicers.Count), slicer);
                }

            if (_pictureNameDeleteSnapshot is not null)
                foreach (var (pic, oldValue) in _pictureNameDeleteSnapshot)
                    pic.LinkedSourceSheetName = oldValue;

            // R108 restore: mirrors the slicer restore immediately above.
            if (_timelineNameDeleteSnapshot is not null)
                for (var k = _timelineNameDeleteSnapshot.Count - 1; k >= 0; k--)
                {
                    var (timeline, oldValue, index) = _timelineNameDeleteSnapshot[k];
                    timeline.SourceSheetName = oldValue;
                    ctx.Workbook.Timelines.Insert(Math.Min(index, ctx.Workbook.Timelines.Count), timeline);
                }

            if (_chartPivotSourceNameDeleteSnapshot is not null)
                foreach (var (chart, oldValue) in _chartPivotSourceNameDeleteSnapshot)
                    chart.PivotSourceSheetName = oldValue;

            // R95 restore: hyperlink bookmarks/targets rewritten to #REF!
            if (_hyperlinkBookmarkDeleteSnapshot is not null)
            {
                foreach (var (sheetId, addr, oldBookmark) in _hyperlinkBookmarkDeleteSnapshot)
                {
                    var sh = ctx.Workbook.GetSheet(sheetId);
                    if (sh is null) continue;
                    if (sh.HyperlinkMetadata.TryGetValue(addr, out var meta))
                        sh.HyperlinkMetadata[addr] = meta with { Bookmark = oldBookmark };
                }
            }

            if (_hyperlinkTargetDeleteSnapshot is not null)
            {
                foreach (var (sheetId, addr, oldTarget) in _hyperlinkTargetDeleteSnapshot)
                {
                    var sh = ctx.Workbook.GetSheet(sheetId);
                    if (sh is null) continue;
                    if (sh.Hyperlinks.ContainsKey(addr))
                        sh.Hyperlinks[addr] = oldTarget;
                }
            }

            // R107 restore: drawing-object hyperlinks rewritten to #REF! by the Apply-side pass
            // above (mirrors RenameSheetCommand's equivalent restore).
            if (_drawingShapeHyperlinkDeleteSnapshot is not null)
                foreach (var (shape, savedDrawingObjectHyperlink) in _drawingShapeHyperlinkDeleteSnapshot)
                    shape.Hyperlink = savedDrawingObjectHyperlink;

            if (_textBoxHyperlinkDeleteSnapshot is not null)
                foreach (var (textBox, savedDrawingObjectHyperlink) in _textBoxHyperlinkDeleteSnapshot)
                    textBox.Hyperlink = savedDrawingObjectHyperlink;

            if (_pictureHyperlinkDeleteSnapshot is not null)
                foreach (var (pic, savedDrawingObjectHyperlink) in _pictureHyperlinkDeleteSnapshot)
                    pic.Hyperlink = savedDrawingObjectHyperlink;

            if (_chartHyperlinkDeleteSnapshot is not null)
                foreach (var (chart, savedDrawingObjectHyperlink) in _chartHyperlinkDeleteSnapshot)
                    chart.Hyperlink = savedDrawingObjectHyperlink;
        }
    }

    /// <summary>
    /// R96: attempts to render the CURRENT (pre-delete) records of a WorksheetRange/Table pivot
    /// cache into the same &lt;pivotCacheRecords&gt; XML shape XlsxPivotTableWriter.Cache.cs's
    /// ToPivotCacheRecordsXml/ToPivotCacheRecordValueXml produce, so it can be stashed into
    /// <see cref="PivotCacheModel.RawRecordsXml"/> before the cache's SourceSheetName is nulled out
    /// below. That writer's preserved-records fallback (TryGetPreservedPivotCacheRecordsXml) already
    /// exists for External/Consolidation/Scenario cache sources, whose live worksheet range can
    /// never be resolved in the first place -- this reuses the exact same fallback for a
    /// WorksheetRange/Table cache that COULD normally resolve a live range, but is about to lose the
    /// ability to because its source sheet is being deleted here.
    /// </summary>
    private static bool TryCapturePivotCacheRecordsXml(PivotCacheModel cache, Sheet sourceSheet, out string? recordsXml)
    {
        recordsXml = null;

        // Mirrors TryGetPivotCacheSourceRange's own early-out: these source types never resolve a
        // live worksheet range, so there's nothing here for RawRecordsXml to gain that the reader
        // (XlsxPivotCacheReader) wouldn't already have captured at load time.
        if (cache.SourceType is PivotCacheSourceType.External or PivotCacheSourceType.Consolidation or PivotCacheSourceType.Scenario)
            return false;

        if (string.IsNullOrWhiteSpace(cache.SourceReference) || cache.Fields.Count == 0)
            return false;

        GridRange sourceRange;
        try
        {
            sourceRange = GridRange.Parse(NormalizePivotCacheSourceReference(cache.SourceReference), sourceSheet.Id);
        }
        catch (FormatException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }

        if (sourceRange.RowCount <= 1)
            return false; // header row only (or empty range) -- no cached records to lose

        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var fieldCount = Math.Min(cache.Fields.Count, (int)sourceRange.ColCount);
        var records = new List<XElement>();
        for (var row = sourceRange.Start.Row + 1; row <= sourceRange.End.Row; row++)
        {
            var values = new List<XElement>(fieldCount);
            for (var index = 0; index < fieldCount; index++)
            {
                var col = sourceRange.Start.Col + (uint)index;
                values.Add(ToPivotCacheRecordValueXml(sourceSheet.GetValue(row, col), ns));
            }

            records.Add(new XElement(ns + "r", values));
        }

        if (records.Count == 0)
            return false;

        recordsXml = new XElement(
            ns + "pivotCacheRecords",
            new XAttribute("count", records.Count.ToString(CultureInfo.InvariantCulture)),
            records).ToString(SaveOptions.DisableFormatting);
        return true;
    }

    // Mirrors XlsxPivotTableWriter.Cache.cs's NormalizePivotCacheSourceReference: per
    // CT_WorksheetSource (ECMA-376 18.10.2.42) the @ref attribute is an unqualified range like
    // "A1:C10", but defensively strip a sheet-qualifier/$ signs in case one ever creeps in here too.
    private static string NormalizePivotCacheSourceReference(string reference)
    {
        var normalized = reference.Trim();
        var sheetSeparator = normalized.LastIndexOf('!');
        if (sheetSeparator >= 0 && sheetSeparator + 1 < normalized.Length)
            normalized = normalized[(sheetSeparator + 1)..];

        return normalized.Replace("$", "", StringComparison.Ordinal);
    }

    // Mirrors XlsxPivotTableWriter.Cache.cs's ToPivotCacheRecordValueXml exactly (same element
    // names/attribute shape) so a records XML captured here round-trips identically through the
    // writer's TryGetPreservedPivotCacheRecordsXml fallback.
    private static XElement ToPivotCacheRecordValueXml(ScalarValue value, XNamespace ns) =>
        value switch
        {
            TextValue text => new XElement(ns + "s", new XAttribute("v", text.Value)),
            NumberValue number => new XElement(ns + "n", new XAttribute("v", number.Value.ToString(CultureInfo.InvariantCulture))),
            DateTimeValue date => new XElement(ns + "d", new XAttribute("v", date.ToDateTime().ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture))),
            BoolValue boolean => new XElement(ns + "b", new XAttribute("v", boolean.Value ? "1" : "0")),
            ErrorValue error => new XElement(ns + "e", new XAttribute("v", error.Code)),
            _ => new XElement(ns + "m")
        };

    private static Dictionary<string, string> RewriteNamedFormulasForDeletedSheet(
        Workbook workbook, string deletedSheetName, IReadOnlyList<string> deletedTableNames)
    {
        Dictionary<string, string>? snapshot = null;
        // DeleteSheetOp only matches sheet-qualified references, so the host sheet name is
        // irrelevant here — any surviving sheet name (or the deleted one) is fine.
        var hostSheetName = workbook.Sheets.Count > 0 ? workbook.Sheets[0].Name : deletedSheetName;

        foreach (var name in workbook.NamedFormulas.Keys.ToList())
        {
            var original = workbook.NamedFormulas[name];
            var rewritten = FormulaRewriter.Rewrite(
                original, new DeleteSheetOp(deletedSheetName, deletedTableNames), hostSheetName);
            if (rewritten is null || rewritten == original)
                continue; // null = no change or unparseable; leave the original untouched

            (snapshot ??= [])[name] = original;
            workbook.NamedFormulas[name] = rewritten;
        }

        return snapshot ?? [];
    }

    /// <summary>
    /// N3: rewrites sheet-scoped named formulas living on surviving sheets whose text
    /// references the just-deleted sheet, producing #REF! — symmetric with the workbook-global
    /// pass in <see cref="RewriteNamedFormulasForDeletedSheet"/> and with the scoped named-RANGE
    /// handling already done via <c>RowColumnShiftHelpers.RewriteAllFormulas</c>. Must run after
    /// <c>Workbook.RemoveSheet</c> has purged the deleted sheet's own scoped formulas, since this
    /// only walks what's left (<see cref="Workbook.ScopedNamedFormulas"/> is keyed by the OWNING
    /// sheet, not by which sheets the formula text references).
    /// </summary>
    private static Dictionary<(string Name, SheetId Sheet), string> RewriteScopedNamedFormulasForDeletedSheet(
        Workbook workbook, string deletedSheetName, IReadOnlyList<string> deletedTableNames)
    {
        Dictionary<(string Name, SheetId Sheet), string>? snapshot = null;
        // DeleteSheetOp only matches sheet-qualified references, so the host sheet name passed to
        // the rewriter is irrelevant to whether a match is found — but pass the scope-owning
        // sheet's own name for consistency with RowColumnShiftHelpers.RewriteNamedFormulas.
        foreach (var ((name, sheetId), original) in workbook.ScopedNamedFormulas.ToList())
        {
            var sheet = workbook.GetSheet(sheetId);
            var hostSheetName = sheet?.Name ?? string.Empty;
            var rewritten = FormulaRewriter.Rewrite(
                original, new DeleteSheetOp(deletedSheetName, deletedTableNames), hostSheetName);
            if (rewritten is null || rewritten == original)
                continue; // null = no change or unparseable; leave the original untouched

            (snapshot ??= [])[(name, sheetId)] = original;
            workbook.DefineNamedFormula(name, rewritten, sheetId);
        }

        return snapshot ?? [];
    }

    private static void RestoreNamedFormulas(Workbook workbook, Dictionary<string, string>? snapshot)
    {
        if (snapshot is null)
            return;

        foreach (var (name, original) in snapshot)
            workbook.NamedFormulas[name] = original;
    }

    private static void RestoreScopedNamedFormulas(
        Workbook workbook,
        Dictionary<(string Name, SheetId Sheet), string>? snapshot)
    {
        if (snapshot is null)
            return;

        foreach (var ((name, sheetId), formulaText) in snapshot)
            workbook.DefineNamedFormula(name, formulaText, sheetId);
    }
}

/// <summary>Command to move a sheet tab from one workbook position to another.</summary>
public sealed class MoveSheetCommand : IWorkbookCommand, IWholeWorkbookRecalcCommand
{
    private readonly int _fromIndex;
    private readonly int _toIndex;
    private bool _applied;

    public string Label => "Move Sheet";

    public MoveSheetCommand(int fromIndex, int toIndex)
    {
        _fromIndex = fromIndex;
        _toIndex = toIndex;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        if (CommandGuards.RejectIfWorkbookStructureProtected(ctx.Workbook) is { } protectedOutcome)
            return protectedOutcome;

        if (!IsValidIndex(ctx.Workbook, _fromIndex) || !IsValidIndex(ctx.Workbook, _toIndex))
            return new CommandOutcome(false, "Sheet index is out of range.");

        if (_fromIndex == _toIndex)
            return new CommandOutcome(true, IsNoOp: true);

        // R139-workbook-protection: an individually-protected sheet must refuse Move of its own
        // tab even when the workbook's structure is not protected -- see RenameSheetCommand's
        // matching comment above. Checked after the no-op short-circuit so dragging a protected
        // tab to its own position (a true no-op) is not rejected.
        if (CommandGuards.RejectIfProtected(ctx.Workbook.Sheets[_fromIndex]) is { } sheetProtectedOutcome)
            return sheetProtectedOutcome;

        ctx.Workbook.MoveSheet(_fromIndex, _toIndex);
        _applied = true;
        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        if (!_applied || _fromIndex == _toIndex)
            return;

        ctx.Workbook.MoveSheet(_toIndex, _fromIndex);
        _applied = false;
    }

    private static bool IsValidIndex(Workbook workbook, int index) =>
        index >= 0 && index < workbook.Sheets.Count;
}

/// <summary>Command to hide or unhide a worksheet.</summary>
public sealed class SetSheetHiddenCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly bool _hidden;
    private bool? _previousHidden;

    public string Label => _hidden ? "Hide Sheet" : "Unhide Sheet";

    public SetSheetHiddenCommand(SheetId sheetId, bool hidden)
    {
        _sheetId = sheetId;
        _hidden = hidden;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        if (CommandGuards.RejectIfWorkbookStructureProtected(ctx.Workbook) is { } protectedOutcome)
            return protectedOutcome;

        var sheet = ctx.GetSheet(_sheetId);
        // R139-workbook-protection: an individually-protected sheet must refuse being Hidden even
        // when the workbook's structure is not protected -- see RenameSheetCommand's matching
        // comment above. Unhide (_hidden == false) is deliberately left ungated: revealing an
        // already-hidden sheet does not alter its protection state and real Excel's Unhide dialog
        // operates at the workbook level, not gated by the target sheet's own protection.
        if (_hidden && CommandGuards.RejectIfProtected(sheet) is { } sheetProtectedOutcome)
            return sheetProtectedOutcome;

        if (_hidden && !ctx.Workbook.Sheets.Any(s => s.Id != _sheetId && !s.IsHidden && !s.IsVeryHidden))
            return new CommandOutcome(false, "Cannot hide the only visible sheet.");

        _previousHidden = sheet.IsHidden;
        sheet.IsHidden = _hidden;
        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_previousHidden is null)
            return;

        ctx.GetSheet(_sheetId).IsHidden = _previousHidden.Value;
    }
}

/// <summary>Command to set or clear a worksheet tab color.</summary>
public sealed class SetSheetTabColorCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly CellColor? _color;
    private CellColor? _previousColor;
    private bool _hadPreviousColor;

    public string Label => "Set Sheet Tab Color";

    public SetSheetTabColorCommand(SheetId sheetId, CellColor? color)
    {
        _sheetId = sheetId;
        _color = color;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        if (CommandGuards.RejectIfWorkbookStructureProtected(ctx.Workbook) is { } protectedOutcome)
            return protectedOutcome;

        var sheet = ctx.GetSheet(_sheetId);
        _previousColor = sheet.TabColor;
        _hadPreviousColor = sheet.TabColor.HasValue;
        sheet.TabColor = _color;
        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        ctx.GetSheet(_sheetId).TabColor = _hadPreviousColor ? _previousColor : null;
    }
}
