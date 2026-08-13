using FreeW.App.Presentation.Dialogs;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Tests;

public sealed class DesignDialogPlannerTests
{
    [Fact]
    public void ThemeColors_VisualMetricsPreservePairedWpfAuthorityGeometry()
    {
        CustomizeThemeColorsDialogPlanner.VisualMetrics.Should().Be(
            new CustomizeThemeColorsDialogVisualMetrics(
                DialogWidth: 440,
                DialogMargin: 14,
                HintFontSize: 10,
                HintBottomMargin: 10,
                LabelColumnWidth: 190,
                ColorFieldMinWidth: 120,
                NameFieldMinWidth: 200,
                RowVerticalMargin: 2,
                LabelRightMargin: 8,
                SeparatorTopMargin: 8,
                SeparatorBottomMargin: 4,
                ActionButtonWidth: 72,
                ActionRowTopMargin: 12,
                AvaloniaColorRowHeight: 29.4,
                AvaloniaSeparatorHeight: 1,
                AvaloniaValidationTopMargin: 8));
    }

    [Fact]
    public void ThemeColors_RenderersConsumeSharedVisualMetrics()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var wpf = File.ReadAllText(Path.Combine(
            root, "freew", "FreeW.App.Host", "CustomizeThemeColorsDialog.cs"));
        var avaloniaFile = File.ReadAllText(Path.Combine(
            root, "freew", "FreeW.App.Avalonia", "DesignDialogParity.cs"));
        var avalonia = avaloniaFile[..avaloniaFile.IndexOf(
            "public sealed partial class CustomizeThemeFontsDialog", StringComparison.Ordinal)];

        foreach (var source in new[] { wpf, avalonia })
        {
            source.Should().Contain("CustomizeThemeColorsDialogPlanner.VisualMetrics");
            source.Should().Contain("Layout.DialogWidth");
            source.Should().Contain("Layout.DialogMargin");
            source.Should().Contain("Layout.HintFontSize");
            source.Should().Contain("Layout.HintBottomMargin");
            source.Should().Contain("Layout.LabelColumnWidth");
            source.Should().Contain("Layout.ColorFieldMinWidth");
            source.Should().Contain("Layout.NameFieldMinWidth");
            source.Should().Contain("Layout.ActionButtonWidth");
            source.Should().Contain("Layout.ActionRowTopMargin");
        }

        wpf.Should().NotContain("Width = 440;");
        wpf.Should().NotContain("MinWidth = 120");
        avalonia.Should().NotContain("private const double DialogWidth");
        avalonia.Should().NotContain("CustomizeThemeFontsDialogPlanner.DialogMargin");
        avalonia.Should().Contain("Layout.AvaloniaColorRowHeight");
        avalonia.Should().Contain("Layout.AvaloniaSeparatorHeight");
        avalonia.Should().Contain("IUserMessageService? messageService = null");
        avalonia.Should().Contain("messageService ?? new AvaloniaUserMessageService(this)");
        avalonia.Should().Contain("await _messageService.ShowWarningAsync(");
        avalonia.Should().Contain("ok.Click += (_, _) => AcceptAndClose()");
    }

    [Fact]
    public void ThemeColors_UsesWpfSlotOrderAndCurrentSchemeDefaults()
    {
        var state = CustomizeThemeColorsDialogPlanner.BuildInitialState(DocumentTheme.Default);

        CustomizeThemeColorsDialogPlanner.Slots.Select(slot => slot.Label)
            .Should().Equal(
                "Dark 1 (Text/Background)", "Light 1 (Background/Text)",
                "Dark 2 (Text/Background)", "Light 2 (Background/Text)",
                "Accent 1", "Accent 2", "Accent 3", "Accent 4", "Accent 5", "Accent 6",
                "Hyperlink", "Followed Hyperlink");
        state.ColorHexTexts.Should().Equal(
            "#000000", "#FFFFFF", "#44546A", "#E7E6E6", "#000000", "#2F5496",
            "#1F3864", "#FFC000", "#5B9BD5", "#70AD47", "#0563C1", "#954F72");
        state.NameText.Should().Be("Custom");
    }

    [Fact]
    public void ThemeColors_NormalizesAndPreservesCustomName()
    {
        var values = CustomizeThemeColorsDialogPlanner.BuildInitialState(DocumentTheme.Default).ColorHexTexts.ToArray();
        values[4] = " c00000 ";

        CustomizeThemeColorsDialogPlanner.TryBuildResult(
                DocumentTheme.Default,
                new CustomizeThemeColorsDialogInput(values, "  Brand  "),
                out var result,
                out var validation)
            .Should().BeTrue();

        validation.Should().BeNull();
        result!.Name.Should().Be("Brand");
        result.ColorScheme.Accent1.Should().Be("C00000");
        result.HeadingFont.Should().Be(DocumentTheme.Default.HeadingFont);
    }

    [Fact]
    public void ThemeColors_RejectsInvalidSlotAndReportsItsFocusIndex()
    {
        var values = CustomizeThemeColorsDialogPlanner.BuildInitialState(DocumentTheme.Default).ColorHexTexts.ToArray();
        values[7] = "#12";

        CustomizeThemeColorsDialogPlanner.TryBuildResult(
                DocumentTheme.Default,
                new CustomizeThemeColorsDialogInput(values, "Custom"),
                out var result,
                out var validation)
            .Should().BeFalse();

        result.Should().BeNull();
        validation!.SlotIndex.Should().Be(7);
        validation.Message.Should().Contain("Accent 4");
    }

    [Fact]
    public void ThemeFonts_UsesWpfChoicesAndDefaultName()
    {
        var session = CustomizeThemeFontsDialogPlanner.CreateSession(DocumentFontSet.Default);
        var state = session.InitialState;
        state.Should().Be(new CustomizeThemeFontsInitialState("Calibri", "Calibri", "Custom"));
        CustomizeThemeFontsDialogPlanner.HeadingFontLabel.Should().Be("Heading font:");
        CustomizeThemeFontsDialogPlanner.BodyFontLabel.Should().Be("Body font:");
        CustomizeThemeFontsDialogPlanner.NameLabel.Should().Be("Name:");
        CustomizeThemeFontsDialogPlanner.CommonFonts.Should().ContainInOrder("Arial", "Calibri", "Cambria", "Georgia", "Verdana");
        CustomizeThemeFontsDialogPlanner.DialogWidth.Should().Be(380);
        CustomizeThemeFontsDialogPlanner.DialogMargin.Should().Be(14);
        CustomizeThemeFontsDialogPlanner.LabelColumnWidth.Should().Be(130);
        CustomizeThemeFontsDialogPlanner.FieldMinWidth.Should().Be(200);
        CustomizeThemeFontsDialogPlanner.ActionButtonWidth.Should().Be(72);
        CustomizeThemeFontsDialogPlanner.ActionRowTopMargin.Should().Be(8);
        CustomizeThemeFontsDialogPlanner.ActionRowBottomMargin.Should().Be(14);
        CustomizeThemeFontsDialogPlanner.RowMargin.Should().Be(4);
        CustomizeThemeFontsDialogPlanner.SeparatorTopMargin.Should().Be(6);
        CustomizeThemeFontsDialogPlanner.SeparatorBottomMargin.Should().Be(2);
    }

    [Theory]
    [InlineData("", "Calibri", "HeadingFont")]
    [InlineData("Calibri", "", "BodyFont")]
    public void ThemeFonts_RejectsMissingFont(string heading, string body, string expectedField)
    {
        CustomizeThemeFontsDialogPlanner.TryBuildResult(
                new CustomizeThemeFontsDialogInput(heading, body, ""),
                out var result,
                out var validation)
            .Should().BeFalse();

        result.Should().BeNull();
        validation!.Field.ToString().Should().Be(expectedField);
    }

    [Fact]
    public void ThemeFonts_TrimsFontsAndDefaultsBlankName()
    {
        CustomizeThemeFontsDialogPlanner.TryBuildResult(
                new CustomizeThemeFontsDialogInput(" Cambria ", " Georgia ", "  "),
                out var result,
                out _)
            .Should().BeTrue();

        result.Should().Be(new DocumentFontSet("Custom", "Cambria", "Georgia"));
    }

    [Fact]
    public void ThemeFontsSession_OwnsAcceptanceAndValidationFocus()
    {
        var session = CustomizeThemeFontsDialogPlanner.CreateSession(DocumentFontSet.Default);

        var rejected = session.PlanAcceptance(new CustomizeThemeFontsDialogInput("Calibri", "", "Ignored"));
        var accepted = session.PlanAcceptance(new CustomizeThemeFontsDialogInput(" Cambria ", " Georgia ", "  "));

        rejected.IsAccepted.Should().BeFalse();
        rejected.ErrorMessage.Should().Be("Enter a body font name.");
        rejected.FocusField.Should().Be(CustomizeThemeFontsDialogField.BodyFont);
        accepted.IsAccepted.Should().BeTrue();
        accepted.Result.Should().Be(new DocumentFontSet("Custom", "Cambria", "Georgia"));
    }

    [Fact]
    public void PageColor_UsesPaletteAndSupportsCustomHexOrNoColor()
    {
        PageColorDialogPlanner.TryBuildResult(new PageColorDialogInput(1, ""), out var lightBlue, out _).Should().BeTrue();
        lightBlue.Should().Be("#DDEBF7");

        PageColorDialogPlanner.TryBuildResult(new PageColorDialogInput(-1, " d9ead3 "), out var custom, out _).Should().BeTrue();
        custom.Should().Be("#D9EAD3");

        PageColorDialogPlanner.TryBuildResult(new PageColorDialogInput(PageColorDialogPlanner.Palette.Count - 1, ""), out var none, out _).Should().BeTrue();
        none.Should().BeNull();
    }

    [Fact]
    public void PageColor_RejectsInvalidCustomHex()
    {
        PageColorDialogPlanner.TryBuildResult(new PageColorDialogInput(-1, "oops"), out var result, out var validation).Should().BeFalse();
        result.Should().BeNull();
        validation!.Message.Should().Be(PageColorDialogPlanner.CustomColorValidationMessage);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("DDEBF7", "#DDEBF7")]
    [InlineData(" #F2F2F2 ", "#F2F2F2")]
    public void PageColor_NormalizeForModel_MatchesBothRendererContracts(
        string? input,
        string? expected)
    {
        PageColorDialogPlanner.NormalizeForModel(input).Should().Be(expected);
    }

    [Fact]
    public void SetAsDefaultConfirmation_UsesStableWordActionText()
    {
        SetAsDefaultConfirmationPlanner.BuildState().Should().Be(new SetAsDefaultConfirmationState(
            "Set as Default",
            "Set this design as the default for new documents?",
            "Yes",
            "No"));
    }

    [Fact]
    public void PageBorderPlanner_RetainsArtAndWidthSemanticsUsedByAvaloniaDialog()
    {
        BordersAndShadingDialogPlanner.ArtBorders[BordersAndShadingDialogPlanner.ArtIndexFor(84)]
            .Should().Be(new PageBorderArtOption("People (84)", 84));
        var input = new BordersAndShadingDialogInput(
            ParagraphSettingIndex: 0,
            ParagraphLineStyleIndex: 0,
            ParagraphColorHex: null,
            ParagraphWidthText: "1",
            Top: false,
            Left: false,
            Bottom: false,
            Right: false,
            PageSettingIndex: 1,
            PageLineStyleIndex: 3,
            PageColorHex: "#7030A0",
            PageWidthText: "2.25",
            PageArtIndex: BordersAndShadingDialogPlanner.ArtIndexFor(84),
            ShadingColorHex: null,
            ShadingPatternIndex: 0);

        BordersAndShadingDialogPlanner.TryBuildResult(input, System.Globalization.CultureInfo.InvariantCulture, out var result, out _).Should().BeTrue();
        result!.PageBorder.Should().BeEquivalentTo(new PageBorder("#7030A0", 2.25)
        {
            LineStyle = BorderLineStyle.Double,
            ArtId = 84,
        });
    }
}
