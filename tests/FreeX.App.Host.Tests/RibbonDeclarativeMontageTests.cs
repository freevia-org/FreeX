using System.IO;
using FluentAssertions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FreeX.Ribbon;

namespace FreeX.App.Host.Tests;

public class RibbonDeclarativeMontageTests
{
    [Fact]
    public void RendersEveryTabToMontagePng()
    {
        const double width = 1700;
        const double tabHeight = 116;
        const double labelHeight = 22;
        var definition = FreeXRibbon.Build();

        // Show all tabs (normal + contextual) for the montage.
        var allActive = RibbonContextState.None
            .With("chart.selected").With("picture.selected").With("shape.selected")
            .With("table.active").With("pivot.active");
        var tabs = RibbonContextResolver.Resolve(definition, allActive);

        var outputPath = Path.Combine(
            WorkspaceFileLocator.FindWorkspaceRoot(),
            "screenshots", "ribbon-declarative", "all_tabs_declarative.png");

        StaTestRunner.Run(() =>
        {
            if (Application.Current is null)
                _ = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };

            var column = new StackPanel { Orientation = Orientation.Vertical };
            var resources = new ResourceDictionary
            {
                Source = new Uri(
                    "pack://application:,,,/FreeX.App.Host;component/Resources/MainWindowResources.xaml")
            };

            foreach (var tab in tabs)
            {
                var header = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(0x17, 0x32, 0x4D)),
                    Height = labelHeight,
                    Child = new TextBlock
                    {
                        Text = "  " + tab.Header + (tab.IsContextual ? "  (contextual)" : ""),
                        Foreground = Brushes.White,
                        FontWeight = FontWeights.SemiBold,
                        VerticalAlignment = VerticalAlignment.Center
                    }
                };
                column.Children.Add(header);

                var body = new Border { Width = width, Height = tabHeight };
                body.Resources.MergedDictionaries.Add(resources);
                body.Background = body.TryFindResource("FreeXRibbonSurfaceBrush") as Brush ?? Brushes.White;
                body.Child = RibbonWpfRenderer.BuildTabContent(tab, body);
                column.Children.Add(body);
            }

            var totalHeight = tabs.Count * (tabHeight + labelHeight);
            column.Measure(new Size(width, totalHeight));
            column.Arrange(new Rect(0, 0, width, totalHeight));
            column.UpdateLayout();

            var bitmap = new RenderTargetBitmap((int)width, (int)totalHeight, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(column);

            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(outputPath);
            encoder.Save(stream);
        });

        File.Exists(outputPath).Should().BeTrue();

        // Sanity: the generated definition is structurally valid and covers all the tabs.
        RibbonDefinitionValidator.Validate(definition).HasErrors.Should().BeFalse();
        definition.Tabs.Should().HaveCountGreaterThanOrEqualTo(14);
    }
}
