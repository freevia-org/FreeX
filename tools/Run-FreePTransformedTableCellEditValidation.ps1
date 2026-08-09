<##
.SYNOPSIS
  Runs the FreeP Wave 62 transformed table-cell inline-edit X11 lane.
#>
[CmdletBinding()]
param(
    [ValidateRange(1024, 65535)][int]$Port = 6098,
    [ValidateRange(640, 7680)][int]$Width = 1280,
    [ValidateRange(480, 4320)][int]$Height = 820,
    [ValidateRange(72, 240)][int]$Dpi = 96,
    [ValidateSet("2g", "4g", "6g", "8g")][string]$MemoryLimit = "4g",
    [string]$OutputDir = "artifacts/freep-transformed-table-cell-edit-wave62",
    [string]$PublishDir = "",
    [switch]$SkipPublish,
    [switch]$SkipImageBuild,
    [switch]$Replace,
    [switch]$KeepContainer
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot "VisualEvidenceScriptSupport.ps1")
$resolvedOutputRoot = Resolve-VisualEvidenceOutputDirectory -OutputDirectory $OutputDir -RepoRoot $repoRoot
$fixturePath = Join-Path $resolvedOutputRoot "transformed-table-cell-fixture.pptx"
$baseFixturePath = Join-Path $repoRoot "tools/FreeP.RenderCompare/corpus/05-table.pptx"
$genericRunner = Join-Path $PSScriptRoot "Run-LinuxInteractiveDocker.ps1"
$probeSource = Join-Path $PSScriptRoot "LinuxInteractiveDocker/run-freep-transformed-table-cell-edit.sh"
$schemaPath = Join-Path $PSScriptRoot "LinuxInteractiveDocker/freep-transformed-table-cell-edit-validation.schema.json"
$requiredIds = @("visible-window-discovery", "transformed-editor-entry-and-caret", "transformed-editor-typing-selection-commit", "saved-transformed-table-package", "escape-cancels-and-preserves-package")

function New-TransformedTableFixture {
    param([Parameter(Mandatory = $true)][string]$Source, [Parameter(Mandatory = $true)][string]$Destination)
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    New-Item -ItemType Directory -Path (Split-Path -Parent $Destination) -Force | Out-Null
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
                    $frame = $slideXml.SelectSingleNode("//p:graphicFrame[p:nvGraphicFramePr/p:cNvPr[@id='2']]", $ns)
                    if ($null -eq $frame) { throw "Base table fixture has no graphic frame ID 2." }
                    $metadata = $frame.SelectSingleNode("p:nvGraphicFramePr/p:cNvPr", $ns)
                    $metadata.SetAttribute("name", "Wave62 Transformed Table")
                    $xfrm = $frame.SelectSingleNode("p:xfrm", $ns)
                    if ($null -eq $xfrm) { throw "Table frame ID 2 has no transform." }
                    $xfrm.SetAttribute("rot", "1800000"); $xfrm.SetAttribute("flipH", "1"); $xfrm.SetAttribute("flipV", "1")
                    $off = $xfrm.SelectSingleNode("a:off", $ns); $ext = $xfrm.SelectSingleNode("a:ext", $ns)
                    $off.SetAttribute("x", "1651000"); $off.SetAttribute("y", "1778000"); $ext.SetAttribute("cx", "8890000"); $ext.SetAttribute("cy", "3302000")
                    $firstText = $frame.SelectSingleNode(".//a:t", $ns)
                    if ($null -eq $firstText) { throw "Table fixture has no editable text." }
                    $firstText.InnerText = "Rotate me"
                    $settings = [Xml.XmlWriterSettings]::new(); $settings.Encoding = [Text.UTF8Encoding]::new($false); $settings.Indent = $false
                    $memory = [IO.MemoryStream]::new(); $writer = [Xml.XmlWriter]::Create($memory, $settings)
                    try { $slideXml.Save($writer); $writer.Flush(); $bytes = $memory.ToArray() } finally { $writer.Dispose(); $memory.Dispose() }
                    $outputStream.Write($bytes, 0, $bytes.Length)
                } else {
                    $inputStream = $entry.Open(); try { $inputStream.CopyTo($outputStream) } finally { $inputStream.Dispose() }
                }
            } finally { $outputStream.Dispose() }
        }
    } finally { $destinationArchive.Dispose(); $destinationStream.Dispose(); $sourceArchive.Dispose() }
    Move-Item -LiteralPath $temporary -Destination $Destination -Force
}

if (-not (Test-Path -LiteralPath $baseFixturePath -PathType Leaf)) { throw "Base fixture missing: $baseFixturePath" }
if (-not (Test-Path -LiteralPath $probeSource -PathType Leaf)) { throw "Probe missing: $probeSource" }
if (-not (Test-Path -LiteralPath $schemaPath -PathType Leaf)) { throw "Schema missing: $schemaPath" }
New-TransformedTableFixture -Source $baseFixturePath -Destination $fixturePath
New-Item -ItemType Directory -Path $resolvedOutputRoot -Force | Out-Null

$started = $false; $sessionDirectory = $null; $manifestPath = $null; $probeExitCode = 1
try {
    $startArguments = @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", $genericRunner, "-Action", "Start", "-App", "FreeP", "-Port", "$Port", "-Width", "$Width", "-Height", "$Height", "-Dpi", "$Dpi", "-MemoryLimit", $MemoryLimit, "-OutputDir", $resolvedOutputRoot, "-DocumentPath", $fixturePath)
    if (-not [string]::IsNullOrWhiteSpace($PublishDir)) { $startArguments += @("-PublishDir", $PublishDir) }
    if ($SkipPublish) { $startArguments += "-SkipPublish" }; if ($SkipImageBuild) { $startArguments += "-SkipImageBuild" }; if ($Replace) { $startArguments += "-Replace" }
    Invoke-VisualEvidenceProcess -FilePath "powershell.exe" -Arguments $startArguments -WorkingDirectory $repoRoot; $started = $true
    $session = Get-Content -LiteralPath (Join-Path $resolvedOutputRoot "freep/current-session.json") -Raw | ConvertFrom-Json
    $sessionDirectory = [IO.Path]::GetFullPath([string]$session.sessionDirectory)
    $probeInWork = Join-Path $sessionDirectory "freep-transformed-table-cell-edit-probe.sh"
    Copy-Item -LiteralPath $probeSource -Destination $probeInWork -Force
    $manifestPath = Join-Path $sessionDirectory "freep-transformed-table-cell-edit-validation/results.json"
    $evidenceDirectory = Split-Path -Parent $manifestPath
    New-Item -ItemType Directory -Path $evidenceDirectory -Force | Out-Null
    $dockerArguments = @("exec", "--env", "FREEP_DOCUMENT_PATH=/documents/transformed-table-cell-fixture.pptx", "--env", "FREEP_EXPECTED_DOCUMENT_NAME=transformed-table-cell-fixture.pptx", "--env", "FREEP_EXPECTED_WINDOW_PATTERN=FreeP", "--env", "FREEP_SCREEN_WIDTH=$Width", "--env", "FREEP_SCREEN_HEIGHT=$Height", "--env", "FREEP_SCREEN_DPI=$Dpi", [string]$session.containerName, "bash", "/work/freep-transformed-table-cell-edit-probe.sh", "/work/freep-transformed-table-cell-edit-validation")
    Push-Location $repoRoot; try { $probeOutput = @(& docker @dockerArguments 2>&1); $probeExitCode = $LASTEXITCODE } finally { Pop-Location }
    $probeOutput | Set-Content -LiteralPath (Join-Path $evidenceDirectory "probe.log") -Encoding utf8
    Invoke-VisualEvidenceProcess -FilePath "docker" -Arguments @("cp", "$($session.containerName):/work/freep-transformed-table-cell-edit-validation/.", $evidenceDirectory) -WorkingDirectory $repoRoot
    if ($started -and -not $KeepContainer) { Invoke-VisualEvidenceProcess -FilePath "powershell.exe" -Arguments @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", $genericRunner, "-Action", "Stop", "-App", "FreeP", "-Port", "$Port", "-OutputDir", $resolvedOutputRoot) -WorkingDirectory $repoRoot; $started = $false }
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) { throw "Probe did not write manifest: $manifestPath" }
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    if ($manifest.schemaVersion -ne 1 -or $manifest.suite -ne "freep-linux-transformed-table-cell-edit-physical" -or $manifest.platform -ne "linux" -or $manifest.shell -ne "avalonia" -or $manifest.app -ne "FreeP") { throw "Manifest header failed the Wave 62 schema contract." }
    $ids = @($manifest.results | ForEach-Object { [string]$_.id })
    if ([string]::Join("|", $ids) -ne [string]::Join("|", $requiredIds)) { throw "Manifest result IDs/order failed the Wave 62 contract." }
    if ($manifest.fixture.file -ne "transformed-table-cell-fixture.pptx" -or $manifest.fixture.shapeId -ne 2 -or $manifest.fixture.name -ne "Wave62 Transformed Table" -or $manifest.fixture.rotation -ne 30 -or -not $manifest.fixture.flipH -or -not $manifest.fixture.flipV -or $manifest.package.savedText -ne "Typed transformed cell text") { throw "Manifest fixture/package values failed the exact text/geometry/rotation/flip contract." }
    foreach ($result in @($manifest.results)) { if ($result.status -ne "passed" -or $result.evidenceLevel -ne "physical-x11-input") { throw "Physical result '$($result.id)' did not pass its evidence contract." } }
    $manifest.contractValidation = [ordered]@{ status = "passed"; validator = "tools/Run-FreePTransformedTableCellEditValidation.ps1"; contractReference = "tools/LinuxInteractiveDocker/freep-transformed-table-cell-edit-validation.schema.json" }
    $manifest | ConvertTo-Json -Depth 16 | Set-Content -LiteralPath $manifestPath -Encoding utf8
    $report = [ordered]@{ suite = $manifest.suite; probeExitCode = $probeExitCode; manifest = $manifestPath; evidenceDirectory = $evidenceDirectory; fixture = $fixturePath; results = $manifest.summary }
    $report | ConvertTo-Json -Depth 16 | Set-Content -LiteralPath (Join-Path $resolvedOutputRoot "wave62-report.json") -Encoding utf8
    Write-Host "Manifest contract validation: passed"; Write-Host "Results: $($manifest.summary.passed) passed, $($manifest.summary.failed) failed, $($manifest.summary.total) total"; Write-Host "Manifest: $manifestPath"; Write-Host "Evidence: $evidenceDirectory"
    if ($probeExitCode -ne 0 -or $manifest.summary.failed -ne 0) { throw "FreeP transformed table-cell physical validation failed with probe exit code $probeExitCode." }
} finally {
    if ($started -and -not $KeepContainer) { try { Invoke-VisualEvidenceProcess -FilePath "powershell.exe" -Arguments @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", $genericRunner, "-Action", "Stop", "-App", "FreeP", "-Port", "$Port", "-OutputDir", $resolvedOutputRoot) -WorkingDirectory $repoRoot } catch { Write-Warning "Could not stop harness-owned FreeP container on port ${Port}: $($_.Exception.Message)" } }
}
