namespace Free.Shared.AppServices.Printing;

public sealed record ProcessInvocation(
    string FileName,
    IReadOnlyList<string> Arguments,
    string? WorkingDirectory = null);

public sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError)
{
    public bool Succeeded => ExitCode == 0;
}

public interface IProcessRunner
{
    Task<ProcessResult> RunAsync(ProcessInvocation invocation, CancellationToken cancellationToken = default);
}
