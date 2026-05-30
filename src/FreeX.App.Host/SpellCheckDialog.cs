using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;

namespace FreeX.App.Host;

public enum SpellCheckDialogAction
{
    Replace,
    ReplaceAll,
    Ignore,
    IgnoreAll,
    Add
}

public sealed record SpellCheckDialogResult(SpellCheckDialogAction Action, string? Replacement);

public sealed class SpellCheckDialog : Window
{
    private readonly TextBox _notInDictionaryBox = new();
    private readonly TextBox _replacementBox = new();
    private readonly ListBox _suggestionsBox = new();
    private readonly Button _changeButton = new() { Content = UiText.Get("SpellCheck_Change"), Width = 90, IsDefault = true };
    private readonly Button _changeAllButton = new() { Content = UiText.Get("SpellCheck_ChangeAll"), Width = 90 };

    public SpellCheckDialogResult Result { get; private set; }

    public SpellCheckDialog(string word, string suggestion)
    {
        Result = CreateReplaceResult(word, suggestion);
        Title = UiText.Get("SpellCheck_Spelling");
        Width = 480;
        Height = 330;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        _notInDictionaryBox.Text = word;
        _notInDictionaryBox.IsReadOnly = true;
        _notInDictionaryBox.Height = 56;
        _notInDictionaryBox.TextWrapping = TextWrapping.Wrap;
        AutomationProperties.SetName(_notInDictionaryBox, UiText.Get("SpellCheck_NotInDictionary2"));
        AutomationProperties.SetAutomationId(_notInDictionaryBox, "SpellCheckNotInDictionaryBox");
        AutomationProperties.SetHelpText(_notInDictionaryBox, UiText.Get("SpellCheck_ShowsTheWordThatWasNotFoundInTheDictionary"));
        _replacementBox.Text = suggestion;
        _replacementBox.TextChanged += (_, _) => RefreshChangeButtonState();
        AutomationProperties.SetName(_replacementBox, UiText.Get("SpellCheck_ChangeTo2"));
        AutomationProperties.SetAutomationId(_replacementBox, "SpellCheckReplacementBox");
        AutomationProperties.SetHelpText(_replacementBox, UiText.Get("SpellCheck_EnterTheReplacementTextForTheMisspelledWord"));
        if (!string.IsNullOrWhiteSpace(suggestion))
        {
            _suggestionsBox.Items.Add(suggestion);
            _suggestionsBox.SelectedIndex = 0;
        }

        _suggestionsBox.Height = 76;
        AutomationProperties.SetName(_suggestionsBox, UiText.Get("SpellCheck_Suggestions2"));
        AutomationProperties.SetAutomationId(_suggestionsBox, "SpellCheckSuggestionsList");
        AutomationProperties.SetHelpText(_suggestionsBox, UiText.Get("SpellCheck_ChooseASuggestedSpellingReplacement"));
        _suggestionsBox.SelectionChanged += (_, _) =>
        {
            if (_suggestionsBox.SelectedItem is string selected)
                _replacementBox.Text = selected;
        };
        _suggestionsBox.MouseDoubleClick += (_, _) => Accept(CreateReplaceResult(word, _replacementBox.Text));

        Content = CreateSpellCheckContent(word);
        RefreshChangeButtonState();
        Loaded += (_, _) => FocusInitialKeyboardTarget();
    }

    public static SpellCheckDialogResult CreateReplaceResult(string word, string replacement) =>
        new(SpellCheckDialogAction.Replace, string.IsNullOrWhiteSpace(replacement) ? word : replacement.Trim());

    public static SpellCheckDialogResult CreateReplaceAllResult(string word, string replacement) =>
        new(SpellCheckDialogAction.ReplaceAll, string.IsNullOrWhiteSpace(replacement) ? word : replacement.Trim());

    public static SpellCheckDialogResult CreateIgnoreResult() =>
        new(SpellCheckDialogAction.Ignore, null);

    public static SpellCheckDialogResult CreateIgnoreAllResult() =>
        new(SpellCheckDialogAction.IgnoreAll, null);

    public static SpellCheckDialogResult CreateAddResult(string word) =>
        new(SpellCheckDialogAction.Add, word.Trim());

    private void Accept(SpellCheckDialogResult result)
    {
        Result = result;
        DialogResult = true;
    }

    private void RefreshChangeButtonState()
    {
        var hasReplacement = !string.IsNullOrWhiteSpace(_replacementBox.Text);
        _changeButton.IsEnabled = hasReplacement;
        _changeAllButton.IsEnabled = hasReplacement;
    }

    private void FocusInitialKeyboardTarget()
    {
        if (_suggestionsBox.Items.Count > 0)
        {
            _suggestionsBox.Focus();
            Keyboard.Focus(_suggestionsBox);
            return;
        }

        DialogFocus.FocusAndSelect(_replacementBox);
    }

    private UIElement CreateSpellCheckContent(string word)
    {
        var root = new Grid { Margin = new Thickness(16) };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(124) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var fields = new StackPanel { Margin = new Thickness(0, 0, 12, 0) };
        fields.Children.Add(new Label { Content = UiText.Get("SpellCheck_NotInDictionary"), Target = _notInDictionaryBox, Padding = new Thickness(0), Margin = new Thickness(0, 0, 0, 4) });
        fields.Children.Add(_notInDictionaryBox);
        fields.Children.Add(new Label { Content = UiText.Get("SpellCheck_Suggestions"), Target = _suggestionsBox, Padding = new Thickness(0), Margin = new Thickness(0, 10, 0, 4) });
        fields.Children.Add(_suggestionsBox);
        fields.Children.Add(new Label { Content = UiText.Get("SpellCheck_ChangeTo"), Target = _replacementBox, Padding = new Thickness(0), Margin = new Thickness(0, 10, 0, 4) });
        fields.Children.Add(_replacementBox);
        root.Children.Add(fields);

        var actionButtons = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right };
        actionButtons.Children.Add(CreateSpellingButton(new Button { Content = UiText.Get("SpellCheck_IgnoreOnce"), Width = 118 }, "SpellCheckIgnoreOnceButton", UiText.Get("SpellCheck_IgnoreThisOccurrenceHelpText"), (_, _) => Accept(CreateIgnoreResult())));
        actionButtons.Children.Add(CreateSpellingButton(new Button { Content = UiText.Get("SpellCheck_IgnoreAll"), Width = 90 }, "SpellCheckIgnoreAllButton", UiText.Get("SpellCheck_IgnoreAllOccurrencesHelpText"), (_, _) => Accept(CreateIgnoreAllResult())));
        actionButtons.Children.Add(CreateSpellingButton(_changeButton, "SpellCheckChangeButton", UiText.Get("SpellCheck_ReplaceThisOccurrenceHelpText"), (_, _) => Accept(CreateReplaceResult(word, _replacementBox.Text))));
        actionButtons.Children.Add(CreateSpellingButton(_changeAllButton, "SpellCheckChangeAllButton", UiText.Get("SpellCheck_ReplaceAllOccurrencesHelpText"), (_, _) => Accept(CreateReplaceAllResult(word, _replacementBox.Text))));
        actionButtons.Children.Add(CreateSpellingButton(new Button { Content = UiText.Get("SpellCheck_AddToDictionary"), Width = 118 }, "SpellCheckAddToDictionaryButton", UiText.Get("SpellCheck_AddTheWordToTheCustomDictionaryHelpText"), (_, _) => Accept(CreateAddResult(word))));
        var cancelButton = new Button { Content = UiText.Get("SpellCheck_Cancel"), Width = 90, IsCancel = true, Margin = new Thickness(0, 8, 0, 0) };
        AutomationProperties.SetName(cancelButton, UiText.Get("SpellCheck_CancelSpelling"));
        AutomationProperties.SetAutomationId(cancelButton, "SpellCheckCancelButton");
        AutomationProperties.SetHelpText(cancelButton, UiText.Get("SpellCheck_CloseTheSpellingDialogWithoutApplyingASpellingAction"));
        actionButtons.Children.Add(cancelButton);
        Grid.SetColumn(actionButtons, 1);
        root.Children.Add(actionButtons);
        return root;
    }

    private static Button CreateSpellingButton(Button button, string automationId, string helpText, RoutedEventHandler click)
    {
        button.Margin = new Thickness(0, 0, 0, 6);
        AutomationProperties.SetName(button, button.Content.ToString()?.Replace("_", string.Empty) ?? string.Empty);
        AutomationProperties.SetAutomationId(button, automationId);
        AutomationProperties.SetHelpText(button, helpText);
        button.Click += click;
        return button;
    }
}
