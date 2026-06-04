namespace FreeX.App.Host.Tests;

internal sealed class TestProgress<T>(Action<T> report) : IProgress<T>
{
    public void Report(T value) => report(value);
}
