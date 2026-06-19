using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;

using FreeX.App.Presentation.TextToColumns;
using FreeX.Core.Commands;
using FreeX.Core.Model;

using AvaloniaGrid = Avalonia.Controls.Grid;
using AvaloniaHorizontalAlignment = Avalonia.Layout.HorizontalAlignment;
using AvaloniaVerticalAlignment = Avalonia.Layout.VerticalAlignment;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    /// <summary>The per-column format choices the dialog offers, in dropdown order.</summary>
    private static readonly IReadOnlyList<(TextToColumnsColumnFormat Format, string Label)> TextToColumnsFormatChoices =
    [
        (TextToColumnsColumnFormat.General, "General"),
        (TextToColumnsColumnFormat.Text, "Text"),
        (TextToColumnsColumnFormat.DateMDY, "Date"),
        (TextToColumnsColumnFormat.Skip, "Skip"),
    ];

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
            ShowEditIssue("Select a single column of cells to convert with Text to Columns.");
            return;
        }

        var sources = ReadTextToColumnsSources(sheet, range);
        if (sources.Count == 0)
        {
            ShowEditIssue($"No text to split in {FormatRangeReference(range)}.");
            return;
        }

        var dialog = new Window
        {
            Title = "Text to Columns",
            Width = 520,
            Height = 520,
            MinWidth = 460,
            MinHeight = 440,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };
        AutomationProperties.SetAutomationId(dialog, "TextToColumnsDialog");

        var delimitedButton = new RadioButton { Content = "Delimited", IsChecked = true, GroupName = "TtcMode" };
        AutomationProperties.SetAutomationId(delimitedButton, "TextToColumnsDelimitedButton");
        var fixedWidthButton = new RadioButton { Content = "Fixed width", GroupName = "TtcMode" };
        AutomationProperties.SetAutomationId(fixedWidthButton, "TextToColumnsFixedWidthButton");

        var tabBox = new CheckBox { Content = "Tab" };
        AutomationProperties.SetAutomationId(tabBox, "TextToColumnsTabBox");
        var semicolonBox = new CheckBox { Content = "Semicolon" };
        AutomationProperties.SetAutomationId(semicolonBox, "TextToColumnsSemicolonBox");
        var commaBox = new CheckBox { Content = "Comma", IsChecked = true };
        AutomationProperties.SetAutomationId(commaBox, "TextToColumnsCommaBox");
        var spaceBox = new CheckBox { Content = "Space" };
        AutomationProperties.SetAutomationId(spaceBox, "TextToColumnsSpaceBox");
        var otherBox = new CheckBox { Content = "Other" };
        AutomationProperties.SetAutomationId(otherBox, "TextToColumnsOtherBox");
        var otherCharBox = new TextBox { Width = 44, MaxLength = 1 };
        AutomationProperties.SetAutomationId(otherCharBox, "TextToColumnsOtherCharBox");

        var treatConsecutiveBox = new CheckBox { Content = "Treat consecutive delimiters as one" };
        AutomationProperties.SetAutomationId(treatConsecutiveBox, "TextToColumnsTreatConsecutiveBox");

        var qualifierBox = new ComboBox
        {
            ItemsSource = new[] { "\"", "'", "(none)" },
            SelectedIndex = 0,
            MinWidth = 90,
        };
        AutomationProperties.SetAutomationId(qualifierBox, "TextToColumnsQualifierBox");

        var breaksBox = new TextBox { PlaceholderText = "e.g. 5, 12, 20", MinWidth = 160 };
        AutomationProperties.SetAutomationId(breaksBox, "TextToColumnsBreaksBox");

        var formatColumnBox = new ComboBox { MinWidth = 110 };
        AutomationProperties.SetAutomationId(formatColumnBox, "TextToColumnsFormatColumnBox");
        var formatBox = new ComboBox
        {
            ItemsSource = TextToColumnsFormatChoices.Select(c => c.Label).ToList(),
            SelectedIndex = 0,
            MinWidth = 110,
        };
        AutomationProperties.SetAutomationId(formatBox, "TextToColumnsFormatBox");

        var previewHost = new Border
        {
            BorderBrush = HeaderForeground,
            BorderThickness = new Thickness(1),
            MinHeight = 120,
            Padding = new Thickness(0),
        };
        AutomationProperties.SetAutomationId(previewHost, "TextToColumnsPreviewGrid");

        var statusText = new TextBlock
        {
            Foreground = HeaderForeground,
            TextWrapping = TextWrapping.Wrap,
        };
        var warningText = new TextBlock
        {
            Foreground = Brush(180, 30, 30),
            TextWrapping = TextWrapping.Wrap,
            IsVisible = false,
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
                statusText.Text = ex.Message;
                return;
            }

            var preview = TextToColumnsPlanner.Preview(sources, options);
            previewColumnCount = Math.Max(1, preview.ColumnCount);
            statusText.Text = $"Splitting {sources.Count} cell(s) into {previewColumnCount} column(s).";

            previewHost.Child = BuildTextToColumnsPreviewGrid(preview, previewColumnCount, columnFormats);
            RefreshFormatColumnChoices();
        }

        void RefreshFormatColumnChoices()
        {
            var previousIndex = formatColumnBox.SelectedIndex;
            formatColumnBox.ItemsSource = Enumerable.Range(1, previewColumnCount)
                .Select(n => $"Column {n}")
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

        var applyButton = new Button { Content = "Apply", IsDefault = true, MinWidth = 84 };
        AutomationProperties.SetAutomationId(applyButton, "TextToColumnsApplyButton");
        var cancelButton = new Button { Content = "Cancel", IsCancel = true, MinWidth = 84 };
        AutomationProperties.SetAutomationId(cancelButton, "TextToColumnsCancelButton");

        applyButton.Click += (_, _) =>
        {
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
                warningText.Text = "The current options produce no columns to write.";
                warningText.IsVisible = true;
                return;
            }

            var overwrites = TextToColumnsDialogPlanner.FindOverwriteTargets(sheet, edits, range);
            if (overwrites.Count > 0 && !overwriteConfirmed)
            {
                overwriteConfirmed = true;
                warningText.Text =
                    $"This will overwrite data in {overwrites.Count} cell(s) to the right. Click Apply again to continue.";
                warningText.IsVisible = true;
                return;
            }

            if (!ApplyTextToColumnsEdits(sheet.Id, edits, range))
                return;

            dialog.Close();
        };
        cancelButton.Click += (_, _) => dialog.Close();

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
            Children = { new TextBlock { Text = "Text qualifier:", VerticalAlignment = AvaloniaVerticalAlignment.Center }, qualifierBox },
        };

        var breaksRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children = { new TextBlock { Text = "Break positions:", VerticalAlignment = AvaloniaVerticalAlignment.Center }, breaksBox },
        };

        var formatRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children =
            {
                new TextBlock { Text = "Column format:", VerticalAlignment = AvaloniaVerticalAlignment.Center },
                formatColumnBox,
                formatBox,
            },
        };

        var buttonRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Right,
            Margin = new Thickness(0, 10, 0, 0),
            Children = { cancelButton, applyButton },
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
                        Spacing = 8,
                        Children =
                        {
                            new TextBlock
                            {
                                Text = $"Source: {FormatRangeReference(range)}",
                                Foreground = HeaderForeground,
                                TextWrapping = TextWrapping.Wrap,
                            },
                            new StackPanel
                            {
                                Orientation = Orientation.Horizontal,
                                Spacing = 16,
                                Children = { delimitedButton, fixedWidthButton },
                            },
                            delimiterRow,
                            treatConsecutiveBox,
                            qualifierRow,
                            breaksRow,
                            new TextBlock { Text = "Preview", FontWeight = FontWeight.SemiBold },
                            previewHost,
                            formatRow,
                            statusText,
                            warningText,
                        },
                    },
                },
            },
        };

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
            ShowEditIssue(result.ErrorMessage ?? "Text to Columns failed.");
            return false;
        }

        RefreshShell($"Split {FormatRangeReference(range)} into columns");
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
            var header = skipped ? $"Column {c + 1} (skip)" : $"Column {c + 1}";
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
            },
        };

        AvaloniaGrid.SetRow(border, row);
        AvaloniaGrid.SetColumn(border, column);
        grid.Children.Add(border);
    }
}
