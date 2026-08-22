using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;

namespace FreeW.App.Avalonia.Tests;

public sealed class DrawTableDimensionDialogParityTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    [Fact]
    public async Task Draw_table_dialog_matches_Wpf_action_and_focus_semantics()
    {
        await Session.Dispatch(async () =>
        {
            var owner = new Window();
            owner.Show();
            try
            {
                var dialog = DrawTableDimensionDialog.CreateForVisualHarness();
                var resultTask = dialog.ShowDialog<(int Rows, int Columns)?>(owner);

                var buttons = dialog.GetVisualDescendants().OfType<Button>().ToArray();
                buttons.Select(button => AutomationProperties.GetName(button))
                    .Should().Equal("OK", "Cancel");
                buttons.Single(button => AutomationProperties.GetName(button) == "OK")
                    .IsDefault.Should().BeTrue();
                buttons.Single(button => AutomationProperties.GetName(button) == "Cancel")
                    .IsCancel.Should().BeTrue();

                var rows = dialog.GetVisualDescendants().OfType<TextBox>()
                    .Single(textBox => AutomationProperties.GetAutomationId(textBox) == "DrawTableRowsTextBox");
                rows.IsFocused.Should().BeTrue();

                dialog.Close(null);
                (await resultTask).Should().BeNull();
            }
            finally
            {
                owner.Close();
            }

            return true;
        }, CancellationToken.None);
    }
}
