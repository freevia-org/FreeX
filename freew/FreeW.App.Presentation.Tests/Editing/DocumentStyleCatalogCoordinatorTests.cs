using FreeW.App.Presentation.Dialogs;
using FreeW.App.Presentation.Editing;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Tests.Editing;

public sealed class DocumentStyleCatalogCoordinatorTests
{
    private static DocumentEditingSession SessionWith(params string[] paragraphs)
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        foreach (var text in paragraphs)
            document.Blocks.Add(new Paragraph(text));
        var session = new DocumentEditingSession();
        session.LoadDocument(document);
        return session;
    }

    private static StyleDefinition Definition(
        string name,
        StyleDefinitionKind kind,
        RunFormatting? run = null,
        ParagraphFormatting? paragraph = null,
        string? basedOn = "Normal",
        string? next = null,
        bool addToGallery = true) =>
        new(name, kind, basedOn, next, run ?? RunFormatting.Default, paragraph ?? ParagraphFormatting.Default, addToGallery);

    private static int UndoAll(DocumentEditingSession session)
    {
        var count = 0;
        while (session.Commands.Undo())
            count++;
        return count;
    }

    [Fact]
    public void CreateStyle_Paragraph_AppliesToTargets_AsOneUndoStep()
    {
        var session = SessionWith("one", "two");

        var created = session.Styles.CreateStyle(
            Definition("Callout", StyleDefinitionKind.Paragraph, new RunFormatting { Bold = true }),
            [1, 1, 99]);

        created!.Type.Should().Be(StyleType.Paragraph);
        ((Paragraph)session.Document.Blocks[0]).StyleId.Should().BeNull();
        ((Paragraph)session.Document.Blocks[1]).StyleId.Should().Be(created.Id);

        UndoAll(session).Should().Be(1);
        session.Document.Styles.Should().NotContainKey(created.Id);
        ((Paragraph)session.Document.Blocks[1]).StyleId.Should().BeNull();

        session.Commands.Redo().Should().BeTrue();
        session.Document.Styles.Should().ContainKey(created.Id);
        ((Paragraph)session.Document.Blocks[1]).StyleId.Should().Be(created.Id);
    }

    [Fact]
    public void CreateStyle_Linked_CreatesBothHalves_AndUndoRemovesBoth()
    {
        var session = SessionWith("one");

        var created = session.Styles.CreateStyle(
            Definition("Callout", StyleDefinitionKind.Linked, new RunFormatting { Italic = true }, next: "Normal"),
            [0]);

        var character = StyleManager.FindLinkedCharacterStyle(session.Document, created!.Id);
        character.Should().NotBeNull();
        character!.Run.Italic.Should().BeTrue();
        ((Paragraph)session.Document.Blocks[0]).StyleId.Should().Be(created.Id);

        UndoAll(session).Should().Be(1);
        session.Document.Styles.Should().NotContainKey(created.Id).And.NotContainKey(character.Id);
    }

    [Fact]
    public void CreateStyle_Character_IsNotAppliedToParagraphs()
    {
        var session = SessionWith("one");

        var created = session.Styles.CreateStyle(
            Definition("Code", StyleDefinitionKind.Character, new RunFormatting { FontFamily = "Consolas" }, basedOn: null, addToGallery: false),
            [0]);

        created!.Type.Should().Be(StyleType.Character);
        created.QuickFormat.Should().BeFalse();
        ((Paragraph)session.Document.Blocks[0]).StyleId.Should().BeNull();
        UndoAll(session).Should().Be(1);
    }

    [Fact]
    public void CreateStyle_RunsTheRendererApplicationInsideTheSameUndoGroup()
    {
        var session = SessionWith("one", "two");

        var created = session.Styles.CreateStyle(
            Definition("Code", StyleDefinitionKind.Character, basedOn: null),
            [],
            style => session.SetParagraphStyles([0, 1], "Normal"));

        ((Paragraph)session.Document.Blocks[1]).StyleId.Should().Be("Normal");
        UndoAll(session).Should().Be(1);
        session.Document.Styles.Should().NotContainKey(created!.Id);
        ((Paragraph)session.Document.Blocks[1]).StyleId.Should().BeNull();
    }

    [Fact]
    public void CreateStyle_WhenTheRendererApplicationFails_RollsEverythingBack()
    {
        var session = SessionWith("one");

        var act = () => session.Styles.CreateStyle(
            Definition("Code", StyleDefinitionKind.Character, basedOn: null),
            [],
            _ => throw new InvalidOperationException("apply failed"));

        act.Should().Throw<InvalidOperationException>();
        session.Document.Styles.Values.Should().NotContain(style => style.Name == "Code");
        session.Commands.IsUndoGroupOpen.Should().BeFalse();
        session.Commands.CanUndo.Should().BeFalse();
    }

    [Fact]
    public void CreateStyle_WithBlankName_DoesNothing()
    {
        var session = SessionWith("one");

        session.Styles.CreateStyle(Definition("  ", StyleDefinitionKind.Paragraph), [0]).Should().BeNull();

        session.Commands.CanUndo.Should().BeFalse();
        session.Commands.IsUndoGroupOpen.Should().BeFalse();
    }

    [Fact]
    public void ModifyStyle_RenamesAndReformats_KeepsLinkedHalfInStep_AndUndoesExactly()
    {
        var session = SessionWith("one");
        var created = session.Styles.CreateStyle(
            Definition("Callout", StyleDefinitionKind.Linked, new RunFormatting { Bold = true }), [0])!;
        var characterId = created.LinkedStyleId!;

        var updated = session.Styles.ModifyStyle(
            created.Id,
            Definition(
                "Aside",
                StyleDefinitionKind.Linked,
                new RunFormatting { Italic = true, HighlightColorHex = "#FFFF00" },
                new ParagraphFormatting { Alignment = TextAlignment.Center },
                basedOn: null,
                next: "Normal",
                addToGallery: false));

        updated!.Id.Should().Be(created.Id);
        updated.Name.Should().Be("Aside");
        updated.BasedOnStyleId.Should().BeNull();
        updated.NextStyleId.Should().Be("Normal");
        updated.QuickFormat.Should().BeFalse();
        updated.Paragraph.Alignment.Should().Be(TextAlignment.Center);
        var character = session.Document.Styles[characterId];
        character.Name.Should().Be("Aside Char");
        character.Run.HighlightColorHex.Should().Be("#FFFF00");
        character.Run.Bold.Should().BeFalse();

        session.Commands.Undo().Should().BeTrue();
        session.Document.Styles[created.Id].Name.Should().Be("Callout");
        session.Document.Styles[created.Id].Run.Bold.Should().BeTrue();
        session.Document.Styles[created.Id].LinkedStyleId.Should().Be(characterId);
        session.Document.Styles[characterId].Name.Should().Be("Callout Char");
        session.Document.Styles[characterId].LinkedStyleId.Should().Be(created.Id);
    }

    [Fact]
    public void ModifyStyle_WithUnchangedDefinition_PushesNoUndoEntry()
    {
        var session = SessionWith("one");
        var created = session.Styles.CreateStyle(
            Definition("Callout", StyleDefinitionKind.Linked, new RunFormatting { Bold = true }, next: "Normal"), [0])!;
        var dialog = StyleDefinitionDialogPlanner.CreateModifySession(session.Document, created);
        var before = UndoDepth(session);

        var result = session.Styles.ModifyStyle(created.Id, dialog.PlanAcceptance().Definition!);

        result.Should().BeSameAs(session.Document.Styles[created.Id]);
        UndoDepth(session).Should().Be(before);
    }

    [Fact]
    public void ModifyStyle_OnBuiltIn_KeepsItsName()
    {
        var session = SessionWith("one");
        BuiltInStyles.EnsureSeeded(session.Document, "Heading1");
        var original = session.Document.Styles["Heading1"];

        var updated = session.Styles.ModifyStyle(
            "Heading1",
            Definition("Renamed", StyleDefinitionKind.Paragraph, new RunFormatting { Underline = true }, basedOn: original.BasedOnStyleId, next: original.NextStyleId));

        updated!.Name.Should().Be(original.Name);
        updated.Run.Underline.Should().BeTrue();
        session.Styles.RenameStyle("Heading1", "Other").Should().BeNull();
    }

    [Fact]
    public void RenameStyle_IsUndoable_AndRefusesBlankNames()
    {
        var session = SessionWith("one");
        var created = session.Styles.CreateStyle(Definition("Callout", StyleDefinitionKind.Paragraph), [])!;

        session.Styles.RenameStyle(created.Id, " ").Should().BeNull();
        session.Styles.RenameStyle(created.Id, "Aside")!.Name.Should().Be("Aside");

        session.Commands.Undo().Should().BeTrue();
        session.Document.Styles[created.Id].Name.Should().Be("Callout");
    }

    [Fact]
    public void UpdateStyleToMatch_RedefinesFormatting_AndCharacterStyleIgnoresParagraph()
    {
        var session = SessionWith("one");
        var paragraphStyle = session.Styles.CreateStyle(Definition("Callout", StyleDefinitionKind.Paragraph), [])!;
        var characterStyle = session.Styles.CreateStyle(
            Definition("Code", StyleDefinitionKind.Character, basedOn: null), [])!;
        var run = new RunFormatting { FontFamily = "Georgia", FontSizePt = 20 };
        var paragraph = new ParagraphFormatting { Alignment = TextAlignment.Right };

        session.Styles.UpdateStyleToMatch(paragraphStyle.Id, run, paragraph)!.Paragraph.Alignment
            .Should().Be(TextAlignment.Right);
        var updatedCharacter = session.Styles.UpdateStyleToMatch(characterStyle.Id, run, paragraph)!;

        updatedCharacter.Run.FontFamily.Should().Be("Georgia");
        updatedCharacter.Paragraph.Should().Be(ParagraphFormatting.Default);
        session.Commands.Undo().Should().BeTrue();
        session.Commands.Undo().Should().BeTrue();
        session.Document.Styles[paragraphStyle.Id].Paragraph.Alignment.Should().Be(TextAlignment.Left);
        session.Styles.UpdateStyleToMatch("Missing", run, paragraph).Should().BeNull();
    }

    [Fact]
    public void DeleteStyle_ReassignsParagraphs_AndUndoRestoresThem()
    {
        var session = SessionWith("one", "two");
        var created = session.Styles.CreateStyle(Definition("Callout", StyleDefinitionKind.Linked), [0, 1])!;
        var characterId = created.LinkedStyleId!;

        session.Styles.DeleteStyle(created.Id).Should().BeTrue();
        session.DeleteParagraphStyle("Normal").Should().BeFalse("built-in styles cannot be deleted");

        session.Document.Styles.Should().NotContainKey(created.Id).And.NotContainKey(characterId);
        session.Document.Paragraphs.Should().OnlyContain(paragraph => paragraph.StyleId == null);

        session.Commands.Undo().Should().BeTrue();
        session.Document.Styles.Should().ContainKey(created.Id).And.ContainKey(characterId);
        session.Document.Paragraphs.Should().OnlyContain(paragraph => paragraph.StyleId == created.Id);
    }

    private static int UndoDepth(DocumentEditingSession session)
    {
        var depth = 0;
        while (session.Commands.Undo())
            depth++;
        for (var i = 0; i < depth; i++)
            session.Commands.Redo();
        return depth;
    }
}
