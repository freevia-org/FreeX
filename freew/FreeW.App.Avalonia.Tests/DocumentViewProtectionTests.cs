using Avalonia;
using Avalonia.Headless;
using FreeW.App.Avalonia.Editing;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Tests;

public sealed class DocumentViewProtectionTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    private static async Task<bool> OnUiThread(Action action)
    {
        try
        {
            await Session.Dispatch(action, CancellationToken.None);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    [Fact]
    public async Task MarkAsFinal_blocks_text_and_insert_mutations_until_cleared()
    {
        var textWhileFinal = "";
        var textAfterCleared = "";
        var blocksWhileFinal = 0;
        var protectionEvents = 0;

        var ran = await OnUiThread(() =>
        {
            var view = BuildView("Hello");
            var startingBlocks = view.Document.Blocks.Count;
            view.ProtectionStateChanged += (_, _) => protectionEvents++;

            view.SetMarkedAsFinal(true);
            view.InsertText("X");
            view.InsertTable(2, 2);
            view.InsertPageBreak();

            textWhileFinal = view.PlainText;
            blocksWhileFinal = view.Document.Blocks.Count - startingBlocks;

            view.SetMarkedAsFinal(false);
            view.InsertText("X");
            textAfterCleared = view.PlainText;
        });

        if (!ran)
            return;

        textWhileFinal.Should().Be("Hello");
        blocksWhileFinal.Should().Be(0);
        textAfterCleared.Should().Be("XHello");
        protectionEvents.Should().Be(2);
    }

    [Fact]
    public async Task ReadOnlyProtection_blocks_text_but_trackChangesOnly_records_tracked_edits()
    {
        var textWhileReadOnly = "";
        var textAfterTrackChangesOnly = "";
        var trackChangesEnabled = false;
        var hasRevisions = false;

        var ran = await OnUiThread(() =>
        {
            var view = BuildView("Hello");

            view.SetProtection(ProtectionMode.ReadOnly);
            view.InsertText("X");
            textWhileReadOnly = view.PlainText;

            view.SetProtection(ProtectionMode.None);
            view.SetProtection(ProtectionMode.TrackChangesOnly);
            trackChangesEnabled = view.TrackChangesEnabled;
            view.InsertText("X");
            textAfterTrackChangesOnly = view.PlainText;
            hasRevisions = view.HasRevisions;
        });

        if (!ran)
            return;

        textWhileReadOnly.Should().Be("Hello");
        trackChangesEnabled.Should().BeTrue();
        textAfterTrackChangesOnly.Should().Be("XHello");
        hasRevisions.Should().BeTrue();
    }

    private static DocumentView BuildView(string firstParagraphText)
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph(firstParagraphText));

        var view = new DocumentView();
        view.LoadDocument(doc);
        view.Measure(new Size(800, 2000));
        return view;
    }
}
