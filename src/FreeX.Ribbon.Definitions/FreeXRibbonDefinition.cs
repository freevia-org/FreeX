using Free.Shared.Ribbon;
using Ico = Free.Shared.Ribbon.RibbonCommandIconKind;

namespace FreeX.Ribbon.Definitions;

/// <summary>
/// The complete FreeX ribbon authored declaratively, generated from the catalog structure of
/// the original MainWindow.xaml ribbon (all main tabs + contextual tabs). Stable command ids
/// bind directly to the host's typed command registry.
/// </summary>
public static class FreeXRibbonDefinition
{
    public static RibbonDefinition Build() => new RibbonDefinitionBuilder()
        .Tab("InsertTab", "Insert", "N", tab => tab
            .Group("InsertTablesGroup", "Tables", null, priority: 200,
                g => g
                .Large("PivotTable", "PivotTable", Ico.PivotTable, "PT")
                .Large("Table", "Table", Ico.Table, "TB"))
            .Group("InsertChartsGroup", "Charts", null, priority: 60,
                g => g
                .Large("Recommended Charts", "Recommended Charts", Ico.ChartColumn, "RC")
                .Medium("Column Chart", "Column", Ico.ChartColumn, "CC")
                .Medium("Stacked Column Chart", "Stacked Column", Ico.ChartColumn, "SC")
                .Medium("100% Stacked Column Chart", "100% Stacked Column", Ico.ChartColumn, "PC")
                .Medium("Bar Chart", "Bar", Ico.ChartColumn, "BC")
                .Medium("Stacked Bar Chart", "Stacked Bar", Ico.ChartColumn, "SB")
                .Medium("100% Stacked Bar Chart", "100% Stacked Bar", Ico.ChartColumn, "PB")
                .Medium("Line Chart", "Line", Ico.ChartLine, "LC")
                .Medium("Area Chart", "Area", Ico.ChartArea, "AC")
                .Medium("Stock Chart", "Stock", Ico.ChartLine, "ST")
                .Medium("Pie Chart", "Pie", Ico.ChartPie, "PY")
                .Medium("Doughnut Chart", "Doughnut", Ico.ChartPie, "DO")
                .Medium("Scatter Chart", "Scatter", Ico.ChartScatter, "SX")
                .Medium("Bubble Chart", "Bubble", Ico.ChartScatter, "BU")
                .Medium("Radar Chart", "Radar", Ico.ChartArea, "RD")
                .Medium("Select Data Source", "Select Data Source", Ico.Search, "DS"))
            .Group("InsertSparklinesGroup", "Sparklines", null, priority: 120,
                g => g
                .Medium("Line Sparkline", "Line Sparkline", Ico.Sparkline, "SL")
                .Medium("Column Sparkline", "Column Sparkline", Ico.Sparkline, "SK")
                .Medium("Win/Loss Sparkline", "Win/Loss Sparkline", Ico.Sparkline, "SW"))
            .Group("InsertFiltersGroup", "Filters", null, priority: 110,
                g => g
                .Large("Insert Timeline", "Insert Timeline", Ico.Date, "IT"))
            .Group("InsertLinksGroup", "Links", null, priority: 100,
                g => g
                .Large("Insert Link", "Insert Link", Ico.Link, "K"))
            .Group("InsertCommentsGroup", "Comments", null, priority: 90,
                g => g
                .Large("Comment", "Comment", Ico.Comment, "C2"))
            .Group("InsertTextGroup", "Text", null, priority: 80,
                g => g
                .Large("Text Box", "Text Box", Ico.TextBox, "TX")
                .Large("Header & Footer", "Header & Footer", Ico.HeaderFooter, "HF"))
            .Group("InsertSymbolsGroup", "Symbols", null, priority: 70,
                g => g
                .Large("Symbol", "Symbol", Ico.Symbol, "SY"))
        )
        .Tab("DrawTab", "Draw", "J", tab => tab
            .Group("DrawIllustrationsGroup", "Illustrations", null, priority: 180,
                g => g
                .Large("Pictures", "Pictures", Ico.Picture, "IP")
                .Large("Shapes", "Shapes", Ico.RibbonShape, "SH"))
            .Group("DrawArrangeGroup", "Arrange", null, priority: 70,
                g => g
                .Large("Bring Forward", "Bring Forward", Ico.BringForward, "BF")
                .Large("Send Backward", "Send Backward", Ico.SendBackward, "SB")
                .Large(FreeXRibbonCommandIds.DrawingSelectionPane, "Selection Pane", Ico.List, "SP")
                .Large("Rotate Object", "Rotate Object", Ico.Rotate, "RO")
                .Large("Object Size", "Object Size", Ico.Size, "SZ"))
            .Group("DrawFormatGroup", "Format", null, priority: 160,
                g => g
                .Medium("Shape Fill", "Shape Fill", Ico.RibbonShape, "OF")
                .Medium("Object Outline", "Object Outline", Ico.Border, "OO")
                .Medium("Crop Picture", "Crop Picture", Ico.Picture, "C", menu: m => m.Item("Crop", "Crop...", "C").Item("Reset Crop", "Reset Crop", "R"))
                .Medium("Shape Gradient", "Shape Gradient", Ico.RibbonShape, "G")
                .Medium("Shape Effects", "Shape Effects", Ico.RibbonShape, "FX", menu: m => m.Item("No Effect", "No Effect", "N").Separator().Item("Shadow", "Shadow", "S").Item("Inner Shadow", "Inner Shadow", "I").Item("Reflection", "Reflection", "R").Item("Glow", "Glow", "G").Item("Soft Edges", "Soft Edges", "E").Item("Bevel", "Bevel", "B").Item("3-D Rotation", "3-D Rotation", "D")))
        )
        .Tab("PageLayoutTab", "Page Layout", "P", tab => tab
            .Group("PageLayoutThemesGroup", "Themes", null, priority: 110,
                g => g
                .Large("Themes", "Themes", Ico.Theme, "TH", menu: m => m.Item(FreeXRibbonCommandIds.PageLayoutThemeOffice, "Office", "O").Item(FreeXRibbonCommandIds.PageLayoutThemeColorful, "FreeX Colorful", "C").Item(FreeXRibbonCommandIds.PageLayoutThemeGrayscale, "Grayscale", "G").Item("Customize", "Customize...", "U"))
                .Large("Theme Colors", "Theme Colors", Ico.Theme, "TC", menu: m => m.Item(FreeXRibbonCommandIds.PageLayoutThemeColorsOffice, "Office", "O").Item(FreeXRibbonCommandIds.PageLayoutThemeColorsColorful, "FreeX Colorful", "C").Item(FreeXRibbonCommandIds.PageLayoutThemeColorsGrayscale, "Grayscale", "G").Item("Customize Colors", "Customize Colors...", "U"))
                .Large("Theme Fonts", "Theme Fonts", Ico.Font, "TF", menu: m => m.Item(FreeXRibbonCommandIds.PageLayoutThemeFontsOffice, "Office", "O").Item("Arial", "Arial", "A").Item("Times New Roman", "Times New Roman", "T").Item("Customize Fonts", "Customize Fonts...", "U"))
                .Large("Theme Effects", "Theme Effects", Ico.Effects, "TE", menu: m => m.Item(FreeXRibbonCommandIds.PageLayoutThemeEffectsOffice, "Office", "O").Item("Subtle", "Subtle", "S").Item("Refined", "Refined", "R").Item("Customize Effects", "Customize Effects...", "U")))
            .Group("PageLayoutPageSetupGroup", "Page Setup", null, priority: 200,
                g => g
                .Large("Margins", "Margins", Ico.Margins, "M", menu: m => m.Item(FreeXRibbonCommandIds.PageLayoutMarginsNormal, "Normal", "N").Item("Wide", "Wide", "W").Item("Narrow", "Narrow", "A").Item("Custom Margins", "Custom Margins...", "C"))
                .Large("Page Orientation", "Page Orientation", Ico.Orientation, "OR", menu: m => m.Item("Portrait", "Portrait", "P").Item("Landscape", "Landscape", "L"))
                .Large("Paper Size", "Paper Size", Ico.Page, "SZ", menu: m => m.Item("Letter", "Letter", "L").Item("Legal", "Legal", "G").Item("Executive", "Executive", "E").Item("Statement", "Statement", "S").Item("Tabloid", "Tabloid", "T").Item("A4", "A4", "A").Item("A3", "A3", "3").Item("A5", "A5", "5").Item("B4 (JIS)", "B4 (JIS)", "B4").Item("B5 (JIS)", "B5 (JIS)", "B5"))
                .Large("Print Area", "Print Area", Ico.Print, "PA", menu: m => m.Item("Set Print Area", "Set Print Area", "S").Item("Clear Print Area", "Clear Print Area", "C"))
                .Large("Breaks", "Breaks", Ico.PageBreak, "BK", menu: m => m.Item("Insert Page Break", "Insert Page Break", "I").Item("Remove Page Break", "Remove Page Break", "R").Item("Reset All Page Breaks", "Reset All Page Breaks", "A"))
                .Large("Background", "Background", Ico.Picture, "BG", menu: m => m.Item("Choose Background", "Choose Background...", "C").Item("Delete Background", "Delete Background", "D"))
                .Large("Print Titles", "Print Titles", Ico.Print, "PT")
                .Medium("Page Setup", "Page Setup", Ico.Page, "PS")
                .Medium("Page Setup dialog", "Page Setup dialog", Ico.Page, "PD"))
            .Group("PageLayoutScaleToFitGroup", "Scale To Fit", null, priority: 90,
                g => g
                .ComboBox("Scale Width", "Scale Width", c => c with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Scale), Width = 96, Items = new[] { "Automatic", "1 page", "2 pages" }, KeyTip = "SW" })
                .ComboBox("Scale Height", "Scale Height", c => c with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Scale), Width = 96, Items = new[] { "Automatic", "1 page", "2 pages" }, KeyTip = "SH" })
                .ComboBox("Scale Percent", "Scale Percent", c => c with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Percent), Width = 70, Items = new[] { "100%", "90%", "80%", "75%", "50%" }, KeyTip = "SC" })
                .Large("Scale to Fit", "Scale to Fit", Ico.Scale, "SF"))
            .Group("PageLayoutSheetOptionsGroup", "Sheet Options", null, priority: 80,
                g => g
                .CheckBox("View Gridlines", "View Gridlines", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Grid), KeyTip = "VG" })
                .CheckBox("Print Gridlines", "Print Gridlines", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Grid), KeyTip = "PG" })
                .CheckBox("View Headings", "View Headings", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Grid), KeyTip = "VH" })
                .CheckBox("Print Headings", "Print Headings", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Grid), KeyTip = "PH" }))
        )
        .Tab("FormulasTab", "Formulas", "M", tab => tab
            .Group("FormulasFunctionLibraryGroup", "Function Library", null, priority: 200,
                g => g
                .Large(FreeXRibbonCommandIds.FormulasAutoSum, "AutoSum", Ico.Sum, "U", menu: m => m.Item("Sum", "Sum", "S").Item("Average", "Average", "A").Item("Count Numbers", "Count Numbers", "C").Item("Count All", "Count All", "T").Item("Max", "Max", "X").Item("Min", "Min", "M").Item(FreeXRibbonCommandIds.FormulasAutoSumMoreFunctions, "More Functions...", "F"))
                .Medium("Recently Used", "Recently Used", Ico.Function, "RU")
                .Medium("Financial", "Financial", Ico.Financial, "Y")
                .Medium("Logical Functions", "Logical Functions", Ico.Logical, "L")
                .Medium("Text Functions", "Text Functions", Ico.TextFunction, "TF")
                .Medium("Date & Time", "Date & Time", Ico.Date, "DT")
                .Medium("Lookup & Reference", "Lookup & Reference", Ico.Search, "K")
                .Medium("Math & Trig", "Math & Trig", Ico.Math, "MT")
                .Medium(FreeXRibbonCommandIds.FormulasMoreFunctions, "More Functions", Ico.Function, "MF"))
            .Group("FormulasDefinedNamesGroup", "Defined Names", null, priority: 130,
                g => g
                .Large("Name Manager", "Name Manager", Ico.Label, "N")
                .Large("Define Name", "Define Name", Ico.Label, "DN")
                .Large("Use in Formula", "Use in Formula", Ico.Function, "I")
                .Large("Create from Selection", "Create from Selection", Ico.Label, "CS"))
            .Group("FormulasFormulaAuditingGroup", "Formula Auditing", null, priority: 90,
                g => g
                .Medium("Trace Precedents", "Trace Precedents", Ico.Link, "TP")
                .Medium("Trace Dependents", "Trace Dependents", Ico.Link, "TD")
                .Medium(FreeXRibbonCommandIds.FormulasRemoveArrows, "Remove Arrows", Ico.Clear, "RA", menu: m => m.Item(FreeXRibbonCommandIds.FormulasRemoveAllArrows, "Remove Arrows", "A").Item("Remove Precedent Arrows", "Remove Precedent Arrows", "P").Item("Remove Dependent Arrows", "Remove Dependent Arrows", "D"))
                .IconToggle("Show Formulas", "Show Formulas", Ico.Function, "SF")
                .Medium("Error Checking", "Error Checking", Ico.Warning, "EC", menu: m => m.Item("Error Checking", "Error Checking...", "E").Item("Error Checking Options", "Error Checking Options...", "O"))
                .Medium("Evaluate Formula", "Evaluate Formula", Ico.Function, "V")
                .Medium("Watch Window", "Watch Window", Ico.Watch, "W"))
            .Group("FormulasCalculationGroup", "Calculation", null, priority: 120,
                g => g
                .Large("Calculate Now", "Calculate Now", Ico.Refresh, "CN")
                .Large("Calculate Sheet", "Calculate Sheet", Ico.Refresh, "SC")
                .Large("Calculation Options", "Calculation Options", Ico.Refresh, "O", menu: m => m.Item("Automatic", "Automatic", "A").Item("Automatic Except Data Tables", "Automatic Except Data Tables", "E").Item("Manual", "Manual", "M")))
        )
        .Tab("DataTab", "Data", "A", tab => tab
            .Group("DataGetTransformGroup", "Get Transform", null, priority: 180,
                g => g
                .Large("Get Data", "Get Data", Ico.GetData, "D"))
            .Group("DataQueriesConnectionsGroup", "Queries Connections", null, priority: 170,
                g => g
                .Large("Refresh All", "Refresh All", Ico.Refresh, "FA"))
            .Group("DataSortFilterGroup", "Sort Filter", null, priority: 160,
                g => g
                .Medium(FreeXRibbonCommandIds.DataSortAscending, "Sort A to Z", Ico.SortAscending, "SA")
                .Medium(FreeXRibbonCommandIds.DataSortDescending, "Sort Z to A", Ico.SortDescending, "SD")
                .Medium("Sort", "Sort", Ico.Sort, "SO")
                .Large(FreeXRibbonCommandIds.DataFilter, "Filter", Ico.Filter, "T")
                .Medium(FreeXRibbonCommandIds.DataClearFilter, "Clear", Ico.Clear, "C")
                .Medium("Advanced", "Advanced", Ico.Filter, "A")
                .Medium("Reapply", "Reapply", Ico.Refresh, "R"))
            .Group("DataToolsGroup", "Tools", null, priority: 150,
                g => g
                .Large("Text to Columns", "Text to Columns", Ico.TextColumns, "E")
                .Large("Flash Fill", "Flash Fill", Ico.Flash, "FF")
                .Large(FreeXRibbonCommandIds.DataRemoveDuplicates, "Remove Duplicates", Ico.Delete, "M")
                .Large(FreeXRibbonCommandIds.DataValidation, "Data Validation", Ico.List, "V", menu: m => m.Item(FreeXRibbonCommandIds.DataValidation, "Data Validation...", "V").Item("Circle Invalid Data", "Circle Invalid Data", "I").Item("Clear Validation Circles", "Clear Validation Circles", "C"))
                .Large("Consolidate", "Consolidate", Ico.Date, "N"))
            .Group("DataForecastGroup", "Forecast", null, priority: 80,
                g => g
                .Large("What-If Analysis", "What-If Analysis", Ico.Function, "W", menu: m => m.Item("Goal Seek", "Goal Seek...", "G").Item("Scenario Manager", "Scenario Manager...", "S").Item("Data Table", "Data Table...", "D"))
                .Large("Forecast Sheet", "Forecast Sheet", Ico.ChartLine, "FS"))
            .Group("DataOutlineGroup", "Outline", null, priority: 70,
                g => g
                .SplitButton(
                    FreeXRibbonCommandIds.DataOutlineGroup,
                    "Group",
                    new RibbonMenu([new RibbonMenuItem("Group", FreeXRibbonCommandIds.DataOutlineGroupRows, "G", "G")]),
                    control => control with
                    {
                        PreferredLayout = RibbonCommandLayoutKind.Large,
                        Icon = new RibbonCommandIcon(Ico.Group),
                        KeyTip = "G",
                    })
                .SplitButton(
                    FreeXRibbonCommandIds.DataOutlineUngroup,
                    "Ungroup",
                    new RibbonMenu([
                        new RibbonMenuItem("Ungroup", FreeXRibbonCommandIds.DataOutlineUngroupRows, "U", "U"),
                        RibbonMenuItem.Separator(),
                        new RibbonMenuItem("Clear Outline", "Clear Outline", "C", "C"),
                    ]),
                    control => control with
                    {
                        PreferredLayout = RibbonCommandLayoutKind.Large,
                        Icon = new RibbonCommandIcon(Ico.Group),
                        KeyTip = "U",
                    })
                .Large("Subtotal", "Subtotal", Ico.Sum, "B")
                .Medium("Hide Detail", "Hide Detail", Ico.List, "H")
                .Medium("Show Detail", "Show Detail", Ico.List, "J"))
        )
        .Tab("ReviewTab", "Review", "R", tab => tab
            .Group("ReviewProofingGroup", "Proofing", null, priority: 200,
                g => g
                .Large("Spelling", "Spelling", Ico.Spelling, "SP")
                .Large("Workbook Statistics", "Workbook Statistics", Ico.Info, "W"))
            .Group("ReviewAccessibilityGroup", "Accessibility", null, priority: 120,
                g => g
                .Large("Check Accessibility", "Check Accessibility", Ico.Accessibility, "CA")
                .Medium("Alt Text", "Alt Text", Ico.Label, "T"))
            .Group("ReviewCommentsGroup", "Comments", null, priority: 90,
                g => g
                .Large("New Comment", "New Comment", Ico.Comment, "CM")
                .Medium("Delete Comment", "Delete Comment", Ico.Comment, "XC")
                .Medium("Previous Comment", "Previous Comment", Ico.Comment, "PC")
                .Medium("Next Comment", "Next Comment", Ico.Comment, "JC")
                .Large("Show Comments", "Show Comments", Ico.Comment, "SC"))
            .Group("ReviewNotesGroup", "Notes", null, priority: 80,
                g => g
                .Medium("New Note", "New Note", Ico.Comment, "O")
                .Medium("Edit Note", "Edit Note", Ico.Comment, "E")
                .Medium("Delete Note", "Delete Note", Ico.Comment, "D")
                .Medium("Previous Note", "Previous Note", Ico.Comment, "PN")
                .Medium("Next Note", "Next Note", Ico.Comment, "N")
                .Medium("Show Notes", "Show Notes", Ico.Comment, "H")
                .Medium("Convert to Comments", "Convert to Comments", Ico.Comment, "CV"))
            .Group("ReviewProtectGroup", "Protect", null, priority: 70,
                g => g
                .Large(FreeXRibbonCommandIds.ReviewProtectSheet, "Protect Sheet", Ico.Protect, "PS")
                .Large("Protect Workbook", "Protect Workbook", Ico.Protect, "PW")
                .Medium("Allow Users to Edit Ranges", "Allow Users to Edit Ranges", Ico.Protect, "AR")
                .Medium("Share", "Share", Ico.Share, "SH"))
        )
        .Tab("ViewTab", "View", "W", tab => tab
            .Group("ViewWorkbookViewsGroup", "Workbook Views", null, priority: 200,
                g => g
                .Large(FreeXRibbonCommandIds.ViewNormal, "Normal", Ico.View, "L")
                .Large("Page Break Preview", "Page Break Preview", Ico.PageBreak, "I")
                .Large("Page Layout", "Page Layout", Ico.Page, "P")
                .Large("Custom Views", "Custom Views", Ico.View, "C"))
            .Group("ViewShowGroup", "Show", null, priority: 180,
                g => g
                .CheckBox("Gridlines", "Gridlines", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Grid), KeyTip = "VG" })
                .CheckBox("Headings", "Headings", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Grid), KeyTip = "VH" })
                .CheckBox("Ruler", "Ruler", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Ruler), KeyTip = "RU" })
                .CheckBox("Formula Bar", "Formula Bar", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Function), KeyTip = "VF" }))
            .Group("ViewZoomGroup", "Zoom", null, priority: 110,
                g => g
                .Large("Zoom", "Zoom", Ico.Zoom, "Q", menu: m => m.Item("200%", "200%", "2").Item(FreeXRibbonCommandIds.ViewZoomPreset100, "100%", "1").Item("75%", "75%", "7").Item("50%", "50%", "5").Item("25%", "25%", "3").Separator().Item("More", "Custom...", "C"))
                .Large(FreeXRibbonCommandIds.ViewZoom100, "100%", Ico.Zoom, "Z1")
                .Large("Zoom to Selection", "Zoom to Selection", Ico.Zoom, "ZS"))
            .Group("ViewWindowGroup", "Window", null, priority: 90,
                g => g
                .Medium("New Window", "New Window", Ico.Window, "NW")
                .Medium("Arrange All", "Arrange All", Ico.Grid, "A", menu: m => m.Item("Tiled", "Tiled", Ico.Grid, "T").Item(FreeXRibbonCommandIds.ViewArrangeHorizontal, "Horizontal", "H").Item("Vertical", "Vertical", "V").Item("Cascade", "Cascade", Ico.Window, "C"))
                .Medium(FreeXRibbonCommandIds.ViewFreezePanes, "Freeze Panes", Ico.Freeze, "FP", menu: m => m.Item(FreeXRibbonCommandIds.ViewFreezeAtSelection, "Freeze Panes", "F").Item("Freeze Top Row", "Freeze Top Row", "R").Item("Freeze First Column", "Freeze First Column", "C").Separator().Item("Unfreeze Panes", "Unfreeze Panes", "U"))
                .IconToggle("Split", "Split", Ico.Window, "SP")
                .IconToggle("View Side by Side", "View Side by Side", Ico.Window, "B")
                .IconToggle("Synchronous Scrolling", "Synchronous Scrolling", Ico.Window, "SS")
                .Medium("Switch Windows", "Switch Windows", Ico.Window, "W")
                .Medium("Hide", "Hide", Ico.View, "H")
                .Medium("Unhide", "Unhide", Ico.View, "U")
                .Medium("Reset Window Position", "Reset Window Position", Ico.Window, "RP"))
        )
        .Tab("HelpTab", "Help", "Y", tab => tab
            .Group("HelpHelpGroup", "Help", null, priority: 200,
                g => g
                .Large(FreeXRibbonCommandIds.HelpOnline, "Help Online", Ico.Help, "H")
                .Large(FreeXRibbonCommandIds.HelpFeedback, "Feedback", Ico.Comment, "F")
                .Large(FreeXRibbonCommandIds.HelpCopyDiagnostics, "Copy Diagnostics", Ico.List, "D")
                .Large(FreeXRibbonCommandIds.HelpCheckForUpdates, "Check for Updates", Ico.Refresh, "U")
                .Large(FreeXRibbonCommandIds.HelpAbout, "About FreeX", Ico.Info, "A")
                .Large(FreeXRibbonCommandIds.HelpLegalNotices, "Legal Notices", Ico.Book, "L"))
        )
        .ContextualTab("ChartDesignTab", "Chart Design", new RibbonTabContext("chart.selected", "Chart Design", RibbonContextColor.Green, KeyTip: "JC", DisplayOrder: 2), tab => tab
            .Group("ChartDesignLayoutsGroup", "Layouts", null, priority: 180,
                g => g
                .Medium("Chart Titles", "Chart Titles", Ico.ChartTitle, "T")
                .Medium("Data Labels", "Data Labels", Ico.Label, "D")
                .Medium("Data Label Position", "Data Label Position", Ico.Align, "P")
                .Medium("Trendline", "Trendline", Ico.Trendline, "R")
                .Medium("Error Bars", "Error Bars", Ico.ErrorBars, "E")
                .Medium("Secondary Axis", "Secondary Axis", Ico.SecondaryAxis, "S")
                .Medium("Secondary Axis Series", "Secondary Axis Series", Ico.SecondaryAxis, "SS"))
            .Group("ChartDesignStylesGroup", "Styles", null, priority: 170,
                g => g
                .Medium("Chart Styles", "Chart Styles", Ico.Theme, "Y"))
            .Group("ChartDesignDataGroup", "Data", null, priority: 160,
                g => g
                .Medium("Select Data Source", "Select Data Source", Ico.Search, "A"))
            .Group("ChartDesignTypeGroup", "Type", null, priority: 150,
                g => g
                .Medium(FreeXRibbonCommandIds.ChartChangeType, "Change Chart Type", Ico.ChartColumn, "CT")
                .Medium("Combo Chart", "Combo Chart", Ico.ComboChart, "CO")
                .Medium("Combo Chart Series", "Combo Chart Series", Ico.ComboChart, "CS"))
            .Group("ChartDesignLocationGroup", "Location", null, priority: 140,
                g => g
                .Medium("Move Chart", "Move Chart", Ico.MoveChart, "M"))
        )
        .ContextualTab("ChartFormatTab", "Format", new RibbonTabContext("chart.selected", "Chart Format", RibbonContextColor.Green, KeyTip: "JF", DisplayOrder: 3), tab => tab
            .Group("ChartFormatCurrentSelectionGroup", "Current Selection", null, priority: 180,
                g => g
                .Medium("Format Chart Area", "Format Chart Area", Ico.ChartColumn, "F")
                .Medium("Format Bar/Column", "Format Bar/Column", Ico.ChartColumn, "B")
                .Medium("Format Pie/Doughnut", "Format Pie/Doughnut", Ico.ChartPie, "P")
                .Medium("Format Bubble Chart", "Format Bubble Chart", Ico.ChartScatter, "U")
                .Medium("Format Stock Chart", "Format Stock Chart", Ico.ChartLine, "S"))
            .Group("ChartFormatShapeStylesGroup", "Shape Styles", null, priority: 170,
                g => g
                .Medium("Chart Area Fill", "Chart Area Fill", Ico.Fill, "AF")
                .Medium("Plot Area Fill", "Plot Area Fill", Ico.Fill, "V")
                .Medium("Plot Area Border", "Plot Area Border", Ico.Border, "O")
                .Medium("Series Color", "Series Color", Ico.Color, "C")
                .Medium("Series Width", "Series Width", Ico.Size, "W")
                .Medium("Series Dash", "Series Dash", Ico.Line, "R")
                .Medium("Series Marker", "Series Marker", Ico.Marker, "K")
                .Medium("Marker Size", "Marker Size", Ico.Marker, "Z"))
            .Group("ChartFormatTextGroup", "Text", null, priority: 80,
                g => g
                .Medium("Chart Title Color", "Chart Title Color", Ico.FontColor, "TC")
                .Medium("Chart Title Size", "Chart Title Size", Ico.Font, "TS")
                .Medium("Axis Title Color", "Axis Title Color", Ico.FontColor, "AC")
                .Medium("Axis Title Size", "Axis Title Size", Ico.Font, "AS")
                .Medium("Legend Text", "Legend Text", Ico.Legend, "LT")
                .Medium("Legend Font Size", "Legend Font Size", Ico.Legend, "LS")
                .Medium("Data Label Text", "Data Label Text", Ico.Label, "DT")
                .Medium("Data Label Fill", "Data Label Fill", Ico.Fill, "DF")
                .Medium("Data Label Border", "Data Label Border", Ico.Border, "DB"))
            .Group("ChartFormatAxesGroup", "Axes", null, priority: 150,
                g => g
                .Medium("X Axis Bounds", "X Axis Bounds", Ico.AxisBounds, "XB")
                .Medium("Y Axis Bounds", "Y Axis Bounds", Ico.AxisBounds, "YB")
                .Medium("X Axis Gridlines", "X Axis Gridlines", Ico.Grid, "XG")
                .Medium("Y Axis Gridlines", "Y Axis Gridlines", Ico.Grid, "YG")
                .Medium("X Axis Labels", "X Axis Labels", Ico.Label, "XA")
                .Medium("Y Axis Labels", "Y Axis Labels", Ico.Label, "YA"))
            .Group("ChartFormatLegacyAxesGroup", "Axis Options", null, priority: 145,
                g => g
                .Medium("X Axis Ticks", "X Axis Ticks", Ico.AxisBounds, "XT")
                .Medium("Y Axis Ticks", "Y Axis Ticks", Ico.AxisBounds, "YT")
                .Medium("X Axis Label Font", "X Axis Label Font", Ico.Font, "XF")
                .Medium("Y Axis Label Font", "Y Axis Label Font", Ico.Font, "YF")
                .Medium("X Axis Label Angle", "X Axis Label Angle", Ico.Rotate, "XLA")
                .Medium("Y Axis Label Angle", "Y Axis Label Angle", Ico.Rotate, "YLA")
                .Medium("X Axis Line", "X Axis Line", Ico.Line, "XL")
                .Medium("Y Axis Line", "Y Axis Line", Ico.Line, "YL")
                .Medium("X Axis Number Format", "X Axis Number Format", Ico.Number, "XNF")
                .Medium("Y Axis Number Format", "Y Axis Number Format", Ico.Number, "YNF")
                .Medium("X Gridline Style", "X Gridline Style", Ico.Grid, "XGS")
                .Medium("Y Gridline Style", "Y Gridline Style", Ico.Grid, "YGS")
                .Medium("X Log Scale", "X Log Scale", Ico.Scale, "XLS")
                .Medium("Y Log Scale", "Y Log Scale", Ico.Scale, "YLS"))
        )
        .ContextualTab("PictureFormatTab", "Picture Format", new RibbonTabContext("picture.selected", "Picture Format", RibbonContextColor.Teal, KeyTip: "JP", DisplayOrder: 1), tab => tab
            .Group("PictureFormatFormatGroup", "Format", null, priority: 180,
                g => g
                .Medium("Format Picture", "Format Picture", Ico.Picture, "FP")
                .Medium("Crop Picture", "Crop Picture", Ico.Picture, "C", menu: m => m.Item("Crop", "Crop...", "C").Item("Reset Crop", "Reset Crop", "R")))
            .Group("PictureFormatArrangeGroup", "Arrange", null, priority: 70,
                g => g
                .Large("Bring Forward", "Bring Forward", Ico.BringForward, "BF")
                .Large("Send Backward", "Send Backward", Ico.SendBackward, "SB")
                .Large(FreeXRibbonCommandIds.DrawingSelectionPane, "Selection Pane", Ico.List, "SP")
                .Large("Rotate Object", "Rotate Object", Ico.Rotate, "RO")
                .Large("Object Size", "Object Size", Ico.Size, "SZ"))
            .Group("PictureFormatAccessibilityGroup", "Accessibility", null, priority: 120,
                g => g
                .Medium("Alt Text", "Alt Text", Ico.Label, "AT"))
        )
        .ContextualTab("ShapeFormatTab", "Shape Format", new RibbonTabContext("shape.selected", "Shape Format", RibbonContextColor.Purple, KeyTip: "JS", DisplayOrder: 0), tab => tab
            .Group("ShapeFormatShapeStylesGroup", "Shape Styles", null, priority: 180,
                g => g
                .Medium("Shape Fill", "Shape Fill", Ico.RibbonShape, "F")
                .Medium("Object Outline", "Object Outline", Ico.Border, "O")
                .Medium("Shape Gradient", "Shape Gradient", Ico.RibbonShape, "G")
                .Medium("Shape Effects", "Shape Effects", Ico.RibbonShape, "E", menu: m => m.Item("No Effect", "No Effect", "N").Separator().Item("Shadow", "Shadow", "S").Item("Inner Shadow", "Inner Shadow", "I").Item("Reflection", "Reflection", "R").Item("Glow", "Glow", "G").Item("Soft Edges", "Soft Edges", "E").Item("Bevel", "Bevel", "B").Item("3-D Rotation", "3-D Rotation", "D")))
            .Group("ShapeFormatArrangeGroup", "Arrange", null, priority: 70,
                g => g
                .Large("Bring Forward", "Bring Forward", Ico.BringForward, "BF")
                .Large("Send Backward", "Send Backward", Ico.SendBackward, "SB")
                .Large(FreeXRibbonCommandIds.DrawingSelectionPane, "Selection Pane", Ico.List, "SP")
                .Large("Rotate Object", "Rotate Object", Ico.Rotate, "RO")
                .Large("Object Size", "Object Size", Ico.Size, "SZ"))
            .Group("ShapeFormatAccessibilityGroup", "Accessibility", null, priority: 120,
                g => g
                .Medium("Alt Text", "Alt Text", Ico.Label, "AT"))
        )
        .ContextualTab("TableDesignTab", "Table Design", new RibbonTabContext("table.active", "Table Design", RibbonContextColor.Blue, KeyTip: "JT", DisplayOrder: 4), tab => tab
            .Group("TableDesignPropertiesGroup", "Properties", null, priority: 180,
                g => g
                .Medium("Table Name", "Table Name", Ico.Table, "N")
                .Medium("Resize Table", "Resize Table", Ico.Scale, "Z"))
            .Group("TableDesignToolsGroup", "Tools", null, priority: 170,
                g => g
                .Medium("Summarize with PivotTable", "Summarize with PivotTable", Ico.PivotTable, "S")
                .Large(FreeXRibbonCommandIds.TableRemoveDuplicates, "Remove Duplicates", Ico.Delete, "D")
                .Medium("Convert to Range", "Convert to Range", Ico.Refresh, "V"))
            .Group("TableDesignStyleOptionsGroup", "Style Options", null, priority: 160,
                g => g
                .CheckBox("Total Row", "Total Row", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Sum), KeyTip = "T" })
                .CheckBox("First Column", "First Column", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Table), KeyTip = "FC" })
                .CheckBox("Last Column", "Last Column", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Table), KeyTip = "L" })
                .CheckBox(FreeXRibbonCommandIds.TableBandedRows, "Banded Rows", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Table), KeyTip = "B" })
                .CheckBox(FreeXRibbonCommandIds.TableBandedColumns, "Banded Columns", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Table), KeyTip = "C" })
                .CheckBox("Filter Button", "Filter Button", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Filter), KeyTip = "A" }))
            .Group("TableDesignStylesGroup", "Styles", null, priority: 150,
                g => g
                .Medium("Table Styles", "Table Styles", Ico.Theme, "Y"))
        )
        .ContextualTab("PivotTableAnalyzeTab", "PivotTable Analyze", new RibbonTabContext("pivot.active", "PivotTable Analyze", RibbonContextColor.Orange, KeyTip: "JA", DisplayOrder: 5), tab => tab
            .Group("PivotTableAnalyzePivotTableGroup", "Pivot Table", null, priority: 180,
                g => g
                .Medium("PivotTable Name", "PivotTable Name", Ico.PivotTable, "N")
                .Medium("PivotTable Options", "PivotTable Options", Ico.PivotTable, "O"))
            .Group("PivotTableAnalyzeActiveFieldGroup", "Active Field", null, priority: 170,
                g => g
                .Medium("Show Details", "Show Details", Ico.List, "D")
                .Medium("Field Settings", "Field Settings", Ico.Label, "FS"))
            .Group("PivotTableAnalyzeGroupGroup", "Group", null, priority: 160,
                g => g
                .Medium("Group Field", "Group Field", Ico.Group, "GF")
                .Large(FreeXRibbonCommandIds.PivotUngroup, "Ungroup", Ico.Group, "UG"))
            .Group("PivotTableAnalyzeFilterGroup", "Filter", null, priority: 150,
                g => g
                .Large("Insert Slicer", "Insert Slicer", Ico.Filter, "IS")
                .Large("Insert Timeline", "Insert Timeline", Ico.Date, "IT"))
            .Group("PivotTableAnalyzeDataGroup", "Data", null, priority: 140,
                g => g
                .Medium("Refresh", "Refresh", Ico.Refresh, "R")
                .Medium("Change Data Source", "Change Data Source", Ico.GetData, "CD"))
            .Group("PivotTableAnalyzeActionsGroup", "Actions", null, priority: 130,
                g => g
                .Medium(FreeXRibbonCommandIds.PivotClear, "Clear", Ico.Clear, "CL")
                .Medium("Select", "Select", Ico.Search, "SE")
                .Medium("Move PivotTable", "Move PivotTable", Ico.PivotTable, "M"))
            .Group("PivotTableAnalyzeCalculationsGroup", "Calculations", null, priority: 120,
                g => g
                .Medium("Calculated Field", "Calculated Field", Ico.Refresh, "CF")
                .Medium("Calculated Item", "Calculated Item", Ico.Refresh, "CI"))
            .Group("PivotTableAnalyzeToolsGroup", "Tools", null, priority: 110,
                g => g
                .Medium("PivotChart", "PivotChart", Ico.ChartColumn, "PC")
                .Medium(FreeXRibbonCommandIds.PivotChartChangeType, "Change Chart Type", Ico.ChartColumn, "CT")
                .Medium("PivotChart Options", "PivotChart Options", Ico.ChartColumn, "CO"))
            .Group("PivotTableAnalyzeShowGroup", "Show", null, priority: 180,
                g => g
                .Medium("Field List", "Field List", Ico.Label, "FL")
                .Medium("+/- Buttons", "+/- Buttons", Ico.Expand, "PB")
                .Large("Field Headers", "Field Headers", Ico.HeaderFooter, "FH"))
        )
        .ContextualTab("PivotTableDesignTab", "Design", new RibbonTabContext("pivot.active", "PivotTable Design", RibbonContextColor.Orange, KeyTip: "JD", DisplayOrder: 6), tab => tab
            .Group("PivotTableDesignLayoutGroup", "Layout", null, priority: 180,
                g => g
                .Medium("Grand Totals", "Grand Totals", Ico.Sum, "G")
                .Large("Subtotals", "Subtotals", Ico.Sum, "S")
                .Medium("Report Layout", "Report Layout", Ico.List, "L")
                .Medium("Blank Rows", "Blank Rows", Ico.List, "B"))
            .Group("PivotTableDesignStyleOptionsGroup", "Style Options", null, priority: 170,
                g => g
                .Medium(FreeXRibbonCommandIds.PivotBandedRows, "Banded Rows", Ico.Table, "R")
                .Medium(FreeXRibbonCommandIds.PivotBandedColumns, "Banded Columns", Ico.Table, "C")
                .Large("Row Headers", "Row Headers", Ico.HeaderFooter, "H")
                .Large("Column Headers", "Column Headers", Ico.HeaderFooter, "O"))
            .Group("PivotTableDesignStylesGroup", "Styles", null, priority: 160,
                g => g
                .Medium("PivotTable Styles", "PivotTable Styles", Ico.PivotTable, "Y"))
        )
        .Build();
}
