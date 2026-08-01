<#
.SYNOPSIS
    Creates a minimal, deterministic DOCX containing one canonical Word page-border art token.

.DESCRIPTION
    The generated package keeps body content intentionally small so a Word/FreeW comparison isolates
    the page-border perimeter. It is suitable for short-path Word COM export with Render-WordBaseline.ps1.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$OutputPath,
    [Parameter(Mandatory = $true)][ValidatePattern('^[A-Za-z][A-Za-z0-9]*$')][string]$Token,
    [string]$Label = 'Decorative page border probe',
    [ValidateRange(0.125, 31)][double]$WidthPt = 3,
    [ValidateRange(0, 31)][double]$SpacePt = 24
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$output = [IO.Path]::GetFullPath($OutputPath)
$directory = [IO.Path]::GetDirectoryName($output)
[IO.Directory]::CreateDirectory($directory) | Out-Null
if ([IO.File]::Exists($output)) {
    [IO.File]::Delete($output)
}

$size = [int][Math]::Round($WidthPt * 8)
$space = [int][Math]::Round($SpacePt)
$escapedToken = [Security.SecurityElement]::Escape($Token)
$escapedLabel = [Security.SecurityElement]::Escape($Label)
$contentTypes = @'
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
  <Default Extension="xml" ContentType="application/xml"/>
  <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
</Types>
'@
$rootRelationships = @'
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
</Relationships>
'@
$document = @"
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
  <w:body>
    <w:p><w:pPr><w:spacing w:after="240"/></w:pPr><w:r><w:rPr><w:b/><w:sz w:val="28"/></w:rPr><w:t>$escapedLabel</w:t></w:r></w:p>
    <w:p><w:r><w:t>This exact package isolates the decorative page-border raster while retaining a white interior control.</w:t></w:r></w:p>
    <w:sectPr>
      <w:pgSz w:w="12240" w:h="15840"/>
      <w:pgMar w:top="1440" w:right="1440" w:bottom="1440" w:left="1440" w:header="720" w:footer="720" w:gutter="0"/>
      <w:pgBorders w:offsetFrom="page">
        <w:top w:val="$escapedToken" w:sz="$size" w:space="$space" w:color="auto"/>
        <w:left w:val="$escapedToken" w:sz="$size" w:space="$space" w:color="auto"/>
        <w:bottom w:val="$escapedToken" w:sz="$size" w:space="$space" w:color="auto"/>
        <w:right w:val="$escapedToken" w:sz="$size" w:space="$space" w:color="auto"/>
      </w:pgBorders>
    </w:sectPr>
  </w:body>
</w:document>
"@

$stream = [IO.File]::Open($output, [IO.FileMode]::CreateNew)
try {
    $archive = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Create, $false)
    try {
        foreach ($part in @(
            @{ Path = '[Content_Types].xml'; Text = $contentTypes },
            @{ Path = '_rels/.rels'; Text = $rootRelationships },
            @{ Path = 'word/document.xml'; Text = $document }
        )) {
            $entry = $archive.CreateEntry($part.Path)
            $writer = [IO.StreamWriter]::new($entry.Open(), [Text.UTF8Encoding]::new($false))
            try { $writer.Write($part.Text) } finally { $writer.Dispose() }
        }
    }
    finally {
        $archive.Dispose()
    }
}
finally {
    $stream.Dispose()
}

Get-FileHash -Algorithm SHA256 -LiteralPath $output
