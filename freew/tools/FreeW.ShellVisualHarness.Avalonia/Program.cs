// FreeW.ShellVisualHarness.Avalonia -- capture the actual Avalonia MainWindow chrome at
// deterministic sizes.  This intentionally records FreeW host-to-host evidence only; it does
// not claim that this application chrome is pixel-equivalent to Microsoft Word.

using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Skia;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Free.Shared.Theme;
using Free.Shared.Theme.Avalonia;
using FreeW.App.Avalonia;

[assembly: AvaloniaTestApplication(typeof(ShellCaptureApp))]

var output = Required(args, "--output");
var widths = Optional(args, "--widths")?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
    .Select(value => int.Parse(value, System.Globalization.CultureInfo.InvariantCulture))
    .ToArray() ?? [1500, 1100, 900, 750];
var height = int.TryParse(Optional(args, "--height"), out var requestedHeight) ? requestedHeight : 900;
if (widths.Any(width => width < 720) || height < 480)
    throw new ArgumentOutOfRangeException("The FreeW desktop shell cannot be captured below its supported minimum size.");

Directory.CreateDirectory(output);
var captures = new List<ShellCapture>();
var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(ShellCaptureApp).Assembly);
foreach (var width in widths)
{
    await session.Dispatch(() => CaptureWidth(output, width, height, captures), CancellationToken.None);
}

var manifest = new ShellManifest(
    Schema: "freex.freew.shell-visual-capture.v1",
    Renderer: "Avalonia headless Skia (UseHeadlessDrawing=false)",
    CapturedUtc: DateTimeOffset.UtcNow,
    Widths: widths,
    Height: height,
    Captures: captures);
var manifestPath = Path.Combine(output, "freew_avalonia_shell_capture_manifest.json");
File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, JsonOptions()));
Console.WriteLine($"Avalonia shell captures: {captures.Count}; manifest: {manifestPath}");
return captures.Count == 0 ? 2 : 0;

static void CaptureWidth(string output, int width, int height, List<ShellCapture> captures)
{
    var window = new MainWindow([])
    {
        Width = width,
        Height = height,
        MinWidth = 0,
        MinHeight = 0
    };

    try
    {
        window.Show();
        window.Measure(new Size(width, height));
        window.Arrange(new Rect(0, 0, width, height));
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs(DispatcherPriority.Render);

        var tabs = window.GetVisualDescendants().OfType<TabControl>()
            .OrderByDescending(tab => tab.Items.Count)
            .FirstOrDefault()
            ?? throw new InvalidOperationException("FreeW Avalonia shell did not expose its ribbon TabControl.");
        var tabItems = tabs.Items.OfType<TabItem>().ToArray();
        if (tabItems.Length < 2)
            throw new InvalidOperationException("FreeW Avalonia shell ribbon has no capturable top-level tabs.");

        // The File tab opens an external Backstage window asynchronously.  It is deliberately
        // omitted here: the established dialog evidence harness owns that route.  Every regular
        // and contextual tab present in the live shell is captured at each width.
        foreach (var tab in tabItems.Where(tab => !string.Equals(tab.Tag?.ToString(), "FileTab", StringComparison.Ordinal)))
        {
            tab.IsVisible = true;
            tabs.SelectedItem = tab;
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs(DispatcherPriority.Render);
            using var frame = window.CaptureRenderedFrame();
            if (frame is null)
                throw new InvalidOperationException($"Avalonia compositor did not return a frame for tab '{TabName(tab)}'.");

            var name = Sanitize(TabName(tab));
            var fileName = $"shell-{width}x{height}-{name}.png";
            var path = Path.Combine(output, fileName);
            frame.Save(path);
            if (new FileInfo(path).Length == 0)
                throw new InvalidOperationException($"Avalonia compositor wrote an empty frame for tab '{TabName(tab)}'.");
            captures.Add(new ShellCapture(width, height, TabName(tab), tab.Tag?.ToString(), fileName));
        }
    }
    finally
    {
        window.Close();
    }
}

static string TabName(TabItem tab) => tab.Header switch
{
    string text when !string.IsNullOrWhiteSpace(text) => text,
    TextBlock text when !string.IsNullOrWhiteSpace(text.Text) => text.Text!,
    _ when !string.IsNullOrWhiteSpace(tab.Tag?.ToString()) => tab.Tag!.ToString()!,
    _ => "unnamed-tab"
};

static string Sanitize(string value) => string.Concat(value.ToLowerInvariant().Select(character =>
    char.IsLetterOrDigit(character) ? character : '-')).Trim('-');

static string Required(string[] values, string option)
{
    var index = Array.IndexOf(values, option);
    return index >= 0 && index + 1 < values.Length
        ? Path.GetFullPath(values[index + 1])
        : throw new ArgumentException($"Missing {option}.");
}

static string? Optional(string[] values, string option)
{
    var index = Array.IndexOf(values, option);
    return index >= 0 && index + 1 < values.Length ? values[index + 1] : null;
}

static JsonSerializerOptions JsonOptions() => new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };

sealed record ShellCapture(int Width, int Height, string TabName, string? TabId, string FileName);
sealed record ShellManifest(string Schema, string Renderer, DateTimeOffset CapturedUtc, IReadOnlyList<int> Widths, int Height, IReadOnlyList<ShellCapture> Captures);

sealed class ShellCaptureApp : Application
{
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        AvaloniaThemeApplier.Apply(this, BrandThemes.FreeW, "FreeW");
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<ShellCaptureApp>()
        .UseSkia()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
