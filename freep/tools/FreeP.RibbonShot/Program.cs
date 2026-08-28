using System.Globalization;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Free.Shared.AppServices;
using Free.Shared.Shell;
using FreeP.App.Host;

// Renders FreeP's real WPF MainWindow off-screen for evidence-led PowerPoint ribbon comparisons.
// Usage: FreeP.RibbonShot <output.png> [tab header] [width] [height]
var outputPath = args.Length > 0 ? args[0] : Path.Combine(Directory.GetCurrentDirectory(), "freep-ribbon.png");
var tabHeader = args.Length > 1 ? args[1] : "Home";
// r164 audit: the capture surface is sized straight from these arguments. WPF's own
// RenderTargetBitmap does reject an absurd canvas quickly (measured: "1e9 1e9" and "100000 100000"
// both fail in 2-3s, no hang and no runaway allocation), so this is not the unbounded-quantity
// hazard the audit was hunting -- but a mistyped dimension reported "The image data generated an
// overflow during processing", and a non-numeric one threw a raw FormatException stack trace out of
// Main. Say what is wrong with the argument instead.
if (!TryReadDimension(args, 2, 1280, out var width) ||
    !TryReadDimension(args, 3, 720, out var height))
{
    return 2;
}

static bool TryReadDimension(string[] arguments, int index, double fallback, out double value)
{
    value = fallback;
    if (arguments.Length <= index)
        return true;

    // 16384 is generous: the largest evidence capture this tool is asked for is a 4K-wide window.
    const double MaximumDimension = 16384;
    if (!double.TryParse(arguments[index], NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ||
        double.IsNaN(parsed))
    {
        Console.Error.WriteLine($"FAIL: '{arguments[index]}' is not a number (usage: FreeP.RibbonShot <output.png> [tab] [width] [height]).");
        return false;
    }

    if (parsed < 1 || parsed > MaximumDimension)
    {
        Console.Error.WriteLine($"FAIL: dimension {parsed} is outside the supported range 1..{MaximumDimension}.");
        return false;
    }

    value = parsed;
    return true;
}

var exitCode = 0;
var thread = new Thread(() => exitCode = Capture(outputPath, tabHeader, width, height));
thread.SetApartmentState(ApartmentState.STA);
thread.Start();
thread.Join();
return exitCode;

static int Capture(string outputPath, string tabHeader, double width, double height)
{
    try
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
        AppProduct.Current = new AppProductIdentity("FreeP", "FREEP_DIAGNOSTICS", "FreeP");
        ShellStrings.Current = DefaultShellStrings.Instance;

        var window = new MainWindow
        {
            Width = width,
            Height = height,
            WindowState = WindowState.Normal,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -10000,
            Top = -10000,
            ShowInTaskbar = false
        };

        window.Show();
        window.UpdateLayout();

        var tabs = FindVisualChildren<TabControl>(window)
            .FirstOrDefault(candidate => candidate.Items.OfType<TabItem>()
                .Any(tab => string.Equals(tab.Header?.ToString(), tabHeader, StringComparison.OrdinalIgnoreCase)));
        var selected = tabs?.Items.OfType<TabItem>()
            .FirstOrDefault(tab => string.Equals(tab.Header?.ToString(), tabHeader, StringComparison.OrdinalIgnoreCase));
        if (tabs is null || selected is null)
            throw new InvalidOperationException($"Ribbon tab '{tabHeader}' was not found.");

        tabs.SelectedItem = selected;
        window.UpdateLayout();
        PumpFrames(window.Dispatcher, TimeSpan.FromMilliseconds(100));
        window.UpdateLayout();

        var bitmap = new RenderTargetBitmap((int)width, (int)height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var stream = File.Create(outputPath))
            encoder.Save(stream);

        window.Close();
        Console.WriteLine($"captured {outputPath}");
        return 0;
    }
    catch (Exception exception)
    {
        Console.WriteLine($"FAIL: {exception.GetType().Name}: {exception.Message}");
        return 1;
    }
}

static void PumpFrames(System.Windows.Threading.Dispatcher dispatcher, TimeSpan duration)
{
    var frame = new System.Windows.Threading.DispatcherFrame();
    var timer = new System.Windows.Threading.DispatcherTimer(
        duration,
        System.Windows.Threading.DispatcherPriority.Background,
        (_, _) => frame.Continue = false,
        dispatcher);
    timer.Start();
    System.Windows.Threading.Dispatcher.PushFrame(frame);
    timer.Stop();
}

static IEnumerable<T> FindVisualChildren<T>(DependencyObject root) where T : DependencyObject
{
    for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
    {
        var child = VisualTreeHelper.GetChild(root, index);
        if (child is T match)
            yield return match;

        foreach (var descendant in FindVisualChildren<T>(child))
            yield return descendant;
    }
}
