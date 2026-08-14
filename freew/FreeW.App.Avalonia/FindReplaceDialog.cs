using Free.Shared.AppServices;
using Avalonia;
using Avalonia.Automation;
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
/// <c>freew.find-replace-dialog</c> ribbon command (Home â†’ Editing group) or Ctrl+H.
/// </summary>
public sealed partial class FindReplaceDialog : FreeWDialogWindow
{
    private static readonly AvaloniaCompactDialogChromeStyle DialogChromeStyle = AvaloniaCompactDialogChrome.WindowsStyle;
    private static readonly FindReplaceDialogSurfaceSpec Surface = FindReplaceDialogPlanner.Surface;

    // â”€â”€ Editor reference â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private readonly DocumentView _editor;

    // â”€â”€ Controls â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private readonly TextBox _findBox = new()
    {
        MinWidth = Surface.Metrics.FieldMinWidth,
        Margin = new Thickness(0, 6, 0, 0),
    };

    private readonly TextBox _replaceBox = new()
    {
        MinWidth = Surface.Metrics.FieldMinWidth,
        Margin = new Thickness(0, 6, 0, 0),
    };

    private readonly CheckBox _matchCase = new()
    {
        Margin = new Thickness(0, 6, 0, 0),
    };

    private readonly CheckBox _wholeWord = new()
    {
        Margin = new Thickness(0, 4, 0, 0),
    };

    private readonly CheckBox _useWildcards = new()
    {
        Margin = new Thickness(0, 4, 0, 0),
    };

    private readonly ComboBox _goToTarget = new()
    {
        MinWidth = Surface.Metrics.FieldMinWidth,
        Margin = new Thickness(0, 6, 0, 0),
    };

    private readonly TextBlock _status = new()
    {
        Foreground = new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x80)),
        Margin = new Thickness(0, 6, 0, 0),
    };

    private TextBox _lastFocusedBox = null!;
    private readonly FindReplaceDialogSession _session;

    // â”€â”€ Construction â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    public FindReplaceDialog(
        DocumentView editor,
        FindReplaceOpenMode openMode = FindReplaceOpenMode.Find)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        _session = new FindReplaceDialogSession(new AvaloniaFindReplaceCommandHost(_editor), openMode);

        Title = Surface.Title;
        Width = Surface.Metrics.WindowWidth;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        ShowInTaskbar = false;

        _useWildcards.IsCheckedChanged += (_, _) => ApplyOptionPolicy();
        ApplyOptionPolicy();
        AutomationProperties.SetAutomationId(_findBox, Surface.Field(FindReplaceDialogFieldKind.Find).AutomationId);
        AutomationProperties.SetAutomationId(_replaceBox, Surface.Field(FindReplaceDialogFieldKind.Replace).AutomationId);
        AutomationProperties.SetAutomationId(_goToTarget, Surface.GoToTargetAutomationId);
        _matchCase.Content = Surface.Option(FindReplaceOptionKind.MatchCase).Label;
        _wholeWord.Content = Surface.Option(FindReplaceOptionKind.WholeWord).Label;
        _useWildcards.Content = Surface.Option(FindReplaceOptionKind.UseWildcards).Label;
        AutomationProperties.SetAutomationId(_matchCase, Surface.Option(FindReplaceOptionKind.MatchCase).AutomationId);
        AutomationProperties.SetAutomationId(_wholeWord, Surface.Option(FindReplaceOptionKind.WholeWord).AutomationId);
        AutomationProperties.SetAutomationId(_useWildcards, Surface.Option(FindReplaceOptionKind.UseWildcards).AutomationId);
        AvaloniaCompactDialogChrome.ApplyTextBox(_findBox, DialogChromeStyle);
        AvaloniaCompactDialogChrome.ApplyTextBox(_replaceBox, DialogChromeStyle);
        AvaloniaCompactDialogChrome.ApplyCompactCheckBox(_matchCase, DialogChromeStyle);
        AvaloniaCompactDialogChrome.ApplyCompactCheckBox(_wholeWord, DialogChromeStyle);
        AvaloniaCompactDialogChrome.ApplyCompactCheckBox(_useWildcards, DialogChromeStyle);
        AvaloniaCompactDialogChrome.ApplyComboBox(_goToTarget, DialogChromeStyle);

        // --- Main grid (Find label | Find box, Replace label | Replace box) ------
        var grid = new Grid { Margin = new Thickness(Surface.Metrics.OuterMargin, Surface.Metrics.OuterMargin, Surface.Metrics.OuterMargin, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // Row 0: Find:
        AddLabeledRow(grid, 0, Surface.Field(FindReplaceDialogFieldKind.Find).Label, _findBox);
        // Row 1: Replace:
        AddLabeledRow(grid, 1, Surface.Field(FindReplaceDialogFieldKind.Replace).Label, _replaceBox);

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
        var actionButtons = new[]
        {
            MakeButton(Surface.Actions[0], (_, _) => Execute(Surface.Actions[0].Kind)),
            MakeButton(Surface.Actions[1], (_, _) => Execute(Surface.Actions[1].Kind)),
            MakeButton(Surface.Actions[2], (_, _) => Execute(Surface.Actions[2].Kind)),
            MakeButton(Surface.Actions[3], (_, _) => Close()),
        };
        var btnRow = AvaloniaCompactDialogChrome.CreateActionRow(
            actionButtons,
            new Thickness(Surface.Metrics.OuterMargin, Surface.Metrics.ActionTopMargin, Surface.Metrics.OuterMargin, Surface.Metrics.OuterMargin));

        // --- Go To section ---------------------------------------------------
        var goToSection = BuildGoToSection();

        // --- Status bar -------------------------------------------------------
        var statusHost = new Border { Margin = new Thickness(Surface.Metrics.OuterMargin, 0, Surface.Metrics.OuterMargin, 12), Child = _status };

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
            if (e.Key == Key.Enter)
            {
                Execute(FindReplaceDialogActionKind.FindNext);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape) { Close(); e.Handled = true; }
        };
        _replaceBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { Close(); e.Handled = true; }
        };

        Opened += (_, _) => ActivateFor(_session.State.OpenMode);
    }

    internal void ActivateFor(FindReplaceOpenMode openMode)
    {
        var state = _session.ActivateFor(openMode);
        AvaloniaCompactDialogChrome.FocusAndSelect(
            state.OpenMode == FindReplaceOpenMode.Replace ? _replaceBox : _findBox);
    }

    private Button BuildSpecialButton()
    {
        var button = MakeButton(Surface.SpecialButtonLabel, (_, _) => { }, Surface.SpecialButtonAutomationId);
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
        var plan = _session.PlanSpecialInsertion(box.Text, box.CaretIndex, text);
        box.Text = plan.Text;
        box.CaretIndex = plan.CaretIndex;
        box.Focus();
    }

    // â”€â”€ Go To section â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private Panel BuildGoToSection()
    {
        var panel = new StackPanel { Margin = new Thickness(Surface.Metrics.OuterMargin, 0, Surface.Metrics.OuterMargin, 0) };

        panel.Children.Add(new Border
        {
            Height = 1,
            Background = new SolidColorBrush(Color.FromRgb(0xDD, 0xDD, 0xDD)),
            Margin = new Thickness(0, 0, 0, 6),
        });

        panel.Children.Add(new TextBlock
        {
            Text = Surface.GoToSectionLabel,
            FontWeight = FontWeight.SemiBold,
        });

        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        Grid.SetColumn(_goToTarget, 0);
        row.Children.Add(_goToTarget);

        var goBtn = MakeButton(Surface.GoToButtonLabel, (_, _) => GoTo(), Surface.GoToButtonAutomationId);
        Grid.SetColumn(goBtn, 2);
        row.Children.Add(goBtn);

        row.Margin = new Thickness(0, 0, 0, 2);

        panel.Children.Add(row);

        // Populate initially and again each time the drop-down opens (document may have changed).
        PopulateGoToTargets();
        _goToTarget.DropDownOpened += (_, _) => PopulateGoToTargets();

        return panel;
    }

    private void PopulateGoToTargets()
    {
        var plan = _session.BuildGoToTargets(_editor.Document, _goToTarget.SelectedIndex);
        _goToTarget.ItemsSource = plan.Targets;
        _goToTarget.SelectedIndex = plan.SelectedIndex;
    }

    private void GoTo()
    {
        var plan = _session.PlanGoTo(
            _goToTarget.SelectedItem as FindReplaceGoToTarget,
            _editor.Document.Blocks.Count);
        if (plan is null)
            return;

        ScrollEditorToBlock(plan.BlockIndex);
        _editor.Focus();
        _status.Text = _session.State.StatusText;
    }

    // â”€â”€ Find / Replace logic â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private void Execute(FindReplaceDialogActionKind action) =>
        _status.Text = _session.Execute(action, ReadInput()).StatusText;

    private FindReplaceDialogState SyncSessionInput() =>
        _session.SetInput(ReadInput());

    private FindReplaceDialogInput ReadInput() =>
        new(
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

    // â”€â”€ Scroll helper (mirrors NavigationPane.ScrollEditorToBlock) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

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

    // â”€â”€ Layout helpers â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private static void AddLabeledRow(Grid grid, int row, string label, Control field)
    {
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var lbl = new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, Surface.Metrics.RowTopMargin, 8, 0),
        };
        Grid.SetRow(lbl, row);
        Grid.SetColumn(lbl, 0);
        grid.Children.Add(lbl);

        Grid.SetRow(field, row);
        Grid.SetColumn(field, 1);
        grid.Children.Add(field);
    }

    private static Button MakeButton(string content, EventHandler<RoutedEventArgs> onClick, string automationId)
    {
        var btn = new Button
        {
            Content = content,
        };
        AutomationProperties.SetAutomationId(btn, automationId);
        AvaloniaCompactDialogChrome.ApplyButton(btn, DialogChromeStyle, minWidth: Surface.Metrics.ButtonMinWidth);
        btn.Click += onClick;
        return btn;
    }

    private static Button MakeButton(FindReplaceDialogActionSpec action, EventHandler<RoutedEventArgs> onClick) =>
        MakeButton(action.Label, onClick, action.AutomationId);

}
