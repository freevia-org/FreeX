using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Controls.Primitives;
using FluentAssertions;
using FreeX.App.Presentation.GridInteraction;
using FreeX.App.Services;

namespace FreeX.App.Avalonia.Tests;

[Collection("AvaloniaHeadless")]
public sealed class AvaloniaDragAutoScrollTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public async Task CapturedDragAutoScroll_AdvancesSharedViewportOriginAndScrollbars()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            window.Show();
            window.Measure(new global::Avalonia.Size(1120, 720));
            window.Arrange(new global::Avalonia.Rect(0, 0, 1120, 720));

            try
            {
                var sheet = window.Session.ActiveSheet;
                window.Session.UpdateViewportSize(20, 10);
                sheet.SetCell(new FreeX.Core.Model.CellAddress(sheet.Id, 200, 20), new FreeX.Core.Model.TextValue("used"));

                var vertical = GetPrivateField<ScrollBar>(window, "_verticalWorksheetScrollBar");
                var horizontal = GetPrivateField<ScrollBar>(window, "_horizontalWorksheetScrollBar");
                ConfigureScrollBar(vertical, maximum: 80, viewportSize: 10, value: 1);
                ConfigureScrollBar(horizontal, maximum: 80, viewportSize: 8, value: 1);
                var initialVerticalValue = vertical.Value;
                var initialHorizontalValue = horizontal.Value;

                window.RaiseCellDragAutoScrollForTest(new GridAutoScrollRequest(1, 1));

                vertical.Value.Should().Be(initialVerticalValue + 1);
                horizontal.Value.Should().Be(initialHorizontalValue + 1);
                var expectedOrigin = WorkbookViewportScrollPlanner.CalculateViewportOrigin(
                    sheet,
                    vertical.Value,
                    horizontal.Value);
                sheet.ViewTopRow.Should().Be(expectedOrigin.TopRow);
                sheet.ViewLeftCol.Should().Be(expectedOrigin.LeftCol);
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public void EdgeIntent_UsesHeaderBoundariesForAllCapturedDragModes()
    {
        GridAutofillPlanner.CalculateEdgeScrollIntent(
                pointerX: 795,
                pointerY: 595,
                width: 800,
                height: 600,
                rowHeaderWidth: 48,
                columnHeaderHeight: 24)
            .Should()
            .Be(new GridAutoScrollRequest(1, 1));

        GridAutofillPlanner.CalculateEdgeScrollIntent(
                pointerX: 400,
                pointerY: 300,
                width: 800,
                height: 600,
                rowHeaderWidth: 48,
                columnHeaderHeight: 24)
            .Should()
            .Be(new GridAutoScrollRequest(0, 0));
    }

    private static void ConfigureScrollBar(ScrollBar scrollBar, double maximum, double viewportSize, double value)
    {
        scrollBar.Minimum = 1;
        scrollBar.Maximum = maximum;
        scrollBar.ViewportSize = viewportSize;
        scrollBar.Value = value;
    }

    private static T GetPrivateField<T>(MainWindow window, string name)
        where T : class =>
        (T)typeof(MainWindow)
            .GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(window)!;
}
