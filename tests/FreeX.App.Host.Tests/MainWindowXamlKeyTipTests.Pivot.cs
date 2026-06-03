using System.IO;
using System.Windows.Input;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.App.Host;

namespace FreeX.App.Host.Tests;

public sealed partial class MainWindowXamlKeyTipTests
{
    [Fact]
    public void PivotTableEntryPoint_IsAvailableOnInsertRibbon()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
        XNamespace local = "clr-namespace:FreeX.App.Host";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var buttons = document
            .Descendants(presentation + "Button")
            .Where(element => element.Attribute("Click")?.Value == "PivotTableBtn_Click")
            .ToList();

        buttons.Should().ContainSingle();
        buttons[0].Attribute(local + "RibbonTooltip.Description")?.Value.Should().Contain("Create");
    }

    [Fact]
    public void PivotTableRefreshEntryPoint_IsAvailableOnInsertRibbon()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
        XNamespace local = "clr-namespace:FreeX.App.Host";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var buttons = document
            .Descendants(presentation + "Button")
            .Where(element => element.Attribute("Click")?.Value == "RefreshPivotTableBtn_Click")
            .ToList();

        buttons.Should().NotBeEmpty();
        buttons[0].Attribute(local + "RibbonTooltip.Description")?.Value.Should().Contain("Refresh");
    }

    [Fact]
    public void PivotTableShowDetailsEntryPoint_IsAvailableOnInsertRibbon()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
        XNamespace local = "clr-namespace:FreeX.App.Host";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var buttons = document
            .Descendants(presentation + "Button")
            .Where(element => element.Attribute("Click")?.Value == "PivotTableShowDetailsBtn_Click")
            .ToList();

        buttons.Should().NotBeEmpty();
        LocalizedAttribute(buttons[0], local + "RibbonTooltip.Description").Should().Contain("detail");
    }

    [Fact]
    public void PivotTableShowDetailsGesture_IsAttemptedBeforeDoubleClickEdit()
    {
        var source =
            File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.Selection.cs")) +
            ReadPivotCommandSource();

        source.Should().Contain("e.ClickCount == 2");
        source.Should().Contain("TryShowPivotTableDetails(showMessage: false)");
    }

    [Fact]
    public void PivotTableShowDetailsCommand_UsesUndoableDrillDownAndActivatesCreatedDetailSheet()
    {
        var source = ReadPivotCommandSource();
        var handlerSource = source[
            source.IndexOf("private bool TryShowPivotTableDetails", StringComparison.Ordinal)..
            source.IndexOf("private void RefreshPivotFieldListPane", StringComparison.Ordinal)];

        handlerSource.Should().Contain("PivotUiPlanner.ResolveShowDetailsTarget(sheet, SheetGrid.SelectedRange)");
        handlerSource.Should().Contain("new DrillDownPivotTableCommand(_currentSheetId, target.PivotTableName, target.PivotCell)");
        handlerSource.Should().Contain("\"Show PivotTable Details\"");
        handlerSource.Should().Contain("out var outcome");
        handlerSource.Should().Contain("outcome.AffectedCells?.FirstOrDefault()");
        handlerSource.Should().Contain("_currentSheetId = detailAnchor.Sheet;");
        handlerSource.Should().Contain("RefreshSheetTabs();");
        handlerSource.Should().Contain("UpdateViewport();");
        handlerSource.Should().NotContain("new AddSheetCommand");
        handlerSource.Should().NotContain("_workbook.Sheets.LastOrDefault()");
        handlerSource.Should().NotContain("PivotTableRefreshService.Refresh");
    }

    [Fact]
    public void PivotChartEntryPoint_IsAvailableOnInsertRibbon()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
        XNamespace local = "clr-namespace:FreeX.App.Host";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var buttons = document
            .Descendants(presentation + "Button")
            .Where(element => element.Attribute("Click")?.Value == "PivotChartBtn_Click")
            .ToList();

        buttons.Should().NotBeEmpty();
        buttons.Should().AllSatisfy(button => LocalizedAttribute(button, "Content").Should().Contain("PivotChart"));
        buttons.Should().AllSatisfy(button => LocalizedAttribute(button, local + "RibbonTooltip.Description").Should().Contain("PivotTable"));
    }

    [Fact]
    public void PivotTableFieldListPane_HasExcelLikeZonesAndCommands()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var namedElements = document
            .Descendants()
            .Select(element => element.Attribute(xaml + "Name")?.Value)
            .Where(name => name is not null)
            .ToHashSet(StringComparer.Ordinal);

        namedElements.Should().Contain([
            "PivotFieldListPane",
            "PivotFieldListSearchBox",
            "PivotAvailableFieldsList",
            "PivotFieldListDeferLayoutCheckBox",
            "PivotFieldListUpdateBtn",
            "PivotRowsList",
            "PivotColumnsList",
            "PivotValuesList",
            "PivotFiltersList"
        ]);

        document
            .Descendants(presentation + "Button")
            .Select(button => button.Attribute("Click")?.Value)
            .Should()
            .Contain([
                "PivotFieldToRowsBtn_Click",
                "PivotFieldToColumnsBtn_Click",
                "PivotFieldToValuesBtn_Click",
                "PivotFieldToFiltersBtn_Click",
                "PivotFieldRemoveBtn_Click",
                "PivotFieldListUpdateBtn_Click",
                "PivotFieldListCloseBtn_Click"
            ]);

        document
            .Descendants(presentation + "CheckBox")
            .Single(element => element.Attribute(xaml + "Name")?.Value == "PivotFieldListDeferLayoutCheckBox")
            .Attribute("Click")?.Value
            .Should()
            .Be("PivotFieldListDeferLayoutCheckBox_Click");
    }

    [Fact]
    public void PivotTableFieldListPane_SearchAppearsBeforeAvailableFieldsList()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var searchBox = document
            .Descendants(presentation + "TextBox")
            .Single(element => element.Attribute(xaml + "Name")?.Value == "PivotFieldListSearchBox");
        var availableFieldsList = document
            .Descendants(presentation + "ListBox")
            .Single(element => element.Attribute(xaml + "Name")?.Value == "PivotAvailableFieldsList");

        LocalizedAttribute(searchBox, "AutomationProperties.Name").Should().Be("Search PivotTable Fields");
        searchBox.IsBefore(availableFieldsList).Should().BeTrue("search should be above the available fields list");
    }

    [Fact]
    public void PivotTableFieldListPane_RemoveButton_ExposesVisibleAccessKey()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var removeButton = document
            .Descendants(presentation + "Button")
            .Single(button => button.Attribute("Click")?.Value == "PivotFieldRemoveBtn_Click");

        LocalizedAttribute(removeButton, "Content").Should().Be("_Remove");
    }

    [Fact]
    public void PivotTableFieldListPane_RoutesThroughLayoutCommand()
    {
        var source = ReadPivotCommandSource();

        source.Should().Contain("RefreshPivotFieldListPane()");
        source.Should().Contain("ConfigurePivotTableLayoutCommand");
        source.Should().Contain("PivotFieldToRowsBtn_Click");
        source.Should().Contain("PivotFieldListCloseBtn_Click");
    }

    [Fact]
    public void PivotTableFieldListPane_ExposesFieldDropdownCommands()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
        XNamespace local = "clr-namespace:FreeX.App.Host";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        document
            .Descendants(presentation + "MenuItem")
            .Where(item => item.Attribute("Click")?.Value?.StartsWith("PivotField", StringComparison.Ordinal) == true)
            .Select(item => item.Attribute("Click")!.Value)
            .Should()
            .Contain([
                "PivotFieldSortAscendingMenuItem_Click",
                "PivotFieldSortDescendingMenuItem_Click",
                "PivotFieldSelectItemsMenuItem_Click",
                "PivotFieldLabelFilterMenuItem_Click",
                "PivotFieldValueFilterMenuItem_Click",
                "PivotFieldClearFilterMenuItem_Click",
                "PivotFieldValueSettingsMenuItem_Click"
            ]);

        document
            .Descendants(presentation + "MenuItem")
            .Where(item => item.Attribute("Click")?.Value == "PivotFieldSortAscendingMenuItem_Click")
            .Should()
            .AllSatisfy(item => item.Attribute(local + "RibbonTooltip.KeyTip")?.Value.Should().NotBeNullOrWhiteSpace());
    }

    [Fact]
    public void PivotTableValueFieldSettings_UsesExcelStyleDialog()
    {
        var mainWindowSource = ReadPivotCommandSource();
        var dialogXaml = XamlLocalizationTestHelper.LoadLocalizedXaml("PivotValueFieldSettingsDialog.xaml");
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

        mainWindowSource.Should().Contain("new PivotValueFieldSettingsDialog(current, headers)");
        mainWindowSource.Should().NotContain("Value Field Settings: name,function,show-values-as");
        var plannerSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "PivotValueFieldSettingsDialogPlanner.cs"));
        var dialogSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "PivotValueFieldSettingsDialog.xaml.cs"));
        var expectedShowValuesAsKeys = new[]
        {
            "PivotValueFieldSettings_ShowPercentOfGrandTotal",
            "PivotValueFieldSettings_ShowPercentOfRowTotal",
            "PivotValueFieldSettings_ShowPercentOfColumnTotal",
            "PivotValueFieldSettings_ShowRunningTotalIn",
            "PivotValueFieldSettings_ShowDifferenceFrom",
            "PivotValueFieldSettings_ShowRankSmallest"
        };
        foreach (var key in expectedShowValuesAsKeys)
            plannerSource.Should().Contain($"UiText.Get(\"{key}\")");
        PivotValueFieldSettingsDialogPlanner.ShowValuesAsOptions
            .Select(option => option.Label)
            .Should()
            .Contain(expectedShowValuesAsKeys.Select(UiText.Get));
        dialogSource.Should().Contain("BaseFieldBox");
        dialogSource.Should().Contain("BaseItemBox");
        dialogSource.Should().Contain("NumberFormatPresetBox");
        dialogSource.Should().Contain("NumberFormatPresets");
        dialogSource.Should().Contain("NumberFormatCode");

        dialogXaml
            .Descendants(presentation + "TabItem")
            .Select(tab => LocalizedAttribute(tab, "Header")?.Replace("_", "", StringComparison.Ordinal))
            .Should()
            .Contain(["Summarize Values By", "Show Values As", "Number Format"]);

        dialogXaml
            .Descendants()
            .Select(element => element.Attribute(xaml + "Name")?.Value)
            .Should()
            .Contain([
                "CustomNameBox",
                "SummaryFunctionBox",
                "ShowValuesAsBox",
                "BaseFieldBox",
                "BaseItemBox",
                "NumberFormatPresetBox",
                "NumberFormatBox",
                "NumberFormatCodeBox"
            ]);
    }

    [Fact]
    public void PivotTableFieldListPane_SupportsDragDropReordering()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
        var source = ReadPivotCommandSource();
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var fieldLists = document
            .Descendants(presentation + "ListBox")
            .Where(list => (list.Attribute(xaml + "Name")?.Value ?? "").StartsWith("Pivot", StringComparison.Ordinal))
            .ToList();

        fieldLists.Should().NotBeEmpty();
        fieldLists.Should().AllSatisfy(list =>
        {
            list.Attribute("AllowDrop")?.Value.Should().Be("True");
            list.Attribute("PreviewMouseMove")?.Value.Should().Be("PivotFieldList_PreviewMouseMove");
            list.Attribute("Drop")?.Value.Should().Be("PivotFieldList_Drop");
        });

        source.Should().Contain("PivotFieldList_PreviewMouseMove");
        source.Should().Contain("PivotFieldList_Drop");
        source.Should().Contain("MovePivotFieldToZone");
    }

    [Fact]
    public void PivotTableAvailableFields_ExposeExcelStyleCheckboxToggles()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
        var source = ReadPivotCommandSource();
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var availableList = document
            .Descendants(presentation + "ListBox")
            .Single(list => list.Attribute(xaml + "Name")?.Value == "PivotAvailableFieldsList");

        availableList
            .Descendants(presentation + "CheckBox")
            .Single()
            .Attribute("Click")?.Value
            .Should()
            .Be("PivotAvailableFieldCheckBox_Click");

        source.Should().Contain("PivotFieldListItem");
        source.Should().Contain("PivotAvailableFieldCheckBox_Click");
        source.Should().Contain("TogglePivotAvailableField");
    }

    [Fact]
    public void PivotTableSelectItems_UsesCheckboxFilterDialog()
    {
        var mainWindowSource = ReadPivotCommandSource();
        var dialogXaml = XDocument.Load(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "PivotFieldFilterDialog.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

        mainWindowSource.Should().Contain("new PivotFieldFilterDialog");
        mainWindowSource.Should().NotContain("PivotTable item filter: values separated by comma or semicolon");

        dialogXaml
            .Descendants()
            .Select(element => element.Attribute(xaml + "Name")?.Value)
            .Should()
            .Contain(["FilterSearchBox", "SelectAllCheckBox", "FilterItemsList"]);

        dialogXaml
            .Descendants(presentation + "CheckBox")
            .Where(item => item.Attribute(xaml + "Name")?.Value == "SelectAllCheckBox")
            .Should()
            .ContainSingle();
    }

    [Fact]
    public void PivotTableRuleFilters_UseDialogChrome()
    {
        var mainWindowSource = ReadPivotCommandSource();
        var labelDialog = XDocument.Load(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "PivotLabelFilterDialog.xaml"));
        var valueDialog = XDocument.Load(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "PivotValueFilterDialog.xaml"));
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

        mainWindowSource.Should().Contain("new PivotLabelFilterDialog");
        mainWindowSource.Should().Contain("new PivotValueFilterDialog");
        mainWindowSource.Should().NotContain("Label Filter: equals:text");
        mainWindowSource.Should().NotContain("Value Filter: top:n");

        labelDialog.Descendants().Select(element => element.Attribute(xaml + "Name")?.Value)
            .Should().Contain(["LabelFilterKindBox", "LabelFilterValueBox", "LabelFilterValue2Box"]);
        File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "PivotLabelFilterDialog.xaml.cs"))
            .Should()
            .Contain("PivotLabelFilterKind.Between")
            .And.Contain("PivotLabelFilterKind.GreaterThan")
            .And.Contain("PivotLabelFilterKind.LessThan");
        valueDialog.Descendants().Select(element => element.Attribute(xaml + "Name")?.Value)
            .Should().Contain(["ValueFilterKindBox", "ValueFilterValueBox", "ValueFilterValue2Box"]);
        File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "PivotValueFilterDialog.xaml.cs"))
            .Should()
            .Contain("PivotValueFilterKind.Between")
            .And.Contain("PivotValueFilterKind.NotBetween")
            .And.Contain("PivotValueFilterKind.AboveAverage")
            .And.Contain("PivotValueFilterKind.BelowAverage");
    }

    [Fact]
    public void PivotChartFieldButtons_RouteToPivotFieldMenus()
    {
        var source =
            File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml.cs")) +
            ReadPivotCommandSource();

        source.Should().Contain("SheetGrid.PivotChartFieldButtonRequested += OnPivotChartFieldButtonRequested");
        source.Should().Contain("OnPivotChartFieldButtonRequested");
        source.Should().Contain("CreatePivotFieldContextMenu");
        source.Should().Contain("PivotFieldSelectItemsMenuItem_Click");
        source.Should().Contain("PivotFieldLabelFilterMenuItem_Click");
        source.Should().Contain("PivotFieldValueFilterMenuItem_Click");
    }

    [Fact]
    public void SlicerTimelinePane_ExposesInteractivePivotFilters()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
        var source = ReadPivotCommandSource();
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var slicerTimelinePane = document
            .Descendants(presentation + "Border")
            .Single(element => element.Attribute(xaml + "Name")?.Value == "SlicerTimelinePane");

        LocalizedAttribute(slicerTimelinePane, "AutomationProperties.Name").Should().Be("Slicers and Timelines");

        document.Descendants(presentation + "ItemsControl")
            .Select(element => element.Attribute(xaml + "Name")?.Value)
            .Should()
            .Contain(["SlicerItemsControl", "TimelineItemsControl"]);

        source.Should().Contain("RefreshSlicerTimelinePane");
        source.Should().Contain("GetPivotSourceSheet");
        source.Should().Contain("AddSlicerCommand");
        source.Should().Contain("AddTimelineCommand");
        source.Should().Contain("SetSlicerSelectionCommand");
        source.Should().Contain("SetTimelineRangeCommand");
        source.Should().Contain("SlicerTileButton_Click");
        source.Should().Contain("TimelineApplyButton_Click");
    }

    [Fact]
    public void PivotTableContextualTabs_ExposeAnalyzeAndDesignCommands()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        XNamespace local = "clr-namespace:FreeX.App.Host";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var contextualTabs = document
            .Descendants(presentation + "TabItem")
            .Where(tab => tab.Attribute(xaml + "Name")?.Value is "PivotTableAnalyzeTab" or "PivotTableDesignTab")
            .ToList();

        contextualTabs.Select(tab => LocalizedAttribute(tab, "Header"))
            .Should()
            .BeEquivalentTo(["PivotTable Analyze", "Design"]);

        var clickHandlers = contextualTabs
            .Descendants(presentation + "Button")
            .Select(button => button.Attribute("Click")?.Value)
            .Where(click => click is not null)
            .ToHashSet(StringComparer.Ordinal);

        clickHandlers.Should().Contain([
            "PivotTableNameBtn_Click",
            "PivotTableOptionsBtn_Click",
            "PivotTableClearBtn_Click",
            "PivotTableSelectBtn_Click",
            "PivotTableMoveBtn_Click",
            "PivotFieldListBtn_Click",
            "RefreshPivotTableBtn_Click",
            "PivotTableShowDetailsBtn_Click",
            "PivotChartBtn_Click",
            "PivotChartChangeTypeBtn_Click",
            "PivotChartOptionsBtn_Click",
            "PivotInsertSlicerBtn_Click",
            "PivotInsertTimelineBtn_Click",
            "PivotGrandTotalsBtn_Click",
            "PivotSubtotalsBtn_Click",
            "PivotReportLayoutBtn_Click",
            "PivotBlankRowsBtn_Click",
            "PivotRowHeadersBtn_Click",
            "PivotColumnHeadersBtn_Click",
            "PivotBandedRowsBtn_Click",
            "PivotBandedColumnsBtn_Click",
            "PivotStyleGalleryBtn_Click"
        ]);

        contextualTabs
            .Descendants(presentation + "Button")
            .Should()
            .AllSatisfy(button => button.Attribute(local + "RibbonTooltip.KeyTip")?.Value.Should().NotBeNullOrWhiteSpace());
    }

    [Fact]
    public void PivotTableContextualLayoutCommands_RouteThroughUndoableOptionsCommand()
    {
        var source = ReadPivotCommandSource();

        source.Should().Contain("ApplyPivotOptions(");
        source.Should().Contain("new ConfigurePivotTableOptionsCommand");
        source.Should().NotContain("PivotTableRefreshService.Refresh(_workbook, sheet, pivotTable);");
    }

    [Fact]
    public void PivotTableContextualLayoutCommands_PreserveCompactIndentWhenUsingOptionWrapper()
    {
        var source = ReadPivotCommandSource();

        source.Should().Contain("int? compactRowLabelIndent = null");
        source.Should().Contain("bool? printTitles = null");
        source.Should().Contain("bool? printExpandCollapseButtons = null");
        source.Should().Contain("bool updateAltText = false");
        source.Should().Contain("compactRowLabelIndent,");
        source.Should().Contain("updateAltText: true");
    }

    [Fact]
    public void PivotTableChangeDataSource_RoutesThroughUndoableSourceCommand()
    {
        var source = ReadPivotCommandSource();

        source.Should().Contain("new ChangePivotTableSourceCommand");
        source.Should().Contain("TryParseWorkbookRange");
        source.Should().NotContain("Rebinding a loaded PivotTable cache to a different source range is still tracked as a parity gap.");
    }
}
