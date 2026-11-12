using FreeW.Core.Model;

namespace FreeW.Core.Model.Tests;

public sealed class LegacyFormFieldCommandTests
{
    private sealed class Context(TextDocument document) : IDocumentCommandContext
    {
        public TextDocument Document { get; } = document;
    }

    private static (TextDocument Document, Run Text, Run Box, Run List) Form()
    {
        var document = new TextDocument();
        var paragraph = new Paragraph("Name: ");
        var text = new Run(LegacyFormField.BlankTextResult)
        {
            ComplexField = new ComplexField(" FORMTEXT ", FormField: LegacyFormField.CreateDefault(LegacyFormFieldKind.Text, "Text1") with { DefaultText = "n/a" })
        };
        var box = new Run(ContentControl.UncheckedGlyph)
        {
            ComplexField = new ComplexField(" FORMCHECKBOX ", FormField: LegacyFormField.CreateDefault(LegacyFormFieldKind.CheckBox, "Check1"))
        };
        var list = new Run("A")
        {
            ComplexField = new ComplexField(" FORMDROPDOWN ", FormField: LegacyFormField.CreateDefault(LegacyFormFieldKind.DropDown, "Dropdown1") with { DropDownEntries = ["A", "B"] })
        };
        paragraph.Runs.Add(text);
        paragraph.Runs.Add(box);
        document.Blocks.Add(paragraph);

        // A form commonly lays its fields out in a table: the command must reach a cell too.
        var table = new Table();
        var row = new TableRow();
        var cell = new TableCell("");
        cell.Paragraphs[0].Runs.Add(list);
        row.Cells.Add(cell);
        table.Rows.Add(row);
        document.Blocks.Add(table);
        return (document, text, box, list);
    }

    [Fact]
    public void Set_FillsTheFieldAndUndoRedoRestoreExactly()
    {
        var (document, text, _, _) = Form();
        var original = text.ComplexField!;
        var context = new Context(document);
        var command = new SetLegacyFormFieldCommand(original, "Alice", original.FormField!);

        Assert.True(command.HasEffect(context));
        command.Apply(context);
        Assert.Equal("Alice", text.Text);
        Assert.NotSame(original, text.ComplexField);
        Assert.Equal(original.FormField, text.ComplexField!.FormField);

        command.Revert(context);
        Assert.Equal(LegacyFormField.BlankTextResult, text.Text);
        Assert.Same(original, text.ComplexField);

        command.Apply(context);
        Assert.Equal("Alice", text.Text);
    }

    [Fact]
    public void Set_ReachesAFieldInATableCellAndReportsNoEffectForAnEqualValue()
    {
        var (document, _, _, list) = Form();
        var context = new Context(document);
        var field = list.ComplexField!;

        Assert.False(new SetLegacyFormFieldCommand(field, "A", field.FormField!).HasEffect(context));

        var picked = field.FormField! with { DropDownResult = 1 };
        var command = new SetLegacyFormFieldCommand(field, "B", picked);
        command.Apply(context);
        Assert.Equal("B", list.Text);
        Assert.Equal(1, list.ComplexField!.FormField!.DropDownResult);
        command.Revert(context);
        Assert.Equal("A", list.Text);
        Assert.Null(list.ComplexField!.FormField!.DropDownResult);
    }

    [Fact]
    public void Set_OnAFieldNoLongerInTheDocumentDoesNothing()
    {
        var (document, _, _, _) = Form();
        var stranger = new ComplexField(" FORMTEXT ", FormField: LegacyFormField.CreateDefault(LegacyFormFieldKind.Text));
        var command = new SetLegacyFormFieldCommand(stranger, "x", stranger.FormField!);

        Assert.False(command.HasEffect(new Context(document)));
        command.Apply(new Context(document));
        command.Revert(new Context(document));
    }

    [Fact]
    public void Reset_ReturnsEveryFieldToItsDefaultAndUndoes()
    {
        var (document, text, box, list) = Form();
        var context = new Context(document);
        text.Text = "typed";
        box.Text = ContentControl.CheckedGlyph;
        box.ComplexField = box.ComplexField! with { FormField = box.ComplexField.FormField! with { Checked = true } };
        list.Text = "B";
        list.ComplexField = list.ComplexField! with { FormField = list.ComplexField.FormField! with { DropDownResult = 1 } };
        var before = (text.Text, box.ComplexField, list.ComplexField);

        var reset = new ResetLegacyFormFieldsCommand();
        Assert.True(reset.HasEffect(context));
        reset.Apply(context);
        Assert.Equal("n/a", text.Text);
        Assert.Equal(ContentControl.UncheckedGlyph, box.Text);
        Assert.False(box.ComplexField!.FormField!.IsChecked);
        Assert.Equal("A", list.Text);
        Assert.False(reset.HasEffect(context));

        reset.Revert(context);
        Assert.Equal(before, (text.Text, box.ComplexField, list.ComplexField));
        Assert.Equal("B", list.Text);
    }

    [Fact]
    public void Model_DisplayNamesAndEquality()
    {
        Assert.Equal("Text3", LegacyFormField.NextDefaultName(LegacyFormFieldKind.Text, ["Text1", "text2", null]));
        Assert.Equal(LegacyFormFieldKind.DropDown, LegacyFormField.KindForKeyword("formdropdown"));
        Assert.Null(LegacyFormField.KindForKeyword("PAGE"));

        var a = LegacyFormField.CreateDefault(LegacyFormFieldKind.DropDown) with { DropDownEntries = new List<string> { "x" } };
        var b = LegacyFormField.CreateDefault(LegacyFormFieldKind.DropDown) with { DropDownEntries = new[] { "x" } };
        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.NotEqual(a, b with { DropDownEntries = ["y"] });

        Assert.Equal(LegacyFormField.BlankTextResult, LegacyFormField.CreateDefault(LegacyFormFieldKind.Text).DisplayText(""));
        Assert.Equal("x", a.DisplayText(null));
        Assert.True(LegacyFormField.IsBlankText(LegacyFormField.BlankTextResult));
        Assert.False(LegacyFormField.CreateDefault(LegacyFormFieldKind.Text) with { TextType = LegacyFormTextType.CurrentDate } is { IsFillable: true });
    }
}
