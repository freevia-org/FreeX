using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed partial class PivotWorkflowDialogTests
{
    [Fact]
    public void PivotChartOptionsDialog_CreateResult_ParsesAndClampsStyle()
    {
        PivotChartOptionsDialog.CreateResult(
                " 99 ",
                showFieldButtons: false,
                showReportFilterButtons: true,
                showAxisFieldButtons: false,
                showValueFieldButtons: true)
            .Should()
            .Be(new PivotChartOptionsDialogResult(48, false, true, false, true));

        PivotChartOptionsDialog.CreateResult(
                "not-a-style",
                showFieldButtons: true,
                showReportFilterButtons: false,
                showAxisFieldButtons: true,
                showValueFieldButtons: false)
            .Should()
            .Be(new PivotChartOptionsDialogResult(null, true, false, true, false));

        PivotChartOptionsDialog.CreateResult(
                99,
                showFieldButtons: true,
                showReportFilterButtons: true,
                showAxisFieldButtons: true,
                showValueFieldButtons: true,
                roundedCorners: true,
                showHiddenData: true,
                blankDisplayMode: ChartBlankDisplayMode.Zero)
            .Should()
            .Be(new PivotChartOptionsDialogResult(48, true, true, true, true, false, false, true, true, ChartBlankDisplayMode.Zero));
    }

    [Fact]
    public void PivotChartOptionsDialog_FromChart_UsesCurrentSettings()
    {
        var chart = new ChartModel
        {
            ChartStyleId = 12,
            ShowPivotChartFieldButtons = false,
            ShowPivotChartReportFilterButtons = true,
            ShowPivotChartAxisFieldButtons = false,
            ShowPivotChartValueFieldButtons = true,
            DataTable = new ChartDataTableModel { ShowLegendKeys = true },
            RoundedCorners = true,
            ShowDataInHiddenRowsAndColumns = true,
            BlankDisplayMode = ChartBlankDisplayMode.Span
        };

        PivotChartOptionsDialog.FromChart(chart)
            .Should()
            .Be(new PivotChartOptionsDialogResult(12, false, true, false, true, true, true, true, true, ChartBlankDisplayMode.Span));
    }

    [Fact]
    public void PivotChartOptionsDialog_ExposesExcelLikeStyleAndFieldButtonGroups()
    {
        var source = ReadPivotWorkflowSource();

        source.Should().Contain("UiText.Get(\"PivotChartOptions_ChartStyle\")");
        source.Should().Contain("_styleGallery");
        source.Should().Contain("UiText.Get(\"PivotChartOptions_PivotChartStyleGallery\")");
        source.Should().Contain("ChartStyleDialog.GetStyleOptions()");
        source.Should().NotContain("Chart _style ID");
        source.Should().Contain("UiText.Get(\"PivotChartOptions_FieldButtonsGroup\")");
        source.Should().Contain("UiText.Get(\"PivotChartOptions_ShowFieldButtonsOnChart\")");
        source.Should().Contain("UiText.Get(\"PivotChartOptions_ReportFilterButtons\")");
        source.Should().Contain("UiText.Get(\"PivotChartOptions_AxisFieldButtons\")");
        source.Should().Contain("UiText.Get(\"PivotChartOptions_ValueFieldButtons\")");
        source.Should().Contain("UiText.Get(\"PivotChartOptions_ShowDataTable\")");
        source.Should().Contain("UiText.Get(\"PivotChartOptions_ShowLegendKeys\")");
        source.Should().Contain("UiText.Get(\"PivotChartOptions_RoundedCorners\")");
        source.Should().Contain("UiText.Get(\"PivotChartOptions_ShowDataInHiddenRowsAndColumns\")");
        source.Should().Contain("UiText.Get(\"PivotChartOptions_BlankCells\")");
        source.Should().NotContain("Style IDs match the built-in Excel chart style gallery");
        source.Should().NotContain("Field buttons let you filter and rearrange PivotChart data directly on the chart");
    }

    [Fact]
    public void PivotChartOptionsDialog_UsesVisualStyleGalleryAndPreservesCurrentStyle()
    {
        var chart = new ChartModel
        {
            IsPivotChart = true,
            ChartStyleId = 12
        };

        StaTestRunner.Run(() =>
        {
            var dialog = new PivotChartOptionsDialog(chart);
            var gallery = (ListBox)typeof(PivotChartOptionsDialog)
                .GetField("_styleGallery", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(dialog)!;
            var styleOptions = gallery.Items.Cast<ChartStyleOption>().ToList();

            styleOptions.Should().HaveCount(49);
            styleOptions[0].Should().Be(new ChartStyleOption(null, "Automatic", "Use current chart formatting"));
            gallery.SelectedItem.Should().Be(styleOptions.Single(option => option.StyleId == 12));

            dialog.Close();
        });
    }

    [Fact]
    public void PivotChartOptionsDialogOpenedFromKeyboard_FocusesStyleGallery()
    {
        var source = ReadClassSource(
            "PivotChartOptionsDialog.cs",
            "public sealed class PivotChartOptionsDialog",
            "");

        source.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        source.Should().Contain("private void FocusInitialKeyboardTarget()");
        source.Should().Contain("_styleGallery.Focus();");
        source.Should().Contain("Keyboard.Focus(_styleGallery);");
    }

    [Fact]
    public void PivotAuxiliaryDialogs_ExposeAccessKeysForModeledCheckboxes()
    {
        var source = ReadPivotWorkflowSource();

        source.Should().Contain("Content = UiText.Get(\"PivotChartOptions_ShowFieldButtonsOnChart\")");
        source.Should().Contain("Content = UiText.Get(\"PivotChartOptions_ReportFilterButtons\")");
        source.Should().Contain("Content = UiText.Get(\"PivotChartOptions_AxisFieldButtons\")");
        source.Should().Contain("Content = UiText.Get(\"PivotChartOptions_ValueFieldButtons\")");
        source.Should().Contain("Content = UiText.Get(\"PivotFieldGrouping_UngroupSelectedField\")");
    }
}
