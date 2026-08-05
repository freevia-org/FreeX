using System.Threading;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using FreeW.App.Avalonia;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Tests;

public sealed class ManageSourcesDialogParityTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    [Fact]
    public async Task ManageSourcesDialog_UsesWpfAuthoritySizingAndExistingControls()
    {
        await Session.Dispatch(
            () =>
            {
                var dialog = new ManageSourcesDialog(
                    currentSources: [new Source { Tag = "Current", Title = "Current source" }],
                    masterSources: [new Source { Tag = "Master", Title = "Master source" }]);

                dialog.SizeToContent.Should().Be(SizeToContent.WidthAndHeight);
                double.IsNaN(dialog.Width).Should().BeTrue("WPF lets the dialog size to its content width");
                dialog.CanResize.Should().BeFalse();

                var lists = dialog.GetLogicalDescendants().OfType<ListBox>().ToArray();
                lists.Should().HaveCount(2);
                lists.Should().OnlyContain(list => list.MinWidth == 220 && list.MinHeight == 180);
                lists.Should().OnlyContain(list => double.IsNaN(list.Height));
                lists.Should().OnlyContain(list => list.SelectedIndex == 0);

                var buttons = dialog.GetLogicalDescendants().OfType<Button>().ToArray();
                buttons.Select(button => button.Content as string).Should().Equal(
                    "Add...", "Edit...", "Delete",
                    "Copy →", "Copy <-",
                    "Add...", "Edit...", "Delete",
                    "OK", "Cancel");
                buttons.Should().OnlyContain(button => button.MinWidth == 72);
                buttons.Should().ContainSingle(button => button.IsDefault && Equals(button.Content, "OK"));
                buttons.Should().ContainSingle(button => button.IsCancel && Equals(button.Content, "Cancel"));

                return true;
            },
            CancellationToken.None);
    }
}
