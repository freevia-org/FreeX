using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Free.Shared.Shell.Avalonia;
using FreeX.Core.Model;
using AvaloniaHorizontalAlignment = Avalonia.Layout.HorizontalAlignment;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    private static AvaloniaCompactDialogChromeStyle SpellingDialogChromeStyle => new(FormulaBarFontFamily);

    // Review ▸ Spelling (parity gap: the ribbon button was a no-op). Scans the text content of the
    // active sheet — the current selection when it spans more than one cell, otherwise the used range —
    // tokenizes each text-bearing cell into words and flags any not present in the built-in word list
    // (see SpellingWordList). For each misspelled word a modal dialog offers the word in context
    // (cell reference + cell text), naive suggestions, and Ignore / Ignore All / Change / Change All /
    // Close. "Change" rewrites the offending cell's text through the same commit path the formula bar
    // uses (SelectCell + CommitCellText).
    //
    // This relies on the lightweight, self-contained checker in SpellingWordList — there is no real
    // spell engine in this repository.

    private sealed record SpellingFinding(CellAddress Address, string CellText, string Word, int Start, int Length);

    private async Task ShowSpellingDialogAsync()
    {
        var sheet = _session.ActiveSheet;
        var selection = _session.SelectedRange;
        var scanWholeUsedRange = selection.CellCount <= 1;

        // Collect text-bearing cells in scan order (row-major).
        var findings = CollectSpellingFindings(sheet, selection, scanWholeUsedRange);

        if (findings.Count == 0)
        {
            await ShowSpellingMessageDialogAsync(
                UiText.Get("ShellLoc_SpellingTitle"),
                UiText.Get("ShellLoc_SpellingNoMisspellings"));
            RefreshShell(UiText.Get("ShellLoc_SpellingCompleteZero"));
            return;
        }

        var ignoreAll = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // Maps an original (lowercased) word to its agreed replacement for "Change All".
        var changeAll = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var corrections = 0;

        for (var index = 0; index < findings.Count; index++)
        {
            var finding = findings[index];

            // The cell text may have been edited by an earlier correction in the same cell; re-read it
            // and re-locate the word so positions stay valid.
            var liveText = ReadCellText(finding.Address);
            var located = LocateWord(liveText, finding.Word);
            if (located is null)
                continue;

            var (start, length) = located.Value;
            var actualWord = liveText.Substring(start, length);

            if (ignoreAll.Contains(actualWord))
                continue;

            if (changeAll.TryGetValue(actualWord, out var queuedReplacement))
            {
                if (ApplySpellingCorrection(finding.Address, liveText, start, length, queuedReplacement))
                    corrections++;
                continue;
            }

            var decision = await PromptSpellingDecisionAsync(
                finding.Address, liveText, start, length, actualWord);

            switch (decision.Action)
            {
                case SpellingAction.Close:
                    RefreshShell(FormatSpellingSummary(corrections, completed: false));
                    return;

                case SpellingAction.Ignore:
                    break;

                case SpellingAction.IgnoreAll:
                    ignoreAll.Add(actualWord);
                    break;

                case SpellingAction.Change:
                    if (!string.IsNullOrEmpty(decision.Replacement) &&
                        ApplySpellingCorrection(finding.Address, liveText, start, length, decision.Replacement!))
                    {
                        corrections++;
                    }
                    break;

                case SpellingAction.ChangeAll:
                    if (!string.IsNullOrEmpty(decision.Replacement))
                    {
                        changeAll[actualWord] = decision.Replacement!;
                        if (ApplySpellingCorrection(finding.Address, liveText, start, length, decision.Replacement!))
                            corrections++;
                    }
                    break;
            }
        }

        await ShowSpellingMessageDialogAsync(
            UiText.Get("ShellLoc_SpellingTitle"),
            FormatSpellingSummary(corrections, completed: true));
        RefreshShell(FormatSpellingSummary(corrections, completed: true));
    }

    private List<SpellingFinding> CollectSpellingFindings(Sheet sheet, GridRange selection, bool scanWholeUsedRange)
    {
        var findings = new List<SpellingFinding>();

        foreach (var (address, cell) in sheet.EnumerateCells())
        {
            if (!scanWholeUsedRange && !selection.Contains(address))
                continue;

            // Only literal text cells are checked. Formulas, numbers, booleans, errors and blanks are skipped.
            if (cell.HasFormula)
                continue;
            if (cell.Value is not TextValue text || string.IsNullOrWhiteSpace(text.Value))
                continue;

            foreach (var (word, start, length) in TokenizeWords(text.Value))
            {
                if (!SpellingWordList.IsKnown(word))
                    findings.Add(new SpellingFinding(address, text.Value, word, start, length));
            }
        }

        // Deterministic order: top-to-bottom, left-to-right, then by position within the cell.
        findings.Sort(static (a, b) =>
        {
            var byRow = a.Address.Row.CompareTo(b.Address.Row);
            if (byRow != 0)
                return byRow;
            var byCol = a.Address.Col.CompareTo(b.Address.Col);
            if (byCol != 0)
                return byCol;
            return a.Start.CompareTo(b.Start);
        });

        return findings;
    }

    // Tokenize into words made of letters and intra-word apostrophes (e.g. "don't", "Customer's").
    private static IEnumerable<(string Word, int Start, int Length)> TokenizeWords(string text)
    {
        var i = 0;
        while (i < text.Length)
        {
            if (!char.IsLetter(text[i]))
            {
                i++;
                continue;
            }

            var start = i;
            while (i < text.Length &&
                   (char.IsLetter(text[i]) ||
                    (text[i] == '\'' && i + 1 < text.Length && char.IsLetter(text[i + 1]))))
            {
                i++;
            }

            yield return (text.Substring(start, i - start), start, i - start);
        }
    }

    private string ReadCellText(CellAddress address)
    {
        var cell = _session.ActiveSheet.GetCell(address);
        return FormatEditText(cell, address);
    }

    // Find the given word in the (possibly edited) cell text, preferring an exact match; fall back to a
    // case-insensitive match. Returns null when the word is no longer present.
    private static (int Start, int Length)? LocateWord(string text, string word)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(word))
            return null;

        foreach (var (token, start, length) in TokenizeWords(text))
        {
            if (string.Equals(token, word, StringComparison.Ordinal))
                return (start, length);
        }

        foreach (var (token, start, length) in TokenizeWords(text))
        {
            if (string.Equals(token, word, StringComparison.OrdinalIgnoreCase))
                return (start, length);
        }

        return null;
    }

    private bool ApplySpellingCorrection(CellAddress address, string currentText, int start, int length, string replacement)
    {
        var corrected = currentText[..start] + replacement + currentText[(start + length)..];

        // Commit through the same path the formula bar uses: select the cell, then commit its text.
        _session.SelectCell(address);
        var result = _session.CommitCellText(corrected);
        return result.Success;
    }

    private enum SpellingAction
    {
        Close,
        Ignore,
        IgnoreAll,
        Change,
        ChangeAll,
    }

    private sealed record SpellingDecision(SpellingAction Action, string? Replacement);

    private async Task<SpellingDecision> PromptSpellingDecisionAsync(
        CellAddress address, string cellText, int start, int length, string word)
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

        var decision = new SpellingDecision(SpellingAction.Close, null);

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

        // Show the word in context with the offending token wrapped in brackets.
        var context = cellText[..start] + "[" + cellText.Substring(start, length) + "]" + cellText[(start + length)..];
        layout.Children.Add(new SelectableTextBlock
        {
            Text = $"{_session.ActiveSheet.Name}!{FormatCellReference(address)}:  {context}",
            TextWrapping = TextWrapping.Wrap,
            FontFamily = new FontFamily("Consolas, Menlo, Monospace"),
        });

        layout.Children.Add(new TextBlock
        {
            Text = UiText.Get("ShellLoc_SpellingSuggestions"),
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(0, 6, 0, 0),
        });

        var suggestions = SpellingWordList.Suggest(word);
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
        if (suggestions.Count == 0)
            suggestionList.Items.Add(UiText.Get("ShellLoc_SpellingNoSuggestions"));
        else
            suggestionList.SelectedIndex = 0;
        layout.Children.Add(suggestionList);

        // Editable replacement box, prefilled with the top suggestion when available.
        var replacementBox = new TextBox
        {
            Text = suggestions.Count > 0 ? suggestions[0] : word,
        };
        AutomationProperties.SetAutomationId(replacementBox, "SpellCheckReplacementBox");
        AvaloniaCompactDialogChrome.ApplyTextBox(replacementBox, SpellingDialogChromeStyle);
        layout.Children.Add(new TextBlock { Text = UiText.Get("ShellLoc_SpellingChangeTo") });
        layout.Children.Add(replacementBox);

        suggestionList.SelectionChanged += (_, _) =>
        {
            if (suggestionList.SelectedItem is string picked && picked != UiText.Get("ShellLoc_SpellingNoSuggestions"))
                replacementBox.Text = picked;
        };

        var ignoreButton = new Button { Content = UiText.Get("ShellLoc_SpellingIgnore") };
        var ignoreAllButton = new Button { Content = UiText.Get("ShellLoc_SpellingIgnoreAll") };
        var changeButton = new Button { Content = UiText.Get("ShellLoc_SpellingChange"), IsDefault = true };
        var changeAllButton = new Button { Content = UiText.Get("ShellLoc_SpellingChangeAll") };
        var closeButton = new Button { Content = UiText.Get("Common_Close"), IsCancel = true };
        AutomationProperties.SetAutomationId(closeButton, "SpellCheckCancelButton");
        AvaloniaCompactDialogChrome.ApplyButton(ignoreButton, SpellingDialogChromeStyle, 96);
        AvaloniaCompactDialogChrome.ApplyButton(ignoreAllButton, SpellingDialogChromeStyle, 96);
        AvaloniaCompactDialogChrome.ApplyButton(changeButton, SpellingDialogChromeStyle, 96, isDefault: true);
        AvaloniaCompactDialogChrome.ApplyButton(changeAllButton, SpellingDialogChromeStyle, 96);
        AvaloniaCompactDialogChrome.ApplyButton(closeButton, SpellingDialogChromeStyle, 96);

        ignoreButton.Click += (_, _) => { decision = new SpellingDecision(SpellingAction.Ignore, null); dialog.Close(); };
        ignoreAllButton.Click += (_, _) => { decision = new SpellingDecision(SpellingAction.IgnoreAll, null); dialog.Close(); };
        changeButton.Click += (_, _) => { decision = new SpellingDecision(SpellingAction.Change, replacementBox.Text); dialog.Close(); };
        changeAllButton.Click += (_, _) => { decision = new SpellingDecision(SpellingAction.ChangeAll, replacementBox.Text); dialog.Close(); };
        closeButton.Click += (_, _) => { decision = new SpellingDecision(SpellingAction.Close, null); dialog.Close(); };

        var buttonRowTop = AvaloniaCompactDialogChrome.CreateActionRow(
            [ignoreButton, ignoreAllButton, changeButton],
            new Thickness(0, 10, 0, 0));
        var buttonRowBottom = AvaloniaCompactDialogChrome.CreateActionRow(
            [changeAllButton, closeButton],
            new Thickness(0, 6, 0, 0));

        layout.Children.Add(buttonRowTop);
        layout.Children.Add(buttonRowBottom);

        dialog.Content = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = layout,
        };
        var initialFocus = suggestions.Count > 0 ? (Control)suggestionList : replacementBox;
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
