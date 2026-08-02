using System.IO;

namespace FreeW.App.Host.Tests;

public sealed class DialogCaptureAdapterParitySourceTests
{
    [Fact]
    public void Wpf_manual_hyphenation_capture_uses_a_real_planner_candidate()
    {
        var factory = ReadWorkspaceSource("freew", "tools", "FreeW.DialogVisualHarness.Wpf", "WpfDialogRouteFactory.cs");
        var program = ReadWorkspaceSource("freew", "tools", "FreeW.DialogVisualHarness.Wpf", "Program.cs");

        factory.Should().Contain("routeId == \"manual-hyphenation\"");
        factory.Should().Contain("ManualHyphenationPlanner.CreateSession(editor.Model).Current");
        factory.Should().Contain("FreeW.App.Host.ManualHyphenationDialog");
        program.Should().Contain("scenario.RouteId == \"manual-hyphenation\"");
        program.Should().Contain("PixelContentMetrics.Compute");
        program.Should().Contain("c.FullPixelContent?.PassesContentGate == true");
    }

    [Fact]
    public void Avalonia_capture_routes_use_the_app_owned_dialog_families()
    {
        var factory = ReadWorkspaceSource("freew", "tools", "FreeW.DialogVisualHarness.Avalonia", "AvaloniaDialogRouteFactory.cs");
        var program = ReadWorkspaceSource("freew", "tools", "FreeW.DialogVisualHarness.Avalonia", "Program.cs");

        factory.Should().Contain("[\"caption\"] = \"CaptionDialog\"");
        factory.Should().Contain("[\"character-formatting-picker\"] = \"CharacterFormattingPickerDialog\"");
        factory.Should().Contain("[\"header-footer-text\"] = \"HeaderFooterTextDialog\"");
        factory.Should().Contain("[\"manual-hyphenation\"] = \"ManualHyphenationDialog\"");
        factory.Should().Contain("ForTestShading");
        factory.Should().Contain("ManualHyphenationPlanner.CreateSession(document).Current");
        program.Should().Contain("scenario.RouteId == \"manual-hyphenation\"");
    }

    private static string ReadWorkspaceSource(params string[] parts)
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        return File.ReadAllText(Path.Combine(new[] { root }.Concat(parts).ToArray()));
    }
}
