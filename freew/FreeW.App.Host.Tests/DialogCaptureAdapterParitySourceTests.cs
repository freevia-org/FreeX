using System.IO;

namespace FreeW.App.Host.Tests;

public sealed class DialogCaptureAdapterParitySourceTests
{
    [Fact]
    public void Wpf_manual_hyphenation_capture_uses_a_real_planner_candidate()
    {
        var factory = ReadWorkspaceSource("freew", "tools", "FreeW.DialogVisualHarness.Wpf", "WpfDialogRouteFactory.cs");
        var program = ReadWorkspaceSource("freew", "tools", "FreeW.DialogVisualHarness.Wpf", "Program.cs");
        var catalog = ReadWorkspaceSource("freew", "tools", "FreeW.DialogVisualHarness", "FreeWDialogEvidenceCatalog.cs");

        catalog.Should().Contain("Pair(\"manual-hyphenation\", \"ManualHyphenationDialog\"");
        catalog.Should().Contain("wpfAction: FreeWDialogOpenAction.ManualHyphenation");
        factory.Should().Contain("ManualHyphenationPlanner.CreateSession(editor.Model).Current");
        factory.Should().Contain("ManualHyphenationDialog.CreateForVisualHarness(owner, candidate)");
        program.Should().Contain("FreeWDialogPopulationKind.ManualHyphenation");
        program.Should().Contain("PixelContentMetrics.Compute");
        program.Should().Contain("c.FullPixelContent?.PassesContentGate == true");
    }

    [Fact]
    public void Avalonia_capture_routes_use_the_app_owned_dialog_families()
    {
        var factory = ReadWorkspaceSource("freew", "tools", "FreeW.DialogVisualHarness.Avalonia", "AvaloniaDialogRouteFactory.cs");
        var program = ReadWorkspaceSource("freew", "tools", "FreeW.DialogVisualHarness.Avalonia", "Program.cs");
        var catalog = ReadWorkspaceSource("freew", "tools", "FreeW.DialogVisualHarness", "FreeWDialogEvidenceCatalog.cs");

        catalog.Should().Contain("AvaloniaOnly(\"caption\", \"CaptionDialog\")");
        catalog.Should().Contain("AvaloniaOnly(\"character-formatting-picker\", \"CharacterFormattingPickerDialog\"");
        catalog.Should().Contain("AvaloniaOnly(\"header-footer-text\", \"HeaderFooterTextDialog\")");
        catalog.Should().Contain("Pair(\"manual-hyphenation\", \"ManualHyphenationDialog\"");
        factory.Should().Contain("ForTestShading");
        factory.Should().Contain("ManualHyphenationPlanner.CreateSession(document).Current");
        program.Should().Contain("FreeWDialogPopulationKind.ManualHyphenation");
    }

    private static string ReadWorkspaceSource(params string[] parts)
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        return File.ReadAllText(Path.Combine(new[] { root }.Concat(parts).ToArray()));
    }
}
