using System.IO;
using FluentAssertions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FreeX.Ribbon;

namespace FreeX.App.Host.Tests;

public class RibbonDeclarativePerTabTests
{
    [Fact]
    public void RendersEachTabToItsOwnPng()
    {
        const double width = 1700;
        const double tabHeight = 116;
        const double labelHeight = 24;
        var definition = FreeXRibbon.Build();

        var allActive = RibbonContextState.None
            .With("chart.selected").With("picture.selected").With("shape.selected")
            .With("table.active").With("pivot.active");
        var tabs = RibbonContextResolver.Resolve(definition, allActive);

        var outDir = Path.Combine(
            WorkspaceFileLocator.FindWorkspaceRoot(), "screenshots", "ribbon-declarative", "tabs");
        var written = 0;

        StaTestRunner.Run(() =>
        {
            if (Application.Current is null)
                _ = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };

            var resources = new ResourceDictionary
            {
                Source = new Uri(
                    "pack://application:,,,/FreeX.App.Host;component/Resources/MainWindowResources.xaml")
            };
            Directory.CreateDirectory(outDir);

            var index = 0;
            foreach (var tab in tabs)
            {
                var root = new StackPanel { Orientation = Orientation.Vertical, Width = width };

                var header = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(0x17, 0x32, 0x4D)),
                    Height = labelHeight,
                    Child = new TextBlock
                    {
                        Text = "  " + tab.Header + (tab.IsContextual ? "   (contextual)" : ""),
                        Foreground = Brushes.White,
                        FontWeight = FontWeights.SemiBold,
                        FontSize = 13,
                        VerticalAlignment = VerticalAlignment.Center
                    }
                };
                root.Children.Add(header);

                var body = new Border { Width = width, Height = tabHeight };
                body.Resources.MergedDictionaries.Add(resources);
                body.Background = body.TryFindResource("FreeXRibbonSurfaceBrush") as Brush ?? Brushes.White;
                body.Child = RibbonWpfRenderer.BuildTabContent(tab, body);
                root.Children.Add(body);

                var total = tabHeight + labelHeight;
                root.Measure(new Size(width, total));
                root.Arrange(new Rect(0, 0, width, total));
                root.UpdateLayout();

                var bitmap = new RenderTargetBitmap((int)width, (int)total, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(root);

                var safe = tab.Id;
                var path = Path.Combine(outDir, $"{index:00}_{safe}.png");
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using (var stream = File.Create(path))
                    encoder.Save(stream);
                index++;
                written++;
            }
        });

        written.Should().BeGreaterThanOrEqualTo(14);
    }
}
