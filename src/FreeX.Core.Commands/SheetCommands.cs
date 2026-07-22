using FreeX.Core.Formula;
using FreeX.Core.Model;

namespace FreeX.Core.Commands;

/// <summary>Command to add a new sheet to the workbook.</summary>
public sealed class AddSheetCommand : IWorkbookCommand
{
    private readonly string _name;
    private SheetId? _addedSheetId;

    public string Label => $"Add Sheet '{_name}'";

    public AddSheetCommand(string name) => _name = name;

    public CommandOutcome Apply(ICommandContext ctx)
    {
        if (CommandGuards.RejectIfWorkbookStructureProtected(ctx.Workbook) is { } protectedOutcome)
            return protectedOutcome;

        var validationError = ctx.Workbook.ValidateSheetName(_name);
        if (validationError is not null)
            return new CommandOutcome(false, validationError);

        Sheet sheet;
        if (_addedSheetId is { } existingId)
        {
            // R16: redo. Workbook.AddSheet always mints a brand-new SheetId, which would give
            // the re-created sheet a DIFFERENT id than the first Apply produced — breaking any
            // later redo-stack command that captured the original id. Re-create with the SAME
            // id captured below instead, via the "reinsert an existing sheet instance" overload.
            sheet = new Sheet(existingId, _name);
            ctx.Workbook.InsertSheet(ctx.Workbook.Sheets.Count, sheet);
        }
        else
        {
            sheet = ctx.Workbook.AddSheet(_name);
            _addedSheetId = sheet.Id;
        }
        sheet.ResetViewStateToA1();
        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_addedSheetId.HasValue)
            ctx.Workbook.RemoveSheet(_addedSheetId.Value);
    }
}

/// <summary>Command to rename a sheet.</summary>
public sealed class RenameSheetCommand : IWorkbookCommand
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

        _cfFormulaRenameSnapshot = [];
        _dvFormulaRenameSnapshot = [];
        foreach (var s in ctx.Workbook.Sheets)
        {
            bool sheetCfChanged = false;
            foreach (var cf in s.ConditionalFormats)
            {
                if (cf.FormulaText is { } ft)
                {
                    var rewritten = FormulaRewriter.Rewrite(ft, renameOp, s.Name);
                    if (rewritten is not null && rewritten != ft)
                    {
                        _cfFormulaRenameSnapshot.Add((cf.Id, ft, s.Id));
                        cf.FormulaText = rewritten;
                        sheetCfChanged = true;
                    }
                }
            }
            // The CF viewport context cache is keyed on (sheet.Id, sheet.ContentVersion,
            // sheet.ConditionalFormats.Version) and caches a precompiled AST per CF object
            // reference, so mutating cf.FormulaText in place above never invalidates it —
            // bump Version explicitly so a stale cache hit doesn't keep evaluating the old
            // sheet name after the rename.
            if (sheetCfChanged)
                s.ConditionalFormats.NotifyRulesChanged();
            foreach (var dv in s.DataValidations)
            {
                if (dv.Formula1 is { } f1)
                {
                    var rewritten = FormulaRewriter.Rewrite(f1, renameOp, s.Name);
                    if (rewritten is not null && rewritten != f1)
                    {
                        _dvFormulaRenameSnapshot.Add((dv.Id, 1, f1, s.Id));
                        dv.Formula1 = rewritten;
                    }
                }
                if (dv.Formula2 is { } f2)
                {
                    var rewritten = FormulaRewriter.Rewrite(f2, renameOp, s.Name);
                    if (rewritten is not null && rewritten != f2)
                    {
                        _dvFormulaRenameSnapshot.Add((dv.Id, 2, f2, s.Id));
                        dv.Formula2 = rewritten;
                    }
                }
            }
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

            // T7 restore: CF/DV formula text
            if (_cfFormulaRenameSnapshot is not null)
            {
                foreach (var (ruleId, oldValue, sheetId) in _cfFormulaRenameSnapshot)
                {
                    var sh = ctx.Workbook.GetSheet(sheetId);
                    if (sh is null) continue;
                    foreach (var cf in sh.ConditionalFormats)
                        if (cf.Id == ruleId) { cf.FormulaText = oldValue; break; }
                }
            }

            if (_dvFormulaRenameSnapshot is not null)
            {
                foreach (var (ruleId, slot, oldValue, sheetId) in _dvFormulaRenameSnapshot)
                {
                    var sh = ctx.Workbook.GetSheet(sheetId);
                    if (sh is null) continue;
                    foreach (var dv in sh.DataValidations)
                    {
                        if (dv.Id != ruleId) continue;
                        if (slot == 1) dv.Formula1 = oldValue;
                        else           dv.Formula2 = oldValue;
                        break;
                    }
                }
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
        }
    }
}

/// <summary>Command to delete a sheet from the workbook.</summary>
public sealed class RemoveSheetCommand : IWorkbookCommand
{
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
    // String sheet-name refs on model objects that named the deleted sheet — cleared so no
    // dangling deleted-sheet reference remains (mirrors RenameSheetCommand's T6 block, but the
    // sheet has no new name to rewrite onto, so these are nulled instead of renamed).
    private List<(PivotCacheModel Cache, string OldValue)>? _pivotCacheNameDeleteSnapshot;
    private List<(SlicerModel Slicer, string OldValue)>? _slicerNameDeleteSnapshot;
    // P84: mirrors _slicerNameDeleteSnapshot above for TimelineModel.SourceSheetName — a timeline
    // anchored on the deleted sheet must have its dangling sheet-name ref cleared too, or it can
    // silently reattach to an unrelated sheet later re-created/renamed with the same name.
    private List<(TimelineModel Timeline, string OldValue)>? _timelineNameDeleteSnapshot;
    private List<(PictureModel Picture, string OldValue)>? _pictureNameDeleteSnapshot;
    // K16: chart verbatim series/data-label formulas that reference the deleted sheet must
    // become #REF! just like ordinary cell/CF/DV formulas do via DeleteSheetOp — otherwise
    // they keep dangling text naming a sheet that no longer exists.
    private List<RowColumnShiftHelpers.ChartVerbatimWorkbookSnapshot>? _chartVerbatimDeleteSnapshot;

    public string Label => "Delete Sheet";

    public RemoveSheetCommand(SheetId sheetId) => _sheetId = sheetId;

    public CommandOutcome Apply(ICommandContext ctx)
    {
        if (CommandGuards.RejectIfWorkbookStructureProtected(ctx.Workbook) is { } protectedOutcome)
            return protectedOutcome;

        if (ctx.Workbook.Sheets.Count <= 1)
            return new CommandOutcome(false, "Cannot delete the only sheet.");

        var sheet = ctx.GetSheet(_sheetId);
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

        // X3: rewrite CF FormulaText and DV Formula1/Formula2 on all surviving sheets
        // that reference the deleted sheet, producing #REF! — mirrors RenameSheetCommand T7.
        var deleteOp = new DeleteSheetOp(deletedSheetName, deletedTableNames);
        _cfFormulaDeleteSnapshot = [];
        _dvFormulaDeleteSnapshot = [];
        foreach (var s in ctx.Workbook.Sheets)
        {
            bool sheetCfChanged = false;
            foreach (var cf in s.ConditionalFormats)
            {
                if (cf.FormulaText is { } ft)
                {
                    var rewritten = FormulaRewriter.Rewrite(ft, deleteOp, s.Name);
                    if (rewritten is not null && rewritten != ft)
                    {
                        _cfFormulaDeleteSnapshot.Add((cf.Id, ft, s.Id));
                        cf.FormulaText = rewritten;
                        sheetCfChanged = true;
                    }
                }
            }
            // See RenameSheetCommand's T7 pass: mutating cf.FormulaText in place never
            // invalidates the (sheet.Id, ContentVersion, ConditionalFormats.Version)-keyed CF
            // viewport cache on its own, so bump Version explicitly for surviving sheets whose
            // rules were rewritten to #REF!.
            if (sheetCfChanged)
                s.ConditionalFormats.NotifyRulesChanged();
            foreach (var dv in s.DataValidations)
            {
                if (dv.Formula1 is { } f1)
                {
                    var rewritten = FormulaRewriter.Rewrite(f1, deleteOp, s.Name);
                    if (rewritten is not null && rewritten != f1)
                    {
                        _dvFormulaDeleteSnapshot.Add((dv.Id, 1, f1, s.Id));
                        dv.Formula1 = rewritten;
                    }
                }
                if (dv.Formula2 is { } f2)
                {
                    var rewritten = FormulaRewriter.Rewrite(f2, deleteOp, s.Name);
                    if (rewritten is not null && rewritten != f2)
                    {
                        _dvFormulaDeleteSnapshot.Add((dv.Id, 2, f2, s.Id));
                        dv.Formula2 = rewritten;
                    }
                }
            }
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

        // String sheet-name refs (PivotCacheModel.SourceSheetName / SlicerModel.SourceSheetName /
        // PictureModel.LinkedSourceSheetName) that named the deleted sheet: clear them so they can
        // never silently reattach to an unrelated sheet later re-created/renamed with the same name
        // — mirrors RenameSheetCommand's T6 block, which uses the same three fields.
        _pivotCacheNameDeleteSnapshot = [];
        foreach (var cache in ctx.Workbook.PivotCaches)
        {
            if (cache.SourceSheetName is not null &&
                string.Equals(cache.SourceSheetName, deletedSheetName, StringComparison.OrdinalIgnoreCase))
            {
                _pivotCacheNameDeleteSnapshot.Add((cache, cache.SourceSheetName));
                cache.SourceSheetName = null;
            }
        }

        _slicerNameDeleteSnapshot = [];
        foreach (var slicer in ctx.Workbook.Slicers)
        {
            if (slicer.SourceSheetName is not null &&
                string.Equals(slicer.SourceSheetName, deletedSheetName, StringComparison.OrdinalIgnoreCase))
            {
                _slicerNameDeleteSnapshot.Add((slicer, slicer.SourceSheetName));
                slicer.SourceSheetName = null;
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

        _timelineNameDeleteSnapshot = [];
        foreach (var timeline in ctx.Workbook.Timelines)
        {
            if (timeline.SourceSheetName is not null &&
                string.Equals(timeline.SourceSheetName, deletedSheetName, StringComparison.OrdinalIgnoreCase))
            {
                _timelineNameDeleteSnapshot.Add((timeline, timeline.SourceSheetName));
                timeline.SourceSheetName = null;
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

            // X3 restore: CF/DV formula text rewritten to #REF! must be restored
            if (_cfFormulaDeleteSnapshot is not null)
            {
                foreach (var (ruleId, oldValue, sheetId) in _cfFormulaDeleteSnapshot)
                {
                    var sh = ctx.Workbook.GetSheet(sheetId);
                    if (sh is null) continue;
                    foreach (var cf in sh.ConditionalFormats)
                        if (cf.Id == ruleId) { cf.FormulaText = oldValue; break; }
                }
            }
            if (_dvFormulaDeleteSnapshot is not null)
            {
                foreach (var (ruleId, slot, oldValue, sheetId) in _dvFormulaDeleteSnapshot)
                {
                    var sh = ctx.Workbook.GetSheet(sheetId);
                    if (sh is null) continue;
                    foreach (var dv in sh.DataValidations)
                    {
                        if (dv.Id != ruleId) continue;
                        if (slot == 1) dv.Formula1 = oldValue;
                        else           dv.Formula2 = oldValue;
                        break;
                    }
                }
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

            if (_pivotCacheNameDeleteSnapshot is not null)
                foreach (var (cache, oldValue) in _pivotCacheNameDeleteSnapshot)
                    cache.SourceSheetName = oldValue;

            if (_slicerNameDeleteSnapshot is not null)
                foreach (var (slicer, oldValue) in _slicerNameDeleteSnapshot)
                    slicer.SourceSheetName = oldValue;

            if (_pictureNameDeleteSnapshot is not null)
                foreach (var (pic, oldValue) in _pictureNameDeleteSnapshot)
                    pic.LinkedSourceSheetName = oldValue;

            if (_timelineNameDeleteSnapshot is not null)
                foreach (var (timeline, oldValue) in _timelineNameDeleteSnapshot)
                    timeline.SourceSheetName = oldValue;
        }
    }

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
            var sheet = workbook.Sheets.FirstOrDefault(s => s.Id == sheetId);
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
public sealed class MoveSheetCommand : IWorkbookCommand
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
