namespace FreeX.App.Avalonia.Tests;

public sealed class PivotOptionsParitySourceTests
{
    [Fact]
    public void PivotOptions_WiresEveryWpfEditableValueThroughTheSharedCommand()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.PivotOptions.cs"));

        source.Should().Contain("PivotOptionsPlanner.CaptureDialogValues(pivot, cache)");
        source.Should().Contain("PivotOptionsPlanner.CreateDialogValues(");
        source.Should().Contain("PivotApplication.PlanDialogOptions(");
        source.Should().Contain("PivotStyleGalleryPlanner.GetStyleNames(values.StyleName)");
        source.Should().Contain("PivotOptionsPlanner.PageFieldLayouts");
        source.Should().Contain("PivotOptionsPlanner.MissingItemsLimits");
        source.Should().Contain("PivotOptionsPlanner.FindMissingItemsLimitIndex(values.MissingItemsLimit)");
        source.Should().Contain("TryParsePageWrap(pageWrapBox.Text");
        source.Should().Contain("AvaloniaCompactDialogChrome.ApplyGroupBox(groupBox, PivotDialogChromeStyle);");
        source.Should().Contain("AvaloniaDisplayOptionSpacingCompensation");
        source.Should().Contain("AvaloniaDisplayOptionTopInsetCompensation");
        source.Should().Contain("AvaloniaDisplayOptionBottomInsetCompensation");
        source.Should().NotContain("PivotOptionsPlanner.DisplayOption");
        source.Should().NotContain("presentation-only");
        source.Should().NotContain("only the\n    // nine totals");

        foreach (var automationId in new[]
                 {
                     "PivotOptionsPageFieldLayoutBox",
                     "PivotOptionsPageWrapBox",
                     "PivotOptionsEmptyCellsBox",
                     "PivotOptionsErrorValuesBox",
                     "PivotOptionsStyleBox",
                     "PivotOptionsMissingItemsLimitBox",
                     "PivotOptionsAltTextTitleBox",
                     "PivotOptionsAltTextDescriptionBox"
                 })
            source.Should().Contain($"\"{automationId}\"");

        foreach (var argument in new[]
                 {
                     "emptyValueText: emptyCellsBox.Text",
                     "refreshOnOpen: refreshOnOpenBox.IsChecked == true",
                     "saveSourceData: saveSourceDataBox.IsChecked == true",
                     "enableRefresh: enableRefreshBox.IsChecked == true",
                     "preserveSourceSortFilter: preserveSourceSortFilterBox.IsChecked == true",
                     "missingItemsLimit: PivotOptionsPlanner.MissingItemsLimitFromIndex(missingItemsLimitBox.SelectedIndex)",
                     "printTitles: printTitlesBox.IsChecked == true",
                     "printExpandCollapseButtons: printExpandCollapseBox.IsChecked == true",
                     "altTextTitle: altTextTitleBox.Text",
                     "altTextDescription: altTextDescriptionBox.Text",
                     "autofitColumnsOnUpdate: autofitColumnsBox.IsChecked == true",
                     "preserveFormattingOnUpdate: preserveFormattingBox.IsChecked == true",
                     "showFieldHeaders: fieldHeadersBox.IsChecked == true",
                     "showContextualTooltips: contextualTooltipsBox.IsChecked == true",
                     "showPropertiesInTooltips: propertiesInTooltipsBox.IsChecked == true",
                     "showClassicLayout: classicLayoutBox.IsChecked == true",
                     "showItemsWithNoDataOnRows: showItemsWithNoDataRowsBox.IsChecked == true",
                     "showItemsWithNoDataOnColumns: showItemsWithNoDataColumnsBox.IsChecked == true",
                     "errorValueText: errorValuesBox.Text",
                     "enableDrill: enableShowDetailsBox.IsChecked == true"
                 })
            source.Should().Contain(argument);
    }

    [Fact]
    public void PivotOptionsParityFixture_SeedsDisplayStyleOptionsLikeWpf()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.ParityCapture.cs"));

        source.Should().Contain("StyleName = PivotStyleGalleryPlanner.DefaultStyleName,");
        source.Should().Contain("ShowRowStripes = true,");
    }

    [Fact]
    public void PivotOptionsDisplay_UsesSharedDisplayValuesAndLocalizedWpfLabels()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.PivotOptions.cs"));

        foreach (var value in new[]
                 {
                     "values.ShowRowHeaders",
                     "values.ShowColumnHeaders",
                     "values.ShowFieldHeaders",
                     "values.ShowContextualTooltips",
                     "values.ShowPropertiesInTooltips",
                     "values.ShowClassicLayout",
                     "values.ShowItemsWithNoDataOnRows",
                     "values.ShowItemsWithNoDataOnColumns",
                     "values.ShowRowStripes",
                     "values.ShowColumnStripes",
                     "values.ShowExpandCollapseButtons"
                 })
            source.Should().Contain(value);

        foreach (var key in new[]
                 {
                     "PivotTableOptions_RowHeaders",
                     "PivotTableOptions_ColumnHeaders",
                     "PivotTableOptions_DisplayFieldCaptionsAndFilterDropDowns",
                     "PivotTableOptions_ShowContextualTooltips",
                     "PivotTableOptions_ShowPropertiesInTooltips",
                     "PivotTableOptions_ClassicPivotTableLayoutEnablesDraggingOfFieldsInTheGrid",
                     "PivotTableOptions_ShowItemsWithNoDataOnRows",
                     "PivotTableOptions_ShowItemsWithNoDataOnColumns",
                     "PivotTableOptions_BandedRows",
                     "PivotTableOptions_BandedColumns",
                     "PivotTableOptions_ShowExpandCollapseButtons"
                 })
            source.Should().Contain($"UiText.Get(\"{key}\")");
    }

    private static string RepoFile(params string[] parts) =>
        Path.Combine([TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx"), .. parts]);
}
