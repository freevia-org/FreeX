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
            Notes: "Raw missing counts preserve one-sided generated profile surface counts. Actionable missing counts exclude platform-only commands so Avalonia shell and backed profile commands are not reported as WPF or Avalonia implementation gaps. Workflow evidence rows track bounded FreeP WPF/Avalonia parity-depth slices that are not command gaps.",
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
                "docs/parity/freep-presenter-recording-execution-2026-07-04.md",
                "docs/parity/freep-presenter-recording-review-2026-07-04.md"
            ],
            Verification:
            [
                "freep/FreeP.App.Presentation.Tests/SlideShowRecordingExecutionPlannerTests.cs",
                "freep/FreeP.App.Presentation.Tests/SlideShowRecordingReviewPlannerTests.cs",
                "freep/FreeP.App.Host.Tests/SlideShowTests.cs",
                "freep/FreeP.App.Avalonia.Tests/SlideShowWindowHeadlessTests.cs"
            ],
            RemainingWork: "Real narration/audio capture, camera/media capture backends, captured media persistence, subtitles, and PowerPoint COM recording baselines remain deferred."),
        new(
            EvidenceId: "freep.presenter.ink.execution",
            Area: "Presenter ink, laser, and persistence execution",
            Status: "shared-executable-evidence",
            HostCoverage: "WPF/Avalonia shared planner, overlay render primitives, and retention planning",
            EvidenceDocs:
            [
                "docs/planning/freep-powerpoint-parity-status-2026-06-27.md"
            ],
            Verification:
            [
                "freep/FreeP.App.Presentation.Tests/SlideShowInkExecutionPlannerTests.cs",
                "freep/FreeP.App.Presentation.Tests/SlideShowInkPersistencePlannerTests.cs",
                "freep/FreeP.App.Host.Tests/SlideShowTests.cs",
                "freep/FreeP.App.Avalonia.Tests/SlideShowWindowHeadlessTests.cs"
            ],
            RemainingWork: "Deeper ink persistence workflows, authored PPTX ink package baselines, richer presenter UI, and PowerPoint visual baselines remain deferred."),
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
            RemainingWork: "PowerPoint-authoritative review-pane visual baselines, people-picker mention insertion, coauthor presence, and notification routing remain deferred."),
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
            RemainingWork: "PowerPoint-authoritative accessibility checker baselines, grammar-scale proofing, richer remediation panes, and full reading-order visual parity remain deferred.")
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
            return new Classification("platform-only", "Avalonia shell or backed profile command exposed by its generated profile.");

        if (wpfPresent && IsKnownDeferredWpfSlice(wpfLocations ?? Array.Empty<CommandLocation>()))
            return new Classification("known-deferred", "WPF-only profile slice not yet present in the generated Avalonia profile.");

        if (wpfPresent && !avaloniaPresent)
            return new Classification("avalonia-gap", "Shared WPF command is missing from the generated Avalonia profile.");

        return new Classification("platform-only", "Command is present only in one generated platform profile.");
    }

    private static bool IsAvaloniaPlatformCommand(string commandId) =>
        commandId.StartsWith("freep.file.", StringComparison.Ordinal) ||
        string.Equals(commandId, "freep.undo", StringComparison.Ordinal) ||
        string.Equals(commandId, "freep.redo", StringComparison.Ordinal) ||
        string.Equals(commandId, "freep.font-size", StringComparison.Ordinal) ||
        string.Equals(commandId, "freep.font-color", StringComparison.Ordinal);

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
