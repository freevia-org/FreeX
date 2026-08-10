namespace FreeW.App.Presentation.Tests;

public sealed class RendererUtilityOwnershipSourceTests
{
    [Fact]
    public void WordArtForegroundSelectionBelongsToPresentation()
    {
        var wpf = ReadSource("freew", "FreeW.App.Host", "Editing", "DocumentView.cs");
        var avalonia = ReadSource("freew", "FreeW.App.Avalonia", "Editing", "DocumentView.cs");

        wpf.Should().Contain("WordArtForegroundPolicy.ResolveColorHex(");
        wpf.Should().NotContain("0.2126 * background.R");
        avalonia.Should().Contain("WordArtForegroundPolicy.ResolveColorHex(");
        Occurrences(avalonia, "WordArtForegroundPolicy.ResolveColorHex(").Should().Be(2);
        avalonia.Should().NotContain("ContrastingWordArtTextColor");
        avalonia.Should().NotContain("ContrastingPdfTextColor");
    }

    [Fact]
    public void ArrowheadGeometryBelongsToSharedAndPresentationPlanners()
    {
        var shared = ReadSource(
            "shared",
            "Free.Shared.Drawing",
            "DirectionalArrowheadGeometryPlanner.cs");
        var formula = ReadSource(
            "src",
            "FreeX.App.Presentation",
            "FormulaAuditing",
            "FormulaTraceOverlayPlanner.cs");
        var wpf = ReadSource("freew", "FreeW.App.Host", "Editing", "SmartArtRenderer.cs");
        var avalonia = ReadSource("freew", "FreeW.App.Avalonia", "Editing", "DocumentView.cs");

        shared.Should().Contain("public static class DirectionalArrowheadGeometryPlanner");
        formula.Should().Contain("DirectionalArrowheadGeometryPlanner.Calculate(");
        wpf.Should().Contain("SmartArtConnectorArrowheadPlanner.Calculate(");
        avalonia.Should().Contain("SmartArtConnectorArrowheadPlanner.Calculate(");
        wpf.Should().NotContain("const double arrowLength = 6;");
        avalonia.Should().NotContain("const double arrowLength = 6;");
    }

    [Fact]
    public void WpfUrlShellLaunchesUseTheDesktopUriAdapter()
    {
        var documentView = ReadSource("freew", "FreeW.App.Host", "Editing", "DocumentView.cs");
        var ribbon = ReadSource("freew", "FreeW.App.Host", "Ribbon", "FreeWRibbonCommands.cs");

        documentView.Should().Contain("DesktopExternalUriLauncher.Open(uri.AbsoluteUri)");
        documentView.Should().NotContain("target => Process.Start(new ProcessStartInfo(target.AbsoluteUri)");
        ribbon.Should().Contain("DesktopExternalUriLauncher.Open(target)");
        ribbon.Should().NotContain("new System.Diagnostics.ProcessStartInfo(uri.AbsoluteUri)");
    }

    private static int Occurrences(string source, string value) =>
        source.Split(value, StringSplitOptions.None).Length - 1;

    private static string ReadSource(params string[] parts)
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        return File.ReadAllText(Path.Combine([root, .. parts]));
    }
}
