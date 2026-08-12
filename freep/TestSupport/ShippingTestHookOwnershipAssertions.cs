using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Xml.Linq;

namespace FreeP.TestSupport;

internal static class ShippingTestHookOwnershipAssertions
{
    internal static readonly string[] ForbiddenSourceTokens =
    [
        "ForTests",
        "ForTest ",
        "ForTest=>",
        "ForTest =>",
        "ForAccessibilityTests",
        "TestResponder",
        "TestHook",
        "BuildResultForTest",
    ];

    internal static IReadOnlyList<string> FindShippingSourceViolations(string projectDirectory) =>
        Directory.EnumerateFiles(projectDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(path))
            .SelectMany(path => ForbiddenSourceTokens
                .Where(token => File.ReadAllText(path).Contains(token, StringComparison.Ordinal))
                .Select(token => $"{Path.GetFileName(path)}: {token}"))
            .ToArray();

    internal static IReadOnlyList<string> FindUnconditionalSupportItems(
        string projectPath,
        string supportPathFragment,
        string requiredProperty)
    {
        var document = XDocument.Load(projectPath);
        return document
            .Descendants("Compile")
            .Where(element =>
                ((string?)element.Attribute("Include"))?.Contains(
                    supportPathFragment,
                    StringComparison.OrdinalIgnoreCase) == true)
            .Where(element =>
                ((string?)element.Parent?.Attribute("Condition"))?.Contains(
                    requiredProperty,
                    StringComparison.Ordinal) != true)
            .Select(element => (string)element.Attribute("Include")!)
            .ToArray();
    }

    internal static IReadOnlyList<string> FindFriendItemsMissingCondition(
        string projectPath,
        string friendAssembly,
        string requiredProperty)
    {
        var document = XDocument.Load(projectPath);
        return document
            .Descendants("InternalsVisibleTo")
            .Where(element => string.Equals(
                (string?)element.Attribute("Include"),
                friendAssembly,
                StringComparison.Ordinal))
            .Where(element =>
                ((string?)element.Parent?.Attribute("Condition"))?.Contains(
                    requiredProperty,
                    StringComparison.Ordinal) != true)
            .Select(_ => friendAssembly)
            .ToArray();
    }

    internal static IReadOnlyList<string> ReadCompiledTestHookNames(string assemblyPath)
    {
        using var stream = File.OpenRead(assemblyPath);
        using var peReader = new PEReader(stream);
        var metadata = peReader.GetMetadataReader();
        var names = new HashSet<string>(StringComparer.Ordinal);

        foreach (var handle in metadata.TypeDefinitions)
            AddIfTestHook(metadata.GetString(metadata.GetTypeDefinition(handle).Name));
        foreach (var handle in metadata.MethodDefinitions)
            AddIfTestHook(metadata.GetString(metadata.GetMethodDefinition(handle).Name));
        foreach (var handle in metadata.PropertyDefinitions)
            AddIfTestHook(metadata.GetString(metadata.GetPropertyDefinition(handle).Name));
        foreach (var handle in metadata.FieldDefinitions)
            AddIfTestHook(metadata.GetString(metadata.GetFieldDefinition(handle).Name));

        return names.OrderBy(name => name, StringComparer.Ordinal).ToArray();

        void AddIfTestHook(string name)
        {
            if (name.Contains("ForTests", StringComparison.Ordinal) ||
                name.EndsWith("ForTest", StringComparison.Ordinal) ||
                name.Contains("ForAccessibilityTests", StringComparison.Ordinal) ||
                name.Contains("TestResponder", StringComparison.Ordinal) ||
                name.Contains("TestHook", StringComparison.Ordinal) ||
                name.Contains("BuildResultForTest", StringComparison.Ordinal))
            {
                names.Add(name);
            }
        }
    }

    internal static string ShippingAssemblyPath(
        string root,
        string projectDirectory,
        string assemblyFileName)
    {
        var testOutput = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar));
        var configuration = testOutput.Parent?.Name
            ?? throw new InvalidOperationException("Could not determine the test configuration.");
        var targetFramework = testOutput.Name;
        return Path.Combine(
            root,
            "freep",
            projectDirectory,
            "bin",
            configuration,
            targetFramework,
            assemblyFileName);
    }

    private static bool IsBuildOutput(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ||
        path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);
}
