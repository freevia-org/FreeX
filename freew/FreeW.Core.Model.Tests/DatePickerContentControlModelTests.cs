namespace FreeW.Core.Model.Tests;

public sealed class DatePickerContentControlModelTests
{
    [Fact]
    public void Factory_PreservesDatePickerMetadata()
    {
        var metadata = new ContentControlDateMetadata(
            FullDate: "2026-06-19T00:00:00Z",
            Calendar: "gregorian",
            LanguageId: "en-US",
            StoreMappedDataAs: "dateTime");

        var run = Run.DatePickerControl(
            "2026-06-19",
            tag: "Signed",
            alias: "Signed on",
            dateFormat: "yyyy-MM-dd",
            dateMetadata: metadata);

        run.Control.Should().Be(new ContentControl(
            ContentControlKind.DatePicker,
            Tag: "Signed",
            Alias: "Signed on",
            DateFormat: "yyyy-MM-dd",
            DateMetadata: metadata));
    }

    [Fact]
    public void Factory_DefaultsFormatAndLeavesDateMetadataAbsent()
    {
        var run = Run.DatePickerControl("6/19/2026");

        run.Control!.DateFormat.Should().Be(ContentControl.DefaultDateFormat);
        run.Control.DateMetadata.Should().BeNull();
    }
}
