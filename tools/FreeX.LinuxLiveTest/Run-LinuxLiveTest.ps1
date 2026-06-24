<#
.SYNOPSIS
  Run the FreeX Linux Interaction Smoke test inside Docker (Xvfb + openbox + xdotool).

.DESCRIPTION
  1. Publishes FreeX.App.Avalonia self-contained for linux-x64 to a temp dir OUTSIDE OneDrive.
  2. Copies linux-live-test.sh next to the publish output.
  3. Runs ubuntu:24.04 via Docker, mounting the temp dir as /work.
  4. Collects screenshots + result.json into artifacts/linux-live-test/ (or -OutputDir).
  5. Prints PASS/FAIL per flow and exits non-zero if any flow failed.

.PARAMETER OutputDir
  Where to write collected artifacts (screenshots, result.json, app.log).
  Defaults to artifacts/linux-live-test/ under the repo root.

.PARAMETER TempPublishDir
  Where to publish FreeX for Linux. Must be OUTSIDE the repo/OneDrive tree.
  Defaults to $env:TEMP\FreeX-LinuxLiveTest-<timestamp>.

.PARAMETER SkipPublish
  Skip dotnet publish and reuse an existing TempPublishDir.

.PARAMETER Image
  Docker image to use. Default: ubuntu:24.04.

.PARAMETER TimeoutSeconds
  Docker run timeout in seconds. Default: 300.

.EXAMPLE
  # Normal run from the repo root:
  pwsh -ExecutionPolicy Bypass -File tools/FreeX.LinuxLiveTest/Run-LinuxLiveTest.ps1

.EXAMPLE
  # Reuse a previous publish (skip the ~60s publish step):
  pwsh -ExecutionPolicy Bypass -File tools/FreeX.LinuxLiveTest/Run-LinuxLiveTest.ps1 `
    -SkipPublish -TempPublishDir "C:\Temp\FreeX-LinuxLiveTest-20260625T120000"
#>
param(
    [string]$OutputDir = "",
    [string]$TempPublishDir = "",
    [switch]$SkipPublish,
    [string]$Image = "ubuntu:24.04",
    [int]$TimeoutSeconds = 300
)

$ErrorActionPreference = "Stop"

# Resolve paths
$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$scriptDir = $PSScriptRoot

if (-not $OutputDir) {
    $OutputDir = Join-Path $repoRoot "artifacts/linux-live-test"
}
$outFull = if ([System.IO.Path]::IsPathRooted($OutputDir)) { $OutputDir } else { Join-Path $repoRoot $OutputDir }

if (-not $TempPublishDir) {
    $stamp = (Get-Date).ToString("yyyyMMddTHHmmss")
    $TempPublishDir = Join-Path $env:TEMP "FreeX-LinuxLiveTest-$stamp"
}
$appDir  = Join-Path $TempPublishDir "app"
$workDir = $TempPublishDir

New-Item -ItemType Directory -Force -Path $outFull   | Out-Null
New-Item -ItemType Directory -Force -Path $appDir    | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $workDir "out") | Out-Null

Write-Host ""
Write-Host "=== FreeX Linux Interaction Smoke (Docker) ==="
Write-Host "  Repo root   : $repoRoot"
Write-Host "  Work dir    : $workDir"
Write-Host "  Output dir  : $outFull"
Write-Host "  Docker image: $Image"
Write-Host ""

# Step 1: Publish
$project = Join-Path $repoRoot "src/FreeX.App.Avalonia/FreeX.App.Avalonia.csproj"

if (-not $SkipPublish) {
    Write-Host "[1/4] Publishing FreeX for linux-x64 (self-contained)..."
    Write-Host "      Output: $appDir"
    Write-Host "      (first run takes ~60s)"
    $publishArgs = @(
        "publish", $project,
        "-c", "Release",
        "-f", "net10.0",
        "-r", "linux-x64",
        "--self-contained", "true",
        "-p:UseAppHost=true",
        "-p:PublishReadyToRun=false",
        "-p:PublishSingleFile=false",
        "-o", $appDir
    )
    & dotnet @publishArgs
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed (exit $LASTEXITCODE)." }
    Write-Host "[1/4] Publish done."
} else {
    Write-Host "[1/4] Skipping publish (-SkipPublish). Using: $appDir"
}

$appHost = Join-Path $appDir "FreeX"
if (-not (Test-Path $appHost)) {
    throw "Published apphost not found at: $appHost"
}

# Step 2: Copy and normalize test script
Write-Host ""
Write-Host "[2/4] Copying linux-live-test.sh to work dir..."
$scriptSrc = Join-Path $scriptDir "linux-live-test.sh"
$scriptDst = Join-Path $workDir "linux-live-test.sh"
Copy-Item -LiteralPath $scriptSrc -Destination $scriptDst -Force

# Strip UTF-8 BOM and normalize CRLF to LF (Docker bash on Linux requires plain LF)
$rawBytes = [System.IO.File]::ReadAllBytes($scriptDst)
if ($rawBytes.Length -ge 3 -and $rawBytes[0] -eq 0xEF -and $rawBytes[1] -eq 0xBB -and $rawBytes[2] -eq 0xBF) {
    $rawBytes = $rawBytes[3..($rawBytes.Length - 1)]
}
$rawText = [System.Text.Encoding]::UTF8.GetString($rawBytes)
$lfText  = $rawText -replace "`r`n", "`n" -replace "`r", "`n"
[System.IO.File]::WriteAllText($scriptDst, $lfText, (New-Object System.Text.UTF8Encoding($false)))
Write-Host "[2/4] Script copied (no BOM, LF endings)."

# Step 3: Run Docker
Write-Host ""
Write-Host "[3/4] Running Docker container (timeout: ${TimeoutSeconds}s)..."

$dockerMount   = ($workDir   -replace '\\', '/') + ":/work"
$dockerLogFile = Join-Path $workDir "docker-run.log"
$dockerErrFile = Join-Path $workDir "docker-run.err"

$prevEap = $ErrorActionPreference
$ErrorActionPreference = "Continue"

$dockerProc = Start-Process -FilePath "docker" `
    -ArgumentList @(
        "run", "--rm", "--memory=2g",
        "-v", $dockerMount,
        $Image,
        "bash", "/work/linux-live-test.sh"
    ) `
    -NoNewWindow -PassThru -Wait `
    -RedirectStandardOutput $dockerLogFile `
    -RedirectStandardError  $dockerErrFile

$dockerExit = $dockerProc.ExitCode
$ErrorActionPreference = $prevEap

Write-Host ""
if (Test-Path $dockerLogFile) {
    Get-Content -LiteralPath $dockerLogFile | ForEach-Object { Write-Host $_ }
}
if (Test-Path $dockerErrFile) {
    $errLines = Get-Content -LiteralPath $dockerErrFile
    if ($errLines) {
        Write-Host "[docker stderr]"
        $errLines | ForEach-Object { Write-Host $_ }
    }
}
Write-Host ""
Write-Host "Docker exit code: $dockerExit"

# Step 4: Collect artifacts
Write-Host ""
Write-Host "[4/4] Collecting artifacts to: $outFull"
$outContainerDir = Join-Path $workDir "out"

if (Test-Path $outContainerDir) {
    Get-ChildItem -LiteralPath $outContainerDir | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $outFull $_.Name) -Force
        Write-Host "  Collected: $($_.Name)"
    }
} else {
    Write-Warning "Container out/ directory not found: $outContainerDir"
}

# Parse result.json and print summary
Write-Host ""
$resultFile  = Join-Path $outFull "result.json"
$overallPass = $false

if (Test-Path $resultFile) {
    $result = Get-Content -LiteralPath $resultFile -Encoding UTF8 -Raw | ConvertFrom-Json
    $overall = $result.overall
    $overallPass = ($overall -eq "PASS")

    Write-Host "============================================"
    Write-Host " Linux Interaction Smoke -- $overall"
    Write-Host "============================================"

    $result.flows.PSObject.Properties | ForEach-Object {
        $status = $_.Value
        $icon   = if ($status -eq "PASS") { "OK " } else { "ERR" }
        Write-Host ("  [{0}] {1,-22} {2}" -f $icon, $_.Name, $status)
    }

    Write-Host "============================================"
    Write-Host "  Screenshots : $outFull"
    Write-Host "  Result JSON : $resultFile"
    Write-Host ""
} else {
    Write-Warning "result.json not found -- container may have failed before writing results."
    Write-Host "Docker exit code was: $dockerExit"
}

# Exit with appropriate code
if (-not $overallPass) {
    Write-Host ""
    Write-Host "FAILED -- one or more flows did not pass. See above for details."
    exit 1
}

Write-Host "PASSED -- all flows passed."
exit 0
