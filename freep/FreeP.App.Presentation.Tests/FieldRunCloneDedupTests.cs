using System.IO;
using FreeP.App.Compositor;

namespace FreeP.App.Compositor.Tests;

public sealed class FieldRunCloneDedupTests
{
    [Fact]
    public void Clone_PreservesEveryFieldPropertyInDetachedInstance()
    {
        var source = CreateField();

        var clone = source.Clone();

        clone.Should().NotBeSameAs(source);
        clone.Should().BeEquivalentTo(source);
    }

    [Fact]
    public void CoreAndPresentationRunCloners_UseDetachedFieldClone()
    {
        var sourceField = CreateField();
        var sourceRun = new Run { Text = "Field", Field = sourceField };
        var shape = new SlideShape
        {
            Id = 1,
            TextBody = new TextBody
            {
                Paragraphs =
                {
                    new Paragraph { Runs = { sourceRun } },
                },
            },
        };

        var coreClone = SlideCloner.CloneShape(shape).TextBody!.Paragraphs[0].Runs[0].Field;
        var presentationClone = TextBodyModelCloner.CloneRun(sourceRun).Field;

        coreClone.Should().NotBeSameAs(sourceField);
        coreClone.Should().BeEquivalentTo(sourceField);
        presentationClone.Should().NotBeSameAs(sourceField);
        presentationClone.Should().BeEquivalentTo(sourceField);
    }

    [Fact]
    public void CoreAndPresentationCallers_DoNotReintroduceFieldPropertyCopyBlocks()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        var coreSource = File.ReadAllText(Path.Combine(
            root,
            "freep",
            "FreeP.Core.Model",
            "PresentationModelCloneHelper.cs"));
        var presentationSource = File.ReadAllText(Path.Combine(
            root,
            "freep",
            "FreeP.App.Presentation",
            "InCanvasTextEditPlanner.cs"));
        var textCloneSource = File.ReadAllText(Path.Combine(
            root,
            "freep",
            "FreeP.Core.Model",
            "TextBodyModelCloner.cs"));

        textCloneSource.Should().Contain("Field = source.Field?.Clone(),");
        coreSource.Should().NotContain("Field = source.Field?.Clone(),");
        presentationSource.Should().NotContain("Field = source.Field?.Clone(),");
        coreSource.Should().NotContain("private static FieldRun? CloneField");
        presentationSource.Should().NotContain("private static FieldRun? CloneField");
        textCloneSource.Should().NotContain("private static FieldRun? CloneField");
        coreSource.Should().NotContain("FieldType = source.FieldType");
        presentationSource.Should().NotContain("FieldType = source.FieldType");
        textCloneSource.Should().NotContain("FieldType = source.FieldType");
    }

    private static FieldRun CreateField() => new()
    {
        FieldType = "datetime14",
        Id = "{B6591082-5E44-4B12-AC92-A3A1E8C3F923}",
        Dirty = true,
        Language = "en-US",
        AlternateLanguage = "fr-FR",
        RunDirty = false,
        NoProof = true,
        Error = false,
        Kumimoji = true,
        SmartTagClean = false,
        NormalizeHeight = true,
        CharacterSpacingHundredthsPt = 125,
        KerningThresholdHundredthsPt = 900,
        BaselineOffset = 2500,
        RightToLeft = false,
        Caps = RunTextCaps.Small,
        BoldSet = true,
        ItalicSet = true,
        Instruction = "DATE \\@ yyyy-MM-dd",
        CachedText = "2026-08-10",
        FontFamily = "Aptos",
        FontSizePt = 18,
        Bold = true,
        Italic = true,
        UnderlineStyleToken = "sng",
        StrikeStyleToken = "sngStrike",
        Underline = true,
        Strikethrough = true,
        Color = new SrgbColor(12, 34, 56),
    };
}
