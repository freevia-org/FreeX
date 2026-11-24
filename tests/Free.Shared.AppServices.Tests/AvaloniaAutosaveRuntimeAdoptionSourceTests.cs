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
            // FreeW and FreeP share one host, which owns the fan-out registration.
            File.ReadAllText(Path.Combine(root, "shared", "Free.Shared.Shell.Avalonia", "AvaloniaAutosaveHost.cs")),
        };

        sources.Should().OnlyContain(source => source.Contains("EmergencySnapshotFanOut<", StringComparison.Ordinal));
        sources.Should().OnlyContain(source => source.Contains("EmergencySnapshots.TrySnapshotAll()", StringComparison.Ordinal));
        sources.Should().OnlyContain(source => !source.Contains("ActiveAdaptersGate", StringComparison.Ordinal));
        sources.Should().OnlyContain(source => !source.Contains("ActiveCoordinatorsGate", StringComparison.Ordinal));
    }

    [Fact]
    public void FreePAvaloniaAdapter_WordsTheStartupOfferLikeItsWpfHost()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");
        var wpfHost = File.ReadAllText(Path.Combine(
            root, "shared", "Free.Shared.Shell.Wpf", "WpfAutosaveHost.cs"));
        var freepAdapter = File.ReadAllText(Path.Combine(
            root, "freep", "FreeP.App.Avalonia", "AutosaveAdapter.cs"));

        // FreeP's WPF host (the reference) uses the unquoted Startup wording; its Avalonia adapter must
        // opt out of the shared Avalonia default (quoted display name) rather than drift from it.
        wpfHost.Should().Contain("AutosaveRecoveryPromptMode.Startup)");
        freepAdapter.Should().Contain("quoteStartupDisplayName: false");
    }

    [Fact]
    public void SisterAvaloniaAdapters_UseSharedPeriodicAndBoundedTransactionRuntimes()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");
        var sharedHost = File.ReadAllText(Path.Combine(
            root, "shared", "Free.Shared.Shell.Avalonia", "AvaloniaAutosaveHost.cs"));
        sharedHost.Should().Contain("AutosavePeriodicTaskLoop")
            .And.Contain("AvaloniaBoundedDispatcherTransaction.TryExecute(")
            .And.Contain("Session.Snapshot")
            .And.Contain("Session.TryEmergencySnapshot")
            .And.NotContain("ManualResetEventSlim")
            .And.NotContain("RunLoopAsync(")
            .And.NotContain("CancellationTokenSource");

        var adapters = new[]
        {
            File.ReadAllText(Path.Combine(root, "freew", "FreeW.App.Avalonia", "AutosaveAdapter.cs")),
            File.ReadAllText(Path.Combine(root, "freep", "FreeP.App.Avalonia", "AutosaveAdapter.cs")),
        };
        foreach (var adapter in adapters)
        {
            adapter.Should().Contain("AvaloniaAutosaveHost<")
                .And.NotContain("AutosavePeriodicTaskLoop")
                .And.NotContain("AvaloniaBoundedDispatcherTransaction")
                .And.NotContain("EmergencySnapshotFanOut<");
        }
    }
}
