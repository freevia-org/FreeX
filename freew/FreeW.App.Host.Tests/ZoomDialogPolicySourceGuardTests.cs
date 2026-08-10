using System.IO;

namespace FreeW.App.Host.Tests;

public sealed class ZoomDialogPolicySourceGuardTests
{
    [Fact]
    public void ZoomDialog_UsesSharedPresentationPlannerForPolicy()
    {
        var source = ReadHostSource("ZoomDialog.cs");

        source.Should().Contain("using FreeW.App.Presentation.Dialogs;");
        source.Should().Contain("new ZoomDialogSession(currentFactor)");
        source.Should().Contain("_session.PlanAcceptance(_fitFactors)");
        source.Should().Contain("acceptance.Validation.FocusTarget");
        source.Should().Contain("ZoomDialogPlanner.Text");
        source.Should().Contain("ZoomDialogPlanner.FormatPresetLabel(preset.Percent)");
        source.Should().NotContain("Content = \"Page width\"");
        source.Should().NotContain("Title = \"Zoom\"");
        source.Should().NotContain("new ZoomDialogSelectionRequest(");
        source.Should().NotContain("GetSelectedFitOption");
        source.Should().NotContain("GetSelectedPresetPercent");
        source.Should().NotContain("ResolveValidationError");
        source.Should().NotContain("private static readonly int[] Presets");
        source.Should().NotContain("int.TryParse(");
        source.Should().NotContain("NumberStyles.Integer");
        source.Should().NotContain("CultureInfo.CurrentCulture");
        source.Should().NotContain("ZoomLevels.FromPercent(");
    }

    private static string ReadHostSource(string fileName)
    {
        var path = Path.Combine(TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx"), "freew", "FreeW.App.Host", fileName);
        return File.ReadAllText(path);
    }

}
