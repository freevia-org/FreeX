using System.Xml.Linq;
using System.Text;
using FluentAssertions;

namespace FreeX.App.Services.Tests;

public sealed class MacOsBundleMetadataTests
{
    [Fact]
    public void InfoPlist_DefinesPreviewBundleIdentityAndDocumentRegistration()
    {
        var plistPath = RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "Packaging", "macos", "Info.plist");
        var plist = XDocument.Load(plistPath);
        var project = XDocument.Load(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "FreeX.App.Avalonia.csproj"));

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

        var importedWorkbooks = documentTypes[1];
        PlistString(importedWorkbooks, "CFBundleTypeName").Should().Be("Spreadsheet Workbooks");
        PlistString(importedWorkbooks, "CFBundleTypeRole").Should().Be("Viewer");
        PlistString(importedWorkbooks, "LSHandlerRank").Should().Be("Alternate");
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

        workflow.Should().Contain("runs-on: macos-latest");
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
        workflow.Should().Contain("evidence_path=\"$artifact_root/freex-$runtime-macos-evidence.txt\"");
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
        workflow.Should().Contain("echo \"binary_archs=$binary_archs\"");
        workflow.Should().Contain("echo \"bundle_icon=$(/usr/libexec/PlistBuddy -c 'Print :CFBundleIconFile' \"$app/Contents/Info.plist\")\"");
        workflow.Should().Contain("echo \"codesign_verified=true\"");
        workflow.Should().Contain("echo \"codesign_mode=$signing_mode\"");
        workflow.Should().Contain("echo \"notarization_status=$notarization_status\"");
        workflow.Should().Contain("echo \"stapler_validated=$stapler_validated\"");
        workflow.Should().Contain("echo \"zip_sha256=$zip_sha256\"");
        workflow.Should().Contain("echo \"smoke_status=passed\" >> \"$evidence_path\"");
        workflow.Should().Contain("echo \"smoke_status=skipped_host_arch_mismatch\" >> \"$evidence_path\"");
        workflow.Should().Contain("codesign --verify --deep --strict");
        workflow.Should().Contain("host_arch=\"$(uname -m)\"");
        workflow.Should().Contain("unzip -q");
        workflow.Should().Contain("test -x \"$unzip_root/FreeX.app/Contents/MacOS/FreeX\"");
        workflow.Should().Contain("(cd \"$artifact_root\" && shasum -a 256 \"$zip_name\" > \"$zip_name.sha256\")");
        workflow.Should().Contain("(cd \"$artifact_root\" && shasum -a 256 -c \"$zip_name.sha256\")");
        workflow.Should().Contain("zip_sha256=\"$(cut -d ' ' -f 1 \"$artifact_root/$zip_name.sha256\")\"");
        workflow.Should().Contain("cat > \"$tester_instructions_path\" <<EOF");
        workflow.Should().Contain("This artifact is a preview build for macOS port validation. It is not a public release channel.");
        workflow.Should().Contain("Use osx-arm64 for Apple Silicon Macs and osx-x64 for Intel Macs.");
        workflow.Should().Contain("Unzip the GitHub Actions artifact wrapper first; these files are inside it.");
        workflow.Should().Contain("Ad-hoc signed or non-notarized previews may require Control-click or right-click > Open for trusted internal testing.");
        workflow.Should().Contain("codesign --verify --deep --strict \"$unzip_root/FreeX.app\"");
        workflow.Should().Contain("\"$unzip_root/FreeX.app/Contents/MacOS/FreeX\" --packaging-smoke | tee \"$smoke_log\"");
        workflow.Should().Contain("grep -q \"macOS Preview Workbook\" \"$smoke_log\"");
        workflow.Should().Contain("grep -q \"drawing_object_previews=3\" \"$smoke_log\"");
        workflow.Should().Contain("grep -q \"roundtrip_drawing_object_previews=3\" \"$smoke_log\"");
        workflow.Should().Contain("\"$unzip_root/FreeX.app/Contents/MacOS/FreeX\" --packaging-smoke \"$smoke_file\" | tee -a \"$smoke_log\"");
        workflow.Should().Contain("grep -q \"Packaging smoke opened\" \"$smoke_log\"");
        workflow.Should().Contain("grep -q \"edited, saved, and reopened\" \"$smoke_log\"");
        workflow.Should().Contain("lsregister -f \"$unzip_root/FreeX.app\"");
        workflow.Should().Contain("open -W -n -b io.github.tony-xmelon.freex \"$launch_smoke_file\" --args --macos-launch-smoke \"$launch_smoke_report\"");
        workflow.Should().Contain("macos_launch_smoke=missing_report");
        workflow.Should().Contain("grep -q \"macos_launch_smoke=passed\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"window_shown=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"opened_source_path=.*freex-$runtime-launch.csv\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"new_sheet_button=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_file_menu=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_new_workbook_menu_item=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_open_menu_item=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_open_recent_menu_item=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_open_recent_item_count=[1-9]\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_save_menu_item=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_save_as_menu_item=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_close_workbook_menu_item=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_select_all_menu_item=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_edit_menu=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_format_menu=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_sheet_menu=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_help_menu=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_new_sheet_menu_item=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_rename_sheet_menu_item=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_duplicate_sheet_menu_item=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_move_sheet_left_menu_item=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_move_sheet_right_menu_item=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_delete_sheet_menu_item=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_undo_menu_item=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_redo_menu_item=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_cut_menu_item=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_copy_menu_item=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_paste_menu_item=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_paste_special_menu_item=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_clear_contents_menu_item=true\" \"$launch_smoke_report\"");
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
        workflow.Should().Contain("grep -q \"native_wrap_text_menu_item=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_decrease_indent_menu_item=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_increase_indent_menu_item=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_align_left_menu_item=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_align_center_menu_item=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_align_right_menu_item=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_help_online_menu_item=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_send_feedback_menu_item=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_check_for_updates_menu_item=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_about_menu_item=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_legal_notices_menu_item=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_quit_menu_item=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("artifacts/freex-${{ matrix.runtime }}-macos-app.zip");
        workflow.Should().Contain("artifacts/freex-${{ matrix.runtime }}-macos-app.zip.sha256");
        workflow.Should().Contain("artifacts/freex-${{ matrix.runtime }}-macos-evidence.txt");
        workflow.Should().Contain("artifacts/freex-${{ matrix.runtime }}-macos-packaging-smoke.log");
        workflow.Should().Contain("artifacts/freex-${{ matrix.runtime }}-macos-launch-smoke.txt");
        workflow.Should().Contain("artifacts/freex-${{ matrix.runtime }}-macos-notarization.log");
        workflow.Should().Contain("artifacts/freex-${{ matrix.runtime }}-macos-tester-instructions.md");
        workflow.Should().Contain("if-no-files-found: error");
    }

    [Fact]
    public void Program_RunsPackagingSmokeBeforeAvaloniaLifetime()
    {
        var program = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "Program.cs"));

        program.Should().Contain("PackagingSmokeCommand.TryRun(args, Console.Out, Console.Error, out var smokeExitCode)");
        program.Should().Contain("return smokeExitCode;");
        program.Should().Contain("MacOsLaunchSmokeOptions.TryParse(");
        program.Should().Contain("App.LaunchSmokeOptions = launchSmokeOptions;");
        program.Should().Contain("StartWithClassicDesktopLifetime(startupArguments)");
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
        PlistValue(dict, key)?
            .Elements("string")
            .Select(element => element.Value)
            .ToList() ?? [];

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
