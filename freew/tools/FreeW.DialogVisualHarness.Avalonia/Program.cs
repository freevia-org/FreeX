using System.Reflection;
using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.Themes.Fluent;
using Avalonia.LogicalTree;
using FreeW.App.Avalonia;
using FreeW.App.Avalonia.Editing;
using FreeW.Core.Model;
using FreeW.App.Presentation.Options;
using SkiaSharp;

[assembly: AvaloniaTestApplication(typeof(HarnessApp))]

internal static class Program
{
static async Task<int> Main(string[] args)
{
    var inventoryPath = Required(args, "--inventory");
    var output = Path.GetFullPath(Required(args, "--output"));
    var inventory = JsonSerializer.Deserialize<RouteInventory>(File.ReadAllText(inventoryPath), JsonOptions())
        ?? throw new InvalidOperationException("Invalid inventory.");
    Directory.CreateDirectory(output);
    var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(HarnessApp).Assembly);
    var captures = new List<Capture>();
    foreach (var scenario in inventory.Scenarios.Where(s => s.Host == "avalonia"))
    {
        Capture? result = null;
        try
        {
            await session.Dispatch(() => result = CaptureOne(scenario, output), CancellationToken.None);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"avalonia {scenario.Id}: {ex.GetType().Name}: {ex.Message}");
        }
        captures.Add(result ?? Unsupported(scenario, "The Avalonia adapter requires an app-owned route constructor or a temporary capture hook for this family."));
    }
    var manifest = new CaptureManifest("freew.dialog-capture-manifest.v1", 1, "avalonia", output, captures);
    File.WriteAllText(Path.Combine(output, "avalonia_dialog_capture_manifest.json"), JsonSerializer.Serialize(manifest, JsonOptions()));
    Console.WriteLine($"avalonia scenarios: {captures.Count}; captured: {captures.Count(c => c.Status == "captured")}; unsupported: {captures.Count(c => c.Status != "captured")}");
    return 0;
}

static Capture? CaptureOne(Scenario scenario, string output)
{
    if (scenario.RouteId is not ("page-setup" or "options"))
        return null;
    var dialog = CreateDialog(scenario.RouteId);
    if (dialog is null) return null;
    dialog.Width = 560;
    dialog.Height = 600;
    dialog.SizeToContent = SizeToContent.Manual;
    dialog.Show();
    dialog.Measure(new Size(560, 600));
    dialog.Arrange(new Avalonia.Rect(0, 0, 560, 600));
    dialog.UpdateLayout();
    Populate(dialog, scenario.State);
    Dispatcher.UIThread.RunJobs(DispatcherPriority.Render);
    var frame = dialog.CaptureRenderedFrame();
    if (frame is null) { dialog.Close(); return null; }
    var bytes = WriteableBitmapToPng(frame);
    if (bytes.Length == 0) { dialog.Close(); return null; }
    var path = Path.Combine(output, "full", "avalonia", Safe(scenario.Id) + ".png");
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    File.WriteAllBytes(path, bytes);
    var semantics = ReadSemantics(dialog);
    dialog.Close();
    return new Capture(scenario.Id, "avalonia", scenario.RouteId, scenario.State, "captured", Relative(output, path), 560, 600, 560, 600, 96, 96, new Rect(0, 0, 560, 600), semantics, null, "Real app-owned Avalonia dialog rendered through the headless compositor.");
}

static Window? CreateDialog(string route)
{
    var assembly = typeof(MainWindow).Assembly;
    if (route == "page-setup") return new PageSetupDialog(new PageSettings());
    var type = assembly.GetType("FreeW.App.Avalonia.OptionsDialog", throwOnError: true)!;
    return (Window?)Activator.CreateInstance(type, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new object?[] { new FreeWOptions() }, null);
}

static void Populate(Window dialog, string state)
{
    var textBoxes = FindVisualChildren<TextBox>(dialog).ToArray();
    if (state == "populated") foreach (var box in textBoxes) if (string.IsNullOrWhiteSpace(box.Text)) box.Text = "12";
    if (state == "validation-error" && textBoxes.Length > 0) textBoxes[0].Text = "not-a-number";
    var tabs = FindVisualChildren<TabControl>(dialog).FirstOrDefault();
    if (tabs is not null) tabs.SelectedIndex = state == "relevant-tab" ? Math.Min(1, Math.Max(0, tabs.ItemCount - 1)) : 0;
    FindVisualChildren<Control>(dialog).FirstOrDefault(c => c.IsTabStop && c.IsEffectivelyEnabled)?.Focus();
}

static Semantics ReadSemantics(Window dialog)
{
    var controls = FindVisualChildren<Control>(dialog).Select(c => new ControlSemantic(
        Avalonia.Automation.AutomationProperties.GetAutomationId(c), c.GetType().Name, Avalonia.Automation.AutomationProperties.GetName(c), c.IsEffectivelyEnabled,
        c is CheckBox check ? check.IsChecked : c is ToggleButton toggle ? toggle.IsChecked : null,
        c is SelectingItemsControl selector ? selector.SelectedIndex : null)).ToArray();
    var buttons = FindVisualChildren<Button>(dialog).ToArray();
    var focused = FindVisualChildren<Control>(dialog).FirstOrDefault(c => c.IsFocused);
    return new Semantics(
        focused is null ? null : Avalonia.Automation.AutomationProperties.GetAutomationId(focused),
        buttons.FirstOrDefault(b => b.IsDefault) is { } d ? d.Content?.ToString() : null,
        buttons.FirstOrDefault(b => b.IsCancel) is { } c ? c.Content?.ToString() : null,
        buttons.Select(b => b.Content?.ToString() ?? b.GetType().Name).ToArray(), controls);
}

static byte[] WriteableBitmapToPng(WriteableBitmap bitmap)
{
    using var locked = bitmap.Lock();
    var colorType = locked.Format == PixelFormat.Bgra8888 ? SKColorType.Bgra8888 : SKColorType.Rgba8888;
    var info = new SKImageInfo(locked.Size.Width, locked.Size.Height, colorType, SKAlphaType.Premul);
    using var skBitmap = new SKBitmap();
    if (!skBitmap.InstallPixels(info, locked.Address, locked.RowBytes)) return [];
    using var image = SKImage.FromBitmap(skBitmap);
    using var data = image.Encode(SKEncodedImageFormat.Png, 100);
    return data?.ToArray() ?? [];
}

static Capture Unsupported(Scenario scenario, string note) => new(scenario.Id, "avalonia", scenario.RouteId, scenario.State, "unsupported", "", 0, 0, 0, 0, 96, 96, new Rect(0, 0, 0, 0), new Semantics(null, null, null, [], []), scenario.Limitation, note);
static IEnumerable<T> FindVisualChildren<T>(Visual root) where T : Visual
{
    if (root is T value) yield return value;
    foreach (var child in root.GetLogicalDescendants().OfType<T>()) yield return child;
}
static string Required(string[] args, string option) { var i = Array.IndexOf(args, option); return i >= 0 && i + 1 < args.Length ? args[i + 1] : throw new ArgumentException($"Missing {option}."); }
static string Relative(string root, string path) => Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');
static string Safe(string value) => System.Text.RegularExpressions.Regex.Replace(value, "[^A-Za-z0-9._-]", "-");
static JsonSerializerOptions JsonOptions() => new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };
}

sealed class HarnessApp : Application
{
    public override void Initialize() => Styles.Add(new FluentTheme());
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<HarnessApp>().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = true });
}
