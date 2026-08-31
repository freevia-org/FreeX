using System.IO;

using FluentAssertions;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Round 178. Format Cells > Font fills its size box with an InvariantCulture rendering
/// (currentFontSize.ToString("0.##", CultureInfo.InvariantCulture), so "10.5"), but the live
/// preview parsed that same text with CultureInfo.CurrentCulture only. On a comma-decimal locale
/// the preview could not read the value it had just written: it silently kept the previous size,
/// so the preview disagreed with the size the dialog would actually apply on OK.
///
/// FormatCellsInputParser.TryParseFontSize -- which the commit path already goes through -- tries
/// the current culture and falls back to invariant, which is exactly what this box needs. Pinning
/// that the preview shares that parser rather than re-deriving the rule inline: the bug was the
/// divergence between the two, so what matters is that there is only one parser.
/// </summary>
public sealed class Round178_FontSizePreviewUsesSharedParserTests
{
    [Fact]
    public void TheFontSizeLivePreviewParsesThroughTheSharedInputParser()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.cs"));

        source.Should().Contain("FormatCellsInputParser.TryParseFontSize(fontSizeBox.Text",
            "the live preview must use the same parser as the commit path");
        source.Should().NotContain(
            "double.TryParse(fontSizeBox.Text?.Trim(), NumberStyles.Float, CultureInfo.CurrentCulture",
            "a CurrentCulture-only parse cannot read the InvariantCulture text this box is filled with");
    }

    private static string RepoFile(params string[] parts) =>
        Path.Combine([TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx"), .. parts]);
}
