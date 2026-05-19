using System.IO;
using FluentAssertions;

namespace Freexcel.App.Host.Tests;

public sealed class MainWindowSourceHygieneTests
{
    [Fact]
    public void MainWindow_DoesNotKeepLegacyZoomConversionHelpers()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml.cs"));

        source.Should().NotContain("SliderToZoomPct(");
        source.Should().NotContain("ZoomPctToSlider(");
    }
}
