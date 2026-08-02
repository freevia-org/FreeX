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
using FreeW.DialogVisualHarness;

internal static class Program
{
[STAThread]
static int Main(string[] args)
{
    var inventoryPath = Required(args, "--inventory");
    var output = Path.GetFullPath(Required(args, "--output"));
    var inventory = JsonSerializer.Deserialize<RouteInventory>(File.ReadAllText(inventoryPath), JsonOptions())
        ?? throw new InvalidOperationException("Invalid inventory.");
    var scenarioFilter = Optional(args, "--scenario");
    Directory.CreateDirectory(output);
    var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
    var captures = new List<Capture>();
    foreach (var scenario in inventory.Scenarios.Where(s => s.Host == "wpf" && (scenarioFilter is null || s.Id.Equals(scenarioFilter, StringComparison.OrdinalIgnoreCase))))
    {
        if (!TryCapture(scenario, output, out var capture))
            capture = Unsupported(scenario, "No constructible app-owned WPF route adapter was available for this source family.");
        captures.Add(capture);
    }
    var manifest = new CaptureManifest("freew.dialog-capture-manifest.v1", 1, "wpf", output, captures);
    File.WriteAllText(Path.Combine(output, "wpf_dialog_capture_manifest.json"), JsonSerializer.Serialize(manifest, JsonOptions()));
    Console.WriteLine($"wpf scenarios: {captures.Count}; captured: {captures.Count(c => c.Status == "captured")}; unsupported: {captures.Count(c => c.Status != "captured")}");
    application.Shutdown();
    return captures.All(c => c.Status == "captured"
        && c.FullPixelContent?.PassesContentGate == true
        && c.TargetPixelContent?.PassesContentGate == true) ? 0 : 2;
}

static bool TryCapture(Scenario scenario, string output, out Capture capture)
{
    capture = default!;
    var owner = new Window { Width = 960, Height = 720, ShowInTaskbar = false };
    Window? dialog = null;
    try
    {
        owner.Show();
        if (WpfDialogRouteFactory.IsStaticPromptRoute(scenario.RouteId))
            return TryCaptureStaticPrompt(scenario, output, owner, out capture);
        dialog = WpfDialogRouteFactory.Create(scenario.RouteId, scenario.State, owner);
        if (dialog is null) return false;
        dialog.Width = 560;
        dialog.Height = TargetHeight(scenario);
        dialog.SizeToContent = SizeToContent.Manual;
        dialog.Show();
        dialog.UpdateLayout();
        if (dialog.ActualWidth < 1 || dialog.ActualHeight < 1) return false;
        Populate(dialog, scenario);
        dialog.UpdateLayout();
        var width = Math.Max(1, (int)Math.Ceiling(dialog.ActualWidth));
        var height = Math.Max(1, (int)Math.Ceiling(dialog.ActualHeight));
        var path = Path.Combine(output, "full", "wpf", Safe(scenario.Id) + ".png");
        var cropPath = Path.Combine(output, "crops", "wpf", Safe(scenario.Id) + ".png");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        Directory.CreateDirectory(Path.GetDirectoryName(cropPath)!);
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(dialog);
        var content = ReadContent(bitmap, width, height);
        if (!content.PassesContentGate)
        {
            Console.Error.WriteLine($"wpf {scenario.Id}: invalid rendered content: {content.Failure}");
            return false;
        }
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        var png = stream.ToArray();
        File.WriteAllBytes(path, png);
        File.WriteAllBytes(cropPath, png);
        var dpi = VisualTreeHelper.GetDpi(dialog);
        var semantics = ReadSemantics(dialog);
        capture = new Capture(scenario.Id, "wpf", scenario.RouteId, scenario.State, "captured", Relative(output, path), width, height, width, height, dpi.PixelsPerInchX, dpi.PixelsPerInchY, new Rect(0, 0, width, height), semantics, null, "Real app-owned WPF dialog rendered through RenderTargetBitmap; full and target images passed pixel-content validation.", Relative(output, cropPath), content, content);
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

static bool TryCaptureStaticPrompt(Scenario scenario, string output, Window owner, out Capture capture)
{
    capture = default!;
    var frame = new System.Windows.Threading.DispatcherFrame();
    var captured = false;
    Capture? result = null;
    owner.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Normal, new Action(() =>
    {
        try { WpfDialogRouteFactory.InvokeStaticPrompt(scenario.RouteId, scenario.State, owner); }
        catch (Exception ex) { Console.Error.WriteLine($"wpf {scenario.Id}: {ex}"); }
        finally { frame.Continue = false; }
    }));
    owner.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() =>
    {
        var dialog = Application.Current.Windows.OfType<Window>().FirstOrDefault(window => window != owner && window.IsVisible);
        if (dialog is null) return;
        dialog.UpdateLayout();
        if (scenario.RouteId is "font" or "paragraph" or "style")
        {
            Populate(dialog, scenario);
            dialog.UpdateLayout();
        }
        captured = CaptureRenderedWindow(scenario, output, dialog, out result);
        dialog.Close();
    }));
    System.Windows.Threading.Dispatcher.PushFrame(frame);
    capture = result!;
    return captured;
}

static bool CaptureRenderedWindow(Scenario scenario, string output, Window dialog, out Capture capture)
{
    capture = default!;
    try
    {
        var width = Math.Max(1, (int)Math.Ceiling(dialog.ActualWidth));
        var height = Math.Max(1, (int)Math.Ceiling(dialog.ActualHeight));
        var path = Path.Combine(output, "full", "wpf", Safe(scenario.Id) + ".png");
        var cropPath = Path.Combine(output, "crops", "wpf", Safe(scenario.Id) + ".png");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        Directory.CreateDirectory(Path.GetDirectoryName(cropPath)!);
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(dialog);
        var content = ReadContent(bitmap, width, height);
        if (!content.PassesContentGate)
        {
            Console.Error.WriteLine($"wpf {scenario.Id}: invalid rendered content: {content.Failure}");
            return false;
        }
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        var png = stream.ToArray();
        if (png.Length == 0) return false;
        File.WriteAllBytes(path, png);
        File.WriteAllBytes(cropPath, png);
        var dpi = VisualTreeHelper.GetDpi(dialog);
        var semantics = ReadSemantics(dialog);
        capture = new Capture(scenario.Id, "wpf", scenario.RouteId, scenario.State, "captured", Relative(output, path), width, height, width, height, dpi.PixelsPerInchX, dpi.PixelsPerInchY, new Rect(0, 0, width, height), semantics, null, "Real app-owned WPF static-prompt dialog captured before its cancel path returned; full and target images passed pixel-content validation.", Relative(output, cropPath), content, content);
        return true;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"wpf {scenario.Id}: {ex}");
        return false;
    }
}

static PixelContent ReadContent(RenderTargetBitmap bitmap, int width, int height)
{
    var pixels = new byte[checked(width * height * 4)];
    bitmap.CopyPixels(pixels, width * 4, 0);
    return PixelContentMetrics.Compute(pixels, width, height);
}

static void Populate(Window dialog, Scenario scenario)
{
    var state = scenario.State;
    if (scenario.RouteId == "manual-hyphenation")
    {
        var choices = FindVisualChildren<ComboBox>(dialog).FirstOrDefault();
        if (choices is not null && state != "initial")
            choices.SelectedIndex = Math.Max(0, choices.Items.Count - 1);
        FocusScenarioTarget(dialog, scenario);
        return;
    }
    if (scenario.RouteId == "style")
    {
        var styleTextBoxes = FindVisualChildren<TextBox>(dialog).ToArray();
        var combos = FindVisualChildren<ComboBox>(dialog).ToArray();
        var checks = FindVisualChildren<CheckBox>(dialog).ToArray();
        if (state == "populated")
        {
            if (styleTextBoxes.Length > 0) styleTextBoxes[0].Text = "Sample Style";
            if (combos.Length >= 5)
            {
                combos[0].SelectedIndex = 1;
                combos[1].SelectedIndex = 2;
                combos[2].SelectedIndex = 7;
                combos[3].SelectedIndex = 5;
                combos[4].SelectedIndex = 1;
            }
            if (checks.Length >= 3)
            {
                checks[0].IsChecked = true;
                checks[1].IsChecked = true;
                checks[2].IsChecked = false;
            }
        }
        else if (state == "validation-error" && styleTextBoxes.Length > 0)
            styleTextBoxes[0].Text = string.Empty;
        FocusScenarioTarget(dialog, scenario);
        return;
    }
    if (scenario.RouteId == "footnote-endnote-options")
    {
        var combos = FindVisualChildren<ComboBox>(dialog).ToArray();
        var routeTextBoxes = FindVisualChildren<TextBox>(dialog).ToArray();
        if (state == "populated")
        {
            combos.Select((combo, index) => (combo, index)).ToList().ForEach(pair =>
                pair.combo.SelectedIndex = pair.index switch
                {
                    0 => 1,
                    1 => 2,
                    2 => 4,
                    3 => 1,
                    4 => 1,
                    _ => pair.combo.SelectedIndex
                });
            if (routeTextBoxes.Length >= 2)
            {
                routeTextBoxes[0].Text = "12";
                routeTextBoxes[1].Text = "24";
            }
        }
        else if (state == "validation-error" && routeTextBoxes.Length > 0)
        {
            routeTextBoxes[0].Text = "not-a-number";
            dialog.GetType().GetMethod("ValidateForTest", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(dialog, null);
        }
        FocusScenarioTarget(dialog, scenario);
        return;
    }
    var textBoxes = FindVisualChildren<TextBox>(dialog).ToArray();
    if (state == "populated")
        foreach (var box in textBoxes) if (string.IsNullOrWhiteSpace(box.Text)) box.Text = "12";
    if (state == "validation-error" && textBoxes.Length > 0)
        textBoxes[0].Text = "not-a-number";
    var tabs = FindVisualChildren<TabControl>(dialog).FirstOrDefault();
    if (tabs is not null)
    {
        var index = scenario.Tab is null
            ? 0
            : tabs.Items.Cast<object?>().Select((item, itemIndex) => (item, itemIndex)).FirstOrDefault(pair =>
                pair.item is TabItem tabItem && tabItem.Header?.ToString()?.Equals(scenario.Tab, StringComparison.OrdinalIgnoreCase) == true).itemIndex;
        tabs.SelectedIndex = index;
    }
    if (scenario.Tab?.Equals("More", StringComparison.OrdinalIgnoreCase) == true)
    {
        var expander = FindVisualChildren<Expander>(dialog).FirstOrDefault(candidate => candidate.Header?.ToString() == "More");
        if (expander is not null) expander.IsExpanded = true;
    }
    FocusScenarioTarget(dialog, scenario);
}

static void FocusScenarioTarget(Window dialog, Scenario scenario)
{
    if (scenario.RouteId == "table-properties")
    {
        var automationId = scenario.Tab?.ToLowerInvariant() switch
        {
            "row" => "TablePropertiesRowHeightBox",
            "column" => "TablePropertiesColumnWidthBox",
            "cell" => "TablePropertiesCellWidthBox",
            _ => "TablePropertiesPreferredWidthBox",
        };
        var target = FindVisualChildren<Control>(dialog)
            .FirstOrDefault(control => AutomationProperties.GetAutomationId(control) == automationId);
        if (target is not null)
        {
            target.Focus();
            Keyboard.Focus(target);
            if (target is TextBox textBox)
                textBox.SelectAll();
        }
        return;
    }

    if (scenario.RouteId == "style")
    {
        var name = FindVisualChildren<TextBox>(dialog).FirstOrDefault();
        if (name is not null)
        {
            name.Focus();
            Keyboard.Focus(name);
            name.CaretIndex = name.Text.Length;
        }
        return;
    }
    if (scenario.RouteId == "symbol-picker")
        return;

    if (scenario.RouteId == "legal-notices")
    {
        var selectedText = FindVisualChildren<TabControl>(dialog)
            .FirstOrDefault()?.SelectedItem is TabItem { Content: TextBox textBox }
            ? textBox
            : null;
        if (selectedText is not null)
        {
            selectedText.Focus();
            Keyboard.Focus(selectedText);
            selectedText.CaretIndex = 0;
            return;
        }
    }

    if (Keyboard.FocusedElement is null)
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
    var focusedAutomationId = focused is null ? null : AutomationProperties.GetAutomationId(focused);
    return new Semantics(
        string.IsNullOrWhiteSpace(focusedAutomationId) ? null : focusedAutomationId,
        buttons.FirstOrDefault(b => b.IsDefault) is { } d ? ButtonText(d) : null,
        buttons.FirstOrDefault(b => b.IsCancel) is { } c ? ButtonText(c) : null,
        buttons.Select(ButtonText).ToArray(), controls);
}

// WPF stores access-key markers in Content (for example "_OK"), while Avalonia exposes the
// normalized automation name. Compare the shared semantic name so the report does not flag a
// framework-specific accelerator encoding as a dialog action mismatch.
static string ButtonText(Button button) => DialogSemanticText.ResolveButtonText(
    AutomationProperties.GetName(button),
    button.Content?.ToString(),
    button.GetType().Name);
static Capture Unsupported(Scenario scenario, string note) => new(scenario.Id, "wpf", scenario.RouteId, scenario.State, "unsupported", "", 0, 0, 0, 0, 96, 96, new Rect(0, 0, 0, 0), new Semantics(null, null, null, [], []), scenario.Limitation, note);
static IEnumerable<T> FindVisualChildren<T>(DependencyObject root) where T : DependencyObject
{
    if (root is T value) yield return value;
    for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        foreach (var child in FindVisualChildren<T>(VisualTreeHelper.GetChild(root, i))) yield return child;
}
static string Required(string[] args, string option) { var i = Array.IndexOf(args, option); return i >= 0 && i + 1 < args.Length ? args[i + 1] : throw new ArgumentException($"Missing {option}."); }
static string? Optional(string[] args, string option) { var i = Array.IndexOf(args, option); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
static string Relative(string root, string path) => Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');
static string Safe(string value) => System.Text.RegularExpressions.Regex.Replace(value, "[^A-Za-z0-9._-]", "-");
static int TargetHeight(Scenario scenario) => scenario.RouteId == "compare-documents" && scenario.Tab == "More" ? 720 : 600;
static JsonSerializerOptions JsonOptions() => new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };
}
