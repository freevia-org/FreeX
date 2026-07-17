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

    [switch]$SkipImageBuild,
    [switch]$SkipPublish
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$harness = Join-Path $PSScriptRoot "Run-LinuxInteractiveDocker.ps1"
$currentSessionPath = Join-Path $repoRoot "artifacts/linux-interactive/freex/current-session.json"
$containerName = "freex-linux-interactive-freex-$Port"
$x11ProbeScript = Join-Path $PSScriptRoot "LinuxInteractiveDocker/run-freex-input-probes.sh"

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

    # Phase two runs deterministic in-process model/dispatch/dialog probes in the same real X11 image.
    & $harness `
        -Action Start `
        -App FreeX `
        -Port $Port `
        -Replace `
        -SkipImageBuild `
        -SkipPublish `
        -AppArgument @("--interaction-validation", "/work/validation")
    if ($LASTEXITCODE -ne 0) {
        throw "Linux interaction-validation harness failed to start."
    }

    $session = Get-Content -LiteralPath $currentSessionPath -Raw | ConvertFrom-Json
    $manifestPath = Join-Path ([string]$session.sessionDirectory) "validation/interaction-validation.json"
    $deadline = (Get-Date).AddMinutes($TimeoutMinutes)
    while ((Get-Date) -lt $deadline -and -not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
        Start-Sleep -Milliseconds 500
    }

    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
        throw "Interaction-validation manifest was not written within $TimeoutMinutes minute(s): $manifestPath"
    }

    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    if ($manifest.error) {
        throw "Interaction validation failed before producing results: $($manifest.error)"
    }

    $manifest.results = @($manifest.results) + @($x11Manifest.results)
    $summary = [ordered]@{}
    foreach ($group in @($manifest.results | Group-Object -Property status)) {
        $summary[[string]$group.Name] = [int]$group.Count
    }
    $summary.total = @($manifest.results).Count
    $manifest.summary = $summary
    [IO.File]::WriteAllText(
        $manifestPath,
        ($manifest | ConvertTo-Json -Depth 12),
        (New-Object Text.UTF8Encoding($false)))

    $reportPath = Join-Path ([string]$session.sessionDirectory) "validation/interaction-validation.html"
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
