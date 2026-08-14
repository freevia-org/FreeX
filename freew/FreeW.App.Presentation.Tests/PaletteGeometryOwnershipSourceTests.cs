namespace FreeW.App.Presentation.Tests;

public sealed class PaletteGeometryOwnershipSourceTests
{
    [Fact]
    public void Renderer_ribbon_commands_project_the_portable_palette_catalog()
    {
        var catalog = ReadSource(
            "freew", "FreeW.App.Presentation", "Ribbon", "FreeWRibbonPaletteCatalog.cs");
        var workflow = ReadSource(
            "freew", "FreeW.App.Presentation", "Ribbon", "FormattingGalleryRibbonWorkflow.cs");
        var definitions = ReadSource(
            "freew", "FreeW.Ribbon.Definitions", "FreeWCanonicalRibbonTabs.Ordinary.cs");
        var wpf = ReadSource("freew", "FreeW.App.Host", "Ribbon", "FreeWRibbonCommands.cs");
        var avalonia = ReadSource(
            "freew", "FreeW.App.Avalonia", "Ribbon", "FreeWAvaloniaRibbonCommands.cs");

        catalog.Should().Contain("public static class FreeWRibbonPaletteCatalog");
        definitions.Should().Contain("BuildPaletteMenu(FreeWRibbonPaletteCatalog.Highlights)");
        definitions.Should().NotContain("new(\"Yellow\", new RibbonCommandId(\"freew.para-shading.yellow\"))");
        wpf.Should().Contain("FreeWRibbonPaletteCatalog.TextAndHighlightPickerSwatches");
        wpf.Should().Contain("FreeWRibbonPaletteCatalog.PageColorPickerSwatches");
        workflow.Should().Contain("RegisterPalette(FreeWRibbonPaletteCatalog.FontColors");
        wpf.Should().Contain("FormattingGalleryRibbonWorkflow.Register(");
        avalonia.Should().Contain("FormattingGalleryRibbonWorkflow.Register(");
        avalonia.Should().NotContain("RegisterColorPalette(");
        avalonia.Should().NotContain("Add(r, editor, \"freew.highlight.black\", \"#000000\")");
        avalonia.Should().NotContain("Add(r, editor, \"freew.page-color.white\",        \"#FFFFFF\")");
    }

    [Fact]
    public void Renderer_geometry_adapters_project_portable_path_figures()
    {
        var planner = ReadSource(
            "freew", "FreeW.App.Presentation", "DocumentView", "CustomShapePathPlanner.cs");
        var wpfAdapter = ReadSource(
            "freew", "FreeW.App.Host", "Editing", "CustomShapePathWpfAdapter.cs");
        var avaloniaAdapter = ReadSource(
            "freew", "FreeW.App.Avalonia", "Editing", "CustomShapePathAvaloniaAdapter.cs");
        var wpf = ReadSource("freew", "FreeW.App.Host", "Editing", "DocumentView.cs");
        var avalonia = ReadSource("freew", "FreeW.App.Avalonia", "Editing", "DocumentView.cs");

        planner.Should().Contain("public static class CustomShapePathPlanner");
        wpfAdapter.Should().Contain("CustomShapePathPlanner.Build(");
        avaloniaAdapter.Should().Contain("CustomShapePathPlanner.Build(");
        wpf.Should().Contain("CustomShapePathWpfAdapter.Build(");
        avalonia.Should().Contain("CustomShapePathAvaloniaAdapter.Build(");
        avalonia.Should().Contain("new CustomShapePathBounds(x, y, width, height, InvertY: true)");
        wpf.Should().NotContain("foreach (var segment in cg.Segments)");
        wpf.Should().NotContain("foreach (var seg in cg.Segments)");
        avalonia.Should().NotContain("foreach (var segment in cg.Segments)");
        avalonia.Should().NotContain("foreach (var seg in cg.Segments)");
    }

    private static string ReadSource(params string[] parts)
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        return File.ReadAllText(Path.Combine([root, .. parts]));
    }
}
