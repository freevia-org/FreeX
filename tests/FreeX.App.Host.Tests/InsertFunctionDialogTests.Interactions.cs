using System.Windows.Controls;
using System.Windows.Input;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed partial class InsertFunctionDialogTests
{
    [Fact]
    public void InsertFunctionDialog_FunctionListDoubleClickWithoutSelectionDoesNotHandleMouseEvent()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = new InsertFunctionDialog();
            var listBox = GetPrivateControl<ListBox>(dialog, "_listBox");
            listBox.SelectedItem = null;

            var doubleClick = DialogSourceTestSupport.CreateMouseDoubleClickEvent();

            listBox.RaiseEvent(doubleClick);

            doubleClick.Handled.Should().BeFalse();
            dialog.SelectedFormula.Should().BeNull();
            dialog.DialogResult.Should().BeNull();
        });
    }
}
