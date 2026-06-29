using System.IO;

namespace FreeW.App.Avalonia.Tests;

public sealed class ZoomDialogPolicySourceGuardTests
{
    [Fact]
    public void ZoomDialog_UsesSharedPresentationPlannerForPolicy()
    {
        var source = ReadAvaloniaSource("ZoomDialog.cs");

        source.Should().Contain("using FreeW.App.Presentation.Dialogs;");
        source.Should().Contain("ZoomDialogPlanner.Build(currentScale)");
        source.Should().Contain("new ZoomDialogSelectionRequest(");
        source.Should().Contain("ZoomDialogPlanner.TryCreateResult(");
        source.Should().Contain("ZoomDialogPlanner.ValidationMessageFor(error)");
        source.Should().Contain("ZoomDialogFitFactors");
        source.Should().NotContain("NumericUpDown");
        source.Should().NotContain("currentScale * 100");
        source.Should().NotContain("switch (pct)");
        source.Should().NotContain("int.TryParse(");
        source.Should().NotContain("ZoomLevels.FromPercent(");
    }

    private static string ReadAvaloniaSource(string fileName)
    {
        var path = Path.Combine(FindRepositoryRoot(), "freew", "FreeW.App.Avalonia", fileName);
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
