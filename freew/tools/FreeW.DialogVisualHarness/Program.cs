using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using SkiaSharp;

const string InventorySchema = "freew.dialog-route-inventory.v1";

var command = args.FirstOrDefault()?.ToLowerInvariant() ?? "help";
try
{
    return command switch
    {
        "inventory" => RunInventory(args.Skip(1).ToArray()),
        "compare" => RunCompare(args.Skip(1).ToArray()),
        "help" or "--help" or "-h" => Usage(),
        _ => Usage()
    };
}
catch (Exception ex)
{
    Console.Error.WriteLine($"FreeW.DialogVisualHarness failed: {ex.GetType().Name}: {ex.Message}");
    return 1;
}

static int Usage()
{
    Console.WriteLine("FreeW.DialogVisualHarness");
    Console.WriteLine("  inventory --repo-root <root> --output <dir> [--check]");
    Console.WriteLine("  compare --inventory <scenarios.json> --wpf <manifest.json> --avalonia <manifest.json> --output <dir> [--check]");
    return 0;
}

static int RunInventory(string[] args)
{
    var root = Path.GetFullPath(Required(args, "--repo-root"));
    var output = Path.GetFullPath(Required(args, "--output"));
    var check = args.Contains("--check", StringComparer.Ordinal);
    var inventory = BuildInventory(root);
    var json = JsonSerializer.Serialize(inventory, JsonOptions());
    var markdown = BuildInventoryMarkdown(inventory);
    Directory.CreateDirectory(output);
    var jsonPath = Path.Combine(output, "freew_dialog_route_inventory.json");
    var scenariosPath = Path.Combine(output, "freew_dialog_evidence_inventory.json");
    var markdownPath = Path.Combine(output, "freew_dialog_inventory.md");
    var scenarioJson = JsonSerializer.Serialize(new EvidenceInventory(inventory.Schema, inventory.GeneratedFromSha256, inventory.Scenarios), JsonOptions());
    if (check)
    {
        var fresh = File.Exists(jsonPath) && File.Exists(scenariosPath) && File.Exists(markdownPath)
            && File.ReadAllText(jsonPath) == json
            && File.ReadAllText(scenariosPath) == scenarioJson
            && File.ReadAllText(markdownPath) == markdown;
        Console.WriteLine(fresh ? $"inventory current: {output}" : $"inventory stale: {output}");
        return fresh ? 0 : 1;
    }
    File.WriteAllText(jsonPath, json, new UTF8Encoding(false));
    File.WriteAllText(scenariosPath, scenarioJson, new UTF8Encoding(false));
    File.WriteAllText(markdownPath, markdown, new UTF8Encoding(false));
    Console.WriteLine($"routes: {inventory.Routes.Count}");
    Console.WriteLine($"scenarios: {inventory.Scenarios.Count}");
    Console.WriteLine($"inventory: {jsonPath}");
    return 0;
}

static int RunCompare(string[] args)
{
    var inventoryPath = Path.GetFullPath(Required(args, "--inventory"));
    var wpfPath = Path.GetFullPath(Required(args, "--wpf"));
    var avaloniaPath = Path.GetFullPath(Required(args, "--avalonia"));
    var output = Path.GetFullPath(Required(args, "--output"));
    var check = args.Contains("--check", StringComparer.Ordinal);
    var inventory = Read<EvidenceInventory>(inventoryPath);
    var wpf = Read<CaptureManifest>(wpfPath);
    var avalonia = Read<CaptureManifest>(avaloniaPath);
    Directory.CreateDirectory(output);
    var rows = CompareCaptures(inventory, wpf, avalonia, output);
    var report = new ComparisonReport(
        Schema: "freew.dialog-visual-comparison.v1",
        GeneratedFromSha256: Sha256(string.Join("\n", File.ReadAllText(inventoryPath), File.ReadAllText(wpfPath), File.ReadAllText(avaloniaPath))),
        TargetDpi: 96,
        InventoryScenarioCount: inventory.Scenarios.Count,
        WpfCaptureCount: wpf.Captures.Count(c => c.Status == "captured"),
        AvaloniaCaptureCount: avalonia.Captures.Count(c => c.Status == "captured"),
        Rows: rows,
        Counts: rows.GroupBy(r => r.Classification).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal));
    var json = JsonSerializer.Serialize(report, JsonOptions());
    var markdown = BuildComparisonMarkdown(report);
    var html = BuildComparisonHtml(report);
    var jsonPath = Path.Combine(output, "freew_dialog_visual_comparison.json");
    var markdownPath = Path.Combine(output, "freew_dialog_visual_comparison.md");
    var htmlPath = Path.Combine(output, "freew_dialog_visual_comparison.html");
    var freshnessPath = Path.Combine(output, "freew_dialog_visual_freshness.json");
    var freshness = JsonSerializer.Serialize(new Freshness(
        report.GeneratedFromSha256,
        Sha256(File.ReadAllText(inventoryPath)),
        Sha256(File.ReadAllText(wpfPath)),
        Sha256(File.ReadAllText(avaloniaPath))), JsonOptions());
    if (check)
    {
        var fresh = File.Exists(freshnessPath) && File.ReadAllText(freshnessPath) == freshness;
        Console.WriteLine(fresh ? $"comparison current: {output}" : $"comparison stale: {output}");
        return fresh ? 0 : 1;
    }
    File.WriteAllText(jsonPath, json, new UTF8Encoding(false));
    File.WriteAllText(markdownPath, markdown, new UTF8Encoding(false));
    File.WriteAllText(htmlPath, html, new UTF8Encoding(false));
    File.WriteAllText(freshnessPath, freshness, new UTF8Encoding(false));
    Console.WriteLine($"scenarios: {report.InventoryScenarioCount}");
    Console.WriteLine($"wpf captured: {report.WpfCaptureCount}");
    Console.WriteLine($"avalonia captured: {report.AvaloniaCaptureCount}");
    Console.WriteLine("classifications: " + string.Join(", ", report.Counts.OrderBy(p => p.Key).Select(p => $"{p.Key}={p.Value}")));
    Console.WriteLine($"json: {jsonPath}");
    Console.WriteLine($"html: {htmlPath}");
    return report.Rows.Any(r => r.Classification == "genuine-visual-mismatch") ? 2 : 0;
}

static RouteInventory BuildInventory(string root)
{
    var sourceFiles = Directory.EnumerateFiles(Path.Combine(root, "freew"), "*.cs", SearchOption.AllDirectories)
        .Where(p => p.Contains("FreeW.App.Host", StringComparison.OrdinalIgnoreCase)
                 || p.Contains("FreeW.App.Avalonia", StringComparison.OrdinalIgnoreCase))
        .Where(p => !p.Contains("Tests", StringComparison.OrdinalIgnoreCase))
        .Where(p => !p.Contains("bin", StringComparison.OrdinalIgnoreCase) && !p.Contains("obj", StringComparison.OrdinalIgnoreCase))
        .OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToArray();
    var routes = new List<Route>();
    foreach (var path in sourceFiles)
    {
        var text = File.ReadAllText(path);
        var fileName = Path.GetFileName(path);
        if (!fileName.Contains("Dialog", StringComparison.OrdinalIgnoreCase)
            && !fileName.Equals("BackstageView.cs", StringComparison.OrdinalIgnoreCase)
            && !fileName.Equals("NotesPane.cs", StringComparison.OrdinalIgnoreCase)
            && !fileName.Equals("ScreenClipOverlay.cs", StringComparison.OrdinalIgnoreCase))
            continue;
        foreach (Match match in Regex.Matches(text, @"\bclass\s+(?<name>[A-Za-z_][A-Za-z0-9_]*(?:Dialog|Pane|Overlay))\b"))
        {
            var typeName = match.Groups["name"].Value;
            var host = path.Contains("FreeW.App.Avalonia", StringComparison.OrdinalIgnoreCase) ? "avalonia" : "wpf";
            var sourceRouteId = Kebab(typeName.EndsWith("Dialog", StringComparison.Ordinal) ? typeName[..^6] : typeName);
            var routeId = CanonicalRoute(host, sourceRouteId);
            if (routes.Any(r => r.Host == host && r.RouteId == routeId)) continue;
            var discoveredTabs = Regex.Matches(text, "(?:Header\\s*=\\s*|Label\\s*=\\s*)[\\\"'](?<tab>[^\\\"']+)[\\\"']")
                .Select(m => m.Groups["tab"].Value);
            var tabs = (KnownTabs(routeId).Concat(discoveredTabs).Distinct(StringComparer.OrdinalIgnoreCase).Take(16)).ToArray();
            var modalMode = text.Contains("ShowDialog", StringComparison.OrdinalIgnoreCase) || text.Contains("modal", StringComparison.OrdinalIgnoreCase)
                ? "modal" : text.Contains("Show()", StringComparison.Ordinal) || text.Contains("modeless", StringComparison.OrdinalIgnoreCase)
                    ? "modeless" : "modal-or-modeless";
            var limitation = fileName.Contains("File", StringComparison.OrdinalIgnoreCase) || text.Contains("PrintDialog", StringComparison.Ordinal)
                ? "native-picker-platform-limitation" : null;
            var surfaceKind = typeName.Equals("BackstageView", StringComparison.Ordinal) || sourceRouteId.StartsWith("backstage-", StringComparison.Ordinal)
                ? "backstage"
                : typeName.EndsWith("Pane", StringComparison.Ordinal)
                    ? "pane"
                    : typeName.EndsWith("Overlay", StringComparison.Ordinal)
                        ? "overlay"
                        : "dialog";
            routes.Add(new Route(host, routeId, typeName, Relative(root, path), modalMode, tabs, limitation, surfaceKind, StateIds(surfaceKind, tabs), sourceRouteId));
        }
    }
    AddBackstageRoutes(routes);
    routes = routes.OrderBy(r => r.Host).ThenBy(r => r.RouteId).ToList();
    var scenarios = new List<Scenario>();
    foreach (var route in routes)
    {
        foreach (var state in route.States ?? StateIds(route.SurfaceKind ?? "dialog", route.Tabs))
        {
            var tab = state.StartsWith("tab-", StringComparison.Ordinal)
                ? route.Tabs.FirstOrDefault(candidate => $"tab-{Kebab(candidate)}".Equals(state, StringComparison.OrdinalIgnoreCase))
                : null;
            var scenarioState = tab is null ? state : "relevant-tab";
            scenarios.Add(new Scenario($"{route.Host}.{route.RouteId}.{state}", route.Host, route.RouteId, scenarioState, tab, tab is null ? StateDescription(state) : $"Selected tab: {tab}.", route.Limitation, route.SurfaceKind));
        }
    }
    var inputHash = Sha256(string.Join("\n", sourceFiles.Select(File.ReadAllText)));
    return new RouteInventory(InventorySchema, 1, inputHash, routes, scenarios);
}

static void AddBackstageRoutes(List<Route> routes)
{
    var entries = new[] { "home", "new", "open", "info", "save", "save-a-copy", "close", "share", "save-as", "print", "export", "account", "options" };
    foreach (var host in new[] { "wpf", "avalonia" })
        foreach (var entry in entries)
            routes.Add(new Route(host, $"backstage-{entry}", "BackstageView", host == "wpf" ? "freew/FreeW.App.Host/MainWindow.cs" : "freew/FreeW.App.Avalonia/Backstage/BackstageView.cs", "modeless", [], entry is "open" or "save-as" or "print" ? "native-picker-platform-limitation" : null, "backstage", ["open"], $"backstage-{entry}"));
}

static List<ComparisonRow> CompareCaptures(EvidenceInventory inventory, CaptureManifest wpf, CaptureManifest avalonia, string output)
{
    var rows = new List<ComparisonRow>();
    var wpfByKey = wpf.Captures.ToDictionary(c => c.Key, StringComparer.OrdinalIgnoreCase);
    var avaloniaByKey = avalonia.Captures.ToDictionary(c => c.Key, StringComparer.OrdinalIgnoreCase);
    foreach (var pairKey in inventory.Scenarios.Select(PairKey).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(k => k, StringComparer.OrdinalIgnoreCase))
    {
        wpfByKey.TryGetValue($"wpf.{pairKey}", out var left);
        avaloniaByKey.TryGetValue($"avalonia.{pairKey}", out var right);
        if (left is null || right is null)
        {
            rows.Add(new ComparisonRow(pairKey, "host-missing", "not-implemented-on-host", null, null, null, left is null ? "No WPF route/state row was generated." : "No Avalonia route/state row was generated."));
            continue;
        }
        if (left.Status != "captured" || right.Status != "captured")
        {
            var limitation = left.Limitation ?? right.Limitation ?? "capture-hook-required";
            rows.Add(new ComparisonRow(pairKey, left.Status + "/" + right.Status, limitation, null, null, null, left.Note ?? right.Note));
            continue;
        }
        var leftPath = ResolveCapturePath(wpf, left.FullPngPath);
        var rightPath = ResolveCapturePath(avalonia, right.FullPngPath);
        var leftTarget = EnsureTargetCrop(left, wpf, leftPath, output, "wpf");
        var rightTarget = EnsureTargetCrop(right, avalonia, rightPath, output, "avalonia");
        using var a = DecodeAndScale(leftTarget, left.LogicalWidth, left.LogicalHeight);
        using var b = DecodeAndScale(rightTarget, right.LogicalWidth, right.LogicalHeight);
        var metrics = PixelMetrics.Compute(a, b);
        var heatmap = Path.Combine(output, "heatmaps", Safe(pairKey) + ".png");
        Directory.CreateDirectory(Path.GetDirectoryName(heatmap)!);
        PixelMetrics.WriteHeatmap(a, b, heatmap);
        var semantic = SemanticDiff(left.Semantics, right.Semantics);
        var classification = metrics.ChangedRatio > 0.03 ? "genuine-visual-mismatch" : semantic is not null ? "semantic-mismatch" : "pass";
        rows.Add(new ComparisonRow(pairKey, "captured/captured", classification, metrics, semantic, Relative(output, heatmap), null));
    }
    return rows;
}

static string ResolveCapturePath(CaptureManifest manifest, string path) =>
    Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(manifest.CaptureRoot, path.Replace('/', Path.DirectorySeparatorChar)));

static string PairKey(Scenario scenario)
{
    var separator = scenario.Id.IndexOf('.');
    return separator >= 0 ? scenario.Id[(separator + 1)..] : scenario.Id;
}

static string? SemanticDiff(Semantics left, Semantics right)
{
    var differences = new List<string>();
    if (!string.Equals(left.FocusedAutomationId, right.FocusedAutomationId, StringComparison.OrdinalIgnoreCase)) differences.Add("focus");
    if (!string.Equals(left.DefaultButton, right.DefaultButton, StringComparison.OrdinalIgnoreCase)) differences.Add("default-button");
    if (!string.Equals(left.CancelButton, right.CancelButton, StringComparison.OrdinalIgnoreCase)) differences.Add("cancel-button");
    if (!left.ActionButtonOrder.SequenceEqual(right.ActionButtonOrder, StringComparer.OrdinalIgnoreCase)) differences.Add("action-button-order");
    return differences.Count == 0 ? null : string.Join(",", differences);
}

static string EnsureTargetCrop(Capture capture, CaptureManifest manifest, string fullPath, string output, string host)
{
    if (!string.IsNullOrWhiteSpace(capture.TargetPngPath))
    {
        var recordedTarget = ResolveCapturePath(manifest, capture.TargetPngPath);
        if (File.Exists(recordedTarget)) return recordedTarget;
    }
    var target = Path.Combine(output, "crops", host, Safe(capture.ScenarioId) + ".png");
    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
    using var bitmap = SKBitmap.Decode(fullPath) ?? throw new InvalidOperationException($"Cannot decode {fullPath}");
    var rect = capture.TargetCrop;
    var crop = new SKBitmap(Math.Max(1, rect.Width), Math.Max(1, rect.Height));
    using var canvas = new SKCanvas(crop);
    canvas.Clear(SKColors.Transparent);
    canvas.DrawBitmap(bitmap, new SKRect(rect.X, rect.Y, rect.X + rect.Width, rect.Y + rect.Height), new SKRect(0, 0, crop.Width, crop.Height));
    using var image = SKImage.FromBitmap(crop);
    using var data = image.Encode(SKEncodedImageFormat.Png, 100);
    File.WriteAllBytes(target, data.ToArray());
    return target;
}

static SKBitmap DecodeAndScale(string path, int width, int height)
{
    using var source = SKBitmap.Decode(path) ?? throw new InvalidOperationException($"Cannot decode {path}");
    var result = new SKBitmap(Math.Max(1, width), Math.Max(1, height));
    using var canvas = new SKCanvas(result);
    canvas.Clear(SKColors.Transparent);
    canvas.DrawBitmap(source, new SKRect(0, 0, result.Width, result.Height));
    return result;
}

static T Read<T>(string path) => JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonOptions()) ?? throw new InvalidOperationException($"Invalid JSON: {path}");
static string Required(string[] args, string option) { var i = Array.IndexOf(args, option); return i >= 0 && i + 1 < args.Length ? args[i + 1] : throw new ArgumentException($"Missing {option}."); }
static string Relative(string root, string path) => Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');
static string Safe(string value) => Regex.Replace(value, "[^A-Za-z0-9._-]", "-");
static string Kebab(string value)
{
    var separated = Regex.Replace(value.Trim(), "([a-z0-9])([A-Z])", "$1-$2");
    return Regex.Replace(separated, "[^A-Za-z0-9]+", "-").Trim('-').ToLowerInvariant();
}
static string StateDescription(string state) => state switch { "initial" => "Default constructor state with initial keyboard focus.", "populated" => "Representative populated fields, selections, and checked options.", "validation-error" => "Representative validation or error state after invalid input.", "seeded" => "Seeded app-owned pane state after the route is opened.", "open" => "Opened app-owned overlay or Backstage pane state.", _ => $"Explicit route state: {state}." };
static IReadOnlyList<string> StateIds(string surfaceKind, IReadOnlyList<string> tabs) => surfaceKind switch
{
    "pane" => ["seeded"],
    "overlay" or "backstage" => ["open"],
    _ => new[] { "initial", "populated", "validation-error" }.Concat(tabs.Select(tab => $"tab-{Kebab(tab)}")).ToArray(),
};
static string CanonicalRoute(string host, string routeId) => (host, routeId) switch
{
    ("wpf", "paragraph-breaks") or ("wpf", "paragraph-indent") => "paragraph",
    ("wpf", "watermark-options") or ("avalonia", "watermark") => "watermark",
    ("wpf", "statistics") => "word-count",
    ("wpf", "about") or ("avalonia", "free-winfo") => "about",
    _ => routeId,
};
static IReadOnlyList<string> KnownTabs(string routeId) => routeId switch
{
    "options" => ["General", "AutoCorrect", "AutoFormat As You Type"],
    "page-setup" => ["Margins", "Paper", "Layout"],
    _ => []
};
static string Sha256(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
static JsonSerializerOptions JsonOptions() => new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };

static string BuildInventoryMarkdown(RouteInventory inventory)
{
    var b = new StringBuilder();
    b.AppendLine("# FreeW Dialog Route And Evidence Inventory");
    b.AppendLine();
    b.AppendLine($"> Generated by `FreeW.DialogVisualHarness`; source SHA-256 `{inventory.GeneratedFromSha256}`. Desktop DPI is recorded by captures and is not a parity exemption.");
    b.AppendLine();
    b.AppendLine($"Routes: **{inventory.Routes.Count}**. Exact scenarios: **{inventory.Scenarios.Count}**.");
    b.AppendLine();
    b.AppendLine("| Host | Route | Type | Lifecycle | Tabs | Limitation |");
    b.AppendLine("| --- | --- | --- | --- | --- | --- |");
    foreach (var r in inventory.Routes) b.AppendLine($"| {r.Host} | `{r.RouteId}` | `{r.TypeName}` | {r.ModalMode} | {string.Join(", ", r.Tabs)} | {r.Limitation ?? ""} |");
    b.AppendLine();
    b.AppendLine("## Scenario States");
    b.AppendLine();
    b.AppendLine("| Scenario | State | Tab | Evidence requirement |");
    b.AppendLine("| --- | --- | --- | --- |");
    foreach (var s in inventory.Scenarios) b.AppendLine($"| `{s.Id}` | {s.State} | {s.Tab ?? ""} | {s.Description} |");
    return b.ToString();
}

static string BuildComparisonMarkdown(ComparisonReport report)
{
    var b = new StringBuilder();
    b.AppendLine("# FreeW Paired Dialog Visual Comparison");
    b.AppendLine();
    b.AppendLine($"> Target: {report.TargetDpi} DPI logical pixels. Semantic checks and nonblank checks are reported separately from image parity.");
    b.AppendLine();
    b.AppendLine($"Inventory scenarios: **{report.InventoryScenarioCount}**. Captured WPF: **{report.WpfCaptureCount}**. Captured Avalonia: **{report.AvaloniaCaptureCount}**.");
    b.AppendLine();
    b.AppendLine("| Scenario | Capture | Classification | Changed ratio | Mean channel delta | Semantic diff | Heatmap |");
    b.AppendLine("| --- | --- | --- | ---: | ---: | --- | --- |");
    foreach (var r in report.Rows)
        b.AppendLine($"| `{r.ScenarioId}` | {r.CaptureStatus} | **{r.Classification}** | {r.Metrics?.ChangedRatio.ToString("P2", CultureInfo.InvariantCulture) ?? ""} | {r.Metrics?.MeanAbsoluteChannelDelta.ToString("F2", CultureInfo.InvariantCulture) ?? ""} | {r.SemanticDifference ?? ""} | {r.HeatmapPath ?? ""} |");
    b.AppendLine();
    b.AppendLine("## Honest Limitations");
    b.AppendLine();
    b.AppendLine("Native file/printer pickers, OS-owned modal focus, and host callbacks requiring a live shell are not inferred from semantic checks. They remain `native-picker-platform-limitation` or `capture-hook-required` until a foreground adapter records app-owned evidence.");
    return b.ToString();
}

static string BuildComparisonHtml(ComparisonReport report)
{
    var rows = string.Join("\n", report.Rows.Select(r => $"<tr><td>{System.Net.WebUtility.HtmlEncode(r.ScenarioId)}</td><td>{r.CaptureStatus}</td><td><b>{r.Classification}</b></td><td>{r.Metrics?.ChangedRatio.ToString("P2", CultureInfo.InvariantCulture) ?? ""}</td><td>{r.Metrics?.MeanAbsoluteChannelDelta.ToString("F2", CultureInfo.InvariantCulture) ?? ""}</td><td>{System.Net.WebUtility.HtmlEncode(r.SemanticDifference ?? "")}</td><td>{System.Net.WebUtility.HtmlEncode(r.HeatmapPath ?? "")}</td></tr>"));
    return $"<!doctype html><meta charset='utf-8'><title>FreeW dialog visual comparison</title><style>body{{font:14px Segoe UI,Arial;margin:24px}}table{{border-collapse:collapse}}td,th{{border:1px solid #bbb;padding:6px;text-align:left}}b{{color:#a33}}</style><h1>FreeW Paired Dialog Visual Comparison</h1><p>96-DPI logical target. Semantic checks are separate from visual parity.</p><table><thead><tr><th>Scenario</th><th>Capture</th><th>Classification</th><th>Changed ratio</th><th>Mean delta</th><th>Semantic diff</th><th>Heatmap</th></tr></thead><tbody>{rows}</tbody></table>";
}

public record RouteInventory(string Schema, int SchemaVersion, string GeneratedFromSha256, IReadOnlyList<Route> Routes, IReadOnlyList<Scenario> Scenarios);
public record EvidenceInventory(string Schema, string GeneratedFromSha256, IReadOnlyList<Scenario> Scenarios);
public record Route(string Host, string RouteId, string TypeName, string SourcePath, string ModalMode, IReadOnlyList<string> Tabs, string? Limitation, string SurfaceKind = "dialog", IReadOnlyList<string>? States = null, string? SourceRouteId = null);
public record Scenario(string Id, string Host, string RouteId, string State, string? Tab, string Description, string? Limitation, string? SurfaceKind = null);
public record CaptureManifest(string Schema, int SchemaVersion, string Host, string CaptureRoot, IReadOnlyList<Capture> Captures);
public record Capture(string ScenarioId, string Host, string RouteId, string State, string Status, string FullPngPath, int LogicalWidth, int LogicalHeight, int ActualWidth, int ActualHeight, double DpiX, double DpiY, Rect TargetCrop, Semantics Semantics, string? Limitation, string? Note, string? TargetPngPath = null)
{
    public string Key => ScenarioId;
}
public record Rect(int X, int Y, int Width, int Height);
public record Semantics(string? FocusedAutomationId, string? DefaultButton, string? CancelButton, IReadOnlyList<string> ActionButtonOrder, IReadOnlyList<ControlSemantic> Controls);
public record ControlSemantic(string? AutomationId, string Type, string? Name, bool Enabled, bool? Checked, int? SelectedIndex);
public record Metrics(long ComparedPixels, long ChangedPixels, double ChangedRatio, double MeanAbsoluteChannelDelta, double P95AbsoluteChannelDelta, double LuminanceSimilarity, int PerceptualHashDistance);
public record ComparisonRow(string ScenarioId, string CaptureStatus, string Classification, Metrics? Metrics, string? SemanticDifference, string? HeatmapPath, string? Note);
public record ComparisonReport(string Schema, string GeneratedFromSha256, int TargetDpi, int InventoryScenarioCount, int WpfCaptureCount, int AvaloniaCaptureCount, IReadOnlyList<ComparisonRow> Rows, IReadOnlyDictionary<string, int> Counts);
public record Freshness(string ComparisonInputSha256, string InventorySha256, string WpfSha256, string AvaloniaSha256);

static class PixelMetrics
{
    public static Metrics Compute(SKBitmap a, SKBitmap b)
    {
        var count = Math.Min(a.Width * a.Height, b.Width * b.Height);
        long changed = 0; double total = 0; var deltas = new List<double>(count);
        double lumSq = 0; double lumTotal = 0;
        for (var i = 0; i < count; i++)
        {
            var x = i % a.Width; var y = i / a.Width;
            var p = a.GetPixel(x, y); var q = b.GetPixel(i % b.Width, i / b.Width);
            var d = (Math.Abs(p.Red - q.Red) + Math.Abs(p.Green - q.Green) + Math.Abs(p.Blue - q.Blue)) / 3.0;
            total += d; deltas.Add(d); if (d > 8) changed++;
            var l = (p.Red * 0.2126 + p.Green * 0.7152 + p.Blue * 0.0722) / 255;
            var m = (q.Red * 0.2126 + q.Green * 0.7152 + q.Blue * 0.0722) / 255;
            lumSq += (l - m) * (l - m); lumTotal += 1;
        }
        deltas.Sort();
        var hashDistance = AverageHashDistance(a, b);
        return new Metrics(count, changed, count == 0 ? 1 : (double)changed / count, count == 0 ? 0 : total / count, count == 0 ? 0 : deltas[(int)Math.Min(deltas.Count - 1, deltas.Count * .95)], 1 - Math.Sqrt(lumSq / Math.Max(1, lumTotal)), hashDistance);
    }

    public static void WriteHeatmap(SKBitmap a, SKBitmap b, string path)
    {
        using var map = new SKBitmap(Math.Min(a.Width, b.Width), Math.Min(a.Height, b.Height));
        for (var y = 0; y < map.Height; y++) for (var x = 0; x < map.Width; x++)
        {
            var p = a.GetPixel(x, y); var q = b.GetPixel(x, y);
            var d = Math.Clamp((Math.Abs(p.Red - q.Red) + Math.Abs(p.Green - q.Green) + Math.Abs(p.Blue - q.Blue)) / 3, 0, 255);
            map.SetPixel(x, y, new SKColor((byte)d, (byte)Math.Max(0, 80 - d / 3), (byte)Math.Max(0, 255 - d), 255));
        }
        using var image = SKImage.FromBitmap(map); using var data = image.Encode(SKEncodedImageFormat.Png, 100); File.WriteAllBytes(path, data.ToArray());
    }

    private static int AverageHashDistance(SKBitmap a, SKBitmap b)
    {
        var bitsA = Hash(a); var bitsB = Hash(b); var distance = 0;
        for (var i = 0; i < bitsA.Length; i++) if (bitsA[i] != bitsB[i]) distance++;
        return distance;
    }
    private static bool[] Hash(SKBitmap b)
    {
        var vals = new double[64]; for (var y = 0; y < 8; y++) for (var x = 0; x < 8; x++) { var p = b.GetPixel(x * b.Width / 8, y * b.Height / 8); vals[y * 8 + x] = p.Red * .2126 + p.Green * .7152 + p.Blue * .0722; }
        var avg = vals.Average(); return vals.Select(v => v >= avg).ToArray();
    }
}
