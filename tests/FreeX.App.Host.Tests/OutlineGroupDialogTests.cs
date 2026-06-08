using System.Windows.Automation;
using System.Windows.Controls;
using FreeX.Core.Commands;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class OutlineGroupDialogTests
{
    [Theory]
    [InlineData(OutlineGroupDialogMode.Group, "MainWindow_Content_Group")]
    [InlineData(OutlineGroupDialogMode.Ungroup, "MainWindow_Content_Ungroup")]
    public void Dialog_UsesExcelOutlineTitlesAndRowColumnOptions(
        OutlineGroupDialogMode mode,
        string titleKey)
    {
        StaTestRunner.Run(() =>
        {
            var dialog = new OutlineGroupDialog(mode);
            try
            {
                dialog.Title.Should().Be(UiText.Get(titleKey));

                var buttons = GetButtons(dialog);
                buttons.Should().HaveCount(2);
                buttons[0].Content.Should().Be(UiText.Get("MainWindow_Text_Rows"));
                buttons[0].Tag.Should().Be(OutlineGroupingAxis.Rows);
                buttons[0].IsChecked.Should().BeTrue();
                buttons[1].Content.Should().Be(UiText.Get("MainWindow_Text_Columns"));
                buttons[1].Tag.Should().Be(OutlineGroupingAxis.Columns);
                buttons[1].IsChecked.Should().NotBeTrue();
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void DialogChoiceButtons_ExposeAutomationMetadata()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = new OutlineGroupDialog(OutlineGroupDialogMode.Group);
            try
            {
                var buttons = GetButtons(dialog);

                AutomationProperties.GetName(buttons[0]).Should().Be("Rows");
                AutomationProperties.GetAutomationId(buttons[0]).Should().Be("OutlineGroupRowsOption");
                AutomationProperties.GetHelpText(buttons[0]).Should().Be("Apply the outline command to selected rows.");

                AutomationProperties.GetName(buttons[1]).Should().Be("Columns");
                AutomationProperties.GetAutomationId(buttons[1]).Should().Be("OutlineGroupColumnsOption");
                AutomationProperties.GetHelpText(buttons[1]).Should().Be("Apply the outline command to selected columns.");
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void DialogSource_FocusesDefaultChoiceAndUsesSharedOkCancelButtons()
    {
        var source = DialogSourceTestSupport.ReadHostSources("OutlineGroupDialog.cs");

        source.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        source.Should().Contain("Keyboard.Focus(firstButton);");
        source.Should().Contain("DialogButtonRowFactory.Create");
        source.Should().Contain("DialogResult = true;");
    }

    private static IReadOnlyList<RadioButton> GetButtons(OutlineGroupDialog dialog)
        => DialogSourceTestSupport.GetPrivateField<List<RadioButton>>(dialog, "_buttons");
}
