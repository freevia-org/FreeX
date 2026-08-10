using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using FreeW.App.Presentation.Dialogs;
using FreeW.App.Presentation.Ribbon;
using Free.Shared.Opc;

namespace FreeW.App.Avalonia.Tests;

public sealed class FreeWDialogFocusParityTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    [Fact]
    public async Task Focus_targets_match_the_shared_WPF_authority_plan()
    {
        await Session.Dispatch(() =>
        {
            var compare = CompareDocumentsDialog.CreateForTest(
                "C:\\Docs\\Original.docx",
                new CompareDocumentsPromptState("Reviewer", "Revised.docx"));
            var properties = new PropertiesDialog(new DocumentProperties());
            var formula = new TableFormulaDialog(new TableFormulaDialogInitialState("=SUM(ABOVE)", 0));
            var zoom = new ZoomDialog(1.0);

            AutomationProperties.GetAutomationId(compare.AuthorBoxForTest)
                .Should().Be(FreeWDialogFocusPlanner.CompareDocuments.InitialFocusTarget);
            FindTextBox(properties, FreeWDialogFocusPlanner.Properties)
                .Should().NotBeNull();
            AutomationProperties.GetAutomationId(formula.FormulaBoxForTest)
                .Should().Be(FreeWDialogFocusPlanner.TableFormula.InitialFocusTarget);
            FindTextBox(zoom, FreeWDialogFocusPlanner.Zoom)
                .Should().NotBeNull();
        }, CancellationToken.None);
    }

    private static TextBox? FindTextBox(Control dialog, Free.Shared.Shell.DialogFocusPlan<string> plan) =>
        dialog.GetLogicalDescendants().OfType<TextBox>().FirstOrDefault(textBox =>
            AutomationProperties.GetAutomationId(textBox) == plan.InitialFocusTarget);
}
