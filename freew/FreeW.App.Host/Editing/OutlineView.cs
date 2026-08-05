using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FreeW.App.Presentation.Editing;
using FreeW.Core.Model;

namespace FreeW.App.Host.Editing;

/// <summary>
/// Word's Outline view (View &gt; Views &gt; Outline): the document shown as an indented heading/body
/// outline with an "Outlining" mini-toolbar (Show Level, Promote / Demote / Promote to Heading 1, Move
/// Up / Down, Expand / Collapse, Show First Line Only). It is a presentation surface over the existing
/// <see cref="DocumentView"/> model — every restructuring command reuses the editor's reversible heading
/// operations (<see cref="DocumentView.PromoteHeading"/>, <see cref="DocumentView.DemoteHeading"/>,
/// <see cref="DocumentView.MoveHeading"/>, <see cref="DocumentView.CollapseHeading"/>,
/// <see cref="DocumentView.ExpandHeading"/>). Shared state transitions and row projection live in
/// <see cref="OutlineViewController"/>. Nothing here mutates the model directly, so toggling back to Print
/// Layout restores the normal editing surface untouched.
/// </summary>
internal sealed class OutlineView : Border
{
    private readonly DocumentView _editor;
    private readonly OutlineViewController _controller;
    private readonly ListBox _list;
    private readonly ComboBox _showLevel;
    private ComboBox _outlineLevelCombo = null!;
    private bool _updatingLevelCombo;

    public OutlineView(DocumentView editor)
    {
        _editor = editor;
        _controller = new OutlineViewController(GetCommittedDocument, _editor.SetHeadingLevel, _editor.MoveHeading);

        Background = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));

        _list = new ListBox
        {
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            FontFamily = new FontFamily("Calibri"),
            FontSize = 15
        };
        _controller.RowsChanged += RenderRows;
        _list.SelectionChanged += OnSelectionChanged;

        _showLevel = new ComboBox { Width = 120, VerticalAlignment = VerticalAlignment.Center };
        _outlineLevelCombo = BuildOutlineLevelCombo();
        var toolbar = BuildToolbar();

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(toolbar, 0);
        Grid.SetRow(_list, 1);
        grid.Children.Add(toolbar);
        grid.Children.Add(_list);
        Child = grid;
    }

    // The "Outlining" mini-toolbar: the Show-Level box plus the outline restructuring buttons, all wired
    // straight to the editor's existing reversible heading commands so the outline and the document agree.
    private UIElement BuildToolbar()
    {
        var bar = new WrapPanel
        {
            Margin = new Thickness(8, 6, 8, 6),
            VerticalAlignment = VerticalAlignment.Center
        };

        Button ToolButton(string content, string tip, Action onClick)
        {
            var button = new Button
            {
                Content = content,
                ToolTip = tip,
                MinWidth = 28,
                Margin = new Thickness(2, 0, 2, 0),
                Padding = new Thickness(6, 2, 6, 2),
                VerticalAlignment = VerticalAlignment.Center
            };
            button.Click += (_, _) => onClick();
            return button;
        }

        bar.Children.Add(new TextBlock
        {
            Text = "Show Level:",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0)
        });

        _showLevel.Items.Add(new ShowLevelItem("All Levels", OutlineViewModel.ShowAllLevels));
        for (var level = OutlineViewModel.MinShowLevel; level <= OutlineViewModel.MaxShowLevel; level++)
            _showLevel.Items.Add(new ShowLevelItem($"Level {level}", level));
        _showLevel.SelectedIndex = 0;
        _showLevel.SelectionChanged += (_, _) =>
        {
            if (_showLevel.SelectedItem is ShowLevelItem item)
            {
                _controller.SetShowLevel(item.Level);
            }
        };
        bar.Children.Add(_showLevel);

        bar.Children.Add(Spacer());
        bar.Children.Add(new TextBlock
        {
            Text = "Outline Level:",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0)
        });
        bar.Children.Add(_outlineLevelCombo);

        bar.Children.Add(Spacer());
        bar.Children.Add(ToolButton("⟪", "Promote to Heading 1", () => _controller.Apply(_editor.PromoteHeadingToHeading1)));
        bar.Children.Add(ToolButton("◄", "Promote", () => _controller.Apply(_editor.PromoteHeading)));
        bar.Children.Add(ToolButton("►", "Demote", () => _controller.Apply(_editor.DemoteHeading)));

        bar.Children.Add(Spacer());
        bar.Children.Add(ToolButton("▲", "Move Up", () => _controller.Move(moveUp: true)));
        bar.Children.Add(ToolButton("▼", "Move Down", () => _controller.Move(moveUp: false)));

        bar.Children.Add(Spacer());
        bar.Children.Add(ToolButton("+", "Expand", () => _controller.Apply(_editor.ExpandHeading)));
        bar.Children.Add(ToolButton("−", "Collapse", () => _controller.Apply(_editor.CollapseHeading)));

        bar.Children.Add(Spacer());
        var firstLine = new CheckBox
        {
            Content = "Show First Line Only",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0, 0, 0)
        };
        firstLine.Checked += (_, _) => _controller.SetFirstLineOnly(true);
        firstLine.Unchecked += (_, _) => _controller.SetFirstLineOnly(false);
        bar.Children.Add(firstLine);

        return new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xD0, 0xD0, 0xD0)),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Background = new SolidColorBrush(Color.FromRgb(0xFA, 0xFA, 0xFA)),
            Child = bar
        };
    }

    private ComboBox BuildOutlineLevelCombo()
    {
        var combo = new ComboBox { Width = 130, VerticalAlignment = VerticalAlignment.Center };
        combo.Items.Add(new OutlineLevelItem("Body Text", -1));
        combo.Items.Add(new OutlineLevelItem("Title", 0));
        for (var lvl = 1; lvl <= OutlineTools.MaxHeadingLevel; lvl++)
            combo.Items.Add(new OutlineLevelItem($"Level {lvl}", lvl));
        combo.SelectedIndex = 0;
        combo.SelectionChanged += (_, _) =>
        {
            if (!_updatingLevelCombo && combo.SelectedItem is OutlineLevelItem item)
                _controller.SetOutlineLevel(item.Level);
        };
        return combo;
    }
    private void UpdateOutlineLevelCombo()
    {
        if (_outlineLevelCombo is null) return;
        _updatingLevelCombo = true;
        try
        {
            var targetLevel = _controller.CurrentOutlineLevel;
            foreach (var item in _outlineLevelCombo.Items.OfType<OutlineLevelItem>())
            {
                if (item.Level == targetLevel)
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

    private static UIElement Spacer() =>
        new Border { Width = 10 };

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_list.SelectedItem is OutlineRowItem selected)
            _controller.SelectBlock(selected.Row.BlockIndex);
        else
            _controller.ClearSelection();

        UpdateOutlineLevelCombo();
    }

    private TextDocument GetCommittedDocument()
    {
        _editor.CommitToModel();
        return _editor.Model;
    }

    public void Refresh() => _controller.Refresh();

    private void RenderRows()
    {
        _list.SelectionChanged -= OnSelectionChanged;
        try
        {
            _list.Items.Clear();
            OutlineRowItem? toSelect = null;
            foreach (var row in _controller.VisibleRows)
            {
                var item = new OutlineRowItem(row, _editor.IsHeadingCollapsed(row.BlockIndex));
                _list.Items.Add(item);
                if (row.BlockIndex == _controller.SelectedBlockIndex)
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

        foreach (var listItem in _list.Items)
        {
            if (listItem is OutlineRowItem item && item.Row.BlockIndex == blockIndex)
            {
                _list.SelectedItem = item;
                return;
            }
        }
    }

    // --- Test seams (FreeW.App.Host.Tests has InternalsVisibleTo) -------------------------------------

    /// <summary>The block indices currently shown in the outline (document order). For tests.</summary>
    internal IReadOnlyList<OutlineRow> VisibleRows => _controller.VisibleRows;

    /// <summary>Select the row mapping to <paramref name="blockIndex"/> (test seam for command targeting).</summary>
    internal void SelectBlockIndex(int blockIndex) => SelectBlock(blockIndex);

    /// <summary>Choose a "Show Level" (1..9 or <see cref="OutlineViewModel.ShowAllLevels"/>) and refresh. For tests.</summary>
    internal void SetShowLevel(int level) => _controller.SetShowLevel(level);

    /// <summary>Toggle "Show First Line Only" and refresh. For tests.</summary>
    internal void SetFirstLineOnly(bool firstLineOnly) => _controller.SetFirstLineOnly(firstLineOnly);

    /// <summary>
    /// Apply an outline level (-1 = Body Text, 0 = Title, 1..MaxHeadingLevel = Heading) to the
    /// currently selected row. For tests.
    /// </summary>
    internal void SetOutlineLevel(int level) => _controller.SetOutlineLevel(level);

    /// <summary>The level currently shown in the Outline Level combo (-1 = Body Text / 0 = Title / 1–N = HeadingN). For tests.</summary>
    internal int CurrentOutlineLevel => _controller.CurrentOutlineLevel;

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

    // One outline row: indents the text by its outline level, prefixes a heading marker (collapsed
    // headings get a "+"), and remembers the source row so a command can map back to the model block index.
    private sealed class OutlineRowItem(OutlineRow row, bool collapsed)
    {
        public OutlineRow Row { get; } = row;
        private readonly bool _collapsed = collapsed;

        public override string ToString()
        {
            var indent = new string(' ', Row.Level * 4);
            var marker = Row.IsHeading ? (_collapsed ? "⊞ " : "▢ ") : "    ";
            var text = Row.Text.Length > 0 ? Row.Text : (Row.IsHeading ? "(untitled heading)" : string.Empty);
            return indent + marker + text;
        }
    }
}
