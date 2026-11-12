using FreeW.App.Presentation.Dialogs;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Tests;

public sealed class StyleDefinitionDialogSessionTests
{
    private static TextDocument DocumentWithStyles()
    {
        var document = TextDocument.CreateEmpty();
        BuiltInStyles.EnsureSeeded(document, "Heading1");
        StyleManager.CreateStyle(
            document, "Code", null, new RunFormatting { FontFamily = "Consolas" },
            ParagraphFormatting.Default, null, StyleType.Character);
        return document;
    }

    [Fact]
    public void NewSession_DefaultsToParagraphStyle_ShownInGallery_BasedOnCaretStyle()
    {
        var document = DocumentWithStyles();

        var session = StyleDefinitionDialogPlanner.CreateNewSession(document, "Normal");

        session.IsNew.Should().BeTrue();
        session.Title.Should().Be(StyleDefinitionDialogText.NewTitle);
        session.Kind.Should().Be(StyleDefinitionKind.Paragraph);
        session.CanChangeKind.Should().BeTrue();
        session.CanRename.Should().BeTrue();
        session.AddToGallery.Should().BeTrue();
        session.KindChoices.Select(choice => choice.Kind).Should().Equal(Enum.GetValues<StyleDefinitionKind>());
        session.BasedOnOptions[session.BasedOnIndex].Value.Should().Be("Normal");
        session.BasedOnOptions.Select(option => option.Value).Should().NotContain("Code",
            "a paragraph style can only be based on a paragraph style");
        session.NextStyleOptions[0].Key.Should().Be(StyleDefinitionDialogText.SameStyle);
        session.FormatMenu.Should().OnlyContain(entry => entry.IsEnabled);
        session.FormatMenu.Select(entry => entry.AutomationId).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void SelectingCharacterType_LimitsBasedOnToCharacterStyles_AndDisablesParagraphFormatting()
    {
        var document = DocumentWithStyles();
        var session = StyleDefinitionDialogPlanner.CreateNewSession(document, "Normal");
        session.SelectAlignment((int)TextAlignment.Center);
        session.SelectNextStyle(1);

        session.SelectKind((int)StyleDefinitionKind.Character);

        session.Kind.Should().Be(StyleDefinitionKind.Character);
        session.HasParagraphFormatting.Should().BeFalse();
        session.BasedOnIndex.Should().Be(0, "the paragraph based-on is not valid for a character style");
        session.BasedOnOptions.Skip(1).Select(option => option.Key).Should().Equal("Code");
        session.NextStyleOptions.Should().BeEmpty();
        session.Paragraph.Should().Be(ParagraphFormatting.Default);
        session.IsFormatCommandEnabled(StyleDefinitionFormatCommand.Font).Should().BeTrue();
        session.IsFormatCommandEnabled(StyleDefinitionFormatCommand.Paragraph).Should().BeFalse();
        session.IsFormatCommandEnabled(StyleDefinitionFormatCommand.Tabs).Should().BeFalse();
        session.IsFormatCommandEnabled(StyleDefinitionFormatCommand.Border).Should().BeFalse();

        session.SelectAlignment((int)TextAlignment.Right);
        session.ReplaceParagraph(new ParagraphFormatting { IndentLeftPt = 36 });
        session.Paragraph.Should().Be(ParagraphFormatting.Default);
    }

    [Fact]
    public void QuickControls_BuildTheDefinition()
    {
        var document = DocumentWithStyles();
        var session = StyleDefinitionDialogPlanner.CreateNewSession(document, "Normal");
        session.Name = "  Callout  ";
        session.SelectKind((int)StyleDefinitionKind.Linked);
        session.SetFontFamily(" Georgia ");
        session.SetFontSizeText("13.5");
        session.SetBold(true);
        session.SetItalic(true);
        session.SetUnderline(true);
        session.SetStrikethrough(true);
        session.SelectFontColor(StyleDialogPlanner.IndexOfColor("#C00000"));
        var yellow = session.HighlightColors.ToList().FindIndex(choice => choice.Hex == "#FFFF00");
        session.SelectHighlight(yellow);
        session.SelectAlignment((int)TextAlignment.Justify);
        session.SelectNextStyle(session.NextStyleOptions.ToList().FindIndex(option => option.Value == "Normal"));
        session.AddToGallery = false;

        var acceptance = session.PlanAcceptance();

        acceptance.IsAccepted.Should().BeTrue();
        var definition = acceptance.Definition!;
        definition.Name.Should().Be("Callout");
        definition.Kind.Should().Be(StyleDefinitionKind.Linked);
        definition.BasedOnId.Should().Be("Normal");
        definition.NextStyleId.Should().Be("Normal");
        definition.AddToGallery.Should().BeFalse();
        definition.Run.FontFamily.Should().Be("Georgia");
        definition.Run.FontSizePt.Should().Be(13.5);
        definition.Run.Bold.Should().BeTrue();
        definition.Run.Italic.Should().BeTrue();
        definition.Run.Underline.Should().BeTrue();
        definition.Run.Strikethrough.Should().BeTrue();
        definition.Run.ColorHex.Should().Be("#C00000");
        definition.Run.HighlightColorHex.Should().Be("#FFFF00");
        definition.Paragraph.Alignment.Should().Be(TextAlignment.Justify);
        session.HighlightIndex.Should().Be(yellow);
        session.HighlightColors[0].Hex.Should().BeNull("the first highlight choice clears the highlight");
    }

    [Fact]
    public void FormatMenuResults_ReplaceTheWorkingFormatting()
    {
        var session = StyleDefinitionDialogPlanner.CreateNewSession(DocumentWithStyles(), "Normal");
        session.Name = "Boxed";

        session.ReplaceRun(new RunFormatting { FontSizePt = 20, SmallCaps = true });
        session.ReplaceParagraph(new ParagraphFormatting
        {
            IndentLeftPt = 36,
            Border = new ParagraphBorder(),
            TabStops = [new TabStop(144)],
        });

        session.FontSizeText.Should().Be(20.ToString(System.Globalization.CultureInfo.CurrentCulture));
        var definition = session.PlanAcceptance().Definition!;
        definition.Run.SmallCaps.Should().BeTrue();
        definition.Paragraph.IndentLeftPt.Should().Be(36);
        definition.Paragraph.Border.Should().NotBeNull();
        definition.Paragraph.TabStops.Should().ContainSingle();
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-4")]
    [InlineData("1639")]
    [InlineData("NaN")]
    [InlineData("abc")]
    public void InvalidFontSize_IsRejected_NotSilentlyDropped(string text)
    {
        var session = StyleDefinitionDialogPlanner.CreateNewSession(DocumentWithStyles(), null);
        session.Name = "Sized";

        session.SetFontSizeText(text);
        var acceptance = session.PlanAcceptance();

        acceptance.IsAccepted.Should().BeFalse();
        acceptance.Error.Should().Be(StyleDefinitionValidationError.FontSize);
        acceptance.ErrorMessage.Should().Be(StyleDefinitionDialogText.FontSizeError);
        StyleDefinitionDialogSession.TryParseFontSize(text, out _).Should().BeFalse();
    }

    [Fact]
    public void EmptyFontSize_MeansInherit()
    {
        var session = StyleDefinitionDialogPlanner.CreateNewSession(DocumentWithStyles(), null);
        session.Name = "Sized";
        session.SetFontSizeText("14");
        session.SetFontSizeText(string.Empty);

        session.PlanAcceptance().Definition!.Run.FontSizePt.Should().BeNull();
        StyleDefinitionDialogSession.TryParseFontSize("1", out var min).Should().BeTrue();
        min.Should().Be(1);
        StyleDefinitionDialogSession.TryParseFontSize("1638", out var max).Should().BeTrue();
        max.Should().Be(1638);
        StyleDefinitionDialogSession.TryParseFontSize("10.26", out var rounded).Should().BeTrue();
        rounded.Should().Be(10.5, "Word stores font sizes in half-points");
    }

    [Fact]
    public void Acceptance_RejectsEmptyAndDuplicateNames()
    {
        var document = DocumentWithStyles();
        var session = StyleDefinitionDialogPlanner.CreateNewSession(document, null);

        session.Name = "   ";
        session.PlanAcceptance().Error.Should().Be(StyleDefinitionValidationError.EmptyName);

        session.Name = "code";
        var duplicate = session.PlanAcceptance();
        duplicate.Error.Should().Be(StyleDefinitionValidationError.DuplicateName);
        duplicate.ErrorMessage.Should().Contain("code");
    }

    [Fact]
    public void ModifySession_SeedsFromStyle_LocksType_AndAllowsKeepingItsOwnName()
    {
        var document = DocumentWithStyles();
        var linked = StyleManager.CreateLinkedStyle(
            document,
            "Callout",
            "Normal",
            new RunFormatting { Bold = true, FontSizePt = 14 },
            new ParagraphFormatting { Alignment = TextAlignment.Center },
            "Normal",
            quickFormat: false);
        var child = StyleManager.CreateStyle(
            document, "Child", linked.Id, RunFormatting.Default, ParagraphFormatting.Default);

        var session = StyleDefinitionDialogPlanner.CreateModifySession(document, document.Styles[linked.Id]);

        session.IsNew.Should().BeFalse();
        session.Title.Should().Be(StyleDefinitionDialogText.ModifyTitle);
        session.Kind.Should().Be(StyleDefinitionKind.Linked);
        session.CanChangeKind.Should().BeFalse();
        session.SelectKind((int)StyleDefinitionKind.Character);
        session.Kind.Should().Be(StyleDefinitionKind.Linked);
        session.CanRename.Should().BeTrue();
        session.Name.Should().Be("Callout");
        session.AddToGallery.Should().BeFalse();
        session.FontSizeText.Should().Be(14.ToString(System.Globalization.CultureInfo.CurrentCulture));
        session.AlignmentIndex.Should().Be((int)TextAlignment.Center);
        session.BasedOnOptions.Select(option => option.Value).Should().NotContain(
            [linked.Id, child.Id],
            "a style cannot be based on itself or on a style that already inherits from it");

        var acceptance = session.PlanAcceptance();
        acceptance.IsAccepted.Should().BeTrue("keeping the style's own name is not a duplicate");
        acceptance.Definition!.Run.Should().Be(linked.Run);
        acceptance.Definition.Paragraph.Should().BeSameAs(linked.Paragraph);
    }

    [Fact]
    public void ModifySession_OnBuiltInStyle_KeepsItsName()
    {
        var document = DocumentWithStyles();
        var session = StyleDefinitionDialogPlanner.CreateModifySession(document, document.Styles["Heading1"]);

        session.CanRename.Should().BeFalse();
        session.Name = "Renamed";

        session.PlanAcceptance().Definition!.Name.Should().Be(document.Styles["Heading1"].Name);
    }

    [Fact]
    public void Preview_ResolvesThroughBasedOnChainAndDocumentDefaults()
    {
        var document = DocumentWithStyles();
        var parent = StyleManager.CreateStyle(
            document,
            "Parent",
            null,
            new RunFormatting { FontFamily = "Georgia", FontSizePt = 18, Italic = true },
            new ParagraphFormatting { Alignment = TextAlignment.Right, IndentLeftPt = 24 });
        var session = StyleDefinitionDialogPlanner.CreateNewSession(document, parent.Id);
        session.SetBold(true);
        session.SelectFontColor(StyleDialogPlanner.IndexOfColor("#0070C0"));

        var preview = session.Preview;

        preview.SampleText.Should().Be(StyleDefinitionDialogText.PreviewSample);
        preview.FontFamily.Should().Be("Georgia");
        preview.FontSizePt.Should().Be(18);
        preview.Italic.Should().BeTrue("inherited from the based-on style");
        preview.Bold.Should().BeTrue("set on the style being defined");
        preview.ColorHex.Should().Be("#0070C0");
        preview.Alignment.Should().Be(TextAlignment.Right);
        preview.IndentLeftPt.Should().Be(24);

        session.SelectBasedOn(0);
        session.Preview.Italic.Should().BeFalse("the based-on style no longer contributes");
    }

    [Fact]
    public void Preview_OfCharacterStyle_ResolvesThroughItsCharacterBasedOn()
    {
        var document = DocumentWithStyles();
        var session = StyleDefinitionDialogPlanner.CreateNewSession(document, null);
        session.SelectKind((int)StyleDefinitionKind.Character);
        session.SelectBasedOn(1);
        session.SetStrikethrough(true);

        var preview = session.Preview;

        preview.FontFamily.Should().Be("Consolas");
        preview.Strikethrough.Should().BeTrue();
    }

    [Fact]
    public void Description_ListsOwnFormattingThenLineage()
    {
        var description = StyleDefinitionDescriptionBuilder.Describe(
            StyleDefinitionKind.Linked,
            new RunFormatting
            {
                FontFamily = "Georgia",
                FontSizePt = 14,
                Bold = true,
                ColorHex = "#FF0000",
                HighlightColorHex = "#FFFF00",
            },
            new ParagraphFormatting
            {
                Alignment = TextAlignment.Center,
                IndentLeftPt = 18,
                FirstLineIndentPt = -9,
                SpaceBeforePt = 6,
                KeepWithNext = true,
                Border = new ParagraphBorder(),
                TabStops = [new TabStop(72)],
            },
            addToGallery: true,
            basedOnName: "Normal",
            nextStyleName: "Body Text");

        var lines = description.ReplaceLineEndings("\n").Split('\n');
        lines.Should().HaveCount(3);
        lines[0].Should().StartWith("Font: Georgia, 14 pt, Bold, Font color: Red, Highlight: Yellow");
        lines[0].Should().Contain("Centered")
            .And.Contain("Indent: Left: 18 pt")
            .And.Contain("Hanging: 9 pt")
            .And.Contain("Space Before: 6 pt")
            .And.Contain("Keep with next")
            .And.Contain("Border")
            .And.Contain("Tab stops: 72 pt")
            .And.EndWith("Style: Linked, Show in the Styles gallery");
        lines[1].Should().Be("Based on: Normal");
        lines[2].Should().Be("Following style: Body Text");
    }

    [Fact]
    public void Description_OfCharacterStyle_OmitsParagraphFormatting()
    {
        var session = StyleDefinitionDialogPlanner.CreateNewSession(DocumentWithStyles(), null);
        session.SelectKind((int)StyleDefinitionKind.Character);
        session.SetItalic(true);
        session.AddToGallery = false;

        session.Description.Should().Be("Font: Italic");
    }

    [Fact]
    public void RequiredResourceKeys_ResolveToRealText()
    {
        StyleDefinitionDialogText.RequiredResourceKeys.Should().OnlyHaveUniqueItems();
        foreach (var key in StyleDefinitionDialogText.RequiredResourceKeys)
        {
            var text = FreeW.App.Localization.Loc.Get(key);
            text.Should().NotBeNullOrWhiteSpace();
            text.Should().NotContain("[[", $"resource {key} must exist in the neutral catalog");
        }
    }
}
