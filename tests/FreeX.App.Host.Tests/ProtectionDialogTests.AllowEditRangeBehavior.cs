using System.Reflection;
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
            .Be(new AllowEditRangeButtonState(false, false));
        AllowEditRangeDialogPlanner.BuildButtonState(rangeCount: 1, hasSelectedRange: false)
            .Should()
            .Be(new AllowEditRangeButtonState(false, true));
        AllowEditRangeDialogPlanner.BuildButtonState(rangeCount: 1, hasSelectedRange: true)
            .Should()
            .Be(new AllowEditRangeButtonState(true, true));
    }

    [Fact]
    public void AllowEditRangeDialogExistingRangesList_DoubleClickRemovesSelectedRange()
    {
        StaTestRunner.Run(() =>
        {
            var sheetId = SheetId.New();
            var range = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 2, 2));
            var dialog = new AllowEditRangeDialog(sheetId, "C3:D4", [range]);
            var existingRangesBox = GetPrivateField<ListBox>(dialog, "_existingRangesBox");

            dialog.Dispatcher.BeginInvoke(() =>
            {
                existingRangesBox.SelectedIndex = 0;
                var doubleClick = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
                {
                    RoutedEvent = Control.MouseDoubleClickEvent
                };
                existingRangesBox.RaiseEvent(doubleClick);
                doubleClick.Handled.Should().BeTrue();

                dialog.Dispatcher.BeginInvoke(() =>
                {
                    if (dialog.DialogResult is null)
                        dialog.Close();
                }, DispatcherPriority.ContextIdle);
            }, DispatcherPriority.ApplicationIdle);

            dialog.ShowDialog().Should().BeTrue();
            dialog.Result.Should().Be(AllowEditRangeDialog.CreateRemoveResult(range));
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
            var existingRangesBox = GetPrivateField<ListBox>(dialog, "_existingRangesBox");

            existingRangesBox.SelectedItem = null;
            var doubleClick = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
            {
                RoutedEvent = Control.MouseDoubleClickEvent
            };
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
                GetPrivateField<TextBox>(dialog, "_rangeBox").SelectionLength.Should().Be("$A$1:$C$10".Length + 2);
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

                var rangeBox = GetPrivateField<TextBox>(dialog, "_rangeBox");
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
