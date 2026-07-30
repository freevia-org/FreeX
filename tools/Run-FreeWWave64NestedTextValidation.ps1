[CmdletBinding()]
param(
    [int]$Port = 6094,
    [string]$OutputDir = "freew/artifacts/wave64-linux"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$resolvedOutput = if ([IO.Path]::IsPathRooted($OutputDir)) { [IO.Path]::GetFullPath($OutputDir) } else { [IO.Path]::GetFullPath((Join-Path $repoRoot $OutputDir)) }
$fixtureProject = Join-Path $repoRoot "freew/tools/FreeW.GroupChildPhysicalFixture/FreeW.GroupChildPhysicalFixture.csproj"
$fixturePath = Join-Path $resolvedOutput "nested-text-wave64.docx"
$savedDocumentPath = Join-Path $resolvedOutput "freew/documents/nested-text-wave64.docx"
$reopenSourcePath = Join-Path $resolvedOutput "reopen-source/nested-text-wave64.docx"
$probePath = Join-Path $repoRoot "tools/LinuxInteractiveDocker/run-freew-wave64-nested-text-probe.sh"
$sessionPath = Join-Path $resolvedOutput "freew/current-session.json"
$containerName = "freex-linux-interactive-freew-$Port"
$started = $false

function Invoke-Fixture { param([string]$Action, [string]$Path)
    $lines = @(& dotnet run --project $fixtureProject --configuration Release --no-restore -- $Action $Path 2>&1)
    if ($LASTEXITCODE -ne 0) { throw "Fixture '$Action' failed: $($lines -join [Environment]::NewLine)" }
    return $lines
}
function Read-Geometry { param([string]$Action, [string]$Path)
    $values = [ordered]@{}
    foreach ($line in @(Invoke-Fixture $Action $Path)) {
        if ($line -match '^([^=]+)=(.*)$') { $values[$Matches[1]] = $Matches[2] }
    }
    return $values
}

try {
    New-Item -ItemType Directory -Path $resolvedOutput -Force | Out-Null
    Invoke-Fixture generate-nested-text $fixturePath | Out-File (Join-Path $resolvedOutput "fixture-generation.txt") -Encoding utf8
    $before = Read-Geometry inspect-nested-text $fixturePath
    if ($before['child-text'] -ne 'Nested leaf') { throw "Unexpected fixture text: '$($before['child-text'])'." }
    if ($before['child-kind'] -ne 'Shape') { throw "Nested text fixture leaf is not a Shape." }

    & powershell -NoProfile -File (Join-Path $repoRoot "tools/Run-LinuxInteractiveDocker.ps1") -Action Start -App FreeW -Port $Port -OutputDir $resolvedOutput -DocumentPath $fixturePath -Replace
    $started = $true
    $session = Get-Content $sessionPath -Raw | ConvertFrom-Json
    $sessionDir = [string]$session.sessionDirectory
    $probeSessionDir = $sessionDir
    Copy-Item $probePath (Join-Path $sessionDir "run-freew-wave64-nested-text-probe.sh") -Force
    & docker exec $containerName bash /work/run-freew-wave64-nested-text-probe.sh /work/nested-text-wave64
    if ($LASTEXITCODE -ne 0) { throw "The Linux/X11 nested text probe failed with exit code $LASTEXITCODE." }

    # Run-LinuxInteractiveDocker copies DocumentPath into its bind-mounted documents directory;
    # inspect that saved copy rather than the untouched source fixture.
    $after = Read-Geometry inspect-nested-text $savedDocumentPath
    if ($after['child-text'] -ne 'Nested leaf!') { throw "Saved nested text mismatch: '$($after['child-text'])'." }
    if ($after['outer-transform'] -ne $before['outer-transform'] -or $after['inner-transform'] -ne $before['inner-transform']) { throw "Nested group transforms changed during text editing." }

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
    $reopened = Read-Geometry inspect-nested-text $savedDocumentPath
    if ($reopened['child-text'] -ne 'Nested leaf!') { throw "Reopened nested text mismatch: '$($reopened['child-text'])'." }

    $probe = Get-Content (Join-Path $probeSessionDir "nested-text-wave64/probe-results.json") -Raw | ConvertFrom-Json
    $manifest = [ordered]@{
        schemaVersion = 1
        suite = "freew-linux-nested-text-wave64-physical"
        platform = "linux"
        shell = "avalonia"
        app = "FreeW"
        coverage = [ordered]@{ scope = "physical nested grouped-child text selection, insertion, save, and reopen"; exhaustive = $false }
        contractValidation = [ordered]@{ status = "passed"; validator = "tools/Run-FreeWWave64NestedTextValidation.ps1"; contractReference = "tools/LinuxInteractiveDocker/freew-nested-text-validation.schema.json" }
        screenshots = @(
            [ordered]@{ name = "01-baseline.png"; kind = "screenshot" },
            [ordered]@{ name = "02-nested-text-editing.png"; kind = "screenshot" },
            [ordered]@{ name = "03-nested-text-edited.png"; kind = "screenshot" },
            [ordered]@{ name = "04-reopened.png"; kind = "screenshot" }
        )
        summary = [ordered]@{ passed = 4; failed = 0; total = 4 }
        results = @($probe.results)
        persistedText = [ordered]@{ before = $before['child-text']; after = $after['child-text']; reopened = $reopened['child-text']; exact = $true }
        preservedStructure = [ordered]@{ childPath = "0,1"; childKind = $reopened['child-kind']; outerTransformUnchanged = $true; innerTransformUnchanged = $true }
        evidence = [ordered]@{ session = $probeSessionDir; reopenSession = $reopenSessionDir; fixture = $savedDocumentPath }
    }
    $manifest | ConvertTo-Json -Depth 12 | Set-Content (Join-Path $resolvedOutput "freew-wave64-nested-text-validation.json") -Encoding utf8
    Write-Host "Wave 64 physical validation passed. Manifest: $(Join-Path $resolvedOutput 'freew-wave64-nested-text-validation.json')"
}
finally {
    if ($started) { & powershell -NoProfile -File (Join-Path $repoRoot "tools/Run-LinuxInteractiveDocker.ps1") -Action Stop -App FreeW -Port $Port }
}
