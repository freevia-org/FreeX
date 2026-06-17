using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FreeW.App.Host;

// FreeW.RibbonShot — renders the FreeW MainWindow offscreen to a PNG so the ribbon can be visually
// verified (the app needs no human at the keyboard). This is the visual-regression harness for the
// Word-style ribbon: it instantiates the real MainWindow, optionally selects a ribbon tab, lays it out
// at a given size, and rasterises it.
//
// Usage: FreeW.RibbonShot <outDir> [tabIndex|all] [width] [height]
//   tabIndex: 0=Home 1=Insert 2=Layout 3=Design 4=View 5=Mailings 6=Review, or "all".

string outDir = args.Length > 0 ? args[0] : Directory.GetCurrentDirectory();
string tabArg = args.Length > 1 ? args[1] : "0";
double w = args.Length > 2 ? double.Parse(args[2]) : 1500;
double h = args.Length > 3 ? double.Parse(args[3]) : 300;

int rc = 0;
var t = new Thread(() => rc = Run(outDir, tabArg, w, h));
t.SetApartmentState(ApartmentState.STA);
t.Start();
t.Join();
return rc;

static int Run(string outDir, string tabArg, double w, double h)
{
    Directory.CreateDirectory(outDir);
    try
    {
        // The backstage is a full-window overlay toggled visible AFTER the window is shown. On an OFFSCREEN
        // window the compositor never paints content shown post-Show(), so RenderTargetBitmap captures a
        // blank-white surface. For that mode we show the window on-screen so the overlay actually composites,
        // then capture. (Tab shots stay offscreen — their content is present from the first frame.)
        bool backstageMode = tabArg == "backstage";
        var win = new MainWindow
        {
            Width = w,
            Height = h,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = backstageMode ? 0 : -10000,
            Top = backstageMode ? 0 : -10000,
            ShowInTaskbar = false,
            Topmost = backstageMode
        };
        win.Show();
        win.UpdateLayout();
        win.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Loaded);

        // "backstage" mode: click the title-bar File button to open the Backstage overlay, then capture.
        if (backstageMode)
        {
            var file = FindButtonByText(win, "File");
            if (file is not null)
            {
                file.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                win.UpdateLayout();
            }
            // Let the compositor paint the just-shown overlay (RibbonIcon glyphs build on Loaded too).
            PumpFrames(win.Dispatcher, TimeSpan.FromMilliseconds(700));
            var bmp0 = new RenderTargetBitmap((int)w, (int)h, 96, 96, PixelFormats.Pbgra32);
            bmp0.Render(win);
            var p0 = Path.Combine(outDir, "backstage.png");
            var enc0 = new PngBitmapEncoder();
            enc0.Frames.Add(BitmapFrame.Create(bmp0));
            using (var fs0 = File.Create(p0)) enc0.Save(fs0);
            Console.WriteLine($"captured {p0}{(file is null ? " (File button not found!)" : "")}");
            win.Close();
            return 0;
        }

        var tabs = FindTabControl(win);
        var indices = tabArg == "all"
            ? Enumerable.Range(0, tabs?.Items.Count ?? 1).ToList()
            : [int.Parse(tabArg)];

        foreach (var i in indices)
        {
            if (tabs is not null && i < tabs.Items.Count)
            {
                tabs.SelectedIndex = i;
                win.UpdateLayout();
                win.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Loaded);
            }
            var name = tabs?.Items.Count > i && tabs.Items[i] is TabItem ti ? (ti.Header?.ToString() ?? $"tab{i}") : $"tab{i}";
            var bmp = new RenderTargetBitmap((int)w, (int)h, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(win);
            var path = Path.Combine(outDir, $"ribbon-{i}-{name}.png");
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(bmp));
            using var fs = File.Create(path);
            enc.Save(fs);
            Console.WriteLine($"captured {path}");
        }
        win.Close();
        return 0;
    }
    catch (Exception ex)
    {
        Console.WriteLine($"FAIL: {ex.GetType().Name}: {ex.Message}");
        return 1;
    }
}

// Pump real dispatcher frames for the given wall-clock duration so the WPF render thread composites any
// just-shown / freshly-built visuals before an offscreen RenderTargetBitmap capture.
static void PumpFrames(System.Windows.Threading.Dispatcher dispatcher, TimeSpan duration)
{
    var frame = new System.Windows.Threading.DispatcherFrame();
    var timer = new System.Windows.Threading.DispatcherTimer(
        duration, System.Windows.Threading.DispatcherPriority.Background,
        (_, _) => frame.Continue = false, dispatcher);
    timer.Start();
    System.Windows.Threading.Dispatcher.PushFrame(frame);
    timer.Stop();
}

static TabControl? FindTabControl(DependencyObject root)
{
    if (root is TabControl tc) return tc;
    int n = VisualTreeHelper.GetChildrenCount(root);
    for (int i = 0; i < n; i++)
    {
        var found = FindTabControl(VisualTreeHelper.GetChild(root, i));
        if (found is not null) return found;
    }
    return null;
}

static System.Windows.Controls.Button? FindButtonByText(DependencyObject root, string text)
{
    if (root is System.Windows.Controls.Button b)
    {
        var s = b.Content as string ?? (b.Content as System.Windows.Controls.TextBlock)?.Text;
        if (string.Equals(s, text, StringComparison.OrdinalIgnoreCase)) return b;
    }
    int n = VisualTreeHelper.GetChildrenCount(root);
    for (int i = 0; i < n; i++)
    {
        var found = FindButtonByText(VisualTreeHelper.GetChild(root, i), text);
        if (found is not null) return found;
    }
    return null;
}
