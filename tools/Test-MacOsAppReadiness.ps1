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
        'runs-on: ${{ matrix.runner }}',
        "runner: macos-latest",
        "runner: macos-15-intel",
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
        "Upload app diagnostics",
        "if: always()",
        'freex-${{ github.run_id }}-${{ github.run_attempt }}-${{ matrix.runtime }}-macos-diagnostics',
        "if-no-files-found: warn",
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
        "--macos-launch-smoke-verify-image-clipboard",
        'launch_clipboard_image="$RUNNER_TEMP/freex-$runtime-clipboard.png"',
        'base64 -D > "$launch_clipboard_image"',
        '/usr/bin/swift - "$launch_clipboard_image"',
        "NSPasteboard.general",
        "pasteboard.clearContents()",
        "pasteboard.writeObjects([image])",
        "external_image_clipboard_paste_required=true",
        "external_image_clipboard_paste=true",
        "external_image_clipboard_picture_count=[1-9]",
        "external_image_clipboard_picture_png_bytes=[1-9]",
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
        "native_fill_color_menu_item=true",
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
                "using FreeX.Core.Calc;",
                "public async Task OpenActivatedFilesAsync(IReadOnlyList<IStorageItem> files)",
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
                "`"FormatCellsSuperscriptBox`"",
                "`"FormatCellsSubscriptBox`"",
                "`"FormatCellsLockedBox`"",
                "`"FormatCellsHiddenBox`"",
                "var currentMergeCells = _session.IsSelectedRangeMerged;",
                "MergeCells: ReadChangedFormatCellsBool(currentMergeCells, mergeCellsBox)",
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
                "e.Key is Key.Z or Key.Y or Key.X or Key.C or Key.V or Key.A or Key.B or Key.D or Key.I or Key.R or Key.U",
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
                "_mergeAndCenterMenuItem.Click += (_, _) => MergeAndCenterSelectedRange();",
                "_unmergeCellsMenuItem.Header = `"Unmerge Cells`";",
                "_unmergeCellsMenuItem.Click += (_, _) => UnmergeSelectedRange();",
                "formatMenu.Items.Add(_mergeAndCenterMenuItem);",
                "formatMenu.Items.Add(_unmergeCellsMenuItem);",
                "_mergeAndCenterButton.IsEnabled = isIdle;",
                "_mergeAndCenterMenuItem.IsEnabled = _mergeAndCenterButton.IsEnabled;",
                "_unmergeCellsMenuItem.IsEnabled = isIdle && _session.IsSelectedRangeMerged;",
                "private void MergeAndCenterSelectedRange()",
                "_session.MergeAndCenterSelectedRange()",
                "private void UnmergeSelectedRange()",
                "_session.UnmergeSelectedRange()",
                "HasMergeAndCenterButton: _mergeAndCenterButton.Content?.ToString() == `"Merge & Center`"",
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
                "private readonly NativeMenuItem _findMenuItem = new();",
                "private readonly NativeMenuItem _findNextMenuItem = new();",
                "private readonly NativeMenuItem _replaceMenuItem = new();",
                "private readonly NativeMenuItem _goToMenuItem = new();",
                "private readonly NativeMenuItem _goToSpecialMenuItem = new();",
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
                "HasNativeFindMenuItem: HasNativeMenuItem(_findMenuItem, `"Find...`")",
                "HasNativeFindNextMenuItem: HasNativeMenuItem(_findNextMenuItem, `"Find Next`")",
                "HasNativeReplaceMenuItem: HasNativeMenuItem(_replaceMenuItem, `"Replace...`")",
                "HasNativeGoToMenuItem: HasNativeMenuItem(_goToMenuItem, `"Go To...`")",
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
                "FirstOrDefault(item => item.IsEnabled)?",
                ".Focus();",
                "private Button? FindSheetTabButton(SheetId sheetId)",
                "button.Tag is SheetId tag && tag == sheetId",
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
                "Header = `"Help`"",
                "TopLevel.GetTopLevel(this)?.Launcher",
                "AppHelpInfo.BuildAboutText(versionText, PlatformAboutSummary)",
                "LegalNoticeProvider.GetDocuments().Select(document =>",
                "internal MacOsLaunchSmokeSnapshot CreateLaunchSmokeSnapshot()",
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
                "public const string VerifyImageClipboardPasteArgument = `"--macos-launch-smoke-verify-image-clipboard`";",
                "startupArguments = filteredArguments.ToArray();",
                "verifyImageClipboardPaste = true;",
                "new MacOsLaunchSmokeOptions(reportPath, verifyImageClipboardPaste)",
                "await mainWindow.TryPasteLaunchSmokeClipboardImageAsync();",
                "IsPassed(snapshot, options, initialExternalImageClipboardPictureCount)",
                "HasExternalImageClipboardPasteEvidence(",
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
                "HasNativeFindMenuItem &&",
                "HasNativeFindNextMenuItem &&",
                "HasNativeReplaceMenuItem &&",
                "HasNativeGoToMenuItem &&",
                "HasNativeGoToSpecialMenuItem &&",
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
                "native_new_workbook_menu_item=",
                "external_image_clipboard_paste_required=",
                "external_image_clipboard_paste=",
                "external_image_clipboard_picture_count=",
                "external_image_clipboard_picture_png_bytes=",
                "native_open_recent_menu_item=",
                "native_open_recent_item_count=",
                "native_close_workbook_menu_item=",
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
                "native_select_all_menu_item=",
                "native_find_menu_item=",
                "native_find_next_menu_item=",
                "native_replace_menu_item=",
                "native_go_to_menu_item=",
                "native_go_to_special_menu_item=",
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
                "AutoSumFormulaPlanner.BuildFormula(ActiveSheet, functionName, target)",
                "CreateEditCellsCommand([(target, Cell.FromFormula(formula))])",
                "SelectCell(GetNextAutoSumCell(target));",
                "public bool CanFillSelectedRange(FillCellsDirection direction)",
                "public WorkbookCellEditResult FillSelectedRange(FillCellsDirection direction)",
                "new FillCellsCommand(sheetId, sheetRange, direction)",
                "private static string GetFillCellsTitle(FillCellsDirection direction)",
                "FillCellsDirection.Down => `"Fill Down`"",
                "FillCellsDirection.Right => `"Fill Right`"",
                "FillCellsDirection.Up => `"Fill Up`"",
                "FillCellsDirection.Left => `"Fill Left`"",
                "public WorkbookCellEditResult SetSelectedRangeBorderPreset(CellBorderPreset preset)",
                "CreateBorderPresetCommand(range, preset)",
                "CellBorderPresetPlanner.Plan(preset, range, range.Start, borderStyle, borderColor)",
                "CellBorderPresetPlanner.RequiresPerCellPlanning(preset)",
                "BorderShortcutService.HasBorderChanges(diff)",
                "GroupedApplyStyleCommand(targetSheetIds, sourceRange, diff)",
                "public WorkbookCellEditResult ApplySelectedRangeCompactFormat(",
                "bool? mergeCells = null",
                "CreateFormatCellsMergeCommands(range, shouldMerge)",
                "public bool IsSelectedRangeMerged => CellMergePlanner.IsSelectionMerged(ActiveSheet, SelectedRange);",
                "public WorkbookCellEditResult MergeAndCenterSelectedRange()",
                "CreateMergeAndCenterCommand(range)",
                "public WorkbookCellEditResult UnmergeSelectedRange()",
                "CreateUnmergeCommands(range)",
                "private IWorkbookCommand CreateMergeAndCenterCommand(GridRange range)",
                "CellMergePlanner.CreateMergeAndCenterCommands(sheetId, sheetRange)",
                "private IReadOnlyList<IWorkbookCommand> CreateFormatCellsMergeCommands(GridRange range, bool mergeCells)",
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
