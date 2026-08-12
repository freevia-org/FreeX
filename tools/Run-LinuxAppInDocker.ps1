<#
.SYNOPSIS
  Run the Linux (linux-x64) FreeX build inside an Ubuntu Docker container as a local UI smoke,
  and capture a screenshot of the live window. Lets Windows developers visually validate the
  Linux app without a Linux box (uses the WSL2 Docker backend + Xvfb + Skia software rendering).

.DESCRIPTION
  Publishes the Avalonia app self-contained for linux-x64, then runs it in `ubuntu:24.04`:
    1. headless --packaging-smoke (workbook open/edit/save/reopen),
    2. Xvfb --launch-smoke (window/menus/dialogs/accessibility),
    3. a screenshot of the running window opening a demo CSV.
  Requires Docker Desktop running. Mirrors the linux-app CI UI lane locally.

.EXAMPLE
  pwsh -File tools/Run-LinuxAppInDocker.ps1
#>
param(
    [string]$OutputDir = "artifacts/docker-run",
    [string]$Image = "ubuntu:24.04",
    [switch]$SkipPublish,
    [switch]$NoScreenshot
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$outFull = if ([System.IO.Path]::IsPathRooted($OutputDir)) { $OutputDir } else { Join-Path $repoRoot $OutputDir }
$publishDir = Join-Path $outFull "linux-x64"
$project = Join-Path $repoRoot "src/FreeX.App.Avalonia/FreeX.App.Avalonia.csproj"
$validationPublishDir = Join-Path $outFull "linux-x64-validation"
$validationProject = Join-Path $repoRoot "tools/FreeX.Validation.Avalonia/FreeX.Validation.Avalonia.csproj"

New-Item -ItemType Directory -Path $outFull -Force | Out-Null

if (-not $SkipPublish) {
    Write-Host "Publishing linux-x64 self-contained..."
    dotnet publish $project -c Release -f net10.0 -r linux-x64 --self-contained true `
        -p:UseAppHost=true -p:PublishReadyToRun=false -p:PublishSingleFile=false -o $publishDir
    if ($LASTEXITCODE -ne 0) { throw "Publish failed." }
    dotnet publish $validationProject -c Release -f net10.0 -r linux-x64 --self-contained true `
        -p:UseAppHost=true -p:PublishReadyToRun=false -p:PublishSingleFile=false -o $validationPublishDir
    if ($LASTEXITCODE -ne 0) { throw "Validation host publish failed." }
}
if (-not (Test-Path (Join-Path $publishDir "FreeX"))) { throw "Published apphost not found at $publishDir/FreeX." }
if (-not (Test-Path (Join-Path $validationPublishDir "FreeX.Validation.Avalonia"))) { throw "Published validation host not found at $validationPublishDir/FreeX.Validation.Avalonia." }

$screenshotBlock = if ($NoScreenshot) { 'echo "screenshot skipped"' } else { @'
printf 'Region,Q1,Q2,Total\nNorth,120,135,255\nSouth,98,110,208\nEast,143,150,293\nWest,87,92,179\n' > /tmp/demo.csv
xvfb-run -a --server-args="-screen 0 1280x820x24" bash -c '
  cd /opt/freex
  ./FreeX /tmp/demo.csv >/tmp/app.log 2>&1 &
  app_pid=$!
  sleep 10
  import -window root /tmp/shot.png 2>/tmp/import.log || echo IMPORT_FAIL
  kill "$app_pid" 2>/dev/null || true
'
cp /tmp/shot.png /work/freex-linux-screenshot.png 2>/dev/null && echo "screenshot: artifacts/docker-run/freex-linux-screenshot.png" || { echo "no screenshot"; tail -5 /tmp/app.log; }
'@ }

# Container script (LF endings; the docker invocation strips any CR just in case).
$runScript = @"
#!/usr/bin/env bash
set -u
export DEBIAN_FRONTEND=noninteractive
export LIBGL_ALWAYS_SOFTWARE=1
apt-get update -qq >/dev/null
apt-get install -y -qq \
  libfontconfig1 libice6 libsm6 libx11-6 libx11-xcb1 libxext6 libxrender1 \
  libgl1 libegl1 libicu74 libssl3 zlib1g xvfb fonts-dejavu fonts-noto-cjk \
  imagemagick procps >/dev/null
cp -a /work/linux-x64 /opt/freex
cp -a /work/linux-x64-validation /opt/freex-validation
cd /opt/freex
chmod +x FreeX
chmod +x /opt/freex-validation/FreeX.Validation.Avalonia
echo "=== PROOF 1: headless packaging smoke ==="
/opt/freex-validation/FreeX.Validation.Avalonia --packaging-smoke || echo "packaging-smoke exit=`$?"
echo "=== PROOF 2: Xvfb GUI launch smoke ==="
printf 'Name,Amount\nFreeX on Linux,42\n' > /tmp/launch.csv
xvfb-run -a /opt/freex-validation/FreeX.Validation.Avalonia --launch-smoke /tmp/report.txt /tmp/launch.csv || echo "launch-smoke exit=`$?"
grep -E "^macos_launch_smoke=|^window_shown=|^viewport_rows=|^viewport_columns=|^native_file_menu=|^find_dialog=|^format_cells_dialog=|^macos_accessibility_smoke=" /tmp/report.txt 2>/dev/null || echo "(no report)"
echo "=== PROOF 3: screenshot ==="
$screenshotBlock
echo "=== done ==="
"@

$runScript = $runScript -replace "`r`n", "`n"
$runScriptPath = Join-Path $outFull "run.sh"
[System.IO.File]::WriteAllText($runScriptPath, $runScript)

$dockerMount = ($outFull -replace '\\', '/') + ":/work"
Write-Host "Running $Image (mount $dockerMount)..."
# Docker writes progress/warnings to stderr; under ErrorActionPreference=Stop (esp. when a caller
# pipes 2>&1) that would abort. Relax around the native call and check the real exit code.
$prevEap = $ErrorActionPreference
$ErrorActionPreference = "Continue"
docker run --rm -v $dockerMount $Image bash -c "tr -d '\r' < /work/run.sh | bash"
$dockerExit = $LASTEXITCODE
$ErrorActionPreference = $prevEap
if ($dockerExit -ne 0) { throw "Container run failed (exit $dockerExit)." }

$shot = Join-Path $outFull "freex-linux-screenshot.png"
if (Test-Path $shot) { Write-Host "Screenshot: $shot" }
