using System.IO;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace FreeW.TestSupport;

internal static class HostAccessOwnershipAssertions
{
    private static readonly string[] HookTokens =
    [
        "ForTest",
        "ForTests",
        "TestOnly",
        "TestResponder"
    ];

    internal static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "FreeX.slnx")))
                return directory.FullName;
        }

        throw new DirectoryNotFoundException("Repository root was not found.");
    }

    internal static string CurrentConfiguration() =>
        new DirectoryInfo(AppContext.BaseDirectory).Parent?.Name
        ?? throw new DirectoryNotFoundException("Test configuration directory was not found.");

    internal static IReadOnlySet<string> MemberNames(Type type) =>
        type.GetMembers(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            .Select(member => member.Name)
            .ToHashSet(StringComparer.Ordinal);

    internal static IReadOnlyList<string> ShippingSourceHookViolations(string projectDirectory)
    {
        var violations = new List<string>();
        foreach (var path in Directory.EnumerateFiles(projectDirectory, "*.cs", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(projectDirectory, path);
            if (relativePath.Split(Path.DirectorySeparatorChar)
                .Any(segment => segment is "bin" or "obj"))
                continue;

            var source = File.ReadAllText(path);
            foreach (var token in HookTokens.Where(token => source.Contains(token, StringComparison.Ordinal)))
                violations.Add($"{relativePath}: {token}");
        }

        return violations;
    }

    internal static IReadOnlySet<string> AssemblyMemberNames(string assemblyPath)
    {
        using var stream = File.OpenRead(assemblyPath);
        using var peReader = new PEReader(stream);
        var reader = peReader.GetMetadataReader();
        var members = new HashSet<string>(StringComparer.Ordinal);

        foreach (var typeHandle in reader.TypeDefinitions)
        {
            var type = reader.GetTypeDefinition(typeHandle);
            var typeName = reader.GetString(type.Name);
            var typeNamespace = reader.GetString(type.Namespace);
            var qualifiedTypeName = string.IsNullOrEmpty(typeNamespace)
                ? typeName
                : $"{typeNamespace}.{typeName}";

            members.Add(qualifiedTypeName);
            foreach (var handle in type.GetMethods())
                members.Add($"{qualifiedTypeName}.{reader.GetString(reader.GetMethodDefinition(handle).Name)}");
            foreach (var handle in type.GetProperties())
                members.Add($"{qualifiedTypeName}.{reader.GetString(reader.GetPropertyDefinition(handle).Name)}");
            foreach (var handle in type.GetFields())
                members.Add($"{qualifiedTypeName}.{reader.GetString(reader.GetFieldDefinition(handle).Name)}");
            foreach (var handle in type.GetEvents())
                members.Add($"{qualifiedTypeName}.{reader.GetString(reader.GetEventDefinition(handle).Name)}");
        }

        return members;
    }
}
