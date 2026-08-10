using FluentAssertions;
using FreeX.App.Presentation.Shell;

namespace FreeX.App.Presentation.Tests.Shell;

public sealed class FreeXAutomationIdCatalogTests
{
    [Fact]
    public void Catalog_PreservesCrossRendererAutomationContracts()
    {
        FreeXAutomationIdCatalog.ActivateSheetList.Should().Be("ActivateSheetList");
        FreeXAutomationIdCatalog.ActivateSheetOkButton.Should().Be("ActivateSheetOkButton");
        FreeXAutomationIdCatalog.ActivateSheetCancelButton.Should().Be("ActivateSheetCancelButton");
        FreeXAutomationIdCatalog.QuickAccessToolbarImportExportButton.Should().Be("QuickAccessToolbarImportExportButton");
        FreeXAutomationIdCatalog.QuickAccessToolbarImportCustomizationMenuItem
            .Should().Be("QuickAccessToolbarImportCustomizationMenuItem");
        FreeXAutomationIdCatalog.QuickAccessToolbarExportCustomizationMenuItem
            .Should().Be("QuickAccessToolbarExportCustomizationMenuItem");
        FreeXAutomationIdCatalog.MergeCellsContentWarningDialog.Should().Be("MergeCellsContentWarningDialog");
        FreeXAutomationIdCatalog.MergeCellsKeepFirstButton.Should().Be("MergeCellsKeepFirstButton");
        FreeXAutomationIdCatalog.MergeCellsConcatenateButton.Should().Be("MergeCellsConcatenateButton");
        FreeXAutomationIdCatalog.MergeCellsCancelButton.Should().Be("MergeCellsCancelButton");
        FreeXAutomationIdCatalog.WorkbookStatisticsSummary.Should().Be("WorkbookStatisticsSummary");
        FreeXAutomationIdCatalog.WorkbookStatisticsCopyButton.Should().Be("WorkbookStatisticsCopyButton");
    }

    [Fact]
    public void ActivateSheetRenderers_UseCatalogInsteadOfRawIds()
    {
        var wpf = ReadSource("src", "FreeX.App.Host", "ActivateSheetDialog.cs");
        var avalonia = ReadSource("src", "FreeX.App.Avalonia", "MainWindow.SheetTabPointer.cs");
        var paired = wpf + Environment.NewLine + avalonia;

        foreach (var member in new[]
                 {
                     "FreeXAutomationIdCatalog.ActivateSheetList",
                     "FreeXAutomationIdCatalog.ActivateSheetOkButton",
                     "FreeXAutomationIdCatalog.ActivateSheetCancelButton"
                 })
        {
            wpf.Should().Contain(member);
            avalonia.Should().Contain(member);
        }

        paired.Should().NotContain("\"ActivateSheetList\"");
        paired.Should().NotContain("\"ActivateSheetOkButton\"");
        paired.Should().NotContain("\"ActivateSheetCancelButton\"");
    }

    [Fact]
    public void QuickAccessImportExportRenderers_UseCatalogInsteadOfRawIds()
    {
        var wpfXaml = ReadSource("src", "FreeX.App.Host", "OptionsDialog.xaml");
        var wpf = ReadSource("src", "FreeX.App.Host", "OptionsDialog.xaml.cs");
        var avalonia = ReadSource("src", "FreeX.App.Avalonia", "MainWindow.Options.cs");
        var paired = wpfXaml + Environment.NewLine + wpf + Environment.NewLine + avalonia;

        wpfXaml.Should().Contain(
            "{x:Static presentation:FreeXAutomationIdCatalog.QuickAccessToolbarImportExportButton}");
        wpf.Should().Contain("FreeXAutomationIdCatalog.QuickAccessToolbarImportCustomizationMenuItem");
        wpf.Should().Contain("FreeXAutomationIdCatalog.QuickAccessToolbarExportCustomizationMenuItem");
        avalonia.Should().Contain("FreeXAutomationIdCatalog.QuickAccessToolbarImportExportButton");
        avalonia.Should().Contain("FreeXAutomationIdCatalog.QuickAccessToolbarImportCustomizationMenuItem");
        avalonia.Should().Contain("FreeXAutomationIdCatalog.QuickAccessToolbarExportCustomizationMenuItem");

        paired.Should().NotContain("\"QuickAccessToolbarImportExportButton\"");
        paired.Should().NotContain("\"QuickAccessToolbarImportCustomizationMenuItem\"");
        paired.Should().NotContain("\"QuickAccessToolbarExportCustomizationMenuItem\"");
    }

    [Fact]
    public void MergeWarningRenderers_UseCatalogInsteadOfRawIds()
    {
        var wpf = ReadSource("src", "FreeX.App.Host", "MainWindow.HomeFormatting.cs");
        var avalonia = ReadSource("src", "FreeX.App.Avalonia", "MainWindow.cs");
        var planner = ReadSource(
            "src",
            "FreeX.App.Presentation",
            "Editing",
            "MergeCellsContentWarningPlanner.cs");
        var paired = wpf + Environment.NewLine + avalonia;

        foreach (var member in new[]
                 {
                     "FreeXAutomationIdCatalog.MergeCellsContentWarningDialog",
                     "FreeXAutomationIdCatalog.MergeCellsKeepFirstButton",
                     "FreeXAutomationIdCatalog.MergeCellsConcatenateButton",
                     "FreeXAutomationIdCatalog.MergeCellsCancelButton"
                 })
        {
            planner.Should().Contain(member);
        }

        wpf.Should().Contain("presentation.DialogAutomationId");
        wpf.Should().Contain("keepFirstAction.AutomationId");
        avalonia.Should().Contain("presentation.DialogAutomationId");
        avalonia.Should().Contain("keepFirstAction.AutomationId");
        paired.Should().NotContain("\"MergeCellsContentWarningDialog\"");
        paired.Should().NotContain("\"MergeCellsKeepFirstButton\"");
        paired.Should().NotContain("\"MergeCellsConcatenateButton\"");
        paired.Should().NotContain("\"MergeCellsCancelButton\"");
    }

    [Fact]
    public void WorkbookStatisticsRenderers_UseCatalogInsteadOfRawIds()
    {
        var wpf = ReadSource("src", "FreeX.App.Host", "WorkbookStatisticsDialog.cs");
        var avalonia = ReadSource("src", "FreeX.App.Avalonia", "MainWindow.cs");
        var paired = wpf + Environment.NewLine + avalonia;

        wpf.Should().Contain("FreeXAutomationIdCatalog.WorkbookStatisticsSummary");
        wpf.Should().Contain("FreeXAutomationIdCatalog.WorkbookStatisticsCopyButton");
        avalonia.Should().Contain("FreeXAutomationIdCatalog.WorkbookStatisticsSummary");
        avalonia.Should().Contain("FreeXAutomationIdCatalog.WorkbookStatisticsCopyButton");
        paired.Should().NotContain("\"WorkbookStatisticsSummary\"");
        paired.Should().NotContain("\"WorkbookStatisticsCopyButton\"");
    }

    [Fact]
    public void SelectionPaneRenderers_UseCatalogInsteadOfRawIds()
    {
        var wpf = ReadSource("src", "FreeX.App.Host", "SelectionPaneDialog.cs");
        var avalonia = ReadSource("src", "FreeX.App.Avalonia", "MainWindow.SelectionPane.cs");
        var paired = wpf + Environment.NewLine + avalonia;

        foreach (var member in new[]
                 {
                     "FreeXAutomationIdCatalog.SelectionPane.Dialog",
                     "FreeXAutomationIdCatalog.SelectionPane.ObjectList",
                     "FreeXAutomationIdCatalog.SelectionPane.SearchBox",
                     "FreeXAutomationIdCatalog.SelectionPane.FilterBox",
                     "FreeXAutomationIdCatalog.SelectionPane.RenameBox",
                     "FreeXAutomationIdCatalog.SelectionPane.RenameButton",
                     "FreeXAutomationIdCatalog.SelectionPane.ToggleVisibilityButton",
                     "FreeXAutomationIdCatalog.SelectionPane.BringForwardButton",
                     "FreeXAutomationIdCatalog.SelectionPane.SendBackwardButton",
                     "FreeXAutomationIdCatalog.SelectionPane.ShowAllButton",
                     "FreeXAutomationIdCatalog.SelectionPane.HideAllButton",
                     "FreeXAutomationIdCatalog.SelectionPane.DeleteButton",
                     "FreeXAutomationIdCatalog.SelectionPane.OkButton",
                     "FreeXAutomationIdCatalog.SelectionPane.CancelButton"
                 })
        {
            wpf.Should().Contain(member);
            avalonia.Should().Contain(member);
        }

        paired.Should().NotContain("AutomationId(this, \"SelectionPane");
        paired.Should().NotContain("AutomationId(_list, \"SelectionPane");
        paired.Should().NotContain("AutomationId(listBox, \"SelectionPane");
        paired.Should().NotContain("AutomationId(dialog, \"SelectionPane");
    }

    [Fact]
    public void ConsolidateRenderers_UseCatalogInsteadOfRawIds()
    {
        var wpf = ReadSource("src", "FreeX.App.Host", "ConsolidateDialog.cs");
        var avalonia = ReadSource("src", "FreeX.App.Avalonia", "MainWindow.Consolidate.cs");
        var paired = wpf + Environment.NewLine + avalonia;

        foreach (var member in new[]
                 {
                     "FreeXAutomationIdCatalog.Consolidate.FunctionBox",
                     "FreeXAutomationIdCatalog.Consolidate.ReferenceBox",
                     "FreeXAutomationIdCatalog.Consolidate.AllReferencesList",
                     "FreeXAutomationIdCatalog.Consolidate.AddReferenceButton",
                     "FreeXAutomationIdCatalog.Consolidate.DeleteReferenceButton",
                     "FreeXAutomationIdCatalog.Consolidate.DestinationCellBox",
                     "FreeXAutomationIdCatalog.Consolidate.TopRowLabelsBox",
                     "FreeXAutomationIdCatalog.Consolidate.LeftColumnLabelsBox",
                     "FreeXAutomationIdCatalog.Consolidate.CreateLinksBox"
                 })
        {
            wpf.Should().Contain(member);
            avalonia.Should().Contain(member);
        }

        paired.Should().NotContain("AutomationId(_referenceBox, \"Consolidate");
        paired.Should().NotContain("AutomationId(referenceBox, \"Consolidate");
        paired.Should().NotContain("AutomationId(_functionBox, \"Consolidate");
        paired.Should().NotContain("AutomationId(functionBox, \"Consolidate");
    }

    private static string ReadSource(params string[] parts) =>
        TestWorkspaceFileLocator.ReadAllText(parts);
}
