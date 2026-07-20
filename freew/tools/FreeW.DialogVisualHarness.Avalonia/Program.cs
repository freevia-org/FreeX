using System.Reflection;
using System.Runtime.InteropServices;
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
    var scenarioFilter = Optional(args, "--scenario");
    var inventory = JsonSerializer.Deserialize<RouteInventory>(File.ReadAllText(inventoryPath), JsonOptions())
        ?? throw new InvalidOperationException("Invalid inventory.");
    Directory.CreateDirectory(output);
    var progressPath = Path.Combine(output, "capture-progress.log");
    File.WriteAllText(progressPath, string.Empty);
    var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(HarnessApp).Assembly);
    var captures = new List<Capture>();
    foreach (var scenario in inventory.Scenarios.Where(s => s.Host == "avalonia" && (scenarioFilter is null || s.Id.Equals(scenarioFilter, StringComparison.OrdinalIgnoreCase))))
    {
        File.AppendAllText(progressPath, $"start {scenario.Id}{Environment.NewLine}");
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
        File.AppendAllText(progressPath, $"complete {scenario.Id}{Environment.NewLine}");
    }
    var manifest = new CaptureManifest("freew.dialog-capture-manifest.v1", 1, "avalonia", output, captures);
    File.WriteAllText(Path.Combine(output, "avalonia_dialog_capture_manifest.json"), JsonSerializer.Serialize(manifest, JsonOptions()));
    Console.WriteLine($"avalonia scenarios: {captures.Count}; captured: {captures.Count(c => c.Status == "captured")}; unsupported: {captures.Count(c => c.Status != "captured")}");
    return 0;
}

static Capture? CaptureOne(Scenario scenario, string output)
{
    var dialog = AvaloniaDialogRouteFactory.Create(scenario.RouteId, scenario.State);
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
    using var bitmap = new RenderTargetBitmap(new PixelSize(560, 600), new Vector(96, 96));
    bitmap.Render(dialog);
    var bytes = RenderTargetBitmapToPng(bitmap);
    var semantics = ReadSemantics(dialog);
    if (bytes.Length == 0)
    {
        Console.Error.WriteLine($"avalonia {scenario.Id}: RenderTargetBitmap produced zero bytes");
        dialog.Close();
        return Unsupported(scenario, "Avalonia headless renderer returned zero-byte PNG output on this machine; no placeholder image was substituted.", "avalonia-headless-render-unavailable", semantics);
    }
    var path = Path.Combine(output, "full", "avalonia", Safe(scenario.Id) + ".png");
    var cropPath = Path.Combine(output, "crops", "avalonia", Safe(scenario.Id) + ".png");
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    Directory.CreateDirectory(Path.GetDirectoryName(cropPath)!);
    File.WriteAllBytes(path, bytes);
    File.WriteAllBytes(cropPath, bytes);
    dialog.Close();
    return new Capture(scenario.Id, "avalonia", scenario.RouteId, scenario.State, "captured", Relative(output, path), 560, 600, 560, 600, 96, 96, new Rect(0, 0, 560, 600), semantics, null, "Real app-owned Avalonia dialog rendered through the headless compositor.", Relative(output, cropPath));
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

static byte[] RenderTargetBitmapToPng(RenderTargetBitmap bitmap)
{
    var width = bitmap.PixelSize.Width;
    var height = bitmap.PixelSize.Height;
    var stride = checked(width * 4);
    var byteCount = checked(stride * height);
    var pointer = Marshal.AllocHGlobal(byteCount);
    try
    {
        bitmap.CopyPixels(new PixelRect(0, 0, width, height), pointer, byteCount, stride);
        var pixels = new byte[byteCount];
        Marshal.Copy(pointer, pixels, 0, byteCount);
        using var skBitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul));
        Marshal.Copy(pixels, 0, skBitmap.GetPixels(), byteCount);
        using var image = SKImage.FromBitmap(skBitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data?.ToArray() ?? [];
    }
    finally
    {
        Marshal.FreeHGlobal(pointer);
    }
}

static Capture Unsupported(Scenario scenario, string note, string? limitation = null, Semantics? semantics = null) => new(scenario.Id, "avalonia", scenario.RouteId, scenario.State, "unsupported", "", 0, 0, 0, 0, 96, 96, new Rect(0, 0, 0, 0), semantics ?? new Semantics(null, null, null, [], []), limitation ?? scenario.Limitation, note);
static IEnumerable<T> FindVisualChildren<T>(Visual root) where T : Visual
{
    if (root is T value) yield return value;
    foreach (var child in root.GetLogicalDescendants().OfType<T>()) yield return child;
}
static string Required(string[] args, string option) { var i = Array.IndexOf(args, option); return i >= 0 && i + 1 < args.Length ? args[i + 1] : throw new ArgumentException($"Missing {option}."); }
static string? Optional(string[] args, string option) { var i = Array.IndexOf(args, option); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
static string Relative(string root, string path) => Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');
static string Safe(string value) => System.Text.RegularExpressions.Regex.Replace(value, "[^A-Za-z0-9._-]", "-");
static JsonSerializerOptions JsonOptions() => new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };
}

sealed class HarnessApp : Application
{
    public override void Initialize() => Styles.Add(new FluentTheme());
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<HarnessApp>().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = true });
}
