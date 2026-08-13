using System.IO;

namespace FreeP.App.Compositor.Tests;

public sealed class TextBodyModelClonerTests
{
    [Fact]
    public void CloneRun_PreservesFieldIdentityAndDetachesRichPayloads()
    {
        var source = CreateRichRun();

        var clone = TextBodyModelCloner.CloneRun(source);

        clone.Should().NotBeSameAs(source);
        clone.Field.Should().NotBeSameAs(source.Field);
        clone.Field!.Id.Should().Be(source.Field!.Id);
        clone.Field.CachedText.Should().Be(source.Field.CachedText);
        clone.InlineImage.Should().NotBeSameAs(source.InlineImage);
        clone.InlineImage!.Bytes.Should().Equal(source.InlineImage!.Bytes);
        clone.InlineImage.Bytes.Should().NotBeSameAs(source.InlineImage.Bytes);
        clone.InlineOleObject.Should().NotBeSameAs(source.InlineOleObject);
        clone.InlineOleObject!.EmbeddedBytes.Should().NotBeSameAs(source.InlineOleObject!.EmbeddedBytes);
        clone.Hyperlink.Should().NotBeSameAs(source.Hyperlink);
        clone.TextShadow.Should().NotBeSameAs(source.TextShadow);
        clone.Math.Should().NotBeSameAs(source.Math);
        clone.Math!.ContainingProperties.Should().Be(source.Math!.ContainingProperties);
    }

    [Fact]
    public void CloneRunWithText_SlicedFragmentKeepsFormattingAndDropsAtomicPayloads()
    {
        var source = CreateRichRun();

        var fragment = TextBodyModelCloner.CloneRunWithText(source, "part");

        fragment.Text.Should().Be("part");
        fragment.Bold.Should().BeTrue();
        fragment.Hyperlink.Should().BeSameAs(source.Hyperlink);
        fragment.TextFill.Should().BeSameAs(source.TextFill);
        fragment.Field.Should().BeNull();
        fragment.Math.Should().BeNull();
        fragment.InlineImage.Should().BeNull();
        fragment.InlineOleObject.Should().BeNull();
        fragment.InlineTable.Should().BeNull();
    }

    [Fact]
    public void CloneTextBody_PreservesBodyAndParagraphMetadataAsDetachedSnapshot()
    {
        var source = new TextBody
        {
            AutoFitKind = TextAutoFitKind.Normal,
            Text3dEffects = new ShapeEffects
            {
                HasGlow = true,
                GlowRadiusEmu = 12700,
            },
            LstStyle = new TextStyleLevels(),
        };
        source.LstStyle[0] = new TextStyleLevel { Bold = true };
        var paragraph = new Paragraph
        {
            AutoNumStartAt = 4,
            AutoNumStartAtSpecified = true,
            BulletImage = new ImagePart
            {
                Bytes = [1, 2, 3],
                ContentType = "image/png",
            },
        };
        paragraph.TabStops.Add(new TabStop { PositionEmu = 914400 });
        paragraph.Runs.Add(CreateRichRun());
        source.Paragraphs.Add(paragraph);

        var clone = TextBodyModelCloner.CloneTextBody(source)!;

        clone.Should().NotBeSameAs(source);
        clone.Text3dEffects.Should().NotBeSameAs(source.Text3dEffects);
        clone.Text3dEffects!.GlowRadiusEmu.Should().Be(12700);
        clone.LstStyle.Should().NotBeSameAs(source.LstStyle);
        clone.LstStyle![0]!.Bold.Should().BeTrue();
        clone.Paragraphs[0].Should().NotBeSameAs(paragraph);
        clone.Paragraphs[0].BulletImage.Should().NotBeSameAs(paragraph.BulletImage);
        clone.Paragraphs[0].TabStops[0].Should().NotBeSameAs(paragraph.TabStops[0]);
    }

    [Fact]
    public void SplitRunsAtSelection_UsesModelOwnedFragmentSemantics()
    {
        var body = new TextBody();
        body.Paragraphs.Add(new Paragraph { Runs = { CreateRichRun() } });

        var selected = TextBodyRunMutator.SplitRunsAtSelection(body, 1, 3);

        body.Paragraphs[0].Runs.Select(run => run.Text).Should().Equal("F", "ie", "ld");
        selected.Should().ContainSingle();
        body.Paragraphs[0].Runs.Should().OnlyContain(run => run.Field == null && run.Math == null);
    }

    [Fact]
    public void SetShapeTextCommand_OwnsDetachedApplyAndUndoSnapshots()
    {
        var presentation = new Presentation();
        var slide = new Slide();
        var original = new TextBody
        {
            Paragraphs = { new Paragraph { Runs = { new Run { Text = "Original" } } } },
        };
        var replacement = new TextBody
        {
            Paragraphs = { new Paragraph { Runs = { new Run { Text = "Replacement" } } } },
        };
        var shape = new SlideShape { Id = 7, TextBody = original };
        slide.Shapes.Add(shape);
        presentation.Slides.Add(slide);
        var command = new SetShapeTextCommand(0, shape.Id, replacement);
        replacement.Paragraphs[0].Runs[0].Text = "Caller mutation";

        command.Apply(presentation);
        shape.TextBody!.Paragraphs[0].Runs[0].Text.Should().Be("Replacement");
        shape.TextBody.Paragraphs[0].Runs[0].Text = "Live mutation";

        command.Revert(presentation);
        shape.TextBody!.Should().NotBeSameAs(original);
        shape.TextBody.Paragraphs[0].Runs[0].Text.Should().Be("Original");
    }

    [Fact]
    public void CloneAndMutationOwnership_LivesInCoreModel()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        var core = File.ReadAllText(Path.Combine(root, "freep", "FreeP.Core.Model", "TextBodyModelCloner.cs"));
        var presentation = File.ReadAllText(Path.Combine(root, "freep", "FreeP.App.Presentation", "InCanvasTextEditPlanner.cs"));
        var tablePlanner = File.ReadAllText(Path.Combine(root, "freep", "FreeP.App.Presentation", "TableCellEditPlanner.cs"));
        var commands = File.ReadAllText(Path.Combine(root, "freep", "FreeP.Core.Model", "PresentationCommands.cs"));
        var wpf = File.ReadAllText(Path.Combine(root, "freep", "FreeP.App.Rendering.Wpf", "TextBodyFlowDocumentConverter.cs"));
        var avalonia = File.ReadAllText(Path.Combine(root, "freep", "FreeP.App.Rendering.Avalonia", "AvaloniaRichTextEditor.cs"));

        core.Should().Contain("public static class TextBodyModelCloner");
        core.Should().Contain("public static class TextBodyRunMutator");
        presentation.Should().NotContain("class TextBodyModelCloner");
        presentation.Should().Contain("TextBodyRunMutator.SplitRunsAtSelection");
        tablePlanner.Should().NotContain("private static List<Run> SplitRunsAtSelection");
        commands.Should().Contain("TextBodyModelCloner.CloneTextBody");
        wpf.Should().Contain("TextBodyModelCloner.CloneParagraphMetadata");
        avalonia.Should().Contain("InCanvasRichTextEditBuffer");
    }

    private static Run CreateRichRun() => new()
    {
        Text = "Field",
        Bold = true,
        BoldSet = true,
        Field = new FieldRun
        {
            Id = "{A37B32D5-C3E6-4B65-B558-246B81419E4C}",
            FieldType = "slidenum",
            CachedText = "Field",
        },
        InlineImage = new ImagePart
        {
            Bytes = [4, 5, 6],
            ContentType = "image/png",
        },
        InlineOleObject = new InlineOleObjectInfo
        {
            EmbeddedBytes = [7, 8, 9],
            FileName = "Object.bin",
        },
        InlineTable = new InlineTableInfo(),
        Hyperlink = new Hyperlink { Url = "https://example.test" },
        TextFill = new ShapeFill.Solid(SrgbColor.FromRgb(0x123456)),
        TextShadow = new RunTextShadow { DirDeg = 135 },
        Math = new MathRunInfo
        {
            RawXml = "<m:oMath />",
            ContainingProperties = new OmmlMathProperties(MathFontFamily: "Cambria Math"),
        },
    };
}
