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
$branchProtectionReady = $branchProtection.Succeeded -and
    $null -ne $branchProtection.Value.required_pull_request_reviews -and
    [int]$branchProtection.Value.required_pull_request_reviews.required_approving_review_count -ge 1 -and
    $null -ne $branchProtection.Value.required_status_checks -and
    (@($branchProtection.Value.required_status_checks.contexts).Count -gt 0 -or
        @($branchProtection.Value.required_status_checks.checks).Count -gt 0) -and
    $branchProtection.Value.allow_force_pushes.enabled -ne $true -and
    $branchProtection.Value.allow_deletions.enabled -ne $true

$rulesets = Invoke-GhJson -Arguments @("api", "repos/$Repository/rulesets")
$hasProtectiveActiveRuleset = $false
if ($rulesets.Succeeded) {
    foreach ($ruleset in @($rulesets.Value | Where-Object { $_.enforcement -eq "active" -and $_.target -eq "branch" })) {
        $rulesetDetail = Invoke-GhJson -Arguments @("api", "repos/$Repository/rulesets/$($ruleset.id)")
        if (-not $rulesetDetail.Succeeded) {
            continue
        }

        $includedRefs = @($rulesetDetail.Value.conditions.ref_name.include)
        $coversMain = $includedRefs -contains "~DEFAULT_BRANCH" -or $includedRefs -contains "refs/heads/main"
        $ruleTypes = @($rulesetDetail.Value.rules | ForEach-Object { $_.type })
        $statusRules = @($rulesetDetail.Value.rules | Where-Object { $_.type -eq "required_status_checks" })
        $hasRequiredChecks = @(
            $statusRules | Where-Object { @($_.parameters.required_status_checks).Count -gt 0 }
        ).Count -gt 0
        if ($coversMain -and
            $ruleTypes -contains "pull_request" -and
            $hasRequiredChecks -and
            $ruleTypes -contains "non_fast_forward" -and
            $ruleTypes -contains "deletion") {
            $hasProtectiveActiveRuleset = $true
            break
        }
    }
}
$checks.Add((New-Check `
    -Name "main branch protection or active branch ruleset" `
    -Passed ($branchProtectionReady -or $hasProtectiveActiveRuleset) `
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

foreach ($workflowPath in @("ci.yml", "codeql.yml")) {
    $workflow = Invoke-GhJson -Arguments @("api", "repos/$Repository/actions/workflows/$workflowPath")
    $checks.Add((New-Check `
        -Name "active workflow: $workflowPath" `
        -Passed ($workflow.Succeeded -and $workflow.Value.state -eq "active") `
        -Remediation "Enable the $workflowPath workflow in the repository Actions settings."))
}

$releaseEnvironment = Invoke-GhJson -Arguments @("api", "repos/$Repository/environments/public-preview")
$requiredReviewerRules = @(
    if ($releaseEnvironment.Succeeded) {
        $releaseEnvironment.Value.protection_rules | Where-Object { $_.type -eq "required_reviewers" }
    }
)
$hasProtectedReleaseEnvironment = $requiredReviewerRules.Count -gt 0 -and @(
    $requiredReviewerRules | ForEach-Object { @($_.reviewers).Count } | Where-Object { $_ -gt 0 }
).Count -gt 0
$checks.Add((New-Check `
    -Name "protected public-preview deployment environment" `
    -Passed $hasProtectedReleaseEnvironment `
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
