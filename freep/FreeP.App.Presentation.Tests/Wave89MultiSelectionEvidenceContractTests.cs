using System.Text.Json;

namespace FreeP.App.Compositor.Tests;

public sealed class Wave89MultiSelectionEvidenceContractTests
{
    [Fact]
    public void Wave89Schema_RequiresNinePhysicalRowsAndPackageStates()
    {
        using var document = JsonDocument.Parse(ReadWorkspaceFile(
            "tools", "LinuxInteractiveDocker", "freep-multiselect-x11-wave89-validation.schema.json"));
        var root = document.RootElement;

        root.GetProperty("properties").GetProperty("schemaVersion").GetProperty("const").GetInt32().Should().Be(1);
        root.GetProperty("properties").GetProperty("suite").GetProperty("const").GetString()
            .Should().Be("freep-linux-multiselect-x11-wave89-physical");
        root.GetProperty("properties").GetProperty("results").GetProperty("minItems")
            .GetInt32().Should().Be(9);
        root.GetProperty("properties").GetProperty("results").GetProperty("maxItems")
            .GetInt32().Should().Be(9);
        root.GetProperty("properties").GetProperty("packageStates").GetProperty("required")
            .EnumerateArray().Select(value => value.GetString()).Should().Contain(new[]
            {
                "baseline", "afterResize", "afterRotate", "afterUndo", "afterEscape", "afterCaptureLoss",
            });
    }

    [Fact]
    public void Wave89Probe_ParsesBothShapesAndChecksExactPersistedStates()
    {
        var probe = ReadWorkspaceFile(
            "tools", "LinuxInteractiveDocker", "run-freep-multiselect-x11-wave89-probe.sh");

        probe.Should().Contain("xdotool keydown ctrl");
        probe.Should().Contain("xdotool mousedown 1");
        probe.Should().Contain("assert_package_state");
        probe.Should().Contain("ctrl-z-restores-resize");
        probe.Should().Contain("escape-cancel-preserves-package");
        probe.Should().Contain("capture-loss-cancel-preserves-package");
        probe.Should().Contain("after-rotate.json");
        probe.Should().Contain("after-undo.sha256.txt");
        probe.Should().Contain("Wave89 Left");
        probe.Should().Contain("Wave89 Right");
        probe.Should().Contain("\"rotation\":90.0");
    }

    private static string ReadWorkspaceFile(params string[] relativeParts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var parts = new string[relativeParts.Length + 1];
            parts[0] = directory.FullName;
            relativeParts.CopyTo(parts, 1);
            var candidate = Path.Combine(parts);
            if (File.Exists(candidate))
                return File.ReadAllText(candidate);
            directory = directory.Parent;
        }

        throw new FileNotFoundException("Could not locate workspace file.", Path.Combine(relativeParts));
    }
}
