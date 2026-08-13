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
    public void PresentationMetrics_OwnAvaloniaAuthorityTemplateCompensations()
    {
        var metrics = PageSetupDialogPlanner.PresentationMetrics;

        metrics.AvaloniaTabWidths.Should().Equal(59, 40, 48);
        metrics.AvaloniaActionSpacing.Should().Be(14);
        metrics.AvaloniaActionRightInset.Should().Be(15);
        metrics.AvaloniaLauncherLeftInset.Should().Be(-1);
        metrics.AvaloniaLauncherSpacing.Should().Be(14);
        metrics.AvaloniaValidationMargin.Should().Be(new PageSetupDialogThickness(16, 8, 16, 0));
    }

    [Fact]
    public void AvaloniaRenderer_ProjectsSharedAuthorityMetricsWithoutLocalLayoutPolicy()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var source = File.ReadAllText(Path.Combine(
            root, "freew", "FreeW.App.Avalonia", "PageSetupDialog.cs"));

        source.Should().Contain("metrics.AvaloniaTabWidths");
        source.Should().Contain("metrics.AvaloniaActionSpacing");
        source.Should().Contain("metrics.AvaloniaActionRightInset");
        source.Should().Contain("metrics.AvaloniaLauncherLeftInset");
        source.Should().Contain("metrics.AvaloniaLauncherSpacing");
        source.Should().Contain("metrics.AvaloniaValidationMargin");
        source.Should().NotContain("AuthorityTabWidths");
        source.Should().NotContain("private const double Authority");
    }

    [Fact]
    public void VisualHarnessSectionStart_is_an_explicit_shared_seed()
    {
        PageSetupDialogPlanner.VisualHarnessSectionStart.Should().Be(SectionBreakKind.NextPage);
        PageSetupDialogPlanner.SectionStartNames[(int)PageSetupDialogPlanner.VisualHarnessSectionStart]
            .Should().Be("New page");
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
    public void Surface_GroupsFieldsTogglesAndLaunchersInSharedDisplayOrder()
    {
        var surface = PageSetupDialogPlanner.Surface;

        surface.Title.Should().Be(PageSetupDialogPlanner.Title);
        surface.Tabs.Select(tab => tab.Kind).Should().Equal(
            PageSetupDialogTabKind.Margins,
            PageSetupDialogTabKind.Paper,
            PageSetupDialogTabKind.Layout);
        surface.Tabs.Single(tab => tab.Kind == PageSetupDialogTabKind.Margins)
            .Rows.Select(row => row.Kind).Should().Equal(
                PageSetupDialogControlKind.MarginTop,
                PageSetupDialogControlKind.MarginBottom,
                PageSetupDialogControlKind.MarginLeft,
                PageSetupDialogControlKind.MarginRight,
                PageSetupDialogControlKind.Gutter,
                PageSetupDialogControlKind.GutterPosition,
                PageSetupDialogControlKind.Orientation,
                PageSetupDialogControlKind.MultiplePages,
                PageSetupDialogControlKind.ApplyTo);
        surface.LayoutToggles.Select(toggle => toggle.Kind).Should().Equal(
            PageSetupDialogToggleKind.DifferentFirstPage,
            PageSetupDialogToggleKind.DifferentOddEvenPages);
        surface.LayoutLaunchers.Select(launcher => launcher.FollowUp).Should().Equal(
            PageSetupDialogFollowUp.LineNumbers,
            PageSetupDialogFollowUp.Borders);
        surface.Tabs.Should().OnlyContain(tab => !string.IsNullOrWhiteSpace(tab.AutomationId));
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
    public void Session_ProjectsInitialPaperStateAndKeepsDimensionsEditable()
    {
        var session = PageSetupDialogPlanner.CreateSession(
            new PageSettings { WidthPt = 612.4, HeightPt = 791.6 },
            SectionBreakKind.NextPage,
            CultureInfo.InvariantCulture);

        session.InitialFocusPlan.Should().Be(
            new PageSetupDialogFocusPlan(PageSetupDialogField.MarginTop, SelectAllOnFocus: true));
        session.InitialState.PaperSizeIndex.Should().Be(0);
        session.InitialState.WidthText.Should().Be("612.4");
        session.InitialState.HeightText.Should().Be("791.6");
        session.PaperOptions.Should().Equal(PageSetupDialogPlanner.HostPaperOptions);
        session.EnabledState.Should().Be(new PageSetupDialogEnabledState(true, true));

        var letter = session.PlanPaperSelection(session.InitialState.PaperSizeIndex);
        letter.UpdateDimensions.Should().BeTrue();
        letter.WidthText.Should().Be("612");
        letter.HeightText.Should().Be("792");
        letter.EnabledState.Should().Be(new PageSetupDialogEnabledState(true, true));

        var custom = session.PlanPaperSelection(PageSetupDialogPlanner.CustomIndex(session.PaperOptions));
        custom.UpdateDimensions.Should().BeFalse();
        custom.WidthText.Should().BeNull();
        custom.HeightText.Should().BeNull();
        custom.EnabledState.Should().Be(new PageSetupDialogEnabledState(true, true));
    }

    [Fact]
    public void Session_NormalizesCultureAwareDimensionEditsToTheSharedPaperSelection()
    {
        var culture = CultureInfo.GetCultureInfo("fr-FR");
        var session = PageSetupDialogPlanner.CreateSession(
            new PageSettings(),
            SectionBreakKind.NextPage,
            culture);

        var a4 = session.PlanDimensionEdit("595,3", "841,9", currentPaperSizeIndex: 0);
        a4.UpdatePaperSize.Should().BeTrue();
        a4.PaperSizeIndex.Should().Be(4);
        a4.EnabledState.Should().Be(new PageSetupDialogEnabledState(true, true));

        var invalid = session.PlanDimensionEdit("wide", "841,9", currentPaperSizeIndex: 4);
        invalid.UpdatePaperSize.Should().BeFalse();
        invalid.PaperSizeIndex.Should().Be(4);
    }

    [Fact]
    public void Session_ProjectsControlValuesAndConstructsThePortableResult()
    {
        var session = PageSetupDialogPlanner.CreateSession(
            new PageSettings(),
            SectionBreakKind.NextPage,
            CultureInfo.InvariantCulture);
        var source = new TestControlSource(
            MarginTopText: "50",
            MarginBottomText: "72",
            MarginLeftText: "40",
            MarginRightText: "72",
            GutterText: "18",
            GutterPositionIndex: 1,
            OrientationIndex: 1,
            MultiplePagesIndex: 1,
            WidthText: "1224",
            HeightText: "792",
            PaperSizeIndex: PageSetupDialogPlanner.CustomIndex(session.PaperOptions),
            SectionStartIndex: 2,
            DifferentFirstPage: true,
            DifferentOddEvenPages: true,
            HeaderDistanceText: "30",
            FooterDistanceText: "40",
            VerticalAlignmentIndex: 2);

        var projected = session.ProjectControlState(source);
        projected.GutterPositionIndex.Should().Be(1);
        projected.PaperSizeIndex.Should().Be(PageSetupDialogPlanner.CustomIndex(session.PaperOptions));

        var acceptance = session.PlanAcceptance(source, PageSetupDialogFollowUp.Borders);
        acceptance.IsAccepted.Should().BeTrue();
        acceptance.ErrorMessage.Should().BeNull();
        acceptance.FocusPlan.Should().BeNull();
        acceptance.FollowUp.Should().Be(PageSetupDialogFollowUp.Borders);
        acceptance.Result.Should().Be(new PageSetupDialogResult(
            MarginTopPt: 50,
            MarginBottomPt: 72,
            MarginLeftPt: 40,
            MarginRightPt: 72,
            GutterPt: 18,
            Landscape: true,
            MirrorMargins: true,
            WidthPt: 792,
            HeightPt: 1224,
            SectionStart: SectionBreakKind.EvenPage,
            DifferentFirstPage: true,
            DifferentOddEvenPages: true,
            HeaderDistancePt: 30,
            FooterDistancePt: 40,
            VerticalAlignment: PageVerticalAlignment.Justified,
            GutterAtTop: true));
    }

    [Fact]
    public void Session_NormalizesValidationFailuresToTheSharedDialogMessage()
    {
        var session = PageSetupDialogPlanner.CreateSession(
            new PageSettings(),
            SectionBreakKind.NextPage,
            CultureInfo.InvariantCulture);
        var state = ValidControlState() with { MarginTopText = "-1" };

        var acceptance = session.PlanAcceptance(state);

        acceptance.IsAccepted.Should().BeFalse();
        acceptance.Result.Should().BeNull();
        acceptance.ErrorMessage.Should().Be(PageSetupDialogPlanner.UnifiedValidationMessage);
        acceptance.FocusPlan.Should().Be(
            new PageSetupDialogFocusPlan(PageSetupDialogField.MarginTop, SelectAllOnFocus: true));
        acceptance.FollowUp.Should().Be(PageSetupDialogFollowUp.None);
    }

    [Theory]
    [InlineData(PageSetupDialogField.MarginTop)]
    [InlineData(PageSetupDialogField.MarginBottom)]
    [InlineData(PageSetupDialogField.MarginLeft)]
    [InlineData(PageSetupDialogField.MarginRight)]
    [InlineData(PageSetupDialogField.Gutter)]
    [InlineData(PageSetupDialogField.PageWidth)]
    [InlineData(PageSetupDialogField.PageHeight)]
    [InlineData(PageSetupDialogField.HeaderDistance)]
    [InlineData(PageSetupDialogField.FooterDistance)]
    public void Session_ValidationFailureOwnsFieldFocusAndRejectsFollowUpIntent(PageSetupDialogField field)
    {
        var session = PageSetupDialogPlanner.CreateSession(
            new PageSettings(),
            SectionBreakKind.NextPage,
            CultureInfo.InvariantCulture);
        var state = InvalidField(ValidControlState(), field);

        var acceptance = session.PlanAcceptance(state, PageSetupDialogFollowUp.LineNumbers);

        acceptance.IsAccepted.Should().BeFalse();
        acceptance.Result.Should().BeNull();
        acceptance.ErrorMessage.Should().Be(PageSetupDialogPlanner.UnifiedValidationMessage);
        acceptance.FocusPlan.Should().Be(new PageSetupDialogFocusPlan(field, SelectAllOnFocus: true));
        acceptance.FollowUp.Should().Be(PageSetupDialogFollowUp.None);
    }

    [Fact]
    public void BuildInitialState_OwnsHeaderAndFooterFallbackDefaults()
    {
        var state = PageSetupDialogPlanner.CreateSession(
            new PageSettings { HeaderDistancePt = 0, FooterDistancePt = -1 },
            SectionBreakKind.NextPage,
            CultureInfo.InvariantCulture).InitialState;

        PageSetupDialogPlanner.DefaultHeaderDistancePt.Should().Be(36);
        PageSetupDialogPlanner.DefaultFooterDistancePt.Should().Be(36);
        state.HeaderDistanceText.Should().Be("36");
        state.FooterDistanceText.Should().Be("36");
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

    private static PageSetupDialogControlState ValidControlState() => new(
        MarginTopText: "72",
        MarginBottomText: "72",
        MarginLeftText: "72",
        MarginRightText: "72",
        GutterText: "0",
        GutterPositionIndex: 0,
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
        VerticalAlignmentIndex: 0);

    private static PageSetupDialogControlState InvalidField(
        PageSetupDialogControlState state,
        PageSetupDialogField field) =>
        field switch
        {
            PageSetupDialogField.MarginTop => state with { MarginTopText = "-1" },
            PageSetupDialogField.MarginBottom => state with { MarginBottomText = "-1" },
            PageSetupDialogField.MarginLeft => state with { MarginLeftText = "-1" },
            PageSetupDialogField.MarginRight => state with { MarginRightText = "-1" },
            PageSetupDialogField.Gutter => state with { GutterText = "-1" },
            PageSetupDialogField.PageWidth => state with { WidthText = "0" },
            PageSetupDialogField.PageHeight => state with { HeightText = "0" },
            PageSetupDialogField.HeaderDistance => state with { HeaderDistanceText = "-1" },
            PageSetupDialogField.FooterDistance => state with { FooterDistanceText = "-1" },
            _ => throw new ArgumentOutOfRangeException(nameof(field)),
        };

    private sealed record TestControlSource(
        string? MarginTopText,
        string? MarginBottomText,
        string? MarginLeftText,
        string? MarginRightText,
        string? GutterText,
        int GutterPositionIndex,
        int OrientationIndex,
        int MultiplePagesIndex,
        string? WidthText,
        string? HeightText,
        int PaperSizeIndex,
        int SectionStartIndex,
        bool DifferentFirstPage,
        bool DifferentOddEvenPages,
        string? HeaderDistanceText,
        string? FooterDistanceText,
        int VerticalAlignmentIndex) : IPageSetupDialogControlSource;
}
