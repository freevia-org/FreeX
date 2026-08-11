using System.IO;
using System.Windows.Controls;
using FreeX.App.Host;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

/// <summary>
/// R123-app-options-multiwindow-lastwriter: each MainWindow loads its own independent
/// <see cref="AppOptions"/> snapshot at construction (MainWindow.xaml.cs), and View &gt; New
/// Window (MainWindow.MultiWindow.cs ViewNewWindowBtn_Click) creates a second MainWindow over the
/// SAME live workbook without sharing that snapshot -- it loads its own copy independently. Before
/// the fix, OptionsDialog.OkBtn_Click built the saved record purely from the dialog's own
/// open-time snapshot, so saving in one window silently reverted any option another window had
/// already saved (last-writer-wins), and any field this dialog exposes no control for reset to a
/// hardcoded default on every OK click. Excel shares one Application.Options object across every
/// window of a process, so a change in one window is never silently undone by another.
/// </summary>
public sealed partial class OptionsDialogSourceTests
{
    [Fact]
    public void WpfOptionsCommit_UsesSharedValidationProjectionAndIndexMappings()
    {
        var source = DialogSourceTestSupport.ReadHostSources("OptionsDialog.xaml.cs");

        source.Should().Contain("OptionsDialogPlanner.TryBuildInput(");
        source.Should().Contain("var saveResult = _dialogSession.Commit(");
        source.Should().Contain("enableAutoCompleteForCellValues: OptAdvancedAutoComplete.IsChecked == true");
        source.Should().Contain("OptionsDialogPlanner.AfterEnterDirectionToIndex(_opts.AfterEnterDirection)");
        source.Should().Contain("OptionsDialogPlanner.IndexToAfterEnterDirection(OptAfterEnterDirection.SelectedIndex)");
        source.Should().Contain("OptionsDialogPlanner.ObjectDisplayToIndex(_opts.ObjectsDisplay)");
        source.Should().Contain("OptionsDialogPlanner.IndexToObjectDisplay(OptObjectsDisplay.SelectedIndex)");
        source.Should().Contain("OptionsDialogPlanner.DefaultFormatToIndex(_opts.DefaultFormat)");
        source.Should().Contain("OptionsDialogPlanner.IndexToDefaultFormat(OptDefaultFormat.SelectedIndex)");
        source.Should().NotContain("OptionsDialogPlanner.Project(");
    }

    [Fact]
    public void R123_SecondWindowsStaleOptionsSnapshotDoesNotRevertFirstWindowsSavedOption()
    {
        using var temp = new TestTemporaryDirectory();
        var path = Path.Combine(temp.Path, "options.json");
        using var optionsPath = TestEnvironmentVariableScope.Set(AppOptionsStore.OptionsPathEnvironmentVariable, path);

        // Seed on-disk options at their defaults (ShowGridlines = true, AutoCalculate = true).
        AppOptionsStore.SaveToPath(new AppOptions(), path).Should().BeTrue();

        StaTestRunner.Run(() =>
        {
            // Simulate two MainWindow instances sharing one workbook (View > New Window): each
            // independently calls AppOptionsStore.Load() at construction time, before either one has
            // saved anything, so both start from the identical on-disk defaults.
            var windowASnapshot = AppOptionsStore.Load();
            var windowBSnapshot = AppOptionsStore.Load();

            // Window A opens Options, turns gridlines off, and saves.
            var dialogA = new OptionsDialog(windowASnapshot);
            dialogA.Show();
            try
            {
                var gridlinesA = GetControl<CheckBox>(dialogA, "OptShowGridlines");
                gridlinesA.IsChecked.Should().BeTrue();
                gridlinesA.IsChecked = false;
                ClickOkAllowingNonModalDialogResult(dialogA);
            }
            finally
            {
                dialogA.Close();
            }

            // Window B still holds its OWN independently-loaded snapshot from before window A
            // saved (windowBSnapshot never saw ShowGridlines flip to false). It edits a completely
            // unrelated option (AutoCalculate) and saves.
            var dialogB = new OptionsDialog(windowBSnapshot);
            dialogB.Show();
            try
            {
                var autoCalcB = GetControl<RadioButton>(dialogB, "OptCalcAuto");
                autoCalcB.IsChecked.Should().BeTrue();
                autoCalcB.IsChecked = false;
                ClickOkAllowingNonModalDialogResult(dialogB);
            }
            finally
            {
                dialogB.Close();
            }
        });

        var reloaded = AppOptionsStore.LoadFromPath(path);
        reloaded.ShowGridlines.Should().BeFalse(
            "window A's saved ShowGridlines change must survive window B's later, unrelated save " +
            "-- matching Excel, where every window of a process shares one Application.Options object");
        reloaded.AutoCalculate.Should().BeFalse("window B's own edit must still take effect");
    }

    /// <summary>
    /// No-regression sibling: a single window's normal Save still applies its edited field AND
    /// keeps whatever is on disk for fields this dialog exposes no control for (e.g. status-bar
    /// visibility toggles set via the status-bar context menu) -- proving the reload-and-merge in
    /// OkBtn_Click did not turn into "wipe everything not in the dialog" for the ordinary
    /// single-window path.
    /// </summary>
    [Fact]
    public void R123_SingleWindowSaveAppliesEditAndPreservesFieldsWithNoDialogControl()
    {
        using var temp = new TestTemporaryDirectory();
        var path = Path.Combine(temp.Path, "options.json");
        using var optionsPath = TestEnvironmentVariableScope.Set(AppOptionsStore.OptionsPathEnvironmentVariable, path);

        // Seed the disk with a non-default value for a field OptionsDialog has no control for.
        AppOptionsStore.SaveToPath(new AppOptions { StatusBarShowMinimum = true }, path).Should().BeTrue();

        StaTestRunner.Run(() =>
        {
            var dialog = new OptionsDialog(AppOptionsStore.Load());
            dialog.Show();
            try
            {
                var gridlines = GetControl<CheckBox>(dialog, "OptShowGridlines");
                gridlines.IsChecked.Should().BeTrue();
                gridlines.IsChecked = false;
                ClickOkAllowingNonModalDialogResult(dialog);

                dialog.Result.ShowGridlines.Should().BeFalse();
                dialog.Result.StatusBarShowMinimum.Should().BeTrue(
                    "a field with no control in this dialog must round-trip, not reset to its hardcoded default");
            }
            finally
            {
                dialog.Close();
            }
        });

        var reloaded = AppOptionsStore.LoadFromPath(path);
        reloaded.ShowGridlines.Should().BeFalse("the dialog-edited field must persist");
        reloaded.StatusBarShowMinimum.Should().BeTrue(
            "StatusBarShowMinimum has no control on this dialog, so it must not be reset to its default (false) on save");
    }
}
