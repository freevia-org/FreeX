namespace FreeW.Core.Model.Tests;

public sealed class TrackRevisionsModelTests
{
    [Fact]
    public void TrackRevisions_DefaultsOffAndCanBeEnabled()
    {
        var document = new TextDocument();

        document.TrackRevisions.Should().BeFalse();

        document.TrackRevisions = true;

        document.TrackRevisions.Should().BeTrue();
    }
}
