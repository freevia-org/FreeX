using FluentAssertions;

namespace FreeX.Core.IO.Tests;

public sealed class SmallArchitectureOwnershipTests
{
    [Fact]
    public void SlicerTimelineRelationshipUris_HaveOneProductionOwner()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");
        var sourceDirectory = Path.Combine(root, "src", "FreeX.Core.IO");
        var relationshipUri = "http://schemas.microsoft.com/office/2007/relationships/slicerCache";
        var owners = Directory.EnumerateFiles(sourceDirectory, "*.cs")
            .Where(path => File.ReadAllText(path).Contains(relationshipUri, StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .ToArray();

        owners.Should().Equal("XlsxSlicerTimelineRelationshipTypes.cs");
    }
}
