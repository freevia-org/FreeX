using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using FluentAssertions;
using FreeX.App.Avalonia;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Headless-Avalonia tests for <see cref="CellBorderPanel"/>'s reaction to a RenderScaling (DPI)
/// change that happens without a resize (R116 -- Avalonia cell border geometry is not rebuilt on
/// a RenderScaling change without a resize).
///
/// <para>
/// <c>CellBorderPanel.Build</c>/<c>AddEdge</c> bake the <em>current</em> RenderScaling into
/// pixel-snapped stroke thickness/position (see <c>GetDisplayThickness</c>/<c>BorderStrokePixelSnapper</c>),
/// but pre-fix, <c>ArrangeOverride</c> only rebuilt the border-line children when the panel's final
/// size changed. Dragging a window from one monitor to a differently-scaled one changes
/// <c>TopLevel.RenderScaling</c> without necessarily resizing the panel, so the previously
/// pixel-snapped geometry (computed for the old scale) was left stale -- unlike WPF, which
/// re-invokes <c>OnRender</c> (and re-queries the DPI) automatically on a per-monitor DPI change.
/// </para>
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class R116_CellBorderPanelScalingChangeTests
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
    public async Task R116_ScalingChangeWithoutResize_RebuildsBorderLineAtNewScale()
    {
        // Failure scenario: a Thin bottom border is pixel-snapped to a minimum 1-device-pixel
        // hairline. At RenderScaling 1.0, 0.5 DIP snaps *up* to 1 device px == 1.0 DIP. At
        // RenderScaling 2.0, that same 0.5 DIP authored thickness already *is* exactly 1 device
        // px (0.5 * 2.0), so it snaps to 0.5 DIP with no inflation. Dragging the host window to
        // the higher-DPI monitor (RenderScaling 1.0 -> 2.0) with no resize must therefore change
        // the rendered line's StrokeThickness from 1.0 to 0.5 DIP. Pre-fix, CellBorderPanel never
        // re-ran Build() because ArrangeOverride's rebuild gate only looked at finalSize, so the
        // stale 1.0-DIP-thick line (baked in at the old scale) was left on screen.
        await Session.Dispatch(() =>
        {
            var style = StyleWithBottomBorder(BorderStyle.Thin);
            var panel = new CellBorderPanel(style) { Width = 60, Height = 20 };
            var window = new Window { Content = panel };

            window.Show();
            window.SetRenderScaling(1.0);
            window.Measure(new Size(200, 200));
            window.Arrange(new Rect(0, 0, 200, 200));
            window.UpdateLayout();

            var beforeLine = panel.Children.OfType<Line>().Single();
            beforeLine.StrokeThickness.Should().Be(1.0,
                "at RenderScaling 1.0 a 0.5-DIP Thin border must snap up to a full 1-device-pixel hairline");

            // Simulate dragging the window to a 2x-scaled monitor with no resize at all.
            window.SetRenderScaling(2.0);
            window.UpdateLayout();

            var afterLine = panel.Children.OfType<Line>().Single();
            afterLine.StrokeThickness.Should().Be(0.5,
                "the border geometry must be rebuilt against the new RenderScaling even though the panel's " +
                "arranged size never changed -- otherwise the stale 1.0-DIP-thick line from the old scale " +
                "stays on screen, silently mis-rendering the border thickness on the new monitor");
        }, CancellationToken.None);
    }

    [Fact]
    public async Task R116_ResizeWithConstantScaling_StillRebuildsBorderGeometry()
    {
        // Sibling no-regression case: an ordinary resize at a *constant* RenderScaling (the
        // pre-existing, common path -- e.g. the user widens a column) must still rebuild the
        // border lines exactly as before this fix, unaffected by the new scaling-change wiring.
        await Session.Dispatch(() =>
        {
            var style = StyleWithBottomBorder(BorderStyle.Thin);
            var panel = new CellBorderPanel(style) { Width = 60, Height = 20 };
            var window = new Window { Content = panel };

            window.Show();
            window.SetRenderScaling(1.0);
            window.Measure(new Size(200, 200));
            window.Arrange(new Rect(0, 0, 200, 200));
            window.UpdateLayout();

            var beforeLine = panel.Children.OfType<Line>().Single();
            beforeLine.EndPoint.X.Should().Be(60);

            panel.Width = 120;
            window.Measure(new Size(200, 200));
            window.Arrange(new Rect(0, 0, 200, 200));
            window.UpdateLayout();

            var lines = panel.Children.OfType<Line>().ToList();
            lines.Should().HaveCount(1, "a plain resize must still rebuild to exactly one bottom-edge line");
            lines[0].EndPoint.X.Should().Be(120,
                "the rebuilt line must reflect the new width, same as before the scaling-change fix");
            lines[0].StrokeThickness.Should().Be(1.0,
                "RenderScaling did not change, so the snapped thickness must stay the same as before the resize");
        }, CancellationToken.None);
    }
}
