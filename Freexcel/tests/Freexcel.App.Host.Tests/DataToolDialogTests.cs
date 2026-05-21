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
    public void TextToColumnsDialog_ExposesExcelWizardStepStateAndSourceModeChoices()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "TextToColumnsDialog.cs"));

        source.Should().Contain("_stepOneIndicator");
        source.Should().Contain("_stepTwoIndicator");
        source.Should().Contain("_stepThreeIndicator");
        source.Should().Contain("Step 1 of 3");
        source.Should().Contain("_delimitedButton");
        source.Should().Contain("_fixedWidthButton");
        source.Should().Contain("Delimited");
        source.Should().Contain("Fixed width");
    }

    [Fact]
    public void TextToColumnsDialog_ExposesExcelDelimiterQualifierPreviewAndDestinationAffordances()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "TextToColumnsDialog.cs"));

        foreach (var content in new[]
        {
            "_Tab",
            "_Semicolon",
            "_Comma",
            "S_pace",
            "_Other:",
            "Text _qualifier:",
            "Data preview",
            "_Destination:",
            "CreateReferenceEditor(_destinationBox",
            "ReferencePickerButton_Click"
        })
            source.Should().Contain(content);

        source.Should().Contain("_previewGrid");
        source.Should().Contain("_textQualifierBox");
        source.Should().Contain("_destinationBox");
        source.Should().Contain("var label = new Label");
        source.Should().Contain("Content = \"Text _qualifier:\"");
        source.Should().Contain("Target = _textQualifierBox");
        source.Should().Contain("new Label { Content = \"_Destination:\", Target = _destinationBox");
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
            "_Use function:"
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
        source.Should().Contain("Filter the list, in-place");
        source.Should().Contain("Copy to another location");
        source.Should().Contain("AddReferenceRow(rangesGrid, 0, \"_List range:\", _listRangeBox");
        source.Should().Contain("AddReferenceRow(rangesGrid, 1, \"_Criteria range:\", _criteriaRangeBox");
        source.Should().Contain("AddReferenceRow(rangesGrid, 2, \"Copy _to:\", _copyToBox");
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

        var oneVariableParsed = DataTableDialog.TryParse(
            sheetId,
            DataTableMode.OneVariable,
            formulaCellText: "B2",
            rowInputCellText: "",
            columnInputCellText: "C1",
            out var oneVariable,
            out var oneVariableError);

        oneVariableParsed.Should().BeTrue(oneVariableError);
        oneVariable.Mode.Should().Be(DataTableMode.OneVariable);
        oneVariable.FormulaCell.Should().Be(new CellAddress(sheetId, 2, 2));
        oneVariable.RowInputCell.Should().BeNull();
        oneVariable.ColumnInputCell.Should().Be(new CellAddress(sheetId, 1, 3));

        var twoVariableParsed = DataTableDialog.TryParse(
            sheetId,
            DataTableMode.TwoVariable,
            formulaCellText: "B2",
            rowInputCellText: "A1",
            columnInputCellText: "C1",
            out var twoVariable,
            out var twoVariableError);

        twoVariableParsed.Should().BeTrue(twoVariableError);
        twoVariable.RowInputCell.Should().Be(new CellAddress(sheetId, 1, 1));
        twoVariable.ColumnInputCell.Should().Be(new CellAddress(sheetId, 1, 3));
    }

    [Fact]
    public void DataTableDialog_RejectsInvalidFormulaCell()
    {
        var sheetId = SheetId.New();

        var parsed = DataTableDialog.TryParse(
            sheetId,
            DataTableMode.OneVariable,
            formulaCellText: "not-a-cell",
            rowInputCellText: "",
            columnInputCellText: "C1",
            out _,
            out var error);

        parsed.Should().BeFalse();
        error.Should().Be("Enter a valid formula cell.");
    }

    [Fact]
    public void DataTableDialog_RejectsInvalidOptionalInputCell()
    {
        var sheetId = SheetId.New();

        var parsed = DataTableDialog.TryParse(
            sheetId,
            DataTableMode.OneVariable,
            formulaCellText: "B2",
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

        source.Should().Contain("AddReferenceRow(grid, 0, \"_Formula cell:\", _formulaBox");
        source.Should().Contain("AddReferenceRow(grid, 1, \"_Row input cell:\", _rowInputBox");
        source.Should().Contain("AddReferenceRow(grid, 2, \"_Column input cell:\", _columnInputBox");
        source.Should().Contain("ReferencePickerButton_Click");
        source.Should().Contain("Select formula cell");
        source.Should().Contain("Select row input cell");
        source.Should().Contain("Select column input cell");
        source.Should().Contain("new Label { Content = \"_Type:\", Target = _modeBox");
        source.Should().Contain("var labelBlock = new Label");
        source.Should().Contain("Target = textBox");
        source.Should().Contain("Header = \"Inputs\"");
        source.Should().Contain("One-variable data tables use either");
        source.Should().Contain("Two-variable data tables require both");
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

        source.Should().Contain("_Select All");
        source.Should().Contain("_Unselect All");
        source.Should().Contain("_My data has headers");
        source.Should().Contain("_columnsPanel");
        source.Should().Contain("Columns:");
        source.Should().Contain("SelectAllButton_Click");
        source.Should().Contain("UnselectAllButton_Click");
    }
}
