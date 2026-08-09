using System.Text;

namespace Free.ToolsShared;

public sealed record GeneratedEvidenceToolSpec(
    string RepositoryMarker,
    string DefaultRelativeOutputPath,
    Func<string> BuildReport);

public static class GeneratedEvidenceToolRunner
{
    public static int Run(IReadOnlyList<string> args, GeneratedEvidenceToolSpec spec)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(spec);

        var root = RepositoryRootLocator.Find(AppContext.BaseDirectory, spec.RepositoryMarker)
            ?? throw new InvalidOperationException(
                $"Could not locate the repository root containing {spec.RepositoryMarker}.");
        var configuredOutput = GetOption(args, "--output");
        var outputPath = configuredOutput is not null
            ? Path.GetFullPath(configuredOutput, Environment.CurrentDirectory)
            : Path.Combine(root, spec.DefaultRelativeOutputPath);
        var report = spec.BuildReport();

        if (args.Contains("--check", StringComparer.Ordinal))
        {
            if (!File.Exists(outputPath) ||
                !string.Equals(File.ReadAllText(outputPath), report, StringComparison.Ordinal))
            {
                Console.Error.WriteLine($"Generated evidence is stale: {outputPath}");
                return 1;
            }

            Console.WriteLine($"Generated evidence is current: {outputPath}");
            return 0;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        File.WriteAllText(
            outputPath,
            report,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        Console.WriteLine($"Generated evidence: {outputPath}");
        return 0;
    }

    private static string? GetOption(IReadOnlyList<string> args, string name)
    {
        for (var index = 0; index < args.Count; index++)
        {
            if (!string.Equals(args[index], name, StringComparison.Ordinal))
                continue;
            if (index + 1 >= args.Count)
                throw new ArgumentException($"Missing value after {name}.");
            return args[index + 1];
        }

        return null;
    }

}
