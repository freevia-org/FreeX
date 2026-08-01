using System;
using FluentAssertions;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Tests for R112-app-host-fillseries-trend-unreachable (MED): the Avalonia Fill ▸ Series dialog
/// (<c>ShowFillSeriesDialogAsync</c> in MainWindow.FillSeries.cs) built rowsButton/columnsButton/
/// linearButton/.../stepBox/stopBox but never constructed a Trend control, and its
/// <c>okButton.Click</c> handler only ever called <c>FillSeriesPlanner.TryCreateOptions</c> with
/// seriesIn/type/dateUnit/step/stop -- so, like the WPF host's FillSeriesStepDialog, this shell could
/// never reach Excel's Fill ▸ Series "Trend" checkbox either, even though the shared
/// <c>FillSeriesPlanner</c> fully implements it. These tests source-check the Avalonia dialog's Trend
/// checkbox wiring (the dialog itself isn't practically constructible headless outside a live
/// MainWindow/session, matching this file's existing source-based testing style for MainWindow.cs
/// partials -- see R88_InlineCellAutoCompleteSuggestionSourceTests).
/// </summary>
public sealed class R112_FillSeriesDialogTrendTests
{
    private static string ReadFillSeriesSource() =>
        TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot("src", "FreeX.App.Avalonia", "MainWindow.FillSeries.cs");

    [Fact]
    public void FillSeriesDialog_ConstructsATrendCheckBoxWiredToSelectedType()
    {
        var source = ReadFillSeriesSource();

        source.Should().Contain("var trendBox = new CheckBox { Content = UiText.Get(\"FillSeries_Trend\") };");
        source.Should().Contain("void UpdateTrendAvailability()");
        source.Should().Contain("FillSeriesPlanner.IsTrendEnabled(SelectedType())");
        source.Should().Contain("linearButton.IsCheckedChanged += (_, _) => UpdateTrendAvailability();");
        source.Should().Contain("growthButton.IsCheckedChanged += (_, _) => UpdateTrendAvailability();");
        source.Should().Contain("dateButton.IsCheckedChanged += (_, _) => UpdateTrendAvailability();");
        source.Should().Contain("autoFillButton.IsCheckedChanged += (_, _) => UpdateTrendAvailability();");
        source.Should().Contain("trendBox.IsCheckedChanged += (_, _) => UpdateTrendAvailability();");
    }

    [Fact]
    public void FillSeriesDialog_TrendCheckedDisablesStepValueBox()
    {
        var source = ReadFillSeriesSource();

        // Excel disables the Step value box while Trend is checked (Step plays no part in Trend mode).
        source.Should().Contain("stepBox.IsEnabled = !(isTrendEligible && trendBox.IsChecked == true);");
    }

    [Fact]
    public void FillSeriesDialog_OkButtonClick_PassesTheTrendCheckBoxStateIntoTryCreateOptions()
    {
        var source = ReadFillSeriesSource();

        source.Should().Contain("var trend = trendBox.IsEnabled && trendBox.IsChecked == true;");
        source.Should().Contain("if (!FillSeriesPlanner.TryCreateOptions(");
        source.Should().Contain("stepBox.Text,");
        source.Should().Contain("stopBox.Text,");
        source.Should().Contain("trend,");
        source.Should().Contain("out var options,");
        source.Should().Contain("out var inputError))");
    }

    // No-regression sibling: the Trend checkbox must actually appear in the dialog's visible content
    // tree (not just be constructed and left unattached), sitting alongside the existing Step/Stop rows.
    [Fact]
    public void FillSeriesDialog_TrendCheckBoxIsAddedToTheDialogsVisibleContent()
    {
        var source = ReadFillSeriesSource();

        source.Should().Contain("FillSeriesLabeledBox(UiText.Get(\"FillSeries_StepValueLabel\"), stepBox),");
        source.Should().Contain("FillSeriesLabeledBox(UiText.Get(\"FillSeries_StopValueLabel\"), stopBox),");
        source.Should().Contain("trendBox,");
        source.Should().Contain("warningText,");
    }
}
