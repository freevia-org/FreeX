namespace FreeX.App.Services.Tests;

internal sealed class TestProgress<T>(Action<T> report) : IProgress<T>
{
    public void Report(T value) => report(value);
}
