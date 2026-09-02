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
        ["SetStructuredTableTotalsRowCommand"] =
            "the planner adds it only when the value differs, and both shells pass the negation",
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
        ["PasteRangeAsPictureCommand"] =
            "r221: the picture is built by the caller and handed in ready-made, and Apply's only "
            + "mutation is to add it -- there is no path that reaches the add and skips it",
        ["ClearSparklineCommand"] =
            "returns Success:false when the sparkline is not found, and otherwise always removes one "
            + "-- there is no path where it finds the target and leaves it in place",
        ["ToggleWorksheetAutoFilterCommand"] =
            "self-guaranteeing: Apply reads sheet.AutoFilter fresh and branches on it, so it either "
            + "creates or removes the filter -- there is no same-value path",
        // The r208 census claimed these could no-op; two verifiers each refuted it.
        ["ApplyStructuredTableFiltersCommand"] = "refuted: the caller only issues a changed filter set",
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
        "AdvancedFilterCommand",
        "AllowEditRangeCommand",
        "ApplyConditionalFormatCommand",
        "AutofillCommand",
        "AverageFilterCommand",
        "BringDrawingShapeForwardCommand",
        "CellFillColorFilterCommand",
        "CellFontColorFilterCommand",
        "CellNoFillColorFilterCommand",
        "ChangeChartSourceCommand",
        "ChangeChartTypeCommand",
        "ChangePivotChartTypeCommand",
        "ChangePivotTableSourceCommand",
        "CollapseRowGroupCommand",
        "ConsolidateCommand",
        "ConvertNotesToCommentsCommand",
        "ConvertStructuredTableToRangeCommand",
        "CopyRangeCommand",
        "DataTableBodyRefreshCommand",
        "DeleteCellsCommand",
        "DeleteColumnsCommand",
        "DeleteCommentCommand",
        "DeleteCustomViewCommand",
        "DeleteDrawingObjectCommand",
        "DeleteRowsCommand",
        "DeleteScenarioCommand",
        "DeleteThreadedCommentCommand",
        "DeleteThreadedCommentReplyCommand",
        "DrillDownPivotTableCommand",
        "DuplicateDrawingObjectCommand",
        "DuplicateSheetCommand",
        "EditCellsCommand",
        "ExpandColGroupCommand",
        "ExpandRowGroupCommand",
        "ExternalTextPasteSpecialCommand",
        "ExternalTextPasteValuesCommand",
        "FillCellsCommand",
        "FilterCommand",
        "FilterConditionCommand",
        "FlashFillCommand",
        "ForecastSheetCommand",
        "FormControlInteractionCommand",
        "FormatPainterDataValidationCommand",
        "GoalSeekCommand",
        "GroupColumnsCommand",
        "GroupRowsCommand",
        "GroupedApplyStyleCommand",
        "GroupedEditCellsCommand",
        "ImportSheetCommand",
        "MergeCellsCommand",
        "MergeScenarioCommand",
        "MoveChartCommand",
        "MoveChartToNewSheetCommand",
        "MovePivotTableCommand",
        "MoveRangeCommand",
        "NudgeChartCommand",
        "NudgeDrawingShapeCommand",
        "NudgePictureCommand",
        "NudgeTextBoxCommand",
        "OneVariableDataTableCommand",
        "PropagateCalculatedColumnCommand",
        "ReapplyStructuredTableStyleCommand",
        "RefreshPivotTableCommand",
        "RefreshStructuredTableTotalsCommand",
        "RejectedWorkbookCommand",
        "RemoveAllowEditRangeCommand",
        "RemoveChartSeriesCommand",
        "RemoveHyperlinksCommand",
        "RemoveNamedRangeCommand",
        "RemoveSheetCommand",
        "RemoveSheetsCommand",
        "ResizeStructuredTableCommand",
        "ResolveThreadedCommentCommand",
        "SaveCustomViewCommand",
        "SaveScenarioCommand",
        "ScenarioSummaryReportCommand",
        "SendDrawingShapeBackwardCommand",
        "SetDataValidationCommand",
        "SetSelectionPaneObjectVisibilityCommand",
        "ShowAllNotesCommand",
        "ShowHideCommentCommand",
        "SubtotalCommand",
        "TopBottomFilterCommand",
        "TwoVariableDataTableCommand",
        "UpdateThreadedCommentReplyCommand",
        "UpdateThreadedCommentTextCommand",
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
    /// <para>History: 163 at r217 (11 + 152), 154 at r218, 151 at r219, 139 at r220, 128 at r221, 106 here.</para>
    /// </summary>
    private const int OutstandingCeiling = 106;

    [Fact]
    public void EveryWorkbookCommandDeclaresWhetherItCanNoOp()
    {
        var undeclared = AllWorkbookCommands()
            .Where(entry => !entry.Value)
            .Select(entry => entry.Key)
            .Where(name => !DeliberatelyNeverReportsNoOp.ContainsKey(name)
                           && !KnownNoOpCapableNotYetFixed.Contains(name)
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
            86,
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
