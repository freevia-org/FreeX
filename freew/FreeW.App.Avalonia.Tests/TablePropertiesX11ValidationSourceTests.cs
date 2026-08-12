using FreeW.Validation.Avalonia;

namespace FreeW.App.Avalonia.Tests;

public sealed class TablePropertiesX11ValidationSourceTests
{
    [Fact]
    public void Validation_option_filters_only_its_control_arguments()
    {
        TablePropertiesX11ValidationOptions.TryParse(
            ["--table-properties-x11-validation", "/work/result.json", "/documents/demo.docx"],
            out var options,
            out var startupArguments,
            out var error).Should().BeTrue(error);

        options.Should().NotBeNull();
        options!.ResultPath.Should().Be("/work/result.json");
        startupArguments.Should().Equal("/documents/demo.docx");
        error.Should().BeNull();
    }

    [Fact]
    public void Shipping_renderer_has_no_x11_or_persistence_ownership()
    {
        var mainWindow = File.ReadAllText(RepoFile("freew/FreeW.App.Avalonia/MainWindow.cs"));
        var adapter = File.ReadAllText(RepoFile(
            "freew/FreeW.App.Avalonia/MainWindow.ValidationAccessAdapter.cs"));
        var coordinator = File.ReadAllText(RepoFile(
            "freew/TestSupport/Validation.Avalonia/TablePropertiesX11Validation.cs"));

        mainWindow.Should().NotContain("FREEW_TABLE_PROPERTIES_X11");
        mainWindow.Should().NotContain("freew.table-properties.x11-result.v1");
        mainWindow.Should().NotContain("JsonSerializer");
        adapter.Should().NotContain("FREEW_TABLE_PROPERTIES_X11");
        adapter.Should().NotContain("JsonSerializer");
        adapter.Should().NotContain("File.WriteAllText");
        adapter.Should().NotContain("InsertTable(2, 2)");
        coordinator.Should().Contain("InsertTable(2, 2)");
        coordinator.Should().Contain("CommandLineValueOptionParser.Parse");
        coordinator.Should().Contain("JsonArtifactIO.Write");
        coordinator.Should().NotContain("JsonSerializer.Serialize");
        coordinator.Should().Contain("freew.table-properties.x11-result.v1");
    }

    [Fact]
    public void Physical_runner_selects_the_validation_host_without_x11_environment_contracts()
    {
        var runner = File.ReadAllText(RepoFile("tools/Run-FreeWTablePropertiesX11Validation.ps1"));
        var dockerRunner = File.ReadAllText(RepoFile("tools/Run-LinuxInteractiveDocker.ps1"));

        runner.Should().Contain("Host = \"Validation\"");
        runner.Should().Contain("--table-properties-x11-validation");
        runner.Should().NotContain("FREEW_TABLE_PROPERTIES_X11_SEED");
        runner.Should().NotContain("FREEW_TABLE_PROPERTIES_X11_RESULT");
        dockerRunner.Should().Contain("freew/TestSupport/Validation.Avalonia/FreeW.Validation.Avalonia.csproj");
        dockerRunner.Should().Contain("Executable = \"FreeW.Validation.Avalonia\"");
    }

    private static string RepoFile(string relativePath) =>
        TestWorkspaceFileLocator.Find(relativePath);
}
