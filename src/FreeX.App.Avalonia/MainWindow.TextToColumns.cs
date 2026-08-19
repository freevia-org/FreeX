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
            Title = UiText.Format("TableLoc_TtcWizardTitle", 1, 3),
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
        var otherCharBox = new TextBox { Width = 44, MaxLength = 1 };
        ApplyDataOpsTextBoxChrome(otherCharBox);
        AutomationProperties.SetAutomationId(otherCharBox, "TextToColumnsOtherCharBox");

        var treatConsecutiveBox = new CheckBox { Content = UiText.Get("TableLoc_TtcTreatConsecutive") };
        ApplyDataOpsCheckBoxChrome(treatConsecutiveBox);
        AutomationProperties.SetAutomationId(treatConsecutiveBox, "TextToColumnsTreatConsecutiveBox");

        var qualifierBox = new ComboBox
        {
            ItemsSource = new[] { "\"", "'", UiText.Get("TableLoc_TtcQualifierNone") },
            SelectedIndex = 0,
            MinWidth = 90,
        };
        ApplyDataOpsComboBoxChrome(qualifierBox);
        AutomationProperties.SetAutomationId(qualifierBox, "TextToColumnsQualifierBox");

        var breaksBox = new TextBox { PlaceholderText = UiText.Get("TableLoc_TtcBreaksPlaceholder"), MinWidth = 160 };
        ApplyDataOpsTextBoxChrome(breaksBox);
        AutomationProperties.SetAutomationId(breaksBox, "TextToColumnsBreaksBox");

        var formatColumnBox = new ComboBox { MinWidth = 110 };
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
            MinWidth = 160,
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

        var previewHost = new Border
        {
            BorderBrush = HeaderForeground,
            BorderThickness = new Thickness(1),
            Height = 88,
            MinHeight = 88,
            Padding = new Thickness(0),
        };
        AutomationProperties.SetAutomationId(previewHost, "TextToColumnsPreviewGrid");
        // Step-1 preview is a separate host (Avalonia controls can only have one parent)
        var previewHost1 = new Border
        {
            BorderBrush = HeaderForeground,
            BorderThickness = new Thickness(1),
            Height = 88,
            MinHeight = 88,
            Padding = new Thickness(0),
        };
        AutomationProperties.SetAutomationId(previewHost1, "TextToColumnsPreviewGrid1");

        var statusText = new TextBlock
        {
            Foreground = HeaderForeground,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            FontFamily = FormulaBarFontFamily,
        };
        var warningText = new TextBlock
        {
            Foreground = Brush(180, 30, 30),
            TextWrapping = TextWrapping.Wrap,
            IsVisible = false,
            FontSize = 12,
            FontFamily = FormulaBarFontFamily,
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
                OtherDelimiter: string.IsNullOrEmpty(otherCharBox.Text) ? null : otherCharBox.Text[0],
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
                previewHost1.Child = null;
                statusText.Text = TextToColumnsDialogPlanner
                    .DescribeValidationIssue(previewIssue)
                    .Message.Resolve(UiText.Get, UiText.Format);
                return;
            }

            var preview = TextToColumnsPlanner.Preview(
                sources,
                options,
                TextToColumnsDialogMetrics.PreviewRowLimit);
            previewColumnCount = Math.Max(1, preview.ColumnCount);
            statusText.Text = UiText.Format("TableLoc_TtcSplittingStatus", sources.Count, previewColumnCount);

            previewHost.Child = BuildTextToColumnsPreviewGrid(preview, previewColumnCount, columnFormats);
            // Step-1 preview mirrors the main preview (separate control instance required)
            previewHost1.Child = BuildTextToColumnsPreviewGrid(preview, previewColumnCount, columnFormats);
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

        void UpdateModeVisibility()
        {
            var delimited = fixedWidthButton.IsChecked != true;
            tabBox.IsVisible = delimited;
            semicolonBox.IsVisible = delimited;
            commaBox.IsVisible = delimited;
            spaceBox.IsVisible = delimited;
            otherBox.IsVisible = delimited;
            otherCharBox.IsVisible = delimited;
            treatConsecutiveBox.IsVisible = delimited;
            qualifierBox.IsVisible = delimited;
            breaksBox.IsVisible = !delimited;
            RefreshPreview();
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
        delimitedButton.IsCheckedChanged += (_, _) => UpdateModeVisibility();
        fixedWidthButton.IsCheckedChanged += (_, _) => UpdateModeVisibility();
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

        // Wizard step header — the bold "Text Wizard - Step N of 3" banner the Windows dialog shows at the
        // top of the body (the window title bar isn't captured, so the step indicator must live in-body).
        var wizardStepHeader = new TextBlock
        {
            Text = UiText.Format("TableLoc_TtcWizardTitle", 1, 3),
            FontWeight = FontWeight.Bold,
            FontSize = 13,
            FontFamily = FormulaBarFontFamily,
            Margin = new Thickness(0, 0, 0, 2),
        };
        AutomationProperties.SetAutomationId(wizardStepHeader, "TextToColumnsWizardStepHeader");

        // Wizard step tracking — declare forward refs for SyncWizardNavigation closure
        var currentStep = 1;
        const int totalSteps = 3;
        StackPanel? step1Content = null;
        StackPanel? step2Content = null;
        StackPanel? step3Content = null;

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
            var wizardTitle = UiText.Format("TableLoc_TtcWizardTitle", currentStep, totalSteps);
            dialog.Title = wizardTitle;
            wizardStepHeader.Text = wizardTitle;
            backButton.IsEnabled = currentStep > 1;
            nextButton.IsEnabled = currentStep < totalSteps;
            // Step visibility (step*Content assigned after this function definition)
            if (step1Content is not null) step1Content.IsVisible = currentStep == 1;
            if (step2Content is not null) step2Content.IsVisible = currentStep == 2;
            if (step3Content is not null) step3Content.IsVisible = currentStep == 3;
        }

        applyButton.Click += (_, _) => ApplyWizardStep();
        cancelButton.Click += (_, _) => dialog.Close();
        backButton.Click += (_, _) =>
        {
            if (currentStep > 1)
            {
                currentStep--;
                SyncWizardNavigation();
                FocusCurrentWizardStepTarget();
            }
        };
        nextButton.Click += (_, _) =>
        {
            if (currentStep < totalSteps)
            {
                currentStep++;
                SyncWizardNavigation();
                FocusCurrentWizardStepTarget();
            }
        };

        var delimiterRow = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            Children = { tabBox, semicolonBox, commaBox, spaceBox, otherBox, otherCharBox },
        };
        foreach (var child in delimiterRow.Children)
            child.Margin = new Thickness(0, 0, 12, 4);

        var qualifierRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children = { new TextBlock { Text = UiText.Get("TableLoc_TtcTextQualifierLabel"), VerticalAlignment = AvaloniaVerticalAlignment.Center, FontSize = 12, FontFamily = FormulaBarFontFamily }, qualifierBox },
        };

        var breaksRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children = { new TextBlock { Text = UiText.Get("TableLoc_TtcBreakPositionsLabel"), VerticalAlignment = AvaloniaVerticalAlignment.Center, FontSize = 12, FontFamily = FormulaBarFontFamily }, breaksBox },
        };

        var formatRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children =
            {
                new TextBlock { Text = UiText.Get("TableLoc_TtcColumnFormatLabel"), VerticalAlignment = AvaloniaVerticalAlignment.Center, FontSize = 12, FontFamily = FormulaBarFontFamily },
                formatColumnBox,
                formatBox,
            },
        };

        var destinationRow = new StackPanel
        {
            Spacing = 4,
            Children =
            {
                new TextBlock
                {
                    Text = StripDisplayMnemonic(UiText.Get("TextToColumns_DestinationLabel")),
                    FontSize = 12,
                    FontFamily = FormulaBarFontFamily,
                },
                BuildDialogRangePickerRow(destinationBox, destinationPicker),
            },
        };

        // Advanced options group (WPF parity: TextToColumnsDialog.ColumnFormats.cs's
        // CreateAdvancedOptionsPanel) -- decimal/thousands separator overrides + trailing-minus checkbox.
        var advancedSeparatorsRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children =
            {
                new Label
                {
                    Content = UiText.Get("TextToColumns_DecimalSeparatorLabel"),
                    Target = decimalSeparatorBox,
                    Padding = new Thickness(0),
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
                    VerticalAlignment = AvaloniaVerticalAlignment.Center,
                    FontSize = 12,
                    FontFamily = FormulaBarFontFamily,
                },
                thousandsSeparatorBox,
            },
        };

        var advancedOptionsGroup = new GroupBox
        {
            Header = UiText.Get("TextToColumns_AdvancedGroup"),
            Content = new StackPanel
            {
                Spacing = 8,
                Margin = new Thickness(4),
                Children = { advancedSeparatorsRow, trailingMinusBox },
            },
            Margin = new Thickness(0, 8, 0, 0),
        };

        var originalDataTypeGroup = new GroupBox
        {
            Header = UiText.Get("TableLoc_TtcOriginalDataType"),
            Padding = new Thickness(8, 2),
            Margin = new Thickness(0, 0, 0, 8),
            Content = new StackPanel
            {
                Children = { delimitedButton, fixedWidthButton },
            },
        };
        AvaloniaCompactDialogChrome.ApplyGroupBox(originalDataTypeGroup);

        // Step 1: Choose the file type / original data type + preview (assigns forward-declared var)
        step1Content = new StackPanel
        {
            Spacing = 0,
            Children =
            {
                new TextBlock
                {
                    Text = UiText.Get("TableLoc_TtcStep1Description"),
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 12,
                    FontFamily = FormulaBarFontFamily,
                    Margin = new Thickness(0, 0, 0, 11),
                },
                originalDataTypeGroup,
                new TextBlock
                {
                    Text = UiText.Get("TableLoc_TtcPreviewLabel"),
                    FontWeight = FontWeight.SemiBold,
                    FontSize = 12,
                    FontFamily = FormulaBarFontFamily,
                    Margin = new Thickness(0, 9, 0, 7),
                },
                previewHost1,
            },
        };

        // Step 2: Delimiters / Fixed-width break positions + preview
        step2Content = new StackPanel
        {
            Spacing = 8,
            IsVisible = false,
            Children =
            {
                new TextBlock
                {
                    Text = UiText.Format("TableLoc_TtcSourceLabel", FormatRangeReference(range)),
                    Foreground = HeaderForeground,
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 12,
                    FontFamily = FormulaBarFontFamily,
                },
                delimiterRow,
                treatConsecutiveBox,
                qualifierRow,
                breaksRow,
                new TextBlock
                {
                    Text = UiText.Get("TableLoc_TtcPreviewLabel"),
                    FontWeight = FontWeight.SemiBold,
                    FontSize = 12,
                    FontFamily = FormulaBarFontFamily,
                    Margin = new Thickness(0, 9, 0, 7),
                },
                previewHost,
            },
        };

        // Step 3: Column format options
        step3Content = new StackPanel
        {
            Spacing = 8,
            IsVisible = false,
            Children =
            {
                formatRow,
                destinationRow,
                advancedOptionsGroup,
                statusText,
                warningText,
            },
        };

        // WPF wizard nav button order: [< Back][Next >][Finish][Cancel]
        var buttonRow = AvaloniaCompactDialogChrome.CreateActionRow(
            [backButton, nextButton, applyButton, cancelButton],
            new Thickness(0, 12, 0, 0));
        DockPanel.SetDock(buttonRow, Dock.Bottom);

        dialog.Content = new DockPanel
        {
            Margin = new Thickness(12),
            Children =
            {
                buttonRow,
                new ScrollViewer
                {
                    Content = new StackPanel
                    {
                        Spacing = 0,
                        Children =
                        {
                            wizardStepHeader,
                            step1Content,
                            step2Content,
                            step3Content,
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
        UpdateModeVisibility();
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

    private static void AddTextToColumnsPreviewCell(
        AvaloniaGrid grid,
        string text,
        int row,
        int column,
        bool isHeader,
        bool isSkipped)
    {
        var border = new Border
        {
            BorderBrush = Brush(214, 214, 214),
            BorderThickness = new Thickness(0, 0, 1, 1),
            Padding = new Thickness(6, 2.5),
            Child = new TextBlock
            {
                Text = text,
                FontWeight = isHeader ? FontWeight.SemiBold : FontWeight.Normal,
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
