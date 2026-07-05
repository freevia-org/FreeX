using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// Regression tests for K13: FreeX had no sheet-level RTL flag in the domain model at all, so an
/// Excel-authored right-to-left sheet (OOXML <c>sheetView/@rightToLeft="1"</c>) could not be
/// represented, cloned, or round-tripped through the model layer. <see cref="Sheet.IsRightToLeft"/>
/// is the foundation property; XLSX/.fxl IO round-trip is covered separately in
/// FreeX.Core.IO.Tests.SheetRightToLeftRoundTripTests.
/// </summary>
public sealed class SheetRightToLeftModelTests
{
    [Fact]
    public void Sheet_IsRightToLeft_DefaultsFalse()
    {
        var workbook = new Workbook("RtlBook");
        var sheet = workbook.AddSheet("Sheet1");

        sheet.IsRightToLeft.Should().BeFalse("Excel sheets default to left-to-right");
    }

    [Fact]
    public void Sheet_IsRightToLeft_IsSettable()
    {
        var workbook = new Workbook("RtlBook");
        var sheet = workbook.AddSheet("Sheet1");

        sheet.IsRightToLeft = true;

        sheet.IsRightToLeft.Should().BeTrue();
    }

    [Fact]
    public void Clone_CarriesOverIsRightToLeft_WhenTrue()
    {
        var workbook = new Workbook("RtlBook");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.IsRightToLeft = true;

        var clone = sheet.Clone(new SheetId(Guid.NewGuid()), "Sheet1 Copy");

        clone.IsRightToLeft.Should().BeTrue("cloning a sheet must preserve its RTL layout direction");
    }

    [Fact]
    public void Clone_CarriesOverIsRightToLeft_WhenFalse()
    {
        var workbook = new Workbook("LtrBook");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.IsRightToLeft = false;

        var clone = sheet.Clone(new SheetId(Guid.NewGuid()), "Sheet1 Copy");

        clone.IsRightToLeft.Should().BeFalse();
    }
}
