using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Free.Shared.Shell.Avalonia;
using FreeX.App.Services;
using FreeX.Core.Model;
using AvaloniaProofingHorizontalAlignment = Avalonia.Layout.HorizontalAlignment;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    private static AvaloniaCompactDialogChromeStyle ProofingDialogChromeStyle => new(FormulaBarFontFamily);

    // Review ▸ Proofing (Thesaurus / Translate) and Insert ▸ Equation.
    // The thesaurus and manual translation flows use shared planners. Equation input commits through
    // the normal cell-edit session; Insert Object is implemented separately in MainWindow.InsertObjects.

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
        if (!ThesaurusWorkflowPlanner.TryCreateLookup(cellText, out var lookup))
        {
            RefreshShell(UiText.Get("ShellLoc_ThesaurusSelectWord"));
            return;
        }

        var word = lookup.Word;
        var synonyms = lookup.Synonyms;

        var layout = new StackPanel { Margin = new Thickness(16), Spacing = 8 };
        layout.Children.Add(new TextBlock { Text = UiText.Format("ShellLoc_ThesaurusLookedUp", word), FontWeight = FontWeight.SemiBold });

        var list = new ListBox
        {
            Height = 150,
            ItemsSource = synonyms,
        };
        AvaloniaCompactDialogChrome.ApplyListBox(list, ProofingDialogChromeStyle);
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

        var replace = new Button { Content = UiText.Get("ShellLoc_ReplaceButton"), IsEnabled = synonyms.Count > 0, IsDefault = true };
        var close = new Button { Content = UiText.Get("Common_Close"), IsCancel = true };
        AvaloniaCompactDialogChrome.ApplyButton(replace, ProofingDialogChromeStyle, 90, isDefault: true);
        AvaloniaCompactDialogChrome.ApplyButton(close, ProofingDialogChromeStyle, 90);
        replace.Click += (_, _) =>
        {
            var chosen = list.SelectedItem as string ?? (synonyms.Count > 0 ? synonyms[0] : null);
            if (chosen is not null)
            {
                var updated = ThesaurusWorkflowPlanner.ApplyReplacement(lookup, chosen);
                CommitProofingText(updated, UiText.Format("ShellLoc_ThesaurusReplaced", word, chosen));
            }
            dialog.Close();
        };
        close.Click += (_, _) => dialog.Close();
        layout.Children.Add(AvaloniaCompactDialogChrome.CreateActionRow([replace, close]));

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

        // WPF TranslateDialog: a 440x470 *outer* resizable window (min 400x420). Avalonia sizes the
        // client area, so the Windows caption/frame comes off both dimensions.
        var dialog = new Window
        {
            Title = UiText.Get("WfTranslate_Title"),
            Width = 440 - WpfResizableFrameWidth,
            Height = 470 - WpfResizableFrameHeight,
            MinWidth = 400 - WpfResizableFrameWidth,
            MinHeight = 420 - WpfResizableFrameHeight,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };
        AvaloniaCompactDialogChrome.ApplyWindow(dialog, ProofingDialogChromeStyle);
        AutomationProperties.SetAutomationId(dialog, "WfTranslateDialog");

        // WPF AddLabeledControl: a SemiBold label 4px above its control.
        static Label CreateTranslateLabel(string resourceKey, Control target) =>
            new()
            {
                Content = UiText.Get(resourceKey),
                Target = target,
                Padding = new Thickness(0),
                FontWeight = FontWeight.SemiBold,
                Margin = new Thickness(0, 0, 0, 4),
            };

        // From / To language pickers (the planner owns the language list; we resolve display labels).
        var languages = TranslateDialogPlanner.Languages;
        var fromBox = CreateTranslateLanguageBox("WfTranslateFromLanguage", languages, TranslateDialogPlanner.DefaultFromCode);
        var toBox = CreateTranslateLanguageBox("WfTranslateToLanguage", languages, TranslateDialogPlanner.DefaultToCode);
        AvaloniaCompactDialogChrome.ApplyComboBox(fromBox, ProofingDialogChromeStyle);
        AvaloniaCompactDialogChrome.ApplyComboBox(toBox, ProofingDialogChromeStyle);
        AutomationProperties.SetName(fromBox, UiText.Get("WfTranslate_FromLabel"));
        AutomationProperties.SetName(toBox, UiText.Get("WfTranslate_ToLabel"));
        // WPF: Grid { Margin = (0, 0, 0, 10) } with * / 12 / * columns; each picker fills its column.
        var fromPanel = new StackPanel { Children = { CreateTranslateLabel("WfTranslate_FromLabel", fromBox), fromBox } };
        var toPanel = new StackPanel { Children = { CreateTranslateLabel("WfTranslate_ToLabel", toBox), toBox } };
        var languagesRow = new Grid
        {
            Margin = new Thickness(0, 0, 0, 10),
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = new GridLength(12) },
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
            },
            Children = { fromPanel, toPanel },
        };
        Grid.SetColumn(toPanel, 2);

        // WPF: a read-only wrapping box, 46..84px tall, 10px above the next label.
        var sourceBox = new TextBox
        {
            Text = string.IsNullOrEmpty(cellText) ? UiText.Get("WfTranslate_EmptyCell") : cellText,
            IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 46,
            MaxHeight = 84,
            Margin = new Thickness(0, 0, 0, 10),
        };
        AvaloniaCompactDialogChrome.ApplyTextBox(sourceBox, ProofingDialogChromeStyle, fixedHeight: false);
        sourceBox.VerticalContentAlignment = global::Avalonia.Layout.VerticalAlignment.Top;
        AutomationProperties.SetAutomationId(sourceBox, "WfTranslateSourceBox");
        AutomationProperties.SetName(sourceBox, UiText.Get("WfTranslate_SourceLabel"));

        var translationBox = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            // WPF: MinHeight = 70, Height = 82, Margin = (0, 0, 0, 10).
            MinHeight = 70,
            Height = 82,
            Margin = new Thickness(0, 0, 0, 10),
        };
        AvaloniaCompactDialogChrome.ApplyTextBox(translationBox, ProofingDialogChromeStyle, fixedHeight: false);
        translationBox.VerticalContentAlignment = global::Avalonia.Layout.VerticalAlignment.Top;
        AutomationProperties.SetAutomationId(translationBox, "WfTranslateTranslationBox");
        AutomationProperties.SetName(translationBox, UiText.Get("WfTranslate_TranslationLabel"));

        var targetBox = new TextBox { Text = TranslateDialogPlanner.SuggestTargetReference(source) };
        AvaloniaCompactDialogChrome.ApplyTextBox(targetBox, ProofingDialogChromeStyle);
        AutomationProperties.SetAutomationId(targetBox, "WfTranslateTargetBox");
        AutomationProperties.SetName(targetBox, UiText.Get("WfTranslate_TargetLabel"));

        // WPF: the grey manual-translation note leads the body, 12px above the language pickers.
        var body = new StackPanel
        {
            Children =
            {
                new TextBlock
                {
                    Text = UiText.Get("WfTranslate_ManualNote"),
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = Brush(109, 109, 109),
                    Margin = new Thickness(0, 0, 0, 12),
                },
                languagesRow,
                CreateTranslateLabel("WfTranslate_SourceLabel", sourceBox),
                sourceBox,
                CreateTranslateLabel("WfTranslate_TranslationLabel", translationBox),
                translationBox,
                CreateTranslateLabel("WfTranslate_TargetLabel", targetBox),
                targetBox,
            },
        };

        var insert = new Button { Content = UiText.Get("WfTranslate_InsertButton"), IsDefault = true };
        var close = new Button { Content = UiText.Get("WfTranslate_CloseButton"), IsCancel = true };
        // WPF: Insert MinWidth = 130, Close MinWidth = 90, 8px apart.
        AvaloniaCompactDialogChrome.ApplyButton(insert, ProofingDialogChromeStyle, 130, isDefault: true);
        AvaloniaCompactDialogChrome.ApplyButton(close, ProofingDialogChromeStyle, 90);
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

        // WPF TranslateDialog: DockPanel { Margin = 16 }; the action row is docked to the bottom with
        // a 12px top margin and is the first child, so it also leads the tab order as in WPF.
        var actions = AvaloniaCompactDialogChrome.CreateActionRow([insert, close]);
        actions.Margin = new Thickness(0, 12, 0, 0);
        DockPanel.SetDock(actions, Dock.Bottom);
        dialog.Content = new DockPanel
        {
            Margin = new Thickness(16),
            Children = { actions, body },
        };
        // WPF: Loaded += DialogFocus.Focus(_translationBox).
        dialog.Opened += (_, _) =>
        {
            // The shared descendant chrome pass (run on Opened by ApplyWindow) centres text box
            // content; multi-line boxes start at the top as in WPF.
            sourceBox.VerticalContentAlignment = global::Avalonia.Layout.VerticalAlignment.Top;
            translationBox.VerticalContentAlignment = global::Avalonia.Layout.VerticalAlignment.Top;
            translationBox.Focus();
        };
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
        var box = new ComboBox { ItemsSource = items, HorizontalAlignment = AvaloniaProofingHorizontalAlignment.Stretch };
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

        var result = _session.ExecuteReviewCommand(
            TranslateDialogPlanner.BuildCommand(plan),
            plan.TargetRange.Start);
        if (!result.Success)
        {
            RefreshShell(result.ErrorMessage ?? UiText.Get("WfTranslate_ErrorGeneric"));
            return false;
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
        AvaloniaCompactDialogChrome.ApplyTextBox(input, ProofingDialogChromeStyle);

        var symbols = new WrapPanel { MaxWidth = 360 };
        foreach (var symbol in new[] { "±", "×", "÷", "≤", "≥", "≠", "√", "π", "∑", "∞", "→", "²", "³" })
        {
            var btn = new Button { Content = symbol, Width = 40, Margin = new Thickness(2) };
            AvaloniaCompactDialogChrome.ApplyButton(btn, ProofingDialogChromeStyle, 40);
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

        var ok = new Button { Content = UiText.Get("ShellLoc_InsertButton"), IsDefault = true };
        var cancel = new Button { Content = UiText.Get("Common_Cancel"), IsCancel = true };
        AvaloniaCompactDialogChrome.ApplyButton(ok, ProofingDialogChromeStyle, 90, isDefault: true);
        AvaloniaCompactDialogChrome.ApplyButton(cancel, ProofingDialogChromeStyle, 90);
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
        layout.Children.Add(AvaloniaCompactDialogChrome.CreateActionRow([ok, cancel]));

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
