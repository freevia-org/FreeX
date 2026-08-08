using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Input;
using FluentAssertions;
using FreeX.App.Avalonia;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Wave 105 closes the high-resolution pointer-wheel residual found by comparing the concrete WPF
/// and Avalonia worksheet routes. WPF preserves a coalesced raw delta through the shared planner;
/// Avalonia must consume the equivalent pointer-notch count rather than reducing it to a sign.
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class Wave105_AvaloniaWheelDeltaParityTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public async Task CoalescedPointerWheelDelta_PansThreeTimesTheSingleNotchDistance()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            var originalTopRow = window.Session.ViewportOrigin.TopRow;
            var pointer = new Pointer(1, PointerType.Mouse, isPrimary: true);

            RaiseWheel(window, pointer, -1);
            var singleNotchTopRow = window.Session.ViewportOrigin.TopRow;

            window.Session.SetViewportOrigin(originalTopRow, window.Session.ViewportOrigin.LeftCol);
            RaiseWheel(window, pointer, -3);
            var coalescedTopRow = window.Session.ViewportOrigin.TopRow;

            (singleNotchTopRow - originalTopRow).Should().BeGreaterThan(0);
            (coalescedTopRow - originalTopRow).Should().Be((singleNotchTopRow - originalTopRow) * 3,
                "a Linux pointer event carrying three wheel notches must pan three times the WPF-equivalent single-notch distance");

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
        }, CancellationToken.None);
    }

    private static void RaiseWheel(MainWindow window, Pointer pointer, double verticalDelta)
    {
        var args = new PointerWheelEventArgs(
            window,
            pointer,
            window.SheetGridHostForTest,
            new Point(10, 10),
            0,
            new PointerPointProperties(),
            KeyModifiers.None,
            new Vector(0, verticalDelta));

        window.RaisePointerWheelChangedForTest(args);
    }
}
