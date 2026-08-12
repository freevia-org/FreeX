using FluentAssertions;

namespace FreeX.Core.IO.Tests;

public sealed class SmallArchitectureOwnershipTests
{
    [Fact]
    public void SlicerTimelineRelationshipUris_HaveOneProductionOwner()
    {
        var root = FindRepositoryRoot();
        var sourceDirectory = Path.Combine(root, "src", "FreeX.Core.IO");
        var relationshipUri = "http://schemas.microsoft.com/office/2007/relationships/slicerCache";
        var owners = Directory.EnumerateFiles(sourceDirectory, "*.cs")
            .Where(path => File.ReadAllText(path).Contains(relationshipUri, StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .ToArray();

        owners.Should().Equal("XlsxSlicerTimelineRelationshipTypes.cs");
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "FreeX.slnx")))
                return directory.FullName;
        }

        throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
