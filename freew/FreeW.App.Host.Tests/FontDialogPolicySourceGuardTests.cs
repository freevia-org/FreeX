using System.IO;

namespace FreeW.App.Host.Tests;

public sealed class FontDialogPolicySourceGuardTests
{
    [Fact]
    public void FontDialog_DelegatesCatalogsStateValidationAndResultConstructionToPresentationPlanner()
    {
        var source = ReadHostSource("FontDialog.cs");

        source.Should().Contain("using FreeW.App.Presentation.Dialogs;");
        source.Should().Contain("FontDialogPlanner.BuildInitialState(");
        source.Should().Contain("FontDialogPlanner.SizeChoices");
        source.Should().Contain("FontDialogPlanner.ColorChoices");
        source.Should().Contain("FontDialogPlanner.LigatureChoices");
        source.Should().Contain("FontDialogPlanner.NumberFormChoices");
        source.Should().Contain("FontDialogPlanner.NumberSpacingChoices");
        source.Should().Contain("new FontDialogInput(");
        source.Should().Contain("FontDialogPlanner.TryBuildResult(");
        source.Should().Contain("Double strikethrough");
        source.Should().Contain("state.DoubleStrikethrough");
    }

    [Fact]
    public void FontDialog_DoesNotOwnFontPolicyOrParsing()
    {
        var source = ReadHostSource("FontDialog.cs");

        source.Should().NotContain("private static readonly (string Label, string? Hex)[] Colors");
        source.Should().NotContain("private static readonly (string Label, double Size)[] Sizes");
        source.Should().NotContain("LigatureMode.");
        source.Should().NotContain("NumberForm.");
        source.Should().NotContain("NumberSpacing.");
        source.Should().NotContain("IndexOfColor(");
        source.Should().NotContain("IndexOfLigature(");
        source.Should().NotContain("IndexOfNumberForm(");
        source.Should().NotContain("IndexOfNumberSpacing(");
        source.Should().NotContain("TryParseDouble(");
        source.Should().NotContain("double.TryParse(");
        source.Should().NotContain("int.TryParse(");
        source.Should().NotContain("NumberStyles.");
        source.Should().NotContain("current with");
        source.Should().NotContain("FontSizePt   =");
        source.Should().NotContain("ColorHex     =");
        source.Should().NotContain("CharacterSpacingPt =");
        source.Should().NotContain("KerningMinSizePt   =");
        source.Should().NotContain("StylisticSet       =");
        source.Should().NotContain(FontSizeValidationMessage);
        source.Should().NotContain(StylisticSetValidationMessage);
    }

    private const string FontSizeValidationMessage = "Enter a positive font size in points.";
    private const string StylisticSetValidationMessage = "Stylistic set must be a number from 1 to 20, or blank.";

    private static string ReadHostSource(string fileName)
    {
        var path = Path.Combine(TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx"), "freew", "FreeW.App.Host", fileName);
        return File.ReadAllText(path);
    }

}
