using System.Threading;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using FreeW.App.Avalonia;
using FreeW.App.Presentation.Ribbon;
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
                var text = SourceManagementDialogPlanner.ResolveText(UiText.Get);
                buttons.Select(button => button.Content as string).Should().Equal(
                    text.AddButtonLabel, text.EditButtonLabel, text.DeleteButtonLabel,
                    text.CopyToCurrentButtonLabel, text.CopyToMasterButtonLabel,
                    text.AddButtonLabel, text.EditButtonLabel, text.DeleteButtonLabel,
                    text.OkButtonLabel, text.CancelButtonLabel);
                buttons.Should().OnlyContain(button => button.MinWidth == 72);
                buttons.Should().ContainSingle(button => button.IsDefault && Equals(button.Content, text.OkButtonLabel));
                buttons.Should().ContainSingle(button => button.IsCancel && Equals(button.Content, text.CancelButtonLabel));

                return true;
            },
            CancellationToken.None);
    }
}
