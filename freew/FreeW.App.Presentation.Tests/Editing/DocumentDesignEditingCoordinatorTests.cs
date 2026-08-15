using FreeW.App.Presentation.Dialogs;
using FreeW.App.Presentation.Editing;

namespace FreeW.App.Presentation.Tests.Editing;

public sealed class DocumentDesignEditingCoordinatorTests
{
    [Fact]
    public void PageSurfaceMutationsNormalizeAndRestoreCompleteState()
    {
        var document = TextDocument.CreateEmpty();
        document.Page.Watermark = "OLD";
        var session = SessionWith(document);
        var changeCount = 0;
        session.Changed += () => changeCount++;

        var colorResult = session.Design.SetPageColor(" abcdef ");

        colorResult.Should().Be(new DocumentDesignEditResult(true, "Page Color"));
        document.Page.BackgroundColorHex.Should().Be("#abcdef");
        changeCount.Should().Be(1);
        session.Commands.Undo().Should().BeTrue();
        document.Page.BackgroundColorHex.Should().BeNull();

        var watermarkResult = session.Design.SetWatermarkText(" DRAFT ");

        watermarkResult.Label.Should().Be("Watermark");
        document.Page.Watermark.Should().BeNull();
        document.Page.WatermarkOptions.Should().NotBeNull();
        document.Page.WatermarkOptions!.Text.Should().Be("DRAFT");
        session.Commands.Undo().Should().BeTrue();
        document.Page.Watermark.Should().Be("OLD");
        document.Page.WatermarkOptions.Should().BeNull();

        session.Design.TogglePageBorder("#123456", 2).Label.Should().Be("Page Border");
        document.Page.PageBorder.Should().Be(new PageBorder("#123456", 2));
        session.Commands.Undo().Should().BeTrue();
        document.Page.PageBorder.Should().BeNull();
    }

    [Fact]
    public void CatalogAndDocumentPropertyMutationsAreSingleSharedUndoEntries()
    {
        var document = TextDocument.CreateEmpty();
        var originalTheme = document.Theme;
        var session = SessionWith(document);

        session.Design.ApplyTheme(DocumentTheme.Catalog[1])
            .Should().Be(new DocumentDesignEditResult(true, "Apply Theme"));
        document.Theme.Should().Be(DocumentTheme.Catalog[1]);
        session.Commands.Undo().Should().BeTrue();
        document.Theme.Should().Be(originalTheme);
        session.Commands.CanUndo.Should().BeFalse();

        var values = DocumentPropertiesDialogValues.FromInput(
            " Title ",
            " Author ",
            null,
            null,
            null,
            null,
            null,
            " en-US ",
            null);

        session.Design.ApplyDocumentProperties(values).Label.Should().Be("Document Properties");
        document.Properties.Title.Should().Be("Title");
        document.Properties.Author.Should().Be("Author");
        document.Properties.Language.Should().Be("en-US");
        session.Commands.Undo().Should().BeTrue();
        document.Properties.Title.Should().BeNull();
        document.Properties.Author.Should().BeNull();
    }

    [Fact]
    public void PageSettingsInputIsSnapshottedBeforeEnteringHistory()
    {
        var document = TextDocument.CreateEmpty();
        var session = SessionWith(document);
        var settings = document.Page.Clone();
        settings.WidthPt = 700;

        session.Design.SetPageSettings(settings);
        settings.WidthPt = 900;

        document.Page.WidthPt.Should().Be(700);
        session.Commands.Undo().Should().BeTrue();
        session.Commands.Redo().Should().BeTrue();
        document.Page.WidthPt.Should().Be(700);
    }

    [Fact]
    public void DesignPreviewSwitchingAndCancellationRestoreTheCompleteBaselineWithoutUndo()
    {
        var document = TextDocument.CreateEmpty();
        var session = SessionWith(document);
        var originalTheme = document.Theme;
        var originalDefaultRun = document.DefaultRun;
        var originalDefaultParagraph = document.DefaultParagraph;
        var originalStyles = document.Styles.ToDictionary(
            pair => pair.Key,
            pair => (pair.Value, pair.Value.Run, pair.Value.Paragraph));

        session.Design.PreviewTheme(DocumentTheme.Catalog[^1]);
        session.Design.HasActivePreview.Should().BeTrue();
        document.Theme.Should().Be(DocumentTheme.Catalog[^1]);

        session.Design.PreviewThemeColors(DocumentTheme.Catalog[1]);
        session.Design.PreviewStyleSet(DocumentStyleSet.Catalog[^1]);
        session.Design.PreviewFontSet(DocumentFontSet.Catalog[^1]);
        session.Design.PreviewParagraphSpacingSet(DocumentParagraphSpacingSet.Catalog[^1]);
        session.Design.PreviewEffectSet(DocumentEffectSet.Catalog[^1]);

        session.Commands.CanUndo.Should().BeFalse();
        session.Design.CancelPreview().Should().BeTrue();
        session.Design.CancelPreview().Should().BeFalse();
        session.Design.HasActivePreview.Should().BeFalse();
        document.Theme.Should().Be(originalTheme);
        document.DefaultRun.Should().Be(originalDefaultRun);
        document.DefaultParagraph.Should().Be(originalDefaultParagraph);
        document.Styles.Keys.Should().BeEquivalentTo(originalStyles.Keys);
        foreach (var (styleId, snapshot) in originalStyles)
        {
            document.Styles[styleId].Should().BeSameAs(snapshot.Value);
            document.Styles[styleId].Run.Should().Be(snapshot.Run);
            document.Styles[styleId].Paragraph.Should().Be(snapshot.Paragraph);
        }
    }

    [Fact]
    public void CommittingDuringPreviewRevertsFirstSoUndoReturnsToTheTrueBaseline()
    {
        var document = TextDocument.CreateEmpty();
        var originalTheme = document.Theme;
        var session = SessionWith(document);
        var preview = DocumentTheme.Catalog[^1];
        var committed = DocumentTheme.Catalog[1];

        session.Design.PreviewTheme(preview);
        document.Theme.Should().Be(preview);

        session.Design.ApplyTheme(committed);

        session.Design.HasActivePreview.Should().BeFalse();
        document.Theme.Should().Be(committed);
        session.Commands.Undo().Should().BeTrue();
        document.Theme.Should().Be(originalTheme);
    }

    [Fact]
    public void LoadingAnotherDocumentCancelsTheOldDocumentPreviewBeforeReplacement()
    {
        var original = TextDocument.CreateEmpty();
        var originalTheme = original.Theme;
        var replacement = TextDocument.CreateEmpty();
        var replacementTheme = replacement.Theme;
        var session = SessionWith(original);

        session.Design.PreviewTheme(DocumentTheme.Catalog[^1]);
        session.LoadDocument(replacement);

        original.Theme.Should().Be(originalTheme);
        replacement.Theme.Should().Be(replacementTheme);
        session.Design.HasActivePreview.Should().BeFalse();
        session.Document.Should().BeSameAs(replacement);
    }

    private static DocumentEditingSession SessionWith(TextDocument document)
    {
        var session = new DocumentEditingSession();
        session.LoadDocument(document);
        return session;
    }
}

public sealed class DocumentWordArtCommandOwnershipTests
{
    [Fact]
    public void DefaultInsertionAndFormattingCommandsArePortableAndUndoable()
    {
        var document = TextDocument.CreateEmpty();
        var paragraph = document.Blocks.OfType<Paragraph>().Single();
        var session = new DocumentEditingSession();
        session.LoadDocument(document);
        var wordArt = DocumentObjectEditingCoordinator.PlanWordArtInsertion();

        wordArt.Text.Should().Be("WordArt");
        wordArt.Style.Should().Be(WordArtStyle.GradientFill);
        session.Objects.InsertObjectRun(0, Run.FromWordArt(wordArt)).Should().BeTrue();

        var runIndex = paragraph.Runs.Count - 1;
        paragraph.Runs[runIndex].WordArt.Should().BeSameAs(wordArt);
        var target = new DocumentObjectTarget(0, runIndex);

        session.Objects.SetWordArtStyle(target, WordArtStyle.GlowGold).Applied.Should().BeTrue();
        wordArt.Style.Should().Be(WordArtStyle.GlowGold);
        session.Objects.SetWordArtWarp(target, WordArtWarp.ArchUp).Applied.Should().BeTrue();
        wordArt.Warp.Should().Be(WordArtWarp.ArchUp);
        session.Objects.SetAltText(target, "  heading art  ").Applied.Should().BeTrue();
        wordArt.AltText.Should().Be("heading art");

        session.Commands.Undo().Should().BeTrue();
        wordArt.AltText.Should().BeNull();
        session.Commands.Undo().Should().BeTrue();
        wordArt.Warp.Should().Be(WordArtWarp.None);
        session.Commands.Undo().Should().BeTrue();
        wordArt.Style.Should().Be(WordArtStyle.GradientFill);
        session.Commands.Undo().Should().BeTrue();
        paragraph.Runs.Should().NotContain(run => run.WordArt != null);
    }

    [Fact]
    public void ObjectInsertionRejectsTextRunsAndInvalidParagraphs()
    {
        var document = TextDocument.CreateEmpty();
        var session = new DocumentEditingSession();
        session.LoadDocument(document);

        session.Objects.InsertObjectRun(0, new Run("text")).Should().BeFalse();
        session.Objects.InsertObjectRun(5, Run.FromWordArt(
            DocumentObjectEditingCoordinator.PlanWordArtInsertion())).Should().BeFalse();
        session.Commands.CanUndo.Should().BeFalse();
    }
}
