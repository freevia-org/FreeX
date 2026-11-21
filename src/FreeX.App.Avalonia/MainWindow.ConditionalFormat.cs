using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;

using Free.Shared.Shell.Avalonia;
using FreeX.App.Presentation;
using FreeX.App.Presentation.ConditionalFormatting;
using FreeX.App.Presentation.Dialogs;
using Free.Shared.Localization;
using FreeX.App.Presentation.QuickAnalysis;
using FreeX.App.Services;
using FreeX.Core.Commands;
using FreeX.Core.Model;

using AvaloniaHorizontalAlignment = Avalonia.Layout.HorizontalAlignment;
using AvaloniaVerticalAlignment = Avalonia.Layout.VerticalAlignment;
using AvaloniaGrid = Avalonia.Controls.Grid;

namespace FreeX.App.Avalonia;

/// <summary>
/// One entry in the Manage Rules dialog's scope ComboBox (Sheet / Table / Selection), mirroring the
/// WPF host's <c>ComboBoxItem</c> with a <c>ManageConditionalFormatScopeOption</c> tag. The ComboBox
/// renders <see cref="Label"/> via <see cref="ToString"/>; <see cref="Range"/> (null for "This
/// Worksheet") is what the dialog actually filters by.
/// </summary>
internal sealed record ManageConditionalFormatScopeItem(string Label, ManageConditionalFormatScope Scope, GridRange? Range)
{
    public override string ToString() => Label;
}

public sealed partial class MainWindow
{
    private static readonly ResourceKeyTextResolver ManageConditionalFormatsText = new(UiText.Get, UiText.Format);

    private static AvaloniaCompactDialogChromeStyle ConditionalFormatDialogChromeStyle => new(FormulaBarFontFamily);

    /// <summary>The native Format-menu "Conditional Formatting" submenu: New Rule, quick presets, Manage.</summary>
    private NativeMenu CreateNativeConditionalFormatMenu()
    {
        var menu = new NativeMenu();

        var newRule = new NativeMenuItem { Header = UiText.Get("MainWindow_Header_NewRule") };
        newRule.Click += (_, _) => RunGuarded(ShowConditionalFormatNewRuleDialogAsync);
        menu.Items.Add(newRule);
        menu.Items.Add(new NativeMenuItemSeparator());

        foreach (var (preset, ruleType) in new[]
                 {
                     (ConditionalFormatPreset.DataBar, ConditionalFormatDialogCatalog.DataBarRule),
                     (ConditionalFormatPreset.ColorScale, ConditionalFormatDialogCatalog.ColorScaleRule),
                     (ConditionalFormatPreset.IconSet, ConditionalFormatDialogCatalog.IconSetRule),
                 })
        {
            var capturedRuleType = ruleType;
            var item = new NativeMenuItem { Header = ConditionalFormatPresetFactory.DisplayName(preset) };
            item.Click += (_, _) => RunGuarded(() => ShowConditionalFormatRuleDialogAndApplyAsync(capturedRuleType));
            menu.Items.Add(item);
        }

        var greaterThan = new NativeMenuItem { Header = UiText.Get("ConditionalFormat_HighlightCellsGreaterThan") };
        greaterThan.Click += (_, _) => RunGuarded(() =>
            ShowConditionalFormatRuleDialogAndApplyAsync(ConditionalFormatDialogCatalog.GreaterThanRule));
        menu.Items.Add(greaterThan);

        var top10 = new NativeMenuItem { Header = UiText.Get("ConditionalFormatDialog_RuleType_Top10Items") };
        top10.Click += (_, _) => RunGuarded(() =>
            ShowConditionalFormatRuleDialogAndApplyAsync(ConditionalFormatDialogCatalog.Top10ItemsRule));
        menu.Items.Add(top10);

        menu.Items.Add(new NativeMenuItemSeparator());

        var clear = new NativeMenuItem { Header = UiText.Get("ConditionalFormat_ClearRulesFromSelectedCells") };
        clear.Click += (_, _) => ClearConditionalFormatsFromSelection();
        menu.Items.Add(clear);

        var manage = new NativeMenuItem { Header = UiText.Get("MainWindow_Header_ManageRules") };
        manage.Click += (_, _) => RunGuarded(ShowManageConditionalFormatsDialogAsync);
        menu.Items.Add(manage);

        return menu;
    }

    /// <summary>Applies a value-free quick preset to the selection through the session command path.</summary>
    private void ApplyConditionalFormatPreset(ConditionalFormatPreset preset)
    {
        if (!TryCommitPendingFormulaEdit())
            return;

        var range = _session.SelectedRange;
        RunConditionalFormatCommand(ConditionalFormatCommandPlanner.PlanApplyPreset(
            _session.GetCurrentGroupedEditSheetIds(),
            ResolveConditionalFormatSelectionRanges(range),
            preset));
    }

    /// <summary>Applies an icon-set conditional format of the given catalog style to the selection.</summary>
    private void ApplyConditionalFormatIconSet(string iconSetStyle)
    {
        if (!TryCommitPendingFormulaEdit())
            return;

        var range = _session.SelectedRange;
        RunConditionalFormatCommand(ConditionalFormatCommandPlanner.PlanApplyIconSet(
            _session.GetCurrentGroupedEditSheetIds(),
            ResolveConditionalFormatSelectionRanges(range),
            iconSetStyle));
    }

    /// <summary>Prompts for a threshold and applies the Highlight &gt; Greater Than preset.</summary>
    private async Task ApplyHighlightGreaterThanPresetAsync()
    {
        if (!TryCommitPendingFormulaEdit())
            return;

        var value = await ShowConditionalFormatValuePromptAsync(
            UiText.Get("Remaining_NewFormattingRule"),
            StripDisplayMnemonic(UiText.Get("Remaining_FormatCellsGreaterThan")),
            "0");
        if (value is null)
            return;

        var range = _session.SelectedRange;
        RunConditionalFormatCommand(ConditionalFormatCommandPlanner.PlanApplyHighlightGreaterThan(
            _session.GetCurrentGroupedEditSheetIds(),
            ResolveConditionalFormatSelectionRanges(range),
            value));
    }

    /// <summary>Clears every conditional-format rule overlapping the current selection (one undo step).</summary>
    private void ClearConditionalFormatsFromSelection()
    {
        if (!TryCommitPendingFormulaEdit())
            return;

        var range = _session.SelectedRange;
        RunConditionalFormatCommand(ConditionalFormatCommandPlanner.PlanClear(
            _session.GetCurrentGroupedEditSheetIds(),
            ResolveConditionalFormatSelectionRanges(range)));
    }

    private IReadOnlyList<GridRange> ResolveConditionalFormatSelectionRanges(GridRange fallbackRange)
    {
        var ranges = SelectionStyleCommandPlanner.ResolveRanges(_session.SelectedRange, _session.SelectedRanges);
        return ranges.Count > 0 ? ranges : [fallbackRange];
    }

    /// <summary>Runs a conditional-format command through the shared session command path and refreshes.</summary>
    private void RunConditionalFormatCommand(ConditionalFormatCommandExecutionPlan plan)
    {
        var result = _session.ExecuteReviewCommand(plan.Command);
        if (!result.Success)
        {
            ShowEditIssue(result.ErrorMessage ?? UiText.Get(plan.FailureResourceKey));
            return;
        }

        ApplyConditionalFormatRefresh(plan.RefreshPolicy, ResolveConditionalFormatStatus(plan.SuccessStatus));
    }

    private void ApplyConditionalFormatRefresh(
        ConditionalFormatStateRefreshPolicy policy,
        string successStatus)
    {
        if (policy == ConditionalFormatStateRefreshPolicy.WorksheetVisualState)
            RefreshShell(successStatus);
    }

    private static string ResolveConditionalFormatStatus(ConditionalFormatStatusPlan status) =>
        status.Arguments.Count == 0
            ? UiText.Get(status.ResourceKey)
            : UiText.Format(status.ResourceKey, status.Arguments.Cast<object?>().ToArray());

    /// <summary>Shows the rule editor for a new rule and applies the built Core rule to the selection.</summary>
    private Task ShowConditionalFormatNewRuleDialogAsync() =>
        ShowConditionalFormatNewRuleDialogAsync(startRuleType: null);

    /// <summary>
    /// Shows the new-rule editor, optionally pre-selecting <paramref name="startRuleType"/> (used by the
    /// ribbon's "New Formula Rule…" item, which seeds the Formula rule type), and applies the result.
    /// </summary>
    private async Task ShowConditionalFormatNewRuleDialogAsync(CfRuleType? startRuleType)
    {
        if (!TryCommitPendingFormulaEdit())
            return;

        var built = await ShowConditionalFormatRuleEditorAsync(existingRule: null, startRuleType);
        if (built is null)
            return;

        var range = built.AppliesTo;
        RunConditionalFormatCommand(ConditionalFormatCommandPlanner.PlanApplyRule(
            _session.GetCurrentGroupedEditSheetIds(),
            ResolveConditionalFormatSelectionRanges(range),
            built));
    }

    private Task<ConditionalFormat?> ShowConditionalFormatRuleEditorAsync(ConditionalFormat? existingRule) =>
        ShowConditionalFormatRuleEditorAsync(existingRule, startRuleType: null);

    /// <summary>
    /// Shows the WPF-style rule editor for an existing rule (Edit Formatting Rule) or, for a new rule,
    /// pre-selecting <paramref name="startRuleType"/> (the capture/validation entry points).
    /// </summary>
    private Task<ConditionalFormat?> ShowConditionalFormatRuleEditorAsync(
        ConditionalFormat? existingRule,
        CfRuleType? startRuleType)
    {
        if (existingRule is not null)
        {
            return ShowConditionalFormatRuleDialogCoreAsync(
                ConditionalFormatDialogPlanner.RuleTypeLabel(existingRule),
                existingRule,
                UiText.Get("ConditionalFormatDialog_EditTitle"));
        }

        // WPF's generic "New Rule" dialog is the New Formatting Rule shell opened on the cell-value
        // (Greater Than) rule; the dedicated dialogs keep their family titles.
        if (startRuleType is not { } start)
        {
            return ShowConditionalFormatRuleDialogCoreAsync(
                ConditionalFormatDialogCatalog.GreaterThanRule,
                existingRule: null,
                UiText.Get("ConditionalFormatDialog_NewTitle"));
        }

        return ShowConditionalFormatRuleDialogAsync(ConditionalFormatRuleDialogRuleTypeFor(start));
    }

    /// <summary>
    /// Opens the dedicated rule dialog WPF's <c>ShowCfDialog(ruleType)</c> opens for a rule token such as
    /// "Greater Than", "Top 10%", "Data Bar" or "Formula" (same title, fields and defaults), and returns
    /// the built rule, or null on cancel.
    /// </summary>
    private async Task<ConditionalFormat?> ShowConditionalFormatRuleDialogAsync(string ruleType)
    {
        if (!TryCommitPendingFormulaEdit())
            return null;

        return await ShowConditionalFormatRuleDialogCoreAsync(ruleType, existingRule: null, title: null);
    }

    private Task<ConditionalFormat?> ShowConditionalFormatRuleDialogCoreAsync(
        string ruleType,
        ConditionalFormat? existingRule,
        string? title,
        GridRange? defaultRange = null)
    {
        var range = existingRule?.AppliesTo ?? defaultRange ?? _session.SelectedRange;
        var editor = new ConditionalFormatRuleEditor(
            this,
            ruleType,
            range,
            existingRule,
            title ?? ConditionalFormatRuleDialogTitle(ruleType));
        return editor.ShowAsync();
    }

    /// <summary>The window title WPF's <c>ConditionalFormatDialogFactory</c> gives each dialog family.</summary>
    private static string ConditionalFormatRuleDialogTitle(string ruleType)
    {
        string DisplayName() => ConditionalFormatDialogCatalog.RuleTypeDisplayNameKey(ruleType) is { } key
            ? UiText.Get(key)
            : ruleType;

        return ConditionalFormatDialogCatalog.DialogFamilyForRuleType(ruleType) switch
        {
            ConditionalFormatDialogFamily.HighlightCells =>
                UiText.Format("ConditionalFormatDialog_HighlightCellsRuleTitleFormat", DisplayName()),
            ConditionalFormatDialogFamily.TopBottom =>
                UiText.Format("ConditionalFormatDialog_TopBottomRuleTitleFormat", DisplayName()),
            ConditionalFormatDialogFamily.DataBar => UiText.Get("ConditionalFormatDialog_DataBarRuleTitle"),
            ConditionalFormatDialogFamily.ColorScale => UiText.Get("ConditionalFormatDialog_ColorScaleRuleTitle"),
            ConditionalFormatDialogFamily.IconSet => UiText.Get("ConditionalFormatDialog_IconSetRuleTitle"),
            _ => UiText.Get("ConditionalFormatDialog_NewTitle"),
        };
    }

    /// <summary>Maps a model rule type to the WPF rule-dialog token its dedicated dialog opens with.</summary>
    private static string ConditionalFormatRuleDialogRuleTypeFor(CfRuleType ruleType) => ruleType switch
    {
        CfRuleType.Formula => ConditionalFormatDialogCatalog.FormulaRule,
        CfRuleType.Top10 => ConditionalFormatDialogCatalog.Top10ItemsRule,
        CfRuleType.IconSet => ConditionalFormatDialogCatalog.IconSetRule,
        CfRuleType.DataBar => ConditionalFormatDialogCatalog.DataBarRule,
        CfRuleType.ColorScale => ConditionalFormatDialogCatalog.ColorScaleRule,
        CfRuleType.ContainsText => ConditionalFormatDialogCatalog.TextContainsRule,
        CfRuleType.NotContainsText => ConditionalFormatDialogCatalog.TextDoesNotContainRule,
        CfRuleType.BeginsWith => ConditionalFormatDialogCatalog.TextBeginsWithRule,
        CfRuleType.EndsWith => ConditionalFormatDialogCatalog.TextEndsWithRule,
        CfRuleType.DateOccurring => ConditionalFormatDialogCatalog.DateOccurringRule,
        CfRuleType.DuplicateValues or CfRuleType.UniqueValues => ConditionalFormatDialogCatalog.DuplicateValuesRule,
        CfRuleType.AboveAverage => ConditionalFormatDialogCatalog.AboveAverageRule,
        CfRuleType.Blanks => ConditionalFormatDialogCatalog.BlanksRule,
        CfRuleType.NoBlanks => ConditionalFormatDialogCatalog.NoBlanksRule,
        CfRuleType.Errors => ConditionalFormatDialogCatalog.ErrorsRule,
        CfRuleType.NoErrors => ConditionalFormatDialogCatalog.NoErrorsRule,
        _ => ConditionalFormatDialogCatalog.GreaterThanRule,
    };

    /// <summary>
    /// Ribbon entry for a Home &gt; Conditional Formatting rule item: opens the dedicated rule dialog and
    /// applies the built rule to the selection (WPF <c>ShowCfDialog</c> + <c>ApplyConditionalFormatPreset</c>).
    /// </summary>
    private async Task ShowConditionalFormatRuleDialogAndApplyAsync(string ruleType)
    {
        var built = await ShowConditionalFormatRuleDialogAsync(ruleType);
        if (built is null)
            return;

        RunConditionalFormatCommand(ConditionalFormatCommandPlanner.PlanApplyRule(
            _session.GetCurrentGroupedEditSheetIds(),
            ResolveConditionalFormatSelectionRanges(built.AppliesTo),
            built));
    }

    private static string FormatRgb(RgbColor color) =>
        ConditionalFormatInputParser.FormatRgb(color);

    // ── Chrome helpers ────────────────────────────────────────────────────────

    private static void ApplyCfButtonChrome(
        Button button,
        double width,
        bool isDefault = false,
        AvaloniaCompactDialogChromeStyle? style = null)
    {
        button.Width = width;
        AvaloniaCompactDialogChrome.ApplyButton(button, style ?? ConditionalFormatDialogChromeStyle, width, isDefault);
    }

    private static void ApplyCfTextBoxChrome(TextBox tb)
        => AvaloniaCompactDialogChrome.ApplyTextBox(tb, ConditionalFormatDialogChromeStyle);

    private static void ApplyCfComboBoxChrome(ComboBox cb)
        => AvaloniaCompactDialogChrome.ApplyComboBox(cb, ConditionalFormatDialogChromeStyle);

    private static void ApplyCfCheckBoxChrome(CheckBox cb)
    {
        StripContentMnemonic(cb);
        cb.MinHeight = 20;
        cb.MaxHeight = 20;
        AvaloniaCompactDialogChrome.ApplyCheckBox(cb, ConditionalFormatDialogChromeStyle);
    }

    /// <summary>A tiny single-value prompt used by the Highlight &gt; Greater Than preset.</summary>
    private async Task<string?> ShowConditionalFormatValuePromptAsync(string title, string prompt, string initial)
    {
        string? result = null;
        var dialog = new Window
        {
            Title = title,
            // WPF ConditionalFormatThresholdDialog is a 360x150 NoResize window: an OUTER size, so
            // the Avalonia client area drops the WPF frame.
            Width = 360 - WpfNoResizeFrameWidth,
            Height = 150 - WpfNoResizeClientFrameHeight,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };
        AutomationProperties.SetAutomationId(dialog, "ConditionalFormatValuePromptDialog");

        var valueBox = new TextBox { Text = initial, MinWidth = 240 };
        ApplyCfTextBoxChrome(valueBox);
        AutomationProperties.SetAutomationId(valueBox, "ConditionalFormatValuePromptBox");

        // WPF ConditionalFormatThresholdDialog uses ObjectSizeDialog.CreateSingleInputContent: 72px buttons.
        var okButton = new Button { Content = UiText.Get("InsertLoc_OkButton"), IsDefault = true, MinWidth = 72 };
        ApplyCfButtonChrome(okButton, 72, isDefault: true);
        var cancelButton = new Button { Content = UiText.Get("InsertLoc_CancelButton"), IsCancel = true, MinWidth = 72 };
        ApplyCfButtonChrome(cancelButton, 72);
        okButton.Click += (_, _) => RunGuarded(async () =>
        {
            // WPF ConditionalFormatThresholdDialog.Accept: blank input shows "Enter a threshold value."
            // (titled with the dialog), refocuses the box and keeps the dialog open.
            if (!ConditionalFormatThresholdDialogPlanner.TryCreateResult(valueBox.Text, out var threshold))
            {
                await AvaloniaUserMessageDialog.ShowWarningAsync(
                    dialog,
                    UiText.Get("Remaining_EnterThresholdValue"),
                    dialog.Title ?? string.Empty);
                AvaloniaCompactDialogChrome.FocusAndSelect(valueBox);
                return;
            }

            result = threshold.ThresholdText;
            dialog.Close();
        });
        cancelButton.Click += (_, _) => dialog.Close();

        // WPF button order: [OK][Cancel] — primary on left
        var buttonRow = AvaloniaCompactDialogChrome.CreateActionRow([okButton, cancelButton]);

        // WPF CreateSingleInputContent: one top-down stack (margin 16) of the prompt 4px above the
        // value box and the button row 12px below it, so the buttons follow the field instead of
        // being docked to the window bottom.
        valueBox.Margin = new Thickness(0, 0, 0, 12);
        dialog.Content = new StackPanel
        {
            Margin = new Thickness(16),
            Children =
            {
                new TextBlock
                {
                    Text = prompt,
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 12,
                    FontFamily = FormulaBarFontFamily,
                    Margin = new Thickness(0, 0, 0, 4),
                },
                valueBox,
                buttonRow,
            },
        };
        dialog.Opened += (_, _) =>
        {
            valueBox.Focus();
            valueBox.SelectAll();
        };

        await dialog.ShowDialog(this);
        return result;
    }

    /// <summary>
    /// The Manage Rules dialog: lists the selection's overlapping rules (or every sheet rule when the
    /// selection is a single cell) with New, Edit, Delete, reorder (move up/down), and change applies-to.
    /// All edits (including toggling Stop If True) mutate an in-memory working copy of the sheet's
    /// rules — nothing touches the live workbook until OK/Apply commits the whole working copy as one
    /// atomic <see cref="ReplaceAllConditionalFormatsCommand"/> (a single undo step). Cancel/closing the
    /// window without committing simply discards the working copy, so the workbook is untouched —
    /// mirroring the Windows host's manager (which buffers edits in a private
    /// <c>ObservableCollection&lt;ConditionalFormat&gt;</c>).
    /// </summary>
    private async Task ShowManageConditionalFormatsDialogAsync()
    {
        if (!TryCommitPendingFormulaEdit())
            return;

        var scopePlan = ManageConditionalFormatsPlanner.CreateDialogPlan(
            _session.ActiveSheet,
            _session.SelectedRange);
        var manageSession = new ManageConditionalFormatsSession(
            _session.ActiveSheet.ConditionalFormats,
            scopePlan.DefaultScopeOption.Range,
            ManageConditionalFormatsWorkingCopyPolicy.FullSheet);

        var dialog = new Window
        {
            Title = UiText.Get("ManageConditionalFormats_ConditionalFormattingRulesManager"),
            // WPF ManageConditionalFormatsDialog is a 560x420 NoResize window; both are OUTER sizes
            // there, so the client area drops the measured Windows frame.
            Width = 560 - WpfNoResizeFrameWidth,
            Height = 420 - WpfNoResizeClientFrameHeight,
            MinWidth = 480,
            MinHeight = 360,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };
        AutomationProperties.SetAutomationId(dialog, "ManageConditionalFormatsDialog");
        var manageDialogChrome = ConditionalFormatDialogChromeStyle with { ButtonHeight = 22 };

        var listBox = new ListBox
        {
            // The list fills whatever the fixed client area leaves between the header and the rows
            // docked below it; the floor only has to keep a few rules visible.
            MinHeight = 180,
            Background = Brushes.White,
            BorderThickness = new Thickness(0),
        };
        AvaloniaCompactDialogChrome.ApplyListBox(
            listBox,
            manageDialogChrome with { ListBoxItemPadding = new Thickness(2, 0) });
        AutomationProperties.SetAutomationId(listBox, "ManageConditionalFormatsListBox");
        AutomationProperties.SetName(listBox, UiText.Get("ManageConditionalFormats_ConditionalFormattingRules"));

        var emptyText = new TextBlock
        {
            Text = UiText.Get("ConditionalFormat_NoRules"),
            Foreground = HeaderForeground,
            IsVisible = false,
            FontSize = 12,
            FontFamily = FormulaBarFontFamily,
        };

        // WPF edits "Applies to" inline: every row carries its own reference box and range picker, enabled
        // only on the selected row. Rows register here so the selection handler can toggle them.
        var appliesToCells = new Dictionary<Guid, (TextBox Box, Button Picker)>();

        // WPF keeps OK/Apply from pushing an undo step for a visit with zero edits: both are no-ops until
        // the user adds, edits, deletes, duplicates, moves, re-ranges or toggles Stop If True on a rule.
        var hasPendingChanges = false;
        Button? applyButton = null;
        void MarkPendingChange(bool pending = true)
        {
            hasPendingChanges = pending;
            if (applyButton is not null)
                applyButton.IsEnabled = pending;
        }

        // Shared with the WPF host: builds Sheet/Table/Selection options, adding "This Table"
        // only when the current selection sits inside a structured table (FindSelectionTableRange).
        var scopeItems = scopePlan.ScopeOptions
            .Select(option => new ManageConditionalFormatScopeItem(
                UiText.Get(option.LabelKey).Replace("_", string.Empty, StringComparison.Ordinal),
                option.Scope,
                option.Range))
            .ToArray();

        var scopeBox = new ComboBox
        {
            MinWidth = 160,
            ItemsSource = scopeItems,
            SelectedIndex = Math.Max(0, Array.FindIndex(scopeItems, item => item.Scope == scopePlan.DefaultScope)),
            VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center,
        };
        AvaloniaCompactDialogChrome.ApplyComboBox(scopeBox, manageDialogChrome);
        AutomationProperties.SetAutomationId(scopeBox, "ManageConditionalFormatsScopeBox");
        AutomationProperties.SetName(scopeBox, UiText.Get("ManageConditionalFormats_ShowFormattingRulesFor").Replace("_", string.Empty, StringComparison.Ordinal));

        // The shared session uses this scope for both the list projection and move neighbours.
        GridRange? CurrentScope() => scopeBox.SelectedItem is ManageConditionalFormatScopeItem { Range: { } range } ? range : null;

        // WPF ManageConditionalFormatsDialog opens (and changes scope) with nothing selected, so Edit,
        // Duplicate, Delete and Move stay disabled; a delete selects the neighbouring row.
        void Reload(Guid? selectId = null, int? selectIndex = null)
        {
            var scope = CurrentScope();
            manageSession.SetScope(scope);
            var items = manageSession.BuildProjection();
            listBox.ItemsSource = items;
            emptyText.IsVisible = items.Count == 0;
            if (items.Count > 0)
            {
                var index = -1;
                if (selectId is { } id)
                    for (var i = 0; i < items.Count; i++)
                        if (items[i].Id == id)
                        {
                            index = i;
                            break;
                        }

                if (selectIndex is { } neighbour)
                    index = Math.Min(neighbour, items.Count - 1);

                listBox.SelectedIndex = index;
            }

            SyncCommandState();
        }

        var newButton = new Button { Content = UiText.Get("ManageConditionalFormats_NewRule"), Width = 104, Margin = new Thickness(0, 0, 6, 0) };
        ApplyCfButtonChrome(newButton, 104, style: manageDialogChrome);
        AutomationProperties.SetAutomationId(newButton, "ManageConditionalFormatsNewButton");
        var editButton = new Button { Content = UiText.Get("ManageConditionalFormats_EditRule"), Width = 94, Margin = new Thickness(0, 0, 6, 0), IsEnabled = false };
        ApplyCfButtonChrome(editButton, 94, style: manageDialogChrome);
        AutomationProperties.SetAutomationId(editButton, "ManageConditionalFormatsEditButton");
        var duplicateButton = new Button { Content = UiText.Get("ManageConditionalFormats_DuplicateRule"), Width = 118, Margin = new Thickness(0, 0, 6, 0), IsEnabled = false };
        ApplyCfButtonChrome(duplicateButton, 118, style: manageDialogChrome);
        AutomationProperties.SetAutomationId(duplicateButton, "ManageConditionalFormatsDuplicateButton");
        var deleteButton = new Button { Content = UiText.Get("ManageConditionalFormats_DeleteRule"), Width = 100, Margin = new Thickness(0, 0, 12, 0), IsEnabled = false };
        ApplyCfButtonChrome(deleteButton, 100, style: manageDialogChrome);
        AutomationProperties.SetAutomationId(deleteButton, "ManageConditionalFormatsDeleteButton");
        var moveUpButton = new Button { Content = "\u25B2", Width = 32, Margin = new Thickness(0, 0, 4, 0), IsEnabled = false };
        ApplyCfButtonChrome(moveUpButton, 32, style: manageDialogChrome);
        // The compact 12px side padding left the 32px arrow buttons a 6px content box that clipped
        // the triangle glyphs; WPF centres them in the full button.
        moveUpButton.Padding = new Thickness(0);
        AutomationProperties.SetAutomationId(moveUpButton, "ManageConditionalFormatsMoveUpButton");
        AutomationProperties.SetName(moveUpButton, UiText.Get("ManageConditionalFormats_MoveUp"));
        ToolTip.SetTip(moveUpButton, UiText.Get("ManageConditionalFormats_MoveSelectedRuleUp"));
        var moveDownButton = new Button { Content = "\u25BC", Width = 32, IsEnabled = false };
        ApplyCfButtonChrome(moveDownButton, 32, style: manageDialogChrome);
        moveDownButton.Padding = new Thickness(0);
        AutomationProperties.SetAutomationId(moveDownButton, "ManageConditionalFormatsMoveDownButton");
        AutomationProperties.SetName(moveDownButton, UiText.Get("ManageConditionalFormats_MoveDown"));
        ToolTip.SetTip(moveDownButton, UiText.Get("ManageConditionalFormats_MoveSelectedRuleDown"));
        var applyChangesButton = new Button { Content = UiText.Get("ManageConditionalFormats_Apply"), Width = 72, IsEnabled = false };
        ApplyCfButtonChrome(applyChangesButton, 72, style: manageDialogChrome);
        AutomationProperties.SetAutomationId(applyChangesButton, "ManageConditionalFormatsApplyButton");
        applyButton = applyChangesButton;
        var closeButton = new Button { Content = UiText.Get("Common_Ok"), IsDefault = true, Width = 72, Margin = new Thickness(0, 0, 6, 0) };
        ApplyCfButtonChrome(closeButton, 72, isDefault: true, style: manageDialogChrome);
        AutomationProperties.SetAutomationId(closeButton, "ManageConditionalFormatsCloseButton");
        var cancelButton = new Button { Content = UiText.Get("Common_Cancel"), IsCancel = true, Width = 72, Margin = new Thickness(0, 0, 6, 0) };
        ApplyCfButtonChrome(cancelButton, 72, style: manageDialogChrome);
        AutomationProperties.SetAutomationId(cancelButton, "ManageConditionalFormatsCancelButton");

        void SyncCommandState()
        {
            var hasSelection = listBox.SelectedItem is ManageConditionalFormatRuleProjection;
            editButton.IsEnabled = hasSelection;
            duplicateButton.IsEnabled = hasSelection;
            deleteButton.IsEnabled = hasSelection;
            moveUpButton.IsEnabled = hasSelection && listBox.SelectedIndex > 0;
            moveDownButton.IsEnabled = hasSelection && listBox.SelectedIndex >= 0 && listBox.SelectedIndex < listBox.ItemCount - 1;
            foreach (var (ruleId, cell) in appliesToCells)
            {
                var isSelected = listBox.SelectedItem is ManageConditionalFormatRuleProjection current && current.Id == ruleId;
                cell.Box.IsEnabled = isSelected;
                cell.Picker.IsEnabled = isSelected;
            }
        }

        listBox.SelectionChanged += (_, _) => SyncCommandState();
        scopeBox.SelectionChanged += (_, _) => Reload();

        newButton.Click += (_, _) => RunGuarded(async () =>
        {
            // WPF ManageConditionalFormatsDialog opens New Rule on the Data Bar rule type, over the plan's
            // default new-rule range, in the "New Formatting Rule" dialog.
            var built = await ShowConditionalFormatRuleDialogCoreAsync(
                ConditionalFormatDialogCatalog.DataBarRule,
                existingRule: null,
                UiText.Get("ConditionalFormatDialog_NewTitle"),
                scopePlan.DefaultNewRuleRange);
            if (built is null)
                return;

            // Append to the working copy only — nothing reaches the live sheet until Commit().
            manageSession.Add(built);
            MarkPendingChange();
            Reload(built.Id);
        });

        editButton.Click += (_, _) => RunGuarded(async () =>
        {
            if (listBox.SelectedItem is not ManageConditionalFormatRuleProjection item)
                return;

            var edited = await ShowConditionalFormatRuleEditorAsync(item.Rule);
            if (edited is null)
                return;

            if (!manageSession.Replace(edited))
                return;

            MarkPendingChange();
            Reload(edited.Id);
        });

        deleteButton.Click += (_, _) =>
        {
            if (listBox.SelectedItem is not ManageConditionalFormatRuleProjection item)
                return;

            var deletedIndex = listBox.SelectedIndex;
            if (!manageSession.Delete(item.Id))
                return;

            MarkPendingChange();
            Reload(selectIndex: deletedIndex);
        };

        duplicateButton.Click += (_, _) => RunGuarded(async () =>
        {
            if (listBox.SelectedItem is not ManageConditionalFormatRuleProjection item)
                return;

            var duplicateId = Guid.NewGuid();
            if (!manageSession.Duplicate(item.Id, duplicateId))
                return;

            MarkPendingChange();
            Reload(duplicateId);
        });

        void Move(ConditionalFormatRuleMoveDirection direction)
        {
            if (listBox.SelectedItem is not ManageConditionalFormatRuleProjection item)
                return;

            if (!manageSession.Move(item.Id, direction))
                return;

            MarkPendingChange();
            Reload(item.Id);
        }

        moveUpButton.Click += (_, _) => Move(ConditionalFormatRuleMoveDirection.Up);
        moveDownButton.Click += (_, _) => Move(ConditionalFormatRuleMoveDirection.Down);

        // Commits an Applies To reference typed into (or picked for) a row. WPF's two-way text binding keeps the
        // rule unchanged when the text does not parse; typing a fresh range also drops stale multi-area ranges.
        void CommitAppliesTo(ManageConditionalFormatRuleProjection item, string text)
        {
            var reference = ManageConditionalFormatsPlanner.NormalizeAppliesToText(text);
            if (!string.IsNullOrWhiteSpace(reference)
                && (ManageConditionalFormatsPlanner.TryParseAppliesToText(reference, _session.ActiveSheet.Id, out var range)
                    || _session.TryResolveReferenceRange(reference, out range))
                && manageSession.ApplyRange(item.Id, range))
            {
                MarkPendingChange();
            }

            Reload(item.Id);
        }

        listBox.DoubleTapped += (_, e) =>
        {
            if (e.Source is Visual { } source
                && source.FindAncestorOfType<ListBoxItem>() is not null
                && listBox.SelectedItem is ManageConditionalFormatRuleProjection)
            {
                editButton.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                e.Handled = true;
            }
        };
        listBox.KeyDown += (_, e) =>
        {
            // WPF ListView: Enter edits and Delete deletes the selected rule (only from the list/row itself,
            // never while typing in a row's Applies To box).
            if (e.Source is not (ListBox or ListBoxItem))
                return;

            if (e.Key == Key.Enter)
            {
                editButton.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                e.Handled = true;
            }
            else if (e.Key == Key.Delete)
            {
                deleteButton.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                e.Handled = true;
            }
        };

        void Commit()
        {
            // A single atomic replace-all: one undo step for every New/Edit/Delete/Duplicate/Move/
            // AppliesTo/Stop-If-True edit made in this dialog session.
            RunConditionalFormatCommand(manageSession.CreateApplyPlan(
                [_session.ActiveSheet.Id],
                _session.ActiveSheet.Id));
        }

        // OK commits only when something changed (WPF skips the spurious undo entry for an untouched visit);
        // Apply commits the pending edits without closing and disables itself until the next edit.
        closeButton.Click += (_, _) =>
        {
            if (hasPendingChanges)
                Commit();
            dialog.Close();
        };
        applyChangesButton.Click += (_, _) =>
        {
            if (!hasPendingChanges)
                return;

            Commit();
            MarkPendingChange(false);
        };
        // Cancel (and closing the window without clicking OK) simply discards the working copy —
        // the workbook is never touched, matching Excel/WPF.
        cancelButton.Click += (_, _) => dialog.Close();

        var scopeRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 8),
            Children =
            {
                new TextBlock
                {
                    Text = StripDisplayMnemonic(UiText.Get("ManageConditionalFormats_ShowFormattingRulesFor")).Replace("_", string.Empty, StringComparison.Ordinal),
                    VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 6, 0),
                    FontSize = 12,
                    FontFamily = FormulaBarFontFamily,
                },
                scopeBox,
            },
        };
        DockPanel.SetDock(scopeRow, Dock.Top);

        var toolbarRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 6),
            Children = { newButton, editButton, duplicateButton, deleteButton, moveUpButton, moveDownButton },
        };
        DockPanel.SetDock(toolbarRow, Dock.Bottom);

        var buttonRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Right,
            Margin = new Thickness(0, 8, 0, 0),
            Children = { closeButton, cancelButton, applyChangesButton },
        };
        DockPanel.SetDock(buttonRow, Dock.Bottom);

        static TextBlock HeaderCell(string text, int column) =>
            new()
            {
                Text = text,
                // WPF GridView column headers: regular weight, centred captions.
                FontWeight = FontWeight.Normal,
                TextAlignment = global::Avalonia.Media.TextAlignment.Center,
                FontSize = 12,
                FontFamily = FormulaBarFontFamily,
                Padding = new Thickness(5, 3),
                VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center,
                TextTrimming = global::Avalonia.Media.TextTrimming.CharacterEllipsis,
                ClipToBounds = true,
                [AvaloniaGrid.ColumnProperty] = column,
            };

        // Columns sum to less than the rules-frame inner width (560 dialog − 24 margin − 2 border ≈ 534),
        // and the final "Stop If True" column is a star so it absorbs the remainder and can never spill
        // past the frame's right border (the Linux overflow bug). ClipToBounds keeps any long header text
        // inside its own cell.
        var headerGrid = new AvaloniaGrid
        {
            ColumnDefinitions = new ColumnDefinitions(ManageCfRuleColumns),
            Background = Brush(243, 243, 243),
            ClipToBounds = true,
            Children =
            {
                HeaderCell("#", 0),
                HeaderCell(UiText.Get("ManageConditionalFormats_RuleTypeColumn"), 1),
                HeaderCell(UiText.Get("ManageConditionalFormats_FormatColumn"), 2),
                HeaderCell(UiText.Get("ManageConditionalFormats_AppliesToColumn"), 3),
                HeaderCell(UiText.Get("ManageConditionalFormats_StopIfTrueColumn"), 4),
            },
        };
        DockPanel.SetDock(headerGrid, Dock.Top);

        var rulesPanel = new DockPanel
        {
            Children =
            {
                headerGrid,
                listBox,
            },
        };

        var rulesFrame = new Border
        {
            BorderBrush = Brush(171, 173, 179),
            BorderThickness = new Thickness(1),
            Background = Brushes.White,
            Child = rulesPanel,
        };

        // Render each rule as a #/Rule-type/Format-swatch/Applies-to/Stop-if row matching the header
        // columns (the WPF GridView), instead of the default single-string row. Toggling Stop If
        // True mutates the matching rule directly in the working copy (mirroring the WPF grid's
        // two-way-bound checkbox column) — it never touches the live sheet until Commit().
        listBox.ItemTemplate = new FuncDataTemplate<ManageConditionalFormatRuleProjection>(
            (item, _) => BuildManageConditionalFormatRow(
                item,
                isChecked =>
                {
                    if (manageSession.SetStopIfTrue(item.Id, isChecked))
                        MarkPendingChange();
                },
                (box, picker) =>
                {
                    appliesToCells[item.Id] = (box, picker);
                    var selected = listBox.SelectedItem is ManageConditionalFormatRuleProjection current
                        && current.Id == item.Id;
                    box.IsEnabled = selected;
                    picker.IsEnabled = selected;
                    ConfigureManageAppliesToCell(dialog, box, picker, item, CommitAppliesTo);
                }),
            supportsRecycling: false);

        Reload();
        SyncCommandState();
        dialog.Content = new DockPanel
        {
            Margin = new Thickness(12),
            Children =
            {
                scopeRow,
                buttonRow,
                toolbarRow,
                // Center fill: "Rules:" label (top) + appliesTo box (bottom) + the rules frame
                // stretched to consume all remaining height, so there is no dead gap above the
                // docked button/toolbar rows (matches the Windows layout spacing).
                new DockPanel
                {
                    Children =
                    {
                        new TextBlock
                        {
                            Text = StripDisplayMnemonic(UiText.Get("ManageConditionalFormats_Rules")).Replace("_", string.Empty, StringComparison.Ordinal),
                            FontSize = 12,
                            FontFamily = FormulaBarFontFamily,
                            Margin = new Thickness(0, 0, 0, 4),
                            [DockPanel.DockProperty] = Dock.Top,
                        },
                        rulesFrame,
                    },
                },
            },
        };

        // Match the WPF manager's Loaded focus target: the scope selector is the first keyboard
        // target, so Tab and Shift+Tab both start from the same predictable control.
        dialog.Opened += (_, _) => scopeBox.Focus();

        AttachOptionalManageConditionalFormatsDialogObservation(
            dialog,
            scopeBox,
            listBox,
            newButton,
            editButton,
            deleteButton,
            moveUpButton,
            moveDownButton,
            applyChangesButton,
            closeButton);

        await dialog.ShowDialog(this);
    }

    /// <summary>
    /// Wires one row's inline Applies To box: the shared range picker writes the picked reference into the
    /// box, and a reference that differs from the one the row opened with is committed when the box loses
    /// focus or is set programmatically while unfocused (the picker path).
    /// </summary>
    private void ConfigureManageAppliesToCell(
        Window dialog,
        TextBox box,
        Button picker,
        ManageConditionalFormatRuleProjection item,
        Action<ManageConditionalFormatRuleProjection, string> commit)
    {
        var committedText = box.Text ?? string.Empty;

        void CommitIfChanged()
        {
            var text = box.Text ?? string.Empty;
            if (string.Equals(text, committedText, StringComparison.Ordinal))
                return;

            committedText = text;
            commit(item, text);
        }

        box.LostFocus += (_, _) => CommitIfChanged();
        box.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBox.TextProperty && !box.IsFocused)
                CommitIfChanged();
        };
        AttachDialogRangePicker(dialog, picker, box, "range.conditional-format.applies-to");
    }

    // Shared column layout for the Manage-rules header AND each rule row so they line up:
    // # | Rule (Type) | Format | Applies To | Stop If True(*). Widths sum under the ~534px frame so
    // the star "Stop If True" column absorbs the remainder (and fits its full header text).
    private const string ManageCfRuleColumns = "32,170,92,128,*";

    /// <summary>
    /// Builds one rules-manager row matching the column header (mirrors the WPF GridView rows).
    /// <paramref name="onStopIfTrueToggled"/> is invoked with the new checked state whenever the user
    /// toggles the row's Stop-If-True checkbox, mirroring the WPF grid's two-way-bound column (which
    /// edits the working-copy rule directly rather than requiring the rule editor).
    /// </summary>
    private Control BuildManageConditionalFormatRow(
        ManageConditionalFormatRuleProjection item,
        Action<bool> onStopIfTrueToggled,
        Action<TextBox, Button> onAppliesToCellCreated)
    {
        var rule = item.Rule;
        var grid = new AvaloniaGrid
        {
            ColumnDefinitions = new ColumnDefinitions(ManageCfRuleColumns),
            // WPF ListView rows are about 28px: the 24px Applies To editor plus the item chrome.
            Height = 28,
        };

        void AddCell(Control control, int column)
        {
            AvaloniaGrid.SetColumn(control, column);
            grid.Children.Add(control);
        }

        static TextBlock RowText(string text) => new()
        {
            Text = text,
            FontSize = 12,
            FontFamily = FormulaBarFontFamily,
            VerticalAlignment = AvaloniaVerticalAlignment.Center,
            Margin = new Thickness(5, 0),
            TextTrimming = global::Avalonia.Media.TextTrimming.CharacterEllipsis,
        };

        AddCell(RowText(rule.Priority.ToString(global::System.Globalization.CultureInfo.InvariantCulture)), 0);
        AddCell(RowText(ManageConditionalFormatsPlanner.ResolveDescription(item.Description, ManageConditionalFormatsText)), 1);
        AddCell(BuildConditionalFormatPreviewSwatch(rule), 2);
        // WPF Applies To cell: an editable reference box (2px padding) with a 24px range picker docked 4px to
        // its right; both are enabled only while the row is selected.
        var appliesToPickerCell = new Button
        {
            Content = "...",
            Width = 24,
            Margin = new Thickness(4, 0, 0, 0),
        };
        ApplyCfButtonChrome(appliesToPickerCell, 24, style: ConditionalFormatDialogChromeStyle with { ButtonHeight = 22 });
        appliesToPickerCell.Padding = new Thickness(0);
        AutomationProperties.SetAutomationId(appliesToPickerCell, "ManageConditionalFormatsAppliesToPickerButton");
        AutomationProperties.SetName(appliesToPickerCell, UiText.Get("ManageConditionalFormats_SelectAppliesToRange"));
        AutomationProperties.SetHelpText(appliesToPickerCell, UiText.Get("ManageConditionalFormats_SelectAppliesToRangeHelpText"));
        ToolTip.SetTip(appliesToPickerCell, UiText.Get("ManageConditionalFormats_CollapseDialogAndSelectAppliesToRange"));
        DockPanel.SetDock(appliesToPickerCell, Dock.Right);
        var appliesToBoxCell = new TextBox
        {
            Text = ManageConditionalFormatsPlanner.FormatAppliesToRange(rule.AppliesTo),
            Padding = new Thickness(2, 0),
            VerticalContentAlignment = AvaloniaVerticalAlignment.Center,
        };
        ApplyCfTextBoxChrome(appliesToBoxCell);
        appliesToBoxCell.Height = 22;
        appliesToBoxCell.MinHeight = 22;
        appliesToBoxCell.MaxHeight = 22;
        AutomationProperties.SetAutomationId(appliesToBoxCell, "ManageConditionalFormatsAppliesToBox");
        AutomationProperties.SetName(appliesToBoxCell, UiText.Get("ManageConditionalFormats_AppliesToColumn"));
        var appliesToCell = new DockPanel
        {
            Margin = new Thickness(5, 0, 5, 0),
            Height = 22,
            VerticalAlignment = AvaloniaVerticalAlignment.Center,
            Children =
            {
                appliesToPickerCell,
                appliesToBoxCell,
            },
        };
        onAppliesToCellCreated(appliesToBoxCell, appliesToPickerCell);
        AddCell(appliesToCell, 3);
        // Stop-If-True: an interactive checkbox that mutates the working-copy rule directly
        // (mirroring the WPF grid's two-way-bound column), not just a display of the current value.
        var stopBox = new CheckBox
        {
            IsChecked = rule.StopIfTrue,
            MinWidth = 0,
            VerticalAlignment = AvaloniaVerticalAlignment.Center,
            Margin = new Thickness(8, 0),
        };
        AutomationProperties.SetName(stopBox, UiText.Get("ManageConditionalFormats_StopIfTrueColumn"));
        stopBox.IsCheckedChanged += (_, _) => onStopIfTrueToggled(stopBox.IsChecked == true);
        AddCell(stopBox, 4);
        return grid;
    }

    /// <summary>
    /// The Format column preview (WPF <c>CreateFormatPreviewColumn</c>): an 82x20 swatch with a 0.5px
    /// dark-grey frame, filled and lettered from the shared <c>ManageConditionalFormatsPlanner.CreatePreviewPlan</c>
    /// so data bars, colour scales and icon sets read the same as the cell-value rules.
    /// </summary>
    private Control BuildConditionalFormatPreviewSwatch(ConditionalFormat rule)
    {
        static Color ToColor(PresentationRgb color) => Color.FromRgb(color.R, color.G, color.B);

        var preview = ManageConditionalFormatsPlanner.CreatePreviewPlan(rule);
        IBrush background;
        if (preview.Fill.IsGradient)
        {
            var gradient = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative),
            };
            for (var index = 0; index < preview.Fill.Stops.Count; index++)
            {
                var offset = preview.Fill.Stops.Count == 1 ? 0 : (double)index / (preview.Fill.Stops.Count - 1);
                gradient.GradientStops.Add(new GradientStop(ToColor(preview.Fill.Stops[index]), offset));
            }

            background = gradient;
        }
        else
        {
            background = preview.Fill.Stops.Count > 0
                ? new SolidColorBrush(ToColor(preview.Fill.Stops[0]))
                : Brushes.LightGray;
        }

        TextDecorationCollection? decorations = null;
        if (preview.Underline || preview.Strikethrough)
        {
            decorations = new TextDecorationCollection();
            if (preview.Underline)
                decorations.Add(new TextDecoration { Location = global::Avalonia.Media.TextDecorationLocation.Underline });
            if (preview.Strikethrough)
                decorations.Add(new TextDecoration { Location = global::Avalonia.Media.TextDecorationLocation.Strikethrough });
        }

        var swatch = new Border
        {
            Width = 82,
            Height = 20,
            Margin = new Thickness(0, 2),
            HorizontalAlignment = AvaloniaHorizontalAlignment.Left,
            BorderBrush = Brushes.DarkGray,
            BorderThickness = new Thickness(0.5),
            Background = background,
            Child = new TextBlock
            {
                Text = UiText.Get(preview.SampleTextKey),
                FontSize = 10,
                FontFamily = FormulaBarFontFamily,
                HorizontalAlignment = AvaloniaHorizontalAlignment.Center,
                VerticalAlignment = AvaloniaVerticalAlignment.Center,
                Foreground = new SolidColorBrush(ToColor(preview.Foreground)),
                FontWeight = preview.Bold ? FontWeight.Bold : FontWeight.Normal,
                FontStyle = preview.Italic ? global::Avalonia.Media.FontStyle.Italic : global::Avalonia.Media.FontStyle.Normal,
                TextDecorations = decorations,
            },
        };

        return swatch;
    }
}
