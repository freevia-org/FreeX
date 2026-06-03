using System.Globalization;
using System.Text;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

public sealed partial class DelimitedTextFileAdapterTests
{
    [Fact]
    public void Load_UsesExcelLikeTextCoercionForIsoDatesAndTimes()
    {
        var adapter = new DelimitedTextFileAdapter(".tsv", "Tab-separated values", '\t');
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("2026-05-17\t2026-05-17 09:30\r\n"));

        var workbook = adapter.Load(stream);
        var sheet = workbook.Sheets.Single();

        sheet.GetValue(new CellAddress(sheet.Id, 1, 1))
            .Should().Be(DateTimeValue.FromDateTime(new DateTime(2026, 5, 17)));
        sheet.GetValue(new CellAddress(sheet.Id, 1, 2))
            .Should().Be(DateTimeValue.FromDateTime(new DateTime(2026, 5, 17, 9, 30, 0)));
    }

    [Fact]
    public void Load_UsesExcelLikeTextCoercionForIsoSlashDatesAndTimes()
    {
        var adapter = new DelimitedTextFileAdapter(".tsv", "Tab-separated values", '\t');
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("2026/5/17\t2026/05/17 9:30\r\n"));

        var workbook = adapter.Load(stream);
        var sheet = workbook.Sheets.Single();

        sheet.GetValue(new CellAddress(sheet.Id, 1, 1))
            .Should().Be(DateTimeValue.FromDateTime(new DateTime(2026, 5, 17)));
        sheet.GetValue(new CellAddress(sheet.Id, 1, 2))
            .Should().Be(DateTimeValue.FromDateTime(new DateTime(2026, 5, 17, 9, 30, 0)));
    }

    [Fact]
    public void Load_UsesExcelLikeTextCoercionForIsoDateTimesWithSingleDigitHours()
    {
        var adapter = new DelimitedTextFileAdapter(".tsv", "Tab-separated values", '\t');
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("2026-05-17 9:30\t2026-05-17T9:30:15.250\r\n"));

        var workbook = adapter.Load(stream);
        var sheet = workbook.Sheets.Single();

        sheet.GetValue(new CellAddress(sheet.Id, 1, 1))
            .Should().Be(DateTimeValue.FromDateTime(new DateTime(2026, 5, 17, 9, 30, 0)));
        sheet.GetValue(new CellAddress(sheet.Id, 1, 2))
            .Should().Be(DateTimeValue.FromDateTime(new DateTime(2026, 5, 17, 9, 30, 15, 250)));
    }

    [Fact]
    public void Load_UsesExcelLikeTextCoercionForIsoDateTimesWithOffsets()
    {
        var adapter = new DelimitedTextFileAdapter(".tsv", "Tab-separated values", '\t');
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("2026-05-17T09:30:00Z\t2026-05-17T11:30:00+02:00\r\n"));

        var workbook = adapter.Load(stream);
        var sheet = workbook.Sheets.Single();

        var expectedUtc = new DateTime(2026, 5, 17, 9, 30, 0, DateTimeKind.Unspecified);
        sheet.GetValue(new CellAddress(sheet.Id, 1, 1))
            .Should().Be(DateTimeValue.FromDateTime(expectedUtc));
        sheet.GetValue(new CellAddress(sheet.Id, 1, 2))
            .Should().Be(DateTimeValue.FromDateTime(expectedUtc));
    }

    [Fact]
    public void Load_DoesNotCoerceNonIsoSingleDigitHourDateTimesWithOffsets()
    {
        var adapter = new DelimitedTextFileAdapter(".tsv", "Tab-separated values", '\t');
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("2026-05-17T9:30Z\r\n"));

        var workbook = adapter.Load(stream);
        var sheet = workbook.Sheets.Single();

        sheet.GetValue(new CellAddress(sheet.Id, 1, 1)).Should().Be(new TextValue("2026-05-17T9:30Z"));
    }

    [Fact]
    public void Load_UsesExcelLikeTextCoercionForIsoDateTimesWithFractionalSecondOffsets()
    {
        var adapter = new DelimitedTextFileAdapter(".tsv", "Tab-separated values", '\t');
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("2026-05-17T09:30:15.250Z\t2026-05-17T11:30:15.25+02:00\r\n"));

        var workbook = adapter.Load(stream);
        var sheet = workbook.Sheets.Single();

        var expectedUtc = new DateTime(2026, 5, 17, 9, 30, 15, 250);
        sheet.GetValue(new CellAddress(sheet.Id, 1, 1))
            .Should().Be(DateTimeValue.FromDateTime(expectedUtc));
        sheet.GetValue(new CellAddress(sheet.Id, 1, 2))
            .Should().Be(DateTimeValue.FromDateTime(expectedUtc));
    }

    [Fact]
    public void Load_DoesNotCoerceIsoOffsetDateTimesWithBareFractionalSecondDecimal()
    {
        var adapter = new DelimitedTextFileAdapter(".tsv", "Tab-separated values", '\t');
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("2026-05-17T09:30:00.Z\t2026-05-17T09:30:00.+02:00\r\n"));

        var workbook = adapter.Load(stream);
        var sheet = workbook.Sheets.Single();

        sheet.GetValue(new CellAddress(sheet.Id, 1, 1)).Should().Be(new TextValue("2026-05-17T09:30:00.Z"));
        sheet.GetValue(new CellAddress(sheet.Id, 1, 2)).Should().Be(new TextValue("2026-05-17T09:30:00.+02:00"));
    }

    [Fact]
    public void Load_UsesExcelLikeTextCoercionForUsSlashDatesWithFourDigitYears()
    {
        var adapter = new DelimitedTextFileAdapter(".tsv", "Tab-separated values", '\t');
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("5/17/2026\t5/17/2026 09:30\r\n"));

        var workbook = adapter.Load(stream);
        var sheet = workbook.Sheets.Single();

        sheet.GetValue(new CellAddress(sheet.Id, 1, 1))
            .Should().Be(DateTimeValue.FromDateTime(new DateTime(2026, 5, 17)));
        sheet.GetValue(new CellAddress(sheet.Id, 1, 2))
            .Should().Be(DateTimeValue.FromDateTime(new DateTime(2026, 5, 17, 9, 30, 0)));
    }

    [Fact]
    public void Load_UsesExcelLikeTextCoercionForUsHyphenDatesWithFourDigitYears()
    {
        var adapter = new DelimitedTextFileAdapter(".tsv", "Tab-separated values", '\t');
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("5-17-2026\t5-17-2026 9:30\r\n"));

        var workbook = adapter.Load(stream);
        var sheet = workbook.Sheets.Single();

        sheet.GetValue(new CellAddress(sheet.Id, 1, 1))
            .Should().Be(DateTimeValue.FromDateTime(new DateTime(2026, 5, 17)));
        sheet.GetValue(new CellAddress(sheet.Id, 1, 2))
            .Should().Be(DateTimeValue.FromDateTime(new DateTime(2026, 5, 17, 9, 30, 0)));
    }

    [Fact]
    public void Load_UsesExcelLikeTextCoercionForUsSlashDatesWithSingleDigit24HourTimes()
    {
        var adapter = new DelimitedTextFileAdapter(".tsv", "Tab-separated values", '\t');
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("5/17/2026 9:30\t5/17/26 9:30:15.250\r\n"));

        var workbook = adapter.Load(stream);
        var sheet = workbook.Sheets.Single();

        sheet.GetValue(new CellAddress(sheet.Id, 1, 1))
            .Should().Be(DateTimeValue.FromDateTime(new DateTime(2026, 5, 17, 9, 30, 0)));
        sheet.GetValue(new CellAddress(sheet.Id, 1, 2))
            .Should().Be(DateTimeValue.FromDateTime(new DateTime(2026, 5, 17, 9, 30, 15, 250)));
    }

    [Fact]
    public void Load_UsesExcelLikeTextCoercionForUsSlashDatesWithTwoDigitYears()
    {
        var adapter = new DelimitedTextFileAdapter(".tsv", "Tab-separated values", '\t');
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("5/17/26\t5/17/26 09:30\r\n"));

        var workbook = adapter.Load(stream);
        var sheet = workbook.Sheets.Single();

        sheet.GetValue(new CellAddress(sheet.Id, 1, 1))
            .Should().Be(DateTimeValue.FromDateTime(new DateTime(2026, 5, 17)));
        sheet.GetValue(new CellAddress(sheet.Id, 1, 2))
            .Should().Be(DateTimeValue.FromDateTime(new DateTime(2026, 5, 17, 9, 30, 0)));
    }

    [Fact]
    public void Load_UsesExcelLikeTextCoercionForMonthNameDateTimes()
    {
        var adapter = new DelimitedTextFileAdapter(".tsv", "Tab-separated values", '\t');
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("May 17, 2026 9:30 AM\tMay 17, 2026 21:30:15.250\r\n"));

        var workbook = adapter.Load(stream);
        var sheet = workbook.Sheets.Single();

        sheet.GetValue(new CellAddress(sheet.Id, 1, 1))
            .Should().Be(DateTimeValue.FromDateTime(new DateTime(2026, 5, 17, 9, 30, 0)));
        sheet.GetValue(new CellAddress(sheet.Id, 1, 2))
            .Should().Be(DateTimeValue.FromDateTime(new DateTime(2026, 5, 17, 21, 30, 15, 250)));
    }

    [Fact]
    public void Load_UsesExcelLikeTextCoercionForMonthNameDateTimesWithoutCommas()
    {
        var adapter = new DelimitedTextFileAdapter(".tsv", "Tab-separated values", '\t');
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("May 17 2026 9:30 AM\tMay 17 2026 21:30:15.250\r\n"));

        var workbook = adapter.Load(stream);
        var sheet = workbook.Sheets.Single();

        sheet.GetValue(new CellAddress(sheet.Id, 1, 1))
            .Should().Be(DateTimeValue.FromDateTime(new DateTime(2026, 5, 17, 9, 30, 0)));
        sheet.GetValue(new CellAddress(sheet.Id, 1, 2))
            .Should().Be(DateTimeValue.FromDateTime(new DateTime(2026, 5, 17, 21, 30, 15, 250)));
    }

    [Fact]
    public void Load_UsesExcelLikeTextCoercionForDayFirstMonthNameDateTimes()
    {
        var adapter = new DelimitedTextFileAdapter(".tsv", "Tab-separated values", '\t');
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("17 May 2026 9:30\t17-May-26 9:45 PM\r\n"));

        var workbook = adapter.Load(stream);
        var sheet = workbook.Sheets.Single();

        sheet.GetValue(new CellAddress(sheet.Id, 1, 1))
            .Should().Be(DateTimeValue.FromDateTime(new DateTime(2026, 5, 17, 9, 30, 0)));
        sheet.GetValue(new CellAddress(sheet.Id, 1, 2))
            .Should().Be(DateTimeValue.FromDateTime(new DateTime(2026, 5, 17, 21, 45, 0)));
    }

    [Fact]
    public void Load_UsesExcelLikeTextCoercionForStandaloneTimes()
    {
        var adapter = new DelimitedTextFileAdapter(".tsv", "Tab-separated values", '\t');
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("09:30\t21:45:15\r\n"));

        var workbook = adapter.Load(stream);
        var sheet = workbook.Sheets.Single();

        sheet.GetValue(new CellAddress(sheet.Id, 1, 1)).Should().Be(new DateTimeValue(new TimeSpan(9, 30, 0).TotalDays));
        sheet.GetValue(new CellAddress(sheet.Id, 1, 2)).Should().Be(new DateTimeValue(new TimeSpan(21, 45, 15).TotalDays));
    }

    [Fact]
    public void Load_UsesExcelLikeTextCoercionForFractionalSecondDateTimesAndTimes()
    {
        var adapter = new DelimitedTextFileAdapter(".tsv", "Tab-separated values", '\t');
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("2026-05-17 09:30:15.250\t09:30:15.250\r\n"));

        var workbook = adapter.Load(stream);
        var sheet = workbook.Sheets.Single();

        sheet.GetValue(new CellAddress(sheet.Id, 1, 1))
            .Should().Be(DateTimeValue.FromDateTime(new DateTime(2026, 5, 17, 9, 30, 15, 250)));
        sheet.GetValue(new CellAddress(sheet.Id, 1, 2))
            .Should().Be(new DateTimeValue(new TimeSpan(0, 9, 30, 15, 250).TotalDays));
    }

    [Fact]
    public void Load_UsesExcelLikeTextCoercionForStandaloneAmPmTimes()
    {
        var adapter = new DelimitedTextFileAdapter(".tsv", "Tab-separated values", '\t');
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("9:30 AM\t9:45:15 PM\r\n"));

        var workbook = adapter.Load(stream);
        var sheet = workbook.Sheets.Single();

        sheet.GetValue(new CellAddress(sheet.Id, 1, 1)).Should().Be(new DateTimeValue(new TimeSpan(9, 30, 0).TotalDays));
        sheet.GetValue(new CellAddress(sheet.Id, 1, 2)).Should().Be(new DateTimeValue(new TimeSpan(21, 45, 15).TotalDays));
    }

    [Fact]
    public void Load_UsesExcelLikeTextCoercionForStandaloneAmPmHours()
    {
        var adapter = new DelimitedTextFileAdapter(".tsv", "Tab-separated values", '\t');
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("9 AM\t9 PM\r\n"));

        var workbook = adapter.Load(stream);
        var sheet = workbook.Sheets.Single();

        sheet.GetValue(new CellAddress(sheet.Id, 1, 1)).Should().Be(new DateTimeValue(new TimeSpan(9, 0, 0).TotalDays));
        sheet.GetValue(new CellAddress(sheet.Id, 1, 2)).Should().Be(new DateTimeValue(new TimeSpan(21, 0, 0).TotalDays));
    }

}
