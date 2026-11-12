namespace FreeX.App.Host.Tests;

/// <summary>
/// Marks a test whose expectations are derived from the retained foreground-capture evidence under
/// <c>tools/foreground-captures</c>. That directory is local-only (it is git-ignored since screenshots
/// and raw parity evidence were taken out of the repository), so on a clean checkout -- which is what
/// hosted CI has -- the artifact catalog legitimately reports more missing artifacts than the
/// assertions expect and the test would fail for a reason that says nothing about the code.
///
/// The skip is decided at discovery time because xunit 2.9.3 has no dynamic skip (throwing
/// SkipException surfaces as a failure). The test still runs, and still fails on a real regression,
/// on any machine that holds the retained evidence.
/// </summary>
internal sealed class RequiresRetainedForegroundEvidenceFactAttribute : FactAttribute
{
    private const string RetainedExcelOpenDialogManifest =
        "tools/foreground-captures/excel-open-dialog/excel-open-dialog_manifest.json";

    public RequiresRetainedForegroundEvidenceFactAttribute()
    {
        if (!RetainedEvidenceIsPresent())
        {
            Skip = "The retained foreground-capture evidence (tools/foreground-captures, local-only and git-ignored) " +
                   "is not present on this machine, so the artifact catalog cannot match this test's expectations.";
        }
    }

    private static bool RetainedEvidenceIsPresent()
    {
        try
        {
            // Same root the test hands the script, so a linked worktree is judged by its own tree.
            return File.Exists(Path.Combine(WorkspaceFileLocator.FindWorkspaceRoot(), RetainedExcelOpenDialogManifest));
        }
        catch (Exception)
        {
            // The locator throws when it cannot find the workspace root.
            return false;
        }
    }
}