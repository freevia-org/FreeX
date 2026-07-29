using System.Reflection;
using System.Windows.Media;
using FluentAssertions;
using FreeX.App.UI;

namespace FreeX.App.UI.Tests;

/// <summary>
/// R91-render-frozen-print-titles-5-1: real Excel draws a MANUAL page break as a thick SOLID line and
/// an AUTOMATIC page break as a thin DASHED line -- the opposite of what MakePageBreakPen (used
/// exclusively for manual breaks via RenderManualPageBreaks) used to apply, so both kinds looked
/// dashed and a user could no longer tell "I set this break" from "Excel decided to break here".
/// </summary>
public sealed class R91_PageBreakPenStyleTests
{
    private static Pen InvokePenFactory(string methodName)
    {
        var method = typeof(GridView).GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull($"{methodName} must exist as a private static pen factory on GridView");
        return (Pen)method!.Invoke(null, null)!;
    }

    [Fact]
    public void MakePageBreakPen_ManualBreak_IsSolid()
    {
        var pen = InvokePenFactory("MakePageBreakPen");

        pen.DashStyle.Dashes.Should().BeEmpty(
            "Excel draws a MANUAL page break as a solid line, not dashed");
    }

    [Fact]
    public void MakePageBreakAutomaticPen_AutomaticBreak_StaysDashed_NoRegression()
    {
        var pen = InvokePenFactory("MakePageBreakAutomaticPen");

        pen.DashStyle.Dashes.Should().NotBeEmpty(
            "automatic page breaks must keep their pre-existing dashed rendering -- only the manual-break pen's style should have changed");
    }
}
