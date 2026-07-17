using System.IO;

using FreeX.App.Presentation.InteractionValidation;

namespace FreeX.App.Avalonia.Tests;

public sealed class DialogRangeSelectionTests
{
    private static readonly string[] ExpectedTargetIds =
    [
        "range.create-table.range",
        "range.sparklines.data-range",
        "range.sparklines.location-range",
        "range.consolidate.reference",
        "range.consolidate.destination-cell",
        "range.advanced-filter.list-range",
        "range.advanced-filter.criteria-range",
        "range.advanced-filter.copy-to",
        "range.goal-seek.set-cell",
        "range.goal-seek.changing-cell",
        "range.chart-data-source.range",
    ];

    [Fact]
    public void InteractiveValidationRangeTargetIds_ExactlyMatchTheElevenWiredInventoryTargets()
    {
        MainWindow.InteractiveValidationRangeTargetIds.Should().BeEquivalentTo(ExpectedTargetIds);
        MainWindow.InteractiveValidationRangeTargetIds.Should().HaveCount(11);
        InteractiveValidationInventory.WorksheetRangeTargets
            .Where(target => MainWindow.InteractiveValidationRangeTargetIds.Contains(target.Id))
            .Select(target => target.Id)
            .Should().BeEquivalentTo(ExpectedTargetIds);
    }

    [Fact]
    public void OwnedDialogBuilders_DelegateTheirSixPickersToTheSharedSession()
    {
        var insertObjects = ReadSource("MainWindow.InsertObjects.cs");
        var sparklines = ReadSource("MainWindow.Sparklines.cs");
        var consolidate = ReadSource("MainWindow.Consolidate.cs");
        var chartTabs = ReadSource("MainWindow.ChartTabs.cs");

        insertObjects.Should().Contain("AttachDialogRangePicker(dialog, rangePicker, rangeBox, \"range.create-table.range\");");
        sparklines.Should().Contain("AttachDialogRangePicker(dialog, selectDataRangeButton, dataRangeBox, \"range.sparklines.data-range\");");
        sparklines.Should().Contain("AttachDialogRangePicker(dialog, selectLocationRangeButton, locationBox, \"range.sparklines.location-range\");");
        consolidate.Should().Contain("AttachDialogRangePicker(dialog, browseButton, referenceBox, \"range.consolidate.reference\");");
        consolidate.Should().Contain("AttachDialogRangePicker(dialog, destinationBrowseButton, destinationBox, \"range.consolidate.destination-cell\");");
        chartTabs.Should().Contain("AttachDialogRangePicker(dialog, rangePickButton, rangeBox, \"range.chart-data-source.range\");");
    }

    [Fact]
    public void SharedSession_CoversAcceptCancelRestoreAndCloseCleanup()
    {
        var source = ReadSource("MainWindow.DialogRangeSelection.cs");

        source.Should().Contain("Window.OwnerProperty.Changed.AddClassHandler<Window>(DialogRangePickerOwnerChanged);");
        source.Should().Contain("DialogRangePickerPointerReleased");
        source.Should().Contain("e.Key == Key.Escape");
        source.Should().Contain("e.Key == Key.Enter");
        source.Should().Contain("session.Target.Text = session.OriginalText;");
        source.Should().Contain("RestoreDialogAfterRangeSelection(session);");
        source.Should().Contain("dialog.Closed += DialogRangePickerDialogClosed;");
        source.Should().Contain("CancelDialogRangeSelection(restoreDialog: false, restoreOriginalText: false);");
        source.Should().Contain("SpreadsheetDisplayFormatter.FormatRangeReference");
        source.Should().Contain("SetPlatformWindowEnabledMethod?.Invoke(platformImpl, [isEnabled]);");
    }

    private static string ReadSource(string fileName) =>
        File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", fileName));

    private static string RepoFile(params string[] parts)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(parts).ToArray());
            if (File.Exists(candidate))
                return candidate;
        }

        throw new FileNotFoundException("Could not locate repository file.", Path.Combine(parts));
    }
}
