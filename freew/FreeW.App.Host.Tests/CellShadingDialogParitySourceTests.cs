using System.IO;

namespace FreeW.App.Host.Tests;

public sealed class CellShadingDialogParitySourceTests
{
    [Fact]
    public void Wpf_ribbon_uses_the_app_owned_picker_instead_of_an_inline_window()
    {
        var commandSource = ReadHostSource("Ribbon", "FreeWRibbonCommands.cs");
        var dialogSource = ReadHostSource("CellShadingDialog.cs");
        var cellShadingBlock = ExtractBlock(commandSource, "private sealed class CellShadingCommand", "private sealed class CellBordersCommand");

        cellShadingBlock.Should().Contain("var result = CellShadingDialog.Prompt(owner);");
        cellShadingBlock.Should().NotContain("private (bool Chosen, string? Hex) ShowPicker");
        dialogSource.Should().Contain("CellShadingDialogPlanner.Layout");
        dialogSource.Should().Contain("PreviewKeyDown");
        dialogSource.Should().Contain("first.Focus()");
        dialogSource.Should().Contain("CellShadingDialogPlanner.SelectNoColor()");
    }

    [Fact]
    public void Wpf_dialog_is_an_app_owned_harness_route_with_explicit_cancel_semantics()
    {
        var catalog = ReadWorkspaceSource("freew", "tools", "FreeW.DialogVisualHarness", "FreeWDialogEvidenceCatalog.cs");

        catalog.Should().Contain("Pair(\"cell-shading\", \"CellShadingDialog\")");
    }

    private static string ReadHostSource(params string[] parts) => ReadWorkspaceSource(new[] { "freew", "FreeW.App.Host" }.Concat(parts).ToArray());

    private static string ExtractBlock(string source, string start, string end)
    {
        var startIndex = source.IndexOf(start, StringComparison.Ordinal);
        startIndex.Should().BeGreaterThanOrEqualTo(0);
        var endIndex = source.IndexOf(end, startIndex, StringComparison.Ordinal);
        endIndex.Should().BeGreaterThan(startIndex);
        return source[startIndex..endIndex];
    }

    private static string ReadWorkspaceSource(params string[] parts)
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        return File.ReadAllText(Path.Combine(new[] { root }.Concat(parts).ToArray()));
    }
}
