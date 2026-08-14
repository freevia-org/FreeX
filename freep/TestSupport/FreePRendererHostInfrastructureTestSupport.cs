using System.IO;
using System.Reflection;
using System.Xml.Linq;
using FluentAssertions;

namespace FreeP.TestSupport;

internal sealed record FreePRendererHostTestProfile(
    string ProjectDirectory,
    string ProjectFileName,
    string HostAccessDirectory,
    string VisualEvidenceDirectory,
    string VisualEvidenceProjectFileName,
    string VisualCaptureAdapterTypeName,
    string ShippingAssemblyFileName,
    IReadOnlyList<FreePRendererFriendAssemblyPolicy> FriendAssemblies)
{
    internal static FreePRendererHostTestProfile Avalonia { get; } = new(
        "FreeP.App.Avalonia",
        "FreeP.App.Avalonia.csproj",
        "HostAccess.Avalonia",
        "VisualEvidence.Avalonia",
        "FreeP.VisualEvidence.Avalonia.csproj",
        "AvaloniaVisualCaptureAdapter",
        "FreeP.dll",
        [
            new("FreeP.App.Avalonia.Tests", "FreePHostAccess"),
            new("FreeP.VisualEvidence.Avalonia", "FreePVisualEvidenceHost"),
            new("FreeP.Validation.Avalonia", "FreePValidationHost"),
        ]);

    internal static FreePRendererHostTestProfile Wpf { get; } = new(
        "FreeP.App.Host",
        "FreeP.App.Host.csproj",
        "HostAccess.Wpf",
        "VisualEvidence.Wpf",
        "FreeP.VisualEvidence.Wpf.csproj",
        "WpfVisualCaptureAdapter",
        "FreeP.App.Host.dll",
        [
            new("FreeP.App.Host.Tests", "FreePHostAccess"),
            new("FreeP.VisualEvidence.Wpf", "FreePVisualEvidenceHost"),
        ]);
}

internal sealed record FreePRendererFriendAssemblyPolicy(
    string AssemblyName,
    string RequiredProperty);

internal static class FreePRendererHostInfrastructureTestSupport
{
    private const string SolutionFileName = "FreeP.slnx";

    internal static void AssertLocalizationKeysExist(
        FreePRendererHostTestProfile profile,
        IReadOnlySet<string> neutralResourceKeys) =>
        LocalizationKeyIntegrityTestSupport.AssertAllLiteralUiTextKeysExist(
            SolutionFileName,
            neutralResourceKeys,
            requireLiteralUses: false,
            "freep",
            profile.ProjectDirectory);

    internal static void AssertRepresentativeLocalizationKeysResolve(
        Func<string, string> getNeutralText) =>
        LocalizationKeyIntegrityTestSupport.AssertKeysResolveToRealNonSentinelText(
            getNeutralText,
            "Common_Ok",
            "Common_Cancel");

    internal static void AssertVisualCaptureAdapterOwnership(
        Type mainWindowType,
        FreePRendererHostTestProfile profile)
    {
        mainWindowType.GetMethod(
                "CreateVisualCaptureAdapter",
                BindingFlags.Instance | BindingFlags.NonPublic)
            .Should().BeNull();
        mainWindowType.GetNestedType(
                profile.VisualCaptureAdapterTypeName,
                BindingFlags.NonPublic)
            .Should().BeNull();

        var root = FindRoot();
        var shippingDirectory = ProjectDirectory(root, profile);
        var evidenceDirectory = Path.Combine(root, "freep", "TestSupport", profile.VisualEvidenceDirectory);

        File.Exists(Path.Combine(shippingDirectory, "MainWindow.VisualCaptureAdapter.cs"))
            .Should().BeFalse();
        File.Exists(Path.Combine(evidenceDirectory, "MainWindow.VisualCaptureAdapter.cs"))
            .Should().BeTrue();

        var shippingProject = File.ReadAllText(Path.Combine(shippingDirectory, profile.ProjectFileName));
        var evidenceProject = File.ReadAllText(Path.Combine(evidenceDirectory, profile.VisualEvidenceProjectFileName));
        shippingProject.Should().Contain("Condition=\"'$(FreePVisualEvidenceHost)' == 'true'\"");
        shippingProject.Should().Contain(
            $"..\\TestSupport\\{profile.VisualEvidenceDirectory}\\MainWindow.VisualCaptureAdapter.cs");
        evidenceProject.Should().Contain("Compile Remove=\"MainWindow.VisualCaptureAdapter.cs\"");
        evidenceProject.Should().Contain("AdditionalProperties=\"FreePVisualEvidenceHost=true\"");
    }

    internal static void AssertHostAccessOwnership(FreePRendererHostTestProfile profile)
    {
        var root = FindRoot();
        var hostDirectory = ProjectDirectory(root, profile);
        var supportDirectory = Path.Combine(root, "freep", "TestSupport", profile.HostAccessDirectory);
        var projectPath = Path.Combine(hostDirectory, profile.ProjectFileName);

        File.Exists(Path.Combine(hostDirectory, "MainWindow.TestAccess.cs")).Should().BeFalse();
        File.Exists(Path.Combine(supportDirectory, "MainWindow.TestAccess.cs")).Should().BeTrue();

        var project = File.ReadAllText(projectPath);
        project.Should().Contain("'$(FreePHostAccess)' == 'true'");
        project.Should().Contain($"..\\TestSupport\\{profile.HostAccessDirectory}\\MainWindow.TestAccess.cs");
        project.Should().Contain($"..\\TestSupport\\{profile.HostAccessDirectory}\\MainWindow.DiagnosticsAccess.cs");

        foreach (var friend in profile.FriendAssemblies)
        {
            project.Should().Contain($"<InternalsVisibleTo Include=\"{friend.AssemblyName}\"");
            ShippingTestHookOwnershipAssertions.FindFriendItemsMissingCondition(
                    projectPath,
                    friend.AssemblyName,
                    friend.RequiredProperty)
                .Should().BeEmpty();
        }

        ShippingTestHookOwnershipAssertions.FindUnconditionalSupportItems(
                projectPath,
                $"TestSupport\\{profile.HostAccessDirectory}",
                "FreePHostAccess")
            .Should().BeEmpty();
    }

    internal static void AssertSharedRendererHostVariantPolicy(
        FreePRendererHostTestProfile profile)
    {
        var root = FindRoot();
        var projectPath = Path.Combine(ProjectDirectory(root, profile), profile.ProjectFileName);
        var project = XDocument.Load(projectPath);
        project.Descendants("Import")
            .Select(element => (string?)element.Attribute("Project"))
            .Should().Contain("..\\FreeP.RendererHostVariants.props");
        project.Descendants("OutputPath").Should().BeEmpty(
            "renderer-host output variants are owned by the shared props file");
        project.Descendants("IntermediateOutputPath").Should().BeEmpty(
            "renderer-host intermediate variants are owned by the shared props file");

        var variantsPath = Path.Combine(root, "freep", "FreeP.RendererHostVariants.props");
        var variants = XDocument.Load(variantsPath);
        variants.Descendants("FreePRendererHostVariant").Should().HaveCount(5);

        var isolatedProperties = variants.Descendants("GlobalPropertiesToRemove").Single().Value
            .Split(';', StringSplitOptions.RemoveEmptyEntries);
        isolatedProperties.Should().Contain(
        [
            "FreePHostAccess",
            "FreePSlideShowTestSupport",
            "FreePValidationHost",
            "FreePVisualEvidenceHost",
        ]);
    }

    internal static void AssertShippingSourceAndAssemblyExcludeHostTestHooks(
        FreePRendererHostTestProfile profile)
    {
        var root = FindRoot();
        var hostDirectory = ProjectDirectory(root, profile);
        ShippingTestHookOwnershipAssertions.FindShippingSourceViolations(hostDirectory)
            .Should().BeEmpty();

        var assemblyPath = ShippingTestHookOwnershipAssertions.ShippingAssemblyPath(
            root,
            profile.ProjectDirectory,
            profile.ShippingAssemblyFileName);
        File.Exists(assemblyPath).Should().BeTrue(
            "the normal shipping variant is built before ownership tests");
        ShippingTestHookOwnershipAssertions.ReadCompiledTestHookNames(assemblyPath)
            .Should().BeEmpty();
    }

    private static string FindRoot() =>
        TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory(SolutionFileName);

    private static string ProjectDirectory(
        string root,
        FreePRendererHostTestProfile profile) =>
        Path.Combine(root, "freep", profile.ProjectDirectory);
}
