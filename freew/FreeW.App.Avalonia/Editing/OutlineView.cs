using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Editing;

/// <summary>
/// Word's View &gt; Outline surface for the Avalonia host. The rows are produced by the shared
/// <see cref="OutlineViewModel"/> and every edit is routed through <see cref="DocumentView"/>'s
/// undoable heading operations. The control is a presentation surface; entering and leaving it never
/// changes the document just because the view changed.
/// </summary>
internal sealed class OutlineView : Border
{
    private readonly DocumentView _editor;
    private readonly ListBox _list;
    private readonly ComboBox _showLevel;
    private readonly ComboBox _outlineLevelCombo;
    private bool _updatingLevelCombo;
    private int _selectedShowLevel = OutlineViewModel.ShowAllLevels;
    private bool _firstLineOnly;

    public OutlineView(DocumentView editor)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        Background = Brushes.White;

        _list = new ListBox
        {
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            FontFamily = new FontFamily("Calibri"),
            FontSize = 15,
        };
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
        combo.Items.Add(new ShowLevelItem("All Levels", OutlineViewModel.ShowAllLevels));
        for (var level = OutlineViewModel.MinShowLevel; level <= OutlineViewModel.MaxShowLevel; level++)
            combo.Items.Add(new ShowLevelItem($"Level {level}", level));
        combo.SelectedIndex = 0;
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedItem is ShowLevelItem item)
            {
                _selectedShowLevel = item.Level;
                Refresh();
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
        combo.Items.Add(new OutlineLevelItem("Body Text", -1));
        combo.Items.Add(new OutlineLevelItem("Title", 0));
        for (var level = 1; level <= OutlineTools.MaxHeadingLevel; level++)
            combo.Items.Add(new OutlineLevelItem($"Level {level}", level));
        combo.SelectedIndex = 0;
        combo.SelectionChanged += (_, _) =>
        {
            if (!_updatingLevelCombo && combo.SelectedItem is OutlineLevelItem item)
                ApplyOutlineLevel(item.Level);
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

        bar.Children.Add(Label("Show Level:"));
        bar.Children.Add(_showLevel);
        bar.Children.Add(Spacer());
        bar.Children.Add(Label("Outline Level:"));
        bar.Children.Add(_outlineLevelCombo);
        bar.Children.Add(Spacer());
        bar.Children.Add(ToolButton("Promote to Heading 1", "Promote to Heading 1", () => Apply(i => _editor.PromoteHeadingToHeading1(i))));
        bar.Children.Add(ToolButton("Promote", "Promote", () => Apply(i => _editor.PromoteHeading(i))));
        bar.Children.Add(ToolButton("Demote", "Demote", () => Apply(i => _editor.DemoteHeading(i))));
        bar.Children.Add(Spacer());
        bar.Children.Add(ToolButton("Move Up", "Move Up", () => Move(moveUp: true)));
        bar.Children.Add(ToolButton("Move Down", "Move Down", () => Move(moveUp: false)));
        bar.Children.Add(Spacer());
        bar.Children.Add(ToolButton("Expand", "Expand", () => Apply(i => _editor.ExpandHeading(i))));
        bar.Children.Add(ToolButton("Collapse", "Collapse", () => Apply(i => _editor.CollapseHeading(i))));

        var firstLine = new CheckBox
        {
            Content = "Show First Line Only",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0),
        };
        firstLine.IsCheckedChanged += (_, _) =>
        {
            _firstLineOnly = firstLine.IsChecked == true;
            Refresh();
        };
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
        if (_list.SelectedItem is not OutlineRowItem selected)
        {
            UpdateOutlineLevelCombo();
            return;
        }

        // Selecting an outline row is navigation, not an edit. Move the editor caret to the source
        // block so subsequent ribbon commands and keyboard actions target the selected row.
        _editor.MoveCaretToBlock(selected.Row.BlockIndex, 0);
        UpdateOutlineLevelCombo();
    }

    private void UpdateOutlineLevelCombo()
    {
        _updatingLevelCombo = true;
        try
        {
            var level = _list.SelectedItem is OutlineRowItem selected && selected.Row.IsHeading
                ? selected.Row.Level
                : -1;
            foreach (var item in _outlineLevelCombo.Items.OfType<OutlineLevelItem>())
            {
                if (item.Level == level)
                {
                    _outlineLevelCombo.SelectedItem = item;
                    return;
                }
            }
            _outlineLevelCombo.SelectedIndex = 0;
        }
        finally
        {
            _updatingLevelCombo = false;
        }
    }

    /// <summary>Refreshes rows from the current document while preserving the selected block.</summary>
    public void Refresh()
    {
        var selectedBlock = (_list.SelectedItem as OutlineRowItem)?.Row.BlockIndex ?? -1;
        var rows = OutlineViewModel.Build(_editor.Document, _selectedShowLevel, _firstLineOnly);

        _list.SelectionChanged -= OnSelectionChanged;
        try
        {
            _list.Items.Clear();
            OutlineRowItem? toSelect = null;
            foreach (var row in rows)
            {
                var item = new OutlineRowItem(row, _editor.IsHeadingCollapsed(row.BlockIndex));
                _list.Items.Add(item);
                if (row.BlockIndex == selectedBlock)
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

    private void Apply(Action<int> command)
    {
        if (_list.SelectedItem is not OutlineRowItem selected)
            return;
        var blockIndex = selected.Row.BlockIndex;
        command(blockIndex);
        Refresh();
        SelectBlock(blockIndex);
    }

    private void Move(bool moveUp)
    {
        if (_list.SelectedItem is not OutlineRowItem selected)
            return;
        var newIndex = _editor.MoveHeading(selected.Row.BlockIndex, moveUp);
        Refresh();
        SelectBlock(newIndex);
    }

    private void ApplyOutlineLevel(int level)
    {
        if (_list.SelectedItem is not OutlineRowItem selected)
            return;
        _editor.SetHeadingLevel(selected.Row.BlockIndex, level);
        Refresh();
        SelectBlock(selected.Row.BlockIndex);
    }

    private void SelectBlock(int blockIndex)
    {
        foreach (var item in _list.Items.OfType<OutlineRowItem>())
        {
            if (item.Row.BlockIndex == blockIndex)
            {
                _list.SelectedItem = item;
                return;
            }
        }
    }

    // Test seams mirror the WPF outline surface and keep assertions on actual rows/actions.
    internal IReadOnlyList<OutlineRow> VisibleRows =>
        _list.Items.OfType<OutlineRowItem>().Select(item => item.Row).ToList();

    internal int? SelectedBlockIndex =>
        (_list.SelectedItem as OutlineRowItem)?.Row.BlockIndex;

    internal string? RowDisplayTextForTests(int blockIndex) =>
        _list.Items.OfType<OutlineRowItem>()
            .FirstOrDefault(item => item.Row.BlockIndex == blockIndex)
            ?.ToString();

    internal void SelectBlockIndex(int blockIndex) => SelectBlock(blockIndex);

    internal void SetShowLevel(int level)
    {
        _selectedShowLevel = level;
        Refresh();
    }

    internal void SetFirstLineOnly(bool firstLineOnly)
    {
        _firstLineOnly = firstLineOnly;
        Refresh();
    }

    internal void SetOutlineLevel(int level) => ApplyOutlineLevel(level);

    internal void PromoteSelectedForTests() => Apply(i => _editor.PromoteHeading(i));

    internal void DemoteSelectedForTests() => Apply(i => _editor.DemoteHeading(i));

    internal void PromoteSelectedToHeading1ForTests() => Apply(i => _editor.PromoteHeadingToHeading1(i));

    internal void MoveSelectedForTests(bool moveUp) => Move(moveUp);

    internal void CollapseSelectedForTests() => Apply(i => _editor.CollapseHeading(i));

    internal void ExpandSelectedForTests() => Apply(i => _editor.ExpandHeading(i));

    internal int CurrentOutlineLevel =>
        (_outlineLevelCombo.SelectedItem as OutlineLevelItem)?.Level ?? -1;

    private sealed class ShowLevelItem(string label, int level)
    {
        public int Level { get; } = level;
        private readonly string _label = label;
        public override string ToString() => _label;
    }

    private sealed class OutlineLevelItem(string label, int level)
    {
        public int Level { get; } = level;
        private readonly string _label = label;
        public override string ToString() => _label;
    }

    private sealed class OutlineRowItem(OutlineRow row, bool collapsed)
    {
        public OutlineRow Row { get; } = row;
        private readonly bool _collapsed = collapsed;

        public override string ToString()
        {
            var indent = new string(' ', Math.Max(0, Row.Level) * 4);
            var marker = Row.IsHeading ? (_collapsed ? "[+] " : "[-] ") : "    ";
            var text = Row.Text.Length > 0 ? Row.Text : Row.IsHeading ? "(untitled heading)" : string.Empty;
            return indent + marker + text;
        }
    }
}
