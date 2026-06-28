using System.IO;

namespace FreeW.App.Host.Tests;

public sealed class ZoomDialogPolicySourceGuardTests
{
    [Fact]
    public void ZoomDialog_UsesSharedPresentationPlannerForPolicy()
    {
        var source = ReadHostSource("ZoomDialog.cs");

        source.Should().Contain("using FreeW.App.Presentation.Dialogs;");
        source.Should().Contain("ZoomDialogPlanner.Build(currentFactor)");
        source.Should().Contain("new ZoomDialogSelectionRequest(");
        source.Should().Contain("ZoomDialogPlanner.TryCreateResult(");
        source.Should().NotContain("private static readonly int[] Presets");
        source.Should().NotContain("int.TryParse(");
        source.Should().NotContain("NumberStyles.Integer");
        source.Should().NotContain("CultureInfo.CurrentCulture");
        source.Should().NotContain("ZoomLevels.FromPercent(");
    }

    private static string ReadHostSource(string fileName)
    {
        var path = Path.Combine(FindRepositoryRoot(), "freew", "FreeW.App.Host", fileName);
        return File.ReadAllText(path);
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "FreeW.slnx")))
                return directory.FullName;
        }

        throw new DirectoryNotFoundException("Could not locate repository root from test output directory.");
    }
}
