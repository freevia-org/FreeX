using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using FluentAssertions;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// r324: the shell must survive being rebuilt, not merely built.
///
/// <para>The Avalonia shell has a known crash class: re-parenting a long-lived control without
/// detaching it from its previous parent first, which Avalonia rejects at runtime. Three sites guard
/// it explicitly (<c>if (_newSheetButton.Parent is Panel parent)</c> and friends) against
/// thirty-six places that add a field-held control to a panel -- and the difference between the safe
/// majority and the dangerous few is whether the path can run TWICE.</para>
///
/// <para>The existing launch guard, <c>MainWindowLaunchTests</c>, constructs the window once and
/// lays it out once. So the dimension that decides whether this class fires -- how many times a
/// region has been rebuilt -- was never varied, and a missing detach on a rebuild path could not be
/// caught by any test. This drives each internal rebuild seam twice, with a layout pass between, so
/// the second pass meets controls the first pass already parented.</para>
///
/// <para>A source scan was the alternative and was rejected: thirty-six add-sites cannot be
/// classified textually into "runs once" and "runs again", and guessing would have produced a list
/// of mostly-safe sites -- the failure mode r316's raw census demonstrated.</para>
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class R324_ShellRebuildDoesNotThrowTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public async Task RebuildingTheShellRepeatedlyDoesNotThrow()
    {
        Exception? thrown = null;
        var passes = 0;

        await Session.Dispatch(() =>
        {
            try
            {
                var window = new MainWindow([]);
                window.Measure(new Size(1120, 720));
                window.Arrange(new Rect(0, 0, 1120, 720));
                window.UpdateLayout();

                // Twice, not once: the first call may still be building regions the constructor left
                // empty. The second is the one that meets an already-parented control.
                for (var pass = 0; pass < 2; pass++)
                {
                    window.ApplyQuickAccessToolbarChanged();
                    window.RefreshFromSharedWorkbook();
                    window.RefreshWindowVisibilityCommandStates();

                    window.Measure(new Size(1120, 720));
                    window.Arrange(new Rect(0, 0, 1120, 720));
                    window.UpdateLayout();
                    passes++;
                }

                window.AllowCloseWithoutDirtyPromptForParityCapture();
                window.Close();
            }
            catch (Exception ex)
            {
                thrown = ex;
            }
        }, CancellationToken.None);

        passes.Should().Be(2, "both rebuild passes must have run for this to mean anything");
        thrown.Should().BeNull(
            "a rebuild that re-parents a control Avalonia still considers attached throws at "
            + "runtime, and only a second pass over the same region can reach that");
    }
}
