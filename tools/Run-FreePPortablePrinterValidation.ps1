<#
.SYNOPSIS
  Run the focused Wave 105 physical Linux lane for FreeP's portable printer dialog.

.DESCRIPTION
  Starts the existing Ubuntu/X11 FreeP harness with its private CUPS dry-run PATH, replaces
  only that PATH's lpstat/lp entries with deterministic two-queue fakes, and drives File > Print
  through the app-owned Avalonia printer/settings dialog. Docker execution is deliberately kept
  in the foreground so the caller owns serialization and can inspect the mounted evidence.
#>
[CmdletBinding()]
param(
    [ValidateRange(1024, 65535)]
    [int]$Port = 6105,
    [ValidateRange(640, 7680)]
    [int]$Width = 1280,
    [ValidateRange(480, 4320)]
    [int]$Height = 820,
    [ValidateRange(72, 240)]
    [int]$Dpi = 96,
    [ValidateSet("2g", "4g", "6g", "8g")]
    [string]$MemoryLimit = "4g",
    [string]$OutputDir = "artifacts/freep-portable-printer-wave105",
    [string]$PublishDir = "",
    [switch]$SkipPublish,
    [switch]$SkipImageBuild,
    [switch]$Replace
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot "ToolScriptSupport.ps1")
$genericRunner = Join-Path $PSScriptRoot "Run-LinuxInteractiveDocker.ps1"
$probeSource = Join-Path $PSScriptRoot "LinuxInteractiveDocker/run-freep-portable-printer-probe.sh"
$schemaPath = Join-Path $PSScriptRoot "LinuxInteractiveDocker/freep-portable-printer-wave105-validation.schema.json"
$null = . (Join-Path $PSScriptRoot "LinuxInteractiveDocker/ManifestEvidence.ps1")
$fakeLpstat = Join-Path $PSScriptRoot "LinuxInteractiveDocker/freep-portable-printer-fake-lpstat.sh"
$fakeLp = Join-Path $PSScriptRoot "LinuxInteractiveDocker/freep-portable-printer-fake-lp.sh"
$resolvedOutputRoot = if ([IO.Path]::IsPathRooted($OutputDir)) {
    [IO.Path]::GetFullPath($OutputDir)
} else {
    [IO.Path]::GetFullPath((Join-Path $repoRoot $OutputDir))
}

function Invoke-Docker {
    param([Parameter(Mandatory = $true)][string[]]$Arguments)
    Push-Location $repoRoot
    try {
        $output = @(& docker @Arguments 2>&1)
        $exitCode = $LASTEXITCODE
    } finally { Pop-Location }
    if ($exitCode -ne 0) {
        throw "docker $($Arguments -join ' ') failed with exit code $exitCode.`n$($output -join [Environment]::NewLine)"
    }
    return $output
}

function Read-JsonFile {
    param([Parameter(Mandatory = $true)][string]$Path)
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw "Required evidence file is missing: $Path" }
    return Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
}

function Assert-ManifestContract {
    param([Parameter(Mandatory = $true)]$Manifest, [Parameter(Mandatory = $true)][string]$ManifestPath, [Parameter(Mandatory = $true)][string]$EvidenceDirectory)
    Assert-ManifestIdentity -Manifest $Manifest -Expected ([ordered]@{
        schemaVersion = 1; suite = "freep-portable-printer-wave105-physical"; platform = "linux"
        shell = "avalonia"; app = "FreeP"; baseline = $false; appSurface = "file-print-portable-printer-dialog"
    }) -FailureMessage "Invalid FreeP portable printer manifest identity: $ManifestPath"
    $expectedIds = @(
        "owner-window-visible", "file-print-route", "portable-dialog-visible", "portable-dialog-controls",
        "non-default-printer-selected", "settings-submitted", "fake-lp-arguments", "submitted-pdf", "owner-focus-restored"
    )
    $results = @($Manifest.results)
    Assert-ManifestResultIds -Results $results -ExpectedIds $expectedIds `
        -FailureMessage "Portable printer result order or count is invalid: $ManifestPath"
    Assert-ManifestResultSummary -Manifest $Manifest -Results $results -ExpectedTotal 9 `
        -RequireCompleteStatuses -FailureMessage "Portable printer manifest did not pass all nine physical gates: $ManifestPath"
    $fileMap = Get-ManifestEvidenceFileMap -EvidenceDirectory $EvidenceDirectory
    Assert-ManifestResultEvidence -Results $results -FileMap $fileMap `
        -Category "physical-x11-portable-printer" -EvidenceLevel "physical-x11-input" `
        -ValidStatuses @("passed") -RequireNote
    Assert-ManifestScreenshotEvidence -Screenshots @($Manifest.screenshots) -FileMap $fileMap -ExpectedCount 5
    if ($Manifest.fakePrinter.privatePath -ne "/tmp/freex-cups-dry-run" -or
        [string]::Join("|", @($Manifest.fakePrinter.printers)) -ne "FreeP-Default|FreeP-Secondary" -or
        $Manifest.fakePrinter.defaultPrinter -ne "FreeP-Default" -or $Manifest.fakePrinter.realDevice -ne $false) {
        throw "Portable printer fake boundary is not deterministic or private."
    }
    if ($Manifest.submission.queue -ne "FreeP-Secondary" -or $Manifest.submission.copies -ne 2 -or
        $Manifest.submission.pageRange -ne "2-3" -or $Manifest.submission.collate -ne $false -or
        $Manifest.submission.orientation -ne "landscape" -or $Manifest.submission.pdfBytes -le 0) {
        throw "Portable printer submission settings did not match the physical contract."
    }
    if ($Manifest.processExitCode -ne 0) { throw "Portable printer probe exited unsuccessfully." }
    return Complete-ManifestContract -Manifest $Manifest -ManifestPath $ManifestPath `
        -Validator "tools/Run-FreePPortablePrinterValidation.ps1" `
        -ContractReference "tools/LinuxInteractiveDocker/freep-portable-printer-wave105-validation.schema.json" -JsonDepth 20
}

foreach ($path in @($genericRunner, $probeSource, $schemaPath, $fakeLpstat, $fakeLp)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Required portable printer lane file is missing: $path" }
}
New-Item -ItemType Directory -Path $resolvedOutputRoot -Force | Out-Null
$started = $false
$sessionDirectory = $null
$evidenceDirectory = $null
$manifestPath = $null
$probeExitCode = 1
$session = $null
try {
    $startArguments = @(
        "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", $genericRunner,
        "-Action", "Start", "-App", "FreeP", "-Port", "$Port",
        "-Width", "$Width", "-Height", "$Height", "-Dpi", "$Dpi",
        "-MemoryLimit", $MemoryLimit, "-OutputDir", $resolvedOutputRoot,
        "-CupsDryRun", "-CupsDryRunMode", "success",
        "-AppEnvironment", "FREEP_PORTABLE_PRINTER_OUTPUT=/work/portable-printer"
    )
    if (-not [string]::IsNullOrWhiteSpace($PublishDir)) { $startArguments += @("-PublishDir", $PublishDir) }
    if ($SkipPublish) { $startArguments += "-SkipPublish" }
    if ($SkipImageBuild) { $startArguments += "-SkipImageBuild" }
    if ($Replace) { $startArguments += "-Replace" }
    Invoke-ToolProcess -FilePath "powershell.exe" -Arguments $startArguments -WorkingDirectory $repoRoot -OutputToHost
    $started = $true

    $sessionPath = Join-Path $resolvedOutputRoot "freep/current-session.json"
    $session = Read-JsonFile $sessionPath
    $sessionDirectory = [IO.Path]::GetFullPath([string]$session.sessionDirectory)
    $expectedContainerName = "freex-linux-interactive-freep-$Port"
    if ([string]$session.containerName -ne $expectedContainerName) { throw "Unexpected harness container name: $($session.containerName)" }
    $evidenceDirectory = Join-Path $sessionDirectory "portable-printer"
    New-Item -ItemType Directory -Path $evidenceDirectory -Force | Out-Null
    $probeInWork = Join-Path $sessionDirectory "run-freep-portable-printer-probe.sh"
    Copy-Item -LiteralPath $probeSource -Destination $probeInWork -Force
    $manifestPath = Join-Path $evidenceDirectory "freep-portable-printer-wave105.json"
    $probeLog = Join-Path $evidenceDirectory "probe.log"

    $containerName = [string]$session.containerName
    Invoke-Docker -Arguments @("cp", $fakeLpstat, "${containerName}:/tmp/freex-cups-dry-run/lpstat") | Out-Null
    Invoke-Docker -Arguments @("cp", $fakeLp, "${containerName}:/tmp/freex-cups-dry-run/lp") | Out-Null
    Invoke-Docker -Arguments @("exec", $containerName, "chmod", "0755", "/tmp/freex-cups-dry-run/lpstat", "/tmp/freex-cups-dry-run/lp") | Out-Null

    $probeOutput = @()
    Push-Location $repoRoot
    try {
        $probeOutput = @(& docker exec $containerName bash /work/run-freep-portable-printer-probe.sh /work/portable-printer 2>&1)
        $probeExitCode = $LASTEXITCODE
    } finally { Pop-Location }
    if ($probeOutput.Count -gt 0) { $probeOutput | Set-Content -LiteralPath $probeLog -Encoding utf8 }
    else { "docker exec produced no stdout/stderr." | Set-Content -LiteralPath $probeLog -Encoding utf8 }

    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) { throw "Portable printer probe did not write a manifest: $probeLog" }
    $manifest = Read-ManifestContract -ManifestPath $manifestPath -SchemaPath $schemaPath
    $manifest = Assert-ManifestContract -Manifest $manifest -ManifestPath $manifestPath -EvidenceDirectory $evidenceDirectory
    Write-Host "FreeP portable printer validation: $($manifest.summary.passed) passed, $($manifest.summary.failed) failed, $($manifest.summary.total) total"
    Write-Host "Manifest: $manifestPath"
    Write-Host "Evidence: $evidenceDirectory"
    if ($probeExitCode -ne 0) { throw "Portable printer probe exited with code $probeExitCode." }
} finally {
    if ($started) {
        try {
            Invoke-ToolProcess -FilePath "powershell.exe" -Arguments @(
                "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", $genericRunner,
                "-Action", "Stop", "-App", "FreeP", "-Port", "$Port", "-OutputDir", $resolvedOutputRoot) -WorkingDirectory $repoRoot -OutputToHost
        } catch { Write-Warning "Could not stop harness-owned FreeP container '$($session.containerName)': $($_.Exception.Message)" }
    }
}
