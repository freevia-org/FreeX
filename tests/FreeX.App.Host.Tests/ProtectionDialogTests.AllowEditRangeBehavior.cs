using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using FluentAssertions;
using FreeX.Core.Model;
using System.IO;

namespace FreeX.App.Host.Tests;

public sealed partial class ProtectionDialogTests
{
    [Fact]
    public void AllowEditRangeDialog_CreateResults_CaptureRequestedAction()
    {
        var sheetId = SheetId.New();
        var range = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 2, 2));

        AllowEditRangeDialog.CreateAddResult(range)
            .Should()
            .Be(new AllowEditRangeDialogResult(AllowEditRangeDialogAction.Add, range));
        AllowEditRangeDialog.CreateModifyResult(range, range)
            .Should()
            .Be(new AllowEditRangeDialogResult(AllowEditRangeDialogAction.Modify, range, range));
        AllowEditRangeDialog.CreateRemoveResult(range)
            .Should()
            .Be(new AllowEditRangeDialogResult(AllowEditRangeDialogAction.Remove, range));
        AllowEditRangeDialog.CreateClearResult()
            .Should()
            .Be(new AllowEditRangeDialogResult(AllowEditRangeDialogAction.Clear, null));
    }

    [Fact]
    public void AllowEditRangeDialogPlanner_BuildsRangeListAndButtonState()
    {
        var sheetId = SheetId.New();
        var range = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 2, 2));

        AllowEditRangeDialogPlanner.BuildExistingRangeItems([range]).Should().Equal(range.ToString());
        AllowEditRangeDialogPlanner.BuildButtonState(rangeCount: 0, hasSelectedRange: false)
            .Should()
            .Be(new AllowEditRangeButtonState(false, false, false));
        AllowEditRangeDialogPlanner.BuildButtonState(rangeCount: 1, hasSelectedRange: false)
            .Should()
            .Be(new AllowEditRangeButtonState(false, false, false));
        AllowEditRangeDialogPlanner.BuildButtonState(rangeCount: 1, hasSelectedRange: true)
            .Should()
            .Be(new AllowEditRangeButtonState(true, true, false));
    }

    [Fact]
    public void AllowEditRangeDialogExistingRangesList_DoubleClickLoadsSelectedRangeForModification()
    {
        StaTestRunner.Run(() =>
        {
            var sheetId = SheetId.New();
            var range = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 2, 2));
            var dialog = new AllowEditRangeDialog(sheetId, "C3:D4", [range]);
            var existingRangesBox = DialogSourceTestSupport.GetPrivateField<ListBox>(dialog, "_existingRangesBox");
            var rangeBox = DialogSourceTestSupport.GetPrivateField<TextBox>(dialog, "_rangeBox");

            existingRangesBox.SelectedIndex = 0;
            var doubleClick = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
            {
                RoutedEvent = Control.MouseDoubleClickEvent
            };
            existingRangesBox.RaiseEvent(doubleClick);

            doubleClick.Handled.Should().BeTrue();
            rangeBox.Text.Should().Be(range.ToString());
            dialog.DialogResult.Should().BeNull();
        });
    }

    [Fact]
    public void AllowEditRangeDialogModifySelectedRange_ReturnsModifyResultOnAccept()
    {
        StaTestRunner.Run(() =>
        {
            var sheetId = SheetId.New();
            var originalRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 2, 2));
            var updatedRange = new GridRange(new CellAddress(sheetId, 3, 3), new CellAddress(sheetId, 4, 4));
            var dialog = new AllowEditRangeDialog(sheetId, "C3:D4", [originalRange]);
            var existingRangesBox = DialogSourceTestSupport.GetPrivateField<ListBox>(dialog, "_existingRangesBox");

            dialog.Dispatcher.BeginInvoke(() =>
            {
                existingRangesBox.SelectedIndex = 0;
                InvokePrivate(dialog, "ModifySelectedRange_Click");
                dialog.ApplyRangeSelection("C3:D4");
                InvokePrivate(dialog, "Accept");
            }, DispatcherPriority.ApplicationIdle);

            dialog.ShowDialog().Should().BeTrue();
            dialog.Result.Should().Be(AllowEditRangeDialog.CreateModifyResult(originalRange, updatedRange));
        });
    }

    [Fact]
    public void AllowEditRangeDialogExistingRangesList_DoubleClickWithoutSelectionDoesNotHandleMouseEvent()
    {
        StaTestRunner.Run(() =>
        {
            var sheetId = SheetId.New();
            var range = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 2, 2));
            var dialog = new AllowEditRangeDialog(sheetId, "C3:D4", [range]);
            var existingRangesBox = DialogSourceTestSupport.GetPrivateField<ListBox>(dialog, "_existingRangesBox");

            existingRangesBox.SelectedItem = null;
            var doubleClick = DialogSourceTestSupport.CreateMouseDoubleClickEvent();
            existingRangesBox.RaiseEvent(doubleClick);

            doubleClick.Handled.Should().BeFalse();
            dialog.DialogResult.Should().BeNull();
        });
    }

    [Fact]
    public void AllowEditRangeSelectionRequest_TrimsCurrentTextAndCollapsesDialog()
    {
        AllowEditRangeDialog.CreateRangeSelectionRequest(" $A$1:$C$10 ")
            .Should()
            .Be(new AllowEditRangeSelectionRequest("$A$1:$C$10", CollapseDialog: true));
    }

    [Fact]
    public void AllowEditRangePicker_RaisesRangeSelectionRequest()
    {
        StaTestRunner.Run(() =>
        {
            var requests = new List<AllowEditRangeSelectionRequest>();
            var dialog = new AllowEditRangeDialog(SheetId.New(), " $A$1:$C$10 ", requests.Add);
            dialog.Show();
            try
            {
                InvokePrivate(dialog, "RangePicker_Click");

                requests.Should().Equal(new AllowEditRangeSelectionRequest("$A$1:$C$10", CollapseDialog: true));
                dialog.RangeSelectionRequest.Should().Be(requests[0]);
                DialogSourceTestSupport.GetPrivateField<TextBox>(dialog, "_rangeBox").SelectionLength.Should().Be("$A$1:$C$10".Length + 2);
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void AllowEditRangeDialogApplyRangeSelection_UpdatesRangeBox()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = new AllowEditRangeDialog(SheetId.New(), "$A$1:$C$10");
            try
            {
                dialog.ApplyRangeSelection("$B$2:$D$8");

                var rangeBox = DialogSourceTestSupport.GetPrivateField<TextBox>(dialog, "_rangeBox");
                rangeBox.Text.Should().Be("$B$2:$D$8");
                rangeBox.SelectionLength.Should().Be("$B$2:$D$8".Length);
            }
            finally
            {
                dialog.Close();
            }
        });
    }
}
