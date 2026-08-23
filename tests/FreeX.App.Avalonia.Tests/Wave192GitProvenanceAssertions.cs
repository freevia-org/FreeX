using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace FreeX.App.Avalonia.Tests;

internal static class Wave192GitProvenanceAssertions
{
    public static void Verify(JsonElement manifest)
    {
        var repositoryRoot = Path.GetDirectoryName(
            TestWorkspaceFileLocator.FindFromWorkspaceRoot("FreeX.slnx"))!;
        var imageRevision = manifest.GetProperty("sourceCommitAtImageBuild").GetString()!;
        var integration = manifest.GetProperty("integrationProvenance");
        var integrationResultRevision = integration.GetProperty("resultCommit").GetString()!;
        var integrationSourceRevision = integration.GetProperty("sourceEquivalentCommit").GetString()!;
        var integrationEvidenceRevision = integration.GetProperty("evidenceCommit").GetString()!;
        var manifestPath = integration.GetProperty("manifestPath").GetString()!;
        var integrationIndexPath = integration.GetProperty("indexPath").GetString()!;
        var integrationResultPath = integration.GetProperty("resultPath").GetString()!;

        integration.GetProperty("imageSourceCommit").GetString().Should().Be(imageRevision);
        GitSucceeds(repositoryRoot, "cat-file", "-e", $"{imageRevision}^{{commit}}");
        GitSucceeds(repositoryRoot, "cat-file", "-e", $"{integrationResultRevision}:{integrationResultPath}");
        GitSucceeds(repositoryRoot, "cat-file", "-e", $"{integrationResultRevision}:{manifestPath}");
        GitSucceeds(repositoryRoot, "merge-base", "--is-ancestor", integrationSourceRevision, integrationResultRevision);
        GitSucceeds(repositoryRoot, "merge-base", "--is-ancestor", integrationEvidenceRevision, integrationResultRevision);
        Encoding.UTF8.GetString(ReadGitBlob(repositoryRoot, integrationResultRevision, integrationIndexPath))
            .Should()
            .Contain(manifestPath, "the integration index must make this evidence manifest reachable");

        var audits = manifest.GetProperty("gitBlobAudit")
            .EnumerateArray()
            .ToDictionary(entry => entry.GetProperty("path").GetString()!, StringComparer.Ordinal);
        foreach (var provenance in manifest.GetProperty("provenanceFiles").EnumerateArray())
        {
            var path = provenance.GetProperty("path").GetString()!;
            var hashMode = provenance.GetProperty("hashMode").GetString()!;
            var expected = provenance.GetProperty("sha256").GetString()!;
            var imageBlobHash = ComputeHash(ReadGitBlob(repositoryRoot, imageRevision, path), hashMode);
            var integrationBlobHash = ComputeHash(ReadGitBlob(repositoryRoot, integrationSourceRevision, path), hashMode);

            imageBlobHash.Should().Be(expected, $"{path} must match the declared image-build revision");
            integrationBlobHash.Should().Be(
                expected,
                $"{path} must be byte-equivalent in the integration-reachable source revision");

            audits.Should().ContainKey(path);
            var audit = audits[path];
            audit.GetProperty("hashMode").GetString().Should().Be(hashMode);
            audit.GetProperty("gitBlobContentSha256").GetString().Should().Be(imageBlobHash);
            audit.GetProperty("worktreeSha256").GetString().Should().Be(expected);
            audit.GetProperty("match").GetBoolean().Should().BeTrue();
        }
    }

    private static byte[] ReadGitBlob(string repositoryRoot, string revision, string path) =>
        RunGit(repositoryRoot, "cat-file", "blob", $"{revision}:{path}").Output;

    private static void GitSucceeds(string repositoryRoot, params string[] arguments)
    {
        var result = RunGit(repositoryRoot, arguments);
        result.ExitCode.Should().Be(0, result.Error);
    }

    private static GitResult RunGit(string repositoryRoot, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = repositoryRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start git for provenance verification.");
        using var output = new MemoryStream();
        process.StandardOutput.BaseStream.CopyTo(output);
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return new GitResult(process.ExitCode, output.ToArray(), error);
    }

    private static string ComputeHash(byte[] bytes, string hashMode)
    {
        var hashBytes = hashMode switch
        {
            "raw" => bytes,
            "canonical-lf" => Encoding.UTF8.GetBytes(
                new UTF8Encoding(false, true).GetString(bytes)
                    .Replace("\r\n", "\n", StringComparison.Ordinal)
                    .Replace("\r", "\n", StringComparison.Ordinal)),
            _ => throw new InvalidDataException($"Unknown provenance hash mode '{hashMode}'."),
        };
        return Convert.ToHexString(SHA256.HashData(hashBytes)).ToLowerInvariant();
    }

    private sealed record GitResult(int ExitCode, byte[] Output, string Error);
}
