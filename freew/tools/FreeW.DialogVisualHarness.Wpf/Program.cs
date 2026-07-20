using System.Reflection;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FreeW.App.Host;
using FreeW.App.Host.Editing;
using FreeW.App.Presentation.Options;
using FreeW.Core.Model;

internal static class Program
{
[STAThread]
static int Main(string[] args)
{
    var inventoryPath = Required(args, "--inventory");
    var output = Path.GetFullPath(Required(args, "--output"));
    var inventory = JsonSerializer.Deserialize<RouteInventory>(File.ReadAllText(inventoryPath), JsonOptions())
        ?? throw new InvalidOperationException("Invalid inventory.");
    Directory.CreateDirectory(output);
    var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
    var captures = new List<Capture>();
    foreach (var scenario in inventory.Scenarios.Where(s => s.Host == "wpf"))
    {
        if (!TryCapture(scenario, output, out var capture))
            capture = Unsupported(scenario, "The WPF adapter currently captures the page-setup and options families; other families remain in the generated inventory until an app-owned route adapter is added.");
        captures.Add(capture);
    }
    var manifest = new CaptureManifest("freew.dialog-capture-manifest.v1", 1, "wpf", output, captures);
    File.WriteAllText(Path.Combine(output, "wpf_dialog_capture_manifest.json"), JsonSerializer.Serialize(manifest, JsonOptions()));
    Console.WriteLine($"wpf scenarios: {captures.Count}; captured: {captures.Count(c => c.Status == "captured")}; unsupported: {captures.Count(c => c.Status != "captured")}");
    application.Shutdown();
    return 0;
}

static bool TryCapture(Scenario scenario, string output, out Capture capture)
{
    capture = default!;
    if (scenario.RouteId is not ("page-setup" or "options")) return false;
    var owner = new Window { Width = 960, Height = 720, ShowInTaskbar = false };
    Window? dialog = null;
    try
    {
        owner.Show();
        dialog = CreateDialog(scenario.RouteId, owner);
        if (dialog is null) return false;
        dialog.Width = 560;
        dialog.Height = 600;
        dialog.SizeToContent = SizeToContent.Manual;
        dialog.Show();
        dialog.UpdateLayout();
        if (dialog.ActualWidth < 1 || dialog.ActualHeight < 1) return false;
        Populate(dialog, scenario.State);
        dialog.UpdateLayout();
        var width = Math.Max(1, (int)Math.Ceiling(dialog.ActualWidth));
        var height = Math.Max(1, (int)Math.Ceiling(dialog.ActualHeight));
        var path = Path.Combine(output, "full", "wpf", Safe(scenario.Id) + ".png");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(dialog);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var stream = File.Create(path)) encoder.Save(stream);
        var dpi = VisualTreeHelper.GetDpi(dialog);
        var semantics = ReadSemantics(dialog);
        capture = new Capture(scenario.Id, "wpf", scenario.RouteId, scenario.State, "captured", Relative(output, path), width, height, width, height, dpi.PixelsPerInchX, dpi.PixelsPerInchY, new Rect(0, 0, width, height), semantics, null, "Real app-owned WPF dialog rendered through RenderTargetBitmap.");
        return true;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"wpf {scenario.Id}: {ex}");
        return false;
    }
    finally
    {
        dialog?.Close();
        owner.Close();
    }
}

static Window? CreateDialog(string route, Window owner)
{
    if (route == "options")
    {
        var type = typeof(MainWindow).Assembly.GetType("FreeW.App.Host.OptionsDialog", throwOnError: true)!;
        return (Window?)Activator.CreateInstance(type, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new object?[] { owner, new FreeWOptions() }, null);
    }
    var pageType = typeof(MainWindow).Assembly.GetType("FreeW.App.Host.PageSetupDialog", throwOnError: true)!;
    var ctor = pageType.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Single(c => c.GetParameters().Length >= 2);
    var args = ctor.GetParameters().Select(p =>
    {
        if (typeof(Window).IsAssignableFrom(p.ParameterType)) return (object?)owner;
        if (p.ParameterType == typeof(PageSettings)) return (object?)new PageSettings();
        if (p.ParameterType.IsEnum) return Enum.GetValues(p.ParameterType).GetValue(0);
        return p.HasDefaultValue ? p.DefaultValue : null;
    }).ToArray();
    return (Window?)ctor.Invoke(args);
}

static void Populate(Window dialog, string state)
{
    var textBoxes = FindVisualChildren<TextBox>(dialog).ToArray();
    if (state == "populated")
        foreach (var box in textBoxes) if (string.IsNullOrWhiteSpace(box.Text)) box.Text = "12";
    if (state == "validation-error" && textBoxes.Length > 0)
        textBoxes[0].Text = "not-a-number";
    var tabs = FindVisualChildren<TabControl>(dialog).FirstOrDefault();
    if (tabs is not null)
    {
        var index = state == "relevant-tab" ? Math.Min(1, Math.Max(0, tabs.Items.Count - 1)) : 0;
        tabs.SelectedIndex = index;
    }
    Keyboard.Focus(FindVisualChildren<Control>(dialog).FirstOrDefault(c => c.IsTabStop && c.IsEnabled));
}

static Semantics ReadSemantics(Window dialog)
{
    var controls = FindVisualChildren<FrameworkElement>(dialog).Select(e => new ControlSemantic(
        AutomationProperties.GetAutomationId(e), e.GetType().Name, AutomationProperties.GetName(e), e.IsEnabled,
        e is ToggleButton toggle ? toggle.IsChecked : null,
        e is Selector selector ? selector.SelectedIndex : null)).ToArray();
    var buttons = FindVisualChildren<Button>(dialog).ToArray();
    var focused = Keyboard.FocusedElement as FrameworkElement;
    return new Semantics(
        focused is null ? null : AutomationProperties.GetAutomationId(focused),
        buttons.FirstOrDefault(b => b.IsDefault) is { } d ? ButtonText(d) : null,
        buttons.FirstOrDefault(b => b.IsCancel) is { } c ? ButtonText(c) : null,
        buttons.Select(ButtonText).ToArray(), controls);
}

static string ButtonText(Button button) => button.Content?.ToString() ?? AutomationProperties.GetName(button) ?? button.GetType().Name;
static Capture Unsupported(Scenario scenario, string note) => new(scenario.Id, "wpf", scenario.RouteId, scenario.State, "unsupported", "", 0, 0, 0, 0, 96, 96, new Rect(0, 0, 0, 0), new Semantics(null, null, null, [], []), scenario.Limitation, note);
static IEnumerable<T> FindVisualChildren<T>(DependencyObject root) where T : DependencyObject
{
    if (root is T value) yield return value;
    for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        foreach (var child in FindVisualChildren<T>(VisualTreeHelper.GetChild(root, i))) yield return child;
}
static string Required(string[] args, string option) { var i = Array.IndexOf(args, option); return i >= 0 && i + 1 < args.Length ? args[i + 1] : throw new ArgumentException($"Missing {option}."); }
static string Relative(string root, string path) => Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');
static string Safe(string value) => System.Text.RegularExpressions.Regex.Replace(value, "[^A-Za-z0-9._-]", "-");
static JsonSerializerOptions JsonOptions() => new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };
}
