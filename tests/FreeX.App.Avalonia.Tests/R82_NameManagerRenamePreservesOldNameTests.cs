using FluentAssertions;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Round-82 regression test for finding R82-app-dialog-parity-5-1 (HIGH): the Avalonia Name
/// Manager's Define Name editor, when used to rename an existing name (Edit -> change the Name
/// field -> OK), removed the old entry before
/// defining the new one. The instant that delete commits, every formula referencing the old name
/// by its literal text (FreeX resolves names in formulas by literal text, not by a stable identity
/// that survives a rename) recalculates to #NAME? — nothing rewrites referencing formulas
/// old-name -> new-name on rename. The WPF host's NamedRangeDialog.DefineOrUpdateName deliberately
/// does NOT do this (see its comment), accepting an orphaned second name as the lesser, cosmetic
/// bug instead of silently breaking live formulas. The fix removes the Avalonia dialog's
/// delete-then-recreate step so a rename behaves the same way: the new name is defined, and the
/// old one survives (orphaned but harmless) so any formula still referencing it by text keeps
/// working.
///
/// The OK handler this bug lives in is a private lambda embedded in
/// <c>MainWindow.ShowDefineNameDialogAsync</c> (constructs a real dialog Window/TextBoxes), so the
/// decision itself is not independently extractable into a pure function. This asserts directly
/// against the shipped source text of the OK handler's rename branch — the same technique the
/// codebase already uses for source-hygiene regression guards (see
/// FreeX.App.Host.Tests.MainWindowSourceHygieneTests) — so the test is tied to the exact code path
/// that was fixed and fails on the pre-fix source.
/// </summary>
public sealed class R82_NameManagerRenamePreservesOldNameTests
{
    private static string ReadDefinedNamesSource() =>
        TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot("src", "FreeX.App.Avalonia", "MainWindow.DefinedNames.cs");

    /// <summary>Isolates the Define Name editor's OK handler body (from the "okButton.Click +="
    /// assignment through the paired "cancelButton.Click" line right after it), so assertions about
    /// what the rename path does/doesn't call are anchored to that handler specifically rather than
    /// matching an unrelated occurrence elsewhere in the file.</summary>
    private static string ExtractOkHandlerSource(string source)
    {
        const string start = "okButton.Click += (_, _) =>";
        const string end = "cancelButton.Click += (_, _) => dialog.Close();";

        var startIndex = source.IndexOf(start, StringComparison.Ordinal);
        startIndex.Should().BeGreaterThanOrEqualTo(0, "the Define Name dialog's OK handler must still exist");

        var endIndex = source.IndexOf(end, startIndex, StringComparison.Ordinal);
        endIndex.Should().BeGreaterThan(startIndex, "the OK handler must still be immediately followed by the Cancel handler");

        return source[startIndex..endIndex];
    }

    [Fact]
    public void OkHandler_RenameBranch_DoesNotDeleteTheOldNameBeforeDefiningTheNew()
    {
        var handlerSource = ExtractOkHandlerSource(ReadDefinedNamesSource());

        // This is the exact regression: before the fix, the rename branch called
        // a delete command for the seed's OLD name before ever defining the
        // new one, which — since RemoveNamedRangeCommand actually removes the name and reports the
        // referencing formula cells as affected — recalculates any live formula referencing the old
        // name to #NAME? the instant the rename commits.
        handlerSource.Should().NotContain(
            "definedNames.BuildDeleteCommand(",
            "renaming a defined name must not delete the old entry first — FreeX resolves names in " +
            "formulas by literal text, so removing the old entry before the new one is defined turns " +
            "every formula still referencing the old name into #NAME? the instant the rename commits");
    }

    [Fact]
    public void OkHandler_StillDefinesTheNewNameOrFormula_NoRegression()
    {
        // No-regression sibling: removing the delete-then-recreate step must not have also removed
        // the actual define step — the OK handler must still commit the new name (as a range or a
        // named formula/constant) through the shared session command path either way.
        var handlerSource = ExtractOkHandlerSource(ReadDefinedNamesSource());

        handlerSource.Should().Contain("definedNames.PlanSave(draft, seed?.Identity)");
        handlerSource.Should().Contain("_session.ExecuteReviewCommand(plan.Command!)");
    }
}
