param(
    [string]$JsonPath = "freew\FreeW.Ribbon.Definitions.Tests\freew-canonical-ribbon-evidence.json",
    [switch]$Check
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
. (Join-Path $repoRoot "tools\ToolScriptSupport.ps1")

$resolvedJsonPath = Resolve-ToolRepoPath -Path $JsonPath -RepoRoot $repoRoot
$definitionsProject = ConvertTo-ToolXmlAttribute (Resolve-ToolRepoPath `
    -Path "freew\FreeW.Ribbon.Definitions\FreeW.Ribbon.Definitions.csproj" `
    -RepoRoot $repoRoot)

$programSource = @'
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Free.Shared.Ribbon;
using FreeW.Ribbon.Definitions;

if (args.Length != 1)
    throw new ArgumentException("Expected the JSON output path.");

var wpf = FreeWRibbon.Build(FreeWRibbonCapabilities.Wpf);
var avalonia = FreeWRibbon.Build(FreeWRibbonCapabilities.Avalonia);
var tabIds = wpf.Tabs.Select(tab => tab.Id)
    .Intersect(avalonia.Tabs.Select(tab => tab.Id), StringComparer.Ordinal)
    .Order(StringComparer.Ordinal)
    .ToArray();

var evidence = new
{
    schema = "freew.canonical-ribbon-profiles.v1",
    generatedBy = "freew/FreeW.Ribbon.Definitions.Tests/Generate-FreeWCanonicalRibbonEvidence.ps1",
    topologySource = "FreeWRibbon.Build(FreeWRibbonCapabilities.Wpf/Avalonia)",
    tabs = tabIds.Select(tabId => new
    {
        tabId,
        wpfSha256 = Hash(TabSignature(wpf.FindTab(tabId)!)),
        avaloniaSha256 = Hash(TabSignature(avalonia.FindTab(tabId)!)),
    }),
};

File.WriteAllText(
    args[0],
    JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine,
    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

static string Hash(string value) =>
    Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

static string TabSignature(RibbonTab tab) =>
    $"{tab.Id}|{tab.Header}|{tab.KeyTip}|{tab.Context}|" +
    string.Join(";;", tab.Groups.Select(GroupSignature));

static string GroupSignature(RibbonGroup group) =>
    $"{group.Id}|{group.Header}|{group.KeyTip}|{group.Priority}|" +
    $"{string.Join(',', group.Sizing.SupportedVariants)}|{group.Sizing.Hints}|" +
    string.Join(';', group.Controls.Select(ControlSignature));

static string ControlSignature(RibbonControl control) =>
    $"{control.GetType().Name}|{control.CommandId.Value}|{control.Label}|{control.KeyTip}|" +
    $"{control.Icon}|{control.PreferredLayout}|{control.TooltipTitle}|{control.TooltipDescription}|{ControlExtra(control)}";

static string ControlExtra(RibbonControl control) => control switch
{
    RibbonComboBox combo => $"{combo.Width}|{string.Join(',', combo.Items)}",
    RibbonDropdown dropdown => MenuSignature(dropdown.Menu),
    RibbonSplitButton splitButton => MenuSignature(splitButton.Menu),
    _ => string.Empty,
};

static string MenuSignature(RibbonMenu menu) =>
    string.Join(',', menu.Items.Select(MenuItemSignature));

static string MenuItemSignature(RibbonMenuItem item) =>
    $"{item.Header}|{item.CommandId?.Value}|{item.KeyTip}|{item.InputGesture}|{item.Kind}|{item.IsEnabled}|{item.IsChecked}|" +
    string.Join(';', item.Children.Select(MenuItemSignature));
'@

Invoke-ToolGeneratedProject @{
    Prefix = "freex-freew-canonical-ribbon-evidence"
    Name = "FreeW.CanonicalRibbonEvidence.Generator"
    Reference = $definitionsProject
    Source = $programSource
    Outputs = [ordered]@{ $resolvedJsonPath = $JsonPath }
    Arguments = {
        param($outputPaths)
        @($outputPaths[0].TempPath)
    }
    Script = "freew\FreeW.Ribbon.Definitions.Tests\Generate-FreeWCanonicalRibbonEvidence.ps1"
    Failure = "FreeW canonical ribbon evidence generator failed."
    Check = $Check
    CheckMessage = "FreeW canonical ribbon evidence is up to date."
    WriteMessage = "Wrote $JsonPath."
    DotNetPath = "dotnet"
}
