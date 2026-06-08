param(
    [string]$WorkflowDirectory = ""
)

$ErrorActionPreference = "Stop"

$scriptDirectory = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $scriptDirectory

if ([string]::IsNullOrWhiteSpace($WorkflowDirectory)) {
    $WorkflowDirectory = Join-Path $repoRoot ".github\workflows"
}

$resolvedWorkflowDirectory = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($WorkflowDirectory)
if (-not (Test-Path -LiteralPath $resolvedWorkflowDirectory -PathType Container)) {
    throw "GitHub workflow directory does not exist: $resolvedWorkflowDirectory"
}

$workflows = @(
    Get-ChildItem -LiteralPath $resolvedWorkflowDirectory -File |
        Where-Object { $_.Extension -in @(".yml", ".yaml") } |
        Sort-Object Name
)

if ($workflows.Count -eq 0) {
    throw "No GitHub workflow files were found in $resolvedWorkflowDirectory."
}

$allowedActionMajors = @{
    "actions/checkout" = "v6"
    "actions/download-artifact" = "v7"
    "actions/setup-dotnet" = "v5"
    "actions/upload-artifact" = "v7"
}

function Get-IndentedYamlBlock {
    param(
        [Parameter(Mandatory = $true)][AllowEmptyString()][string[]]$Lines,
        [Parameter(Mandatory = $true)][string]$Pattern
    )

    for ($lineIndex = 0; $lineIndex -lt $Lines.Count; $lineIndex++) {
        $match = [regex]::Match($Lines[$lineIndex], $Pattern)
        if (-not $match.Success -or -not $match.Groups["indent"].Success) {
            continue
        }

        $indentLength = $match.Groups["indent"].Value.Length
        $blockLines = [System.Collections.Generic.List[string]]::new()
        $blockLines.Add($Lines[$lineIndex])
        for ($nextLineIndex = $lineIndex + 1; $nextLineIndex -lt $Lines.Count; $nextLineIndex++) {
            if ([string]::IsNullOrWhiteSpace($Lines[$nextLineIndex])) {
                $blockLines.Add($Lines[$nextLineIndex])
                continue
            }

            $indentMatch = [regex]::Match($Lines[$nextLineIndex], "^(\s*)\S")
            if ($indentMatch.Success -and $indentMatch.Groups[1].Value.Length -le $indentLength) {
                break
            }

            $blockLines.Add($Lines[$nextLineIndex])
        }

        return $blockLines -join "`n"
    }

    return $null
}

function Get-WorkflowStepBlock {
    param(
        [Parameter(Mandatory = $true)][AllowEmptyString()][string]$Workflow,
        [Parameter(Mandatory = $true)][string]$StepName
    )

    return Get-IndentedYamlBlock `
        -Lines ($Workflow -split "\r?\n") `
        -Pattern "^(?<indent>\s*)-\s+name:\s+$([regex]::Escape($StepName))\s*(?:#.*)?$"
}

function Get-DotNetTestCommandBlocks {
    param(
        [Parameter(Mandatory = $true)][AllowEmptyString()][string[]]$Lines
    )

    $commands = [System.Collections.Generic.List[string]]::new()
    for ($lineIndex = 0; $lineIndex -lt $Lines.Count; $lineIndex++) {
        if ($Lines[$lineIndex] -notmatch "\bdotnet\s+test\b") {
            continue
        }

        $commandLines = [System.Collections.Generic.List[string]]::new()
        $commandLines.Add($Lines[$lineIndex].Trim())
        $commandEndIndex = $lineIndex
        while ($commandLines[$commandLines.Count - 1].TrimEnd().EndsWith('\') -and
            $commandEndIndex + 1 -lt $Lines.Count) {
            $commandEndIndex++
            $commandLines.Add($Lines[$commandEndIndex].Trim())
        }

        $commands.Add($commandLines -join "`n")
        $lineIndex = $commandEndIndex
    }

    return @($commands)
}

function Get-FullyQualifiedNameFilterEntries {
    param(
        [Parameter(Mandatory = $true)][string]$CommandBlock
    )

    return @(
        [regex]::Matches($CommandBlock, "FullyQualifiedName~(?<name>[A-Za-z0-9_.]+)") |
            ForEach-Object { $_.Groups["name"].Value }
    )
}

function Test-CSharpTestClassExists {
    param(
        [Parameter(Mandatory = $true)][string]$RepoRoot,
        [Parameter(Mandatory = $true)][string]$FullyQualifiedName
    )

    $lastDotIndex = $FullyQualifiedName.LastIndexOf(".", [System.StringComparison]::Ordinal)
    if ($lastDotIndex -le 0 -or $lastDotIndex -ge $FullyQualifiedName.Length - 1) {
        return $false
    }

    $namespace = $FullyQualifiedName.Substring(0, $lastDotIndex)
    $className = $FullyQualifiedName.Substring($lastDotIndex + 1)
    $testsRoot = Join-Path $RepoRoot "tests"
    if (-not (Test-Path -LiteralPath $testsRoot -PathType Container)) {
        return $false
    }

    $namespacePattern = "\bnamespace\s+$([regex]::Escape($namespace))\s*[;{]"
    $classPattern = "(?m)^\s*(?:public|internal)?\s*(?:sealed\s+|partial\s+|abstract\s+)*class\s+$([regex]::Escape($className))\b"
    foreach ($classFile in @(Get-ChildItem -LiteralPath $testsRoot -Recurse -File -Filter "$className.cs" -ErrorAction SilentlyContinue)) {
        $source = Get-Content -LiteralPath $classFile.FullName -Raw
        if ($source -match $namespacePattern -and $source -match $classPattern) {
            return $true
        }
    }

    return $false
}

$errors = [System.Collections.Generic.List[string]]::new()
foreach ($workflow in $workflows) {
    $content = Get-Content -LiteralPath $workflow.FullName -Raw
    $lines = $content -split "\r?\n"
    if ($content -match "`t") {
        $errors.Add("$($workflow.Name): workflow YAML must use spaces for indentation, not tabs.")
    }

    if ($content -match "(?m)^\s*pull_request_target\s*:") {
        $errors.Add("$($workflow.Name): workflow must not use the privileged pull_request_target event.")
    }

    foreach ($match in [regex]::Matches($content, "(?ms)^\s*runs-on\s*:\s*(?<runner>[^\r\n]*(?:\r?\n\s+-\s+[^\r\n]+)*)")) {
        $runnerBlock = (($match.Value -split "\r?\n") | ForEach-Object { $_ -replace "#.*$", "" }) -join "`n"
        if ($runnerBlock -match "(?i)(^|[\[\s,'`"-])self-hosted($|[\]\s,'`"])") {
            $errors.Add("$($workflow.Name): workflow must not use self-hosted runners.")
        }
    }

    for ($lineIndex = 0; $lineIndex -lt $lines.Count; $lineIndex++) {
        $runsOnMatch = [regex]::Match($lines[$lineIndex], "^(\s*)runs-on\s*:\s*(?<runner>[^\r\n#]*)")
        if (-not $runsOnMatch.Success) {
            continue
        }

        $propertyIndent = $runsOnMatch.Groups[1].Value
        $runner = $runsOnMatch.Groups["runner"].Value.Trim("`"", "'")
        if ([string]::IsNullOrWhiteSpace($runner)) {
            $runner = "runs-on"
        }

        $escapedPropertyIndent = [regex]::Escape($propertyIndent)
        $jobStartIndex = $lineIndex
        for ($previousLineIndex = $lineIndex - 1; $previousLineIndex -ge 0; $previousLineIndex--) {
            $indentMatch = [regex]::Match($lines[$previousLineIndex], "^(\s*)\S")
            if ($indentMatch.Success -and
                $indentMatch.Groups[1].Value.Length -lt $propertyIndent.Length) {
                $jobStartIndex = $previousLineIndex
                break
            }
        }

        $jobLines = [System.Collections.Generic.List[string]]::new()
        for ($nextLineIndex = $jobStartIndex; $nextLineIndex -lt $lines.Count; $nextLineIndex++) {
            $indentMatch = [regex]::Match($lines[$nextLineIndex], "^(\s*)\S")
            if ($nextLineIndex -gt $jobStartIndex -and
                $indentMatch.Success -and
                $indentMatch.Groups[1].Value.Length -lt $propertyIndent.Length) {
                break
            }

            $jobLines.Add($lines[$nextLineIndex])
        }

        $jobBlock = $jobLines -join "`n"
        if ($jobBlock -notmatch "(?m)^$escapedPropertyIndent\s*timeout-minutes:\s*\d+\s*(?:#.*)?$") {
            $errors.Add("$($workflow.Name): job running on '$runner' must declare timeout-minutes.")
        }
    }

    $permissionsMatch = [regex]::Match($content, "(?m)^permissions:\s*(?<value>[^\r\n#]*)")
    if (-not $permissionsMatch.Success) {
        $errors.Add("$($workflow.Name): workflow must declare top-level permissions explicitly.")
    } else {
        $permissionsValue = $permissionsMatch.Groups["value"].Value.Trim().Trim("`"", "'")
        if ($permissionsValue -eq "write-all") {
            $errors.Add("$($workflow.Name): workflow must not request write-all permissions.")
        }
    }

    foreach ($match in [regex]::Matches($content, "(?ms)^(\s*)-\s+name:\s+(?<name>[^\r\n]+).*?^\1\s+run:\s+")) {
        $stepBlock = $match.Value
        if ($stepBlock -notmatch "(?m)^\s+shell:\s+") {
            $stepName = $match.Groups["name"].Value.Trim("`"", "'")
            $errors.Add("$($workflow.Name): run step '$stepName' must declare an explicit shell.")
        }
    }

    for ($lineIndex = 0; $lineIndex -lt $lines.Count; $lineIndex++) {
        if ($lines[$lineIndex] -notmatch "^(\s*)-\s+(?:name|uses):") {
            continue
        }

        $stepIndent = $Matches[1]
        $escapedStepIndent = [regex]::Escape($stepIndent)
        $stepLines = [System.Collections.Generic.List[string]]::new()
        $stepLines.Add($lines[$lineIndex])
        for ($nextLineIndex = $lineIndex + 1; $nextLineIndex -lt $lines.Count; $nextLineIndex++) {
            if ($lines[$nextLineIndex] -match "^$escapedStepIndent-\s+") {
                break
            }

            $stepLines.Add($lines[$nextLineIndex])
        }

        $stepBlock = $stepLines -join "`n"
        if ($stepBlock -match "(?m)^\s*uses:\s+actions/checkout@v\d+\s*(?:#.*)?$" -and
            $stepBlock -notmatch "(?m)^\s*persist-credentials:\s*false\s*(?:#.*)?$") {
            $errors.Add("$($workflow.Name): actions/checkout steps must set persist-credentials: false.")
        }

        if ($stepBlock -match "(?m)^\s*uses:\s+actions/upload-artifact@v\d+\s*(?:#.*)?$" -and
            $stepBlock -notmatch "(?m)^\s*if-no-files-found:\s*(?:error|warn)\s*(?:#.*)?$") {
            $errors.Add("$($workflow.Name): actions/upload-artifact steps must set if-no-files-found to error or warn.")
        }
    }

    if ($workflow.Name -eq "macos-app.yml") {
        $requiredMacOsFocusedTestFilters = @(
            [pscustomobject]@{
                Project = "tests/FreeX.App.Services.Tests/FreeX.App.Services.Tests.csproj"
                Entries = @(
                    "FreeX.App.Services.Tests.PortablePdfDocumentExporterTests",
                    "FreeX.App.Services.Tests.PortablePdfExportPlannerTests",
                    "FreeX.App.Services.Tests.PortablePdfPageContentPlannerTests",
                    "FreeX.App.Services.Tests.WorkbookExportPrintPlannerTests",
                    "FreeX.App.Services.Tests.WorkbookShareActionPlannerTests",
                    "FreeX.App.Services.Tests.WorkbookViewportScrollPlannerTests",
                    "FreeX.App.Services.Tests.AppServicesPortabilityGuardTests",
                    "FreeX.App.Services.Tests.AvaloniaProjectPortabilityGuardTests",
                    "FreeX.App.Services.Tests.ApplicationDataPathGuardTests",
                    "FreeX.App.Services.Tests.AvaloniaShellSourceTests",
                    "FreeX.App.Services.Tests.MacOsLaunchSmokeReportKeyDriftGuardTests"
                )
            },
            [pscustomobject]@{
                Project = "tests/FreeX.Core.Model.Tests/FreeX.Core.Model.Tests.csproj"
                Entries = @(
                    "FreeX.Core.Model.Tests.ExportPathPlannerTests"
                )
            }
        )

        $requiredMacOsEvidenceMarkers = @(
            "- name: Capture runner toolchain evidence",
            'evidence_path="$artifact_root/freex-$runtime-macos-evidence.txt"',
            'echo "github_run_id=${GITHUB_RUN_ID}"',
            'echo "github_run_attempt=${GITHUB_RUN_ATTEMPT}"',
            'echo "runner_label=${{ matrix.runner }}"',
            'echo "runner_os=${RUNNER_OS:-unknown}"',
            'echo "runner_arch=${RUNNER_ARCH:-unknown}"',
            'echo "image_os=${ImageOS:-unknown}"',
            'echo "image_version=${ImageVersion:-unknown}"',
            'echo "[sw_vers]"',
            "sw_vers",
            'echo "[uname -m]"',
            "uname -m",
            'echo "[dotnet --info]"',
            "dotnet --info",
            'echo "[xcodebuild -version]"',
            "xcodebuild -version",
            '} | tee "$evidence_path"',
            'echo "[bundle]"',
            '} >> "$evidence_path"'
        )

        foreach ($marker in $requiredMacOsEvidenceMarkers) {
            if (-not $content.Contains($marker)) {
                $errors.Add("$($workflow.Name): macOS app workflow is missing hosted runner/toolchain evidence marker: $marker")
            }
        }

        $workflowDispatchBlock = Get-IndentedYamlBlock `
            -Lines $lines `
            -Pattern "^(?<indent>\s*)workflow_dispatch\s*:\s*(?:#.*)?$"
        $distributionCandidateInputBlock = $null
        if (-not [string]::IsNullOrWhiteSpace($workflowDispatchBlock)) {
            $distributionCandidateInputBlock = Get-IndentedYamlBlock `
                -Lines ($workflowDispatchBlock -split "\r?\n") `
                -Pattern "^(?<indent>\s*)distribution_candidate\s*:\s*(?:#.*)?$"
        }

        if ([string]::IsNullOrWhiteSpace($distributionCandidateInputBlock) -or
            $distributionCandidateInputBlock -notmatch "(?m)^\s*type:\s*boolean\s*(?:#.*)?$" -or
            $distributionCandidateInputBlock -notmatch "(?m)^\s*default:\s*false\s*(?:#.*)?$") {
            $errors.Add("$($workflow.Name): macOS app workflow must declare a workflow_dispatch distribution_candidate boolean input defaulting to false.")
        }

        $releasePublicationJobBlock = Get-IndentedYamlBlock `
            -Lines $lines `
            -Pattern "^(?<indent>\s*)publish-distribution-candidate\s*:\s*(?:#.*)?$"
        if ([string]::IsNullOrWhiteSpace($releasePublicationJobBlock)) {
            $errors.Add("$($workflow.Name): macOS release publication job 'publish-distribution-candidate' is missing.")
        } else {
            if ($releasePublicationJobBlock -notmatch '(?m)^\s*if:\s*\$\{\{\s*github\.event_name\s*==\s*''workflow_dispatch''\s*&&\s*inputs\.distribution_candidate\s*==\s*true\s*\}\}\s*(?:#.*)?$') {
                $errors.Add("$($workflow.Name): macOS release publication job must be gated to workflow_dispatch distribution-candidate runs.")
            }

            if ($releasePublicationJobBlock -notmatch "(?m)^\s*permissions:\s*(?:#.*)?$") {
                $errors.Add("$($workflow.Name): macOS release publication job must declare job-level permissions.")
            }

            if ($releasePublicationJobBlock -match "(?m)^\s*permissions:\s*write-all\s*(?:#.*)?$") {
                $errors.Add("$($workflow.Name): macOS release publication job must not request write-all permissions.")
            }

            if ($releasePublicationJobBlock -notmatch "(?m)^\s*actions:\s*read\s*(?:#.*)?$") {
                $errors.Add("$($workflow.Name): macOS release publication job must declare actions: read.")
            }

            if ($releasePublicationJobBlock -notmatch "(?m)^\s*contents:\s*write\s*(?:#.*)?$") {
                $errors.Add("$($workflow.Name): macOS release publication job must declare contents: write.")
            }

            $contentsWriteMatches = [regex]::Matches($content, "(?m)^\s*contents:\s*write\s*(?:#.*)?$")
            if ($contentsWriteMatches.Count -ne 1 -or
                $releasePublicationJobBlock -notmatch "(?m)^\s*contents:\s*write\s*(?:#.*)?$") {
                $errors.Add("$($workflow.Name): macOS release publication must be the only workflow scope requesting contents: write.")
            }

            if ($releasePublicationJobBlock -notmatch "(?ms)^\s*concurrency:\s*(?:#.*)?\r?\n(?:^\s+.*\r?\n)*?^\s*group:\s*macos-distribution-candidate-release\s*(?:#.*)?$" -or
                $releasePublicationJobBlock -notmatch "(?m)^\s*cancel-in-progress:\s*false\s*(?:#.*)?$") {
                $errors.Add("$($workflow.Name): macOS release publication job must use non-canceling concurrency with cancel-in-progress: false.")
            }

            if ($releasePublicationJobBlock -notmatch "(?ms)^      - name:\s+Checkout\s*(?:#.*)?\r?\n        uses:\s+actions/checkout@v6\s*(?:#.*)?\r?\n        with:\s*(?:#.*)?\r?\n(?:          [^\r\n]*\r?\n)*?          persist-credentials:\s*false\s*(?:#.*)?(?:\r?\n|$)") {
                $errors.Add("$($workflow.Name): macOS release publication checkout must use actions/checkout@v6 with persist-credentials: false.")
            }

            $downloadMacOsAppArtifactsBlock = Get-WorkflowStepBlock -Workflow $releasePublicationJobBlock -StepName "Download macOS app artifacts"
            if ([string]::IsNullOrWhiteSpace($downloadMacOsAppArtifactsBlock)) {
                $errors.Add("$($workflow.Name): macOS release publication job must download macOS app artifacts.")
            } elseif (-not $downloadMacOsAppArtifactsBlock.Contains('pattern: freex-${{ github.run_id }}-${{ github.run_attempt }}-*-macos-app')) {
                $errors.Add("$($workflow.Name): macOS release publication must download app artifacts using the current run id and run attempt.")
            }

            if (-not $releasePublicationJobBlock.Contains('source_artifact_pattern = "freex-$($env:GITHUB_RUN_ID)-$($env:GITHUB_RUN_ATTEMPT)-*-macos-app"')) {
                $errors.Add("$($workflow.Name): macOS release publication manifest must record the current run id/run attempt source artifact pattern.")
            }

            if (-not $releasePublicationJobBlock.Contains('"github_run_id=$($env:GITHUB_RUN_ID)"') -or
                -not $releasePublicationJobBlock.Contains('"github_run_attempt=$($env:GITHUB_RUN_ATTEMPT)"')) {
                $errors.Add("$($workflow.Name): macOS release publication must validate downloaded evidence run identity against the current run.")
            }
        }

        $macOsAppJobBlock = Get-IndentedYamlBlock `
            -Lines $lines `
            -Pattern "^(?<indent>\s*)macos-app\s*:\s*(?:#.*)?$"
        if ([string]::IsNullOrWhiteSpace($macOsAppJobBlock)) {
            $errors.Add("$($workflow.Name): macOS app job 'macos-app' is missing.")
        } else {
            $macOsAppTestCommands = @(Get-DotNetTestCommandBlocks -Lines ($macOsAppJobBlock -split "\r?\n"))
            if ($macOsAppTestCommands.Count -eq 0) {
                $errors.Add("$($workflow.Name): macOS app job must run focused hosted dotnet test filters before packaging.")
            }

            $focusedTestStepMarker = "- name: Test portable PDF macOS route"
            $focusedTestStepIndex = $macOsAppJobBlock.IndexOf($focusedTestStepMarker, [System.StringComparison]::Ordinal)
            if ($focusedTestStepIndex -lt 0) {
                $errors.Add("$($workflow.Name): macOS app job must run focused hosted dotnet test filters before packaging.")
            } else {
                foreach ($laterStepName in @("Build app project", "Publish app bundle", "Upload app artifact", "Upload app diagnostics")) {
                    $laterStepIndex = $macOsAppJobBlock.IndexOf("- name: $laterStepName", [System.StringComparison]::Ordinal)
                    if ($laterStepIndex -ge 0 -and $focusedTestStepIndex -gt $laterStepIndex) {
                        $errors.Add("$($workflow.Name): macOS app workflow must run focused hosted tests before package/upload step '$laterStepName'.")
                    }
                }
            }

            $appArtifactUploadBlock = Get-WorkflowStepBlock -Workflow $macOsAppJobBlock -StepName "Upload app artifact"
            if ([string]::IsNullOrWhiteSpace($appArtifactUploadBlock)) {
                $errors.Add("$($workflow.Name): macOS app workflow must upload the macOS app artifact.")
            } elseif (-not $appArtifactUploadBlock.Contains('name: freex-${{ github.run_id }}-${{ github.run_attempt }}-${{ matrix.runtime }}-macos-app')) {
                $errors.Add("$($workflow.Name): macOS app artifact upload name must include github.run_id, github.run_attempt, matrix runtime, and macos-app suffix.")
            }

            $appDiagnosticsUploadBlock = Get-WorkflowStepBlock -Workflow $macOsAppJobBlock -StepName "Upload app diagnostics"
            if ([string]::IsNullOrWhiteSpace($appDiagnosticsUploadBlock)) {
                $errors.Add("$($workflow.Name): macOS app workflow must upload macOS diagnostics.")
            } elseif (-not $appDiagnosticsUploadBlock.Contains('name: freex-${{ github.run_id }}-${{ github.run_attempt }}-${{ matrix.runtime }}-macos-diagnostics')) {
                $errors.Add("$($workflow.Name): macOS diagnostics artifact upload name must include github.run_id, github.run_attempt, matrix runtime, and macos-diagnostics suffix.")
            }

            foreach ($command in $macOsAppTestCommands) {
                if ($command -notmatch "(?m)--filter\s+") {
                    $errors.Add("$($workflow.Name): macOS app hosted test command must use a focused --filter: $($command.Split("`n")[0])")
                }

                foreach ($broadTarget in @("FreeX.slnx", "FreeX.DefaultTests.slnx", "FreeX.UiTests.slnx")) {
                    if ($command.IndexOf($broadTarget, [System.StringComparison]::Ordinal) -ge 0) {
                        $errors.Add("$($workflow.Name): macOS app hosted test command must not run broad test target '$broadTarget'.")
                    }
                }
            }

            foreach ($requiredFilter in $requiredMacOsFocusedTestFilters) {
                $matchingCommands = @(
                    $macOsAppTestCommands |
                        Where-Object { $_.IndexOf($requiredFilter.Project, [System.StringComparison]::Ordinal) -ge 0 }
                )
                if ($matchingCommands.Count -ne 1) {
                    $errors.Add("$($workflow.Name): macOS app workflow must run exactly one focused dotnet test command for $($requiredFilter.Project).")
                    continue
                }

                $actualEntries = @(Get-FullyQualifiedNameFilterEntries -CommandBlock $matchingCommands[0])
                $actualDistinctEntries = @($actualEntries | Sort-Object -Unique)
                foreach ($expectedEntry in $requiredFilter.Entries) {
                    if ($actualDistinctEntries -notcontains $expectedEntry) {
                        $errors.Add("$($workflow.Name): macOS app workflow focused test filter is missing '$expectedEntry'.")
                    }

                    if (-not (Test-CSharpTestClassExists -RepoRoot $repoRoot -FullyQualifiedName $expectedEntry)) {
                        $errors.Add("$($workflow.Name): macOS app workflow focused test filter references missing test class '$expectedEntry'.")
                    }
                }

                foreach ($actualEntry in $actualDistinctEntries) {
                    if ($requiredFilter.Entries -notcontains $actualEntry) {
                        $errors.Add("$($workflow.Name): macOS app workflow has unexpected focused test filter '$actualEntry' for $($requiredFilter.Project).")
                    }
                }

                foreach ($duplicateGroup in @($actualEntries | Group-Object | Where-Object { $_.Count -gt 1 })) {
                    $errors.Add("$($workflow.Name): macOS app workflow duplicates focused test filter '$($duplicateGroup.Name)'.")
                }
            }
        }

        if (-not [string]::IsNullOrWhiteSpace($releasePublicationJobBlock) -and
            $releasePublicationJobBlock -match "\bdotnet\s+test\b") {
            $errors.Add("$($workflow.Name): macOS release publication job must not run dotnet test; publish artifacts from the focused macOS app job instead.")
        }
    }

    foreach ($match in [regex]::Matches($content, "(?m)^\s*(?:-\s*)?uses:\s+([^\s#]+)")) {
        $actionRef = $match.Groups[1].Value.Trim("`"", "'")
        if ($actionRef -match "^\.[\\/]") {
            $localActionPath = $actionRef.Substring(2)
            $segments = $localActionPath -split "[\\/]+"
            if ($segments -contains "..") {
                $errors.Add("$($workflow.Name): local action reference '$actionRef' must stay within the workflow workspace.")
            }

            continue
        }

        if ($actionRef -notmatch "@v\d+$") {
            $errors.Add("$($workflow.Name): action '$actionRef' must be pinned to an explicit major version such as @v7.")
            continue
        }

        $actionName = $actionRef.Substring(0, $actionRef.LastIndexOf("@", [System.StringComparison]::Ordinal))
        $actionMajor = $actionRef.Substring($actionRef.LastIndexOf("@", [System.StringComparison]::Ordinal) + 1)
        if ($allowedActionMajors.ContainsKey($actionName) -and $allowedActionMajors[$actionName] -ne $actionMajor) {
            $errors.Add("$($workflow.Name): action '$actionRef' must use supported major $($allowedActionMajors[$actionName]).")
        }
    }
}

if ($errors.Count -gt 0) {
    throw "GitHub workflow validation failed:`n$($errors -join "`n")"
}

Write-Output "Validated $($workflows.Count) GitHub workflow file(s)."
