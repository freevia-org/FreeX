using System.Text.RegularExpressions;
using System.Xml.Linq;
using FluentAssertions;

namespace FreeX.App.Services.Tests;

public sealed class AvaloniaProjectPortabilityGuardTests
{
    private const RegexOptions DefaultRegexOptions = RegexOptions.Compiled | RegexOptions.CultureInvariant;

    private static readonly HashSet<string> SourceExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs",
        ".csproj",
        ".props",
        ".targets"
    };

    private static readonly string[] AllowedProjectReferences =
    [
        "FreeX.App.Presentation",
        "FreeX.App.Services",
        "FreeX.Core.Calc",
        "FreeX.Core.Commands",
        "FreeX.Core.IO",
        "FreeX.Core.Model",
        "Free.Shared.Ribbon",
        "FreeX.Ribbon.Avalonia"
    ];

    private static readonly (string Description, Regex Pattern)[] PortableForbiddenPatterns =
    [
        ("System.Windows namespace", new(@"(?<![\w.])System\.Windows(?:\.[A-Za-z_]\w*)?(?![\w.])", DefaultRegexOptions)),
        ("Microsoft.Win32 namespace", new(@"(?<![\w.])Microsoft\.Win32(?:\.[A-Za-z_]\w*)?(?![\w.])", DefaultRegexOptions)),
        ("WinRT Windows namespace", new(@"(?<![\w.])Windows\.[A-Za-z_]\w*(?:\.[A-Za-z_]\w*)*(?![\w.])", DefaultRegexOptions)),
        ("FreeX.App.Host dependency", new(@"(?<![\w.])FreeX\.App\.Host(?![\w.])", DefaultRegexOptions)),
        ("FreeX.App.UI dependency", new(@"(?<![\w.])FreeX\.App\.UI(?![\w.])", DefaultRegexOptions)),
        ("UseWPF project marker", new(@"(?<![\w])UseWPF(?![\w])", DefaultRegexOptions | RegexOptions.IgnoreCase)),
        ("WindowsDesktop SDK", new(@"(?<![\w.])Microsoft\.NET\.Sdk\.WindowsDesktop(?![\w.])", DefaultRegexOptions)),
        ("Windows-targeted framework", new(@"(?<![\w.-])net\d+(?:\.\d+)?-windows(?:\d+(?:\.\d+)*)?(?![\w.-])", DefaultRegexOptions | RegexOptions.IgnoreCase)),
        ("Windows Forms project marker", new(@"(?<![\w])UseWindowsForms(?![\w])", DefaultRegexOptions | RegexOptions.IgnoreCase)),
        ("WindowsDesktop framework reference", new(@"(?<![\w.])Microsoft\.WindowsDesktop\.App(?:\.(?:WPF|WindowsForms))?(?![\w.])", DefaultRegexOptions)),
        ("WPF assembly reference", new(@"(?<![\w.])(?:PresentationCore|PresentationFramework|System\.Xaml|WindowsBase|WindowsFormsIntegration)(?![\w.])", DefaultRegexOptions)),
        ("WinForms dependency marker", new(@"(?<![\w.])(?:System\.Windows\.Forms|WinForms|WindowsForms)(?![\w.])", DefaultRegexOptions | RegexOptions.IgnoreCase))
    ];

    private static readonly (string Description, Regex Pattern)[] NativeMacOsForbiddenPatterns =
    [
        ("AppKit namespace", new(@"(?<![\w.])AppKit(?:\.[A-Za-z_]\w*)?(?![\w.])", DefaultRegexOptions)),
        ("Foundation namespace", new(@"(?<![\w.])Foundation(?:\.[A-Za-z_]\w*)?(?![\w.])", DefaultRegexOptions)),
        ("ObjCRuntime namespace", new(@"(?<![\w.])ObjCRuntime(?:\.[A-Za-z_]\w*)?(?![\w.])", DefaultRegexOptions)),
        ("NSSharingService type", new(@"(?<![\w.])NSSharingService(?![\w.])", DefaultRegexOptions)),
        ("NSSharingServicePicker type", new(@"(?<![\w.])NSSharingServicePicker(?![\w.])", DefaultRegexOptions))
    ];

    [Fact]
    public void AvaloniaProjectReferences_StayInsidePortableAppBoundary()
    {
        var projectPath = RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "FreeX.App.Avalonia.csproj");
        var project = XDocument.Load(projectPath);

        var projectReferences = ProjectItemIncludes(project, "ProjectReference")
            .Select(ProjectReferenceName)
            .ToArray();

        projectReferences.Should().Equal(
            AllowedProjectReferences,
            "the Avalonia app path must stay explicitly bounded to app services and core projects, not the Windows/WPF host projects");

        var dependencyViolations = ProjectDependencyMarkers(project)
            .SelectMany(marker => PortableForbiddenPatterns
                .Where(forbidden => forbidden.Pattern.IsMatch(marker))
                .Select(forbidden => $"{forbidden.Description}: {marker}"))
            .Order(StringComparer.Ordinal)
            .ToArray();

        dependencyViolations.Should().BeEmpty(
            "the Avalonia project must not acquire WPF, WinForms, System.Windows, Microsoft.Win32, FreeX.App.Host, or FreeX.App.UI dependencies or project references");
    }

    [Fact]
    public void AvaloniaProjectSources_KeepPortableAndNativeMacOsBoundariesExplicit()
    {
        var projectPath = RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "FreeX.App.Avalonia.csproj");
        var avaloniaRoot = Path.GetDirectoryName(projectPath)!;
        var repositoryRoot = Path.GetFullPath(Path.Combine(avaloniaRoot, "..", ".."));

        var violations = Directory.EnumerateFiles(avaloniaRoot, "*", SearchOption.AllDirectories)
            .Where(IsPortableSourceFile)
            .SelectMany(path => FindViolations(path, repositoryRoot))
            .OrderBy(violation => violation.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(violation => violation.LineNumber)
            .ThenBy(violation => violation.Description, StringComparer.Ordinal)
            .ToArray();

        violations.Should().BeEmpty(
            "FreeX.App.Avalonia source must keep Windows/WPF dependencies out of every path and direct AppKit/Foundation/ObjCRuntime/NSSharingService usage confined to the macOS-only compile folder");
    }

    [Fact]
    public void MacOsTargetFramework_StaysOptInUntilNativeHostBoundaryIsReady()
    {
        var projectPath = RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "FreeX.App.Avalonia.csproj");
        var project = XDocument.Load(projectPath);

        var targetFramework = ProjectPropertyElements(project, "TargetFramework")
            .Should()
            .ContainSingle("default builds and the current hosted bundle lane must stay on plain net10.0")
            .Subject;
        targetFramework.Value.Trim().Should().Be("net10.0");
        targetFramework.Attribute("Condition")?.Value.Should().Be("'$(EnableMacOsTargetFramework)' != 'true'");

        var macOsTargetFrameworks = ProjectPropertyElements(project, "TargetFrameworks")
            .Should()
            .ContainSingle("the macOS TFM must be reachable only through an explicit opt-in property")
            .Subject;
        macOsTargetFrameworks.Attribute("Condition")?.Value.Should().Be("'$(EnableMacOsTargetFramework)' == 'true'");
        macOsTargetFrameworks.Value
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Should()
            .Equal("net10.0", "net10.0-macos");

        var supportedOsVersion = ProjectPropertyElements(project, "SupportedOSPlatformVersion")
            .Should()
            .ContainSingle("the macOS TFM should match the bundle minimum system version")
            .Subject;
        supportedOsVersion.Attribute("Condition")?.Value.Should().Be("'$(TargetFramework)' == 'net10.0-macos'");
        supportedOsVersion.Value.Trim().Should().Be("12.0");

        var macOsCompileRemove = ProjectItemElements(project, "Compile")
            .Where(element => element.Attribute("Remove")?.Value == @"MacOs\**\*.cs")
            .Should()
            .ContainSingle("native macOS source must be excluded from every non-macOS target framework")
            .Subject;
        ProjectCondition(macOsCompileRemove).Should().Be("'$(TargetFramework)' != 'net10.0-macos'");

        var macOsDefineConstants = ProjectPropertyElements(project, "DefineConstants")
            .Should()
            .ContainSingle("the native share-sheet implementation must be opt-in behind the macOS TFM")
            .Subject;
        ProjectCondition(macOsDefineConstants).Should().Be("'$(TargetFramework)' == 'net10.0-macos'");
        macOsDefineConstants.Value
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Should()
            .Contain("FREEX_MACOS_SHARE_SHEET");
    }

    [Fact]
    public void ForbiddenPatterns_DoNotMatchPlainPlatformProse()
    {
        const string prose = "This note says Windows-only and macOS-friendly behavior without declaring a desktop dependency or native Cocoa binding.";

        var matches = PortableForbiddenPatterns
            .Concat(NativeMacOsForbiddenPatterns)
            .Where(pattern => pattern.Pattern.IsMatch(prose))
            .Select(pattern => pattern.Description)
            .ToArray();

        matches.Should().BeEmpty("plain prose is not a dependency marker");
    }

    private static bool IsPortableSourceFile(string path)
    {
        if (!SourceExtensions.Contains(Path.GetExtension(path)))
            return false;

        var segments = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return !segments.Contains("bin", StringComparer.OrdinalIgnoreCase)
            && !segments.Contains("obj", StringComparer.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> ProjectDependencyMarkers(XDocument project)
    {
        if (project.Root?.Attribute("Sdk")?.Value is { Length: > 0 } sdk)
            yield return $"Project Sdk={sdk}";

        foreach (var itemName in new[] { "ProjectReference", "PackageReference", "FrameworkReference", "Reference" })
        {
            foreach (var include in ProjectItemIncludes(project, itemName))
                yield return $"{itemName} Include={include}";
        }

        foreach (var propertyName in new[] { "TargetFramework", "TargetFrameworks", "UseWPF", "UseWindowsForms" })
        {
            foreach (var value in ProjectPropertyValues(project, propertyName))
                yield return $"{propertyName}={value}";
        }
    }

    private static IEnumerable<string> ProjectItemIncludes(XDocument project, string itemName) =>
        project
            .Descendants()
            .Where(element => element.Name.LocalName == itemName)
            .Select(element => element.Attribute("Include")?.Value)
            .Where(include => !string.IsNullOrWhiteSpace(include))
            .Select(include => include!);

    private static IEnumerable<string> ProjectPropertyValues(XDocument project, string propertyName) =>
        ProjectPropertyElements(project, propertyName)
            .Select(element => element.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value));

    private static IEnumerable<XElement> ProjectPropertyElements(XDocument project, string propertyName) =>
        project
            .Descendants()
            .Where(element => element.Name.LocalName == propertyName);

    private static IEnumerable<XElement> ProjectItemElements(XDocument project, string itemName) =>
        project
            .Descendants()
            .Where(element => element.Name.LocalName == itemName);

    private static string? ProjectCondition(XElement element) =>
        element.Attribute("Condition")?.Value ?? element.Parent?.Attribute("Condition")?.Value;

    private static string ProjectReferenceName(string include)
    {
        var fileName = include.Split('\\', '/').Last();
        return Path.GetFileNameWithoutExtension(fileName);
    }

    private static IEnumerable<SourceViolation> FindViolations(string path, string repositoryRoot)
    {
        var relativePath = Path.GetRelativePath(repositoryRoot, path);
        var allowNativeMacOsTokens = IsMacOsConditionalSourcePath(relativePath);
        var lineNumber = 0;
        foreach (var line in File.ReadLines(path))
        {
            lineNumber++;

            foreach (var (description, pattern) in PortableForbiddenPatterns)
            {
                if (pattern.IsMatch(line))
                    yield return new SourceViolation(
                        relativePath,
                        lineNumber,
                        description,
                        line.Trim());
            }

            if (allowNativeMacOsTokens)
                continue;

            foreach (var (description, pattern) in NativeMacOsForbiddenPatterns)
            {
                if (pattern.IsMatch(line))
                    yield return new SourceViolation(
                        relativePath,
                        lineNumber,
                        description,
                        line.Trim());
            }
        }
    }

    private static bool IsMacOsConditionalSourcePath(string relativePath)
    {
        var normalizedPath = relativePath.Replace('\\', '/');
        return normalizedPath.StartsWith("src/FreeX.App.Avalonia/MacOs/", StringComparison.OrdinalIgnoreCase);
    }

    private readonly record struct SourceViolation(
        string RelativePath,
        int LineNumber,
        string Description,
        string SourceLine)
    {
        public override string ToString() => $"{RelativePath}:{LineNumber}: {Description}: {SourceLine}";
    }
}
