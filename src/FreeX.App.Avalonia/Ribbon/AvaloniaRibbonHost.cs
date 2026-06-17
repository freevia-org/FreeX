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
                    g.SplitButton("home.paste", "Paste", PasteMenu(), c => c with
                    {
                        PreferredLayout = RibbonCommandLayoutKind.Large,
                        Icon = new RibbonCommandIcon(RibbonCommandIconKind.Paste),
                    });
                    g.Button("home.cut", "Cut", c => c with
                    {
                        Icon = new RibbonCommandIcon(RibbonCommandIconKind.Cut),
                    });
                    g.Button("home.copy", "Copy", c => c with
                    {
                        Icon = new RibbonCommandIcon(RibbonCommandIconKind.Copy),
                    });
                    g.Button("home.formatPainter", "Format Painter", c => c with
                    {
                        Icon = new RibbonCommandIcon(RibbonCommandIconKind.FormatPainter),
                    });
                });

                home.Group("font", "Font", "F", 90, g =>
                {
                    g.ComboBox("home.fontName", "Font", c => c with
                    {
                        Items = new[] { "Calibri", "Arial", "Times New Roman", "Consolas" },
                    });
                    g.ComboBox("home.fontSize", "Size", c => c with
                    {
                        Items = new[] { "8", "9", "10", "11", "12", "14", "16", "18", "24" },
                    });
                    g.Toggle("home.bold", "Bold", c => c with
                    {
                        Icon = new RibbonCommandIcon(RibbonCommandIconKind.Bold),
                    });
                    g.Toggle("home.italic", "Italic", c => c with
                    {
                        Icon = new RibbonCommandIcon(RibbonCommandIconKind.Italic),
                    });
                    g.Toggle("home.underline", "Underline", c => c with
                    {
                        Icon = new RibbonCommandIcon(RibbonCommandIconKind.Underline),
                    });
                    g.Toggle("home.strikethrough", "Strikethrough", c => c with
                    {
                        Icon = new RibbonCommandIcon(RibbonCommandIconKind.Strikethrough),
                    });
                    g.Button("home.increaseFont", "Increase Font Size", c => c with
                    {
                        Icon = new RibbonCommandIcon(RibbonCommandIconKind.Font),
                    });
                    g.Button("home.decreaseFont", "Decrease Font Size", c => c with
                    {
                        Icon = new RibbonCommandIcon(RibbonCommandIconKind.Font),
                    });
                    g.Separator();
                    g.Dropdown("home.fontColor", "Font Color", FontColorMenu(), c => c with
                    {
                        Icon = new RibbonCommandIcon(RibbonCommandIconKind.Font),
                    });
                    g.Dropdown("home.fillColor", "Fill Color", FillColorMenu(), c => c with
                    {
                        Icon = new RibbonCommandIcon(RibbonCommandIconKind.Fill),
                    });
                    g.Dropdown("home.borders", "Borders", BordersMenu(), c => c with
                    {
                        Icon = new RibbonCommandIcon(RibbonCommandIconKind.Border),
                    });
                });

                home.Group("alignment", "Alignment", "A", 80, g =>
                {
                    g.Button("home.alignTop", "Top Align", c => c with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Align) });
                    g.Button("home.alignMiddle", "Middle Align", c => c with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Align) });
                    g.Button("home.alignBottom", "Bottom Align", c => c with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Align) });
                    g.Button("home.orientation", "Orientation", c => c with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Orientation) });
                    g.Separator();
                    g.Button("home.alignLeft", "Align Left", c => c with
                    {
                        Icon = new RibbonCommandIcon(RibbonCommandIconKind.Align),
                    });
                    g.Button("home.alignCenter", "Center", c => c with
                    {
                        Icon = new RibbonCommandIcon(RibbonCommandIconKind.Align),
                    });
                    g.Button("home.alignRight", "Align Right", c => c with
                    {
                        Icon = new RibbonCommandIcon(RibbonCommandIconKind.Align),
                    });
                    g.Button("home.decreaseIndent", "Decrease Indent", c => c with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Align) });
                    g.Button("home.increaseIndent", "Increase Indent", c => c with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Align) });
                    g.Button("home.wrapText", "Wrap Text", c => c with
                    {
                        Icon = new RibbonCommandIcon(RibbonCommandIconKind.Wrap),
                    });
                    g.SplitButton("home.merge", "Merge & Center", MergeMenu(), c => c with
                    {
                        PreferredLayout = RibbonCommandLayoutKind.Large,
                        Icon = new RibbonCommandIcon(RibbonCommandIconKind.Merge),
                    });
                });

                home.Group("number", "Number", "N", 70, g =>
                {
                    g.Dropdown("home.numberFormat", "Number Format", NumberFormatMenu(), c => c with
                    {
                        Icon = new RibbonCommandIcon(RibbonCommandIconKind.Number),
                    });
                    g.Button("home.currency", "Currency", c => c with
                    {
                        Icon = new RibbonCommandIcon(RibbonCommandIconKind.Currency),
                    });
                    g.Button("home.percent", "Percent", c => c with
                    {
                        Icon = new RibbonCommandIcon(RibbonCommandIconKind.Percent),
                    });
                    g.Button("home.accounting", "Accounting", c => c with
                    {
                        Icon = new RibbonCommandIcon(RibbonCommandIconKind.Currency),
                    });
                    g.Button("home.comma", "Comma", c => c with
                    {
                        Icon = new RibbonCommandIcon(RibbonCommandIconKind.Comma),
                    });
                    g.Button("home.increaseDecimal", "Increase Decimal", c => c with
                    {
                        Icon = new RibbonCommandIcon(RibbonCommandIconKind.Decimal),
                    });
                    g.Button("home.decreaseDecimal", "Decrease Decimal", c => c with
                    {
                        Icon = new RibbonCommandIcon(RibbonCommandIconKind.Decimal),
                    });
                });

                home.Group("styles", "Styles", "S", 60, g =>
                {
                    g.Button("home.cellStyles", "Cell Styles", c => c with
                    {
                        PreferredLayout = RibbonCommandLayoutKind.Large,
                        Icon = new RibbonCommandIcon(RibbonCommandIconKind.Theme),
                    });
                    g.Button("home.formatAsTable", "Format as Table", c => c with
                    {
                        Icon = new RibbonCommandIcon(RibbonCommandIconKind.Table),
                    });
                    g.Button("home.conditional", "Conditional", c => c with
                    {
                        Icon = new RibbonCommandIcon(RibbonCommandIconKind.Color),
                    });
                });

                home.Group("cells", "Cells", "E", 50, g =>
                {
                    g.Button("home.insertCells", "Insert", c => c with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Insert) });
                    g.Button("home.deleteCells", "Delete", c => c with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Delete) });
                    g.Button("home.formatCells", "Format", c => c with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Size) });
                });

                home.Group("editing", "Editing", "G", 40, g =>
                {
                    g.Button("home.autoSum", "AutoSum", c => c with
                    {
                        PreferredLayout = RibbonCommandLayoutKind.Large,
                        Icon = new RibbonCommandIcon(RibbonCommandIconKind.Sum),
                    });
                    g.Button("home.fillDown", "Fill", c => c with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Fill) });
                    g.Button("home.clear", "Clear", c => c with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Clear) });
                    g.Button("home.findSelect", "Find & Select", c => c with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Search) });
                });
            })
            .Tab("insert", "Insert", "I", insert =>
            {
                insert.Group("tables", "Tables", "T", 100, g =>
                {
                    g.Button("insert.pivotTable", "PivotTable", c => c with
                    {
                        PreferredLayout = RibbonCommandLayoutKind.Large,
                        Icon = new RibbonCommandIcon(RibbonCommandIconKind.PivotTable),
                    });
                    g.Button("insert.table", "Table", c => c with
                    {
                        PreferredLayout = RibbonCommandLayoutKind.Large,
                        Icon = new RibbonCommandIcon(RibbonCommandIconKind.Table),
                    });
                });

                insert.Group("charts", "Charts", "C", 90, g =>
                {
                    g.Dropdown("insert.column", "Column", ChartTypeMenu(), c => c with
                    {
                        Icon = new RibbonCommandIcon(RibbonCommandIconKind.ChartColumn),
                    });
                    g.Button("insert.line", "Line", c => c with
                    {
                        Icon = new RibbonCommandIcon(RibbonCommandIconKind.ChartLine),
                    });
                    g.Button("insert.pie", "Pie", c => c with
                    {
                        Icon = new RibbonCommandIcon(RibbonCommandIconKind.ChartPie),
                    });
                    g.Button("insert.scatter", "Scatter", c => c with
                    {
                        Icon = new RibbonCommandIcon(RibbonCommandIconKind.ChartScatter),
                    });
                });

                insert.Group("illustrations", "Illustrations", "L", 80, g =>
                {
                    g.Button("insert.picture", "Picture", c => c with
                    {
                        PreferredLayout = RibbonCommandLayoutKind.Large,
                        Icon = new RibbonCommandIcon(RibbonCommandIconKind.Picture),
                    });
                    g.Button("insert.shapes", "Shapes", c => c with
                    {
                        Icon = new RibbonCommandIcon(RibbonCommandIconKind.Rectangle),
                    });
                    g.Button("insert.textBox", "Text Box", c => c with
                    {
                        Icon = new RibbonCommandIcon(RibbonCommandIconKind.TextBox),
                    });
                });

                insert.Group("sparklines", "Sparklines", "S", 70, g =>
                {
                    g.Button("insert.sparklineLine", "Line", c => c with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Sparkline) });
                    g.Button("insert.sparklineColumn", "Column", c => c with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.ChartColumn) });
                    g.Button("insert.sparklineWinLoss", "Win/Loss", c => c with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Sparkline) });
                });

                insert.Group("filters", "Filters", "F", 60, g =>
                {
                    g.Button("insert.slicer", "Slicer", c => c with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Filter) });
                    g.Button("insert.timeline", "Timeline", c => c with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Date) });
                });

                insert.Group("links", "Links", "I", 50, g =>
                {
                    g.Button("insert.hyperlink", "Link", c => c with
                    {
                        PreferredLayout = RibbonCommandLayoutKind.Large,
                        Icon = new RibbonCommandIcon(RibbonCommandIconKind.Link),
                    });
                });

                insert.Group("comments", "Comments", "C", 40, g =>
                {
                    g.Button("insert.comment", "Comment", c => c with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Comment) });
                });

                insert.Group("text", "Text", "X", 30, g =>
                {
                    g.Button("insert.headerFooter", "Header & Footer", c => c with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.HeaderFooter) });
                    g.Button("insert.object", "Object", c => c with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Insert) });
                });

                insert.Group("symbols", "Symbols", "Y", 20, g =>
                {
                    g.Button("insert.equation", "Equation", c => c with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Function) });
                    g.Button("insert.symbol", "Symbol", c => c with { Icon = new RibbonCommandIcon(RibbonCommandIconKind.Symbol) });
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

    private static RibbonMenu ValidationMenu() => new(new[]
    {
        new RibbonMenuItem("Data Validation...", "data.validationDialog"),
        new RibbonMenuItem("Circle Invalid Data", "data.circleInvalid"),
        new RibbonMenuItem("Clear Validation Circles", "data.clearCircles"),
    });
}
