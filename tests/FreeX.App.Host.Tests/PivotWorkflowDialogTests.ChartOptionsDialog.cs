using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FluentAssertions;
using FreeX.App.Presentation.Charts.Editing;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed partial class PivotWorkflowDialogTests
{
    [Fact]
    public void PivotChartOptionsDialog_CreateResult_ParsesAndClampsStyle()
    {
        PivotChartOptionsPlanner.CreateResult(
                " 99 ",
                showFieldButtons: false,
                showReportFilterButtons: true,
                showAxisFieldButtons: false,
                showValueFieldButtons: true)
            .Should()
            .Be(new PivotChartOptionsInput(48, false, true, false, true));

        PivotChartOptionsPlanner.CreateResult(
                "not-a-style",
                showFieldButtons: true,
                showReportFilterButtons: false,
                showAxisFieldButtons: true,
                showValueFieldButtons: false)
            .Should()
            .Be(new PivotChartOptionsInput(null, true, false, true, false));

        PivotChartOptionsPlanner.CreateResult(
                99,
                showFieldButtons: true,
                showReportFilterButtons: true,
                showAxisFieldButtons: true,
                showValueFieldButtons: true,
                roundedCorners: true,
                showHiddenData: true,
                blankDisplayMode: ChartBlankDisplayMode.Zero)
            .Should()
            .Be(new PivotChartOptionsInput(48, true, true, true, true, false, false, true, true, ChartBlankDisplayMode.Zero));
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

        PivotChartOptionsPlanner.Read(chart)
            .Should()
            .Be(new PivotChartOptionsInput(12, false, true, false, true, true, true, true, true, ChartBlankDisplayMode.Span));
    }

    [Fact]
    public void PivotChartOptionsDialog_ExposesExcelLikeStyleAndFieldButtonGroups()
    {
        var source = ReadPivotWorkflowSource();

        source.Should().Contain("FieldLabel(PivotChartOptionsDialogFieldId.ChartStyle)");
        source.Should().Contain("_styleGallery");
        source.Should().Contain("ApplyFieldAutomation(_styleGallery, PivotChartOptionsDialogFieldId.ChartStyle)");
        source.Should().Contain("ChartStyleDialog.GetStyleOptions()");
        source.Should().Contain("PivotChartOptionsPlanner.Read(chart)");
        source.Should().Contain("Result = PivotChartOptionsPlanner.CreateResult(");
        source.Should().NotContain("Chart _style ID");
        source.Should().NotContain("Math.Clamp(value.Value, 1, 48)");
        source.Should().Contain("PivotChartOptionsPlanner.GetFieldButtonsSection().HeaderResourceKey");
        source.Should().Contain("PivotChartOptionsDialogFieldId.ShowFieldButtons");
        source.Should().Contain("PivotChartOptionsDialogFieldId.ShowReportFilterButtons");
        source.Should().Contain("PivotChartOptionsDialogFieldId.ShowAxisFieldButtons");
        source.Should().Contain("PivotChartOptionsDialogFieldId.ShowValueFieldButtons");
        source.Should().Contain("PivotChartOptionsDialogFieldId.ShowDataTable");
        source.Should().Contain("PivotChartOptionsDialogFieldId.ShowDataTableLegendKeys");
        source.Should().Contain("PivotChartOptionsDialogFieldId.RoundedCorners");
        source.Should().Contain("PivotChartOptionsDialogFieldId.ShowHiddenData");
        source.Should().Contain("PivotChartOptionsDialogFieldId.BlankDisplayMode");
        source.Should().NotContain("Style IDs match the built-in Excel chart style gallery");
        source.Should().NotContain("Field buttons let you filter and rearrange PivotChart data directly on the chart");
        source.Should().Contain("public PivotChartOptionsInput Result { get; private set; }");
        source.Should().NotContain("PivotChartOptionsDialogResult");
        source.Should().NotContain("FromInput(");
        source.Should().NotContain("public static PivotChartOptionsInput CreateResult(");
        source.Should().NotContain("public static PivotChartOptionsInput FromChart(");
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
            var gallery = DialogSourceTestSupport.GetPrivateField<ListBox>(dialog, "_styleGallery");
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

        source.Should().Contain("FieldLabel(PivotChartOptionsDialogFieldId.ShowFieldButtons)");
        source.Should().Contain("FieldLabel(PivotChartOptionsDialogFieldId.ShowReportFilterButtons)");
        source.Should().Contain("FieldLabel(PivotChartOptionsDialogFieldId.ShowAxisFieldButtons)");
        source.Should().Contain("FieldLabel(PivotChartOptionsDialogFieldId.ShowValueFieldButtons)");
        source.Should().Contain("Content = UiText.Get(\"PivotFieldGrouping_UngroupSelectedField\")");
    }
}
