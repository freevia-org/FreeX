using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Free.Shared.AppServices;
using FreeW.DialogVisualHarness;
using SkiaSharp;

const string InventorySchema = "freew.dialog-route-inventory.v1";

var firstArgument = args
    .FirstOrDefault(argument => !string.Equals(argument, "--", StringComparison.Ordinal))
    ?.ToLowerInvariant();
var command = firstArgument switch
{
    "inventory" => "inventory",
    "compare" => "compare",
    _ when args.Contains("--repo-root", StringComparer.Ordinal) => "inventory",
    _ when args.Contains("--wpf", StringComparer.Ordinal) => "compare",
    _ => "help",
};
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
    Console.WriteLine("  compare --inventory <scenarios.json> --wpf <manifest.json> --avalonia <manifest.json> --output <dir> [--baseline <report.json> --refresh-route <route>] [--check]");
    return 0;
}

static int RunInventory(string[] args)
{
    var root = Path.GetFullPath(Required(args, "--repo-root"));
    var output = Path.GetFullPath(Required(args, "--output"));
    var check = args.Contains("--check", StringComparer.Ordinal);
    var inventory = BuildInventory(root);
    var json = VisualEvidenceManifestIO.Serialize(inventory, JsonOptions());
    var markdown = BuildInventoryMarkdown(inventory);
    Directory.CreateDirectory(output);
    var jsonPath = Path.Combine(output, "freew_dialog_route_inventory.json");
    var scenariosPath = Path.Combine(output, "freew_dialog_evidence_inventory.json");
    var markdownPath = Path.Combine(output, "freew_dialog_inventory.md");
    var scenarioJson = VisualEvidenceManifestIO.Serialize(
        new EvidenceInventory(inventory.Schema, inventory.GeneratedFromSha256, inventory.Scenarios),
        JsonOptions());
    if (check)
    {
        var fresh = File.Exists(jsonPath) && File.Exists(scenariosPath) && File.Exists(markdownPath)
            && File.ReadAllText(jsonPath) == json
            && File.ReadAllText(scenariosPath) == scenarioJson
            && File.ReadAllText(markdownPath) == markdown;
        Console.WriteLine(fresh ? $"inventory current: {output}" : $"inventory stale: {output}");
        return fresh ? 0 : 1;
    }
    VisualEvidenceManifestIO.Write(jsonPath, inventory, JsonOptions());
    VisualEvidenceManifestIO.Write(
        scenariosPath,
        new EvidenceInventory(inventory.Schema, inventory.GeneratedFromSha256, inventory.Scenarios),
        JsonOptions());
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
    var baselinePath = Optional(args, "--baseline");
    var refreshRoute = Optional(args, "--refresh-route");
    if ((baselinePath is null) != (refreshRoute is null))
        throw new ArgumentException("--baseline and --refresh-route must be supplied together.");
    var check = args.Contains("--check", StringComparer.Ordinal);
    var inventory = Read<EvidenceInventory>(inventoryPath);
    var wpf = Read<CaptureManifest>(wpfPath);
    var avalonia = Read<CaptureManifest>(avaloniaPath);
    Directory.CreateDirectory(output);
    var rows = CompareCaptures(inventory, wpf, avalonia, output);
    var inventoryScenarioCount = inventory.Scenarios.Count;
    var wpfCaptureCount = wpf.Captures.Count(c => c.Status == "captured");
    var avaloniaCaptureCount = avalonia.Captures.Count(IsValidatedAvaloniaCapture);
    var generatedFromSha256 = Sha256(string.Join("\n", File.ReadAllText(inventoryPath), File.ReadAllText(wpfPath), File.ReadAllText(avaloniaPath)));
    var targetDpi = 96;
    IReadOnlyDictionary<string, int> counts;
    if (baselinePath is not null)
    {
        var baseline = Read<ComparisonReport>(Path.GetFullPath(baselinePath));
        var merged = ComparisonReportMerger.Merge(baseline, rows, refreshRoute!);
        rows = merged.Rows.ToList();
        inventoryScenarioCount = merged.InventoryScenarioCount;
        wpfCaptureCount = merged.WpfCaptureCount;
        avaloniaCaptureCount = merged.AvaloniaCaptureCount;
        generatedFromSha256 = merged.GeneratedFromSha256;
        targetDpi = merged.TargetDpi;
        counts = merged.Counts;
    }
    else
    {
        inventoryScenarioCount = inventory.Scenarios.Count;
        wpfCaptureCount = wpf.Captures.Count(c => c.Status == "captured");
        avaloniaCaptureCount = avalonia.Captures.Count(IsValidatedAvaloniaCapture);
        counts = rows.GroupBy(r => r.Classification).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
    }
    var report = new ComparisonReport(
        Schema: "freew.dialog-visual-comparison.v1",
        GeneratedFromSha256: generatedFromSha256,
        TargetDpi: targetDpi,
        InventoryScenarioCount: inventoryScenarioCount,
        WpfCaptureCount: wpfCaptureCount,
        AvaloniaCaptureCount: avaloniaCaptureCount,
        Rows: rows,
        Counts: counts,
        Scope: new ComparisonScope(
            Kind: "canonical-inputs-only",
            Description: "Rows and counts cover only the inventory and WPF/Avalonia capture manifests supplied to this compare invocation.",
            RefreshInstruction: "Route-local evidence remains outside this aggregate until it is merged with --baseline and --refresh-route."));
    var json = VisualEvidenceManifestIO.Serialize(report, JsonOptions());
    var markdown = BuildComparisonMarkdown(report);
    var html = BuildComparisonHtml(report);
    var jsonPath = Path.Combine(output, "freew_dialog_visual_comparison.json");
    var markdownPath = Path.Combine(output, "freew_dialog_visual_comparison.md");
    var htmlPath = Path.Combine(output, "freew_dialog_visual_comparison.html");
    var freshnessPath = Path.Combine(output, "freew_dialog_visual_freshness.json");
    var freshnessRecord = new Freshness(
        report.GeneratedFromSha256,
        Sha256(File.ReadAllText(inventoryPath)),
        Sha256(File.ReadAllText(wpfPath)),
        Sha256(File.ReadAllText(avaloniaPath)));
    var freshness = VisualEvidenceManifestIO.Serialize(freshnessRecord, JsonOptions());
    if (check)
    {
        var fresh = File.Exists(jsonPath) && File.Exists(markdownPath) && File.Exists(htmlPath) && File.Exists(freshnessPath)
            && File.ReadAllText(jsonPath) == json
            && File.ReadAllText(markdownPath) == markdown
            && File.ReadAllText(htmlPath) == html
            && File.ReadAllText(freshnessPath) == freshness;
        Console.WriteLine(fresh ? $"comparison current: {output}" : $"comparison stale: {output}");
        return fresh ? 0 : 1;
    }
    VisualEvidenceManifestIO.Write(jsonPath, report, JsonOptions());
    File.WriteAllText(markdownPath, markdown, new UTF8Encoding(false));
    File.WriteAllText(htmlPath, html, new UTF8Encoding(false));
    VisualEvidenceManifestIO.Write(freshnessPath, freshnessRecord, JsonOptions());
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
        var classMatches = Regex.Matches(text, @"\bclass\s+(?<name>[A-Za-z_][A-Za-z0-9_]*(?:Dialog|Pane|Overlay))\b");
        for (var classIndex = 0; classIndex < classMatches.Count; classIndex++)
        {
            var match = classMatches[classIndex];
            var typeName = match.Groups["name"].Value;
            var host = path.Contains("FreeW.App.Avalonia", StringComparison.OrdinalIgnoreCase) ? "avalonia" : "wpf";
            var sourceRouteId = Kebab(typeName.EndsWith("Dialog", StringComparison.Ordinal) ? typeName[..^6] : typeName);
            var routeId = FreeWDialogEvidenceCatalog.CanonicalRoute(host, sourceRouteId);
            if (routes.Any(r => r.Host == host && r.RouteId == routeId)) continue;
            var classEnd = classIndex + 1 < classMatches.Count ? classMatches[classIndex + 1].Index : text.Length;
            var classText = text[match.Index..classEnd];
            var discoveredTabs = Regex.Matches(classText, "(?:Header\\s*=\\s*|Label\\s*=\\s*)[\\\"'](?<tab>[^\\\"']+)[\\\"']")
                .Select(m => m.Groups["tab"].Value);
            var tabs = FreeWDialogEvidenceCatalog.ValidTabs(
                routeId,
                FreeWDialogEvidenceCatalog.KnownTabs(routeId)
                    .Concat(discoveredTabs)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(16)).ToArray();
            var modalMode = text.Contains("ShowDialog", StringComparison.OrdinalIgnoreCase) || text.Contains("modal", StringComparison.OrdinalIgnoreCase)
                ? "modal" : text.Contains("Show()", StringComparison.Ordinal) || text.Contains("modeless", StringComparison.OrdinalIgnoreCase)
                    ? "modeless" : "modal-or-modeless";
            var limitation = typeName.Equals("CupsPrintDialog", StringComparison.Ordinal)
                ? null
                : fileName.Contains("File", StringComparison.OrdinalIgnoreCase) || text.Contains("PrintDialog", StringComparison.Ordinal)
                ? "native-picker-platform-limitation" : null;
            var surfaceKind = typeName.Equals("BackstageView", StringComparison.Ordinal) || sourceRouteId.StartsWith("backstage-", StringComparison.Ordinal)
                ? "backstage"
                : typeName.EndsWith("Pane", StringComparison.Ordinal)
                    ? "pane"
                    : typeName.EndsWith("Overlay", StringComparison.Ordinal)
                        ? "overlay"
                        : "dialog";
            routes.Add(new Route(
                host,
                routeId,
                typeName,
                Relative(root, path),
                modalMode,
                tabs,
                limitation,
                surfaceKind,
                FreeWDialogEvidenceCatalog.ValidStates(routeId, surfaceKind, tabs),
                sourceRouteId));
        }
    }
    AddBackstageRoutes(routes);
    AddKnownAvaloniaRoutes(routes);
    routes = routes.OrderBy(r => r.Host).ThenBy(r => r.RouteId).ToList();
    var scenarios = new List<Scenario>();
    foreach (var route in routes)
    {
        foreach (var state in route.States ?? FreeWDialogEvidenceCatalog.StateIds(route.SurfaceKind ?? "dialog", route.Tabs))
        {
            var tab = state.StartsWith("tab-", StringComparison.Ordinal)
                ? route.Tabs.FirstOrDefault(candidate => $"tab-{Kebab(candidate)}".Equals(state, StringComparison.OrdinalIgnoreCase))
                : null;
            var scenarioState = tab is null ? state : "relevant-tab";
            scenarios.Add(new Scenario(
                $"{route.Host}.{route.RouteId}.{state}",
                route.Host,
                route.RouteId,
                scenarioState,
                tab,
                tab is null ? FreeWDialogEvidenceCatalog.StateDescription(state) : $"Selected tab: {tab}.",
                route.Limitation,
                route.SurfaceKind));
        }
    }
    var inputHash = Sha256(string.Join("\n", sourceFiles.Select(File.ReadAllText)));
    return new RouteInventory(InventorySchema, 1, inputHash, routes, scenarios);
}

static void AddBackstageRoutes(List<Route> routes)
{
    foreach (var host in new[] { "wpf", "avalonia" })
    {
        foreach (var route in FreeWDialogEvidenceCatalog.Routes.Where(route => route.SurfaceKind == FreeWDialogSurfaceKind.Backstage))
        {
            routes.Add(new Route(
                host,
                route.RouteId,
                route.ForHost(host == "wpf" ? FreeWDialogHost.Wpf : FreeWDialogHost.Avalonia)!.DialogTypeName,
                host == "wpf" ? "freew/FreeW.App.Host/MainWindow.cs" : "freew/FreeW.App.Avalonia/Backstage/BackstageView.cs",
                "modeless",
                [],
                null,
                "backstage",
                ["open"],
                route.RouteId));
        }
    }
}

static void AddKnownAvaloniaRoutes(List<Route> routes)
{
    if (routes.Any(route => route.Host == "avalonia" && route.RouteId == "compare-documents")) return;
    routes.Add(new Route(
        "avalonia",
        "compare-documents",
        "CompareDocumentsDialog",
        "freew/FreeW.App.Avalonia/ReviewCompareCombineDialogs.cs",
        "modal",
        ["More"],
        null,
        "dialog",
        ["initial", "populated", "validation-error", "tab-more"],
        "compare-documents"));
}

static List<ComparisonRow> CompareCaptures(EvidenceInventory inventory, CaptureManifest wpf, CaptureManifest avalonia, string output)
{
    var rows = new List<ComparisonRow>();
    var wpfByKey = wpf.Captures.ToDictionary(c => c.Key, StringComparer.OrdinalIgnoreCase);
    var avaloniaByKey = avalonia.Captures.ToDictionary(c => c.Key, StringComparer.OrdinalIgnoreCase);
    var wpfKeys = inventory.Scenarios.Where(s => s.Host == "wpf").Select(PairKey).Distinct(StringComparer.OrdinalIgnoreCase).ToHashSet(StringComparer.OrdinalIgnoreCase);
    var avaloniaKeys = inventory.Scenarios.Where(s => s.Host == "avalonia").Select(PairKey).Distinct(StringComparer.OrdinalIgnoreCase).ToHashSet(StringComparer.OrdinalIgnoreCase);
    foreach (var pairKey in wpfKeys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase))
    {
        wpfByKey.TryGetValue($"wpf.{pairKey}", out var left);
        avaloniaByKey.TryGetValue($"avalonia.{pairKey}", out var right);
        if (left is null || right is null)
        {
            var routeExistsOnOtherHost = left is not null
                ? inventory.Scenarios.Any(s => s.Host == "avalonia" && s.RouteId.Equals(left.RouteId, StringComparison.OrdinalIgnoreCase))
                : right is not null && inventory.Scenarios.Any(s => s.Host == "wpf" && s.RouteId.Equals(right.RouteId, StringComparison.OrdinalIgnoreCase));
            var classification = routeExistsOnOtherHost ? "state-not-applicable" : "product-parity-gap";
            var note = left is null
                ? "No WPF route/state row was generated."
                : routeExistsOnOtherHost
                    ? "The equivalent Avalonia route exists, but this WPF authority tab/state is not applicable on Avalonia."
                    : "WPF authority route/state is absent on Avalonia pending the product parity slice.";
            rows.Add(new ComparisonRow(pairKey, routeExistsOnOtherHost ? "state-not-applicable" : "host-missing", classification, null, null, null, note));
            continue;
        }
        if (left.Status != "captured" || right.Status != "captured")
        {
            var pendingClassification = left.Limitation ?? right.Limitation
                ?? (left.Status != "captured" ? "pending-wpf-factory" : "pending-avalonia-factory");
            rows.Add(new ComparisonRow(pairKey, left.Status + "/" + right.Status, pendingClassification, null, null, null, left.Note ?? right.Note));
            continue;
        }
        var leftPath = ResolveCapturePath(wpf, left.FullPngPath);
        var rightPath = ResolveCapturePath(avalonia, right.FullPngPath);
        var leftTarget = EnsureTargetCrop(left, wpf, leftPath, output, "wpf");
        var rightTarget = EnsureTargetCrop(right, avalonia, rightPath, output, "avalonia");
        using var a = DecodeAndScale(leftTarget, left.LogicalWidth, left.LogicalHeight);
        using var b = DecodeAndScale(rightTarget, right.LogicalWidth, right.LogicalHeight);
        var leftContent = left.TargetPixelContent ?? PixelContentMetrics.Compute(a);
        var rightContent = right.TargetPixelContent ?? PixelContentMetrics.Compute(b);
        if (!leftContent.PassesContentGate || !rightContent.PassesContentGate)
        {
            var failures = new List<string>();
            if (!leftContent.PassesContentGate) failures.Add($"WPF: {leftContent.Failure}");
            if (!rightContent.PassesContentGate) failures.Add($"Avalonia: {rightContent.Failure}");
            rows.Add(new ComparisonRow(pairKey, "invalid-content", "invalid-capture-content", null, null, null, string.Join(" | ", failures), leftContent, rightContent));
            continue;
        }
        var metrics = PixelMetrics.Compute(a, b);
        var heatmap = Path.Combine(output, "heatmaps", Safe(pairKey) + ".png");
        Directory.CreateDirectory(Path.GetDirectoryName(heatmap)!);
        PixelMetrics.WriteHeatmap(a, b, heatmap);
        var semantic = SemanticDiff(left.Semantics, right.Semantics);
        var visualClassification = metrics.ChangedRatio > 0.03 ? "genuine-visual-mismatch" : semantic is not null ? "semantic-mismatch" : "pass";
        rows.Add(new ComparisonRow(pairKey, "captured/captured", visualClassification, metrics, semantic, Relative(output, heatmap), null, leftContent, rightContent));
    }
    foreach (var pairKey in avaloniaKeys.Except(wpfKeys, StringComparer.OrdinalIgnoreCase).OrderBy(k => k, StringComparer.OrdinalIgnoreCase))
    {
        avaloniaByKey.TryGetValue($"avalonia.{pairKey}", out var extension);
        var targetContent = extension?.TargetPixelContent;
        var validExtension = extension is not null && IsValidatedAvaloniaCapture(extension);
        rows.Add(new ComparisonRow(pairKey, validExtension ? "avalonia-extension" : "invalid-content", validExtension ? "avalonia-extension" : "invalid-capture-content", null, null, null,
            validExtension ? "Avalonia-owned route/state; outside the WPF-authority pairing set." : $"Avalonia extension failed capture content validation: {targetContent?.Failure ?? extension?.Note ?? "missing pixel-content metadata"}", null, targetContent));
    }
    return rows;
}

static bool IsValidatedAvaloniaCapture(Capture capture) =>
    capture.Status == "captured"
    && capture.FullPixelContent?.PassesContentGate == true
    && capture.TargetPixelContent?.PassesContentGate == true;

static string ResolveCapturePath(CaptureManifest manifest, string path) =>
    VisualEvidencePathPolicy.ResolveDeclaredPath(
        manifest.CaptureRoot,
        path.Replace('/', Path.DirectorySeparatorChar));

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

static T Read<T>(string path) where T : class =>
    VisualEvidenceManifestIO.Read<T>(
        path,
        JsonOptions(),
        invalidExceptionFactory: () => new InvalidOperationException($"Invalid JSON: {path}"));
static string Required(string[] args, string option) { var i = Array.IndexOf(args, option); return i >= 0 && i + 1 < args.Length ? args[i + 1] : throw new ArgumentException($"Missing {option}."); }
static string? Optional(string[] args, string option) { var i = Array.IndexOf(args, option); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
static string Relative(string root, string path) =>
    VisualEvidencePathPolicy.NormalizeRelativePath(root, path);
static string Safe(string value) => VisualEvidenceTextPolicy.ToAsciiSafeArtifactName(value);
static string Kebab(string value)
{
    var separated = Regex.Replace(value.Trim(), "([a-z0-9])([A-Z])", "$1-$2");
    return Regex.Replace(separated, "[^A-Za-z0-9]+", "-").Trim('-').ToLowerInvariant();
}
static string Sha256(string text) => VisualEvidenceHash.Sha256Text(text);
static JsonSerializerOptions JsonOptions() =>
    VisualEvidenceManifestIO.CreateJsonOptions(stringEnums: false);

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
    b.AppendLine($"**Evidence scope:** `{report.Scope.Kind}`. {report.Scope.Description} {report.Scope.RefreshInstruction}");
    b.AppendLine();
    b.AppendLine($"Inventory scenarios: **{report.InventoryScenarioCount}**. Captured WPF: **{report.WpfCaptureCount}**. Captured Avalonia: **{report.AvaloniaCaptureCount}**.");
    b.AppendLine();
    b.AppendLine("| Scenario | Capture | Classification | WPF content | Avalonia content | Changed ratio | Mean channel delta | Semantic diff | Heatmap |");
    b.AppendLine("| --- | --- | --- | --- | --- | ---: | ---: | --- | --- |");
    foreach (var r in report.Rows)
        b.AppendLine($"| `{r.ScenarioId}` | {r.CaptureStatus} | **{r.Classification}** | {ContentLabel(r.WpfContent)} | {ContentLabel(r.AvaloniaContent)} | {r.Metrics?.ChangedRatio.ToString("P2", CultureInfo.InvariantCulture) ?? ""} | {r.Metrics?.MeanAbsoluteChannelDelta.ToString("F2", CultureInfo.InvariantCulture) ?? ""} | {r.SemanticDifference ?? ""} | {r.HeatmapPath ?? ""} |");
    b.AppendLine();
    b.AppendLine("## Honest Limitations");
    b.AppendLine();
    b.AppendLine("Native file/printer pickers, OS-owned modal focus, and host callbacks requiring a live shell are not inferred from semantic checks. They remain `native-picker-platform-limitation` or `capture-hook-required` until a foreground adapter records app-owned evidence.");
    return b.ToString();
}

static string BuildComparisonHtml(ComparisonReport report)
{
    var rows = string.Join("\n", report.Rows.Select(r => $"<tr><td>{System.Net.WebUtility.HtmlEncode(r.ScenarioId)}</td><td>{r.CaptureStatus}</td><td><b>{r.Classification}</b></td><td>{ContentLabel(r.WpfContent)}</td><td>{ContentLabel(r.AvaloniaContent)}</td><td>{r.Metrics?.ChangedRatio.ToString("P2", CultureInfo.InvariantCulture) ?? ""}</td><td>{r.Metrics?.MeanAbsoluteChannelDelta.ToString("F2", CultureInfo.InvariantCulture) ?? ""}</td><td>{System.Net.WebUtility.HtmlEncode(r.SemanticDifference ?? "")}</td><td>{System.Net.WebUtility.HtmlEncode(r.HeatmapPath ?? "")}</td></tr>"));
    return $"<!doctype html><meta charset='utf-8'><title>FreeW dialog visual comparison</title><style>body{{font:14px Segoe UI,Arial;margin:24px}}table{{border-collapse:collapse}}td,th{{border:1px solid #bbb;padding:6px;text-align:left}}b{{color:#a33}}</style><h1>FreeW Paired Dialog Visual Comparison</h1><p>96-DPI logical target. Semantic checks and pixel-content gates are separate from visual parity.</p><p><strong>Evidence scope:</strong> <code>{System.Net.WebUtility.HtmlEncode(report.Scope.Kind)}</code>. {System.Net.WebUtility.HtmlEncode(report.Scope.Description)} {System.Net.WebUtility.HtmlEncode(report.Scope.RefreshInstruction)}</p><table><thead><tr><th>Scenario</th><th>Capture</th><th>Classification</th><th>WPF content</th><th>Avalonia content</th><th>Changed ratio</th><th>Mean delta</th><th>Semantic diff</th><th>Heatmap</th></tr></thead><tbody>{rows}</tbody></table>";
}

static string ContentLabel(PixelContent? content) => content is null ? "" : content.PassesContentGate ? $"pass ({content.ContentPixelRatio:P1} painted)" : $"fail: {content.Failure}";

public record RouteInventory(string Schema, int SchemaVersion, string GeneratedFromSha256, IReadOnlyList<Route> Routes, IReadOnlyList<Scenario> Scenarios);
public record EvidenceInventory(string Schema, string GeneratedFromSha256, IReadOnlyList<Scenario> Scenarios);
public record Route(string Host, string RouteId, string TypeName, string SourcePath, string ModalMode, IReadOnlyList<string> Tabs, string? Limitation, string SurfaceKind = "dialog", IReadOnlyList<string>? States = null, string? SourceRouteId = null);
public record Scenario(string Id, string Host, string RouteId, string State, string? Tab, string Description, string? Limitation, string? SurfaceKind = null);
public record CaptureManifest(string Schema, int SchemaVersion, string Host, string CaptureRoot, IReadOnlyList<Capture> Captures);
public record Capture(string ScenarioId, string Host, string RouteId, string State, string Status, string FullPngPath, int LogicalWidth, int LogicalHeight, int ActualWidth, int ActualHeight, double DpiX, double DpiY, Rect TargetCrop, Semantics Semantics, string? Limitation, string? Note, string? TargetPngPath = null, PixelContent? FullPixelContent = null, PixelContent? TargetPixelContent = null)
{
    public string Key => ScenarioId;
}
public record Rect(int X, int Y, int Width, int Height);
public record PixelContent(bool PassesContentGate, string? Failure, int Width, int Height, long PixelCount, double OpaqueRatio, double NearTransparentRatio, double NearBlackRatio, double DominantColorRatio, int DistinctColorCount, double LuminanceRange, double ContentPixelRatio, Rect ContentBounds);
public record Semantics(string? FocusedAutomationId, string? DefaultButton, string? CancelButton, IReadOnlyList<string> ActionButtonOrder, IReadOnlyList<ControlSemantic> Controls);
public record ControlSemantic(string? AutomationId, string Type, string? Name, bool Enabled, bool? Checked, int? SelectedIndex);
public record Metrics(long ComparedPixels, long ChangedPixels, double ChangedRatio, double MeanAbsoluteChannelDelta, double P95AbsoluteChannelDelta, double LuminanceSimilarity, int PerceptualHashDistance);
public record ComparisonRow(string ScenarioId, string CaptureStatus, string Classification, Metrics? Metrics, string? SemanticDifference, string? HeatmapPath, string? Note, PixelContent? WpfContent = null, PixelContent? AvaloniaContent = null);
public record ComparisonReport(string Schema, string GeneratedFromSha256, int TargetDpi, int InventoryScenarioCount, int WpfCaptureCount, int AvaloniaCaptureCount, IReadOnlyList<ComparisonRow> Rows, IReadOnlyDictionary<string, int> Counts, ComparisonScope Scope);
public record ComparisonScope(string Kind, string Description, string RefreshInstruction);
public record Freshness(string ComparisonInputSha256, string InventorySha256, string WpfSha256, string AvaloniaSha256);

public static class PixelContentMetrics
{
    public static PixelContent Compute(SKBitmap bitmap)
    {
        var pixels = new byte[checked(bitmap.Width * bitmap.Height * 4)];
        var offset = 0;
        for (var y = 0; y < bitmap.Height; y++)
        for (var x = 0; x < bitmap.Width; x++)
        {
            var pixel = bitmap.GetPixel(x, y);
            pixels[offset++] = pixel.Blue;
            pixels[offset++] = pixel.Green;
            pixels[offset++] = pixel.Red;
            pixels[offset++] = pixel.Alpha;
        }
        return Compute(pixels, bitmap.Width, bitmap.Height);
    }

    public static PixelContent Compute(byte[] bgraPixels, int width, int height)
    {
        var pixelCount = checked(width * height);
        if (width <= 0 || height <= 0 || bgraPixels.Length < pixelCount * 4)
            return new PixelContent(false, "pixel buffer is empty or truncated", width, height, Math.Max(0, pixelCount), 0, 1, 1, 1, 0, 0, 0, new Rect(0, 0, 0, 0));

        long opaque = 0;
        long transparent = 0;
        long visible = 0;
        long nearBlack = 0;
        var colors = new Dictionary<uint, int>();
        var minLuminance = double.MaxValue;
        var maxLuminance = double.MinValue;
        for (var i = 0; i < pixelCount; i++)
        {
            var offset = i * 4;
            var b = bgraPixels[offset];
            var g = bgraPixels[offset + 1];
            var r = bgraPixels[offset + 2];
            var a = bgraPixels[offset + 3];
            if (a >= 240) opaque++;
            if (a < 16) transparent++;
            if (a < 16) continue;
            visible++;
            if (r <= 8 && g <= 8 && b <= 8) nearBlack++;
            var key = ((uint)a << 24) | ((uint)r << 16) | ((uint)g << 8) | b;
            colors[key] = colors.GetValueOrDefault(key) + 1;
            var luminance = r * .2126 + g * .7152 + b * .0722;
            minLuminance = Math.Min(minLuminance, luminance);
            maxLuminance = Math.Max(maxLuminance, luminance);
        }

        var dominant = colors.Count == 0 ? 0U : colors.MaxBy(pair => pair.Value).Key;
        var dominantCount = colors.GetValueOrDefault(dominant);
        var dominantB = (byte)dominant;
        var dominantG = (byte)(dominant >> 8);
        var dominantR = (byte)(dominant >> 16);
        var dominantA = (byte)(dominant >> 24);
        long contentPixels = 0;
        var minX = width;
        var minY = height;
        var maxX = -1;
        var maxY = -1;
        for (var i = 0; i < pixelCount; i++)
        {
            var offset = i * 4;
            var b = bgraPixels[offset];
            var g = bgraPixels[offset + 1];
            var r = bgraPixels[offset + 2];
            var a = bgraPixels[offset + 3];
            if (a < 16) continue;
            var delta = Math.Max(Math.Max(Math.Abs(r - dominantR), Math.Abs(g - dominantG)), Math.Max(Math.Abs(b - dominantB), Math.Abs(a - dominantA)));
            if (delta <= 8) continue;
            contentPixels++;
            var x = i % width;
            var y = i / width;
            minX = Math.Min(minX, x);
            minY = Math.Min(minY, y);
            maxX = Math.Max(maxX, x);
            maxY = Math.Max(maxY, y);
        }

        var opaqueRatio = (double)opaque / pixelCount;
        var transparentRatio = (double)transparent / pixelCount;
        var nearBlackRatio = visible == 0 ? 1 : (double)nearBlack / visible;
        var dominantRatio = visible == 0 ? 1 : (double)dominantCount / visible;
        var luminanceRange = visible == 0 ? 0 : maxLuminance - minLuminance;
        var contentRatio = (double)contentPixels / pixelCount;
        var bounds = maxX < minX || maxY < minY ? new Rect(0, 0, 0, 0) : new Rect(minX, minY, maxX - minX + 1, maxY - minY + 1);
        var failures = new List<string>();
        if (opaqueRatio < .05) failures.Add($"near-transparent output ({opaqueRatio:P2} opaque)");
        if (nearBlackRatio >= .985) failures.Add($"near-black output ({nearBlackRatio:P2} of visible pixels)");
        if (dominantRatio >= .9995) failures.Add($"near-uniform output ({dominantRatio:P2} dominant color)");
        if (colors.Count < 3) failures.Add($"insufficient color variation ({colors.Count} colors)");
        if (luminanceRange < 12) failures.Add($"insufficient luminance range ({luminanceRange:F2})");
        if (contentRatio < .0005 || bounds.Width < 8 || bounds.Height < 8) failures.Add("no meaningful painted content bounds");
        return new PixelContent(failures.Count == 0, failures.Count == 0 ? null : string.Join("; ", failures), width, height, pixelCount, opaqueRatio, transparentRatio, nearBlackRatio, dominantRatio, colors.Count, luminanceRange, contentRatio, bounds);
    }
}

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
