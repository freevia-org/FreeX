using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;

using Free.Shared.Shell.Avalonia;
using FreeX.App.Presentation.DrawingUI;
using FreeX.App.Presentation.Shell;
using FreeX.App.Services;
using FreeX.Core.Model;

using AvaloniaHorizontalAlignment = Avalonia.Layout.HorizontalAlignment;
using AvaloniaPath = Avalonia.Controls.Shapes.Path;
using AvaloniaVerticalAlignment = Avalonia.Layout.VerticalAlignment;

namespace FreeX.App.Avalonia;

/// <summary>
/// Selection Pane for the Avalonia/macOS shell: a modal dialog that lists every drawing object on the active
/// sheet (charts, pictures, shapes, text boxes) and lets the user select one, toggle its visibility, rename it,
/// reorder its z-order (bring forward / send backward via per-row up/down), and Show All / Hide All. All of the
/// object-list building, the can-move-up/down reasoning, the reorder math and the change-to-Core-command
/// translation come from the portable <see cref="SelectionPaneSession"/> so this behaves identically to the
/// WPF host's Selection Pane and is reusable on macOS. Reached from the Picture/Shape Format contextual tabs'
/// "Selection Pane" buttons (pictureFormat.selectionPane / shapeFormat.selectionPane).
/// </summary>
public sealed partial class MainWindow
{
    private static AvaloniaCompactDialogChromeStyle SelectionPaneDialogChromeStyle => new(FormulaBarFontFamily);

    /// <summary>A native binding adapter over the portable Selection Pane session item.</summary>
    private sealed class SelectionPaneRow(
        SelectionPaneSession session,
        SelectionPaneSessionItem item) : INotifyPropertyChanged
    {
        public SelectionPaneItem Item => item.Source;
        public Guid Id => Item.Id;
        public SelectionPaneObjectKind Kind => Item.Kind;
        public bool IsVisible
        {
            get => item.IsVisible;
            set
            {
                if (item.IsVisible == value)
                    return;

                session.SetVisibility(Id, value);
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsVisible)));
            }
        }

        public string Name
        {
            get => item.Name;
            set
            {
                if (string.Equals(item.Name, value, System.StringComparison.Ordinal))
                    return;

                session.SetName(Id, value);
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
            }
        }

        public bool IsDropBefore
        {
            get => session.DropVisual is { IsAllowed: true, Placement: SelectionPaneDropPlacement.Before } plan &&
                plan.TargetId == Id;
        }

        public bool IsDropAfter
        {
            get => session.DropVisual is { IsAllowed: true, Placement: SelectionPaneDropPlacement.After } plan &&
                plan.TargetId == Id;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public void NotifySessionStateChanged()
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsVisible)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsDropBefore)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsDropAfter)));
        }
    }

    private System.Threading.Tasks.Task OpenSelectionPaneDialogAsync() =>
        OpenSelectionPaneDialogAsync(captureItems: null);

    private async System.Threading.Tasks.Task OpenSelectionPaneDialogAsync(
        IReadOnlyList<SelectionPaneItem>? captureItems)
    {
        if (_isOpening || _isSaving)
            return;

        var session = captureItems is null
            ? SelectionPaneSession.Create(_session.ActiveSheet, SelectionPaneText())
            : new SelectionPaneSession(captureItems);
        if (session.Items.Count == 0)
        {
            RefreshShell(UiText.Get("SelectionPane_NoObjects"));
            return;
        }

        var rows = session.Items.Select(item => new SelectionPaneRow(session, item)).ToList();
        var listBox = new ListBox
        {
            MinHeight = 140,
            SelectionMode = SelectionMode.Single,
            Margin = new Thickness(0, 0, 0, 10),
            Background = Brushes.White,
            BorderBrush = Brush(190, 190, 190),
            BorderThickness = new Thickness(1),
        };
        ApplySelectionPaneListStyle(listBox);
        AutomationProperties.SetAutomationId(listBox, FreeXAutomationIdCatalog.SelectionPane.ObjectList);
        AutomationProperties.SetName(listBox, UiText.Get("SelectionPane_ObjectListLabel"));

        var searchBox = new TextBox { MinWidth = 160, Margin = new Thickness(0, 0, 10, 0) };
        ApplySelectionPaneTextBoxChrome(searchBox);
        AutomationProperties.SetAutomationId(searchBox, FreeXAutomationIdCatalog.SelectionPane.SearchBox);
        var filterBox = new ComboBox { MinWidth = 130 };
        ApplySelectionPaneComboBoxChrome(filterBox);
        foreach (var filter in new[] { "All", "Visible", "Hidden", "Charts", "Pictures", "Shapes", "Text Boxes" })
            filterBox.Items.Add(filter);
        filterBox.SelectedIndex = 0;
        AutomationProperties.SetAutomationId(filterBox, FreeXAutomationIdCatalog.SelectionPane.FilterBox);

        var renameBox = new TextBox { MinWidth = 160, Margin = new Thickness(0, 0, 6, 0) };
        ApplySelectionPaneTextBoxChrome(renameBox);
        AutomationProperties.SetAutomationId(renameBox, FreeXAutomationIdCatalog.SelectionPane.RenameBox);
        var renameButton = new Button { Content = UiText.Get("SelectionPane_RenameButton"), MinWidth = 78, Margin = new Thickness(0, 0, 6, 0) };
        ApplySelectionPaneButtonChrome(renameButton, 78);
        AutomationProperties.SetAutomationId(renameButton, FreeXAutomationIdCatalog.SelectionPane.RenameButton);
        var toggleVisibilityButton = new Button { Content = CreateSelectionPaneEyeIcon(), Width = 32, Margin = new Thickness(0, 0, 6, 0) };
        ApplySelectionPaneButtonChrome(toggleVisibilityButton, 32);
        AutomationProperties.SetAutomationId(toggleVisibilityButton, FreeXAutomationIdCatalog.SelectionPane.ToggleVisibilityButton);

        var moveUpButton = new Button { Content = UiText.Get("SelectionPane_BringForward"), MinWidth = 104, Margin = new Thickness(0, 0, 6, 6) };
        ApplySelectionPaneButtonChrome(moveUpButton, 104);
        AutomationProperties.SetAutomationId(moveUpButton, FreeXAutomationIdCatalog.SelectionPane.BringForwardButton);
        var moveDownButton = new Button { Content = UiText.Get("SelectionPane_SendBackward"), MinWidth = 104, Margin = new Thickness(0, 0, 6, 6) };
        ApplySelectionPaneButtonChrome(moveDownButton, 104);
        AutomationProperties.SetAutomationId(moveDownButton, FreeXAutomationIdCatalog.SelectionPane.SendBackwardButton);
        var showAllButton = new Button { Content = UiText.Get("SelectionPane_ShowAll"), MinWidth = 82, Margin = new Thickness(0, 0, 6, 6) };
        ApplySelectionPaneButtonChrome(showAllButton, 82);
        AutomationProperties.SetAutomationId(showAllButton, FreeXAutomationIdCatalog.SelectionPane.ShowAllButton);
        var hideAllButton = new Button { Content = UiText.Get("SelectionPane_HideAll"), MinWidth = 82, Margin = new Thickness(0, 0, 6, 6) };
        ApplySelectionPaneButtonChrome(hideAllButton, 82);
        AutomationProperties.SetAutomationId(hideAllButton, FreeXAutomationIdCatalog.SelectionPane.HideAllButton);
        var deleteButton = new Button { Content = UiText.Get("SelectionPane_Delete"), MinWidth = 82, Margin = new Thickness(0, 0, 6, 6) };
        ApplySelectionPaneButtonChrome(deleteButton, 82);
        AutomationProperties.SetAutomationId(deleteButton, FreeXAutomationIdCatalog.SelectionPane.DeleteButton);

        var isRebinding = false;

        void NotifyRows() => rows.ForEach(row => row.NotifySessionStateChanged());

        void Rebind(Guid? preferredSelection)
        {
            session.SetView(
                searchBox.Text,
                filterBox.SelectedIndex switch
                {
                    1 => SelectionPaneFilterValues.Visible,
                    2 => SelectionPaneFilterValues.Hidden,
                    3 => SelectionPaneFilterValues.Charts,
                    4 => SelectionPaneFilterValues.Pictures,
                    5 => SelectionPaneFilterValues.Shapes,
                    6 => SelectionPaneFilterValues.TextBoxes,
                    _ => SelectionPaneFilterValues.All,
                },
                preferredSelection);

            var rowsById = rows.ToDictionary(row => row.Id);
            rows = session.Items.Select(item => rowsById[item.Id]).ToList();
            var filtered = session.FilteredItems.Select(item => rowsById[item.Id]).ToList();
            isRebinding = true;
            try
            {
                listBox.ItemsSource = null;
                listBox.ItemsSource = filtered;
                listBox.SelectedItem = session.SelectedId is { } selectedId
                    ? filtered.FirstOrDefault(row => row.Id == selectedId)
                    : null;
            }
            finally
            {
                isRebinding = false;
            }

            NotifyRows();
        }

        void UpdateMoveButtons()
        {
            if (listBox.SelectedItem is not SelectionPaneRow selected)
            {
                moveUpButton.IsEnabled = false;
                moveDownButton.IsEnabled = false;
                renameBox.Text = string.Empty;
                renameButton.IsEnabled = false;
                toggleVisibilityButton.IsEnabled = false;
                deleteButton.IsEnabled = false;
                return;
            }

            session.Select(selected.Id);
            moveUpButton.IsEnabled = session.CanMoveUp;
            moveDownButton.IsEnabled = session.CanMoveDown;
            if (!string.Equals(renameBox.Text, selected.Name, System.StringComparison.Ordinal))
                renameBox.Text = selected.Name;
            renameButton.IsEnabled = session.CanRename;
            toggleVisibilityButton.IsEnabled = session.CanToggleVisibility;
            deleteButton.IsEnabled = session.CanDelete;
        }

        void DeleteSelected()
        {
            if (listBox.SelectedItem is not SelectionPaneRow selected)
                return;

            session.Select(selected.Id);
            if (session.DeleteSelected().StateChanged)
            {
                Rebind(session.SelectedId);
                UpdateMoveButtons();
            }
        }

        void Move(bool forward)
        {
            if (listBox.SelectedItem is not SelectionPaneRow selected)
                return;

            session.Select(selected.Id);
            if (session.MoveSelected(forward).StateChanged)
            {
                Rebind(selected.Id);
                UpdateMoveButtons();
            }
        }

        void ToggleSelectedVisibility()
        {
            if (listBox.SelectedItem is not SelectionPaneRow selected)
                return;

            session.Select(selected.Id);
            if (session.ToggleSelectedVisibility().StateChanged)
                Rebind(selected.Id);
        }

        void FocusRenameBox()
        {
            renameBox.Focus();
            renameBox.SelectAll();
        }

        moveUpButton.Click += (_, _) => Move(forward: true);
        moveDownButton.Click += (_, _) => Move(forward: false);
        renameButton.Click += (_, _) =>
        {
            if (listBox.SelectedItem is not SelectionPaneRow selected)
                return;

            session.Select(selected.Id);
            if (session.RenameSelected(renameBox.Text).StateChanged)
                Rebind(selected.Id);
        };
        toggleVisibilityButton.Click += (_, _) => ToggleSelectedVisibility();
        showAllButton.Click += (_, _) =>
        {
            session.SetAllVisibility(isVisible: true);
            Rebind(null);
        };
        hideAllButton.Click += (_, _) =>
        {
            session.SetAllVisibility(isVisible: false);
            Rebind(null);
        };
        deleteButton.Click += (_, _) => DeleteSelected();
        searchBox.TextChanged += (_, _) => Rebind(null);
        filterBox.SelectionChanged += (_, _) => Rebind(null);

        listBox.KeyDown += (_, e) =>
        {
            // Keep typing and checkbox activation inside their editors; WPF's list keyboard contract only
            // applies when the list row itself owns focus.
            if (e.Source is TextBox or CheckBox)
                return;

            var outcome = session.HandleKeyboard(
                ToSelectionPaneKeyboardKey(e.Key),
                e.KeyModifiers.HasFlag(KeyModifiers.Control));
            if (outcome.StateChanged)
                Rebind(session.SelectedId);
            if (outcome.FocusRename)
                FocusRenameBox();

            e.Handled = outcome.IsHandled;
        };

        SelectionPaneRow? dragRow = null;
        IPointer? dragPointer = null;
        Point dragStart = default;
        var isDragging = false;

        SelectionPaneRow? FindRow(object? source)
        {
            if (source is ListBoxItem item)
                return item.DataContext as SelectionPaneRow;

            return source is Visual visual
                ? visual.GetVisualAncestors().OfType<ListBoxItem>()
                    .Select(item => item.DataContext)
                    .OfType<SelectionPaneRow>()
                    .FirstOrDefault()
                : null;
        }

        (SelectionPaneRow Row, SelectionPaneDropPlacement Placement)? FindDropTarget(Point position)
        {
            foreach (var item in listBox.GetVisualDescendants().OfType<ListBoxItem>())
            {
                var origin = item.TranslatePoint(new Point(0, 0), listBox);
                if (origin is not { } topLeft)
                    continue;

                var bounds = new Rect(topLeft, item.Bounds.Size);
                if (!bounds.Contains(position) || item.DataContext is not SelectionPaneRow row)
                    continue;

                var placement = position.Y > bounds.Top + bounds.Height / 2
                    ? SelectionPaneDropPlacement.After
                    : SelectionPaneDropPlacement.Before;
                return (row, placement);
            }

            return null;
        }

        void UpdateDropVisual(Point position)
        {
            if (dragRow is null || FindDropTarget(position) is not { } target)
            {
                session.ClearDropVisual();
                NotifyRows();
                return;
            }

            session.UpdateDrag(
                target.Row.Id,
                target.Placement);
            NotifyRows();
        }

        void ClearDragState(bool releasePointer)
        {
            var pointer = dragPointer;
            dragPointer = null;
            dragRow = null;
            isDragging = false;
            session.CancelDrag();
            NotifyRows();
            if (releasePointer)
                pointer?.Capture(null);
        }

        listBox.AddHandler(
            InputElement.PointerPressedEvent,
            (_, e) =>
            {
                if (!e.GetCurrentPoint(listBox).Properties.IsLeftButtonPressed ||
                    e.Source is TextBox or CheckBox ||
                    FindRow(e.Source) is not { } row)
                    return;

                dragRow = row;
                dragPointer = e.Pointer;
                dragStart = e.GetPosition(listBox);
                isDragging = false;
                session.BeginDrag(row.Id);
                e.Pointer.Capture(listBox);
            },
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        listBox.PointerMoved += (_, e) =>
        {
            if (dragRow is null || dragPointer != e.Pointer)
                return;

            var point = e.GetCurrentPoint(listBox);
            if (!point.Properties.IsLeftButtonPressed)
            {
                ClearDragState(releasePointer: true);
                return;
            }

            var position = e.GetPosition(listBox);
            if (!isDragging &&
                Math.Abs(position.X - dragStart.X) < 4 &&
                Math.Abs(position.Y - dragStart.Y) < 4)
            {
                return;
            }

            isDragging = true;
            UpdateDropVisual(position);
        };
        listBox.PointerReleased += (_, e) =>
        {
            if (dragRow is null || dragPointer != e.Pointer)
                return;

            var dragged = dragRow;
            var wasDragging = isDragging;
            var target = wasDragging ? FindDropTarget(e.GetPosition(listBox)) : null;
            var pointer = dragPointer;
            dragPointer = null;
            dragRow = null;
            isDragging = false;
            pointer?.Capture(null);
            if (!wasDragging || target is not { } dropTarget)
            {
                session.CancelDrag();
                NotifyRows();
                return;
            }

            var outcome = session.Drop(
                dropTarget.Row.Id,
                dropTarget.Placement);
            if (outcome.StateChanged)
            {
                Rebind(dragged.Id);
                UpdateMoveButtons();
            }
            else
            {
                NotifyRows();
            }

            e.Handled = outcome.IsHandled;
        };
        listBox.PointerCaptureLost += (_, _) => ClearDragState(releasePointer: false);

        listBox.ItemTemplate = new FuncDataTemplate<SelectionPaneRow>((row, _) =>
        {
            var visibilityBox = new CheckBox
            {
                IsChecked = row.IsVisible,
                VerticalAlignment = AvaloniaVerticalAlignment.Center,
                VerticalContentAlignment = AvaloniaVerticalAlignment.Center,
                HorizontalAlignment = AvaloniaHorizontalAlignment.Left,
                MinWidth = 0,
                Margin = new Thickness(0),
                Padding = new Thickness(0),
            };
            ApplySortOptionsCheckBoxChrome(visibilityBox);
            AutomationProperties.SetAutomationId(
                visibilityBox,
                FreeXAutomationIdCatalog.SelectionPane.AvaloniaVisibility(row.Id));
            AutomationProperties.SetName(visibilityBox, UiText.Get("SelectionPane_VisibilityToggle"));
            visibilityBox.IsCheckedChanged += (_, _) => row.IsVisible = visibilityBox.IsChecked == true;

            var nameBox = new TextBox
            {
                Text = row.Name,
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
                VerticalAlignment = AvaloniaVerticalAlignment.Center,
                HorizontalAlignment = AvaloniaHorizontalAlignment.Stretch,
                FontSize = 12,
                FontFamily = FormulaBarFontFamily,
                Height = 22,
                Padding = new Thickness(0),
                Margin = new Thickness(8, 0, 0, 0),
            };
            AutomationProperties.SetAutomationId(
                nameBox,
                FreeXAutomationIdCatalog.SelectionPane.AvaloniaName(row.Id));
            AutomationProperties.SetName(nameBox, UiText.Get("SelectionPane_NameLabel"));
            nameBox.TextChanged += (_, _) => row.Name = nameBox.Text ?? string.Empty;

            var kindText = new TextBlock
            {
                Text = SelectionPaneKindLabel(row.Kind),
                Foreground = Brush(128, 128, 128),
                VerticalAlignment = AvaloniaVerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0),
                FontSize = 12,
                FontFamily = FormulaBarFontFamily,
            };

            // Fixed shared columns so the checkbox / name / type line up row-to-row (Windows parity):
            // the name occupies a fixed-width slot and the type label sits immediately after it (matching
            // the Windows screenshot, where the type is not pushed to the far right). Every cell is
            // vertically centered in a uniform 24px row so the three columns share one horizontal line.
            var rowGrid = new Grid
            {
                MinHeight = 24,
                VerticalAlignment = AvaloniaVerticalAlignment.Center,
                ColumnDefinitions =
                {
                    new ColumnDefinition { Width = new GridLength(32) },
                    new ColumnDefinition { Width = new GridLength(160) },
                    new ColumnDefinition { Width = GridLength.Auto },
                    new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                },
            };
            Grid.SetColumn(visibilityBox, 0);
            Grid.SetColumn(nameBox, 1);
            Grid.SetColumn(kindText, 2);
            rowGrid.Children.Add(visibilityBox);
            rowGrid.Children.Add(nameBox);
            rowGrid.Children.Add(kindText);

            var rowSurface = new Grid { MinHeight = 24 };
            rowSurface.Children.Add(rowGrid);
            rowSurface.Children.Add(new Border
            {
                Background = Brush(218, 218, 218),
                Height = 1,
                VerticalAlignment = AvaloniaVerticalAlignment.Bottom,
                IsHitTestVisible = false,
            });
            var beforeCue = new Border
            {
                Background = Brush(32, 122, 197),
                Height = 2,
                VerticalAlignment = AvaloniaVerticalAlignment.Top,
                IsHitTestVisible = false,
            };
            beforeCue.Bind(Visual.IsVisibleProperty, new Binding(nameof(SelectionPaneRow.IsDropBefore)) { Source = row });
            rowSurface.Children.Add(beforeCue);
            var afterCue = new Border
            {
                Background = Brush(32, 122, 197),
                Height = 2,
                VerticalAlignment = AvaloniaVerticalAlignment.Bottom,
                IsHitTestVisible = false,
            };
            afterCue.Bind(Visual.IsVisibleProperty, new Binding(nameof(SelectionPaneRow.IsDropAfter)) { Source = row });
            rowSurface.Children.Add(afterCue);
            return rowSurface;
        });

        listBox.SelectionChanged += (_, _) =>
        {
            if (!isRebinding)
                session.Select((listBox.SelectedItem as SelectionPaneRow)?.Id);
            UpdateMoveButtons();
        };

        var dialog = new Window
        {
            Title = UiText.Get("SelectionPane_Title"),
            Width = 520,
            Height = 440,
            MinWidth = 460,
            MinHeight = 360,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };
        AutomationProperties.SetAutomationId(dialog, FreeXAutomationIdCatalog.SelectionPane.Dialog);
        KeyboardNavigation.SetTabNavigation(dialog, KeyboardNavigationMode.Cycle);

        var ok = new Button { Content = UiText.Get("Common_Ok"), IsDefault = true, Width = 78 };
        ApplySelectionPaneButtonChrome(ok, 78, isDefault: true);
        AutomationProperties.SetAutomationId(ok, FreeXAutomationIdCatalog.SelectionPane.OkButton);
        var cancel = new Button { Content = UiText.Get("Common_Cancel"), IsCancel = true, Width = 78 };
        ApplySelectionPaneButtonChrome(cancel, 78);
        AutomationProperties.SetAutomationId(cancel, FreeXAutomationIdCatalog.SelectionPane.CancelButton);
        ok.Click += (_, _) => dialog.Close(true);
        cancel.Click += (_, _) => dialog.Close(false);

        var searchRow = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        searchRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        searchRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 160 });
        searchRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        searchRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        AddGridChild(searchRow, new TextBlock { Text = StripDisplayMnemonic(UiText.Get("SelectionPane_SearchLabel")), FontSize = 12, VerticalAlignment = AvaloniaVerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) }, 0);
        AddGridChild(searchRow, searchBox, 1);
        AddGridChild(searchRow, new TextBlock { Text = StripDisplayMnemonic(UiText.Get("SelectionPane_FilterLabel")), FontSize = 12, VerticalAlignment = AvaloniaVerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) }, 2);
        AddGridChild(searchRow, filterBox, 3);

        var renameRow = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        renameRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        renameRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 160 });
        renameRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        renameRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        AddGridChild(renameRow, new TextBlock { Text = StripDisplayMnemonic(UiText.Get("SelectionPane_NameLabel")), FontSize = 12, VerticalAlignment = AvaloniaVerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) }, 0);
        AddGridChild(renameRow, renameBox, 1);
        AddGridChild(renameRow, renameButton, 2);
        AddGridChild(renameRow, toggleVisibilityButton, 3);

        var commandRow = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 4),
            Children = { showAllButton, hideAllButton, moveUpButton, moveDownButton, deleteButton },
        };

        var buttonRow = AvaloniaCompactDialogChrome.CreateActionRow([ok, cancel]);
        buttonRow.Spacing = 6;
        var content = new Grid { Margin = new Thickness(16) };
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star), MinHeight = 140 });
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        // WPF's fixed 520x440 capture reserves the native title-bar/client-area delta below the action row;
        // preserve that same visual baseline in Avalonia's borderless dialog client area.
        content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(37) });
        AddGridChild(content, searchRow, 0, isRow: true);
        AddGridChild(content, listBox, 1, isRow: true);
        AddGridChild(content, renameRow, 2, isRow: true);
        AddGridChild(content, commandRow, 3, isRow: true);
        AddGridChild(content, buttonRow, 4, isRow: true);
        dialog.Content = content;
        ConfigureNativeDialogInitialFocus(dialog, content, searchBox);
        ConfigureDeferredDialogCancel(dialog, cancel);

        Rebind(null);
        UpdateMoveButtons();

        var confirmed = await dialog.ShowDialog<bool>(this);
        if (!confirmed)
            return;

        ApplySelectionPaneChanges(session);
    }

    private static void AddGridChild(Grid grid, Control child, int index, bool isRow = false)
    {
        if (isRow)
            Grid.SetRow(child, index);
        else
            Grid.SetColumn(child, index);

        grid.Children.Add(child);
    }

    private static void ApplySelectionPaneListStyle(ListBox listBox)
    {
        AvaloniaCompactDialogChrome.ApplyListBox(listBox, SelectionPaneDialogChromeStyle);
        listBox.Styles.Add(new Style(x => x.OfType<ListBoxItem>())
        {
            Setters =
            {
                new Setter(TemplatedControl.BackgroundProperty, Brushes.White),
                new Setter(TemplatedControl.ForegroundProperty, Brushes.Black),
                new Setter(TemplatedControl.PaddingProperty, new Thickness(0)),
                new Setter(TemplatedControl.BorderBrushProperty, Brush(218, 218, 218)),
                new Setter(TemplatedControl.BorderThicknessProperty, new Thickness(0, 0, 0, 1)),
                new Setter(Layoutable.MinHeightProperty, 28d),
                new Setter(ContentControl.HorizontalContentAlignmentProperty, AvaloniaHorizontalAlignment.Stretch),
            },
        });
        listBox.Styles.Add(new Style(x => x.OfType<ListBoxItem>().Class(":selected"))
        {
            Setters =
            {
                new Setter(TemplatedControl.BackgroundProperty, Brush(246, 246, 246)),
                new Setter(TemplatedControl.ForegroundProperty, Brushes.Black),
                new Setter(TemplatedControl.BorderBrushProperty, Brush(218, 218, 218)),
                new Setter(TemplatedControl.BorderThicknessProperty, new Thickness(0, 0, 0, 1)),
            },
        });
        listBox.Styles.Add(new Style(x => x.OfType<ListBoxItem>().Class(":selected").Template().OfType<Border>())
        {
            Setters =
            {
                new Setter(Border.BackgroundProperty, Brush(246, 246, 246)),
                new Setter(Border.BorderBrushProperty, Brush(218, 218, 218)),
                new Setter(Border.BorderThicknessProperty, new Thickness(0, 0, 0, 1)),
            },
        });
    }

    private static void ApplySelectionPaneTextBoxChrome(TextBox textBox)
    {
        AvaloniaCompactDialogChrome.ApplyTextBox(textBox, SelectionPaneDialogChromeStyle);
        textBox.Height = 22;
        textBox.MinHeight = 22;
        textBox.MaxHeight = 22;
    }

    private static void ApplySelectionPaneComboBoxChrome(ComboBox comboBox)
    {
        AvaloniaCompactDialogChrome.ApplyComboBox(comboBox, SelectionPaneDialogChromeStyle);
        comboBox.Height = 22;
        comboBox.MinHeight = 22;
        comboBox.MaxHeight = 22;
    }

    private static void ApplySelectionPaneButtonChrome(Button button, double width, bool isDefault = false)
    {
        button.Width = width;
        AvaloniaCompactDialogChrome.ApplyButton(button, SelectionPaneDialogChromeStyle, width, isDefault);
        button.Height = 20;
        button.MinHeight = 20;
        button.MaxHeight = 20;
    }

    private static Viewbox CreateSelectionPaneEyeIcon()
    {
        return new Viewbox
        {
            Width = 14,
            Height = 14,
            Child = new Grid
            {
                Width = 16,
                Height = 16,
                Children =
                {
                    new AvaloniaPath
                    {
                        Data = Geometry.Parse("M1.5,8 C3.7,4.2 5.9,3 8,3 C10.1,3 12.3,4.2 14.5,8 C12.3,11.8 10.1,13 8,13 C5.9,13 3.7,11.8 1.5,8 Z"),
                        Stroke = Brushes.Black,
                        StrokeThickness = 1.1,
                        Fill = Brushes.Transparent,
                    },
                    new Ellipse
                    {
                        Width = 4,
                        Height = 4,
                        Fill = Brushes.Black,
                        HorizontalAlignment = AvaloniaHorizontalAlignment.Center,
                        VerticalAlignment = AvaloniaVerticalAlignment.Center,
                    },
                },
            },
        };
    }

    private void ApplySelectionPaneChanges(SelectionPaneSession session)
    {
        var command = session.CreateCommand(_session.ActiveSheet.Id);
        if (command is null)
        {
            RefreshShell(UiText.Get("SelectionPane_NoChanges"));
            return;
        }

        var result = _session.ExecuteReviewCommand(command);
        if (!result.Success)
        {
            ShowEditIssue(result.ErrorMessage ?? UiText.Get("SelectionPane_ApplyFailed"));
            return;
        }

        RefreshShell(UiText.Get("SelectionPane_Applied"));
    }

    private static SelectionPanePlannerText SelectionPaneText() =>
        new(
            UiText.Get("SelectionPane_DefaultChartName"),
            UiText.Get("SelectionPane_DefaultPictureName"),
            UiText.Get("SelectionPane_DefaultTextBoxName"),
            UiText.Get("SelectionPane_DefaultShapeNameFormat"),
            UiText.Get("SelectionPane_DefaultEllipseName"),
            UiText.Get("SelectionPane_DefaultLineName"),
            UiText.Get("SelectionPane_DefaultRectangleName"));

    private static string SelectionPaneKindLabel(SelectionPaneObjectKind kind) =>
        kind switch
        {
            SelectionPaneObjectKind.Chart => UiText.Get("SelectionPane_KindChart"),
            SelectionPaneObjectKind.Picture => UiText.Get("SelectionPane_KindPicture"),
            SelectionPaneObjectKind.Shape => UiText.Get("SelectionPane_KindShape"),
            SelectionPaneObjectKind.TextBox => UiText.Get("SelectionPane_KindTextBox"),
            _ => kind.ToString()
        };

    private static SelectionPaneKeyboardKey ToSelectionPaneKeyboardKey(Key key) =>
        key switch
        {
            Key.F2 => SelectionPaneKeyboardKey.F2,
            Key.Space => SelectionPaneKeyboardKey.Space,
            Key.Up => SelectionPaneKeyboardKey.Up,
            Key.Down => SelectionPaneKeyboardKey.Down,
            Key.Delete => SelectionPaneKeyboardKey.Delete,
            _ => SelectionPaneKeyboardKey.Other,
        };
}
