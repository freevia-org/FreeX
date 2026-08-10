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

    private static string ReadSource(params string[] parts) =>
        TestWorkspaceFileLocator.ReadAllText(parts);
}
