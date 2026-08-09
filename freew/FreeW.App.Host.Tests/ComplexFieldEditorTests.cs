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
    public void InsertComplexField_MergeField_PreservesNativeInstructionAndCachedLabel()
    {
        var view = ViewWithBody();

        view.InsertComplexField(
            MailMerge.BuildMergeFieldInstruction("First Name"),
            "«First Name»");
        view.CommitToModel();

        var run = FieldRun(view)!;
        run.ComplexField!.Instruction.Should().Be(" MERGEFIELD \"First Name\" \\* MERGEFORMAT ");
        run.Text.Should().Be("«First Name»");
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
    public void ImportedSimpleField_RendersTogglesAndCommitsWithoutLosingStorageMetadata()
    {
        var metadata = new SimpleFieldMetadata(IsLocked: true, IsDirty: true);
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph
        {
            Runs =
            {
                new Run("Contoso")
                {
                    ComplexField = new ComplexField(
                        " DOCPROPERTY \"Company\" ",
                        SimpleField: metadata)
                }
            }
        });
        var view = new DocumentView();
        view.LoadModel(doc);

        view.ToggleFieldCodes();
        string.Concat(view.Document.Blocks.OfType<System.Windows.Documents.Paragraph>()
                .SelectMany(p => p.Inlines.OfType<System.Windows.Documents.Run>())
                .Select(run => run.Text))
            .Should().Contain("{ DOCPROPERTY \"Company\" }");

        view.ToggleFieldCodes();
        view.CommitToModel();

        var field = FieldRun(view)!;
        field.Text.Should().Be("Contoso");
        field.ComplexField!.Instruction.Should().Be(" DOCPROPERTY \"Company\" ");
        field.ComplexField.SimpleField.Should().Be(metadata);
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
    public void UpdateFields_AppliesDateAndTimePictureSwitches()
    {
        var before = DateTime.Now;
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var metadataMoment = new DateTime(2026, 8, 6, 14, 5, 0);
        var localOffset = TimeZoneInfo.Local.GetUtcOffset(metadataMoment);
        doc.Properties.Created = new DateTimeOffset(metadataMoment, localOffset);
        doc.Properties.Modified = new DateTimeOffset(metadataMoment.AddDays(2), localOffset);
        doc.Properties.LastModifiedBy = "Ada Lovelace";
        doc.Blocks.Add(new Paragraph
        {
            Runs =
            {
                Run.ComplexFieldRun(" DATE \\@ \"yyyy-MM-dd\" ", "stale date"),
                Run.ComplexFieldRun(" TIME \\@ \"HH:mm\" ", "stale time"),
                Run.ComplexFieldRun(" CREATEDATE \\@ \"yyyy-MM-dd\" ", "stale created"),
                Run.ComplexFieldRun(" SAVEDATE \\@ \"yyyy-MM-dd HH:mm\" ", "stale saved"),
                Run.ComplexFieldRun(" LASTSAVEDBY ", "stale owner")
            }
        });
        var view = new DocumentView();
        view.LoadModel(doc);

        view.UpdateFields();
        view.CommitToModel();

        var after = DateTime.Now;
        var runs = ((Paragraph)view.Model.Blocks.Single()).Runs;
        runs[0].Text.Should().BeOneOf(before.ToString("yyyy-MM-dd"), after.ToString("yyyy-MM-dd"));
        runs[1].Text.Should().BeOneOf(before.ToString("HH:mm"), after.ToString("HH:mm"));
        runs[2].Text.Should().Be("2026-08-06");
        runs[3].Text.Should().Be("2026-08-08 14:05");
        runs[4].Text.Should().Be("Ada Lovelace");
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
    public void UpdateFields_RefreshesDocPropertyAndDocVariableFromDocumentPackageState()
    {
        var word = System.Xml.Linq.XNamespace.Get(
            "http://schemas.openxmlformats.org/wordprocessingml/2006/main");
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Properties.Title = "Current title";
        doc.Preserved.OriginalSettings = new System.Xml.Linq.XElement(
            word + "settings",
            new System.Xml.Linq.XElement(
                word + "docVars",
                new System.Xml.Linq.XElement(
                    word + "docVar",
                    new System.Xml.Linq.XAttribute(word + "name", "Channel"),
                    new System.Xml.Linq.XAttribute(word + "val", "Preview"))));
        doc.Preserved.Parts.Add(new PreservedPart(
            "/docProps/app.xml",
            System.Text.Encoding.UTF8.GetBytes(
                """
                <Properties xmlns="http://schemas.openxmlformats.org/officeDocument/2006/extended-properties">
                  <Company>Contoso Research</Company>
                  <Manager>Ada Lovelace</Manager>
                </Properties>
                """)));
        var title = Run.ComplexFieldRun(" DOCPROPERTY Title ", "stale title");
        var company = Run.ComplexFieldRun(" DOCPROPERTY Company ", "stale company");
        var manager = Run.ComplexFieldRun(" DOCPROPERTY Manager ", "stale manager");
        var channel = Run.ComplexFieldRun(" DOCVARIABLE Channel ", "stale channel");
        doc.Blocks.Add(new Paragraph { Runs = { title, company, manager, channel } });
        var view = new DocumentView();
        view.LoadModel(doc);

        view.UpdateFields();
        view.CommitToModel();

        var updatedFields = view.Model.Blocks.OfType<Paragraph>()
            .SelectMany(paragraph => paragraph.Runs)
            .Where(run => run.ComplexField is not null)
            .ToArray();
        updatedFields.Single(run => ComplexFieldEngine.Argument(run.ComplexField!.Instruction) == "Title")
            .Text.Should().Be("Current title");
        updatedFields.Single(run => ComplexFieldEngine.Argument(run.ComplexField!.Instruction) == "Company")
            .Text.Should().Be("Contoso Research");
        updatedFields.Single(run => ComplexFieldEngine.Argument(run.ComplexField!.Instruction) == "Manager")
            .Text.Should().Be("Ada Lovelace");
        updatedFields.Single(run => run.ComplexField!.Keyword == "DOCVARIABLE").Text.Should().Be("Preview");
    }

    [StaFact]
    public void UpdateFields_SeqUsesAuthoredResultPicture()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph
        {
            Runs = { Run.ComplexFieldRun(" SEQ Figure \\r 14 \\* ROMAN ", "stale") }
        });
        var view = new DocumentView();
        view.LoadModel(doc);

        view.UpdateFields();
        view.CommitToModel();

        FieldRun(view)!.Text.Should().Be("XIV");
    }

    [StaFact]
    public void UpdateFields_SeqCountsTableFieldsAndClearsHiddenResult()
    {
        var first = Run.ComplexFieldRun(" SEQ Figure ", "stale");
        var hidden = Run.ComplexFieldRun(" SEQ Figure \\h ", "stale");
        var last = Run.ComplexFieldRun(" SEQ Figure ", "stale");
        var table = new Table();
        var row = new TableRow();
        var cell = new TableCell();
        cell.Paragraphs.Add(new Paragraph { Runs = { hidden } });
        row.Cells.Add(cell);
        table.Rows.Add(row);
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph { Runs = { first } });
        doc.Blocks.Add(table);
        doc.Blocks.Add(new Paragraph { Runs = { last } });
        var view = new DocumentView();
        view.LoadModel(doc);

        view.UpdateFields();
        view.CommitToModel();

        ((Paragraph)view.Model.Blocks[0]).Runs[0].Text.Should().Be("1");
        ((Table)view.Model.Blocks[1]).Rows[0].Cells[0].Paragraphs[0].Runs[0].Text.Should().BeEmpty();
        ((Paragraph)view.Model.Blocks[2]).Runs[0].Text.Should().Be("3");
    }

    [StaFact]
    public void UpdateFields_DoesNotRecomputeLockedImportedSimpleField()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph("Chapter Two") { StyleId = "Heading1" });
        doc.Blocks.Add(new Paragraph
        {
            Runs =
            {
                new Run("Locked chapter")
                {
                    ComplexField = new ComplexField(
                        " STYLEREF 1 ",
                        SimpleField: new SimpleFieldMetadata(IsLocked: true, IsDirty: true))
                }
            }
        });
        var view = new DocumentView();
        view.LoadModel(doc);

        view.UpdateFields();
        view.CommitToModel();

        var field = FieldRun(view)!;
        field.Text.Should().Be("Locked chapter");
        field.ComplexField!.SimpleField.Should().Be(new SimpleFieldMetadata(true, true));
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

    [StaFact]
    public void UpdateFields_RefreshesComplexNoteRefAboveBelow()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var target = new Paragraph("Body");
        target.Runs.Add(Run.FootnoteReference(20));
        target.BookmarkNames.Add("_RefNote");
        target.BookmarkBoundaries.Add(new BookmarkBoundary("note", BookmarkBoundaryKind.Start, 1, "_RefNote"));
        target.BookmarkBoundaries.Add(new BookmarkBoundary("note", BookmarkBoundaryKind.End, 2));
        doc.Blocks.Add(target);
        var field = Run.ComplexFieldRun(" NOTEREF _RefNote \\p ", "stale");
        doc.Blocks.Add(new Paragraph { Runs = { field } });
        doc.Footnotes[20] = new Footnote(20, "note");

        var view = new DocumentView();
        view.LoadModel(doc);
        view.UpdateFields();

        field.Text.Should().Be("1 above");
    }
}
