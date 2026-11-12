namespace FreeW.Core.Model.Tests;

/// <summary>
/// Word's Create New Style / Manage Styles rules in the pure catalog: style type, the linked
/// paragraph+character pair, rename uniqueness, the gallery flag, and delete-with-reassign plus its undo.
/// </summary>
public sealed class StyleCatalogAuthoringTests
{
    private sealed class Context(TextDocument document) : IDocumentCommandContext
    {
        public TextDocument Document { get; } = document;

        public string? RevisionAuthor => null;
    }

    [Fact]
    public void CreateStyle_Character_HasNoParagraphFormattingOrFollowingStyle()
    {
        var doc = TextDocument.CreateEmpty();

        var style = StyleManager.CreateStyle(
            doc,
            "Code",
            basedOnId: "Normal",
            new RunFormatting { FontFamily = "Consolas", HighlightColorHex = "#FFFF00" },
            new ParagraphFormatting { Alignment = TextAlignment.Center },
            nextStyleId: "Normal",
            StyleType.Character,
            quickFormat: false);

        style.Type.Should().Be(StyleType.Character);
        style.BasedOnStyleId.Should().BeNull("a character style cannot inherit from a paragraph style");
        style.NextStyleId.Should().BeNull();
        style.Paragraph.Should().Be(ParagraphFormatting.Default);
        style.Run.HighlightColorHex.Should().Be("#FFFF00");
        style.QuickFormat.Should().BeFalse();
    }

    [Fact]
    public void CreateStyle_RejectsTableAndNumberingTypes()
    {
        var doc = TextDocument.CreateEmpty();

        var act = () => StyleManager.CreateStyle(
            doc, "Grid", null, RunFormatting.Default, ParagraphFormatting.Default, null, StyleType.Table);

        act.Should().Throw<ArgumentOutOfRangeException>();
        doc.Styles.Should().NotContainKey("Grid");
    }

    [Fact]
    public void CreateLinkedStyle_CreatesParagraphAndCharHalves_LinkedBothWays()
    {
        var doc = TextDocument.CreateEmpty();
        var run = new RunFormatting { Bold = true, ColorHex = "#C00000" };

        var paragraph = StyleManager.CreateLinkedStyle(
            doc, "Callout", "Normal", run, new ParagraphFormatting { IndentLeftPt = 18 }, "Normal");

        paragraph.Type.Should().Be(StyleType.Paragraph);
        paragraph.Name.Should().Be("Callout");
        paragraph.NextStyleId.Should().Be("Normal");
        var character = doc.Styles[paragraph.LinkedStyleId!];
        character.Type.Should().Be(StyleType.Character);
        character.Name.Should().Be("Callout Char");
        character.LinkedStyleId.Should().Be(paragraph.Id);
        character.Run.Should().Be(run);
        character.QuickFormat.Should().BeFalse("Word shows a linked pair as one gallery entry");
        StyleManager.FindLinkedCharacterStyle(doc, paragraph.Id).Should().BeSameAs(character);
        StyleManager.IsLinkedCharacterHalf(doc, character).Should().BeTrue();
        StyleManager.IsLinkedCharacterHalf(doc, paragraph).Should().BeFalse();
    }

    [Fact]
    public void CreateLinkedStyle_BasesCharacterHalfOnParentsCharacterHalf()
    {
        var doc = TextDocument.CreateEmpty();
        var parent = StyleManager.CreateLinkedStyle(
            doc, "Base", null, RunFormatting.Default, ParagraphFormatting.Default);

        var child = StyleManager.CreateLinkedStyle(
            doc, "Derived", parent.Id, RunFormatting.Default, ParagraphFormatting.Default);

        doc.Styles[child.LinkedStyleId!].BasedOnStyleId.Should().Be(parent.LinkedStyleId);
    }

    [Fact]
    public void SyncLinkedCharacterStyle_FollowsRenameAndRunFormatting()
    {
        var doc = TextDocument.CreateEmpty();
        var paragraph = StyleManager.CreateLinkedStyle(
            doc, "Callout", null, RunFormatting.Default, ParagraphFormatting.Default);

        StyleManager.ModifyStyle(doc, paragraph.Id, run: new RunFormatting { Italic = true }, name: "Aside");
        var synced = StyleManager.SyncLinkedCharacterStyle(doc, paragraph.Id);

        synced!.Name.Should().Be("Aside Char");
        synced.Run.Italic.Should().BeTrue();
        synced.LinkedStyleId.Should().Be(paragraph.Id);
        StyleManager.SyncLinkedCharacterStyle(doc, "Normal").Should().BeNull();
    }

    [Fact]
    public void ModifyStyle_Rename_IsDisambiguatedAgainstOtherStyles_AndKeepsOwnName()
    {
        var doc = TextDocument.CreateEmpty();
        var first = StyleManager.CreateStyle(doc, "Alpha", null, RunFormatting.Default, ParagraphFormatting.Default);
        var second = StyleManager.CreateStyle(doc, "Beta", null, RunFormatting.Default, ParagraphFormatting.Default);

        StyleManager.ModifyStyle(doc, second.Id, name: "alpha")!.Name.Should().Be("alpha 2");
        StyleManager.ModifyStyle(doc, first.Id, name: "Alpha")!.Name.Should().Be("Alpha");
        doc.Styles[first.Id].Id.Should().Be(first.Id, "a rename never changes the id paragraphs reference");
    }

    [Fact]
    public void ModifyStyle_PreservesEveryPropertyItDoesNotEdit()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Styles["Imported"] = new DocumentStyle
        {
            Id = "Imported",
            Name = "Imported",
            IsDefault = true,
            LinkedStyleId = "ImportedChar",
            QuickFormat = false,
            OutlineLevel = 2,
            TableBorders = true,
        };

        var updated = StyleManager.ModifyStyle(doc, "Imported", run: new RunFormatting { Bold = true })!;

        updated.IsDefault.Should().BeTrue();
        updated.LinkedStyleId.Should().Be("ImportedChar");
        updated.QuickFormat.Should().BeFalse();
        updated.OutlineLevel.Should().Be(2);
        updated.TableBorders.Should().BeTrue();
        StyleManager.ModifyStyle(doc, "Imported", quickFormat: true)!.QuickFormat.Should().BeTrue();
    }

    [Fact]
    public void DeleteStyleAndReassign_ReturnsParagraphsAndRunsToDefault_AndClearsReferences()
    {
        var doc = TextDocument.CreateEmpty();
        var linked = StyleManager.CreateLinkedStyle(
            doc, "Callout", "Normal", RunFormatting.Default, ParagraphFormatting.Default);
        var follower = StyleManager.CreateStyle(
            doc, "Follower", linked.Id, RunFormatting.Default, ParagraphFormatting.Default, linked.Id);
        var characterId = linked.LinkedStyleId!;
        var styled = new Paragraph("styled") { StyleId = linked.Id };
        var withRun = new Paragraph("run");
        withRun.Runs[0].StyleId = characterId;
        doc.Blocks.Add(styled);
        doc.Blocks.Add(withRun);
        var table = new Table();
        var row = new TableRow();
        var cell = new TableCell("cell");
        cell.Paragraphs[0].StyleId = linked.Id;
        row.Cells.Add(cell);
        table.Rows.Add(row);
        doc.Blocks.Add(table);

        var removed = StyleManager.DeleteStyleAndReassign(doc, linked.Id);

        removed.Should().BeEquivalentTo([linked.Id, characterId]);
        doc.Styles.Should().NotContainKey(linked.Id).And.NotContainKey(characterId);
        styled.StyleId.Should().BeNull("Word returns paragraphs of a deleted style to Normal");
        withRun.Runs[0].StyleId.Should().BeNull();
        cell.Paragraphs[0].StyleId.Should().BeNull();
        doc.Styles[follower.Id].BasedOnStyleId.Should().Be("Normal", "children are promoted to the removed style's parent");
        doc.Styles[follower.Id].NextStyleId.Should().BeNull();
    }

    [Fact]
    public void DeleteStyleAndReassign_RefusesBuiltInAndUnknownStyles()
    {
        var doc = TextDocument.CreateEmpty();
        var paragraph = new Paragraph("body") { StyleId = "Normal" };
        doc.Blocks.Add(paragraph);

        StyleManager.DeleteStyleAndReassign(doc, "Normal").Should().BeEmpty();
        StyleManager.DeleteStyleAndReassign(doc, "Missing").Should().BeEmpty();
        paragraph.StyleId.Should().Be("Normal");
    }

    [Fact]
    public void DeleteStyleCommand_UndoRestoresCatalogAndEveryReference_AndRedoRepeats()
    {
        var doc = TextDocument.CreateEmpty();
        var linked = StyleManager.CreateLinkedStyle(
            doc, "Callout", "Normal", new RunFormatting { Bold = true }, ParagraphFormatting.Default);
        var characterId = linked.LinkedStyleId!;
        var styled = new Paragraph("styled") { StyleId = linked.Id };
        styled.Runs[0].StyleId = characterId;
        var plain = new Paragraph("plain") { StyleId = "Normal" };
        doc.Blocks.Add(styled);
        doc.Blocks.Add(plain);
        var context = new Context(doc);
        var command = new DeleteStyleCommand(linked.Id);

        command.HasEffect(context).Should().BeTrue();
        command.Apply(context);

        doc.Styles.Should().NotContainKey(linked.Id);
        styled.StyleId.Should().BeNull();
        styled.Runs[0].StyleId.Should().BeNull();

        command.Revert(context);

        doc.Styles[linked.Id].LinkedStyleId.Should().Be(characterId);
        doc.Styles[characterId].LinkedStyleId.Should().Be(linked.Id);
        doc.Styles[linked.Id].Run.Bold.Should().BeTrue();
        styled.StyleId.Should().Be(linked.Id);
        styled.Runs[0].StyleId.Should().Be(characterId);
        plain.StyleId.Should().Be("Normal");

        command.Apply(context);
        doc.Styles.Should().NotContainKey(linked.Id).And.NotContainKey(characterId);
        styled.StyleId.Should().BeNull();
    }

    [Fact]
    public void DeleteStyleCommand_OnBuiltInOrMissingStyle_HasNoEffectAndChangesNothing()
    {
        var doc = TextDocument.CreateEmpty();
        var context = new Context(doc);
        var count = doc.Styles.Count;

        foreach (var id in new[] { "Normal", "Missing" })
        {
            var command = new DeleteStyleCommand(id);
            command.HasEffect(context).Should().BeFalse();
            command.Apply(context);
            command.Revert(context);
        }

        doc.Styles.Should().HaveCount(count);
    }

    [Fact]
    public void StyleCatalogCommand_UndoRestoresLinkDefaultAndGalleryFlags()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Styles["Imported"] = new DocumentStyle
        {
            Id = "Imported",
            Name = "Imported",
            IsDefault = true,
            LinkedStyleId = "ImportedChar",
            QuickFormat = false,
            PreservedTableStyleXml = "<x/>",
        };
        var context = new Context(doc);
        var command = new StyleCatalogCommand("Modify Style", document => document.Styles.Remove("Imported"));

        command.Apply(context);
        command.Revert(context);

        var restored = doc.Styles["Imported"];
        restored.IsDefault.Should().BeTrue();
        restored.LinkedStyleId.Should().Be("ImportedChar");
        restored.QuickFormat.Should().BeFalse();
        restored.PreservedTableStyleXml.Should().Be("<x/>");
    }
}
