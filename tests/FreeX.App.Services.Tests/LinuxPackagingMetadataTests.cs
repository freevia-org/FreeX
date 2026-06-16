using System.Xml.Linq;
using FluentAssertions;

namespace FreeX.App.Services.Tests;

public sealed class LinuxPackagingMetadataTests
{
    private const string AppId = "io.github.tony-xmelon.freex";
    private const string NativeWorkbookMimeType = "application/vnd.freex.workbook+json";

    private static string PackagingFile(string name) =>
        RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "Packaging", "linux", name);

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
    public void Icon_IsScalableSvg()
    {
        var iconPath = PackagingFile($"{AppId}.svg");
        File.Exists(iconPath).Should().BeTrue();
        File.ReadAllText(iconPath).Should().Contain("<svg");
    }

    [Fact]
    public void PackagingScripts_AssembleRelocatableLayout()
    {
        var packageScript = File.ReadAllText(PackagingFile("package-linux-app.sh"));
        packageScript.Should().Contain("#!/usr/bin/env bash");
        packageScript.Should().Contain("set -euo pipefail");
        packageScript.Should().Contain("lib/freex");
        packageScript.Should().Contain("install.sh");
        packageScript.Should().Contain("uninstall.sh");
        packageScript.Should().Contain("update-desktop-database");
        packageScript.Should().Contain("update-mime-database");
        packageScript.Should().Contain("tar -C");

        var appImageScript = File.ReadAllText(PackagingFile("build-appimage.sh"));
        appImageScript.Should().Contain("AppRun");
        appImageScript.Should().Contain("appimagetool");
        appImageScript.Should().Contain("x86_64");
        appImageScript.Should().Contain("aarch64");
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
        workflow.Should().Contain("xvfb-run -a");
        workflow.Should().Contain("--launch-smoke");
        workflow.Should().Contain("desktop-file-validate");
        workflow.Should().Contain("package-linux-app.sh");
        workflow.Should().Contain("sha256sum -c");
        workflow.Should().Contain("packaging_smoke_status=passed");
        workflow.Should().Contain("launch_smoke_status=passed");

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
}
