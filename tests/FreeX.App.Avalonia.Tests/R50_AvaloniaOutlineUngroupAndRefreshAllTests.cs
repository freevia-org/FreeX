using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

using Avalonia.Headless;

using FluentAssertions;

using Free.Shared.Ribbon;
using FreeX.Core.Commands;
using FreeX.Core.Model;

using Xunit;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Round-50 findings:
///
/// R50-meta-1 (un-mirrored r49 twin): the Avalonia shell's UngroupSelection() (MainWindow.Outline.cs)
/// issued ONE GroupRowsCommand over the whole selection with a single computed level, force-setting
/// every selected row to that uniform level -- wrongly raising shallower rows (or grouping rows that
/// had no outline level at all) instead of decrementing each row's OWN existing level by one. Fixed
/// by mirroring the WPF host's GetContiguousSameLevelRuns + per-run GroupRowsCommand/GroupColumnsCommand
/// composite (FreeX.App.Host's MainWindow.OutlineCommands.cs).
///
/// (refresh-all/sweep-1): MainWindow.cs's ExtraCommands dictionary initializer assigned
/// ["data.refresh"] TWICE -- once to RefreshImportedData, once (later) to CalculateNow -- so the
/// second silently won and Data ▸ Connections ▸ Refresh All never re-imported the remembered file
/// source. Fixed by removing the duplicate CalculateNow assignment so RefreshImportedData wins.
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class R50_AvaloniaOutlineUngroupAndRefreshAllTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    // R50-meta-1 -------------------------------------------------------------------------------

    [Fact]
    public Task UngroupSelection_MixedLevelRowSelection_DecrementsEachRunsOwnLevel_DoesNotRaiseShallowerRows() =>
        Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.Workbook.AddSheet("MixedLevels");
            window.Session.SelectSheet(sheet.Id);

            // Rows 1-3 grouped at level 1; rows 4-6 grouped at level 3 (deliberately skipping level
            // 2), to prove the fix decrements EACH run's OWN existing level rather than computing a
            // single uniform target from the deepest level found anywhere across the selection.
            window.Session.ExecuteReviewCommand(new GroupRowsCommand(sheet.Id, 1, 3, 1)).Success.Should().BeTrue();
            window.Session.ExecuteReviewCommand(new GroupRowsCommand(sheet.Id, 4, 6, 3)).Success.Should().BeTrue();

            window.Session.SelectRange(new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 6, 1)));

            InvokePrivate(window, "UngroupSelection");

            // Rows 1-3 (own level 1) must drop out of their group entirely (level 0/absent) -- NOT
            // be raised to level 2. Pre-fix, one GroupRowsCommand computed a single level for the
            // whole 1-6 range (deepest existing level 3, minus one = 2) and force-set every selected
            // row -- including the shallower level-1 rows -- to that uniform level 2.
            for (uint row = 1; row <= 3; row++)
            {
                sheet.RowOutlineLevels.ContainsKey(row).Should().BeFalse(
                    $"row {row} was only ever grouped at level 1; Ungroup must drop it out of its " +
                    "group entirely, not raise it to level 2");
            }

            // Rows 4-6 (own level 3) must drop by exactly one, to level 2.
            for (uint row = 4; row <= 6; row++)
            {
                sheet.RowOutlineLevels.TryGetValue(row, out var level).Should().BeTrue();
                level.Should().Be(2, $"row {row} was grouped at level 3; Ungroup must decrement it by exactly one");
            }

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
        }, CancellationToken.None);

    [Fact]
    public Task UngroupSelection_UniformLevelRowSelection_DecrementsAllRowsByOne_NoRegression() =>
        Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.Workbook.AddSheet("UniformLevels");
            window.Session.SelectSheet(sheet.Id);

            // All rows 1-6 grouped at the same level 2 -- a single contiguous same-level run, so
            // both the old whole-range computation and the new per-run computation must agree here.
            window.Session.ExecuteReviewCommand(new GroupRowsCommand(sheet.Id, 1, 6, 2)).Success.Should().BeTrue();

            window.Session.SelectRange(new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 6, 1)));

            InvokePrivate(window, "UngroupSelection");

            for (uint row = 1; row <= 6; row++)
            {
                sheet.RowOutlineLevels.TryGetValue(row, out var level).Should().BeTrue();
                level.Should().Be(1, $"row {row} was uniformly grouped at level 2; Ungroup must decrement it to level 1");
            }

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
        }, CancellationToken.None);

    // (refresh-all/sweep-1) ---------------------------------------------------------------------

    [Fact]
    public Task DataRefreshAllCommand_WithNoRememberedImportSource_ReportsNothingToRefresh_NotRecalculated() =>
        Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            // A fresh, blank sheet/selection so the footer renders the ready/cell-mode text (what
            // RefreshShell sets _statusText to) instead of an aggregate-stats readout, which would
            // otherwise blank _statusText regardless of which handler ran.
            var sheet = window.Session.Workbook.AddSheet("RefreshAllFixture");
            window.Session.SelectSheet(sheet.Id);
            window.Session.SelectRange(new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 1, 1)));

            var registry = window.RibbonCommandRegistryForTest;
            registry.Should().NotBeNull();
            registry!.TryGet(new RibbonCommandId("Refresh All"), out var command).Should().BeTrue();
            command.Should().NotBeNull();

            command!.Execute(RibbonCommandContext.Empty);

            // With no remembered import source, RefreshImportedData reports "nothing to refresh".
            // Pre-fix, the second ["data.refresh"] = CalculateNow dictionary entry silently won, so
            // this would instead report the plain-recalc status below.
            window.StatusTextForTest.Text.Should().Be(
                UiText.Get("GetData_RefreshNoSource"),
                "Refresh All must route to RefreshImportedData, not silently fall through to " +
                "CalculateNow because of the duplicate dictionary key");
            window.StatusTextForTest.Text.Should().NotBe(UiText.Get("ShellLoc_RecalculatedAllFormulas"));

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
        }, CancellationToken.None);

    [Fact]
    public Task DataRefreshAllCommand_IsRegisteredAndLive_OverwritesPriorRecalcStatus_NoRegression() =>
        Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.Workbook.AddSheet("RefreshAllFixture");
            window.Session.SelectSheet(sheet.Id);
            window.Session.SelectRange(new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 1, 1)));

            var registry = window.RibbonCommandRegistryForTest;
            registry.Should().NotBeNull();
            registry!.TryGet(new RibbonCommandId("Refresh All"), out var refreshCommand).Should().BeTrue();
            refreshCommand.Should().NotBeNull();

            // Sanity/no-regression: dirty the status via a plain recalc first, then confirm Refresh
            // All is still live and wired (not merely present-but-inert) by overwriting that status.
            InvokePrivate(window, "CalculateNow");
            window.StatusTextForTest.Text.Should().Be(UiText.Get("ShellLoc_RecalculatedAllFormulas"));

            refreshCommand!.Execute(RibbonCommandContext.Empty);
            window.StatusTextForTest.Text.Should().Be(UiText.Get("GetData_RefreshNoSource"));

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
        }, CancellationToken.None);

    private static void InvokePrivate(MainWindow window, string methodName)
    {
        var method = typeof(MainWindow).GetMethod(
            methodName, BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new System.MissingMethodException(nameof(MainWindow), methodName);
        method.Invoke(window, null);
    }
}
