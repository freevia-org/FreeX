#!/usr/bin/env pwsh
[CmdletBinding()]
param(
    [string]$Repository = "tony-xmelon/FreeX",
    [switch]$Strict,
    [string]$OutputPath
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
    throw "GitHub CLI (gh) is required."
}

& gh auth status *> $null
if ($LASTEXITCODE -ne 0) {
    throw "GitHub CLI is not authenticated. Run 'gh auth login' first."
}

function Invoke-GhJson {
    param([Parameter(Mandatory = $true)][string[]]$Arguments)

    $previousPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = "Continue"
        $raw = & gh @Arguments 2>$null
        $exitCode = $LASTEXITCODE
    } finally {
        $ErrorActionPreference = $previousPreference
    }
    if ($exitCode -ne 0) {
        return [pscustomobject]@{ Succeeded = $false; Value = $null }
    }

    $text = ($raw -join [Environment]::NewLine).Trim()
    $value = if ([string]::IsNullOrWhiteSpace($text)) { $null } else { $text | ConvertFrom-Json }
    return [pscustomobject]@{ Succeeded = $true; Value = $value }
}

function New-Check {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][bool]$Passed,
        [Parameter(Mandatory = $true)][string]$Remediation
    )

    [pscustomobject]@{
        name = $Name
        passed = $Passed
        remediation = if ($Passed) { "" } else { $Remediation }
    }
}

$checks = [System.Collections.Generic.List[object]]::new()

$branchProtection = Invoke-GhJson -Arguments @("api", "repos/$Repository/branches/main/protection")
$rulesets = Invoke-GhJson -Arguments @("api", "repos/$Repository/rulesets")
$hasActiveRuleset = $rulesets.Succeeded -and @(
    $rulesets.Value | Where-Object { $_.enforcement -eq "active" -and $_.target -eq "branch" }
).Count -gt 0
$checks.Add((New-Check `
    -Name "main branch protection or active branch ruleset" `
    -Passed ($branchProtection.Succeeded -or $hasActiveRuleset) `
    -Remediation "Protect main with pull-request review, required CI, deletion protection, and no force pushes."))

$vulnerabilityAlerts = Invoke-GhJson -Arguments @("api", "repos/$Repository/vulnerability-alerts")
$checks.Add((New-Check `
    -Name "dependency vulnerability alerts" `
    -Passed $vulnerabilityAlerts.Succeeded `
    -Remediation "Enable dependency graph and vulnerability alerts in repository security settings."))

$privateReporting = Invoke-GhJson -Arguments @("api", "repos/$Repository/private-vulnerability-reporting")
$privateReportingEnabled = $privateReporting.Succeeded -and $privateReporting.Value.enabled -eq $true
$checks.Add((New-Check `
    -Name "private vulnerability reporting" `
    -Passed $privateReportingEnabled `
    -Remediation "Enable private vulnerability reporting before linking users to SECURITY.md."))

$workflowPermissions = Invoke-GhJson -Arguments @("api", "repos/$Repository/actions/permissions/workflow")
$readOnlyDefault = $workflowPermissions.Succeeded -and $workflowPermissions.Value.default_workflow_permissions -eq "read"
$checks.Add((New-Check `
    -Name "read-only default workflow token" `
    -Passed $readOnlyDefault `
    -Remediation "Set the default GITHUB_TOKEN permission to read-only."))

$environments = Invoke-GhJson -Arguments @("api", "repos/$Repository/environments")
$hasReleaseEnvironment = $environments.Succeeded -and @(
    $environments.Value.environments | Where-Object { $_.name -eq "public-preview" }
).Count -eq 1
$checks.Add((New-Check `
    -Name "public-preview deployment environment" `
    -Passed $hasReleaseEnvironment `
    -Remediation "Create a protected public-preview environment with a required reviewer."))

$labels = Invoke-GhJson -Arguments @("label", "list", "--repo", $Repository, "--limit", "200", "--json", "name")
$labelNames = @($labels.Value | ForEach-Object { $_.name })
foreach ($requiredLabel in @("needs-triage", "user-feedback", "user-testing")) {
    $checks.Add((New-Check `
        -Name "issue label: $requiredLabel" `
        -Passed ($labels.Succeeded -and $labelNames -contains $requiredLabel) `
        -Remediation "Create the '$requiredLabel' label used by the public issue forms."))
}

$secrets = Invoke-GhJson -Arguments @("secret", "list", "--repo", $Repository, "--app", "actions", "--json", "name")
$secretNames = @($secrets.Value | ForEach-Object { $_.name })
$checks.Add((New-Check `
    -Name "Sentry release DSN configured" `
    -Passed ($secrets.Succeeded -and $secretNames -contains "FREE_FAMILY_SENTRY_DSN") `
    -Remediation "Configure FREE_FAMILY_SENTRY_DSN only after privacy/operator/Sentry settings are finalized."))

$result = [pscustomobject]@{
    repository = $Repository
    checkedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
    passed = @($checks | Where-Object passed).Count
    failed = @($checks | Where-Object { -not $_.passed }).Count
    checks = @($checks)
}

$result.checks | Format-Table `
    @{ Label = "Status"; Expression = { if ($_.passed) { "PASS" } else { "FAIL" } } }, `
    @{ Label = "Check"; Expression = { $_.name } }, `
    @{ Label = "Remediation"; Expression = { $_.remediation } } `
    -Wrap

if (-not [string]::IsNullOrWhiteSpace($OutputPath)) {
    $resolvedOutput = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputPath)
    $parent = Split-Path -Parent $resolvedOutput
    if (-not [string]::IsNullOrWhiteSpace($parent)) {
        New-Item -ItemType Directory -Force -Path $parent | Out-Null
    }
    $result | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $resolvedOutput -Encoding utf8
}

if ($Strict -and $result.failed -gt 0) {
    throw "$($result.failed) public-release repository setting check(s) failed."
}
