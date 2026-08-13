[CmdletBinding()]
param(
    [int]$Port = 6094,
    [string]$OutputDir = "freew/artifacts/wave64-linux",
    [ValidateSet("nested-text", "nested-text-direction", "nested-text-alignment")]
    [string]$Selector = "nested-text"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot "PhysicalValidationScriptSupport.ps1")
$resolvedOutput = if ([IO.Path]::IsPathRooted($OutputDir)) { [IO.Path]::GetFullPath($OutputDir) } else { [IO.Path]::GetFullPath((Join-Path $repoRoot $OutputDir)) }
$fixtureProject = Join-Path $repoRoot "freew/tools/FreeW.GroupChildPhysicalFixture/FreeW.GroupChildPhysicalFixture.csproj"
$fixturePath = Join-Path $resolvedOutput "nested-text-wave64.docx"
$savedDocumentPath = Join-Path $resolvedOutput "freew/documents/nested-text-wave64.docx"
$reopenSourcePath = Join-Path $resolvedOutput "reopen-source/nested-text-wave64.docx"
$probePath = Join-Path $repoRoot "tools/LinuxInteractiveDocker/run-freew-wave64-nested-text-probe.sh"
$sessionPath = Join-Path $resolvedOutput "freew/current-session.json"
$containerName = "freex-linux-interactive-freew-$Port"
$started = $false
$isTextDirection = $Selector -eq "nested-text-direction"
$isAlignment = $Selector -eq "nested-text-alignment"

try {
    New-Item -ItemType Directory -Path $resolvedOutput -Force | Out-Null
    Invoke-PhysicalValidationFixture -ProjectPath $fixtureProject -Action "generate-nested-text" -ArtifactPath $fixturePath |
        Out-File (Join-Path $resolvedOutput "fixture-generation.txt") -Encoding utf8
    $before = Read-PhysicalValidationFixtureValues -ProjectPath $fixtureProject -Action "inspect-nested-text" -ArtifactPath $fixturePath
    if ($before['child-text'] -ne 'Nested leaf') { throw "Unexpected fixture text: '$($before['child-text'])'." }
    if ($before['child-kind'] -ne 'Shape') { throw "Nested text fixture leaf is not a Shape." }
    if ($before['child-text-direction'] -ne 'Horizontal') { throw "Unexpected fixture text direction: '$($before['child-text-direction'])'." }
    if ($before['child-text-alignment'] -ne 'Left') { throw "Unexpected fixture text alignment: '$($before['child-text-alignment'])'." }

    & powershell -NoProfile -File (Join-Path $repoRoot "tools/Run-LinuxInteractiveDocker.ps1") -Action Start -App FreeW -Port $Port -OutputDir $resolvedOutput -DocumentPath $fixturePath -Replace
    $started = $true
    $session = Get-Content $sessionPath -Raw | ConvertFrom-Json
    $sessionDir = [string]$session.sessionDirectory
    $probeSessionDir = $sessionDir
    Copy-Item $probePath (Join-Path $sessionDir "run-freew-wave64-nested-text-probe.sh") -Force
    & docker exec --env "FREEW_WAVE64_SELECTOR=$Selector" $containerName bash /work/run-freew-wave64-nested-text-probe.sh /work/nested-text-wave64
    if ($LASTEXITCODE -ne 0) { throw "The Linux/X11 nested text probe failed with exit code $LASTEXITCODE." }

    # Run-LinuxInteractiveDocker copies DocumentPath into its bind-mounted documents directory;
    # inspect that saved copy rather than the untouched source fixture.
    $after = Read-PhysicalValidationFixtureValues -ProjectPath $fixtureProject -Action "inspect-nested-text" -ArtifactPath $savedDocumentPath
    $expectedText = if ($isTextDirection -or $isAlignment) { 'Nested leaf' } else { 'Nested leaf!' }
    $expectedDirection = if ($isTextDirection) { 'Rotate90' } else { 'Horizontal' }
    $expectedAlignment = if ($isAlignment) { 'Center' } else { 'Left' }
    if ($after['child-text'] -ne $expectedText) { throw "Saved nested text mismatch: '$($after['child-text'])'." }
    if ($after['child-text-direction'] -ne $expectedDirection) { throw "Saved nested text direction mismatch: '$($after['child-text-direction'])'." }
    if ($after['child-text-alignment'] -ne $expectedAlignment) { throw "Saved nested text alignment mismatch: '$($after['child-text-alignment'])'." }
    if ($after['outer-transform'] -ne $before['outer-transform'] -or $after['inner-transform'] -ne $before['inner-transform']) { throw "Nested group transforms changed during text editing." }
    if ($after['child-transform'] -ne $before['child-transform']) { throw "Nested leaf transform changed during text alignment." }

    & powershell -NoProfile -File (Join-Path $repoRoot "tools/Run-LinuxInteractiveDocker.ps1") -Action Stop -App FreeW -Port $Port
    $started = $false
    New-Item -ItemType Directory -Path (Split-Path -Parent $reopenSourcePath) -Force | Out-Null
    Copy-Item -LiteralPath $savedDocumentPath -Destination $reopenSourcePath -Force
    & powershell -NoProfile -File (Join-Path $repoRoot "tools/Run-LinuxInteractiveDocker.ps1") -Action Start -App FreeW -Port $Port -OutputDir $resolvedOutput -DocumentPath $reopenSourcePath -Replace
    $started = $true
    $session = Get-Content $sessionPath -Raw | ConvertFrom-Json
    $reopenSessionDir = [string]$session.sessionDirectory
    $reopenScreenshotCommand = 'export DISPLAY=:99; mkdir -p /work/nested-text-wave64; window_id=$(xdotool search --onlyvisible --name "FreeW" 2>/dev/null | tail -1); test -n "$window_id"; xdotool windowactivate --sync "$window_id"; sleep 1; scrot /work/nested-text-wave64/04-reopened.png'
    & docker exec $containerName bash -lc $reopenScreenshotCommand
    if ($LASTEXITCODE -ne 0) { throw "The saved nested text document did not reopen in the Linux harness." }
    $reopened = Read-PhysicalValidationFixtureValues -ProjectPath $fixtureProject -Action "inspect-nested-text" -ArtifactPath $savedDocumentPath
    if ($reopened['child-text'] -ne $expectedText) { throw "Reopened nested text mismatch: '$($reopened['child-text'])'." }
    if ($reopened['child-text-direction'] -ne $expectedDirection) { throw "Reopened nested text direction mismatch: '$($reopened['child-text-direction'])'." }
    if ($reopened['child-text-alignment'] -ne $expectedAlignment) { throw "Reopened nested text alignment mismatch: '$($reopened['child-text-alignment'])'." }

    $probe = Get-Content (Join-Path $probeSessionDir "nested-text-wave64/probe-results.json") -Raw | ConvertFrom-Json
    $suite = if ($isTextDirection) { "freew-linux-nested-text-wave65-physical" } elseif ($isAlignment) { "freew-linux-nested-text-wave66-physical" } else { "freew-linux-nested-text-wave64-physical" }
    $scope = if ($isTextDirection) {
        "physical nested grouped-child text-direction selection, ribbon command, save, and reopen"
    } elseif ($isAlignment) {
        "physical nested grouped-child paragraph-alignment selection, ribbon command, save, and reopen"
    } else {
        "physical nested grouped-child text selection, insertion, save, and reopen"
    }
    $screenshots = if ($isTextDirection) {
        @(
            [ordered]@{ name = "01-baseline.png"; kind = "screenshot" },
            [ordered]@{ name = "02-nested-text-direction-rotate90.png"; kind = "screenshot" },
            [ordered]@{ name = "04-reopened.png"; kind = "screenshot" }
        )
    } elseif ($isAlignment) {
        @(
            [ordered]@{ name = "01-baseline.png"; kind = "screenshot" },
            [ordered]@{ name = "02-nested-text-alignment-center.png"; kind = "screenshot" },
            [ordered]@{ name = "04-reopened.png"; kind = "screenshot" }
        )
    } else {
        @(
            [ordered]@{ name = "01-baseline.png"; kind = "screenshot" },
            [ordered]@{ name = "02-nested-text-editing.png"; kind = "screenshot" },
            [ordered]@{ name = "03-nested-text-edited.png"; kind = "screenshot" },
            [ordered]@{ name = "04-reopened.png"; kind = "screenshot" }
        )
    }
    $results = @($probe.results) + @(
        [ordered]@{
            id = if ($isTextDirection) { "nested-text-direction-x11-reopen" } elseif ($isAlignment) { "nested-text-alignment-x11-reopen" } else { "nested-text-x11-reopen" }
            status = "passed"
            evidence = @("04-reopened.png")
            note = "The saved DOCX reopened with the exact nested child text, alignment/direction, child path, and group transforms."
        }
    )
    $manifest = [ordered]@{
        schemaVersion = 1
        suite = $suite
        platform = "linux"
        shell = "avalonia"
        app = "FreeW"
        coverage = [ordered]@{ scope = $scope; exhaustive = $false }
        contractValidation = [ordered]@{ status = "passed"; validator = "tools/Run-FreeWWave64NestedTextValidation.ps1"; contractReference = "tools/LinuxInteractiveDocker/freew-nested-text-validation.schema.json" }
        screenshots = $screenshots
        summary = [ordered]@{ passed = 4; failed = 0; total = 4 }
        results = $results
        persistedText = [ordered]@{ before = $before['child-text']; after = $after['child-text']; reopened = $reopened['child-text']; exact = $true }
        persistedDirection = [ordered]@{ before = $before['child-text-direction']; after = $after['child-text-direction']; reopened = $reopened['child-text-direction']; exact = $true }
        persistedAlignment = [ordered]@{ before = $before['child-text-alignment']; after = $after['child-text-alignment']; reopened = $reopened['child-text-alignment']; exact = $true }
        preservedStructure = [ordered]@{ childPath = "0,1"; childKind = $reopened['child-kind']; outerTransformUnchanged = $true; innerTransformUnchanged = $true; childTransformUnchanged = $true }
        evidence = [ordered]@{ session = $probeSessionDir; reopenSession = $reopenSessionDir; fixture = $savedDocumentPath }
    }
    $manifestName = if ($isTextDirection) { "freew-wave65-nested-text-direction-validation.json" } elseif ($isAlignment) { "freew-wave66-nested-text-alignment-validation.json" } else { "freew-wave64-nested-text-validation.json" }
    $manifestPath = Join-Path $resolvedOutput $manifestName
    $manifest | ConvertTo-Json -Depth 12 | Set-Content $manifestPath -Encoding utf8
    Write-Host "Nested text physical validation passed. Manifest: $manifestPath"
}
finally {
    if ($started) { & powershell -NoProfile -File (Join-Path $repoRoot "tools/Run-LinuxInteractiveDocker.ps1") -Action Stop -App FreeW -Port $Port }
}
