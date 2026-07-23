using System.Reflection;
using System.Windows.Media;
using FluentAssertions;
using FreeX.App.UI;
using Xunit;

namespace FreeX.App.UI.Tests;

/// <summary>
/// R75-render-selection-marquee-4-4: the Cut marching-ants marquee rendered in a solid orange
/// overlay pen (245,124,0) instead of the black/white ants Excel uses identically for both Copy
/// and Cut. GridView.MarchingAntsCutOverlayPens must now be the SAME white overlay pens as
/// MarchingAntsCopyOverlayPens (GridView.cs ~808-810).
/// </summary>
public sealed class GridViewClipboardMarqueeCutColorTests
{
    private static Pen[] GetPens(string fieldName)
    {
        var field = typeof(GridView).GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Static);
        field.Should().NotBeNull($"GridView must declare a static field named {fieldName}");
        var pens = (Pen[])field!.GetValue(null)!;
        pens.Should().NotBeEmpty();
        return pens;
    }

    private static Color GetPenColor(Pen pen)
    {
        var brush = (SolidColorBrush)pen.Brush;
        return brush.Color;
    }

    [Fact]
    public void MarchingAntsCutOverlayPens_AreWhite_NotOrange()
    {
        var cutPens = GetPens("MarchingAntsCutOverlayPens");

        foreach (var pen in cutPens)
        {
            var color = GetPenColor(pen);
            color.Should().Be(Colors.White,
                "Excel does not color-differentiate a Cut marquee from a Copy marquee -- both use " +
                "the same black/white marching ants, not a distinct orange (245,124,0)");
        }
    }

    [Fact]
    public void MarchingAntsCopyOverlayPens_AreWhite_NoRegression()
    {
        var copyPens = GetPens("MarchingAntsCopyOverlayPens");

        foreach (var pen in copyPens)
        {
            GetPenColor(pen).Should().Be(Colors.White,
                "the Copy marquee's overlay pen color must remain unchanged by the Cut-color fix");
        }
    }
}
