using System.Diagnostics;
using System.IO;

namespace FreeW.App.Host.Tests;

internal static class IsolatedTestProcess
{
    public static bool RunIfNeeded(string environmentVariable, string fullyQualifiedTestName)
    {
        if (string.Equals(
                Environment.GetEnvironmentVariable(environmentVariable),
                "1",
                StringComparison.Ordinal))
        {
            return false;
        }

        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var project = Path.Combine(root, "freew", "FreeW.App.Host.Tests", "FreeW.App.Host.Tests.csproj");
        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = root,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var argument in new[]
        {
            "test",
            project,
            "--configuration", "Release",
            "--no-build",
            "--disable-build-servers",
            "-p:UseSharedCompilation=false",
            "-p:NodeReuse=false",
            "/nr:false",
            "-m:1",
            "--filter",
            "FullyQualifiedName=" + fullyQualifiedTestName,
            "--logger", "console;verbosity=minimal",
        })
        {
            startInfo.ArgumentList.Add(argument);
        }
        startInfo.Environment[environmentVariable] = "1";

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start the isolated test process.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(TimeSpan.FromMinutes(4)))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("The isolated test process exceeded four minutes.");
        }

        var transcript = output.GetAwaiter().GetResult() + error.GetAwaiter().GetResult();
        process.ExitCode.Should().Be(0, transcript);
        return true;
    }
}
