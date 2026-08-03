using System.Globalization;
using FreeW.App.Presentation.Dialogs;

namespace FreeW.App.Presentation.Tests;

public sealed class PageSetupDialogPlannerTests
{
    [Fact]
    public void PresentationMetrics_Expose_the_shared_Wpf_field_height()
    {
        PageSetupDialogPlanner.PresentationMetrics.FieldHeight.Should().Be(24);
        PageSetupDialogPlanner.PresentationMetrics.AvaloniaTabContentInset.Should().Be(3);
    }

    [Fact]
    public void PresentationMetrics_DescribeSharedWpfAuthorityGeometryAndValidationPolicy()
    {
        var metrics = PageSetupDialogPlanner.PresentationMetrics;

        metrics.WindowWidth.Should().Be(420);
        metrics.RowInset.Should().Be(4);
        metrics.LabelFieldSpacing.Should().Be(8);
        metrics.NumberBoxMinWidth.Should().Be(120);
        metrics.ComboBoxMinWidth.Should().Be(180);
        metrics.ActionButtonWidth.Should().Be(72);
        metrics.TabNames.Should().Equal("Margins", "Paper", "Layout");
        metrics.Validation.GeometryMode.Should().Be(PageSetupGeometryMode.PortraitInputSwappedWhenLandscape);
        metrics.Validation.ValidationProfile.Should().Be(PageSetupValidationProfile.UnifiedDialog);
        metrics.Validation.UseSelectedPaperPreset.Should().BeFalse();
        metrics.Validation.Message.Should().Be(PageSetupDialogPlanner.UnifiedValidationMessage);
    }

    [Fact]
    public void PresentationLabels_UsePointUnitsAcrossBothHosts()
    {
        PageSetupDialogPlanner.TopMarginLabel.Should().Be("Top (pt):");
        PageSetupDialogPlanner.BottomMarginLabel.Should().Be("Bottom (pt):");
        PageSetupDialogPlanner.LeftMarginLabel.Should().Be("Left (pt):");
        PageSetupDialogPlanner.RightMarginLabel.Should().Be("Right (pt):");
        PageSetupDialogPlanner.GutterLabel.Should().Be("Gutter (pt):");
        PageSetupDialogPlanner.PaperSizeLabel.Should().Be("Paper size:");
        PageSetupDialogPlanner.CustomHeightLabel.Should().Be("Height (pt):");
    }

    [Fact]
    public void BuildInitialState_UsesUnifiedDialogLandscapeRoundTripGeometry()
    {
        var page = new PageSettings
        {
            Landscape = true,
            WidthPt = 792,
            HeightPt = 1224,
            MarginTopPt = 50,
            MarginLeftPt = 40,
            GutterPt = 18,
            GutterAtTop = true,
            MirrorMargins = true,
            HeaderDistancePt = 30,
            FooterDistancePt = 40,
            DifferentFirstPage = true,
            VerticalAlignment = PageVerticalAlignment.Center,
        };

        var state = PageSetupDialogPlanner.BuildInitialState(
            page,
            SectionBreakKind.NextPage,
            PageSetupDialogPlanner.HostPaperOptions,
            PageSetupGeometryMode.PortraitInputSwappedWhenLandscape,
            CultureInfo.InvariantCulture);

        state.OrientationIndex.Should().Be(1);
        state.MultiplePagesIndex.Should().Be(1);
        state.GutterPositionIndex.Should().Be(1);
        state.WidthText.Should().Be("1224");
        state.HeightText.Should().Be("792");
        state.PaperSizeIndex.Should().Be(PageSetupDialogPlanner.CustomIndex(PageSetupDialogPlanner.HostPaperOptions));
        state.HeaderDistanceText.Should().Be("30");
        state.FooterDistanceText.Should().Be("40");
        state.VerticalAlignmentIndex.Should().Be(1);
    }

    [Fact]
    public void TryBuildResult_UnifiedDialog_PreservesExistingValidationMessageAndSwapsLandscapeInput()
    {
        var input = new PageSetupDialogInput(
            MarginTopText: "50",
            MarginBottomText: "72",
            MarginLeftText: "40",
            MarginRightText: "72",
            GutterText: "18",
            OrientationIndex: 1,
            MultiplePagesIndex: 1,
            WidthText: "1224",
            HeightText: "792",
            PaperSizeIndex: -1,
            SectionStartIndex: 1,
            DifferentFirstPage: true,
            DifferentOddEvenPages: false,
            HeaderDistanceText: "30",
            FooterDistanceText: "40",
            VerticalAlignmentIndex: 1,
            UseSelectedPaperPreset: false,
            GeometryMode: PageSetupGeometryMode.PortraitInputSwappedWhenLandscape,
            ValidationProfile: PageSetupValidationProfile.UnifiedDialog,
            GutterPositionIndex: 1);

        PageSetupDialogPlanner.TryBuildResult(
                input,
                PageSetupDialogPlanner.HostPaperOptions,
                CultureInfo.InvariantCulture,
                out var result,
                out var error)
            .Should().BeTrue();

        error.Should().BeNull();
        result.Should().NotBeNull();
        result!.WidthPt.Should().Be(792);
        result.HeightPt.Should().Be(1224);
        result.MirrorMargins.Should().BeTrue();
        result.GutterAtTop.Should().BeTrue();
        result.DifferentFirstPage.Should().BeTrue();
        result.VerticalAlignment.Should().Be(PageVerticalAlignment.Center);
    }

    [Fact]
    public void TryBuildResult_UnifiedDialog_RejectsInvalidNumbersWithSharedMessage()
    {
        var input = ValidUnifiedInput() with { MarginTopText = "-1" };

        PageSetupDialogPlanner.TryBuildResult(
                input,
                PageSetupDialogPlanner.HostPaperOptions,
                CultureInfo.InvariantCulture,
                out var result,
                out var error)
            .Should().BeFalse();

        result.Should().BeNull();
        error.Should().Be(PageSetupDialogPlanner.UnifiedValidationMessage);
    }

    [Fact]
    public void TryBuildResult_CompactDialog_UsesSelectedPresetAndNormalizesLandscape()
    {
        var a4Index = PageSetupDialogPlanner.AvaloniaPaperOptions
            .Select((option, index) => (option, index))
            .Single(pair => pair.option.AvaloniaLabel.StartsWith("A4", StringComparison.Ordinal))
            .index;
        var input = ValidCompactInput() with
        {
            OrientationIndex = 1,
            PaperSizeIndex = a4Index,
            UseSelectedPaperPreset = true,
        };

        PageSetupDialogPlanner.TryBuildResult(
                input,
                PageSetupDialogPlanner.AvaloniaPaperOptions,
                CultureInfo.InvariantCulture,
                out var result,
                out var error)
            .Should().BeTrue();

        error.Should().BeNull();
        result.Should().NotBeNull();
        result!.Landscape.Should().BeTrue();
        result.WidthPt.Should().BeApproximately(841.9, 0.01);
        result.HeightPt.Should().BeApproximately(595.3, 0.01);
    }

    [Fact]
    public void TryBuildResult_CompactDialog_BlankMarginMeansZeroAndFieldErrorsStaySpecific()
    {
        var blankMargin = ValidCompactInput() with { MarginTopText = "" };

        PageSetupDialogPlanner.TryBuildResult(
                blankMargin,
                PageSetupDialogPlanner.AvaloniaPaperOptions,
                CultureInfo.InvariantCulture,
                out var result,
                out _)
            .Should().BeTrue();

        result!.MarginTopPt.Should().Be(0);

        var badWidth = blankMargin with
        {
            PaperSizeIndex = PageSetupDialogPlanner.CustomIndex(PageSetupDialogPlanner.AvaloniaPaperOptions),
            WidthText = "wide",
            UseSelectedPaperPreset = true,
        };

        PageSetupDialogPlanner.TryBuildResult(
                badWidth,
                PageSetupDialogPlanner.AvaloniaPaperOptions,
                CultureInfo.InvariantCulture,
                out _,
                out var error)
            .Should().BeFalse();

        error.Should().Be("Invalid value for Paper width: \"wide\". Enter a positive number.");
    }

    [Fact]
    public void ApplyToPageSettings_AppliesEveryDialogField()
    {
        var page = new PageSettings();
        var result = new PageSetupDialogResult(
            MarginTopPt: 54,
            MarginBottomPt: 60,
            MarginLeftPt: 66,
            MarginRightPt: 70,
            GutterPt: 12,
            Landscape: true,
            MirrorMargins: true,
            WidthPt: 1008,
            HeightPt: 612,
            SectionStart: SectionBreakKind.NextPage,
            DifferentFirstPage: true,
            DifferentOddEvenPages: true,
            HeaderDistancePt: 24,
            FooterDistancePt: 30,
            VerticalAlignment: PageVerticalAlignment.Justified,
            GutterAtTop: true);

        PageSetupDialogPlanner.ApplyToPageSettings(page, result);

        page.MarginTopPt.Should().Be(54);
        page.MarginBottomPt.Should().Be(60);
        page.MarginLeftPt.Should().Be(66);
        page.MarginRightPt.Should().Be(70);
        page.GutterPt.Should().Be(12);
        page.GutterAtTop.Should().BeTrue();
        page.Landscape.Should().BeTrue();
        page.MirrorMargins.Should().BeTrue();
        page.WidthPt.Should().Be(1008);
        page.HeightPt.Should().Be(612);
        page.DifferentFirstPage.Should().BeTrue();
        page.DifferentOddEvenPages.Should().BeTrue();
        page.HeaderDistancePt.Should().Be(24);
        page.FooterDistancePt.Should().Be(30);
        page.VerticalAlignment.Should().Be(PageVerticalAlignment.Justified);
    }

    private static PageSetupDialogInput ValidUnifiedInput() => new(
        MarginTopText: "72",
        MarginBottomText: "72",
        MarginLeftText: "72",
        MarginRightText: "72",
        GutterText: "0",
        OrientationIndex: 0,
        MultiplePagesIndex: 0,
        WidthText: "612",
        HeightText: "792",
        PaperSizeIndex: 0,
        SectionStartIndex: 1,
        DifferentFirstPage: false,
        DifferentOddEvenPages: false,
        HeaderDistanceText: "36",
        FooterDistanceText: "36",
        VerticalAlignmentIndex: 0,
        UseSelectedPaperPreset: false,
        GeometryMode: PageSetupGeometryMode.PortraitInputSwappedWhenLandscape,
        ValidationProfile: PageSetupValidationProfile.UnifiedDialog);

    private static PageSetupDialogInput ValidCompactInput() => new(
        MarginTopText: "72",
        MarginBottomText: "72",
        MarginLeftText: "72",
        MarginRightText: "72",
        GutterText: "0",
        OrientationIndex: 0,
        MultiplePagesIndex: 0,
        WidthText: "612",
        HeightText: "792",
        PaperSizeIndex: 0,
        SectionStartIndex: 1,
        DifferentFirstPage: false,
        DifferentOddEvenPages: false,
        HeaderDistanceText: "0",
        FooterDistanceText: "0",
        VerticalAlignmentIndex: 0,
        UseSelectedPaperPreset: true,
        GeometryMode: PageSetupGeometryMode.NormalizeToOrientation,
        ValidationProfile: PageSetupValidationProfile.CompactDialog);
}
