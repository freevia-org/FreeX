using System.Globalization;
using FreeW.App.Presentation.Dialogs;

namespace FreeW.App.Presentation.Tests;

public sealed class FontDialogPlannerTests
{
    [Fact]
    public void Catalogs_ExposeWordFontDialogChoicesInDisplayOrder()
    {
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
    public void BasicCatalogs_ExposeAvaloniaDialogChoicesInDisplayOrder()
    {
        FontDialogPlanner.BasicFamilyChoices
            .Should().Equal("Calibri", "Arial", "Times New Roman", "Inter", "Verdana", "Georgia", "Courier New");

        FontDialogPlanner.BasicSizeChoices.Select(choice => choice.Label)
            .Should().Equal("8", "9", "10", "11", "12", "14", "16", "18", "20", "24", "28", "36", "48", "72");

        FontDialogPlanner.BasicColorChoices.Select(choice => choice.Label)
            .Should().Equal("Automatic", "Black", "Dark Red", "Red", "Orange", "Yellow", "Green", "Blue", "Dark Blue", "Purple", "White");

        FontDialogPlanner.BasicColorChoices.Select(choice => choice.Hex)
            .Should().Equal(null, "#000000", "#C00000", "#FF0000", "#FF6600", "#FFFF00", "#00B050", "#0070C0", "#00008B", "#7030A0", "#FFFFFF");

        FontDialogPlanner.HighlightColorChoices.Select(choice => choice.Label)
            .Should().Equal("None", "Yellow", "Bright Green", "Cyan", "Magenta", "Red", "Dark Blue", "Teal", "Dark Red", "Dark Yellow", "Gray 50%", "Gray 25%", "Black", "White");
    }

    [Fact]
    public void BuildBasicInitialState_ProjectsCurrentFormattingToAvaloniaDialogState()
    {
        var current = new RunFormatting
        {
            FontFamily = "Cambria",
            FontSizePt = 10.125,
            ColorHex = "#ff6600",
            HighlightColorHex = "#00ffff",
            Bold = true,
            Italic = true,
            Underline = true,
            Strikethrough = true,
            SmallCaps = true,
            AllCaps = true,
            VerticalAlign = VerticalAlign.Superscript,
        };

        var state = FontDialogPlanner.BuildBasicInitialState(current, CultureInfo.InvariantCulture);

        state.FontFamilyText.Should().Be("Cambria");
        state.FontSizeText.Should().Be("10.125");
        state.ColorIndex.Should().Be(4);
        state.HighlightColorIndex.Should().Be(3);
        state.Bold.Should().BeTrue();
        state.Italic.Should().BeTrue();
        state.Underline.Should().BeTrue();
        state.Strikethrough.Should().BeTrue();
        state.SmallCaps.Should().BeTrue();
        state.AllCaps.Should().BeTrue();
        state.Superscript.Should().BeTrue();
        state.Subscript.Should().BeFalse();
    }

    [Fact]
    public void BuildBasicInitialState_BlanksIndeterminateFamilyAndSize()
    {
        var current = new RunFormatting
        {
            FontFamily = "Aptos",
            FontSizePt = 13,
        };

        var state = FontDialogPlanner.BuildBasicInitialState(
            current,
            CultureInfo.InvariantCulture,
            familyIndeterminate: true,
            sizeIndeterminate: true);

        state.FontFamilyText.Should().BeEmpty();
        state.FontSizeText.Should().BeEmpty();
    }

    [Fact]
    public void TryBuildBasicResult_ConstructsAvaloniaDialogResult()
    {
        var input = ValidBasicInput() with
        {
            FontFamilyText = "  Cambria  ",
            FontSizeText = "13",
            ColorIndex = 7,
            HighlightColorIndex = 1,
            Bold = null,
            Italic = true,
            Underline = true,
            Strikethrough = false,
            SmallCaps = true,
            AllCaps = true,
            Superscript = true,
            Subscript = true,
        };

        FontDialogPlanner.TryBuildBasicResult(
                input,
                CultureInfo.InvariantCulture,
                out var result,
                out var errorMessage)
            .Should().BeTrue();

        errorMessage.Should().BeNull();
        result.Should().NotBeNull();
        result!.Family.Should().Be("Cambria");
        result.SizePt.Should().Be(13);
        result.Bold.Should().BeNull();
        result.Italic.Should().BeTrue();
        result.Underline.Should().BeTrue();
        result.Strikethrough.Should().BeFalse();
        result.VerticalAlign.Should().Be(VerticalAlign.Superscript);
        result.SmallCaps.Should().BeTrue();
        result.AllCaps.Should().BeTrue();
        result.ColorHex.Should().Be("#0070C0");
        result.HighlightHex.Should().Be("#FFFF00");
        result.FamilyChanged.Should().BeTrue();
        result.SizeChanged.Should().BeTrue();
    }

    [Fact]
    public void TryBuildBasicResult_PreservesIndeterminateBlankFamilyAndSize()
    {
        var input = ValidBasicInput() with
        {
            FontFamilyText = "",
            FontSizeText = "",
            FamilyIndeterminate = true,
            SizeIndeterminate = true,
        };

        FontDialogPlanner.TryBuildBasicResult(
                input,
                CultureInfo.InvariantCulture,
                out var result,
                out var errorMessage)
            .Should().BeTrue();

        errorMessage.Should().BeNull();
        result!.Family.Should().BeNull();
        result.SizePt.Should().BeNull();
        result.FamilyChanged.Should().BeFalse();
        result.SizeChanged.Should().BeFalse();
    }

    [Theory]
    [InlineData("0")]
    [InlineData("bad")]
    [InlineData("1639")]
    public void TryBuildBasicResult_ValidatesFontSizeRange(string fontSizeText)
    {
        var input = ValidBasicInput() with { FontSizeText = fontSizeText };

        FontDialogPlanner.TryBuildBasicResult(
                input,
                CultureInfo.InvariantCulture,
                out var result,
                out var errorMessage)
            .Should().BeFalse();

        result.Should().BeNull();
        errorMessage.Should().Be($"Invalid font size: \"{fontSizeText}\". Enter a number between 1 and 1638.");
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

    private static FontDialogBasicInput ValidBasicInput() => new(
        FontFamilyText: "Calibri",
        FontSizeText: "11",
        FamilyIndeterminate: false,
        SizeIndeterminate: false,
        ColorIndex: 0,
        HighlightColorIndex: 0,
        Bold: false,
        Italic: false,
        Underline: false,
        Strikethrough: false,
        SmallCaps: false,
        AllCaps: false,
        Superscript: false,
        Subscript: false);
}
