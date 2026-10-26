using FluentAssertions;
using FreeP.Core.Model;
using Xunit;

namespace FreeP.App.Compositor.Tests;

/// <summary>
/// r379: a chartEx chart must not offer "Plot visible cells only" or "Rounded corners".
///
/// <para>Both settings live in the CLASSIC chart part -- <c>PptxChartWriter</c> emits
/// <c>c:plotVisOnly</c> and <c>c:roundedCorners</c> in the <c>c:</c> namespace only, and the cx part
/// has no equivalent. The dialog offered them for every chart, so on a waterfall or treemap a user
/// could tick a box that silently did nothing and was gone by the next open. PowerPoint does not
/// offer either control for those chart types.</para>
///
/// <para>The capability is enforced in BOTH places: the fields are disabled in the plan, and the
/// commit path refuses them. Gating only the UI would leave the value writable by any caller that
/// builds a dialog input directly -- the same defect somewhere quieter.</para>
/// </summary>
public sealed class R379_ChartExCannotStoreClassicChartAreaOptionsTests
{
    private static ChartShape Chart(bool isChartEx) => new()
    {
        IsChartEx = isChartEx,
        PlotVisibleOnly = true,
        RoundedCorners = false,
    };
    [Fact]
    public void AChartExChartDoesNotSupportTheClassicChartAreaOptions()
    {
        ChartDisplayOptionsPlanner.FromChart(Chart(isChartEx: true))
            .SupportsClassicChartAreaOptions.Should().BeFalse(
                "the cx part has no plotVisOnly or roundedCorners to store them in");
    }
    [Fact]
    public void AClassicChartStillSupportsThem()
    {
        // The guard must be narrow: an ordinary bar or line chart keeps both controls.
        ChartDisplayOptionsPlanner.FromChart(Chart(isChartEx: false))
            .SupportsClassicChartAreaOptions.Should().BeTrue();
    }
    [Fact]
    public void TheCapabilityIsTheInverseOfTheChartExTitleLayoutOne()
    {
        // These two capabilities partition the dialog: title position/alignment are chartEx-only,
        // plot-visible/rounded-corners are classic-only. Asserting the relationship stops a later
        // edit setting both false (or both true) for some chart kind and leaving a control that
        // silently does nothing.
        foreach (var isChartEx in new[] { true, false })
        {
            var planner = ChartDisplayOptionsPlanner.FromChart(Chart(isChartEx));
            planner.SupportsClassicChartAreaOptions
                .Should().Be(!planner.SupportsChartExTitleLayout, "chart kind decides exactly one of the two");
        }
    }
}
