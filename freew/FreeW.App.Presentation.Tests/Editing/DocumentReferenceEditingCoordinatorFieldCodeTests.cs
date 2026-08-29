using FreeW.App.Presentation.DocumentView;
using FreeW.App.Presentation.Editing;

namespace FreeW.App.Presentation.Tests.Editing;

/// <summary>
/// Round 167 correction (meta F1): real Word's Alt+F9 toggles field-code display for a simple
/// <see cref="RunFieldKind"/> field (PAGE/DATE/...) exactly like it does for a <see cref="ComplexField"/> --
/// a document showing a page number shows <c>{ PAGE }</c> when codes are toggled on. The round-166/167
/// carryover wrongly recorded this as an intentional no-op (no "code" state to toggle) and pinned it with
/// characterization tests; those claims are corrected here and in the WPF/Avalonia shell tests.
/// <see cref="DocumentReferenceEditingCoordinator.ToggleFieldCodes"/> (Alt+F9, the document-wide command
/// both shells actually call) is the production entry point this exercises directly.
/// </summary>
public sealed class DocumentReferenceEditingCoordinatorFieldCodeTests
{
    [Fact]
    public void ToggleFieldCodes_TogglesAndUntogglesARibbonInsertedPageNumberField()
    {
        var session = SessionWithField(Run.PageNumberField());

        var result = session.References.ToggleFieldCodes();

        result.Applied.Should().BeTrue();
        result.ShowCodes.Should().BeTrue();
        var run = FieldRun(session);
        run.FieldCodeVisible.Should().BeTrue(
            "Shift+F9/Alt+F9 must be able to show a simple field's code just like a complex field's");
        DocumentFieldDisplayPlanner.ResolveCode(run.FieldKind).Should().Be("{ PAGE }");

        var toggledBack = session.References.ToggleFieldCodes();

        toggledBack.Applied.Should().BeTrue();
        toggledBack.ShowCodes.Should().BeFalse();
        FieldRun(session).FieldCodeVisible.Should().BeFalse("toggling again must restore the result view");
    }

    [Fact]
    public void ToggleFieldCodes_TogglesAndUntogglesARibbonInsertedDateField()
    {
        var session = SessionWithField(Run.DateField("8/29/2026"));

        session.References.ToggleFieldCodes().ShowCodes.Should().BeTrue();
        var run = FieldRun(session);
        run.FieldCodeVisible.Should().BeTrue();
        DocumentFieldDisplayPlanner.ResolveCode(run.FieldKind).Should().Be("{ DATE }");

        session.References.ToggleFieldCodes().ShowCodes.Should().BeFalse();
        FieldRun(session).FieldCodeVisible.Should().BeFalse();
    }

    /// <summary>Sibling no-regression: a document made only of ComplexField instances keeps toggling the
    /// same way this fix's simple-field branch must not disturb.</summary>
    [Fact]
    public void ToggleFieldCodes_StillTogglesAComplexFieldOnlyDocument()
    {
        var field = Run.ComplexFieldRun(" DATE ", "cached date text");
        var session = SessionWithField(field);

        session.References.ToggleFieldCodes().ShowCodes.Should().BeTrue();
        field.ComplexField!.ShowCode.Should().BeTrue();

        session.References.ToggleFieldCodes().ShowCodes.Should().BeFalse();
        field.ComplexField!.ShowCode.Should().BeFalse();
    }

    /// <summary>A document mixing both field forms flips both to one shared majority state together,
    /// matching Word's single document-wide Alt+F9 toggle rather than tracking two independent states.</summary>
    [Fact]
    public void ToggleFieldCodes_MovesBothComplexAndSimpleFieldsToOneSharedState()
    {
        var complexField = Run.ComplexFieldRun(" DATE ", "cached date text");
        var simpleField = Run.PageNumberField();
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph { Runs = { complexField, simpleField } });
        var session = new DocumentEditingSession();
        session.LoadDocument(document);

        var result = session.References.ToggleFieldCodes();

        result.Applied.Should().BeTrue();
        result.FieldCount.Should().Be(2);
        result.ShowCodes.Should().BeTrue();
        complexField.ComplexField!.ShowCode.Should().BeTrue();
        simpleField.FieldCodeVisible.Should().BeTrue();
    }

    private static DocumentEditingSession SessionWithField(Run field)
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph { Runs = { field } });
        var session = new DocumentEditingSession();
        session.LoadDocument(document);
        return session;
    }

    private static Run FieldRun(DocumentEditingSession session) =>
        ((Paragraph)session.Document.Blocks[0]).Runs.Single();
}
