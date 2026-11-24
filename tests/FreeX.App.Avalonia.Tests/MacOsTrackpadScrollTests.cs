using System.Threading;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Input;

namespace FreeX.App.Avalonia.Tests;

[Collection("AvaloniaHeadless")]
public sealed class MacOsTrackpadScrollTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public void FractionalPackets_PreserveTheSameDistanceAsOneWholeNotch()
    {
        var accumulator = new PointerWheelNotchAccumulator();
        Enumerable.Range(0, 8).Sum(_ => accumulator.Accumulate(-0.125)).Should().Be(-1);
        accumulator.Accumulate(-3.5).Should().Be(-3);
        accumulator.Accumulate(-0.5).Should().Be(-1);
        accumulator.Reset();
        Enumerable.Range(0, 10).Sum(_ => accumulator.Accumulate(0.1)).Should().Be(1);
    }

    [Fact]
    public void DirectionChangesAndReset_DiscardThePreviousGestureTail()
    {
        var accumulator = new PointerWheelNotchAccumulator();
        accumulator.Accumulate(0.75).Should().Be(0);
        accumulator.Accumulate(-0.5).Should().Be(0);
        accumulator.Accumulate(-0.5).Should().Be(-1);
        accumulator.Accumulate(-0.75).Should().Be(0);
        accumulator.Reset();
        accumulator.Accumulate(-0.25).Should().Be(0);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(0)]
    public void NonMotionPackets_DoNotPoisonPendingMotion(double delta)
    {
        var accumulator = new PointerWheelNotchAccumulator();
        accumulator.Accumulate(0.5).Should().Be(0);
        accumulator.Accumulate(delta).Should().Be(0);
        accumulator.Accumulate(0.5).Should().Be(1);
    }

    [Theory]
    [InlineData(double.MaxValue, int.MaxValue)]
    [InlineData(-double.MaxValue, -int.MaxValue)]
    public void ExcessiveMotion_SaturatesWithoutReversingDirection(double delta, int expected)
    {
        var accumulator = new PointerWheelNotchAccumulator();
        accumulator.Accumulate(delta).Should().Be(expected);
        accumulator.Accumulate(0.25).Should().Be(0);
    }

    [Fact]
    public Task SmallVerticalPackets_PanOnceForTheCombinedNotch() => Run(window =>
    {
        var before = window.Session.ViewportOrigin;
        for (var i = 0; i < 7; i++)
            RaiseWheel(window, new Vector(0, -0.125)).Handled.Should().BeTrue();
        window.Session.ViewportOrigin.Should().Be(before);

        RaiseWheel(window, new Vector(0, -0.125));
        window.Session.ViewportOrigin.TopRow.Should().Be(before.TopRow + 3);
        window.Session.ViewportOrigin.LeftCol.Should().Be(before.LeftCol);
    });

    [Fact]
    public Task DiagonalPackets_UseTheDominantRawAxis() => Run(window =>
    {
        var before = window.Session.ViewportOrigin;
        for (var i = 0; i < 4; i++)
        {
            RaiseWheel(window, new Vector(-0.25, -0.125));
            RaiseWheel(window, default);
        }
        window.Session.ViewportOrigin.LeftCol.Should().Be(before.LeftCol + 3);
        window.Session.ViewportOrigin.TopRow.Should().Be(before.TopRow);
    });

    [Fact]
    public Task ShiftWheel_AccumulatesVerticalMotionAsHorizontalScroll() => Run(window =>
    {
        var before = window.Session.ViewportOrigin;
        for (var i = 0; i < 4; i++)
            RaiseWheel(window, new Vector(-0.01, -0.25), KeyModifiers.Shift);
        window.Session.ViewportOrigin.LeftCol.Should().Be(before.LeftCol + 3);
        window.Session.ViewportOrigin.TopRow.Should().Be(before.TopRow);
    });

    [Fact]
    public Task CommandWheel_DoesNotInheritPendingScrollOrAmplifyTinyZoomEvents() => Run(window =>
    {
        var before = window.Session.ViewportOrigin;
        var zoomBefore = window.Session.ZoomPercent;
        RaiseWheel(window, new Vector(0, -0.75));
        for (var i = 0; i < 3; i++)
            RaiseWheel(window, new Vector(0, 0.25), KeyModifiers.Meta);
        window.Session.ZoomPercent.Should().Be(zoomBefore);
        RaiseWheel(window, new Vector(0, 0.25), KeyModifiers.Meta);
        window.Session.ZoomPercent.Should().Be(zoomBefore + 10);
        RaiseWheel(window, new Vector(0, -0.25));
        window.Session.ViewportOrigin.Should().Be(before);
    });

    private static Task Run(Action<MainWindow> test)
    {
        if (!OperatingSystem.IsMacOS())
            return Task.CompletedTask;

        return Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            try { test(window); }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();
                window.Close();
            }
        }, CancellationToken.None);
    }

    private static PointerWheelEventArgs RaiseWheel(MainWindow window, Vector delta, KeyModifiers modifiers = KeyModifiers.None)
    {
        var args = new PointerWheelEventArgs(window, new Pointer(1, PointerType.Mouse, isPrimary: true),
            window.SheetGridHostForTest, new Point(10, 10), 0, new PointerPointProperties(), modifiers, delta);
        window.RaisePointerWheelChangedForTest(args);
        return args;
    }
}
