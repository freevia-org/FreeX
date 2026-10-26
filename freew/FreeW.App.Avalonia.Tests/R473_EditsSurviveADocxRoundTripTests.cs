using System.IO;
using Avalonia;
using Avalonia.Headless;
using FluentAssertions;
using FreeW.App.Avalonia.Editing;
using FreeW.Core.IO;
using FreeW.Core.Model;
using Xunit;

namespace FreeW.App.Avalonia.Tests;

/// <summary>
/// r473: things inserted through the editor must still be there after a save and reload.
///
/// <para>This extends the family behind r461 (a pasted inline picture was dropped on save). A
/// path-level census over every zero-argument mutator - edit, write .docx, read it back, diff the
/// model by property path with an unedited control subtracted as normalisation noise - produced
/// twelve suspects. Verifying them individually left ZERO defects: the horizontal rule's border
/// survives, the table's border is added rather than lost, and an empty header is dropped exactly
/// as Word drops one. That ratio is the same one r419 measured, and is why nothing here was
/// reported before each case was checked by hand.</para>
///
/// <para>What remains is these two pins, on the cases that were genuinely at risk and are now known
/// good. The census itself is not kept as a gate: with 90 noise paths on an untouched document it
/// needs shape-based filtering that would make it fragile rather than protective.</para>
/// </summary>
public class R473_EditsSurviveADocxRoundTripTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    private static Task<bool> OnUiThread(Action action) => HeadlessUiThread.Run(action);

    private static DocumentView BuildView()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph("Hello"));
        var view = new DocumentView();
        view.LoadDocument(doc);
        view.Measure(new Size(800, 2000));
        return view;
    }

    private static TextDocument RoundTrip(TextDocument doc)
    {
        using var ms = new MemoryStream();
        DocxWriter.Write(doc, ms);
        ms.Position = 0;
        return DocxReader.Read(ms);
    }

    [Fact]
    public async Task AnInsertedTextBoxSurvivesWithItsTextAndIsNotFlattenedIntoTheBody()
    {
        string? editedShapeText = null;
        string? reloadedShapeText = null;
        var reloadedShapeRuns = 0;
        var reloadedBodyRuns = 0;

        var ran = await OnUiThread(() =>
        {
            var view = BuildView();
            view.InsertTextBox();

            editedShapeText = ShapeText(view.Document);

            var reloaded = RoundTrip(view.Document);
            reloadedShapeText = ShapeText(reloaded);

            var runs = reloaded.Blocks.OfType<Paragraph>().SelectMany(p => p.Runs).ToList();
            reloadedShapeRuns = runs.Count(r => r.Shape is not null);
            reloadedBodyRuns = runs.Count(r => r.Shape is null);
        });

        ran.Should().BeTrue();
        editedShapeText.Should().Be("Text Box", "the insert must put text in the shape");
        reloadedShapeText.Should().Be("Text Box", "and the save must keep it there");
        reloadedShapeRuns.Should().Be(1, "the text box must come back as a shape, not be flattened");
        reloadedBodyRuns.Should().Be(1, "and its text must not also appear as a second body run");
    }

    [Fact]
    public async Task AHeaderWithTextSurvivesTheRoundTrip()
    {
        string? edited = null;
        string? reloaded = null;
        var headerPresent = false;

        var ran = await OnUiThread(() =>
        {
            var view = BuildView();
            view.EnsureHeader();

            var header = view.Document.Sections[0].HeadersFooters.Header;
            header!.Paragraphs[0].Runs.Add(new Run("Chapter One"));
            edited = header.Paragraphs[0].PlainText;

            var reloadedHeader = RoundTrip(view.Document).Sections[0].HeadersFooters.Header;
            headerPresent = reloadedHeader is not null;
            reloaded = reloadedHeader?.Paragraphs.FirstOrDefault()?.PlainText;
        });

        ran.Should().BeTrue();
        edited.Should().Be("Chapter One");
        headerPresent.Should().BeTrue("a header carrying text must be written; only an EMPTY one is dropped, as Word does");
        reloaded.Should().Be("Chapter One");
    }

    private static string? ShapeText(TextDocument doc) =>
        doc.Blocks.OfType<Paragraph>()
            .SelectMany(p => p.Runs)
            .Where(r => r.Shape is not null)
            .Select(r => string.Join("/", r.Shape!.TextParagraphs.Select(tp => tp.PlainText)))
            .FirstOrDefault();
}
