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
    private static readonly OutlineRowMarkers RowMarkers = new("▢ ", "⊞ ", "    ");

    private readonly DocumentView _editor;
    private readonly OutlineViewController _controller;
    private readonly ListBox _list;
    private readonly ComboBox _showLevel;
    private ComboBox _outlineLevelCombo = null!;
    private bool _updatingLevelCombo;

    public OutlineView(DocumentView editor)
    {
        _editor = editor;
        _controller = new OutlineViewController(new OutlineViewOperations(
            getDocument: GetCommittedDocument,
            setHeadingLevel: _editor.SetHeadingLevel,
            moveHeading: _editor.MoveHeading,
            promoteToHeading1: _editor.PromoteHeadingToHeading1,
            promote: _editor.PromoteHeading,
            demote: _editor.DemoteHeading,
            expand: _editor.ExpandHeading,
            collapse: _editor.CollapseHeading,
            isHeadingCollapsed: _editor.IsHeadingCollapsed));

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

    // Native WPF control construction for the portable outline toolbar plan.
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
            Text = OutlineViewPlanner.ShowLevelLabel,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0)
        });

        foreach (var option in OutlineViewPlanner.ShowLevelOptions)
            _showLevel.Items.Add(option);
        _showLevel.SelectedIndex = 0;
        _showLevel.SelectionChanged += (_, _) =>
        {
            if (_showLevel.SelectedItem is OutlineLevelOption item)
            {
                _controller.SetShowLevel(item.Level);
            }
        };
        bar.Children.Add(_showLevel);

        bar.Children.Add(Spacer());
        bar.Children.Add(new TextBlock
        {
            Text = OutlineViewPlanner.OutlineLevelLabel,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0)
        });
        bar.Children.Add(_outlineLevelCombo);

        bar.Children.Add(Spacer());
        foreach (var command in OutlineViewPlanner.CommandPlans)
        {
            if (command.StartsGroup)
                bar.Children.Add(Spacer());
            bar.Children.Add(ToolButton(
                CommandContent(command.Command),
                command.Label,
                () => _controller.Execute(command.Command)));
        }

        bar.Children.Add(Spacer());
        var firstLine = new CheckBox
        {
            Content = OutlineViewPlanner.ShowFirstLineOnlyLabel,
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
    private void UpdateOutlineLevelCombo()
    {
        if (_outlineLevelCombo is null) return;
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

    private static UIElement Spacer() =>
        new Border { Width = 10 };

    private static string CommandContent(OutlineCommand command) => command switch
    {
        OutlineCommand.PromoteToHeading1 => "⟪",
        OutlineCommand.Promote => "◄",
        OutlineCommand.Demote => "►",
        OutlineCommand.MoveUp => "▲",
        OutlineCommand.MoveDown => "▼",
        OutlineCommand.Expand => "+",
        OutlineCommand.Collapse => "−",
        _ => throw new ArgumentOutOfRangeException(nameof(command), command, null),
    };

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_list.SelectedItem is OutlineRowItem selected)
            _controller.SelectBlock(selected.Row.BlockIndex, navigate: true);
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
            foreach (var projectedRow in _controller.ProjectedRows)
            {
                var row = projectedRow.Row;
                var item = new OutlineRowItem(projectedRow);
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

    internal void ExecuteForTests(OutlineCommand command) => _controller.Execute(command);

    internal int? SelectedBlockIndex => _controller.SelectedBlockIndex;

    internal string? RowDisplayTextForTests(int blockIndex) =>
        _list.Items.OfType<OutlineRowItem>()
            .FirstOrDefault(item => item.Row.BlockIndex == blockIndex)
            ?.ToString();

    /// <summary>The level currently shown in the Outline Level combo (-1 = Body Text / 0 = Title / 1–N = HeadingN). For tests.</summary>
    internal int CurrentOutlineLevel => _controller.CurrentOutlineLevel;

    // One outline row: indents the text by its outline level, prefixes a heading marker (collapsed
    // headings get a "+"), and remembers the source row so a command can map back to the model block index.
    private sealed class OutlineRowItem(OutlineProjectedRow projectedRow)
    {
        public OutlineRow Row => projectedRow.Row;

        public override string ToString() => OutlineViewPlanner.FormatRow(projectedRow, RowMarkers);
    }
}
