using System.Threading;
using System.Threading.Tasks;

using Avalonia.Headless;

using FluentAssertions;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Regression coverage for R83-services-doc-recovery-props-5-1 (src/FreeX.App.Avalonia/MainWindow.cs).
/// Before the fix, <c>ApplyReadOnlyRecommendedPromptIfNeeded</c> (see
/// R75_ProtectionSelectionAndReadOnlyPromptTests) set <c>_isWorkbookReadOnly</c> on open but nothing
/// ever consulted it again: <c>SaveCurrentWorkbookAsync</c> resolved straight to the existing path via
/// <c>_session.CanSaveCurrentSource</c> and would have silently overwritten the very file the user had
/// just told FreeX to treat as read-only -- the identical gap the WPF host had. <c>ResolveExistingSaveTarget</c>
/// now withholds the existing-path target whenever the session is marked read-only, which makes
/// <c>SaveCurrentWorkbookAsync</c> fall through to the Save-As dialog instead.
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class R83_ReadOnlyWorkbookSaveEnforcementTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public async Task ResolveExistingSaveTarget_ReadOnlySession_ReturnsNull_EvenWithAResolvableExistingPath()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            try
            {
                window.Session.MarkSaved(@"C:\fake\Budget.xlsx");
                window.SetWorkbookReadOnlyForTest(true);

                var target = window.ResolveExistingSaveTargetForTest();

                target.Should().BeNull(
                    "a session marked read-only by ApplyReadOnlyRecommendedPromptIfNeeded must never " +
                    "resolve back to its own path -- Save must fall through to Save-As instead of " +
                    "silently overwriting the protected file");
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();

                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ResolveExistingSaveTarget_EditableSession_StillResolvesTheExistingPath()
    {
        // Sibling/no-regression case: an ordinary (non-read-only) session with a resolvable existing
        // path must keep resolving to it, exactly as before this fix -- only the read-only session's
        // behavior changed.
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            try
            {
                const string path = @"C:\fake\Budget.xlsx";
                window.Session.MarkSaved(path);
                window.SetWorkbookReadOnlyForTest(false);

                var target = window.ResolveExistingSaveTargetForTest();

                target.Should().NotBeNull(
                    "an editable session with a resolvable existing path must still Save-over it directly");
                target!.Path.Should().Be(path);
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();

                window.Close();
            }
        }, CancellationToken.None);
    }
}
