namespace FreeW.Core.Model.Tests;

public sealed class DoNotTrackMovesModelTests
{
    [Fact]
    public void DoNotTrackMoves_DefaultsOffAndCanBeEnabled()
    {
        var document = new TextDocument();

        document.DoNotTrackMoves.Should().BeFalse();

        document.DoNotTrackMoves = true;

        document.DoNotTrackMoves.Should().BeTrue();
    }
}
