using Xunit;

namespace FreeP.App.Host.Tests;

/// <summary>
/// r474: a recovery that failed must not leave a blank presentation window on screen.
///
/// <para>Both FreeP shells called <c>window.Show()</c> unconditionally and then returned the load
/// result, so a snapshot that would not load still produced a visible, empty window. On startup
/// that path is deliberately "best-effort, silent on failure", so the user got an unexplained blank
/// deck; through the manual "Recover Unsaved Presentations" command -- which exists precisely to
/// surface failures rather than swallow them -- they got a blank window AND an error telling them
/// recovery failed. FreeW's OpenNewWindowWithRecoveredSnapshotAsync already returns without showing
/// anything, so this was the sibling-left-behind shape again, in both FreeP shells at once.</para>
///
/// <para>This is a SOURCE contract, not a behavioural test, and that is a real limitation worth
/// stating: the production path constructs and shows a live top-level window, the host test project
/// has no WPF <c>Application</c> to count windows against, and FreeP's Avalonia suite does not run
/// on this machine. The codebase already uses source contracts for invariants in exactly this
/// position (see AutosaveCoordinatorEmergencySnapshotTests). It pins the ordering that matters --
/// the failure branch returns before any Show() -- and it fails if either shell reverts.</para>
/// </summary>
public sealed class R474_FailedRecoveryShowsNoWindowTests
{
    private static string RecoveryMethod(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        start.Should().BeGreaterThan(-1, $"the recovery entry point '{signature}' must still exist");

        // The method ends at the next member declared at the same indentation.
        var end = source.IndexOf("\r\n    private", start + signature.Length, StringComparison.Ordinal);
        return end < 0 ? source[start..] : source[start..end];
    }

    [Theory]
    [InlineData("FreeP.App.Host", "private bool OpenNewWindowWithRecoveredSnapshot(")]
    [InlineData("FreeP.App.Avalonia", "private Task<bool> OpenNewWindowWithRecoveredSnapshotAsync(")]
    public void AFailedSnapshotLoadReturnsBeforeTheWindowIsShown(string project, string signature)
    {
        var source = TestWorkspaceFileLocator.ReadAllText("freep", project, "MainWindow.cs");
        var method = RecoveryMethod(source, signature);

        // Non-vacuity: the slice must be the real method, not an empty or runaway match.
        method.Should().Contain("RestoreAutosaveSnapshot", "the slice must cover the recovery body");
        method.Should().Contain("Show()", "the success path still has to present the window");

        var failureBranch = method.IndexOf("if (!loaded)", StringComparison.Ordinal);
        var show = method.IndexOf("Show()", StringComparison.Ordinal);

        failureBranch.Should().BeGreaterThan(-1,
            "a snapshot that does not load must be handled explicitly, not shown anyway");
        failureBranch.Should().BeLessThan(show,
            "the failure branch has to return before the window is presented, otherwise a failed " +
            "recovery still puts a blank presentation on screen");
        method.Should().Contain("window.Close()",
            "the window built for the failed recovery must be released, not left constructed and orphaned");
    }
}
