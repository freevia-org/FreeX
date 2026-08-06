using System.IO;

namespace FreeW.App.Host.Tests;

public sealed class CrossReferenceDedupSourceGuardTests
{
    [Fact]
    public void WpfAndAvaloniaConsumersDelegateCrossReferenceInsertionToThePortableCoordinator()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var sources = new[]
        {
            File.ReadAllText(Path.Combine(root, "freew", "FreeW.App.Host", "Editing", "DocumentView.cs")),
            File.ReadAllText(Path.Combine(root, "freew", "FreeW.App.Avalonia", "Editing", "DocumentView.cs"))
        };

        sources.Should().OnlyContain(source => source.Contains(
            "DocumentReferenceEditingCoordinator ReferenceEdits",
            StringComparison.Ordinal));
        sources.Should().OnlyContain(source => source.Contains(
            "ReferenceEdits.InsertCrossReference(",
            StringComparison.Ordinal));
        sources.Should().OnlyContain(source => !source.Contains(
            "CrossReferences.PlanInsertion(",
            StringComparison.Ordinal));
        sources.Should().OnlyContain(source => !source.Contains(
            "new InsertCrossReferenceCommand(",
            StringComparison.Ordinal));
        sources.Should().OnlyContain(source => !source.Contains("EnsureCrossReferenceAnchor", StringComparison.Ordinal));
        sources.Should().OnlyContain(source => !source.Contains("CrossReferences.BuildField(", StringComparison.Ordinal));
        sources.Should().OnlyContain(source => !source.Contains("CrossReferences.ResolveText(", StringComparison.Ordinal));
        sources.Should().OnlyContain(source => !source.Contains("name = \"_Ref\"", StringComparison.Ordinal));
    }
}
