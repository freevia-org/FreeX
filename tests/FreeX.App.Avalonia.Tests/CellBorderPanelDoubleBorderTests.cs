using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using FluentAssertions;
using FreeX.App.Avalonia;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Headless-Avalonia render tests for <see cref="CellBorderPanel"/>'s handling of
/// <see cref="BorderStyle.Double"/> (R48-reimplementation-twin-sweep-1). Needs the headless
/// platform because Avalonia <see cref="Line"/> shapes require <c>IPlatformRenderInterface</c>
/// when arranged.
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class CellBorderPanelDoubleBorderTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    private static CellStyle StyleWithBottomBorder(BorderStyle style)
    {
        var cellStyle = new CellStyle();
        cellStyle.BorderBottom = new CellBorder(style);
        return cellStyle;
    }

    [Fact]
    public async Task DoubleBottomBorder_RendersTwoParallelLines_NotOneSolidLine()
    {
        // Failure scenario: format a cell's bottom border as "Double" (Format Cells > Border).
        // Real Excel (and the WPF host) render it as two thin parallel lines; pre-fix, the
        // Avalonia shell drew exactly one Line, indistinguishable from Thin.
        await Session.Dispatch(() =>
        {
            var style = StyleWithBottomBorder(BorderStyle.Double);
            var panel = new CellBorderPanel(style);

            panel.Measure(new Size(60, 20));
            panel.Arrange(new Rect(0, 0, 60, 20));

            var lines = panel.Children.OfType<Line>().ToList();
            lines.Should().HaveCount(2,
                "Excel's Double border style must render as two parallel lines, matching the WPF twin's DrawDoubleBorderLines");

            // The two lines must be distinct (straddling the edge), not coincident duplicates.
            lines[0].StartPoint.Y.Should().NotBe(lines[1].StartPoint.Y);
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ThinBottomBorder_StillRendersExactlyOneLine()
    {
        // Sibling no-regression case: an ordinary (non-Double) border style must still draw
        // exactly one line, as before the fix.
        await Session.Dispatch(() =>
        {
            var style = StyleWithBottomBorder(BorderStyle.Thin);
            var panel = new CellBorderPanel(style);

            panel.Measure(new Size(60, 20));
            panel.Arrange(new Rect(0, 0, 60, 20));

            var lines = panel.Children.OfType<Line>().ToList();
            lines.Should().HaveCount(1,
                "non-Double border styles must be unaffected by the Double-border fix");
        }, CancellationToken.None);
    }
}
