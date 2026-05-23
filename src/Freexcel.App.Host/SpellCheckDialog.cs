using System.Globalization;
using System.Windows;
using System.Windows.Controls;

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
        _replacementBox.Text = suggestion;
        if (!string.IsNullOrWhiteSpace(suggestion))
        {
            _suggestionsBox.Items.Add(suggestion);
            _suggestionsBox.SelectedIndex = 0;
        }

        _suggestionsBox.Height = 76;
        _suggestionsBox.SelectionChanged += (_, _) =>
        {
            if (_suggestionsBox.SelectedItem is string selected)
                _replacementBox.Text = selected;
        };

        Content = CreateSpellCheckContent(word);
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

    private UIElement CreateSpellCheckContent(string word)
    {
        var root = new Grid { Margin = new Thickness(16) };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });
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
        actionButtons.Children.Add(CreateSpellingButton(new Button { Content = "_Ignore", Width = 90 }, (_, _) => Accept(CreateIgnoreResult())));
        actionButtons.Children.Add(CreateSpellingButton(new Button { Content = "Ignore _All", Width = 90 }, (_, _) => Accept(CreateIgnoreAllResult())));
        actionButtons.Children.Add(CreateSpellingButton(new Button { Content = "_Change", Width = 90 }, (_, _) => Accept(CreateReplaceResult(word, _replacementBox.Text))));
        actionButtons.Children.Add(CreateSpellingButton(new Button { Content = "Change A_ll", Width = 90 }, (_, _) => Accept(CreateReplaceAllResult(word, _replacementBox.Text))));
        actionButtons.Children.Add(CreateSpellingButton(new Button { Content = "_Add", Width = 90 }, (_, _) => Accept(CreateAddResult(word))));
        actionButtons.Children.Add(new Button { Content = "_Cancel", Width = 90, IsCancel = true, Margin = new Thickness(0, 8, 0, 0) });
        Grid.SetColumn(actionButtons, 1);
        root.Children.Add(actionButtons);
        return root;
    }

    private static Button CreateSpellingButton(Button button, RoutedEventHandler click)
    {
        button.Margin = new Thickness(0, 0, 0, 6);
        button.Click += click;
        return button;
    }
}
