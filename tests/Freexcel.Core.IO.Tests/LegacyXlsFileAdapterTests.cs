using FluentAssertions;
using Freexcel.Core.IO;
using Freexcel.Core.Model;
using System.Reflection;

namespace Freexcel.Core.IO.Tests;

public sealed class LegacyXlsFileAdapterTests
{
    [Fact]
    public void Formats_AreOpenOnly()
    {
        var adapter = new LegacyXlsFileAdapter();

        adapter.Formats.Should().Contain(format =>
            format.Extension == ".xls" &&
            format.CanOpen &&
            !format.CanSave);
        adapter.Formats.Should().Contain(format =>
            format.Extension == ".xlsb" &&
            format.FormatName == "Excel Binary Workbook" &&
            format.CanOpen &&
            !format.CanSave);
        adapter.Formats.Should().Contain(format =>
            format.Extension == ".xlt" &&
            format.FormatName == "Excel 97-2003 Template" &&
            format.CanOpen &&
            !format.CanSave &&
            format.OpensAsTemplate);
    }

    [Fact]
    public void Load_ReadsLegacyBinaryWorkbookSheetsAndCells()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Simple.xls");
        using var stream = File.OpenRead(path);
        var adapter = new LegacyXlsFileAdapter();

        var workbook = adapter.Load(stream);

        workbook.Sheets.Should().NotBeEmpty();
        var firstSheet = workbook.Sheets[0];
        firstSheet.Name.Should().NotBeNullOrWhiteSpace();
        firstSheet.GetUsedRange().Should().NotBeNull();
        firstSheet.EnumerateCells()
            .Any(cell => cell.Cell.Value is TextValue or NumberValue or BoolValue)
            .Should()
            .BeTrue();
    }

    [Fact]
    public void Load_MapsLegacyDateCellsToDateTimeValues()
    {
        var value = MapLegacyXlsValue(new DateTime(2026, 5, 17, 9, 30, 0));

        value.Should().Be(DateTimeValue.FromDateTime(new DateTime(2026, 5, 17, 9, 30, 0)));
    }

    [Fact]
    public void Load_MapsLegacyTimeOnlyCellsToDateTimeValues()
    {
        var value = MapLegacyXlsValue(new TimeSpan(9, 30, 0));

        value.Should().Be(new DateTimeValue(new TimeSpan(9, 30, 0).TotalDays));
    }

    [Theory]
    [MemberData(nameof(AdditionalNumericValues))]
    public void Load_MapsLegacyNumericPrimitiveCellsToNumberValues(object legacyValue, double expected)
    {
        var value = MapLegacyXlsValue(legacyValue);

        value.Should().Be(new NumberValue(expected));
    }

    [Fact]
    public void Save_IsNotSupported()
    {
        var adapter = new LegacyXlsFileAdapter();

        var act = () => adapter.Save(new Workbook("Book1"), new MemoryStream());

        act.Should().Throw<NotSupportedException>();
    }

    private static ScalarValue MapLegacyXlsValue(object? value)
    {
        var method = typeof(LegacyXlsFileAdapter).GetMethod("MapValue", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();
        return (ScalarValue)method!.Invoke(null, [value])!;
    }

    public static TheoryData<object, double> AdditionalNumericValues() => new()
    {
        { 123L, 123d },
        { (short)-7, -7d },
        { 12.5f, 12.5d },
        { (byte)42, 42d },
        { (sbyte)-42, -42d },
        { 456u, 456d },
        { (ushort)789, 789d },
        { 900UL, 900d }
    };
}
