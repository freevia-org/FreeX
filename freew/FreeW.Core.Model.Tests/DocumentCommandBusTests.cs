namespace FreeW.Core.Model.Tests;

public class DocumentCommandBusTests
{
    private sealed class Context(TextDocument document, string? revisionAuthor = null) : IDocumentCommandContext
    {
        public TextDocument Document => document;
        public string? RevisionAuthor => revisionAuthor;
    }

    private static (TextDocument doc, DocumentCommandBus bus) New(string? revisionAuthor = null)
    {
        var doc = new TextDocument();
        return (doc, new DocumentCommandBus(new Context(doc, revisionAuthor)));
    }

    private sealed class ClassifiedCommand(DocumentCommandMutationKind mutationKind) : IDocumentCommand
    {
        public string Label => mutationKind.ToString();
        public DocumentCommandMutationKind MutationKind => mutationKind;
        public void Apply(IDocumentCommandContext context) { }
        public void Revert(IDocumentCommandContext context) { }
    }

    /// <summary>Command whose Apply/Revert can be made to throw on demand, for exercising the
    /// rollback/safety-net paths in <see cref="CompositeDocumentCommand"/> and
    /// <see cref="DocumentCommandBus"/> that a normal always-succeeding command never touches.</summary>
    private sealed class ThrowingCommand(bool throwOnApply = false, bool throwOnRevert = false) : IDocumentCommand
    {
        public string Label => "Throw";
        public bool ThrowOnApply { get; set; } = throwOnApply;
        public bool ThrowOnRevert { get; set; } = throwOnRevert;

        public void Apply(IDocumentCommandContext context)
        {
            if (ThrowOnApply)
                throw new InvalidOperationException("apply boom");
        }

        public void Revert(IDocumentCommandContext context)
        {
            if (ThrowOnRevert)
                throw new InvalidOperationException("revert boom");
        }
    }

    [Fact]
    public void InsertParagraph_Execute_Undo_Redo()
    {
        var (doc, bus) = New();

        bus.Execute(new InsertParagraphCommand(0, new Paragraph("A")));
        doc.PlainText.Should().Be("A");
        bus.CanUndo.Should().BeTrue();

        bus.Undo().Should().BeTrue();
        doc.Blocks.Should().BeEmpty();
        bus.CanRedo.Should().BeTrue();

        bus.Redo().Should().BeTrue();
        doc.PlainText.Should().Be("A");
    }

    [Fact]
    public void NewCommand_InvalidatesRedo()
    {
        var (doc, bus) = New();
        bus.Execute(new InsertParagraphCommand(0, new Paragraph("A")));
        bus.Undo();
        bus.CanRedo.Should().BeTrue();

        bus.Execute(new InsertParagraphCommand(0, new Paragraph("B")));

        bus.CanRedo.Should().BeFalse();
        doc.PlainText.Should().Be("B");
    }

    [Fact]
    public void SetCellParagraphMarkRevision_UsesLogicalColumnAndRestoresPreviousState()
    {
        var (doc, bus) = New();
        var table = Table.Create(1, 2);
        table.Rows[0].Cells[0].GridSpan = 2;
        var paragraph = table.Rows[0].Cells[1].Paragraphs[0];
        paragraph.MarkRevision = RevisionKind.Inserted;
        paragraph.MarkRevisionAuthor = "Before";
        paragraph.MarkRevisionDateXml = "before-date";
        doc.Blocks.Add(table);

        bus.Execute(new SetCellParagraphMarkRevisionCommand(
            0,
            0,
            2,
            0,
            RevisionKind.Deleted,
            "After",
            "after-date"));

        paragraph.MarkRevision.Should().Be(RevisionKind.Deleted);
        paragraph.MarkRevisionAuthor.Should().Be("After");
        paragraph.MarkRevisionDateXml.Should().Be("after-date");

        bus.Undo().Should().BeTrue();
        paragraph.MarkRevision.Should().Be(RevisionKind.Inserted);
        paragraph.MarkRevisionAuthor.Should().Be("Before");
        paragraph.MarkRevisionDateXml.Should().Be("before-date");

        bus.Redo().Should().BeTrue();
        paragraph.MarkRevision.Should().Be(RevisionKind.Deleted);
    }

    [Fact]
    public void RollbackUndoGroup_RevertsAppliedCommandsWithoutCreatingHistory()
    {
        var (doc, bus) = New();
        bus.BeginUndoGroup();
        bus.Execute(new InsertParagraphCommand(0, new Paragraph("A")));
        bus.Execute(new InsertParagraphCommand(1, new Paragraph("B")));

        bus.RollbackUndoGroup();

        doc.Blocks.Should().BeEmpty();
        bus.IsUndoGroupOpen.Should().BeFalse();
        bus.CanUndo.Should().BeFalse();
    }

    // R137-documentcommands-composite-rollback-1 (finding A): a composite command whose Nth
    // child throws mid-Apply must not leave the earlier children applied. Before the fix,
    // CompositeDocumentCommand.Apply had no try/catch, so the first child's edit stuck around
    // in the document even though the whole composite failed and no undo entry was pushed.
    [Fact]
    public void CompositeCommand_SecondChildThrows_RollsBackFirstChild_AndPushesNoUndoEntry()
    {
        var (doc, bus) = New();
        doc.Blocks.Add(new Paragraph("existing"));
        var beforeState = doc.PlainText;

        var first = new InsertParagraphCommand(0, new Paragraph("A"));
        var second = new ThrowingCommand(throwOnApply: true);
        var composite = new CompositeDocumentCommand("Two-step", [first, second]);

        Action act = () => bus.Execute(composite);

        act.Should().Throw<InvalidOperationException>().WithMessage("apply boom");
        // The differentiator: before the fix, "A" from the first child remained inserted.
        doc.PlainText.Should().Be(beforeState);
        doc.Blocks.Should().ContainSingle().Which.Should().BeOfType<Paragraph>();
        bus.CanUndo.Should().BeFalse();
    }

    // Sibling no-regression: a composite whose children all succeed still applies and reverts
    // normally through the same Apply()/Revert() code path exercised above.
    [Fact]
    public void CompositeCommand_AllChildrenSucceed_AppliesAndReverts()
    {
        var (doc, bus) = New();
        var first = new InsertParagraphCommand(0, new Paragraph("A"));
        var second = new InsertParagraphCommand(1, new Paragraph("B"));
        var composite = new CompositeDocumentCommand("Two-step", [first, second]);

        bus.Execute(composite);
        doc.Blocks.Should().HaveCount(2);
        bus.CanUndo.Should().BeTrue();

        bus.Undo().Should().BeTrue();
        doc.Blocks.Should().BeEmpty();

        bus.Redo().Should().BeTrue();
        doc.Blocks.Should().HaveCount(2);
    }

    // R137-documentcommands-undoredo-safetynet-1 (finding B): Undo()/Redo() must restore the
    // shared UndoRedoStack's bookkeeping (via RollbackPopUndo/PushRedo) when the command's own
    // Revert()/Apply() throws, or the entry is left dangling in the wrong stack -- neither
    // undoable nor redoable, or double-counted. Before the fix there was no try/catch at all.
    [Fact]
    public void Undo_WhenRevertThrows_RestoresEntryToUndoStack_AndDoesNotLeaveItOnRedoStack()
    {
        var (_, bus) = New();
        var throwing = new ThrowingCommand(throwOnRevert: true);
        bus.Execute(throwing);
        bus.CanUndo.Should().BeTrue();

        Action act = () => bus.Undo();

        act.Should().Throw<InvalidOperationException>().WithMessage("revert boom");
        // The differentiator: before the fix, PopUndo already moved the entry onto the redo
        // stack and the throw left it stranded there (CanUndo false, CanRedo true).
        bus.CanUndo.Should().BeTrue();
        bus.CanRedo.Should().BeFalse();

        // The command must still be the live top-of-stack entry: a subsequent successful
        // Undo (once the fault clears) reverts it, not some other stale state.
        throwing.ThrowOnRevert = false;
        bus.Undo().Should().BeTrue();
        bus.CanUndo.Should().BeFalse();
        bus.CanRedo.Should().BeTrue();
    }

    [Fact]
    public void Redo_WhenApplyThrows_RestoresEntryToRedoStack_AndDoesNotLoseIt()
    {
        var (_, bus) = New();
        var throwing = new ThrowingCommand();
        bus.Execute(throwing);
        bus.Undo().Should().BeTrue();
        bus.CanRedo.Should().BeTrue();

        throwing.ThrowOnApply = true;
        Action act = () => bus.Redo();

        act.Should().Throw<InvalidOperationException>().WithMessage("apply boom");
        // The differentiator: before the fix, PopRedo already removed the entry from the redo
        // stack and the throw lost it entirely (CanRedo false, CanUndo false too).
        bus.CanRedo.Should().BeTrue();
        bus.CanUndo.Should().BeFalse();

        throwing.ThrowOnApply = false;
        bus.Redo().Should().BeTrue();
        bus.CanRedo.Should().BeFalse();
        bus.CanUndo.Should().BeTrue();
    }

    // Sibling no-regression: normal (non-throwing) Undo/Redo through the new try/catch wrapping
    // still behaves exactly as before.
    [Fact]
    public void Undo_Redo_WhenCommandsDoNotThrow_BehaveNormally()
    {
        var (doc, bus) = New();
        bus.Execute(new InsertParagraphCommand(0, new Paragraph("A")));

        bus.Undo().Should().BeTrue();
        doc.Blocks.Should().BeEmpty();
        bus.CanRedo.Should().BeTrue();

        bus.Redo().Should().BeTrue();
        doc.PlainText.Should().Be("A");
        bus.CanUndo.Should().BeTrue();
    }

    // Trap guard (see round-137 task notes): CommitUndoGroup builds its CompositeDocumentCommand
    // from children that were already applied individually as Execute() collected them -- it
    // never calls the composite's own Apply(). A "track outstanding children via an Apply-only
    // list" fix would leave that list empty for every grouped edit, making Revert() (and
    // therefore one Undo() call) a silent no-op. This pins the correct behavior: one Undo
    // reverts the whole group.
    [Fact]
    public void CommitUndoGroup_ThenOneUndo_RevertsEveryChild()
    {
        var (doc, bus) = New();
        bus.BeginUndoGroup();
        bus.Execute(new InsertParagraphCommand(0, new Paragraph("A")));
        bus.Execute(new InsertParagraphCommand(1, new Paragraph("B")));
        bus.CommitUndoGroup("Insert Two");

        doc.Blocks.Should().HaveCount(2);
        bus.CanUndo.Should().BeTrue();
        bus.CanRedo.Should().BeFalse();

        bus.Undo().Should().BeTrue();

        doc.Blocks.Should().BeEmpty();
        bus.CanUndo.Should().BeFalse();
        bus.CanRedo.Should().BeTrue();

        bus.Redo().Should().BeTrue();
        doc.Blocks.Should().HaveCount(2);
    }

    [Fact]
    public void Next_history_mutation_kind_tracks_undo_and_redo_tops()
    {
        var (_, bus) = New();

        bus.NextUndoMutationKind.Should().BeNull();
        bus.NextRedoMutationKind.Should().BeNull();

        bus.Execute(new ClassifiedCommand(DocumentCommandMutationKind.BodyText));
        bus.Execute(new ClassifiedCommand(DocumentCommandMutationKind.Comment));

        bus.NextUndoMutationKind.Should().Be(DocumentCommandMutationKind.Comment);

        bus.Undo().Should().BeTrue();

        bus.NextUndoMutationKind.Should().Be(DocumentCommandMutationKind.BodyText);
        bus.NextRedoMutationKind.Should().Be(DocumentCommandMutationKind.Comment);
    }

    [Fact]
    public void ReplaceContentControlRun_ClassifiesAsFormField_AndUndoRedoRestoresRuns()
    {
        var (doc, bus) = New();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(Run.CheckBoxControl(@checked: false, tag: "Agree"));
        doc.Blocks.Add(paragraph);

        var command = new ReplaceContentControlRunCommand(0, 0, Run.CheckBoxControl(@checked: true, tag: "Agree"));

        command.MutationKind.Should().Be(DocumentCommandMutationKind.FormField);

        bus.Execute(command);

        paragraph.Runs[0].Control!.Checked.Should().BeTrue();
        paragraph.Runs[0].Text.Should().Be(ContentControl.CheckedGlyph);
        bus.NextUndoMutationKind.Should().Be(DocumentCommandMutationKind.FormField);

        bus.Undo().Should().BeTrue();
        paragraph.Runs[0].Control!.Checked.Should().BeFalse();
        paragraph.Runs[0].Text.Should().Be(ContentControl.UncheckedGlyph);
        bus.NextRedoMutationKind.Should().Be(DocumentCommandMutationKind.FormField);

        bus.Redo().Should().BeTrue();
        paragraph.Runs[0].Control!.Checked.Should().BeTrue();
    }

    [Fact]
    public void DeleteParagraph_Undo_RestoresSameInstance()
    {
        var (doc, bus) = New();
        var p = new Paragraph("keep");
        doc.Blocks.Add(p);

        bus.Execute(new DeleteParagraphCommand(0));
        doc.Blocks.Should().BeEmpty();

        bus.Undo();
        doc.Blocks.Should().ContainSingle().Which.Should().BeSameAs(p);
    }

    [Fact]
    public void ReorderBlocks_AppliesPermutation_AndRevertsToOriginalOrder()
    {
        var (doc, bus) = New();
        var a = new Paragraph("A") { StyleId = "Heading1" };
        var b = new Paragraph("B") { StyleId = "Heading1" };
        doc.Blocks.Add(a);
        doc.Blocks.Add(b);

        // Move the "A" heading-subtree down past "B" through the pure helper, then commit the reorder.
        var reordered = OutlineTools.MoveSubtree(doc.Blocks, 0, moveUp: false);
        bus.Execute(new ReorderBlocksCommand(reordered));

        doc.Blocks.Should().Equal(b, a); // same instances, new order

        bus.Undo();
        doc.Blocks.Should().Equal(a, b);

        bus.Redo();
        doc.Blocks.Should().Equal(b, a);
    }

    [Fact]
    public void FormatParagraphRuns_TogglesBold_AndReverts()
    {
        var (doc, bus) = New();
        var p = new Paragraph();
        p.Runs.Add(new Run("x"));
        p.Runs.Add(new Run("y"));
        doc.Blocks.Add(p);

        bus.Execute(new FormatParagraphRunsCommand(0, f => f with { Bold = true }));
        p.Runs.Should().OnlyContain(r => r.Formatting.Bold);

        bus.Undo();
        p.Runs.Should().OnlyContain(r => !r.Formatting.Bold);
    }

    [Fact]
    public void FormattingCommands_RecordAndUndoTrackedFormattingRevisions()
    {
        var (doc, bus) = New("Ada Reviewer");
        doc.TrackRevisions = true;
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("x"));
        paragraph.Runs.Add(new Run("y"));
        doc.Blocks.Add(paragraph);

        bus.Execute(new SetParagraphFormattingCommand(
            0,
            paragraph.Formatting with { Alignment = TextAlignment.Center }));
        paragraph.ParagraphFormatRevision.Should().NotBeNull();
        paragraph.ParagraphFormatRevision!.PreviousParagraphFormatting.Alignment.Should().Be(TextAlignment.Left);
        paragraph.ParagraphFormatRevision.Author.Should().Be("Ada Reviewer");

        bus.Execute(new FormatParagraphRunsCommand(0, formatting => formatting with { Italic = true }));
        paragraph.Runs.Should().OnlyContain(run => run.FormatRevision != null);
        paragraph.Runs[1].FormatRevision!.PreviousFormatting.Italic.Should().BeFalse();

        bus.Undo().Should().BeTrue();
        paragraph.Runs[0].FormatRevision.Should().BeNull();
        paragraph.Runs[1].FormatRevision.Should().BeNull();

        bus.Undo().Should().BeTrue();
        paragraph.ParagraphFormatRevision.Should().BeNull();
    }

    [Fact]
    public void FormattingCommands_HonorDoNotTrackFormattingPolicy()
    {
        var (doc, bus) = New();
        doc.TrackRevisions = true;
        doc.DoNotTrackFormatting = true;
        var paragraph = new Paragraph("x");
        doc.Blocks.Add(paragraph);

        bus.Execute(new SetParagraphFormattingCommand(
            0,
            paragraph.Formatting with { Alignment = TextAlignment.Right }));
        bus.Execute(new FormatParagraphRunsCommand(0, formatting => formatting with { Bold = true }));

        paragraph.ParagraphFormatRevision.Should().BeNull();
        paragraph.Runs.Should().OnlyContain(run => run.FormatRevision == null);
    }

    [Fact]
    public void SetParagraphFormatting_Applies_AndReverts()
    {
        var (doc, bus) = New();
        doc.Blocks.Add(new Paragraph("p"));
        var centered = ParagraphFormatting.Default with { Alignment = TextAlignment.Center };

        bus.Execute(new SetParagraphFormattingCommand(0, centered));
        doc.Paragraphs.First().Formatting.Alignment.Should().Be(TextAlignment.Center);

        bus.Undo();
        doc.Paragraphs.First().Formatting.Alignment.Should().Be(TextAlignment.Left);
    }

    [Fact]
    public void SetParagraphFormatting_LineSpacing_Applies_AndReverts()
    {
        var (doc, bus) = New();
        doc.Blocks.Add(new Paragraph("p"));
        var original = doc.Paragraphs.First().Formatting;
        var doubled = original with { LineSpacing = 2.0 };

        bus.Execute(new SetParagraphFormattingCommand(0, doubled));
        doc.Paragraphs.First().Formatting.LineSpacing.Should().Be(2.0);

        bus.Undo();
        doc.Paragraphs.First().Formatting.LineSpacing.Should().Be(original.LineSpacing);

        bus.Redo();
        doc.Paragraphs.First().Formatting.LineSpacing.Should().Be(2.0);
    }

    [Fact]
    public void SetParagraphFormatting_SpaceBeforeAfter_Applies_AndReverts()
    {
        var (doc, bus) = New();
        doc.Blocks.Add(new Paragraph("p"));
        var spaced = doc.Paragraphs.First().Formatting with { SpaceBeforePt = 12, SpaceAfterPt = 0 };

        bus.Execute(new SetParagraphFormattingCommand(0, spaced));
        doc.Paragraphs.First().Formatting.SpaceBeforePt.Should().Be(12);
        doc.Paragraphs.First().Formatting.SpaceAfterPt.Should().Be(0);

        bus.Undo();
        doc.Paragraphs.First().Formatting.SpaceBeforePt.Should().Be(0);
        doc.Paragraphs.First().Formatting.SpaceAfterPt.Should().Be(8); // model default
    }

    [Fact]
    public void FormatParagraphRunsCommand_AppliesCharacterBorderAndShadingToEveryRun()
    {
        var (doc, bus) = New();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("one"));
        paragraph.Runs.Add(new Run("two"));
        doc.Blocks.Add(paragraph);
        var border = new ParagraphBorder("#C00000", 0.75, BottomOnly: true);

        bus.Execute(new FormatParagraphRunsCommand(0, f => f with
        {
            CharacterBorder = border,
            CharacterShadingHex = "#D9EAD3",
            CharacterShadingPattern = ShadingPattern.Pct10,
        }));

        paragraph.Runs.Should().OnlyContain(run => run.Formatting.CharacterBorder == border);
        paragraph.Runs.Should().OnlyContain(run => run.Formatting.CharacterShadingHex == "#D9EAD3");
        paragraph.Runs.Should().OnlyContain(run => run.Formatting.CharacterShadingPattern == ShadingPattern.Pct10);

        bus.Undo().Should().BeTrue();
        paragraph.Runs.Should().OnlyContain(run => run.Formatting.CharacterBorder == null);
        paragraph.Runs.Should().OnlyContain(run => run.Formatting.CharacterShadingHex == null);
    }

    [Fact]
    public void StyleCatalogCommand_CapturesCatalogSnapshot_ForUndoRedo()
    {
        var doc = TextDocument.CreateEmpty();
        var bus = new DocumentCommandBus(new Context(doc));
        var originalCount = doc.Styles.Count;

        bus.Execute(new StyleCatalogCommand("New Style", model =>
            StyleManager.CreateStyle(
                model,
                "Callout",
                "Normal",
                RunFormatting.Default with { Bold = true },
                ParagraphFormatting.Default)));

        var created = doc.Styles.Values.Single(style => style.Name == "Callout");
        created.Run.Bold.Should().BeTrue();
        doc.Styles.Should().HaveCount(originalCount + 1);

        bus.Undo().Should().BeTrue();
        doc.Styles.Should().HaveCount(originalCount);
        doc.Styles.Should().NotContainKey(created.Id);

        bus.Redo().Should().BeTrue();
        doc.Styles.Should().ContainKey(created.Id);
        doc.Styles[created.Id].Run.Bold.Should().BeTrue();
    }

    [Fact]
    public void InsertBlock_Table_Execute_Undo_Redo()
    {
        var (doc, bus) = New();
        doc.Blocks.Add(new Paragraph("p"));
        var table = Table.Create(2, 2);

        bus.Execute(new InsertBlockCommand(1, table));
        doc.Blocks.Should().HaveCount(2);
        doc.Blocks[1].Should().BeSameAs(table);

        bus.Undo();
        doc.Blocks.Should().ContainSingle().Which.Should().BeOfType<Paragraph>();

        bus.Redo();
        doc.Blocks[1].Should().BeSameAs(table);
    }

    [Fact]
    public void Undo_WhenEmpty_ReturnsFalse()
    {
        var (_, bus) = New();
        bus.CanUndo.Should().BeFalse();
        bus.Undo().Should().BeFalse();
    }

    [Fact]
    public void InsertTableRow_IncreasesRowCount_AndUndoRestores()
    {
        var (doc, bus) = New();
        var table = Table.Create(2, 3);
        doc.Blocks.Add(table);

        bus.Execute(new InsertTableRowCommand(0, 1));
        table.RowCount.Should().Be(3);
        table.ColumnCount.Should().Be(3);
        table.Rows[1].Cells.Should().HaveCount(3);

        bus.Undo();
        table.RowCount.Should().Be(2);

        bus.Redo();
        table.RowCount.Should().Be(3);
    }

    [Fact]
    public void DeleteTableRow_ReducesRowCount_AndUndoRestoresSameRow()
    {
        var (doc, bus) = New();
        var table = Table.Create(3, 2);
        doc.Blocks.Add(table);
        var middle = table.Rows[1];

        bus.Execute(new DeleteTableRowCommand(0, 1));
        table.RowCount.Should().Be(2);
        table.Rows.Should().NotContain(middle);

        bus.Undo();
        table.RowCount.Should().Be(3);
        table.Rows[1].Should().BeSameAs(middle);
    }

    [Fact]
    public void DeleteTableRow_LastRow_IsNoOp()
    {
        var (doc, bus) = New();
        var table = Table.Create(1, 2);
        doc.Blocks.Add(table);

        bus.Execute(new DeleteTableRowCommand(0, 0));
        table.RowCount.Should().Be(1);

        bus.Undo();
        table.RowCount.Should().Be(1);
    }

    [Fact]
    public void InsertTableColumn_AddsCellToEveryRow_AndUndoRestores()
    {
        var (doc, bus) = New();
        var table = Table.Create(2, 2);
        doc.Blocks.Add(table);

        bus.Execute(new InsertTableColumnCommand(0, 1));
        table.ColumnCount.Should().Be(3);
        table.Rows.Should().OnlyContain(r => r.Cells.Count == 3);

        bus.Undo();
        table.ColumnCount.Should().Be(2);
        table.Rows.Should().OnlyContain(r => r.Cells.Count == 2);
    }

    [Fact]
    public void DeleteTableColumn_ReducesColumnCount_AndUndoRestoresCells()
    {
        var (doc, bus) = New();
        var table = Table.Create(2, 3);
        doc.Blocks.Add(table);
        var keptCell = table.Rows[0].Cells[1];

        bus.Execute(new DeleteTableColumnCommand(0, 1));
        table.ColumnCount.Should().Be(2);
        table.Rows[0].Cells.Should().NotContain(keptCell);

        bus.Undo();
        table.ColumnCount.Should().Be(3);
        table.Rows[0].Cells[1].Should().BeSameAs(keptCell);
    }

    [Fact]
    public void DeleteTableColumn_LastColumn_IsNoOp()
    {
        var (doc, bus) = New();
        var table = Table.Create(2, 1);
        doc.Blocks.Add(table);

        bus.Execute(new DeleteTableColumnCommand(0, 0));
        table.ColumnCount.Should().Be(1);
    }

    [Fact]
    public void SetImageSize_ChangesSize_AndUndoRestores()
    {
        var (doc, bus) = New();
        var image = new InlineImage([1, 2, 3], widthPt: 100, heightPt: 50);
        var paragraph = new Paragraph();
        paragraph.Runs.Add(Run.FromImage(image));
        doc.Blocks.Add(paragraph);

        bus.Execute(new SetImageSizeCommand(0, 0, widthPt: 200, heightPt: 100));
        image.WidthPt.Should().Be(200);
        image.HeightPt.Should().Be(100);

        bus.Undo();
        image.WidthPt.Should().Be(100);
        image.HeightPt.Should().Be(50);

        bus.Redo();
        image.WidthPt.Should().Be(200);
        image.HeightPt.Should().Be(100);
    }

    [Fact]
    public void PictureCoreCommands_AltTextBorderAndReset_AreUndoableAndRedoable()
    {
        var (doc, bus) = New();
        var image = new InlineImage([1, 2, 3], widthPt: 240, heightPt: 120)
        {
            AltText = "Before",
            BorderColorHex = "112233",
            BorderWidthPt = 0.75,
            BorderDash = "dash",
            RotationAngle = 45,
            FlipH = true,
            CropLeft = 0.1,
            BrightnessPct = 20,
            OriginalPixelWidth = 200,
            OriginalPixelHeight = 100,
            ImportedEffects = new ShapeEffectLst { HasGlow = true, GlowRad = 63500, GlowAlpha = 60000 },
        };
        var paragraph = new Paragraph();
        paragraph.Runs.Add(Run.FromImage(image));
        doc.Blocks.Add(paragraph);

        bus.Execute(new SetImageAltTextCommand(0, 0, "  After  "));
        image.AltText.Should().Be("After");
        bus.Undo().Should().BeTrue();
        image.AltText.Should().Be("Before");
        bus.Redo().Should().BeTrue();
        image.AltText.Should().Be("After");

        bus.Execute(new SetImageBorderCommand(0, 0, "ABCDEF", 2.25, "dot"));
        (image.BorderColorHex, image.BorderWidthPt, image.BorderDash)
            .Should().Be(("ABCDEF", 2.25, "dot"));
        bus.Undo().Should().BeTrue();
        (image.BorderColorHex, image.BorderWidthPt, image.BorderDash)
            .Should().Be(("112233", 0.75, "dash"));

        bus.Execute(new ResetImageSizeCommand(0, 0, 150, 75));
        (image.WidthPt, image.HeightPt).Should().Be((150, 75));
        image.RotationAngle.Should().Be(0);
        image.FlipH.Should().BeFalse();
        image.HasCrop.Should().BeFalse();
        image.BrightnessPct.Should().Be(0);
        image.ImportedEffects.Should().BeNull();
        bus.Undo().Should().BeTrue();
        (image.WidthPt, image.HeightPt).Should().Be((240, 120));
        image.RotationAngle.Should().Be(45);
        image.FlipH.Should().BeTrue();
        image.CropLeft.Should().Be(0.1);
        image.BrightnessPct.Should().Be(20);
        image.ImportedEffects.Should().NotBeNull();
        image.ImportedEffects!.GlowRad.Should().Be(63500);
    }

    [Fact]
    public void PictureEffectAndStyleCommands_ClearImportedEffects_AndRestoreThemOnUndo()
    {
        var (doc, bus) = New();
        var image = new InlineImage([1, 2, 3], widthPt: 120, heightPt: 80)
        {
            ImportedEffects = new ShapeEffectLst
            {
                HasShadow = true,
                ShadowBlurRad = 76200,
                ShadowAlpha = 55000,
                HasReflection = true,
                ReflectionScaleY = -100000,
            },
        };
        var paragraph = new Paragraph();
        paragraph.Runs.Add(Run.FromImage(image));
        doc.Blocks.Add(paragraph);

        bus.Execute(new SetImageEffectCommand(0, 0, 2, 5, "4472C4", 1, 0, 0));
        image.ImportedEffects.Should().BeNull();
        bus.Undo().Should().BeTrue();
        image.ImportedEffects.Should().NotBeNull();
        image.ImportedEffects!.ShadowBlurRad.Should().Be(76200);
        image.ImportedEffects.ReflectionScaleY.Should().Be(-100000);
        bus.Redo().Should().BeTrue();
        image.ImportedEffects.Should().BeNull();

        bus.Undo().Should().BeTrue();
        bus.Execute(new SetImageStyleCommand(0, 0, PictureStyleCatalog.Catalog[0]));
        image.ImportedEffects.Should().BeNull();
        bus.Undo().Should().BeTrue();
        image.ImportedEffects.Should().NotBeNull();
        image.ImportedEffects!.ShadowAlpha.Should().Be(55000);
    }

    [Fact]
    public void ArtisticEffectCommand_RestoresBakedPreviewProvenanceOnUndo()
    {
        var (doc, bus) = New();
        var image = new InlineImage([1, 2, 3], widthPt: 120, heightPt: 80)
        {
            ArtisticEffect = ImageArtisticEffect.GlowDiffused,
            HasBakedArtisticEffectPreview = true,
            NativeArtisticSourceBytes = [4, 5, 6],
        };
        var paragraph = new Paragraph();
        paragraph.Runs.Add(Run.FromImage(image));
        doc.Blocks.Add(paragraph);

        bus.Execute(new SetImageArtisticEffectCommand(0, 0, ImageArtisticEffect.Blur));
        image.ArtisticEffect.Should().Be(ImageArtisticEffect.Blur);
        image.HasBakedArtisticEffectPreview.Should().BeFalse();
        image.NativeArtisticSourceBytes.Should().BeNull();

        bus.Undo().Should().BeTrue();
        image.ArtisticEffect.Should().Be(ImageArtisticEffect.GlowDiffused);
        image.HasBakedArtisticEffectPreview.Should().BeTrue();
        image.NativeArtisticSourceBytes.Should().Equal(4, 5, 6);
    }

    [Fact]
    public void PictureStyleCatalog_AllPresetsApplyAndUndoThroughSharedCommand()
    {
        foreach (var preset in PictureStyleCatalog.Catalog)
        {
            var (doc, bus) = New();
            var image = new InlineImage([1, 2, 3], widthPt: 120, heightPt: 80)
            {
                BorderColorHex = "112233",
                BorderWidthPt = 0.75,
                BorderDash = "dash",
                ShadowPreset = 5,
                ReflectionPreset = 2,
                SoftEdgePt = 1.25,
                PictureStylePreset = 99,
            };
            var paragraph = new Paragraph();
            paragraph.Runs.Add(Run.FromImage(image));
            doc.Blocks.Add(paragraph);

            bus.Execute(new SetImageStyleCommand(0, 0, preset));

            PictureStyle(image).Should().Be(PictureStyle(preset));
            bus.Undo().Should().BeTrue();
            PictureStyle(image).Should().Be((99, "112233", 0.75, "dash", 5, 2, 1.25));
            bus.Redo().Should().BeTrue();
            PictureStyle(image).Should().Be(PictureStyle(preset));
        }
    }

    private static (int Id, string? Border, double Width, string? Dash, int Shadow, int Reflection, double SoftEdge)
        PictureStyle(InlineImage image) =>
        (image.PictureStylePreset, image.BorderColorHex, image.BorderWidthPt, image.BorderDash,
            image.ShadowPreset, image.ReflectionPreset, image.SoftEdgePt);

    private static (int Id, string? Border, double Width, string? Dash, int Shadow, int Reflection, double SoftEdge)
        PictureStyle(PictureStylePreset preset) =>
        (preset.Id, preset.BorderColorHex, preset.BorderWidthPt, preset.BorderDash,
            preset.ShadowPreset, preset.ReflectionPreset, preset.SoftEdgePt);

    [Fact]
    public void MergeCellsHorizontal_SetsGridSpan_DropsCells_AndReverts()
    {
        var (doc, bus) = New();
        var table = Table.Create(1, 3);
        table.Rows[0].Cells[0] = new TableCell("a");
        table.Rows[0].Cells[1] = new TableCell("b");
        table.Rows[0].Cells[2] = new TableCell("c");
        doc.Blocks.Add(table);

        bus.Execute(new MergeCellsHorizontalCommand(0, rowIndex: 0, firstColumn: 0, lastColumn: 1));
        table.Rows[0].Cells.Should().HaveCount(2);
        table.Rows[0].Cells[0].GridSpan.Should().Be(2);
        table.Rows[0].Cells[0].PlainText.Should().Be("a");
        table.Rows[0].Cells[1].PlainText.Should().Be("c");

        bus.Undo();
        table.Rows[0].Cells.Should().HaveCount(3);
        table.Rows[0].Cells[0].GridSpan.Should().Be(1);
        table.Rows[0].Cells.Select(c => c.PlainText).Should().Equal("a", "b", "c");

        bus.Redo();
        table.Rows[0].Cells.Should().HaveCount(2);
        table.Rows[0].Cells[0].GridSpan.Should().Be(2);
    }

    [Fact]
    public void MergeCellsVertical_SetsRestartAndContinue_AndReverts()
    {
        var (doc, bus) = New();
        var table = Table.Create(2, 2);
        doc.Blocks.Add(table);

        bus.Execute(new MergeCellsVerticalCommand(0, columnIndex: 0, firstRow: 0, lastRow: 1));
        table.Rows[0].Cells[0].VerticalMerge.Should().Be(VerticalMergeState.Restart);
        table.Rows[1].Cells[0].VerticalMerge.Should().Be(VerticalMergeState.Continue);

        bus.Undo();
        table.Rows[0].Cells[0].VerticalMerge.Should().Be(VerticalMergeState.None);
        table.Rows[1].Cells[0].VerticalMerge.Should().Be(VerticalMergeState.None);

        bus.Redo();
        table.Rows[0].Cells[0].VerticalMerge.Should().Be(VerticalMergeState.Restart);
        table.Rows[1].Cells[0].VerticalMerge.Should().Be(VerticalMergeState.Continue);
    }

    [Fact]
    public void SplitCell_UndoesHorizontalMerge_AndIsReversible()
    {
        var (doc, bus) = New();
        var table = Table.Create(1, 2);
        // Start from a horizontally merged cell (span 2, single cell in the row).
        table.Rows[0].Cells[0] = new TableCell("merged") { GridSpan = 2 };
        table.Rows[0].Cells.RemoveAt(1);
        doc.Blocks.Add(table);

        bus.Execute(new SplitCellCommand(0, rowIndex: 0, columnIndex: 0));
        table.Rows[0].Cells.Should().HaveCount(2);
        table.Rows[0].Cells[0].GridSpan.Should().Be(1);
        table.Rows[0].Cells[1].GridSpan.Should().Be(1);

        bus.Undo();
        table.Rows[0].Cells.Should().HaveCount(1);
        table.Rows[0].Cells[0].GridSpan.Should().Be(2);
    }

    [Fact]
    public void SplitCell_UndoesVerticalMerge_AndIsReversible()
    {
        var (doc, bus) = New();
        var table = Table.Create(2, 1);
        table.Rows[0].Cells[0].VerticalMerge = VerticalMergeState.Restart;
        table.Rows[1].Cells[0].VerticalMerge = VerticalMergeState.Continue;
        doc.Blocks.Add(table);

        bus.Execute(new SplitCellCommand(0, rowIndex: 0, columnIndex: 0));
        table.Rows[0].Cells[0].VerticalMerge.Should().Be(VerticalMergeState.None);
        table.Rows[1].Cells[0].VerticalMerge.Should().Be(VerticalMergeState.None);

        bus.Undo();
        table.Rows[0].Cells[0].VerticalMerge.Should().Be(VerticalMergeState.Restart);
        table.Rows[1].Cells[0].VerticalMerge.Should().Be(VerticalMergeState.Continue);
    }

    [Fact]
    public void SplitCell_SubdividesOrdinaryCellLikeWord_AndIsReversible()
    {
        var (doc, bus) = New();
        var table = Table.Create(2, 2);
        table.Rows[0].Cells[0] = new TableCell("A") { WidthPt = 234 };
        table.Rows[0].Cells[1] = new TableCell("B") { WidthPt = 234 };
        table.Rows[1].Cells[0] = new TableCell("C") { WidthPt = 234 };
        table.Rows[1].Cells[1] = new TableCell("D") { WidthPt = 234 };
        table.ColumnWidthsPt.AddRange([234, 234]);
        doc.Blocks.Add(table);

        bus.Execute(new SplitCellCommand(0, rowIndex: 0, columnIndex: 0, rows: 2, columns: 2));

        table.ColumnWidthsPt.Should().Equal(117, 117, 234);
        table.Rows.Should().HaveCount(3);
        table.Rows[0].Cells.Select(c => c.PlainText).Should().Equal("A", "", "B");
        table.Rows[0].Cells.Select(c => c.GridSpan).Should().Equal(1, 1, 1);
        table.Rows[0].Cells.Select(c => c.VerticalMerge).Should().Equal(
            VerticalMergeState.None, VerticalMergeState.None, VerticalMergeState.Restart);
        table.Rows[1].Cells.Select(c => c.PlainText).Should().Equal("", "", "");
        table.Rows[1].Cells.Select(c => c.VerticalMerge).Should().Equal(
            VerticalMergeState.None, VerticalMergeState.None, VerticalMergeState.Continue);
        table.Rows[2].Cells.Select(c => c.PlainText).Should().Equal("C", "D");
        table.Rows[2].Cells.Select(c => c.GridSpan).Should().Equal(2, 1);

        bus.Undo();
        table.ColumnWidthsPt.Should().Equal(234, 234);
        table.Rows.Should().HaveCount(2);
        table.Rows[0].Cells.Select(c => c.PlainText).Should().Equal("A", "B");
        table.Rows[0].Cells.Select(c => c.VerticalMerge).Should().OnlyContain(state => state == VerticalMergeState.None);
        table.Rows[1].Cells.Select(c => c.PlainText).Should().Equal("C", "D");
        table.Rows[1].Cells.Select(c => c.GridSpan).Should().Equal(1, 1);

        bus.Redo();
        table.ColumnWidthsPt.Should().Equal(117, 117, 234);
        table.Rows.Should().HaveCount(3);
        table.Rows[2].Cells.Select(c => c.GridSpan).Should().Equal(2, 1);
    }

    [Fact]
    public void SplitCell_WithDialogDimensions_PreservesLegacyMergedCellSplit()
    {
        var (doc, bus) = New();
        var table = Table.Create(1, 2);
        table.Rows[0].Cells[0] = new TableCell("merged") { GridSpan = 2 };
        table.Rows[0].Cells.RemoveAt(1);
        doc.Blocks.Add(table);

        bus.Execute(new SplitCellCommand(0, rowIndex: 0, columnIndex: 0, rows: 1, columns: 2));

        table.Rows[0].Cells.Should().HaveCount(2);
        table.Rows[0].Cells.Should().OnlyContain(cell => cell.GridSpan == 1);
        bus.Undo();
        table.Rows[0].Cells.Should().ContainSingle().Which.GridSpan.Should().Be(2);
    }
}
