<#
.SYNOPSIS
  Run FreeP's bounded deterministic native-picker workflow evidence.

.DESCRIPTION
  Exercises the app-owned Open/Save As lifecycle through focused Avalonia headless
  tests. The tests substitute picker results after the shared picker plan is built;
  they do not emulate or claim native GTK/Windows picker chrome. The generated
  report keeps that foreground-native boundary explicit.
#>
[CmdletBinding()]
param(
    [string]$OutputDir = "artifacts/freep-native-picker-workflow"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$resolvedOutputDir = if ([IO.Path]::IsPathRooted($OutputDir)) {
    [IO.Path]::GetFullPath($OutputDir)
} else {
    [IO.Path]::GetFullPath((Join-Path $repoRoot $OutputDir))
}
$testProject = Join-Path $repoRoot "freep\FreeP.App.Avalonia.Tests\FreeP.App.Avalonia.Tests.csproj"
$trxPath = Join-Path $resolvedOutputDir "native-picker-workflow.trx"
$reportPath = Join-Path $resolvedOutputDir "native-picker-workflow-evidence.json"
$testResultsDirectory = Join-Path (Split-Path $testProject -Parent) "TestResults"

New-Item -ItemType Directory -Path $resolvedOutputDir -Force | Out-Null
if (Test-Path -LiteralPath $trxPath -PathType Leaf) {
    Remove-Item -LiteralPath $trxPath -Force
}
if (Test-Path -LiteralPath $testResultsDirectory -PathType Container) {
    Get-ChildItem -LiteralPath $testResultsDirectory -Filter "native-picker-workflow.trx" -File |
        Remove-Item -Force
}

Push-Location $repoRoot
try {
    & dotnet test $testProject --configuration Release --no-restore `
        --filter "FullyQualifiedName~NativePickerWorkflowEvidenceTests" `
        --logger "trx;LogFileName=native-picker-workflow.trx"
    $testExitCode = $LASTEXITCODE
} finally {
    Pop-Location
}

$generatedTrx = Get-ChildItem -LiteralPath $testResultsDirectory -Filter "native-picker-workflow.trx" -File |
    Select-Object -First 1
if ($null -eq $generatedTrx) {
    throw "Focused picker evidence tests did not produce a TRX report under $testResultsDirectory"
}
Copy-Item -LiteralPath $generatedTrx.FullName -Destination $trxPath -Force

$xml = [xml](Get-Content -LiteralPath $trxPath -Raw)
$results = @($xml.SelectNodes("//*[local-name()='UnitTestResult']"))
$knownTests = [ordered]@{
    "open.cancel" = "OpenPickerCancel_PreservesStateAndRestoresOwnerFocus"
    "open.extension-selection" = "OpenPickerExtensionSelection_LoadsLegacyFxpThroughSharedPlan"
    "open.error" = "OpenPickerError_PreservesCurrentDocumentAndRestoresOwnerFocus"
    "save.overwrite-decline" = "SavePickerDecline_PreservesPathAndDirtyStateAndRestoresOwnerFocus"
    "save.error" = "SavePickerError_DoesNotClearDirtyStateAndRestoresOwnerFocus"
    "save.extension-validation" = "SavePickerUnsupportedExtension_IsRejectedBeforeWriting"
}

$evidenceRows = foreach ($entry in $knownTests.GetEnumerator()) {
    $result = $results | Where-Object { $_.testName -like "*$($entry.Value)" } | Select-Object -First 1
    $passed = $null -ne $result -and [string]$result.outcome -eq "Passed"
    [ordered]@{
        id = $entry.Key
        status = if ($passed) { "passed" } else { "failed" }
        evidenceLevel = "deterministic-workflow"
        test = $entry.Value
        detail = if ($passed) {
            "Focused Avalonia headless test exercised the app-owned workflow after the shared picker plan was built."
        } else {
            "The focused deterministic workflow test was missing or did not pass."
        }
    }
}

$focusPassed = @($evidenceRows | Where-Object {
        $_.status -eq "passed" -and $_.id -in @(
            "open.cancel", "open.error", "save.overwrite-decline", "save.error")
    }).Count -eq 4
$evidenceRows += [ordered]@{
    id = "owner.focus-return"
    status = if ($focusPassed) { "passed" } else { "failed" }
    evidenceLevel = "deterministic-workflow"
    test = "OpenPickerCancel/OpenPickerError/SavePickerDecline/SavePickerError"
    detail = if ($focusPassed) {
        "The FreeP owner-focus callback was observed after picker cancel, open error, declined Save As, and save error."
    } else {
        "At least one deterministic failure prevented the owner-focus aggregate from passing."
    }
}
$evidenceRows += [ordered]@{
    id = "native.foreground-picker"
    status = "not-proven"
    evidenceLevel = "foreground-native-required"
    test = $null
    detail = "GTK/Windows picker chrome, native extension controls, native overwrite confirmation, and OS foreground ownership remain outside this headless validator."
}

$passedCount = @($evidenceRows | Where-Object status -eq "passed").Count
$failedCount = @($evidenceRows | Where-Object status -eq "failed").Count
$notProvenCount = @($evidenceRows | Where-Object status -eq "not-proven").Count
$report = [ordered]@{
    schemaVersion = 1
    suite = "freep-native-picker-workflow"
    platform = "headless-avalonia"
    generatedBy = "tools/Run-FreePNativePickerWorkflowEvidence.ps1"
    nativeUiParity = "not-proven"
    scope = "FreeP app-owned Open/Save As lifecycle after picker result"
    summary = [ordered]@{
        passed = $passedCount
        failed = $failedCount
        notProven = $notProvenCount
        total = $evidenceRows.Count
    }
    tests = $evidenceRows
    artifacts = [ordered]@{
        trx = [IO.Path]::GetFileName($trxPath)
    }
}
$report | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $reportPath -Encoding utf8
Write-Host "Picker workflow evidence: $passedCount passed, $failedCount failed, $notProvenCount not proven"
Write-Host "Report: $reportPath"

if ($testExitCode -ne 0 -or $failedCount -gt 0) {
    throw "FreeP native-picker deterministic evidence failed; report retained at $reportPath"
}
