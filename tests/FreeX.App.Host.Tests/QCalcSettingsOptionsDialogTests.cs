using System.IO;
using System.Windows.Controls;
using FreeX.App.Host;
using FreeX.Core.Commands;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Regression tests for the Q-calc-settings group:
/// J26 — File &gt; Options &gt; Formulas calc-mode radios must seed from and apply back to the
/// live workbook's <see cref="Workbook.CalculationMode"/>, not the persisted app default.
/// J58 — iterative-calculation controls (enable/max iterations/max change) must be present and
/// wired to <see cref="Workbook.IterativeCalculation"/>/<see cref="Workbook.MaxCalculationIterations"/>/
/// <see cref="Workbook.MaxCalculationChange"/>.
/// </summary>
public sealed partial class OptionsDialogSourceTests
{
    [Fact]
    public void OptionsDialog_SeedsCalcModeFromLiveWorkbookNotPersistedDefault()
    {
        using var temp = new TestTemporaryDirectory();
        var path = Path.Combine(temp.Path, "options.json");
        using var optionsPath = TestEnvironmentVariableScope.Set(AppOptionsStore.OptionsPathEnvironmentVariable, path);

        StaTestRunner.Run(() =>
        {
            // The persisted app default says Automatic (true), but the live workbook the user is
            // actually editing has been switched to Manual via the ribbon. The dialog must reflect
            // the workbook, not the stale app-wide default.
            var opts = new AppOptions { AutoCalculate = true };
            var calcSettings = new OptionsDialogCalculationSettings(
                AutoCalculate: false,
                IterativeCalculation: false,
                MaxCalculationIterations: null,
                MaxCalculationChange: null);

            var dialog = new OptionsDialog(opts, calcSettings: calcSettings);
            dialog.Show();
            try
            {
                var calcAuto = GetControl<RadioButton>(dialog, "OptCalcAuto");
                var calcManual = GetControl<RadioButton>(dialog, "OptCalcManual");

                calcAuto.IsChecked.Should().BeFalse();
                calcManual.IsChecked.Should().BeTrue();
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void OptionsDialog_UnrelatedEditLeavesCalculationSettingsResultNull()
    {
        StaTestRunner.Run(() =>
        {
            var calcSettings = new OptionsDialogCalculationSettings(
                AutoCalculate: false,
                IterativeCalculation: false,
                MaxCalculationIterations: null,
                MaxCalculationChange: null);

            var dialog = new OptionsDialog(new AppOptions(), calcSettings: calcSettings);
            dialog.Show();
            try
            {
                // Change something unrelated to calculation settings, and never touch the calc
                // radios or iterative-calc fields.
                GetControl<TextBox>(dialog, "OptUserName").Text = "Someone Else";

                ClickOkAllowingNonModalDialogResult(dialog);

                dialog.CalculationSettingsResult.Should().BeNull();
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void OptionsDialog_TogglingCalcModeSurfacesCalculationSettingsResult()
    {
        StaTestRunner.Run(() =>
        {
            var calcSettings = new OptionsDialogCalculationSettings(
                AutoCalculate: true,
                IterativeCalculation: false,
                MaxCalculationIterations: null,
                MaxCalculationChange: null);

            var dialog = new OptionsDialog(new AppOptions(), calcSettings: calcSettings);
            dialog.Show();
            try
            {
                GetControl<RadioButton>(dialog, "OptCalcManual").IsChecked = true;

                ClickOkAllowingNonModalDialogResult(dialog);

                dialog.CalculationSettingsResult.Should().NotBeNull();
                dialog.CalculationSettingsResult!.AutoCalculate.Should().BeFalse();
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void OptionsDialog_ExposesIterativeCalculationControlsSeededFromWorkbook()
    {
        StaTestRunner.Run(() =>
        {
            var calcSettings = new OptionsDialogCalculationSettings(
                AutoCalculate: true,
                IterativeCalculation: true,
                MaxCalculationIterations: 250,
                MaxCalculationChange: 0.0005);

            var dialog = new OptionsDialog(new AppOptions(), calcSettings: calcSettings);
            dialog.Show();
            try
            {
                var iterativeBox = GetControl<CheckBox>(dialog, "OptIterativeEnabled");
                var maxIterations = GetControl<TextBox>(dialog, "OptMaxIterations");
                var maxChange = GetControl<TextBox>(dialog, "OptMaxChange");

                iterativeBox.IsChecked.Should().BeTrue();
                maxIterations.Text.Should().Be("250");
                maxIterations.IsEnabled.Should().BeTrue();
                maxChange.Text.Should().Be("0.0005");
                maxChange.IsEnabled.Should().BeTrue();
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void OptionsDialog_DisablingIterativeCalculationDisablesBoundsFields()
    {
        StaTestRunner.Run(() =>
        {
            var calcSettings = new OptionsDialogCalculationSettings(true, true, 100, 0.001);
            var dialog = new OptionsDialog(new AppOptions(), calcSettings: calcSettings);
            dialog.Show();
            try
            {
                var iterativeBox = GetControl<CheckBox>(dialog, "OptIterativeEnabled");
                var maxIterations = GetControl<TextBox>(dialog, "OptMaxIterations");
                var maxChange = GetControl<TextBox>(dialog, "OptMaxChange");

                iterativeBox.IsChecked = false;

                maxIterations.IsEnabled.Should().BeFalse();
                maxChange.IsEnabled.Should().BeFalse();
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void OptionsDialog_EditingIterativeCalculationRoundTripsIntoCalculationSettingsResult()
    {
        StaTestRunner.Run(() =>
        {
            var calcSettings = new OptionsDialogCalculationSettings(true, false, null, null);
            var dialog = new OptionsDialog(new AppOptions(), calcSettings: calcSettings);
            dialog.Show();
            try
            {
                GetControl<CheckBox>(dialog, "OptIterativeEnabled").IsChecked = true;
                GetControl<TextBox>(dialog, "OptMaxIterations").Text = "50";
                GetControl<TextBox>(dialog, "OptMaxChange").Text = "0.01";

                ClickOkAllowingNonModalDialogResult(dialog);

                dialog.CalculationSettingsResult.Should().NotBeNull();
                dialog.CalculationSettingsResult!.IterativeCalculation.Should().BeTrue();
                dialog.CalculationSettingsResult!.MaxCalculationIterations.Should().Be(50);
                dialog.CalculationSettingsResult!.MaxCalculationChange.Should().Be(0.01);
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void OptionsDialog_DisabledIterativeCalculationAcceptsEmptyBoundsWithoutWarning()
    {
        using var temp = new TestTemporaryDirectory();
        var path = Path.Combine(temp.Path, "options.json");
        using var optionsPath = TestEnvironmentVariableScope.Set(AppOptionsStore.OptionsPathEnvironmentVariable, path);

        StaTestRunner.Run(() =>
        {
            var calcSettings = new OptionsDialogCalculationSettings(true, false, null, null);
            var dialog = new OptionsDialog(new AppOptions(), calcSettings: calcSettings);
            dialog.Show();
            try
            {
                GetControl<CheckBox>(dialog, "OptIterativeEnabled").IsChecked = false;
                GetControl<TextBox>(dialog, "OptMaxIterations").Text = string.Empty;
                GetControl<TextBox>(dialog, "OptMaxChange").Text = string.Empty;

                ClickOkAllowingNonModalDialogResult(dialog);

                dialog.Result.Should().NotBeNull();
                dialog.CalculationSettingsResult.Should().BeNull();
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void OptionsDialog_WiresInvalidEnabledIterationBoundsToOwnedWarning()
    {
        CalculationOptionsInputParser.TryParseMaxIterations("0", out _).Should().BeFalse();

        var source = DialogSourceTestSupport.ReadHostSources("OptionsDialog.xaml.cs");
        source.Should().Contain("CalculationOptionsInputParser.TryParseBounds(");
        source.Should().Contain("CalculationOptionsInputError.InvalidMaxIterations");
        source.Should().Contain("\"Options_InvalidMaxIterationsMessage\"");
        source.Should().Contain("invalidIterations ? OptMaxIterations : OptMaxChange");
    }
}
