using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using FreeX.App.Services;
using FreeX.Core.Model;
using AvaloniaProofingHorizontalAlignment = Avalonia.Layout.HorizontalAlignment;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    // Review ▸ Proofing (Thesaurus / Translate) and Insert ▸ Equation / Object.
    // Honest scope: the thesaurus uses a small built-in synonym map (see ThesaurusData),
    // translation is offline-unavailable (no network/service in this build), equation is
    // inserted as plain cell text (no true equation object), and object embedding is unsupported.

    private static string? FirstAlphabeticWord(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var builder = new StringBuilder();
        foreach (var ch in text)
        {
            if (char.IsLetter(ch))
                builder.Append(ch);
            else if (builder.Length > 0)
                break;
        }

        return builder.Length > 0 ? builder.ToString() : null;
    }

    private bool CommitProofingText(string text, string successStatus)
    {
        // The proofing dialogs await before reaching here; an open/save may have started in the
        // meantime. Don't mutate the workbook mid-operation (parity with the chart/pivot handlers).
        if (_isOpening || _isSaving)
            return false;

        var address = _session.ActiveCell;
        _session.SelectCell(address);
        var result = _session.CommitCellText(text);
        RefreshShell(result.Success
            ? successStatus
            : result.ErrorMessage ?? UiText.Get("ShellLoc_CouldNotUpdateCell"));
        return result.Success;
    }

    /// <summary>Review ▸ Thesaurus — look up synonyms for the active cell's first word.</summary>
    private async Task ShowThesaurusDialogAsync()
    {
        var address = _session.ActiveCell;
        var cellText = FormatEditText(_session.ActiveSheet.GetCell(address), address);
        var word = FirstAlphabeticWord(cellText);

        if (word is null)
        {
            RefreshShell(UiText.Get("ShellLoc_ThesaurusSelectWord"));
            return;
        }

        var synonyms = ThesaurusData.Lookup(word);

        var layout = new StackPanel { Margin = new Thickness(16), Spacing = 8 };
        layout.Children.Add(new TextBlock { Text = UiText.Format("ShellLoc_ThesaurusLookedUp", word), FontWeight = FontWeight.SemiBold });

        var list = new ListBox
        {
            Height = 150,
            ItemsSource = synonyms,
        };
        if (synonyms.Count == 0)
            layout.Children.Add(new TextBlock { Text = UiText.Get("ShellLoc_ThesaurusNoSynonyms"), TextWrapping = TextWrapping.Wrap });
        else
            layout.Children.Add(list);

        var dialog = new Window
        {
            Title = UiText.Get("ShellLoc_ThesaurusTitle"),
            Width = 320,
            Height = 280,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = AvaloniaProofingHorizontalAlignment.Right,
        };
        var replace = new Button { Content = UiText.Get("ShellLoc_ReplaceButton"), IsEnabled = synonyms.Count > 0, IsDefault = true };
        var close = new Button { Content = UiText.Get("Common_Close"), IsCancel = true };
        ApplyDialogButtonChrome(replace, 90, isDefault: true);
        ApplyDialogButtonChrome(close, 90);
        replace.Click += (_, _) =>
        {
            var chosen = list.SelectedItem as string ?? (synonyms.Count > 0 ? synonyms[0] : null);
            if (chosen is not null && cellText is not null)
            {
                var index = cellText.IndexOf(word, System.StringComparison.OrdinalIgnoreCase);
                var updated = index >= 0
                    ? cellText.Remove(index, word.Length).Insert(index, chosen)
                    : chosen;
                CommitProofingText(updated, UiText.Format("ShellLoc_ThesaurusReplaced", word, chosen));
            }
            dialog.Close();
        };
        close.Click += (_, _) => dialog.Close();
        buttons.Children.Add(replace);
        buttons.Children.Add(close);
        layout.Children.Add(buttons);

        dialog.Content = layout;
        await dialog.ShowDialog(this);
    }

    /// <summary>
    /// Review ▸ Translate — honest manual-translation helper. There is no offline translation engine in
    /// this build, so instead of faking an auto-translator this surfaces the selected source text, From/To
    /// language pickers, and a manual translation entry that writes back to a chosen target cell/range. All
    /// option/validation/write-planning logic lives in the portable <see cref="TranslateDialogPlanner"/> so
    /// macOS inherits it; this method is only the Avalonia chrome + commit glue.
    /// </summary>
    private async Task ShowTranslateDialogAsync()
    {
        var source = _session.ActiveCell;
        var cellText = FormatEditText(_session.ActiveSheet.GetCell(source), source);

        var layout = new StackPanel { Margin = new Thickness(16), Spacing = 10, Width = 380 };
        layout.Children.Add(new TextBlock { Text = UiText.Get("WfTranslate_Title"), FontWeight = FontWeight.SemiBold });

        // From / To language pickers (the planner owns the language list; we resolve display labels).
        var languages = TranslateDialogPlanner.Languages;
        var fromBox = CreateTranslateLanguageBox("WfTranslateFromLanguage", languages, TranslateDialogPlanner.DefaultFromCode);
        var toBox = CreateTranslateLanguageBox("WfTranslateToLanguage", languages, TranslateDialogPlanner.DefaultToCode);
        var languagesRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        languagesRow.Children.Add(new StackPanel
        {
            Spacing = 2,
            Children = { new TextBlock { Text = UiText.Get("WfTranslate_FromLabel") }, fromBox },
        });
        languagesRow.Children.Add(new StackPanel
        {
            Spacing = 2,
            Children = { new TextBlock { Text = UiText.Get("WfTranslate_ToLabel") }, toBox },
        });
        layout.Children.Add(languagesRow);

        layout.Children.Add(new TextBlock { Text = UiText.Get("WfTranslate_SourceLabel") });
        layout.Children.Add(new SelectableTextBlock
        {
            Text = string.IsNullOrEmpty(cellText) ? UiText.Get("WfTranslate_EmptyCell") : cellText,
            TextWrapping = TextWrapping.Wrap,
        });

        layout.Children.Add(new TextBlock { Text = UiText.Get("WfTranslate_TranslationLabel") });
        var translationBox = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            Height = 70,
            PlaceholderText = UiText.Get("WfTranslate_TranslationWatermark"),
        };
        AutomationProperties.SetAutomationId(translationBox, "WfTranslateTranslationBox");
        AutomationProperties.SetName(translationBox, UiText.Get("WfTranslate_TranslationLabel"));
        layout.Children.Add(translationBox);

        layout.Children.Add(new TextBlock { Text = UiText.Get("WfTranslate_TargetLabel") });
        var targetBox = new TextBox { Text = TranslateDialogPlanner.SuggestTargetReference(source) };
        AutomationProperties.SetAutomationId(targetBox, "WfTranslateTargetBox");
        AutomationProperties.SetName(targetBox, UiText.Get("WfTranslate_TargetLabel"));
        layout.Children.Add(targetBox);

        layout.Children.Add(new TextBlock
        {
            Text = UiText.Get("WfTranslate_ManualNote"),
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brush(120, 120, 120),
        });

        var dialog = new Window
        {
            Title = UiText.Get("WfTranslate_Title"),
            Width = 420,
            Height = 380,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };
        AutomationProperties.SetAutomationId(dialog, "WfTranslateDialog");

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = AvaloniaProofingHorizontalAlignment.Right,
        };
        var insert = new Button { Content = UiText.Get("WfTranslate_InsertButton"), IsDefault = true };
        var close = new Button { Content = UiText.Get("WfTranslate_CloseButton"), IsCancel = true };
        ApplyDialogButtonChrome(insert, 110, isDefault: true);
        ApplyDialogButtonChrome(close, 90);
        AutomationProperties.SetAutomationId(insert, "WfTranslateInsertButton");
        AutomationProperties.SetAutomationId(close, "WfTranslateCloseButton");
        insert.Click += (_, _) =>
        {
            var fromCode = (fromBox.SelectedItem as TranslateLanguageItem)?.Code ?? TranslateDialogPlanner.DefaultFromCode;
            var toCode = (toBox.SelectedItem as TranslateLanguageItem)?.Code ?? TranslateDialogPlanner.DefaultToCode;
            if (CommitManualTranslation(source, translationBox.Text, targetBox.Text, fromCode, toCode))
                dialog.Close();
        };
        close.Click += (_, _) => dialog.Close();
        buttons.Children.Add(insert);
        buttons.Children.Add(close);
        layout.Children.Add(buttons);

        dialog.Content = layout;
        await dialog.ShowDialog(this);
    }

    private static ComboBox CreateTranslateLanguageBox(
        string automationId,
        IReadOnlyList<TranslateLanguageOption> options,
        string defaultCode)
    {
        var items = options
            .Select(o => new TranslateLanguageItem(o.Code, UiText.Get(o.DisplayKey)))
            .ToList();
        var box = new ComboBox { ItemsSource = items, Width = 150 };
        box.SelectedItem = items.FirstOrDefault(i => i.Code == defaultCode) ?? items.FirstOrDefault();
        AutomationProperties.SetAutomationId(box, automationId);
        return box;
    }

    private sealed record TranslateLanguageItem(string Code, string Label)
    {
        public override string ToString() => Label;
    }

    /// <summary>
    /// Validates the manual translation via the portable planner and commits the resulting cell writes
    /// through the normal session edit path (undo/redo + protection apply). Returns true when the write
    /// succeeded so the caller can close the dialog; surfaces a localized status otherwise.
    /// </summary>
    private bool CommitManualTranslation(
        CellAddress source,
        string? translation,
        string? targetReference,
        string fromCode,
        string toCode)
    {
        if (_isOpening || _isSaving)
            return false;

        if (!TranslateDialogPlanner.TryPlan(
                _session.ActiveSheet.Id, source, translation, targetReference, fromCode, toCode,
                out var plan, out var error))
        {
            RefreshShell(error switch
            {
                TranslateDialogValidationError.EmptyTranslation => UiText.Get("WfTranslate_ErrorEmptyTranslation"),
                TranslateDialogValidationError.MissingTargetReference => UiText.Get("WfTranslate_ErrorMissingTarget"),
                TranslateDialogValidationError.InvalidTargetReference => UiText.Get("WfTranslate_ErrorInvalidTarget"),
                TranslateDialogValidationError.SameSourceAndTarget => UiText.Get("WfTranslate_ErrorSameTarget"),
                _ => UiText.Get("WfTranslate_ErrorGeneric"),
            });
            return false;
        }

        foreach (var write in plan.Writes)
        {
            _session.SelectCell(write.Address);
            var result = _session.CommitCellText(write.Text);
            if (!result.Success)
            {
                RefreshShell(result.ErrorMessage ?? UiText.Get("WfTranslate_ErrorGeneric"));
                return false;
            }
        }

        _session.SelectCell(plan.TargetRange.Start);
        RefreshShell(UiText.Format("WfTranslate_StatusInserted", plan.TargetRange.ToString()));
        return true;
    }

    /// <summary>Insert ▸ Equation — type an equation; it is inserted into the active cell as text.</summary>
    private async Task ShowEquationDialogAsync()
    {
        var address = _session.ActiveCell;
        var current = FormatEditText(_session.ActiveSheet.GetCell(address), address);

        var input = new TextBox
        {
            Text = current,
            Width = 360,
            AcceptsReturn = false,
        };

        var symbols = new WrapPanel { MaxWidth = 360 };
        foreach (var symbol in new[] { "±", "×", "÷", "≤", "≥", "≠", "√", "π", "∑", "∞", "→", "²", "³" })
        {
            var btn = new Button { Content = symbol, Width = 40, Margin = new Thickness(2) };
            btn.Click += (_, _) =>
            {
                input.Text += symbol;
                input.CaretIndex = input.Text?.Length ?? 0;
            };
            symbols.Children.Add(btn);
        }

        var layout = new StackPanel { Margin = new Thickness(16), Spacing = 10 };
        layout.Children.Add(new TextBlock { Text = UiText.Get("ShellLoc_EquationLabel"), FontWeight = FontWeight.SemiBold });
        layout.Children.Add(input);
        layout.Children.Add(symbols);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = AvaloniaProofingHorizontalAlignment.Right,
        };
        var ok = new Button { Content = UiText.Get("ShellLoc_InsertButton"), IsDefault = true };
        var cancel = new Button { Content = UiText.Get("Common_Cancel"), IsCancel = true };
        ApplyDialogButtonChrome(ok, 90, isDefault: true);
        ApplyDialogButtonChrome(cancel, 90);
        // Captured below once the dialog exists. layout is the Window's Content, so the previous
        // layout.Parent.Parent walked one level too far (Parent is the Window, Parent.Parent null).
        Window? dialog = null;
        ok.Click += (_, _) =>
        {
            var text = input.Text ?? string.Empty;
            CommitProofingText(text, UiText.Get("ShellLoc_InsertedEquation"));
            input.Tag = "ok";
            dialog?.Close();
        };
        cancel.Click += (_, _) => dialog?.Close();
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        layout.Children.Add(buttons);

        dialog = new Window
        {
            Title = UiText.Get("ShellLoc_InsertEquationTitle"),
            Width = 410,
            Height = 240,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
            Content = layout,
        };
        await dialog.ShowDialog(this);
    }

}
