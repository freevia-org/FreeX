<#
.SYNOPSIS
    Generates the three sample .docx files used by the visual-fidelity harness validation run.
    These are minimal hand-rolled OOXML packages; they do NOT require Word to be installed.

.DESCRIPTION
    Outputs to freew-fidelity-corpus/files/:
      tables-styled.docx   — table with TableNormal style, per-cell borders, and header-row shading
      text-sections.docx   — Heading 1/2/3 paragraphs, a page-break (section separator), and body text
      picture-inline.docx  — a small inline image (1x1 red PNG embedded as media/img1.png)

.EXAMPLE
    pwsh freew-fidelity-corpus/tools/New-SampleCorpus.ps1
#>
param(
    [string]$OutDir
)

$ErrorActionPreference = 'Stop'
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
if (-not $OutDir) { $OutDir = Join-Path $scriptDir '../files' }
$OutDir = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutDir)
$null = New-Item -ItemType Directory -Force $OutDir

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

function Write-Docx([string]$path, [hashtable]$parts) {
    if (Test-Path $path) { Remove-Item $path -Force }
    $stream = [System.IO.File]::Open($path, 'Create')
    try {
        $zip = [System.IO.Compression.ZipArchive]::new($stream, 'Create', $false)
        try {
            foreach ($kv in $parts.GetEnumerator()) {
                $entry = $zip.CreateEntry($kv.Key)
                $w = $entry.Open()
                try {
                    $bytes = [System.Text.Encoding]::UTF8.GetBytes($kv.Value)
                    $w.Write($bytes, 0, $bytes.Length)
                } finally { $w.Dispose() }
            }
        } finally { $zip.Dispose() }
    } finally { $stream.Dispose() }
}

function Write-DocxBinary([string]$path, [hashtable]$textParts, [hashtable]$binaryParts) {
    if (Test-Path $path) { Remove-Item $path -Force }
    $stream = [System.IO.File]::Open($path, 'Create')
    try {
        $zip = [System.IO.Compression.ZipArchive]::new($stream, 'Create', $false)
        try {
            foreach ($kv in $textParts.GetEnumerator()) {
                $entry = $zip.CreateEntry($kv.Key)
                $w = $entry.Open()
                try {
                    $bytes = [System.Text.Encoding]::UTF8.GetBytes($kv.Value)
                    $w.Write($bytes, 0, $bytes.Length)
                } finally { $w.Dispose() }
            }
            foreach ($kv in $binaryParts.GetEnumerator()) {
                $entry = $zip.CreateEntry($kv.Key)
                $w = $entry.Open()
                try { $w.Write($kv.Value, 0, $kv.Value.Length) }
                finally { $w.Dispose() }
            }
        } finally { $zip.Dispose() }
    } finally { $stream.Dispose() }
}

# ---- shared XML fragments -----------------------------------------------
$contentTypesBase = @'
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
  <Default Extension="xml"  ContentType="application/xml"/>
  <Override PartName="/word/document.xml"
    ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
  <Override PartName="/word/styles.xml"
    ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml"/>
  <Override PartName="/word/settings.xml"
    ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.settings+xml"/>
</Types>
'@

$relsRoot = @'
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
  <Relationship Id="rId1"
    Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument"
    Target="word/document.xml"/>
</Relationships>
'@

$wordRelsBase = @'
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
  <Relationship Id="rId1"
    Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles"
    Target="styles.xml"/>
  <Relationship Id="rId2"
    Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/settings"
    Target="settings.xml"/>
</Relationships>
'@

$settings = @'
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<w:settings xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
  <w:defaultTabStop w:val="720"/>
</w:settings>
'@

# Minimal styles: Normal, Heading1, Heading2, Heading3, TableNormal
$styles = @'
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<w:styles xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"
          xmlns:w14="http://schemas.microsoft.com/office/word/2010/wordml">
  <w:docDefaults>
    <w:rPrDefault>
      <w:rPr>
        <w:rFonts w:ascii="Calibri" w:hAnsi="Calibri"/>
        <w:sz w:val="24"/><w:szCs w:val="24"/>
      </w:rPr>
    </w:rPrDefault>
  </w:docDefaults>

  <w:style w:type="paragraph" w:default="1" w:styleId="Normal">
    <w:name w:val="Normal"/>
    <w:pPr><w:spacing w:after="160" w:line="276" w:lineRule="auto"/></w:pPr>
  </w:style>

  <w:style w:type="paragraph" w:styleId="Heading1">
    <w:name w:val="heading 1"/>
    <w:basedOn w:val="Normal"/>
    <w:pPr>
      <w:outlineLvl w:val="0"/>
      <w:spacing w:before="240" w:after="0"/>
    </w:pPr>
    <w:rPr>
      <w:b/><w:color w:val="2E74B5"/><w:sz w:val="32"/><w:szCs w:val="32"/>
    </w:rPr>
  </w:style>

  <w:style w:type="paragraph" w:styleId="Heading2">
    <w:name w:val="heading 2"/>
    <w:basedOn w:val="Normal"/>
    <w:pPr>
      <w:outlineLvl w:val="1"/>
      <w:spacing w:before="200" w:after="0"/>
    </w:pPr>
    <w:rPr>
      <w:b/><w:color w:val="2E74B5"/><w:sz w:val="26"/><w:szCs w:val="26"/>
    </w:rPr>
  </w:style>

  <w:style w:type="paragraph" w:styleId="Heading3">
    <w:name w:val="heading 3"/>
    <w:basedOn w:val="Normal"/>
    <w:pPr>
      <w:outlineLvl w:val="2"/>
      <w:spacing w:before="160" w:after="0"/>
    </w:pPr>
    <w:rPr>
      <w:b/><w:color w:val="1F3864"/><w:sz w:val="24"/>
    </w:rPr>
  </w:style>

  <w:style w:type="table" w:styleId="TableNormal" w:default="1">
    <w:name w:val="Normal Table"/>
    <w:tblPr>
      <w:tblCellMar>
        <w:top w:w="0" w:type="dxa"/><w:left w:w="108" w:type="dxa"/>
        <w:bottom w:w="0" w:type="dxa"/><w:right w:w="108" w:type="dxa"/>
      </w:tblCellMar>
    </w:tblPr>
  </w:style>
</w:styles>
'@

# =====================================================================
# 1. tables-styled.docx
#    A 3x3 table: header row (blue shading, white bold text) + 2 data rows.
#    Per-cell single borders. Table style = TableNormal.
# =====================================================================
$tableDoc = @'
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
<w:body>
  <w:p><w:r><w:t>Styled Table Sample</w:t></w:r></w:p>

  <w:tbl>
    <w:tblPr>
      <w:tblStyle w:val="TableNormal"/>
      <w:tblW w:w="5000" w:type="pct"/>
      <w:tblBorders>
        <w:top    w:val="single" w:sz="8" w:space="0" w:color="2E74B5"/>
        <w:left   w:val="single" w:sz="8" w:space="0" w:color="2E74B5"/>
        <w:bottom w:val="single" w:sz="8" w:space="0" w:color="2E74B5"/>
        <w:right  w:val="single" w:sz="8" w:space="0" w:color="2E74B5"/>
        <w:insideH w:val="single" w:sz="4" w:space="0" w:color="9DC3E6"/>
        <w:insideV w:val="single" w:sz="4" w:space="0" w:color="9DC3E6"/>
      </w:tblBorders>
    </w:tblPr>
    <w:tblGrid>
      <w:gridCol w:w="2000"/>
      <w:gridCol w:w="2000"/>
      <w:gridCol w:w="2000"/>
    </w:tblGrid>

    <!-- Header row -->
    <w:tr>
      <w:trPr><w:tblHeader/></w:trPr>
      <w:tc>
        <w:tcPr><w:shd w:val="clear" w:color="auto" w:fill="2E74B5"/></w:tcPr>
        <w:p><w:pPr><w:jc w:val="center"/></w:pPr>
           <w:r><w:rPr><w:b/><w:color w:val="FFFFFF"/></w:rPr><w:t>Product</w:t></w:r></w:p>
      </w:tc>
      <w:tc>
        <w:tcPr><w:shd w:val="clear" w:color="auto" w:fill="2E74B5"/></w:tcPr>
        <w:p><w:pPr><w:jc w:val="center"/></w:pPr>
           <w:r><w:rPr><w:b/><w:color w:val="FFFFFF"/></w:rPr><w:t>Qty</w:t></w:r></w:p>
      </w:tc>
      <w:tc>
        <w:tcPr><w:shd w:val="clear" w:color="auto" w:fill="2E74B5"/></w:tcPr>
        <w:p><w:pPr><w:jc w:val="center"/></w:pPr>
           <w:r><w:rPr><w:b/><w:color w:val="FFFFFF"/></w:rPr><w:t>Price</w:t></w:r></w:p>
      </w:tc>
    </w:tr>

    <!-- Data row 1 (light shading) -->
    <w:tr>
      <w:tc>
        <w:tcPr><w:shd w:val="clear" w:color="auto" w:fill="DEEAF1"/></w:tcPr>
        <w:p><w:r><w:t>Widget A</w:t></w:r></w:p>
      </w:tc>
      <w:tc>
        <w:tcPr><w:shd w:val="clear" w:color="auto" w:fill="DEEAF1"/></w:tcPr>
        <w:p><w:pPr><w:jc w:val="center"/></w:pPr><w:r><w:t>12</w:t></w:r></w:p>
      </w:tc>
      <w:tc>
        <w:tcPr><w:shd w:val="clear" w:color="auto" w:fill="DEEAF1"/></w:tcPr>
        <w:p><w:pPr><w:jc w:val="right"/></w:pPr><w:r><w:t>$9.99</w:t></w:r></w:p>
      </w:tc>
    </w:tr>

    <!-- Data row 2 (white) -->
    <w:tr>
      <w:tc><w:p><w:r><w:t>Gadget B</w:t></w:r></w:p></w:tc>
      <w:tc><w:p><w:pPr><w:jc w:val="center"/></w:pPr><w:r><w:t>5</w:t></w:r></w:p></w:tc>
      <w:tc><w:p><w:pPr><w:jc w:val="right"/></w:pPr><w:r><w:t>$24.50</w:t></w:r></w:p></w:tc>
    </w:tr>
  </w:tbl>

  <w:p/>
  <w:sectPr>
    <w:pgSz w:w="12240" w:h="15840"/>
    <w:pgMar w:top="1440" w:right="1440" w:bottom="1440" w:left="1440"/>
  </w:sectPr>
</w:body>
</w:document>
'@

Write-Docx (Join-Path $OutDir 'tables-styled.docx') @{
    '[Content_Types].xml'  = $contentTypesBase
    '_rels/.rels'          = $relsRoot
    'word/_rels/document.xml.rels' = $wordRelsBase
    'word/document.xml'    = $tableDoc
    'word/styles.xml'      = $styles
    'word/settings.xml'    = $settings
}
Write-Host "  created: tables-styled.docx"

# =====================================================================
# 2. text-sections.docx
#    Heading 1/2/3 + body paragraphs + explicit page break to exercise
#    multi-section/heading rendering.
# =====================================================================
$textDoc = @'
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
<w:body>

  <w:p>
    <w:pPr><w:pStyle w:val="Heading1"/></w:pPr>
    <w:r><w:t>Document Title — Section One</w:t></w:r>
  </w:p>

  <w:p>
    <w:pPr><w:pStyle w:val="Heading2"/></w:pPr>
    <w:r><w:t>Subsection 1.1</w:t></w:r>
  </w:p>

  <w:p>
    <w:r><w:t xml:space="preserve">This is the body text of subsection 1.1.  FreeW renders Heading styles
using the theme accent colour defined in styles.xml (blue, #2E74B5 for H1/H2
and dark navy #1F3864 for H3).  Body paragraphs use Calibri 12 pt with
1.15-line spacing and 8 pt spacing after.</w:t></w:r>
  </w:p>

  <w:p>
    <w:pPr><w:pStyle w:val="Heading3"/></w:pPr>
    <w:r><w:t>Sub-subsection 1.1.1</w:t></w:r>
  </w:p>

  <w:p>
    <w:r><w:rPr><w:b/></w:rPr><w:t xml:space="preserve">Bold lead-in: </w:t></w:r>
    <w:r><w:t xml:space="preserve">Normal continuation text with </w:t></w:r>
    <w:r><w:rPr><w:i/></w:rPr><w:t>italic</w:t></w:r>
    <w:r><w:t xml:space="preserve"> and </w:t></w:r>
    <w:r><w:rPr><w:u w:val="single"/></w:rPr><w:t>underline</w:t></w:r>
    <w:r><w:t xml:space="preserve"> character formatting to exercise run-level properties.</w:t></w:r>
  </w:p>

  <!-- Explicit page break -->
  <w:p>
    <w:r><w:br w:type="page"/></w:r>
  </w:p>

  <w:p>
    <w:pPr><w:pStyle w:val="Heading1"/></w:pPr>
    <w:r><w:t>Section Two — After Page Break</w:t></w:r>
  </w:p>

  <w:p>
    <w:r><w:t xml:space="preserve">This paragraph appears on page 2.  The page break above is
an explicit w:br of type page, which FreeW should honour by starting a new
page in its paginator.  Word will also honour it, so the baseline and FreeW
renders should agree that content starts here.</w:t></w:r>
  </w:p>

  <w:p>
    <w:pPr><w:pStyle w:val="Heading2"/></w:pPr>
    <w:r><w:t>Subsection 2.1</w:t></w:r>
  </w:p>

  <w:p>
    <w:r><w:t>Final body paragraph on page 2.</w:t></w:r>
  </w:p>

  <w:sectPr>
    <w:pgSz w:w="12240" w:h="15840"/>
    <w:pgMar w:top="1440" w:right="1440" w:bottom="1440" w:left="1440"/>
  </w:sectPr>
</w:body>
</w:document>
'@

Write-Docx (Join-Path $OutDir 'text-sections.docx') @{
    '[Content_Types].xml'  = $contentTypesBase
    '_rels/.rels'          = $relsRoot
    'word/_rels/document.xml.rels' = $wordRelsBase
    'word/document.xml'    = $textDoc
    'word/styles.xml'      = $styles
    'word/settings.xml'    = $settings
}
Write-Host "  created: text-sections.docx"

# =====================================================================
# 3. picture-inline.docx
#    An inline image (a small red square PNG, 16x16 px, generated in memory).
#    Exercises the image-embedding path in FreeW.Core.IO + WPF image rendering.
# =====================================================================

# Minimal 16x16 solid-red PNG (1-bit palette PNG would be tinier but BMP-in-PNG
# header is tricky to hand-roll; instead we embed a hard-coded DEFLATE-compressed
# true-color PNG byte array).  Generated offline and base64-encoded here.
# This is a valid 16x16 RGBA PNG with every pixel = FF0000FF (opaque red).
$redPngB64 = 'iVBORw0KGgoAAAANSUhEUgAAABAAAAAQCAYAAAAf8/9hAAAAFElEQVR42mP8z8BQDwADhQGAWjR9awAAAABJRU5ErkJggg=='
$redPngBytes = [System.Convert]::FromBase64String($redPngB64)

# Content-Types with image/png override
$ctWithImage = @'
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
  <Default Extension="xml"  ContentType="application/xml"/>
  <Default Extension="png"  ContentType="image/png"/>
  <Override PartName="/word/document.xml"
    ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
  <Override PartName="/word/styles.xml"
    ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml"/>
  <Override PartName="/word/settings.xml"
    ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.settings+xml"/>
</Types>
'@

$wordRelsWithImage = @'
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
  <Relationship Id="rId1"
    Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles"
    Target="styles.xml"/>
  <Relationship Id="rId2"
    Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/settings"
    Target="settings.xml"/>
  <Relationship Id="rId3"
    Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/image"
    Target="media/img1.png"/>
</Relationships>
'@

# DrawingML inline image — 16x16 px at 96dpi = 152400 EMU per inch; 16px = 152400*16/96 = 25400 EMU
$picDoc = @'
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"
            xmlns:wp="http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing"
            xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main"
            xmlns:pic="http://schemas.openxmlformats.org/drawingml/2006/picture"
            xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
<w:body>
  <w:p><w:r><w:t>Document with an inline image (16&#xd7;16 px red square):</w:t></w:r></w:p>

  <w:p>
    <w:r>
      <w:drawing>
        <wp:inline distT="0" distB="0" distL="0" distR="0">
          <wp:extent cx="152400" cy="152400"/>
          <wp:effectExtent l="0" t="0" r="0" b="0"/>
          <wp:docPr id="1" name="Image1" descr="Red square"/>
          <wp:cNvGraphicFramePr>
            <a:graphicFrameLocks xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" noChangeAspect="1"/>
          </wp:cNvGraphicFramePr>
          <a:graphic>
            <a:graphicData uri="http://schemas.openxmlformats.org/drawingml/2006/picture">
              <pic:pic>
                <pic:nvPicPr>
                  <pic:cNvPr id="1" name="img1.png"/>
                  <pic:cNvPicPr/>
                </pic:nvPicPr>
                <pic:blipFill>
                  <a:blip r:embed="rId3"/>
                  <a:stretch><a:fillRect/></a:stretch>
                </pic:blipFill>
                <pic:spPr>
                  <a:xfrm><a:off x="0" y="0"/><a:ext cx="152400" cy="152400"/></a:xfrm>
                  <a:prstGeom prst="rect"><a:avLst/></a:prstGeom>
                  <a:ln w="19050">
                    <a:solidFill><a:srgbClr val="000000"/></a:solidFill>
                  </a:ln>
                </pic:spPr>
              </pic:pic>
            </a:graphicData>
          </a:graphic>
        </wp:inline>
      </w:drawing>
    </w:r>
  </w:p>

  <w:p>
    <w:r><w:t xml:space="preserve">The image above is a 16x16 px solid-red PNG embedded as word/media/img1.png.
This exercises FreeW's image loading + inline DrawingML rendering path.</w:t></w:r>
  </w:p>

  <w:sectPr>
    <w:pgSz w:w="12240" w:h="15840"/>
    <w:pgMar w:top="1440" w:right="1440" w:bottom="1440" w:left="1440"/>
  </w:sectPr>
</w:body>
</w:document>
'@

Write-DocxBinary (Join-Path $OutDir 'picture-inline.docx') @{
    '[Content_Types].xml'          = $ctWithImage
    '_rels/.rels'                  = $relsRoot
    'word/_rels/document.xml.rels' = $wordRelsWithImage
    'word/document.xml'            = $picDoc
    'word/styles.xml'              = $styles
    'word/settings.xml'            = $settings
} @{
    'word/media/img1.png' = $redPngBytes
}
Write-Host "  created: picture-inline.docx"

Write-Host ""
Write-Host "Sample corpus written to: $OutDir"
