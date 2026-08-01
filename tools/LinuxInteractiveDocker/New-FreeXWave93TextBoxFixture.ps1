[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$OutputPath
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$sourcePath = Join-Path $repoRoot "tests/FreeX.Core.IO.Tests/Fixtures/Excel_native_shapes_fill_outline_004.xlsx"
$resolvedOutput = [IO.Path]::GetFullPath($OutputPath)
$outputDirectory = Split-Path -Parent $resolvedOutput
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
Copy-Item -LiteralPath $sourcePath -Destination $resolvedOutput -Force

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::Open($resolvedOutput, [IO.Compression.ZipArchiveMode]::Update)
try {
    $entry = $archive.GetEntry("xl/drawings/drawing1.xml")
    if ($null -eq $entry) { throw "The deterministic drawing fixture has no worksheet drawing part." }

    $reader = [IO.StreamReader]::new($entry.Open())
    try { [xml]$xml = $reader.ReadToEnd() } finally { $reader.Dispose() }

    $nsmgr = [Xml.XmlNamespaceManager]::new($xml.NameTable)
    $nsmgr.AddNamespace("xdr", "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing")
    $nsmgr.AddNamespace("a", "http://schemas.openxmlformats.org/drawingml/2006/main")
    $shape = $xml.SelectSingleNode("/xdr:wsDr/xdr:twoCellAnchor/xdr:sp", $nsmgr)
    if ($null -eq $shape) { throw "The deterministic drawing fixture has no shape anchor." }

    $shape.SelectSingleNode("xdr:nvSpPr/xdr:cNvPr", $nsmgr).SetAttribute("name", "Wave93 Physical TextBox")
    $nonVisual = $shape.SelectSingleNode("xdr:nvSpPr/xdr:cNvSpPr", $nsmgr)
    $nonVisual.SetAttribute("txBox", "1")
    $textBody = $shape.SelectSingleNode("xdr:txBody", $nsmgr)
    $paragraph = $textBody.SelectSingleNode("a:p", $nsmgr)
    foreach ($child in @($paragraph.ChildNodes | Where-Object { $_.LocalName -in @("r", "fld") })) {
        [void]$paragraph.RemoveChild($child)
    }
    $run = $xml.CreateElement("a", "r", "http://schemas.openxmlformats.org/drawingml/2006/main")
    $runProperties = $xml.CreateElement("a", "rPr", "http://schemas.openxmlformats.org/drawingml/2006/main")
    $runProperties.SetAttribute("lang", "en-US")
    $runProperties.SetAttribute("sz", "1200")
    $text = $xml.CreateElement("a", "t", "http://schemas.openxmlformats.org/drawingml/2006/main")
    $text.InnerText = "Wave93 initial text"
    [void]$run.AppendChild($runProperties)
    [void]$run.AppendChild($text)
    $endRunProperties = $paragraph.SelectSingleNode("a:endParaRPr", $nsmgr)
    if ($null -ne $endRunProperties) { [void]$paragraph.InsertBefore($run, $endRunProperties) }
    else { [void]$paragraph.AppendChild($run) }

    $entry.Delete()
    $replacement = $archive.CreateEntry("xl/drawings/drawing1.xml", [IO.Compression.CompressionLevel]::Optimal)
    $writer = [IO.StreamWriter]::new($replacement.Open(), [Text.UTF8Encoding]::new($false))
    try { $xml.Save($writer) } finally { $writer.Dispose() }
}
finally {
    $archive.Dispose()
}

Write-Host "Created deterministic FreeX Wave 93 text-box fixture: $resolvedOutput"
