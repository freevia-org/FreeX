using System;
using System.IO;
using System.Linq;

using FluentAssertions;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// R68-async-ordering-race-sweep-1: <c>MainWindow.PasteClipboardTextAsync</c> and
/// <c>TryPasteClipboardImageAsync</c> used to capture the paste-destination cell (for the "Pasted at
/// X" / "Pasted picture at X" status message) BEFORE further clipboard <c>await</c>s -- the OS
/// clipboard read (or the bitmap read) can take real wall-clock time, and if the user clicks a
/// different cell while it is pending, the data still lands at the (now different) live active cell
/// but the message named the stale, pre-await cell. <c>Avalonia.Input.Platform.IClipboard</c> is
/// <c>[NotClientImplementable]</c> (no test double can be built for it, see
/// <see cref="R66_ClipboardHtmlReadPasteTests"/>'s own doc comment), and the buggy vs. fixed code
/// only differ when a real OS-clipboard read genuinely suspends (an already-completed Task never
/// yields, so nothing else could run in between in a synchronous headless test) -- so this asserts
/// the fix at the source-ordering level established elsewhere in this test project for exactly this
/// class of unfakeable-dependency, ordering-sensitive bug (see e.g.
/// <see cref="AvaloniaMainWindowChromeSourceTests"/>'s <c>File.ReadAllText</c> + ordering assertions).
/// </summary>
public sealed class R68_PasteDestinationCaptureOrderTests
{
    private const string PasteClipboardTextAsyncSignature = "private async Task PasteClipboardTextAsync()";
    private const string TryPasteClipboardImageAsyncSignature = "private async Task<bool> TryPasteClipboardImageAsync()";

    [Fact]
    public void PasteClipboardTextAsync_CapturesDestination_AfterTheHtmlAwait_NotBeforeIt()
    {
        var body = ExtractMethodBody(PasteClipboardTextAsyncSignature);

        var htmlAwaitIndex = body.IndexOf("await TryGetClipboardHtmlAsync()", StringComparison.Ordinal);
        var destinationCaptureIndex = body.IndexOf("var destination = _session.ActiveCell;", StringComparison.Ordinal);
        var pasteCallIndex = body.IndexOf("_session.PasteClipboardTextAtActiveCell(", StringComparison.Ordinal);

        htmlAwaitIndex.Should().BeGreaterThanOrEqualTo(0, "the html read must still happen");
        destinationCaptureIndex.Should().BeGreaterThanOrEqualTo(0, "the destination must still be captured for the status message");
        pasteCallIndex.Should().BeGreaterThanOrEqualTo(0);

        destinationCaptureIndex.Should().BeGreaterThan(htmlAwaitIndex,
            "destination must be captured AFTER the last clipboard await (the html read), as the last synchronous " +
            "step before use -- capturing it earlier (before the html await) is exactly the R68 bug: a click during " +
            "that await lands the paste at a different cell than the one the message names");
        destinationCaptureIndex.Should().BeLessThan(pasteCallIndex,
            "destination must be captured immediately before the synchronous session paste call, not after it");
    }

    [Fact]
    public void PasteClipboardTextAsync_NoRegression_StillPastesAndReportsACell()
    {
        // Sibling no-regression check: the destination capture and its use in the status message
        // must both still be present (the fix only moves capture later, it doesn't remove either).
        var body = ExtractMethodBody(PasteClipboardTextAsyncSignature);

        body.Should().Contain("var destination = _session.ActiveCell;");
        body.Should().Contain("UiText.Format(\"MainLoc_PastedAt\", FormatCellReference(destination))");
    }

    [Fact]
    public void TryPasteClipboardImageAsync_CapturesDestination_AfterTheBitmapAwait_NotBeforeIt()
    {
        // ExtractMethodBody itself only matches the NEW parameterless signature -- the OLD signature
        // accepted a clipboard and destination, so this whole
        // test fails at signature-lookup before the fix, proving the destination PARAMETER (the
        // caller used to capture it before awaiting the bitmap read) was removed.
        var body = ExtractMethodBody(TryPasteClipboardImageAsyncSignature);

        var bitmapAwaitIndex = body.IndexOf("await _platformClipboard.ReadImageAsync()", StringComparison.Ordinal);
        var destinationCaptureIndex = body.IndexOf("var destination = _session.ActiveCell;", StringComparison.Ordinal);
        var pasteCallIndex = body.IndexOf("_session.PasteClipboardImageAtActiveCell(", StringComparison.Ordinal);

        bitmapAwaitIndex.Should().BeGreaterThanOrEqualTo(0);
        destinationCaptureIndex.Should().BeGreaterThanOrEqualTo(0);
        pasteCallIndex.Should().BeGreaterThanOrEqualTo(0);

        destinationCaptureIndex.Should().BeGreaterThan(bitmapAwaitIndex,
            "destination must be captured AFTER the bitmap read await, not before it");
        destinationCaptureIndex.Should().BeLessThan(pasteCallIndex);
    }

    [Fact]
    public void TryPasteClipboardImageAsync_NoRegression_StillReportsThePastedPictureCell()
    {
        var body = ExtractMethodBody(TryPasteClipboardImageAsyncSignature);

        body.Should().Contain("UiText.Format(\"MainLoc_PastedPictureAt\", FormatCellReference(destination))");

        // Both call sites must compile against the new (destination-less) signature.
        var source = MainWindowSource();
        source.Should().Contain("await TryPasteClipboardImageAsync())");
        source.Should().Contain("return await TryPasteClipboardImageAsync();");
    }

    private static string ExtractMethodBody(string signature)
    {
        var source = MainWindowSource();

        var startIndex = source.IndexOf(signature, StringComparison.Ordinal);
        startIndex.Should().BeGreaterThanOrEqualTo(0, $"MainWindow.cs should declare a method with the exact signature '{signature}'");

        var braceOpenIndex = source.IndexOf('{', startIndex);
        braceOpenIndex.Should().BeGreaterThan(startIndex);

        var depth = 0;
        var index = braceOpenIndex;
        for (; index < source.Length; index++)
        {
            if (source[index] == '{')
                depth++;
            else if (source[index] == '}')
            {
                depth--;
                if (depth == 0)
                    break;
            }
        }

        index.Should().BeLessThan(source.Length, "the method's closing brace should be found");
        return source[braceOpenIndex..(index + 1)];
    }

    private static string MainWindowSource() => File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.cs"));

    private static string RepoFile(params string[] parts) =>
        Path.Combine([TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx"), .. parts]);
}
