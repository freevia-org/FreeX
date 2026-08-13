<#
.SYNOPSIS
  Physically validates FreeW Avalonia Alt+F9 and F9 field shortcuts in the Linux Docker desktop.

.DESCRIPTION
  Generates a deterministic DOCX through FreeW.Core.Model/FreeW.Core.IO, starts the harness-owned
  FreeW Avalonia window with that document, injects real X11 key chords, and validates the saved DOCX
  through the structured DocxReader inspector. This is a dedicated, non-exhaustive lane and does not
  modify the clean/untitled FreeW family baseline.
#>
[CmdletBinding()]
param(
    [ValidateRange(1024, 65535)][int]$Port = 6091,
    [ValidateRange(640, 7680)][int]$Width = 1280,
    [ValidateRange(480, 4320)][int]$Height = 820,
    [ValidateRange(72, 240)][int]$Dpi = 96,
    [ValidateSet("2g", "4g", "6g", "8g")][string]$MemoryLimit = "4g",
    [string]$OutputDir = "artifacts/linux-field-shortcuts",
    [string]$PublishDir = "",
    [switch]$SkipPublish,
    [switch]$SkipImageBuild,
    [switch]$Replace,
    [switch]$KeepContainer
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot "ToolScriptSupport.ps1")
$resolvedOutputRoot = if ([IO.Path]::IsPathRooted($OutputDir)) {
    [IO.Path]::GetFullPath($OutputDir)
} else {
    [IO.Path]::GetFullPath((Join-Path $repoRoot $OutputDir))
}
$fixtureProject = Join-Path $repoRoot "freew/tools/FreeW.FieldShortcutFixture/FreeW.FieldShortcutFixture.csproj"
$fixturePath = Join-Path $resolvedOutputRoot "fixture/field-shortcut-fixture.docx"
$fixtureFileName = Split-Path -Leaf $fixturePath
$expectedTitle = "FreeW deterministic field shortcut title"
$genericRunner = Join-Path $PSScriptRoot "Run-LinuxInteractiveDocker.ps1"
$probeSource = Join-Path $PSScriptRoot "LinuxInteractiveDocker/run-freew-field-shortcut-probe.sh"
$schemaPath = Join-Path $PSScriptRoot "LinuxInteractiveDocker/field-shortcut-validation.schema.json"
$manifestEvidenceHelper = Join-Path $PSScriptRoot "LinuxInteractiveDocker/ManifestEvidence.ps1"
$null = . $manifestEvidenceHelper

function Assert-ManifestContract {
    param(
        [Parameter(Mandatory = $true)][string]$ManifestPath,
        [Parameter(Mandatory = $true)][string]$EvidenceDirectory
    )
    $manifest = Read-ManifestContract -ManifestPath $ManifestPath -SchemaPath $schemaPath `
        -InvalidSchemaMessage "Field shortcut schema is not a JSON Schema document."
    Assert-ManifestIdentity -Manifest $manifest -Expected ([ordered]@{
        schemaVersion = 1; suite = "freew-linux-field-shortcut-physical"; platform = "linux"
        shell = "avalonia"; app = "FreeW"; baseline = $false; appSurface = "document-editor-field-shortcuts"
    }) -FailureMessage "Field shortcut manifest header does not satisfy its dedicated contract."
    if ($manifest.coverage.exhaustive -ne $false -or
        $manifest.coverage.scope -ne "physical Alt+F9/F9 field shortcut lane" -or
        $manifest.window.pattern -ne $fixtureFileName -or
        ([string]$manifest.window.title).IndexOf($fixtureFileName, [StringComparison]::Ordinal) -lt 0 -or
        $manifest.window.visible -ne $true) {
        throw "Field shortcut manifest header does not satisfy its dedicated contract."
    }

    $requiredIds = @(
        "visible-window-discovery",
        "field-code-shortcut-show",
        "field-code-shortcut-hide",
        "field-update-shortcut-persist"
    )
    $results = @($manifest.results)
    Assert-ManifestResultIds -Results $results -ExpectedIds $requiredIds -AllowAnyOrder `
        -FailureMessage "Field shortcut manifest must contain exactly four unique result rows."
    Assert-ManifestResultSummary -Manifest $manifest -Results $results -ExpectedTotal 4 `
        -FailureMessage "Field shortcut manifest summary does not match its result rows."

    $fileMap = Get-ManifestEvidenceFileMap -EvidenceDirectory $EvidenceDirectory
    Assert-ManifestResultEvidence -Results $results -FileMap $fileMap `
        -Category "physical-x11-field-shortcut" -EvidenceLevel "physical-x11-input"
    Assert-ManifestScreenshotEvidence -Screenshots @($manifest.screenshots) -FileMap $fileMap
    return Complete-ManifestContract -Manifest $manifest -ManifestPath $ManifestPath `
        -Validator "tools/Run-FreeWFieldShortcutValidation.ps1" `
        -ContractReference "tools/LinuxInteractiveDocker/field-shortcut-validation.schema.json"
}

New-Item -ItemType Directory -Path (Split-Path -Parent $fixturePath) -Force | Out-Null
Invoke-ToolProcess -FilePath "dotnet" -Arguments @(
    "run", "--project", $fixtureProject, "--configuration", "Release", "--",
    "generate", $fixturePath
) -WorkingDirectory $repoRoot

$started = $false
$sessionDirectory = $null
$probeExitCode = 1
$manifestPath = $null
try {
    $startArguments = @(
        "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", $genericRunner,
        "-Action", "Start", "-App", "FreeW", "-Port", "$Port",
        "-Width", "$Width", "-Height", "$Height", "-Dpi", "$Dpi",
        "-MemoryLimit", $MemoryLimit, "-OutputDir", $resolvedOutputRoot,
        "-DocumentPath", $fixturePath
    )
    if (-not [string]::IsNullOrWhiteSpace($PublishDir)) { $startArguments += @("-PublishDir", $PublishDir) }
    if ($SkipPublish) { $startArguments += "-SkipPublish" }
    if ($SkipImageBuild) { $startArguments += "-SkipImageBuild" }
    if ($Replace) { $startArguments += "-Replace" }
    Invoke-ToolProcess -FilePath "powershell.exe" -Arguments $startArguments -WorkingDirectory $repoRoot
    $started = $true

    $sessionMetadataPath = Join-Path $resolvedOutputRoot "freew/current-session.json"
    $session = Get-Content -LiteralPath $sessionMetadataPath -Raw | ConvertFrom-Json
    $sessionDirectory = [IO.Path]::GetFullPath([string]$session.sessionDirectory)
    $probeInWork = Join-Path $sessionDirectory "field-shortcut-probe.sh"
    Copy-Item -LiteralPath $probeSource -Destination $probeInWork -Force
    $probeLog = Join-Path $sessionDirectory "field-shortcut-validation/probe.log"
    New-Item -ItemType Directory -Path (Split-Path -Parent $probeLog) -Force | Out-Null
    $dockerArguments = @(
        "exec", "--env", "FIELD_DOCUMENT_PATH=/documents/field-shortcut-fixture.docx",
        "--env", "FIELD_EXPECTED_DOCUMENT_NAME=$fixtureFileName",
        [string]$session.containerName, "bash", "/work/field-shortcut-probe.sh", "/work/field-shortcut-validation"
    )
    Push-Location $repoRoot
    try {
        $probeOutput = @(& docker @dockerArguments 2>&1)
        $probeExitCode = $LASTEXITCODE
    } finally { Pop-Location }
    if ($probeOutput.Count -gt 0) { $probeOutput | Set-Content -LiteralPath $probeLog -Encoding utf8 }

    $manifestPath = Join-Path $sessionDirectory "field-shortcut-validation/field-shortcut-results.json"
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
        throw "Field shortcut probe did not write its manifest. Probe log: $probeLog"
    }
    $evidenceDirectory = Split-Path -Parent $manifestPath
    $savedDocument = Join-Path $resolvedOutputRoot "freew/documents/field-shortcut-fixture.docx"
    $inspectionName = "saved-field-inspection.txt"
    $inspectionPath = Join-Path $evidenceDirectory $inspectionName
    $inspectionArguments = @(
        "run", "--project", $fixtureProject, "--configuration", "Release", "--",
        "inspect", $savedDocument, $expectedTitle
    )
    $inspectionExitCode = 0
    try {
        Invoke-ToolProcess -FilePath "dotnet" -Arguments $inspectionArguments -WorkingDirectory $repoRoot -OutputPath $inspectionPath
    } catch {
        $inspectionExitCode = 1
        $_ | Out-String | Add-Content -LiteralPath $inspectionPath
    }

    Wait-ForManifestEvidence -ManifestPath $manifestPath -EvidenceDirectory $evidenceDirectory
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    $persistResult = @($manifest.results | Where-Object { $_.id -eq "field-update-shortcut-persist" })[0]
    $evidence = [System.Collections.Generic.List[string]]::new()
    foreach ($name in @($persistResult.evidence)) { $evidence.Add([string]$name) }
    if (-not $evidence.Contains($inspectionName)) { $evidence.Add($inspectionName) }
    $persistResult.evidence = $evidence.ToArray()
    if ($inspectionExitCode -ne 0) {
        $persistResult.status = "failed"
        $persistResult.note = "Physical F9/Ctrl+S evidence was collected, but structured DocxReader validation did not prove the expected TITLE cache."
    } else {
        $persistResult.note = "Physical F9/Ctrl+S changed the mounted DOCX and structured DocxReader validation proved the exact deterministic TITLE cache."
    }
    $manifest.summary.passed = @($manifest.results | Where-Object { $_.status -eq "passed" }).Count
    $manifest.summary.failed = @($manifest.results | Where-Object { $_.status -eq "failed" }).Count
    $manifest.summary.total = @($manifest.results).Count
    $manifest | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $manifestPath -Encoding utf8
    $manifest = Assert-ManifestContract -ManifestPath $manifestPath -EvidenceDirectory $evidenceDirectory
    Write-Host "Manifest contract validation: $($manifest.contractValidation.status)"
    Write-Host "Results: $($manifest.summary.passed) passed, $($manifest.summary.failed) failed, $($manifest.summary.total) total"
    Write-Host "Manifest: $manifestPath"
    Write-Host "Fixture: $fixturePath"
    if ($probeExitCode -ne 0 -or $manifest.summary.failed -gt 0) {
        throw "Field shortcut physical validation failed with probe exit code $probeExitCode and $($manifest.summary.failed) failed result(s). Evidence retained at $manifestPath."
    }
} finally {
    if ($started -and -not $KeepContainer) {
        try {
            Invoke-ToolProcess -FilePath "powershell.exe" -Arguments @(
                "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", $genericRunner,
                "-Action", "Stop", "-App", "FreeW", "-Port", "$Port", "-OutputDir", $resolvedOutputRoot
            ) -WorkingDirectory $repoRoot
        } catch { Write-Warning "Could not stop harness-owned FreeW container: $($_.Exception.Message)" }
    } elseif ($started) {
        Write-Host "Container retained by request on port $Port."
    }
}
