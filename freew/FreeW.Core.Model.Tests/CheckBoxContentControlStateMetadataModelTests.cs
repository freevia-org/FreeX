namespace FreeW.Core.Model.Tests;

public sealed class CheckBoxContentControlStateMetadataModelTests
{
    [Fact]
    public void Factory_PreservesOptionalStateMetadataWithoutChangingCheckedSemantics()
    {
        var metadata = new ContentControlCheckBoxMetadata(
            CheckedState: new ContentControlCheckBoxStateMetadata("2612", "Segoe UI Symbol"),
            UncheckedState: new ContentControlCheckBoxStateMetadata("2610", "MS Gothic"));

        var run = Run.CheckBoxControl(
            @checked: true,
            tag: "Approval",
            alias: "Approved",
            checkBoxMetadata: metadata);

        run.Text.Should().Be(ContentControl.CheckedGlyph);
        run.Control.Should().Be(new ContentControl(
            ContentControlKind.CheckBox,
            Tag: "Approval",
            Alias: "Approved",
            Checked: true,
            CheckBoxMetadata: metadata));
    }

    [Fact]
    public void Factory_LeavesStateMetadataAbsentByDefault()
    {
        var run = Run.CheckBoxControl(@checked: false);

        run.Text.Should().Be(ContentControl.UncheckedGlyph);
        run.Control!.Checked.Should().BeFalse();
        run.Control.CheckBoxMetadata.Should().BeNull();
    }
}
