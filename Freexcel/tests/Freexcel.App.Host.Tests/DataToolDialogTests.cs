using System.IO;
using FluentAssertions;
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
    public void TextToColumnsDialog_ExposesDelimitedAndFixedWidthSplitChoices()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "TextToColumnsDialog.cs"));

        source.Should().Contain("Original data type");
        source.Should().Contain("Content = \"_Delimited\"");
        source.Should().Contain("Content = \"Fi_xed width\"");
        source.Should().Contain("CreateFixedWidthResult");
        source.Should().Contain("ParseFixedWidthBreakPositions");
        source.Should().Contain("Choose the delimiters that separate your selected text.");
        source.Should().Contain("Header = \"Delimiters\"");
        source.Should().Contain("Header = \"Fixed width\"");
        source.Should().Contain("Text _qualifier:");
        source.Should().Contain("_Treat consecutive delimiters as one");
        source.Should().Contain("_Destination:");
    }

    [Fact]
    public void TextToColumnsDialog_ExposesDelimiterPreviewAffordances()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "TextToColumnsDialog.cs"));

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
        source.Should().Contain("ReferencePickerButton_Click");
    }

    [Fact]
    public void TextToColumnsDialog_UsesExcelWizardChromeAroundDelimitedFlow()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "TextToColumnsDialog.cs"));

        source.Should().Contain("Step 2 of 3");
        source.Should().Contain("CreateWizardButtonRow");
        source.Should().Contain("Content = \"< _Back\"");
        source.Should().Contain("Content = \"_Next >\"");
        source.Should().Contain("Content = \"_Finish\"");
        source.Should().Contain("IsDefault = true");
        source.Should().Contain("Accept()");
        source.Should().NotContain("Additional wizard steps are not supported yet.");
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
        result.UniqueRecordsOnly.Should().BeTrue();
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
        error.Should().Be("Enter a valid copy-to cell.");
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
        source.Should().Contain("Content = \"...\"");
        source.Should().Contain("ToolTip = automationName");
        source.Should().NotContain("Content = \"Collapse Dialog\"");
        source.Should().NotContain("Text = \"E1:F2\"");
        source.Should().Contain("Header = \"Action\"");
        source.Should().Contain("Criteria should include column labels");
        source.Should().Contain("ReferencePickerButton_Click");
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

        var result = ConsolidateDialog.CreateResult([first, second], new CellAddress(sheetId, 9, 1));
        result.SourceRanges.Should().Equal(first, second);
        result.DestinationCell.Should().Be(new CellAddress(sheetId, 9, 1));
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
    }

    [Fact]
    public void ConsolidateDialog_JoinsAllReferencesListForExistingParser()
    {
        ConsolidateDialog.SplitSourceRangeText("A1:B3; D5:E7").Should().Equal("A1:B3", "D5:E7");
        ConsolidateDialog.JoinSourceRanges(["A1:B3", "D5:E7"]).Should().Be("A1:B3; D5:E7");
    }

    [Fact]
    public void ConsolidateDialog_ExposesExcelStyleAllReferencesWorkflow()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "ConsolidateDialog.cs"));

        source.Should().Contain("_referenceBox");
        source.Should().Contain("_referencesList");
        source.Should().Contain("_Reference:");
        source.Should().Contain("_All references:");
        source.Should().Contain("_Destination cell:");
        source.Should().Contain("Use _labels in:");
        source.Should().Contain("Content = \"_Add\"");
        source.Should().Contain("Content = \"_Delete\"");
        source.Should().Contain("AddReferenceButton_Click");
        source.Should().Contain("DeleteReferenceButton_Click");
        source.Should().Contain("CreateReferenceEditor(_referenceBox");
    }

    [Fact]
    public void ConsolidateDialog_ExposesExcelStyleFunctionLabelsAndLinkOptions()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "ConsolidateDialog.cs"));

        source.Should().Contain("_functionBox");
        source.Should().Contain("_topRowBox");
        source.Should().Contain("_leftColumnBox");
        source.Should().Contain("_createLinksBox");
        source.Should().Contain("_Function:");
        source.Should().Contain("_Top row");
        source.Should().Contain("_Left column");
        source.Should().Contain("Create _links to source data");
        source.Should().Contain("DisableUnsupported(_functionBox, SumOnlyHelpText)");
        source.Should().Contain("DisableUnsupported(_topRowBox, LabelMatchingHelpText)");
        source.Should().Contain("DisableUnsupported(_leftColumnBox, LabelMatchingHelpText)");
        source.Should().Contain("DisableUnsupported(_createLinksBox, SourceLinksHelpText)");
        source.Should().Contain("Only Sum consolidation is currently applied.");
        source.Should().Contain("source ranges are consolidated by position");
        source.Should().Contain("consolidated values are written as results");
        source.Should().Contain("AutomationProperties.SetHelpText(control, helpText)");
    }

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
        oneVariable.FormulaCell.Should().Be(new CellAddress(sheetId, 2, 3));
        oneVariable.RowInputCell.Should().BeNull();
        oneVariable.ColumnInputCell.Should().Be(new CellAddress(sheetId, 1, 3));

        var twoVariableParsed = DataTableDialog.TryParse(
            sheetId,
            range,
            rowInputCellText: "A1",
            columnInputCellText: "C1",
            out var twoVariable,
            out var twoVariableError);

        twoVariableParsed.Should().BeTrue(twoVariableError);
        twoVariable.Mode.Should().Be(DataTableMode.TwoVariable);
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
        source.Should().Contain("ReferencePickerButton_Click");
        source.Should().Contain("Select row input cell");
        source.Should().Contain("Select column input cell");
        source.Should().Contain("Content = \"...\"");
        source.Should().NotContain("Content = \"Collapse Dialog\"");
        source.Should().Contain("var labelBlock = new Label");
        source.Should().Contain("Target = textBox");
        source.Should().Contain("Header = \"Inputs\"");
        source.Should().Contain("DataTableInputParser.GetDefaultFormulaCell");
    }

    [Fact]
    public void CreateTableDialog_ExposesHeadersCheckboxAndRangePicker()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "CreateTableDialog.cs"));

        source.Should().Contain("_headersBox");
        source.Should().Contain("Content = \"_My table has headers\"");
        source.Should().Contain("new Label { Content = \"_Where is the data for your table?\", Target = _rangeBox");
        source.Should().Contain("CreateReferenceEditor(_rangeBox");
        source.Should().Contain("ReferencePickerButton_Click");
        source.Should().Contain("Select table range");
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
    public void RemoveDuplicatesDialog_ExposesExcelStyleBulkHeaderAndColumnListControls()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "RemoveDuplicatesDialog.cs"));
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
}
