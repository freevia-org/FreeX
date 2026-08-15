using System.ComponentModel;
using System.Diagnostics;

namespace Free.Shared.AppServices.Printing;

public sealed class SystemProcessRunner : IProcessRunner
{
    public async Task<ProcessResult> RunAsync(
        ProcessInvocation invocation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        ArgumentException.ThrowIfNullOrWhiteSpace(invocation.FileName);
        if (invocation.Timeout is { } timeout && timeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(invocation), "Process timeout must be positive.");

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = invocation.FileName,
                WorkingDirectory = invocation.WorkingDirectory ?? string.Empty,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            }
        };
        foreach (var argument in invocation.Arguments)
            process.StartInfo.ArgumentList.Add(argument);

        if (!process.Start())
            throw new InvalidOperationException($"Could not start process '{invocation.FileName}'.");

        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        using var timeoutCancellation = invocation.Timeout is null
            ? null
            : new CancellationTokenSource(invocation.Timeout.Value);
        using var effectiveCancellation = timeoutCancellation is null
            ? null
            : CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                timeoutCancellation.Token);
        var effectiveToken = effectiveCancellation?.Token ?? cancellationToken;

        try
        {
            await process.WaitForExitAsync(effectiveToken).ConfigureAwait(false);
            return new ProcessResult(
                process.ExitCode,
                await outputTask.ConfigureAwait(false),
                await errorTask.ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            await WaitForKilledProcessAsync(process).ConfigureAwait(false);
            return new ProcessResult(
                -1,
                CompletedText(outputTask),
                CompletedText(errorTask),
                TimedOut: true);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            await WaitForKilledProcessAsync(process).ConfigureAwait(false);

            throw;
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (
            ex is InvalidOperationException or Win32Exception or NotSupportedException)
        {
        }
    }

    private static async Task WaitForKilledProcessAsync(Process process)
    {
        try
        {
            await process.WaitForExitAsync()
                .WaitAsync(TimeSpan.FromSeconds(2))
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (
            ex is InvalidOperationException or TimeoutException or ObjectDisposedException)
        {
        }
    }

    private static string CompletedText(Task<string> task) =>
        task.IsCompletedSuccessfully ? task.Result : string.Empty;
}
