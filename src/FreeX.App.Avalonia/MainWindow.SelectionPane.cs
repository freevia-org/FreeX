using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;

using Free.Shared.Shell.Avalonia;
using FreeX.App.Presentation.DrawingUI;
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
/// translation come from the portable <see cref="SelectionPanePlanner"/> so this behaves identically to the
/// WPF host's Selection Pane and is reusable on macOS. Reached from the Picture/Shape Format contextual tabs'
/// "Selection Pane" buttons (pictureFormat.selectionPane / shapeFormat.selectionPane).
/// </summary>
public sealed partial class MainWindow
{
    private static AvaloniaCompactDialogChromeStyle SelectionPaneDialogChromeStyle => new(FormulaBarFontFamily);

    /// <summary>A mutable working row for the Selection Pane dialog (visibility + name edited in place).</summary>
    private sealed class SelectionPaneRow(SelectionPaneItem item)
    {
        public SelectionPaneItem Item { get; set; } = item;
        public Guid Id => Item.Id;
        public SelectionPaneObjectKind Kind => Item.Kind;
        public bool IsVisible { get; set; } = item.IsVisible;
        public string Name { get; set; } = item.Name;
    }

    private System.Threading.Tasks.Task OpenSelectionPaneDialogAsync() =>
        OpenSelectionPaneDialogAsync(captureItems: null);

    private async System.Threading.Tasks.Task OpenSelectionPaneDialogAsync(
        IReadOnlyList<SelectionPaneItem>? captureItems)
    {
        if (_isOpening || _isSaving)
            return;

        var sheet = _session.ActiveSheet;
        var planned = captureItems ?? SelectionPanePlanner.BuildItems(sheet, SelectionPaneText());
        if (planned.Count == 0)
        {
            RefreshShell(UiText.Get("SelectionPane_NoObjects"));
            return;
        }

        var originals = planned;
        var rows = planned.Select(item => new SelectionPaneRow(item)).ToList();
        var captureSelectedRow = captureItems is not null ? rows.FirstOrDefault() : null;

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
        AutomationProperties.SetAutomationId(listBox, "SelectionPaneObjectList");
        AutomationProperties.SetName(listBox, UiText.Get("SelectionPane_ObjectListLabel"));

        var searchBox = new TextBox { MinWidth = 160, Margin = new Thickness(0, 0, 10, 0) };
        ApplySelectionPaneTextBoxChrome(searchBox);
        AutomationProperties.SetAutomationId(searchBox, "SelectionPaneSearchBox");
        var filterBox = new ComboBox { MinWidth = 130 };
        ApplySelectionPaneComboBoxChrome(filterBox);
        foreach (var filter in new[] { "All", "Visible", "Hidden", "Charts", "Pictures", "Shapes", "Text Boxes" })
            filterBox.Items.Add(filter);
        filterBox.SelectedIndex = 0;
        AutomationProperties.SetAutomationId(filterBox, "SelectionPaneFilterBox");

        var renameBox = new TextBox { MinWidth = 160, Margin = new Thickness(0, 0, 6, 0) };
        ApplySelectionPaneTextBoxChrome(renameBox);
        AutomationProperties.SetAutomationId(renameBox, "SelectionPaneRenameBox");
        var renameButton = new Button { Content = UiText.Get("SelectionPane_RenameButton"), MinWidth = 78, Margin = new Thickness(0, 0, 6, 0) };
        ApplySelectionPaneButtonChrome(renameButton, 78);
        AutomationProperties.SetAutomationId(renameButton, "SelectionPaneRenameButton");
        var toggleVisibilityButton = new Button { Content = CreateSelectionPaneEyeIcon(), Width = 32, Margin = new Thickness(0, 0, 6, 0) };
        ApplySelectionPaneButtonChrome(toggleVisibilityButton, 32);
        AutomationProperties.SetAutomationId(toggleVisibilityButton, "SelectionPaneToggleVisibilityButton");

        var moveUpButton = new Button { Content = UiText.Get("SelectionPane_BringForward"), MinWidth = 104, Margin = new Thickness(0, 0, 6, 6) };
        ApplySelectionPaneButtonChrome(moveUpButton, 104);
        AutomationProperties.SetAutomationId(moveUpButton, "SelectionPaneBringForwardButton");
        var moveDownButton = new Button { Content = UiText.Get("SelectionPane_SendBackward"), MinWidth = 104, Margin = new Thickness(0, 0, 6, 6) };
        ApplySelectionPaneButtonChrome(moveDownButton, 104);
        AutomationProperties.SetAutomationId(moveDownButton, "SelectionPaneSendBackwardButton");
        var showAllButton = new Button { Content = UiText.Get("SelectionPane_ShowAll"), MinWidth = 82, Margin = new Thickness(0, 0, 6, 6) };
        ApplySelectionPaneButtonChrome(showAllButton, 82);
        AutomationProperties.SetAutomationId(showAllButton, "SelectionPaneShowAllButton");
        var hideAllButton = new Button { Content = UiText.Get("SelectionPane_HideAll"), MinWidth = 82, Margin = new Thickness(0, 0, 6, 6) };
        ApplySelectionPaneButtonChrome(hideAllButton, 82);
        AutomationProperties.SetAutomationId(hideAllButton, "SelectionPaneHideAllButton");

        bool MatchesFilter(SelectionPaneRow row)
        {
            var search = searchBox.Text?.Trim() ?? string.Empty;
            if (search.Length > 0 &&
                !row.Name.Contains(search, System.StringComparison.OrdinalIgnoreCase) &&
                !SelectionPaneKindLabel(row.Kind).Contains(search, System.StringComparison.OrdinalIgnoreCase))
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

            var currentStates = ToItemStates(rows);
            var currentIndex = rows.FindIndex(row => row.Id == selected.Id);
            moveUpButton.IsEnabled = SelectionPanePlanner.FindMoveTargetIndex(currentStates, currentIndex, forward: true) >= 0;
            moveDownButton.IsEnabled = SelectionPanePlanner.FindMoveTargetIndex(currentStates, currentIndex, forward: false) >= 0;
            if (!string.Equals(renameBox.Text, selected.Name, System.StringComparison.Ordinal))
                renameBox.Text = selected.Name;
            renameButton.IsEnabled = true;
            toggleVisibilityButton.IsEnabled = true;
        }

        // Pending move changes accumulate across button presses (z-order is applied as a sequence of one-step
        // moves so it round-trips through the existing MoveSelectionPaneObjectCommand and undo/redo).
        var moveChanges = new List<SelectionPaneMoveChange>();

        void Move(bool forward)
        {
            if (listBox.SelectedItem is not SelectionPaneRow selected)
                return;

            var plan = SelectionPanePlanner.PlanMove(ToItemStates(rows), selected.Id, forward);
            if (plan is null)
                return;

            moveChanges.AddRange(plan.MoveChanges);
            var byId = rows.ToDictionary(r => r.Id);
            rows.Clear();
            foreach (var id in plan.OrderedIds)
            {
                if (byId.TryGetValue(id, out var row))
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
                VerticalContentAlignment = AvaloniaVerticalAlignment.Center,
                HorizontalAlignment = AvaloniaHorizontalAlignment.Left,
                MinWidth = 0,
                Margin = new Thickness(0),
                Padding = new Thickness(0),
            };
            ApplySortOptionsCheckBoxChrome(visibilityBox);
            AutomationProperties.SetAutomationId(visibilityBox, "SelectionPaneVisibility_" + row.Id.ToString("N"));
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
                Height = 24,
                Padding = new Thickness(0, 1),
            };
            AutomationProperties.SetAutomationId(nameBox, "SelectionPaneName_" + row.Id.ToString("N"));
            AutomationProperties.SetName(nameBox, UiText.Get("SelectionPane_NameLabel"));
            nameBox.TextChanged += (_, _) => row.Name = nameBox.Text ?? string.Empty;

            var kindText = new TextBlock
            {
                Text = SelectionPaneKindLabel(row.Kind),
                Foreground = SecondaryInk,
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
                    new ColumnDefinition { Width = new GridLength(28) },
                    new ColumnDefinition { Width = new GridLength(150) },
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
            return rowGrid;
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
        KeyboardNavigation.SetTabNavigation(dialog, KeyboardNavigationMode.Cycle);

        var ok = new Button { Content = UiText.Get("Common_Ok"), IsDefault = true, Width = 78 };
        ApplySelectionPaneButtonChrome(ok, 78, isDefault: true);
        AutomationProperties.SetAutomationId(ok, "SelectionPaneOkButton");
        var cancel = new Button { Content = UiText.Get("Common_Cancel"), IsCancel = true, Width = 78 };
        ApplySelectionPaneButtonChrome(cancel, 78);
        AutomationProperties.SetAutomationId(cancel, "SelectionPaneCancelButton");
        ok.Click += (_, _) => dialog.Close(true);
        cancel.Click += (_, _) => dialog.Close(false);
        dialog.Opened += (_, _) => Dispatcher.UIThread.Post(
            () =>
            {
                if (dialog.IsVisible && searchBox.IsVisible && searchBox.IsEffectivelyEnabled)
                    searchBox.Focus();
            },
            DispatcherPriority.Input);
        dialog.AddHandler(
            InputElement.KeyDownEvent,
            (_, args) =>
            {
                if (args.Key != Key.Escape || args.KeyModifiers != KeyModifiers.None)
                    return;

                if (dialog.IsVisible)
                    dialog.Close(false);
                args.Handled = true;
            },
            RoutingStrategies.Tunnel,
            handledEventsToo: true);

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
            Children = { showAllButton, hideAllButton, moveUpButton, moveDownButton },
        };

        var buttonRow = AvaloniaCompactDialogChrome.CreateActionRow([ok, cancel]);
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
                // Stretch each row so the name column (star-width) fills the list and the type
                // column right-edge lines up row-to-row.
                new Setter(ContentControl.HorizontalContentAlignmentProperty, AvaloniaHorizontalAlignment.Stretch),
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

    private static void ApplySelectionPaneTextBoxChrome(TextBox textBox)
        => AvaloniaCompactDialogChrome.ApplyTextBox(textBox, SelectionPaneDialogChromeStyle);

    private static void ApplySelectionPaneComboBoxChrome(ComboBox comboBox)
        => AvaloniaCompactDialogChrome.ApplyComboBox(comboBox, SelectionPaneDialogChromeStyle);

    private static void ApplySelectionPaneButtonChrome(Button button, double width, bool isDefault = false)
    {
        button.Width = width;
        AvaloniaCompactDialogChrome.ApplyButton(button, SelectionPaneDialogChromeStyle, width, isDefault);
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
        IReadOnlyList<SelectionPaneItem> originals,
        IReadOnlyList<SelectionPaneRow> rows,
        IReadOnlyList<SelectionPaneMoveChange> moveChanges)
    {
        var current = ToItemStates(rows);
        var visibilityChanges = SelectionPanePlanner.CreateVisibilityChanges(originals, current);
        var renameChanges = SelectionPanePlanner.CreateRenameChanges(originals, current);
        var command = SelectionPanePlanner.CreateCommand(
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

    private static IReadOnlyList<SelectionPaneItemState> ToItemStates(IReadOnlyList<SelectionPaneRow> rows) =>
        rows
            .Select(row => new SelectionPaneItemState(row.Kind, row.Id, row.Name, row.IsVisible))
            .ToList();

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
}
