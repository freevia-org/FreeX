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
    /// Commands the r208 census CONFIRMED can be invoked where they mutate nothing, each checked by
    /// two independent verifiers, and which do not report IsNoOp yet. Known defects, not unknowns.
    /// </summary>
    private static readonly HashSet<string> KnownNoOpCapableNotYetFixed =
    [
        "ApplyCustomViewCommand",
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
        "AddChartCommand",
        "AddChartSheetCommand",
        "AddDrawingShapeCommand",
        "AddFormControlCommand",
        "AddPivotChartCommand",
        "AddPivotTableCommand",
        "AddPivotTableToNewWorksheetCommand",
        "AddSheetCommand",
        "AddSlicerCommand",
        "AddSparklineCommand",
        "AddTextBoxCommand",
        "AddThreadedCommentReplyCommand",
        "AddTimelineCommand",
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
        "ClearAllowEditRangesCommand",
        "ClearCommentsCommand",
        "ClearConditionalFormatsCommand",
        "ClearContentsCommand",
        "ClearHyperlinksCommand",
        "ClearPivotTableViewCommand",
        "ClearPrintAreaCommand",
        "ClearSparklineCommand",
        "ClearWorksheetBackgroundCommand",
        "CollapseRowGroupCommand",
        "ConfigureChartHiddenEmptyCellsCommand",
        "ConfigurePivotChartOptionsCommand",
        "ConfigurePivotTableCalculatedItemsCommand",
        "ConfigurePivotTableFieldFiltersCommand",
        "ConfigurePivotTableLayoutCommand",
        "ConfigurePivotTableOptionsCommand",
        "ConfigurePivotTableViewCommand",
        "ConfigureSparklineCommand",
        "ConfigureStructuredTableStyleOptionsCommand",
        "ConsolidateCommand",
        "ConvertNotesToCommentsCommand",
        "ConvertStructuredTableToRangeCommand",
        "CopyRangeCommand",
        "CreateNamedRangesFromSelectionCommand",
        "CreateStructuredTableCommand",
        "CreateStyledStructuredTableCommand",
        "DataTableBodyRefreshCommand",
        "DefineNamedFormulaCommand",
        "DefineNamedRangeCommand",
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
        "InsertCellsCommand",
        "InsertColumnsCommand",
        "InsertPictureCommand",
        "InsertRowsCommand",
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
        "PasteCellsCommand",
        "PasteChartsCommand",
        "PasteColumnWidthsCommand",
        "PasteCommentsCommand",
        "PasteConditionalFormatsCommand",
        "PasteDataValidationCommand",
        "PasteFormatsCommand",
        "PasteMergedRegionsCommand",
        "PastePicturesCommand",
        "PasteRangeAsPictureCommand",
        "PasteShapesCommand",
        "PasteSpecialCellsCommand",
        "PasteTextBoxesCommand",
        "PropagateCalculatedColumnCommand",
        "ProtectSheetCommand",
        "ProtectWorkbookCommand",
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
        "UnprotectSheetCommand",
        "UnprotectWorkbookCommand",
        "UpdateThreadedCommentReplyCommand",
        "UpdateThreadedCommentTextCommand",
    ];

    /// <summary>The ceiling on the known-broken list. Lower it as rounds fix; never raise it.</summary>
    private const int DebtCeiling = 11;

    /// <summary>The ceiling on the never-examined list. Same rule: it only ever comes down.</summary>
    private const int UnexaminedCeiling = 143;

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
    public void TheKnownBrokenListOnlyEverShrinks() =>
        KnownNoOpCapableNotYetFixed.Count.Should().BeLessThanOrEqualTo(
            DebtCeiling,
            "this list is debt with evidence behind each entry. A command leaves it by reporting "
            + "IsNoOp; nothing may join it, and the ceiling must be lowered to match.");

    [Fact]
    public void TheNeverExaminedListOnlyEverShrinks() =>
        NeverExaminedForThisClass.Count.Should().BeLessThanOrEqualTo(
            UnexaminedCeiling,
            "this list is the honest record of what nobody has looked at. A command leaves it by "
            + "being examined -- fixed, or moved to one of the judged lists with the reason. Nothing "
            + "may join it: a command written after r217 has no claim to never having been looked "
            + "at, and the ceiling must come down as rounds work through it.");

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
