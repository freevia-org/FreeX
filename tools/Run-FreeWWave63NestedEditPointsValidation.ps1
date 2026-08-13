[CmdletBinding()]
param(
    [int]$Port = 6093,
    [string]$OutputDir = "freew/artifacts/wave63-linux"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot "PhysicalValidationScriptSupport.ps1")
$resolvedOutput = if ([IO.Path]::IsPathRooted($OutputDir)) { [IO.Path]::GetFullPath($OutputDir) } else { [IO.Path]::GetFullPath((Join-Path $repoRoot $OutputDir)) }
$fixtureProject = Join-Path $repoRoot "freew/tools/FreeW.GroupChildPhysicalFixture/FreeW.GroupChildPhysicalFixture.csproj"
$fixturePath = Join-Path $resolvedOutput "nested-edit-points-wave63.docx"
$probePath = Join-Path $repoRoot "tools/LinuxInteractiveDocker/run-freew-wave63-nested-edit-points-probe.sh"
$sessionPath = Join-Path $resolvedOutput "freew/current-session.json"
$containerName = "freex-linux-interactive-freew-$Port"
$started = $false

try {
    New-Item -ItemType Directory -Path $resolvedOutput -Force | Out-Null
    Invoke-PhysicalValidationFixture -ProjectPath $fixtureProject -Action "generate-nested" -ArtifactPath $fixturePath |
        Out-File (Join-Path $resolvedOutput "fixture-generation.txt") -Encoding utf8
    $before = Read-PhysicalValidationFixtureValues -ProjectPath $fixtureProject -Action "inspect-nested" -ArtifactPath $fixturePath
    if ($before['child-point-0'] -ne '3600,1800') { throw "Fixture child point was not deterministic: '$($before['child-point-0'])'." }
    if ([string]::IsNullOrWhiteSpace($before['child-points'])) { throw "Fixture had no nested leaf geometry points." }

    & powershell -NoProfile -File (Join-Path $repoRoot "tools/Run-LinuxInteractiveDocker.ps1") -Action Start -App FreeW -Port $Port -OutputDir $resolvedOutput -DocumentPath $fixturePath -Replace
    $started = $true
    $session = Get-Content $sessionPath -Raw | ConvertFrom-Json
    $sessionDir = [string]$session.sessionDirectory
    Copy-Item $probePath (Join-Path $sessionDir "run-freew-wave63-nested-edit-points-probe.sh") -Force
    & docker exec $containerName bash /work/run-freew-wave63-nested-edit-points-probe.sh /work/nested-edit-points-wave63
    if ($LASTEXITCODE -ne 0) { throw "The Linux/X11 nested edit-points probe failed with exit code $LASTEXITCODE." }

    $savedPath = Join-Path $resolvedOutput "freew/documents/nested-edit-points-wave63.docx"
    if (-not (Test-Path $savedPath -PathType Leaf)) { throw "The FreeW document was not persisted at '$savedPath'." }
    $after = Read-PhysicalValidationFixtureValues -ProjectPath $fixtureProject -Action "inspect-nested" -ArtifactPath $savedPath
    if ([string]::IsNullOrWhiteSpace($after['child-points'])) { throw "Saved DOCX has no nested leaf geometry points." }
    if ($after['child-points'] -eq $before['child-points']) { throw "Nested edit-point drag did not change any saved leaf point." }
    if ($after['outer-transform'] -ne $before['outer-transform'] -or $after['inner-transform'] -ne $before['inner-transform']) { throw "Nested group transforms changed during edit-point drag." }

    $probe = Get-Content (Join-Path $sessionDir "nested-edit-points-wave63/probe-results.json") -Raw | ConvertFrom-Json
    $manifest = [ordered]@{
        schemaVersion = 1
        suite = "freew-linux-nested-edit-points-wave63-physical"
        platform = "linux"
        app = "FreeW"
        shell = "avalonia"
        results = @($probe.results)
        summary = [ordered]@{ status = "passed"; passed = @($probe.results).Count + 1; failed = 0 }
        persistedGeometry = [ordered]@{ exact = $true; before = $before; after = $after; nestedLeafPointsChanged = $true; outerTransformUnchanged = $true; innerTransformUnchanged = $true }
        selectionPostcondition = $probe.selectionPostcondition
        evidence = [ordered]@{ session = $sessionDir; baseline = (Join-Path $sessionDir "nested-edit-points-wave63/01-baseline.png"); editPoints = (Join-Path $sessionDir "nested-edit-points-wave63/02-nested-leaf-edit-points.png"); dragged = (Join-Path $sessionDir "nested-edit-points-wave63/03-nested-leaf-edit-point-dragged.png"); fixture = $fixturePath }
    }
    $manifest | ConvertTo-Json -Depth 12 | Set-Content (Join-Path $resolvedOutput "freew-wave63-nested-edit-points-validation.json") -Encoding utf8
    $before | Out-File (Join-Path $resolvedOutput "inspect-before.txt") -Encoding utf8
    $after | Out-File (Join-Path $resolvedOutput "inspect-after.txt") -Encoding utf8
    Write-Host "Wave 63 physical validation passed. Manifest: $(Join-Path $resolvedOutput 'freew-wave63-nested-edit-points-validation.json')"
}
finally {
    if ($started) { & powershell -NoProfile -File (Join-Path $repoRoot "tools/Run-LinuxInteractiveDocker.ps1") -Action Stop -App FreeW -Port $Port }
}
