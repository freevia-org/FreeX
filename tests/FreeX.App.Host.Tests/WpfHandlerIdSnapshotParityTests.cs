using System;
using System.IO;
using System.Linq;

using FluentAssertions;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Keeps the portable WPF handler-id snapshot (<c>docs/parity/wpf-handler-ids.txt</c>) byte-for-byte in
/// lock-step with the live <see cref="FreeXRibbonHandlerMap"/>. The cross-platform functional-parity gate
/// (in the App.Avalonia.Tests lane, which cannot reference the Windows-only App.Host assembly) reads that
/// snapshot as the authoritative "what the WPF shell handles" set; this guard guarantees the snapshot can
/// never drift away from the generated map. If this fails, regenerate the snapshot from
/// <c>FreeXRibbonHandlerMap.Handlers.Keys</c> (sorted, ordinal, one per line, trailing newline).
/// </summary>
public class WpfHandlerIdSnapshotParityTests
{
    [Fact]
    public void Snapshot_MatchesLiveHandlerMap_Exactly()
    {
        var path = SnapshotPath();
        File.Exists(path).Should().BeTrue($"the WPF handler-id snapshot must exist at {path}");

        var expected = FreeXRibbonHandlerMap.Handlers.Keys
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToArray();

        var actual = File.ReadAllLines(path)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToArray();

        actual.Should().Equal(expected,
            "docs/parity/wpf-handler-ids.txt must list exactly FreeXRibbonHandlerMap.Handlers.Keys (sorted ordinal)");
    }

    private static string SnapshotPath() =>
        TestWorkspaceFileLocator.FindFromWorkspaceRoot("docs", "parity", "wpf-handler-ids.txt");
}
