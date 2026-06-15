using System.IO;
using FluentAssertions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FreeX.Ribbon;

namespace FreeX.App.Host.Tests;

public class RibbonDeclarativeHomeRenderTests
{
    [Fact]
    public void RendersDeclarativeHomeTabToPng()
    {
        const double width = 1880;
        const double height = 124;
        string outputPath = Path.Combine(
            WorkspaceFileLocator.FindWorkspaceRoot(),
            "screenshots", "ribbon-declarative", "home_declarative.png");

        StaTestRunner.Run(() =>
        {
            EnsureApplication();

            var host = new Border { Width = width };
            host.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri(
                    "pack://application:,,,/FreeX.App.Host;component/Resources/MainWindowResources.xaml")
            });
            host.Background = host.TryFindResource("FreeXRibbonSurfaceBrush") as Brush ?? Brushes.White;

            var homeTab = HomeRibbonDefinition.Build().FindTab("HomeTab")!;
            host.Child = RibbonWpfRenderer.BuildTabContent(homeTab, host);

            host.Measure(new Size(width, height));
            host.Arrange(new Rect(0, 0, width, height));
            host.UpdateLayout();

            var bitmap = new RenderTargetBitmap(
                (int)width, (int)height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(host);

            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(outputPath);
            encoder.Save(stream);
        });

        File.Exists(outputPath).Should().BeTrue();
        new FileInfo(outputPath).Length.Should().BeGreaterThan(0);
    }

    private static void EnsureApplication()
    {
        if (Application.Current is null)
        {
            _ = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        }
    }
}
