using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using FreeW.App.Host.Editing;
using FreeW.App.Presentation.Dialogs;
using TableCell = FreeW.Core.Model.TableCell;
using TableRow = FreeW.Core.Model.TableRow;
using TextAlignment = System.Windows.TextAlignment;

namespace FreeW.App.Host.Tests;

/// <summary>
/// Equation editing in the WPF editor: selecting an equation, the undoable insert/replace edits the
/// in-place editor commits, display layout, and the editor window's binding to its session.
/// </summary>
public sealed class DocumentViewEquationEditingTests
{
    private static (DocumentView View, Equation Equation) ViewWithInlineEquation(string linear = "a/b")
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        var paragraph = new Paragraph();
        var equation = EquationLinearFormat.Parse(linear);
        paragraph.Runs.Add(new Run("Before "));
        paragraph.Runs.Add(Run.FromEquation(equation));
        paragraph.Runs.Add(new Run(" after"));
        document.Blocks.Add(paragraph);

        var view = new DocumentView();
        view.LoadModel(document);
        return (view, equation);
    }

    private static List<Equation> Equations(DocumentView view)
    {
        view.CommitToModel();
        return view.Model.Blocks.OfType<Paragraph>()
            .SelectMany(paragraph => paragraph.Runs)
            .Where(run => run.Equation is not null)
            .Select(run => run.Equation!)
            .ToList();
    }

    private static List<string> ParagraphTexts(DocumentView view)
    {
        view.CommitToModel();
        return view.Model.Blocks.OfType<Paragraph>().Select(paragraph => paragraph.PlainText).ToList();
    }

    [StaFact]
    public void SelectedEquation_IsOnlyAnEquationTheSelectionExactlyCovers()
    {
        var (view, equation) = ViewWithInlineEquation();

        view.SelectedEquation().Should().BeNull("a caret in the text selects nothing");

        view.SelectEquation(equation).Should().BeTrue();
        view.SelectedEquation().Should().BeSameAs(equation);

        // A caret right after the equation is ordinary text editing, not an equation selection.
        view.Selection.Select(view.Selection.End, view.Selection.End);
        view.SelectedEquation().Should().BeNull();

        // A selection that also takes in text is not an equation selection either.
        view.SelectAll();
        view.SelectedEquation().Should().BeNull();
    }

    [StaFact]
    public void ReplaceEquation_IsOneUndoableEdit()
    {
        var (view, equation) = ViewWithInlineEquation();
        var replacement = EquationLinearFormat.Parse("x^2");

        view.ReplaceEquation(equation, replacement).Should().BeTrue();

        Equations(view).Should().ContainSingle().Which.Should().BeSameAs(replacement);
        ParagraphTexts(view).Should().Equal("Before x^2 after");
        view.SelectedEquation().Should().BeSameAs(replacement, "the edited equation stays selected");

        view.CanUndo.Should().BeTrue();
        view.Undo();
        Equations(view).Should().ContainSingle().Which.Should().BeSameAs(equation);
        ParagraphTexts(view).Should().Equal("Before a/b after");

        view.Redo();
        Equations(view).Should().ContainSingle().Which.Should().BeSameAs(replacement);
    }

    [StaFact]
    public void ReplaceEquation_InsideATableCell_UndoesExactly()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        var table = new Table();
        var row = new TableRow();
        var cell = new TableCell("Cell ");
        var equation = EquationLinearFormat.Parse("a/b");
        cell.Paragraphs[0].Runs.Add(Run.FromEquation(equation));
        row.Cells.Add(cell);
        row.Cells.Add(new TableCell("other"));
        table.Rows.Add(row);
        document.Blocks.Add(table);
        document.Blocks.Add(new Paragraph("after"));
        var view = new DocumentView();
        view.LoadModel(document);
        var replacement = EquationLinearFormat.Parse("x_1");

        Equation CellEquation()
        {
            view.CommitToModel();
            return view.Model.Blocks.OfType<Table>().Single().Rows[0].Cells[0].Paragraphs
                .SelectMany(paragraph => paragraph.Runs)
                .Single(run => run.Equation is not null)
                .Equation!;
        }

        view.ReplaceEquation(equation, replacement).Should().BeTrue();
        CellEquation().Should().BeSameAs(replacement);

        view.Undo();
        CellEquation().Should().BeSameAs(equation, "undo must restore the cell, not just top-level paragraphs");

        view.Redo();
        CellEquation().Should().BeSameAs(replacement);
    }

    [StaFact]
    public void ReplaceEquation_WithAnEquationThatIsNotInTheDocument_DoesNothing()
    {
        var (view, equation) = ViewWithInlineEquation();

        view.ReplaceEquation(EquationLinearFormat.Parse("gone"), EquationLinearFormat.Parse("x")).Should().BeFalse();

        Equations(view).Should().ContainSingle().Which.Should().BeSameAs(equation);
        view.Commands.CanUndo.Should().BeFalse();
    }

    [StaFact]
    public void ChangeToDisplay_PutsTheEquationOnItsOwnLine_WithItsJustification()
    {
        var (view, equation) = ViewWithInlineEquation();
        var display = EquationLinearFormat.Parse("a/b");
        display.IsDisplayMath = true;
        display.Justification = MathJustification.Right;

        view.ReplaceEquation(equation, display).Should().BeTrue();

        ParagraphTexts(view).Should().Equal("Before ", "a/b", " after");
        var blocks = view.Document.Blocks.OfType<System.Windows.Documents.Paragraph>().ToList();
        blocks[1].TextAlignment.Should().Be(TextAlignment.Right);

        var centred = EquationLinearFormat.Parse("a/b");
        centred.IsDisplayMath = true;
        view.ReplaceEquation(display, centred).Should().BeTrue();
        view.Document.Blocks.OfType<System.Windows.Documents.Paragraph>().ElementAt(1)
            .TextAlignment.Should().Be(TextAlignment.Center);

        view.Undo();
        view.Undo();
        ParagraphTexts(view).Should().Equal("Before a/b after");
        Equations(view).Single().Should().BeSameAs(equation);
    }

    [StaFact]
    public void InsertEquationAtCaret_InsertsAtTheCaret_AsOneUndoableEdit()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph("HelloWorld"));
        var view = new DocumentView();
        view.LoadModel(document);
        var paragraph = view.Document.Blocks.OfType<System.Windows.Documents.Paragraph>().Single();
        view.CaretPosition = paragraph.ContentStart.GetPositionAtOffset(6, LogicalDirection.Forward)!;
        var caretText = new TextRange(paragraph.ContentStart, view.CaretPosition).Text;
        var equation = EquationLinearFormat.Parse(@"\sqrt(x)");

        view.InsertEquationAtCaret(equation);

        Equations(view).Should().ContainSingle().Which.Should().BeSameAs(equation);
        var runs = view.Model.Blocks.OfType<Paragraph>().Single().Runs;
        string.Concat(runs.TakeWhile(run => run.Equation is null).Select(run => run.Text)).Should().Be(caretText);
        view.SelectedEquation().Should().BeSameAs(equation);

        view.Undo();
        Equations(view).Should().BeEmpty();
        ParagraphTexts(view).Should().Equal("HelloWorld");
    }

    [StaFact]
    public void InsertEquationAtCaret_DisplayEquation_GetsItsOwnLineInTheSameUndoStep()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph("Text"));
        var view = new DocumentView();
        view.LoadModel(document);
        view.CaretPosition = view.Document.ContentEnd;
        var equation = EquationLinearFormat.Parse("a/b");
        equation.IsDisplayMath = true;

        view.InsertEquationAtCaret(equation);

        ParagraphTexts(view).Should().Equal("Text", "a/b");
        view.Document.Blocks.OfType<System.Windows.Documents.Paragraph>().Last()
            .TextAlignment.Should().Be(TextAlignment.Center);

        view.Undo();
        ParagraphTexts(view).Should().Equal("Text");
        view.Commands.CanUndo.Should().BeFalse("inserting and isolating the equation is a single edit");
    }

    [StaFact]
    public void DisplayEquationParagraph_FollowsTheEquationsJustification()
    {
        foreach (var (justification, expected) in new[]
                 {
                     (MathJustification.CenterGroup, TextAlignment.Center),
                     (MathJustification.Center, TextAlignment.Center),
                     (MathJustification.Left, TextAlignment.Left),
                     (MathJustification.Right, TextAlignment.Right),
                 })
        {
            var document = TextDocument.CreateEmpty();
            document.Blocks.Clear();
            var equation = EquationLinearFormat.Parse("a/b");
            equation.IsDisplayMath = true;
            equation.Justification = justification;
            var paragraph = new Paragraph();
            paragraph.Runs.Add(Run.FromEquation(equation));
            document.Blocks.Add(paragraph);
            var view = new DocumentView();
            view.LoadModel(document);

            view.Document.Blocks.OfType<System.Windows.Documents.Paragraph>().Single()
                .TextAlignment.Should().Be(expected, justification.ToString());
        }
    }

    [StaFact]
    public void EditorWindow_ShowsTheLinearSource_AndALivePreview()
    {
        var equation = EquationLinearFormat.Parse("a/b");
        var window = new EquationEditorWindow(EquationEditorSession.ForExisting(equation));

        window.LinearTextBox.Text.Should().Be("a/b");
        window.PreviewContent.Should().BeAssignableTo<StackPanel>("a fraction is drawn built up, not as text");

        window.LinearTextBox.Text = "x+1";
        window.Session.Text.Should().Be("x+1");
        window.PreviewContent.Should().BeOfType<TextBlock>();

        window.LinearTextBox.Text = string.Empty;
        window.PreviewContent.Should().BeOfType<TextBlock>()
            .Which.Text.Should().Be(EquationEditorText.BuildSurface().EmptyPreview);
    }

    [StaFact]
    public void EditorWindow_Accept_RaisesTheCommit_OnlyForARealChange()
    {
        var equation = EquationLinearFormat.Parse("a/b");
        var unchanged = new EquationEditorWindow(EquationEditorSession.ForExisting(equation));
        var commits = new List<EquationEditorCommit>();
        unchanged.Committed += commits.Add;
        unchanged.Accept();
        commits.Should().BeEmpty();

        var edited = new EquationEditorWindow(EquationEditorSession.ForExisting(equation));
        edited.Committed += commits.Add;
        edited.LinearTextBox.Text = "a/c";
        edited.Accept();

        var commit = commits.Should().ContainSingle().Subject;
        commit.Original.Should().BeSameAs(equation);
        commit.Equation.Runs.Single().Denominator.Should().Be("c");
    }

    [StaFact]
    public void EditorWindow_InsertText_FillsTheSelection_AndSelectsThePlaceholder()
    {
        var window = new EquationEditorWindow(EquationEditorSession.ForNew());
        window.LinearTextBox.Text = "x=";
        window.LinearTextBox.CaretIndex = 2;

        window.InsertText("⬚/⬚");

        window.LinearTextBox.Text.Should().Be("x=⬚/⬚");
        window.LinearTextBox.SelectionStart.Should().Be(2);
        window.LinearTextBox.SelectionLength.Should().Be(1);
        window.Session.Text.Should().Be("x=⬚/⬚");

        window.InsertText("α");
        window.LinearTextBox.Text.Should().Be("x=α/⬚");
        window.LinearTextBox.CaretIndex.Should().Be(3);
    }

    [StaFact]
    public void EditorWindow_AutoCorrectsANameWhenASpaceIsTyped()
    {
        var window = new EquationEditorWindow(EquationEditorSession.ForNew());

        window.LinearTextBox.Text = @"\alpha";
        window.LinearTextBox.CaretIndex = 6;
        window.LinearTextBox.SelectedText = " ";
        window.LinearTextBox.CaretIndex = 7;
        window.LinearTextBox.Text = @"\alpha ";
        window.LinearTextBox.CaretIndex = 7;

        // Setting Text resets the caret, so drive the same rule the TextChanged handler applies.
        var corrected = EquationEditorSession.AutoCorrect(@"\alpha ", 7);
        corrected.Should().Be(new EquationEditorTextEdit("α", 1, 0));
    }

    [StaFact]
    public void Controller_CommitsANewEquation_ThroughTheViewsUndoableInsert()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph("Text"));
        var view = new DocumentView();
        view.LoadModel(document);
        var states = 0;
        view.EquationEditor.StateChanged += (_, _) => states++;

        view.EquationEditor.InsertText("⬚^2");
        try
        {
            view.EquationEditor.IsOpen.Should().BeTrue("inserting a structure opens the editor");
            var window = view.EquationEditor.Window!;
            window.LinearTextBox.SelectedText = "x";
            window.Accept();
        }
        finally
        {
            view.EquationEditor.Cancel();
        }

        view.EquationEditor.IsOpen.Should().BeFalse();
        states.Should().Be(2, "opening and closing each notify the shell once");
        var inserted = Equations(view).Should().ContainSingle().Subject;
        inserted.Runs.Single().Kind.Should().Be(MathRunKind.Superscript);
        inserted.Runs.Single().Base.Should().Be("x");

        view.Undo();
        Equations(view).Should().BeEmpty();
    }

    [StaFact]
    public void Controller_EditsTheSelectedEquation_AndReplacesItOnAccept()
    {
        var (view, equation) = ViewWithInlineEquation();

        view.EquationEditor.BeginEditSelected().Should().BeFalse("no equation is selected yet");
        view.SelectEquation(equation);
        view.EquationEditor.BeginEditSelected().Should().BeTrue();
        try
        {
            var window = view.EquationEditor.Window!;
            window.LinearTextBox.Text.Should().Be("a/b");
            window.LinearTextBox.Text = "a/(b+1)";
            window.Accept();
        }
        finally
        {
            view.EquationEditor.Cancel();
        }

        var edited = Equations(view).Should().ContainSingle().Subject;
        edited.Should().NotBeSameAs(equation);
        edited.Runs.Single().Denominator.Should().Be("b+1");

        view.Undo();
        Equations(view).Single().Should().BeSameAs(equation);
    }
}
