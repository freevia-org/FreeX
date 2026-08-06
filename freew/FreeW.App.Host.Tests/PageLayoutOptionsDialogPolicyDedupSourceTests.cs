using System.IO;

namespace FreeW.App.Host.Tests;

public sealed class PageLayoutOptionsDialogPolicyDedupSourceTests
{
    [Theory]
    [InlineData("HyphenationOptionsDialog.cs", "HyphenationOptionsDialogSession")]
    [InlineData("LineNumberOptionsDialog.cs", "LineNumberOptionsDialogSession")]
    public void Dialogs_DelegateInitialStateAndResultPolicyToPresentationSessions(
        string fileName,
        string sessionName)
    {
        var source = ReadHostSource(fileName);

        source.Should().Contain("using FreeW.App.Presentation.Dialogs;");
        source.Should().Contain(sessionName);
        source.Should().Contain("_session.InitialState");
        source.Should().Contain("_session.PlanAcceptance(");
        source.Should().NotContain("Planner.BuildInitialState(");
        source.Should().NotContain("Planner.TryBuildResult(");
    }

    [Fact]
    public void HyphenationOptionsDialog_DoesNotOwnParsingValidationRoundingOrResultConstruction()
    {
        var source = ReadHostSource("HyphenationOptionsDialog.cs");

        source.Should().NotContain("TryParseDouble");
        source.Should().NotContain("double.TryParse");
        source.Should().NotContain("NumberStyles.Float");
        source.Should().NotContain("Math.Round");
        source.Should().NotContain("new Result(");
        source.Should().NotContain("new HyphenationOptionsDialogResult(");
        source.Should().NotContain(HyphenationValidationText);
    }

    [Fact]
    public void LineNumberOptionsDialog_DoesNotOwnModeLabelsParsingValidationOrResultConstruction()
    {
        var source = ReadHostSource("LineNumberOptionsDialog.cs");

        source.Should().NotContain("private static readonly string[] ModeLabels");
        source.Should().NotContain("[\"Continuous\", \"Restart Each Page\"]");
        source.Should().NotContain("int.TryParse");
        source.Should().NotContain("NumberStyles.Integer");
        source.Should().NotContain("new Result(");
        source.Should().NotContain("new LineNumberOptionsDialogResult(");
        source.Should().NotContain("Start At must be a whole number of 1 or greater.");
        source.Should().NotContain("Count By must be a whole number of 1 or greater.");
        source.Should().NotContain("LineNumberMode.RestartEachPage ? 1 : 0");
        source.Should().NotContain("SelectedIndex == 1");
    }

    private const string HyphenationValidationText =
        "Enter a non-negative hyphenation zone and a non-negative consecutive-hyphen limit (0 = no limit).";

    private static string ReadHostSource(string fileName)
    {
        var path = Path.Combine(TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx"), "freew", "FreeW.App.Host", fileName);
        return File.ReadAllText(path);
    }

}
