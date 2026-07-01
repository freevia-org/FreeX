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

function Get-RelativePath {
    param([Parameter(Mandatory = $true)][string]$Path)

    $root = (Resolve-Path -LiteralPath $repoRoot).Path.TrimEnd('\') + '\'
    $resolved = (Resolve-Path -LiteralPath $Path).Path
    if ($resolved.ToLowerInvariant().StartsWith($root.ToLowerInvariant())) {
        return $resolved.Substring($root.Length).Replace('\', '/')
    }

    return $resolved.Replace('\', '/')
}

function Get-FreeWCommandIds {
    param([Parameter(Mandatory = $true)][string]$Path)

    $content = Get-Content -LiteralPath $Path -Raw
    $matches = [regex]::Matches($content, 'freew\.[A-Za-z0-9_.-]+')
    $ids = New-Object System.Collections.Generic.HashSet[string] ([StringComparer]::Ordinal)

    foreach ($match in $matches) {
        $id = $match.Value.TrimEnd('.', '-')
        if ($id.Length -eq 0) {
            continue
        }

        if ($id.Contains('*') -or $id.Contains('<') -or $id.Contains('>')) {
            continue
        }

        [void]$ids.Add($id)
    }

    return @($ids | Sort-Object)
}

function Get-CommandContext {
    param(
        [Parameter(Mandatory = $true)][string]$CommandId,
        [Parameter(Mandatory = $true)][string[]]$Paths
    )

    $escaped = [regex]::Escape($CommandId)
    foreach ($path in $Paths) {
        $content = Get-Content -LiteralPath $path -Raw
        $match = [regex]::Match($content, $escaped)
        if (-not $match.Success) {
            continue
        }

        $start = [Math]::Max(0, $match.Index - 240)
        $length = [Math]::Min($content.Length - $start, $match.Length + 480)
        return $content.Substring($start, $length)
    }

    return ""
}

function Get-KnownClassification {
    param(
        [Parameter(Mandatory = $true)][string]$CommandId,
        [Parameter(Mandatory = $true)][string[]]$Paths
    )

    $context = Get-CommandContext -CommandId $CommandId -Paths $Paths
    if ($context -match '(?i)windows-only|win32|platform-only') {
        return "platform-only"
    }

    if ($context -match '(?i)deferred|EmptyRibbonCommand|stub button|safe no-op|placeholder') {
        return "known-deferred"
    }

    return $null
}

function Test-SetContains {
    param(
        [Parameter(Mandatory = $true)]$Set,
        [Parameter(Mandatory = $true)][string]$Value
    )

    return $Set.Contains($Value)
}

function New-StringSet {
    param([string[]]$Values)

    $set = New-Object System.Collections.Generic.HashSet[string] ([StringComparer]::Ordinal)
    foreach ($value in $Values) {
        [void]$set.Add($value)
    }

    return $set
}

function Normalize-GeneratedText {
    param([Parameter(Mandatory = $true)][string]$Value)

    return $Value.Replace("`r`n", "`n")
}

$wpfDefinitionPath = Resolve-RepoPath "freew\FreeW.Ribbon.Definitions\FreeWRibbon.cs"
$avaloniaDefinitionPath = Resolve-RepoPath "freew\FreeW.Ribbon.Definitions\FreeWAvaloniaRibbonDefinition.cs"
$wpfRegistryPath = Resolve-RepoPath "freew\FreeW.App.Host\Ribbon\FreeWRibbonCommands.cs"
$avaloniaRegistryPath = Resolve-RepoPath "freew\FreeW.App.Avalonia\Ribbon\FreeWAvaloniaRibbonCommands.cs"

$wpfDefinitionIds = New-StringSet ([string[]](Get-FreeWCommandIds $wpfDefinitionPath))
$avaloniaDefinitionIds = New-StringSet ([string[]](Get-FreeWCommandIds $avaloniaDefinitionPath))
$wpfRegistryIds = New-StringSet ([string[]](Get-FreeWCommandIds $wpfRegistryPath))
$avaloniaRegistryIds = New-StringSet ([string[]](Get-FreeWCommandIds $avaloniaRegistryPath))

$allIds = New-Object System.Collections.Generic.HashSet[string] ([StringComparer]::Ordinal)
foreach ($set in @($wpfDefinitionIds, $avaloniaDefinitionIds, $wpfRegistryIds, $avaloniaRegistryIds)) {
    foreach ($id in $set) {
        [void]$allIds.Add($id)
    }
}

$metadataPaths = @($wpfDefinitionPath, $avaloniaDefinitionPath, $wpfRegistryPath, $avaloniaRegistryPath)
$rows = New-Object System.Collections.Generic.List[object]

foreach ($id in ($allIds | Sort-Object)) {
    $wpfDefined = Test-SetContains $wpfDefinitionIds $id
    $avaloniaDefined = Test-SetContains $avaloniaDefinitionIds $id
    $wpfRegistered = Test-SetContains $wpfRegistryIds $id
    $avaloniaRegistered = Test-SetContains $avaloniaRegistryIds $id
    $known = Get-KnownClassification -CommandId $id -Paths $metadataPaths

    $status = if ($wpfRegistered -and $avaloniaRegistered) {
        "both"
    } elseif ($known -eq "platform-only") {
        "platform-only"
    } elseif ($known -eq "known-deferred") {
        "known-deferred"
    } elseif ($wpfRegistered -and -not $avaloniaRegistered) {
        "wpf-only"
    } elseif (-not $wpfRegistered -and $avaloniaRegistered) {
        "avalonia-only"
    } else {
        "definition-only"
    }

    $note = switch ($status) {
        "both" { "Registered by both FreeW shells." }
        "wpf-only" { "Registered by the WPF host registry only." }
        "avalonia-only" { "Registered by the Avalonia host registry only." }
        "known-deferred" { "Source metadata marks this command as deferred, placeholder, or safe no-op." }
        "platform-only" { "Source metadata marks this command as platform-specific." }
        default { "Declared or mentioned in a FreeW ribbon source, but not registered by either host registry." }
    }

    $rows.Add([pscustomobject]@{
        commandId = $id
        status = $status
        wpf = [pscustomobject]@{
            defined = $wpfDefined
            registered = $wpfRegistered
        }
        avalonia = [pscustomobject]@{
            defined = $avaloniaDefined
            registered = $avaloniaRegistered
        }
        note = $note
    })
}

function Count-Status {
    param([string]$Status)
    return @($rows | Where-Object { $_.status -eq $Status }).Count
}

$sourceFiles = @(
    (Get-RelativePath $wpfDefinitionPath),
    (Get-RelativePath $avaloniaDefinitionPath),
    (Get-RelativePath $wpfRegistryPath),
    (Get-RelativePath $avaloniaRegistryPath)
)

$summary = [pscustomobject]@{
    totalCommands = $rows.Count
    both = (Count-Status "both")
    wpfOnly = (Count-Status "wpf-only")
    avaloniaOnly = (Count-Status "avalonia-only")
    knownDeferred = (Count-Status "known-deferred")
    platformOnly = (Count-Status "platform-only")
    definitionOnly = (Count-Status "definition-only")
}
$rowArray = $rows.ToArray()

$artifact = [pscustomobject]@{
    schema = "freew.command-inventory.v1"
    generatedBy = "tools/Generate-FreeWCommandInventory.ps1"
    sourceFiles = $sourceFiles
    summary = $summary
    commands = $rowArray
}

$json = ($artifact | ConvertTo-Json -Depth 8) + "`n"

$mdLines = New-Object System.Collections.Generic.List[string]
$mdLines.Add("# FreeW command inventory (WPF vs Avalonia)")
$mdLines.Add("")
$mdLines.Add('Generated by `tools/Generate-FreeWCommandInventory.ps1` from FreeW ribbon definitions and command registry sources. Do not edit by hand.')
$mdLines.Add("")
$mdLines.Add('This inventory compares command IDs declared or referenced by the FreeW WPF ribbon, the Avalonia ribbon, the WPF command registry, and the Avalonia command registry. `both`, `wpf-only`, and `avalonia-only` are registry classifications. `known-deferred` and `platform-only` are used only when nearby source metadata supports that classification.')
$mdLines.Add("")
$mdLines.Add("## Summary")
$mdLines.Add("")
$mdLines.Add("| Status | Count |")
$mdLines.Add("|---|---:|")
$mdLines.Add("| Both | $($artifact.summary.both) |")
$mdLines.Add("| WPF-only | $($artifact.summary.wpfOnly) |")
$mdLines.Add("| Avalonia-only | $($artifact.summary.avaloniaOnly) |")
$mdLines.Add("| Known/deferred | $($artifact.summary.knownDeferred) |")
$mdLines.Add("| Platform-only | $($artifact.summary.platformOnly) |")
$mdLines.Add("| Definition-only | $($artifact.summary.definitionOnly) |")
$mdLines.Add("| Total | $($artifact.summary.totalCommands) |")
$mdLines.Add("")
$mdLines.Add("## Matrix")
$mdLines.Add("")
$mdLines.Add("| Command ID | Status | WPF definition | WPF registry | Avalonia definition | Avalonia registry | Notes |")
$mdLines.Add("|---|---|:---:|:---:|:---:|:---:|---|")
foreach ($row in $rows) {
    $wpfDef = if ($row.wpf.defined) { "yes" } else { "" }
    $wpfReg = if ($row.wpf.registered) { "yes" } else { "" }
    $avDef = if ($row.avalonia.defined) { "yes" } else { "" }
    $avReg = if ($row.avalonia.registered) { "yes" } else { "" }
    $command = "``$($row.commandId)``"
    $mdLines.Add("| $command | $($row.status) | $wpfDef | $wpfReg | $avDef | $avReg | $($row.note) |")
}
$markdown = ($mdLines -join "`n") + "`n"

$resolvedJsonPath = Resolve-RepoPath $JsonPath
$resolvedMarkdownPath = Resolve-RepoPath $MarkdownPath

if ($Check) {
    $existingJson = if (Test-Path -LiteralPath $resolvedJsonPath) { Get-Content -LiteralPath $resolvedJsonPath -Raw } else { "" }
    $existingMarkdown = if (Test-Path -LiteralPath $resolvedMarkdownPath) { Get-Content -LiteralPath $resolvedMarkdownPath -Raw } else { "" }

    if ((Normalize-GeneratedText $existingJson) -cne (Normalize-GeneratedText $json)) {
        throw "FreeW command inventory JSON is out of date. Run tools\Generate-FreeWCommandInventory.ps1 to refresh it."
    }

    if ((Normalize-GeneratedText $existingMarkdown) -cne (Normalize-GeneratedText $markdown)) {
        throw "FreeW command inventory Markdown is out of date. Run tools\Generate-FreeWCommandInventory.ps1 to refresh it."
    }

    Write-Host "FreeW command inventory docs are up to date."
    return
}

$jsonDirectory = Split-Path -Parent $resolvedJsonPath
$markdownDirectory = Split-Path -Parent $resolvedMarkdownPath
New-Item -ItemType Directory -Path $jsonDirectory -Force | Out-Null
New-Item -ItemType Directory -Path $markdownDirectory -Force | Out-Null
[IO.File]::WriteAllText($resolvedJsonPath, $json)
[IO.File]::WriteAllText($resolvedMarkdownPath, $markdown)

Write-Host "Wrote $JsonPath and $MarkdownPath."
