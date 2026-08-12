using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;

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

    /// <summary>The quick presets the New Rule editor offers as a starting point, in dropdown order.</summary>
    private static readonly IReadOnlyList<(ConditionalFormatPreset Preset, string Label)> ConditionalFormatPresetChoices =
        Enum.GetValues<ConditionalFormatPreset>()
            .Select(preset => (preset, ConditionalFormatPresetFactory.DisplayName(preset)))
            .ToList();

    /// <summary>The native Format-menu "Conditional Formatting" submenu: New Rule, quick presets, Manage.</summary>
    private NativeMenu CreateNativeConditionalFormatMenu()
    {
        var menu = new NativeMenu();

        var newRule = new NativeMenuItem { Header = UiText.Get("MainWindow_Header_NewRule") };
        newRule.Click += async (_, _) => await ShowConditionalFormatNewRuleDialogAsync();
        menu.Items.Add(newRule);
        menu.Items.Add(new NativeMenuItemSeparator());

        foreach (var preset in new[]
                 {
                     ConditionalFormatPreset.DataBar,
                     ConditionalFormatPreset.ColorScale,
                     ConditionalFormatPreset.IconSet,
                 })
        {
            var captured = preset;
            var item = new NativeMenuItem { Header = ConditionalFormatPresetFactory.DisplayName(captured) };
            item.Click += (_, _) => ApplyConditionalFormatPreset(captured);
            menu.Items.Add(item);
        }

        var greaterThan = new NativeMenuItem { Header = UiText.Get("ConditionalFormat_HighlightCellsGreaterThan") };
        greaterThan.Click += async (_, _) => await ApplyHighlightGreaterThanPresetAsync();
        menu.Items.Add(greaterThan);

        var top10 = new NativeMenuItem { Header = UiText.Get("ConditionalFormatDialog_RuleType_Top10Items") };
        top10.Click += (_, _) => ApplyConditionalFormatPreset(ConditionalFormatPreset.Top10);
        menu.Items.Add(top10);

        menu.Items.Add(new NativeMenuItemSeparator());

        var clear = new NativeMenuItem { Header = UiText.Get("ConditionalFormat_ClearRulesFromSelectedCells") };
        clear.Click += (_, _) => ClearConditionalFormatsFromSelection();
        menu.Items.Add(clear);

        var manage = new NativeMenuItem { Header = UiText.Get("MainWindow_Header_ManageRules") };
        manage.Click += async (_, _) => await ShowManageConditionalFormatsDialogAsync();
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
            UiText.Get("InsertLoc_CfGreaterThanTitle"),
            UiText.Get("InsertLoc_CfGreaterThanPrompt"),
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

    private Task<ConditionalFormat?> ShowConditionalFormatRuleEditorAsync(
        QuickAnalysisConditionalFormatDialogSeed seed) =>
        ShowConditionalFormatRuleEditorAsync(
            existingRule: null,
            startRuleType: seed.RuleType,
            initialSeed: seed);

    /// <summary>
    /// The compact rule editor: a rule-type dropdown plus per-type fields shown/hidden from
    /// <see cref="ConditionalFormatRuleSchema"/>, with inline validation from <c>Validate</c>. A preset
    /// dropdown seeds the value/visual families from <see cref="ConditionalFormatPresetFactory"/>. On OK,
    /// builds the Core rule (reusing the existing rule's id when editing). Returns null on cancel.
    /// </summary>
    private async Task<ConditionalFormat?> ShowConditionalFormatRuleEditorAsync(
        ConditionalFormat? existingRule,
        CfRuleType? startRuleType,
        QuickAnalysisConditionalFormatDialogSeed? initialSeed = null)
    {
        ConditionalFormat? result = null;
        var range = existingRule?.AppliesTo ?? _session.SelectedRange;

        var dialog = new Window
        {
            Title = existingRule is null ? UiText.Get("ConditionalFormat_NewRuleTitle") : UiText.Get("ConditionalFormat_EditRuleTitle"),
            Width = ConditionalFormatDialogCatalog.RuleEditorCaptureWidth,
            Height = ConditionalFormatDialogCatalog.RuleEditorCaptureHeight,
            MinWidth = ConditionalFormatDialogCatalog.RuleEditorMinWidth,
            MinHeight = ConditionalFormatDialogCatalog.RuleEditorMinHeight,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };
        AutomationProperties.SetAutomationId(dialog, "ConditionalFormatRuleDialog");

        var ruleTypeBox = new ComboBox
        {
            ItemsSource = ConditionalFormatDialogCatalog.RuleEditorTypeOptions
                .Select(option => UiText.Get(option.LabelKey))
                .ToList(),
            MinWidth = 220,
        };
        ApplyCfComboBoxChrome(ruleTypeBox);
        AutomationProperties.SetAutomationId(ruleTypeBox, "ConditionalFormatRuleTypeBox");
        AutomationProperties.SetName(ruleTypeBox, UiText.CreateAutomationName(UiText.Get("ConditionalFormat_RuleTypeLabel")));

        var presetBox = new ComboBox
        {
            ItemsSource = ConditionalFormatPresetChoices.Select(c => c.Label).ToList(),
            MinWidth = 220,
        };
        ApplyCfComboBoxChrome(presetBox);
        AutomationProperties.SetAutomationId(presetBox, "ConditionalFormatPresetBox");
        AutomationProperties.SetName(presetBox, UiText.CreateAutomationName(UiText.Get("ConditionalFormat_PresetLabel")));

        var operatorBox = new ComboBox
        {
            ItemsSource = ConditionalFormatDialogCatalog.RuleEditorOperatorOptions
                .Select(option => UiText.Get(option.LabelKey))
                .ToList(),
            SelectedIndex = 0,
            MinWidth = 220,
        };
        ApplyCfComboBoxChrome(operatorBox);
        AutomationProperties.SetAutomationId(operatorBox, "ConditionalFormatOperatorBox");

        var value1Box = new TextBox { MinWidth = 220 };
        ApplyCfTextBoxChrome(value1Box);
        AutomationProperties.SetAutomationId(value1Box, "ConditionalFormatValue1Box");
        var value2Box = new TextBox { MinWidth = 220 };
        ApplyCfTextBoxChrome(value2Box);
        AutomationProperties.SetAutomationId(value2Box, "ConditionalFormatValue2Box");
        var formulaBox = new TextBox { MinWidth = 220 };
        ApplyCfTextBoxChrome(formulaBox);
        AutomationProperties.SetAutomationId(formulaBox, "ConditionalFormatFormulaBox");
        var textBox = new TextBox { MinWidth = 220 };
        ApplyCfTextBoxChrome(textBox);
        AutomationProperties.SetAutomationId(textBox, "ConditionalFormatTextBox");
        var rankBox = new TextBox { MinWidth = 220, Text = "10" };
        ApplyCfTextBoxChrome(rankBox);
        AutomationProperties.SetAutomationId(rankBox, "ConditionalFormatRankBox");
        var percentBox = new CheckBox { Content = UiText.Get("ConditionalFormat_PercentOfRange") };
        ApplyCfCheckBoxChrome(percentBox);
        AutomationProperties.SetAutomationId(percentBox, "ConditionalFormatPercentBox");
        var topBottomBox = new ComboBox
        {
            ItemsSource = new[] { "Top", "Bottom" },
            SelectedIndex = 0,
            MinWidth = 220,
        };
        ApplyCfComboBoxChrome(topBottomBox);
        AutomationProperties.SetAutomationId(topBottomBox, "ConditionalFormatTopBottomBox");
        AutomationProperties.SetName(topBottomBox, UiText.CreateAutomationName(UiText.Get("ConditionalFormat_TopOrBottomLabel")));
        var iconSetBox = new ComboBox
        {
            ItemsSource = ConditionalFormatIconSetCatalog.Styles.Select(s => s.Style).ToList(),
            SelectedItem = ConditionalFormatIconSetCatalog.DefaultStyle,
            MinWidth = 220,
        };
        ApplyCfComboBoxChrome(iconSetBox);
        AutomationProperties.SetAutomationId(iconSetBox, "ConditionalFormatIconSetBox");
        var threeColorBox = new CheckBox { Content = UiText.Get("ConditionalFormat_UseThreeColorScale"), IsChecked = true };
        ApplyCfCheckBoxChrome(threeColorBox);
        AutomationProperties.SetAutomationId(threeColorBox, "ConditionalFormatThreeColorBox");

        var minColorBox = new TextBox { MinWidth = 220, Text = "99,190,123" };
        ApplyCfTextBoxChrome(minColorBox);
        AutomationProperties.SetAutomationId(minColorBox, "ConditionalFormatMinColorBox");
        var midColorBox = new TextBox { MinWidth = 220, Text = "255,235,132" };
        ApplyCfTextBoxChrome(midColorBox);
        AutomationProperties.SetAutomationId(midColorBox, "ConditionalFormatMidColorBox");
        var maxColorBox = new TextBox { MinWidth = 220, Text = "248,105,107" };
        ApplyCfTextBoxChrome(maxColorBox);
        AutomationProperties.SetAutomationId(maxColorBox, "ConditionalFormatMaxColorBox");

        var customFormatLabel = StripDisplayMnemonic(UiText.Get("ConditionalFormatDialog_FormatPreset_CustomFormat"));
        var highlightBox = new ComboBox
        {
            ItemsSource = ConditionalFormatHighlightPreset.Presets.Select(p => p.Label).Append(customFormatLabel).ToList(),
            SelectedIndex = 0,
            MinWidth = 220,
        };
        ApplyCfComboBoxChrome(highlightBox);
        AutomationProperties.SetAutomationId(highlightBox, "ConditionalFormatHighlightBox");

        // "Format…" opens a fill-colour picker that overrides the named preset with an explicit
        // custom style, mirroring Excel's New-Formatting-Rule Format button (and the WPF host).
        // For an edited rule, seed from its existing FormatIfTrue so the format round-trips.
        CellStyle? customFormatStyle = existingRule?.FormatIfTrue?.Clone();
        var formatButton = new Button { Content = StripDisplayMnemonic(UiText.Get("ConditionalFormatDialog_FormatButton")) };
        ApplyCfButtonChrome(formatButton, 84);
        AutomationProperties.SetAutomationId(formatButton, "ConditionalFormatFormatButton");
        formatButton.Click += async (_, _) =>
        {
            var presetFill = highlightBox.SelectedIndex >= 0 && highlightBox.SelectedIndex < ConditionalFormatHighlightPreset.Presets.Count
                ? ConditionalFormatHighlightPreset.Presets[highlightBox.SelectedIndex].FillColor
                : null;
            var initial = customFormatStyle?.FillColor ?? presetFill ?? new CellColor(255, 199, 206);
            var chosen = await ShowMoreColorsDialogAsync(
                StripDisplayMnemonic(UiText.Get("ConditionalFormatDialog_FormatButton")), initial);
            if (chosen is { } color)
            {
                customFormatStyle = new CellStyle { FillColor = color };
                highlightBox.SelectedItem = customFormatLabel;
            }
        };

        var operatorField = CreateDataValidationField(UiText.Get("ConditionalFormat_OperatorLabel"), operatorBox);
        var value1Field = CreateDataValidationField(UiText.Get("ConditionalFormat_ValueLabel"), value1Box);
        var value2Field = CreateDataValidationField(UiText.Get("ConditionalFormat_MaximumLabel"), value2Box);
        var formulaField = CreateDataValidationField(UiText.Get("ConditionalFormat_FormulaLabel"), formulaBox);
        var textField = CreateDataValidationField(UiText.Get("ConditionalFormat_TextLabel"), textBox);
        var rankField = CreateDataValidationField(UiText.Get("ConditionalFormat_RankOrPercentLabel"), rankBox);
        var topBottomField = CreateDataValidationField(UiText.Get("ConditionalFormat_TopOrBottomLabel"), topBottomBox);
        var iconSetField = CreateDataValidationField(UiText.Get("ConditionalFormat_IconSetStyleLabel"), iconSetBox);
        var minColorField = CreateDataValidationField(UiText.Get("ConditionalFormat_MinColorLabel"), minColorBox);
        var midColorField = CreateDataValidationField(UiText.Get("ConditionalFormat_MidColorLabel"), midColorBox);
        var maxColorField = CreateDataValidationField(UiText.Get("ConditionalFormat_MaxColorLabel"), maxColorBox);
        var highlightRow = new StackPanel
        {
            Spacing = 6,
            Children = { highlightBox, formatButton },
        };
        var highlightField = CreateDataValidationField(UiText.Get("ConditionalFormat_FormatLabel"), highlightRow);
        var presetField = CreateDataValidationField(UiText.Get("ConditionalFormat_PresetLabel"), presetBox);

        // The WPF editor gives its rule description controls the full right-column width. Keep the
        // same control instances and automation ids while letting the compact Avalonia column stretch.
        foreach (var control in new Control[]
                 {
                     ruleTypeBox, presetBox, operatorBox, value1Box, value2Box, formulaBox, textBox,
                     rankBox, topBottomBox, iconSetBox, minColorBox, midColorBox, maxColorBox, highlightBox,
                 })
        {
            control.HorizontalAlignment = AvaloniaHorizontalAlignment.Stretch;
            switch (control)
            {
                case ComboBox combo:
                    combo.Height = 21;
                    combo.MinHeight = 21;
                    combo.MaxHeight = 21;
                    break;
                case TextBox text:
                    text.Height = 20;
                    text.MinHeight = 20;
                    text.MaxHeight = 20;
                    break;
            }
        }
        formatButton.Height = 21;
        formatButton.MinHeight = 21;
        formatButton.MaxHeight = 21;

        var errorText = new TextBlock
        {
            Foreground = Brush(180, 30, 30),
            TextWrapping = TextWrapping.Wrap,
            IsVisible = false,
            FontSize = 12,
            FontFamily = FormulaBarFontFamily,
        };
        AutomationProperties.SetAutomationId(errorText, "ConditionalFormatErrorText");

        CfRuleType SelectedRuleType() =>
            ConditionalFormatDialogCatalog.RuleEditorTypeOptions[Math.Max(0, ruleTypeBox.SelectedIndex)].RuleType;

        CfRuleInput CollectInput()
        {
            var op = ConditionalFormatDialogCatalog.RuleEditorOperatorOptions[Math.Max(0, operatorBox.SelectedIndex)].Operator;
            return new CfRuleInput
            {
                RuleType = SelectedRuleType(),
                Operator = op,
                Value1 = value1Box.Text,
                Value2 = value2Box.Text,
                Formula = formulaBox.Text,
                Text = textBox.Text,
                Rank = rankBox.Text,
                IsPercent = percentBox.IsChecked == true,
                IsTop = topBottomBox.SelectedIndex <= 0,
                IconSetStyle = iconSetBox.SelectedItem as string,
                UseThreeColorScale = threeColorBox.IsChecked == true,
                MinColor = minColorBox.Text,
                MidColor = midColorBox.Text,
                MaxColor = maxColorBox.Text,
            };
        }

        void UpdateFieldVisibility()
        {
            var ruleType = SelectedRuleType();
            var schema = ConditionalFormatRuleSchema.ForRuleType(ruleType);
            operatorField.IsVisible = schema.HasField(CfInputField.Operator);
            value1Field.IsVisible = schema.HasField(CfInputField.Value1);
            var op = ConditionalFormatDialogCatalog.RuleEditorOperatorOptions[Math.Max(0, operatorBox.SelectedIndex)].Operator;
            value2Field.IsVisible = schema.HasField(CfInputField.Value2)
                && op is CfOperator.Between or CfOperator.NotBetween;
            formulaField.IsVisible = schema.HasField(CfInputField.Formula);
            textField.IsVisible = schema.HasField(CfInputField.Text);
            rankField.IsVisible = schema.HasField(CfInputField.Rank);
            percentBox.IsVisible = schema.HasField(CfInputField.Percent);
            topBottomField.IsVisible = schema.HasField(CfInputField.TopBottom);
            iconSetField.IsVisible = schema.HasField(CfInputField.IconSetStyle);
            threeColorBox.IsVisible = schema.HasField(CfInputField.UseThreeColorScale);
            var isColorScale = ruleType is CfRuleType.ColorScale;
            minColorField.IsVisible = isColorScale;
            midColorField.IsVisible = isColorScale && threeColorBox.IsChecked == true;
            maxColorField.IsVisible = isColorScale;
            // The highlight format only applies to the non-visual rule families.
            highlightField.IsVisible = ruleType
                is not (CfRuleType.IconSet or CfRuleType.DataBar or CfRuleType.ColorScale);
            // The preset ("Format Style") selector only belongs to the visual "format all cells
            // based on their values" family — Excel shows no preset row for Cell Value / text /
            // rank / formula rules (those use the explicit Format picker instead). Gating it here
            // removes the stray empty Preset dropdown that otherwise showed for every rule type.
            presetField.IsVisible = ruleType
                is CfRuleType.IconSet or CfRuleType.DataBar or CfRuleType.ColorScale;
            errorText.IsVisible = false;
        }

        ruleTypeBox.SelectionChanged += (_, _) => UpdateFieldVisibility();
        operatorBox.SelectionChanged += (_, _) => UpdateFieldVisibility();
        threeColorBox.IsCheckedChanged += (_, _) => UpdateFieldVisibility();

        presetBox.SelectionChanged += (_, _) =>
        {
            if (presetBox.SelectedIndex < 0)
                return;

            var preset = ConditionalFormatPresetChoices[presetBox.SelectedIndex].Preset;
            var presetInput = ConditionalFormatPresetFactory.BuildInput(preset);

            ApplyConditionalFormatPresetToEditor(
                presetInput,
                ruleTypeBox, operatorBox, value1Box, rankBox, percentBox, topBottomBox,
                iconSetBox, threeColorBox, minColorBox, midColorBox, maxColorBox);
            UpdateFieldVisibility();
        };

        SeedConditionalFormatEditor(
            existingRule, ruleTypeBox, operatorBox, value1Box, value2Box,
            formulaBox, textBox, rankBox, percentBox, topBottomBox, iconSetBox, threeColorBox,
            minColorBox, midColorBox, maxColorBox, highlightBox);

        // Reflect an edited rule's explicit FormatIfTrue in the Format dropdown: pick the matching
        // named preset when one exists, otherwise show the "Custom Format" sentinel (and keep the
        // seeded customFormatStyle so OK round-trips the exact style).
        if (existingRule?.FormatIfTrue is { } seededFormat)
        {
            var presets = ConditionalFormatHighlightPreset.Presets;
            var matchIndex = -1;
            for (var i = 0; i < presets.Count; i++)
            {
                var ps = presets[i].ToCellStyle();
                if (ps.FillColor == seededFormat.FillColor
                    && ps.FontColor == seededFormat.FontColor
                    && ps.Bold == seededFormat.Bold)
                {
                    matchIndex = i;
                    break;
                }
            }

            if (matchIndex >= 0)
            {
                highlightBox.SelectedIndex = matchIndex;
                customFormatStyle = null;
            }
            else
            {
                highlightBox.SelectedItem = customFormatLabel;
            }
        }

        if (existingRule is null && initialSeed is { } seed)
            ApplyConditionalFormatDialogSeed(
                seed,
                ruleTypeBox,
                operatorBox,
                value1Box,
                value2Box,
                textBox,
                rankBox,
                percentBox,
                topBottomBox);

        // Pre-select a starting rule type for new rules (e.g. the ribbon's "New Formula Rule…").
        if (existingRule is null && initialSeed is null && startRuleType is { } seedType)
        {
            var seedIndex = ConditionalFormatDialogCatalog.RuleEditorTypeOptions
                .ToList()
                .FindIndex(option => option.RuleType == seedType);
            if (seedIndex >= 0)
                ruleTypeBox.SelectedIndex = seedIndex;
        }

        UpdateFieldVisibility();

        // The Excel "Select a Rule Type:" list (left column). Selecting a row re-targets the rule-type
        // dropdown that drives the description editor on the right, keeping the two in sync.
        var ruleTypeShellList = new ListBox
        {
            ItemsSource = ConditionalFormatDialogCatalog.RuleEditorShellOptions
                .Select(option => UiText.Get(option.LabelKey).Replace("_", string.Empty, StringComparison.Ordinal))
                .ToList(),
            MinHeight = 182,
            Background = Brushes.White,
            BorderThickness = new Thickness(1),
            BorderBrush = Brush(171, 173, 179),
            FontSize = 12,
            FontFamily = FormulaBarFontFamily,
        };
        AutomationProperties.SetAutomationId(ruleTypeShellList, "ConditionalFormatRuleShellList");
        AutomationProperties.SetName(ruleTypeShellList, UiText.Get("ConditionalFormatDialog_RuleTypeAutomationName"));

        var syncingShell = false;

        void SyncShellFromRuleType()
        {
            if (syncingShell)
                return;
            syncingShell = true;
            ruleTypeShellList.SelectedIndex = ConditionalFormatDialogCatalog
                .RuleEditorShellIndexForModelRuleType(SelectedRuleType());
            syncingShell = false;
        }

        ruleTypeShellList.SelectionChanged += (_, _) =>
        {
            if (syncingShell || ruleTypeShellList.SelectedIndex < 0)
                return;

            var targetType = ConditionalFormatDialogCatalog
                .RuleEditorShellOptions[ruleTypeShellList.SelectedIndex]
                .RuleType;
            var idx = ConditionalFormatDialogCatalog.RuleEditorTypeOptions
                .ToList()
                .FindIndex(option => option.RuleType == targetType);
            if (idx >= 0 && idx != ruleTypeBox.SelectedIndex)
            {
                syncingShell = true;
                ruleTypeBox.SelectedIndex = idx;
                syncingShell = false;
                UpdateFieldVisibility();
            }
        };
        ruleTypeBox.SelectionChanged += (_, _) => SyncShellFromRuleType();
        SyncShellFromRuleType();

        var okButton = new Button { Content = UiText.Get("Common_Ok"), IsDefault = true, MinWidth = 84 };
        ApplyCfButtonChrome(okButton, 84, isDefault: true);
        okButton.Height = 21;
        okButton.MinHeight = 21;
        okButton.MaxHeight = 21;
        AutomationProperties.SetAutomationId(okButton, "ConditionalFormatOkButton");
        var cancelButton = new Button { Content = UiText.Get("Common_Cancel"), IsCancel = true, MinWidth = 84 };
        ApplyCfButtonChrome(cancelButton, 84);
        cancelButton.Height = 21;
        cancelButton.MinHeight = 21;
        cancelButton.MaxHeight = 21;
        AutomationProperties.SetAutomationId(cancelButton, "ConditionalFormatCancelButton");

        okButton.Click += (_, _) =>
        {
            var input = CollectInput();
            var isCustomFormat = highlightBox.SelectedItem as string == customFormatLabel;
            // The "Custom Format" sentinel sits past the preset list; clamp so the preset index stays valid.
            var presetIndex = Math.Max(0, Math.Min(highlightBox.SelectedIndex, ConditionalFormatHighlightPreset.Presets.Count - 1));
            var highlight = ConditionalFormatHighlightPreset.Presets[presetIndex];
            var build = ConditionalFormatRuleBuilder.TryBuildApplyCommand(
                input, _session.ActiveSheet.Id, range, highlight, existingRule?.Id,
                isCustomFormat ? customFormatStyle : null, existingRule);
            if (!build.IsValid)
            {
                errorText.Text = string.Join("\n", build.Validation.Errors.Select(e => e.Message));
                errorText.IsVisible = true;
                return;
            }

            result = build.Rule;
            dialog.Close();
        };
        cancelButton.Click += (_, _) => dialog.Close();

        // WPF button order: [OK][Cancel] — primary on left
        var buttonRow = AvaloniaCompactDialogChrome.CreateActionRow([okButton, cancelButton], new Thickness(0, 10, 0, 0));
        DockPanel.SetDock(buttonRow, Dock.Bottom);

        // Left column: "Select a Rule Type:" header + the Excel rule-type list.
        var leftColumn = new StackPanel
        {
            Width = 218,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 12, 0),
            Children =
            {
                new TextBlock
                {
                    Text = UiText.Get("ConditionalFormatDialog_SelectRuleTypeHeader").Replace("_", string.Empty, StringComparison.Ordinal),
                    FontWeight = FontWeight.SemiBold,
                    Margin = new Thickness(0, 0, 0, 6),
                    FontSize = 12,
                    FontFamily = FormulaBarFontFamily,
                },
                ruleTypeShellList,
            },
        };
        AvaloniaGrid.SetColumn(leftColumn, 0);

        // Right column: "Edit the Rule Description:" header, the per-type description editor, and buttons.
        var descriptionEditor = new ScrollViewer
        {
            Content = new StackPanel
            {
                Margin = new Thickness(0, 12, 0, 0),
                Spacing = 8,
                Children =
                {
                    CreateDataValidationField(UiText.Get("ConditionalFormatDialog_FormatOnlyCellsWithLabel"), ruleTypeBox),
                    presetField,
                    operatorField,
                    value1Field,
                    value2Field,
                    formulaField,
                    textField,
                    rankField,
                    percentBox,
                    topBottomField,
                    iconSetField,
                    threeColorBox,
                    minColorField,
                    midColorField,
                    maxColorField,
                    highlightField,
                    errorText,
                },
            },
        };

        var rightColumn = new DockPanel
        {
            Children =
            {
                new TextBlock
                {
                    Text = UiText.Get("ConditionalFormatDialog_EditRuleDescriptionHeader").Replace("_", string.Empty, StringComparison.Ordinal),
                    FontWeight = FontWeight.SemiBold,
                    Margin = new Thickness(0, 0, 0, 6),
                    FontSize = 12,
                    FontFamily = FormulaBarFontFamily,
                    [DockPanel.DockProperty] = Dock.Top,
                },
                buttonRow,
                descriptionEditor,
            },
        };
        AvaloniaGrid.SetColumn(rightColumn, 1);

        var root = new AvaloniaGrid
        {
            Margin = new Thickness(16, 16, 29, 29),
            ColumnDefinitions = new ColumnDefinitions("244,*"),
            Children =
            {
                leftColumn,
                rightColumn,
            },
        };
        ConfigureDialogTabCycle(dialog, root);
        ConfigureNativeDialogInitialFocus(dialog, root, ruleTypeBox);
        dialog.Content = root;

        AttachOptionalConditionalFormatRuleDialogObservation(
            dialog,
            ruleTypeBox,
            presetBox,
            operatorBox,
            value1Box,
            formulaBox,
            textBox,
            rankBox,
            topBottomBox,
            iconSetBox,
            minColorBox,
            maxColorBox,
            highlightBox,
            okButton,
            cancelButton);

        await dialog.ShowDialog(this);
        return result;
    }

    /// <summary>Seeds the editor controls from an existing rule (edit), or leaves defaults (new).</summary>
    private static void SeedConditionalFormatEditor(
        ConditionalFormat? rule,
        ComboBox ruleTypeBox,
        ComboBox operatorBox,
        TextBox value1Box,
        TextBox value2Box,
        TextBox formulaBox,
        TextBox textBox,
        TextBox rankBox,
        CheckBox percentBox,
        ComboBox topBottomBox,
        ComboBox iconSetBox,
        CheckBox threeColorBox,
        TextBox minColorBox,
        TextBox midColorBox,
        TextBox maxColorBox,
        ComboBox highlightBox)
    {
        if (rule is null)
        {
            ruleTypeBox.SelectedIndex = 0;
            return;
        }

        var typeIndex = 0;
        for (var i = 0; i < ConditionalFormatDialogCatalog.RuleEditorTypeOptions.Count; i++)
            if (ConditionalFormatDialogCatalog.RuleEditorTypeOptions[i].RuleType == rule.RuleType)
            {
                typeIndex = i;
                break;
            }

        ruleTypeBox.SelectedIndex = typeIndex;

        for (var i = 0; i < ConditionalFormatDialogCatalog.RuleEditorOperatorOptions.Count; i++)
            if (ConditionalFormatDialogCatalog.RuleEditorOperatorOptions[i].Operator == rule.Operator)
            {
                operatorBox.SelectedIndex = i;
                break;
            }

        value1Box.Text = rule.Value1 ?? string.Empty;
        value2Box.Text = rule.Value2 ?? string.Empty;
        formulaBox.Text = string.IsNullOrEmpty(rule.FormulaText) ? string.Empty : "=" + rule.FormulaText;
        textBox.Text = rule.TextRuleText ?? rule.DateOccurringPeriod ?? string.Empty;
        rankBox.Text = rule.TopBottomRank.ToString(System.Globalization.CultureInfo.InvariantCulture);
        percentBox.IsChecked = rule.TopBottomPercent;
        topBottomBox.SelectedIndex = rule.AboveAverage ? 0 : 1;
        if (!string.IsNullOrEmpty(rule.IconSetStyle))
            iconSetBox.SelectedItem = rule.IconSetStyle;
        threeColorBox.IsChecked = rule.UseThreeColorScale;
        minColorBox.Text = FormatRgb(rule.MinColor);
        midColorBox.Text = FormatRgb(rule.MidColor);
        maxColorBox.Text = FormatRgb(rule.MaxColor);
        highlightBox.SelectedIndex = 0;
    }

    /// <summary>Seeds the editor's value/visual controls from a quick-preset input.</summary>
    private static void ApplyConditionalFormatPresetToEditor(
        CfRuleInput preset,
        ComboBox ruleTypeBox,
        ComboBox operatorBox,
        TextBox value1Box,
        TextBox rankBox,
        CheckBox percentBox,
        ComboBox topBottomBox,
        ComboBox iconSetBox,
        CheckBox threeColorBox,
        TextBox minColorBox,
        TextBox midColorBox,
        TextBox maxColorBox)
    {
        for (var i = 0; i < ConditionalFormatDialogCatalog.RuleEditorTypeOptions.Count; i++)
            if (ConditionalFormatDialogCatalog.RuleEditorTypeOptions[i].RuleType == preset.RuleType)
            {
                ruleTypeBox.SelectedIndex = i;
                break;
            }

        for (var i = 0; i < ConditionalFormatDialogCatalog.RuleEditorOperatorOptions.Count; i++)
            if (ConditionalFormatDialogCatalog.RuleEditorOperatorOptions[i].Operator == preset.Operator)
            {
                operatorBox.SelectedIndex = i;
                break;
            }

        if (!string.IsNullOrWhiteSpace(preset.Value1))
            value1Box.Text = preset.Value1;
        if (!string.IsNullOrWhiteSpace(preset.Rank))
            rankBox.Text = preset.Rank;
        percentBox.IsChecked = preset.IsPercent;
        topBottomBox.SelectedIndex = preset.IsTop ? 0 : 1;
        if (!string.IsNullOrWhiteSpace(preset.IconSetStyle))
            iconSetBox.SelectedItem = preset.IconSetStyle;
        threeColorBox.IsChecked = preset.UseThreeColorScale;
        if (!string.IsNullOrWhiteSpace(preset.MinColor))
            minColorBox.Text = preset.MinColor;
        if (!string.IsNullOrWhiteSpace(preset.MidColor))
            midColorBox.Text = preset.MidColor;
        if (!string.IsNullOrWhiteSpace(preset.MaxColor))
            maxColorBox.Text = preset.MaxColor;
    }

    private static void ApplyConditionalFormatDialogSeed(
        QuickAnalysisConditionalFormatDialogSeed seed,
        ComboBox ruleTypeBox,
        ComboBox operatorBox,
        TextBox value1Box,
        TextBox value2Box,
        TextBox textBox,
        TextBox rankBox,
        CheckBox percentBox,
        ComboBox topBottomBox)
    {
        var ruleTypeIndex = ConditionalFormatDialogCatalog.RuleEditorTypeOptions
            .ToList()
            .FindIndex(option => option.RuleType == seed.RuleType);
        if (ruleTypeIndex >= 0)
            ruleTypeBox.SelectedIndex = ruleTypeIndex;

        var operatorIndex = ConditionalFormatDialogCatalog.RuleEditorOperatorOptions
            .ToList()
            .FindIndex(option => option.Operator == seed.Operator);
        if (operatorIndex >= 0)
            operatorBox.SelectedIndex = operatorIndex;

        value1Box.Text = seed.Value1 ?? string.Empty;
        value2Box.Text = seed.Value2 ?? string.Empty;
        textBox.Text = seed.Text ?? seed.DateOccurringPeriod ?? string.Empty;
        rankBox.Text = seed.TopBottomRank.ToString(System.Globalization.CultureInfo.InvariantCulture);
        percentBox.IsChecked = seed.TopBottomPercent;
        topBottomBox.SelectedIndex = seed.IsTop ? 0 : 1;
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
            Width = 360,
            Height = 180,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };
        AutomationProperties.SetAutomationId(dialog, "ConditionalFormatValuePromptDialog");

        var valueBox = new TextBox { Text = initial, MinWidth = 240 };
        ApplyCfTextBoxChrome(valueBox);
        AutomationProperties.SetAutomationId(valueBox, "ConditionalFormatValuePromptBox");

        var okButton = new Button { Content = UiText.Get("InsertLoc_OkButton"), IsDefault = true, MinWidth = 84 };
        ApplyCfButtonChrome(okButton, 84, isDefault: true);
        var cancelButton = new Button { Content = UiText.Get("InsertLoc_CancelButton"), IsCancel = true, MinWidth = 84 };
        ApplyCfButtonChrome(cancelButton, 84);
        okButton.Click += (_, _) =>
        {
            result = valueBox.Text ?? string.Empty;
            dialog.Close();
        };
        cancelButton.Click += (_, _) => dialog.Close();

        // WPF button order: [OK][Cancel] — primary on left
        var buttonRow = AvaloniaCompactDialogChrome.CreateActionRow([okButton, cancelButton], new Thickness(0, 10, 0, 0));
        DockPanel.SetDock(buttonRow, Dock.Bottom);

        dialog.Content = new DockPanel
        {
            Margin = new Thickness(16),
            Children =
            {
                buttonRow,
                new StackPanel
                {
                    Spacing = 8,
                    Children = { new TextBlock { Text = prompt, TextWrapping = TextWrapping.Wrap, FontSize = 12, FontFamily = FormulaBarFontFamily }, valueBox },
                },
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
            Width = 560,
            Height = 420,
            MinWidth = 480,
            MinHeight = 360,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };
        AutomationProperties.SetAutomationId(dialog, "ManageConditionalFormatsDialog");
        var manageDialogChrome = ConditionalFormatDialogChromeStyle with { ButtonHeight = 22 };

        var listBox = new ListBox
        {
            MinHeight = 210,
            Background = Brushes.White,
            BorderThickness = new Thickness(0),
        };
        AvaloniaCompactDialogChrome.ApplyListBox(
            listBox,
            manageDialogChrome with { ListBoxItemPadding = new Thickness(2, 0) });
        AutomationProperties.SetAutomationId(listBox, "ManageConditionalFormatsListBox");
        AutomationProperties.SetName(listBox, UiText.Get("ManageConditionalFormats_ConditionalFormattingRules"));
        // Render each rule as a #/Rule-type/Format-swatch/Applies-to/Stop-if row matching the header
        // columns (the WPF GridView), instead of the default single-string row. Toggling Stop If
        // True mutates the matching rule directly in the working copy (mirroring the WPF grid's
        // two-way-bound checkbox column) — it never touches the live sheet until Commit().
        listBox.ItemTemplate = new FuncDataTemplate<ManageConditionalFormatRuleProjection>(
            (item, _) => BuildManageConditionalFormatRow(item, isChecked =>
            {
                manageSession.SetStopIfTrue(item.Id, isChecked);
            }),
            supportsRecycling: true);

        var emptyText = new TextBlock
        {
            Text = UiText.Get("ConditionalFormat_NoRules"),
            Foreground = HeaderForeground,
            IsVisible = false,
            FontSize = 12,
            FontFamily = FormulaBarFontFamily,
        };

        var appliesToBox = new TextBox { MinWidth = 160 };
        ApplyCfTextBoxChrome(appliesToBox);
        AutomationProperties.SetAutomationId(appliesToBox, "ManageConditionalFormatsAppliesToBox");
        AutomationProperties.SetName(appliesToBox, UiText.Get("ManageConditionalFormats_AppliesToColumn"));

        var appliesToPicker = new Button
        {
            Content = "...",
            Width = 32,
            MinWidth = 32,
            Margin = new Thickness(6, 0, 0, 0),
        };
        ApplyCfButtonChrome(appliesToPicker, 32);
        AutomationProperties.SetAutomationId(appliesToPicker, "ManageConditionalFormatsAppliesToPickerButton");
        AutomationProperties.SetName(appliesToPicker, UiText.Get("ConditionalFormat_SelectRangeAutomationName"));

        var appliesToRow = new AvaloniaGrid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            IsVisible = false,
            Margin = new Thickness(0, 4, 0, 0),
            [DockPanel.DockProperty] = Dock.Bottom,
        };
        appliesToRow.Children.Add(appliesToBox);
        AvaloniaGrid.SetColumn(appliesToPicker, 1);
        appliesToRow.Children.Add(appliesToPicker);

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

        void Reload(Guid? selectId = null)
        {
            var scope = CurrentScope();
            manageSession.SetScope(scope);
            var items = manageSession.BuildProjection();
            listBox.ItemsSource = items;
            emptyText.IsVisible = items.Count == 0;
            if (items.Count > 0)
            {
                var index = 0;
                if (selectId is { } id)
                    for (var i = 0; i < items.Count; i++)
                        if (items[i].Id == id)
                        {
                            index = i;
                            break;
                        }

                listBox.SelectedIndex = index;
            }

            SyncCommandState();
        }

        void SyncAppliesTo()
        {
            appliesToBox.Text = listBox.SelectedItem is ManageConditionalFormatRuleProjection item
                ? FormatRangeReference(item.Rule.AppliesTo)
                : string.Empty;
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
        AutomationProperties.SetAutomationId(moveUpButton, "ManageConditionalFormatsMoveUpButton");
        AutomationProperties.SetName(moveUpButton, UiText.Get("ManageConditionalFormats_MoveUp"));
        ToolTip.SetTip(moveUpButton, UiText.Get("ManageConditionalFormats_MoveSelectedRuleUp"));
        var moveDownButton = new Button { Content = "\u25BC", Width = 32, IsEnabled = false };
        ApplyCfButtonChrome(moveDownButton, 32, style: manageDialogChrome);
        AutomationProperties.SetAutomationId(moveDownButton, "ManageConditionalFormatsMoveDownButton");
        AutomationProperties.SetName(moveDownButton, UiText.Get("ManageConditionalFormats_MoveDown"));
        ToolTip.SetTip(moveDownButton, UiText.Get("ManageConditionalFormats_MoveSelectedRuleDown"));
        var applyAppliesToButton = new Button { Content = UiText.Get("ManageConditionalFormats_Apply"), Width = 72 };
        ApplyCfButtonChrome(applyAppliesToButton, 72, style: manageDialogChrome);
        AutomationProperties.SetAutomationId(applyAppliesToButton, "ManageConditionalFormatsApplyAppliesToButton");
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
            applyAppliesToButton.IsEnabled = hasSelection;
            appliesToRow.IsVisible = hasSelection;
            appliesToPicker.IsEnabled = hasSelection;
        }

        listBox.SelectionChanged += (_, _) =>
        {
            SyncAppliesTo();
            SyncCommandState();
        };
        scopeBox.SelectionChanged += (_, _) => Reload();

        newButton.Click += async (_, _) =>
        {
            var built = await ShowConditionalFormatRuleEditorAsync(existingRule: null);
            if (built is null)
                return;

            // Append to the working copy only — nothing reaches the live sheet until Commit().
            manageSession.Add(built);
            Reload(built.Id);
        };

        editButton.Click += async (_, _) =>
        {
            if (listBox.SelectedItem is not ManageConditionalFormatRuleProjection item)
                return;

            var edited = await ShowConditionalFormatRuleEditorAsync(item.Rule);
            if (edited is null)
                return;

            if (!manageSession.Replace(edited))
                return;

            Reload(edited.Id);
        };

        deleteButton.Click += (_, _) =>
        {
            if (listBox.SelectedItem is not ManageConditionalFormatRuleProjection item)
                return;

            if (!manageSession.Delete(item.Id))
                return;

            Reload();
        };

        duplicateButton.Click += async (_, _) =>
        {
            if (listBox.SelectedItem is not ManageConditionalFormatRuleProjection item)
                return;

            var duplicateId = Guid.NewGuid();
            if (!manageSession.Duplicate(item.Id, duplicateId))
                return;

            Reload(duplicateId);
        };

        void Move(ConditionalFormatRuleMoveDirection direction)
        {
            if (listBox.SelectedItem is not ManageConditionalFormatRuleProjection item)
                return;

            if (!manageSession.Move(item.Id, direction))
                return;

            Reload(item.Id);
        }

        moveUpButton.Click += (_, _) => Move(ConditionalFormatRuleMoveDirection.Up);
        moveDownButton.Click += (_, _) => Move(ConditionalFormatRuleMoveDirection.Down);

        applyAppliesToButton.Click += (_, _) =>
        {
            if (listBox.SelectedItem is not ManageConditionalFormatRuleProjection item)
                return;

            var reference = ManageConditionalFormatsPlanner.NormalizeAppliesToText(appliesToBox.Text);
            if (string.IsNullOrWhiteSpace(reference)
                || !_session.TryResolveReferenceRange(reference, out var range))
            {
                ShowEditIssue(UiText.Get(ConditionalFormatCommandPlanner.InvalidAppliesToResourceKey));
                return;
            }

            if (!manageSession.ApplyRange(item.Id, range))
                return;

            Reload(item.Id);
        };

        void Commit()
        {
            // A single atomic replace-all: one undo step for every New/Edit/Delete/Duplicate/Move/
            // AppliesTo/Stop-If-True edit made in this dialog session.
            RunConditionalFormatCommand(manageSession.CreateApplyPlan(
                [_session.ActiveSheet.Id],
                _session.ActiveSheet.Id));
        }

        closeButton.Click += (_, _) =>
        {
            Commit();
            dialog.Close();
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
            Children = { closeButton, cancelButton, applyAppliesToButton },
        };
        DockPanel.SetDock(buttonRow, Dock.Bottom);

        static TextBlock HeaderCell(string text, int column) =>
            new()
            {
                Text = text,
                FontWeight = FontWeight.SemiBold,
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

        Reload();
        SyncAppliesTo();
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
                        appliesToRow,
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
            appliesToBox,
            newButton,
            editButton,
            deleteButton,
            moveUpButton,
            moveDownButton,
            applyAppliesToButton,
            appliesToRow,
            closeButton);

        AttachDialogRangePicker(
            dialog,
            appliesToPicker,
            appliesToBox,
            "range.conditional-format.applies-to");

        await dialog.ShowDialog(this);
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
        Action<bool> onStopIfTrueToggled)
    {
        var rule = item.Rule;
        var grid = new AvaloniaGrid
        {
            ColumnDefinitions = new ColumnDefinitions(ManageCfRuleColumns),
            Height = 22,
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
        AddCell(RowText(FormatRangeReference(rule.AppliesTo)), 3);
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

    /// <summary>A compact preview of a rule's effect for the Format column (mirrors the WPF swatch).</summary>
    private Control BuildConditionalFormatPreviewSwatch(ConditionalFormat rule)
    {
        static IBrush RgbBrush(RgbColor c) => new SolidColorBrush(Color.FromRgb(c.R, c.G, c.B));
        static IBrush CellBrush(CellColor c) => new SolidColorBrush(Color.FromRgb(c.R, c.G, c.B));

        var swatch = new Border
        {
            Width = 78,
            Height = 16,
            Margin = new Thickness(3, 0),
            BorderBrush = Brush(150, 150, 150),
            BorderThickness = new Thickness(0.5),
            Background = Brushes.White,
        };

        switch (rule.RuleType)
        {
            case CfRuleType.DataBar:
                swatch.Child = new Border
                {
                    Background = RgbBrush(rule.DataBarColor),
                    Width = 46,
                    Margin = new Thickness(1),
                    HorizontalAlignment = AvaloniaHorizontalAlignment.Left,
                };
                break;
            case CfRuleType.ColorScale:
                var gradient = new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative),
                    EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative),
                };
                gradient.GradientStops.Add(new GradientStop(Color.FromRgb(rule.MinColor.R, rule.MinColor.G, rule.MinColor.B), 0));
                if (rule.UseThreeColorScale)
                    gradient.GradientStops.Add(new GradientStop(Color.FromRgb(rule.MidColor.R, rule.MidColor.G, rule.MidColor.B), 0.5));
                gradient.GradientStops.Add(new GradientStop(Color.FromRgb(rule.MaxColor.R, rule.MaxColor.G, rule.MaxColor.B), 1));
                swatch.Background = gradient;
                break;
            case CfRuleType.IconSet:
                swatch.Child = new TextBlock
                {
                    Text = "▲ ◆ ▼",
                    FontSize = 9,
                    HorizontalAlignment = AvaloniaHorizontalAlignment.Center,
                    VerticalAlignment = AvaloniaVerticalAlignment.Center,
                };
                break;
            default:
                var format = rule.FormatIfTrue;
                if (format?.FillColor is { } fill)
                    swatch.Background = CellBrush(fill);
                swatch.Child = new TextBlock
                {
                    Text = UiText.Get("ManageConditionalFormats_FormatPreviewSample"),
                    FontSize = 10,
                    HorizontalAlignment = AvaloniaHorizontalAlignment.Center,
                    VerticalAlignment = AvaloniaVerticalAlignment.Center,
                    Foreground = format is null ? Brushes.Black : CellBrush(format.FontColor),
                    FontWeight = format?.Bold == true ? FontWeight.Bold : FontWeight.Normal,
                    FontStyle = format?.Italic == true ? global::Avalonia.Media.FontStyle.Italic : global::Avalonia.Media.FontStyle.Normal,
                    TextDecorations = format?.Underline == true
                        ? CreateTextDecorations(global::Avalonia.Media.TextDecorationLocation.Underline)
                        : null,
                };
                break;
        }

        return swatch;
    }
}
