using FluentAssertions;
using System.Windows.Threading;
using Xunit;

namespace FreeX.App.Host.Tests;

/// <summary>
/// r444: the shared test harness's <c>PumpDispatcher</c> must actually leave the dispatcher idle.
///
/// <para>Every UI test in this lane calls it and then asserts on what the window did, so its promise
/// -- "the pending work has run" -- is load-bearing for the whole lane. It posts a sentinel at
/// <see cref="DispatcherPriority.Background"/> and pushes a frame until that sentinel runs, which
/// drains everything at Background or above and NOTHING below it: <c>ContextIdle</c>,
/// <c>ApplicationIdle</c> and <c>SystemIdle</c> all survive the pump.</para>
///
/// <para>A test whose assertion depends on work posted at one of those priorities therefore passes
/// or fails according to timing rather than behaviour. That is not hypothetical here: the full lane
/// produced exactly one such failure during r443's verification --
/// <c>R62_NameBoxStructuredTableTests.NameBoxEnter_WithExistingTableName...</c> found the selection
/// still at A1 after Enter was handled -- and the same test passed alone and passed on a re-run of
/// the whole lane. This test does not claim to explain that failure; it pins the harness gap that
/// makes such a failure possible, which is provable on its own.</para>
/// </summary>
public sealed class R444_PumpDispatcherDrainsIdleWorkTests
{
    [Fact]
    public void PumpDispatcherRunsWorkPostedBelowBackgroundPriority()
    {
        StaTestRunner.Run(() =>
        {
            var contextIdleRan = false;
            var applicationIdleRan = false;

            Dispatcher.CurrentDispatcher.BeginInvoke(
                DispatcherPriority.ContextIdle, new Action(() => contextIdleRan = true));
            Dispatcher.CurrentDispatcher.BeginInvoke(
                DispatcherPriority.ApplicationIdle, new Action(() => applicationIdleRan = true));

            R49MainWindowTestHarness.PumpDispatcher();

            contextIdleRan.Should().BeTrue(
                "every UI test in this lane treats PumpDispatcher as \"the window has settled\", so " +
                "work the window posted at ContextIdle must have run before the test asserts on it");
            applicationIdleRan.Should().BeTrue(
                "ApplicationIdle is lower still, and a pump that leaves it queued makes any " +
                "assertion depending on it pass or fail by timing rather than by behaviour");
        });
    }

    [Fact]
    public void PumpDispatcherRunsWorkPostedByAlreadyQueuedWork()
    {
        // The other half: a single pass is not enough even at one priority, because a queued item
        // may post more work as it runs -- which is how a UI settles in practice, one stage handing
        // off to the next. A pump that stops after the first sentinel returns mid-settle.
        StaTestRunner.Run(() =>
        {
            var secondStageRan = false;

            Dispatcher.CurrentDispatcher.BeginInvoke(
                DispatcherPriority.Background,
                new Action(() => Dispatcher.CurrentDispatcher.BeginInvoke(
                    DispatcherPriority.Background, new Action(() => secondStageRan = true))));

            R49MainWindowTestHarness.PumpDispatcher();

            secondStageRan.Should().BeTrue(
                "work posted by work the pump itself ran must also complete, or the harness reports " +
                "settled while the window is still halfway through reacting");
        });
    }
}
