using FluentAssertions;

namespace Freexcel.App.Host.Tests;

public sealed class RibbonResizeCoordinatorTests
{
    [Fact]
    public void NativeResizeExit_OnlySchedulesRibbonFallbackWhenResizeLoopCompacted()
    {
        var fields = System.IO.File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml.cs"));
        var altKeyTipsSource = System.IO.File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.AltKeyTips.cs"));
        var ribbonSource = System.IO.File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.Ribbon.cs"));

        var wndProc = altKeyTipsSource.Substring(
            altKeyTipsSource.IndexOf("private IntPtr MainWindow_WndProc", StringComparison.Ordinal));
        var resizeCompactor = ribbonSource.Substring(
            ribbonSource.IndexOf("private void CompactRibbonSurfaceAfterResize", StringComparison.Ordinal),
            ribbonSource.IndexOf("private void QueueRibbonFallback", StringComparison.Ordinal) -
            ribbonSource.IndexOf("private void CompactRibbonSurfaceAfterResize", StringComparison.Ordinal));
        var resizeExit = ribbonSource.Substring(
            ribbonSource.IndexOf("private void CompleteRibbonResizeCompaction", StringComparison.Ordinal),
            ribbonSource.IndexOf("private bool ShouldNormalizeRibbonSurfaceForResize", StringComparison.Ordinal) -
            ribbonSource.IndexOf("private void CompleteRibbonResizeCompaction", StringComparison.Ordinal));

        fields.Should().Contain("private bool _ribbonResizeCompactionPendingOnExit;");
        wndProc.Should().Contain("_ribbonResizeCompactionPendingOnExit = false;");
        resizeCompactor.Should().Contain("_ribbonResizeCompactionPendingOnExit = true;");
        resizeExit.Should().Contain("if (_ribbonResizeCompactionPendingOnExit)");
        resizeExit.Should().Contain("_ribbonResizeCompactionPendingOnExit = false;");
        resizeExit.Should().Contain("QueueRibbonFallback(RibbonFallbackWork.CompactOnly);");
        resizeExit.Should().NotContain("CompactRibbonSurfaceAfterResize(scheduleFallback: true)");
        resizeExit.Should().Contain("_lastRibbonResizeWidth = width;");
    }
}
