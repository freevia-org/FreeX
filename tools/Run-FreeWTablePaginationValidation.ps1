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
. (Join-Path $PSScriptRoot "ToolScriptSupport.ps1")
$outputRoot = if ([IO.Path]::IsPathRooted($OutputDir)) { [IO.Path]::GetFullPath($OutputDir) } else { [IO.Path]::GetFullPath((Join-Path $repoRoot $OutputDir)) }
$fixtureDir = Join-Path $outputRoot "fixture"
$fixturePath = Join-Path $fixtureDir "table-page-composition-stress.docx"
$fixtureName = Split-Path -Leaf $fixturePath
$fidelityProject = Join-Path $repoRoot "freew/tools/FreeW.FidelityRender/FreeW.FidelityRender.csproj"
$plannerProject = Join-Path $repoRoot "freew/FreeW.App.Presentation.Tests/FreeW.App.Presentation.Tests.csproj"
$avaloniaTableProject = Join-Path $repoRoot "freew/FreeW.App.Avalonia.Tests/FreeW.App.Avalonia.Tests.csproj"
$runner = Join-Path $PSScriptRoot "Run-LinuxInteractiveDocker.ps1"
$probe = Join-Path $PSScriptRoot "LinuxInteractiveDocker/run-freew-table-pagination-probe.sh"
$schemaPath = Join-Path $PSScriptRoot "LinuxInteractiveDocker/freew-table-pagination-validation.schema.json"
$requiredIds = @("visible-window-discovery", "generated-fixture-hash-integrity", "physical-third-page-navigation", "nonblank-final-page-render", "shared-plan-proof")

function Invoke-CapturedExternal {
    param([Parameter(Mandatory)][string]$FilePath, [Parameter(Mandatory)][string[]]$Arguments, [string]$WorkingDirectory = $repoRoot, [string]$OutputPath = "")
    Push-Location $WorkingDirectory
    try {
        $result = Invoke-NativeCaptured $FilePath $Arguments
        foreach ($line in @($result.Output)) { Write-Host $line }
        if ($OutputPath) {
            $outputText = [string]::Join([Environment]::NewLine, [string[]]@($result.Output))
            [IO.File]::WriteAllText($OutputPath, $outputText, (New-Object Text.UTF8Encoding($false)))
        }
        if ($result.ExitCode -ne 0) { throw "$FilePath exited with code $($result.ExitCode)." }
    } finally { Pop-Location }
}

function Invoke-NativeCaptured {
    param([Parameter(Mandatory)][string]$FilePath, [Parameter(Mandatory)][string[]]$Arguments)
    $previousErrorActionPreference = $ErrorActionPreference
    $capturedOutput = @()
    $capturedExitCode = -1
    try {
        $ErrorActionPreference = "Continue"
        $capturedOutput = @(& $FilePath @Arguments 2>&1)
        $capturedExitCode = $LASTEXITCODE
    } finally {
        $ErrorActionPreference = $previousErrorActionPreference
    }
    [pscustomobject]@{
        ExitCode = $capturedExitCode
        Output = @($capturedOutput | ForEach-Object { [string]$_ })
    }
}

function Assert-FocusedTestProof {
    param([Parameter(Mandatory)][string]$Path, [Parameter(Mandatory)][string]$TestName)
    $text = Get-Content -LiteralPath $Path -Raw
    if ($text -notmatch '(?im)Failed:\s*0' -or $text -notmatch '(?im)Passed:\s*[1-9]\d*') {
        throw "Focused test '$TestName' did not produce a passing test summary in '$Path'."
    }
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
        if ($row.id -eq "shared-plan-proof" -and [string]::Join("|", @($row.evidence)) -ne "shared-plan-test.txt|avalonia-table-structure-test.txt") { throw "The shared-plan-proof row must retain both focused-test evidence files." }
        foreach ($name in @($row.evidence)) { $n = [string]$name; if ([IO.Path]::GetFileName($n) -ne $n -or $n.Contains("/") -or $n.Contains("\") -or -not $files.ContainsKey($n) -or $files[$n] -le 0) { throw "Result '$($row.id)' references invalid evidence '$n'." } }
    }
    if ($manifest.summary.passed -ne 5 -or $manifest.summary.failed -ne 0 -or $manifest.summary.total -ne 5) { throw "Manifest summary does not satisfy the five-passed contract." }
    if (Test-Path -LiteralPath (Join-Path $EvidenceDirectory "probe-incomplete.txt")) { throw "Probe completion sentinel remains; the manifest cannot be promoted." }
    $manifest | Add-Member -NotePropertyName contractValidation -NotePropertyValue ([pscustomobject]@{ status = "passed"; validator = "tools/Run-FreeWTablePaginationValidation.ps1"; contractReference = "tools/LinuxInteractiveDocker/freew-table-pagination-validation.schema.json" }) -Force
    $manifest | ConvertTo-Json -Depth 16 | Set-Content -LiteralPath $ManifestPath -Encoding utf8
    $manifest
}

New-Item -ItemType Directory -Path $fixtureDir -Force | Out-Null
$sharedPlanPath = Join-Path $outputRoot "shared-plan-test.txt"
$fidelityOutput = if ($ShortOutput) { Join-Path $outputRoot "fidelity-render.txt" } else { "" }
Invoke-CapturedExternal dotnet @("run", "--project", $fidelityProject, "--configuration", "Release", "--", "--generate-f2-corpus", $fixtureDir) -OutputPath $fidelityOutput
if (-not (Test-Path -LiteralPath $fixturePath -PathType Leaf)) { throw "Generated fixture is missing: $fixturePath" }
$fixtureHash = (Get-FileHash -LiteralPath $fixturePath -Algorithm SHA256).Hash.ToLowerInvariant()
Invoke-CapturedExternal dotnet @("test", $plannerProject, "--configuration", "Release", "--filter", "FullyQualifiedName~DocumentViewLayoutPlannerTests.BuildTableLayoutPlans_AccountsForLeadingDocumentContentWhenEstimatingFirstTablePage", "--logger", "console;verbosity=minimal") -OutputPath $sharedPlanPath
Assert-FocusedTestProof $sharedPlanPath "DocumentViewLayoutPlannerTests.BuildTableLayoutPlans_AccountsForLeadingDocumentContentWhenEstimatingFirstTablePage"
$avaloniaTablePath = Join-Path $outputRoot "avalonia-table-structure-test.txt"
Invoke-CapturedExternal dotnet @("test", $avaloniaTableProject, "--configuration", "Release", "--filter", "FullyQualifiedName~DocumentViewTableStructureTests.TablePageCompositionStress_UsesSharedPlanForThreeRenderedPages", "--logger", "console;verbosity=minimal") -OutputPath $avaloniaTablePath
Assert-FocusedTestProof $avaloniaTablePath "DocumentViewTableStructureTests.TablePageCompositionStress_UsesSharedPlanForThreeRenderedPages"

$started = $false
try {
    $startArgs = @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", $runner, "-Action", "Start", "-App", "FreeW", "-Port", "$Port", "-Width", "$Width", "-Height", "$Height", "-Dpi", "$Dpi", "-MemoryLimit", $MemoryLimit, "-OutputDir", $outputRoot, "-DocumentPath", $fixturePath)
    if ($PublishDir) { $startArgs += @("-PublishDir", $PublishDir) }; if ($SkipPublish) { $startArgs += "-SkipPublish" }; if ($SkipImageBuild) { $startArgs += "-SkipImageBuild" }; if ($Replace) { $startArgs += "-Replace" }
    Invoke-ToolProcess -FilePath "powershell.exe" -Arguments $startArgs -WorkingDirectory $repoRoot; $started = $true
    $session = Get-Content -LiteralPath (Join-Path $outputRoot "freew/current-session.json") -Raw | ConvertFrom-Json
    $sessionDir = [IO.Path]::GetFullPath([string]$session.sessionDirectory)
    Copy-Item -LiteralPath $probe -Destination (Join-Path $sessionDir "run-freew-table-pagination-probe.sh") -Force
    Copy-Item -LiteralPath $fixturePath -Destination (Join-Path $sessionDir "fixture-source.docx") -Force
    Copy-Item -LiteralPath $sharedPlanPath -Destination (Join-Path $sessionDir "shared-plan-test.txt") -Force
    Copy-Item -LiteralPath $avaloniaTablePath -Destination (Join-Path $sessionDir "avalonia-table-structure-test.txt") -Force
    $evidenceDirectory = Join-Path $sessionDir "freew-table-pagination-validation"; New-Item -ItemType Directory -Path $evidenceDirectory -Force | Out-Null
    $containerPathChecks = [ordered]@{
        "mounted-fixture" = "/documents/$fixtureName"
        "source-fixture" = "/work/fixture-source.docx"
        "shared-plan-test" = "/work/shared-plan-test.txt"
        "avalonia-table-structure-test" = "/work/avalonia-table-structure-test.txt"
    }
    $containerPathLog = [System.Collections.Generic.List[string]]::new()
    foreach ($check in $containerPathChecks.GetEnumerator()) {
        $checkResult = Invoke-NativeCaptured "docker" @("exec", $session.containerName, "test", "-s", $check.Value)
        $checkOutput = @($checkResult.Output)
        $checkExitCode = $checkResult.ExitCode
        $containerPathLog.Add("$($check.Key)=$($check.Value) exit=$checkExitCode")
        foreach ($line in $checkOutput) { $containerPathLog.Add([string]$line) }
        if ($checkExitCode -ne 0) {
            $containerPathLog | Set-Content -LiteralPath (Join-Path $evidenceDirectory "container-path-preflight.txt") -Encoding utf8
            throw "FreeW validation path '$($check.Value)' was not visible as a non-empty file inside the harness container."
        }
    }
    $containerPathLog | Set-Content -LiteralPath (Join-Path $evidenceDirectory "container-path-preflight.txt") -Encoding utf8
    $probeResult = Invoke-NativeCaptured "docker" @("exec", "--env", "FREEW_DOCUMENT_PATH=/documents/$fixtureName", "--env", "FREEW_SOURCE_FIXTURE_PATH=/work/fixture-source.docx", "--env", "FREEW_EXPECTED_DOCUMENT_NAME=$fixtureName", "--env", "FREEW_SHARED_PLAN_TEST_PATH=/work/shared-plan-test.txt", "--env", "FREEW_AVALONIA_TABLE_TEST_PATH=/work/avalonia-table-structure-test.txt", $session.containerName, "bash", "/work/run-freew-table-pagination-probe.sh", "/work/freew-table-pagination-validation")
    $probeExitCode = $probeResult.ExitCode
    $probeLogPath = Join-Path $evidenceDirectory "probe.log"
    $probeLogText = [string]::Join([Environment]::NewLine, [string[]]@($probeResult.Output))
    [IO.File]::WriteAllText($probeLogPath, $probeLogText, (New-Object Text.UTF8Encoding($false)))
    $manifestPath = Join-Path $evidenceDirectory "results.json"
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) { throw "Probe did not write $manifestPath (docker exec exit code $probeExitCode). See probe.log." }
    Copy-Item -LiteralPath $sharedPlanPath -Destination (Join-Path $evidenceDirectory "shared-plan-test.txt") -Force
    Copy-Item -LiteralPath $avaloniaTablePath -Destination (Join-Path $evidenceDirectory "avalonia-table-structure-test.txt") -Force
    if ($probeExitCode -ne 0) { throw "FreeW table-pagination probe exited with code $probeExitCode. See probe.log and results.json for the retained failure evidence." }
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    $manifest.parameters = [ordered]@{ fixture = $fixtureName; port = $Port; width = $Width; height = $Height; dpi = $Dpi }; $manifest.coverage.scope = "physical FreeW table pagination and third-page composition evidence lane"; $manifest.contractValidation = [ordered]@{ status = "pending"; validator = "tools/Run-FreeWTablePaginationValidation.ps1"; contractReference = "tools/LinuxInteractiveDocker/freew-table-pagination-validation.schema.json" }
    $manifest.summary.passed = @($manifest.results | Where-Object status -eq "passed").Count; $manifest.summary.failed = @($manifest.results | Where-Object status -eq "failed").Count; $manifest.summary.total = 5
    $manifest | ConvertTo-Json -Depth 16 | Set-Content -LiteralPath $manifestPath -Encoding utf8
    $validated = Assert-ManifestContract $manifestPath $evidenceDirectory
    Write-Host "Manifest contract validation: $($validated.contractValidation.status)"; Write-Host "Manifest: $manifestPath"
    if ($probeExitCode -ne 0 -or $validated.summary.failed -gt 0) { throw "FreeW table-pagination validation failed with probe exit code $probeExitCode." }
}
finally {
    if ($started -and -not $KeepContainer) { try { Invoke-ToolProcess -FilePath "powershell.exe" -Arguments @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", $runner, "-Action", "Stop", "-App", "FreeW", "-Port", "$Port", "-OutputDir", $outputRoot) -WorkingDirectory $repoRoot } catch { Write-Warning "Could not stop the harness-owned FreeW container." } }
}
