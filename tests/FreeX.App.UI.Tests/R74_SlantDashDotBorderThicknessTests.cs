using System.Reflection;
using System.Windows;
using System.Windows.Media;
using FluentAssertions;
using FreeX.App.UI;
using FreeX.Core.Model;

namespace FreeX.App.UI.Tests;

/// <summary>
/// R74-render-gridlines-borders-4-4: GridView.Rendering.CellStyles.cs's DrawBorderEdge thickness
/// switch was missing <see cref="BorderStyle.SlantDashDot"/> from its medium (1.5 DIP) bucket, so
/// it fell through to the default 0.5 (Thin) weight -- even though BorderEdgePrecedence
/// (GridView.Rendering.cs) already ranks SlantDashDot as a medium-weight style. The fix adds
/// SlantDashDot to the medium-thickness case alongside Medium/MediumDashed/MediumDashDot/
/// MediumDashDotDot.
/// </summary>
public sealed class R74_SlantDashDotBorderThicknessTests
{
    private static Pen InvokeDrawBorderEdgeAndCapturePen(CellBorder border)
    {
        var method = typeof(GridView).GetMethod("DrawBorderEdge", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        var borderPenCache = new Dictionary<CellBorder, Pen>();
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            method!.Invoke(null, [dc, border, new Point(0, 0), new Point(40, 0), null, borderPenCache, 1.0]);
        }

        borderPenCache.Should().ContainKey(border);
        return borderPenCache[border];
    }

    [Fact]
    public void SlantDashDotBorder_RendersAtMediumWeight()
    {
        WpfTestThread.Run(() =>
        {
            var border = new CellBorder(BorderStyle.SlantDashDot, CellColor.Black);

            var pen = InvokeDrawBorderEdgeAndCapturePen(border);

            pen.Thickness.Should().Be(2,
                "SlantDashDot is ranked as a medium-weight style and snaps to the same 2 device-pixel weight as Medium at 100% zoom");
        });
    }

    [Fact]
    public void ThinBorder_StillRendersAtThinWeight_NoRegression()
    {
        WpfTestThread.Run(() =>
        {
            var border = new CellBorder(BorderStyle.Thin, CellColor.Black);

            var pen = InvokeDrawBorderEdgeAndCapturePen(border);

            pen.Thickness.Should().Be(1, "a Thin border must snap to one crisp device pixel at 100% zoom");
        });
    }

    [Fact]
    public void MediumBorder_StillRendersAtTheSameMediumWeightAsSlantDashDot_NoRegression()
    {
        WpfTestThread.Run(() =>
        {
            var border = new CellBorder(BorderStyle.Medium, CellColor.Black);

            var pen = InvokeDrawBorderEdgeAndCapturePen(border);

            pen.Thickness.Should().Be(2, "a Medium border must snap to two crisp device pixels at 100% zoom");
        });
    }
}
