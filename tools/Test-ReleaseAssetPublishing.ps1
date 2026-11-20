[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

# Offline proof of the two pieces that make release publication resumable without ever overwriting a
# published asset silently: Send-ReleaseAssets.ps1's plan, and Invoke-WithRetry.ps1's bounded retry.

$errors = [System.Collections.Generic.List[string]]::new()
$root = Join-Path ([System.IO.Path]::GetTempPath()) ("freex-asset-publish-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $root | Out-Null

function New-LocalFile([string]$Name, [string]$Content) {
    $path = Join-Path $root $Name
    [System.IO.File]::WriteAllText($path, $Content, (New-Object System.Text.UTF8Encoding $false))
    return $path
}

function Get-Digest([string]$Path) { return 'sha256:' + (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant() }

function Get-Plan([string[]]$Paths, [object[]]$RemoteAssets) {
    $jsonPath = Join-Path $root 'release.json'
    [ordered]@{ assets = @($RemoteAssets) } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $jsonPath -Encoding utf8
    return @(& (Join-Path $PSScriptRoot 'Send-ReleaseAssets.ps1') -Repository 'o/r' -Tag 't' -AssetPaths $Paths -ExistingReleaseJsonPath $jsonPath -PlanOnly)
}

function Get-Action([object[]]$Plan, [string]$Name) { return [string](@($Plan | Where-Object { $_.Name -ceq $Name })[0].Action) }

function Assert-Action([string]$Case, [object[]]$Plan, [string]$Name, [string]$Expected) {
    $actual = Get-Action $Plan $Name
    if ($actual -ne $Expected) { $errors.Add("${Case}: '$Name' planned '$actual', expected '$Expected'.") }
}

try {
    $same = New-LocalFile 'same.zip' 'identical'
    $changed = New-LocalFile 'changed.zip' 'local content'
    $fresh = New-LocalFile 'fresh.zip' 'new'
    $partial = New-LocalFile 'partial.zip' 'whole'
    $nodigest = New-LocalFile 'nodigest.zip' 'abc'

    $remote = @(
        [ordered]@{ id = 1; name = 'same.zip'; state = 'uploaded'; size = (Get-Item $same).Length; digest = (Get-Digest $same) },
        [ordered]@{ id = 2; name = 'changed.zip'; state = 'uploaded'; size = 12; digest = ('sha256:' + ('0' * 64)) },
        [ordered]@{ id = 3; name = 'partial.zip'; state = 'starter'; size = 0; digest = $null },
        [ordered]@{ id = 4; name = 'nodigest.zip'; state = 'uploaded'; size = (Get-Item $nodigest).Length; digest = $null }
    )
    $plan = Get-Plan @($same, $changed, $fresh, $partial, $nodigest) $remote
    Assert-Action 'resume' $plan 'same.zip' 'skip'
    Assert-Action 'resume' $plan 'changed.zip' 'conflict'
    Assert-Action 'resume' $plan 'fresh.zip' 'upload'
    Assert-Action 'resume' $plan 'partial.zip' 'replace'
    Assert-Action 'resume' $plan 'nodigest.zip' 'skip'

    # A different size without a digest is still a conflict, never a silent overwrite.
    $remoteSize = @([ordered]@{ id = 5; name = 'nodigest.zip'; state = 'uploaded'; size = 999; digest = $null })
    Assert-Action 'size mismatch' (Get-Plan @($nodigest) $remoteSize) 'nodigest.zip' 'conflict'

    # An empty release plans an upload for everything.
    Assert-Action 'first publish' (Get-Plan @($fresh) @()) 'fresh.zip' 'upload'

    # The real (non-plan) path must refuse a conflict BEFORE touching the release: no gh is reachable
    # here, so reaching an upload or a delete would surface a different error than the refusal.
    $jsonPath = Join-Path $root 'release.json'
    [ordered]@{ assets = @($remote[1]) } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $jsonPath -Encoding utf8
    $message = ''
    try { & (Join-Path $PSScriptRoot 'Send-ReleaseAssets.ps1') -Repository 'o/r' -Tag 't' -AssetPaths @($changed, $fresh) -ExistingReleaseJsonPath $jsonPath | Out-Null }
    catch { $message = $_.Exception.Message }
    if ($message -notmatch 'refusing to overwrite') { $errors.Add("conflict: expected a refusal, got '$message'.") }

    # Invoke-WithRetry: succeeds after transient failures, is bounded, and rethrows a permanent one.
    . (Join-Path $PSScriptRoot 'Invoke-WithRetry.ps1')
    $script:calls = 0
    $script:slept = @()
    $result = Invoke-WithRetry -Description 'flaky' -Attempts 3 -DelaySeconds @(1, 2) -Sleep { param($s) $script:slept += $s } -Action {
        $script:calls++
        if ($script:calls -lt 3) { throw 'transient' }
        'ok'
    }
    if ($result -ne 'ok' -or $script:calls -ne 3) { $errors.Add("retry: expected success on attempt 3, got '$result' after $($script:calls) call(s).") }
    if (($script:slept -join ',') -ne '1,2') { $errors.Add("retry: expected delays 1,2 but slept '$($script:slept -join ',')'.") }

    $script:calls = 0
    $thrown = ''
    try { Invoke-WithRetry -Description 'permanent' -Attempts 3 -Sleep { param($s) } -Action { $script:calls++; throw 'always' } | Out-Null }
    catch { $thrown = $_.Exception.Message }
    if ($script:calls -ne 3 -or $thrown -notmatch 'failed after 3 attempt') { $errors.Add("retry: a permanent failure must stop after exactly 3 attempts and say so (calls=$($script:calls), '$thrown').") }

    # A native non-zero exit code is a failure too, not just an exception.
    $script:calls = 0
    $thrown = ''
    try { Invoke-WithRetry -Description 'exit code' -Attempts 2 -Sleep { param($s) } -Action { $script:calls++; pwsh -NoProfile -Command 'exit 7' } | Out-Null }
    catch { $thrown = $_.Exception.Message }
    if ($script:calls -ne 2 -or $thrown -notmatch 'exit code 7') { $errors.Add("retry: a non-zero native exit must be retried then reported (calls=$($script:calls), '$thrown').") }

    # No output must mean an EMPTY array for callers that wrap the result in @() (git ls-remote of an
    # absent tag), not a one-element array holding $null.
    $empty = @(Invoke-WithRetry -Description 'empty' -Action { })
    if ($empty.Count -ne 0) { $errors.Add("retry: an action with no output produced $($empty.Count) element(s), expected 0.") }
    $false1 = Invoke-WithRetry -Description 'false value' -Action { $false }
    if ($false1 -ne $false -or $null -eq $false1) { $errors.Add('retry: a $false result must be returned as $false.') }
}
finally {
    Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
}

if ($errors.Count -gt 0) {
    throw ("Release asset publishing contract failed:`n - " + ($errors -join "`n - "))
}

Write-Host 'Release asset publishing contract passed: resumable plan, immutable-asset refusal, bounded retry.'
