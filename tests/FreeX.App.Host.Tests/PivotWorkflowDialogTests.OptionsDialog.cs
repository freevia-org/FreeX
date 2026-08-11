using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FluentAssertions;
using FreeX.App.Presentation.PivotUI;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed partial class PivotWorkflowDialogTests
{
    [Fact]
    public void PivotOptionsPlanner_CreateDialogValues_CapturesModeledLayoutAndStyleSettings()
    {
        var result = PivotOptionsPlanner.CreateDialogValues(
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
            enableRefresh: false,
            preserveSourceSortFilter: false,
            missingItemsLimit: 42,
            showExpandCollapseButtons: false,
            autofitColumnsOnUpdate: false,
            preserveFormattingOnUpdate: false,
            showFieldHeaders: false,
            showContextualTooltips: false,
            showPropertiesInTooltips: false,
            showClassicLayout: true,
            mergeAndCenterLabels: true,
            pageOverThenDown: true,
            pageWrap: 4,
            compactRowLabelIndent: 3,
            enableDrill: false);

        result.Should().BeEquivalentTo(new
        {
            ShowRowGrandTotals = true,
            ShowColumnGrandTotals = false,
            ShowSubtotals = true,
            SubtotalPlacement = PivotSubtotalPlacement.Top,
            RepeatItemLabels = false,
            BlankLineAfterItems = true,
            StyleName = "PivotStyleMedium9",
            ShowRowHeaders = false,
            ShowColumnHeaders = true,
            ShowRowStripes = true,
            ShowColumnStripes = false,
            ReportLayout = PivotReportLayout.Outline,
            EmptyValueText = "N/A",
            ErrorValueText = (string?)null,
            RefreshOnOpen = true,
            SaveSourceData = false,
            EnableRefresh = false,
            PreserveSourceSortFilter = false,
            MissingItemsLimit = 1_048_576,
            ShowExpandCollapseButtons = false,
            AutofitColumnsOnUpdate = false,
            PreserveFormattingOnUpdate = false,
            ShowFieldHeaders = false,
            ShowContextualTooltips = false,
            ShowPropertiesInTooltips = false,
            ShowClassicLayout = true,
            MergeAndCenterLabels = true,
            PageOverThenDown = true,
            PageWrap = 4,
            CompactRowLabelIndent = 3,
            EnableDrill = false
        });
    }

    [Fact]
    public void PivotOptionsPlanner_CreateDialogValues_CapturesEmptyAndErrorValueText()
    {
        var result = PivotOptionsPlanner.CreateDialogValues(
            showRowGrandTotals: true,
            showColumnGrandTotals: true,
            showSubtotals: true,
            subtotalPlacement: PivotSubtotalPlacement.Bottom,
            repeatItemLabels: false,
            blankLineAfterItems: false,
            styleName: "PivotStyleLight16",
            showRowHeaders: true,
            showColumnHeaders: true,
            showRowStripes: false,
            showColumnStripes: false,
            reportLayout: PivotReportLayout.Tabular,
            emptyValueText: "  N/A  ",
            errorValueText: "  #VALUE!  ");

        result.EmptyValueText.Should().Be("N/A");
        result.ErrorValueText.Should().Be("#VALUE!");

        var blankResult = PivotOptionsPlanner.CreateDialogValues(
            showRowGrandTotals: true,
            showColumnGrandTotals: true,
            showSubtotals: true,
            subtotalPlacement: PivotSubtotalPlacement.Bottom,
            repeatItemLabels: false,
            blankLineAfterItems: false,
            styleName: "PivotStyleLight16",
            showRowHeaders: true,
            showColumnHeaders: true,
            showRowStripes: false,
            showColumnStripes: false,
            reportLayout: PivotReportLayout.Tabular,
            emptyValueText: " ",
            errorValueText: " \t ");

        blankResult.EmptyValueText.Should().BeNull();
        blankResult.ErrorValueText.Should().BeNull();
    }

    [Fact]
    public void PivotOptionsPlanner_CreateDialogValues_KeepsExistingPositionalOptionalOrder()
    {
        var result = PivotOptionsPlanner.CreateDialogValues(
            true,
            true,
            true,
            PivotSubtotalPlacement.Bottom,
            false,
            false,
            "PivotStyleLight16",
            true,
            true,
            false,
            false,
            PivotReportLayout.Tabular,
            "empty",
            true,
            false,
            false,
            false,
            0,
            true,
            true,
            "title",
            "description",
            2,
            false,
            false,
            false,
            false,
            false,
            false,
            true,
            true,
            true,
            true,
            true,
            7,
            "error");

        result.ErrorValueText.Should().Be("error");
        result.EnableDrill.Should().BeTrue();
    }

    [Fact]
    public void PivotOptionsPlanner_CaptureDialogValues_UsesConnectedCacheDataOptions()
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
            SaveData = false,
            EnableRefresh = false,
            PreserveSourceSortFilter = false,
            MissingItemsLimit = 0
        };

        PivotOptionsPlanner.CaptureDialogValues(pivotTable, cache)
            .Should()
            .Match<PivotOptionsDialogValues>(result =>
                result.RefreshOnOpen &&
                !result.SaveSourceData &&
                !result.EnableRefresh &&
                !result.PreserveSourceSortFilter &&
                result.MissingItemsLimit == 0);
    }

    [Fact]
    public void PivotTableOptionsDialog_UsesCanonicalPresentationResultDirectly()
    {
        var dialogSource = DialogSourceTestSupport.ReadHostSources("PivotTableOptionsDialog.cs");
        var commandSource = DialogSourceTestSupport.ReadHostSources("MainWindow.PivotDesignCommands.cs");
        var repoRoot = WorkspaceFileLocator.FindWorkspaceRoot();

        dialogSource.Should().Contain("public PivotOptionsDialogValues Result { get; private set; }");
        dialogSource.Should().Contain("Result = PivotOptionsPlanner.CaptureDialogValues(pivotTable, cache);");
        dialogSource.Should().Contain("Result = PivotOptionsPlanner.CreateDialogValues(");
        dialogSource.Should().NotContain("PivotTableOptionsDialogResult");
        commandSource.Should().Contain("ApplyPivotOptions(PivotTableModel pivotTable, PivotOptionsDialogValues values)");
        commandSource.Should().Contain("PivotApplication.PlanDialogOptions(");
        commandSource.Should().NotContain("PivotOptionsPlanner.CreateDialogValues(");
        File.Exists(Path.Combine(repoRoot, "src", "FreeX.App.Host", "PivotTableOptionsDialog.Result.cs"))
            .Should().BeFalse("the WPF dialog no longer needs a result projection partial");
        dialogSource.Should().Contain("PivotStyleGalleryPlanner.GetStyleNames(result.StyleName)");
        dialogSource.Should().Contain("PivotStyleGalleryPlanner.FindStyleIndex(styleNames, result.StyleName)");
        dialogSource.Should().Contain("PivotOptionsPlanner.TryParseCompactRowLabelIndent(_compactIndentBox.Text");
        dialogSource.Should().Contain("PivotOptionsPlanner.TryParsePageWrap(_pageWrapBox.Text");
    }

    [Fact]
    public void PivotOptionsPlanner_CaptureDialogValues_UsesCurrentPivotSettings()
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
            ErrorCaption = "(error)",
            ShowExpandCollapseButtons = false,
            PrintExpandCollapseButtons = true,
            AutofitColumnsOnUpdate = false,
            PreserveFormattingOnUpdate = false,
            ShowFieldHeaders = false,
            ShowContextualTooltips = false,
            ShowPropertiesInTooltips = false,
            ShowClassicLayout = true,
            MergeAndCenterLabels = true,
            PageOverThenDown = true,
            PageWrap = 2,
            CompactRowLabelIndent = 5,
            EnableDrill = false
        };

        PivotOptionsPlanner.CaptureDialogValues(pivotTable)
            .Should()
            .BeEquivalentTo(new
            {
                ShowRowGrandTotals = false,
                ShowColumnGrandTotals = true,
                ShowSubtotals = true,
                SubtotalPlacement = PivotSubtotalPlacement.Top,
                RepeatItemLabels = false,
                BlankLineAfterItems = true,
                StyleName = "PivotStyleDark4",
                ShowRowHeaders = true,
                ShowColumnHeaders = false,
                ShowRowStripes = true,
                ShowColumnStripes = true,
                ReportLayout = PivotReportLayout.Compact,
                EmptyValueText = "-",
                ErrorValueText = "(error)",
                PrintExpandCollapseButtons = true,
                ShowExpandCollapseButtons = false,
                AutofitColumnsOnUpdate = false,
                PreserveFormattingOnUpdate = false,
                ShowFieldHeaders = false,
                ShowContextualTooltips = false,
                ShowPropertiesInTooltips = false,
                ShowClassicLayout = true,
                MergeAndCenterLabels = true,
                PageOverThenDown = true,
                PageWrap = 2,
                CompactRowLabelIndent = 5,
                EnableDrill = false
            });
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
            var styleBox = DialogSourceTestSupport.GetPrivateField<ComboBox>(dialog, "_styleBox");
            var styleNames = styleBox.Items.Cast<object>().Select(item => item.ToString()).ToList();

            styleNames.Should().Contain(["PivotStyleLight16", "PivotStyleMedium10", "PivotStyleDark7"]);
            styleNames.Should().HaveCountGreaterThan(12);
            styleBox.SelectedItem.Should().Be("PivotStyleMedium10");

            dialog.Close();
        });
    }

    [Fact]
    public void PivotStyleGalleryDialog_UsesPresentationPlannerForCatalogSelectionAndResult()
    {
        var source = DialogSourceTestSupport.ReadHostSources("PivotStyleGalleryDialog.cs");

        source.Should().Contain("public PivotStyleGalleryValues Result { get; private set; }");
        source.Should().Contain("PivotStyleGalleryPlanner.CreateResult(styleName)");
        source.Should().Contain("PivotStyleGalleryPlanner.GetStyleNames(styleName)");
        source.Should().Contain("PivotStyleGalleryPlanner.FindStyleIndex(styleNames, styleName)");
        source.Should().NotContain("PivotStyleCatalog");
    }

    [Fact]
    public void PivotStyleGalleryDialog_UsesCurrentStyleAsInitialSelectionAndPreservesCustomStyle()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = new PivotStyleGalleryDialog("CustomPivotStyle");
            var styleGallery = DialogSourceTestSupport.GetPrivateField<ListBox>(dialog, "_styleGallery");
            var styleNames = styleGallery.Items.Cast<object>().Select(item => item.ToString()).ToList();

            styleNames.Should().HaveCount(85);
            styleNames.Should().Contain("CustomPivotStyle");
            styleGallery.SelectedItem.Should().Be("CustomPivotStyle");

            dialog.Close();
        });
    }

    [Fact]
    public void PivotStyleGalleryDialog_LabelsStyleGalleryWithAccessKeyAndAutomationName()
    {
        var source = DialogSourceTestSupport.ReadHostSources("PivotStyleGalleryDialog.cs");

        source.Should().Contain("new Label { Content = UiText.Get(\"PivotStyleGallery_PivotTableStyle\"), Target = _styleGallery");
        source.Should().Contain("AutomationProperties.SetName(_styleGallery, UiText.Get(\"PivotStyleGallery_PivotTableStyleGallery\"));");
    }

    [Fact]
    public void PivotStyleGalleryDialog_CreateResult_NormalizesBlankStyleToDefault()
    {
        PivotStyleGalleryDialog.CreateResult("  PivotStyleDark28  ")
            .Should()
            .Be(new PivotStyleGalleryValues("PivotStyleDark28"));

        PivotStyleGalleryDialog.CreateResult("  ")
            .Should()
            .Be(new PivotStyleGalleryValues("PivotStyleLight16"));
    }

    [Fact]
    public void MainWindow_PivotStyleGalleryButton_OpensLightweightGalleryInsteadOfOptionsDialog()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.PivotDesignCommands.cs");
        var handlerSource = source[
            source.IndexOf("private void PivotStyleGalleryBtn_Click", StringComparison.Ordinal)..
            source.IndexOf("private void PivotRowHeadersBtn_Click", StringComparison.Ordinal)];

        handlerSource.Should().Contain("ShowPivotStyleGalleryDialog();");
        handlerSource.Should().NotContain("ShowPivotTableOptionsDialog();");
        source.Should().Contain("private void ShowPivotStyleGalleryDialog()");
        source.Should().Contain("new PivotStyleGalleryDialog(pivotTable.StyleName)");
        source.Should().Contain("StyleName = dialog.Result.StyleName");
    }

    [Fact]
    public void MainWindow_PivotStyleOptionButtons_PreserveCurrentStyleAndToggleOnlyTargetFlag()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.PivotDesignCommands.cs");

        AssertPivotStyleOptionHandler(source, "PivotRowHeadersBtn_Click", "!pivotTable.ShowRowHeaders");
        AssertPivotStyleOptionHandler(source, "PivotColumnHeadersBtn_Click", "!pivotTable.ShowColumnHeaders");
        AssertPivotStyleOptionHandler(source, "PivotBandedRowsBtn_Click", "!pivotTable.ShowRowStripes");
        AssertPivotStyleOptionHandler(source, "PivotBandedColumnsBtn_Click", "!pivotTable.ShowColumnStripes");
    }

    private static void AssertPivotStyleOptionHandler(string source, string handlerName, string toggledFlag)
    {
        var start = source.IndexOf($"private void {handlerName}", StringComparison.Ordinal);
        var end = source.IndexOf("    private void", start + 1, StringComparison.Ordinal);
        var handlerSource = source[start..end];

        handlerSource.Should().Contain("ApplyPivotDesignOptions(");
        handlerSource.Should().Contain("PivotOptionsPlanner.CaptureDesignValues(pivotTable)");
        handlerSource.Should().Contain(toggledFlag);
        handlerSource.Should().NotContain("PivotStyleLight16");
        handlerSource.Should().NotContain("PivotStyleMedium");
        handlerSource.Should().NotContain("PivotStyleDark");
    }

    [Fact]
    public void PivotTableOptionsDialog_UsesExcelStyleTabbedOptionShell()
    {
        var source = DialogSourceTestSupport.ReadHostSources("PivotTableOptionsDialog.cs");

        foreach (var content in new[]
        {
            "UiText.Get(\"PivotTableOptions_LayoutAndFormat\")",
            "UiText.Get(\"PivotTableOptions_TotalsAndFilters\")",
            "UiText.Get(\"PivotTableOptions_Display\")",
            "UiText.Get(\"PivotTableOptions_Data\")",
            "UiText.Get(\"PivotTableOptions_Printing\")",
            "UiText.Get(\"PivotTableOptions_AltText\")",
            "_emptyCellsBox",
            "_compactIndentBox",
            "_autofitColumnsBox",
            "_preserveFormattingBox",
            "_refreshOnOpenBox",
            "_enableRefreshBox",
            "_preserveSourceSortFilterBox",
            "_enableShowDetailsBox",
            "_missingItemsLimitBox",
            "_fieldHeadersBox",
            "_showExpandCollapseBox",
            "_printTitlesBox",
            "_printExpandCollapseBox",
            "_altTextTitleBox",
            "_altTextDescriptionBox",
            "Loaded += (_, _) => FocusInitialKeyboardTarget();",
            "private void FocusInitialKeyboardTarget()",
            "_reportLayoutBox.Focus();",
            "Keyboard.Focus(_reportLayoutBox);"
        })
            source.Should().Contain(content);
        source.Should().NotContain("Title and description metadata can be added in a future pass.");
    }

    [Fact]
    public void PivotTableOptionsDialog_DocksButtonRowBelowTabContent()
    {
        var source = DialogSourceTestSupport.ReadHostSources("PivotTableOptionsDialog.cs");
        var method = ReadClassSource(
            "PivotTableOptionsDialog.cs",
            "private DockPanel CreateContent()",
            "private StackPanel CreateLayoutAndFormatTab()");

        method.Should().Contain("var buttons = PivotDialogLayout.CreateButtonRow(Accept);");
        method.Should().Contain("DockPanel.SetDock(buttons, Dock.Bottom);");
        source.Should().Contain("stack.Children.Add(new Border { Height = 1.5 });");
        method.IndexOf("root.Children.Add(buttons);", StringComparison.Ordinal)
            .Should()
            .BeLessThan(method.IndexOf("root.Children.Add(_tabs);", StringComparison.Ordinal));
        source.Should().NotContain("DockPanel.SetDock(_tabs, Dock.Top);");
    }

    [Fact]
    public void PivotTableOptionsParityCapture_SupportsTargetedTabRefresh()
    {
        var source = DialogSourceTestSupport.ReadHostSources("ParityCapture.cs");

        source.Should().Contain("targetSurfaceId.StartsWith(\"dialog.PivotTableOptions.\", StringComparison.Ordinal)");
        source.Should().Contain("CaptureDialogTabs(results, \"dialog.PivotTableOptions\", outDir");
        source.Should().Contain("[\"LayoutAndFormat\", \"TotalsAndFilters\", \"Display\", \"Printing\", \"Data\", \"AltText\"]");
        source.Should().Contain("Targeted WPF parity capture only supports");
        source.Should().Contain("dialog.CreateTable");
        source.Should().Contain("dialog.PivotTableOptions");
        source.Should().Contain("the targeted Options tabs.");
    }

    [Fact]
    public void PivotTableOptionsDialog_ButtonRowKeepsNaturalHeightAtRuntime()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = new PivotTableOptionsDialog(new PivotTableModel { Name = "PivotTable1" });
            dialog.Show();
            try
            {
                dialog.UpdateLayout();
                var buttons = WpfTestTree.FindVisualDescendants<Button>(dialog).ToList();
                var ok = buttons.Single(button => button.IsDefault);
                var cancel = buttons.Single(button => button.IsCancel);

                ok.ActualHeight.Should().BeLessThan(40);
                cancel.ActualHeight.Should().BeLessThan(40);
                Math.Abs(ok.ActualHeight - cancel.ActualHeight).Should().BeLessThan(1);
                Math.Round(dialog.ActualHeight).Should().Be(PivotOptionsPlanner.LayoutAndFormatCaptureHeight);
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void PivotTableOptionsDialog_ExposesPrintingTab()
    {
        var source = ReadPivotWorkflowSource();

        source.Should().Contain("Header = UiText.Get(\"PivotTableOptions_Printing\")");
        source.Should().Contain("UiText.Get(\"PivotTableOptions_ShowExpandCollapseButtons\")");
        source.Should().Contain("UiText.Get(\"PivotTableOptions_SetPrintTitles\")");
        source.Should().Contain("UiText.Get(\"PivotTableOptions_PrintExpandCollapseButtonsWhenDisplayedOnPivotTable\")");
        source.Should().NotContain("Print titles and print expand/collapse buttons are not yet available.");
    }

    [Fact]
    public void PivotTableOptionsDialog_ExposesExcelLikeGroupsInsideTabs()
    {
        var source = ReadPivotWorkflowSource();

        foreach (var content in new[]
        {
            "UiText.Get(\"PivotTableOptions_LayoutSectionGroup\")",
            "UiText.Get(\"PivotTableOptions_FormatSectionGroup\")",
            "UiText.Get(\"PivotTableOptions_GrandTotalsGroup\")",
            "UiText.Get(\"PivotTableOptions_PivotTableStyleOptionsGroup\")",
            "UiText.Get(\"PivotTableOptions_DataOptionsGroup\")",
            "UiText.Get(\"PivotTableOptions_PrintOptionsGroup\")",
            "UiText.Get(\"PivotTableOptions_AltTextGroup\")",
            "UiText.Get(\"PivotTableOptions_PreserveSourceSortAndFilterSettings\")",
            "UiText.Get(\"PivotTableOptions_RetainItemsDeletedLabel\")",
            "UiText.Get(\"PivotTableOptions_DisplayFieldCaptionsAndFilterDropDowns\")",
            "UiText.Get(\"PivotTableOptions_ShowItemsWithNoDataOnRows\")",
            "UiText.Get(\"PivotTableOptions_ShowItemsWithNoDataOnColumns\")"
        })
            source.Should().Contain(content);

        source.Should().NotContain("Field list and buttons remain available");
    }

    [Fact]
    public void PivotTableOptionsDialog_ModelsPreserveSourceSortFilterOption()
    {
        var source = ReadPivotWorkflowSource();

        source.Should().Contain("private readonly CheckBox _preserveSourceSortFilterBox");
        source.Should().Contain("Content = UiText.Get(\"PivotTableOptions_PreserveSourceSortAndFilterSettings\")");
        source.Should().Contain("PreserveSourceSortFilter");
        source.Should().Contain("AddCheckBox(dataPanel, _preserveSourceSortFilterBox)");
        source.Should().NotContain("IsEnabled = false");
        source.Should().NotContain("changing this option is not modeled yet");
        source.Should().NotContain("new CheckBox { Content = \"Preserve source sort and _filter settings\"");
    }

    [Fact]
    public void PivotTableOptionsDialog_LabelsEditableOptionsWithAccessKeyTargets()
    {
        var source = ReadPivotWorkflowSource();

        foreach (var content in new[]
        {
            "AddLabeledControl(layoutPanel, UiText.Get(\"PivotTableOptions_ReportLayoutLabel\"), _reportLayoutBox",
            "AddLabeledControl(layoutPanel, UiText.Get(\"PivotTableOptions_CompactIndentLabel\"), _compactIndentBox",
            "AddLabeledControl(formatPanel, UiText.Get(\"PivotTableOptions_EmptyCellsLabel\"), _emptyCellsBox",
            "AddLabeledControl(formatPanel, UiText.Get(\"PivotTableOptions_ErrorValuesLabel\"), _errorValuesBox",
            "AddLabeledControl(dataPanel, UiText.Get(\"PivotTableOptions_RetainItemsDeletedLabel\"), _missingItemsLimitBox",
            "AddLabeledControl(filtersPanel, UiText.Get(\"PivotTableOptions_SubtotalPlacementLabel\"), _subtotalPlacementBox",
            "AddLabeledControl(stylePanel, UiText.Get(\"PivotTableOptions_PivotTableStyleLabel\"), _styleBox",
            "new Label",
            "Content = label",
            "Target = control"
        })
            source.Should().Contain(content);
    }

    [Fact]
    public void PivotTableOptionsDialogInvalidNumericOptions_ShowOwnedWarningAndRefocusBadInput()
    {
        var source = ReadClassSource(
            "PivotTableOptionsDialog.cs",
            "public sealed partial class PivotTableOptionsDialog",
            "");

        source.Should().Contain("if (!ValidateInputs())");
        source.Should().Contain("ShowInvalidInputWarning(UiText.Get(\"PivotTableOptions_EnterCompactIndent\"), _compactIndentBox);");
        source.Should().Contain("ShowInvalidInputWarning(UiText.Get(\"PivotTableOptions_EnterPageFieldsPerColumn\"), _pageWrapBox);");
        source.Should().Contain("_tabs.SelectedItem = _layoutTab;");
        source.Should().Contain("DialogFocus.ShowWarningAndFocus(this, message, Title, target);");
    }

    [Fact]
    public void PivotTableOptionsDialog_ExposesAccessKeysForModeledCheckboxes()
    {
        var source = ReadPivotWorkflowSource();

        foreach (var content in new[]
        {
            "Content = UiText.Get(\"PivotTableOptions_ShowRowGrandTotals\")",
            "Content = UiText.Get(\"PivotTableOptions_ShowColumnGrandTotals\")",
            "Content = UiText.Get(\"PivotTableOptions_ShowSubtotals\")",
            "Content = UiText.Get(\"PivotTableOptions_RepeatItemLabels\")",
            "Content = UiText.Get(\"PivotTableOptions_InsertBlankLineAfterEachItem\")",
            "Content = UiText.Get(\"PivotTableOptions_RowHeaders\")",
            "Content = UiText.Get(\"PivotTableOptions_ColumnHeaders\")",
            "Content = UiText.Get(\"PivotTableOptions_DisplayFieldCaptionsAndFilterDropDowns\")",
            "Content = UiText.Get(\"PivotTableOptions_ShowItemsWithNoDataOnRows\")",
            "Content = UiText.Get(\"PivotTableOptions_ShowItemsWithNoDataOnColumns\")",
            "Content = UiText.Get(\"PivotTableOptions_BandedRows\")",
            "Content = UiText.Get(\"PivotTableOptions_BandedColumns\")",
            "Content = UiText.Get(\"PivotTableOptions_AutofitColumnWidthsOnUpdate\")",
            "Content = UiText.Get(\"PivotTableOptions_PreserveCellFormattingOnUpdate\")",
            "Content = UiText.Get(\"PivotTableOptions_RefreshDataWhenOpeningTheFile\")",
            "Content = UiText.Get(\"PivotTableOptions_EnableRefresh\")",
            "Content = UiText.Get(\"PivotTableOptions_EnableShowDetails\")",
            "Content = UiText.Get(\"PivotTableOptions_ShowExpandCollapseButtons\")",
            "Content = UiText.Get(\"PivotTableOptions_SetPrintTitles\")",
            "Content = UiText.Get(\"PivotTableOptions_PrintExpandCollapseButtonsWhenDisplayedOnPivotTable\")"
        })
            source.Should().Contain(content);
    }

    [Fact]
    public void PivotTableOptionsDialog_DataTabAccessKeysAreUnique()
    {
        string[] dataTabLabels =
        [
            "_Refresh data when opening the file",
            "_Save source data with file",
            "_Enable refresh",
            "Enable Show De_tails",
            "Preserve source sort and _filter settings",
            "Retain items _deleted from the data source"
        ];

        var accessKeys = dataTabLabels
            .Select(label => char.ToUpperInvariant(label[label.IndexOf('_') + 1]))
            .ToList();

        accessKeys.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void PivotTableOptionsDialog_ResultIncludesPrintingAndAltText()
    {
        var result = PivotOptionsPlanner.CreateDialogValues(
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
            enableRefresh: false,
            missingItemsLimit: 0,
            compactRowLabelIndent: 6,
            showExpandCollapseButtons: false,
            autofitColumnsOnUpdate: false,
            preserveFormattingOnUpdate: false,
            showFieldHeaders: false,
            showContextualTooltips: false,
            showPropertiesInTooltips: false,
            showClassicLayout: true,
            mergeAndCenterLabels: true,
            showItemsWithNoDataOnRows: true,
            showItemsWithNoDataOnColumns: true,
            printTitles: true,
            printExpandCollapseButtons: true,
            altTextTitle: "  Sales pivot ",
            altTextDescription: " Quarterly sales summary ");

        result.ShowExpandCollapseButtons.Should().BeFalse();
        result.AutofitColumnsOnUpdate.Should().BeFalse();
        result.PreserveFormattingOnUpdate.Should().BeFalse();
        result.ShowFieldHeaders.Should().BeFalse();
        result.ShowContextualTooltips.Should().BeFalse();
        result.ShowPropertiesInTooltips.Should().BeFalse();
        result.ShowClassicLayout.Should().BeTrue();
        result.MergeAndCenterLabels.Should().BeTrue();
        result.ShowItemsWithNoDataOnRows.Should().BeTrue();
        result.ShowItemsWithNoDataOnColumns.Should().BeTrue();
        result.EnableRefresh.Should().BeFalse();
        result.MissingItemsLimit.Should().Be(0);
        result.PrintTitles.Should().BeTrue();
        result.PrintExpandCollapseButtons.Should().BeTrue();
        result.CompactRowLabelIndent.Should().Be(6);
        result.AltTextTitle.Should().Be("Sales pivot");
        result.AltTextDescription.Should().Be("Quarterly sales summary");
    }
}
