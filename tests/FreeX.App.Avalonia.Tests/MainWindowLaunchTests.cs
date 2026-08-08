using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using FluentAssertions;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Regression guard that the worksheet grid and its top-level selection overlay can be constructed
/// without visual-parent or layout exceptions.
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class MainWindowLaunchTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public async Task MainWindow_Constructor_DoesNotThrow_WhenBuildingSheetGrid()
    {
        // Arrange & Act: constructing MainWindow synchronously builds the default grid and selection overlay.
        Exception? thrown = null;
        await Session.Dispatch(() =>
        {
            try
            {
                var window = new MainWindow([]);
                window.Measure(new Size(1120, 720));
                window.Arrange(new Rect(0, 0, 1120, 720));
                window.UpdateLayout();

                window.AllowCloseWithoutDirtyPromptForParityCapture();

                window.Close();
            }
            catch (Exception ex)
            {
                thrown = ex;
            }
        }, CancellationToken.None);

        // Assert
        thrown.Should().BeNull("MainWindow must construct its worksheet and selection overlay cleanly");
    }
}
