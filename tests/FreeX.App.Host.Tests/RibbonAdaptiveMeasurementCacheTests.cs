using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class RibbonAdaptiveMeasurementCacheTests
{
    [Fact]
    public void AdaptiveMeasurementAndCollapseState_AreOwnedBySharedWpfPanel()
    {
        var fieldsSource = DialogSourceTestSupport.ReadHostSources("MainWindow.xaml.cs");
        var ribbonSource = DialogSourceTestSupport.ReadHostSources("MainWindow.Ribbon.cs");
        var panelSource = DialogSourceTestSupport.ReadSharedRibbonWpfSource("RibbonAdaptivePanel.cs");

        fieldsSource.Should().NotContain("_ribbonAdaptiveMeasurementCacheKey");
        fieldsSource.Should().NotContain("_ribbonAdaptiveLayoutPlanCache");
        fieldsSource.Should().NotContain("_ribbonCorrectedStateCache");
        fieldsSource.Should().NotContain("_lastRibbonAdaptiveAppliedStates");
        ribbonSource.Should().Contain("panel.InvalidateMeasure();");
        ribbonSource.Should().NotContain("RibbonAdaptiveLayoutEngine");
        panelSource.Should().Contain("host.FullWidth = host.MeasureFullWidth(infinite);");
        panelSource.Should().Contain("RibbonAdaptiveCollapsePolicy.Plan(");
        panelSource.Should().Contain("hosts[index].Collapsed = decisions[index].IsCollapsed;");
    }
}
