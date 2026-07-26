using FluentAssertions;

namespace FreeX.App.Services.Tests;

public sealed class OptionsDialogPlannerTests
{
    [Fact]
    public void OptionsDialogFrameConstants_PinSharedWpfAndAvaloniaCaptureContract()
    {
        OptionsDialogPlanner.WindowWidth.Should().Be(760);
        OptionsDialogPlanner.WindowHeight.Should().Be(560);
        OptionsDialogPlanner.CaptureWidth.Should().Be(744);
        OptionsDialogPlanner.CaptureHeight.Should().Be(520.5);
        OptionsDialogPlanner.FormulasCaptureHeight.Should().Be(776.5);
    }

    [Theory]
    [InlineData("11", true, 11)]
    [InlineData(" 8 ", true, 8)]
    [InlineData("409", true, 409)]
    [InlineData("0", false, 0)]
    [InlineData("-3", false, 0)]
    [InlineData("410", false, 0)]
    [InlineData("abc", false, 0)]
    [InlineData("", false, 0)]
    [InlineData(null, false, 0)]
    public void TryParseDefaultFontSize_EnforcesBounds(string? input, bool expectedOk, int expectedValue)
    {
        var ok = OptionsDialogPlanner.TryParseDefaultFontSize(input, out var value);

        ok.Should().Be(expectedOk);
        if (expectedOk)
            value.Should().Be(expectedValue);
    }

    [Theory]
    [InlineData("1", true, 1)]
    [InlineData("255", true, 255)]
    [InlineData("0", false, 0)]
    [InlineData("256", false, 0)]
    [InlineData("x", false, 0)]
    public void TryParseDefaultSheetCount_EnforcesBounds(string? input, bool expectedOk, int expectedValue)
    {
        var ok = OptionsDialogPlanner.TryParseDefaultSheetCount(input, out var value);

        ok.Should().Be(expectedOk);
        if (expectedOk)
            value.Should().Be(expectedValue);
    }

    [Fact]
    public void TryBuildInput_ReportsInvalidFontSize()
    {
        var ok = OptionsDialogPlanner.TryBuildInput(
            "Arial", "0", "1", "Tester",
            autoCalculate: true, useR1C1ReferenceStyle: false, errorCheckingEnabled: true,
            proofingIgnoreUppercase: true, proofingIgnoreNumbers: true,
            showFormulaBar: true, showGridlines: true, showHeadings: true,
            defaultFormat: ".xlsx", showScreenTips: true,
            moveSelectionAfterEnter: true, afterEnterDirection: AppOptionsEnterDirection.Down,
            out _, out var error);

        ok.Should().BeFalse();
        error.Should().Be(OptionsDialogPlanner.OptionsInputError.InvalidFontSize);
    }

    [Fact]
    public void TryBuildInput_ReportsInvalidSheetCount()
    {
        var ok = OptionsDialogPlanner.TryBuildInput(
            "Arial", "12", "999", "Tester",
            autoCalculate: true, useR1C1ReferenceStyle: false, errorCheckingEnabled: true,
            proofingIgnoreUppercase: true, proofingIgnoreNumbers: true,
            showFormulaBar: true, showGridlines: true, showHeadings: true,
            defaultFormat: ".xlsx", showScreenTips: true,
            moveSelectionAfterEnter: true, afterEnterDirection: AppOptionsEnterDirection.Down,
            out _, out var error);

        ok.Should().BeFalse();
        error.Should().Be(OptionsDialogPlanner.OptionsInputError.InvalidSheetCount);
    }

    [Fact]
    public void TryBuildInput_NormalizesAndSucceeds()
    {
        var ok = OptionsDialogPlanner.TryBuildInput(
            "  Arial  ", "14", "3", "  Ada  ",
            autoCalculate: false, useR1C1ReferenceStyle: true, errorCheckingEnabled: false,
            proofingIgnoreUppercase: false, proofingIgnoreNumbers: true,
            showFormulaBar: false, showGridlines: false, showHeadings: true,
            defaultFormat: ".json", showScreenTips: false,
            moveSelectionAfterEnter: false, afterEnterDirection: AppOptionsEnterDirection.Up,
            out var input, out var error);

        ok.Should().BeTrue();
        error.Should().Be(OptionsDialogPlanner.OptionsInputError.None);
        input.DefaultFontName.Should().Be("Arial");
        input.DefaultFontSize.Should().Be(14);
        input.DefaultSheetCount.Should().Be(3);
        input.UserName.Should().Be("Ada");
        input.AutoCalculate.Should().BeFalse();
        input.UseR1C1ReferenceStyle.Should().BeTrue();
        input.ErrorCheckingEnabled.Should().BeFalse();
        input.ProofingIgnoreUppercase.Should().BeFalse();
        input.ProofingIgnoreNumbers.Should().BeTrue();
        input.ShowFormulaBar.Should().BeFalse();
        input.ShowGridlines.Should().BeFalse();
        input.ShowHeadings.Should().BeTrue();
        // Legacy .json maps to the native FreeX format.
        input.DefaultFormat.Should().Be(AppOptions.FreeXWorkbookDefaultFormat);
        input.ShowScreenTips.Should().BeFalse();
        input.MoveSelectionAfterEnter.Should().BeFalse();
        input.AfterEnterDirection.Should().Be(AppOptionsEnterDirection.Up);
    }

    [Fact]
    public void Project_CarriesOverUnmanagedFields()
    {
        var existing = new AppOptions
        {
            AppLanguage = "fr-FR",
            CollapseRibbonAutomatically = true,
            FormulaBarExpanded = true,
            MoveSelectionAfterEnter = false,
            AfterEnterDirection = AppOptionsEnterDirection.Right,
            ObjectsDisplay = AppOptionsObjectDisplay.Placeholders,
            StatusBarShowMaximum = true,
            QuickAccessToolbarBelowRibbon = true,
            QuickAccessToolbarCommands = ["Save", "Undo"],
            CrashAnalyticsEnabled = true,
            CrashAnalyticsPrompted = true,
            PdfExportLanguage = "de-DE",
            SpellCheckCustomDictionaryWords = ["Foo", "Bar"],
        };

        OptionsDialogPlanner.TryBuildInput(
            "Calibri", "12", "2", "Grace",
            autoCalculate: true, useR1C1ReferenceStyle: false, errorCheckingEnabled: true,
            proofingIgnoreUppercase: true, proofingIgnoreNumbers: false,
            showFormulaBar: true, showGridlines: true, showHeadings: true,
            defaultFormat: ".xlsx", showScreenTips: true,
            // Distinct from `existing` (false / Right) so the projection proves these are now
            // dialog-managed — the input wins, not the carried-over value.
            moveSelectionAfterEnter: true, afterEnterDirection: AppOptionsEnterDirection.Down,
            out var input, out _);

        var projected = OptionsDialogPlanner.Project(existing, input);

        // Dialog-managed fields take the new values.
        projected.DefaultFontName.Should().Be("Calibri");
        projected.DefaultSheetCount.Should().Be(2);
        projected.UserName.Should().Be("Grace");
        projected.ProofingIgnoreNumbers.Should().BeFalse();
        projected.ErrorCheckingEnabled.Should().BeTrue();
        projected.MoveSelectionAfterEnter.Should().BeTrue();
        projected.AfterEnterDirection.Should().Be(AppOptionsEnterDirection.Down);

        // Unmanaged fields are carried over verbatim.
        projected.AppLanguage.Should().Be("fr-FR");
        projected.CollapseRibbonAutomatically.Should().BeTrue();
        projected.FormulaBarExpanded.Should().BeTrue();
        projected.ObjectsDisplay.Should().Be(AppOptionsObjectDisplay.Placeholders);
        projected.StatusBarShowMaximum.Should().BeTrue();
        projected.QuickAccessToolbarBelowRibbon.Should().BeTrue();
        projected.QuickAccessToolbarCommands.Should().Equal("Save", "Undo");
        projected.CrashAnalyticsEnabled.Should().BeTrue();
        projected.CrashAnalyticsPrompted.Should().BeTrue();
        projected.PdfExportLanguage.Should().Be("de-DE");
        projected.SpellCheckCustomDictionaryWords.Should().Equal("Bar", "Foo");

        // A cancelled editor works on copies and cannot mutate the persisted options object.
        existing.QuickAccessToolbarCommands.Should().Equal("Save", "Undo");
        existing.SpellCheckCustomDictionaryWords.Should().Equal("Foo", "Bar");
    }

    [Theory]
    [InlineData(".xlsx", 0)]
    [InlineData(".fxl", 1)]
    [InlineData(".json", 1)]
    [InlineData("", 0)]
    public void DefaultFormatToIndex_MapsFormats(string format, int expectedIndex) =>
        OptionsDialogPlanner.DefaultFormatToIndex(format).Should().Be(expectedIndex);

    [Theory]
    [InlineData(0, ".xlsx")]
    [InlineData(1, ".fxl")]
    public void IndexToDefaultFormat_RoundTrips(int index, string expectedFormat) =>
        OptionsDialogPlanner.IndexToDefaultFormat(index).Should().Be(expectedFormat);

    [Theory]
    [InlineData(AppOptionsEnterDirection.Down, 0)]
    [InlineData(AppOptionsEnterDirection.Right, 1)]
    [InlineData(AppOptionsEnterDirection.Up, 2)]
    [InlineData(AppOptionsEnterDirection.Left, 3)]
    public void AfterEnterDirectionIndex_RoundTrips(AppOptionsEnterDirection direction, int expectedIndex)
    {
        OptionsDialogPlanner.AfterEnterDirectionToIndex(direction).Should().Be(expectedIndex);
        OptionsDialogPlanner.IndexToAfterEnterDirection(expectedIndex).Should().Be(direction);
    }

    [Fact]
    public void IndexToAfterEnterDirection_FallsBackToDownForOutOfRangeIndex() =>
        OptionsDialogPlanner.IndexToAfterEnterDirection(-1).Should().Be(AppOptionsEnterDirection.Down);

    [Fact]
    public void DefaultFontToIndex_FallsBackToCalibriForCustomFonts()
    {
        var customIndex = OptionsDialogPlanner.DefaultFontToIndex("Wingdings");
        OptionsDialogPlanner.FontNames[customIndex].Should().Be(AppOptions.DefaultFontNameFallback);

        var arialIndex = OptionsDialogPlanner.DefaultFontToIndex("Arial");
        OptionsDialogPlanner.FontNames[arialIndex].Should().Be("Arial");
    }
}
