namespace Free.Shared.AppServices.Printing;

public sealed record ProcessInvocation(
    string FileName,
    IReadOnlyList<string> Arguments,
    string? WorkingDirectory = null,
    TimeSpan? Timeout = null);

public sealed record ProcessResult(
    int ExitCode,
    string StandardOutput,
    string StandardError,
    bool TimedOut = false)
{
    public bool Succeeded => ExitCode == 0 && !TimedOut;
}

public interface IProcessRunner
{
    Task<ProcessResult> RunAsync(ProcessInvocation invocation, CancellationToken cancellationToken = default);
}
