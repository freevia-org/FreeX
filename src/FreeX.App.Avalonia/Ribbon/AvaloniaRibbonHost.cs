using Avalonia.Controls;
using Free.Shared.Ribbon;
using FreeX.App.Avalonia.Charts;
using FreeX.App.Services;
using FreeX.App.Services.Ribbon;
using FreeX.Core.Model;
using FreeX.Ribbon.Avalonia;

namespace FreeX.App.Avalonia.Ribbon;

/// <summary>
/// Builds the FreeX Avalonia ribbon: a representative multi-tab <see cref="RibbonDefinition"/>
/// (the Avalonia app cannot reference the WPF host that owns the real definition) rendered via
/// <see cref="AvaloniaRibbonRenderer"/>. Most controls route to no-op commands, but the Bold / Italic /
/// Underline toggles bind to the shared, platform-neutral <see cref="WorkbookFormatRibbonCommands"/> —
/// the same command logic the WPF host uses — so clicking them formats the live selection.
/// </summary>
internal static class AvaloniaRibbonHost
{
    /// <summary>Builds the ribbon control to dock at the top of the main window.</summary>
    /// <param name="session">Accessor for the live workbook session the format commands act on.</param>
    /// <param name="setStatus">Host refresh hook (redraws the grid and reports a status line).</param>
    public static Control Build(Func<WorkbookSession?> session, Action<string> setStatus)
        => Build(session, setStatus, new AvaloniaRibbonHostCallbacks());

    /// <summary>
    /// Builds the ribbon control, wiring all host callbacks and an optional
    /// <paramref name="contextSource"/> so contextual tabs (Chart/Picture/Shape/Table/Pivot) appear and
    /// disappear with the selection. A null source falls back to the static tab strip.
    /// </summary>
    public static Control Build(
        Func<WorkbookSession?> session,
        Action<string> setStatus,
        AvaloniaRibbonHostCallbacks callbacks,
        IRibbonContextSource? contextSource)
    {
        var registry = SampleRibbon.BuildRegistry(session, setStatus, callbacks);
        var definition = SampleRibbon.BuildDefinition();
        return AvaloniaRibbonRenderer.BuildRibbon(definition, registry, contextSource);
    }

    /// <summary>
    /// Builds the ribbon control, additionally wiring the Data-tab <c>Text to Columns</c> and
    /// <c>Consolidate</c> buttons to host callbacks that open the corresponding dialogs. A null callback
    /// leaves its button on the no-op registration (so the smoke harness can still build the ribbon).
    /// </summary>
    public static Control Build(
        Func<WorkbookSession?> session,
        Action<string> setStatus,
        Action? openTextToColumns,
        Action? openConsolidate)
        => Build(session, setStatus, new AvaloniaRibbonHostCallbacks
        {
            OpenTextToColumns = openTextToColumns,
            OpenConsolidate = openConsolidate,
        });

    /// <summary>
    /// Builds the ribbon control, wiring every host dialog/action the shell exposes through
    /// <paramref name="callbacks"/>. Each non-null callback overrides its control's no-op registration with
    /// a <see cref="RelayRibbonCommand"/>; null callbacks (e.g. in the smoke harness) leave the no-op.
    /// </summary>
    public static Control Build(
        Func<WorkbookSession?> session,
        Action<string> setStatus,
        AvaloniaRibbonHostCallbacks callbacks)
    {
        var registry = SampleRibbon.BuildRegistry(session, setStatus, callbacks);
        var definition = SampleRibbon.BuildDefinition();
        return AvaloniaRibbonRenderer.BuildRibbon(definition, registry);
    }
}

/// <summary>
/// Host-supplied actions the Avalonia ribbon binds to. Each maps one (or more) ribbon command id(s) to a
/// shell handler — opening a dialog or running a selection command. Kept as a record of nullable
/// <see cref="Action"/>s so the smoke harness (and tests) can build the ribbon with none of them wired.
/// </summary>
internal sealed record AvaloniaRibbonHostCallbacks
{
    /// <summary>Data ▸ Text to Columns.</summary>
    public Action? OpenTextToColumns { get; init; }

    /// <summary>Data ▸ Consolidate.</summary>
    public Action? OpenConsolidate { get; init; }

    /// <summary>Insert ▸ Table and Home ▸ Format as Table — create a structured table from the selection.</summary>
    public Action? InsertTable { get; init; }

    /// <summary>Home ▸ Conditional — open the conditional-format New Rule editor.</summary>
    public Action? ConditionalFormatting { get; init; }

    /// <summary>Data ▸ Quick Analysis — open the Quick Analysis popup for the selection.</summary>
    public Action? QuickAnalysis { get; init; }

    /// <summary>Insert ▸ PivotTable — open the Insert PivotTable dialog for the selection.</summary>
    public Action? InsertPivotTable { get; init; }

    /// <summary>Insert ▸ Picture — choose an image file and place it on the sheet.</summary>
    public Action? InsertPicture { get; init; }

    /// <summary>Insert ▸ Shapes — insert the default drawing shape at the active cell.</summary>
    public Action? InsertShape { get; init; }

    /// <summary>Insert ▸ Text Box — insert a text box at the active cell.</summary>
    public Action? InsertTextBox { get; init; }

    /// <summary>Home ▸ Clipboard ▸ Format Painter — capture the selection's format for a one-shot apply.</summary>
    public Action? FormatPainter { get; init; }

    /// <summary>Home ▸ Font ▸ Size combo — apply the chosen font size (string) to the selection.</summary>
    public Action<string?>? SetFontSize { get; init; }

    /// <summary>Home ▸ Font ▸ Name combo — apply the chosen font family (string) to the selection.</summary>
    public Action<string?>? SetFontName { get; init; }

    /// <summary>Data ▸ Sort A-Z.</summary>
    public Action? SortAscending { get; init; }

    /// <summary>Data ▸ Sort Z-A.</summary>
    public Action? SortDescending { get; init; }

    /// <summary>Data ▸ Filter — toggle the sheet AutoFilter over the selection / current region.</summary>
    public Action? ToggleFilter { get; init; }

    /// <summary>Data ▸ Data Validation (dropdown + dialog menu item).</summary>
    public Action? DataValidation { get; init; }

    /// <summary>Home ▸ Clipboard ▸ Cut.</summary>
    public Action? Cut { get; init; }

    /// <summary>Home ▸ Clipboard ▸ Copy.</summary>
    public Action? Copy { get; init; }

    /// <summary>Home ▸ Clipboard ▸ Paste (split-button primary + Paste menu item).</summary>
    public Action? Paste { get; init; }

    /// <summary>Home ▸ Alignment ▸ Align Left.</summary>
    public Action? AlignLeft { get; init; }

    /// <summary>Home ▸ Alignment ▸ Center.</summary>
    public Action? AlignCenter { get; init; }

    /// <summary>Home ▸ Alignment ▸ Align Right.</summary>
    public Action? AlignRight { get; init; }

    /// <summary>Home ▸ Alignment ▸ Wrap Text.</summary>
    public Action? WrapText { get; init; }

    /// <summary>Home ▸ Alignment ▸ Merge &amp; Center (split-button primary + menu item).</summary>
    public Action? MergeAndCenter { get; init; }

    /// <summary>Home ▸ Number ▸ Currency.</summary>
    public Action? CurrencyFormat { get; init; }

    /// <summary>Home ▸ Number ▸ Percent.</summary>
    public Action? PercentFormat { get; init; }

    /// <summary>Home ▸ Number ▸ Comma.</summary>
    public Action? CommaStyle { get; init; }

    /// <summary>
    /// Additional command-id → action bindings for parameterized menu items the named callbacks do not
    /// cover (e.g. the Number Format dropdown's General/Number/Currency/Date/Percent items, or the Fill
    /// Color dropdown's swatch items). Applied after the named callbacks; each id overrides its no-op.
    /// </summary>
    public IReadOnlyDictionary<string, Action>? ExtraCommands { get; init; }
}

/// <summary>An <see cref="IRibbonCommand"/> that invokes a host-supplied callback (e.g. opens a dialog).</summary>
internal sealed class RelayRibbonCommand : IRibbonCommand
{
    private readonly Action _execute;

    public RelayRibbonCommand(Action execute)
        => _execute = execute ?? throw new ArgumentNullException(nameof(execute));

    public void Execute(RibbonCommandContext context) => _execute();
}

/// <summary>
/// An <see cref="IRibbonCommand"/> for value-bearing controls (combo boxes / galleries): invokes a
/// host-supplied callback with the control's selected value (<see cref="RibbonCommandContext.SelectedValue"/>).
/// </summary>
internal sealed class RelayValueRibbonCommand : IRibbonCommand
{
    private readonly Action<string?> _execute;

    public RelayValueRibbonCommand(Action<string?> execute)
        => _execute = execute ?? throw new ArgumentNullException(nameof(execute));

    public void Execute(RibbonCommandContext context) => _execute(context.SelectedValue);
}

/// <summary>A do-nothing command — enough to mark a control as registered/enabled.</summary>
internal sealed class NoOpRibbonCommand : IRibbonCommand
{
    public static readonly NoOpRibbonCommand Instance = new();

    public void Execute(RibbonCommandContext context)
    {
        // Intentionally empty: the Avalonia shell wires real behavior elsewhere.
    }
}

/// <summary>
/// Inserts a chart of a fixed <see cref="ChartType"/> over the live session's selection by running the
/// shared Core <see cref="FreeX.Core.Commands.AddChartCommand"/> (built by
/// <see cref="InsertChartCommandFactory"/>). On success the host refresh hook redraws the grid so the new
/// chart paints in the drawing-object overlay; on failure the Core guard message is surfaced on the
/// status bar. The session is read each time (it may be replaced on open/new).
/// </summary>
internal sealed class InsertChartRibbonCommand : IRibbonCommand
{
    private readonly Func<WorkbookSession?> _session;
    private readonly ChartType _chartType;
    private readonly Action<string> _setStatus;

    public InsertChartRibbonCommand(Func<WorkbookSession?> session, ChartType chartType, Action<string> setStatus)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _chartType = chartType;
        _setStatus = setStatus ?? throw new ArgumentNullException(nameof(setStatus));
    }

    public void Execute(RibbonCommandContext context)
    {
        if (_session() is not { } session)
            return;

        var command = InsertChartCommandFactory.Build(session.ActiveSheet.Id, session.SelectedRange, _chartType);
        var result = session.ExecuteReviewCommand(command);
        _setStatus(result.Success
            ? $"Inserted {_chartType} chart"
            : result.ErrorMessage ?? "Insert Chart failed.");
    }
}

/// <summary>
/// A representative Home-like ribbon plus a few more tabs, exercising large/medium/small controls,
/// a dropdown with menu items, a split button, separators, and a combo box.
/// </summary>
internal static class SampleRibbon
{
    public static RibbonDefinition BuildDefinition()
    {
        return new RibbonDefinitionBuilder()
            .Tab("home", "Home", "H", home =>
            {
                home.Group("clipboard", "Clipboard", "C", 100, g =>
                {
                    // WPF: Paste large (with menu); Cut/Copy/Format Painter medium (icon + label).
                    g.SplitButton("home.paste", "Paste", PasteMenu(), c => c with
                    {
                        PreferredLayout = RibbonCommandLayoutKind.Large,
                        Icon = new RibbonCommandIcon(RibbonCommandIconKind.Paste),
                    });
                    g.Medium("home.cut", "Cut", RibbonCommandIconKind.Cut, "X");
                    g.Medium("home.copy", "Copy", RibbonCommandIconKind.Copy, "C");
                    g.Medium("home.formatPainter", "Format Painter", RibbonCommandIconKind.FormatPainter, "FP");
                });

                home.Group("font", "Font", "F", 90, g =>
                {
                    // WPF Font group: row 1 = Font/Size combos + Increase/Decrease Font Size (icon-only),
                    // RowBreak, row 2 = Bold/Italic/Underline/Strikethrough (icon-only toggles), separator,
                    // Borders/Fill Color/Font Color (icon-only dropdowns).
                    g.ComboBox("home.fontName", "Font", c => c with
                    {
                        Width = 120,
                        Items = new[] { "Calibri", "Arial", "Times New Roman", "Consolas" },
                    });
                    g.ComboBox("home.fontSize", "Size", c => c with
                    {
                        Width = 44,
                        Items = new[] { "8", "9", "10", "11", "12", "14", "16", "18", "24" },
                    });
                    g.Icon("home.increaseFont", "Increase Font Size", RibbonCommandIconKind.Font, "FG");
                    g.Icon("home.decreaseFont", "Decrease Font Size", RibbonCommandIconKind.Font, "FK");
                    g.RowBreak();
                    g.IconToggle("home.bold", "Bold", RibbonCommandIconKind.Bold, "1");
                    g.IconToggle("home.italic", "Italic", RibbonCommandIconKind.Italic, "2");
                    g.IconToggle("home.underline", "Underline", RibbonCommandIconKind.Underline, "3");
                    g.IconToggle("home.strikethrough", "Strikethrough", RibbonCommandIconKind.Strikethrough, "4");
                    g.Separator();
                    g.Dropdown("home.borders", "Borders", BordersMenu(), c => c with
                    {
                        PreferredLayout = RibbonCommandLayoutKind.Small,
                        Icon = new RibbonCommandIcon(RibbonCommandIconKind.Border),
                    });
                    g.Dropdown("home.fillColor", "Fill Color", FillColorMenu(), c => c with
                    {
                        PreferredLayout = RibbonCommandLayoutKind.Small,
                        Icon = new RibbonCommandIcon(RibbonCommandIconKind.Fill),
                    });
                    g.Dropdown("home.fontColor", "Font Color", FontColorMenu(), c => c with
                    {
                        PreferredLayout = RibbonCommandLayoutKind.Small,
                        Icon = new RibbonCommandIcon(RibbonCommandIconKind.Color),
                    });
                });

                home.Group("alignment", "Alignment", "A", 80, g =>
                {
                    // WPF Alignment group: row 1 = vertical aligns (icon-only toggles), separator, Orientation
                    // (icon-only), Wrap Text (medium); RowBreak; row 2 = horizontal aligns (icon-only toggles),
                    // separator, Decrease/Increase Indent (icon-only), Merge & Center (medium, with menu).
                    g.IconToggle("home.alignTop", "Top Align", RibbonCommandIconKind.Align, "AT");
                    g.IconToggle("home.alignMiddle", "Middle Align", RibbonCommandIconKind.Align, "AM");
                    g.IconToggle("home.alignBottom", "Bottom Align", RibbonCommandIconKind.Align, "AB");
                    g.Separator();
                    g.Icon("home.orientation", "Orientation", RibbonCommandIconKind.Orientation, "RO");
                    g.Medium("home.wrapText", "Wrap Text", RibbonCommandIconKind.Wrap, "W");
                    g.RowBreak();
                    g.IconToggle("home.alignLeft", "Align Left", RibbonCommandIconKind.Align, "AL");
                    g.IconToggle("home.alignCenter", "Center", RibbonCommandIconKind.Align, "AC");
                    g.IconToggle("home.alignRight", "Align Right", RibbonCommandIconKind.Align, "AR");
                    g.Separator();
                    g.Icon("home.decreaseIndent", "Decrease Indent", RibbonCommandIconKind.Align, "AO");
                    g.Icon("home.increaseIndent", "Increase Indent", RibbonCommandIconKind.Align, "AI");
                    g.SplitButton("home.merge", "Merge & Center", MergeMenu(), c => c with
                    {
                        PreferredLayout = RibbonCommandLayoutKind.Medium,
                        Icon = new RibbonCommandIcon(RibbonCommandIconKind.Merge),
                    });
                });

                home.Group("number", "Number", "N", 70, g =>
                {
                    // WPF Number group: row 1 = Number Format selector; RowBreak; row 2 = Accounting/Percent/
                    // Comma (icon-only), separator, Increase/Decrease Decimal (icon-only). The Number Format
                    // selector keeps its dropdown menu (callbacks bind the fmt* menu ids) and reads as a
                    // labeled (medium) format picker on the top row.
                    g.Dropdown("home.numberFormat", "Number Format", NumberFormatMenu(), c => c with
                    {
                        PreferredLayout = RibbonCommandLayoutKind.Medium,
                        Icon = new RibbonCommandIcon(RibbonCommandIconKind.Number),
                    });
                    g.RowBreak();
                    g.Icon("home.accounting", "Accounting", RibbonCommandIconKind.Currency, "AN");
                    g.Icon("home.currency", "Currency", RibbonCommandIconKind.Currency, "CY");
                    g.Icon("home.percent", "Percent", RibbonCommandIconKind.Percent, "P");
                    g.Icon("home.comma", "Comma", RibbonCommandIconKind.Comma, "K");
                    g.Separator();
                    g.Icon("home.increaseDecimal", "Increase Decimal", RibbonCommandIconKind.Decimal, "QI");
                    g.Icon("home.decreaseDecimal", "Decrease Decimal", RibbonCommandIconKind.Decimal, "QD");
                });

                home.Group("styles", "Styles", "S", 60, g =>
                {
                    // WPF Styles group: Conditional Formatting / Format as Table / Cell Styles are all large.
                    g.Large("home.conditional", "Conditional", RibbonCommandIconKind.Color, "L");
                    g.Large("home.formatAsTable", "Format as Table", RibbonCommandIconKind.Table, "T");
                    g.Large("home.cellStyles", "Cell Styles", RibbonCommandIconKind.Theme, "J");
                });

                home.Group("cells", "Cells", "E", 50, g =>
                {
                    // WPF Cells group: Insert / Delete / Format are medium (icon + label).
                    g.Medium("home.insertCells", "Insert", RibbonCommandIconKind.Insert, "I");
                    g.Medium("home.deleteCells", "Delete", RibbonCommandIconKind.Delete, "D");
                    g.Medium("home.formatCells", "Format", RibbonCommandIconKind.Size, "O");
                });

                home.Group("editing", "Editing", "G", 40, g =>
                {
                    // WPF Editing group: AutoSum / Fill / Clear / Find & Select are medium (icon + label).
                    g.Medium("home.autoSum", "AutoSum", RibbonCommandIconKind.Sum, "U");
                    g.Medium("home.fillDown", "Fill", RibbonCommandIconKind.Fill, "FI");
                    g.Medium("home.clear", "Clear", RibbonCommandIconKind.Clear, "E");
                    g.Medium("home.findSelect", "Find & Select", RibbonCommandIconKind.Search, "FD");
                });
            })
            .Tab("insert", "Insert", "I", insert =>
            {
                // WPF Insert tab: PivotTable/Table large; chart types medium; illustrations/links/comments/
                // text/symbols large; sparklines medium.
                insert.Group("tables", "Tables", "T", 100, g =>
                {
                    g.Large("insert.pivotTable", "PivotTable", RibbonCommandIconKind.PivotTable, "PT");
                    g.Medium("insert.pivotChart", "PivotChart", RibbonCommandIconKind.ChartColumn, "PC");
                    g.Large("insert.table", "Table", RibbonCommandIconKind.Table, "TB");
                });

                insert.Group("charts", "Charts", "C", 90, g =>
                {
                    g.Dropdown("insert.column", "Column", ChartTypeMenu(), c => c with
                    {
                        PreferredLayout = RibbonCommandLayoutKind.Medium,
                        Icon = new RibbonCommandIcon(RibbonCommandIconKind.ChartColumn),
                    });
                    g.Medium("insert.line", "Line", RibbonCommandIconKind.ChartLine, "LC");
                    g.Medium("insert.pie", "Pie", RibbonCommandIconKind.ChartPie, "PY");
                    g.Medium("insert.scatter", "Scatter", RibbonCommandIconKind.ChartScatter, "SX");
                });

                insert.Group("illustrations", "Illustrations", "L", 80, g =>
                {
                    g.Large("insert.picture", "Picture", RibbonCommandIconKind.Picture, "IP");
                    g.Large("insert.shapes", "Shapes", RibbonCommandIconKind.Rectangle, "SH");
                    g.Large("insert.textBox", "Text Box", RibbonCommandIconKind.TextBox, "TX");
                });

                insert.Group("sparklines", "Sparklines", "S", 70, g =>
                {
                    g.Medium("insert.sparklineLine", "Line", RibbonCommandIconKind.Sparkline, "SL");
                    g.Medium("insert.sparklineColumn", "Column", RibbonCommandIconKind.ChartColumn, "SK");
                    g.Medium("insert.sparklineWinLoss", "Win/Loss", RibbonCommandIconKind.Sparkline, "SW");
                });

                insert.Group("filters", "Filters", "F", 60, g =>
                {
                    g.Medium("insert.slicer", "Slicer", RibbonCommandIconKind.Filter, "SR");
                    g.Large("insert.timeline", "Timeline", RibbonCommandIconKind.Date, "IT");
                });

                insert.Group("links", "Links", "I", 50, g =>
                {
                    g.Large("insert.hyperlink", "Link", RibbonCommandIconKind.Link, "K");
                });

                insert.Group("comments", "Comments", "C", 40, g =>
                {
                    g.Large("insert.comment", "Comment", RibbonCommandIconKind.Comment, "C2");
                });

                insert.Group("text", "Text", "X", 30, g =>
                {
                    g.Large("insert.headerFooter", "Header & Footer", RibbonCommandIconKind.HeaderFooter, "HF");
                    g.Large("insert.object", "Object", RibbonCommandIconKind.Insert, "OB");
                });

                insert.Group("symbols", "Symbols", "Y", 20, g =>
                {
                    g.Large("insert.equation", "Equation", RibbonCommandIconKind.Function, "EQ");
                    g.Large("insert.symbol", "Symbol", RibbonCommandIconKind.Symbol, "SY");
                });
            })
            .Tab("data", "Data", "D", data =>
            {
                data.Group("getData", "Get Data", "G", 100, g =>
                {
                    g.Button("data.getData", "Get Data", c => c with
                    {
                        PreferredLayout = RibbonCommandLayoutKind.Large,
                        Icon = new RibbonCommandIcon(RibbonCommandIconKind.GetData),
                    });
                    g.Button("data.refresh", "Refresh All", c => c with
                    {
                        Icon = new RibbonCommandIcon(RibbonCommandIconKind.Refresh),
                    });
                });

                data.Group("sortFilter", "Sort & Filter", "S", 90, g =>
                {
                    g.Button("data.sortAsc", "Sort A-Z", c => c with
                    {
                        Icon = new RibbonCommandIcon(RibbonCommandIconKind.SortAscending),
                    });
                    g.Button("data.sortDesc", "Sort Z-A", c => c with
                    {
                        Icon = new RibbonCommandIcon(RibbonCommandIconKind.SortDescending),
                    });
                    g.Toggle("data.filter", "Filter", c => c with
                    {
                        Icon = new RibbonCommandIcon(RibbonCommandIconKind.Filter),
                    });
                    g.Button("data.reapply", "Reapply", c => c with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Refresh) });
                    g.Button("data.advancedFilter", "Advanced", c => c with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Filter) });
                });

                data.Group("tools", "Data Tools", "O", 80, g =>
                {
                    g.Button("data.flashFill", "Flash Fill", c => c with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Flash) });
                    g.Button("data.removeDuplicates", "Remove Duplicates", c => c with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Delete) });
                    g.Dropdown("data.validation", "Data Validation", ValidationMenu(), c => c with
                    {
                        Icon = new RibbonCommandIcon(RibbonCommandIconKind.Logical),
                    });
                    g.Button("data.textToColumns", "Text to Columns", c => c with
                    {
                        Icon = new RibbonCommandIcon(RibbonCommandIconKind.TextColumns),
                    });
                    g.Button("data.consolidate", "Consolidate", c => c with
                    {
                        Icon = new RibbonCommandIcon(RibbonCommandIconKind.Consolidate),
                    });
                    g.Button("data.quickAnalysis", "Quick Analysis", c => c with
                    {
                        Icon = new RibbonCommandIcon(RibbonCommandIconKind.Logical),
                    });
                });

                data.Group("forecast", "Forecast", "C", 70, g =>
                {
                    g.Button("data.whatIf", "What-If Analysis", c => c with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Logical) });
                    g.Button("data.forecastSheet", "Forecast Sheet", c => c with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.ChartLine) });
                });

                data.Group("outline", "Outline", "U", 60, g =>
                {
                    g.Button("data.group", "Group", c => c with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Group) });
                    g.Button("data.ungroup", "Ungroup", c => c with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Ungroup) });
                    g.Button("data.subtotal", "Subtotal", c => c with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Sum) });
                });
            })
            .Tab("pageLayout", "Page Layout", "P", page =>
            {
                page.Group("pageLayoutThemes", "Themes", "T", 100, g =>
                {
                    g.Button("pageLayout.themes", "Themes", c => c with
                    {
                        PreferredLayout = RibbonCommandLayoutKind.Large,
                        Icon = new RibbonCommandIcon(RibbonCommandIconKind.Theme),
                    });
                    g.Button("pageLayout.themeColors", "Colors", c => c with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Color) });
                    g.Button("pageLayout.themeFonts", "Fonts", c => c with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Font) });
                    g.Button("pageLayout.themeEffects", "Effects", c => c with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Effects) });
                });
                page.Group("pageLayoutPageSetup", "Page Setup", "S", 90, g =>
                {
                    g.Button("pageLayout.margins", "Margins", c => c with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Margins) });
                    g.Button("pageLayout.orientation", "Orientation", c => c with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Orientation) });
                    g.Button("pageLayout.size", "Size", c => c with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Size) });
                    g.Button("pageLayout.printArea", "Print Area", c => c with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Print) });
                    g.Button("pageLayout.breaks", "Breaks", c => c with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.PageBreak) });
                    g.Button("pageLayout.printTitles", "Print Titles", c => c with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.HeaderFooter) });
                    g.Button("pageLayout.background", "Background", c => c with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Picture) });
                });
                page.Group("pageLayoutScaleToFit", "Scale to Fit", "F", 80, g =>
                {
                    g.Button("pageLayout.width", "Width", c => c with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Scale) });
                    g.Button("pageLayout.height", "Height", c => c with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Scale) });
                    g.Button("pageLayout.scale", "Scale", c => c with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Scale) });
                });
                page.Group("pageLayoutSheetOptions", "Sheet Options", "O", 70, g =>
                {
                    g.Toggle("pageLayout.gridlines", "Gridlines", c => c with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Grid) });
                    g.Toggle("pageLayout.headings", "Headings", c => c with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Grid) });
                });
            })
            .Tab("formulas", "Formulas", "M", formulas =>
            {
                formulas.Group("formulasFunctionLibrary", "Function Library", "F", 100, g =>
                {
                    g.Button("formulas.insertFunction", "Insert Function", c => c with
                    {
                        PreferredLayout = RibbonCommandLayoutKind.Large,
                        Icon = new RibbonCommandIcon(RibbonCommandIconKind.Function),
                    });
                    g.Button("formulas.autoSum", "AutoSum", c => c with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Sum) });
                    g.Button("formulas.financial", "Financial", c => c with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Financial) });
                    g.Button("formulas.logical", "Logical", c => c with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Logical) });
                    g.Button("formulas.text", "Text", c => c with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.TextFunction) });
                    g.Button("formulas.dateTime", "Date & Time", c => c with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Date) });
                    g.Button("formulas.lookupReference", "Lookup & Reference", c => c with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Search) });
                    g.Button("formulas.mathTrig", "Math & Trig", c => c with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Math) });
                    g.Button("formulas.moreFunctions", "More Functions", c => c with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.More) });
                    g.Button("formulas.recentlyUsed", "Recently Used", c => c with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Recent) });
                });
                formulas.Group("formulasDefinedNames", "Defined Names", "N", 90, g =>
                {
                    g.Button("formulas.nameManager", "Name Manager", c => c with
                    {
                        PreferredLayout = RibbonCommandLayoutKind.Large,
                        Icon = new RibbonCommandIcon(RibbonCommandIconKind.Label),
                    });
                    g.Button("formulas.defineName", "Define Name", c => c with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Label) });
                    g.Button("formulas.createFromSelection", "Create from Selection", c => c with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Label) });
                });
                formulas.Group("formulasFormulaAuditing", "Formula Auditing", "U", 85, g =>
                {
                    g.Button("formulas.tracePrecedents", "Trace Precedents", c => c with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Search) });
                    g.Button("formulas.traceDependents", "Trace Dependents", c => c with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Search) });
                    g.Toggle("formulas.showFormulas", "Show Formulas", c => c with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Function) });
                    g.Button("formulas.errorChecking", "Error Checking", c => c with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Warning) });
                    g.Button("formulas.evaluateFormula", "Evaluate Formula", c => c with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Function) });
                });
                formulas.Group("formulasCalculation", "Calculation", "C", 80, g =>
                {
                    g.Button("formulas.calcOptions", "Calculation Options", c => c with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Refresh) });
                    g.Button("formulas.calcNow", "Calculate Now", c => c with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Refresh) });
                });
            })
            .Tab("review", "Review", "R", review =>
            {
                review.Group("reviewProofing", "Proofing", "P", 100, g =>
                {
                    g.Button("review.spelling", "Spelling", c => c with
                    {
                        PreferredLayout = RibbonCommandLayoutKind.Large,
                        Icon = new RibbonCommandIcon(RibbonCommandIconKind.Spelling),
                    });
                    g.Button("review.thesaurus", "Thesaurus", c => c with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Book) });
                    g.Button("review.translate", "Translate", c => c with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Translate) });
                });
                review.Group("reviewAccessibility", "Accessibility", "A", 90, g =>
                {
                    g.Button("review.checkAccessibility", "Check Accessibility", c => c with
                    {
                        PreferredLayout = RibbonCommandLayoutKind.Large,
                        Icon = new RibbonCommandIcon(RibbonCommandIconKind.Accessibility),
                    });
                });
                review.Group("reviewComments", "Comments", "C", 80, g =>
                {
                    g.Button("review.newComment", "New Comment", c => c with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Comment) });
                    g.Button("review.deleteComment", "Delete", c => c with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Delete) });
                });
                review.Group("reviewNotes", "Notes", "N", 75, g =>
                {
                    g.Button("review.newNote", "New Note", c => c with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Comment) });
                    g.Button("review.showNotes", "Show Notes", c => c with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.List) });
                });
                review.Group("reviewProtect", "Protect", "T", 70, g =>
                {
                    g.Button("review.protectSheet", "Protect Sheet", c => c with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Protect) });
                    g.Button("review.protectWorkbook", "Protect Workbook", c => c with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Protect) });
                });
            })
            .Tab("view", "View", "W", view =>
            {
                view.Group("viewWorkbookViews", "Workbook Views", "V", 100, g =>
                {
                    g.Button("view.normal", "Normal", c => c with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.View) });
                    g.Button("view.pageBreakPreview", "Page Break Preview", c => c with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.PageBreak) });
                    g.Button("view.pageLayoutView", "Page Layout", c => c with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Page) });
                });
                view.Group("viewShow", "Show", "H", 90, g =>
                {
                    g.Toggle("view.gridlines", "Gridlines", c => c with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Grid) });
                    g.Toggle("view.headings", "Headings", c => c with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Grid) });
                    g.Toggle("view.formulaBar", "Formula Bar", c => c with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Function) });
                });
                view.Group("viewZoom", "Zoom", "Z", 80, g =>
                {
                    g.Button("view.zoom", "Zoom", c => c with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Zoom) });
                    g.Button("view.zoom100", "100%", c => c with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Zoom) });
                    g.Button("view.zoomToSelection", "Zoom to Selection", c => c with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Zoom) });
                });
                view.Group("viewWindow", "Window", "N", 70, g =>
                {
                    g.Button("view.freezePanes", "Freeze Panes", c => c with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Freeze) });
                    g.Button("view.split", "Split", c => c with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Grid) });
                    g.Button("view.arrangeAll", "Arrange All", c => c with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Window) });
                    g.Button("view.hide", "Hide", c => c with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.View) });
                    g.Button("view.unhide", "Unhide", c => c with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.View) });
                    g.Button("view.newWindow", "New Window", c => c with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Window) });
                });
            })
            .Tab("help", "Help", "Y", help =>
            {
                help.Group("helpHelp", "Help", "H", 100, g =>
                {
                    g.Button("help.about", "About FreeX", c => c with
                    {
                        PreferredLayout = RibbonCommandLayoutKind.Large,
                        Icon = new RibbonCommandIcon(RibbonCommandIconKind.Info),
                    });
                    g.Button("help.helpOnline", "Help Online", c => c with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Book) });
                    g.Button("help.feedback", "Send Feedback", c => c with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Comment) });
                    g.Button("help.checkUpdates", "Check for Updates", c => c with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Refresh) });
                });
            })
            // --- Contextual tabs — shown via IRibbonContextSource on selection. Groups mirror the WPF host
            // reference (Chart/Picture/Shape/Table/Pivot). Commands with real handlers in the
            // MainWindow.*Tabs partials are wired in BuildContextualTabCommands(); the rest resolve to the
            // honest "not yet available" stub there (no silent no-ops). ---
            .ContextualTab("chartDesign", "Chart Design", new RibbonTabContext("chart.selected", "Chart Design", RibbonContextColor.Green), tab =>
            {
                tab.Group("chartDesignLayouts", "Layouts", "L", 180, g =>
                {
                    g.Button("chartDesign.titles", "Chart Titles", c => c with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.ChartColumn) });
                    g.Button("chartDesign.dataLabels", "Data Labels", c => c with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Label) });
                    g.Button("chartDesign.dataLabelPosition", "Data Label Position", c => c with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Label) });
                    g.Button("chartDesign.trendline", "Trendline", c => c with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.ChartLine) });
                    g.Button("chartDesign.errorBars", "Error Bars", c => c with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.ChartLine) });
                    g.Button("chartDesign.secondaryAxis", "Secondary Axis", c => c with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.ChartLine) });
                });
                tab.Group("chartDesignStyles", "Styles", "S", 170, g =>
                {
                    g.Button("chartDesign.chartStyles", "Chart Styles", c => c with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.ChartColumn) });
                });
                tab.Group("chartDesignData", "Data", "D", 160, g =>
                {
                    g.Button("chartDesign.selectData", "Select Data Source", c => c with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Search) });
                });
                tab.Group("chartDesignType", "Type", "T", 150, g =>
                {
                    g.Button("chartDesign.changeType", "Change Chart Type", c => c with
                    {
                        PreferredLayout = RibbonCommandLayoutKind.Large,
                        Icon = new RibbonCommandIcon(RibbonCommandIconKind.ChartColumn),
                    });
                    g.Button("chartDesign.comboChart", "Combo Chart", c => c with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.ChartColumn) });
                });
                tab.Group("chartDesignLocation", "Location", "C", 140, g =>
                {
                    g.Button("chartDesign.moveChart", "Move Chart", c => c with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.ChartColumn) });
                });
            })
            .ContextualTab("chartFormat", "Chart Format", new RibbonTabContext("chart.selected", "Chart Format", RibbonContextColor.Green), tab =>
            {
                tab.Group("chartFormatCurrentSelection", "Current Selection", "U", 180, g =>
                {
                    g.Button("chartFormat.formatChartArea", "Format Chart Area", c => c with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.ChartColumn) });
                });
                tab.Group("chartFormatStyles", "Shape Styles", "S", 170, g =>
                {
                    g.Button("chartFormat.chartAreaFill", "Chart Area Fill", c => c with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Fill) });
                    g.Button("chartFormat.plotAreaFill", "Plot Area Fill", c => c with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.ChartArea) });
                    g.Button("chartFormat.plotAreaBorder", "Plot Area Border", c => c with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Border) });
                    g.Button("chartFormat.seriesColor", "Series Color", c => c with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.ChartColumn) });
                });
                tab.Group("chartFormatText", "Text", "X", 80, g =>
                {
                    g.Button("chartFormat.legendText", "Legend Text", c => c with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Label) });
                });
                tab.Group("chartFormatAxes", "Axes", "A", 150, g =>
                {
                    g.Button("chartFormat.xGridlines", "X Axis Gridlines", c => c with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Grid) });
                    g.Button("chartFormat.yGridlines", "Y Axis Gridlines", c => c with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Grid) });
                    g.Button("chartFormat.xLabels", "X Axis Labels", c => c with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.ChartLine) });
                    g.Button("chartFormat.yLabels", "Y Axis Labels", c => c with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.ChartLine) });
                });
            })
            .ContextualTab("pictureFormat", "Picture Format", new RibbonTabContext("picture.selected", "Picture Format", RibbonContextColor.Teal), tab =>
            {
                tab.Group("pictureFormatFormat", "Format", "F", 180, g =>
                {
                    g.Button("pictureFormat.formatPicture", "Format Picture", c => c with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Picture) });
                    g.Button("pictureFormat.crop", "Crop", c => c with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Picture) });
                });
                tab.Group("pictureFormatArrange", "Arrange", "A", 70, g =>
                {
                    g.Button("pictureFormat.bringForward", "Bring Forward", c => c with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.BringForward) });
                    g.Button("pictureFormat.sendBackward", "Send Backward", c => c with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.SendBackward) });
                    g.Button("pictureFormat.selectionPane", "Selection Pane", c => c with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.List) });
                    g.Button("pictureFormat.rotate", "Rotate Object", c => c with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Rotate) });
                    g.Button("pictureFormat.size", "Object Size", c => c with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Size) });
                });
                tab.Group("pictureFormatAccessibility", "Accessibility", "Y", 120, g =>
                {
                    g.Button("pictureFormat.altText", "Alt Text", c => c with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Label) });
                });
            })
            .ContextualTab("shapeFormat", "Shape Format", new RibbonTabContext("shape.selected", "Shape Format", RibbonContextColor.Purple), tab =>
            {
                tab.Group("shapeFormatStyles", "Shape Styles", "S", 180, g =>
                {
                    g.Button("shapeFormat.shapeFill", "Shape Fill", c => c with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.RibbonShape) });
                    g.Button("shapeFormat.shapeOutline", "Shape Outline", c => c with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Border) });
                    g.Button("shapeFormat.shapeGradient", "Shape Gradient", c => c with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.RibbonShape) });
                    g.Dropdown("shapeFormat.shapeEffects", "Shape Effects", ShapeEffectsMenu(), c => c with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Effects) });
                });
                tab.Group("shapeFormatArrange", "Arrange", "A", 70, g =>
                {
                    g.Button("shapeFormat.bringForward", "Bring Forward", c => c with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.BringForward) });
                    g.Button("shapeFormat.sendBackward", "Send Backward", c => c with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.SendBackward) });
                    g.Button("shapeFormat.selectionPane", "Selection Pane", c => c with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.List) });
                    g.Button("shapeFormat.rotate", "Rotate Object", c => c with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Rotate) });
                    g.Button("shapeFormat.size", "Object Size", c => c with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Size) });
                });
                tab.Group("shapeFormatAccessibility", "Accessibility", "Y", 120, g =>
                {
                    g.Button("shapeFormat.altText", "Alt Text", c => c with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Label) });
                });
            })
            .ContextualTab("tableDesign", "Table Design", new RibbonTabContext("table.active", "Table Design", RibbonContextColor.Blue), tab =>
            {
                tab.Group("tableDesignProperties", "Properties", "P", 180, g =>
                {
                    g.Button("tableDesign.tableName", "Table Name", c => c with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Label) });
                    g.Button("tableDesign.resize", "Resize Table", c => c with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Scale) });
                });
                tab.Group("tableDesignTools", "Tools", "T", 170, g =>
                {
                    g.Button("tableDesign.removeDuplicates", "Remove Duplicates", c => c with
                    {
                        PreferredLayout = RibbonCommandLayoutKind.Large,
                        Icon = new RibbonCommandIcon(RibbonCommandIconKind.Delete),
                    });
                    g.Button("tableDesign.convertToRange", "Convert to Range", c => c with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Refresh) });
                });
                tab.Group("tableDesignStyleOptions", "Style Options", "O", 160, g =>
                {
                    g.Toggle("tableDesign.totalRow", "Total Row", c => c with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Sum) });
                    g.Toggle("tableDesign.firstColumn", "First Column", c => c with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Table) });
                    g.Toggle("tableDesign.lastColumn", "Last Column", c => c with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Table) });
                    g.Toggle("tableDesign.bandedRows", "Banded Rows", c => c with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Table) });
                    g.Toggle("tableDesign.bandedColumns", "Banded Columns", c => c with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Table) });
                    g.Toggle("tableDesign.filterButton", "Filter Button", c => c with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Filter) });
                });
                tab.Group("tableDesignStyles", "Styles", "Y", 150, g =>
                {
                    g.Button("tableDesign.tableStyles", "Table Styles", c => c with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Theme) });
                });
            })
            .ContextualTab("pivotAnalyze", "PivotTable Analyze", new RibbonTabContext("pivot.active", "PivotTable Analyze", RibbonContextColor.Orange), tab =>
            {
                tab.Group("pivotAnalyzePivotTable", "PivotTable", "P", 180, g =>
                {
                    g.Button("pivotAnalyze.name", "PivotTable Name", c => c with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.PivotTable) });
                    g.Button("pivotAnalyze.options", "PivotTable Options", c => c with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.PivotTable) });
                });
                tab.Group("pivotAnalyzeActiveField", "Active Field", "F", 170, g =>
                {
                    g.Button("pivotAnalyze.fieldSettings", "Field Settings", c => c with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Label) });
                });
                tab.Group("pivotAnalyzeGroup", "Group", "G", 160, g =>
                {
                    g.Button("pivotAnalyze.groupField", "Group Field", c => c with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Group) });
                    g.Button("pivotAnalyze.ungroup", "Ungroup", c => c with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Ungroup) });
                });
                tab.Group("pivotAnalyzeFilter", "Filter", "L", 150, g =>
                {
                    g.Button("pivotAnalyze.insertSlicer", "Insert Slicer", c => c with
                    {
                        PreferredLayout = RibbonCommandLayoutKind.Large,
                        Icon = new RibbonCommandIcon(RibbonCommandIconKind.Filter),
                    });
                    g.Button("pivotAnalyze.insertTimeline", "Insert Timeline", c => c with
                    {
                        PreferredLayout = RibbonCommandLayoutKind.Large,
                        Icon = new RibbonCommandIcon(RibbonCommandIconKind.Date),
                    });
                });
                tab.Group("pivotAnalyzeData", "Data", "D", 140, g =>
                {
                    g.Button("pivotAnalyze.refresh", "Refresh", c => c with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Refresh) });
                    g.Button("pivotAnalyze.changeDataSource", "Change Data Source", c => c with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.GetData) });
                });
                tab.Group("pivotAnalyzeCalculations", "Calculations", "C", 120, g =>
                {
                    g.Button("pivotAnalyze.calculatedField", "Calculated Field", c => c with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Function) });
                });
                tab.Group("pivotAnalyzeShow", "Show", "W", 110, g =>
                {
                    g.Toggle("pivotAnalyze.fieldList", "Field List", c => c with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.List) });
                    g.Toggle("pivotAnalyze.fieldHeaders", "Field Headers", c => c with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.HeaderFooter) });
                });
            })
            .ContextualTab("pivotDesign", "PivotTable Design", new RibbonTabContext("pivot.active", "PivotTable Design", RibbonContextColor.Orange), tab =>
            {
                tab.Group("pivotDesignLayout", "Layout", "L", 180, g =>
                {
                    g.Button("pivotDesign.grandTotals", "Grand Totals", c => c with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Sum) });
                    g.Button("pivotDesign.subtotals", "Subtotals", c => c with
                    {
                        PreferredLayout = RibbonCommandLayoutKind.Large,
                        Icon = new RibbonCommandIcon(RibbonCommandIconKind.Sum),
                    });
                    g.Button("pivotDesign.reportLayout", "Report Layout", c => c with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.List) });
                    g.Button("pivotDesign.blankRows", "Blank Rows", c => c with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.List) });
                });
                tab.Group("pivotDesignStyleOptions", "Style Options", "O", 170, g =>
                {
                    g.Toggle("pivotDesign.bandedRows", "Banded Rows", c => c with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Table) });
                    g.Toggle("pivotDesign.bandedColumns", "Banded Columns", c => c with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Table) });
                    g.Toggle("pivotDesign.rowHeaders", "Row Headers", c => c with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.HeaderFooter) });
                    g.Toggle("pivotDesign.columnHeaders", "Column Headers", c => c with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.HeaderFooter) });
                });
                tab.Group("pivotDesignStyles", "Styles", "Y", 160, g =>
                {
                    g.Button("pivotDesign.pivotStyles", "PivotTable Styles", c => c with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.PivotTable) });
                });
            })
            .Build();
    }

    public static IRibbonCommandRegistry BuildRegistry(Func<WorkbookSession?> session, Action<string> setStatus)
        => BuildRegistry(session, setStatus, new AvaloniaRibbonHostCallbacks());

    public static IRibbonCommandRegistry BuildRegistry(
        Func<WorkbookSession?> session,
        Action<string> setStatus,
        AvaloniaRibbonHostCallbacks callbacks)
    {
        var registry = new RibbonCommandRegistry();
        foreach (var id in EnumerateCommandIds(BuildDefinition()))
            registry.Register(id, NoOpRibbonCommand.Instance);

        // Override the representative formatting toggles with the shared, platform-neutral commands so
        // the Avalonia ribbon performs real edits (the same WorkbookSession logic the WPF host runs).
        registry.Register("home.bold", WorkbookFormatRibbonCommands.Bold(session, ApplyStatus(setStatus, "Bold")));
        registry.Register("home.italic", WorkbookFormatRibbonCommands.Italic(session, ApplyStatus(setStatus, "Italic")));
        registry.Register("home.underline", WorkbookFormatRibbonCommands.Underline(session, ApplyStatus(setStatus, "Underline")));

        // Override the Insert ▸ Charts controls with a real insert action: each maps its command id to a
        // ChartType, runs the Core AddChartCommand over the selection, and refreshes so the chart paints.
        RegisterChartCommands(registry, session, setStatus);

        // Override the controls whose behavior lives in the Avalonia shell (dialogs / selection commands)
        // with host callbacks, so the declarative ribbon invokes the same handlers as the native menus.
        ApplyHostCallbacks(registry, callbacks);
        return registry;
    }

    /// <summary>
    /// Binds each non-null host callback to its ribbon command id(s) via a <see cref="RelayRibbonCommand"/>,
    /// replacing the no-op registration. Null callbacks leave the no-op so the smoke harness still builds.
    /// </summary>
    private static void ApplyHostCallbacks(IRibbonCommandRegistry registry, AvaloniaRibbonHostCallbacks callbacks)
    {
        void Bind(string id, Action? action)
        {
            if (action is not null)
                registry.Register(new RibbonCommandId(id), new RelayRibbonCommand(action));
        }

        Bind("data.textToColumns", callbacks.OpenTextToColumns);
        Bind("data.consolidate", callbacks.OpenConsolidate);
        Bind("insert.table", callbacks.InsertTable);
        Bind("home.formatAsTable", callbacks.InsertTable);
        Bind("home.conditional", callbacks.ConditionalFormatting);
        Bind("data.quickAnalysis", callbacks.QuickAnalysis);
        Bind("insert.pivotTable", callbacks.InsertPivotTable);
        Bind("insert.picture", callbacks.InsertPicture);
        Bind("insert.shapes", callbacks.InsertShape);
        Bind("insert.textBox", callbacks.InsertTextBox);
        Bind("home.formatPainter", callbacks.FormatPainter);

        if (callbacks.SetFontSize is { } setFontSize)
            registry.Register(new RibbonCommandId("home.fontSize"), new RelayValueRibbonCommand(setFontSize));
        if (callbacks.SetFontName is { } setFontName)
            registry.Register(new RibbonCommandId("home.fontName"), new RelayValueRibbonCommand(setFontName));
        Bind("data.sortAsc", callbacks.SortAscending);
        Bind("data.sortDesc", callbacks.SortDescending);
        Bind("data.filter", callbacks.ToggleFilter);
        Bind("data.validation", callbacks.DataValidation);
        Bind("data.validationDialog", callbacks.DataValidation);

        Bind("home.cut", callbacks.Cut);
        Bind("home.copy", callbacks.Copy);
        Bind("home.paste", callbacks.Paste);
        Bind("home.alignLeft", callbacks.AlignLeft);
        Bind("home.alignCenter", callbacks.AlignCenter);
        Bind("home.alignRight", callbacks.AlignRight);
        Bind("home.wrapText", callbacks.WrapText);
        Bind("home.merge", callbacks.MergeAndCenter);
        Bind("home.mergeCenter", callbacks.MergeAndCenter);
        Bind("home.currency", callbacks.CurrencyFormat);
        Bind("home.percent", callbacks.PercentFormat);
        Bind("home.comma", callbacks.CommaStyle);

        if (callbacks.ExtraCommands is { } extra)
            foreach (var (id, action) in extra)
                Bind(id, action);
    }

    /// <summary>
    /// Wires the Insert chart-type buttons and their chart-type menu items to
    /// <see cref="InsertChartCommandFactory"/>. Any command id the factory maps to a
    /// <see cref="ChartType"/> gets an <see cref="InsertChartRibbonCommand"/>; unmapped ids keep their
    /// no-op registration.
    /// </summary>
    private static void RegisterChartCommands(
        IRibbonCommandRegistry registry,
        Func<WorkbookSession?> session,
        Action<string> setStatus)
    {
        foreach (var id in EnumerateCommandIds(BuildDefinition()))
        {
            if (InsertChartCommandFactory.ChartTypeForRibbonCommand(id.Value) is not { } chartType)
                continue;
            registry.Register(id, new InsertChartRibbonCommand(session, chartType, setStatus));
        }
    }

    /// <summary>Builds a post-apply callback that redraws the shell and reports the outcome on the status bar.</summary>
    private static Action<WorkbookCellEditResult, bool> ApplyStatus(Action<string> setStatus, string label) =>
        (result, on) => setStatus(result.Success
            ? $"{label} {(on ? "on" : "off")}"
            : result.ErrorMessage ?? $"{label} failed.");

    private static IEnumerable<RibbonCommandId> EnumerateCommandIds(RibbonDefinition definition)
    {
        foreach (var tab in definition.Tabs)
        foreach (var group in tab.Groups)
        foreach (var control in group.Controls)
        {
            if (!string.IsNullOrEmpty(control.CommandId.Value))
                yield return control.CommandId;

            foreach (var menuId in EnumerateMenuIds(control))
                yield return menuId;
        }
    }

    private static IEnumerable<RibbonCommandId> EnumerateMenuIds(RibbonControl control)
    {
        var menu = control switch
        {
            RibbonSplitButton split => split.Menu,
            RibbonDropdown dropdown => dropdown.Menu,
            _ => null,
        };
        if (menu is null)
            yield break;

        foreach (var id in EnumerateMenuIds(menu.Items))
            yield return id;
    }

    private static IEnumerable<RibbonCommandId> EnumerateMenuIds(IReadOnlyList<RibbonMenuItem> items)
    {
        foreach (var item in items)
        {
            if (item.CommandId is { } id && !string.IsNullOrEmpty(id.Value))
                yield return id;
            foreach (var childId in EnumerateMenuIds(item.Children))
                yield return childId;
        }
    }

    private static RibbonMenu PasteMenu() => new(new[]
    {
        new RibbonMenuItem("Paste", "home.paste", InputGesture: "Ctrl+V"),
        new RibbonMenuItem("Paste Values", "home.pasteValues"),
        new RibbonMenuItem("Paste Formatting", "home.pasteFormat"),
        RibbonMenuItem.Separator(),
        new RibbonMenuItem("Paste Special...", "home.pasteSpecial"),
    });

    private static RibbonMenu FillColorMenu() => new(new[]
    {
        new RibbonMenuItem("No Fill", "home.fillNone"),
        new RibbonMenuItem("Yellow", "home.fillYellow"),
        new RibbonMenuItem("Green", "home.fillGreen"),
        new RibbonMenuItem("More Colors...", "home.fillMore"),
    });

    private static RibbonMenu FontColorMenu() => new(new[]
    {
        new RibbonMenuItem("Automatic", "home.fontColorAuto"),
        new RibbonMenuItem("Red", "home.fontColorRed"),
        new RibbonMenuItem("Green", "home.fontColorGreen"),
        new RibbonMenuItem("Blue", "home.fontColorBlue"),
        new RibbonMenuItem("More Colors...", "home.fontColorMore"),
    });

    private static RibbonMenu BordersMenu() => new(new[]
    {
        new RibbonMenuItem("All Borders", "home.bordersAll"),
        new RibbonMenuItem("Outside Borders", "home.bordersOutside"),
        new RibbonMenuItem("No Border", "home.bordersNone"),
    });

    private static RibbonMenu MergeMenu() => new(new[]
    {
        new RibbonMenuItem("Merge & Center", "home.mergeCenter"),
        new RibbonMenuItem("Merge Across", "home.mergeAcross"),
        new RibbonMenuItem("Merge Cells", "home.mergeCells"),
        new RibbonMenuItem("Unmerge Cells", "home.unmerge"),
    });

    private static RibbonMenu NumberFormatMenu() => new(new[]
    {
        new RibbonMenuItem("General", "home.fmtGeneral"),
        new RibbonMenuItem("Number", "home.fmtNumber"),
        new RibbonMenuItem("Currency", "home.fmtCurrency"),
        new RibbonMenuItem("Date", "home.fmtDate"),
        new RibbonMenuItem("Percentage", "home.fmtPercent"),
    });

    private static RibbonMenu ChartTypeMenu() => new(new[]
    {
        new RibbonMenuItem("Clustered Column", "insert.colClustered"),
        new RibbonMenuItem("Stacked Column", "insert.colStacked"),
        new RibbonMenuItem("100% Stacked Column", "insert.col100"),
    });

    private static RibbonMenu ShapeEffectsMenu() => new(new[]
    {
        new RibbonMenuItem("No Effect", "shapeFormat.shapeEffectNone"),
        RibbonMenuItem.Separator(),
        new RibbonMenuItem("Shadow", "shapeFormat.shapeEffectShadow"),
    });

    private static RibbonMenu ValidationMenu() => new(new[]
    {
        new RibbonMenuItem("Data Validation...", "data.validationDialog"),
        new RibbonMenuItem("Circle Invalid Data", "data.circleInvalid"),
        new RibbonMenuItem("Clear Validation Circles", "data.clearCircles"),
    });
}
