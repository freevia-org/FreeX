<##
.SYNOPSIS
  Runs the dedicated physical FreeP SmartArt outline authoring evidence lane.

.DESCRIPTION
  Exercises SmartArt text-pane text replacement, Add sibling, save, and
  fresh-process reopen workflow on Linux/X11. The two app sessions are
  intentionally isolated so the final row proves a real package reopen.
#>
[CmdletBinding()]
param(
    [ValidateRange(1024, 65535)][int]$Port = 6098,
    [ValidateRange(640, 7680)][int]$Width = 1280,
    [ValidateRange(480, 4320)][int]$Height = 820,
    [ValidateRange(72, 240)][int]$Dpi = 96,
    [ValidateSet("2g", "4g", "6g", "8g")][string]$MemoryLimit = "4g",
    [string]$OutputDir = "artifacts/freep-smartart-authoring",
    [string]$PublishDir = "",
    [switch]$SkipPublish,
    [switch]$SkipImageBuild,
    [switch]$Replace
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot "VisualEvidenceScriptSupport.ps1")
$resolvedOutputRoot = Resolve-VisualEvidenceOutputDirectory -OutputDirectory $OutputDir -RepoRoot $repoRoot
$fixturePath = Join-Path $repoRoot "tools/FreeP.RenderCompare/corpus/14-smartart-live.pptx"
$genericRunner = Join-Path $PSScriptRoot "Run-LinuxInteractiveDocker.ps1"
$probeSource = Join-Path $PSScriptRoot "LinuxInteractiveDocker/run-freep-smartart-authoring-probe.sh"
$schemaPath = Join-Path $PSScriptRoot "LinuxInteractiveDocker/freep-smartart-authoring-validation.schema.json"
$fixtureName = Split-Path -Leaf $fixturePath
$evidenceDirectory = Join-Path $resolvedOutputRoot "freep/smartart-authoring"
$requiredIds = @(
    "visible-window-discovery",
    "smartart-outline-add-sibling",
    "smartart-outline-apply-text",
    "smartart-outline-apply-undo-redo",
    "smartart-outline-save",
    "smartart-outline-reopen"
)

function Get-Session {
    $path = Join-Path $resolvedOutputRoot "freep/current-session.json"
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Interactive runner did not write session metadata: $path"
    }
    Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
}

function Start-Session {
    param([Parameter(Mandatory = $true)][string]$DocumentPath, [switch]$ReusePublishedImage)
    $args = @(
        "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", $genericRunner,
        "-Action", "Start", "-App", "FreeP", "-Port", "$Port", "-Width", "$Width",
        "-Height", "$Height", "-Dpi", "$Dpi", "-MemoryLimit", $MemoryLimit,
        "-OutputDir", $resolvedOutputRoot, "-DocumentPath", $DocumentPath,
        "-Host", "Validation",
        "-AppArgument", "--physical-smartart-text-pane-fixture"
    )
    if (-not [string]::IsNullOrWhiteSpace($PublishDir)) { $args += @("-PublishDir", $PublishDir) }
    if ($SkipPublish -or $ReusePublishedImage) { $args += "-SkipPublish" }
    if ($SkipImageBuild -or $ReusePublishedImage) { $args += "-SkipImageBuild" }
    if ($Replace) { $args += "-Replace" }
    Invoke-VisualEvidenceProcess -FilePath "powershell.exe" -Arguments $args -WorkingDirectory $repoRoot
    Get-Session
}

function Invoke-Probe {
    param(
        [Parameter(Mandatory = $true)]$Session,
        [Parameter(Mandatory = $true)][ValidateSet("first", "reopen")][string]$Phase
    )
    Copy-Item -LiteralPath $probeSource -Destination (Join-Path ([IO.Path]::GetFullPath([string]$Session.sessionDirectory)) "run-freep-smartart-authoring-probe.sh") -Force
    $probeLog = Join-Path $resolvedOutputRoot "freep/smartart-authoring-$Phase.log"
    $dockerArgs = @(
        "exec", "--env", "FREEP_DOCUMENT_PATH=/documents/$fixtureName",
        "--env", "FREEP_EXPECTED_DOCUMENT_NAME=$fixtureName",
        "--env", "FREEP_EXPECTED_WINDOW_PATTERN=FreeP",
        "--env", "FREEP_SMARTART_PROBE_PHASE=$Phase",
        "--env", "FREEP_SCREEN_WIDTH=$Width",
        "--env", "FREEP_SCREEN_HEIGHT=$Height",
        "--env", "FREEP_SCREEN_DPI=$Dpi",
        [string]$Session.containerName, "bash", "/work/run-freep-smartart-authoring-probe.sh",
        "/work/freep-smartart-authoring"
    )
    Push-Location $repoRoot
    try {
        $probeOutput = @(& docker @dockerArgs 2>&1)
        $probeExitCode = $LASTEXITCODE
    } finally { Pop-Location }
    if ($probeOutput.Count -gt 0) { $probeOutput | Set-Content -LiteralPath $probeLog -Encoding utf8 }
    if ($probeExitCode -ne 0) { throw "SmartArt $Phase probe failed with exit code $probeExitCode." }

    $phaseDirectory = Join-Path $evidenceDirectory $Phase
    New-Item -ItemType Directory -Path $phaseDirectory -Force | Out-Null
    Push-Location $repoRoot
    try {
        & docker cp "$($Session.containerName):/work/freep-smartart-authoring/." $phaseDirectory
        if ($LASTEXITCODE -ne 0) { throw "Could not copy SmartArt $Phase evidence." }
    } finally { Pop-Location }
    return Get-Content -LiteralPath (Join-Path $phaseDirectory "results.json") -Raw | ConvertFrom-Json
}

function Stop-Session {
    try {
        Invoke-VisualEvidenceProcess -FilePath "powershell.exe" -Arguments @(
            "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", $genericRunner,
            "-Action", "Stop", "-App", "FreeP", "-Port", "$Port", "-OutputDir", $resolvedOutputRoot) -WorkingDirectory $repoRoot
    } catch { Write-Warning "Could not stop harness-owned FreeP container: $($_.Exception.Message)" }
}

function Assert-EvidenceReference {
    param([Parameter(Mandatory = $true)][string]$Reference)
    if ([string]::IsNullOrWhiteSpace($Reference) -or
        [IO.Path]::IsPathRooted($Reference) -or
        $Reference -match '(^|[\\/])\.\.([\\/]|$)') {
        throw "Evidence reference must be a non-rooted relative path without parent traversal: '$Reference'."
    }

    $root = [IO.Path]::GetFullPath($evidenceDirectory).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    $path = [IO.Path]::GetFullPath((Join-Path $evidenceDirectory $Reference))
    if (-not $path.StartsWith($root, [StringComparison]::OrdinalIgnoreCase) -or
        -not (Test-Path -LiteralPath $path -PathType Leaf) -or
        (Get-Item -LiteralPath $path).Length -le 0) {
        throw "Evidence reference is missing, empty, or outside the combined evidence directory: '$Reference'."
    }
}

function Assert-ManifestContract {
    param([Parameter(Mandatory = $true)]$Manifest)

    if ($Manifest.schemaVersion -ne 1 -or
        $Manifest.suite -ne "freep-linux-smartart-authoring-physical" -or
        $Manifest.platform -ne "linux" -or
        $Manifest.shell -ne "avalonia" -or
        $Manifest.app -ne "FreeP" -or
        $Manifest.baseline -ne $false -or
        $Manifest.appSurface -ne "smartart-text-pane-outline") {
        throw "SmartArt physical manifest header does not satisfy the bounded contract."
    }

    $expectedScope = "physical FreeP SmartArt text-pane text replacement, add-sibling, apply undo/redo, save, and reopen"
    $expectedSessionBoundary = "two fresh harness-owned FreeP processes; first saves, second reopens the copied mounted package"
    if ($Manifest.coverage.scope -ne $expectedScope -or
        $Manifest.coverage.exhaustive -ne $false -or
        $Manifest.window.pattern -ne $fixtureName -or
        $Manifest.window.visible -ne $true -or
        $Manifest.parameters.width -ne $Width -or
        $Manifest.parameters.height -ne $Height -or
        $Manifest.parameters.dpi -ne $Dpi -or
        $Manifest.parameters.fixture -ne $fixtureName -or
        $Manifest.sessionBoundary -ne $expectedSessionBoundary) {
        throw "SmartArt physical manifest dimensions, fixture, coverage, or session boundary are invalid."
    }

    if ($Manifest.semanticReadback.tool -ne "xclip" -or
        $Manifest.semanticReadback.selection -ne "clipboard" -or
        ([string]::Join("|", @($Manifest.semanticReadback.transcripts)) -ne "smartart-outline-apply-text|smartart-outline-apply-undo-redo|smartart-outline-reopen") -or
        ([string]::Join("|", @($Manifest.semanticReadback.packageParts)) -ne "ppt/diagrams/data1.xml|ppt/diagrams/drawing1.xml")) {
        throw "SmartArt physical manifest semantic readback contract is invalid."
    }

    $results = @($Manifest.results)
    if ($results.Count -ne $requiredIds.Count -or
        $Manifest.summary.passed -ne $requiredIds.Count -or
        $Manifest.summary.failed -ne 0 -or
        $Manifest.summary.total -ne $requiredIds.Count) {
        throw "SmartArt physical manifest summary counts are invalid."
    }
    $actualIds = @($results | ForEach-Object { [string]$_.id })
    if ([string]::Join("|", $actualIds) -ne [string]::Join("|", $requiredIds) -or
        @($actualIds | Sort-Object -Unique).Count -ne $requiredIds.Count) {
        throw "SmartArt physical manifest required IDs are not present exactly once and in contract order."
    }
    foreach ($result in $results) {
        if ($result.status -ne "passed" -or
            $result.category -ne "physical-x11-smartart-authoring" -or
            $result.evidenceLevel -ne "physical-x11-input" -or
            @($result.evidence).Count -lt 1) {
            throw "SmartArt physical result '$($result.id)' is not passed with bounded evidence."
        }
        foreach ($reference in @($result.evidence)) {
            Assert-EvidenceReference -Reference ([string]$reference)
        }
    }
    foreach ($screenshot in @($Manifest.screenshots)) {
        if ($screenshot.kind -ne "screenshot") { throw "SmartArt physical screenshot kind is invalid." }
        Assert-EvidenceReference -Reference ([string]$screenshot.name)
    }
    if ($Manifest.contractValidation.status -ne "pending" -or
        $Manifest.contractValidation.validator -ne "tools/Run-FreePSmartArtAuthoringValidation.ps1" -or
        $Manifest.contractValidation.contractReference -ne "tools/LinuxInteractiveDocker/freep-smartart-authoring-validation.schema.json") {
        throw "SmartArt physical manifest must be pending while its bounded contract is validated."
    }
}

function Merge-Results {
    param([Parameter(Mandatory = $true)]$First, [Parameter(Mandatory = $true)]$Reopen)
    $byId = @{}
    $rowPhase = @{}
    foreach ($phaseManifest in @(
        [pscustomobject]@{ Name = "first"; Value = $First },
        [pscustomobject]@{ Name = "reopen"; Value = $Reopen }
    )) {
        foreach ($row in @($phaseManifest.Value.results)) {
            if ($null -eq $row) { continue }
            $row.evidence = @($row.evidence | ForEach-Object { "$($phaseManifest.Name)/$_" })
            if (-not $byId.ContainsKey([string]$row.id) -or $row.status -eq "passed") {
                $byId[[string]$row.id] = $row
                $rowPhase[[string]$row.id] = $phaseManifest.Name
            }
        }
    }
    foreach ($row in @($byId.Values)) {
        if ($null -eq $row) { continue }
        if (-not $rowPhase.ContainsKey([string]$row.id)) { throw "Missing evidence phase for result '$($row.id)'." }
    }
    foreach ($id in $requiredIds) {
        if (-not $byId.ContainsKey($id) -or $byId[$id].status -ne "passed") {
            throw "SmartArt physical contract row '$id' did not pass in the combined evidence."
        }
    }
    $screenshots = @()
    foreach ($phase in @("first", "reopen")) {
        $dir = Join-Path $evidenceDirectory $phase
        foreach ($file in Get-ChildItem -LiteralPath $dir -Filter *.png -File) {
            $screenshots += [ordered]@{ name = "$phase/$($file.Name)"; kind = "screenshot" }
        }
    }
    $manifest = [ordered]@{
        schemaVersion = 1
        suite = "freep-linux-smartart-authoring-physical"
        platform = "linux"
        shell = "avalonia"
        app = "FreeP"
        baseline = $false
        appSurface = "smartart-text-pane-outline"
        window = [ordered]@{ id = "combined-x11-owner"; title = "FreeP $fixtureName"; pattern = $fixtureName; visible = $true }
        parameters = [ordered]@{ width = $Width; height = $Height; dpi = $Dpi; fixture = $fixtureName }
        coverage = [ordered]@{ scope = "physical FreeP SmartArt text-pane text replacement, add-sibling, apply undo/redo, save, and reopen"; exhaustive = $false }
        semanticReadback = [ordered]@{ tool = "xclip"; selection = "clipboard"; transcripts = @("smartart-outline-apply-text", "smartart-outline-apply-undo-redo", "smartart-outline-reopen"); packageParts = @("ppt/diagrams/data1.xml", "ppt/diagrams/drawing1.xml") }
        contractValidation = [ordered]@{ status = "pending"; validator = "tools/Run-FreePSmartArtAuthoringValidation.ps1"; contractReference = "tools/LinuxInteractiveDocker/freep-smartart-authoring-validation.schema.json" }
        screenshots = $screenshots
        summary = [ordered]@{ passed = $requiredIds.Count; failed = 0; total = $requiredIds.Count }
        results = @($requiredIds | ForEach-Object { $byId[$_] })
        sessionBoundary = "two fresh harness-owned FreeP processes; first saves, second reopens the copied mounted package"
    }
    $schema = Get-Content -LiteralPath $schemaPath -Raw | ConvertFrom-Json
    if ($schema.'$schema' -notmatch "json-schema.org" -or
        $schema.suite -ne $null -or
        $schema.properties.summary.properties.total.const -ne $requiredIds.Count -or
        $schema.properties.results.minItems -ne $requiredIds.Count -or
        $schema.properties.results.maxItems -ne $requiredIds.Count) {
        throw "SmartArt validation schema does not describe the bounded six-row contract."
    }
    $pendingPath = Join-Path $evidenceDirectory ".results.pending.json"
    $manifestPath = Join-Path $evidenceDirectory "results.json"
    $manifest | ConvertTo-Json -Depth 16 | Set-Content -LiteralPath $pendingPath -Encoding utf8
    $pendingManifest = Get-Content -LiteralPath $pendingPath -Raw | ConvertFrom-Json
    Assert-ManifestContract -Manifest $pendingManifest
    $pendingManifest.contractValidation.status = "passed"
    $pendingManifest | ConvertTo-Json -Depth 16 | Set-Content -LiteralPath $manifestPath -Encoding utf8
    Remove-Item -LiteralPath $pendingPath -Force
    return $manifestPath
}

if (-not (Test-Path -LiteralPath $fixturePath -PathType Leaf)) { throw "Fixture was not found: $fixturePath" }
if (-not (Test-Path -LiteralPath $probeSource -PathType Leaf)) { throw "Probe was not found: $probeSource" }
if (-not (Test-Path -LiteralPath $schemaPath -PathType Leaf)) { throw "Schema was not found: $schemaPath" }
New-Item -ItemType Directory -Path $evidenceDirectory -Force | Out-Null
$sourceBefore = (Get-FileHash -LiteralPath $fixturePath -Algorithm SHA256).Hash.ToLowerInvariant()
$firstResult = $null
$reopenResult = $null
$started = $false
try {
    $started = $true
    $firstSession = Start-Session -DocumentPath $fixturePath
    $firstResult = Invoke-Probe -Session $firstSession -Phase first
    $mountedDocument = Join-Path $resolvedOutputRoot "freep/documents/$fixtureName"
    if (-not (Test-Path -LiteralPath $mountedDocument -PathType Leaf)) { throw "The first session did not produce a mounted saved package." }
    $reopenFixtureDirectory = Join-Path $resolvedOutputRoot "reopen-fixture"
    New-Item -ItemType Directory -Path $reopenFixtureDirectory -Force | Out-Null
    $reopenFixture = Join-Path $reopenFixtureDirectory $fixtureName
    Copy-Item -LiteralPath $mountedDocument -Destination $reopenFixture -Force
    Stop-Session
    $started = $false

    $started = $true
    $secondSession = Start-Session -DocumentPath $reopenFixture -ReusePublishedImage
    $reopenResult = Invoke-Probe -Session $secondSession -Phase reopen
    Stop-Session
    $started = $false

    $manifestPath = Merge-Results -First $firstResult -Reopen $reopenResult
    $sourceAfter = (Get-FileHash -LiteralPath $fixturePath -Algorithm SHA256).Hash.ToLowerInvariant()
    $sourceBefore | Set-Content -LiteralPath (Join-Path $evidenceDirectory "source-before.sha256.txt") -Encoding ascii
    $sourceAfter | Set-Content -LiteralPath (Join-Path $evidenceDirectory "source-after.sha256.txt") -Encoding ascii
    if ($sourceBefore -ne $sourceAfter) { throw "The immutable corpus fixture changed during physical validation." }
    Write-Host "SmartArt physical validation passed: $manifestPath"
} finally {
    if ($started) { Stop-Session }
}
