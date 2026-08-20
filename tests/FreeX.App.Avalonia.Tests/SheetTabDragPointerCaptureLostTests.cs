using System.Reflection;
using Avalonia.Headless;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

// Covers finding shared-drag-drop F2: an aborted sheet-tab drag (pointer capture lost while the
// left button is still down, e.g. Alt-Tab mid-drag) must NOT commit the reorder. Only a genuine
// PointerReleased-driven drop may move the sheet. Mirrors CellSelectionCapturePointerCaptureLost's
// "capture lost means the drag was interrupted" contract for the sheet-tab drag gesture.
[Collection("AvaloniaHeadless")]
public sealed class SheetTabDragPointerCaptureLostTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public async Task CaptureLostMidDrag_AbortsWithoutMovingTheSheet()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            window.Show();
            window.Measure(new global::Avalonia.Size(1120, 720));
            window.Arrange(new global::Avalonia.Rect(0, 0, 1120, 720));

            try
            {
                window.Session.AddSheet();
                window.Session.AddSheet();
                var namesBefore = window.Session.SheetTabs.Select(t => t.Id).ToArray();
                namesBefore.Length.Should().BeGreaterThanOrEqualTo(3);

                var draggedId = namesBefore[0];
                var targetIndex = namesBefore.Length - 1;

                // Simulate a drag in progress: pointer down on the first tab, moved far enough to
                // compute a pending reorder target, then capture revoked (Alt-Tab) -- no
                // PointerReleased ever fires.
                SetField(window, "_sheetTabDragId", (SheetId?)draggedId);
                SetField(window, "_sheetTabDragPendingToIndex", (int?)targetIndex);

                InvokeCaptureLost(window);

                window.Session.SheetTabs.Select(t => t.Id).Should().Equal(namesBefore,
                    "an interrupted drag (capture lost while the button was never released) must not reorder sheets");
                GetField<SheetId?>(window, "_sheetTabDragId").Should().BeNull(
                    "drag state must be cleared even though nothing was committed");
                GetField<int?>(window, "_sheetTabDragPendingToIndex").Should().BeNull();
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task NormalRelease_StillCommitsTheReorder()
    {
        // Sibling/no-regression case: a completed drop (PointerReleased path, exercised here via
        // the same commit routine PointerReleased calls) must still move the sheet. The fix only
        // removes the commit from the capture-lost abort path.
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            window.Show();
            window.Measure(new global::Avalonia.Size(1120, 720));
            window.Arrange(new global::Avalonia.Rect(0, 0, 1120, 720));

            try
            {
                window.Session.AddSheet();
                window.Session.AddSheet();
                var namesBefore = window.Session.SheetTabs.Select(t => t.Id).ToArray();
                namesBefore.Length.Should().BeGreaterThanOrEqualTo(3);

                var draggedId = namesBefore[0];
                var targetIndex = namesBefore.Length - 1;

                SetField(window, "_sheetTabDragId", (SheetId?)draggedId);
                SetField(window, "_sheetTabDragPendingToIndex", (int?)targetIndex);

                InvokeCompleteRelease(window);

                window.Session.SheetTabs[^1].Id.Should().Be(draggedId,
                    "a genuine mouse-up drop must still commit the pending reorder");
                GetField<SheetId?>(window, "_sheetTabDragId").Should().BeNull();
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();
                window.Close();
            }
        }, CancellationToken.None);
    }

    private static void InvokeCaptureLost(MainWindow window) =>
        typeof(MainWindow)
            .GetMethod("SheetTabDragPointerCaptureLost", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(window, [null, null]);

    private static void InvokeCompleteRelease(MainWindow window) =>
        typeof(MainWindow)
            .GetMethod("CompleteSheetTabPointerRelease", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(window, []);

    private static void SetField<T>(MainWindow window, string name, T value) =>
        typeof(MainWindow)
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(window, value);

    private static T GetField<T>(MainWindow window, string name) =>
        (T)typeof(MainWindow)
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(window)!;
}
