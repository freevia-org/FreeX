using System.IO;

namespace FreeW.Core.Model.Tests;

public sealed class SourceCloneOwnershipTests
{
    [Fact]
    public void SourceModel_OwnsCloneIdentityAndContributorProjection()
    {
        var model = ReadSource("freew", "FreeW.Core.Model", "TextDocument.cs");
        var identity = ReadSource("freew", "FreeW.Core.Model", "SourceTagIdentity.cs");
        var merge = ReadSource("freew", "FreeW.Core.Model", "DocumentMerge.cs");
        var commands = ReadSource("freew", "FreeW.Core.Model", "EditCommands.cs");
        var planner = ReadSource("freew", "FreeW.App.Presentation", "Ribbon", "SourceManagementDialogPlanner.cs");
        var store = ReadSource("freew", "FreeW.App.Presentation", "Ribbon", "MasterSourceStore.cs");

        model.Should().Contain("public Source Clone()");
        model.Should().Contain("public Source CloneWithTag(string? tag)");
        model.Should().Contain("public Source CloneCanonicalized()");
        identity.Should().Contain("public static class SourceTagIdentity");
        merge.Should().Contain("sourceEntry.CloneWithTag(targetTag)");
        merge.Should().NotContain("private static Source CloneSource(");
        commands.Should().Contain("sources.Select(source => source.Clone()).ToArray()");
        planner.Should().Contain("return source.CloneCanonicalized();");
        store.Should().Contain("SourceAuthorPerson.Canonicalize(");
        store.Should().Contain("SourceTagIdentity.Canonicalize(");
    }

    [Fact]
    public void Renderers_DelegateSourceReplacementThroughPortableEditingCoordinator()
    {
        var wpf = ReadSource("freew", "FreeW.App.Host", "Editing", "DocumentView.cs");
        var avalonia = ReadSource("freew", "FreeW.App.Avalonia", "Editing", "DocumentView.cs");

        foreach (var renderer in new[] { wpf, avalonia })
        {
            renderer.Should().Contain("ReferenceEdits.ReplaceSources(sources)");
            renderer.Should().NotContain("new ReplaceSourcesCommand(");
            renderer.Should().NotContain("private static Source CloneSource(");
        }
    }

    private static string ReadSource(params string[] relativePath)
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        return File.ReadAllText(Path.Combine(new[] { root }.Concat(relativePath).ToArray()));
    }
}
