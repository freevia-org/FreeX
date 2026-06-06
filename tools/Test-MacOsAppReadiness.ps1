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
        if (-not [string]::IsNullOrWhiteSpace($value)) {
            return [string]$value
        }
    }

    return $null
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

    $targetFramework = Get-ProjectProperty -Project $project -Name "TargetFramework"
    Assert-True -Condition ($targetFramework -eq "net10.0") -Message "Avalonia app TargetFramework must be net10.0, but was '$targetFramework'."
    Assert-True -Condition ($targetFramework.IndexOf("-windows", [System.StringComparison]::OrdinalIgnoreCase) -lt 0) -Message "Avalonia app TargetFramework must not be Windows-specific."

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
    Assert-True -Condition ($documentTypeDicts.Count -ge 2) -Message "Info.plist must declare native and imported workbook document types."

    $nativeWorkbook = $documentTypeDicts[0]
    Assert-True -Condition ((Get-PlistString -Dict $nativeWorkbook -Key "CFBundleTypeName") -eq "FreeX Workbook") -Message "Info.plist native document type name must be FreeX Workbook."
    Assert-True -Condition ((Get-PlistString -Dict $nativeWorkbook -Key "CFBundleTypeRole") -eq "Editor") -Message "Info.plist native document type role must be Editor."
    Assert-True -Condition ((Get-PlistString -Dict $nativeWorkbook -Key "LSHandlerRank") -eq "Owner") -Message "Info.plist native document handler rank must be Owner."
    $nativeExtensions = Get-PlistValue -Dict $nativeWorkbook -Key "CFBundleTypeExtensions"
    $nativeExtensionValues = @($nativeExtensions.ChildNodes | Where-Object { $_.NodeType -eq [System.Xml.XmlNodeType]::Element -and $_.Name -eq "string" } | ForEach-Object { $_.InnerText })
    Assert-True -Condition ($nativeExtensionValues -contains "fxl") -Message "Info.plist native document type must include fxl."

    $importedWorkbooks = $documentTypeDicts[1]
    Assert-True -Condition ((Get-PlistString -Dict $importedWorkbooks -Key "CFBundleTypeName") -eq "Spreadsheet Workbooks") -Message "Info.plist imported document type name must be Spreadsheet Workbooks."
    Assert-True -Condition ((Get-PlistString -Dict $importedWorkbooks -Key "CFBundleTypeRole") -eq "Viewer") -Message "Info.plist imported document type role must be Viewer."
    Assert-True -Condition ((Get-PlistString -Dict $importedWorkbooks -Key "LSHandlerRank") -eq "Alternate") -Message "Info.plist imported document handler rank must be Alternate."
    $importedExtensions = Get-PlistValue -Dict $importedWorkbooks -Key "CFBundleTypeExtensions"
    $importedExtensionValues = @($importedExtensions.ChildNodes | Where-Object { $_.NodeType -eq [System.Xml.XmlNodeType]::Element -and $_.Name -eq "string" } | ForEach-Object { $_.InnerText })
    foreach ($extension in @("xlsx", "xlsm", "xltx", "xltm", "xls", "xlsb", "xlt", "csv", "tsv", "tab")) {
        Assert-True -Condition ($importedExtensionValues -contains $extension) -Message "Info.plist imported document type must include $extension."
    }
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

    $requiredWorkflowMarkers = @(
        "runs-on: macos-latest",
        "dotnet-version: 10.0.x",
        "dotnet build $projectPath --configuration Release",
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
        "PlistBuddy -c 'Print :CFBundleDocumentTypes:0:CFBundleTypeExtensions:0'",
        "PlistBuddy -c 'Print :CFBundleDocumentTypes:1:CFBundleTypeExtensions:0'",
        "lipo -archs",
        "codesign --verify --deep --strict",
        "ditto -c -k --sequesterRsrc --keepParent",
        "shasum -a 256",
        'test -x "$unzip_root/FreeX.app/Contents/MacOS/FreeX"',
        'test -f "$unzip_root/FreeX.app/Contents/MacOS/FreeX.dll"',
        "actions/upload-artifact@v7",
        "if-no-files-found: error",
        "Developer ID signing is disabled for pull_request events; using ad-hoc signing.",
        "xcrun notarytool submit",
        "xcrun stapler validate",
        "--packaging-smoke",
        "Packaging smoke opened",
        "edited, saved, and reopened",
        "lsregister -f",
        "open -W -n -b io.github.tony-xmelon.freex",
        "osascript -e 'tell application id `"io.github.tony-xmelon.freex`" to quit' || true",
        "--macos-launch-smoke",
        "native_file_menu=true",
        "native_edit_menu=true",
        "native_format_menu=true",
        "native_cut_menu_item=true",
        "native_copy_menu_item=true",
        "native_paste_menu_item=true",
        "native_clear_contents_menu_item=true",
        "native_bold_menu_item=true",
        "native_fill_color_menu_item=true",
        "native_font_color_menu_item=true",
        "native_fill_color_swatch_count=69",
        "native_font_color_swatch_count=69",
        "native_cell_styles_menu_item=true",
        "native_cell_styles_preset_count=33",
        "native_horizontal_text_menu_item=true",
        "native_angle_counterclockwise_menu_item=true",
        "native_angle_clockwise_menu_item=true",
        "native_vertical_text_menu_item=true",
        "native_rotate_text_up_menu_item=true",
        "native_rotate_text_down_menu_item=true",
        'bundle_icon=$('
    )

    foreach ($marker in $requiredWorkflowMarkers) {
        Assert-ContainsText -Text $workflow -Needle $marker -Message "macOS workflow is missing required readiness marker: $marker"
    }
}

function Test-SourceWiring {
    $sourceContracts = @(
        @{
            Path = "src\FreeX.App.Avalonia\Program.cs"
            Markers = @(
                "PackagingSmokeCommand.TryRun(args, Console.Out, Console.Error, out var smokeExitCode)",
                "MacOsLaunchSmokeOptions.TryParse(",
                "App.StartupArguments = startupArguments;",
                "App.LaunchSmokeOptions = launchSmokeOptions;",
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
                "this.TryGetFeature<IActivatableLifetime>() is { } activatableLifetime",
                "args is not FileActivatedEventArgs fileArgs",
                "fileArgs.Kind != ActivationKind.File",
                "await mainWindow.OpenActivatedFilesAsync(fileArgs.Files);"
            )
            OrderedPairs = @()
        },
        @{
            Path = "src\FreeX.App.Avalonia\MainWindow.cs"
            Markers = @(
                "private const string NativeWorkbookExtension = `".fxl`";",
                "public async Task OpenActivatedFilesAsync(IReadOnlyList<IStorageItem> files)",
                "CreateColorPaletteFlyout(ColorPaletteTarget.Fill, includeClearFill: true)",
                "CellColorPalettePlanner.BuildDefaultSwatches()",
                "AddStyledCellBorderOverlay(content, style);",
                "private static bool HasVisibleCellBorder(CellStyle? style)",
                "internal MacOsLaunchSmokeSnapshot CreateLaunchSmokeSnapshot()"
            )
            OrderedPairs = @()
        },
        @{
            Path = "src\FreeX.App.Avalonia\MacOsLaunchSmoke.cs"
            Markers = @(
                "public const string Argument = `"--macos-launch-smoke`";",
                "startupArguments = filteredArguments.ToArray();",
                "HasNativeFileMenu &&",
                "HasNativeEditMenu &&",
                "HasNativeFormatMenu &&",
                "native_cut_menu_item=",
                "native_copy_menu_item=",
                "native_clear_contents_menu_item=",
                "native_bold_menu_item=",
                "native_fill_color_swatch_count=",
                "native_font_color_swatch_count=",
                "native_cell_styles_menu_item=",
                "native_cell_styles_preset_count=",
                "native_horizontal_text_menu_item=",
                "native_angle_counterclockwise_menu_item=",
                "native_angle_clockwise_menu_item=",
                "native_vertical_text_menu_item=",
                "native_rotate_text_up_menu_item=",
                "native_rotate_text_down_menu_item="
            )
            OrderedPairs = @()
        },
        @{
            Path = "src\FreeX.App.Services\WorkbookStartupSmokeService.cs"
            Markers = @(
                "private const string RoundTripExtension = `".fxl`";",
                "public static class PackagingSmokeCommand",
                "public const string Argument = `"--packaging-smoke`";",
                "Packaging smoke opened",
                "edited, saved, and reopened"
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

    $forbiddenTokens = @(
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
        foreach ($token in $forbiddenTokens) {
            if ($content.IndexOf($token, [System.StringComparison]::Ordinal) -ge 0) {
                throw "Portable macOS source contains forbidden token '$token' in $(Get-RepoRelativePath $file.FullName)."
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
