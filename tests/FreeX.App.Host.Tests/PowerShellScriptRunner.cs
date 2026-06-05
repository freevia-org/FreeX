using System.Diagnostics;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

internal static class PowerShellScriptRunner
{
    public static PowerShellResult Run(string scriptPath, string workingDirectory)
    {
        return RunWithPowerShellArguments(workingDirectory, $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\"");
    }

    public static PowerShellResult Run(string scriptPath, string workingDirectory, string arguments)
    {
        return RunWithPowerShellArguments(workingDirectory, $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\" {arguments}");
    }

    private static PowerShellResult RunWithPowerShellArguments(string workingDirectory, string powerShellArguments)
    {
        return TestProcessRunner.Run("powershell.exe", powerShellArguments, workingDirectory);
    }
}

internal static class TestProcessRunner
{
    public static PowerShellResult Run(string fileName, string arguments, string workingDirectory)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        process.Start().Should().BeTrue();
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        return new PowerShellResult(process.ExitCode, output, error);
    }
}

internal sealed record PowerShellResult(int ExitCode, string Output, string Error);
