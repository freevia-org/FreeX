namespace FreeW.Core.Model.Tests;

public sealed class RunFormattingWebHiddenTests
{
    [Fact]
    public void WebHidden_DefaultsToFalse()
    {
        RunFormatting.Default.WebHidden.Should().BeFalse();
        new RunFormatting().WebHidden.Should().BeFalse();
    }

    [Fact]
    public void WebHidden_IsPreservedByRecordCopy()
    {
        var source = new RunFormatting { WebHidden = true, Bold = true };

        var copy = source with { Bold = false };

        copy.WebHidden.Should().BeTrue();
    }
}
