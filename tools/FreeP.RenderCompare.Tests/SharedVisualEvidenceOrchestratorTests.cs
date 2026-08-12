using Free.ToolsShared;

namespace FreeP.RenderCompare.Tests;

public sealed class SharedVisualEvidenceOrchestratorTests
{
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
