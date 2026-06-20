using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class FormatCrossCheckSourceTests
{
    [Fact]
    public void Program_FailsHardValidationFailuresAndEmptyFilteredRuns()
    {
        var source = WorkspaceFileLocator.ReadAllText("tools", "FreeX.FormatCrossCheck", "Program.cs");

        source.Should().Contain("int totalHardFailures = 0");
        source.Should().Contain("int totalCheckedFormats = 0");
        source.Should().Contain("IsHardValidationFailure(r.Kind)");
        source.Should().Contain("results.Count == 0");
        source.Should().Contain("no interchange format matched --format=");
        source.Should().Contain("return totalMissingSources == 0 && totalCheckedFormats > 0 ? 0 : 2;");
        source.Should().Contain("kind is CrossKind.FreeXError or CrossKind.LibreOfficeOpenFailed");
    }
}
