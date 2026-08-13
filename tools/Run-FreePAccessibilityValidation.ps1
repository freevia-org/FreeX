<#
.SYNOPSIS
  Run FreeP live-pane accessibility evidence in the Linux Docker desktop.

.DESCRIPTION
  Starts the production Avalonia FreeP shell, reads representative pane metadata
  from live controls, and runs a companion AT-SPI focus-event query against the
  same X11 desktop. The probe sends real X11 Tab key events and reports an honest
  not-proven result when semantic exposure or deterministic focus traversal is
  incomplete.
#>
[CmdletBinding()]
param(
    [ValidateRange(1024, 65535)]
    [int]$Port = 6158,
    [ValidateRange(640, 7680)]
    [int]$Width = 1280,
    [ValidateRange(480, 4320)]
    [int]$Height = 820,
    [ValidateRange(72, 240)]
    [int]$Dpi = 96,
    [ValidateSet("2g", "4g", "6g", "8g")]
    [string]$MemoryLimit = "4g",
    [string]$OutputDir = "artifacts/linux-family-interactive-wave58/freep-accessibility",
    [string]$PublishDir = "",
    [switch]$SkipPublish,
    [switch]$SkipImageBuild,
    [switch]$Replace
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot "VisualEvidenceScriptSupport.ps1")
$genericRunner = Join-Path $PSScriptRoot "Run-LinuxInteractiveDocker.ps1"
$resolvedOutputRoot = Resolve-VisualEvidenceOutputDirectory -OutputDirectory $OutputDir -RepoRoot $repoRoot

New-Item -ItemType Directory -Path $resolvedOutputRoot -Force | Out-Null
$containerName = "freex-linux-interactive-freep-$Port"
$started = $false
try {
    $startArguments = @(
        "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", $genericRunner,
        "-Action", "Start", "-App", "FreeP", "-Host", "Validation", "-Port", "$Port",
        "-Width", "$Width", "-Height", "$Height", "-Dpi", "$Dpi",
        "-MemoryLimit", $MemoryLimit, "-OutputDir", $resolvedOutputRoot,
        "-AppArgument", "--accessibility-validation=/work/accessibility-validation"
    )
    if (-not [string]::IsNullOrWhiteSpace($PublishDir)) { $startArguments += @("-PublishDir", $PublishDir) }
    if ($SkipPublish) { $startArguments += "-SkipPublish" }
    if ($SkipImageBuild) { $startArguments += "-SkipImageBuild" }
    if ($Replace) { $startArguments += "-Replace" }

    Invoke-VisualEvidenceProcess -FilePath "powershell.exe" -Arguments $startArguments -WorkingDirectory $repoRoot -OutputToHost
    $started = $true

    # Copy the branch-local probe into the running container so a cached app image
    # cannot silently execute an older matcher.
    $probeSource = Join-Path $repoRoot "tools/LinuxInteractiveDocker/run-freep-accessibility-probe.sh"
    $probeTarget = "${containerName}:/tmp/freep-accessibility-probe.sh"
    & docker cp $probeSource $probeTarget 2>&1 | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "Could not copy the branch-local AT-SPI probe into owned container '$containerName'."
    }
    $probeResult = & docker exec $containerName /bin/bash /tmp/freep-accessibility-probe.sh /work/accessibility-validation 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "AT-SPI probe failed in owned container '$containerName': $($probeResult -join [Environment]::NewLine)"
    }

    $sessionPath = Join-Path $resolvedOutputRoot "freep/current-session.json"
    $session = Read-VisualEvidenceJson -Path $sessionPath -TimeoutMilliseconds 60000 -PollMilliseconds 250 -MissingMessage "Expected validation file was not written: $sessionPath"
    $sessionDirectory = [IO.Path]::GetFullPath([string]$session.sessionDirectory)
    $livePath = Join-Path $sessionDirectory "accessibility-validation/live-pane-accessibility.json"
    $atSpiPath = Join-Path $sessionDirectory "accessibility-validation/atspi-result.json"
    $live = Read-VisualEvidenceJson -Path $livePath -TimeoutMilliseconds 60000 -PollMilliseconds 250 -MissingMessage "Expected validation file was not written: $livePath"
    $atSpi = Read-VisualEvidenceJson -Path $atSpiPath -TimeoutMilliseconds 60000 -PollMilliseconds 250 -MissingMessage "Expected validation file was not written: $atSpiPath"
    if ($live.schemaVersion -ne 1 -or $live.suite -ne "freep-live-pane-accessibility" -or
        $live.platform -ne "linux" -or $live.shell -ne "avalonia" -or $live.app -ne "FreeP") {
        throw "Invalid live FreeP accessibility manifest: $livePath"
    }
    $expectedLivePaneIds = @("slide-pane", "notes-pane", "comments-pane", "selection-pane", "animation-pane")
    if (@($live.observations).Count -ne $expectedLivePaneIds.Count) {
        throw "Live FreeP accessibility manifest must contain exactly five representative panes: $livePath"
    }
    foreach ($paneId in $expectedLivePaneIds) {
        if (@($live.observations | Where-Object paneId -eq $paneId).Count -ne 1) {
            throw "Live FreeP accessibility manifest did not contain exactly one '$paneId' observation."
        }
    }
    foreach ($observation in @($live.observations)) {
        foreach ($property in @("paneId", "automationId", "name", "helpText", "role", "state", "value")) {
            if ([string]::IsNullOrWhiteSpace([string]$observation.$property)) {
                throw "Live FreeP accessibility observation '$($observation.paneId)' has an empty $property."
            }
        }
        if (-not $observation.isVisible -or -not $observation.focusable -or
            -not $observation.isTabStop -or [int]$observation.tabIndex -lt 1) {
            throw "Live FreeP pane '$($observation.paneId)' is not visible/focusable/tab-stoppable with a stable positive tab index."
        }
    }
    if ($atSpi.schemaVersion -ne 2 -or $atSpi.suite -ne "freep-atspi-accessibility" -or
        $atSpi.platform -ne "linux" -or $atSpi.shell -ne "avalonia" -or $atSpi.app -ne "FreeP" -or
        $atSpi.evidenceLevel -ne "os-atspi-x11-focus-events" -or
        $atSpi.status -notin @("passed", "not-proven")) {
        throw "Invalid AT-SPI result: $atSpiPath"
    }
    $expectedAtSpiRoles = @{
        slides = @("list", "list box", "listbox")
        notes = @("entry")
        comments = @("panel")
        selection = @("panel")
        animation = @("panel")
    }
    foreach ($target in $expectedAtSpiRoles.Keys) {
        $observation = @($atSpi.observations | Where-Object target -eq $target)
        if ($observation.Count -gt 1) {
            throw "AT-SPI result did not contain exactly one '$target' observation."
        }
        if ($observation.Count -eq 0) {
            if ([string]$atSpi.status -eq "passed") {
                throw "AT-SPI passed result did not contain '$target' observation."
            }
            continue
        }
        foreach ($property in @("name", "role", "state")) {
            if ([string]::IsNullOrWhiteSpace([string]$observation[0].$property)) {
                throw "AT-SPI '$target' observation has an empty $property."
            }
        }
        $role = ([string]$observation[0].role).ToLowerInvariant().Replace("-", " ").Trim()
        if ($expectedAtSpiRoles[$target] -notcontains $role) {
            throw "AT-SPI '$target' observation had role '$($observation[0].role)'; expected $($expectedAtSpiRoles[$target] -join ', ')."
        }
        if ($null -eq $observation[0].PSObject.Properties["value"]) {
            throw "AT-SPI '$target' observation did not report a value field."
        }
        foreach ($property in @("focusable", "visible", "showing", "focusEventCount")) {
            if ($null -eq $observation[0].PSObject.Properties[$property]) {
                throw "AT-SPI '$target' observation did not report $property."
            }
        }
    }
    $expectedFocusOrder = @("slides", "notes", "comments", "selection", "animation")
    if ((@($atSpi.expectedFocusOrder) -join ",") -ne ($expectedFocusOrder -join ",")) {
        throw "AT-SPI expected focus order did not match the shared pane order."
    }
    if ([string]$atSpi.status -eq "passed" -and @($atSpi.focusEvents).Count -lt 5) {
        throw "AT-SPI focus event trail must contain at least five target events."
    }
    if ([string]$atSpi.status -eq "passed" -and ((@($atSpi.focusTraversal) -join ",") -ne ($expectedFocusOrder -join ","))) {
        throw "AT-SPI passed result did not contain the exact shared focus traversal order."
    }

    $report = [ordered]@{
        schemaVersion = 1
        suite = "freep-accessibility-wave59-report"
        platform = "linux"
        shell = "avalonia"
        app = "FreeP"
        parameters = [ordered]@{ width = $Width; height = $Height; dpi = $Dpi; port = $Port }
        liveControl = [ordered]@{
            status = "passed"
            observationCount = @($live.observations).Count
            manifestPath = $livePath
        }
        atSpi = [ordered]@{
            status = [string]$atSpi.status
            observationCount = @($atSpi.observations).Count
            focusEventCount = @($atSpi.focusEvents).Count
            expectedFocusOrder = @($atSpi.expectedFocusOrder)
            focusTraversal = @($atSpi.focusTraversal)
            resultPath = $atSpiPath
            limitation = [string]$atSpi.limitation
            applicationName = [string]$atSpi.applicationName
            windowName = [string]$atSpi.windowName
        }
    }
    $reportPath = Join-Path $resolvedOutputRoot "freep/accessibility-validation/report.json"
    New-Item -ItemType Directory -Path (Split-Path -Parent $reportPath) -Force | Out-Null
    $report | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $reportPath -Encoding utf8
    Write-Host "FreeP accessibility validation: live controls passed ($(@($live.observations).Count)); AT-SPI $($atSpi.status) ($(@($atSpi.observations).Count) observations)."
    Write-Host "Report: $reportPath"
}
finally {
    if ($started) {
        try {
            Invoke-VisualEvidenceProcess -FilePath "powershell.exe" -Arguments @(
                "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", $genericRunner,
                "-Action", "Stop", "-App", "FreeP", "-Port", "$Port",
                "-OutputDir", $resolvedOutputRoot) -WorkingDirectory $repoRoot -OutputToHost
        } catch { Write-Warning "Could not stop harness-owned FreeP container: $($_.Exception.Message)" }
    }
}
