Set-StrictMode -Version Latest

$script:RichDataRoot = "xl/richData/"
$script:RelationshipNamespace = "http://schemas.openxmlformats.org/officeDocument/2006/relationships"

function Get-UxParityPackagePath {
    param([Parameter(Mandatory = $true)][string]$Path)

    $segments = New-Object System.Collections.Generic.List[string]
    foreach ($segment in (($Path -replace '\\', '/') -split '/')) {
        if ([string]::IsNullOrWhiteSpace($segment) -or $segment -eq ".") {
            continue
        }

        if ($segment -eq "..") {
            if ($segments.Count -gt 0) {
                $segments.RemoveAt($segments.Count - 1)
            }
            continue
        }

        $segments.Add($segment)
    }

    return [string]::Join("/", $segments)
}

function Get-UxParityArchiveEntry {
    param(
        [Parameter(Mandatory = $true)][System.IO.Compression.ZipArchive]$Archive,
        [Parameter(Mandatory = $true)][string]$Path
    )

    $normalizedPath = Get-UxParityPackagePath $Path
    return @($Archive.Entries | Where-Object {
            [string]::Equals((Get-UxParityPackagePath $_.FullName), $normalizedPath, [System.StringComparison]::OrdinalIgnoreCase)
        } | Select-Object -First 1)[0]
}

function Read-UxParityPackageXml {
    param([Parameter(Mandatory = $true)][System.IO.Compression.ZipArchiveEntry]$Entry)

    $document = New-Object System.Xml.XmlDocument
    $document.PreserveWhitespace = $true
    $stream = $Entry.Open()
    try {
        $document.Load($stream)
    }
    finally {
        $stream.Dispose()
    }

    return $document
}

function Set-UxParityPackageXml {
    param(
        [Parameter(Mandatory = $true)][System.IO.Compression.ZipArchive]$Archive,
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][System.Xml.XmlDocument]$Document
    )

    $existingEntry = Get-UxParityArchiveEntry $Archive $Path
    if ($null -ne $existingEntry) {
        $existingEntry.Delete()
    }

    $entry = $Archive.CreateEntry($Path, [System.IO.Compression.CompressionLevel]::Optimal)
    $settings = New-Object System.Xml.XmlWriterSettings
    $settings.Encoding = New-Object System.Text.UTF8Encoding($false)
    $settings.Indent = $false
    $stream = $entry.Open()
    $writer = [System.Xml.XmlWriter]::Create($stream, $settings)
    try {
        $Document.Save($writer)
    }
    finally {
        $writer.Dispose()
        $stream.Dispose()
    }
}

function Get-UxParityRelationshipSourcePath {
    param([Parameter(Mandatory = $true)][string]$RelationshipPartPath)

    $path = Get-UxParityPackagePath $RelationshipPartPath
    if ($path -eq "_rels/.rels") {
        return ""
    }

    $relationshipPartMatch = [System.Text.RegularExpressions.Regex]::Match(
        $path,
        "^(?<directory>.*?)/_rels/(?<name>[^/]+)\.rels$")
    if (-not $relationshipPartMatch.Success) {
        return $null
    }

    $directory = $relationshipPartMatch.Groups["directory"].Value
    $name = $relationshipPartMatch.Groups["name"].Value
    if ([string]::IsNullOrEmpty($directory)) {
        return $name
    }

    return "$directory/$name"
}

function Resolve-UxParityRelationshipTarget {
    param(
        [Parameter(Mandatory = $true)][AllowEmptyString()][string]$SourcePartPath,
        [Parameter(Mandatory = $true)][string]$Target
    )

    $normalizedTarget = ($Target -replace '\\', '/').Trim()
    if ($normalizedTarget.StartsWith("/", [System.StringComparison]::Ordinal)) {
        return Get-UxParityPackagePath $normalizedTarget
    }

    $sourceDirectory = ""
    $separatorIndex = $SourcePartPath.LastIndexOf('/')
    if ($separatorIndex -ge 0) {
        $sourceDirectory = $SourcePartPath.Substring(0, $separatorIndex)
    }

    $combinedPath = if ([string]::IsNullOrEmpty($sourceDirectory)) {
        $normalizedTarget
    }
    else {
        "$sourceDirectory/$normalizedTarget"
    }

    return Get-UxParityPackagePath $combinedPath
}

function Test-UxParityRichDataRelationshipType {
    param([string]$RelationshipType)

    if ([string]::IsNullOrWhiteSpace($RelationshipType)) {
        return $false
    }

    foreach ($suffix in @(
            "/rdArray",
            "/rdSupportingPropertyBag",
            "/rdSupportingPropertyBagStructure",
            "/rdRichValueTypes",
            "/richStyles",
            "/richValueRel",
            "/rdRichValue",
            "/rdRichValueStructure")) {
        if ($RelationshipType.EndsWith($suffix, [System.StringComparison]::OrdinalIgnoreCase)) {
            return $true
        }
    }

    return $false
}

function Test-UxParityRichValueMetadataType {
    param([string]$MetadataTypeName)

    return -not [string]::IsNullOrWhiteSpace($MetadataTypeName) -and
        $MetadataTypeName.IndexOf("RICHVALUE", [System.StringComparison]::OrdinalIgnoreCase) -ge 0
}

function Remove-UxParityRichValueMetadataBindings {
    param(
        [Parameter(Mandatory = $true)][System.Xml.XmlDocument]$Document,
        [Parameter(Mandatory = $true)][string]$ContainerName,
        [Parameter(Mandatory = $true)][hashtable]$RichTypeIndexes,
        [Parameter(Mandatory = $true)][hashtable]$RetainedTypeIndexes
    )

    $container = $Document.SelectSingleNode("/*[local-name()='metadata']/*[local-name()='$ContainerName']")
    $indexMap = @{}
    if ($null -eq $container) {
        return $indexMap
    }

    $oldIndex = 0
    $newIndex = 0
    foreach ($binding in @($container.SelectNodes("./*[local-name()='bk']"))) {
        $oldIndex++
        foreach ($record in @($binding.SelectNodes("./*[local-name()='rc']"))) {
            $typeIndex = 0
            if (-not [int]::TryParse($record.GetAttribute("t"), [ref]$typeIndex)) {
                continue
            }

            if ($RichTypeIndexes.ContainsKey($typeIndex)) {
                [void]$binding.RemoveChild($record)
                continue
            }

            if ($RetainedTypeIndexes.ContainsKey($typeIndex)) {
                $record.SetAttribute("t", [string]$RetainedTypeIndexes[$typeIndex])
            }
        }

        if ($binding.SelectNodes("./*").Count -eq 0) {
            [void]$container.RemoveChild($binding)
            $indexMap[$oldIndex] = 0
            continue
        }

        $newIndex++
        $indexMap[$oldIndex] = $newIndex
    }

    $container.SetAttribute("count", [string]$newIndex)
    return $indexMap
}

function Update-UxParityWorksheetMetadataReferences {
    param(
        [Parameter(Mandatory = $true)][System.IO.Compression.ZipArchive]$Archive,
        [Parameter(Mandatory = $true)][hashtable]$CellMetadataIndexMap,
        [Parameter(Mandatory = $true)][hashtable]$ValueMetadataIndexMap,
        [Parameter(Mandatory = $true)][bool]$RemoveAllMetadataReferences
    )

    foreach ($entry in @($Archive.Entries | Where-Object {
                $path = Get-UxParityPackagePath $_.FullName
                $path.StartsWith("xl/worksheets/", [System.StringComparison]::OrdinalIgnoreCase) -and
                $path.EndsWith(".xml", [System.StringComparison]::OrdinalIgnoreCase)
            })) {
        $document = Read-UxParityPackageXml $entry
        $changed = $false
        foreach ($cell in @($document.SelectNodes("//*[local-name()='c' and (@cm or @vm)]"))) {
            foreach ($reference in @(
                    [pscustomobject]@{ Attribute = "cm"; Map = $CellMetadataIndexMap },
                    [pscustomobject]@{ Attribute = "vm"; Map = $ValueMetadataIndexMap })) {
                if (-not $cell.HasAttribute($reference.Attribute)) {
                    continue
                }

                $oldIndex = 0
                if ($RemoveAllMetadataReferences -or
                    -not [int]::TryParse($cell.GetAttribute($reference.Attribute), [ref]$oldIndex) -or
                    -not $reference.Map.ContainsKey($oldIndex) -or
                    [int]$reference.Map[$oldIndex] -eq 0) {
                    $cell.RemoveAttribute($reference.Attribute)
                    $changed = $true
                    continue
                }

                $newIndex = [int]$reference.Map[$oldIndex]
                if ($newIndex -ne $oldIndex) {
                    $cell.SetAttribute($reference.Attribute, [string]$newIndex)
                    $changed = $true
                }
            }
        }

        if ($changed) {
            Set-UxParityPackageXml $Archive (Get-UxParityPackagePath $entry.FullName) $document
        }
    }
}

function Remove-UxParityRichValueMetadata {
    param([Parameter(Mandatory = $true)][System.IO.Compression.ZipArchive]$Archive)

    $metadataEntry = Get-UxParityArchiveEntry $Archive "xl/metadata.xml"
    if ($null -eq $metadataEntry) {
        return [pscustomobject]@{ Removed = $false; RemoveMetadataPart = $false }
    }

    $document = Read-UxParityPackageXml $metadataEntry
    $metadataTypes = $document.SelectSingleNode("/*[local-name()='metadata']/*[local-name()='metadataTypes']")
    if ($null -eq $metadataTypes) {
        return [pscustomobject]@{ Removed = $false; RemoveMetadataPart = $false }
    }

    $richTypeIndexes = @{}
    $retainedTypeIndexes = @{}
    $oldIndex = 0
    $newIndex = 0
    foreach ($metadataType in @($metadataTypes.SelectNodes("./*[local-name()='metadataType']"))) {
        $oldIndex++
        if (Test-UxParityRichValueMetadataType $metadataType.GetAttribute("name")) {
            $richTypeIndexes[$oldIndex] = $true
            [void]$metadataTypes.RemoveChild($metadataType)
            continue
        }

        $newIndex++
        $retainedTypeIndexes[$oldIndex] = $newIndex
    }

    if ($richTypeIndexes.Count -eq 0) {
        return [pscustomobject]@{ Removed = $false; RemoveMetadataPart = $false }
    }

    $metadataTypes.SetAttribute("count", [string]$newIndex)
    foreach ($futureMetadata in @($document.SelectNodes("/*[local-name()='metadata']/*[local-name()='futureMetadata']"))) {
        if (Test-UxParityRichValueMetadataType $futureMetadata.GetAttribute("name")) {
            [void]$futureMetadata.ParentNode.RemoveChild($futureMetadata)
        }
    }

    $cellMetadataIndexMap = Remove-UxParityRichValueMetadataBindings $document "cellMetadata" $richTypeIndexes $retainedTypeIndexes
    $valueMetadataIndexMap = Remove-UxParityRichValueMetadataBindings $document "valueMetadata" $richTypeIndexes $retainedTypeIndexes
    $removeMetadataPart = $newIndex -eq 0
    Update-UxParityWorksheetMetadataReferences $Archive $cellMetadataIndexMap $valueMetadataIndexMap $removeMetadataPart

    if (-not $removeMetadataPart) {
        Set-UxParityPackageXml $Archive "xl/metadata.xml" $document
    }

    return [pscustomobject]@{ Removed = $true; RemoveMetadataPart = $removeMetadataPart }
}

function Remove-UxParityRelationshipReferences {
    param(
        [Parameter(Mandatory = $true)][System.IO.Compression.ZipArchive]$Archive,
        [Parameter(Mandatory = $true)][string]$SourcePartPath,
        [Parameter(Mandatory = $true)][string[]]$RelationshipIds
    )

    if ([string]::IsNullOrWhiteSpace($SourcePartPath) -or $RelationshipIds.Count -eq 0) {
        return
    }

    $sourceEntry = Get-UxParityArchiveEntry $Archive $SourcePartPath
    if ($null -eq $sourceEntry) {
        return
    }

    $document = Read-UxParityPackageXml $sourceEntry
    $references = @($document.SelectNodes("//*[@*[namespace-uri()='$script:RelationshipNamespace']]") | Where-Object {
            foreach ($attribute in $_.Attributes) {
                if ($attribute.NamespaceURI -eq $script:RelationshipNamespace -and $RelationshipIds -contains $attribute.Value) {
                    return $true
                }
            }
            return $false
        })

    foreach ($reference in $references) {
        $removalTarget = $reference
        while ($null -ne $removalTarget.ParentNode -and $removalTarget.LocalName -ne "ext") {
            $removalTarget = $removalTarget.ParentNode
        }

        if ($null -ne $removalTarget.ParentNode) {
            [void]$removalTarget.ParentNode.RemoveChild($removalTarget)
        }
    }

    if ($references.Count -gt 0) {
        Set-UxParityPackageXml $Archive $SourcePartPath $document
    }
}

function Get-UxParityLinkedDataTypePackageEntries {
    param([Parameter(Mandatory = $true)][string]$WorkbookPath)

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [System.IO.Compression.ZipFile]::OpenRead($WorkbookPath)
    try {
        return @($archive.Entries |
            Where-Object { (Get-UxParityPackagePath $_.FullName).StartsWith($script:RichDataRoot, [System.StringComparison]::OrdinalIgnoreCase) } |
            ForEach-Object FullName)
    }
    finally {
        $archive.Dispose()
    }
}

function Remove-UxParityLinkedDataTypes {
    param([Parameter(Mandatory = $true)][string]$WorkbookPath)

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [System.IO.Compression.ZipFile]::Open($WorkbookPath, [System.IO.Compression.ZipArchiveMode]::Update)
    try {
        $pathsToRemove = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
        foreach ($entry in @($archive.Entries)) {
            $path = Get-UxParityPackagePath $entry.FullName
            if ($path.StartsWith($script:RichDataRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
                [void]$pathsToRemove.Add($path)
            }
        }

        $metadata = Remove-UxParityRichValueMetadata $archive
        if ($metadata.RemoveMetadataPart) {
            [void]$pathsToRemove.Add("xl/metadata.xml")
        }

        $removedRelationshipCount = 0
        $removedRelationshipReferences = @{}
        foreach ($relationshipEntry in @($archive.Entries | Where-Object {
                    (Get-UxParityPackagePath $_.FullName).EndsWith(".rels", [System.StringComparison]::OrdinalIgnoreCase)
                })) {
            $relationshipPath = Get-UxParityPackagePath $relationshipEntry.FullName
            $sourcePath = Get-UxParityRelationshipSourcePath $relationshipPath
            if ($null -eq $sourcePath -or $pathsToRemove.Contains($sourcePath)) {
                continue
            }

            $document = Read-UxParityPackageXml $relationshipEntry
            $removedIds = New-Object System.Collections.Generic.List[string]
            foreach ($relationship in @($document.SelectNodes("/*[local-name()='Relationships']/*[local-name()='Relationship']"))) {
                $target = $relationship.GetAttribute("Target")
                $targetPath = Resolve-UxParityRelationshipTarget $sourcePath $target
                if ($pathsToRemove.Contains($targetPath) -or
                    $targetPath.StartsWith($script:RichDataRoot, [System.StringComparison]::OrdinalIgnoreCase) -or
                    (Test-UxParityRichDataRelationshipType $relationship.GetAttribute("Type"))) {
                    $removedIds.Add($relationship.GetAttribute("Id"))
                    [void]$relationship.ParentNode.RemoveChild($relationship)
                    $removedRelationshipCount++
                }
            }

            if ($removedIds.Count -gt 0) {
                Set-UxParityPackageXml $archive $relationshipPath $document
                $removedRelationshipReferences[$sourcePath] = $removedIds.ToArray()
            }
        }

        foreach ($sourcePath in $removedRelationshipReferences.Keys) {
            Remove-UxParityRelationshipReferences $archive $sourcePath $removedRelationshipReferences[$sourcePath]
        }

        $contentTypes = Get-UxParityArchiveEntry $archive "[Content_Types].xml"
        if ($null -ne $contentTypes) {
            $contentTypesDocument = Read-UxParityPackageXml $contentTypes
            $removedOverrides = @($contentTypesDocument.SelectNodes("/*[local-name()='Types']/*[local-name()='Override']") | Where-Object {
                    $partName = Get-UxParityPackagePath $_.GetAttribute("PartName")
                    $pathsToRemove.Contains($partName) -or
                    $partName.StartsWith($script:RichDataRoot, [System.StringComparison]::OrdinalIgnoreCase)
                })
            foreach ($override in $removedOverrides) {
                [void]$override.ParentNode.RemoveChild($override)
            }
            if ($removedOverrides.Count -gt 0) {
                Set-UxParityPackageXml $archive "[Content_Types].xml" $contentTypesDocument
            }
        }

        $removedEntries = New-Object System.Collections.Generic.List[string]
        foreach ($entry in @($archive.Entries)) {
            $path = Get-UxParityPackagePath $entry.FullName
            if ($pathsToRemove.Contains($path) -or $path.StartsWith($script:RichDataRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
                $removedEntries.Add($entry.FullName)
                $entry.Delete()
            }
        }

        return [pscustomobject]@{
            RemovedEntries = $removedEntries.ToArray()
            RemovedRelationshipCount = $removedRelationshipCount
            RemovedMetadataPart = $metadata.RemoveMetadataPart
            RemovedRichValueMetadata = $metadata.Removed
        }
    }
    finally {
        $archive.Dispose()
    }
}

Export-ModuleMember -Function Get-UxParityLinkedDataTypePackageEntries, Remove-UxParityLinkedDataTypes
