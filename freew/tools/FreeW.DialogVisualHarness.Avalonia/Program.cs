using System.Reflection;
using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Platform;
using Avalonia.Skia;
using Avalonia.Threading;
using Avalonia.Themes.Fluent;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using Free.Shared.Theme;
using Free.Shared.Theme.Avalonia;
using FreeW.App.Avalonia;
using FreeW.App.Avalonia.Editing;
using FreeW.Core.Model;
using FreeW.App.Presentation.Options;
using FreeW.DialogVisualHarness;
using SkiaSharp;

[assembly: AvaloniaTestApplication(typeof(HarnessApp))]

internal static class Program
{
// The neutral WPF Backstage host paints a 546-DIP client surface inside its
// 560-DIP capture, so the paired Avalonia surface must reserve 14 DIPs.
const int WpfNonClientWidth = 14;
const int WpfNonClientHeight = 37;

static async Task<int> Main(string[] args)
{
    var inventoryPath = Required(args, "--inventory");
    var output = Path.GetFullPath(Required(args, "--output"));
    var scenarioFilter = Optional(args, "--scenario");
    var routeFilter = Optional(args, "--route");
    var wpfAuthorityPath = Optional(args, "--wpf-authority");
    var inventory = JsonSerializer.Deserialize<RouteInventory>(File.ReadAllText(inventoryPath), JsonOptions())
        ?? throw new InvalidOperationException("Invalid inventory.");
    var wpfAuthority = string.IsNullOrWhiteSpace(wpfAuthorityPath)
        ? new Dictionary<string, Capture>(StringComparer.OrdinalIgnoreCase)
        : (JsonSerializer.Deserialize<CaptureManifest>(File.ReadAllText(wpfAuthorityPath), JsonOptions())
            ?? throw new InvalidOperationException("Invalid WPF authority manifest."))
            .Captures.ToDictionary(capture => capture.ScenarioId, StringComparer.OrdinalIgnoreCase);
    Directory.CreateDirectory(output);
    var progressPath = Path.Combine(output, "capture-progress.log");
    File.WriteAllText(progressPath, string.Empty);
    var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(HarnessApp).Assembly);
    var captures = new List<Capture>();
    foreach (var scenario in inventory.Scenarios.Where(s => s.Host == "avalonia"
        && (scenarioFilter is null || s.Id.Equals(scenarioFilter, StringComparison.OrdinalIgnoreCase))
        && (routeFilter is null || s.RouteId.Equals(routeFilter, StringComparison.OrdinalIgnoreCase))))
    {
        File.AppendAllText(progressPath, $"start {scenario.Id}{Environment.NewLine}");
        Capture? result = null;
        try
        {
            var authorityId = scenario.Id.StartsWith("avalonia.", StringComparison.OrdinalIgnoreCase)
                ? "wpf." + scenario.Id["avalonia.".Length..]
                : scenario.Id;
            wpfAuthority.TryGetValue(authorityId, out var authorityCapture);
            await session.Dispatch(
                () => result = CaptureOne(scenario, output, authorityCapture),
                CancellationToken.None);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"avalonia {scenario.Id}: {ex.GetType().Name}: {ex.Message}");
        }
        captures.Add(result ?? Unsupported(scenario, "The Avalonia adapter requires an app-owned route constructor or a temporary capture hook for this family."));
        File.AppendAllText(progressPath, $"complete {scenario.Id}{Environment.NewLine}");
    }
    var manifest = new CaptureManifest("freew.dialog-capture-manifest.v1", 2, "avalonia", output, captures);
    File.WriteAllText(Path.Combine(output, "avalonia_dialog_capture_manifest.json"), JsonSerializer.Serialize(manifest, JsonOptions()));
    Console.WriteLine($"avalonia scenarios: {captures.Count}; captured: {captures.Count(c => c.Status == "captured")}; unsupported: {captures.Count(c => c.Status != "captured")}");
    return captures.All(c => c.Status == "captured" && c.FullPixelContent?.PassesContentGate == true && c.TargetPixelContent?.PassesContentGate == true) ? 0 : 2;
}

static Capture? CaptureOne(Scenario scenario, string output, Capture? authorityCapture)
{
    var dialog = AvaloniaDialogRouteFactory.Create(scenario.RouteId, scenario.State, scenario.Tab);
    if (dialog is null) return null;
    var useAuthoritySize = authorityCapture is { Status: "captured" }
        && scenario.RouteId is "accessibility-report" or "font" or "paragraph" or "multilevel-list" or "paste-special" or "style" or "manage-styles";
    var width = useAuthoritySize
        ? authorityCapture!.LogicalWidth
        : Math.Max(560, (int)Math.Ceiling(dialog.MinWidth));
    var height = useAuthoritySize
        ? authorityCapture!.LogicalHeight
        : Math.Max(TargetHeight(scenario), (int)Math.Ceiling(dialog.MinHeight));
    var hasNativeFrame = scenario.RouteId != "screen-clip-overlay";
    var clientWidth = hasNativeFrame ? width - WpfNonClientWidth : width;
    var clientHeight = hasNativeFrame ? height - WpfNonClientHeight : height;
    if (scenario.RouteId == "multilevel-list")
    {
        // The WPF RenderTargetBitmap retains one additional right-edge client pixel
        // for this static prompt. Preserve that authority width without changing the
        // shared native-frame compensation used by other routes.
        clientWidth++;
    }
    dialog.Width = clientWidth;
    dialog.Height = clientHeight;
    dialog.SizeToContent = SizeToContent.Manual;
    dialog.Show();
    dialog.Measure(new Size(clientWidth, clientHeight));
    dialog.Arrange(new Avalonia.Rect(0, 0, clientWidth, clientHeight));
    dialog.UpdateLayout();
    Populate(dialog, scenario);
    Dispatcher.UIThread.RunJobs(DispatcherPriority.Render);
    using var frame = dialog.CaptureRenderedFrame();
    var semantics = ReadSemantics(dialog);
    if (frame is null)
    {
        Console.Error.WriteLine($"avalonia {scenario.Id}: headless compositor returned no frame");
        dialog.Close();
        return Unsupported(scenario, "Avalonia headless compositor returned no frame; no placeholder image was substituted.", "avalonia-headless-render-unavailable", semantics);
    }
    // WPF RenderTargetBitmap captures the outer Window bounds but leaves native frame pixels
    // transparent. Avalonia headless has no native frame, so reserve the same logical area to
    // compare equivalent client geometry at the same outer target size.
    var rendered = ReadFrame(frame, width, height);
    var bytes = rendered.Png;
    var fullContent = PixelContentMetrics.Compute(rendered.Pixels, rendered.Width, rendered.Height);
    var targetContent = fullContent;
    if (bytes.Length == 0 || !fullContent.PassesContentGate)
    {
        Console.Error.WriteLine($"avalonia {scenario.Id}: invalid rendered content: {fullContent.Failure ?? "zero-byte PNG"}");
        dialog.Close();
        return Unsupported(scenario, $"Avalonia compositor output failed the visual-content gate: {fullContent.Failure ?? "zero-byte PNG"}.", "avalonia-invalid-rendered-content", semantics, fullContent, targetContent);
    }
    var path = Path.Combine(output, "full", "avalonia", Safe(scenario.Id) + ".png");
    var cropPath = Path.Combine(output, "crops", "avalonia", Safe(scenario.Id) + ".png");
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    Directory.CreateDirectory(Path.GetDirectoryName(cropPath)!);
    File.WriteAllBytes(path, bytes);
    File.WriteAllBytes(cropPath, bytes);
    dialog.Close();
    return new Capture(scenario.Id, "avalonia", scenario.RouteId, scenario.State, "captured", Relative(output, path), width, height, width, height, 96, 96, new Rect(0, 0, width, height), semantics, null, "Real app-owned Avalonia dialog rendered through CaptureRenderedFrame; full and target images passed pixel-content validation.", Relative(output, cropPath), fullContent, targetContent);
}

static void Populate(Window dialog, Scenario scenario)
{
    var state = scenario.State;
    if (scenario.RouteId == "about")
    {
        // About has no editable or stateful fields. Keep initial and populated
        // captures on the same production presentation contract.
        FocusScenarioTarget(dialog, scenario);
        return;
    }
    if (scenario.RouteId == "manual-hyphenation")
    {
        var choices = FindVisualChildren<ComboBox>(dialog).FirstOrDefault();
        if (choices is not null && state != "initial")
            choices.SelectedIndex = Math.Max(0, choices.ItemCount - 1);
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
    if (state == "populated") foreach (var box in textBoxes) if (string.IsNullOrWhiteSpace(box.Text)) box.Text = "12";
    if (state == "populated")
    {
        // WPF's visual-tree walker reaches the editable ComboBox template textbox;
        // mirror that authority state without changing the dialog's normal model seed.
        foreach (var box in dialog.GetVisualDescendants().OfType<TextBox>()
                     .Where(box => box.Name == "PART_EditableTextBox" && string.IsNullOrWhiteSpace(box.Text)))
            box.Text = "12";
    }
    if (state == "validation-error" && textBoxes.Length > 0) textBoxes[0].Text = "not-a-number";
    var tabs = FindVisualChildren<TabControl>(dialog).FirstOrDefault();
    if (tabs is not null)
    {
        var selectedIndex = scenario.Tab is null
            ? 0
            : tabs.Items.Cast<object?>().Select((item, index) => (item, index)).FirstOrDefault(pair =>
                pair.item is TabItem tabItem && tabItem.Header?.ToString()?.Equals(scenario.Tab, StringComparison.OrdinalIgnoreCase) == true).index;
        tabs.SelectedIndex = Math.Clamp(selectedIndex, 0, Math.Max(0, tabs.ItemCount - 1));
    }
    FocusScenarioTarget(dialog, scenario);
}

static void FocusScenarioTarget(Window dialog, Scenario scenario)
{
    if (scenario.RouteId == "style")
    {
        var name = FindVisualChildren<TextBox>(dialog).FirstOrDefault();
        if (name is not null)
        {
            name.Focus(NavigationMethod.Tab);
            name.SelectionStart = name.Text?.Length ?? 0;
            name.SelectionEnd = name.SelectionStart;
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
            selectedText.Focus(NavigationMethod.Tab);
            selectedText.CaretIndex = 0;
            return;
        }
    }

    if (!FindVisualChildren<Control>(dialog).Any(control => control.IsFocused))
        FindVisualChildren<Control>(dialog)
            .FirstOrDefault(control => control.IsTabStop && control.IsEffectivelyEnabled)
            ?.Focus(NavigationMethod.Tab);
}

static Semantics ReadSemantics(Window dialog)
{
    var controls = FindVisualChildren<Control>(dialog).Select(c => new ControlSemantic(
        Avalonia.Automation.AutomationProperties.GetAutomationId(c), c.GetType().Name, Avalonia.Automation.AutomationProperties.GetName(c), c.IsEffectivelyEnabled,
        c is CheckBox check ? check.IsChecked : c is ToggleButton toggle ? toggle.IsChecked : null,
        c is SelectingItemsControl selector ? selector.SelectedIndex : null)).ToArray();
    var buttons = ReadActionButtons(dialog);
    var focused = FindVisualChildren<Control>(dialog).FirstOrDefault(c => c.IsFocused);
    var defaultButton = buttons.FirstOrDefault(action => action.Button.IsDefault);
    var cancelButton = buttons.FirstOrDefault(action => action.Button.IsCancel);
    return new Semantics(
        focused is null ? null : Avalonia.Automation.AutomationProperties.GetAutomationId(focused),
        defaultButton.Button is null ? null : defaultButton.Text,
        cancelButton.Button is null ? null : cancelButton.Text,
        buttons.Select(action => action.Text).ToArray(), controls);
}

static IReadOnlyList<(Button Button, string Text)> ReadActionButtons(Window dialog)
{
    var actions = new List<(Button Button, string Text)>();
    foreach (var button in dialog.GetVisualDescendants().OfType<Button>())
    {
        if (button is ToggleButton or RepeatButton)
            continue;

        if (DialogSemanticText.TryResolveActionButtonText(
                button.IsEffectivelyVisible,
                Avalonia.Automation.AutomationProperties.GetName(button),
                UserFacingButtonContent(button.Content),
                out var text))
        {
            actions.Add((button, text));
        }
    }

    return actions;
}

static string? UserFacingButtonContent(object? content) => content switch
{
    string text => text,
    TextBlock textBlock => textBlock.Text,
    _ => null,
};

static RenderedFrame ReadFrame(
    Avalonia.Media.Imaging.WriteableBitmap bitmap,
    int outputWidth,
    int outputHeight)
{
    using var locked = bitmap.Lock();
    var colorType = locked.Format == PixelFormat.Bgra8888 ? SKColorType.Bgra8888 : SKColorType.Rgba8888;
    var info = new SKImageInfo(locked.Size.Width, locked.Size.Height, colorType, SKAlphaType.Premul);
    var sourcePixels = new byte[checked(locked.Size.Width * locked.Size.Height * 4)];
    for (var y = 0; y < locked.Size.Height; y++)
        System.Runtime.InteropServices.Marshal.Copy(
            locked.Address + y * locked.RowBytes,
            sourcePixels,
            y * locked.Size.Width * 4,
            locked.Size.Width * 4);
    using var source = new SKBitmap(info);
    System.Runtime.InteropServices.Marshal.Copy(sourcePixels, 0, source.GetPixels(), sourcePixels.Length);
    using var target = new SKBitmap(new SKImageInfo(
        outputWidth,
        outputHeight,
        SKColorType.Bgra8888,
        SKAlphaType.Premul));
    using (var canvas = new SKCanvas(target))
    {
        canvas.Clear(SKColors.Transparent);
        canvas.DrawBitmap(source, 0, 0);
    }
    using var image = SKImage.FromBitmap(target);
    using var data = image.Encode(SKEncodedImageFormat.Png, 100);
    var bgraPixels = new byte[checked(outputWidth * outputHeight * 4)];
    System.Runtime.InteropServices.Marshal.Copy(
        target.GetPixels(),
        bgraPixels,
        0,
        bgraPixels.Length);
    return new RenderedFrame(data?.ToArray() ?? [], bgraPixels, outputWidth, outputHeight);
}

static Capture Unsupported(Scenario scenario, string note, string? limitation = null, Semantics? semantics = null, PixelContent? fullContent = null, PixelContent? targetContent = null) => new(scenario.Id, "avalonia", scenario.RouteId, scenario.State, "unsupported", "", 0, 0, 0, 0, 96, 96, new Rect(0, 0, 0, 0), semantics ?? new Semantics(null, null, null, [], []), limitation ?? scenario.Limitation, note, null, fullContent, targetContent);
static IEnumerable<T> FindVisualChildren<T>(Visual root) where T : Visual
{
    if (root is T value) yield return value;
    foreach (var child in root.GetLogicalDescendants().OfType<T>()) yield return child;
}

sealed record RenderedFrame(byte[] Png, byte[] Pixels, int Width, int Height);
static string Required(string[] args, string option) { var i = Array.IndexOf(args, option); return i >= 0 && i + 1 < args.Length ? args[i + 1] : throw new ArgumentException($"Missing {option}."); }
static string? Optional(string[] args, string option) { var i = Array.IndexOf(args, option); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
static string Relative(string root, string path) => Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');
static string Safe(string value) => System.Text.RegularExpressions.Regex.Replace(value, "[^A-Za-z0-9._-]", "-");
static int TargetHeight(Scenario scenario) => scenario.RouteId == "compare-documents" && scenario.Tab == "More" ? 720 : 600;
static JsonSerializerOptions JsonOptions() => new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };
}

sealed class HarnessApp : Application
{
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        // Keep the capture host aligned with FreeW.App.Avalonia.App. Shared controls may
        // resolve brand-neutral and accent aliases even when their immediate chrome uses
        // literal fallbacks, so the harness must expose the production resource contract.
        AvaloniaThemeApplier.Apply(this, BrandThemes.FreeW, "FreeW");
    }
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<HarnessApp>()
        .UseSkia()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
