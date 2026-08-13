using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using FluentAssertions;

namespace FreeX.App.Services.Tests;

public sealed class MacOsBundleMetadataTests
{
    private const string NativeWorkbookContentType = "io.github.tony-xmelon.freex.workbook";
    private const string NativeWorkbookMimeType = "application/vnd.freex.workbook+json";

    [Fact]
    public void InfoPlist_DefinesPreviewBundleIdentityAndDocumentRegistration()
    {
        var plistPath = RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "Packaging", "macos", "Info.plist");
        var plist = XDocument.Load(plistPath);
        var project = XDocument.Load(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "FreeX.App.Avalonia.csproj"));
        var expectedLocalizations = HostResourceLocalizations();

        PlistString(plist, "CFBundleDevelopmentRegion").Should().Be(HostNeutralResourcesLanguage());
        PlistStringArray(plist, "CFBundleLocalizations").Should().Equal(expectedLocalizations);
        PlistString(plist, "CFBundleDisplayName").Should().Be("FreeX");
        PlistString(plist, "CFBundleExecutable").Should().Be("FreeX");
        PlistString(plist, "CFBundleExecutable").Should().Be(ProjectProperty(project, "AssemblyName"));
        PlistString(plist, "CFBundleIdentifier").Should().Be("io.github.tony-xmelon.freex");
        PlistString(plist, "CFBundlePackageType").Should().Be("APPL");
        PlistString(plist, "LSMinimumSystemVersion").Should().Be("12.0");
        PlistString(plist, "CFBundleIconFile").Should().Be("FreeX.icns");
        AssertIcnsFile(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "Packaging", "macos", "FreeX.icns"));

        var documentTypesElement = PlistArray(plist, "CFBundleDocumentTypes");
        documentTypesElement.Should().NotBeNull("the preview app should advertise Finder-openable workbook formats");
        var documentTypes = documentTypesElement!
            .Elements("dict")
            .ToList();
        documentTypes.Should().HaveCount(2);

        var nativeWorkbook = documentTypes[0];
        PlistString(nativeWorkbook, "CFBundleTypeName").Should().Be("FreeX Workbook");
        PlistString(nativeWorkbook, "CFBundleTypeRole").Should().Be("Editor");
        PlistString(nativeWorkbook, "LSHandlerRank").Should().Be("Owner");
        PlistStringArray(nativeWorkbook, "CFBundleTypeExtensions").Should().Equal("fxl");
        PlistStringArray(nativeWorkbook, "LSItemContentTypes").Should().Equal(NativeWorkbookContentType);

        var exportedTypesElement = PlistArray(plist, "UTExportedTypeDeclarations");
        exportedTypesElement.Should().NotBeNull("the native .fxl workbook should advertise a stable UTI");
        var exportedTypes = exportedTypesElement!
            .Elements("dict")
            .ToList();
        exportedTypes.Should().HaveCount(1);

        var nativeWorkbookType = exportedTypes[0];
        PlistString(nativeWorkbookType, "UTTypeIdentifier").Should().Be(NativeWorkbookContentType);
        PlistString(nativeWorkbookType, "UTTypeDescription").Should().Be("FreeX Workbook");
        PlistStringArray(nativeWorkbookType, "UTTypeConformsTo").Should().Equal("public.json");

        var tagSpecification = PlistValue(nativeWorkbookType, "UTTypeTagSpecification");
        tagSpecification.Should().NotBeNull();
        tagSpecification!.Name.LocalName.Should().Be("dict");
        PlistStringArray(tagSpecification, "public.filename-extension").Should().Equal("fxl");
        PlistString(tagSpecification, "public.mime-type").Should().Be(NativeWorkbookMimeType);

        var importedWorkbooks = documentTypes[1];
        PlistString(importedWorkbooks, "CFBundleTypeName").Should().Be("Spreadsheet Workbooks");
        PlistString(importedWorkbooks, "CFBundleTypeRole").Should().Be("Viewer");
        PlistString(importedWorkbooks, "LSHandlerRank").Should().Be("Alternate");
        PlistStringArray(importedWorkbooks, "LSItemContentTypes").Should().BeEmpty();
        PlistStringArray(importedWorkbooks, "CFBundleTypeExtensions")
            .Should()
            .Equal("xlsx", "xlsm", "xltx", "xltm", "xls", "xlsb", "xlt", "csv", "tsv", "tab");
    }

    [Fact]
    public void MacOsWorkflow_VerifiesPublishedBundleBeforeUploadingArtifact()
    {
        var workflow = File.ReadAllText(RepositoryFileLocator.Find(".github", "workflows", "macos-app.yml"));
        var project = XDocument.Load(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "FreeX.App.Avalonia.csproj"));
        var runtimes = ProjectProperty(project, "RuntimeIdentifiers")!.Split(';', StringSplitOptions.RemoveEmptyEntries);

        workflow.Should().Contain("runs-on: ${{ matrix.runner }}");
        workflow.Should().Contain("runner: macos-15");
        workflow.Should().Contain("runner: macos-15-intel");
        workflow.Should().NotContain("runner: macos-latest");
        WorkflowRuntimeRunnerPairs(workflow).Should().Equal("osx-arm64=macos-15", "osx-x64=macos-15-intel");
        workflow.Should().Contain("runtime:");
        foreach (var runtime in runtimes)
            workflow.Should().Contain(runtime);

        workflow.Should().Contain("dotnet publish src/FreeX.App.Avalonia/FreeX.App.Avalonia.csproj");
        workflow.Should().Contain("--self-contained true");
        workflow.Should().Contain("-p:UseAppHost=true");
        workflow.Should().Contain("MACOS_CODESIGN_CERTIFICATE_P12: ${{ secrets.MACOS_CODESIGN_CERTIFICATE_P12 }}");
        workflow.Should().Contain("MACOS_CODESIGN_CERTIFICATE_PASSWORD: ${{ secrets.MACOS_CODESIGN_CERTIFICATE_PASSWORD }}");
        workflow.Should().Contain("MACOS_DEVELOPER_ID_APPLICATION: ${{ secrets.MACOS_DEVELOPER_ID_APPLICATION }}");
        workflow.Should().Contain("MACOS_NOTARY_APPLE_ID: ${{ secrets.MACOS_NOTARY_APPLE_ID }}");
        workflow.Should().Contain("MACOS_NOTARY_TEAM_ID: ${{ secrets.MACOS_NOTARY_TEAM_ID }}");
        workflow.Should().Contain("MACOS_NOTARY_PASSWORD: ${{ secrets.MACOS_NOTARY_PASSWORD }}");
        workflow.Should().Contain("plutil -lint");
        workflow.Should().Contain("cp src/FreeX.App.Avalonia/Packaging/macos/FreeX.icns \"$app/Contents/Resources/FreeX.icns\"");
        workflow.Should().Contain("test -f \"$app/Contents/Resources/FreeX.icns\"");
        workflow.Should().Contain("PlistBuddy -c 'Print :CFBundleExecutable'");
        workflow.Should().Contain("PlistBuddy -c 'Print :CFBundleIconFile'");
        workflow.Should().Contain("PlistBuddy -c 'Print :CFBundleDocumentTypes:0:CFBundleTypeExtensions:0'");
        workflow.Should().Contain("PlistBuddy -c 'Print :CFBundleDocumentTypes:1:CFBundleTypeExtensions:0'");
        workflow.Should().Contain("lipo -archs");
        workflow.Should().Contain("- name: Capture runner toolchain evidence");
        workflow.Should().Contain("evidence_path=\"$artifact_root/freex-$runtime-macos-evidence.txt\"");
        workflow.Should().Contain("echo \"runner_label=${{ matrix.runner }}\"");
        workflow.Should().Contain("echo \"runner_os=${RUNNER_OS:-unknown}\"");
        workflow.Should().Contain("echo \"runner_arch=${RUNNER_ARCH:-unknown}\"");
        workflow.Should().Contain("echo \"image_os=${ImageOS:-unknown}\"");
        workflow.Should().Contain("echo \"image_version=${ImageVersion:-unknown}\"");
        workflow.Should().Contain("echo \"[sw_vers]\"");
        workflow.Should().Contain("sw_vers");
        workflow.Should().Contain("echo \"[uname -m]\"");
        workflow.Should().Contain("uname -m");
        workflow.Should().Contain("echo \"[dotnet --info]\"");
        workflow.Should().Contain("dotnet --info");
        workflow.Should().Contain("echo \"[xcodebuild -version]\"");
        workflow.Should().Contain("xcodebuild -version");
        workflow.Should().Contain("} | tee \"$evidence_path\"");
        workflow.Should().Contain("- name: Test portable PDF macOS route");
        workflow.Should().Contain("dotnet test tests/FreeX.App.Services.Tests/FreeX.App.Services.Tests.csproj");
        workflow.Should().Contain("FullyQualifiedName~FreeX.App.Services.Tests.PortablePdfDocumentExporterTests");
        workflow.Should().Contain("FullyQualifiedName~FreeX.App.Services.Tests.PortablePdfExportPlannerTests");
        workflow.Should().Contain("FullyQualifiedName~FreeX.App.Services.Tests.PortablePdfPageContentPlannerTests");
        workflow.Should().Contain("FullyQualifiedName~FreeX.App.Services.Tests.WorkbookExportPrintPlannerTests");
        workflow.Should().Contain("FullyQualifiedName~FreeX.App.Services.Tests.AppServicesPortabilityGuardTests");
        workflow.Should().Contain("FullyQualifiedName~FreeX.App.Services.Tests.ApplicationDataPathGuardTests");
        workflow.Should().Contain("FullyQualifiedName~FreeX.App.Services.Tests.AvaloniaShellSourceTests");
        workflow.Should().Contain("FullyQualifiedName~FreeX.App.Services.Tests.MacOsLaunchSmokeReportKeyDriftGuardTests");
        workflow.Should().Contain("dotnet test tests/FreeX.Core.Model.Tests/FreeX.Core.Model.Tests.csproj");
        workflow.Should().Contain("FullyQualifiedName~FreeX.Core.Model.Tests.ExportPathPlannerTests");
        workflow.Should().Contain("freex-${{ matrix.runtime }}-portable-pdf-exporter-tests.trx");
        workflow.Should().Contain("freex-${{ matrix.runtime }}-export-path-tests.trx");
        workflow.Should().Contain("artifacts/freex-${{ matrix.runtime }}-portable-pdf-exporter-tests.trx");
        workflow.Should().Contain("artifacts/freex-${{ matrix.runtime }}-export-path-tests.trx");
        workflow.Should().Contain("--results-directory artifacts");
        workflow.IndexOf("- name: Capture runner toolchain evidence", StringComparison.Ordinal)
            .Should()
            .BeLessThan(workflow.IndexOf("- name: Test portable PDF macOS route", StringComparison.Ordinal));
        workflow.IndexOf("- name: Test portable PDF macOS route", StringComparison.Ordinal)
            .Should()
            .BeLessThan(workflow.IndexOf("- name: Build app project", StringComparison.Ordinal));
        workflow.IndexOf("- name: Build app project", StringComparison.Ordinal)
            .Should()
            .BeLessThan(workflow.IndexOf("- name: Publish app bundle", StringComparison.Ordinal));
        workflow.Should().Contain("smoke_log=\"$artifact_root/freex-$runtime-macos-packaging-smoke.log\"");
        workflow.Should().Contain("launch_smoke_report=\"$artifact_root/freex-$runtime-macos-launch-smoke.txt\"");
        workflow.Should().Contain("notary_log=\"$artifact_root/freex-$runtime-macos-notarization.log\"");
        workflow.Should().Contain("tester_instructions_path=\"$artifact_root/freex-$runtime-macos-tester-instructions.md\"");
        workflow.Should().Contain("signing_mode=\"ad-hoc\"");
        workflow.Should().Contain("signing_mode=\"developer-id\"");
        workflow.Should().Contain("stapler_validated=\"false\"");
        workflow.Should().Contain("[[ \"$GITHUB_EVENT_NAME\" == \"pull_request\" && \"$has_any_signing_secret\" == \"true\" ]]");
        workflow.Should().Contain("Developer ID signing is disabled for pull_request events; using ad-hoc signing.");
        workflow.Should().Contain("base64 -D > \"$certificate_path\"");
        workflow.Should().Contain("security create-keychain");
        workflow.Should().Contain("security import \"$certificate_path\"");
        workflow.Should().Contain("security set-key-partition-list");
        workflow.Should().Contain("security find-identity -v -p codesigning \"$keychain_path\" | grep -F \"$MACOS_DEVELOPER_ID_APPLICATION\"");
        workflow.Should().Contain("codesign --force --deep --options runtime --timestamp --sign \"$MACOS_DEVELOPER_ID_APPLICATION\" \"$app\"");
        workflow.Should().Contain("codesign --force --deep --sign - \"$app\"");
        workflow.Should().Contain("xcrun notarytool submit \"$zip_path\"");
        workflow.Should().Contain("--output-format json | tee \"$notary_log\"");
        workflow.Should().Contain("grep -q '\"status\": *\"Accepted\"' \"$notary_log\"");
        workflow.Should().Contain("xcrun stapler staple \"$app\"");
        workflow.Should().Contain("xcrun stapler validate \"$app\"");
        workflow.Should().Contain("stapler_validated=\"true\"");
        workflow.Should().Contain("notarization_status=\"accepted\"");
        workflow.Should().Contain("notarization_status=\"skipped_missing_credentials\"");
        workflow.Should().Contain("echo \"[bundle]\"");
        workflow.Should().Contain("echo \"binary_archs=$binary_archs\"");
        workflow.Should().Contain("app_info_plist=\"$unzip_root/FreeX.app/Contents/Info.plist\"");
        workflow.Should().Contain("echo \"artifact_bundle_metadata_subject=unzipped_app_bundle\"");
        workflow.Should().Contain("echo \"bundle_executable=$(/usr/libexec/PlistBuddy -c 'Print :CFBundleExecutable' \"$app_info_plist\")\"");
        workflow.Should().Contain("echo \"bundle_icon=$(/usr/libexec/PlistBuddy -c 'Print :CFBundleIconFile' \"$app_info_plist\")\"");
        workflow.Should().Contain("echo \"bundle_identifier=$(/usr/libexec/PlistBuddy -c 'Print :CFBundleIdentifier' \"$app_info_plist\")\"");
        workflow.Should().Contain("echo \"bundle_package_type=$(/usr/libexec/PlistBuddy -c 'Print :CFBundlePackageType' \"$app_info_plist\")\"");
        workflow.Should().Contain("echo \"bundle_minimum_system_version=$(/usr/libexec/PlistBuddy -c 'Print :LSMinimumSystemVersion' \"$app_info_plist\")\"");
        workflow.Should().Contain("echo \"bundle_high_resolution_capable=$(/usr/libexec/PlistBuddy -c 'Print :NSHighResolutionCapable' \"$app_info_plist\")\"");
        workflow.Should().Contain("echo \"artifact_document_extensions_subject=unzipped_app_bundle\"");
        workflow.Should().Contain("echo \"codesign_verified=true\"");
        workflow.Should().Contain("echo \"codesign_mode=$signing_mode\"");
        workflow.Should().Contain("echo \"notarization_status=$notarization_status\"");
        workflow.Should().Contain("echo \"stapler_validated=$stapler_validated\"");
        workflow.Should().Contain("echo \"zip_sha256=$zip_sha256\"");
        workflow.Should().Contain("} >> \"$evidence_path\"");
        workflow.Should().Contain("echo \"smoke_status=passed\" >> \"$evidence_path\"");
        workflow.Should().Contain("echo \"smoke_status=skipped_host_arch_mismatch\" >> \"$evidence_path\"");
        workflow.Should().Contain("codesign --verify --deep --strict");
        workflow.Should().Contain("host_arch=\"$(uname -m)\"");
        workflow.Should().Contain("ditto -x -k \"$zip_path\" \"$unzip_root\"");
        workflow.Should().Contain("test -x \"$unzip_root/FreeX.app/Contents/MacOS/FreeX\"");
        workflow.Should().Contain("(cd \"$artifact_root\" && shasum -a 256 \"$zip_name\" > \"$zip_name.sha256\")");
        workflow.Should().Contain("(cd \"$artifact_root\" && shasum -a 256 -c \"$zip_name.sha256\")");
        workflow.Should().Contain("zip_sha256=\"$(cut -d ' ' -f 1 \"$artifact_root/$zip_name.sha256\")\"");
        workflow.Should().Contain("cat > \"$tester_instructions_path\" <<EOF");
        workflow.Should().Contain("This artifact is a preview build for macOS port validation. It is not a public release channel.");
        workflow.Should().Contain("Use osx-arm64 for Apple Silicon Macs and osx-x64 for Intel Macs.");
        workflow.Should().Contain("Unzip the GitHub Actions artifact wrapper first; these files are inside it.");
        workflow.Should().Contain("ditto -x -k $zip_name .");
        workflow.Should().Contain("Ad-hoc signed or non-notarized previews may require Control-click or right-click > Open for trusted internal testing.");
        workflow.Should().Contain("codesign --verify --deep --strict \"$unzip_root/FreeX.app\"");
        workflow.Should().Contain("\"$validation_host\" --packaging-smoke | tee \"$smoke_log\"");
        workflow.Should().Contain("grep -q \"macOS Preview Workbook\" \"$smoke_log\"");
        workflow.Should().Contain("grep -q \"drawing_object_previews=3\" \"$smoke_log\"");
        workflow.Should().Contain("grep -q \"roundtrip_drawing_object_previews=3\" \"$smoke_log\"");
        workflow.Should().Contain("grep -q \"format_cells_style_roundtrip=true\" \"$smoke_log\"");
        workflow.Should().Contain("\"$validation_host\" --packaging-smoke \"$smoke_file\" | tee -a \"$smoke_log\"");
        workflow.Should().Contain("grep -q \"Packaging smoke opened\" \"$smoke_log\"");
        workflow.Should().Contain("grep -q \"edited, saved, and reopened\" \"$smoke_log\"");
        workflow.Should().Contain("format_cells_style_roundtrip_count=\"$(grep -c \"format_cells_style_roundtrip=true\" \"$smoke_log\")\"");
        workflow.Should().Contain("test \"$format_cells_style_roundtrip_count\" -ge 2");
        workflow.Should().Contain("echo \"format_cells_style_roundtrip=true\"");
        workflow.Should().Contain("echo \"format_cells_style_roundtrip_count=$format_cells_style_roundtrip_count\"");
        workflow.Should().Contain("bash tools/Run-PackagedProductLaunchProbe.sh");
        workflow.Should().Contain("--executable \"$unzip_root/FreeX.app/Contents/MacOS/FreeX\"");
        workflow.Should().Contain("grep -Fqx \"packaged_product_launch_status=passed\" \"$packaged_product_launch_report\"");
        workflow.Should().Contain("grep -Fqx \"packaged_product_executable=$unzip_root/FreeX.app/Contents/MacOS/FreeX\" \"$packaged_product_launch_report\"");
        workflow.IndexOf("bash tools/Run-PackagedProductLaunchProbe.sh", StringComparison.Ordinal)
            .Should().BeLessThan(workflow.IndexOf("echo \"smoke_status=passed\"", StringComparison.Ordinal));
        workflow.Should().Contain("lsregister -f \"$unzip_root/FreeX.app\"");
        workflow.Should().Contain("run_launchservices_with_validation \"$launch_smoke_report\" \"$launch_smoke_file\"");
        workflow.Should().Contain("open -W -n -b io.github.tony-xmelon.freex \"$launch_smoke_file\"");
        workflow.Should().Contain("--macos-launch-smoke-diagnostics-dir \"$app_diagnostics_dir\"");
        workflow.Should().Contain("grep -q \"app_diagnostics_directory_configured=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("app_diagnostics_events_path=\"$app_diagnostics_dir/events.jsonl\"");
        workflow.Should().Contain("grep -q '\"eventName\":\"app_start\"' \"$app_diagnostics_events_path\"");
        workflow.Should().Contain("grep -q '\"eventName\":\"app_ready\"' \"$app_diagnostics_events_path\"");
        workflow.Should().Contain("grep -q '\"eventName\":\"macos_launch_smoke\"' \"$app_diagnostics_events_path\"");
        workflow.Should().NotContain("--macos-launch-smoke-verify-image-clipboard");
        workflow.Should().NotContain("--macos-launch-smoke-verify-live-command-keys");
        workflow.Should().NotContain("<<'APPLESCRIPT'");
        workflow.Should().Contain("macos_launch_smoke=missing_report");
        workflow.Should().Contain("grep -q \"macos_launch_smoke=passed\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"window_shown=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"opened_source_path=.*freex-$runtime-launch.csv\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"external_image_clipboard_paste_required=false\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"live_command_key_smoke_required=false\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"live_command_key_smoke=not_required\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"macos_accessibility_smoke=passed\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"a11y_formula_box_name=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"a11y_formula_box_help=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"a11y_status_text_name=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"a11y_status_text_value=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"a11y_cell_address_name=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"a11y_selection_stats_name=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"new_sheet_button=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"toolbar_format_painter_button=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"toolbar_fill_cells_button=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"toolbar_fill_down_menu_item=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"toolbar_fill_right_menu_item=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"toolbar_fill_up_menu_item=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"toolbar_fill_left_menu_item=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"toolbar_clear_button=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"toolbar_clear_all_menu_item=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"toolbar_clear_formats_menu_item=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"toolbar_clear_contents_menu_item=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"toolbar_clear_comments_menu_item=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"toolbar_clear_hyperlinks_menu_item=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"toolbar_borders_button=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"focusable_sheet_tab=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"focusable_active_sheet_tab=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"shell_focus_cycle_targets=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"sheet_tab_context_keyboard_help=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"sheet_tab_context_rename_menu_item=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"sheet_tab_context_tab_color_menu_item=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"sheet_tab_context_no_color_menu_item=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"sheet_tab_context_select_all_sheets_menu_item=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"sheet_tab_context_ungroup_sheets_menu_item=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_file_menu=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_new_workbook_menu_item=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_open_menu_item=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_open_recent_menu_item=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_open_recent_item_count=[1-9]\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_save_menu_item=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_save_as_menu_item=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_export_pdf_menu_item=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_share_workbook_menu_item=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_workbook_statistics_menu_item=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_close_workbook_menu_item=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_dock_top_level_menu_order=File|Home|Insert|Page Layout|Formulas|Data|Review|View|Sheet|Window|Help\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_dock_menu_installed=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_dock_file_menu=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_dock_file_menu_item_count=[1-9]\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_data_menu=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_select_all_menu_item=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_go_to_special_menu_item=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_sort_ascending_menu_item=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_sort_descending_menu_item=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"macos_dialog_smoke=passed\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"macos_dialog_smoke_attempted=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"macos_dialog_smoke_status=passed\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"macos_dialog_activation_completed=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"find_dialog=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"find_dialog_text_box=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"find_dialog_action_buttons=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"find_dialog_options=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"find_dialog_format_controls=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"find_dialog_compact_layout=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"find_dialog_result_closed_without_accept=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"replace_dialog=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"replace_dialog_text_boxes=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"replace_dialog_action_buttons=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"replace_dialog_options=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"replace_dialog_format_controls=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"replace_dialog_compact_layout=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"replace_dialog_result_closed_without_accept=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"go_to_dialog=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"go_to_dialog_reference_controls=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"go_to_dialog_compact_layout=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"go_to_dialog_result_closed_without_accept=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"go_to_special_dialog=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"go_to_special_dialog_kind_controls=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"go_to_special_dialog_value_type_controls=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"go_to_special_dialog_compact_layout=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"go_to_special_dialog_result_closed_without_accept=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_top_level_menu_order=File|Home|Insert|Page Layout|Formulas|Data|Review|View|Sheet|Window|Help\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_home_menu=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_insert_menu=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_page_layout_menu=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_formulas_menu=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_data_menu=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_view_menu=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_sheet_menu=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_window_menu=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_help_menu=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_new_sheet_menu_item=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_rename_sheet_menu_item=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_duplicate_sheet_menu_item=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_move_sheet_left_menu_item=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_move_sheet_right_menu_item=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_tab_color_menu_item=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_tab_color_clear_item=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_tab_color_swatch_count=69\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_select_all_sheets_menu_item=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_ungroup_sheets_menu_item=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_hide_sheet_menu_item=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_unhide_sheet_menu_item=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_delete_sheet_menu_item=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_undo_menu_item=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_redo_menu_item=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_cut_menu_item=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_copy_menu_item=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_paste_menu_item=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_paste_special_menu_item=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_format_painter_menu_item=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_paste_special_comments_menu_item=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_paste_special_validation_menu_item=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_paste_special_all_except_borders_menu_item=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_paste_special_all_merging_conditional_formats_menu_item=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_paste_special_column_widths_menu_item=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_paste_special_formulas_and_number_formats_menu_item=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_paste_special_values_and_number_formats_menu_item=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_paste_special_values_and_source_formatting_menu_item=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_paste_special_keep_source_column_widths_menu_item=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_paste_special_paste_link_menu_item=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_paste_special_text_menu_item=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_paste_special_unicode_text_menu_item=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_paste_special_picture_menu_item=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_paste_special_linked_picture_menu_item=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_fill_cells_menu_item=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_fill_down_menu_item=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_fill_right_menu_item=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_fill_up_menu_item=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_fill_left_menu_item=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_clear_menu_item=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_clear_all_menu_item=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_clear_formats_menu_item=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_clear_contents_menu_item=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_clear_comments_menu_item=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_clear_hyperlinks_menu_item=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_bold_menu_item=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_italic_menu_item=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_underline_menu_item=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_double_underline_menu_item=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_strikethrough_menu_item=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_increase_font_size_menu_item=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_decrease_font_size_menu_item=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_fill_color_menu_item=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_clear_fill_menu_item=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_font_color_menu_item=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_borders_menu_item=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_borders_preset_count=14\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"toolbar_merge_and_center_button=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_cell_styles_menu_item=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_cell_styles_preset_count=33\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_horizontal_text_menu_item=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_angle_counterclockwise_menu_item=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_angle_clockwise_menu_item=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_vertical_text_menu_item=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_rotate_text_up_menu_item=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_rotate_text_down_menu_item=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_currency_format_menu_item=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_percent_format_menu_item=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_comma_style_menu_item=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_increase_decimal_menu_item=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_decrease_decimal_menu_item=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_align_top_menu_item=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_align_middle_menu_item=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_align_bottom_menu_item=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"toolbar_wrap_text_button=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_wrap_text_menu_item=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_merge_and_center_menu_item=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_unmerge_cells_menu_item=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_decrease_indent_menu_item=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_increase_indent_menu_item=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_align_left_menu_item=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_align_center_menu_item=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_align_right_menu_item=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_show_gridlines_menu_item=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_show_headings_menu_item=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_zoom_in_menu_item=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_zoom_out_menu_item=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_zoom_100_menu_item=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_zoom_to_selection_menu_item=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_freeze_panes_menu_item=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_freeze_top_row_menu_item=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_freeze_first_column_menu_item=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_unfreeze_panes_menu_item=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_show_formulas_menu_item=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_minimize_window_menu_item=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_zoom_window_menu_item=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_bring_all_to_front_menu_item=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_help_online_menu_item=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_send_feedback_menu_item=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_check_for_updates_menu_item=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_about_menu_item=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_legal_notices_menu_item=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_quit_menu_item=true\" \"$launch_smoke_report\"");
        var appArtifactUpload = ExtractWorkflowStepBlock(workflow, "Upload app artifact");
        appArtifactUpload.Should().Contain("artifacts/freex-${{ matrix.runtime }}-macos-app.zip");
        appArtifactUpload.Should().Contain("artifacts/freex-${{ matrix.runtime }}-macos-app.zip.sha256");
        appArtifactUpload.Should().Contain("artifacts/freex-${{ matrix.runtime }}-macos-evidence.txt");
        appArtifactUpload.Should().Contain("artifacts/freex-${{ matrix.runtime }}-macos-packaging-smoke.log");
        appArtifactUpload.Should().Contain("artifacts/freex-${{ matrix.runtime }}-macos-launch-smoke.txt");
        appArtifactUpload.Should().Contain("artifacts/freex-${{ matrix.runtime }}-macos-notarization.log");
        appArtifactUpload.Should().Contain("artifacts/freex-${{ matrix.runtime }}-macos-tester-instructions.md");
        appArtifactUpload.Should().NotContain("portable-pdf-exporter-tests.trx");
        appArtifactUpload.Should().NotContain("export-path-tests.trx");
        appArtifactUpload.Should().NotContain("macos-app-diagnostics");
        appArtifactUpload.Should().Contain("if-no-files-found: error");

        var diagnosticsUpload = ExtractWorkflowStepBlock(workflow, "Upload app diagnostics");
        diagnosticsUpload.Should().Contain("if: always()");
        diagnosticsUpload.Should().Contain("name: freex-${{ github.run_id }}-${{ github.run_attempt }}-${{ matrix.runtime }}-macos-diagnostics");
        diagnosticsUpload.Should().Contain("artifacts/freex-${{ matrix.runtime }}-portable-pdf-exporter-tests.trx");
        diagnosticsUpload.Should().Contain("artifacts/freex-${{ matrix.runtime }}-export-path-tests.trx");
        diagnosticsUpload.Should().Contain("artifacts/freex-${{ matrix.runtime }}-macos-app-diagnostics/**");
        diagnosticsUpload.Should().Contain("if-no-files-found: warn");
    }

    [Fact]
    public void MacOsWorkflow_DistributionCandidatePublicationWaitsForAggregateReadiness()
    {
        var workflow = File.ReadAllText(RepositoryFileLocator.Find(".github", "workflows", "macos-app.yml"));
        var aggregateJob = ExtractWorkflowJobBlock(workflow, "macos-preview-readiness");
        var releaseJob = ExtractWorkflowJobBlock(workflow, "publish-distribution-candidate");

        releaseJob.Should().Contain("needs: [macos-app, macos-preview-readiness]");
        releaseJob.Should().Contain("if: ${{ github.event_name == 'workflow_dispatch' && inputs.distribution_candidate == true }}");
        releaseJob.Should().Contain("- name: Download macOS app artifacts");
        releaseJob.Should().Contain("pattern: freex-${{ github.run_id }}-${{ github.run_attempt }}-*-macos-app");
        workflow.IndexOf(aggregateJob, StringComparison.Ordinal)
            .Should()
            .BeLessThan(workflow.IndexOf(releaseJob, StringComparison.Ordinal));
    }

    [Fact]
    public void PackagingSmoke_IsOwnedByValidationHostBeforeAvaloniaLifetime()
    {
        var program = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "Program.cs"));
        var validationProgram = File.ReadAllText(
            RepositoryFileLocator.Find("tools", "FreeX.Validation.Avalonia", "Program.cs"));
        var validationAccessProgram = File.ReadAllText(RepositoryFileLocator.Find(
            "tools", "FreeX.Validation.Avalonia", "RendererHost", "Program.ValidationHost.cs"));

        program.Should().NotContain("PackagingSmokeCommand");
        validationProgram.Should().Contain("PackagingSmokeCommand.TryRun");
        validationProgram.Should().Contain("ValidationHostCommandRouteExecutor.Immediate(");
        validationProgram.Should().Contain("ValidationHostCommandRouteExecutor.Run(");
        program.Should().NotContain("MacOsLaunchSmokeOptions.TryParse(");
        program.Should().NotContain("internal static int RunValidationToolHost(");
        validationAccessProgram.Should().Contain("internal static int RunValidationToolHost(");
        program.Should().Contain("StartWithClassicDesktopLifetime(arguments)");
    }

    private static string? PlistString(XDocument plist, string key) =>
        PlistValue(plist, key)?.Name.LocalName == "string"
            ? PlistValue(plist, key)!.Value
            : null;

    private static string? PlistString(XElement dict, string key) =>
        PlistValue(dict, key)?.Name.LocalName == "string"
            ? PlistValue(dict, key)!.Value
            : null;

    private static IReadOnlyList<string> PlistStringArray(XElement dict, string key) =>
        PlistStringArray(PlistValue(dict, key));

    private static IReadOnlyList<string> PlistStringArray(XDocument plist, string key) =>
        PlistStringArray(PlistValue(plist, key));

    private static IReadOnlyList<string> PlistStringArray(XElement? value) =>
        value?.Name.LocalName == "array"
            ? value.Elements("string").Select(element => element.Value).ToList()
            : [];

    private static XElement? PlistArray(XDocument plist, string key) =>
        PlistValue(plist, key)?.Name.LocalName == "array"
            ? PlistValue(plist, key)
            : null;

    private static XElement? PlistValue(XDocument plist, string key)
    {
        var dict = plist.Root?.Element("dict");
        return dict is null ? null : PlistValue(dict, key);
    }

    private static XElement? PlistValue(XElement dict, string key)
    {
        var elements = dict.Elements().ToList();
        for (var index = 0; index < elements.Count - 1; index++)
        {
            if (elements[index].Name.LocalName == "key" &&
                elements[index].Value == key)
            {
                return elements[index + 1];
            }
        }

        return null;
    }

    private static string? ProjectProperty(XDocument project, string name) =>
        project.Root?
            .Elements("PropertyGroup")
            .Elements(name)
            .Select(element => element.Value)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    private static IReadOnlyList<string> HostResourceLocalizations()
    {
        var neutralCulture = HostNeutralResourcesLanguage();
        var neutralResourcePath = RepositoryFileLocator.Find("src", "FreeX.App.Localization", "Resources", "Strings.resx");
        var resourcesDirectory = Path.GetDirectoryName(neutralResourcePath)!;
        var satelliteCultures = Directory
            .EnumerateFiles(resourcesDirectory, "Strings.*.resx")
            .Select(Path.GetFileNameWithoutExtension)
            .Select(fileName => fileName!["Strings.".Length..])
            .Order(StringComparer.Ordinal)
            .ToList();
        var localizations = satelliteCultures
            .Prepend(neutralCulture)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var localization in localizations)
            CultureInfo.GetCultureInfo(localization);

        return localizations;
    }

    private static string HostNeutralResourcesLanguage()
    {
        var source = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Host", "AssemblyInfo.cs"));
        var match = Regex.Match(source, @"NeutralResourcesLanguage\(""(?<culture>[^""]+)""\)");
        match.Success.Should().BeTrue("the host should declare its neutral resource culture");
        return match.Groups["culture"].Value;
    }

    private static string ExtractWorkflowStepBlock(string workflow, string stepName)
    {
        var marker = $"      - name: {stepName}";
        var start = workflow.IndexOf(marker, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, $"workflow should contain the {stepName} step");
        var next = workflow.IndexOf("\n      - name:", start + marker.Length, StringComparison.Ordinal);
        if (next < 0)
            next = workflow.Length;

        return workflow[start..next];
    }

    private static string ExtractWorkflowJobBlock(string workflow, string jobName)
    {
        var marker = $"  {jobName}:";
        var start = workflow.IndexOf(marker, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, $"workflow should contain the {jobName} job");
        var nextMatch = Regex.Match(workflow[(start + marker.Length)..], @"(?m)^  [A-Za-z0-9_-]+:\s*$");
        var end = nextMatch.Success
            ? start + marker.Length + nextMatch.Index
            : workflow.Length;

        return workflow[start..end];
    }

    private static IReadOnlyList<string> WorkflowRuntimeRunnerPairs(string workflow) =>
        Regex.Matches(
                workflow,
                @"(?m)^\s*-\s*runtime:\s*(?<runtime>osx-[A-Za-z0-9]+)\s*\r?\n\s*runner:\s*(?<runner>[A-Za-z0-9._-]+)\s*$")
            .Select(match => $"{match.Groups["runtime"].Value}={match.Groups["runner"].Value}")
            .ToList();

    private static void AssertIcnsFile(string path)
    {
        var bytes = File.ReadAllBytes(path);
        bytes.Length.Should().BeGreaterThan(8);
        Encoding.ASCII.GetString(bytes, 0, 4).Should().Be("icns");
        ReadBigEndianUInt32(bytes, 4).Should().Be((uint)bytes.Length);

        var entryTypes = new List<string>();
        var offset = 8;
        while (offset < bytes.Length)
        {
            var entryType = Encoding.ASCII.GetString(bytes, offset, 4);
            var entryLength = ReadBigEndianUInt32(bytes, offset + 4);
            entryLength.Should().BeGreaterThanOrEqualTo(8);
            ((long)offset + entryLength).Should().BeLessThanOrEqualTo(bytes.Length);
            entryTypes.Add(entryType);
            offset = checked(offset + (int)entryLength);
        }

        entryTypes.Should().Contain("icp4");
        entryTypes.Should().Contain("icp5");
        entryTypes.Should().Contain("ic08");
    }

    private static uint ReadBigEndianUInt32(byte[] bytes, int offset) =>
        ((uint)bytes[offset] << 24) |
        ((uint)bytes[offset + 1] << 16) |
        ((uint)bytes[offset + 2] << 8) |
        bytes[offset + 3];
}
