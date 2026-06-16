using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

public class XlsxFormControlMapperTests
{
    private static readonly XNamespace FormControlNs =
        "http://schemas.microsoft.com/office/spreadsheetml/2009/9/main";

    [Fact]
    public void ReadControlProperties_CheckBoxChecked_ParsesTypeCheckedAndLinkedCell()
    {
        var formControlPr = XElement.Parse(
            """
            <formControlPr xmlns="http://schemas.microsoft.com/office/spreadsheetml/2009/9/main"
                           objectType="CheckBox" checked="Checked" fmlaLink="I4"/>
            """);

        var control = XlsxFormControlMapper.ReadControlProperties(formControlPr);

        control.Should().NotBeNull();
        control!.Kind.Should().Be(FormControlKind.CheckBox);
        control.IsChecked.Should().BeTrue();
        control.LinkedCell.Should().Be("I4");
    }

    [Fact]
    public void ReadControlProperties_ScrollBar_ParsesMinMaxValueIncrementPageAndLinkedCell()
    {
        var formControlPr = XElement.Parse(
            """
            <formControlPr xmlns="http://schemas.microsoft.com/office/spreadsheetml/2009/9/main"
                           objectType="Scroll" fmlaLink="'Calc (2)'!$D$14" max="12" min="1" page="3" val="12"/>
            """);

        var control = XlsxFormControlMapper.ReadControlProperties(formControlPr);

        control.Should().NotBeNull();
        control!.Kind.Should().Be(FormControlKind.ScrollBar);
        control.Min.Should().Be(1);
        control.Max.Should().Be(12);
        control.Value.Should().Be(12);
        control.PageChange.Should().Be(3);
        control.LinkedCell.Should().Be("'Calc (2)'!$D$14");
    }

    [Fact]
    public void ReadControlProperties_OptionButtonUnchecked_ParsesKindAndUncheckedState()
    {
        var formControlPr = XElement.Parse(
            """
            <formControlPr xmlns="http://schemas.microsoft.com/office/spreadsheetml/2009/9/main"
                           objectType="Radio"/>
            """);

        var control = XlsxFormControlMapper.ReadControlProperties(formControlPr);

        control.Should().NotBeNull();
        control!.Kind.Should().Be(FormControlKind.OptionButton);
        control.IsChecked.Should().BeFalse();
    }

    [Fact]
    public void ReadControlProperties_DropDown_ParsesSelectionAndListFillRange()
    {
        var formControlPr = XElement.Parse(
            """
            <formControlPr xmlns="http://schemas.microsoft.com/office/spreadsheetml/2009/9/main"
                           objectType="Drop" fmlaLink="$M$5" fmlaRange="high.choices" sel="2" val="0"/>
            """);

        var control = XlsxFormControlMapper.ReadControlProperties(formControlPr);

        control.Should().NotBeNull();
        control!.Kind.Should().Be(FormControlKind.DropDown);
        control.SelectedIndex.Should().Be(2);
        control.ListFillRange.Should().Be("high.choices");
        control.LinkedCell.Should().Be("$M$5");
    }
}
