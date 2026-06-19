using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using FreeX.App.Presentation.DrawingUI;
using FreeX.Core.Model;

using AvaloniaHorizontalAlignment = Avalonia.Layout.HorizontalAlignment;
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

    private async System.Threading.Tasks.Task OpenSelectionPaneDialogAsync()
    {
        if (_isOpening || _isSaving)
            return;

        var sheet = _session.ActiveSheet;
        var planned = SelectionPaneViewPlanner.BuildItems(sheet, SelectionPaneText());
        if (planned.Count == 0)
        {
            RefreshShell(UiText.Get("SelectionPane_NoObjects"));
            return;
        }

        var originals = planned;
        var rows = planned.Select(item => new SelectionPaneRow(item)).ToList();

        var listBox = new ListBox
        {
            MinHeight = 220,
            SelectionMode = SelectionMode.Single,
        };
        AutomationProperties.SetAutomationId(listBox, "SelectionPaneObjectList");
        AutomationProperties.SetName(listBox, UiText.Get("SelectionPane_ObjectListLabel"));

        var moveUpButton = new Button { Content = UiText.Get("SelectionPane_BringForward"), MinWidth = 120 };
        AutomationProperties.SetAutomationId(moveUpButton, "SelectionPaneBringForwardButton");
        var moveDownButton = new Button { Content = UiText.Get("SelectionPane_SendBackward"), MinWidth = 120 };
        AutomationProperties.SetAutomationId(moveDownButton, "SelectionPaneSendBackwardButton");
        var showAllButton = new Button { Content = UiText.Get("SelectionPane_ShowAll"), MinWidth = 84 };
        AutomationProperties.SetAutomationId(showAllButton, "SelectionPaneShowAllButton");
        var hideAllButton = new Button { Content = UiText.Get("SelectionPane_HideAll"), MinWidth = 84 };
        AutomationProperties.SetAutomationId(hideAllButton, "SelectionPaneHideAllButton");

        void Rebind(Guid? preferredSelection)
        {
            var selected = preferredSelection ?? (listBox.SelectedItem as SelectionPaneRow)?.Id;
            listBox.ItemsSource = null;
            listBox.ItemsSource = rows;
            if (selected is { } id)
            {
                var match = rows.FirstOrDefault(r => r.Id == id);
                listBox.SelectedItem = match ?? rows.FirstOrDefault();
            }
            else
            {
                listBox.SelectedItem = rows.FirstOrDefault();
            }
        }

        void UpdateMoveButtons()
        {
            if (listBox.SelectedItem is not SelectionPaneRow selected)
            {
                moveUpButton.IsEnabled = false;
                moveDownButton.IsEnabled = false;
                return;
            }

            moveUpButton.IsEnabled = selected.Item.CanMoveUp;
            moveDownButton.IsEnabled = selected.Item.CanMoveDown;
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
                MinWidth = 220,
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
            Width = 440,
            Height = 420,
            MinWidth = 380,
            MinHeight = 320,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };
        AutomationProperties.SetAutomationId(dialog, "SelectionPaneDialog");

        var ok = new Button { Content = UiText.Get("Common_Ok"), IsDefault = true, MinWidth = 80 };
        AutomationProperties.SetAutomationId(ok, "SelectionPaneOkButton");
        var cancel = new Button { Content = UiText.Get("Common_Cancel"), IsCancel = true, MinWidth = 80 };
        AutomationProperties.SetAutomationId(cancel, "SelectionPaneCancelButton");
        ok.Click += (_, _) => dialog.Close(true);
        cancel.Click += (_, _) => dialog.Close(false);

        var reorderRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children = { moveUpButton, moveDownButton, showAllButton, hideAllButton },
        };

        var content = new DockPanel { Margin = new Thickness(12), LastChildFill = true };

        var hint = new TextBlock
        {
            Text = UiText.Get("SelectionPane_Hint"),
            Foreground = HeaderForeground,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8),
        };
        DockPanel.SetDock(hint, Dock.Top);
        content.Children.Add(hint);

        DockPanel.SetDock(reorderRow, Dock.Bottom);
        reorderRow.Margin = new Thickness(0, 8, 0, 0);
        content.Children.Add(reorderRow);

        var buttonRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Right,
            Margin = new Thickness(0, 8, 0, 0),
            Children = { ok, cancel },
        };
        DockPanel.SetDock(buttonRow, Dock.Bottom);
        content.Children.Add(buttonRow);

        content.Children.Add(listBox);
        dialog.Content = content;

        Rebind(null);
        UpdateMoveButtons();

        var confirmed = await dialog.ShowDialog<bool>(this);
        if (!confirmed)
            return;

        ApplySelectionPaneChanges(originals, rows, moveChanges);
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
