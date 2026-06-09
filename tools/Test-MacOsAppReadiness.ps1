param(
    [string]$ProjectRoot = "",
    [string]$AvaloniaProjectPath = "src\FreeX.App.Avalonia\FreeX.App.Avalonia.csproj",
    [string]$InfoPlistPath = "src\FreeX.App.Avalonia\Packaging\macos\Info.plist",
    [string]$IconPath = "src\FreeX.App.Avalonia\Packaging\macos\FreeX.icns",
    [string]$WorkflowPath = ".github\workflows\macos-app.yml",
    [string[]]$PortableSourceRoots = @("src\FreeX.App.Avalonia", "src\FreeX.App.Services")
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($ProjectRoot)) {
    $repoRoot = Split-Path -Parent $PSScriptRoot
}
else {
    $repoRoot = [System.IO.Path]::GetFullPath($ProjectRoot)
}

function Resolve-RepoPath {
    param([Parameter(Mandatory = $true)][string]$Path)

    if ([System.IO.Path]::IsPathRooted($Path)) {
        return [System.IO.Path]::GetFullPath($Path)
    }

    return [System.IO.Path]::GetFullPath((Join-Path $repoRoot $Path))
}

function Get-RepoRelativePath {
    param([Parameter(Mandatory = $true)][string]$Path)

    $fullPath = [System.IO.Path]::GetFullPath($Path)
    $root = [System.IO.Path]::GetFullPath($repoRoot)
    if (-not $root.EndsWith([System.IO.Path]::DirectorySeparatorChar.ToString(), [System.StringComparison]::Ordinal)) {
        $root += [System.IO.Path]::DirectorySeparatorChar
    }

    if ($fullPath.StartsWith($root, [System.StringComparison]::OrdinalIgnoreCase)) {
        return $fullPath.Substring($root.Length).Replace("\", "/")
    }

    return $Path.Replace("\", "/")
}

function Assert-True {
    param(
        [Parameter(Mandatory = $true)][bool]$Condition,
        [Parameter(Mandatory = $true)][string]$Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

function Assert-ContainsText {
    param(
        [Parameter(Mandatory = $true)][string]$Text,
        [Parameter(Mandatory = $true)][string]$Needle,
        [Parameter(Mandatory = $true)][string]$Message
    )

    if ($Text.IndexOf($Needle, [System.StringComparison]::Ordinal) -lt 0) {
        throw $Message
    }
}

function Assert-TextBefore {
    param(
        [Parameter(Mandatory = $true)][string]$Text,
        [Parameter(Mandatory = $true)][string]$First,
        [Parameter(Mandatory = $true)][string]$Second,
        [Parameter(Mandatory = $true)][string]$Message
    )

    $firstIndex = $Text.IndexOf($First, [System.StringComparison]::Ordinal)
    $secondIndex = $Text.IndexOf($Second, [System.StringComparison]::Ordinal)
    if ($firstIndex -lt 0 -or $secondIndex -lt 0 -or $firstIndex -ge $secondIndex) {
        throw $Message
    }
}

function Get-WorkflowStepBlock {
    param(
        [Parameter(Mandatory = $true)][string]$Workflow,
        [Parameter(Mandatory = $true)][string]$StepName
    )

    $marker = "      - name: $StepName"
    $start = $Workflow.IndexOf($marker, [System.StringComparison]::Ordinal)
    if ($start -lt 0) {
        throw "macOS workflow is missing the '$StepName' step."
    }

    $next = $Workflow.IndexOf("`n      - name:", $start + $marker.Length, [System.StringComparison]::Ordinal)
    if ($next -lt 0) {
        $next = $Workflow.Length
    }

    return $Workflow.Substring($start, $next - $start)
}

function Assert-ExactSet {
    param(
        [Parameter(Mandatory = $true)][string[]]$Actual,
        [Parameter(Mandatory = $true)][string[]]$Expected,
        [Parameter(Mandatory = $true)][string]$Label
    )

    $actualSet = @($Actual | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Sort-Object -Unique)
    $expectedSet = @($Expected | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Sort-Object -Unique)
    foreach ($expectedValue in $expectedSet) {
        Assert-True -Condition ($actualSet -contains $expectedValue) -Message "$Label must include '$expectedValue'."
    }

    foreach ($actualValue in $actualSet) {
        Assert-True -Condition ($expectedSet -contains $actualValue) -Message "$Label must not include unexpected value '$actualValue'. Expected: $($expectedSet -join ';')."
    }

    Assert-True -Condition ($actualSet.Count -eq $expectedSet.Count) -Message "$Label must exactly match $($expectedSet -join ';')."
}

function Assert-FileExists {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Label
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "$Label was not found: $Path"
    }
}

function Get-ProjectProperty {
    param(
        [Parameter(Mandatory = $true)][xml]$Project,
        [Parameter(Mandatory = $true)][string]$Name
    )

    foreach ($group in @($Project.Project.PropertyGroup)) {
        $value = $group.$Name
        if ($null -eq $value) {
            continue
        }

        if ($value -is [System.Xml.XmlElement]) {
            $text = [string]$value.InnerText
        } else {
            $text = [string]$value
        }

        if (-not [string]::IsNullOrWhiteSpace($text)) {
            return $text
        }
    }

    return $null
}

function Get-ProjectPropertyNodes {
    param(
        [Parameter(Mandatory = $true)][xml]$Project,
        [Parameter(Mandatory = $true)][string]$Name
    )

    $nodes = @()
    foreach ($group in @($Project.Project.PropertyGroup)) {
        foreach ($child in @($group.ChildNodes)) {
            if ($child.NodeType -eq [System.Xml.XmlNodeType]::Element -and $child.LocalName -eq $Name) {
                $nodes += $child
            }
        }
    }

    return $nodes
}

function Get-ProjectItemNodes {
    param(
        [Parameter(Mandatory = $true)][xml]$Project,
        [Parameter(Mandatory = $true)][string]$Name
    )

    $nodes = @()
    foreach ($group in @($Project.Project.ItemGroup)) {
        foreach ($child in @($group.ChildNodes)) {
            if ($child.NodeType -eq [System.Xml.XmlNodeType]::Element -and $child.LocalName -eq $Name) {
                $nodes += $child
            }
        }
    }

    return $nodes
}

function Get-ProjectNodeCondition {
    param([Parameter(Mandatory = $true)]$Node)

    if ($Node.HasAttribute("Condition")) {
        return [string]$Node.GetAttribute("Condition")
    }

    if ($Node.ParentNode -and $Node.ParentNode.HasAttribute("Condition")) {
        return [string]$Node.ParentNode.GetAttribute("Condition")
    }

    return ""
}

function Get-ProjectItems {
    param(
        [Parameter(Mandatory = $true)][xml]$Project,
        [Parameter(Mandatory = $true)][string]$Name
    )

    $items = @()
    foreach ($group in @($Project.Project.ItemGroup)) {
        foreach ($item in @($group.$Name)) {
            if ($null -ne $item) {
                $items += $item
            }
        }
    }

    return $items
}

function Get-BigEndianUInt32 {
    param(
        [Parameter(Mandatory = $true)][byte[]]$Bytes,
        [Parameter(Mandatory = $true)][int]$Offset
    )

    return ([uint32]$Bytes[$Offset] -shl 24) -bor
        ([uint32]$Bytes[$Offset + 1] -shl 16) -bor
        ([uint32]$Bytes[$Offset + 2] -shl 8) -bor
        [uint32]$Bytes[$Offset + 3]
}

function Get-PlistValue {
    param(
        [Parameter(Mandatory = $true)][System.Xml.XmlElement]$Dict,
        [Parameter(Mandatory = $true)][string]$Key
    )

    $children = @($Dict.ChildNodes | Where-Object { $_.NodeType -eq [System.Xml.XmlNodeType]::Element })
    for ($index = 0; $index -lt $children.Count - 1; $index++) {
        if ($children[$index].Name -eq "key" -and $children[$index].InnerText -eq $Key) {
            return $children[$index + 1]
        }
    }

    return $null
}

function Get-PlistString {
    param(
        [Parameter(Mandatory = $true)][System.Xml.XmlElement]$Dict,
        [Parameter(Mandatory = $true)][string]$Key
    )

    $value = Get-PlistValue -Dict $Dict -Key $Key
    if ($null -eq $value) {
        return $null
    }

    Assert-True -Condition ($value.Name -eq "string") -Message "Info.plist key '$Key' must be a string."
    return [string]$value.InnerText
}

function Get-PlistBool {
    param(
        [Parameter(Mandatory = $true)][System.Xml.XmlElement]$Dict,
        [Parameter(Mandatory = $true)][string]$Key
    )

    $value = Get-PlistValue -Dict $Dict -Key $Key
    if ($null -eq $value) {
        return $null
    }

    if ($value.Name -eq "true") {
        return $true
    }

    if ($value.Name -eq "false") {
        return $false
    }

    throw "Info.plist key '$Key' must be a boolean."
}

function Test-IsIgnoredSourcePath {
    param([Parameter(Mandatory = $true)][string]$Path)

    $relative = Get-RepoRelativePath $Path
    $segments = $relative.Split(@("/", "\"), [System.StringSplitOptions]::RemoveEmptyEntries)
    return $segments -contains "bin" -or
        $segments -contains "obj" -or
        $segments -contains ".worktrees" -or
        $segments -contains ".claude"
}

function Test-IsMacOsConditionalSourcePath {
    param([Parameter(Mandatory = $true)][string]$Path)

    $relative = Get-RepoRelativePath $Path
    return $relative.StartsWith("src/FreeX.App.Avalonia/MacOs/", [System.StringComparison]::OrdinalIgnoreCase)
}

function Test-MacOsIcon {
    param([Parameter(Mandatory = $true)][string]$IconFilePath)

    $bytes = [System.IO.File]::ReadAllBytes($IconFilePath)
    Assert-True -Condition ($bytes.Length -ge 8) -Message "macOS app icon must be a non-empty .icns file."

    $magic = [System.Text.Encoding]::ASCII.GetString($bytes, 0, 4)
    Assert-True -Condition ($magic -eq "icns") -Message "macOS app icon must start with the icns magic header."

    $declaredLength = Get-BigEndianUInt32 -Bytes $bytes -Offset 4
    Assert-True -Condition ($declaredLength -eq $bytes.Length) -Message "macOS app icon declared length must match the file length."

    $entryTypes = @()
    $offset = 8
    while ($offset -lt $bytes.Length) {
        Assert-True -Condition ($offset + 8 -le $bytes.Length) -Message "macOS app icon contains a truncated entry header."

        $entryType = [System.Text.Encoding]::ASCII.GetString($bytes, $offset, 4)
        $entryLength = Get-BigEndianUInt32 -Bytes $bytes -Offset ($offset + 4)
        Assert-True -Condition ($entryLength -ge 8) -Message "macOS app icon entry '$entryType' must include an entry header."
        Assert-True -Condition ($offset + $entryLength -le $bytes.Length) -Message "macOS app icon entry '$entryType' extends past the file length."

        $entryTypes += $entryType
        $offset += $entryLength
    }

    Assert-True -Condition ($offset -eq $bytes.Length) -Message "macOS app icon entries must end at the file length."
    foreach ($entryType in @("icp4", "icp5", "ic08")) {
        Assert-True -Condition ($entryTypes -contains $entryType) -Message "macOS app icon must include '$entryType'."
    }

    Write-Host "Validated macOS app icon $(Get-RepoRelativePath $IconFilePath) with entries: $($entryTypes -join ', ')."
}

function Test-AvaloniaProject {
    param([Parameter(Mandatory = $true)][string]$ProjectPath)

    [xml]$project = Get-Content -LiteralPath $ProjectPath -Raw

    $targetFrameworkNodes = @(Get-ProjectPropertyNodes -Project $project -Name "TargetFramework")
    Assert-True -Condition ($targetFrameworkNodes.Count -gt 0) -Message "Avalonia app TargetFramework must be net10.0."

    $targetFrameworkValues = @($targetFrameworkNodes | ForEach-Object { [string]$_.InnerText } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    Assert-True -Condition ($targetFrameworkValues -contains "net10.0") -Message "Avalonia app TargetFramework must be net10.0, but was '$($targetFrameworkValues -join ';')'."
    foreach ($targetFrameworkValue in $targetFrameworkValues) {
        Assert-True -Condition ($targetFrameworkValue.IndexOf("-windows", [System.StringComparison]::OrdinalIgnoreCase) -lt 0) -Message "Avalonia app TargetFramework must not be Windows-specific."
    }

    $targetFrameworksNodes = @(Get-ProjectPropertyNodes -Project $project -Name "TargetFrameworks")
    Assert-True -Condition ($targetFrameworksNodes.Count -eq 1) -Message "Avalonia app TargetFrameworks must have a single opt-in macOS TFM property."
    $macOsTargetFrameworksCondition = Get-ProjectNodeCondition $targetFrameworksNodes[0]
    Assert-True -Condition ($macOsTargetFrameworksCondition -eq "'`$(EnableMacOsTargetFramework)' == 'true'") -Message "Avalonia app TargetFrameworks must be guarded by EnableMacOsTargetFramework."
    $macOsTargetFrameworks = @([string]$targetFrameworksNodes[0].InnerText -split ";" | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    Assert-ExactSet -Actual $macOsTargetFrameworks -Expected @("net10.0", "net10.0-macos") -Label "Avalonia app opt-in TargetFrameworks"

    $defaultFrameworkNodes = @($targetFrameworkNodes | Where-Object { [string]$_.InnerText -eq "net10.0" })
    Assert-True -Condition ($defaultFrameworkNodes.Count -eq 1) -Message "Avalonia app must keep exactly one default net10.0 TargetFramework."
    Assert-True -Condition ((Get-ProjectNodeCondition $defaultFrameworkNodes[0]) -eq "'`$(EnableMacOsTargetFramework)' != 'true'") -Message "Avalonia app default TargetFramework must be used when EnableMacOsTargetFramework is not true."

    $supportedOsVersionNodes = @(Get-ProjectPropertyNodes -Project $project -Name "SupportedOSPlatformVersion")
    Assert-True -Condition ($supportedOsVersionNodes.Count -eq 1) -Message "Avalonia app must declare SupportedOSPlatformVersion for net10.0-macos."
    Assert-True -Condition ([string]$supportedOsVersionNodes[0].InnerText -eq "12.0") -Message "Avalonia app SupportedOSPlatformVersion must be 12.0."
    Assert-True -Condition ((Get-ProjectNodeCondition $supportedOsVersionNodes[0]) -eq "'`$(TargetFramework)' == 'net10.0-macos'") -Message "Avalonia app SupportedOSPlatformVersion must be scoped to net10.0-macos."

    $outputType = Get-ProjectProperty -Project $project -Name "OutputType"
    Assert-True -Condition ($outputType -eq "Exe") -Message "Avalonia app OutputType must be Exe."

    $assemblyName = Get-ProjectProperty -Project $project -Name "AssemblyName"
    Assert-True -Condition ($assemblyName -eq "FreeX") -Message "Avalonia app AssemblyName must be FreeX."

    $applicationTitle = Get-ProjectProperty -Project $project -Name "ApplicationTitle"
    Assert-True -Condition ($applicationTitle -eq "FreeX") -Message "Avalonia app ApplicationTitle must be FreeX."

    $runtimeIdentifiers = (Get-ProjectProperty -Project $project -Name "RuntimeIdentifiers")
    $runtimeSet = @($runtimeIdentifiers -split ";" | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    Assert-ExactSet -Actual $runtimeSet -Expected @("osx-arm64", "osx-x64") -Label "Avalonia app RuntimeIdentifiers"

    $useWpf = Get-ProjectProperty -Project $project -Name "UseWPF"
    Assert-True -Condition ($useWpf -ne "true") -Message "Avalonia app project must not enable UseWPF."

    $packageReferences = @(Get-ProjectItems -Project $project -Name "PackageReference" | ForEach-Object { [string]$_.Include })
    foreach ($package in @("Avalonia", "Avalonia.Desktop", "Avalonia.Fonts.Inter", "Avalonia.Themes.Fluent")) {
        Assert-True -Condition ($packageReferences -contains $package) -Message "Avalonia app project must reference package '$package'."
    }

    $contentItems = @(Get-ProjectItems -Project $project -Name "Content" | ForEach-Object { [string]$_.Include })
    Assert-True -Condition ($contentItems -contains "Packaging\macos\FreeX.icns") -Message "Avalonia app project must include the macOS app icon as content."

    $macOsSourceRemoves = @(Get-ProjectItemNodes -Project $project -Name "Compile" | Where-Object { $_.GetAttribute("Remove") -eq "MacOs\**\*.cs" })
    Assert-True -Condition ($macOsSourceRemoves.Count -eq 1) -Message "Avalonia app project must exclude MacOs source from non-macOS target frameworks."
    Assert-True -Condition ((Get-ProjectNodeCondition $macOsSourceRemoves[0]) -eq "'`$(TargetFramework)' != 'net10.0-macos'") -Message "Avalonia app MacOs source exclusion must apply outside net10.0-macos."

    $macOsDefineConstants = @(Get-ProjectPropertyNodes -Project $project -Name "DefineConstants" | Where-Object { [string]$_.InnerText -match "(^|;)FREEX_MACOS_SHARE_SHEET(;|$)" })
    Assert-True -Condition ($macOsDefineConstants.Count -eq 1) -Message "Avalonia app project must define FREEX_MACOS_SHARE_SHEET for the native macOS share sheet."
    Assert-True -Condition ((Get-ProjectNodeCondition $macOsDefineConstants[0]) -eq "'`$(TargetFramework)' == 'net10.0-macos'") -Message "Avalonia app FREEX_MACOS_SHARE_SHEET constant must be scoped to net10.0-macos."

    $allowedProjectReferences = @(
        "FreeX.App.Services",
        "FreeX.Core.Calc",
        "FreeX.Core.Commands",
        "FreeX.Core.IO",
        "FreeX.Core.Model"
    )
    $projectReferences = @(Get-ProjectItems -Project $project -Name "ProjectReference")
    Assert-True -Condition ($projectReferences.Count -gt 0) -Message "Avalonia app project must reference shared portable projects."

    foreach ($reference in $projectReferences) {
        $include = [string]$reference.Include
        $name = [System.IO.Path]::GetFileNameWithoutExtension($include)
        Assert-True -Condition ($allowedProjectReferences -contains $name) -Message "Avalonia app ProjectReference '$include' is not in the portable allowlist."
        Assert-True -Condition ($include.IndexOf("FreeX.App.Host", [System.StringComparison]::OrdinalIgnoreCase) -lt 0) -Message "Avalonia app must not reference FreeX.App.Host."
        Assert-True -Condition ($include.IndexOf("FreeX.App.UI", [System.StringComparison]::OrdinalIgnoreCase) -lt 0) -Message "Avalonia app must not reference FreeX.App.UI."
        Assert-True -Condition ($include.IndexOf("tests", [System.StringComparison]::OrdinalIgnoreCase) -lt 0) -Message "Avalonia app must not reference test projects."
        Assert-True -Condition ($include.IndexOf("tools", [System.StringComparison]::OrdinalIgnoreCase) -lt 0) -Message "Avalonia app must not reference tool projects."
    }

    return @{
        AssemblyName = $assemblyName
        ProjectPathForWorkflow = (Get-RepoRelativePath $ProjectPath)
        RuntimeIdentifiers = $runtimeSet
    }
}

function Test-InfoPlist {
    param(
        [Parameter(Mandatory = $true)][string]$PlistPath,
        [Parameter(Mandatory = $true)][string]$ExpectedExecutable
    )

    [xml]$plist = Get-Content -LiteralPath $PlistPath -Raw
    $rootDict = $plist.plist.dict
    Assert-True -Condition ($null -ne $rootDict) -Message "Info.plist must contain a root dict."

    Assert-True -Condition ((Get-PlistString -Dict $rootDict -Key "CFBundleDisplayName") -eq "FreeX") -Message "Info.plist CFBundleDisplayName must be FreeX."
    Assert-True -Condition ((Get-PlistString -Dict $rootDict -Key "CFBundleExecutable") -eq $ExpectedExecutable) -Message "Info.plist CFBundleExecutable must match the Avalonia AssemblyName."
    Assert-True -Condition ((Get-PlistString -Dict $rootDict -Key "CFBundleIdentifier") -eq "io.github.tony-xmelon.freex") -Message "Info.plist CFBundleIdentifier must be io.github.tony-xmelon.freex."
    Assert-True -Condition ((Get-PlistString -Dict $rootDict -Key "CFBundleIconFile") -eq "FreeX.icns") -Message "Info.plist CFBundleIconFile must be FreeX.icns."
    Assert-True -Condition ((Get-PlistString -Dict $rootDict -Key "CFBundleName") -eq "FreeX") -Message "Info.plist CFBundleName must be FreeX."
    Assert-True -Condition ((Get-PlistString -Dict $rootDict -Key "CFBundlePackageType") -eq "APPL") -Message "Info.plist CFBundlePackageType must be APPL."
    Assert-True -Condition ((Get-PlistString -Dict $rootDict -Key "LSMinimumSystemVersion") -eq "12.0") -Message "Info.plist LSMinimumSystemVersion must be 12.0."
    Assert-True -Condition ((Get-PlistBool -Dict $rootDict -Key "NSHighResolutionCapable") -eq $true) -Message "Info.plist NSHighResolutionCapable must be true."

    $documentTypes = Get-PlistValue -Dict $rootDict -Key "CFBundleDocumentTypes"
    Assert-True -Condition ($null -ne $documentTypes -and $documentTypes.Name -eq "array") -Message "Info.plist must declare CFBundleDocumentTypes."
    $documentTypeDicts = @($documentTypes.ChildNodes | Where-Object { $_.NodeType -eq [System.Xml.XmlNodeType]::Element -and $_.Name -eq "dict" })
    Assert-True -Condition ($documentTypeDicts.Count -eq 2) -Message "Info.plist must declare exactly the native and imported workbook document types."

    $nativeWorkbook = $documentTypeDicts[0]
    Assert-True -Condition ((Get-PlistString -Dict $nativeWorkbook -Key "CFBundleTypeName") -eq "FreeX Workbook") -Message "Info.plist native document type name must be FreeX Workbook."
    Assert-True -Condition ((Get-PlistString -Dict $nativeWorkbook -Key "CFBundleTypeRole") -eq "Editor") -Message "Info.plist native document type role must be Editor."
    Assert-True -Condition ((Get-PlistString -Dict $nativeWorkbook -Key "LSHandlerRank") -eq "Owner") -Message "Info.plist native document handler rank must be Owner."
    $nativeExtensions = Get-PlistValue -Dict $nativeWorkbook -Key "CFBundleTypeExtensions"
    $nativeExtensionValues = @($nativeExtensions.ChildNodes | Where-Object { $_.NodeType -eq [System.Xml.XmlNodeType]::Element -and $_.Name -eq "string" } | ForEach-Object { $_.InnerText })
    Assert-ExactSet -Actual $nativeExtensionValues -Expected @("fxl") -Label "Info.plist native document type extensions"

    $importedWorkbooks = $documentTypeDicts[1]
    Assert-True -Condition ((Get-PlistString -Dict $importedWorkbooks -Key "CFBundleTypeName") -eq "Spreadsheet Workbooks") -Message "Info.plist imported document type name must be Spreadsheet Workbooks."
    Assert-True -Condition ((Get-PlistString -Dict $importedWorkbooks -Key "CFBundleTypeRole") -eq "Viewer") -Message "Info.plist imported document type role must be Viewer."
    Assert-True -Condition ((Get-PlistString -Dict $importedWorkbooks -Key "LSHandlerRank") -eq "Alternate") -Message "Info.plist imported document handler rank must be Alternate."
    $importedExtensions = Get-PlistValue -Dict $importedWorkbooks -Key "CFBundleTypeExtensions"
    $importedExtensionValues = @($importedExtensions.ChildNodes | Where-Object { $_.NodeType -eq [System.Xml.XmlNodeType]::Element -and $_.Name -eq "string" } | ForEach-Object { $_.InnerText })
    Assert-ExactSet -Actual $importedExtensionValues -Expected @("xlsx", "xlsm", "xltx", "xltm", "xls", "xlsb", "xlt", "csv", "tsv", "tab") -Label "Info.plist imported document type extensions"
}

function Test-MacOsWorkflow {
    param(
        [Parameter(Mandatory = $true)][string]$WorkflowPath,
        [Parameter(Mandatory = $true)][string]$ProjectPathForWorkflow,
        [Parameter(Mandatory = $true)][string[]]$RuntimeIdentifiers
    )

    $workflow = Get-Content -LiteralPath $WorkflowPath -Raw
    $projectPath = $ProjectPathForWorkflow.Replace("\", "/")

    $workflowRuntimeSet = @([System.Text.RegularExpressions.Regex]::Matches($workflow, "\bosx-[A-Za-z0-9]+\b") | ForEach-Object { $_.Value })
    Assert-ExactSet -Actual $workflowRuntimeSet -Expected $RuntimeIdentifiers -Label "macOS workflow runtime markers"

    $workflowRunnerPairs = @(
        [System.Text.RegularExpressions.Regex]::Matches(
            $workflow,
            "(?m)^\s*-\s*runtime:\s*(?<runtime>osx-[A-Za-z0-9]+)\s*\r?\n\s*runner:\s*(?<runner>[A-Za-z0-9._-]+)\s*$") |
            ForEach-Object { "$($_.Groups['runtime'].Value)=$($_.Groups['runner'].Value)" }
    )
    Assert-ExactSet -Actual $workflowRunnerPairs -Expected @("osx-arm64=macos-15", "osx-x64=macos-15-intel") -Label "macOS workflow runtime runner matrix"

    $requiredWorkflowMarkers = @(
        'runs-on: ${{ matrix.runner }}',
        "runner: macos-15",
        "runner: macos-15-intel",
        "distribution_candidate:",
        "Require Developer ID signing, accepted notarization, stapled ticket, and Gatekeeper assessment evidence.",
        "dotnet-version: 10.0.x",
        "dotnet build $projectPath --configuration Release",
        "Test portable PDF macOS route",
        "dotnet test tests/FreeX.App.Services.Tests/FreeX.App.Services.Tests.csproj",
        "FullyQualifiedName~FreeX.App.Services.Tests.PortablePdfDocumentExporterTests",
        "FullyQualifiedName~FreeX.App.Services.Tests.PortablePdfExportPlannerTests",
        "FullyQualifiedName~FreeX.App.Services.Tests.PortablePdfPageContentPlannerTests",
        "FullyQualifiedName~FreeX.App.Services.Tests.PortablePdfTextCapabilityPlannerTests",
        "FullyQualifiedName~FreeX.App.Services.Tests.WorkbookExportPrintPlannerTests",
        "FullyQualifiedName~FreeX.App.Services.Tests.WorkbookShareActionPlannerTests",
        "FullyQualifiedName~FreeX.App.Services.Tests.WorkbookViewportScrollPlannerTests",
        "FullyQualifiedName~FreeX.App.Services.Tests.OpenRecentWorkbookMenuPlannerTests",
        "FullyQualifiedName~FreeX.App.Services.Tests.AppDiagnosticsFileStoreTests",
        "FullyQualifiedName~FreeX.App.Services.Tests.AppServicesPortabilityGuardTests",
        "FullyQualifiedName~FreeX.App.Services.Tests.AvaloniaProjectPortabilityGuardTests",
        "FullyQualifiedName~FreeX.App.Services.Tests.ApplicationDataPathGuardTests",
        "FullyQualifiedName~FreeX.App.Services.Tests.AppStoragePathPlannerTests",
        "FullyQualifiedName~FreeX.App.Services.Tests.AppOptionsStoreTests",
        "FullyQualifiedName~FreeX.App.Services.Tests.AtomicFileWriterTests",
        "FullyQualifiedName~FreeX.App.Services.Tests.AvaloniaShellSourceTests",
        "FullyQualifiedName~FreeX.App.Services.Tests.MacOsLaunchSmokeReportKeyDriftGuardTests",
        "dotnet test tests/FreeX.Core.Model.Tests/FreeX.Core.Model.Tests.csproj",
        "FullyQualifiedName~FreeX.Core.Model.Tests.ExportPathPlannerTests",
        'freex-${{ matrix.runtime }}-portable-pdf-exporter-tests.trx',
        'freex-${{ matrix.runtime }}-export-path-tests.trx',
        'artifacts/freex-${{ matrix.runtime }}-portable-pdf-exporter-tests.trx',
        'artifacts/freex-${{ matrix.runtime }}-export-path-tests.trx',
        "--results-directory artifacts",
        "dotnet publish $projectPath",
        "--framework net10.0",
        "--self-contained true",
        "-p:UseAppHost=true",
        "-p:PublishReadyToRun=false",
        "-p:PublishSingleFile=false",
        '--output "$app/Contents/MacOS"',
        "cp src/FreeX.App.Avalonia/Packaging/macos/Info.plist",
        'cp src/FreeX.App.Avalonia/Packaging/macos/FreeX.icns "$app/Contents/Resources/FreeX.icns"',
        "plutil -lint",
        'test -f "$app/Contents/MacOS/FreeX"',
        'test -x "$app/Contents/MacOS/FreeX"',
        'test -f "$app/Contents/MacOS/FreeX.dll"',
        'test -f "$app/Contents/Resources/FreeX.icns"',
        "PlistBuddy -c 'Print :CFBundleExecutable'",
        "PlistBuddy -c 'Print :CFBundleIconFile'",
        "PlistBuddy -c 'Print :CFBundleIdentifier'",
        "PlistBuddy -c 'Print :CFBundlePackageType'",
        "PlistBuddy -c 'Print :LSMinimumSystemVersion'",
        "PlistBuddy -c 'Print :NSHighResolutionCapable'",
        "PlistBuddy -c 'Print :CFBundleDocumentTypes:0:CFBundleTypeExtensions:0'",
        "PlistBuddy -c 'Print :CFBundleDocumentTypes:1:CFBundleTypeExtensions:0'",
        "lipo -archs",
        "codesign --verify --deep --strict",
        "ditto -c -k --sequesterRsrc --keepParent",
        'ditto -x -k "$zip_path" "$unzip_root"',
        "shasum -a 256",
        'test -x "$unzip_root/FreeX.app/Contents/MacOS/FreeX"',
        'test -f "$unzip_root/FreeX.app/Contents/MacOS/FreeX.dll"',
        'tester_instructions_path="$artifact_root/freex-$runtime-macos-tester-instructions.md"',
        "Require hosted smoke before app artifact upload",
        'smoke_status=skipped_host_arch_mismatch',
        'echo "app_artifact_upload_blocked=host_arch_mismatch" >> "$evidence_path"',
        'rm -f "$zip_path" "$zip_path.sha256"',
        'Host/runtime architecture mismatch for $runtime on $host_arch cannot publish a macOS app artifact.',
        'grep -q "^smoke_status=passed$" "$evidence_path"',
        'grep -q "^macos_launch_smoke=passed$" "$launch_smoke_report"',
        'grep -q "^macos_launch_smoke=passed$" "$open_with_report"',
        'grep -q "^macos_launch_smoke=passed$" "$default_open_report"',
        "actions/upload-artifact@v7",
        "if-no-files-found: error",
        "Upload app diagnostics",
        "if: always()",
        'freex-${{ github.run_id }}-${{ github.run_attempt }}-${{ matrix.runtime }}-macos-diagnostics',
        "if-no-files-found: warn",
        "publish-distribution-candidate:",
        "Publish macOS distribution candidate",
        "needs: [macos-app, macos-preview-readiness]",
        "if: `${{ github.event_name == 'workflow_dispatch' && inputs.distribution_candidate == true }}",
        "permissions:",
        "actions: read",
        "contents: write",
        "macos-distribution-candidate-release",
        "actions/download-artifact@v7",
        'pattern: freex-${{ github.run_id }}-${{ github.run_attempt }}-*-macos-app',
        "merge-multiple: true",
        "Prepare release-channel assets",
        "FreeX-latest-macos-arm64.zip",
        "FreeX-latest-macos-x64.zip",
        "FreeX-latest-macos-distribution-candidate-manifest.json",
        "FreeX-latest-macos-distribution-candidate-instructions.md",
        'FreeX-latest-$assetLabel-default-open-launch-smoke.txt',
        "source_artifact_pattern",
        "distribution_candidate_required_markers",
        '$packagingSmokeText = Get-Content -LiteralPath $packagingSmokePath -Raw',
        'Assert-ContainsRequiredText -Text $smokeReportText -Needle "macos_launch_smoke=passed"',
        "default_open_launch_smoke_report",
        "Upload release-channel prepared assets",
        "Create or update GitHub release",
        "gh release create",
        "gh release upload",
        "--draft=false",
        "--prerelease",
        "Developer ID signing is disabled for pull_request events; using ad-hoc signing.",
        'FREEX_RUNTIME: ${{ matrix.runtime }}',
        'FREEX_DISTRIBUTION_CANDIDATE: ${{ github.event_name == ''workflow_dispatch'' && inputs.distribution_candidate == true }}',
        'runtime="$FREEX_RUNTIME"',
        'distribution_candidate="$FREEX_DISTRIBUTION_CANDIDATE"',
        'artifact_channel="internal-preview"',
        'artifact_channel="distribution-candidate"',
        "internal_preview_not_for_distribution_notarization_optional",
        "distribution_candidate_requires_developer_id_notarization_stapling",
        "Distribution-candidate macOS app runs require Developer ID signing secrets",
        "Distribution-candidate macOS app runs require notarization secrets",
        "xcrun notarytool submit",
        "xcrun stapler validate",
        'xcrun stapler validate "$app" | tee -a "$notary_log"',
        "Distribution-candidate run requires codesign_mode=developer-id, notarization_status=accepted, and stapler_validated=true.",
        '/usr/sbin/spctl --assess --type execute --verbose=4 "$app_path"',
        'gatekeeper_assessment_required="$distribution_candidate"',
        'echo "gatekeeper_assessment_attempted=true"',
        'echo "gatekeeper_assessment_required=$gatekeeper_assessment_required"',
        'echo "gatekeeper_assessment_subject=unzipped_app_bundle"',
        'echo "gatekeeper_assessment_type=execute"',
        'echo "gatekeeper_assessment_exit_code=$gatekeeper_assessment_exit_code"',
        'echo "gatekeeper_assessment_status=$gatekeeper_assessment_status"',
        'echo "gatekeeper_assessment_source=$gatekeeper_assessment_source"',
        'echo "gatekeeper_assessment_output=$gatekeeper_line"',
        "distribution_candidate_blocked_gatekeeper_assessment",
        'gatekeeper_assessment_source" != "Notarized Developer ID"',
        "Distribution-candidate run requires accepted Gatekeeper assessment from Notarized Developer ID.",
        "gatekeeper_assessment_required=true",
        "gatekeeper_assessment_exit_code=0",
        "gatekeeper_assessment_status=accepted",
        "gatekeeper_assessment_source=Notarized Developer ID",
        "distribution_readiness=internal_preview_not_for_distribution",
        "distribution_readiness=distribution_candidate_ready",
        "smoke_status=passed",
        'echo "artifact_channel=$artifact_channel"',
        'echo "distribution_candidate=$distribution_candidate"',
        'echo "distribution_readiness=$distribution_readiness"',
        'shasum -a 256 -c "$zip_name.sha256"',
        'zip_sha256="$(cut -d '' '' -f 1 "$artifact_root/$zip_name.sha256")"',
        'echo "zip_sha256=$zip_sha256"',
        'echo "artifact_bundle_metadata_subject=unzipped_app_bundle"',
        'bundle_executable=$(/usr/libexec/PlistBuddy -c ''Print :CFBundleExecutable'' "$app_info_plist")',
        'bundle_icon=$(/usr/libexec/PlistBuddy -c ''Print :CFBundleIconFile'' "$app_info_plist")',
        'bundle_identifier=$(/usr/libexec/PlistBuddy -c ''Print :CFBundleIdentifier'' "$app_info_plist")',
        'bundle_package_type=$(/usr/libexec/PlistBuddy -c ''Print :CFBundlePackageType'' "$app_info_plist")',
        'bundle_minimum_system_version=$(/usr/libexec/PlistBuddy -c ''Print :LSMinimumSystemVersion'' "$app_info_plist")',
        'bundle_high_resolution_capable=$(/usr/libexec/PlistBuddy -c ''Print :NSHighResolutionCapable'' "$app_info_plist")',
        'artifact_document_extensions_subject=unzipped_app_bundle',
        'native_document_type=$(/usr/libexec/PlistBuddy -c ''Print :CFBundleDocumentTypes:0:CFBundleTypeName'' "$app_info_plist")',
        'imported_document_type=$(/usr/libexec/PlistBuddy -c ''Print :CFBundleDocumentTypes:1:CFBundleTypeName'' "$app_info_plist")',
        'cat > "$tester_instructions_path" <<EOF',
        "This artifact is a macOS port validation build. Internal-preview artifacts are not a public release channel; distribution-candidate artifacts must show Developer ID signing, accepted notarization, stapler validation, and accepted Gatekeeper assessment in evidence.",
        "Use osx-arm64 for Apple Silicon Macs and osx-x64 for Intel Macs.",
        "Unzip the GitHub Actions artifact wrapper first; these files are inside it.",
        'ditto -x -k $zip_name .',
        "If artifact_channel=internal-preview, ad-hoc signed or non-notarized previews may require Control-click or right-click > Open for trusted internal testing.",
        "--packaging-smoke",
        "Packaging smoke opened",
        "macOS Preview Workbook",
        "drawing_object_previews=3",
        "roundtrip_drawing_object_previews=3",
        'grep -q "format_cells_style_roundtrip=true" "$smoke_log"',
        'format_cells_style_roundtrip_count="$(grep -c "format_cells_style_roundtrip=true" "$smoke_log")"',
        'test "$format_cells_style_roundtrip_count" -ge 2',
        'echo "format_cells_style_roundtrip=true"',
        'echo "format_cells_style_roundtrip_count=$format_cells_style_roundtrip_count"',
        "edited, saved, and reopened",
        "lsregister -f",
        'app_diagnostics_dir="$artifact_root/freex-$runtime-macos-app-diagnostics"',
        '--macos-launch-smoke-diagnostics-dir "$app_diagnostics_dir"',
        "app_diagnostics_directory_configured=true",
        'app_diagnostics_events_path="$app_diagnostics_dir/events.jsonl"',
        'app_diagnostics_crash_reports_dir="$app_diagnostics_dir/CrashReports"',
        'if [[ -d "$app_diagnostics_crash_reports_dir" ]]; then',
        'app_diagnostics_crash_count=0',
        "app_diagnostics_artifact=freex-`$runtime-macos-app-diagnostics",
        "app_diagnostics_events_jsonl=true",
        "app_diagnostics_crash_report_count=`$app_diagnostics_crash_count",
        'test -f "$app_diagnostics_events_path"',
        'grep -q ''"eventName":"app_start"'' "$app_diagnostics_events_path"',
        'grep -q ''"eventName":"app_ready"'' "$app_diagnostics_events_path"',
        'grep -q ''"eventName":"macos_launch_smoke"'' "$app_diagnostics_events_path"',
        "launchservices_smoke_timeout_seconds=60",
        "launchservices_cleanup_timeout_seconds=10",
        "append_launchservices_failure_diagnostics",
        "wait_for_bounded_launchservices_cleanup",
        'run_bounded_launchservices_smoke "bundle_id" "$launch_smoke_report"',
        "open -W -n -b io.github.tony-xmelon.freex",
        'open_with_report="$artifact_root/freex-$runtime-macos-open-with-launch-smoke.txt"',
        'open_with_smoke_file="$RUNNER_TEMP/freex-$runtime-open-with.csv"',
        'app_path="$unzip_root/FreeX.app"',
        'run_bounded_launchservices_smoke "open_with" "$open_with_report"',
        'open -W -n -a "$app_path" "$open_with_smoke_file" --args --macos-launch-smoke "$open_with_report"',
        'default_open_report="$artifact_root/freex-$runtime-macos-default-open-launch-smoke.txt"',
        'default_open_smoke_file="$RUNNER_TEMP/freex-$runtime-default-open.fxl"',
        '"FileFormat": "FreeX.NativeJsonWorkbook"',
        'run_bounded_launchservices_smoke "default_open" "$default_open_report"',
        'open -W -n "$default_open_smoke_file" --args --macos-launch-smoke "$default_open_report"',
        'launchservices_smoke_timed_out=$timed_out',
        "launchservices_smoke_cleanup_timeout=true",
        'kill "$launchservices_pid" 2>/dev/null || true',
        'kill -9 "$launchservices_pid" 2>/dev/null || true',
        'cat "$report_path" >> "$evidence_path"',
        "launchservices_default_open_app_override=false",
        "launchservices_default_open_document_extension=fxl",
        "launchservices_default_open_boundary=ci_open_document_without_app_override_not_finder_double_click",
        "osascript -e 'tell application id `"io.github.tony-xmelon.freex`" to quit' || true",
        "--macos-launch-smoke",
        "cmd_find_direct_route_source_guard=true",
        "cmd_page_up_direct_route_source_guard=true",
        "cmd_page_down_direct_route_source_guard=true",
        "live_command_key_smoke_required=false",
        "live_command_key_smoke=not_required",
        "external_image_clipboard_paste_required=false",
        "macos_accessibility_smoke=passed",
        "a11y_formula_box_name=true",
        "a11y_formula_box_help=true",
        "a11y_formula_box_id=true",
        "a11y_status_text_name=true",
        "a11y_status_text_help=true",
        "a11y_status_text_id=true",
        "a11y_status_text_value=true",
        "a11y_cell_address_name=true",
        "a11y_cell_address_help=true",
        "a11y_cell_address_id=true",
        "a11y_selection_stats_name=true",
        "a11y_selection_stats_help=true",
        "a11y_selection_stats_id=true",
        "new_sheet_button=true",
        "toolbar_format_painter_button=true",
        "toolbar_autosum_button=true",
        "toolbar_autosum_sum_menu_item=true",
        "toolbar_autosum_average_menu_item=true",
        "toolbar_autosum_count_numbers_menu_item=true",
        "toolbar_autosum_count_all_menu_item=true",
        "toolbar_autosum_max_menu_item=true",
        "toolbar_autosum_min_menu_item=true",
        "toolbar_fill_cells_button=true",
        "toolbar_fill_down_menu_item=true",
        "toolbar_fill_right_menu_item=true",
        "toolbar_fill_up_menu_item=true",
        "toolbar_fill_left_menu_item=true",
        "toolbar_clear_button=true",
        "toolbar_clear_all_menu_item=true",
        "toolbar_clear_formats_menu_item=true",
        "toolbar_clear_contents_menu_item=true",
        "toolbar_clear_comments_menu_item=true",
        "toolbar_clear_hyperlinks_menu_item=true",
        "toolbar_borders_button=true",
        "toolbar_wrap_text_button=true",
        "toolbar_merge_and_center_button=true",
        "native_file_menu=true",
        "native_edit_menu=true",
        "native_data_menu=true",
        "native_review_menu=true",
        "native_format_menu=true",
        "native_view_menu=true",
        "native_sheet_menu=true",
        "native_window_menu=true",
        "native_help_menu=true",
        "native_new_workbook_menu_item=true",
        "native_open_recent_menu_item=true",
        "native_open_recent_item_count=[1-9]",
        "native_export_pdf_menu_item=true",
        "native_share_workbook_menu_item=true",
        'grep -q "macos_launch_smoke=passed" "$open_with_report"',
        'grep -q "window_shown=true" "$open_with_report"',
        'grep -q "opened_source_path=.*freex-$runtime-open-with.csv" "$open_with_report"',
        'grep -q "viewport_rows=[1-9]" "$open_with_report"',
        'grep -q "viewport_columns=[1-9]" "$open_with_report"',
        'grep -q "native_open_recent_menu_item=true" "$open_with_report"',
        'grep -q "native_open_recent_item_count=[1-9]" "$open_with_report"',
        'freex-${{ matrix.runtime }}-macos-open-with-launch-smoke.txt',
        'grep -q "opened_source_path=.*freex-$runtime-default-open.fxl" "$default_open_report"',
        'grep -q "launchservices_default_open_app_override=false" "$default_open_report"',
        'grep -q "launchservices_default_open_document_extension=fxl" "$default_open_report"',
        'grep -q "launchservices_default_open_boundary=ci_open_document_without_app_override_not_finder_double_click" "$default_open_report"',
        'freex-${{ matrix.runtime }}-macos-default-open-launch-smoke.txt',
        "native_close_workbook_menu_item=true",
        "native_workbook_statistics_menu_item=true",
        "native_new_sheet_menu_item=true",
        "native_rename_sheet_menu_item=true",
        "native_duplicate_sheet_menu_item=true",
        "native_move_sheet_left_menu_item=true",
        "native_move_sheet_right_menu_item=true",
        "native_tab_color_menu_item=true",
        "native_tab_color_clear_item=true",
        "native_tab_color_swatch_count=69",
        "focusable_sheet_tab=true",
        "focusable_active_sheet_tab=true",
        "shell_focus_cycle_targets=true",
        "sheet_tab_context_keyboard_help=true",
        "sheet_tab_context_rename_menu_item=true",
        "sheet_tab_context_tab_color_menu_item=true",
        "sheet_tab_context_no_color_menu_item=true",
        "sheet_tab_context_select_all_sheets_menu_item=true",
        "sheet_tab_context_ungroup_sheets_menu_item=true",
        "native_select_all_sheets_menu_item=true",
        "native_ungroup_sheets_menu_item=true",
        "native_hide_sheet_menu_item=true",
        "native_unhide_sheet_menu_item=true",
        "native_delete_sheet_menu_item=true",
        "native_cut_menu_item=true",
        "native_copy_menu_item=true",
        "native_paste_menu_item=true",
        "native_paste_special_menu_item=true",
        "native_format_painter_menu_item=true",
        "native_paste_special_comments_menu_item=true",
        "native_paste_special_validation_menu_item=true",
        "native_paste_special_all_except_borders_menu_item=true",
        "native_paste_special_all_merging_conditional_formats_menu_item=true",
        "native_paste_special_column_widths_menu_item=true",
        "native_paste_special_formulas_and_number_formats_menu_item=true",
        "native_paste_special_values_and_number_formats_menu_item=true",
        "native_paste_special_values_and_source_formatting_menu_item=true",
        "native_paste_special_keep_source_column_widths_menu_item=true",
        "native_paste_special_paste_link_menu_item=true",
        "native_paste_special_text_menu_item=true",
        "native_paste_special_unicode_text_menu_item=true",
        "native_paste_special_picture_menu_item=true",
        "native_paste_special_linked_picture_menu_item=true",
        "native_select_all_menu_item=true",
        "native_find_menu_item=true",
        "native_find_next_menu_item=true",
        "native_replace_menu_item=true",
        "native_go_to_menu_item=true",
        "native_go_to_special_menu_item=true",
        "native_sort_ascending_menu_item=true",
        "native_sort_descending_menu_item=true",
        "native_flash_fill_menu_item=true",
        "native_advanced_filter_menu_item=true",
        "native_remove_duplicates_menu_item=true",
        "native_subtotal_menu_item=true",
        "native_data_validation_preview_menu_item=true",
        "native_data_validation_menu_item=true",
        "native_what_if_analysis_menu_item=true",
        "native_goal_seek_menu_item=true",
        "native_data_table_menu_item=true",
        "native_scenario_manager_menu_item=true",
        "native_forecast_sheet_menu_item=true",
        "native_review_summary_menu_item=true",
        "native_check_accessibility_menu_item=true",
        "native_next_note_menu_item=true",
        "native_previous_note_menu_item=true",
        "native_next_comment_menu_item=true",
        "native_previous_comment_menu_item=true",
        "native_format_cells_menu_item=true",
        "macos_dialog_smoke=passed",
        "macos_dialog_smoke_attempted=true",
        "macos_dialog_smoke_status=passed",
        "macos_dialog_activation_completed=true",
        "find_dialog=true",
        "find_dialog_text_box=true",
        "find_dialog_action_buttons=true",
        "find_dialog_options=true",
        "find_dialog_format_controls=true",
        "find_dialog_compact_layout=true",
        "find_dialog_result_closed_without_accept=true",
        "replace_dialog=true",
        "replace_dialog_text_boxes=true",
        "replace_dialog_action_buttons=true",
        "replace_dialog_options=true",
        "replace_dialog_format_controls=true",
        "replace_dialog_compact_layout=true",
        "replace_dialog_result_closed_without_accept=true",
        "go_to_dialog=true",
        "go_to_dialog_reference_controls=true",
        "go_to_dialog_compact_layout=true",
        "go_to_dialog_result_closed_without_accept=true",
        "go_to_special_dialog=true",
        "go_to_special_dialog_kind_controls=true",
        "go_to_special_dialog_value_type_controls=true",
        "go_to_special_dialog_compact_layout=true",
        "go_to_special_dialog_result_closed_without_accept=true",
        "format_cells_dialog=true",
        "format_cells_dialog_tab_strip=true",
        "format_cells_dialog_default_number_tab=true",
        "format_cells_dialog_number_controls=true",
        "format_cells_dialog_action_buttons=true",
        "format_cells_dialog_compact_layout=true",
        "format_cells_dialog_result_closed_without_accept=true",
        "sort_dialog=true",
        "sort_dialog_sort_on_controls=true",
        "sort_dialog_color_controls=true",
        "sort_dialog_action_buttons=true",
        "sort_dialog_compact_layout=true",
        "sort_dialog_result_closed_without_accept=true",
        "data_validation_dropdown_control=true",
        "data_validation_dropdown_items=true",
        "data_validation_dialog=true",
        "data_validation_dialog_criteria_controls=true",
        "data_validation_dialog_message_controls=true",
        "data_validation_dialog_action_buttons=true",
        "data_validation_dialog_compact_layout=true",
        "data_validation_dialog_result_closed_without_accept=true",
        "native_autosum_menu_item=true",
        "native_autosum_sum_menu_item=true",
        "native_autosum_average_menu_item=true",
        "native_autosum_count_numbers_menu_item=true",
        "native_autosum_count_all_menu_item=true",
        "native_autosum_max_menu_item=true",
        "native_autosum_min_menu_item=true",
        "native_fill_cells_menu_item=true",
        "native_fill_down_menu_item=true",
        "native_fill_right_menu_item=true",
        "native_fill_up_menu_item=true",
        "native_fill_left_menu_item=true",
        "native_clear_menu_item=true",
        "native_clear_all_menu_item=true",
        "native_clear_formats_menu_item=true",
        "native_clear_contents_menu_item=true",
        "native_clear_comments_menu_item=true",
        "native_clear_hyperlinks_menu_item=true",
        "native_bold_menu_item=true",
        "native_italic_menu_item=true",
        "native_underline_menu_item=true",
        "native_double_underline_menu_item=true",
        "native_strikethrough_menu_item=true",
        "native_increase_font_size_menu_item=true",
        "native_decrease_font_size_menu_item=true",
        "native_fill_color_menu_item=true",
        "native_clear_fill_menu_item=true",
        "native_font_color_menu_item=true",
        "native_fill_color_swatch_count=69",
        "native_font_color_swatch_count=69",
        "native_borders_menu_item=true",
        "native_borders_preset_count=8",
        "native_merge_and_center_menu_item=true",
        "native_unmerge_cells_menu_item=true",
        "native_cell_styles_menu_item=true",
        "native_cell_styles_preset_count=33",
        "native_horizontal_text_menu_item=true",
        "native_angle_counterclockwise_menu_item=true",
        "native_angle_clockwise_menu_item=true",
        "native_vertical_text_menu_item=true",
        "native_rotate_text_up_menu_item=true",
        "native_rotate_text_down_menu_item=true",
        "native_currency_format_menu_item=true",
        "native_percent_format_menu_item=true",
        "native_comma_style_menu_item=true",
        "native_increase_decimal_menu_item=true",
        "native_decrease_decimal_menu_item=true",
        "native_align_top_menu_item=true",
        "native_align_middle_menu_item=true",
        "native_align_bottom_menu_item=true",
        "toolbar_wrap_text_button=true",
        "native_wrap_text_menu_item=true",
        "native_decrease_indent_menu_item=true",
        "native_increase_indent_menu_item=true",
        "native_align_left_menu_item=true",
        "native_align_center_menu_item=true",
        "native_align_right_menu_item=true",
        "native_show_gridlines_menu_item=true",
        "native_show_headings_menu_item=true",
        "native_zoom_in_menu_item=true",
        "native_zoom_out_menu_item=true",
        "native_zoom_100_menu_item=true",
        "native_zoom_to_selection_menu_item=true",
        "native_freeze_panes_menu_item=true",
        "native_freeze_top_row_menu_item=true",
        "native_freeze_first_column_menu_item=true",
        "native_unfreeze_panes_menu_item=true",
        "native_show_formulas_menu_item=true",
        "native_minimize_window_menu_item=true",
        "native_zoom_window_menu_item=true",
        "native_bring_all_to_front_menu_item=true",
        "native_help_online_menu_item=true",
        "native_send_feedback_menu_item=true",
        "native_check_for_updates_menu_item=true",
        "native_about_menu_item=true",
        "native_legal_notices_menu_item=true",
        "native_quit_menu_item=true",
        'bundle_icon=$('
    )

    foreach ($marker in $requiredWorkflowMarkers) {
        Assert-ContainsText -Text $workflow -Needle $marker -Message "macOS workflow is missing required readiness marker: $marker"
    }

    $releasePublicationJobMatch = [System.Text.RegularExpressions.Regex]::Match(
        $workflow,
        "(?ms)^  publish-distribution-candidate:\s*(?:#.*)?\r?\n(?<block>.*?)(?=^  [A-Za-z0-9_-]+:\s*(?:#.*)?$|\z)")
    Assert-True -Condition $releasePublicationJobMatch.Success -Message "macOS workflow must define the publish-distribution-candidate job."
    $releasePublicationJobBlock = $releasePublicationJobMatch.Value
    Assert-ContainsText `
        -Text $releasePublicationJobBlock `
        -Needle "needs: [macos-app, macos-preview-readiness]" `
        -Message "macOS distribution-candidate publication must depend on aggregate preview readiness."

    $boundedLaunchSmokeCount = ([regex]::Matches($workflow, 'run_bounded_launchservices_smoke "')).Count
    Assert-True -Condition ($boundedLaunchSmokeCount -eq 3) -Message "macOS workflow must route all three hosted LaunchServices launch smoke paths through run_bounded_launchservices_smoke."
    Assert-TextBefore -Text $workflow -First "run_bounded_launchservices_smoke() {" -Second 'run_bounded_launchservices_smoke "bundle_id" "$launch_smoke_report"' -Message "macOS workflow must define the bounded LaunchServices smoke helper before the bundle-id launch smoke."

    Assert-TextBefore -Text $workflow -First "Capture runner toolchain evidence" -Second "Test portable PDF macOS route" -Message "macOS workflow must capture hosted runner evidence before running the focused portable PDF/service tests."
    Assert-TextBefore -Text $workflow -First "Test portable PDF macOS route" -Second "dotnet build $projectPath --configuration Release" -Message "macOS workflow must run the focused portable PDF/service tests before building the Avalonia app project."
    Assert-TextBefore -Text $workflow -First "dotnet build $projectPath --configuration Release" -Second "dotnet publish $projectPath" -Message "macOS workflow must build the Avalonia app project before publishing the app bundle."
    Assert-TextBefore -Text $workflow -First 'echo "smoke_status=skipped_host_arch_mismatch" >> "$evidence_path"' -Second 'Host/runtime architecture mismatch for $runtime on $host_arch cannot publish a macOS app artifact.' -Message "macOS workflow must record host/runtime mismatch evidence before failing app artifact publication."
    Assert-TextBefore -Text $workflow -First 'rm -f "$zip_path" "$zip_path.sha256"' -Second 'Host/runtime architecture mismatch for $runtime on $host_arch cannot publish a macOS app artifact.' -Message "macOS workflow must remove a mismatched app zip before failing app artifact publication."
    Assert-TextBefore -Text $workflow -First 'Host/runtime architecture mismatch for $runtime on $host_arch cannot publish a macOS app artifact.' -Second "Require hosted smoke before app artifact upload" -Message "macOS workflow must fail host/runtime mismatches before reaching the hosted smoke upload gate."
    Assert-TextBefore -Text $workflow -First "Require hosted smoke before app artifact upload" -Second "Upload app artifact" -Message "macOS workflow must require successful hosted smoke before uploading the app artifact."

    $appArtifactUpload = Get-WorkflowStepBlock -Workflow $workflow -StepName "Upload app artifact"
    $hostedSmokeGate = Get-WorkflowStepBlock -Workflow $workflow -StepName "Require hosted smoke before app artifact upload"
    $diagnosticsUpload = Get-WorkflowStepBlock -Workflow $workflow -StepName "Upload app diagnostics"
    Assert-ContainsText -Text $hostedSmokeGate -Needle 'smoke_status=skipped_host_arch_mismatch' -Message "macOS hosted smoke gate must reject host/runtime architecture mismatch evidence before app artifact upload."
    Assert-ContainsText -Text $hostedSmokeGate -Needle 'grep -q "^smoke_status=passed$" "$evidence_path"' -Message "macOS hosted smoke gate must require smoke_status=passed evidence before app artifact upload."
    Assert-ContainsText -Text $hostedSmokeGate -Needle 'grep -q "^macos_launch_smoke=passed$" "$launch_smoke_report"' -Message "macOS hosted smoke gate must require bundle-id launch smoke before app artifact upload."
    Assert-ContainsText -Text $hostedSmokeGate -Needle 'grep -q "^macos_launch_smoke=passed$" "$open_with_report"' -Message "macOS hosted smoke gate must require Open With launch smoke before app artifact upload."
    Assert-ContainsText -Text $hostedSmokeGate -Needle 'grep -q "^macos_launch_smoke=passed$" "$default_open_report"' -Message "macOS hosted smoke gate must require default-open launch smoke before app artifact upload."
    $testResultPaths = @(
        'artifacts/freex-${{ matrix.runtime }}-portable-pdf-exporter-tests.trx',
        'artifacts/freex-${{ matrix.runtime }}-export-path-tests.trx'
    )
    foreach ($testResultPath in $testResultPaths) {
        Assert-ContainsText -Text $diagnosticsUpload -Needle $testResultPath -Message "macOS diagnostics upload must include $testResultPath."
        Assert-True -Condition ($appArtifactUpload.IndexOf($testResultPath, [System.StringComparison]::Ordinal) -lt 0) -Message "macOS app artifact upload must not include diagnostic test result $testResultPath."
    }

    Assert-ContainsText -Text $diagnosticsUpload -Needle 'artifacts/freex-${{ matrix.runtime }}-macos-app-diagnostics/**' -Message "macOS diagnostics upload must include app diagnostics emitted by hosted launch smoke."
    Assert-True -Condition ($appArtifactUpload.IndexOf('artifacts/freex-${{ matrix.runtime }}-macos-app-diagnostics/**', [System.StringComparison]::Ordinal) -lt 0) -Message "macOS app artifact upload must not include app diagnostics internals."
    Assert-ContainsText -Text $diagnosticsUpload -Needle "if: always()" -Message "macOS diagnostics upload must run even when earlier workflow steps fail."
    Assert-ContainsText -Text $diagnosticsUpload -Needle "if-no-files-found: warn" -Message "macOS diagnostics upload must warn, not fail, when optional diagnostics are missing."
}

function Test-SourceWiring {
    $sourceContracts = @(
        @{
            Path = "src\FreeX.App.Avalonia\Program.cs"
            Markers = @(
                "PackagingSmokeCommand.TryRun(args, Console.Out, Console.Error, out var smokeExitCode)",
                "MacOsLaunchSmokeOptions.TryParse(",
                "AvaloniaAppDiagnostics.Create(launchSmokeOptions?.DiagnosticsDirectory)",
                "diagnostics.RegisterUnhandledExceptionHandlers();",
                "diagnostics.RecordEvent(`"app_start`"",
                "App.StartupArguments = startupArguments;",
                "App.LaunchSmokeOptions = launchSmokeOptions;",
                "App.Diagnostics = diagnostics;",
                "diagnostics.RecordEvent(`"app_exit`"",
                "diagnostics.RecordCrash(ex, `"avalonia_startup`")",
                "BuildAvaloniaApp().StartWithClassicDesktopLifetime(startupArguments);"
            )
            OrderedPairs = @(
                @{
                    First = "PackagingSmokeCommand.TryRun(args, Console.Out, Console.Error, out var smokeExitCode)"
                    Second = "BuildAvaloniaApp().StartWithClassicDesktopLifetime(startupArguments);"
                }
            )
        },
        @{
            Path = "src\FreeX.App.Avalonia\App.cs"
            Markers = @(
                "internal static AvaloniaAppDiagnostics? Diagnostics { get; set; }",
                "Diagnostics?.RecordEvent(`"app_ready`"",
                "this.TryGetFeature<IActivatableLifetime>() is { } activatableLifetime",
                "args is not FileActivatedEventArgs fileArgs",
                "fileArgs.Kind != ActivationKind.File",
                "await mainWindow.OpenActivatedFilesAsync(fileArgs.Files);",
                "MacOsLaunchSmokeCoordinator.Start(mainWindow, launchSmokeOptions, Diagnostics);"
            )
            OrderedPairs = @()
        },
        @{
            Path = "src\FreeX.App.Avalonia\AvaloniaAppDiagnostics.cs"
            Markers = @(
                "internal sealed class AvaloniaAppDiagnostics",
                "AppDiagnosticsOptions.CreateDefault()",
                "new AppDiagnosticsFileStore(options)",
                "AppDiagnosticsMetadata.Create(",
                "AppDomain.CurrentDomain.UnhandledException +=",
                "TaskScheduler.UnobservedTaskException +=",
                "RecordEvent(string eventName",
                "RecordCrash(Exception exception, string source)",
                "AppDiagnosticsFileStore.SanitizeProperties(properties)"
            )
            OrderedPairs = @()
        },
        @{
            Path = "src\FreeX.App.Services\AppDiagnosticsFileStore.cs"
            Markers = @(
                "AllowedPropertyNames",
                '"grantKind"',
                '"payloadRedacted"'
            )
            OrderedPairs = @()
        },
        @{
            Path = "src\FreeX.App.Avalonia\WorkbookFileAccessService.cs"
            Markers = @(
                "Create(AvaloniaAppDiagnostics? diagnostics = null)",
                "new AvaloniaWorkbookFileAccessService(diagnostics)",
                "AvaloniaWorkbookFileAccessService(AvaloniaAppDiagnostics? diagnostics = null)",
                "MacOsSecurityScopedBookmarkKind = `"macos-security-scoped-bookmark`"",
                "storageItem is { CanBookmark: true }",
                "StorageItemMatchesPath(storageItem, path)",
                "storageItem.SaveBookmarkAsync()",
                "storageProvider.OpenFileBookmarkAsync(bookmark)",
                "PlatformPathIdentityComparer.Current.Equals(identity.LocalPath, resolvedPath)",
                "WorkbookFileAccessScope.FromDisposable(",
                "RecordIdentityEvent(`"bookmark_created`", grantKind: MacOsSecurityScopedBookmarkKind);",
                "RecordScopeEvent(`"scope_started`", grantKind: MacOsSecurityScopedBookmarkKind);",
                "RecordScopeEvent(`"scope_ended`", grantKind: MacOsSecurityScopedBookmarkKind)",
                "RecordFileAccessEvent(`"workbook_file_access_identity`", status, grantKind)",
                "RecordFileAccessEvent(`"workbook_file_access_scope`", status, grantKind)",
                '["scope"] = "workbook_file_access"',
                '["grantKind"] = string.IsNullOrWhiteSpace(grantKind) ? null : grantKind',
                '["payloadRedacted"] = string.IsNullOrWhiteSpace(grantKind) ? null : "true"'
            )
            OrderedPairs = @()
        },
        @{
            Path = "src\FreeX.App.Avalonia\MainWindow.cs"
            Markers = @(
                "private const string NativeWorkbookExtension = `".fxl`";",
                "using FreeX.Core.Calc;",
                "private readonly ScrollBar _verticalWorksheetScrollBar = new();",
                "private readonly ScrollBar _horizontalWorksheetScrollBar = new();",
                "private bool _isUpdatingWorksheetScrollBars;",
                "root.Children.Add(BuildWorksheetViewportChrome());",
                "_sheetScrollViewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;",
                "_sheetScrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;",
                "_verticalWorksheetScrollBar.ValueChanged += WorksheetScrollBar_ValueChanged;",
                "_horizontalWorksheetScrollBar.ValueChanged += WorksheetScrollBar_ValueChanged;",
                "WorkbookViewportScrollPlanner.Create(_session.ActiveSheet, _session.Viewport)",
                "ApplyWorksheetScrollAxis(_verticalWorksheetScrollBar, state.Vertical);",
                "ApplyWorksheetScrollAxis(_horizontalWorksheetScrollBar, state.Horizontal);",
                "WorkbookViewportScrollPlanner.CalculateViewportOrigin(",
                "_session.SetViewportOrigin(topRow, leftCol)",
                "public async Task OpenActivatedFilesAsync(IReadOnlyList<IStorageItem> files)",
                "WorkbookFileAccessServiceFactory.Create(App.Diagnostics)",
                "CreateColorPaletteFlyout(ColorPaletteTarget.Fill, includeClearFill: true)",
                "_formatPainterButton.Content = `"Format Painter`";",
                "AutomationProperties.SetAutomationId(_formatPainterButton, `"HomeFormatPainterButton`");",
                "AutomationProperties.SetHelpText(_formatPainterButton, `"Copy formatting from the selection and apply it to another range.`");",
                "_formatPainterMenuItem.Header = `"Format Painter`";",
                "_formatPainterMenuItem.Click += (_, _) => CaptureFormatPainterSource(persistent: false);",
                "editMenu.Items.Add(_formatPainterMenuItem);",
                "_formatPainterButton.IsEnabled = isIdle;",
                "_formatPainterMenuItem.IsEnabled = _formatPainterButton.IsEnabled;",
                "private void CaptureFormatPainterSource(bool persistent)",
                "_session.CaptureFormatPainterSource(persistent)",
                "private void ApplyFormatPainterAfterTargetSelection()",
                "_session.ApplyFormatPainterToSelectedRange()",
                "private void CancelFormatPainter()",
                "_session.CancelFormatPainter();",
                "HasFormatPainterButton: _formatPainterButton.Content?.ToString() == `"Format Painter`"",
                "HasNativeFormatPainterMenuItem: HasNativeMenuItem(_formatPainterMenuItem, `"Format Painter`", requireGesture: false)",
                "private readonly NativeMenuItem _workbookStatisticsMenuItem = new();",
                "private readonly NativeMenuItem _exportPdfMenuItem = new();",
                "_exportPdfMenuItem.Header = `"Export to PDF...`";",
                "_exportPdfMenuItem.Click += async (_, _) => await ExportActiveSheetPdfAsync();",
                "fileMenu.Items.Add(_exportPdfMenuItem);",
                "_exportPdfMenuItem.IsEnabled = isIdle && StorageProvider.CanSave;",
                "HasNativeExportPdfMenuItem: HasNativeMenuItem(_exportPdfMenuItem, `"Export to PDF...`", requireGesture: false)",
                "private async Task ExportActiveSheetPdfAsync()",
                "var exportPathPlan = ExportPathPlanner.Plan(requestedPath, ExportFileFormat.Pdf);",
                "ExportPathPlanner.ShouldPromptForNormalizedOverwrite(requestedPath, exportPathPlan, File.Exists)",
                "!await ConfirmNormalizedPdfOverwriteAsync(exportPathPlan.Path)",
                "path = exportPathPlan.Path;",
                "private async Task<bool> ConfirmNormalizedPdfOverwriteAsync(string normalizedPath)",
                "IsCancel = true,",
                "dialog.Opened += (_, _) => cancelButton.Focus();",
                "AutomationProperties.SetAutomationId(replaceButton, `"PdfExportOverwriteReplaceButton`")",
                "AutomationProperties.SetAutomationId(cancelButton, `"PdfExportOverwriteCancelButton`")",
                "PortablePdfDocumentExporter.Save(_session.Workbook, exportPlan, path)",
                "_workbookStatisticsMenuItem.Header = `"Workbook Statistics...`";",
                "_workbookStatisticsMenuItem.Gesture = new KeyGesture(Key.G, KeyModifiers.Control | KeyModifiers.Shift);",
                "_workbookStatisticsMenuItem.Click += async (_, _) => await ShowWorkbookStatisticsDialogAsync();",
                "fileMenu.Items.Add(_workbookStatisticsMenuItem);",
                "_workbookStatisticsMenuItem.IsEnabled = isIdle;",
                "HasNativeWorkbookStatisticsMenuItem: HasNativeMenuItem(_workbookStatisticsMenuItem, `"Workbook Statistics...`")",
                "e.Key == Key.G && e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift)",
                "private async Task ShowWorkbookStatisticsDialogAsync()",
                "WorkbookStatisticsService.GetStatistics(_session.Workbook)",
                "AutomationProperties.SetAutomationId(dialog, `"WorkbookStatisticsDialog`");",
                "AutomationProperties.SetAutomationId(okButton, `"WorkbookStatisticsOkButton`");",
                "AutomationProperties.SetAutomationId(statisticsBlock, `"WorkbookStatisticsSummary`");",
                "private static string FormatWorkbookStatistics(WorkbookStatistics statistics)",
                "Cells with data: {statistics.CellCount}",
                "Shapes and text boxes: {statistics.ShapeCount}",
                "Named ranges: {statistics.NamedRangeCount}",
                "private readonly NativeMenuItem _formatCellsMenuItem = new();",
                "_formatCellsMenuItem.Header = `"Format Cells...`";",
                "_formatCellsMenuItem.Gesture = new KeyGesture(Key.D1, KeyModifiers.Meta);",
                "_formatCellsMenuItem.Click += async (_, _) => await ShowFormatCells",
                "formatMenu.Items.Add(_formatCellsMenuItem);",
                "_formatCellsMenuItem.IsEnabled = isIdle;",
                "Key.D1",
                "HasOnlyCommandModifier(e.KeyModifiers)",
                "await ShowFormatCells",
                "private async Task ShowFormatCells",
                "FormatCellsCompactPlanner.TryPlan",
                "_session.ApplySelectedRangeCompactFormat(",
                "selection.Request.MergeCells",
                "`"FormatCellsCompactDialog`"",
                "`"FormatCellsNumberFormatBox`"",
                "`"FormatCellsHorizontalAlignmentBox`"",
                "`"FormatCellsVerticalAlignmentBox`"",
                "new(`"Justify`", CellHAlign.Justify)",
                "new(`"Distributed`", CellHAlign.Distributed)",
                "new(`"Justify`", CellVAlign.Justify)",
                "new(`"Distributed`", CellVAlign.Distributed)",
                "`"FormatCellsWrapTextBox`"",
                "`"FormatCellsMergeCellsBox`"",
                "`"FormatCellsFontSizeBox`"",
                "`"FormatCellsFontColorBox`"",
                "`"FormatCellsFillColorBox`"",
                "`"FormatCellsFillPatternStyleBox`"",
                "`"FormatCellsFillPatternColorBox`"",
                "`"FormatCellsBorderPresetBox`"",
                "`"FormatCellsBorderStyleBox`"",
                "`"FormatCellsBorderColorBox`"",
                "`"FormatCellsDoubleUnderlineBox`"",
                "`"FormatCellsShrinkToFitBox`"",
                "`"FormatCellsIndentLevelBox`"",
                "`"FormatCellsTextRotationBox`"",
                "`"FormatCellsFontNameBox`"",
                "`"FormatCellsNormalFontBox`"",
                "`"FormatCellsSuperscriptBox`"",
                "`"FormatCellsSubscriptBox`"",
                "`"FormatCellsLockedBox`"",
                "`"FormatCellsHiddenBox`"",
                "`"FormatCellsProtectionExplanationText`"",
                "Locking cells or hiding formulas has no effect until you protect the worksheet.",
                "var currentMergeCells = _session.IsSelectedRangeMerged;",
                "MergeCells: ReadChangedFormatCellsBool(currentMergeCells, mergeCellsBox)",
                "var normalStyle = CellStyle.Default;",
                "Bold: normalFont ? normalStyle.Bold : ReadChangedFormatCellsBool(_session.IsSelectedRangeStartBold, boldBox)",
                "FontName: normalFont ? normalStyle.FontName : ReadChangedFormatCellsText(currentFontName, fontNameBox)",
                "FontColor: normalFont ? normalStyle.FontColor : (fontColorBox.SelectedItem as FormatCellsColorChoice)?.Color",
                "SelectFormatCellsColor(fontColorBox, normal.FontColor)",
                "FillPatternStyle: clearFill ? null : ReadChangedFormatCellsValue(currentFillPatternStyle, fillPatternStyleBox)",
                "FillPatternColor: clearFill ? null : (fillPatternColorBox.SelectedItem as FormatCellsColorChoice)?.Color",
                "CreateFormatCellsField(`"Pattern style`", fillPatternStyleBox)",
                "CreateFormatCellsField(`"Pattern color`", fillPatternColorBox)",
                "private static IReadOnlyList<FormatCellsNullableChoice<CellFillPatternStyle>> CreateFormatCellsFillPatternStyleChoices()",
                "CellFillPatternStyle.DarkTrellis",
                "_autoSumButton.Content = `"AutoSum`";",
                "_autoSumButton.Flyout = CreateAutoSumFlyout();",
                "AutomationProperties.SetAutomationId(_autoSumButton, `"HomeAutoSumButton`");",
                "AutomationProperties.SetHelpText(_autoSumButton, `"Insert a formula using nearby numeric cells.`");",
                "_autoSumSumFlyoutItem.Click += (_, _) => InsertAutoSumFormula(`"SUM`");",
                "_autoSumAverageFlyoutItem.Click += (_, _) => InsertAutoSumFormula(`"AVERAGE`");",
                "_autoSumCountNumbersFlyoutItem.Click += (_, _) => InsertAutoSumFormula(`"COUNT`");",
                "_autoSumCountAllFlyoutItem.Click += (_, _) => InsertAutoSumFormula(`"COUNTA`");",
                "_autoSumMaxFlyoutItem.Click += (_, _) => InsertAutoSumFormula(`"MAX`");",
                "_autoSumMinFlyoutItem.Click += (_, _) => InsertAutoSumFormula(`"MIN`");",
                "_autoSumMenuItem.Header = `"AutoSum`";",
                "_autoSumMenuItem.Menu = CreateNativeAutoSumMenu();",
                "_autoSumSumMenuItem.Gesture = new KeyGesture(Key.OemPlus, KeyModifiers.Alt);",
                "editMenu.Items.Add(_autoSumMenuItem);",
                "_autoSumButton.IsEnabled = isIdle;",
                "_autoSumMenuItem.IsEnabled = _autoSumButton.IsEnabled;",
                "private MenuFlyout CreateAutoSumFlyout()",
                "private NativeMenu CreateNativeAutoSumMenu()",
                "private void InsertAutoSumFormula(string functionName)",
                "_session.InsertAutoSumFormula(functionName)",
                "private static bool IsAutoSumShortcut(KeyEventArgs args)",
                "HasAutoSumButton: _autoSumButton.Content?.ToString() == `"AutoSum`"",
                "HasNativeAutoSumMenuItem: HasNativeMenuItem(_autoSumMenuItem, `"AutoSum`", requireGesture: false)",
                "_fillCellsButton.Content = `"Fill Cells`";",
                "_fillCellsButton.Flyout = CreateFillCellsFlyout();",
                "AutomationProperties.SetAutomationId(_fillCellsButton, `"HomeFillCellsButton`");",
                "AutomationProperties.SetHelpText(_fillCellsButton, `"Copy the edge cells across the selected range.`");",
                "_fillDownFlyoutItem.Header = `"Down`";",
                "_fillDownFlyoutItem.Click += (_, _) => FillSelectedRange(FillCellsDirection.Down);",
                "_fillRightFlyoutItem.Header = `"Right`";",
                "_fillRightFlyoutItem.Click += (_, _) => FillSelectedRange(FillCellsDirection.Right);",
                "_fillUpFlyoutItem.Header = `"Up`";",
                "_fillUpFlyoutItem.Click += (_, _) => FillSelectedRange(FillCellsDirection.Up);",
                "_fillLeftFlyoutItem.Header = `"Left`";",
                "_fillLeftFlyoutItem.Click += (_, _) => FillSelectedRange(FillCellsDirection.Left);",
                "_fillCellsMenuItem.Header = `"Fill`";",
                "_fillCellsMenuItem.Menu = CreateNativeFillCellsMenu();",
                "_fillDownMenuItem.Gesture = new KeyGesture(Key.D, KeyModifiers.Control);",
                "_fillRightMenuItem.Gesture = new KeyGesture(Key.R, KeyModifiers.Control);",
                "editMenu.Items.Add(_fillCellsMenuItem);",
                "_fillDownFlyoutItem.IsEnabled = isIdle && _session.CanFillSelectedRange(FillCellsDirection.Down);",
                "_fillRightFlyoutItem.IsEnabled = isIdle && _session.CanFillSelectedRange(FillCellsDirection.Right);",
                "_fillUpFlyoutItem.IsEnabled = isIdle && _session.CanFillSelectedRange(FillCellsDirection.Up);",
                "_fillLeftFlyoutItem.IsEnabled = isIdle && _session.CanFillSelectedRange(FillCellsDirection.Left);",
                "_fillCellsMenuItem.IsEnabled = _fillCellsButton.IsEnabled;",
                "private MenuFlyout CreateFillCellsFlyout()",
                "private NativeMenu CreateNativeFillCellsMenu()",
                "private void FillSelectedRange(FillCellsDirection direction)",
                "_session.FillSelectedRange(direction)",
                "FormatFillCellsAction(direction)",
                "e.Key is Key.Z or Key.Y or Key.X or Key.C or Key.V or Key.A or Key.B or Key.D or Key.E or Key.I or Key.R or Key.U",
                "else if (e.Key == Key.D && HasOnlyControlModifier(e.KeyModifiers))",
                "else if (e.Key == Key.R && HasOnlyControlModifier(e.KeyModifiers))",
                "HasFillCellsButton: _fillCellsButton.Content?.ToString() == `"Fill Cells`"",
                "HasFillDownMenuItem: HasToolbarMenuItem(_fillDownFlyoutItem, `"Down`")",
                "HasFillRightMenuItem: HasToolbarMenuItem(_fillRightFlyoutItem, `"Right`")",
                "HasFillUpMenuItem: HasToolbarMenuItem(_fillUpFlyoutItem, `"Up`")",
                "HasFillLeftMenuItem: HasToolbarMenuItem(_fillLeftFlyoutItem, `"Left`")",
                "HasNativeFillCellsMenuItem: HasNativeMenuItem(_fillCellsMenuItem, `"Fill`", requireGesture: false)",
                "HasNativeFillDownMenuItem: HasNativeSubmenuItem(_fillCellsMenuItem.Menu, `"Down`")",
                "HasNativeFillRightMenuItem: HasNativeSubmenuItem(_fillCellsMenuItem.Menu, `"Right`")",
                "HasNativeFillUpMenuItem: HasNativeSubmenuItem(_fillCellsMenuItem.Menu, `"Up`")",
                "HasNativeFillLeftMenuItem: HasNativeSubmenuItem(_fillCellsMenuItem.Menu, `"Left`")",
                "_clearButton.Content = `"Clear`";",
                "AutomationProperties.SetAutomationId(_clearButton, `"HomeClearButton`");",
                "AutomationProperties.SetHelpText(_clearButton, `"Clear contents, formatting, comments, hyperlinks, or all cell state from the selected range.`");",
                "_clearButton.Flyout = CreateClearFlyout();",
                "_clearAllFlyoutItem.Header = `"Clear All`";",
                "_clearFormatsFlyoutItem.Header = `"Clear Formats`";",
                "_clearContentsFlyoutItem.Header = `"Clear Contents`";",
                "_clearCommentsFlyoutItem.Header = `"Clear Comments and Notes`";",
                "_clearHyperlinksFlyoutItem.Header = `"Clear Hyperlinks`";",
                "_clearMenuItem.Header = `"Clear`";",
                "_clearMenuItem.Menu = CreateNativeClearMenu();",
                "_clearAllMenuItem.Header = `"Clear All`";",
                "_clearAllMenuItem.Click += (_, _) => ClearSelectedRangeAll();",
                "_clearFormatsMenuItem.Header = `"Clear Formats`";",
                "_clearFormatsMenuItem.Click += (_, _) => ClearSelectedRangeFormats();",
                "_clearContentsMenuItem.Header = `"Clear Contents`";",
                "_clearContentsMenuItem.Click += (_, _) => ClearSelectedRangeContents();",
                "_clearCommentsMenuItem.Header = `"Clear Comments and Notes`";",
                "_clearCommentsMenuItem.Click += (_, _) => ClearSelectedRangeComments();",
                "_clearHyperlinksMenuItem.Header = `"Clear Hyperlinks`";",
                "_clearHyperlinksMenuItem.Click += (_, _) => ClearSelectedRangeHyperlinks();",
                "editMenu.Items.Add(_clearMenuItem);",
                "_clearButton.IsEnabled = isIdle;",
                "_clearMenuItem.IsEnabled = _clearButton.IsEnabled;",
                "private MenuFlyout CreateClearFlyout()",
                "private NativeMenu CreateNativeClearMenu()",
                "private void ClearSelectedRangeAll()",
                "_session.ClearSelectedRangeAll()",
                "private void ClearSelectedRangeFormats()",
                "_session.ClearSelectedRangeFormats()",
                "private void ClearSelectedRangeComments()",
                "_session.ClearSelectedRangeComments()",
                "private void ClearSelectedRangeHyperlinks()",
                "_session.ClearSelectedRangeHyperlinks()",
                "HasClearButton: _clearButton.Content?.ToString() == `"Clear`"",
                "HasClearAllMenuItem: HasToolbarMenuItem(_clearAllFlyoutItem, `"Clear All`")",
                "HasNativeClearMenuItem: HasNativeMenuItem(_clearMenuItem, `"Clear`", requireGesture: false)",
                "HasNativeClearHyperlinksMenuItem: HasNativeSubmenuItem(_clearMenuItem.Menu, `"Clear Hyperlinks`")",
                "_bordersButton.Flyout = CreateBorderPresetFlyout();",
                "AutomationProperties.SetAutomationId(_bordersButton, `"HomeBordersButton`");",
                "AutomationProperties.SetHelpText(_bordersButton, `"Apply or change borders on the selected cells.`");",
                "_bordersMenuItem.Header = `"Borders`";",
                "_bordersMenuItem.Menu = CreateNativeBorderPresetMenu();",
                "formatMenu.Items.Add(_bordersMenuItem);",
                "_bordersButton.IsEnabled = isIdle;",
                "_bordersMenuItem.IsEnabled = _bordersButton.IsEnabled;",
                "private MenuFlyout CreateBorderPresetFlyout()",
                "private MenuItem CreateBorderPresetMenuItem(CellBorderPreset preset)",
                "AutomationProperties.SetAutomationId(menuItem, `$`"HomeBorders{preset}MenuItem`");",
                "private NativeMenu CreateNativeBorderPresetMenu()",
                "private NativeMenuItem CreateNativeBorderPresetMenuItem(CellBorderPreset preset)",
                "private void ApplySelectedRangeBorderPreset(CellBorderPreset preset)",
                "_session.SetSelectedRangeBorderPreset(preset)",
                "HasBordersButton: _bordersButton.Content?.ToString() == `"Borders`"",
                "HasNativeBordersMenuItem: HasNativeMenuItem(_bordersMenuItem, `"Borders`", requireGesture: false)",
                "NativeBordersPresetCount: nativeBordersPresetCount",
                "_mergeAndCenterButton.Content = `"Merge & Center`";",
                "AutomationProperties.SetAutomationId(_mergeAndCenterButton, `"HomeMergeAndCenterButton`");",
                "AutomationProperties.SetHelpText(_mergeAndCenterButton, `"Merge and center the selected cells.`");",
                "_mergeAndCenterMenuItem.Header = `"Merge & Center`";",
                "_mergeAndCenterMenuItem.Click += async (_, _) => await MergeAndCenterSelectedRangeAsync();",
                "_unmergeCellsMenuItem.Header = `"Unmerge Cells`";",
                "_unmergeCellsMenuItem.Click += (_, _) => UnmergeSelectedRange();",
                "formatMenu.Items.Add(_mergeAndCenterMenuItem);",
                "formatMenu.Items.Add(_unmergeCellsMenuItem);",
                "_mergeAndCenterButton.IsEnabled = isIdle;",
                "_mergeAndCenterMenuItem.IsEnabled = _mergeAndCenterButton.IsEnabled;",
                "_unmergeCellsMenuItem.IsEnabled = isIdle && _session.IsSelectedRangeMerged;",
                "private async Task MergeAndCenterSelectedRangeAsync()",
                "_session.MergeAndCenterSelectedRange(contentResolution)",
                "ShowMergeCellsContentWarningDialogAsync(contentPlan)",
                "MergeCellsContentWarningDialog",
                "private void UnmergeSelectedRange()",
                "_session.UnmergeSelectedRange()",
                "HasMergeAndCenterButton: _mergeAndCenterButton.Content?.ToString() == `"Merge & Center`"",
                "AutomationProperties.SetAutomationId(_formulaBox, `"FormulaBox`");",
                "AutomationProperties.SetName(_formulaBox, `"Formula bar`");",
                "AutomationProperties.SetHelpText(_formulaBox, `"Edit the active cell value or formula.`");",
                "AutomationProperties.SetAutomationId(_statusText, `"StatusText`");",
                "AutomationProperties.SetName(_statusText, `"Status`");",
                "AutomationProperties.SetHelpText(_statusText, `"Shows the current workbook status.`");",
                "AutomationProperties.SetAutomationId(_cellAddressText, `"CellAddressText`");",
                "AutomationProperties.SetName(_cellAddressText, `"Cell address`");",
                "AutomationProperties.SetHelpText(_cellAddressText, `"Shows the active cell address.`");",
                "AutomationProperties.SetAutomationId(_selectionStatsText, `"SelectionStatsText`");",
                "AutomationProperties.SetName(_selectionStatsText, `"Selection statistics`");",
                "AutomationProperties.SetHelpText(_selectionStatsText, `"Shows statistics for the current selection.`");",
                "HasFormulaBoxAutomationName: string.Equals(AutomationProperties.GetName(_formulaBox), `"Formula bar`", StringComparison.Ordinal)",
                "HasFormulaBoxAutomationHelp: string.Equals(AutomationProperties.GetHelpText(_formulaBox), `"Edit the active cell value or formula.`", StringComparison.Ordinal)",
                "HasFormulaBoxAutomationId: string.Equals(AutomationProperties.GetAutomationId(_formulaBox), `"FormulaBox`", StringComparison.Ordinal)",
                "HasStatusTextAutomationName: string.Equals(AutomationProperties.GetName(_statusText), `"Status`", StringComparison.Ordinal)",
                "HasStatusTextAutomationHelp: string.Equals(AutomationProperties.GetHelpText(_statusText), `"Shows the current workbook status.`", StringComparison.Ordinal)",
                "HasStatusTextAutomationId: string.Equals(AutomationProperties.GetAutomationId(_statusText), `"StatusText`", StringComparison.Ordinal)",
                "HasStatusTextValue: !string.IsNullOrWhiteSpace(_statusText.Text)",
                "HasCellAddressAutomationName: string.Equals(AutomationProperties.GetName(_cellAddressText), `"Cell address`", StringComparison.Ordinal)",
                "HasCellAddressAutomationHelp: string.Equals(AutomationProperties.GetHelpText(_cellAddressText), `"Shows the active cell address.`", StringComparison.Ordinal)",
                "HasCellAddressAutomationId: string.Equals(AutomationProperties.GetAutomationId(_cellAddressText), `"CellAddressText`", StringComparison.Ordinal)",
                "HasSelectionStatsAutomationName: string.Equals(AutomationProperties.GetName(_selectionStatsText), `"Selection statistics`", StringComparison.Ordinal)",
                "HasSelectionStatsAutomationHelp: string.Equals(AutomationProperties.GetHelpText(_selectionStatsText), `"Shows statistics for the current selection.`", StringComparison.Ordinal)",
                "HasSelectionStatsAutomationId: string.Equals(AutomationProperties.GetAutomationId(_selectionStatsText), `"SelectionStatsText`", StringComparison.Ordinal)",
                "HasNativeMergeAndCenterMenuItem: HasNativeMenuItem(_mergeAndCenterMenuItem, `"Merge & Center`", requireGesture: false)",
                "HasNativeUnmergeCellsMenuItem: HasNativeMenuItem(_unmergeCellsMenuItem, `"Unmerge Cells`", requireGesture: false)",
                "CreateNativePasteSpecialMenu()",
                "PasteSpecialClipboardAtActiveCell(text, mode, options)",
                "CreatePasteCommentsMenuItem(`"Comments and Notes`")",
                "CreatePasteDataValidationMenuItem(`"Validation`")",
                "CreatePasteSpecialMenuItem(`"All Except Borders`", PasteCellsMode.All, new PasteSpecialOptions(ContentKind: PasteSpecialContentKind.AllExceptBorders))",
                "CreatePasteSpecialMenuItem(`"All Merging Conditional Formats`", PasteCellsMode.All, new PasteSpecialOptions(ContentKind: PasteSpecialContentKind.AllMergingConditionalFormats))",
                "CreatePasteColumnWidthsMenuItem(`"Column Widths`")",
                "CreatePasteSpecialMenuItem(`"Formulas and Number Formats`", PasteCellsMode.All, new PasteSpecialOptions(ContentKind: PasteSpecialContentKind.FormulasAndNumberFormats))",
                "CreatePasteSpecialMenuItem(`"Values and Number Formats`", PasteCellsMode.All, new PasteSpecialOptions(ContentKind: PasteSpecialContentKind.ValuesAndNumberFormats))",
                "CreatePasteSpecialMenuItem(`"Values and Source Formatting`", PasteCellsMode.All, new PasteSpecialOptions(ContentKind: PasteSpecialContentKind.ValuesAndSourceFormatting))",
                "CreatePasteSpecialMenuItem(`"Keep Source Column Widths`", PasteCellsMode.All, default, keepSourceColumnWidths: true)",
                "CreatePasteLinkMenuItem(`"Paste Link`")",
                "CreatePasteSpecialTextMenuItem(`"Text`")",
                "CreatePasteSpecialTextMenuItem(`"Unicode Text`")",
                "CreatePastePictureMenuItem(`"Picture`", linkedPicture: false)",
                "CreatePastePictureMenuItem(`"Linked Picture`", linkedPicture: true)",
                "CreateNativePasteCommentsMenuItem(`"Comments and Notes`")",
                "CreateNativePasteDataValidationMenuItem(`"Validation`")",
                "CreateNativePasteSpecialMenuItem(`"All Except Borders`", PasteCellsMode.All, new PasteSpecialOptions(ContentKind: PasteSpecialContentKind.AllExceptBorders))",
                "CreateNativePasteSpecialMenuItem(`"All Merging Conditional Formats`", PasteCellsMode.All, new PasteSpecialOptions(ContentKind: PasteSpecialContentKind.AllMergingConditionalFormats))",
                "CreateNativePasteColumnWidthsMenuItem(`"Column Widths`")",
                "CreateNativePasteSpecialMenuItem(`"Formulas and Number Formats`", PasteCellsMode.All, new PasteSpecialOptions(ContentKind: PasteSpecialContentKind.FormulasAndNumberFormats))",
                "CreateNativePasteSpecialMenuItem(`"Values and Number Formats`", PasteCellsMode.All, new PasteSpecialOptions(ContentKind: PasteSpecialContentKind.ValuesAndNumberFormats))",
                "CreateNativePasteSpecialMenuItem(`"Values and Source Formatting`", PasteCellsMode.All, new PasteSpecialOptions(ContentKind: PasteSpecialContentKind.ValuesAndSourceFormatting))",
                "CreateNativePasteSpecialMenuItem(`"Keep Source Column Widths`", PasteCellsMode.All, default, keepSourceColumnWidths: true)",
                "CreateNativePasteLinkMenuItem(`"Paste Link`")",
                "CreateNativePasteSpecialTextMenuItem(`"Text`")",
                "CreateNativePasteSpecialTextMenuItem(`"Unicode Text`")",
                "CreateNativePastePictureMenuItem(`"Picture`", linkedPicture: false)",
                "CreateNativePastePictureMenuItem(`"Linked Picture`", linkedPicture: true)",
                "private async Task PasteSpecialExternalTextFromClipboardAsync(string label)",
                "_session.PasteClipboardTextAtActiveCell(text, preserveText: true)",
                "_session.ShouldPreferExternalClipboardImage(text)",
                "private async Task<bool> TryPasteClipboardImageAsync(IClipboard clipboard, CellAddress destination)",
                "await clipboard.TryGetBitmapAsync()",
                "bitmap.Save(stream)",
                "_session.PasteClipboardImageAtActiveCell(pngBytes, pixelWidth, pixelHeight)",
                "internal async Task<bool> TryPasteLaunchSmokeClipboardImageAsync()",
                "return await TryPasteClipboardImageAsync(clipboard, _session.ActiveCell);",
                "private async Task PastePictureFromClipboardAsync(string label, bool linkedPicture)",
                "_session.PastePictureFromClipboardAtActiveCell(text, linkedPicture)",
                "private async Task PasteColumnWidthsFromClipboardAsync(string label)",
                "_session.PasteColumnWidthsFromClipboardAtActiveCell(text)",
                "private async Task PasteCommentsFromClipboardAsync(string label)",
                "_session.PasteCommentsFromClipboardAtActiveCell(text)",
                "private async Task PasteDataValidationFromClipboardAsync(string label)",
                "_session.PasteDataValidationFromClipboardAtActiveCell(text)",
                "private async Task PasteLinkFromClipboardAsync(string label)",
                "_session.PasteLinkFromClipboardAtActiveCell(text)",
                "HasNativePasteSpecialCommentsMenuItem: HasNativeSubmenuItem(_pasteSpecialMenuItem.Menu, `"Comments and Notes`")",
                "HasNativePasteSpecialValidationMenuItem: HasNativeSubmenuItem(_pasteSpecialMenuItem.Menu, `"Validation`")",
                "HasNativePasteSpecialAllExceptBordersMenuItem: HasNativeSubmenuItem(_pasteSpecialMenuItem.Menu, `"All Except Borders`")",
                "HasNativePasteSpecialAllMergingConditionalFormatsMenuItem: HasNativeSubmenuItem(_pasteSpecialMenuItem.Menu, `"All Merging Conditional Formats`")",
                "HasNativePasteSpecialColumnWidthsMenuItem: HasNativeSubmenuItem(_pasteSpecialMenuItem.Menu, `"Column Widths`")",
                "HasNativePasteSpecialFormulasAndNumberFormatsMenuItem: HasNativeSubmenuItem(_pasteSpecialMenuItem.Menu, `"Formulas and Number Formats`")",
                "HasNativePasteSpecialValuesAndNumberFormatsMenuItem: HasNativeSubmenuItem(_pasteSpecialMenuItem.Menu, `"Values and Number Formats`")",
                "HasNativePasteSpecialValuesAndSourceFormattingMenuItem: HasNativeSubmenuItem(_pasteSpecialMenuItem.Menu, `"Values and Source Formatting`")",
                "HasNativePasteSpecialKeepSourceColumnWidthsMenuItem: HasNativeSubmenuItem(_pasteSpecialMenuItem.Menu, `"Keep Source Column Widths`")",
                "HasNativePasteSpecialPasteLinkMenuItem: HasNativeSubmenuItem(_pasteSpecialMenuItem.Menu, `"Paste Link`")",
                "HasNativePasteSpecialTextMenuItem: HasNativeSubmenuItem(_pasteSpecialMenuItem.Menu, `"Text`")",
                "HasNativePasteSpecialUnicodeTextMenuItem: HasNativeSubmenuItem(_pasteSpecialMenuItem.Menu, `"Unicode Text`")",
                "HasNativePasteSpecialPictureMenuItem: HasNativeSubmenuItem(_pasteSpecialMenuItem.Menu, `"Picture`")",
                "HasNativePasteSpecialLinkedPictureMenuItem: HasNativeSubmenuItem(_pasteSpecialMenuItem.Menu, `"Linked Picture`")",
                "private static bool HasNativeSubmenuItem(NativeMenu? menu, string expectedHeader)",
                "CellColorPalettePlanner.BuildDefaultSwatches()",
                "DrawingObjectRenderPlanner.Plan(viewport)",
                "CreateSelectableDrawingObjectVisual(renderPlan, width, height)",
                "AutomationProperties.SetAutomationId(container, `$`"DrawingObject{drawingObject.Kind}{drawingObject.Id:N}`");",
                "AutomationProperties.SetHelpText(container, `"Selects this drawing object preview in the workbook viewport.`");",
                "AutomationProperties.SetItemStatus(container, selected ? `"Selected`" : `"Not selected`");",
                "container.PointerPressed += (_, args) =>",
                "if (args.Key is Key.Enter or Key.Space)",
                "CreateSelectedDrawingObjectAdorner()",
                "ClearSelectedDrawingObject();",
                "CreateDrawingObjectVisual(renderPlan, width, height)",
                "CreateDrawingCellRangeSnapshotVisual(renderPlan, width, height)",
                "CreateDrawingImageSourceRect(crop)",
                "TryCreateDrawingBitmap(imageBytes, out var bitmap)",
                "AddStyledCellBorderOverlay(content, style);",
                "private static bool HasVisibleCellBorder(CellStyle? style)",
                "private readonly RecentFilesStore _recentFiles = RecentFilesStore.Load();",
                "_newWorkbookMenuItem.Click += (_, _) => CreateNewWorkbook();",
                "_openRecentMenuItem.Header = `"Open Recent`";",
                "_openRecentMenuItem.Menu = CreateNativeOpenRecentMenu(isIdle: true);",
                "fileMenu.Items.Add(_openRecentMenuItem);",
                "RefreshNativeOpenRecentMenu(isIdle);",
                "LocalFilePath.TryNormalize(candidate, out var normalizedCandidate)",
                "Directory.Exists(normalizedCandidate)",
                "File.Exists(normalizedCandidate)",
                "_session.TryResolveOpenTarget(normalizedCandidate, out var target, out unsupportedMessage)",
                "path = target!.Path;",
                "_selectAllMenuItem.Header = `"Select All`";",
                "_selectAllMenuItem.Gesture = new KeyGesture(Key.A, KeyModifiers.Meta);",
                "_selectAllMenuItem.Click += (_, _) => SelectCurrentRegionOrAll();",
                "editMenu.Items.Add(_selectAllMenuItem);",
                "_selectAllMenuItem.IsEnabled = isIdle;",
                "private void SelectCurrentRegionOrAll()",
                "var range = _session.SelectCurrentRegionOrAll();",
                "private readonly NativeMenuItem _findMenuItem = new();",
                "private readonly NativeMenuItem _findNextMenuItem = new();",
                "private readonly NativeMenuItem _replaceMenuItem = new();",
                "private readonly NativeMenuItem _goToMenuItem = new();",
                "private readonly NativeMenuItem _goToSpecialMenuItem = new();",
                "private readonly NativeMenuItem _sortAscendingMenuItem = new();",
                "private readonly NativeMenuItem _sortDescendingMenuItem = new();",
                "private readonly NativeMenuItem _flashFillMenuItem = new();",
                "private readonly NativeMenuItem _advancedFilterMenuItem = new();",
                "private readonly NativeMenuItem _dataValidationMenuItem = new();",
                "private readonly NativeMenuItem _subtotalMenuItem = new();",
                "private readonly NativeMenuItem _whatIfAnalysisMenuItem = new();",
                "private readonly NativeMenuItem _goalSeekMenuItem = new();",
                "private readonly NativeMenuItem _dataTableMenuItem = new();",
                "private readonly NativeMenuItem _scenarioManagerMenuItem = new();",
                "private readonly NativeMenuItem _forecastSheetMenuItem = new();",
                "private readonly NativeMenuItem _reviewSummaryMenuItem = new();",
                "private readonly NativeMenuItem _checkAccessibilityMenuItem = new();",
                "private readonly NativeMenuItem _nextNoteMenuItem = new();",
                "private readonly NativeMenuItem _previousNoteMenuItem = new();",
                "private readonly NativeMenuItem _nextCommentMenuItem = new();",
                "private readonly NativeMenuItem _previousCommentMenuItem = new();",
                "private enum FindDialogAction",
                "private sealed record FindDialogResult(",
                "FindOptions Options,",
                "bool MatchCase,",
                "bool MatchEntireCell);",
                "private enum ReplaceDialogAction",
                "private sealed record ReplaceDialogResult(",
                "ReplaceDialogAction Action,",
                "StyleDiff? ReplacementFormat);",
                "private sealed record FindOptionsControls(",
                "private sealed record GoToSpecialDialogResult(GoToSpecialKind Kind, GoToSpecialOptions Options);",
                "private sealed record GoToSpecialChoice(GoToSpecialKind Kind, string Label)",
                "_findMenuItem.Header = `"Find...`";",
                "_findMenuItem.Gesture = new KeyGesture(Key.F, KeyModifiers.Meta);",
                "_findMenuItem.Click += async (_, _) => await ShowFindDialogAsync();",
                "_findNextMenuItem.Header = `"Find Next`";",
                "_findNextMenuItem.Gesture = new KeyGesture(Key.G, KeyModifiers.Meta);",
                "_findNextMenuItem.Click += (_, _) => FindNext();",
                "_replaceMenuItem.Header = `"Replace...`";",
                "_replaceMenuItem.Gesture = new KeyGesture(Key.H, KeyModifiers.Control);",
                "_replaceMenuItem.Click += async (_, _) => await ShowReplaceDialogAsync();",
                "_goToMenuItem.Header = `"Go To...`";",
                "_goToMenuItem.Gesture = new KeyGesture(Key.G, KeyModifiers.Control);",
                "_goToMenuItem.Click += async (_, _) => await ShowGoToDialogAsync();",
                "_goToSpecialMenuItem.Header = `"Go To Special...`";",
                "_goToSpecialMenuItem.Click += async (_, _) => await ShowGoToSpecialDialogAsync();",
                "_sortAscendingMenuItem.Header = `"Sort A to Z`";",
                "_sortAscendingMenuItem.Click += (_, _) => SortSelectedRange(ascending: true);",
                "_sortDescendingMenuItem.Header = `"Sort Z to A`";",
                "_sortDescendingMenuItem.Click += (_, _) => SortSelectedRange(ascending: false);",
                "_flashFillMenuItem.Header = `"Flash Fill`";",
                "_flashFillMenuItem.Gesture = new KeyGesture(Key.E, KeyModifiers.Control);",
                "_flashFillMenuItem.Click += (_, _) => FlashFillSelectedRange();",
                "_advancedFilterMenuItem.Header = `"Advanced Filter...`";",
                "_advancedFilterMenuItem.Click += async (_, _) => await ShowAdvancedFilterDialogAsync();",
                "_removeDuplicatesMenuItem.Header = `"Remove Duplicates...`";",
                "_removeDuplicatesMenuItem.Click += async (_, _) => await ShowRemoveDuplicatesDialogAsync();",
                "_subtotalMenuItem.Header = `"Subtotal...`";",
                "_subtotalMenuItem.Click += async (_, _) => await ShowSubtotalDialogAsync();",
                "_dataValidationMenuItem.Header = `"Data Validation...`";",
                "_dataValidationMenuItem.Click += async (_, _) => await ShowDataValidationDialogAsync();",
                "_whatIfAnalysisMenuItem.Header = `"What-If Analysis`";",
                "_whatIfAnalysisMenuItem.Menu = CreateNativeWhatIfAnalysisMenu();",
                "_goalSeekMenuItem.Header = `"Goal Seek...`";",
                "_scenarioManagerMenuItem.Header = `"Scenario Manager...`";",
                "_dataTableMenuItem.Header = `"Data Table...`";",
                "_forecastSheetMenuItem.Header = `"Forecast Sheet...`";",
                "_reviewSummaryMenuItem.Header = `"Review Summary...`";",
                "_checkAccessibilityMenuItem.Header = `"Check Accessibility...`";",
                "_nextNoteMenuItem.Header = `"Next Note`";",
                "_previousNoteMenuItem.Header = `"Previous Note`";",
                "_nextCommentMenuItem.Header = `"Next Comment`";",
                "_previousCommentMenuItem.Header = `"Previous Comment`";",
                "var dataMenu = new NativeMenu();",
                "dataMenu.Items.Add(_sortAscendingMenuItem);",
                "dataMenu.Items.Add(_sortDescendingMenuItem);",
                "dataMenu.Items.Add(_flashFillMenuItem);",
                "dataMenu.Items.Add(_advancedFilterMenuItem);",
                "dataMenu.Items.Add(_removeDuplicatesMenuItem);",
                "dataMenu.Items.Add(_subtotalMenuItem);",
                "dataMenu.Items.Add(_dataValidationMenuItem);",
                "dataMenu.Items.Add(_whatIfAnalysisMenuItem);",
                "dataMenu.Items.Add(_forecastSheetMenuItem);",
                "var reviewMenu = new NativeMenu();",
                "reviewMenu.Items.Add(_reviewSummaryMenuItem);",
                "reviewMenu.Items.Add(_checkAccessibilityMenuItem);",
                "reviewMenu.Items.Add(_nextNoteMenuItem);",
                "reviewMenu.Items.Add(_previousNoteMenuItem);",
                "reviewMenu.Items.Add(_nextCommentMenuItem);",
                "reviewMenu.Items.Add(_previousCommentMenuItem);",
                "Header = `"Data`",",
                "Menu = dataMenu,",
                "Header = `"Review`",",
                "Menu = reviewMenu,",
                "var hasNativeDataMenu = _nativeMenu?.Items.OfType<NativeMenuItem>().Any(item =>",
                "string.Equals(item.Header?.ToString(), `"Data`", StringComparison.Ordinal)",
                "var hasNativeReviewMenu = _nativeMenu?.Items.OfType<NativeMenuItem>().Any(item =>",
                "string.Equals(item.Header?.ToString(), `"Review`", StringComparison.Ordinal)",
                "HasNativeDataMenu: hasNativeDataMenu",
                "HasNativeReviewMenu: hasNativeReviewMenu",
                "editMenu.Items.Add(_findMenuItem);",
                "editMenu.Items.Add(_findNextMenuItem);",
                "editMenu.Items.Add(_replaceMenuItem);",
                "editMenu.Items.Add(_goToMenuItem);",
                "editMenu.Items.Add(_goToSpecialMenuItem);",
                "_findMenuItem.IsEnabled = isIdle;",
                "_findNextMenuItem.IsEnabled = isIdle && !string.IsNullOrWhiteSpace(_session.LastFindText);",
                "_replaceMenuItem.IsEnabled = isIdle;",
                "_goToMenuItem.IsEnabled = isIdle;",
                "_goToSpecialMenuItem.IsEnabled = isIdle;",
                "_sortAscendingMenuItem.IsEnabled = isIdle && _session.CanSortSelectedRange;",
                "_sortDescendingMenuItem.IsEnabled = isIdle && _session.CanSortSelectedRange;",
                "_flashFillMenuItem.IsEnabled = isIdle;",
                "_advancedFilterMenuItem.IsEnabled = isIdle;",
                "_removeDuplicatesMenuItem.IsEnabled = isIdle && _session.SelectedRange.RowCount > 1;",
                "_subtotalMenuItem.IsEnabled = isIdle && _session.SelectedRange.RowCount > 1 && _session.SelectedRange.ColCount > 1;",
                "_dataValidationMenuItem.IsEnabled = isIdle;",
                "_whatIfAnalysisMenuItem.IsEnabled = isIdle;",
                "_goalSeekMenuItem.IsEnabled = isIdle;",
                "_dataTableMenuItem.IsEnabled = isIdle && _session.SelectedRange.RowCount > 1 && _session.SelectedRange.ColCount > 1;",
                "_scenarioManagerMenuItem.IsEnabled = isIdle;",
                "_forecastSheetMenuItem.IsEnabled = isIdle;",
                "_reviewSummaryMenuItem.IsEnabled = isIdle;",
                "_checkAccessibilityMenuItem.IsEnabled = isIdle;",
                "_nextNoteMenuItem.IsEnabled = isIdle;",
                "_previousNoteMenuItem.IsEnabled = isIdle;",
                "_nextCommentMenuItem.IsEnabled = isIdle;",
                "_previousCommentMenuItem.IsEnabled = isIdle;",
                "private void SortSelectedRange(bool ascending)",
                "_session.SortSelectedRange(ascending)",
                "private void FlashFillSelectedRange()",
                "_session.FlashFillSelectedRange()",
                "e.Key == Key.E && HasOnlyControlModifier(e.KeyModifiers)",
                "private async Task ShowSubtotalDialogAsync()",
                "private async Task<SubtotalDialogResult?> ShowSubtotalInputDialogAsync()",
                "_session.ExecuteSubtotalOptions(selection.Options!)",
                "_session.RemoveSelectedRangeSubtotals()",
                "new SubtotalInputOptions(",
                "AutomationProperties.SetAutomationId(dialog, `"SubtotalCompactDialog`");",
                "AutomationProperties.SetAutomationId(groupColumnBox, `"SubtotalGroupColumnBox`");",
                "AutomationProperties.SetAutomationId(functionBox, `"SubtotalFunctionBox`");",
                "AutomationProperties.SetAutomationId(columnsPanel, `"SubtotalColumnsPanel`");",
                "AutomationProperties.SetAutomationId(removeAllButton, `"SubtotalRemoveAllButton`");",
                "private NativeMenu CreateNativeWhatIfAnalysisMenu()",
                "menu.Items.Add(_goalSeekMenuItem);",
                "menu.Items.Add(_scenarioManagerMenuItem);",
                "menu.Items.Add(_dataTableMenuItem);",
                "HasNativeFindMenuItem: HasNativeMenuItem(_findMenuItem, `"Find...`")",
                "HasNativeFindNextMenuItem: HasNativeMenuItem(_findNextMenuItem, `"Find Next`")",
                "HasNativeReplaceMenuItem: HasNativeMenuItem(_replaceMenuItem, `"Replace...`")",
                "HasNativeGoToMenuItem: HasNativeMenuItem(_goToMenuItem, `"Go To...`")",
                "HasNativeSortAscendingMenuItem: HasNativeMenuItem(_sortAscendingMenuItem, `"Sort A to Z`", requireGesture: false)",
                "HasNativeSortDescendingMenuItem: HasNativeMenuItem(_sortDescendingMenuItem, `"Sort Z to A`", requireGesture: false)",
                "HasNativeFlashFillMenuItem: HasNativeMenuItem(_flashFillMenuItem, `"Flash Fill`")",
                "HasNativeAdvancedFilterMenuItem: HasNativeMenuItem(_advancedFilterMenuItem, `"Advanced Filter...`", requireGesture: false)",
                "HasNativeRemoveDuplicatesMenuItem: HasNativeMenuItem(_removeDuplicatesMenuItem, `"Remove Duplicates...`", requireGesture: false)",
                "HasNativeSubtotalMenuItem: HasNativeMenuItem(_subtotalMenuItem, `"Subtotal...`", requireGesture: false)",
                "HasNativeDataValidationPreviewMenuItem: HasNativeMenuItem(_dataValidationPreviewMenuItem, `"Data Validation Preview...`", requireGesture: false)",
                "HasNativeDataValidationMenuItem: HasNativeMenuItem(_dataValidationMenuItem, `"Data Validation...`", requireGesture: false)",
                "HasNativeWhatIfAnalysisMenuItem: HasNativeMenuItem(_whatIfAnalysisMenuItem, `"What-If Analysis`", requireGesture: false)",
                "HasNativeGoalSeekMenuItem: HasNativeSubmenuItem(_whatIfAnalysisMenuItem.Menu, `"Goal Seek...`")",
                "HasNativeDataTableMenuItem: HasNativeSubmenuItem(_whatIfAnalysisMenuItem.Menu, `"Data Table...`")",
                "HasNativeScenarioManagerMenuItem: HasNativeSubmenuItem(_whatIfAnalysisMenuItem.Menu, `"Scenario Manager...`")",
                "HasNativeForecastSheetMenuItem: HasNativeMenuItem(_forecastSheetMenuItem, `"Forecast Sheet...`", requireGesture: false)",
                "HasNativeReviewSummaryMenuItem: HasNativeMenuItem(_reviewSummaryMenuItem, `"Review Summary...`", requireGesture: false)",
                "HasNativeCheckAccessibilityMenuItem: HasNativeMenuItem(_checkAccessibilityMenuItem, `"Check Accessibility...`", requireGesture: false)",
                "HasNativeNextNoteMenuItem: HasNativeMenuItem(_nextNoteMenuItem, `"Next Note`", requireGesture: false)",
                "HasNativePreviousNoteMenuItem: HasNativeMenuItem(_previousNoteMenuItem, `"Previous Note`", requireGesture: false)",
                "HasNativeNextCommentMenuItem: HasNativeMenuItem(_nextCommentMenuItem, `"Next Comment`", requireGesture: false)",
                "HasNativePreviousCommentMenuItem: HasNativeMenuItem(_previousCommentMenuItem, `"Previous Comment`", requireGesture: false)",
                "HasNativeFormatCellsMenuItem:",
                "private async Task ShowFindDialogAsync()",
                "private async Task<FindDialogResult?> ShowFindInputDialogAsync(Action<FindDialogSmokeProbe>? launchSmokeProbe = null)",
                "private async Task ShowFindAllResultsDialogAsync(string searchText, IReadOnlyList<WorkbookFindAllMatch> matches)",
                "private void NavigateToFindAllMatch(WorkbookFindAllMatch match)",
                "FindOptions? options = null,",
                "private async Task ShowReplaceDialogAsync()",
                "private async Task<ReplaceDialogResult?> ShowReplaceInputDialogAsync(Action<ReplaceDialogSmokeProbe>? launchSmokeProbe = null)",
                "private async Task ShowGoToDialogAsync()",
                "private async Task ShowGoToSpecialDialogAsync()",
                "private async Task<GoToSpecialDialogResult?> ShowGoToSpecialInputDialogAsync(Action<GoToSpecialDialogSmokeProbe>? launchSmokeProbe = null)",
                "private static GoToSpecialChoice[] CreateGoToSpecialChoices()",
                "private bool SelectGoToSpecial(GoToSpecialKind kind, GoToSpecialOptions? options = null)",
                "private async Task<string?> ShowSingleInputDialogAsync(",
                "`"FindTextBox`"",
                "`"FindNextButton`"",
                "`"FindAllButton`"",
                "CreateFindOptionsControls(`"Find`", defaultLookInIndex: 0)",
                "CreateFindReplaceFormatButton(`"FindChooseFormatFromCellButton`", `"Choose From Cell`")",
                "CreateFindReplaceFormatButton(`"FindClearFormatButton`", `"Clear Format`")",
                "CreateFindReplaceFormatRow(`"Find format`",",
                "_session.CreateFormatDiffFromActiveCell()",
                "{automationPrefix}WithinBox",
                "{automationPrefix}SearchBox",
                "{automationPrefix}LookInBox",
                "{automationPrefix}MatchCaseBox",
                "{automationPrefix}MatchEntireCellBox",
                "`"FindAllResultsStatusText`"",
                "`"FindAllResultsList`"",
                "`"FindAllCloseButton`"",
                "`"ReplaceFindTextBox`"",
                "`"ReplaceWithTextBox`"",
                "`"ReplaceButton`"",
                "`"ReplaceAllButton`"",
                "CreateFindOptionsControls(`"Replace`", defaultLookInIndex: 1)",
                "CreateFindReplaceFormatButton(`"ReplaceFindChooseFormatFromCellButton`", `"Choose From Cell`")",
                "CreateFindReplaceFormatButton(`"ReplaceFindClearFormatButton`", `"Clear Format`")",
                "CreateFindReplaceFormatButton(`"ReplaceWithChooseFormatFromCellButton`", `"Choose From Cell`")",
                "CreateFindReplaceFormatButton(`"ReplaceWithClearFormatButton`", `"Clear Format`")",
                "CreateFindReplaceFormatRow(`"Replace format`",",
                "`"GoToReferenceBox`"",
                "`"GoToSpecialKindBox`"",
                "`"GoToSpecialNumbersBox`"",
                "`"GoToSpecialTextBox`"",
                "`"GoToSpecialLogicalsBox`"",
                "`"GoToSpecialErrorsBox`"",
                "`"GoToSpecialOkButton`"",
                "private FindOptions CreateFindOptions(FindOptionsControls controls, StyleDiff? requiredFormat = null)",
                "CreateFindOptions(optionsControls, findFormat)",
                "RequiredFormat: requiredFormat);",
                "private static FindOptionsControls CreateFindOptionsControls(string automationPrefix, int defaultLookInIndex)",
                "private static Button CreateFindReplaceFormatButton(string automationId, string content)",
                "private static StackPanel CreateFindReplaceFormatRow(string label, Button chooseButton, Button clearButton)",
                "private static void UpdateFindReplaceFormatState(StyleDiff? format, Button chooseButton, Button clearButton)",
                "FindLookIn.Formulas",
                "FindLookIn.Notes",
                "FindLookIn.Comments",
                "var result = _session.FindNext(searchText, options, matchCase, matchEntireCell);",
                "var result = _session.FindAll(search.FindText, search.Options, search.MatchCase, search.MatchEntireCell);",
                "await ShowFindAllResultsDialogAsync(search.FindText, result.Matches);",
                "var result = _session.GoToCell(match.Address);",
                "replacement.Action == ReplaceDialogAction.ReplaceAll",
                "replacement.Options,",
                "replacement.MatchCase,",
                "replacement.MatchEntireCell",
                "replacement.ReplacementFormat",
                "_session.ReplaceNextValue(",
                "_session.ReplaceAllValues(",
                "var result = _session.GoToReference(reference);",
                "var result = _session.GoToSpecial(kind, options);",
                "result.SelectedRanges.Count == 1",
                "e.Key == Key.F5",
                "args.Key == Key.Oem1 && args.KeyModifiers == KeyModifiers.Alt;",
                "SelectGoToSpecial(GoToSpecialKind.VisibleCellsOnly);",
                "e.Key == Key.F && HasOnlyCommandModifier(e.KeyModifiers)",
                "e.Key == Key.G && e.KeyModifiers == KeyModifiers.Meta",
                "e.Key == Key.H && HasOnlyControlModifier(e.KeyModifiers)",
                "e.Key == Key.G && HasOnlyControlModifier(e.KeyModifiers)",
                "e.Key is Key.Z or Key.Y or Key.X or Key.C or Key.V or Key.A",
                "else if (e.Key == Key.A && HasOnlyCommandModifier(e.KeyModifiers))",
                "private static bool HasCommandAndShiftModifiers(KeyModifiers modifiers)",
                "private enum ShellFocusRegion",
                "private static readonly ShellFocusRegion[] ShellFocusCycle",
                "ShellFocusRegion.Worksheet",
                "ShellFocusRegion.Toolbar",
                "ShellFocusRegion.FormulaBar",
                "ShellFocusRegion.SheetTabs",
                "ShellFocusRegion.StatusBar",
                "_sheetGridHost.Focusable = true;",
                "AutomationProperties.SetName(_sheetGridHost, `"Worksheet`");",
                "_zoomText.Focusable = true;",
                "AutomationProperties.SetName(_zoomText, `"Zoom`");",
                "private static bool IsShellFocusCycleKey(KeyEventArgs args)",
                "args.Key == Key.F6 &&",
                "if (IsShellFocusCycleKey(e))",
                "CycleShellFocus(reverse: e.KeyModifiers == KeyModifiers.Shift);",
                "private void CycleShellFocus(bool reverse)",
                "private static ShellFocusRegion GetNextShellFocusRegion(ShellFocusRegion current, bool reverse)",
                "private ShellFocusRegion GetCurrentShellFocusRegion()",
                "private bool FocusShellRegion(ShellFocusRegion region)",
                "ShellFocusRegion.Toolbar => FocusFirstEnabledToolbarControl()",
                "ShellFocusRegion.FormulaBar => FocusControl(_formulaBox)",
                "ShellFocusRegion.SheetTabs => FocusActiveSheetTab()",
                "ShellFocusRegion.StatusBar => FocusControl(_zoomText)",
                "_ => FocusControl(_sheetGridHost)",
                "private bool FocusFirstEnabledToolbarControl()",
                "private IReadOnlyList<Control> GetToolbarFocusTargets()",
                "_openButton,",
                "_alignRightButton",
                "private bool IsAnyToolbarControlFocused()",
                "private bool IsAnySheetTabFocused()",
                "private static bool FocusControl(Control control)",
                "e.Key == Key.PageUp && HasCommandAndShiftModifiers(e.KeyModifiers)",
                "SelectAdjacentVisibleSheetFromKeyboard(direction: -1, selectRange: true)",
                "e.Key == Key.PageDown && HasCommandAndShiftModifiers(e.KeyModifiers)",
                "SelectAdjacentVisibleSheetFromKeyboard(direction: 1, selectRange: true)",
                "e.Key == Key.PageUp && HasOnlyCommandModifier(e.KeyModifiers)",
                "SelectAdjacentVisibleSheetFromKeyboard(direction: -1, selectRange: false)",
                "e.Key == Key.PageDown && HasOnlyCommandModifier(e.KeyModifiers)",
                "SelectAdjacentVisibleSheetFromKeyboard(direction: 1, selectRange: false)",
                "private NativeMenu CreateNativeOpenRecentMenu(bool isIdle)",
                "Header = `"(No Recent Workbooks)`"",
                "OpenRecentWorkbookMenuPlanner.Create(",
                "_recentFiles.Entries",
                "File.Exists",
                "path => _session.TryResolveOpenTarget(path, out var target, out _) ? target!.Path : null",
                "plan.ItemCount == 0",
                "foreach (var entry in plan.Items)",
                "var fileAccessIdentity = entry.FileAccessIdentity;",
                "Header = entry.Header",
                "private async Task OpenRecentWorkbookAsync(",
                "WorkbookFileAccessIdentity? fileAccessIdentity = null",
                "if (!_session.TryResolveOpenTarget(path, fileAccessIdentity, out var target, out _)",
                "await OpenWorkbookPathAsync(target.Path, target.FileAccessIdentity);",
                "private void RecordStartupRecentWorkbook(StartupWorkbookLoadResult source)",
                "private void RecordRecentWorkbook(string path, WorkbookFileAccessIdentity? fileAccessIdentity = null)",
                "_recentFiles.AddOrUpdate(target.Path, fileAccessIdentity ?? target.FileAccessIdentity);",
                "RecordRecentWorkbook(target.Path, target.FileAccessIdentity);",
                "RecordRecentWorkbook(target.Path, fileAccessIdentity);",
                "_closeWorkbookMenuItem.Click += async (_, _) => await CloseWorkbookAsync();",
                "fileMenu.Items.Add(_newWorkbookMenuItem);",
                "fileMenu.Items.Add(_closeWorkbookMenuItem);",
                "_sessionFactory.CreateNew(viewportHeight, viewportWidth, includeObjects: true)",
                "RefreshViewportSizeForZoom();",
                "Closing += MainWindow_Closing;",
                "private async Task CloseWorkbookAsync()",
                "ConfirmDirtyWorkbookCloseAsync(`"Close Workbook`", `"Discard and Close`")",
                "ResetToNewWorkbook(`"Closed workbook.`");",
                "private async void MainWindow_Closing(object? sender, WindowClosingEventArgs e)",
                "ConfirmDirtyWorkbookCloseAsync(`"Close FreeX`", `"Discard and Close`")",
                "private async Task TryQuitApplicationAsync()",
                "ConfirmDirtyWorkbookCloseAsync(`"Quit FreeX`", `"Discard and Quit`")",
                "_allowCloseWithoutDirtyPrompt = true;",
                "private async Task<bool> ConfirmDirtyWorkbookCloseAsync(string title, string discardButtonText)",
                "await SaveCurrentWorkbookAsync();",
                "private async Task<DirtyWorkbookCloseChoice> ShowDirtyWorkbookCloseDialogAsync(",
                "AutomationProperties.SetAutomationId(saveButton, `"DirtyWorkbookSaveButton`");",
                "AutomationProperties.SetAutomationId(discardButton, `"DirtyWorkbookDiscardButton`");",
                "AutomationProperties.SetAutomationId(cancelButton, `"DirtyWorkbookCancelButton`");",
                "_newSheetButton.Click += (_, _) => AddNewSheet();",
                "_newSheetMenuItem.Click += (_, _) => AddNewSheet();",
                "_renameSheetMenuItem.Click += async (_, _) => await RenameActiveSheetAsync();",
                "_duplicateSheetMenuItem.Click += (_, _) => DuplicateActiveSheet();",
                "_moveSheetLeftMenuItem.Click += (_, _) => MoveActiveSheetLeft();",
                "_moveSheetRightMenuItem.Click += (_, _) => MoveActiveSheetRight();",
                "_tabColorMenuItem.Header = `"Tab Color`";",
                "_tabColorMenuItem.Menu = CreateNativeSheetTabColorMenu();",
                "_selectAllSheetsMenuItem.Header = `"Select All Sheets`";",
                "_selectAllSheetsMenuItem.Click += (_, _) => SelectAllVisibleSheets();",
                "_ungroupSheetsMenuItem.Header = `"Ungroup Sheets`";",
                "_ungroupSheetsMenuItem.Click += (_, _) => UngroupSheets();",
                "sheetMenu.Items.Add(_tabColorMenuItem);",
                "sheetMenu.Items.Add(_selectAllSheetsMenuItem);",
                "sheetMenu.Items.Add(_ungroupSheetsMenuItem);",
                "_tabColorMenuItem.IsEnabled = isIdle;",
                "_selectAllSheetsMenuItem.IsEnabled = isIdle && _session.SheetTabs.Count > 1;",
                "_ungroupSheetsMenuItem.IsEnabled = isIdle && _session.IsWorkbookGrouped;",
                "private string FormatWindowWorkbookTitle()",
                "? `$`"{_session.DisplayName} [Group]`"",
                "var isGroupedTab = tab.IsGrouped && _session.IsWorkbookGrouped;",
                "tab.TabColor is { } tabColor ? Brush(tabColor) : Brushes.Transparent",
                "private const string SheetTabContextHelpText = `"Selects this sheet. Press F6 repeatedly to reach sheet tabs, use arrow keys to switch sheets, or right-click/press Shift+F10 for sheet tab options.`";",
                "Focusable = true,",
                "Tag = tab.Id,",
                "button.ContextMenu = CreateSheetTabContextMenu(tab);",
                "button.DoubleTapped += async (_, args) => await RenameSheetFromTabAsync(tab.Id, args);",
                "button.KeyDown += (_, args) => HandleSheetTabKeyDown(tab.Id, button, args);",
                "AutomationProperties.SetName(button, tab.Name);",
                "AutomationProperties.SetHelpText(button, SheetTabContextHelpText);",
                "private ContextMenu CreateSheetTabContextMenu(WorkbookSheetTab tab)",
                "ItemsSource = CreateSheetTabContextMenuItems(tab, isIdle, sheetTabIndex).ToArray()",
                "private IEnumerable<Control> CreateSheetTabContextMenuItems(WorkbookSheetTab tab, bool isIdle, int sheetTabIndex)",
                "CreateSheetTabContextMenuItem(tab, `"Rename...`", async () => await RenameActiveSheetAsync(), isIdle)",
                "CreateSheetTabContextMenuItem(tab, `"Insert Sheet`", AddNewSheet, isIdle)",
                "CreateSheetTabContextMenuItem(tab, `"Duplicate`", DuplicateActiveSheet, isIdle)",
                "CreateSheetTabContextMenuItem(tab, `"Delete Sheet`", DeleteActiveSheet, isIdle)",
                "CreateSheetTabContextMenuItem(tab, `"Hide`", HideActiveSheet, isIdle && _session.SheetTabs.Count > 1)",
                "CreateSheetTabContextMenuItem(tab, `"Unhide...`", async () => await UnhideSheetAsync(), isIdle && _session.HiddenSheets.Count > 0)",
                "CreateSheetTabColorContextMenuItem(tab, isIdle)",
                "CreateSheetTabContextMenuItem(tab, `"Select All Sheets`", SelectAllVisibleSheets, isIdle && _session.SheetTabs.Count > 1)",
                "CreateSheetTabContextMenuItem(tab, `"Ungroup Sheets`", UngroupSheets, isIdle && _session.IsWorkbookGrouped)",
                "CreateSheetTabContextMenuItem(tab, `"Move Left`", MoveActiveSheetLeft, isIdle && sheetTabIndex > 0)",
                "CreateSheetTabContextMenuItem(",
                "private bool SelectSheetForContextCommand(SheetId sheetId)",
                "private async Task RenameSheetFromTabAsync(SheetId sheetId, TappedEventArgs args)",
                "await RenameActiveSheetAsync();",
                "private void HandleSheetTabKeyDown(SheetId sheetId, Button button, KeyEventArgs args)",
                "NavigateSheetTabFromKeyboard(sheetId, args);",
                "private void OpenSheetTabContextMenuFromKeyboard(SheetId sheetId, Button button, KeyEventArgs args)",
                "private static bool IsSheetTabContextMenuKey(KeyEventArgs args)",
                "args.Key == Key.Apps",
                "args.Key == Key.F10 && args.KeyModifiers == KeyModifiers.Shift",
                "contextMenu.Opened -= SheetTabContextMenu_Opened;",
                "contextMenu.Opened += SheetTabContextMenu_Opened;",
                "contextMenu.Open(button);",
                "private void NavigateSheetTabFromKeyboard(SheetId sheetId, KeyEventArgs args)",
                "args.KeyModifiers != KeyModifiers.None",
                "Key.Left => GetAdjacentSheetTabId(sheetId, direction: -1)",
                "Key.Right => GetAdjacentSheetTabId(sheetId, direction: 1)",
                "Key.Home => GetEdgeSheetTabId(first: true)",
                "Key.End => GetEdgeSheetTabId(first: false)",
                "private bool SelectAdjacentVisibleSheetFromKeyboard(int direction, bool selectRange)",
                "private void SelectSheetTabFromKeyboard(SheetId sheetId, bool selectRange)",
                "private SheetId? GetAdjacentSheetTabId(SheetId sheetId, int direction)",
                "Math.Clamp(targetIndex, 0, _session.SheetTabs.Count - 1)",
                "private SheetId? GetEdgeSheetTabId(bool first)",
                "private bool FocusActiveSheetTab()",
                "private bool FocusSheetTab(SheetId sheetId)",
                "private static void SheetTabContextMenu_Opened(object? sender, RoutedEventArgs args)",
                "FocusFirstEnabledSheetTabMenuItem(items);",
                "private static void FocusFirstEnabledSheetTabMenuItem(IEnumerable<Control> items)",
                "foreach (var item in items)",
                "item is MenuItem { IsEnabled: true } menuItem",
                "menuItem.Focus();",
                "private Button? FindSheetTabButton(SheetId sheetId)",
                "button.Tag is SheetId tag &&",
                "tag == sheetId",
                "private bool HasSheetTabButton(Func<Button, bool> predicate)",
                "HasFocusableSheetTab: HasSheetTabButton(button => button.Focusable)",
                "HasFocusableActiveSheetTab: FindSheetTabButton(_session.ActiveSheet.Id)?.Focusable == true",
                "HasShellFocusCycleTargets: _sheetGridHost.Focusable &&",
                "GetToolbarFocusTargets().Any(control => control.Focusable) &&",
                "_formulaBox.Focusable &&",
                "_zoomText.Focusable",
                "HasSheetTabContextKeyboardHelp: HasSheetTabButton(button =>",
                "string.Equals(AutomationProperties.GetHelpText(button), SheetTabContextHelpText, StringComparison.Ordinal))",
                "HasSheetTabContextRenameMenuItem: HasSheetTabContextMenuItem(`"Rename...`")",
                "HasSheetTabContextTabColorMenuItem: HasSheetTabContextMenuItem(`"Tab Color`")",
                "HasSheetTabContextNoColorMenuItem: HasSheetTabContextSubmenuItem(`"Tab Color`", `"No Color`")",
                "HasSheetTabContextSelectAllSheetsMenuItem: HasSheetTabContextMenuItem(`"Select All Sheets`")",
                "HasSheetTabContextUngroupSheetsMenuItem: HasSheetTabContextMenuItem(`"Ungroup Sheets`")",
                "private NativeMenu CreateNativeSheetTabColorMenu()",
                "var clearColorItem = new NativeMenuItem { Header = `"No Color`" };",
                "clearColorItem.Click += (_, _) => ApplyActiveSheetTabColor(null);",
                "private NativeMenuItem CreateNativeSheetTabColorSwatchMenuItem(CellColorSwatch swatch)",
                "ApplyActiveSheetTabColor(swatch.Color);",
                "private void ApplyActiveSheetTabColor(CellColor? color)",
                "var result = _session.SetActiveSheetTabColor(color);",
                "private void SelectAllVisibleSheets()",
                "var changed = _session.SelectAllVisibleSheets();",
                "private void UngroupSheets()",
                "var changed = _session.UngroupSheets();",
                "_hideSheetMenuItem.Click += (_, _) => HideActiveSheet();",
                "_unhideSheetMenuItem.Click += async (_, _) => await UnhideSheetAsync();",
                "_deleteSheetMenuItem.Click += (_, _) => DeleteActiveSheet();",
                "_showGridlinesMenuItem.Header = `"Gridlines`";",
                "_showGridlinesMenuItem.ToggleType = MenuItemToggleType.CheckBox;",
                "_showGridlinesMenuItem.Click += (_, _) => ToggleShowGridlines();",
                "_showHeadingsMenuItem.Header = `"Headings`";",
                "_showHeadingsMenuItem.ToggleType = MenuItemToggleType.CheckBox;",
                "_showHeadingsMenuItem.Click += (_, _) => ToggleShowHeadings();",
                "_zoomInMenuItem.Header = `"Zoom In`";",
                "_zoomOutMenuItem.Header = `"Zoom Out`";",
                "_zoom100MenuItem.Header = `"100%`";",
                "_zoomToSelectionMenuItem.Header = `"Zoom to Selection`";",
                "_zoomInMenuItem.Click += (_, _) => ZoomIn();",
                "_zoomOutMenuItem.Click += (_, _) => ZoomOut();",
                "_zoom100MenuItem.Click += (_, _) => ZoomTo100Percent();",
                "_zoomToSelectionMenuItem.Click += (_, _) => ZoomToSelection();",
                "viewMenu.Items.Add(_showGridlinesMenuItem);",
                "viewMenu.Items.Add(_showHeadingsMenuItem);",
                "viewMenu.Items.Add(_zoomInMenuItem);",
                "viewMenu.Items.Add(_zoomOutMenuItem);",
                "viewMenu.Items.Add(_zoom100MenuItem);",
                "viewMenu.Items.Add(_zoomToSelectionMenuItem);",
                "_freezePanesMenuItem.Header = `"Freeze Panes`";",
                "_freezePanesMenuItem.Click += (_, _) => FreezePanesAtActiveCell();",
                "_freezeTopRowMenuItem.Header = `"Freeze Top Row`";",
                "_freezeFirstColumnMenuItem.Header = `"Freeze First Column`";",
                "_unfreezePanesMenuItem.Header = `"Unfreeze Panes`";",
                "viewMenu.Items.Add(_freezePanesMenuItem);",
                "viewMenu.Items.Add(_freezeTopRowMenuItem);",
                "viewMenu.Items.Add(_freezeFirstColumnMenuItem);",
                "viewMenu.Items.Add(_unfreezePanesMenuItem);",
                "private void ApplyFreezePaneCommand(Func<WorkbookCellEditResult> execute, string successAction, string failureMessage)",
                "_session.FreezePanesAtActiveCell",
                "_showFormulasMenuItem.ToggleType = MenuItemToggleType.CheckBox;",
                "_showFormulasMenuItem.Click += (_, _) => ToggleShowFormulas();",
                "Header = `"View`"",
                "Header = `"Sheet`"",
                "var result = _session.AddSheet();",
                "var result = _session.RenameActiveSheet(newName);",
                "private async Task<string?> ShowRenameSheetDialogAsync(string currentName)",
                "AutomationProperties.SetAutomationId(nameBox, `"RenameSheetNameBox`");",
                "var validationError = _session.Workbook.ValidateSheetName(proposedName, _session.ActiveSheet.Id);",
                "button.PointerPressed += (_, args) => SelectSheetFromPointer(tab.Id, args);",
                "private void SelectSheetFromPointer(SheetId sheetId, PointerPressedEventArgs args)",
                "if (!args.GetCurrentPoint(this).Properties.IsLeftButtonPressed)",
                "var selectRange = modifiers.HasFlag(KeyModifiers.Shift);",
                "var toggle = modifiers.HasFlag(KeyModifiers.Control) || modifiers.HasFlag(KeyModifiers.Meta);",
                "args.Handled = true;",
                "_session.SelectSheetFromTab(sheetId, selectRange, toggle)",
                "var result = _session.DuplicateActiveSheet();",
                "var result = _session.MoveActiveSheetLeft();",
                "var result = _session.MoveActiveSheetRight();",
                "var result = _session.HideActiveSheet();",
                "private async Task UnhideSheetAsync()",
                "private async Task<WorkbookHiddenSheet?> ShowUnhideSheetDialogAsync(IReadOnlyList<WorkbookHiddenSheet> hiddenSheets)",
                "AutomationProperties.SetAutomationId(sheetBox, `"UnhideSheetList`");",
                "var result = _session.UnhideSheet(sheet.Id);",
                "var result = _session.DeleteActiveSheet();",
                "private void ToggleShowGridlines()",
                "var result = _session.SetShowGridlines(showGridlines);",
                "private void ToggleShowHeadings()",
                "var result = _session.SetShowHeadings(showHeadings);",
                "private void ZoomIn() =>",
                "ApplyZoomPercent(_session.ZoomPercent + ZoomStepPercent, `"Zoom In failed.`")",
                "private void ZoomOut() =>",
                "ApplyZoomPercent(_session.ZoomPercent - ZoomStepPercent, `"Zoom Out failed.`")",
                "private void ZoomTo100Percent() =>",
                "ApplyZoomPercent(100, `"100% Zoom failed.`")",
                "private void ZoomToSelection()",
                "private void ApplyZoomPercent(int zoomPercent, string errorMessage)",
                "var result = _session.SetZoomPercent(zoomPercent);",
                "private int CalculateZoomToSelectionPercent()",
                "_zoomText.Text = FormatZoomPercent(_session.ZoomPercent);",
                "private void FreezePanesAtActiveCell()",
                "private void FreezeTopRow()",
                "private void FreezeFirstColumn()",
                "private void UnfreezePanes()",
                "private void ToggleShowFormulas()",
                "var result = _session.SetShowFormulas(showFormulas);",
                "e.Key == Key.F11 && e.KeyModifiers == KeyModifiers.Shift",
                "_helpOnlineMenuItem.Click += async (_, _) => await OpenExternalHelpLinkAsync(AppHelpInfo.HelpUrl, `"Help Online`");",
                "_sendFeedbackMenuItem.Click += async (_, _) => await OpenExternalHelpLinkAsync(AppHelpInfo.FeedbackUrl, `"Send Feedback`");",
                "_checkForUpdatesMenuItem.Click += async (_, _) => await OpenExternalHelpLinkAsync(AppHelpInfo.LatestReleaseUrl, `"Check for Updates`");",
                "_aboutMenuItem.Click += async (_, _) => await ShowAboutDialogAsync();",
                "_legalNoticesMenuItem.Click += async (_, _) => await ShowLegalNoticesDialogAsync();",
                "_minimizeWindowMenuItem.Gesture = new KeyGesture(Key.M, KeyModifiers.Meta);",
                "_minimizeWindowMenuItem.Click += (_, _) => WindowState = WindowState.Minimized;",
                "_zoomWindowMenuItem.Header = `"Zoom`";",
                "_bringAllToFrontMenuItem.Header = `"Bring All to Front`";",
                "Header = `"Window`"",
                "Header = `"Help`"",
                "TopLevel.GetTopLevel(this)?.Launcher",
                "AppHelpInfo.BuildAboutText(versionText, PlatformAboutSummary)",
                "LegalNoticeProvider.GetDocuments().Select(document =>",
                "internal MacOsLaunchSmokeSnapshot CreateLaunchSmokeSnapshot()",
                "HasNativeWindowMenu: hasNativeWindowMenu",
                "HasNativeMinimizeWindowMenuItem: HasNativeMenuItem(_minimizeWindowMenuItem, `"Minimize`")",
                "HasNativeZoomWindowMenuItem: HasNativeMenuItem(_zoomWindowMenuItem, `"Zoom`", requireGesture: false)",
                "HasNativeBringAllToFrontMenuItem: HasNativeMenuItem(_bringAllToFrontMenuItem, `"Bring All to Front`", requireGesture: false)",
                "ExternalImageClipboardPictureCount: externalImageClipboardPictures.Length",
                "ExternalImageClipboardPicturePngByteCount: externalImageClipboardPictures.Sum(static picture => picture.ImageBytes!.Length)",
                "var showHeadings = _session.ActiveSheet.ShowHeadings;",
                "var zoomFactor = GetActiveZoomFactor();",
                "showGridlines ? GridLine : Brushes.Transparent",
                "AddGridChild(grid, CreateCell(cell, row, col, zoomFactor, colWidth, rowHeight)",
                "CalculateDisplayedGridWidth(viewport, showHeadings, zoomFactor)",
                "CalculateDisplayedGridHeight(viewport, showHeadings, zoomFactor)",
                "fontSize * zoomFactor",
                "displayHeight / zoomFactor",
                "private double GetActiveZoomFactor()",
                "CellTextOrientationLayoutPlanner.HasTextOrientation(textRotation)",
                "CreateOrientedCellContent(",
                "CellTextOrientationLayoutPlanner.CalculateLayout(",
                "CreateTextRotationTransform(layout.TransformAngle)",
                "textBlock.RenderTransformOrigin = new RelativePoint(0, 0, RelativeUnit.Relative);",
                "textBlock.RenderTransform = transform;",
                "Canvas.SetLeft(textBlock, layout.TextPoint.X);",
                "Canvas.SetTop(textBlock, layout.TextPoint.Y);",
                "CellTextOrientationLayoutPlanner.PrepareDisplayText(text, textRotation)",
                "CellTextOrientationLayoutPlanner.NormalizeRotationForDisplay(textRotation)",
                "private static RotateTransform? CreateTextRotationTransform(double transformAngle)",
                "new RotateTransform(transformAngle)"
            )
            OrderedPairs = @()
        },
        @{
            Path = "src\FreeX.App.Services\PortablePdfDocumentExporter.cs"
            Markers = @(
                "public static class PortablePdfDocumentExporter",
                "PortablePdfPageContentPlanner.CreatePlan(workbook, request)",
                "/Encoding /WinAnsiEncoding",
                "EncodeWinAnsiHexText(normalized)",
                "private static byte EncodeWinAnsiByte(char ch)",
                "built-in Helvetica/WinAnsi set"
            )
            OrderedPairs = @()
        },
        @{
            Path = "src\FreeX.Core.Calc\CellTextOrientationLayoutPlanner.cs"
            Markers = @(
                "public readonly record struct CellTextLayoutPoint",
                "public readonly record struct CellTextLayoutRect",
                "public readonly record struct CellTextOrientationLayout",
                "public static class CellTextOrientationLayoutPlanner",
                "public static bool HasTextOrientation(int textRotation)",
                "public static bool IsStackedTextRotation(int textRotation)",
                "public static int NormalizeRotationForDisplay(int textRotation)",
                "public static string PrepareDisplayText(string text, int textRotation)",
                "public static CellTextOrientationLayout CalculateLayout(",
                "public static bool ShouldClip("
            )
            OrderedPairs = @()
        },
        @{
            Path = "src\FreeX.App.Avalonia\MacOsLaunchSmoke.cs"
            Markers = @(
                "public const string Argument = `"--macos-launch-smoke`";",
                "public const string DiagnosticsDirectoryArgument = `"--macos-launch-smoke-diagnostics-dir`";",
                "public const string VerifyImageClipboardPasteArgument = `"--macos-launch-smoke-verify-image-clipboard`";",
                "public const string VerifyLiveCommandKeysArgument = `"--macos-launch-smoke-verify-live-command-keys`";",
                "startupArguments = filteredArguments.ToArray();",
                "verifyImageClipboardPaste = true;",
                "verifyLiveCommandKeys = true;",
                "diagnosticsDirectory = args[++index];",
                "diagnosticsDirectory);",
                "RunAsync(mainWindow, options, diagnostics)",
                "diagnostics?.RecordEvent(`"macos_launch_smoke`"",
                "diagnostics?.RecordCrash(ex, `"macos_launch_smoke`")",
                "app_diagnostics_directory_configured={FormatBool(appDiagnosticsConfigured)}",
                "await mainWindow.TryPasteLaunchSmokeClipboardImageAsync();",
                "liveCommandKeyEvidence = mainWindow.BeginLaunchSmokeLiveCommandKeyProbe();",
                "liveCommandKeyEvidence.IsPassed",
                "HasFindDirectRouteSourceGuard &&",
                "HasPageUpDirectRouteSourceGuard &&",
                "HasPageDownDirectRouteSourceGuard",
                "HasMainWindowDirectCommandRouteSourceSupport(",
                "IsPassed(snapshot, options, initialExternalImageClipboardPictureCount)",
                "HasExternalImageClipboardPasteEvidence(",
                "HasNativeFileMenu &&",
                "HasNativeEditMenu &&",
                "HasNativeDataMenu &&",
                "HasNativeReviewMenu &&",
                "HasNativeFormatMenu &&",
                "HasNativeViewMenu &&",
                "HasNativeSheetMenu &&",
                "HasNativeWindowMenu &&",
                "HasNativeHelpMenu &&",
                "HasNativeNewWorkbookMenuItem &&",
                "HasNativeOpenRecentMenuItem &&",
                "NativeOpenRecentItemCount > 0 &&",
                "HasNativeSelectAllMenuItem &&",
                "HasNativeFindMenuItem &&",
                "HasNativeFindNextMenuItem &&",
                "HasNativeReplaceMenuItem &&",
                "HasNativeGoToMenuItem &&",
                "HasNativeGoToSpecialMenuItem &&",
                "HasNativeSortAscendingMenuItem &&",
                "HasNativeSortDescendingMenuItem &&",
                "HasNativeFlashFillMenuItem &&",
                "HasNativeAdvancedFilterMenuItem &&",
                "HasNativeRemoveDuplicatesMenuItem &&",
                "HasNativeSubtotalMenuItem &&",
                "HasNativeDataValidationPreviewMenuItem &&",
                "HasNativeDataValidationMenuItem &&",
                "HasNativeWhatIfAnalysisMenuItem &&",
                "HasNativeGoalSeekMenuItem &&",
                "HasNativeDataTableMenuItem &&",
                "HasNativeScenarioManagerMenuItem &&",
                "HasNativeForecastSheetMenuItem &&",
                "HasNativeReviewSummaryMenuItem &&",
                "HasNativeCheckAccessibilityMenuItem &&",
                "HasNativeNextNoteMenuItem &&",
                "HasNativePreviousNoteMenuItem &&",
                "HasNativeNextCommentMenuItem &&",
                "HasNativePreviousCommentMenuItem &&",
                "HasNativeFormatCellsMenuItem &&",
                "HasFormatCellsDialog &&",
                "HasFormatCellsDialogTabStrip &&",
                "HasFormatCellsDialogDefaultNumberTab &&",
                "HasFormatCellsDialogNumberControls &&",
                "HasFormatCellsDialogActionButtons &&",
                "HasFormatCellsDialogCompactLayout &&",
                "HasFormatCellsDialogClosedWithoutAccept",
                "HasNativeCloseWorkbookMenuItem &&",
                "HasNativeRenameSheetMenuItem &&",
                "HasNativeMoveSheetLeftMenuItem &&",
                "HasNativeMoveSheetRightMenuItem &&",
                "HasNativeTabColorMenuItem &&",
                "HasNativeClearTabColorMenuItem &&",
                "NativeTabColorSwatchCount == CellColorPalettePlanner.BuildDefaultSwatches().Count",
                "HasFormatPainterButton &&",
                "HasAutoSumButton &&",
                "HasAutoSumSumMenuItem &&",
                "HasAutoSumAverageMenuItem &&",
                "HasAutoSumCountNumbersMenuItem &&",
                "HasAutoSumCountAllMenuItem &&",
                "HasAutoSumMaxMenuItem &&",
                "HasAutoSumMinMenuItem &&",
                "HasFillCellsButton &&",
                "HasFillDownMenuItem &&",
                "HasFillRightMenuItem &&",
                "HasFillUpMenuItem &&",
                "HasFillLeftMenuItem &&",
                "HasClearButton &&",
                "HasClearAllMenuItem &&",
                "HasClearFormatsMenuItem &&",
                "HasClearContentsMenuItem &&",
                "HasClearCommentsMenuItem &&",
                "HasClearHyperlinksMenuItem &&",
                "HasBordersButton &&",
                "HasWrapTextButton &&",
                "HasMergeAndCenterButton &&",
                "HasAccessibilitySmokeEvidence &&",
                "HasFormulaBoxAutomationName &&",
                "HasFormulaBoxAutomationHelp &&",
                "HasFormulaBoxAutomationId &&",
                "HasStatusTextAutomationName &&",
                "HasStatusTextAutomationHelp &&",
                "HasStatusTextAutomationId &&",
                "HasStatusTextValue &&",
                "HasCellAddressAutomationName &&",
                "HasCellAddressAutomationHelp &&",
                "HasCellAddressAutomationId &&",
                "HasSelectionStatsAutomationName &&",
                "HasSelectionStatsAutomationHelp &&",
                "HasSelectionStatsAutomationId",
                "HasFocusableSheetTab &&",
                "HasFocusableActiveSheetTab &&",
                "HasShellFocusCycleTargets &&",
                "HasSheetTabContextKeyboardHelp &&",
                "HasSheetTabContextRenameMenuItem &&",
                "HasSheetTabContextTabColorMenuItem &&",
                "HasSheetTabContextNoColorMenuItem &&",
                "HasSheetTabContextSelectAllSheetsMenuItem &&",
                "HasSheetTabContextUngroupSheetsMenuItem &&",
                "HasNativeSelectAllSheetsMenuItem &&",
                "HasNativeUngroupSheetsMenuItem &&",
                "HasNativeHideSheetMenuItem &&",
                "HasNativeUnhideSheetMenuItem &&",
                "HasNativeDeleteSheetMenuItem &&",
                "HasNativeShowGridlinesMenuItem &&",
                "HasNativeShowHeadingsMenuItem &&",
                "HasNativeZoomInMenuItem &&",
                "HasNativeZoomOutMenuItem &&",
                "HasNativeZoom100MenuItem &&",
                "HasNativeZoomToSelectionMenuItem &&",
                "HasNativeFreezePanesMenuItem &&",
                "HasNativeFreezeTopRowMenuItem &&",
                "HasNativeFreezeFirstColumnMenuItem &&",
                "HasNativeUnfreezePanesMenuItem &&",
                "HasNativeShowFormulasMenuItem &&",
                "HasNativeMinimizeWindowMenuItem &&",
                "HasNativeZoomWindowMenuItem &&",
                "HasNativeBringAllToFrontMenuItem &&",
                "HasNativeFormatPainterMenuItem &&",
                "HasNativePasteSpecialCommentsMenuItem &&",
                "HasNativePasteSpecialValidationMenuItem &&",
                "HasNativePasteSpecialAllExceptBordersMenuItem &&",
                "HasNativePasteSpecialAllMergingConditionalFormatsMenuItem &&",
                "HasNativePasteSpecialColumnWidthsMenuItem &&",
                "HasNativePasteSpecialFormulasAndNumberFormatsMenuItem &&",
                "HasNativePasteSpecialValuesAndNumberFormatsMenuItem &&",
                "HasNativePasteSpecialValuesAndSourceFormattingMenuItem &&",
                "HasNativePasteSpecialKeepSourceColumnWidthsMenuItem &&",
                "HasNativePasteSpecialPasteLinkMenuItem &&",
                "HasNativePasteSpecialTextMenuItem &&",
                "HasNativePasteSpecialUnicodeTextMenuItem &&",
                "HasNativePasteSpecialPictureMenuItem &&",
                "HasNativePasteSpecialLinkedPictureMenuItem &&",
                "HasNativeAutoSumMenuItem &&",
                "HasNativeAutoSumSumMenuItem &&",
                "HasNativeAutoSumAverageMenuItem &&",
                "HasNativeAutoSumCountNumbersMenuItem &&",
                "HasNativeAutoSumCountAllMenuItem &&",
                "HasNativeAutoSumMaxMenuItem &&",
                "HasNativeAutoSumMinMenuItem &&",
                "HasNativeFillCellsMenuItem &&",
                "HasNativeFillDownMenuItem &&",
                "HasNativeFillRightMenuItem &&",
                "HasNativeFillUpMenuItem &&",
                "HasNativeFillLeftMenuItem &&",
                "HasNativeClearMenuItem &&",
                "HasNativeClearAllMenuItem &&",
                "HasNativeClearFormatsMenuItem &&",
                "HasNativeClearContentsMenuItem &&",
                "HasNativeClearCommentsMenuItem &&",
                "HasNativeClearHyperlinksMenuItem &&",
                "HasNativeBordersMenuItem &&",
                "NativeBordersPresetCount == Enum.GetValues<CellBorderPreset>().Length",
                "HasNativeMergeAndCenterMenuItem &&",
                "HasNativeUnmergeCellsMenuItem &&",
                "HasNativeExportPdfMenuItem &&",
                "HasNativeShareWorkbookMenuItem &&",
                "HasNativeWorkbookStatisticsMenuItem &&",
                "native_new_workbook_menu_item=",
                "cmd_find_direct_route_source_guard=",
                "cmd_page_up_direct_route_source_guard=",
                "cmd_page_down_direct_route_source_guard=",
                "live_command_key_smoke_required=",
                "live_command_key_smoke=",
                "live_command_key_smoke_attempted=",
                "live_command_key_smoke_ready=",
                "live_cmd_select_all_received=",
                "live_cmd_select_all_state_changed=",
                "live_cmd_bold_received=",
                "live_cmd_bold_state_changed=",
                "live_cmd_italic_received=",
                "live_cmd_italic_state_changed=",
                "live_cmd_underline_received=",
                "live_cmd_underline_state_changed=",
                "external_image_clipboard_paste_required=",
                "external_image_clipboard_paste=",
                "external_image_clipboard_picture_count=",
                "external_image_clipboard_picture_png_bytes=",
                "macos_accessibility_smoke=",
                "a11y_formula_box_name=",
                "a11y_formula_box_help=",
                "a11y_formula_box_id=",
                "a11y_status_text_name=",
                "a11y_status_text_help=",
                "a11y_status_text_id=",
                "a11y_status_text_value=",
                "a11y_cell_address_name=",
                "a11y_cell_address_help=",
                "a11y_cell_address_id=",
                "a11y_selection_stats_name=",
                "a11y_selection_stats_help=",
                "a11y_selection_stats_id=",
                "native_open_recent_menu_item=",
                "native_open_recent_item_count=",
                "native_export_pdf_menu_item=",
                "native_share_workbook_menu_item=",
                "native_close_workbook_menu_item=",
                "native_workbook_statistics_menu_item=",
                "new_sheet_button=",
                "toolbar_format_painter_button=",
                "toolbar_autosum_button=",
                "toolbar_autosum_sum_menu_item=",
                "toolbar_autosum_average_menu_item=",
                "toolbar_autosum_count_numbers_menu_item=",
                "toolbar_autosum_count_all_menu_item=",
                "toolbar_autosum_max_menu_item=",
                "toolbar_autosum_min_menu_item=",
                "toolbar_fill_cells_button=",
                "toolbar_fill_down_menu_item=",
                "toolbar_fill_right_menu_item=",
                "toolbar_fill_up_menu_item=",
                "toolbar_fill_left_menu_item=",
                "toolbar_clear_button=",
                "toolbar_clear_all_menu_item=",
                "toolbar_clear_formats_menu_item=",
                "toolbar_clear_contents_menu_item=",
                "toolbar_clear_comments_menu_item=",
                "toolbar_clear_hyperlinks_menu_item=",
                "toolbar_borders_button=",
                "toolbar_wrap_text_button=",
                "toolbar_merge_and_center_button=",
                "focusable_sheet_tab=",
                "focusable_active_sheet_tab=",
                "shell_focus_cycle_targets=",
                "sheet_tab_context_keyboard_help=",
                "sheet_tab_context_rename_menu_item=",
                "sheet_tab_context_tab_color_menu_item=",
                "sheet_tab_context_no_color_menu_item=",
                "sheet_tab_context_select_all_sheets_menu_item=",
                "sheet_tab_context_ungroup_sheets_menu_item=",
                "native_view_menu=",
                "native_sheet_menu=",
                "native_new_sheet_menu_item=",
                "native_rename_sheet_menu_item=",
                "native_duplicate_sheet_menu_item=",
                "native_move_sheet_left_menu_item=",
                "native_move_sheet_right_menu_item=",
                "native_tab_color_menu_item=",
                "native_tab_color_clear_item=",
                "native_tab_color_swatch_count=",
                "native_select_all_sheets_menu_item=",
                "native_ungroup_sheets_menu_item=",
                "native_hide_sheet_menu_item=",
                "native_unhide_sheet_menu_item=",
                "native_delete_sheet_menu_item=",
                "native_cut_menu_item=",
                "native_copy_menu_item=",
                "native_paste_special_menu_item=",
                "native_format_painter_menu_item=",
                "native_paste_special_comments_menu_item=",
                "native_paste_special_validation_menu_item=",
                "native_paste_special_all_except_borders_menu_item=",
                "native_paste_special_all_merging_conditional_formats_menu_item=",
                "native_paste_special_column_widths_menu_item=",
                "native_paste_special_formulas_and_number_formats_menu_item=",
                "native_paste_special_values_and_number_formats_menu_item=",
                "native_paste_special_values_and_source_formatting_menu_item=",
                "native_paste_special_keep_source_column_widths_menu_item=",
                "native_paste_special_paste_link_menu_item=",
                "native_paste_special_text_menu_item=",
                "native_paste_special_unicode_text_menu_item=",
                "native_paste_special_picture_menu_item=",
                "native_paste_special_linked_picture_menu_item=",
                "native_data_menu=",
                "native_review_menu=",
                "native_select_all_menu_item=",
                "native_find_menu_item=",
                "native_find_next_menu_item=",
                "native_replace_menu_item=",
                "native_go_to_menu_item=",
                "native_go_to_special_menu_item=",
                "native_sort_ascending_menu_item=",
                "native_sort_descending_menu_item=",
                "native_flash_fill_menu_item=",
                "native_advanced_filter_menu_item=",
                "native_remove_duplicates_menu_item=",
                "native_subtotal_menu_item=",
                "native_data_validation_preview_menu_item=",
                "native_data_validation_menu_item=",
                "native_what_if_analysis_menu_item=",
                "native_goal_seek_menu_item=",
                "native_data_table_menu_item=",
                "native_scenario_manager_menu_item=",
                "native_forecast_sheet_menu_item=",
                "native_review_summary_menu_item=",
                "native_check_accessibility_menu_item=",
                "native_next_note_menu_item=",
                "native_previous_note_menu_item=",
                "native_next_comment_menu_item=",
                "native_previous_comment_menu_item=",
                "native_format_cells_menu_item=",
                "macos_dialog_smoke=",
                "macos_dialog_smoke_attempted=",
                "macos_dialog_smoke_status=",
                "macos_dialog_activation_completed=",
                "find_dialog=",
                "find_dialog_text_box=",
                "find_dialog_action_buttons=",
                "find_dialog_options=",
                "find_dialog_format_controls=",
                "find_dialog_compact_layout=",
                "find_dialog_result_closed_without_accept=",
                "replace_dialog=",
                "replace_dialog_text_boxes=",
                "replace_dialog_action_buttons=",
                "replace_dialog_options=",
                "replace_dialog_format_controls=",
                "replace_dialog_compact_layout=",
                "replace_dialog_result_closed_without_accept=",
                "go_to_dialog=",
                "go_to_dialog_reference_controls=",
                "go_to_dialog_compact_layout=",
                "go_to_dialog_result_closed_without_accept=",
                "go_to_special_dialog=",
                "go_to_special_dialog_kind_controls=",
                "go_to_special_dialog_value_type_controls=",
                "go_to_special_dialog_compact_layout=",
                "go_to_special_dialog_result_closed_without_accept=",
                "format_cells_dialog=",
                "format_cells_dialog_tab_strip=",
                "format_cells_dialog_default_number_tab=",
                "format_cells_dialog_number_controls=",
                "format_cells_dialog_action_buttons=",
                "format_cells_dialog_compact_layout=",
                "format_cells_dialog_result_closed_without_accept=",
                "sort_dialog=",
                "sort_dialog_sort_on_controls=",
                "sort_dialog_color_controls=",
                "sort_dialog_action_buttons=",
                "sort_dialog_compact_layout=",
                "sort_dialog_result_closed_without_accept=",
                "data_validation_dropdown_control=",
                "data_validation_dropdown_items=",
                "data_validation_dialog=",
                "data_validation_dialog_criteria_controls=",
                "data_validation_dialog_message_controls=",
                "data_validation_dialog_action_buttons=",
                "data_validation_dialog_compact_layout=",
                "data_validation_dialog_result_closed_without_accept=",
                "native_autosum_menu_item=",
                "native_autosum_sum_menu_item=",
                "native_autosum_average_menu_item=",
                "native_autosum_count_numbers_menu_item=",
                "native_autosum_count_all_menu_item=",
                "native_autosum_max_menu_item=",
                "native_autosum_min_menu_item=",
                "native_fill_cells_menu_item=",
                "native_fill_down_menu_item=",
                "native_fill_right_menu_item=",
                "native_fill_up_menu_item=",
                "native_fill_left_menu_item=",
                "native_clear_menu_item=",
                "native_clear_all_menu_item=",
                "native_clear_formats_menu_item=",
                "native_clear_contents_menu_item=",
                "native_clear_comments_menu_item=",
                "native_clear_hyperlinks_menu_item=",
                "native_bold_menu_item=",
                "native_fill_color_swatch_count=",
                "native_font_color_swatch_count=",
                "native_borders_menu_item=",
                "native_borders_preset_count=",
                "native_merge_and_center_menu_item=",
                "native_unmerge_cells_menu_item=",
                "native_cell_styles_menu_item=",
                "native_cell_styles_preset_count=",
                "native_horizontal_text_menu_item=",
                "native_angle_counterclockwise_menu_item=",
                "native_angle_clockwise_menu_item=",
                "native_vertical_text_menu_item=",
                "native_rotate_text_up_menu_item=",
                "native_rotate_text_down_menu_item=",
                "native_show_gridlines_menu_item=",
                "native_show_headings_menu_item=",
                "native_zoom_in_menu_item=",
                "native_zoom_out_menu_item=",
                "native_zoom_100_menu_item=",
                "native_zoom_to_selection_menu_item=",
                "native_freeze_panes_menu_item=",
                "native_freeze_top_row_menu_item=",
                "native_freeze_first_column_menu_item=",
                "native_unfreeze_panes_menu_item=",
                "native_show_formulas_menu_item=",
                "native_window_menu=",
                "native_minimize_window_menu_item=",
                "native_zoom_window_menu_item=",
                "native_bring_all_to_front_menu_item=",
                "native_help_menu=",
                "native_help_online_menu_item=",
                "native_send_feedback_menu_item=",
                "native_check_for_updates_menu_item=",
                "native_about_menu_item=",
                "native_legal_notices_menu_item="
            )
            OrderedPairs = @()
        },
        @{
            Path = "src\FreeX.App.Services\WorkbookSessionFactory.cs"
            Markers = @(
                "public WorkbookSession CreateNew(",
                "WorkbookFactory.Create(options)",
                "var source = new StartupWorkbookLoadResult(",
                "`"Created new workbook.`"",
                "return Create("
            )
            OrderedPairs = @()
        },
        @{
            Path = "src\FreeX.App.Services\WorkbookSession.cs"
            Markers = @(
                "public WorkbookCellEditResult AddSheet()",
                "new AddSheetCommand(WorkbookSheetNameGenerator.GenerateUniqueSheetName(Workbook))",
                "public WorkbookCellEditResult RenameActiveSheet(string? name)",
                "new RenameSheetCommand(ActiveSheet.Id, newName)",
                "ApplySuccessfulWorkbookMetadataResult(ActiveSheet.Id)",
                "public WorkbookCellEditResult DuplicateActiveSheet()",
                "new DuplicateSheetCommand(sourceSheetId)",
                "public WorkbookCellEditResult DeleteActiveSheet()",
                "new RemoveSheetCommand(sheetId)",
                "public IReadOnlyList<WorkbookHiddenSheet> HiddenSheets =>",
                "public bool CanHideActiveSheet =>",
                "public bool IsWorkbookGrouped =>",
                "public WorkbookCellEditResult SetActiveSheetTabColor(CellColor? color)",
                "new SetSheetTabColorCommand(ActiveSheet.Id, color)",
                "public bool SelectSheetFromTab(SheetId sheetId, bool selectRange, bool toggle)",
                "SheetGroupSelectionService.SelectRange(",
                "SheetGroupSelectionService.Toggle(sheetId, _groupedSheetIds)",
                "public bool SelectAllVisibleSheets()",
                "SheetGroupSelectionService.SelectAll(GetSelectableSheetIds())",
                "public bool UngroupSheets()",
                "public WorkbookCellEditResult HideActiveSheet()",
                "new SetSheetHiddenCommand(sheetId, hidden: true)",
                "public WorkbookCellEditResult UnhideSheet(SheetId sheetId)",
                "new SetSheetHiddenCommand(sheetId, hidden: false)",
                "public bool IsShowingFormulas => ActiveSheet.ShowFormulas;",
                "public int ZoomPercent => ActiveSheet.ZoomPercent;",
                "public WorkbookCellEditResult SetShowFormulas(bool showFormulas)",
                "new SetWorksheetShowFormulasCommand(ActiveSheet.Id, showFormulas)",
                "public bool IsShowingGridlines => ActiveSheet.ShowGridlines;",
                "public bool IsShowingHeadings => ActiveSheet.ShowHeadings;",
                "public WorkbookCellEditResult SetShowGridlines(bool showGridlines)",
                "public WorkbookCellEditResult SetShowHeadings(bool showHeadings)",
                "new SetWorksheetViewOptionsCommand(ActiveSheet.Id, showGridlines, showHeadings, showRulers)",
                "public bool IsFormatPainterActive =>",
                "public bool CaptureFormatPainterSource(bool persistent = false)",
                "public void CancelFormatPainter()",
                "public WorkbookCellEditResult ApplyFormatPainterToSelectedRange()",
                "CreateFormatPainterCommand(sourceSheet, sourceRange, targetRange)",
                "private IWorkbookCommand CreateFormatPainterCommand(Sheet sourceSheet, GridRange sourceRange, GridRange targetRange)",
                "FormatPainterCommandFactory.Create(",
                "public WorkbookCellEditResult ClearSelectedRangeAll()",
                "public WorkbookCellEditResult ClearSelectedRangeFormats()",
                "public WorkbookCellEditResult ClearSelectedRangeComments()",
                "public WorkbookCellEditResult ClearSelectedRangeHyperlinks()",
                "private IWorkbookCommand CreateClearAllCommand(GridRange range)",
                "new ClearContentsCommand(sheetId, sheetRange)",
                "CellStyleDiffPlanner.ClearFormatsDiff()",
                "new ClearConditionalFormatsCommand(sheetId, sheetRange)",
                "new ClearDataValidationCommand(sheetId, sheetRange)",
                "new ClearCommentsCommand(sheetId, sheetRange)",
                "new ClearHyperlinksCommand(sheetId, sheetRange)",
                "public WorkbookCellEditResult InsertAutoSumFormula(string functionName)",
                "AutoSumFormulaPlanner.TryCreatePlan(ActiveSheet, functionName, SelectedRange, out var plan)",
                "CreateEditCellsCommand([(plan.Target, Cell.FromFormula(plan.Formula))])",
                "SelectCell(GetNextAutoSumCell(plan.Target));",
                "public bool CanFillSelectedRange(FillCellsDirection direction)",
                "public WorkbookCellEditResult FillSelectedRange(FillCellsDirection direction)",
                "new FillCellsCommand(sheetId, sheetRange, direction)",
                "public WorkbookCellEditResult FlashFillSelectedRange()",
                "var plan = FlashFillRangePlanner.Plan(sheet, sheetRange);",
                "FlashFillRangePlanner.HasFillTargets(sheet, plan)",
                "commands.Add(plan.CreateCommand(sheetId));",
                "public WorkbookCellEditResult ExecuteSubtotalOptions(SubtotalInputOptions options)",
                "public WorkbookCellEditResult RemoveSelectedRangeSubtotals()",
                "new SubtotalCommand(",
                "new RemoveSubtotalRowsCommand(sheetId, sheetRange)",
                "private static string GetFillCellsTitle(FillCellsDirection direction)",
                "FillCellsDirection.Down => `"Fill Down`"",
                "FillCellsDirection.Right => `"Fill Right`"",
                "FillCellsDirection.Up => `"Fill Up`"",
                "FillCellsDirection.Left => `"Fill Left`"",
                "public bool CanSortSelectedRange => SelectedRange.RowCount > 1;",
                "public WorkbookCellEditResult SortSelectedRange(bool ascending)",
                "new SortCommand(sheetId, sheetRange, sortByColOffset: 0, ascending)",
                "`"Select at least two rows to sort.`"",
                "public WorkbookCellEditResult SetSelectedRangeBorderPreset(CellBorderPreset preset)",
                "CreateBorderPresetCommand(range, preset)",
                "CellBorderPresetPlanner.Plan(preset, range, range.Start, borderStyle, borderColor)",
                "CellBorderPresetPlanner.RequiresPerCellPlanning(preset)",
                "BorderShortcutService.HasBorderChanges(diff)",
                "GroupedApplyStyleCommand(targetSheetIds, sourceRange, diff)",
                "public WorkbookCellEditResult ApplySelectedRangeCompactFormat(",
                "bool? mergeCells = null",
                "CreateFormatCellsMergeCommands(range, shouldMerge, mergeContentResolution)",
                "public bool IsSelectedRangeMerged => CellMergePlanner.IsSelectionMerged(ActiveSheet, SelectedRange);",
                "public WorkbookCellEditResult MergeAndCenterSelectedRange(",
                "CreateMergeAndCenterCommand(range, contentResolution)",
                "public WorkbookCellEditResult UnmergeSelectedRange()",
                "CreateUnmergeCommands(range)",
                "private IWorkbookCommand CreateMergeAndCenterCommand(",
                "CellMergePlanner.CreateMergeAndCenterCommands(",
                "private IReadOnlyList<IWorkbookCommand> CreateFormatCellsMergeCommands(",
                "MergeCellContentResolution contentResolution = MergeCellContentResolution.KeepFirstCell",
                "CellMergePlanner.CreateMergeCommands(",
                "private IReadOnlyList<IWorkbookCommand> CreateUnmergeCommands(GridRange range)",
                "CellMergePlanner.CreateUnmergeCommands(sheet, sheetId, RemapRangeToSheet(range, sheetId))",
                "public WorkbookCellEditResult SetZoomPercent(int zoomPercent)",
                "new SetWorksheetZoomCommand(ActiveSheet.Id, zoomPercent)",
                "public WorkbookCellEditResult FreezePanesAtActiveCell()",
                "public WorkbookCellEditResult FreezeTopRow()",
                "public WorkbookCellEditResult FreezeFirstColumn()",
                "public WorkbookCellEditResult UnfreezePanes()",
                "new SetFreezePanesCommand(ActiveSheet.Id, frozenRows, frozenCols)",
                "public WorkbookCellEditResult PasteColumnWidthsFromClipboardAtActiveCell(string? text)",
                "public WorkbookCellEditResult PasteCommentsFromClipboardAtActiveCell(string? text, bool transpose = false)",
                "new PasteCommentsCommand(",
                "public WorkbookCellEditResult PasteDataValidationFromClipboardAtActiveCell(string? text, bool transpose = false)",
                "new PasteDataValidationCommand(",
                "public WorkbookCellEditResult PasteLinkFromClipboardAtActiveCell(",
                "PasteLinkService.CreateLinkedCells(",
                "public bool ShouldPreferExternalClipboardImage(string? text)",
                "public WorkbookCellEditResult PasteClipboardImageAtActiveCell(",
                "ClipboardPictureService.CreateInsertCommand(",
                "public WorkbookCellEditResult PastePictureFromClipboardAtActiveCell(",
                "new PasteRangeAsPictureCommand(",
                "private static string FormatPictureCellText(ScalarValue value)",
                "new PasteColumnWidthsCommand(",
                "private IWorkbookCommand CreatePasteLinkCommand(",
                "var sheetDestination = RemapAddressToSheet(destination, sheetId)",
                "IWorkbookCommand command = new EditCellsCommand(sheetId, linkedCells)",
                "private IWorkbookCommand CreateGroupedSheetCommand(",
                "Func<SheetId, IWorkbookCommand> createCommand",
                "bool keepSourceColumnWidths = false",
                "if (keepSourceColumnWidths)",
                "public GridRange SelectCurrentRegionOrAll()",
                "SelectionRangeService.GetCurrentRegion(ActiveSheet, ActiveCell)",
                "SelectedRange != currentRegion",
                "new CellAddress(ActiveSheet.Id, CellAddress.MaxRow, CellAddress.MaxCol)",
                "public string LastFindText => _lastFindText ??",
                "public StyleDiff? CreateFormatDiffFromActiveCell()",
                "public StyleDiff? CreateFormatDiffFromCell(CellAddress address)",
                "public IReadOnlyList<GridRange> SelectedRanges { get; private set; } = [];",
                "public WorkbookFindAllResult FindAll(",
                "return WorkbookFindAllResult.Found(results.Select(CreateFindAllMatch).ToList());",
                "private WorkbookFindAllMatch CreateFindAllMatch(FindResult result)",
                "private string FindNameForAddress(CellAddress address)",
                "public WorkbookReplaceResult ReplaceAllValues(",
                "public WorkbookReplaceResult ReplaceNextValue(",
                "FindOptions? options,",
                "StyleDiff? replacementFormat = null",
                "replacementFormat is not null",
                "new GridRange(edit.Address, edit.Address)",
                "private static bool TryCreateReplacementCommand(",
                "new CompositeWorkbookCommand(",
                "new ApplyStyleCommand(",
                "new GridRange(match.Address, match.Address)",
                "var effectiveOptions = ResolveFindOptions(options, FindLookIn.Values);",
                "GetReplaceTargetIndex(matches, effectiveOptions.SearchOrder, sameSearch)",
                "commands.Add(new EditCellsCommand(sheetId, edits));",
                "var editCommand = new EditCellsCommand(sheet.Id, [(match.Address, newCell)]);",
                "effectiveOptions.LookIn,",
                "FindLookIn.Formulas => cell.FormulaText",
                "FindLookIn.Values => cell.HasFormula ? null : GetReplaceableDisplayText(cell.Value)",
                "newCell = cell.Clone();",
                "FindLookIn.Notes when",
                "match.Target == FindResultTarget.Note",
                "sheet.Comments.TryGetValue(match.Address, out var note) => note",
                "new SetCommentCommand(",
                "new UpdateThreadedCommentTextCommand(",
                "match.Target == FindResultTarget.ThreadedCommentReply",
                "match.ReplyIndex is { } replyIndex",
                "new UpdateThreadedCommentReplyCommand(",
                "private static bool IsValidThreadedCommentReplyIndex(ThreadedComment comment, int replyIndex)",
                "return WorkbookReplaceResult.Replaced(1, replacedRange, index + 1, matches.Count);",
                "public WorkbookNavigationResult GoToReference(string reference)",
                "public WorkbookGoToSpecialResult GoToSpecial(GoToSpecialKind kind, GoToSpecialOptions? options = null)",
                "GoToSpecialService.Find(Workbook, ActiveSheet, SelectedRange, kind, ActiveCell, options)",
                "SelectionRangeService.CompressAddresses(matches)",
                "SelectRanges(selectedRange, ranges);",
                "WorkbookReferenceNavigator.TryParseReferenceRange(",
                "public WorkbookNavigationResult FindNext(",
                "FindReplaceService.Find(Workbook, text, effectiveOptions, matchCase, matchEntireCell)",
                "return WorkbookNavigationResult.Found(",
                "private WorkbookNavigationResult NavigateToRange(GridRange range)",
                "SelectSheet(range.Start.Sheet);",
                "private int GetNextFindResultIndex(",
                "private int CompareFindOrder(CellAddress left, CellAddress right, FindSearchOrder searchOrder)",
                "private SheetId? ResolveSheetIdByName(string sheetName)",
                "ApplySuccessfulWorkbookStructureResult(Workbook.Sheets[^1].Id)",
                "ApplySuccessfulHistoryResult(result, sheetIdsBefore)",
                "private void ApplySuccessfulWorkbookStructureResult(SheetId preferredSheetId)"
            )
            OrderedPairs = @()
        },
        @{
            Path = "src\FreeX.App.Services\FlashFillRangePlanner.cs"
            Markers = @(
                "public readonly record struct FlashFillCommandPlan(",
                "public FlashFillCommand CreateCommand(SheetId sheetId)",
                "new FlashFillCommand(sheetId, FillColumn, SourceColumn, StartRow, EndRow)",
                "public static bool HasFillTargets(Sheet sheet, FlashFillCommandPlan plan)"
            )
            OrderedPairs = @()
        },
        @{
            Path = "src\FreeX.Core.Commands\FindReplaceService.cs"
            Markers = @(
                "public enum FindResultTarget",
                "ThreadedCommentReply",
                "FindResultTarget Target = FindResultTarget.Cell,",
                "int? ReplyIndex = null);"
            )
            OrderedPairs = @()
        },
        @{
            Path = "src\FreeX.Core.Commands\FindReplaceSearchPlanner.cs"
            Markers = @(
                "public readonly record struct SearchText(",
                "comment.Replies[replyIndex].Text",
                "FindResultTarget.ThreadedCommentReply,"
            )
            OrderedPairs = @()
        },
        @{
            Path = "src\FreeX.App.Services\WorkbookReferenceNavigator.cs"
            Markers = @(
                "public static class WorkbookReferenceNavigator",
                "public static bool TryParseAddress(string text, SheetId sheetId, out CellAddress address)",
                "public static IReadOnlyList<string> BuildReferenceChoices(",
                "public static bool TryParseReference(",
                "public static bool TryParseReferenceRange(",
                "Func<string, SheetId?> resolveSheetId",
                "private static bool TryResolveReferenceSheet(",
                "private static string? NormalizeAbsoluteA1Reference(string input)",
                "private static bool TryParseAbsoluteR1C1CellReference(string input, SheetId sheetId, out CellAddress address)"
            )
            OrderedPairs = @()
        },
        @{
            Path = "src\FreeX.App.Services\LocalFilePath.cs"
            Markers = @(
                "public static class LocalFilePath",
                "public static bool TryNormalize(string? candidate, out string normalizedPath)",
                "TryCreateExplicitUri(path, out var uri)",
                "if (!uri.IsFile)",
                "path = uri.LocalPath;",
                "path.Contains('\0', StringComparison.Ordinal)",
                "IsUnixAbsolutePath(path)",
                "Path.GetFullPath(path)",
                "private static bool TryCreateExplicitUri(string candidate, out Uri uri)",
                "Uri.TryCreate(candidate, UriKind.Absolute, out var parsed)",
                "IsWindowsDrivePath(candidate, parsed.Scheme)",
                "private static bool IsWindowsDrivePath(string candidate, string scheme)",
                "char.IsAsciiLetter(candidate[0])",
                "private static bool IsUnixAbsolutePath(string path)"
            )
            OrderedPairs = @()
        },
        @{
            Path = "src\FreeX.App.Services\OpenRecentWorkbookMenuPlanner.cs"
            Markers = @(
                "public sealed record OpenRecentWorkbookMenuItemPlan(",
                "public sealed record OpenRecentWorkbookMenuPlan(",
                "public int ItemCount => Items.Count;",
                "public static class OpenRecentWorkbookMenuPlanner",
                "public const int DefaultMaximumItems = 10;",
                "public static OpenRecentWorkbookMenuPlan Create(",
                "IEnumerable<RecentFileEntry> entries",
                "Func<string, bool> fileExists",
                "Func<string, bool> canOpenWorkbook",
                "Func<string, string?> resolveOpenWorkbookPath",
                "maximumItems < 1",
                "PlatformPathIdentityComparer.Current",
                ".Where(entry => !string.IsNullOrWhiteSpace(entry.Path))",
                ".OrderByDescending(entry => entry.LastOpened)",
                ".Select(entry => (Entry: entry, Path: resolveOpenWorkbookPath(entry.Path)))",
                ".Where(item => !string.IsNullOrWhiteSpace(item.Path) && fileExists(item.Path))",
                ".Where(item => seenPaths.Add(item.Path!))",
                ".Take(maximumItems)",
                "FormatHeader(item.Path!)",
                "public static string FormatHeader(string path)",
                "Path.GetFileName(path)",
                "Path.GetDirectoryName(path)"
            )
            OrderedPairs = @()
        },
        @{
            Path = "src\FreeX.App.Services\WorkbookShareActionPlanner.cs"
            Markers = @(
                "public enum WorkbookShareActionPlanKind",
                "ShareSheet,",
                "OpenContainingFolder,",
                "SaveAsBeforeShare,",
                "Deferred",
                "public sealed record WorkbookShareActionSurface(",
                "bool CanShowShareSheet,",
                "bool CanOpenContainingFolder = false",
                "public static WorkbookShareActionSurface MacOsPreview",
                'new("macOS Share Sheet", CanShowShareSheet: false);',
                "public static class WorkbookShareActionPlanner",
                "CreatePlan(currentFilePath, WorkbookShareActionSurface.MacOsPreview, fileExists);",
                "WorkbookShareReadinessPlanner.CreatePlan(",
                "surface.CanShowShareSheet || surface.CanOpenContainingFolder",
                "WorkbookShareActionPlanKind.SaveAsBeforeShare",
                "WorkbookShareActionPlanKind.OpenContainingFolder",
                "WorkbookShareActionUnavailableReason.ShareSheetUnavailable",
                "TryGetContainingFolderPath(readiness.Path, out var containingFolderPath)"
            )
            OrderedPairs = @()
        },
        @{
            Path = "src\FreeX.App.Services\WorkbookViewportScrollPlanner.cs"
            Markers = @(
                "public readonly record struct WorkbookViewportScrollAxis(",
                "public readonly record struct WorkbookViewportScrollState(",
                "public static class WorkbookViewportScrollPlanner",
                "public static WorkbookViewportScrollState Create(Sheet sheet, ViewportModel viewport)",
                "CountScrollableRows(viewport.RowMetrics, sheet.FrozenRows)",
                "CountScrollableColumns(viewport.ColMetrics, sheet.FrozenCols)",
                "public static (uint TopRow, uint LeftCol) CalculateViewportOrigin(",
                "ScrollbarValueToWorksheetIndex(verticalScrollValue, sheet.FrozenRows, CellAddress.MaxRow)",
                "ScrollbarValueToWorksheetIndex(horizontalScrollValue, sheet.FrozenCols, CellAddress.MaxCol)",
                "public static uint WorksheetIndexToScrollbarValue(uint worksheetIndex, uint frozenCount)",
                "public static uint CalculateScrollableLimit(uint absoluteLimit, uint frozenCount)",
                "public static uint CalculateMaximumViewportOrigin(uint absoluteLimit, uint visibleSpan)",
                "SmallChange: 1",
                "IsEnabled: maximum > MinimumScrollValue"
            )
            OrderedPairs = @()
        },
        @{
            Path = "src\FreeX.App.Services\FormatCellsCompactPlanner.cs"
            Markers = @(
                "public sealed record FormatCellsCompactRequest(",
                "bool? DoubleUnderline = null",
                "bool? MergeCells = null",
                "bool? ShrinkToFit = null",
                "int? IndentLevel = null",
                "int? TextRotation = null",
                "string? FontName = null",
                "bool? Superscript = null",
                "bool? Subscript = null",
                "bool? Locked = null",
                "bool? Hidden = null",
                "CellFillPatternStyle? FillPatternStyle = null",
                "CellColor? FillPatternColor = null",
                "DoubleUnderline: request.DoubleUnderline",
                "ShrinkToFit: request.ShrinkToFit",
                "IndentLevel: NormalizeIndentLevel(request.IndentLevel)",
                "TextRotation: NormalizeTextRotation(request.TextRotation)",
                "FontName: NormalizeFontName(request.FontName)",
                "Superscript: request.Superscript",
                "Subscript: request.Subscript",
                "Locked: request.Locked",
                "Hidden: request.Hidden",
                "FillPatternStyle: request.ClearFill ? null : request.FillPatternStyle",
                "FillPatternColor: request.ClearFill ? null : request.FillPatternColor"
            )
            OrderedPairs = @()
        },
        @{
            Path = "src\FreeX.App.Services\CellBorderPresetPlanner.cs"
            Markers = @(
                "public enum CellBorderPreset",
                "CellBorderPreset.All",
                "CellBorderPreset.Outside",
                "CellBorderPreset.Inside",
                "CellBorderPreset.NoBorder",
                "public static StyleDiff Plan(",
                "BorderShortcutService.GetAllBorderDiff(style, borderColor)",
                "BorderShortcutService.GetOutlineBorderDiff(range, address, style, borderColor)",
                "BorderShortcutService.GetInsideBorderDiff(range, address, style, borderColor)",
                "BorderShortcutService.GetClearBorderDiff()",
                "public static string GetDisplayName(CellBorderPreset preset)",
                "public static bool RequiresPerCellPlanning(CellBorderPreset preset)"
            )
            OrderedPairs = @()
        },
        @{
            Path = "src\FreeX.App.Services\CellMergePlanner.cs"
            Markers = @(
                "public static class CellMergePlanner",
                "public static bool IsSelectionMerged(Sheet sheet, GridRange range)",
                "sheet.MergedRegions.Any(region => region.Overlaps(range))",
                "public static IReadOnlyList<IWorkbookCommand> CreateMergeAndCenterCommands(SheetId sheetId, GridRange range)",
                "new MergeCellsCommand(sheetId, range)",
                "new ApplyStyleCommand(sheetId, range, new StyleDiff(HAlign: HorizontalAlignment.Center))",
                "public static IReadOnlyList<IWorkbookCommand> CreateMergeCommands(",
                "public static IReadOnlyList<IWorkbookCommand> CreateUnmergeCommands(Sheet sheet, SheetId sheetId, GridRange range)",
                "new UnmergeCellsCommand(sheetId, region)"
            )
            OrderedPairs = @()
        },
        @{
            Path = "src\FreeX.App.Services\RecentFilesStore.cs"
            Markers = @(
                "public sealed class RecentFileEntry",
                "public sealed class RecentFilesStore",
                "public static RecentFilesStore Load() => Load(DefaultStorePath);",
                "public static RecentFilesStore Load(string storePath, Func<DateTimeOffset>? clock = null)",
                "_clock = clock ?? (() => DateTimeOffset.UtcNow);",
                "LastOpened = _clock()",
                "AtomicFileWriter.WriteAllText(_storePath, JsonSerializer.Serialize(Entries));"
            )
            OrderedPairs = @()
        },
        @{
            Path = "src\FreeX.App.Services\AtomicFileWriter.cs"
            Markers = @(
                "public static class AtomicFileWriter",
                "File.WriteAllText(tempPath, content);",
                "File.Move(tempPath, path, overwrite: true);"
            )
            OrderedPairs = @()
        },
        @{
            Path = "src\FreeX.App.Services\WorkbookStartupSmokeService.cs"
            Markers = @(
                "private const string RoundTripExtension = `".fxl`";",
                "_sessionFactory.Create(source, SmokeViewportHeight, SmokeViewportWidth, includeObjects: true)",
                "VerifyDrawingObjectPreviews(",
                "drawing_object_previews={drawingObjectPreviewCount}",
                "roundtrip_drawing_object_previews={roundTripDrawingObjectPreviewCount}",
                "after applying compact Format Cells style to B2",
                "format_cells_style_roundtrip=true",
                "ApplyFormatCellsStartupSmokeStyle(",
                "VerifyFormatCellsStartupSmokeStyle(",
                "PortPreviewWorkbookFactory.PreviewShapeName",
                "public static class PackagingSmokeCommand",
                "public const string Argument = `"--packaging-smoke`";",
                "Packaging smoke opened",
                "edited, saved, and reopened"
            )
            OrderedPairs = @()
        },
        @{
            Path = "src\FreeX.App.Services\PortPreviewWorkbookFactory.cs"
            Markers = @(
                "public const string PreviewShapeName = `"Port readiness shape`";",
                "public const string PreviewTextBoxName = `"Port preview note`";",
                "public const string PreviewPictureName = `"Port preview logo`";",
                "AddPreviewDrawingObjects(sheet);",
                "sheet.DrawingShapes.Add(shape);",
                "sheet.TextBoxes.Add(textBox);",
                "sheet.Pictures.Add(picture);",
                "sheet.DrawingObjectZOrder.AddRange("
            )
            OrderedPairs = @()
        },
        @{
            Path = "src\FreeX.Core.IO\NativeJsonAdapter.cs"
            Markers = @(
                "public string Extension => `".fxl`";",
                "public string FormatName => `"FreeX Workbook`";"
            )
            OrderedPairs = @()
        }
    )

    foreach ($contract in $sourceContracts) {
        $sourcePath = Resolve-RepoPath $contract.Path
        Assert-FileExists -Path $sourcePath -Label "macOS source wiring file"
        $sourceText = Get-Content -LiteralPath $sourcePath -Raw
        foreach ($marker in $contract.Markers) {
            Assert-ContainsText -Text $sourceText -Needle $marker -Message "macOS source wiring is missing '$marker' in $($contract.Path)."
        }

        foreach ($orderedPair in $contract.OrderedPairs) {
            Assert-TextBefore -Text $sourceText -First $orderedPair.First -Second $orderedPair.Second -Message "macOS source wiring order is invalid in $($contract.Path)."
        }
    }

    Write-Host "Validated macOS app source wiring markers."
}

function Test-PortableSourceHygiene {
    param([Parameter(Mandatory = $true)][string[]]$SourceRoots)

    $alwaysForbiddenTokens = @(
        "System.Windows",
        "Microsoft.Win32",
        "Windows.ApplicationModel",
        "Windows.Storage",
        "[ComImport",
        "[DllImport",
        "DllImportAttribute",
        "OxyPlot.Wpf",
        "PDFsharp-WPF",
        "SharpVectors.Wpf",
        "UseWPF",
        "FreeX.App.Host",
        "FreeX.App.UI"
    )
    $nativeMacOsTokens = @(
        "AppKit",
        "Foundation",
        "ObjCRuntime",
        "NSSharingServicePicker",
        "NSSharingService"
    )
    $extensions = @(".cs", ".csproj", ".axaml", ".xaml")
    $sourceFiles = New-Object System.Collections.Generic.List[System.IO.FileInfo]

    foreach ($root in $SourceRoots) {
        $resolvedRoot = Resolve-RepoPath $root
        Assert-True -Condition (Test-Path -LiteralPath $resolvedRoot -PathType Container) -Message "Portable source root was not found: $resolvedRoot"

        foreach ($file in Get-ChildItem -LiteralPath $resolvedRoot -Recurse -File) {
            if ($extensions -notcontains $file.Extension) {
                continue
            }

            if (Test-IsIgnoredSourcePath $file.FullName) {
                continue
            }

            $sourceFiles.Add($file)
        }
    }

    foreach ($file in $sourceFiles) {
        $content = Get-Content -LiteralPath $file.FullName -Raw
        foreach ($token in $alwaysForbiddenTokens) {
            if ($content.IndexOf($token, [System.StringComparison]::Ordinal) -ge 0) {
                throw "Portable macOS source contains forbidden token '$token' in $(Get-RepoRelativePath $file.FullName)."
            }
        }

        if (Test-IsMacOsConditionalSourcePath $file.FullName) {
            continue
        }

        foreach ($token in $nativeMacOsTokens) {
            if ($content.IndexOf($token, [System.StringComparison]::Ordinal) -ge 0) {
                throw "Portable macOS source contains native macOS token '$token' outside src/FreeX.App.Avalonia/MacOs in $(Get-RepoRelativePath $file.FullName)."
            }
        }
    }

    Write-Host "Validated portable macOS source hygiene across $($sourceFiles.Count) source file(s)."
}

$resolvedAvaloniaProjectPath = Resolve-RepoPath $AvaloniaProjectPath
$resolvedInfoPlistPath = Resolve-RepoPath $InfoPlistPath
$resolvedIconPath = Resolve-RepoPath $IconPath
$resolvedWorkflowPath = Resolve-RepoPath $WorkflowPath

Assert-FileExists -Path $resolvedAvaloniaProjectPath -Label "Avalonia macOS app project"
Assert-FileExists -Path $resolvedInfoPlistPath -Label "macOS Info.plist"
Assert-FileExists -Path $resolvedIconPath -Label "macOS app icon"
Assert-FileExists -Path $resolvedWorkflowPath -Label "macOS app workflow"

$projectReadiness = Test-AvaloniaProject -ProjectPath $resolvedAvaloniaProjectPath
Test-MacOsIcon -IconFilePath $resolvedIconPath
Test-InfoPlist -PlistPath $resolvedInfoPlistPath -ExpectedExecutable $projectReadiness.AssemblyName
Test-MacOsWorkflow -WorkflowPath $resolvedWorkflowPath -ProjectPathForWorkflow $projectReadiness.ProjectPathForWorkflow -RuntimeIdentifiers $projectReadiness.RuntimeIdentifiers
Test-SourceWiring
Test-PortableSourceHygiene -SourceRoots $PortableSourceRoots

Write-Host "macOS app readiness preflight passed."
