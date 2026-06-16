using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using FreeW.App.Host.Editing;

namespace FreeW.App.Host;

/// <summary>
/// A modeless Find &amp; Replace tool over the FreeW editing surface. Searches the live document via
/// TextPointer navigation (within a text run), selects matches, and replaces the selection. Opened
/// with Ctrl+F / Ctrl+H.
/// </summary>
internal sealed class FindReplaceDialog : Window
{
    private readonly DocumentView _editor;
    private readonly TextBox _findBox = new() { MinWidth = 220 };
    private readonly TextBox _replaceBox = new() { MinWidth = 220 };
    private readonly CheckBox _matchCase = new() { Content = "Match case", Margin = new Thickness(0, 6, 0, 0) };
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
        for (var i = 0; i < 4; i++)
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        AddRow(grid, 0, "Find:", _findBox);
        AddRow(grid, 1, "Replace:", _replaceBox);

        Grid.SetRow(_matchCase, 2);
        Grid.SetColumn(_matchCase, 1);
        grid.Children.Add(_matchCase);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        buttons.Children.Add(MakeButton("Find Next", (_, _) => FindNext()));
        buttons.Children.Add(MakeButton("Replace", (_, _) => Replace()));
        buttons.Children.Add(MakeButton("Replace All", (_, _) => ReplaceAll()));
        buttons.Children.Add(MakeButton("Close", (_, _) => Close()));
        Grid.SetRow(buttons, 3);
        Grid.SetColumn(buttons, 1);
        grid.Children.Add(buttons);

        var outer = new StackPanel();
        outer.Children.Add(grid);
        var statusHost = new Border { Margin = new Thickness(14, 0, 14, 12), Child = _status };
        outer.Children.Add(statusHost);
        Content = outer;
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

    private StringComparison Comparison =>
        _matchCase.IsChecked == true ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

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
        if (term.Length > 0 && !_editor.Selection.IsEmpty &&
            string.Equals(_editor.Selection.Text, term, Comparison))
        {
            _editor.Selection.Text = _replaceBox.Text;
        }
        FindNext();
    }

    private void ReplaceAll()
    {
        var term = _findBox.Text;
        if (term.Length == 0)
            return;

        var count = 0;
        var pointer = _editor.Document.ContentStart;
        while (TryFind(pointer, term, out var matchStart, out var matchEnd))
        {
            _editor.Selection.Select(matchStart, matchEnd);
            _editor.Selection.Text = _replaceBox.Text;
            pointer = _editor.Selection.End;
            count++;
        }
        _status.Text = count == 0 ? $"\"{term}\" not found." : $"Replaced {count} occurrence(s).";
    }

    private bool SelectFrom(TextPointer from, string term)
    {
        if (!TryFind(from, term, out var matchStart, out var matchEnd))
            return false;
        _editor.Selection.Select(matchStart, matchEnd);
        _editor.Focus();
        return true;
    }

    private bool TryFind(TextPointer from, string term, out TextPointer matchStart, out TextPointer matchEnd)
    {
        matchStart = matchEnd = _editor.Document.ContentStart;
        for (var pointer = from; pointer is not null; pointer = pointer.GetNextContextPosition(LogicalDirection.Forward))
        {
            if (pointer.GetPointerContext(LogicalDirection.Forward) != TextPointerContext.Text)
                continue;

            var runText = pointer.GetTextInRun(LogicalDirection.Forward);
            var index = runText.IndexOf(term, Comparison);
            if (index < 0)
                continue;

            var start = pointer.GetPositionAtOffset(index);
            var end = start?.GetPositionAtOffset(term.Length);
            if (start is null || end is null)
                continue;

            matchStart = start;
            matchEnd = end;
            return true;
        }
        return false;
    }
}
