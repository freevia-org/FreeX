using FreeW.App.Presentation.DocumentView;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Tests;

public sealed class ImportedWordLineSpacingPlannerTests
{
    [Fact]
    public void ImportedApplicationDefaultsUseNativeLineHeightCalibration()
    {
        var document = ImportedDocument();

        ImportedWordLineSpacingPlanner.UsesApplicationDefaultLineSpacing(
            document,
            ParagraphFormatting.Default).Should().BeTrue();
        ImportedWordLineSpacingPlanner.UsesApplicationDefaultRunLineHeightCalibration(
            document,
            ParagraphFormatting.Default).Should().BeTrue();
    }

    [Fact]
    public void ModelAuthoredDocumentUsesWordCalibrationLikeAWordBlankDocument()
    {
        // A document created in FreeW is Word's blank document, so it lays out at Word's cadence
        // rather than the host text engine's natural single-line box. Only a package that carries
        // authoritative docDefaults opts out, which is what the two tests below cover.
        var document = new TextDocument();

        ImportedWordLineSpacingPlanner.UsesApplicationDefaultLineSpacing(
            document,
            ParagraphFormatting.Default).Should().BeTrue();
        ImportedWordLineSpacingPlanner.UsesApplicationDefaultRunLineHeightCalibration(
            document,
            ParagraphFormatting.Default).Should().BeTrue();
    }

    [Fact]
    public void PackageAuthoredParagraphDefaultsOptOutOfWordCalibration()
    {
        var document = new TextDocument { UseWordApplicationDefaultLineSpacing = false };

        ImportedWordLineSpacingPlanner.UsesApplicationDefaultLineSpacing(
            document,
            ParagraphFormatting.Default).Should().BeFalse();
        ImportedWordLineSpacingPlanner.UsesApplicationDefaultRunLineHeightCalibration(
            document,
            ParagraphFormatting.Default).Should().BeFalse();
    }

    [Fact]
    public void ExplicitOrNonDefaultSpacingRemainsAuthoritative()
    {
        var document = ImportedDocument();
        var explicitDefault = ParagraphFormatting.Default with { LineSpacingIsSet = true };
        var nonDefault = ParagraphFormatting.Default with { LineSpacing = 2.0 };
        var exact = ParagraphFormatting.Default with
        {
            LineRule = LineSpacingRule.Exact,
            LineHeightPt = 14
        };

        ImportedWordLineSpacingPlanner.UsesApplicationDefaultLineSpacing(document, explicitDefault)
            .Should().BeFalse();
        ImportedWordLineSpacingPlanner.UsesApplicationDefaultLineSpacing(document, nonDefault)
            .Should().BeFalse();
        ImportedWordLineSpacingPlanner.UsesApplicationDefaultLineSpacing(document, exact)
            .Should().BeFalse();
    }

    [Fact]
    public void ExplicitDocumentRunDefaultDoesNotUseApplicationRunCalibration()
    {
        var document = ImportedDocument();
        document.UseWordApplicationDefaultRunFormatting = false;

        ImportedWordLineSpacingPlanner.UsesApplicationDefaultLineSpacing(
            document,
            ParagraphFormatting.Default).Should().BeTrue();
        ImportedWordLineSpacingPlanner.UsesApplicationDefaultRunLineHeightCalibration(
            document,
            ParagraphFormatting.Default).Should().BeFalse();
    }

    [Fact]
    public void BothRenderersConsumeSharedProvenancePolicy()
    {
        var wpf = ReadSource("freew", "FreeW.App.Host", "Editing", "DocumentView.cs");
        var avalonia = ReadSource("freew", "FreeW.App.Avalonia", "Editing", "DocumentView.cs");

        wpf.Should().Contain("ImportedWordLineSpacingPlanner.UsesApplicationDefaultLineSpacing(");
        wpf.Should().Contain("ImportedWordLineSpacingPlanner.UsesApplicationDefaultRunLineHeightCalibration(");
        avalonia.Should().Contain("ImportedWordLineSpacingPlanner");
        avalonia.Should().Contain(".UsesApplicationDefaultRunLineHeightCalibration(");
        avalonia.Should().NotContain("UsesWordDefaultBodyLineBox(");
    }

    private static TextDocument ImportedDocument() => new()
    {
        UseWordApplicationDefaultLineSpacing = true,
        UseWordApplicationDefaultRunFormatting = true
    };

    private static string ReadSource(params string[] parts)
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        return File.ReadAllText(Path.Combine([root, .. parts]));
    }
}
