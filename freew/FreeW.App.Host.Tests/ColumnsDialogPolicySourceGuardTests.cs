using System.IO;

namespace FreeW.App.Host.Tests;

public sealed class ColumnsDialogPolicySourceGuardTests
{
    [Fact]
    public void ColumnsDialog_DelegatesPolicyToPresentationPlanner()
    {
        var source = ReadHostSource("ColumnsDialog.cs");

        source.Should().Contain("using FreeW.App.Presentation.Dialogs;");
        source.Should().Contain("ColumnsDialogPlanner.BuildInitialState(");
        source.Should().Contain("ColumnsDialogPlanner.Presets");
        source.Should().Contain("ColumnsDialogPlanner.ColumnCountForPreset(");
        source.Should().Contain("new ColumnsDialogInput(");
        source.Should().Contain("ColumnsDialogPlanner.TryBuildResult(");
        source.Should().Contain("ColumnsDialogResult?");
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
