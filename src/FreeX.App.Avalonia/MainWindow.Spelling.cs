using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Free.Shared.Shell.Avalonia;
using FreeX.App.Services;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    private static AvaloniaCompactDialogChromeStyle SpellingDialogChromeStyle => new(FormulaBarFontFamily);

    private async Task ShowSpellingDialogAsync()
    {
        if (_isOpening || _isSaving)
            return;

        var options = AppOptionsStore.Load();
        var controller = new SpellCheckSessionController(new SpellCheckSessionAdapter(
            () => _session.Workbook,
            () => _session.ActiveSheet.Id,
            () => options.SpellCheckCustomDictionaryWords,
            command =>
            {
                var result = _session.ExecuteReviewCommand(command);
                return new SpellCheckCommandExecutionResult(
                    result.Success,
                    result.ErrorMessage,
                    result.IsNoOp);
            },
            () => AppOptionsStore.Save(options)));
        var transition = controller.Start();

        if (transition.Status == SpellCheckSessionStatus.Complete)
        {
            await ShowSpellingMessageDialogAsync(
                UiText.Get("ShellLoc_SpellingTitle"),
                UiText.Get("ShellLoc_SpellingNoMisspellings"));
            RefreshShell(UiText.Get("ShellLoc_SpellingCompleteZero"));
            return;
        }

        while (transition.RequiresReview)
        {
            var issue = transition.Issue!;
            _session.SelectCell(issue.Address);
            RefreshShell(string.Empty);

            var decision = await PromptSpellingDecisionAsync(issue);
            transition = controller.Apply(decision);

            if (transition.Status == SpellCheckSessionStatus.Failed)
            {
                RefreshShell(transition.ErrorMessage ?? UiText.Get("ShellLoc_CouldNotUpdateCell"));
                return;
            }

            if (transition.Status == SpellCheckSessionStatus.Stopped)
            {
                RefreshShell(FormatSpellingSummary(transition.CorrectionsApplied, completed: false));
                return;
            }
        }

        var summary = FormatSpellingSummary(transition.CorrectionsApplied, completed: true);
        await ShowSpellingMessageDialogAsync(UiText.Get("ShellLoc_SpellingTitle"), summary);
        RefreshShell(summary);
    }

    private async Task<SpellCheckSessionDecision> PromptSpellingDecisionAsync(
        SpellCheckIssueDisplayModel issue)
    {
        var dialog = new Window
        {
            Title = UiText.Get("ShellLoc_SpellingTitle"),
            Width = 460,
            Height = 360,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };
        AutomationProperties.SetAutomationId(dialog, "SpellCheckDialog");

        var decision = new SpellCheckSessionDecision(SpellCheckSessionAction.Stop);
        var layout = new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 8,
        };

        layout.Children.Add(new TextBlock
        {
            Text = UiText.Get("ShellLoc_SpellingNotInDictionary"),
            FontWeight = FontWeight.SemiBold,
        });
        layout.Children.Add(new SelectableTextBlock
        {
            Text = $"{issue.SheetName}!{issue.CellReference}:  {issue.ContextText}",
            TextWrapping = TextWrapping.Wrap,
            FontFamily = new FontFamily("Consolas, Menlo, Monospace"),
        });
        layout.Children.Add(new TextBlock
        {
            Text = UiText.Get("ShellLoc_SpellingSuggestions"),
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(0, 6, 0, 0),
        });

        var suggestions = string.IsNullOrWhiteSpace(issue.Suggestion)
            ? Array.Empty<string>()
            : new[] { issue.Suggestion };
        var suggestionList = new ListBox
        {
            Height = 110,
            Focusable = true,
        };
        KeyboardNavigation.SetIsTabStop(suggestionList, true);
        AutomationProperties.SetAutomationId(suggestionList, "SpellCheckSuggestionsList");
        AvaloniaCompactDialogChrome.ApplyListBox(suggestionList, SpellingDialogChromeStyle);
        foreach (var suggestion in suggestions)
            suggestionList.Items.Add(suggestion);
        if (suggestions.Length == 0)
            suggestionList.Items.Add(UiText.Get("ShellLoc_SpellingNoSuggestions"));
        else
            suggestionList.SelectedIndex = 0;
        layout.Children.Add(suggestionList);

        var replacementBox = new TextBox
        {
            Text = suggestions.Length > 0 ? suggestions[0] : issue.Word,
        };
        AutomationProperties.SetAutomationId(replacementBox, "SpellCheckReplacementBox");
        AvaloniaCompactDialogChrome.ApplyTextBox(replacementBox, SpellingDialogChromeStyle);
        layout.Children.Add(new TextBlock { Text = UiText.Get("ShellLoc_SpellingChangeTo") });
        layout.Children.Add(replacementBox);

        suggestionList.SelectionChanged += (_, _) =>
        {
            if (suggestionList.SelectedItem is string picked &&
                picked != UiText.Get("ShellLoc_SpellingNoSuggestions"))
            {
                replacementBox.Text = picked;
            }
        };

        var ignoreButton = new Button { Content = UiText.Get("ShellLoc_SpellingIgnore") };
        var ignoreAllButton = new Button { Content = UiText.Get("ShellLoc_SpellingIgnoreAll") };
        var changeButton = new Button { Content = UiText.Get("ShellLoc_SpellingChange"), IsDefault = true };
        var changeAllButton = new Button { Content = UiText.Get("ShellLoc_SpellingChangeAll") };
        var addButton = new Button { Content = UiText.Get("SpellCheck_AddToDictionary") };
        var closeButton = new Button { Content = UiText.Get("Common_Close"), IsCancel = true };
        AutomationProperties.SetAutomationId(addButton, "SpellCheckAddToDictionaryButton");
        AutomationProperties.SetAutomationId(closeButton, "SpellCheckCancelButton");
        AvaloniaCompactDialogChrome.ApplyButton(ignoreButton, SpellingDialogChromeStyle, 96);
        AvaloniaCompactDialogChrome.ApplyButton(ignoreAllButton, SpellingDialogChromeStyle, 96);
        AvaloniaCompactDialogChrome.ApplyButton(changeButton, SpellingDialogChromeStyle, 96, isDefault: true);
        AvaloniaCompactDialogChrome.ApplyButton(changeAllButton, SpellingDialogChromeStyle, 96);
        AvaloniaCompactDialogChrome.ApplyButton(addButton, SpellingDialogChromeStyle, 118);
        AvaloniaCompactDialogChrome.ApplyButton(closeButton, SpellingDialogChromeStyle, 96);

        ignoreButton.Click += (_, _) =>
        {
            decision = new(SpellCheckSessionAction.IgnoreOnce);
            dialog.Close();
        };
        ignoreAllButton.Click += (_, _) =>
        {
            decision = new(SpellCheckSessionAction.IgnoreAll);
            dialog.Close();
        };
        changeButton.Click += (_, _) =>
        {
            decision = new(SpellCheckSessionAction.Change, replacementBox.Text);
            dialog.Close();
        };
        changeAllButton.Click += (_, _) =>
        {
            decision = new(SpellCheckSessionAction.ChangeAll, replacementBox.Text);
            dialog.Close();
        };
        addButton.Click += (_, _) =>
        {
            decision = new(SpellCheckSessionAction.AddToDictionary);
            dialog.Close();
        };
        closeButton.Click += (_, _) =>
        {
            decision = new(SpellCheckSessionAction.Stop);
            dialog.Close();
        };

        layout.Children.Add(AvaloniaCompactDialogChrome.CreateActionRow(
            [ignoreButton, ignoreAllButton, changeButton],
            new Thickness(0, 10, 0, 0)));
        layout.Children.Add(AvaloniaCompactDialogChrome.CreateActionRow(
            [changeAllButton, addButton, closeButton],
            new Thickness(0, 6, 0, 0)));

        dialog.Content = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = layout,
        };
        var initialFocus = suggestions.Length > 0 ? (Control)suggestionList : replacementBox;
        ConfigureNativeDialogInitialFocus(dialog, layout, initialFocus);
        ConfigureDeferredDialogCancel(dialog, closeButton);

        await dialog.ShowDialog(this);
        return decision;
    }

    private async Task ShowSpellingMessageDialogAsync(string title, string message)
    {
        var dialog = new Window
        {
            Title = title,
            Width = 380,
            Height = 160,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };

        var layout = new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 12,
        };
        layout.Children.Add(new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
        });

        var okButton = new Button
        {
            Content = UiText.Get("Common_Ok"),
            Width = 90,
        };
        AvaloniaCompactDialogChrome.ApplyButton(okButton, SpellingDialogChromeStyle, 90, isDefault: true);
        okButton.Click += (_, _) => dialog.Close();
        layout.Children.Add(AvaloniaCompactDialogChrome.CreateActionRow([okButton]));

        dialog.Content = layout;
        await dialog.ShowDialog(this);
    }

    private static string FormatSpellingSummary(int corrections, bool completed)
    {
        var noun = corrections == 1
            ? UiText.Get("ShellLoc_SpellingCorrectionSingular")
            : UiText.Get("ShellLoc_SpellingCorrectionPlural");
        return completed
            ? UiText.Format("ShellLoc_SpellingComplete", corrections, noun)
            : UiText.Format("ShellLoc_SpellingStopped", corrections, noun);
    }
}
