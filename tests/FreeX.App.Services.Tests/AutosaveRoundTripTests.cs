using FluentAssertions;
using FreeX.App.Services;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

/// <summary>
/// Round-trip test: workbook → autosave snapshot writer → recovery loader → equal cell content.
/// Uses NativeJsonAdapter directly, matching what AutosaveService uses.
/// </summary>
public sealed class AutosaveRoundTripTests
{
    private static readonly NativeJsonAdapter Adapter = new();

    [Fact]
    public void AutosaveSnapshot_RoundTrip_PreservesCellContent()
    {
        using var dir = new TestTemporaryDirectory();
        var store = new AutosaveSnapshotStore(dir.Path);

        // Build a workbook with a variety of cell types.
        var original = new Workbook("RoundTripTest");
        original.AddSheet("Sheet1");
        var sheet = original.Sheets[0];
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new NumberValue(42)));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), Cell.FromValue(new TextValue("hello")));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), Cell.FromFormula("SUM(A1:B1)"));

        // --- Write via AutosaveService snapshot path ---
        const string snapshotId = "roundtrip-test";
        var snapshotPath = store.GetSnapshotPath(snapshotId);
        Directory.CreateDirectory(dir.Path);

        using (var fs = new FileStream(snapshotPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
        {
            Adapter.Save(original, fs);
        }

        File.Exists(snapshotPath).Should().BeTrue();

        // --- Reload via NativeJsonAdapter (the same path recovery uses) ---
        Workbook recovered;
        using (var fs = new FileStream(snapshotPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            recovered = Adapter.Load(fs);
        }

        // --- Verify cell content parity ---
        recovered.Sheets.Should().HaveCount(1);
        var recoveredSheet = recovered.Sheets[0];

        var cell11 = recoveredSheet.GetCell(new CellAddress(recoveredSheet.Id, 1, 1));
        cell11.Should().NotBeNull();
        cell11!.Value.Should().BeOfType<NumberValue>()
            .Which.Value.Should().Be(42);

        var cell12 = recoveredSheet.GetCell(new CellAddress(recoveredSheet.Id, 1, 2));
        cell12.Should().NotBeNull();
        cell12!.Value.Should().BeOfType<TextValue>()
            .Which.Value.Should().Be("hello");

        var cell21 = recoveredSheet.GetCell(new CellAddress(recoveredSheet.Id, 2, 1));
        cell21.Should().NotBeNull();
        cell21!.HasFormula.Should().BeTrue();
    }

    [Fact]
    public void AutosaveSnapshot_RoundTrip_PreservesWorkbookName()
    {
        using var dir = new TestTemporaryDirectory();
        var store = new AutosaveSnapshotStore(dir.Path);

        var original = new Workbook("MyWorkbook");

        var snapshotPath = store.GetSnapshotPath("roundtrip-name-test");
        using (var fs = new FileStream(snapshotPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
        {
            Adapter.Save(original, fs);
        }

        Workbook recovered;
        using (var fs = new FileStream(snapshotPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            recovered = Adapter.Load(fs);
        }

        recovered.Name.Should().Be("MyWorkbook");
    }

    [Fact]
    public void AutosaveSnapshot_RoundTrip_MultipleSheets()
    {
        using var dir = new TestTemporaryDirectory();
        var store = new AutosaveSnapshotStore(dir.Path);

        var original = new Workbook("MultiSheet");
        original.AddSheet("Sheet1");
        original.AddSheet("Sheet2");
        var s1 = original.Sheets[0];
        var s2 = original.Sheets[1];
        s1.SetCell(new CellAddress(s1.Id, 1, 1), Cell.FromValue(new NumberValue(1)));
        s2.SetCell(new CellAddress(s2.Id, 1, 1), Cell.FromValue(new NumberValue(2)));

        var snapshotPath = store.GetSnapshotPath("roundtrip-multi");
        using (var fs = new FileStream(snapshotPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
        {
            Adapter.Save(original, fs);
        }

        Workbook recovered;
        using (var fs = new FileStream(snapshotPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            recovered = Adapter.Load(fs);
        }

        recovered.Sheets.Should().HaveCount(2);
        recovered.Sheets[0].GetCell(new CellAddress(recovered.Sheets[0].Id, 1, 1))!.Value
            .Should().BeOfType<NumberValue>().Which.Value.Should().Be(1);
        recovered.Sheets[1].GetCell(new CellAddress(recovered.Sheets[1].Id, 1, 1))!.Value
            .Should().BeOfType<NumberValue>().Which.Value.Should().Be(2);
    }
}
