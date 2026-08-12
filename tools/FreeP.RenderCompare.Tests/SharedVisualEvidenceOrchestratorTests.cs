using Free.ToolsShared;

namespace FreeP.RenderCompare.Tests;

public sealed class SharedVisualEvidenceOrchestratorTests
{
    [Fact]
    public void Tool_temporary_directory_owns_contained_paths_and_cleanup()
    {
        string directoryPath;
        using (var directory = new ToolTemporaryDirectory("shared-tool-temp-"))
        {
            directoryPath = directory.Path;
            var outputPath = directory.GetPath(Path.Combine("nested", "artifact.json"));
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            File.WriteAllText(outputPath, "evidence");

            outputPath.Should().StartWith(directoryPath)
                .And.EndWith(Path.Combine("nested", "artifact.json"));
        }

        Directory.Exists(directoryPath).Should().BeFalse();
    }

    [Fact]
    public void Tool_temporary_directory_rejects_paths_outside_its_lease()
    {
        using var directory = new ToolTemporaryDirectory("shared-tool-temp-guards-");

        var rooted = () => directory.GetPath(Path.GetFullPath("outside.txt"));
        var traversal = () => directory.GetPath(Path.Combine("..", "outside.txt"));

        rooted.Should().Throw<ArgumentException>();
        traversal.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void App_io_bench_uses_the_shared_temporary_directory_owner()
    {
        var root = RepositoryRootLocator.Find(AppContext.BaseDirectory, "FreeX.slnx")!;
        var source = File.ReadAllText(Path.Combine(root, "tools", "FreeX.AppIoBench", "Program.cs"));
        var project = File.ReadAllText(Path.Combine(
            root,
            "tools",
            "FreeX.AppIoBench",
            "FreeX.AppIoBench.csproj"));

        source.Should().Contain("new ToolTemporaryDirectory(\"freex-app-io-bench-\")")
            .And.Contain("temporaryOutput!.GetPath(\"output.xlsx\")")
            .And.NotContain("class TemporaryOutputFile");
        project.Should().Contain("Free.ToolsShared\\Free.ToolsShared.csproj");
    }

    [Fact]
    public void Run_directory_owns_creation_and_cleanup()
    {
        string path;
        using (var directory = new VisualEvidenceRunDirectory("shared-visual-evidence-"))
        {
            path = directory.Path;
            Directory.Exists(path).Should().BeTrue();
            File.WriteAllText(Path.Combine(path, "artifact.txt"), "evidence");
        }

        Directory.Exists(path).Should().BeFalse();
    }

    [Fact]
    public void Process_plan_quotes_structured_arguments_and_validates_timeout()
    {
        var plan = VisualEvidenceProcessPlan.Create(
            "capture host.exe",
            Path.GetTempPath(),
            ["--output", Path.Combine(Path.GetTempPath(), "capture output"), "--scenario", "review.comments"],
            TimeSpan.FromSeconds(45),
            "capture process tree");

        plan.Arguments.Should().Contain(@"""--output""")
            .And.Contain($@"""{Path.Combine(Path.GetTempPath(), "capture output")}""")
            .And.Contain(@"""--scenario"" ""review.comments""");
        plan.TimeoutMilliseconds.Should().Be(45_000);

        var invalid = () => VisualEvidenceProcessPlan.Create(
            "capture.exe",
            Path.GetTempPath(),
            [],
            TimeSpan.Zero,
            "capture process tree");
        invalid.Should().Throw<ArgumentOutOfRangeException>();
    }
}
