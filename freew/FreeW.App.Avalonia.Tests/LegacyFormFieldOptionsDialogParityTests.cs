using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Tests;

public sealed class LegacyFormFieldOptionsDialogParityTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    [Fact]
    public async Task Options_dialog_returns_edited_field_on_accept()
    {
        await Session.Dispatch(async () =>
        {
            var owner = new Window();
            owner.Show();
            var dialog = new LegacyFormFieldOptionsDialog(
                LegacyFormField.CreateDefault(LegacyFormFieldKind.Text, "Text1") with { DefaultText = "Original" });
            try
            {
                var resultTask = dialog.ShowDialog<LegacyFormField?>(owner);
                var defaultText = dialog.GetVisualDescendants().OfType<TextBox>()
                    .Single(box => AutomationProperties.GetName(box) == "Default text");
                defaultText.Text = "Updated";

                var accept = dialog.GetVisualDescendants().OfType<Button>()
                    .Single(button => AutomationProperties.GetAutomationId(button) == "LegacyFormFieldOptionsAccept");
                accept.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

                var result = await resultTask;
                result.Should().NotBeNull();
                result!.DefaultText.Should().Be("Updated");
            }
            finally
            {
                dialog.Close();
                owner.Close();
            }

            return true;
        }, CancellationToken.None);
    }
}
