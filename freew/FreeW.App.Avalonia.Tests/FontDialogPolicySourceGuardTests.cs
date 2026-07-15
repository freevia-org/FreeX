using System.IO;

namespace FreeW.App.Avalonia.Tests;

public sealed class FontDialogPolicySourceGuardTests
{
    [Fact]
    public void FontDialog_DelegatesBasicCatalogsStateValidationAndResultConstructionToPresentationPlanner()
    {
        var source = ReadAvaloniaSource("FontDialog.cs");

        source.Should().Contain("using FreeW.App.Presentation.Dialogs;");
        source.Should().Contain("FontDialogPlanner.BuildBasicInitialState(");
        source.Should().Contain("FontDialogPlanner.BasicFamilyChoices");
        source.Should().Contain("FontDialogPlanner.BasicSizeChoices");
        source.Should().Contain("FontDialogPlanner.BasicColorChoices");
        source.Should().Contain("FontDialogPlanner.HighlightColorChoices");
        source.Should().Contain("new FontDialogBasicInput(");
        source.Should().Contain("FontDialogPlanner.TryBuildBasicResult(");
        source.Should().Contain("ToDialogResult(plannedResult!)");
    }

    [Fact]
    public void FontDialog_DoesNotOwnBasicDialogPolicyOrParsing()
    {
        var source = ReadAvaloniaSource("FontDialog.cs");

        source.Should().NotContain("private static readonly string[] SizeLadder");
        source.Should().NotContain("private static readonly string[] FamilyPresets");
        source.Should().NotContain("private const double MinFontSizePt");
        source.Should().NotContain("private const double MaxFontSizePt");
        source.Should().NotContain("private static readonly (string Label, string? Hex)[] FontColorPalette");
        source.Should().NotContain("private static readonly (string Label, string? Hex)[] HighlightPalette");
        source.Should().NotContain("double.TryParse(");
        source.Should().NotContain("NumberStyles.");
        source.Should().NotContain("Math.Clamp(");
        source.Should().NotContain("SelectedHex(");
        source.Should().NotContain("Invalid font size:");
    }

    private static string ReadAvaloniaSource(string fileName)
    {
        var path = Path.Combine(TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx"), "freew", "FreeW.App.Avalonia", fileName);
        return File.ReadAllText(path);
    }

}
