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
        var win = new MainWindow { Width = w, Height = h, WindowStartupLocation = WindowStartupLocation.Manual, Left = -10000, Top = -10000, ShowInTaskbar = false };
        win.Show();
        win.UpdateLayout();
        win.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Loaded);

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
