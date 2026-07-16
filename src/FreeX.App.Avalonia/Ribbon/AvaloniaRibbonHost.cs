using Avalonia.Controls;
using Free.Shared.Ribbon;
using FreeX.App.Avalonia.Charts;
using FreeX.App.Services;
using FreeX.App.Services.Ribbon;
using FreeX.Core.Model;
using Free.Shared.Ribbon.Avalonia;
using Free.Shared.Theme;
using FreeX.Ribbon.Definitions;

namespace FreeX.App.Avalonia.Ribbon;

/// <summary>
/// Builds the FreeX Avalonia ribbon from the SAME canonical, single-source ribbon definition the WPF app
/// consumes (<see cref="FreeXRibbon.Build"/> in the shared <c>FreeX.Ribbon.Definitions</c> project), rendered
/// via <see cref="AvaloniaRibbonRenderer"/>. There is no longer a separate Avalonia ribbon definition: one
/// declarative definition drives both apps.
///
/// The shell still registers its command handlers under the dotted ids it has always used; each registration
/// is translated through <see cref="AvaloniaCommandIdAdapter.ToCanonical"/> to the canonical id the shared
/// definition emits, so the renderer (which queries the registry by canonical id) finds the handler. Bold /
/// Italic / Underline bind to the shared, platform-neutral <see cref="WorkbookFormatRibbonCommands"/> — the
/// same command logic the WPF host uses — so clicking them formats the live selection. Every canonical id in
/// the shared definition resolves in the registry to either a real handler or the honest NoOp stub.
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
    public static (Control Ribbon, Action RefreshToggleStates) Build(
        Func<WorkbookSession?> session,
        Action<string> setStatus,
        AvaloniaRibbonHostCallbacks callbacks,
        IRibbonContextSource? contextSource)
    {
        var registry = AvaloniaRibbonComposition.BuildRegistry(session, setStatus, callbacks);
        var definition = AvaloniaRibbonComposition.BuildDefinition();
        var palette = RibbonVisualPalette.FromTheme(App.ActiveTheme);
        var ribbon = AvaloniaRibbonRenderer.BuildRibbon(
            definition,
            registry,
            contextSource,
            palette: palette,
            onFileTabSelected: callbacks.Backstage);
        return (ribbon, () => AvaloniaRibbonRenderer.SyncToggleStates(ribbon, registry, palette));
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
    /// an <see cref="ActionRibbonCommand"/>; null callbacks (e.g. in the smoke harness) leave the no-op.
    /// </summary>
    public static Control Build(
        Func<WorkbookSession?> session,
        Action<string> setStatus,
        AvaloniaRibbonHostCallbacks callbacks)
    {
        var registry = AvaloniaRibbonComposition.BuildRegistry(session, setStatus, callbacks);
        var definition = AvaloniaRibbonComposition.BuildDefinition();
        return AvaloniaRibbonRenderer.BuildRibbon(
            definition,
            registry,
            palette: RibbonVisualPalette.FromTheme(App.ActiveTheme));
    }
}

/// <summary>
/// Host-supplied actions the Avalonia ribbon binds to. Each maps one (or more) ribbon command id(s) to a
/// shell handler — opening a dialog or running a selection command. Kept as a record of nullable
/// <see cref="Action"/>s so the smoke harness (and tests) can build the ribbon with none of them wired.
/// </summary>
internal sealed record AvaloniaRibbonHostCallbacks
{
    /// <summary>File tab - open the Office-style backstage surface.</summary>
    public Action? Backstage { get; init; }

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

    /// <summary>Applies a Home Number Format combo selection.</summary>
    public Action<string?>? SetNumberFormat { get; init; }

    /// <summary>
    /// Additional command-id → action bindings for parameterized menu items the named callbacks do not
    /// cover (e.g. the Number Format dropdown's General/Number/Currency/Date/Percent items, or the Fill
    /// Color dropdown's swatch items). Applied after the named callbacks; each id overrides its no-op.
    /// Keys are the Avalonia dotted ids; the registry re-keys them to canonical ids via the adapter.
    /// </summary>
    public IReadOnlyDictionary<string, Action>? ExtraCommands { get; init; }

    /// <summary>
    /// Optional command-id -> state bindings for entries in <see cref="ExtraCommands"/> that render checked or
    /// disabled state, such as the View-tab show/hide checkboxes.
    /// </summary>
    public IReadOnlyDictionary<string, Func<RibbonCommandState>>? ExtraCommandStates { get; init; }
}

/// <summary>An <see cref="IRibbonStatefulCommand"/> that invokes a host callback and reports live UI state.</summary>
internal sealed class StatefulRelayRibbonCommand : IRibbonStatefulCommand
{
    private readonly Action _execute;
    private readonly Func<RibbonCommandState> _getState;

    public StatefulRelayRibbonCommand(Action execute, Func<RibbonCommandState> getState)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _getState = getState ?? throw new ArgumentNullException(nameof(getState));
    }

    public void Execute(RibbonCommandContext context) => _execute();

    public RibbonCommandState GetState() => _getState() ?? RibbonCommandState.Default;
}

/// <summary>A disabled placeholder for shared ribbon commands that are intentionally unavailable.</summary>
internal sealed class DisabledNoOpRibbonCommand : IRibbonStatefulCommand
{
    public static readonly DisabledNoOpRibbonCommand Instance = new();

    public void Execute(RibbonCommandContext context)
    {
        // Intentionally empty: unavailable commands render disabled and ignore activation.
    }

    public RibbonCommandState GetState() => new(IsEnabled: false);
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

        var command = InsertChartCommandFactory.Build(session.ActiveSheet, session.SelectedRange, _chartType);
        var result = session.ExecuteReviewCommand(command);
        _setStatus(result.Success
            ? $"Inserted {_chartType} chart"
            : result.ErrorMessage ?? "Insert Chart failed.");
    }
}

/// <summary>
/// Composes the Avalonia ribbon from the canonical shared definition and seeds the command registry. The
/// definition is <see cref="FreeXRibbon.Build"/> verbatim — the single source of truth shared with WPF — so
/// the Avalonia and Windows ribbons render from identical declarations. All command resolution is keyed to
/// the canonical ids the definition emits; shell handlers (registered under their historical dotted ids) are
/// translated to canonical ids via <see cref="AvaloniaCommandIdAdapter"/>.
/// </summary>
internal static class AvaloniaRibbonComposition
{
    private static readonly IReadOnlySet<string> StaticDrawUnavailableCommandIds = new HashSet<string>(StringComparer.Ordinal)
    {
        "Crop Picture",
        "Shape Gradient",
        "Shape Effects",
    };

    /// <summary>The canonical, single-source ribbon definition shared with the WPF app.</summary>
    public static RibbonDefinition BuildDefinition()
    {
        var definition = FreeXRibbon.Build();
        var numberFormatCommandId = AvaloniaCommandIdAdapter.ToCanonical("home.numberFormat");
        var numberFormatLabels = HomeNumberFormatDropdownPlanner.Options
            .Select(option => option.Label)
            .ToArray();
        return definition with
        {
            Tabs = definition.Tabs.Select(tab => tab with
            {
                Groups = tab.Groups.Select(group => group with
                {
                    Controls = group.Controls.Select(control =>
                        control is RibbonComboBox combo &&
                        string.Equals(combo.CommandId.Value, numberFormatCommandId, StringComparison.Ordinal)
                            ? combo with { Items = numberFormatLabels }
                            : control).ToArray(),
                }).ToArray(),
            }).ToArray(),
        };
    }

    public static IRibbonCommandRegistry BuildRegistry(Func<WorkbookSession?> session, Action<string> setStatus)
        => BuildRegistry(session, setStatus, new AvaloniaRibbonHostCallbacks());

    public static IRibbonCommandRegistry BuildRegistry(
        Func<WorkbookSession?> session,
        Action<string> setStatus,
        AvaloniaRibbonHostCallbacks callbacks)
    {
        var registry = new RibbonCommandRegistry();

        // Seed every canonical id the shared definition emits with the shared no-op stub, so the shared
        // definition's richer surface (Draw/Help tabs, deeper menus) renders enabled without a crash even
        // before any real handler is wired. Real handlers below override the relevant ids.
        var definition = BuildDefinition();
        foreach (var id in EnumerateCommandIds(definition))
            registry.Register(id, EmptyRibbonCommand.Instance);

        RegisterDisabledDrawDefaults(registry, definition);

        // Override the representative formatting toggles with the shared, platform-neutral commands so the
        // Avalonia ribbon performs real edits (the same WorkbookSession logic the WPF host runs). Keys are
        // translated to the canonical ids the shared definition emits.
        Register(registry, "home.bold", WorkbookFormatRibbonCommands.Bold(session, ApplyStatus(setStatus, "Bold")));
        Register(registry, "home.italic", WorkbookFormatRibbonCommands.Italic(session, ApplyStatus(setStatus, "Italic")));
        Register(registry, "home.underline", WorkbookFormatRibbonCommands.Underline(session, ApplyStatus(setStatus, "Underline")));

        // Override the Insert ▸ Charts controls with a real insert action: each canonical id the factory maps
        // to a ChartType gets an InsertChartRibbonCommand; unmapped ids keep their NoOp registration.
        RegisterChartCommands(registry, session, setStatus);

        // Override the controls whose behavior lives in the Avalonia shell (dialogs / selection commands)
        // with host callbacks, so the declarative ribbon invokes the same handlers as the native menus.
        ApplyHostCallbacks(registry, callbacks);
        return registry;
    }

    private static void RegisterDisabledDrawDefaults(RibbonCommandRegistry registry, RibbonDefinition definition)
    {
        var drawTab = definition.Tabs.FirstOrDefault(tab => string.Equals(tab.Id, "DrawTab", StringComparison.Ordinal));
        if (drawTab is null)
            return;

        foreach (var id in EnumerateCommandIds(drawTab))
            if (StaticDrawUnavailableCommandIds.Contains(id.Value))
                registry.Register(id, DisabledNoOpRibbonCommand.Instance);
    }

    /// <summary>Registers <paramref name="command"/> under the canonical id for the Avalonia <paramref name="avaloniaId"/>.</summary>
    private static void Register(IRibbonCommandRegistry registry, string avaloniaId, IRibbonCommand command)
        => registry.Register(new RibbonCommandId(AvaloniaCommandIdAdapter.ToCanonical(avaloniaId)), command);

    /// <summary>
    /// Binds each non-null host callback to its canonical command id(s) via an <see cref="ActionRibbonCommand"/>,
    /// replacing the no-op registration. Null callbacks leave the no-op so the smoke harness still builds.
    /// </summary>
    private static void ApplyHostCallbacks(IRibbonCommandRegistry registry, AvaloniaRibbonHostCallbacks callbacks)
    {
        void Bind(string avaloniaId, Action? action)
        {
            if (action is not null)
                Register(registry, avaloniaId, CreateRelayCommand(avaloniaId, action));
        }

        IRibbonCommand CreateRelayCommand(string avaloniaId, Action action)
        {
            var canonicalId = AvaloniaCommandIdAdapter.ToCanonical(avaloniaId);
            if (callbacks.ExtraCommandStates?.TryGetValue(avaloniaId, out var state) == true ||
                callbacks.ExtraCommandStates?.TryGetValue(canonicalId, out state) == true)
            {
                return new StatefulRelayRibbonCommand(action, state);
            }

            return new ActionRibbonCommand(action);
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
            Register(registry, "home.fontSize", new ValueRibbonCommand(setFontSize));
        if (callbacks.SetFontName is { } setFontName)
            Register(registry, "home.fontName", new ValueRibbonCommand(setFontName));
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
        if (callbacks.SetNumberFormat is { } setNumberFormat)
            Register(registry, "home.numberFormat", new ValueRibbonCommand(setNumberFormat));

        if (callbacks.ExtraCommands is { } extra)
            foreach (var (id, action) in extra)
                Bind(id, action);
    }

    /// <summary>
    /// Wires the Insert chart-type buttons and their chart-type menu items to
    /// <see cref="InsertChartCommandFactory"/>. Any canonical command id the factory maps to a
    /// <see cref="ChartType"/> gets an <see cref="InsertChartRibbonCommand"/>; unmapped ids keep their
    /// no-op registration. The factory recognizes the shared definition's descriptive chart labels
    /// (e.g. <c>Column Chart</c>, <c>Line Chart</c>) directly.
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

    /// <summary>Enumerates every control + menu command id in a definition (the registry's seeding set).</summary>
    public static IEnumerable<RibbonCommandId> EnumerateCommandIds(RibbonDefinition definition)
    {
        foreach (var tab in definition.Tabs)
        foreach (var id in EnumerateCommandIds(tab))
            yield return id;
    }

    private static IEnumerable<RibbonCommandId> EnumerateCommandIds(RibbonTab tab)
    {
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
}
