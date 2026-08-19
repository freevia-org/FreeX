namespace FreeP.App.Avalonia.Tests;

/// <summary>
/// The manual "Recover Unsaved Presentations" Backstage command on the Avalonia shell.
///
/// <para>
/// r146-remediation: this used to re-run the same best-effort, UNGATED
/// <see cref="FreeP.App.Avalonia.AutosaveAdapter.OfferRecoveryAsync"/> flow used at silent STARTUP,
/// so accepting an older crash snapshot for the CURRENT window silently overwrote the current
/// presentation's unsaved edits with no save/discard prompt -- the WPF host's
/// <c>AutosaveCoordinator.RecoverUnsavedPresentations</c> never had this bug, since it always routed
/// the current-window restore through <c>PresentationFileCommandSession.ConfirmCloseAllowedAsync</c>.
/// It now drives the dedicated, dirty-gated
/// <see cref="FreeP.App.Avalonia.AutosaveAdapter.RecoverUnsavedPresentationsAsync"/>, matching the
/// WPF host's behavior and mirroring FreeW's Avalonia
/// <c>AutosaveAdapter.RecoverUnsavedDocumentsAsync</c> fix from the same round. This test pins that
/// wiring at the source level -- <see cref="R146_ManualRecoveryDirtyGateTests"/> covers
/// <c>RecoverUnsavedPresentationsAsync</c>'s dirty-gate behavior itself.
/// </para>
/// </summary>
public sealed class BackstageViewRecoverUnsavedTests
{
    [Fact]
    public void MainWindow_WiresRecoverUnsavedIntoTheBackstageEndpoints()
    {
        var source = TestWorkspaceFileLocator.ReadAllText("freep", "FreeP.App.Avalonia", "MainWindow.cs");

        source.Should().Contain("RecoverUnsaved: () => _ = _autosave.RecoverUnsavedPresentationsAsync(this),");
        source.Should().NotContain("RecoverUnsaved: () => _ = _autosave.OfferRecoveryAsync(this),",
            "the manual command must no longer reuse the ungated startup offer");
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
