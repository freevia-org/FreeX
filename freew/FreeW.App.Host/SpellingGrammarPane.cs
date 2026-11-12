using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FreeW.App.Host.Editing;
using FreeW.App.Presentation.Proofing;

namespace FreeW.App.Host;

/// <summary>
/// The docked Spelling &amp; Grammar pane (Review &gt; Proofing &gt; Spelling &amp; Grammar, F7). A thin WPF
/// projection of the shared <see cref="ProofingReviewSession"/> and <see cref="ProofingReviewPanePlanner"/>:
/// the session owns the walk order and every decision, the planner owns the text and enablement, and the
/// editor owns selection and the undoable edits. This class only lays controls out and forwards clicks.
/// </summary>
internal sealed class SpellingGrammarPane
{
    private readonly DocumentView _editor;
    private readonly ProofingReviewSession _session;
    private readonly Action<string, string> _showMessage;

    private Border _pane = null!;
    private TextBlock _title = null!;
    private TextBlock _heading = null!;
    private TextBlock _issueText = null!;
    private TextBlock _explanation = null!;
    private TextBlock _suggestionsLabel = null!;
    private ListBox _suggestions = null!;
    private TextBlock _status = null!;
    private Button _change = null!;
    private Button _changeAll = null!;
    private Button _ignoreOnce = null!;
    private Button _ignoreAll = null!;
    private Button _addToDictionary = null!;
    private Button _close = null!;

    public SpellingGrammarPane(DocumentView editor, Action<string, string> showMessage)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        _showMessage = showMessage ?? throw new ArgumentNullException(nameof(showMessage));
        _session = new ProofingReviewSession(editor.CreateProofingReviewHost());
    }

    public bool IsVisible => _pane.Visibility == Visibility.Visible;

    public UIElement Build()
    {
        _title = new TextBlock { FontWeight = FontWeights.SemiBold, Margin = new Thickness(10, 8, 10, 6) };
        _heading = new TextBlock
        {
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(10, 4, 10, 2),
            TextWrapping = TextWrapping.Wrap,
        };
        _issueText = new TextBlock
        {
            Foreground = new SolidColorBrush(Color.FromRgb(0xB0, 0x1E, 0x1E)),
            Margin = new Thickness(10, 0, 10, 4),
            TextWrapping = TextWrapping.Wrap,
        };
        _explanation = new TextBlock
        {
            Foreground = new SolidColorBrush(Color.FromRgb(0x50, 0x50, 0x50)),
            Margin = new Thickness(10, 0, 10, 8),
            TextWrapping = TextWrapping.Wrap,
        };
        _suggestionsLabel = new TextBlock { Margin = new Thickness(10, 0, 10, 2) };
        _suggestions = new ListBox { Margin = new Thickness(10, 0, 10, 6), MinHeight = 96, MaxHeight = 180 };
        _suggestions.SelectionChanged += (_, _) => UpdateChangeEnablement();
        _suggestions.MouseDoubleClick += (_, _) => Run(() => _session.Change(_suggestions.SelectedIndex));
        _status = new TextBlock
        {
            Foreground = new SolidColorBrush(Color.FromRgb(0x60, 0x60, 0x60)),
            Margin = new Thickness(10, 0, 10, 8),
            TextWrapping = TextWrapping.Wrap,
        };

        _change = ActionButton(() => Run(() => _session.Change(_suggestions.SelectedIndex)));
        _changeAll = ActionButton(() => Run(() => _session.ChangeAll(_suggestions.SelectedIndex)));
        _ignoreOnce = ActionButton(() => Run(_session.IgnoreOnce, isEdit: false));
        _ignoreAll = ActionButton(() => Run(_session.IgnoreAll, isEdit: false));
        _addToDictionary = ActionButton(() => Run(_session.AddToDictionary, isEdit: false));
        _close = ActionButton(Hide);

        var layout = new StackPanel { Width = 260 };
        foreach (var element in new UIElement[]
                 {
                     _title, _heading, _issueText, _explanation, _suggestionsLabel, _suggestions, _status,
                     _change, _changeAll, _ignoreOnce, _ignoreAll, _addToDictionary, _close,
                 })
        {
            layout.Children.Add(element);
        }

        _pane = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0xFA, 0xFA, 0xFB)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xD0, 0xD0, 0xD0)),
            BorderThickness = new Thickness(1, 0, 0, 0),
            Visibility = Visibility.Collapsed,
            Child = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = layout,
            },
        };
        _pane.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape)
                return;
            e.Handled = true;
            Hide();
        };
        Render(lastChangeFailed: false);
        return _pane;
    }

    /// <summary>Starts (or restarts) the walk-through from the editor's caret — the F7 gesture.</summary>
    public void Start()
    {
        _editor.SetProofingReviewActive(true);
        _session.Start();
        if (FinishIfComplete())
            return;

        _pane.Visibility = Visibility.Visible;
        Render(lastChangeFailed: false);
        FocusFirstAction();
    }

    public void Hide()
    {
        _session.Close();
        _pane.Visibility = Visibility.Collapsed;
        _editor.SetProofingReviewActive(false);
        Render(lastChangeFailed: false);
        _editor.Focus();
    }

    private void Run(Func<bool> action, bool isEdit = true)
    {
        var succeeded = action();
        if (FinishIfComplete())
            return;

        Render(lastChangeFailed: isEdit && !succeeded);
    }

    // Word ends the check with a message and puts the pane away.
    private bool FinishIfComplete()
    {
        if (_session.Status != ProofingReviewStatus.Completed)
            return false;

        var plan = ProofingReviewPanePlanner.Build(_session, UiText.Get);
        _session.Close();
        _pane.Visibility = Visibility.Collapsed;
        _editor.SetProofingReviewActive(false);
        Render(lastChangeFailed: false);
        _showMessage(plan.StatusText, plan.Title);
        _editor.Focus();
        return true;
    }

    private void Render(bool lastChangeFailed)
    {
        var plan = ProofingReviewPanePlanner.Build(_session, UiText.Get, lastChangeFailed);
        _title.Text = plan.Title;
        _heading.Text = plan.Heading;
        _issueText.Text = plan.IssueText;
        _explanation.Text = plan.Explanation;
        _suggestionsLabel.Text = plan.SuggestionsLabel;
        _status.Text = plan.StatusText;

        var issueVisibility = plan.ShowIssue ? Visibility.Visible : Visibility.Collapsed;
        _heading.Visibility = issueVisibility;
        _issueText.Visibility = issueVisibility;
        _explanation.Visibility = plan.Explanation.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        _suggestionsLabel.Visibility = issueVisibility;
        _suggestions.Visibility = issueVisibility;
        _status.Visibility = plan.StatusText.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        _suggestions.Items.Clear();
        foreach (var suggestion in plan.Suggestions)
            _suggestions.Items.Add(suggestion);
        _suggestions.SelectedIndex = plan.Suggestions.Count > 0 ? 0 : -1;
        AutomationProperties.SetName(_suggestions, plan.SuggestionsLabel);

        Apply(_change, plan.Change);
        Apply(_changeAll, plan.ChangeAll);
        Apply(_ignoreOnce, plan.IgnoreOnce);
        Apply(_ignoreAll, plan.IgnoreAll);
        Apply(_addToDictionary, plan.AddToDictionary);
        Apply(_close, plan.Close);
        UpdateChangeEnablement();
    }

    private void UpdateChangeEnablement()
    {
        var hasSelection = _suggestions.SelectedIndex >= 0;
        _change.IsEnabled = _session.CanChange && hasSelection;
        _changeAll.IsEnabled = _session.CanChangeAll && hasSelection;
    }

    private void FocusFirstAction()
    {
        if (_suggestions.Items.Count > 0)
            _suggestions.Focus();
        else
            _ignoreOnce.Focus();
    }

    private static void Apply(Button button, ProofingReviewPaneAction action)
    {
        button.Content = action.Label;
        button.IsEnabled = action.IsEnabled;
        button.Visibility = action.IsVisible ? Visibility.Visible : Visibility.Collapsed;
        AutomationProperties.SetName(button, action.Label);
    }

    private static Button ActionButton(Action onClick)
    {
        var button = new Button
        {
            Margin = new Thickness(10, 0, 10, 6),
            Padding = new Thickness(8, 3, 8, 3),
            HorizontalContentAlignment = HorizontalAlignment.Left,
        };
        button.Click += (_, _) => onClick();
        return button;
    }
}
