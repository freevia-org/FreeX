using System.IO;

namespace FreeW.Core.IO.Tests;

public sealed class StoryTraversalAndHighlightOwnershipTests
{
    [Fact]
    public void StoryConsumersUseModelOwnedTraversalProfiles()
    {
        var bindingResolver = ReadSource("freew", "FreeW.Core.IO", "CustomXmlDataBindingResolver.cs");
        var imageResolver = ReadSource("freew", "FreeW.Core.IO", "LinkedImagePreviewResolver.cs");
        var writer = ReadSource("freew", "FreeW.Core.IO", "DocxWriter.cs");

        bindingResolver.Should().Contain("TextDocumentStoryTraversal.EnumerateParagraphs(document, EnumerateComments(document))");
        imageResolver.Should().Contain("TextDocumentStoryTraversal.EnumerateParagraphs(");
        writer.Should().Contain("TextDocumentStoryTraversalOptions.IncludeTextBoxes");
        writer.Should().Contain("TextDocumentStoryTraversalOptions.PreserveDuplicateParagraphs");
        bindingResolver.Should().NotContain("new HashSet<Paragraph>");
        imageResolver.Should().NotContain("new HashSet<Paragraph>");
    }

    [Fact]
    public void DocxReaderAndWriterUseTheBidirectionalHighlightCodec()
    {
        var reader = ReadSource("freew", "FreeW.Core.IO", "DocxReader.cs");
        var writer = ReadSource("freew", "FreeW.Core.IO", "DocxWriter.cs");

        reader.Should().Contain("WordHighlightColorCodec.ToHex(highlightNamedToken)");
        writer.Should().Contain("WordHighlightColorCodec.ToToken(highlightToken)");
        reader.Should().NotContain("HighlightTokenToHex(");
        writer.Should().NotContain("HexToHighlightToken(");
    }

    private static string ReadSource(params string[] relativePath)
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        return File.ReadAllText(relativePath.Aggregate(root, Path.Combine));
    }
}
