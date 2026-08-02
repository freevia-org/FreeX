namespace FreeW.Core.Model.Tests;

public sealed class DoNotTrackFormattingModelTests
{
    [Fact]
    public void DoNotTrackFormatting_DefaultsOffAndCanBeEnabled()
    {
        var document = new TextDocument();

        document.DoNotTrackFormatting.Should().BeFalse();

        document.DoNotTrackFormatting = true;

        document.DoNotTrackFormatting.Should().BeTrue();
    }
}
