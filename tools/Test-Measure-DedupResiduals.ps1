param(
    [string]$MeasureScriptPath = (Join-Path $PSScriptRoot "Measure-DedupResiduals.ps1")
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Assert-Condition {
    param(
        [Parameter(Mandatory = $true)][bool]$Condition,
        [Parameter(Mandatory = $true)][string]$Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

function Write-Utf8Fixture {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [AllowEmptyString()][Parameter(Mandatory = $true)][string]$Content
    )

    [System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($Path)) | Out-Null
    [System.IO.File]::WriteAllText($Path, $Content, (New-Object System.Text.UTF8Encoding($false)))
}

function Invoke-FixtureGit {
    param(
        [Parameter(Mandatory = $true)][string]$Root,
        [Parameter(Mandatory = $true)][string[]]$Arguments
    )

    & git -C $Root @Arguments | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "Fixture git command failed: git $($Arguments -join ' ')"
    }
}

$fixtureRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("freex-dedup-metrics-test-" + [System.Guid]::NewGuid().ToString("N"))
$jsonPath = Join-Path $fixtureRoot "docs/unification/dedup-residual-metrics.json"
$markdownPath = Join-Path $fixtureRoot "docs/unification/dedup-residual-metrics.md"

$rendererRoots = @(
    "src/FreeX.App.Host",
    "src/FreeX.App.Avalonia",
    "freew/FreeW.App.Host",
    "freew/FreeW.App.Avalonia",
    "freep/FreeP.App.Host",
    "freep/FreeP.App.Rendering.Wpf",
    "freep/FreeP.App.Avalonia",
    "freep/FreeP.App.Rendering.Avalonia"
)

$sharedCode = @'
namespace Fixture;
public static class SharedRendererFlow
{
    public static int Execute(int value)
    {
        var adjusted = value + 10;
        var doubled = adjusted * 2;
        return doubled - 4;
    }
}
'@ + "`n"

try {
    [System.IO.Directory]::CreateDirectory($fixtureRoot) | Out-Null
    Invoke-FixtureGit -Root $fixtureRoot -Arguments @("init", "--initial-branch=main")
    Invoke-FixtureGit -Root $fixtureRoot -Arguments @("config", "user.email", "dedup-metrics@example.invalid")
    Invoke-FixtureGit -Root $fixtureRoot -Arguments @("config", "user.name", "Dedup Metrics Test")
    Invoke-FixtureGit -Root $fixtureRoot -Arguments @("config", "core.autocrlf", "false")

    foreach ($rendererRoot in $rendererRoots) {
        Write-Utf8Fixture -Path (Join-Path $fixtureRoot "$rendererRoot/Renderer.cs") -Content $sharedCode
    }
    Write-Utf8Fixture -Path (Join-Path $fixtureRoot "freew/FreeW.App.Host/Empty.cs") -Content ""
    Write-Utf8Fixture -Path (Join-Path $fixtureRoot "src/FreeX.App.Host/obj/Ignored.cs") -Content $sharedCode
    Write-Utf8Fixture -Path (Join-Path $fixtureRoot "src/FreeX.App.Host/Ignored.g.cs") -Content $sharedCode
    Write-Utf8Fixture -Path (Join-Path $fixtureRoot "shared/Free.Shared.Demo/Free.Shared.Demo.csproj") -Content "<Project Sdk=`"Microsoft.NET.Sdk`" />`n"

    $catalogTemplate = @'
<?xml version="1.0" encoding="utf-8"?>
<root>
  <data name="Common"><value>Common value</value></data>
  <data name="Unique"><value>{0}</value></data>
</root>
'@
    Write-Utf8Fixture -Path (Join-Path $fixtureRoot "shared/Free.Shared.Localization/Resources/Strings.resx") -Content ($catalogTemplate -f "Shared only")
    Write-Utf8Fixture -Path (Join-Path $fixtureRoot "src/FreeX.App.Localization/Resources/Strings.resx") -Content ($catalogTemplate -f "FreeX only")
    Write-Utf8Fixture -Path (Join-Path $fixtureRoot "freew/FreeW.App.Localization/Resources/Strings.resx") -Content ($catalogTemplate -f "FreeW only")
    Write-Utf8Fixture -Path (Join-Path $fixtureRoot "freep/FreeP.App.Localization/Resources/Strings.resx") -Content ($catalogTemplate -f "FreeP only")

    Invoke-FixtureGit -Root $fixtureRoot -Arguments @("add", ".")
    Invoke-FixtureGit -Root $fixtureRoot -Arguments @("commit", "-m", "fixture baseline")
    Invoke-FixtureGit -Root $fixtureRoot -Arguments @("update-ref", "refs/remotes/origin/main", "HEAD")
    Invoke-FixtureGit -Root $fixtureRoot -Arguments @("checkout", "-b", "campaign")
    Write-Utf8Fixture -Path (Join-Path $fixtureRoot "src/FreeX.App.Host/CampaignOnly.cs") -Content "namespace Fixture;`npublic sealed class CampaignOnly { }`n"
    Invoke-FixtureGit -Root $fixtureRoot -Arguments @("add", ".")
    Invoke-FixtureGit -Root $fixtureRoot -Arguments @("commit", "-m", "fixture campaign")

    & $MeasureScriptPath -RepositoryRoot $fixtureRoot -JsonPath $jsonPath -MarkdownPath $markdownPath -BlockSize 4 -MinimumBlockCharacters 20 -MaximumFingerprintOccurrences 64 -TopCandidateCount 5
    $report = Get-Content -LiteralPath $jsonPath -Raw | ConvertFrom-Json
    Assert-Condition ($report.schema -eq "freex.dedup-residual-metrics.v1") "Unexpected metrics schema."
    Assert-Condition ($report.renderer.roots.Count -eq 8) "All eight renderer roots must be measured."
    Assert-Condition ($report.renderer.files.Count -eq 10) "Generated and obj C# files must be excluded while empty source files remain inventoried."
    $emptyFile = @($report.renderer.files | Where-Object { $_.path -eq "freew/FreeW.App.Host/Empty.cs" })[0]
    Assert-Condition ($emptyFile.codeLines -eq 0) "Empty source files must report zero code LOC."
    Assert-Condition ($report.renderer.duplicateBlocks.exact.Count -gt 0) "The fixture must produce exact duplicate blocks."
    Assert-Condition ($report.renderer.duplicateCoverage.exact.duplicateLines -gt 0) "Exact duplicate coverage must be nonzero."
    Assert-Condition ($report.sharedProjects.count -eq 1) "The shared project count must exclude nothing from the valid fixture project."
    $freeXFreeW = @($report.localization.pairwiseValueOverlap | Where-Object { $_.catalogA -eq "FreeX" -and $_.catalogB -eq "FreeW" })[0]
    Assert-Condition ($freeXFreeW.commonValueCount -eq 1) "Localization value overlap must identify the shared fixture value."
    Assert-Condition ($report.repository.campaignLocDelta.allCSharp.addedLines -eq 2) "Campaign C# LOC delta must use the merge base."
    Assert-Condition ((Get-Content -LiteralPath $markdownPath -Raw) -match 'Renderer\.cs:\d+-\d+') "Markdown candidates must include file and line ranges."

    $jsonHashBefore = (Get-FileHash -Algorithm SHA256 -LiteralPath $jsonPath).Hash
    $markdownHashBefore = (Get-FileHash -Algorithm SHA256 -LiteralPath $markdownPath).Hash
    & $MeasureScriptPath -RepositoryRoot $fixtureRoot -JsonPath $jsonPath -MarkdownPath $markdownPath -BlockSize 4 -MinimumBlockCharacters 20 -MaximumFingerprintOccurrences 64 -TopCandidateCount 5
    $jsonHashAfter = (Get-FileHash -Algorithm SHA256 -LiteralPath $jsonPath).Hash
    $markdownHashAfter = (Get-FileHash -Algorithm SHA256 -LiteralPath $markdownPath).Hash
    Assert-Condition ($jsonHashBefore -eq $jsonHashAfter) "JSON output must be byte-for-byte deterministic."
    Assert-Condition ($markdownHashBefore -eq $markdownHashAfter) "Markdown output must be byte-for-byte deterministic."

    & $MeasureScriptPath -RepositoryRoot $fixtureRoot -JsonPath $jsonPath -MarkdownPath $markdownPath -BlockSize 4 -MinimumBlockCharacters 20 -MaximumFingerprintOccurrences 64 -TopCandidateCount 5 -Check
    Write-Host "Dedup residual measurement fixture tests passed."
}
finally {
    if (Test-Path -LiteralPath $fixtureRoot) {
        Remove-Item -LiteralPath $fixtureRoot -Recurse -Force
    }
}
