using System.Collections.Generic;
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

using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// R171-freex-data-validation-F1: editing an existing Data Validation rule through the Avalonia
/// shell's compact dialog (Data &gt; Data Validation, change something, click OK) rebuilt the rule
/// from a brand-new <c>DataValidationRuleEditorInput</c> that never copied the rule-being-edited's
/// <c>IsX14</c>/<c>NativeAttributes</c>/<c>NativeChildXmls</c>/<c>NativeContainerAttributes</c>/
/// <c>NativeContainerChildXmls</c> -- so every x14/native attribute or child element a real Excel
/// file commonly stamps on a cross-sheet List validation (e.g. an <c>xr:uid</c>) was silently
/// discarded on save, even though the dialog has no editor for any of it and the user never asked to
/// remove it. The WPF host already guards this (<c>DataValidationDialog.xaml.cs</c>'s
/// <c>_existingIsX14</c>/<c>_existingNative*</c> fields, captured once from the rule being edited and
/// threaded back into the OK result) -- this test proves the Avalonia shell now does the same, by
/// driving the REAL production dialog (<c>ShowDataValidationDialogAsync</c>, exposed for tests as
/// <c>ShowDataValidationDialogForTestAsync</c>) end to end: open it on a cell whose rule carries
/// native metadata, edit the error message, click OK, and confirm the mutation that lands on the
/// sheet keeps every native field byte-for-byte while still applying the user's edit.
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class R171_DataValidationEditPreservesNativeMetadataTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public async Task EditingExistingRule_PreservesX14AndNativeMetadata()
    {
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            try
            {
                window.Show();

                var sheet = window.Session.ActiveSheet;
                var address = new CellAddress(sheet.Id, 1, 1); // A1
                var range = new GridRange(address, address);

                // Deliberately does NOT start with '=' and does NOT exceed 255 chars, so
                // DataValidationDialogPlanner's RequiresX14ForListSource heuristic cannot
                // self-heal IsX14 -- only the fix under test can preserve it.
                var nativeAttributes = new Dictionary<string, string> { ["xr:uid"] = "{TEST-UID-0001}" };
                var nativeChildXmls = new List<string> { "<extLst><ext uri=\"test\"/></extLst>" };
                var containerAttributes = new Dictionary<string, string> { ["xmlns:xr"] = "urn:test" };
                var containerChildXmls = new List<string> { "<x14:dataValidations count=\"1\" />" };

                sheet.DataValidations.Add(new DataValidation
                {
                    AppliesTo = range,
                    Type = DvType.List,
                    Formula1 = "Sheet2!$A$1:$A$3",
                    IsX14 = true,
                    NativeAttributes = nativeAttributes,
                    NativeChildXmls = nativeChildXmls,
                    NativeContainerAttributes = containerAttributes,
                    NativeContainerChildXmls = containerChildXmls,
                });

                window.Session.SelectCell(address);
                window.Session.SelectRange(range);

                var task = window.ShowDataValidationDialogForTestAsync();
                await DrainInputAsync();

                window.OwnedWindows.Should().ContainSingle();
                var dialog = window.OwnedWindows.OfType<Window>().Single();
                AutomationProperties.GetAutomationId(dialog).Should().Be("DataValidationCompactDialog");

                // Mirror the user gesture from the finding: change something the dialog DOES model
                // (the Settings tab's "Ignore blank" checkbox, visible without switching tabs) that
                // has nothing to do with the native metadata, then click OK.
                var allowBlankBox = dialog.GetVisualDescendants().OfType<CheckBox>()
                    .Single(c => AutomationProperties.GetAutomationId(c) == "DataValidationAllowBlankBox");
                allowBlankBox.IsChecked.Should().BeTrue("DataValidation's AllowBlank defaults to true and was not overridden");
                allowBlankBox.IsChecked = false;

                var applyButton = dialog.GetVisualDescendants().OfType<Button>()
                    .Single(b => AutomationProperties.GetAutomationId(b) == "DataValidationApplyButton");
                applyButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

                await DrainInputAsync();
                await task;

                var savedRule = sheet.DataValidations.Single(r => r.AppliesTo.Contains(address));
                savedRule.AllowBlank.Should().BeFalse(
                    "the user's actual edit must still take effect");
                savedRule.IsX14.Should().BeTrue(
                    "the x14 extLst flag on the rule being edited must be carried through untouched");
                savedRule.NativeAttributes.Should().NotBeNull();
                savedRule.NativeAttributes!.Should().ContainKey("xr:uid").WhoseValue.Should().Be("{TEST-UID-0001}");
                savedRule.NativeChildXmls.Should().BeEquivalentTo(nativeChildXmls);
                savedRule.NativeContainerAttributes.Should().NotBeNull();
                savedRule.NativeContainerAttributes!.Should().ContainKey("xmlns:xr");
                savedRule.NativeContainerChildXmls.Should().BeEquivalentTo(containerChildXmls);
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();
                window.Close();
            }

            return true;
        }, CancellationToken.None);
    }

    /// <summary>
    /// Sibling/adjacent case: a BRAND-NEW rule (no prior rule on the cell) must not spuriously pick
    /// up native metadata from nowhere -- IsX14 must still fall back to false and the Native* fields
    /// to null when there is nothing to carry through, exactly like before the fix.
    /// </summary>
    [Fact]
    public async Task NewRuleOnBlankCell_LeavesNativeMetadataNullAndIsX14False()
    {
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            try
            {
                window.Show();

                var sheet = window.Session.ActiveSheet;
                var address = new CellAddress(sheet.Id, 1, 2); // B1, no existing rule
                var range = new GridRange(address, address);

                window.Session.SelectCell(address);
                window.Session.SelectRange(range);

                var task = window.ShowDataValidationDialogForTestAsync();
                await DrainInputAsync();

                var dialog = window.OwnedWindows.OfType<Window>().Single();

                var formula1Box = dialog.GetVisualDescendants().OfType<TextBox>()
                    .Single(t => AutomationProperties.GetAutomationId(t) == "DataValidationFormula1Box");
                formula1Box.Text = "10";

                var formula2Box = dialog.GetVisualDescendants().OfType<TextBox>()
                    .Single(t => AutomationProperties.GetAutomationId(t) == "DataValidationFormula2Box");
                formula2Box.Text = "20";

                var applyButton = dialog.GetVisualDescendants().OfType<Button>()
                    .Single(b => AutomationProperties.GetAutomationId(b) == "DataValidationApplyButton");
                applyButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

                await DrainInputAsync();
                await task;

                var savedRule = sheet.DataValidations.Single(r => r.AppliesTo.Contains(address));
                savedRule.IsX14.Should().BeFalse("a freshly created WholeNumber rule has no x14 metadata to carry");
                savedRule.NativeAttributes.Should().BeNull();
                savedRule.NativeChildXmls.Should().BeNull();
                savedRule.NativeContainerAttributes.Should().BeNull();
                savedRule.NativeContainerChildXmls.Should().BeNull();
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();
                window.Close();
            }

            return true;
        }, CancellationToken.None);
    }

    private static async Task DrainInputAsync()
    {
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Input);
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
    }
}
