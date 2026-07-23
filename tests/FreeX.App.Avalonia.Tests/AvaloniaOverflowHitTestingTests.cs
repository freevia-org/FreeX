using System.Reflection;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using FluentAssertions;
using FreeX.App.Services;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

[Collection("AvaloniaHeadless")]
public sealed class AvaloniaOverflowHitTestingTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public async Task OverflowingCellText_DoesNotCapturePointerOverAdjacentCell()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.Workbook.AddSheet("OverflowHitTestFixture");
            window.Session.SelectSheet(sheet.Id);

            var source = new CellAddress(sheet.Id, 13, 7);
            var target = new CellAddress(sheet.Id, 13, 8);
            sheet.SetCell(source, new TextValue("X11CopyPaste"));
            window.Session.UpdateViewportSize(881, 1440);

            window.Show();
            window.Measure(new Size(1120, 720));
            window.Arrange(new Rect(0, 0, 1120, 720));
            Refresh(window);
            window.Session.SelectCell(source);
            Refresh(window);
            window.UpdateLayout();

            try
            {
                window.Session.ActiveCell.Should().Be(source);
                var sourceBorder = FindByAutomationId<Border>(window, "Cell_G13");
                var sourceText = sourceBorder.GetVisualDescendants()
                    .OfType<TextBlock>()
                    .Single(text => text.Text == "X11CopyPaste");
                sourceBorder.Child.Should().NotBeNull();
                sourceBorder.Child!.IsHitTestVisible.Should().BeFalse(
                    "the rendered cell-content host is not an independent worksheet hit target");
                sourceText.Text.Should().Be("X11CopyPaste");

                var targetBorder = FindByAutomationId<Border>(window, "Cell_H13");
                targetBorder.IsHitTestVisible.Should().BeTrue();
                var targetPoint = targetBorder.TranslatePoint(
                    new Point(targetBorder.Bounds.Width / 2, targetBorder.Bounds.Height / 2),
                    window);
                targetPoint.Should().NotBeNull();
                targetBorder.IsAttachedToVisualTree().Should().BeTrue();
                targetPoint!.Value.X.Should().BeInRange(0, window.Bounds.Width);
                targetPoint.Value.Y.Should().BeInRange(0, window.Bounds.Height);

                window.MouseMove(targetPoint.Value, RawInputModifiers.None);
                window.MouseDown(
                    targetPoint.Value,
                    MouseButton.Left,
                    RawInputModifiers.LeftMouseButton);
                window.MouseUp(targetPoint.Value, MouseButton.Left, RawInputModifiers.None);

                window.Session.ActiveCell.Should().Be(target,
                    "clicking the calibrated center of H13 must select H13 even when G13 text overflows visually");
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    private static void Refresh(MainWindow window) =>
        typeof(MainWindow)
            .GetMethod("RefreshShell", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(window, ["Ready"]);

    private static T FindByAutomationId<T>(MainWindow window, string automationId)
        where T : Control =>
        window.GetVisualDescendants()
            .OfType<T>()
            .Single(control => AutomationProperties.GetAutomationId(control) == automationId);
}
