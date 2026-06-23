using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using FreeX.App.Presentation.DrawingUI;
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
/// translation come from the portable <see cref="SelectionPaneViewPlanner"/> so this behaves identically to the
/// WPF host's Selection Pane and is reusable on macOS. Reached from the Picture/Shape Format contextual tabs'
/// "Selection Pane" buttons (pictureFormat.selectionPane / shapeFormat.selectionPane).
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>A mutable working row for the Selection Pane dialog (visibility + name edited in place).</summary>
    private sealed class SelectionPaneRow(SelectionPaneViewPlanner.Item item)
    {
        public SelectionPaneViewPlanner.Item Item { get; set; } = item;
        public Guid Id => Item.Id;
        public SelectionPaneObjectKind Kind => Item.Kind;
        public bool IsVisible { get; set; } = item.IsVisible;
        public string Name { get; set; } = item.Name;
    }

    private System.Threading.Tasks.Task OpenSelectionPaneDialogAsync() =>
        OpenSelectionPaneDialogAsync(captureItems: null);

    private async System.Threading.Tasks.Task OpenSelectionPaneDialogAsync(
        IReadOnlyList<SelectionPaneViewPlanner.Item>? captureItems)
    {
        if (_isOpening || _isSaving)
            return;

        var sheet = _session.ActiveSheet;
        var planned = captureItems ?? SelectionPaneViewPlanner.BuildItems(sheet, SelectionPaneText());
        if (planned.Count == 0)
        {
            RefreshShell(UiText.Get("SelectionPane_NoObjects"));
            return;
        }

        var originals = planned;
        var rows = planned.Select(item => new SelectionPaneRow(item)).ToList();
        var captureSelectedRow = captureItems is not null ? rows.FirstOrDefault() : null;

        var listBox = new ListBox { MinHeight = 140, SelectionMode = SelectionMode.Single };
        ApplySelectionPaneListStyle(listBox);
        AutomationProperties.SetAutomationId(listBox, "SelectionPaneObjectList");
        AutomationProperties.SetName(listBox, UiText.Get("SelectionPane_ObjectListLabel"));

        var searchBox = new TextBox { MinWidth = 160, Margin = new Thickness(0, 0, 10, 0) };
        AutomationProperties.SetAutomationId(searchBox, "SelectionPaneSearchBox");
        var filterBox = new ComboBox { MinWidth = 130 };
        foreach (var filter in new[] { "All", "Visible", "Hidden", "Charts", "Pictures", "Shapes", "Text Boxes" })
            filterBox.Items.Add(filter);
        filterBox.SelectedIndex = 0;
        AutomationProperties.SetAutomationId(filterBox, "SelectionPaneFilterBox");

        var renameBox = new TextBox { MinWidth = 160, Margin = new Thickness(0, 0, 6, 0) };
        AutomationProperties.SetAutomationId(renameBox, "SelectionPaneRenameBox");
        var renameButton = new Button { Content = UiText.Get("SelectionPane_RenameButton"), MinWidth = 78, Margin = new Thickness(0, 0, 6, 0) };
        AutomationProperties.SetAutomationId(renameButton, "SelectionPaneRenameButton");
        var toggleVisibilityButton = new Button { Content = CreateSelectionPaneEyeIcon(), Width = 32, Margin = new Thickness(0, 0, 6, 0) };
        AutomationProperties.SetAutomationId(toggleVisibilityButton, "SelectionPaneToggleVisibilityButton");

        var moveUpButton = new Button { Content = UiText.Get("SelectionPane_BringForward"), MinWidth = 104, Margin = new Thickness(0, 0, 6, 6) };
        AutomationProperties.SetAutomationId(moveUpButton, "SelectionPaneBringForwardButton");
        var moveDownButton = new Button { Content = UiText.Get("SelectionPane_SendBackward"), MinWidth = 104, Margin = new Thickness(0, 0, 6, 6) };
        AutomationProperties.SetAutomationId(moveDownButton, "SelectionPaneSendBackwardButton");
        var showAllButton = new Button { Content = UiText.Get("SelectionPane_ShowAll"), MinWidth = 82, Margin = new Thickness(0, 0, 6, 6) };
        AutomationProperties.SetAutomationId(showAllButton, "SelectionPaneShowAllButton");
        var hideAllButton = new Button { Content = UiText.Get("SelectionPane_HideAll"), MinWidth = 82, Margin = new Thickness(0, 0, 6, 6) };
        AutomationProperties.SetAutomationId(hideAllButton, "SelectionPaneHideAllButton");

        bool MatchesFilter(SelectionPaneRow row)
        {
            var search = searchBox.Text?.Trim() ?? string.Empty;
            if (search.Length > 0 &&
                !row.Name.Contains(search, System.StringComparison.OrdinalIgnoreCase) &&
                !row.Item.KindLabel.Contains(search, System.StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return filterBox.SelectedIndex switch
            {
                1 => row.IsVisible,
                2 => !row.IsVisible,
                3 => row.Kind == SelectionPaneObjectKind.Chart,
                4 => row.Kind == SelectionPaneObjectKind.Picture,
                5 => row.Kind == SelectionPaneObjectKind.Shape,
                6 => row.Kind == SelectionPaneObjectKind.TextBox,
                _ => true,
            };
        }

        void Rebind(Guid? preferredSelection)
        {
            var selected = preferredSelection ?? (listBox.SelectedItem as SelectionPaneRow)?.Id;
            var filtered = rows.Where(MatchesFilter).ToList();
            listBox.ItemsSource = null;
            listBox.ItemsSource = filtered;
            if (selected is { } id)
            {
                var match = filtered.FirstOrDefault(r => r.Id == id);
                listBox.SelectedItem = match ?? filtered.FirstOrDefault();
            }
            else
            {
                listBox.SelectedItem = captureSelectedRow is null ? filtered.FirstOrDefault() : null;
            }
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
                return;
            }

            moveUpButton.IsEnabled = selected.Item.CanMoveUp;
            moveDownButton.IsEnabled = selected.Item.CanMoveDown;
            if (!string.Equals(renameBox.Text, selected.Name, System.StringComparison.Ordinal))
                renameBox.Text = selected.Name;
            renameButton.IsEnabled = true;
            toggleVisibilityButton.IsEnabled = true;
        }

        // Pending move changes accumulate across button presses (z-order is applied as a sequence of one-step
        // moves so it round-trips through the existing MoveSelectionPaneObjectCommand and undo/redo).
        var moveChanges = new List<SelectionPaneViewPlanner.MoveChange>();

        void Move(bool forward)
        {
            if (listBox.SelectedItem is not SelectionPaneRow selected)
                return;

            var currentItems = ToItems(rows);
            var currentIndex = rows.FindIndex(r => r.Id == selected.Id);
            var plan = SelectionPaneViewPlanner.PlanMove(currentItems, currentIndex, forward);
            if (plan is not { } result)
                return;

            moveChanges.Add(result.Change);
            // Re-order the working rows to match the planned order, refreshing the move flags.
            var byId = rows.ToDictionary(r => r.Id);
            rows.Clear();
            foreach (var item in result.Ordered)
            {
                var row = byId[item.Id];
                row.Item = item;
                rows.Add(row);
            }

            Rebind(selected.Id);
            UpdateMoveButtons();
        }

        moveUpButton.Click += (_, _) => Move(forward: true);
        moveDownButton.Click += (_, _) => Move(forward: false);
        renameButton.Click += (_, _) =>
        {
            if (listBox.SelectedItem is not SelectionPaneRow selected)
                return;

            selected.Name = renameBox.Text ?? string.Empty;
            Rebind(selected.Id);
        };
        toggleVisibilityButton.Click += (_, _) =>
        {
            if (listBox.SelectedItem is not SelectionPaneRow selected)
                return;

            selected.IsVisible = !selected.IsVisible;
            Rebind(selected.Id);
        };
        showAllButton.Click += (_, _) =>
        {
            foreach (var row in rows)
                row.IsVisible = true;
            Rebind(null);
        };
        hideAllButton.Click += (_, _) =>
        {
            foreach (var row in rows)
                row.IsVisible = false;
            Rebind(null);
        };
        searchBox.TextChanged += (_, _) => Rebind(null);
        filterBox.SelectionChanged += (_, _) => Rebind(null);

        listBox.ItemTemplate = new FuncDataTemplate<SelectionPaneRow>((row, _) =>
        {
            var visibilityBox = new CheckBox
            {
                IsChecked = row.IsVisible,
                VerticalAlignment = AvaloniaVerticalAlignment.Center,
                MinWidth = 24,
            };
            AutomationProperties.SetAutomationId(visibilityBox, "SelectionPaneVisibility_" + row.Id.ToString("N"));
            AutomationProperties.SetName(visibilityBox, UiText.Get("SelectionPane_VisibilityToggle"));
            visibilityBox.IsCheckedChanged += (_, _) => row.IsVisible = visibilityBox.IsChecked == true;

            var nameBox = new TextBox
            {
                Text = row.Name,
                Width = 160,
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
                VerticalAlignment = AvaloniaVerticalAlignment.Center,
            };
            AutomationProperties.SetAutomationId(nameBox, "SelectionPaneName_" + row.Id.ToString("N"));
            AutomationProperties.SetName(nameBox, UiText.Get("SelectionPane_NameLabel"));
            nameBox.TextChanged += (_, _) => row.Name = nameBox.Text ?? string.Empty;

            var kindText = new TextBlock
            {
                Text = row.Item.KindLabel,
                Foreground = HeaderForeground,
                VerticalAlignment = AvaloniaVerticalAlignment.Center,
                MinWidth = 64,
            };

            return new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Children = { visibilityBox, nameBox, kindText },
            };
        });

        listBox.SelectionChanged += (_, _) => UpdateMoveButtons();

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
        AutomationProperties.SetAutomationId(dialog, "SelectionPaneDialog");

        var ok = new Button { Content = UiText.Get("Common_Ok"), IsDefault = true, Width = 78, Margin = new Thickness(0, 0, 6, 0) };
        AutomationProperties.SetAutomationId(ok, "SelectionPaneOkButton");
        var cancel = new Button { Content = UiText.Get("Common_Cancel"), IsCancel = true, Width = 78 };
        AutomationProperties.SetAutomationId(cancel, "SelectionPaneCancelButton");
        ok.Click += (_, _) => dialog.Close(true);
        cancel.Click += (_, _) => dialog.Close(false);

        var searchRow = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        searchRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        searchRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 160 });
        searchRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        searchRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        AddGridChild(searchRow, new TextBlock { Text = UiText.Get("SelectionPane_SearchLabel"), VerticalAlignment = AvaloniaVerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) }, 0);
        AddGridChild(searchRow, searchBox, 1);
        AddGridChild(searchRow, new TextBlock { Text = UiText.Get("SelectionPane_FilterLabel"), VerticalAlignment = AvaloniaVerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) }, 2);
        AddGridChild(searchRow, filterBox, 3);

        var renameRow = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        renameRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        renameRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 160 });
        renameRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        renameRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        AddGridChild(renameRow, new TextBlock { Text = UiText.Get("SelectionPane_NameLabel"), VerticalAlignment = AvaloniaVerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) }, 0);
        AddGridChild(renameRow, renameBox, 1);
        AddGridChild(renameRow, renameButton, 2);
        AddGridChild(renameRow, toggleVisibilityButton, 3);

        var commandRow = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 4),
            Children = { showAllButton, hideAllButton, moveUpButton, moveDownButton },
        };

        var buttonRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Right,
            Children = { ok, cancel },
        };
        var content = new Grid { Margin = new Thickness(16) };
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star), MinHeight = 140 });
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        AddGridChild(content, searchRow, 0, isRow: true);
        AddGridChild(content, listBox, 1, isRow: true);
        AddGridChild(content, renameRow, 2, isRow: true);
        AddGridChild(content, commandRow, 3, isRow: true);
        AddGridChild(content, buttonRow, 4, isRow: true);
        dialog.Content = content;

        Rebind(null);
        if (captureSelectedRow is not null)
        {
            renameBox.Text = captureSelectedRow.Name;
            renameButton.IsEnabled = true;
            toggleVisibilityButton.IsEnabled = true;
            moveUpButton.IsEnabled = false;
            moveDownButton.IsEnabled = false;
        }
        else
        {
            UpdateMoveButtons();
        }

        var confirmed = await dialog.ShowDialog<bool>(this);
        if (!confirmed)
            return;

        ApplySelectionPaneChanges(originals, rows, moveChanges);
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
        listBox.Styles.Add(new Style(x => x.OfType<ListBoxItem>())
        {
            Setters =
            {
                new Setter(TemplatedControl.BackgroundProperty, Brushes.Transparent),
                new Setter(TemplatedControl.ForegroundProperty, Brushes.Black),
                new Setter(TemplatedControl.PaddingProperty, new Thickness(0)),
            },
        });
        listBox.Styles.Add(new Style(x => x.OfType<ListBoxItem>().Class(":selected"))
        {
            Setters =
            {
                new Setter(TemplatedControl.BackgroundProperty, Brushes.Transparent),
                new Setter(TemplatedControl.ForegroundProperty, Brushes.Black),
            },
        });
        listBox.Styles.Add(new Style(x => x.OfType<ListBoxItem>().Class(":selected").Template().OfType<Border>())
        {
            Setters =
            {
                new Setter(Border.BackgroundProperty, Brushes.Transparent),
                new Setter(Border.BorderBrushProperty, Brushes.Transparent),
            },
        });
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

    private void ApplySelectionPaneChanges(
        IReadOnlyList<SelectionPaneViewPlanner.Item> originals,
        IReadOnlyList<SelectionPaneRow> rows,
        IReadOnlyList<SelectionPaneViewPlanner.MoveChange> moveChanges)
    {
        var current = ToItems(rows);
        var visibilityChanges = SelectionPaneViewPlanner.CreateVisibilityChanges(originals, current);
        var renameChanges = SelectionPaneViewPlanner.CreateRenameChanges(originals, current);
        var command = SelectionPaneViewPlanner.CreateCommand(
            _session.ActiveSheet.Id,
            visibilityChanges,
            renameChanges,
            moveChanges);
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

    private static IReadOnlyList<SelectionPaneViewPlanner.Item> ToItems(IReadOnlyList<SelectionPaneRow> rows) =>
        rows
            .Select(row => row.Item with { IsVisible = row.IsVisible, Name = row.Name })
            .ToList();

    private static SelectionPaneViewPlanner.Text SelectionPaneText() =>
        new(
            UiText.Get("SelectionPane_DefaultChartName"),
            UiText.Get("SelectionPane_DefaultPictureName"),
            UiText.Get("SelectionPane_DefaultTextBoxName"),
            UiText.Get("SelectionPane_DefaultShapeNameFormat"),
            UiText.Get("SelectionPane_DefaultEllipseName"),
            UiText.Get("SelectionPane_DefaultLineName"),
            UiText.Get("SelectionPane_DefaultRectangleName"),
            UiText.Get("SelectionPane_KindChart"),
            UiText.Get("SelectionPane_KindPicture"),
            UiText.Get("SelectionPane_KindShape"),
            UiText.Get("SelectionPane_KindTextBox"));
}
