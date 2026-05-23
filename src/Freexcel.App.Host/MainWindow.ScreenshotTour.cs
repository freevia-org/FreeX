using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Freexcel.App.Host;

public partial class MainWindow
{
    private static readonly string[] TourTabNames =
        ["Home", "Insert", "Draw", "Page_Layout", "Formulas", "Data", "Review", "View"];
    private static readonly int[] TourTabIndices = [1, 2, 3, 4, 5, 6, 7, 8];

    // Activated by FREEXCEL_SS_TOUR=1 env var.  Output lands in <repo-root>/screenshots/.
    private void TryStartScreenshotTour()
    {
        if (Environment.GetEnvironmentVariable("FREEXCEL_SS_TOUR") != "1")
            return;

        var outputDir = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "screenshots"));
        Directory.CreateDirectory(outputDir);
        _ = RunScreenshotTourAsync(outputDir);
    }

    private async Task RunScreenshotTourAsync(string outputDir)
    {
        await Task.Delay(1200);
        await CaptureAllTabsAsync(outputDir, "max");

        WindowState = WindowState.Normal;
        Width = 1100; Height = 768;
        await Task.Delay(600);
        await CaptureAllTabsAsync(outputDir, "1100");

        Width = 900;
        await Task.Delay(600);
        await CaptureAllTabsAsync(outputDir, "900");

        Width = 750;
        await Task.Delay(600);
        await CaptureAllTabsAsync(outputDir, "750");

        Application.Current.Shutdown();
    }

    private async Task CaptureAllTabsAsync(string outputDir, string label)
    {
        for (int i = 0; i < TourTabIndices.Length; i++)
        {
            RibbonTabs.SelectedIndex = TourTabIndices[i];
            UpdateLayout();
            await Task.Delay(350);
            UpdateLayout();

            var source = PresentationSource.FromVisual(RibbonTabs);
            var dpiX = source?.CompositionTarget.TransformToDevice.M11 ?? 1.0;
            var dpiY = source?.CompositionTarget.TransformToDevice.M22 ?? 1.0;
            int pw = Math.Max(1, (int)(RibbonTabs.ActualWidth * dpiX));
            int ph = Math.Max(1, (int)(RibbonTabs.ActualHeight * dpiY));

            var rtb = new RenderTargetBitmap(pw, ph, 96 * dpiX, 96 * dpiY, PixelFormats.Pbgra32);
            rtb.Render(RibbonTabs);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(rtb));
            var path = Path.Combine(outputDir, $"{label}_{TourTabNames[i]}.png");
            await using var stream = File.OpenWrite(path);
            encoder.Save(stream);
        }
    }
}
