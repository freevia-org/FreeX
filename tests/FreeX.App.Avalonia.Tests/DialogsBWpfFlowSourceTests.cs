using FluentAssertions;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Pins the WPF-matching flows for Allow Users to Edit Ranges, File &gt; Export / Account, Define Name,
/// Data Validation, Page Setup, Options and the Pivot field-filter summaries.
/// </summary>
public sealed class DialogsBWpfFlowSourceTests
{
    [Fact]
    public void AllowEditRanges_IsModalOneShotLikeWpf()
    {
        var source = Read("MainWindow.AllowEditRange.cs");

        source.Should().NotContain("AllowEditRangeWarningText");
        source.Should().Contain("AllowEditRange_InvalidRangeMessage");
        source.Should().Contain("rangesList.DoubleTapped");
        source.Should().Contain("dialogResult = AllowEditRangePlanner.CreateRemoveResult(range);");
        source.Should().Contain("MainWindowMessage_AllowEditRangeAdded");
        source.Should().Contain("MainWindowMessage_AllowEditRangesTitle");
        source.Should().Contain("UserMessageIcon.Information");
        // Command execution happens after the dialog closes, not from the New/Modify/Delete buttons.
        source.IndexOf("await dialog.ShowDialog(this);", StringComparison.Ordinal)
            .Should().BeLessThan(source.IndexOf("ExecuteReviewCommand(plan.Command)", StringComparison.Ordinal));
    }

    [Fact]
    public void BackstageAccount_IsPlainInformationMessageLikeWpf()
    {
        var source = Read("MainWindow.Backstage.cs");
        var method = Between(source, "private async Task ShowBackstageAccountDialogAsync()", "private FreeXBackstageAccountPanePlan BuildBackstageAccountPanePlan");

        method.Should().Contain("DeferredCommandMessagePlanner.LocalAccountInfo()");
        method.Should().Contain("FreeXBackstageAccountPanePlanner.FormatMessageBody(");
        method.Should().Contain("AvaloniaUserMessageDialog.ShowAsync(");
        method.Should().NotContain("new Window");
    }

    [Fact]
    public void Export_OpensSaveDialogThenOptionsThenOverwriteConfirm()
    {
        var backstage = Read("MainWindow.Backstage.cs");
        Between(backstage, "private async Task ShowBackstageExportDialogAsync()", "BuildBackstageExportScopeOptions")
            .Should().Contain("offerXpsInSavePicker: true").And.NotContain("new Window");

        var main = Read("MainWindow.cs");
        var export = Between(main, "private async Task ExportWorkbookFixedLayoutAsync(", "private PortablePdfExportPlan CreatePortableFixedLayoutExportPlan");
        var picker = export.IndexOf("ShowPortablePdfSavePickerAsync", StringComparison.Ordinal);
        var options = export.IndexOf("ShowExportOptionsDialogAsync(", StringComparison.Ordinal);
        var overwrite = export.IndexOf("ConfirmNormalizedOverwriteAsync(", StringComparison.Ordinal);
        picker.Should().BeGreaterThan(0);
        picker.Should().BeLessThan(options);
        options.Should().BeLessThan(overwrite);
        export.Should().Contain("MainWindowDialog_ExportPdfXpsTitle");
    }

    [Fact]
    public void ExportOptions_UsesWarningBoxAndSelectionAvailabilityLikeWpf()
    {
        var source = Read("MainWindow.ExportOptions.cs");

        source.Should().NotContain("errorBlock");
        source.Should().Contain("AvaloniaUserMessageDialog.ShowWarningAsync(");
        source.Should().Contain("ExportOptions_ExportOptions");
        source.Should().Contain(".HasSelection");
    }

    [Fact]
    public void DefineName_ReportsProblemsInMessageBoxesLikeWpf()
    {
        var source = Read("MainWindow.DefinedNames.cs");
        var method = Between(source, "private async Task ShowDefineNameDialogAsync(", "private async Task ShowCreateNamesFromSelectionDialogAsync");

        method.Should().NotContain("DefineNameWarningText");
        method.Should().NotContain("okButton.IsEnabled = false");
        method.Should().Contain("NameDefinition_PleaseEnterNameMessage");
        method.Should().Contain("NameDefinition_InvalidRangeFormatMessage");
        method.Should().Contain("NamedRange_NamedRangeTitle");
    }

    [Fact]
    public void DataValidation_UsesLocalizedWarningBoxAndFocusLikeWpf()
    {
        var source = Read("MainWindow.cs");
        var method = Between(source, "async Task AcceptAsync()", "var rule = DataValidationDialogPlanner.CreateRule");

        method.Should().Contain("AvaloniaUserMessageDialog.ShowWarningAsync(");
        method.Should().Contain("FocusTargetForInvalidCriteria");
        source.Should().Contain("DvValidationErrorKind.InvalidListCriteria => UiText.Get(\"DataValidation_InvalidListCriteria\")");
    }

    [Fact]
    public void PageSetupAndOptions_ValidationUsesWarningBoxes()
    {
        var pageLayout = Read("MainWindow.PageLayout.cs");
        pageLayout.Should().Contain("PageSetupSubmissionPlanner.DefaultCaptionResourceKey");
        pageLayout.Should().Contain("async Task AcceptAsync(PageSetupDialogAction requestedAction)");

        var options = Read("MainWindow.Options.cs");
        options.Should().Contain("async Task<bool> TryCommit()");
        options.Should().Contain("WarnAndFocusAsync(");
    }

    [Fact]
    public void PivotFieldFilter_ShowsLiveSummariesLikeWpf()
    {
        var source = Read("MainWindow.PivotFilters.cs");

        source.Should().Contain("Text = filterState.ItemSummary,");
        source.Should().Contain("Text = filterState.LabelSummary,");
        source.Should().Contain("Text = filterState.ValueSummary,");
        source.Should().Contain("PivotFieldFilterSummary.FormatClearFilterHeader(filterState)");
    }

    private static string Read(string file) =>
        File.ReadAllText(Path.Combine(
            TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx"),
            "src", "FreeX.App.Avalonia", file));

    private static string Between(string source, string start, string end)
    {
        var from = source.IndexOf(start, StringComparison.Ordinal);
        from.Should().BeGreaterThanOrEqualTo(0, start);
        var to = source.IndexOf(end, from, StringComparison.Ordinal);
        to.Should().BeGreaterThan(from, end);
        return source[from..to];
    }
}
