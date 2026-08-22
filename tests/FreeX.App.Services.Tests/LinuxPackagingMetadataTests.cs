using System.Xml.Linq;
using FluentAssertions;

namespace FreeX.App.Services.Tests;

public sealed class LinuxPackagingMetadataTests
{
    private const string AppId = "io.github.tony-xmelon.freex";
    private const string NativeWorkbookMimeType = "application/vnd.freex.workbook+json";
    private const string CanonicalIconRelativePath = "shared/Free.Shared.Shell/Resources/FreeX.svg";

    private static string PackagingFile(string name) =>
        RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "Packaging", "linux", name);

    private static string PackagingDirectory() =>
        Path.GetDirectoryName(PackagingFile("README.md"))!;

    private static string SharedPackagingFile() =>
        RepositoryFileLocator.Find("tools", "packaging", "linux", "package-linux.sh");

    private static string CanonicalIconFile() =>
        RepositoryFileLocator.Find("shared", "Free.Shared.Shell", "Resources", "FreeX.svg");

    private static Dictionary<string, string> ParseDesktopEntry(string path)
    {
        var entries = new Dictionary<string, string>(StringComparer.Ordinal);
        var inEntrySection = false;
        foreach (var rawLine in File.ReadAllLines(path))
        {
            var line = rawLine.Trim();
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                inEntrySection = line == "[Desktop Entry]";
                continue;
            }

            if (!inEntrySection || line.Length == 0 || line.StartsWith('#'))
                continue;

            var separator = line.IndexOf('=');
            if (separator < 1)
                continue;

            entries[line[..separator]] = line[(separator + 1)..];
        }

        return entries;
    }

    [Fact]
    public void DesktopEntry_DeclaresLauncherIdentityAndMimeAssociations()
    {
        var entries = ParseDesktopEntry(PackagingFile($"{AppId}.desktop"));

        entries["Type"].Should().Be("Application");
        entries["Name"].Should().Be("FreeX");
        entries["Exec"].Should().Be("freex %F");
        entries["TryExec"].Should().Be("freex");
        entries["Icon"].Should().Be(AppId);
        entries["Terminal"].Should().Be("false");
        entries["StartupWMClass"].Should().Be("FreeX");
        entries["Categories"].Should().Contain("Office").And.Contain("Spreadsheet");

        var mimeTypes = entries["MimeType"].Split(';', StringSplitOptions.RemoveEmptyEntries);
        mimeTypes.Should().Contain(NativeWorkbookMimeType);
        mimeTypes.Should().Contain("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        mimeTypes.Should().Contain("application/vnd.ms-excel");
        mimeTypes.Should().Contain("application/vnd.ms-excel.sheet.macroEnabled.12");
        mimeTypes.Should().Contain("application/vnd.openxmlformats-officedocument.spreadsheetml.template");
        mimeTypes.Should().Contain("application/vnd.ms-excel.template.macroEnabled.12");
        mimeTypes.Should().Contain("application/vnd.ms-excel.sheet.binary.macroEnabled.12");
        mimeTypes.Should().Contain("text/csv");
        mimeTypes.Should().Contain("text/tab-separated-values");
    }

    [Fact]
    public void MimeDefinition_DeclaresNativeWorkbookType()
    {
        var doc = XDocument.Load(PackagingFile($"{AppId}.xml"));
        XNamespace ns = "http://www.freedesktop.org/standards/shared-mime-info";

        var mimeType = doc.Root!.Elements(ns + "mime-type").Single();
        mimeType.Attribute("type")!.Value.Should().Be(NativeWorkbookMimeType);
        mimeType.Element(ns + "glob")!.Attribute("pattern")!.Value.Should().Be("*.fxl");
        mimeType.Element(ns + "sub-class-of")!.Attribute("type")!.Value.Should().Be("application/json");
        mimeType.Element(ns + "icon")!.Attribute("name")!.Value.Should().Be(AppId);
    }

    [Fact]
    public void Icon_IsCanonicalScalableSvg_AndPackagingEntrypointsLinkToIt()
    {
        var iconPath = CanonicalIconFile();
        File.Exists(iconPath).Should().BeTrue();

        var svg = XDocument.Load(iconPath).Root!;
        svg.Name.LocalName.Should().Be("svg");
        svg.Attribute("viewBox")!.Value.Should().Be("0 0 256 256");

        foreach (var script in new[] { "package-linux-app.sh", "build-appimage.sh", "build-deb.sh" })
        {
            File.ReadAllText(PackagingFile(script))
                .Should().Contain($"--icon-file \"$repo_root/{CanonicalIconRelativePath}\"");
        }

        File.Exists(Path.Combine(PackagingDirectory(), $"{AppId}.svg")).Should().BeFalse();
    }

    [Fact]
    public void MetainfoXml_DeclaresAppStreamComponentAndIsPackaged()
    {
        var doc = XDocument.Load(PackagingFile($"{AppId}.metainfo.xml"));
        var root = doc.Root!;

        root.Name.LocalName.Should().Be("component");
        root.Attribute("type")!.Value.Should().Be("desktop-application");
        root.Element("id")!.Value.Should().Be(AppId);
        root.Element("name")!.Value.Should().Be("FreeX");
        root.Element("project_license").Should().NotBeNull();
        var launchable = root.Element("launchable")!;
        launchable.Attribute("type")!.Value.Should().Be("desktop-id");
        launchable.Value.Should().Be($"{AppId}.desktop");
        root.Element("categories")!.Elements("category").Select(c => c.Value)
            .Should().Contain("Office").And.Contain("Spreadsheet");

        // All three packagers install the metainfo under share/metainfo.
        foreach (var script in new[] { "package-linux-app.sh", "build-appimage.sh", "build-deb.sh" })
            File.ReadAllText(PackagingFile(script)).Should().Contain("package-linux.sh");
        File.ReadAllText(SharedPackagingFile()).Should().Contain("share/metainfo");
    }

    [Fact]
    public void PackagingScripts_AssembleRelocatableLayout()
    {
        var packageScript = File.ReadAllText(SharedPackagingFile());
        packageScript.Should().Contain("#!/usr/bin/env bash");
        packageScript.Should().Contain("set -euo pipefail");
        packageScript.Should().Contain("library_dir");
        packageScript.Should().Contain("install.sh");
        packageScript.Should().Contain("uninstall.sh");
        packageScript.Should().Contain("update-desktop-database");
        packageScript.Should().Contain("update-mime-database");
        packageScript.Should().Contain("tar -C");

        var appImageScript = File.ReadAllText(SharedPackagingFile());
        appImageScript.Should().Contain("AppRun");
        appImageScript.Should().Contain("appimagetool");
        appImageScript.Should().Contain("x86_64");
        appImageScript.Should().Contain("aarch64");
    }

    [Fact]
    public void DebBuilder_DeclaresControlMaintainerScriptsAndArchMapping()
    {
        var deb = File.ReadAllText(SharedPackagingFile());

        deb.Should().Contain("dpkg-deb");
        deb.Should().Contain("DEBIAN/control");
        deb.Should().Contain("Package: %s");
        deb.Should().Contain("Architecture: %s");
        deb.Should().Contain("linux-x64) deb_arch=\"amd64\"");
        deb.Should().Contain("linux-arm64) deb_arch=\"arm64\"");
        // Maintainer scripts refresh the desktop/MIME/icon caches on install and removal.
        deb.Should().Contain("DEBIAN/postinst");
        deb.Should().Contain("DEBIAN/postrm");
        deb.Should().Contain("update-desktop-database");
        deb.Should().Contain("update-mime-database");
        deb.Should().Contain("usr/share/applications");
        deb.Should().Contain("usr/share/mime/packages");

        // The release workflow builds and publishes the .deb (non-fatal evidence).
        var release = File.ReadAllText(RepositoryFileLocator.Find(".github", "workflows", "linux-release.yml"));
        release.Should().Contain("build-deb.sh");
        release.Should().Contain("deb_status=");
        release.Should().Contain("freex_${{ inputs.release_version }}_*.deb");
    }

    [Fact]
    public void LinuxWorkflow_BuildsPackagesAndSmokeTestsBothRuntimes()
    {
        var workflow = File.ReadAllText(RepositoryFileLocator.Find(".github", "workflows", "linux-app.yml"));

        workflow.Should().Contain("name: Linux App Preview");
        workflow.Should().Contain("runtime: linux-x64");
        workflow.Should().Contain("runtime: linux-arm64");
        workflow.Should().Contain("runner: ubuntu-latest");
        workflow.Should().Contain("runner: ubuntu-24.04-arm");

        workflow.Should().Contain("dotnet publish src/FreeX.App.Avalonia/FreeX.App.Avalonia.csproj");
        workflow.Should().Contain("--self-contained true");
        workflow.Should().Contain("-p:UseAppHost=true");
        workflow.Should().Contain("--runtime \"$runtime\"");

        // Headless hard gate plus GUI launch smoke under a virtual display.
        workflow.Should().Contain("--packaging-smoke");
        workflow.Should().Contain("\"$validation_published/FreeX.Validation.Avalonia\" --packaging-smoke");
        workflow.Should().Contain("xvfb-run -a");
        workflow.Should().Contain("--launch-smoke");
        workflow.Should().Contain("bash tools/Run-PackagedProductLaunchProbe.sh");
        workflow.Should().Contain("--executable \"$published/FreeX\"");
        workflow.Should().Contain("grep -Fqx \"packaged_product_launch_status=passed\" \"$packaged_product_launch_report\"");
        workflow.Should().Contain("grep -Fqx \"packaged_product_executable=$published/FreeX\" \"$packaged_product_launch_report\"");
        workflow.Should().Contain("desktop-file-validate");
        workflow.Should().Contain("package-linux-app.sh");
        workflow.Should().Contain("sha256sum -c");
        workflow.Should().Contain("packaging_smoke_status=passed");
        workflow.Should().Contain("launch_smoke_status=passed");
        workflow.IndexOf("bash tools/Run-PackagedProductLaunchProbe.sh", StringComparison.Ordinal)
            .Should().BeLessThan(workflow.IndexOf("launch_smoke_status=passed", StringComparison.Ordinal));

        // Aggregate readiness lane.
        workflow.Should().Contain("linux-preview-readiness");
        workflow.Should().Contain("Test-LinuxPublicPreviewReadiness.ps1");
    }

    [Fact]
    public void LinuxWorkflow_DoesNotLeakMacOsSigningMachinery()
    {
        var workflow = File.ReadAllText(RepositoryFileLocator.Find(".github", "workflows", "linux-app.yml"));

        foreach (var forbidden in new[] { "codesign", "notarytool", "MACOS_CODESIGN", "lsregister", "spctl", "Developer ID" })
            workflow.Should().NotContain(forbidden);
    }

    [Fact]
    public void LinuxUiLane_CapturesScreenshotAndProvidesReusableDockerTool()
    {
        // The UI lane captures a screenshot of the live window as visual evidence and uploads it.
        var workflow = File.ReadAllText(RepositoryFileLocator.Find(".github", "workflows", "linux-app.yml"));
        workflow.Should().Contain("imagemagick");
        workflow.Should().Contain("import -window root");
        workflow.Should().Contain("ui_screenshot_status");
        workflow.Should().Contain("linux-screenshot.png");

        // A reusable local tool reproduces the containerized UI smoke + screenshot for Windows devs.
        var tool = File.ReadAllText(RepositoryFileLocator.Find("tools", "Run-LinuxAppInDocker.ps1"));
        tool.Should().Contain("docker run");
        tool.Should().Contain("--packaging-smoke");
        tool.Should().Contain("--launch-smoke");
        tool.Should().Contain("import -window root");
        tool.Should().Contain("freex-linux-screenshot.png");
    }
}
