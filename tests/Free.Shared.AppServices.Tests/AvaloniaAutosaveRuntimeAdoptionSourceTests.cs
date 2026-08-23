namespace Free.Shared.AppServices.Tests;

public sealed class AvaloniaAutosaveRuntimeAdoptionSourceTests
{
    [Fact]
    public void AllAvaloniaApps_UseTheSharedEmergencyFanOut()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");
        var sources = new[]
        {
            File.ReadAllText(Path.Combine(root, "src", "FreeX.App.Avalonia", "App.cs")),
            File.ReadAllText(Path.Combine(root, "freew", "FreeW.App.Avalonia", "AutosaveAdapter.cs")),
            File.ReadAllText(Path.Combine(root, "freep", "FreeP.App.Avalonia", "AutosaveAdapter.cs")),
        };

        sources.Should().OnlyContain(source => source.Contains("EmergencySnapshotFanOut<", StringComparison.Ordinal));
        sources.Should().OnlyContain(source => source.Contains("EmergencySnapshots.TrySnapshotAll()", StringComparison.Ordinal));
        sources.Should().OnlyContain(source => !source.Contains("ActiveAdaptersGate", StringComparison.Ordinal));
        sources.Should().OnlyContain(source => !source.Contains("ActiveCoordinatorsGate", StringComparison.Ordinal));
    }

    [Fact]
    public void SisterAvaloniaAdapters_UseSharedPeriodicAndBoundedTransactionRuntimes()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");
        var sources = new[]
        {
            File.ReadAllText(Path.Combine(root, "freew", "FreeW.App.Avalonia", "AutosaveAdapter.cs")),
            File.ReadAllText(Path.Combine(root, "freep", "FreeP.App.Avalonia", "AutosaveAdapter.cs")),
        };

        foreach (var source in sources)
        {
            source.Should().Contain("AutosavePeriodicTaskLoop")
                .And.Contain("AvaloniaBoundedDispatcherTransaction.TryExecute(")
                .And.Contain("_session.Snapshot")
                .And.Contain("_session.TryEmergencySnapshot")
                .And.NotContain("ManualResetEventSlim")
                .And.NotContain("RunLoopAsync(")
                .And.NotContain("CancellationTokenSource");
        }
    }
}
