using System.Windows.Input;
using System.Xml.Linq;
using System.IO;
using FluentAssertions;
using FreeX.App.Host;
using FreeX.App.Presentation.PivotUI;

namespace FreeX.App.Host.Tests;

public sealed partial class MainWindowXamlKeyTipTests
{
    [Fact]
    public void PivotTableShowDetailsGesture_IsAttemptedBeforeDoubleClickEdit()
    {
        var source =
            DialogSourceTestSupport.ReadHostSources("MainWindow.Selection.cs") +
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

        handlerSource.Should().Contain("PivotApplication.PlanShowDetails(_currentSheetId, SheetGrid.SelectedRange)");
        handlerSource.Should().Contain("ApplyPivotApplicationPlan(plan, title)");
        handlerSource.Should().NotContain("new DrillDownPivotTableCommand(");
        handlerSource.Should().NotContain("new AddSheetCommand");
        handlerSource.Should().NotContain("_workbook.Sheets.LastOrDefault()");
        handlerSource.Should().NotContain("PivotTableRefreshService.Refresh");
    }

    [Fact]
    public void PivotTableFieldListPane_HasExcelLikeZonesAndCommands()
    {
        var document = DialogSourceTestSupport.LoadHostXamlDocument("MainWindow.xaml");
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
        var document = DialogSourceTestSupport.LoadHostXamlDocument("MainWindow.xaml");
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
        var document = DialogSourceTestSupport.LoadHostXamlDocument("MainWindow.xaml");
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var removeButton = document
            .Descendants(presentation + "Button")
            .Single(button => button.Attribute("Click")?.Value == "PivotFieldRemoveBtn_Click");

        LocalizedAttribute(removeButton, "Content").Should().Be("_Remove");
    }

    [Fact]
    public void PivotTableFieldListPane_UsesCompactSearchRowAndGivesBucketsFlexibleSpace()
    {
        var document = DialogSourceTestSupport.LoadHostXamlDocument("MainWindow.xaml");
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var paneGrid = document
            .Descendants(presentation + "Border")
            .Single(element => element.Attribute(xaml + "Name")?.Value == "PivotFieldListPane")
            .Elements(presentation + "Grid")
            .Single();
        var rowHeights = paneGrid
            .Element(presentation + "Grid.RowDefinitions")!
            .Elements(presentation + "RowDefinition")
            .Select(row => row.Attribute("Height")?.Value ?? "Auto")
            .ToArray();

        rowHeights.Should().Equal("Auto", "Auto", "Auto", "0.85*", "Auto", "1.25*");

        var searchBox = document
            .Descendants(presentation + "TextBox")
            .Single(element => element.Attribute(xaml + "Name")?.Value == "PivotFieldListSearchBox");
        var availableFieldsList = document
            .Descendants(presentation + "ListBox")
            .Single(element => element.Attribute(xaml + "Name")?.Value == "PivotAvailableFieldsList");

        searchBox.Attribute("Margin")?.Value.Should().Be("0,0,0,4");
        availableFieldsList.Attribute("MinHeight")?.Value.Should().Be("96");
    }

    [Fact]
    public void PivotTableFieldListPane_RoutesThroughLayoutCommand()
    {
        var hostSource = ReadPivotCommandSource();
        var plannerSource = DialogSourceTestSupport.ReadPresentationSources(
            "PivotUI",
            "PivotApplicationSession.cs");

        hostSource.Should().Contain("RefreshPivotFieldListPane()");
        hostSource.Should().Contain("PivotApplication.PlanLayout(");
        hostSource.Should().Contain("PivotFieldToRowsBtn_Click");
        hostSource.Should().Contain("PivotFieldListCloseBtn_Click");
        plannerSource.Should().Contain("new ConfigurePivotTableLayoutCommand(");
    }

    [Fact]
    public void PivotTableFieldListPane_ExposesFieldDropdownCommands()
    {
        // The PivotTable field-area context menus are now single-sourced through the neutral
        // PivotFieldContextMenuPlanner (rendered at runtime via PivotFieldList_Loaded) instead of five
        // duplicated XAML ContextMenus. Assert the planner still exposes the sort/filter/settings commands and
        // that the field lists wire the runtime builder.
        var xamlSource = DialogSourceTestSupport.ReadHostSources("MainWindow.xaml");
        var contextMenuSource = DialogSourceTestSupport.ReadHostSources("MainWindow.ContextMenus.cs");

        var actions = PivotFieldContextMenuPlanner.BuildPivotFieldCommands(includeRemove: false)
            .Where(command => !command.IsSeparator)
            .Select(command => command.Action)
            .ToList();

        actions.Should().Contain([
            PivotFieldContextMenuAction.SortAscending,
            PivotFieldContextMenuAction.SortDescending,
            PivotFieldContextMenuAction.SelectItems,
            PivotFieldContextMenuAction.LabelFilter,
            PivotFieldContextMenuAction.ValueFilter,
            PivotFieldContextMenuAction.ClearFilter,
            PivotFieldContextMenuAction.ValueFieldSettings
        ]);

        PivotFieldContextMenuPlanner.BuildPivotFieldCommands(includeRemove: false)
            .Where(command => !command.IsSeparator)
            .Should()
            .AllSatisfy(command => command.KeyTip.Should().NotBeNullOrWhiteSpace());

        xamlSource.Should().Contain("Loaded=\"PivotFieldList_Loaded\"");
        contextMenuSource.Should().Contain("PivotFieldSortAscendingMenuItem_Click");
        contextMenuSource.Should().Contain("PivotFieldSelectItemsMenuItem_Click");
        contextMenuSource.Should().Contain("PivotFieldValueSettingsMenuItem_Click");
    }

    [Fact]
    public void PivotTableValueFieldSettings_UsesExcelStyleDialog()
    {
        var mainWindowSource = ReadPivotCommandSource();
        var dialogXaml = XamlLocalizationTestHelper.LoadLocalizedXaml("PivotValueFieldSettingsDialog.xaml");
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

        mainWindowSource.Should().Contain("new PivotValueFieldSettingsDialog(current, context.Headers)");
        mainWindowSource.Should().NotContain("Value Field Settings: name,function,show-values-as");
        var hostPlannerPath = Path.Combine(
            WorkspaceFileLocator.FindWorkspaceRoot(),
            "src",
            "FreeX.App.Host",
            "PivotValueFieldSettingsDialogPlanner.cs");
        var presentationSource = DialogSourceTestSupport.ReadPresentationSources("PivotUI", "PivotValueFieldPlanner.cs");
        var dialogSource = DialogSourceTestSupport.ReadHostSources("PivotValueFieldSettingsDialog.xaml.cs");
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
        {
            presentationSource.Should().Contain($"\"{key}\"");
            dialogSource.Should().NotContain($"\"{key}\"");
        }

        presentationSource.Should().Contain("PivotValueFieldOption<PivotShowValuesAs>");
        presentationSource.Should().Contain("PivotValueFieldValidationErrorPlan");
        presentationSource.Should().Contain("PivotValueNumberFormatPreset");
        presentationSource.Should().Contain("NumberFormatPresets");
        presentationSource.Should().Contain("\"PivotValueFieldSettings_NumberFormatCurrency\"");
        presentationSource.Should().Contain("DefaultCustomNumberFormatId");
        presentationSource.Should().Contain("\"PivotValueFieldSettings_SelectBaseFieldMessage\"");
        presentationSource.Should().Contain("\"PivotValueFieldSettings_EnterBaseItemMessage\"");
        File.Exists(hostPlannerPath).Should().BeFalse();
        presentationSource.Should().Contain("GetShowValuesAsOptions(");
        presentationSource.Should().Contain("GetNumberFormatPresets(");
        presentationSource.Should().Contain("ResourceKeyTextResolver text");
        dialogSource.Should().Contain("WpfResourceKeyTextResolver.Instance");
        dialogSource.Should().NotContain("\"PivotValueFieldSettings_SelectBaseFieldMessage\"");
        dialogSource.Should().NotContain("\"PivotValueFieldSettings_EnterBaseItemMessage\"");
        dialogSource.Should().NotContain("\"PivotValueFieldSettings_NumberFormatCurrency\"");
        PivotValueFieldPlanner.ShowValuesAsOptions
            .Select(option => option.ResourceKey)
            .Should()
            .Contain(expectedShowValuesAsKeys);
        PivotValueFieldPlanner.GetShowValuesAsOptions(new ResourceKeyTextResolver(UiText.Get, UiText.Format))
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
        var document = DialogSourceTestSupport.LoadHostXamlDocument("MainWindow.xaml");
        var source = ReadPivotCommandSource();
        var xamlSource = DialogSourceTestSupport.ReadHostSources("MainWindow.xaml");
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
            list.Attribute("PreviewMouseRightButtonDown")?.Value.Should().Be("PivotFieldList_PreviewMouseRightButtonDown");
            list.Attribute("GiveFeedback")?.Value.Should().Be("PivotFieldList_GiveFeedback");
            list.Attribute("DragOver")?.Value.Should().Be("PivotFieldList_DragOver");
            list.Attribute("Drop")?.Value.Should().Be("PivotFieldList_Drop");
        });

        source.Should().Contain("PivotFieldList_PreviewMouseMove");
        source.Should().Contain("GetPivotFieldDragCaption(list, e.OriginalSource)");
        source.Should().Contain("PivotFieldDragPayload");
        source.Should().Contain("GetPivotFieldDropInsertIndex");
        source.Should().Contain("GetDisplayedOrCurrentPivotLayout");
        source.Should().Contain("PivotFieldList_Drop");
        source.Should().Contain("MovePivotFieldToZone");
        source.Should().Contain("PivotFieldRemoveDropZone_DragOver");
        source.Should().Contain("PivotFieldListRemoveZone_DragOver");
        source.Should().Contain("Mouse.SetCursor(Cursors.No)");
        source.Should().Contain("MovePivotFieldToZone(caption, PivotFieldDropZone.Available");
        xamlSource.Should().Contain("DragOver=\"PivotFieldRemoveDropZone_DragOver\"");
        xamlSource.Should().Contain("DragOver=\"PivotFieldListRemoveZone_DragOver\"");
    }

    [Fact]
    public void PivotTableFieldListPane_BucketContextMenusExposeRemoveField()
    {
        // The four bucket lists (Filters/Columns/Rows/Values) share one runtime-built menu that includes the
        // trailing "Remove" command, while the available-fields list omits it. Assert the planner carries that
        // distinction and routes Remove through the existing handler.
        var contextMenuSource = DialogSourceTestSupport.ReadHostSources("MainWindow.ContextMenus.cs");

        var bucketCommands = PivotFieldContextMenuPlanner.BuildPivotFieldCommands(includeRemove: true)
            .Where(command => !command.IsSeparator)
            .ToList();
        var removeCommand = bucketCommands.Single(command => command.Action == PivotFieldContextMenuAction.Remove);
        removeCommand.ResourceKey.Should().Be("MainWindow_Content_Remove");

        PivotFieldContextMenuPlanner.BuildPivotFieldCommands(includeRemove: false)
            .Should()
            .NotContain(command => command.Action == PivotFieldContextMenuAction.Remove);

        // The available-fields list is the only one that omits Remove; the bucket lists include it.
        contextMenuSource.Should().Contain("ReferenceEquals(list, PivotAvailableFieldsList)");
        contextMenuSource.Should().Contain("PivotFieldContextMenuAction.Remove => PivotFieldRemoveBtn_Click");
    }

    [Fact]
    public void PivotTableAvailableFields_ExposeExcelStyleCheckboxToggles()
    {
        var document = DialogSourceTestSupport.LoadHostXamlDocument("MainWindow.xaml");
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

        source.Should().Contain("PivotAvailableFieldItemModel");
        source.Should().Contain("PivotFieldListPaneBuilder.BuildAvailableFields");
        source.Should().Contain("PivotAvailableFieldCheckBox_Click");
        source.Should().Contain("TogglePivotAvailableField");
    }

    [Fact]
    public void PivotTableSelectItems_UsesCheckboxFilterDialog()
    {
        var mainWindowSource = ReadPivotCommandSource();
        var dialogXaml = DialogSourceTestSupport.LoadHostXamlDocument("PivotFieldFilterDialog.xaml");
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
        var labelDialog = DialogSourceTestSupport.LoadHostXamlDocument("PivotLabelFilterDialog.xaml");
        var valueDialog = DialogSourceTestSupport.LoadHostXamlDocument("PivotValueFilterDialog.xaml");
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

        mainWindowSource.Should().Contain("new PivotLabelFilterDialog");
        mainWindowSource.Should().Contain("new PivotValueFilterDialog");
        mainWindowSource.Should().NotContain("Label Filter: equals:text");
        mainWindowSource.Should().NotContain("Value Filter: top:n");

        labelDialog.Descendants().Select(element => element.Attribute(xaml + "Name")?.Value)
            .Should().Contain(["LabelFilterKindBox", "LabelFilterValueBox", "LabelFilterValue2Box"]);
        DialogSourceTestSupport.ReadHostSources("PivotLabelFilterDialog.xaml.cs")
            .Should()
            .Contain("PivotFieldFilterPlanner.LabelFilterKinds")
            .And.Contain("PivotFieldFilterPlanner.LabelKindNeedsSecondValue")
            .And.Contain("PivotFieldFilterPlanner.TryCreateLabelFilterWithValidationError");
        valueDialog.Descendants().Select(element => element.Attribute(xaml + "Name")?.Value)
            .Should().Contain(["ValueFilterKindBox", "ValueFilterValueBox", "ValueFilterValue2Box"]);
        DialogSourceTestSupport.ReadHostSources("PivotValueFilterDialog.xaml.cs")
            .Should()
            .Contain("PivotFieldFilterPlanner.ValueFilterKinds")
            .And.Contain("PivotFieldFilterPlanner.ValueKindNeedsPrimaryInput")
            .And.Contain("PivotFieldFilterPlanner.ValueKindNeedsSecondValue");
    }

    [Fact]
    public void PivotChartFieldButtons_RouteToPivotFieldMenus()
    {
        var source =
            DialogSourceTestSupport.ReadHostSources("MainWindow.xaml.cs") +
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
        var document = DialogSourceTestSupport.LoadHostXamlDocument("MainWindow.xaml");
        var xamlSource = DialogSourceTestSupport.ReadHostSources("MainWindow.xaml");
        var hostSource = ReadPivotCommandSource();
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

        hostSource.Should().Contain("RefreshSlicerTimelinePane");
        hostSource.Should().Contain("PivotApplication.ReadSourceHeaders(");
        hostSource.Should().Contain("PivotApplication.PlanInsertSlicer(");
        hostSource.Should().Contain("PivotApplication.PlanInsertTimeline(");
        hostSource.Should().Contain("PivotApplication.PlanSlicerSelection(");
        hostSource.Should().Contain("PivotApplication.PlanTimelineRange(");
        hostSource.Should().Contain("SlicerTileButton_Click");
        hostSource.Should().Contain("TimelineApplyButton_Click");
        hostSource.Should().Contain("new SlicerTimelineSourceSession(_workbook)");
        hostSource.Should().Contain("Select(sourceSession.BuildSlicerPaneItem)");
        xamlSource.Should().Contain("Binding=\"{Binding HasActiveFilter}\"");
        xamlSource.Should().Contain("IsEnabled=\"{Binding HasActiveFilter}\"");
        xamlSource.Should().Contain("Binding=\"{Binding IsSelected}\"");
    }

    [Fact]
    public void PivotTableContextualLayoutCommands_RouteThroughUndoableOptionsCommand()
    {
        var source = ReadPivotCommandSource();

        source.Should().Contain("ApplyPivotDesignOptions(");
        source.Should().Contain("PivotApplication.PlanDesignOptions(");
        source.Should().NotContain("new ConfigurePivotTableOptionsCommand");
        source.Should().NotContain("PivotTableRefreshService.Refresh(_workbook, sheet, pivotTable);");
    }

    [Fact]
    public void PivotTableContextualLayoutCommands_PreserveCompactIndentWhenUsingOptionWrapper()
    {
        var source = ReadPivotCommandSource();

        source.Should().Contain("PivotOptionsPlanner.CaptureDesignValues(pivotTable)");
        source.Should().Contain("showExpandCollapseButtons: !pivotTable.ShowExpandCollapseButtons");
        source.Should().Contain("ShowFieldHeaders = !pivotTable.ShowFieldHeaders");
        source.Should().Contain("PivotApplication.PlanDialogOptions(");
    }

    [Fact]
    public void PivotTableChangeDataSource_RoutesThroughUndoableSourceCommand()
    {
        var source = ReadPivotCommandSource();

        source.Should().Contain("PivotApplication.PlanChangeDataSource(target, dialog.Result.SourceRangeText)");
        source.Should().Contain("TryParseWorkbookRange");
        source.Should().NotContain("Rebinding a loaded PivotTable cache to a different source range is still tracked as a parity gap.");
    }
}
