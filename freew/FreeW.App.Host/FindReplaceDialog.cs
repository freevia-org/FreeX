using Free.Shared.AppServices;
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
using FreeW.App.Host.Editing;
using FreeW.Core.Model;

namespace FreeW.App.Host;

/// <summary>
/// A modeless Find &amp; Replace tool over the FreeW editing surface, modelled on Word's: special
/// (caret) codes, find/replace formatting with a summary line under each box, the Match prefix /
/// Match suffix / Ignore punctuation / Ignore white-space options, and a search that covers every
/// story. Match decisions are delegated to the shared planner (<see cref="FindReplaceDialogPlanner"/>
/// and <see cref="FindReplaceStoryPlanner"/>); this class only owns the native controls and the
/// TextPointer traversal of the body. Includes a Go To section that jumps to a heading (via
/// <see cref="DocumentOutline"/>) or to the document start/end. Opened with Ctrl+F / Ctrl+H.
/// </summary>
internal sealed partial class FindReplaceDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
    private static readonly FindReplaceDialogSurfaceSpec Surface = FindReplaceDialogPlanner.Surface;
    private readonly DocumentView _editor;
    private readonly TextBox _findBox = new() { MinWidth = Surface.Metrics.FieldMinWidth };
    private readonly TextBox _replaceBox = new() { MinWidth = Surface.Metrics.FieldMinWidth };
    private readonly TextBlock _findFormatSummary = NewFormatSummary();
    private readonly TextBlock _replaceFormatSummary = NewFormatSummary();
    private readonly Dictionary<FindReplaceOptionKind, CheckBox> _options = [];
    private readonly ComboBox _goToTarget = new() { MinWidth = Surface.Metrics.FieldMinWidth, Margin = new Thickness(0, 6, 0, 0) };
    private readonly TextBlock _status = new() { Foreground = Brushes.Gray, Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap };
    private readonly FindReplaceDialogText _text = new(UiText.Get);
    private readonly FindReplaceDialogSession _session;

    public FindReplaceDialog(
        Window owner,
        DocumentView editor,
        FindReplaceOpenMode openMode = FindReplaceOpenMode.Find)
    {
        _editor = editor;
        _session = new FindReplaceDialogSession(
            new WpfFindReplaceCommandHost(editor, _text),
            openMode,
            FindReplaceDialogPlanner.ResolvePolicyText(UiText.Get),
            _text,
            () => editor.Model);
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
        AutomationProperties.SetAutomationId(_findFormatSummary, FindReplaceDialogPlanner.FindFormatSummaryAutomationId);
        AutomationProperties.SetAutomationId(_replaceFormatSummary, FindReplaceDialogPlanner.ReplaceFormatSummaryAutomationId);

        var grid = new Grid { Margin = new Thickness(Surface.Metrics.OuterMargin) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        AddRow(grid, Surface.Field(FindReplaceDialogFieldKind.Find).Label, _findBox);
        AddInFieldColumn(grid, _findFormatSummary);
        AddRow(grid, Surface.Field(FindReplaceDialogFieldKind.Replace).Label, _replaceBox);
        AddInFieldColumn(grid, _replaceFormatSummary);

        AddOption(grid, FindReplaceOptionKind.MatchCase, Surface.Option(FindReplaceOptionKind.MatchCase).AutomationId);
        AddOption(grid, FindReplaceOptionKind.WholeWord, Surface.Option(FindReplaceOptionKind.WholeWord).AutomationId);
        AddOption(grid, FindReplaceOptionKind.UseWildcards, Surface.Option(FindReplaceOptionKind.UseWildcards).AutomationId);
        AddOption(grid, FindReplaceOptionKind.MatchPrefix, Surface.Option(FindReplaceOptionKind.MatchPrefix).AutomationId);
        AddOption(grid, FindReplaceOptionKind.MatchSuffix, Surface.Option(FindReplaceOptionKind.MatchSuffix).AutomationId);
        AddOption(grid, FindReplaceOptionKind.IgnorePunctuation, Surface.Option(FindReplaceOptionKind.IgnorePunctuation).AutomationId);
        AddOption(grid, FindReplaceOptionKind.IgnoreWhiteSpace, Surface.Option(FindReplaceOptionKind.IgnoreWhiteSpace).AutomationId);

        // "Use wildcards" disables every option but Match case (incompatible, mirrors Word).
        _options[FindReplaceOptionKind.UseWildcards].Checked += (_, _) => ApplyOptionPolicy();
        _options[FindReplaceOptionKind.UseWildcards].Unchecked += (_, _) => ApplyOptionPolicy();
        ApplyOptionPolicy();

        // Special / Format / No Formatting act on whichever box last had focus, as in Word.
        var tools = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
        tools.Children.Add(BuildSpecialButton());
        tools.Children.Add(MakeToolButton(
            _text.Get(FindReplaceTextId.FormatButton),
            FindReplaceDialogPlanner.FormatButtonAutomationId,
            (_, _) => ChooseFormat()));
        tools.Children.Add(MakeToolButton(
            _text.Get(FindReplaceTextId.NoFormattingButton),
            FindReplaceDialogPlanner.NoFormattingButtonAutomationId,
            (_, _) => ShowFormatState(_session.ClearFormat(LastFocusedField))));
        AddInFieldColumn(grid, tools);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, Surface.Metrics.ActionTopMargin, 0, 0) };
        buttons.Children.Add(MakeButton(Surface.Actions[0], (_, _) => Execute(Surface.Actions[0].Kind)));
        buttons.Children.Add(MakeButton(Surface.Actions[1], (_, _) => Execute(Surface.Actions[1].Kind)));
        buttons.Children.Add(MakeButton(Surface.Actions[2], (_, _) => Execute(Surface.Actions[2].Kind)));
        buttons.Children.Add(MakeButton(Surface.Actions[3], (_, _) => Close()));
        AddInFieldColumn(grid, buttons);

        var outer = new StackPanel();
        outer.Children.Add(grid);
        outer.Children.Add(BuildGoToSection());
        var statusHost = new Border { Margin = new Thickness(Surface.Metrics.OuterMargin, 0, Surface.Metrics.OuterMargin, 12), Child = _status };
        outer.Children.Add(statusHost);
        Content = outer;

        Loaded += (_, _) => ActivateFor(_session.State.OpenMode);
    }

    internal void ActivateFor(FindReplaceOpenMode openMode)
    {
        var state = _session.ActivateFor(openMode);
        DialogFocus.FocusAndSelect(state.OpenMode == FindReplaceOpenMode.Replace ? _replaceBox : _findBox);
    }

    // Track which text field was focused last so Special / Format act on the right box.
    private TextBox _lastFocusedBox = null!;

    private FindReplaceDialogFieldKind LastFocusedField =>
        ReferenceEquals(_lastFocusedBox, _replaceBox)
            ? FindReplaceDialogFieldKind.Replace
            : FindReplaceDialogFieldKind.Find;

    private UIElement BuildSpecialButton()
    {
        _lastFocusedBox = _findBox;
        _findBox.GotFocus += (_, _) => _lastFocusedBox = _findBox;
        _replaceBox.GotFocus += (_, _) => _lastFocusedBox = _replaceBox;

        var menu = new ContextMenu();
        var btn = new Button
        {
            Content = Surface.SpecialButtonLabel,
            HorizontalAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(6, 3, 6, 3),
        };
        AutomationProperties.SetAutomationId(btn, Surface.SpecialButtonAutomationId);
        btn.Click += (_, _) =>
        {
            // Word offers a different list per box, and wildcard operators when wildcards are on.
            SyncSessionInput();
            menu.Items.Clear();
            foreach (var item in _session.BuildSpecialItems(LastFocusedField))
            {
                var entry = new MenuItem { Header = item.Label };
                var insertValue = item.Insert; // capture
                entry.Click += (_, _) => InsertSpecial(insertValue);
                menu.Items.Add(entry);
            }

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

    private void ChooseFormat()
    {
        var field = LastFocusedField;
        if (FindFormatDialog.Choose(this, field, _session.FormatFor(field), _editor.Model, _text) is { } format)
            ShowFormatState(_session.SetFormat(field, format));
    }

    private void ShowFormatState(FindReplaceDialogState state)
    {
        ShowSummary(_findFormatSummary, state.FindFormatSummary);
        ShowSummary(_replaceFormatSummary, state.ReplaceFormatSummary);
    }

    private static void ShowSummary(TextBlock summary, string text)
    {
        summary.Text = text;
        summary.Visibility = text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static TextBlock NewFormatSummary() => new()
    {
        Foreground = Brushes.Gray,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 2, 0, 0),
        Visibility = Visibility.Collapsed,
    };

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

    private static void AddRow(Grid grid, string label, UIElement field)
    {
        var row = grid.RowDefinitions.Count;
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
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

    private void AddOption(Grid grid, FindReplaceOptionKind kind, string automationId)
    {
        var box = new CheckBox
        {
            Content = FindReplaceDialogPlanner.LabelFor(kind, _text),
            Margin = new Thickness(0, _options.Count == 0 ? 6 : 4, 0, 0),
        };
        AutomationProperties.SetAutomationId(box, automationId);
        _options[kind] = box;
        AddInFieldColumn(grid, box);
    }

    private static void AddInFieldColumn(Grid grid, UIElement element)
    {
        var row = grid.RowDefinitions.Count;
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(element, row);
        Grid.SetColumn(element, 1);
        grid.Children.Add(element);
    }

    private static Button MakeButton(string content, RoutedEventHandler onClick, string automationId)
    {
        var button = new Button { Content = content, MinWidth = Surface.Metrics.ButtonMinWidth, Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(6, 3, 6, 3) };
        AutomationProperties.SetAutomationId(button, automationId);
        button.Click += onClick;
        return button;
    }

    private static Button MakeToolButton(string content, string automationId, RoutedEventHandler onClick)
    {
        var button = new Button { Content = content, Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(6, 3, 6, 3) };
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
            IsChecked(FindReplaceOptionKind.MatchCase),
            IsChecked(FindReplaceOptionKind.WholeWord),
            IsChecked(FindReplaceOptionKind.UseWildcards),
            IsChecked(FindReplaceOptionKind.MatchPrefix),
            IsChecked(FindReplaceOptionKind.MatchSuffix),
            IsChecked(FindReplaceOptionKind.IgnorePunctuation),
            IsChecked(FindReplaceOptionKind.IgnoreWhiteSpace));

    private bool IsChecked(FindReplaceOptionKind kind) => _options[kind].IsChecked == true;

    private void ApplyOptionPolicy()
    {
        var state = SyncSessionInput();
        foreach (var (kind, box) in _options)
        {
            box.IsEnabled = state.IsOptionEnabled(kind);
            if (box.IsChecked == true && !state.IsOptionChecked(kind))
                box.IsChecked = false;
        }
    }

    private sealed class WpfFindReplaceCommandHost(DocumentView editor, FindReplaceDialogText text)
        : IFindReplaceDialogCommandHost
    {
        private const int MaxReplacements = 100_000;

        // Find Next has left the body: the match it is "on" lives in a header/footer, a note, a
        // comment or a text box, none of which the body RichTextBox can select. The walk continues
        // from _storyPosition until the stories run out, then wraps back to the top of the body.
        private FindReplaceStoryMatch? _storyMatch;
        private FindReplaceSearchRequest? _storyRequest;
        private FindReplaceStoryPosition? _storyPosition;

        public string? LastMatchStatus { get; private set; }

        public bool FindNext(FindReplaceSearchRequest request)
        {
            LastMatchStatus = null;
            return Advance(request);
        }

        public bool ReplaceNext(FindReplaceReplaceRequest request)
        {
            LastMatchStatus = null;
            var search = request.ToSearchRequest();
            if (!editor.RestrictEditingPolicy.Allows(RestrictEditingOperationKind.BodyTextEdit))
                return Advance(search);

            if (_storyMatch is { } story && Equals(_storyRequest, search))
            {
                editor.CommitToModel();
                if (FindReplaceStoryPlanner.IsCurrent(editor.Model, search, story))
                {
                    var replacement = FindReplaceStoryPlanner.PlanReplacement(
                        editor.Model, story, request, RevisionContext());
                    if (replacement.Command is { } command)
                        editor.Commands.Execute(command);
                    _storyPosition = replacement.Resume();
                }

                return Advance(search);
            }

            if (!editor.Selection.IsEmpty)
            {
                var projection = Project(search);
                var selectionStart = projection.OffsetOf(editor.Selection.Start);
                var selectionEnd = projection.OffsetOf(editor.Selection.End);
                var selected = FindReplaceDialogPlanner.FindAllInSegments(projection.Segments, search)
                    .FirstOrDefault(match =>
                        match.Start == selectionStart && match.Start + match.Length == selectionEnd);
                if (selected.Length > 0)
                {
                    // Text plus formatting is several bus commands; the user asked for one Replace.
                    int resume;
                    editor.Commands.BeginUndoGroup(notifyOnEachExecute: true);
                    try
                    {
                        resume = ReplaceBodyMatch(ref projection, selected, request, out _);
                    }
                    finally
                    {
                        if (editor.Commands.IsUndoGroupOpen)
                            editor.Commands.CommitUndoGroup("Replace");
                    }

                    ClearStoryCursor();
                    projection = Project(search);
                    return SelectBody(projection, search, resume)
                        || SelectStory(search, null)
                        || SelectBody(projection, search, 0);
                }
            }

            return Advance(search);
        }

        public FindReplaceAllExecutionResult ReplaceAll(FindReplaceReplaceRequest request)
        {
            LastMatchStatus = null;
            ClearStoryCursor();
            var restrictToSelection = !editor.Selection.IsEmpty;
            if (!editor.RestrictEditingPolicy.Allows(RestrictEditingOperationKind.BodyTextEdit))
                return new FindReplaceAllExecutionResult(0, restrictToSelection);

            var search = request.ToSearchRequest();
            var projection = Project(search);
            var from = restrictToSelection ? projection.OffsetOf(editor.Selection.Start) : 0;
            var limit = restrictToSelection ? projection.OffsetOf(editor.Selection.End) : int.MaxValue;
            var count = 0;

            // Every edit below lands on editor.Commands as its own DocumentCommandBus entry; without
            // this group, N replacements become N separate undo-stack entries and one Ctrl+Z only reverts
            // the last match instead of the whole Replace All (matches the BeginUndoGroup/CommitUndoGroup
            // idiom other multi-step edits already use, e.g. DocumentEditingSession.ApplyMultilevelListDefinition).
            // notifyOnEachExecute: true -- unlike those coordinators, each replacement here is FOUND by
            // walking the rendered surface (the projection), so the redraw between edits must still happen
            // mid-batch or the next edit's CommitToModel() re-reads the stale surface and silently discards
            // every replacement but the last (see DocumentCommandBus.BeginUndoGroup's doc comment).
            editor.Commands.BeginUndoGroup(notifyOnEachExecute: true);
            try
            {
                for (var visited = 0; visited < MaxReplacements; visited++)
                {
                    var start = from;
                    var match = FindReplaceDialogPlanner.FindAllInSegments(projection.Segments, search)
                        .FirstOrDefault(candidate => candidate.Start >= start);
                    if (match.Length == 0 || (restrictToSelection && match.Start >= limit))
                        break;

                    var lengthBefore = projection.Length;
                    from = ReplaceBodyMatch(ref projection, match, request, out var replaced);
                    if (restrictToSelection)
                        limit += projection.Length - lengthBefore;
                    if (replaced)
                        count++;
                }

                // r179: the body FlowDocument is finished; now every story the projection cannot
                // reach at all (they are not in editor.Document): the headers and footers of every
                // section, footnotes, endnotes, comments and text boxes. Skipped when the operation
                // is restricted to a selection -- a selection is a body concept, and reaching outside
                // it is the r178 bug the Avalonia side already had.
                if (!restrictToSelection)
                {
                    editor.CommitToModel();
                    count += FindReplaceStoryPlanner.ReplaceAll(
                        editor.Model,
                        request,
                        editor.Commands.Execute,
                        RevisionContext());
                }

                editor.Commands.CommitUndoGroup("Replace All");
            }
            catch
            {
                // r177: ROLL BACK, do not merely abort. AbortUndoGroup discards the group without
                // reverting anything already applied (its own doc comment says so and tells the
                // caller to handle cleanup -- nothing here did). Because notifyOnEachExecute is true
                // above, every replacement before the failing one is already written and on screen;
                // abandoning the group left them permanently in the document AND absent from the
                // undo stack, so Ctrl+Z skipped straight past them and the user had no way back.
                // Replace All is one operation to the user: if it cannot finish, it must leave the
                // document as it found it.
                // r178: only roll back if the group is still open. The throw can come from
                // CommitUndoGroup itself -- it clears the batch BEFORE raising Changed, and Changed
                // runs the renderer synchronously -- in which case the work is already committed and
                // undoable, and an unguarded RollbackUndoGroup threw "No undo group is open" over the
                // top of the real failure. AbortUndoGroup was idempotent so this never showed before
                // r177 swapped it. Same guard DocumentUndoGroupExecutor.Execute already uses.
                if (editor.Commands.IsUndoGroupOpen)
                {
                    editor.Commands.RollbackUndoGroup();
                    // The rollback is silent by design, and this loop has been rendering into the
                    // live FlowDocument all along (notifyOnEachExecute above), so the surface is now
                    // ahead of the reverted model. Pull it back, or the next edit commits the
                    // rolled-back text straight into the document.
                    editor.RefreshFromModel();
                }

                throw;
            }

            return new FindReplaceAllExecutionResult(count, restrictToSelection);
        }

        /// <summary>
        /// Moves to the next match: the rest of the body from the caret, then every other story, then
        /// the body again from its top. While the walk is inside the other stories it stays there
        /// until they are exhausted.
        /// </summary>
        private bool Advance(FindReplaceSearchRequest request)
        {
            var projection = Project(request);
            if (_storyMatch is not null && Equals(_storyRequest, request))
            {
                if (SelectStory(request, _storyPosition))
                    return true;

                ClearStoryCursor();
                return SelectBody(projection, request, 0) || SelectStory(request, null);
            }

            ClearStoryCursor();
            var from = projection.OffsetOf(
                editor.Selection.IsEmpty ? editor.CaretPosition : editor.Selection.End);
            return SelectBody(projection, request, from)
                || SelectStory(request, null)
                || SelectBody(projection, request, 0);
        }

        private FindReplaceBodyProjection Project(FindReplaceSearchRequest request) =>
            editor.BuildFindReplaceProjection(includeFormatting: request.Format is not null);

        private bool SelectBody(FindReplaceBodyProjection projection, FindReplaceSearchRequest request, int from)
        {
            foreach (var (start, length) in FindReplaceDialogPlanner.FindAllInSegments(projection.Segments, request))
            {
                if (start < from || !projection.TryGetRange(start, length, out var matchStart, out var matchEnd))
                    continue;

                editor.Selection.Select(matchStart, matchEnd);
                editor.Focus();
                return true;
            }

            return false;
        }

        private bool SelectStory(FindReplaceSearchRequest request, FindReplaceStoryPosition? from)
        {
            editor.CommitToModel();
            if (FindReplaceStoryPlanner.FindNext(editor.Model, request, from) is not { } match)
                return false;

            _storyMatch = match;
            _storyRequest = request;
            _storyPosition = match.End;
            LastMatchStatus = FindReplaceStoryPlanner.DescribeMatch(match, text);
            return true;
        }

        private void ClearStoryCursor()
        {
            _storyMatch = null;
            _storyRequest = null;
            _storyPosition = null;
        }

        private FindReplaceRevisionContext? RevisionContext() =>
            editor.TrackChangesEnabled
                ? new FindReplaceRevisionContext(editor.RevisionAuthor, editor.RevisionDateXmlForEdit())
                : null;

        /// <summary>
        /// Replaces one body match through the editor (so Track Changes, restrict-editing and undo all
        /// behave as for typing) and returns the haystack offset the search resumes from, with
        /// <paramref name="projection"/> rebuilt against the re-rendered surface.
        /// </summary>
        /// <remarks>
        /// Every edit re-renders and drops the selection, so each step re-selects by offset. The
        /// replaced stretch is located by how much the haystack grew: untracked that is exactly the
        /// new text; tracked it also holds the struck-through original, which projects as
        /// placeholders nothing can match -- so a replacement that re-creates the search term cannot
        /// be found again either way.
        /// </remarks>
        private int ReplaceBodyMatch(
            ref FindReplaceBodyProjection projection,
            (int Start, int Length) match,
            FindReplaceReplaceRequest request,
            out bool replaced)
        {
            replaced = false;
            var skip = match.Start + match.Length;
            var found = projection.Text.Substring(match.Start, match.Length);
            var plan = FindReplaceDialogPlanner.PlanReplacement(request, found);
            var includeFormatting = request.FindFormat is not null;
            var regionLength = match.Length;

            if (plan.ReplacesText)
            {
                if (!projection.TryGetRange(match.Start, match.Length, out var matchStart, out var matchEnd))
                    return skip;

                var before = projection;
                editor.Selection.Select(matchStart, matchEnd);
                editor.ReplaceSelectionForFind(plan.Text);
                projection = editor.BuildFindReplaceProjection(includeFormatting);

                // Unchanged text means the editor refused the edit (a locked region, say): step over
                // the match rather than report a replacement that did not happen.
                if (plan.Text != found && projection.Text == before.Text)
                    return skip;

                regionLength = Math.Max(0, match.Length + projection.Length - before.Length);
            }

            replaced = true;
            if (plan.Format is { } format)
            {
                var (start, length) = TrimPlaceholders(projection.Text, match.Start, regionLength);
                if (format.ParagraphStyleId is { Length: > 0 } styleId && Select(projection, start, length))
                {
                    editor.SetParagraphStyle(styleId);
                    projection = editor.BuildFindReplaceProjection(includeFormatting);
                }

                if (format.HasCharacterFormatting && Select(projection, start, length))
                {
                    editor.ApplyFindCharacterFormatToSelection(format);
                    projection = editor.BuildFindReplaceProjection(includeFormatting);
                }
            }

            return match.Start + regionLength;
        }

        private bool Select(FindReplaceBodyProjection projection, int start, int length)
        {
            if (!projection.TryGetRange(start, length, out var rangeStart, out var rangeEnd))
                return false;

            editor.Selection.Select(rangeStart, rangeEnd);
            return !editor.Selection.IsEmpty;
        }

        private static (int Start, int Length) TrimPlaceholders(string text, int start, int length)
        {
            var end = Math.Min(text.Length, start + length);
            start = Math.Min(start, end);
            while (start < end && text[start] == FindSpecialCodes.ObjectPlaceholder)
                start++;
            while (end > start && text[end - 1] == FindSpecialCodes.ObjectPlaceholder)
                end--;
            return (start, end - start);
        }
    }
}
