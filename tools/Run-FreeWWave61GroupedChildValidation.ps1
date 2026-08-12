[CmdletBinding()]
param(
    [int]$Port = 6091,
    [string]$OutputDir = "freew/artifacts/wave61-linux"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot "PhysicalValidationScriptSupport.ps1")
$resolvedOutput = if ([IO.Path]::IsPathRooted($OutputDir)) {
    [IO.Path]::GetFullPath($OutputDir)
} else {
    [IO.Path]::GetFullPath((Join-Path $repoRoot $OutputDir))
}
$fixtureProject = Join-Path $repoRoot "freew/tools/FreeW.GroupChildPhysicalFixture/FreeW.GroupChildPhysicalFixture.csproj"
$fixturePath = Join-Path $resolvedOutput "group-child-wave61.docx"
$probePath = Join-Path $repoRoot "tools/LinuxInteractiveDocker/run-freew-wave61-group-child-probe.sh"
$sessionPath = Join-Path $resolvedOutput "freew/current-session.json"
$containerName = "freex-linux-interactive-freew-$Port"
$started = $false

try {
    New-Item -ItemType Directory -Path $resolvedOutput -Force | Out-Null
    Invoke-PhysicalValidationFixture -ProjectPath $fixtureProject -Action "generate" -ArtifactPath $fixturePath |
        Out-File -LiteralPath (Join-Path $resolvedOutput "fixture-generation.txt") -Encoding utf8
    $before = Read-PhysicalValidationFixtureValues -ProjectPath $fixtureProject -Action "inspect" -ArtifactPath $fixturePath

    & powershell -NoProfile -File (Join-Path $repoRoot "tools/Run-LinuxInteractiveDocker.ps1") `
        -Action Start -App FreeW -Port $Port -OutputDir $resolvedOutput -DocumentPath $fixturePath `
        -Replace
    $started = $true

    $session = Get-Content -LiteralPath $sessionPath -Raw | ConvertFrom-Json
    $sessionDir = [string]$session.sessionDirectory
    Copy-Item -LiteralPath $probePath -Destination (Join-Path $sessionDir "run-freew-wave61-group-child-probe.sh") -Force
    & docker exec $containerName bash /work/run-freew-wave61-group-child-probe.sh /work/group-child-wave61
    if ($LASTEXITCODE -ne 0) {
        throw "The Linux/X11 group-child probe failed with exit code $LASTEXITCODE."
    }

    $savedPath = Join-Path $resolvedOutput "freew/documents/group-child-wave61.docx"
    if (-not (Test-Path -LiteralPath $savedPath -PathType Leaf)) {
        throw "The FreeW document was not persisted at '$savedPath'."
    }
    $after = Read-PhysicalValidationFixtureValues -ProjectPath $fixtureProject -Action "inspect" -ArtifactPath $savedPath
    $beforeOffset = $before['child-offset-pt'].Split(',') | ForEach-Object { [double]::Parse($_, [Globalization.CultureInfo]::InvariantCulture) }
    $afterOffset = $after['child-offset-pt'].Split(',') | ForEach-Object { [double]::Parse($_, [Globalization.CultureInfo]::InvariantCulture) }
    $beforeSize = $before['child-size-pt'].Split(',') | ForEach-Object { [double]::Parse($_, [Globalization.CultureInfo]::InvariantCulture) }
    $afterSize = $after['child-size-pt'].Split(',') | ForEach-Object { [double]::Parse($_, [Globalization.CultureInfo]::InvariantCulture) }
    if ($after['group-offset-pt'] -ne $before['group-offset-pt'] -or $after['group-size-pt'] -ne $before['group-size-pt']) {
        throw "Physical edit changed the owning group geometry: '$($after['group-offset-pt'])' '$($after['group-size-pt'])'."
    }
    if ([Math]::Abs($afterOffset[0] - $beforeOffset[0]) -lt 0.01 -and [Math]::Abs($afterOffset[1] - $beforeOffset[1]) -lt 0.01) {
        throw "Physical move did not persist a child-local offset change."
    }
    if ($afterSize[0] -le $beforeSize[0] -or $afterSize[1] -le $beforeSize[1]) {
        throw "Physical resize did not grow both child dimensions."
    }

    $probe = Get-Content -LiteralPath (Join-Path $sessionDir "group-child-wave61/probe-results.json") -Raw | ConvertFrom-Json
    $manifest = [ordered]@{
        schemaVersion = 1
        suite = "freew-linux-group-child-wave61-physical"
        platform = "linux"
        app = "FreeW"
        shell = "avalonia"
        results = @($probe.results)
        summary = [ordered]@{ status = "passed"; passed = @($probe.results).Count + 1; failed = 0 }
        persistedGeometry = [ordered]@{
            exact = $true
            source = "FreeW.Core.IO DocxReader inspect"
            document = $savedPath
            before = [ordered]@{ groupOffsetPt = $before['group-offset-pt']; groupSizePt = $before['group-size-pt']; childOffsetPt = $before['child-offset-pt']; childSizePt = $before['child-size-pt'] }
            after = [ordered]@{ groupOffsetPt = $after['group-offset-pt']; groupSizePt = $after['group-size-pt']; childOffsetPt = $after['child-offset-pt']; childSizePt = $after['child-size-pt'] }
            groupUnchanged = $true
            childMoved = $true
            childResized = $true
        }
        selectionPostcondition = $probe.selectionPostcondition
        evidence = [ordered]@{
            session = $sessionDir
            baseline = (Join-Path $sessionDir "group-child-wave61/01-baseline.png")
            selected = (Join-Path $sessionDir "group-child-wave61/02-child-selected.png")
            moved = (Join-Path $sessionDir "group-child-wave61/03-child-moved.png")
            resizedSelected = (Join-Path $sessionDir "group-child-wave61/04-child-resized-selected.png")
            fixture = $fixturePath
        }
    }
    $manifest | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $resolvedOutput "freew-wave61-group-child-validation.json") -Encoding utf8
    $before | Out-File -LiteralPath (Join-Path $resolvedOutput "inspect-before.txt") -Encoding utf8
    $after | Out-File -LiteralPath (Join-Path $resolvedOutput "inspect-after.txt") -Encoding utf8
    Write-Host "Wave 61 physical validation passed. Manifest: $(Join-Path $resolvedOutput 'freew-wave61-group-child-validation.json')"
} finally {
    if ($started) {
        & powershell -NoProfile -File (Join-Path $repoRoot "tools/Run-LinuxInteractiveDocker.ps1") -Action Stop -App FreeW -Port $Port
    }
}
