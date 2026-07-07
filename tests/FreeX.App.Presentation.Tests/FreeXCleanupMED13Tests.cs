using FluentAssertions;
using FreeX.App.Presentation.Charts;
using FreeX.Core.Model;
using static FreeX.App.Presentation.Tests.Charts.ChartLayoutTestData;

namespace FreeX.App.Presentation.Tests;

/// <summary>
/// Focused regression tests for FreeX cleanup batch MED13 (round-10 MED/LOW findings).
/// </summary>
public sealed class FreeXCleanupMED13Tests
{
    /// <summary>
    /// P89: the Avalonia legend layout must honor chart.LegendEntries so a series whose
    /// &lt;c:legendEntry&gt;&lt;c:delete val="1"/&gt; hides it from the legend (e.g. an internal
    /// helper series in a target-band combo chart) is not resurrected in the rendered legend.
    /// </summary>
    [Fact]
    public void Legend_excludes_series_marked_deleted_via_LegendEntries()
    {
        var chart = Chart(ChartType.Column, c =>
        {
            c.ShowLegend = true;
            c.LegendPosition = ChartLegendPosition.Right;
            // Series index 1 ("Helper") is hidden from the legend, mirroring Excel's
            // <c:legendEntry><c:idx val="1"/><c:delete val="1"/></c:legendEntry>.
            c.LegendEntries.Add(new ChartLegendEntryModel(1, true));
        });
        var request = Request(chart, ["A"], [Series(0, "Visible", 10), Series(1, "Helper", 20)]);

        var layout = ChartLayoutEngine.Layout(request);

        layout.Legend.Entries.Should().ContainSingle();
        layout.Legend.Entries[0].Label.Should().Be("Visible");
        layout.Legend.Entries.Should().NotContain(e => e.Label == "Helper");
    }
}
