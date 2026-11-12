using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

using Free.Shared.Shell.Avalonia;
using FreeX.App.Presentation;
using FreeX.App.Presentation.TextToColumns;
using FreeX.Core.Commands;
using FreeX.Core.Model;

using AvaloniaGrid = Avalonia.Controls.Grid;
using AvaloniaHorizontalAlignment = Avalonia.Layout.HorizontalAlignment;
using AvaloniaVerticalAlignment = Avalonia.Layout.VerticalAlignment;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    /// <summary>
    /// The date-order variants, in the same MDY/DMY/YMD/MYD/DYM/YDM order the WPF host's date-format
    /// combo lists them (<see cref="TextToColumnsDialogPlanner.DateColumnFormatLabel"/>), so a European
    /// "day first" column (e.g. "03/04/2024" meaning 3 April) can be parsed correctly instead of always
    /// being forced through Month/Day/Year.
    /// </summary>
    private static readonly IReadOnlyList<TextToColumnsColumnFormat> TextToColumnsDateFormatChoices =
    [
        TextToColumnsColumnFormat.DateMDY,
        TextToColumnsColumnFormat.DateDMY,
        TextToColumnsColumnFormat.DateYMD,
        TextToColumnsColumnFormat.DateMYD,
        TextToColumnsColumnFormat.DateDYM,
        TextToColumnsColumnFormat.DateYDM,
    ];

    /// <summary>
    /// The per-column format choices the dialog offers, in dropdown order. One entry per date order
    /// (not just MDY) so every DMY/YMD/MYD/DYM/YDM layout Excel's Text Import Wizard supports is
    /// reachable here too.
    /// </summary>
    private static IReadOnlyList<(TextToColumnsColumnFormat Format, string Label)> TextToColumnsFormatChoices
    {
        get
        {
            var choices = new List<(TextToColumnsColumnFormat Format, string Label)>
            {
                (TextToColumnsColumnFormat.General, UiText.Get("TableLoc_TtcFormatGeneral")),
                (TextToColumnsColumnFormat.Text, UiText.Get("TableLoc_TtcFormatText")),
            };
            foreach (var format in TextToColumnsDateFormatChoices)
            {
                var order = TextToColumnsDialogPlanner.DateColumnFormatLabel(format);
                choices.Add((format, $"{UiText.Get("TableLoc_TtcFormatDate")} ({order})"));
            }

            choices.Add((TextToColumnsColumnFormat.Skip, UiText.Get("TableLoc_TtcFormatSkip")));
            return choices;
        }
    }

    /// <summary>Opens the Text-to-Columns dialog (invoked from the Data menu and the Data-tab ribbon button).</summary>
    private void TextToColumns() => RunGuarded(ShowTextToColumnsDialogAsync);

    /// <summary>
    /// The compact Text-to-Columns dialog: pick Delimited vs Fixed-width; for delimited choose the
    /// delimiter checkboxes (Tab/Semicolon/Comma/Space/Other+char), treat-consecutive and the text
    /// qualifier; for fixed-width enter the break positions. A live preview (driven by
    /// <see cref="TextToColumnsPlanner.Preview"/>) shows the split, and a per-column format dropdown
    /// (General/Text/Date/Skip) annotates the output. On Apply the split is written across columns
    /// starting at the source column through the shared session command path (undoable + refreshing).
    /// </summary>
    private async Task ShowTextToColumnsDialogAsync()
    {
        if (_isOpening || _isSaving || !TryCommitPendingFormulaEdit())
            return;

        var sheet = _session.ActiveSheet;
        var range = _session.SelectedRange;
        if (range.ColCount != 1)
        {
            ShowEditIssue(UiText.Get("TableLoc_TtcSelectSingleColumn"));
            return;
        }

        var sources = ReadTextToColumnsSources(sheet, range);
        if (sources.Count == 0)
        {
            ShowEditIssue(UiText.Format("TableLoc_TtcNoTextToSplit", FormatRangeReference(range)));
            return;
        }

        var dialog = new Window
        {
            // WPF TextToColumnsDialog keeps the fixed "Text to Columns" caption; the step indicator
            // lives in the body header ("Text Wizard - Step N of 3"), not in the title bar.
            Title = UiText.Get("TextToColumns_TextToColumns"),
            Width = TextToColumnsDialogWidth,
            Height = TextToColumnsDialogHeight,
            MinWidth = TextToColumnsDialogMetrics.MinimumWindowWidth,
            MinHeight = TextToColumnsDialogMetrics.MinimumWindowHeight,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };
        AutomationProperties.SetAutomationId(dialog, "TextToColumnsDialog");
        AvaloniaCompactDialogChrome.ApplyWindow(dialog, DataOpsDialogChromeStyle);

        var delimitedButton = new RadioButton { Content = UiText.Get("TableLoc_TtcDelimited"), IsChecked = true, GroupName = "TtcMode" };
        ApplyDataOpsRadioButtonChrome(delimitedButton);
        AvaloniaCompactDialogChrome.ApplyCompactRadioButton(delimitedButton, DataOpsDialogChromeStyle);
        delimitedButton.Height = 20;
        delimitedButton.MinHeight = 20;
        delimitedButton.MaxHeight = 20;
        AutomationProperties.SetAutomationId(delimitedButton, "TextToColumnsDelimitedButton");
        var fixedWidthButton = new RadioButton { Content = UiText.Get("TableLoc_TtcFixedWidth"), GroupName = "TtcMode" };
        ApplyDataOpsRadioButtonChrome(fixedWidthButton);
        AvaloniaCompactDialogChrome.ApplyCompactRadioButton(fixedWidthButton, DataOpsDialogChromeStyle);
        fixedWidthButton.Height = 20;
        fixedWidthButton.MinHeight = 20;
        fixedWidthButton.MaxHeight = 20;
        AutomationProperties.SetAutomationId(fixedWidthButton, "TextToColumnsFixedWidthButton");

        var tabBox = new CheckBox { Content = UiText.Get("TableLoc_TtcDelimTab") };
        ApplyDataOpsCheckBoxChrome(tabBox);
        AutomationProperties.SetAutomationId(tabBox, "TextToColumnsTabBox");
        var semicolonBox = new CheckBox { Content = UiText.Get("TableLoc_TtcDelimSemicolon") };
        ApplyDataOpsCheckBoxChrome(semicolonBox);
        AutomationProperties.SetAutomationId(semicolonBox, "TextToColumnsSemicolonBox");
        var commaBox = new CheckBox { Content = UiText.Get("TableLoc_TtcDelimComma"), IsChecked = true };
        ApplyDataOpsCheckBoxChrome(commaBox);
        AutomationProperties.SetAutomationId(commaBox, "TextToColumnsCommaBox");
        var spaceBox = new CheckBox { Content = UiText.Get("TableLoc_TtcDelimSpace") };
        ApplyDataOpsCheckBoxChrome(spaceBox);
        AutomationProperties.SetAutomationId(spaceBox, "TextToColumnsSpaceBox");
        var otherBox = new CheckBox { Content = UiText.Get("TableLoc_TtcDelimOther") };
        ApplyDataOpsCheckBoxChrome(otherBox);
        AutomationProperties.SetAutomationId(otherBox, "TextToColumnsOtherBox");
        // WPF: _customBox Width = 48, Margin = (6, 0, 0, 0) beside the "Other:" check box.
        var otherCharBox = new TextBox { Width = 48, MaxLength = 1 };
        ApplyDataOpsTextBoxChrome(otherCharBox);
        AutomationProperties.SetAutomationId(otherCharBox, "TextToColumnsOtherCharBox");

        var treatConsecutiveBox = new CheckBox { Content = UiText.Get("TableLoc_TtcTreatConsecutive") };
        ApplyDataOpsCheckBoxChrome(treatConsecutiveBox);
        AutomationProperties.SetAutomationId(treatConsecutiveBox, "TextToColumnsTreatConsecutiveBox");

        var qualifierBox = new ComboBox
        {
            ItemsSource = new[] { "\"", "'", UiText.Get("TableLoc_TtcQualifierNone") },
            SelectedIndex = 0,
            // WPF: _textQualifierBox Width = 130, Margin = (8, 0, 0, 0).
            Width = 130,
        };
        ApplyDataOpsComboBoxChrome(qualifierBox);
        AutomationProperties.SetAutomationId(qualifierBox, "TextToColumnsQualifierBox");

        // WPF: _fixedWidthBreaksBox.Width = 160.
        var breaksBox = new TextBox { PlaceholderText = UiText.Get("TableLoc_TtcBreaksPlaceholder"), Width = 160 };
        ApplyDataOpsTextBoxChrome(breaksBox);
        AutomationProperties.SetAutomationId(breaksBox, "TextToColumnsBreaksBox");

        // WPF: _formatColumnBox Width = 110, Margin = (0, 0, 10, 0).
        var formatColumnBox = new ComboBox { Width = 110 };
        ApplyDataOpsComboBoxChrome(formatColumnBox);
        AutomationProperties.SetAutomationId(formatColumnBox, "TextToColumnsFormatColumnBox");
        var formatBox = new ComboBox
        {
            ItemsSource = TextToColumnsFormatChoices.Select(c => c.Label).ToList(),
            SelectedIndex = 0,
            MinWidth = 140,
        };
        ApplyDataOpsComboBoxChrome(formatBox);
        AutomationProperties.SetAutomationId(formatBox, "TextToColumnsFormatBox");

        var destinationBox = new TextBox
        {
            Text = FormatCellReference(range.Start),
            // WPF: _destinationBox Width = 120.
            Width = 120,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Left,
        };
        ApplyDataOpsTextBoxChrome(destinationBox);
        AutomationProperties.SetAutomationId(destinationBox, "TextToColumnsDestinationBox");
        AutomationProperties.SetName(destinationBox, UiText.Get("TextToColumns_DestinationLabel"));
        var destinationPicker = CreateDialogRangePickerButton(
            "TextToColumnsDestinationPickerButton",
            UiText.Get("TextToColumns_SelectDestinationCell"));

        // Advanced options (WPF parity: TextToColumnsDialog.ColumnFormats.cs's CreateAdvancedOptionsPanel) --
        // decimal/thousands separator overrides and trailing-minus negatives, so locale-mismatched or
        // mainframe-style numeric text can still import as numbers instead of silently staying text.
        var decimalSeparatorBox = new TextBox { Text = ".", Width = 42 };
        ApplyDataOpsTextBoxChrome(decimalSeparatorBox);
        AutomationProperties.SetAutomationId(decimalSeparatorBox, "TextToColumnsDecimalSeparatorBox");
        var thousandsSeparatorBox = new TextBox { Text = ",", Width = 42 };
        ApplyDataOpsTextBoxChrome(thousandsSeparatorBox);
        AutomationProperties.SetAutomationId(thousandsSeparatorBox, "TextToColumnsThousandsSeparatorBox");
        var trailingMinusBox = new CheckBox { Content = UiText.Get("TextToColumns_TrailingMinusForNegativeNumbers") };
        ApplyDataOpsCheckBoxChrome(trailingMinusBox);
        AutomationProperties.SetAutomationId(trailingMinusBox, "TextToColumnsTrailingMinusBox");

        // WPF: one ListView { Height = 88 } shared by all three steps (body order: step panels,
        // "Data preview" caption, preview, column format, destination). The frame is the default
        // WPF list border (#ABADB3), not a heavy ink rule.
        var previewHost = new Border
        {
            BorderBrush = Brush(171, 173, 179),
            BorderThickness = new Thickness(1),
            Background = Brushes.White,
            Height = 88,
            MinHeight = 88,
            Padding = new Thickness(0),
        };
        AutomationProperties.SetAutomationId(previewHost, "TextToColumnsPreviewGrid");

        // WPF has no status line under the wizard; this one only appears while the current options
        // cannot produce a preview (WPF silently falls back to a comma split there).
        var statusText = new TextBlock
        {
            Foreground = HeaderForeground,
            TextWrapping = TextWrapping.Wrap,
            IsVisible = false,
            FontSize = 12,
            FontFamily = FormulaBarFontFamily,
            Margin = new Thickness(0, 8, 0, 0),
        };
        var warningText = new TextBlock
        {
            Foreground = Brush(180, 30, 30),
            TextWrapping = TextWrapping.Wrap,
            IsVisible = false,
            FontSize = 12,
            FontFamily = FormulaBarFontFamily,
            Margin = new Thickness(0, 8, 0, 0),
        };
        AutomationProperties.SetAutomationId(warningText, "TextToColumnsWarningText");

        // Per-output-column format hints, keyed by output column index. Empty (General) by default.
        var columnFormats = new Dictionary<int, TextToColumnsColumnFormat>();
        var previewColumnCount = 1;
        var overwriteConfirmed = false;

        TextToColumnsDialogState BuildState()
        {
            var orderedFormats = new List<TextToColumnsColumnFormat>();
            for (var i = 0; i < previewColumnCount; i++)
                orderedFormats.Add(columnFormats.TryGetValue(i, out var f) ? f : TextToColumnsColumnFormat.General);

            return new TextToColumnsDialogState(
                SplitMode: fixedWidthButton.IsChecked == true
                    ? TextToColumnsSplitMode.FixedWidth
                    : TextToColumnsSplitMode.Delimited,
                Tab: tabBox.IsChecked == true,
                Semicolon: semicolonBox.IsChecked == true,
                Comma: commaBox.IsChecked == true,
                Space: spaceBox.IsChecked == true,
                Other: otherBox.IsChecked == true,
                OtherDelimiter: string.IsNullOrEmpty(otherCharBox.Text) ? null : otherCharBox.Text,
                TreatConsecutiveDelimitersAsOne: treatConsecutiveBox.IsChecked == true,
                TextQualifier: qualifierBox.SelectedIndex switch
                {
                    1 => TextToColumnsTextQualifier.SingleQuote,
                    2 => TextToColumnsTextQualifier.None,
                    _ => TextToColumnsTextQualifier.DoubleQuote,
                },
                FixedWidthBreakPositions: TextToColumnsFixedWidthBreakPlanner.ParseBreakPositions(breaksBox.Text),
                ColumnFormats: orderedFormats);
        }

        void RefreshPreview()
        {
            overwriteConfirmed = false;
            warningText.IsVisible = false;

            if (!TextToColumnsDialogPlanner.TryBuildOptions(BuildState(), out var options, out var previewIssue))
            {
                previewHost.Child = null;
                statusText.Text = TextToColumnsDialogPlanner
                    .DescribeValidationIssue(previewIssue)
                    .Message.Resolve(UiText.Get, UiText.Format);
                statusText.IsVisible = true;
                return;
            }

            var preview = TextToColumnsPlanner.Preview(
                sources,
                options,
                TextToColumnsDialogMetrics.PreviewRowLimit);
            previewColumnCount = Math.Max(1, preview.ColumnCount);
            statusText.Text = UiText.Format("TableLoc_TtcSplittingStatus", sources.Count, previewColumnCount);
            statusText.IsVisible = false;

            previewHost.Child = BuildTextToColumnsPreviewGrid(preview, previewColumnCount, columnFormats);
            RefreshFormatColumnChoices();
        }

        void RefreshFormatColumnChoices()
        {
            var previousIndex = formatColumnBox.SelectedIndex;
            formatColumnBox.ItemsSource = Enumerable.Range(1, previewColumnCount)
                .Select(n => UiText.Format("TableLoc_TtcColumnN", n))
                .ToList();
            formatColumnBox.SelectedIndex = previousIndex >= 0 && previousIndex < previewColumnCount
                ? previousIndex
                : 0;
            SyncFormatBoxToSelectedColumn();
        }

        void SyncFormatBoxToSelectedColumn()
        {
            var column = Math.Max(0, formatColumnBox.SelectedIndex);
            var format = columnFormats.TryGetValue(column, out var f) ? f : TextToColumnsColumnFormat.General;
            for (var i = 0; i < TextToColumnsFormatChoices.Count; i++)
            {
                if (TextToColumnsFormatChoices[i].Format == format)
                {
                    formatBox.SelectedIndex = i;
                    return;
                }
            }

            formatBox.SelectedIndex = 0;
        }

        foreach (var box in new[] { tabBox, semicolonBox, commaBox, spaceBox, otherBox })
        {
            box.IsCheckedChanged += (_, _) => RefreshPreview();
        }

        treatConsecutiveBox.IsCheckedChanged += (_, _) => RefreshPreview();
        otherCharBox.TextChanged += (_, _) => RefreshPreview();
        qualifierBox.SelectionChanged += (_, _) => RefreshPreview();
        breaksBox.TextChanged += (_, _) => RefreshPreview();
        destinationBox.TextChanged += (_, _) => overwriteConfirmed = false;
        formatColumnBox.SelectionChanged += (_, _) => SyncFormatBoxToSelectedColumn();
        formatBox.SelectionChanged += (_, _) =>
        {
            var column = Math.Max(0, formatColumnBox.SelectedIndex);
            var choiceIndex = Math.Max(0, formatBox.SelectedIndex);
            columnFormats[column] = TextToColumnsFormatChoices[choiceIndex].Format;
            RefreshPreview();
        };

        // WPF wizard navigation: [< Back][Next >][Finish][Cancel]
        var backButton = new Button { Content = UiText.Get("TableLoc_TtcBack"), MinWidth = 72, IsEnabled = false };
        ApplyDataOpsButtonChrome(backButton);
        AutomationProperties.SetAutomationId(backButton, "TextToColumnsBackButton");
        var nextButton = new Button { Content = UiText.Get("TableLoc_TtcNext"), MinWidth = 72 };
        ApplyDataOpsButtonChrome(nextButton);
        AutomationProperties.SetAutomationId(nextButton, "TextToColumnsNextButton");
        var applyButton = new Button { Content = UiText.Get("TableLoc_TtcFinish"), IsDefault = true, MinWidth = 72 };
        ApplyDataOpsButtonChrome(applyButton, isDefault: true);
        AutomationProperties.SetAutomationId(applyButton, "TextToColumnsApplyButton");
        var cancelButton = new Button { Content = UiText.Get("TableLoc_Cancel"), IsCancel = true, MinWidth = 72 };
        ApplyDataOpsButtonChrome(cancelButton);
        AutomationProperties.SetAutomationId(cancelButton, "TextToColumnsCancelButton");

        // Wizard header (WPF TextToColumnsDialog.cs: _wizardHeader / _wizardInstruction) -- a SemiBold
        // "Text Wizard - Step N of 3" line (bottom margin 8) over the wrapped per-step instruction
        // (bottom margin 10), both at the dialog's normal 12px size and outside the scrolling body.
        var wizardStepHeader = new TextBlock
        {
            Text = UiText.Format(TextToColumnsWizardSurfacePlanner.HeaderFormatKey, 1),
            FontWeight = FontWeight.SemiBold,
            FontSize = 12,
            FontFamily = FormulaBarFontFamily,
            Margin = new Thickness(0, 0, 0, 8),
        };
        AutomationProperties.SetAutomationId(wizardStepHeader, "TextToColumnsWizardStepHeader");
        var wizardInstruction = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            FontFamily = FormulaBarFontFamily,
            Margin = new Thickness(0, 0, 0, 10),
        };
        AutomationProperties.SetAutomationId(wizardInstruction, "TextToColumnsWizardInstruction");

        // Wizard step tracking — declare forward refs for SyncWizardNavigation closure
        var currentStep = 1;
        const int totalSteps = 3;
        Control? originalDataTypePanel = null;
        Control? delimiterPanel = null;
        Control? fixedWidthPanel = null;
        Control? columnFormatPanel = null;
        Control? destinationPanel = null;

        // Match the WPF wizard's keyboard entry point on every step. The generic owned-dialog
        // policy is intentionally a fallback; this wizard changes its visible focus scope as the
        // user moves between steps, so the production route owns the exact target.
        bool FocusCurrentWizardStepTarget()
        {
            Control target = currentStep switch
            {
                1 => fixedWidthButton.IsChecked == true ? fixedWidthButton : delimitedButton,
                2 when fixedWidthButton.IsChecked == true => breaksBox,
                2 => tabBox,
                _ => formatColumnBox,
            };

            target.Focus();
            return ReferenceEquals(dialog.FocusManager?.GetFocusedElement(), target);
        }

        void ApplyWizardStep()
        {
            warningText.IsVisible = false;

            if (!TextToColumnsDialogPlanner.TryParseDestination(
                    destinationBox.Text,
                    range.Start,
                    out var destination))
            {
                currentStep = 3;
                SyncWizardNavigation();
                dialog.UpdateLayout();
                warningText.Text = TextToColumnsDialogPlanner
                    .DescribeValidationIssue(TextToColumnsDialogValidationIssue.InvalidDestination)
                    .Message.Resolve(UiText.Get, UiText.Format);
                warningText.IsVisible = true;
                destinationBox.Focus();
                destinationBox.SelectAll();
                return;
            }

            if (!TextToColumnsDialogPlanner.TryBuildOptions(BuildState(), out var options, out var optionsIssue))
            {
                warningText.Text = TextToColumnsDialogPlanner
                    .DescribeValidationIssue(optionsIssue)
                    .Message.Resolve(UiText.Get, UiText.Format);
                warningText.IsVisible = true;
                return;
            }

            if (!TextToColumnsDialogPlanner.TryParseAdvancedSeparator(decimalSeparatorBox.Text, out var decimalSeparator))
            {
                warningText.Text = TextToColumnsDialogPlanner
                    .DescribeValidationIssue(TextToColumnsDialogValidationIssue.InvalidDecimalSeparator)
                    .Message.Resolve(UiText.Get, UiText.Format);
                warningText.IsVisible = true;
                return;
            }

            if (!TextToColumnsDialogPlanner.TryParseAdvancedSeparator(thousandsSeparatorBox.Text, out var thousandsSeparator))
            {
                warningText.Text = TextToColumnsDialogPlanner
                    .DescribeValidationIssue(TextToColumnsDialogValidationIssue.InvalidThousandsSeparator)
                    .Message.Resolve(UiText.Get, UiText.Format);
                warningText.IsVisible = true;
                return;
            }

            var advancedOptions = new TextToColumnsAdvancedOptions(
                decimalSeparator,
                thousandsSeparator,
                trailingMinusBox.IsChecked == true);

            var result = TextToColumnsPlanner.Plan(sources, options);
            var edits = TextToColumnsApplyPlanner.MapResultToEdits(
                sheet.Id,
                result,
                range,
                destination,
                advancedOptions);
            if (edits.Count == 0)
            {
                warningText.Text = TextToColumnsDialogPlanner
                    .DescribeValidationIssue(TextToColumnsDialogValidationIssue.NoColumnsToWrite)
                    .Message.Resolve(UiText.Get, UiText.Format);
                warningText.IsVisible = true;
                return;
            }

            var overwrites = TextToColumnsApplyPlanner.FindOverwriteTargets(sheet, edits, range);
            if (overwrites.Count > 0 && !overwriteConfirmed)
            {
                overwriteConfirmed = true;
                warningText.Text = UiText.Format("TableLoc_TtcOverwriteWarning", overwrites.Count);
                warningText.IsVisible = true;
                return;
            }

            if (!ApplyTextToColumnsEdits(sheet.Id, edits, range))
                return;

            dialog.Close();
        }

        void SyncWizardNavigation()
        {
            // Same shared step plan the WPF wizard's UpdateWizardStep consumes: header/instruction
            // text and which panels a step shows (the preview stays visible on every step).
            var stepPlan = TextToColumnsWizardSurfacePlanner.CreateStepPlan(
                currentStep,
                fixedWidthButton.IsChecked == true);
            wizardStepHeader.Text = UiText.Format(TextToColumnsWizardSurfacePlanner.HeaderFormatKey, stepPlan.Step);
            wizardInstruction.Text = UiText.Get(stepPlan.InstructionKey);
            backButton.IsEnabled = stepPlan.BackEnabled;
            nextButton.IsEnabled = stepPlan.NextEnabled;
            // Panel visibility (panels are assigned after this function definition)
            if (originalDataTypePanel is not null) originalDataTypePanel.IsVisible = stepPlan.ShowOriginalDataTypePanel;
            if (delimiterPanel is not null) delimiterPanel.IsVisible = stepPlan.ShowDelimiterPanel;
            if (fixedWidthPanel is not null) fixedWidthPanel.IsVisible = stepPlan.ShowFixedWidthPanel;
            if (columnFormatPanel is not null) columnFormatPanel.IsVisible = stepPlan.ShowColumnFormatPanel;
            if (destinationPanel is not null) destinationPanel.IsVisible = stepPlan.ShowDestinationPanel;
        }

        void HandleSplitModeChanged()
        {
            SyncWizardNavigation();
            RefreshPreview();
        }

        delimitedButton.IsCheckedChanged += (_, _) => HandleSplitModeChanged();
        fixedWidthButton.IsCheckedChanged += (_, _) => HandleSplitModeChanged();

        applyButton.Click += (_, _) => ApplyWizardStep();
        cancelButton.Click += (_, _) => dialog.Close();
        backButton.Click += (_, _) =>
        {
            if (currentStep > 1)
            {
                currentStep--;
                SyncWizardNavigation();
                // The step panels are GroupBoxes: a group that has never been visible has no applied
                // template yet, so its content is not in the visual tree and cannot take focus.
                // Lay the newly shown step out first (WPF MoveWizardStep calls UpdateLayout too).
                dialog.UpdateLayout();
                FocusCurrentWizardStepTarget();
            }
        };
        nextButton.Click += (_, _) =>
        {
            if (currentStep < totalSteps)
            {
                currentStep++;
                SyncWizardNavigation();
                // The step panels are GroupBoxes: a group that has never been visible has no applied
                // template yet, so its content is not in the visual tree and cannot take focus.
                // Lay the newly shown step out first (WPF MoveWizardStep calls UpdateLayout too).
                dialog.UpdateLayout();
                FocusCurrentWizardStepTarget();
            }
        };

        TextBlock CreateFieldLabel(string resourceKey, Thickness margin) =>
            new()
            {
                Text = StripDisplayMnemonic(UiText.Get(resourceKey)),
                VerticalAlignment = AvaloniaVerticalAlignment.Center,
                FontSize = 12,
                FontFamily = FormulaBarFontFamily,
                Margin = margin,
            };

        // Step 1 -- WPF CreateOriginalDataTypePanel: GroupBox { Padding = 8, Margin = (0, 0, 0, 8) }
        // holding the two stacked radio buttons.
        var originalDataTypeGroup = new GroupBox
        {
            Header = UiText.Get("TextToColumns_OriginalDataTypeGroup"),
            Padding = new Thickness(8),
            Margin = new Thickness(0, 0, 0, 8),
            Content = new StackPanel
            {
                Children = { delimitedButton, fixedWidthButton },
            },
        };
        ApplyTextToColumnsGroupBoxChrome(originalDataTypeGroup);
        originalDataTypePanel = originalDataTypeGroup;

        // Step 2 (delimited) -- WPF CreateDelimiterPanel: a wrapping row of delimiter check boxes with
        // the "Other:" box and its 48px field kept together, the text-qualifier row 8px below, then
        // "Treat consecutive delimiters as one" another 8px below. WPF packs the check boxes with no
        // gap at all; an 8px gap is kept here so adjacent captions stay readable.
        otherCharBox.Margin = new Thickness(6, 0, 0, 0);
        var otherPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children = { otherBox, otherCharBox },
        };
        var delimiterRow = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            Children = { tabBox, semicolonBox, commaBox, spaceBox, otherPanel },
        };
        foreach (var child in new Control[] { tabBox, semicolonBox, commaBox, spaceBox })
            child.Margin = new Thickness(0, 0, 8, 0);

        qualifierBox.Margin = new Thickness(8, 0, 0, 0);
        var qualifierRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 8, 0, 0),
            Children =
            {
                CreateFieldLabel("TextToColumns_TextQualifierLabel", new Thickness(0)),
                qualifierBox,
            },
        };
        treatConsecutiveBox.Margin = new Thickness(0, 8, 0, 0);

        var delimiterGroup = new GroupBox
        {
            Header = UiText.Get("TextToColumns_DelimitersGroup"),
            Padding = new Thickness(8),
            Margin = new Thickness(0, 0, 0, 8),
            Content = new StackPanel
            {
                Children = { delimiterRow, qualifierRow, treatConsecutiveBox },
            },
        };
        ApplyTextToColumnsGroupBoxChrome(delimiterGroup);
        delimiterPanel = delimiterGroup;

        // Step 2 (fixed width) -- WPF CreateFixedWidthPanel. The interactive break-line ruler is not
        // implemented in this shell, so the group carries only the "Breaks:" row (label 8px before a
        // 160px field).
        var breaksRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children =
            {
                CreateFieldLabel("TextToColumns_Breaks", new Thickness(0, 0, 8, 0)),
                breaksBox,
            },
        };
        var fixedWidthGroup = new GroupBox
        {
            Header = UiText.Get("TextToColumns_FixedWidth2"),
            Padding = new Thickness(8),
            Margin = new Thickness(0, 0, 0, 8),
            Content = breaksRow,
        };
        ApplyTextToColumnsGroupBoxChrome(fixedWidthGroup);
        fixedWidthPanel = fixedWidthGroup;

        // WPF: _dataPreviewLabel -- "Data preview" in regular weight, Margin = (0, 10, 0, 4).
        var dataPreviewLabel = new TextBlock
        {
            Text = UiText.Get("TextToColumns_DataPreview"),
            FontSize = 12,
            FontFamily = FormulaBarFontFamily,
            Margin = new Thickness(0, 10, 0, 4),
        };

        // Advanced options group (WPF parity: TextToColumnsDialog.ColumnFormats.cs's
        // CreateAdvancedOptionsPanel) -- decimal/thousands separator overrides + trailing-minus checkbox.
        // Labels sit 6px before their 42px field, fields are followed by a 12px gap, and the check box
        // is 8px below the row.
        decimalSeparatorBox.Margin = new Thickness(0, 0, 12, 0);
        thousandsSeparatorBox.Margin = new Thickness(0, 0, 12, 0);
        var advancedSeparatorsRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children =
            {
                new Label
                {
                    Content = UiText.Get("TextToColumns_DecimalSeparatorLabel"),
                    Target = decimalSeparatorBox,
                    Padding = new Thickness(0),
                    Margin = new Thickness(0, 0, 6, 0),
                    VerticalAlignment = AvaloniaVerticalAlignment.Center,
                    FontSize = 12,
                    FontFamily = FormulaBarFontFamily,
                },
                decimalSeparatorBox,
                new Label
                {
                    Content = UiText.Get("TextToColumns_ThousandsSeparatorLabel"),
                    Target = thousandsSeparatorBox,
                    Padding = new Thickness(0),
                    Margin = new Thickness(0, 0, 6, 0),
                    VerticalAlignment = AvaloniaVerticalAlignment.Center,
                    FontSize = 12,
                    FontFamily = FormulaBarFontFamily,
                },
                thousandsSeparatorBox,
            },
        };
        trailingMinusBox.Margin = new Thickness(0, 8, 0, 0);

        var advancedOptionsGroup = new GroupBox
        {
            Header = UiText.Get("TextToColumns_AdvancedGroup"),
            Padding = new Thickness(8),
            Margin = new Thickness(0, 8, 0, 0),
            Content = new StackPanel
            {
                Children = { advancedSeparatorsRow, trailingMinusBox },
            },
        };
        ApplyTextToColumnsGroupBoxChrome(advancedOptionsGroup);

        // Step 3 -- WPF CreateColumnFormatPanel: GroupBox { Padding = 8, Margin = (0, 10, 0, 0) } with
        // the "Column:" label 8px before the 110px column picker (10px gap after it), the per-column
        // format choice, then the nested Advanced group. WPF lists the format as radio buttons; this
        // shell keeps its single format drop-down in that position.
        formatColumnBox.Margin = new Thickness(0, 0, 10, 0);
        var formatRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children =
            {
                CreateFieldLabel("TextToColumns_ColumnLabel", new Thickness(0, 0, 8, 0)),
                formatColumnBox,
                formatBox,
            },
        };
        var columnFormatGroup = new GroupBox
        {
            Header = UiText.Get("TextToColumns_ColumnDataFormatGroup"),
            Padding = new Thickness(8),
            Margin = new Thickness(0, 10, 0, 0),
            Content = new StackPanel
            {
                Children = { formatRow, advancedOptionsGroup },
            },
        };
        ApplyTextToColumnsGroupBoxChrome(columnFormatGroup);
        columnFormatPanel = columnFormatGroup;

        // Step 3 -- WPF CreateDestinationPanel: one row, Margin = (0, 10, 0, 0), with the label 8px
        // before the reference editor. DialogReferencePicker.CreateEditor docks its 28px "..." picker
        // on the LEFT of the box (6px gap). The box stays first in the logical order so the existing
        // Tab order (box, then picker) is unchanged.
        destinationPicker.Margin = new Thickness(0, 0, 6, 0);
        var destinationRow = new AvaloniaGrid
        {
            Margin = new Thickness(0, 10, 0, 0),
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = GridLength.Star },
            },
        };
        var destinationLabel = CreateFieldLabel("TextToColumns_DestinationLabel", new Thickness(0, 0, 8, 0));
        AvaloniaGrid.SetColumn(destinationLabel, 0);
        AvaloniaGrid.SetColumn(destinationBox, 2);
        AvaloniaGrid.SetColumn(destinationPicker, 1);
        destinationRow.Children.Add(destinationLabel);
        destinationRow.Children.Add(destinationBox);
        destinationRow.Children.Add(destinationPicker);
        destinationPanel = destinationRow;

        // WPF wizard nav button order: [< Back][Next >][Finish][Cancel], 72px wide, 12px above.
        var buttonRow = AvaloniaCompactDialogChrome.CreateActionRow(
            [backButton, nextButton, applyButton, cancelButton],
            new Thickness(0, 12, 0, 0));
        DockPanel.SetDock(buttonRow, Dock.Bottom);

        var wizardHeaderPanel = new StackPanel
        {
            Children = { wizardStepHeader, wizardInstruction },
        };
        DockPanel.SetDock(wizardHeaderPanel, Dock.Top);

        // WPF root: Grid { Margin = 12 } with rows header / scrolling body / buttons, and the body a
        // single StackPanel in this order. The button row stays the first logical child so the
        // existing Tab cycle is unchanged.
        dialog.Content = new DockPanel
        {
            Margin = new Thickness(12),
            Children =
            {
                buttonRow,
                wizardHeaderPanel,
                new ScrollViewer
                {
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    Content = new StackPanel
                    {
                        Spacing = 0,
                        Children =
                        {
                            originalDataTypeGroup,
                            delimiterGroup,
                            fixedWidthGroup,
                            dataPreviewLabel,
                            previewHost,
                            columnFormatGroup,
                            destinationRow,
                            statusText,
                            warningText,
                        },
                    },
                },
            },
        };
        AttachDialogRangePicker(dialog, destinationPicker, destinationBox, "range.text-to-columns.destination");

        // WPF's TextToColumnsDialog is a keyboard-first wizard: the first choice is focused when
        // the window opens, every step is tab-cyclic, and Escape is always Cancel. Keep these
        // lifecycle guarantees on the production dialog itself instead of relying on capture code.
        KeyboardNavigation.SetTabNavigation(dialog, KeyboardNavigationMode.Cycle);
        dialog.Opened += (_, _) =>
        {
            dialog.UpdateLayout();
            // One post is not enough: if the step target is not focusable yet the call silently
            // fails, and the shared owned-dialog fallback then focuses the first focusable control
            // -- the Next button -- instead of the step's own target. Keep retrying on layout until
            // the target accepts focus.
            // Stop after the first success. Retrying past that re-focuses the step target on every
            // later layout pass, and since Tab itself triggers one, the first Tab was immediately
            // undone and forward navigation looked stuck on the radio.
            var initialFocusEstablished = false;
            EventHandler? retryFocus = null;
            retryFocus = (_, _) =>
            {
                if (initialFocusEstablished || !dialog.IsVisible)
                {
                    dialog.LayoutUpdated -= retryFocus;
                    return;
                }

                if (FocusCurrentWizardStepTarget())
                {
                    initialFocusEstablished = true;
                    dialog.LayoutUpdated -= retryFocus;
                }
            };
            dialog.LayoutUpdated += retryFocus;
            dialog.Closed += (_, _) => dialog.LayoutUpdated -= retryFocus;
            Dispatcher.UIThread.Post(
                () =>
                {
                    if (!initialFocusEstablished && FocusCurrentWizardStepTarget())
                        initialFocusEstablished = true;
                },
                DispatcherPriority.Input);
        };
        ConfigureDeferredDialogCancel(dialog, cancelButton);

        SyncWizardNavigation();
        RefreshPreview();
        await dialog.ShowDialog(this);
    }

    /// <summary>Reads the source column's cell texts (skipping trailing blanks) for the split.</summary>
    private static IReadOnlyList<string> ReadTextToColumnsSources(Sheet sheet, GridRange range)
    {
        var col = range.Start.Col;
        var texts = new List<string>();
        for (var row = range.Start.Row; row <= range.End.Row; row++)
            texts.Add(SpreadsheetDisplayFormatter.FormatScalarValue(sheet.GetValue(row, col)));

        // Drop trailing empty rows so a single-cell selection that happens to span blanks does nothing.
        while (texts.Count > 0 && string.IsNullOrEmpty(texts[^1]))
            texts.RemoveAt(texts.Count - 1);

        return texts;
    }

    /// <summary>Applies the mapped edits through the shared session command path and refreshes the shell.</summary>
    private bool ApplyTextToColumnsEdits(
        SheetId sheetId,
        IReadOnlyList<(CellAddress Address, Cell NewCell)> edits,
        GridRange range)
    {
        var command = new EditCellsCommand(sheetId, edits);
        var result = _session.ExecuteReviewCommand(command);
        if (!result.Success)
        {
            ShowEditIssue(result.ErrorMessage ?? UiText.Get("TableLoc_TtcFailed"));
            return false;
        }

        RefreshShell(UiText.Format("TableLoc_TtcSplitIntoColumns", FormatRangeReference(range)));
        return true;
    }

    /// <summary>
    /// Builds the read-only preview table from primitives (a <see cref="AvaloniaGrid"/> of bordered
    /// cells): a header row of "Column N" labels (Skip columns annotated) plus one row per sample row,
    /// each field padded to the full column count.
    /// </summary>
    private static Control BuildTextToColumnsPreviewGrid(
        TextToColumnsPreview preview,
        int columnCount,
        IReadOnlyDictionary<int, TextToColumnsColumnFormat> columnFormats)
    {
        var grid = new AvaloniaGrid
        {
            Width = 140 + Math.Max(0, columnCount - 1) * 100,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Left,
        };
        for (var c = 0; c < columnCount; c++)
            grid.ColumnDefinitions.Add(new ColumnDefinition(c == 0 ? 140 : 100, GridUnitType.Pixel));

        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        for (var r = 0; r < preview.SampleRows.Count; r++)
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        for (var c = 0; c < columnCount; c++)
        {
            var skipped = columnFormats.TryGetValue(c, out var f) && f == TextToColumnsColumnFormat.Skip;
            var header = skipped
                ? UiText.Format("TableLoc_TtcColumnNSkip", c + 1)
                : UiText.Format("TableLoc_TtcColumnN", c + 1);
            AddTextToColumnsPreviewCell(grid, header, row: 0, column: c, isHeader: true, isSkipped: skipped);
        }

        for (var r = 0; r < preview.SampleRows.Count; r++)
        {
            var fields = preview.SampleRows[r].Fields;
            for (var c = 0; c < columnCount; c++)
            {
                var skipped = columnFormats.TryGetValue(c, out var f) && f == TextToColumnsColumnFormat.Skip;
                var text = c < fields.Count ? fields[c] : string.Empty;
                AddTextToColumnsPreviewCell(grid, text, row: r + 1, column: c, isHeader: false, isSkipped: skipped);
            }
        }

        return new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = grid,
        };
    }

    /// <summary>
    /// The WPF wizard is a plain <c>Window</c>, so its group captions are normal ink rather than the
    /// accent tint of the shared compact chrome (see <see cref="ApplyPlainWindowGroupBoxChrome"/>).
    /// </summary>
    private static void ApplyTextToColumnsGroupBoxChrome(GroupBox groupBox) =>
        ApplyPlainWindowGroupBoxChrome(groupBox, DataOpsDialogChromeStyle);

    private static void AddTextToColumnsPreviewCell(
        AvaloniaGrid grid,
        string text,
        int row,
        int column,
        bool isHeader,
        bool isSkipped)
    {
        // WPF renders the preview as a GridView: a regular-weight, centred column header strip with
        // hairline column separators, and plain rows without cell grid lines.
        var border = new Border
        {
            BorderBrush = Brush(229, 229, 229),
            BorderThickness = isHeader ? new Thickness(0, 0, 1, 1) : new Thickness(0),
            Padding = isHeader ? new Thickness(6, 3) : new Thickness(6, 2.5),
            Child = new TextBlock
            {
                Text = text,
                FontWeight = FontWeight.Normal,
                Foreground = isSkipped ? HeaderForeground : Brushes.Black,
                TextTrimming = TextTrimming.CharacterEllipsis,
                FontSize = 12,
                TextAlignment = isHeader ? TextAlignment.Center : TextAlignment.Left,
            },
        };

        AvaloniaGrid.SetRow(border, row);
        AvaloniaGrid.SetColumn(border, column);
        grid.Children.Add(border);
    }
}
