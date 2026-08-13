using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class RibbonTabSelectionCoordinatorTests
{
    [Fact]
    public void TabSelection_RefreshesOnlyTheRenderedSharedSurface()
    {
        var selectionSource = DialogSourceTestSupport.ReadHostSources("MainWindow.Backstage.cs");
        var ribbonSource = DialogSourceTestSupport.ReadHostSources("MainWindow.Ribbon.cs");

        selectionSource.Should().Contain("NormalizeRibbonSurfaceAfterTabSelection();");
        ribbonSource.Should().Contain("RefreshActiveDeclarativeRibbonLayout(forceLayout);");
        ribbonSource.Should().Contain("GetActiveDeclarativeRibbonPanel()");
        ribbonSource.Should().NotContain("QueueRibbonFallback");
        ribbonSource.Should().NotContain("UpdateRibbonCompactMode");
    }
}
