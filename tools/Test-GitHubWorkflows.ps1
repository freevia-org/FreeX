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
        $requiredMacOsEvidenceMarkers = @(
            "- name: Capture runner toolchain evidence",
            'evidence_path="$artifact_root/freex-$runtime-macos-evidence.txt"',
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
