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
$coveredProjectPlatforms = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
$staticPreflightOwners = [System.Collections.Generic.List[string]]::new()
$platformPreflightOwners = @{
    windows = [System.Collections.Generic.List[string]]::new()
    linux = [System.Collections.Generic.List[string]]::new()
    macos = [System.Collections.Generic.List[string]]::new()
}

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
    if ($gate.gate -eq "commit" -and @($gate.platforms) -contains "windows") {
        if ($gate.PSObject.Properties.Name -notcontains "impactPaths" -or @($gate.impactPaths).Count -eq 0) {
            $errors.Add("Windows commit gate '$($gate.id)' must declare impactPaths for cross-project source-contract coverage.")
        }
        else {
            foreach ($impactPath in @($gate.impactPaths)) {
                if ($impactPath -isnot [string] -or [string]::IsNullOrWhiteSpace($impactPath) -or $impactPath.Contains('\')) {
                    $errors.Add("Gate '$($gate.id)' contains invalid impact path '$impactPath'; use a non-empty repository path with forward slashes.")
                }
            }
        }
    }
    $platformProjects = [System.Collections.Generic.List[object]]::new()
    if ($gate.PSObject.Properties.Name -contains "platformProjects") {
        foreach ($platformProperty in @($gate.platformProjects.PSObject.Properties)) {
            if ($allowedPlatforms -notcontains $platformProperty.Name -or @($gate.platforms) -notcontains $platformProperty.Name) {
                $errors.Add("Gate '$($gate.id)' has platformProjects for unsupported or untargeted platform '$($platformProperty.Name)'.")
            }
            foreach ($projectPath in @($platformProperty.Value)) {
                $platformProjects.Add($projectPath)
                if ($projectPath -is [string] -and -not [string]::IsNullOrWhiteSpace($projectPath)) {
                    $projectPlatform = "$($platformProperty.Name)|$($projectPath.Replace('\', '/'))"
                    if (-not $coveredProjectPlatforms.Add($projectPlatform)) {
                        $errors.Add("Test project '$projectPath' is assigned to more than one '$($platformProperty.Name)' gate.")
                    }
                }
            }
        }
    }
    if (@($gate.projects).Count + $platformProjects.Count -eq 0) {
        $errors.Add("Gate '$($gate.id)' has no test projects.")
        continue
    }

    $partitionCount = if ($gate.PSObject.Properties.Name -contains "partitions") { [int]$gate.partitions } else { 1 }
    $partitionProjects = @(if ($gate.PSObject.Properties.Name -contains "partitionProjects") { @($gate.partitionProjects) })
    if ($partitionCount -lt 1 -or $partitionCount -gt 64) {
        $errors.Add("Gate '$($gate.id)' must declare between 1 and 64 partitions.")
    }

    # "costHintMinutes" orders the generated CI matrix longest-job-first so that only short jobs
    # absorb the queueing delay once the runner pool saturates. A commit gate that omits it would
    # silently sort last, so require it rather than defaulting it.
    if ([string]$gate.gate -eq "commit") {
        if ($gate.PSObject.Properties.Name -notcontains "costHintMinutes") {
            $errors.Add("Gate '$($gate.id)' is a commit gate and must declare costHintMinutes (approximate minutes for one job of the gate as partitioned).")
        }
        else {
            $costHint = $gate.costHintMinutes
            $isPositiveNumber = ($costHint -is [int] -or $costHint -is [long] -or $costHint -is [double]) -and
                [double]$costHint -gt 0 -and [double]$costHint -le 120
            if (-not $isPositiveNumber) {
                $errors.Add("Gate '$($gate.id)' has an invalid costHintMinutes; it must be a number greater than 0 and at most 120.")
            }
        }
    }

    # "platformPartitions" is an optional sibling of "partitions" that overrides the partition
    # count for individual platforms (e.g. running fewer jobs on scarce macOS capacity). It must
    # only reference platforms the gate actually targets, and every value must be a positive
    # integer within the same bound as "partitions".
    $platformPartitionCounts = @{}
    if ($gate.PSObject.Properties.Name -contains "platformPartitions") {
        foreach ($platformPartitionProperty in @($gate.platformPartitions.PSObject.Properties)) {
            $platform = [string]$platformPartitionProperty.Name
            if ($allowedPlatforms -notcontains $platform -or @($gate.platforms) -notcontains $platform) {
                $errors.Add("Gate '$($gate.id)' has platformPartitions for unsupported or untargeted platform '$platform'.")
                continue
            }
            $value = $platformPartitionProperty.Value
            $isPositiveInteger = ($value -is [int] -or $value -is [long] -or $value -is [double]) -and
                [double]$value -eq [Math]::Floor([double]$value) -and [double]$value -ge 1
            if (-not $isPositiveInteger -or [int]$value -gt 64) {
                $errors.Add("Gate '$($gate.id)' has an invalid platformPartitions value for '$platform'; it must be a positive integer between 1 and 64.")
                continue
            }
            $platformPartitionCounts[$platform] = [int]$value
        }
    }

    # A gate with multiple partitions and no partitionProjects uses whole-project partitioning
    # (tools/Invoke-TestGate.ps1 assigns each of the gate's test projects to exactly one
    # partition via weighted bin packing) rather than class-level filtering of one named
    # project. That strategy needs at least one project per partition on every targeted
    # platform, or a partition would run nothing.
    if ($partitionProjects.Count -eq 0) {
        foreach ($platform in @($gate.platforms)) {
            $effectivePartitionCount = if ($platformPartitionCounts.ContainsKey($platform)) {
                $platformPartitionCounts[$platform]
            }
            else {
                $partitionCount
            }
            if ($effectivePartitionCount -le 1) {
                continue
            }
            $platformSpecificProjects = @(if (
                $gate.PSObject.Properties.Name -contains "platformProjects" -and
                $gate.platformProjects.PSObject.Properties.Name -contains $platform) {
                @($gate.platformProjects.$platform)
            })
            $totalForPlatform = @($gate.projects).Count + $platformSpecificProjects.Count
            if ($totalForPlatform -lt $effectivePartitionCount) {
                $errors.Add("Gate '$($gate.id)' declares $effectivePartitionCount partitions for platform '$platform' but has only $totalForPlatform project(s); name partitionProjects to split within a project instead, or reduce the partition count.")
            }
        }
    }
    if ($partitionCount -eq 1 -and $platformPartitionCounts.Count -eq 0 -and $partitionProjects.Count -gt 0) {
        $errors.Add("Gate '$($gate.id)' names partitionProjects without declaring multiple partitions.")
    }
    if ($gate.PSObject.Properties.Name -contains "preflightModes") {
        if ($gate.gate -ne "commit") {
            $errors.Add("Gate '$($gate.id)' assigns preflightModes outside the commit tier.")
        }
        if ($partitionCount -ne 1) {
            $errors.Add("Gate '$($gate.id)' cannot assign preflightModes to a partitioned gate.")
        }
        foreach ($platformProperty in @($gate.preflightModes.PSObject.Properties)) {
            $platform = [string]$platformProperty.Name
            if ($allowedPlatforms -notcontains $platform -or @($gate.platforms) -notcontains $platform) {
                $errors.Add("Gate '$($gate.id)' assigns preflightModes for unsupported or untargeted platform '$platform'.")
                continue
            }
            $seenModes = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
            foreach ($mode in @($platformProperty.Value)) {
                if ($mode -notin @("static", "platform")) {
                    $errors.Add("Gate '$($gate.id)' assigns unsupported preflight mode '$mode' on '$platform'.")
                    continue
                }
                if (-not $seenModes.Add([string]$mode)) {
                    $errors.Add("Gate '$($gate.id)' assigns preflight mode '$mode' more than once on '$platform'.")
                    continue
                }
                $owner = "$($gate.id)/$platform"
                if ($mode -eq "static") {
                    $staticPreflightOwners.Add($owner)
                }
                else {
                    $platformPreflightOwners[$platform].Add($owner)
                }
            }
        }
    }

    $buildProjectsInGate = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    $buildProjects = if ($gate.PSObject.Properties.Name -contains "buildProjects") {
        @($gate.buildProjects)
    }
    else {
        @()
    }
    foreach ($projectPath in $buildProjects) {
        if ($projectPath -isnot [string] -or [string]::IsNullOrWhiteSpace($projectPath)) {
            $errors.Add("Gate '$($gate.id)' contains an invalid build prerequisite path.")
            continue
        }
        if (-not $buildProjectsInGate.Add($projectPath)) {
            $errors.Add("Gate '$($gate.id)' references build prerequisite '$projectPath' more than once.")
        }
        if (-not (Test-Path -LiteralPath (Join-Path $repoRoot $projectPath) -PathType Leaf)) {
            $errors.Add("Gate '$($gate.id)' references missing build prerequisite '$projectPath'.")
        }
    }

    $projectsInGate = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    $allGateProjects = @($gate.projects) + @($platformProjects)
    foreach ($projectPath in @($gate.projects)) {
        if ($projectPath -isnot [string] -or [string]::IsNullOrWhiteSpace($projectPath)) {
            continue
        }
        foreach ($platform in @($gate.platforms)) {
            $projectPlatform = "$platform|$($projectPath.Replace('\', '/'))"
            if (-not $coveredProjectPlatforms.Add($projectPlatform)) {
                $errors.Add("Test project '$projectPath' is assigned to more than one '$platform' gate.")
            }
        }
    }
    foreach ($projectPath in $allGateProjects) {
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
    foreach ($projectPath in $partitionProjects) {
        if ($allGateProjects -notcontains $projectPath) {
            $errors.Add("Gate '$($gate.id)' partitions unassigned project '$projectPath'.")
        }
    }
}

if ($staticPreflightOwners.Count -ne 1) {
    $errors.Add("Commit gates must assign exactly one static preflight owner; found $($staticPreflightOwners.Count): $($staticPreflightOwners -join ', ').")
}
foreach ($platform in $allowedPlatforms) {
    $owners = $platformPreflightOwners[$platform]
    if ($owners.Count -ne 1) {
        $errors.Add("Commit gates must assign exactly one '$platform' platform preflight owner; found $($owners.Count): $($owners -join ', ').")
    }
}

$gateById = @{}
foreach ($gate in @($manifest.gates)) {
    $gateById[[string]$gate.id] = $gate
}
foreach ($required in @(
    @{ Gate = "freew-desktop"; Project = "freew/FreeW.App.Host/FreeW.App.Host.csproj" },
    @{ Gate = "freew-desktop"; Project = "freew/FreeW.App.Avalonia/FreeW.App.Avalonia.csproj" },
    @{ Gate = "freep-wpf-desktop"; Project = "freep/FreeP.App.Host/FreeP.App.Host.csproj" },
    @{ Gate = "freep-avalonia-desktop"; Project = "freep/FreeP.App.Avalonia/FreeP.App.Avalonia.csproj" }
)) {
    if (-not $gateById.ContainsKey($required.Gate) -or
        @($gateById[$required.Gate].buildProjects) -notcontains $required.Project) {
        $errors.Add("Gate '$($required.Gate)' must build shipping prerequisite '$($required.Project)'.")
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

Assert-WorkflowContains -Path ".github/workflows/ci.yml" -Expected 'tools/Get-TestGateMatrix.ps1 -Gate commit'
Assert-WorkflowContains -Path ".github/workflows/ci.yml" -Expected 'fromJSON(needs.prepare.outputs.matrix)'
Assert-WorkflowContains -Path ".github/workflows/ci.yml" -Expected '-Gate "${{ matrix.gate }}"'
Assert-WorkflowContains -Path ".github/workflows/ci.yml" -Expected '-App "${{ matrix.app }}"'
Assert-WorkflowContains -Path ".github/workflows/ci.yml" -Expected '-Platform "${{ matrix.platform }}"'
Assert-WorkflowContains -Path ".github/workflows/ci.yml" -Expected '-GateId "${{ matrix.gateId }}"'
Assert-WorkflowContains -Path ".github/workflows/ci.yml" -Expected '-PartitionIndex ${{ matrix.partitionIndex }}'
Assert-WorkflowContains -Path ".github/workflows/ci.yml" -Expected '-PartitionCount ${{ matrix.partitionCount }}'
Assert-WorkflowContains -Path ".github/workflows/ci.yml" -Expected 'if: matrix.runStaticPreflight'
Assert-WorkflowContains -Path ".github/workflows/ci.yml" -Expected 'if: matrix.runPlatformPreflight'
Assert-WorkflowContains -Path ".github/workflows/freew-ci.yml" -Expected '-Gate commit -App FreeW -Platform ${{ matrix.platform }}'
Assert-WorkflowContains -Path ".github/workflows/freep-ci.yml" -Expected '-Gate commit -App FreeP -Platform ${{ matrix.platform }}'
Assert-WorkflowContains -Path ".github/workflows/full-release.yml" -Expected 'name: Full Signed Release'
Assert-WorkflowContains -Path ".github/workflows/full-release.yml" -Expected 'tools/Test-GitHubReleaseCandidate.ps1'
Assert-WorkflowContains -Path ".github/workflows/full-release.yml" -Expected 'tools/Get-TestGateMatrix.ps1 -Gate release'
Assert-WorkflowContains -Path ".github/workflows/full-release.yml" -Expected 'fromJSON(needs.prepare.outputs.release_matrix)'
Assert-WorkflowContains -Path ".github/workflows/full-release.yml" -Expected 'name: FreeX full release gate'
Assert-WorkflowContains -Path ".github/workflows/full-release.yml" -Expected 'name: Validate complete release inventory'
Assert-WorkflowContains -Path ".github/workflows/full-release.yml" -Expected 'Authenticate to Azure Artifact Signing'
Assert-WorkflowContains -Path ".github/workflows/full-release.yml" -Expected 'Publish-WindowsVelopackPackage.ps1'
Assert-WorkflowContains -Path ".github/workflows/full-release.yml" -Expected 'New-FreeSuiteWindowsBootstrapper.ps1'
Assert-WorkflowContains -Path ".github/workflows/full-release.yml" -Expected 'New-SignedMacOsReleasePackages.ps1'

$integrationGateContent = Get-Content -LiteralPath (Join-Path $repoRoot "tools/Test-BranchForIntegration.ps1") -Raw
if ($integrationGateContent -match '(?m)^\s+-NoBuild\s+`?\s*$') {
    $errors.Add("The branch integration gate must let selected test projects build; FreeW and FreeP test projects are not all outputs of FreeX.slnx.")
}

function Get-SelectedLocalGateIds {
    param([string[]]$ChangedPaths)

    $selector = Join-Path $PSScriptRoot "Get-ImpactedTestGates.ps1"
    $json = & $selector -ChangedPaths $ChangedPaths -OutputFormat Json
    return @(($json | ConvertFrom-Json).gateIds)
}

$hostSourceGates = @(Get-SelectedLocalGateIds -ChangedPaths @("src/FreeX.App.Host/MainWindow.Viewport.cs"))
if ($hostSourceGates -notcontains "freex-contract") {
    $errors.Add("FreeX host source changes must select the freex-contract gate, including source guards that do not use ProjectReference.")
}
$freePHostGates = @(Get-SelectedLocalGateIds -ChangedPaths @("freep/FreeP.App.Host/PresentationAnimationGallery.cs"))
if ($freePHostGates -notcontains "freep-wpf-desktop") {
    $errors.Add("FreeP WPF host changes must select the freep-wpf-desktop gate.")
}
$coreGates = @(Get-SelectedLocalGateIds -ChangedPaths @("src/FreeX.Core.Commands/CommandDispatcher.cs"))
foreach ($requiredGate in @("freex-core-portable", "freex-contract", "freex-avalonia")) {
    if ($coreGates -notcontains $requiredGate) {
        $errors.Add("FreeX core changes must transitively select '$requiredGate'.")
    }
}
$documentationGates = @(Get-SelectedLocalGateIds -ChangedPaths @("docs/testing/example.md"))
if ($documentationGates.Count -ne 0) {
    $errors.Add("Documentation-only changes must not select local commit-test gates.")
}
$globalGates = @(Get-SelectedLocalGateIds -ChangedPaths @("Directory.Build.props"))
$expectedWindowsCommitGates = @($manifest.gates | Where-Object {
    $_.gate -eq "commit" -and @($_.platforms) -contains "windows"
} | ForEach-Object { [string]$_.id })
$actualGlobalGateSet = @($globalGates | Sort-Object) -join '|'
$expectedGlobalGateSet = @($expectedWindowsCommitGates | Sort-Object) -join '|'
if ($actualGlobalGateSet -ne $expectedGlobalGateSet) {
    $errors.Add("Global build-contract changes must select every Windows commit gate.")
}

$gateDocumentation = Get-Content -LiteralPath (Join-Path $repoRoot "docs/testing/test-gates.md") -Raw
foreach ($requiredHeading in @("Commit gate", "Release gate", "all platforms", "Invoke-TestGate.ps1")) {
    if ($gateDocumentation.IndexOf($requiredHeading, [System.StringComparison]::OrdinalIgnoreCase) -lt 0) {
        $errors.Add("docs/testing/test-gates.md must document '$requiredHeading'.")
    }
}

if ($errors.Count -gt 0) {
    throw ("Test-gate contract failed:`n - " + ($errors -join "`n - "))
}

Write-Host "Test-gate contract passed: $($manifest.gates.Count) gates, $($coveredProjects.Count) assigned projects, and affected-gate selection probes."
