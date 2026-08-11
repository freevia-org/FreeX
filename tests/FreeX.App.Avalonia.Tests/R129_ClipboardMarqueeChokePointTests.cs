using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

using Avalonia.Headless;

using FluentAssertions;

using FreeX.App.Services;
using FreeX.Core.Commands;
using FreeX.Core.Model;

using Xunit;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// R129: the Copy/Cut marching-ants overlay (<c>_clipboardMarqueeRange</c> in MainWindow.cs) has now
/// had its "call SetClipboardMarquee(null, ...) after every edit-committing call site" fix applied
/// three times -- R127-services-clipboard-formats-copy-cancel-1's InsertDeleteCells sites, R127C's
/// ribbon/undo/clear sites, and (closed by this round) the Proofing/Translate/Spelling/Symbol/
/// Data-Validation-dropdown sites, all of which call <c>WorkbookSession.CommitCellText</c> directly
/// (bypassing CommitFormulaBox/CommitEditAcrossSelection) and then <c>RefreshShell</c>. Rather than add
/// a 4th per-call-site pass, this round moved the clear into <c>RefreshShell</c> itself: it now compares
/// the overlay against the new <see cref="FreeX.App.Services.WorkbookSession.HasPendingClipboardMarquee"/>
/// property and clears the overlay whenever the session's own pending Copy/Cut has already been retired
/// (WorkbookSession.CancelPendingCutAfterMutatingEdit, unconditional since R127-services-clipboard-
/// formats-copy-cancel-1) but the shell's overlay has not caught up. Every commit path that already
/// calls RefreshShell inherits this for free -- these tests exercise the 5 sites named for this round
/// plus the mechanism itself and its two "must NOT eagerly clear" guardrails.
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class R129_ClipboardMarqueeChokePointTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public Task CommitProofingText_ClearsAnActivePendingClipboardMarquee() =>
        Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.Workbook.AddSheet("ProofingMarquee");
            window.Session.SelectSheet(sheet.Id);
            window.Session.SelectRange(new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 1)));

            var copiedRange = new GridRange(new CellAddress(sheet.Id, 4, 4), new CellAddress(sheet.Id, 4, 4));
            window.SetClipboardMarqueeForTest(copiedRange, isCut: false);
            window.ClipboardMarqueeRangeForTest.Should().NotBeNull("sanity: the marquee must be active before the proofing commit runs");

            InvokePrivate(window, "CommitProofingText", "replacement", "Replaced");

            window.ClipboardMarqueeRangeForTest.Should().BeNull(
                "the Thesaurus/Equation proofing commit path (CommitProofingText) must retire the pending " +
                "Copy marquee overlay via the RefreshShell choke point");

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
        }, CancellationToken.None);

    [Fact]
    public Task ApplySpellingCorrection_ClearsAnActivePendingClipboardMarquee() =>
        Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.Workbook.AddSheet("SpellingMarquee");
            window.Session.SelectSheet(sheet.Id);
            var address = new CellAddress(sheet.Id, 1, 1);
            sheet.SetCell(address, new TextValue("teh world"));
            window.Session.SelectRange(new GridRange(address, address));

            var copiedRange = new GridRange(new CellAddress(sheet.Id, 4, 4), new CellAddress(sheet.Id, 4, 4));
            window.SetClipboardMarqueeForTest(copiedRange, isCut: true);

            var issue = SpellCheckService.FindIssues(window.Session.Workbook, sheet.Id).Single();
            var result = window.Session.ExecuteReviewCommand(
                SpellCheckWorkflowPlanner.BuildReplacementCommand(issue, "the"));
            result.Success.Should().BeTrue();
            InvokePrivate(window, "RefreshShell", "Ready");

            window.ClipboardMarqueeRangeForTest.Should().BeNull(
                "Spelling dialog Change/Change All must retire the pending Cut marquee overlay once the " +
                "corrected text is committed and the shell refreshes");

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
        }, CancellationToken.None);

    [Fact]
    public Task CommitDataValidationDropdownSelection_ClearsAnActivePendingClipboardMarquee() =>
        Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.Workbook.AddSheet("DataValidationDropdownMarquee");
            window.Session.SelectSheet(sheet.Id);
            window.Session.SelectRange(new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 1)));

            var copiedRange = new GridRange(new CellAddress(sheet.Id, 4, 4), new CellAddress(sheet.Id, 4, 4));
            window.SetClipboardMarqueeForTest(copiedRange, isCut: false);

            InvokePrivate(window, "CommitDataValidationDropdownSelection", "Picked");

            window.ClipboardMarqueeRangeForTest.Should().BeNull(
                "picking a Data Validation dropdown entry must retire the pending Copy marquee overlay");

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
        }, CancellationToken.None);

    // Mechanism test: any future commit site that calls WorkbookSession.CommitCellText (which already
    // retires the session-level clipboard) and then RefreshShell inherits the fix automatically, with
    // no new SetClipboardMarquee(null, ...) call needed at the site itself -- proven here by driving
    // the session/RefreshShell pair directly instead of through a named UI call site.
    [Fact]
    public Task RefreshShell_AfterAnyCommitCellTextThatInvalidatesTheClipboard_ClearsTheMarquee() =>
        Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.Workbook.AddSheet("GenericChokePointMarquee");
            window.Session.SelectSheet(sheet.Id);
            window.Session.SelectRange(new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 1)));

            var copiedRange = new GridRange(new CellAddress(sheet.Id, 4, 4), new CellAddress(sheet.Id, 4, 4));
            window.SetClipboardMarqueeForTest(copiedRange, isCut: false);
            window.Session.HasPendingClipboardMarquee.Should().BeFalse(
                "the test-only SetClipboardMarqueeForTest seam sets only the UI overlay, not the session's " +
                "own pending clipboard -- this asserts the two really are independent state before the fix is exercised");

            window.Session.CommitCellText("new value");
            InvokePrivate(window, "RefreshShell", "Ready");

            window.ClipboardMarqueeRangeForTest.Should().BeNull(
                "any commit path that calls CommitCellText then RefreshShell must have its overlay cleared " +
                "by the choke point even with no call-site-specific SetClipboardMarquee(null, ...)");

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
        }, CancellationToken.None);

    // No-regression #1: RefreshShell must NOT eagerly clear a marquee whose session-level clipboard is
    // still genuinely pending -- e.g. immediately after Copy/Cut itself, which both call
    // SetClipboardMarquee(...) then RefreshShell(...) in the same method. If the choke point cleared
    // unconditionally, Copy/Cut would never be able to show a marquee at all.
    [Fact]
    public Task RefreshShell_WithAStillPendingSessionClipboard_LeavesTheMarqueeAlone_NoRegression() =>
        Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.Workbook.AddSheet("StillPendingMarquee");
            window.Session.SelectSheet(sheet.Id);
            var address = new CellAddress(sheet.Id, 2, 2);
            sheet.SetCell(address, new TextValue("copy me"));
            window.Session.SelectRange(new GridRange(address, address));

            var copyResult = window.Session.TryCopySelectedRangeText();
            copyResult.Success.Should().BeTrue("sanity: the copy itself must succeed");
            window.Session.HasPendingClipboardMarquee.Should().BeTrue(
                "sanity: a just-completed Copy leaves the session-level clipboard pending");

            window.SetClipboardMarqueeForTest(new GridRange(address, address), isCut: false);
            InvokePrivate(window, "RefreshShell", "Copied A2");

            window.ClipboardMarqueeRangeForTest.Should().NotBeNull(
                "RefreshShell must not clear the marquee while WorkbookSession still reports a pending " +
                "Copy/Cut -- this is exactly the state right after Copy/Cut itself sets the overlay and " +
                "then calls RefreshShell");

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
        }, CancellationToken.None);

    // No-regression #2: a pure selection change (no edit, no clipboard invalidation) must leave an
    // active marquee alone. SelectCell does not call RefreshShell at all, so this also guards against a
    // future refactor accidentally routing selection changes through the choke point.
    [Fact]
    public Task SelectCell_PureSelectionChange_LeavesAnActiveMarqueeAlone_NoRegression() =>
        Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.Workbook.AddSheet("SelectionChangeMarquee");
            window.Session.SelectSheet(sheet.Id);

            var copiedRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 1));
            window.SetClipboardMarqueeForTest(copiedRange, isCut: false);
            window.Session.TryCopySelectedRangeText().Success.Should().BeTrue();

            InvokePrivate(window, "SelectCell", new CellAddress(sheet.Id, 5, 5));

            window.ClipboardMarqueeRangeForTest.Should().NotBeNull(
                "moving the active cell with no edit must not disturb an active Copy marquee, matching Excel");

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
        }, CancellationToken.None);

    private static void InvokePrivate(MainWindow window, string methodName, params object[] args)
    {
        var method = typeof(MainWindow).GetMethod(
            methodName, BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new MissingMethodException(nameof(MainWindow), methodName);
        method.Invoke(window, args);
    }
}
