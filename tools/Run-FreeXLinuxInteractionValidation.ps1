<#
.SYNOPSIS
  Runs the exhaustive FreeX Avalonia interaction-validation matrix in the Linux Docker desktop.

.DESCRIPTION
  Publishes the current FreeX Avalonia build, first drives production keyboard and pointer input
  through X11, then asks the app to validate every catalogued interaction surface and model route.
  The two evidence streams are merged into one JSON/HTML report. Only the harness-owned container
  on the requested port is stopped.
#>
[CmdletBinding()]
param(
    [ValidateRange(1024, 65535)]
    [int]$Port = 6082,

    [ValidateRange(1, 60)]
    [int]$TimeoutMinutes = 20,

    [ValidateRange(1, 20)]
    [int]$DialogBatchSize = 1,

    [ValidateRange(1, 32)]
    [int]$RibbonBatchSize = 8,

    [switch]$SkipImageBuild,
    [switch]$SkipPublish
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$harness = Join-Path $PSScriptRoot "Run-LinuxInteractiveDocker.ps1"
$currentSessionPath = Join-Path $repoRoot "artifacts/linux-interactive/freex/current-session.json"
$containerName = "freex-linux-interactive-freex-$Port"
$x11ProbeScript = Join-Path $PSScriptRoot "LinuxInteractiveDocker/run-freex-input-probes.sh"
$reportStamp = (Get-Date).ToUniversalTime().ToString("yyyyMMddTHHmmssZ")
$reportDirectory = Join-Path $repoRoot "artifacts/linux-interactive/freex/interaction-validation/$reportStamp"
New-Item -ItemType Directory -Path $reportDirectory -Force | Out-Null

function Read-CompletedJsonManifest {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][datetime]$Deadline
    )

    while ((Get-Date) -lt $Deadline) {
        if (Test-Path -LiteralPath $Path -PathType Leaf) {
            try {
                return Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
            } catch {
                # The app writes a large manifest directly; file existence can precede the final byte.
            }
        }
        Start-Sleep -Milliseconds 500
    }
    return $null
}

$desktopStartArguments = @{
    Action = "Start"
    App = "FreeX"
    Port = $Port
    Replace = $true
}
if ($SkipImageBuild) { $desktopStartArguments.SkipImageBuild = $true }
if ($SkipPublish) { $desktopStartArguments.SkipPublish = $true }

try {
    # Phase one sends real X11 keyboard and pointer events through the production handlers.
    & $harness @desktopStartArguments
    if ($LASTEXITCODE -ne 0) {
        throw "Linux X11 input-validation harness failed to start."
    }

    $x11Session = Get-Content -LiteralPath $currentSessionPath -Raw | ConvertFrom-Json
    & docker cp $x11ProbeScript "${containerName}:/tmp/run-freex-input-probes.sh"
    if ($LASTEXITCODE -ne 0) { throw "Could not copy X11 input probes into '$containerName'." }
    & docker exec --env DISPLAY=:99 $containerName bash /tmp/run-freex-input-probes.sh /work/x11-validation
    $x11ProbeExit = $LASTEXITCODE
    $x11ManifestPath = Join-Path ([string]$x11Session.sessionDirectory) "x11-validation/x11-input-results.json"
    if (-not (Test-Path -LiteralPath $x11ManifestPath -PathType Leaf)) {
        throw "X11 input probes did not write a result manifest (exit $x11ProbeExit): $x11ManifestPath"
    }
    $x11Manifest = Get-Content -LiteralPath $x11ManifestPath -Raw | ConvertFrom-Json

    & $harness -Action Stop -App FreeX -Port $Port

    # Phase two uses a fresh X11 process for each bounded dialog slice. Avalonia retains native modal/input
    # resources across repeated closes, so one 120-dialog process is not a reliable validation boundary.
    $manifest = $null
    $combinedResults = @()
    $authoritativeDialogCount = $null
    $authoritativeRibbonCount = $null
    $coreSections = @("ribbon-bindings", "shortcuts", "context-menus", "range-inventory", "editing")
    foreach ($coreSection in $coreSections) {
        $appArguments = @(
            "--interaction-validation", "/work/validation",
            "--interaction-validation-dialog-start", "0",
            "--interaction-validation-dialog-count", "0",
            "--interaction-validation-ribbon-start", "0",
            "--interaction-validation-ribbon-count", "0",
            "--interaction-validation-core-section", $coreSection
        )
        Write-Host "Running core interaction section '$coreSection'..."
        & $harness -Action Start -App FreeX -Port $Port -Replace -SkipImageBuild -SkipPublish -AppArgument $appArguments
        if ($LASTEXITCODE -ne 0) {
            throw "Linux core interaction section '$coreSection' failed to start."
        }

        $session = Get-Content -LiteralPath $currentSessionPath -Raw | ConvertFrom-Json
        $batchManifestPath = Join-Path ([string]$session.sessionDirectory) "validation/interaction-validation.json"
        $deadline = (Get-Date).AddMinutes($TimeoutMinutes)
        $batchManifest = Read-CompletedJsonManifest -Path $batchManifestPath -Deadline $deadline
        if ($null -eq $batchManifest) {
            $appLogPath = Join-Path ([string]$session.sessionDirectory) "logs/app.log"
            $appLog = if (Test-Path -LiteralPath $appLogPath) { Get-Content -LiteralPath $appLogPath -Raw } else { "" }
            throw "Core interaction section '$coreSection' did not write a complete manifest within $TimeoutMinutes minute(s).`n$appLog"
        }
        if ($batchManifest.error) {
            throw "Core interaction section '$coreSection' failed: $($batchManifest.error)"
        }

        if ($null -eq $authoritativeDialogCount) {
            $authoritativeDialogCount = [int]$batchManifest.dialogCatalogCount
            $authoritativeRibbonCount = [int]$batchManifest.ribbonCommandCatalogCount
            if ($authoritativeDialogCount -le 0 -or $authoritativeRibbonCount -le 0) {
                throw "Interaction validation reported invalid catalog counts."
            }
            Write-Host "Authoritative dialog routes: $authoritativeDialogCount"
            Write-Host "Authoritative ribbon commands: $authoritativeRibbonCount"
        }
        if ($null -eq $manifest) { $manifest = $batchManifest }
        $combinedResults += @($batchManifest.results)
        Copy-Item -LiteralPath $batchManifestPath -Destination (Join-Path $reportDirectory "core-$coreSection.json") -Force
        & $harness -Action Stop -App FreeX -Port $Port
    }

    for ($dialogStart = 0; $null -eq $authoritativeDialogCount -or $dialogStart -lt $authoritativeDialogCount; $dialogStart += $DialogBatchSize) {
        $dialogCount = if ($null -eq $authoritativeDialogCount) {
            $DialogBatchSize
        } else {
            [Math]::Min($DialogBatchSize, $authoritativeDialogCount - $dialogStart)
        }
        $appArguments = @(
            "--interaction-validation", "/work/validation",
            "--interaction-validation-dialog-start", [string]$dialogStart,
            "--interaction-validation-dialog-count", [string]$dialogCount,
            "--interaction-validation-dialog-only"
        )

        Write-Host "Running dialog interaction batch $dialogStart..$($dialogStart + $dialogCount - 1)..."
        $batchStartArguments = @{
            Action = "Start"
            App = "FreeX"
            Port = $Port
            Replace = $true
            SkipImageBuild = $true
            SkipPublish = $true
            AppArgument = $appArguments
        }
        & $harness @batchStartArguments
        if ($LASTEXITCODE -ne 0) {
            throw "Linux interaction-validation batch starting at $dialogStart failed to start."
        }

        $session = Get-Content -LiteralPath $currentSessionPath -Raw | ConvertFrom-Json
        $batchManifestPath = Join-Path ([string]$session.sessionDirectory) "validation/interaction-validation.json"
        $deadline = (Get-Date).AddMinutes($TimeoutMinutes)
        $batchManifest = Read-CompletedJsonManifest -Path $batchManifestPath -Deadline $deadline
        if ($null -eq $batchManifest) {
            $appLogPath = Join-Path ([string]$session.sessionDirectory) "logs/app.log"
            $appLog = if (Test-Path -LiteralPath $appLogPath) { Get-Content -LiteralPath $appLogPath -Raw } else { "" }
            throw "Interaction-validation batch $dialogStart did not write a complete manifest within $TimeoutMinutes minute(s): $batchManifestPath`n$appLog"
        }

        if ($batchManifest.error) {
            throw "Interaction validation batch $dialogStart failed before producing results: $($batchManifest.error)"
        }
        if ($null -eq $authoritativeDialogCount) {
            $authoritativeDialogCount = [int]$batchManifest.dialogCatalogCount
            if ($authoritativeDialogCount -le 0) {
                throw "Interaction validation reported an invalid dialog catalog count: $authoritativeDialogCount"
            }
            Write-Host "Authoritative dialog routes: $authoritativeDialogCount"
        } elseif ([int]$batchManifest.dialogCatalogCount -ne $authoritativeDialogCount) {
            throw "Dialog catalog count changed during validation: expected $authoritativeDialogCount, observed $($batchManifest.dialogCatalogCount)."
        }
        if ($null -eq $authoritativeRibbonCount) {
            $authoritativeRibbonCount = [int]$batchManifest.ribbonCommandCatalogCount
            if ($authoritativeRibbonCount -le 0) {
                throw "Interaction validation reported an invalid ribbon command catalog count: $authoritativeRibbonCount"
            }
            Write-Host "Authoritative ribbon commands: $authoritativeRibbonCount"
        } elseif ([int]$batchManifest.ribbonCommandCatalogCount -ne $authoritativeRibbonCount) {
            throw "Ribbon command catalog count changed during validation: expected $authoritativeRibbonCount, observed $($batchManifest.ribbonCommandCatalogCount)."
        }
        if ($null -eq $manifest) { $manifest = $batchManifest }
        $combinedResults += @($batchManifest.results)
        Copy-Item -LiteralPath $batchManifestPath -Destination (Join-Path $reportDirectory ("batch-{0:D3}.json" -f $dialogStart)) -Force
        & $harness -Action Stop -App FreeX -Port $Port
    }

    # Ribbon commands are isolated into bounded app processes. Production ribbon dispatch can rebuild
    # substantial visual state, and Avalonia retains some subscriptions until process shutdown.
    for ($ribbonStart = 0; $ribbonStart -lt $authoritativeRibbonCount; $ribbonStart += $RibbonBatchSize) {
        $ribbonCount = [Math]::Min($RibbonBatchSize, $authoritativeRibbonCount - $ribbonStart)
        $appArguments = @(
            "--interaction-validation", "/work/validation",
            "--interaction-validation-dialog-start", "0",
            "--interaction-validation-dialog-count", "0",
            "--interaction-validation-ribbon-start", [string]$ribbonStart,
            "--interaction-validation-ribbon-count", [string]$ribbonCount,
            "--interaction-validation-ribbon-only"
        )
        Write-Host "Running ribbon interaction batch $ribbonStart..$($ribbonStart + $ribbonCount - 1)..."
        & $harness -Action Start -App FreeX -Port $Port -Replace -SkipImageBuild -SkipPublish -AppArgument $appArguments
        if ($LASTEXITCODE -ne 0) {
            throw "Linux ribbon interaction-validation batch starting at $ribbonStart failed to start."
        }

        $session = Get-Content -LiteralPath $currentSessionPath -Raw | ConvertFrom-Json
        $batchManifestPath = Join-Path ([string]$session.sessionDirectory) "validation/interaction-validation.json"
        $deadline = (Get-Date).AddMinutes($TimeoutMinutes)
        $batchManifest = Read-CompletedJsonManifest -Path $batchManifestPath -Deadline $deadline
        if ($null -eq $batchManifest) {
            throw "Ribbon interaction-validation batch $ribbonStart did not write a complete manifest within $TimeoutMinutes minute(s): $batchManifestPath"
        }

        if ($batchManifest.error) {
            throw "Ribbon interaction validation batch $ribbonStart failed before producing results: $($batchManifest.error)"
        }
        if ([int]$batchManifest.ribbonCommandCatalogCount -ne $authoritativeRibbonCount) {
            throw "Ribbon command catalog count changed during validation: expected $authoritativeRibbonCount, observed $($batchManifest.ribbonCommandCatalogCount)."
        }
        $combinedResults += @($batchManifest.results)
        Copy-Item -LiteralPath $batchManifestPath -Destination (Join-Path $reportDirectory ("ribbon-batch-{0:D3}.json" -f $ribbonStart)) -Force
        & $harness -Action Stop -App FreeX -Port $Port
    }

    $rangeInventory = @($combinedResults | Where-Object category -eq "range-selection-inventory")
    $rangeInteractionRows = @($combinedResults | Where-Object category -eq "range-selection")
    $deduplicatedRangeRows = foreach ($group in @($rangeInteractionRows | Group-Object id)) {
        $candidates = @($group.Group | Where-Object status -eq "failed" | Select-Object -First 1) +
            @($group.Group | Where-Object status -ne "failed" | Select-Object -First 1)
        $candidates | Select-Object -First 1
    }
    $observedRangeIds = @($deduplicatedRangeRows | Select-Object -ExpandProperty id -Unique)
    $missingRangeRows = foreach ($inventoryRow in $rangeInventory) {
        if ($observedRangeIds -contains [string]$inventoryRow.id) { continue }
        [pscustomobject]@{
            id = [string]$inventoryRow.id
            category = "range-selection"
            status = "failed"
            evidenceLevel = "registered-not-exercised"
            evidence = [string]$inventoryRow.evidence
            note = "No production picker apply/cancel evidence was observed across the complete dialog run."
        }
    }
    $combinedResults = @($combinedResults | Where-Object category -ne "range-selection") +
        @($deduplicatedRangeRows) + @($missingRangeRows)

    $dialogContractIds = @($combinedResults | Where-Object category -eq "dialog-contract" | Select-Object -ExpandProperty id -Unique)
    if ($dialogContractIds.Count -ne $authoritativeDialogCount) {
        $combinedResults += [pscustomobject]@{
            id = "validation.dialog-catalog-completeness"
            category = "validation-completeness"
            status = "failed"
            evidenceLevel = "catalog-count-mismatch"
            evidence = "expected=$authoritativeDialogCount; observed=$($dialogContractIds.Count)"
            note = "Every authoritative production dialog route must emit exactly one keyboard/focus contract row."
        }
    }

    $manifest.results = @($combinedResults) + @($x11Manifest.results)
    $summary = [ordered]@{}
    foreach ($group in @($manifest.results | Group-Object -Property status)) {
        $summary[[string]$group.Name] = [int]$group.Count
    }
    $summary.total = @($manifest.results).Count
    $manifest.summary = $summary
    $manifestPath = Join-Path $reportDirectory "interaction-validation.json"
    [IO.File]::WriteAllText(
        $manifestPath,
        ($manifest | ConvertTo-Json -Depth 12),
        (New-Object Text.UTF8Encoding($false)))

    $reportPath = Join-Path $reportDirectory "interaction-validation.html"
    $rows = foreach ($result in $manifest.results) {
        $statusClass = [System.Net.WebUtility]::HtmlEncode([string]$result.status)
        $id = [System.Net.WebUtility]::HtmlEncode([string]$result.id)
        $category = [System.Net.WebUtility]::HtmlEncode([string]$result.category)
        $level = [System.Net.WebUtility]::HtmlEncode([string]$result.evidenceLevel)
        $evidence = [System.Net.WebUtility]::HtmlEncode([string]$result.evidence)
        $note = [System.Net.WebUtility]::HtmlEncode([string]$result.note)
        "<tr class='$statusClass'><td>$statusClass</td><td>$category</td><td>$id</td><td>$level</td><td>$evidence</td><td>$note</td></tr>"
    }
    $summaryText = ($manifest.summary.PSObject.Properties | ForEach-Object {
        "<strong>$([System.Net.WebUtility]::HtmlEncode($_.Name))</strong>: $($_.Value)"
    }) -join " &nbsp; "
    $categoryOptions = @($manifest.results | Select-Object -ExpandProperty category -Unique | Sort-Object) | ForEach-Object {
        $encoded = [System.Net.WebUtility]::HtmlEncode([string]$_)
        "<option value='$encoded'>$encoded</option>"
    }
    $html = @"
<!doctype html>
<html lang="en"><head><meta charset="utf-8"><title>FreeX Linux interaction validation</title>
<style>
body{font:13px Segoe UI,Arial,sans-serif;margin:24px;color:#202124}h1{font-size:22px}table{border-collapse:collapse;width:100%}
th,td{border:1px solid #d0d5da;padding:6px 8px;text-align:left;vertical-align:top}th{background:#eef1f4;position:sticky;top:0}
tr.failed{background:#fde7e9}tr.skipped{background:#fff4ce}tr.passed td:first-child{color:#107c10;font-weight:600}
</style></head><body><h1>FreeX Linux interaction validation</h1><p>$summaryText</p>
<p><input id="query" type="search" placeholder="Filter interactions" style="width:320px;padding:6px">
<select id="status" style="padding:6px"><option value="">All statuses</option><option>passed</option><option>failed</option><option>skipped</option></select>
<select id="category" style="padding:6px"><option value="">All categories</option>$($categoryOptions -join '')</select>
<span id="visibleCount"></span></p>
<table><thead><tr><th>Status</th><th>Category</th><th>Interaction</th><th>Evidence level</th><th>Evidence</th><th>Note</th></tr></thead>
<tbody>$($rows -join [Environment]::NewLine)</tbody></table>
<script>
const rows=[...document.querySelectorAll('tbody tr')], q=document.querySelector('#query'), s=document.querySelector('#status'), c=document.querySelector('#category'), n=document.querySelector('#visibleCount');
function filter(){let shown=0; for(const row of rows){const ok=(!q.value||row.textContent.toLowerCase().includes(q.value.toLowerCase()))&&(!s.value||row.cells[0].textContent===s.value)&&(!c.value||row.cells[1].textContent===c.value);row.hidden=!ok;if(ok)shown++;}n.textContent=shown+' of '+rows.length+' rows';}
q.addEventListener('input',filter);s.addEventListener('change',filter);c.addEventListener('change',filter);filter();
</script></body></html>
"@
    [IO.File]::WriteAllText($reportPath, $html, (New-Object Text.UTF8Encoding($false)))

    Write-Host "Manifest: $manifestPath"
    Write-Host "Report  : $reportPath"
    Write-Host "Summary : $summaryText"

    if ($x11ProbeExit -ne 0 -or [int]$manifest.summary.failed -gt 0) {
        exit 1
    }
} finally {
    & $harness -Action Stop -App FreeX -Port $Port
}
