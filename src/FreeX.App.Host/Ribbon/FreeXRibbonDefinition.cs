using FreeX.Ribbon;

namespace FreeX.App.Host;

/// <summary>
/// The complete FreeX ribbon authored declaratively, generated from the catalog structure of
/// the original MainWindow.xaml ribbon (all main tabs + contextual tabs). Command ids match
/// the catalog CommandNames so the registry binds them to existing handlers.
/// </summary>
public static class FreeXRibbonDefinition
{
    public static RibbonDefinition Build() => new RibbonDefinitionBuilder()
        .Tab("HomeTab", "Home", "H", tab => tab
            .Group("HomeClipboardGroup", "Clipboard", null, priority: 180,
                g => g
                .Button("Paste", "Paste", b => b with { PreferredLayout = RibbonCommandLayoutKind.Large, Icon = new RibbonCommandIcon(RibbonCommandIconKind.Paste), KeyTip = "V" })
                .Button("Cut", "Cut", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Cut), KeyTip = "X" })
                .Button("Copy", "Copy", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Copy), KeyTip = "C" })
                .Button("Format Painter", "Format Painter", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.FormatPainter), KeyTip = "FP" }))
            .Group("HomeFontGroup", "Font", null, priority: 170,
                g => g
                .ComboBox("Font", "Font", c => c with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Font) })
                .ComboBox("Font Size", "Font Size", c => c with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Font) })
                .Button("Increase Font Size", "Increase Font Size", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Font), KeyTip = "FG" })
                .Button("Decrease Font Size", "Decrease Font Size", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Font), KeyTip = "FK" })
                .Toggle("Bold", "Bold", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Bold), KeyTip = "1" })
                .Toggle("Italic", "Italic", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Italic), KeyTip = "2" })
                .Toggle("Underline", "Underline", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Underline), KeyTip = "3" })
                .Toggle("Strikethrough", "Strikethrough", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Strikethrough), KeyTip = "4" })
                .Button("Borders", "Borders", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Border), KeyTip = "B" })
                .Button("Fill Color", "Fill Color", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Fill), KeyTip = "H" })
                .Button("Font Color", "Font Color", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Color), KeyTip = "FC" }))
            .Group("HomeAlignmentGroup", "Alignment", null, priority: 160,
                g => g
                .Toggle("Top Align", "Top Align", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Align), KeyTip = "AT" })
                .Toggle("Middle Align", "Middle Align", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Align), KeyTip = "AM" })
                .Toggle("Bottom Align", "Bottom Align", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Align), KeyTip = "AB" })
                .Button("Orientation", "Orientation", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Orientation), KeyTip = "RO" })
                .Toggle("Wrap Text", "Wrap Text", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Wrap), KeyTip = "W" })
                .Toggle("Align Left", "Align Left", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Align), KeyTip = "AL" })
                .Toggle("Center", "Center", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Generic), KeyTip = "AC" })
                .Toggle("Align Right", "Align Right", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Align), KeyTip = "AR" })
                .Button("Decrease Indent", "Decrease Indent", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Align), KeyTip = "AO" })
                .Button("Increase Indent", "Increase Indent", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Align), KeyTip = "AI" })
                .Button("Merge & Center", "Merge & Center", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Merge), KeyTip = "M" }))
            .Group("HomeNumberGroup", "Number", null, priority: 150,
                g => g
                .ComboBox("Number Format", "Number Format", c => c with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Number) })
                .Button("Accounting Number Format", "Accounting Number Format", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Number), KeyTip = "AN" })
                .Button("Percent Style", "Percent Style", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Percent), KeyTip = "P" })
                .Button("Comma Style", "Comma Style", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Comma), KeyTip = "K" })
                .Button("Increase Decimal Places", "Increase Decimal Places", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Decimal), KeyTip = "QI" })
                .Button("Decrease Decimal Places", "Decrease Decimal Places", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Decimal), KeyTip = "QD" }))
            .Group("HomeStylesGroup", "Styles", null, priority: 140,
                g => g
                .Button("Conditional Formatting", "Conditional Formatting", b => b with { PreferredLayout = RibbonCommandLayoutKind.Large, Icon = new RibbonCommandIcon(RibbonCommandIconKind.Effects), KeyTip = "L" })
                .Button("Format as Table", "Format as Table", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Table), KeyTip = "T" })
                .Button("Cell Styles", "Cell Styles", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Theme), KeyTip = "J" }))
            .Group("HomeCellsGroup", "Cells", null, priority: 130,
                g => g
                .Button("Insert", "Insert", b => b with { PreferredLayout = RibbonCommandLayoutKind.Large, Icon = new RibbonCommandIcon(RibbonCommandIconKind.Insert), KeyTip = "I" })
                .Button("Delete", "Delete", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Delete), KeyTip = "D" })
                .Button("Format", "Format", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Size), KeyTip = "O" }))
            .Group("HomeEditingGroup", "Editing", null, priority: 120,
                g => g
                .Button("AutoSum", "AutoSum", b => b with { PreferredLayout = RibbonCommandLayoutKind.Large, Icon = new RibbonCommandIcon(RibbonCommandIconKind.Sum), KeyTip = "U" })
                .Button("Fill", "Fill", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Generic), KeyTip = "FI" })
                .Button("Clear", "Clear", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Clear), KeyTip = "E" })
                .Button("Sort & Filter", "Sort & Filter", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Sort), KeyTip = "S" })
                .Button("Find & Select", "Find & Select", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Search), KeyTip = "FD" }))
        )
        .Tab("InsertTab", "Insert", "N", tab => tab
            .Group("InsertTablesGroup", "Tables", null, priority: 180,
                g => g
                .Button("PivotTable", "PivotTable", b => b with { PreferredLayout = RibbonCommandLayoutKind.Large, Icon = new RibbonCommandIcon(RibbonCommandIconKind.PivotTable), KeyTip = "PT" })
                .Button("Table", "Table", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Generic), KeyTip = "TB" }))
            .Group("InsertChartsGroup", "Charts", null, priority: 170,
                g => g
                .Button("Recommended Charts", "Recommended Charts", b => b with { PreferredLayout = RibbonCommandLayoutKind.Large, Icon = new RibbonCommandIcon(RibbonCommandIconKind.ChartColumn), KeyTip = "RC" })
                .Button("Column Chart", "Column Chart", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.ChartColumn), KeyTip = "CC" })
                .Button("Stacked Column Chart", "Stacked Column Chart", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.ChartColumn), KeyTip = "SC" })
                .Button("100% Stacked Column Chart", "100% Stacked Column Chart", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.ChartColumn), KeyTip = "PC" })
                .Button("Bar Chart", "Bar Chart", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.ChartColumn), KeyTip = "BC" })
                .Button("Stacked Bar Chart", "Stacked Bar Chart", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.ChartColumn), KeyTip = "SB" })
                .Button("100% Stacked Bar Chart", "100% Stacked Bar Chart", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.ChartColumn), KeyTip = "PB" })
                .Button("Line Chart", "Line Chart", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.ChartLine), KeyTip = "LC" })
                .Button("3D Line Chart", "3D Line Chart", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.ChartLine), KeyTip = "3L" })
                .Button("Area Chart", "Area Chart", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.ChartArea), KeyTip = "AC" })
                .Button("3D Area Chart", "3D Area Chart", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.ChartArea), KeyTip = "3A" })
                .Button("Stock Chart", "Stock Chart", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.ChartLine), KeyTip = "ST" })
                .Button("Pie Chart", "Pie Chart", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.ChartPie), KeyTip = "PY" })
                .Button("3D Pie Chart", "3D Pie Chart", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.ChartPie), KeyTip = "3P" })
                .Button("Doughnut Chart", "Doughnut Chart", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.ChartPie), KeyTip = "DO" })
                .Button("Scatter Chart", "Scatter Chart", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.ChartScatter), KeyTip = "SX" }))
            .Group("InsertSparklinesGroup", "Sparklines", null, priority: 160,
                g => g
                .Button("Line Sparkline", "Line Sparkline", b => b with { PreferredLayout = RibbonCommandLayoutKind.Large, Icon = new RibbonCommandIcon(RibbonCommandIconKind.Sparkline), KeyTip = "SL" })
                .Button("Column Sparkline", "Column Sparkline", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Sparkline), KeyTip = "SK" })
                .Button("Win/Loss Sparkline", "Win/Loss Sparkline", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Sparkline), KeyTip = "SW" }))
            .Group("InsertFiltersGroup", "Filters", null, priority: 150,
                g => g
                .Button("Insert Slicer", "Insert Slicer", b => b with { PreferredLayout = RibbonCommandLayoutKind.Large, Icon = new RibbonCommandIcon(RibbonCommandIconKind.Filter), KeyTip = "SF" })
                .Button("Insert Timeline", "Insert Timeline", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Date), KeyTip = "IT" }))
            .Group("InsertLinksGroup", "Links", null, priority: 140,
                g => g
                .Button("Insert Link", "Insert Link", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Link), KeyTip = "K" }))
            .Group("InsertCommentsGroup", "Comments", null, priority: 130,
                g => g
                .Button("Comment", "Comment", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Comment), KeyTip = "C2" }))
            .Group("InsertTextGroup", "Text", null, priority: 120,
                g => g
                .Button("Text Box", "Text Box", b => b with { PreferredLayout = RibbonCommandLayoutKind.Large, Icon = new RibbonCommandIcon(RibbonCommandIconKind.TextBox), KeyTip = "TX" })
                .Button("Header & Footer", "Header & Footer", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.HeaderFooter), KeyTip = "HF" }))
            .Group("InsertSymbolsGroup", "Symbols", null, priority: 110,
                g => g
                .Button("Symbol", "Symbol", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Symbol), KeyTip = "SY" }))
        )
        .Tab("DrawTab", "Draw", "J", tab => tab
            .Group("DrawIllustrationsGroup", "Illustrations", null, priority: 180,
                g => g
                .Button("Pictures", "Pictures", b => b with { PreferredLayout = RibbonCommandLayoutKind.Large, Icon = new RibbonCommandIcon(RibbonCommandIconKind.Picture), KeyTip = "IP" })
                .Button("Shapes", "Shapes", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.RibbonShape), KeyTip = "SH" }))
            .Group("DrawArrangeGroup", "Arrange", null, priority: 170,
                g => g
                .Button("Bring Forward", "Bring Forward", b => b with { PreferredLayout = RibbonCommandLayoutKind.Large, Icon = new RibbonCommandIcon(RibbonCommandIconKind.BringForward), KeyTip = "BF" })
                .Button("Send Backward", "Send Backward", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.SendBackward), KeyTip = "SB" })
                .Button("Selection Pane", "Selection Pane", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.List), KeyTip = "SP" })
                .Button("Rotate Object", "Rotate Object", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Rotate), KeyTip = "RO" })
                .Button("Object Size", "Object Size", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Size), KeyTip = "SZ" }))
            .Group("DrawFormatGroup", "Format", null, priority: 160,
                g => g
                .Button("Shape Fill", "Shape Fill", b => b with { PreferredLayout = RibbonCommandLayoutKind.Large, Icon = new RibbonCommandIcon(RibbonCommandIconKind.RibbonShape), KeyTip = "OF" })
                .Button("Object Outline", "Object Outline", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Border), KeyTip = "OO" })
                .Button("Crop Picture", "Crop Picture", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Picture), KeyTip = "C" })
                .Button("Shape Gradient", "Shape Gradient", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.RibbonShape), KeyTip = "G" })
                .Button("Shape Effects", "Shape Effects", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.RibbonShape), KeyTip = "FX" }))
        )
        .Tab("PageLayoutTab", "Page Layout", "P", tab => tab
            .Group("PageLayoutThemesGroup", "Themes", null, priority: 180,
                g => g
                .Button("Themes", "Themes", b => b with { PreferredLayout = RibbonCommandLayoutKind.Large, Icon = new RibbonCommandIcon(RibbonCommandIconKind.Theme), KeyTip = "TH" })
                .Button("Theme Colors", "Theme Colors", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Theme), KeyTip = "TC" })
                .Button("Theme Fonts", "Theme Fonts", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Font), KeyTip = "TF" })
                .Button("Theme Effects", "Theme Effects", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Effects), KeyTip = "TE" }))
            .Group("PageLayoutPageSetupGroup", "Page Setup", null, priority: 170,
                g => g
                .Button("Margins", "Margins", b => b with { PreferredLayout = RibbonCommandLayoutKind.Large, Icon = new RibbonCommandIcon(RibbonCommandIconKind.Margins), KeyTip = "M" })
                .Button("Page Orientation", "Page Orientation", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Orientation), KeyTip = "OR" })
                .Button("Paper Size", "Paper Size", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Page), KeyTip = "SZ" })
                .Button("Print Area", "Print Area", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Print), KeyTip = "PA" })
                .Button("Breaks", "Breaks", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.PageBreak), KeyTip = "BK" })
                .Button("Background", "Background", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Picture), KeyTip = "BG" })
                .Button("Print Titles", "Print Titles", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Print), KeyTip = "PT" })
                .Button("Page Setup", "Page Setup", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Page), KeyTip = "PS" })
                .Button("Page Setup dialog", "Page Setup dialog", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Page), KeyTip = "PD" }))
            .Group("PageLayoutScaleToFitGroup", "Scale To Fit", null, priority: 160,
                g => g
                .ComboBox("Scale Width", "Scale Width", c => c with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Scale) })
                .ComboBox("Scale Height", "Scale Height", c => c with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Scale) })
                .ComboBox("Scale Percent", "Scale Percent", c => c with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Percent) })
                .Button("Scale to Fit", "Scale to Fit", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Scale), KeyTip = "SF" }))
            .Group("PageLayoutSheetOptionsGroup", "Sheet Options", null, priority: 150,
                g => g
                .CheckBox("View Gridlines", "View Gridlines", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Grid), KeyTip = "VG" })
                .CheckBox("Print Gridlines", "Print Gridlines", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Grid), KeyTip = "PG" })
                .CheckBox("View Headings", "View Headings", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Grid), KeyTip = "VH" })
                .CheckBox("Print Headings", "Print Headings", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Grid), KeyTip = "PH" }))
        )
        .Tab("FormulasTab", "Formulas", "M", tab => tab
            .Group("FormulasFunctionLibraryGroup", "Function Library", null, priority: 180,
                g => g
                .Button("AutoSum", "AutoSum", b => b with { PreferredLayout = RibbonCommandLayoutKind.Large, Icon = new RibbonCommandIcon(RibbonCommandIconKind.Sum), KeyTip = "U" })
                .Button("Recently Used", "Recently Used", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Generic), KeyTip = "RU" })
                .Button("Financial", "Financial", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Financial), KeyTip = "Y" })
                .Button("Logical Functions", "Logical Functions", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Logical), KeyTip = "L" })
                .Button("Text Functions", "Text Functions", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.TextFunction), KeyTip = "TF" })
                .Button("Date & Time", "Date & Time", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Date), KeyTip = "DT" })
                .Button("Lookup & Reference", "Lookup & Reference", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Search), KeyTip = "K" })
                .Button("Math & Trig", "Math & Trig", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Math), KeyTip = "MT" })
                .Button("More Functions", "More Functions", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Function), KeyTip = "MF" }))
            .Group("FormulasDefinedNamesGroup", "Defined Names", null, priority: 170,
                g => g
                .Button("Name Manager", "Name Manager", b => b with { PreferredLayout = RibbonCommandLayoutKind.Large, Icon = new RibbonCommandIcon(RibbonCommandIconKind.Label), KeyTip = "N" })
                .Button("Define Name", "Define Name", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Label), KeyTip = "DN" })
                .Button("Use in Formula", "Use in Formula", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Function), KeyTip = "I" })
                .Button("Create from Selection", "Create from Selection", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Label), KeyTip = "CS" }))
            .Group("FormulasFormulaAuditingGroup", "Formula Auditing", null, priority: 160,
                g => g
                .Button("Trace Precedents", "Trace Precedents", b => b with { PreferredLayout = RibbonCommandLayoutKind.Large, Icon = new RibbonCommandIcon(RibbonCommandIconKind.Link), KeyTip = "TP" })
                .Button("Trace Dependents", "Trace Dependents", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Link), KeyTip = "TD" })
                .Button("Remove Arrows", "Remove Arrows", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Clear), KeyTip = "RA" })
                .Toggle("Show Formulas", "Show Formulas", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Function), KeyTip = "SF" })
                .Button("Error Checking", "Error Checking", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Warning), KeyTip = "EC" })
                .Button("Evaluate Formula", "Evaluate Formula", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Function), KeyTip = "V" })
                .Button("Watch Window", "Watch Window", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Watch), KeyTip = "W" }))
            .Group("FormulasCalculationGroup", "Calculation", null, priority: 150,
                g => g
                .Button("Calculate Now", "Calculate Now", b => b with { PreferredLayout = RibbonCommandLayoutKind.Large, Icon = new RibbonCommandIcon(RibbonCommandIconKind.Refresh), KeyTip = "CN" })
                .Button("Calculate Sheet", "Calculate Sheet", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Refresh), KeyTip = "SC" })
                .Button("Calculation Options", "Calculation Options", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Refresh), KeyTip = "O" }))
        )
        .Tab("DataTab", "Data", "A", tab => tab
            .Group("DataGetTransformGroup", "Get Transform", null, priority: 180,
                g => g
                .Button("Get Data", "Get Data", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.GetData), KeyTip = "D" }))
            .Group("DataQueriesConnectionsGroup", "Queries Connections", null, priority: 170,
                g => g
                .Button("Refresh All", "Refresh All", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Refresh), KeyTip = "FA" }))
            .Group("DataSortFilterGroup", "Sort Filter", null, priority: 160,
                g => g
                .Button("Sort A to Z", "Sort A to Z", b => b with { PreferredLayout = RibbonCommandLayoutKind.Large, Icon = new RibbonCommandIcon(RibbonCommandIconKind.SortAscending), KeyTip = "SA" })
                .Button("Sort Z to A", "Sort Z to A", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.SortDescending), KeyTip = "SD" })
                .Button("Sort", "Sort", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Sort), KeyTip = "SO" })
                .Button("Filter", "Filter", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Filter), KeyTip = "T" })
                .Button("Clear", "Clear", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Clear), KeyTip = "C" })
                .Button("Advanced", "Advanced", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Filter), KeyTip = "A" })
                .Button("Reapply", "Reapply", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Refresh), KeyTip = "R" }))
            .Group("DataToolsGroup", "Tools", null, priority: 150,
                g => g
                .Button("Text to Columns", "Text to Columns", b => b with { PreferredLayout = RibbonCommandLayoutKind.Large, Icon = new RibbonCommandIcon(RibbonCommandIconKind.TextColumns), KeyTip = "E" })
                .Button("Flash Fill", "Flash Fill", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Flash), KeyTip = "FF" })
                .Button("Remove Duplicates", "Remove Duplicates", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Delete), KeyTip = "M" })
                .Button("Data Validation", "Data Validation", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.List), KeyTip = "V" })
                .Button("Consolidate", "Consolidate", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Date), KeyTip = "N" }))
            .Group("DataForecastGroup", "Forecast", null, priority: 140,
                g => g
                .Button("What-If Analysis", "What-If Analysis", b => b with { PreferredLayout = RibbonCommandLayoutKind.Large, Icon = new RibbonCommandIcon(RibbonCommandIconKind.Function), KeyTip = "W" })
                .Button("Forecast Sheet", "Forecast Sheet", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.ChartLine), KeyTip = "FS" }))
            .Group("DataOutlineGroup", "Outline", null, priority: 130,
                g => g
                .Button("Group", "Group", b => b with { PreferredLayout = RibbonCommandLayoutKind.Large, Icon = new RibbonCommandIcon(RibbonCommandIconKind.Group), KeyTip = "G" })
                .Button("Ungroup", "Ungroup", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Group), KeyTip = "U" })
                .Button("Subtotal", "Subtotal", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Sum), KeyTip = "B" })
                .Button("Hide Detail", "Hide Detail", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.List), KeyTip = "H" })
                .Button("Show Detail", "Show Detail", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.List), KeyTip = "J" }))
        )
        .Tab("ReviewTab", "Review", "R", tab => tab
            .Group("ReviewProofingGroup", "Proofing", null, priority: 180,
                g => g
                .Button("Spelling", "Spelling", b => b with { PreferredLayout = RibbonCommandLayoutKind.Large, Icon = new RibbonCommandIcon(RibbonCommandIconKind.Spelling), KeyTip = "SP" })
                .Button("Workbook Statistics", "Workbook Statistics", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Info), KeyTip = "W" }))
            .Group("ReviewAccessibilityGroup", "Accessibility", null, priority: 170,
                g => g
                .Button("Check Accessibility", "Check Accessibility", b => b with { PreferredLayout = RibbonCommandLayoutKind.Large, Icon = new RibbonCommandIcon(RibbonCommandIconKind.Accessibility), KeyTip = "CA" })
                .Button("Alt Text", "Alt Text", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Label), KeyTip = "T" }))
            .Group("ReviewCommentsGroup", "Comments", null, priority: 160,
                g => g
                .Button("New Comment", "New Comment", b => b with { PreferredLayout = RibbonCommandLayoutKind.Large, Icon = new RibbonCommandIcon(RibbonCommandIconKind.Comment), KeyTip = "CM" })
                .Button("Delete Comment", "Delete Comment", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Comment), KeyTip = "XC" })
                .Button("Previous Comment", "Previous Comment", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Comment), KeyTip = "PC" })
                .Button("Next Comment", "Next Comment", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Comment), KeyTip = "JC" })
                .Button("Show Comments", "Show Comments", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Comment), KeyTip = "SC" }))
            .Group("ReviewNotesGroup", "Notes", null, priority: 150,
                g => g
                .Button("New Note", "New Note", b => b with { PreferredLayout = RibbonCommandLayoutKind.Large, Icon = new RibbonCommandIcon(RibbonCommandIconKind.Comment), KeyTip = "O" })
                .Button("Edit Note", "Edit Note", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Comment), KeyTip = "E" })
                .Button("Delete Note", "Delete Note", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Comment), KeyTip = "D" })
                .Button("Previous Note", "Previous Note", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Comment), KeyTip = "PN" })
                .Button("Next Note", "Next Note", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Comment), KeyTip = "N" })
                .Button("Show Notes", "Show Notes", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Comment), KeyTip = "H" }))
            .Group("ReviewProtectGroup", "Protect", null, priority: 140,
                g => g
                .Button("Protect Sheet", "Protect Sheet", b => b with { PreferredLayout = RibbonCommandLayoutKind.Large, Icon = new RibbonCommandIcon(RibbonCommandIconKind.Protect), KeyTip = "PS" })
                .Button("Protect Workbook", "Protect Workbook", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Protect), KeyTip = "PW" })
                .Button("Allow Users to Edit Ranges", "Allow Users to Edit Ranges", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Generic), KeyTip = "AR" })
                .Button("Share", "Share", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Share), KeyTip = "SH" }))
        )
        .Tab("ViewTab", "View", "W", tab => tab
            .Group("ViewWorkbookViewsGroup", "Workbook Views", null, priority: 180,
                g => g
                .Toggle("Normal", "Normal", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.View), KeyTip = "L" })
                .Toggle("Page Break Preview", "Page Break Preview", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.PageBreak), KeyTip = "I" })
                .Toggle("Page Layout", "Page Layout", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Page), KeyTip = "P" })
                .Button("Custom Views", "Custom Views", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.View), KeyTip = "C" }))
            .Group("ViewShowGroup", "Show", null, priority: 170,
                g => g
                .CheckBox("Gridlines", "Gridlines", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Grid), KeyTip = "VG" })
                .CheckBox("Headings", "Headings", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Grid), KeyTip = "VH" })
                .CheckBox("Ruler", "Ruler", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Ruler), KeyTip = "RU" })
                .CheckBox("Formula Bar", "Formula Bar", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Function), KeyTip = "VF" }))
            .Group("ViewZoomGroup", "Zoom", null, priority: 160,
                g => g
                .Button("Zoom", "Zoom", b => b with { PreferredLayout = RibbonCommandLayoutKind.Large, Icon = new RibbonCommandIcon(RibbonCommandIconKind.Zoom), KeyTip = "Q" })
                .Button("100%", "100%", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Generic), KeyTip = "Z1" })
                .Button("Zoom to Selection", "Zoom to Selection", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Zoom), KeyTip = "ZS" }))
            .Group("ViewWindowGroup", "Window", null, priority: 150,
                g => g
                .Button("New Window", "New Window", b => b with { PreferredLayout = RibbonCommandLayoutKind.Large, Icon = new RibbonCommandIcon(RibbonCommandIconKind.Window), KeyTip = "NW" })
                .Button("Arrange All", "Arrange All", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Grid), KeyTip = "A" })
                .Button("Freeze Panes", "Freeze Panes", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Freeze), KeyTip = "FP" })
                .Toggle("Split", "Split", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Window), KeyTip = "SP" })
                .Toggle("View Side by Side", "View Side by Side", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Window), KeyTip = "B" })
                .Toggle("Synchronous Scrolling", "Synchronous Scrolling", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Window), KeyTip = "SS" })
                .Button("Switch Windows", "Switch Windows", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Window), KeyTip = "W" })
                .Button("Hide", "Hide", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.View), KeyTip = "H" })
                .Button("Unhide", "Unhide", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.View), KeyTip = "U" })
                .Button("Reset Window Position", "Reset Window Position", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Window), KeyTip = "RP" }))
        )
        .ContextualTab("ChartDesignTab", "Chart Design", new RibbonTabContext("chart.selected", "Chart Design", RibbonContextColor.Green), tab => tab
            .Group("ChartDesignLayoutsGroup", "Layouts", null, priority: 180,
                g => g
                .Button("Chart Titles", "Chart Titles", b => b with { PreferredLayout = RibbonCommandLayoutKind.Large, Icon = new RibbonCommandIcon(RibbonCommandIconKind.ChartColumn), KeyTip = "T" })
                .Button("Data Labels", "Data Labels", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Label), KeyTip = "D" })
                .Button("Data Label Position", "Data Label Position", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Label), KeyTip = "P" })
                .Button("Trendline", "Trendline", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.ChartLine), KeyTip = "R" })
                .Button("Error Bars", "Error Bars", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.ChartLine), KeyTip = "E" })
                .Button("Secondary Axis", "Secondary Axis", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.ChartLine), KeyTip = "S" }))
            .Group("ChartDesignStylesGroup", "Styles", null, priority: 170,
                g => g
                .Button("Chart Styles", "Chart Styles", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.ChartColumn), KeyTip = "Y" }))
            .Group("ChartDesignDataGroup", "Data", null, priority: 160,
                g => g
                .Button("Select Data Source", "Select Data Source", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Search), KeyTip = "A" }))
            .Group("ChartDesignTypeGroup", "Type", null, priority: 150,
                g => g
                .Button("Change Chart Type", "Change Chart Type", b => b with { PreferredLayout = RibbonCommandLayoutKind.Large, Icon = new RibbonCommandIcon(RibbonCommandIconKind.ChartColumn), KeyTip = "CT" })
                .Button("Combo Chart", "Combo Chart", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.ChartColumn), KeyTip = "CO" })
                .Button("Combo Chart Series", "Combo Chart Series", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.ChartColumn), KeyTip = "CS" }))
            .Group("ChartDesignLocationGroup", "Location", null, priority: 140,
                g => g
                .Button("Move Chart", "Move Chart", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.ChartColumn), KeyTip = "M" }))
        )
        .ContextualTab("ChartFormatTab", "Chart Format", new RibbonTabContext("chart.selected", "Chart Format", RibbonContextColor.Green), tab => tab
            .Group("ChartFormatCurrentSelectionGroup", "Current Selection", null, priority: 180,
                g => g
                .Button("Format Chart Area", "Format Chart Area", b => b with { PreferredLayout = RibbonCommandLayoutKind.Large, Icon = new RibbonCommandIcon(RibbonCommandIconKind.ChartColumn), KeyTip = "F" })
                .Button("Format Bar/Column", "Format Bar/Column", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Size), KeyTip = "B" })
                .Button("Format Pie/Doughnut", "Format Pie/Doughnut", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.ChartPie), KeyTip = "P" })
                .Button("Format Bubble Chart", "Format Bubble Chart", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.ChartScatter), KeyTip = "U" })
                .Button("Format Stock Chart", "Format Stock Chart", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.ChartLine), KeyTip = "S" }))
            .Group("ChartFormatShapeStylesGroup", "Shape Styles", null, priority: 170,
                g => g
                .Button("Chart Area Fill", "Chart Area Fill", b => b with { PreferredLayout = RibbonCommandLayoutKind.Large, Icon = new RibbonCommandIcon(RibbonCommandIconKind.ChartColumn), KeyTip = "AF" })
                .Button("Plot Area Fill", "Plot Area Fill", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.ChartArea), KeyTip = "V" })
                .Button("Plot Area Border", "Plot Area Border", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Border), KeyTip = "O" })
                .Button("Series Color", "Series Color", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.ChartColumn), KeyTip = "C" })
                .Button("Series Width", "Series Width", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.ChartColumn), KeyTip = "W" })
                .Button("Series Dash", "Series Dash", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.ChartColumn), KeyTip = "R" })
                .Button("Series Marker", "Series Marker", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.ChartColumn), KeyTip = "K" })
                .Button("Marker Size", "Marker Size", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.ChartScatter), KeyTip = "Z" }))
            .Group("ChartFormatTextGroup", "Text", null, priority: 160,
                g => g
                .Button("Chart Title Color", "Chart Title Color", b => b with { PreferredLayout = RibbonCommandLayoutKind.Large, Icon = new RibbonCommandIcon(RibbonCommandIconKind.ChartColumn), KeyTip = "TC" })
                .Button("Chart Title Size", "Chart Title Size", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.ChartColumn), KeyTip = "TS" })
                .Button("Axis Title Color", "Axis Title Color", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.ChartLine), KeyTip = "AC" })
                .Button("Axis Title Size", "Axis Title Size", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.ChartLine), KeyTip = "AS" })
                .Button("Legend Text", "Legend Text", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Label), KeyTip = "LT" })
                .Button("Legend Font Size", "Legend Font Size", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Font), KeyTip = "LS" })
                .Button("Data Label Text", "Data Label Text", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Label), KeyTip = "DT" })
                .Button("Data Label Fill", "Data Label Fill", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Label), KeyTip = "DF" })
                .Button("Data Label Border", "Data Label Border", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Border), KeyTip = "DB" }))
            .Group("ChartFormatAxesGroup", "Axes", null, priority: 150,
                g => g
                .Button("X Axis Bounds", "X Axis Bounds", b => b with { PreferredLayout = RibbonCommandLayoutKind.Large, Icon = new RibbonCommandIcon(RibbonCommandIconKind.ChartLine), KeyTip = "XB" })
                .Button("Y Axis Bounds", "Y Axis Bounds", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.ChartLine), KeyTip = "YB" })
                .Button("X Axis Gridlines", "X Axis Gridlines", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Grid), KeyTip = "XG" })
                .Button("Y Axis Gridlines", "Y Axis Gridlines", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Grid), KeyTip = "YG" })
                .Button("X Axis Labels", "X Axis Labels", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.ChartLine), KeyTip = "XA" })
                .Button("Y Axis Labels", "Y Axis Labels", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.ChartLine), KeyTip = "YA" }))
        )
        .ContextualTab("PictureFormatTab", "Picture Format", new RibbonTabContext("picture.selected", "Picture Format", RibbonContextColor.Teal), tab => tab
            .Group("PictureFormatFormatGroup", "Format", null, priority: 180,
                g => g
                .Button("Format Picture", "Format Picture", b => b with { PreferredLayout = RibbonCommandLayoutKind.Large, Icon = new RibbonCommandIcon(RibbonCommandIconKind.Picture), KeyTip = "FP" })
                .Button("Crop Picture", "Crop Picture", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Picture), KeyTip = "C" }))
            .Group("PictureFormatArrangeGroup", "Arrange", null, priority: 170,
                g => g
                .Button("Bring Forward", "Bring Forward", b => b with { PreferredLayout = RibbonCommandLayoutKind.Large, Icon = new RibbonCommandIcon(RibbonCommandIconKind.BringForward), KeyTip = "BF" })
                .Button("Send Backward", "Send Backward", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.SendBackward), KeyTip = "SB" })
                .Button("Selection Pane", "Selection Pane", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.List), KeyTip = "SP" })
                .Button("Rotate Object", "Rotate Object", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Rotate), KeyTip = "RO" })
                .Button("Object Size", "Object Size", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Size), KeyTip = "SZ" }))
            .Group("PictureFormatAccessibilityGroup", "Accessibility", null, priority: 160,
                g => g
                .Button("Alt Text", "Alt Text", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Label), KeyTip = "AT" }))
        )
        .ContextualTab("ShapeFormatTab", "Shape Format", new RibbonTabContext("shape.selected", "Shape Format", RibbonContextColor.Purple), tab => tab
            .Group("ShapeFormatShapeStylesGroup", "Shape Styles", null, priority: 180,
                g => g
                .Button("Shape Fill", "Shape Fill", b => b with { PreferredLayout = RibbonCommandLayoutKind.Large, Icon = new RibbonCommandIcon(RibbonCommandIconKind.RibbonShape), KeyTip = "F" })
                .Button("Object Outline", "Object Outline", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Border), KeyTip = "O" })
                .Button("Shape Gradient", "Shape Gradient", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.RibbonShape), KeyTip = "G" })
                .Button("Shape Effects", "Shape Effects", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.RibbonShape), KeyTip = "E" }))
            .Group("ShapeFormatArrangeGroup", "Arrange", null, priority: 170,
                g => g
                .Button("Bring Forward", "Bring Forward", b => b with { PreferredLayout = RibbonCommandLayoutKind.Large, Icon = new RibbonCommandIcon(RibbonCommandIconKind.BringForward), KeyTip = "BF" })
                .Button("Send Backward", "Send Backward", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.SendBackward), KeyTip = "SB" })
                .Button("Selection Pane", "Selection Pane", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.List), KeyTip = "SP" })
                .Button("Rotate Object", "Rotate Object", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Rotate), KeyTip = "RO" })
                .Button("Object Size", "Object Size", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Size), KeyTip = "SZ" }))
            .Group("ShapeFormatAccessibilityGroup", "Accessibility", null, priority: 160,
                g => g
                .Button("Alt Text", "Alt Text", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Label), KeyTip = "AT" }))
        )
        .ContextualTab("TableDesignTab", "Table Design", new RibbonTabContext("table.active", "Table Design", RibbonContextColor.Blue), tab => tab
            .Group("TableDesignPropertiesGroup", "Properties", null, priority: 180,
                g => g
                .Button("Table Name", "Table Name", b => b with { PreferredLayout = RibbonCommandLayoutKind.Large, Icon = new RibbonCommandIcon(RibbonCommandIconKind.Generic), KeyTip = "N" })
                .Button("Resize Table", "Resize Table", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Scale), KeyTip = "Z" }))
            .Group("TableDesignToolsGroup", "Tools", null, priority: 170,
                g => g
                .Button("Summarize with PivotTable", "Summarize with PivotTable", b => b with { PreferredLayout = RibbonCommandLayoutKind.Large, Icon = new RibbonCommandIcon(RibbonCommandIconKind.PivotTable), KeyTip = "S" })
                .Button("Remove Duplicates", "Remove Duplicates", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Delete), KeyTip = "D" })
                .Button("Convert to Range", "Convert to Range", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Refresh), KeyTip = "V" }))
            .Group("TableDesignStyleOptionsGroup", "Style Options", null, priority: 160,
                g => g
                .CheckBox("Total Row", "Total Row", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Sum), KeyTip = "T" })
                .CheckBox("First Column", "First Column", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Table), KeyTip = "FC" })
                .CheckBox("Last Column", "Last Column", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Table), KeyTip = "L" })
                .CheckBox("Banded Rows", "Banded Rows", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Table), KeyTip = "B" })
                .CheckBox("Banded Columns", "Banded Columns", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Table), KeyTip = "C" })
                .CheckBox("Filter Button", "Filter Button", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Filter), KeyTip = "A" }))
            .Group("TableDesignStylesGroup", "Styles", null, priority: 150,
                g => g
                .Button("Table Styles", "Table Styles", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Theme), KeyTip = "Y" }))
        )
        .ContextualTab("PivotTableAnalyzeTab", "PivotTable Analyze", new RibbonTabContext("pivot.active", "PivotTable Analyze", RibbonContextColor.Orange), tab => tab
            .Group("PivotTableAnalyzePivotTableGroup", "Pivot Table", null, priority: 180,
                g => g
                .Button("PivotTable Name", "PivotTable Name", b => b with { PreferredLayout = RibbonCommandLayoutKind.Large, Icon = new RibbonCommandIcon(RibbonCommandIconKind.PivotTable), KeyTip = "N" })
                .Button("PivotTable Options", "PivotTable Options", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.PivotTable), KeyTip = "O" }))
            .Group("PivotTableAnalyzeActiveFieldGroup", "Active Field", null, priority: 170,
                g => g
                .Button("Show Details", "Show Details", b => b with { PreferredLayout = RibbonCommandLayoutKind.Large, Icon = new RibbonCommandIcon(RibbonCommandIconKind.List), KeyTip = "D" })
                .Button("Field Settings", "Field Settings", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Label), KeyTip = "FS" }))
            .Group("PivotTableAnalyzeGroupGroup", "Group", null, priority: 160,
                g => g
                .Button("Group Field", "Group Field", b => b with { PreferredLayout = RibbonCommandLayoutKind.Large, Icon = new RibbonCommandIcon(RibbonCommandIconKind.Group), KeyTip = "GF" })
                .Button("Ungroup", "Ungroup", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Group), KeyTip = "UG" }))
            .Group("PivotTableAnalyzeFilterGroup", "Filter", null, priority: 150,
                g => g
                .Button("Insert Slicer", "Insert Slicer", b => b with { PreferredLayout = RibbonCommandLayoutKind.Large, Icon = new RibbonCommandIcon(RibbonCommandIconKind.Filter), KeyTip = "IS" })
                .Button("Insert Timeline", "Insert Timeline", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Date), KeyTip = "IT" }))
            .Group("PivotTableAnalyzeDataGroup", "Data", null, priority: 140,
                g => g
                .Button("Refresh", "Refresh", b => b with { PreferredLayout = RibbonCommandLayoutKind.Large, Icon = new RibbonCommandIcon(RibbonCommandIconKind.Refresh), KeyTip = "R" })
                .Button("Change Data Source", "Change Data Source", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Generic), KeyTip = "CD" }))
            .Group("PivotTableAnalyzeActionsGroup", "Actions", null, priority: 130,
                g => g
                .Button("Clear", "Clear", b => b with { PreferredLayout = RibbonCommandLayoutKind.Large, Icon = new RibbonCommandIcon(RibbonCommandIconKind.Clear), KeyTip = "CL" })
                .Button("Select", "Select", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Search), KeyTip = "SE" })
                .Button("Move PivotTable", "Move PivotTable", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.PivotTable), KeyTip = "M" }))
            .Group("PivotTableAnalyzeCalculationsGroup", "Calculations", null, priority: 120,
                g => g
                .Button("Calculated Field", "Calculated Field", b => b with { PreferredLayout = RibbonCommandLayoutKind.Large, Icon = new RibbonCommandIcon(RibbonCommandIconKind.Refresh), KeyTip = "CF" })
                .Button("Calculated Item", "Calculated Item", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Refresh), KeyTip = "CI" }))
            .Group("PivotTableAnalyzeToolsGroup", "Tools", null, priority: 110,
                g => g
                .Button("PivotChart", "PivotChart", b => b with { PreferredLayout = RibbonCommandLayoutKind.Large, Icon = new RibbonCommandIcon(RibbonCommandIconKind.ChartColumn), KeyTip = "PC" })
                .Button("Change Chart Type", "Change Chart Type", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.ChartColumn), KeyTip = "CT" })
                .Button("PivotChart Options", "PivotChart Options", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.ChartColumn), KeyTip = "CO" }))
            .Group("PivotTableAnalyzeShowGroup", "Show", null, priority: 100,
                g => g
                .Button("Field List", "Field List", b => b with { PreferredLayout = RibbonCommandLayoutKind.Large, Icon = new RibbonCommandIcon(RibbonCommandIconKind.Label), KeyTip = "FL" })
                .Button("+/- Buttons", "+/- Buttons", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Generic), KeyTip = "PB" })
                .Button("Field Headers", "Field Headers", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.HeaderFooter), KeyTip = "FH" }))
        )
        .ContextualTab("PivotTableDesignTab", "PivotTable Design", new RibbonTabContext("pivot.active", "PivotTable Design", RibbonContextColor.Orange), tab => tab
            .Group("PivotTableDesignLayoutGroup", "Layout", null, priority: 180,
                g => g
                .Button("Grand Totals", "Grand Totals", b => b with { PreferredLayout = RibbonCommandLayoutKind.Large, Icon = new RibbonCommandIcon(RibbonCommandIconKind.Sum), KeyTip = "G" })
                .Button("Subtotals", "Subtotals", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Sum), KeyTip = "S" })
                .Button("Report Layout", "Report Layout", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.List), KeyTip = "L" })
                .Button("Blank Rows", "Blank Rows", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.List), KeyTip = "B" }))
            .Group("PivotTableDesignStyleOptionsGroup", "Style Options", null, priority: 170,
                g => g
                .Button("Banded Rows", "Banded Rows", b => b with { PreferredLayout = RibbonCommandLayoutKind.Large, Icon = new RibbonCommandIcon(RibbonCommandIconKind.Table), KeyTip = "R" })
                .Button("Banded Columns", "Banded Columns", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Table), KeyTip = "C" })
                .Button("Row Headers", "Row Headers", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.HeaderFooter), KeyTip = "H" })
                .Button("Column Headers", "Column Headers", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.HeaderFooter), KeyTip = "O" }))
            .Group("PivotTableDesignStylesGroup", "Styles", null, priority: 160,
                g => g
                .Button("PivotTable Styles", "PivotTable Styles", b => b with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.PivotTable), KeyTip = "Y" }))
        )
        .Build();
}
