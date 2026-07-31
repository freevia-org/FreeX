<##
.SYNOPSIS
  Runs the dedicated FreeP Wave 89 physical X11 multi-selection transform lane.
#>
[CmdletBinding()]
param(
    [ValidateRange(1024, 65535)][int]$Port = 6108,
    [ValidateRange(640, 7680)][int]$Width = 1280,
    [ValidateRange(480, 4320)][int]$Height = 820,
    [ValidateRange(72, 240)][int]$Dpi = 96,
    [ValidateSet("2g", "4g", "6g", "8g")][string]$MemoryLimit = "4g",
    [string]$OutputDir = "artifacts/freep-multiselect-x11-wave89-20260801",
    [string]$PublishDir = "",
    [switch]$SkipPublish,
    [switch]$SkipImageBuild,
    [switch]$Replace,
    [switch]$KeepContainer
)
$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$resolvedOutputRoot = if ([IO.Path]::IsPathRooted($OutputDir)) { [IO.Path]::GetFullPath($OutputDir) } else { [IO.Path]::GetFullPath((Join-Path $repoRoot $OutputDir)) }
$fixtureName = "freep-multiselect-x11-wave89-fixture.pptx"
$fixturePath = Join-Path $resolvedOutputRoot $fixtureName
$genericRunner = Join-Path $PSScriptRoot "Run-LinuxInteractiveDocker.ps1"
$probeSource = Join-Path $PSScriptRoot "LinuxInteractiveDocker/run-freep-multiselect-x11-wave89-probe.sh"
$schemaPath = Join-Path $PSScriptRoot "LinuxInteractiveDocker/freep-multiselect-x11-wave89-validation.schema.json"
$baseFixturePath = Join-Path $repoRoot "tools/FreeP.RenderCompare/corpus/02-autoshapes.pptx"
$requiredIds = @("visible-window-discovery", "two-shape-pointer-selection", "group-resize-handle-drag", "saved-resize-geometry", "group-rotate-handle-drag", "saved-rotate-geometry", "ctrl-z-restores-resize", "escape-cancel-preserves-package", "capture-loss-cancel-preserves-package")

function Invoke-External { param([Parameter(Mandatory=$true)][string]$FilePath, [Parameter(Mandatory=$true)][string[]]$Arguments); Push-Location $repoRoot; try { & $FilePath @Arguments; if ($LASTEXITCODE -ne 0) { throw "$FilePath exited with code $LASTEXITCODE." } } finally { Pop-Location } }
function Write-Fixture {
    param([string]$Source, [string]$Destination)
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    New-Item -ItemType Directory -Path (Split-Path -Parent $Destination) -Force | Out-Null
    $temporary = "$Destination.tmp"; Remove-Item -LiteralPath $temporary -Force -ErrorAction SilentlyContinue
    $sourceZip = [IO.Compression.ZipFile]::OpenRead($Source); $stream = [IO.File]::Open($temporary, [IO.FileMode]::Create, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None); $zip = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Create, $false)
    try {
        foreach ($entry in $sourceZip.Entries) {
            $out = $zip.CreateEntry($entry.FullName, [IO.Compression.CompressionLevel]::Optimal); $outStream = $out.Open()
            try {
                if ($entry.FullName -eq "ppt/slides/slide1.xml") {
                    $reader = [IO.StreamReader]::new($entry.Open(), [Text.Encoding]::UTF8); try { $doc = [Xml.XmlDocument]::new(); $doc.LoadXml($reader.ReadToEnd()) } finally { $reader.Dispose() }
                    $ns = [Xml.XmlNamespaceManager]::new($doc.NameTable); $ns.AddNamespace("p", "http://schemas.openxmlformats.org/presentationml/2006/main"); $ns.AddNamespace("a", "http://schemas.openxmlformats.org/drawingml/2006/main")
                    foreach ($shape in @($doc.SelectNodes("//p:sp", $ns))) { if ($shape.SelectSingleNode("p:nvSpPr/p:cNvPr", $ns).GetAttribute("id") -notin @("2", "3")) { [void]$shape.ParentNode.RemoveChild($shape) } }
                    $specs = @(@{ id = "2"; name = "Wave89 Left"; x = 1905000; y = 1714500; cx = 1905000; cy = 1143000; text = "Left" }, @{ id = "3"; name = "Wave89 Right"; x = 4762500; y = 2857500; cx = 1905000; cy = 1143000; text = "Right" })
                    foreach ($spec in $specs) { $shape = $doc.SelectSingleNode("//p:sp[p:nvSpPr/p:cNvPr[@id='$($spec.id)']]", $ns); $shape.SelectSingleNode("p:nvSpPr/p:cNvPr", $ns).SetAttribute("name", $spec.name); $xf = $shape.SelectSingleNode("p:spPr/a:xfrm", $ns); $xf.SetAttribute("rot", "0"); $xf.SelectSingleNode("a:off", $ns).SetAttribute("x", "$($spec.x)"); $xf.SelectSingleNode("a:off", $ns).SetAttribute("y", "$($spec.y)"); $xf.SelectSingleNode("a:ext", $ns).SetAttribute("cx", "$($spec.cx)"); $xf.SelectSingleNode("a:ext", $ns).SetAttribute("cy", "$($spec.cy)"); $text = $shape.SelectSingleNode(".//a:t", $ns); if ($null -ne $text) { $text.InnerText = $spec.text } }
                    $settings = [Xml.XmlWriterSettings]::new(); $settings.Encoding = [Text.UTF8Encoding]::new($false); $settings.Indent = $false; $memory = [IO.MemoryStream]::new(); $writer = [Xml.XmlWriter]::Create($memory, $settings); try { $doc.Save($writer); $writer.Flush(); $bytes = $memory.ToArray() } finally { $writer.Dispose(); $memory.Dispose() }; $outStream.Write($bytes, 0, $bytes.Length)
                } else { $input = $entry.Open(); try { $input.CopyTo($outStream) } finally { $input.Dispose() } }
            } finally { $outStream.Dispose() }
        }
    } finally { $zip.Dispose(); $stream.Dispose(); $sourceZip.Dispose() }
    Move-Item -LiteralPath $temporary -Destination $Destination -Force
}
function Assert-Manifest {
    param([string]$ManifestPath, [string]$EvidenceDirectory)
    $manifest = Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json
    if ($manifest.schemaVersion -ne 1 -or $manifest.suite -ne "freep-linux-multiselect-x11-wave89-physical" -or $manifest.platform -ne "linux" -or $manifest.shell -ne "avalonia" -or $manifest.app -ne "FreeP" -or $manifest.baseline) { throw "Wave 89 manifest header failed." }
    if ($manifest.fixture.file -ne $fixtureName -or @($manifest.fixture.shapes).Count -ne 2) { throw "Wave 89 fixture contract failed." }
    $ids = @($manifest.results | ForEach-Object { [string]$_.id }); if ([string]::Join("|", $ids) -ne [string]::Join("|", $requiredIds)) { throw "Wave 89 result IDs/order failed." }
    if ($manifest.results.Count -ne 9 -or $manifest.summary.total -ne 9 -or $manifest.summary.failed -ne 0 -or $manifest.summary.passed -ne 9) { throw "Wave 89 result summary failed." }
    if ($manifest.calibration.status -ne "passed" -or @($manifest.screenshots).Count -lt 7) { throw "Wave 89 calibration/screenshot contract failed." }
    foreach ($result in @($manifest.results)) { if ($result.category -ne "physical-x11-multiselect" -or $result.status -ne "passed" -or $result.evidenceLevel -ne "physical-x11-input" -or @($result.evidence).Count -lt 1) { throw "Result '$($result.id)' failed the physical evidence contract." }; foreach ($name in @($result.evidence)) { $path = Join-Path $EvidenceDirectory ([IO.Path]::GetFileName([string]$name)); if ([IO.Path]::GetFileName([string]$name) -ne [string]$name -or -not (Test-Path -LiteralPath $path -PathType Leaf) -or (Get-Item -LiteralPath $path).Length -le 0) { throw "Missing or empty evidence '$name' for '$($result.id)'." } } }
    foreach ($shot in @($manifest.screenshots)) { $path = Join-Path $EvidenceDirectory ([IO.Path]::GetFileName([string]$shot.name)); if ([IO.Path]::GetFileName([string]$shot.name) -ne [string]$shot.name -or -not (Test-Path -LiteralPath $path -PathType Leaf) -or (Get-Item -LiteralPath $path).Length -le 0) { throw "Missing or empty screenshot '$($shot.name)'." } }
    $expectedStates = @{
        baseline = @(@{ id = 2; name = "Wave89 Left"; x = 1905000; y = 1714500; cx = 1905000; cy = 1143000; rotation = 0 }, @{ id = 3; name = "Wave89 Right"; x = 4762500; y = 2857500; cx = 1905000; cy = 1143000; rotation = 0 })
        afterResize = @(@{ id = 2; name = "Wave89 Left"; x = 1905000; y = 1714500; cx = 2286000; cy = 1714500; rotation = 0 }, @{ id = 3; name = "Wave89 Right"; x = 5334000; y = 3429000; cx = 2286000; cy = 1714500; rotation = 0 })
        afterRotate = @(@{ id = 2; name = "Wave89 Left"; x = 4476750; y = 857250; cx = 2286000; cy = 1714500; rotation = 90 }, @{ id = 3; name = "Wave89 Right"; x = 2762250; y = 4286250; cx = 2286000; cy = 1714500; rotation = 90 })
        afterUndo = @(@{ id = 2; name = "Wave89 Left"; x = 1905000; y = 1714500; cx = 2286000; cy = 1714500; rotation = 0 }, @{ id = 3; name = "Wave89 Right"; x = 5334000; y = 3429000; cx = 2286000; cy = 1714500; rotation = 0 })
        afterEscape = @(@{ id = 2; name = "Wave89 Left"; x = 1905000; y = 1714500; cx = 2286000; cy = 1714500; rotation = 0 }, @{ id = 3; name = "Wave89 Right"; x = 5334000; y = 3429000; cx = 2286000; cy = 1714500; rotation = 0 })
        afterCaptureLoss = @(@{ id = 2; name = "Wave89 Left"; x = 1905000; y = 1714500; cx = 2286000; cy = 1714500; rotation = 0 }, @{ id = 3; name = "Wave89 Right"; x = 5334000; y = 3429000; cx = 2286000; cy = 1714500; rotation = 0 })
    }
    foreach ($state in $expectedStates.Keys) {
        $value = $manifest.packageStates.$state
        if ($value.packageSha256 -notmatch '^[0-9a-f]{64}$' -or @($value.shapes).Count -ne 2) { throw "Package state '$state' failed the strict geometry/hash contract." }
        $actualShapes = @($value.shapes) | Sort-Object id
        for ($index = 0; $index -lt 2; $index++) {
            $actual = $actualShapes[$index]; $expected = $expectedStates[$state][$index]
            if ($actual.id -ne $expected.id -or $actual.name -ne $expected.name -or $actual.bounds.x -ne $expected.x -or $actual.bounds.y -ne $expected.y -or $actual.bounds.cx -ne $expected.cx -or $actual.bounds.cy -ne $expected.cy -or [Math]::Abs([double]$actual.rotation - $expected.rotation) -gt 0.001) { throw "Package state '$state' shape $($expected.id) failed exact EMU/rotation validation." }
        }
    }
    if ($manifest.packageStates.afterEscape.packageSha256 -ne $manifest.packageStates.afterUndo.packageSha256 -or $manifest.packageStates.afterCaptureLoss.packageSha256 -ne $manifest.packageStates.afterUndo.packageSha256) { throw "Cancellation package hashes did not remain identical to the restored package." }
    $manifest.contractValidation = [ordered]@{ status = "passed"; validator = "tools/Run-FreePMultiSelectionX11Validation.ps1"; contractReference = "tools/LinuxInteractiveDocker/freep-multiselect-x11-wave89-validation.schema.json" }
    $manifest | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $ManifestPath -Encoding utf8
    return $manifest
}

if (-not (Test-Path -LiteralPath $baseFixturePath -PathType Leaf)) { throw "Base fixture missing: $baseFixturePath" }; if (-not (Test-Path -LiteralPath $probeSource -PathType Leaf)) { throw "Probe missing: $probeSource" }; if (-not (Test-Path -LiteralPath $schemaPath -PathType Leaf)) { throw "Schema missing: $schemaPath" }
Write-Fixture -Source $baseFixturePath -Destination $fixturePath; New-Item -ItemType Directory -Path $resolvedOutputRoot -Force | Out-Null
$started = $false; $sessionDirectory = $null; $manifestPath = $null; $probeExitCode = 1
try {
    $startArgs = @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", $genericRunner, "-Action", "Start", "-App", "FreeP", "-Port", "$Port", "-Width", "$Width", "-Height", "$Height", "-Dpi", "$Dpi", "-MemoryLimit", $MemoryLimit, "-OutputDir", $resolvedOutputRoot, "-DocumentPath", $fixturePath)
    if ($PublishDir) { $startArgs += @("-PublishDir", $PublishDir) }; if ($SkipPublish) { $startArgs += "-SkipPublish" }; if ($SkipImageBuild) { $startArgs += "-SkipImageBuild" }; if ($Replace) { $startArgs += "-Replace" }
    Invoke-External powershell.exe $startArgs ; $started = $true
    $session = Get-Content -LiteralPath (Join-Path $resolvedOutputRoot "freep/current-session.json") -Raw | ConvertFrom-Json; $sessionDirectory = [IO.Path]::GetFullPath([string]$session.sessionDirectory); $probeInWork = Join-Path $sessionDirectory "freep-multiselect-x11-wave89-probe.sh"; Copy-Item -LiteralPath $probeSource -Destination $probeInWork -Force
    $manifestPath = Join-Path $sessionDirectory "freep-multiselect-x11-wave89-validation/results.json"; $evidenceDirectory = Split-Path -Parent $manifestPath; New-Item -ItemType Directory -Path $evidenceDirectory -Force | Out-Null
    Push-Location $repoRoot; try { $dockerArgs = @("exec", "--env", "FREEP_DOCUMENT_PATH=/documents/$fixtureName", "--env", "FREEP_EXPECTED_DOCUMENT_NAME=$fixtureName", "--env", "FREEP_EXPECTED_WINDOW_PATTERN=FreeP", $session.containerName, "bash", "/work/freep-multiselect-x11-wave89-probe.sh", "/work/freep-multiselect-x11-wave89-validation"); $probeOutput = @(& docker @dockerArgs 2>&1); $probeExitCode = $LASTEXITCODE } finally { Pop-Location }
    $probeOutput | Set-Content -LiteralPath (Join-Path $evidenceDirectory "probe.log") -Encoding utf8; Invoke-External docker @("cp", "$($session.containerName):/work/freep-multiselect-x11-wave89-validation/.", $evidenceDirectory)
    if ($started -and -not $KeepContainer) { Invoke-External powershell.exe @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", $genericRunner, "-Action", "Stop", "-App", "FreeP", "-Port", "$Port", "-OutputDir", $resolvedOutputRoot); $started = $false }
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) { throw "Probe did not write manifest: $manifestPath" }; $manifest = Assert-Manifest -ManifestPath $manifestPath -EvidenceDirectory $evidenceDirectory; $report = [ordered]@{ suite = $manifest.suite; probeExitCode = $probeExitCode; manifest = $manifestPath; evidenceDirectory = $evidenceDirectory; fixture = $fixturePath; results = $manifest.summary }; $report | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $resolvedOutputRoot "wave89-report.json") -Encoding utf8
    Write-Host "Manifest contract validation: passed"; Write-Host "Results: $($manifest.summary.passed) passed, $($manifest.summary.failed) failed, $($manifest.summary.total) total"; Write-Host "Manifest: $manifestPath"; Write-Host "Evidence: $evidenceDirectory"; if ($probeExitCode -ne 0) { throw "Wave 89 probe exited with code $probeExitCode." }
} finally { if ($started -and -not $KeepContainer) { try { Invoke-External powershell.exe @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", $genericRunner, "-Action", "Stop", "-App", "FreeP", "-Port", "$Port", "-OutputDir", $resolvedOutputRoot) } catch { Write-Warning "Could not stop harness-owned FreeP container: $($_.Exception.Message)" } } }
