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

$errors = [System.Collections.Generic.List[string]]::new()
foreach ($workflow in $workflows) {
    $content = Get-Content -LiteralPath $workflow.FullName -Raw
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
        }
    }
}

if ($errors.Count -gt 0) {
    throw "GitHub workflow validation failed:`n$($errors -join "`n")"
}

Write-Output "Validated $($workflows.Count) GitHub workflow file(s)."
