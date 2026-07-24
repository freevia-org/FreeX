using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Free.Shared.Shell;
using FreeW.App.Presentation.Dialogs;
using FreeW.App.Presentation.ContextMenus;
using FreeW.App.Host.Editing;
using FreeW.Core.Model;

namespace FreeW.App.Host;

/// <summary>
/// A modeless Find &amp; Replace tool over the FreeW editing surface. Searches the live document via
/// TextPointer navigation (within a text run), selects matches, and replaces the selection. Match
/// decisions (case sensitivity, whole-word boundaries, Word-style wildcards) are delegated to the
/// pure <see cref="TextSearch"/> helper. Includes a Go To section that jumps to a heading (via
/// <see cref="DocumentOutline"/>) or to the document start/end. Opened with Ctrl+F / Ctrl+H.
/// </summary>
internal sealed class FindReplaceDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
    private readonly DocumentView _editor;
    private readonly TextBox _findBox = new() { MinWidth = 220 };
    private readonly TextBox _replaceBox = new() { MinWidth = 220 };
    private readonly CheckBox _matchCase = new() { Content = FindReplaceDialogPlanner.LabelFor(FindReplaceOptionKind.MatchCase), Margin = new Thickness(0, 6, 0, 0) };
    private readonly CheckBox _wholeWord = new() { Content = FindReplaceDialogPlanner.LabelFor(FindReplaceOptionKind.WholeWord), Margin = new Thickness(0, 4, 0, 0) };
    private readonly CheckBox _useWildcards = new() { Content = FindReplaceDialogPlanner.LabelFor(FindReplaceOptionKind.UseWildcards), Margin = new Thickness(0, 4, 0, 0) };
    private readonly ComboBox _goToTarget = new() { MinWidth = 220, Margin = new Thickness(0, 6, 0, 0) };
    private readonly TextBlock _status = new() { Foreground = Brushes.Gray, Margin = new Thickness(0, 6, 0, 0) };
    private FindReplaceDialogOpenMode _openMode;

    public FindReplaceDialog(
        Window owner,
        DocumentView editor,
        FindReplaceDialogOpenMode openMode = FindReplaceDialogOpenMode.Find)
    {
        _editor = editor;
        _openMode = openMode;
        Owner = owner;
        Title = "Find & Replace";
        Width = 420;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        var grid = new Grid { Margin = new Thickness(14) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var i = 0; i < 7; i++)
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        AddRow(grid, 0, "Find:", _findBox);
        AddRow(grid, 1, "Replace:", _replaceBox);

        Grid.SetRow(_matchCase, 2);
        Grid.SetColumn(_matchCase, 1);
        grid.Children.Add(_matchCase);

        Grid.SetRow(_wholeWord, 3);
        Grid.SetColumn(_wholeWord, 1);
        grid.Children.Add(_wholeWord);

        Grid.SetRow(_useWildcards, 4);
        Grid.SetColumn(_useWildcards, 1);
        grid.Children.Add(_useWildcards);

        // "Use Wildcards" disables "Whole word" (incompatible, mirrors Word).
        _useWildcards.Checked += (_, _) => ApplyOptionPolicy();
        _useWildcards.Unchecked += (_, _) => ApplyOptionPolicy();
        ApplyOptionPolicy();

        // Special ▾ button — inserts a special character into whichever box last had focus.
        var specialButton = BuildSpecialButton();
        Grid.SetRow(specialButton, 5);
        Grid.SetColumn(specialButton, 1);
        grid.Children.Add(specialButton);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        buttons.Children.Add(MakeButton("Find Next", (_, _) => FindNext()));
        buttons.Children.Add(MakeButton("Replace", (_, _) => Replace()));
        buttons.Children.Add(MakeButton("Replace All", (_, _) => ReplaceAll()));
        buttons.Children.Add(MakeButton("Close", (_, _) => Close()));
        Grid.SetRow(buttons, 6);
        Grid.SetColumn(buttons, 1);
        grid.Children.Add(buttons);

        var outer = new StackPanel();
        outer.Children.Add(grid);
        outer.Children.Add(BuildGoToSection());
        var statusHost = new Border { Margin = new Thickness(14, 0, 14, 12), Child = _status };
        outer.Children.Add(statusHost);
        Content = outer;

        Loaded += (_, _) => ActivateFor(_openMode);
    }

    internal void ActivateFor(FindReplaceDialogOpenMode openMode)
    {
        _openMode = openMode;
        DialogFocus.FocusAndSelect(_openMode == FindReplaceDialogOpenMode.Replace ? _replaceBox : _findBox);
    }

    internal FindReplaceDialogOpenMode OpenModeForTest => _openMode;

    // Track which text field was focused last so Special inserts into the right box.
    private TextBox _lastFocusedBox = null!;

    private UIElement BuildSpecialButton()
    {
        _lastFocusedBox = _findBox;
        _findBox.GotFocus += (_, _) => _lastFocusedBox = _findBox;
        _replaceBox.GotFocus += (_, _) => _lastFocusedBox = _replaceBox;

        var menu = new ContextMenu();
        foreach (var (label, insert) in FreeWContextMenuPlanner.FindSpecialCharacters)
        {
            var item = new MenuItem { Header = label };
            var insertValue = insert; // capture
            item.Click += (_, _) => InsertSpecial(insertValue);
            menu.Items.Add(item);
        }

        var btn = new Button
        {
            Content = "Special ▾",
            HorizontalAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(6, 3, 6, 3),
            Margin = new Thickness(0, 4, 0, 0)
        };
        btn.Click += (_, _) =>
        {
            menu.PlacementTarget = btn;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            menu.IsOpen = true;
        };
        return btn;
    }

    private void InsertSpecial(string text)
    {
        var box = _lastFocusedBox ?? _findBox;
        var caret = box.CaretIndex;
        box.Text = box.Text.Insert(caret, text);
        box.CaretIndex = caret + text.Length;
        box.Focus();
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

        // Then each bookmark by name (jumps to the bookmarked paragraph via BringBlockIntoView).
        foreach (var bookmark in Bookmarks.List(_editor.Model))
            items.Add(new GoToItem(bookmark.BlockIndex, $"Bookmark: {bookmark.Name}"));

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

    private void FindNext()
    {
        if (!FindReplaceDialogPlanner.TryCreateSearchRequest(
                _findBox.Text,
                CurrentOptions(),
                out var request,
                out var error))
        {
            _status.Text = FindReplaceDialogPlanner.ValidationMessageFor(error);
            return;
        }

        var start = _editor.Selection.IsEmpty ? _editor.CaretPosition : _editor.Selection.End;
        var found = SelectFrom(start, request!) || SelectFrom(_editor.Document.ContentStart, request!);
        _status.Text = FindReplaceDialogPlanner.BuildFindStatus(request!, found);
    }

    private void Replace()
    {
        if (!FindReplaceDialogPlanner.TryCreateReplaceRequest(
                _findBox.Text,
                _replaceBox.Text,
                CurrentOptions(),
                out var request,
                out var error))
        {
            _status.Text = FindReplaceDialogPlanner.ValidationMessageFor(error);
            return;
        }

        var replaced = !_editor.Selection.IsEmpty && IsTermSelected(request!);
        if (replaced)
        {
            _editor.Selection.Text = request!.Replacement;
        }

        var searchRequest = new FindReplaceSearchRequest(request!.Term, request.Options);
        var start = _editor.Selection.IsEmpty ? _editor.CaretPosition : _editor.Selection.End;
        var found = SelectFrom(start, searchRequest)
            || SelectFrom(_editor.Document.ContentStart, searchRequest);
        _status.Text = FindReplaceDialogPlanner.BuildReplaceStatus(request!, found);
    }

    // True when the current selection is exactly an occurrence of term under the active match options.
    private bool IsTermSelected(FindReplaceReplaceRequest request) =>
        FindReplaceDialogPlanner.MatchesExactly(
            _editor.Selection.Text,
            request.Term,
            request.Options);

    private void ReplaceAll()
    {
        if (!FindReplaceDialogPlanner.TryCreateReplaceRequest(
                _findBox.Text,
                _replaceBox.Text,
                CurrentOptions(),
                out var request,
                out var error))
        {
            _status.Text = FindReplaceDialogPlanner.ValidationMessageFor(error);
            return;
        }

        // Restrict to the current selection when there is one; otherwise sweep the whole document.
        var restrictToSelection = !_editor.Selection.IsEmpty;
        var (from, limit) = restrictToSelection
            ? (_editor.Selection.Start, _editor.Selection.End)
            : (_editor.Document.ContentStart, _editor.Document.ContentEnd);

        var count = 0;
        var pointer = from;
        var searchRequest = new FindReplaceSearchRequest(request!.Term, request.Options);
        while (TryFind(pointer, searchRequest, out var matchStart, out var matchEnd))
        {
            // When restricted to a selection, stop once a match would start past the selection end.
            if (restrictToSelection && matchStart.CompareTo(limit) >= 0)
                break;

            _editor.Selection.Select(matchStart, matchEnd);
            _editor.Selection.Text = request!.Replacement;
            pointer = _editor.Selection.End;
            count++;
        }

        _status.Text = FindReplaceDialogPlanner.BuildReplaceAllStatus(request!, count, restrictToSelection);
    }

    private bool SelectFrom(TextPointer from, FindReplaceSearchRequest request)
    {
        if (!TryFind(from, request, out var matchStart, out var matchEnd))
            return false;
        _editor.Selection.Select(matchStart, matchEnd);
        _editor.Focus();
        return true;
    }

    // Finds the first match of term at or after `from`, scanning text runs in document order. Match
    // decisions (case, whole-word boundaries within the run text) come from the pure TextSearch helper.
    private bool TryFind(TextPointer from, FindReplaceSearchRequest request, out TextPointer matchStart, out TextPointer matchEnd)
    {
        matchStart = matchEnd = _editor.Document.ContentStart;
        for (var pointer = from; pointer is not null; pointer = pointer.GetNextContextPosition(LogicalDirection.Forward))
        {
            if (pointer.GetPointerContext(LogicalDirection.Forward) != TextPointerContext.Text)
                continue;

            var runText = pointer.GetTextInRun(LogicalDirection.Forward);
            foreach (var (index, length) in FindReplaceDialogPlanner.FindAll(runText, request.Term, request.Options))
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

    private FindReplaceSearchOptions CurrentOptions() =>
        FindReplaceDialogPlanner.NormalizeOptions(new FindReplaceSearchOptions(
            _matchCase.IsChecked == true,
            _wholeWord.IsChecked == true,
            _useWildcards.IsChecked == true));

    private void ApplyOptionPolicy()
    {
        var options = CurrentOptions();
        _wholeWord.IsEnabled = FindReplaceDialogPlanner.IsOptionEnabled(
            FindReplaceOptionKind.WholeWord,
            options);
        if (!_wholeWord.IsEnabled)
            _wholeWord.IsChecked = false;
    }
}
