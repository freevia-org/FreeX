param(
    [string]$ProjectRoot = "."
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot

. (Join-Path $PSScriptRoot "ToolScriptSupport.ps1")

# These are the only shared projects exempt from the neutral shared boundary. Each exception is
# explicit and reason-bearing so a renderer suffix alone can never opt a new project out.
$rendererSharedProjectExceptions = [ordered]@{
    "shared/Free.Shared.AppServices.Windows/Free.Shared.AppServices.Windows.csproj" = "Windows file-association adapter"
    "shared/Free.Shared.Pdf.Skia/Free.Shared.Pdf.Skia.csproj" = "Skia PDF renderer"
    "shared/Free.Shared.Pdf.Wpf/Free.Shared.Pdf.Wpf.csproj" = "WPF PDF renderer"
    "shared/Free.Shared.Ribbon.Avalonia/Free.Shared.Ribbon.Avalonia.csproj" = "Avalonia ribbon renderer"
    "shared/Free.Shared.Ribbon.Wpf/Free.Shared.Ribbon.Wpf.csproj" = "WPF ribbon renderer"
    "shared/Free.Shared.Shell.Avalonia/Free.Shared.Shell.Avalonia.csproj" = "Avalonia shell adapter"
    "shared/Free.Shared.Shell.Wpf/Free.Shared.Shell.Wpf.csproj" = "WPF shell adapter"
    "shared/Free.Shared.Theme.Avalonia/Free.Shared.Theme.Avalonia.csproj" = "Avalonia theme adapter"
    "shared/Free.Shared.Theme.Wpf/Free.Shared.Theme.Wpf.csproj" = "WPF theme adapter"
}

$rendererPackagePatterns = [ordered]@{
    "^Avalonia(?:\.|$)" = "Avalonia renderer package"
    "(?:^|[.-])WPF(?:[.-]|$)" = "WPF renderer package"
    "(?:^|[.-])Win32(?:[.-]|$)|WindowsDesktop|WindowsForms" = "Win32 desktop renderer package"
    "^SkiaSharp(?:\.|$)" = "Skia renderer package"
    "^VideoLAN\.LibVLC\.Windows$" = "Windows media renderer package"
    "^System\.Drawing\.Common$" = "Windows GDI renderer package"
}

$rendererReferencePatterns = [ordered]@{
    "^Microsoft\.WindowsDesktop\.App(?:\.(?:WPF|WindowsForms))?$" = "WindowsDesktop framework reference"
    "^(?:PresentationCore|PresentationFramework|ReachFramework|System\.Xaml|WindowsBase|WindowsFormsIntegration)$" = "WPF assembly reference"
}

function Test-IsIgnoredProjectPath {
    param([Parameter(Mandatory = $true)][System.IO.FileInfo]$ProjectFile)

    if ($ProjectFile.Name -like "*_wpftmp.csproj") {
        return $true
    }

    $relativePath = Get-ToolRelativePath -RootPath $resolvedProjectRoot -Path $ProjectFile.FullName
    $segments = $relativePath -split "/"
    return $segments -contains "bin" -or
        $segments -contains "obj" -or
        $segments -contains ".git" -or
        $segments -contains ".worktrees" -or
        $segments -contains ".claude"
}

function Get-ProjectXmlItemIncludes {
    param(
        [Parameter(Mandatory = $true)][xml]$ProjectXml,
        [Parameter(Mandatory = $true)][string]$ItemName
    )

    foreach ($item in @($ProjectXml.SelectNodes("//*[local-name()='$ItemName']"))) {
        $include = $item.GetAttribute("Include")
        if (-not [string]::IsNullOrWhiteSpace($include)) {
            $include
        }
    }
}

function Get-ProjectXmlPropertyValues {
    param(
        [Parameter(Mandatory = $true)][xml]$ProjectXml,
        [Parameter(Mandatory = $true)][string]$PropertyName
    )

    foreach ($property in @($ProjectXml.SelectNodes("//*[local-name()='$PropertyName']"))) {
        if (-not [string]::IsNullOrWhiteSpace($property.InnerText)) {
            $property.InnerText.Trim()
        }
    }
}

function Get-RendererDependencyMarkers {
    param([Parameter(Mandatory = $true)][xml]$ProjectXml)

    $sdk = $ProjectXml.Project.GetAttribute("Sdk")
    if ($sdk -match "(?i)Microsoft\.NET\.Sdk\.WindowsDesktop") {
        "WindowsDesktop SDK"
    }

    foreach ($targetFramework in @(Get-ProjectXmlPropertyValues -ProjectXml $ProjectXml -PropertyName "TargetFramework") +
            @(Get-ProjectXmlPropertyValues -ProjectXml $ProjectXml -PropertyName "TargetFrameworks")) {
        if ($targetFramework -match "(?i)(?:^|;)[^;]*-windows(?:[0-9.]*)?(?:;|$)") {
            "Windows target framework: $targetFramework"
        }
    }

    foreach ($propertyName in @("UseWPF", "UseWindowsForms")) {
        foreach ($value in Get-ProjectXmlPropertyValues -ProjectXml $ProjectXml -PropertyName $propertyName) {
            if ($value -match "(?i)^true$") {
                "$propertyName=$value"
            }
        }
    }

    foreach ($package in Get-ProjectXmlItemIncludes -ProjectXml $ProjectXml -ItemName "PackageReference") {
        foreach ($pattern in $rendererPackagePatterns.GetEnumerator()) {
            if ($package -match "(?i)$($pattern.Key)") {
                "$($pattern.Value): $package"
                break
            }
        }
    }

    foreach ($itemName in @("FrameworkReference", "Reference")) {
        foreach ($reference in Get-ProjectXmlItemIncludes -ProjectXml $ProjectXml -ItemName $itemName) {
            foreach ($pattern in $rendererReferencePatterns.GetEnumerator()) {
                if ($reference -match "(?i)$($pattern.Key)") {
                    "$($pattern.Value): $reference"
                    break
                }
            }
        }
    }
}

$resolvedProjectRoot = Resolve-ToolRepoPath -Path $ProjectRoot -RepoRoot $repoRoot
if (-not (Test-Path -LiteralPath $resolvedProjectRoot -PathType Container)) {
    throw "Project root was not found: $resolvedProjectRoot"
}
$resolvedProjectRootPath = [System.IO.Path]::GetFullPath($resolvedProjectRoot)
if (-not $resolvedProjectRootPath.EndsWith([System.IO.Path]::DirectorySeparatorChar)) {
    $resolvedProjectRootPath += [System.IO.Path]::DirectorySeparatorChar
}

$projectFiles = @(
    Get-ToolProjectFiles -Directory (Get-Item -LiteralPath $resolvedProjectRoot) |
        Sort-Object FullName
)

if ($projectFiles.Count -eq 0) {
    throw "No .csproj files were found in $resolvedProjectRoot"
}

$missingReferences = New-Object System.Collections.Generic.List[string]
$escapedReferences = New-Object System.Collections.Generic.List[string]
$duplicateReferences = New-Object System.Collections.Generic.List[string]
$architectureBoundaryViolations = New-Object System.Collections.Generic.List[string]
$projectsByResolvedPath = @{}

foreach ($projectFile in $projectFiles) {
    [xml]$projectXml = Get-Content -LiteralPath $projectFile.FullName -Raw
    $projectsByResolvedPath[$projectFile.FullName.ToUpperInvariant()] = [pscustomobject]@{
        File = $projectFile
        Xml = $projectXml
        RelativePath = Get-ToolRelativePath -RootPath $resolvedProjectRoot -Path $projectFile.FullName
    }
}

foreach ($exception in $rendererSharedProjectExceptions.GetEnumerator()) {
    $exceptionPath = Resolve-ToolFullPath -Path $exception.Key -BasePath $resolvedProjectRoot
    $exceptionKey = $exceptionPath.ToUpperInvariant()
    if ([string]::IsNullOrWhiteSpace($exception.Value)) {
        $architectureBoundaryViolations.Add("Renderer exception has no documented reason: $($exception.Key)")
        continue
    }
    if (-not $projectsByResolvedPath.ContainsKey($exceptionKey)) {
        $architectureBoundaryViolations.Add("Renderer exception does not name a scanned project: $($exception.Key)")
        continue
    }

    $rendererMarkers = @(Get-RendererDependencyMarkers -ProjectXml $projectsByResolvedPath[$exceptionKey].Xml)
    if ($rendererMarkers.Count -eq 0) {
        $architectureBoundaryViolations.Add("Renderer exception has no renderer dependency marker: $($exception.Key) ($($exception.Value))")
    }
}

foreach ($projectFile in $projectFiles) {
    $projectRecord = $projectsByResolvedPath[$projectFile.FullName.ToUpperInvariant()]
    [xml]$projectXml = $projectRecord.Xml
    $projectReferences = @(Get-ProjectXmlItemIncludes -ProjectXml $projectXml -ItemName "ProjectReference")
    $referencesByResolvedPath = @{}
    $relativeProjectPath = $projectRecord.RelativePath
    $isNeutralSharedProject = $relativeProjectPath.StartsWith("shared/", [System.StringComparison]::OrdinalIgnoreCase) -and
        -not $rendererSharedProjectExceptions.Contains($relativeProjectPath)
    $isPortableAppTier = $projectFile.BaseName -match "^Free[XPW]\.App\.(?:Presentation|Services)$"

    if ($isPortableAppTier) {
        foreach ($rendererMarker in Get-RendererDependencyMarkers -ProjectXml $projectXml) {
            $architectureBoundaryViolations.Add("${relativeProjectPath}: portable app tier declares $rendererMarker")
        }
    }

    foreach ($projectReference in $projectReferences) {
        $include = [string]$projectReference
        if ([string]::IsNullOrWhiteSpace($include)) {
            continue
        }

        $referencedProjectPath = Join-Path $projectFile.DirectoryName $include
        $resolvedReferencePath = [System.IO.Path]::GetFullPath($referencedProjectPath)
        $resolvedReferenceKey = $resolvedReferencePath.ToUpperInvariant()

        if ($referencesByResolvedPath.ContainsKey($resolvedReferenceKey)) {
            $duplicateReferences.Add("${relativeProjectPath}: $include")
        } else {
            $referencesByResolvedPath[$resolvedReferenceKey] = $include
        }

        if (-not $resolvedReferencePath.StartsWith($resolvedProjectRootPath, [System.StringComparison]::OrdinalIgnoreCase)) {
            $escapedReferences.Add("${relativeProjectPath}: $include")
            continue
        }

        if (-not (Test-Path -LiteralPath $resolvedReferencePath -PathType Leaf)) {
            $missingReferences.Add("${relativeProjectPath}: $include")
            continue
        }

        $referencedProjectName = [System.IO.Path]::GetFileNameWithoutExtension($resolvedReferencePath)
        $referencedProject = if ($projectsByResolvedPath.ContainsKey($resolvedReferenceKey)) {
            $projectsByResolvedPath[$resolvedReferenceKey]
        } else {
            $null
        }
        $isProductProject = $referencedProjectName -match "^Free[XPW](?:\.|$)" -or
            ($null -ne $referencedProject -and
                $referencedProject.RelativePath -match "^(?:src/|freew/(?!tools/)|freep/(?!tools/))")
        if ($isNeutralSharedProject -and $isProductProject) {
            $architectureBoundaryViolations.Add("${relativeProjectPath}: neutral shared project references product project $include")
        }

        if ($isPortableAppTier -and $null -ne $referencedProject) {
            foreach ($rendererMarker in Get-RendererDependencyMarkers -ProjectXml $referencedProject.Xml) {
                $architectureBoundaryViolations.Add("${relativeProjectPath}: portable app tier references renderer project $($referencedProject.RelativePath) ($rendererMarker)")
            }
        }
    }
}

if ($duplicateReferences.Count -gt 0) {
    foreach ($duplicateReference in $duplicateReferences) {
        Write-Error "Duplicate ProjectReference target: $duplicateReference" -ErrorAction Continue
    }
}

if ($escapedReferences.Count -gt 0) {
    foreach ($escapedReference in $escapedReferences) {
        Write-Error "ProjectReference target escapes project root: $escapedReference" -ErrorAction Continue
    }
}

if ($missingReferences.Count -gt 0) {
    foreach ($missingReference in $missingReferences) {
        Write-Error "Missing ProjectReference target: $missingReference" -ErrorAction Continue
    }
}

if ($architectureBoundaryViolations.Count -gt 0) {
    foreach ($architectureBoundaryViolation in $architectureBoundaryViolations) {
        Write-Error "Architecture boundary violation: $architectureBoundaryViolation" -ErrorAction Continue
    }
}

if ($duplicateReferences.Count -gt 0 -or
    $escapedReferences.Count -gt 0 -or
    $missingReferences.Count -gt 0 -or
    $architectureBoundaryViolations.Count -gt 0) {
    $violationCount = $duplicateReferences.Count + $escapedReferences.Count +
        $missingReferences.Count + $architectureBoundaryViolations.Count
    throw "Project reference validation failed for $violationCount reference or architecture violation(s)."
}

Write-Host "Validated ProjectReference targets for $($projectFiles.Count) .NET project file(s)."
Write-Host "Validated neutral shared and portable app-tier architecture boundaries."
