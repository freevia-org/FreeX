using System.IO;
using FreeW.App.Presentation.Dialogs;
using FreeW.App.Presentation.DocumentView;
using FreeW.App.Presentation.Editing;
using FreeW.App.Presentation.Ribbon;

namespace FreeW.App.Presentation.Tests.Editing;

public sealed class DocumentTableEditingCoordinatorTests
{
    [Fact]
    public void SetCellTextPreservesFirstRunFormattingAndUsesPortableUndo()
    {
        var table = Table.Create(1, 1);
        table.Rows[0].Cells[0].Paragraphs[0].Runs.Add(new Run("original")
        {
            Formatting = RunFormatting.Default with { Italic = true, ColorHex = "#4472C4" },
        });
        var document = new TextDocument { Blocks = { table } };
        var session = new DocumentEditingSession();
        session.LoadDocument(document);
        var address = session.Tables.AddressFromCellIndex(0, 0, 0)!.Value;

        session.Tables.SetCellText(address, "updated").Applied.Should().BeTrue();

        var run = table.Rows[0].Cells[0].Paragraphs.Single().Runs.Single();
        run.Text.Should().Be("updated");
        run.Formatting.Italic.Should().BeTrue();
        run.Formatting.ColorHex.Should().Be("#4472C4");
        session.Commands.Undo().Should().BeTrue();
        table.Rows[0].Cells[0].PlainText.Should().Be("original");
        session.Commands.Redo().Should().BeTrue();
        table.Rows[0].Cells[0].PlainText.Should().Be("updated");

        session.Tables.SetCellText(
                new DocumentTableCellAddress(0, 9, 9),
                "ignored")
            .Applied.Should().BeFalse();
        table.Rows[0].Cells[0].PlainText.Should().Be("updated");
    }

    [Fact]
    public void AddressesNormalizeCellAndGridCoordinatesAcrossMergedCells()
    {
        var table = Table.Create(1, 3);
        table.Rows[0].Cells[0].GridSpan = 2;
        table.Rows[0].Cells.RemoveAt(1);
        var session = SessionWith(table);

        session.Tables.AddressFromCellIndex(0, 0, 1)
            .Should().Be(new DocumentTableCellAddress(0, 0, 2));
        session.Tables.AddressFromGridColumn(0, 0, 1)
            .Should().Be(new DocumentTableCellAddress(0, 0, 1));
        session.Tables.AddressFromGridColumn(0, 0, 3).Should().BeNull();
    }

    [Fact]
    public void RowAndColumnStructureEditsReportPortableCaretAndUndo()
    {
        var table = Table.Create(2, 2);
        var session = SessionWith(table);
        var address = new DocumentTableCellAddress(0, 0, 0);

        var rowResult = session.Tables.InsertRow(address, after: true);

        rowResult.Applied.Should().BeTrue();
        rowResult.InvalidatesNativeSelection.Should().BeTrue();
        rowResult.Caret.RowIndex.Should().Be(1);
        table.Rows.Should().HaveCount(3);
        session.Commands.Undo().Should().BeTrue();
        table.Rows.Should().HaveCount(2);

        var columnResult = session.Tables.InsertColumn(address, after: true);

        columnResult.Caret.GridColumn.Should().Be(1);
        table.Rows.Should().OnlyContain(row => row.Cells.Count == 3);
        session.Commands.Undo().Should().BeTrue();
        table.Rows.Should().OnlyContain(row => row.Cells.Count == 2);
    }

    [Fact]
    public void MergeSplitAndEraseUseGridCoordinatesWithSingleUndoEntries()
    {
        var table = Table.Create(1, 3);
        var session = SessionWith(table);

        session.Tables.MergeCells(
                new DocumentTableCellAddress(0, 0, 0),
                new DocumentTableCellAddress(0, 0, 2))
            .Applied.Should().BeTrue();
        table.Rows[0].Cells.Should().ContainSingle();
        table.Rows[0].Cells[0].GridSpan.Should().Be(3);

        session.Tables.SplitCell(new DocumentTableCellAddress(0, 0, 0))
            .Applied.Should().BeTrue();
        table.Rows[0].Cells.Should().HaveCount(3);
        session.Commands.Undo().Should().BeTrue();
        table.Rows[0].Cells.Should().ContainSingle();
        session.Commands.Undo().Should().BeTrue();
        table.Rows[0].Cells.Should().HaveCount(3);

        session.Tables.EraseBorderAt(new DocumentTableCellAddress(0, 0, 0))
            .Applied.Should().BeTrue();
        table.Rows[0].Cells.Should().HaveCount(2);
    }

    [Fact]
    public void MultiCellFormattingIsOneUndoableOperation()
    {
        var table = Table.Create(1, 2);
        var session = SessionWith(table);
        var addresses = new[]
        {
            new DocumentTableCellAddress(0, 0, 0),
            new DocumentTableCellAddress(0, 0, 1),
        };

        session.Tables.SetCellShading(addresses, "#ABCDEF").Applied.Should().BeTrue();

        table.Rows[0].Cells.Should().OnlyContain(cell => cell.ShadingColorHex == "#ABCDEF");
        session.Commands.Undo().Should().BeTrue();
        table.Rows[0].Cells.Should().OnlyContain(cell => cell.ShadingColorHex == null);
        session.Commands.CanUndo.Should().BeFalse();
    }

    [Fact]
    public void MergedCellGridAddressesAreDeduplicatedAndBorderEditsAreGrouped()
    {
        var table = Table.Create(1, 3);
        table.Rows[0].Cells[0].GridSpan = 2;
        table.Rows[0].Cells.RemoveAt(1);
        var session = SessionWith(table);

        session.Tables.SetCellShading(
            [
                new DocumentTableCellAddress(0, 0, 0),
                new DocumentTableCellAddress(0, 0, 1),
            ],
            "#ABCDEF");

        session.Commands.Undo().Should().BeTrue();
        session.Commands.CanUndo.Should().BeFalse();

        session.Tables.SetCellBorderEdges(
            [
                new DocumentTableCellBorderEdit(
                    new DocumentTableCellAddress(0, 0, 0),
                    CellBorderEdges.Top),
                new DocumentTableCellBorderEdit(
                    new DocumentTableCellAddress(0, 0, 2),
                    CellBorderEdges.Bottom),
            ],
            BorderLineStyle.Single,
            "#123456",
            1,
            clearEdges: false);

        table.Rows[0].Cells[0].Borders!.Top!.ColorHex.Should().Be("#123456");
        table.Rows[0].Cells[1].Borders!.Bottom!.ColorHex.Should().Be("#123456");
        session.Commands.Undo().Should().BeTrue();
        table.Rows[0].Cells.Should().OnlyContain(cell => cell.Borders == null);
        session.Commands.CanUndo.Should().BeFalse();
    }

    [Fact]
    public void SortAndConvertTableReturnPortablePostEditTargets()
    {
        var table = Table.Create(2, 1);
        table.Rows[0].Cells[0].Paragraphs[0].Runs.Add(new Run("b"));
        table.Rows[1].Cells[0].Paragraphs[0].Runs.Add(new Run("a"));
        var session = SessionWith(table);
        var address = new DocumentTableCellAddress(0, 0, 0);

        session.Tables.SortRows(
                address,
                SortKind.Text,
                ascending: true,
                caseSensitive: false,
                hasHeaderRow: false)
            .Applied.Should().BeTrue();
        table = (Table)session.Document.Blocks[0];
        table.Rows.Select(row => row.Cells[0].PlainText).Should().Equal("a", "b");

        var result = session.Tables.ConvertToText(address, ',');

        result.Applied.Should().BeTrue();
        result.InvalidatesNativeSelection.Should().BeTrue();
        session.Document.Blocks.Cast<Paragraph>().Select(paragraph => paragraph.PlainText)
            .Should().Equal("a", "b");
    }

    [Fact]
    public void TableStyleAndFormulaConstructionAreCoordinatorOwned()
    {
        var table = Table.Create(1, 1);
        var session = SessionWith(table);
        var address = new DocumentTableCellAddress(0, 0, 0);

        session.Tables.ApplyStyle(address, DocumentTableStyle.Catalog[0]).Applied.Should().BeTrue();
        table.TableStyleId.Should().Be(DocumentTableStyle.Catalog[0].WordStyleId);

        var result = session.Tables.InsertFormula(
            address,
            paragraphIndex: 0,
            textOffset: 0,
            new TableFormulaField("=SUM(ABOVE)"));

        result.Applied.Should().BeTrue();
        result.TextOffset.Should().BeGreaterThan(0);
        table.Rows[0].Cells[0].Paragraphs[0].Runs.Should()
            .Contain(run => run.TableFormula != null);
    }

    private static DocumentEditingSession SessionWith(Block block)
    {
        var document = new TextDocument();
        document.Blocks.Add(block);
        var session = new DocumentEditingSession();
        session.LoadDocument(document);
        return session;
    }
}

public sealed class DocumentEditingSessionWorkflowTests
{
    [Fact]
    public void ParagraphFormattingAndStylesAreGroupedAcrossTargets()
    {
        var document = DocumentWith("one", "two");
        var session = new DocumentEditingSession();
        session.LoadDocument(document);

        session.FormatParagraphs(
                [0, 1],
                formatting => formatting with { KeepWithNext = true })
            .Should().BeTrue();
        document.Blocks.Cast<Paragraph>()
            .Should().OnlyContain(paragraph => paragraph.Formatting.KeepWithNext);
        session.Commands.Undo().Should().BeTrue();
        session.Commands.CanUndo.Should().BeFalse();

        session.SetParagraphStyles([0, 1], "Heading1").Should().BeTrue();
        document.Blocks.Cast<Paragraph>().Should().OnlyContain(paragraph => paragraph.StyleId == "Heading1");
        session.Commands.Undo().Should().BeTrue();
        session.Commands.CanUndo.Should().BeFalse();
    }

    [Fact]
    public void SortParagraphSpanPreservesInterleavedTableSlots()
    {
        var document = DocumentWith("b", "a");
        var table = Table.Create(1, 1);
        document.Blocks.Insert(1, table);
        var session = new DocumentEditingSession();
        session.LoadDocument(document);

        session.SortParagraphSpan(
                0,
                2,
                SortKind.Text,
                ascending: true,
                caseSensitive: false,
                hasHeaderRow: false)
            .Should().BeTrue();

        ((Paragraph)document.Blocks[0]).PlainText.Should().Be("a");
        document.Blocks[1].Should().BeSameAs(table);
        ((Paragraph)document.Blocks[2]).PlainText.Should().Be("b");
        session.Commands.Undo().Should().BeTrue();
        ((Paragraph)document.Blocks[0]).PlainText.Should().Be("b");
    }

    [Fact]
    public void ParagraphConversionAndSourcePasteAreSessionOwned()
    {
        var document = DocumentWith("a,b", "c,d");
        var session = new DocumentEditingSession();
        session.LoadDocument(document);

        session.ConvertParagraphsToTable([0, 1], ',', showBorders: true).Should().Be(0);
        document.Blocks.Should().ContainSingle().Which.Should().BeOfType<Table>();
        ((Table)document.Blocks[0]).Formatting.Borders.Should().BeTrue();
        session.Commands.Undo().Should().BeTrue();

        var target = DocumentWith(string.Empty);
        var source = DocumentWith("source one", "source two");
        session.LoadDocument(target);
        session.ReplaceEmptyParagraphWithDocument(0, source).Should().BeTrue();
        target.Blocks.Cast<Paragraph>().Select(paragraph => paragraph.PlainText)
            .Should().Equal("source one", "source two");
    }

    [Fact]
    public void StyleCatalogCreationAppliesTargetsAndUndoesAtomically()
    {
        var document = DocumentWith("one", "two");
        var session = new DocumentEditingSession();
        session.LoadDocument(document);

        var created = session.CreateParagraphStyleAndApply(
            [0, 1],
            "Custom",
            "Normal",
            RunFormatting.Default with { Bold = true },
            ParagraphFormatting.Default,
            "Normal");

        created.Should().NotBeNull();
        document.Blocks.Cast<Paragraph>().Should().OnlyContain(paragraph => paragraph.StyleId == created!.Id);
        session.Commands.Undo().Should().BeTrue();
        document.Blocks.Cast<Paragraph>().Should().OnlyContain(paragraph => paragraph.StyleId == null);
    }

    [Fact]
    public void CharacterFormattingHyphenationAndOutlineMovesAreSessionOwned()
    {
        var document = DocumentWith("Heading", "body", "Next");
        ((Paragraph)document.Blocks[0]).StyleId = "Heading1";
        ((Paragraph)document.Blocks[2]).StyleId = "Heading1";
        var session = new DocumentEditingSession();
        session.LoadDocument(document);

        session.FormatParagraphRuns([0, 1], formatting => formatting with { Bold = true });
        document.Blocks.OfType<Paragraph>().Take(2)
            .Should().OnlyContain(paragraph => paragraph.Runs.All(run => run.Formatting.Bold));
        session.Commands.Undo().Should().BeTrue();
        session.Commands.CanUndo.Should().BeFalse();

        var bodyRun = ((Paragraph)document.Blocks[1]).Runs[0];
        session.ApplyManualHyphenation([new ManualHyphenationEdit(bodyRun, 2)]).Should().BeTrue();
        bodyRun.Text.Should().Contain(Hyphenator.SoftHyphen.ToString());
        session.Commands.Undo().Should().BeTrue();

        session.MoveHeadingSubtree(2, moveUp: true).Should().Be(0);
        ((Paragraph)document.Blocks[0]).PlainText.Should().Be("Next");

        session.ApplyDropCap(
            0,
            DropCapPosition.Dropped,
            DropCap.DefaultSizePt,
            DropCap.DefaultLineSpan,
            DropCap.DefaultDistanceFromTextPt).Should().BeTrue();
        ((Paragraph)document.Blocks[0]).DropCap.Should().NotBeNull();
        session.ClearDropCap(0).Should().BeTrue();
        ((Paragraph)document.Blocks[0]).Runs.Should()
            .OnlyContain(run => run.Formatting == RunFormatting.Default);
    }

    private static TextDocument DocumentWith(params string[] paragraphs)
    {
        var document = new TextDocument();
        foreach (var text in paragraphs)
            document.Blocks.Add(new Paragraph(text));
        return document;
    }
}

public sealed class DocumentReferenceEditingCoordinatorTests
{
    [Fact]
    public void NoteNumberingOptionsUseOnePortableUndoableCommand()
    {
        var document = TextDocument.CreateEmpty();
        var session = new DocumentEditingSession();
        session.LoadDocument(document);

        session.References.ApplyNoteNumberingOptions(new FootnoteEndnoteOptionsDialogResult(
            NoteNumberFormat.UpperRoman,
            4,
            NoteNumberRestart.EachPage,
            NoteNumberFormat.LowerLetter,
            9,
            NoteNumberRestart.EachSection));

        document.FootnoteNumbering.StartAt.Should().Be(4);
        document.EndnoteNumbering.StartAt.Should().Be(9);
        session.Commands.Undo().Should().BeTrue();
        document.FootnoteNumbering.StartAt.Should().Be(1);
        document.EndnoteNumbering.StartAt.Should().Be(1);
    }

    [Fact]
    public void FieldCodeToggleUsesOnePortableDocumentWideMajorityDecision()
    {
        var hiddenOne = Run.ComplexFieldRun(" PAGE ", "1");
        var shown = Run.ComplexFieldRun(" AUTHOR ", "Ada", showCode: true);
        var hiddenTwo = Run.ComplexFieldRun(" TITLE ", "Notes");
        var document = new TextDocument();
        document.Blocks.Add(new Paragraph { Runs = { hiddenOne, shown, hiddenTwo } });
        var session = new DocumentEditingSession();
        session.LoadDocument(document);

        var showResult = session.References.ToggleFieldCodes();

        showResult.Should().Be(new DocumentFieldCodeToggleResult(true, true, 3));
        document.Blocks.OfType<Paragraph>()
            .SelectMany(paragraph => paragraph.Runs)
            .Should().OnlyContain(run => run.ComplexField!.ShowCode);

        var hideResult = session.References.ToggleFieldCodes();

        hideResult.Should().Be(new DocumentFieldCodeToggleResult(true, false, 3));
        document.Blocks.OfType<Paragraph>()
            .SelectMany(paragraph => paragraph.Runs)
            .Should().OnlyContain(run => !run.ComplexField!.ShowCode);
    }

    [Fact]
    public void SelectedComplexFieldTransitionsAreCoordinatorOwned()
    {
        var title = Run.ComplexFieldRun(" TITLE ", "stale title");
        var author = Run.ComplexFieldRun(" AUTHOR ", "stale author");
        var document = new TextDocument();
        document.Properties.Title = "Current title";
        document.Properties.Author = "Ada Lovelace";
        document.Blocks.Add(new Paragraph { Runs = { title, author } });
        var session = new DocumentEditingSession();
        session.LoadDocument(document);

        var toggled = session.References.ToggleComplexFieldCodes([title.ComplexField!]);

        toggled.Should().Be(new DocumentComplexFieldEditResult(true, 1, 1));
        title.ComplexField!.ShowCode.Should().BeTrue();
        author.ComplexField!.ShowCode.Should().BeFalse();

        session.References.SetComplexFieldsLocked([title.ComplexField!], true).Applied
            .Should().BeTrue();
        title.ComplexField!.IsLocked.Should().BeTrue();
        session.References.SetComplexFieldsLocked([title.ComplexField!], false);

        var updated = session.References.UpdateComplexFields(
            [title.ComplexField!],
            evaluatedAt: new DateTime(2026, 8, 10));

        updated.Should().Be(new DocumentComplexFieldEditResult(true, 1, 1));
        title.Text.Should().Be("Current title");
        author.Text.Should().Be("stale author");

        var unlinked = session.References.UnlinkComplexFields(
            [new DocumentComplexFieldTarget(title.ComplexField!, "visible title")]);

        unlinked.Should().Be(new DocumentComplexFieldEditResult(true, 1, 1));
        title.Text.Should().Be("visible title");
        title.ComplexField.Should().BeNull();
        author.ComplexField.Should().NotBeNull();
    }

    [Fact]
    public void FieldUpdateOwnsLiveReferenceLockAndPageResultPolicy()
    {
        var evaluatedAt = new DateTime(2026, 8, 6, 14, 5, 0, DateTimeKind.Local);
        var simpleDate = new Run("old date") { FieldKind = RunFieldKind.Date };
        var complexTime = Run.ComplexFieldRun(" TIME ", "old time");
        var author = new Run("old author") { FieldKind = RunFieldKind.Author };
        var fileName = Run.ComplexFieldRun(" FILENAME ", "old.docx");
        var page = new Run("9") { FieldKind = RunFieldKind.PageNumber };
        var pageCount = Run.ComplexFieldRun(" NUMPAGES ", "9");
        var pageReference = Run.CrossReferenceFieldRun(
            new CrossReferenceField(
                CrossRefFieldKind.PageRef,
                "target",
                CrossRefInsertAs.PageNumber,
                Hyperlink: false),
            "9");
        var importedPageReference = Run.ComplexFieldRun(" PAGEREF target ", "9");
        var styleReference = Run.ComplexFieldRun(" STYLEREF 1 ", "Old heading");
        var lockedStyleReference = new Run("Locked heading")
        {
            ComplexField = new ComplexField(
                " STYLEREF 1 ",
                SimpleField: new SimpleFieldMetadata(IsLocked: true, IsDirty: true))
        };
        var document = new TextDocument();
        document.Page.PageNumberFormat = PageNumberFormat.UpperRoman;
        document.Page.PageNumberStartAt = 4;
        document.Properties.Author = "Ada Lovelace";
        document.Blocks.Add(new Paragraph("Current heading")
        {
            StyleId = "Heading1",
            BookmarkName = "target"
        });
        document.Blocks.Add(new Paragraph
        {
            Runs =
            {
                simpleDate,
                complexTime,
                author,
                fileName,
                page,
                pageCount,
                pageReference,
                importedPageReference,
                styleReference,
                lockedStyleReference
            }
        });
        var session = new DocumentEditingSession();
        session.LoadDocument(document);
        var layoutCalls = 0;

        var result = session.References.UpdateFields(
            blockPageResolutionFactory: () =>
            {
                layoutCalls++;
                return new DocumentReferenceBlockPageResolution(_ => 2, PageCount: 7);
            },
            fileName: "current.docx",
            evaluatedAt: evaluatedAt);

        result.UpdatedFieldCount.Should().Be(9);
        result.RefreshedGeneratedRegionCount.Should().Be(0);
        layoutCalls.Should().Be(1);
        simpleDate.Text.Should().Be("8/6/2026");
        complexTime.Text.Should().Be("2:05 PM");
        author.Text.Should().Be("Ada Lovelace");
        fileName.Text.Should().Be("current.docx");
        page.Text.Should().Be("V");
        pageCount.Text.Should().Be("7");
        pageReference.Text.Should().Be("V");
        importedPageReference.Text.Should().Be("V");
        styleReference.Text.Should().Be("Current heading");
        lockedStyleReference.Text.Should().Be("Locked heading");
        lockedStyleReference.ComplexField!.SimpleField.Should()
            .Be(new SimpleFieldMetadata(IsLocked: true, IsDirty: true));
    }

    [Fact]
    public void FieldUpdateCoversTableRunsAndUsesSourceRunPositionForNoteReferences()
    {
        var noteReference = Run.CrossReferenceFieldRun(
            new CrossReferenceField(
                CrossRefFieldKind.NoteRef,
                "_Ref1",
                CrossRefInsertAs.AboveBelow,
                Hyperlink: true),
            "stale");
        var noteMarker = Run.FootnoteReference(1);
        var sequence = Run.ComplexFieldRun(" SEQ Figure \\h ", "stale");
        var cellParagraph = new Paragraph { Runs = { noteReference, noteMarker, sequence } };
        cellParagraph.BookmarkNames.Add("_Ref1");
        cellParagraph.BookmarkBoundaries.Add(new BookmarkBoundary(
            "auto:_Ref1", BookmarkBoundaryKind.Start, 1, "_Ref1"));
        cellParagraph.BookmarkBoundaries.Add(new BookmarkBoundary(
            "auto:_Ref1", BookmarkBoundaryKind.End, 2));
        var cell = new TableCell();
        cell.Paragraphs.Add(cellParagraph);
        var row = new TableRow();
        row.Cells.Add(cell);
        var table = new Table();
        table.Rows.Add(row);
        var document = new TextDocument();
        document.Blocks.Add(table);
        document.Footnotes[1] = new Footnote(1, "note");
        var session = new DocumentEditingSession();
        session.LoadDocument(document);

        var result = session.References.UpdateFields();

        result.UpdatedFieldCount.Should().Be(2);
        noteReference.Text.Should().Be("1 below");
        sequence.Text.Should().BeEmpty();
    }

    [Fact]
    public void FieldUpdateCanEvaluateSubEditorFieldsAgainstOwningDocument()
    {
        var owner = TextDocument.CreateEmpty();
        owner.Properties.Author = "Owning author";
        var subEditor = TextDocument.CreateEmpty();
        var author = Run.AuthorField("stale");
        ((Paragraph)subEditor.Blocks[0]).Runs.Add(author);
        var session = new DocumentEditingSession();
        session.LoadDocument(subEditor);

        var result = session.References.UpdateFields(evaluationDocument: owner);

        result.UpdatedFieldCount.Should().Be(1);
        author.Text.Should().Be("Owning author");
    }

    [Fact]
    public void CrossReferenceInsertionPreservesPlannedCaptionBookmarkScope()
    {
        var caption = Captions.BuildCaption(CaptionLabel.Figure, 1, "Sample caption text");
        var host = new Paragraph("See ");
        var document = new TextDocument();
        document.Blocks.Add(caption);
        document.Blocks.Add(host);
        var session = new DocumentEditingSession();
        session.LoadDocument(document);
        var target = CrossReferences.Targets(document, CrossRefType.Figure).Single();

        session.References.InsertCrossReference(
            sourceBlockIndex: 1,
            preferredHostBlockIndex: 1,
            CrossRefType.Figure,
            target,
            CrossRefInsertAs.CaptionText,
            hyperlink: true);

        caption.BookmarkBoundaries.Should().Contain(new BookmarkBoundary(
            "auto:_Ref1", BookmarkBoundaryKind.Start, 3, "_Ref1"));
        caption.BookmarkBoundaries.Should().Contain(new BookmarkBoundary(
            "auto:_Ref1", BookmarkBoundaryKind.End, 4));
        host.Runs.Single(run => run.CrossReference is not null).Text
            .Should().Be("Sample caption text");
    }

    [Fact]
    public void FieldUpdateRefreshesEveryGeneratedReferenceRegionInOnePass()
    {
        var document = new TextDocument();
        document.Blocks.Add(new Paragraph(TableOfContents.HeadingText)
        {
            StyleId = TableOfContents.HeadingStyleId
        });
        document.Blocks.Add(new Paragraph("Old heading\t9")
        {
            StyleId = TableOfContents.EntryStyleId(1)
        });
        document.Blocks.Add(new Paragraph("Fresh heading") { StyleId = "Heading1" });
        document.Blocks.Add(new Paragraph(Citations.HeadingText)
        {
            StyleId = Citations.HeadingStyleId
        });
        document.Blocks.Add(new Paragraph("Old bibliography")
        {
            StyleId = Citations.EntryStyleId
        });
        document.Blocks.Add(Captions.BuildCaption(CaptionLabel.Figure, 1, "Fresh diagram"));
        document.Blocks.Add(new Paragraph("Table of Figures")
        {
            StyleId = TableOfFigures.HeadingStyleId
        });
        document.Blocks.Add(new Paragraph("Old figure\t9")
        {
            StyleId = TableOfFigures.EntryStyleId
        });
        document.Blocks.Add(new Paragraph
        {
            Runs = { Run.CitationMark(new Citation("Fresh Case", CitationCategory.Cases)) }
        });
        document.Blocks.AddRange(TableOfAuthorities.Build(
            new[] { new Citation("Old Case", CitationCategory.Cases) }));
        document.Sources.Add(new Source
        {
            Tag = "Fresh2026",
            Author = "Fresh Author",
            Title = "Current Source",
            Year = "2026"
        });
        var session = new DocumentEditingSession();
        session.LoadDocument(document);
        var blockPageLayoutCalls = 0;
        var authorityLayoutCalls = 0;

        var result = session.References.UpdateFields(
            blockPageResolutionFactory: () =>
            {
                blockPageLayoutCalls++;
                return new DocumentReferenceBlockPageResolution(_ => 1, PageCount: 1);
            },
            authorityPageResolverFactory: () =>
            {
                authorityLayoutCalls++;
                return (_, _, _, _) => TableOfAuthorities.CreatePageReference(3);
            });

        result.RefreshedGeneratedRegionCount.Should().Be(4);
        blockPageLayoutCalls.Should().Be(2, "TOC and figure pages are resolved after prior region edits");
        authorityLayoutCalls.Should().Be(1);
        document.Blocks.Where(TableOfContents.IsTocParagraph)
            .Cast<Paragraph>()
            .Select(paragraph => paragraph.PlainText)
            .Should().Contain("Fresh heading\t1").And.NotContain("Old heading\t9");
        document.Blocks.Where(Citations.IsBibliographyParagraph)
            .Cast<Paragraph>()
            .Select(paragraph => paragraph.PlainText)
            .Should().Contain(text => text.Contains("Current Source", StringComparison.Ordinal))
            .And.NotContain("Old bibliography");
        document.Blocks.Where(TableOfFigures.IsTableOfFiguresParagraph)
            .Cast<Paragraph>()
            .Select(paragraph => paragraph.PlainText)
            .Should().Contain("Figure 1: Fresh diagram\t1").And.NotContain("Old figure\t9");
        document.Blocks.Where(TableOfAuthorities.IsTableOfAuthoritiesParagraph)
            .Cast<Paragraph>()
            .Select(paragraph => paragraph.PlainText)
            .Should().Contain("Fresh Case\t3").And.NotContain(text => text.Contains("Old Case", StringComparison.Ordinal));
    }

    [Fact]
    public void GeneratedReferencePaginationExpandsTableSpanAndFormatsTableParagraphPages()
    {
        var document = FreeWVisualEvidenceDocumentFactory.BuildTablePaginationRepeatHeaderDocument();
        var tableIndex = document.Blocks
            .Select((block, index) => (block, index))
            .Single(item => item.block is Table)
            .index;
        var table = (Table)document.Blocks[tableIndex];

        var pagination = GeneratedReferencePaginationContext.Create(
            document,
            minimumPageCount: 2,
            physicalPageOfBlock: blockIndex => blockIndex == tableIndex ? 3 : 1);
        var lastParagraph = new TableParagraphAddress(
            table.Rows.Count - 1,
            CellIndex: 0,
            ParagraphIndex: 0);

        pagination.EffectivePageCount.Should().Be(4);
        pagination.ResolvePageText(tableIndex, lastParagraph).Should().Be("4");
        pagination.ResolveTableOfAuthoritiesPageReference(tableIndex, lastParagraph)
            .Should().Be(new ToaCitationPageReference(4, "4"));
    }

    [Fact]
    public void BibliographyInsertAndRefreshOwnInsertionCaretAndAtomicUndo()
    {
        var lead = new Paragraph("Lead");
        var caretParagraph = new Paragraph("Caret");
        var document = new TextDocument { Blocks = { lead, caretParagraph } };
        document.Sources.Add(new Source
        {
            Tag = "Ada2026",
            Author = "Ada",
            Title = "First source",
            Year = "2026"
        });
        var session = new DocumentEditingSession();
        session.LoadDocument(document);

        var inserted = session.References.InsertBibliography(new DocumentTextPosition(1, 3));

        inserted.Region.InsertIndex.Should().Be(1);
        inserted.Region.DeletedCount.Should().Be(0);
        inserted.Region.InsertedCount.Should().BeGreaterThan(0);
        inserted.Caret.Should().Be(new DocumentTextPosition(1 + inserted.Region.InsertedCount, 3));
        document.Blocks[inserted.Caret.BlockIndex].Should().BeSameAs(caretParagraph);
        session.Commands.Undo().Should().BeTrue();
        document.Blocks.Should().Equal(lead, caretParagraph);

        inserted = session.References.InsertBibliography(new DocumentTextPosition(1, 3));
        var oldRegionText = document.Blocks
            .Where(Citations.IsBibliographyParagraph)
            .Cast<Paragraph>()
            .Select(paragraph => paragraph.PlainText)
            .ToArray();
        document.Sources.Add(new Source
        {
            Tag = "Grace2026",
            Author = "Grace",
            Title = "Second source",
            Year = "2026"
        });

        var refreshed = session.References.RefreshBibliography(inserted.Caret);

        refreshed.Region.InsertIndex.Should().Be(1);
        refreshed.Region.DeletedCount.Should().Be(oldRegionText.Length);
        document.Blocks[refreshed.Caret.BlockIndex].Should().BeSameAs(caretParagraph);
        document.Blocks.Where(Citations.IsBibliographyParagraph)
            .Cast<Paragraph>()
            .Select(paragraph => paragraph.PlainText)
            .Should().Contain(text => text.Contains("Second source", StringComparison.Ordinal));
        session.Commands.Undo().Should().BeTrue();
        document.Blocks.Where(Citations.IsBibliographyParagraph)
            .Cast<Paragraph>()
            .Select(paragraph => paragraph.PlainText)
            .Should().Equal(oldRegionText);
    }

    [Fact]
    public void IndexInsertAndRefreshOwnSelectiveRegionLookupAndCaretTransition()
    {
        var marked = new Paragraph
        {
            Runs = { new Run("People"), DocumentIndex.MarkRun(new IndexMark("Ada", Identifier: "People")) }
        };
        var caretParagraph = new Paragraph("Caret");
        var document = new TextDocument { Blocks = { marked, caretParagraph } };
        var session = new DocumentEditingSession();
        session.LoadDocument(document);

        var inserted = session.References.InsertIndex(
            new DocumentTextPosition(1, 2),
            "People",
            pageReferenceOf: null);

        inserted.Region.InsertIndex.Should().Be(1);
        inserted.Caret.Should().Be(new DocumentTextPosition(1 + inserted.Region.InsertedCount, 2));
        document.Blocks[inserted.Caret.BlockIndex].Should().BeSameAs(caretParagraph);
        session.Commands.Undo().Should().BeTrue();
        document.Blocks.Should().Equal(marked, caretParagraph);

        inserted = session.References.InsertIndex(
            new DocumentTextPosition(1, 2),
            "People",
            pageReferenceOf: null);
        marked.Runs.Add(DocumentIndex.MarkRun(new IndexMark("Grace", Identifier: "People")));

        var refreshed = session.References.RefreshIndex(
            inserted.Caret,
            "People",
            pageReferenceOf: null);

        refreshed.Region.InsertIndex.Should().Be(1);
        document.Blocks[refreshed.Caret.BlockIndex].Should().BeSameAs(caretParagraph);
        document.Blocks.Where(block => DocumentIndex.IsIndexParagraph(block, "People"))
            .Cast<Paragraph>()
            .Select(paragraph => paragraph.PlainText)
            .Should().Contain("Grace, 1");
        session.Commands.Undo().Should().BeTrue();
        document.Blocks.Where(block => DocumentIndex.IsIndexParagraph(block, "People"))
            .Cast<Paragraph>()
            .Select(paragraph => paragraph.PlainText)
            .Should().NotContain("Grace, 1");
    }

    [Fact]
    public void IndexRefreshRemapsCaretsAcrossSparseGeneratedRegion()
    {
        static (DocumentEditingSession Session, TextDocument Document, Paragraph Before, Paragraph After)
            CreateScenario()
        {
            var before = new Paragraph("Before");
            var marked = new Paragraph
            {
                Runs = { DocumentIndex.MarkRun(new IndexMark("Ada", Identifier: "People")) }
            };
            var after = new Paragraph("After");
            var document = new TextDocument
            {
                Blocks =
                {
                    before,
                    new Paragraph(DocumentIndex.HeadingText)
                    {
                        StyleId = DocumentIndex.HeadingStyleIdFor("People")
                    },
                    marked,
                    new Paragraph("Old Person, 9")
                    {
                        StyleId = DocumentIndex.EntryStyleIdFor("People")
                    },
                    after
                }
            };
            var session = new DocumentEditingSession();
            session.LoadDocument(document);
            return (session, document, before, after);
        }

        var beforeScenario = CreateScenario();
        var before = beforeScenario.Session.References.RefreshIndex(
            new DocumentTextPosition(0, 2),
            "People",
            pageReferenceOf: null);
        before.Caret.Should().Be(new DocumentTextPosition(0, 2));
        beforeScenario.Document.Blocks[before.Caret.BlockIndex]
            .Should().BeSameAs(beforeScenario.Before);

        var insideScenario = CreateScenario();
        var inside = insideScenario.Session.References.RefreshIndex(
            new DocumentTextPosition(3, 7),
            "People",
            pageReferenceOf: null);
        inside.Caret.Should().Be(new DocumentTextPosition(1, 0));
        insideScenario.Document.Blocks[inside.Caret.BlockIndex]
            .Should().Match<Block>(block => DocumentIndex.IsIndexParagraph(block, "People"));

        var afterScenario = CreateScenario();
        var after = afterScenario.Session.References.RefreshIndex(
            new DocumentTextPosition(4, 3),
            "People",
            pageReferenceOf: null);
        after.Caret.Should().Be(new DocumentTextPosition(2 + after.Region.InsertedCount, 3));
        afterScenario.Document.Blocks[after.Caret.BlockIndex]
            .Should().BeSameAs(afterScenario.After);
    }

    [Fact]
    public void IndexRefreshWithoutExistingRegionAppendsAndPreservesCaret()
    {
        var marked = new Paragraph
        {
            Runs = { DocumentIndex.MarkRun(new IndexMark("Ada", Identifier: "People")) }
        };
        var document = new TextDocument { Blocks = { marked } };
        var session = new DocumentEditingSession();
        session.LoadDocument(document);

        var refreshed = session.References.RefreshIndex(
            new DocumentTextPosition(0, 0),
            "People",
            pageReferenceOf: null);

        refreshed.Region.InsertIndex.Should().Be(1);
        refreshed.Caret.Should().Be(new DocumentTextPosition(0, 0));
        document.Blocks[0].Should().BeSameAs(marked);
        document.Blocks.Skip(1)
            .Should().OnlyContain(block => DocumentIndex.IsIndexParagraph(block, "People"));
    }

    [Fact]
    public void TableOfFiguresInsertAndRefreshOwnNormalizationRegionLookupAndCaretTransition()
    {
        var caption = Captions.BuildCaption(CaptionLabel.Figure, 1, "First");
        var caretParagraph = new Paragraph("Caret");
        var document = new TextDocument { Blocks = { caption, caretParagraph } };
        var session = new DocumentEditingSession();
        session.LoadDocument(document);

        var inserted = session.References.InsertTableOfFigures(
            new DocumentTextPosition(1, 4),
            " Figure ",
            (_, _) => "2");

        inserted.Region.InsertIndex.Should().Be(1);
        inserted.Caret.Should().Be(new DocumentTextPosition(1 + inserted.Region.InsertedCount, 4));
        document.Blocks[inserted.Caret.BlockIndex].Should().BeSameAs(caretParagraph);
        session.Commands.Undo().Should().BeTrue();
        document.Blocks.Should().Equal(caption, caretParagraph);

        inserted = session.References.InsertTableOfFigures(
            new DocumentTextPosition(1, 4),
            Captions.FigureLabelText,
            (_, _) => "2");
        caption.Runs[^1].Text = ": Updated";

        var refreshed = session.References.RefreshTableOfFigures(
            inserted.Caret,
            Captions.FigureLabelText,
            (_, _) => "3");

        refreshed.Region.InsertIndex.Should().Be(1);
        document.Blocks[refreshed.Caret.BlockIndex].Should().BeSameAs(caretParagraph);
        document.Blocks.Where(TableOfFigures.IsTableOfFiguresParagraph)
            .Cast<Paragraph>()
            .Select(paragraph => paragraph.PlainText)
            .Should().Contain("Figure 1: Updated\t3");
        session.Commands.Undo().Should().BeTrue();
        document.Blocks.Where(TableOfFigures.IsTableOfFiguresParagraph)
            .Cast<Paragraph>()
            .Select(paragraph => paragraph.PlainText)
            .Should().Contain("Figure 1: First\t2");
    }

    [Fact]
    public void TableOfAuthoritiesInsertAndRefreshOwnPlansCaretAndStabilization()
    {
        var citation = new Citation("Fresh Case", CitationCategory.Cases);
        var marked = new Paragraph { Runs = { Run.CitationMark(citation) } };
        var caretParagraph = new Paragraph("Caret");
        var document = new TextDocument { Blocks = { marked, caretParagraph } };
        var session = new DocumentEditingSession();
        session.LoadDocument(document);

        var inserted = session.References.InsertTableOfAuthorities(
            new DocumentTextPosition(1, 2),
            ToaOptions.Default,
            () => (_, _, _, _, _) => TableOfAuthorities.CreatePageReference(1));

        inserted.Region.InsertIndex.Should().Be(1);
        inserted.Caret.Should().Be(new DocumentTextPosition(1 + inserted.Region.InsertedCount, 2));
        document.Blocks[inserted.Caret.BlockIndex].Should().BeSameAs(caretParagraph);

        var physicalPage = 1;
        var layoutRefreshes = 0;
        var refreshed = session.References.RefreshTableOfAuthorities(
            inserted.Caret,
            options: null,
            pageResolverFactory: () => (_, _, _, _, _) =>
                TableOfAuthorities.CreatePageReference(physicalPage),
            refreshLayout: () =>
            {
                layoutRefreshes++;
                physicalPage = 3;
            });

        refreshed.Region.InsertIndex.Should().Be(1);
        refreshed.Region.DeletedCount.Should().Be(inserted.Region.InsertedCount);
        refreshed.Caret.BlockIndex.Should().Be(
            inserted.Caret.BlockIndex - refreshed.Region.DeletedCount + refreshed.Region.InsertedCount);
        document.Blocks[refreshed.Caret.BlockIndex].Should().BeSameAs(caretParagraph);
        layoutRefreshes.Should().Be(2);
        document.Blocks.Where(TableOfAuthorities.IsTableOfAuthoritiesParagraph)
            .Cast<Paragraph>()
            .Select(paragraph => paragraph.PlainText)
            .Should().Contain("Fresh Case\t3");

        session.Commands.Undo().Should().BeTrue();
        document.Blocks.Where(TableOfAuthorities.IsTableOfAuthoritiesParagraph)
            .Cast<Paragraph>()
            .Select(paragraph => paragraph.PlainText)
            .Should().Contain("Fresh Case\t1");
        session.Commands.Undo().Should().BeTrue();
        document.Blocks.Should().Equal(marked, caretParagraph);
    }

    [Fact]
    public void TableOfAuthoritiesStabilizationIsOnePortableUndoTransaction()
    {
        var citation = new Citation("Fresh Case", CitationCategory.Cases);
        var document = new TextDocument();
        document.Blocks.Add(new Paragraph { Runs = { Run.CitationMark(citation) } });
        document.Blocks.AddRange(TableOfAuthorities.Build(
            document,
            ToaOptions.Default,
            (_, _, _, _) => TableOfAuthorities.CreatePageReference(1)));
        var session = new DocumentEditingSession();
        session.LoadDocument(document);
        var initialPlan = TableOfAuthoritiesRegionPlanner.BuildRefreshPlanWithTableAddresses(
            document,
            pageResolver: (_, _, _, _, _) => TableOfAuthorities.CreatePageReference(1));
        var physicalPage = 1;
        var layoutRefreshes = 0;

        var result = session.References.ApplyStabilizedTableOfAuthoritiesRegion(
            initialPlan,
            pageResolverFactory: () => (_, _, _, _, _) =>
                TableOfAuthorities.CreatePageReference(physicalPage),
            undoLabel: "Update Table of Authorities",
            refreshLayout: () =>
            {
                layoutRefreshes++;
                physicalPage = 3;
            });

        result.Applied.Should().BeTrue();
        layoutRefreshes.Should().Be(2);
        document.Blocks.Where(TableOfAuthorities.IsTableOfAuthoritiesParagraph)
            .Cast<Paragraph>()
            .Select(paragraph => paragraph.PlainText)
            .Should().Contain("Fresh Case\t3");

        session.Commands.Undo().Should().BeTrue();
        document.Blocks.Where(TableOfAuthorities.IsTableOfAuthoritiesParagraph)
            .Cast<Paragraph>()
            .Select(paragraph => paragraph.PlainText)
            .Should().Contain("Fresh Case\t1");
    }

    [Fact]
    public void TocInsertAndRefreshAreAtomicGeneratedRegionEdits()
    {
        var heading = new Paragraph("Old heading") { StyleId = "Heading1" };
        var document = new TextDocument();
        document.Blocks.Add(heading);
        var session = new DocumentEditingSession();
        session.LoadDocument(document);

        session.References.InsertTableOfContents(0, pageTextResolver: null).Applied.Should().BeTrue();
        document.Blocks.Any(TableOfContents.IsTocParagraph).Should().BeTrue();
        session.Commands.Undo().Should().BeTrue();
        document.Blocks.Should().ContainSingle().Which.Should().BeSameAs(heading);

        session.References.InsertTableOfContents(0, pageTextResolver: null);
        heading.Runs.Clear();
        heading.Runs.Add(new Run("New heading"));
        session.References.RefreshTableOfContents(pageTextResolver: null).Applied.Should().BeTrue();
        document.Blocks.OfType<Paragraph>()
            .Where(TableOfContents.IsTocParagraph)
            .Should().Contain(paragraph => paragraph.PlainText.Contains("New heading", StringComparison.Ordinal));
    }

    [Fact]
    public void TocInsertStabilizesPageTextInsideOnePortableUndoGroup()
    {
        var heading = new Paragraph("Paged heading") { StyleId = "Heading1" };
        var document = new TextDocument();
        document.Blocks.Add(heading);
        var session = new DocumentEditingSession();
        session.LoadDocument(document);
        var page = "1";
        var layoutRefreshes = 0;

        var result = session.References.InsertTableOfContents(
            0,
            pageTextResolverFactory: () => _ => page,
            refreshLayout: () =>
            {
                layoutRefreshes++;
                page = "3";
            });

        result.Applied.Should().BeTrue();
        layoutRefreshes.Should().Be(2);
        document.Blocks.OfType<Paragraph>()
            .Where(TableOfContents.IsTocParagraph)
            .Select(paragraph => paragraph.PlainText)
            .Should().Contain("Paged heading\t3");

        session.Commands.Undo().Should().BeTrue();
        document.Blocks.Should().ContainSingle().Which.Should().BeSameAs(heading);
    }

    [Fact]
    public void CaptionAndCrossReferenceConstructionArePortable()
    {
        var target = new Paragraph("Chapter") { StyleId = "Heading1" };
        var host = new Paragraph("See ");
        var document = new TextDocument();
        document.Blocks.Add(target);
        document.Blocks.Add(host);
        var session = new DocumentEditingSession();
        session.LoadDocument(document);

        var crossReference = session.References.InsertCrossReference(
            sourceBlockIndex: 1,
            preferredHostBlockIndex: 1,
            CrossRefType.Heading,
            new CrossRefTarget("Chapter", Anchor: null, BlockIndex: 0),
            CrossRefInsertAs.Text,
            hyperlink: true);

        crossReference.HostBlockIndex.Should().Be(1);
        host.Runs.Should().Contain(run => run.CrossReference != null);

        var caption = session.References.InsertCaption(1, "Figure", "Diagram");
        caption.InsertedBlockIndex.Should().Be(2);
        ((Paragraph)document.Blocks[2]).PlainText.Should().Contain("Figure 1");
    }

    [Fact]
    public void NotesBookmarksAndCitationSettingsAreCoordinatorOwned()
    {
        var document = new TextDocument();
        document.Blocks.Add(new Paragraph("Host"));
        var session = new DocumentEditingSession();
        session.LoadDocument(document);

        var note = session.References.InsertNote(0, 2, "note", footnote: true);

        note.Applied.Should().BeTrue();
        document.Footnotes.Should().ContainKey(note.NoteId);
        session.Commands.Undo().Should().BeTrue();
        document.Footnotes.Should().NotContainKey(note.NoteId);

        session.References.SetBookmark(0, " Target ").Should().BeTrue();
        ((Paragraph)document.Blocks[0]).BookmarkNames.Should().Contain("Target");
        session.References.ApplyCitationStyle(CitationStyle.Ieee).Should().BeTrue();
        document.BibliographyStyle.Should().Be(CitationStyle.Ieee);

        session.References.InsertIndexEntry(0, 0, new IndexMark("Host"))
            .Applied.Should().BeTrue();
        ((Paragraph)document.Blocks[0]).Runs.Should()
            .Contain(run => DocumentIndex.MarkedEntry(run) != null);
        session.References.InsertAuthorityCitation(0, 0, new Citation("Case"))
            .Applied.Should().BeTrue();
        ((Paragraph)document.Blocks[0]).Runs.Should()
            .Contain(run => run.Citation != null);
    }
}

public sealed class DocumentPortableEditingOwnershipTests
{
    [Fact]
    public void RenderersDelegateMigratedTableParagraphAndReferenceCommands()
    {
        var wpf = ReadSource("freew", "FreeW.App.Host", "Editing", "DocumentView.cs");
        var avalonia = ReadSource("freew", "FreeW.App.Avalonia", "Editing", "DocumentView.cs");
        var forbidden = new[]
        {
            "new InsertTableRowCommand(",
            "new DeleteTableRowCommand(",
            "new InsertTableColumnCommand(",
            "new DeleteTableColumnCommand(",
            "new MergeCellsHorizontalCommand(",
            "new MergeCellsVerticalCommand(",
            "new SplitCellCommand(",
            "new SetCellShadingCommand(",
            "new SetCellAlignmentCommand(",
            "new SetCellBordersCommand(",
            "new SetTableFormattingCommand(",
            "new SetTableAutoFitCommand(",
            "new ApplyTableStyleCommand(",
            "new ApplyTablePropertiesCommand(",
            "new InsertTableCellFormulaCommand(",
            "new InsertTableCellNoteCommand(",
            "new SetParagraphStyleCommand(",
            "new DeleteParagraphCommand(",
            "new InsertCrossReferenceCommand(",
            "new ApplyManualHyphenationCommand(",
            "new FormatParagraphRunsCommand(",
            "new ReorderBlocksCommand(",
            "new InsertNoteCommand(",
            "new DeleteNoteCommand(",
            "new ReplaceNoteContentCommand(",
            "new SetNoteNumberingOptionsCommand(",
            "new ApplyCitationStyleCommand(",
            "new ReplaceSourcesCommand(",
            "new SetParagraphBookmarkNameCommand(",
            "new SetBookmarkNameCommand(",
            "new CellTextCommand(",
            "new ReplaceContentControlRunCommand(",
        };

        foreach (var source in new[] { wpf, avalonia })
        {
            source.Should().Contain("DocumentTableEditingCoordinator TableEdits");
            source.Should().Contain("DocumentReferenceEditingCoordinator ReferenceEdits");
            source.Should().Contain("_editingSession.FormatParagraphs(");
            source.Should().Contain("_editingSession.SetParagraphStyles(");
            source.Should().Contain("_editingSession.ApplyDropCap(");
            source.Should().Contain("ReferenceEdits.InsertIndexEntry(");
            source.Should().Contain("ReferenceEdits.MarkAllIndexEntries(");
            source.Should().Contain("ReferenceEdits.ToggleFieldCodes()");
            source.Should().Contain("ReferenceEdits.UpdateFields(");
            source.Should().Contain("ReferenceEdits.ApplyNoteNumberingOptions(result)");
            source.Should().Contain("DocumentReferenceBlockPageResolution BuildReferenceBlockPageResolution()");
            source.Should().NotContain("CrossReferences.ResolveField(");
            source.Should().NotContain("ComplexFieldEngine.Recompute(");
            source.Should().NotContain("refreshedGeneratedRegion");
            source.Should().NotContain("with { ShowCode = show");
            foreach (var constructor in forbidden)
                source.Should().NotContain(constructor);
        }

        avalonia.Should().Contain("TableEdits.SetCellText(address, text)");

        File.Exists(Path.Combine(
                TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx"),
                "freew", "FreeW.App.Avalonia", "Editing", "ReferenceCommands.cs"))
            .Should().BeFalse("renderer-neutral reference commands belong in Core or Presentation");
    }

    [Fact]
    public void GeneratedReferenceOrchestrationAndPaginationStayRendererNeutral()
    {
        var wpf = ReadSource("freew", "FreeW.App.Host", "Editing", "DocumentView.cs");
        var avalonia = ReadSource("freew", "FreeW.App.Avalonia", "Editing", "DocumentView.cs");
        var coordinator = ReadSource(
            "freew",
            "FreeW.App.Presentation",
            "Editing",
            "DocumentReferenceEditingCoordinator.cs");
        var operations = new[]
        {
            "InsertBibliography",
            "RefreshBibliography",
            "InsertIndex",
            "RefreshIndex",
            "InsertTableOfFigures",
            "RefreshTableOfFigures",
            "InsertTableOfAuthorities",
            "RefreshTableOfAuthorities",
        };
        var rendererForbidden = new[]
        {
            "BibliographyRegionPlanner.Build",
            "DocumentIndex.EnsureStyles(",
            "DocumentIndex.Build(",
            "DocumentIndex.IsIndexParagraph(",
            "TableOfFigures.EnsureStyles(",
            "TableOfFigures.BuildWithTableAddresses(",
            "TableOfFigures.IsTableOfFiguresParagraph(",
            "TableOfAuthoritiesRegionPlanner.Build",
            "ReferenceEdits.InsertGeneratedRegion(",
            "ReferenceEdits.RefreshGeneratedRegion(",
            "ReferenceEdits.ApplyGeneratedRegion(",
            "ReferenceEdits.ApplyStabilizedTableOfAuthoritiesRegion(",
            "\"Insert Bibliography\"",
            "\"Update Bibliography\"",
            "\"Insert Index\"",
            "\"Update Index\"",
            "\"Insert Table of Figures\"",
            "\"Update Table of Figures\"",
            "\"Insert Table of Authorities\"",
            "\"Update Table of Authorities\"",
        };

        foreach (var source in new[] { wpf, avalonia })
        {
            source.Should().Contain("GeneratedReferencePaginationContext.Create(");
            source.Should().NotContain("ResolveTableParagraphPageOffset(");
            source.Should().NotContain("private static ToaCitationPageReference CreateTableOfAuthoritiesPageReference(");
            source.Should().NotContain("ApplyTableOfAuthoritiesPlanCommands(");
            source.Should().NotContain("maxStabilizationPasses");
            foreach (var operation in operations)
                source.Should().Contain($"ReferenceEdits.{operation}(");
            foreach (var forbidden in rendererForbidden)
                source.Should().NotContain(forbidden);
        }

        foreach (var label in rendererForbidden.Where(value => value.StartsWith("\"", StringComparison.Ordinal)))
            coordinator.Should().Contain(label);
        coordinator.Should().Contain("BibliographyRegionPlanner.BuildInsertPlan(");
        coordinator.Should().Contain("GeneratedRegionIndices(");
        coordinator.Should().Contain("CompleteGeneratedReferenceEdit(");
        coordinator.Should().Contain("ApplyStabilizedTableOfAuthoritiesRegion(");
    }

    [Fact]
    public void PortableCoordinatorsHaveNoRendererDependencies()
    {
        foreach (var file in new[]
        {
            "DocumentTableEditingCoordinator.cs",
            "DocumentReferenceEditingCoordinator.cs",
        })
        {
            var source = ReadSource("freew", "FreeW.App.Presentation", "Editing", file);
            source.Should().NotContain("using Avalonia");
            source.Should().NotContain("using System.Windows");
            source.Should().NotContain("FreeW.App.Host");
            source.Should().NotContain("FreeW.App.Avalonia");
            source.Should().NotContain("TextPointer");
            source.Should().NotContain("DocPosition");
        }
    }

    [Fact]
    public void AvaloniaEditingFolderContainsNoDocumentCommandImplementations()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var editingDirectory = Path.Combine(root, "freew", "FreeW.App.Avalonia", "Editing");
        var offenders = Directory.EnumerateFiles(editingDirectory, "*.cs")
            .Where(file => File.ReadAllText(file).Contains(": IDocumentCommand", StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .ToArray();

        offenders.Should().BeEmpty("renderer-neutral undo commands belong in Core or Presentation");
    }

    private static string ReadSource(params string[] parts)
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        return File.ReadAllText(Path.Combine(new[] { root }.Concat(parts).ToArray()));
    }
}
