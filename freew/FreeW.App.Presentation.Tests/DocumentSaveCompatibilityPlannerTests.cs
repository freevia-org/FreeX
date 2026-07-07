using Free.Shared.IO;
using FreeW.App.Presentation.Shell;
using FreeW.Core.IO;

namespace FreeW.App.Presentation.Tests;

public sealed class DocumentSaveCompatibilityPlannerTests
{
    [Fact]
    public void Build_NativeDocxWithoutRiskyContent_ReturnsNoWarning()
    {
        var document = TextDocument.CreateEmpty();
        var target = ResolveTarget("Native.docx");

        var plan = DocumentSaveCompatibilityPlanner.Build(document, target);

        plan.RequiresConfirmation.Should().BeFalse();
        plan.Warnings.Should().BeEmpty();
        plan.TargetLabel.Should().Be("Word Document (*.docx)");
    }

    [Fact]
    public void Build_NonMacroDocxWithPreservedMacroProject_WarnsBeforeDroppingMacros()
    {
        var document = TextDocument.CreateEmpty();
        document.Preserved.Parts.Add(new PreservedPart(
            "/word/vbaProject.bin",
            [1, 2, 3],
            "application/vnd.ms-office.vbaProject",
            "http://schemas.microsoft.com/office/2006/relationships/vbaProject"));
        var target = ResolveTarget("MacroCopy.docx");

        var plan = DocumentSaveCompatibilityPlanner.Build(document, target);

        plan.RequiresConfirmation.Should().BeTrue();
        plan.ContinueButtonText.Should().Be(DocumentSaveCompatibilityPlanner.ContinueButtonText);
        plan.CancelButtonText.Should().Be(DocumentSaveCompatibilityPlanner.CancelButtonText);
        plan.Warnings.Select(warning => warning.Kind)
            .Should()
            .Equal(DocumentSaveCompatibilityWarningKind.MacroProject);
        plan.Message.Should().Contain("VBA macro project parts");
        plan.Message.Should().Contain("Choose Continue");
    }

    [Fact]
    public void Build_MacroEnabledDocmWithPreservedMacroProject_ReturnsNoWarning()
    {
        var document = TextDocument.CreateEmpty();
        document.Preserved.Parts.Add(new PreservedPart("/word/vbaProject.bin", [1, 2, 3]));
        var target = ResolveTarget("MacroCopy.docm");

        var plan = DocumentSaveCompatibilityPlanner.Build(document, target);

        plan.RequiresConfirmation.Should().BeFalse();
    }

    [Fact]
    public void Build_PlainTextWithMixedContent_ListsFeatureLossWarningsDeterministically()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        var paragraph = new Paragraph();
        paragraph.Formatting = paragraph.Formatting with { Alignment = TextAlignment.Center };
        paragraph.BookmarkName = "Intro";
        paragraph.Runs.Add(new Run("Heading", RunFormatting.Default with { Bold = true }));
        paragraph.Runs.Add(Run.PageNumberField());
        paragraph.Runs.Add(Run.PlainTextControl("Name", "name", "Name"));
        paragraph.Runs.Add(Run.FootnoteReference(1));
        document.Blocks.Add(paragraph);
        document.Blocks.Add(Table.Create(1, 1));
        document.Blocks.Add(new Paragraph
        {
            Runs =
            {
                Run.FromImage(new InlineImage([0x89, 0x50, 0x4E, 0x47], widthPt: 24, heightPt: 24)),
            },
        });
        document.Footnotes[1] = new Footnote(1, "Footnote");
        document.Comments[0] = new Comment(0, "Review note");
        document.Protection = new ProtectionSettings(ProtectionMode.CommentsOnly);
        document.Header = new HeaderFooter("Header");
        var target = ResolveTarget("Plain.txt");

        var plan = DocumentSaveCompatibilityPlanner.Build(document, target);

        plan.RequiresConfirmation.Should().BeTrue();
        plan.Warnings.Select(warning => warning.Kind).Should().Equal(
            DocumentSaveCompatibilityWarningKind.TextOnlyTarget,
            DocumentSaveCompatibilityWarningKind.ReviewAndProtection,
            DocumentSaveCompatibilityWarningKind.FieldsAndReferences,
            DocumentSaveCompatibilityWarningKind.FootnotesAndEndnotes,
            DocumentSaveCompatibilityWarningKind.Tables,
            DocumentSaveCompatibilityWarningKind.DrawingsChartsSmartArtAndImages,
            DocumentSaveCompatibilityWarningKind.ContentControls,
            DocumentSaveCompatibilityWarningKind.HeadersAndFooters,
            DocumentSaveCompatibilityWarningKind.RichFormatting);
        plan.Message.Should().Contain("Plain text keeps only characters");
        plan.Message.Should().Contain("Choose Continue to write this file anyway");
    }

    [Fact]
    public void Build_PlainTextWithBlockLevelContentControl_WarnsAboutContentControls()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph("References")
        {
            BlockContentControl = BlockContentControl.BibliographyRegion(),
        });
        var target = ResolveTarget("Plain.txt");

        var plan = DocumentSaveCompatibilityPlanner.Build(document, target);

        plan.RequiresConfirmation.Should().BeTrue();
        plan.Warnings.Select(warning => warning.Kind).Should().Equal(
            DocumentSaveCompatibilityWarningKind.TextOnlyTarget,
            DocumentSaveCompatibilityWarningKind.ContentControls);
    }

    [Fact]
    public void Build_Word2003XmlUsesSelectedFormatMetadataInsteadOfXmlExtension()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks[0] = new Paragraph
        {
            Runs =
            {
                Run.FromChart(new Chart { WidthPt = 120, HeightPt = 80 }),
            },
        };
        var workflow = new DocumentPersistenceWorkflow();
        var flatOpcIndex = SaveFilterIndex(workflow, "Word XML Document");
        var word2003Index = SaveFilterIndex(workflow, "Word 2003 XML Document");

        workflow.TryResolveSaveTarget("Package.xml", flatOpcIndex, out var flatOpcTarget).Should().BeTrue();
        workflow.TryResolveSaveTarget("Compat.xml", word2003Index, out var word2003Target).Should().BeTrue();

        var flatOpcPlan = DocumentSaveCompatibilityPlanner.Build(document, flatOpcTarget);
        var word2003Plan = DocumentSaveCompatibilityPlanner.Build(document, word2003Target);

        flatOpcPlan.RequiresConfirmation.Should().BeFalse();
        word2003Plan.RequiresConfirmation.Should().BeTrue();
        word2003Plan.TargetLabel.Should().Be("Word 2003 XML Document (*.xml)");
        word2003Plan.Warnings.Select(warning => warning.Kind).Should().ContainInOrder(
            DocumentSaveCompatibilityWarningKind.CompatibilityTarget,
            DocumentSaveCompatibilityWarningKind.DrawingsChartsSmartArtAndImages);
    }

    [Fact]
    public void Build_ReadOnlyTarget_WarnsUnsupported()
    {
        var adapter = new FakeDocumentAdapter(
            [new FileFormatDescriptor(".pdf", "PDF Document", CanOpen: true, CanSave: false)]);
        var target = new DocumentSaveTarget(
            "ReadOnly.pdf",
            adapter,
            adapter.Formats[0]);

        var plan = DocumentSaveCompatibilityPlanner.Build(TextDocument.CreateEmpty(), target);

        plan.RequiresConfirmation.Should().BeTrue();
        plan.Warnings.Should().ContainSingle(warning =>
            warning.Kind == DocumentSaveCompatibilityWarningKind.UnsupportedTarget);
    }

    private static DocumentSaveTarget ResolveTarget(string path)
    {
        var workflow = new DocumentPersistenceWorkflow();
        workflow.TryResolveCurrentSaveTarget(path, out var target).Should().BeTrue();
        return target;
    }

    private static int SaveFilterIndex(DocumentPersistenceWorkflow workflow, string formatName) =>
        workflow.SaveFormats
            .Select((format, index) => new { format.FormatName, Index = index + 1 })
            .Single(row => row.FormatName == formatName)
            .Index;

    private sealed class FakeDocumentAdapter(IReadOnlyList<FileFormatDescriptor> formats) : IDocumentFileAdapter
    {
        public string Extension => formats[0].Extension;

        public string FormatName => formats[0].FormatName;

        public IReadOnlyList<FileFormatDescriptor> Formats => formats;

        public TextDocument Load(Stream stream) => TextDocument.CreateEmpty();

        public void Save(TextDocument document, Stream stream) { }
    }
}
