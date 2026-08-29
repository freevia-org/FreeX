namespace FreeW.Core.Model.Tests;

public class StyleManagerTests
{
    [Fact]
    public void CreateStyle_AddsStyle_AndGeneratesIdFromName()
    {
        var doc = TextDocument.CreateEmpty();

        var style = StyleManager.CreateStyle(
            doc, "My Heading", basedOnId: "Normal",
            new RunFormatting { Bold = true, FontSizePt = 14 },
            new ParagraphFormatting { Alignment = TextAlignment.Center });

        style.Id.Should().Be("MyHeading"); // spaces stripped
        style.Name.Should().Be("My Heading");
        style.BasedOnStyleId.Should().Be("Normal");
        style.Run.Bold.Should().BeTrue();
        style.Paragraph.Alignment.Should().Be(TextAlignment.Center);
        doc.Styles.Should().ContainKey("MyHeading");
        doc.Styles["MyHeading"].Should().BeSameAs(style);
    }

    [Fact]
    public void CreateStyle_TrimsName_AndIsTreatedAsParagraphStyle()
    {
        var doc = TextDocument.CreateEmpty();

        var style = StyleManager.CreateStyle(
            doc, "  Spaced  ", null, RunFormatting.Default, ParagraphFormatting.Default);

        style.Name.Should().Be("Spaced");
        style.Id.Should().Be("Spaced");
        style.Type.Should().Be(StyleType.Paragraph);
    }

    [Fact]
    public void CreateStyle_GeneratesUniqueId_OnCollisionWithBuiltIn()
    {
        var doc = TextDocument.CreateEmpty();

        // "Normal" is a built-in id; a custom style named "Normal" must not clobber it.
        var style = StyleManager.CreateStyle(
            doc, "Normal", null, RunFormatting.Default, ParagraphFormatting.Default);

        style.Id.Should().Be("Normal2");
        doc.Styles["Normal"].Name.Should().Be("Normal");      // built-in untouched
        doc.Styles["Normal2"].Should().BeSameAs(style);
    }

    [Fact]
    public void CreateStyle_GeneratesUniqueId_AcrossRepeatedNames()
    {
        var doc = TextDocument.CreateEmpty();

        var a = StyleManager.CreateStyle(doc, "Callout", null, RunFormatting.Default, ParagraphFormatting.Default);
        var b = StyleManager.CreateStyle(doc, "Callout", null, RunFormatting.Default, ParagraphFormatting.Default);
        var c = StyleManager.CreateStyle(doc, "Call out!", null, RunFormatting.Default, ParagraphFormatting.Default);

        a.Id.Should().Be("Callout");
        b.Id.Should().Be("Callout2");
        c.Id.Should().Be("Callout3"); // "Call out!" compacts to "Callout" -> next free suffix
    }

    [Fact]
    public void CreateStyle_FallsBackToStyleId_WhenNameHasNoAlphanumerics()
    {
        var doc = TextDocument.CreateEmpty();

        var style = StyleManager.CreateStyle(doc, "!!!", null, RunFormatting.Default, ParagraphFormatting.Default);

        style.Id.Should().Be("Style");
        style.Name.Should().Be("!!!");
    }

    [Fact]
    public void CreateStyle_IgnoresUnknownBasedOn()
    {
        var doc = TextDocument.CreateEmpty();

        var style = StyleManager.CreateStyle(
            doc, "Floating", basedOnId: "DoesNotExist", RunFormatting.Default, ParagraphFormatting.Default);

        style.BasedOnStyleId.Should().BeNull();
    }

    [Fact]
    public void CreateStyle_RejectsEmptyName()
    {
        var doc = TextDocument.CreateEmpty();

        var act = () => StyleManager.CreateStyle(doc, "   ", null, RunFormatting.Default, ParagraphFormatting.Default);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ModifyStyle_UpdatesFormattingNameAndBasedOn_KeepingId()
    {
        var doc = TextDocument.CreateEmpty();
        var created = StyleManager.CreateStyle(doc, "Callout", "Normal", RunFormatting.Default, ParagraphFormatting.Default);

        var updated = StyleManager.ModifyStyle(
            doc, created.Id,
            run: new RunFormatting { Italic = true },
            para: new ParagraphFormatting { Alignment = TextAlignment.Right },
            name: "Callout Box",
            basedOnId: "Heading1");

        updated.Should().NotBeNull();
        updated!.Id.Should().Be("Callout");          // id never changes
        updated.Name.Should().Be("Callout Box");
        updated.Run.Italic.Should().BeTrue();
        updated.Paragraph.Alignment.Should().Be(TextAlignment.Right);
        updated.BasedOnStyleId.Should().Be("Heading1");
        doc.Styles["Callout"].Should().BeSameAs(updated);
    }

    [Fact]
    public void CreateStyle_SetsNextStyle_WhenItNamesExistingStyle()
    {
        var doc = TextDocument.CreateEmpty();

        var style = StyleManager.CreateStyle(
            doc, "My Heading", basedOnId: "Heading1",
            RunFormatting.Default, ParagraphFormatting.Default, nextStyleId: "Normal");

        style.NextStyleId.Should().Be("Normal");
    }

    [Fact]
    public void CreateStyle_AllowsNextStyle_PointingAtItself()
    {
        var doc = TextDocument.CreateEmpty();

        // Word permits a style to chain its follow-on to itself (a body style that stays the body style).
        var style = StyleManager.CreateStyle(
            doc, "Body", basedOnId: "Normal",
            RunFormatting.Default, ParagraphFormatting.Default, nextStyleId: "Body");

        style.NextStyleId.Should().Be(style.Id);
    }

    [Fact]
    public void CreateStyle_DropsUnknownNextStyle()
    {
        var doc = TextDocument.CreateEmpty();

        var style = StyleManager.CreateStyle(
            doc, "Floating", basedOnId: null,
            RunFormatting.Default, ParagraphFormatting.Default, nextStyleId: "Ghost");

        style.NextStyleId.Should().BeNull();
    }

    [Fact]
    public void ModifyStyle_UpdatesAndClearsNextStyle()
    {
        var doc = TextDocument.CreateEmpty();
        var created = StyleManager.CreateStyle(
            doc, "Callout", "Normal", RunFormatting.Default, ParagraphFormatting.Default, nextStyleId: "Normal");

        StyleManager.ModifyStyle(doc, created.Id, nextStyleId: "Heading1")!.NextStyleId.Should().Be("Heading1");
        StyleManager.ModifyStyle(doc, created.Id, nextStyleId: "Ghost")!.NextStyleId.Should().Be("Heading1"); // unknown ignored
        StyleManager.ModifyStyle(doc, created.Id, clearNext: true)!.NextStyleId.Should().BeNull();
    }

    [Fact]
    public void ModifyStyle_PreservesPreservedNumbering_AndTableBorders()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Styles["ListStyle"] = new DocumentStyle
        {
            Id = "ListStyle",
            Name = "List Style",
            TableBorders = true,
            PreservedNumbering = new PreservedNumbering(7, 1),
        };

        var updated = StyleManager.ModifyStyle(doc, "ListStyle", run: new RunFormatting { Bold = true });

        updated!.TableBorders.Should().BeTrue();
        updated.PreservedNumbering.Should().Be(new PreservedNumbering(7, 1));
    }

    [Fact]
    public void ModifyStyle_PreservesLinkedStyleId_AndPreservedTableStyleXml()
    {
        // Mirrors what DocxReader produces for a real Word document: a paragraph style ("Heading1")
        // linked to its character style ("Heading1Char") via w:style/w:link, and a table style carrying
        // its exact imported XML payload. Editing either through Modify Style must not drop the pairing
        // or the preserved XML on the next save (freew-styles-inheritance F1).
        var doc = TextDocument.CreateEmpty();
        doc.Styles["Heading1"] = new DocumentStyle
        {
            Id = "Heading1",
            Name = "heading 1",
            Type = StyleType.Paragraph,
            LinkedStyleId = "Heading1Char",
        };
        doc.Styles["Heading1Char"] = new DocumentStyle
        {
            Id = "Heading1Char",
            Name = "Heading 1 Char",
            Type = StyleType.Character,
            LinkedStyleId = "Heading1",
        };
        doc.Styles["TableStyle1"] = new DocumentStyle
        {
            Id = "TableStyle1",
            Name = "Table Style 1",
            Type = StyleType.Table,
            PreservedTableStyleXml = "<w:style w:type=\"table\" w:styleId=\"TableStyle1\">...</w:style>",
        };

        // A simple color/formatting-only edit -- exactly what the Modify Style dialog does.
        var updatedHeading = StyleManager.ModifyStyle(doc, "Heading1", run: new RunFormatting { Bold = true });
        var updatedTable = StyleManager.ModifyStyle(doc, "TableStyle1", run: new RunFormatting { Bold = true });

        updatedHeading!.LinkedStyleId.Should().Be("Heading1Char");
        doc.Styles["Heading1"].LinkedStyleId.Should().Be("Heading1Char");
        // The other half of the pair, untouched by this call, must still resolve back.
        doc.Styles["Heading1Char"].LinkedStyleId.Should().Be("Heading1");

        updatedTable!.PreservedTableStyleXml.Should().Be("<w:style w:type=\"table\" w:styleId=\"TableStyle1\">...</w:style>");
    }

    [Fact]
    public void ModifyStyle_ReturnsNull_ForUnknownStyle()
    {
        var doc = TextDocument.CreateEmpty();

        StyleManager.ModifyStyle(doc, "Nope", run: new RunFormatting { Bold = true }).Should().BeNull();
    }

    [Fact]
    public void ModifyStyle_IgnoresUnknownBasedOn_AndSelfReference()
    {
        var doc = TextDocument.CreateEmpty();
        var created = StyleManager.CreateStyle(doc, "Callout", "Normal", RunFormatting.Default, ParagraphFormatting.Default);

        StyleManager.ModifyStyle(doc, created.Id, basedOnId: "Ghost")!.BasedOnStyleId.Should().Be("Normal");
        StyleManager.ModifyStyle(doc, created.Id, basedOnId: created.Id)!.BasedOnStyleId.Should().Be("Normal");
    }

    [Fact]
    public void ModifyStyle_CanClearBasedOn()
    {
        var doc = TextDocument.CreateEmpty();
        var created = StyleManager.CreateStyle(doc, "Callout", "Normal", RunFormatting.Default, ParagraphFormatting.Default);

        StyleManager.ModifyStyle(doc, created.Id, clearBasedOn: true)!.BasedOnStyleId.Should().BeNull();
    }

    // --- Indirect BasedOn cycle rejection (Modify Style UI path) ---

    [Fact]
    public void ModifyStyle_RejectsIndirectCycle_TwoStyles()
    {
        var doc = TextDocument.CreateEmpty();
        // A -> B (A currently based on B). Trying to point B at A would close a 2-cycle: A -> B -> A.
        doc.Styles["A"] = new DocumentStyle { Id = "A", Name = "A", BasedOnStyleId = "B" };
        doc.Styles["B"] = new DocumentStyle { Id = "B", Name = "B", BasedOnStyleId = null };

        var updated = StyleManager.ModifyStyle(doc, "B", basedOnId: "A");

        updated.Should().NotBeNull();
        updated!.BasedOnStyleId.Should().BeNull("an indirect cycle must be rejected exactly like a direct self-reference");
        doc.Styles["B"].BasedOnStyleId.Should().BeNull();
    }

    [Fact]
    public void ModifyStyle_RejectsIndirectCycle_ThreeStyleChain()
    {
        var doc = TextDocument.CreateEmpty();
        // A -> B -> C. Re-pointing C at A would close a 3-cycle: A -> B -> C -> A.
        doc.Styles["A"] = new DocumentStyle { Id = "A", Name = "A", BasedOnStyleId = "B" };
        doc.Styles["B"] = new DocumentStyle { Id = "B", Name = "B", BasedOnStyleId = "C" };
        doc.Styles["C"] = new DocumentStyle { Id = "C", Name = "C", BasedOnStyleId = null };

        var updated = StyleManager.ModifyStyle(doc, "C", basedOnId: "A");

        updated.Should().NotBeNull();
        updated!.BasedOnStyleId.Should().BeNull("a 3-deep cycle must be rejected just as much as a direct one");
        doc.Styles["C"].BasedOnStyleId.Should().BeNull();
    }

    [Fact]
    public void ModifyStyle_AllowsValidIndirectRebase_ThatIsNotACycle()
    {
        // Sibling no-regression: re-basing onto a style that is itself part of an (unrelated, non-cyclic)
        // chain must still succeed — the cycle guard must not over-correct and block legitimate rebasing.
        var doc = TextDocument.CreateEmpty();
        doc.Styles["Normal"] = new DocumentStyle { Id = "Normal", Name = "Normal" };
        doc.Styles["Heading1"] = new DocumentStyle { Id = "Heading1", Name = "Heading 1", BasedOnStyleId = "Normal" };
        doc.Styles["Heading2"] = new DocumentStyle
        {
            Id = "Heading2",
            Name = "Heading 2",
            BasedOnStyleId = "Normal",
            Run = new RunFormatting { Bold = true, FontSizePt = 13 },
        };

        // Heading2 -> Heading1 -> Normal is a valid, acyclic 3-level chain.
        var updated = StyleManager.ModifyStyle(doc, "Heading2", basedOnId: "Heading1");

        updated.Should().NotBeNull();
        updated!.BasedOnStyleId.Should().Be("Heading1");
        doc.Styles["Heading2"].BasedOnStyleId.Should().Be("Heading1");
    }

    [Fact]
    public void ResolveStyleColor_TerminatesSafely_OnCycleAlreadyPresentInLoadedDocument()
    {
        // Reader-side defence: a document loaded from disk (e.g. a hand-edited or malicious docx) can
        // already contain a cyclic w:basedOn chain that never went through ModifyStyle's guard at all.
        // The style-resolution walk that effective-formatting consumers perform (here, accessibility's
        // colour-contrast check) must terminate safely instead of hanging/stack-overflowing.
        var doc = TextDocument.CreateEmpty();
        doc.Styles["Loop1"] = new DocumentStyle { Id = "Loop1", Name = "Loop1", BasedOnStyleId = "Loop2" };
        doc.Styles["Loop2"] = new DocumentStyle { Id = "Loop2", Name = "Loop2", BasedOnStyleId = "Loop1" }; // cycle, bypassing ModifyStyle entirely
        doc.Properties.Title = "Cyclic Styles";
        doc.Blocks.Add(new Paragraph { StyleId = "Loop1", Runs = { new Run("Some text with no explicit run colour.") } });

        // Must return promptly (guarded walk), not hang or throw.
        var report = AccessibilityChecker.Check(doc);

        report.Should().NotBeNull();
    }

    [Fact]
    public void DeleteStyle_RemovesCustomStyle()
    {
        var doc = TextDocument.CreateEmpty();
        var created = StyleManager.CreateStyle(doc, "Callout", null, RunFormatting.Default, ParagraphFormatting.Default);

        StyleManager.DeleteStyle(doc, created.Id).Should().BeTrue();
        doc.Styles.Should().NotContainKey("Callout");
    }

    [Theory]
    [InlineData("Normal")]
    [InlineData("Heading1")]
    [InlineData("Heading2")]
    [InlineData("Title")]
    [InlineData("Subtitle")]
    [InlineData("Quote")]
    [InlineData("Caption")]
    public void DeleteStyle_RefusesBuiltIn(string builtInId)
    {
        var doc = TextDocument.CreateEmpty();

        StyleManager.DeleteStyle(doc, builtInId).Should().BeFalse();
        doc.Styles.Should().ContainKey(builtInId);
    }

    [Fact]
    public void DeleteStyle_ReturnsFalse_ForUnknownStyle()
    {
        var doc = TextDocument.CreateEmpty();

        StyleManager.DeleteStyle(doc, "Nope").Should().BeFalse();
    }

    [Fact]
    public void IsBuiltIn_TrueForSeededStyles_FalseForCustom()
    {
        var doc = TextDocument.CreateEmpty();
        var created = StyleManager.CreateStyle(doc, "Callout", null, RunFormatting.Default, ParagraphFormatting.Default);

        StyleManager.IsBuiltIn("Normal").Should().BeTrue();
        StyleManager.IsBuiltIn(created.Id).Should().BeFalse();
    }

    [Theory]
    [InlineData("Normal")]
    [InlineData("Caption")]
    [InlineData("IndexEntry")]
    [InlineData("TableOfFiguresEntry")]
    [InlineData("TableOfFiguresHeading")]
    public void DeleteStyle_RefusesEverySeededBuiltIn(string styleId)
    {
        var doc = TextDocument.CreateEmpty();
        doc.Styles.Should().ContainKey(styleId); // the style is actually seeded by AddBuiltInStyles

        StyleManager.DeleteStyle(doc, styleId).Should().BeFalse();
        doc.Styles.Should().ContainKey(styleId);
    }

    // ── GA1: the built-in guard must cover every style BuiltInStyles.Gallery can seed ──────────────
    // Regression coverage: BuiltInStyleIds used to be a hand-maintained subset that omitted NoSpacing,
    // ListParagraph, IntenseQuote and the character styles (Strong, Emphasis, SubtleEmphasis,
    // IntenseEmphasis), so IsBuiltIn/DeleteStyle let the user delete them and any content still
    // referencing the deleted styleId silently lost its formatting. Word never allows deleting these.

    [Theory]
    [InlineData("Strong")]
    [InlineData("Emphasis")]
    [InlineData("SubtleEmphasis")]
    [InlineData("IntenseEmphasis")]
    [InlineData("NoSpacing")]
    [InlineData("ListParagraph")]
    [InlineData("IntenseQuote")]
    [InlineData("Normal")]
    [InlineData("Heading1")]
    public void IsBuiltIn_TrueForEveryGalleryStyle_PreviouslyOmittedOrCovered(string styleId)
    {
        StyleManager.IsBuiltIn(styleId).Should().BeTrue($"'{styleId}' is seeded by BuiltInStyles.Gallery");
    }

    [Theory]
    [InlineData("Strong")]
    [InlineData("Emphasis")]
    [InlineData("SubtleEmphasis")]
    [InlineData("IntenseEmphasis")]
    [InlineData("NoSpacing")]
    [InlineData("ListParagraph")]
    [InlineData("IntenseQuote")]
    [InlineData("Normal")]
    [InlineData("Heading1")]
    public void DeleteStyle_RefusesEveryGalleryStyle_PreviouslyDeletable(string styleId)
    {
        var doc = TextDocument.CreateEmpty();
        BuiltInStyles.EnsureSeeded(doc, styleId); // ensure present regardless of default seeding
        doc.Styles.Should().ContainKey(styleId);

        StyleManager.DeleteStyle(doc, styleId).Should().BeFalse(
            $"'{styleId}' is a genuine Word built-in and must never be deletable");
        doc.Styles.Should().ContainKey(styleId);
    }

    [Fact]
    public void IsBuiltIn_IsTrueForEveryStyleInBuiltInStylesGallery()
    {
        // Drift guard: whatever BuiltInStyles.Gallery seeds as a built-in, the delete-guard must protect.
        // This fails automatically if a new gallery style is ever added without updating the guard logic.
        foreach (var descriptor in BuiltInStyles.Gallery)
            StyleManager.IsBuiltIn(descriptor.Id).Should().BeTrue(
                $"gallery style '{descriptor.Id}' must be treated as built-in");
    }

    [Fact]
    public void DeleteStyle_ACustomStyle_IsStillDeletable()
    {
        var doc = TextDocument.CreateEmpty();
        var created = StyleManager.CreateStyle(doc, "My Custom Style", null, RunFormatting.Default, ParagraphFormatting.Default);

        StyleManager.IsBuiltIn(created.Id).Should().BeFalse();
        StyleManager.DeleteStyle(doc, created.Id).Should().BeTrue();
        doc.Styles.Should().NotContainKey(created.Id);
    }

    // ── F2: deleting a style another style is based on must not leave a dangling BasedOnStyleId ─────
    // Regression coverage: DeleteStyle used to only guard built-in/unknown ids and otherwise just remove
    // the entry, leaving any other style's BasedOnStyleId pointing at an id no longer in the catalog. The
    // formatting-resolver walk (ResolveStyleChain/StyleChain) stops the instant TryGetValue fails, so every
    // property contributed by anything *above* the deleted style in its own chain silently vanished from
    // the cascade too. Word instead "promotes" such children onto the deleted style's own based-on.

    [Fact]
    public void DeleteStyle_PromotesChild_OntoDeletedStylesOwnBasedOn_InsteadOfDangling()
    {
        var doc = TextDocument.CreateEmpty();
        var grandparent = StyleManager.CreateStyle(
            doc, "Grandparent", null, RunFormatting.Default,
            new ParagraphFormatting { Alignment = TextAlignment.Center });
        var parent = StyleManager.CreateStyle(doc, "Parent", grandparent.Id, RunFormatting.Default, ParagraphFormatting.Default);
        var child = StyleManager.CreateStyle(doc, "Child", parent.Id, RunFormatting.Default, ParagraphFormatting.Default);

        StyleManager.DeleteStyle(doc, parent.Id).Should().BeTrue();

        doc.Styles.Should().NotContainKey(parent.Id);
        doc.Styles[child.Id].BasedOnStyleId.Should().Be(grandparent.Id,
            "the child must be re-pointed onto the deleted style's own parent, not left dangling");

        // The promoted chain must resolve end-to-end: every id it now walks through must actually exist,
        // exactly what DocumentParagraphFormattingResolver.ResolveStyleChain / DocumentRunFormattingResolver
        // .StyleChain require in order to keep pulling in the grandparent's own contribution.
        var current = doc.Styles[child.Id].BasedOnStyleId;
        var hops = 0;
        while (current is not null)
        {
            doc.Styles.Should().ContainKey(current, $"the promoted based-on chain must not dangle at '{current}'");
            current = doc.Styles[current].BasedOnStyleId;
            hops++;
            hops.Should().BeLessThan(10, "guards against an infinite loop masking the assertion above");
        }
    }

    [Fact]
    public void DeleteStyle_ChildOfARootStyle_ClearsBasedOn_InsteadOfDangling()
    {
        // Matches the finding's exact repro shape: the deleted style itself has no based-on (it is a root
        // custom style), so promotion has nothing to re-point onto — the child's BasedOnStyleId must become
        // null (an ordinary, resolvable "no based-on" style), never the stale, now-nonexistent id.
        var doc = TextDocument.CreateEmpty();
        var baseStyle = StyleManager.CreateStyle(
            doc, "BaseCustom", null, RunFormatting.Default,
            new ParagraphFormatting { Alignment = TextAlignment.Center });
        var child = StyleManager.CreateStyle(doc, "ChildCustom", baseStyle.Id, RunFormatting.Default, ParagraphFormatting.Default);

        StyleManager.DeleteStyle(doc, baseStyle.Id).Should().BeTrue();

        doc.Styles.Should().NotContainKey(baseStyle.Id);
        doc.Styles[child.Id].BasedOnStyleId.Should().BeNull(
            "with no grandparent to promote onto, the child must end up with no based-on rather than a dangling one");
    }

    [Fact]
    public void DeleteStyle_LeafStyleNobodyBasesOn_TouchesNoOtherStyle()
    {
        // Adjacent case: deleting a style that nothing else is based on must behave exactly as before —
        // only the deleted entry itself is removed, every other custom style's BasedOnStyleId is untouched.
        var doc = TextDocument.CreateEmpty();
        var unrelatedParent = StyleManager.CreateStyle(doc, "UnrelatedParent", null, RunFormatting.Default, ParagraphFormatting.Default);
        var unrelatedChild = StyleManager.CreateStyle(doc, "UnrelatedChild", unrelatedParent.Id, RunFormatting.Default, ParagraphFormatting.Default);
        var leaf = StyleManager.CreateStyle(doc, "Leaf", unrelatedParent.Id, RunFormatting.Default, ParagraphFormatting.Default);

        StyleManager.DeleteStyle(doc, leaf.Id).Should().BeTrue();

        doc.Styles.Should().NotContainKey(leaf.Id);
        doc.Styles[unrelatedChild.Id].BasedOnStyleId.Should().Be(unrelatedParent.Id,
            "a style unrelated to the deleted one must keep its own based-on untouched");
    }

    // ── GA3: CreateStyle must not allow a duplicate display name ───────────────────────────────────
    // Regression coverage: CreateStyle used to only disambiguate the styleId, not the display Name, so
    // creating a style named "Heading 1" (colliding with the built-in "Heading1"/"Heading 1") produced a
    // distinct id but a duplicate w:name — which Word treats as invalid / merges on load.

    [Fact]
    public void CreateStyle_DisambiguatesName_OnCollisionWithBuiltInDisplayName()
    {
        var doc = TextDocument.CreateEmpty();

        var style = StyleManager.CreateStyle(doc, "Heading 1", null, RunFormatting.Default, ParagraphFormatting.Default);

        style.Name.Should().NotBe("Heading 1");
        style.Name.Should().Be("Heading 1 2");
        doc.Styles.Values.Count(s => string.Equals(s.Name, style.Name, StringComparison.OrdinalIgnoreCase))
            .Should().Be(1);
        doc.Styles["Heading1"].Name.Should().Be("Heading 1"); // built-in untouched
    }

    [Fact]
    public void CreateStyle_DisambiguatesName_CaseInsensitively_OnCollisionWithCustomStyle()
    {
        var doc = TextDocument.CreateEmpty();
        StyleManager.CreateStyle(doc, "Callout", null, RunFormatting.Default, ParagraphFormatting.Default);

        // Same name, different case — Word treats style names as case-insensitively unique.
        var second = StyleManager.CreateStyle(doc, "CALLOUT", null, RunFormatting.Default, ParagraphFormatting.Default);

        second.Name.Should().Be("CALLOUT 2");
        doc.Styles.Values.Select(s => s.Name.ToUpperInvariant())
            .Count(n => n == "CALLOUT" || n == "CALLOUT 2")
            .Should().Be(2);
    }

    [Fact]
    public void CreateStyle_DisambiguatesName_AcrossRepeatedCollisions()
    {
        var doc = TextDocument.CreateEmpty();
        var a = StyleManager.CreateStyle(doc, "Callout", null, RunFormatting.Default, ParagraphFormatting.Default);
        var b = StyleManager.CreateStyle(doc, "Callout", null, RunFormatting.Default, ParagraphFormatting.Default);
        var c = StyleManager.CreateStyle(doc, "Callout", null, RunFormatting.Default, ParagraphFormatting.Default);

        a.Name.Should().Be("Callout");
        b.Name.Should().Be("Callout 2");
        c.Name.Should().Be("Callout 3");
        // No two styles share a name (no duplicate w:name would be emitted).
        doc.Styles.Values.Select(s => s.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count()
            .Should().Be(doc.Styles.Count);
    }

    [Fact]
    public void CreateStyle_NoNameCollision_KeepsNameUnchanged()
    {
        var doc = TextDocument.CreateEmpty();

        var style = StyleManager.CreateStyle(doc, "Totally Unique Name", null, RunFormatting.Default, ParagraphFormatting.Default);

        style.Name.Should().Be("Totally Unique Name");
    }

    [Fact]
    public void FindStyleIdByName_ReturnsMatchingStyleId_RegardlessOfCase_AndNullWhenAbsent()
    {
        // Round 167: this is the shared lookup DocumentMerge.TransferStyles and DocumentCompare's style
        // union both now consult before adding a style under a possibly-new id, so a name collision under a
        // NEW id is caught the same way a same-id collision already was.
        var doc = TextDocument.CreateEmpty();
        doc.Styles["CustomId"] = new DocumentStyle { Id = "CustomId", Name = "My Style" };

        StyleManager.FindStyleIdByName(doc, "my style").Should().Be("CustomId"); // case-insensitive
        StyleManager.FindStyleIdByName(doc, "Nonexistent Name").Should().BeNull();
    }
}
