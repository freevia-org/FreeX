<#
.SYNOPSIS
  Runs the focused physical FreeW table-pagination validation lane.
#>
[CmdletBinding()]
param(
    [ValidateRange(1024, 65535)][int]$Port = 6096,
    [ValidateRange(640, 7680)][int]$Width = 1280,
    [ValidateRange(480, 4320)][int]$Height = 820,
    [ValidateRange(72, 240)][int]$Dpi = 96,
    [ValidateSet("2g", "4g", "6g", "8g")][string]$MemoryLimit = "4g",
    [string]$OutputDir = "artifacts/freew-table-pagination",
    [string]$PublishDir = "",
    [switch]$SkipPublish,
    [switch]$SkipImageBuild,
    [switch]$Replace,
    [switch]$ShortOutput,
    [switch]$KeepContainer
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$outputRoot = if ([IO.Path]::IsPathRooted($OutputDir)) { [IO.Path]::GetFullPath($OutputDir) } else { [IO.Path]::GetFullPath((Join-Path $repoRoot $OutputDir)) }
$fixtureDir = Join-Path $outputRoot "fixture"
$fixturePath = Join-Path $fixtureDir "table-page-composition-stress.docx"
$fixtureName = Split-Path -Leaf $fixturePath
$fidelityProject = Join-Path $repoRoot "freew/tools/FreeW.FidelityRender/FreeW.FidelityRender.csproj"
$plannerProject = Join-Path $repoRoot "freew/FreeW.App.Presentation.Tests/FreeW.App.Presentation.Tests.csproj"
$runner = Join-Path $PSScriptRoot "Run-LinuxInteractiveDocker.ps1"
$probe = Join-Path $PSScriptRoot "LinuxInteractiveDocker/run-freew-table-pagination-probe.sh"
$schemaPath = Join-Path $PSScriptRoot "LinuxInteractiveDocker/freew-table-pagination-validation.schema.json"
$requiredIds = @("visible-window-discovery", "generated-fixture-hash-integrity", "physical-third-page-navigation", "nonblank-final-page-render", "shared-plan-proof")

function Invoke-External {
    param([Parameter(Mandatory)][string]$FilePath, [Parameter(Mandatory)][string[]]$Arguments, [string]$WorkingDirectory = $repoRoot, [string]$OutputPath = "")
    Push-Location $WorkingDirectory
    try {
        if ($OutputPath) { & $FilePath @Arguments 2>&1 | Tee-Object -FilePath $OutputPath } else { & $FilePath @Arguments }
        if ($LASTEXITCODE -ne 0) { throw "$FilePath exited with code $LASTEXITCODE." }
    } finally { Pop-Location }
}

function Assert-ManifestContract {
    param([Parameter(Mandatory)][string]$ManifestPath, [Parameter(Mandatory)][string]$EvidenceDirectory)
    $schema = Get-Content -LiteralPath $schemaPath -Raw | ConvertFrom-Json
    if ($schema.'$schema' -notmatch "json-schema.org" -or $schema.title -notmatch "table pagination") { throw "Committed table-pagination schema is not a JSON Schema document." }
    $manifest = Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json
    if ($manifest.schemaVersion -ne 1 -or $manifest.suite -ne "freew-linux-table-pagination-physical" -or $manifest.platform -ne "linux" -or $manifest.shell -ne "avalonia" -or $manifest.app -ne "FreeW" -or $manifest.baseline -ne $false -or $manifest.appSurface -ne "table-page-composition-stress" -or $manifest.window.visible -ne $true -or $manifest.parameters.fixture -ne $fixtureName -or $manifest.parameters.port -ne $Port -or $manifest.parameters.width -ne $Width -or $manifest.parameters.height -ne $Height -or $manifest.parameters.dpi -ne $Dpi -or $manifest.coverage.scope -ne "physical FreeW table pagination and third-page composition evidence lane" -or $manifest.coverage.exhaustive -ne $false -or $manifest.processExitCode -ne 0) { throw "Manifest header violates the committed schema or probe exit contract." }
    $rows = @($manifest.results); $ids = @($rows | ForEach-Object { [string]$_.id })
    if ($rows.Count -ne 5 -or [string]::Join("|", $ids) -ne [string]::Join("|", $requiredIds)) { throw "Manifest result IDs/order violate the exact contract." }
    $files = @{}; Get-ChildItem -LiteralPath $EvidenceDirectory -File | ForEach-Object { $files[$_.Name] = $_.Length }
    for ($i = 0; $i -lt $rows.Count; $i++) {
        $row = $rows[$i]; $physical = $i -lt 4
        $category = if ($physical) { "physical-x11-table-pagination" } else { "deterministic-shared-plan" }
        $level = if ($physical) { "physical-x11-input" } else { "focused-test" }
        if (($row.PSObject.Properties.Name -notcontains "evidenceLevel") -or ($row.PSObject.Properties.Name -contains "level") -or $row.category -ne $category -or $row.evidenceLevel -ne $level -or $row.status -ne "passed" -or @($row.evidence).Count -lt 1 -or [string]::IsNullOrWhiteSpace([string]$row.note)) { throw "Result '$($row.id)' violates the committed schema." }
        foreach ($name in @($row.evidence)) { $n = [string]$name; if ([IO.Path]::GetFileName($n) -ne $n -or $n.Contains("/") -or $n.Contains("\") -or -not $files.ContainsKey($n) -or $files[$n] -le 0) { throw "Result '$($row.id)' references invalid evidence '$n'." } }
    }
    if ($manifest.summary.passed -ne 5 -or $manifest.summary.failed -ne 0 -or $manifest.summary.total -ne 5) { throw "Manifest summary does not satisfy the five-passed contract." }
    $manifest | Add-Member -NotePropertyName contractValidation -NotePropertyValue ([pscustomobject]@{ status = "passed"; validator = "tools/Run-FreeWTablePaginationValidation.ps1"; contractReference = "tools/LinuxInteractiveDocker/freew-table-pagination-validation.schema.json" }) -Force
    $manifest | ConvertTo-Json -Depth 16 | Set-Content -LiteralPath $ManifestPath -Encoding utf8
    $manifest
}

New-Item -ItemType Directory -Path $fixtureDir -Force | Out-Null
$sharedPlanPath = Join-Path $outputRoot "shared-plan-test.txt"
$fidelityOutput = if ($ShortOutput) { Join-Path $outputRoot "fidelity-render.txt" } else { "" }
Invoke-External dotnet @("run", "--project", $fidelityProject, "--configuration", "Release", "--", "--generate-f2-corpus", $fixtureDir) -OutputPath $fidelityOutput
if (-not (Test-Path -LiteralPath $fixturePath -PathType Leaf)) { throw "Generated fixture is missing: $fixturePath" }
$fixtureHash = (Get-FileHash -LiteralPath $fixturePath -Algorithm SHA256).Hash.ToLowerInvariant()
Invoke-External dotnet @("test", $plannerProject, "--configuration", "Release", "--filter", "FullyQualifiedName~DocumentViewLayoutPlannerTests.BuildTableLayoutPlans_AccountsForLeadingDocumentContentWhenEstimatingFirstTablePage", "--logger", "console;verbosity=minimal") -OutputPath $sharedPlanPath

$started = $false
try {
    $startArgs = @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", $runner, "-Action", "Start", "-App", "FreeW", "-Port", "$Port", "-Width", "$Width", "-Height", "$Height", "-Dpi", "$Dpi", "-MemoryLimit", $MemoryLimit, "-OutputDir", $outputRoot, "-DocumentPath", $fixturePath)
    if ($PublishDir) { $startArgs += @("-PublishDir", $PublishDir) }; if ($SkipPublish) { $startArgs += "-SkipPublish" }; if ($SkipImageBuild) { $startArgs += "-SkipImageBuild" }; if ($Replace) { $startArgs += "-Replace" }
    Invoke-External powershell.exe $startArgs; $started = $true
    $session = Get-Content -LiteralPath (Join-Path $outputRoot "freew/current-session.json") -Raw | ConvertFrom-Json
    $sessionDir = [IO.Path]::GetFullPath([string]$session.sessionDirectory)
    Copy-Item -LiteralPath $probe -Destination (Join-Path $sessionDir "run-freew-table-pagination-probe.sh") -Force
    Copy-Item -LiteralPath $fixturePath -Destination (Join-Path $sessionDir "fixture-source.docx") -Force
    Copy-Item -LiteralPath $sharedPlanPath -Destination (Join-Path $sessionDir "shared-plan-test.txt") -Force
    $evidenceDirectory = Join-Path $sessionDir "freew-table-pagination-validation"; New-Item -ItemType Directory -Path $evidenceDirectory -Force | Out-Null
    $containerPathChecks = [ordered]@{
        "mounted-fixture" = "/documents/$fixtureName"
        "source-fixture" = "/work/fixture-source.docx"
        "shared-plan-test" = "/work/shared-plan-test.txt"
    }
    $containerPathLog = [System.Collections.Generic.List[string]]::new()
    foreach ($check in $containerPathChecks.GetEnumerator()) {
        $checkOutput = @(& docker exec $session.containerName test -s $check.Value 2>&1)
        $checkExitCode = $LASTEXITCODE
        $containerPathLog.Add("$($check.Key)=$($check.Value) exit=$checkExitCode")
        foreach ($line in $checkOutput) { $containerPathLog.Add([string]$line) }
        if ($checkExitCode -ne 0) {
            $containerPathLog | Set-Content -LiteralPath (Join-Path $evidenceDirectory "container-path-preflight.txt") -Encoding utf8
            throw "FreeW validation path '$($check.Value)' was not visible as a non-empty file inside the harness container."
        }
    }
    $containerPathLog | Set-Content -LiteralPath (Join-Path $evidenceDirectory "container-path-preflight.txt") -Encoding utf8
    $probeOutput = @(& docker exec --env "FREEW_DOCUMENT_PATH=/documents/$fixtureName" --env "FREEW_SOURCE_FIXTURE_PATH=/work/fixture-source.docx" --env "FREEW_EXPECTED_DOCUMENT_NAME=$fixtureName" --env "FREEW_SHARED_PLAN_TEST_PATH=/work/shared-plan-test.txt" $session.containerName bash /work/run-freew-table-pagination-probe.sh /work/freew-table-pagination-validation 2>&1)
    $probeExitCode = $LASTEXITCODE; $probeOutput | Set-Content -LiteralPath (Join-Path $evidenceDirectory "probe.log") -Encoding utf8
    $manifestPath = Join-Path $evidenceDirectory "results.json"
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) { throw "Probe did not write $manifestPath." }
    Copy-Item -LiteralPath $sharedPlanPath -Destination (Join-Path $evidenceDirectory "shared-plan-test.txt") -Force
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    $sharedRow = @($manifest.results | Where-Object id -eq "shared-plan-proof")[0]
    $sharedRow.status = if ($probeExitCode -eq 0) { "passed" } else { "failed" }; $sharedRow.evidenceLevel = "focused-test"; $sharedRow.category = "deterministic-shared-plan"; $sharedRow.evidence = @("shared-plan-test.txt"); $sharedRow.note = "Focused DocumentViewLayoutPlannerTests proof retained with physical evidence."
    $manifest.parameters = [ordered]@{ fixture = $fixtureName; port = $Port; width = $Width; height = $Height; dpi = $Dpi }; $manifest.coverage.scope = "physical FreeW table pagination and third-page composition evidence lane"; $manifest.contractValidation = [ordered]@{ status = "pending"; validator = "tools/Run-FreeWTablePaginationValidation.ps1"; contractReference = "tools/LinuxInteractiveDocker/freew-table-pagination-validation.schema.json" }
    $manifest.summary.passed = @($manifest.results | Where-Object status -eq "passed").Count; $manifest.summary.failed = @($manifest.results | Where-Object status -eq "failed").Count; $manifest.summary.total = 5
    $manifest | ConvertTo-Json -Depth 16 | Set-Content -LiteralPath $manifestPath -Encoding utf8
    $validated = Assert-ManifestContract $manifestPath $evidenceDirectory
    Write-Host "Manifest contract validation: $($validated.contractValidation.status)"; Write-Host "Manifest: $manifestPath"
    if ($probeExitCode -ne 0 -or $validated.summary.failed -gt 0) { throw "FreeW table-pagination validation failed with probe exit code $probeExitCode." }
}
finally {
    if ($started -and -not $KeepContainer) { try { Invoke-External powershell.exe @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", $runner, "-Action", "Stop", "-App", "FreeW", "-Port", "$Port", "-OutputDir", $outputRoot) } catch { Write-Warning "Could not stop the harness-owned FreeW container." } }
}
