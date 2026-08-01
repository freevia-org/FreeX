<##
.SYNOPSIS
  Runs the dedicated FreeP Wave 90 physical Avalonia native picker lane.

.DESCRIPTION
  Starts the existing LinuxInteractiveDocker FreeP desktop, seeds a second PPTX
  fixture and a pre-existing collision package in the mounted documents folder,
  copies the branch-local X11 probe into the session, and validates its ordered
  screenshot/hash/package manifest. The probe uses only physical X11 keyboard and
  mouse input for Open and Save As; no picker callback override is involved.
#>
[CmdletBinding()]
param(
    [ValidateRange(1024, 65535)][int]$Port = 6110,
    [ValidateRange(640, 7680)][int]$Width = 1280,
    [ValidateRange(480, 4320)][int]$Height = 820,
    [ValidateRange(72, 240)][int]$Dpi = 96,
    [ValidateSet("2g", "4g", "6g", "8g")][string]$MemoryLimit = "4g",
    [string]$OutputDir = "artifacts/freep-native-picker-x11-wave90-20260801",
    [string]$PublishDir = "",
    [switch]$SkipPublish,
    [switch]$SkipImageBuild,
    [switch]$Replace,
    [switch]$KeepContainer
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$resolvedOutputRoot = if ([IO.Path]::IsPathRooted($OutputDir)) {
    [IO.Path]::GetFullPath($OutputDir)
} else {
    [IO.Path]::GetFullPath((Join-Path $repoRoot $OutputDir))
}
$genericRunner = Join-Path $PSScriptRoot "Run-LinuxInteractiveDocker.ps1"
$probeSource = Join-Path $PSScriptRoot "LinuxInteractiveDocker/run-freep-native-picker-x11-wave90-probe.sh"
$schemaPath = Join-Path $PSScriptRoot "LinuxInteractiveDocker/freep-native-picker-x11-wave90-validation.schema.json"
$manifestEvidenceHelper = Join-Path $PSScriptRoot "LinuxInteractiveDocker/ManifestEvidence.ps1"
$null = . $manifestEvidenceHelper

$initialFixturePath = Join-Path $repoRoot "tools/FreeP.RenderCompare/corpus/01-title-slide.pptx"
$selectedFixturePath = Join-Path $repoRoot "tools/FreeP.RenderCompare/corpus/03-mixed-text.pptx"
$initialName = "01-title-slide.pptx"
$selectedName = "freep-picker-open-selected.pptx"
$collisionName = "freep-picker-existing-collision.pptx"
$saveName = "freep-picker-save-as-selected.pptx"
$initialContainerPath = "/documents/$initialName"
$selectedContainerPath = "/documents/$selectedName"
$collisionContainerPath = "/documents/$collisionName"
$saveContainerPath = "/documents/$saveName"
$invalidContainerPath = "/proc/freep-native-picker-x11-wave90.pptx"
$requiredIds = @(
    "visible-window-discovery",
    "open-cancel-preserves-document",
    "open-pptx-selection-loads-package",
    "save-as-pptx-filter-selection-writes-package",
    "save-as-overwrite-cancel-preserves-collision",
    "save-as-unwritable-bounded-error",
    "escape-cancel-open-no-modal-blocker",
    "escape-cancel-save-no-modal-blocker",
    "focus-return-after-cancel-and-error"
)

function Invoke-External {
    param(
        [Parameter(Mandatory = $true)][string]$FilePath,
        [Parameter(Mandatory = $true)][string[]]$Arguments,
        [string]$WorkingDirectory = $repoRoot
    )
    Push-Location $WorkingDirectory
    try {
        & $FilePath @Arguments
        if ($LASTEXITCODE -ne 0) {
            throw "$FilePath exited with code $LASTEXITCODE."
        }
    } finally {
        Pop-Location
    }
}

function Write-HashArtifact {
    param([Parameter(Mandatory = $true)][string]$Path, [Parameter(Mandatory = $true)][string]$Destination)
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        "MISSING: $Path" | Set-Content -LiteralPath $Destination -Encoding utf8
        return
    }
    (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant() |
        Set-Content -LiteralPath $Destination -Encoding ascii
}

function Read-HashArtifact {
    param([Parameter(Mandatory = $true)][string]$Path)
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return "" }
    return (Get-Content -LiteralPath $Path -Raw).Trim().ToLowerInvariant()
}

function Wait-EvidenceFile {
    param([Parameter(Mandatory = $true)][string]$Directory, [Parameter(Mandatory = $true)][string]$Name)
    if ([IO.Path]::GetFileName($Name) -ne $Name) { throw "Evidence reference must be a basename: $Name" }
    for ($attempt = 0; $attempt -lt 60; $attempt++) {
        $entry = Get-ChildItem -LiteralPath $Directory -File -Force -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -ceq $Name } | Select-Object -First 1
        if ($null -ne $entry -and $entry.Length -gt 0) { return $entry.FullName }
        Start-Sleep -Milliseconds 100
    }
    throw "Missing or empty evidence '$Name' in '$Directory'."
}

function Add-ResultEvidence {
    param([Parameter(Mandatory = $true)]$Result, [Parameter(Mandatory = $true)][string[]]$Names)
    $names = [System.Collections.Generic.List[string]]::new()
    foreach ($name in @($Result.evidence) + $Names) {
        if (-not [string]::IsNullOrWhiteSpace([string]$name) -and -not $names.Contains([string]$name)) {
            $names.Add([string]$name)
        }
    }
    $Result.evidence = $names.ToArray()
}

function Assert-PackageState {
    param([Parameter(Mandatory = $true)]$State, [Parameter(Mandatory = $true)][bool]$Exists, [Parameter(Mandatory = $true)][string]$ExpectedPath)
    if ([string]$State.path -ne $ExpectedPath -or [bool]$State.exists -ne $Exists) {
        throw "Package state path/existence mismatch for '$ExpectedPath'."
    }
    if ($Exists) {
        if ([string]$State.packageKind -ne "pptx-zip-package" -or
            [bool]$State.containsPresentationXml -ne $true -or
            [int]$State.slideCount -lt 1 -or
            [string]$State.sha256 -notmatch '^[0-9a-f]{64}$') {
            throw "Package state '$ExpectedPath' is not a strict PPTX package inspection."
        }
    } elseif ([string]$State.packageKind -ne "not-created" -or [string]$State.sha256 -ne "" -or [int]$State.slideCount -ne 0) {
        throw "Missing package state '$ExpectedPath' is not a strict absence inspection."
    }
}

function Assert-ManifestContract {
    param([Parameter(Mandatory = $true)][string]$ManifestPath, [Parameter(Mandatory = $true)][string]$EvidenceDirectory)
    $schema = Get-Content -LiteralPath $schemaPath -Raw | ConvertFrom-Json
    if ($schema.'$schema' -notmatch "json-schema.org") { throw "Manifest schema is not a JSON Schema document." }
    $manifest = Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json
    if ($manifest.contractValidation.status -ne "pending") { throw "Probe must leave contractValidation pending until strict validation." }
    if ($manifest.schemaVersion -ne 1 -or $manifest.suite -ne "freep-native-picker-x11-wave90-physical" -or
        $manifest.platform -ne "linux" -or $manifest.shell -ne "avalonia" -or $manifest.app -ne "FreeP" -or
        $manifest.baseline -ne $false -or $manifest.appSurface -ne "native-storage-provider-open-save-as" -or
        $manifest.window.pattern -ne "FreeP" -or $manifest.window.visible -ne $true) {
        throw "Native picker manifest header failed the Wave 90 contract."
    }
    $ids = @($manifest.results | ForEach-Object { [string]$_.id })
    if ([string]::Join("|", $ids) -ne [string]::Join("|", $requiredIds)) {
        throw "Native picker result rows are not the required ordered nine-row contract."
    }
    if ($manifest.results.Count -ne 9 -or $manifest.summary.total -ne 9 -or
        $manifest.summary.passed -ne 9 -or $manifest.summary.failed -ne 0 -or
        @($manifest.results | Where-Object status -ne "passed").Count -ne 0) {
        throw "Native picker manifest summary contains failed or incomplete physical rows."
    }
    $fixtureIds = @($manifest.fixtures | ForEach-Object { [string]$_.id })
    if ([string]::Join("|", $fixtureIds) -ne "initial|openSelected|collision") { throw "Fixture rows are not ordered or complete." }
    foreach ($fixture in @($manifest.fixtures)) {
        if ([string]$fixture.path -notmatch '^/documents/[^/]+\.pptx$' -or
            [string]$fixture.fileName -notmatch '^[^/]+\.pptx$' -or
            [string]$fixture.sha256 -notmatch '^[0-9a-f]{64}$' -or
            [string]$fixture.packageKind -ne "pptx-zip-package") {
            throw "Fixture '$($fixture.id)' failed the package/hash contract."
        }
    }
    Assert-PackageState $manifest.packageStates.initial $true $initialContainerPath
    Assert-PackageState $manifest.packageStates.openSelected $true $selectedContainerPath
    Assert-PackageState $manifest.packageStates.savePptx $true $saveContainerPath
    Assert-PackageState $manifest.packageStates.collisionBefore $true $collisionContainerPath
    Assert-PackageState $manifest.packageStates.collisionAfter $true $collisionContainerPath
    Assert-PackageState $manifest.packageStates.invalidTarget $false $invalidContainerPath
    if ($manifest.packageStates.collisionBefore.sha256 -ne $manifest.packageStates.collisionAfter.sha256) { throw "Collision cancellation changed the existing package hash." }

    $fileMap = Get-ManifestEvidenceFileMap -EvidenceDirectory $EvidenceDirectory
    foreach ($result in @($manifest.results)) {
        if ([string]$result.category -ne "physical-x11-native-picker" -or
            [string]$result.evidenceLevel -ne "physical-x11-input" -or
            @($result.evidence).Count -lt 1 -or [string]::IsNullOrWhiteSpace([string]$result.note)) {
            throw "Result '$($result.id)' is missing physical evidence metadata."
        }
        foreach ($evidence in @($result.evidence)) {
            $name = [string]$evidence
            if ([IO.Path]::GetFileName($name) -ne $name -or $name.Contains('/') -or $name.Contains('\') -or
                -not $fileMap.ContainsKey($name) -or $fileMap[$name].Length -le 0) {
                throw "Result '$($result.id)' references missing, empty, or non-basename evidence '$name'."
            }
        }
    }
    if (@($manifest.screenshots).Count -lt 9) { throw "Native picker manifest retained fewer than nine screenshots." }
    foreach ($screenshot in @($manifest.screenshots)) { [void](Wait-EvidenceFile $EvidenceDirectory ([string]$screenshot.name)) }

    $manifest.contractValidation = [ordered]@{
        status = "passed"
        validator = "tools/Run-FreePNativePickerX11Validation.ps1"
        contractReference = "tools/LinuxInteractiveDocker/freep-native-picker-x11-wave90-validation.schema.json"
    }
    $manifest | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $ManifestPath -Encoding utf8
    return $manifest
}

foreach ($path in @($probeSource, $schemaPath, $initialFixturePath, $selectedFixturePath)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Required native picker lane file is missing: $path" }
}
New-Item -ItemType Directory -Path $resolvedOutputRoot -Force | Out-Null
$started = $false
$sessionDirectory = $null
$manifestPath = $null
$evidenceDirectory = $null
$probeExitCode = 1
try {
    $startArguments = @(
        "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", $genericRunner,
        "-Action", "Start", "-App", "FreeP", "-Port", "$Port", "-Width", "$Width",
        "-Height", "$Height", "-Dpi", "$Dpi", "-MemoryLimit", $MemoryLimit,
        "-OutputDir", $resolvedOutputRoot, "-DocumentPath", $initialFixturePath
    )
    if (-not [string]::IsNullOrWhiteSpace($PublishDir)) { $startArguments += @("-PublishDir", $PublishDir) }
    if ($SkipPublish) { $startArguments += "-SkipPublish" }
    if ($SkipImageBuild) { $startArguments += "-SkipImageBuild" }
    if ($Replace) { $startArguments += "-Replace" }
    Invoke-External -FilePath "powershell.exe" -Arguments $startArguments
    $started = $true

    $sessionMetadataPath = Join-Path $resolvedOutputRoot "freep/current-session.json"
    if (-not (Test-Path -LiteralPath $sessionMetadataPath -PathType Leaf)) { throw "Generic runner did not write FreeP session metadata." }
    $session = Get-Content -LiteralPath $sessionMetadataPath -Raw | ConvertFrom-Json
    $sessionDirectory = [IO.Path]::GetFullPath([string]$session.sessionDirectory)
    $documentsDirectory = Join-Path $resolvedOutputRoot "freep/documents"
    New-Item -ItemType Directory -Path $documentsDirectory -Force | Out-Null
    Copy-Item -LiteralPath $selectedFixturePath -Destination (Join-Path $documentsDirectory $selectedName) -Force
    Copy-Item -LiteralPath $initialFixturePath -Destination (Join-Path $documentsDirectory $collisionName) -Force

    $probeInWork = Join-Path $sessionDirectory "run-freep-native-picker-x11-wave90-probe.sh"
    Copy-Item -LiteralPath $probeSource -Destination $probeInWork -Force
    $manifestPath = Join-Path $sessionDirectory "freep-native-picker-x11-wave90-validation/results.json"
    $evidenceDirectory = Split-Path -Parent $manifestPath
    New-Item -ItemType Directory -Path $evidenceDirectory -Force | Out-Null
    $probeLog = Join-Path $evidenceDirectory "probe.log"

    Write-HashArtifact $initialFixturePath (Join-Path $evidenceDirectory "fixture-initial-source-before.sha256.txt")
    Write-HashArtifact $selectedFixturePath (Join-Path $evidenceDirectory "fixture-selected-source-before.sha256.txt")
    Write-HashArtifact (Join-Path $documentsDirectory $collisionName) (Join-Path $evidenceDirectory "fixture-collision-before.sha256.txt")
    Write-HashArtifact (Join-Path $documentsDirectory $initialName) (Join-Path $evidenceDirectory "fixture-initial-mounted-before.sha256.txt")
    Write-HashArtifact (Join-Path $documentsDirectory $selectedName) (Join-Path $evidenceDirectory "fixture-selected-mounted-before.sha256.txt")

    $dockerArguments = @(
        "exec",
        "--env", "FREEP_DOCUMENT_PATH=$initialContainerPath",
        "--env", "FREEP_EXPECTED_DOCUMENT_NAME=$initialName",
        "--env", "FREEP_EXPECTED_WINDOW_PATTERN=FreeP",
        "--env", "FREEP_PICKER_OPEN_SELECTED_PATH=$selectedContainerPath",
        "--env", "FREEP_PICKER_SAVE_PATH=$saveContainerPath",
        "--env", "FREEP_PICKER_COLLISION_PATH=$collisionContainerPath",
        "--env", "FREEP_PICKER_INVALID_PATH=$invalidContainerPath",
        "--env", "FREEP_SCREEN_WIDTH=$Width",
        "--env", "FREEP_SCREEN_HEIGHT=$Height",
        "--env", "FREEP_SCREEN_DPI=$Dpi",
        [string]$session.containerName, "bash", "/work/run-freep-native-picker-x11-wave90-probe.sh",
        "/work/freep-native-picker-x11-wave90-validation"
    )
    Push-Location $repoRoot
    try {
        $probeOutput = @(& docker @dockerArguments 2>&1)
        $probeExitCode = $LASTEXITCODE
    } finally {
        Pop-Location
    }
    if ($probeOutput.Count -gt 0) { $probeOutput | Set-Content -LiteralPath $probeLog -Encoding utf8 }
    else { "docker exec produced no stdout/stderr." | Set-Content -LiteralPath $probeLog -Encoding utf8 }

    Write-HashArtifact (Join-Path $documentsDirectory $initialName) (Join-Path $evidenceDirectory "fixture-initial-mounted-after.sha256.txt")
    Write-HashArtifact (Join-Path $documentsDirectory $selectedName) (Join-Path $evidenceDirectory "fixture-selected-mounted-after.sha256.txt")
    Write-HashArtifact (Join-Path $documentsDirectory $collisionName) (Join-Path $evidenceDirectory "fixture-collision-after.sha256.txt")
    Write-HashArtifact (Join-Path $documentsDirectory $saveName) (Join-Path $evidenceDirectory "fixture-save-mounted-after.sha256.txt")
    Write-HashArtifact $initialFixturePath (Join-Path $evidenceDirectory "fixture-initial-source-after.sha256.txt")
    Write-HashArtifact $selectedFixturePath (Join-Path $evidenceDirectory "fixture-selected-source-after.sha256.txt")

    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
        throw "Native picker probe did not write its manifest. Probe log: $probeLog"
    }
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    $manifest.results | Where-Object id -eq "visible-window-discovery" | ForEach-Object {
        Add-ResultEvidence $_ @("fixture-initial-source-before.sha256.txt", "fixture-initial-source-after.sha256.txt", "fixture-initial-mounted-before.sha256.txt", "fixture-initial-mounted-after.sha256.txt")
    }
    $manifest.results | Where-Object id -eq "open-pptx-selection-loads-package" | ForEach-Object {
        Add-ResultEvidence $_ @("fixture-selected-source-before.sha256.txt", "fixture-selected-source-after.sha256.txt", "fixture-selected-mounted-before.sha256.txt", "fixture-selected-mounted-after.sha256.txt")
    }
    $manifest.results | Where-Object id -eq "save-as-pptx-filter-selection-writes-package" | ForEach-Object {
        Add-ResultEvidence $_ @("fixture-save-mounted-after.sha256.txt")
    }
    $manifest.results | Where-Object id -eq "save-as-overwrite-cancel-preserves-collision" | ForEach-Object {
        Add-ResultEvidence $_ @("fixture-collision-before.sha256.txt", "fixture-collision-after.sha256.txt")
    }
    $manifest | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $manifestPath -Encoding utf8

    $hashPairs = @(
        @("fixture-initial-source-before.sha256.txt", "fixture-initial-source-after.sha256.txt"),
        @("fixture-initial-source-before.sha256.txt", "fixture-initial-mounted-before.sha256.txt"),
        @("fixture-initial-mounted-before.sha256.txt", "fixture-initial-mounted-after.sha256.txt"),
        @("fixture-selected-source-before.sha256.txt", "fixture-selected-source-after.sha256.txt"),
        @("fixture-selected-source-before.sha256.txt", "fixture-selected-mounted-before.sha256.txt"),
        @("fixture-selected-mounted-before.sha256.txt", "fixture-selected-mounted-after.sha256.txt"),
        @("fixture-collision-before.sha256.txt", "fixture-collision-after.sha256.txt")
    )
    foreach ($pair in $hashPairs) {
        if ((Read-HashArtifact (Join-Path $evidenceDirectory $pair[0])) -notmatch '^[0-9a-f]{64}$' -or
            (Read-HashArtifact (Join-Path $evidenceDirectory $pair[0])) -ne (Read-HashArtifact (Join-Path $evidenceDirectory $pair[1]))) {
            throw "Fixture hash postcondition failed: $($pair[0]) != $($pair[1])."
        }
    }
    $hashStatePairs = @(
        @("fixture-initial-mounted-after.sha256.txt", [string]$manifest.packageStates.initial.sha256),
        @("fixture-selected-mounted-after.sha256.txt", [string]$manifest.packageStates.openSelected.sha256),
        @("fixture-save-mounted-after.sha256.txt", [string]$manifest.packageStates.savePptx.sha256),
        @("fixture-collision-before.sha256.txt", [string]$manifest.packageStates.collisionBefore.sha256),
        @("fixture-collision-after.sha256.txt", [string]$manifest.packageStates.collisionAfter.sha256)
    )
    foreach ($pair in $hashStatePairs) {
        if ((Read-HashArtifact (Join-Path $evidenceDirectory $pair[0])) -ne $pair[1]) {
            throw "Package state SHA-256 does not match mounted artifact '$($pair[0])'."
        }
    }
    $manifest = Assert-ManifestContract -ManifestPath $manifestPath -EvidenceDirectory $evidenceDirectory
    Write-Host "Manifest contract validation: $($manifest.contractValidation.status)"
    Write-Host "Results: $($manifest.summary.passed) passed, $($manifest.summary.failed) failed, $($manifest.summary.total) total"
    Write-Host "Manifest: $manifestPath"
    Write-Host "Evidence: $evidenceDirectory"
    if ($probeExitCode -ne 0) { throw "Native picker probe exited with code $probeExitCode." }
} finally {
    if ($started -and -not $KeepContainer) {
        try {
            Invoke-External -FilePath "powershell.exe" -Arguments @(
                "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", $genericRunner,
                "-Action", "Stop", "-App", "FreeP", "-Port", "$Port", "-OutputDir", $resolvedOutputRoot
            )
        } catch {
            Write-Warning "Could not stop harness-owned FreeP container on port ${Port}: $($_.Exception.Message)"
        }
    } elseif ($started) {
        Write-Host "Container retained by request on port $Port."
    }
}
