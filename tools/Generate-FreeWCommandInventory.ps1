param(
    [string]$JsonPath = "docs\parity\freew-command-inventory.json",
    [string]$MarkdownPath = "docs\parity\freew-command-inventory.md",
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
        throw "$Label is missing. Run tools\Generate-FreeWCommandInventory.ps1 to create it."
    }

    $expected = Get-Content -LiteralPath $ExpectedPath -Raw
    $actual = Get-Content -LiteralPath $ActualPath -Raw
    if ($expected -cne $actual) {
        throw "$Label is out of date. Run tools\Generate-FreeWCommandInventory.ps1 to refresh it."
    }
}

$resolvedJsonPath = Resolve-RepoPath $JsonPath
$resolvedMarkdownPath = Resolve-RepoPath $MarkdownPath
$tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("freex-freew-command-inventory-" + [System.Guid]::NewGuid().ToString("N"))
$tempJsonPath = Join-Path $tempRoot "freew-command-inventory.json"
$tempMarkdownPath = Join-Path $tempRoot "freew-command-inventory.md"

New-Item -ItemType Directory -Path $tempRoot | Out-Null

try {
    $definitionsProject = Convert-ToXmlAttribute (Resolve-RepoPath "freew\FreeW.Ribbon.Definitions\FreeW.Ribbon.Definitions.csproj")
    $projectPath = Join-Path $tempRoot "FreeW.CommandInventory.Generator.csproj"
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
using System.Text.RegularExpressions;
using Free.Shared.Ribbon;
using FreeW.Ribbon.Definitions;

if (args.Length != 3)
{
    throw new ArgumentException("Expected repository root, JSON output path, and Markdown output path.");
}

var inventory = FreeWCommandInventory.Build(args[0]);
var options = new JsonSerializerOptions
{
    WriteIndented = true,
    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
};

File.WriteAllText(args[1], JsonSerializer.Serialize(inventory, options) + Environment.NewLine, Encoding.UTF8);
File.WriteAllText(args[2], FreeWCommandInventoryMarkdown.Build(inventory), Encoding.UTF8);

internal static class FreeWCommandInventory
{
    private const string WpfProfile = "WPF";
    private const string AvaloniaProfile = "Avalonia";

    private static readonly SourceLiteralFile[] SourceFiles =
    [
        new("wpfDefinitionSource", "WPF definition source", "freew/FreeW.Ribbon.Definitions/FreeWRibbon.cs"),
        new("avaloniaDefinitionSource", "Avalonia definition source", "freew/FreeW.Ribbon.Definitions/FreeWAvaloniaRibbonDefinition.cs"),
        new("wpfRegistrySource", "WPF registry source", "freew/FreeW.App.Host/Ribbon/FreeWRibbonCommands.cs"),
        new("avaloniaRegistrySource", "Avalonia registry source", "freew/FreeW.App.Avalonia/Ribbon/FreeWAvaloniaRibbonCommands.cs"),
    ];

    public static InventoryDocument Build(string repoRoot)
    {
        var wpf = Collect(FreeWRibbon.Build(FreeWRibbonCapabilities.Wpf), WpfProfile);
        var avalonia = Collect(FreeWRibbon.Build(FreeWRibbonCapabilities.Avalonia), AvaloniaProfile);
        var commandIds = wpf.Keys.Concat(avalonia.Keys)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var sourceTexts = SourceFiles.ToDictionary(
            file => file.Id,
            file => ReadRepositoryFile(repoRoot, file.RelativePath),
            StringComparer.Ordinal);

        var commands = commandIds.Select(commandId =>
        {
            wpf.TryGetValue(commandId, out var wpfLocations);
            avalonia.TryGetValue(commandId, out var avaloniaLocations);
            var wpfPresent = wpfLocations is { Count: > 0 };
            var avaloniaPresent = avaloniaLocations is { Count: > 0 };
            var sourceLiteralEvidence = new SourceLiteralEvidence(
                WpfDefinitionSource: ContainsCommandLiteral(sourceTexts["wpfDefinitionSource"], commandId),
                AvaloniaDefinitionSource: ContainsCommandLiteral(sourceTexts["avaloniaDefinitionSource"], commandId),
                WpfRegistrySource: ContainsCommandLiteral(sourceTexts["wpfRegistrySource"], commandId),
                AvaloniaRegistrySource: ContainsCommandLiteral(sourceTexts["avaloniaRegistrySource"], commandId));
            var classification = Classify(wpfPresent, avaloniaPresent);
            return new CommandEntry(
                CommandId: commandId,
                Label: (wpfLocations ?? avaloniaLocations ?? throw new InvalidOperationException()).First().Label,
                WpfPresent: wpfPresent,
                AvaloniaPresent: avaloniaPresent,
                ProfileSurface: Surface(wpfPresent, avaloniaPresent),
                MissingProfile: MissingProfile(wpfPresent, avaloniaPresent),
                Classification: classification.Name,
                Notes: classification.Notes,
                WpfLocations: wpfLocations ?? Array.Empty<CommandLocation>(),
                AvaloniaLocations: avaloniaLocations ?? Array.Empty<CommandLocation>(),
                SourceLiteralEvidence: sourceLiteralEvidence);
        }).ToArray();

        return new InventoryDocument(
            Schema: "freew.command-inventory.v2",
            SchemaVersion: 2,
            GeneratedBy: "tools/Generate-FreeWCommandInventory.ps1",
            TopologySource: "freew/FreeW.Ribbon.Definitions FreeWRibbon.Build(FreeWRibbonCapabilities.Wpf/Avalonia)",
            SourceLiteralEvidenceNote: "Source literal evidence records exact command-id text in source files only; it is not behavior proof and never creates inventory rows.",
            SourceLiteralFiles: SourceFiles.Select(file => new SourceLiteralFileEntry(file.Id, file.Label, file.RelativePath)).ToArray(),
            Summary: new InventorySummary(
                TotalCommands: commands.Length,
                Both: commands.Count(command => command.ProfileSurface == "both"),
                WpfOnly: commands.Count(command => command.ProfileSurface == "wpf-only"),
                AvaloniaOnly: commands.Count(command => command.ProfileSurface == "avalonia-only"),
                MissingWpf: commands.Count(command => command.MissingProfile == WpfProfile),
                MissingAvalonia: commands.Count(command => command.MissingProfile == AvaloniaProfile)),
            Commands: commands);
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<CommandLocation>> Collect(RibbonDefinition definition, string profile)
    {
        var locations = new Dictionary<string, List<CommandLocation>>(StringComparer.Ordinal);
        foreach (var tab in definition.Tabs)
        {
            foreach (var group in tab.Groups)
            {
                foreach (var control in group.Controls)
                    AddControl(locations, tab, group, control, profile);
            }
        }

        return locations.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<CommandLocation>)pair.Value
                .OrderBy(location => location.TabId, StringComparer.Ordinal)
                .ThenBy(location => location.GroupId, StringComparer.Ordinal)
                .ThenBy(location => location.Label, StringComparer.Ordinal)
                .ThenBy(location => location.ControlType, StringComparer.Ordinal)
                .ThenBy(location => location.Layout, StringComparer.Ordinal)
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
            AddLocation(locations, menuLocation.CommandId, menuLocation.Location);
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

    private static Classification Classify(bool wpfPresent, bool avaloniaPresent) =>
        (wpfPresent, avaloniaPresent) switch
        {
            (true, true) => new Classification("shared-profile", "Command is present in both compiled FreeW ribbon profiles."),
            (true, false) => new Classification("wpf-profile-only", "Command is present only in the compiled WPF FreeW ribbon profile."),
            (false, true) => new Classification("avalonia-profile-only", "Command is present only in the compiled Avalonia FreeW ribbon profile."),
            _ => throw new InvalidOperationException("Command row has no compiled profile location."),
        };

    private static string Surface(bool wpfPresent, bool avaloniaPresent) =>
        wpfPresent && avaloniaPresent
            ? "both"
            : wpfPresent
                ? "wpf-only"
                : "avalonia-only";

    private static string MissingProfile(bool wpfPresent, bool avaloniaPresent) =>
        wpfPresent && avaloniaPresent
            ? "none"
            : wpfPresent
                ? AvaloniaProfile
                : WpfProfile;

    private static string ReadRepositoryFile(string repoRoot, string relativePath)
    {
        var path = Path.Combine(new[] { repoRoot }.Concat(relativePath.Split('/')).ToArray());
        return File.Exists(path)
            ? File.ReadAllText(path)
            : "";
    }

    private static bool ContainsCommandLiteral(string source, string commandId) =>
        Regex.IsMatch(
            source,
            $@"(?<![A-Za-z0-9_.-]){Regex.Escape(commandId)}(?![A-Za-z0-9_.-])",
            RegexOptions.CultureInvariant);
}

internal static class FreeWCommandInventoryMarkdown
{
    public static string Build(InventoryDocument inventory)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# FreeW WPF/Avalonia Command Inventory");
        builder.AppendLine();
        builder.AppendLine("Generated by `tools/Generate-FreeWCommandInventory.ps1` from compiled `FreeW.Ribbon.Definitions` profiles. Do not edit by hand.");
        builder.AppendLine();
        builder.AppendLine("Rows are created only from `FreeWRibbon.Build(FreeWRibbonCapabilities.Wpf)` and `FreeWRibbon.Build(FreeWRibbonCapabilities.Avalonia)`, including menu children. Source literal evidence columns show exact command-id text in source files only; they are not behavior proof and never create rows.");
        builder.AppendLine();
        builder.AppendLine("## Summary");
        builder.AppendLine();
        builder.AppendLine("| Total | Both profiles | WPF profile only | Avalonia profile only | Missing WPF profile | Missing Avalonia profile |");
        builder.AppendLine("|---:|---:|---:|---:|---:|---:|");
        builder.AppendLine($"| {inventory.Summary.TotalCommands} | {inventory.Summary.Both} | {inventory.Summary.WpfOnly} | {inventory.Summary.AvaloniaOnly} | {inventory.Summary.MissingWpf} | {inventory.Summary.MissingAvalonia} |");
        builder.AppendLine();
        builder.AppendLine("## Matrix");
        builder.AppendLine();
        builder.AppendLine("| Command ID | Label | WPF profile | Avalonia profile | Missing profile | Classification | WPF locations | Avalonia locations | Source literal evidence | Notes |");
        builder.AppendLine("|---|---|---:|---:|---|---|---|---|---|---|");

        foreach (var command in inventory.Commands)
        {
            builder.AppendLine(
                $"| `{Escape(command.CommandId)}` | {Escape(command.Label)} | {YesNo(command.WpfPresent)} | {YesNo(command.AvaloniaPresent)} | {Escape(command.MissingProfile)} | {Escape(command.Classification)} | {Escape(Locations(command.WpfLocations))} | {Escape(Locations(command.AvaloniaLocations))} | {Escape(SourceEvidence(command.SourceLiteralEvidence))} | {Escape(command.Notes)} |");
        }

        return builder.ToString();
    }

    private static string Locations(IReadOnlyList<CommandLocation> locations) =>
        locations.Count == 0
            ? "-"
            : string.Join("<br>", locations.Select(location => $"{location.TabId}/{location.GroupId} ({location.ControlType}; {location.Layout})"));

    private static string SourceEvidence(SourceLiteralEvidence evidence)
    {
        var hits = new List<string>();
        if (evidence.WpfDefinitionSource)
            hits.Add("WPF definition source");
        if (evidence.AvaloniaDefinitionSource)
            hits.Add("Avalonia definition source");
        if (evidence.WpfRegistrySource)
            hits.Add("WPF registry source");
        if (evidence.AvaloniaRegistrySource)
            hits.Add("Avalonia registry source");

        return hits.Count == 0 ? "-" : string.Join("<br>", hits);
    }

    private static string YesNo(bool value) => value ? "Yes" : "No";

    private static string Escape(string value) =>
        value.Replace("|", "\\|", StringComparison.Ordinal)
            .Replace("\r", "", StringComparison.Ordinal)
            .Replace("\n", "<br>", StringComparison.Ordinal);
}

internal sealed record InventoryDocument(
    string Schema,
    int SchemaVersion,
    string GeneratedBy,
    string TopologySource,
    string SourceLiteralEvidenceNote,
    IReadOnlyList<SourceLiteralFileEntry> SourceLiteralFiles,
    InventorySummary Summary,
    IReadOnlyList<CommandEntry> Commands);

internal sealed record SourceLiteralFileEntry(
    string Id,
    string Label,
    string Path);

internal sealed record InventorySummary(
    int TotalCommands,
    int Both,
    int WpfOnly,
    int AvaloniaOnly,
    int MissingWpf,
    int MissingAvalonia);

internal sealed record CommandEntry(
    string CommandId,
    string Label,
    bool WpfPresent,
    bool AvaloniaPresent,
    string ProfileSurface,
    string MissingProfile,
    string Classification,
    string Notes,
    IReadOnlyList<CommandLocation> WpfLocations,
    IReadOnlyList<CommandLocation> AvaloniaLocations,
    SourceLiteralEvidence SourceLiteralEvidence);

internal sealed record CommandLocation(
    string Profile,
    string TabId,
    string Tab,
    string GroupId,
    string Group,
    string Label,
    string ControlType,
    string Layout);

internal sealed record SourceLiteralEvidence(
    bool WpfDefinitionSource,
    bool AvaloniaDefinitionSource,
    bool WpfRegistrySource,
    bool AvaloniaRegistrySource);

internal sealed record Classification(string Name, string Notes);

internal sealed record SourceLiteralFile(string Id, string Label, string RelativePath);
'@)

    & dotnet run --project $projectPath --configuration Release -- $repoRoot $tempJsonPath $tempMarkdownPath
    if ($LASTEXITCODE -ne 0) {
        throw "FreeW command inventory generator failed."
    }

    if ($Check) {
        Test-FileContentMatches -ExpectedPath $tempJsonPath -ActualPath $resolvedJsonPath -Label $JsonPath
        Test-FileContentMatches -ExpectedPath $tempMarkdownPath -ActualPath $resolvedMarkdownPath -Label $MarkdownPath
        Write-Host "FreeW command inventory docs are up to date."
        return
    }

    $jsonDirectory = Split-Path -Parent $resolvedJsonPath
    $markdownDirectory = Split-Path -Parent $resolvedMarkdownPath
    New-Item -ItemType Directory -Path $jsonDirectory -Force | Out-Null
    New-Item -ItemType Directory -Path $markdownDirectory -Force | Out-Null
    Copy-Item -LiteralPath $tempJsonPath -Destination $resolvedJsonPath -Force
    Copy-Item -LiteralPath $tempMarkdownPath -Destination $resolvedMarkdownPath -Force
    Write-Host "Wrote $JsonPath and $MarkdownPath."
}
finally {
    if (Test-Path -LiteralPath $tempRoot) {
        Remove-Item -LiteralPath $tempRoot -Recurse -Force
    }
}
