param(
    [string]$ManifestPath = "eng/test-gates.json"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$manifestFullPath = Join-Path $repoRoot $ManifestPath
if (-not (Test-Path -LiteralPath $manifestFullPath -PathType Leaf)) {
    throw "Test-gate manifest was not found: $ManifestPath"
}

$manifest = Get-Content -LiteralPath $manifestFullPath -Raw | ConvertFrom-Json
if ($manifest.schemaVersion -ne 1) {
    throw "Unsupported test-gate schema version '$($manifest.schemaVersion)'."
}

$errors = [System.Collections.Generic.List[string]]::new()
$allowedApps = @("FreeX", "FreeW", "FreeP")
$allowedGates = @("commit", "release")
$allowedPlatforms = @("windows", "linux", "macos")
$seenGateIds = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
$coveredProjects = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)

foreach ($gate in @($manifest.gates)) {
    if ([string]::IsNullOrWhiteSpace($gate.id) -or -not $seenGateIds.Add($gate.id)) {
        $errors.Add("Test gates must have unique non-empty ids; found '$($gate.id)'.")
    }
    if ($allowedApps -notcontains $gate.app) {
        $errors.Add("Gate '$($gate.id)' has unsupported app '$($gate.app)'.")
    }
    if ($allowedGates -notcontains $gate.gate) {
        $errors.Add("Gate '$($gate.id)' has unsupported type '$($gate.gate)'.")
    }
    if (@($gate.platforms).Count -eq 0 -or @($gate.platforms | Where-Object { $allowedPlatforms -notcontains $_ }).Count -gt 0) {
        $errors.Add("Gate '$($gate.id)' must target one or more supported platforms.")
    }
    if (@($gate.projects).Count -eq 0) {
        $errors.Add("Gate '$($gate.id)' has no test projects.")
        continue
    }

    $projectsInGate = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($projectPath in @($gate.projects)) {
        if ($projectPath -isnot [string] -or [string]::IsNullOrWhiteSpace($projectPath)) {
            $errors.Add("Gate '$($gate.id)' contains an invalid test project path.")
            continue
        }
        if (-not $projectsInGate.Add($projectPath)) {
            $errors.Add("Gate '$($gate.id)' references '$projectPath' more than once.")
        }
        if (-not (Test-Path -LiteralPath (Join-Path $repoRoot $projectPath) -PathType Leaf)) {
            $errors.Add("Gate '$($gate.id)' references missing project '$projectPath'.")
        }
        [void]$coveredProjects.Add($projectPath.Replace('\\', '/'))
    }
}

$testProjects = @(
    Get-ChildItem -LiteralPath $repoRoot -Recurse -File -Filter "*.csproj" |
        Where-Object {
            $_.FullName -notmatch '[\\/](?:bin|obj|\.git|\.worktrees)[\\/]' -and
            $_.Name -match '(?:\.Tests|CaptureTests)(?:\.Batch\d+)?\.csproj$'
        } |
        ForEach-Object { $_.FullName.Substring($repoRoot.Length + 1).Replace('\', '/') }
)
$intentionallyReplacedProjects = @(
    "tests/FreeX.App.Host.Tests/FreeX.App.Host.Tests.csproj"
)
foreach ($projectPath in $testProjects) {
    if ($intentionallyReplacedProjects -contains $projectPath) {
        continue
    }
    if (-not $coveredProjects.Contains($projectPath)) {
        $errors.Add("Test project '$projectPath' is not assigned to a test gate.")
    }
}

function Assert-WorkflowContains {
    param([string]$Path, [string]$Expected)

    $content = Get-Content -LiteralPath (Join-Path $repoRoot $Path) -Raw
    if ($content.IndexOf($Expected, [System.StringComparison]::Ordinal) -lt 0) {
        $errors.Add("$Path must contain '$Expected'.")
    }
}

foreach ($platform in @("windows", "linux", "macos")) {
    Assert-WorkflowContains -Path ".github/workflows/ci.yml" -Expected "-Gate commit -App FreeX -Platform $platform"
}
Assert-WorkflowContains -Path ".github/workflows/freew-ci.yml" -Expected '-Gate commit -App FreeW -Platform ${{ matrix.platform }}'
Assert-WorkflowContains -Path ".github/workflows/freep-ci.yml" -Expected '-Gate commit -App FreeP -Platform ${{ matrix.platform }}'
Assert-WorkflowContains -Path ".github/workflows/tester-release.yml" -Expected '-Gate release -App FreeX -Platform windows'
Assert-WorkflowContains -Path ".github/workflows/app-tester-release.yml" -Expected '-Gate release'
Assert-WorkflowContains -Path ".github/workflows/app-tester-release.yml" -Expected '-App "${{ matrix.app }}"'
Assert-WorkflowContains -Path ".github/workflows/app-tester-release.yml" -Expected '-Platform "${{ matrix.platform }}"'

$gateDocumentation = Get-Content -LiteralPath (Join-Path $repoRoot "docs/testing/test-gates.md") -Raw
foreach ($requiredHeading in @("Commit gate", "Release gate", "all platforms", "Invoke-TestGate.ps1")) {
    if ($gateDocumentation.IndexOf($requiredHeading, [System.StringComparison]::OrdinalIgnoreCase) -lt 0) {
        $errors.Add("docs/testing/test-gates.md must document '$requiredHeading'.")
    }
}

if ($errors.Count -gt 0) {
    throw ("Test-gate contract failed:`n - " + ($errors -join "`n - "))
}

Write-Host "Test-gate contract passed: $($manifest.gates.Count) gates, $($coveredProjects.Count) assigned projects."
