[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

# Live self-test of the release publish resilience helpers, run by the full-release.yml dry run when
# inject_fault=selftest. It talks to no release and uploads nothing: Invoke-WithRetry is driven by an
# action that fails once, and Send-ReleaseAssets.ps1 is run with -PlanOnly against a synthetic release
# listing that looks like a half-finished publish. Any deviation throws, which fails the job.

. (Join-Path $PSScriptRoot 'Invoke-WithRetry.ps1')

# 1. Invoke-WithRetry: the first attempt fails like a network blip, the second succeeds.
$script:calls = 0
$result = Invoke-WithRetry -Description 'Injected flaky step' -DelaySeconds @(2) -Action {
    $script:calls++
    if ($script:calls -eq 1) { throw 'injected transient failure on the first attempt' }
    'recovered'
}
if ($script:calls -ne 2 -or $result -ne 'recovered') {
    throw "Invoke-WithRetry did not retry exactly once and return the second attempt (calls: $script:calls, result: '$result')."
}
Write-Host 'Invoke-WithRetry: first attempt failed, second attempt succeeded.'

# 2. Send-ReleaseAssets planning against a half-published release.
$root = Join-Path ([System.IO.Path]::GetTempPath()) ("release-selftest-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $root | Out-Null
try {
    $names = @('same.zip', 'changed.zip', 'partial.zip', 'missing.zip')
    $paths = @()
    foreach ($name in $names) {
        $path = Join-Path $root $name
        [System.IO.File]::WriteAllText($path, "content of $name")
        $paths += $path
    }

    $sameHash = (Get-FileHash -LiteralPath (Join-Path $root 'same.zip') -Algorithm SHA256).Hash.ToLowerInvariant()
    $release = [ordered]@{
        assets = @(
            [ordered]@{ id = 1; name = 'same.zip'; state = 'uploaded'; size = 20; digest = "sha256:$sameHash" },
            [ordered]@{ id = 2; name = 'changed.zip'; state = 'uploaded'; size = 20; digest = 'sha256:' + ('0' * 64) },
            [ordered]@{ id = 3; name = 'partial.zip'; state = 'starter'; size = 0 }
        )
    }
    $releasePath = Join-Path $root 'release.json'
    ($release | ConvertTo-Json -Depth 5) | Set-Content -LiteralPath $releasePath -Encoding utf8

    $plan = @(& (Join-Path $PSScriptRoot 'Send-ReleaseAssets.ps1') -Repository 'example/example' -Tag 'selftest-v0.0.0' -AssetPaths $paths -ExistingReleaseJsonPath $releasePath -PlanOnly)
    $expected = @{ 'same.zip' = 'skip'; 'changed.zip' = 'conflict'; 'partial.zip' = 'replace'; 'missing.zip' = 'upload' }
    foreach ($name in $expected.Keys) {
        $actual = @($plan | Where-Object { $_.Name -ceq $name })
        if ($actual.Count -ne 1 -or $actual[0].Action -cne $expected[$name]) {
            throw "Send-ReleaseAssets planned '$(if ($actual.Count -gt 0) { $actual[0].Action } else { 'nothing' })' for $name; expected '$($expected[$name])'."
        }
    }
    Write-Host 'Send-ReleaseAssets plan: identical skipped, changed refused, interrupted replaced, missing uploaded.'
}
finally {
    Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host 'Release resilience self-test passed.'
