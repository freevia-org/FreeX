using System.IO;
using FreeW.App.Presentation.DocumentView;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Tests;

public sealed class DocumentParagraphPaginationPlannerTests
{
    [Fact]
    public void OmittedWidowControl_UsesWordDefaultKeepTogetherPolicy()
    {
        DocumentParagraphPaginationPlanner
            .ShouldKeepParagraphTogether(ParagraphFormatting.Default)
            .Should().BeTrue();
    }

    [Fact]
    public void ExplicitWidowControlOff_DoesNotKeepOrdinaryParagraphTogether()
    {
        var formatting = ParagraphFormatting.Default with
        {
            WidowControl = false,
            WidowControlIsSet = true,
        };

        DocumentParagraphPaginationPlanner
            .ShouldKeepParagraphTogether(formatting)
            .Should().BeFalse();
    }

    [Fact]
    public void ExplicitKeepLinesTogether_OverridesWidowControlOff()
    {
        var formatting = ParagraphFormatting.Default with
        {
            KeepLinesTogether = true,
            WidowControl = false,
            WidowControlIsSet = true,
        };

        DocumentParagraphPaginationPlanner
            .ShouldKeepParagraphTogether(formatting)
            .Should().BeTrue();
    }

    [Fact]
    public void TableCellAndNonTextObjectPaths_DoNotInheritOrdinaryDefault()
    {
        var formatting = ParagraphFormatting.Default;

        DocumentParagraphPaginationPlanner
            .ShouldKeepParagraphTogether(formatting, isTableCell: true)
            .Should().BeFalse();
        DocumentParagraphPaginationPlanner
            .ShouldKeepParagraphTogether(formatting, hasNonTextLayoutObject: true)
            .Should().BeFalse();
    }

    [Fact]
    public void PlatformDocumentViews_DelegateOrdinaryParagraphPaginationToSharedPlanner()
    {
        var hostSource = ReadSource("freew", "FreeW.App.Host", "Editing", "DocumentView.cs");
        var avaloniaSource = ReadSource("freew", "FreeW.App.Avalonia", "Editing", "DocumentView.cs");

        hostSource.Should().Contain("DocumentParagraphPaginationPlanner.ShouldKeepParagraphTogether(");
        avaloniaSource.Should().Contain("DocumentParagraphPaginationPlanner.ShouldKeepParagraphTogether(");
    }

    private static string ReadSource(params string[] relativeParts)
    {
        var parts = new string[relativeParts.Length + 1];
        parts[0] = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        Array.Copy(relativeParts, 0, parts, 1, relativeParts.Length);
        return File.ReadAllText(Path.Combine(parts));
    }
}
