using System.Xml.Linq;
using FluentAssertions;

namespace FreeX.App.Services.Tests;

public sealed class MacOsBundleMetadataTests
{
    [Fact]
    public void InfoPlist_DefinesPreviewBundleIdentityWithoutDocumentRegistration()
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

        PlistValue(plist, "CFBundleDocumentTypes")
            .Should()
            .BeNull("the preview app has in-app Open support but does not handle macOS open-document events yet");
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
        workflow.Should().Contain("lipo -archs");
        workflow.Should().Contain("codesign --verify --deep --strict");
        workflow.Should().Contain("host_arch=\"$(uname -m)\"");
        workflow.Should().Contain("unzip -q");
        workflow.Should().Contain("test -x \"$unzip_root/FreeX.app/Contents/MacOS/FreeX\"");
        workflow.Should().Contain("(cd \"$artifact_root\" && shasum -a 256 \"$zip_name\" > \"$zip_name.sha256\")");
        workflow.Should().Contain("codesign --verify --deep --strict \"$unzip_root/FreeX.app\"");
        workflow.Should().Contain("\"$unzip_root/FreeX.app/Contents/MacOS/FreeX\" --packaging-smoke \"$smoke_file\"");
        workflow.Should().Contain("grep -q \"Packaging smoke opened\" \"$smoke_log\"");
        workflow.Should().Contain("artifacts/freex-${{ matrix.runtime }}-macos-app.zip");
        workflow.Should().Contain("artifacts/freex-${{ matrix.runtime }}-macos-app.zip.sha256");
        workflow.Should().Contain("if-no-files-found: error");
    }

    [Fact]
    public void Program_RunsPackagingSmokeBeforeAvaloniaLifetime()
    {
        var program = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "Program.cs"));

        program.Should().Contain("PackagingSmokeCommand.TryRun(args, Console.Out, Console.Error, out var smokeExitCode)");
        program.Should().Contain("return smokeExitCode;");
        program.Should().Contain("StartWithClassicDesktopLifetime(args)");
    }

    private static string? PlistString(XDocument plist, string key) =>
        PlistValue(plist, key)?.Name.LocalName == "string"
            ? PlistValue(plist, key)!.Value
            : null;

    private static XElement? PlistValue(XDocument plist, string key)
    {
        var elements = plist.Root?.Element("dict")?.Elements().ToList() ?? [];
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
