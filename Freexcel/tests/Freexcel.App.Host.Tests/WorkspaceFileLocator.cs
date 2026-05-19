using System.IO;

namespace Freexcel.App.Host.Tests;

internal static class WorkspaceFileLocator
{
    public static string Find(params string[] relativeParts)
    {
        foreach (var root in CandidateRoots())
        {
            var directory = new DirectoryInfo(root);
            while (directory is not null)
            {
                var candidate = Path.Combine(new[] { directory.FullName }.Concat(relativeParts).ToArray());
                if (File.Exists(candidate))
                    return candidate;

                directory = directory.Parent;
            }
        }

        throw new FileNotFoundException("Could not locate workspace file.", Path.Combine(relativeParts));
    }

    private static IEnumerable<string> CandidateRoots()
    {
        var envRoot = Environment.GetEnvironmentVariable("FREEXCEL_REPO_ROOT");
        if (!string.IsNullOrWhiteSpace(envRoot))
            yield return envRoot;

        yield return Environment.CurrentDirectory;
        yield return AppContext.BaseDirectory;
    }
}
