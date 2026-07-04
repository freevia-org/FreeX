using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;

using Free.Shared.Shell.Avalonia;
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
    private void TextToColumns() => _ = ShowTextToColumnsDialogAsync();

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
            Width = TextToColumnsParityDialogWidth,
            Height = TextToColumnsParityDialogHeight,
            MinWidth = TextToColumnsParityDialogWidth,
            MinHeight = TextToColumnsParityDialogHeight,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };
        AutomationProperties.SetAutomationId(dialog, "TextToColumnsDialog");

        var delimitedButton = new RadioButton { Content = UiText.Get("TableLoc_TtcDelimited"), IsChecked = true, GroupName = "TtcMode" };
        ApplyDataOpsRadioButtonChrome(delimitedButton);
        AutomationProperties.SetAutomationId(delimitedButton, "TextToColumnsDelimitedButton");
        var fixedWidthButton = new RadioButton { Content = UiText.Get("TableLoc_TtcFixedWidth"), GroupName = "TtcMode" };
        ApplyDataOpsRadioButtonChrome(fixedWidthButton);
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

        var previewHost = new Border
        {
            BorderBrush = HeaderForeground,
            BorderThickness = new Thickness(1),
            MinHeight = 120,
            Padding = new Thickness(0),
        };
        AutomationProperties.SetAutomationId(previewHost, "TextToColumnsPreviewGrid");
        // Step-1 preview is a separate host (Avalonia controls can only have one parent)
        var previewHost1 = new Border
        {
            BorderBrush = HeaderForeground,
            BorderThickness = new Thickness(1),
            MinHeight = 120,
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
                FixedWidthBreakPositions: ParseBreakPositions(breaksBox.Text),
                ColumnFormats: orderedFormats);
        }

        void RefreshPreview()
        {
            overwriteConfirmed = false;
            warningText.IsVisible = false;

            TextToColumnsOptions options;
            try
            {
                options = TextToColumnsDialogPlanner.BuildOptions(BuildState());
            }
            catch (ArgumentException ex)
            {
                previewHost.Child = null;
                previewHost1.Child = null;
                statusText.Text = ex.Message;
                return;
            }

            var preview = TextToColumnsPlanner.Preview(sources, options);
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
        var backButton = new Button { Content = UiText.Get("TableLoc_TtcBack"), MinWidth = 84, IsEnabled = false };
        ApplyDataOpsButtonChrome(backButton);
        AutomationProperties.SetAutomationId(backButton, "TextToColumnsBackButton");
        var nextButton = new Button { Content = UiText.Get("TableLoc_TtcNext"), MinWidth = 84 };
        ApplyDataOpsButtonChrome(nextButton);
        AutomationProperties.SetAutomationId(nextButton, "TextToColumnsNextButton");
        var applyButton = new Button { Content = UiText.Get("TableLoc_TtcFinish"), IsDefault = true, MinWidth = 84 };
        ApplyDataOpsButtonChrome(applyButton, isDefault: true);
        AutomationProperties.SetAutomationId(applyButton, "TextToColumnsApplyButton");
        var cancelButton = new Button { Content = UiText.Get("TableLoc_Cancel"), IsCancel = true, MinWidth = 84 };
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

        void ApplyWizardStep()
        {
            warningText.IsVisible = false;
            overwriteConfirmed = false;

            TextToColumnsOptions options;
            try
            {
                options = TextToColumnsDialogPlanner.BuildOptions(BuildState());
            }
            catch (ArgumentException ex)
            {
                warningText.Text = ex.Message;
                warningText.IsVisible = true;
                return;
            }

            var result = TextToColumnsPlanner.Plan(sources, options);
            var edits = TextToColumnsDialogPlanner.MapToEdits(sheet.Id, result, range);
            if (edits.Count == 0)
            {
                warningText.Text = UiText.Get("TableLoc_TtcNoColumnsToWrite");
                warningText.IsVisible = true;
                return;
            }

            var overwrites = TextToColumnsDialogPlanner.FindOverwriteTargets(sheet, edits, range);
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
            }
        };
        nextButton.Click += (_, _) =>
        {
            if (currentStep < totalSteps)
            {
                currentStep++;
                SyncWizardNavigation();
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

        // Step 1: Choose the file type / original data type + preview (assigns forward-declared var)
        step1Content = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                new TextBlock
                {
                    Text = UiText.Get("TableLoc_TtcStep1Description"),
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 12,
                    FontFamily = FormulaBarFontFamily,
                },
                new Border
                {
                    BorderBrush = Brush(171, 173, 179),
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(8),
                    Child = new StackPanel
                    {
                        Spacing = 6,
                        Children =
                        {
                            new TextBlock
                            {
                                Text = UiText.Get("TableLoc_TtcOriginalDataType"),
                                FontWeight = FontWeight.SemiBold,
                                FontSize = 12,
                                FontFamily = FormulaBarFontFamily,
                            },
                            delimitedButton,
                            fixedWidthButton,
                        },
                    },
                },
                new TextBlock { Text = UiText.Get("TableLoc_TtcPreviewLabel"), FontWeight = FontWeight.SemiBold, FontSize = 12, FontFamily = FormulaBarFontFamily },
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
                new TextBlock { Text = UiText.Get("TableLoc_TtcPreviewLabel"), FontWeight = FontWeight.SemiBold, FontSize = 12, FontFamily = FormulaBarFontFamily },
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
                statusText,
                warningText,
            },
        };

        // WPF wizard nav button order: [< Back][Next >][Finish][Cancel]
        var buttonRow = AvaloniaCompactDialogChrome.CreateActionRow(
            [backButton, nextButton, applyButton, cancelButton],
            new Thickness(0, 10, 0, 0));
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
                        Spacing = 8,
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
            texts.Add(FormatScalarValue(sheet.GetValue(row, col)));

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

    /// <summary>Parses a comma/space-separated list of fixed-width break positions, ignoring junk tokens.</summary>
    private static IReadOnlyList<int> ParseBreakPositions(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [];

        var positions = new List<int>();
        foreach (var token in text.Split([',', ' ', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (int.TryParse(token, out var value) && value > 0)
                positions.Add(value);
        }

        return positions;
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
        var grid = new AvaloniaGrid { Margin = new Thickness(1) };
        for (var c = 0; c < columnCount; c++)
            grid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));

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
            BorderBrush = HeaderForeground,
            BorderThickness = new Thickness(0, 0, 1, 1),
            Padding = new Thickness(6, 3),
            Child = new TextBlock
            {
                Text = text,
                FontWeight = isHeader ? FontWeight.SemiBold : FontWeight.Normal,
                Foreground = isSkipped ? HeaderForeground : Brushes.Black,
                TextTrimming = TextTrimming.CharacterEllipsis,
                FontSize = 12,
            },
        };

        AvaloniaGrid.SetRow(border, row);
        AvaloniaGrid.SetColumn(border, column);
        grid.Children.Add(border);
    }
}
