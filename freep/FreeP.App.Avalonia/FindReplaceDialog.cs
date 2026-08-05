using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Free.Shared.AppServices;
using Free.Shared.Shell.Avalonia;
using FreeP.App.Compositor;

namespace FreeP.App.Avalonia;

internal sealed class FindReplaceDialog : Window
{
    private static readonly AvaloniaCompactDialogChromeStyle DialogChromeStyle = new(FontFamily.Default);

    private readonly FindReplaceDialogSession _session;
    private readonly TextBox _findBox;
    private readonly TextBlock _replaceLabel;
    private readonly TextBox _replaceBox;
    private readonly CheckBox _matchCaseCheck;
    private readonly CheckBox _wholeWordCheck;
    private readonly Button _findNextButton;
    private readonly Button _findPreviousButton;
    private readonly Button _replaceButton;
    private readonly Button _replaceAllButton;
    private readonly TextBlock _statusText;
    private readonly Grid _replaceInputRow;
    private readonly StackPanel _replaceButtonRow;
    internal FindReplaceWorkflowPlan LastWorkflowPlan => _session.LastWorkflowPlan;
    internal bool ShowReplace => _session.ShowReplace;
    internal string StatusText => _statusText.Text ?? string.Empty;

    public FindReplaceDialog(
        EditingSession editor,
        bool showReplace = false,
        Action? onNavigationOrMutation = null)
    {
        _session = new FindReplaceDialogSession(editor, showReplace, onNavigationOrMutation);

        Width = 425.3333333333333;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brushes.White;

        _findBox = new TextBox { MinWidth = 260, Margin = new Thickness(6, 4, 0, 4) };
        _replaceLabel = BuildLabel("Replace with:");
        _replaceBox = new TextBox { MinWidth = 260, Margin = new Thickness(6, 4, 0, 4) };
        _matchCaseCheck = new CheckBox { Content = "Match case", Margin = new Thickness(0, 0, 12, 0) };
        _wholeWordCheck = new CheckBox { Content = "Whole word" };
        _findNextButton = BuildButton(
            "Find Next",
            () => ApplyWorkflowPlan(_session.Navigate(+1)),
            isDefault: true);
        _findPreviousButton = BuildButton(
            "Find Previous",
            () => ApplyWorkflowPlan(_session.Navigate(-1)));
        _replaceButton = BuildButton(
            "Replace",
            () => ApplyWorkflowPlan(_session.ReplaceCurrent()));
        _replaceAllButton = BuildButton(
            "Replace All",
            () => ApplyWorkflowPlan(_session.ReplaceAll()));
        var closeButton = BuildButton("Close", Close, isCancel: true);
        _statusText = new TextBlock
        {
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 0),
        };

        AvaloniaCompactDialogChrome.ApplyTextBox(_findBox, DialogChromeStyle);
        AvaloniaCompactDialogChrome.ApplyTextBox(_replaceBox, DialogChromeStyle);
        AvaloniaCompactDialogChrome.ApplyCheckBox(_matchCaseCheck, DialogChromeStyle);
        AvaloniaCompactDialogChrome.ApplyCheckBox(_wholeWordCheck, DialogChromeStyle);

        _findBox.TextChanged += (_, _) => ApplyWorkflowPlan(_session.SetQuery(_findBox.Text));
        _replaceBox.TextChanged += (_, _) => ApplyWorkflowPlan(_session.SetReplacement(_replaceBox.Text));
        _matchCaseCheck.IsCheckedChanged += (_, _) =>
            ApplyWorkflowPlan(_session.SetMatchCase(_matchCaseCheck.IsChecked == true));
        _wholeWordCheck.IsCheckedChanged += (_, _) =>
            ApplyWorkflowPlan(_session.SetWholeWord(_wholeWordCheck.IsChecked == true));
        _findBox.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter)
                return;

            ApplyWorkflowPlan(_session.Navigate(+1));
            e.Handled = true;
        };

        _replaceInputRow = BuildInputRow(_replaceLabel, _replaceBox);
        _replaceButtonRow = AvaloniaCompactDialogChrome.CreateActionRow(
            [_replaceButton, _replaceAllButton],
            new Thickness(0, 4, 0, 0));
        _replaceButtonRow.Spacing = 4;

        Content = new StackPanel
        {
            Margin = new Thickness(12),
            Children =
            {
                BuildInputRow(BuildLabel("Find what:"), _findBox),
                _replaceInputRow,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Margin = new Thickness(0, 4, 0, 4),
                    Children = { _matchCaseCheck, _wholeWordCheck },
                },
                _replaceButtonRow,
                AvaloniaCompactDialogChrome.CreateActionRow(
                    [_findNextButton, _findPreviousButton, closeButton],
                    new Thickness(0, 4, 0, 0)),
                _statusText,
            },
        };

        KeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape)
                return;

            Close();
            e.Handled = true;
        };
        Opened += (_, _) =>
        {
            _findBox.Focus();
            _findBox.SelectAll();
        };
        ShowReplaceMode(showReplace);
    }

    internal void ShowReplaceMode(bool show)
    {
        Height = show ? 198.66666666666666 : 134;
        _replaceInputRow.IsVisible = show;
        _replaceButtonRow.IsVisible = show;
        ApplyWorkflowPlan(_session.SetShowReplace(show));
    }

    internal FindReplaceWorkflowPlan SetInputForTests(
        string? query,
        string? replacement = null,
        bool matchCase = false,
        bool wholeWord = false)
    {
        _findBox.Text = query ?? string.Empty;
        _replaceBox.Text = replacement ?? string.Empty;
        _matchCaseCheck.IsChecked = matchCase;
        _wholeWordCheck.IsChecked = wholeWord;
        return ApplyWorkflowPlan(_session.SetInput(query, replacement, matchCase, wholeWord));
    }

    internal FindReplaceWorkflowPlan NavigateForTests(int direction) =>
        ApplyWorkflowPlan(_session.Navigate(direction));

    internal FindReplaceWorkflowPlan ReplaceAllForTests() =>
        ApplyWorkflowPlan(_session.ReplaceAll());

    private FindReplaceWorkflowPlan ApplyWorkflowPlan(FindReplaceWorkflowPlan plan)
    {
        Title = plan.Title;
        _statusText.Text = plan.StatusText;
        _statusText.Foreground = plan.StatusKind switch
        {
            FindReplacePolicyStatusKind.NoMatches or FindReplacePolicyStatusKind.NoReplacements =>
                new SolidColorBrush(Color.FromRgb(0xC6, 0x28, 0x28)),
            FindReplacePolicyStatusKind.Match or FindReplacePolicyStatusKind.Replacements =>
                new SolidColorBrush(Color.FromRgb(0x1B, 0x7E, 0x30)),
            _ => new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66)),
        };
        _findNextButton.IsEnabled = plan.CanSearch;
        _findPreviousButton.IsEnabled = plan.CanSearch;
        _replaceButton.IsEnabled = plan.CanReplace;
        _replaceAllButton.IsEnabled = plan.CanReplaceAll;
        return plan;
    }

    private static Grid BuildInputRow(TextBlock label, Control field)
    {
        var row = new Grid
        {
            Margin = new Thickness(0, 0, 0, 6),
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(90) },
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
            },
        };
        Grid.SetColumn(field, 1);
        row.Children.Add(label);
        row.Children.Add(field);
        return row;
    }

    private static TextBlock BuildLabel(string text) => new()
    {
        Text = text,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(0, 4, 6, 4),
        MinWidth = 90,
    };

    private static Button BuildButton(
        string text,
        Action action,
        bool isDefault = false,
        bool isCancel = false)
    {
        var button = new Button { Content = text, IsCancel = isCancel };
        AvaloniaCompactDialogChrome.ApplyButton(button, DialogChromeStyle, minWidth: 80, isDefault: isDefault);
        button.Click += (_, _) => action();
        return button;
    }
}
