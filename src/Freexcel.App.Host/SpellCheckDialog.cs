using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;

namespace Freexcel.App.Host;

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
    private readonly Button _changeButton = new() { Content = "_Change", Width = 90, IsDefault = true };
    private readonly Button _changeAllButton = new() { Content = "Change A_ll", Width = 90 };

    public SpellCheckDialogResult Result { get; private set; }

    public SpellCheckDialog(string word, string suggestion)
    {
        Result = CreateReplaceResult(word, suggestion);
        Title = "Spelling";
        Width = 480;
        Height = 330;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        _notInDictionaryBox.Text = word;
        _notInDictionaryBox.IsReadOnly = true;
        _notInDictionaryBox.Height = 56;
        _notInDictionaryBox.TextWrapping = TextWrapping.Wrap;
        AutomationProperties.SetName(_notInDictionaryBox, "Not in Dictionary");
        AutomationProperties.SetAutomationId(_notInDictionaryBox, "SpellCheckNotInDictionaryBox");
        AutomationProperties.SetHelpText(_notInDictionaryBox, "Shows the word that was not found in the dictionary.");
        _replacementBox.Text = suggestion;
        _replacementBox.TextChanged += (_, _) => RefreshChangeButtonState();
        AutomationProperties.SetName(_replacementBox, "Change to");
        AutomationProperties.SetAutomationId(_replacementBox, "SpellCheckReplacementBox");
        AutomationProperties.SetHelpText(_replacementBox, "Enter the replacement text for the misspelled word.");
        if (!string.IsNullOrWhiteSpace(suggestion))
        {
            _suggestionsBox.Items.Add(suggestion);
            _suggestionsBox.SelectedIndex = 0;
        }

        _suggestionsBox.Height = 76;
        AutomationProperties.SetName(_suggestionsBox, "Suggestions");
        AutomationProperties.SetAutomationId(_suggestionsBox, "SpellCheckSuggestionsList");
        AutomationProperties.SetHelpText(_suggestionsBox, "Choose a suggested spelling replacement.");
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

        _replacementBox.Focus();
        _replacementBox.SelectAll();
        Keyboard.Focus(_replacementBox);
    }

    private UIElement CreateSpellCheckContent(string word)
    {
        var root = new Grid { Margin = new Thickness(16) };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(124) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var fields = new StackPanel { Margin = new Thickness(0, 0, 12, 0) };
        fields.Children.Add(new Label { Content = "Not in _Dictionary:", Target = _notInDictionaryBox, Padding = new Thickness(0), Margin = new Thickness(0, 0, 0, 4) });
        fields.Children.Add(_notInDictionaryBox);
        fields.Children.Add(new Label { Content = "_Suggestions:", Target = _suggestionsBox, Padding = new Thickness(0), Margin = new Thickness(0, 10, 0, 4) });
        fields.Children.Add(_suggestionsBox);
        fields.Children.Add(new Label { Content = "_Change to:", Target = _replacementBox, Padding = new Thickness(0), Margin = new Thickness(0, 10, 0, 4) });
        fields.Children.Add(_replacementBox);
        root.Children.Add(fields);

        var actionButtons = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right };
        actionButtons.Children.Add(CreateSpellingButton(new Button { Content = "Ignore _Once", Width = 118 }, "SpellCheckIgnoreOnceButton", "Ignore this occurrence.", (_, _) => Accept(CreateIgnoreResult())));
        actionButtons.Children.Add(CreateSpellingButton(new Button { Content = "Ignore _All", Width = 90 }, "SpellCheckIgnoreAllButton", "Ignore all occurrences.", (_, _) => Accept(CreateIgnoreAllResult())));
        actionButtons.Children.Add(CreateSpellingButton(_changeButton, "SpellCheckChangeButton", "Replace this occurrence.", (_, _) => Accept(CreateReplaceResult(word, _replacementBox.Text))));
        actionButtons.Children.Add(CreateSpellingButton(_changeAllButton, "SpellCheckChangeAllButton", "Replace all occurrences.", (_, _) => Accept(CreateReplaceAllResult(word, _replacementBox.Text))));
        actionButtons.Children.Add(CreateSpellingButton(new Button { Content = "Add to _Dictionary", Width = 118 }, "SpellCheckAddToDictionaryButton", "Add the word to the custom dictionary.", (_, _) => Accept(CreateAddResult(word))));
        var cancelButton = new Button { Content = "Ca_ncel", Width = 90, IsCancel = true, Margin = new Thickness(0, 8, 0, 0) };
        AutomationProperties.SetName(cancelButton, "Cancel spelling");
        AutomationProperties.SetAutomationId(cancelButton, "SpellCheckCancelButton");
        AutomationProperties.SetHelpText(cancelButton, "Close the spelling dialog without applying a spelling action.");
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
