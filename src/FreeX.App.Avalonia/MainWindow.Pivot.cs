using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using FreeX.App.Presentation.PivotUI;
using FreeX.Core.Commands;
using FreeX.Core.Model;
using Free.Shared.Shell.Avalonia;

using AvaloniaGrid = Avalonia.Controls.Grid;
using AvaloniaHorizontalAlignment = Avalonia.Layout.HorizontalAlignment;
using AvaloniaVerticalAlignment = Avalonia.Layout.VerticalAlignment;

namespace FreeX.App.Avalonia;

/// <summary>
/// PivotTable field-list pane + header dropdown menus for the Avalonia/macOS shell. When the active cell
/// sits inside a pivot the pane docks on the right and shows the available-field pool plus the four layout
/// buckets (rows/columns/filters/values) built by <see cref="PivotFieldListPaneBuilder"/>, with a caption
/// search box. Fields are dragged between buckets (pointer-capture gesture) and removed via an x; each
/// placed field also carries a "▾" dropdown mirroring the desktop header dropdown
/// (<see cref="PivotHeaderDropdownMenuBuilder"/>). All mutations route through the shared pivot mutation
/// commands (<see cref="ConfigurePivotTableLayoutCommand"/>/<see cref="ConfigurePivotTableViewCommand"/>) via
/// the session's command path, then refresh the grid and the pane. The non-UI mapping (validated drop →
/// command, menu action → command) lives in <see cref="PivotFieldLayoutPlanner"/> /
/// <see cref="PivotHeaderCommandPlanner"/> so it can be unit-tested without a running app.
/// </summary>
public sealed partial class MainWindow
{
    // WPF PivotFieldListPane: Width=320, Background=#F7F7F7, BorderBrush=#C8C8C8 on the leading edge.
    private const double PivotPaneWidth = 320;
    private const double PivotPaneControlHeight = 24;

    private static readonly IBrush PivotPaneBackground = Brush(0xF7, 0xF7, 0xF7);
    private static readonly IBrush PivotPaneBorderBrush = Brush(0xC8, 0xC8, 0xC8);
    private static readonly IBrush PivotBucketBackground = Brushes.White;
    // The WPF ListBox border and selected-row fill.
    private static readonly IBrush PivotListBorderBrush = Brush(0xAB, 0xAD, 0xB3);
    private static readonly IBrush PivotFieldSelectedBackground = Brush(0xCB, 0xE8, 0xF6);
    private static readonly IBrush PivotDropHighlight = Brush(225, 244, 242);

    private readonly Border _pivotFieldPaneHost = new();
    private TextBox? _pivotFieldPaneSearchBox;

    // Signature of the pivot the pane currently reflects, so the pane only rebuilds when the pivot identity
    // or its layout actually changes (cheap guard against rebuilding on every selection move).
    private string? _pivotPaneSignature;
    private string _pivotPaneSearchText = string.Empty;
    private int _pivotFieldPaneBuildCount;

    // The field currently being dragged within the pane (pointer-capture gesture), or null when idle.
    private PivotPaneDragItem? _pivotPaneDragItem;
    private readonly List<PivotDropZone> _pivotDropZones = [];

    // The rendered field rows and the row the move buttons act on.
    private readonly List<(Border Row, PivotFieldListItemModel Field)> _pivotPaneFieldRows = [];
    private PivotFieldListItemModel? _pivotPaneSelectedField;

    private Control BuildPivotFieldPaneChrome()
    {
        _pivotFieldPaneHost.Width = PivotPaneWidth;
        _pivotFieldPaneHost.Background = PivotPaneBackground;
        _pivotFieldPaneHost.BorderBrush = PivotPaneBorderBrush;
        _pivotFieldPaneHost.BorderThickness = new Thickness(1, 0, 0, 0);
        _pivotFieldPaneHost.Focusable = true;
        _pivotFieldPaneHost.IsVisible = false;
        AutomationProperties.SetAutomationId(_pivotFieldPaneHost, "PivotFieldListPane");
        AutomationProperties.SetName(_pivotFieldPaneHost, UiText.Get("PivotLoc_FieldsPaneTitle"));
        return _pivotFieldPaneHost;
    }

    /// <summary>
    /// Shows the field pane when the active cell is inside a pivot and rebuilds it only when the pivot's
    /// identity/layout signature changes; hides it otherwise. Called from <c>RefreshShell</c> on the same
    /// events that refresh the grid/ribbon.
    /// </summary>
    private void RefreshPivotFieldPane()
    {
        var pivot = PivotSourceContext.FindActivePivot(_session.ActiveSheet, _session.ActiveCell);
        // Honor the Analyze ▸ Field List toggle: if the user explicitly closed the pane, keep it hidden
        // even while a pivot stays active (so the choice survives selection moves).
        if (pivot is null || _pivotFieldPaneUserHidden)
        {
            if (_pivotFieldPaneHost.IsVisible)
            {
                _pivotFieldPaneHost.IsVisible = false;
                _pivotFieldPaneHost.Child = null;
                _pivotFieldPaneSearchBox = null;
                _pivotPaneSignature = null;
                _pivotPaneSearchText = string.Empty;
                _pivotPaneSelectedField = null;
                _pivotPaneFieldRows.Clear();
            }

            ObservePivotRuntimeState("pane-hidden");
            return;
        }

        var headers = PivotSourceContext.ReadHeaders(_session.Workbook, pivot);
        var signature = BuildPivotPaneSignature(pivot);
        if (_pivotFieldPaneHost.IsVisible && signature == _pivotPaneSignature)
            return;

        _pivotPaneSignature = signature;
        _pivotFieldPaneHost.Child = BuildPivotFieldPaneBody(pivot, headers);
        _pivotFieldPaneHost.IsVisible = true;
        ObservePivotRuntimeState("pane-visible");
    }

    private void ObservePivotRuntimeState(string stage)
    {
        var observer = _pivotRuntimeObserver;
        if (observer is null)
            return;

        try
        {
            var sheet = _session.ActiveSheet;
            var activeCell = _session.ActiveCell;
            var pivot = PivotSourceContext.FindActivePivot(sheet, activeCell);
            observer(new PivotRuntimeObservation(
                stage,
                sheet.Name,
                sheet.Id.ToString(),
                activeCell.Sheet.ToString(),
                activeCell.Row,
                activeCell.Col,
                _session.CurrentFilePath,
                _session.Workbook.Name,
                _session.Workbook.Sheets
                    .Select(item => new PivotRuntimeSheetObservation(item.Name, item.PivotTables.Count))
                    .ToArray(),
                sheet.PivotTables.Count,
                sheet.PivotTables
                    .Select(item => new PivotRuntimeTableObservation(
                        item.Name,
                        item.TargetRange.Start.ToA1(),
                        item.TargetRange.End.ToA1(),
                        item.LastRenderedRange?.Start.ToA1(),
                        item.LastRenderedRange?.End.ToA1()))
                    .ToArray(),
                pivot?.Name,
                _pivotFieldPaneHost.IsVisible,
                _pivotFieldPaneHost.Bounds.Width,
                _pivotFieldPaneUserHidden));
        }
        catch
        {
            // Optional observers must never affect worksheet behavior.
        }
    }

    // Identity + ordered field membership; a layout change (drag, sort) shifts the signature and rebuilds.
    private static string BuildPivotPaneSignature(PivotTableModel pivot)
    {
        static string Axis(IEnumerable<PivotFieldModel> fields) =>
            string.Join(",", fields.Select(field => field.SourceFieldIndex));

        var data = string.Join(",", pivot.DataFields.Select(field => $"{field.SourceFieldIndex}:{field.SummaryFunction}"));
        var sorts = string.Join(",", pivot.Sorts.Select(sort => $"{sort.Target}:{sort.FieldIndex}:{sort.DataFieldIndex}:{sort.Direction}"));
        return string.Join(
            "|",
            pivot.Name,
            Axis(pivot.RowFields),
            Axis(pivot.ColumnFields),
            Axis(pivot.PageFields),
            data,
            sorts,
            pivot.LabelFilters.Count,
            pivot.ValueFilters.Count);
    }

    private Control BuildPivotFieldPaneBody(PivotTableModel pivot, IReadOnlyList<string> headers)
    {
        _pivotFieldPaneBuildCount++;
        _pivotDropZones.Clear();
        _pivotPaneFieldRows.Clear();
        var model = PivotFieldListPaneBuilder.Build(pivot, headers);

        // WPF: Grid Margin=10 with rows Auto, Auto, Auto, 0.85*, Auto, 1.25*.
        var layout = new AvaloniaGrid
        {
            Margin = new Thickness(10),
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto,0.85*,Auto,1.25*"),
        };

        AddGridChild(layout, BuildPivotPaneHeader(), 0, 0);

        // WPF: FontSize=12 Margin=0,8,0,3.
        AddGridChild(
            layout,
            new TextBlock
            {
                Text = UiText.Get("MainWindow_Text_ChooseFieldsToAddToReport"),
                FontSize = 12,
                Foreground = HeaderForeground,
                Margin = new Thickness(0, 8, 0, 3),
            },
            1,
            0);

        // WPF: Height=24 Margin=0,0,0,4, vertically centered text.
        var searchBox = new TextBox
        {
            Text = _pivotPaneSearchText,
            Height = PivotPaneControlHeight,
            MinHeight = PivotPaneControlHeight,
            FontSize = 12,
            Padding = new Thickness(4, 0, 4, 0),
            Margin = new Thickness(0, 0, 0, 4),
            VerticalContentAlignment = AvaloniaVerticalAlignment.Center,
            CornerRadius = new CornerRadius(0),
            Background = PivotBucketBackground,
            BorderBrush = PivotListBorderBrush,
            BorderThickness = new Thickness(1),
        };
        _pivotFieldPaneSearchBox = searchBox;
        AutomationProperties.SetAutomationId(searchBox, "PivotFieldListSearchBox");
        AutomationProperties.SetName(searchBox, UiText.Get("MainWindow_AutomationName_SearchPivotTableFields"));
        searchBox.TextChanged += (_, _) =>
        {
            var searchText = searchBox.Text ?? string.Empty;
            // Avalonia can raise TextChanged when a newly-created TextBox is attached and its
            // template initializes, even though Text was assigned before this handler. Rebuilding
            // for that unchanged notification creates another TextBox and an unbounded pane loop.
            if (string.Equals(searchText, _pivotPaneSearchText, StringComparison.Ordinal))
                return;

            _pivotPaneSearchText = searchText;
            _pivotFieldPaneHost.Child = BuildPivotFieldPaneBody(pivot, headers);
        };
        AddGridChild(layout, searchBox, 2, 0);

        AddGridChild(layout, BuildPivotAvailableBucket(pivot, headers), 3, 0);
        AddGridChild(layout, BuildPivotMoveButtons(pivot, headers), 4, 0);
        AddGridChild(layout, BuildPivotAreaGrid(pivot, headers, model), 5, 0);
        return layout;
    }

    // WPF: a 22x22 close button docked right of a SemiBold 14px title.
    private Control BuildPivotPaneHeader()
    {
        var closeButton = new Button { Content = "X" };
        AvaloniaCompactDialogChrome.ApplyButton(
            closeButton,
            AvaloniaCompactDialogChrome.WindowsStyle,
            minWidth: 0,
            fixedHeight: false);
        closeButton.Width = 22;
        closeButton.Height = 22;
        closeButton.MinHeight = 0;
        closeButton.Padding = new Thickness(0);
        AutomationProperties.SetAutomationId(closeButton, "PivotFieldListCloseBtn");
        AutomationProperties.SetName(closeButton, UiText.Get("MainWindow_AutomationName_ClosePivotTableFields"));
        // The pane is only visible while it is not user-hidden, so the toggle closes it and keeps
        // the ribbon field-list state in step.
        closeButton.Click += (_, _) => TogglePivotFieldList();
        DockPanel.SetDock(closeButton, Dock.Right);

        var title = new TextBlock
        {
            Text = UiText.Get("PivotLoc_FieldsPaneTitle"),
            FontWeight = FontWeight.SemiBold,
            FontSize = 14,
            Foreground = HeaderForeground,
            VerticalAlignment = AvaloniaVerticalAlignment.Center,
        };
        return new DockPanel { LastChildFill = true, Children = { closeButton, title } };
    }

    // WPF lists every source field with a check box that is ticked while the field is in the layout.
    private Control BuildPivotAvailableBucket(PivotTableModel pivot, IReadOnlyList<string> headers)
    {
        var available = PivotFieldListPaneBuilder.FilterAvailableFields(
            PivotFieldListPaneBuilder.BuildAvailableFields(headers, PivotFieldLayoutPlanner.Capture(pivot)),
            _pivotPaneSearchText);
        var fields = available
            .Select(item => new PivotFieldListItemModel(
                item.SourceFieldIndex,
                item.Caption,
                PivotFieldBucket.Available))
            .ToList();
        var body = new StackPanel();
        for (var index = 0; index < fields.Count; index++)
        {
            body.Children.Add(BuildPivotFieldChip(
                pivot,
                headers,
                fields[index],
                showMenu: false,
                isChecked: available[index].IsChecked));
        }

        var container = BuildPivotBucketContainer(PivotFieldBucket.Available, fields, body);
        container.MinHeight = 96;
        AutomationProperties.SetAutomationId(container, "PivotAvailableFieldsList");
        AutomationProperties.SetName(container, UiText.Get("MainWindow_AutomationName_AvailablePivotTableFields"));
        return container;
    }

    private Border BuildPivotBucket(
        PivotTableModel pivot,
        IReadOnlyList<string> headers,
        PivotFieldListBucketModel bucket)
    {
        var body = new StackPanel();
        foreach (var field in bucket.Fields)
            body.Children.Add(BuildPivotFieldChip(pivot, headers, field, showMenu: true));

        return BuildPivotBucketContainer(bucket.Bucket, bucket.Fields, body);
    }

    // A WPF ListBox surface: white, square, one-pixel #ABADB3 border, scrolling its rows.
    private Border BuildPivotBucketContainer(
        PivotFieldBucket bucketKind,
        IReadOnlyList<PivotFieldListItemModel> fields,
        StackPanel body)
    {
        var container = new Border
        {
            Background = PivotBucketBackground,
            BorderBrush = PivotListBorderBrush,
            BorderThickness = new Thickness(1),
            Child = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = body,
            },
        };

        _pivotDropZones.Add(new PivotDropZone(container, bucketKind, body.Children.ToList(), fields));
        return container;
    }

    // WPF: a five-column UniformGrid (Margin=0,6,0,6) of buttons with Margin=1 and MinHeight=24 that
    // act on the selected field.
    private Control BuildPivotMoveButtons(PivotTableModel pivot, IReadOnlyList<string> headers)
    {
        var buttons = new UniformGrid { Columns = 5, Margin = new Thickness(0, 6, 0, 6) };
        buttons.Children.Add(CreatePivotPaneMoveButton(
            "MainWindow_Content_Rows",
            "MainWindow_AutomationName_AddSelectedPivotTableFieldToRows",
            () => MoveSelectedPivotPaneField(pivot, headers, PivotFieldBucket.Rows)));
        buttons.Children.Add(CreatePivotPaneMoveButton(
            "MainWindow_Content_Columns",
            "MainWindow_AutomationName_AddSelectedPivotTableFieldToColumns",
            () => MoveSelectedPivotPaneField(pivot, headers, PivotFieldBucket.Columns)));
        buttons.Children.Add(CreatePivotPaneMoveButton(
            "MainWindow_Content_Values",
            "MainWindow_AutomationName_AddSelectedPivotTableFieldToValues",
            () => MoveSelectedPivotPaneField(pivot, headers, PivotFieldBucket.Values)));
        buttons.Children.Add(CreatePivotPaneMoveButton(
            "MainWindow_Content_Filters",
            "MainWindow_AutomationName_AddSelectedPivotTableFieldToFilters",
            () => MoveSelectedPivotPaneField(pivot, headers, PivotFieldBucket.Filters)));
        buttons.Children.Add(CreatePivotPaneMoveButton(
            "MainWindow_Content_Remove",
            "MainWindow_AutomationName_RemoveSelectedPivotTableField",
            () => MoveSelectedPivotPaneField(pivot, headers, PivotFieldBucket.Available)));
        return buttons;
    }

    private static Button CreatePivotPaneMoveButton(string contentKey, string automationNameKey, Action action)
    {
        var button = new Button
        {
            Content = UiText.Get(contentKey),
            Margin = new Thickness(1),
            HorizontalAlignment = AvaloniaHorizontalAlignment.Stretch,
        };
        AvaloniaCompactDialogChrome.ApplyButton(
            button,
            AvaloniaCompactDialogChrome.WindowsStyle,
            minWidth: 0,
            fixedHeight: false);
        button.MinHeight = PivotPaneControlHeight;
        button.Padding = new Thickness(2, 1, 2, 1);
        AutomationProperties.SetName(button, UiText.Get(automationNameKey));
        button.Click += (_, _) => action();
        return button;
    }

    // WPF: Filters and Columns above Rows and Values, an 8px gutter, a SemiBold heading over each
    // list (Margin=0,0,0,3 on the first row, 0,8,0,3 on the second) and MinHeight=80 lists.
    private Control BuildPivotAreaGrid(
        PivotTableModel pivot,
        IReadOnlyList<string> headers,
        PivotFieldListPaneModel model)
    {
        var grid = new AvaloniaGrid
        {
            RowDefinitions = new RowDefinitions("Auto,*,Auto,*"),
            ColumnDefinitions = new ColumnDefinitions("*,8,*"),
        };
        AddPivotArea(grid, pivot, headers, model.Filters, "MainWindow_Text_Filters", "PivotFiltersList", "MainWindow_AutomationName_PivotTableFiltersArea", 0, 0);
        AddPivotArea(grid, pivot, headers, model.Columns, "MainWindow_Text_Columns", "PivotColumnsList", "MainWindow_AutomationName_PivotTableColumnsArea", 0, 2);
        AddPivotArea(grid, pivot, headers, model.Rows, "MainWindow_Text_Rows", "PivotRowsList", "MainWindow_AutomationName_PivotTableRowsArea", 2, 0);
        AddPivotArea(grid, pivot, headers, model.Values, "MainWindow_Text_Values", "PivotValuesList", "MainWindow_AutomationName_PivotTableValuesArea", 2, 2);
        return grid;
    }

    private void AddPivotArea(
        AvaloniaGrid grid,
        PivotTableModel pivot,
        IReadOnlyList<string> headers,
        PivotFieldListBucketModel bucket,
        string headingKey,
        string automationId,
        string automationNameKey,
        int headingRow,
        int column)
    {
        AddGridChild(
            grid,
            new TextBlock
            {
                Text = UiText.Get(headingKey),
                FontWeight = FontWeight.SemiBold,
                FontSize = 12,
                Foreground = HeaderForeground,
                Margin = new Thickness(0, headingRow == 0 ? 0 : 8, 0, 3),
            },
            headingRow,
            column);

        var list = BuildPivotBucket(pivot, headers, bucket);
        list.MinHeight = 80;
        AutomationProperties.SetAutomationId(list, automationId);
        AutomationProperties.SetName(list, UiText.Get(automationNameKey));
        AddGridChild(grid, list, headingRow + 1, column);
    }

    // One list row. An available field carries the WPF check box (ticked while the field is placed);
    // the caption sits beside the box so the row still starts a drag.
    private Control BuildPivotFieldChip(
        PivotTableModel pivot,
        IReadOnlyList<string> headers,
        PivotFieldListItemModel field,
        bool showMenu,
        bool? isChecked = null)
    {
        var caption = new TextBlock
        {
            Text = field.Caption,
            VerticalAlignment = AvaloniaVerticalAlignment.Center,
            Foreground = HeaderForeground,
            FontSize = 12,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };

        var row = new AvaloniaGrid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = GridLength.Auto },
            },
        };
        AddGridChild(row, caption, 0, 1);

        if (isChecked is { } placed)
        {
            var checkBox = new CheckBox
            {
                IsChecked = placed,
                Margin = new Thickness(0, 0, 4, 0),
                VerticalAlignment = AvaloniaVerticalAlignment.Center,
            };
            AvaloniaCompactDialogChrome.ApplyCompactCheckBox(checkBox, AvaloniaCompactDialogChrome.WindowsStyle);
            AutomationProperties.SetName(checkBox, field.Caption);
            ToolTip.SetTip(
                checkBox,
                UiText.Get("MainWindow_TooltipDescription_AddOrRemoveThisFieldFromTheActivePivotTableLayout"));
            checkBox.Click += (_, _) => TogglePivotAvailableField(pivot, headers, field, checkBox.IsChecked == true);
            AddGridChild(row, checkBox, 0, 0);
        }

        if (showMenu)
        {
            var menuButton = new Button
            {
                Content = "▾",
                FontSize = 10,
                Padding = new Thickness(4, 0, 4, 0),
                Margin = new Thickness(2, 0, 0, 0),
                MinHeight = 0,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
            };
            menuButton.Click += (_, _) => ShowPivotHeaderDropdown(pivot, headers, field, menuButton);
            AddGridChild(row, menuButton, 0, 2);
        }

        // A WPF ListBoxItem row: flat, 12px text, about 20px tall.
        var chip = new Border
        {
            Background = field == _pivotPaneSelectedField ? PivotFieldSelectedBackground : Brushes.Transparent,
            Padding = new Thickness(4, 2, 2, 2),
            MinHeight = 20,
            Child = row,
        };
        _pivotPaneFieldRows.Add((chip, field));

        chip.PointerPressed += (_, _) => SelectPivotPaneField(field);
        chip.PointerPressed += (_, e) => BeginPivotFieldDrag(field, e);
        chip.PointerMoved += PivotFieldDrag_PointerMoved;
        chip.PointerReleased += (_, e) => CompletePivotFieldDrag(pivot, headers, e);
        // If the OS revokes pointer capture mid-drag (pane rebuild, context menu, focus loss), abort
        // the drag and reset all visual state so highlighted drop-zone backgrounds are not stuck and
        // _pivotPaneDragItem cannot be acted on by a subsequent stale PointerReleased — mirrors the
        // chart-drag PointerCaptureLost cleanup pattern.
        chip.PointerCaptureLost += (_, _) =>
        {
            // Null _pivotPaneDragItem FIRST so that any PointerReleased that still fires after
            // PointerCaptureLost sees a null drag item and is a no-op.  No explicit Capture(null)
            // needed here — the capture is already gone by the time this handler fires.
            _pivotPaneDragItem = null;
            foreach (var zone in _pivotDropZones)
                zone.Bucket.Background = PivotBucketBackground;
        };
        AttachPivotFieldContextMenu(chip, pivot, headers, field);
        return chip;
    }

    // The selected row is the target of the move buttons, as the selected ListBox item is in WPF.
    private void SelectPivotPaneField(PivotFieldListItemModel field)
    {
        _pivotPaneSelectedField = field;
        foreach (var (row, item) in _pivotPaneFieldRows)
            row.Background = item == field ? PivotFieldSelectedBackground : Brushes.Transparent;
    }

    private void MoveSelectedPivotPaneField(
        PivotTableModel pivot,
        IReadOnlyList<string> headers,
        PivotFieldBucket target)
    {
        if (_pivotPaneSelectedField is not { } field || field.Bucket == target)
            return;

        ApplyPivotFieldDrop(
            pivot,
            headers,
            field.Bucket == PivotFieldBucket.Available
                ? new PivotFieldDropRequest(field.SourceFieldIndex, target)
                : new PivotFieldDropRequest(
                    field.SourceFieldIndex,
                    target,
                    SourceBucket: field.Bucket,
                    SourceItemIndex: field.DataFieldIndex ?? -1));
    }

    // WPF: ticking a field adds it to Values when its source column is numeric and to Rows
    // otherwise; clearing the tick removes it from the layout.
    private void TogglePivotAvailableField(
        PivotTableModel pivot,
        IReadOnlyList<string> headers,
        PivotFieldListItemModel field,
        bool isChecked)
    {
        var target = !isChecked
            ? PivotFieldBucket.Available
            : PivotSourceContext.IsNumericSourceColumn(_session.Workbook, pivot, field.SourceFieldIndex)
                ? PivotFieldBucket.Values
                : PivotFieldBucket.Rows;
        ApplyPivotFieldDrop(pivot, headers, new PivotFieldDropRequest(field.SourceFieldIndex, target));

        // A rejected move leaves the layout unchanged, so the ticks are re-read from the model.
        _pivotPaneSignature = null;
        RefreshPivotFieldPane();
    }

    // ── Pointer-capture drag gesture (no platform DnD dependency) ─────────────
    private void BeginPivotFieldDrag(PivotFieldListItemModel field, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(null).Properties.IsLeftButtonPressed)
            return;

        _pivotPaneDragItem = new PivotPaneDragItem(field.SourceFieldIndex, field.Bucket, field.DataFieldIndex);
        e.Pointer.Capture((IInputElement)e.Source!);
    }

    private void PivotFieldDrag_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (_pivotPaneDragItem is null)
            return;

        // Highlight the bucket and insertion target the pointer currently hovers so the drop is legible.
        var target = ResolvePivotDropTarget(e.GetPosition(_pivotFieldPaneHost));
        foreach (var zone in _pivotDropZones)
            zone.Bucket.Background = target?.Zone == zone ? PivotDropHighlight : PivotBucketBackground;
    }

    private void CompletePivotFieldDrag(
        PivotTableModel pivot,
        IReadOnlyList<string> headers,
        PointerReleasedEventArgs e)
    {
        var drag = _pivotPaneDragItem;
        _pivotPaneDragItem = null;
        e.Pointer.Capture(null);
        foreach (var zone in _pivotDropZones)
            zone.Bucket.Background = PivotBucketBackground;

        if (drag is null)
            return;

        var target = ResolvePivotDropTarget(e.GetPosition(_pivotFieldPaneHost));
        if (target is null ||
            (target.Zone.Kind == PivotFieldBucket.Available && drag.SourceBucket == PivotFieldBucket.Available))
            return;

        ApplyPivotFieldDrop(
            pivot,
            headers,
            new PivotFieldDropRequest(
                drag.SourceFieldIndex,
                target.Zone.Kind,
                AdjustPivotDropIndex(target, drag),
                SourceBucket: drag.SourceBucket,
                SourceItemIndex: drag.SourceItemIndex ?? -1));
    }

    private PivotDropTarget? ResolvePivotDropTarget(Point pointInPane)
    {
        foreach (var zone in _pivotDropZones)
        {
            if (!PivotZoneContains(zone.Bucket, pointInPane))
                continue;

            return new PivotDropTarget(zone, ResolvePivotDropIndex(zone, pointInPane));
        }

        return null;
    }

    private int ResolvePivotDropIndex(PivotDropZone zone, Point pointInPane)
    {
        for (var index = 0; index < zone.Items.Count; index++)
        {
            var topLeft = zone.Items[index].TranslatePoint(default, _pivotFieldPaneHost);
            if (topLeft is not { } origin)
                continue;

            var midpoint = origin.Y + zone.Items[index].Bounds.Height / 2;
            if (pointInPane.Y < midpoint)
                return index;
        }

        return zone.Fields.Count;
    }

    private static int AdjustPivotDropIndex(PivotDropTarget target, PivotPaneDragItem drag)
    {
        if (target.Zone.Kind == PivotFieldBucket.Available || target.Zone.Kind != drag.SourceBucket)
            return target.TargetIndex;

        var sourceIndex = -1;
        for (var index = 0; index < target.Zone.Fields.Count; index++)
        {
            if (target.Zone.Fields[index].SourceFieldIndex == drag.SourceFieldIndex)
            {
                sourceIndex = index;
                break;
            }
        }

        return sourceIndex >= 0 && target.TargetIndex > sourceIndex
            ? target.TargetIndex - 1
            : target.TargetIndex;
    }

    private bool PivotZoneContains(Border bucket, Point pointInPane)
    {
        var topLeft = bucket.TranslatePoint(default, _pivotFieldPaneHost);
        if (topLeft is not { } origin)
            return false;

        var bounds = new Rect(origin, bucket.Bounds.Size);
        return bounds.Contains(pointInPane);
    }

    // ── Apply a validated drop through the shared layout command ──────────────
    private void ApplyPivotFieldDrop(
        PivotTableModel pivot,
        IReadOnlyList<string> headers,
        PivotFieldDropRequest request)
    {
        if (_isOpening || _isSaving)
            return;

        var validator = BuildPivotDragValidator(pivot);
        var dropPlan = PivotFieldLayoutPlanner.PlanDrop(
            PivotFieldLayoutPlanner.Capture(pivot),
            headers,
            request,
            validator);
        if (!dropPlan.Result.IsAllowed)
        {
            ShowEditIssue(dropPlan.Result.RejectionReason ?? UiText.Get("PivotLoc_FieldMoveNotAllowed"));
            return;
        }

        if (dropPlan.Areas is not { } areas)
        {
            ShowEditIssue(UiText.Get("PivotLoc_NeedsValueField"));
            return;
        }

        ApplyPivotApplicationPlan(
            PivotApplication.PlanLayout(
                new PivotApplicationTarget(_session.ActiveSheet, pivot),
                areas));
    }

    // ── Header dropdown menu ──────────────────────────────────────────────────
    private void ShowPivotHeaderDropdown(
        PivotTableModel pivot,
        IReadOnlyList<string> headers,
        PivotFieldListItemModel field,
        Control anchor)
    {
        var target = ResolvePivotHeaderTarget(pivot, headers, field);
        if (target is null)
            return;

        var menuModel = PivotHeaderDropdownMenuBuilder.BuildMenu(pivot, target);
        var validator = BuildPivotDragValidator(pivot);
        var items = new List<Control>();
        foreach (var item in menuModel.Items)
        {
            if (item.IsSeparator)
            {
                items.Add(new Separator());
                continue;
            }

            var menuItem = new MenuItem { Header = item.Label, IsEnabled = item.IsEnabled };
            if (item.IsChecked)
                menuItem.Icon = new TextBlock { Text = "✓" };

            var action = item.Action;
            menuItem.Click += (_, _) => InvokePivotHeaderAction(pivot, headers, target, action, validator);
            items.Add(menuItem);
        }

        // Manual item (checkbox) filter for row/column/page fields.
        if (target.Area is PivotHeaderArea.Row or PivotHeaderArea.Column or PivotHeaderArea.Page)
        {
            var itemFilter = new MenuItem { Header = UiText.Get("PivotLoc_FilterItemsMenu") };
            itemFilter.Click += (_, _) => OpenPivotItemFilter(pivot, headers, target);
            items.Add(new Separator());
            items.Add(itemFilter);
        }

        var menu = new ContextMenu { ItemsSource = items };
        // The WPF menu chrome; a checked row keeps its tick in the icon column.
        AvaloniaContextMenuChrome.Apply(menu);
        menu.Open(anchor);
    }

    /// <summary>
    /// Opens the pivot header dropdown menu given a pre-resolved <see cref="PivotHeaderDropdownTargetModel"/>.
    /// Used by <c>MainWindow.PivotAdornments.cs</c> when a cell-level pivot dropdown button is clicked —
    /// the target was already computed during grid construction so no field-list item is needed.
    /// </summary>
    private void ShowPivotHeaderDropdownFromTarget(
        PivotTableModel pivot,
        PivotHeaderDropdownTargetModel target,
        Control anchor)
    {
        var headers = PivotSourceContext.ReadHeaders(_session.Workbook, pivot);
        var menuModel = PivotHeaderDropdownMenuBuilder.BuildMenu(pivot, target);
        var validator = BuildPivotDragValidator(pivot);
        var items = new List<Control>();
        foreach (var item in menuModel.Items)
        {
            if (item.IsSeparator)
            {
                items.Add(new Separator());
                continue;
            }
            var menuItem = new MenuItem { Header = item.Label, IsEnabled = item.IsEnabled };
            if (item.IsChecked)
                menuItem.Icon = new TextBlock { Text = "✓" };
            var action = item.Action;
            menuItem.Click += (_, _) => InvokePivotHeaderAction(pivot, headers, target, action, validator);
            items.Add(menuItem);
        }
        if (target.Area is PivotHeaderArea.Row or PivotHeaderArea.Column or PivotHeaderArea.Page)
        {
            var itemFilter = new MenuItem { Header = UiText.Get("PivotLoc_FilterItemsMenu") };
            itemFilter.Click += (_, _) => OpenPivotItemFilter(pivot, headers, target);
            items.Add(new Separator());
            items.Add(itemFilter);
        }
        var menu = new ContextMenu { ItemsSource = items };
        // The WPF menu chrome; a checked row keeps its tick in the icon column.
        AvaloniaContextMenuChrome.Apply(menu);
        menu.Open(anchor);
    }

    private void InvokePivotHeaderAction(
        PivotTableModel pivot,
        IReadOnlyList<string> headers,
        PivotHeaderDropdownTargetModel target,
        PivotHeaderMenuAction action,
        PivotFieldDragValidator validator)
    {
        if (_isOpening || _isSaving)
            return;

        var actionPlan = PivotHeaderActionPlanner.Plan(action);
        if (actionPlan.RouteKind == PivotHeaderActionRouteKind.None)
            return;

        if (actionPlan.RouteKind == PivotHeaderActionRouteKind.Deferred)
        {
            ShowEditIssue(actionPlan.DeferredReason ?? UiText.Get("PivotLoc_ActionNotAvailableYet"));
            return;
        }

        if (actionPlan.RouteKind == PivotHeaderActionRouteKind.Dialog &&
            (TryOpenPivotFieldFilter(pivot, headers, target, action) ||
             TryOpenPivotFieldSettings(pivot, headers, target, action)))
        {
            return;
        }

        var result = PivotHeaderCommandPlanner.Create(
            _session.ActiveSheet.Id, pivot, headers, target, action, validator);

        if (result.IsDeferred)
        {
            ShowEditIssue(result.DeferredReason ?? UiText.Get("PivotLoc_ActionNotAvailableYet"));
            return;
        }

        if (result.IsNoOp || result.Command is null)
            return;

        ApplyPivotApplicationPlan(
            PivotApplication.PlanHeaderCommand(
                new PivotApplicationTarget(_session.ActiveSheet, pivot),
                result));
    }

    // The pane chip carries a layout-area bucket; map it to the header-target the menu builder expects.
    private static PivotHeaderDropdownTargetModel? ResolvePivotHeaderTarget(
        PivotTableModel pivot,
        IReadOnlyList<string> headers,
        PivotFieldListItemModel field)
    {
        var area = field.Bucket switch
        {
            PivotFieldBucket.Rows => PivotHeaderArea.Row,
            PivotFieldBucket.Columns => PivotHeaderArea.Column,
            PivotFieldBucket.Filters => PivotHeaderArea.Page,
            PivotFieldBucket.Values => PivotHeaderArea.Value,
            _ => (PivotHeaderArea?)null,
        };
        if (area is null)
            return null;

        return new PivotHeaderDropdownTargetModel(
            pivot.Name,
            PivotFieldListPaneBuilder.FieldCaption(headers, field.SourceFieldIndex),
            field.SourceFieldIndex,
            area.Value,
            IsActive: false,
            DataFieldIndex: field.DataFieldIndex);
    }

    private PivotFieldDragValidator BuildPivotDragValidator(PivotTableModel pivot) =>
        new(sourceFieldIndex => PivotSourceContext.IsNumericSourceColumn(_session.Workbook, pivot, sourceFieldIndex));

    private sealed record PivotPaneDragItem(
        int SourceFieldIndex,
        PivotFieldBucket SourceBucket,
        int? SourceItemIndex);

    private sealed record PivotDropZone(
        Border Bucket,
        PivotFieldBucket Kind,
        IReadOnlyList<Control> Items,
        IReadOnlyList<PivotFieldListItemModel> Fields);

    private sealed record PivotDropTarget(PivotDropZone Zone, int TargetIndex);
}
