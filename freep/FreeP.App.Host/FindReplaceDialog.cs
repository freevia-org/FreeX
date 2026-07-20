using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Free.Shared.AppServices;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Host;

/// <summary>
/// Find and Replace dialog for FreeP (Wave 12B).
///
/// Modes:
///   - Find only (opened via Ctrl+F): Replace row is hidden.
///   - Find + Replace (opened via Ctrl+H): Replace row is visible.
///
/// Navigates to the next/previous match, selects the shape on the canvas,
/// and performs single or bulk replace via undoable <see cref="EditingSession"/> commands.
///
/// The dialog is modeless (Show, not ShowDialog) so the user can interact with
/// the slide canvas while the dialog is open.
/// </summary>
public sealed class FindReplaceDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
    private readonly EditingSession _editor;
    private readonly Action? _onNavigationOrMutation;

    // UI elements
    private readonly TextBox   _findBox;
    private readonly TextBox   _replaceBox;
    private readonly CheckBox  _matchCaseBox;
    private readonly CheckBox  _wholeWordBox;
    private readonly TextBlock _statusText;
    private readonly RowDefinition _replaceRow;
    private readonly RowDefinition _replaceButtonRow;
    private readonly Button _findNextButton;
    private readonly Button _findPreviousButton;
    private readonly Button _replaceButton;
    private readonly Button _replaceAllButton;

    // Search state
    private List<TextSearchMatch> _matches = new();
    private int _currentMatchIndex = -1;
    private bool _showReplace;

    internal FindReplaceWorkflowPlan LastWorkflowPlan { get; private set; } = null!;
    internal bool ShowReplace => _showReplace;
    internal string StatusText => _statusText.Text;

    // ── Construction ──────────────────────────────────────────────────────────

    public FindReplaceDialog(
        EditingSession editor,
        bool showReplace = false,
        Action? onNavigationOrMutation = null)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        _onNavigationOrMutation = onNavigationOrMutation;

        Title  = FindReplaceDialogPlanner.TitleForMode(showReplace);
        Width  = 440;
        SizeToContent = SizeToContent.Height;
        ResizeMode    = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        // ── Layout ────────────────────────────────────────────────────────────

        var grid = new Grid { Margin = new Thickness(12) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // Row 0: Find label + textbox
        // Row 1: Replace label + textbox  (hidden in Find-only mode)
        // Row 2: Options checkboxes
        // Row 3: Replace buttons  (hidden in Find-only mode)
        // Row 4: Find Next + status
        // Row 5: Replace All / status strip

        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 0 find
        _replaceRow = new RowDefinition { Height = GridLength.Auto };
        grid.RowDefinitions.Add(_replaceRow);                                    // 1 replace input
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 2 options
        _replaceButtonRow = new RowDefinition { Height = GridLength.Auto };
        grid.RowDefinitions.Add(_replaceButtonRow);                              // 3 replace buttons
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 4 find buttons
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 5 status

        // Row 0 — Find
        var findLabel = MakeLabel("Find what:");
        Grid.SetRow(findLabel, 0);
        Grid.SetColumn(findLabel, 0);
        grid.Children.Add(findLabel);

        _findBox = new TextBox { Margin = new Thickness(6, 4, 0, 4) };
        _findBox.TextChanged += (_, _) => InvalidateSearch();
        _findBox.KeyDown += OnFindBoxKeyDown;
        Grid.SetRow(_findBox, 0);
        Grid.SetColumn(_findBox, 1);
        grid.Children.Add(_findBox);

        // Row 1 — Replace
        var replaceLabel = MakeLabel("Replace with:");
        Grid.SetRow(replaceLabel, 1);
        Grid.SetColumn(replaceLabel, 0);
        grid.Children.Add(replaceLabel);

        _replaceBox = new TextBox { Margin = new Thickness(6, 4, 0, 4) };
        _replaceBox.TextChanged += (_, _) => RefreshWorkflowPlan();
        Grid.SetRow(_replaceBox, 1);
        Grid.SetColumn(_replaceBox, 1);
        grid.Children.Add(_replaceBox);

        // Row 2 — Options
        var optPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin      = new Thickness(0, 4, 0, 4)
        };
        _matchCaseBox = new CheckBox { Content = "Match case",  Margin = new Thickness(0, 0, 12, 0) };
        _wholeWordBox = new CheckBox { Content = "Whole word",  Margin = new Thickness(0, 0, 0, 0)  };
        _matchCaseBox.Checked   += (_, _) => InvalidateSearch();
        _matchCaseBox.Unchecked += (_, _) => InvalidateSearch();
        _wholeWordBox.Checked   += (_, _) => InvalidateSearch();
        _wholeWordBox.Unchecked += (_, _) => InvalidateSearch();
        optPanel.Children.Add(_matchCaseBox);
        optPanel.Children.Add(_wholeWordBox);
        Grid.SetRow(optPanel, 2);
        Grid.SetColumnSpan(optPanel, 2);
        grid.Children.Add(optPanel);

        // Row 3 — Replace / Replace All buttons
        var replBtnPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 4, 0, 0)
        };
        _replaceButton = MakeButton("Replace", OnReplace);
        _replaceAllButton = MakeButton("Replace All", OnReplaceAll);
        replBtnPanel.Children.Add(_replaceButton);
        replBtnPanel.Children.Add(_replaceAllButton);
        Grid.SetRow(replBtnPanel, 3);
        Grid.SetColumnSpan(replBtnPanel, 2);
        grid.Children.Add(replBtnPanel);

        // Row 4 — Find Next / Find Prev / Close
        var findBtnPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 4, 0, 0)
        };
        _findNextButton = MakeButton("Find Next", OnFindNext, isDefault: true);
        _findPreviousButton = MakeButton("Find Previous", OnFindPrev);
        var closeBtn    = MakeButton("Close",         (_, _) => Close());
        closeBtn.IsCancel = true;
        findBtnPanel.Children.Add(_findNextButton);
        findBtnPanel.Children.Add(_findPreviousButton);
        findBtnPanel.Children.Add(closeBtn);
        Grid.SetRow(findBtnPanel, 4);
        Grid.SetColumnSpan(findBtnPanel, 2);
        grid.Children.Add(findBtnPanel);

        // Row 5 — Status
        _statusText = new TextBlock
        {
            Margin     = new Thickness(0, 6, 0, 0),
            FontSize   = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66))
        };
        Grid.SetRow(_statusText, 5);
        Grid.SetColumnSpan(_statusText, 2);
        grid.Children.Add(_statusText);

        Content = grid;
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape)
                return;

            Close();
            e.Handled = true;
        };
        Loaded += (_, _) =>
        {
            _findBox.Focus();
            _findBox.SelectAll();
        };

        // Apply mode.
        ShowReplaceMode(showReplace);
    }

    // ── Mode switching ────────────────────────────────────────────────────────

    /// <summary>
    /// Shows or hides the Replace row/buttons.  Used to switch between Find and
    /// Find-and-Replace modes after the dialog is open.
    /// </summary>
    public void ShowReplaceMode(bool show)
    {
        _showReplace = show;
        SetReplaceRowsVisible(show);
        Title = FindReplaceDialogPlanner.TitleForMode(show);
        RefreshWorkflowPlan();
    }

    internal FindReplaceWorkflowPlan SetInputForTests(
        string? query,
        string? replacement = null,
        bool matchCase = false,
        bool wholeWord = false)
    {
        _findBox.Text = query ?? string.Empty;
        _replaceBox.Text = replacement ?? string.Empty;
        _matchCaseBox.IsChecked = matchCase;
        _wholeWordBox.IsChecked = wholeWord;
        InvalidateSearch();
        return LastWorkflowPlan;
    }

    internal FindReplaceWorkflowPlan NavigateForTests(int direction) => Navigate(direction);
    internal FindReplaceWorkflowPlan ReplaceAllForTests() => ReplaceAll();

    private void SetReplaceRowsVisible(bool visible)
    {
        var vis = visible ? GridLength.Auto : new GridLength(0);
        _replaceRow.Height       = vis;
        _replaceButtonRow.Height = vis;
        _replaceBox.Visibility   = visible ? Visibility.Visible : Visibility.Collapsed;
        foreach (UIElement child in ((Grid)Content).Children)
        {
            if (Grid.GetRow(child) == 1 || Grid.GetRow(child) == 3)
            {
                if (child != _replaceBox) // _replaceBox handled above
                    child.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            }
        }
    }

    // ── Button handlers ───────────────────────────────────────────────────────

    private void OnFindNext(object sender, RoutedEventArgs e) => Navigate(+1);
    private void OnFindPrev(object sender, RoutedEventArgs e) => Navigate(-1);

    private void OnReplace(object sender, RoutedEventArgs e) => ReplaceCurrent();

    private FindReplaceWorkflowPlan ReplaceCurrent()
    {
        EnsureMatches();
        int idx = FindReplaceDialogPlanner.ReplacementTargetIndex(_currentMatchIndex, _matches.Count);
        if (idx < 0)
        {
            return RefreshWorkflowPlan(
                FindReplaceDialogPolicy.NoMatchesStatus,
                FindReplacePolicyStatusKind.NoMatches);
        }

        _editor.ReplaceOne(_matches[idx], _replaceBox.Text ?? string.Empty);
        _onNavigationOrMutation?.Invoke();
        InvalidateSearch();
        return Navigate(+1);
    }

    private void OnReplaceAll(object sender, RoutedEventArgs e) => ReplaceAll();

    private FindReplaceWorkflowPlan ReplaceAll()
    {
        var query = _findBox.Text;
        if (!FindReplaceDialogPlanner.CanReplaceAll(query))
        {
            return RefreshWorkflowPlan(
                FindReplaceDialogPolicy.SearchTermRequiredMessage,
                FindReplacePolicyStatusKind.None);
        }

        int count = _editor.ReplaceAll(query, _replaceBox.Text ?? string.Empty, BuildOptions());
        _onNavigationOrMutation?.Invoke();
        InvalidateSearch();
        var status = FindReplaceDialogPlanner.ReplacementStatus(count);
        return RefreshWorkflowPlan(status.StatusText, status.StatusKind);
    }

    // ── Keyboard shortcut: Enter = Find Next ─────────────────────────────────

    private void OnFindBoxKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Return || e.Key == Key.Enter)
        {
            Navigate(+1);
            e.Handled = true;
        }
    }

    // ── Search helpers ────────────────────────────────────────────────────────

    private void EnsureMatches()
    {
        if (_matches.Count == 0)
            _matches = _editor.FindAll(_findBox.Text, BuildOptions());
    }

    private void InvalidateSearch()
    {
        _matches.Clear();
        _currentMatchIndex = -1;
        RefreshWorkflowPlan();
    }

    private FindReplaceWorkflowPlan Navigate(int direction)
    {
        EnsureMatches();

        var plan = FindReplaceDialogPlanner.Navigate(_currentMatchIndex, _matches.Count, direction);
        if (!plan.HasMatch)
            return RefreshWorkflowPlan(plan.StatusText, plan.StatusKind);

        _currentMatchIndex = plan.MatchIndex;
        var match = _matches[_currentMatchIndex];

        _editor.NavigateTo(match);
        _onNavigationOrMutation?.Invoke();

        return RefreshWorkflowPlan(plan.StatusText, plan.StatusKind);
    }

    private TextSearchOptions BuildOptions() => FindReplaceDialogPlanner.BuildOptions(
        _matchCaseBox.IsChecked == true,
        _wholeWordBox.IsChecked == true);

    private FindReplaceWorkflowPlan RefreshWorkflowPlan(
        string? statusText = null,
        FindReplacePolicyStatusKind statusKind = FindReplacePolicyStatusKind.None)
    {
        LastWorkflowPlan = FindReplaceDialogPlanner.BuildWorkflowPlan(
            _showReplace,
            _findBox.Text,
            _replaceBox.Text,
            _matchCaseBox.IsChecked == true,
            _wholeWordBox.IsChecked == true,
            _matches,
            _currentMatchIndex,
            statusText,
            statusKind);

        _statusText.Text = LastWorkflowPlan.StatusText;
        _statusText.Foreground = LastWorkflowPlan.StatusKind switch
        {
            FindReplacePolicyStatusKind.NoMatches or FindReplacePolicyStatusKind.NoReplacements =>
                new SolidColorBrush(Color.FromRgb(0xC6, 0x28, 0x28)),
            FindReplacePolicyStatusKind.Match or FindReplacePolicyStatusKind.Replacements =>
                new SolidColorBrush(Color.FromRgb(0x1B, 0x7E, 0x30)),
            _ => new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66)),
        };
        _findNextButton.IsEnabled = LastWorkflowPlan.CanSearch;
        _findPreviousButton.IsEnabled = LastWorkflowPlan.CanSearch;
        _replaceButton.IsEnabled = LastWorkflowPlan.CanReplace;
        _replaceAllButton.IsEnabled = LastWorkflowPlan.CanReplaceAll;
        return LastWorkflowPlan;
    }

    // ── UI factory helpers ────────────────────────────────────────────────────

    private static TextBlock MakeLabel(string text) => new TextBlock
    {
        Text              = text,
        VerticalAlignment = VerticalAlignment.Center,
        Margin            = new Thickness(0, 4, 6, 4),
        MinWidth          = 90
    };

    private static Button MakeButton(
        string text,
        RoutedEventHandler handler,
        bool isDefault = false)
    {
        var btn = new Button
        {
            Content = text,
            Padding = new Thickness(10, 4, 10, 4),
            Margin  = new Thickness(4, 0, 0, 0),
            MinWidth = 80,
            IsDefault = isDefault,
        };
        btn.Click += handler;
        return btn;
    }
}
