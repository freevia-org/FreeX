using System.Windows.Threading;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class StaTestRunnerClipboardIsolationTests
{
    [WindowsClipboardFact]
    public void RunClipboardIsolated_DoesNotCarryQueuedDispatcherWorkIntoNextRun()
    {
        var staleCallbackRan = false;
        var firstThreadId = 0;
        var secondThreadId = 0;

        StaTestRunner.RunClipboardIsolated(() =>
        {
            firstThreadId = Environment.CurrentManagedThreadId;
            Dispatcher.CurrentDispatcher.BeginInvoke(
                DispatcherPriority.Background,
                new Action(() => staleCallbackRan = true));
        });

        StaTestRunner.RunClipboardIsolated(() =>
        {
            secondThreadId = Environment.CurrentManagedThreadId;
            R49MainWindowTestHarness.PumpDispatcher();
        });

        staleCallbackRan.Should().BeFalse(
            "queued work from a completed clipboard test must not execute in a later test");
        secondThreadId.Should().NotBe(
            firstThreadId,
            "each clipboard-isolated run must own a fresh STA lifecycle");
    }
}
