using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using FreeW.App.Host.Editing;
using FreeW.Core.Model;
using TextSearch = FreeW.Core.Model.TextSearch;

namespace FreeW.App.Host;

/// <summary>
/// A modeless Find &amp; Replace tool over the FreeW editing surface. Searches the live document via
/// TextPointer navigation (within a text run), selects matches, and replaces the selection. Match
/// decisions (case sensitivity, whole-word boundaries) are delegated to the pure
/// <see cref="TextSearch"/> helper. Includes a Go To section that jumps to a heading (via
/// <see cref="DocumentOutline"/>) or to the document start/end. Opened with Ctrl+F / Ctrl+H.
/// </summary>
internal sealed class FindReplaceDialog : Window
{
    private readonly DocumentView _editor;
    private readonly TextBox _findBox = new() { MinWidth = 220 };
    private readonly TextBox _replaceBox = new() { MinWidth = 220 };
    private readonly CheckBox _matchCase = new() { Content = "Match case", Margin = new Thickness(0, 6, 0, 0) };
    private readonly CheckBox _wholeWord = new() { Content = "Whole word", Margin = new Thickness(0, 4, 0, 0) };
    private readonly ComboBox _goToTarget = new() { MinWidth = 220, Margin = new Thickness(0, 6, 0, 0) };
    private readonly TextBlock _status = new() { Foreground = Brushes.Gray, Margin = new Thickness(0, 6, 0, 0) };

    public FindReplaceDialog(Window owner, DocumentView editor)
    {
        _editor = editor;
        Owner = owner;
        Title = "Find & Replace";
        Width = 380;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        var grid = new Grid { Margin = new Thickness(14) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var i = 0; i < 5; i++)
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        AddRow(grid, 0, "Find:", _findBox);
        AddRow(grid, 1, "Replace:", _replaceBox);

        Grid.SetRow(_matchCase, 2);
        Grid.SetColumn(_matchCase, 1);
        grid.Children.Add(_matchCase);

        Grid.SetRow(_wholeWord, 3);
        Grid.SetColumn(_wholeWord, 1);
        grid.Children.Add(_wholeWord);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        buttons.Children.Add(MakeButton("Find Next", (_, _) => FindNext()));
        buttons.Children.Add(MakeButton("Replace", (_, _) => Replace()));
        buttons.Children.Add(MakeButton("Replace All", (_, _) => ReplaceAll()));
        buttons.Children.Add(MakeButton("Close", (_, _) => Close()));
        Grid.SetRow(buttons, 4);
        Grid.SetColumn(buttons, 1);
        grid.Children.Add(buttons);

        var outer = new StackPanel();
        outer.Children.Add(grid);
        outer.Children.Add(BuildGoToSection());
        var statusHost = new Border { Margin = new Thickness(14, 0, 14, 12), Child = _status };
        outer.Children.Add(statusHost);
        Content = outer;
    }

    // The Go To section: a labelled combo of jump targets (document start/end + each heading) and a
    // Go button that jumps the caret/scroll there via DocumentView.BringBlockIntoView.
    private UIElement BuildGoToSection()
    {
        var panel = new StackPanel { Margin = new Thickness(14, 0, 14, 0) };
        panel.Children.Add(new Separator { Margin = new Thickness(0, 0, 0, 6) });
        panel.Children.Add(new TextBlock { Text = "Go to:", FontWeight = FontWeights.SemiBold });

        var row = new Grid { Margin = new Thickness(0, 0, 0, 2) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        Grid.SetColumn(_goToTarget, 0);
        row.Children.Add(_goToTarget);

        var goButton = MakeButton("Go", (_, _) => GoTo());
        Grid.SetColumn(goButton, 1);
        row.Children.Add(goButton);

        panel.Children.Add(row);
        _goToTarget.DropDownOpened += (_, _) => PopulateGoToTargets();
        PopulateGoToTargets();
        return panel;
    }

    // A jump target: a model block index (-1 for document start, int.MaxValue for document end) and a
    // human-readable label shown in the combo.
    private readonly record struct GoToItem(int BlockIndex, string Label)
    {
        public override string ToString() => Label;
    }

    private void PopulateGoToTargets()
    {
        var selectedIndex = _goToTarget.SelectedIndex;
        var items = new List<GoToItem>
        {
            new(-1, "Document start"),
            new(int.MaxValue, "Document end"),
        };

        foreach (var entry in DocumentOutline.Of(_editor.Model))
        {
            var text = string.IsNullOrWhiteSpace(entry.Text) ? "(untitled heading)" : entry.Text;
            var indent = new string(' ', entry.Level * 2);
            items.Add(new GoToItem(entry.BlockIndex, $"{indent}{text}"));
        }

        _goToTarget.ItemsSource = items;
        _goToTarget.SelectedIndex = selectedIndex >= 0 && selectedIndex < items.Count ? selectedIndex : 0;
    }

    private void GoTo()
    {
        if (_goToTarget.SelectedItem is not GoToItem item)
            return;

        switch (item.BlockIndex)
        {
            case -1:
                _editor.CaretPosition = _editor.Document.ContentStart;
                _editor.Document.ContentStart.Paragraph?.BringIntoView();
                _editor.Focus();
                break;
            case int.MaxValue:
                _editor.CaretPosition = _editor.Document.ContentEnd;
                _editor.Document.ContentEnd.Paragraph?.BringIntoView();
                _editor.Focus();
                break;
            default:
                _editor.BringBlockIntoView(item.BlockIndex);
                break;
        }

        _status.Text = $"Jumped to {item.Label.Trim()}.";
    }

    private static void AddRow(Grid grid, int row, string label, UIElement field)
    {
        var text = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 6, 8, 0) };
        Grid.SetRow(text, row);
        Grid.SetColumn(text, 0);
        grid.Children.Add(text);
        Grid.SetRow(field, row);
        Grid.SetColumn(field, 1);
        if (field is FrameworkElement fe)
            fe.Margin = new Thickness(0, 6, 0, 0);
        grid.Children.Add(field);
    }

    private static Button MakeButton(string content, RoutedEventHandler onClick)
    {
        var button = new Button { Content = content, MinWidth = 84, Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(6, 3, 6, 3) };
        button.Click += onClick;
        return button;
    }

    private bool MatchCase => _matchCase.IsChecked == true;

    private bool WholeWord => _wholeWord.IsChecked == true;

    private void FindNext()
    {
        var term = _findBox.Text;
        if (term.Length == 0)
            return;

        var start = _editor.Selection.IsEmpty ? _editor.CaretPosition : _editor.Selection.End;
        if (!SelectFrom(start, term) && !SelectFrom(_editor.Document.ContentStart, term))
            _status.Text = $"\"{term}\" not found.";
        else
            _status.Text = string.Empty;
    }

    private void Replace()
    {
        var term = _findBox.Text;
        if (term.Length > 0 && !_editor.Selection.IsEmpty && IsTermSelected(term))
        {
            _editor.Selection.Text = _replaceBox.Text;
        }
        FindNext();
    }

    // True when the current selection is exactly an occurrence of term under the active match options.
    private bool IsTermSelected(string term)
    {
        var selected = _editor.Selection.Text;
        if (selected.Length != term.Length)
            return false;
        var comparison = MatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        return string.Equals(selected, term, comparison);
    }

    private void ReplaceAll()
    {
        var term = _findBox.Text;
        if (term.Length == 0)
            return;

        // Restrict to the current selection when there is one; otherwise sweep the whole document.
        var restrictToSelection = !_editor.Selection.IsEmpty;
        var (from, limit) = restrictToSelection
            ? (_editor.Selection.Start, _editor.Selection.End)
            : (_editor.Document.ContentStart, _editor.Document.ContentEnd);

        var count = 0;
        var pointer = from;
        while (TryFind(pointer, term, out var matchStart, out var matchEnd))
        {
            // When restricted to a selection, stop once a match would start past the selection end.
            if (restrictToSelection && matchStart.CompareTo(limit) >= 0)
                break;

            _editor.Selection.Select(matchStart, matchEnd);
            _editor.Selection.Text = _replaceBox.Text;
            pointer = _editor.Selection.End;
            count++;
        }

        var scope = restrictToSelection ? " in selection" : string.Empty;
        _status.Text = count == 0 ? $"\"{term}\" not found." : $"Replaced {count} occurrence(s){scope}.";
    }

    private bool SelectFrom(TextPointer from, string term)
    {
        if (!TryFind(from, term, out var matchStart, out var matchEnd))
            return false;
        _editor.Selection.Select(matchStart, matchEnd);
        _editor.Focus();
        return true;
    }

    // Finds the first match of term at or after `from`, scanning text runs in document order. Match
    // decisions (case, whole-word boundaries within the run text) come from the pure TextSearch helper.
    private bool TryFind(TextPointer from, string term, out TextPointer matchStart, out TextPointer matchEnd)
    {
        matchStart = matchEnd = _editor.Document.ContentStart;
        for (var pointer = from; pointer is not null; pointer = pointer.GetNextContextPosition(LogicalDirection.Forward))
        {
            if (pointer.GetPointerContext(LogicalDirection.Forward) != TextPointerContext.Text)
                continue;

            var runText = pointer.GetTextInRun(LogicalDirection.Forward);
            foreach (var (index, length) in TextSearch.FindAll(runText, term, MatchCase, WholeWord))
            {
                var start = pointer.GetPositionAtOffset(index);
                var end = start?.GetPositionAtOffset(length);
                if (start is null || end is null)
                    continue;

                matchStart = start;
                matchEnd = end;
                return true;
            }
        }
        return false;
    }
}
