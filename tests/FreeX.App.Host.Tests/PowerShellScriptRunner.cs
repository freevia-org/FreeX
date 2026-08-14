using System.Diagnostics;
using System.Text.RegularExpressions;
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

    public static PowerShellResult RunToolScript(string scriptName, string workingDirectory)
    {
        return Run(WorkspaceFileLocator.FindToolScript(scriptName), workingDirectory);
    }

    public static PowerShellResult RunToolScript(string scriptName, string workingDirectory, string arguments)
    {
        return Run(WorkspaceFileLocator.FindToolScript(scriptName), workingDirectory, arguments);
    }

    public static PowerShellResult RunToolScriptFromTemporaryWorkingDirectory(string scriptName)
    {
        using var workingDirectory = new TestTemporaryDirectory();
        return RunToolScript(scriptName, workingDirectory.Path);
    }

    public static PowerShellResult RunToolScriptFromTemporaryWorkingDirectory(string scriptName, string arguments)
    {
        using var workingDirectory = new TestTemporaryDirectory();
        return RunToolScript(scriptName, workingDirectory.Path, arguments);
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
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        Task.WaitAll(outputTask, errorTask);

        return new PowerShellResult(process.ExitCode, outputTask.Result, errorTask.Result);
    }
}

internal sealed record PowerShellResult(int ExitCode, string Output, string Error)
{
    public string CombinedOutput => Output + Error;

    public string NormalizedCombinedOutput => Regex.Replace(CombinedOutput, "\\s+", " ");
}
