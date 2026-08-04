using FreeW.App.Host.Editing;
using FreeW.App.Presentation.Dialogs;
using FreeW.Core.Model;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace FreeW.App.Host.Tests;

public sealed class DocumentPropertiesUndoTests
{
    [StaFact]
    public void Dialog_exposes_all_modeled_core_properties_with_provenance_read_only()
    {
        var properties = TextDocument.CreateEmpty().Properties;
        properties.Category = "Reports";
        properties.ContentStatus = "Final";
        properties.Language = "en-GB";
        properties.Version = "4.2";
        properties.LastModifiedBy = "Word Owner";
        properties.Created = new DateTimeOffset(2026, 8, 4, 9, 30, 0, TimeSpan.Zero);
        properties.Modified = new DateTimeOffset(2026, 8, 4, 10, 15, 0, TimeSpan.Zero);

        var dialog = new PropertiesDialog(null!, properties);
        try
        {
            var grid = ((StackPanel)dialog.Content).Children.OfType<Grid>().Single();

            Find<TextBox>(grid, "DocumentPropertiesCategory").Text.Should().Be("Reports");
            Find<TextBox>(grid, "DocumentPropertiesContentStatus").Text.Should().Be("Final");
            Find<TextBox>(grid, "DocumentPropertiesLanguage").Text.Should().Be("en-GB");
            Find<TextBox>(grid, "DocumentPropertiesVersion").Text.Should().Be("4.2");
            Find<TextBlock>(grid, "DocumentPropertiesLastModifiedBy").Text.Should().Be("Word Owner");
            Find<TextBlock>(grid, "DocumentPropertiesCreated").Text.Should().NotBeNullOrWhiteSpace();
            Find<TextBlock>(grid, "DocumentPropertiesModified").Text.Should().NotBeNullOrWhiteSpace();
        }
        finally
        {
            dialog.Close();
        }
    }

    [StaFact]
    public void ApplyDocumentProperties_is_one_undoable_operation()
    {
        var document = TextDocument.CreateEmpty();
        document.Properties.Title = "Before";
        document.Properties.Category = "Before category";
        document.Properties.LastModifiedBy = "Word Owner";
        var view = new DocumentView();
        view.LoadModel(document);

        view.ApplyDocumentProperties(new DocumentPropertiesDialogValues(
            "After",
            "Ada",
            "Parity",
            "metadata",
            null,
            "Reports",
            "Final",
            "en-GB",
            "4.2"));

        view.Model.Properties.Title.Should().Be("After");
        view.Model.Properties.Author.Should().Be("Ada");
        view.Model.Properties.Category.Should().Be("Reports");
        view.Model.Properties.ContentStatus.Should().Be("Final");
        view.Model.Properties.Language.Should().Be("en-GB");
        view.Model.Properties.Version.Should().Be("4.2");
        view.Model.Properties.LastModifiedBy.Should().Be("Word Owner");
        view.CanUndo.Should().BeTrue();

        view.Undo();
        view.Model.Properties.Title.Should().Be("Before");
        view.Model.Properties.Author.Should().BeNull();
        view.Model.Properties.Category.Should().Be("Before category");
        view.Model.Properties.ContentStatus.Should().BeNull();
        view.Model.Properties.LastModifiedBy.Should().Be("Word Owner");

        view.Redo();
        view.Model.Properties.Title.Should().Be("After");
        view.Model.Properties.Author.Should().Be("Ada");
        view.Model.Properties.Category.Should().Be("Reports");
        view.Model.Properties.ContentStatus.Should().Be("Final");
    }

    private static T Find<T>(Panel panel, string automationId) where T : FrameworkElement =>
        panel.Children.OfType<T>().Single(element =>
            AutomationProperties.GetAutomationId(element) == automationId);
}
