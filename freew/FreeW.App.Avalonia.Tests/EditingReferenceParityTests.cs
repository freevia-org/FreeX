using System.Globalization;
using System.Threading;
using Avalonia.Headless;
using Free.Shared.Ribbon;
using FreeW.App.Avalonia.Editing;
using FreeW.App.Avalonia.Ribbon;
using FreeW.App.Presentation.Dialogs;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Tests;

public sealed class EditingReferenceParityTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    [Fact]
    public void Notes_EditDeleteOptionsAndNavigation_AreUndoableAndCyclic()
    {
        var first = new Paragraph("A");
        first.Runs.Add(Run.FootnoteReference(1));
        var second = new Paragraph("B");
        second.Runs.Add(Run.FootnoteReference(2));
        var view = ViewWith(first, second);
        view.Document.Footnotes[1] = new Footnote(1, "original");
        view.Document.Footnotes[2] = new Footnote(2, "second");

        view.ReplaceNoteContent(1, footnote: true, [new Paragraph("updated"), new Paragraph("more")]);
        view.Document.Footnotes[1].PlainText.Should().Be("updated\nmore");
        view.Undo();
        view.Document.Footnotes[1].PlainText.Should().Be("original");

        view.DeleteNote(1, footnote: true);
        view.Document.Footnotes.Should().NotContainKey(1);
        ((Paragraph)view.Document.Blocks[0]).Runs.Should().NotContain(run => run.FootnoteId == 1);
        view.Undo();
        view.Document.Footnotes.Should().ContainKey(1);
        ((Paragraph)view.Document.Blocks[0]).Runs.Should().Contain(run => run.FootnoteId == 1);

        var options = new FootnoteEndnoteOptionsDialogResult(
            NoteNumberFormat.UpperRoman, 4, NoteNumberRestart.EachPage,
            NoteNumberFormat.LowerLetter, 9, NoteNumberRestart.EachSection);
        view.ApplyFootnoteEndnoteOptions(options);
        (view.Document.FootnoteNumbering.NumberFormat, view.Document.FootnoteNumbering.StartAt, view.Document.FootnoteNumbering.NumberRestart)
            .Should().Be((NoteNumberFormat.UpperRoman, 4, NoteNumberRestart.EachPage));
        (view.Document.EndnoteNumbering.NumberFormat, view.Document.EndnoteNumbering.StartAt, view.Document.EndnoteNumbering.NumberRestart)
            .Should().Be((NoteNumberFormat.LowerLetter, 9, NoteNumberRestart.EachSection));
        view.Undo();
        view.Document.FootnoteNumbering.StartAt.Should().Be(1);
        view.Document.EndnoteNumbering.StartAt.Should().Be(1);

        view.SetSelectionRangePublic(0, 0, 0, 0);
        view.MoveToNextFootnote().Should().BeTrue();
        (view.CaretBlockForTest, view.CaretOffsetForTest).Should().Be((0, 1));
        view.MoveToNextFootnote().Should().BeTrue();
        (view.CaretBlockForTest, view.CaretOffsetForTest).Should().Be((1, 1));
        view.MoveToNextFootnote().Should().BeTrue();
        (view.CaretBlockForTest, view.CaretOffsetForTest).Should().Be((0, 1));
        view.MoveToPreviousFootnote().Should().BeTrue();
        (view.CaretBlockForTest, view.CaretOffsetForTest).Should().Be((1, 1));
    }

    [Fact]
    public void TextToTable_ConvertsAllSelectedParagraphs_PreservesRaggedRows_AndUndoesOnce()
    {
        var view = ViewWith(new Paragraph("A;B;C"), new Paragraph("D;E"), new Paragraph("F"));
        view.SetSelectionRangePublic(0, 0, 2, 1);

        view.ConvertSelectedParagraphsToTable(';');

        var table = view.Document.Blocks.Should().ContainSingle().Which.Should().BeOfType<Table>().Subject;
        table.Rows.Should().HaveCount(3);
        table.Rows[0].Cells.Select(cell => cell.PlainText).Should().Equal("A", "B", "C");
        table.Rows[1].Cells.Select(cell => cell.PlainText).Should().Equal("D", "E", "");
        table.Rows[2].Cells.Select(cell => cell.PlainText).Should().Equal("F", "", "");
        (view.CaretBlockForTest, view.CaretOffsetForTest).Should().Be((0, 0));

        view.Undo();
        view.Document.Blocks.OfType<Paragraph>().Select(paragraph => paragraph.PlainText)
            .Should().Equal("A;B;C", "D;E", "F");
    }

    [Fact]
    public void TextToTable_MixedSpanMatchesWpfAndReplacesInterleavedBlocksInOneUndoStep()
    {
        var interleavedTable = Table.Create(1, 1);
        interleavedTable.Rows[0].Cells[0] = new TableCell("old table");
        var view = ViewWith(new Paragraph("A;B"), interleavedTable, new Paragraph("C"));
        view.SetSelectionRangePublic(0, 0, 2, 1);

        view.ConvertSelectedParagraphsToTable(';');

        var converted = view.Document.Blocks.Should().ContainSingle().Which.Should().BeOfType<Table>().Subject;
        converted.Rows.Should().HaveCount(2);
        converted.Rows[0].Cells.Select(cell => cell.PlainText).Should().Equal("A", "B");
        converted.Rows[1].Cells.Select(cell => cell.PlainText).Should().Equal("C", "");

        view.Undo();
        view.Document.Blocks.Should().HaveCount(3);
        view.Document.Blocks[0].Should().BeOfType<Paragraph>().Which.PlainText.Should().Be("A;B");
        view.Document.Blocks[1].Should().BeSameAs(interleavedTable);
        view.Document.Blocks[2].Should().BeOfType<Paragraph>().Which.PlainText.Should().Be("C");
    }

    [Fact]
    public void BookmarkManagerDelete_IsUndoable()
    {
        var paragraph = new Paragraph("target");
        paragraph.BookmarkNames.Add("Here");
        var view = ViewWith(paragraph);

        view.DeleteBookmark("Here");
        view.BookmarkNames().Should().BeEmpty();
        view.Undo();
        view.BookmarkNames().Should().Equal("Here");
    }

    [Fact]
    public void MultilevelDefinition_AppliesStartsFormatsAndUndoesAsOneStep()
    {
        var first = new Paragraph("One") { Formatting = ParagraphFormatting.Default with { ListLevel = 0 } };
        var second = new Paragraph("Two") { Formatting = ParagraphFormatting.Default with { ListLevel = 1 } };
        var view = ViewWith(first, second);
        view.SetSelectionRangePublic(0, 0, 1, 3);
        var formats = MultiLevelListFormat.DecimalNumberFormats.ToArray();
        formats[0] = ListNumberFormat.UpperRoman;
        formats[1] = ListNumberFormat.LowerLetter;

        view.ApplyMultiLevelListDefinition(new MultilevelListDefinition(3, 4, 7, formats));

        first.Formatting.ListKind.Should().Be(ListKind.MultiLevel);
        first.Formatting.ListStartOverride.Should().Be(4);
        second.Formatting.ListStartOverride.Should().Be(7);
        view.Document.MultiLevelList.NumberFormats.Take(2).Should().Equal(ListNumberFormat.UpperRoman, ListNumberFormat.LowerLetter);
        view.Undo();
        first.Formatting.ListKind.Should().Be(ListKind.None);
        second.Formatting.ListKind.Should().Be(ListKind.None);
        view.Document.MultiLevelList.NumberFormats[0].Should().Be(ListNumberFormat.Decimal);
    }

    [Fact]
    public void DialogBackedCommands_CancelCallbacksLeaveDocumentUnchanged()
    {
        var view = ViewWith(new Paragraph("Body"));
        var callbacks = NoopCallbacks() with
        {
            OpenDateTimeDialog = () => { },
            OpenTextToTableDialog = () => { },
            OpenFootnoteDialog = () => { },
            OpenEndnoteDialog = () => { },
            ShowTableOfAuthoritiesDialog = () => { },
            OpenMarkCitationDialog = () => { },
        };
        var registry = FreeWAvaloniaRibbonCommands.Build(view, callbacks);

        foreach (var id in new[] { "freew.datetime", "freew.text-to-table", "freew.footnote", "freew.endnote", "freew.table-of-authorities", "freew.mark-citation" })
        {
            registry.TryGet(new RibbonCommandId(id), out var command).Should().BeTrue();
            command!.Execute(RibbonCommandContext.Empty);
        }

        view.Document.Blocks.Should().ContainSingle();
        ((Paragraph)view.Document.Blocks[0]).PlainText.Should().Be("Body");
        view.Document.Footnotes.Should().BeEmpty();
        view.Document.Endnotes.Should().BeEmpty();
        view.Document.Citations.Should().BeEmpty();
    }

    [Fact]
    public Task DateTimeDialog_UsesOneCapturedMomentForStaticAndComplexFieldResults() =>
        Session.Dispatch(() =>
        {
            var moment = new DateTime(2026, 7, 20, 16, 35, 12);
            var culture = CultureInfo.GetCultureInfo("en-GB");
            var dialog = new DateTimeDialog(moment, culture);
            var formats = DateTimeFormats.Build(moment, culture);

            dialog.BuildResultForTest(0, false).Should().Be(new DateTimeDialogResult(formats[0].Text, false, null));
            var time = dialog.BuildResultForTest(3, true);
            time.Text.Should().Be(formats[3].Text);
            time.FieldInstruction.Should().StartWith(" TIME ").And.Contain(DateTimeFormats.BuildFieldPicture(3, culture));

            var view = ViewWith(new Paragraph());
            view.InsertComplexField(time.FieldInstruction!, time.Text);
            var run = ((Paragraph)view.Document.Blocks[0]).Runs.Single();
            run.Text.Should().Be(formats[3].Text);
            run.ComplexField!.Instruction.Should().Be(time.FieldInstruction);
        }, CancellationToken.None);

    [Fact]
    public Task NotesAndThesaurusPanes_AreModelessRefreshableAndApplyActions() =>
        Session.Dispatch(async () =>
        {
            var view = ViewWith(new Paragraph("A happy day"));
            view.InsertFootnote("old");
            var notes = new NotesPane(view);
            notes.ShowAndSelect(footnote: true, id: 1);
            notes.ItemCountForTest.Should().Be(1);
            notes.SubEditorForTest.SetSelectionRangePublic(0, 0, 0, 3);
            notes.SubEditorForTest.InsertText("new rich text");
            notes.ApplyForTest();
            view.Document.Footnotes[1].PlainText.Should().Be("new rich text");
            view.Undo();
            view.Document.Footnotes[1].PlainText.Should().Be("old");

            string? copied = null;
            // InsertFootnote above prepends the footnote reference mark ("1") to the paragraph, so the
            // word's offsets are one past where the authored text alone would put them. Derive them
            // from the live text instead of hard-coding pre-footnote positions.
            var paragraphText = ((Paragraph)view.Document.Blocks[0]).PlainText;
            var happyStart = paragraphText.IndexOf("happy", StringComparison.Ordinal);
            happyStart.Should().BeGreaterThanOrEqualTo(0);
            view.SetSelectionRangePublic(0, happyStart, 0, happyStart + "happy".Length);
            var thesaurus = new ThesaurusPane(view, text => { copied = text; return Task.CompletedTask; });
            thesaurus.Toggle();
            thesaurus.HeadingForTest.Should().Be("happy");
            thesaurus.SenseCountForTest.Should().BeGreaterThan(0);
            await thesaurus.CopyForTestAsync("cheerful");
            copied.Should().Be("cheerful");
            thesaurus.ReplaceForTest("cheerful").Should().BeTrue();
            // The paragraph still carries the footnote reference mark inserted above, so its plain text
            // is "1A cheerful day" -- the leading "1" is the note mark, not body text.
            ((Paragraph)view.Document.Blocks[0]).PlainText.Should().Be("1A cheerful day");
            return true;
        }, CancellationToken.None);

    private static DocumentView ViewWith(params Block[] blocks)
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.AddRange(blocks);
        var view = new DocumentView();
        view.LoadDocument(document);
        return view;
    }

    private static FreeWRibbonHostExecutionPorts NoopCallbacks() => new(
        Open: () => { }, Save: () => { }, Cut: () => { }, Copy: () => { }, Paste: () => { },
        Backstage: () => { }, NewDocument: () => { }, ToggleNavigationPane: () => { },
        ToggleReviewingPane: () => { }, ToggleRevealFormatting: () => { }, OpenFindReplaceDialog: () => { },
        SetPrintLayout: () => { }, SetWebLayout: () => { }, SetDraftView: () => { },
        OpenFontDialog: () => { }, OpenParagraphDialog: () => { }, OpenPageSetupDialog: () => { },
        ToggleOrientation: () => { }, ApplyMarginPreset: _ => { }, ApplyPaperSize: _ => { },
        InsertPicture: () => { }, OpenWordCountDialog: () => { }, ApplyZoom: (_, _) => { });
}
