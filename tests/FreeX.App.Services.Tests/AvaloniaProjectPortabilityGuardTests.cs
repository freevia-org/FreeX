using System.Text.RegularExpressions;
using System.Xml.Linq;
using FluentAssertions;

namespace FreeX.App.Services.Tests;

public sealed class AvaloniaProjectPortabilityGuardTests
{
    private const RegexOptions DefaultRegexOptions = RegexOptions.Compiled | RegexOptions.CultureInvariant;

    private static readonly string[] AllowedProjectReferences =
    [
        "FreeX.App.Localization",
        "FreeX.App.Presentation",
        "FreeX.App.Services",
        "FreeX.Core.Calc",
        "FreeX.Core.Commands",
        "FreeX.Core.IO",
        "FreeX.Core.Model",
        "Free.Shared.Drawing",
        "Free.Shared.Pdf",
        "Free.Shared.Pdf.Skia",
        "Free.Shared.Ribbon",
        "Free.Shared.Shell",
        "FreeX.Ribbon.Definitions",
        "Free.Shared.Ribbon.Avalonia",
        "Free.Shared.Shell.Avalonia",
        "Free.Shared.Theme.Avalonia"
    ];

    /// <summary>
    /// R128: scan CODE, not prose. A comment cannot create a dependency -- a <c>using</c> or a type
    /// reference can never live inside one -- so matching forbidden namespaces in comment text
    /// produces false positives without adding any protection. This fired when an Avalonia doc
    /// comment legitimately cross-referenced the WPF host's equivalent method by file path
    /// ("Mirrors the WPF host's ConfirmLossyFormatFeatureLossSave (src/FreeX.App.Host/...)"), which
    /// is exactly the kind of comment that makes the two shells easier to keep in parity.
    /// Rewording such comments to appease the scanner would be the wrong trade: it would make the
    /// codebase worse to preserve a check that was never protecting anything on those lines.
    /// Real code is still scanned at full strength.
    /// </summary>
    private static bool IsNotCommentLine(string line)
    {
        var trimmed = line.TrimStart();
        return !trimmed.StartsWith("//", StringComparison.Ordinal)
            && !trimmed.StartsWith("*", StringComparison.Ordinal)
            && !trimmed.StartsWith("/*", StringComparison.Ordinal);
    }

    private static readonly PortableBoundaryPattern[] PortableForbiddenPatterns =
    [
        new("System.Windows namespace", new(@"(?<![\w.])System\.Windows(?:\.[A-Za-z_]\w*)?(?![\w.])", DefaultRegexOptions)),
        new("Microsoft.Win32 namespace", new(@"(?<![\w.])Microsoft\.Win32(?:\.[A-Za-z_]\w*)?(?![\w.])", DefaultRegexOptions)),
        new("WinRT Windows namespace", new(@"(?<![\w.])Windows\.[A-Za-z_]\w*(?:\.[A-Za-z_]\w*)*(?![\w.])", DefaultRegexOptions)),
        new("FreeX.App.Host dependency", new(@"(?<![\w.])FreeX\.App\.Host(?![\w.])", DefaultRegexOptions)),
        new("FreeX.App.UI dependency", new(@"(?<![\w.])FreeX\.App\.UI(?![\w.])", DefaultRegexOptions)),
        new("UseWPF project marker", new(@"(?<![\w])UseWPF(?![\w])", DefaultRegexOptions | RegexOptions.IgnoreCase)),
        new("WindowsDesktop SDK", new(@"(?<![\w.])Microsoft\.NET\.Sdk\.WindowsDesktop(?![\w.])", DefaultRegexOptions)),
        new("Windows-targeted framework", new(@"(?<![\w.-])net\d+(?:\.\d+)?-windows(?:\d+(?:\.\d+)*)?(?![\w.-])", DefaultRegexOptions | RegexOptions.IgnoreCase)),
        new("Windows Forms project marker", new(@"(?<![\w])UseWindowsForms(?![\w])", DefaultRegexOptions | RegexOptions.IgnoreCase)),
        new("WindowsDesktop framework reference", new(@"(?<![\w.])Microsoft\.WindowsDesktop\.App(?:\.(?:WPF|WindowsForms))?(?![\w.])", DefaultRegexOptions)),
        new("WPF assembly reference", new(@"(?<![\w.])(?:PresentationCore|PresentationFramework|System\.Xaml|WindowsBase|WindowsFormsIntegration)(?![\w.])", DefaultRegexOptions)),
        new("WinForms dependency marker", new(@"(?<![\w.])(?:System\.Windows\.Forms|WinForms|WindowsForms)(?![\w.])", DefaultRegexOptions | RegexOptions.IgnoreCase))
    ];

    private static readonly PortableBoundaryPattern[] NativeMacOsForbiddenPatterns =
    [
        new("AppKit namespace", new(@"(?<![\w.])AppKit(?:\.[A-Za-z_]\w*)?(?![\w.])", DefaultRegexOptions)),
        new("Foundation namespace", new(@"(?<![\w.])Foundation(?:\.[A-Za-z_]\w*)?(?![\w.])", DefaultRegexOptions)),
        new("ObjCRuntime namespace", new(@"(?<![\w.])ObjCRuntime(?:\.[A-Za-z_]\w*)?(?![\w.])", DefaultRegexOptions)),
        new("NSSharingService type", new(@"(?<![\w.])NSSharingService(?![\w.])", DefaultRegexOptions)),
        new("NSSharingServicePicker type", new(@"(?<![\w.])NSSharingServicePicker(?![\w.])", DefaultRegexOptions))
    ];

    private static readonly HashSet<string> NativeMacOsForbiddenPatternDescriptions = NativeMacOsForbiddenPatterns
        .Select(pattern => pattern.Description)
        .ToHashSet(StringComparer.Ordinal);

    [Fact]
    public void AvaloniaProjectReferences_StayInsidePortableAppBoundary()
    {
        var projectPath = RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "FreeX.App.Avalonia.csproj");
        var project = XDocument.Load(projectPath);

        var projectReferences = PortableBoundaryGuard.ProjectItemIncludes(project, "ProjectReference")
            .Select(PortableBoundaryGuard.ProjectReferenceName)
            .ToArray();

        projectReferences.Should().Equal(
            AllowedProjectReferences,
            "the Avalonia app path must stay explicitly bounded to app services and core projects, not the Windows/WPF host projects");

        var dependencyViolations = PortableBoundaryGuard.ProjectDependencyMarkers(project)
            .SelectMany(marker => PortableForbiddenPatterns
                .Where(forbidden => forbidden.IsMatch(marker))
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

        var violations = PortableBoundaryGuard.FindSourceViolations(
                avaloniaRoot,
                repositoryRoot,
                PortableForbiddenPatterns.Concat(NativeMacOsForbiddenPatterns),
                isAllowed: IsAllowedNativeMacOsPattern,
                shouldScanLine: IsNotCommentLine)
            .Select(violation => violation.ToString())
            .ToArray();

        violations.Should().BeEmpty(
            "FreeX.App.Avalonia source must keep Windows/WPF dependencies out of every path and direct AppKit/Foundation/ObjCRuntime/NSSharingService usage confined to the macOS-only compile folder");
    }

    [Fact]
    public void MacOsTargetFramework_StaysOptInUntilNativeHostBoundaryIsReady()
    {
        var projectPath = RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "FreeX.App.Avalonia.csproj");
        var project = XDocument.Load(projectPath);

        var targetFramework = PortableBoundaryGuard.ProjectPropertyElements(project, "TargetFramework")
            .Should()
            .ContainSingle("default builds and the current hosted bundle lane must stay on plain net10.0")
            .Subject;
        targetFramework.Value.Trim().Should().Be("net10.0");
        targetFramework.Attribute("Condition")?.Value.Should().Be("'$(EnableMacOsTargetFramework)' != 'true'");

        var macOsTargetFrameworks = PortableBoundaryGuard.ProjectPropertyElements(project, "TargetFrameworks")
            .Should()
            .ContainSingle("the macOS TFM must be reachable only through an explicit opt-in property")
            .Subject;
        macOsTargetFrameworks.Attribute("Condition")?.Value.Should().Be("'$(EnableMacOsTargetFramework)' == 'true'");
        macOsTargetFrameworks.Value
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Should()
            .Equal("net10.0", "net10.0-macos");

        var supportedOsVersion = PortableBoundaryGuard.ProjectPropertyElements(project, "SupportedOSPlatformVersion")
            .Should()
            .ContainSingle("the macOS TFM should match the bundle minimum system version")
            .Subject;
        supportedOsVersion.Attribute("Condition")?.Value.Should().Be("'$(TargetFramework)' == 'net10.0-macos'");
        supportedOsVersion.Value.Trim().Should().Be("12.0");

        var macOsCompileRemove = PortableBoundaryGuard.ProjectItemElements(project, "Compile")
            .Where(element => element.Attribute("Remove")?.Value == @"MacOs\**\*.cs")
            .Should()
            .ContainSingle("native macOS source must be excluded from every non-macOS target framework")
            .Subject;
        PortableBoundaryGuard.ProjectCondition(macOsCompileRemove).Should().Be("'$(TargetFramework)' != 'net10.0-macos'");

        var macOsDefineConstants = PortableBoundaryGuard.ProjectPropertyElements(project, "DefineConstants")
            .Should()
            .ContainSingle("the native share-sheet implementation must be opt-in behind the macOS TFM")
            .Subject;
        PortableBoundaryGuard.ProjectCondition(macOsDefineConstants).Should().Be("'$(TargetFramework)' == 'net10.0-macos'");
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
            .Where(pattern => pattern.IsMatch(prose))
            .Select(pattern => pattern.Description)
            .ToArray();

        matches.Should().BeEmpty("plain prose is not a dependency marker");
    }

    private static bool IsAllowedNativeMacOsPattern(string relativePath, PortableBoundaryPattern pattern) =>
        NativeMacOsForbiddenPatternDescriptions.Contains(pattern.Description)
        && IsMacOsConditionalSourcePath(relativePath);

    private static bool IsMacOsConditionalSourcePath(string relativePath)
    {
        var normalizedPath = relativePath.Replace('\\', '/');
        return normalizedPath.StartsWith("src/FreeX.App.Avalonia/MacOs/", StringComparison.OrdinalIgnoreCase);
    }
}
