using System;
using System.IO;
using System.Linq;

using FluentAssertions;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Keeps the portable legacy WPF inventory readable while the live ribbon uses typed semantic ids.
/// The snapshot remains a cross-platform parity artifact; it is no longer runtime execution metadata.
/// </summary>
public class WpfHandlerIdSnapshotParityTests
{
    [Fact]
    public void Snapshot_RemainsLegacyEvidence_WhileRuntimeIdsStaySemantic()
    {
        var path = SnapshotPath();
        File.Exists(path).Should().BeTrue($"the WPF handler-id snapshot must exist at {path}");

        var actual = File.ReadAllLines(path)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToArray();

        actual.Should().NotBeEmpty();
        actual.Should().Contain("100%#Zoom100Btn_Click",
            "the prohibited docs lane intentionally remains the legacy parity inventory");
        MainWindow.FreeXRibbonHandlers.Keys.Should().OnlyContain(id =>
            !id.Contains('#', StringComparison.Ordinal) &&
            !id.Contains("_Click", StringComparison.Ordinal));
    }

    private static string SnapshotPath() =>
        TestWorkspaceFileLocator.FindFromWorkspaceRoot("docs", "parity", "wpf-handler-ids.txt");
}
