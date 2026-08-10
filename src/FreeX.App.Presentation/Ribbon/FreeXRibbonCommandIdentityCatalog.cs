using System.Collections.Generic;
using FreeX.App.Presentation.DrawingUI;

namespace FreeX.App.Presentation.Ribbon;

/// <summary>
/// Stable bridge between the Avalonia shell's historical dotted command ids (e.g. <c>home.bold</c>,
/// <c>insert.column</c>, <c>chartDesign.changeType</c>) and the canonical, descriptive command ids the shared
/// ribbon definition (<see cref="FreeX.Ribbon.Definitions.FreeXRibbon"/>) emits (e.g. <c>Bold</c>,
/// <c>Column Chart</c>, <c>Change Chart Type#ChangeChartTypeBtn_Click</c>).
///
/// The Avalonia shell registers its command handlers under the dotted ids it has always used; this adapter
/// translates each registration to the canonical id the rendered (shared) definition queries the registry
/// with, so a single declarative definition drives both the WPF and the Avalonia app. Canonical ids are the
/// FULL <c>RibbonCommandId.Value</c> string, including any <c>#HandlerName</c> suffix — the registry keys on
/// the whole string, so the suffix must be preserved (matching how the WPF host resolves handlers).
/// </summary>
public static partial class FreeXRibbonCommandIdentityCatalog
{
    // Avalonia dotted id -> canonical (shared-definition) id. Derived by matching the two definitions'
    // control/menu Labels; the unit test cross-checks that every value here is a real id in FreeXRibbon.Build().
    private static readonly IReadOnlyDictionary<string, string> AvaloniaToCanonicalMap = BuildMap();

    private static readonly IReadOnlyDictionary<string, string> CanonicalToAvaloniaMap = BuildReverseMap();

    /// <summary>
    /// The canonical (shared-definition) id for an Avalonia dotted id, or the input unchanged when the id is
    /// not a known Avalonia handler id (so callers can pass already-canonical ids through harmlessly).
    /// </summary>
    public static string ToCanonical(string avaloniaId)
        => AvaloniaToCanonicalMap.TryGetValue(avaloniaId, out var canonical) ? canonical : avaloniaId;

    /// <summary>
    /// The Avalonia dotted id for a canonical (shared-definition) id, or the input unchanged when no Avalonia
    /// handler maps to it.
    /// </summary>
    public static string ToAvalonia(string canonicalId)
        => CanonicalToAvaloniaMap.TryGetValue(canonicalId, out var avalonia) ? avalonia : canonicalId;

    /// <summary>True when <paramref name="avaloniaId"/> is a known Avalonia handler id with a canonical mapping.</summary>
    public static bool IsKnownAvaloniaId(string avaloniaId) => AvaloniaToCanonicalMap.ContainsKey(avaloniaId);

    /// <summary>All Avalonia dotted handler ids the adapter maps to a canonical control/menu id.</summary>
    public static IEnumerable<string> AvaloniaIds => AvaloniaToCanonicalMap.Keys;

    public static string ShapeCommandId(DrawingShapeKind kind) => $"insert.shape.{kind}";

    /// <summary>
    /// Avalonia handler ids that have NO counterpart control in the shared single-source definition — features
    /// the historical Avalonia ribbon exposed that the canonical FreeX ribbon does not (e.g. a standalone Quick
    /// Analysis / Equation / Object / Thesaurus / Translate button, or the Insert-tab slicer/timeline whose
    /// only canonical home is the PivotTable Analyze contextual tab). They pass through <see cref="ToCanonical"/>
    /// unchanged, so the shell can still register their handlers without hijacking an unrelated canonical
    /// control; the dead registration is harmless because the shared definition never renders that id. Kept as
    /// an explicit, documented set so the coverage test can distinguish "honestly orphaned" from "mis-mapped".
    /// </summary>
    public static readonly IReadOnlySet<string> OrphanAvaloniaIds = new HashSet<string>(StringComparer.Ordinal)
    {
        "data.quickAnalysis",   // no canonical Quick Analysis control
        "insert.equation",      // no canonical Equation control
        "insert.object",        // no canonical Object control
        "insert.slicer",        // canonical Insert Slicer lives only on the PivotTable Analyze contextual tab
        "insert.timeline",      // canonical Insert Timeline lives only on the PivotTable Analyze contextual tab
        "insert.pivotChart",    // canonical PivotChart lives only on the PivotTable Analyze contextual tab
        "review.thesaurus",     // no canonical Thesaurus control
        "review.translate",     // no canonical Translate control
    };

    private static IReadOnlyDictionary<string, string> BuildReverseMap()
    {
        // First mapping wins so the canonical id resolves back to a single, primary Avalonia id even when two
        // Avalonia ids alias the same canonical command (e.g. home.merge / home.mergeCenter).
        var reverse = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (avalonia, canonical) in AvaloniaToCanonicalMap)
            reverse.TryAdd(canonical, avalonia);
        return reverse;
    }

    private static IReadOnlyDictionary<string, string> BuildMap() => new Dictionary<string, string>(StringComparer.Ordinal)
    {
        // ── Home ▸ Clipboard ────────────────────────────────────────────────────────────────────────────
        ["home.paste"] = "Paste",
        ["home.pasteValues"] = "Paste Values",
        ["home.pasteFormat"] = "Paste Formatting",
        ["home.pasteSpecial"] = "Paste Special",
        ["home.cut"] = "Cut",
        ["home.copy"] = "Copy",
        ["home.formatPainter"] = "Format Painter",

        // ── Home ▸ Font ─────────────────────────────────────────────────────────────────────────────────
        ["home.fontName"] = "Font",
        ["home.fontSize"] = "Font Size",
        ["home.increaseFont"] = "Increase Font Size",
        ["home.decreaseFont"] = "Decrease Font Size",
        ["home.bold"] = "Bold",
        ["home.italic"] = "Italic",
        ["home.underline"] = "Underline",
        ["home.strikethrough"] = "Strikethrough",
        ["home.borders"] = "Borders",
        ["home.bordersAll"] = "All Borders",
        ["home.bordersOutside"] = "Outside Borders",
        ["home.bordersNone"] = "No Border",
        ["home.fillColor"] = "Fill Color",
        ["home.fontColor"] = "Font Color",
        // Fill/Font color swatch menu items have no per-colour ids in the shared definition; they fall through
        // to the parent command, which the adapter leaves unmapped (resolves to the parent's NoOp/handler).

        // ── Home ▸ Alignment ────────────────────────────────────────────────────────────────────────────
        ["home.alignTop"] = "Top Align",
        ["home.alignMiddle"] = "Middle Align",
        ["home.alignBottom"] = "Bottom Align",
        ["home.orientation"] = "Orientation",
        ["home.wrapText"] = "Wrap Text",
        ["home.alignLeft"] = "Align Left",
        ["home.alignCenter"] = "Center",
        ["home.alignRight"] = "Align Right",
        ["home.decreaseIndent"] = "Decrease Indent",
        ["home.increaseIndent"] = "Increase Indent",
        ["home.merge"] = "Merge & Center",
        ["home.mergeCenter"] = "Merge & Center",
        ["home.mergeAcross"] = "Merge Across",
        ["home.mergeCells"] = "Merge Cells",
        ["home.unmerge"] = "Unmerge Cells",

        // ── Home ▸ Number ───────────────────────────────────────────────────────────────────────────────
        ["home.numberFormat"] = "Number Format",
        ["home.accounting"] = "Accounting Number Format",
        ["home.percent"] = "Percent Style",
        ["home.comma"] = "Comma Style",
        ["home.currency"] = "Accounting Number Format",
        ["home.increaseDecimal"] = "Increase Decimal Places",
        ["home.decreaseDecimal"] = "Decrease Decimal Places",

        // ── Home ▸ Styles ───────────────────────────────────────────────────────────────────────────────
        ["home.conditional"] = "Conditional Formatting",
        ["home.formatAsTable"] = "Format as Table",
        ["home.cellStyles"] = "Cell Styles",

        // ── Home ▸ Cells ────────────────────────────────────────────────────────────────────────────────
        ["home.insertCells"] = "Insert",
        ["home.deleteCells"] = "Delete",
        ["home.formatCells"] = "Format",

        // ── Home ▸ Editing ──────────────────────────────────────────────────────────────────────────────
        ["home.autoSum"] = "AutoSum",
        ["home.fillDown"] = "Fill",
        ["home.clear"] = "Clear",
        ["home.findSelect"] = "Find & Select",

        // ── Insert ▸ Tables / Charts / etc. (canonical Insert ids are descriptive labels) ────────────────
        ["insert.pivotTable"] = "PivotTable",
        ["insert.table"] = "Table",
        ["insert.column"] = "Column Chart",
        ["insert.line"] = "Line Chart",
        ["insert.pie"] = "Pie Chart",
        ["insert.scatter"] = "Scatter Chart",
        ["insert.sparklineLine"] = "Line Sparkline",
        ["insert.sparklineColumn"] = "Column Sparkline",
        ["insert.sparklineWinLoss"] = "Win/Loss Sparkline",
        ["insert.hyperlink"] = "Insert Link",
        ["insert.comment"] = "Comment",
        ["insert.textBox"] = "Text Box",
        ["insert.headerFooter"] = "Header & Footer",
        ["insert.symbol"] = "Symbol",
        // Pictures/Shapes moved to the Draw tab in the shared definition; the Insert-tab handlers bind there.
        ["insert.picture"] = "Pictures",
        ["insert.shapes"] = "Shapes",
        // insert.slicer / insert.timeline / insert.pivotChart / insert.equation / insert.object have NO
        // canonical Insert-tab control — see OrphanAvaloniaIds. They pass through ToCanonical unchanged.

        // ── Page Layout ─────────────────────────────────────────────────────────────────────────────────
        ["pageLayout.themes"] = "Themes",
        ["pageLayout.themeColors"] = "Theme Colors",
        ["pageLayout.themeFonts"] = "Theme Fonts",
        ["pageLayout.themeEffects"] = "Theme Effects",
        ["pageLayout.margins"] = "Margins",
        ["pageLayout.orientation"] = "Page Orientation",
        ["pageLayout.size"] = "Paper Size",
        ["pageLayout.printArea"] = "Print Area",
        ["pageLayout.breaks"] = "Breaks",
        ["pageLayout.background"] = "Background",
        ["pageLayout.printTitles"] = "Print Titles",
        ["pageLayout.width"] = "Scale Width",
        ["pageLayout.height"] = "Scale Height",
        ["pageLayout.scale"] = "Scale Percent",
        ["pageLayout.gridlines"] = "View Gridlines",
        ["pageLayout.headings"] = "View Headings",

        // ── Formulas ────────────────────────────────────────────────────────────────────────────────────
        ["formulas.insertFunction"] = "More Functions#FormulaMoreBtn_Click",
        ["formulas.autoSum"] = "AutoSum#FormulasAutoSumPickerBtn_Click",
        ["formulas.financial"] = "Financial",
        ["formulas.logical"] = "Logical Functions",
        ["formulas.text"] = "Text Functions",
        ["formulas.dateTime"] = "Date & Time",
        ["formulas.lookupReference"] = "Lookup & Reference",
        ["formulas.mathTrig"] = "Math & Trig",
        ["formulas.moreFunctions"] = "More Functions#FormulaMoreBtn_Click",
        ["formulas.recentlyUsed"] = "Recently Used",
        ["formulas.nameManager"] = "Name Manager",
        ["formulas.defineName"] = "Define Name",
        ["formulas.createFromSelection"] = "Create from Selection",
        ["formulas.tracePrecedents"] = "Trace Precedents",
        ["formulas.traceDependents"] = "Trace Dependents",
        ["formulas.removeArrows"] = "Remove Arrows#RemoveArrowsBtn_Click",
        ["formulas.showFormulas"] = "Show Formulas",
        ["formulas.errorChecking"] = "Error Checking",
        ["formulas.evaluateFormula"] = "Evaluate Formula",
        ["formulas.calcOptions"] = "Calculation Options",
        ["formulas.calcNow"] = "Calculate Now",

        // ── Data ────────────────────────────────────────────────────────────────────────────────────────
        ["data.getData"] = "Get Data",
        ["data.refresh"] = "Refresh All",
        ["data.sortAsc"] = "Sort A to Z#SortAscButton_Click",
        ["data.sortDesc"] = "Sort Z to A#SortDescButton_Click",
        ["data.filter"] = "Filter#FilterButton_Click",
        ["data.reapply"] = "Reapply",
        ["data.advancedFilter"] = "Advanced",
        ["data.flashFill"] = "Flash Fill",
        ["data.removeDuplicates"] = "Remove Duplicates#RemoveDuplicatesBtn_Click",
        ["data.validation"] = "Data Validation#ValidationButton_Click",
        ["data.validationDialog"] = "Data Validation#ValidationButton_Click",
        ["data.circleInvalid"] = "Circle Invalid Data",
        ["data.clearCircles"] = "Clear Validation Circles",
        ["data.textToColumns"] = "Text to Columns",
        ["data.consolidate"] = "Consolidate",
        // data.quickAnalysis has no canonical control — see OrphanAvaloniaIds (passes through unchanged).
        ["data.whatIf"] = "What-If Analysis",
        ["data.forecastSheet"] = "Forecast Sheet",
        ["data.group"] = "Group#GroupRowsBtn_Click",
        ["data.ungroup"] = "Ungroup#UngroupRowsBtn_Click",
        ["data.subtotal"] = "Subtotal",

        // ── Review ──────────────────────────────────────────────────────────────────────────────────────
        ["review.spelling"] = "Spelling",
        ["review.checkAccessibility"] = "Check Accessibility",
        ["review.newComment"] = "New Comment",
        ["review.deleteComment"] = "Delete Comment",
        ["review.newNote"] = "New Note",
        ["review.showNotes"] = "Show Notes",
        ["review.convertNotesToComments"] = "Convert to Comments",
        ["review.protectSheet"] = "Protect Sheet#ProtectSheetBtn_Click",
        ["review.protectWorkbook"] = "Protect Workbook",
        // review.thesaurus / review.translate have no canonical control — see OrphanAvaloniaIds.

        // ── View ────────────────────────────────────────────────────────────────────────────────────────
        ["view.normal"] = "Normal#NormalViewBtn_Click",
        ["view.pageBreakPreview"] = "Page Break Preview",
        ["view.pageLayoutView"] = "Page Layout",
        ["view.gridlines"] = "Gridlines",
        ["view.headings"] = "Headings",
        ["view.formulaBar"] = "Formula Bar",
        ["view.zoom"] = "Zoom",
        ["view.zoom100"] = "100%#Zoom100Btn_Click",
        ["view.zoomToSelection"] = "Zoom to Selection",
        ["view.newWindow"] = "New Window",
        ["view.arrangeAll"] = "Arrange All",
        ["view.freezePanes"] = "Freeze Panes#FreezePanesPickerBtn_Click",
        ["view.split"] = "Split",
        ["view.hide"] = "Hide",
        ["view.unhide"] = "Unhide",

        // ── Help ────────────────────────────────────────────────────────────────────────────────────────
        ["help.about"] = "About FreeX#AboutBtn_Click",
        ["help.helpOnline"] = "Help Online#HelpOnlineBtn_Click",
        ["help.feedback"] = "Feedback#FeedbackBtn_Click",
        ["help.checkUpdates"] = "Check for Updates#CheckForUpdatesBtn_Click",
        ["help.copyDiagnostics"] = "Copy Diagnostics#CopyDiagnosticsBtn_Click",
        ["help.legalNotices"] = "Legal Notices#LegalNoticesBtn_Click",

        // ── Chart Design (contextual: chart.selected) ────────────────────────────────────────────────────
        ["chartDesign.titles"] = "Chart Titles",
        ["chartDesign.dataLabels"] = "Data Labels",
        ["chartDesign.dataLabelPosition"] = "Data Label Position",
        ["chartDesign.trendline"] = "Trendline",
        ["chartDesign.errorBars"] = "Error Bars",
        ["chartDesign.secondaryAxis"] = "Secondary Axis",
        ["chartDesign.secondaryAxisSeries"] = "Secondary Axis Series",
        ["chartDesign.chartStyles"] = "Chart Styles",
        ["chartDesign.selectData"] = "Select Data Source",
        ["chartDesign.changeType"] = "Change Chart Type#ChangeChartTypeBtn_Click",
        ["chartDesign.comboChart"] = "Combo Chart",
        ["chartDesign.comboChartSeries"] = "Combo Chart Series",
        ["chartDesign.moveChart"] = "Move Chart",

        // ── Chart Format (contextual: chart.selected) ────────────────────────────────────────────────────
        ["chartFormat.formatChartArea"] = "Format Chart Area",
        // Current Selection ▸ Format: type-specific format dialogs (bar/column, pie/doughnut, bubble, stock).
        ["chartFormat.formatBarColumn"] = "Format Bar/Column",
        ["chartFormat.formatPieDoughnut"] = "Format Pie/Doughnut",
        ["chartFormat.formatBubble"] = "Format Bubble Chart",
        ["chartFormat.formatStock"] = "Format Stock Chart",
        ["chartFormat.chartAreaFill"] = "Chart Area Fill",
        ["chartFormat.plotAreaFill"] = "Plot Area Fill",
        ["chartFormat.plotAreaBorder"] = "Plot Area Border",
        ["chartFormat.seriesColor"] = "Series Color",
        ["chartFormat.seriesWidth"] = "Series Width",
        // Shape Styles ▸ Series Dash / Series Marker (full series dialog) / Marker Size quick buttons.
        ["chartFormat.seriesDash"] = "Series Dash",
        ["chartFormat.seriesMarker"] = "Series Marker",
        ["chartFormat.markerSize"] = "Marker Size",
        ["chartFormat.legendText"] = "Legend Text",
        // Text group quick buttons: title/axis-title colors & sizes, legend font size, data-label text/fill/border.
        ["chartFormat.chartTitleColor"] = "Chart Title Color",
        ["chartFormat.chartTitleSize"] = "Chart Title Size",
        ["chartFormat.axisTitleColor"] = "Axis Title Color",
        ["chartFormat.axisTitleSize"] = "Axis Title Size",
        ["chartFormat.legendFontSize"] = "Legend Font Size",
        ["chartFormat.dataLabelText"] = "Data Label Text",
        ["chartFormat.dataLabelFill"] = "Data Label Fill",
        ["chartFormat.dataLabelBorder"] = "Data Label Border",
        ["chartFormat.xAxisBounds"] = "X Axis Bounds",
        ["chartFormat.yAxisBounds"] = "Y Axis Bounds",
        ["chartFormat.xGridlines"] = "X Axis Gridlines",
        ["chartFormat.yGridlines"] = "Y Axis Gridlines",
        ["chartFormat.xLabels"] = "X Axis Labels",
        ["chartFormat.yLabels"] = "Y Axis Labels",

        // ── Picture Format (contextual: picture.selected) ───────────────────────────────────────────────
        ["pictureFormat.formatPicture"] = "Format Picture",
        ["pictureFormat.crop"] = "Crop Picture",
        ["pictureFormat.bringForward"] = "Bring Forward",
        ["pictureFormat.sendBackward"] = "Send Backward",
        ["pictureFormat.selectionPane"] = "Selection Pane#SelectionPaneBtn_Click",
        ["pictureFormat.rotate"] = "Rotate Object",
        ["pictureFormat.size"] = "Object Size",
        ["pictureFormat.altText"] = "Alt Text",

        // ── Shape Format (contextual: shape.selected) ───────────────────────────────────────────────────
        ["shapeFormat.shapeFill"] = "Shape Fill",
        ["shapeFormat.shapeOutline"] = "Object Outline",
        ["shapeFormat.shapeGradient"] = "Shape Gradient",
        ["shapeFormat.shapeEffects"] = "Shape Effects",
        ["shapeFormat.shapeEffectNone"] = "No Effect",
        ["shapeFormat.shapeEffectShadow"] = "Shadow",
        ["shapeFormat.bringForward"] = "Bring Forward",
        ["shapeFormat.sendBackward"] = "Send Backward",
        ["shapeFormat.selectionPane"] = "Selection Pane#SelectionPaneBtn_Click",
        ["shapeFormat.rotate"] = "Rotate Object",
        ["shapeFormat.size"] = "Object Size",
        ["shapeFormat.altText"] = "Alt Text",

        // ── Table Design (contextual: table.active) ─────────────────────────────────────────────────────
        ["tableDesign.tableName"] = "Table Name",
        ["tableDesign.resize"] = "Resize Table",
        ["tableDesign.removeDuplicates"] = "Remove Duplicates#TableDesignRemoveDuplicatesBtn_Click",
        ["tableDesign.convertToRange"] = "Convert to Range",
        ["tableDesign.totalRow"] = "Total Row",
        ["tableDesign.firstColumn"] = "First Column",
        ["tableDesign.lastColumn"] = "Last Column",
        ["tableDesign.bandedRows"] = "Banded Rows#TableDesignBandedRowsBtn_Click",
        ["tableDesign.bandedColumns"] = "Banded Columns#TableDesignBandedColumnsBtn_Click",
        ["tableDesign.filterButton"] = "Filter Button",
        ["tableDesign.tableStyles"] = "Table Styles",
        ["tableDesign.summarizeWithPivot"] = "Summarize with PivotTable",

        // ── PivotTable Analyze (contextual: pivot.active) ───────────────────────────────────────────────
        ["pivotAnalyze.name"] = "PivotTable Name",
        ["pivotAnalyze.options"] = "PivotTable Options",
        ["pivotAnalyze.fieldSettings"] = "Field Settings",
        ["pivotAnalyze.groupField"] = "Group Field",
        ["pivotAnalyze.ungroup"] = "Ungroup#PivotUngroupFieldBtn_Click",
        ["pivotAnalyze.insertSlicer"] = "Insert Slicer",
        ["pivotAnalyze.insertTimeline"] = "Insert Timeline",
        ["pivotAnalyze.refresh"] = "Refresh",
        ["pivotAnalyze.changeDataSource"] = "Change Data Source",
        ["pivotAnalyze.calculatedField"] = "Calculated Field",
        ["pivotAnalyze.calculatedItem"] = "Calculated Item",
        ["pivotAnalyze.fieldList"] = "Field List",
        ["pivotAnalyze.fieldHeaders"] = "Field Headers",
        ["pivotAnalyze.showDetails"] = "Show Details",
        ["pivotAnalyze.clear"] = "Clear#PivotTableClearBtn_Click",
        ["pivotAnalyze.select"] = "Select",
        ["pivotAnalyze.move"] = "Move PivotTable",
        ["pivotAnalyze.plusMinusButtons"] = "+/- Buttons",
        ["pivotAnalyze.pivotChart"] = "PivotChart",
        ["pivotAnalyze.changeChartType"] = "Change Chart Type#PivotChartChangeTypeBtn_Click",
        ["pivotAnalyze.pivotChartOptions"] = "PivotChart Options",

        // ── PivotTable Design (contextual: pivot.active) ────────────────────────────────────────────────
        ["pivotDesign.grandTotals"] = "Grand Totals",
        ["pivotDesign.subtotals"] = "Subtotals",
        ["pivotDesign.reportLayout"] = "Report Layout",
        ["pivotDesign.blankRows"] = "Blank Rows",
        ["pivotDesign.bandedRows"] = "Banded Rows#PivotBandedRowsBtn_Click",
        ["pivotDesign.bandedColumns"] = "Banded Columns#PivotBandedColumnsBtn_Click",
        ["pivotDesign.rowHeaders"] = "Row Headers",
        ["pivotDesign.columnHeaders"] = "Column Headers",
        ["pivotDesign.pivotStyles"] = "PivotTable Styles",
    };
}
