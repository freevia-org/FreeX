using FreeW.App.Presentation.Documents;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Tests;

public sealed class FreeWSampleDocumentFactoryTests
{
    [Fact]
    public void ClassicProfilePreservesTheWpfStarterDocumentContract()
    {
        var document = FreeWSampleDocumentFactory.Create(FreeWSampleDocumentProfile.ClassicEditor);

        document.Blocks.Should().HaveCount(4);
        var paragraphs = document.Blocks.OfType<Paragraph>().ToArray();
        paragraphs.Select(paragraph => paragraph.PlainText)
            .Should().ContainInOrder("Welcome to FreeW", "A free word processor");
        paragraphs[2].PlainText.Should().StartWith("This document is rendered from the FreeW model.");
        paragraphs[3].PlainText.Should().Be("Centered paragraph.");
        var intro = paragraphs[2];
        intro.Runs.Single(run => run.Text == "bold").Formatting.Bold.Should().BeTrue();
        intro.Runs.Single(run => run.Text == "italic").Formatting.Italic.Should().BeTrue();
        intro.Runs.Single(run => run.Text == "underline").Formatting.Underline.Should().BeTrue();
        intro.Runs.Single(run => run.Text == "colour").Formatting.ColorHex.Should().Be("#C0504D");
    }

    [Fact]
    public void FeatureShowcasePreservesTheAvaloniaStarterDocumentContract()
    {
        var document = FreeWSampleDocumentFactory.Create(FreeWSampleDocumentProfile.FeatureShowcase);

        document.PlainText.Should().Contain("Welcome to FreeW");
        document.PlainText.Should().Contain("now running natively on Linux through Avalonia");
        document.Blocks.OfType<Table>().Should().ContainSingle();
        var imageRuns = document.Blocks.OfType<Paragraph>()
            .SelectMany(paragraph => paragraph.Runs)
            .Where(run => run.Image is not null)
            .ToArray();
        imageRuns.Should().ContainSingle();
        imageRuns[0].Image!.AltText.Should().Be("FreeW sample image");
        document.Styles["Heading1"].Run.Bold.Should().BeTrue();
    }

    [Fact]
    public void HostsKeepOnlyProfileSelectionAndDoNotBuildSampleModels()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var wpf = File.ReadAllText(Path.Combine(root, "freew", "FreeW.App.Host", "MainWindow.cs"));
        var avalonia = File.ReadAllText(Path.Combine(root, "freew", "FreeW.App.Avalonia", "SampleDocument.cs"));

        wpf.Should().Contain(
            "FreeWSampleDocumentFactory.Create(FreeWSampleDocumentProfile.ClassicEditor)");
        wpf.Should().NotContain("private static TextDocument CreateSampleDocument(");
        avalonia.Should().Contain(
            "FreeWSampleDocumentFactory.Create(FreeWSampleDocumentProfile.FeatureShowcase)");
        avalonia.Should().NotContain("TextDocument.CreateEmpty");
        avalonia.Should().NotContain("new Paragraph");
    }
}
