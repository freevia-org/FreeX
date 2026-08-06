using System.IO;

namespace FreeW.App.Avalonia.Tests;

public sealed class FontDialogPolicySourceGuardTests
{
    [Fact]
    public void FontDialog_DelegatesInteractionStateAcceptanceAndApplyPlanningToPresentationSession()
    {
        var source = ReadAvaloniaSource("FontDialog.cs");

        source.Should().Contain("using FreeW.App.Presentation.Dialogs;");
        source.Should().Contain("_session = FontDialogPlanner.CreateSession(");
        source.Should().Contain("_session.InitialState");
        source.Should().Contain("_session.PlanAcceptance(new FontDialogControlState(");
        source.Should().Contain("_session.PlanVerticalAlignmentToggle(");
        source.Should().Contain("_session.BuildApplyPlan(result)");
        source.Should().Contain("ExecuteApplyPlan(");
        source.Should().Contain("FontDialogApplyCommand.SetFamily");
        source.Should().Contain("FontDialogPlanner.SizeChoices");
        source.Should().Contain("FontDialogPlanner.ColorChoices");
        source.Should().Contain("FontDialogPlanner.LigatureChoices");
        source.Should().Contain("FontDialogPlanner.NumberFormChoices");
        source.Should().Contain("FontDialogPlanner.NumberSpacingChoices");
        source.Should().Contain("FontDialogPlanner.Text.DoubleStrikethroughLabel");
        source.Should().Contain("DoubleStrikethroughIndeterminate");
        source.Should().Contain("Check(FontDialogPlanner.Text.HiddenLabel, threeState: true)");
        source.Should().Contain("HiddenIndeterminate");
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
        source.Should().NotContain("new FontDialogInput(");
        source.Should().NotContain("FontDialogPlanner.TryBuildResult(");
        source.Should().NotContain("ToDialogResult(");
        source.Should().NotContain("Invalid font size:");
        source.Should().NotContain("Title = \"Font\"");
        source.Should().NotContain("AddField(fontPanel, \"Font family:\"");
        source.Should().NotContain("AddField(advancedPanel, \"Character spacing (pt):\"");
    }

    private static string ReadAvaloniaSource(string fileName)
    {
        var path = Path.Combine(TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx"), "freew", "FreeW.App.Avalonia", fileName);
        return File.ReadAllText(path);
    }

}
