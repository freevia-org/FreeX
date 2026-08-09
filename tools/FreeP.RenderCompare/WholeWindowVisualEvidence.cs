using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using FreeP.App.Compositor;
using FreeP.VisualEvidence;

namespace FreeP.RenderCompare;

internal sealed record WholeWindowVisualEvidencePixelMetrics(
    int WpfPixelWidth,
    int WpfPixelHeight,
    int AvaloniaPixelWidth,
    int AvaloniaPixelHeight,
    int NormalizedWidth,
    int NormalizedHeight,
    double ChangedPixelRatio,
    double ForegroundChangedPixelRatio,
    double MeanChannelDelta,
    int MaxChannelDelta,
    int PerceptualHashDistance,
    string WpfPerceptualHash,
    string AvaloniaPerceptualHash,
    double MaximumChangedPixelRatio,
    double MaximumMeanChannelDelta,
    int MaximumPerceptualHashDistance,
    bool ThresholdPassed,
    string HeatmapPath,
    string WpfImageSha256,
    string AvaloniaImageSha256,
    string HeatmapSha256);

internal sealed record WholeWindowVisualEvidenceComparison(
    string ScenarioId,
    WholeWindowVisualEvidenceScenarioKind Kind,
    DialogPaneVisualEvidenceClassification Classification,
    string WpfFullImagePath,
    string AvaloniaFullImagePath,
    string WpfClientImagePath,
    string AvaloniaClientImagePath,
    IReadOnlyList<string> MismatchCategories,
    IReadOnlyList<string> Details,
    WholeWindowVisualEvidencePixelMetrics? PixelMetrics,
    WholeWindowVisualEvidencePixelMetrics? SelectionPixelMetrics = null,
    ImageContentValidation? WpfContentValidation = null,
    ImageContentValidation? AvaloniaContentValidation = null,
    TitleBarRasterValidation? WpfTitleBarRasterValidation = null,
    TitleBarRasterValidation? AvaloniaTitleBarRasterValidation = null);

internal sealed record WholeWindowVisualEvidenceSummary(
    int SchemaVersion,
    string GeneratedAtUtc,
    int ScenarioCount,
    int PairedCaptureCount,
    int PassCount,
    int MismatchCount,
    int LimitationCount,
    int DuplicateCaptureCount,
    int DeclaredContextualTabCount,
    IReadOnlyDictionary<string, int> ScenarioKindCounts,
    IReadOnlyDictionary<string, int> MismatchCategoryCounts,
    WholeWindowVisualEvidenceHostManifest Wpf,
    WholeWindowVisualEvidenceHostManifest Avalonia,
    IReadOnlyList<WholeWindowVisualEvidenceComparison> Comparisons,
    IReadOnlyList<string> EnvironmentNotes,
    IReadOnlyList<string> Limitations);

internal static class WholeWindowVisualEvidence
{
    private const double MaximumChangedPixelRatio = 0.20;
    private const double MaximumMeanChannelDelta = 18.0;
    private const int MaximumPerceptualHashDistance = 18;
    private const double BoundsTolerance = 3.0;

    internal static int Run(string outputDirectory, string wpfExecutable, string avaloniaExecutable, TimeSpan timeout)
    {
        outputDirectory = Path.GetFullPath(outputDirectory);
        wpfExecutable = Path.GetFullPath(wpfExecutable);
        avaloniaExecutable = Path.GetFullPath(avaloniaExecutable);
        if (!File.Exists(wpfExecutable))
            throw new FileNotFoundException("WPF capture host was not found.", wpfExecutable);
        if (!File.Exists(avaloniaExecutable))
            throw new FileNotFoundException("Avalonia capture host was not found.", avaloniaExecutable);

        Directory.CreateDirectory(outputDirectory);
        using var runDirectory = new TestTemporaryDirectory(
            FreePVisualEvidenceRoutes.WholeWindow.TemporaryDirectoryPrefix);
        var runRoot = runDirectory.Path;
        var runnerLimitations = new List<string>();
        var wpf = CaptureHost(
            FreePVisualEvidenceCaptureOrchestration.WpfHost,
            wpfExecutable,
            outputDirectory,
            runRoot,
            timeout,
            runnerLimitations);
        var avalonia = CaptureHost(
            FreePVisualEvidenceCaptureOrchestration.AvaloniaHost,
            avaloniaExecutable,
            outputDirectory,
            runRoot,
            timeout,
            runnerLimitations);
        var summary = BuildSummary(wpf, avalonia, outputDirectory, runnerLimitations);
        WriteReports(outputDirectory, summary);
        WriteArtifactManifest(outputDirectory);
        Console.WriteLine($"Whole-window paired captures: {summary.PairedCaptureCount}/{summary.ScenarioCount}");
        Console.WriteLine($"Pass: {summary.PassCount}; mismatch: {summary.MismatchCount}; limitation: {summary.LimitationCount}; duplicate captures: {summary.DuplicateCaptureCount}");
        Console.WriteLine($"Summary: {Path.Combine(outputDirectory, "summary.json")}");
        return summary.LimitationCount == 0 && summary.PairedCaptureCount == summary.ScenarioCount ? 0 : 1;
    }

    internal static int RegenerateReports(string outputDirectory)
    {
        outputDirectory = Path.GetFullPath(outputDirectory);
        var wpf = ReadHostManifest(outputDirectory, FreePVisualEvidenceCaptureOrchestration.WpfHost);
        var avalonia = ReadHostManifest(outputDirectory, FreePVisualEvidenceCaptureOrchestration.AvaloniaHost);
        var summary = BuildSummary(wpf, avalonia, outputDirectory);
        WriteReports(outputDirectory, summary);
        WriteArtifactManifest(outputDirectory);
        Console.WriteLine($"Regenerated whole-window report: {summary.PassCount} pass, {summary.MismatchCount} mismatch, {summary.LimitationCount} limitation.");
        return summary.LimitationCount == 0 && summary.PairedCaptureCount == summary.ScenarioCount ? 0 : 1;
    }

    internal static WholeWindowVisualEvidenceSummary BuildSummary(
        WholeWindowVisualEvidenceHostManifest wpf,
        WholeWindowVisualEvidenceHostManifest avalonia,
        string evidenceRoot,
        IReadOnlyList<string>? runnerLimitations = null)
    {
        var wpfByScenario = wpf.Captures.ToDictionary(capture => capture.ScenarioId, StringComparer.Ordinal);
        var avaloniaByScenario = avalonia.Captures.ToDictionary(capture => capture.ScenarioId, StringComparer.Ordinal);
        var wpfDuplicates = DuplicateScenarioMap(wpf.Captures);
        var avaloniaDuplicates = DuplicateScenarioMap(avalonia.Captures);
        var comparisons = WholeWindowVisualEvidenceCatalog.All
            .Select(scenario => Compare(
                scenario,
                wpfByScenario.GetValueOrDefault(scenario.Id),
                avaloniaByScenario.GetValueOrDefault(scenario.Id),
                evidenceRoot,
                wpfDuplicates.GetValueOrDefault(scenario.Id),
                avaloniaDuplicates.GetValueOrDefault(scenario.Id)))
            .ToArray();
        var limitations = wpf.Limitations
            .Concat(avalonia.Limitations)
            .Concat(runnerLimitations ?? [])
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var categoryCounts = comparisons
            .SelectMany(comparison => comparison.MismatchCategories)
            .GroupBy(category => category, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        var scenarioKindCounts = WholeWindowVisualEvidenceCatalog.All
            .GroupBy(scenario => scenario.Kind)
            .ToDictionary(
                group => group.Key.ToString(),
                group => group.Count(),
                StringComparer.Ordinal);
        var declaredContextualTabs = Math.Max(
            wpf.Captures.SelectMany(capture => capture.SemanticState.VisibleContextualTabIds).Distinct(StringComparer.Ordinal).Count(),
            avalonia.Captures.SelectMany(capture => capture.SemanticState.VisibleContextualTabIds).Distinct(StringComparer.Ordinal).Count());

        return new WholeWindowVisualEvidenceSummary(
            1,
            DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            WholeWindowVisualEvidenceCatalog.All.Count,
            comparisons.Count(comparison =>
                !string.IsNullOrWhiteSpace(comparison.WpfClientImagePath) &&
                !string.IsNullOrWhiteSpace(comparison.AvaloniaClientImagePath) &&
                comparison.WpfContentValidation?.IsValid == true &&
                comparison.AvaloniaContentValidation?.IsValid == true),
            comparisons.Count(comparison => comparison.Classification == DialogPaneVisualEvidenceClassification.Pass),
            comparisons.Count(comparison => comparison.Classification == DialogPaneVisualEvidenceClassification.Mismatch),
            comparisons.Count(comparison => comparison.Classification == DialogPaneVisualEvidenceClassification.Limitation),
            comparisons.Count(comparison => comparison.MismatchCategories.Contains("duplicate-capture", StringComparer.Ordinal)),
            declaredContextualTabs,
            scenarioKindCounts,
            categoryCounts,
            wpf,
            avalonia,
            comparisons,
            [
                "All pixel gates use the complete 1280x760 app-owned client at logical 96 DPI.",
                "The app-owned titlebar, QAT, ribbon, Backstage, workspace, notes, panes, status bar, and zoom/view state are included in the gate.",
                "Native OS caption buttons, window-manager shadows, and other non-client decoration are excluded on both hosts; no app-owned client region is masked.",
                $"WPF capture mode: {wpf.CaptureMode}; Avalonia capture mode: {avalonia.CaptureMode}.",
                declaredContextualTabs == 0
                    ? "FreeP currently declares zero contextual ribbon tabs, so the evidence contract contains no invented contextual-tab scenarios."
                    : $"Observed {declaredContextualTabs} contextual ribbon tab id(s).",
                "Gridline/guide scenarios use the runtime ribbon command path; both current canvas renderers apply those flags to snapping and do not paint gridline/guide canvas raster.",
                "Rich-editor overlay scenarios use each host's production editor with shared mixed runs and deterministic selection/caret offsets. They prove visible layout, focus, selection, and caret state; they do not claim physical pointer hit-testing, IME, clipboard, undo, or commit behavior.",
            ],
            limitations);
    }

    private static WholeWindowVisualEvidenceComparison Compare(
        WholeWindowVisualEvidenceScenario scenario,
        WholeWindowVisualEvidenceCapture? wpf,
        WholeWindowVisualEvidenceCapture? avalonia,
        string evidenceRoot,
        IReadOnlyList<string>? wpfDuplicatePeers,
        IReadOnlyList<string>? avaloniaDuplicatePeers)
    {
        if (wpf is null || avalonia is null)
        {
            return new(
                scenario.Id,
                scenario.Kind,
                DialogPaneVisualEvidenceClassification.Limitation,
                wpf?.FullImagePath ?? string.Empty,
                avalonia?.FullImagePath ?? string.Empty,
                wpf?.ClientImagePath ?? string.Empty,
                avalonia?.ClientImagePath ?? string.Empty,
                ["capture-missing"],
                [wpf is null ? "WPF capture is missing." : "Avalonia capture is missing."],
                null);
        }

        var categories = new HashSet<string>(StringComparer.Ordinal);
        var details = new List<string>();
        void Mismatch(string category, string detail)
        {
            categories.Add(category);
            details.Add(detail);
        }

        var wpfClientPath = EvidencePath(evidenceRoot, wpf.ClientImagePath);
        var avaloniaClientPath = EvidencePath(evidenceRoot, avalonia.ClientImagePath);
        var wpfContent = TryValidateContent(wpfClientPath);
        var avaloniaContent = TryValidateContent(avaloniaClientPath);
        var wpfTitleBarRaster = TryValidateTitleBarRaster(wpfClientPath, wpf.SemanticState.TitleBarBounds);
        var avaloniaTitleBarRaster = TryValidateTitleBarRaster(avaloniaClientPath, avalonia.SemanticState.TitleBarBounds);

        if (!StringComparer.Ordinal.Equals(wpf.CaptureStatus, "complete") ||
            !StringComparer.Ordinal.Equals(avalonia.CaptureStatus, "complete") ||
            wpf.NonBackgroundPixelCount <= 0 || avalonia.NonBackgroundPixelCount <= 0)
            Mismatch("capture-invalid", "One or both host captures are incomplete or blank.");
        if (wpfContent?.IsValid != true || avaloniaContent?.IsValid != true)
        {
            var reasons = new List<string>();
            if (wpfContent is null)
                reasons.Add("WPF PNG could not be decoded");
            else if (!wpfContent.IsValid)
                reasons.Add("WPF: " + string.Join(", ", wpfContent.Failures));
            if (avaloniaContent is null)
                reasons.Add("Avalonia PNG could not be decoded");
            else if (!avaloniaContent.IsValid)
                reasons.Add("Avalonia: " + string.Join(", ", avaloniaContent.Failures));
            Mismatch("capture-pixel-content-invalid", string.Join("; ", reasons) + ".");
        }
        if (wpf.SemanticState.AppOwnedTitleBarVisible && wpfTitleBarRaster?.IsValid != true)
            Mismatch("app-owned-titlebar-raster", $"WPF titlebar accent is not visibly present in its declared raster bounds (accent ratio {wpfTitleBarRaster?.AccentPixelRatio ?? 0:P1}).");
        if (avalonia.SemanticState.AppOwnedTitleBarVisible && avaloniaTitleBarRaster?.IsValid != true)
            Mismatch("app-owned-titlebar-raster", $"Avalonia titlebar accent is not visibly present in its declared raster bounds (accent ratio {avaloniaTitleBarRaster?.AccentPixelRatio ?? 0:P1}).");

        if (wpf.PixelWidth != WholeWindowVisualEvidenceCatalog.LogicalClientWidth ||
            wpf.PixelHeight != WholeWindowVisualEvidenceCatalog.LogicalClientHeight ||
            avalonia.PixelWidth != WholeWindowVisualEvidenceCatalog.LogicalClientWidth ||
            avalonia.PixelHeight != WholeWindowVisualEvidenceCatalog.LogicalClientHeight ||
            Math.Abs(wpf.DpiX - 96) > 0.1 || Math.Abs(avalonia.DpiX - 96) > 0.1)
            Mismatch("capture-normalization", "One or both client crops are not 1280x760 at logical 96 DPI.");

        if (wpf.SemanticState.Assertions.Any(assertion => !assertion.Passed) ||
            avalonia.SemanticState.Assertions.Any(assertion => !assertion.Passed))
        {
            var failedAssertions = wpf.SemanticState.Assertions.Concat(avalonia.SemanticState.Assertions)
                .Where(assertion => !assertion.Passed)
                .Select(assertion => $"{assertion.Id}: {assertion.Detail}")
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            var category = failedAssertions.Any(detail => detail.StartsWith("contextual-tab-visible:", StringComparison.Ordinal))
                ? "contextual-tab-unavailable"
                : "scenario-activation";
            Mismatch(category, string.Join(" ", failedAssertions));
        }

        CompareSemanticStates(wpf.SemanticState, avalonia.SemanticState, Mismatch);

        ClassifyDuplicateCapture("WPF", scenario, wpfDuplicatePeers, Mismatch);
        ClassifyDuplicateCapture("Avalonia", scenario, avaloniaDuplicatePeers, Mismatch);

        var pixelMetrics = ComputePixelMetrics(evidenceRoot, scenario.Id, wpf, avalonia);
        if (pixelMetrics is null)
        {
            Mismatch("pixel-evidence-missing", "The normalized whole-client pixel comparison could not be computed.");
        }
        else if (!pixelMetrics.ThresholdPassed)
        {
            Mismatch(
                "full-client-pixel-threshold",
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Full-client threshold failed: changed {pixelMetrics.ChangedPixelRatio:P2} (max {MaximumChangedPixelRatio:P0}), mean channel delta {pixelMetrics.MeanChannelDelta:F2} (max {MaximumMeanChannelDelta:F2}), perceptual hash distance {pixelMetrics.PerceptualHashDistance} (max {MaximumPerceptualHashDistance})."));
        }

        WholeWindowVisualEvidencePixelMetrics? selectionPixelMetrics = null;
        if (scenario.Kind == WholeWindowVisualEvidenceScenarioKind.RichEditorOverlay &&
            StringComparer.Ordinal.Equals(scenario.ActivationId, "selection"))
        {
            CompareRichEditorSelectionState(wpf.SemanticState, avalonia.SemanticState, Mismatch);
            selectionPixelMetrics = ComputeSelectionPixelMetrics(evidenceRoot, scenario.Id, wpf, avalonia, out var selectionEvidenceFailure);
            if (selectionPixelMetrics is null)
                Mismatch(
                    selectionEvidenceFailure ?? "rich-editor-evidence-missing",
                    selectionEvidenceFailure == "rich-editor-evidence-blank"
                        ? "The paired rich-editor selection crop decoded but failed the pixel-content gate."
                        : "The paired rich-editor selection crop is missing or undecodable.");
            else if (!selectionPixelMetrics.ThresholdPassed)
                Mismatch(
                    "rich-editor-selection-pixel-threshold",
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"Rich-editor selection threshold failed: changed {selectionPixelMetrics.ChangedPixelRatio:P2} (max {MaximumChangedPixelRatio:P0}), mean channel delta {selectionPixelMetrics.MeanChannelDelta:F2} (max {MaximumMeanChannelDelta:F2}), perceptual hash distance {selectionPixelMetrics.PerceptualHashDistance} (max {MaximumPerceptualHashDistance})."));
        }

        var limitations = wpf.Limitations.Concat(avalonia.Limitations).Distinct(StringComparer.Ordinal).ToArray();
        details.AddRange(limitations);
        var classification = categories.Count > 0
            ? DialogPaneVisualEvidenceClassification.Mismatch
            : limitations.Length > 0
                ? DialogPaneVisualEvidenceClassification.Limitation
                : DialogPaneVisualEvidenceClassification.Pass;
        return new(
            scenario.Id,
            scenario.Kind,
            classification,
            wpf.FullImagePath,
            avalonia.FullImagePath,
            wpf.ClientImagePath,
            avalonia.ClientImagePath,
            categories.OrderBy(category => category, StringComparer.Ordinal).ToArray(),
            details,
            pixelMetrics,
            selectionPixelMetrics,
            wpfContent,
            avaloniaContent,
            wpfTitleBarRaster,
            avaloniaTitleBarRaster);
    }

    private static void ClassifyDuplicateCapture(
        string host,
        WholeWindowVisualEvidenceScenario scenario,
        IReadOnlyList<string>? duplicatePeers,
        Action<string, string> mismatch)
    {
        if (duplicatePeers is not { Count: > 0 })
            return;

        var viewStatePeers = duplicatePeers
            .Where(peer => IsViewShowStatePair(scenario.Id, peer))
            .ToArray();
        if (viewStatePeers.Length > 0)
        {
            mismatch(
                "view-state-raster-no-effect",
                $"{host} applies different gridline/guide semantic state, but its client PNG is byte-identical to: {string.Join(", ", viewStatePeers)}. The current canvas uses these flags for snapping and renders no gridline/guide raster.");
        }

        var unexpectedPeers = duplicatePeers.Except(viewStatePeers, StringComparer.Ordinal).ToArray();
        if (unexpectedPeers.Length > 0)
            mismatch("duplicate-capture", $"{host} client PNG is byte-identical to: {string.Join(", ", unexpectedPeers)}.");
    }

    internal static bool IsViewShowStatePair(string scenarioId, string peerId) =>
        (StringComparer.Ordinal.Equals(scenarioId, "view.gridlines-guides") &&
         StringComparer.Ordinal.Equals(peerId, "view.clean-canvas")) ||
        (StringComparer.Ordinal.Equals(scenarioId, "view.clean-canvas") &&
         StringComparer.Ordinal.Equals(peerId, "view.gridlines-guides"));

    private static void CompareSemanticStates(
        WholeWindowVisualEvidenceSemanticState wpf,
        WholeWindowVisualEvidenceSemanticState avalonia,
        Action<string, string> mismatch)
    {
        if (wpf.CurrentSlideIndex != avalonia.CurrentSlideIndex ||
            !StringComparer.Ordinal.Equals(wpf.CurrentSlideTitle, avalonia.CurrentSlideTitle) ||
            !wpf.SelectedShapeIds.SequenceEqual(avalonia.SelectedShapeIds) ||
            !StringComparer.Ordinal.Equals(wpf.SelectedShapeKind, avalonia.SelectedShapeKind))
            mismatch("workspace-state", "Slide or selected-shape semantic state differs between hosts.");

        if (!StringComparer.Ordinal.Equals(NormalizeTabId(wpf.ActiveRibbonTabId), NormalizeTabId(avalonia.ActiveRibbonTabId)))
            mismatch("ribbon-active-tab", $"Active tab differs: WPF '{wpf.ActiveRibbonTabId}', Avalonia '{avalonia.ActiveRibbonTabId}'.");
        if (!NormalizeTabIds(wpf.VisibleRibbonTabIds).SequenceEqual(NormalizeTabIds(avalonia.VisibleRibbonTabIds), StringComparer.Ordinal))
            mismatch("ribbon-tab-strip", "Visible ribbon tab order differs between hosts.");
        if (!wpf.VisibleContextualTabIds.SequenceEqual(avalonia.VisibleContextualTabIds, StringComparer.Ordinal))
            mismatch("contextual-tab-strip", "Visible contextual ribbon tabs differ between hosts.");

        if (wpf.BackstageOpen != avalonia.BackstageOpen ||
            !StringComparer.OrdinalIgnoreCase.Equals(wpf.BackstagePane, avalonia.BackstagePane))
            mismatch("backstage-state", $"Backstage state differs: WPF {wpf.BackstageOpen}/{wpf.BackstagePane}, Avalonia {avalonia.BackstageOpen}/{avalonia.BackstagePane}.");

        if (!StringComparer.Ordinal.Equals(wpf.StatusText, avalonia.StatusText))
            mismatch("status-content", $"Status text differs: WPF '{wpf.StatusText}', Avalonia '{avalonia.StatusText}'.");
        if (wpf.StatusViewModeControlCount != avalonia.StatusViewModeControlCount ||
            wpf.StatusZoomControlVisible != avalonia.StatusZoomControlVisible)
            mismatch("status-controls", "Status view-mode/zoom control availability differs.");
        if (wpf.ShowGridlines != avalonia.ShowGridlines || wpf.ShowGuides != avalonia.ShowGuides ||
            !StringComparer.Ordinal.Equals(wpf.ZoomMode, avalonia.ZoomMode) || wpf.ZoomPercent != avalonia.ZoomPercent)
            mismatch("view-state", "Gridline, guide, or zoom semantic state differs.");

        if (wpf.AppOwnedTitleBarVisible != avalonia.AppOwnedTitleBarVisible ||
            !BoundsMatch(wpf.TitleBarBounds, avalonia.TitleBarBounds))
            mismatch("app-owned-titlebar", "App-owned titlebar visibility or geometry differs.");
        if (wpf.QuickAccessButtonCount != avalonia.QuickAccessButtonCount)
            mismatch("quick-access-toolbar", $"QAT button count differs: WPF {wpf.QuickAccessButtonCount}, Avalonia {avalonia.QuickAccessButtonCount}.");
        if (!StringComparer.Ordinal.Equals(wpf.AppIconIdentity, avalonia.AppIconIdentity))
            mismatch("app-icon", $"App icon identity differs: WPF '{wpf.AppIconIdentity}', Avalonia '{avalonia.AppIconIdentity}'.");
        if (!StringComparer.Ordinal.Equals(wpf.WindowTitle, avalonia.WindowTitle))
            mismatch("window-title", $"Window title differs: WPF '{wpf.WindowTitle}', Avalonia '{avalonia.WindowTitle}'.");

        if (!BoundsMatch(wpf.RibbonBounds, avalonia.RibbonBounds))
            mismatch("ribbon-geometry", BoundsDetail("Ribbon", wpf.RibbonBounds, avalonia.RibbonBounds));
        if (!BoundsMatch(wpf.SlidePaneBounds, avalonia.SlidePaneBounds) ||
            !BoundsMatch(wpf.CanvasBounds, avalonia.CanvasBounds) ||
            !BoundsMatch(wpf.NotesPaneBounds, avalonia.NotesPaneBounds))
            mismatch("workspace-geometry", "Slide pane, canvas, or notes-pane bounds differ.");
        if (!BoundsMatch(wpf.StatusBarBounds, avalonia.StatusBarBounds))
            mismatch("status-geometry", BoundsDetail("Status bar", wpf.StatusBarBounds, avalonia.StatusBarBounds));
        if (!wpf.VisibleAuxiliaryPanes.SequenceEqual(avalonia.VisibleAuxiliaryPanes, StringComparer.Ordinal))
            mismatch("auxiliary-pane-state", $"Visible auxiliary panes differ: WPF [{string.Join(", ", wpf.VisibleAuxiliaryPanes)}], Avalonia [{string.Join(", ", avalonia.VisibleAuxiliaryPanes)}].");
    }

    private static void CompareRichEditorSelectionState(
        WholeWindowVisualEvidenceSemanticState wpf,
        WholeWindowVisualEvidenceSemanticState avalonia,
        Action<string, string> mismatch)
    {
        var expectedStart = DialogPaneVisualEvidenceFixtureFactory.RichEditorSelectionStart;
        var expectedEnd = DialogPaneVisualEvidenceFixtureFactory.RichEditorSelectionEnd;
        var expectedText = DialogPaneVisualEvidenceFixtureFactory.RichEditorSelectedText;
        var left = wpf.RichEditor;
        var right = avalonia.RichEditor;
        if (!left.Active || !right.Active || !left.Bounds.IsVisible || !right.Bounds.IsVisible)
        {
            mismatch("rich-editor-evidence-missing", "Both hosts must expose an active, visible production rich editor for the selection pair.");
            return;
        }
        if (left.SelectionStart != expectedStart || left.SelectionEnd != expectedEnd ||
            right.SelectionStart != expectedStart || right.SelectionEnd != expectedEnd ||
            !StringComparer.Ordinal.Equals(left.SelectedText, expectedText) ||
            !StringComparer.Ordinal.Equals(right.SelectedText, expectedText))
            mismatch("rich-editor-evidence-stale", "The paired captures do not contain the deterministic selected range and selected text from the rich-editor fixture.");
        if (!BoundsMatch(left.Bounds, right.Bounds))
            mismatch("rich-editor-selection-geometry", BoundsDetail("Rich-editor selection", left.Bounds, right.Bounds));
    }

    private static WholeWindowVisualEvidencePixelMetrics? ComputePixelMetrics(
        string evidenceRoot,
        string scenarioId,
        WholeWindowVisualEvidenceCapture wpf,
        WholeWindowVisualEvidenceCapture avalonia)
    {
        var wpfPath = Path.Combine(evidenceRoot, wpf.ClientImagePath.Replace('/', Path.DirectorySeparatorChar));
        var avaloniaPath = Path.Combine(evidenceRoot, avalonia.ClientImagePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(wpfPath) || !File.Exists(avaloniaPath))
            return null;
        return ComputePixelMetricsFromPaths(
            evidenceRoot,
            scenarioId,
            wpfPath,
            avaloniaPath,
            WholeWindowVisualEvidenceCatalog.LogicalClientWidth,
            WholeWindowVisualEvidenceCatalog.LogicalClientHeight);
    }

    private static WholeWindowVisualEvidencePixelMetrics? ComputeSelectionPixelMetrics(
        string evidenceRoot,
        string scenarioId,
        WholeWindowVisualEvidenceCapture wpf,
        WholeWindowVisualEvidenceCapture avalonia,
        out string? failureCategory)
    {
        failureCategory = null;
        if (!wpf.SemanticState.RichEditor.Active || !avalonia.SemanticState.RichEditor.Active ||
            !wpf.SemanticState.RichEditor.Bounds.IsVisible || !avalonia.SemanticState.RichEditor.Bounds.IsVisible)
            return null;

        var wpfPath = EvidencePath(evidenceRoot, wpf.ClientImagePath);
        var avaloniaPath = EvidencePath(evidenceRoot, avalonia.ClientImagePath);
        var wpfCrop = EvidencePath(evidenceRoot, $"diff/{scenarioId}.wpf-selection.png");
        var avaloniaCrop = EvidencePath(evidenceRoot, $"diff/{scenarioId}.avalonia-selection.png");
        if (!File.Exists(wpfPath) || !File.Exists(avaloniaPath))
        {
            failureCategory = "rich-editor-evidence-missing";
            return null;
        }
        if (!ImageDiff.TryWriteCrop(wpfPath, wpf.SemanticState.RichEditor.Bounds, wpfCrop) ||
            !ImageDiff.TryWriteCrop(avaloniaPath, avalonia.SemanticState.RichEditor.Bounds, avaloniaCrop))
        {
            failureCategory = "rich-editor-evidence-missing";
            return null;
        }

        var wpfContent = TryValidateContent(wpfCrop);
        var avaloniaContent = TryValidateContent(avaloniaCrop);
        if (wpfContent?.IsValid != true || avaloniaContent?.IsValid != true)
        {
            failureCategory = "rich-editor-evidence-blank";
            return null;
        }

        int normalizedWidth = Math.Max(wpfContent.Width, avaloniaContent.Width);
        int normalizedHeight = Math.Max(wpfContent.Height, avaloniaContent.Height);
        return ComputePixelMetricsFromPaths(
            evidenceRoot,
            $"{scenarioId}.selection",
            wpfCrop,
            avaloniaCrop,
            normalizedWidth,
            normalizedHeight);
    }

    private static WholeWindowVisualEvidencePixelMetrics? ComputePixelMetricsFromPaths(
        string evidenceRoot,
        string scenarioId,
        string wpfPath,
        string avaloniaPath,
        int normalizedWidth,
        int normalizedHeight)
    {
        if (!File.Exists(wpfPath) || !File.Exists(avaloniaPath) || normalizedWidth <= 0 || normalizedHeight <= 0)
            return null;

        var heatmapRelativePath = $"diff/{scenarioId}.png";
        var heatmapPath = Path.Combine(evidenceRoot, heatmapRelativePath.Replace('/', Path.DirectorySeparatorChar));
        var diff = ImageDiff.CompareNormalized(
            wpfPath,
            avaloniaPath,
            normalizedWidth,
            normalizedHeight,
            heatmapPath);
        var perceptual = ImageDiff.CompareDifferenceHash(wpfPath, avaloniaPath);
        var dimensionsMatch = diff.WidthA == diff.WidthB && diff.HeightA == diff.HeightB &&
            diff.WidthA == normalizedWidth &&
            diff.HeightA == normalizedHeight;
        var thresholdPassed = dimensionsMatch &&
            diff.ChangedPixelRatio <= MaximumChangedPixelRatio &&
            diff.MeanChannelDelta <= MaximumMeanChannelDelta &&
            perceptual.HammingDistance <= MaximumPerceptualHashDistance;
        return new(
            diff.WidthA,
            diff.HeightA,
            diff.WidthB,
            diff.HeightB,
            diff.NormalizedWidth,
            diff.NormalizedHeight,
            diff.ChangedPixelRatio,
            diff.ForegroundChangedPixelRatio,
            diff.MeanChannelDelta,
            diff.MaxChannelDelta,
            perceptual.HammingDistance,
            perceptual.HashA,
            perceptual.HashB,
            MaximumChangedPixelRatio,
            MaximumMeanChannelDelta,
            MaximumPerceptualHashDistance,
            thresholdPassed,
            heatmapRelativePath,
            VisualEvidenceToolSupport.Sha256(wpfPath),
            VisualEvidenceToolSupport.Sha256(avaloniaPath),
            VisualEvidenceToolSupport.Sha256(heatmapPath));
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> DuplicateScenarioMap(
        IReadOnlyList<WholeWindowVisualEvidenceCapture> captures)
    {
        var result = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (var group in captures
                     .Where(capture => !string.IsNullOrWhiteSpace(capture.ClientImageSha256))
                     .GroupBy(capture => capture.ClientImageSha256, StringComparer.Ordinal)
                     .Where(group => group.Count() > 1))
        {
            var ids = group.Select(capture => capture.ScenarioId).OrderBy(id => id, StringComparer.Ordinal).ToArray();
            foreach (var id in ids)
                result[id] = ids.Where(peer => !StringComparer.Ordinal.Equals(peer, id)).ToArray();
        }
        return result;
    }

    private static WholeWindowVisualEvidenceHostManifest CaptureHost(
        string host,
        string executable,
        string outputDirectory,
        string runRoot,
        TimeSpan timeout,
        List<string> runnerLimitations)
    {
        var outputPlan = FreePVisualEvidenceCaptureOrchestration.CreateHostOutputPlan(
            outputDirectory,
            host,
            FreePVisualEvidenceRoutes.WholeWindow);
        outputPlan.EnsureDirectories();
        var captures = new List<WholeWindowVisualEvidenceCapture>();
        var hostLimitations = new List<string>();

        foreach (var scenario in WholeWindowVisualEvidenceCatalog.All)
        {
            Console.WriteLine($"[{host}] {scenario.Id}");
            var scenarioRoot = FreePVisualEvidenceCaptureOrchestration.CreateScenarioRunRoot(
                runRoot,
                host,
                scenario.Id);
            Directory.CreateDirectory(scenarioRoot);
            var result = RunScenario(executable, scenarioRoot, scenario.Id, timeout);
            var scenarioOutput = FreePVisualEvidenceCaptureOrchestration.CreateScenarioOutputPlan(
                scenarioRoot,
                host,
                scenario.Id,
                FreePVisualEvidenceRoutes.WholeWindow);
            var scenarioManifest = FreePVisualEvidenceCaptureOrchestration.ReadScenarioManifest<
                WholeWindowVisualEvidenceHostManifest,
                WholeWindowVisualEvidenceCapture>(
                    scenarioOutput.HostManifestPath,
                    FreePVisualEvidenceCaptureOrchestration.ToolManifestJsonOptions,
                    scenario.Id,
                    manifest => manifest.Captures,
                    capture => capture.ScenarioId);
            if (scenarioManifest.Status == VisualEvidenceScenarioManifestStatus.MissingManifest)
            {
                runnerLimitations.Add($"{host} {scenario.Id}: {result} No host manifest was produced.");
                continue;
            }

            var manifest = scenarioManifest.Manifest;
            var capture = scenarioManifest.Capture;
            if (capture is null)
            {
                runnerLimitations.Add($"{host} {scenario.Id}: {result} The host manifest contained no matching capture.");
                continue;
            }

            var sourceFull = FreePVisualEvidenceCaptureOrchestration.ResolveDeclaredPath(
                scenarioRoot,
                capture.FullImagePath);
            var sourceClient = FreePVisualEvidenceCaptureOrchestration.ResolveDeclaredPath(
                scenarioRoot,
                capture.ClientImagePath);
            if (!FreePVisualEvidenceCaptureOrchestration.IsNonzeroFile(sourceFull) ||
                !FreePVisualEvidenceCaptureOrchestration.IsNonzeroFile(sourceClient))
            {
                runnerLimitations.Add($"{host} {scenario.Id}: {result} One or both declared PNGs were missing or empty.");
                continue;
            }

            var finalScenarioOutput = FreePVisualEvidenceCaptureOrchestration.CreateScenarioOutputPlan(
                outputDirectory,
                host,
                scenario.Id,
                FreePVisualEvidenceRoutes.WholeWindow);
            var finalFull = finalScenarioOutput.FullImagePath!;
            var finalClient = finalScenarioOutput.ClientImagePath!;
            File.Copy(sourceFull, finalFull, overwrite: true);
            File.Copy(sourceClient, finalClient, overwrite: true);
            var fullValidation = TryValidateContent(finalFull);
            var clientValidation = TryValidateContent(finalClient);
            var contentValid = fullValidation?.IsValid == true && clientValidation?.IsValid == true;
            var contentLimitations = contentValid
                ? Array.Empty<string>()
                : new[]
                {
                    $"{host} {scenario.Id}: decoded pixel-content gate rejected the capture. " +
                    $"Full: {ValidationDetail(fullValidation)} Client: {ValidationDetail(clientValidation)}",
                };
            runnerLimitations.AddRange(contentLimitations);
            captures.Add(capture with
            {
                CaptureStatus = contentValid ? capture.CaptureStatus : "invalid-pixel-content",
                FullImagePath = finalScenarioOutput.FullImageRelativePath!,
                ClientImagePath = finalScenarioOutput.ClientImageRelativePath!,
                FullImageSha256 = VisualEvidenceToolSupport.Sha256(finalFull),
                ClientImageSha256 = VisualEvidenceToolSupport.Sha256(finalClient),
                Limitations = capture.Limitations.Concat(contentLimitations).ToArray(),
            });
            if (manifest is not null)
                hostLimitations.AddRange(manifest.Limitations);
        }

        var hostManifest = new WholeWindowVisualEvidenceHostManifest(
            1,
            host,
            "visible-app-owned-full-client-render-target; native-non-client-excluded; scenario-isolated-processes",
            WholeWindowVisualEvidenceCatalog.TargetDpi,
            WholeWindowVisualEvidenceCatalog.LogicalClientWidth,
            WholeWindowVisualEvidenceCatalog.LogicalClientHeight,
            FreePVisualEvidenceCaptureOrchestration.UtcTimestamp(),
            captures,
            hostLimitations.Distinct(StringComparer.Ordinal).ToArray());
        FreePVisualEvidenceCaptureOrchestration.WriteManifest(
            outputPlan.ManifestPath,
            hostManifest,
            FreePVisualEvidenceCaptureOrchestration.ToolManifestJsonOptions);
        return hostManifest;
    }

    private static string RunScenario(string executable, string outputRoot, string scenarioId, TimeSpan timeout)
        => VisualEvidenceToolSupport.RunScenario(
            FreePVisualEvidenceCaptureOrchestration.CreateScenarioProcessPlan(
                executable,
                outputRoot,
                FreePVisualEvidenceRoutes.WholeWindow,
                scenarioId,
                timeout,
                "exact owned process tree"));

    private static WholeWindowVisualEvidenceHostManifest ReadHostManifest(string outputDirectory, string host)
    {
        var path = FreePVisualEvidenceCaptureOrchestration.CreateHostOutputPlan(
            outputDirectory,
            host,
            FreePVisualEvidenceRoutes.WholeWindow).ManifestPath;
        return VisualEvidenceToolSupport.ReadManifest<WholeWindowVisualEvidenceHostManifest>(
            path,
            FreePVisualEvidenceCaptureOrchestration.ToolManifestJsonOptions,
            $"{host} whole-window evidence manifest was not found.",
            $"{host} whole-window evidence manifest could not be read.");
    }

    private static void WriteReports(string outputDirectory, WholeWindowVisualEvidenceSummary summary)
    {
        FreePVisualEvidenceCaptureOrchestration.WriteManifest(
            Path.Combine(outputDirectory, "summary.json"),
            summary,
            FreePVisualEvidenceCaptureOrchestration.ToolManifestJsonOptions);
        File.WriteAllText(Path.Combine(outputDirectory, "report.md"), BuildMarkdown(summary));
        File.WriteAllText(Path.Combine(outputDirectory, "report.html"), BuildHtml(summary));
    }

    internal static string BuildMarkdown(WholeWindowVisualEvidenceSummary summary)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# FreeP Paired Whole-Window Visual Evidence");
        builder.AppendLine();
        builder.AppendLine($"Generated `{summary.GeneratedAtUtc}` from independently activated WPF and Avalonia app processes.");
        builder.AppendLine();
        builder.AppendLine($"- Scenarios: {summary.ScenarioCount}");
        builder.AppendLine($"- Paired captures: {summary.PairedCaptureCount}");
        builder.AppendLine($"- Pass: {summary.PassCount}");
        builder.AppendLine($"- Mismatch: {summary.MismatchCount}");
        builder.AppendLine($"- Limitation: {summary.LimitationCount}");
        builder.AppendLine($"- Duplicate-image scenarios: {summary.DuplicateCaptureCount}");
        builder.AppendLine($"- Declared contextual tabs observed: {summary.DeclaredContextualTabCount}");
        foreach (var note in summary.EnvironmentNotes)
            builder.AppendLine($"- Environment: {note}");
        builder.AppendLine();
        builder.AppendLine("## Mismatch Categories");
        builder.AppendLine();
        builder.AppendLine("| Category | Scenarios |");
        builder.AppendLine("|---|---:|");
        foreach (var category in summary.MismatchCategoryCounts)
            builder.AppendLine($"| {category.Key} | {category.Value} |");
        builder.AppendLine();
        builder.AppendLine("## Scenarios");
        builder.AppendLine();
        builder.AppendLine("| Scenario | Kind | Result | Categories | Changed pixels | Mean delta | Perceptual distance | Selection changed pixels | Evidence |");
        builder.AppendLine("|---|---|---|---|---:|---:|---:|---:|---|");
        foreach (var comparison in summary.Comparisons)
        {
            var metrics = comparison.PixelMetrics;
            builder.Append("| ").Append(comparison.ScenarioId)
                .Append(" | ").Append(comparison.Kind)
                .Append(" | ").Append(comparison.Classification.ToString().ToLowerInvariant())
                .Append(" | ").Append(comparison.MismatchCategories.Count == 0 ? "none" : string.Join(", ", comparison.MismatchCategories))
                .Append(" | ").Append(metrics is null ? "n/a" : metrics.ChangedPixelRatio.ToString("P2", CultureInfo.InvariantCulture))
                .Append(" | ").Append(metrics is null ? "n/a" : metrics.MeanChannelDelta.ToString("F2", CultureInfo.InvariantCulture))
                .Append(" | ").Append(metrics is null ? "n/a" : metrics.PerceptualHashDistance.ToString(CultureInfo.InvariantCulture))
                .Append(" | ").Append(comparison.SelectionPixelMetrics is null ? "n/a" : comparison.SelectionPixelMetrics.ChangedPixelRatio.ToString("P2", CultureInfo.InvariantCulture))
                .Append(" | ").Append(EvidenceLinks(comparison))
                .AppendLine(" |");
            foreach (var detail in comparison.Details)
                builder.AppendLine($"|  | Detail |  |  |  |  |  |  | {detail.Replace("|", "\\|")} |");
        }
        if (summary.Limitations.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("## Limitations");
            builder.AppendLine();
            foreach (var limitation in summary.Limitations)
                builder.AppendLine($"- {limitation}");
        }
        return builder.ToString();
    }

    internal static string BuildHtml(WholeWindowVisualEvidenceSummary summary)
    {
        var rows = new StringBuilder();
        foreach (var comparison in summary.Comparisons)
        {
            var metrics = comparison.PixelMetrics;
            var selectionMetrics = comparison.SelectionPixelMetrics;
            var details = comparison.Details.Count == 0
                ? string.Empty
                : $"<ul>{string.Concat(comparison.Details.Select(detail => $"<li>{WebUtility.HtmlEncode(detail)}</li>"))}</ul>";
            rows.Append($"""
                <section class="pair {comparison.Classification.ToString().ToLowerInvariant()}">
                  <h2>{WebUtility.HtmlEncode(comparison.ScenarioId)} <span>{comparison.Classification}</span></h2>
                  <p><b>{comparison.Kind}</b> | categories: {WebUtility.HtmlEncode(comparison.MismatchCategories.Count == 0 ? "none" : string.Join(", ", comparison.MismatchCategories))}</p>
                  <p>Changed pixels {metrics?.ChangedPixelRatio.ToString("P2", CultureInfo.InvariantCulture) ?? "n/a"}; mean delta {metrics?.MeanChannelDelta.ToString("F2", CultureInfo.InvariantCulture) ?? "n/a"}; perceptual distance {metrics?.PerceptualHashDistance.ToString(CultureInfo.InvariantCulture) ?? "n/a"}. Selection pair changed pixels: {selectionMetrics?.ChangedPixelRatio.ToString("P2", CultureInfo.InvariantCulture) ?? "n/a"}; mean delta: {selectionMetrics?.MeanChannelDelta.ToString("F2", CultureInfo.InvariantCulture) ?? "n/a"}; perceptual distance: {selectionMetrics?.PerceptualHashDistance.ToString(CultureInfo.InvariantCulture) ?? "n/a"}.</p>
                  {details}
                  <div class="images">{HtmlImage("WPF full client", comparison.WpfFullImagePath)}{HtmlImage("Avalonia full client", comparison.AvaloniaFullImagePath)}{HtmlImage("WPF normalized client crop", comparison.WpfClientImagePath)}{HtmlImage("Avalonia normalized client crop", comparison.AvaloniaClientImagePath)}{HtmlImage("Whole-client diff", metrics?.HeatmapPath ?? string.Empty)}{HtmlImage("WPF selection crop", selectionMetrics is null ? string.Empty : $"diff/{comparison.ScenarioId}.wpf-selection.png")}{HtmlImage("Avalonia selection crop", selectionMetrics is null ? string.Empty : $"diff/{comparison.ScenarioId}.avalonia-selection.png")}{HtmlImage("Selection diff", selectionMetrics?.HeatmapPath ?? string.Empty)}</div>
                </section>
                """);
        }

        var categories = string.Join(
            string.Empty,
            summary.MismatchCategoryCounts.Select(category => $"<li>{WebUtility.HtmlEncode(category.Key)}: {category.Value}</li>"));
        var html = $$$"""
            <!doctype html>
            <html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
            <title>FreeP paired whole-window visual evidence</title>
            <style>
            body{font-family:Segoe UI,Arial,sans-serif;margin:24px;color:#202124;background:#f7f8fa}main{max-width:1600px;margin:auto}.summary{display:flex;gap:18px;flex-wrap:wrap;margin:16px 0}.pair{background:white;border:1px solid #cfd4dc;border-left:5px solid #2e7d32;margin:14px 0;padding:16px}.pair.mismatch{border-left-color:#c62828}.pair.limitation{border-left-color:#8a5a00}h2{font-size:18px;margin:0 0 8px}h2 span{font-size:13px;text-transform:uppercase;margin-left:10px}.images{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:14px;margin-top:12px}figure{margin:0}img{width:100%;border:1px solid #b8bec7;background:white}figcaption{font-weight:600;margin-bottom:6px}@media(max-width:900px){.images{grid-template-columns:1fr}}
            </style></head><body><main>
            <h1>FreeP Paired Whole-Window Visual Evidence</h1>
            <p>Every row is an independently activated 1280x760 logical-96-DPI app-owned client capture. App titlebar/QAT, ribbon, Backstage, workspace, panes, notes, status, and view state are part of the decision.</p>
            <div class="summary"><b>Scenarios {{{summary.ScenarioCount}}}</b><b>Paired {{{summary.PairedCaptureCount}}}</b><b>Pass {{{summary.PassCount}}}</b><b>Mismatch {{{summary.MismatchCount}}}</b><b>Limitation {{{summary.LimitationCount}}}</b><b>Duplicate {{{summary.DuplicateCaptureCount}}}</b></div>
            <h2>Mismatch categories</h2><ul>{{{categories}}}</ul>
            {{{rows}}}
            </main></body></html>
            """;
        return string.Join(
            Environment.NewLine,
            html.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').Select(line => line.TrimEnd()));
    }

    private static void WriteArtifactManifest(string outputDirectory)
    {
        var entries = Directory.EnumerateFiles(outputDirectory, "*", SearchOption.AllDirectories)
            .Where(path => !StringComparer.OrdinalIgnoreCase.Equals(Path.GetFileName(path), "artifact-manifest.json"))
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(path => new
            {
                path = Path.GetRelativePath(outputDirectory, path).Replace('\\', '/'),
                length = new FileInfo(path).Length,
                sha256 = VisualEvidenceToolSupport.Sha256(path),
            })
            .ToArray();
        FreePVisualEvidenceCaptureOrchestration.WriteManifest(
            Path.Combine(outputDirectory, "artifact-manifest.json"),
            new { schemaVersion = 1, artifacts = entries },
            FreePVisualEvidenceCaptureOrchestration.ToolManifestJsonOptions);
    }

    private static string EvidenceLinks(WholeWindowVisualEvidenceComparison comparison)
    {
        var links = new List<string>();
        void Add(string label, string path)
        {
            if (!string.IsNullOrWhiteSpace(path))
                links.Add($"[{label}]({path})");
        }
        Add("WPF full", comparison.WpfFullImagePath);
        Add("Avalonia full", comparison.AvaloniaFullImagePath);
        Add("WPF client", comparison.WpfClientImagePath);
        Add("Avalonia client", comparison.AvaloniaClientImagePath);
        Add("diff", comparison.PixelMetrics?.HeatmapPath ?? string.Empty);
        Add("selection diff", comparison.SelectionPixelMetrics?.HeatmapPath ?? string.Empty);
        return links.Count == 0 ? "n/a" : string.Join(" / ", links);
    }

    private static string HtmlImage(string label, string path) => string.IsNullOrWhiteSpace(path)
        ? $"<figure><figcaption>{WebUtility.HtmlEncode(label)}</figcaption><p>Capture unavailable.</p></figure>"
        : $"<figure><figcaption>{WebUtility.HtmlEncode(label)}</figcaption><img loading=\"lazy\" src=\"{WebUtility.HtmlEncode(path)}\" alt=\"{WebUtility.HtmlEncode(label)}\"></figure>";

    private static bool BoundsMatch(WholeWindowVisualEvidenceBounds left, WholeWindowVisualEvidenceBounds right) =>
        Math.Abs(left.X - right.X) <= BoundsTolerance &&
        Math.Abs(left.Y - right.Y) <= BoundsTolerance &&
        Math.Abs(left.Width - right.Width) <= BoundsTolerance &&
        Math.Abs(left.Height - right.Height) <= BoundsTolerance;

    private static string BoundsDetail(
        string label,
        WholeWindowVisualEvidenceBounds wpf,
        WholeWindowVisualEvidenceBounds avalonia) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{label} bounds differ: WPF {wpf.X:F1},{wpf.Y:F1} {wpf.Width:F1}x{wpf.Height:F1}; Avalonia {avalonia.X:F1},{avalonia.Y:F1} {avalonia.Width:F1}x{avalonia.Height:F1}.");

    private static IReadOnlyList<string> NormalizeTabIds(IReadOnlyList<string> ids) =>
        ids.Select(NormalizeTabId).ToArray();

    private static string NormalizeTabId(string id) =>
        StringComparer.OrdinalIgnoreCase.Equals(id, "FileTab") ? "file" : id;

    private static string EvidencePath(string root, string relativePath) =>
        Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));

    private static ImageContentValidation? TryValidateContent(string path)
    {
        if (!FreePVisualEvidenceCaptureOrchestration.IsNonzeroFile(path))
            return null;
        try
        {
            return ImageDiff.ValidateContent(path);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or NotSupportedException)
        {
            return null;
        }
    }

    private static TitleBarRasterValidation? TryValidateTitleBarRaster(
        string path,
        WholeWindowVisualEvidenceBounds bounds)
    {
        if (!FreePVisualEvidenceCaptureOrchestration.IsNonzeroFile(path) || !bounds.IsVisible)
            return null;
        try
        {
            return ImageDiff.ValidateFreePTitleBarRegion(path, bounds);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or NotSupportedException)
        {
            return null;
        }
    }

    private static string ValidationDetail(ImageContentValidation? validation) => validation is null
        ? "missing or undecodable."
        : validation.IsValid
            ? "valid."
            : string.Join(", ", validation.Failures) + ".";

}
