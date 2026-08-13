using System.IO;

namespace FreeW.App.Host.Tests;

public sealed class ColumnsDialogPolicySourceGuardTests
{
    [Fact]
    public void ColumnsDialog_DelegatesPolicyToPresentationSession()
    {
        var source = ReadHostSource("ColumnsDialog.cs");

        source.Should().Contain("using FreeW.App.Presentation.Dialogs;");
        source.Should().Contain("ColumnsDialogSession");
        source.Should().Contain("_session.InitialState");
        source.Should().Contain("_session.Presets");
        source.Should().Contain("_session.CountTextForPreset(");
        source.Should().Contain("_session.PlanAcceptance(");
        source.Should().Contain("ColumnsDialogResult?");
        source.Should().NotContain("ColumnsDialogPlanner.BuildInitialState(");
        source.Should().NotContain("ColumnsDialogPlanner.TryBuildResult(");
        source.Should().NotContain("private static readonly string[] Presets");
        source.Should().NotContain("PresetIndexFor(");
        source.Should().NotContain("ApplyPreset(");
        source.Should().NotContain("UnequalWidths(");
        source.Should().NotContain("TryParseInt(");
        source.Should().NotContain("TryParseDouble(");
        source.Should().NotContain("ParseOr(");
        source.Should().NotContain("int.TryParse(");
        source.Should().NotContain("double.TryParse(");
        source.Should().NotContain("NumberStyles.");
    }

    private static string ReadHostSource(string fileName)
    {
        var path = Path.Combine(TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx"), "freew", "FreeW.App.Host", fileName);
        return File.ReadAllText(path);
    }

}
