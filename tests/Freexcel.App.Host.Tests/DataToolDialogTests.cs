using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using FluentAssertions;
using Freexcel.Core.Commands;
using Freexcel.Core.Model;

namespace Freexcel.App.Host.Tests;

public sealed class DataToolDialogTests
{
    [Theory]
    [InlineData(TextToColumnsDelimiterKind.Comma, null, ",")]
    [InlineData(TextToColumnsDelimiterKind.Semicolon, null, ";")]
    [InlineData(TextToColumnsDelimiterKind.Tab, null, "\t")]
    [InlineData(TextToColumnsDelimiterKind.Space, null, " ")]
    [InlineData(TextToColumnsDelimiterKind.Custom, "|", "|")]
    public void TextToColumnsResult_MapsDelimiterChoiceToDelimiterString(
        TextToColumnsDelimiterKind kind,
        string? customDelimiter,
        string expectedDelimiter)
    {
        var result = TextToColumnsDialog.CreateResult(kind, customDelimiter);

        result.Delimiter.Should().Be(expectedDelimiter);
    }

    [Fact]
    public void TextToColumnsResult_CombinesCheckedDelimiters()
    {
        var result = TextToColumnsDialog.CreateResult(
            [TextToColumnsDelimiterKind.Tab, TextToColumnsDelimiterKind.Comma, TextToColumnsDelimiterKind.Custom],
            "|");

        result.Delimiters.Should().Be("\t,|");
        result.DelimiterKind.Should().Be(TextToColumnsDelimiterKind.Custom);
    }

    [Fact]
    public void TextToColumnsPreview_UsesSelectedTextRows()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var range = new GridRange(new CellAddress(sheet.Id, 2, 1), new CellAddress(sheet.Id, 5, 1));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("East,42,Open"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("West;7;Closed"));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 1), new TextValue(""));

        TextToColumnsDialog.BuildPreviewRows(sheet, range).Should().Equal("East,42,Open", "West;7;Closed");
    }

    [Fact]
    public void TextToColumnsDialog_AllowsOnlySingleColumnSelections()
    {
        var sheetId = SheetId.New();

        TextToColumnsDialog.CanConvertRange(new GridRange(
                new CellAddress(sheetId, 2, 1),
                new CellAddress(sheetId, 8, 1)))
            .Should()
            .BeTrue();

        TextToColumnsDialog.CanConvertRange(new GridRange(
                new CellAddress(sheetId, 2, 1),
                new CellAddress(sheetId, 8, 2)))
            .Should()
            .BeFalse();
    }

    [Fact]
    public void TextToColumnsDialog_ExposesDelimitedAndFixedWidthSplitChoices()
    {
        var source = ReadTextToColumnsDialogSources();

        source.Should().Contain("Original data type");
        source.Should().Contain("Content = \"_Delimited\"");
        source.Should().Contain("Content = \"Fi_xed width\"");
        source.Should().Contain("CreateFixedWidthResult");
        source.Should().Contain("ParseFixedWidthBreakPositions");
        source.Should().Contain("Choose the delimiters that separate your selected text.");
        source.Should().Contain("Header = \"Delimiters\"");
        source.Should().Contain("Header = \"Fixed width\"");
        source.Should().Contain("_fixedWidthRuler");
        source.Should().Contain("MouseLeftButtonDown");
        source.Should().Contain("MouseMove");
        source.Should().Contain("MouseRightButtonDown");
        source.Should().Contain("Click the ruler to create a break line");
        source.Should().Contain("Text _qualifier:");
        source.Should().Contain("_Treat consecutive delimiters as one");
        source.Should().Contain("_Destination:");
        source.Should().Contain("Column data format");
        source.Should().Contain("Content = \"_General\"");
        source.Should().Contain("Content = \"_Text\"");
        source.Should().Contain("Content = \"_Date:\"");
        source.Should().Contain("_dateFormatBox");
        source.Should().Contain("Do not import column (_skip)");
        source.Should().Contain("Header = \"Advanced\"");
        source.Should().Contain("_Decimal separator:");
        source.Should().Contain("_Thousands separator:");
        source.Should().Contain("_Trailing minus for negative numbers");
    }

    [Fact]
    public void TextToColumnsDialog_ExposesDelimiterPreviewAffordances()
    {
        var source = ReadTextToColumnsDialogSources();

        foreach (var content in new[]
        {
            "_Tab",
            "_Semicolon",
            "_Comma",
            "S_pace",
            "_Other:",
            "Data preview"
        })
            source.Should().Contain(content);

        source.Should().Contain("_previewGrid");
        source.Should().Contain("RefreshPreview");
        source.Should().Contain("TextToColumnsPlanner.SplitText");
        source.Should().Contain("_textQualifierBox");
        source.Should().Contain("SelectedTextQualifier");
        source.Should().Contain("TreatConsecutiveDelimitersAsOne");
        source.Should().Contain("_destinationBox");
        source.Should().Contain("_formatColumnBox");
        source.Should().Contain("BuildColumnFormats");
        source.Should().Contain("DialogReferencePicker.CreateEditor");
        source.Should().Contain("TextToColumnsRangeSelectionRequest");
        source.Should().Contain("_requestRangeSelection?.Invoke(RangeSelectionRequest)");
    }

    [Fact]
    public void TextToColumnsRangeSelectionRequest_TrimsCurrentTextAndCollapsesDialog()
    {
        TextToColumnsDialog.CreateRangeSelectionRequest(" F2 ")
            .Should()
            .Be(new TextToColumnsRangeSelectionRequest("F2", CollapseDialog: true));
    }

    [Fact]
    public void TextToColumnsDestinationPicker_RaisesRangeSelectionRequest()
    {
        StaTestRunner.Run(() =>
        {
            var sheetId = SheetId.New();
            var requests = new List<TextToColumnsRangeSelectionRequest>();
            var dialog = new TextToColumnsDialog(
                ["East,42"],
                new CellAddress(sheetId, 2, 6),
                requests.Add);
            dialog.Show();
            try
            {
                var picker = FindVisualChildren<Button>(dialog)
                    .Single(button => AutomationProperties.GetName(button) == "Select destination cell");

                picker.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

                requests.Should().Equal(new TextToColumnsRangeSelectionRequest("F2", CollapseDialog: true));
                dialog.RangeSelectionRequest.Should().Be(requests[0]);
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void TextToColumnsDialog_ExposesAllExcelDateColumnFormats()
    {
        var dialogSource = ReadTextToColumnsDialogSources();
        var modelSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "TextToColumnsDialogModel.cs"));

        foreach (var dateOrder in new[] { "MDY", "DMY", "YMD", "MYD", "DYM", "YDM" })
        {
            dialogSource.Should().Contain($"\"{dateOrder}\"");
            modelSource.Should().Contain($"Date{dateOrder}");
        }
    }

    [Fact]
    public void TextToColumnsDialog_UsesExcelWizardChromeAroundDelimitedFlow()
    {
        var source = ReadTextToColumnsDialogSources();

        source.Should().Contain("Step {_wizardStep} of 3");
        source.Should().Contain("CreateWizardButtonRow");
        source.Should().Contain("Content = \"< _Back\"");
        source.Should().Contain("Content = \"_Next >\"");
        source.Should().Contain("Content = \"_Finish\"");
        source.Should().Contain("MoveWizardStep");
        source.Should().Contain("UpdateWizardStep");
        source.Should().Contain("_backButton.IsEnabled = _wizardStep > 1");
        source.Should().Contain("_nextButton.IsEnabled = _wizardStep < 3");
        source.Should().Contain("Choose the file type that best describes your data.");
        source.Should().Contain("IsDefault = true");
        source.Should().Contain("Accept()");
        source.Should().NotContain("Additional wizard steps are not supported yet.");
        source.Should().NotContain("This dialog opens on the split-options step.");
    }

    [Fact]
    public void TextToColumnsDialogOpenedFromKeyboard_FocusesOriginalDataTypeChoice()
    {
        var source = ReadTextToColumnsDialogSources();

        source.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        source.Should().Contain("private void FocusInitialKeyboardTarget()");
        source.Should().Contain("_delimitedButton.Focus();");
        source.Should().Contain("Keyboard.Focus(_delimitedButton);");
    }

    [Fact]
    public void TextToColumnsResult_ParsesFixedWidthBreakPositions()
    {
        TextToColumnsDialog.ParseFixedWidthBreakPositions("12, 4; 8 4")
            .Should()
            .Equal(4, 8, 12);

        var result = TextToColumnsDialog.CreateFixedWidthResult("4,8");
        result.SplitMode.Should().Be(TextToColumnsSplitMode.FixedWidth);
        result.FixedWidthBreakPositions.Should().Equal(4, 8);
    }

    [Fact]
    public void TextToColumnsFixedWidthBreakHelpers_AddMoveAndRemoveBreaks()
    {
        TextToColumnsDialog.AddFixedWidthBreakPosition([8, 4], 12, maxLength: 20)
            .Should()
            .Equal(4, 8, 12);
        TextToColumnsDialog.AddFixedWidthBreakPosition([4, 8], 99, maxLength: 20)
            .Should()
            .Equal(4, 8, 19);

        TextToColumnsDialog.MoveFixedWidthBreakPosition([4, 8, 12], index: 1, position: 10, maxLength: 20)
            .Should()
            .Equal(4, 10, 12);

        TextToColumnsDialog.RemoveFixedWidthBreakPosition([4, 8, 12], index: 1)
            .Should()
            .Equal(4, 12);
    }

    [Fact]
    public void TextToColumnsResult_CapturesTextQualifierAndConsecutiveDelimiterChoice()
    {
        var result = TextToColumnsDialog.CreateResult(
            [TextToColumnsDelimiterKind.Comma],
            textQualifier: TextToColumnsTextQualifier.SingleQuote,
            treatConsecutiveDelimitersAsOne: true);

        result.Delimiters.Should().Be(",");
        result.TextQualifier.Should().Be(TextToColumnsTextQualifier.SingleQuote);
        result.TextQualifierChar.Should().Be('\'');
        result.TreatConsecutiveDelimitersAsOne.Should().BeTrue();
    }

    [Fact]
    public void TextToColumnsResult_NormalizesTrailingGeneralColumnFormats()
    {
        TextToColumnsDialog.NormalizeColumnFormats(
            [
                TextToColumnsColumnFormat.Text,
                TextToColumnsColumnFormat.DateMDY,
                TextToColumnsColumnFormat.General,
                TextToColumnsColumnFormat.General
            ])
            .Should()
            .Equal(TextToColumnsColumnFormat.Text, TextToColumnsColumnFormat.DateMDY);

        var result = TextToColumnsDialog.CreateResult(
            [TextToColumnsDelimiterKind.Comma],
            columnFormats:
            [
                TextToColumnsColumnFormat.General,
                TextToColumnsColumnFormat.Skip
            ]);

        result.ColumnFormats.Should().Equal(
            TextToColumnsColumnFormat.General,
            TextToColumnsColumnFormat.Skip);
    }

    [Fact]
    public void TextToColumnsResult_ParsesDestinationCellOrDefaultsToSelectionStart()
    {
        var sheetId = SheetId.New();
        var defaultDestination = new CellAddress(sheetId, 2, 1);

        TextToColumnsDialog.TryParseDestination("", defaultDestination, out var blankDestination).Should().BeTrue();
        blankDestination.Should().Be(defaultDestination);

        TextToColumnsDialog.TryParseDestination(" F2 ", defaultDestination, out var parsedDestination).Should().BeTrue();
        parsedDestination.Should().Be(new CellAddress(sheetId, 2, 6));

        TextToColumnsDialog.TryParseDestination("F2:G3", defaultDestination, out _).Should().BeFalse();
    }

    [Fact]
    public void TextToColumnsCommand_WarnsBeforeOverwritingDestinationData()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.DataCommands.cs"));

        source.Should().Contain("FindOverwriteTargets");
        source.Should().Contain("There's already data here. Do you want to replace it?");
        source.Should().Contain("MessageBoxButton.YesNo");
        source.Should().Contain("MessageBoxImage.Warning");
        source.Should().Contain("BuildTextToColumnsEdits");
    }

    [Fact]
    public void RemoveDuplicatesDialog_BuildsColumnOffsetSelectionAndBulkToggleStates()
    {
        var columns = RemoveDuplicatesDialog.SelectAll(4);
        columns.Should().AllSatisfy(column => column.IsSelected.Should().BeTrue());

        var cleared = RemoveDuplicatesDialog.ClearAll(columns);
        cleared.Should().AllSatisfy(column => column.IsSelected.Should().BeFalse());

        var selected = RemoveDuplicatesDialog.CreateResult(
            [
                new RemoveDuplicateColumnChoice(0, "Region", true),
                new RemoveDuplicateColumnChoice(1, "Sales", false),
                new RemoveDuplicateColumnChoice(2, "Rep", true)
            ]);

        selected.SelectedColumnOffsets.Should().Equal(0u, 2u);
    }

    [Fact]
    public void RemoveDuplicatesDialog_BuildsHeaderAwareColumnChoices()
    {
        var sheetId = SheetId.New();
        var sheet = new Sheet(sheetId, "Data");
        sheet.SetCell(new CellAddress(sheetId, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheetId, 1, 2), new TextValue("Sales"));

        var range = new GridRange(
            new CellAddress(sheetId, 1, 1),
            new CellAddress(sheetId, 8, 3));

        RemoveDuplicatesDialog.BuildColumnChoices(sheet, range).Should().Equal(
            new RemoveDuplicateColumnChoice(0, "Region", true),
            new RemoveDuplicateColumnChoice(1, "Sales", true),
            new RemoveDuplicateColumnChoice(2, "Column C", true));
    }

    [Fact]
    public void RemoveDuplicatesDialog_BuildsGenericColumnChoicesWhenHeadersAreDisabled()
    {
        var sheetId = SheetId.New();
        var sheet = new Sheet(sheetId, "Data");
        sheet.SetCell(new CellAddress(sheetId, 1, 2), new TextValue("Region"));
        var range = new GridRange(
            new CellAddress(sheetId, 1, 2),
            new CellAddress(sheetId, 8, 4));

        RemoveDuplicatesDialog.BuildColumnChoices(sheet, range, hasHeaders: false).Should().Equal(
            new RemoveDuplicateColumnChoice(0, "Column B", true),
            new RemoveDuplicateColumnChoice(1, "Column C", true),
            new RemoveDuplicateColumnChoice(2, "Column D", true));
    }

    [Fact]
    public void RemoveDuplicatesDialog_GuessesHeadersFromFirstRowShape()
    {
        var sheetId = SheetId.New();
        var sheet = new Sheet(sheetId, "Data");
        sheet.SetCell(new CellAddress(sheetId, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheetId, 1, 2), new TextValue("Sales"));
        sheet.SetCell(new CellAddress(sheetId, 2, 1), new TextValue("East"));
        sheet.SetCell(new CellAddress(sheetId, 2, 2), new NumberValue(42));
        var range = new GridRange(
            new CellAddress(sheetId, 1, 1),
            new CellAddress(sheetId, 4, 2));

        RemoveDuplicatesDialog.GuessHasHeaders(sheet, range).Should().BeTrue();

        var numericSheet = new Sheet(sheetId, "Numbers");
        numericSheet.SetCell(new CellAddress(sheetId, 1, 1), new NumberValue(10));
        numericSheet.SetCell(new CellAddress(sheetId, 1, 2), new NumberValue(20));
        numericSheet.SetCell(new CellAddress(sheetId, 2, 1), new NumberValue(10));
        numericSheet.SetCell(new CellAddress(sheetId, 2, 2), new NumberValue(30));

        RemoveDuplicatesDialog.GuessHasHeaders(numericSheet, range).Should().BeFalse();
    }

    [Fact]
    public void RemoveDuplicatesDialog_ExcludesHeaderRowOnlyWhenHeadersAreEnabled()
    {
        var sheetId = SheetId.New();
        var range = new GridRange(
            new CellAddress(sheetId, 1, 1),
            new CellAddress(sheetId, 8, 3));

        RemoveDuplicatesDialog.ExcludeHeaderRow(range, hasHeaders: true).Should().Be(new GridRange(
            new CellAddress(sheetId, 2, 1),
            new CellAddress(sheetId, 8, 3)));
        RemoveDuplicatesDialog.ExcludeHeaderRow(range, hasHeaders: false).Should().Be(range);
        RemoveDuplicatesDialog.ExcludeHeaderRow(new GridRange(range.Start, range.Start), hasHeaders: true)
            .Should()
            .Be(new GridRange(range.Start, range.Start));
    }

    [Fact]
    public void RemoveDuplicatesDialog_ResultCapturesHeaderFlag()
    {
        var result = new RemoveDuplicatesDialogResult([0u, 2u], HasHeaders: true);

        result.SelectedColumnOffsets.Should().Equal(0u, 2u);
        result.HasHeaders.Should().BeTrue();
    }

    [Fact]
    public void SubtotalDialog_CreatesOptionsUsingSubtotalFunctionServiceNames()
    {
        var result = SubtotalDialog.CreateResult(
            groupColumnOffset: 0,
            subtotalColumnOffsets: [1u, 3u],
            functionText: "average",
            replaceCurrentSubtotals: true,
            pageBreakBetweenGroups: true,
            summaryBelowData: false);

        result.GroupColumnOffset.Should().Be(0);
        result.SubtotalColumnOffsets.Should().Equal(1u, 3u);
        result.FunctionNumber.Should().Be(1);
        result.ReplaceCurrentSubtotals.Should().BeTrue();
        result.PageBreakBetweenGroups.Should().BeTrue();
        result.SummaryBelowData.Should().BeFalse();
        result.Action.Should().Be(SubtotalDialogAction.Apply);
    }

    [Fact]
    public void SubtotalDialog_CreatesRemoveAllResultWithoutSubtotalColumns()
    {
        var result = SubtotalDialog.CreateRemoveAllResult();

        result.Action.Should().Be(SubtotalDialogAction.RemoveAll);
        result.SubtotalColumnOffsets.Should().BeEmpty();
        result.ReplaceCurrentSubtotals.Should().BeFalse();
        result.PageBreakBetweenGroups.Should().BeFalse();
        result.SummaryBelowData.Should().BeTrue();
    }

    [Fact]
    public void SubtotalDialog_BuildsHeaderAwareColumnChoices()
    {
        var sheetId = SheetId.New();
        var sheet = new Sheet(sheetId, "Data");
        sheet.SetCell(new CellAddress(sheetId, 1, 2), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheetId, 1, 3), new TextValue("Sales"));

        var range = new GridRange(
            new CellAddress(sheetId, 1, 2),
            new CellAddress(sheetId, 8, 4));

        SubtotalDialog.BuildColumnChoices(sheet, range).Should().Equal(
            new SubtotalColumnChoice(0, "Region", false),
            new SubtotalColumnChoice(1, "Sales", true),
            new SubtotalColumnChoice(2, "Column D", true));
    }

    [Fact]
    public void SubtotalDialog_ExposesKeyboardAccessKeysForStaticOptions()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "SubtotalDialog.cs"));

        foreach (var content in new[]
        {
            "_Replace current subtotals",
            "_Page break between groups",
            "_Summary below data",
            "_At each change in:",
            "_Add subtotal to:",
            "_Use function:",
            "_Remove All"
        })
            source.Should().Contain($"Content = \"{content}\"");
    }

    [Fact]
    public void SubtotalDialog_ExposesExcelStyleFunctionDropdownAndSubtotalChecklist()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "SubtotalDialog.cs"));

        source.Should().Contain("_functionBox = new ComboBox");
        source.Should().Contain("SubtotalFunctionChoices");
        source.Should().Contain("ItemsSource = SubtotalFunctionChoices");
        source.Should().Contain("SelectedItem = \"Sum\"");
        source.Should().Contain("new GroupBox { Header = \"Add subtotal to:\"");
        source.Should().Contain("_subtotalColumnPanel");
    }

    [Fact]
    public void SubtotalDialogOpenedFromKeyboard_FocusesGroupColumnChoice()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "SubtotalDialog.cs"));

        source.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        source.Should().Contain("private void FocusInitialKeyboardTarget()");
        source.Should().Contain("_groupColumnBox.Focus();");
        source.Should().Contain("Keyboard.Focus(_groupColumnBox);");
    }

    [Fact]
    public void SubtotalDialog_OrdersControlsLikeExcelSubtotalDialog()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "SubtotalDialog.cs"));

        source.IndexOf("Content = \"_At each change in:\"", StringComparison.Ordinal).Should()
            .BeLessThan(source.IndexOf("Content = \"_Use function:\"", StringComparison.Ordinal));
        source.IndexOf("Content = \"_Use function:\"", StringComparison.Ordinal).Should()
            .BeLessThan(source.IndexOf("Header = \"Add subtotal to:\"", StringComparison.Ordinal));
        source.IndexOf("Header = \"Add subtotal to:\"", StringComparison.Ordinal).Should()
            .BeLessThan(source.IndexOf("Content = \"_Replace current subtotals\"", StringComparison.Ordinal));
        source.Should().Contain("CreateSubtotalButtonRow");
    }

    [Fact]
    public void SubtotalCommandSurface_RoutesRemoveAllToRemoveSubtotalRowsCommand()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.DataCommands.cs"));

        source.Should().Contain("SubtotalDialogAction.RemoveAll");
        source.Should().Contain("new RemoveSubtotalRowsCommand(_currentSheetId, currentRange)");
    }

    [Fact]
    public void AdvancedFilterDialog_ParsesRangesAndOptionalCopyToCellOnCurrentSheet()
    {
        var sheetId = SheetId.New();

        var parsed = AdvancedFilterDialog.TryParse(
            sheetId,
            listRangeText: "A1:D20",
            criteriaRangeText: "F1:G2",
            copyToCellText: "J1",
            uniqueRecordsOnly: true,
            out var result,
            out var error);

        parsed.Should().BeTrue(error);
        result.ListRange.Should().Be(new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 20, 4)));
        result.CriteriaRange.Should().Be(new GridRange(new CellAddress(sheetId, 1, 6), new CellAddress(sheetId, 2, 7)));
        result.CopyToCell.Should().Be(new CellAddress(sheetId, 1, 10));
        result.CopyToRange.Should().Be(new GridRange(new CellAddress(sheetId, 1, 10), new CellAddress(sheetId, 1, 10)));
        result.UniqueRecordsOnly.Should().BeTrue();
    }

    [Fact]
    public void TextToColumnsResult_CapturesAdvancedNumberOptions()
    {
        var advanced = new TextToColumnsAdvancedOptions(",", ".", TrailingMinusNumbers: true);

        var result = TextToColumnsDialog.CreateResult(
            [TextToColumnsDelimiterKind.Semicolon],
            advancedOptions: advanced);

        result.AdvancedOptions.Should().Be(advanced);
    }

    [Fact]
    public void AdvancedFilterDialog_ParsesCopyToHeaderRange()
    {
        var sheetId = SheetId.New();

        var parsed = AdvancedFilterDialog.TryParse(
            sheetId,
            listRangeText: "A1:D20",
            criteriaRangeText: "F1:G2",
            copyToCellText: "J1:L1",
            uniqueRecordsOnly: true,
            out var result,
            out var error);

        parsed.Should().BeTrue(error);
        result.CopyToCell.Should().Be(new CellAddress(sheetId, 1, 10));
        result.CopyToRange.Should().Be(new GridRange(new CellAddress(sheetId, 1, 10), new CellAddress(sheetId, 1, 12)));
    }

    [Fact]
    public void AdvancedFilterDialog_AcceptsSingleCellRangesOnCurrentSheet()
    {
        var sheetId = SheetId.New();

        var parsed = AdvancedFilterDialog.TryParse(
            sheetId,
            listRangeText: "A1",
            criteriaRangeText: "C3",
            copyToCellText: "",
            uniqueRecordsOnly: false,
            out var result,
            out var error);

        parsed.Should().BeTrue(error);
        result.ListRange.Should().Be(new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 1, 1)));
        result.CriteriaRange.Should().Be(new GridRange(new CellAddress(sheetId, 3, 3), new CellAddress(sheetId, 3, 3)));
        result.CopyToCell.Should().BeNull();
        result.UniqueRecordsOnly.Should().BeFalse();
    }

    [Fact]
    public void AdvancedFilterDialog_ParsesSheetQualifiedListAndCriteriaRanges()
    {
        var currentSheetId = SheetId.New();
        var dataSheetId = SheetId.New();
        var criteriaSheetId = SheetId.New();

        var parsed = AdvancedFilterDialog.TryParse(
            currentSheetId,
            listRangeText: "Data!A1:D20",
            criteriaRangeText: "Criteria!F1:G2",
            copyToCellText: "",
            uniqueRecordsOnly: false,
            resolveSheetId: sheetName => sheetName switch
            {
                "Data" => dataSheetId,
                "Criteria" => criteriaSheetId,
                _ => null
            },
            out var result,
            out var error);

        parsed.Should().BeTrue(error);
        result.ListRange.Should().Be(new GridRange(new CellAddress(dataSheetId, 1, 1), new CellAddress(dataSheetId, 20, 4)));
        result.CriteriaRange.Should().Be(new GridRange(new CellAddress(criteriaSheetId, 1, 6), new CellAddress(criteriaSheetId, 2, 7)));
    }

    [Fact]
    public void AdvancedFilterDialog_RejectsInvalidCopyToCell()
    {
        var sheetId = SheetId.New();

        var parsed = AdvancedFilterDialog.TryParse(
            sheetId,
            listRangeText: "A1:D20",
            criteriaRangeText: "F1:G2",
            copyToCellText: "NotACell",
            uniqueRecordsOnly: false,
            out _,
            out var error);

        parsed.Should().BeFalse();
        error.Should().Be("Enter a valid copy-to cell or one-row header range.");
    }

    [Fact]
    public void AdvancedFilterDialog_InPlaceModeIgnoresCopyToText()
    {
        var sheetId = SheetId.New();

        var parsed = AdvancedFilterDialog.TryParse(
            sheetId,
            listRangeText: "A1:D20",
            criteriaRangeText: "F1:G2",
            copyToCellText: "NotACell",
            copyToAnotherLocation: false,
            uniqueRecordsOnly: false,
            out var result,
            out var error);

        parsed.Should().BeTrue(error);
        result.CopyToCell.Should().BeNull();
    }

    [Fact]
    public void AdvancedFilterDialog_ExposesExcelStyleModesAndReferencePickers()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "AdvancedFilterDialog.cs"));
        var pickerSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "DialogReferencePicker.cs"));

        source.Should().Contain("_filterInPlaceButton");
        source.Should().Contain("_copyToAnotherLocationButton");
        source.Should().Contain("Content = \"_Filter the list, in-place\"");
        source.Should().Contain("Content = \"_Copy to another location\"");
        source.Should().Contain("Content = \"_Unique records only\"");
        source.Should().Contain("AddReferenceRow(rangesGrid, 0, \"_List range:\", _listRangeBox");
        source.Should().Contain("AddReferenceRow(rangesGrid, 1, \"_Criteria range:\", _criteriaRangeBox");
        source.Should().Contain("AddReferenceRow(rangesGrid, 2, \"Copy _to:\", _copyToBox");
        source.Should().Contain("var labelBlock = new Label");
        source.Should().Contain("Target = textBox");
        source.Should().Contain("DialogReferencePicker.CreateEditor");
        source.Should().Contain("RequestRangeSelection");
        source.Should().Contain("_requestRangeSelection?.Invoke(RangeSelectionRequest)");
        pickerSource.Should().Contain("Collapse dialog and select range");
        source.Should().NotContain("Content = \"Collapse Dialog\"");
        source.Should().NotContain("Text = \"E1:F2\"");
        source.Should().Contain("Header = \"Action\"");
        source.Should().Contain("Criteria should include column labels");
        source.Should().Contain("DialogReferencePicker.CreateEditor");
    }

    [Fact]
    public void AdvancedFilterDialogOpenedFromKeyboard_FocusesInPlaceAction()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "AdvancedFilterDialog.cs"));

        source.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        source.Should().Contain("private void FocusInitialKeyboardTarget()");
        source.Should().Contain("_filterInPlaceButton.Focus();");
        source.Should().Contain("Keyboard.Focus(_filterInPlaceButton);");
    }

    [Fact]
    public void AdvancedFilterRangeSelectionRequest_TrimsCurrentTextAndCollapsesDialog()
    {
        AdvancedFilterDialog.CreateRangeSelectionRequest(AdvancedFilterRangeSelectionTarget.CriteriaRange, " E1:F4 ")
            .Should()
            .Be(new AdvancedFilterRangeSelectionRequest(
                AdvancedFilterRangeSelectionTarget.CriteriaRange,
                "E1:F4",
                CollapseDialog: true));
    }

    [Theory]
    [InlineData("Select list range", AdvancedFilterRangeSelectionTarget.ListRange, "A1:C12")]
    [InlineData("Select criteria range", AdvancedFilterRangeSelectionTarget.CriteriaRange, "E1:F4")]
    [InlineData("Select copy-to cell", AdvancedFilterRangeSelectionTarget.CopyTo, "H1:J1")]
    public void AdvancedFilterReferencePickers_RaiseRangeSelectionRequest(
        string automationName,
        AdvancedFilterRangeSelectionTarget expectedTarget,
        string expectedText)
    {
        StaTestRunner.Run(() =>
        {
            var requests = new List<AdvancedFilterRangeSelectionRequest>();
            var dialog = new AdvancedFilterDialog(SheetId.New(), " A1:C12 ", requestRangeSelection: requests.Add);
            dialog.Show();
            try
            {
                var textBoxes = FindVisualChildren<TextBox>(dialog).ToList();
                textBoxes[1].Text = " E1:F4 ";
                textBoxes[2].Text = " H1:J1 ";
                var picker = FindVisualChildren<Button>(dialog)
                    .Single(button => AutomationProperties.GetName(button) == automationName);

                picker.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

                requests.Should().Equal(new AdvancedFilterRangeSelectionRequest(
                    expectedTarget,
                    expectedText,
                    CollapseDialog: true));
                dialog.RangeSelectionRequest.Should().Be(requests[0]);
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void ConsolidateDialog_ValidatesSameSizeSourceRanges()
    {
        var sheetId = SheetId.New();
        var first = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 2));
        var second = new GridRange(new CellAddress(sheetId, 5, 4), new CellAddress(sheetId, 7, 5));
        var different = new GridRange(new CellAddress(sheetId, 10, 1), new CellAddress(sheetId, 12, 3));

        ConsolidateDialog.HaveSameSize([first, second]).Should().BeTrue();
        ConsolidateDialog.HaveSameSize([first, different]).Should().BeFalse();

        var result = ConsolidateDialog.CreateResult(
            [first, second],
            new CellAddress(sheetId, 9, 1),
            ConsolidateFunction.Sum);
        result.SourceRanges.Should().Equal(first, second);
        result.DestinationCell.Should().Be(new CellAddress(sheetId, 9, 1));
        result.Function.Should().Be(ConsolidateFunction.Sum);
    }

    [Fact]
    public void ConsolidateDialog_TryParse_DelegatesSourceAndDestinationParsing()
    {
        var sheetId = SheetId.New();

        var parsed = ConsolidateDialog.TryParse(
            sheetId,
            sourceRangesText: "A1:B3; D5:E7",
            destinationCellText: "G10",
            out var result,
            out var error);

        parsed.Should().BeTrue(error);
        result.SourceRanges.Should().Equal(
            new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 2)),
            new GridRange(new CellAddress(sheetId, 5, 4), new CellAddress(sheetId, 7, 5)));
        result.DestinationCell.Should().Be(new CellAddress(sheetId, 10, 7));
        result.Function.Should().Be(ConsolidateFunction.Sum);
    }

    [Fact]
    public void ConsolidateDialog_TryParse_CapturesSelectedFunctionAndOptions()
    {
        var sheetId = SheetId.New();

        var parsed = ConsolidateDialog.TryParse(
            sheetId,
            sourceRangesText: "A1:B3; D5:E7",
            destinationCellText: "G10",
            function: ConsolidateFunction.Average,
            useTopRowLabels: true,
            useLeftColumnLabels: true,
            createLinksToSourceData: true,
            out var result,
            out var error);

        parsed.Should().BeTrue(error);
        result.Function.Should().Be(ConsolidateFunction.Average);
        result.UseTopRowLabels.Should().BeTrue();
        result.UseLeftColumnLabels.Should().BeTrue();
        result.CreateLinksToSourceData.Should().BeTrue();
    }

    [Fact]
    public void ConsolidateDialog_JoinsAllReferencesListForExistingParser()
    {
        ConsolidateDialog.SplitSourceRangeText("A1:B3; D5:E7").Should().Equal("A1:B3", "D5:E7");
        ConsolidateDialog.JoinSourceRanges(["A1:B3", "D5:E7"]).Should().Be("A1:B3; D5:E7");
    }

    [Fact]
    public void ConsolidateDialog_TryAddReference_RejectsMalformedReferenceImmediately()
    {
        var sheetId = SheetId.New();

        ConsolidateDialog.TryAddReference(
                sheetId,
                ["A1:B3"],
                "nope",
                out var unchanged,
                out var error)
            .Should()
            .BeFalse();

        unchanged.Should().Equal("A1:B3");
        error.Should().Be("Enter a valid source range: nope.");

        ConsolidateDialog.TryAddReference(
                sheetId,
                ["A1:B3"],
                "D5:E7",
                out var updated,
                out error)
            .Should()
            .BeTrue();

        updated.Should().Equal("A1:B3", "D5:E7");
        error.Should().BeNull();
    }

    [Fact]
    public void ConsolidateDialog_ExposesExcelStyleAllReferencesWorkflow()
    {
        var source = ReadConsolidateDialogSources();

        source.Should().Contain("_referenceBox");
        source.Should().Contain("_referencesList");
        source.Should().Contain("_Reference:");
        source.Should().Contain("_All references:");
        source.Should().Contain("_Destination cell:");
        source.Should().Contain("Use _labels in:");
        source.Should().Contain("Content = \"_Add\"");
        source.Should().Contain("Content = \"_Delete\"");
        source.Should().Contain("_deleteReferenceButton");
        source.Should().Contain("UpdateReferenceButtons");
        source.Should().Contain("_referencesList.SelectionChanged");
        source.Should().Contain("AddReferenceButton_Click");
        source.Should().Contain("DeleteReferenceButton_Click");
        source.Should().Contain("CreateReferenceEditor(_referenceBox");
        source.Should().Contain("RequestRangeSelection");
        source.Should().Contain("_requestRangeSelection?.Invoke(RangeSelectionRequest)");
    }

    [Fact]
    public void ConsolidateDialogOpenedFromKeyboard_FocusesFunctionChoice()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "ConsolidateDialog.cs"));

        source.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        source.Should().Contain("private void FocusInitialKeyboardTarget()");
        source.Should().Contain("_functionBox.Focus();");
        source.Should().Contain("Keyboard.Focus(_functionBox);");
    }

    [Fact]
    public void ConsolidateRangeSelectionRequest_TrimsCurrentTextAndCollapsesDialog()
    {
        ConsolidateDialog.CreateRangeSelectionRequest(ConsolidateRangeSelectionTarget.Reference, " A1:B3 ")
            .Should()
            .Be(new ConsolidateRangeSelectionRequest(
                ConsolidateRangeSelectionTarget.Reference,
                "A1:B3",
                CollapseDialog: true));
    }

    [Theory]
    [InlineData("Select reference range", ConsolidateRangeSelectionTarget.Reference, "A1:B3")]
    [InlineData("Select destination cell", ConsolidateRangeSelectionTarget.DestinationCell, "G10")]
    public void ConsolidateReferencePickers_RaiseRangeSelectionRequest(
        string automationName,
        ConsolidateRangeSelectionTarget expectedTarget,
        string expectedText)
    {
        StaTestRunner.Run(() =>
        {
            var requests = new List<ConsolidateRangeSelectionRequest>();
            var dialog = new ConsolidateDialog(SheetId.New(), " A1:B3 ", " G10 ", requests.Add);
            dialog.Show();
            try
            {
                var picker = FindVisualChildren<Button>(dialog)
                    .Single(button => AutomationProperties.GetName(button) == automationName);

                picker.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

                requests.Should().Equal(new ConsolidateRangeSelectionRequest(
                    expectedTarget,
                    expectedText,
                    CollapseDialog: true));
                dialog.RangeSelectionRequest.Should().Be(requests[0]);
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void ConsolidateDialog_ExposesExcelStyleFunctionLabelsAndLinkOptions()
    {
        var source = ReadConsolidateDialogSources();

        source.Should().Contain("_functionBox");
        source.Should().Contain("_topRowBox");
        source.Should().Contain("_leftColumnBox");
        source.Should().Contain("_createLinksBox");
        source.Should().Contain("_Function:");
        source.Should().Contain("_Top row");
        source.Should().Contain("_Left column");
        source.Should().Contain("Create _links to source data");
        source.Should().Contain("Enum.GetValues<ConsolidateFunction>()");
        source.Should().Contain("FunctionLabel(function)");
        source.Should().Contain("ConsolidateFunction.CountNumbers => \"Count Numbers\"");
        source.Should().Contain("SelectedFunction()");
        source.Should().NotContain("DisableUnsupported(_functionBox, SumOnlyHelpText)");
        source.Should().NotContain("DisableUnsupported(_topRowBox, LabelMatchingHelpText)");
        source.Should().NotContain("DisableUnsupported(_leftColumnBox, LabelMatchingHelpText)");
        source.Should().NotContain("DisableUnsupported(_createLinksBox, SourceLinksHelpText)");
        source.Should().NotContain("Source links are not available yet");
        source.Should().Contain("UseTopRowLabels");
        source.Should().Contain("UseLeftColumnLabels");
        source.Should().Contain("CreateLinksToSourceData");
        source.Should().Contain("Write formulas that reference the source cells");
    }

    private static string ReadConsolidateDialogSources() =>
        File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "ConsolidateDialog.cs")) +
        File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "ConsolidateDialog.Planning.cs"));

    [Fact]
    public void ConsolidateDialog_TryParse_RejectsMalformedSourceRange()
    {
        var sheetId = SheetId.New();

        var parsed = ConsolidateDialog.TryParse(
            sheetId,
            sourceRangesText: "A1:B3; nope",
            destinationCellText: "G10",
            out _,
            out var error);

        parsed.Should().BeFalse();
        error.Should().Be("Enter a valid source range: nope.");
    }

    [Fact]
    public void ConsolidateDialog_TryParse_RejectsMismatchedSourceSizes()
    {
        var sheetId = SheetId.New();

        var parsed = ConsolidateDialog.TryParse(
            sheetId,
            sourceRangesText: "A1:B3; D5:F7",
            destinationCellText: "G10",
            out _,
            out var error);

        parsed.Should().BeFalse();
        error.Should().Be("Source ranges must be the same size.");
    }

    [Fact]
    public void ConsolidateDialog_TryParse_RejectsInvalidDestinationCell()
    {
        var sheetId = SheetId.New();

        var parsed = ConsolidateDialog.TryParse(
            sheetId,
            sourceRangesText: "A1:B3",
            destinationCellText: "nope",
            out _,
            out var error);

        parsed.Should().BeFalse();
        error.Should().Be("Enter a valid destination cell.");
    }

    [Fact]
    public void DataTableDialog_ParsesOneAndTwoVariableInputs()
    {
        var sheetId = SheetId.New();
        var range = new GridRange(
            new CellAddress(sheetId, 2, 2),
            new CellAddress(sheetId, 8, 5));

        var oneVariableParsed = DataTableDialog.TryParse(
            sheetId,
            range,
            rowInputCellText: "",
            columnInputCellText: "C1",
            out var oneVariable,
            out var oneVariableError);

        oneVariableParsed.Should().BeTrue(oneVariableError);
        oneVariable.Mode.Should().Be(DataTableMode.OneVariable);
        oneVariable.Orientation.Should().Be(DataTableInputOrientation.Column);
        oneVariable.FormulaCell.Should().Be(new CellAddress(sheetId, 2, 3));
        oneVariable.RowInputCell.Should().BeNull();
        oneVariable.ColumnInputCell.Should().Be(new CellAddress(sheetId, 1, 3));

        var rowInputParsed = DataTableDialog.TryParse(
            sheetId,
            range,
            rowInputCellText: "A1",
            columnInputCellText: "",
            out var rowInput,
            out var rowInputError);

        rowInputParsed.Should().BeTrue(rowInputError);
        rowInput.Mode.Should().Be(DataTableMode.OneVariable);
        rowInput.Orientation.Should().Be(DataTableInputOrientation.Row);
        rowInput.FormulaCell.Should().Be(new CellAddress(sheetId, 3, 2));

        var twoVariableParsed = DataTableDialog.TryParse(
            sheetId,
            range,
            rowInputCellText: "A1",
            columnInputCellText: "C1",
            out var twoVariable,
            out var twoVariableError);

        twoVariableParsed.Should().BeTrue(twoVariableError);
        twoVariable.Mode.Should().Be(DataTableMode.TwoVariable);
        twoVariable.Orientation.Should().Be(DataTableInputOrientation.Column);
        twoVariable.FormulaCell.Should().Be(new CellAddress(sheetId, 2, 2));
        twoVariable.RowInputCell.Should().Be(new CellAddress(sheetId, 1, 1));
        twoVariable.ColumnInputCell.Should().Be(new CellAddress(sheetId, 1, 3));
    }

    [Fact]
    public void DataTableDialog_RejectsMissingInputCells()
    {
        var sheetId = SheetId.New();
        var range = new GridRange(
            new CellAddress(sheetId, 2, 2),
            new CellAddress(sheetId, 8, 5));

        var parsed = DataTableDialog.TryParse(
            sheetId,
            range,
            rowInputCellText: "",
            columnInputCellText: "",
            out _,
            out var error);

        parsed.Should().BeFalse();
        error.Should().Be("Enter either a row input cell or a column input cell.");
    }

    [Fact]
    public void DataTableDialog_RejectsInvalidOptionalInputCell()
    {
        var sheetId = SheetId.New();
        var range = new GridRange(
            new CellAddress(sheetId, 2, 2),
            new CellAddress(sheetId, 8, 5));

        var parsed = DataTableDialog.TryParse(
            sheetId,
            range,
            rowInputCellText: "",
            columnInputCellText: "not-a-cell",
            out _,
            out var error);

        parsed.Should().BeFalse();
        error.Should().Be("Enter a valid column input cell.");
    }

    [Fact]
    public void DataTableDialog_ExposesReferencePickersForCellInputs()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "DataTableDialog.cs"));

        source.Should().Contain("AddReferenceRow(grid, 0, \"_Row input cell:\", _rowInputBox");
        source.Should().Contain("AddReferenceRow(grid, 1, \"_Column input cell:\", _columnInputBox");
        source.Should().NotContain("_formulaBox");
        source.Should().NotContain("_modeBox");
        source.Should().Contain("DialogReferencePicker.CreateEditor");
        source.Should().Contain("RequestRangeSelection");
        source.Should().Contain("_requestRangeSelection?.Invoke(RangeSelectionRequest)");
        source.Should().Contain("Select row input cell");
        source.Should().Contain("Select column input cell");
        source.Should().NotContain("Content = \"Collapse Dialog\"");
        source.Should().Contain("var labelBlock = new Label");
        source.Should().Contain("Target = textBox");
        source.Should().NotContain("Substitute values in the selected data table using worksheet input cells.");
        source.Should().NotContain("Header = \"Inputs\"");
        source.Should().Contain("DataTableInputParser.GetDefaultFormulaCell");
    }

    [Fact]
    public void DataTableDialogOpenedFromKeyboard_FocusesRowInputCell()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "DataTableDialog.cs"));

        source.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        source.Should().Contain("private void FocusInitialKeyboardTarget()");
        source.Should().Contain("_rowInputBox.Focus();");
        source.Should().Contain("Keyboard.Focus(_rowInputBox);");
    }

    [Fact]
    public void DataTableDialogInvalidInput_RefocusesInvalidCellEntry()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "DataTableDialog.cs"));

        source.Should().Contain("FocusInvalidInput(error);");
        source.Should().Contain("private void FocusInvalidInput(string? error)");
        source.Should().Contain("target.Focus();");
        source.Should().Contain("target.SelectAll();");
        source.Should().Contain("Keyboard.Focus(target);");
    }

    [Fact]
    public void DataTableDialogRangeSelectionRequest_TrimsCurrentTextAndCollapsesDialog()
    {
        DataTableDialog.CreateRangeSelectionRequest(DataTableRangeSelectionTarget.ColumnInputCell, " $C$1 ")
            .Should()
            .Be(new DataTableRangeSelectionRequest(
                DataTableRangeSelectionTarget.ColumnInputCell,
                "$C$1",
                CollapseDialog: true));
    }

    [Theory]
    [InlineData("Select row input cell", DataTableRangeSelectionTarget.RowInputCell, "A1")]
    [InlineData("Select column input cell", DataTableRangeSelectionTarget.ColumnInputCell, "C1")]
    public void DataTableDialogReferencePickers_RaiseRangeSelectionRequest(
        string automationName,
        DataTableRangeSelectionTarget expectedTarget,
        string expectedText)
    {
        StaTestRunner.Run(() =>
        {
            var sheetId = SheetId.New();
            var range = new GridRange(new CellAddress(sheetId, 2, 2), new CellAddress(sheetId, 8, 5));
            var requests = new List<DataTableRangeSelectionRequest>();
            var dialog = new DataTableDialog(sheetId, range, requests.Add);
            dialog.Show();
            try
            {
                var textBoxes = FindVisualChildren<TextBox>(dialog).ToList();
                textBoxes[0].Text = " A1 ";
                textBoxes[1].Text = " C1 ";
                var picker = FindVisualChildren<Button>(dialog)
                    .Single(button => AutomationProperties.GetName(button) == automationName);

                picker.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

                requests.Should().Equal(new DataTableRangeSelectionRequest(
                    expectedTarget,
                    expectedText,
                    CollapseDialog: true));
                dialog.RangeSelectionRequest.Should().Be(requests[0]);
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void CreateTableDialog_ExposesHeadersCheckboxAndRangePicker()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "CreateTableDialog.cs"));

        source.Should().Contain("_headersBox");
        source.Should().Contain("Content = \"_My table has headers\"");
        source.Should().Contain("new Label { Content = \"_Where is the data for your table?\", Target = _rangeBox");
        source.Should().Contain("CreateReferenceEditor(_rangeBox");
        source.Should().Contain("DialogReferencePicker.CreateEditor");
        source.Should().Contain("RequestRangeSelection");
        source.Should().Contain("_requestRangeSelection?.Invoke(RangeSelectionRequest)");
        source.Should().Contain("Select table range");
    }

    [Fact]
    public void CreateTableDialogOpenedFromKeyboard_FocusesRangeBox()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "CreateTableDialog.cs"));

        source.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        source.Should().Contain("private void FocusInitialKeyboardTarget()");
        source.Should().Contain("_rangeBox.Focus();");
        source.Should().Contain("Keyboard.Focus(_rangeBox);");
    }

    [Fact]
    public void CreateTableDialogInvalidRange_RefocusesAndSelectsRangeBox()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "CreateTableDialog.cs"));

        source.Should().Contain("FocusRangeBox();");
        source.Should().Contain("private void FocusRangeBox()");
        source.Should().Contain("_rangeBox.SelectAll();");
        source.Should().Contain("Keyboard.Focus(_rangeBox);");
    }

    [Fact]
    public void CreateTableDialog_ParsesRangeHeadersAndStyle()
    {
        var sheetId = SheetId.New();

        var parsed = CreateTableDialog.TryParse(
            sheetId,
            rangeText: " A1:C12 ",
            firstRowHasHeaders: false,
            tableStyleName: "TableStyleMedium2",
            out var result,
            out var error);

        parsed.Should().BeTrue(error);
        result.Range.Should().Be(new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 12, 3)));
        result.FirstRowHasHeaders.Should().BeFalse();
        result.TableStyleName.Should().Be("TableStyleMedium2");
    }

    [Fact]
    public void CreateTableDialog_RangePickerRaisesRangeSelectionRequest()
    {
        StaTestRunner.Run(() =>
        {
            var requests = new List<CreateTableRangeSelectionRequest>();
            var dialog = new CreateTableDialog(
                SheetId.New(),
                " A1:C12 ",
                "TableStyleMedium2",
                requests.Add);
            dialog.Show();
            try
            {
                var picker = FindVisualChildren<Button>(dialog)
                    .Where(button => Equals(button.Content, "..."))
                    .Single();

                picker.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

                requests.Should().Equal(new CreateTableRangeSelectionRequest("A1:C12", CollapseDialog: true));
                dialog.RangeSelectionRequest.Should().Be(requests[0]);
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void CreateTableRangeSelectionRequest_TrimsCurrentTextAndCollapsesDialog()
    {
        CreateTableDialog.CreateRangeSelectionRequest(" A1:C12 ")
            .Should()
            .Be(new CreateTableRangeSelectionRequest("A1:C12", CollapseDialog: true));
    }

    [Fact]
    public void DialogReferencePicker_RaisesSelectionRequestAndMarksCollapseAffordance()
    {
        StaTestRunner.Run(() =>
        {
            var box = new TextBox { Text = "A1:C10" };
            DialogReferencePickerRequest? captured = null;

            var request = DialogReferencePicker.RequestSelection(
                box,
                "Select table range",
                next => captured = next);

            request.Target.Should().BeSameAs(box);
            request.AutomationName.Should().Be("Select table range");
            request.CurrentText.Should().Be("A1:C10");
            captured.Should().Be(request);

            var button = DialogReferencePicker.CreateButton(box, "Select table range");
            button.ToolTip.Should().Be("Collapse dialog and select range");
        });
    }

    [Fact]
    public void RemoveDuplicatesDialog_ExposesExcelStyleBulkHeaderAndColumnListControls()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "RemoveDuplicatesDialog.cs")) +
            File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "RemoveDuplicatesDialog.Planning.cs"));
        var mainWindowSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.DataCommands.cs"));

        source.Should().Contain("_Select All");
        source.Should().Contain("_Unselect All");
        source.Should().Contain("_My data has headers");
        source.Should().Contain("_columnsPanel");
        source.Should().Contain("Columns:");
        source.Should().Contain("SelectAllButton_Click");
        source.Should().Contain("UnselectAllButton_Click");
        source.Should().Contain("RefreshColumnLabels");
        source.Should().Contain("HasHeaders");
        mainWindowSource.Should().Contain("RemoveDuplicatesDialog.ExcludeHeaderRow(currentRange, dialog.Result.HasHeaders)");
    }

    [Fact]
    public void RemoveDuplicatesDialogOpenedFromKeyboard_FocusesHeaderChoice()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "RemoveDuplicatesDialog.cs"));

        source.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        source.Should().Contain("private void FocusInitialKeyboardTarget()");
        source.Should().Contain("_hasHeadersBox.Focus();");
        source.Should().Contain("Keyboard.Focus(_hasHeadersBox);");
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject root)
        where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
                yield return match;

            foreach (var descendant in FindVisualChildren<T>(child))
                yield return descendant;
        }
    }

    private static string ReadTextToColumnsDialogSources() =>
        string.Join(
            Environment.NewLine,
            File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "TextToColumnsDialog.cs")),
            File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "TextToColumnsDialog.FixedWidth.cs")),
            File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "TextToColumnsDialog.ColumnFormats.cs")));
}
