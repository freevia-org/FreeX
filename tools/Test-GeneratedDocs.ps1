param(
    [string]$CommandInventoryScriptPath = "tools/Generate-CommandInventoryDocs.ps1",
    [string]$DialogParityInventoryScriptPath = "tools/Generate-DialogParityInventory.ps1",
    [string]$DialogVisualEvidenceSummaryScriptPath = "tools/Generate-DialogVisualEvidenceSummary.ps1",
    [string]$ConditionalFormatOpenedStateEvidenceScriptPath = "tools/Generate-ConditionalFormatOpenedStateEvidence.ps1",
    [string]$CrossAppParityDashboardScriptPath = "tools/Generate-CrossAppParityDashboard.ps1",
    [string]$FreePCommandParityInventoryScriptPath = "tools/Generate-FreePCommandParityInventory.ps1",
    [string]$FreePDialogPaneParityInventoryScriptPath = "tools/Generate-FreePDialogPaneParityInventory.ps1",
    [string]$FreePDialogPaneVisualEvidenceManifestScriptPath = "tools/Generate-FreePDialogPaneVisualEvidenceManifest.ps1",
    [string]$FreePWholeWindowVisualEvidenceManifestScriptPath = "tools/Generate-FreePWholeWindowVisualEvidenceManifest.ps1",
    [string]$FreePPowerPointChromeEvidenceScriptPath = "tools/Test-FreePPowerPointChromeEvidence.ps1",
    [string]$FreeWShellVisualEvidenceScriptPath = "tools/Test-FreeWShellVisualEvidence.ps1",
    [string]$FreeWWordChromeEvidenceScriptPath = "tools/Test-FreeWWordChromeEvidence.ps1",
    [string]$FreeWEditingReferenceParityEvidenceScriptPath = "tools/Generate-FreeWEditingReferenceParityEvidence.ps1",
    [string]$FreeWDesignDialogParityEvidenceScriptPath = "tools/Generate-FreeWDesignDialogParityEvidence.ps1",
    [string]$FreeWMailMergeDialogParityEvidenceScriptPath = "tools/Generate-FreeWMailMergeDialogParityEvidence.ps1",
    [string]$FreeWMediaDialogParityEvidenceScriptPath = "tools/Generate-FreeWMediaDialogParityEvidence.ps1",
    [string]$FreeWPageLayoutDialogParityEvidenceScriptPath = "tools/Generate-FreeWPageLayoutDialogParityEvidence.ps1",
    [string]$FreeWShellPlatformParityEvidenceScriptPath = "tools/Generate-FreeWShellPlatformParityEvidence.ps1",
    [string]$FreeWCommandInventoryScriptPath = "tools/Generate-FreeWCommandInventory.ps1",
    [string]$CrossAppParityDashboardBehaviorScriptPath = "tools/Test-CrossAppParityDashboard.ps1",
    [string]$FreeWDialogVisualEvidenceCheckScriptPath = "tools/Test-FreeWDialogVisualEvidence.ps1",

    [ValidateRange(1, 32)]
    [int]$ThrottleLimit = 8
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot "ToolScriptSupport.ps1")

# These generators must run under PowerShell 7. Windows PowerShell 5.1 serializes JSON
# differently (2-space vs 4-space indent, ": " vs ":  ", and local vs UTC timestamp offsets), so
# running them under 5.1 reports every generated doc as "out of date" and, if regenerated,
# rewrites the whole file in a shape CI then rejects. Fail with an actionable message rather than
# silently falling back to a host that produces different bytes.
$pwshCommand = Get-Command pwsh -ErrorAction SilentlyContinue | Select-Object -First 1
if ($null -eq $pwshCommand) {
    throw "PowerShell 7 (pwsh) is required for the generated-docs checks but was not found. " +
        "Windows PowerShell 5.1 produces different JSON formatting and would report false " +
        "staleness. Install it with: winget install --id Microsoft.PowerShell"
}
$pwshSource = $pwshCommand.Source

function Resolve-GeneratedDocsCheckEntry {
    param(
        [Parameter(Mandatory = $true)][string]$Label,
        [Parameter(Mandatory = $true)][string]$ScriptPath,
        [string[]]$ScriptArguments = @("-Check")
    )

    $resolvedScriptPath = Resolve-ToolRepoPath -Path $ScriptPath -RepoRoot $repoRoot
    if (-not (Test-Path -LiteralPath $resolvedScriptPath)) {
        throw "$Label generated-docs check script was not found: $resolvedScriptPath"
    }
    [pscustomobject]@{
        Label = $Label
        ScriptPath = $resolvedScriptPath
        ScriptArguments = $ScriptArguments
    }
}

function Invoke-GeneratedDocsCheckEntry {
    param(
        [Parameter(Mandatory = $true)][string]$PwshSource,
        [Parameter(Mandatory = $true)]$Entry
    )

    $output = & $PwshSource -NoProfile -File $Entry.ScriptPath @($Entry.ScriptArguments) 2>&1
    [pscustomobject]@{
        Label = $Entry.Label
        Success = ($LASTEXITCODE -eq 0)
        ExitCode = $LASTEXITCODE
        Output = @($output | ForEach-Object { $_.ToString() })
    }
}

function Write-GeneratedDocsResults {
    param(
        [Parameter(Mandatory = $true)][string[]]$OrderedLabels,
        [Parameter(Mandatory = $true)][hashtable]$ResultsByLabel,
        [Parameter(Mandatory = $true)]$Failures
    )

    foreach ($label in $OrderedLabels) {
        $result = $ResultsByLabel[$label]
        Write-Host "Checking $label generated docs..."
        foreach ($line in $result.Output) {
            Write-Host $line
        }
        if (-not $result.Success) {
            $Failures.Add("$label generated-docs check failed with exit code $($result.ExitCode).")
        }
    }
}

# Almost all of these checks are independent, read-only comparisons (each reads its own generated
# doc(s) off disk and reports whether regenerating them would change anything) with no shared
# mutable state, so they are dispatched in parallel to cut wall-clock time dominated by
# per-process PowerShell startup overhead.
#
# Two are held back and run in a separate, always-sequential-with-each-other background job:
# FreeP command parity inventory and FreeW command inventory both build a scratch console project
# that references shared/Free.Shared.Ribbon/Free.Shared.Ribbon.csproj (among other shared
# projects). Running concurrent `dotnet build`s that share a project's obj/bin output is a known
# source of MSBuild lock contention/corruption on this machine (see the
# freex-machine-resource-hygiene memory), so those two never overlap each other - but neither one
# touches a shared project the other 19 checks build or write to, so that job still runs
# concurrently with the parallel batch below.
$parallelEntries = @(
    Resolve-GeneratedDocsCheckEntry -Label "command inventory" -ScriptPath $CommandInventoryScriptPath
    Resolve-GeneratedDocsCheckEntry -Label "dialog parity inventory" -ScriptPath $DialogParityInventoryScriptPath
    Resolve-GeneratedDocsCheckEntry -Label "dialog visual evidence summary" -ScriptPath $DialogVisualEvidenceSummaryScriptPath
    Resolve-GeneratedDocsCheckEntry -Label "conditional-format opened-state evidence" -ScriptPath $ConditionalFormatOpenedStateEvidenceScriptPath
    Resolve-GeneratedDocsCheckEntry -Label "cross-app parity dashboard" -ScriptPath $CrossAppParityDashboardScriptPath
    Resolve-GeneratedDocsCheckEntry -Label "FreeP dialog/pane parity inventory" -ScriptPath $FreePDialogPaneParityInventoryScriptPath
    Resolve-GeneratedDocsCheckEntry -Label "FreeP dialog/pane visual evidence" -ScriptPath $FreePDialogPaneVisualEvidenceManifestScriptPath
    Resolve-GeneratedDocsCheckEntry -Label "FreeP whole-window visual evidence" -ScriptPath $FreePWholeWindowVisualEvidenceManifestScriptPath
    Resolve-GeneratedDocsCheckEntry -Label "FreeP PowerPoint chrome evidence" -ScriptPath $FreePPowerPointChromeEvidenceScriptPath
    Resolve-GeneratedDocsCheckEntry -Label "FreeW shell visual evidence" -ScriptPath $FreeWShellVisualEvidenceScriptPath
    Resolve-GeneratedDocsCheckEntry -Label "FreeW Word chrome evidence" -ScriptPath $FreeWWordChromeEvidenceScriptPath
    Resolve-GeneratedDocsCheckEntry -Label "FreeW editing/reference parity evidence" -ScriptPath $FreeWEditingReferenceParityEvidenceScriptPath
    Resolve-GeneratedDocsCheckEntry -Label "FreeW design-dialog parity evidence" -ScriptPath $FreeWDesignDialogParityEvidenceScriptPath
    Resolve-GeneratedDocsCheckEntry -Label "FreeW mail-merge dialog parity evidence" -ScriptPath $FreeWMailMergeDialogParityEvidenceScriptPath
    Resolve-GeneratedDocsCheckEntry -Label "FreeW media-dialog parity evidence" -ScriptPath $FreeWMediaDialogParityEvidenceScriptPath
    Resolve-GeneratedDocsCheckEntry -Label "FreeW page-layout dialog parity evidence" -ScriptPath $FreeWPageLayoutDialogParityEvidenceScriptPath
    Resolve-GeneratedDocsCheckEntry -Label "FreeW shell platform parity evidence" -ScriptPath $FreeWShellPlatformParityEvidenceScriptPath
    Resolve-GeneratedDocsCheckEntry -Label "cross-app parity dashboard aggregation guards" -ScriptPath $CrossAppParityDashboardBehaviorScriptPath -ScriptArguments @()
    Resolve-GeneratedDocsCheckEntry -Label "FreeW canonical dialog evidence counts and scope" -ScriptPath $FreeWDialogVisualEvidenceCheckScriptPath
)

$buildBasedEntries = @(
    Resolve-GeneratedDocsCheckEntry -Label "FreeP command parity inventory" -ScriptPath $FreePCommandParityInventoryScriptPath
    Resolve-GeneratedDocsCheckEntry -Label "FreeW command inventory" -ScriptPath $FreeWCommandInventoryScriptPath
)

$buildJob = Start-Job -ScriptBlock {
    param($pwshSource, $entries)
    $ErrorActionPreference = "Stop"
    foreach ($entry in $entries) {
        $output = & $pwshSource -NoProfile -File $entry.ScriptPath @($entry.ScriptArguments) 2>&1
        [pscustomobject]@{
            Label = $entry.Label
            Success = ($LASTEXITCODE -eq 0)
            ExitCode = $LASTEXITCODE
            Output = @($output | ForEach-Object { $_.ToString() })
        }
    }
} -ArgumentList $pwshSource, $buildBasedEntries

$parallelResults = $parallelEntries | ForEach-Object -ThrottleLimit $ThrottleLimit -Parallel {
    $entry = $_
    $pwshSource = $using:pwshSource
    $output = & $pwshSource -NoProfile -File $entry.ScriptPath @($entry.ScriptArguments) 2>&1
    [pscustomobject]@{
        Label = $entry.Label
        Success = ($LASTEXITCODE -eq 0)
        ExitCode = $LASTEXITCODE
        Output = @($output | ForEach-Object { $_.ToString() })
    }
}

$buildResults = @(Receive-Job -Job $buildJob -Wait)
Remove-Job -Job $buildJob | Out-Null

# Preserve deterministic, attributable output: print each check's own output grouped under its
# label in the original declared order (not parallel/job completion order), then fail the whole
# gate naming every check that failed, if any did.
$resultsByLabel = @{}
foreach ($result in @($parallelResults) + @($buildResults)) {
    $resultsByLabel[$result.Label] = $result
}
$failures = [System.Collections.Generic.List[string]]::new()
$orderedLabels = @($parallelEntries | ForEach-Object { $_.Label }) + @($buildBasedEntries | ForEach-Object { $_.Label })
Write-GeneratedDocsResults -OrderedLabels $orderedLabels -ResultsByLabel $resultsByLabel -Failures $failures
if ($failures.Count -gt 0) {
    throw ("Generated-docs checks failed:`n - " + ($failures -join "`n - "))
}

Write-Host "Generated documentation checks passed."
