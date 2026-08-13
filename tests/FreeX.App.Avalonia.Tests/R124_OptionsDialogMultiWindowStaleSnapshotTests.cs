using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

using FluentAssertions;

using FreeX.App.Services;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Regression coverage for R124-avalonia-options-multiwindow-lastwriter (src/FreeX.App.Avalonia/MainWindow.Options.cs,
/// src/FreeX.App.Services/OptionsDialogPlanner.cs).
///
/// <para>
/// <see cref="AppOptions"/> (options.json) is a single whole-document store shared by every open
/// window/process -- FreeX has no single-instance enforcement, and Avalonia's View &gt; New Window
/// (<c>MainWindow.WindowManagement.cs</c>'s <c>NewWindow()</c>) opens an independent <see cref="MainWindow"/>
/// that loads its own <see cref="AppOptions"/> snapshot. Before this fix, the Options dialog's OK handler
/// (<c>ShowOptionsDialogAsync</c>'s <c>TryCommit</c>) built the saved record purely from the dialog's own
/// open-time snapshot (<c>current = AppOptionsStore.Load()</c>, captured when the dialog opened) via
/// <c>OptionsDialogPlanner.Project</c>, then saved that as the whole document with no reload-from-disk
/// immediately before <c>AppOptionsStore.Save</c>. So closing one window's Options dialog with OK would
/// silently discard whatever option another window (or this window's own right-click "Add to Quick Access
/// Toolbar"/"Customize Status Bar" menus, which already reload-before-mutate) had already saved while the
/// dialog was open -- last-writer-wins / lost update. Excel shares one <c>Application.Options</c> object
/// across every window of a process, so a preference change in one window is never silently undone by
/// another window's unrelated Options OK click. This is the exact defect the WPF host already fixed in
/// round 123 (<c>OptionsDialog.xaml.cs</c>, <c>tests/FreeX.App.Host.Tests/OptionsDialogSourceTests.MultiWindow.cs</c>).
/// </para>
///
/// <para>
/// These tests drive the REAL production entry point directly: <c>ShowOptionsDialogAsync</c> (the actual
/// File &gt; Options handler, invoked via reflection since it is private), interacting with the live
/// dialog's real controls (<c>OptionsShowGridlinesCheckBox</c>, the calculation-mode radio buttons,
/// <c>OptionsOkButton</c>) exactly as a user would, then reading back the saved <c>options.json</c> --
/// mirroring <c>R119_FindReplaceStaleScopeTests</c>' reflection-driven modal/modeless dialog pattern.
/// </para>
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class R124_OptionsDialogMultiWindowStaleSnapshotTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public async Task R124_SecondWindowsStaleOptionsSnapshotDoesNotRevertFirstWindowsSavedOption()
    {
        var previousPath = Environment.GetEnvironmentVariable(AppOptionsStore.OptionsPathEnvironmentVariable);
        var optionsPath = Path.Combine(Path.GetTempPath(), $"freex-options-multiwindow-{Guid.NewGuid():N}.json");
        try
        {
            // Seed on-disk options at their defaults (ShowGridlines = true).
            AppOptionsStore.SaveToPath(new AppOptions(), optionsPath).Should().BeTrue();
            Environment.SetEnvironmentVariable(AppOptionsStore.OptionsPathEnvironmentVariable, optionsPath);

            await Session.Dispatch(async () =>
            {
                var windowA = new MainWindow([]);
                var windowB = new MainWindow([]);
                try
                {
                    windowA.Show();
                    windowB.Show();

                    // Simulate two MainWindow instances sharing the process (View > New Window): each
                    // independently opens Options, which independently calls AppOptionsStore.Load() at
                    // dialog-open time, before either has saved anything -- so both dialogs start from
                    // the identical on-disk defaults.
                    var taskA = windowA.ShowOptionsDialogForTestAsync();
                    var dialogA = FindOwnedOptionsWindow(windowA);
                    var taskB = windowB.ShowOptionsDialogForTestAsync();
                    var dialogB = FindOwnedOptionsWindow(windowB);

                    // OptionsShowGridlinesCheckBox lives on the "Advanced" category page, which is not
                    // the dialog's default-selected category -- switch to it via the same left-list
                    // category selector the parity capture uses (stashed as an Action<int> on the
                    // category list's Tag, since these rows are Borders, not a TabControl).
                    SelectAdvancedCategory(dialogA);

                    // Window A turns gridlines off and saves.
                    var gridlinesA = GetByAutomationId<CheckBox>(dialogA, "OptionsShowGridlinesCheckBox");
                    gridlinesA.IsChecked.Should().BeTrue();
                    gridlinesA.IsChecked = false;
                    ClickOk(dialogA);
                    await taskA;

                    // Window B still holds its OWN independently-loaded snapshot from before window A
                    // saved. It edits a completely unrelated option (calculation mode -> Manual, on the
                    // "Formulas" category page, index 1) and saves.
                    SelectFormulasCategory(dialogB);
                    var calcManualB = GetByAutomationId<RadioButton>(dialogB, "OptionsCalcManualButton");
                    calcManualB.IsChecked.Should().BeFalse();
                    calcManualB.IsChecked = true;
                    ClickOk(dialogB);
                    await taskB;
                }
                finally
                {
                    CloseOwnedWindows(windowA);
                    CloseOwnedWindows(windowB);
                    windowA.AllowCloseWithoutDirtyPromptForParityCapture();
                    windowB.AllowCloseWithoutDirtyPromptForParityCapture();
                    windowA.Close();
                    windowB.Close();
                }

                // IMPORTANT: HeadlessUnitTestSession.Dispatch's Func<Task> (non-generic) overload does
                // NOT propagate an exception/assertion failure thrown inside the delegate back to the
                // awaiting xUnit test -- it is silently swallowed and the test reports Passed regardless
                // of what happened inside. Only the Func<Task<T>> overload propagates correctly. This
                // return makes the compiler pick that overload; do not remove it.
                return true;
            }, CancellationToken.None);

            var reloaded = AppOptionsStore.LoadFromPath(optionsPath);
            reloaded.ShowGridlines.Should().BeFalse(
                "window A's saved ShowGridlines change must survive window B's later, unrelated save -- " +
                "matching Excel, where every window of a process shares one Application.Options object");
            reloaded.AutoCalculate.Should().BeFalse("window B's own edit must still take effect");
        }
        finally
        {
            Environment.SetEnvironmentVariable(AppOptionsStore.OptionsPathEnvironmentVariable, previousPath);
            if (File.Exists(optionsPath))
                File.Delete(optionsPath);
        }
    }

    /// <summary>
    /// No-regression sibling: a single window's normal Save still applies its edited field AND keeps
    /// whatever is on disk for fields this dialog exposes no control for (e.g. status-bar visibility
    /// toggles set via the status-bar context menu) -- proving the reload-and-merge in the OK handler did
    /// not turn into "wipe everything not in the dialog" for the ordinary single-window path.
    /// </summary>
    [Fact]
    public async Task R124_SingleWindowSaveAppliesEditAndPreservesFieldsWithNoDialogControl()
    {
        var previousPath = Environment.GetEnvironmentVariable(AppOptionsStore.OptionsPathEnvironmentVariable);
        var optionsPath = Path.Combine(Path.GetTempPath(), $"freex-options-singlewindow-{Guid.NewGuid():N}.json");
        try
        {
            // Seed the disk with a non-default value for a field the Avalonia Options dialog has no
            // control for at all.
            AppOptionsStore.SaveToPath(new AppOptions { StatusBarShowMinimum = true }, optionsPath).Should().BeTrue();
            Environment.SetEnvironmentVariable(AppOptionsStore.OptionsPathEnvironmentVariable, optionsPath);

            await Session.Dispatch(async () =>
            {
                var window = new MainWindow([]);
                try
                {
                    window.Show();

                    var task = window.ShowOptionsDialogForTestAsync();
                    var dialog = FindOwnedOptionsWindow(window);
                    SelectAdvancedCategory(dialog);

                    var gridlines = GetByAutomationId<CheckBox>(dialog, "OptionsShowGridlinesCheckBox");
                    gridlines.IsChecked.Should().BeTrue();
                    gridlines.IsChecked = false;
                    ClickOk(dialog);
                    await task;
                }
                finally
                {
                    CloseOwnedWindows(window);
                    window.AllowCloseWithoutDirtyPromptForParityCapture();
                    window.Close();
                }

                return true;
            }, CancellationToken.None);

            var reloaded = AppOptionsStore.LoadFromPath(optionsPath);
            reloaded.ShowGridlines.Should().BeFalse("the dialog-edited field must persist");
            reloaded.StatusBarShowMinimum.Should().BeTrue(
                "StatusBarShowMinimum has no control on this dialog, so it must not be reset to its " +
                "default (false) on save");
        }
        finally
        {
            Environment.SetEnvironmentVariable(AppOptionsStore.OptionsPathEnvironmentVariable, previousPath);
            if (File.Exists(optionsPath))
                File.Delete(optionsPath);
        }
    }

    private static Window FindOwnedOptionsWindow(MainWindow owner)
    {
        var dialog = owner.OwnedWindows.Single(window =>
            string.Equals(AutomationProperties.GetAutomationId(window), "OptionsDialog", StringComparison.Ordinal));
        dialog.UpdateLayout();
        Dispatcher.UIThread.RunJobs(DispatcherPriority.Input);
        return dialog;
    }

    /// <summary>
    /// Switches the Options dialog's left category list to the "Advanced" page (index 6, where
    /// <c>OptionsShowGridlinesCheckBox</c>/<c>OptionsShowHeadingsCheckBox</c> actually live -- despite
    /// being constructed in the "View" section of <c>MainWindow.Options.cs</c>, they are placed into
    /// <c>advancedPanel</c>, not <c>viewPanel</c>).
    /// </summary>
    private static void SelectAdvancedCategory(Window dialog) => SelectCategory(dialog, categoryIndex: 6);

    /// <summary>Switches the Options dialog's left category list to the "Formulas" page (index 1).</summary>
    private static void SelectFormulasCategory(Window dialog) => SelectCategory(dialog, categoryIndex: 1);

    /// <summary>
    /// Switches the Options dialog's left category list to <paramref name="categoryIndex"/> using the same
    /// <c>Action&lt;int&gt;</c> selector the parity capture stashes on the category list's <c>Tag</c> (see
    /// <c>MainWindow.ParityCapture.cs</c>'s <c>FindParityCategorySelector</c>), since these category rows
    /// are plain <c>Border</c>s, not a <see cref="TabControl"/>.
    /// </summary>
    private static void SelectCategory(Window dialog, int categoryIndex)
    {
        var selector = dialog.GetVisualDescendants()
            .OfType<Control>()
            .Select(control => control.Tag as Action<int>)
            .FirstOrDefault(candidate => candidate is not null);
        selector.Should().NotBeNull("the Options dialog must expose its category selector on a control's Tag");
        selector!(categoryIndex);
        dialog.UpdateLayout();
        Dispatcher.UIThread.RunJobs(DispatcherPriority.Input);
    }

    private static T GetByAutomationId<T>(Window dialog, string automationId) where T : Control =>
        dialog.GetVisualDescendants().OfType<T>()
            .Single(control => string.Equals(AutomationProperties.GetAutomationId(control), automationId, StringComparison.Ordinal));

    private static void ClickOk(Window dialog)
    {
        var okButton = GetByAutomationId<Button>(dialog, "OptionsOkButton");
        okButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, okButton));
    }

    private static void CloseOwnedWindows(MainWindow owner)
    {
        foreach (var owned in owner.OwnedWindows.ToArray())
        {
            if (owned.IsVisible)
                owned.Close();
        }
    }

}
