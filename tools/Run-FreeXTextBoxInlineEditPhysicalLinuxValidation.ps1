[CmdletBinding()]
param(
    [ValidateRange(1024, 65535)][int]$Port = 6096,
    [ValidateRange(640, 7680)][int]$Width = 1280,
    [ValidateRange(480, 4320)][int]$Height = 820,
    [ValidateRange(72, 240)][int]$Dpi = 96,
    [ValidateSet("2g", "4g", "6g", "8g")][string]$MemoryLimit = "4g",
    [string]$OutputDir = "artifacts/freex-textbox-inline-edit-physical-wave93",
    [switch]$SkipPublish,
    [switch]$SkipImageBuild,
    [switch]$Replace
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$resolvedOutput = [IO.Path]::GetFullPath((Join-Path $repoRoot $OutputDir))
$sessionMetadata = Join-Path $resolvedOutput "session.json"
$fixturePath = Join-Path $resolvedOutput "freex-wave93-textbox-fixture.xlsx"
$probePath = Join-Path $repoRoot "tools/LinuxInteractiveDocker/run-freex-textbox-inline-edit-physical.sh"
$schemaPath = Join-Path $repoRoot "tools/LinuxInteractiveDocker/freex-textbox-inline-edit-physical.schema.json"
$runnerPath = Join-Path $repoRoot "tools/Run-LinuxInteractiveDocker.ps1"
New-Item -ItemType Directory -Path $resolvedOutput -Force | Out-Null
& (Join-Path $repoRoot "tools/LinuxInteractiveDocker/New-FreeXWave93TextBoxFixture.ps1") -OutputPath $fixturePath

$startArguments = @{
    Action = "Start"; App = "FreeX"; Port = $Port; Width = $Width; Height = $Height; Dpi = $Dpi
    MemoryLimit = $MemoryLimit; OutputDir = $resolvedOutput; SessionMetadataPath = $sessionMetadata
    DocumentPath = $fixturePath
    AppEnvironment = @("FREEX_TEXTBOX_INLINE_PHYSICAL_RESULT=/work/freex-textbox-inline-physical.json")
}
if ($SkipPublish) { $startArguments.SkipPublish = $true }
if ($SkipImageBuild) { $startArguments.SkipImageBuild = $true }
if ($Replace) { $startArguments.Replace = $true }

$container = $null
try {
    & $runnerPath @startArguments
    $session = Get-Content -LiteralPath $sessionMetadata -Raw | ConvertFrom-Json
    $container = [string]$session.containerName
    & docker cp $probePath "${container}:/work/freex-textbox-inline-edit-physical.sh"
    & docker cp $schemaPath "${container}:/work/freex-textbox-inline-edit-physical.schema.json"
    & docker exec $container chmod 0755 /work/freex-textbox-inline-edit-physical.sh
    & docker exec --env DISPLAY=:99 --env FREEX_TEXTBOX_DOCUMENT=/documents/$(Split-Path -Leaf $fixturePath) $container /work/freex-textbox-inline-edit-physical.sh /work/freex-textbox-inline-edit-physical
    if ($LASTEXITCODE -ne 0) { throw "The FreeX TextBox inline-edit physical probe failed." }

    $schemaValidationCode = @'
import json
from jsonschema import validate

with open("/work/freex-textbox-inline-edit-physical.schema.json", encoding="utf-8") as schema_file:
    schema = json.load(schema_file)
with open("/work/freex-textbox-inline-edit-physical/results.json", encoding="utf-8") as manifest_file:
    manifest = json.load(manifest_file)
validate(instance=manifest, schema=schema)
print("FreeX TextBox inline-edit manifest JSON Schema validation passed")
'@
    & docker exec $container python3 -c $schemaValidationCode
    if ($LASTEXITCODE -ne 0) { throw "The FreeX TextBox inline-edit physical manifest failed JSON Schema validation." }

    & docker cp "${container}:/work/freex-textbox-inline-edit-physical/results.json" (Join-Path $resolvedOutput "results.json")
    & docker cp "${container}:/work/freex-textbox-inline-edit-physical" (Join-Path $resolvedOutput "evidence")
    & docker cp "${container}:/work/freex-textbox-inline-physical.json" (Join-Path $resolvedOutput "runtime-observations.json")
    Copy-Item -LiteralPath $schemaPath -Destination (Join-Path $resolvedOutput "schema.json") -Force

    $manifest = Get-Content -LiteralPath (Join-Path $resolvedOutput "results.json") -Raw | ConvertFrom-Json
    if ($manifest.schemaVersion -ne 1 -or $manifest.suite -ne "freex-linux-textbox-inline-edit-physical" -or
        $manifest.platform -ne "linux" -or $manifest.shell -ne "avalonia" -or $manifest.app -ne "FreeX" -or
        $manifest.summary.total -ne 6 -or $manifest.summary.passed -ne 6 -or $manifest.summary.failed -ne 0) {
        throw "The FreeX TextBox inline-edit physical manifest failed its root/count contract."
    }
    if (@($manifest.screenshots).Count -ne 5 -or @($manifest.results).Count -ne 6 -or
        @($manifest.results | Where-Object status -ne "passed").Count -ne 0) {
        throw "The FreeX TextBox inline-edit physical manifest is incomplete or contains failed rows."
    }
    foreach ($shot in @($manifest.screenshots)) {
        if ([int]$shot.width -le 0 -or [int]$shot.height -le 0) { throw "Screenshot dimensions are invalid: $($shot.name)" }
    }
    Write-Host "PASS: FreeX drawing TextBox inline-edit physical Linux validation"
    Write-Host "Artifacts: $resolvedOutput"
}
finally {
    if ($null -ne $container) {
        & $runnerPath -Action Stop -App FreeX -Port $Port -OutputDir $resolvedOutput
    }
}
