using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

using FreeX.App.Avalonia.Dialogs;
using FreeX.App.Presentation.Dialogs;
using FreeX.Core.Commands;
using FreeX.Core.Model;

using AvaloniaHorizontalAlignment = Avalonia.Layout.HorizontalAlignment;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    /// <summary>The rule types the Avalonia conditional-format editor exposes, in dropdown order.</summary>
    private static readonly IReadOnlyList<(CfRuleType Type, string Label)> ConditionalFormatRuleTypeChoices =
    [
        (CfRuleType.CellValue, "Cell Value"),
        (CfRuleType.Formula, "Formula"),
        (CfRuleType.Top10, "Top 10"),
        (CfRuleType.IconSet, "Icon Set"),
        (CfRuleType.DataBar, "Data Bar"),
        (CfRuleType.ColorScale, "Color Scale"),
        (CfRuleType.DuplicateValues, "Duplicate Values"),
        (CfRuleType.UniqueValues, "Unique Values"),
        (CfRuleType.AboveAverage, "Above Average"),
    ];

    private static readonly IReadOnlyList<(CfOperator Op, string Label)> ConditionalFormatOperatorChoices =
    [
        (CfOperator.GreaterThan, "greater than"),
        (CfOperator.LessThan, "less than"),
        (CfOperator.GreaterThanOrEqual, "greater than or equal to"),
        (CfOperator.LessThanOrEqual, "less than or equal to"),
        (CfOperator.Equal, "equal to"),
        (CfOperator.NotEqual, "not equal to"),
        (CfOperator.Between, "between"),
        (CfOperator.NotBetween, "not between"),
    ];

    /// <summary>The native Format-menu "Conditional Formatting" submenu: New Rule, quick presets, Manage.</summary>
    private NativeMenu CreateNativeConditionalFormatMenu()
    {
        var menu = new NativeMenu();

        var newRule = new NativeMenuItem { Header = "New Rule..." };
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

        var greaterThan = new NativeMenuItem { Header = "Highlight Cells > Greater Than..." };
        greaterThan.Click += async (_, _) => await ApplyHighlightGreaterThanPresetAsync();
        menu.Items.Add(greaterThan);

        var top10 = new NativeMenuItem { Header = "Top 10 Items" };
        top10.Click += (_, _) => ApplyConditionalFormatPreset(ConditionalFormatPreset.Top10);
        menu.Items.Add(top10);

        menu.Items.Add(new NativeMenuItemSeparator());

        var clear = new NativeMenuItem { Header = "Clear Rules from Selected Cells" };
        clear.Click += (_, _) => ClearConditionalFormatsFromSelection();
        menu.Items.Add(clear);

        var manage = new NativeMenuItem { Header = "Manage Rules..." };
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
        var command = ConditionalFormatPresetFactory.BuildApplyCommand(preset, _session.ActiveSheet.Id, range);
        RunConditionalFormatCommand(
            command,
            $"Applied {ConditionalFormatPresetFactory.DisplayName(preset)} to {FormatRangeReference(range)}");
    }

    /// <summary>Prompts for a threshold and applies the Highlight &gt; Greater Than preset.</summary>
    private async Task ApplyHighlightGreaterThanPresetAsync()
    {
        if (!TryCommitPendingFormulaEdit())
            return;

        var value = await ShowConditionalFormatValuePromptAsync(
            "Greater Than",
            "Format cells that are GREATER THAN:",
            "0");
        if (value is null)
            return;

        var range = _session.SelectedRange;
        var command = ConditionalFormatPresetFactory.BuildApplyCommand(
            ConditionalFormatPreset.HighlightGreaterThan,
            _session.ActiveSheet.Id,
            range,
            value);
        RunConditionalFormatCommand(command, $"Applied highlight rule to {FormatRangeReference(range)}");
    }

    /// <summary>Clears every conditional-format rule overlapping the current selection (one undo step).</summary>
    private void ClearConditionalFormatsFromSelection()
    {
        if (!TryCommitPendingFormulaEdit())
            return;

        var range = _session.SelectedRange;
        RunConditionalFormatCommand(
            new ClearConditionalFormatsCommand(_session.ActiveSheet.Id, range),
            $"Cleared conditional formatting from {FormatRangeReference(range)}");
    }

    /// <summary>Runs a conditional-format command through the shared session command path and refreshes.</summary>
    private void RunConditionalFormatCommand(IWorkbookCommand command, string successStatus)
    {
        var result = _session.ExecuteReviewCommand(command);
        if (!result.Success)
        {
            ShowEditIssue(result.ErrorMessage ?? "Conditional Formatting failed.");
            return;
        }

        RefreshShell(successStatus);
    }

    /// <summary>Shows the rule editor for a new rule and applies the built Core rule to the selection.</summary>
    private async Task ShowConditionalFormatNewRuleDialogAsync()
    {
        if (!TryCommitPendingFormulaEdit())
            return;

        var built = await ShowConditionalFormatRuleEditorAsync(existingRule: null);
        if (built is null)
            return;

        var range = _session.SelectedRange;
        RunConditionalFormatCommand(
            ConditionalFormatRuleBuilder.ToApplyCommand(_session.ActiveSheet.Id, built),
            $"Applied conditional formatting to {FormatRangeReference(range)}");
    }

    /// <summary>
    /// The compact rule editor: a rule-type dropdown plus per-type fields shown/hidden from
    /// <see cref="ConditionalFormatRuleSchema"/>, with inline validation from <c>Validate</c>. On OK,
    /// builds the Core rule (reusing the existing rule's id when editing). Returns null on cancel.
    /// </summary>
    private async Task<ConditionalFormat?> ShowConditionalFormatRuleEditorAsync(ConditionalFormat? existingRule)
    {
        ConditionalFormat? result = null;
        var range = existingRule?.AppliesTo ?? _session.SelectedRange;

        var dialog = new Window
        {
            Title = existingRule is null ? "New Formatting Rule" : "Edit Formatting Rule",
            Width = 460,
            Height = 420,
            MinWidth = 420,
            MinHeight = 360,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };
        AutomationProperties.SetAutomationId(dialog, "ConditionalFormatRuleDialog");

        var ruleTypeBox = new ComboBox
        {
            ItemsSource = ConditionalFormatRuleTypeChoices.Select(c => c.Label).ToList(),
            MinWidth = 220,
        };
        AutomationProperties.SetAutomationId(ruleTypeBox, "ConditionalFormatRuleTypeBox");

        var operatorBox = new ComboBox
        {
            ItemsSource = ConditionalFormatOperatorChoices.Select(c => c.Label).ToList(),
            SelectedIndex = 0,
            MinWidth = 220,
        };
        AutomationProperties.SetAutomationId(operatorBox, "ConditionalFormatOperatorBox");

        var value1Box = new TextBox { MinWidth = 220 };
        AutomationProperties.SetAutomationId(value1Box, "ConditionalFormatValue1Box");
        var value2Box = new TextBox { MinWidth = 220 };
        AutomationProperties.SetAutomationId(value2Box, "ConditionalFormatValue2Box");
        var formulaBox = new TextBox { MinWidth = 220 };
        AutomationProperties.SetAutomationId(formulaBox, "ConditionalFormatFormulaBox");
        var textBox = new TextBox { MinWidth = 220 };
        AutomationProperties.SetAutomationId(textBox, "ConditionalFormatTextBox");
        var rankBox = new TextBox { MinWidth = 220, Text = "10" };
        AutomationProperties.SetAutomationId(rankBox, "ConditionalFormatRankBox");
        var percentBox = new CheckBox { Content = "% of range" };
        AutomationProperties.SetAutomationId(percentBox, "ConditionalFormatPercentBox");
        var iconSetBox = new ComboBox
        {
            ItemsSource = ConditionalFormatIconSetCatalog.Styles.Select(s => s.Style).ToList(),
            SelectedItem = ConditionalFormatIconSetCatalog.DefaultStyle,
            MinWidth = 220,
        };
        AutomationProperties.SetAutomationId(iconSetBox, "ConditionalFormatIconSetBox");
        var threeColorBox = new CheckBox { Content = "Use three-color scale", IsChecked = true };
        AutomationProperties.SetAutomationId(threeColorBox, "ConditionalFormatThreeColorBox");

        var highlightBox = new ComboBox
        {
            ItemsSource = ConditionalFormatHighlightPreset.Presets.Select(p => p.Label).ToList(),
            SelectedIndex = 0,
            MinWidth = 220,
        };
        AutomationProperties.SetAutomationId(highlightBox, "ConditionalFormatHighlightBox");

        var operatorField = CreateDataValidationField("Operator", operatorBox);
        var value1Field = CreateDataValidationField("Value", value1Box);
        var value2Field = CreateDataValidationField("Maximum", value2Box);
        var formulaField = CreateDataValidationField("Formula (e.g. =A1>10)", formulaBox);
        var textField = CreateDataValidationField("Text", textBox);
        var rankField = CreateDataValidationField("Rank or percent", rankBox);
        var iconSetField = CreateDataValidationField("Icon set style", iconSetBox);
        var highlightField = CreateDataValidationField("Format", highlightBox);

        var errorText = new TextBlock
        {
            Foreground = Brush(180, 30, 30),
            TextWrapping = TextWrapping.Wrap,
            IsVisible = false,
        };
        AutomationProperties.SetAutomationId(errorText, "ConditionalFormatErrorText");

        CfRuleType SelectedRuleType() =>
            ConditionalFormatRuleTypeChoices[Math.Max(0, ruleTypeBox.SelectedIndex)].Type;

        CfRuleInput CollectInput()
        {
            var op = ConditionalFormatOperatorChoices[Math.Max(0, operatorBox.SelectedIndex)].Op;
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
                IconSetStyle = iconSetBox.SelectedItem as string,
                UseThreeColorScale = threeColorBox.IsChecked == true,
                MinColor = "99,190,123",
                MidColor = "255,235,132",
                MaxColor = "248,105,107",
            };
        }

        void UpdateFieldVisibility()
        {
            var schema = ConditionalFormatRuleSchema.ForRuleType(SelectedRuleType());
            operatorField.IsVisible = schema.HasField(CfInputField.Operator);
            value1Field.IsVisible = schema.HasField(CfInputField.Value1);
            var op = ConditionalFormatOperatorChoices[Math.Max(0, operatorBox.SelectedIndex)].Op;
            value2Field.IsVisible = schema.HasField(CfInputField.Value2)
                && op is CfOperator.Between or CfOperator.NotBetween;
            formulaField.IsVisible = schema.HasField(CfInputField.Formula);
            textField.IsVisible = schema.HasField(CfInputField.Text);
            rankField.IsVisible = schema.HasField(CfInputField.Rank);
            percentBox.IsVisible = schema.HasField(CfInputField.Percent);
            iconSetField.IsVisible = schema.HasField(CfInputField.IconSetStyle);
            threeColorBox.IsVisible = schema.HasField(CfInputField.UseThreeColorScale);
            // The highlight format only applies to the non-visual rule families.
            highlightField.IsVisible = SelectedRuleType()
                is not (CfRuleType.IconSet or CfRuleType.DataBar or CfRuleType.ColorScale);
            errorText.IsVisible = false;
        }

        ruleTypeBox.SelectionChanged += (_, _) => UpdateFieldVisibility();
        operatorBox.SelectionChanged += (_, _) => UpdateFieldVisibility();

        SeedConditionalFormatEditor(
            existingRule, ruleTypeBox, operatorBox, value1Box, value2Box,
            formulaBox, textBox, rankBox, percentBox, iconSetBox, threeColorBox, highlightBox);
        UpdateFieldVisibility();

        var okButton = new Button { Content = "OK", IsDefault = true, MinWidth = 84 };
        AutomationProperties.SetAutomationId(okButton, "ConditionalFormatOkButton");
        var cancelButton = new Button { Content = "Cancel", IsCancel = true, MinWidth = 84 };
        AutomationProperties.SetAutomationId(cancelButton, "ConditionalFormatCancelButton");

        okButton.Click += (_, _) =>
        {
            var input = CollectInput();
            var highlight = ConditionalFormatHighlightPreset.Presets[Math.Max(0, highlightBox.SelectedIndex)];
            var build = ConditionalFormatRuleBuilder.TryBuildApplyCommand(
                input, _session.ActiveSheet.Id, range, highlight, existingRule?.Id);
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

        var buttonRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Right,
            Margin = new Thickness(0, 10, 0, 0),
            Children = { cancelButton, okButton },
        };
        DockPanel.SetDock(buttonRow, Dock.Bottom);

        dialog.Content = new DockPanel
        {
            Margin = new Thickness(16),
            Children =
            {
                buttonRow,
                new ScrollViewer
                {
                    Content = new StackPanel
                    {
                        Spacing = 10,
                        Children =
                        {
                            new TextBlock
                            {
                                Text = $"Applies to {FormatRangeReference(range)}",
                                Foreground = HeaderForeground,
                                TextWrapping = TextWrapping.Wrap,
                            },
                            CreateDataValidationField("Rule type", ruleTypeBox),
                            operatorField,
                            value1Field,
                            value2Field,
                            formulaField,
                            textField,
                            rankField,
                            percentBox,
                            iconSetField,
                            threeColorBox,
                            highlightField,
                            errorText,
                        },
                    },
                },
            },
        };

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
        ComboBox iconSetBox,
        CheckBox threeColorBox,
        ComboBox highlightBox)
    {
        if (rule is null)
        {
            ruleTypeBox.SelectedIndex = 0;
            return;
        }

        var typeIndex = 0;
        for (var i = 0; i < ConditionalFormatRuleTypeChoices.Count; i++)
            if (ConditionalFormatRuleTypeChoices[i].Type == rule.RuleType)
            {
                typeIndex = i;
                break;
            }

        ruleTypeBox.SelectedIndex = typeIndex;

        for (var i = 0; i < ConditionalFormatOperatorChoices.Count; i++)
            if (ConditionalFormatOperatorChoices[i].Op == rule.Operator)
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
        if (!string.IsNullOrEmpty(rule.IconSetStyle))
            iconSetBox.SelectedItem = rule.IconSetStyle;
        threeColorBox.IsChecked = rule.UseThreeColorScale;
        highlightBox.SelectedIndex = 0;
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
        AutomationProperties.SetAutomationId(valueBox, "ConditionalFormatValuePromptBox");

        var okButton = new Button { Content = "OK", IsDefault = true, MinWidth = 84 };
        var cancelButton = new Button { Content = "Cancel", IsCancel = true, MinWidth = 84 };
        okButton.Click += (_, _) =>
        {
            result = valueBox.Text ?? string.Empty;
            dialog.Close();
        };
        cancelButton.Click += (_, _) => dialog.Close();

        var buttonRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Right,
            Margin = new Thickness(0, 10, 0, 0),
            Children = { cancelButton, okButton },
        };
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
                    Children = { new TextBlock { Text = prompt, TextWrapping = TextWrapping.Wrap }, valueBox },
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
    /// selection is a single cell) with Edit and Delete. Edit re-runs the editor and replaces the rule;
    /// Delete drops it. Both commit through the atomic replace-all command for one undo step.
    /// </summary>
    private async Task ShowManageConditionalFormatsDialogAsync()
    {
        if (!TryCommitPendingFormulaEdit())
            return;

        var dialog = new Window
        {
            Title = "Manage Conditional Formatting Rules",
            Width = 560,
            Height = 420,
            MinWidth = 480,
            MinHeight = 320,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };
        AutomationProperties.SetAutomationId(dialog, "ManageConditionalFormatsDialog");

        var listBox = new ListBox { MinHeight = 220 };
        AutomationProperties.SetAutomationId(listBox, "ManageConditionalFormatsListBox");

        var emptyText = new TextBlock
        {
            Text = "No conditional formatting rules for this selection.",
            Foreground = HeaderForeground,
            IsVisible = false,
        };

        void Reload()
        {
            // A single-cell selection lists the whole sheet; a wider selection scopes to overlap.
            var selection = _session.SelectedRange;
            GridRange? scope = selection.RowCount == 1 && selection.ColCount == 1 ? null : selection;
            var items = ConditionalFormatManageModel.BuildList(_session.ActiveSheet.ConditionalFormats, scope);
            listBox.ItemsSource = items;
            emptyText.IsVisible = items.Count == 0;
            if (items.Count > 0)
                listBox.SelectedIndex = 0;
        }

        var editButton = new Button { Content = "Edit…", MinWidth = 84 };
        AutomationProperties.SetAutomationId(editButton, "ManageConditionalFormatsEditButton");
        var deleteButton = new Button { Content = "Delete", MinWidth = 84 };
        AutomationProperties.SetAutomationId(deleteButton, "ManageConditionalFormatsDeleteButton");
        var closeButton = new Button { Content = "Close", IsCancel = true, MinWidth = 84 };
        AutomationProperties.SetAutomationId(closeButton, "ManageConditionalFormatsCloseButton");

        editButton.Click += async (_, _) =>
        {
            if (listBox.SelectedItem is not ConditionalFormatRuleListItem item)
                return;

            var edited = await ShowConditionalFormatRuleEditorAsync(item.Rule);
            if (edited is null)
                return;

            var command = ConditionalFormatManageModel.BuildEditCommand(
                _session.ActiveSheet.Id, _session.ActiveSheet.ConditionalFormats, edited);
            if (command is null)
                return;

            RunConditionalFormatCommand(command, "Edited conditional formatting rule");
            Reload();
        };

        deleteButton.Click += (_, _) =>
        {
            if (listBox.SelectedItem is not ConditionalFormatRuleListItem item)
                return;

            var command = ConditionalFormatManageModel.BuildDeleteCommand(
                _session.ActiveSheet.Id, _session.ActiveSheet.ConditionalFormats, item.Id);
            if (command is null)
                return;

            RunConditionalFormatCommand(command, "Deleted conditional formatting rule");
            Reload();
        };
        closeButton.Click += (_, _) => dialog.Close();

        var buttonRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Right,
            Margin = new Thickness(0, 10, 0, 0),
            Children = { editButton, deleteButton, closeButton },
        };
        DockPanel.SetDock(buttonRow, Dock.Bottom);

        Reload();
        dialog.Content = new DockPanel
        {
            Margin = new Thickness(16),
            Children =
            {
                buttonRow,
                new StackPanel
                {
                    Spacing = 8,
                    Children = { emptyText, listBox },
                },
            },
        };

        await dialog.ShowDialog(this);
    }
}
