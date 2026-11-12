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

        var options = _optionsRuntimeSession.Reload();
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
            () => _optionsRuntimeSession.MutateFresh(fresh =>
            {
                // MutateFresh hands us a snapshot it just reloaded from disk (so it can carry
                // words another FreeX process persisted since this window's own snapshot was
                // taken -- FreeX has no single-instance guard, so two windows over the same
                // options store is ordinary). Union that fresh list with this window's in-memory
                // words instead of overwriting it wholesale with the (possibly stale) in-memory
                // list, or the other process's newly-added word gets silently dropped from disk.
                // AppOptions.Normalize() (run right after this mutation) dedupes and sorts.
                fresh.SpellCheckCustomDictionaryWords = fresh.SpellCheckCustomDictionaryWords
                    .Concat(options.SpellCheckCustomDictionaryWords)
                    .ToList();
            })));
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
            // WPF SpellCheckDialog is a 480x330 *outer* NoResize window. Avalonia sizes the client
            // area, so take the Windows caption/frame off the height.
            Width = 480,
            Height = 330 - WpfNoResizeFrameHeight,
            MinWidth = 480,
            MinHeight = 330 - WpfNoResizeFrameHeight,
            MaxWidth = 480,
            MaxHeight = 330 - WpfNoResizeFrameHeight,
            CanResize = false,
            FontFamily = FormulaBarFontFamily,
            FontSize = 12,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };
        AutomationProperties.SetAutomationId(dialog, "SpellCheckDialog");

        var decision = new SpellCheckSessionDecision(SpellCheckSessionAction.Stop);

        // WPF: a read-only, wrapping 56px box showing the word that is not in the dictionary. The
        // cell the word lives in is selected in the grid behind the dialog, as in WPF.
        var notInDictionaryBox = new TextBox
        {
            Text = issue.Word,
            IsReadOnly = true,
            Height = 56,
            TextWrapping = TextWrapping.Wrap,
            AcceptsReturn = false,
        };
        AutomationProperties.SetName(notInDictionaryBox, UiText.Get("SpellCheck_NotInDictionary2"));
        AutomationProperties.SetAutomationId(notInDictionaryBox, "SpellCheckNotInDictionaryBox");
        AutomationProperties.SetHelpText(
            notInDictionaryBox,
            UiText.Get("SpellCheck_ShowsTheWordThatWasNotFoundInTheDictionary"));
        ToolTip.SetTip(notInDictionaryBox, $"{issue.SheetName}!{issue.CellReference}:  {issue.ContextText}");
        AvaloniaCompactDialogChrome.ApplyTextBox(notInDictionaryBox, SpellingDialogChromeStyle, fixedHeight: false);
        notInDictionaryBox.VerticalContentAlignment = VerticalAlignment.Top;

        var suggestions = string.IsNullOrWhiteSpace(issue.Suggestion)
            ? Array.Empty<string>()
            : new[] { issue.Suggestion };
        var suggestionList = new ListBox
        {
            // WPF: _suggestionsBox.Height = 76; an issue without a suggestion leaves the list empty.
            Height = 76,
            Focusable = true,
        };
        KeyboardNavigation.SetIsTabStop(suggestionList, true);
        AutomationProperties.SetName(suggestionList, UiText.Get("SpellCheck_Suggestions2"));
        AutomationProperties.SetAutomationId(suggestionList, "SpellCheckSuggestionsList");
        AutomationProperties.SetHelpText(suggestionList, UiText.Get("SpellCheck_ChooseASuggestedSpellingReplacement"));
        AvaloniaCompactDialogChrome.ApplyListBox(suggestionList, SpellingDialogChromeStyle);
        foreach (var suggestion in suggestions)
            suggestionList.Items.Add(suggestion);
        if (suggestions.Length > 0)
            suggestionList.SelectedIndex = 0;

        var replacementBox = new TextBox
        {
            Text = suggestions.Length > 0 ? suggestions[0] : issue.Word,
        };
        AutomationProperties.SetName(replacementBox, UiText.Get("SpellCheck_ChangeTo2"));
        AutomationProperties.SetAutomationId(replacementBox, "SpellCheckReplacementBox");
        AutomationProperties.SetHelpText(
            replacementBox,
            UiText.Get("SpellCheck_EnterTheReplacementTextForTheMisspelledWord"));
        AvaloniaCompactDialogChrome.ApplyTextBox(replacementBox, SpellingDialogChromeStyle);

        suggestionList.SelectionChanged += (_, _) =>
        {
            if (suggestionList.SelectedItem is string picked)
                replacementBox.Text = picked;
        };

        // WPF captions (and their access keys). The default button keeps a plain-string caption.
        var ignoreButton = new Button { Content = UiText.Get("SpellCheck_IgnoreOnce") };
        var ignoreAllButton = new Button { Content = UiText.Get("SpellCheck_IgnoreAll") };
        var changeButton = new Button { Content = UiText.Get("ShellLoc_SpellingChange"), IsDefault = true };
        var changeAllButton = new Button { Content = UiText.Get("SpellCheck_ChangeAll") };
        var addButton = new Button { Content = UiText.Get("SpellCheck_AddToDictionary") };
        var closeButton = new Button { Content = UiText.Get("SpellCheck_Cancel"), IsCancel = true };
        // WPF button widths: Ignore Once / Add to Dictionary 118, the rest 90.
        AvaloniaCompactDialogChrome.ApplyButton(ignoreButton, SpellingDialogChromeStyle, 118);
        AvaloniaCompactDialogChrome.ApplyButton(ignoreAllButton, SpellingDialogChromeStyle, 90);
        AvaloniaCompactDialogChrome.ApplyButton(changeButton, SpellingDialogChromeStyle, 90, isDefault: true);
        AvaloniaCompactDialogChrome.ApplyButton(changeAllButton, SpellingDialogChromeStyle, 90);
        AvaloniaCompactDialogChrome.ApplyButton(addButton, SpellingDialogChromeStyle, 118);
        AvaloniaCompactDialogChrome.ApplyButton(closeButton, SpellingDialogChromeStyle, 90);
        ignoreButton.Width = 118;
        ignoreAllButton.Width = 90;
        changeButton.Width = 90;
        changeAllButton.Width = 90;
        addButton.Width = 118;
        closeButton.Width = 90;
        AutomationProperties.SetAutomationId(ignoreButton, "SpellCheckIgnoreOnceButton");
        AutomationProperties.SetAutomationId(ignoreAllButton, "SpellCheckIgnoreAllButton");
        AutomationProperties.SetAutomationId(changeButton, "SpellCheckChangeButton");
        AutomationProperties.SetAutomationId(changeAllButton, "SpellCheckChangeAllButton");
        AutomationProperties.SetAutomationId(addButton, "SpellCheckAddToDictionaryButton");
        AutomationProperties.SetAutomationId(closeButton, "SpellCheckCancelButton");
        AutomationProperties.SetName(closeButton, UiText.Get("SpellCheck_CancelSpelling"));
        AutomationProperties.SetHelpText(ignoreButton, UiText.Get("SpellCheck_IgnoreThisOccurrenceHelpText"));
        AutomationProperties.SetHelpText(ignoreAllButton, UiText.Get("SpellCheck_IgnoreAllOccurrencesHelpText"));
        AutomationProperties.SetHelpText(changeButton, UiText.Get("SpellCheck_ReplaceThisOccurrenceHelpText"));
        AutomationProperties.SetHelpText(changeAllButton, UiText.Get("SpellCheck_ReplaceAllOccurrencesHelpText"));
        AutomationProperties.SetHelpText(addButton, UiText.Get("SpellCheck_AddTheWordToTheCustomDictionaryHelpText"));
        AutomationProperties.SetHelpText(
            closeButton,
            UiText.Get("SpellCheck_CloseTheSpellingDialogWithoutApplyingASpellingAction"));

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

        // WPF: Change / Change All are only available while there is replacement text.
        void RefreshChangeButtonState()
        {
            var hasReplacement = !string.IsNullOrWhiteSpace(replacementBox.Text);
            changeButton.IsEnabled = hasReplacement;
            changeAllButton.IsEnabled = hasReplacement;
        }

        replacementBox.TextChanged += (_, _) => RefreshChangeButtonState();
        RefreshChangeButtonState();

        // WPF SpellCheckDialog.CreateSpellCheckContent: Grid { Margin = 16 } with a star field column
        // (12px right gutter) and a 124px action column. Labels sit 4px above their control and 10px
        // below the previous one; the action buttons are stacked 6px apart with Cancel 8px lower.
        static Label CreateSpellingFieldLabel(string resourceKey, Control target, double topMargin) =>
            new()
            {
                Content = new AccessText { Text = UiText.Get(resourceKey) },
                Target = target,
                Padding = new Thickness(0),
                Margin = new Thickness(0, topMargin, 0, 4),
            };

        var fields = new StackPanel
        {
            Margin = new Thickness(0, 0, 12, 0),
            Children =
            {
                CreateSpellingFieldLabel("SpellCheck_NotInDictionary", notInDictionaryBox, 0),
                notInDictionaryBox,
                CreateSpellingFieldLabel("SpellCheck_Suggestions", suggestionList, 10),
                suggestionList,
                CreateSpellingFieldLabel("SpellCheck_ChangeTo", replacementBox, 10),
                replacementBox,
            },
        };

        var actionButtons = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        foreach (var button in new[] { ignoreButton, ignoreAllButton, changeButton, changeAllButton, addButton })
        {
            button.Margin = new Thickness(0, 0, 0, 6);
            button.HorizontalAlignment = HorizontalAlignment.Center;
            actionButtons.Children.Add(button);
        }

        closeButton.Margin = new Thickness(0, 8, 0, 0);
        closeButton.HorizontalAlignment = HorizontalAlignment.Center;
        actionButtons.Children.Add(closeButton);

        var layout = new Grid
        {
            Margin = new Thickness(16),
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = new GridLength(124) },
            },
            Children =
            {
                fields,
                actionButtons,
            },
        };
        Grid.SetColumn(actionButtons, 1);

        dialog.Content = layout;
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
