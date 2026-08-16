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
using FreeW.App.Avalonia.Editing;
using FreeW.Core.Model;

[assembly: AvaloniaTestApplication(typeof(ShellCaptureApp))]

var output = Required(args, "--output");
var includeContextual = args.Contains("--include-contextual", StringComparer.OrdinalIgnoreCase);
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
    await session.Dispatch(
        () => CaptureWidth(output, width, height, captures, includeContextual),
        CancellationToken.None);
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

static void CaptureWidth(
    string output,
    int width,
    int height,
    List<ShellCapture> captures,
    bool includeContextual)
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
            captures.Add(new ShellCapture(width, height, TabName(tab), tab.Tag?.ToString(), fileName, "static"));
        }
    }
    finally
    {
        window.Close();
    }

    if (!includeContextual)
        return;

    // Each fixture drives the real MainWindow's editor state, which in turn activates the
    // production IRibbonContextSource used by the ribbon renderer.  Keeping one window per
    // fixture prevents unrelated contexts (for example a table caret plus an object selection)
    // being combined into a synthetic tab strip.
    foreach (var fixture in ContextFixtures())
        CaptureContextualFixture(output, width, height, captures, fixture);
}

static IReadOnlyList<ContextFixture> ContextFixtures() =>
[
    new("drawing", ["drawing-format"]),
    new("picture", ["picture-format"]),
    new("chart", ["chart-design", "chart-format"]),
    new("smartart", ["smartart-design"]),
    new("table", ["table-design", "table-layout"]),
    new("header-footer", ["header-footer-design"]),
];

static void CaptureContextualFixture(
    string output,
    int width,
    int height,
    List<ShellCapture> captures,
    ContextFixture fixture)
{
    var window = new MainWindow([])
    {
        Width = width,
        Height = height,
        MinWidth = 0,
        MinHeight = 0,
    };

    try
    {
        window.Show();
        window.Measure(new Size(width, height));
        window.Arrange(new Rect(0, 0, width, height));
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs(DispatcherPriority.Render);

        ActivateContextFixture(window, fixture.Id);
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs(DispatcherPriority.Render);

        var tabs = window.GetVisualDescendants().OfType<TabControl>()
            .OrderByDescending(tab => tab.Items.Count)
            .FirstOrDefault()
            ?? throw new InvalidOperationException("FreeW Avalonia contextual fixture did not expose its ribbon TabControl.");

        foreach (var tabId in fixture.TabIds)
        {
            var tab = tabs.Items.OfType<TabItem>()
                .SingleOrDefault(item => string.Equals(item.Tag?.ToString(), tabId, StringComparison.Ordinal));
            if (tab is null)
                throw new InvalidOperationException(
                    $"Context fixture '{fixture.Id}' did not activate expected ribbon tab '{tabId}'.");

            tabs.SelectedItem = tab;
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs(DispatcherPriority.Render);
            using var frame = window.CaptureRenderedFrame();
            if (frame is null)
                throw new InvalidOperationException(
                    $"Avalonia compositor did not return a contextual frame for tab '{tabId}'.");

            var fileName = $"shell-{width}x{height}-context-{tabId}.png";
            var path = Path.Combine(output, fileName);
            frame.Save(path);
            if (new FileInfo(path).Length == 0)
                throw new InvalidOperationException($"Avalonia compositor wrote an empty contextual frame for tab '{tabId}'.");
            captures.Add(new ShellCapture(width, height, TabName(tab), tabId, fileName, fixture.Id));
        }
    }
    finally
    {
        window.Close();
    }
}

static void ActivateContextFixture(MainWindow window, string fixtureId)
{
    var editor = typeof(MainWindow)
        .GetField("_editor", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
        ?.GetValue(window) as DocumentView
        ?? throw new InvalidOperationException("FreeW contextual capture could not access MainWindow's real editor.");

    switch (fixtureId)
    {
        case "drawing":
            editor.InsertShape();
            SelectNewestFloating(editor, "Shape");
            break;
        case "picture":
            var image = new InlineImage(TinyPng(), 48, 48);
            editor.InsertInlineImage(image);
            // InsertInlineImage deliberately normalizes to inline mode for the user-facing Insert
            // command. The fixture needs a genuine selectable floating picture, so promote only
            // its just-inserted model object and reload through the real editor pipeline.
            image.Wrapping = ImageWrapping.Square;
            editor.LoadDocument(editor.Document);
            SelectNewestFloating(editor, "Image");
            break;
        case "chart":
            var chart = Chart.Create(ChartKind.Column, ["Q1", "Q2"], [4d, 7d], "Revenue", "Quarterly revenue");
            chart.Placement = new FloatingPlacement { Wrapping = ImageWrapping.Square };
            editor.InsertChart(chart);
            SelectNewestFloating(editor, "Chart");
            break;
        case "smartart":
            var smartArt = SmartArt.Create(SmartArtKind.Process, ["Plan", "Build", "Review"]);
            smartArt.Placement = new FloatingPlacement { Wrapping = ImageWrapping.Square };
            editor.InsertSmartArt(smartArt);
            SelectNewestFloating(editor, "SmartArt");
            break;
        case "table":
            editor.InsertTable(2, 2);
            var tableIndex = editor.Document.Blocks
                .Select((block, index) => (block, index))
                .LastOrDefault(item => item.block is Table).index;
            if (editor.Document.Blocks[tableIndex] is not Table)
                throw new InvalidOperationException("FreeW contextual table fixture did not create a table.");
            editor.PlaceCaretInCell(tableIndex, 0, 0, 0, 0);
            break;
        case "header-footer":
            editor.PlaceCaretInHeaderFooter(footer: false);
            break;
        default:
            throw new ArgumentOutOfRangeException(nameof(fixtureId), fixtureId, "Unknown contextual fixture.");
    }
}

static void SelectNewestFloating(DocumentView editor, string expectedKind)
{
    for (var blockIndex = editor.Document.Blocks.Count - 1; blockIndex >= 0; blockIndex--)
    {
        if (editor.Document.Blocks[blockIndex] is not Paragraph paragraph)
            continue;
        for (var runIndex = paragraph.Runs.Count - 1; runIndex >= 0; runIndex--)
        {
            editor.SelectFloating(blockIndex, runIndex);
            if (editor.SelectedDrawingObjectInfo is { } selection && selection.Kind == expectedKind)
                return;
        }
    }

    throw new InvalidOperationException($"FreeW contextual fixture could not select its expected {expectedKind} object.");
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

// 1x1 opaque PNG. The picture context is driven by a real floating InlineImage rather than an
// image-free mock object, while keeping the contextual fixture self-contained and deterministic.
static byte[] TinyPng() => Convert.FromBase64String(
    "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVQIHWP4z8DwHwAFgAI/ScLWhAAAAABJRU5ErkJggg==");

sealed record ShellCapture(
    int Width,
    int Height,
    string TabName,
    string? TabId,
    string FileName,
    string Fixture);
sealed record ContextFixture(string Id, IReadOnlyList<string> TabIds);
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
