using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using System.Text.Json;
using FreeX.App.Presentation.PivotUI;
using FreeX.Core.Commands;
using FreeX.Core.Model;

using AvaloniaGrid = Avalonia.Controls.Grid;
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
    private const double PivotPaneWidth = 248;

    private static readonly IBrush PivotPaneBackground = Brush(247, 248, 250);
    private static readonly IBrush PivotBucketBackground = Brushes.White;
    private static readonly IBrush PivotFieldChipBackground = Brush(236, 240, 244);
    private static readonly IBrush PivotDropHighlight = Brush(225, 244, 242);

    private readonly Border _pivotFieldPaneHost = new();
    private TextBox? _pivotFieldPaneSearchBox;

    // Signature of the pivot the pane currently reflects, so the pane only rebuilds when the pivot identity
    // or its layout actually changes (cheap guard against rebuilding on every selection move).
    private string? _pivotPaneSignature;
    private string _pivotPaneSearchText = string.Empty;
    private int _pivotFieldPaneBuildCount;

    internal int PivotFieldPaneBuildCountForTest => _pivotFieldPaneBuildCount;

    internal bool PivotFieldPaneVisibleForTest => _pivotFieldPaneHost.IsVisible;

    // The field currently being dragged within the pane (pointer-capture gesture), or null when idle.
    private PivotPaneDragItem? _pivotPaneDragItem;
    private readonly List<PivotDropZone> _pivotDropZones = [];

    private Control BuildPivotFieldPaneChrome()
    {
        _pivotFieldPaneHost.Width = PivotPaneWidth;
        _pivotFieldPaneHost.Background = PivotPaneBackground;
        _pivotFieldPaneHost.BorderBrush = ToolbarBorder;
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
            }

            RecordPivotRuntimeEvidence("pane-hidden");
            return;
        }

        var headers = PivotSourceContext.ReadHeaders(_session.Workbook, pivot);
        var signature = BuildPivotPaneSignature(pivot);
        if (_pivotFieldPaneHost.IsVisible && signature == _pivotPaneSignature)
            return;

        _pivotPaneSignature = signature;
        _pivotFieldPaneHost.Child = BuildPivotFieldPaneBody(pivot, headers);
        _pivotFieldPaneHost.IsVisible = true;
        RecordPivotRuntimeEvidence("pane-visible");
    }

    private void RecordPivotRuntimeEvidence(string stage)
    {
        var path = FindPivotRuntimeEvidencePath(App.StartupArguments);
        if (string.IsNullOrWhiteSpace(path))
            return;

        try
        {
            var sheet = _session.ActiveSheet;
            var activeCell = _session.ActiveCell;
            var pivot = PivotSourceContext.FindActivePivot(sheet, activeCell);
            var payload = new
            {
                utc = DateTimeOffset.UtcNow,
                stage,
                activeSheet = sheet.Name,
                activeSheetId = sheet.Id.ToString(),
                activeCellSheetId = activeCell.Sheet.ToString(),
                activeCellRow = activeCell.Row,
                activeCellColumn = activeCell.Col,
                startupArguments = App.StartupArguments.ToArray(),
                currentFilePath = _session.CurrentFilePath,
                workbookName = _session.Workbook.Name,
                workbookSheets = _session.Workbook.Sheets.Select(item => new
                {
                    item.Name,
                    pivotCount = item.PivotTables.Count,
                }).ToArray(),
                sheetPivotCount = sheet.PivotTables.Count,
                pivots = sheet.PivotTables.Select(item => new
                {
                    item.Name,
                    targetStart = item.TargetRange.Start.ToA1(),
                    targetEnd = item.TargetRange.End.ToA1(),
                    renderedStart = item.LastRenderedRange?.Start.ToA1(),
                    renderedEnd = item.LastRenderedRange?.End.ToA1(),
                }).ToArray(),
                resolvedPivot = pivot?.Name,
                paneVisible = _pivotFieldPaneHost.IsVisible,
                paneWidth = _pivotFieldPaneHost.Bounds.Width,
                userHidden = _pivotFieldPaneUserHidden,
            };
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);
            File.AppendAllText(path, JsonSerializer.Serialize(payload) + Environment.NewLine);
        }
        catch
        {
            // Evidence is opt-in and must never affect worksheet behavior.
        }
    }

    private static string? FindPivotRuntimeEvidencePath(IReadOnlyList<string> arguments)
    {
        for (var index = 0; index + 1 < arguments.Count; index++)
        {
            if (string.Equals(arguments[index], "--freex-pivot-runtime-evidence", StringComparison.OrdinalIgnoreCase))
                return arguments[index + 1];
        }

        return null;
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
        var model = PivotFieldListPaneBuilder.Build(pivot, headers);

        var layout = new DockPanel { Margin = new Thickness(8) };

        var title = new TextBlock
        {
            Text = UiText.Get("PivotLoc_FieldsPaneTitle"),
            FontWeight = FontWeight.SemiBold,
            Foreground = HeaderForeground,
            Margin = new Thickness(2, 0, 0, 6),
        };
        DockPanel.SetDock(title, Dock.Top);
        layout.Children.Add(title);

        var searchBox = new TextBox
        {
            PlaceholderText = UiText.Get("PivotLoc_SearchFields"),
            Text = _pivotPaneSearchText,
            Margin = new Thickness(0, 0, 0, 8),
        };
        _pivotFieldPaneSearchBox = searchBox;
        AutomationProperties.SetAutomationId(searchBox, "PivotFieldListSearchBox");
        AutomationProperties.SetName(searchBox, UiText.Get("PivotLoc_SearchFields"));
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
        DockPanel.SetDock(searchBox, Dock.Top);
        layout.Children.Add(searchBox);

        var stack = new StackPanel { Spacing = 8 };
        stack.Children.Add(BuildPivotAvailableBucket(pivot, headers, model.Available));
        stack.Children.Add(BuildPivotBucket(pivot, headers, UiText.Get("PivotLoc_BucketFilters"), model.Filters));
        stack.Children.Add(BuildPivotBucket(pivot, headers, UiText.Get("PivotLoc_BucketColumns"), model.Columns));
        stack.Children.Add(BuildPivotBucket(pivot, headers, UiText.Get("PivotLoc_BucketRows"), model.Rows));
        stack.Children.Add(BuildPivotBucket(pivot, headers, UiText.Get("PivotLoc_BucketValues"), model.Values));

        layout.Children.Add(new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = stack,
        });

        return layout;
    }

    private Control BuildPivotAvailableBucket(
        PivotTableModel pivot,
        IReadOnlyList<string> headers,
        PivotFieldListBucketModel bucket)
    {
        var filtered = PivotFieldListPaneBuilder.FilterByCaption(bucket.Fields, _pivotPaneSearchText);
        var body = new StackPanel { Spacing = 2 };
        if (filtered.Count == 0)
            body.Children.Add(BuildPivotPlaceholder(UiText.Get("PivotLoc_NoFields")));
        else
            foreach (var field in filtered)
                body.Children.Add(BuildPivotFieldChip(pivot, headers, field, showMenu: false));

        return BuildPivotBucketContainer(
            pivot,
            headers,
            UiText.Get("PivotLoc_ChooseFields"),
            PivotFieldBucket.Available,
            filtered,
            body);
    }

    private Control BuildPivotBucket(
        PivotTableModel pivot,
        IReadOnlyList<string> headers,
        string title,
        PivotFieldListBucketModel bucket)
    {
        var body = new StackPanel { Spacing = 2 };
        if (bucket.IsEmpty)
            body.Children.Add(BuildPivotPlaceholder(UiText.Get("PivotLoc_DropFieldsHere")));
        else
            foreach (var field in bucket.Fields)
                body.Children.Add(BuildPivotFieldChip(pivot, headers, field, showMenu: true));

        return BuildPivotBucketContainer(pivot, headers, title, bucket.Bucket, bucket.Fields, body);
    }

    private Border BuildPivotBucketContainer(
        PivotTableModel pivot,
        IReadOnlyList<string> headers,
        string title,
        PivotFieldBucket bucketKind,
        IReadOnlyList<PivotFieldListItemModel> fields,
        StackPanel body)
    {
        var header = new TextBlock
        {
            Text = title,
            FontWeight = FontWeight.SemiBold,
            FontSize = 11,
            Foreground = HeaderForeground,
            Margin = new Thickness(2, 0, 0, 4),
        };

        var container = new Border
        {
            Background = PivotBucketBackground,
            BorderBrush = ToolbarBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6),
            MinHeight = 44,
            Child = new StackPanel { Children = { header, body } },
        };

        _pivotDropZones.Add(new PivotDropZone(container, bucketKind, body.Children.ToList(), fields));
        return container;
    }

    private static Control BuildPivotPlaceholder(string text) => new TextBlock
    {
        Text = text,
        FontSize = 11,
        Foreground = HeaderForeground,
        Opacity = 0.6,
        Margin = new Thickness(2, 2, 0, 2),
    };

    private Control BuildPivotFieldChip(
        PivotTableModel pivot,
        IReadOnlyList<string> headers,
        PivotFieldListItemModel field,
        bool showMenu)
    {
        var caption = new TextBlock
        {
            Text = field.Caption,
            VerticalAlignment = AvaloniaVerticalAlignment.Center,
            Foreground = HeaderForeground,
            FontSize = 12,
        };

        var row = new AvaloniaGrid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = GridLength.Auto },
            },
        };
        AddGridChild(row, caption, 0, 0);

        if (showMenu)
        {
            var menuButton = new Button
            {
                Content = "▾",
                FontSize = 10,
                Padding = new Thickness(4, 0, 4, 0),
                Margin = new Thickness(2, 0, 0, 0),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
            };
            menuButton.Click += (_, _) => ShowPivotHeaderDropdown(pivot, headers, field, menuButton);
            AddGridChild(row, menuButton, 0, 1);
        }

        if (field.Bucket != PivotFieldBucket.Available)
        {
            var removeButton = new Button
            {
                Content = "×",
                FontSize = 11,
                Padding = new Thickness(4, 0, 4, 0),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
            };
            removeButton.Click += (_, _) => ApplyPivotFieldDrop(
                pivot,
                headers,
                new PivotFieldDropRequest(field.SourceFieldIndex, PivotFieldBucket.Available));
            AddGridChild(row, removeButton, 0, 2);
        }

        var chip = new Border
        {
            Background = PivotFieldChipBackground,
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(6, 3, 4, 3),
            Child = row,
        };

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
        var result = validator.Validate(pivot, headers, request);
        if (!result.IsAllowed)
        {
            ShowEditIssue(result.RejectionReason ?? UiText.Get("PivotLoc_FieldMoveNotAllowed"));
            return;
        }

        var command = PivotFieldLayoutPlanner.TryCreateCommand(_session.ActiveSheet.Id, pivot, headers, result);
        if (command is null)
        {
            ShowEditIssue(UiText.Get("PivotLoc_NeedsValueField"));
            return;
        }

        ExecutePivotCommand(command);
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

        ExecutePivotCommand(result.Command);
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

    private void ExecutePivotCommand(IWorkbookCommand command)
    {
        var result = _session.ExecuteReviewCommand(command);
        if (!result.Success)
        {
            ShowEditIssue(result.ErrorMessage ?? UiText.Get("PivotLoc_UpdateFailed"));
            return;
        }

        // Force a pane rebuild on the next refresh regardless of signature drift timing.
        _pivotPaneSignature = null;
        RefreshShell(command.Label);
    }

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
