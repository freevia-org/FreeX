using FreeW.App.Presentation.Dialogs;
using FreeW.App.Presentation.DocumentView;

namespace FreeW.App.Presentation.Tests;

public sealed class DocumentSelectionFormattingPlannerTests
{
    [Fact]
    public void Build_detects_mixed_effective_formatting_across_paragraphs_and_styles()
    {
        var document = TextDocument.CreateEmpty();
        document.DefaultRun = new RunFormatting { FontFamily = "Calibri", FontSizePt = 11 };
        document.Styles["Emphasis"] = new DocumentStyle
        {
            Id = "Emphasis",
            Name = "Emphasis",
            Run = new RunFormatting { Italic = true },
        };

        var first = new Paragraph();
        first.Runs.Add(new Run("first", new RunFormatting { Bold = true }));
        var second = new Paragraph { StyleId = "Emphasis" };
        second.Runs.Add(new Run("second", new RunFormatting
        {
            FontFamily = "Aptos",
            FontSizePt = 12,
        }));

        var current = DocumentFormattingProbePlanner.Resolve(document, first, 0).Run;
        var state = DocumentSelectionFormattingPlanner.Build(
            document,
            current,
            [
                new DocumentFormattingTextRange(first, 0, first.PlainText.Length, true),
                new DocumentFormattingTextRange(second, 0, second.PlainText.Length),
            ]);

        state.BoldIndeterminate.Should().BeTrue();
        state.ItalicIndeterminate.Should().BeTrue();
        state.FamilyIndeterminate.Should().BeTrue();
        state.SizeIndeterminate.Should().BeTrue();
        state.UnderlineIndeterminate.Should().BeFalse();
        state.Run.Should().Be(current);
    }

    [Fact]
    public void Build_only_considers_runs_overlapping_the_selected_offsets()
    {
        var document = TextDocument.CreateEmpty();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("left", new RunFormatting { Bold = true }));
        paragraph.Runs.Add(new Run("right", new RunFormatting { Italic = true }));
        var current = DocumentFormattingProbePlanner.Resolve(document, paragraph, 6).Run;

        var state = DocumentSelectionFormattingPlanner.Build(
            document,
            current,
            [new DocumentFormattingTextRange(paragraph, 5, 10)]);

        state.Should().Be(new FontDialogSelectionState(current));
        state.Run.Italic.Should().BeTrue();
        state.Run.Bold.Should().BeFalse();
    }

    [Fact]
    public void Build_includes_selected_empty_paragraph_marks()
    {
        var document = TextDocument.CreateEmpty();
        document.Styles["Strong"] = new DocumentStyle
        {
            Id = "Strong",
            Name = "Strong",
            Run = new RunFormatting { Bold = true },
        };
        var ordinary = new Paragraph("text");
        var emptyStrong = new Paragraph { StyleId = "Strong" };
        var current = DocumentFormattingProbePlanner.Resolve(document, ordinary, 0).Run;

        var state = DocumentSelectionFormattingPlanner.Build(
            document,
            current,
            [
                new DocumentFormattingTextRange(ordinary, 0, ordinary.PlainText.Length),
                new DocumentFormattingTextRange(emptyStrong, 0, 0, IncludesParagraphMark: true),
            ]);

        state.BoldIndeterminate.Should().BeTrue();
    }

    [Fact]
    public void Build_normalizes_reversed_and_out_of_bounds_offsets()
    {
        var document = TextDocument.CreateEmpty();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("one", new RunFormatting { Underline = true }));
        paragraph.Runs.Add(new Run("two", new RunFormatting { Strikethrough = true }));
        var current = DocumentFormattingProbePlanner.Resolve(document, paragraph, 0).Run;

        var state = DocumentSelectionFormattingPlanner.Build(
            document,
            current,
            [new DocumentFormattingTextRange(paragraph, 100, -5)]);

        state.UnderlineIndeterminate.Should().BeTrue();
        state.StrikethroughIndeterminate.Should().BeTrue();
    }

    [Fact]
    public void Renderers_report_all_native_and_model_selection_regions()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var avalonia = Read(root, "freew", "FreeW.App.Avalonia", "Editing", "DocumentView.cs");
        var wpf = Read(root, "freew", "FreeW.App.Host", "Editing", "DocumentView.cs");

        avalonia.Should().Contain("DocumentSelectionFormattingPlanner.Build(")
            .And.Contain("CurrentShapeTextSelection is { } shapeSelection")
            .And.Contain("NormalizedHfSelection() is { } headerFooterSelection")
            .And.Contain("if (SelectedCellRange is { } cellRange")
            .And.Contain("if (_cellCaret is { } cellCaret")
            .And.Contain("if (NormalizedSelection() is not { } bodySelection)")
            .And.NotContain("var selectedFormatting = allCells");

        wpf.Should().Contain("FamilyIndeterminate: family == DependencyProperty.UnsetValue")
            .And.Contain("SizeIndeterminate: size == DependencyProperty.UnsetValue");
    }

    private static string Read(string root, params string[] parts) =>
        File.ReadAllText(Path.Combine([root, .. parts])).ReplaceLineEndings("\n");
}
