<##
.SYNOPSIS
  Runs the dedicated FreeP Wave 61 rotated-shape text editing X11 lane.
#>
[CmdletBinding()]
param(
    [ValidateRange(1024, 65535)][int]$Port = 6097,
    [ValidateRange(640, 7680)][int]$Width = 1280,
    [ValidateRange(480, 4320)][int]$Height = 820,
    [ValidateRange(72, 240)][int]$Dpi = 96,
    [ValidateSet("2g", "4g", "6g", "8g")][string]$MemoryLimit = "4g",
    [string]$OutputDir = "artifacts/freep-rotated-shape-text-edit-wave61",
    [string]$PublishDir = "",
    [switch]$SkipPublish,
    [switch]$SkipImageBuild,
    [switch]$Replace,
    [switch]$KeepContainer
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot "ToolScriptSupport.ps1")
$resolvedOutputRoot = if ([IO.Path]::IsPathRooted($OutputDir)) { [IO.Path]::GetFullPath($OutputDir) } else { [IO.Path]::GetFullPath((Join-Path $repoRoot $OutputDir)) }
$fixturePath = Join-Path $resolvedOutputRoot "rotated-shape-text-fixture.pptx"
$baseFixturePath = Join-Path $repoRoot "tools/FreeP.RenderCompare/corpus/02-autoshapes.pptx"
$genericRunner = Join-Path $PSScriptRoot "Run-LinuxInteractiveDocker.ps1"
$probeSource = Join-Path $PSScriptRoot "LinuxInteractiveDocker/run-freep-rotated-shape-text-edit.sh"
$schemaPath = Join-Path $PSScriptRoot "LinuxInteractiveDocker/freep-rotated-shape-text-edit-validation.schema.json"
$requiredIds = @("visible-window-discovery", "rotated-editor-entry-and-caret", "rotated-editor-typing-selection-commit", "saved-rotated-shape-package", "escape-cancels-and-preserves-package")

function New-RotatedFixture {
    param([Parameter(Mandatory = $true)][string]$Source, [Parameter(Mandatory = $true)][string]$Destination)
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $destinationDirectory = Split-Path -Parent $Destination
    New-Item -ItemType Directory -Path $destinationDirectory -Force | Out-Null
    $temporary = "$Destination.tmp"
    Remove-Item -LiteralPath $temporary -Force -ErrorAction SilentlyContinue
    $sourceArchive = [IO.Compression.ZipFile]::OpenRead($Source)
    $destinationStream = [IO.File]::Open($temporary, [IO.FileMode]::Create, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
    $destinationArchive = [IO.Compression.ZipArchive]::new($destinationStream, [IO.Compression.ZipArchiveMode]::Create, $false)
    try {
        foreach ($entry in $sourceArchive.Entries) {
            $outputEntry = $destinationArchive.CreateEntry($entry.FullName, [IO.Compression.CompressionLevel]::Optimal)
            $outputStream = $outputEntry.Open()
            try {
                if ($entry.FullName -eq "ppt/slides/slide1.xml") {
                    $inputStream = $entry.Open()
                    try { $reader = [IO.StreamReader]::new($inputStream, [Text.Encoding]::UTF8); $xmlText = $reader.ReadToEnd(); $reader.Dispose() } finally { $inputStream.Dispose() }
                    $slideXml = [xml]$xmlText
                    $ns = [Xml.XmlNamespaceManager]::new($slideXml.NameTable)
                    $ns.AddNamespace("p", "http://schemas.openxmlformats.org/presentationml/2006/main")
                    $ns.AddNamespace("a", "http://schemas.openxmlformats.org/drawingml/2006/main")
                    $shape = $slideXml.SelectSingleNode("//p:sp[p:nvSpPr/p:cNvPr[@id='2']]", $ns)
                    if ($null -eq $shape) { throw "Base fixture has no shape ID 2." }
                    $shape.SelectSingleNode("p:nvSpPr/p:cNvPr", $ns).SetAttribute("name", "Wave61 Rotated Text")
                    $xfrm = $shape.SelectSingleNode("p:spPr/a:xfrm", $ns)
                    if ($null -eq $xfrm) { throw "Shape ID 2 has no transform." }
                    $xfrm.SetAttribute("rot", "1800000")
                    $off = $xfrm.SelectSingleNode("a:off", $ns); $ext = $xfrm.SelectSingleNode("a:ext", $ns)
                    $off.SetAttribute("x", "2857500"); $off.SetAttribute("y", "1428750"); $ext.SetAttribute("cx", "2286000"); $ext.SetAttribute("cy", "1524000")
                    $textNodes = @($shape.SelectNodes(".//a:t", $ns))
                    if ($textNodes.Count -eq 0) { throw "Shape ID 2 has no text run." }
                    $textNodes[0].InnerText = "Rotate me"
                    foreach ($textNode in $textNodes | Select-Object -Skip 1) { $textNode.ParentNode.RemoveChild($textNode) | Out-Null }
                    $settings = [Xml.XmlWriterSettings]::new(); $settings.Encoding = [Text.UTF8Encoding]::new($false); $settings.Indent = $false
                    $memory = [IO.MemoryStream]::new(); $writer = [Xml.XmlWriter]::Create($memory, $settings)
                    try { $slideXml.Save($writer); $writer.Flush(); $bytes = $memory.ToArray() } finally { $writer.Dispose(); $memory.Dispose() }
                    $outputStream.Write($bytes, 0, $bytes.Length)
                } else {
                    $inputStream = $entry.Open()
                    try { $inputStream.CopyTo($outputStream) } finally { $inputStream.Dispose() }
                }
            } finally { $outputStream.Dispose() }
        }
    } finally { $destinationArchive.Dispose(); $destinationStream.Dispose(); $sourceArchive.Dispose() }
    Move-Item -LiteralPath $temporary -Destination $Destination -Force
}

if (-not (Test-Path -LiteralPath $baseFixturePath -PathType Leaf)) { throw "Base fixture missing: $baseFixturePath" }
if (-not (Test-Path -LiteralPath $probeSource -PathType Leaf)) { throw "Probe missing: $probeSource" }
if (-not (Test-Path -LiteralPath $schemaPath -PathType Leaf)) { throw "Schema missing: $schemaPath" }
New-RotatedFixture -Source $baseFixturePath -Destination $fixturePath
New-Item -ItemType Directory -Path $resolvedOutputRoot -Force | Out-Null

$started = $false; $sessionDirectory = $null; $manifestPath = $null; $probeExitCode = 1
try {
    $startArguments = @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", $genericRunner, "-Action", "Start", "-App", "FreeP", "-Port", "$Port", "-Width", "$Width", "-Height", "$Height", "-Dpi", "$Dpi", "-MemoryLimit", $MemoryLimit, "-OutputDir", $resolvedOutputRoot, "-DocumentPath", $fixturePath)
    if (-not [string]::IsNullOrWhiteSpace($PublishDir)) { $startArguments += @("-PublishDir", $PublishDir) }
    if ($SkipPublish) { $startArguments += "-SkipPublish" }; if ($SkipImageBuild) { $startArguments += "-SkipImageBuild" }; if ($Replace) { $startArguments += "-Replace" }
    Invoke-ToolProcess -FilePath "powershell.exe" -Arguments $startArguments -WorkingDirectory $repoRoot; $started = $true
    $session = Get-Content -LiteralPath (Join-Path $resolvedOutputRoot "freep/current-session.json") -Raw | ConvertFrom-Json
    $sessionDirectory = [IO.Path]::GetFullPath([string]$session.sessionDirectory)
    $probeInWork = Join-Path $sessionDirectory "freep-rotated-shape-text-edit-probe.sh"
    Copy-Item -LiteralPath $probeSource -Destination $probeInWork -Force
    $manifestPath = Join-Path $sessionDirectory "freep-rotated-shape-text-edit-validation/results.json"
    $evidenceDirectory = Split-Path -Parent $manifestPath
    New-Item -ItemType Directory -Path $evidenceDirectory -Force | Out-Null
    $dockerArguments = @("exec", "--env", "FREEP_DOCUMENT_PATH=/documents/rotated-shape-text-fixture.pptx", "--env", "FREEP_EXPECTED_DOCUMENT_NAME=rotated-shape-text-fixture.pptx", "--env", "FREEP_EXPECTED_WINDOW_PATTERN=FreeP", "--env", "FREEP_SCREEN_WIDTH=$Width", "--env", "FREEP_SCREEN_HEIGHT=$Height", "--env", "FREEP_SCREEN_DPI=$Dpi", [string]$session.containerName, "bash", "/work/freep-rotated-shape-text-edit-probe.sh", "/work/freep-rotated-shape-text-edit-validation")
    Push-Location $repoRoot; try { $probeOutput = @(& docker @dockerArguments 2>&1); $probeExitCode = $LASTEXITCODE } finally { Pop-Location }
    $probeOutput | Set-Content -LiteralPath (Join-Path $evidenceDirectory "probe.log") -Encoding utf8
    Invoke-ToolProcess -FilePath "docker" -Arguments @("cp", "$($session.containerName):/work/freep-rotated-shape-text-edit-validation/.", $evidenceDirectory) -WorkingDirectory $repoRoot
    if ($started -and -not $KeepContainer) {
        Invoke-ToolProcess -FilePath "powershell.exe" -Arguments @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", $genericRunner, "-Action", "Stop", "-App", "FreeP", "-Port", "$Port", "-OutputDir", $resolvedOutputRoot) -WorkingDirectory $repoRoot
        $started = $false
    }
    Start-Sleep -Seconds 2
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) { throw "Probe did not write manifest: $manifestPath" }
    for ($attempt = 0; $attempt -lt 40; $attempt++) {
        $manifestCandidate = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
        $evidenceNames = @($manifestCandidate.results | ForEach-Object { @($_.evidence) })
        if ($evidenceNames.Count -gt 0 -and (@($evidenceNames | Where-Object { Test-Path -LiteralPath (Join-Path $evidenceDirectory $_) -PathType Leaf }).Count -eq $evidenceNames.Count)) { break }
        Start-Sleep -Milliseconds 250
    }
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    $ids = @($manifest.results | ForEach-Object { [string]$_.id })
    if ($manifest.schemaVersion -ne 1 -or $manifest.suite -ne "freep-linux-rotated-shape-text-edit-physical" -or $manifest.platform -ne "linux" -or $manifest.shell -ne "avalonia" -or $manifest.app -ne "FreeP") { throw "Manifest header failed the Wave 61 schema contract." }
    if ([string]::Join("|", $ids) -ne [string]::Join("|", $requiredIds)) { throw "Manifest result IDs/order failed the Wave 61 contract." }
    $expectedBounds = @{ x = 2857500; y = 1428750; cx = 2286000; cy = 1524000 }
    if ($manifest.fixture.file -ne "rotated-shape-text-fixture.pptx" -or $manifest.fixture.shapeId -ne 2 -or $manifest.fixture.name -ne "Wave61 Rotated Text" -or $manifest.fixture.rotation -ne 30 -or $manifest.fixture.text -ne "Rotate me" -or $manifest.package.savedText -ne "Typed rotated text" -or $manifest.package.rotation -ne 30) { throw "Manifest fixture/package values failed exact text/geometry/rotation contract." }
    foreach ($coordinate in @("x", "y", "cx", "cy")) { if ([int]$manifest.fixture.bounds.$coordinate -ne $expectedBounds[$coordinate] -or [int]$manifest.package.bounds.$coordinate -ne $expectedBounds[$coordinate]) { throw "Manifest bounds failed exact value for $coordinate." } }
    $evidenceFiles = @{}
    foreach ($file in @(Get-ChildItem -LiteralPath $evidenceDirectory -File)) { $evidenceFiles[$file.Name] = [int64]$file.Length }
    foreach ($result in @($manifest.results)) {
        if ($result.status -ne "passed" -or $result.evidenceLevel -ne "physical-x11-input" -or @($result.evidence).Count -lt 1) { throw "Physical result '$($result.id)' did not pass its evidence contract." }
        foreach ($name in @($result.evidence)) { if ([IO.Path]::GetFileName([string]$name) -ne [string]$name -or -not $evidenceFiles.ContainsKey([string]$name) -or $evidenceFiles[[string]$name] -le 0) { throw "Missing evidence '$name' for '$($result.id)'." } }
    }
    $manifest.contractValidation = [ordered]@{ status = "passed"; validator = "tools/Run-FreePRotatedShapeTextEditValidation.ps1"; contractReference = "tools/LinuxInteractiveDocker/freep-rotated-shape-text-edit-validation.schema.json" }
    $manifest | ConvertTo-Json -Depth 16 | Set-Content -LiteralPath $manifestPath -Encoding utf8
    $report = [ordered]@{ suite = $manifest.suite; probeExitCode = $probeExitCode; manifest = $manifestPath; evidenceDirectory = $evidenceDirectory; fixture = $fixturePath; results = $manifest.summary }
    $report | ConvertTo-Json -Depth 16 | Set-Content -LiteralPath (Join-Path $resolvedOutputRoot "wave61-report.json") -Encoding utf8
    Write-Host "Manifest contract validation: passed"; Write-Host "Results: $($manifest.summary.passed) passed, $($manifest.summary.failed) failed, $($manifest.summary.total) total"; Write-Host "Manifest: $manifestPath"; Write-Host "Evidence: $evidenceDirectory"
    if ($probeExitCode -ne 0 -or $manifest.summary.failed -ne 0) { throw "FreeP rotated-shape physical validation failed with probe exit code $probeExitCode." }
} finally {
    if ($started -and -not $KeepContainer) { try { Invoke-ToolProcess -FilePath "powershell.exe" -Arguments @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", $genericRunner, "-Action", "Stop", "-App", "FreeP", "-Port", "$Port", "-OutputDir", $resolvedOutputRoot) -WorkingDirectory $repoRoot } catch { Write-Warning "Could not stop harness-owned FreeP container on port ${Port}: $($_.Exception.Message)" } }
}
