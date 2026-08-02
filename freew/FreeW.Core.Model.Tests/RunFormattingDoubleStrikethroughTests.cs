namespace FreeW.Core.Model.Tests;

public sealed class RunFormattingDoubleStrikethroughTests
{
    [Fact]
    public void DoubleStrikethrough_DefaultsToFalse()
    {
        RunFormatting.Default.DoubleStrikethrough.Should().BeFalse();
        new RunFormatting().DoubleStrikethrough.Should().BeFalse();
    }

    [Fact]
    public void DoubleStrikethrough_IsPreservedByRecordCopy()
    {
        var source = new RunFormatting { DoubleStrikethrough = true, Bold = true };

        var copy = source with { Bold = false };

        copy.DoubleStrikethrough.Should().BeTrue();
    }
}
