using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using FreeW.App.Avalonia;
using FreeW.App.Presentation.Dialogs;

namespace FreeW.App.Avalonia.Tests;

public sealed class InsertSmartArtDialogVisualParityTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    [Fact]
    public async Task Avalonia_consumes_the_shared_Wpf_authority_geometry()
    {
        await Session.Dispatch(() =>
        {
            var metrics = SmartArtDialogPlanner.VisualMetrics;
            var dialog = CreateDialog();

            dialog.Width.Should().Be(metrics.DialogWidth);
            dialog.MinHeight.Should().Be(metrics.MinimumDialogHeight);
            ((StackPanel)dialog.Content!).Margin.Should().Be(new Thickness(metrics.OuterMargin));

            var kind = Field<ComboBox>(dialog, "_kind");
            kind.Margin.Bottom.Should().Be(metrics.LayoutControlBottomMargin);

            var nodes = Field<ListBox>(dialog, "_nodes");
            nodes.Height.Should().Be(metrics.NodeListHeight);
            nodes.MinHeight.Should().Be(metrics.NodeListHeight);
            nodes.MaxHeight.Should().Be(metrics.NodeListHeight);
            nodes.Margin.Bottom.Should().Be(metrics.NodeListBottomMargin);

            Field<TextBox>(dialog, "_edit").Margin.Bottom.Should().Be(metrics.EditorBottomMargin);

            var buttonRows = dialog.GetLogicalDescendants()
                .OfType<StackPanel>()
                .Where(panel => panel.Children.OfType<Button>().Any())
                .ToArray();
            var inlineActions = buttonRows.Single(panel =>
                panel.Children.OfType<Button>().Any(button => Equals(button.Content, "Add Shape")));
            inlineActions.Spacing.Should().Be(metrics.InlineActionSpacing);
            inlineActions.Margin.Top.Should().Be(InsertSmartArtDialog.InlineActionTemplateTopCompensation);
            inlineActions.Margin.Bottom.Should().Be(metrics.InlineActionBottomMargin);
            inlineActions.Children.OfType<Button>().Should().AllSatisfy(button =>
            {
                button.Padding.Left.Should().Be(metrics.InlineButtonHorizontalPadding);
                button.Padding.Top.Should().Be(metrics.ButtonVerticalPadding);
            });

            var footer = buttonRows.Single(panel =>
                panel.Children.OfType<Button>().Any(button => Equals(button.Content, "OK")));
            footer.Margin.Top.Should().Be(metrics.FooterTopMargin);
            footer.Children.OfType<Button>().Should().AllSatisfy(button =>
                button.MinWidth.Should().Be(metrics.FooterButtonWidth));

            dialog.Width = 546;
            dialog.Height = 563;
            dialog.SizeToContent = SizeToContent.Manual;
            dialog.Show();
            dialog.Measure(new Size(546, 563));
            dialog.Arrange(new Rect(0, 0, 546, 563));
            dialog.UpdateLayout();

            var nodeTop = nodes.TranslatePoint(default, dialog)!.Value.Y;
            var editTop = Field<TextBox>(dialog, "_edit").TranslatePoint(default, dialog)!.Value.Y;
            var inlineTop = inlineActions.TranslatePoint(default, dialog)!.Value.Y;
            var footerTop = footer.TranslatePoint(default, dialog)!.Value.Y;
            editTop.Should().BeApproximately(
                nodeTop + nodes.Bounds.Height + metrics.NodeListBottomMargin,
                0.1);
            inlineTop.Should().BeApproximately(
                editTop + Field<TextBox>(dialog, "_edit").Bounds.Height + metrics.EditorBottomMargin
                    + InsertSmartArtDialog.InlineActionTemplateTopCompensation,
                0.1);
            footerTop.Should().BeGreaterThan(inlineTop + inlineActions.Bounds.Height);
            dialog.Close();
        }, CancellationToken.None);
    }

    private static InsertSmartArtDialog CreateDialog()
    {
        var constructor = typeof(InsertSmartArtDialog).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            [typeof(FreeW.Core.Model.SmartArt)],
            modifiers: null);
        return (InsertSmartArtDialog)constructor!.Invoke([null]);
    }

    private static T Field<T>(InsertSmartArtDialog dialog, string name) where T : class =>
        (T)(typeof(InsertSmartArtDialog).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(dialog)
            ?? throw new InvalidOperationException($"Missing InsertSmartArtDialog field {name}."));
}
