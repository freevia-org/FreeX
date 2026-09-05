<#
.SYNOPSIS
Runs the manifest-driven Linux commit test gates (eng/test-gates.json) locally inside a
lean Docker container, so platform-specific failures (e.g. behaviour that only diverges on
Linux/macOS, such as .NET mapping ApplicationData and LocalApplicationData to the same
directory) are caught before pushing, instead of only in CI.

This script is a thin wrapper: it builds/reuses tools/LinuxTestGate/Dockerfile, then invokes
the SAME entry point CI uses (tools/Invoke-TestGate.ps1) inside the container for each
requested gate id, on -Platform linux. It does not reimplement gate selection logic.

.PARAMETER GateId
One or more gate ids to run. Defaults to every commit gate in eng/test-gates.json whose
platforms include "linux" (freex-portable-unix, freex-avalonia, freew-core-portable,
freep-core-portable as of this writing -- read from the manifest, not hardcoded).

.PARAMETER Rebuild
Force a Docker image rebuild even if an image tagged with the current Dockerfile content
hash already exists.

.PARAMETER ResultsDirectory
Host-relative directory (under the repo root) that receives TRX results. Defaults to
"artifacts/test-gates-linux". Mounted straight into the container (small file count, so the
bind-mount cost that matters for build outputs does not apply here).

.EXAMPLE
pwsh -NoProfile -File tools/Run-LinuxTestGate.ps1

.EXAMPLE
pwsh -NoProfile -File tools/Run-LinuxTestGate.ps1 -GateId freew-core-portable -Rebuild
#>
[CmdletBinding()]
param(
    [string[]]$GateId = @(),

    [switch]$Rebuild,

    [string]$Configuration = "Release",

    [ValidateRange(0, 63)]
    [int]$PartitionIndex = 0,

    [ValidateRange(1, 64)]
    [int]$PartitionCount = 1,

    [switch]$NoBuild,

    [switch]$NoRestore,

    [ValidatePattern("^\d+[smh]$")]
    [string]$HangTimeout = "15m",

    [string]$ResultsDirectory = "artifacts/test-gates-linux"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot "ToolScriptSupport.ps1")

$repoRoot = Split-Path -Parent $PSScriptRoot
$dockerfilePath = Join-Path $PSScriptRoot "LinuxTestGate/Dockerfile"
if (-not (Test-Path -LiteralPath $dockerfilePath -PathType Leaf)) {
    throw "Missing $dockerfilePath."
}

if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
    throw "Docker is required to run the Linux test gate locally, but 'docker' was not found on PATH."
}

# --- Resolve which gates to run from the SAME manifest Invoke-TestGate.ps1 reads. -----------------
$manifestPath = Join-Path $repoRoot "eng/test-gates.json"
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if ($manifest.schemaVersion -ne 1) {
    throw "Unsupported test-gates schema version '$($manifest.schemaVersion)'."
}

$linuxCommitGates = @($manifest.gates | Where-Object {
    $_.gate -eq "commit" -and $_.platforms -contains "linux"
} | ForEach-Object { [string]$_.id })

if ($GateId.Count -eq 0) {
    $resolvedGateIds = $linuxCommitGates
}
else {
    $resolvedGateIds = @($GateId)
    foreach ($requestedId in $resolvedGateIds) {
        if ($linuxCommitGates -notcontains $requestedId) {
            throw "Gate '$requestedId' is not a linux commit gate in eng/test-gates.json. Linux commit gates: $($linuxCommitGates -join ', ')."
        }
    }
}
if ($resolvedGateIds.Count -eq 0) {
    throw "No linux commit gates found in eng/test-gates.json."
}

Write-Host "Linux commit gates to run: $($resolvedGateIds -join ', ')"

# --- Build (or reuse) the lean test image. Tag = content hash of the Dockerfile, so an -------------
# --- unchanged Dockerfile never triggers a rebuild, and a changed one always gets a fresh tag. -----
$dockerfileHash = Get-ToolNormalizedTextSha256 -Path $dockerfilePath
$imageTag = "freex-linux-testgate:$($dockerfileHash.Substring(0, 12))"
$floatingTag = "freex-linux-testgate:latest"

$imageExists = $false
if (-not $Rebuild) {
    & docker image inspect $imageTag *> $null
    $imageExists = ($LASTEXITCODE -eq 0)
}

$buildStopwatch = [System.Diagnostics.Stopwatch]::StartNew()
if ($Rebuild -or -not $imageExists) {
    Write-Host "Building Linux test-gate image '$imageTag' (Dockerfile changed or -Rebuild requested)..."
    & docker build -t $imageTag -t $floatingTag -f $dockerfilePath (Join-Path $PSScriptRoot "LinuxTestGate")
    if ($LASTEXITCODE -ne 0) {
        throw "docker build failed with exit code $LASTEXITCODE."
    }
    $buildStopwatch.Stop()
    Write-Host ("Image build took {0:N1}s." -f $buildStopwatch.Elapsed.TotalSeconds)
}
else {
    Write-Host "Reusing existing image '$imageTag' (Dockerfile unchanged; pass -Rebuild to force a rebuild)."
    # Keep the floating tag pointed at the image actually used this run.
    & docker tag $imageTag $floatingTag
}

# --- Prepare host-side mounts. ----------------------------------------------------------------------
$resultsFullPath = Resolve-ToolFullPath -Path $ResultsDirectory -BasePath $repoRoot
if (-not (Test-Path -LiteralPath $resultsFullPath)) {
    New-Item -ItemType Directory -Path $resultsFullPath -Force | Out-Null
}

$nugetVolume = "freex-linux-testgate-nuget"
$repoVolume = "freex-linux-testgate-repo"
foreach ($volumeName in @($nugetVolume, $repoVolume)) {
    & docker volume inspect $volumeName *> $null
    if ($LASTEXITCODE -ne 0) {
        & docker volume create $volumeName *> $null
    }
}

# --- Build the in-container script. -----------------------------------------------------------------
# Performance: the repo is bind-mounted read-only at /repo-src. Every actual restore/build/test run
# happens against /repo, a Docker-managed volume (freex-linux-testgate-repo) synced from /repo-src via
# rsync on every invocation. bin/ and obj/ are excluded from the sync (and, since --delete-excluded is
# NOT passed, never deleted from the destination either), so they persist inside the volume across
# runs and give real incremental-build speedups -- a bind-mounted Windows filesystem is slow for the
# large number of small files MSBuild/NuGet write under bin/obj, but the volume lives entirely inside
# the Linux VM/container storage. NuGet's global packages folder is likewise redirected to a separate
# named volume so restores are warm across runs too.
$gateIdList = $resolvedGateIds -join " "
$extraArgLines = ""
if ($NoBuild) { $extraArgLines += "    args+=(-NoBuild)`n" }
if ($NoRestore) { $extraArgLines += "    args+=(-NoRestore)`n" }

# NOTE: this is a single-quoted here-string, so none of the bash `$...` references below are
# expanded by PowerShell. Only the __TOKEN__ placeholders are substituted, via literal .Replace()
# calls, to avoid any ambiguity between PowerShell and bash variable syntax.
$innerScriptTemplate = @'
set -euo pipefail
echo 'Syncing repository into container-local volume (bin/obj excluded and preserved across runs)...'
rsync -a --delete \
    --exclude='.git/' \
    --exclude='bin/' \
    --exclude='obj/' \
    --exclude='.worktrees/' \
    --exclude='artifacts/' \
    /repo-src/ /repo/
rm -rf /repo/__RESULTS_DIR__
mkdir -p "$(dirname /repo/__RESULTS_DIR__)"
ln -s /results /repo/__RESULTS_DIR__

cd /repo
export NUGET_PACKAGES=/nuget-cache
status=0
for gate in __GATE_IDS__; do
    echo ''
    echo "=== Linux commit gate: $gate ==="
    args=(-Gate commit -App all -Platform linux -GateId "$gate" -PartitionIndex __PARTITION_INDEX__ -PartitionCount __PARTITION_COUNT__ -Configuration "__CONFIGURATION__" -HangTimeout "__HANG_TIMEOUT__" -ResultsDirectory "__RESULTS_DIR__")
__EXTRA_ARG_LINES__    pwsh -NoProfile -File tools/Invoke-TestGate.ps1 "${args[@]}" || status=$?
done
exit $status
'@

$innerScript = $innerScriptTemplate.
    Replace("__RESULTS_DIR__", $ResultsDirectory).
    Replace("__GATE_IDS__", $gateIdList).
    Replace("__PARTITION_INDEX__", [string]$PartitionIndex).
    Replace("__PARTITION_COUNT__", [string]$PartitionCount).
    Replace("__CONFIGURATION__", $Configuration).
    Replace("__HANG_TIMEOUT__", $HangTimeout).
    Replace("__EXTRA_ARG_LINES__", $extraArgLines)

$scratchDir = Join-Path ([System.IO.Path]::GetTempPath()) "freex-linux-testgate"
New-Item -ItemType Directory -Path $scratchDir -Force | Out-Null
$innerScriptPath = Join-Path $scratchDir "run-gates.sh"
[System.IO.File]::WriteAllText($innerScriptPath, $innerScript.Replace("`r`n", "`n"), [System.Text.UTF8Encoding]::new($false))

$runStopwatch = [System.Diagnostics.Stopwatch]::StartNew()
& docker run --rm `
    -v "${repoRoot}:/repo-src:ro" `
    -v "${repoVolume}:/repo" `
    -v "${nugetVolume}:/nuget-cache" `
    -v "${resultsFullPath}:/results" `
    -v "${innerScriptPath}:/run-gates.sh:ro" `
    $imageTag `
    bash /run-gates.sh
$exitCode = $LASTEXITCODE
$runStopwatch.Stop()
Write-Host ("Linux test-gate run took {0:N1}s (wall clock)." -f $runStopwatch.Elapsed.TotalSeconds)

if ($exitCode -ne 0) {
    Write-Error "One or more Linux commit gates failed. See TRX results under $ResultsDirectory."
}
else {
    Write-Host "All requested Linux commit gates passed."
}
exit $exitCode
