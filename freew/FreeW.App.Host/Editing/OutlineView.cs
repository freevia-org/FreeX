using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FreeW.Core.Model;

namespace FreeW.App.Host.Editing;

/// <summary>
/// Word's Outline view (View &gt; Views &gt; Outline): the document shown as an indented heading/body
/// outline with an "Outlining" mini-toolbar (Show Level, Promote / Demote / Promote to Heading 1, Move
/// Up / Down, Expand / Collapse, Show First Line Only). It is a presentation surface over the existing
/// <see cref="DocumentView"/> model — every restructuring command reuses the editor's reversible heading
/// operations (<see cref="DocumentView.PromoteHeading"/>, <see cref="DocumentView.DemoteHeading"/>,
/// <see cref="DocumentView.MoveHeading"/>, <see cref="DocumentView.CollapseHeading"/>,
/// <see cref="DocumentView.ExpandHeading"/>) and the rows come from the pure
/// <see cref="OutlineViewModel.Build"/>. Nothing here mutates the model directly, so toggling back to
/// Print Layout restores the normal editing surface untouched.
/// </summary>
internal sealed class OutlineView : Border
{
    private readonly DocumentView _editor;
    private readonly ListBox _list;
    private readonly ComboBox _showLevel;
    private int _selectedShowLevel = OutlineViewModel.ShowAllLevels;
    private bool _firstLineOnly;

    public OutlineView(DocumentView editor)
    {
        _editor = editor;

        Background = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));

        _list = new ListBox
        {
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            FontFamily = new FontFamily("Calibri"),
            FontSize = 15
        };

        _showLevel = new ComboBox { Width = 120, VerticalAlignment = VerticalAlignment.Center };
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
                _selectedShowLevel = item.Level;
                Refresh();
            }
        };
        bar.Children.Add(_showLevel);

        bar.Children.Add(Spacer());
        // Promote to Heading 1, Promote, Demote — Word's outline left/right arrows.
        bar.Children.Add(ToolButton("⟪", "Promote to Heading 1", () => Apply(i => _editor.PromoteHeadingToHeading1(i))));
        bar.Children.Add(ToolButton("◄", "Promote", () => Apply(i => _editor.PromoteHeading(i))));
        bar.Children.Add(ToolButton("►", "Demote", () => Apply(i => _editor.DemoteHeading(i))));

        bar.Children.Add(Spacer());
        // Move Up / Move Down — relocate the heading subtree (reuses MoveHeading / OutlineTools.MoveSubtree).
        bar.Children.Add(ToolButton("▲", "Move Up", () => Move(moveUp: true)));
        bar.Children.Add(ToolButton("▼", "Move Down", () => Move(moveUp: false)));

        bar.Children.Add(Spacer());
        // Expand / Collapse the selected heading's body (view-only, reuses CollapseHeading / ExpandHeading).
        bar.Children.Add(ToolButton("+", "Expand", () => Apply(i => _editor.ExpandHeading(i))));
        bar.Children.Add(ToolButton("−", "Collapse", () => Apply(i => _editor.CollapseHeading(i))));

        bar.Children.Add(Spacer());
        var firstLine = new CheckBox
        {
            Content = "Show First Line Only",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0, 0, 0)
        };
        firstLine.Checked += (_, _) => { _firstLineOnly = true; Refresh(); };
        firstLine.Unchecked += (_, _) => { _firstLineOnly = false; Refresh(); };
        bar.Children.Add(firstLine);

        return new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xD0, 0xD0, 0xD0)),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Background = new SolidColorBrush(Color.FromRgb(0xFA, 0xFA, 0xFA)),
            Child = bar
        };
    }

    private static UIElement Spacer() =>
        new Border { Width = 10 };

    // Rebuild the outline rows from the editor's committed model, honouring the current Show-Level filter
    // and First-Line-Only preference, then repopulate the list while preserving the selected block index.
    public void Refresh()
    {
        _editor.CommitToModel();
        var selectedBlock = (_list.SelectedItem as OutlineRowItem)?.Row.BlockIndex ?? -1;

        var rows = OutlineViewModel.Build(_editor.Model, _selectedShowLevel, _firstLineOnly);

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

    // Run a heading command against the selected row's block index, then refresh so promoted/demoted
    // levels and collapse markers update. A no-op when nothing is selected.
    private void Apply(Action<int> command)
    {
        if (_list.SelectedItem is not OutlineRowItem selected)
            return;
        command(selected.Row.BlockIndex);
        Refresh();
    }

    // Move the selected heading subtree one sibling position, then re-select it at its new index so it
    // stays highlighted (mirrors the nav-pane Move Up / Move Down behaviour).
    private void Move(bool moveUp)
    {
        if (_list.SelectedItem is not OutlineRowItem selected)
            return;
        var newIndex = _editor.MoveHeading(selected.Row.BlockIndex, moveUp);
        Refresh();
        SelectBlock(newIndex);
    }

    // Select the row mapping to the given model block index (no-op when it is not currently shown).
    private void SelectBlock(int blockIndex)
    {
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
    internal IReadOnlyList<OutlineRow> VisibleRows =>
        _list.Items.OfType<OutlineRowItem>().Select(item => item.Row).ToList();

    /// <summary>Select the row mapping to <paramref name="blockIndex"/> (test seam for command targeting).</summary>
    internal void SelectBlockIndex(int blockIndex) => SelectBlock(blockIndex);

    /// <summary>Choose a "Show Level" (1..9 or <see cref="OutlineViewModel.ShowAllLevels"/>) and refresh. For tests.</summary>
    internal void SetShowLevel(int level)
    {
        _selectedShowLevel = level;
        Refresh();
    }

    /// <summary>Toggle "Show First Line Only" and refresh. For tests.</summary>
    internal void SetFirstLineOnly(bool firstLineOnly)
    {
        _firstLineOnly = firstLineOnly;
        Refresh();
    }

    // A Show-Level dropdown entry: a label plus the level it selects (or ShowAllLevels).
    private sealed class ShowLevelItem(string label, int level)
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
