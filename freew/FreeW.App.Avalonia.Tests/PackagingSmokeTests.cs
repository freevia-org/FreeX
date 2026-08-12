using FreeW.Validation.Avalonia;

namespace FreeW.App.Avalonia.Tests;

public sealed class PackagingSmokeTests
{
    [Fact]
    public void TryRun_HandlesPackagingSmokeWithoutDisplay()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var handled = PackagingSmoke.TryRun(["--packaging-smoke"], output, error, out var exitCode);

        handled.Should().BeTrue("--packaging-smoke must be handled before the Avalonia host starts");
        exitCode.Should().Be(0, "the headless DOCX round-trip smoke should pass");
        output.ToString().Should().Contain("freew_packaging_smoke=passed");
        error.ToString().Should().BeEmpty();
    }
}
