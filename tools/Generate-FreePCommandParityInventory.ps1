param(
    [string]$InventoryPath = "docs\parity\freep-command-parity-inventory.json",
    [string]$MarkdownPath = "docs\parity\freep-command-parity-inventory.md",
    [switch]$Check
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot

function Resolve-RepoPath {
    param([Parameter(Mandatory = $true)][string]$Path)

    if ([System.IO.Path]::IsPathRooted($Path)) {
        return $Path
    }

    return Join-Path $repoRoot $Path
}

function Convert-ToXmlAttribute {
    param([Parameter(Mandatory = $true)][string]$Value)

    return [System.Security.SecurityElement]::Escape($Value)
}

function Test-FileContentMatches {
    param(
        [Parameter(Mandatory = $true)][string]$ExpectedPath,
        [Parameter(Mandatory = $true)][string]$ActualPath,
        [Parameter(Mandatory = $true)][string]$Label
    )

    if (-not (Test-Path -LiteralPath $ActualPath -PathType Leaf)) {
        throw "$Label is missing. Run tools\Generate-FreePCommandParityInventory.ps1 to create it."
    }

    $expected = Get-Content -LiteralPath $ExpectedPath -Raw
    $actual = Get-Content -LiteralPath $ActualPath -Raw
    if ($expected -cne $actual) {
        throw "$Label is out of date. Run tools\Generate-FreePCommandParityInventory.ps1 to refresh it."
    }
}

$resolvedInventoryPath = Resolve-RepoPath $InventoryPath
$resolvedMarkdownPath = Resolve-RepoPath $MarkdownPath
$tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("freex-freep-command-inventory-" + [System.Guid]::NewGuid().ToString("N"))
$tempJsonPath = Join-Path $tempRoot "freep-command-parity-inventory.json"
$tempMarkdownPath = Join-Path $tempRoot "freep-command-parity-inventory.md"

New-Item -ItemType Directory -Path $tempRoot | Out-Null

try {
    $definitionsProject = Convert-ToXmlAttribute (Resolve-RepoPath "freep\FreeP.Ribbon.Definitions\FreeP.Ribbon.Definitions.csproj")
    $projectPath = Join-Path $tempRoot "FreeP.CommandInventory.Generator.csproj"
    $programPath = Join-Path $tempRoot "Program.cs"

    [IO.File]::WriteAllText($projectPath, @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="$definitionsProject" />
  </ItemGroup>
</Project>
"@)

    [IO.File]::WriteAllText($programPath, @'
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
            Source: "freep/FreeP.Ribbon.Definitions FreePRibbon.Build(FreePRibbonCapabilities.Wpf/Avalonia)",
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
                "docs/parity/freep-powerpoint-native-media-caption-package-baseline-2026-07-05.md",
                "docs/parity/freep-presenter-recording-review-2026-07-04.md"
            ],
            Verification:
            [
                "freep/FreeP.App.Presentation.Tests/SlideShowRecordingExecutionPlannerTests.cs",
                "freep/FreeP.App.Presentation.Tests/SlideShowRecordingReviewPlannerTests.cs",
                "freep/FreeP.App.Host.Tests/SlideShowTests.cs",
                "freep/FreeP.App.Avalonia.Tests/SlideShowWindowHeadlessTests.cs"
            ],
            RemainingWork: "Shared recording capture adapter readiness contracts, paired WPF/Avalonia backend injection, deterministic captured-artifact host evidence, review rows, session-summary persistable counts, captured PPTX media-part payload authoring, generated WebVTT recording-caption artifact persistence, and focused single-track plus multi-track PowerPoint-native media caption relationship/package baselines are covered. Real OS microphone/camera capture implementations, broader real-deck PowerPoint-native media/caption corpus baselines, and PowerPoint COM recording baselines remain deferred."),
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
            RemainingWork: "Shared mention candidate planning and mention insertion now route through the WPF/Avalonia review planner and thin host adapters. PowerPoint-authoritative review-pane visual baselines, coauthor presence, and notification routing remain deferred."),
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
                "docs/parity/freep-animation-pane-advanced-effect-options-2026-07-05.md"
            ],
            Verification:
            [
                "freep/FreeP.App.Presentation.Tests/AnimationPanePlannerTests.cs",
                "freep/FreeP.App.Host.Tests/AnimationPaneTests.cs",
                "freep/FreeP.App.Avalonia.Tests/MainWindowHeadlessTests.cs"
            ],
            RemainingWork: "Shared animation-pane row evidence now covers selected-row state, timing editors, effect-option rows, reorder availability, and playback control readiness across WPF/Avalonia. PowerPoint-authoritative animation-pane UI baselines, advanced effect playback visuals, and unsupported imported effect families remain deferred."),
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
            EvidenceId: "freep.table.inline-text.workflow-depth",
            Area: "Rich inline table-cell text editing, paragraph formatting, selection, and persistence",
            Status: "shared-planner-and-host-evidence",
            HostCoverage: "WPF/Avalonia shared TableCellEditPlanner routes with thin WPF RichTextBox and Avalonia overlay adapters",
            EvidenceDocs:
            [
                "docs/parity/freep-table-cell-rich-editor-fidelity-2026-07-03.md",
                "docs/parity/freep-list-gallery-image-bullet-ui-2026-07-05.md",
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
                "freep/FreeP.App.Avalonia.Tests/MainWindowHeadlessTests.cs"
            ],
            RemainingWork: "WPF/Avalonia now share picture-bullet picker payload execution, paragraph authoring, and PPTX media-part persistence. Avalonia still lacks a true editable rich-text widget equivalent to WPF RichTextBox; PowerPoint-authoritative list-gallery/rich-editor visual baselines remain deferred."),
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
            RemainingWork: "Shared chart label planning now applies preserved PowerPoint-style number/date format codes for common axis and category label cases, including conditional threshold sections and scaled display-unit commas. Broader Excel custom-format semantics, exact locale behavior, and PowerPoint-authoritative chart visual baselines remain deferred."),
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
            EvidenceId: "freep.smartart.continuous-block-process",
            Area: "SmartArt continuous block process live layout",
            Status: "shared-render-planner-evidence",
            HostCoverage: "WPF/Avalonia consume ordinary shared slide shape and connector ops emitted by the SmartArt layout planner; no renderer-local SmartArt policy",
            EvidenceDocs:
            [
                "docs/parity/freep-smartart-continuous-block-process-2026-07-06.md"
            ],
            Verification:
            [
                "freep/FreeP.App.Presentation.Tests/SmartArtLayoutTests.cs",
                "freep/FreeP.App.Host.Tests/SmartArtTests.cs"
            ],
            RemainingWork: "continuousBlockProcess now uses the bounded shared process live-layout path for parsed nodes while unsupported process variants still fall back to cached drawing. Broader SmartArt geometry families, PowerPoint-authoritative visual baselines, and SmartArt authoring/editing remain deferred."),
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
            EvidenceId: "freep.smartart.segmented-process",
            Area: "SmartArt segmented process live layout",
            Status: "shared-render-planner-evidence",
            HostCoverage: "WPF/Avalonia consume ordinary shared slide shape and connector ops emitted by the SmartArt layout planner; no renderer-local SmartArt policy",
            EvidenceDocs:
            [
                "docs/parity/freep-smartart-segmented-process-2026-07-06.md"
            ],
            Verification:
            [
                "freep/FreeP.App.Presentation.Tests/SmartArtLayoutTests.cs",
                "freep/FreeP.App.Host.Tests/SmartArtTests.cs"
            ],
            RemainingWork: "segmentedProcess now uses the bounded shared process live-layout path for parsed ordered-stage nodes while chevron-style and other unsupported process variants remain on cached drawing fallback. Broader SmartArt geometry families, PowerPoint-authoritative visual baselines, and SmartArt authoring/editing remain deferred."),
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

    private static IEnumerable<(string CommandId, CommandLocation Location)> MenuLocations(
        RibbonControl control,
        RibbonTab tab,
        RibbonGroup group,
        string profile)
    {
        var menu = control switch
        {
            RibbonSplitButton splitButton => splitButton.Menu,
            RibbonDropdown dropdown => dropdown.Menu,
            _ => null,
        };

        if (menu is null)
            yield break;

        foreach (var item in MenuItems(menu.Items))
        {
            if (item.CommandId is null)
                continue;

            yield return (item.CommandId.Value.Value, new CommandLocation(
                Profile: profile,
                TabId: tab.Id,
                Tab: tab.Header,
                GroupId: group.Id,
                Group: group.Header,
                Label: item.Header,
                ControlType: "RibbonMenuItem",
                Layout: "Menu"));
        }
    }

    private static IEnumerable<RibbonMenuItem> MenuItems(IEnumerable<RibbonMenuItem> items)
    {
        foreach (var item in items)
        {
            yield return item;
            foreach (var child in MenuItems(item.Children))
                yield return child;
        }
    }

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
        commandId.StartsWith("freep.file.", StringComparison.Ordinal) ||
        string.Equals(commandId, "freep.undo", StringComparison.Ordinal) ||
        string.Equals(commandId, "freep.redo", StringComparison.Ordinal);

    private static string AvaloniaPlatformCommandNote(string commandId) =>
        commandId switch
        {
            "freep.file.new" => "Intended shell/profile variance: Avalonia exposes document lifecycle in its generated Home/File ribbon group; WPF routes New through ApplicationCommands.New, Backstage chrome, and FileCommands.New rather than a generated ribbon control.",
            "freep.file.open" => "Intended shell/profile variance: Avalonia exposes document lifecycle in its generated Home/File ribbon group; WPF routes Open through ApplicationCommands.Open, Backstage chrome, and FileCommands.Open rather than a generated ribbon control.",
            "freep.file.save" => "Intended shell/profile variance: Avalonia exposes document lifecycle in its generated Home/File ribbon group; WPF routes Save through ApplicationCommands.Save, Backstage chrome, and FileCommands.Save rather than a generated ribbon control.",
            "freep.file.save-as" => "Intended shell/profile variance: Avalonia exposes document lifecycle in its generated Home/File ribbon group; WPF routes Save As through ApplicationCommands.SaveAs, Backstage chrome, and FileCommands.SaveAs rather than a generated ribbon control.",
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
'@)

    & dotnet run --project $projectPath --configuration Release -- $tempJsonPath $tempMarkdownPath
    if ($LASTEXITCODE -ne 0) {
        throw "FreeP command parity inventory generator failed."
    }

    if ($Check) {
        Test-FileContentMatches -ExpectedPath $tempJsonPath -ActualPath $resolvedInventoryPath -Label $InventoryPath
        Test-FileContentMatches -ExpectedPath $tempMarkdownPath -ActualPath $resolvedMarkdownPath -Label $MarkdownPath
        Write-Host "FreeP command parity inventory docs are up to date."
        return
    }

    $inventoryDirectory = Split-Path -Parent $resolvedInventoryPath
    $markdownDirectory = Split-Path -Parent $resolvedMarkdownPath
    New-Item -ItemType Directory -Path $inventoryDirectory -Force | Out-Null
    New-Item -ItemType Directory -Path $markdownDirectory -Force | Out-Null
    Copy-Item -LiteralPath $tempJsonPath -Destination $resolvedInventoryPath -Force
    Copy-Item -LiteralPath $tempMarkdownPath -Destination $resolvedMarkdownPath -Force
    Write-Host "Wrote $InventoryPath and $MarkdownPath."
}
finally {
    if (Test-Path -LiteralPath $tempRoot) {
        Remove-Item -LiteralPath $tempRoot -Recurse -Force
    }
}
