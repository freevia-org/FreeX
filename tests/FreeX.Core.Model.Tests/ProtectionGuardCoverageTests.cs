using System.Reflection;
using FreeX.Core.Commands;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// Systematic test that enumerates IWorkbookCommand implementors in FreeX.Core.Commands and
/// asserts every cell-writing / structure-mutating command rejects an appropriately protected
/// workbook. This test is intended to catch future omissions: a new command whose Apply writes
/// cells or mutates workbook structure must either appear in the factory map below, or be
/// explicitly added to the skip-list with a justification comment.
///
/// Scope: FreeX.Core.Commands assembly only (public + internal sealed classes).
/// The test does NOT cover App-layer command wrappers or host-only commands.
/// </summary>
public class ProtectionGuardCoverageTests
{
    // ---------------------------------------------------------------------------
    // Shared test setup
    // ---------------------------------------------------------------------------

    private static (Workbook wb, Sheet sheet, ICommandContext ctx) MakeProtectedSetup()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");

        // Populate two cells so row/column commands have something to work with.
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var a2 = new CellAddress(sheet.Id, 2, 1);
        sheet.SetCell(a1, new TextValue("data"));
        sheet.SetCell(a2, new TextValue("more"));

        // Fully protect the sheet — no extra permissions granted.
        // Default ProtectionPermissions has [SelectLockedCells, SelectUnlockedCells];
        // we clear even those to ensure no special permission bypass is possible.
        sheet.ProtectionPermissions.Clear();
        sheet.IsProtected = true;

        return (wb, sheet, new TestCommandContext(wb));
    }

    private static (Workbook wb, Sheet sheet, ICommandContext ctx) MakeStructureProtectedSetup()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("label"));
        wb.IsStructureProtected = true;
        return (wb, sheet, new TestCommandContext(wb));
    }

    // ---------------------------------------------------------------------------
    // Sheet-protection tests: cell/row/column mutation commands
    // ---------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(SheetProtectedCommandFactories))]
    public void CellMutatingCommand_RejectsProtectedSheet(string name, Func<Workbook, Sheet, IWorkbookCommand> factory)
    {
        var (wb, sheet, ctx) = MakeProtectedSetup();
        var command = factory(wb, sheet);

        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeFalse(
            because: $"{name} must reject a fully-protected sheet before mutating anything");
        outcome.ErrorMessage.Should().NotBeNullOrWhiteSpace(
            because: $"{name} must return an error message when rejected");
    }

    // ---------------------------------------------------------------------------
    // Workbook-structure-protection tests: named-range / sheet commands
    // ---------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(WorkbookStructureProtectedCommandFactories))]
    public void StructureMutatingCommand_RejectsStructureProtectedWorkbook(string name, Func<Workbook, Sheet, IWorkbookCommand> factory)
    {
        var (wb, sheet, ctx) = MakeStructureProtectedSetup();
        var command = factory(wb, sheet);

        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeFalse(
            because: $"{name} must reject a structure-protected workbook");
        outcome.ErrorMessage.Should().NotBeNullOrWhiteSpace(
            because: $"{name} must return an error message when rejected");
    }

    // ---------------------------------------------------------------------------
    // Reflection census: every IWorkbookCommand in the assembly must either be
    // in the factory map or in the skip-list with a documented reason.
    // ---------------------------------------------------------------------------

    [Fact]
    public void AllCommandImplementors_AreAccountedFor()
    {
        var commandAssembly = typeof(EditCellsCommand).Assembly;
        var allConcreteCommands = commandAssembly
            .GetTypes()
            .Where(t => t is { IsAbstract: false, IsInterface: false }
                        && typeof(IWorkbookCommand).IsAssignableFrom(t))
            .Select(t => t.Name)
            .OrderBy(n => n)
            .ToHashSet(StringComparer.Ordinal);

        var covered = SheetProtectedNames
            .Concat(WorkbookStructureProtectedNames)
            .Concat(SkipList.Keys)
            .ToHashSet(StringComparer.Ordinal);

        var uncovered = allConcreteCommands.Except(covered).OrderBy(n => n).ToList();

        uncovered.Should().BeEmpty(
            because: "every IWorkbookCommand must appear in SheetProtectedCommandFactories, " +
                     "WorkbookStructureProtectedCommandFactories, or SkipList with a justification. " +
                     "Add any new command to one of those three collections.");
    }

    // ---------------------------------------------------------------------------
    // Skip-list: commands that are genuinely hard to construct simply, OR that do
    // not write cell/sheet state, OR whose protection is enforced by a containing
    // command (e.g. composite commands that call guarded sub-commands).
    // ---------------------------------------------------------------------------

    // Justifications:
    // - CompositeWorkbookCommand: wraps other commands; protection enforced by each child.
    // - RejectedWorkbookCommand: always fails; never mutates anything.
    // - AddPivotTableToNewWorksheetCommand: modifies workbook structure (adds a sheet) —
    //   delegates to AddSheetCommand which is covered by WorkbookStructureProtected set.
    //   Constructing it requires a full pivot cache/table model setup; hard to construct simply.
    // - ForecastSheetCommand: adds a chart sheet + writes cells; requires complex ctor args
    //   (source range with ≥2 data points, timeline column index, etc.). Covered by manual
    //   review: it calls AddChartSheetCommand (structure-guarded) + EditCellsCommand (cell-guarded).
    // - ScenarioSummaryReportCommand: adds a sheet + writes summary cells; requires a named
    //   scenario model and populated changing-cells. Covered by review: calls AddSheetCommand.
    // - DrillDownPivotTableCommand: writes a new sheet with drilled data; requires a populated
    //   pivot table model. Covered by review: calls AddSheetCommand.
    // - ImportSheetCommand: copies cells from a source sheet; constructor requires a second
    //   Workbook + SheetId. Covered by review: calls CanEditCell for each target.
    // - FlashFillCommand: reads cells, writes predictions; constructor requires prior state.
    //   Covered: calls CanEditCell per output row.
    // - AutofillCommand: fills series; constructor needs at least 2 rows. Covered: CanEditCell.
    // - AdvancedFilterCommand: copies filtered rows to an output range; constructor requires
    //   a FilterCriteria object. Covered: CanEditCell per output address.
    // - ConsolidateCommand: copies consolidated values; constructor requires a consolidation
    //   source list. Covered: CanEditCell per destination.
    // - RemoveDuplicateRowsCommand: deletes rows; covered: calls DeleteRowsCommand (guarded).
    // - PasteRangeAsPictureCommand: inserts a picture from a range; requires special setup.
    //   Covered: delegates to InsertPictureCommand.
    // - MoveRangeCommand: moves cells (cut+paste); covered: CanEditCell on source+destination.
    // - CopyRangeCommand: non-destructive copy (Ctrl+drag); mirrors MoveRangeCommand but leaves
    //   the source intact, so only destination cells are CanEditCell-checked. Same skip rationale.
    // - CustomView commands: mutate custom-view bookmarks, not cell content.
    //   Not blocked by sheet protection in Excel; skip.
    // - WorkbookWindow/Theme/CalculationMode commands: workbook-global settings.
    //   Not blocked by sheet or structure protection in Excel; skip.
    // - WorksheetView commands (zoom, view mode, freeze, split, show-formulas, outline-symbols,
    //   view-options): view state only; not blocked by protection in Excel; skip.
    // - PrintTitles, PageSetup, HeaderFooter, PageBreaks, PrintOptions, PageMargins,
    //   PaperSize, ScaleToFit, PrintArea, ClearPrintArea, OutlineClear: print/layout metadata;
    //   not blocked by sheet protection in Excel; skip.
    // - SheetProtection commands (Protect/Unprotect Sheet+Workbook, AllowEditRange*): these ARE
    //   the protection commands; they do not guard against themselves.
    // - ChartCommands (Add/Move/Mutate/Style/Layout/Bounds): write to chart model, not cell grid.
    //   AddChartSheetCommand is covered under WorkbookStructure set (adds a new sheet).
    //   Object-level chart commands (move, resize, change type) require complex chart model setup
    //   and EditObjects audit is pending; skip for now.
    // - PivotTable commands: require populated cache/table models; skip pending pivot audit.
    // - DrawingShape/TextBox/Picture commands: object manipulation; EditObjects governs these;
    //   skip pending drawing protection audit.
    // - ThreadedComment commands: not blocked by standard cell protection; skip.
    // - Comment commands: not blocked by sheet protection in Excel; skip.
    // - ScenarioCommands: EditScenarios permission; complex model setup; skip.
    // - SetDataValidation/ClearDataValidation: data validation metadata; not in scope.
    // - ConditionalFormat commands: CF rules metadata; not cell content.
    // - Filter commands: UseAutoFilter permission; auto-guarded inside FilterCommand.
    // - StructuredTable commands: table protection audit pending.
    // - Outline/group commands: require row/group state; audit pending.
    // - SetFormulaError*: formula metadata; not protection-blocked.
    // - SetWaterfallTotalPoint: chart series point flag; EditObjects audit pending.
    // - Paste partial variants (PasteColumnWidths, PasteComments, PasteConditionalFormats,
    //   PasteDataValidation, PasteFormats): covered by sub-command guards.

    private static readonly Dictionary<string, string> SkipList = new(StringComparer.Ordinal)
    {
        // Meta / always-failing
        ["RejectedWorkbookCommand"] = "Never mutates anything; always returns failure.",
        ["CompositeWorkbookCommand"] = "Wraps other guarded commands; protection enforced per child.",

        // Hard to construct + covered by sub-command guards
        ["AddPivotTableToNewWorksheetCommand"] = "Delegates to AddSheetCommand (structure-guarded). Complex ctor.",
        ["ForecastSheetCommand"] = "Adds sheet + writes cells; calls AddChartSheetCommand + EditCellsCommand. Complex ctor.",
        ["ScenarioSummaryReportCommand"] = "Adds sheet + writes summary; calls AddSheetCommand. Requires scenario model.",
        ["DrillDownPivotTableCommand"] = "Adds drilldown sheet; calls AddSheetCommand. Requires pivot model.",
        ["ImportSheetCommand"] = "Copies cells; calls CanEditCell per target. Requires two Workbooks.",
        ["FlashFillCommand"] = "Calls CanEditCell per output. Requires prior fill pattern state.",
        ["AutofillCommand"] = "Calls CanEditCell per cell. Requires ≥2 source rows with data.",
        ["AdvancedFilterCommand"] = "Calls CanEditCell per output. Requires FilterCriteria model.",
        ["ConsolidateCommand"] = "Calls CanEditCell per destination. Requires consolidation source list.",
        ["RemoveDuplicateRowsCommand"] = "Calls DeleteRowsCommand (guarded). Requires duplicate rows.",
        ["PasteRangeAsPictureCommand"] = "Delegates to InsertPictureCommand. Requires renderer.",
        ["MoveRangeCommand"] = "Calls CanEditCell on source + destination. Requires non-trivial range.",
        ["CopyRangeCommand"] = "Non-destructive copy (mirrors MoveRangeCommand); calls CanEditCell on destination cells only, since source is left untouched. Requires non-trivial range.",

        // View / metadata — not blocked by protection in Excel
        ["SaveCustomViewCommand"] = "Saves view bookmarks; not cell content; not protection-blocked.",
        ["ApplyCustomViewCommand"] = "Restores view bookmarks; not cell content; not protection-blocked.",
        ["DeleteCustomViewCommand"] = "Removes a custom view entry; not cell content; not protection-blocked.",
        ["SetCalculationModeCommand"] = "Workbook-global calc setting; not protection-blocked.",
        ["DefineNamedFormulaCommand"] = "Workbook/sheet-scoped named-formula metadata edit; not cell content, not protection-blocked.",
        ["SetIterativeCalculationOptionsCommand"] = "Workbook-global calc setting (iterative calc); not protection-blocked.",
        ["SetWorkbookThemeCommand"] = "Workbook theme; not protection-blocked.",
        ["SetWorkbookWindowArrangementCommand"] = "Window layout metadata; not protection-blocked.",
        ["SetWorksheetViewModeCommand"] = "View mode; not protection-blocked.",
        ["SetWorksheetViewOptionsCommand"] = "View options; not protection-blocked.",
        ["SetWorksheetZoomCommand"] = "Zoom level; not protection-blocked.",
        ["SetWorksheetOutlineSymbolsCommand"] = "Outline symbol visibility; not protection-blocked.",
        ["SetWorksheetOutlineSettingsCommand"] = "Outline summary-direction + auto-style settings; not protection-blocked.",
        ["SetWorksheetShowFormulasCommand"] = "Show-formulas mode; not protection-blocked.",
        ["SetFreezePanesCommand"] = "Freeze panes; not protection-blocked.",
        ["SetSplitPanesCommand"] = "Split panes; not protection-blocked.",

        // Print / page layout — not blocked by protection
        ["SetPrintTitlesCommand"] = "Print titles; not protection-blocked.",
        ["SetPageBreaksCommand"] = "Page breaks; not protection-blocked.",
        ["SetHeaderFooterCommand"] = "Header/footer; not protection-blocked.",
        ["SetPageSetupCommand"] = "Page setup aggregate; not protection-blocked.",
        ["SetPageMarginsCommand"] = "Page margins; not protection-blocked.",
        ["SetPageOrientationCommand"] = "Page orientation; not protection-blocked.",
        ["SetPaperSizeCommand"] = "Paper size; not protection-blocked.",
        ["SetScaleToFitCommand"] = "Scale-to-fit; not protection-blocked.",
        ["SetPrintAreaCommand"] = "Print area; not protection-blocked.",
        ["SetPrintAreasCommand"] = "Multi-region print area; not protection-blocked.",
        ["ClearPrintAreaCommand"] = "Clear print area; not protection-blocked.",
        ["SetPrintOptionsCommand"] = "Print options; not protection-blocked.",

        // Protection commands themselves
        ["ProtectSheetCommand"] = "Is the protection command; does not guard against itself.",
        ["UnprotectSheetCommand"] = "Is the un-protect command; does not guard against itself.",
        ["ProtectWorkbookCommand"] = "Is the workbook protect command; does not guard against itself.",
        ["UnprotectWorkbookCommand"] = "Is the workbook un-protect command; does not guard against itself.",
        ["AllowEditRangeCommand"] = "Adds an allowed-edit range; part of protection setup, not blocked.",
        ["RemoveAllowEditRangeCommand"] = "Removes an allowed-edit range; part of protection setup.",
        ["ClearAllowEditRangesCommand"] = "Clears allowed-edit ranges; part of protection setup.",
        ["SetAllowEditRangePasswordCommand"] = "Sets allowed-edit range credentials; part of protection setup.",

        // Chart commands — AddChartSheetCommand covered in WorkbookStructure set; others pending
        ["AddChartCommand"] = "Chart insertion; EditObjects audit pending.",
        ["AddPivotChartCommand"] = "Pivot chart insertion; EditObjects audit pending.",
        ["MoveChartCommand"] = "Chart repositioning; EditObjects audit pending.",
        ["MoveChartToNewSheetCommand"] = "Chart move to new sheet; structure + EditObjects audit pending.",
        ["ChangeChartTypeCommand"] = "Chart type mutation; EditObjects audit pending.",
        ["ChangeChartSourceCommand"] = "Chart source mutation; EditObjects audit pending.",
        ["ChangePivotChartTypeCommand"] = "Pivot chart type mutation; EditObjects audit pending.",
        ["SetChartStyleCommand"] = "Chart style; EditObjects audit pending.",
        ["SetChartLayoutCommand"] = "Chart layout; EditObjects audit pending.",
        ["SetChartBoundsCommand"] = "Chart bounds; EditObjects audit pending.",
        ["SetWaterfallTotalPointCommand"] = "Chart series point flag; EditObjects audit pending.",
        ["ConfigurePivotChartOptionsCommand"] = "Pivot chart options; audit pending.",

        // Pivot table commands — require populated pivot model
        ["AddPivotTableCommand"] = "Pivot table creation; requires cache + source model.",
        ["ConfigurePivotTableFieldFiltersCommand"] = "Pivot filter config; requires pivot model.",
        ["ConfigurePivotTableLayoutCommand"] = "Pivot layout config; requires pivot model.",
        ["ConfigurePivotTableOptionsCommand"] = "Pivot options config; requires pivot model.",
        ["ConfigurePivotTableViewCommand"] = "Pivot view config; requires pivot model.",
        ["ConfigurePivotTableCalculatedItemsCommand"] = "Pivot calc items; requires pivot model.",
        ["RefreshPivotTableCommand"] = "Pivot refresh; requires pivot model.",
        ["MovePivotTableCommand"] = "Pivot move; requires pivot model.",
        ["ClearPivotTableViewCommand"] = "Pivot view clear; requires pivot model.",
        ["ChangePivotTableSourceCommand"] = "Pivot source change; requires pivot model.",
        ["RenamePivotTableCommand"] = "Pivot rename; requires pivot model.",
        ["SetSlicerSelectionCommand"] = "Slicer selection; requires slicer model.",
        ["AddSlicerCommand"] = "Slicer insertion; requires pivot + slicer models.",
        ["SetTimelineRangeCommand"] = "Timeline range; requires timeline model.",
        ["SetTimelineGranularityCommand"] = "Timeline granularity; requires timeline model.",
        ["AddTimelineCommand"] = "Timeline insertion; requires timeline model.",

        // Drawing / shape / textbox / picture commands — require drawing model; EditObjects audit pending
        ["AddDrawingShapeCommand"] = "Drawing shape insertion; EditObjects audit pending.",
        ["ResizeDrawingShapeCommand"] = "Drawing shape resize; EditObjects audit pending.",
        ["RotateDrawingShapeCommand"] = "Drawing shape rotation; EditObjects audit pending.",
        ["BringDrawingShapeForwardCommand"] = "Z-order; EditObjects audit pending.",
        ["SendDrawingShapeBackwardCommand"] = "Z-order; EditObjects audit pending.",
        ["SetDrawingShapeColorsCommand"] = "Shape colors; EditObjects audit pending.",
        ["SetDrawingShapeGradientCommand"] = "Shape gradient; EditObjects audit pending.",
        ["SetDrawingShapeEffectCommand"] = "Shape effect; EditObjects audit pending.",
        ["SetDrawingShapeAltTextCommand"] = "Shape alt-text; EditObjects audit pending.",
        ["SetDrawingObjectRotationCommand"] = "Drawing rotation; EditObjects audit pending.",
        ["RepositionShapeCommand"] = "Shape reposition; EditObjects audit pending.",
        ["MoveSelectionPaneObjectCommand"] = "Selection pane z-order; EditObjects audit pending.",
        ["SetSelectionPaneObjectVisibilityCommand"] = "Selection pane visibility; EditObjects audit pending.",
        ["RenameSelectionPaneObjectCommand"] = "Selection pane name; EditObjects audit pending.",
        ["AddTextBoxCommand"] = "Text box insertion; EditObjects audit pending.",
        ["ResizeTextBoxCommand"] = "Text box resize; EditObjects audit pending.",
        ["RotateTextBoxCommand"] = "Text box rotation; EditObjects audit pending.",
        ["RepositionTextBoxCommand"] = "Text box reposition; EditObjects audit pending.",
        ["SetTextBoxColorsCommand"] = "Text box colors; EditObjects audit pending.",
        ["SetTextBoxAltTextCommand"] = "Text box alt-text; EditObjects audit pending.",
        ["InsertPictureCommand"] = "Picture insertion; EditObjects audit pending.",
        ["ResizePictureCommand"] = "Picture resize; EditObjects audit pending.",
        ["RepositionPictureCommand"] = "Picture reposition; EditObjects audit pending.",
        ["RotatePictureCommand"] = "Picture rotation; EditObjects audit pending.",
        ["SetPictureAltTextCommand"] = "Picture alt-text; EditObjects audit pending.",
        ["SetPictureCropCommand"] = "Picture crop; EditObjects audit pending.",
        ["SetPictureLockAspectRatioCommand"] = "Picture lock-aspect; EditObjects audit pending.",

        // Comment / annotation commands — not blocked by cell protection in Excel
        ["SetCommentCommand"] = "Standard comment; not blocked by cell protection in Excel.",
        ["DeleteCommentCommand"] = "Comment deletion; not blocked by cell protection.",
        ["ClearCommentsCommand"] = "Comment clear; not blocked by cell protection.",
        ["ShowHideCommentCommand"] = "Show/hide a pinned note box; not blocked by cell protection.",
        ["ShowAllNotesCommand"] = "Pin/unpin all note boxes; not blocked by cell protection.",
        ["ConvertNotesToCommentsCommand"] = "Convert notes to threaded comments; guarded by EditObjects (covered by ConvertNotesToCommentsCommand_ProtectedSheet_Blocked test).",
        ["SetThreadedCommentCommand"] = "Threaded comment; not blocked by cell protection.",
        ["AddThreadedCommentReplyCommand"] = "Threaded reply; not blocked by cell protection.",
        ["UpdateThreadedCommentTextCommand"] = "Threaded comment edit; not blocked by cell protection.",
        ["UpdateThreadedCommentReplyCommand"] = "Threaded reply edit; not blocked by cell protection.",
        ["DeleteThreadedCommentReplyCommand"] = "Threaded reply delete; not blocked by cell protection.",
        ["ResolveThreadedCommentCommand"] = "Thread resolve; not blocked by cell protection.",
        ["ApplyThreadedCommentChangesCommand"] = "Thread apply; not blocked by cell protection.",
        ["DeleteThreadedCommentCommand"] = "Thread delete; not blocked by cell protection.",

        // Scenario commands — EditScenarios permission; require scenario model
        ["SaveScenarioCommand"] = "Scenario save; requires existing scenario or new-scenario model.",
        ["ApplyScenarioCommand"] = "Scenario apply; requires scenario model with changing cells.",
        ["DeleteScenarioCommand"] = "Scenario delete; requires scenario model.",
        ["MergeScenarioCommand"] = "Scenario merge; requires source scenarios + honors the plan's own protected-cell checks.",

        // Filter commands — UseAutoFilter permission; auto-guarded in FilterCommand
        ["FilterCommand"] = "AutoFilter state; UseAutoFilter-guarded in its own Apply.",
        ["FilterConditionCommand"] = "AutoFilter condition; UseAutoFilter-guarded.",
        ["AverageFilterCommand"] = "AutoFilter by average; UseAutoFilter-guarded.",
        ["TopBottomFilterCommand"] = "AutoFilter top/bottom; UseAutoFilter-guarded.",
        ["CellFillColorFilterCommand"] = "AutoFilter by fill color; UseAutoFilter-guarded.",
        ["CellFontColorFilterCommand"] = "AutoFilter by font color; UseAutoFilter-guarded.",
        ["CellNoFillColorFilterCommand"] = "AutoFilter by no fill; UseAutoFilter-guarded.",
        ["ToggleWorksheetAutoFilterCommand"] = "AutoFilter toggle; UseAutoFilter-guarded.",
        ["ApplyStructuredTableFiltersCommand"] = "Table filter applies to table ranges; table audit pending.",

        // Conditional format commands — CF rules metadata; not cell content
        ["ApplyConditionalFormatCommand"] = "CF rule metadata; not cell content.",
        ["ClearConditionalFormatsCommand"] = "CF rule clear; not cell content.",
        ["ReplaceAllConditionalFormatsCommand"] = "CF rule replace; not cell content.",
        ["PasteConditionalFormatsCommand"] = "CF paste; not cell content.",

        // Data validation
        ["SetDataValidationCommand"] = "DV metadata; not cell content.",
        ["ClearDataValidationCommand"] = "DV clear; not cell content.",
        ["PasteDataValidationCommand"] = "DV paste; not cell content.",
        ["FormatPainterDataValidationCommand"] = "DV format painter copy; not cell content.",

        // Paste partial variants — covered by sub-command guards
        ["PasteColumnWidthsCommand"] = "Column width paste; FormatColumns-guarded sub-command.",
        ["PasteFormatsCommand"] = "Format paste; covered by ApplyStyleCommand guard.",
        ["PasteCommentsCommand"] = "Comment paste; not blocked by cell protection.",

        // Structured table commands — table audit pending
        ["CreateStructuredTableCommand"] = "Table creation; table protection audit pending.",
        ["CreateStyledStructuredTableCommand"] = "Table creation; table protection audit pending.",
        ["ResizeStructuredTableCommand"] = "Table resize; table protection audit pending.",
        ["RenameStructuredTableCommand"] = "Table rename; table protection audit pending.",
        ["ConvertStructuredTableToRangeCommand"] = "Table convert; table protection audit pending.",
        ["ApplyStructuredTableStyleCommand"] = "Table style; table protection audit pending.",
        ["ConfigureStructuredTableStyleOptionsCommand"] = "Table style options; audit pending.",
        ["ReapplyStructuredTableStyleCommand"] = "Table style reapply; audit pending.",
        ["RefreshStructuredTableTotalsCommand"] = "Table totals refresh; audit pending.",
        ["SetStructuredTableTotalsRowCommand"] = "Table totals row; audit pending.",

        // Outline / group commands — require group state; audit pending
        ["GroupRowsCommand"] = "Row group; requires ungrouped rows; audit pending.",
        ["GroupColumnsCommand"] = "Column group; requires ungrouped cols; audit pending.",
        ["ExpandRowGroupCommand"] = "Row group expand; view state; audit pending.",
        ["CollapseRowGroupCommand"] = "Row group collapse; view state; audit pending.",
        ["ExpandColGroupCommand"] = "Column group expand; view state; audit pending.",
        ["CollapseColGroupCommand"] = "Column group collapse; view state; audit pending.",
        ["SetRowOutlineGroupCollapsedCommand"] = "Row outline collapsed; view state; audit pending.",
        ["SetColumnOutlineGroupCollapsedCommand"] = "Column outline collapsed; view state; audit pending.",
        ["ClearWorksheetOutlineCommand"] = "Outline clear; view state; audit pending.",

        // Misc metadata
        ["SetFormulaErrorCheckingRuleCommand"] = "Formula error rule; metadata; not protection-blocked.",
        ["SetFormulaErrorIgnoredCommand"] = "Formula error ignore; metadata; not protection-blocked.",
        ["SetWorksheetBackgroundCommand"] = "Worksheet background; not protection-blocked.",
        ["ClearWorksheetBackgroundCommand"] = "Worksheet background clear; not protection-blocked.",

        // Form control interaction — delegates to EditCellsCommand which is protection-guarded.
        ["FormControlInteractionCommand"] = "Wraps EditCellsCommand; protection enforced by the inner cell-edit.",
    };

    // ---------------------------------------------------------------------------
    // Factory maps for sheet-protection-guarded commands
    // These must return a command that will FAIL when applied to a fully-protected
    // sheet with no permissions, with a meaningful error message.
    // ---------------------------------------------------------------------------

    public static IEnumerable<object[]> SheetProtectedCommandFactories =>
        SheetProtectedFactoryMap.Select(kvp => new object[]
        {
            kvp.Key,
            kvp.Value
        });

    private static IEnumerable<string> SheetProtectedNames => SheetProtectedFactoryMap.Keys;

    private static readonly Dictionary<string, Func<Workbook, Sheet, IWorkbookCommand>>
        SheetProtectedFactoryMap = new(StringComparer.Ordinal)
        {
            // ---- Cell content writes ----
            ["EditCellsCommand"] = (wb, sheet) =>
                EditCellsCommand.ForValue(sheet.Id, new CellAddress(sheet.Id, 1, 1), new TextValue("x")),

            ["GroupedEditCellsCommand"] = (wb, sheet) =>
            {
                var addr = new CellAddress(sheet.Id, 1, 1);
                return new GroupedEditCellsCommand(
                    [sheet.Id],
                    sheet.Id,
                    [(addr, Cell.FromValue(new TextValue("x")))]);
            },

            ["ClearContentsCommand"] = (wb, sheet) =>
                new ClearContentsCommand(sheet.Id, new GridRange(
                    new CellAddress(sheet.Id, 1, 1),
                    new CellAddress(sheet.Id, 1, 1))),

            ["FillCellsCommand"] = (wb, sheet) =>
                new FillCellsCommand(sheet.Id, new GridRange(
                    new CellAddress(sheet.Id, 1, 1),
                    new CellAddress(sheet.Id, 3, 1)),
                FillCellsDirection.Down),

            ["PasteCellsCommand"] = (wb, sheet) =>
            {
                var addr = new CellAddress(sheet.Id, 1, 1);
                return new PasteCellsCommand(sheet.Id, [
                    (addr, Cell.FromValue(new TextValue("paste")))
                ]);
            },

            ["PasteSpecialCellsCommand"] = (wb, sheet) =>
            {
                var addr = new CellAddress(sheet.Id, 1, 1);
                return new PasteSpecialCellsCommand(
                    sheet.Id,
                    new GridRange(addr, addr),
                    [(addr, Cell.FromValue(new TextValue("paste")))],
                    addr,
                    new PasteSpecialOptions());
            },

            // Pastes external (non-FreeX) clipboard text combined with the existing destination
            // cell via Paste Special's Add/Subtract/Multiply/Divide Operation (review P46). Guards
            // via CommandGuards.CanEditCell per address exactly like PasteSpecialCellsCommand above.
            ["ExternalTextPasteSpecialCommand"] = (wb, sheet) =>
            {
                var addr = new CellAddress(sheet.Id, 1, 1);
                return new ExternalTextPasteSpecialCommand(
                    sheet.Id,
                    [(addr, "1")],
                    PasteSpecialOperation.Add);
            },

            // Pastes external (non-FreeX) clipboard text as plain values, honoring the destination's
            // Text (@) number format (R28-clipboard-external-formats-deep-3). Its Apply delegates to
            // a real EditCellsCommand, so it rejects a fully-protected sheet the same way that command
            // does.
            ["ExternalTextPasteValuesCommand"] = (wb, sheet) =>
            {
                var addr = new CellAddress(sheet.Id, 1, 1);
                return new ExternalTextPasteValuesCommand(
                    sheet.Id,
                    [(addr, "1")],
                    preserveText: false);
            },

            ["PasteMergedRegionsCommand"] = (wb, sheet) =>
            {
                // Recreating a copied merge at the destination is a FormatCells-gated edit,
                // mirroring MergeCellsCommand.
                sheet.IsProtected = false;
                sheet.AddMergedRegion(new GridRange(
                    new CellAddress(sheet.Id, 1, 1),
                    new CellAddress(sheet.Id, 1, 2)));
                sheet.IsProtected = true;
                return new PasteMergedRegionsCommand(
                    sheet.Id,
                    new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 2)),
                    new CellAddress(sheet.Id, 5, 5),
                    transpose: false);
            },

            // R91-io-clipboard-image-formats-5-2: pasting a floating picture anchored inside the
            // copied range is EditObjects-gated, mirroring InsertPictureCommand/PasteMergedRegionsCommand.
            ["PastePicturesCommand"] = (wb, sheet) =>
            {
                var anchor = new CellAddress(sheet.Id, 1, 1);
                var picture = new PictureModel
                {
                    Anchor = anchor,
                    Kind = PictureKind.Image,
                    ImageBytes = [1, 2, 3],
                    ContentType = "image/png"
                };
                return new PastePicturesCommand(
                    sheet.Id,
                    new GridRange(anchor, anchor),
                    new CellAddress(sheet.Id, 5, 5),
                    [picture],
                    transpose: false);
            },

            // R92: chart analogue of PastePicturesCommand. ChartCommandGuards.RejectIfEditObjectsBlocked
            // runs on the destination sheet before any source lookup or chart math, so an empty
            // carried-chart list still exercises the guard.
            ["PasteChartsCommand"] = (wb, sheet) =>
            {
                var anchor = new CellAddress(sheet.Id, 1, 1);
                return new PasteChartsCommand(
                    sheet.Id,
                    sheet.Id,
                    new GridRange(anchor, anchor),
                    new CellAddress(sheet.Id, 5, 5),
                    [],
                    transpose: false);
            },

            // R92: DrawingShape/TextBox analogues of PastePicturesCommand. Each runs its
            // <Kind>CommandGuards.RejectIfEditObjectsBlocked on the destination sheet before any
            // geometry math, so an empty carried-object list still exercises the guard.
            ["PasteShapesCommand"] = (wb, sheet) =>
            {
                var anchor = new CellAddress(sheet.Id, 1, 1);
                return new PasteShapesCommand(
                    sheet.Id,
                    new GridRange(anchor, anchor),
                    new CellAddress(sheet.Id, 5, 5),
                    [],
                    transpose: false);
            },

            ["PasteTextBoxesCommand"] = (wb, sheet) =>
            {
                var anchor = new CellAddress(sheet.Id, 1, 1);
                return new PasteTextBoxesCommand(
                    sheet.Id,
                    new GridRange(anchor, anchor),
                    new CellAddress(sheet.Id, 5, 5),
                    [],
                    transpose: false);
            },

            // R92/R112-sibling-fix: removing a chart series is governed by the EditObjects
            // protection bit. As of the R112 sibling fix ChartCommandGuards.RejectIfEditObjectsBlocked
            // now runs AFTER the chart lookup (it needs the chart to check its per-object Locked
            // flag), so a real (non-pivot, column-major) chart must be seeded here -- otherwise the
            // command would fail with "chart not found" instead of exercising protection.
            ["RemoveChartSeriesCommand"] = (wb, sheet) =>
            {
                var chart = new ChartModel
                {
                    Type = ChartType.Column,
                    DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 2))
                };
                sheet.IsProtected = false;
                sheet.Charts.Add(chart);
                sheet.IsProtected = true;
                return new RemoveChartSeriesCommand(sheet.Id, chart.Id, seriesIndex: 0);
            },

            // ---- Goal Seek ----
            ["GoalSeekCommand"] = (wb, sheet) =>
                new GoalSeekCommand(new CellAddress(sheet.Id, 1, 1), 42.0),

            // ---- Data Table ----
            ["OneVariableDataTableCommand"] = (wb, sheet) =>
            {
                // Set up data while unprotected, then re-protect.
                sheet.IsProtected = false;
                var formulaCell = new CellAddress(sheet.Id, 1, 1);
                var inputCell = new CellAddress(sheet.Id, 10, 1);
                sheet.SetCell(formulaCell, Cell.FromFormula("A10*2"));
                sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(1));
                sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(2));
                sheet.IsProtected = true;
                return new OneVariableDataTableCommand(
                    new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 2)),
                    formulaCell,
                    inputCell,
                    DataTableInputOrientation.Column);
            },

            ["TwoVariableDataTableCommand"] = (wb, sheet) =>
            {
                sheet.IsProtected = false;
                var formulaCell = new CellAddress(sheet.Id, 1, 1);
                sheet.SetCell(formulaCell, Cell.FromFormula("B1+A2"));
                sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(1));
                sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new NumberValue(2));
                sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(10));
                sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(20));
                sheet.IsProtected = true;
                return new TwoVariableDataTableCommand(
                    new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 3)),
                    formulaCell,
                    new CellAddress(sheet.Id, 1, 1),
                    new CellAddress(sheet.Id, 1, 1));
            },

            // ---- Merge / unmerge ----
            ["MergeCellsCommand"] = (wb, sheet) =>
                new MergeCellsCommand(sheet.Id, new GridRange(
                    new CellAddress(sheet.Id, 1, 1),
                    new CellAddress(sheet.Id, 2, 2))),

            ["UnmergeCellsCommand"] = (wb, sheet) =>
            {
                sheet.IsProtected = false;
                sheet.AddMergedRegion(new GridRange(
                    new CellAddress(sheet.Id, 1, 1),
                    new CellAddress(sheet.Id, 2, 2)));
                sheet.IsProtected = true;
                return new UnmergeCellsCommand(sheet.Id, new GridRange(
                    new CellAddress(sheet.Id, 1, 1),
                    new CellAddress(sheet.Id, 2, 2)));
            },

            // ---- Row/column structural ----
            ["InsertRowsCommand"] = (wb, sheet) =>
                new InsertRowsCommand(sheet.Id, beforeRow: 1),

            ["DeleteRowsCommand"] = (wb, sheet) =>
                new DeleteRowsCommand(sheet.Id, startRow: 1),

            ["InsertColumnsCommand"] = (wb, sheet) =>
                new InsertColumnsCommand(sheet.Id, beforeCol: 1),

            ["DeleteColumnsCommand"] = (wb, sheet) =>
                new DeleteColumnsCommand(sheet.Id, startCol: 1),

            ["InsertCellsCommand"] = (wb, sheet) =>
                new InsertCellsCommand(sheet.Id, new GridRange(
                    new CellAddress(sheet.Id, 1, 1),
                    new CellAddress(sheet.Id, 1, 1)),
                InsertCellsShiftDirection.Down),

            ["DeleteCellsCommand"] = (wb, sheet) =>
                new DeleteCellsCommand(sheet.Id, new GridRange(
                    new CellAddress(sheet.Id, 1, 1),
                    new CellAddress(sheet.Id, 1, 1)),
                DeleteCellsShiftDirection.Up),

            // ---- Style ----
            ["ApplyStyleCommand"] = (wb, sheet) =>
                new ApplyStyleCommand(sheet.Id, new GridRange(
                    new CellAddress(sheet.Id, 1, 1),
                    new CellAddress(sheet.Id, 1, 1)),
                new StyleDiff(Bold: true)),

            ["GroupedApplyStyleCommand"] = (wb, sheet) =>
                new GroupedApplyStyleCommand(
                    [sheet.Id],
                    new GridRange(
                        new CellAddress(sheet.Id, 1, 1),
                        new CellAddress(sheet.Id, 1, 1)),
                    new StyleDiff(Bold: true)),

            // ---- Layout ----
            ["SetRowHeightCommand"] = (wb, sheet) =>
                new SetRowHeightCommand(sheet.Id, 1, 1, 30),

            ["SetColumnWidthCommand"] = (wb, sheet) =>
                new SetColumnWidthCommand(sheet.Id, 1, 1, 20),

            ["SetRowsHiddenCommand"] = (wb, sheet) =>
                new SetRowsHiddenCommand(sheet.Id, 1, 1, hidden: true),

            ["SetColumnsHiddenCommand"] = (wb, sheet) =>
                new SetColumnsHiddenCommand(sheet.Id, 1, 1, hidden: true),

            // ---- Sort ----
            ["SortCommand"] = (wb, sheet) =>
                new SortCommand(sheet.Id, new GridRange(
                    new CellAddress(sheet.Id, 1, 1),
                    new CellAddress(sheet.Id, 3, 2)),
                [new SortKey(0, Ascending: true)]),

            // ---- Subtotals ----
            ["SubtotalCommand"] = (wb, sheet) =>
            {
                sheet.IsProtected = false;
                for (uint r = 1; r <= 4; r++)
                {
                    sheet.SetCell(new CellAddress(sheet.Id, r, 1), new TextValue(r == 1 ? "Group" : "A"));
                    sheet.SetCell(new CellAddress(sheet.Id, r, 2), new NumberValue(r * 10));
                }
                sheet.IsProtected = true;
                return new SubtotalCommand(sheet.Id,
                    new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 2)),
                    groupByColumnOffset: 0,
                    subtotalColumnOffset: 1);
            },

            ["RemoveSubtotalRowsCommand"] = (wb, sheet) =>
                new RemoveSubtotalRowsCommand(sheet.Id,
                    new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 2))),

            // ---- Sparklines ----
            ["AddSparklineCommand"] = (wb, sheet) =>
                new AddSparklineCommand(
                    sheet.Id,
                    new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 1)),
                    new CellAddress(sheet.Id, 1, 2),
                    SparklineKind.Line),

            // R92/R112-sibling-fix: the Select-Data dialog's Hidden-and-Empty-Cells setting is a
            // chart mutation governed by the EditObjects protection bit. As of the R112 sibling fix
            // ChartCommandGuards.RejectIfEditObjectsBlocked now runs AFTER the chart lookup (it needs
            // the chart to check its per-object Locked flag), so a real chart must be seeded here --
            // otherwise the command would fail with "chart not found" instead of exercising
            // protection, defeating the point of this test.
            ["ConfigureChartHiddenEmptyCellsCommand"] = (wb, sheet) =>
            {
                var chart = new ChartModel
                {
                    Type = ChartType.Column,
                    DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 2))
                };
                sheet.IsProtected = false;
                sheet.Charts.Add(chart);
                sheet.IsProtected = true;
                return new ConfigureChartHiddenEmptyCellsCommand(
                    sheet.Id,
                    chart.Id,
                    ChartBlankDisplayMode.Gap,
                    showDataInHiddenRowsAndColumns: false);
            },

            // R91: duplicating a drawing object is governed by the EditObjects protection bit
            // (DrawingShapeCommandGuards/ChartCommandGuards.RejectIfEditObjectsBlocked). The command
            // resolves the source object BEFORE the guard runs, so seed a real shape to duplicate.
            ["DuplicateDrawingObjectCommand"] = (wb, sheet) =>
            {
                var source = new DrawingShapeModel
                {
                    Anchor = new CellAddress(sheet.Id, 2, 2),
                    Kind = DrawingShapeKind.Rectangle
                };
                sheet.DrawingShapes.Add(source);
                return new DuplicateDrawingObjectCommand(
                    sheet.Id,
                    sheet.Id,
                    SelectionPaneObjectKind.Shape,
                    source.Id);
            },

            ["ConfigureSparklineCommand"] = (wb, sheet) =>
            {
                var sparkline = new SparklineModel
                {
                    DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 1)),
                    Location = new CellAddress(sheet.Id, 1, 2),
                    Kind = SparklineKind.Line,
                };
                sheet.IsProtected = false;
                sheet.Sparklines.Add(sparkline);
                sheet.IsProtected = true;
                return new ConfigureSparklineCommand(
                    sheet.Id,
                    sparkline.Id,
                    SparklineSettings.Capture(sparkline) with { Kind = SparklineKind.Column });
            },

            ["ClearSparklineCommand"] = (wb, sheet) =>
            {
                var sparkline = new SparklineModel
                {
                    DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 1)),
                    Location = new CellAddress(sheet.Id, 1, 2),
                    Kind = SparklineKind.Line,
                };
                sheet.IsProtected = false;
                sheet.Sparklines.Add(sparkline);
                sheet.IsProtected = true;
                return new ClearSparklineCommand(sheet.Id, sparkline.Id);
            },

            // ---- Hyperlinks ----
            ["SetHyperlinkCommand"] = (wb, sheet) =>
                new SetHyperlinkCommand(
                    sheet.Id,
                    new CellAddress(sheet.Id, 1, 1),
                    "https://example.com",
                    "Click here"),

            ["RemoveHyperlinksCommand"] = (wb, sheet) =>
            {
                // Add a hyperlink directly to the sheet model (bypassing command layer) so
                // the command has something to remove; CanEditCell check then fires.
                var addr = new CellAddress(sheet.Id, 1, 1);
                sheet.IsProtected = false;
                sheet.Hyperlinks[addr] = "https://example.com";
                sheet.IsProtected = true;
                return new RemoveHyperlinksCommand(sheet.Id,
                    new GridRange(addr, addr));
            },

            ["ClearHyperlinksCommand"] = (wb, sheet) =>
            {
                var addr = new CellAddress(sheet.Id, 1, 1);
                sheet.IsProtected = false;
                sheet.Hyperlinks[addr] = "https://example.com";
                sheet.IsProtected = true;
                return new ClearHyperlinksCommand(sheet.Id,
                    new GridRange(addr, addr));
            },

            // ---- Drawing object text ----
            ["SetTextBoxTextCommand"] = (wb, sheet) =>
            {
                var textBox = new TextBoxModel
                {
                    Anchor = new CellAddress(sheet.Id, 1, 1),
                    Text = "Before"
                };
                sheet.IsProtected = false;
                sheet.TextBoxes.Add(textBox);
                sheet.IsProtected = true;
                return new SetTextBoxTextCommand(sheet.Id, textBox.Id, "After");
            },

            // ---- Structured table calculated-column propagation (N34) ----
            // Writes the row-shifted formula into the table's other data-body cells and persists
            // it as the column's CalculatedColumnFormula; calls CommandGuards.RejectIfProtected(sheet)
            // directly (Commands.cs), so it must reject a fully-protected sheet like EditCellsCommand.
            ["PropagateCalculatedColumnCommand"] = (wb, sheet) =>
            {
                sheet.IsProtected = false;
                var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 2));
                var table = new StructuredTableModel
                {
                    Id = 1,
                    Name = "Table1",
                    DisplayName = "Table1",
                    Range = range,
                    HeaderRowCount = 1
                };
                table.Columns.Add(new StructuredTableColumnModel(1, "Col1"));
                table.Columns.Add(new StructuredTableColumnModel(2, "Col2"));
                sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Col1"));
                sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Col2"));
                sheet.SetCell(new CellAddress(sheet.Id, 2, 2), Cell.FromFormula("A2*2"));
                sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new BlankValue());
                sheet.StructuredTables.Add(table);
                sheet.IsProtected = true;
                return new PropagateCalculatedColumnCommand(
                    sheet.Id, table.Id, columnId: 2, sourceRow: 2, sourceFormulaText: "A2*2", targetRows: [3]);
            },
        };

    // ---------------------------------------------------------------------------
    // Factory maps for workbook-structure-protection-guarded commands
    // ---------------------------------------------------------------------------

    public static IEnumerable<object[]> WorkbookStructureProtectedCommandFactories =>
        WorkbookStructureProtectedFactoryMap.Select(kvp => new object[]
        {
            kvp.Key,
            kvp.Value
        });

    private static IEnumerable<string> WorkbookStructureProtectedNames =>
        WorkbookStructureProtectedFactoryMap.Keys;

    private static readonly Dictionary<string, Func<Workbook, Sheet, IWorkbookCommand>>
        WorkbookStructureProtectedFactoryMap = new(StringComparer.Ordinal)
        {
            ["AddSheetCommand"] = (wb, sheet) =>
                new AddSheetCommand("NewSheet"),

            ["RemoveSheetCommand"] = (wb, sheet) =>
                new RemoveSheetCommand(sheet.Id),

            ["RenameSheetCommand"] = (wb, sheet) =>
                new RenameSheetCommand(sheet.Id, "Renamed"),

            ["MoveSheetCommand"] = (wb, sheet) =>
            {
                wb.IsStructureProtected = false;
                wb.AddSheet("Sheet2");
                wb.IsStructureProtected = true;
                // MoveSheetCommand takes (int fromIndex, int toIndex)
                return new MoveSheetCommand(fromIndex: 0, toIndex: 1);
            },

            ["MoveSheetsCommand"] = (wb, sheet) =>
            {
                wb.IsStructureProtected = false;
                wb.AddSheet("Sheet2");
                wb.IsStructureProtected = true;
                return new MoveSheetsCommand([sheet.Id], insertBeforeIndex: 1);
            },

            ["DuplicateSheetCommand"] = (wb, sheet) =>
                new DuplicateSheetCommand(sheet.Id, "Copy"),

            ["SetSheetHiddenCommand"] = (wb, sheet) =>
            {
                // Need at least 2 visible sheets so hide doesn't fail on "last visible sheet"
                wb.IsStructureProtected = false;
                wb.AddSheet("Extra");
                wb.IsStructureProtected = true;
                return new SetSheetHiddenCommand(sheet.Id, hidden: true);
            },

            ["SetSheetTabColorCommand"] = (wb, sheet) =>
                new SetSheetTabColorCommand(sheet.Id, new CellColor(255, 0, 0)),

            ["DefineNamedRangeCommand"] = (wb, sheet) =>
                new DefineNamedRangeCommand("MyRange",
                    new GridRange(
                        new CellAddress(sheet.Id, 1, 1),
                        new CellAddress(sheet.Id, 1, 1))),

            ["RemoveNamedRangeCommand"] = (wb, sheet) =>
            {
                wb.IsStructureProtected = false;
                wb.DefineNamedRange("ToRemove", new GridRange(
                    new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 1)));
                wb.IsStructureProtected = true;
                return new RemoveNamedRangeCommand("ToRemove");
            },

            ["CreateNamedRangesFromSelectionCommand"] = (wb, sheet) =>
            {
                wb.IsStructureProtected = false;
                sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("SalesTotal"));
                wb.IsStructureProtected = true;
                return new CreateNamedRangesFromSelectionCommand(
                    new GridRange(
                        new CellAddress(sheet.Id, 1, 1),
                        new CellAddress(sheet.Id, 2, 1)),
                    UseTopRow: true,
                    UseLeftColumn: false,
                    UseBottomRow: false,
                    UseRightColumn: false);
            },

            // AddChartSheetCommand adds a new sheet (workbook structure mutation)
            ["AddChartSheetCommand"] = (wb, sheet) =>
                new AddChartSheetCommand(
                    sheet.Id,
                    new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 1)),
                    ChartType.Column),
        };
}
