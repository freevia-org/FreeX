using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed partial class FlashFillServiceTests
{
    [Fact]
    public void Fill_DateLikeComponents_ExtractsSameComponentAcrossMixedSeparators()
    {
        var result = FlashFillService.Fill(
            [("2024-1-15", "1"), ("2023/12/05", "12")],
            ["2022.7.09", "2030-11-30"]);

        result.Should().BeEquivalentTo(["7", "11"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_DateLikeComponents_ReturnsNullWhenRemainingIsNotDateLike()
    {
        var result = FlashFillService.Fill(
            [("2024-01-15", "01"), ("2023/12/05", "12")],
            ["2022-Q3-09"]);

        result.Should().BeNull();
    }

    [Fact]
    public void Fill_DateNormalization_FormatsMixedSeparatorsWithPadding()
    {
        var result = FlashFillService.Fill(
            [("1/5/2024", "2024-01-05"), ("2.9.2023", "2023-02-09")],
            ["11/3/2022", "2026.12.31"]);

        result.Should().BeEquivalentTo(["2022-11-03", "2026-12-31"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_DateNormalization_FormatsIsoSourcesToSlashDates()
    {
        var result = FlashFillService.Fill(
            [("2024-1-5", "01/05/2024"), ("2023.2.9", "02/09/2023")],
            ["2022-11-3", "2026.12.31"]);

        result.Should().BeEquivalentTo(["11/03/2022", "12/31/2026"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_DateNormalization_ReturnsNullWhenRemainingIsInvalidDate()
    {
        var result = FlashFillService.Fill(
            [("1/5/2024", "2024-01-05"), ("2/9/2023", "2023-02-09")],
            ["2/30/2022"]);

        result.Should().BeNull();
    }

    [Fact]
    public void Fill_EmbeddedDateExtraction_NormalizesDateInsideLabels()
    {
        var result = FlashFillService.Fill(
            [
                ("Invoice INV-1001 due 2024-1-5", "01/05/2024"),
                ("Ship date: 2.9.2023", "02/09/2023")
            ],
            ["Paid on 2022-11-3", "Renewal 2026.12.31 confirmed"]);

        result.Should().BeEquivalentTo(["11/03/2022", "12/31/2026"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_EmbeddedDateExtraction_ExtractsDateTokenFromLabeledText()
    {
        var result = FlashFillService.Fill(
            [
                ("Start: 2024-01-05", "2024-01-05"),
                ("Start: 2023-02-09", "2023-02-09")
            ],
            ["Start: 2022-11-03.", "Start: 2026-12-31"]);

        result.Should().BeEquivalentTo(["2022-11-03", "2026-12-31"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_EmbeddedDateExtraction_ReturnsNullWhenRemainingHasMultipleDates()
    {
        var result = FlashFillService.Fill(
            [
                ("Start: 2024-01-05", "2024-01-05"),
                ("Start: 2023-02-09", "2023-02-09")
            ],
            ["Window: 2022-11-03 to 2022-11-04"]);

        result.Should().BeNull();
    }

    [Theory]
    [InlineData("North (Retail)", "Retail", "South (Wholesale)", "Wholesale", "East (Online)", "Online")]
    [InlineData("INV [Open]", "Open", "INV [Closed]", "Closed", "INV [Pending]", "Pending")]
    [InlineData("Batch {Ready}", "Ready", "Batch {Held}", "Held", "Batch {Review}", "Review")]
    [InlineData("North \"Retail\"", "Retail", "South \"Wholesale\"", "Wholesale", "East \"Online\"", "Online")]
    [InlineData("North 'Retail'", "Retail", "South 'Wholesale'", "Wholesale", "East 'Online'", "Online")]
    [InlineData("Dept <Retail>", "Retail", "Dept <Wholesale>", "Wholesale", "Dept <Online>", "Online")]
    public void Fill_PairedDelimiterExtraction_ExtractsTextBetweenMatchingDelimiters(
        string source1,
        string expected1,
        string source2,
        string expected2,
        string remaining,
        string expectedRemaining)
    {
        var result = FlashFillService.Fill(
            [(source1, expected1), (source2, expected2)],
            [remaining]);

        result.Should().BeEquivalentTo([expectedRemaining], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_PairedDelimiterExtraction_ReturnsNullWhenRemainingDelimiterIsMissing()
    {
        var result = FlashFillService.Fill(
            [("North (Retail)", "Retail"), ("South (Wholesale)", "Wholesale")],
            ["East Online"]);

        result.Should().BeNull();
    }

    [Theory]
    [InlineData("North (Retail)", "North", "South (Wholesale)", "South", "East (Online)", "East")]
    [InlineData("INV [Open]", "INV", "REQ [Closed]", "REQ", "PO [Pending]", "PO")]
    [InlineData("Dept <Retail>", "Dept", "Team <Wholesale>", "Team", "Channel <Online>", "Channel")]
    public void Fill_PairedDelimiterRemoval_RemovesDelimitedQualifier(
        string source1,
        string expected1,
        string source2,
        string expected2,
        string remaining,
        string expectedRemaining)
    {
        var result = FlashFillService.Fill(
            [(source1, expected1), (source2, expected2)],
            [remaining]);

        result.Should().BeEquivalentTo([expectedRemaining], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_PairedDelimiterRemoval_ReturnsNullWhenRemainingDelimiterIsMissing()
    {
        var result = FlashFillService.Fill(
            [("North (Retail)", "North"), ("South (Wholesale)", "South")],
            ["East Online"]);

        result.Should().BeNull();
    }

    [Theory]
    [InlineData("Status: Open", "Open", "Status: Closed", "Closed", "Status: Pending", "Pending")]
    [InlineData("Priority = High", "High", "Priority = Low", "Low", "Priority = Medium", "Medium")]
    [InlineData("Owner - Ada", "Ada", "Owner - Grace", "Grace", "Owner - Alan", "Alan")]
    [InlineData("Owner-Ada", "Ada", "Owner-Grace", "Grace", "Owner-Alan", "Alan")]
    [InlineData("Status / Open", "Open", "Status / Closed", "Closed", "Status / Pending", "Pending")]
    [InlineData("Status/Open", "Open", "Status/Closed", "Closed", "Status/Pending", "Pending")]
    [InlineData("Status | Open", "Open", "Status | Closed", "Closed", "Status | Pending", "Pending")]
    [InlineData("Status|Open", "Open", "Status|Closed", "Closed", "Status|Pending", "Pending")]
    [InlineData("Status -> Open", "Open", "Status -> Closed", "Closed", "Status -> Pending", "Pending")]
    [InlineData("Status->Open", "Open", "Status->Closed", "Closed", "Status->Pending", "Pending")]
    [InlineData("Status => Open", "Open", "Status => Closed", "Closed", "Status => Pending", "Pending")]
    [InlineData("Status=>Open", "Open", "Status=>Closed", "Closed", "Status=>Pending", "Pending")]
    public void Fill_LabelValueExtraction_ExtractsTrimmedValueAfterSeparator(
        string source1,
        string expected1,
        string source2,
        string expected2,
        string remaining,
        string expectedRemaining)
    {
        var result = FlashFillService.Fill(
            [(source1, expected1), (source2, expected2)],
            [remaining]);

        result.Should().BeEquivalentTo([expectedRemaining], o => o.WithStrictOrdering());
    }

    [Theory]
    [InlineData("Owner  -   Ada", "Ada", "Owner\t-\tGrace", "Grace", "Owner - Alan", "Alan")]
    [InlineData("Status  /   Open", "Open", "Status\t/\tClosed", "Closed", "Status / Pending", "Pending")]
    [InlineData("Status  |   Open", "Open", "Status\t|\tClosed", "Closed", "Status | Pending", "Pending")]
    [InlineData("Status  ->   Open", "Open", "Status\t->\tClosed", "Closed", "Status -> Pending", "Pending")]
    [InlineData("Status  =>   Open", "Open", "Status\t=>\tClosed", "Closed", "Status => Pending", "Pending")]
    public void Fill_LabelValueExtraction_ToleratesUnevenSeparatorWhitespace(
        string source1,
        string expected1,
        string source2,
        string expected2,
        string remaining,
        string expectedRemaining)
    {
        var result = FlashFillService.Fill(
            [(source1, expected1), (source2, expected2)],
            [remaining]);

        result.Should().BeEquivalentTo([expectedRemaining], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_LabelValueExtraction_ReturnsNullWhenRemainingSeparatorIsMissing()
    {
        var result = FlashFillService.Fill(
            [("Status: Open", "Open"), ("Status: Closed", "Closed")],
            ["Status Pending"]);

        result.Should().BeNull();
    }

    [Theory]
    [InlineData("Status / Open", "Open", "Status / Closed", "Closed")]
    [InlineData("Status | Open", "Open", "Status | Closed", "Closed")]
    [InlineData("Status -> Open", "Open", "Status -> Closed", "Closed")]
    public void Fill_LabelValueExtraction_ReturnsNullWhenSlashPipeOrArrowSeparatorIsMissing(
        string source1,
        string expected1,
        string source2,
        string expected2)
    {
        var result = FlashFillService.Fill(
            [(source1, expected1), (source2, expected2)],
            ["Status Pending"]);

        result.Should().BeNull();
    }

    [Theory]
    [InlineData("Status: Open", "Status", "Priority: High", "Priority", "Owner: Ada", "Owner")]
    [InlineData("Status = Open", "Status", "Priority = High", "Priority", "Owner = Ada", "Owner")]
    [InlineData("Status - Open", "Status", "Priority - High", "Priority", "Owner - Ada", "Owner")]
    [InlineData("Status-Open", "Status", "Priority-High", "Priority", "Owner-Ada", "Owner")]
    [InlineData("Status / Open", "Status", "Priority / High", "Priority", "Owner / Ada", "Owner")]
    [InlineData("Status/Open", "Status", "Priority/High", "Priority", "Owner/Ada", "Owner")]
    [InlineData("Status | Open", "Status", "Priority | High", "Priority", "Owner | Ada", "Owner")]
    [InlineData("Status|Open", "Status", "Priority|High", "Priority", "Owner|Ada", "Owner")]
    [InlineData("Status -> Open", "Status", "Priority -> High", "Priority", "Owner -> Ada", "Owner")]
    [InlineData("Status->Open", "Status", "Priority->High", "Priority", "Owner->Ada", "Owner")]
    [InlineData("Status => Open", "Status", "Priority => High", "Priority", "Owner => Ada", "Owner")]
    [InlineData("Status=>Open", "Status", "Priority=>High", "Priority", "Owner=>Ada", "Owner")]
    public void Fill_LabelQualifierRemoval_RemovesValueAfterSeparator(
        string source1,
        string expected1,
        string source2,
        string expected2,
        string remaining,
        string expectedRemaining)
    {
        var result = FlashFillService.Fill(
            [(source1, expected1), (source2, expected2)],
            [remaining]);

        result.Should().BeEquivalentTo([expectedRemaining], o => o.WithStrictOrdering());
    }

    [Theory]
    [InlineData("Status  -   Open", "Status", "Priority\t-\tHigh", "Priority", "Owner - Ada", "Owner")]
    [InlineData("Status  /   Open", "Status", "Priority\t/\tHigh", "Priority", "Owner / Ada", "Owner")]
    [InlineData("Status  |   Open", "Status", "Priority\t|\tHigh", "Priority", "Owner | Ada", "Owner")]
    [InlineData("Status  ->   Open", "Status", "Priority\t->\tHigh", "Priority", "Owner -> Ada", "Owner")]
    [InlineData("Status  =>   Open", "Status", "Priority\t=>\tHigh", "Priority", "Owner => Ada", "Owner")]
    public void Fill_LabelQualifierRemoval_ToleratesUnevenSeparatorWhitespace(
        string source1,
        string expected1,
        string source2,
        string expected2,
        string remaining,
        string expectedRemaining)
    {
        var result = FlashFillService.Fill(
            [(source1, expected1), (source2, expected2)],
            [remaining]);

        result.Should().BeEquivalentTo([expectedRemaining], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_LabelQualifierRemoval_ReturnsNullWhenRemainingSeparatorIsMissing()
    {
        var result = FlashFillService.Fill(
            [("Status: Open", "Status"), ("Priority: High", "Priority")],
            ["Owner Ada"]);

        result.Should().BeNull();
    }

    [Theory]
    [InlineData("Status / Open", "Status", "Priority / High", "Priority")]
    [InlineData("Status | Open", "Status", "Priority | High", "Priority")]
    [InlineData("Status -> Open", "Status", "Priority -> High", "Priority")]
    public void Fill_LabelQualifierRemoval_ReturnsNullWhenSlashPipeOrArrowSeparatorIsMissing(
        string source1,
        string expected1,
        string source2,
        string expected2)
    {
        var result = FlashFillService.Fill(
            [(source1, expected1), (source2, expected2)],
            ["Owner Ada"]);

        result.Should().BeNull();
    }

    [Fact]
    public void Fill_DelimitedPartCaseTransform_ProperCasesExtractedLabelValues()
    {
        var result = FlashFillService.Fill(
            [
                ("Status: pending review", "Pending Review"),
                ("Status: closed won", "Closed Won")
            ],
            ["Status: in progress"]);

        result.Should().BeEquivalentTo(["In Progress"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_DelimitedPartCaseTransform_UppercasesExtractedEmailLocalPart()
    {
        var result = FlashFillService.Fill(
            [
                ("ada.lovelace@example.com", "ADA.LOVELACE"),
                ("grace.hopper@example.com", "GRACE.HOPPER")
            ],
            ["alan.turing@example.com"]);

        result.Should().BeEquivalentTo(["ALAN.TURING"], o => o.WithStrictOrdering());
    }

}
