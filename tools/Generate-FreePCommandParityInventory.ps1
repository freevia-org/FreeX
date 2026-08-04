param(
    [string]$InventoryPath = "docs\parity\freep-command-parity-inventory.json",
    [string]$MarkdownPath = "docs\parity\freep-command-parity-inventory.md",
    [switch]$Check
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot "ToolScriptSupport.ps1")

$resolvedInventoryPath = Resolve-ToolRepoPath -Path $InventoryPath -RepoRoot $repoRoot
$resolvedMarkdownPath = Resolve-ToolRepoPath -Path $MarkdownPath -RepoRoot $repoRoot
$definitionsProject = ConvertTo-ToolXmlAttribute (Resolve-ToolRepoPath -Path "freep\FreeP.Ribbon.Definitions\FreeP.Ribbon.Definitions.csproj" -RepoRoot $repoRoot)

$programSource = @'
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Free.Shared.Ribbon;
using FreeP.Ribbon.Definitions;

if (args.Length != 2)
{
    throw new ArgumentException("Expected JSON and Markdown output paths.");
}

var inventory = FreePCommandInventory.Build();
var options = new JsonSerializerOptions
{
    WriteIndented = true,
    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
};

File.WriteAllText(args[0], JsonSerializer.Serialize(inventory, options) + Environment.NewLine, Encoding.UTF8);
File.WriteAllText(args[1], FreePCommandInventoryMarkdown.Build(inventory), Encoding.UTF8);

internal static class FreePCommandInventory
{
    public static InventoryDocument Build()
    {
        var wpf = Collect(FreePRibbon.Build(FreePRibbonCapabilities.Wpf), "WPF");
        var avalonia = Collect(FreePRibbon.Build(FreePRibbonCapabilities.Avalonia), "Avalonia");
        EnsureSmartArtSourceCoverage(wpf, avalonia);
        var commandIds = wpf.Keys.Concat(avalonia.Keys)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        var commands = commandIds.Select(commandId =>
        {
            wpf.TryGetValue(commandId, out var wpfLocations);
            avalonia.TryGetValue(commandId, out var avaloniaLocations);
            var wpfPresent = wpfLocations is { Count: > 0 };
            var avaloniaPresent = avaloniaLocations is { Count: > 0 };
            var classification = Classify(commandId, wpfPresent, avaloniaPresent, wpfLocations, avaloniaLocations, commandIds);
            return new CommandEntry(
                CommandId: commandId,
                Label: (wpfLocations ?? avaloniaLocations ?? throw new InvalidOperationException()).First().Label,
                WpfPresent: wpfPresent,
                AvaloniaPresent: avaloniaPresent,
                Surface: Surface(wpfPresent, avaloniaPresent),
                MissingSide: MissingSide(wpfPresent, avaloniaPresent),
                Classification: classification.Name,
                Notes: classification.Notes,
                WpfLocations: wpfLocations ?? Array.Empty<CommandLocation>(),
                AvaloniaLocations: avaloniaLocations ?? Array.Empty<CommandLocation>());
        }).ToArray();

        var workflowEvidence = BuildWorkflowEvidence();

        return new InventoryDocument(
            SchemaVersion: 1,
            GeneratedBy: "tools/Generate-FreePCommandParityInventory.ps1",
            Source: "freep/FreeP.Ribbon.Definitions FreePRibbon.Build(FreePRibbonCapabilities.Wpf/Avalonia) plus freep/FreeP.App.Presentation SmartArtLayoutPreset",
            Notes: "Raw missing counts preserve one-sided generated profile surface counts. Actionable missing counts exclude platform-only commands so Avalonia shell and backed profile commands are not reported as WPF or Avalonia implementation gaps. Platform-only rows are allowed only when the note names the intended shell/profile variance and the WPF shell route that carries the behavior. Workflow evidence rows track bounded FreeP WPF/Avalonia parity-depth slices that are not command gaps.",
            Summary: new InventorySummary(
                TotalCommands: commands.Length,
                Both: commands.Count(command => command.Surface == "both"),
                WpfOnly: commands.Count(command => command.Surface == "wpf-only"),
                AvaloniaOnly: commands.Count(command => command.Surface == "avalonia-only"),
                MissingWpf: commands.Count(command => command.MissingSide == "WPF"),
                MissingAvalonia: commands.Count(command => command.MissingSide == "Avalonia"),
                ActionableMissingWpf: commands.Count(command => command.MissingSide == "WPF" && command.Classification != "platform-only"),
                ActionableMissingAvalonia: commands.Count(command => command.MissingSide == "Avalonia" && command.Classification != "platform-only"),
                Shared: commands.Count(command => command.Classification == "shared"),
                AvaloniaGaps: commands.Count(command => command.Classification == "avalonia-gap"),
                KnownDeferred: commands.Count(command => command.Classification == "known-deferred"),
                PlatformOnly: commands.Count(command => command.Classification == "platform-only"),
                CommandIdAliases: commands.Count(command => command.Classification == "command-id-alias"),
                WorkflowEvidenceRows: workflowEvidence.Count),
            WorkflowEvidence: workflowEvidence,
            Commands: commands);
    }

    private static void EnsureSmartArtSourceCoverage(
        IReadOnlyDictionary<string, IReadOnlyList<CommandLocation>> wpf,
        IReadOnlyDictionary<string, IReadOnlyList<CommandLocation>> avalonia)
    {
        // Derived from SmartArtLayoutPreset.Cycle2 through SlideObjectInsertionPlanner and
        // FreePRibbon's Enum.GetValues gallery projection. Keep this guard here so the
        // generated inventory cannot silently omit a newly authored SmartArt route.
        const string cycle2CommandId = "freep.insert-smartart-cycle2";
        if (!wpf.ContainsKey(cycle2CommandId) || !avalonia.ContainsKey(cycle2CommandId))
            throw new InvalidOperationException(
                $"The generated WPF/Avalonia command profiles must expose the source SmartArt layout {cycle2CommandId}.");
    }

    private static IReadOnlyList<WorkflowEvidenceEntry> BuildWorkflowEvidence() =>
    [
        new(
            EvidenceId: "freep.presenter.recording.execution",
            Area: "Presenter recording and media artifact execution",
            Status: "shared-executable-evidence",
            HostCoverage: "WPF/Avalonia shared planner plus thin slideshow-window adapters",
            EvidenceDocs:
            [
                "docs/parity/freep-presenter-recording-backend-contract-2026-07-05.md",
                "docs/parity/freep-presenter-recording-capture-injection-2026-07-06.md",
                "docs/parity/freep-presenter-recording-execution-2026-07-04.md",
                "docs/parity/freep-presenter-recording-media-artifact-manifest-2026-07-05.md",
                "docs/parity/freep-media-caption-shared-sidecar-retention-2026-07-14.md",
                "docs/parity/freep-presenter-recording-microphone-handoff-2026-07-14.md",
                "docs/parity/freep-presenter-recording-camera-handoff-2026-07-14.md",
                "docs/parity/freep-presenter-recording-default-camera-encoding-readiness-2026-07-14.md",
                "docs/parity/freep-presenter-recording-camera-payload-2026-07-14.md",
                "docs/parity/freep-presenter-recording-unavailable-hardware-readiness-2026-07-14.md",
                "docs/parity/freep-powerpoint-native-media-caption-package-baseline-2026-07-05.md",
                "docs/parity/freep-powerpoint-native-media-caption-package-baseline-2026-07-13.md",
                "docs/parity/freep-media-caption-relid-collision-2026-07-14.md",
                "docs/parity/freep-media-caption-ttml-sidecar-retention-2026-07-14.md",
                "docs/parity/freep-presenter-recording-review-2026-07-04.md"
            ],
            Verification:
            [
                "freep/FreeP.App.Presentation.Tests/SlideShowRecordingExecutionPlannerTests.cs",
                "freep/FreeP.App.Presentation.Tests/SlideShowRecordingHostAdapterParityPlannerTests.cs",
                "freep/FreeP.App.Presentation.Tests/SlideShowRecordingReviewPlannerTests.cs",
                "freep/FreeP.App.Host.Tests/MediaFieldsTests.cs",
                "freep/FreeP.App.Host.Tests/WpfWindowsRecordingCaptureBackendTests.cs",
                "freep/FreeP.App.Avalonia.Tests/AvaloniaWindowsRecordingCaptureBackendTests.cs",
                "freep/FreeP.App.Host.Tests/SlideShowTests.cs",
                "freep/FreeP.App.Avalonia.Tests/SlideShowWindowHeadlessTests.cs"
            ],
            RemainingWork: "Shared recording capture adapter readiness contracts, paired WPF/Avalonia backend injection, paired real Windows microphone narration handoff evidence, paired Windows camera handoff readiness, paired default no-COM camera handoff/package-target readiness without encoded-payload or PowerPoint-baseline claims, paired deterministic injected encoded camera media payload artifacts through host-specific PPTX package paths, explicit planner evidence-source tagging that separates injected payload packaging, default-engine handoff, future default-engine payload rows, and PowerPoint COM baselines, paired OS-backed unavailable-hardware/no-device evidence, deterministic captured-artifact host evidence, review rows, session-summary persistable counts, captured PPTX media-part payload authoring, generated WebVTT recording-caption artifact persistence, focused single-track, external-link, multi-track, original-path/relationship-id, relationship-id collision remapping, content-type override, shared-sidecar, and basic TTML/DFXP cue parsing/package retention baselines are covered. Live capture on real microphone/camera hardware, actual local default no-COM camera video encoding that produces non-empty mp4 payload bytes, broader real-deck PowerPoint-native media/caption corpus baselines, and PowerPoint COM recording baselines remain deferred."),
        new(
            EvidenceId: "freep.presenter.recording.default-camera-encoding-readiness",
            Area: "Local default no-COM camera encoding readiness",
            Status: "shared-readiness-evidence",
            HostCoverage: "WPF/Avalonia default Windows camera engines feed source-tagged handoff-only readiness rows, while deterministic injected capture-engine rows prove paired mp4 package payload paths without a PowerPoint COM claim",
            EvidenceDocs:
            [
                "docs/parity/freep-presenter-recording-default-camera-encoding-readiness-2026-07-14.md",
                "docs/parity/freep-presenter-recording-camera-handoff-2026-07-14.md",
                "docs/parity/freep-presenter-recording-camera-payload-2026-07-14.md"
            ],
            Verification:
            [
                "freep/FreeP.App.Presentation/SlideShowRecordingHostAdapterParityPlanner.cs",
                "freep/FreeP.App.Presentation.Tests/SlideShowRecordingHostAdapterParityPlannerTests.cs",
                "freep/FreeP.App.Host.Tests/WpfWindowsRecordingCaptureBackendTests.cs",
                "freep/FreeP.App.Avalonia.Tests/AvaloniaWindowsRecordingCaptureBackendTests.cs"
            ],
            RemainingWork: "Paired WPF/Avalonia default no-COM camera handoff readiness records stable video/mp4 package targets while explicitly proving default-engine rows have no local encoded payload bytes and no PowerPoint COM baseline claim. Paired deterministic injected capture-engine rows prove non-empty mp4 package payload paths separately, and the shared planner now has a distinct source-specific row for future actual default-engine mp4 payload success, so local default no-COM camera video encoding that produces non-empty mp4 payloads, live unavailable-hardware/permission UX, PowerPoint COM recording baselines, and broader real-deck PowerPoint-native media/caption corpus baselines remain deferred until real evidence lands."),
        new(
            EvidenceId: "freep.presenter.recording.unavailable-hardware-readiness",
            Area: "Unavailable microphone/camera hardware readiness",
            Status: "shared-readiness-evidence",
            HostCoverage: "WPF/Avalonia Windows recording adapters feed a shared no-device evidence contract that distinguishes unavailable hardware from unregistered adapters",
            EvidenceDocs:
            [
                "docs/parity/freep-presenter-recording-unavailable-hardware-readiness-2026-07-14.md",
                "docs/parity/freep-presenter-recording-camera-handoff-2026-07-14.md",
                "docs/parity/freep-presenter-recording-default-camera-encoding-readiness-2026-07-14.md"
            ],
            Verification:
            [
                "freep/FreeP.App.Presentation/SlideShowRecordingHostAdapterParityPlanner.cs",
                "freep/FreeP.App.Presentation.Tests/SlideShowRecordingHostAdapterParityPlannerTests.cs",
                "freep/FreeP.App.Host.Tests/WpfWindowsRecordingCaptureBackendTests.cs",
                "freep/FreeP.App.Avalonia.Tests/AvaloniaWindowsRecordingCaptureBackendTests.cs"
            ],
            RemainingWork: "Paired WPF/Avalonia unavailable-hardware readiness now proves OS-backed recording adapters can report no microphone/camera devices without claiming capture, encoded payloads, or PowerPoint COM baselines. Live capture on real microphone/camera hardware, local default camera mp4 encoding, PowerPoint COM recording baselines, and broader real-deck media/caption corpus baselines remain deferred."),
        new(
            EvidenceId: "freep.media-caption.native-sidecar-depth",
            Area: "PowerPoint-native media and caption sidecar package depth",
            Status: "shared-package-retention-evidence",
            HostCoverage: "WPF/Avalonia consume shared PPTX reader/writer media package paths and caption descriptors with no host-specific media sidecar policy",
            EvidenceDocs:
            [
                "docs/parity/freep-media-caption-native-media-sidecar-depth-2026-07-14.md",
                "docs/parity/freep-media-caption-relid-collision-2026-07-14.md",
                "docs/parity/freep-media-caption-ttml-sidecar-retention-2026-07-14.md",
                "docs/parity/freep-media-caption-playback-2026-07-24.md"
            ],
            Verification:
            [
                "freep/FreeP.Core.Model/Slide.cs",
                "freep/FreeP.Core.IO/PptxPackageReader.cs",
                "freep/FreeP.Core.IO/PptxPackageWriter.cs",
                "freep/FreeP.App.Host.Tests/MediaFieldsTests.cs",
                "freep/FreeP.App.Presentation.Tests/PresentationMediaTranscriptPlannerTests.cs",
                "freep/FreeP.App.Avalonia/AvaloniaSlideShowMediaController.cs",
                "freep/FreeP.App.Avalonia.Tests/AvaloniaMediaPlaybackAdapterTests.cs",
                "freep/FreeP.App.Host/SlideShowMediaController.cs",
                "freep/FreeP.App.Host.Tests/SlideShowTests.cs"
            ],
            RemainingWork: "Imported embedded media now retains original ppt/media package paths, matching package-snapshot bytes save back to the authored media path, nested caption sidecars keep package entries plus relationship targets after semantic slide edits, colliding native caption relationship ids remap away from writer-owned poster/media ids while retargeting p20media:caption metadata, and the shared planner now resolves TTML/DFXP inherited body/div offsets plus frame/tick clocks before both WPF and Avalonia slideshow playback surface available cues from the active media clock. Broader real-deck PowerPoint-native media/caption baselines, PowerPoint COM baselines, advanced timing/style/accessibility semantics, and real microphone/camera/playback/capture-device behavior remain deferred."),
        new(
            EvidenceId: "freep.presenter.ink.execution",
            Area: "Presenter ink, laser, and persistence execution",
            Status: "shared-executable-evidence",
            HostCoverage: "WPF/Avalonia shared planner, overlay render primitives, and retention planning",
            EvidenceDocs:
            [
                "docs/parity/freep-presenter-ink-custom-show-persistence-2026-07-05.md",
                "docs/planning/freep-powerpoint-parity-status-2026-06-27.md"
            ],
            Verification:
            [
                "freep/FreeP.App.Presentation.Tests/SlideShowInkExecutionPlannerTests.cs",
                "freep/FreeP.App.Presentation.Tests/SlideShowInkPersistencePlannerTests.cs",
                "freep/FreeP.App.Host.Tests/SlideShowTests.cs",
                "freep/FreeP.App.Avalonia.Tests/SlideShowWindowHeadlessTests.cs"
            ],
            RemainingWork: "Retained presenter ink now carries full playback-route metadata, source slide ids, playback counts, and repeated custom-show occurrence indexes through the shared WPF/Avalonia planner. Authored PPTX ink package baselines, PowerPoint-authoritative presenter UI baselines, and PowerPoint visual baselines remain deferred."),
        new(
            EvidenceId: "freep.presenter.session.summary",
            Area: "Presenter recording plus ink session summary",
            Status: "shared-executable-evidence",
            HostCoverage: "WPF/Avalonia slideshow-window summary state over shared recording and ink planners",
            EvidenceDocs:
            [
                "docs/planning/freep-powerpoint-parity-status-2026-06-27.md"
            ],
            Verification:
            [
                "freep/FreeP.App.Presentation.Tests/SlideShowPresenterSessionSummaryPlannerTests.cs",
                "freep/FreeP.App.Host.Tests/SlideShowTests.cs",
                "freep/FreeP.App.Avalonia.Tests/SlideShowWindowHeadlessTests.cs"
            ],
            RemainingWork: "PowerPoint-authoritative recording studio/session-summary baselines and capture-backed transcript evidence remain deferred."),
        new(
            EvidenceId: "freep.review.comments.thread-depth",
            Area: "Modern comments mention, reply, filter, navigation, and action-state depth",
            Status: "shared-planner-and-host-evidence",
            HostCoverage: "WPF/Avalonia shared review planner plus thin WPF adapter and Avalonia headless consumers",
            EvidenceDocs:
            [
                "docs/parity/freep-comments-review-navigation-2026-07-03.md",
                "docs/parity/freep-modern-comments-anchor-fidelity-2026-07-03.md",
                "docs/parity/freep-modern-comment-author-identity-2026-07-04.md",
                "docs/parity/freep-comment-mention-detail-2026-07-04.md",
                "docs/parity/freep-comment-mention-insertion-2026-07-06.md",
                "docs/parity/freep-comment-mention-picker-2026-07-24.md",
                "docs/parity/freep-comment-thread-filter-depth-2026-07-04.md",
                "docs/parity/freep-comments-review-accessibility-evidence-inventory-2026-07-05.md"
            ],
            Verification:
            [
                "freep/FreeP.App.Presentation.Tests/PresentationReviewWorkflowPlannerTests.cs",
                "freep/FreeP.App.Host.Tests/ReviewWorkflowAdapterTests.cs",
                "freep/FreeP.App.Host.Tests/SectionsCommentsTests.cs",
                "freep/FreeP.App.Avalonia.Tests/MainWindowHeadlessTests.cs"
            ],
            RemainingWork: "Shared mention candidate planning, selectable WPF/Avalonia mention pickers, and mention insertion now route through the WPF/Avalonia review planner and thin host adapters. PowerPoint-authoritative review-pane visual baselines, coauthor presence, and notification routing remain deferred."),
        new(
            EvidenceId: "freep.review.accessibility.proofing-depth",
            Area: "Accessibility checker remediation, reading order, and proofing action depth",
            Status: "shared-planner-and-host-evidence",
            HostCoverage: "WPF/Avalonia shared review planner with host row-action, reading-order, and proofing adapters",
            EvidenceDocs:
            [
                "docs/parity/freep-accessibility-table-header-depth-2026-07-03.md",
                "docs/parity/freep-comments-review-accessibility-evidence-inventory-2026-07-05.md"
            ],
            Verification:
            [
                "freep/FreeP.App.Presentation.Tests/PresentationReviewWorkflowPlannerTests.cs",
                "freep/FreeP.App.Host.Tests/ReviewWorkflowAdapterTests.cs",
                "freep/FreeP.App.Avalonia.Tests/MainWindowHeadlessTests.cs"
            ],
            RemainingWork: "PowerPoint-authoritative accessibility checker baselines, grammar-scale proofing, remediation-pane baselines, and reading-order pane visual baselines remain deferred."),
        new(
            EvidenceId: "freep.animation-pane.workflow-depth",
            Area: "Animation pane row workflow, timing/effect options, reordering, and playback readiness",
            Status: "shared-planner-and-host-evidence",
            HostCoverage: "WPF/Avalonia consume shared animation-pane workflow evidence over thin host pane adapters",
            EvidenceDocs:
            [
                "docs/parity/freep-animation-pane-workflow-depth-2026-07-06.md",
                "docs/parity/freep-animation-pane-advanced-effect-options-2026-07-05.md",
                "docs/parity/freep-remaining-imported-animation-playback-2026-07-13.md",
                "docs/parity/freep-animation-playback-frame-evidence-2026-07-13.md",
                "docs/parity/freep-animation-pane-playback-workflow-evidence-2026-07-14.md",
                "docs/parity/freep-slideshow-playback-readiness-2026-07-14.md",
                "docs/parity/freep-animation-pane-powerpoint-baseline-readiness-2026-07-14.md",
                "docs/parity/freep-animation-split-effect-options-wave22-20260727.md",
                "docs/parity/freep-animation-asymmetric-scale-playback-wave24-20260727.md"
            ],
            Verification:
            [
                "freep/FreeP.Core.Model/AnimationDirectionSemantics.cs",
                "freep/FreeP.App.Presentation.Tests/AnimationPanePlannerTests.cs",
                "freep/FreeP.App.Presentation.Tests/AnimationPresetRoundTripTests.cs",
                "freep/FreeP.App.Presentation.Tests/SlideShowPlaybackPlannerTests.cs",
                "freep/FreeP.App.Host.Tests/AnimationPaneTests.cs",
                "freep/FreeP.App.Host.Tests/SlideShowHostPolicySourceTests.cs",
                "freep/FreeP.App.Avalonia.Tests/SlideShowHostPolicySourceTests.cs",
                "freep/FreeP.App.Avalonia.Tests/MainWindowHeadlessTests.cs"
            ],
            RemainingWork: "Shared animation-pane row evidence now covers selected-row state, timing editors, effect-option rows, reorder availability, playback control readiness, all four Split direction combinations (Horizontal In/Out and Vertical In/Out) with legacy axis-only compatibility and PPTX subtype round-trip, deterministic playback plans for remaining imported Dissolve/Flash/Spiral/Swivel/Bounce/Float/Swoop/Boomerang families, asymmetric Grow/Shrink X/Y playback through shared planner/frame contracts and paired WPF/Avalonia host adapters, renderer-neutral playback frame descriptors consumed by WPF/Avalonia slideshow hosts, paired no-COM pane playback workflow/session evidence rows, paired no-COM slideshow playback-readiness host rows, and no-COM PowerPoint/WPF/Avalonia baseline capture readiness manifests for pane UI plus playback checkpoints. Capturing and comparing the PowerPoint-authoritative animation-pane UI baselines and exact advanced effect playback visuals still requires a COM-capable PowerPoint baseline machine."),
        new(
            EvidenceId: "freep.export.backstage.package-handoff",
            Area: "Backstage export and print package-handoff depth beyond notes-page PDF",
            Status: "shared-planner-and-tool-evidence",
            HostCoverage: "WPF/Avalonia shared Backstage export, print package, image export, and video frame-package planners with PowerPoint COM rows n/a/deferred",
            EvidenceDocs:
            [
                "docs/parity/freep-export-backstage-evidence-2026-07-05.md",
                "docs/parity/2026-06-27-avalonia-wpf-parity-scope.md"
            ],
            Verification:
            [
                "freep/FreeP.App.Presentation.Tests/PresentationExportBackstageEvidencePlannerTests.cs",
                "tools/FreeP.RenderCompare.Tests/ExportBackstageEvidenceTests.cs",
                "tools/FreeP.RenderCompare --export-backstage-evidence <deck.pptx> <outDir>"
            ],
            RemainingWork: "PowerPoint-authoritative fixed-layout, image, video, print, and Backstage visual baselines still require a PowerPoint COM-capable machine; local WPF/Avalonia evidence does not claim Microsoft PowerPoint visual parity."),
        new(
            EvidenceId: "freep.export.pdf-visual-baseline-readiness",
            Area: "PowerPoint-authoritative PDF visual baseline readiness",
            Status: "shared-baseline-readiness-evidence",
            HostCoverage: "WPF/Avalonia share one PDF/export package contract with source-bound manifest, WPF/Avalonia PDF and page-raster artifact patterns, paired diff-report paths, and deferred PowerPoint PDF/PNG artifact paths; no local PowerPoint COM visual baseline is claimed",
            EvidenceDocs:
            [
                "docs/parity/freep-pdf-visual-baseline-readiness-2026-07-14.md",
                "docs/parity/freep-pdf-baseline-diff-readiness-2026-07-14.md"
            ],
            Verification:
            [
                "freep/FreeP.App.Presentation/PresentationPdfVisualBaselineReadinessPlanner.cs",
                "freep/FreeP.App.Presentation.Tests/PresentationPdfVisualBaselineReadinessPlannerTests.cs"
            ],
            RemainingWork: "No-COM readiness now covers portable slide PDF, full-page raster PDF, 3-up handout PDF, and notes-page PDF rows with source-normalized manifest paths, matching WPF/Avalonia fingerprints, WPF/Avalonia page-raster artifact patterns, paired WPF-vs-Avalonia and deferred PowerPoint diff report targets, pinned raster DPI, and explicit PowerPoint PDF/PNG artifact targets for a COM-capable baseline machine. Actual PowerPoint-exported PDFs/PNGs, calibrated visual diffs, thresholds, and representative real-deck capture still require PowerPoint.Application COM on a baseline host."),
        new(
            EvidenceId: "freep.export.pdf-ellipse-fixed-layout",
            Area: "Fixed-layout PDF ellipse and oval shape export",
            Status: "shared-fixed-layout-evidence",
            HostCoverage: "WPF/Avalonia share the same FreeP PDF exporter, notes-page PDF thumbnail mapper, handout PDF thumbnail mapper, and shared portable/Skia PDF draw-op model",
            EvidenceDocs:
            [
                "docs/parity/freep-pdf-ellipse-export-2026-07-13.md"
            ],
            Verification:
            [
                "tests/Free.Shared.Pdf.Tests/PortablePdfWriterTests.cs",
                "freep/FreeP.App.Host.Tests/PresentationPdfExporterTests.cs",
                "freep/FreeP.App.Presentation.Tests/PresentationExportPlannerTests.cs"
            ],
            RemainingWork: "Axis-aligned oval/ellipse fixed-layout output is covered through shared WPF/Avalonia PDF paths. Broader gradient-stop transparency nuance, soft-edge/blur tuning, richer clipping cases, and PowerPoint-authoritative PDF visual baselines remain deferred."),
        new(
            EvidenceId: "freep.export.pdf-picture-frame-clips",
            Area: "Fixed-layout PDF picture frame clipping",
            Status: "shared-fixed-layout-evidence",
            HostCoverage: "WPF/Avalonia share the same FreeP PDF exporter and shared portable/Skia PDF draw-op model; picture frame geometry is mapped before host-specific PDF emission",
            EvidenceDocs:
            [
                "docs/parity/freep-pdf-picture-frame-export-2026-07-13.md"
            ],
            Verification:
            [
                "tests/Free.Shared.Pdf.Tests/PortablePdfWriterTests.cs",
                "freep/FreeP.App.Host.Tests/PresentationPdfExporterTests.cs"
            ],
            RemainingWork: "Ellipse and roundRect picture-frame masks now export through shared WPF/Avalonia PDF paths. Follow-on picture crop, picture alpha, picture color-effect, and shape-opacity PDF slices cover the next bounded effects. Arbitrary custom/freeform picture-frame clipping, richer shape effects, and PowerPoint-authoritative PDF visual baselines remain deferred."),
        new(
            EvidenceId: "freep.export.pdf-shape-opacity",
            Area: "Fixed-layout PDF shape fill and outline opacity",
            Status: "shared-fixed-layout-evidence",
            HostCoverage: "WPF/Avalonia share the same FreeP PPTX alpha model, fixed-layout PDF exporter, notes-page PDF thumbnail mapper, handout PDF thumbnail mapper, and shared portable PDF draw-op model",
            EvidenceDocs:
            [
                "docs/parity/freep-pdf-shape-opacity-export-2026-07-14.md",
                "docs/parity/freep-pdf-effect-thumbnail-export-2026-07-13.md"
            ],
            Verification:
            [
                "freep/FreeP.App.Host.Tests/PptxRoundTripTests.cs",
                "freep/FreeP.App.Host.Tests/PresentationPdfExporterTests.cs",
                "freep/FreeP.App.Presentation.Tests/PresentationExportPlannerTests.cs"
            ],
            RemainingWork: "PPTX-authored shape fill and outline alpha now round-trip through the shared model and survive full-slide, notes-page, and handout PDF export paths used by WPF/Avalonia. Gradient-stop transparency nuance, soft-edge/blur tuning, richer arbitrary clipping cases, and PowerPoint-authoritative PDF visual baselines remain deferred."),
        new(
            EvidenceId: "freep.table.inline-text.workflow-depth",
            Area: "Rich inline table-cell text editing, paragraph formatting, selection, and persistence",
            Status: "shared-planner-and-host-evidence",
            HostCoverage: "WPF/Avalonia shared TableCellEditPlanner and renderer-neutral rich clipboard routes with WPF RichTextBox and Avalonia native-input/custom-rich-surface adapters, including bounded external RTF and XamlPackage ingestion with editable native table cell styles",
            EvidenceDocs:
            [
                "docs/parity/freep-rich-clipboard-wave15-20260727.md",
                "docs/parity/freep-rich-effects-clipboard-wave16-20260727.md",
                "docs/parity/freep-external-rtf-paste-wave17-20260727.md",
                "docs/parity/freep-wave74-xamlpackage-clipboard-parity-20260731.md",
                "docs/parity/freep-rich-table-cell-editing-shared-visual-2026-07-27.md",
                "docs/parity/freep-table-cell-rich-editor-fidelity-2026-07-03.md",
                "docs/parity/freep-list-gallery-image-bullet-ui-2026-07-05.md",
                "docs/parity/freep-table-cell-tab-navigation-2026-07-13.md",
                "docs/parity/freep-table-cell-keyboard-routing-2026-07-24.md",
                "docs/planning/freep-powerpoint-parity-status-2026-06-27.md"
            ],
            Verification:
            [
                "freep/FreeP.App.Presentation.Tests/TableCellEditPlannerTests.cs",
                "freep/FreeP.App.Presentation.Tests/TableEditCommandTests.cs",
                "freep/FreeP.App.Presentation.Tests/BulletsAutofitTests.cs",
                "freep/FreeP.App.Presentation.Tests/TextLayoutPlannerTests.cs",
                "freep/FreeP.App.Host.Tests/CanvasEditingTests.cs",
                "freep/FreeP.App.Host.Tests/RibbonEditorCompleteness5BTests.cs",
                "freep/FreeP.App.Rendering.Avalonia.Tests/SlideCanvasAvaloniaTests.cs",
                "freep/FreeP.App.Rendering.Avalonia.Tests/AvaloniaRichTextEditorTests.cs",
                "freep/FreeP.App.Presentation.Tests/InCanvasRichClipboardTests.cs",
                "freep/FreeP.App.Presentation.Tests/InCanvasRichTextVisualPlannerTests.cs",
                "freep/FreeP.App.Host.Tests/RichTextEditorTests.cs",
                "freep/FreeP.App.Host.Tests/WpfRichTextClipboardAdapterTests.cs",
                "freep/FreeP.App.Avalonia.Tests/PresentationClipboardInteropTests.cs",
                "freep/FreeP.App.Avalonia.Tests/MainWindowHeadlessTests.cs",
                "freep/FreeP.App.Presentation.Tests/ExternalRichTextClipboardTests.cs"
                ,"freep/FreeP.App.Presentation/ExternalXamlClipboardPlanner.cs"
                ,"freep/FreeP.App.Host.Tests/OsClipboardServiceTests.cs"
                ,"freep/FreeP.App.Host.Tests/WpfRichTextClipboardAdapterTests.cs"
                ,"freep/FreeP.App.Presentation.Tests/ExternalRichTextClipboardTests.cs"
                ,"freep/FreeP.App.Avalonia.Tests/PresentationClipboardInteropTests.cs"
            ],
            RemainingWork: "WPF/Avalonia now share mixed-run and paragraph-preserving edits, marker sequencing, selection/caret rendering, rich copy/cut/paste payloads including modeled inline effects, inline picture/object placement, external OLE activation, plain-text clipboard interoperability, picture-bullet picker payload execution, paragraph authoring, PPTX media-part persistence, Tab/Shift+Tab navigation, focused-editor keyboard ownership, commit/cancel routing, and bounded common external RTF and XamlPackage ingestion in Avalonia, including editable native table cell fill, border, inset, vertical-anchor styles, safe run-level hyperlinks, bounded list markers, recursive nested inline tables from the supported XamlPackage path, and common RTF tab leaders in the shared rich-text plan. Avalonia still uses a custom rich surface over a native TextBox rather than a framework-native RichTextBox. Unsupported XamlPackage resources and FlowDocument controls, nested inline tables from unsupported RTF forms, unsupported RTF destinations and controls, richer RTF lists/fields, advanced RTF leader rendering/provider-specific controls, broader IME/RTL/FlowDocument behavior, in-place OLE hosting, and PowerPoint-authoritative list-gallery/rich-editor visual baselines remain deferred."),
        new(
            EvidenceId: "freep.clipboard.external-rtf-depth",
            Area: "External RTF list, paragraph-layout, hyperlink, field, and tab-stop paste depth",
            Status: "shared-planner-and-host-evidence",
            HostCoverage: "WPF/Avalonia renderer-neutral rich-text model and paste adapters consume the same bounded external RTF planner; WPF remains authoritative and no platform-specific semantic fork was added",
            EvidenceDocs:
            [
                "docs/parity/freep-external-rtf-paste-wave18-20260727.md",
                "docs/parity/freep-external-rtf-tables-wave19-20260727.md",
                "docs/parity/freep-external-rtf-picture-paste-20260730.md",
                "docs/parity/freep-external-rtf-list-level-templates-20260731.md",
                "docs/parity/freep-external-rtf-file-hyperlink-20260729.md",
                "docs/parity/freep-external-rtf-field-runs-20260730.md",
                "docs/parity/freep-external-rtf-object-results-20260730.md",
                "docs/parity/freep-external-rtf-tab-stops-20260801.md",
                "docs/parity/freep-external-rtf-tab-leaders-20260801.md"
            ],
            Verification:
            [
                "freep/FreeP.App.Presentation/ExternalRichTextClipboardPlanner.cs",
                "freep/FreeP.App.Presentation/InCanvasRichTextVisualPlanner.cs",
                "freep/FreeP.App.Presentation/ClipboardTablePlanner.cs",
                "freep/FreeP.App.Presentation.Tests/ExternalRichTextClipboardTests.cs",
                "freep/FreeP.App.Presentation.Tests/InCanvasRichTextVisualPlannerTests.cs",
                "freep/FreeP.App.Presentation.Tests/InCanvasRichClipboardTests.cs",
                "freep/FreeP.App.Rendering.Avalonia.Tests/AvaloniaRichTextEditorTests.cs",
                "freep/FreeP.App.Rendering.Wpf/WpfRichTextClipboardAdapter.cs",
                "freep/FreeP.App.Host.Tests/WpfRichTextClipboardAdapterTests.cs"
            ],
            RemainingWork: "The bounded subset now preserves common Word/LibreOffice list markers, nested levels, bounded multi-level level-text substitutions, continuation/restart intent where the existing model can represent it, paragraph alignment/indent/spacing, guarded http/https/mailto/file HYPERLINK field results with remote file hosts blocked, safe non-hyperlink field tokens with cached result text, visible embedded-object result text with inline placement and external activation, common trowd/cellx/cell/row plus nestcell/nestrow table boundaries, recursive Word-style itap nesting as inline tables, merged-cell topology from clmgf/clmrg/clvmgf/clvmrg, common cell pattern shading from clcbpat/clcfpat/clshdng and hatch controls for standalone native slide tables, and validated PNG/JPEG \\pict payloads as slide-level picture shapes while retaining custom-v2 > RTF > plain-text precedence. Advanced RTF table layout and providers that omit nesting-depth controls, unsupported XamlPackage resources and controls, unsupported RTF destinations and controls, complex Word field calculation, RTL/IME nuances, broader Word list-template numbering beyond the bounded level-text substitutions, in-place OLE hosting, and PowerPoint-authoritative external RTF visual baselines remain deferred."),
        new(
            EvidenceId: "freep.header-footer.placeholder-creation",
            Area: "Header/Footer date, footer, and slide-number placeholder creation",
            Status: "shared-planner-and-host-evidence",
            HostCoverage: "WPF/Avalonia route Header & Footer options into the shared planner; no renderer-local placeholder policy",
            EvidenceDocs:
            [
                "docs/parity/freep-header-footer-placeholder-creation-2026-07-05.md",
                "docs/parity/freep-header-footer-options-2026-07-06.md",
                "docs/parity/freep-header-footer-inherited-layout-geometry-2026-07-06.md"
            ],
            Verification:
            [
                "freep/FreeP.App.Presentation.Tests/HeaderFooterCommandPlannerTests.cs",
                "freep/FreeP.App.Host.Tests/RibbonEditorCompleteness5BTests.cs",
                "freep/FreeP.App.Host.Tests/HeaderFooterDialogTests.cs",
                "freep/FreeP.App.Avalonia.Tests/HeaderFooterCommandRoutingTests.cs"
            ],
            RemainingWork: "Shared Header/Footer application now creates missing date, footer, and slide-number placeholders, stamps inherited layout/master geometry before using computed fallback slots, supports title-slide suppression for apply-all, and distinguishes auto-updating date fields from fixed literal date text through WPF/Avalonia thin host options. PowerPoint-authoritative header/footer visual baselines and theme-specific visual tuning remain deferred."),
        new(
            EvidenceId: "freep.chart.number-format-rendering",
            Area: "Chart axis number/date format rendering",
            Status: "shared-render-planner-evidence",
            HostCoverage: "WPF/Avalonia consume shared ChartRenderPlanner text plans with no renderer-local number-format policy",
            EvidenceDocs:
            [
                "docs/parity/freep-chart-number-format-rendering-2026-07-05.md"
            ],
            Verification:
            [
                "freep/FreeP.App.Presentation.Tests/ChartRenderPlannerTests.cs",
                "freep/FreeP.App.Presentation.Tests/RendererNeutralDedupPlannerTests.cs",
                "freep/FreeP.App.Host.Tests/ChartTests.cs",
                "freep/FreeP.App.Host.Tests/ChartDataLabelsTests.cs",
                "freep/FreeP.App.Rendering.Avalonia.Tests/SlideCanvasAvaloniaTests.cs"
            ],
            RemainingWork: "Shared chart label planning now applies preserved PowerPoint-style number/date format codes for common axis and category label cases, including conditional threshold sections, scaled display-unit commas, bounded elapsed-time labels, and bounded fraction labels. Broader Excel custom-format semantics, exact locale behavior, and PowerPoint-authoritative chart visual baselines remain deferred."),
        new(
            EvidenceId: "freep.chart.edge-manual-layout",
            Area: "Chart manual plot and legend edge layout rendering",
            Status: "shared-render-planner-evidence",
            HostCoverage: "WPF/Avalonia consume shared ChartRenderPlanner bounds with no renderer-local manual-layout policy",
            EvidenceDocs:
            [
                "docs/parity/freep-chart-edge-manual-layout-2026-07-06.md"
            ],
            Verification:
            [
                "freep/FreeP.App.Presentation.Tests/ChartRenderPlannerTests.cs"
            ],
            RemainingWork: "Shared chart planning now resolves edge and mixed factor/edge manual layout modes for plot-area and legend rectangles, clamping non-negative bounds inside the chart base rectangle. PowerPoint-authoritative chart visual baselines and nuanced layoutTarget tuning remain deferred."),
        new(
            EvidenceId: "freep.chart.bar-gap-overlap",
            Area: "Chart bar and column gap width plus series overlap",
            Status: "shared-render-planner-evidence",
            HostCoverage: "WPF/Avalonia consume shared ChartRenderPlanner bar and column primitive geometry with no renderer-local spacing policy",
            EvidenceDocs:
            [
                "docs/parity/freep-chart-bar-gap-overlap-2026-07-13.md"
            ],
            Verification:
            [
                "freep/FreeP.Core.Model/ChartShape.cs",
                "freep/FreeP.Core.Model/SlideCloner.cs",
                "freep/FreeP.Core.IO/PptxChartReader.cs",
                "freep/FreeP.Core.IO/PptxChartWriter.cs",
                "freep/FreeP.App.Presentation/ChartRenderPlanner.cs",
                "freep/FreeP.App.Presentation.Tests/ChartRenderPlannerTests.cs",
                "freep/FreeP.App.Host.Tests/ChartTests.cs"
            ],
             RemainingWork: "Bar and column c:gapWidth/c:overlap now round-trip through the model/package and drive shared primitive and data-label spacing for WPF/Avalonia. PowerPoint-authoritative chart visual baselines, 3-D bar/column spacing, and broader type-specific visual fidelity remain deferred."),
        new(
            EvidenceId: "freep.chart.data-label-text-style",
            Area: "Chart-level data-label text styling",
            Status: "shared-model-and-host-evidence",
            HostCoverage: "WPF/Avalonia consume one shared ChartDisplayOptions planner and undo command for chart-scoped c:dLbls text styling; no renderer-local chart-label policy",
            EvidenceDocs:
            [
                "docs/parity/freep-chart-data-label-text-style-20260726.md"
            ],
            Verification:
            [
                "freep/FreeP.Core.Model/ChartDisplayOptions.cs",
                "freep/FreeP.Core.Model/PresentationCommands.ChartOptions.cs",
                "freep/FreeP.App.Presentation/ChartDisplayOptionsPlanner.cs",
                "freep/FreeP.Core.IO/PptxChartReader.cs",
                "freep/FreeP.Core.IO/PptxChartWriter.cs",
                "freep/FreeP.App.Presentation.Tests/ChartDataDialogPlannerTests.cs",
                "freep/FreeP.App.Presentation.Tests/ChartDataCommandTests.cs",
                "freep/FreeP.App.Host.Tests/ChartDataDialogTests.cs",
                "freep/FreeP.App.Avalonia.Tests/MainWindowHeadlessTests.cs"
            ],
            RemainingWork: "Chart-level font family, size, color, bold, and italic controls now round-trip through c:txPr/c:rich and one undoable edit in both hosts; PowerPoint-authoritative chart visual baselines remain deferred."),
        new(
            EvidenceId: "freep.chart.bubble-size-data-labels",
            Area: "Chart bubble-size data-label authoring",
            Status: "shared-model-and-host-evidence",
            HostCoverage: "WPF/Avalonia consume shared chart, series, and point data-label planners and render bubble-size values from BubbleSizes; no renderer-local chart-label policy",
            EvidenceDocs:
            [
                "docs/parity/freep-chart-bubble-size-data-labels-20260726.md"
            ],
            Verification:
            [
                "freep/FreeP.Core.Model/ChartShape.cs",
                "freep/FreeP.Core.Model/ChartDisplayOptions.cs",
                "freep/FreeP.Core.Model/ChartSeriesOptions.cs",
                "freep/FreeP.Core.Model/ChartPointOptions.cs",
                "freep/FreeP.Core.IO/PptxChartReader.cs",
                "freep/FreeP.Core.IO/PptxChartWriter.cs",
                "freep/FreeP.App.Presentation/ChartRenderPlanner.cs",
                "freep/FreeP.App.Presentation/ChartDisplayOptionsPlanner.cs",
                "freep/FreeP.App.Presentation/ChartSeriesOptionsPlanner.cs",
                "freep/FreeP.App.Presentation/ChartPointOptionsPlanner.cs",
                "freep/FreeP.App.Presentation.Tests/ChartDataCommandTests.cs",
                "freep/FreeP.App.Presentation.Tests/ChartDataDialogPlannerTests.cs",
                "freep/FreeP.App.Presentation.Tests/ChartRenderPlannerTests.cs",
                "freep/FreeP.App.Host.Tests/ChartDataLabelsTests.cs",
                "freep/FreeP.App.Host.Tests/ChartDataDialogTests.cs",
                "freep/FreeP.App.Avalonia.Tests/MainWindowHeadlessTests.cs"
            ],
            RemainingWork: "Chart, series, and point bubble-size authoring now round-trips through c:showBubbleSize, remains undoable, and renders from source BubbleSizes in both hosts. PowerPoint-authoritative bubble-label typography and placement baselines remain deferred."),
        new(
            EvidenceId: "freep.chart.series-data-labels",
            Area: "Chart per-series data-label authoring",
            Status: "shared-model-and-host-evidence",
            HostCoverage: "WPF/Avalonia consume one shared ChartSeriesOptions planner and undo command for series-scoped c:dLbls; no renderer-local chart-label policy",
            EvidenceDocs:
            [
                "docs/parity/freep-chart-series-data-labels-20260726.md"
            ],
            Verification:
            [
                "freep/FreeP.Core.Model/ChartSeriesOptions.cs",
                "freep/FreeP.Core.Model/PresentationCommands.ChartSeriesOptions.cs",
                "freep/FreeP.App.Presentation/ChartSeriesOptionsPlanner.cs",
                "freep/FreeP.Core.IO/PptxChartReader.cs",
                "freep/FreeP.Core.IO/PptxChartWriter.cs",
                "freep/FreeP.App.Presentation.Tests/ChartDataDialogPlannerTests.cs",
                "freep/FreeP.App.Presentation.Tests/ChartDataCommandTests.cs",
                "freep/FreeP.App.Host.Tests/ChartDataDialogTests.cs",
                "freep/FreeP.App.Avalonia.Tests/MainWindowHeadlessTests.cs"
            ],
            RemainingWork: "Series-scoped value, percentage, category, series-name, legend-key, position, format, separator, and font styling controls now round-trip through c:dLbls and one undoable edit in both hosts. PowerPoint-authoritative chart visual baselines remain deferred."),
        new(
            EvidenceId: "freep.chart.point-data-labels",
            Area: "Chart per-point data-label authoring",
            Status: "shared-model-and-host-evidence",
            HostCoverage: "WPF/Avalonia consume one shared ChartPointOptions planner and undo command for point-scoped c:dLbl overrides; no renderer-local chart-label policy",
            EvidenceDocs:
            [
                "docs/parity/freep-chart-point-data-labels-20260726.md"
            ],
            Verification:
            [
                "freep/FreeP.Core.Model/ChartShape.cs",
                "freep/FreeP.Core.Model/ChartPointOptions.cs",
                "freep/FreeP.Core.Model/PresentationCommands.ChartPointOptions.cs",
                "freep/FreeP.App.Presentation/ChartPointOptionsPlanner.cs",
                "freep/FreeP.Core.IO/PptxChartReader.cs",
                "freep/FreeP.Core.IO/PptxChartWriter.cs",
                "freep/FreeP.App.Presentation.Tests/ChartDataDialogPlannerTests.cs",
                "freep/FreeP.App.Presentation.Tests/ChartDataCommandTests.cs",
                "freep/FreeP.App.Host.Tests/ChartDataDialogTests.cs",
                "freep/FreeP.App.Avalonia.Tests/MainWindowHeadlessTests.cs"
            ],
            RemainingWork: "Selected-point value, percentage, category, series-name, legend-key, position, format, separator, delete, and font styling overrides now round-trip through c:dLbl entries and one undoable edit in both hosts. PowerPoint-authoritative chart visual baselines remain deferred."),
        new(
            EvidenceId: "freep.chart.bubble-sizing-semantics",
            Area: "Chart bubble authored sizing semantics",
            Status: "shared-render-planner-evidence",
            HostCoverage: "WPF/Avalonia consume shared ChartBubblePrimitive radii with no renderer-local bubble sizing policy",
            EvidenceDocs:
            [
                "docs/parity/freep-chart-bubble-sizing-semantics-2026-07-13.md"
            ],
            Verification:
            [
                "freep/FreeP.Core.Model/ChartShape.cs",
                "freep/FreeP.Core.IO/PptxChartReader.cs",
                "freep/FreeP.Core.IO/PptxChartWriter.cs",
                "freep/FreeP.App.Presentation/ChartRenderPlanner.cs",
                "freep/FreeP.App.Presentation.Tests/ChartRenderPlannerTests.cs",
                "freep/FreeP.App.Host.Tests/ChartTests.cs"
            ],
            RemainingWork: "Shared bubble primitive planning now consumes c:bubbleScale, c:sizeRepresents, and c:showNegBubbles metadata before WPF/Avalonia rendering. PowerPoint-authoritative bubble chart visual baselines and broader chart fidelity remain deferred."),
        new(
            EvidenceId: "freep.chart.pie-first-slice-angle",
            Area: "Chart pie and doughnut first-slice angle preservation",
            Status: "shared-render-planner-evidence",
            HostCoverage: "WPF/Avalonia consume shared ChartPieSlicePrimitive angles from ChartRenderPlanner with no renderer-local pie, doughnut, or 3-D pie angle policy",
            EvidenceDocs:
            [
                "docs/parity/freep-chart-pie-first-slice-angle-2026-07-07.md"
            ],
            Verification:
            [
                "freep/FreeP.Core.Model/ChartShape.cs",
                "freep/FreeP.Core.Model/SlideCloner.cs",
                "freep/FreeP.Core.IO/PptxChartReader.cs",
                "freep/FreeP.Core.IO/PptxChartWriter.cs",
                "freep/FreeP.App.Presentation/ChartRenderPlanner.cs",
                "freep/FreeP.App.Presentation.Tests/ChartRenderPlannerTests.cs",
                "freep/FreeP.App.Host.Tests/ChartTests.cs"
            ],
            RemainingWork: "Pie and doughnut c:firstSliceAng now round-trips through the model and PPTX package and drives shared slice primitive planning. PowerPoint-authoritative visual baselines and broader chart visual fidelity remain deferred."),
        new(
            EvidenceId: "freep.chart.pie3d-depth-rendering",
            Area: "Chart 3-D pie compressed top-face and depth-pass rendering",
            Status: "shared-render-planner-evidence",
            HostCoverage: "WPF/Avalonia consume shared ChartPieSlicePrimitive vertical scale, depth offset, and depth-fill alpha with no renderer-local 3-D pie policy",
            EvidenceDocs:
            [
                "docs/parity/freep-chart-pie3d-depth-rendering-2026-07-14.md"
            ],
            Verification:
            [
                "freep/FreeP.App.Presentation/ChartRenderPlanner.cs",
                "freep/FreeP.App.Rendering.Wpf/SlideCanvas.cs",
                "freep/FreeP.App.Rendering.Avalonia/SlideCanvas.cs",
                "freep/FreeP.App.Presentation.Tests/ChartRenderPlannerTests.cs",
                "freep/FreeP.App.Presentation.Tests/ChartBaselineCorpusTests.cs",
                "freep/FreeP.App.Presentation.Tests/RendererNeutralDedupPlannerTests.cs"
            ],
            RemainingWork: "Shared 3-D pie planning now gives WPF and Avalonia the same compressed top face plus lower depth pass before either renderer draws the top slice. PowerPoint-authoritative 3-D pie visual baselines, side-wall lighting/camera fidelity, and pixel-diff thresholds remain deferred to a COM-capable baseline host."),
        new(
            EvidenceId: "freep.chart.blank-point-rendering",
            Area: "Chart blank-point rendering decisions",
            Status: "shared-render-planner-evidence",
            HostCoverage: "WPF/Avalonia consume shared ChartRenderPlanner null-point policies for bar, column, line, area, scatter, radar, pie, and doughnut charts",
            EvidenceDocs:
            [
                "docs/parity/freep-chart-blank-point-rendering-2026-07-13.md"
            ],
            Verification:
            [
                "freep/FreeP.App.Presentation/ChartRenderPlanner.cs",
                "freep/FreeP.App.Presentation.Tests/ChartRenderPlannerTests.cs",
                "freep/FreeP.App.Presentation.Tests/RendererNeutralDedupPlannerTests.cs"
            ],
            RemainingWork: "Shared chart planning now applies PowerPoint-style c:dispBlanksAs decisions for gap, zero, and span cases across renderer-neutral primitives, including radar path-list contracts and pie/doughnut no-sweep point identity. PowerPoint-authoritative radar/pie/doughnut blank-point visual baselines and bubble charts with missing coordinate inputs remain deferred."),
        new(
            EvidenceId: "freep.chart.stacked-area-bands",
            Area: "Chart stacked area band geometry",
            Status: "shared-render-planner-evidence",
            HostCoverage: "WPF/Avalonia consume shared ChartAreaSeriesPrimitive baseline polygons with no renderer-local stacked-area policy",
            EvidenceDocs:
            [
                "docs/parity/freep-chart-stacked-area-render-planning-2026-07-14.md"
            ],
            Verification:
            [
                "freep/FreeP.App.Presentation/ChartRenderPlanner.cs",
                "freep/FreeP.App.Presentation.Tests/ChartRenderPlannerTests.cs"
            ],
            RemainingWork: "Shared stacked-area planning now scales primary value axes from category totals and emits cumulative band baselines before WPF/Avalonia renderers draw the same area primitives. PowerPoint-authoritative chart visual baselines, mixed positive/negative corpus coverage, and broader type-specific chart visual fidelity remain deferred."),
        new(
            EvidenceId: "freep.chart.surface-grid-rendering",
            Area: "Chart surface grid rendering",
            Status: "shared-render-planner-evidence",
            HostCoverage: "WPF/Avalonia consume shared ChartSurfaceCellPrimitive rectangles with no renderer-local surface grid policy",
            EvidenceDocs:
            [
                "docs/parity/freep-chart-surface-grid-rendering-2026-07-14.md"
            ],
            Verification:
            [
                "freep/FreeP.App.Rendering.Wpf/SlideCanvas.cs",
                "freep/FreeP.App.Rendering.Avalonia/SlideCanvas.cs",
                "freep/FreeP.App.Presentation/ChartRenderPlanner.cs",
                "freep/FreeP.App.Presentation.Tests/ChartRenderPlannerTests.cs",
                "freep/FreeP.App.Presentation.Tests/RendererNeutralDedupPlannerTests.cs"
            ],
            RemainingWork: "Surface and 3-D surface charts now render through the shared surface-cell primitive plan on both WPF and Avalonia. PowerPoint-authoritative surface chart visual baselines, true contour/3-D surface geometry, wireframe styling, and broader real-deck corpus coverage remain deferred."),
        new(
            EvidenceId: "freep.chart.radar-style-render-planning",
            Area: "Chart radar standard, marker, and filled style rendering",
            Status: "shared-render-planner-evidence",
            HostCoverage: "WPF/Avalonia consume shared ChartRadarPrimitivePlan paths, fills, spokes, and markers with no renderer-local radar style policy",
            EvidenceDocs:
            [
                "docs/parity/freep-chart-radar-style-render-planning-2026-07-14.md",
                "docs/parity/freep-chart-powerpoint-baseline-readiness-2026-07-14.md"
            ],
            Verification:
            [
                "freep/FreeP.App.Presentation/ChartRenderPlanner.cs",
                "freep/FreeP.App.Rendering.Wpf/SlideCanvas.cs",
                "freep/FreeP.App.Rendering.Avalonia/SlideCanvas.cs",
                "freep/FreeP.App.Presentation.Tests/ChartRenderPlannerTests.cs",
                "freep/FreeP.App.Presentation.Tests/ChartBaselineCorpusTests.cs",
                "freep/FreeP.App.Presentation.Tests/RendererNeutralDedupPlannerTests.cs",
                "freep/FreeP.App.Host.Tests/SlideCanvasTests.cs",
                "freep/FreeP.App.Rendering.Avalonia.Tests/SlideCanvasAvaloniaTests.cs"
            ],
            RemainingWork: "Standard, marker, and filled radar styles now have explicit no-COM shared-planner evidence plus paired WPF/Avalonia capture-readiness rows. PowerPoint-authoritative radar PNG captures, pixel-diff thresholds, broader real-deck radar corpus coverage, exact axis/ring labeling nuance, and additional radar subtype visual comparisons remain deferred to a COM-capable baseline host."),
        new(
            EvidenceId: "freep.chart.powerpoint-baseline-readiness",
            Area: "Chart PowerPoint baseline capture readiness",
            Status: "shared-baseline-readiness-evidence",
            HostCoverage: "WPF/Avalonia consume shared chart-surface capture requests from ChartRenderPlanner while PowerPoint rows are explicit COM-required baseline contracts",
            EvidenceDocs:
            [
                "docs/parity/freep-chart-powerpoint-baseline-readiness-2026-07-14.md",
                "docs/parity/freep-chart-powerpoint-com-baseline-20260720.md"
            ],
            Verification:
            [
                "freep/FreeP.App.Presentation/ChartVisualBaselineReadinessPlanner.cs",
                "freep/FreeP.App.Presentation.Tests/ChartBaselineCorpusTests.cs"
            ],
            RemainingWork: "The chart baseline-depth corpus now projects stable PowerPoint/WPF/Avalonia capture requests for stock high-low/open-close ticks, 3-D surface facets/wireframe/contours, smooth scatter paths, and 100% stacked normalized extents; fresh COM PNGs now cover the baseline-depth, chart-types, and chart-label corpora. Remaining work is exact Surface3D mesh/camera/facet ownership, WPF chart-label rasterization, broader real-deck radar/stock/doughnut/bubble coverage, and family-specific acceptance thresholds."),
        new(
            EvidenceId: "freep.chart.stock-ohlc-baseline-readiness",
            Area: "Chart stock high-low/open-close baseline readiness",
            Status: "shared-baseline-readiness-evidence",
            HostCoverage: "WPF/Avalonia consume shared stock high-low line and open/close tick primitives plus paired chart-surface capture requests while PowerPoint rows remain explicit COM-required baseline contracts",
            EvidenceDocs:
            [
                "docs/parity/freep-chart-stock-ohlc-baseline-readiness-2026-07-14.md",
                "docs/parity/freep-chart-powerpoint-baseline-readiness-2026-07-14.md"
            ],
            Verification:
            [
                "freep/FreeP.App.Presentation/ChartVisualBaselineReadinessPlanner.cs",
                "freep/FreeP.App.Presentation/ChartRenderPlanner.cs",
                "freep/FreeP.App.Presentation.Tests/ChartBaselineCorpusTests.cs",
                "freep/FreeP.App.Presentation.Tests/ChartRenderPlannerTests.cs"
            ],
            RemainingWork: "Shared stock chart planning now has no-COM baseline-readiness evidence for high-low stems, open/close tick orientation, rising/falling/unchanged price-move classification, and WPF/Avalonia capture request metadata. Microsoft PowerPoint stock PNG captures, exact Office tick stroke styling, broader real-deck OHLC/volume stock variants, and calibrated pixel-diff thresholds remain deferred to a COM-capable baseline host."),
        new(
            EvidenceId: "freep.chart.stock-volume-baseline-readiness",
            Area: "Chart stock volume/open-high-low-close baseline readiness",
            Status: "shared-baseline-readiness-evidence",
            HostCoverage: "WPF/Avalonia consume shared stock volume column primitives, high-low stems, open/close ticks, and paired chart-surface capture requests while PowerPoint rows remain explicit COM-required baseline contracts",
            EvidenceDocs:
            [
                "docs/parity/freep-chart-stock-volume-baseline-readiness-2026-07-14.md",
                "docs/parity/freep-chart-stock-ohlc-baseline-readiness-2026-07-14.md"
            ],
            Verification:
            [
                "freep/FreeP.App.Presentation/ChartVisualBaselineReadinessPlanner.cs",
                "freep/FreeP.App.Presentation/ChartRenderPlanner.cs",
                "freep/FreeP.App.Presentation.Tests/ChartBaselineCorpusTests.cs",
                "freep/FreeP.App.Presentation.Tests/ChartRenderPlannerTests.cs",
                "freep/FreeP.App.Presentation.Tests/RendererNeutralDedupPlannerTests.cs",
                "freep/FreeP.App.Rendering.Wpf/SlideCanvas.cs",
                "freep/FreeP.App.Rendering.Avalonia/SlideCanvas.cs",
                "freep/FreeP.App.Host.Tests/SlideCanvasTests.cs",
                "freep/FreeP.App.Rendering.Avalonia.Tests/SlideCanvasAvaloniaTests.cs"
            ],
            RemainingWork: "Shared stock chart planning now has no-COM baseline-readiness evidence for bottom-band volume columns, five-series volume/open/high/low/close ordering, high-low stems, open/close ticks, WPF/Avalonia renderer consumption, and WPF/Avalonia capture request metadata. Microsoft PowerPoint stock-volume PNG captures, exact Office volume-axis styling, real-deck stock-volume corpus coverage, and calibrated pixel-diff thresholds remain deferred to a COM-capable baseline host."),
        new(
            EvidenceId: "freep.chart.doughnut-ring-baseline-readiness",
            Area: "Chart doughnut ring and hole-size baseline readiness",
            Status: "shared-baseline-readiness-evidence",
            HostCoverage: "WPF/Avalonia consume shared doughnut slice primitives and chart-surface capture requests while PowerPoint rows remain explicit COM-required baseline contracts",
            EvidenceDocs:
            [
                "docs/parity/freep-chart-doughnut-ring-baseline-readiness-2026-07-14.md"
            ],
            Verification:
            [
                "freep/FreeP.App.Presentation/ChartVisualBaselineReadinessPlanner.cs",
                "freep/FreeP.App.Presentation/ChartRenderPlanner.cs",
                "freep/FreeP.App.Presentation.Tests/ChartBaselineCorpusTests.cs",
                "freep/FreeP.App.Presentation.Tests/ChartRenderPlannerTests.cs"
            ],
            RemainingWork: "Shared doughnut planning now has no-COM baseline-readiness evidence for authored hole size, first-slice angle, series-zero innermost ring ordering, and WPF/Avalonia capture request metadata. Microsoft PowerPoint visual baselines, broader real-deck doughnut corpus coverage, pie3D behavior, and pixel-diff thresholds remain deferred to a COM-capable baseline host."),
        new(
            EvidenceId: "freep.omml.transparent-phantom-spacing",
            Area: "OMML transparent phantom spacing classes",
            Status: "shared-layout-evidence",
            HostCoverage: "WPF/Avalonia consume shared MathLayoutEngine row advances and MathBoxRenderPlanner draw ops with no renderer-local math policy",
            EvidenceDocs:
            [
                "docs/planning/freep-powerpoint-parity-status-2026-06-27.md"
            ],
            Verification:
            [
                "freep/FreeP.App.Presentation.Tests/MathLayoutEngineTests.cs",
                "freep/FreeP.App.Presentation.Tests/OmmlParserTests.cs",
                "freep/FreeP.App.Host.Tests/SlideCanvasMathBaselineTests.cs",
                "freep/FreeP.App.Rendering.Avalonia.Tests/SlideCanvasMathBaselineTests.cs"
            ],
            RemainingWork: "Shared row layout now consumes m:phantPr/m:transp for bounded single-token binary, relation, large-operator, and punctuation spacing classes while ambiguous multi-character and structured phantom bases stay packed. PowerPoint-authoritative visual baselines and full OfficeMath spacing-table typography remain deferred."),
        new(
            EvidenceId: "freep.omml.box-operator-emulator-spacing",
            Area: "OMML boxed operator-emulator spacing",
            Status: "shared-layout-evidence",
            HostCoverage: "WPF/Avalonia consume shared MathNode.Box operator-emulator metadata and MathLayoutEngine row advances with no renderer-local math policy",
            EvidenceDocs:
            [
                "docs/parity/freep-omml-box-operator-emulator-2026-07-13.md"
            ],
            Verification:
            [
                "freep/FreeP.App.Presentation/Math/MathNode.cs",
                "freep/FreeP.App.Presentation/Math/OmmlParser.cs",
                "freep/FreeP.App.Presentation/Math/MathLayoutEngine.cs",
                "freep/FreeP.App.Presentation.Tests/OmmlParserTests.cs",
                "freep/FreeP.App.Presentation.Tests/MathLayoutEngineTests.cs",
                "freep/FreeP.App.Host.Tests/SlideCanvasMathBaselineTests.cs",
                "freep/FreeP.App.Rendering.Avalonia.Tests/SlideCanvasMathBaselineTests.cs"
            ],
            RemainingWork: "Shared OMML parsing preserves m:boxPr/m:opEmu as renderer-neutral MathNode.Box metadata, and shared row layout gives boxed single-token and bounded multi-glyph relation operators deterministic operator-class spacing before WPF or Avalonia draw. PowerPoint-authoritative math visual baselines, exact OfficeMath spacing-table metrics, and broader operator-emulator line-break/alignment behavior remain deferred."),
        new(
            EvidenceId: "freep.omml.accent-bar-render-plan",
            Area: "OMML accent-bar render-plan semantics",
            Status: "shared-layout-evidence",
            HostCoverage: "WPF/Avalonia consume shared MathNode.Acc overbar metadata and MathBoxRenderPlanner horizontal-rule ops with no renderer-local accent-bar policy",
            EvidenceDocs:
            [
                "docs/parity/freep-omml-accent-bar-render-plan-2026-07-14.md"
            ],
            Verification:
            [
                "freep/FreeP.App.Presentation/Math/MathLayoutEngine.cs",
                "freep/FreeP.App.Presentation.Tests/OmmlParserTests.cs",
                "freep/FreeP.App.Presentation.Tests/MathLayoutEngineTests.cs",
                "freep/FreeP.App.Host.Tests/SlideCanvasMathBaselineTests.cs",
                "freep/FreeP.App.Rendering.Avalonia.Tests/SlideCanvasMathBaselineTests.cs"
            ],
            RemainingWork: "Shared OMML layout now maps PowerPoint-authored m:accPr/m:chr macron and overline accents to renderer-neutral horizontal-rule draw ops above the base expression before WPF or Avalonia draw. PowerPoint-authoritative math visual baselines, exact Cambria Math accent placement, stretched accent typography, and complete OfficeMath accent semantics remain deferred."),
        new(
            EvidenceId: "freep.omml.radical-degree-layout",
            Area: "OMML radical degree layout and baseline",
            Status: "shared-layout-evidence",
            HostCoverage: "WPF/Avalonia consume shared MathNode.Rad degree metadata and MathBoxRenderPlanner radical/glyph ops with no renderer-local radical-degree policy",
            EvidenceDocs:
            [
                "docs/parity/freep-omml-radical-degree-layout-2026-07-14.md"
            ],
            Verification:
            [
                "freep/FreeP.App.Presentation/Math/MathNode.cs",
                "freep/FreeP.App.Presentation/Math/OmmlParser.cs",
                "freep/FreeP.App.Presentation/Math/MathLayoutEngine.cs",
                "freep/FreeP.App.Presentation.Tests/OmmlParserTests.cs",
                "freep/FreeP.App.Presentation.Tests/MathLayoutEngineTests.cs",
                "freep/FreeP.App.Host.Tests/SlideCanvasMathBaselineTests.cs",
                "freep/FreeP.App.Rendering.Avalonia.Tests/SlideCanvasMathBaselineTests.cs"
            ],
            RemainingWork: "Shared OMML parsing and layout now proves default visible radical degrees, hidden m:radPr/m:degHide no-draw/no-gutter behavior, script-sized degree placement left of the radical sign, and paired WPF/Avalonia consumption of the same renderer-neutral radical/glyph draw ops. PowerPoint-authoritative math visual baselines, exact Cambria Math radical glyph metrics, radical-degree kerning, overline/check-mark shape tuning, and broader OfficeMath radical variants remain deferred."),
        new(
            EvidenceId: "freep.omml.fraction-type",
            Area: "OMML fraction type semantics",
            Status: "shared-layout-evidence",
            HostCoverage: "WPF/Avalonia consume shared MathNode.FracType layout and MathBoxRenderPlanner glyph/rule/line ops with no renderer-local fraction policy",
            EvidenceDocs:
            [
                "docs/parity/freep-omml-fraction-type-2026-07-14.md"
            ],
            Verification:
            [
                "freep/FreeP.App.Presentation/Math/MathNode.cs",
                "freep/FreeP.App.Presentation/Math/OmmlParser.cs",
                "freep/FreeP.App.Presentation/Math/MathLayoutEngine.cs",
                "freep/FreeP.App.Presentation.Tests/OmmlParserTests.cs",
                "freep/FreeP.App.Presentation.Tests/MathLayoutEngineTests.cs",
                "freep/FreeP.App.Host.Tests/SlideCanvasMathBaselineTests.cs",
                "freep/FreeP.App.Rendering.Avalonia.Tests/SlideCanvasMathBaselineTests.cs"
            ],
            RemainingWork: "Shared OMML parsing and layout preserve m:fPr/m:type default bar, noBar, lin, and skw variants, carrying stacked/no-rule, inline-slash, and diagonal-line draw plans to both WPF and Avalonia. PowerPoint-authoritative math visual baselines, exact Cambria Math fraction metrics, skewed slash-angle fidelity, and complete OfficeMath fraction typography remain deferred."),
        new(
            EvidenceId: "freep.omml.manual-break-alignment",
            Area: "OMML manual line-break alignment",
            Status: "shared-layout-evidence",
            HostCoverage: "WPF/Avalonia consume shared MathNode.EqArray rows created from m:brk and m:alnAt draw coordinates with no renderer-local line-break policy",
            EvidenceDocs:
            [
                "docs/parity/freep-omml-manual-breaks-2026-07-06.md"
            ],
            Verification:
            [
                "freep/FreeP.App.Presentation/Math/OmmlParser.cs",
                "freep/FreeP.App.Presentation/Math/MathLayoutEngine.cs",
                "freep/FreeP.App.Presentation.Tests/OmmlParserTests.cs",
                "freep/FreeP.App.Presentation.Tests/MathLayoutEngineTests.cs",
                "freep/FreeP.App.Host.Tests/SlideCanvasMathBaselineTests.cs",
                "freep/FreeP.App.Rendering.Avalonia.Tests/SlideCanvasMathBaselineTests.cs"
            ],
            RemainingWork: "Shared OMML parsing maps direct m:brk plus run and box break properties into equation-array rows, preserves m:alnAt alignment indices, and now proves the aligned draw coordinates through paired WPF/Avalonia host smoke tests. PowerPoint-authoritative math visual baselines, richer break-distribution heuristics, and full OfficeMath paragraph alignment remain deferred."),
        new(
            EvidenceId: "freep.omml.box-alignment-points",
            Area: "OMML boxed equation-array alignment points",
            Status: "shared-layout-evidence",
            HostCoverage: "WPF/Avalonia consume shared MathNode.EqArray alignment metadata and MathBoxRenderPlanner draw ops with no renderer-local math policy",
            EvidenceDocs:
            [
                "docs/parity/freep-omml-box-alignment-2026-07-14.md"
            ],
            Verification:
            [
                "freep/FreeP.App.Presentation.Tests/OmmlParserTests.cs",
                "freep/FreeP.App.Presentation.Tests/MathLayoutEngineTests.cs",
                "freep/FreeP.App.Host.Tests/SlideCanvasMathBaselineTests.cs",
                "freep/FreeP.App.Rendering.Avalonia.Tests/SlideCanvasMathBaselineTests.cs"
            ],
            RemainingWork: "Shared OMML parsing now treats m:boxPr/m:aln on direct m:eqArr row boxes as invisible equation-array alignment points while preserving the boxed expression. WPF and Avalonia consume the resulting shared MathBox draw coordinates. PowerPoint-authoritative math visual baselines and broader OfficeMath alignment semantics remain deferred."),
        new(
            EvidenceId: "freep.omml.eqarray-spacing-base-justification",
            Area: "OMML equation-array row spacing and base justification",
            Status: "shared-layout-evidence",
            HostCoverage: "WPF/Avalonia consume shared MathNode.EqArray spacing/base metadata and MathBoxRenderPlanner draw ops with no renderer-local equation-array policy",
            EvidenceDocs:
            [
                "docs/parity/freep-omml-eqarray-spacing-base-justification-2026-07-14.md"
            ],
            Verification:
            [
                "freep/FreeP.App.Presentation/Math/OmmlParser.cs",
                "freep/FreeP.App.Presentation/Math/MathLayoutEngine.cs",
                "freep/FreeP.App.Presentation.Tests/OmmlParserTests.cs",
                "freep/FreeP.App.Presentation.Tests/MathLayoutEngineTests.cs",
                "freep/FreeP.App.Host.Tests/SlideCanvasMathBaselineTests.cs",
                "freep/FreeP.App.Rendering.Avalonia.Tests/SlideCanvasMathBaselineTests.cs"
            ],
            RemainingWork: "Shared OMML parsing and layout preserve m:eqArrPr/m:rSpRule, m:rSp, and m:baseJc so row offsets and reported baseline/ascent are resolved before WPF or Avalonia draw. PowerPoint-authoritative math visual baselines, exact OfficeMath spacing metrics, and complete paragraph-level equation alignment remain deferred."),
        new(
            EvidenceId: "freep.omml.paragraph-justification",
            Area: "OMML paragraph-level equation justification",
            Status: "shared-layout-evidence",
            HostCoverage: "WPF/Avalonia consume shared MathNode.MathParagraph justification metadata and MathBoxRenderPlanner glyph coordinates with no renderer-local equation-paragraph policy",
            EvidenceDocs:
            [
                "docs/parity/freep-omml-paragraph-justification-2026-07-14.md"
            ],
            Verification:
            [
                "freep/FreeP.App.Presentation/Math/MathNode.cs",
                "freep/FreeP.App.Presentation/Math/OmmlParser.cs",
                "freep/FreeP.App.Presentation/Math/MathLayoutEngine.cs",
                "freep/FreeP.App.Presentation.Tests/OmmlParserTests.cs",
                "freep/FreeP.App.Presentation.Tests/MathLayoutEngineTests.cs",
                "freep/FreeP.App.Host.Tests/SlideCanvasMathBaselineTests.cs",
                "freep/FreeP.App.Rendering.Avalonia.Tests/SlideCanvasMathBaselineTests.cs"
            ],
            RemainingWork: "Shared OMML parsing and layout now preserve m:oMathParaPr/m:jc left, center, right, and centerGroup metadata and apply it to renderer-neutral glyph coordinates when an equation paragraph width is supplied. PowerPoint-authoritative math visual baselines, exact text-box/frame width integration, and full OfficeMath paragraph-distribution heuristics remain deferred."),
        new(
            EvidenceId: "freep.omml.delimiter-shape",
            Area: "OMML delimiter shape semantics",
            Status: "shared-layout-evidence",
            HostCoverage: "WPF/Avalonia consume shared MathNode.Delim shape metadata and MathBoxRenderPlanner bracket height with no renderer-local delimiter policy",
            EvidenceDocs:
            [
                "docs/parity/freep-omml-delimiter-shape-2026-07-14.md"
            ],
            Verification:
            [
                "freep/FreeP.App.Presentation/Math/MathNode.cs",
                "freep/FreeP.App.Presentation/Math/OmmlParser.cs",
                "freep/FreeP.App.Presentation/Math/MathLayoutEngine.cs",
                "freep/FreeP.App.Presentation.Tests/OmmlParserTests.cs",
                "freep/FreeP.App.Presentation.Tests/MathLayoutEngineTests.cs",
                "freep/FreeP.App.Host.Tests/SlideCanvasMathBaselineTests.cs",
                "freep/FreeP.App.Rendering.Avalonia.Tests/SlideCanvasMathBaselineTests.cs"
            ],
            RemainingWork: "Shared OMML parsing and layout now preserve m:dPr/m:shp centered vs match semantics, keeping centered delimiter glyphs at ordinary bracket height while the tall inner expression still drives shared container height and baseline. PowerPoint-authoritative math visual baselines, exact delimiter glyph metrics, and complete OfficeMath delimiter typography remain deferred."),
        new(
            EvidenceId: "freep.omml.delimiter-separator",
            Area: "OMML delimiter separator semantics",
            Status: "shared-layout-evidence",
            HostCoverage: "WPF/Avalonia consume shared MathNode.Delim separator metadata and MathBoxRenderPlanner glyph coordinates with no renderer-local delimiter policy",
            EvidenceDocs:
            [
                "docs/parity/freep-omml-delimiter-separator-2026-07-14.md"
            ],
            Verification:
            [
                "freep/FreeP.App.Presentation/Math/MathNode.cs",
                "freep/FreeP.App.Presentation/Math/OmmlParser.cs",
                "freep/FreeP.App.Presentation/Math/MathLayoutEngine.cs",
                "freep/FreeP.App.Presentation.Tests/OmmlParserTests.cs",
                "freep/FreeP.App.Presentation.Tests/MathLayoutEngineTests.cs",
                "freep/FreeP.App.Host.Tests/SlideCanvasMathBaselineTests.cs",
                "freep/FreeP.App.Rendering.Avalonia.Tests/SlideCanvasMathBaselineTests.cs"
            ],
            RemainingWork: "Shared OMML parsing and layout now preserve m:dPr/m:sepChr default comma, custom separator glyphs, and explicit-empty separator suppression for multi-element delimiters, carrying renderer-neutral glyph coordinates to WPF and Avalonia. PowerPoint-authoritative math visual baselines, exact Cambria Math separator spacing, and complete OfficeMath delimiter typography remain deferred."),
        new(
            EvidenceId: "freep.omml.groupchr-vertical-justification",
            Area: "OMML group-character vertical justification",
            Status: "shared-layout-evidence",
            HostCoverage: "WPF/Avalonia consume shared MathNode.GroupChr vertical-justification metadata and MathBox baseline metrics with no renderer-local group-character policy",
            EvidenceDocs:
            [
                "docs/parity/freep-omml-groupchr-vertical-justification-2026-07-14.md"
            ],
            Verification:
            [
                "freep/FreeP.App.Presentation/Math/MathNode.cs",
                "freep/FreeP.App.Presentation/Math/OmmlParser.cs",
                "freep/FreeP.App.Presentation/Math/MathLayoutEngine.cs",
                "freep/FreeP.App.Presentation.Tests/OmmlParserTests.cs",
                "freep/FreeP.App.Presentation.Tests/MathLayoutEngineTests.cs",
                "freep/FreeP.App.Host.Tests/SlideCanvasMathBaselineTests.cs",
                "freep/FreeP.App.Rendering.Avalonia.Tests/SlideCanvasMathBaselineTests.cs"
            ],
            RemainingWork: "Shared OMML parsing now preserves m:groupChrPr/m:vertJc top vs bottom baseline alignment, including the bare-element default to bottom, and shared layout maps it to renderer-neutral MathBox ascent metrics consumed by both WPF and Avalonia. PowerPoint-authoritative math visual baselines, exact stretched group-character glyph metrics, and complete OfficeMath group-character typography remain deferred."),
        new(
            EvidenceId: "freep.omml.pre-subsup-layout",
            Area: "OMML pre-sub/superscript layout",
            Status: "shared-layout-evidence",
            HostCoverage: "WPF/Avalonia consume shared MathNode.PreSubSup script-stack coordinates and MathBoxRenderPlanner draw ops with no renderer-local math policy",
            EvidenceDocs:
            [
                "docs/parity/freep-omml-pre-subsup-2026-07-14.md"
            ],
            Verification:
            [
                "freep/FreeP.App.Presentation.Tests/OmmlParserTests.cs",
                "freep/FreeP.App.Presentation.Tests/MathLayoutEngineTests.cs",
                "freep/FreeP.App.Host.Tests/SlideCanvasMathBaselineTests.cs",
                "freep/FreeP.App.Rendering.Avalonia.Tests/SlideCanvasMathBaselineTests.cs"
            ],
            RemainingWork: "Shared OMML parsing and layout now preserve m:sPre base, pre-subscript, and pre-superscript children, place the reduced script stack to the left of the base, and carry parsed draw-plan coordinates to both hosts. PowerPoint-authoritative math visual baselines and exact OfficeMath script metrics remain deferred."),
        new(
            EvidenceId: "freep.omml.script-align-argument-size",
            Area: "OMML script alignment and argument size",
            Status: "shared-layout-evidence",
            HostCoverage: "WPF/Avalonia consume shared MathNode.SubSup alignment and MathNode.ArgSize glyph plans with no renderer-local script or argument-size policy",
            EvidenceDocs:
            [
                "docs/parity/freep-omml-script-align-argsize-2026-07-14.md"
            ],
            Verification:
            [
                "freep/FreeP.App.Presentation.Tests/OmmlParserTests.cs",
                "freep/FreeP.App.Presentation.Tests/MathLayoutEngineTests.cs",
                "freep/FreeP.App.Host.Tests/SlideCanvasMathBaselineTests.cs",
                "freep/FreeP.App.Rendering.Avalonia.Tests/SlideCanvasMathBaselineTests.cs"
            ],
            RemainingWork: "Shared OMML parsing and layout now preserve PowerPoint-authored m:sSubSupPr/m:alnScr script-column alignment and m:argPr/m:argSz argument script-size adjustments, carrying renderer-neutral glyph coordinates and font-size metadata to both WPF and Avalonia. PowerPoint-authoritative math visual baselines, exact Cambria Math script metrics, and broader OfficeMath script-spacing table parity remain deferred."),
        new(
            EvidenceId: "freep.omml.matrix-spacing-base-justification",
            Area: "OMML matrix spacing and base justification",
            Status: "shared-layout-evidence",
            HostCoverage: "WPF/Avalonia consume shared MathNode.Matrix spacing/base metadata and MathBoxRenderPlanner glyph coordinates with no renderer-local matrix policy",
            EvidenceDocs:
            [
                "docs/parity/freep-omml-matrix-spacing-2026-07-06.md"
            ],
            Verification:
            [
                "freep/FreeP.App.Presentation/Math/OmmlParser.cs",
                "freep/FreeP.App.Presentation/Math/MathLayoutEngine.cs",
                "freep/FreeP.App.Presentation.Tests/OmmlParserTests.cs",
                "freep/FreeP.App.Presentation.Tests/MathLayoutEngineTests.cs",
                "freep/FreeP.App.Host.Tests/SlideCanvasMathBaselineTests.cs",
                "freep/FreeP.App.Rendering.Avalonia.Tests/SlideCanvasMathBaselineTests.cs"
            ],
            RemainingWork: "Shared OMML parsing and layout preserve m:mPr/m:baseJc, m:rSpRule, m:rSp, m:cGpRule, m:cGp, and m:cSp so row offsets, column gaps, minimum column widths, and reported baseline/ascent are resolved before WPF or Avalonia draw. PowerPoint-authoritative matrix visual baselines, exact OfficeMath spacing metrics, and broader matrix typography remain deferred."),
        new(
            EvidenceId: "freep.omml.matrix-placeholder",
            Area: "OMML matrix empty-cell placeholder handling",
            Status: "shared-layout-evidence",
            HostCoverage: "WPF/Avalonia consume shared MathNode.Matrix HidePlaceholders state and MathBoxRenderPlanner glyph ops with no renderer-local matrix placeholder policy",
            EvidenceDocs:
            [
                "docs/parity/freep-omml-matrix-placeholder-2026-07-14.md"
            ],
            Verification:
            [
                "freep/FreeP.App.Presentation/Math/MathNode.cs",
                "freep/FreeP.App.Presentation/Math/OmmlParser.cs",
                "freep/FreeP.App.Presentation/Math/MathLayoutEngine.cs",
                "freep/FreeP.App.Presentation.Tests/OmmlParserTests.cs",
                "freep/FreeP.App.Presentation.Tests/MathLayoutEngineTests.cs",
                "freep/FreeP.App.Host.Tests/SlideCanvasMathBaselineTests.cs",
                "freep/FreeP.App.Rendering.Avalonia.Tests/SlideCanvasMathBaselineTests.cs"
            ],
            RemainingWork: "Shared OMML parsing and layout now preserve m:mPr/m:plcHide, emit renderer-neutral placeholders for authored empty matrix cells by default, and suppress those placeholders when plcHide is set. PowerPoint-authoritative math visual baselines, exact OfficeMath placeholder chrome, full spacing-table typography, additional equation constructs, and remaining alignment semantics remain deferred."),
        new(
            EvidenceId: "freep.omml.matrix-column-count-alignment",
            Area: "OMML matrix column alignment repeat counts",
            Status: "shared-layout-evidence",
            HostCoverage: "WPF/Avalonia consume shared MathNode.Matrix repeated column-alignment metadata and MathBoxRenderPlanner glyph coordinates with no renderer-local matrix policy",
            EvidenceDocs:
            [
                "docs/parity/freep-omml-matrix-column-count-alignment-2026-07-14.md"
            ],
            Verification:
            [
                "freep/FreeP.App.Presentation/Math/OmmlParser.cs",
                "freep/FreeP.App.Presentation.Tests/OmmlParserTests.cs",
                "freep/FreeP.App.Presentation.Tests/MathLayoutEngineTests.cs",
                "freep/FreeP.App.Host.Tests/SlideCanvasMathBaselineTests.cs",
                "freep/FreeP.App.Rendering.Avalonia.Tests/SlideCanvasMathBaselineTests.cs"
            ],
            RemainingWork: "Shared OMML parsing now expands m:mPr/m:mcs/m:mc/m:mcPr/m:count into repeated matrix column-alignment metadata, so counted left/center/right column policies affect renderer-neutral glyph coordinates before WPF or Avalonia draws. PowerPoint-authoritative matrix visual baselines, exact OfficeMath column metrics, and broader matrix spacing semantics remain deferred."),
        new(
            EvidenceId: "freep.omml.literal-run-style",
            Area: "OMML literal math run style",
            Status: "shared-layout-evidence",
            HostCoverage: "WPF/Avalonia consume shared MathNode.Run literal metadata and MathBoxRenderPlanner glyph style with no renderer-local literal policy",
            EvidenceDocs:
            [
                "docs/parity/freep-omml-literal-run-style-2026-07-14.md"
            ],
            Verification:
            [
                "freep/FreeP.App.Presentation/Math/MathNode.cs",
                "freep/FreeP.App.Presentation/Math/OmmlParser.cs",
                "freep/FreeP.App.Presentation.Tests/OmmlParserTests.cs",
                "freep/FreeP.App.Presentation.Tests/MathLayoutEngineTests.cs",
                "freep/FreeP.App.Host.Tests/SlideCanvasMathBaselineTests.cs",
                "freep/FreeP.App.Rendering.Avalonia.Tests/SlideCanvasMathBaselineTests.cs"
            ],
            RemainingWork: "Shared OMML parsing now preserves m:rPr/m:lit as literal run metadata and resolves the bounded no-style literal case to an upright renderer-neutral glyph plan consumed by WPF and Avalonia. PowerPoint-authoritative math visual baselines, full OfficeMath linear-build-up semantics, and exact Cambria Math typography remain deferred."),
        new(
            EvidenceId: "freep.omml.math-alphabet-style",
            Area: "OMML styled math alphabet glyphs",
            Status: "shared-layout-evidence",
            HostCoverage: "WPF/Avalonia consume shared MathNode.Run alphabet/style metadata and MathBoxRenderPlanner Unicode glyph ops with no renderer-local math alphabet policy",
            EvidenceDocs:
            [
                "docs/parity/freep-omml-math-alphabet-style-2026-07-14.md"
            ],
            Verification:
            [
                "freep/FreeP.App.Presentation/Math/MathNode.cs",
                "freep/FreeP.App.Presentation/Math/OmmlParser.cs",
                "freep/FreeP.App.Presentation/Math/MathLayoutEngine.cs",
                "freep/FreeP.App.Presentation.Tests/OmmlParserTests.cs",
                "freep/FreeP.App.Presentation.Tests/MathLayoutEngineTests.cs",
                "freep/FreeP.App.Host.Tests/SlideCanvasMathBaselineTests.cs",
                "freep/FreeP.App.Rendering.Avalonia.Tests/SlideCanvasMathBaselineTests.cs"
            ],
            RemainingWork: "Shared OMML parsing and layout now preserve m:rPr/m:scr alphabet metadata plus m:rPr/m:sty bold/italic requests, resolving supported styled alphabet combinations to renderer-neutral mathematical Unicode glyphs consumed by WPF and Avalonia. PowerPoint-authoritative math visual baselines, exact Cambria Math metrics, complete mathematical alphabet coverage, and broader OfficeMath typography remain deferred."),
        new(
            EvidenceId: "freep.omml.math-font",
            Area: "OMML equation-wide math font semantics",
            Status: "shared-layout-evidence",
            HostCoverage: "WPF/Avalonia consume shared MathNode.MathParagraph math-font metadata and MathBoxRenderPlanner glyph font families with no renderer-local font policy",
            EvidenceDocs:
            [
                "docs/parity/freep-omml-math-font-20260801.md"
            ],
            Verification:
            [
                "freep/FreeP.App.Presentation/Math/MathNode.cs",
                "freep/FreeP.App.Presentation/Math/OmmlParser.cs",
                "freep/FreeP.App.Presentation/Math/MathLayoutEngine.cs",
                "freep/FreeP.App.Presentation.Tests/OmmlParserTests.cs",
                "freep/FreeP.App.Presentation.Tests/MathLayoutEngineTests.cs",
                "freep/FreeP.App.Host.Tests/SlideCanvasMathBaselineTests.cs",
                "freep/FreeP.App.Rendering.Avalonia.Tests/SlideCanvasMathBaselineTests.cs"
            ],
            RemainingWork: "Shared OMML parsing and layout now preserve non-empty m:mathPr/m:mathFont values and emit the selected family on renderer-neutral glyphs before WPF or Avalonia draw. PowerPoint-authoritative font fallback and exact Cambria Math metrics remain deferred."),
        new(
            EvidenceId: "freep.omml.math-default-inheritance",
            Area: "OMML document-level mathPr inheritance",
            Status: "shared-parser-layout-evidence",
            HostCoverage: "WPF/Avalonia consume shared inherited MathNode.MathProperties and MathRoot/MathParagraph layout metadata with no renderer-local math-property policy",
            EvidenceDocs:
            [
                "docs/parity/freep-omml-math-default-inheritance-wave100-20260801.md"
            ],
            Verification:
            [
                "freep/FreeP.Core.Model/OmmlMathProperties.cs",
                "freep/FreeP.Core.Model/MathRunInfo.cs",
                "freep/FreeP.Core.Model/Presentation.cs",
                "freep/FreeP.Core.IO/PptxPackageReader.cs",
                "freep/FreeP.App.Presentation/SlideCompositor.cs",
                "freep/FreeP.App.Presentation/Math/MathNode.cs",
                "freep/FreeP.App.Presentation/Math/OmmlParser.cs",
                "freep/FreeP.App.Presentation/Math/MathLayoutEngine.cs",
                "freep/FreeP.App.Presentation.Tests/OmmlMathDefaultsIntegrationTests.cs",
                "freep/FreeP.App.Presentation.Tests/OmmlParserTests.cs",
                "freep/FreeP.App.Presentation.Tests/MathLayoutEngineTests.cs",
                "freep/FreeP.App.Host.Tests/OmmlMathDefaultsParityTests.cs",
                "freep/FreeP.App.Host.Tests/SlideCanvasMathBaselineTests.cs",
                "freep/FreeP.App.Rendering.Avalonia.Tests/OmmlMathDefaultsParityTests.cs",
                "freep/FreeP.App.Rendering.Avalonia.Tests/SlideCanvasMathBaselineTests.cs"
            ],
            RemainingWork: "FreeP now propagates authored defaults from a related PresentationML settings part when one exists, then containing a:graphicData/m:mathPr, raw math-wrapper m:mathPr, paragraph-local m:mathPr, and local oMathParaPr properties by property-wise precedence. Standard PowerPoint packages expose no document settings source in the current corpus, so the package level remains null rather than fabricating defaults. PowerPoint-authoritative font fallback, exact Cambria Math metrics, and broader authored settings-part corpus baselines remain deferred."),
        new(
            EvidenceId: "freep.omml.nary-limit-location",
            Area: "OMML n-ary limit-location semantics",
            Status: "shared-layout-evidence",
            HostCoverage: "WPF/Avalonia consume shared MathNode.Nary limit-location metadata and MathBoxRenderPlanner glyph coordinates with no renderer-local n-ary policy",
            EvidenceDocs:
            [
                "docs/parity/freep-omml-nary-limit-location-2026-07-14.md"
            ],
            Verification:
            [
                "freep/FreeP.App.Presentation/Math/MathNode.cs",
                "freep/FreeP.App.Presentation/Math/OmmlParser.cs",
                "freep/FreeP.App.Presentation/Math/MathLayoutEngine.cs",
                "freep/FreeP.App.Presentation.Tests/OmmlParserTests.cs",
                "freep/FreeP.App.Presentation.Tests/MathLayoutEngineTests.cs",
                "freep/FreeP.App.Host.Tests/SlideCanvasMathBaselineTests.cs",
                "freep/FreeP.App.Rendering.Avalonia.Tests/SlideCanvasMathBaselineTests.cs"
            ],
            RemainingWork: "Shared OMML parsing and layout preserve m:naryPr/m:limLoc default subSup versus explicit undOvr placement, carrying distinct side-script and under/over glyph coordinates to both hosts. PowerPoint-authoritative math visual baselines, exact operator metrics, and complete OfficeMath display-style heuristics remain deferred."),
        new(
            EvidenceId: "freep.omml.nary-grow-hidden-limits",
            Area: "OMML n-ary grow and hidden-limit semantics",
            Status: "shared-layout-evidence",
            HostCoverage: "WPF/Avalonia consume shared MathNode.Nary grow metadata and hidden-limit suppression through MathBoxRenderPlanner glyph coordinates with no renderer-local n-ary policy",
            EvidenceDocs:
            [
                "docs/parity/freep-omml-nary-grow-hidden-limits-2026-07-14.md"
            ],
            Verification:
            [
                "freep/FreeP.App.Presentation/Math/MathNode.cs",
                "freep/FreeP.App.Presentation/Math/OmmlParser.cs",
                "freep/FreeP.App.Presentation/Math/MathLayoutEngine.cs",
                "freep/FreeP.App.Presentation.Tests/OmmlParserTests.cs",
                "freep/FreeP.App.Presentation.Tests/MathLayoutEngineTests.cs",
                "freep/FreeP.App.Host.Tests/SlideCanvasMathBaselineTests.cs",
                "freep/FreeP.App.Rendering.Avalonia.Tests/SlideCanvasMathBaselineTests.cs"
            ],
            RemainingWork: "Shared OMML parsing and layout now prove m:naryPr/m:grow remains active after m:subHide and m:supHide suppress authored lower/upper limits, carrying only the grown operator and operand glyphs to WPF and Avalonia. PowerPoint-authoritative math visual baselines, exact Cambria Math n-ary operator metrics, and complete OfficeMath display-style heuristics remain deferred."),
        new(
            EvidenceId: "freep.omml.limit-placement",
            Area: "OMML lower and upper limit placement",
            Status: "shared-layout-evidence",
            HostCoverage: "WPF/Avalonia consume shared MathNode.Limit baseline metrics and MathBoxRenderPlanner glyph coordinates with no renderer-local limit-placement policy",
            EvidenceDocs:
            [
                "docs/parity/freep-omml-limit-placement-2026-07-14.md"
            ],
            Verification:
            [
                "freep/FreeP.App.Presentation/Math/MathNode.cs",
                "freep/FreeP.App.Presentation/Math/OmmlParser.cs",
                "freep/FreeP.App.Presentation/Math/MathLayoutEngine.cs",
                "freep/FreeP.App.Presentation.Tests/OmmlParserTests.cs",
                "freep/FreeP.App.Presentation.Tests/MathLayoutEngineTests.cs",
                "freep/FreeP.App.Host.Tests/SlideCanvasMathBaselineTests.cs",
                "freep/FreeP.App.Rendering.Avalonia.Tests/SlideCanvasMathBaselineTests.cs"
            ],
            RemainingWork: "Shared OMML parsing and layout preserve m:limLow and m:limUpp as centered reduced-size lower/upper limits while keeping the base expression baseline stable. WPF and Avalonia consume the same renderer-neutral glyph coordinates. PowerPoint-authoritative math visual baselines, exact Cambria Math limit metrics, and complete OfficeMath display-style heuristics remain deferred."),
        new(
            EvidenceId: "freep.omml.scripted-function-name",
            Area: "OMML scripted function-name semantics",
            Status: "shared-layout-evidence",
            HostCoverage: "WPF/Avalonia consume shared MathNode.Func names with nested script/limit wrappers and MathBoxRenderPlanner glyph style with no renderer-local function policy",
            EvidenceDocs:
            [
                "docs/parity/freep-omml-scripted-function-name-2026-07-14.md"
            ],
            Verification:
            [
                "freep/FreeP.App.Presentation/Math/OmmlParser.cs",
                "freep/FreeP.App.Presentation.Tests/OmmlParserTests.cs",
                "freep/FreeP.App.Presentation.Tests/MathLayoutEngineTests.cs",
                "freep/FreeP.App.Host.Tests/SlideCanvasMathBaselineTests.cs",
                "freep/FreeP.App.Rendering.Avalonia.Tests/SlideCanvasMathBaselineTests.cs"
            ],
            RemainingWork: "Shared OMML parsing now normalizes scripted and limited m:func/m:fName bases such as sin^2 and lim to upright function operators while preserving ordinary math styling for the applied argument. PowerPoint-authoritative math visual baselines, exact Cambria Math function spacing, and complete OfficeMath function-name typography remain deferred."),
        new(
            EvidenceId: "freep.omml.border-box-side-strike-lines",
            Area: "OMML border-box side and strike-line semantics",
            Status: "shared-layout-evidence",
            HostCoverage: "WPF/Avalonia consume shared MathNode.BorderBox side and strike metadata plus MathBoxRenderPlanner line ops with no renderer-local border-box policy",
            EvidenceDocs:
            [
                "docs/parity/freep-omml-border-box-side-strike-lines-2026-07-14.md"
            ],
            Verification:
            [
                "freep/FreeP.App.Presentation/Math/MathNode.cs",
                "freep/FreeP.App.Presentation/Math/OmmlParser.cs",
                "freep/FreeP.App.Presentation/Math/MathLayoutEngine.cs",
                "freep/FreeP.App.Presentation.Tests/OmmlParserTests.cs",
                "freep/FreeP.App.Presentation.Tests/MathLayoutEngineTests.cs",
                "freep/FreeP.App.Host.Tests/SlideCanvasMathBaselineTests.cs",
                "freep/FreeP.App.Rendering.Avalonia.Tests/SlideCanvasMathBaselineTests.cs"
            ],
            RemainingWork: "Shared OMML parsing preserves m:borderBoxPr hidden side flags plus horizontal, vertical, and diagonal strike flags, and shared layout emits renderer-neutral border/strike line operations around the padded child box before either host draws. PowerPoint-authoritative math visual baselines, exact OfficeMath border padding/thickness metrics, and full math-box typography remain deferred."),
        new(
            EvidenceId: "freep.smartart.continuous-block-process",
            Area: "SmartArt continuous block process live layout",
            Status: "shared-render-planner-evidence",
            HostCoverage: "WPF/Avalonia consume the same dedicated shared rounded-block and ordered-connector ops emitted by the SmartArt layout planner; no renderer-local SmartArt policy",
            EvidenceDocs:
            [
                "docs/parity/freep-smartart-continuous-block-process-2026-07-06.md",
                "docs/parity/freep-smartart-continuous-block-process-wave112-20260802.md"
            ],
            Verification:
            [
                "freep/FreeP.App.Presentation.Tests/SmartArtLayoutTests.cs",
                "freep/FreeP.App.Presentation.Tests/SmartArtEditingPlannerTests.cs",
                "freep/FreeP.App.Presentation.Tests/PptxRepairCorpusValidityTests.cs",
                "freep/FreeP.App.Host.Tests/SmartArtTests.cs"
            ],
            RemainingWork: "continuousBlockProcess now uses dedicated shared compact rounded-block geometry with stable block/connector roles, regenerated drawing-cache evidence, and schema-valid PPTX round-trip coverage. Unsupported process variants still fall back to cached drawing; exact PowerPoint spacing/effects, PowerPoint-authoritative visual baselines, and broader SmartArt authoring remain deferred."),
        new(
            EvidenceId: "freep.smartart.grouped-list-import-bands",
            Area: "SmartArt grouped-list imported band-cache live layout",
            Status: "shared-render-planner-evidence",
            HostCoverage: "WPF/Avalonia consume the same shared grouped-list band, header, and child shape plan through SlideCompositor; no renderer-local SmartArt policy",
            EvidenceDocs:
            [
                "docs/parity/avalonia-parity-wave132-freep-smartart-20260803.md"
            ],
            Verification:
            [
                "freep/FreeP.Core.IO/PptxPackageReader.cs",
                "freep/FreeP.App.Presentation/SmartArtLayoutEngine.cs",
                "freep/FreeP.App.Presentation/SlideCompositor.cs",
                "freep/FreeP.App.Host.Tests/SmartArtTests.cs",
                "freep/FreeP.App.Presentation.Tests/SmartArtLayoutTests.cs",
                "freep/FreeP.App.Host.Tests/SmartArtDefaultLiveRendererContractTests.cs",
                "freep/FreeP.App.Rendering.Avalonia.Tests/SmartArtDefaultLiveRendererContractTests.cs",
                "tools/FreeP.RenderCompare.Tests/SmartArtFixtureEvidenceTests.cs"
            ],
            RemainingWork: "The real fixture-backed grouped-list cache grammar with two distinct empty body bands, two headers, and four child boxes is admitted to the shared live plan. Missing, duplicate, ambiguous, extra, effect-bearing, picture-bearing, or otherwise unproven grouped-list roles remain on cached drawing fallback; broader grouped-list role parity, exact PowerPoint geometry/effects, and authoritative visual baselines remain deferred."),
        new(
            EvidenceId: "freep.smartart.relationship1-import-ellipses",
            Area: "SmartArt relationship1 imported node-ellipse live layout",
            Status: "shared-render-planner-evidence",
            HostCoverage: "WPF/Avalonia consume the same shared relationship1 overlapping-ellipse plan through SlideCompositor; no renderer-local SmartArt policy",
            EvidenceDocs:
            [
                "docs/parity/avalonia-parity-wave133-freep-smartart-20260803.md"
            ],
            Verification:
            [
                "freep/FreeP.Core.IO/PptxPackageReader.cs",
                "freep/FreeP.App.Presentation/SmartArtLayoutEngine.cs",
                "freep/FreeP.App.Presentation/SlideCompositor.cs",
                "freep/FreeP.App.Host.Tests/SmartArtTests.cs",
                "freep/FreeP.App.Presentation.Tests/SmartArtLayoutTests.cs",
                "freep/FreeP.App.Host.Tests/SmartArtDefaultLiveRendererContractTests.cs",
                "freep/FreeP.App.Rendering.Avalonia.Tests/SmartArtDefaultLiveRendererContractTests.cs",
                "tools/FreeP.RenderCompare.Tests/SmartArtFixtureEvidenceTests.cs"
            ],
            RemainingWork: "The checked-in relationship1 package admits exactly three ordered, same-sized ellipse nodes with distinct matching text, a horizontal step within 1 EMU of the shared planner's truncated 58% diameter step, and no extra roles or effects. Missing, duplicate, ambiguous, non-ellipse, wrong-ratio, non-overlapping, effect-bearing, picture-bearing, or otherwise unproven relationship caches remain on cached drawing fallback; other relationship families and exact PowerPoint intersection/effect fidelity remain deferred."),
        new(
            EvidenceId: "freep.smartart.grid-matrix-import-cells",
            Area: "SmartArt gridMatrix imported four-cell live layout",
            Status: "shared-render-planner-evidence",
            HostCoverage: "WPF/Avalonia consume the same shared Grid Matrix four-cell plan through SlideCompositor; no renderer-local SmartArt policy",
            EvidenceDocs:
            [
                "docs/parity/avalonia-parity-wave134-freep-smartart-depth-20260804.md"
            ],
            Verification:
            [
                "freep/FreeP.Core.IO/PptxPackageReader.cs",
                "freep/FreeP.App.Presentation/SmartArtLayoutEngine.cs",
                "freep/FreeP.App.Presentation/SlideCompositor.cs",
                "freep/FreeP.App.Host.Tests/SmartArtTests.cs",
                "freep/FreeP.App.Presentation.Tests/SmartArtLayoutTests.cs",
                "freep/FreeP.App.Host.Tests/SmartArtDefaultLiveRendererContractTests.cs",
                "freep/FreeP.App.Rendering.Avalonia.Tests/SmartArtDefaultLiveRendererContractTests.cs",
                "tools/FreeP.RenderCompare.Tests/SmartArtFixtureEvidenceTests.cs",
                "tools/FreeP.RenderCompare/SmartArtFixtureGenerator.cs"
            ],
            RemainingWork: "The deterministic gridMatrix package admits only four ordered, distinct, non-empty rectangle nodes with equal square cells and the shared planner's 2.5% gap signature, without unsupported effects or extra roles. Missing, duplicate, ambiguous, non-square, wrongly spaced, effect-bearing, picture-bearing, or otherwise unproven Grid Matrix caches remain on cached drawing fallback; exact PowerPoint cell metrics, effects, text fitting, and wider matrix-family import parity remain deferred."),
        new(
            EvidenceId: "freep.smartart.increasing-circle-process-import-growth",
            Area: "SmartArt increasingCircleProcess imported growing-ellipse live layout",
            Status: "shared-render-planner-evidence",
            HostCoverage: "WPF/Avalonia consume the same shared increasingCircleProcess ellipse-and-line plan through SlideCompositor; no renderer-local SmartArt policy",
            EvidenceDocs:
            [
                "docs/parity/avalonia-parity-wave135-freep-smartart-depth-20260804.md"
            ],
            Verification:
            [
                "freep/FreeP.Core.IO/PptxPackageReader.cs",
                "freep/FreeP.App.Presentation/SmartArtLayoutEngine.cs",
                "freep/FreeP.App.Presentation/SlideCompositor.cs",
                "freep/FreeP.App.Host.Tests/SmartArtTests.cs",
                "freep/FreeP.App.Presentation.Tests/PptxRepairCorpusValidityTests.cs",
                "freep/FreeP.App.Host.Tests/SmartArtDefaultLiveRendererContractTests.cs",
                "freep/FreeP.App.Rendering.Avalonia.Tests/SmartArtDefaultLiveRendererContractTests.cs",
                "freep/FreeP.App.Avalonia.Tests/MainWindowHeadlessTests.cs",
                "tools/FreeP.RenderCompare.Tests/SmartArtFixtureEvidenceTests.cs",
                "tools/FreeP.RenderCompare/SmartArtFixtureGenerator.cs"
            ],
            RemainingWork: "The checked-in fixture admits only four ordered distinct non-empty ellipse nodes with strictly growing square diameters on one baseline, equal positive gaps, and three empty line roles, without unsupported effects or extra roles. Malformed, ambiguous, effectful, picture-bearing, richer PowerPoint background/chord/rectangle, or otherwise unproven increasingCircleProcess caches remain on cached drawing fallback; exact PowerPoint role geometry, effects, text fitting, and broader process-family import parity remain deferred."),
        new(
            EvidenceId: "freep.smartart.vertical-arrow-list-import-slots",
            Area: "SmartArt verticalArrowList imported four-slot live layout",
            Status: "shared-render-planner-evidence",
            HostCoverage: "WPF/Avalonia consume the same shared verticalArrowList down-arrow plan through SlideCompositor; no renderer-local SmartArt policy",
            EvidenceDocs:
            [
                "docs/parity/avalonia-parity-wave136-freep-smartart-vertical-arrow-list-20260804.md"
            ],
            Verification:
            [
                "freep/FreeP.Core.IO/PptxPackageReader.cs",
                "freep/FreeP.App.Presentation/SmartArtLayoutEngine.cs",
                "freep/FreeP.App.Presentation.Tests/SmartArtLayoutTests.cs",
                "freep/FreeP.App.Host.Tests/SmartArtTests.cs",
                "freep/FreeP.App.Host.Tests/SmartArtDefaultLiveRendererContractTests.cs",
                "freep/FreeP.App.Rendering.Avalonia.Tests/SmartArtDefaultLiveRendererContractTests.cs",
                "tools/FreeP.RenderCompare.Tests/SmartArtFixtureEvidenceTests.cs",
                "tools/FreeP.RenderCompare/SmartArtFixtureGenerator.cs"
            ],
            RemainingWork: "The deterministic fixture admits only four distinct non-empty flat nodes with four effect-free DownArrow cache shapes in exact shared-planner slot geometry. Richer, malformed, effect-bearing, picture-bearing, reordered, differently spaced, or otherwise unproven verticalArrowList caches remain on cached drawing fallback; exact PowerPoint arrow contours, text fitting, effects, and larger imported variants remain deferred."),
        new(
            EvidenceId: "freep.smartart.process1-import-node-connectors",
            Area: "SmartArt process1 imported five-stage node-and-connector live layout",
            Status: "shared-render-planner-evidence",
            HostCoverage: "WPF/Avalonia consume the same shared process1 node and connector plan through SlideCompositor; no renderer-local SmartArt policy",
            EvidenceDocs:
            [
                "docs/parity/avalonia-parity-wave137-freep-smartart-process1-20260804.md"
            ],
            Verification:
            [
                "freep/FreeP.Core.IO/PptxPackageReader.cs",
                "freep/FreeP.App.Presentation/SmartArtLayoutEngine.cs",
                "freep/FreeP.App.Presentation/SlideCompositor.cs",
                "freep/FreeP.App.Host.Tests/PptxPackageReaderSourceTests.cs",
                "freep/FreeP.App.Host.Tests/SmartArtTests.cs",
                "freep/FreeP.App.Host.Tests/SmartArtDefaultLiveRendererContractTests.cs",
                "freep/FreeP.App.Rendering.Avalonia.Tests/SmartArtDefaultLiveRendererContractTests.cs",
                "tools/FreeP.RenderCompare.Tests/SmartArtFixtureEvidenceTests.cs",
                "tools/FreeP.RenderCompare/SmartArtFixtureGenerator.cs"
            ],
            RemainingWork: "The deterministic process1 fixture admits only one five-node ordered chain with five effect-free rounded node boxes and four empty line connectors at the exact shared-plan slots. Changed geometry, reordered or mismatched text, effects, pictures, extra roles, missing cache parts, richer process caches, and other unproven process variants remain on cached drawing fallback; exact PowerPoint geometry, text fitting, effects, and broader process-family import parity remain deferred."),
        new(
            EvidenceId: "freep.smartart.basic-process",
            Area: "SmartArt basic process live layout",
            Status: "shared-render-planner-evidence",
            HostCoverage: "WPF/Avalonia consume ordinary shared slide shape and connector ops emitted by the SmartArt layout planner; no renderer-local SmartArt policy",
            EvidenceDocs:
            [
                "docs/parity/freep-smartart-basic-process-2026-07-06.md"
            ],
            Verification:
            [
                "freep/FreeP.App.Presentation.Tests/SmartArtLayoutTests.cs",
                "freep/FreeP.App.Host.Tests/SmartArtTests.cs"
            ],
            RemainingWork: "basicProcess now uses the bounded shared process live-layout path for parsed nodes while other unsupported process variants still fall back to cached drawing. Broader SmartArt geometry families, PowerPoint-authoritative visual baselines, and SmartArt authoring/editing remain deferred."),
        new(
            EvidenceId: "freep.smartart.basic-timeline",
            Area: "SmartArt basic timeline live layout",
            Status: "shared-render-planner-evidence",
            HostCoverage: "WPF/Avalonia consume ordinary shared slide shape and connector ops emitted by the SmartArt timeline layout planner; no renderer-local SmartArt policy",
            EvidenceDocs:
            [
                "docs/parity/freep-smartart-basic-timeline-20260726.md"
            ],
            Verification:
            [
                "freep/FreeP.App.Presentation.Tests/SmartArtLayoutTests.cs",
                "freep/FreeP.App.Host.Tests/SmartArtTests.cs",
                "freep/FreeP.App.Avalonia.Tests/MainWindowHeadlessTests.cs"
            ],
            RemainingWork: "basicTimeline now has a shared editable rail, alternating timeline nodes, and deterministic stems while PowerPoint-authoritative artwork metrics and broader SmartArt authoring remain deferred."),
        new(
            EvidenceId: "freep.smartart.step-down-process",
            Area: "SmartArt step down process live layout",
            Status: "shared-render-planner-evidence",
            HostCoverage: "WPF/Avalonia consume ordinary shared slide shape and connector ops emitted by the SmartArt process-family planner; no renderer-local SmartArt policy",
            EvidenceDocs:
            [
                "docs/parity/freep-smartart-step-down-process-20260726.md"
            ],
            Verification:
            [
                "freep/FreeP.App.Presentation.Tests/SmartArtLayoutTests.cs",
                "freep/FreeP.App.Host.Tests/SmartArtTests.cs",
                "freep/FreeP.App.Avalonia.Tests/MainWindowHeadlessTests.cs"
            ],
            RemainingWork: "StepDownProcess now has a shared ordered staggered-box editing path while PowerPoint-authoritative geometry, broader SmartArt authoring, and pixel-level parity remain deferred."),
        new(
            EvidenceId: "freep.smartart.basic-radial",
            Area: "SmartArt basic radial live layout",
            Status: "shared-render-planner-evidence",
            HostCoverage: "WPF/Avalonia consume ordinary shared slide shape and connector ops emitted by the SmartArt radial layout planner; no renderer-local SmartArt policy",
            EvidenceDocs:
            [
                "docs/parity/freep-smartart-basic-radial-20260726.md"
            ],
            Verification:
            [
                "freep/FreeP.App.Presentation.Tests/SmartArtLayoutTests.cs",
                "freep/FreeP.App.Host.Tests/SmartArtTests.cs",
                "freep/FreeP.App.Avalonia.Tests/MainWindowHeadlessTests.cs"
            ],
            RemainingWork: "radial1 now has a shared hub-and-spoke editing path while PowerPoint-authoritative geometry, broader SmartArt authoring, and pixel-level parity remain deferred."),
        new(
            EvidenceId: "freep.smartart.segmented-process",
            Area: "SmartArt segmented process live layout",
            Status: "shared-render-planner-evidence",
            HostCoverage: "WPF/Avalonia consume ordinary shared slide shape and connector ops emitted by the SmartArt layout planner; no renderer-local SmartArt policy",
            EvidenceDocs:
            [
                "docs/parity/freep-smartart-segmented-process-2026-07-06.md",
                "docs/parity/freep-smartart-segmented-process-wave113-20260802.md"
            ],
            Verification:
            [
                "freep/FreeP.App.Presentation.Tests/SmartArtLayoutTests.cs",
                "freep/FreeP.App.Presentation.Tests/SmartArtEditingPlannerTests.cs",
                "freep/FreeP.App.Presentation.Tests/PptxRepairCorpusValidityTests.cs",
                "freep/FreeP.App.Host.Tests/SmartArtTests.cs",
                "freep/FreeP.App.Avalonia.Tests/MainWindowHeadlessTests.cs"
            ],
            RemainingWork: "segmentedProcess now uses dedicated shared vertical rectangular segments with stable ordered down-arrow relationships, regenerated drawing-cache evidence, and schema-valid PPTX round-trip coverage. Exact PowerPoint segment styling/effects, PowerPoint-authoritative visual baselines, and broader SmartArt geometry families remain deferred."),
        new(
            EvidenceId: "freep.smartart.chevron-process",
            Area: "SmartArt chevron process live layout",
            Status: "shared-render-planner-evidence",
            HostCoverage: "WPF/Avalonia consume the same shared Chevron slide-shape ops emitted by the SmartArt process-family planner; no renderer-local SmartArt policy",
            EvidenceDocs:
            [
                "docs/parity/freep-smartart-chevron-process-2026-07-07.md"
            ],
            Verification:
            [
                "freep/FreeP.App.Presentation.Tests/SmartArtLayoutTests.cs",
                "freep/FreeP.App.Host.Tests/SmartArtTests.cs"
            ],
            RemainingWork: "chevronProcess, basicChevronProcess, and closedChevronProcess now share the Chevron preset planner for parsed ordered-stage nodes, and larger inputs no longer fall back solely because of item count. The checked-in corpus does not justify distinct basic/closed geometry, so no separate closed behavior is claimed. Malformed or out-of-bound input remains on cached drawing fallback. Exact PowerPoint chevron metrics, effects, authoring regeneration, and PowerPoint-authoritative pixel baselines remain deferred."),
        new(
            EvidenceId: "freep.smartart.bending-process",
            Area: "SmartArt bendingProcess live layout",
            Status: "shared-render-planner-evidence",
            HostCoverage: "WPF/Avalonia consume ordinary shared slide shape and connector ops emitted by the SmartArt process-family planner; no renderer-local SmartArt policy",
            EvidenceDocs:
            [
                "docs/parity/freep-smartart-bending-process-2026-07-13.md"
            ],
            Verification:
            [
                "freep/FreeP.App.Presentation.Tests/SmartArtLayoutTests.cs",
                "freep/FreeP.App.Host.Tests/SmartArtTests.cs",
                "freep/FreeP.App.Avalonia.Tests/MainWindowHeadlessTests.cs"
            ],
            RemainingWork: "bendingProcess now keeps every parsed ordered-stage node on the shared two-track live-layout path; larger inputs no longer fall back solely because of item count. Exact PowerPoint bending/turning geometry, polygon contours, overlap, spacing, PowerPoint-authoritative visual baselines, and SmartArt authoring/editing remain deferred."),
        new(
            EvidenceId: "freep.smartart.alternating-process",
            Area: "SmartArt alternatingProcess live layout",
            Status: "shared-render-planner-evidence",
            HostCoverage: "WPF/Avalonia consume ordinary shared upper/lower-track shape and connector ops emitted by the SmartArt process-family planner; no renderer-local SmartArt policy",
            EvidenceDocs:
            [
                "docs/parity/freep-smartart-alternating-process-2026-07-14.md"
            ],
            Verification:
            [
                "freep/FreeP.App.Presentation.Tests/SmartArtLayoutTests.cs",
                "freep/FreeP.App.Host.Tests/SmartArtTests.cs",
                "freep/FreeP.App.Avalonia.Tests/MainWindowHeadlessTests.cs"
            ],
            RemainingWork: "alternatingProcess now uses a bounded shared process live-layout path for parsed ordered-stage nodes as alternating upper/lower process tracks with shared connector ops. Exact PowerPoint process contours, effects, spacing, PowerPoint-authoritative visual baselines, and SmartArt authoring/editing remain deferred."),
        new(
            EvidenceId: "freep.smartart.funnel-process",
            Area: "SmartArt funnel process live layout",
            Status: "shared-render-planner-evidence",
            HostCoverage: "WPF/Avalonia consume ordinary shared trapezoid stage and connector ops emitted by the SmartArt process-family planner; no renderer-local SmartArt policy",
            EvidenceDocs:
            [
                "docs/parity/freep-smartart-funnel-process-2026-07-14.md"
            ],
            Verification:
            [
                "freep/FreeP.App.Presentation.Tests/SmartArtLayoutTests.cs",
                "freep/FreeP.App.Host.Tests/SmartArtTests.cs",
                "freep/FreeP.App.Avalonia.Tests/MainWindowHeadlessTests.cs"
            ],
            RemainingWork: "funnelProcess now uses a bounded shared process live-layout path for parsed ordered-stage nodes as top-to-bottom trapezoid segments that narrow toward the bottom with centered connector ops. Exact PowerPoint funnel contours, effects, segment overlap, PowerPoint-authoritative visual baselines, and SmartArt authoring/editing remain deferred."),
        new(
            EvidenceId: "freep.smartart.vertical-process",
            Area: "SmartArt verticalProcess live layout",
            Status: "shared-render-planner-evidence",
            HostCoverage: "WPF/Avalonia consume ordinary shared top-to-bottom stage and connector ops emitted by the SmartArt process-family planner; no renderer-local SmartArt policy",
            EvidenceDocs:
            [
                "docs/parity/freep-smartart-vertical-process-2026-07-14.md"
            ],
            Verification:
            [
                "freep/FreeP.App.Presentation.Tests/SmartArtLayoutTests.cs",
                "freep/FreeP.App.Host.Tests/SmartArtTests.cs",
                "freep/FreeP.App.Host.Tests/PptxPackageReaderSourceTests.cs"
            ],
            RemainingWork: "verticalProcess now uses a bounded shared process live-layout path for parsed ordered-stage nodes as top-to-bottom rounded boxes with centered connector ops. Exact PowerPoint vertical-process geometry, effects, spacing, authoring regeneration, and PowerPoint-authoritative visual baselines remain deferred."),
        new(
            EvidenceId: "freep.smartart.circle-process",
            Area: "SmartArt circle process live layout",
            Status: "shared-render-planner-evidence",
            HostCoverage: "WPF/Avalonia consume ordinary shared circular stage and connector ops emitted by the SmartArt process-family planner; no renderer-local SmartArt policy",
            EvidenceDocs:
            [
                "docs/parity/freep-smartart-circle-process-2026-07-14.md"
            ],
            Verification:
            [
                "freep/FreeP.App.Presentation.Tests/SmartArtLayoutTests.cs",
                "freep/FreeP.App.Host.Tests/SmartArtTests.cs",
                "freep/FreeP.App.Avalonia.Tests/MainWindowHeadlessTests.cs"
            ],
            RemainingWork: "circleProcess now uses a bounded shared process live-layout path for parsed ordered-stage nodes as clockwise rounded boxes around an ellipse with loop-closing connector ops. Exact PowerPoint circular-arrow artwork, segment contours, effects, PowerPoint-authoritative visual baselines, and SmartArt authoring/editing remain deferred."),
        new(
            EvidenceId: "freep.smartart.arrow-ribbon",
            Area: "SmartArt arrowRibbon live layout",
            Status: "shared-render-planner-evidence",
            HostCoverage: "WPF/Avalonia consume ordinary shared ribbon segment and connector ops emitted by the SmartArt process-family planner; no renderer-local SmartArt policy",
            EvidenceDocs:
            [
                "docs/parity/freep-smartart-arrow-ribbon-2026-07-14.md"
            ],
            Verification:
            [
                "freep/FreeP.App.Presentation.Tests/SmartArtLayoutTests.cs",
                "freep/FreeP.App.Host.Tests/SmartArtTests.cs",
                "freep/FreeP.App.Avalonia.Tests/MainWindowHeadlessTests.cs"
            ],
            RemainingWork: "arrowRibbon now uses a bounded shared process live-layout path for parsed ordered-stage nodes as left-to-right ribbon segments with connector ops. Exact PowerPoint folded-ribbon contours, arrow tails, effects, PowerPoint-authoritative visual baselines, and SmartArt authoring/editing remain deferred."),
        new(
            EvidenceId: "freep.smartart.basic-block-list",
            Area: "SmartArt basic block list live layout",
            Status: "shared-render-planner-evidence",
            HostCoverage: "WPF/Avalonia consume ordinary shared slide shape ops emitted by the SmartArt list-family layout planner; no renderer-local SmartArt policy",
            EvidenceDocs:
            [
                "docs/parity/freep-smartart-basic-block-list-2026-07-06.md"
            ],
            Verification:
            [
                "freep/FreeP.App.Presentation.Tests/SmartArtLayoutTests.cs",
                "freep/FreeP.App.Host.Tests/SmartArtTests.cs"
            ],
            RemainingWork: "basicBlockList now uses the bounded shared list-family live-layout path for parsed nodes while unsupported list siblings remain on cached drawing fallback. Broader SmartArt geometry families, PowerPoint-authoritative visual baselines, and SmartArt authoring/editing remain deferred."),
        new(
            EvidenceId: "freep.smartart.vertical-box-list",
            Area: "SmartArt vertical box list live layout",
            Status: "shared-render-planner-evidence",
            HostCoverage: "WPF/Avalonia consume ordinary shared slide shape ops emitted by the SmartArt list-family layout planner; no renderer-local SmartArt policy",
            EvidenceDocs:
            [
                "docs/parity/freep-smartart-vertical-box-list-2026-07-06.md"
            ],
            Verification:
            [
                "freep/FreeP.App.Presentation.Tests/SmartArtLayoutTests.cs",
                "freep/FreeP.App.Host.Tests/SmartArtTests.cs"
            ],
            RemainingWork: "verticalBoxList now uses the bounded shared list-family live-layout path for parsed nodes while other unsupported list siblings remain on cached drawing fallback. Broader SmartArt geometry families, PowerPoint-authoritative visual baselines, and SmartArt authoring/editing remain deferred."),
        new(
            EvidenceId: "freep.smartart.stacked-list",
            Area: "SmartArt stacked list live layout",
            Status: "shared-render-planner-evidence",
            HostCoverage: "WPF/Avalonia consume ordinary shared slide shape ops emitted by the SmartArt list-family layout planner; no renderer-local SmartArt policy",
            EvidenceDocs:
            [
                "docs/parity/freep-smartart-stacked-list-2026-07-06.md"
            ],
            Verification:
            [
                "freep/FreeP.App.Presentation.Tests/SmartArtLayoutTests.cs",
                "freep/FreeP.App.Host.Tests/SmartArtTests.cs"
            ],
            RemainingWork: "stackedList now uses the bounded shared list-family live-layout path for parsed nodes while other unsupported list siblings remain on cached drawing fallback. Broader SmartArt geometry families, PowerPoint-authoritative visual baselines, and SmartArt authoring/editing remain deferred."),
        new(
            EvidenceId: "freep.smartart.descending-block-list",
            Area: "SmartArt descendingBlockList live layout",
            Status: "shared-render-planner-evidence",
            HostCoverage: "WPF/Avalonia consume ordinary shared slide shape ops emitted by the SmartArt list-family layout planner; no renderer-local SmartArt policy",
            EvidenceDocs:
            [
                "docs/parity/freep-smartart-descending-block-list-2026-07-14.md"
            ],
            Verification:
            [
                "freep/FreeP.App.Presentation.Tests/SmartArtLayoutTests.cs",
                "freep/FreeP.App.Host.Tests/SmartArtTests.cs"
            ],
            RemainingWork: "descendingBlockList now uses the bounded shared list-family live-layout path for parsed nodes as top-to-bottom right-aligned blocks that narrow toward the bottom. Unsupported list siblings remain on cached drawing fallback. Exact PowerPoint spacing/effects, PowerPoint-authoritative visual baselines, and SmartArt authoring/editing remain deferred."),
        new(
            EvidenceId: "freep.smartart.basic-pyramid",
            Area: "SmartArt basicPyramid live layout",
            Status: "shared-render-planner-evidence",
            HostCoverage: "WPF/Avalonia consume ordinary shared pyramid segment shape ops emitted by the SmartArt list-family layout planner; no renderer-local SmartArt policy",
            EvidenceDocs:
            [
                "docs/parity/freep-smartart-basic-pyramid-2026-07-14.md"
            ],
            Verification:
            [
                "freep/FreeP.App.Presentation.Tests/SmartArtLayoutTests.cs",
                "freep/FreeP.App.Host.Tests/SmartArtTests.cs",
                "freep/FreeP.App.Avalonia.Tests/MainWindowHeadlessTests.cs"
            ],
            RemainingWork: "basicPyramid uses the bounded shared list-family live-layout path for parsed nodes as centered top-to-bottom pyramid segments that widen toward the base. Unsupported pyramid siblings remain on cached drawing fallback. Exact PowerPoint pyramid contours, bevels/effects, merged segment borders, PowerPoint-authoritative visual baselines, and SmartArt authoring/editing remain deferred."),
        new(
            EvidenceId: "freep.smartart.picture-caption-list",
            Area: "SmartArt pictureCaptionList bounded live layout",
            Status: "shared-render-planner-evidence",
            HostCoverage: "WPF/Avalonia consume ordinary shared picture and caption shape ops emitted by the SmartArt layout planner; no renderer-local SmartArt policy",
            EvidenceDocs:
            [
                "docs/parity/freep-smartart-picture-caption-list-2026-07-07.md",
                "docs/parity/freep-smartart-picture-caption-authoring-20260724.md"
            ],
            Verification:
            [
                "freep/FreeP.App.Presentation.Tests/SmartArtLayoutTests.cs",
                "freep/FreeP.App.Presentation.Tests/SmartArtEditingPlannerTests.cs",
                "freep/FreeP.App.Host.Tests/SmartArtTests.cs",
                "freep/FreeP.App.Avalonia.Tests/MainWindowHeadlessTests.cs"
            ],
            RemainingWork: "pictureCaptionList now supports bounded shared live layout plus an undoable authoring command when every node has image bytes. Missing or ambiguous image mapping keeps cached drawing fallback. PowerPoint-authored visual baselines, broader SmartArt picture layouts, and richer image-payload authoring remain deferred."),
        new(
            EvidenceId: "freep.smartart.basic-cycle",
            Area: "SmartArt basic cycle live layout",
            Status: "shared-render-planner-evidence",
            HostCoverage: "WPF/Avalonia consume ordinary shared slide shape and connector ops emitted by the SmartArt cycle-family layout planner; no renderer-local SmartArt policy",
            EvidenceDocs:
            [
                "docs/parity/freep-smartart-basic-cycle-2026-07-06.md"
            ],
            Verification:
            [
                "freep/FreeP.App.Presentation.Tests/SmartArtLayoutTests.cs",
                "freep/FreeP.App.Host.Tests/SmartArtTests.cs"
            ],
            RemainingWork: "basicCycle now uses the bounded shared cycle-family live-layout path for parsed nodes while unsupported cycle siblings remain on cached drawing fallback. Broader SmartArt geometry families, PowerPoint-authoritative visual baselines, and SmartArt authoring/editing remain deferred.")
        ,
        new(
            EvidenceId: "freep.smartart.radial-cycle",
            Area: "SmartArt radial cycle live layout",
            Status: "shared-render-planner-evidence",
            HostCoverage: "WPF/Avalonia consume ordinary shared slide shape and connector ops emitted by the SmartArt cycle-family layout planner; no renderer-local SmartArt policy",
            EvidenceDocs:
            [
                "docs/parity/freep-smartart-radial-cycle-2026-07-07.md"
            ],
            Verification:
            [
                "freep/FreeP.App.Presentation.Tests/SmartArtLayoutTests.cs",
                "freep/FreeP.App.Host.Tests/SmartArtTests.cs"
            ],
            RemainingWork: "radialCycle now uses the bounded shared cycle-family live-layout path for parsed nodes while other unsupported cycle siblings remain on cached drawing fallback. Broader SmartArt geometry families, PowerPoint-authoritative visual baselines, and SmartArt authoring/editing remain deferred.")
        ,
        new(
            EvidenceId: "freep.smartart.radial-list",
            Area: "SmartArt radial list dedicated live layout",
            Status: "shared-render-planner-evidence",
            HostCoverage: "WPF/Avalonia consume ordinary slide shape and center-spoke connector ops emitted by the dedicated radial-list planner; no renderer-local SmartArt policy",
            EvidenceDocs:
            [
                "docs/parity/freep-smartart-radial-list-wave28-20260727.md"
            ],
            Verification:
            [
                "freep/FreeP.App.Presentation.Tests/SmartArtLayoutTests.cs",
                "freep/FreeP.App.Presentation.Tests/SmartArtEditingPlannerTests.cs",
                "freep/FreeP.App.Host.Tests/SmartArtTests.cs",
                "freep/FreeP.App.Avalonia.Tests/MainWindowHeadlessTests.cs"
            ],
            RemainingWork: "radialList now uses the shared radial-spoke plan for every parsed node, so larger diagrams no longer fall back solely because of item count. Exact PowerPoint sizing, connector attachment/routing, effects, native layout-part regeneration, and PowerPoint-authoritative visual baselines remain deferred.")
        ,
        new(
            EvidenceId: "freep.smartart.gear-cycle",
            Area: "SmartArt gear cycle live layout",
            Status: "shared-render-planner-evidence",
            HostCoverage: "WPF/Avalonia consume ordinary shared slide shape and connector ops emitted by the SmartArt cycle-family layout planner; no renderer-local SmartArt policy",
            EvidenceDocs:
            [
                "docs/parity/freep-smartart-gear-cycle-2026-07-07.md"
            ],
            Verification:
            [
                "freep/FreeP.App.Presentation.Tests/SmartArtLayoutTests.cs",
                "freep/FreeP.App.Host.Tests/SmartArtTests.cs"
            ],
            RemainingWork: "gearCycle now uses the bounded shared cycle-family live-layout path for parsed nodes as a renderer-neutral rounded-box/connector approximation, not true gear-tooth geometry. Other unsupported cycle siblings remain on cached drawing fallback. Broader SmartArt geometry families, PowerPoint-authoritative visual baselines, and SmartArt authoring/editing remain deferred.")
        ,
        new(
            EvidenceId: "freep.smartart.text-cycle",
            Area: "SmartArt text cycle live layout",
            Status: "shared-render-planner-evidence",
            HostCoverage: "WPF/Avalonia consume ordinary shared slide shape and connector ops emitted by the SmartArt cycle-family layout planner; no renderer-local SmartArt policy",
            EvidenceDocs:
            [
                "docs/parity/freep-smartart-text-cycle-2026-07-13.md"
            ],
            Verification:
            [
                "freep/FreeP.App.Presentation.Tests/SmartArtLayoutTests.cs",
                "freep/FreeP.App.Host.Tests/SmartArtTests.cs"
            ],
            RemainingWork: "textCycle now uses the bounded shared cycle-family live-layout path for parsed nodes as a renderer-neutral rounded-box/connector approximation. Other unsupported cycle siblings remain on cached drawing fallback. Exact PowerPoint text-cycle placement, richer cycle geometry, PowerPoint-authoritative visual baselines, and SmartArt authoring/editing remain deferred.")
        ,
        new(
            EvidenceId: "freep.smartart.block-cycle",
            Area: "SmartArt blockCycle live layout",
            Status: "shared-render-planner-evidence",
            HostCoverage: "WPF/Avalonia consume ordinary shared slide shape and connector ops emitted by the SmartArt cycle-family layout planner; no renderer-local SmartArt policy",
            EvidenceDocs:
            [
                "docs/parity/freep-smartart-block-cycle-2026-07-14.md"
            ],
            Verification:
            [
                "freep/FreeP.App.Presentation.Tests/SmartArtLayoutTests.cs",
                "freep/FreeP.App.Host.Tests/SmartArtTests.cs"
            ],
            RemainingWork: "blockCycle now uses the bounded shared cycle-family live-layout path for parsed nodes as a renderer-neutral rounded-box/connector approximation. Other unsupported cycle siblings remain on cached drawing fallback. Exact PowerPoint block-cycle segment geometry, richer cycle spacing/effects, PowerPoint-authoritative visual baselines, and SmartArt authoring/editing remain deferred.")
        ,
        new(
            EvidenceId: "freep.smartart.nondirectional-cycle",
            Area: "SmartArt nonDirectionalCycle live layout",
            Status: "shared-render-planner-evidence",
            HostCoverage: "WPF/Avalonia consume ordinary shared slide shape and connector ops emitted by the SmartArt cycle-family layout planner; no renderer-local SmartArt policy",
            EvidenceDocs:
            [
                "docs/parity/freep-smartart-nondirectional-cycle-2026-07-14.md"
            ],
            Verification:
            [
                "freep/FreeP.App.Presentation.Tests/SmartArtLayoutTests.cs",
                "freep/FreeP.App.Host.Tests/SmartArtTests.cs"
            ],
            RemainingWork: "nonDirectionalCycle now uses the bounded shared cycle-family live-layout path for parsed nodes as a renderer-neutral rounded-box/connector approximation. Other unsupported cycle siblings remain on cached drawing fallback. Exact PowerPoint non-directional-cycle segment geometry, richer cycle spacing/effects, PowerPoint-authoritative visual baselines, and SmartArt authoring/editing remain deferred.")
        ,
        new(
            EvidenceId: "freep.smartart.basic-matrix",
            Area: "SmartArt basic matrix live layout",
            Status: "shared-render-planner-evidence",
            HostCoverage: "WPF/Avalonia consume ordinary shared slide shape ops emitted by the SmartArt matrix-family layout planner; no renderer-local SmartArt policy",
            EvidenceDocs:
            [
                "docs/parity/freep-smartart-basic-matrix-wave115-20260803.md"
            ],
            Verification:
            [
                "freep/FreeP.App.Presentation.Tests/SmartArtLayoutTests.cs",
                "freep/FreeP.App.Host.Tests/SmartArtTests.cs"
            ],
            RemainingWork: "basicMatrix and imported matrix1 use the dedicated shared Basic Matrix plan: the first four model top-level (Level == 0, PowerPoint Level 1) nodes render as stable row-major rounded quadrants over a neutral whole diamond, with no connectors; unused top-level and child nodes remain editable but are omitted from the four-idea layout. Unsupported matrix siblings, PowerPoint-authoritative visual baselines, richer SmartArt effects, and broader SmartArt authoring/editing remain deferred.")
        ,
        new(
            EvidenceId: "freep.smartart.titled-matrix",
            Area: "SmartArt titled matrix live layout",
            Status: "shared-render-planner-evidence",
            HostCoverage: "WPF/Avalonia consume ordinary shared slide shape ops emitted by the SmartArt matrix-family layout planner; no renderer-local SmartArt policy",
            EvidenceDocs:
            [
                "docs/parity/freep-smartart-titled-matrix-2026-07-14.md"
            ],
            Verification:
            [
                "freep/FreeP.App.Presentation.Tests/SmartArtLayoutTests.cs",
                "freep/FreeP.App.Host.Tests/SmartArtTests.cs"
            ],
            RemainingWork: "titledMatrix now uses the shared two-column title-band/body plan for every parsed body node, so larger matrices no longer fall back solely because of item count. Unsupported matrix siblings, exact PowerPoint title-band geometry, richer variant styling, and SmartArt authoring/editing remain deferred.")
        ,
        new(
            EvidenceId: "freep.smartart.basic-venn",
            Area: "SmartArt basic Venn live layout",
            Status: "shared-render-planner-evidence",
            HostCoverage: "WPF/Avalonia consume ordinary shared translucent ellipse shape ops emitted by the SmartArt relationship-family layout planner; no renderer-local SmartArt policy",
            EvidenceDocs:
            [
                "docs/parity/freep-smartart-basic-venn-2026-07-14.md"
            ],
            Verification:
            [
                "freep/FreeP.App.Presentation.Tests/SmartArtLayoutTests.cs",
                "freep/FreeP.App.Host.Tests/SmartArtTests.cs"
            ],
            RemainingWork: "basicVenn now keeps every non-empty authored node list on the shared relationship-family live-layout path, scaling ellipse diameter and overlap to stay inside the frame. Unsupported relationship/Venn siblings remain on cached drawing fallback. Exact PowerPoint intersection blending, effects, text offsets, PowerPoint-authoritative visual baselines, and SmartArt authoring/editing remain deferred.")
        ,
        new(
            EvidenceId: "freep.smartart.radial-venn",
            Area: "SmartArt radial Venn live layout",
            Status: "shared-render-planner-evidence",
            HostCoverage: "WPF/Avalonia consume ordinary shared translucent ellipse shape ops emitted by the SmartArt relationship-family layout planner; no renderer-local SmartArt policy",
            EvidenceDocs:
            [
                "docs/parity/freep-smartart-radial-venn-2026-07-14.md"
            ],
            Verification:
            [
                "freep/FreeP.App.Presentation.Tests/SmartArtLayoutTests.cs",
                "freep/FreeP.App.Host.Tests/SmartArtTests.cs",
                "freep/FreeP.App.Avalonia.Tests/MainWindowHeadlessTests.cs"
            ],
            RemainingWork: "radialVenn now keeps every authored diagram with at least three nodes on the shared live-layout path, arranging translucent ellipses around a shared center. Unsupported relationship/Venn siblings and radialVenn diagrams below the minimum node count remain on cached drawing fallback. Exact PowerPoint intersection blending, effects, text offsets, region labeling, PowerPoint-authoritative visual baselines, and SmartArt authoring/editing remain deferred.")
        ,
        new(
            EvidenceId: "freep.smartart.target-list",
            Area: "SmartArt targetList live layout",
            Status: "shared-render-planner-evidence",
            HostCoverage: "WPF/Avalonia consume ordinary shared translucent ellipse shape ops emitted by the SmartArt relationship-family layout planner; no renderer-local SmartArt policy",
            EvidenceDocs:
            [
                "docs/parity/freep-smartart-target-list-2026-07-14.md"
            ],
            Verification:
            [
                "freep/FreeP.App.Presentation.Tests/SmartArtLayoutTests.cs",
                "freep/FreeP.App.Host.Tests/SmartArtTests.cs"
            ],
            RemainingWork: "targetList now uses the shared relationship-family live-layout path for every parsed node as concentric translucent ellipse shapes, so larger diagrams no longer fall back solely because of node count. Unsupported relationship siblings, exact PowerPoint ring clipping, label offsets, effects, PowerPoint-authoritative visual baselines, and SmartArt authoring/editing remain deferred.")
        ,
        new(
            EvidenceId: "freep.smartart.stacked-venn",
            Area: "SmartArt stacked Venn live layout",
            Status: "shared-render-planner-evidence",
            HostCoverage: "WPF/Avalonia consume ordinary shared translucent ellipse shape ops emitted by the SmartArt relationship-family layout planner; no renderer-local SmartArt policy",
            EvidenceDocs:
            [
                "docs/parity/freep-smartart-stacked-venn-2026-07-14.md"
            ],
            Verification:
            [
                "freep/FreeP.App.Presentation.Tests/SmartArtLayoutTests.cs",
                "freep/FreeP.App.Host.Tests/SmartArtTests.cs",
                "freep/FreeP.App.Avalonia.Tests/MainWindowHeadlessTests.cs"
            ],
            RemainingWork: "stackedVenn now keeps every authored diagram with at least two nodes on the shared live-layout path, scaling its offset ellipse stack to the diagram frame. Unsupported relationship siblings and stackedVenn diagrams below the minimum node count remain on cached drawing fallback. Exact PowerPoint stacked region blending, effects, text offsets, PowerPoint-authoritative visual baselines, and SmartArt authoring/editing remain deferred.")
        ,
        new(
            EvidenceId: "freep.smartart.vertical-bullet-list",
            Area: "SmartArt vertical bullet list live layout",
            Status: "shared-render-planner-evidence",
            HostCoverage: "WPF/Avalonia consume ordinary shared slide shape and connector ops emitted by the SmartArt hierarchy-family layout planner; no renderer-local SmartArt policy",
            EvidenceDocs:
            [
                "docs/parity/freep-smartart-vertical-bullet-list-2026-07-07.md"
            ],
            Verification:
            [
                "freep/FreeP.App.Presentation.Tests/SmartArtLayoutTests.cs",
                "freep/FreeP.App.Host.Tests/SmartArtTests.cs"
            ],
            RemainingWork: "verticalBulletList now uses the bounded shared hierarchy-family live-layout path for parsed root/child nodes while other unsupported hierarchy siblings remain on cached drawing fallback. Broader SmartArt geometry families, exact bullet styling, PowerPoint-authoritative visual baselines, and SmartArt authoring/editing remain deferred.")
        ,
        new(
            EvidenceId: "freep.smartart.basic-hierarchy",
            Area: "SmartArt basic hierarchy live layout",
            Status: "shared-render-planner-evidence",
            HostCoverage: "WPF/Avalonia consume ordinary shared slide shape and connector ops emitted by the SmartArt hierarchy-family layout planner; no renderer-local SmartArt policy",
            EvidenceDocs:
            [
                "docs/parity/freep-smartart-basic-hierarchy-2026-07-06.md"
            ],
            Verification:
            [
                "freep/FreeP.App.Presentation.Tests/SmartArtLayoutTests.cs",
                "freep/FreeP.App.Host.Tests/SmartArtTests.cs"
            ],
            RemainingWork: "basicHierarchy now uses the bounded shared hierarchy-family live-layout path for parsed root/child nodes while unsupported hierarchy siblings remain on cached drawing fallback. Broader SmartArt geometry families, assistant/org-chart nuance, PowerPoint-authoritative visual baselines, and SmartArt authoring/editing remain deferred.")
        ,
        new(
            EvidenceId: "freep.smartart.hierarchy3",
            Area: "SmartArt hierarchy3 live layout",
            Status: "shared-render-planner-evidence",
            HostCoverage: "WPF/Avalonia consume the same shared left-to-right hierarchy3 layout and connector ops; no renderer-local SmartArt policy",
            EvidenceDocs:
            [
                "docs/parity/freep-smartart-hierarchy3-live-layout-20260727.md"
            ],
            Verification:
            [
                "freep/FreeP.Core.IO/PptxPackageReader.cs",
                "freep/FreeP.App.Presentation/SmartArtLayoutEngine.cs",
                "freep/FreeP.App.Presentation.Tests/SmartArtLayoutTests.cs",
                "freep/FreeP.App.Presentation.Tests/SmartArtEditingPlannerTests.cs",
                "freep/FreeP.App.Host.Tests/SmartArtTests.cs",
                "freep/FreeP.App.Host.Tests/PptxPackageReaderSourceTests.cs"
            ],
            RemainingWork: "hierarchy3 now uses the shared left-to-right hierarchy planner for imported and edited diagrams, matching the authored layout definition's fromL child direction. Exact PowerPoint node sizing, connector routing, style/effect metrics, and PowerPoint-authoritative visual baselines remain deferred; other unmodeled SmartArt layout IDs still use cached drawing fallback.")
        ,
        new(
            EvidenceId: "freep.smartart.horizontal-hierarchy",
            Area: "SmartArt horizontal hierarchy live layout",
            Status: "shared-render-planner-evidence",
            HostCoverage: "WPF/Avalonia consume ordinary shared slide shape and connector ops emitted by the SmartArt horizontal hierarchy planner; no renderer-local SmartArt policy",
            EvidenceDocs:
            [
                "docs/parity/freep-smartart-horizontal-hierarchy-2026-07-14.md"
            ],
            Verification:
            [
                "freep/FreeP.App.Presentation/SmartArtLayoutEngine.cs",
                "freep/FreeP.App.Presentation.Tests/SmartArtLayoutTests.cs",
                "freep/FreeP.App.Host.Tests/SmartArtTests.cs"
            ],
            RemainingWork: "horizontalHierarchy now uses a bounded shared hierarchy-family live-layout path with root/parent nodes on the left, child/report nodes in right-hand depth columns, and shared connector ops, while unsupported hierarchy siblings remain on cached drawing fallback. Exact PowerPoint geometry/effects, authoring regeneration for layout/style/color parts, and authoritative PNG baselines remain deferred.")
        ,
        new(
            EvidenceId: "freep.smartart.labeled-hierarchy",
            Area: "SmartArt labeled hierarchy live layout",
            Status: "shared-render-planner-evidence",
            HostCoverage: "WPF/Avalonia consume ordinary shared slide shape and connector ops emitted by the SmartArt hierarchy-family layout planner; no renderer-local SmartArt policy",
            EvidenceDocs:
            [
                "docs/parity/freep-smartart-labeled-hierarchy-2026-07-14.md"
            ],
            Verification:
            [
                "freep/FreeP.Core.IO/PptxPackageReader.cs",
                "freep/FreeP.App.Presentation.Tests/SmartArtLayoutTests.cs",
                "freep/FreeP.App.Presentation.Tests/SmartArtEditingPlannerTests.cs",
                "freep/FreeP.App.Host.Tests/SmartArtTests.cs",
                "freep/FreeP.App.Avalonia.Tests/MainWindowHeadlessTests.cs"
            ],
            RemainingWork: "labeledHierarchy now uses the bounded shared hierarchy-family live-layout path and has a shared undoable authoring command, while other unsupported hierarchy siblings remain on cached drawing fallback. This is a shared hierarchy approximation, not true PowerPoint label geometry; exact label placement/effects and authoritative PNG baselines remain deferred.")
        ,
        new(
            EvidenceId: "freep.smartart.table-hierarchy",
            Area: "SmartArt tableHierarchy shared cell layout",
            Status: "shared-render-planner-evidence",
            HostCoverage: "WPF/Avalonia consume the same shared tableHierarchy cell plan: full-width headers and aligned child-group cells with no connector ops; no renderer-local SmartArt policy",
            EvidenceDocs:
            [
                "docs/parity/freep-smartart-table-hierarchy-2026-07-14.md"
            ],
            Verification:
            [
                "freep/FreeP.Core.IO/PptxPackageReader.cs",
                "freep/FreeP.App.Presentation.Tests/SmartArtLayoutTests.cs",
                "freep/FreeP.App.Presentation.Tests/SmartArtEditingPlannerTests.cs",
                "freep/FreeP.App.Host.Tests/SmartArtTests.cs",
                "freep/FreeP.App.Avalonia.Tests/MainWindowHeadlessTests.cs"
            ],
            RemainingWork: "tableHierarchy now uses a bounded shared no-connector table-cell plan for imported and edited diagrams, while other unsupported hierarchy siblings remain on cached drawing fallback. Exact PowerPoint cell sizing, table styling, spacing/effects, multi-group semantics, and authoritative PNG baselines remain deferred.")
        ,
        new(
            EvidenceId: "freep.smartart.org-chart",
            Area: "SmartArt orgChart live layout",
            Status: "shared-render-planner-evidence",
            HostCoverage: "WPF/Avalonia consume the same dedicated shared orgChart assistant-aware box and connector plan; no renderer-local SmartArt policy",
            EvidenceDocs:
            [
                "docs/parity/freep-smartart-org-chart-2026-07-07.md",
                "docs/parity/freep-smartart-org-chart-assistant-geometry-2026-07-13.md",
                "docs/parity/freep-smartart-org-chart-specialized-wave27-20260727.md"
            ],
            Verification:
            [
                "tools/FreeP.RenderCompare/CorpusGenerator.cs",
                "freep/FreeP.Core.IO/PptxPackageReader.cs",
                "freep/FreeP.App.Presentation/SmartArtLayoutEngine.cs",
                "freep/FreeP.App.Presentation.Tests/SmartArtLayoutTests.cs",
                "freep/FreeP.App.Presentation.Tests/SmartArtEditingPlannerTests.cs",
                "freep/FreeP.App.Host.Tests/SmartArtTests.cs",
                "freep/FreeP.App.Avalonia.Tests/MainWindowHeadlessTests.cs"
            ],
            RemainingWork: "orgChart now uses a dedicated bounded shared hierarchy-family plan: regular nodes use rounded boxes, assistants use rectangular boxes, and both use shared parent-child connector operations. Unsupported hierarchy siblings remain on cached drawing fallback. Exact PowerPoint assistant connector routing, box metrics, style/effect fidelity, native layout/style/color authoring regeneration, and PowerPoint-authoritative visual baselines remain deferred."),
        new(
            EvidenceId: "freep.smartart.outline-editing",
            Area: "SmartArt outline reorder, promote, and demote editing",
            Status: "shared-model-planner-evidence",
            HostCoverage: "WPF/Avalonia can consume the same SmartArtEditingPlanner model mutations and live-layout refreshes; no renderer-local SmartArt editing policy",
            EvidenceDocs:
            [
                "docs/parity/freep-smartart-outline-editing-2026-07-14.md"
            ],
            Verification:
            [
                "freep/FreeP.App.Presentation/SmartArtEditingPlanner.cs",
                "freep/FreeP.App.Presentation.Tests/SmartArtEditingPlannerTests.cs"
            ],
            RemainingWork: "Shared SmartArt outline editing now covers text change, add/remove, reorder, promote, and demote model operations with deterministic outline selection and live-layout refresh evidence. PowerPoint-authored data-part rewrite, richer UI affordances, keyboard shortcuts, and PowerPoint-authoritative authoring baselines remain deferred."),
        new(
            EvidenceId: "freep.smartart.data-part-authoring",
            Area: "SmartArt diagram data-part authoring",
            Status: "shared-model-persistence-evidence",
            HostCoverage: "WPF/Avalonia consume shared SmartArtEditingPlanner data-part rewrite output through the existing PPTX writer with no renderer-local SmartArt persistence policy",
            EvidenceDocs:
            [
                "docs/parity/freep-smartart-data-part-authoring-2026-07-14.md",
                "docs/parity/freep-smartart-nontree-connection-preservation-20260730.md"
            ],
            Verification:
            [
                "freep/FreeP.App.Presentation/SmartArtEditingPlanner.cs",
                "freep/FreeP.App.Presentation.Tests/SmartArtEditingPlannerTests.cs",
                "freep/FreeP.App.Host.Tests/SmartArtTests.cs"
            ],
            RemainingWork: "Shared SmartArt outline edits can now regenerate the native diagram data part, round-trip edited node text plus parOf hierarchy, and preserve authored non-tree relationships such as presOf/presParOf when their endpoints remain live. PowerPoint-authored authoring baselines, text-pane UI workflows, richer assistant/org-chart editing nuance, exact PowerPoint visual baselines, and regeneration of layout/style/color/drawing-cache parts remain deferred."),
        new(
            EvidenceId: "freep.smartart.text-pane-cache-authoring",
            Area: "SmartArt text-pane outline and cache authoring",
            Status: "shared-model-and-cache-evidence",
            HostCoverage: "WPF/Avalonia expose thin SmartArt text-pane hosts that route ordered rows and bounded keyboard shortcuts into shared SmartArtEditingPlanner model, data-part, and drawing-cache regeneration with no renderer-local SmartArt tree policy",
            EvidenceDocs:
            [
                "docs/parity/freep-smartart-text-pane-keyboard-routing-2026-07-14.md",
                "docs/parity/freep-smartart-text-pane-cache-authoring-2026-07-14.md",
                "docs/parity/freep-smartart-data-part-authoring-2026-07-14.md",
                "docs/parity/freep-smartart-cache-regeneration-authoring-2026-07-14.md",
                "docs/parity/freep-smartart-edit-session-package-refresh-2026-07-24.md",
                "docs/parity/freep-smartart-text-pane-hosts-2026-07-14.md"
            ],
            Verification:
            [
                "freep/FreeP.App.Presentation/SmartArtEditingPlanner.cs",
                "freep/FreeP.App.Presentation.Tests/SmartArtEditingPlannerTests.cs",
                "freep/FreeP.App.Host.Tests/SmartArtTests.cs",
                "freep/FreeP.App.Host.Tests/ReviewWorkflowAdapterTests.cs",
                "freep/FreeP.App.Avalonia.Tests/MainWindowHeadlessTests.cs"
            ],
            RemainingWork: "Shared text-pane outline rows now rebuild the SmartArt model transactionally, preserve stable ids and bounded picture payloads, feed the existing native data-part plus drawing-cache regeneration paths, and the shared EditingSession layout/Quick Style/Change Colors routes now refresh those package/cache payloads before commit. Bounded Enter/Ctrl+Enter/Tab/Shift+Tab/Alt+Shift+Up/Down keyboard routes and matching thin WPF/Avalonia host text-pane controls remain covered. PowerPoint-authored authoring baselines, richer assistant/org-chart editing nuance, broader picture/media-backed cache regeneration, exact PowerPoint layout/style/color semantics, and PowerPoint-authoritative visual baselines remain deferred.")
      ];

    private static IReadOnlyDictionary<string, IReadOnlyList<CommandLocation>> Collect(RibbonDefinition definition, string profile)
    {
        var locations = new Dictionary<string, List<CommandLocation>>(StringComparer.Ordinal);
        foreach (var tab in definition.Tabs)
        {
            foreach (var group in tab.Groups)
            {
                foreach (var control in group.Controls)
                {
                    AddControl(locations, tab, group, control, profile);
                }
            }
        }

        return locations.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<CommandLocation>)pair.Value
                .OrderBy(location => location.TabId, StringComparer.Ordinal)
                .ThenBy(location => location.GroupId, StringComparer.Ordinal)
                .ThenBy(location => location.Label, StringComparer.Ordinal)
                .ToArray(),
            StringComparer.Ordinal);
    }

    private static void AddControl(
        Dictionary<string, List<CommandLocation>> locations,
        RibbonTab tab,
        RibbonGroup group,
        RibbonControl control,
        string profile)
    {
        if (!string.IsNullOrEmpty(control.CommandId.Value))
        {
            AddLocation(locations, control.CommandId.Value, new CommandLocation(
                Profile: profile,
                TabId: tab.Id,
                Tab: tab.Header,
                GroupId: group.Id,
                Group: group.Header,
                Label: control.Label,
                ControlType: control.GetType().Name,
                Layout: control.PreferredLayout.ToString()));
        }

        foreach (var menuLocation in MenuLocations(control, tab, group, profile))
        {
            AddLocation(locations, menuLocation.CommandId, menuLocation.Location);
        }
    }

'@ + (Get-ToolCommandInventoryMenuTraversalSource) + @'

    private static void AddLocation(
        Dictionary<string, List<CommandLocation>> locations,
        string commandId,
        CommandLocation location)
    {
        if (!locations.TryGetValue(commandId, out var existing))
        {
            existing = [];
            locations.Add(commandId, existing);
        }

        existing.Add(location);
    }

    private static Classification Classify(
        string commandId,
        bool wpfPresent,
        bool avaloniaPresent,
        IReadOnlyList<CommandLocation>? wpfLocations,
        IReadOnlyList<CommandLocation>? avaloniaLocations,
        IReadOnlyCollection<string> allCommandIds)
    {
        if (wpfPresent && avaloniaPresent && string.Equals(commandId, "freep.anim.pane", StringComparison.Ordinal))
            return new Classification("shared", "Shared callback intent; pane UI remains host-local.");

        if (wpfPresent && avaloniaPresent && IsAnimationTimingCommand(commandId))
            return new Classification("shared", "Shared typed timing intent; applies when a selected value and selected-shape animation are available.");

        if (wpfPresent && avaloniaPresent)
            return new Classification("shared", "Available in both generated FreeP ribbon profiles.");

        if (avaloniaPresent && IsAvaloniaPlatformCommand(commandId))
            return new Classification("platform-only", AvaloniaPlatformCommandNote(commandId));

        if (wpfPresent && IsKnownDeferredWpfSlice(wpfLocations ?? Array.Empty<CommandLocation>()))
            return new Classification("known-deferred", "WPF-only profile slice not yet present in the generated Avalonia profile.");

        if (wpfPresent && !avaloniaPresent)
            return new Classification("avalonia-gap", "Shared WPF command is missing from the generated Avalonia profile.");

        return new Classification("platform-only", "Command is present only in one generated platform profile.");
    }

    private static bool IsAvaloniaPlatformCommand(string commandId) =>
        string.Equals(commandId, "freep.undo", StringComparison.Ordinal) ||
        string.Equals(commandId, "freep.redo", StringComparison.Ordinal);

    private static string AvaloniaPlatformCommandNote(string commandId) =>
        commandId switch
        {
            "freep.undo" => "Intended shell/profile variance: Avalonia exposes Undo in its generated Home/Edit ribbon group; WPF routes Undo through ApplicationCommands.Undo, keyboard bindings, and Editor.Undo rather than a generated ribbon control.",
            "freep.redo" => "Intended shell/profile variance: Avalonia exposes Redo in its generated Home/Edit ribbon group; WPF routes Redo through a routed command, keyboard bindings, and Editor.Redo rather than a generated ribbon control.",
            _ => "Command is present only in one generated platform profile."
        };

    private static bool IsKnownDeferredWpfSlice(IReadOnlyList<CommandLocation> locations) =>
        locations.Any(location => location.TabId is "design" or "transitions" or "animations");

    private static bool IsAnimationTimingCommand(string commandId) =>
        string.Equals(commandId, "freep.anim.trigger", StringComparison.Ordinal) ||
        string.Equals(commandId, "freep.anim.duration", StringComparison.Ordinal) ||
        string.Equals(commandId, "freep.anim.delay", StringComparison.Ordinal);

    private static string Surface(bool wpfPresent, bool avaloniaPresent) =>
        wpfPresent && avaloniaPresent
            ? "both"
            : wpfPresent
                ? "wpf-only"
                : "avalonia-only";

    private static string MissingSide(bool wpfPresent, bool avaloniaPresent) =>
        wpfPresent && avaloniaPresent
            ? "none"
            : wpfPresent
                ? "Avalonia"
                : "WPF";
}

internal static class FreePCommandInventoryMarkdown
{
    public static string Build(InventoryDocument inventory)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# FreeP WPF/Avalonia Command Parity Inventory");
        builder.AppendLine();
        builder.AppendLine("Generated by `tools/Generate-FreePCommandParityInventory.ps1` from `FreeP.Ribbon.Definitions`.");
        builder.AppendLine();
        builder.AppendLine("## Summary");
        builder.AppendLine();
        builder.AppendLine(inventory.Notes);
        builder.AppendLine();
        builder.AppendLine("| Total | Both | WPF only | Avalonia only | Missing WPF raw | Missing Avalonia raw | Actionable missing WPF | Actionable missing Avalonia | Shared | Avalonia gaps | Known deferred | Platform-only | Command-id aliases | Workflow evidence rows |");
        builder.AppendLine("|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|");
        builder.AppendLine($"| {inventory.Summary.TotalCommands} | {inventory.Summary.Both} | {inventory.Summary.WpfOnly} | {inventory.Summary.AvaloniaOnly} | {inventory.Summary.MissingWpf} | {inventory.Summary.MissingAvalonia} | {inventory.Summary.ActionableMissingWpf} | {inventory.Summary.ActionableMissingAvalonia} | {inventory.Summary.Shared} | {inventory.Summary.AvaloniaGaps} | {inventory.Summary.KnownDeferred} | {inventory.Summary.PlatformOnly} | {inventory.Summary.CommandIdAliases} | {inventory.Summary.WorkflowEvidenceRows} |");
        builder.AppendLine();
        builder.AppendLine("## Workflow Evidence");
        builder.AppendLine();
        builder.AppendLine("| Evidence ID | Area | Status | Host coverage | Evidence docs | Verification | Remaining work |");
        builder.AppendLine("|---|---|---|---|---|---|---|");
        foreach (var evidence in inventory.WorkflowEvidence)
        {
            builder.AppendLine(
                $"| `{Escape(evidence.EvidenceId)}` | {Escape(evidence.Area)} | {Escape(evidence.Status)} | {Escape(evidence.HostCoverage)} | {Escape(List(evidence.EvidenceDocs))} | {Escape(List(evidence.Verification))} | {Escape(evidence.RemainingWork)} |");
        }

        builder.AppendLine();
        builder.AppendLine("## Matrix");
        builder.AppendLine();
        builder.AppendLine("| Command ID | Label | WPF | Avalonia | Missing side | Classification | WPF location | Avalonia location | Notes |");
        builder.AppendLine("|---|---|---:|---:|---|---|---|---|---|");

        foreach (var command in inventory.Commands)
        {
            builder.AppendLine(
                $"| `{Escape(command.CommandId)}` | {Escape(command.Label)} | {YesNo(command.WpfPresent)} | {YesNo(command.AvaloniaPresent)} | {Escape(command.MissingSide)} | {Escape(command.Classification)} | {Escape(Locations(command.WpfLocations))} | {Escape(Locations(command.AvaloniaLocations))} | {Escape(command.Notes)} |");
        }

        return builder.ToString();
    }

    private static string Locations(IReadOnlyList<CommandLocation> locations) =>
        locations.Count == 0
            ? "-"
            : string.Join("<br>", locations.Select(location => $"{location.TabId}/{location.GroupId} ({location.ControlType})"));

    private static string List(IReadOnlyList<string> values) =>
        values.Count == 0
            ? "-"
            : string.Join("<br>", values.Select(value => $"`{value}`"));

    private static string YesNo(bool value) => value ? "Yes" : "No";

    private static string Escape(string value) =>
        value.Replace("|", "\\|", StringComparison.Ordinal)
            .Replace("\r", "", StringComparison.Ordinal)
            .Replace("\n", "<br>", StringComparison.Ordinal);
}

internal sealed record InventoryDocument(
    int SchemaVersion,
    string GeneratedBy,
    string Source,
    string Notes,
    InventorySummary Summary,
    IReadOnlyList<WorkflowEvidenceEntry> WorkflowEvidence,
    IReadOnlyList<CommandEntry> Commands);

internal sealed record InventorySummary(
    int TotalCommands,
    int Both,
    int WpfOnly,
    int AvaloniaOnly,
    int MissingWpf,
    int MissingAvalonia,
    int ActionableMissingWpf,
    int ActionableMissingAvalonia,
    int Shared,
    int AvaloniaGaps,
    int KnownDeferred,
    int PlatformOnly,
    int CommandIdAliases,
    int WorkflowEvidenceRows);

internal sealed record WorkflowEvidenceEntry(
    string EvidenceId,
    string Area,
    string Status,
    string HostCoverage,
    IReadOnlyList<string> EvidenceDocs,
    IReadOnlyList<string> Verification,
    string RemainingWork);

internal sealed record CommandEntry(
    string CommandId,
    string Label,
    bool WpfPresent,
    bool AvaloniaPresent,
    string Surface,
    string MissingSide,
    string Classification,
    string Notes,
    IReadOnlyList<CommandLocation> WpfLocations,
    IReadOnlyList<CommandLocation> AvaloniaLocations);

internal sealed record CommandLocation(
    string Profile,
    string TabId,
    string Tab,
    string GroupId,
    string Group,
    string Label,
    string ControlType,
    string Layout);

internal sealed record Classification(string Name, string Notes);
'@

Invoke-ToolGeneratedProject @{
    Prefix = "freex-freep-command-inventory"
    Name = "FreeP.CommandInventory.Generator"
    Reference = $definitionsProject
    Source = $programSource
    Outputs = [ordered]@{ $resolvedInventoryPath = $InventoryPath; $resolvedMarkdownPath = $MarkdownPath }
    Arguments = {
        param($outputPaths)
        @($outputPaths[0].TempPath, $outputPaths[1].TempPath)
    }
    Script = "tools\Generate-FreePCommandParityInventory.ps1"
    Failure = "FreeP command parity inventory generator failed."
    Check = $Check
    CheckMessage = "FreeP command parity inventory docs are up to date."
    WriteMessage = "Wrote $InventoryPath and $MarkdownPath."
    DotNetPath = "dotnet"
}
