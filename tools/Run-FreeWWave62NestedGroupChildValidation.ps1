[CmdletBinding()]
param(
    [int]$Port = 6092,
    [string]$OutputDir = "freew/artifacts/wave62-linux"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot "PhysicalValidationScriptSupport.ps1")
$resolvedOutput = if ([IO.Path]::IsPathRooted($OutputDir)) { [IO.Path]::GetFullPath($OutputDir) } else { [IO.Path]::GetFullPath((Join-Path $repoRoot $OutputDir)) }
$fixtureProject = Join-Path $repoRoot "freew/tools/FreeW.GroupChildPhysicalFixture/FreeW.GroupChildPhysicalFixture.csproj"
$fixturePath = Join-Path $resolvedOutput "nested-group-child-wave62.docx"
$probePath = Join-Path $repoRoot "tools/LinuxInteractiveDocker/run-freew-wave62-nested-group-child-probe.sh"
$sessionPath = Join-Path $resolvedOutput "freew/current-session.json"
$containerName = "freex-linux-interactive-freew-$Port"
$started = $false

function Pair([System.Collections.IDictionary]$Values, [string]$Key) { return @($Values[$Key].Split(',') | ForEach-Object { [double]::Parse($_, [Globalization.CultureInfo]::InvariantCulture) }) }
function Same-Pair([double[]]$Left, [double[]]$Right) { return [Math]::Abs($Left[0]-$Right[0]) -lt 0.01 -and [Math]::Abs($Left[1]-$Right[1]) -lt 0.01 }

try {
    New-Item -ItemType Directory -Path $resolvedOutput -Force | Out-Null
    Invoke-PhysicalValidationFixture -ProjectPath $fixtureProject -Action "generate-nested" -ArtifactPath $fixturePath |
        Out-File (Join-Path $resolvedOutput "fixture-generation.txt") -Encoding utf8
    $before = Read-PhysicalValidationFixtureValues -ProjectPath $fixtureProject -Action "inspect-nested" -ArtifactPath $fixturePath
    & powershell -NoProfile -File (Join-Path $repoRoot "tools/Run-LinuxInteractiveDocker.ps1") -Action Start -App FreeW -Port $Port -OutputDir $resolvedOutput -DocumentPath $fixturePath -Replace
    $started = $true
    $session = Get-Content $sessionPath -Raw | ConvertFrom-Json
    $sessionDir = [string]$session.sessionDirectory
    Copy-Item $probePath (Join-Path $sessionDir "run-freew-wave62-nested-group-child-probe.sh") -Force
    & docker exec $containerName bash /work/run-freew-wave62-nested-group-child-probe.sh /work/nested-group-child-wave62
    if ($LASTEXITCODE -ne 0) { throw "The Linux/X11 nested group-child probe failed with exit code $LASTEXITCODE." }

    $savedPath = Join-Path $resolvedOutput "freew/documents/nested-group-child-wave62.docx"
    if (-not (Test-Path $savedPath -PathType Leaf)) { throw "The FreeW document was not persisted at '$savedPath'." }
    $after = Read-PhysicalValidationFixtureValues -ProjectPath $fixtureProject -Action "inspect-nested" -ArtifactPath $savedPath
    foreach ($key in 'outer-offset-pt','outer-size-pt','inner-offset-pt','inner-size-pt','outer-transform','inner-transform','child-transform') {
        if ($after[$key] -ne $before[$key]) { throw "Nested owning-group geometry changed for ${key}: before '$($before[$key])', after '$($after[$key])'." }
    }
    $beforeOffset = Pair $before 'child-offset-pt'; $afterOffset = Pair $after 'child-offset-pt'
    $beforeSize = Pair $before 'child-size-pt'; $afterSize = Pair $after 'child-size-pt'
    if ((Same-Pair $beforeOffset $afterOffset)) { throw "Physical nested move did not change the leaf-local offset." }
    if ($afterSize[0] -le $beforeSize[0] -or $afterSize[1] -le $beforeSize[1]) { throw "Physical nested resize did not grow both leaf dimensions." }

    $probe = Get-Content (Join-Path $sessionDir "nested-group-child-wave62/probe-results.json") -Raw | ConvertFrom-Json
    $validatedResults = @($probe.results | ForEach-Object {
        [ordered]@{ id=[string]$_.id; status="passed"; evidence=[string]$_.evidence; note=[string]$_.note }
    })
    $manifest = [ordered]@{ schemaVersion=1; suite="freew-linux-nested-group-child-wave62-physical"; platform="linux"; app="FreeW"; shell="avalonia"; results=$validatedResults; summary=[ordered]@{status="passed";passed=$validatedResults.Count+1;failed=0}; persistedGeometry=[ordered]@{exact=$true;source="FreeW.Core.IO DocxReader inspect";document=$savedPath;before=$before;after=$after;outerUnchanged=$true;innerUnchanged=$true;childMoved=$true;childResized=$true;childTransformUnchanged=($after['child-transform'] -eq $before['child-transform'])}; selectionPostcondition=$probe.selectionPostcondition; evidence=[ordered]@{session=$sessionDir;baseline=(Join-Path $sessionDir "nested-group-child-wave62/01-baseline.png");selected=(Join-Path $sessionDir "nested-group-child-wave62/02-nested-child-selected.png");moved=(Join-Path $sessionDir "nested-group-child-wave62/03-nested-child-moved.png");resizedSelected=(Join-Path $sessionDir "nested-group-child-wave62/04-nested-child-resized-selected.png");fixture=$fixturePath} }
    $manifest | ConvertTo-Json -Depth 10 | Set-Content (Join-Path $resolvedOutput "freew-wave62-nested-group-child-validation.json") -Encoding utf8
    $before | Out-File (Join-Path $resolvedOutput "inspect-before.txt") -Encoding utf8
    $after | Out-File (Join-Path $resolvedOutput "inspect-after.txt") -Encoding utf8
    Write-Host "Wave 62 physical validation passed. Manifest: $(Join-Path $resolvedOutput 'freew-wave62-nested-group-child-validation.json')"
} finally {
    if ($started) { & powershell -NoProfile -File (Join-Path $repoRoot "tools/Run-LinuxInteractiveDocker.ps1") -Action Stop -App FreeW -Port $Port }
}
