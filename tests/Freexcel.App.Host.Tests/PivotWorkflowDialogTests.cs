using System.IO;
using System.Reflection;
using System.Windows.Controls;
using FluentAssertions;
using Freexcel.Core.Model;

namespace Freexcel.App.Host.Tests;

public sealed class PivotWorkflowDialogTests
{
    [Fact]
    public void PivotTableDialog_CreateResult_CapturesExcelCreatePivotChoices()
    {
        var result = PivotTableDialog.CreateResult(
            "  Sales!A1:D20  ",
            PivotTableDestinationKind.ExistingWorksheet,
            "  Report!F3  ",
            openFieldList: true);

        result.SourceRangeText.Should().Be("Sales!A1:D20");
        result.DestinationKind.Should().Be(PivotTableDestinationKind.ExistingWorksheet);
        result.DestinationRangeText.Should().Be("Report!F3");
        result.OpenFieldList.Should().BeTrue();
    }

    [Fact]
    public void PivotTableDialog_DefaultResult_UsesNewWorksheetDestinationAndFieldList()
    {
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sales");
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 20, 4));

        StaTestRunner.Run(() =>
        {
            var dialog = new PivotTableDialog(workbook, sheet.Id, range);

            dialog.Result.SourceRangeText.Should().Be("Sales!A1:D20");
            dialog.Result.DestinationKind.Should().Be(PivotTableDestinationKind.NewWorksheet);
            dialog.Result.DestinationRangeText.Should().BeEmpty();
            dialog.Result.OpenFieldList.Should().BeTrue();
        });
    }

    [Fact]
    public void PivotTableDialog_ExposesReferencePickersForSourceAndExistingLocation()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "PivotTableDialog.cs"));

        source.Should().Contain("AddLabeledReferenceEditor(");
        source.Should().Contain("_sourceRangeBox,");
        source.Should().Contain("_destinationRangeBox,");
        source.Should().Contain("CreateReferenceEditor(textBox, automationName, editorMargin)");
        source.Should().Contain("Select PivotTable source range");
        source.Should().Contain("Select PivotTable location");
        source.Should().Contain("ReferencePickerButton_Click");
        source.Should().Contain("UpdateDestinationState");
    }

    [Fact]
    public void PivotTableDialog_ExposesOnlySupportedSourceAndPlacementChoices()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "PivotTableDialog.cs"));

        source.Should().Contain("Choose the data that you want to analyze");
        source.Should().Contain("_selectTableRangeButton");
        source.Should().Contain("_New worksheet");
        source.Should().Contain("_Existing worksheet");
        source.Should().NotContain("_externalSourceButton");
        source.Should().NotContain("_dataModelBox");
        source.Should().NotContain("Use an _external data source");
        source.Should().NotContain("Add this data to the Data _Model");
    }

    [Fact]
    public void PivotTableDialog_ExposesKeyboardAccessKeysForChoicesAndButtons()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "PivotTableDialog.cs"));

        source.Should().Contain("Content = \"_Create\"");
        source.Should().Contain("Content = \"_Cancel\"");
        source.Should().Contain("Content = \"_New worksheet\"");
        source.Should().Contain("Content = \"_Existing worksheet\"");
        source.Should().Contain("Content = \"Open PivotTable _Fields pane\"");
        source.Should().NotContain("Content = \"Use an _external data source\"");
        source.Should().NotContain("Content = \"Add this data to the Data _Model\"");
    }

    [Fact]
    public void PivotTableDialog_LabelsRangeEditorsWithAccessKeyTargets()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "PivotTableDialog.cs"));

        foreach (var content in new[]
        {
            "AddLabeledReferenceEditor(",
            "\"Table/_Range:\"",
            "\"_Location:\"",
            "_sourceRangeBox,",
            "_destinationRangeBox,",
            "new Label",
            "Target = textBox",
            "CreateReferenceEditor(textBox, automationName, editorMargin)"
        })
            source.Should().Contain(content);
    }

    [Fact]
    public void PivotTableDataSourceDialog_CreateResult_TrimsSourceRangeText()
    {
        PivotTableDataSourceDialog.CreateResult("  Sales!A1:E200  ")
            .SourceRangeText
            .Should()
            .Be("Sales!A1:E200");
    }

    [Fact]
    public void PivotTableDataSourceDialog_ExposesReferencePickerForSourceRange()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "PivotWorkflowDialogs.cs"));

        source.Should().Contain("CreateReferenceEditor(_sourceBox");
        source.Should().Contain("Select PivotTable source range");
        source.Should().Contain("ReferencePickerButton_Click");
    }

    [Fact]
    public void PivotAuxiliaryDialogs_LabelEditableFieldsWithAccessKeyTargets()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "PivotWorkflowDialogs.cs"));

        foreach (var content in new[]
        {
            "PivotDialogLayout.AddLabeledControl(",
            "\"Table/_Range:\"",
            "CreateReferenceEditor(_sourceBox",
            "_sourceBox,",
            "PivotDialogLayout.AddLabeledControl(fieldPanel, \"_Field to connect\", _fieldBox",
            "PivotDialogLayout.AddLabeledControl(fieldPanel, \"Slicer _caption\", _nameBox",
            "PivotDialogLayout.AddLabeledControl(fieldPanel, \"_Date field to connect\", _fieldBox",
            "PivotDialogLayout.AddLabeledControl(fieldPanel, \"Timeline _caption\", _nameBox",
            "InsertChartDialog.CreateAllChartsPanel(_categoryList, _subtypeGallery",
            "AutomationProperties.SetName(_styleGallery, \"PivotChart style gallery\")",
            "AddCombo(selectionPanel, \"_Field\", _fieldBox",
            "AddCombo(groupingPanel, \"_Group by\", _groupingBox",
            "AddTextBox(rangePanel, \"_Starting at\", _startBox",
            "AddTextBox(rangePanel, \"_Ending at\", _endBox",
            "AddTextBox(rangePanel, \"_By\", _intervalBox",
            "AddTextBox(formulaPanel, \"_Name\", _nameBox",
            "AddTextBox(formulaPanel, \"_Formula:\", _formulaBox",
            "PivotDialogLayout.AddLabeledControl(itemPanel, \"Source _field\", _fieldBox",
            "AddTextBox(itemPanel, \"Item _formula\", _formulaBox",
            "public static void AddLabeledControl(Panel stack, string label, UIElement control",
            "Target = target"
        })
            source.Should().Contain(content);
    }

    [Fact]
    public void InsertSlicerDialog_CreateResult_CapturesFieldAndSlicerName()
    {
        InsertSlicerDialog.CreateResult("  Region  ", "  Region Slicer  ")
            .Should()
            .Be(new InsertSlicerDialogResult("Region", "Region Slicer"));
    }

    [Fact]
    public void InsertSlicerDialog_ExposesExcelLikeFieldSelectionShell()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "PivotWorkflowDialogs.cs"));

        source.Should().Contain("Choose fields");
        source.Should().Contain("_Field to connect");
        source.Should().Contain("Slicer _caption");
        source.Should().Contain("DialogButtonRowFactory.Create");
        source.Should().NotContain("Slicers make it faster to filter a PivotTable");
    }

    [Fact]
    public void InsertTimelineDialog_CreateResult_CapturesDateFieldAndTimelineName()
    {
        InsertTimelineDialog.CreateResult("  Order Date  ", "  Order Date Timeline  ")
            .Should()
            .Be(new InsertTimelineDialogResult("Order Date", "Order Date Timeline"));
    }

    [Fact]
    public void InsertTimelineDialog_ExposesExcelLikeDateFieldSelectionShell()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "PivotWorkflowDialogs.cs"));

        source.Should().Contain("Choose date fields");
        source.Should().Contain("_Date field to connect");
        source.Should().Contain("Timeline _caption");
        source.Should().NotContain("Timelines filter PivotTables by date");
    }

    [Fact]
    public void PivotChartTypeDialog_PreselectsCurrentTypeAndBuildsResult()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = new PivotChartTypeDialog(ChartType.Line);

            dialog.SelectedChartType.Should().Be(ChartType.Line);
            PivotChartTypeDialog.CreateResult(ChartType.StackedColumn)
                .Should()
                .Be(new PivotChartTypeDialogResult(ChartType.StackedColumn));
        });
    }

    [Fact]
    public void PivotChartTypeDialog_ExposesSelectableRecommendedPivotCharts()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "PivotWorkflowDialogs.cs"));

        source.Should().Contain("Recommended PivotCharts");
        source.Should().Contain("All Charts");
        source.Should().Contain("private readonly ListBox _recommendedGallery");
        source.Should().Contain("CreateRecommendedChartsPanel(_recommendedGallery)");
        source.Should().Contain("SelectedGalleryChoice()");
        source.Should().NotContain("Pick a chart type for the selected PivotTable data");
        source.Should().Contain("InsertChartDialog.CreateAllChartsPanel");
        source.Should().Contain("Chart categories");
        source.Should().Contain("Chart subtype gallery");
        source.Should().NotContain("private readonly ComboBox _chartTypeBox");
    }

    [Fact]
    public void PivotChartInsert_UsesTypeDialogInsteadOfHardCodedColumn()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.PivotCommands.cs"));
        var methodStart = source.IndexOf("private void PivotChartBtn_Click", StringComparison.Ordinal);
        var methodEnd = source.IndexOf("private void PivotChartChangeTypeBtn_Click", StringComparison.Ordinal);
        methodStart.Should().BeGreaterThanOrEqualTo(0);
        methodEnd.Should().BeGreaterThan(methodStart);
        var method = source[methodStart..methodEnd];

        method.Should().Contain("new PivotChartTypeDialog(ChartType.Column)");
        method.Should().Contain("dialog.Result.ChartType");
        method.Should().NotContain("new AddPivotChartCommand(_currentSheetId, pivotTable.Name, ChartType.Column");
    }

    [Fact]
    public void PivotTableOptionsDialog_CreateResult_CapturesModeledLayoutAndStyleSettings()
    {
        var result = PivotTableOptionsDialog.CreateResult(
            showRowGrandTotals: true,
            showColumnGrandTotals: false,
            showSubtotals: true,
            subtotalPlacement: PivotSubtotalPlacement.Top,
            repeatItemLabels: false,
            blankLineAfterItems: true,
            styleName: "  PivotStyleMedium9  ",
            showRowHeaders: false,
            showColumnHeaders: true,
            showRowStripes: true,
            showColumnStripes: false,
            reportLayout: PivotReportLayout.Outline,
            emptyValueText: "  N/A  ",
            refreshOnOpen: true,
            saveSourceData: false,
            showExpandCollapseButtons: false,
            autofitColumnsOnUpdate: false,
            preserveFormattingOnUpdate: false,
            compactRowLabelIndent: 3);

        result.Should().Be(new PivotTableOptionsDialogResult(
            true,
            false,
            true,
            PivotSubtotalPlacement.Top,
            false,
            true,
            "PivotStyleMedium9",
            false,
            true,
            true,
            false,
            PivotReportLayout.Outline,
            "N/A",
            true,
            false,
            false,
            ShowExpandCollapseButtons: false,
            AutofitColumnsOnUpdate: false,
            PreserveFormattingOnUpdate: false,
            CompactRowLabelIndent: 3));
    }

    [Fact]
    public void PivotTableOptionsDialog_FromPivotTable_UsesConnectedCacheDataOptions()
    {
        var pivotTable = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 7,
            StyleName = "PivotStyleMedium4"
        };
        var cache = new PivotCacheModel
        {
            CacheId = 7,
            RefreshOnLoad = true,
            SaveData = false
        };

        PivotTableOptionsDialog.FromPivotTable(pivotTable, cache)
            .Should()
            .Match<PivotTableOptionsDialogResult>(result =>
                result.RefreshOnOpen &&
                !result.SaveSourceData);
    }

    [Fact]
    public void PivotTableOptionsDialog_FromPivotTable_UsesCurrentPivotSettings()
    {
        var sheetId = new SheetId(Guid.NewGuid());
        var pivotTable = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 12, 4)),
            TargetRange = new GridRange(new CellAddress(sheetId, 15, 1), new CellAddress(sheetId, 22, 4)),
            ShowRowGrandTotals = false,
            ShowColumnGrandTotals = true,
            ShowSubtotals = true,
            SubtotalPlacement = PivotSubtotalPlacement.Top,
            RepeatItemLabels = false,
            BlankLineAfterItems = true,
            ReportLayout = PivotReportLayout.Compact,
            StyleName = "PivotStyleDark4",
            ShowRowHeaders = true,
            ShowColumnHeaders = false,
            ShowRowStripes = true,
            ShowColumnStripes = true,
            EmptyValueText = "-",
            ShowExpandCollapseButtons = false,
            PrintExpandCollapseButtons = true,
            AutofitColumnsOnUpdate = false,
            PreserveFormattingOnUpdate = false,
            CompactRowLabelIndent = 5
        };

        PivotTableOptionsDialog.FromPivotTable(pivotTable)
            .Should()
            .Be(new PivotTableOptionsDialogResult(
                false,
                true,
                true,
                PivotSubtotalPlacement.Top,
                false,
                true,
                "PivotStyleDark4",
                true,
                false,
                true,
                true,
                PivotReportLayout.Compact,
                "-",
                PrintExpandCollapseButtons: true,
                ShowExpandCollapseButtons: false,
                AutofitColumnsOnUpdate: false,
                PreserveFormattingOnUpdate: false,
                CompactRowLabelIndent: 5));
    }

    [Fact]
    public void PivotTableOptionsDialog_ExposesBroaderPivotStyleGalleryAndPreservesCurrentStyle()
    {
        var sheetId = new SheetId(Guid.NewGuid());
        var pivotTable = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 12, 4)),
            TargetRange = new GridRange(new CellAddress(sheetId, 15, 1), new CellAddress(sheetId, 22, 4)),
            StyleName = "PivotStyleMedium10"
        };

        StaTestRunner.Run(() =>
        {
            var dialog = new PivotTableOptionsDialog(pivotTable);
            var styleBox = (ComboBox)typeof(PivotTableOptionsDialog)
                .GetField("_styleBox", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(dialog)!;
            var styleNames = styleBox.Items.Cast<object>().Select(item => item.ToString()).ToList();

            styleNames.Should().Contain(["PivotStyleLight16", "PivotStyleMedium10", "PivotStyleDark7"]);
            styleNames.Should().HaveCountGreaterThan(12);
            styleBox.SelectedItem.Should().Be("PivotStyleMedium10");

            dialog.Close();
        });
    }

    [Fact]
    public void PivotTableOptionsDialog_UsesExcelStyleTabbedOptionShell()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "PivotWorkflowDialogs.cs"));

        foreach (var content in new[]
        {
            "Layout & Format",
            "Totals & Filters",
            "Display",
            "Data",
            "Printing",
            "Alt Text",
            "_emptyCellsBox",
            "_compactIndentBox",
            "_autofitColumnsBox",
            "_preserveFormattingBox",
            "_refreshOnOpenBox",
            "_showExpandCollapseBox",
            "_printTitlesBox",
            "_printExpandCollapseBox",
            "_altTextTitleBox",
            "_altTextDescriptionBox"
        })
            source.Should().Contain(content);
        source.Should().NotContain("Title and description metadata can be added in a future pass.");
    }

    [Fact]
    public void PivotTableOptionsDialog_ExposesPrintingTab()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "PivotWorkflowDialogs.cs"));

        source.Should().Contain("Header = \"Printing\"");
        source.Should().Contain("Show expand/collapse _buttons");
        source.Should().Contain("Set print _titles");
        source.Should().Contain("Print expand/collapse _buttons when displayed on PivotTable");
        source.Should().NotContain("Print titles and print expand/collapse buttons are not yet available.");
    }

    [Fact]
    public void PivotTableOptionsDialog_ExposesExcelLikeGroupsInsideTabs()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "PivotWorkflowDialogs.cs"));

        foreach (var content in new[]
        {
            "Layout section",
            "Format section",
            "Grand totals",
            "PivotTable Style Options",
            "Data options",
            "Print options",
            "Alt Text",
            "Preserve source sort and _filter settings"
        })
            source.Should().Contain(content);

        source.Should().NotContain("Field list and buttons remain available");
    }

    [Fact]
    public void PivotTableOptionsDialog_LabelsEditableOptionsWithAccessKeyTargets()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "PivotWorkflowDialogs.cs"));

        foreach (var content in new[]
        {
            "AddLabeledControl(layoutPanel, \"_Report layout\", _reportLayoutBox",
            "AddLabeledControl(layoutPanel, \"When in compact form indent row labels\", _compactIndentBox",
            "AddLabeledControl(formatPanel, \"For _empty cells show:\", _emptyCellsBox",
            "AddLabeledControl(filtersPanel, \"Subtotal _placement\", _subtotalPlacementBox",
            "AddLabeledControl(stylePanel, \"PivotTable _style\", _styleBox",
            "new Label",
            "Content = label",
            "Target = control"
        })
            source.Should().Contain(content);
    }

    [Fact]
    public void PivotTableOptionsDialog_ExposesAccessKeysForModeledCheckboxes()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "PivotWorkflowDialogs.cs"));

        foreach (var content in new[]
        {
            "Content = \"Show _row grand totals\"",
            "Content = \"Show _column grand totals\"",
            "Content = \"Show _subtotals\"",
            "Content = \"_Repeat item labels\"",
            "Content = \"Insert _blank line after each item\"",
            "Content = \"Row _headers\"",
            "Content = \"Column hea_ders\"",
            "Content = \"Banded _rows\"",
            "Content = \"Banded c_olumns\"",
            "Content = \"_Autofit column widths on update\"",
            "Content = \"_Preserve cell formatting on update\"",
            "Content = \"_Refresh data when opening the file\"",
            "Content = \"Show expand/collapse _buttons\"",
            "Content = \"Set print _titles\"",
            "Content = \"Print expand/collapse _buttons when displayed on PivotTable\""
        })
            source.Should().Contain(content);
    }

    [Fact]
    public void PivotTableOptionsDialog_ResultIncludesPrintingAndAltText()
    {
        var result = PivotTableOptionsDialog.CreateResult(
            showRowGrandTotals: true,
            showColumnGrandTotals: false,
            showSubtotals: true,
            PivotSubtotalPlacement.Top,
            repeatItemLabels: true,
            blankLineAfterItems: false,
            " PivotStyleMedium4 ",
            showRowHeaders: true,
            showColumnHeaders: true,
            showRowStripes: false,
            showColumnStripes: true,
            PivotReportLayout.Outline,
            emptyValueText: " - ",
            refreshOnOpen: true,
            saveSourceData: false,
            compactRowLabelIndent: 6,
            showExpandCollapseButtons: false,
            autofitColumnsOnUpdate: false,
            preserveFormattingOnUpdate: false,
            printTitles: true,
            printExpandCollapseButtons: true,
            altTextTitle: "  Sales pivot ",
            altTextDescription: " Quarterly sales summary ");

        result.ShowExpandCollapseButtons.Should().BeFalse();
        result.AutofitColumnsOnUpdate.Should().BeFalse();
        result.PreserveFormattingOnUpdate.Should().BeFalse();
        result.PrintTitles.Should().BeTrue();
        result.PrintExpandCollapseButtons.Should().BeTrue();
        result.CompactRowLabelIndent.Should().Be(6);
        result.AltTextTitle.Should().Be("Sales pivot");
        result.AltTextDescription.Should().Be("Quarterly sales summary");
    }

    [Fact]
    public void PivotFieldGroupingDialog_CreateResult_TrimsFieldAndClampsNumberRangeInterval()
    {
        var result = PivotFieldGroupingDialog.CreateResult(
            "  Order Date  ",
            sourceFieldIndex: -3,
            PivotFieldGrouping.NumberRange,
            "  10  ",
            "  90  ",
            "  -5  ",
            ungroup: false);

        result.Should().Be(new PivotFieldGroupingDialogResult(
            "Order Date",
            0,
            PivotFieldGrouping.NumberRange,
            10,
            90,
            1,
            false));
    }

    [Fact]
    public void PivotFieldGroupingDialog_CreateResult_UngroupClearsGroupingSettings()
    {
        var result = PivotFieldGroupingDialog.CreateResult(
            " Region ",
            sourceFieldIndex: 2,
            PivotFieldGrouping.Month,
            "1",
            "12",
            "3",
            ungroup: true);

        result.Should().Be(new PivotFieldGroupingDialogResult(
            "Region",
            2,
            PivotFieldGrouping.None,
            null,
            null,
            null,
            true));
    }

    [Fact]
    public void PivotFieldGroupingDialog_FromPivotField_UsesCurrentFieldSettings()
    {
        var field = new PivotFieldModel(
            SourceFieldIndex: 1,
            Grouping: PivotFieldGrouping.Month,
            GroupStart: 44562,
            GroupEnd: 44927,
            GroupInterval: 2);

        PivotFieldGroupingDialog.FromPivotField(["Region", "Order Date"], field)
            .Should()
            .Be(new PivotFieldGroupingDialogResult(
                "Order Date",
                1,
                PivotFieldGrouping.Month,
                44562,
                44927,
                2,
                false));
    }

    [Fact]
    public void PivotFieldGroupingDialog_FromPivotField_DefaultsToFirstFieldWhenCurrentSettingsAreMissing()
    {
        PivotFieldGroupingDialog.FromPivotField(["Region", "Order Date"], currentField: null)
            .Should()
            .Be(new PivotFieldGroupingDialogResult(
                "Region",
                0,
                PivotFieldGrouping.None,
                null,
                null,
                null,
                false));
    }

    [Fact]
    public void PivotFieldGroupingDialog_ExposesExcelLikeGroupingSections()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "PivotWorkflowDialogs.cs"));

        source.Should().Contain("Selection");
        source.Should().Contain("Group by");
        source.Should().Contain("Range");
        source.Should().NotContain("Select the PivotTable field and grouping interval");
    }

    [Fact]
    public void PivotCalculatedFieldDialog_CreateResult_TrimsAndBuildsModel()
    {
        var result = PivotCalculatedFieldDialog.CreateResult("  Revenue  ", "  Sales-Cost  ");

        result.Should().Be(new PivotCalculatedFieldDialogResult("Revenue", "Sales-Cost"));
        result.ToModel().Should().Be(new PivotCalculatedFieldModel("Revenue", "Sales-Cost"));
    }

    [Fact]
    public void PivotCalculatedFieldDialog_ExposesExcelLikeFormulaEditorShell()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "PivotWorkflowDialogs.cs"));

        source.Should().Contain("Name and formula");
        source.Should().Contain("Formula:");
        source.Should().NotContain("Use field names in formulas");
        source.Should().NotContain("Calculated fields are added to the Values area");
    }

    [Fact]
    public void PivotCalculatedFieldDialog_ExposesFieldsListAndInsertFieldControl()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "PivotWorkflowDialogs.cs"));

        source.Should().Contain("private readonly ListBox _fieldList");
        source.Should().Contain("Available _fields");
        source.Should().Contain("Insert _Field");
        source.Should().Contain("InsertSelectedField");
        source.Should().Contain("InsertFormulaReference");
    }

    [Fact]
    public void PivotCalculatedFieldDialog_InsertFormulaReference_InsertsQuotedFieldAtCaret()
    {
        PivotCalculatedFieldDialog.InsertFormulaReference("Sales+Cost", "[Region Name]", 6, 0)
            .Should()
            .Be("Sales+[Region Name]Cost");
    }

    [Fact]
    public void PivotCalculatedItemDialog_CreateResult_TrimsClampsAndBuildsModel()
    {
        var result = PivotCalculatedItemDialog.CreateResult(
            "  Region  ",
            sourceFieldIndex: -8,
            "  East + West  ",
            "  East+West  ");

        result.Should().Be(new PivotCalculatedItemDialogResult("Region", 0, "East + West", "East+West"));
        result.ToModel().Should().Be(new PivotCalculatedItemModel(0, "East + West", "East+West"));
    }

    [Fact]
    public void PivotCalculatedItemDialog_ExposesExcelLikeFormulaEditorShell()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "PivotWorkflowDialogs.cs"));

        source.Should().Contain("Field and item");
        source.Should().NotContain("Calculated items are evaluated within the selected field");
        source.Should().Contain("Source _field");
        source.Should().Contain("Item _formula");
    }

    [Fact]
    public void PivotCalculatedItemDialog_ExposesFieldItemListsAndInsertionControls()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "PivotWorkflowDialogs.cs"));

        source.Should().Contain("private readonly ListBox _fieldList");
        source.Should().Contain("private readonly ListBox _itemList");
        source.Should().Contain("Available _items");
        source.Should().Contain("Insert _Field");
        source.Should().Contain("Insert _Item");
        source.Should().Contain("RefreshItemList");
        source.Should().Contain("InsertSelectedItem");
    }

    [Fact]
    public void PivotCalculatedItemDialog_InsertFormulaReference_ReplacesSelectedFormulaText()
    {
        PivotCalculatedItemDialog.InsertFormulaReference("East+West", "North", 5, 4)
            .Should()
            .Be("East+North");
    }

    [Fact]
    public void PivotChartOptionsDialog_CreateResult_ParsesAndClampsStyle()
    {
        PivotChartOptionsDialog.CreateResult(
                " 99 ",
                showFieldButtons: false,
                showReportFilterButtons: true,
                showAxisFieldButtons: false,
                showValueFieldButtons: true)
            .Should()
            .Be(new PivotChartOptionsDialogResult(48, false, true, false, true));

        PivotChartOptionsDialog.CreateResult(
                "not-a-style",
                showFieldButtons: true,
                showReportFilterButtons: false,
                showAxisFieldButtons: true,
                showValueFieldButtons: false)
            .Should()
            .Be(new PivotChartOptionsDialogResult(null, true, false, true, false));

        PivotChartOptionsDialog.CreateResult(
                99,
                showFieldButtons: true,
                showReportFilterButtons: true,
                showAxisFieldButtons: true,
                showValueFieldButtons: true,
                roundedCorners: true,
                showHiddenData: true,
                blankDisplayMode: ChartBlankDisplayMode.Zero)
            .Should()
            .Be(new PivotChartOptionsDialogResult(48, true, true, true, true, false, false, true, true, ChartBlankDisplayMode.Zero));
    }

    [Fact]
    public void PivotChartOptionsDialog_FromChart_UsesCurrentSettings()
    {
        var chart = new ChartModel
        {
            ChartStyleId = 12,
            ShowPivotChartFieldButtons = false,
            ShowPivotChartReportFilterButtons = true,
            ShowPivotChartAxisFieldButtons = false,
            ShowPivotChartValueFieldButtons = true,
            DataTable = new ChartDataTableModel { ShowLegendKeys = true },
            RoundedCorners = true,
            ShowDataInHiddenRowsAndColumns = true,
            BlankDisplayMode = ChartBlankDisplayMode.Span
        };

        PivotChartOptionsDialog.FromChart(chart)
            .Should()
            .Be(new PivotChartOptionsDialogResult(12, false, true, false, true, true, true, true, true, ChartBlankDisplayMode.Span));
    }

    [Fact]
    public void PivotChartOptionsDialog_ExposesExcelLikeStyleAndFieldButtonGroups()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "PivotWorkflowDialogs.cs"));

        source.Should().Contain("Chart style");
        source.Should().Contain("_styleGallery");
        source.Should().Contain("PivotChart style gallery");
        source.Should().Contain("ChartStyleDialog.GetStyleOptions()");
        source.Should().NotContain("Chart _style ID");
        source.Should().Contain("Field buttons");
        source.Should().Contain("_Show field buttons on chart");
        source.Should().Contain("Report _filter buttons");
        source.Should().Contain("_Axis field buttons");
        source.Should().Contain("_Value field buttons");
        source.Should().Contain("Show data _table");
        source.Should().Contain("Show legend _keys");
        source.Should().Contain("_Rounded corners");
        source.Should().Contain("Show data in _hidden rows and columns");
        source.Should().Contain("_Blank cells");
        source.Should().NotContain("Style IDs match the built-in Excel chart style gallery");
        source.Should().NotContain("Field buttons let you filter and rearrange PivotChart data directly on the chart");
    }

    [Fact]
    public void PivotChartOptionsDialog_UsesVisualStyleGalleryAndPreservesCurrentStyle()
    {
        var chart = new ChartModel
        {
            IsPivotChart = true,
            ChartStyleId = 12
        };

        StaTestRunner.Run(() =>
        {
            var dialog = new PivotChartOptionsDialog(chart);
            var gallery = (ListBox)typeof(PivotChartOptionsDialog)
                .GetField("_styleGallery", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(dialog)!;
            var styleOptions = gallery.Items.Cast<ChartStyleOption>().ToList();

            styleOptions.Should().HaveCount(49);
            styleOptions[0].Should().Be(new ChartStyleOption(null, "Automatic", "Use current chart formatting"));
            gallery.SelectedItem.Should().Be(styleOptions.Single(option => option.StyleId == 12));

            dialog.Close();
        });
    }

    [Fact]
    public void PivotAuxiliaryDialogs_ExposeAccessKeysForModeledCheckboxes()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "PivotWorkflowDialogs.cs"));

        source.Should().Contain("Content = \"_Show field buttons on chart\"");
        source.Should().Contain("Content = \"Report _filter buttons\"");
        source.Should().Contain("Content = \"_Axis field buttons\"");
        source.Should().Contain("Content = \"_Value field buttons\"");
        source.Should().Contain("Content = \"_Ungroup selected field\"");
    }
}
