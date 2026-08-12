using Avalonia.Controls;
using Free.Shared.Ribbon;
using FreeX.App.Avalonia.Charts;
using FreeX.App.Services;
using FreeX.App.Services.Ribbon;
using FreeX.App.Presentation.DrawingUI;
using FreeX.App.Presentation.Backstage;
using FreeX.App.Presentation.Charts.Editing;
using FreeX.App.Presentation.Ribbon;
using FreeX.Core.Model;
using Free.Shared.Ribbon.Avalonia;
using Free.Shared.Theme;
using FreeX.Ribbon.Definitions;

namespace FreeX.App.Avalonia.Ribbon;

/// <summary>
/// Provides the canonical renderer-neutral key-tip catalog to the Avalonia shell. Input remains
/// character-by-character while the window retains its native state machine and control activation.
/// </summary>
internal static class AvaloniaRibbonKeyTipRoutes
{
    private static readonly Lazy<FreeXRibbonKeyTipRouteCatalog> Routes =
        new(() => FreeXRibbonKeyTipRoutePlanner.Build(AvaloniaRibbonComposition.BuildDefinition()));

    internal static FreeXRibbonKeyTipMatch Match(string input) => Routes.Value.Match(input);

    internal static bool TryResolveExact(string input, out FreeXRibbonKeyTipRoute route) =>
        Routes.Value.TryResolveExact(input, out route);
}

/// <summary>
/// Builds the FreeX Avalonia ribbon from the SAME canonical, single-source ribbon definition the WPF app
/// consumes (<see cref="FreeXRibbon.Build"/> in the shared <c>FreeX.Ribbon.Definitions</c> project), rendered
/// via <see cref="AvaloniaRibbonRenderer"/>. There is no longer a separate Avalonia ribbon definition: one
/// declarative definition drives both apps.
///
/// The shell registers endpoint delegates against canonical ids obtained from the shared definition, so the
/// renderer and host cannot drift into separate command inventories. Bold /
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
    public static (Control Ribbon, Action RefreshToggleStates, IRibbonCommandRegistry Registry) Build(
        Func<WorkbookSession?> session,
        Action<string> setStatus,
        AvaloniaRibbonHostCallbacks callbacks,
        IRibbonContextSource? contextSource,
        IRibbonStateStore? stateStore = null)
    {
        var registry = AvaloniaRibbonComposition.BuildRegistry(session, setStatus, callbacks);
        var definition = AvaloniaRibbonComposition.BuildDefinition();
        var palette = RibbonVisualPalette.FromTheme(App.ActiveTheme);
        var ribbon = AvaloniaRibbonRenderer.BuildRibbon(
            definition,
            registry,
            contextSource,
            palette: palette,
            onFileTabSelected: callbacks.Backstage,
            stateStore: stateStore);
        return (
            ribbon,
            () => AvaloniaRibbonRenderer.SyncToggleStates(ribbon, registry, palette, stateStore),
            registry);
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

    /// <summary>Insert ▸ PivotTable — open the Insert PivotTable dialog for the selection.</summary>
    public Action? InsertPivotTable { get; init; }

    /// <summary>Insert ▸ Picture — choose an image file and place it on the sheet.</summary>
    public Action? InsertPicture { get; init; }

    /// <summary>Insert ▸ Shapes — insert the selected gallery shape at the active cell.</summary>
    public Action<DrawingShapeKind>? InsertShape { get; init; }

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

    /// <summary>Applies a Page Layout Scale Width combo selection.</summary>
    public Action<string?>? SetPageLayoutScaleWidth { get; init; }

    /// <summary>Applies a Page Layout Scale Height combo selection.</summary>
    public Action<string?>? SetPageLayoutScaleHeight { get; init; }

    /// <summary>Applies a Page Layout Scale Percent combo selection.</summary>
    public Action<string?>? SetPageLayoutScalePercent { get; init; }

    /// <summary>
    /// Additional command-id → action bindings for parameterized menu items the named callbacks do not
    /// cover (e.g. the Number Format dropdown's General/Number/Currency/Date/Percent items, or the Fill
    /// Color dropdown's swatch items). Applied after the named callbacks; each id overrides its no-op.
    /// Keys are canonical ids emitted by the shared definition.
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

/// <summary>Value-bearing host callback with live state for editable combo synchronization.</summary>
internal sealed class StatefulValueRibbonCommand : IRibbonStatefulCommand
{
    private readonly Action<string?> _execute;
    private readonly Func<RibbonCommandState> _getState;

    public StatefulValueRibbonCommand(Action<string?> execute, Func<RibbonCommandState> getState)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _getState = getState ?? throw new ArgumentNullException(nameof(getState));
    }

    public void Execute(RibbonCommandContext context) => _execute(context.SelectedValue);

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
/// <see cref="ChartCommandWorkflowPlanner"/>). On success the host refresh hook redraws the grid so the new
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

        var command = ChartCommandWorkflowPlanner.BuildEmbeddedChartCommand(
            session.ActiveSheet,
            session.SelectedRange,
            _chartType);
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
/// the canonical ids the definition emits; shell handlers retain only endpoint delegates and state mappings.
/// </summary>
internal static class AvaloniaRibbonComposition
{
    internal sealed record SurfaceRow(
        string RowId,
        string TabId,
        string TabHeader,
        string? ActivationKey,
        string GroupId,
        string GroupHeader,
        string Kind,
        string Label,
        string? KeyTip,
        RibbonCommandId CommandId,
        string? ParentCommandId,
        string MenuPath);

    /// <summary>The canonical, single-source ribbon definition shared with the WPF app.</summary>
    public static RibbonDefinition BuildDefinition()
    {
        var definition = FreeXRibbon.Build();
        var numberFormatCommandId = FreeXRibbonCommandCatalog.GetRequired("Number Format");
        var numberFormatLabels = HomeNumberFormatDropdownPlanner.Options
            .Select(option => option.Label)
            .ToArray();
        var tabs = definition.Tabs.Select(tab => tab with
        {
            Groups = tab.Groups.Select(group => group with
            {
                Controls = group.Controls.Select(control =>
                {
                    if (control is RibbonComboBox combo &&
                        combo.CommandId == numberFormatCommandId)
                        return combo with { Items = numberFormatLabels };

                    return string.Equals(control.CommandId.Value, "Shapes", StringComparison.Ordinal)
                        ? CreateShapeGallerySplitButton(control)
                        : control;
                }).ToArray(),
            }).ToArray(),
        }).ToArray();
        return definition with
        {
            Tabs = tabs,
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
        foreach (var id in FreeXRibbonCommandCatalog.Enumerate(definition))
            registry.Register(id, EmptyRibbonCommand.Instance);

        // Override the representative formatting toggles with the shared, platform-neutral commands so the
        // Avalonia ribbon performs real edits (the same WorkbookSession logic the WPF host runs).
        Register(registry, "Bold", WorkbookFormatRibbonCommands.Bold(session, ApplyStatus(setStatus, "Bold")));
        Register(registry, "Italic", WorkbookFormatRibbonCommands.Italic(session, ApplyStatus(setStatus, "Italic")));
        Register(registry, "Underline", WorkbookFormatRibbonCommands.Underline(session, ApplyStatus(setStatus, "Underline")));

        // Override the Insert ▸ Charts controls with a real insert action: each canonical id the factory maps
        // to a ChartType gets an InsertChartRibbonCommand; unmapped ids keep their NoOp registration.
        RegisterChartCommands(registry, session, setStatus);

        // Override the controls whose behavior lives in the Avalonia shell (dialogs / selection commands)
        // with host callbacks, so the declarative ribbon invokes the same handlers as the native menus.
        ApplyHostCallbacks(registry, callbacks);
        return registry;
    }

    private static void Register(IRibbonCommandRegistry registry, string canonicalId, IRibbonCommand command) =>
        registry.Register(FreeXRibbonCommandCatalog.GetRequired(canonicalId), command);

    /// <summary>
    /// Binds each non-null host callback to its canonical command id(s) via an <see cref="ActionRibbonCommand"/>,
    /// replacing the no-op registration. Null callbacks leave the no-op so the smoke harness still builds.
    /// </summary>
    private static void ApplyHostCallbacks(IRibbonCommandRegistry registry, AvaloniaRibbonHostCallbacks callbacks)
    {
        void Bind(string canonicalId, Action? action)
        {
            if (action is not null)
                Register(registry, canonicalId, CreateRelayCommand(canonicalId, action));
        }

        IRibbonCommand CreateRelayCommand(string canonicalId, Action action)
        {
            if (callbacks.ExtraCommandStates?.TryGetValue(canonicalId, out var state) == true)
            {
                return new StatefulRelayRibbonCommand(action, state);
            }

            return new ActionRibbonCommand(action);
        }

        Bind("Text to Columns", callbacks.OpenTextToColumns);
        Bind("Consolidate", callbacks.OpenConsolidate);
        Bind("Table", callbacks.InsertTable);
        Bind("Format as Table", callbacks.InsertTable);
        Bind("Conditional Formatting", callbacks.ConditionalFormatting);
        Bind("PivotTable", callbacks.InsertPivotTable);
        Bind("Pictures", callbacks.InsertPicture);
        if (callbacks.InsertShape is { } insertShape)
        {
            Bind("Shapes", () => insertShape(DrawingInsertionPlanner.DefaultShape));
            foreach (var item in DrawingInsertionPlanner.ShapeItems)
            {
                var kind = item.Kind;
                registry.Register(
                    new RibbonCommandId(GetShapeCommandId(kind)),
                    new ActionRibbonCommand(() => insertShape(kind)));
            }
        }
        Bind("Text Box", callbacks.InsertTextBox);
        Bind("Format Painter", callbacks.FormatPainter);

        if (callbacks.SetFontSize is { } setFontSize)
            Register(registry, "Font Size", new ValueRibbonCommand(setFontSize));
        if (callbacks.SetFontName is { } setFontName)
            Register(registry, "Font", new ValueRibbonCommand(setFontName));
        Bind("Sort A to Z#SortAscButton_Click", callbacks.SortAscending);
        Bind("Sort Z to A#SortDescButton_Click", callbacks.SortDescending);
        Bind("Filter#FilterButton_Click", callbacks.ToggleFilter);
        Bind("Data Validation#ValidationButton_Click", callbacks.DataValidation);

        Bind("Cut", callbacks.Cut);
        Bind("Copy", callbacks.Copy);
        Bind("Paste", callbacks.Paste);
        Bind("Align Left", callbacks.AlignLeft);
        Bind("Center", callbacks.AlignCenter);
        Bind("Align Right", callbacks.AlignRight);
        Bind("Wrap Text", callbacks.WrapText);
        Bind("Merge & Center", callbacks.MergeAndCenter);
        Bind("Accounting Number Format", callbacks.CurrencyFormat);
        Bind("Percent Style", callbacks.PercentFormat);
        Bind("Comma Style", callbacks.CommaStyle);
        if (callbacks.SetNumberFormat is { } setNumberFormat)
            Register(registry, "Number Format", new ValueRibbonCommand(setNumberFormat));

        if (callbacks.ExtraCommands is { } extra)
            foreach (var (id, action) in extra)
                Bind(id, action);

        // Page Layout scale controls carry a selected value. Register these after the generic action map
        // so the value-aware route wins over the Page Setup dialog fallback for the same command ids.
        if (callbacks.SetPageLayoutScaleWidth is { } setScaleWidth)
            Register(registry, "Scale Width", CreateStatefulValueRelayCommand("Scale Width", setScaleWidth));
        if (callbacks.SetPageLayoutScaleHeight is { } setScaleHeight)
            Register(registry, "Scale Height", CreateStatefulValueRelayCommand("Scale Height", setScaleHeight));
        if (callbacks.SetPageLayoutScalePercent is { } setScalePercent)
            Register(registry, "Scale Percent", CreateStatefulValueRelayCommand("Scale Percent", setScalePercent));

        IRibbonCommand CreateStatefulValueRelayCommand(string canonicalId, Action<string?> action)
        {
            if (callbacks.ExtraCommandStates?.TryGetValue(canonicalId, out var state) == true)
            {
                return new StatefulValueRibbonCommand(action, state);
            }

            return new ValueRibbonCommand(action);
        }
    }

    internal static string GetShapeCommandId(DrawingShapeKind kind) =>
        DrawingInsertionPlanner.GetRibbonCommandId(kind);

    private static RibbonControl CreateShapeGallerySplitButton(RibbonControl source) =>
        new RibbonSplitButton(source.CommandId, source.Label, new RibbonMenu(
            DrawingInsertionPlanner.ShapeGroups.Select(group => new RibbonMenuItem(
                group.Label,
                CommandId: null,
                KeyTip: group.KeyTip,
                Children: group.Items.Select(item => new RibbonMenuItem(
                    item.Label,
                    new RibbonCommandId(GetShapeCommandId(item.Kind)),
                    item.KeyTip)).ToArray())).ToArray()))
        {
            KeyTip = source.KeyTip,
            Icon = source.Icon,
            PreferredLayout = source.PreferredLayout,
            TooltipTitle = source.TooltipTitle,
            TooltipDescription = source.TooltipDescription,
        };

    /// <summary>
    /// Wires the Insert chart-type buttons and their chart-type menu items to
    /// <see cref="ChartCommandWorkflowPlanner"/>. Any canonical command id the workflow maps to a
    /// <see cref="ChartType"/> gets an <see cref="InsertChartRibbonCommand"/>; unmapped ids keep their
    /// no-op registration. The factory recognizes the shared definition's descriptive chart labels
    /// (e.g. <c>Column Chart</c>, <c>Line Chart</c>) directly.
    /// </summary>
    private static void RegisterChartCommands(
        IRibbonCommandRegistry registry,
        Func<WorkbookSession?> session,
        Action<string> setStatus)
    {
        foreach (var id in FreeXRibbonCommandCatalog.Enumerate(BuildDefinition()))
        {
            if (ChartCommandWorkflowPlanner.ChartTypeForRibbonCommand(id.Value) is not { } chartType)
                continue;
            registry.Register(id, new InsertChartRibbonCommand(session, chartType, setStatus));
        }
    }

    /// <summary>Builds a post-apply callback that redraws the shell and reports the outcome on the status bar.</summary>
    private static Action<WorkbookCellEditResult, bool> ApplyStatus(Action<string> setStatus, string label) =>
        (result, on) => setStatus(result.Success
            ? $"{label} {(on ? "on" : "off")}"
            : result.ErrorMessage ?? $"{label} failed.");

    /// <summary>
    /// Enumerates every visible command placement, preserving duplicate command ids and nested menu paths.
    /// This is the runtime counterpart of the generated parity surface catalog and is intentionally row based:
    /// two controls which dispatch the same command are still two interactions that must render and route.
    /// </summary>
    public static IEnumerable<SurfaceRow> EnumerateSurfaceRows(RibbonDefinition definition)
    {
        foreach (var tab in definition.Tabs)
        foreach (var group in tab.Groups)
        for (var controlIndex = 0; controlIndex < group.Controls.Count; controlIndex++)
        {
            var control = group.Controls[controlIndex];
            if (!string.IsNullOrEmpty(control.CommandId.Value))
            {
                yield return new SurfaceRow(
                    $"{tab.Id}/{group.Id}/{controlIndex}",
                    tab.Id,
                    tab.Header,
                    tab.Context?.ActivationKey,
                    group.Id,
                    group.Header,
                    control.GetType().Name,
                    control.Label,
                    control.KeyTip,
                    control.CommandId,
                    ParentCommandId: null,
                    MenuPath: "");
            }

            var menu = control switch
            {
                RibbonSplitButton split => split.Menu,
                RibbonDropdown dropdown => dropdown.Menu,
                _ => null,
            };
            if (menu is null)
                continue;

            foreach (var row in EnumerateMenuSurfaceRows(
                         tab,
                         group,
                         control,
                         controlIndex,
                         menu.Items,
                         parentPath: ""))
                yield return row;
        }
    }

    private static IEnumerable<SurfaceRow> EnumerateMenuSurfaceRows(
        RibbonTab tab,
        RibbonGroup group,
        RibbonControl parent,
        int controlIndex,
        IReadOnlyList<RibbonMenuItem> items,
        string parentPath)
    {
        for (var itemIndex = 0; itemIndex < items.Count; itemIndex++)
        {
            var item = items[itemIndex];
            var itemPath = string.IsNullOrEmpty(parentPath)
                ? itemIndex.ToString(System.Globalization.CultureInfo.InvariantCulture)
                : $"{parentPath}.{itemIndex}";
            var labelPath = string.IsNullOrEmpty(parentPath)
                ? item.Header
                : $"{parentPath}/{item.Header}";

            if (item.Kind != RibbonMenuItemKind.Separator && item.CommandId is { } commandId &&
                !string.IsNullOrEmpty(commandId.Value))
            {
                yield return new SurfaceRow(
                    $"{tab.Id}/{group.Id}/{controlIndex}/menu/{itemPath}",
                    tab.Id,
                    tab.Header,
                    tab.Context?.ActivationKey,
                    group.Id,
                    group.Header,
                    nameof(RibbonMenuItem),
                    item.Header,
                    item.KeyTip,
                    commandId,
                    parent.CommandId.Value,
                    labelPath);
            }

            foreach (var row in EnumerateMenuSurfaceRows(
                         tab,
                         group,
                         parent,
                         controlIndex,
                         item.Children,
                         itemPath))
                yield return row;
        }
    }

}
