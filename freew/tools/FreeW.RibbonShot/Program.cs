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
//   tabIndex: 0=File/Backstage 1=Home 2=Insert 3=References 4=Layout 5=Design 6=View 7=Mailings 8=Review,
//             "all" captures content/contextual tabs (skipping File), and "backstage" captures File.

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
        // "dialog" mode: render a sample form using the shared DialogResources theme so the code-built
        // dialog look (flat buttons, accent primary, fields, groupbox) can be verified without standing up
        // a real dialog's dependencies.
        if (tabArg == "dialog")
            return RenderDialogProbe(outDir, w, h);

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

        // "backstage" mode: select the FIRST ribbon tab (the Word-style File tab) to open the Backstage
        // overlay, then capture. The File tab routes to ShowBackstage() and bounces selection back, so we
        // set IsSelected on the first TabItem to trigger that path.
        if (backstageMode)
        {
            var tabs0 = FindTabControl(win);
            var fileTab = tabs0?.Items.Count > 0 ? tabs0.Items[0] as TabItem : null;
            if (fileTab is not null)
            {
                fileTab.IsSelected = true;
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
            Console.WriteLine($"captured {p0}{(fileTab is null ? " (File tab not found!)" : "")}");
            win.Close();
            return 0;
        }

        var tabs = FindTabControl(win);
        var indices = tabArg == "all"
            ? Enumerable.Range(1, Math.Max(0, (tabs?.Items.Count ?? 1) - 1)).ToList()
            : [int.Parse(tabArg)];

        foreach (var i in indices)
        {
            if (tabs is not null && i < tabs.Items.Count)
            {
                // Force the target tab visible so contextual "Tools" tabs (collapsed until their selection
                // context is active) can be captured for verification.
                if (tabs.Items[i] is TabItem forced)
                    forced.Visibility = Visibility.Visible;
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

// Render a representative dialog form themed by the shared DialogResources (merged into the window's own
// resource scope, exactly as DialogWindow does), so the dialog look can be eyeballed offscreen.
static int RenderDialogProbe(string outDir, double w, double h)
{
    // Derive from the real shared DialogWindow base so this exercises its ctor (Win-pack-URI theme merge,
    // typography, white surface) exactly as a converted dialog does — not a hand-merged stand-in.
    var win = new ProbeDialog
    {
        Width = w,
        Height = h,
        WindowStartupLocation = WindowStartupLocation.Manual,
        Left = -10000,
        Top = -10000,
        Title = "Sample Dialog"
    };

    var root = new StackPanel { Margin = new Thickness(16) };

    TextBlock Label(string t) => new() { Text = t, Margin = new Thickness(0, 8, 0, 2) };
    root.Children.Add(Label("Find what:"));
    root.Children.Add(new TextBox { Text = "report" });
    root.Children.Add(Label("Replace with:"));
    root.Children.Add(new TextBox { Text = "summary" });
    root.Children.Add(Label("Look in:"));
    var combo = new ComboBox();
    combo.Items.Add("Whole document");
    combo.Items.Add("Current selection");
    combo.SelectedIndex = 0;
    root.Children.Add(combo);

    var group = new GroupBox { Header = "Options", Margin = new Thickness(0, 12, 0, 0) };
    var groupPanel = new StackPanel();
    groupPanel.Children.Add(new CheckBox { Content = "Match case", IsChecked = true, Margin = new Thickness(0, 2, 0, 2) });
    groupPanel.Children.Add(new CheckBox { Content = "Whole words only", Margin = new Thickness(0, 2, 0, 2) });
    groupPanel.Children.Add(new RadioButton { Content = "Up", Margin = new Thickness(0, 2, 0, 2) });
    groupPanel.Children.Add(new RadioButton { Content = "Down", IsChecked = true, Margin = new Thickness(0, 2, 0, 2) });
    group.Content = groupPanel;
    root.Children.Add(group);

    var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
    var cancel = new Button { Content = "Cancel" };
    var disabled = new Button { Content = "Replace All", IsEnabled = false };
    var ok = new Button { Content = "Find Next" };
    if (win.Resources["DialogPrimaryButton"] is Style primary)
        ok.Style = primary;
    buttons.Children.Add(disabled);
    buttons.Children.Add(cancel);
    buttons.Children.Add(ok);
    root.Children.Add(buttons);

    win.Content = root;
    win.Show();
    win.UpdateLayout();
    win.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Loaded);

    var bmp = new RenderTargetBitmap((int)w, (int)h, 96, 96, PixelFormats.Pbgra32);
    bmp.Render(win);
    var path = Path.Combine(outDir, "dialog.png");
    var enc = new PngBitmapEncoder();
    enc.Frames.Add(BitmapFrame.Create(bmp));
    using (var fs = File.Create(path)) enc.Save(fs);
    Console.WriteLine($"captured {path}");
    win.Close();
    return 0;
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

// Concrete DialogWindow used only by the dialog-probe render mode.
sealed class ProbeDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
}
