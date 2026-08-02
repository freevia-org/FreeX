using System.IO;

namespace FreeW.App.Avalonia.Tests;

public sealed class FontDialogPolicySourceGuardTests
{
    [Fact]
    public void FontDialog_DelegatesFullCatalogsStateValidationAndResultConstructionToPresentationPlanner()
    {
        var source = ReadAvaloniaSource("FontDialog.cs");

        source.Should().Contain("using FreeW.App.Presentation.Dialogs;");
        source.Should().Contain("FontDialogPlanner.BuildInitialState(");
        source.Should().Contain("FontDialogPlanner.SizeChoices");
        source.Should().Contain("FontDialogPlanner.ColorChoices");
        source.Should().Contain("FontDialogPlanner.LigatureChoices");
        source.Should().Contain("FontDialogPlanner.NumberFormChoices");
        source.Should().Contain("FontDialogPlanner.NumberSpacingChoices");
        source.Should().Contain("new FontDialogInput(");
        source.Should().Contain("FontDialogPlanner.TryBuildResult(");
        source.Should().Contain("ToDialogResult(planned!)");
        source.Should().Contain("Double strikethrough");
        source.Should().Contain("DoubleStrikethroughIndeterminate");
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
