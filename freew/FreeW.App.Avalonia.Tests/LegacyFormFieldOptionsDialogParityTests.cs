using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using FreeW.App.Presentation.Dialogs;
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

    [Fact]
    public async Task Restricted_document_does_not_open_the_options_dialog()
    {
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow(Array.Empty<string>());
            try
            {
                var editor = window.ActiveDocumentEditorForTests;
                editor.LoadDocument(TextDocument.CreateEmpty());
                editor.InsertLegacyFormField(LegacyFormFieldKind.Text);
                editor.SetProtection(ProtectionMode.FillingForms);
                var prompts = 0;

                await window.OpenLegacyFormFieldOptionsForTests(field =>
                {
                    prompts++;
                    return Task.FromResult<LegacyFormField?>(field with { DefaultText = "Blocked" });
                });

                prompts.Should().Be(0);
                window.CountsStatusForTests.Should().Be(LegacyFormFieldOptionsModel.Text("EditBlocked"));
                LegacyFormField.Enumerate(editor.Document).Single().FormField.DefaultText.Should().BeNull();
            }
            finally
            {
                window.Close();
            }

            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Late_apply_failure_is_reported_instead_of_silently_discarded()
    {
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow(Array.Empty<string>());
            try
            {
                var editor = window.ActiveDocumentEditorForTests;
                editor.LoadDocument(TextDocument.CreateEmpty());
                editor.InsertLegacyFormField(LegacyFormFieldKind.Text);
                var prompts = 0;

                await window.OpenLegacyFormFieldOptionsForTests(field =>
                {
                    prompts++;
                    editor.SetProtection(ProtectionMode.ReadOnly);
                    return Task.FromResult<LegacyFormField?>(field with { DefaultText = "Rejected" });
                });

                prompts.Should().Be(1);
                window.CountsStatusForTests.Should().Be(LegacyFormFieldOptionsModel.Text("ChangeFailed"));
                LegacyFormField.Enumerate(editor.Document).Single().FormField.DefaultText.Should().BeNull();
            }
            finally
            {
                window.Close();
            }

            return true;
        }, CancellationToken.None);
    }
}
