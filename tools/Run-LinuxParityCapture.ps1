<#
.SYNOPSIS
  Run one bounded current-source Avalonia parity capture in an owned Docker container.

.DESCRIPTION
  Publishes the Avalonia parity capture host for linux-x64 unless -SkipPublish is supplied, then runs it as
  the foreground child of xvfb-run. The supplied or generated exact container name is
  used for cleanup so the command can stop and remove only this invocation's container.
  The command fails unless the requested manifest row is captured and its PNG is a
  nonblank PNG with the requested dimensions.
#>
[CmdletBinding()]
param(
    [string]$OutputDir = "artifacts/avalonia-parity-capture",
    [string]$PublishDir = "",
    [string]$ContainerName = "",
    [string]$SurfaceId = "dialog.GoalSeekStatus",
    [string]$Image = "freex-linux-interactive:ubuntu24.04",
    [int]$Width = 380,
    [int]$Height = 190,
    [int]$TimeoutSeconds = 120,
    [switch]$SkipPublish
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot "tools/FreeX.ParityCapture.Avalonia/FreeX.ParityCapture.Avalonia.csproj"
$resolvedOutputDir = [IO.Path]::GetFullPath((Join-Path $repoRoot $OutputDir))
$resolvedPublishDir = if ([string]::IsNullOrWhiteSpace($PublishDir)) {
    Join-Path $env:TEMP "FreeX-wave125-parity-publish"
} elseif ([IO.Path]::IsPathRooted($PublishDir)) {
    [IO.Path]::GetFullPath($PublishDir)
} else {
    [IO.Path]::GetFullPath((Join-Path $repoRoot $PublishDir))
}
$name = if ([string]::IsNullOrWhiteSpace($ContainerName)) {
    "freex-wave125-goalseek-capture-$([guid]::NewGuid().ToString('N'))"
} else {
    $ContainerName
}

if ($name -notmatch '^[a-zA-Z0-9][a-zA-Z0-9_.-]+$') {
    throw "ContainerName must be a unique Docker-safe name."
}
if ($SurfaceId -notmatch '^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$') {
    throw "SurfaceId must match ^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$."
}
$validatedSurfaceId = $SurfaceId
$targetPngFileName = "$validatedSurfaceId.png"
if ($TimeoutSeconds -lt 1 -or $TimeoutSeconds -gt 600) {
    throw "TimeoutSeconds must be between 1 and 600."
}
if ($Width -lt 1 -or $Height -lt 1) {
    throw "Width and Height must be positive."
}

New-Item -ItemType Directory -Path $resolvedOutputDir -Force | Out-Null
if (-not $SkipPublish) {
    dotnet publish $project --configuration Release --framework net10.0 --runtime linux-x64 `
        --self-contained true --disable-build-servers -p:UseSharedCompilation=false `
        -p:NodeReuse=false /nr:false -m:1 -p:UseAppHost=true `
        -p:PublishReadyToRun=false -p:PublishSingleFile=false --output $resolvedPublishDir
    if ($LASTEXITCODE -ne 0) { throw "Avalonia Linux publish failed." }
}

$publishedExecutable = Join-Path $resolvedPublishDir "FreeX.ParityCapture.Avalonia"
if (-not (Test-Path -LiteralPath $publishedExecutable -PathType Leaf)) {
    throw "Published executable was not found: $publishedExecutable"
}
if (-not (docker image inspect $Image 2>$null)) {
    throw "Docker image '$Image' was not found. Build tools/LinuxInteractiveDocker or pass -Image."
}

$runScript = @'
#!/usr/bin/env bash
set -u
set -o pipefail
export LIBGL_ALWAYS_SOFTWARE=1
mkdir -p /work/logs
rm -f /work/manifest.json /work/__TARGET_PNG__ /work/run-result.txt
set +e
timeout --signal=TERM --kill-after=5s __TIMEOUT__s xvfb-run -a \
  --server-args="-screen 0 1280x720x24 -dpi 96" \
  /opt/freex/FreeX.ParityCapture.Avalonia --parity-capture /work \
    --parity-capture-surface '__SURFACE__' > /work/logs/app.log 2>&1
app_exit=$?
set -e
printf 'app_exit=%s\n' "$app_exit" > /work/run-result.txt
if [[ $app_exit -ne 0 ]]; then
  exit $app_exit
fi
if [[ ! -s /work/manifest.json ]]; then
  printf 'manifest_missing\n' >> /work/run-result.txt
  exit 20
fi
python3 - '__SURFACE__' '__WIDTH__' '__HEIGHT__' <<'PY'
import json, os, struct, sys
surface, expected_width, expected_height = sys.argv[1], int(sys.argv[2]), int(sys.argv[3])
manifest = json.load(open('/work/manifest.json', encoding='utf-8'))
rows = [row for row in manifest.get('surfaces', []) if row.get('id') == surface]
if len(rows) != 1 or not rows[0].get('captured'):
    raise SystemExit('target surface was not captured: ' + repr(rows))
png = os.path.join('/work', rows[0]['png'])
with open(png, 'rb') as handle:
    data = handle.read()
if len(data) <= 1000 or data[:8] != b'\x89PNG\r\n\x1a\n':
    raise SystemExit('target PNG is missing, blank, or invalid')
width, height = struct.unpack('>II', data[16:24])
if (width, height) != (expected_width, expected_height):
    raise SystemExit(f'unexpected PNG dimensions: {width}x{height}')
PY
printf 'capture_validated=true\n' >> /work/run-result.txt
'@
$runScript = $runScript.Replace('__TIMEOUT__', $TimeoutSeconds.ToString([Globalization.CultureInfo]::InvariantCulture))
$runScript = $runScript.Replace('__SURFACE__', $validatedSurfaceId)
$runScript = $runScript.Replace('__TARGET_PNG__', $targetPngFileName)
$runScript = $runScript.Replace('__WIDTH__', $Width.ToString([Globalization.CultureInfo]::InvariantCulture))
$runScript = $runScript.Replace('__HEIGHT__', $Height.ToString([Globalization.CultureInfo]::InvariantCulture))
$runScript = $runScript -replace "`r`n", "`n"
$scriptPath = Join-Path $resolvedOutputDir "container-run.sh"
[IO.File]::WriteAllText($scriptPath, $runScript, [Text.UTF8Encoding]::new($false))

$containerExists = docker ps -a --format '{{.Names}}' | Where-Object { $_ -eq $name }
if ($containerExists) {
    throw "Container '$name' already exists; refusing to touch it."
}

$containerStarted = $false
try {
    docker run --detach --rm --init --name $name --entrypoint /bin/bash `
        --mount "type=bind,source=$resolvedOutputDir,target=/work" `
        --mount "type=bind,source=$resolvedPublishDir,target=/opt/freex,readonly" `
        $Image /work/container-run.sh | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Docker container '$name' failed to start." }
    $containerStarted = $true

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds + 15)
    do {
        $previousErrorAction = $ErrorActionPreference
        $ErrorActionPreference = "Continue"
        $inspectOutput = docker inspect --format '{{.State.Status}}' $name 2>$null
        $inspectExit = $LASTEXITCODE
        $ErrorActionPreference = $previousErrorAction
        $state = if ($inspectExit -eq 0) { [string]$inspectOutput } else { "" }
        if ([string]::IsNullOrWhiteSpace($state) -or $state -eq 'exited') { break }
        Start-Sleep -Milliseconds 250
    } while ((Get-Date) -lt $deadline)

    if ($state -and $state -ne 'exited') {
        docker stop --time 5 $name | Out-Null
        throw "Container '$name' exceeded the bounded timeout of $TimeoutSeconds seconds."
    }

    $resultPath = Join-Path $resolvedOutputDir "run-result.txt"
    if (-not (Test-Path -LiteralPath $resultPath -PathType Leaf)) {
        $logPath = Join-Path $resolvedOutputDir "logs/app.log"
        if (Test-Path -LiteralPath $logPath) { Get-Content -LiteralPath $logPath -Tail 80 }
        throw "Capture container exited without run-result.txt."
    }
    $resultLines = @(Get-Content -LiteralPath $resultPath)
    if (-not ($resultLines -contains "app_exit=0") -or
        -not ($resultLines -contains "capture_validated=true")) {
        $logPath = Join-Path $resolvedOutputDir "logs/app.log"
        if (Test-Path -LiteralPath $logPath) { Get-Content -LiteralPath $logPath -Tail 80 }
        throw "Capture container did not report app_exit=0 and capture_validated=true."
    }
    $resultLines | Write-Output
    $manifestPath = Join-Path $resolvedOutputDir "manifest.json"
    if (-not (Test-Path -LiteralPath $manifestPath)) {
        $logPath = Join-Path $resolvedOutputDir "logs/app.log"
        if (Test-Path -LiteralPath $logPath) { Get-Content -LiteralPath $logPath -Tail 80 }
        throw "Capture container exited without a manifest."
    }
    Write-Host "Capture completed in container '$name': $resolvedOutputDir"
}
finally {
    if ($containerStarted) {
        $remaining = docker ps -a --format '{{.Names}}' | Where-Object { $_ -eq $name }
        if ($remaining) {
            docker stop --time 5 $name 2>$null | Out-Null
            docker rm -f $name 2>$null | Out-Null
        }
    }
}
