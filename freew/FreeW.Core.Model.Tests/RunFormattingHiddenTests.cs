namespace FreeW.Core.Model.Tests;

public sealed class RunFormattingHiddenTests
{
    [Fact]
    public void Hidden_DefaultsToFalse()
    {
        RunFormatting.Default.Hidden.Should().BeFalse();
        new RunFormatting().Hidden.Should().BeFalse();
    }

    [Fact]
    public void Hidden_IsPreservedByRecordCopy()
    {
        var source = new RunFormatting { Hidden = true, Bold = true };

        var copy = source with { Bold = false };

        copy.Hidden.Should().BeTrue();
    }
}
