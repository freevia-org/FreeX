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
            method!.Invoke(null, [dc, border, new Point(0, 0), new Point(40, 0), null, borderPenCache]);
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

            pen.Thickness.Should().Be(1.5,
                "SlantDashDot is ranked as a medium-weight style in BorderEdgePrecedence and must render at the same 1.5 DIP weight as Medium/MediumDashed/MediumDashDot/MediumDashDotDot");
        });
    }

    [Fact]
    public void ThinBorder_StillRendersAtThinWeight_NoRegression()
    {
        WpfTestThread.Run(() =>
        {
            var border = new CellBorder(BorderStyle.Thin, CellColor.Black);

            var pen = InvokeDrawBorderEdgeAndCapturePen(border);

            pen.Thickness.Should().Be(0.5, "a Thin border must keep its pre-existing 0.5 DIP weight");
        });
    }

    [Fact]
    public void MediumBorder_StillRendersAtTheSameMediumWeightAsSlantDashDot_NoRegression()
    {
        WpfTestThread.Run(() =>
        {
            var border = new CellBorder(BorderStyle.Medium, CellColor.Black);

            var pen = InvokeDrawBorderEdgeAndCapturePen(border);

            pen.Thickness.Should().Be(1.5, "a Medium border must keep its pre-existing 1.5 DIP weight");
        });
    }
}
