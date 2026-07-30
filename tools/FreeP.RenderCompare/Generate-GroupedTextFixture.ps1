[CmdletBinding()]
param(
[Parameter(Mandatory = $true)][string]$Source,
[Parameter(Mandatory = $true)][string]$Destination,
[switch]$CaretGeometry,
[switch]$PointerSelectionGeometry
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.IO.Compression.FileSystem
Add-Type -AssemblyName System.IO.Compression

$sourcePath = [IO.Path]::GetFullPath($Source)
$destinationPath = [IO.Path]::GetFullPath($Destination)
if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) { throw "Source fixture was not found: $sourcePath" }
if ([string]::Equals($sourcePath, $destinationPath, [StringComparison]::OrdinalIgnoreCase)) { throw "Destination must be a copy, not the source fixture." }
if ($CaretGeometry -and $PointerSelectionGeometry) { throw "CaretGeometry and PointerSelectionGeometry are mutually exclusive." }

$destinationDirectory = Split-Path -Parent $destinationPath
New-Item -ItemType Directory -Force -Path $destinationDirectory | Out-Null
Copy-Item -LiteralPath $sourcePath -Destination $destinationPath -Force

$pNs = "http://schemas.openxmlformats.org/presentationml/2006/main"
$aNs = "http://schemas.openxmlformats.org/drawingml/2006/main"
$document = New-Object Xml.XmlDocument
$ns = New-Object Xml.XmlNamespaceManager($document.NameTable)
$ns.AddNamespace("p", $pNs)
$ns.AddNamespace("a", $aNs)

$zip = [IO.Compression.ZipFile]::Open($destinationPath, [IO.Compression.ZipArchiveMode]::Update)
try {
    $entry = $zip.GetEntry("ppt/slides/slide1.xml")
    if ($null -eq $entry) { throw "Slide 1 XML is missing from $destinationPath" }

    $reader = New-Object IO.StreamReader($entry.Open())
    try { $document.LoadXml($reader.ReadToEnd()) } finally { $reader.Dispose() }

    $shapeTree = $document.SelectSingleNode("//p:spTree", $ns)
    $shape = $shapeTree.SelectSingleNode("./p:sp[p:nvSpPr/p:cNvPr[@id='2']]", $ns)
    if ($null -eq $shape) { throw "Shape id 2 was not found as a top-level shape." }

    $group = $document.CreateElement("p", "grpSp", $pNs)
    $nvGrpSpPr = $document.CreateElement("p", "nvGrpSpPr", $pNs)
    $cNvPr = $document.CreateElement("p", "cNvPr", $pNs)
    $cNvPr.SetAttribute("id", "100")
    $cNvPr.SetAttribute("name", "Grouped Notes Marker")
    $nvGrpSpPr.AppendChild($cNvPr) | Out-Null
    $nvGrpSpPr.AppendChild($document.CreateElement("p", "cNvGrpSpPr", $pNs)) | Out-Null
    $nvGrpSpPr.AppendChild($document.CreateElement("p", "nvPr", $pNs)) | Out-Null
    $group.AppendChild($nvGrpSpPr) | Out-Null

    $grpSpPr = $document.CreateElement("p", "grpSpPr", $pNs)
    $xfrm = $document.CreateElement("a", "xfrm", $aNs)
    foreach ($name in @("off", "ext", "chOff", "chExt")) {
        $node = $document.CreateElement("a", $name, $aNs)
        if ($name -in @("off", "chOff")) {
            $node.SetAttribute("x", "0")
            $node.SetAttribute("y", "0")
        } else {
            $node.SetAttribute("cx", "12192000")
            $node.SetAttribute("cy", "6858000")
        }
        $xfrm.AppendChild($node) | Out-Null
    }
    $grpSpPr.AppendChild($xfrm) | Out-Null
    $group.AppendChild($grpSpPr) | Out-Null

    # Keep the grouped-child fixture deliberately rich: two paragraphs and
    # multiple native runs are needed to exercise range formatting across the
    # paragraph boundary in both renderers. The caret variant uses longer,
    # unequal-width paragraphs so physical visual-line movement has a stable
    # native document to exercise.
    $groupedShape = $shape.CloneNode($true)
    if ($CaretGeometry -or $PointerSelectionGeometry) {
        $shapeExt = $groupedShape.SelectSingleNode("./p:spPr/a:xfrm/a:ext", $ns)
        if ($null -eq $shapeExt) { throw "Grouped caret fixture shape extent is missing." }
        $shapeExt.SetAttribute("cy", "2743200")
    }
    $txBody = $groupedShape.SelectSingleNode("./p:txBody", $ns)
    $paragraph = $txBody.SelectSingleNode("./a:p", $ns)
    $runs = @($paragraph.SelectNodes("./a:r", $ns))
    foreach ($run in $runs) { $paragraph.RemoveChild($run) | Out-Null }

    function New-TextRun([string]$value) {
        $run = $document.CreateElement("a", "r", $aNs)
        $run.AppendChild($document.CreateElement("a", "rPr", $aNs)) | Out-Null
        $text = $document.CreateElement("a", "t", $aNs)
        $text.InnerText = $value
        $run.AppendChild($text) | Out-Null
        return $run
    }

    if ($CaretGeometry) {
        $paragraph.AppendChild((New-TextRun "Alpha bravo charlie ")) | Out-Null
        $paragraph.AppendChild((New-TextRun "delta echo foxtrot golf hotel")) | Out-Null
    }
    elseif ($PointerSelectionGeometry) {
        $paragraph.AppendChild((New-TextRun "Wide words make this first paragraph wrap at unequal visual line widths")) | Out-Null
    }
    else {
        $paragraph.AppendChild((New-TextRun "Slide 1")) | Out-Null
        $paragraph.AppendChild((New-TextRun " has")) | Out-Null
    }
    $secondParagraph = $document.CreateElement("a", "p", $aNs)
    if ($CaretGeometry) {
        $secondParagraph.AppendChild((New-TextRun "tiny middle wide lower ")) | Out-Null
        $secondParagraph.AppendChild((New-TextRun "line with many words for contrast")) | Out-Null
    }
    elseif ($PointerSelectionGeometry) {
        $secondParagraph.AppendChild((New-TextRun "tail paragraph crosses the boundary")) | Out-Null
    }
    else {
        $secondParagraph.AppendChild((New-TextRun " speaker")) | Out-Null
        $secondParagraph.AppendChild((New-TextRun " notes")) | Out-Null
    }
    $txBody.AppendChild($secondParagraph) | Out-Null

    $group.AppendChild($groupedShape) | Out-Null
    $shapeTree.ReplaceChild($group, $shape) | Out-Null

    $settings = New-Object Xml.XmlWriterSettings
    $settings.Encoding = New-Object Text.UTF8Encoding($false)
    $settings.Indent = $false
    $stream = New-Object IO.MemoryStream
    $writer = [Xml.XmlWriter]::Create($stream, $settings)
    try { $document.Save($writer) } finally { $writer.Dispose() }
    $bytes = $stream.ToArray()
    $stream.Dispose()

    $entry.Delete()
    $replacement = $zip.CreateEntry("ppt/slides/slide1.xml")
    $output = $replacement.Open()
    try { $output.Write($bytes, 0, $bytes.Length) } finally { $output.Dispose() }
}
finally {
    $zip.Dispose()
}

Write-Output $destinationPath
