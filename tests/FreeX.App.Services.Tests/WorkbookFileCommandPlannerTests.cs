using FluentAssertions;
using Free.Shared.IO;
using FreeX.Core.IO;

namespace FreeX.App.Services.Tests;

public sealed class WorkbookFileCommandPlannerTests
{
    [Fact]
    public void PlanOpenPicker_WhenStorageCannotOpen_BlocksBeforeFileTypes()
    {
        var plan = WorkbookFileCommandPlanner.PlanOpenPicker(
            canOpen: false,
            openFormats: Formats(static format => format.CanOpen));

        plan.CanShowPicker.Should().BeFalse();
        plan.Message.Should().Be(WorkbookFileCommandPlanner.OpenUnavailableMessage);
        plan.FileTypes.Should().BeEmpty();
    }

    [Fact]
    public void PlanOpenPicker_WithNoOpenFormats_BlocksWithSharedMessage()
    {
        var plan = WorkbookFileCommandPlanner.PlanOpenPicker(
            canOpen: true,
            openFormats: []);

        plan.CanShowPicker.Should().BeFalse();
        plan.Message.Should().Be(WorkbookFileCommandPlanner.NoOpenFormatsMessage);
    }

    [Fact]
    public void PlanOpenPicker_WithFormats_ReturnsSharedWorkbookPickerDescriptor()
    {
        var plan = WorkbookFileCommandPlanner.PlanOpenPicker(
            canOpen: true,
            openFormats: Formats(static format => format.CanOpen));

        plan.CanShowPicker.Should().BeTrue();
        plan.Message.Should().BeEmpty();
        plan.FileTypes[0].DisplayName.Should().Be(WorkbookFilePickerPlanner.AllSupportedWorkbooksName);
        plan.FileTypes[0].Patterns.Should().Contain("*.xlsx");
        plan.FileTypes[0].Patterns.Should().Contain("*.fxl");
    }

    [Fact]
    public void PlanSaveAsPicker_WhenStorageCannotSave_BlocksButKeepsSuggestedName()
    {
        var plan = WorkbookFileCommandPlanner.PlanSaveAsPicker(
            canSave: false,
            saveFormats: Formats(static format => format.CanSave),
            sourceName: "Quarterly Budget.xlsx",
            fallbackDisplayName: "Book1",
            preferredExtension: ".fxl");

        plan.CanShowPicker.Should().BeFalse();
        plan.Message.Should().Be(WorkbookFileCommandPlanner.SaveAsUnavailableMessage);
        plan.FileTypes.Should().BeEmpty();
        plan.SuggestedFileName.Should().Be("Quarterly Budget.fxl");
        plan.DefaultExtensionWithoutDot.Should().Be("fxl");
    }

    [Fact]
    public void PlanSaveAsPicker_WithNoSaveFormats_BlocksWithSharedMessage()
    {
        var plan = WorkbookFileCommandPlanner.PlanSaveAsPicker(
            canSave: true,
            saveFormats: [],
            sourceName: "Book1",
            fallbackDisplayName: "Book1",
            preferredExtension: ".fxl");

        plan.CanShowPicker.Should().BeFalse();
        plan.Message.Should().Be(WorkbookFileCommandPlanner.NoSaveFormatsMessage);
    }

    [Fact]
    public void PlanSaveAsPicker_WithFormats_PromotesPreferredWorkbookFormat()
    {
        var plan = WorkbookFileCommandPlanner.PlanSaveAsPicker(
            canSave: true,
            saveFormats: Formats(static format => format.CanSave),
            sourceName: "Quarterly Budget.xlsx",
            fallbackDisplayName: "Book1",
            preferredExtension: ".fxl");

        plan.CanShowPicker.Should().BeTrue();
        plan.Message.Should().BeEmpty();
        plan.SuggestedFileName.Should().Be("Quarterly Budget.fxl");
        plan.DefaultExtensionWithoutDot.Should().Be("fxl");
        plan.FileTypes[0].DisplayName.Should().Be("FreeX Workbook");
        plan.FileTypes[0].Patterns.Should().Equal("*.fxl");
    }

    private static IReadOnlyList<FileFormatDescriptor> Formats(Func<FileFormatDescriptor, bool> predicate) =>
        WorkbookFileAdapterCatalog.CreateDefaultAdapters()
            .SelectMany(adapter => adapter.Formats)
            .Where(predicate)
            .ToList();
}
