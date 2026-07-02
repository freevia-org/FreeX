using System.IO;

namespace FreeW.App.Host.Tests;

public sealed class RestrictEditingDialogPolicySourceGuardTests
{
    [Fact]
    public void RestrictEditingDialog_DelegatesProtectionPolicyToPresentationPlanner()
    {
        var source = ReadHostSource("RestrictEditingDialog.cs");

        source.Should().Contain("using FreeW.App.Presentation.Dialogs;");
        source.Should().Contain("RestrictEditingDialogPlanner.BuildPlan(current)");
        source.Should().Contain("RestrictEditingDialogPlanner.ModeOptions");
        source.Should().Contain("RestrictEditingDialogPlanner.TryCreateStartSettings(");
        source.Should().Contain("RestrictEditingDialogPlanner.TryCreateStopSettings(");
        source.Should().Contain("RestrictEditingDialogPlanner.StartButtonText");
        source.Should().Contain("RestrictEditingDialogPlanner.StopButtonText");
        source.Should().NotContain("ProtectionPasswordHelper.CreateWithPassword");
        source.Should().NotContain("ProtectionPasswordHelper.VerifyPassword");
    }

    private static string ReadHostSource(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory);
        while (path is not null && !Directory.Exists(Path.Combine(path, "freew")))
            path = Directory.GetParent(path)?.FullName;

        path.Should().NotBeNull();
        var fullPath = Path.Combine(path!, "freew", "FreeW.App.Host", fileName);
        File.Exists(fullPath).Should().BeTrue($"{fullPath} should exist");
        return File.ReadAllText(fullPath);
    }
}
