namespace FreeX.App.Host.Tests;

internal sealed class TestEnvironmentVariableScope : IDisposable
{
    private readonly string _name;
    private readonly string? _previousValue;

    private TestEnvironmentVariableScope(string name, string? value)
    {
        _name = name;
        _previousValue = Environment.GetEnvironmentVariable(name);
        Environment.SetEnvironmentVariable(name, value);
    }

    public static TestEnvironmentVariableScope Set(string name, string? value) =>
        new(name, value);

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(_name, _previousValue);
    }
}
