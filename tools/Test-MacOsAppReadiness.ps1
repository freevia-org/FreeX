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
        'tester_instructions_path="$artifact_root/freex-$runtime-macos-tester-instructions.md"',
        "actions/upload-artifact@v7",
        "if-no-files-found: error",
        "Developer ID signing is disabled for pull_request events; using ad-hoc signing.",
        "xcrun notarytool submit",
        "xcrun stapler validate",
        'shasum -a 256 -c "$zip_name.sha256"',
        'zip_sha256="$(cut -d '' '' -f 1 "$artifact_root/$zip_name.sha256")"',
        'echo "zip_sha256=$zip_sha256"',
        'cat > "$tester_instructions_path" <<EOF',
        "This artifact is a preview build for macOS port validation. It is not a public release channel.",
        "Use osx-arm64 for Apple Silicon Macs and osx-x64 for Intel Macs.",
        "Unzip the GitHub Actions artifact wrapper first; these files are inside it.",
        "Ad-hoc signed or non-notarized previews may require Control-click or right-click > Open for trusted internal testing.",
        "--packaging-smoke",
        "Packaging smoke opened",
        "macOS Preview Workbook",
        "drawing_object_previews=3",
        "roundtrip_drawing_object_previews=3",
        "edited, saved, and reopened",
        "lsregister -f",
        "open -W -n -b io.github.tony-xmelon.freex",
        "osascript -e 'tell application id `"io.github.tony-xmelon.freex`" to quit' || true",
        "--macos-launch-smoke",
        "new_sheet_button=true",
        "native_file_menu=true",
        "native_edit_menu=true",
        "native_format_menu=true",
        "native_view_menu=true",
        "native_sheet_menu=true",
        "native_help_menu=true",
        "native_new_workbook_menu_item=true",
        "native_open_recent_menu_item=true",
        "native_open_recent_item_count=[1-9]",
        "native_close_workbook_menu_item=true",
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
        "native_help_online_menu_item=true",
        "native_send_feedback_menu_item=true",
        "native_check_for_updates_menu_item=true",
        "native_about_menu_item=true",
        "native_legal_notices_menu_item=true",
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
                "CreateSelectableDrawingObjectVisual(drawingObject, width, height)",
                "AutomationProperties.SetAutomationId(container, `$`"DrawingObject{drawingObject.Kind}{drawingObject.Id:N}`");",
                "AutomationProperties.SetHelpText(container, `"Selects this drawing object preview in the workbook viewport.`");",
                "AutomationProperties.SetItemStatus(container, selected ? `"Selected`" : `"Not selected`");",
                "container.PointerPressed += (_, args) =>",
                "if (args.Key is Key.Enter or Key.Space)",
                "CreateSelectedDrawingObjectAdorner()",
                "ClearSelectedDrawingObject();",
                "CreateDrawingObjectVisual(drawingObject, width, height)",
                "TryCreateDrawingBitmap(imageBytes, out var bitmap)",
                "AddStyledCellBorderOverlay(content, style);",
                "private static bool HasVisibleCellBorder(CellStyle? style)",
                "private readonly RecentFilesStore _recentFiles = RecentFilesStore.Load();",
                "_newWorkbookMenuItem.Click += (_, _) => CreateNewWorkbook();",
                "_openRecentMenuItem.Header = `"Open Recent`";",
                "_openRecentMenuItem.Menu = CreateNativeOpenRecentMenu(isIdle: true);",
                "fileMenu.Items.Add(_openRecentMenuItem);",
                "RefreshNativeOpenRecentMenu(isIdle);",
                "_selectAllMenuItem.Header = `"Select All`";",
                "_selectAllMenuItem.Gesture = new KeyGesture(Key.A, KeyModifiers.Meta);",
                "_selectAllMenuItem.Click += (_, _) => SelectCurrentRegionOrAll();",
                "editMenu.Items.Add(_selectAllMenuItem);",
                "_selectAllMenuItem.IsEnabled = isIdle;",
                "private void SelectCurrentRegionOrAll()",
                "var range = _session.SelectCurrentRegionOrAll();",
                "e.Key is Key.Z or Key.Y or Key.X or Key.C or Key.V or Key.A",
                "else if (e.Key == Key.A && HasOnlyCommandModifier(e.KeyModifiers))",
                "private static bool HasCommandAndShiftModifiers(KeyModifiers modifiers)",
                "private static bool IsSheetTabFocusKey(KeyEventArgs args)",
                "args.Key == Key.F6 &&",
                "if (IsSheetTabFocusKey(e))",
                "FocusActiveSheetTab();",
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
                "private List<RecentFileEntry> GetOpenableRecentWorkbookEntries()",
                "entries.Sort(static (left, right) => right.LastOpened.CompareTo(left.LastOpened));",
                "private async Task OpenRecentWorkbookAsync(string path)",
                "private void RecordStartupRecentWorkbook(StartupWorkbookLoadResult source)",
                "private void RecordRecentWorkbook(string path)",
                "_recentFiles.AddOrUpdate(path);",
                "RecordRecentWorkbook(target.Path);",
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
                "private const string SheetTabContextHelpText = `"Selects this sheet. Press F6 to focus sheet tabs, use arrow keys to switch sheets, or right-click/press Shift+F10 for sheet tab options.`";",
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
                "private void FocusActiveSheetTab()",
                "private bool FocusSheetTab(SheetId sheetId)",
                "private static void SheetTabContextMenu_Opened(object? sender, RoutedEventArgs args)",
                "FirstOrDefault(item => item.IsEnabled)?",
                ".Focus();",
                "private Button? FindSheetTabButton(SheetId sheetId)",
                "button.Tag is SheetId tag && tag == sheetId",
                "private bool HasSheetTabButton(Func<Button, bool> predicate)",
                "HasFocusableSheetTab: HasSheetTabButton(button => button.Focusable)",
                "HasFocusableActiveSheetTab: FindSheetTabButton(_session.ActiveSheet.Id)?.Focusable == true",
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
                "Header = `"Help`"",
                "TopLevel.GetTopLevel(this)?.Launcher",
                "AppHelpInfo.BuildAboutText(versionText, PlatformAboutSummary)",
                "LegalNoticeProvider.GetDocuments().Select(document =>",
                "internal MacOsLaunchSmokeSnapshot CreateLaunchSmokeSnapshot()",
                "var showHeadings = _session.ActiveSheet.ShowHeadings;",
                "var zoomFactor = GetActiveZoomFactor();",
                "showGridlines ? GridLine : Brushes.Transparent",
                "CalculateDisplayedGridWidth(viewport, showHeadings, zoomFactor)",
                "CalculateDisplayedGridHeight(viewport, showHeadings, zoomFactor)",
                "fontSize * zoomFactor",
                "displayHeight / zoomFactor",
                "private double GetActiveZoomFactor()"
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
                "HasNativeViewMenu &&",
                "HasNativeSheetMenu &&",
                "HasNativeHelpMenu &&",
                "HasNativeNewWorkbookMenuItem &&",
                "HasNativeOpenRecentMenuItem &&",
                "NativeOpenRecentItemCount > 0 &&",
                "HasNativeSelectAllMenuItem &&",
                "HasNativeCloseWorkbookMenuItem &&",
                "HasNativeRenameSheetMenuItem &&",
                "HasNativeMoveSheetLeftMenuItem &&",
                "HasNativeMoveSheetRightMenuItem &&",
                "HasNativeTabColorMenuItem &&",
                "HasNativeClearTabColorMenuItem &&",
                "NativeTabColorSwatchCount == CellColorPalettePlanner.BuildDefaultSwatches().Count",
                "HasFocusableSheetTab &&",
                "HasFocusableActiveSheetTab &&",
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
                "native_new_workbook_menu_item=",
                "native_open_recent_menu_item=",
                "native_open_recent_item_count=",
                "native_close_workbook_menu_item=",
                "new_sheet_button=",
                "focusable_sheet_tab=",
                "focusable_active_sheet_tab=",
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
                "native_select_all_menu_item=",
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
                "new EditCellsCommand(ActiveSheet.Id, linkedCells)",
                "bool keepSourceColumnWidths = false",
                "if (keepSourceColumnWidths)",
                "public GridRange SelectCurrentRegionOrAll()",
                "SelectionRangeService.GetCurrentRegion(ActiveSheet, ActiveCell)",
                "SelectedRange != currentRegion",
                "new CellAddress(ActiveSheet.Id, CellAddress.MaxRow, CellAddress.MaxCol)",
                "ApplySuccessfulWorkbookStructureResult(Workbook.Sheets[^1].Id)",
                "ApplySuccessfulHistoryResult(result, sheetIdsBefore)",
                "private void ApplySuccessfulWorkbookStructureResult(SheetId preferredSheetId)"
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
