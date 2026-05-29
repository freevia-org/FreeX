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

    if ($content -notmatch "(?m)^permissions:\s*$") {
        $errors.Add("$($workflow.Name): workflow must declare top-level permissions explicitly.")
    }

    foreach ($match in [regex]::Matches($content, "(?m)^\s*(?:-\s*)?uses:\s+([^\s#]+)")) {
        $actionRef = $match.Groups[1].Value.Trim("`"", "'")
        if ($actionRef -match "^\./") {
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
