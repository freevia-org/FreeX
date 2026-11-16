using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Automation;
using FreeW.App.Avalonia.Editing;
using FreeW.App.Presentation.Proofing;

namespace FreeW.App.Avalonia;

internal sealed class SpellingGrammarPane : Border
{
    private readonly DocumentView _editor;
    private ProofingReviewSession _session;
    private readonly Action<string, string> _showMessage;
    private readonly TextBlock _title;
    private readonly TextBlock _heading;
    private readonly TextBlock _issueText;
    private readonly TextBlock _explanation;
    private readonly TextBlock _suggestionsLabel;
    private readonly ListBox _suggestions;
    private readonly TextBlock _status;
    private readonly Button _change;
    private readonly Button _changeAll;
    private readonly Button _ignoreOnce;
    private readonly Button _ignoreAll;
    private readonly Button _addToDictionary;
    private readonly Button _close;

    internal SpellingGrammarPane(DocumentView editor, Action<string, string> showMessage)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        _showMessage = showMessage ?? throw new ArgumentNullException(nameof(showMessage));
        _session = new ProofingReviewSession(editor.CreateProofingReviewHost());
        _editor.DocumentLoaded += ResetForDocument;
        Width = 260;
        Background = new SolidColorBrush(Color.FromRgb(0xFA, 0xFA, 0xFB));
        BorderBrush = new SolidColorBrush(Color.FromRgb(0xD0, 0xD0, 0xD0));
        BorderThickness = new Thickness(1, 0, 0, 0);
        IsVisible = false;

        _title = TextBlock(new Thickness(10, 8, 10, 6), FontWeight.SemiBold);
        _heading = TextBlock(new Thickness(10, 4, 10, 2), FontWeight.SemiBold);
        _issueText = TextBlock(new Thickness(10, 0, 10, 4));
        _issueText.Foreground = new SolidColorBrush(Color.FromRgb(0xB0, 0x1E, 0x1E));
        _explanation = TextBlock(new Thickness(10, 0, 10, 8));
        _explanation.Foreground = new SolidColorBrush(Color.FromRgb(0x50, 0x50, 0x50));
        _suggestionsLabel = TextBlock(new Thickness(10, 0, 10, 2));
        _suggestions = new ListBox { Margin = new Thickness(10, 0, 10, 6), MinHeight = 96, MaxHeight = 180 };
        _suggestions.SelectionChanged += (_, _) => UpdateChangeEnablement();
        _suggestions.DoubleTapped += (_, _) => Run(() => _session.Change(_suggestions.SelectedIndex));
        _status = TextBlock(new Thickness(10, 0, 10, 8));
        _status.Foreground = new SolidColorBrush(Color.FromRgb(0x60, 0x60, 0x60));

        _change = ActionButton(() => Run(() => _session.Change(_suggestions.SelectedIndex)));
        _changeAll = ActionButton(() => Run(() => _session.ChangeAll(_suggestions.SelectedIndex)));
        _ignoreOnce = ActionButton(() => Run(_session.IgnoreOnce, isEdit: false));
        _ignoreAll = ActionButton(() => Run(_session.IgnoreAll, isEdit: false));
        _addToDictionary = ActionButton(() => Run(_session.AddToDictionary, isEdit: false));
        _close = ActionButton(Hide);

        var layout = new StackPanel { Width = 260 };
        foreach (var control in new Control[]
                 {
                     _title, _heading, _issueText, _explanation, _suggestionsLabel, _suggestions, _status,
                     _change, _changeAll, _ignoreOnce, _ignoreAll, _addToDictionary, _close,
                 })
            layout.Children.Add(control);

        Child = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = layout,
        };
        KeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape)
                return;
            e.Handled = true;
            Hide();
        };
        Render(lastChangeFailed: false);
    }

    internal bool IsReviewing => _session.IsReviewing;
    internal string IssueText => _issueText.Text ?? string.Empty;
    internal IReadOnlyList<string> Suggestions => _suggestions.Items.Cast<object>().Select(item => item.ToString() ?? string.Empty).ToArray();
    internal bool ChangeEnabled => _change.IsEnabled;
    internal bool IsPaneVisible => IsVisible;
    internal Button ChangeButtonForTests => _change;
    internal Button IgnoreAllButtonForTests => _ignoreAll;

    internal void Start()
    {
        _editor.SetProofingReviewActive(true);
        _session.Start();
        if (FinishIfComplete())
            return;

        IsVisible = true;
        Render(lastChangeFailed: false);
        if (_suggestions.ItemCount > 0)
            _suggestions.Focus();
        else
            _ignoreOnce.Focus();
    }

    internal void Hide()
    {
        _session.Close();
        IsVisible = false;
        _editor.SetProofingReviewActive(false);
        Render(lastChangeFailed: false);
        _editor.Focus();
    }

    private void ResetForDocument()
    {
        _session.Close();
        _session = new ProofingReviewSession(_editor.CreateProofingReviewHost());
        IsVisible = false;
        _editor.SetProofingReviewActive(false);
        Render(lastChangeFailed: false);
    }

    private void Run(Func<bool> action, bool isEdit = true)
    {
        var succeeded = action();
        if (FinishIfComplete())
            return;
        Render(lastChangeFailed: isEdit && !succeeded);
    }

    private bool FinishIfComplete()
    {
        if (_session.Status != ProofingReviewStatus.Completed)
            return false;

        var plan = ProofingReviewPanePlanner.Build(_session, UiText.Get);
        _session.Close();
        IsVisible = false;
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
        var issueVisible = plan.ShowIssue;
        _heading.IsVisible = issueVisible;
        _issueText.IsVisible = issueVisible;
        _explanation.IsVisible = plan.Explanation.Length > 0;
        _suggestionsLabel.IsVisible = issueVisible;
        _suggestions.IsVisible = issueVisible;
        _status.IsVisible = plan.StatusText.Length > 0;
        _suggestions.ItemsSource = plan.Suggestions;
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

    private static TextBlock TextBlock(Thickness margin, FontWeight? weight = null) => new()
    {
        Margin = margin,
        FontWeight = weight ?? FontWeight.Normal,
        TextWrapping = TextWrapping.Wrap,
    };

    private static Button ActionButton(Action onClick)
    {
        var button = new Button
        {
            Margin = new Thickness(10, 0, 10, 6),
            Padding = new Thickness(8, 3),
            HorizontalContentAlignment = HorizontalAlignment.Left,
        };
        button.Click += (_, _) => onClick();
        return button;
    }

    private static void Apply(Button button, ProofingReviewPaneAction action)
    {
        button.Content = action.Label;
        button.IsEnabled = action.IsEnabled;
        button.IsVisible = action.IsVisible;
        AutomationProperties.SetName(button, action.Label);
    }
}
