using System.Diagnostics;
using FreeX.Core.Commands;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Model.Tests;

public sealed class WorkbookSheetNameIndexTests
{
    [Fact]
    public void GetSheet_NameIndexPreservesCaseInsensitiveAndNullLookupBehavior()
    {
        var workbook = new Workbook("Sheet name index");
        var sheet = workbook.AddSheet("Data");

        workbook.GetSheet("data").Should().BeSameAs(sheet);
        workbook.GetSheet("DATA").Should().BeSameAs(sheet);
        workbook.GetSheet(null!).Should().BeNull();
    }

    [Fact]
    public void GetSheet_DirectRenameRefreshesOldAndNewNamesIncludingCaseOnlyRename()
    {
        var workbook = new Workbook("Sheet name index");
        var sheet = workbook.AddSheet("Data");

        sheet.Name = "Revenue";

        workbook.GetSheet("Data").Should().BeNull();
        workbook.GetSheet("revenue").Should().BeSameAs(sheet);

        sheet.Name = "REVENUE";

        workbook.GetSheet("Revenue").Should().BeSameAs(sheet);
        workbook.GetSheet("REVENUE").Should().BeSameAs(sheet);
    }

    [Fact]
    public void GetSheet_InsertByNameRegistersInsertedSheet()
    {
        var workbook = new Workbook("Sheet name index");
        workbook.AddSheet("Last");

        var inserted = workbook.InsertSheet(0, "First");

        workbook.GetSheet("FIRST").Should().BeSameAs(inserted);
    }

    [Fact]
    public void GetSheet_InvalidDirectDuplicateRenamePreservesFirstSheetAndMoveOrder()
    {
        var workbook = new Workbook("Sheet name index");
        var first = workbook.AddSheet("First");
        var second = workbook.AddSheet("Second");

        second.Name = "FIRST";

        workbook.GetSheet("first").Should().BeSameAs(first);

        workbook.MoveSheet(1, 0);

        workbook.GetSheet("first").Should().BeSameAs(second);

        workbook.MoveSheet(0, 1);

        workbook.GetSheet("first").Should().BeSameAs(first);
    }

    [Fact]
    public void GetSheet_RenamingFirstInvalidDuplicateAwayPromotesNextSheet()
    {
        var workbook = new Workbook("Sheet name index");
        var first = workbook.AddSheet("First");
        var second = workbook.AddSheet("Second");
        second.Name = "FIRST";

        first.Name = "Other";

        workbook.GetSheet("first").Should().BeSameAs(second);
        workbook.GetSheet("other").Should().BeSameAs(first);
    }

    [Fact]
    public void GetSheet_RemovingFirstInvalidDuplicatePromotesNextAndUnsubscribesRemovedSheet()
    {
        var workbook = new Workbook("Sheet name index");
        var first = workbook.AddSheet("First");
        var second = workbook.AddSheet("Second");
        second.Name = "FIRST";

        workbook.RemoveSheet(first.Id).Should().BeTrue();

        workbook.GetSheet("first").Should().BeSameAs(second);

        first.Name = "Detached";

        workbook.GetSheet("detached").Should().BeNull();
        workbook.GetSheet("first").Should().BeSameAs(second);
    }

    [Fact]
    public void GetSheet_ReinsertedSheetResubscribesToRenameNotifications()
    {
        var workbook = new Workbook("Sheet name index");
        var sheet = workbook.AddSheet("Original");
        workbook.RemoveSheet(sheet.Id).Should().BeTrue();

        sheet.Name = "Detached";
        workbook.GetSheet("Detached").Should().BeNull();

        workbook.InsertSheet(0, sheet);
        workbook.GetSheet("detached").Should().BeSameAs(sheet);

        sheet.Name = "Restored";
        workbook.GetSheet("Detached").Should().BeNull();
        workbook.GetSheet("restored").Should().BeSameAs(sheet);
    }

    [Fact]
    public void GetSheet_RenameCommandApplyAndRevertRefreshNameIndex()
    {
        var workbook = new Workbook("Sheet name index");
        var sheet = workbook.AddSheet("Data");
        var context = new TestCommandContext(workbook);
        var command = new RenameSheetCommand(sheet.Id, "Revenue");

        command.Apply(context).Success.Should().BeTrue();
        workbook.GetSheet("Data").Should().BeNull();
        workbook.GetSheet("revenue").Should().BeSameAs(sheet);

        command.Revert(context);
        workbook.GetSheet("Revenue").Should().BeNull();
        workbook.GetSheet("data").Should().BeSameAs(sheet);
    }

    [Fact]
    public void Sheet_NameRemainsAPublicReadWriteStringProperty()
    {
        var property = typeof(Sheet).GetProperty(nameof(Sheet.Name));

        property.Should().NotBeNull();
        property!.PropertyType.Should().Be(typeof(string));
        property.GetMethod.Should().NotBeNull();
        property.GetMethod!.IsPublic.Should().BeTrue();
        property.SetMethod.Should().NotBeNull();
        property.SetMethod!.IsPublic.Should().BeTrue();
    }

    [Fact]
    public void GetSheet_UsesOrdinalIgnoreCaseDictionaryInsteadOfAListFindPerLookup()
    {
        var workbookSource = ModelSourceTestSupport.ReadModelSource("Workbook.cs");
        var sheetSource = ModelSourceTestSupport.ReadModelSource("Sheet.cs");

        workbookSource.Should().Contain(
            "Dictionary<string, Sheet> _sheetByName = new(StringComparer.OrdinalIgnoreCase)");
        workbookSource.Should().Contain("_sheetByName.TryGetValue(name, out var sheet)");
        workbookSource.Should().Contain("RefreshSheetNameIndex(oldName)");
        workbookSource.Should().Contain("RefreshSheetNameIndex(sheet.Name)");
        workbookSource.Should().NotContain(
            "_sheets.Find(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase))");
        sheetSource.Should().Contain("internal event Action<Sheet, string?, string?>? NameChanged");
        sheetSource.Should().Contain("NameChanged?.Invoke(this, oldName, value)");
    }

    [BenchmarkFact]
    public void Benchmark_GetSheet_LastOf500Sheets_HasConstantLookupCostAndNegligibleAllocations()
    {
        const int sheetCount = 500;
        const int lookupCount = 100_000;

        var workbook = new Workbook("Sheet name index benchmark");
        Sheet? expected = null;
        for (var index = 1; index <= sheetCount; index++)
            expected = workbook.AddSheet($"Sheet{index:D4}");

        const string lookupName = "SHEET0500";
        for (var index = 0; index < 1_000; index++)
            _ = workbook.GetSheet(lookupName);

        _ = GC.GetAllocatedBytesForCurrentThread();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var startedAt = Stopwatch.GetTimestamp();
        var matches = 0;
        for (var index = 0; index < lookupCount; index++)
        {
            if (ReferenceEquals(workbook.GetSheet(lookupName), expected))
                matches++;
        }

        var elapsed = Stopwatch.GetElapsedTime(startedAt);
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        Console.WriteLine(
            $"PERF WORKBOOK_SHEET_NAME_LOOKUP sheets={sheetCount} lookups={lookupCount} " +
            $"elapsed_ms={elapsed.TotalMilliseconds:F2} allocated_bytes={allocatedBytes:N0}");

        matches.Should().Be(lookupCount);
        allocatedBytes.Should().BeLessThan(128 * 1024,
            "indexed sheet-name lookup should not allocate a predicate closure for every call");
        elapsed.Should().BeLessThan(TimeSpan.FromSeconds(1),
            "100,000 lookups should use dictionary probes instead of scanning all 500 sheets");
    }
}
