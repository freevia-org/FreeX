using System;
using System.Linq;
using FreeW.App.Presentation.DocumentView;
using FreeW.App.Host.Editing;
using FreeW.Core.Model;
using Xunit;

namespace FreeW.App.Host.Tests;

/// <summary>
/// Editor-level coverage for the generic complex-field commands: <see cref="DocumentView.InsertComplexField"/>
/// (Insert &gt; Quick Parts &gt; Field), <see cref="DocumentView.ToggleFieldCodes"/> (Alt+F9) and
/// <see cref="DocumentView.UpdateFields"/> (F9). Runs on STA because it drives the real WPF
/// <see cref="DocumentView"/>.
/// </summary>
public sealed class ComplexFieldEditorTests
{
    private static DocumentView ViewWithBody()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph("Body."));
        var view = new DocumentView();
        view.LoadModel(doc);
        return view;
    }

    private static Run? FieldRun(DocumentView view) =>
        view.Model.Blocks.OfType<Paragraph>()
            .SelectMany(p => p.Runs)
            .FirstOrDefault(r => r.ComplexField is not null);

    [StaFact]
    public void InsertComplexField_MaterialisesComplexFieldRun_WithNormalisedInstruction()
    {
        var view = ViewWithBody();

        view.InsertComplexField("PAGE");
        view.CommitToModel();

        var run = FieldRun(view);
        run.Should().NotBeNull();
        // The bare "PAGE" is normalised to Word's spaced form " PAGE ".
        run!.ComplexField!.Instruction.Should().Be(" PAGE ");
        run.ComplexField.Keyword.Should().Be("PAGE");
    }

    [StaFact]
    public void InsertComplexField_Author_ResolvesResultFromDocumentProperties()
    {
        var view = ViewWithBody();
        view.Model.Properties.Author = "Ada Lovelace";

        view.InsertComplexField("AUTHOR");
        view.CommitToModel();

        // The inserted field's cached result resolves live from the document author.
        FieldRun(view)!.Text.Should().Be("Ada Lovelace");
    }

    [StaFact]
    public void ToggleFieldCodes_FlipsShowCodeAcrossFields()
    {
        var view = ViewWithBody();
        view.InsertComplexField("PAGE");
        view.CommitToModel();

        FieldRun(view)!.ComplexField!.ShowCode.Should().BeFalse();

        view.ToggleFieldCodes();
        FieldRun(view)!.ComplexField!.ShowCode.Should().BeTrue();

        view.ToggleFieldCodes();
        FieldRun(view)!.ComplexField!.ShowCode.Should().BeFalse();
    }

    [StaFact]
    public void ToggleFieldCodes_RendersWordCodeShape_AndRestoresLiveResult()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Properties.Title = "Current title";
        doc.Blocks.Add(new Paragraph
        {
            Runs =
            {
                new Run("Title: "),
                Run.ComplexFieldRun(" TITLE ", "Stale result")
            }
        });
        var view = new DocumentView();
        view.LoadModel(doc);

        string.Concat(view.Document.Blocks.OfType<System.Windows.Documents.Paragraph>()
                .SelectMany(p => p.Inlines.OfType<System.Windows.Documents.Run>())
                .Select(run => run.Text))
            .Should().Contain("Title: Current title");

        view.ToggleFieldCodes();
        string.Concat(view.Document.Blocks.OfType<System.Windows.Documents.Paragraph>()
                .SelectMany(p => p.Inlines.OfType<System.Windows.Documents.Run>())
                .Select(run => run.Text))
            .Should().Contain("Title: { TITLE }");

        view.ToggleFieldCodes();
        string.Concat(view.Document.Blocks.OfType<System.Windows.Documents.Paragraph>()
                .SelectMany(p => p.Inlines.OfType<System.Windows.Documents.Run>())
                .Select(run => run.Text))
            .Should().Contain("Title: Current title");
    }

    [StaFact]
    public void UpdateFields_RecomputesDateResult()
    {
        var view = ViewWithBody();
        // Seed a DATE complex field carrying a stale cached result.
        var paragraph = (Paragraph)view.Model.Blocks[0];
        paragraph.Runs.Add(Run.ComplexFieldRun(" DATE ", "1/1/2000"));
        view.LoadModel(view.Model);

        view.UpdateFields();

        var today = DateTime.Now.ToString("d", System.Globalization.CultureInfo.CurrentCulture);
        FieldRun(view)!.Text.Should().Be(today);
        FieldRun(view)!.Text.Should().NotBe("1/1/2000");
    }

    [StaFact]
    public void UpdateFields_StyleRef_RefreshesCachedHeadingText()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph("Chapter Two") { StyleId = "Heading1" });
        doc.Blocks.Add(new Paragraph
        {
            Runs =
            {
                new Run("See "),
                Run.ComplexFieldRun(" STYLEREF 1 ", "Chapter One")
            }
        });

        var view = new DocumentView();
        view.LoadModel(doc);

        view.UpdateFields();
        view.CommitToModel();

        FieldRun(view)!.Text.Should().Be("Chapter Two");
    }

    [StaFact]
    public void BibliographyField_ShowsCachedResultWhenGeneratedRegionIsPresent_AndRetainsItOnCommit()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph
        {
            Runs =
            {
                new Run("Bibliography field cache: "),
                Run.ComplexFieldRun(" BIBLIOGRAPHY \\l 1033 ", "References")
            }
        });
        doc.Blocks.Add(new Paragraph("References") { StyleId = Citations.HeadingStyleId });

        var view = new DocumentView();
        view.LoadModel(doc);

        var rendered = view.Document.Blocks.OfType<System.Windows.Documents.Paragraph>().First();
        string.Concat(rendered.Inlines.OfType<System.Windows.Documents.Run>().Select(run => run.Text))
            .Should().Be("Bibliography field cache: References");

        view.CommitToModel();

        var field = FieldRun(view)!;
        field.Text.Should().Be("References");
        field.ComplexField!.Instruction.Should().Be(" BIBLIOGRAPHY \\l 1033 ");
    }
}
