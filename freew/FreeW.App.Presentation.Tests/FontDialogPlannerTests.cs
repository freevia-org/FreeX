using System.Globalization;
using FreeW.App.Presentation.Dialogs;

namespace FreeW.App.Presentation.Tests;

public sealed class FontDialogPlannerTests
{
    [Fact]
    public void Catalogs_ExposeWordFontDialogChoicesInDisplayOrder()
    {
        FontDialogPlanner.Text.Title.Should().Be("Font");
        FontDialogPlanner.Text.FontFamilyLabel.Should().Be("Font family:");
        FontDialogPlanner.Text.DoubleStrikethroughLabel.Should().Be("Double strikethrough");
        FontDialogPlanner.Text.StylisticSetLabel.Should().Be("Stylistic set (1–20):");

        FontDialogPlanner.ColorChoices.Select(choice => choice.Label)
            .Should().Equal("Automatic", "Black", "Dark Red", "Red", "Blue accent", "Blue", "Green", "Purple", "Grey");

        FontDialogPlanner.ColorChoices.Select(choice => choice.Hex)
            .Should().Equal(null, "#000000", "#C00000", "#FF0000", "#2F5496", "#0070C0", "#00B050", "#7030A0", "#7F7F7F");

        FontDialogPlanner.SizeChoices.Select(choice => choice.Label)
            .Should().Equal("8", "9", "10", "11", "12", "14", "16", "18", "24", "28", "36", "48", "72");

        FontDialogPlanner.LigatureChoices.Select(choice => choice.Mode)
            .Should().Equal(
                LigatureMode.None,
                LigatureMode.NoneExplicit,
                LigatureMode.Standard,
                LigatureMode.Contextual,
                LigatureMode.StandardContextual,
                LigatureMode.Historical,
                LigatureMode.Discretional,
                LigatureMode.All);

        FontDialogPlanner.NumberFormChoices.Select(choice => choice.Form)
            .Should().Equal(NumberForm.Default, NumberForm.Lining, NumberForm.OldStyle);

        FontDialogPlanner.NumberSpacingChoices.Select(choice => choice.Spacing)
            .Should().Equal(NumberSpacing.Default, NumberSpacing.Proportional, NumberSpacing.Tabular);
    }

    [Fact]
    public void Surface_GroupsFieldsAndEffectsForEitherRenderer()
    {
        var surface = FontDialogPlanner.Surface;

        surface.Title.Should().Be(FontDialogPlanner.Text.Title);
        surface.WindowWidth.Should().Be(460);
        surface.ActionButtonWidth.Should().Be(72);
        surface.Tabs.Select(tab => tab.Kind)
            .Should().Equal(FontDialogTabKind.Font, FontDialogTabKind.Advanced);
        surface.Tabs[0].Fields.Should().Equal(
            FontDialogFieldKind.FontFamily,
            FontDialogFieldKind.FontSize,
            FontDialogFieldKind.Color);
        surface.Tabs[1].Fields.Should().HaveCount(7);
        surface.Fields.Select(field => field.AutomationId).Should().OnlyHaveUniqueItems();
        surface.Effects.Select(effect => effect.Kind).Should().Equal(Enum.GetValues<FontDialogEffectKind>());
        surface.Effects.Select(effect => effect.AutomationId).Should().OnlyHaveUniqueItems();
        surface.Effect(FontDialogEffectKind.Hidden).IsThreeState.Should().BeTrue();
        surface.Effect(FontDialogEffectKind.SmallCaps).IsThreeState.Should().BeFalse();
        surface.Field(FontDialogFieldKind.StylisticSet).ToolTip
            .Should().Be(FontDialogPlanner.StylisticSetToolTip);
    }

    [Fact]
    public void VisualMetrics_OwnWpfAuthorityAndAvaloniaTemplateCompensation()
    {
        var metrics = FontDialogPlanner.VisualMetrics;

        metrics.WpfRootMargin.Should().Be(new FontDialogThickness(12, 12, 12, 12));
        metrics.WpfTabContentMargin.Should().Be(new FontDialogThickness(10, 10, 10, 10));
        metrics.AvaloniaRootMargin.Should().Be(new FontDialogThickness(12, 12, 13, 12));
        metrics.AvaloniaFontTabContentMargin.Should().Be(new FontDialogThickness(12, 14, 13, 11));
        metrics.AvaloniaAdvancedTabContentMargin.Should().Be(new FontDialogThickness(10, 12, 10, 10));
        metrics.AvaloniaTabPaneMargin.Should().Be(new FontDialogThickness(-12, -1, -12, 0));
        metrics.FieldLabelMargin.Should().Be(new FontDialogThickness(0, 0, 0, 2));
        metrics.AvaloniaColorControlMargin.Should().Be(new FontDialogThickness(0, -1, 0, 9));
        metrics.AvaloniaLabelLineHeight.Should().Be(17);
        metrics.FieldControlMargin.Should().Be(new FontDialogThickness(0, 0, 0, 8));
        metrics.ActionRowMargin.Should().Be(new FontDialogThickness(0, 10, 0, 0));
        metrics.AvaloniaActionRowMargin.Should().Be(new FontDialogThickness(0, 13, 0, 0));
        metrics.EffectTrailingMargin.Should().Be(12);
        metrics.EffectBottomMargin.Should().Be(4);
        metrics.AvaloniaEffectBottomMargin.Should().Be(2);
    }

    [Fact]
    public void AvaloniaProductionValidationUsesTheSharedWarningFeedbackPort()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var source = File.ReadAllText(Path.Combine(
            root, "freew", "FreeW.App.Avalonia", "FontDialog.cs"));

        source.Should().Contain("IUserMessageService? messageService = null");
        source.Should().Contain("messageService ?? new AvaloniaUserMessageService(this)");
        source.Should().Contain("private async void OnOk()");
        source.Should().Contain("await _messageService.ShowWarningAsync(");
        source.Should().NotContain("_status.IsVisible = true");
    }

    [Fact]
    public void CaptureControlState_MapsTypedEffectsToAcceptanceState()
    {
        var state = FontDialogPlanner.CaptureControlState(
            "Aptos",
            "11",
            colorIndex: 2,
            characterSpacingText: "1",
            kerningMinSizeText: "8",
            positionText: "-2",
            ligatureIndex: 3,
            stylisticSetText: "4",
            numberFormIndex: 1,
            numberSpacingIndex: 2,
            kind => kind switch
            {
                FontDialogEffectKind.Bold or FontDialogEffectKind.Hidden => true,
                FontDialogEffectKind.Italic => null,
                _ => false,
            });

        state.FontFamilyText.Should().Be("Aptos");
        state.Bold.Should().BeTrue();
        state.Italic.Should().BeNull();
        state.Hidden.Should().BeTrue();
        state.SmallCaps.Should().BeFalse();
        state.NumberSpacingIndex.Should().Be(2);
    }

    [Fact]
    public void BuildInitialState_ProjectsCurrentRunFormattingToDialogState()
    {
        var current = new RunFormatting
        {
            FontFamily = "Aptos",
            FontSizePt = 10.5,
            ColorHex = "#ff0000",
            Bold = true,
            Italic = true,
            Underline = true,
            Strikethrough = true,
            SmallCaps = true,
            AllCaps = true,
            VerticalAlign = VerticalAlign.Subscript,
            CharacterSpacingPt = 1.25,
            KerningMinSizePt = 12,
            PositionPt = -2.5,
            Ligatures = LigatureMode.StandardContextual,
            StylisticSet = 7,
            NumberForm = NumberForm.OldStyle,
            NumberSpacing = NumberSpacing.Tabular,
            Hidden = true,
        };

        var state = FontDialogPlanner.BuildInitialState(current, CultureInfo.InvariantCulture);

        state.FontFamilyText.Should().Be("Aptos");
        state.FontSizeText.Should().Be("10.5");
        state.ColorIndex.Should().Be(3);
        state.Bold.Should().BeTrue();
        state.Italic.Should().BeTrue();
        state.Underline.Should().BeTrue();
        state.Strikethrough.Should().BeTrue();
        state.SmallCaps.Should().BeTrue();
        state.AllCaps.Should().BeTrue();
        state.Superscript.Should().BeFalse();
        state.Subscript.Should().BeTrue();
        state.CharacterSpacingText.Should().Be("1.25");
        state.KerningMinSizeText.Should().Be("12");
        state.PositionText.Should().Be("-2.5");
        state.LigatureIndex.Should().Be(4);
        state.StylisticSetText.Should().Be("7");
        state.NumberFormIndex.Should().Be(2);
        state.NumberSpacingIndex.Should().Be(2);
        state.Hidden.Should().BeTrue();
    }

    [Fact]
    public void BuildInitialState_UsesInheritedAndDefaultFieldsForBlankOrUnknownSelections()
    {
        var current = new RunFormatting
        {
            ColorHex = "#123456",
            CharacterSpacingPt = 0,
            PositionPt = 0,
            Ligatures = (LigatureMode)999,
            NumberForm = (NumberForm)999,
            NumberSpacing = (NumberSpacing)999,
        };

        var state = FontDialogPlanner.BuildInitialState(current, CultureInfo.InvariantCulture);

        state.FontFamilyText.Should().BeEmpty();
        state.FontSizeText.Should().BeEmpty();
        state.ColorIndex.Should().Be(0);
        state.CharacterSpacingText.Should().Be("0");
        state.KerningMinSizeText.Should().BeEmpty();
        state.PositionText.Should().Be("0");
        state.LigatureIndex.Should().Be(0);
        state.StylisticSetText.Should().BeEmpty();
        state.NumberFormIndex.Should().Be(0);
        state.NumberSpacingIndex.Should().Be(0);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void BuildInitialState_ProjectsSingleAndDoubleStrikethroughIndependently(
        bool strikethrough,
        bool doubleStrikethrough)
    {
        var current = new RunFormatting
        {
            Strikethrough = strikethrough,
            DoubleStrikethrough = doubleStrikethrough,
        };

        var state = FontDialogPlanner.BuildInitialState(current, CultureInfo.InvariantCulture);

        state.Strikethrough.Should().Be(strikethrough);
        state.DoubleStrikethrough.Should().Be(doubleStrikethrough);
    }

    [Theory]
    [InlineData("0", "0", "0", "", "Enter a positive font size in points.")]
    [InlineData("bad", "0", "0", "", "Enter a positive font size in points.")]
    [InlineData("11", "bad", "0", "", "Enter a valid character spacing in points.")]
    [InlineData("11", "0", "-1", "", "Enter a non-negative kerning threshold in points, or leave blank.")]
    [InlineData("11", "0", "bad", "", "Enter a non-negative kerning threshold in points, or leave blank.")]
    [InlineData("11", "0", "", "bad", "Enter a valid position offset in points.")]
    public void TryBuildResult_ValidatesSizeSpacingKerningAndPosition(
        string fontSizeText,
        string spacingText,
        string kerningText,
        string positionText,
        string expectedMessage)
    {
        var input = ValidInput() with
        {
            FontSizeText = fontSizeText,
            CharacterSpacingText = spacingText,
            KerningMinSizeText = kerningText,
            PositionText = positionText,
        };

        FontDialogPlanner.TryBuildResult(
                input,
                RunFormatting.Default,
                CultureInfo.InvariantCulture,
                out var result,
                out var errorMessage)
            .Should().BeFalse();

        result.Should().BeNull();
        errorMessage.Should().Be(expectedMessage);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("21")]
    [InlineData("bad")]
    public void TryBuildResult_ValidatesStylisticSetRange(string stylisticSetText)
    {
        var input = ValidInput() with { StylisticSetText = stylisticSetText };

        FontDialogPlanner.TryBuildResult(
                input,
                RunFormatting.Default,
                CultureInfo.InvariantCulture,
                out var result,
                out var errorMessage)
            .Should().BeFalse();

        result.Should().BeNull();
        errorMessage.Should().Be(FontDialogPlanner.StylisticSetValidationMessage);
    }

    [Fact]
    public void TryBuildResult_ConstructsRunFormattingAndPreservesUneditedFields()
    {
        var current = new RunFormatting
        {
            HighlightColorHex = "#FFFF00",
            CharacterBorder = new ParagraphBorder("#111111", 1),
            CharacterShadingHex = "#CCCCCC",
        };
        var input = ValidInput() with
        {
            FontFamilyText = "  Aptos  ",
            FontSizeText = "11.5",
            ColorIndex = 5,
            Bold = true,
            Italic = true,
            Underline = true,
            Strikethrough = true,
            SmallCaps = true,
            AllCaps = true,
            Superscript = true,
            Subscript = true,
            CharacterSpacingText = "-1.5",
            KerningMinSizeText = "8",
            PositionText = "-3",
            LigatureIndex = 7,
            StylisticSetText = "12",
            NumberFormIndex = 2,
            NumberSpacingIndex = 1,
        };

        FontDialogPlanner.TryBuildResult(
                input,
                current,
                CultureInfo.InvariantCulture,
                out var result,
                out var errorMessage)
            .Should().BeTrue();

        errorMessage.Should().BeNull();
        result.Should().NotBeNull();
        result!.FontFamily.Should().Be("Aptos");
        result.FontSizePt.Should().Be(11.5);
        result.ColorHex.Should().Be("#0070C0");
        result.Bold.Should().BeTrue();
        result.Italic.Should().BeTrue();
        result.Underline.Should().BeTrue();
        result.Strikethrough.Should().BeTrue();
        result.SmallCaps.Should().BeTrue();
        result.AllCaps.Should().BeTrue();
        result.VerticalAlign.Should().Be(VerticalAlign.Superscript);
        result.CharacterSpacingPt.Should().Be(-1.5);
        result.KerningMinSizePt.Should().Be(8);
        result.PositionPt.Should().Be(-3);
        result.Ligatures.Should().Be(LigatureMode.All);
        result.StylisticSet.Should().Be(12);
        result.NumberForm.Should().Be(NumberForm.OldStyle);
        result.NumberSpacing.Should().Be(NumberSpacing.Proportional);
        result.HighlightColorHex.Should().Be("#FFFF00");
        result.CharacterBorder.Should().Be(current.CharacterBorder);
        result.CharacterShadingHex.Should().Be("#CCCCCC");
    }

    [Fact]
    public void TryBuildResult_BlankOptionalFieldsClearInheritedFontSelections()
    {
        var input = ValidInput() with
        {
            FontFamilyText = "   ",
            FontSizeText = "",
            KerningMinSizeText = "",
            StylisticSetText = "",
            ColorIndex = -1,
            LigatureIndex = -1,
            NumberFormIndex = -1,
            NumberSpacingIndex = -1,
        };

        FontDialogPlanner.TryBuildResult(
                input,
                RunFormatting.Default,
                CultureInfo.InvariantCulture,
                out var result,
                out _)
            .Should().BeTrue();

        result!.FontFamily.Should().BeNull();
        result.FontSizePt.Should().BeNull();
        result.ColorHex.Should().BeNull();
        result.KerningMinSizePt.Should().BeNull();
        result.StylisticSet.Should().BeNull();
        result.Ligatures.Should().Be(LigatureMode.None);
        result.NumberForm.Should().Be(NumberForm.Default);
        result.NumberSpacing.Should().Be(NumberSpacing.Default);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void TryBuildResult_AppliesSingleAndDoubleStrikethroughIndependently(
        bool strikethrough,
        bool doubleStrikethrough)
    {
        var input = ValidInput() with
        {
            Strikethrough = strikethrough,
            DoubleStrikethrough = doubleStrikethrough,
        };

        FontDialogPlanner.TryBuildResult(
                input,
                RunFormatting.Default,
                CultureInfo.InvariantCulture,
                out var result,
                out var errorMessage)
            .Should().BeTrue();

        errorMessage.Should().BeNull();
        result.Should().NotBeNull();
        result!.Strikethrough.Should().Be(strikethrough);
        result.DoubleStrikethrough.Should().Be(doubleStrikethrough);
    }

    [Fact]
    public void TryBuildResult_AppliesHiddenWithoutChangingWebHidden()
    {
        var input = ValidInput() with { Hidden = true };
        var current = RunFormatting.Default with { WebHidden = true };

        FontDialogPlanner.TryBuildResult(
                input,
                current,
                CultureInfo.InvariantCulture,
                out var result,
                out var errorMessage)
            .Should().BeTrue();

        errorMessage.Should().BeNull();
        result.Should().NotBeNull();
        result!.Hidden.Should().BeTrue();
        result.WebHidden.Should().BeTrue();
    }

    [Fact]
    public void Session_ProjectsMixedSelectionToIndeterminateControls()
    {
        var session = FontDialogPlanner.CreateSession(
            new FontDialogSelectionState(
                RunFormatting.Default with
                {
                    FontFamily = "Arial",
                    FontSizePt = 11,
                    Bold = true,
                    DoubleStrikethrough = true,
                },
                BoldIndeterminate: true,
                FamilyIndeterminate: true,
                SizeIndeterminate: true,
                DoubleStrikethroughIndeterminate: true),
            CultureInfo.InvariantCulture);

        session.InitialState.FontFamilyText.Should().BeEmpty();
        session.InitialState.FontSizeText.Should().BeEmpty();
        session.InitialState.Bold.Should().BeNull();
        session.InitialState.DoubleStrikethrough.Should().BeNull();
    }

    [Fact]
    public void Session_UnchangedMixedControlsStayUnapplied()
    {
        var session = FontDialogPlanner.CreateSession(
            new FontDialogSelectionState(
                RunFormatting.Default with { FontFamily = "Arial", FontSizePt = 11, Bold = true },
                BoldIndeterminate: true,
                FamilyIndeterminate: true,
                SizeIndeterminate: true),
            CultureInfo.InvariantCulture);

        var acceptance = session.PlanAcceptance(session.InitialState);

        acceptance.IsAccepted.Should().BeTrue();
        acceptance.Result!.Bold.Should().BeNull();
        acceptance.Result.FamilyChanged.Should().BeFalse();
        acceptance.Result.SizeChanged.Should().BeFalse();
        var plan = session.BuildApplyPlan(acceptance.Result);
        plan.Commands.OfType<FontDialogApplyCommand.SetFamily>().Should().BeEmpty();
        plan.Commands.OfType<FontDialogApplyCommand.SetSize>().Should().BeEmpty();
        plan.Commands.OfType<FontDialogApplyCommand.Toggle>()
            .Should().NotContain(toggle => toggle.Target == FontDialogToggleCommand.Bold);
    }

    [Fact]
    public void Session_UnchangedFormattingProducesNoApplyCommands()
    {
        var original = RunFormatting.Default with
        {
            FontFamily = "Aptos",
            FontSizePt = 11,
            CharacterSpacingPt = 1.25,
            KerningMinSizePt = 8,
            PositionPt = -2,
            Ligatures = LigatureMode.Standard,
            StylisticSet = 4,
            NumberForm = NumberForm.Lining,
            NumberSpacing = NumberSpacing.Tabular,
        };
        var session = FontDialogPlanner.CreateSession(original, CultureInfo.InvariantCulture);

        var acceptance = session.PlanAcceptance(session.InitialState);
        var plan = session.BuildApplyPlan(acceptance.Result!);

        acceptance.IsAccepted.Should().BeTrue();
        acceptance.Result!.AdvancedChanged.Should().BeFalse();
        plan.Commands.Should().BeEmpty();
    }

    [Fact]
    public void Session_ExplicitMixedControlChangesProduceOrderedApplyPlan()
    {
        var session = FontDialogPlanner.CreateSession(
            new FontDialogSelectionState(
                RunFormatting.Default with { FontFamily = "Arial", FontSizePt = 11, Bold = true },
                BoldIndeterminate: true,
                FamilyIndeterminate: true,
                SizeIndeterminate: true),
            CultureInfo.InvariantCulture);
        var state = session.InitialState with
        {
            FontFamilyText = "Cambria",
            FontSizeText = "14",
            Bold = false,
            Superscript = true,
            SmallCaps = true,
            ColorIndex = 3,
            CharacterSpacingText = "1.5",
        };

        var acceptance = session.PlanAcceptance(state);
        var plan = session.BuildApplyPlan(acceptance.Result!);

        acceptance.IsAccepted.Should().BeTrue();
        acceptance.Result!.Bold.Should().BeFalse();
        acceptance.Result.AdvancedChanged.Should().BeTrue();
        plan.UndoLabel.Should().Be(FontDialogSession.UndoLabel);
        plan.Commands.Should().SatisfyRespectively(
            command => command.Should().BeOfType<FontDialogApplyCommand.SetFamily>(),
            command => command.Should().BeOfType<FontDialogApplyCommand.SetSize>(),
            command => command.Should().BeEquivalentTo(
                new FontDialogApplyCommand.Toggle(FontDialogToggleCommand.Bold)),
            command => command.Should().BeEquivalentTo(
                new FontDialogApplyCommand.Toggle(FontDialogToggleCommand.Superscript)),
            command => command.Should().BeOfType<FontDialogApplyCommand.SetColor>(),
            command => command.Should().BeEquivalentTo(
                new FontDialogApplyCommand.Toggle(FontDialogToggleCommand.SmallCaps)),
            command => command.Should().BeOfType<FontDialogApplyCommand.ApplyAdvanced>());
    }

    [Fact]
    public void Session_EnforcesSuperscriptSubscriptExclusivity()
    {
        var session = FontDialogPlanner.CreateSession(RunFormatting.Default, CultureInfo.InvariantCulture);

        session.PlanVerticalAlignmentToggle(
                superscript: true,
                subscript: true,
                FontDialogVerticalAlignmentToggle.Subscript,
                isChecked: true)
            .Should().Be(new FontDialogVerticalAlignmentState(Superscript: false, Subscript: true));
        session.PlanVerticalAlignmentToggle(
                superscript: true,
                subscript: true,
                FontDialogVerticalAlignmentToggle.Superscript,
                isChecked: true)
            .Should().Be(new FontDialogVerticalAlignmentState(Superscript: true, Subscript: false));
    }

    [Fact]
    public void Session_NormalizesValidationFailure()
    {
        var session = FontDialogPlanner.CreateSession(RunFormatting.Default, CultureInfo.InvariantCulture);

        var acceptance = session.PlanAcceptance(session.InitialState with { FontSizeText = "bad" });

        acceptance.IsAccepted.Should().BeFalse();
        acceptance.Result.Should().BeNull();
        acceptance.ErrorMessage.Should().Be(FontDialogPlanner.FontSizeValidationMessage);
    }

    [Fact]
    public void BuildSelectionState_OwnsMixedFormattingPolicy()
    {
        var current = RunFormatting.Default with { FontFamily = "Aptos", FontSizePt = 11 };
        var first = current with
        {
            Bold = true,
            Hidden = false,
            SmallCaps = true,
            AllCaps = true,
            VerticalAlign = VerticalAlign.Superscript,
        };
        var second = first with
        {
            Bold = false,
            Hidden = true,
            SmallCaps = false,
            AllCaps = false,
            VerticalAlign = VerticalAlign.Subscript,
            FontFamily = "Cambria",
            FontSizePt = 12,
        };

        var state = FontDialogPlanner.BuildSelectionState(current, [first, second]);

        state.Run.Should().BeSameAs(current);
        state.BoldIndeterminate.Should().BeTrue();
        state.HiddenIndeterminate.Should().BeTrue();
        state.FamilyIndeterminate.Should().BeTrue();
        state.SizeIndeterminate.Should().BeTrue();
        state.SmallCapsIndeterminate.Should().BeTrue();
        state.AllCapsIndeterminate.Should().BeTrue();
        state.SuperscriptIndeterminate.Should().BeTrue();
        state.SubscriptIndeterminate.Should().BeTrue();
        state.ItalicIndeterminate.Should().BeFalse();
    }

    [Fact]
    public void BuildSelectionState_SingleFormattingIsUniform()
    {
        var current = RunFormatting.Default with { Italic = true };

        FontDialogPlanner.BuildSelectionState(current, [current])
            .Should().Be(new FontDialogSelectionState(current));
    }

    private static FontDialogInput ValidInput() => new(
        FontFamilyText: "Calibri",
        FontSizeText: "11",
        ColorIndex: 0,
        Bold: false,
        Italic: false,
        Underline: false,
        Strikethrough: false,
        SmallCaps: false,
        AllCaps: false,
        Superscript: false,
        Subscript: false,
        CharacterSpacingText: "0",
        KerningMinSizeText: "",
        PositionText: "0",
        LigatureIndex: 0,
        StylisticSetText: "",
        NumberFormIndex: 0,
        NumberSpacingIndex: 0);

}
