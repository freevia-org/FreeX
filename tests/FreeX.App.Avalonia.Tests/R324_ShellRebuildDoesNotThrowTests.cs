using FreeX.Core.Model;
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
    /// <summary>
    /// r325: the slicer/timeline pane's rebuild, which the test above does NOT reach.
    ///
    /// <para>Probing r324 showed that removing the pane's detach guard did not fail it: the pane only
    /// builds its header when the active sheet actually has a slicer, and <c>RefreshFromSharedWorkbook</c>
    /// returns early while the window is not visible. Both conditions are met here -- the window is
    /// shown and a slicer anchored to the active sheet is added -- so the second refresh meets a close
    /// button the first refresh already parented. That is the exact sequence the pane's own comment
    /// describes: "a plain window resize is enough ... the second refresh with the pane open would
    /// take the shell down".</para>
    /// </summary>
    [Fact]
    public async Task RebuildingTheSlicerPaneTwiceDoesNotThrow()
    {
        Exception? thrown = null;
        var refreshes = 0;

        await Session.Dispatch(() =>
        {
            try
            {
                var window = new MainWindow([]);
                window.Show();
                window.Measure(new Size(1120, 720));
                window.Arrange(new Rect(0, 0, 1120, 720));
                window.UpdateLayout();

                var sheet = window.Session.ActiveSheet;
                window.Session.Workbook.Slicers.Add(new SlicerModel
                {
                    Name = "r325 Slicer",
                    CacheName = "Slicer_r325",
                    SourceFieldName = "Region",
                    DrawingAnchor = new DrawingAnchorRange(
                        new DrawingAnchorPoint(6, 0, 1, 0),
                        new DrawingAnchorPoint(9, 0, 8, 0)),
                    SourceSheetName = sheet.Name,
                });

                for (var pass = 0; pass < 2; pass++)
                {
                    window.RefreshFromSharedWorkbook();
                    window.Measure(new Size(1120, 720));
                    window.Arrange(new Rect(0, 0, 1120, 720));
                    window.UpdateLayout();
                    refreshes++;
                }

                window.AllowCloseWithoutDirtyPromptForParityCapture();
                window.Close();
            }
            catch (Exception ex)
            {
                thrown = ex;
            }
        }, CancellationToken.None);

        refreshes.Should().Be(2, "the second refresh is the one that meets an already-parented button");
        thrown.Should().BeNull(
            "rebuilding the pane must detach its reused close button from the previous header first");
    }

}