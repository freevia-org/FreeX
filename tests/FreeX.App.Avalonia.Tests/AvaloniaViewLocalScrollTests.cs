using System.Reflection;
using System.Threading;
using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using FreeX.App.Presentation.GridInteraction;
using FreeX.App.Presentation.Shell;
using FreeX.App.Services;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

[Collection("AvaloniaHeadless")]
public sealed class AvaloniaViewLocalScrollTests
{
    private static readonly HeadlessUnitTestSession Headless =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public Task SiblingWheelAndOneAxisThumbChange_PreserveItsOwnOrigin() => Run((first, second) =>
    {
        SetOrigin(first, 10, 20);
        SetOrigin(second, 40, 30);
        var firstCell = first.Session.ActiveCell;
        var secondCell = second.Session.ActiveCell;
        var history = (second.Session.CanUndo, second.Session.CanRedo, second.Session.IsDirty);

        Wheel(second);
        second.Session.ViewportOrigin.Should().Be((43u, 30u));
        Vertical(second).Value.Should().Be(43);
        Refresh(second);
        Vertical(second).Value.Should().Be(43, "a sibling refresh must retain its own scroll origin");
        Horizontal(second).Value = 35;
        second.Session.ViewportOrigin.Should().Be((43u, 35u), "a one-axis gesture must preserve the other axis");
        first.Session.ViewportOrigin.Should().Be((10u, 20u));
        first.Session.ActiveSheet.ViewTopRow.Should().Be(10);
        first.Session.ActiveCell.Should().Be(firstCell);
        second.Session.ActiveCell.Should().Be(secondCell);
        (second.Session.CanUndo, second.Session.CanRedo, second.Session.IsDirty).Should().Be(history);
    });

    [Fact]
    public Task ScrollbarValues_UseEachViewsFreezeCounts() => Run((first, second) =>
    {
        FreezeFirstOnly(first, second);
        SetOrigin(first, 20, 10);
        SetOrigin(second, 20, 10);
        Vertical(first).Value.Should().Be(15);
        Horizontal(first).Value.Should().Be(7);
        Vertical(second).Value.Should().Be(20);
        Horizontal(second).Value.Should().Be(10);

        Vertical(first).Value = 25;
        Horizontal(first).Value = 15;
        first.Session.ViewportOrigin.Should().Be((30u, 18u));
        Vertical(second).Value = 25;
        Horizontal(second).Value = 15;
        second.Session.ViewportOrigin.Should().Be((25u, 15u));
        Refresh(first);
        Refresh(second);
        Vertical(first).Value.Should().Be(25);
        Vertical(second).Value.Should().Be(25);
        first.Session.ViewportOrigin.Should().Be((30u, 18u));
    });

    [Fact]
    public Task CapturedDrag_UsesTheSiblingsOriginAndFreezeCounts() => Run((first, second) =>
    {
        FreezeFirstOnly(first, second);
        SetOrigin(first, 20, 10);
        SetOrigin(second, 40, 20);
        var selection = second.Session.SelectedRange;
        var history = (second.Session.CanUndo, second.Session.CanRedo, second.Session.IsDirty);
        second.RaiseCellDragAutoScrollForTest(new GridAutoScrollRequest(1, 1));
        second.Session.ViewportOrigin.Should().Be((41u, 21u));
        Vertical(second).Value.Should().Be(41);
        Horizontal(second).Value.Should().Be(21);
        first.Session.ViewportOrigin.Should().Be((20u, 10u));
        second.Session.SelectedRange.Should().Be(selection);
        (second.Session.CanUndo, second.Session.CanRedo, second.Session.IsDirty).Should().Be(history);
    });

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public Task SynchronizedScroll_TransfersAbsoluteWorksheetOriginsInBothDirections(bool frozenSender) => Run((first, second) =>
    {
        FreezeFirstOnly(first, second);
        var sender = frozenSender ? first : second;
        var receiver = frozenSender ? second : first;
        SetOrigin(sender, 20, 10);
        SetOrigin(receiver, 40, 30);
        var selections = (first.Session.SelectedRange, second.Session.SelectedRange);
        var history = (first.Session.CanUndo, first.Session.CanRedo, first.Session.IsDirty);
        Coordinator.Enable(first, second).Should().BeTrue();
        Coordinator.SetSynchronousScroll(true).Should().BeTrue();

        sender.BroadcastScrollOffsetToSideBySidePartner();
        receiver.Session.ViewportOrigin.Should().Be((20u, 10u));
        Vertical(receiver).Value.Should().Be(frozenSender ? 20 : 15);
        Horizontal(receiver).Value.Should().Be(frozenSender ? 10 : 7);
        sender.Session.ViewportOrigin.Should().Be((20u, 10u));

        // A real thumb event in the receiving window becomes the reverse-direction sender.
        Vertical(receiver).Value += 7;
        receiver.Session.ViewportOrigin.Should().Be((27u, 10u));
        sender.Session.ViewportOrigin.Should().Be((27u, 10u));
        Wheel(sender);
        first.Session.ViewportOrigin.Should().Be((30u, 10u));
        second.Session.ViewportOrigin.Should().Be((30u, 10u));
        (first.Session.SelectedRange, second.Session.SelectedRange).Should().Be(selections);
        (first.Session.CanUndo, first.Session.CanRedo, first.Session.IsDirty).Should().Be(history);
    });

    [Fact]
    public Task SynchronizedScroll_ClampsOnlyTheReceivingWindowWithoutEcho() => Run((first, second) =>
    {
        FreezeFirstOnly(first, second);
        SetOrigin(first, 20, 10);
        SetOrigin(second, 2, 2);
        Coordinator.Enable(first, second);
        Coordinator.SetSynchronousScroll(true);
        second.BroadcastScrollOffsetToSideBySidePartner();
        first.Session.ViewportOrigin.Should().Be((6u, 4u));
        second.Session.ViewportOrigin.Should().Be((2u, 2u), "the frozen-boundary clamp must not echo");

        Coordinator.SetSynchronousScroll(false);
        Vertical(first).Maximum = 4;
        Horizontal(first).Maximum = 5;
        SetOrigin(second, 100, 50);
        Coordinator.SetSynchronousScroll(true);
        second.BroadcastScrollOffsetToSideBySidePartner();
        first.Session.ViewportOrigin.Should().Be((9u, 8u));
        second.Session.ViewportOrigin.Should().Be((100u, 50u), "the receiver range clamp must not echo");
    });

    [Fact]
    public Task DisabledSyncAndUnpairedWindow_DoNotMoveThePair() => Run((first, second) =>
    {
        var third = first.CreateSharedViewForTest();
        try
        {
            Show(third);
            SetOrigin(first, 20, 10);
            SetOrigin(second, 40, 20);
            SetOrigin(third, 60, 30);
            Coordinator.Enable(first, second);
            Vertical(first).Value = 25;
            second.Session.ViewportOrigin.Should().Be((40u, 20u));
            Coordinator.SetSynchronousScroll(true);
            Wheel(third);
            third.Session.ViewportOrigin.Should().Be((63u, 30u));
            first.Session.ViewportOrigin.Should().Be((25u, 10u));
            second.Session.ViewportOrigin.Should().Be((40u, 20u));
        }
        finally { Close(third); }
    });

    [Fact]
    public Task PageDown_UsesTheSiblingsVisibleRowsAndRevealsWithItsOwnThumb() => RunAsync(async (first, second) =>
    {
        FreezeFirstOnly(first, second);
        SetOrigin(second, 1, 1);
        second.Session.SelectCell(new CellAddress(second.Session.ActiveSheet.Id, 1, 1));
        Refresh(second);
        var visibleRows = second.Session.Viewport.RowMetrics.Count;
        visibleRows.Should().BeGreaterThan(6);
        second.Session.Viewport.RowMetrics.Count(m => m.Row > first.Session.ActiveSheet.FrozenRows)
            .Should().BeLessThan(visibleRows, "the shared freeze state must differ for this regression to be meaningful");
        var firstCell = first.Session.ActiveCell;
        var history = (second.Session.CanUndo, second.Session.CanRedo, second.Session.IsDirty);
        await second.RaiseKeyDownForTest(new KeyEventArgs { Key = Key.PageDown });
        second.Session.ActiveCell.Row.Should().Be((uint)visibleRows);
        second.Session.ActiveCell.Col.Should().Be(1);
        Vertical(second).Value.Should().Be(second.Session.ViewportOrigin.TopRow);
        first.Session.ActiveCell.Should().Be(firstCell);
        (second.Session.CanUndo, second.Session.CanRedo, second.Session.IsDirty).Should().Be(history);
    });

    [Theory]
    [InlineData("ScrollActiveCellIntoView", true)]
    [InlineData("ScrollActiveCellIntoView", false)]
    [InlineData("EnsureValidationCircleAddressVisible", true)]
    [InlineData("EnsureValidationCircleAddressVisible", false)]
    [InlineData("EnsureInlineEditorAddressVisible", true)]
    [InlineData("EnsureInlineEditorAddressVisible", false)]
    public Task OneAxisReveal_PreservesTheSiblingsOtherAxis(string method, bool horizontalOnly) => Run((first, second) =>
    {
        SetOrigin(first, 10, 10);
        var address = new CellAddress(second.Session.ActiveSheet.Id,
            horizontalOnly ? 45u : 90u, horizontalOnly ? 90u : 32u);
        second.Session.SelectCell(address);
        SetOrigin(second, 40, 30); // Scroll away from the selected cell before invoking a reveal route.
        second.Session.Viewport.RowMetrics.Any(m => m.Row == address.Row).Should().Be(horizontalOnly);
        second.Session.Viewport.ColMetrics.Any(m => m.Col == address.Col).Should().Be(!horizontalOnly);
        var selection = second.Session.SelectedRange;
        var history = (second.Session.CanUndo, second.Session.CanRedo, second.Session.IsDirty);

        typeof(MainWindow).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(second, method == "ScrollActiveCellIntoView" ? null : [address]);
        second.Session.ViewportOrigin.Should().Be(horizontalOnly ? (40u, 90u) : (90u, 30u));
        first.Session.ViewportOrigin.Should().Be((10u, 10u));
        second.Session.SelectedRange.Should().Be(selection);
        (second.Session.CanUndo, second.Session.CanRedo, second.Session.IsDirty).Should().Be(history);
    });

    private static Task Run(Action<MainWindow, MainWindow> test) => RunAsync((first, second) =>
    {
        test(first, second);
        return Task.CompletedTask;
    });

    private static Task RunAsync(Func<MainWindow, MainWindow, Task> test) => Headless.Dispatch(async () =>
    {
        var options = new AppOptions();
        var first = new MainWindow([], null!, new FreeXOptionsRuntimeSession(
            load: () => options.Clone(), save: static _ => true));
        MainWindow? second = null;
        try
        {
            var sheet = first.Session.ActiveSheet;
            sheet.SetCell(new CellAddress(sheet.Id, 500, 100), new TextValue("scroll extent"));
            second = first.CreateSharedViewForTest();
            Show(first);
            Show(second);
            await test(first, second);
        }
        finally
        {
            Coordinator.Disable();
            if (second is not null) Close(second);
            Close(first);
        }
        return true; // Select Func<Task<T>> so async assertion failures propagate out of Dispatch.
    }, CancellationToken.None);

    private static void FreezeFirstOnly(MainWindow first, MainWindow second)
    {
        first.Session.SetFreezePanes(5, 3).Success.Should().BeTrue();
        first.Session.GetEffectiveFrozenRows().Should().Be(5);
        second.Session.GetEffectiveFrozenRows().Should().Be(0);
        second.Session.GetEffectiveFrozenCols().Should().Be(0);
        Refresh(first);
        Refresh(second);
    }

    private static void Show(MainWindow window)
    {
        window.Show();
        window.Measure(new Size(1120, 720));
        window.Arrange(new Rect(0, 0, 1120, 720));
        window.Session.UpdateViewportSize(240, 640);
        Refresh(window);
    }

    private static void SetOrigin(MainWindow window, uint row, uint col)
    {
        window.Session.SetViewportOrigin(row, col);
        Refresh(window);
    }

    private static void Refresh(MainWindow window) => typeof(MainWindow)
        .GetMethod("RefreshShellForViewportPan", BindingFlags.Instance | BindingFlags.NonPublic)!
        .Invoke(window, ["Ready"]);

    private static ScrollBar Vertical(MainWindow window) => Field<ScrollBar>(window, "_verticalWorksheetScrollBar");
    private static ScrollBar Horizontal(MainWindow window) => Field<ScrollBar>(window, "_horizontalWorksheetScrollBar");
    private static T Field<T>(MainWindow window, string name) => (T)typeof(MainWindow)
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
    private static WorkbookSideBySideCoordinator<MainWindow> Coordinator => (WorkbookSideBySideCoordinator<MainWindow>)typeof(MainWindow)
        .GetField("SideBySideCoordinator", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
    private static void Close(MainWindow window)
    {
        window.AllowCloseWithoutDirtyPromptForParityCapture();
        window.Close();
    }
    private static void Wheel(MainWindow window)
    {
        var args = new PointerWheelEventArgs(window, new global::Avalonia.Input.Pointer(1, PointerType.Mouse, true),
            window.SheetGridHostForTest, new Point(100, 100), 0, new PointerPointProperties(), KeyModifiers.None, new Vector(0, -1));
        window.RaisePointerWheelChangedForTest(args);
        args.Handled.Should().BeTrue();
    }
}
