using FluentAssertions;
using FreeX.App.Services;

namespace FreeX.App.Host.Tests;

public sealed partial class FormatCellsDialogXamlTests
{
    [Fact]
    public void WpfFillPaletteContainers_ArePopulatedFromSharedCatalogAtRuntime()
    {
        var xaml = XamlLocalizationTestHelper.ReadLocalizedXaml("FormatCellsDialog.xaml");
        var fillSource = DialogSourceTestSupport.ReadHostSourceFile("FormatCellsDialog.Fill.cs");
        var constructorSource = DialogSourceTestSupport.ReadHostSourceFile("FormatCellsDialog.xaml.cs");

        xaml.Should().Contain(
            "<UniformGrid x:Name=\"DlgFillPalettePanel\" Columns=\"10\" Rows=\"3\" Grid.Row=\"1\" Margin=\"0,0,0,8\"/>");
        xaml.Should().Contain(
            "<UniformGrid x:Name=\"DlgFillPatternColorPalettePanel\" Columns=\"8\" Rows=\"1\" Margin=\"0,0,0,6\"/>");
        fillSource.Should().Contain("FormatCellsFillPalettePlanner.BackgroundEntries");
        fillSource.Should().Contain("FormatCellsFillPalettePlanner.PatternEntries");
        constructorSource.Should().Contain("PopulateFillPalettes();");
    }

    [Fact]
    public void WpfFillPalettes_UseAuthoritativeGeometry()
    {
        var xaml = XamlLocalizationTestHelper.ReadLocalizedXaml("FormatCellsDialog.xaml");
        var source = DialogSourceTestSupport.ReadHostSourceFile("FormatCellsDialog.Fill.cs");

        xaml.Should().Contain(
            "x:Name=\"DlgFillPalettePanel\" Columns=\"10\" Rows=\"3\"");
        xaml.Should().Contain(
            "x:Name=\"DlgFillPatternColorPalettePanel\" Columns=\"8\" Rows=\"1\"");
        source.Should().Contain("Width = 28");
        source.Should().Contain("Height = 20");
        source.Should().Contain("Width = 24");
        source.Should().Contain("Height = 19");
    }

    [Fact]
    public void AvaloniaFillEditor_UsesTheSameSharedCatalog()
    {
        var source = WorkspaceFileLocator.ReadAllText(
            "src", "FreeX.App.Avalonia", "FormatCellsFillEditor.cs");

        source.Should().Contain("FormatCellsFillPalettePlanner.BackgroundEntries");
        source.Should().Contain("FormatCellsFillPalettePlanner.PatternEntries");
        source.Should().NotContain("new CellColor(255, 255, 255)");
    }

}
