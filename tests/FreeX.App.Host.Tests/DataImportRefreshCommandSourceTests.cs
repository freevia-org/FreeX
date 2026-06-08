using FluentAssertions;
using FreeX.App.Host;

using static FreeX.App.Host.Tests.LocalizedXamlTestSupport;

namespace FreeX.App.Host.Tests;

public sealed class DataImportRefreshCommandSourceTests
{
    [Fact]
    public void GetDataRibbonButton_AdvertisesSupportedLocalImportFormatsAndAutomationMetadata()
    {
        var xaml = ReadMainWindowXaml();
        var button = xaml.ExtractButtonElementByInvariantCommandName("Get Data", "Click=\"GetDataBtn_Click\"");

        button.Should().Contain("AutomationProperties.AutomationId=\"DataGetDataButton\"");
        button.ShouldContainLocalizedAttribute("AutomationProperties.Name", "Get Data");
        button.ShouldContainLocalizedAttribute(
            "AutomationProperties.HelpText",
            UiText.Get("MainWindow_TooltipDescription_ImportDataFromALocalCSVFileDatabaseWebAndPowerQueryConnectorsAreExcluded"));
        button.ShouldContainLocalizedAttribute(
            "local:RibbonTooltip.Description",
            UiText.Get("MainWindow_TooltipDescription_ImportDataFromALocalCSVFileDatabaseWebAndPowerQueryConnectorsAreExcluded"));
        button.Should().Contain("local:RibbonTooltip.KeyTip=\"D\"");
        button.Should().NotContain("IsEnabled=\"False\"");

        var helpText = UiText.Get("MainWindow_TooltipDescription_ImportDataFromALocalCSVFileDatabaseWebAndPowerQueryConnectorsAreExcluded");
        helpText.Should().ContainAll("local CSV file", "text/TSV/TAB", "SpreadsheetML XML", "Power Query connectors are excluded");

        var dataCommandsSource = ReadHostSourceFile("MainWindow.DataCommands.cs");
        dataCommandsSource.Should().Contain("\".csv\", \".txt\", \".tsv\", \".tab\", \".xml\"");
        dataCommandsSource.Should().Contain("FileDialogFilterBuilder.BuildOpenFilter(adapters)");
        dataCommandsSource.Should().Contain("FileDialogFilterBuilder.FindOpenAdapter(adapters, ext, out var format)");
        dataCommandsSource.Should().Contain("await Task.Run(() =>");
    }

    [Fact]
    public void RefreshAllRibbonAndQatCommands_AreLiveAlwaysEnabledRefreshAffordances()
    {
        var xaml = ReadMainWindowXaml();
        var button = xaml.ExtractButtonElementByInvariantCommandName("Refresh All", "Click=\"RefreshAllBtn_Click\"");

        button.Should().Contain("AutomationProperties.AutomationId=\"DataRefreshAllButton\"");
        button.ShouldContainLocalizedAttribute("AutomationProperties.Name", "Refresh All");
        button.ShouldContainLocalizedAttribute(
            "AutomationProperties.HelpText",
            UiText.Get("MainWindow_TooltipDescription_RecalculateFormulasAndRefreshFreeXManagedWorkbookDataExternalDataConnect_ECF2806B"));
        button.Should().Contain("local:RibbonTooltip.KeyTip=\"FA\"");
        button.Should().NotContain("IsEnabled=\"False\"");

        QuickAccessCommandStateResolver.GetAvailability(QuickAccessToolbarCommandIds.RefreshAll)
            .Should()
            .Be(QuickAccessCommandAvailability.Always);
        QuickAccessToolbarCatalog.TryGet(QuickAccessToolbarCommandIds.RefreshAll, out var qatCommand)
            .Should()
            .BeTrue();
        qatCommand.AutomationId.Should().Be("RefreshAllQatBtn");
        qatCommand.DescriptionResourceKey.Should()
            .Be("MainWindow_TooltipDescription_RecalculateFormulasAndRefreshFreeXManagedWorkbookDataExternalDataConnect_ECF2806B");

        var dataCommandsSource = ReadHostSourceFile("MainWindow.DataCommands.cs");
        dataCommandsSource.Should().Contain("private void RefreshAllBtn_Click(object sender, RoutedEventArgs e) => CalcNowBtn_Click(sender, e);");
    }

    [Fact]
    public void QueriesAndConnectionsGroup_SurfacesOnlyLiveRefreshCommand()
    {
        var catalog = RibbonXamlCatalogSnapshotReader.ReadMainWindow();
        var dataTab = catalog.FindTab("Data");
        dataTab.Should().NotBeNull();
        var group = dataTab!.FindGroup("Queries & Connections");
        group.Should().NotBeNull();

        group!.Commands.Select(command => command.Title).Should().Equal("Refresh All");

        var xaml = ReadMainWindowXaml();
        xaml.ShouldContainLocalizedAttribute("Text", "Queries &amp; Connections");
        xaml.Should().NotContain("local:RibbonMetadata.CommandName=\"Queries &amp; Connections\"");
        xaml.Should().NotContain("local:RibbonMetadata.CommandName=\"From Text/CSV\"");
        xaml.Should().NotContain("local:RibbonMetadata.CommandName=\"Workbook Connections\"");
    }
}
