using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using FluentAssertions;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Regression guard for the Avalonia launch-blocking crash caused by re-parenting an already-parented control
/// in <c>AddAutofillHandleAdorner</c>.
///
/// Before the fix, constructing <see cref="MainWindow"/> under a headless Avalonia session threw:
///   System.InvalidOperationException: The control Grid already has a visual parent Border while trying
///   to add it as a child of Grid.
///   at MainWindow.AddAutofillHandleAdorner(...) MainWindow.cs:5923
///
/// The fix: set <c>border.Child = null</c> before adding the existing child to the new layer Grid so
/// the control has no visual parent at the moment it is re-parented.
/// </summary>
public sealed class MainWindowLaunchTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public async Task MainWindow_Constructor_DoesNotThrow_WhenBuildingSheetGrid()
    {
        // Arrange & Act: constructing MainWindow synchronously calls BuildSheetGrid() for the default
        // workbook, which calls AddAutofillHandleAdorner for the selected cell (A1).  Before the fix this
        // threw InvalidOperationException due to a double-parent violation in Avalonia.
        Exception? thrown = null;
        await Session.Dispatch(() =>
        {
            try
            {
                var window = new MainWindow([]);
                window.Measure(new Size(1120, 720));
                window.Arrange(new Rect(0, 0, 1120, 720));
                window.UpdateLayout();
                window.Close();
            }
            catch (Exception ex)
            {
                thrown = ex;
            }
        }, CancellationToken.None);

        // Assert
        thrown.Should().BeNull(
            "MainWindow must construct without an InvalidOperationException from AddAutofillHandleAdorner " +
            "re-parenting a Border child before detaching it from its visual parent");
    }
}
