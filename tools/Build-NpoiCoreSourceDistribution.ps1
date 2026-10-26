[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$sourceUri = 'https://github.com/nissl-lab/npoi.git'
$sourceCommit = 'ab1aa0f7d9aaea06159eeb0311b7b74e752d819a'
$expectedSdk = '10.0.400'
$expectedHash = '72B809E480E713F89D4BF4B49C427E16900F61F1A398CCD734B52E63EA97471E'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$outputPath = Join-Path $repositoryRoot 'third_party/npoi/2.8.1/NPOI.Core.dll'
$temporaryParent = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$temporaryRoot = Join-Path $temporaryParent ("freex-npoi-source-" + [Guid]::NewGuid().ToString('N'))

try {
    $sdkVersion = (& dotnet --version).Trim()
    if ($sdkVersion -ne $expectedSdk) {
        throw "NPOI.Core must be reproduced with .NET SDK $expectedSdk; active SDK is $sdkVersion."
    }

    & git clone --quiet --filter=blob:none --no-checkout $sourceUri $temporaryRoot
    if ($LASTEXITCODE -ne 0) { throw 'Failed to clone the NPOI source repository.' }

    & git -C $temporaryRoot checkout --quiet --detach $sourceCommit
    if ($LASTEXITCODE -ne 0) { throw "Failed to check out NPOI source commit $sourceCommit." }

    $actualCommit = (& git -C $temporaryRoot rev-parse HEAD).Trim()
    if ($actualCommit -ne $sourceCommit) {
        throw "NPOI source checkout mismatch: expected $sourceCommit, found $actualCommit."
    }

    $projectPath = Join-Path $temporaryRoot 'main/NPOI.Core.csproj'
    & dotnet build $projectPath --configuration Release --framework net10.0 `
        -p:ContinuousIntegrationBuild=true "-p:PathMap=$temporaryRoot=/_/npoi"
    if ($LASTEXITCODE -ne 0) { throw 'NPOI.Core source build failed.' }

    $builtAssembly = Join-Path $temporaryRoot 'solution/Release/net10.0/NPOI.Core.dll'
    $actualHash = (Get-FileHash -LiteralPath $builtAssembly -Algorithm SHA256).Hash
    if ($actualHash -ne $expectedHash) {
        throw "NPOI.Core deterministic hash mismatch: expected $expectedHash, found $actualHash."
    }

    Copy-Item -LiteralPath $builtAssembly -Destination $outputPath -Force
    Write-Host "Rebuilt $outputPath ($actualHash)."
}
finally {
    & dotnet build-server shutdown | Out-Host

    if (Test-Path -LiteralPath $temporaryRoot) {
        $resolvedTemporaryRoot = [IO.Path]::GetFullPath($temporaryRoot)
        if (-not $resolvedTemporaryRoot.StartsWith($temporaryParent, [StringComparison]::OrdinalIgnoreCase) -or
            -not (Split-Path -Leaf $resolvedTemporaryRoot).StartsWith('freex-npoi-source-', [StringComparison]::Ordinal)) {
            throw "Refusing to remove unexpected temporary path: $resolvedTemporaryRoot"
        }

        Remove-Item -LiteralPath $resolvedTemporaryRoot -Recurse -Force
    }
}
