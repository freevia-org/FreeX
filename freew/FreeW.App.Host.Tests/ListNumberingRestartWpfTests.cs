using System.Linq;
using FreeW.App.Host.Editing;
using FreeW.Core.Model;
using Xunit;

namespace FreeW.App.Host.Tests;

/// <summary>
/// R132 remediation coverage for the WPF twin of the two list-numbering defects fixed in the Avalonia
/// <c>DocumentView</c>:
/// <list type="number">
///   <item>a mid-list <c>w:lvlOverride/startOverride</c> restart (<see cref="ParagraphFormatting.ListStartOverride"/>)
///   was honored for MultiLevel lists but ignored for plain Number lists;</item>
///   <item>any non-list paragraph unconditionally restarted numbered-list counters, so a list
///   interrupted by body text (or preserved-numbering chrome) restarted at 1 instead of continuing.</item>
/// </list>
/// The WPF host renders Number-kind lists via a native <see cref="System.Windows.Documents.List"/> with
/// <c>MarkerStyle=Decimal</c> (auto-numbered by WPF from <c>List.StartIndex</c>) and MultiLevel-kind lists
/// via a manually computed/prepended accumulated marker (<c>MultiLevelListMarkerState</c>), so these
/// assertions read <see cref="DocumentView.Document"/> directly rather than round-tripping through the
/// model (which does not carry rendered marker/StartIndex values).
/// </summary>
public sealed class ListNumberingRestartWpfTests
{
    private static DocumentView ViewWith(TextDocument doc)
    {
        var view = new DocumentView();
        view.LoadModel(doc);
        return view;
    }

    private static TextDocument DocOf(params (string Text, ListKind Kind, int? StartOverride)[] items)
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        foreach (var (text, kind, startOverride) in items)
        {
            doc.Blocks.Add(new Paragraph(text)
            {
                Formatting = ParagraphFormatting.Default with
                {
                    ListKind = kind,
                    ListStartOverride = startOverride,
                }
            });
        }
        return doc;
    }

    // --- Defect (a): mid-list explicit restart override on a Number list --------------------------

    [StaFact]
    public void NumberList_MidListStartOverride_SplitsIntoNewListStartingAtOverride()
    {
        var doc = DocOf(
            ("One", ListKind.Number, null),
            ("Two", ListKind.Number, null),
            ("Restarted", ListKind.Number, 5));

        var view = ViewWith(doc);

        var lists = view.Document.Blocks.OfType<System.Windows.Documents.List>().ToList();

        // Before the fix the whole run collapsed into ONE WpfList (List.StartIndex always defaulted to
        // 1, and the explicit override on "Restarted" was never read), so it rendered 1, 2, 3. The fix
        // must split the run so "Restarted" begins a new list at the overridden value.
        lists.Should().HaveCount(2, "the explicit restart override must begin a new WPF list");
        lists[0].ListItems.Count.Should().Be(2);
        lists[0].StartIndex.Should().Be(1);
        lists[1].ListItems.Count.Should().Be(1);
        lists[1].StartIndex.Should().Be(5, "ListStartOverride=5 must be honored, mirroring the MultiLevel branch");
    }

    // --- Defect (b): a non-list paragraph must not restart a Number list's counters ----------------

    [StaFact]
    public void NumberList_InterruptedByBodyParagraph_ContinuesNumberingAcrossInterruption()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph("One") { Formatting = ParagraphFormatting.Default with { ListKind = ListKind.Number } });
        doc.Blocks.Add(new Paragraph("Two") { Formatting = ParagraphFormatting.Default with { ListKind = ListKind.Number } });
        doc.Blocks.Add(new Paragraph("Interrupting body text."));
        doc.Blocks.Add(new Paragraph("Three") { Formatting = ParagraphFormatting.Default with { ListKind = ListKind.Number } });
        doc.Blocks.Add(new Paragraph("Four") { Formatting = ParagraphFormatting.Default with { ListKind = ListKind.Number } });

        var view = ViewWith(doc);

        var lists = view.Document.Blocks.OfType<System.Windows.Documents.List>().ToList();

        // Before the fix every new WpfList (each interruption forces a new one, since WPF numbers
        // per-List) defaulted its StartIndex to 1, so the second run rendered 1, 2 instead of 3, 4.
        lists.Should().HaveCount(2);
        lists[0].StartIndex.Should().Be(1);
        lists[1].StartIndex.Should().Be(3,
            "Word continues numbering across an intervening body paragraph instead of restarting at 1");
    }

    // --- Defect (b) also applies to MultiLevel lists in the WPF host (fresh marker state per run) ---

    [StaFact]
    public void MultiLevelList_InterruptedByBodyParagraph_ContinuesAccumulatedMarker()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph("One") { Formatting = ParagraphFormatting.Default with { ListKind = ListKind.MultiLevel, ListLevel = 0 } });
        doc.Blocks.Add(new Paragraph("Interrupting body text."));
        doc.Blocks.Add(new Paragraph("Two") { Formatting = ParagraphFormatting.Default with { ListKind = ListKind.MultiLevel, ListLevel = 0 } });

        var view = ViewWith(doc);

        var paragraphs = view.Document.Blocks
            .SelectMany(block => block is System.Windows.Documents.List list
                ? list.ListItems.SelectMany(item => item.Blocks.OfType<System.Windows.Documents.Paragraph>())
                : System.Linq.Enumerable.Empty<System.Windows.Documents.Paragraph>())
            .ToList();

        // Each collected run previously got a brand-new MultiLevelListMarkerState (all counters zeroed),
        // so the second run's marker restarted at "1." instead of continuing to "2.".
        var secondMarkerRun = paragraphs.Last().Inlines.OfType<System.Windows.Documents.Run>().First();
        secondMarkerRun.Text.Should().Be("2. ",
            "Word continues MultiLevel numbering across an intervening body paragraph instead of restarting at 1.");
    }
}
