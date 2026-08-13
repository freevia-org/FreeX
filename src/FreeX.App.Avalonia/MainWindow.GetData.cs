using System.IO;

using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;

using FreeX.App.Presentation.Import;
using FreeX.App.Services;
using Free.Shared.Shell.Avalonia;
using FreeX.Core.Commands;
using FreeX.Core.IO;
using FreeX.Core.Model;

using AvaloniaGrid = Avalonia.Controls.Grid;
using VerticalAlignment = Avalonia.Layout.VerticalAlignment;
using HorizontalAlignment = Avalonia.Layout.HorizontalAlignment;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    // -------------------------------------------------------------------------------------------------------
    // GetData dialog chrome helpers
    // -------------------------------------------------------------------------------------------------------

    private static AvaloniaCompactDialogChromeStyle GetDataDialogChromeStyle => new(FormulaBarFontFamily);

    private static void ApplyGetDataButtonChrome(Button button, double minWidth = 80, bool isDefault = false)
        => AvaloniaCompactDialogChrome.ApplyButton(button, GetDataDialogChromeStyle, minWidth, isDefault);

    private static void ApplyGetDataTextBoxChrome(TextBox tb)
        => AvaloniaCompactDialogChrome.ApplyTextBox(tb, GetDataDialogChromeStyle);

    private static void ApplyGetDataComboBoxChrome(ComboBox cb)
        => AvaloniaCompactDialogChrome.ApplyComboBox(cb, GetDataDialogChromeStyle);

    private static void ApplyGetDataCheckBoxChrome(CheckBox cb)
    {
        StripContentMnemonic(cb);
        AvaloniaCompactDialogChrome.ApplyCheckBox(cb, GetDataDialogChromeStyle);
    }

    private static void ApplyGetDataRadioButtonChrome(RadioButton rb)
    {
        StripContentMnemonic(rb);
        AvaloniaCompactDialogChrome.ApplyRadioButton(rb, GetDataDialogChromeStyle);
    }

    // The most recent file-backed import, remembered so Data ▸ Refresh All can re-run it without prompting.
    private ImportDataSource? _lastImportSource;

    // The delimiter dropdown order. The index maps to a kind; "Custom" reveals the custom-character box.
    private static readonly IReadOnlyList<(ImportDelimiterKind Kind, string Key)> GetDataDelimiterChoices =
    [
        (ImportDelimiterKind.Detect, "GetData_DelimiterDetect"),
        (ImportDelimiterKind.Comma, "GetData_DelimiterComma"),
        (ImportDelimiterKind.Tab, "GetData_DelimiterTab"),
        (ImportDelimiterKind.Semicolon, "GetData_DelimiterSemicolon"),
        (ImportDelimiterKind.Space, "GetData_DelimiterSpace"),
        (ImportDelimiterKind.Pipe, "GetData_DelimiterPipe"),
        (ImportDelimiterKind.Custom, "GetData_DelimiterCustom"),
    ];

    private static readonly IReadOnlyList<(ImportEncodingKind Kind, string Key)> GetDataEncodingChoices =
    [
        (ImportEncodingKind.Detect, "GetData_EncodingDetect"),
        (ImportEncodingKind.Utf8, "GetData_EncodingUtf8"),
        (ImportEncodingKind.Utf16Le, "GetData_EncodingUtf16Le"),
        (ImportEncodingKind.Utf16Be, "GetData_EncodingUtf16Be"),
        (ImportEncodingKind.Windows1252, "GetData_EncodingWindows1252"),
        (ImportEncodingKind.Latin1, "GetData_EncodingLatin1"),
    ];

    /// <summary>Opens the Get Data ▸ From Text/CSV import dialog (Data-tab Get Data button / menu).</summary>
    private void GetDataFromText() => _ = ShowGetDataDialogAsync();

    /// <summary>
    /// The Get Data / From Text-CSV import dialog: Browse to a CSV/TSV/text file, choose the delimiter
    /// (auto-detect/comma/tab/semicolon/space/pipe/custom) and the encoding (auto-detect or an explicit
    /// code page), decide whether to load into the current sheet at the active cell or a new sheet, and
    /// confirm against a live preview. All non-UI decisions (delimiter/encoding resolution, byte decode,
    /// the split preview) run through the portable <see cref="ImportDataPlanner"/>; the parse reuses the
    /// existing delimited-text reader and the load applies via <see cref="ImportSheetCommand"/> on the
    /// shared session command path (undoable + recalc). External DB/web connectors are out of scope.
    /// </summary>
    private async Task ShowGetDataDialogAsync()
    {
        if (_isOpening || _isSaving || !TryCommitPendingFormulaEdit())
            return;

        if (!((IStorageProvider)StorageProvider).CanOpen)
        {
            ShowEditIssue(UiText.Get("GetData_ImportFailed"));
            return;
        }

        var dialog = new Window
        {
            Title = UiText.Get("GetData_DialogTitle"),
            Width = 600,
            Height = 560,
            MinWidth = 520,
            MinHeight = 460,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };
        AutomationProperties.SetAutomationId(dialog, "GetDataDialog");

        var fileLabel = new TextBlock { Text = UiText.Get("GetData_FileLabel"), VerticalAlignment = VerticalAlignment.Center, FontSize = 12, FontFamily = FormulaBarFontFamily };
        var fileBox = new TextBox { IsReadOnly = true, MinWidth = 320 };
        ApplyGetDataTextBoxChrome(fileBox);
        AutomationProperties.SetAutomationId(fileBox, "GetDataFileBox");
        var browseButton = new Button { Content = UiText.Get("GetData_BrowseButton"), MinWidth = 90 };
        ApplyGetDataButtonChrome(browseButton, minWidth: 90);
        AutomationProperties.SetAutomationId(browseButton, "GetDataBrowseButton");

        var fileRow = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 0, 0, 8) };
        DockPanel.SetDock(fileLabel, Dock.Left);
        DockPanel.SetDock(browseButton, Dock.Right);
        fileLabel.Margin = new Thickness(0, 0, 8, 0);
        browseButton.Margin = new Thickness(8, 0, 0, 0);
        fileRow.Children.Add(fileLabel);
        fileRow.Children.Add(browseButton);
        fileRow.Children.Add(fileBox);

        var delimiterBox = new ComboBox
        {
            ItemsSource = GetDataDelimiterChoices.Select(c => UiText.Get(c.Key)).ToList(),
            SelectedIndex = 0,
            MinWidth = 220,
        };
        ApplyGetDataComboBoxChrome(delimiterBox);
        AutomationProperties.SetAutomationId(delimiterBox, "GetDataDelimiterBox");

        var customDelimiterBox = new TextBox { MaxLength = 1, Width = 48, IsVisible = false };
        ApplyGetDataTextBoxChrome(customDelimiterBox);
        AutomationProperties.SetAutomationId(customDelimiterBox, "GetDataCustomDelimiterBox");

        var encodingBox = new ComboBox
        {
            ItemsSource = GetDataEncodingChoices.Select(c => UiText.Get(c.Key)).ToList(),
            SelectedIndex = 0,
            MinWidth = 220,
        };
        ApplyGetDataComboBoxChrome(encodingBox);
        AutomationProperties.SetAutomationId(encodingBox, "GetDataEncodingBox");

        var treatConsecutiveBox = new CheckBox { Content = UiText.Get("GetData_TreatConsecutive") };
        ApplyGetDataCheckBoxChrome(treatConsecutiveBox);
        AutomationProperties.SetAutomationId(treatConsecutiveBox, "GetDataTreatConsecutiveBox");

        var currentSheetButton = new RadioButton
        {
            Content = UiText.Get("GetData_DestinationCurrentSheet"),
            GroupName = "GetDataDestination",
            IsChecked = true,
        };
        ApplyGetDataRadioButtonChrome(currentSheetButton);
        AutomationProperties.SetAutomationId(currentSheetButton, "GetDataCurrentSheetButton");
        var newSheetButton = new RadioButton
        {
            Content = UiText.Get("GetData_DestinationNewSheet"),
            GroupName = "GetDataDestination",
        };
        ApplyGetDataRadioButtonChrome(newSheetButton);
        AutomationProperties.SetAutomationId(newSheetButton, "GetDataNewSheetButton");

        var previewSummary = new TextBlock
        {
            Text = UiText.Get("GetData_PreviewEmpty"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 4),
            FontSize = 12,
            FontFamily = FormulaBarFontFamily,
        };
        AutomationProperties.SetAutomationId(previewSummary, "GetDataPreviewSummary");

        var previewHost = new ScrollViewer
        {
            HorizontalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            MinHeight = 160,
        };
        AutomationProperties.SetAutomationId(previewHost, "GetDataPreviewGrid");

        var warningText = new TextBlock
        {
            Foreground = Brushes.Firebrick,
            TextWrapping = TextWrapping.Wrap,
            IsVisible = false,
            Margin = new Thickness(0, 4, 0, 0),
        };
        AutomationProperties.SetAutomationId(warningText, "GetDataWarningText");

        string? selectedPath = null;
        string? decodedText = null;

        ImportDataOptions BuildOptions()
        {
            var delimiterKind = GetDataDelimiterChoices[Math.Max(0, delimiterBox.SelectedIndex)].Kind;
            var encodingKind = GetDataEncodingChoices[Math.Max(0, encodingBox.SelectedIndex)].Kind;
            char? custom = delimiterKind == ImportDelimiterKind.Custom &&
                !string.IsNullOrEmpty(customDelimiterBox.Text)
                ? customDelimiterBox.Text![0]
                : null;
            return new ImportDataOptions
            {
                Delimiter = delimiterKind,
                CustomDelimiter = custom,
                Encoding = encodingKind,
                TreatConsecutiveDelimitersAsOne = treatConsecutiveBox.IsChecked == true,
                Destination = newSheetButton.IsChecked == true
                    ? ImportDestinationKind.NewSheet
                    : ImportDestinationKind.CurrentSheet,
            };
        }

        void RefreshPreview()
        {
            customDelimiterBox.IsVisible =
                GetDataDelimiterChoices[Math.Max(0, delimiterBox.SelectedIndex)].Kind == ImportDelimiterKind.Custom;

            if (decodedText is null)
            {
                previewSummary.Text = UiText.Get("GetData_PreviewEmpty");
                previewHost.Content = null;
                return;
            }

            var options = BuildOptions();
            var preview = ImportDataPlanner.PreviewText(decodedText, options, sampleRowLimit: 30);
            previewSummary.Text = UiText.Format(
                "GetData_PreviewSummary",
                DescribeDelimiter(preview.Delimiter),
                preview.EncodingName,
                preview.TotalRowCount,
                preview.ColumnCount);
            previewHost.Content = BuildGetDataPreviewGrid(preview);
        }

        async Task BrowseAsync()
        {
            var pickerPlan = ImportDataFilePickerPlanner.BuildTextOpenPickerPlan(UiText.Get("GetData_FileTypeName"));
            using var pickedFile = await AvaloniaFilePickerService.PickSingleOpenFileWithLocalPathAsync(
                StorageProvider,
                AvaloniaFilePickerOpenRequest.FromDescriptors(
                    UiText.Get("GetData_FilePickerTitle"),
                    pickerPlan.FileTypes));

            if (pickedFile is null)
                return;

            var path = pickedFile.LocalPath;
            if (string.IsNullOrEmpty(path))
            {
                warningText.Text = UiText.Format("GetData_ReadError", pickedFile.Name);
                warningText.IsVisible = true;
                return;
            }

            byte[] bytes;
            try
            {
                await using var stream = await pickedFile.StorageFile.OpenReadAsync();
                using var memory = new MemoryStream();
                await stream.CopyToAsync(memory);
                bytes = memory.ToArray();
            }
            catch (IOException ex)
            {
                warningText.Text = UiText.Format("GetData_ReadError", ex.Message);
                warningText.IsVisible = true;
                return;
            }

            selectedPath = path;
            fileBox.Text = path;
            var encodingKind = GetDataEncodingChoices[Math.Max(0, encodingBox.SelectedIndex)].Kind;
            decodedText = ImportDataPlanner.DecodeBytes(bytes, encodingKind);
            warningText.IsVisible = false;
            RefreshPreview();
        }

        // Re-decode the (cached) bytes when the encoding changes by reloading from disk lazily on next
        // browse; for simplicity the encoding change re-reads via the stored path so the preview stays true.
        async Task ReDecodeAsync()
        {
            if (selectedPath is null || !File.Exists(selectedPath))
            {
                RefreshPreview();
                return;
            }

            try
            {
                var bytes = await File.ReadAllBytesAsync(selectedPath);
                var encodingKind = GetDataEncodingChoices[Math.Max(0, encodingBox.SelectedIndex)].Kind;
                decodedText = ImportDataPlanner.DecodeBytes(bytes, encodingKind);
            }
            catch (IOException)
            {
                // Keep the previous decode; the preview simply reflects the last good read.
            }

            RefreshPreview();
        }

        browseButton.Click += (_, _) => _ = BrowseAsync();
        delimiterBox.SelectionChanged += (_, _) => RefreshPreview();
        customDelimiterBox.TextChanged += (_, _) => RefreshPreview();
        treatConsecutiveBox.IsCheckedChanged += (_, _) => RefreshPreview();
        encodingBox.SelectionChanged += (_, _) => _ = ReDecodeAsync();

        var loadButton = new Button { Content = UiText.Get("GetData_LoadButton"), IsDefault = true, MinWidth = 90 };
        ApplyGetDataButtonChrome(loadButton, minWidth: 90, isDefault: true);
        AutomationProperties.SetAutomationId(loadButton, "GetDataLoadButton");
        var cancelButton = new Button { Content = UiText.Get("GetData_CancelButton"), IsCancel = true, MinWidth = 90 };
        ApplyGetDataButtonChrome(cancelButton, minWidth: 90);
        AutomationProperties.SetAutomationId(cancelButton, "GetDataCancelButton");

        loadButton.Click += (_, _) =>
        {
            if (selectedPath is null || decodedText is null)
            {
                warningText.Text = UiText.Get("GetData_NoFileSelected");
                warningText.IsVisible = true;
                return;
            }

            var options = BuildOptions();
            if (!TryImportFromText(selectedPath, decodedText, options, out var error))
            {
                warningText.Text = error ?? UiText.Get("GetData_ImportFailed");
                warningText.IsVisible = true;
                return;
            }

            dialog.Close();
        };
        cancelButton.Click += (_, _) => dialog.Close();

        var optionsGrid = new AvaloniaGrid { Margin = new Thickness(0, 0, 0, 8) };
        optionsGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        optionsGrid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
        for (var i = 0; i < 4; i++)
            optionsGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        void AddOptionRow(int row, string headerKey, Control control)
        {
            var header = new TextBlock
            {
                Text = UiText.Get(headerKey),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 4, 12, 4),
                FontSize = 12,
                FontFamily = FormulaBarFontFamily,
            };
            AvaloniaGrid.SetRow(header, row);
            AvaloniaGrid.SetColumn(header, 0);
            AvaloniaGrid.SetRow(control, row);
            AvaloniaGrid.SetColumn(control, 1);
            control.Margin = new Thickness(0, 4, 0, 4);
            optionsGrid.Children.Add(header);
            optionsGrid.Children.Add(control);
        }

        var delimiterPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        delimiterPanel.Children.Add(delimiterBox);
        delimiterPanel.Children.Add(customDelimiterBox);

        var destinationPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16 };
        destinationPanel.Children.Add(currentSheetButton);
        destinationPanel.Children.Add(newSheetButton);

        AddOptionRow(0, "GetData_DelimiterHeader", delimiterPanel);
        AddOptionRow(1, "GetData_EncodingHeader", encodingBox);
        AddOptionRow(2, "GetData_DestinationHeader", destinationPanel);
        AddOptionRow(3, "GetData_PreviewHeader", treatConsecutiveBox);

        var buttonRow = AvaloniaCompactDialogChrome.CreateActionRow([loadButton, cancelButton], new Thickness(0, 8, 0, 0));

        var root = new DockPanel { Margin = new Thickness(16), LastChildFill = true };
        DockPanel.SetDock(fileRow, Dock.Top);
        DockPanel.SetDock(optionsGrid, Dock.Top);
        DockPanel.SetDock(previewSummary, Dock.Top);
        DockPanel.SetDock(warningText, Dock.Bottom);
        DockPanel.SetDock(buttonRow, Dock.Bottom);
        root.Children.Add(fileRow);
        root.Children.Add(optionsGrid);
        root.Children.Add(previewSummary);
        root.Children.Add(buttonRow);
        root.Children.Add(warningText);
        root.Children.Add(previewHost);

        dialog.Content = root;
        RefreshPreview();
        await dialog.ShowDialog(this);
    }

    /// <summary>
    /// Parses <paramref name="decodedText"/> into a workbook through the existing delimited-text reader
    /// (which performs the value coercion), then applies its first sheet at the resolved destination via
    /// <see cref="ImportSheetCommand"/>. Remembers the source (including the exact anchor written to) so
    /// Refresh can re-run it. When <paramref name="anchorOverride"/> is supplied and its sheet still
    /// exists, the import re-targets that exact anchor instead of resolving a destination fresh (see
    /// <see cref="RefreshImportedData"/> / R88-io-text-import-wizard-5-1) — this is what lets a refresh
    /// land back on the original B2:D10-style block instead of the current selection, and what stops a
    /// repeated new-sheet refresh from adding a new sheet on every run. Returns false with a user message
    /// on failure.
    /// </summary>
    private bool TryImportFromText(
        string filePath,
        string decodedText,
        ImportDataOptions options,
        out string? error,
        CellAddress? anchorOverride = null)
    {
        error = null;
        var delimiter = ImportDataPlanner.ResolveDelimiter(options, decodedText);

        Workbook imported;
        try
        {
            // Re-encode the decoded text as UTF-8 and hand it to the shared reader so the import shares the
            // exact value-coercion (formulas, errors, dates, numbers) used by a plain CSV file open.
            var utf8 = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
            using var stream = new MemoryStream(utf8.GetBytes(decodedText));
            // R88-io-text-import-wizard-5-3: only let an embedded "sep=X" directive override the active
            // delimiter when the user left the wizard's delimiter choice on Detect; an explicit pick (e.g.
            // Comma) must win, or the confirmed preview and the actual import can silently disagree.
            var allowSeparatorDirective = options.Delimiter == ImportDelimiterKind.Detect;
            // R88-io-text-import-wizard-5-2: forward "treat consecutive delimiters as one" into the real
            // parse too, so the committed import matches the preview grid the user just confirmed (the
            // preview is built with the same option via ImportDataPlanner.BuildSplitOptions).
            imported = new DelimitedTextFileAdapter(
                ".csv", "Text", delimiter, allowSeparatorDirective, options.TreatConsecutiveDelimitersAsOne).Load(stream);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or ArgumentException)
        {
            error = UiText.Format("GetData_ImportFailed", ex.Message);
            return false;
        }

        if (imported.Sheets.Count == 0 || imported.Sheets[0].GetUsedRange() is null)
        {
            error = UiText.Get("GetData_EmptyFile");
            return false;
        }

        var sourceSheet = imported.Sheets[0];
        var resolvedDestination = options.Destination;

        // R88-io-text-import-wizard-5-1: a refresh re-targets the exact anchor the original import wrote
        // to (same sheet, same cell) rather than resolving a destination fresh from the current selection
        // or adding another new sheet. Only fall through to the fresh-resolve path below when there is no
        // remembered anchor (first import) or its sheet has since been deleted.
        CellAddress destination;
        // R134-io-getdata-refresh-shrink-1: the extent (row/col count) the PREVIOUS import wrote to this
        // exact anchor, if this is a refresh reusing it. Handed to WorkbookImportWorkflow below so its
        // shared command can clear leftover cells when the refreshed source has lost rows/columns -- otherwise
        // those cells keep the prior, larger import's stale values and read as if they were still part
        // of the current import. Only set when the remembered source's anchor still matches the one
        // being reused; a stale _lastImportSource pointing elsewhere must never drive clearing here.
        (uint RowCount, uint ColCount)? previousExtent = null;
        if (anchorOverride is { } anchor && _session.Workbook.GetSheet(anchor.Sheet) is not null)
        {
            // The anchor's sheet still exists (SelectSheet's bool return only reports whether the active
            // sheet/selection changed, not whether the sheet exists, so existence is checked directly).
            _session.SelectSheet(anchor.Sheet);
            destination = anchor;
            if (_lastImportSource is { } previousSource && previousSource.Anchor == anchor)
                previousExtent = (previousSource.LastRowCount, previousSource.LastColCount);
        }
        else
        {
            if (resolvedDestination == ImportDestinationKind.NewSheet)
            {
                var addResult = _session.AddSheet();
                if (!addResult.Success)
                {
                    error = addResult.ErrorMessage ?? UiText.Get("GetData_ImportFailed");
                    return false;
                }
            }

            var targetSheetId = _session.ActiveSheet.Id;
            destination = resolvedDestination == ImportDestinationKind.NewSheet
                ? new CellAddress(targetSheetId, 1, 1)
                : _session.SelectedRange.Start;
        }

        var importResult = WorkbookImportWorkflow.ApplyImportedWorkbookEdit(
            imported,
            destination.Sheet,
            destination,
            command => _session.ExecuteReviewCommand(command),
            previousExtent);
        if (!importResult.Succeeded)
        {
            error = importResult.CellEditResult?.ErrorMessage ?? UiText.Get("GetData_ImportFailed");
            return false;
        }

        _session.SelectCell(destination);
        var used = sourceSheet.GetUsedRange()!.Value;
        _lastImportSource = new ImportDataSource(
            filePath, options, resolvedDestination, destination, used.RowCount, used.ColCount);

        RefreshShell(UiText.Format(
            "GetData_ImportedStatus",
            System.IO.Path.GetFileName(filePath),
            used.RowCount,
            used.ColCount));
        return true;
    }

    /// <summary>
    /// Data ▸ Refresh All. Re-imports the most recently remembered file source in place (cheap, no prompt)
    /// when one exists; otherwise reports that there is nothing to refresh. There is no external DB/web
    /// connection engine, so file re-import is the entire refresh surface.
    /// </summary>
    private void RefreshImportedData()
    {
        if (_isOpening || _isSaving || !TryCommitPendingFormulaEdit())
            return;

        if (_lastImportSource is not { CanRefresh: true } source || !File.Exists(source.FilePath))
        {
            RefreshShell(UiText.Get("GetData_RefreshNoSource"));
            return;
        }

        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(source.FilePath);
        }
        catch (IOException ex)
        {
            ShowEditIssue(UiText.Format("GetData_ReadError", ex.Message));
            return;
        }

        var decoded = ImportDataPlanner.DecodeBytes(bytes, source.Options.Encoding);
        if (!TryImportFromText(source.FilePath, decoded, source.Options, out var error, source.Anchor))
        {
            ShowEditIssue(error ?? UiText.Get("GetData_ImportFailed"));
            return;
        }

        RefreshShell(UiText.Format("GetData_RefreshedStatus", System.IO.Path.GetFileName(source.FilePath)));
    }

    /// <summary>Maps a resolved delimiter character to a readable label for the preview summary.</summary>
    private static string DescribeDelimiter(char delimiter) => delimiter switch
    {
        '\t' => UiText.Get("GetData_DelimiterTabGlyph"),
        ' ' => UiText.Get("GetData_DelimiterSpaceGlyph"),
        _ => delimiter.ToString(),
    };

    /// <summary>Builds the read-only preview table: a header row of column numbers and one row per sample.</summary>
    private static Control BuildGetDataPreviewGrid(ImportDataPreview preview)
    {
        var grid = new AvaloniaGrid { Margin = new Thickness(1) };
        var columnCount = Math.Max(1, preview.ColumnCount);
        for (var c = 0; c < columnCount; c++)
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        for (var r = 0; r < preview.SampleRows.Count; r++)
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        for (var c = 0; c < columnCount; c++)
        {
            var header = MakeGetDataPreviewCell((c + 1).ToString(), isHeader: true);
            AvaloniaGrid.SetRow(header, 0);
            AvaloniaGrid.SetColumn(header, c);
            grid.Children.Add(header);
        }

        for (var r = 0; r < preview.SampleRows.Count; r++)
        {
            var fields = preview.SampleRows[r];
            for (var c = 0; c < columnCount; c++)
            {
                var text = c < fields.Count ? fields[c] : string.Empty;
                var cell = MakeGetDataPreviewCell(text, isHeader: false);
                AvaloniaGrid.SetRow(cell, r + 1);
                AvaloniaGrid.SetColumn(cell, c);
                grid.Children.Add(cell);
            }
        }

        return grid;
    }

    private static Border MakeGetDataPreviewCell(string text, bool isHeader) => new()
    {
        BorderBrush = Brushes.Gainsboro,
        BorderThickness = new Thickness(0.5),
        Padding = new Thickness(6, 2, 6, 2),
        Background = isHeader ? Brushes.WhiteSmoke : Brushes.Transparent,
        Child = new TextBlock
        {
            Text = text,
            FontWeight = isHeader ? FontWeight.SemiBold : FontWeight.Normal,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 200,
        },
    };
}
