using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using FreeP.App.Avalonia;

namespace FreeP.App.Avalonia.Tests;

public sealed class ValidationHostOwnershipTests
{
    [Fact]
    public void ShippingAssembly_DoesNotContainValidationHostPayload()
    {
        var assembly = typeof(MainWindow).Assembly;

        typeof(MainWindow).GetMethod(
                "CreateValidationAccessAdapter",
                BindingFlags.Instance | BindingFlags.NonPublic)
            .Should().BeNull();
        typeof(MainWindow).GetNestedType(
                "ValidationAccessAdapter",
                BindingFlags.NonPublic)
            .Should().BeNull();
        assembly.GetType("FreeP.App.Avalonia.StartupDirtyTrace").Should().BeNull();
        assembly.GetType("FreeP.App.Avalonia.StartupDirtyTraceEntry").Should().BeNull();

        var validationTypes = ReadTypeNames(RendererAssemblyPath("Validation"));
        validationTypes.Should().Contain("ValidationAccessAdapter");
        validationTypes.Should().Contain("StartupDirtyTrace");
        validationTypes.Should().Contain("StartupDirtyTraceEntry");
    }

    [Fact]
    public void ValidationProject_OwnsConditionallyCompiledPayload()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        var shippingDirectory = Path.Combine(root, "freep", "FreeP.App.Avalonia");
        var supportDirectory = Path.Combine(root, "freep", "TestSupport", "Validation.Avalonia");

        File.Exists(Path.Combine(shippingDirectory, "MainWindow.ValidationAccessAdapter.cs"))
            .Should().BeFalse();
        File.Exists(Path.Combine(shippingDirectory, "StartupDirtyTrace.cs"))
            .Should().BeFalse();
        File.Exists(Path.Combine(supportDirectory, "MainWindow.ValidationAccessAdapter.cs"))
            .Should().BeTrue();
        File.Exists(Path.Combine(supportDirectory, "StartupDirtyTrace.cs"))
            .Should().BeTrue();

        var shippingProject = File.ReadAllText(Path.Combine(
            shippingDirectory,
            "FreeP.App.Avalonia.csproj"));
        var validationProject = File.ReadAllText(Path.Combine(
            supportDirectory,
            "FreeP.Validation.Avalonia.csproj"));

        shippingProject.Should().Contain("Condition=\"'$(FreePValidationHost)' == 'true'\"");
        shippingProject.Should().Contain("..\\TestSupport\\Validation.Avalonia\\MainWindow.ValidationAccessAdapter.cs");
        shippingProject.Should().Contain("..\\TestSupport\\Validation.Avalonia\\StartupDirtyTrace.cs");
        validationProject.Should().Contain("Compile Remove=\"MainWindow.ValidationAccessAdapter.cs\"");
        validationProject.Should().Contain("Compile Remove=\"StartupDirtyTrace.cs\"");
        validationProject.Should().Contain("AdditionalProperties=\"FreePValidationHost=true\"");
    }

    private static string RendererAssemblyPath(string? variant = null)
    {
        var testOutput = Path.GetDirectoryName(typeof(ValidationHostOwnershipTests).Assembly.Location)!;
        var targetFramework = Path.GetFileName(testOutput);
        var configuration = Path.GetFileName(Path.GetDirectoryName(testOutput))!;
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        var binDirectory = Path.Combine(root, "freep", "FreeP.App.Avalonia", "bin");
        return variant is null
            ? Path.Combine(binDirectory, configuration, targetFramework, "FreeP.dll")
            : Path.Combine(binDirectory, variant, configuration, targetFramework, "FreeP.dll");
    }

    private static IReadOnlyList<string> ReadTypeNames(string assemblyPath)
    {
        using var stream = File.OpenRead(assemblyPath);
        using var peReader = new PEReader(stream);
        var metadata = peReader.GetMetadataReader();
        return metadata.TypeDefinitions
            .Select(handle => metadata.GetString(metadata.GetTypeDefinition(handle).Name))
            .ToArray();
    }
}
