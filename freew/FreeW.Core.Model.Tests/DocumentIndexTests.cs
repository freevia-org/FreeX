namespace FreeW.Core.Model.Tests;

public class DocumentIndexTests
{
    [Fact]
    public void Build_EmptyDocument_YieldsNoIndexResultParagraphs()
    {
        var doc = new TextDocument();

        var index = DocumentIndex.Build(doc);

        index.Should().BeEmpty();
    }

    [Fact]
    public void Build_FromTerms_SortsAlphabeticallyCaseInsensitiveAndDedupes()
    {
        var index = DocumentIndex.Build(new[] { "banana", "Apple", "cherry", "apple", "Banana" });

        // Heading first, then distinct terms sorted case-insensitively (the first-seen casing wins).
        index.Select(p => p.PlainText).Should().Equal(
            DocumentIndex.HeadingText,
            "Apple",
            "banana",
            "cherry");

        // Every entry paragraph carries the index entry style.
        index.Skip(1).Should().OnlyContain(p => p.StyleId == DocumentIndex.EntryStyleId);
    }

    [Fact]
    public void Build_TrimsAndSkipsBlankTerms()
    {
        var index = DocumentIndex.Build(new[] { "  spaced  ", "", "   ", "kept" });

        index.Select(p => p.PlainText).Should().Equal(
            DocumentIndex.HeadingText,
            "kept",
            "spaced");
    }

    [Fact]
    public void Build_FromDocumentIndexEntries_UsesMarkedTerms()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("Body"));
        doc.IndexEntries.Add(new IndexEntry("Zebra"));
        doc.IndexEntries.Add(new IndexEntry("alpha"));
        doc.IndexEntries.Add(new IndexEntry("alpha")); // duplicate collapsed

        var index = DocumentIndex.Build(doc);

        index.Select(p => p.PlainText).Should().Equal(
            "A",
            "alpha, 1",
            "Z",
            "Zebra, 1");
    }

    [Fact]
    public void Build_HiddenMarksAggregateDistinctLogicalPagesAndOverrideLegacySideStore()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph
        {
            Runs = { new Run("First"), DocumentIndex.MarkRun("Alpha") }
        });
        doc.Blocks.Add(DocumentOps.CreatePageBreak());
        doc.Blocks.Add(new Paragraph
        {
            Runs =
            {
                new Run("Second"),
                DocumentIndex.MarkRun("alpha"),
                DocumentIndex.MarkRun("Beta"),
                DocumentIndex.MarkRun("Alpha")
            }
        });
        doc.IndexEntries.Add(new IndexEntry("Alpha"));

        var index = DocumentIndex.Build(doc, blockIndex => blockIndex == 0 ? "iv" : "1");

        index.Select(paragraph => paragraph.PlainText).Should().Equal(
            "A",
            "Alpha, iv, 1",
            "B",
            "Beta, 1");
    }

    [Fact]
    public void Build_DeduplicatesByPhysicalPageIdentityInsteadOfRepeatedDisplayLabel()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph
        {
            Runs = { DocumentIndex.MarkRun(new IndexMark("Alpha", BoldPageNumber: true)) }
        });
        doc.Blocks.Add(new Paragraph
        {
            Runs = { DocumentIndex.MarkRun(new IndexMark("Alpha", ItalicPageNumber: true)) }
        });
        doc.Blocks.Add(new Paragraph { Runs = { DocumentIndex.MarkRun("Alpha") } });

        var entry = DocumentIndex.Build(
                doc,
                pageReferenceOf: blockIndex => blockIndex switch
                {
                    0 => new IndexPageReferenceAddress(0, "1"),
                    1 => new IndexPageReferenceAddress(0, "1"),
                    2 => new IndexPageReferenceAddress(1, "1"),
                    _ => null
                })
            .Single(paragraph => paragraph.StyleId == DocumentIndex.EntryStyleId);

        entry.PlainText.Should().Be("Alpha, 1, 1");
        var pageRuns = entry.Runs.Where(run => run.Text == "1").ToList();
        pageRuns.Should().HaveCount(2);
        pageRuns[0].Formatting.Bold.Should().BeTrue();
        pageRuns[0].Formatting.Italic.Should().BeTrue();
        pageRuns[1].Formatting.Bold.Should().BeFalse();
        pageRuns[1].Formatting.Italic.Should().BeFalse();
    }

    [Fact]
    public void MarkRun_RoundTripsQuotedTermThroughFieldInstructionParser()
    {
        var mark = DocumentIndex.MarkRun("  Alpha \\\"quoted\\\"  ");

        mark.Text.Should().BeEmpty();
        mark.ComplexField!.Keyword.Should().Be("XE");
        DocumentIndex.MarkedTerm(mark).Should().Be("Alpha \\\"quoted\\\"");
        DocumentIndex.MarkedTerm(new Run("Alpha")).Should().BeNull();
    }

    [Fact]
    public void Build_HierarchicalXeMarksEmitIndentedSubentriesAndCrossReferenceWithoutPage()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph { Runs = { new Run("Cats"), DocumentIndex.MarkRun(new IndexMark("Animals", "Cats")) } });
        doc.Blocks.Add(new Paragraph { Runs = { new Run("Dogs"), DocumentIndex.MarkRun(new IndexMark("Animals", "Dogs")) } });
        doc.Blocks.Add(new Paragraph
        {
            Runs =
            {
                new Run("Transport"),
                DocumentIndex.MarkRun(new IndexMark("Transportation", CrossReference: "See Vehicles"))
            }
        });

        var index = DocumentIndex.Build(doc, _ => "1");

        index.Select(paragraph => paragraph.PlainText).Should().Equal(
            "A",
            "Animals",
            "Cats, 1",
            "Dogs, 1",
            "T",
            "Transportation. See Vehicles");
        index[1].Formatting.Should().Match<ParagraphFormatting>(format =>
            format.IndentLeftPt == 12 && format.FirstLineIndentPt == -12);
        index[2].Formatting.Should().Match<ParagraphFormatting>(format =>
            format.IndentLeftPt == 24 && format.FirstLineIndentPt == -12);
        index[5].PlainText.Should().NotContain(", 1");
    }

    [Fact]
    public void MarkRun_SerializesAndParsesSubentryAndCrossReference()
    {
        var run = DocumentIndex.MarkRun(new IndexMark(
            "  Animals  ",
            " Cats:Longhair ",
            " See Pet care "));

        run.ComplexField!.Instruction.Should().Be(" XE \"Animals:Cats:Longhair\" \\t \"See Pet care\" ");
        DocumentIndex.MarkedEntry(run).Should().Be(new IndexMark(
            "Animals",
            "Cats:Longhair",
            "See Pet care"));
        DocumentIndex.MarkedTerm(run).Should().Be("Animals:Cats:Longhair");
    }

    [Fact]
    public void Build_PageNumberRunMergesBoldAndItalicFormattingForSamePage()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph
        {
            Runs =
            {
                new Run("Alpha"),
                DocumentIndex.MarkRun(new IndexMark("Alpha", BoldPageNumber: true)),
                DocumentIndex.MarkRun(new IndexMark("Alpha", ItalicPageNumber: true))
            }
        });

        var entry = DocumentIndex.Build(doc).Single(paragraph => paragraph.PlainText == "Alpha, 1");

        entry.Runs.Select(run => run.Text).Should().Equal("Alpha", ", ", "1");
        entry.Runs[0].Formatting.Bold.Should().BeFalse();
        entry.Runs[1].Formatting.Bold.Should().BeFalse();
        entry.Runs[2].Formatting.Bold.Should().BeTrue();
        entry.Runs[2].Formatting.Italic.Should().BeTrue();
    }

    [Fact]
    public void MarkRun_SerializesAndParsesPageNumberFormattingSwitches()
    {
        var run = DocumentIndex.MarkRun(new IndexMark(
            "Alpha",
            BoldPageNumber: true,
            ItalicPageNumber: true));

        run.ComplexField!.Instruction.Should().Be(" XE \"Alpha\" \\b \\i ");
        DocumentIndex.MarkedEntry(run).Should().Be(new IndexMark(
            "Alpha",
            BoldPageNumber: true,
            ItalicPageNumber: true));
    }

    [Fact]
    public void MarkRun_SerializesAndParsesBookmarkPageRangeSwitch()
    {
        var run = DocumentIndex.MarkRun(new IndexMark(
            "Alpha",
            BoldPageNumber: true,
            BookmarkName: "TopicRange"));

        run.ComplexField!.Instruction.Should().Be(" XE \"Alpha\" \\r \"TopicRange\" \\b ");
        DocumentIndex.MarkedEntry(run).Should().Be(new IndexMark(
            "Alpha",
            BoldPageNumber: true,
            BookmarkName: "TopicRange"));
    }

    [Fact]
    public void MarkRun_SerializesAndParsesAlternateIndexIdentifier()
    {
        var run = DocumentIndex.MarkRun(new IndexMark(
            "Alpha",
            BoldPageNumber: true,
            Identifier: "People"));

        run.ComplexField!.Instruction.Should().Be(" XE \"Alpha\" \\f \"People\" \\b ");
        DocumentIndex.MarkedEntry(run).Should().Be(new IndexMark(
            "Alpha",
            BoldPageNumber: true,
            Identifier: "People"));
    }

    [Fact]
    public void Build_FiltersAlternateIdentifiersAndTreatsIAsDefault()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph { Runs = { DocumentIndex.MarkRun(new IndexMark("Alpha")) } });
        doc.Blocks.Add(new Paragraph { Runs = { DocumentIndex.MarkRun(new IndexMark("Beta", Identifier: "I")) } });
        doc.Blocks.Add(new Paragraph { Runs = { DocumentIndex.MarkRun(new IndexMark("Carol", Identifier: "People")) } });
        doc.Blocks.Add(new Paragraph { Runs = { DocumentIndex.MarkRun(new IndexMark("Dave", Identifier: "people")) } });
        doc.Blocks.Add(new Paragraph { Runs = { DocumentIndex.MarkRun(new IndexMark("Rome", Identifier: "Places")) } });
        doc.IndexEntries.Add(new IndexEntry("Zebra"));

        DocumentIndex.Build(doc).Select(paragraph => paragraph.PlainText).Should().Equal(
            "A", "Alpha, 1", "B", "Beta, 1", "Z", "Zebra, 1");
        DocumentIndex.Build(doc, identifier: "People").Select(paragraph => paragraph.PlainText).Should().Equal(
            "C", "Carol, 1", "D", "Dave, 1");
        DocumentIndex.Build(doc, identifier: "Places").Select(paragraph => paragraph.PlainText).Should().Equal(
            "R", "Rome, 1");
    }

    [Fact]
    public void AlternateIndexStylesIdentifyOnlyTheirOwnGeneratedRegion()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Add(new Paragraph
        {
            Runs = { DocumentIndex.MarkRun(new IndexMark("Ada", Identifier: "People")) }
        });
        DocumentIndex.EnsureStyles(doc, "People");
        var paragraphs = DocumentIndex.Build(doc, identifier: "People");

        var headingStyle = DocumentIndex.HeadingStyleIdFor("People");
        var entryStyle = DocumentIndex.EntryStyleIdFor("People");
        doc.Styles.Should().ContainKey(headingStyle);
        doc.Styles.Should().ContainKey(entryStyle);
        paragraphs[0].StyleId.Should().Be(headingStyle);
        DocumentIndex.IsIndexParagraph(paragraphs[0]).Should().BeTrue();
        DocumentIndex.IsIndexParagraph(paragraphs[0], "People").Should().BeTrue();
        DocumentIndex.IsIndexParagraph(paragraphs[0], "Places").Should().BeFalse();
        DocumentIndex.IsIndexParagraph(paragraphs[0], null).Should().BeFalse();
        DocumentIndex.HeadingStyleIdFor("people").Should().Be(headingStyle);
    }

    [Fact]
    public void Build_WordDefaultGroupsSymbolsDigitsAndEnglishDiacriticsLikeWord()
    {
        var doc = new TextDocument();
        foreach (var term in new[] { "1alpha", "!bang", "Éclair", "Zulu" })
        {
            doc.Blocks.Add(new Paragraph
            {
                Runs = { DocumentIndex.MarkRun(term) }
            });
        }

        var paragraphs = DocumentIndex.Build(doc);

        paragraphs.Select(paragraph => paragraph.PlainText).Should().Equal(
            "!", "!bang, 1",
            "1", "1alpha, 1",
            "E", "Éclair, 1",
            "Z", "Zulu, 1");
        paragraphs[0].SpanningFieldStart!.Instruction.Should().Be(" INDEX \\h \"A\" \\z \"1033\" ");
        paragraphs.Should().OnlyContain(paragraph =>
            paragraph.SpanningFieldOwner != null
            && paragraph.SpanningFieldOwner.Instruction == " INDEX \\h \"A\" \\z \"1033\" ");
        paragraphs.Skip(1).Should().OnlyContain(paragraph => paragraph.SpanningFieldStart == null);
        paragraphs.Take(paragraphs.Count - 1).Should().OnlyContain(paragraph => !paragraph.EndsSpanningField);
        paragraphs[^1].EndsSpanningField.Should().BeTrue();
    }

    [Fact]
    public void Build_LegacyOptionsRetainSyntheticTitleWithoutAlphabeticHeadings()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph { Runs = { DocumentIndex.MarkRun("Alpha") } });

        DocumentIndex.Build(doc, options: IndexBuildOptions.LegacyTitleOnly)
            .Select(paragraph => paragraph.PlainText)
            .Should().Equal("Index", "Alpha, 1");
    }

    [Fact]
    public void Build_BookmarkPageRangeUsesFirstAndLastLogicalPageLabels()
    {
        var doc = new TextDocument();
        var start = new Paragraph("Range start");
        start.BookmarkNames.Add("TopicRange");
        start.BookmarkBoundaries.Add(new BookmarkBoundary(
            "42", BookmarkBoundaryKind.Start, 0, "TopicRange"));
        doc.Blocks.Add(start);
        doc.Blocks.Add(new Paragraph("Range middle"));
        var end = new Paragraph
        {
            Runs =
            {
                new Run("Range end"),
                DocumentIndex.MarkRun(new IndexMark(
                    "Alpha",
                    ItalicPageNumber: true,
                    BookmarkName: "TopicRange"))
            }
        };
        end.BookmarkBoundaries.Add(new BookmarkBoundary("42", BookmarkBoundaryKind.End, 0));
        doc.Blocks.Add(end);

        var entry = DocumentIndex.Build(doc, blockIndex => blockIndex switch
        {
            0 => "iv",
            2 => "vi",
            _ => null
        }).Single(paragraph => paragraph.PlainText == "Alpha, iv\u2013vi");

        entry.Runs.Select(run => run.Text).Should().Equal("Alpha", ", ", "iv\u2013vi");
        entry.Runs[^1].Formatting.Italic.Should().BeTrue();
    }

    [Fact]
    public void Build_BookmarkRangeRetainsEqualLabelsFromDistinctPhysicalPages()
    {
        var doc = new TextDocument();
        var start = new Paragraph("Start");
        start.BookmarkNames.Add("RestartedRange");
        start.BookmarkBoundaries.Add(new BookmarkBoundary(
            "range", BookmarkBoundaryKind.Start, 0, "RestartedRange"));
        doc.Blocks.Add(start);
        var end = new Paragraph
        {
            Runs =
            {
                new Run("End"),
                DocumentIndex.MarkRun(new IndexMark("Alpha", BookmarkName: "RestartedRange"))
            }
        };
        end.BookmarkBoundaries.Add(new BookmarkBoundary("range", BookmarkBoundaryKind.End, 0));
        doc.Blocks.Add(end);

        var entry = DocumentIndex.Build(
                doc,
                pageReferenceOf: blockIndex => new IndexPageReferenceAddress(blockIndex, "1"))
            .Single(paragraph => paragraph.StyleId == DocumentIndex.EntryStyleId);

        entry.PlainText.Should().Be("Alpha, 1\u20131");
    }

    [Fact]
    public void Build_BrokenOrMisCasedBookmarkRangeReportsWordError()
    {
        var doc = new TextDocument();
        var target = new Paragraph("Target");
        target.BookmarkName = "TopicRange";
        doc.Blocks.Add(target);
        doc.Blocks.Add(new Paragraph
        {
            Runs =
            {
                DocumentIndex.MarkRun(new IndexMark(
                    "Alpha",
                    BoldPageNumber: true,
                    BookmarkName: "topicrange"))
            }
        });

        var entry = DocumentIndex.Build(doc)
            .Single(paragraph => paragraph.StyleId == DocumentIndex.EntryStyleId);

        entry.PlainText.Should().Be("Alpha, " + DocumentIndex.BrokenBookmarkText);
        entry.Runs[^1].Formatting.Bold.Should().BeTrue();
    }

    [Fact]
    public void Build_PageReferencesAreSortedAscendingRegardlessOfMarkDocumentOrder()
    {
        // Document order of the marks is: page 12, then the "4-7" ranged mark, then page 9 — the
        // ranged mark's own literal location in the document does not correlate with the pages its
        // bookmark resolves to. The generated index must still list the references in ascending page
        // order, not the document (mark-occurrence) order in which they were encountered.
        var doc = new TextDocument();

        doc.Blocks.Add(new Paragraph { Runs = { DocumentIndex.MarkRun(new IndexMark("Alpha")) } }); // block 0: page 12

        var rangeStart = new Paragraph("Range start");
        rangeStart.BookmarkNames.Add("TopicRange");
        rangeStart.BookmarkBoundaries.Add(new BookmarkBoundary("range", BookmarkBoundaryKind.Start, 0, "TopicRange"));
        doc.Blocks.Add(rangeStart); // block 1: range start, page 4

        doc.Blocks.Add(new Paragraph("Range middle")); // block 2

        var rangeEnd = new Paragraph
        {
            Runs =
            {
                new Run("Range end"),
                DocumentIndex.MarkRun(new IndexMark("Alpha", BookmarkName: "TopicRange"))
            }
        };
        rangeEnd.BookmarkBoundaries.Add(new BookmarkBoundary("range", BookmarkBoundaryKind.End, 0));
        doc.Blocks.Add(rangeEnd); // block 3: range end, page 7 — carries the \r XE mark itself

        doc.Blocks.Add(new Paragraph { Runs = { DocumentIndex.MarkRun(new IndexMark("Alpha")) } }); // block 4: page 9

        var entry = DocumentIndex.Build(doc, pageReferenceOf: blockIndex => blockIndex switch
        {
            0 => new IndexPageReferenceAddress(11, "12"),
            1 => new IndexPageReferenceAddress(3, "4"),
            3 => new IndexPageReferenceAddress(6, "7"),
            4 => new IndexPageReferenceAddress(8, "9"),
            _ => null
        }).Single(paragraph => paragraph.StyleId == DocumentIndex.EntryStyleId);

        entry.PlainText.Should().Be("Alpha, 4–7, 9, 12");
    }

    [Fact]
    public void Build_AbuttingRangedAndSinglePageMarkCollapseIntoOneMergedRange()
    {
        // The ranged mark resolves to pages 4-7 and a separate single-page mark for the same term
        // lands on page 8 — immediately abutting the range. Word's index collates these into one
        // continuous "4-8" range instead of listing the abutting page as a separate entry.
        var doc = new TextDocument();

        var rangeStart = new Paragraph("Range start");
        rangeStart.BookmarkNames.Add("TopicRange");
        rangeStart.BookmarkBoundaries.Add(new BookmarkBoundary("range", BookmarkBoundaryKind.Start, 0, "TopicRange"));
        doc.Blocks.Add(rangeStart); // block 0: range start, page 4

        var rangeEnd = new Paragraph
        {
            Runs =
            {
                new Run("Range end"),
                DocumentIndex.MarkRun(new IndexMark("Alpha", BookmarkName: "TopicRange"))
            }
        };
        rangeEnd.BookmarkBoundaries.Add(new BookmarkBoundary("range", BookmarkBoundaryKind.End, 0));
        doc.Blocks.Add(rangeEnd); // block 1: range end, page 7

        doc.Blocks.Add(new Paragraph { Runs = { DocumentIndex.MarkRun(new IndexMark("Alpha")) } }); // block 2: page 8, abuts the range

        var entry = DocumentIndex.Build(doc, pageReferenceOf: blockIndex => blockIndex switch
        {
            0 => new IndexPageReferenceAddress(3, "4"),
            1 => new IndexPageReferenceAddress(6, "7"),
            2 => new IndexPageReferenceAddress(7, "8"),
            _ => null
        }).Single(paragraph => paragraph.StyleId == DocumentIndex.EntryStyleId);

        entry.PlainText.Should().Be("Alpha, 4–8");
    }

    [Fact]
    public void Build_BookmarkRangeResolvesBoundariesInsideTableCells()
    {
        var doc = new TextDocument();
        var start = new Paragraph("Range start");
        start.BookmarkNames.Add("TableRange");
        start.BookmarkBoundaries.Add(new BookmarkBoundary(
            "table-range", BookmarkBoundaryKind.Start, 0, "TableRange"));
        var startCell = new TableCell();
        startCell.Paragraphs.Add(start);
        var startRow = new TableRow();
        startRow.Cells.Add(startCell);
        var startTable = new Table();
        startTable.Rows.Add(startRow);
        doc.Blocks.Add(startTable);
        doc.Blocks.Add(new Paragraph("Range middle"));

        var end = new Paragraph("Range end");
        end.BookmarkBoundaries.Add(new BookmarkBoundary(
            "table-range", BookmarkBoundaryKind.End, 0));
        var endCell = new TableCell();
        endCell.Paragraphs.Add(end);
        var endRow = new TableRow();
        endRow.Cells.Add(endCell);
        var endTable = new Table();
        endTable.Rows.Add(endRow);
        doc.Blocks.Add(endTable);
        doc.Blocks.Add(new Paragraph
        {
            Runs = { DocumentIndex.MarkRun(new IndexMark("Alpha", BookmarkName: "TableRange")) }
        });

        var entry = DocumentIndex.Build(
                doc,
                pageReferenceOf: blockIndex => new IndexPageReferenceAddress(blockIndex, blockIndex switch
                {
                    0 => "ii",
                    2 => "iv",
                    _ => (blockIndex + 1).ToString(System.Globalization.CultureInfo.InvariantCulture)
                }))
            .Single(paragraph => paragraph.StyleId == DocumentIndex.EntryStyleId);

        entry.PlainText.Should().Be("Alpha, ii\u2013iv");
    }

    // THE FAILING-BEFORE PROOF for this item: a plain \r bookmark on a table's later row must not
    // collapse onto the table's own starting page just because the host's pageReferenceOf answers per
    // top-level block (exactly how DocumentReferenceEditingCoordinator.InsertIndex wires it in production
    // -- see DocumentReferenceEditingCoordinator.cs:445). Mirrors ComplexFieldEngineTests'
    // PageRef_BookmarkOnTableRowPastAuthoredPageBreak_ResolvesItsOwnPage and CrossReferencesTests'
    // ResolveField_PageRef_BookmarkOnTableRowPastAuthoredPageBreakUsesItsOwnPage for the same scenario.
    [Fact]
    public void Build_BookmarkOnTableRowPastAuthoredPageBreak_ResolvesItsOwnPage()
    {
        var doc = new TextDocument();
        var table = new Table();
        for (var rowIndex = 0; rowIndex < 3; rowIndex++)
        {
            var paragraph = new Paragraph("Cell " + rowIndex);
            if (rowIndex == 2)
            {
                paragraph.BookmarkNames.Add("rowTwo");
                paragraph.Formatting = ParagraphFormatting.Default with { PageBreakBefore = true };
            }
            var cell = new TableCell();
            cell.Paragraphs.Add(paragraph);
            var row = new TableRow();
            row.Cells.Add(cell);
            table.Rows.Add(row);
        }
        doc.Blocks.Add(table);
        doc.Blocks.Add(new Paragraph
        {
            Runs = { DocumentIndex.MarkRun(new IndexMark("Alpha", BookmarkName: "rowTwo")) }
        });

        var entry = DocumentIndex.Build(
                doc,
                pageReferenceOf: blockIndex => blockIndex == 0 ? new IndexPageReferenceAddress(2, "3") : null)
            .Single(paragraph => paragraph.StyleId == DocumentIndex.EntryStyleId);

        entry.PlainText.Should().Be("Alpha, 4");
    }

    // Sibling no-regression: a table that does NOT span a page break still reports the host's own page for
    // every row -- the row-offset correction must be a no-op when there is nothing to correct for.
    [Fact]
    public void Build_BookmarkOnTableRow_WithNoAuthoredPageBreak_SharesTablesOwnPage()
    {
        var doc = new TextDocument();
        var table = new Table();
        for (var rowIndex = 0; rowIndex < 3; rowIndex++)
        {
            var paragraph = new Paragraph("Cell " + rowIndex);
            if (rowIndex == 2)
                paragraph.BookmarkNames.Add("rowTwo");
            var cell = new TableCell();
            cell.Paragraphs.Add(paragraph);
            var row = new TableRow();
            row.Cells.Add(cell);
            table.Rows.Add(row);
        }
        doc.Blocks.Add(table);
        doc.Blocks.Add(new Paragraph
        {
            Runs = { DocumentIndex.MarkRun(new IndexMark("Alpha", BookmarkName: "rowTwo")) }
        });

        var entry = DocumentIndex.Build(
                doc,
                pageReferenceOf: blockIndex => blockIndex == 0 ? new IndexPageReferenceAddress(2, "3") : null)
            .Single(paragraph => paragraph.StyleId == DocumentIndex.EntryStyleId);

        entry.PlainText.Should().Be("Alpha, 3");
    }

    // Widened coverage: a bookmark that lives inside a footnote genuinely exists, so an INDEX \r entry
    // pointing at it must not report "Bookmark not defined" -- ResolveBookmarkRange's fallback previously
    // searched Bookmarks.List (body + table cells only) and never found it there. The footnote has no page
    // of its own to attribute, so this falls back to the model's ordinary "unresolved" label ("1"), not an
    // error.
    [Fact]
    public void Build_BookmarkInsideFootnote_IsFoundInsteadOfReportedBroken()
    {
        var doc = new TextDocument();
        var notePara = new Paragraph("note text") { BookmarkName = "NoteMark" };
        var footnote = new Footnote(1);
        footnote.Content.Add(notePara);
        doc.Footnotes[1] = footnote;
        doc.Blocks.Add(new Paragraph
        {
            Runs = { DocumentIndex.MarkRun(new IndexMark("Alpha", BookmarkName: "NoteMark")) }
        });

        var entry = DocumentIndex.Build(doc)
            .Single(paragraph => paragraph.StyleId == DocumentIndex.EntryStyleId);

        entry.PlainText.Should().NotContain(DocumentIndex.BrokenBookmarkText);
    }

    // Sibling no-regression: a bookmark that truly does not exist anywhere in the document -- main body,
    // table cell, header/footer, footnote/endnote, or text box -- must still be reported broken.
    [Fact]
    public void Build_BookmarkThatExistsNowhere_IsStillReportedBroken()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph
        {
            Runs = { DocumentIndex.MarkRun(new IndexMark("Alpha", BookmarkName: "NoSuchBookmark")) }
        });

        var entry = DocumentIndex.Build(doc)
            .Single(paragraph => paragraph.StyleId == DocumentIndex.EntryStyleId);

        entry.PlainText.Should().Be("Alpha, " + DocumentIndex.BrokenBookmarkText);
    }

    [Fact]
    public void Build_IncludesXeFieldsInsideTableCells()
    {
        var cell = new TableCell();
        cell.Paragraphs.Add(new Paragraph
        {
            Runs = { DocumentIndex.MarkRun(new IndexMark("Alpha")) }
        });
        var row = new TableRow();
        row.Cells.Add(cell);
        var table = new Table();
        table.Rows.Add(row);
        var doc = new TextDocument();
        doc.Blocks.Add(table);

        DocumentIndex.Build(
                doc,
                pageReferenceOf: _ => new IndexPageReferenceAddress(3, "4"))
            .Select(paragraph => paragraph.PlainText)
            .Should().Equal("A", "Alpha, 4");
    }

    [Fact]
    public void MarkAllTargets_FindWholeTermParagraphsAndSkipGeneratedOrExistingMarks()
    {
        var mark = new IndexMark("Alpha", "Topic");
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("Alpha starts Alpha"));
        doc.Blocks.Add(new Paragraph("alphabet is not the term"));
        doc.Blocks.Add(new Paragraph("Another ALPHA appears"));
        doc.Blocks.Add(new Paragraph("Alpha, 1") { StyleId = DocumentIndex.EntryStyleId });
        doc.Blocks.Add(new Paragraph
        {
            Runs = { new Run("Alpha"), DocumentIndex.MarkRun(mark), new Run(" already marked") }
        });

        DocumentIndex.MarkAllTargets(doc, " alpha ", mark).Should().Equal(
            new IndexMarkTarget(0, 5),
            new IndexMarkTarget(0, 18),
            new IndexMarkTarget(2, 13));
    }

    [Fact]
    public void MarkAllTargets_IncludesParagraphsInsideTableCells()
    {
        var cell = new TableCell();
        cell.Paragraphs.Add(new Paragraph("Alpha in a cell"));
        var row = new TableRow();
        row.Cells.Add(cell);
        var table = new Table();
        table.Rows.Add(row);
        var doc = new TextDocument();
        doc.Blocks.Add(table);
        doc.Blocks.Add(new Paragraph("Alpha in the body"));
        var mark = new IndexMark("Alpha");

        DocumentIndex.MarkAllTargets(doc, "Alpha", mark).Should().Equal(
            new IndexMarkTarget(0, 5, new TableParagraphAddress(0, 0, 0)),
            new IndexMarkTarget(1, 5));
    }

    [Fact]
    public void BuildAndMarkAll_IncludeParagraphsInsideNestedTables()
    {
        var nestedCell = new TableCell();
        nestedCell.Paragraphs.Add(new Paragraph
        {
            Runs = { new Run("Alpha"), DocumentIndex.MarkRun(new IndexMark("Nested")) }
        });
        var nestedRow = new TableRow();
        nestedRow.Cells.Add(nestedCell);
        var nestedTable = new Table();
        nestedTable.Rows.Add(nestedRow);
        var middleCell = new TableCell("middle control");
        middleCell.NestedTables.Add(nestedTable);
        var middleRow = new TableRow();
        middleRow.Cells.Add(middleCell);
        var middleTable = new Table();
        middleTable.Rows.Add(middleRow);
        var outerCell = new TableCell("outer control");
        outerCell.NestedTables.Add(middleTable);
        var outerRow = new TableRow();
        outerRow.Cells.Add(outerCell);
        var outerTable = new Table();
        outerTable.Rows.Add(outerRow);
        var doc = new TextDocument();
        doc.Blocks.Add(outerTable);

        DocumentIndex.Build(
                doc,
                pageReferenceOf: _ => new IndexPageReferenceAddress(2, "3"))
            .Select(paragraph => paragraph.PlainText)
            .Should().Equal("N", "Nested, 3");
        DocumentIndex.MarkAllTargets(doc, "Alpha", new IndexMark("Alpha")).Should().Equal(
            new IndexMarkTarget(
                0,
                5,
                new TableParagraphAddress(
                    0,
                    0,
                    ParagraphIndex: -1,
                    NestedTableIndex: 0,
                    NestedParagraph: new TableParagraphAddress(
                        0,
                        0,
                        ParagraphIndex: -1,
                        NestedTableIndex: 0,
                        NestedParagraph: new TableParagraphAddress(0, 0, 0)))));
    }

    [Fact]
    public void Build_BookmarkRangeResolvesBoundaryInsideNestedTable()
    {
        var start = new Paragraph("Range start");
        start.BookmarkNames.Add("NestedRange");
        start.BookmarkBoundaries.Add(new BookmarkBoundary(
            "nested-range", BookmarkBoundaryKind.Start, 0, "NestedRange"));
        var nestedCell = new TableCell();
        nestedCell.Paragraphs.Add(start);
        var nestedRow = new TableRow();
        nestedRow.Cells.Add(nestedCell);
        var nestedTable = new Table();
        nestedTable.Rows.Add(nestedRow);
        var outerCell = new TableCell("outer control");
        outerCell.NestedTables.Add(nestedTable);
        var outerRow = new TableRow();
        outerRow.Cells.Add(outerCell);
        var outerTable = new Table();
        outerTable.Rows.Add(outerRow);
        var end = new Paragraph("Range end");
        end.BookmarkBoundaries.Add(new BookmarkBoundary(
            "nested-range", BookmarkBoundaryKind.End, 0));
        var doc = new TextDocument();
        doc.Blocks.Add(outerTable);
        doc.Blocks.Add(end);
        doc.Blocks.Add(new Paragraph
        {
            Runs = { DocumentIndex.MarkRun(new IndexMark("Alpha", BookmarkName: "NestedRange")) }
        });

        var entry = DocumentIndex.Build(
                doc,
                pageReferenceOf: blockIndex => new IndexPageReferenceAddress(
                    blockIndex,
                    blockIndex == 0 ? "ii" : "iv"))
            .Single(paragraph => paragraph.StyleId == DocumentIndex.EntryStyleId);

        entry.PlainText.Should().Be("Alpha, ii\u2013iv");
    }

    [Fact]
    public void Build_DoesNotMutateTheDocument()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("Body"));
        doc.IndexEntries.Add(new IndexEntry("term"));

        var blocksBefore = doc.Blocks.Count;
        var entriesBefore = doc.IndexEntries.Count;

        DocumentIndex.Build(doc);

        doc.Blocks.Should().HaveCount(blocksBefore);
        doc.IndexEntries.Should().HaveCount(entriesBefore);
    }

    [Fact]
    public void IsIndexStyleId_RecognisesGeneratedStyles()
    {
        DocumentIndex.IsIndexStyleId(DocumentIndex.HeadingStyleId).Should().BeTrue();
        DocumentIndex.IsIndexStyleId(DocumentIndex.EntryStyleId).Should().BeTrue();

        DocumentIndex.IsIndexStyleId(null).Should().BeFalse();
        DocumentIndex.IsIndexStyleId("").Should().BeFalse();
        DocumentIndex.IsIndexStyleId("Normal").Should().BeFalse();
        DocumentIndex.IsIndexStyleId("Heading1").Should().BeFalse();
    }

    [Fact]
    public void IsIndexParagraph_TrueOnlyForIndexStyledParagraphs()
    {
        DocumentIndex.IsIndexParagraph(new Paragraph("x") { StyleId = DocumentIndex.EntryStyleId }).Should().BeTrue();
        DocumentIndex.IsIndexParagraph(new Paragraph("x") { StyleId = DocumentIndex.HeadingStyleId }).Should().BeTrue();
        DocumentIndex.IsIndexParagraph(new Paragraph("x") { StyleId = "Heading1" }).Should().BeFalse();
        DocumentIndex.IsIndexParagraph(Table.Create(1, 1)).Should().BeFalse();
    }

    [Fact]
    public void EnsureStyles_RegistersIndexStylesIdempotently()
    {
        var doc = TextDocument.CreateEmpty();

        DocumentIndex.EnsureStyles(doc);
        DocumentIndex.EnsureStyles(doc); // second call must not throw or duplicate

        doc.Styles.Should().ContainKey(DocumentIndex.HeadingStyleId);
        doc.Styles.Should().ContainKey(DocumentIndex.EntryStyleId);
    }

    [Fact]
    public void EnsureStyles_DoesNotOverwriteAnExistingStyle()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Styles[DocumentIndex.HeadingStyleId] = new DocumentStyle
        {
            Id = DocumentIndex.HeadingStyleId,
            Name = "Custom"
        };

        DocumentIndex.EnsureStyles(doc);

        doc.Styles[DocumentIndex.HeadingStyleId].Name.Should().Be("Custom");
    }

    [Fact]
    public void IndexEntry_TrimsTermAtConstruction()
    {
        new IndexEntry("  hello  ").Term.Should().Be("hello");
    }

    [Fact]
    public void CreateEmpty_RegistersBuiltInIndexStyles()
    {
        var doc = TextDocument.CreateEmpty();

        doc.Styles.Should().ContainKey(DocumentIndex.HeadingStyleId);
        doc.Styles.Should().ContainKey(DocumentIndex.EntryStyleId);
    }
}
