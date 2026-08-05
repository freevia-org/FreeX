using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Free.Shared.Shell.Avalonia;
using Free.Shared.Ribbon.Avalonia;
using FreeW.App.Avalonia.Editing;
using FreeW.App.Presentation.ContextMenus;
using FreeW.App.Presentation.Dialogs;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia;

/// <summary>
/// FreeW Avalonia Find &amp; Replace dialog: a modeless <see cref="Window"/> (non-blocking) with Find /
/// Replace fields, match-case / whole-word / wildcard checkboxes, Find Next / Replace / Replace All
/// buttons, and a Go To section (headings via <see cref="DocumentOutline"/> + document start/end).
///
/// Find/replace option policy, validation, request composition, and result text live in
/// <see cref="FindReplaceDialogPlanner"/>. Navigation (Find Next, Go To) uses the editor's
/// <see cref="DocumentView.FindNext"/> / <see cref="DocumentView.GetBlockTop"/> surface so the editor
/// controls the caret and scroll.
///
/// Options supported: Match Case, Whole Word, Use Wildcards.
/// "Use Wildcards" disables "Whole Word" through the presentation planner policy.
///
/// The inline find bar in MainWindow continues to work; the dialog is opened via a separate
/// <c>freew.find-replace-dialog</c> ribbon command (Home → Editing group) or Ctrl+H.
/// </summary>
public sealed class FindReplaceDialog : FreeWDialogWindow
{
    private static readonly AvaloniaCompactDialogChromeStyle DialogChromeStyle = AvaloniaCompactDialogChrome.WindowsStyle;

    // ── Editor reference ──────────────────────────────────────────────────────

    private readonly DocumentView _editor;

    // ── Controls ──────────────────────────────────────────────────────────────

    private readonly TextBox _findBox = new()
    {
        MinWidth = 220,
        Margin = new Thickness(0, 6, 0, 0),
    };

    private readonly TextBox _replaceBox = new()
    {
        MinWidth = 220,
        Margin = new Thickness(0, 6, 0, 0),
    };

    private readonly CheckBox _matchCase = new()
    {
        Content = FindReplaceDialogPlanner.LabelFor(FindReplaceOptionKind.MatchCase),
        Margin = new Thickness(0, 6, 0, 0),
    };

    private readonly CheckBox _wholeWord = new()
    {
        Content = FindReplaceDialogPlanner.LabelFor(FindReplaceOptionKind.WholeWord),
        Margin = new Thickness(0, 4, 0, 0),
    };

    private readonly CheckBox _useWildcards = new()
    {
        Content = FindReplaceDialogPlanner.LabelFor(FindReplaceOptionKind.UseWildcards),
        Margin = new Thickness(0, 4, 0, 0),
    };

    private readonly ComboBox _goToTarget = new()
    {
        MinWidth = 220,
        Margin = new Thickness(0, 6, 0, 0),
    };

    private readonly TextBlock _status = new()
    {
        Foreground = new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x80)),
        Margin = new Thickness(0, 6, 0, 0),
    };

    private TextBox _lastFocusedBox = null!;
    private readonly FindReplaceDialogSession _session;

    // ── Construction ──────────────────────────────────────────────────────────

    public FindReplaceDialog(
        DocumentView editor,
        FindReplaceDialogOpenMode openMode = FindReplaceDialogOpenMode.Find)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        _session = new FindReplaceDialogSession(new AvaloniaFindReplaceCommandHost(_editor), openMode);

        Title = "Find & Replace";
        Width = 420;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        ShowInTaskbar = false;

        _useWildcards.IsCheckedChanged += (_, _) => ApplyOptionPolicy();
        ApplyOptionPolicy();
        AvaloniaCompactDialogChrome.ApplyTextBox(_findBox, DialogChromeStyle);
        AvaloniaCompactDialogChrome.ApplyTextBox(_replaceBox, DialogChromeStyle);
        AvaloniaCompactDialogChrome.ApplyCompactCheckBox(_matchCase, DialogChromeStyle);
        AvaloniaCompactDialogChrome.ApplyCompactCheckBox(_wholeWord, DialogChromeStyle);
        AvaloniaCompactDialogChrome.ApplyCompactCheckBox(_useWildcards, DialogChromeStyle);
        AvaloniaCompactDialogChrome.ApplyComboBox(_goToTarget, DialogChromeStyle);

        // --- Main grid (Find label | Find box, Replace label | Replace box) ------
        var grid = new Grid { Margin = new Thickness(14, 14, 14, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // Row 0: Find:
        AddLabeledRow(grid, 0, "Find:", _findBox);
        // Row 1: Replace:
        AddLabeledRow(grid, 1, "Replace:", _replaceBox);

        _lastFocusedBox = _findBox;
        _findBox.GotFocus += (_, _) => _lastFocusedBox = _findBox;
        _replaceBox.GotFocus += (_, _) => _lastFocusedBox = _replaceBox;

        // Row 2-4: checkboxes (span both columns)
        foreach (var (chk, row) in new[] { (_matchCase, 2), (_wholeWord, 3), (_useWildcards, 4) })
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(chk, row);
            Grid.SetColumn(chk, 1);
            grid.Children.Add(chk);
        }

        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var specialButton = BuildSpecialButton();
        Grid.SetRow(specialButton, 5);
        Grid.SetColumn(specialButton, 1);
        grid.Children.Add(specialButton);

        // --- Action buttons ---------------------------------------------------
        var findNextButton = MakeButton("Find Next", (_, _) => FindNext());
        var replaceButton = MakeButton("Replace", (_, _) => Replace());
        var replaceAllButton = MakeButton("Replace All", (_, _) => ReplaceAll());
        var closeButton = MakeButton("Close", (_, _) => Close());
        var btnRow = AvaloniaCompactDialogChrome.CreateActionRow(
            [findNextButton, replaceButton, replaceAllButton, closeButton],
            new Thickness(14, 10, 14, 14));

        // --- Go To section ---------------------------------------------------
        var goToSection = BuildGoToSection();

        // --- Status bar -------------------------------------------------------
        var statusHost = new Border { Margin = new Thickness(14, 0, 14, 12), Child = _status };

        // --- Outer stack ------------------------------------------------------
        var outer = new StackPanel();
        outer.Children.Add(grid);
        outer.Children.Add(btnRow);
        outer.Children.Add(goToSection);
        outer.Children.Add(statusHost);

        Content = outer;

        // Keyboard: Enter = Find Next in find box, Escape = close dialog.
        _findBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { FindNext(); e.Handled = true; }
            else if (e.Key == Key.Escape) { Close(); e.Handled = true; }
        };
        _replaceBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { Close(); e.Handled = true; }
        };

        Opened += (_, _) => ActivateFor(_session.State.OpenMode);
    }

    internal void ActivateFor(FindReplaceDialogOpenMode openMode)
    {
        var state = _session.ActivateFor(openMode);
        AvaloniaCompactDialogChrome.FocusAndSelect(
            state.OpenMode == FindReplaceDialogOpenMode.Replace ? _replaceBox : _findBox);
    }

    internal FindReplaceDialogOpenMode OpenModeForTest => _session.State.OpenMode;

    internal FindReplaceDialogOpenMode? FocusedFieldForTest =>
        _findBox.IsFocused ? FindReplaceDialogOpenMode.Find :
        _replaceBox.IsFocused ? FindReplaceDialogOpenMode.Replace : null;

    private Button BuildSpecialButton()
    {
        var button = MakeButton("Special \u25be", (_, _) => { });
        button.HorizontalAlignment = HorizontalAlignment.Left;
        button.Margin = new Thickness(0, 6, 0, 0);

        var menu = AvaloniaContextMenuRenderer.BuildContextMenu(
            FreeWContextMenuPlanner.BuildFindSpecial(),
            commandId =>
            {
                if (FreeWContextMenuPlanner.TryParseIndex(commandId, FreeWContextMenuPlanner.FindSpecialPrefix, out var index)
                    && index < FreeWContextMenuPlanner.FindSpecialCharacters.Count)
                {
                    InsertSpecial(FreeWContextMenuPlanner.FindSpecialCharacters[index].Insert);
                }
            });
        button.ContextMenu = menu;
        button.Click += (_, _) => menu.Open(button);
        return button;
    }

    private void InsertSpecial(string text)
    {
        var box = _lastFocusedBox ?? _findBox;
        var caret = Math.Clamp(box.CaretIndex, 0, box.Text?.Length ?? 0);
        box.Text = (box.Text ?? string.Empty).Insert(caret, text);
        box.CaretIndex = caret + text.Length;
        box.Focus();
    }

    // ── Go To section ─────────────────────────────────────────────────────────

    private Panel BuildGoToSection()
    {
        var panel = new StackPanel { Margin = new Thickness(14, 0, 14, 0) };

        panel.Children.Add(new Border
        {
            Height = 1,
            Background = new SolidColorBrush(Color.FromRgb(0xDD, 0xDD, 0xDD)),
            Margin = new Thickness(0, 0, 0, 6),
        });

        panel.Children.Add(new TextBlock
        {
            Text = "Go to:",
            FontWeight = FontWeight.SemiBold,
        });

        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        Grid.SetColumn(_goToTarget, 0);
        row.Children.Add(_goToTarget);

        var goBtn = MakeButton("Go", (_, _) => GoTo());
        Grid.SetColumn(goBtn, 2);
        row.Children.Add(goBtn);

        row.Margin = new Thickness(0, 0, 0, 2);

        panel.Children.Add(row);

        // Populate initially and again each time the drop-down opens (document may have changed).
        PopulateGoToTargets();
        _goToTarget.DropDownOpened += (_, _) => PopulateGoToTargets();

        return panel;
    }

    private readonly record struct GoToItem(int BlockIndex, string Label)
    {
        public override string ToString() => Label;
    }

    private void PopulateGoToTargets()
    {
        var prevIndex = _goToTarget.SelectedIndex;
        var items = new List<GoToItem>
        {
            new(-1, "Document start"),
            new(int.MaxValue, "Document end"),
        };

        foreach (var entry in DocumentOutline.Of(_editor.Document))
        {
            var text = string.IsNullOrWhiteSpace(entry.Text) ? "(untitled heading)" : entry.Text;
            var indent = new string(' ', entry.Level * 2);
            items.Add(new GoToItem(entry.BlockIndex, $"{indent}{text}"));
        }

        _goToTarget.ItemsSource = items;
        _goToTarget.SelectedIndex = prevIndex >= 0 && prevIndex < items.Count ? prevIndex : 0;
    }

    private void GoTo()
    {
        if (_goToTarget.SelectedItem is not GoToItem item)
            return;

        int blockIndex;
        string label;
        if (item.BlockIndex == -1)
        {
            // Jump to the first block.
            blockIndex = 0;
            label = "Document start";
        }
        else if (item.BlockIndex == int.MaxValue)
        {
            // Jump to the last block.
            blockIndex = Math.Max(0, _editor.Document.Blocks.Count - 1);
            label = "Document end";
        }
        else
        {
            blockIndex = item.BlockIndex;
            label = item.Label.Trim();
        }

        ScrollEditorToBlock(blockIndex);
        _editor.Focus();
        _status.Text = _session.SetStatus($"Jumped to {label}.").StatusText;
    }

    // ── Find / Replace logic ──────────────────────────────────────────────────

    private void FindNext()
    {
        SyncSessionInput();
        _status.Text = _session.FindNext().StatusText;
    }

    private void Replace()
    {
        SyncSessionInput();
        _status.Text = _session.ReplaceNext().StatusText;
    }

    private void ReplaceAll()
    {
        SyncSessionInput();
        _status.Text = _session.ReplaceAll().StatusText;
    }

    private FindReplaceDialogState SyncSessionInput() =>
        _session.SetInput(
            _findBox.Text,
            _replaceBox.Text,
            _matchCase.IsChecked == true,
            _wholeWord.IsChecked == true,
            _useWildcards.IsChecked == true);

    private void ApplyOptionPolicy()
    {
        var state = SyncSessionInput();
        _wholeWord.IsEnabled = state.WholeWordEnabled;
        if (_wholeWord.IsChecked == true && !state.Options.WholeWord)
            _wholeWord.IsChecked = false;
    }

    private sealed class AvaloniaFindReplaceCommandHost(DocumentView editor) : IFindReplaceDialogCommandHost
    {
        public bool FindNext(FindReplaceSearchRequest request) =>
            editor.FindNext(request.Term, request.Options);

        public bool ReplaceNext(FindReplaceReplaceRequest request) =>
            editor.ReplaceNext(request.Term, request.Replacement, request.Options);

        public FindReplaceAllExecutionResult ReplaceAll(FindReplaceReplaceRequest request) =>
            new(editor.ReplaceAll(request.Term, request.Replacement, request.Options));
    }

    // ── Scroll helper (mirrors NavigationPane.ScrollEditorToBlock) ────────────

    /// <summary>
    /// Scrolls the <see cref="ScrollViewer"/> that wraps the editor so that
    /// <paramref name="blockIndex"/> is visible near the top of the viewport. Set via
    /// <see cref="ScrollerRef"/> after construction (wired by MainWindow).
    /// </summary>
    public ScrollViewer? ScrollerRef { get; set; }

    private void ScrollEditorToBlock(int blockIndex)
    {
        if (ScrollerRef is not { } scroller)
            return;
        var y = _editor.GetBlockTop(blockIndex);
        if (y < 0)
            return;
        scroller.Offset = new Vector(scroller.Offset.X, Math.Max(0, y - 40));
    }

    // ── Layout helpers ────────────────────────────────────────────────────────

    private static void AddLabeledRow(Grid grid, int row, string label, Control field)
    {
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var lbl = new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 6, 8, 0),
        };
        Grid.SetRow(lbl, row);
        Grid.SetColumn(lbl, 0);
        grid.Children.Add(lbl);

        Grid.SetRow(field, row);
        Grid.SetColumn(field, 1);
        grid.Children.Add(field);
    }

    private static Button MakeButton(string content, EventHandler<RoutedEventArgs> onClick)
    {
        var btn = new Button
        {
            Content = content,
        };
        AvaloniaCompactDialogChrome.ApplyButton(btn, DialogChromeStyle, minWidth: 84);
        btn.Click += onClick;
        return btn;
    }

}
