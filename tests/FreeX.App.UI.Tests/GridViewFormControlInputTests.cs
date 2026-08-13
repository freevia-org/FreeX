using System.Windows;
using FluentAssertions;
using FreeX.App.UI;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.UI.Tests;

/// <summary>
/// Tests that <see cref="GridView.FormControlClicked"/> fires with the correct region
/// when a form control is clicked at specific positions.
/// </summary>
public sealed class GridViewFormControlInputTests
{
    private static GridView CreateGrid(params FormControlModel[] controls)
    {
        var grid = new GridView
        {
            Width = 320,
            Height = 200,
            ShowHeaders = false,
            ShowGridLines = false,
            Viewport = new ViewportModel(
                [],
                [
                    new RowMetric(1, 30, 0),
                    new RowMetric(2, 30, 30),
                    new RowMetric(3, 30, 60),
                    new RowMetric(4, 30, 90),
                    new RowMetric(5, 30, 120),
                ],
                [
                    new ColMetric(1, 80, 0),
                    new ColMetric(2, 80, 80),
                    new ColMetric(3, 80, 160),
                    new ColMetric(4, 80, 240),
                ]),
            FormControls = controls,
        };

        grid.Measure(new Size(320, 200));
        grid.Arrange(new Rect(0, 0, 320, 200));
        grid.UpdateLayout();
        return grid;
    }

    private static GridRange Anchor(uint startRow, uint startCol, uint endRow, uint endCol)
    {
        var sheet = SheetId.New();
        return new GridRange(
            new CellAddress(sheet, startRow, startCol),
            new CellAddress(sheet, endRow, endCol));
    }

    [Fact]
    public void Click_CheckBox_FiresFormControlClickedWithBodyRegion()
    {
        WpfTestThread.Run(() =>
        {
            FormControlClickEventArgs? captured = null;
            var control = new FormControlModel
            {
                Kind = FormControlKind.CheckBox,
                IsChecked = false,
                LinkedCell = "A1",
                Anchor = Anchor(1, 1, 1, 3),
            };

            var grid = CreateGrid(control);
            grid.FormControlClicked += (_, e) => captured = e;

            // Simulate a click in the middle of row 1, which is where the checkbox is anchored.
            // Row 1 top=0, height=30: center Y ≈ 15.  Col 1 left=0, width=80: center X ≈ 40.
            grid.SimulateFormControlClick(new Point(10, 15));

            captured.Should().NotBeNull("the checkbox was hit");
            captured!.Control.Should().BeSameAs(control);
            captured.Gesture.Should().Be(FormControlGesture.Body);
        });
    }

    [Fact]
    public void Click_SpinnerUpHalf_FiresStepUpRegion()
    {
        WpfTestThread.Run(() =>
        {
            FormControlClickEventArgs? captured = null;
            var control = new FormControlModel
            {
                Kind = FormControlKind.Spinner,
                Value = 5,
                Min = 1,
                Max = 10,
                Increment = 1,
                LinkedCell = "B2",
                Anchor = Anchor(2, 2, 2, 2),
            };

            var grid = CreateGrid(control);
            grid.FormControlClicked += (_, e) => captured = e;

            // Spinner is at col2 (left=80), row2 (top=30, height=30).
            // Upper half = top=30..45.  Center of upper half: x=88, y=37.
            grid.SimulateFormControlClick(new Point(88, 37));

            captured.Should().NotBeNull();
            captured!.Control.Should().BeSameAs(control);
            captured.Gesture.Should().Be(FormControlGesture.StepUp);
        });
    }

    [Fact]
    public void Click_SpinnerDownHalf_FiresStepDownRegion()
    {
        WpfTestThread.Run(() =>
        {
            FormControlClickEventArgs? captured = null;
            var control = new FormControlModel
            {
                Kind = FormControlKind.Spinner,
                Value = 5,
                Min = 1,
                Max = 10,
                Increment = 1,
                LinkedCell = "B2",
                Anchor = Anchor(2, 2, 2, 2),
            };

            var grid = CreateGrid(control);
            grid.FormControlClicked += (_, e) => captured = e;

            // Lower half of spinner: x=88, y=52 (in bottom half of row 2: 30+15..30+30).
            grid.SimulateFormControlClick(new Point(88, 52));

            captured.Should().NotBeNull();
            captured!.Control.Should().BeSameAs(control);
            captured.Gesture.Should().Be(FormControlGesture.StepDown);
        });
    }

    [Fact]
    public void Click_OutsideAnyControl_DoesNotFireEvent()
    {
        WpfTestThread.Run(() =>
        {
            var fired = false;
            var control = new FormControlModel
            {
                Kind = FormControlKind.CheckBox,
                IsChecked = false,
                LinkedCell = "A1",
                Anchor = Anchor(1, 1, 1, 2),
            };

            var grid = CreateGrid(control);
            grid.FormControlClicked += (_, _) => fired = true;

            // Click far outside the control (row 4, col 4 area)
            grid.SimulateFormControlClick(new Point(250, 100));

            fired.Should().BeFalse("click was outside all form controls");
        });
    }

    [Fact]
    public void Click_GroupBox_DoesNotFireEvent()
    {
        WpfTestThread.Run(() =>
        {
            var fired = false;
            var control = new FormControlModel
            {
                Kind = FormControlKind.GroupBox,
                Caption = "Options",
                Anchor = Anchor(1, 1, 3, 3),
            };

            var grid = CreateGrid(control);
            grid.FormControlClicked += (_, _) => fired = true;

            // GroupBox is not interactive — click in the middle
            grid.SimulateFormControlClick(new Point(80, 45));

            fired.Should().BeFalse("GroupBox is non-interactive");
        });
    }
}

/// <summary>
/// Test-only extension to drive <see cref="GridView"/> form-control hit testing without
/// requiring a real WPF mouse event.
/// </summary>
internal static class GridViewFormControlTestExtensions
{
    /// <summary>
    /// Calls the internal form-control click path directly, bypassing the WPF mouse-event
    /// machinery.  The <see cref="GridView.FormControlClicked"/> event is fired if a control
    /// is hit.
    /// </summary>
    public static void SimulateFormControlClick(this GridView grid, Point pos)
    {
        // Access via reflection since TryHandleFormControlClick is private.
        var method = typeof(GridView).GetMethod(
            "TryHandleFormControlClick",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        method.Should().NotBeNull("TryHandleFormControlClick must exist on GridView");
        method!.Invoke(grid, [pos]);
    }
}
