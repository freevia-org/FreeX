using FluentAssertions;
using Free.Shared.AppServices;
using Free.Shared.IO;

namespace FreeX.App.Services.Tests;

public sealed class FileDialogSurfacePlannerTests
{
    [Fact]
    public void CreateOpenPlan_UsesNeutralChromeRowsDimensionsAndAutomationPolicy()
    {
        var plan = FileDialogSurfacePlanner.CreateOpenPlan(
            new FileDialogSurfaceChrome("Open Document", "Open", "Name:", "Type:"),
            [new FileDialogPickerTypeDescriptor("Text documents", ["*.txt"])],
            AutomationIds());

        plan.Kind.Should().Be(FileDialogSurfaceKind.Open);
        plan.Title.Should().Be("Open Document");
        plan.PrimaryCommandText.Should().Be("Open");
        plan.FileNameLabel.Should().Be("Name:");
        plan.FileName.Should().Be("");
        plan.FileTypeLabel.Should().Be("Type:");
        plan.DefaultExtension.Should().Be("");
        plan.DialogAutomationId.Should().Be("OpenDocumentDialog");
        plan.AutomationIds.FileNameBoxAutomationId.Should().Be("DocumentFileNameBox");
        plan.AutomationIds.FileTypeBoxAutomationId.Should().Be("DocumentFileTypeBox");
        var row = plan.FileTypes.Should().ContainSingle().Subject;
        row.DisplayName.Should().Be("Text documents");
        row.Patterns.Should().Equal("*.txt");
        FileDialogSurfacePlanner.Width.Should().Be(640);
        FileDialogSurfacePlanner.Height.Should().Be(420);
    }

    [Fact]
    public void CreateSaveAsPlan_UsesSaveChromeFileNameAndDefaultExtension()
    {
        var plan = FileDialogSurfacePlanner.CreateSaveAsPlan(
            new FileDialogSurfaceChrome("Save Document", "Save", "Name:", "Save as type:"),
            [new FileDialogPickerTypeDescriptor("Documents", ["*.docx"])],
            fileName: "Document.docx",
            defaultExtension: "docx",
            AutomationIds());

        plan.Kind.Should().Be(FileDialogSurfaceKind.SaveAs);
        plan.Title.Should().Be("Save Document");
        plan.PrimaryCommandText.Should().Be("Save");
        plan.FileName.Should().Be("Document.docx");
        plan.FileTypeLabel.Should().Be("Save as type:");
        plan.DefaultExtension.Should().Be("docx");
        plan.DialogAutomationId.Should().Be("SaveDocumentDialog");
    }

    private static FileDialogSurfaceAutomationIds AutomationIds() =>
        new(
            OpenDialogAutomationId: "OpenDocumentDialog",
            SaveAsDialogAutomationId: "SaveDocumentDialog",
            FileNameBoxAutomationId: "DocumentFileNameBox",
            FileTypeBoxAutomationId: "DocumentFileTypeBox");
}
