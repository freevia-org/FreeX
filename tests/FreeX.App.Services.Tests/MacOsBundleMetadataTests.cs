using System.Xml.Linq;
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
        PlistString(plist, "CFBundleIconFile").Should().BeNull("the preview artifact does not ship a macOS .icns yet");

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
        workflow.Should().Contain("plutil -lint");
        workflow.Should().Contain("PlistBuddy -c 'Print :CFBundleExecutable'");
        workflow.Should().Contain("PlistBuddy -c 'Print :CFBundleDocumentTypes:0:CFBundleTypeExtensions:0'");
        workflow.Should().Contain("PlistBuddy -c 'Print :CFBundleDocumentTypes:1:CFBundleTypeExtensions:0'");
        workflow.Should().Contain("lipo -archs");
        workflow.Should().Contain("evidence_path=\"$artifact_root/freex-$runtime-macos-evidence.txt\"");
        workflow.Should().Contain("smoke_log=\"$artifact_root/freex-$runtime-macos-packaging-smoke.log\"");
        workflow.Should().Contain("launch_smoke_report=\"$artifact_root/freex-$runtime-macos-launch-smoke.txt\"");
        workflow.Should().Contain("echo \"binary_archs=$binary_archs\"");
        workflow.Should().Contain("echo \"codesign_verified=true\"");
        workflow.Should().Contain("echo \"smoke_status=passed\" >> \"$evidence_path\"");
        workflow.Should().Contain("echo \"smoke_status=skipped_host_arch_mismatch\" >> \"$evidence_path\"");
        workflow.Should().Contain("codesign --verify --deep --strict");
        workflow.Should().Contain("host_arch=\"$(uname -m)\"");
        workflow.Should().Contain("unzip -q");
        workflow.Should().Contain("test -x \"$unzip_root/FreeX.app/Contents/MacOS/FreeX\"");
        workflow.Should().Contain("(cd \"$artifact_root\" && shasum -a 256 \"$zip_name\" > \"$zip_name.sha256\")");
        workflow.Should().Contain("codesign --verify --deep --strict \"$unzip_root/FreeX.app\"");
        workflow.Should().Contain("\"$unzip_root/FreeX.app/Contents/MacOS/FreeX\" --packaging-smoke \"$smoke_file\"");
        workflow.Should().Contain("grep -q \"Packaging smoke opened\" \"$smoke_log\"");
        workflow.Should().Contain("grep -q \"edited, saved, and reopened\" \"$smoke_log\"");
        workflow.Should().Contain("lsregister -f \"$unzip_root/FreeX.app\"");
        workflow.Should().Contain("open -W -n -b io.github.tony-xmelon.freex \"$launch_smoke_file\" --args --macos-launch-smoke \"$launch_smoke_report\"");
        workflow.Should().Contain("macos_launch_smoke=missing_report");
        workflow.Should().Contain("grep -q \"macos_launch_smoke=passed\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"window_shown=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"opened_source_path=.*freex-$runtime-launch.csv\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_file_menu=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_open_menu_item=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_save_menu_item=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_save_as_menu_item=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("grep -q \"native_quit_menu_item=true\" \"$launch_smoke_report\"");
        workflow.Should().Contain("artifacts/freex-${{ matrix.runtime }}-macos-app.zip");
        workflow.Should().Contain("artifacts/freex-${{ matrix.runtime }}-macos-app.zip.sha256");
        workflow.Should().Contain("artifacts/freex-${{ matrix.runtime }}-macos-evidence.txt");
        workflow.Should().Contain("artifacts/freex-${{ matrix.runtime }}-macos-packaging-smoke.log");
        workflow.Should().Contain("artifacts/freex-${{ matrix.runtime }}-macos-launch-smoke.txt");
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
}
