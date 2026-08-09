using Free.ToolsShared;
using FluentAssertions;

namespace FreeX.ParityCompare.Tests;

public sealed class RepositoryRootLocatorTests
{
    [Fact]
    public void Find_ReturnsNearestAncestorContainingMarker()
    {
        using var temp = new TestTemporaryDirectory();
        var nearerRoot = Path.Combine(temp.Path, "nearer");
        var nested = Path.Combine(nearerRoot, "one", "two");
        Directory.CreateDirectory(nested);
        File.WriteAllText(Path.Combine(temp.Path, "repo.marker"), string.Empty);
        File.WriteAllText(Path.Combine(nearerRoot, "repo.marker"), string.Empty);

        var result = RepositoryRootLocator.Find(nested, "repo.marker");

        result.Should().Be(nearerRoot);
    }

    [Fact]
    public void Find_ReturnsNullWhenMarkerIsAbsent()
    {
        using var temp = new TestTemporaryDirectory();
        var nested = Path.Combine(temp.Path, "one", "two");
        Directory.CreateDirectory(nested);

        RepositoryRootLocator.Find(nested, "missing.marker").Should().BeNull();
    }

    [Fact]
    public void Find_IgnoresDirectoryMarkers()
    {
        using var temp = new TestTemporaryDirectory();
        var nearerRoot = Path.Combine(temp.Path, "nearer");
        var nested = Path.Combine(nearerRoot, "one", "two");
        Directory.CreateDirectory(nested);
        File.WriteAllText(Path.Combine(temp.Path, "repo.marker"), string.Empty);
        Directory.CreateDirectory(Path.Combine(nearerRoot, "repo.marker"));

        RepositoryRootLocator.Find(nested, "repo.marker").Should().Be(temp.Path);
    }

    [Fact]
    public void FindByDirectoryMarker_ReturnsNearestAncestorContainingMarker()
    {
        using var temp = new TestTemporaryDirectory();
        var nearerRoot = Path.Combine(temp.Path, "nearer");
        var nested = Path.Combine(nearerRoot, "one", "two");
        Directory.CreateDirectory(nested);
        Directory.CreateDirectory(Path.Combine(temp.Path, "fidelity-corpus"));
        Directory.CreateDirectory(Path.Combine(nearerRoot, "fidelity-corpus"));

        var result = RepositoryRootLocator.FindByDirectoryMarker(nested, "fidelity-corpus");

        result.Should().Be(nearerRoot);
    }

    [Fact]
    public void FindByDirectoryMarker_IgnoresFileMarkers()
    {
        using var temp = new TestTemporaryDirectory();
        var nearerRoot = Path.Combine(temp.Path, "nearer");
        var nested = Path.Combine(nearerRoot, "one", "two");
        Directory.CreateDirectory(nested);
        Directory.CreateDirectory(Path.Combine(temp.Path, "fidelity-corpus"));
        File.WriteAllText(Path.Combine(nearerRoot, "fidelity-corpus"), string.Empty);

        RepositoryRootLocator.FindByDirectoryMarker(nested, "fidelity-corpus")
            .Should().Be(temp.Path);
    }

    [Fact]
    public void FindByDirectoryMarker_ReturnsNullWhenMarkerIsAbsent()
    {
        using var temp = new TestTemporaryDirectory();
        var nested = Path.Combine(temp.Path, "one", "two");
        Directory.CreateDirectory(nested);

        RepositoryRootLocator.FindByDirectoryMarker(nested, "missing-directory").Should().BeNull();
    }
}
