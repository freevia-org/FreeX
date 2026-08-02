namespace FreeW.Core.Model.Tests;

public sealed class ListContentControlLastValueModelTests
{
    private static readonly ContentControlListItem[] Items =
    [
        new("Red", "R"),
        new("Green", "G"),
    ];

    [Fact]
    public void Factories_PreserveListLastValueIncludingExplicitEmpty()
    {
        var dropDown = Run.DropDownListControl(Items, lastValue: "G");
        var comboBox = Run.ComboBoxControl(Items, lastValue: string.Empty);

        dropDown.Control!.ListLastValue.Should().Be("G");
        comboBox.Control!.ListLastValue.Should().BeEmpty();
    }

    [Fact]
    public void Factories_LeaveListLastValueAbsentByDefault()
    {
        Run.DropDownListControl(Items).Control!.ListLastValue.Should().BeNull();
        Run.ComboBoxControl(Items).Control!.ListLastValue.Should().BeNull();
    }
}
