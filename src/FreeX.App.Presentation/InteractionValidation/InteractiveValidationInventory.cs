using FreeX.App.Presentation.Shell;

namespace FreeX.App.Presentation.InteractionValidation;

[Flags]
public enum ShortcutModifierKeys
{
    None = 0,
    Control = 1,
    Shift = 2,
    Alt = 4,
    Meta = 8,
}

public enum ShortcutInteractionKind
{
    KeyGesture,
    KeySequence,
    ContextualKeyGesture,
    RibbonKeytipSequence,
    MouseWheel,
}

public enum ShortcutInteractionContext
{
    Application,
    Workbook,
    WorkbookWindow,
    Worksheet,
    WorksheetSelection,
    CellEditor,
    FormulaBar,
    FormulaReferenceEditor,
    HyperlinkCell,
    Ribbon,
    SheetTabs,
    DataValidationListOrFilterHeader,
    FocusedContextTarget,
}

public sealed record ShortcutGestureStep(
    string Key,
    ShortcutModifierKeys Modifiers = ShortcutModifierKeys.None);

public sealed record ShortcutInteractionDescriptor(
    string DisplayText,
    ShortcutInteractionKind Kind,
    ShortcutInteractionContext Context,
    IReadOnlyList<ShortcutGestureStep> Steps,
    string Input = "Keyboard",
    ShortcutModifierKeys InputModifiers = ShortcutModifierKeys.None);

public sealed record KeyboardShortcutValidationScenario(
    string Id,
    string Area,
    string Owner,
    string DisplayChord,
    IReadOnlyList<string> Aliases,
    IReadOnlyList<ShortcutInteractionDescriptor> Interactions,
    string ExpectedBehavior,
    bool IsNative = false,
    bool IsExternal = false);

public sealed record WorksheetRangeValidationTarget(
    string Id,
    string Area,
    string Owner,
    string DisplayTarget,
    IReadOnlyList<string> Aliases,
    string ExpectedBehavior,
    bool IsNative = false,
    bool IsExternal = false);

/// <summary>
/// UI-neutral validation contract for the logical shortcut scenarios documented in
/// docs/parity/shortcuts.md and the worksheet-pointing inputs exposed by desktop dialogs.
/// </summary>
public static class InteractiveValidationInventory
{
    public static IReadOnlyList<KeyboardShortcutValidationScenario> KeyboardShortcuts { get; } =
    [
        Shortcut("shortcut.file.new-workbook", "File", "WorkbookLifecycle", "Ctrl+N", "Creates a blank workbook."),
        Shortcut("shortcut.file.open", "File", "WorkbookLifecycle", "Ctrl+O / Ctrl+F12", "Opens the workbook file picker.", ["Ctrl+F12"], isNative: true),
        Shortcut("shortcut.file.save", "File", "WorkbookLifecycle", "Ctrl+S / Shift+F12", "Saves to the current path or opens Save As when a path cannot be reused.", ["Shift+F12"]),
        Shortcut("shortcut.file.close-workbook", "File", "WorkbookLifecycle", "Ctrl+W / Ctrl+F4", "Closes the current workbook window.", ["Ctrl+F4"]),
        Shortcut("shortcut.file.print", "File", "PrintWorkflow", "Ctrl+P / Ctrl+Shift+F12", "Opens FreeX print preview with keyboard-accessible print settings and Page Setup controls.", ["Ctrl+Shift+F12"]),

        Shortcut("shortcut.edit.undo", "Edit", "CommandHistory", "Ctrl+Z / Alt+Backspace", "Undoes the last command-bus action.", ["Alt+Backspace"]),
        Shortcut("shortcut.edit.redo", "Edit", "CommandHistory", "Ctrl+Y / Ctrl+Shift+Z", "Redoes the last undone command-bus action.", ["Ctrl+Shift+Z"]),

        Shortcut("shortcut.clipboard.copy", "Clipboard", "WorkbookClipboard", "Ctrl+C / Ctrl+Insert", "Copies the selected cells.", ["Ctrl+Insert"]),
        Shortcut("shortcut.clipboard.cut", "Clipboard", "WorkbookClipboard", "Ctrl+X / Shift+Delete", "Starts a pending cut and clears the source only after a valid non-overlapping paste.", ["Shift+Delete"]),
        Shortcut("shortcut.clipboard.paste", "Clipboard", "WorkbookClipboard", "Ctrl+V / Shift+Insert / Ctrl+Shift+V", "Pastes cells; Ctrl+Shift+V uses the paste-values path.", ["Shift+Insert", "Ctrl+Shift+V"]),
        Shortcut("shortcut.clipboard.paste-special", "Clipboard", "WorkbookClipboard", "Ctrl+Alt+V", "Opens Paste Special with keyboard focus, access keys, and the supported paste modes."),

        Shortcut("shortcut.formatting.bold", "Formatting", "CellFormatting", "Ctrl+B / Ctrl+2", "Toggles bold formatting with exact modifier matching.", ["Ctrl+2"]),
        Shortcut("shortcut.formatting.italic", "Formatting", "CellFormatting", "Ctrl+I / Ctrl+3", "Toggles italic formatting with exact modifier matching.", ["Ctrl+3"]),
        Shortcut("shortcut.formatting.underline", "Formatting", "CellFormatting", "Ctrl+U / Ctrl+4", "Toggles underline formatting with exact modifier matching.", ["Ctrl+4"]),
        Shortcut("shortcut.formatting.format-cells", "Formatting", "FormatCells", "Ctrl+1", "Opens Format Cells for the style fields supported by the workbook model."),
        Shortcut("shortcut.formatting.format-cells-font", "Formatting", "FormatCells", "Ctrl+Shift+F / Ctrl+Shift+P", "Opens Format Cells on the Font tab.", ["Ctrl+Shift+P"]),
        Shortcut("shortcut.formatting.number-formats", "Formatting", "CellFormatting", "Ctrl+Shift+~ / ! / @ / # / $ / % / ^", "Applies General, Number, Time, Date, Currency, Percentage, or Scientific number formatting.", ["Ctrl+Shift+!", "Ctrl+Shift+@", "Ctrl+Shift+#", "Ctrl+Shift+$", "Ctrl+Shift+%", "Ctrl+Shift+^"]),
        Shortcut("shortcut.formatting.borders", "Formatting", "CellFormatting", "Ctrl+Shift+& / Ctrl+Shift+_", "Applies an outline border or removes borders from the selection.", ["Ctrl+Shift+_"]),

        Shortcut("shortcut.navigation.arrow", "Navigation", "WorksheetNavigation", "Arrow keys", "Moves the active cell one cell in the requested direction."),
        Shortcut("shortcut.navigation.extend-arrow", "Navigation", "WorksheetNavigation", "Shift+Arrow", "Extends the current selection in the requested direction."),
        Shortcut("shortcut.navigation.data-boundary", "Navigation", "WorksheetNavigation", "Ctrl+Arrow", "Moves to the next data boundary in the requested direction."),
        Shortcut("shortcut.navigation.row-start", "Navigation", "WorksheetNavigation", "Home", "Moves to the first column in the active row."),
        Shortcut("shortcut.navigation.sheet-start", "Navigation", "WorksheetNavigation", "Ctrl+Home", "Moves to cell A1."),
        Shortcut("shortcut.navigation.used-range-end", "Navigation", "WorksheetNavigation", "Ctrl+End", "Moves to the end of the worksheet used range."),
        Shortcut("shortcut.navigation.go-to", "Navigation", "GoTo", "F5 / Ctrl+G", "Opens Go To with the reference field selected and access-keyed Go To Special support.", ["Ctrl+G"]),
        Shortcut("shortcut.navigation.scroll-active-cell", "Navigation", "WorksheetNavigation", "Ctrl+Backspace", "Scrolls the active cell into view without changing the selection."),
        Shortcut("shortcut.navigation.vertical-page", "Navigation", "WorksheetNavigation", "Page Up / Page Down", "Moves one viewport page vertically.", ["Page Down"]),
        Shortcut("shortcut.navigation.horizontal-page", "Navigation", "WorksheetNavigation", "Alt+Page Up / Alt+Page Down", "Moves one viewport page horizontally.", ["Alt+Page Down"]),
        Shortcut("shortcut.navigation.end-mode", "Navigation", "WorksheetNavigation", "End, then Arrow", "Enters End Mode and uses the next arrow to move to the data boundary."),
        Shortcut("shortcut.navigation.commit-forward", "Navigation", "WorksheetEditing", "Enter / Tab", "Commits entry and moves down or right.", ["Tab"]),
        Shortcut("shortcut.navigation.commit-backward", "Navigation", "WorksheetEditing", "Shift+Enter / Shift+Tab", "Commits entry and moves up or left.", ["Shift+Tab"]),

        Shortcut("shortcut.selection.current-region-or-sheet", "Selection", "WorksheetSelection", "Ctrl+A", "Selects the current region first, then the whole sheet."),
        Shortcut("shortcut.selection.whole-sheet", "Selection", "WorksheetSelection", "Ctrl+Shift+Space", "Selects the whole worksheet."),
        Shortcut("shortcut.selection.current-region", "Selection", "WorksheetSelection", "Ctrl+Shift+*", "Selects the current data region."),
        Shortcut("shortcut.selection.column-or-row", "Selection", "WorksheetSelection", "Ctrl+Space / Shift+Space", "Selects the current columns or rows.", ["Shift+Space"]),
        Shortcut("shortcut.selection.visible-cells", "Selection", "WorksheetSelection", "Alt+;", "Restricts the current selection to visible cells."),
        Shortcut("shortcut.selection.notes-comments", "Selection", "WorksheetSelection", "Ctrl+Shift+O", "Selects cells containing notes or comments in the current selection."),
        Shortcut("shortcut.selection.extend-or-add-mode", "Selection", "WorksheetSelection", "F8 / Shift+F8", "Toggles Extend Selection or Add to Selection mode.", ["Shift+F8"]),
        Shortcut("shortcut.selection.cycle-active-corner", "Selection", "WorksheetSelection", "Ctrl+.", "Cycles the active corner of the selection clockwise."),

        Shortcut("shortcut.editing.cell-editor", "Editing", "WorksheetEditing", "F2", "Enters cell edit mode."),
        Shortcut("shortcut.editing.formula-bar", "Editing", "WorksheetEditing", "Ctrl+F2", "Moves editing focus to the formula bar."),
        Shortcut("shortcut.editing.clear-contents", "Editing", "WorksheetEditing", "Delete", "Clears the selected cells' contents."),
        Shortcut("shortcut.editing.clear-and-edit", "Editing", "WorksheetEditing", "Backspace / Shift+Backspace", "Clears the active cell and enters inline editing.", ["Shift+Backspace"]),
        Shortcut("shortcut.editing.insert-delete", "Editing", "WorksheetStructure", "Ctrl++ / Ctrl+-", "Inserts or deletes selected rows, columns, or shifted cells through the appropriate prompt.", ["Ctrl+Shift+=", "Ctrl+-"]),

        Shortcut("shortcut.row-column.rows-hide-unhide", "Row/Column", "RowColumnLayout", "Ctrl+9 / Ctrl+Shift+9", "Hides or unhides the selected rows.", ["Ctrl+Shift+9"]),
        Shortcut("shortcut.row-column.columns-hide-unhide", "Row/Column", "RowColumnLayout", "Ctrl+0 / Ctrl+Shift+0", "Hides or unhides the selected columns.", ["Ctrl+Shift+0"]),

        Shortcut("shortcut.editing.cancel", "Editing", "WorksheetEditing", "Escape", "Cancels inline editing."),
        Shortcut("shortcut.editing.line-break", "Editing", "WorksheetEditing", "Alt+Enter", "Inserts a line break in the edited cell."),
        Shortcut("shortcut.editing.fill-selection", "Editing", "WorksheetEditing", "Ctrl+Enter", "Fills the selected range with the current entry."),
        Shortcut("shortcut.editing.copy-from-above", "Editing", "WorksheetEditing", "Ctrl+'", "Copies the formula or content from the cell above."),
        Shortcut("shortcut.editing.copy-value-from-above", "Editing", "WorksheetEditing", "Ctrl+Shift+\"", "Copies the calculated value from the cell above."),

        Shortcut("shortcut.find.find", "Find", "FindReplace", "Ctrl+F", "Opens Find with keyboard-accessible fields and commands."),
        Shortcut("shortcut.find.replace", "Find", "FindReplace", "Ctrl+H", "Opens Replace with keyboard-accessible fields and commands."),

        Shortcut("shortcut.formulas.show-formulas", "Formulas", "FormulaWorkflows", "Ctrl+`", "Toggles Show Formulas."),
        Shortcut("shortcut.formulas.paste-name", "Formulas", "NamedRanges", "F3", "Opens Paste Name for formula insertion or writing a name/reference list."),
        Shortcut("shortcut.formulas.insert-function", "Formulas", "FormulaWorkflows", "Shift+F3", "Opens Insert Function with keyboard-accessible search and category controls."),
        Shortcut("shortcut.formulas.name-manager", "Formulas", "NamedRanges", "Ctrl+F3 / Ctrl+Shift+F3", "Opens Name Manager or Create Names from Selection.", ["Ctrl+Shift+F3"]),
        Shortcut("shortcut.formulas.calculate", "Formulas", "Calculation", "F9 / Shift+F9 / Ctrl+Alt+F9 / Ctrl+Alt+Shift+F9", "Calculates the sheet or workbook, with the full variant rebuilding dependencies first.", ["Shift+F9", "Ctrl+Alt+F9", "Ctrl+Alt+Shift+F9"]),
        Shortcut("shortcut.formulas.error-checking", "Formulas", "FormulaAuditing", "Alt+Shift+F10", "Recalculates and opens Error Checking."),
        Shortcut("shortcut.formulas.expand-formula-bar", "Formulas", "FormulaWorkflows", "Ctrl+Shift+U", "Expands or collapses the formula bar."),
        Shortcut("shortcut.formulas.trace-references", "Formulas", "FormulaAuditing", "Ctrl+[ / Ctrl+] / Ctrl+Shift+[ / Ctrl+Shift+]", "Selects direct or all traceable precedents or dependents.", ["Ctrl+]", "Ctrl+Shift+[", "Ctrl+Shift+]"]),

        Shortcut("shortcut.view.outline-symbols", "View", "WorksheetView", "Ctrl+8", "Toggles worksheet outline symbols with undo support."),

        Shortcut("shortcut.review.spelling", "Review", "ReviewWorkflows", "F7", "Runs the worksheet spelling check."),
        Shortcut("shortcut.review.note-comment", "Review", "ReviewWorkflows", "Shift+F2 / Ctrl+Shift+F2", "Opens the simple-note editor or the threaded-comment workflow.", ["Ctrl+Shift+F2"]),

        Shortcut("shortcut.help.online", "Help", "ApplicationHelp", "F1", "Opens the FreeX help documentation.", isExternal: true),

        Shortcut("shortcut.view.mouse-zoom", "View", "WorksheetView", "Ctrl+Mouse Wheel", "Zooms the worksheet in or out."),
        Shortcut("shortcut.view.keyboard-zoom", "View", "WorksheetView", "Ctrl+Alt+= / Ctrl+Alt+-", "Zooms the worksheet in or out from the keyboard.", ["Ctrl+Alt+-"]),

        Shortcut("shortcut.data.filter-toggle-reapply", "Data", "AutoFilter", "Ctrl+Shift+L / Ctrl+Alt+L", "Toggles AutoFilter or reapplies the remembered filter.", ["Ctrl+Alt+L", "Alt+D, F, F"]),
        Shortcut("shortcut.data.flash-fill", "Data", "FlashFill", "Ctrl+E", "Runs Flash Fill for the active selection."),
        Shortcut("shortcut.data.dropdown", "Data", "DataDropdowns", "Alt+Down", "Opens the active validation list or AutoFilter dropdown with keyboard navigation."),
        Shortcut("shortcut.data.outline-group", "Data", "WorksheetOutline", "Alt+Shift+Right / Alt+Shift+Left", "Groups or ungroups the selected rows or columns.", ["Alt+Shift+Left"]),

        Shortcut("shortcut.sheet-tabs.activate", "Sheet Tabs", "SheetTabs", "Ctrl+Page Up / Ctrl+Page Down", "Activates the previous or next visible worksheet.", ["Ctrl+Page Down"]),
        Shortcut("shortcut.sheet-tabs.group", "Sheet Tabs", "SheetTabs", "Ctrl+Shift+Page Up / Ctrl+Shift+Page Down", "Extends the grouped-sheet selection to the previous or next visible worksheet.", ["Ctrl+Shift+Page Down"]),
        Shortcut("shortcut.sheet-tabs.insert", "Sheet Tabs", "SheetTabs", "Shift+F11 / Alt+Shift+F1", "Inserts a worksheet.", ["Alt+Shift+F1"]),

        Shortcut("shortcut.insert.autosum", "Insert", "FormulaWorkflows", "Alt+=", "Inserts a SUM formula through AutoSum."),
        Shortcut("shortcut.insert.table", "Insert", "TableWorkflows", "Ctrl+L / Ctrl+T", "Opens Create Table with focus in the table-range field.", ["Ctrl+T"]),
        Shortcut("shortcut.insert.hyperlink", "Insert", "HyperlinkWorkflows", "Ctrl+K", "Opens Insert Hyperlink for the active cell."),
        Shortcut("shortcut.insert.open-hyperlink", "Insert", "HyperlinkWorkflows", "Ctrl+Enter on hyperlink cell", "Opens the active cell's hyperlink without entering edit mode.", isExternal: true),
        Shortcut("shortcut.insert.chart", "Insert", "ChartWorkflows", "Alt+F1 / F11", "Inserts an embedded default chart or creates and activates a chart sheet.", ["F11"]),

        Shortcut("shortcut.analysis.quick-analysis", "Analysis", "QuickAnalysis", "Ctrl+Q", "Opens Quick Analysis for the current selection with keyboard focus, command routing, and previews."),

        Shortcut("shortcut.workbook.statistics", "Workbook", "WorkbookShell", "Ctrl+Shift+G", "Opens Workbook Statistics."),
        Shortcut("shortcut.workbook.next-window", "Workbook", "WorkbookShell", "Ctrl+F6 / Ctrl+Tab", "Activates the next live workbook window with wraparound.", ["Ctrl+Tab"]),
        Shortcut("shortcut.workbook.previous-window", "Workbook", "WorkbookShell", "Ctrl+Shift+F6 / Ctrl+Shift+Tab", "Activates the previous live workbook window with wraparound.", ["Ctrl+Shift+Tab"]),
        Shortcut("shortcut.workbook.window-state", "Workbook", "WorkbookShell", "Ctrl+F5 / Ctrl+F7 / Ctrl+F8 / Ctrl+F9 / Ctrl+F10", "Restores, moves, sizes, minimizes, or maximizes the workbook window as allowed by its state.", ["Ctrl+F7", "Ctrl+F8", "Ctrl+F9", "Ctrl+F10"], isNative: true),

        Shortcut("shortcut.ui.keytips", "UI", "ApplicationShell", "F10", "Enters ribbon keytip mode from worksheet or shell text-entry focus."),
        Shortcut("shortcut.ui.focus-cycle", "UI", "ApplicationShell", "F6 / Shift+F6", "Cycles shell focus forward or backward through available worksheet and shell regions.", ["Shift+F6"]),
        Shortcut("shortcut.ui.ribbon-focus", "UI", "RibbonFocus", "Tab / Shift+Tab in ribbon", "Moves keyboard focus among visible ribbon tabs and commands.", ["Shift+Tab in ribbon"]),
        Shortcut("shortcut.ui.context-menu", "UI", "ContextMenus", "Shift+F10 / Menu key", "Opens the context menu for the currently focused worksheet, sheet-tab, Backstage, or object target.", ["Menu key"]),

        Shortcut("shortcut.editing.current-date-time", "Editing", "WorksheetEditing", "Ctrl+; / Ctrl+Shift+;", "Inserts the current date or current time.", ["Ctrl+Shift+;"]),
        Shortcut("shortcut.editing.fill-down-right", "Editing", "WorksheetEditing", "Ctrl+D / Ctrl+R", "Fills down or right with adjusted formula references and undo support.", ["Ctrl+R"]),
        Shortcut("shortcut.formatting.strikethrough", "Formatting", "CellFormatting", "Ctrl+5", "Toggles strikethrough formatting."),
        Shortcut("shortcut.formulas.reference-mode", "Formulas", "FormulaEditing", "F4 while editing a formula reference", "Cycles supported formula references through relative and absolute modes."),
        Shortcut("shortcut.editing.repeat", "Editing", "CommandHistory", "F4 outside formula editing", "Repeats the last repeatable workbook action using a fresh undoable command."),
        Shortcut(
            "shortcut.ribbon.keytip-routing",
            "Ribbon",
            "RibbonKeytips",
            "Alt, then F/H/N/J/P/M/A/R/W/Y; Alt+F/H/N/J/P/M/A/R/W/Y; Alt+1/2/3",
            "Routes top-level ribbon, Backstage, contextual-tab, command, menu, and Quick Access Toolbar keytips.",
            [
                "Alt, then H", "Alt, then N", "Alt, then J", "Alt, then P", "Alt, then M", "Alt, then A", "Alt, then R", "Alt, then W", "Alt, then Y",
                "Alt+F", "Alt+H", "Alt+N", "Alt+J", "Alt+P", "Alt+M", "Alt+A", "Alt+R", "Alt+W", "Alt+Y",
                "Alt+1", "Alt+2", "Alt+3",
            ]),
    ];

    public static IReadOnlyList<WorksheetRangeValidationTarget> WorksheetRangeTargets { get; } =
    [
        RangeTarget("range.advanced-filter.list-range", "Advanced Filter", "AdvancedFilterPlanner", "List range", "Selects the source list, including its header row.", ["List"]),
        RangeTarget("range.advanced-filter.criteria-range", "Advanced Filter", "AdvancedFilterPlanner", "Criteria range", "Selects the criteria range, including its criteria headers.", ["Criteria"]),
        RangeTarget("range.advanced-filter.copy-to", "Advanced Filter", "AdvancedFilterPlanner", "Copy to", "Selects the destination cell or one-row output-header range.", ["Copy destination"]),

        RangeTarget("range.consolidate.reference", "Consolidate", "ConsolidateDialogPlanner", "Reference", "Adds a worksheet source range to the consolidation references."),
        RangeTarget("range.consolidate.destination-cell", "Consolidate", "ConsolidateDialogPlanner", "Destination cell", "Selects the top-left cell for consolidated output.", ["Destination"]),

        RangeTarget("range.data-table.row-input-cell", "Data Table", "DataTableInputParser", "Row input cell", "Selects the single workbook input cell substituted across the table row."),
        RangeTarget("range.data-table.column-input-cell", "Data Table", "DataTableInputParser", "Column input cell", "Selects the single workbook input cell substituted down the table column."),

        RangeTarget("range.data-validation.formula-1", "Data Validation", "DataValidationDialogPlanner", "Formula 1 / Source", "Selects the first validation reference, including the list source or first bound.", ["Source", "Minimum"]),
        RangeTarget("range.data-validation.formula-2", "Data Validation", "DataValidationDialogPlanner", "Formula 2", "Selects the second validation bound when the chosen operator requires one.", ["Maximum"]),

        RangeTarget("range.sparklines.data-range", "Sparklines", "SparklineDialogPlanner", "Data range", "Selects the worksheet values rendered by the sparklines."),
        RangeTarget("range.sparklines.location-range", "Sparklines", "SparklineDialogPlanner", "Location range", "Selects the cells that will contain the sparklines.", ["Location"]),

        RangeTarget("range.page-setup.print-area", "Page Setup", "PageSetupDialogModel", "Print area", "Selects the worksheet range included in printed output."),
        RangeTarget("range.page-setup.rows-to-repeat", "Page Setup", "PageSetupDialogModel", "Rows to repeat at top", "Selects the full rows repeated at the top of each printed page.", ["Print titles rows"]),
        RangeTarget("range.page-setup.columns-to-repeat", "Page Setup", "PageSetupDialogModel", "Columns to repeat at left", "Selects the full columns repeated at the left of each printed page.", ["Print titles columns"]),

        RangeTarget("range.goal-seek.set-cell", "Goal Seek", "GoalSeekRequestParser", "Set cell", "Selects the single formula cell whose result should reach the target value."),
        RangeTarget("range.goal-seek.changing-cell", "Goal Seek", "GoalSeekRequestParser", "By changing cell", "Selects the single input cell Goal Seek may vary.", ["Changing cell"]),

        RangeTarget("range.named-ranges.selected-refers-to", "Named Ranges", "NamedRangeDialogPlanner", "Selected name Refers to", "Replaces the selected name's reference from a worksheet selection.", ["Name Manager Refers to"]),
        RangeTarget("range.named-ranges.definition-refers-to", "Named Ranges", "NamedRangeDialogPlanner", "Name definition Refers to", "Populates the new or edited name definition from a worksheet selection.", ["New Name Refers to", "Edit Name Refers to"]),

        RangeTarget("range.pivot-create.source", "Pivot Create", "PivotCreatePlanner", "Table/Range", "Selects the source data range used to create the PivotTable.", ["Source range"]),
        RangeTarget("range.pivot-create.destination", "Pivot Create", "PivotCreatePlanner", "Location", "Selects the destination cell when creating the PivotTable on an existing worksheet.", ["Destination range"]),

        RangeTarget("range.scenario-manager.changing-cells", "Scenario Manager", "ScenarioManagerDialogPlanner", "Changing cells", "Selects the cells whose values are stored by a scenario."),
        RangeTarget("range.scenario-manager.result-cells", "Scenario Manager", "ScenarioManagerDialogPlanner", "Result cells", "Selects optional result cells included in a scenario summary."),

        RangeTarget("range.allow-edit-range.range", "Allow Edit Range", "AllowEditRangeDialogPlanner", "Range", "Selects the cells covered by the editable protected-sheet range.", ["Refers to cells"]),
        RangeTarget("range.create-table.range", "Create Table", "CreateTableDialogPlanner", "Where is the data for your table?", "Selects the worksheet range converted into a structured table.", ["Table range"]),
        RangeTarget("range.chart-data-source.range", "Chart Data Source", "SelectDataSourcePlanner", "Chart data range", "Selects the worksheet data used to populate the chart.", ["Data source"]),
        RangeTarget("range.function-argument.reference", "Function Argument", "FunctionArgumentsPlanner", "Function argument", "Inserts the selected worksheet reference into the active function argument.", ["Argument reference"]),
        RangeTarget("range.conditional-format.applies-to", "Conditional Format", "ManageConditionalFormatsPlanner", "Applies to", "Replaces the selected conditional-format rule's application range.", ["Applies To"]),
        RangeTarget("range.move-pivot.destination", "Move Pivot", "PivotUiPlanner", "Location", "Selects the top-left destination cell for the moved PivotTable.", ["Destination"]),
        RangeTarget("range.pivot-data-source.range", "Pivot Data Source", "PivotDataSourcePlanner", "Table/Range", "Selects the replacement source range for the PivotTable.", ["Source range"]),
        RangeTarget("range.text-to-columns.destination", "Text to Columns", "TextToColumnsDialogPlanner", "Destination", "Selects the single top-left destination cell for split output."),
        RangeTarget("range.resize-table.range", "Resize Table", "TableResizePlanner", "Select the new data range for your table", "Selects the table's resized range while preserving its top-left cell.", ["Table range"]),
    ];

    private static KeyboardShortcutValidationScenario Shortcut(
        string id,
        string area,
        string owner,
        string displayChord,
        string expectedBehavior,
        IReadOnlyList<string>? aliases = null,
        bool isNative = false,
        bool isExternal = false) =>
        new(id, area, owner, displayChord, aliases ?? [], InteractionsFor(id), expectedBehavior, isNative, isExternal);

    private static IReadOnlyList<ShortcutInteractionDescriptor> InteractionsFor(string id)
    {
        var windowsInteractions = WindowsInteractionsFor(id);
        var nativeInteractions = NativeMenuInteractionsFor(windowsInteractions);
        return nativeInteractions.Count == 0
            ? windowsInteractions
            : [.. windowsInteractions, .. nativeInteractions];
    }

    private static IReadOnlyList<ShortcutInteractionDescriptor> WindowsInteractionsFor(string id) => id switch
    {
        "shortcut.file.new-workbook" => [K("Ctrl+N", Workbook, "N", Ctrl)],
        "shortcut.file.open" => [K("Ctrl+O", Workbook, "O", Ctrl), K("Ctrl+F12", Workbook, "F12", Ctrl)],
        "shortcut.file.save" => [K("Ctrl+S", Workbook, "S", Ctrl), K("Shift+F12", Workbook, "F12", Shift)],
        "shortcut.file.close-workbook" => [K("Ctrl+W", Workbook, "W", Ctrl), K("Ctrl+F4", Workbook, "F4", Ctrl)],
        "shortcut.file.print" => [K("Ctrl+P", Workbook, "P", Ctrl), K("Ctrl+Shift+F12", Workbook, "F12", CtrlShift)],

        "shortcut.edit.undo" => [K("Ctrl+Z", Workbook, "Z", Ctrl), K("Alt+Backspace", Workbook, "Backspace", Alt)],
        "shortcut.edit.redo" => [K("Ctrl+Y", Workbook, "Y", Ctrl), K("Ctrl+Shift+Z", Workbook, "Z", CtrlShift)],

        "shortcut.clipboard.copy" => [K("Ctrl+C", Worksheet, "C", Ctrl), K("Ctrl+Insert", Worksheet, "Insert", Ctrl)],
        "shortcut.clipboard.cut" => [K("Ctrl+X", Worksheet, "X", Ctrl), K("Shift+Delete", Worksheet, "Delete", Shift)],
        "shortcut.clipboard.paste" => [K("Ctrl+V", Worksheet, "V", Ctrl), K("Shift+Insert", Worksheet, "Insert", Shift), K("Ctrl+Shift+V", Worksheet, "V", CtrlShift)],
        "shortcut.clipboard.paste-special" => [K("Ctrl+Alt+V", Worksheet, "V", CtrlAlt)],

        "shortcut.formatting.bold" => [K("Ctrl+B", Worksheet, "B", Ctrl), K("Ctrl+2", Worksheet, "2", Ctrl)],
        "shortcut.formatting.italic" => [K("Ctrl+I", Worksheet, "I", Ctrl), K("Ctrl+3", Worksheet, "3", Ctrl)],
        "shortcut.formatting.underline" => [K("Ctrl+U", Worksheet, "U", Ctrl), K("Ctrl+4", Worksheet, "4", Ctrl)],
        "shortcut.formatting.format-cells" => [K("Ctrl+1", Worksheet, "1", Ctrl)],
        "shortcut.formatting.format-cells-font" => [K("Ctrl+Shift+F", Worksheet, "F", CtrlShift), K("Ctrl+Shift+P", Worksheet, "P", CtrlShift)],
        "shortcut.formatting.number-formats" =>
        [
            K("Ctrl+Shift+~", Worksheet, "Grave", CtrlShift),
            K("Ctrl+Shift+!", Worksheet, "1", CtrlShift),
            K("Ctrl+Shift+@", Worksheet, "2", CtrlShift),
            K("Ctrl+Shift+#", Worksheet, "3", CtrlShift),
            K("Ctrl+Shift+$", Worksheet, "4", CtrlShift),
            K("Ctrl+Shift+%", Worksheet, "5", CtrlShift),
            K("Ctrl+Shift+^", Worksheet, "6", CtrlShift),
        ],
        "shortcut.formatting.borders" => [K("Ctrl+Shift+&", Worksheet, "7", CtrlShift), K("Ctrl+Shift+_", Worksheet, "Minus", CtrlShift)],

        "shortcut.navigation.arrow" => ArrowInteractions(Worksheet, None),
        "shortcut.navigation.extend-arrow" => ArrowInteractions(WorksheetSelection, Shift),
        "shortcut.navigation.data-boundary" => ArrowInteractions(Worksheet, Ctrl),
        "shortcut.navigation.row-start" => [K("Home", Worksheet, "Home")],
        "shortcut.navigation.sheet-start" => [K("Ctrl+Home", Worksheet, "Home", Ctrl)],
        "shortcut.navigation.used-range-end" => [K("Ctrl+End", Worksheet, "End", Ctrl)],
        "shortcut.navigation.go-to" => [K("F5", Worksheet, "F5"), K("Ctrl+G", Worksheet, "G", Ctrl)],
        "shortcut.navigation.scroll-active-cell" => [K("Ctrl+Backspace", Worksheet, "Backspace", Ctrl)],
        "shortcut.navigation.vertical-page" => [K("Page Up", Worksheet, "PageUp"), K("Page Down", Worksheet, "PageDown")],
        "shortcut.navigation.horizontal-page" => [K("Alt+Page Up", Worksheet, "PageUp", Alt), K("Alt+Page Down", Worksheet, "PageDown", Alt)],
        "shortcut.navigation.end-mode" => EndModeInteractions(),
        "shortcut.navigation.commit-forward" => [K("Enter", Worksheet, "Enter"), K("Tab", Worksheet, "Tab")],
        "shortcut.navigation.commit-backward" => [K("Shift+Enter", Worksheet, "Enter", Shift), K("Shift+Tab", Worksheet, "Tab", Shift)],

        "shortcut.selection.current-region-or-sheet" => [K("Ctrl+A", WorksheetSelection, "A", Ctrl)],
        "shortcut.selection.whole-sheet" => [K("Ctrl+Shift+Space", WorksheetSelection, "Space", CtrlShift)],
        "shortcut.selection.current-region" => [K("Ctrl+Shift+*", WorksheetSelection, "8", CtrlShift)],
        "shortcut.selection.column-or-row" => [K("Ctrl+Space", WorksheetSelection, "Space", Ctrl), K("Shift+Space", WorksheetSelection, "Space", Shift)],
        "shortcut.selection.visible-cells" => [K("Alt+;", WorksheetSelection, "Semicolon", Alt)],
        "shortcut.selection.notes-comments" => [K("Ctrl+Shift+O", WorksheetSelection, "O", CtrlShift)],
        "shortcut.selection.extend-or-add-mode" => [K("F8", WorksheetSelection, "F8"), K("Shift+F8", WorksheetSelection, "F8", Shift)],
        "shortcut.selection.cycle-active-corner" => [K("Ctrl+.", WorksheetSelection, "Period", Ctrl), K("Ctrl+Numpad Decimal", WorksheetSelection, "Decimal", Ctrl)],

        "shortcut.editing.cell-editor" => [K("F2", Worksheet, "F2")],
        "shortcut.editing.formula-bar" => [K("Ctrl+F2", Worksheet, "F2", Ctrl)],
        "shortcut.editing.clear-contents" => [K("Delete", WorksheetSelection, "Delete")],
        "shortcut.editing.clear-and-edit" => [K("Backspace", Worksheet, "Backspace"), K("Shift+Backspace", Worksheet, "Backspace", Shift)],
        "shortcut.editing.insert-delete" => [K("Ctrl++", WorksheetSelection, "Plus", Ctrl), K("Ctrl+Shift+=", WorksheetSelection, "Equals", CtrlShift), K("Ctrl+-", WorksheetSelection, "Minus", Ctrl)],

        "shortcut.row-column.rows-hide-unhide" => [K("Ctrl+9", WorksheetSelection, "9", Ctrl), K("Ctrl+Shift+9", WorksheetSelection, "9", CtrlShift)],
        "shortcut.row-column.columns-hide-unhide" => [K("Ctrl+0", WorksheetSelection, "0", Ctrl), K("Ctrl+Shift+0", WorksheetSelection, "0", CtrlShift)],

        "shortcut.editing.cancel" => [K("Escape", CellEditor, "Escape", kind: ShortcutInteractionKind.ContextualKeyGesture)],
        "shortcut.editing.line-break" => [K("Alt+Enter", CellEditor, "Enter", Alt, ShortcutInteractionKind.ContextualKeyGesture)],
        "shortcut.editing.fill-selection" => [K("Ctrl+Enter", WorksheetSelection, "Enter", Ctrl, ShortcutInteractionKind.ContextualKeyGesture)],
        "shortcut.editing.copy-from-above" => [K("Ctrl+'", Worksheet, "Quote", Ctrl)],
        "shortcut.editing.copy-value-from-above" => [K("Ctrl+Shift+\"", Worksheet, "Quote", CtrlShift)],

        "shortcut.find.find" => [K("Ctrl+F", Workbook, "F", Ctrl)],
        "shortcut.find.replace" => [K("Ctrl+H", Workbook, "H", Ctrl)],

        "shortcut.formulas.show-formulas" => [K("Ctrl+`", Worksheet, "Grave", Ctrl)],
        "shortcut.formulas.paste-name" => [K("F3", Worksheet, "F3")],
        "shortcut.formulas.insert-function" => [K("Shift+F3", Worksheet, "F3", Shift)],
        "shortcut.formulas.name-manager" => [K("Ctrl+F3", Workbook, "F3", Ctrl), K("Ctrl+Shift+F3", Workbook, "F3", CtrlShift)],
        "shortcut.formulas.calculate" =>
        [
            K("F9", Workbook, "F9"),
            K("Shift+F9", Worksheet, "F9", Shift),
            K("Ctrl+Alt+F9", Workbook, "F9", CtrlAlt),
            K("Ctrl+Alt+Shift+F9", Workbook, "F9", CtrlAltShift),
        ],
        "shortcut.formulas.error-checking" => [K("Alt+Shift+F10", Worksheet, "F10", AltShift)],
        "shortcut.formulas.expand-formula-bar" => [K("Ctrl+Shift+U", FormulaBar, "U", CtrlShift, ShortcutInteractionKind.ContextualKeyGesture)],
        "shortcut.formulas.trace-references" =>
        [
            K("Ctrl+[", Worksheet, "OpenBracket", Ctrl),
            K("Ctrl+]", Worksheet, "CloseBracket", Ctrl),
            K("Ctrl+Shift+[", Worksheet, "OpenBracket", CtrlShift),
            K("Ctrl+Shift+]", Worksheet, "CloseBracket", CtrlShift),
        ],

        "shortcut.view.outline-symbols" => [K("Ctrl+8", Worksheet, "8", Ctrl)],

        "shortcut.review.spelling" => [K("F7", Worksheet, "F7")],
        "shortcut.review.note-comment" => [K("Shift+F2", Worksheet, "F2", Shift), K("Ctrl+Shift+F2", Worksheet, "F2", CtrlShift)],

        "shortcut.help.online" => [K("F1", Application, "F1")],

        "shortcut.view.mouse-zoom" => [Mouse("Ctrl+Mouse Wheel", Worksheet, "MouseWheel", Ctrl)],
        "shortcut.view.keyboard-zoom" =>
        [
            K("Ctrl+Alt+=", Worksheet, "Equals", CtrlAlt),
            K("Ctrl+Alt+Numpad +", Worksheet, "NumpadPlus", CtrlAlt),
            K("Ctrl+Alt+-", Worksheet, "Minus", CtrlAlt),
            K("Ctrl+Alt+Numpad -", Worksheet, "NumpadMinus", CtrlAlt),
        ],

        "shortcut.data.filter-toggle-reapply" =>
        [
            K("Ctrl+Shift+L", Worksheet, "L", CtrlShift),
            K("Ctrl+Alt+L", Worksheet, "L", CtrlAlt),
            S("Alt+D, F, F", Worksheet, ShortcutInteractionKind.KeySequence, G("D", Alt), G("F"), G("F")),
        ],
        "shortcut.data.flash-fill" => [K("Ctrl+E", WorksheetSelection, "E", Ctrl)],
        "shortcut.data.dropdown" => [K("Alt+Down", DataValidationListOrFilterHeader, "ArrowDown", Alt, ShortcutInteractionKind.ContextualKeyGesture)],
        "shortcut.data.outline-group" => [K("Alt+Shift+Right", WorksheetSelection, "ArrowRight", AltShift), K("Alt+Shift+Left", WorksheetSelection, "ArrowLeft", AltShift)],

        "shortcut.sheet-tabs.activate" => [K("Ctrl+Page Up", SheetTabs, "PageUp", Ctrl), K("Ctrl+Page Down", SheetTabs, "PageDown", Ctrl)],
        "shortcut.sheet-tabs.group" => [K("Ctrl+Shift+Page Up", SheetTabs, "PageUp", CtrlShift), K("Ctrl+Shift+Page Down", SheetTabs, "PageDown", CtrlShift)],
        "shortcut.sheet-tabs.insert" => [K("Shift+F11", SheetTabs, "F11", Shift), K("Alt+Shift+F1", SheetTabs, "F1", AltShift)],

        "shortcut.insert.autosum" => [K("Alt+=", Worksheet, "Equals", Alt)],
        "shortcut.insert.table" => [K("Ctrl+L", WorksheetSelection, "L", Ctrl), K("Ctrl+T", WorksheetSelection, "T", Ctrl)],
        "shortcut.insert.hyperlink" => [K("Ctrl+K", Worksheet, "K", Ctrl)],
        "shortcut.insert.open-hyperlink" => [K("Ctrl+Enter on hyperlink cell", HyperlinkCell, "Enter", Ctrl, ShortcutInteractionKind.ContextualKeyGesture)],
        "shortcut.insert.chart" => [K("Alt+F1", WorksheetSelection, "F1", Alt), K("F11", WorksheetSelection, "F11")],

        "shortcut.analysis.quick-analysis" => [K("Ctrl+Q", WorksheetSelection, "Q", Ctrl)],

        "shortcut.workbook.statistics" => [K("Ctrl+Shift+G", Workbook, "G", CtrlShift)],
        "shortcut.workbook.next-window" => [K("Ctrl+F6", WorkbookWindow, "F6", Ctrl), K("Ctrl+Tab", WorkbookWindow, "Tab", Ctrl)],
        "shortcut.workbook.previous-window" => [K("Ctrl+Shift+F6", WorkbookWindow, "F6", CtrlShift), K("Ctrl+Shift+Tab", WorkbookWindow, "Tab", CtrlShift)],
        "shortcut.workbook.window-state" =>
        [
            K("Ctrl+F5", WorkbookWindow, "F5", Ctrl),
            K("Ctrl+F7", WorkbookWindow, "F7", Ctrl),
            K("Ctrl+F8", WorkbookWindow, "F8", Ctrl),
            K("Ctrl+F9", WorkbookWindow, "F9", Ctrl),
            K("Ctrl+F10", WorkbookWindow, "F10", Ctrl),
        ],

        "shortcut.ui.keytips" => [K("F10", Ribbon, "F10", kind: ShortcutInteractionKind.ContextualKeyGesture)],
        "shortcut.ui.focus-cycle" => [K("F6", Application, "F6"), K("Shift+F6", Application, "F6", Shift)],
        "shortcut.ui.ribbon-focus" => [K("Tab in ribbon", Ribbon, "Tab", kind: ShortcutInteractionKind.ContextualKeyGesture), K("Shift+Tab in ribbon", Ribbon, "Tab", Shift, ShortcutInteractionKind.ContextualKeyGesture)],
        "shortcut.ui.context-menu" => [K("Shift+F10", FocusedContextTarget, "F10", Shift, ShortcutInteractionKind.ContextualKeyGesture), K("Menu key", FocusedContextTarget, "Menu", kind: ShortcutInteractionKind.ContextualKeyGesture)],

        "shortcut.editing.current-date-time" => [K("Ctrl+;", Worksheet, "Semicolon", Ctrl), K("Ctrl+Shift+;", Worksheet, "Semicolon", CtrlShift)],
        "shortcut.editing.fill-down-right" => [K("Ctrl+D", WorksheetSelection, "D", Ctrl), K("Ctrl+R", WorksheetSelection, "R", Ctrl)],
        "shortcut.formatting.strikethrough" => [K("Ctrl+5", Worksheet, "5", Ctrl)],
        "shortcut.formulas.reference-mode" => [K("F4 while editing a formula reference", FormulaReferenceEditor, "F4", kind: ShortcutInteractionKind.ContextualKeyGesture)],
        "shortcut.editing.repeat" => [K("F4 outside formula editing", Worksheet, "F4", kind: ShortcutInteractionKind.ContextualKeyGesture)],
        "shortcut.ribbon.keytip-routing" => RibbonKeytipInteractions(),

        _ => throw new ArgumentOutOfRangeException(nameof(id), id, "Unknown shortcut inventory ID."),
    };

    private static IReadOnlyList<ShortcutInteractionDescriptor> ArrowInteractions(
        ShortcutInteractionContext context,
        ShortcutModifierKeys modifiers) =>
    [
        K(Display(modifiers, "Up"), context, "ArrowUp", modifiers),
        K(Display(modifiers, "Down"), context, "ArrowDown", modifiers),
        K(Display(modifiers, "Left"), context, "ArrowLeft", modifiers),
        K(Display(modifiers, "Right"), context, "ArrowRight", modifiers),
    ];

    private static IReadOnlyList<ShortcutInteractionDescriptor> EndModeInteractions() =>
    [
        S("End, then Up", Worksheet, ShortcutInteractionKind.KeySequence, G("End"), G("ArrowUp")),
        S("End, then Down", Worksheet, ShortcutInteractionKind.KeySequence, G("End"), G("ArrowDown")),
        S("End, then Left", Worksheet, ShortcutInteractionKind.KeySequence, G("End"), G("ArrowLeft")),
        S("End, then Right", Worksheet, ShortcutInteractionKind.KeySequence, G("End"), G("ArrowRight")),
    ];

    private static IReadOnlyList<ShortcutInteractionDescriptor> NativeMenuInteractionsFor(
        IReadOnlyList<ShortcutInteractionDescriptor> windowsInteractions)
    {
        var routed = windowsInteractions
            .Select(interaction =>
                TryResolveWindowsInteraction(interaction, out var route)
                    ? (Interaction: interaction, Route: (WorkbookShortcutRoute?)route)
                    : (Interaction: interaction, Route: (WorkbookShortcutRoute?)null))
            .Where(item => item.Route is not null)
            .ToArray();
        var native = new List<ShortcutInteractionDescriptor>();

        foreach (var rule in WorkbookKeyboardShortcutCatalog.Rules)
        {
            if (rule.NativeMenuChord is not { } chord || chord == rule.WindowsChord)
                continue;

            var source = routed.FirstOrDefault(item => item.Route == rule.Route).Interaction;
            if (source is null)
                continue;

            native.Add(K(
                Display(chord),
                source.Context,
                KeyName(chord.Key),
                Modifiers(chord.Modifiers)));
        }

        return native
            .DistinctBy(interaction => (interaction.Steps[0].Key, interaction.Steps[0].Modifiers))
            .ToArray();
    }

    private static bool TryResolveWindowsInteraction(
        ShortcutInteractionDescriptor interaction,
        out WorkbookShortcutRoute route)
    {
        route = default;
        return interaction.Steps.Count == 1 &&
            TryWorkbookKey(interaction.Steps[0].Key, out var key) &&
            WorkbookKeyboardShortcutCatalog.TryGetWindowsRoute(
                key,
                WorkbookModifiers(interaction.Steps[0].Modifiers),
                out route);
    }

    private static IReadOnlyList<ShortcutInteractionDescriptor> RibbonKeytipInteractions()
    {
        var interactions = new List<ShortcutInteractionDescriptor>();
        foreach (var key in new[] { "F", "H", "N", "J", "P", "M", "A", "R", "W", "Y" })
        {
            interactions.Add(S($"Alt, then {key}", Ribbon, ShortcutInteractionKind.RibbonKeytipSequence, G("Alt"), G(key)));
            interactions.Add(K($"Alt+{key}", Ribbon, key, Alt));
        }

        foreach (var key in new[] { "1", "2", "3" })
        {
            interactions.Add(S($"Alt, then {key}", Ribbon, ShortcutInteractionKind.RibbonKeytipSequence, G("Alt"), G(key)));
            interactions.Add(K($"Alt+{key}", Ribbon, key, Alt));
        }

        foreach (var sequence in DocumentedNestedRibbonKeytips())
            interactions.Add(RibbonSequence(sequence));

        return interactions;
    }

    private static IReadOnlyList<string[]> DocumentedNestedRibbonKeytips() =>
    [
        .. new[] { "H", "N", "O", "S", "A", "P", "E", "I", "R", "C", "D", "T", "Z" }
            .Select(command => new[] { "F", command }),
        ["J", "A"], ["J", "D"], ["J", "C"], ["J", "F"], ["J", "S"], ["J", "P"],
        ["N", "SH", "R"], ["N", "CH"],
        ["M", "U"], ["M", "O"],
        ["P", "M"], ["P", "OR"], ["P", "SZ"], ["P", "PA"],
        ["P", "BK", "I"], ["P", "BK", "R"], ["P", "BK", "A"],
        ["R", "N"], ["R", "PN"],
        ["W", "L"], ["W", "P"], ["W", "I"],
        ["W", "VG"], ["W", "VH"], ["W", "VF"], ["W", "RU"],
        ["W", "Q", "2"], ["W", "Z1"], ["W", "ZS"],
        ["W", "FP", "R"], ["W", "FP", "C"], ["W", "FP", "F"], ["W", "FP", "U"],
        ["W", "SP"], ["W", "A", "V"], ["W", "A", "C"],
        ["A", "W"], ["A", "G", "G"], ["A", "U", "U"],
        ["H", "B", "S", "D"],
    ];

    private static ShortcutInteractionDescriptor RibbonSequence(IReadOnlyList<string> tokens)
    {
        var steps = new List<ShortcutGestureStep> { G(tokens[0][0].ToString(), Alt) };
        foreach (var token in tokens.Skip(1))
        foreach (var character in token)
            steps.Add(G(character.ToString()));
        return S($"Alt,{string.Join(',', tokens)}", Ribbon, ShortcutInteractionKind.RibbonKeytipSequence, [.. steps]);
    }

    private static bool TryWorkbookKey(string key, out WorkbookShortcutKey workbookKey)
    {
        workbookKey = default;
        var normalized = key switch
        {
            "Backspace" => nameof(WorkbookShortcutKey.Back),
            "Grave" => nameof(WorkbookShortcutKey.Oem3),
            "Minus" => nameof(WorkbookShortcutKey.OemMinus),
            "Plus" or "Equals" => nameof(WorkbookShortcutKey.OemPlus),
            "1" => nameof(WorkbookShortcutKey.D1),
            "2" => nameof(WorkbookShortcutKey.D2),
            "3" => nameof(WorkbookShortcutKey.D3),
            "4" => nameof(WorkbookShortcutKey.D4),
            "5" => nameof(WorkbookShortcutKey.D5),
            "6" => nameof(WorkbookShortcutKey.D6),
            "7" => nameof(WorkbookShortcutKey.D7),
            "PageUp" => nameof(WorkbookShortcutKey.PageUp),
            "PageDown" => nameof(WorkbookShortcutKey.PageDown),
            _ when key.All(char.IsAsciiDigit) => null,
            _ => key,
        };
        return normalized is not null && Enum.TryParse(normalized, ignoreCase: true, out workbookKey);
    }

    private static WorkbookShortcutModifiers WorkbookModifiers(ShortcutModifierKeys modifiers)
    {
        var result = WorkbookShortcutModifiers.None;
        if (modifiers.HasFlag(ShortcutModifierKeys.Control)) result |= WorkbookShortcutModifiers.Control;
        if (modifiers.HasFlag(ShortcutModifierKeys.Shift)) result |= WorkbookShortcutModifiers.Shift;
        if (modifiers.HasFlag(ShortcutModifierKeys.Alt)) result |= WorkbookShortcutModifiers.Alt;
        if (modifiers.HasFlag(ShortcutModifierKeys.Meta)) result |= WorkbookShortcutModifiers.Meta;
        return result;
    }

    private static ShortcutModifierKeys Modifiers(WorkbookShortcutModifiers modifiers)
    {
        var result = ShortcutModifierKeys.None;
        if (modifiers.HasFlag(WorkbookShortcutModifiers.Control)) result |= ShortcutModifierKeys.Control;
        if (modifiers.HasFlag(WorkbookShortcutModifiers.Shift)) result |= ShortcutModifierKeys.Shift;
        if (modifiers.HasFlag(WorkbookShortcutModifiers.Alt)) result |= ShortcutModifierKeys.Alt;
        if (modifiers.HasFlag(WorkbookShortcutModifiers.Meta)) result |= ShortcutModifierKeys.Meta;
        return result;
    }

    private static string KeyName(WorkbookShortcutKey key) => key switch
    {
        WorkbookShortcutKey.Back => "Backspace",
        WorkbookShortcutKey.Oem3 => "Grave",
        WorkbookShortcutKey.OemMinus => "Minus",
        WorkbookShortcutKey.OemPlus => "Plus",
        WorkbookShortcutKey.D1 => "1",
        WorkbookShortcutKey.D2 => "2",
        WorkbookShortcutKey.D3 => "3",
        WorkbookShortcutKey.D4 => "4",
        WorkbookShortcutKey.D5 => "5",
        WorkbookShortcutKey.D6 => "6",
        WorkbookShortcutKey.D7 => "7",
        WorkbookShortcutKey.PageUp => "PageUp",
        WorkbookShortcutKey.PageDown => "PageDown",
        _ => key.ToString(),
    };

    private static string Display(WorkbookShortcutChord chord)
    {
        var parts = new List<string>();
        if (chord.Modifiers.HasFlag(WorkbookShortcutModifiers.Meta)) parts.Add("Cmd");
        if (chord.Modifiers.HasFlag(WorkbookShortcutModifiers.Control)) parts.Add("Ctrl");
        if (chord.Modifiers.HasFlag(WorkbookShortcutModifiers.Alt)) parts.Add("Alt");
        if (chord.Modifiers.HasFlag(WorkbookShortcutModifiers.Shift)) parts.Add("Shift");
        parts.Add(KeyName(chord.Key));
        return string.Join('+', parts);
    }

    private static ShortcutInteractionDescriptor K(
        string displayText,
        ShortcutInteractionContext context,
        string key,
        ShortcutModifierKeys modifiers = None,
        ShortcutInteractionKind kind = ShortcutInteractionKind.KeyGesture) =>
        new(displayText, kind, context, [G(key, modifiers)]);

    private static ShortcutInteractionDescriptor S(
        string displayText,
        ShortcutInteractionContext context,
        ShortcutInteractionKind kind,
        params ShortcutGestureStep[] steps) =>
        new(displayText, kind, context, steps);

    private static ShortcutInteractionDescriptor Mouse(
        string displayText,
        ShortcutInteractionContext context,
        string input,
        ShortcutModifierKeys modifiers) =>
        new(displayText, ShortcutInteractionKind.MouseWheel, context, [], input, modifiers);

    private static ShortcutGestureStep G(
        string key,
        ShortcutModifierKeys modifiers = None) =>
        new(key, modifiers);

    private static string Display(ShortcutModifierKeys modifiers, string key) =>
        modifiers switch
        {
            ShortcutModifierKeys.None => key,
            ShortcutModifierKeys.Control => $"Ctrl+{key}",
            ShortcutModifierKeys.Shift => $"Shift+{key}",
            _ => $"{modifiers}+{key}",
        };

    private const ShortcutModifierKeys None = ShortcutModifierKeys.None;
    private const ShortcutModifierKeys Ctrl = ShortcutModifierKeys.Control;
    private const ShortcutModifierKeys Shift = ShortcutModifierKeys.Shift;
    private const ShortcutModifierKeys Alt = ShortcutModifierKeys.Alt;
    private const ShortcutModifierKeys CtrlShift = ShortcutModifierKeys.Control | ShortcutModifierKeys.Shift;
    private const ShortcutModifierKeys CtrlAlt = ShortcutModifierKeys.Control | ShortcutModifierKeys.Alt;
    private const ShortcutModifierKeys AltShift = ShortcutModifierKeys.Alt | ShortcutModifierKeys.Shift;
    private const ShortcutModifierKeys CtrlAltShift = ShortcutModifierKeys.Control | ShortcutModifierKeys.Alt | ShortcutModifierKeys.Shift;

    private const ShortcutInteractionContext Application = ShortcutInteractionContext.Application;
    private const ShortcutInteractionContext Workbook = ShortcutInteractionContext.Workbook;
    private const ShortcutInteractionContext WorkbookWindow = ShortcutInteractionContext.WorkbookWindow;
    private const ShortcutInteractionContext Worksheet = ShortcutInteractionContext.Worksheet;
    private const ShortcutInteractionContext WorksheetSelection = ShortcutInteractionContext.WorksheetSelection;
    private const ShortcutInteractionContext CellEditor = ShortcutInteractionContext.CellEditor;
    private const ShortcutInteractionContext FormulaBar = ShortcutInteractionContext.FormulaBar;
    private const ShortcutInteractionContext FormulaReferenceEditor = ShortcutInteractionContext.FormulaReferenceEditor;
    private const ShortcutInteractionContext HyperlinkCell = ShortcutInteractionContext.HyperlinkCell;
    private const ShortcutInteractionContext Ribbon = ShortcutInteractionContext.Ribbon;
    private const ShortcutInteractionContext SheetTabs = ShortcutInteractionContext.SheetTabs;
    private const ShortcutInteractionContext DataValidationListOrFilterHeader = ShortcutInteractionContext.DataValidationListOrFilterHeader;
    private const ShortcutInteractionContext FocusedContextTarget = ShortcutInteractionContext.FocusedContextTarget;

    private static WorksheetRangeValidationTarget RangeTarget(
        string id,
        string area,
        string owner,
        string displayTarget,
        string expectedBehavior,
        IReadOnlyList<string>? aliases = null,
        bool isNative = false,
        bool isExternal = false) =>
        new(id, area, owner, displayTarget, aliases ?? [], expectedBehavior, isNative, isExternal);
}
