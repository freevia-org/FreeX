using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using FreeW.App.Presentation.Editing;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Editing;

/// <summary>
/// Word's View &gt; Outline surface for the Avalonia host. Shared state transitions and row projection live
/// in <see cref="OutlineViewController"/>, while edits use <see cref="DocumentView"/>'s undoable heading
/// operations. The control is a presentation surface; entering and leaving it never changes the document
/// just because the view changed.
/// </summary>
internal sealed partial class OutlineView : Border
{
    private static readonly OutlineRowMarkers RowMarkers = new("[-] ", "[+] ", "    ");

    private readonly DocumentView _editor;
    private readonly OutlineViewController _controller;
    private readonly ListBox _list;
    private readonly ComboBox _showLevel;
    private readonly ComboBox _outlineLevelCombo;
    private bool _updatingLevelCombo;

    public OutlineView(DocumentView editor)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        _controller = new OutlineViewController(new OutlineViewOperations(
            getDocument: () => _editor.Document,
            setHeadingLevel: _editor.SetHeadingLevel,
            moveHeading: _editor.MoveHeading,
            promoteToHeading1: _editor.PromoteHeadingToHeading1,
            promote: _editor.PromoteHeading,
            demote: _editor.DemoteHeading,
            expand: _editor.ExpandHeading,
            collapse: _editor.CollapseHeading,
            isHeadingCollapsed: _editor.IsHeadingCollapsed,
            navigateToBlock: blockIndex => _editor.MoveCaretToBlock(blockIndex, 0)));
        Background = Brushes.White;

        _list = new ListBox
        {
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            FontFamily = new FontFamily("Calibri"),
            FontSize = 15,
        };
        _controller.RowsChanged += RenderRows;
        _list.SelectionChanged += OnSelectionChanged;

        _showLevel = BuildShowLevelCombo();
        _outlineLevelCombo = BuildOutlineLevelCombo();

        var layout = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = new GridLength(1, GridUnitType.Star) },
            },
        };
        var toolbar = BuildToolbar();
        Grid.SetRow(toolbar, 0);
        Grid.SetRow(_list, 1);
        layout.Children.Add(toolbar);
        layout.Children.Add(_list);
        Child = layout;
    }

    private ComboBox BuildShowLevelCombo()
    {
        var combo = new ComboBox
        {
            Width = 120,
            VerticalAlignment = VerticalAlignment.Center,
        };
        foreach (var option in OutlineViewPlanner.ShowLevelOptions)
            combo.Items.Add(option);
        combo.SelectedIndex = 0;
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedItem is OutlineLevelOption item)
            {
                _controller.SetShowLevel(item.Level);
            }
        };
        return combo;
    }

    private ComboBox BuildOutlineLevelCombo()
    {
        var combo = new ComboBox
        {
            Width = 130,
            VerticalAlignment = VerticalAlignment.Center,
        };
        foreach (var option in OutlineViewPlanner.OutlineLevelOptions)
            combo.Items.Add(option);
        combo.SelectedIndex = 0;
        combo.SelectionChanged += (_, _) =>
        {
            if (!_updatingLevelCombo && combo.SelectedItem is OutlineLevelOption item)
                _controller.SetOutlineLevel(item.Level);
        };
        return combo;
    }

    private Control BuildToolbar()
    {
        var bar = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(8, 6),
            VerticalAlignment = VerticalAlignment.Center,
        };

        bar.Children.Add(Label(OutlineViewPlanner.ShowLevelLabel));
        bar.Children.Add(_showLevel);
        bar.Children.Add(Spacer());
        bar.Children.Add(Label(OutlineViewPlanner.OutlineLevelLabel));
        bar.Children.Add(_outlineLevelCombo);
        bar.Children.Add(Spacer());
        foreach (var command in OutlineViewPlanner.CommandPlans)
        {
            if (command.StartsGroup)
                bar.Children.Add(Spacer());
            bar.Children.Add(ToolButton(
                command.Label,
                command.Label,
                () => _controller.Execute(command.Command)));
        }

        var firstLine = new CheckBox
        {
            Content = OutlineViewPlanner.ShowFirstLineOnlyLabel,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0),
        };
        firstLine.IsCheckedChanged += (_, _) => _controller.SetFirstLineOnly(firstLine.IsChecked == true);
        bar.Children.Add(firstLine);

        return new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xD0, 0xD0, 0xD0)),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Background = new SolidColorBrush(Color.FromRgb(0xFA, 0xFA, 0xFA)),
            Child = bar,
        };
    }

    private static TextBlock Label(string text) => new()
    {
        Text = text,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(0, 0, 6, 0),
    };

    private static Button ToolButton(string text, string tip, Action action)
    {
        var button = new Button
        {
            Content = text,
            MinWidth = 28,
            Margin = new Thickness(2, 0),
            Padding = new Thickness(6, 2),
            VerticalAlignment = VerticalAlignment.Center,
        };
        ToolTip.SetTip(button, tip);
        button.Click += (_, _) => action();
        return button;
    }

    private static Border Spacer() => new() { Width = 10 };

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_list.SelectedItem is not OutlineDisplayRow selected)
        {
            _controller.ClearSelection();
            UpdateOutlineLevelCombo();
            return;
        }

        _controller.SelectBlock(selected.Row.BlockIndex, navigate: true);
        UpdateOutlineLevelCombo();
    }

    private void UpdateOutlineLevelCombo()
    {
        _updatingLevelCombo = true;
        try
        {
            _outlineLevelCombo.SelectedIndex =
                OutlineViewPlanner.OutlineLevelOptionIndex(_controller.CurrentOutlineLevel);
        }
        finally
        {
            _updatingLevelCombo = false;
        }
    }

    /// <summary>Refreshes rows from the current document while preserving the selected block.</summary>
    public void Refresh() => _controller.Refresh();

    private void RenderRows()
    {
        _list.SelectionChanged -= OnSelectionChanged;
        try
        {
            _list.Items.Clear();
            OutlineDisplayRow? toSelect = null;
            foreach (var item in OutlineViewPlanner.BuildDisplayRows(_controller.ProjectedRows, RowMarkers))
            {
                _list.Items.Add(item);
                if (item.Row.BlockIndex == _controller.SelectedBlockIndex)
                    toSelect = item;
            }
            _list.SelectedItem = toSelect;
        }
        finally
        {
            _list.SelectionChanged += OnSelectionChanged;
        }
        UpdateOutlineLevelCombo();
    }

    private void SelectBlock(int blockIndex)
    {
        if (!_controller.SelectBlock(blockIndex))
            return;

        foreach (var item in _list.Items.OfType<OutlineDisplayRow>())
        {
            if (item.Row.BlockIndex == blockIndex)
            {
                _list.SelectedItem = item;
                return;
            }
        }
    }

}
