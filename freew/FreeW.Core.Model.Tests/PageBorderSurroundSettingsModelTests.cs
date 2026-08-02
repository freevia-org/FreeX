namespace FreeW.Core.Model.Tests;

public sealed class PageBorderSurroundSettingsModelTests
{
    [Fact]
    public void SettingsDefaultOffAndCanBeSetIndependently()
    {
        var document = new TextDocument();

        document.PageBordersDoNotSurroundHeader.Should().BeFalse();
        document.PageBordersDoNotSurroundFooter.Should().BeFalse();

        document.PageBordersDoNotSurroundHeader = true;
        document.PageBordersDoNotSurroundFooter.Should().BeFalse();

        document.PageBordersDoNotSurroundHeader = false;
        document.PageBordersDoNotSurroundFooter = true;
        document.PageBordersDoNotSurroundHeader.Should().BeFalse();
        document.PageBordersDoNotSurroundFooter.Should().BeTrue();
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void DocumentProducingOperationsRetainBothSettingsIndependently(bool excludeHeader, bool excludeFooter)
    {
        var template = DocumentWith("Template", excludeHeader, excludeFooter);
        var revised = DocumentWith("Revised", excludeHeader, excludeFooter);
        var alternate = DocumentWith("Alternate", excludeHeader, excludeFooter);

        var merged = MailMerge.MergeRecord(template, new Dictionary<string, string>());
        var ruleMerged = MailMerge.MergeRecordWithRules(
            template,
            new Dictionary<string, string>(),
            new MergeState(),
            recordIndex: 1);
        var compared = DocumentCompare.Compare(template, revised, "Reviewer", "2026-08-02T00:00:00Z");
        var combined = DocumentCombine.Combine(
            template,
            revised,
            "Reviewer A",
            alternate,
            "Reviewer B",
            "2026-08-02T00:00:00Z");

        AssertValues(merged, excludeHeader, excludeFooter);
        AssertValues(ruleMerged, excludeHeader, excludeFooter);
        AssertValues(compared, excludeHeader, excludeFooter);
        AssertValues(combined, excludeHeader, excludeFooter);
    }

    private static TextDocument DocumentWith(string text, bool excludeHeader, bool excludeFooter)
    {
        var document = new TextDocument
        {
            PageBordersDoNotSurroundHeader = excludeHeader,
            PageBordersDoNotSurroundFooter = excludeFooter
        };
        document.Blocks.Add(new Paragraph(text));
        return document;
    }

    private static void AssertValues(TextDocument document, bool expectedHeader, bool expectedFooter)
    {
        document.PageBordersDoNotSurroundHeader.Should().Be(expectedHeader);
        document.PageBordersDoNotSurroundFooter.Should().Be(expectedFooter);
    }
}
