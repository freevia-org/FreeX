using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
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
internal sealed partial class FindReplaceDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
    private static readonly FindReplaceDialogSurfaceSpec Surface = FindReplaceDialogPlanner.Surface;
    private readonly DocumentView _editor;
    private readonly TextBox _findBox = new() { MinWidth = Surface.Metrics.FieldMinWidth };
    private readonly TextBox _replaceBox = new() { MinWidth = Surface.Metrics.FieldMinWidth };
    private readonly CheckBox _matchCase = new() { Margin = new Thickness(0, 6, 0, 0) };
    private readonly CheckBox _wholeWord = new() { Margin = new Thickness(0, 4, 0, 0) };
    private readonly CheckBox _useWildcards = new() { Margin = new Thickness(0, 4, 0, 0) };
    private readonly ComboBox _goToTarget = new() { MinWidth = Surface.Metrics.FieldMinWidth, Margin = new Thickness(0, 6, 0, 0) };
    private readonly TextBlock _status = new() { Foreground = Brushes.Gray, Margin = new Thickness(0, 6, 0, 0) };
    private readonly FindReplaceDialogSession _session;

    public FindReplaceDialog(
        Window owner,
        DocumentView editor,
        FindReplaceDialogOpenMode openMode = FindReplaceDialogOpenMode.Find)
    {
        _editor = editor;
        _session = new FindReplaceDialogSession(new WpfFindReplaceCommandHost(editor), openMode);
        Owner = owner;
        Title = Surface.Title;
        Width = Surface.Metrics.WindowWidth;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        AutomationProperties.SetAutomationId(_findBox, Surface.Field(FindReplaceDialogFieldKind.Find).AutomationId);
        AutomationProperties.SetAutomationId(_replaceBox, Surface.Field(FindReplaceDialogFieldKind.Replace).AutomationId);
        AutomationProperties.SetAutomationId(_goToTarget, Surface.GoToTargetAutomationId);
        _matchCase.Content = Surface.Option(FindReplaceOptionKind.MatchCase).Label;
        _wholeWord.Content = Surface.Option(FindReplaceOptionKind.WholeWord).Label;
        _useWildcards.Content = Surface.Option(FindReplaceOptionKind.UseWildcards).Label;
        AutomationProperties.SetAutomationId(_matchCase, Surface.Option(FindReplaceOptionKind.MatchCase).AutomationId);
        AutomationProperties.SetAutomationId(_wholeWord, Surface.Option(FindReplaceOptionKind.WholeWord).AutomationId);
        AutomationProperties.SetAutomationId(_useWildcards, Surface.Option(FindReplaceOptionKind.UseWildcards).AutomationId);

        var grid = new Grid { Margin = new Thickness(Surface.Metrics.OuterMargin) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var i = 0; i < 7; i++)
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        AddRow(grid, 0, Surface.Field(FindReplaceDialogFieldKind.Find).Label, _findBox);
        AddRow(grid, 1, Surface.Field(FindReplaceDialogFieldKind.Replace).Label, _replaceBox);

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

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, Surface.Metrics.ActionTopMargin, 0, 0) };
        buttons.Children.Add(MakeButton(Surface.Actions[0], (_, _) => Execute(Surface.Actions[0].Kind)));
        buttons.Children.Add(MakeButton(Surface.Actions[1], (_, _) => Execute(Surface.Actions[1].Kind)));
        buttons.Children.Add(MakeButton(Surface.Actions[2], (_, _) => Execute(Surface.Actions[2].Kind)));
        buttons.Children.Add(MakeButton(Surface.Actions[3], (_, _) => Close()));
        Grid.SetRow(buttons, 6);
        Grid.SetColumn(buttons, 1);
        grid.Children.Add(buttons);

        var outer = new StackPanel();
        outer.Children.Add(grid);
        outer.Children.Add(BuildGoToSection());
        var statusHost = new Border { Margin = new Thickness(Surface.Metrics.OuterMargin, 0, Surface.Metrics.OuterMargin, 12), Child = _status };
        outer.Children.Add(statusHost);
        Content = outer;

        Loaded += (_, _) => ActivateFor(_session.State.OpenMode);
    }

    internal void ActivateFor(FindReplaceDialogOpenMode openMode)
    {
        var state = _session.ActivateFor(openMode);
        DialogFocus.FocusAndSelect(state.OpenMode == FindReplaceDialogOpenMode.Replace ? _replaceBox : _findBox);
    }

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
            Content = Surface.SpecialButtonLabel,
            HorizontalAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(6, 3, 6, 3),
            Margin = new Thickness(0, 4, 0, 0)
        };
        AutomationProperties.SetAutomationId(btn, Surface.SpecialButtonAutomationId);
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
        var plan = _session.PlanSpecialInsertion(box.Text, box.CaretIndex, text);
        box.Text = plan.Text;
        box.CaretIndex = plan.CaretIndex;
        box.Focus();
    }

    // The Go To section: a labelled combo of jump targets (document start/end + each heading) and a
    // Go button that jumps the caret/scroll there via DocumentView.BringBlockIntoView.
    private UIElement BuildGoToSection()
    {
        var panel = new StackPanel { Margin = new Thickness(Surface.Metrics.OuterMargin, 0, Surface.Metrics.OuterMargin, 0) };
        panel.Children.Add(new Separator { Margin = new Thickness(0, 0, 0, 6) });
        panel.Children.Add(new TextBlock { Text = Surface.GoToSectionLabel, FontWeight = FontWeights.SemiBold });

        var row = new Grid { Margin = new Thickness(0, 0, 0, 2) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        Grid.SetColumn(_goToTarget, 0);
        row.Children.Add(_goToTarget);

        var goButton = MakeButton(Surface.GoToButtonLabel, (_, _) => GoTo(), Surface.GoToButtonAutomationId);
        Grid.SetColumn(goButton, 1);
        row.Children.Add(goButton);

        panel.Children.Add(row);
        _goToTarget.DropDownOpened += (_, _) => PopulateGoToTargets();
        PopulateGoToTargets();
        return panel;
    }

    private void PopulateGoToTargets()
    {
        var plan = _session.BuildGoToTargets(_editor.Model, _goToTarget.SelectedIndex);
        _goToTarget.ItemsSource = plan.Targets;
        _goToTarget.SelectedIndex = plan.SelectedIndex;
    }

    private void GoTo()
    {
        var plan = _session.PlanGoTo(
            _goToTarget.SelectedItem as FindReplaceGoToTarget,
            _editor.Model.Blocks.Count);
        if (plan is null)
            return;

        switch (plan.Kind)
        {
            case FindReplaceGoToTargetKind.DocumentStart:
                _editor.CaretPosition = _editor.Document.ContentStart;
                _editor.Document.ContentStart.Paragraph?.BringIntoView();
                _editor.Focus();
                break;
            case FindReplaceGoToTargetKind.DocumentEnd:
                _editor.CaretPosition = _editor.Document.ContentEnd;
                _editor.Document.ContentEnd.Paragraph?.BringIntoView();
                _editor.Focus();
                break;
            default:
                _editor.BringBlockIntoView(plan.BlockIndex);
                break;
        }

        _status.Text = _session.State.StatusText;
    }

    private static void AddRow(Grid grid, int row, string label, UIElement field)
    {
        var text = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, Surface.Metrics.RowTopMargin, 8, 0) };
        Grid.SetRow(text, row);
        Grid.SetColumn(text, 0);
        grid.Children.Add(text);
        Grid.SetRow(field, row);
        Grid.SetColumn(field, 1);
        if (field is FrameworkElement fe)
            fe.Margin = new Thickness(0, Surface.Metrics.RowTopMargin, 0, 0);
        grid.Children.Add(field);
    }

    private static Button MakeButton(string content, RoutedEventHandler onClick, string automationId)
    {
        var button = new Button { Content = content, MinWidth = Surface.Metrics.ButtonMinWidth, Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(6, 3, 6, 3) };
        AutomationProperties.SetAutomationId(button, automationId);
        button.Click += onClick;
        return button;
    }

    private static Button MakeButton(FindReplaceDialogActionSpec action, RoutedEventHandler onClick) =>
        MakeButton(action.Label, onClick, action.AutomationId);

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

    private sealed class WpfFindReplaceCommandHost(DocumentView editor) : IFindReplaceDialogCommandHost
    {
        public bool FindNext(FindReplaceSearchRequest request)
        {
            var start = editor.Selection.IsEmpty ? editor.CaretPosition : editor.Selection.End;
            return SelectFrom(start, request) || SelectFrom(editor.Document.ContentStart, request);
        }

        public bool ReplaceNext(FindReplaceReplaceRequest request)
        {
            var replaced = !editor.Selection.IsEmpty
                && FindReplaceDialogPlanner.MatchesExactly(
                    editor.Selection.Text,
                    request.Term,
                    request.Options)
                && editor.RestrictEditingPolicy.Allows(RestrictEditingOperationKind.BodyTextEdit);
            var originalMatchText = replaced ? editor.Selection.Text : null;
            if (replaced)
            {
                editor.InsertText(request.Replacement);
            }

            var searchRequest = new FindReplaceSearchRequest(request.Term, request.Options);
            var start = editor.Selection.IsEmpty
                ? editor.CaretPosition
                : SkipTrackedLeftoverMatch(editor.Selection.End, originalMatchText);
            return SelectFrom(start, searchRequest)
                || SelectFrom(editor.Document.ContentStart, searchRequest);
        }

        public FindReplaceAllExecutionResult ReplaceAll(FindReplaceReplaceRequest request)
        {
            var restrictToSelection = !editor.Selection.IsEmpty;
            if (!editor.RestrictEditingPolicy.Allows(RestrictEditingOperationKind.BodyTextEdit))
                return new FindReplaceAllExecutionResult(0, restrictToSelection);

            var (from, limit) = restrictToSelection
                ? (editor.Selection.Start, editor.Selection.End)
                : (editor.Document.ContentStart, editor.Document.ContentEnd);

            var count = 0;
            var pointer = from;
            var searchRequest = new FindReplaceSearchRequest(request.Term, request.Options);
            while (count < 100_000 && TryFind(pointer, searchRequest, out var matchStart, out var matchEnd))
            {
                if (restrictToSelection && matchStart.CompareTo(limit) >= 0)
                    break;

                var originalMatchText = new TextRange(matchStart, matchEnd).Text;
                editor.Selection.Select(matchStart, matchEnd);
                editor.InsertText(request.Replacement);
                pointer = SkipTrackedLeftoverMatch(editor.Selection.End, originalMatchText);
                count++;
            }

            return new FindReplaceAllExecutionResult(count, restrictToSelection);
        }

        private static TextPointer SkipTrackedLeftoverMatch(TextPointer afterReplace, string? originalMatchText)
        {
            if (string.IsNullOrEmpty(originalMatchText))
                return afterReplace;
            var probeEnd = afterReplace.GetPositionAtOffset(originalMatchText.Length);
            if (probeEnd is null)
                return afterReplace;
            return new TextRange(afterReplace, probeEnd).Text == originalMatchText
                ? probeEnd
                : afterReplace;
        }

        private bool SelectFrom(TextPointer from, FindReplaceSearchRequest request)
        {
            if (!TryFind(from, request, out var matchStart, out var matchEnd))
                return false;

            editor.Selection.Select(matchStart, matchEnd);
            editor.Focus();
            return true;
        }

        private bool TryFind(
            TextPointer from,
            FindReplaceSearchRequest request,
            out TextPointer matchStart,
            out TextPointer matchEnd)
        {
            matchStart = matchEnd = editor.Document.ContentStart;
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
    }
}
