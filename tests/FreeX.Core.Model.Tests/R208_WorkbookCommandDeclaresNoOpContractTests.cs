using System.Text.RegularExpressions;
using FluentAssertions;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// r208: the FreeX twin of r202's FreeP contract and r203's FreeW contract -- every command must
/// DECLARE whether it can be invoked where it changes nothing.
/// <para>
/// FreeX signals this differently from its sister apps. There is no <c>HasEffect</c> to override:
/// <c>IWorkbookCommand.Apply</c> returns a <c>CommandOutcome</c>, and a command that changed nothing
/// should return one with <c>IsNoOp: true</c>. <c>CommandBus.Execute</c> then skips the push, and
/// skipping matters because <c>UndoRedoStack.Push</c> CLEARS REDO.
/// </para>
/// <para>
/// That difference forces a different check, and the limitation is worth stating plainly: a return
/// VALUE is invisible to reflection, so this contract reads the SOURCE. It asks whether a command's
/// own class body ever mentions <c>IsNoOp</c>. That is weaker than the sister contracts -- it cannot
/// tell a correct IsNoOp from a wrong one, only present from absent -- and it is the strongest thing
/// available without a bus-level or analyzer-level change.
/// </para>
/// <para>
/// Scope: EVERY IWorkbookCommand, as of r217. It began at the setter-shaped ones (Set*, Apply*,
/// Toggle*) because r207 measured that shape at ~90% defective in FreeW against ~21% for structural
/// commands, so it is where the class lives densest. But "we looked where it was densest" was being
/// read off as "we looked", and the rest of the population was invisible to the accounting rather
/// than judged clean. r217 widened the scan to all of them and put the remainder in
/// <see cref="NeverExaminedForThisClass"/> -- a list that asserts nothing about those commands
/// except that nobody has yet decided. That is the honest state, and unlike silence it is counted,
/// capped, and cannot absorb a newly written command.
/// </para>
/// </summary>
public sealed class R208_WorkbookCommandDeclaresNoOpContractTests
{
    /// <summary>Commands the r208 census judged sound, with its reason.</summary>
    private static readonly Dictionary<string, string> DeliberatelyNeverReportsNoOp = new()
    {
        ["ApplyThreadedCommentChangesCommand"] =
            "returns Success:false when nothing changed, which the bus excludes from the stack just "
            + "as effectively as IsNoOp would",
        ["SetFormulaErrorCheckingRuleCommand"] =
            "the planner builds one only for rules whose enabled state actually flipped",
        ["SetFormulaErrorIgnoredCommand"] =
            "only ever issued with ignored:true, and the auditing service filters out cells that are "
            + "already ignored, so the same cell cannot be re-ignored",
        ["SetIterativeCalculationOptionsCommand"] =
            "the planner returns IsNoOp and the session returns early WITHOUT constructing it",
        ["SetPrintTitlesCommand"] =
            "no production caller: the Page Setup dialog routes print titles through SetPageSetupCommand",
        ["SetRowOutlineGroupCollapsedCommand"] = "the caller passes !group.IsCollapsed -- a negation gate",
        ["SetSheetHiddenCommand"] =
            "UnhideSheet returns early for a visible sheet, and a hidden sheet cannot be selected to hide",
        ["SetTextBoxTextCommand"] = "both callers compare the edited text against the original first",
        ["SetThreadedCommentCommand"] = "only built for genuine New Comment creation",
        ["SetTimelineGranularityCommand"] = "the caller only issues a granularity that differs",
        ["SetWaterfallTotalPointCommand"] = "the caller passes the negation of the point's current total flag",
        ["SetWorksheetOutlineSettingsCommand"] = "the caller compares the requested settings first",
        ["SetWorksheetOutlineSymbolsCommand"] =
            "WorkbookSession.SetShowOutlineSymbols returns SuccessfulNoOpEditResult before executing "
            + "when the effective value already matches (r199)",
        ["SetWorksheetShowFormulasCommand"] =
            "WorkbookSession.SetShowFormulas returns a no-op result before executing when the "
            + "effective value already matches",
        ["SetWorksheetViewModeCommand"] =
            "WorkbookSession.SetWorksheetViewMode returns a no-op result when every target sheet "
            + "already has the mode",
        ["SetWorksheetViewOptionsCommand"] = "the session gates each toggle on its effective value",
        ["SetWorksheetZoomCommand"] =
            "WorkbookSession.SetZoomPercent clamps then returns a no-op result when every target "
            + "sheet already has that zoom",
        // r220. The protection family is gated at the planner, which is the structural defence r207
        // preferred over an override: ProtectionWorkflowSession branches on the CURRENT state --
        // protected sheets get Unprotect, unprotected ones get Protect -- so neither command can be
        // issued against a target already in the state it would produce.
        ["ProtectSheetCommand"] =
            "ProtectionWorkflowSession.CreateSheetCommandPlan issues this only when the sheet is NOT "
            + "protected; a protected one gets UnprotectSheetCommand instead -- a negation gate",
        ["ProtectWorkbookCommand"] =
            "CreateWorkbookCommandPlan issues this only when the structure is NOT protected -- the "
            + "same negation gate as the sheet twin",
        ["UnprotectWorkbookCommand"] =
            "the other half of that gate: only issued when the structure IS protected",
        // r222, the Add/Create/Insert family. Every one of these was read, not assumed: in each,
        // the mutation that creates the object is unconditional once the guard checks above it have
        // passed, and every guard that can fail returns Success:false rather than a quiet success.
        // There is no path that reaches the add and skips it. The three commands in this family that
        // DO have a same-value path -- the two Define* and Create from Selection -- were fixed in
        // r222 instead of listed here.
        ["AddChartCommand"] = "sheet.Charts.Add is unconditional once the guards pass",
        ["AddChartSheetCommand"] = "always creates or re-adds the chart sheet",
        ["AddDrawingShapeCommand"] = "sheet.DrawingShapes.Add is unconditional",
        ["AddFormControlCommand"] = "sheet.FormControls.Add is unconditional",
        ["AddPivotChartCommand"] = "builds a ChartModel and adds it unconditionally",
        ["AddPivotTableCommand"] = "builds cache and table and adds them unconditionally",
        ["AddPivotTableToNewWorksheetCommand"] =
            "delegates to AddPivotTableCommand after creating the sheet; both always add",
        ["AddSheetCommand"] = "always inserts a sheet (the existingId path is redo re-using an id)",
        ["AddSlicerCommand"] = "Workbook.Slicers.Add is unconditional",
        ["AddSparklineCommand"] = "sheet.Sparklines.Add is unconditional",
        ["AddTextBoxCommand"] = "sheet.TextBoxes.Add is unconditional",
        ["AddThreadedCommentReplyCommand"] =
            "returns ThreadedCommentNotFound when there is no thread, and otherwise always appends "
            + "the reply -- an empty reply is still a reply",
        ["AddTimelineCommand"] = "Workbook.Timelines.Add is unconditional",
        ["CreateStructuredTableCommand"] = "always constructs and inserts a StructuredTableModel",
        ["CreateStyledStructuredTableCommand"] =
            "delegates to CreateStructuredTableCommand, which always inserts",
        ["InsertCellsCommand"] = "always runs a shift operation over the requested region",
        ["InsertColumnsCommand"] = "always runs the column shift",
        ["InsertPictureCommand"] = "sheet.Pictures.Add is unconditional",
        ["InsertRowsCommand"] = "always runs the row shift",
        // r224, the Delete/Remove family. The pattern that makes these sound is uniform and was
        // checked in each: the target is looked up first, a miss returns Success:false, and the
        // removal below that check is unconditional. A command that cannot find what it was asked
        // to delete reports an error rather than a quiet success, which keeps it off the undo stack
        // just as effectively.
        // r225: the three Nudge siblings that do NOT clamp. They add the delta straight to an anchor
        // offset with no floor, and BuildNudgeCommand is documented as the arrow-key entry point, so
        // the delta is never zero and the object always moves. NudgeChartCommand is the odd one out
        // -- it clamps with Math.Max(0, ...) and so saturates at the edge -- and was fixed in r225.
        ["NudgePictureCommand"] = "unclamped += on the anchor offset, from a non-zero arrow-key delta",
        ["NudgeDrawingShapeCommand"] = "unclamped += on the anchor offset, same as the picture twin",
        ["NudgeTextBoxCommand"] = "unclamped += on the anchor offset, same as the picture twin",
        ["MoveChartToNewSheetCommand"] =
            "always creates a new chart sheet and moves the chart onto it; there is no same-place "
            + "path, unlike its MoveChartCommand sibling",
        ["DeleteCellsCommand"] = "always runs the shift once the range and direction validate",
        ["DeleteColumnsCommand"] = "always removes the requested columns",
        ["DeleteCommentCommand"] = "removes after a found-check that errors on a miss",
        ["DeleteCustomViewCommand"] = "RemoveAt after an index lookup that errors on a miss",
        ["DeleteDrawingObjectCommand"] =
            "dispatches per object kind; each branch does a FindIndex and errors on a miss before "
            + "removing, and an unsupported kind errors too",
        ["DeleteScenarioCommand"] = "RemoveAt after an index lookup that errors on a miss",
        ["DeleteThreadedCommentCommand"] = "removes after a found-check that errors on a miss",
        ["DeleteThreadedCommentReplyCommand"] = "RemoveAt after a reply-index check",
        ["RemoveAllowEditRangeCommand"] = "RemoveAt after an index lookup that errors on a miss",
        ["RemoveChartSeriesCommand"] =
            "errors for a pivot chart, an unsupported chart type, and a chart with no series to "
            + "remove, so every path that reaches the removal has one to remove",
        ["RemoveNamedRangeCommand"] =
            "every branch either removes a range or a formula, or returns Success:false because the "
            + "name does not exist -- all four exits were read",
        ["RemoveSheetCommand"] = "always removes the sheet once the guards pass",
        ["PasteRangeAsPictureCommand"] =
            "r221: the picture is built by the caller and handed in ready-made, and Apply's only "
            + "mutation is to add it -- there is no path that reaches the add and skips it",
        ["ClearSparklineCommand"] =
            "returns Success:false when the sparkline is not found, and otherwise always removes one "
            + "-- there is no path where it finds the target and leaves it in place",
        // r228: two genuine toggles, sound for the same structural reason as
        // ToggleWorksheetAutoFilterCommand below -- each reads the CURRENT state and flips it, so
        // there is no same-value path to guard. This is the self-guaranteeing shape r207 preferred,
        // and it is worth distinguishing from the equal-value setters in the same files: those take
        // the target state as an argument and can be handed the one already in place.
        // r232, the structural block. Each read: every one errors out on degenerate input and then
        // does real structural work -- there is no path that reaches the mutation and skips it.
        ["ConsolidateCommand"] =
            "errors for no source ranges, an out-of-workbook destination or source, and out-of-bounds "
            + "addresses; past those it writes consolidated values into the destination",
        ["ConvertStructuredTableToRangeCommand"] = "removes the table once it resolves",
        ["ForecastSheetCommand"] = "always adds a forecast sheet",
        ["ScenarioSummaryReportCommand"] = "always adds a report sheet",
        ["DuplicateSheetCommand"] = "always inserts the duplicate once the guards pass",
        ["DuplicateDrawingObjectCommand"] = "always adds the clone for each supported object kind",
        ["ImportSheetCommand"] = "always writes the imported cells",
        ["OneVariableDataTableCommand"] = "always writes the data-table body",
        ["TwoVariableDataTableCommand"] = "always writes the data-table body",
        ["DeleteRowsCommand"] = "always removes the requested rows",
        ["CopyRangeCommand"] = "always writes the copied cells into the destination",
        ["RejectedWorkbookCommand"] =
            "trivially: its whole Apply is `new CommandOutcome(false, errorMessage)` -- a rejection "
            + "sentinel that never succeeds, so it never reaches the stack at all",
        ["ConvertNotesToCommentsCommand"] =
            "r231: returns Success:false with \"All notes already have threaded comments -- nothing "
            + "to convert\" when the loop converts none, so the run-it-twice case is already covered "
            + "-- this one was expected to be a fix and reading it said otherwise",
        ["DrillDownPivotTableCommand"] =
            "r231: errors for a disabled drill, a missing pivot table and an empty detail set; every "
            + "path past those adds a detail sheet",
        ["SubtotalCommand"] =
            "r229: errors for a range without a header row and at least one data row, and for "
            + "subtotal columns outside the range; every path past those inserts subtotal rows",
        ["ShowHideCommentCommand"] =
            "reads sheet.ShownComments.Contains(address) and flips it -- a toggle, not a setter",
        ["ShowAllNotesCommand"] =
            "computes allShown from the current state and then either shows every note or hides "
            + "every note; it also errors when the sheet has no notes at all",
        ["ToggleWorksheetAutoFilterCommand"] =
            "self-guaranteeing: Apply reads sheet.AutoFilter fresh and branches on it, so it either "
            + "creates or removes the filter -- there is no same-value path",
        // The r208 census claimed these could no-op; two verifiers each refuted it.
        ["SetAllowEditRangePasswordCommand"] = "refuted: the dialog only commits a changed password",
        ["SetCalculationModeCommand"] = "refuted: the ribbon gates on the current mode",
        ["SetChartBoundsCommand"] = "refuted: issued from a drag that has already moved the chart",
        ["SetPrintOptionsCommand"] = "refuted: the planner diffs the options before building it",
        ["SetScaleToFitCommand"] = "refuted: the planner diffs the scale settings before building it",
        ["SetSheetTabColorCommand"] = "refuted: the colour picker commits only a different colour",
    };

    /// <summary>
    /// Commands CONFIRMED to be invocable where they mutate nothing, and which do not report IsNoOp
    /// yet. Known defects, not unknowns -- the r208 entries were each checked by two independent
    /// verifiers, and entries added later carry their evidence in the round notes.
    /// </summary>
    private static readonly HashSet<string> KnownNoOpCapableNotYetFixed =
    [
        "ApplyCustomViewCommand",
        // r219, the pivot Configure family. Evidence is in the callers, not inference:
        // PivotApplicationSession.PlanFieldFilters passes `sorts ?? PivotTable.Sorts.ToList()` and
        // PlanFieldSort passes the pivot's own LabelFilters/ValueFilters straight back, so
        // re-applying the sort already in effect reaches Apply with every argument equal to current
        // state. Not fixed in r219 because each replaces collections and then runs RefreshGuarded:
        // deciding "no change" means also proving the re-render is unnecessary, and guessing at that
        // is how a guard ends up suppressing a real edit. ConfigurePivotTableOptionsCommand joins
        // them for a related reason: its Apply is a 25-field assignment block, and hand-listing that
        // many fields in a guard is precisely the brittle mirror r218 avoided -- it needs a
        // snapshot-versus-target comparison the way ConfigureSparklineCommand got one, not a
        // transcription that can fall out of step.
        // r220: ClearPivotTableView belongs to the same RefreshGuarded family for the same reason --
        // clearing filters that are already clear replaces empty collections with empty ones, but
        // deciding that means proving the re-render is unnecessary too.
        // r225: the AutoFilter family. Re-applying a filter that is already in effect -- clicking
        // the same colour swatch, re-confirming the same Top 10 -- recomputes the same hidden-row
        // set and writes back the same WorksheetAutoFilterColumnModel. Reachable and ordinary.
        // Not fixed here for the reason r221 made explicit about over-promising: each of these
        // touches BOTH the hidden-row set and the autofilter/structured-table column models, and a
        // guard covering only one of them would let the command declare IsNoOp while still being
        // wrong on the other. TopBottomFilterCommand already has one quiet-success path (count 0
        // with no owned rows) that would be easy to mark alone, and marking it alone is exactly the
        // partial fix that would take the command off this list without making it correct. They
        "AdvancedFilterCommand",
        "AverageFilterCommand",
        "CellFillColorFilterCommand",
        "CellFontColorFilterCommand",
        "CellNoFillColorFilterCommand",
        "FilterCommand",
        "FilterConditionCommand",
        "TopBottomFilterCommand",
        // need a snapshot-versus-target comparison over both models, the way r219's group does.
        // r225: the two structural moves with a same-destination path. MovePivotTableCommand with
        // _targetStart equal to the pivot's current start produces a movedRange equal to the old one
        // and then re-renders over the same cells; MoveRangeCommand with the destination equal to
        // the source start writes the same cells back. Both reachable by dragging something and
        // dropping it where it started. Same reason as the filters for not fixing them here: each
        "MovePivotTableCommand",
        "MoveRangeCommand",
        // r236 REFINES the r233 diagnosis for these, and for GroupedApplyStyleCommand below.
        // r234 built the cell comparison they were said to need, and it is not sufficient for them:
        // all three write COMPANION state as well -- hyperlinks, hyperlink metadata, rich-text runs
        // -- and keep it in SEPARATE parallel snapshot lists from their cell snapshot. So no single
        // snapshot answers "did anything change", and a comparison built on the cell list alone
        // would report unchanged for a fill that altered only a hyperlink. The remedy is to capture
        // CellEditCompanionSnapshot (which covers all four) instead of three parallel tuples -- a
        // change to how these commands hold their undo state, not a guard added to them.
        // Checked rather than assumed, and it cut both ways: the same look confirmed their UNDO is
        // complete, because those parallel snapshots do get restored.
        // r229: the two fill commands whose target list is never empty. Fill Down over cells that
        // already hold the value being filled, or autofilling a series back over itself, changes
        // nothing -- but unlike FlashFillCommand next door, these two validate a non-empty target
        // set and then write to all of it, so the post-hoc "did we write anything" test that fixed
        // the rest of this family would never fire here. Deciding them needs a value comparison per
        // cell, which is the same boundary r221 drew around the Paste guards and declined to cross
        // by guessing.
        // r231: the save/reapply/refresh group, each with its own reason for not being fixed here.
        //
        // SaveScenario and SaveCustomView replace an existing entry with a freshly captured one, so
        // saving twice with nothing changed in between writes an equal value. Both targets ARE
        // records, and the obvious guard is `newValue == previous` -- but both records carry LIST
        // members, which record equality compares by reference, so against a freshly built instance
        // it is always false. That guard would never fire while looking exactly like the ones that
        // do work. Same objection r229 raised against a post-hoc test on Autofill: a guard that
        // cannot fire is worse than an honest entry here, because it takes the command off this list.
        "SaveScenarioCommand",
        "SaveCustomViewCommand",
        // MergeCells over a range already merged exactly that way absorbs the existing region and
        // re-adds it, blanking cells that are already blank. Net effect nil, but establishing that
        // means reasoning through five loops rather than adding a guard.
        "MergeCellsCommand",
        // RefreshStructuredTableTotals rewrites every totals cell from the current data; when the
        // data has not changed it writes back what is there. Deciding needs a per-cell comparison.
        "RefreshStructuredTableTotalsCommand",
        // ReapplyStructuredTableStyle is a delegation case of a kind r223/r224 did not have: it
        // returns ApplyStructuredTableStyleCommand's outcome, and THAT command is on this list. So it
        // inherits the defect rather than a correct signal -- delegation propagates both -- and
        // fixing the inner command fixes this one for free. It is listed separately so the count
        // stays honest, not because it needs its own fix.
        "ReapplyStructuredTableStyleCommand",
        // r232: the last of the cell-writing commands, all held here by the same boundary r221 drew
        // and r229 restated. Each writes values into a target set that its guards have already
        // established is non-empty, so "did we write anything" is always yes; deciding whether the
        // written values DIFFER needs a comparison per cell. Pasting the same text over itself,
        // re-applying a style a range already has, re-importing an unchanged sheet, refreshing a
        // pivot or data table whose source has not moved, resizing a table to its current range --
        // all ordinary, all currently pushing an undo entry.
        "ApplyConditionalFormatCommand",
        "ChangePivotTableSourceCommand",
        "DataTableBodyRefreshCommand",
        "ExternalTextPasteSpecialCommand",
        "FormatPainterDataValidationCommand",
        "MergeScenarioCommand",
        "RefreshPivotTableCommand",
        "ResizeStructuredTableCommand",
        "SetDataValidationCommand",
        // FormControlInteractionCommand delegates its edit to _cellEdit.Apply -- an EditCellsCommand,
        // which is on this list -- so like ReapplyStructuredTableStyle in r231 it inherits the defect
        // rather than a correct signal. It also re-applies control state on redo, which the inner
        // command knows nothing about, so fixing EditCells alone will not settle this one.
        // needs a real before/after comparison, not a guard on the arguments.
        "ClearPivotTableViewCommand",
        // r221: the two Paste commands with no record of what they wrote. Both are no-op-capable --
        // pasting column widths onto columns that already have them, or validation rules identical
        // to the destination's -- but neither accumulates an `affected`/`_added` list the way its
        // eleven siblings do, so there is nothing exact to test after the loop. Deciding them needs
        // a before/after snapshot comparison, which is a change to how they work rather than a
        // guard bolted on, and is the honest reason they are here instead of fixed.
        "PasteColumnWidthsCommand",
        "PasteDataValidationCommand",
        "ConfigurePivotChartOptionsCommand",
        "ConfigurePivotTableCalculatedItemsCommand",
        "ConfigurePivotTableFieldFiltersCommand",
        "ConfigurePivotTableLayoutCommand",
        "ConfigurePivotTableOptionsCommand",
        "ConfigurePivotTableViewCommand",
        "ApplyStructuredTableStyleCommand",
        "ApplyStyleCommand",
        "SetColumnOutlineGroupCollapsedCommand",
        "SetColumnWidthCommand",
        "SetHeaderFooterCommand",
        "SetHyperlinkCommand",
        "SetPageSetupCommand",
        "SetRowHeightCommand",
        "SetSlicerSelectionCommand",
        "SetTimelineRangeCommand",
    ];

    /// <summary>
    /// r223: commands that DO report IsNoOp, but through a shared helper rather than in their own
    /// class body -- so the source scan, which only reads each class, cannot see it. A false unknown,
    /// not a defect and not an exemption, which is why it gets its own list rather than being filed
    /// under "judged sound": these commands do not merely fail to no-op, they correctly report it.
    /// <para>
    /// The value is the delegate name. <see cref="EveryDelegatedEntryNamesAHelperThatReportsNoOp"/>
    /// checks that the named helper really does report IsNoOp, so the claim is machine-checked and
    /// cannot rot into a comment that used to be true.
    /// </para>
    /// </summary>
    private static readonly Dictionary<string, string> DeclaresIsNoOpThroughAHelper = new()
    {
        ["BringDrawingShapeForwardCommand"] = "TryMoveZOrder",
        ["SendDrawingShapeBackwardCommand"] = "TryMoveZOrder",
        // r224: a second shape of the same thing. RemoveSheetsCommand's whole Apply is
        // `_composite.Apply(ctx)`, and CompositeWorkbookCommand deliberately bubbles IsNoOp up --
        // it starts allNoOp true so a composite wrapping zero children, or one whose children were
        // all no-ops, reports IsNoOp itself. So this command already reports correctly and the
        // per-class scan simply cannot see through the delegation.
        ["RemoveSheetsCommand"] = "CompositeWorkbookCommand",
        // r235: both delegate their whole edit to EditCellsCommand, which r234 taught to compare
        // written values against what was there -- so both now report correctly and only the
        // per-class source scan cannot see it. FormControlInteractionCommand needs one extra step of
        // argument: it DOES write control state of its own, but only inside `if (_applied)`, which
        // is the redo path. Redo only runs for an entry that was pushed, and a no-op is never
        // pushed, so on first Apply this command is pure delegation.
        ["ExternalTextPasteValuesCommand"] = "EditCellsCommand",
        ["FormControlInteractionCommand"] = "EditCellsCommand",
    };

    /// <summary>
    /// r217: commands nobody has judged for this class yet. This list makes no claim about them --
    /// it is the "never examined" column of the three-list structure, kept separate from
    /// <see cref="DeliberatelyNeverReportsNoOp"/> (judged sound, with a reason) and
    /// <see cref="KnownNoOpCapableNotYetFixed"/> (known broken, with evidence) precisely so that
    /// "we know it is broken" cannot hide inside "nobody looked" or vice versa.
    /// <para>
    /// Entries leave by being examined -- either fixed to report IsNoOp, or moved to one of the
    /// other two lists with the reasoning written down. Nothing may join: a newly written command
    /// fails <see cref="EveryWorkbookCommandDeclaresWhetherItCanNoOp"/> until someone classifies it,
    /// which is the whole point of widening the scan.
    /// </para>
    /// </summary>
    private static readonly HashSet<string> NeverExaminedForThisClass =
    [
    ];

    /// <summary>
    /// The ceiling on everything still owed: known-broken plus never-examined. It only ever comes
    /// down.
    /// <para>
    /// r219 replaced two independent ceilings with this one, because the independent version had the
    /// wrong incentive. Examining a never-examined command and finding it defective is PROGRESS --
    /// unknown becomes known, with evidence -- but under a per-list ratchet that move was forbidden,
    /// since it raised the known-broken count. The rule that matters is that the total owed never
    /// grows; which of the two lists an entry sits in is bookkeeping, and moving between them is how
    /// examination is supposed to show up. Both lists still exist and are still kept apart, so "we
    /// know it is broken" and "nobody looked" stay legible as different states.
    /// </para>
    /// <para>History: 163 at r217 (11 + 152), 154 at r218, 151 at r219, 139 at r220, 128 at r221, 106 at r222, 101 at r223, 87 at r224, 85 at r225, 84 at r226, 78 at r228, 75 at r229, 72 at r230, 70 at r231, 50 at r232, 49 at r234, 47 at r235, 46 at r237, 45 at r238, 44 here -- and the never-examined column reaches ZERO, so every one of the 233 commands has now been looked at.</para>
    /// </summary>
    private const int OutstandingCeiling = 44;

    [Fact]
    public void EveryWorkbookCommandDeclaresWhetherItCanNoOp()
    {
        var undeclared = AllWorkbookCommands()
            .Where(entry => !entry.Value)
            .Select(entry => entry.Key)
            .Where(name => !DeliberatelyNeverReportsNoOp.ContainsKey(name)
                           && !KnownNoOpCapableNotYetFixed.Contains(name)
                           && !DeclaresIsNoOpThroughAHelper.ContainsKey(name)
                           && !NeverExaminedForThisClass.Contains(name))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        AllWorkbookCommands().Should().HaveCountGreaterThan(200,
            "the source scan must actually be finding commands -- an empty scan would make this test "
            + "pass while guarding nothing");

        undeclared.Should().BeEmpty(
            "a command that never returns IsNoOp has not been checked for the case where it is "
            + "asked to do what is already done. If it can be, return CommandOutcome(true, "
            + "IsNoOp: true) on that path so the bus skips the push and the user's pending redo "
            + "survives. If it genuinely always changes something, add it to "
            + "DeliberatelyNeverReportsNoOp with the reason. Adding it to "
            + "NeverExaminedForThisClass is NOT an option -- that list is capped and only shrinks. "
            + "Undeclared:\n" + string.Join("\n", undeclared));
    }

    [Fact]
    public void TheOutstandingDebtOnlyEverShrinks() =>
        (KnownNoOpCapableNotYetFixed.Count + NeverExaminedForThisClass.Count)
            .Should().BeLessThanOrEqualTo(
                OutstandingCeiling,
                "known-broken plus never-examined is everything still owed on this class. A command "
                + "leaves the total only by reporting IsNoOp or by being judged sound with a reason; "
                + "moving between the two lists is examination showing its work, not payment. "
                + "Nothing may join the total -- a command written now has no claim to never having "
                + "been looked at -- and the ceiling must be lowered to match each round's result.");

    [Fact]
    public void TheNeverExaminedListStillOnlyShrinks() =>
        NeverExaminedForThisClass.Count.Should().BeLessThanOrEqualTo(
            0,
            "the never-examined column specifically must keep draining, or the combined ceiling "
            + "could be satisfied by fixing easy known-broken entries while nobody ever looks at the "
            + "rest. This bound is the r218 count and comes down as rounds examine.");

    [Fact]
    public void EveryEntryStillNamesALiveCommandThatStillLacksAnIsNoOp()
    {
        var live = AllWorkbookCommands();

        DeliberatelyNeverReportsNoOp.Keys
            .Concat(KnownNoOpCapableNotYetFixed)
            .Concat(NeverExaminedForThisClass)
            .Where(name => !live.TryGetValue(name, out var declares) || declares)
            .OrderBy(name => name, StringComparer.Ordinal)
            .Should().BeEmpty(
                "remove entries whose command is gone or which now report IsNoOp -- a stale entry "
                + "would silently cover a future command of the same name");
    }

    /// <summary>
    /// r223: the delegation claim is checked, not asserted. Each entry names the helper its command
    /// returns the outcome of; that helper's source must contain an IsNoOp report, and the command's
    /// own body must actually call it. Without this, the list would be a place to park anything.
    /// </summary>
    [Fact]
    public void EveryDelegatedEntryNamesAHelperThatReportsNoOp()
    {
        var allSource = string.Join(
            "\n",
            Directory.GetFiles(CommandsDirectory(), "*.cs").Select(File.ReadAllText));

        foreach (var (command, helper) in DeclaresIsNoOpThroughAHelper)
        {
            // r224: a delegate can be a helper METHOD (TryMoveZOrder) or a whole COMMAND CLASS
            // (CompositeWorkbookCommand), and the two need different extents. The first version of
            // this test only knew about methods, so it matched CompositeWorkbookCommand's
            // constructor -- a body with no IsNoOp in it -- and failed a claim that was true. It
            // refused to certify what it could not read, which is the right failure to have.
            var isClass = new Regex(@"\bclass\s+" + helper + @"\b").IsMatch(allSource);
            var helperBody = isClass
                ? new Regex(
                        @"\bclass\s+" + helper + @"\b.*?(?=\n(?:public|internal)\s+(?:sealed\s+)?class\s|\z)",
                        RegexOptions.Singleline)
                    .Match(allSource)
                : new Regex(
                        helper + @"\b[^{;]*\{(?:[^{}]|\{(?:[^{}]|\{[^{}]*\})*\})*\}",
                        RegexOptions.Singleline)
                    .Match(allSource);

            helperBody.Success.Should().BeTrue($"{helper} must exist for {command} to delegate to it");
            helperBody.Value.Should().Contain(
                "IsNoOp",
                $"{command} is listed as reporting IsNoOp through {helper}, so {helper} had better "
                + "still do it -- if this fails the delegation was removed and the command is now "
                + "silently undeclared");
            allSource.Should().Contain(
                helper,
                $"{command} must actually call {helper}");
        }
    }

    [Fact]
    public void NoCommandIsInMoreThanOneList()
    {
        var judged = DeliberatelyNeverReportsNoOp.Keys.ToList();

        judged.Intersect(KnownNoOpCapableNotYetFixed).Should().BeEmpty(
            "a command is either judged sound or known broken; being in both hides which");
        judged.Intersect(NeverExaminedForThisClass).Should().BeEmpty(
            "a command that has been judged sound has, by definition, been examined");
        KnownNoOpCapableNotYetFixed.Intersect(NeverExaminedForThisClass).Should().BeEmpty(
            "a command with two verifiers' evidence behind it has been examined -- letting it also "
            + "sit in the never-examined list is exactly the blurring the three lists exist to stop");
    }

    /// <summary>
    /// Every IWorkbookCommand, mapped to whether its own class body mentions IsNoOp. Read from
    /// SOURCE because a return value is invisible to reflection -- see the class remarks for why
    /// that is the strongest check available here. r217 dropped the Set*/Apply*/Toggle* restriction
    /// from this pattern; that filter was the reason two thirds of the population never reached the
    /// accounting at all.
    /// </summary>
    private static Dictionary<string, bool> AllWorkbookCommands()
    {
        var result = new Dictionary<string, bool>(StringComparer.Ordinal);
        var declaration = new Regex(
            @"class\s+(\w+Command)\b[^;]*:\s*[^{]*IWorkbookCommand",
            RegexOptions.Compiled);

        foreach (var path in Directory.GetFiles(CommandsDirectory(), "*.cs"))
        {
            string? current = null;
            var depth = 0;
            foreach (var line in File.ReadLines(path))
            {
                if (declaration.Match(line) is { Success: true } match)
                {
                    current = match.Groups[1].Value;
                    depth = 0;
                    result.TryAdd(current, false);
                }

                if (current is null)
                    continue;

                depth += line.Count(c => c == '{') - line.Count(c => c == '}');
                if (line.Contains("IsNoOp", StringComparison.Ordinal))
                    result[current] = true;
                if (depth < 0)
                    current = null;
            }
        }

        return result;
    }

    /// <summary>
    /// Located through the shared helper rather than a hand-rolled parent walk --
    /// TestWorkspaceFileLocatorSourceGuardTests forbids test sources from reintroducing their own,
    /// and it caught this one on its first gate run.
    /// </summary>
    private static string CommandsDirectory() =>
        TestWorkspaceFileLocator.FindDirectoryFromBaseDirectory("src", "FreeX.Core.Commands");
}
