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

    /// <summary>Data ▸ Sort A-Z.</summary>
    public Action? SortAscending { get; init; }

    /// <summary>Data ▸ Sort Z-A.</summary>
    public Action? SortDescending { get; init; }

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
                    g.Separator();
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
                    g.Button("home.comma", "Comma", c => c with
                    {
                        Icon = new RibbonCommandIcon(RibbonCommandIconKind.Comma),
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
                });

                data.Group("tools", "Data Tools", "O", 80, g =>
                {
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
        Bind("data.sortAsc", callbacks.SortAscending);
        Bind("data.sortDesc", callbacks.SortDescending);
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
