param(
    [string]$OutputDirectory = "docs\parity\freep-responsive-chrome-2026-08-16",
    [int[]]$Widths = @(1280, 1100, 900, 750),
    [switch]$Check
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot "ToolScriptSupport.ps1")
. (Join-Path $PSScriptRoot "VisualEvidenceScriptSupport.ps1")

$tabs = @("home", "insert", "design", "transitions", "animations", "view")
$requiredWidths = @(1280, 1100, 900, 750)
if (@($Widths | Sort-Object -Unique) -notmatch '^(1280|1100|900|750)$' -or @($Widths | Sort-Object -Unique).Count -ne $requiredWidths.Count) {
    throw "Widths must contain exactly: $($requiredWidths -join ', ')."
}

$resolvedOutputDirectory = Resolve-ToolRepoPath -Path $OutputDirectory -RepoRoot $repoRoot
$manifestPath = Join-Path $resolvedOutputDirectory "manifest.json"
$readmePath = Join-Path $resolvedOutputDirectory "README.md"
$dotnet = Join-Path ${env:ProgramFiles} "dotnet\dotnet.exe"
if (-not (Test-Path -LiteralPath $dotnet -PathType Leaf)) {
    throw "The global dotnet host is required for foreground visual evidence: $dotnet"
}

$projects = @(
    [ordered]@{
        host = "wpf"
        project = "freep\TestSupport\VisualEvidence.Wpf\FreeP.VisualEvidence.Wpf.csproj"
        assembly = "freep\TestSupport\VisualEvidence.Wpf\bin\Release\net10.0-windows10.0.19041.0\FreeP.VisualEvidence.Wpf.dll"
    },
    [ordered]@{
        host = "avalonia"
        project = "freep\TestSupport\VisualEvidence.Avalonia\FreeP.VisualEvidence.Avalonia.csproj"
        assembly = "freep\TestSupport\VisualEvidence.Avalonia\bin\Release\net10.0-windows10.0.19041.0\FreeP.VisualEvidence.Avalonia.dll"
    }
)

function Get-RelativeEvidencePath {
    param([Parameter(Mandatory = $true)][string]$Path)
    if (-not $Path.StartsWith($resolvedOutputDirectory, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Evidence path is outside the capture root: $Path"
    }
    return $Path.Substring($resolvedOutputDirectory.Length).TrimStart([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar).Replace('\', '/')
}

function New-ResponsiveChromeManifest {
    $captures = [System.Collections.Generic.List[object]]::new()
    foreach ($width in $requiredWidths) {
        foreach ($project in $projects) {
            foreach ($tab in $tabs) {
                $base = Join-Path $resolvedOutputDirectory "$width\$($project.host)"
                $full = Join-Path $base "full\ribbon.$tab.png"
                $client = Join-Path $base "client\ribbon.$tab.png"
                foreach ($path in @($full, $client)) {
                    if (-not (Test-Path -LiteralPath $path -PathType Leaf) -or (Get-Item -LiteralPath $path).Length -le 0) {
                        throw "Responsive chrome capture is missing or empty: $path"
                    }
                }
                [void]$captures.Add([ordered]@{
                    captureKey = "ribbon:${width}:$($project.host):$tab"
                    host = $project.host
                    tabId = $tab
                    logicalWidth = $width
                    logicalHeight = 760
                    fullImagePath = Get-RelativeEvidencePath -Path $full
                    clientImagePath = Get-RelativeEvidencePath -Path $client
                    fullImageSha256 = Get-VisualEvidenceFileSha256 -Path $full
                    clientImageSha256 = Get-VisualEvidenceFileSha256 -Path $client
                    captureStatus = "complete"
                })
            }
        }
    }

    return [ordered]@{
        schemaVersion = 1
        tool = "tools/Capture-FreePResponsiveChrome.ps1"
        generatedAtUtc = [DateTime]::UtcNow.ToString("O")
        captureStatus = "complete"
        evidenceSubject = "FreeP WPF and Avalonia app-owned ribbon/chrome"
        captureMethod = "visible app-owned whole-client render target, scenario-isolated host process; each host invocation exits successfully before its captured PNG pair is recorded"
        normalizedDpi = 96
        widths = $requiredWidths
        mappedFreePTabs = $tabs
        expectedCaptureCount = $requiredWidths.Count * $tabs.Count * $projects.Count
        actualCaptureCount = $captures.Count
        captures = @($captures)
        comparisonBoundary = "This responsive lane exercises FreeP's own WPF and Avalonia chrome at four widths. It complements the 1280px full-window scenario lane and the native PowerPoint reference lane; it does not assert raw cross-host or PowerPoint pixel equivalence."
        limitations = @(
            "The six shared FreeP top-level tabs are captured. Slide Show is exposed by FreeP as a group on Transitions rather than a separate top-level tab.",
            "Backstage, dialogs, panes, editing overlays, canvas and status states remain covered by the full-window and dialog/pane evidence lanes at the canonical 1280px viewport."
        )
    }
}

if ($Check) {
    $manifest = New-ResponsiveChromeManifest
    if ($manifest.actualCaptureCount -ne $manifest.expectedCaptureCount) {
        throw "Responsive chrome evidence is incomplete: $($manifest.actualCaptureCount)/$($manifest.expectedCaptureCount)."
    }
    $existing = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    if ($existing.captureStatus -ne "complete" -or $existing.actualCaptureCount -ne $manifest.actualCaptureCount) {
        throw "Responsive chrome manifest is stale or incomplete."
    }
    Write-Host "FreeP responsive chrome evidence is current: $($manifest.actualCaptureCount)/$($manifest.expectedCaptureCount) captures."
    exit 0
}

if (Test-Path -LiteralPath $resolvedOutputDirectory) {
    Remove-Item -LiteralPath $resolvedOutputDirectory -Recurse -Force
}
New-Item -ItemType Directory -Path $resolvedOutputDirectory -Force | Out-Null

foreach ($project in $projects) {
    & $dotnet build $project.project --configuration Release --nologo
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to build $($project.project)."
    }
}

foreach ($width in $requiredWidths) {
    foreach ($project in $projects) {
        $assemblyPath = Join-Path $repoRoot $project.assembly
        if (-not (Test-Path -LiteralPath $assemblyPath -PathType Leaf)) {
            throw "Visual evidence assembly is missing after build: $assemblyPath"
        }
        $runRoot = Join-Path $resolvedOutputDirectory "$width"
        foreach ($tab in $tabs) {
            Write-Host "Capturing FreeP $($project.host) ribbon '$tab' at ${width}px..."
            & $dotnet $assemblyPath `
                --whole-window-visual-evidence-output $runRoot `
                --whole-window-visual-evidence-scenario "ribbon.$tab" `
                --whole-window-visual-evidence-width $width
            if ($LASTEXITCODE -ne 0) {
                throw "FreeP $($project.host) responsive chrome capture failed for '$tab' at ${width}px (exit $LASTEXITCODE)."
            }
        }
    }
}

$manifest = New-ResponsiveChromeManifest
if ($manifest.actualCaptureCount -ne $manifest.expectedCaptureCount) {
    throw "Responsive chrome evidence is incomplete: $($manifest.actualCaptureCount)/$($manifest.expectedCaptureCount)."
}

$json = ($manifest | ConvertTo-Json -Depth 8) + [Environment]::NewLine
[IO.File]::WriteAllText($manifestPath, $json, [Text.UTF8Encoding]::new($false))
$readme = @"
# FreeP responsive WPF/Avalonia chrome capture — 2026-08-16

This directory contains a guarded, scenario-isolated capture matrix for FreeP's six actual top-level ribbon tabs: Home, Insert, Design, Transitions, Animations and View. Both WPF and Avalonia are captured at 1280, 1100, 900 and 750 logical pixels, for 48 app-owned chrome captures.

The 1280px full-window lane remains the evidence for Backstage, dialogs, panes, editor overlays, canvas and status areas. Native Microsoft PowerPoint references are held separately in `docs/parity/freep-powerpoint-chrome-2026-08-16`.
"@
[IO.File]::WriteAllText($readmePath, $readme, [Text.UTF8Encoding]::new($false))
Write-Host "Captured FreeP responsive WPF/Avalonia chrome: $($manifest.actualCaptureCount)/$($manifest.expectedCaptureCount)."
