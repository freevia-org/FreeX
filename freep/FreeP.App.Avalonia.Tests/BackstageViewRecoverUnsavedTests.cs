namespace FreeP.App.Avalonia.Tests;

/// <summary>
/// The manual "Recover Unsaved Presentations" Backstage command on the Avalonia shell. Unlike the
/// WPF shell (whose Backstage command drives a dedicated
/// <c>AutosaveCoordinator.RecoverUnsavedPresentations</c> that shows an info message when there is
/// nothing to recover and surfaces failures), FreeP's Avalonia shell mirrors FreeW's Avalonia
/// sibling exactly: the manual command just re-runs the same best-effort
/// <see cref="FreeP.App.Avalonia.AutosaveAdapter.OfferRecoveryAsync"/> flow already used at startup,
/// rather than adding a second recovery method. This test pins that wiring at the source level --
/// <see cref="AutosaveAdapterEmergencySnapshotTests"/> already covers
/// <c>OfferRecoveryAsync</c>/<c>TryEmergencySnapshot</c> behavior itself.
/// </summary>
public sealed class BackstageViewRecoverUnsavedTests
{
    [Fact]
    public void MainWindow_WiresRecoverUnsavedIntoTheBackstageEndpoints()
    {
        var source = TestWorkspaceFileLocator.ReadAllText("freep", "FreeP.App.Avalonia", "MainWindow.cs");

        source.Should().Contain("RecoverUnsaved: () => _ = _autosave.OfferRecoveryAsync(this),");
    }

    [Fact]
    public void BackstageView_WiresTheOpenPaneAndBindsRecoverUnsavedThroughDismissBeforeDispatch()
    {
        var source = TestWorkspaceFileLocator.ReadAllText(
            "freep", "FreeP.App.Avalonia", "Backstage", "BackstageView.cs");

        source.Should().Contain("BuildOpenPane = BuildOpenPane,");
        source.Should().Contain("_dismissBeforeDispatch.Bind(_endpoints.RecoverUnsaved)");

        // The Open Backstage entry became a Pane to host the recovery command; it must still bind
        // the plain Open action too, or Backstage > Open silently loses the ability to open a
        // presentation. See PresentationBackstagePanePlannerTests.
        // BuildOpenPane_ExposesBrowseSoTheOpenEntryStillOpensFiles.
        source.Should().Contain("_dismissBeforeDispatch.Bind(_endpoints.Open)");
    }
}
