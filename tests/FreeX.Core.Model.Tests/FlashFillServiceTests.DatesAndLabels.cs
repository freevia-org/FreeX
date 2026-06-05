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
    public void Fill_DateLikeComponents_ExtractsMonthTokenFromMonthNameDates()
    {
        var result = FlashFillService.Fill(
            [("Jan 5, 2024", "Jan"), ("5th February 2023", "February")],
            ["2022 Mar 7th", "APR 8th, 2021", "9 sept 2020"]);

        result.Should().BeEquivalentTo(["Mar", "APR", "sept"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_DateLikeComponents_ExtractsOrdinalDayFromMonthNameDates()
    {
        var result = FlashFillService.Fill(
            [("Jan 5th, 2024", "5"), ("21st February 2023", "21")],
            ["2022 Mar 7th", "April 22nd, 2021", "11th May 2020"]);

        result.Should().BeEquivalentTo(["7", "22", "11"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_DateLikeComponents_ExtractsYearFromMonthNameDates()
    {
        var result = FlashFillService.Fill(
            [("Jan 5, 2024", "2024"), ("5th February 2023", "2023")],
            ["2022 Mar 7th", "April 22nd, 2021", "11th May 2020"]);

        result.Should().BeEquivalentTo(["2022", "2021", "2020"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_DateLikeComponents_ReturnsNullWhenMonthNameRemainingIsInvalidDate()
    {
        var result = FlashFillService.Fill(
            [("Jan 5th, 2024", "5"), ("Feb 9th, 2023", "9")],
            ["February 30th, 2022"]);

        result.Should().BeNull();
    }

    [Fact]
    public void Fill_DateLikeComponents_ReturnsNullWhenMonthNameExamplesSelectDifferentComponents()
    {
        var result = FlashFillService.Fill(
            [("Jan 5, 2024", "Jan"), ("Feb 9, 2023", "9")],
            ["Mar 7, 2022"]);

        result.Should().BeNull();
    }

    [Fact]
    public void Fill_DateLikeComponents_ExtractsMonthTokenFromWeekdayPrefixedMonthNameDates()
    {
        var result = FlashFillService.Fill(
            [("Mon Jan 2, 2024", "Jan"), ("Tuesday 3 February 2023", "February")],
            ["Wed 2022 Mar 7th", "Thursday APR 8th, 2021"]);

        result.Should().BeEquivalentTo(["Mar", "APR"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_DateLikeComponents_ExtractsDayTokenFromWeekdayPrefixedMonthNameDates()
    {
        var result = FlashFillService.Fill(
            [("Monday 2 Jan 2024", "2"), ("Tue February 3, 2023", "3")],
            ["Wed 2022 Mar 7th", "Thursday APR 8th, 2021"]);

        result.Should().BeEquivalentTo(["7", "8"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_DateLikeComponents_ExtractsYearTokenFromWeekdayPrefixedMonthNameDates()
    {
        var result = FlashFillService.Fill(
            [("Tue 2024 Jan 2", "2024"), ("Wednesday 2023 February 3", "2023")],
            ["Thu 2022 Mar 7th", "Friday 2021 APR 8th"]);

        result.Should().BeEquivalentTo(["2022", "2021"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_DateLikeComponents_ExtractsMonthTokenFromWeekdayPrefixedNumericDates()
    {
        var result = FlashFillService.Fill(
            [("Mon 1/2/2024", "1"), ("Tuesday 03-4-2023", "03")],
            ["Wed 11/5/2022", "Thursday 06.07.2021"]);

        result.Should().BeEquivalentTo(["11", "06"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_DateLikeComponents_ExtractsYearTokenFromWeekdayPrefixedNumericDates()
    {
        var result = FlashFillService.Fill(
            [("Mon 1/2/2024", "2024"), ("Tuesday 03-4-2023", "2023")],
            ["Wed 11/5/2022", "Thursday 06.07.2021"]);

        result.Should().BeEquivalentTo(["2022", "2021"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_DateLikeComponents_ReturnsNullWhenWeekdayPrefixedNumericRemainingIsInvalidDate()
    {
        var result = FlashFillService.Fill(
            [("Mon 1/2/2024", "1"), ("Tue 3/4/2023", "3")],
            ["Wed 2/30/2022"]);

        result.Should().BeNull();
    }

    [Fact]
    public void Fill_DateLikeComponents_ReturnsNullWhenWeekdayPrefixedMonthNameRemainingIsInvalidDate()
    {
        var result = FlashFillService.Fill(
            [("Mon Jan 2, 2024", "Jan"), ("Tue February 3, 2023", "February")],
            ["Wed February 30, 2022"]);

        result.Should().BeNull();
    }

    [Fact]
    public void Fill_DateLikeComponents_ReturnsNullWhenWeekdayPrefixedNumericExamplesAreAmbiguous()
    {
        var result = FlashFillService.Fill(
            [("Mon 1/1/2024", "1"), ("Tue 02-02-2023", "02")],
            ["Wed 03/03/2022"]);

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
    public void Fill_DateNormalization_FormatsMonthNameSourcesWithLearnedNumericPattern()
    {
        var result = FlashFillService.Fill(
            [("Jan 5, 2024", "01/05/2024"), ("February 9 2023", "02/09/2023")],
            ["5 Mar 2022", "2026 apr 7", "SEPT 30 2021"]);

        result.Should().BeEquivalentTo(["03/05/2022", "04/07/2026", "09/30/2021"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_DateNormalization_FormatsOrdinalMonthNameSourcesWithLearnedNumericPattern()
    {
        var result = FlashFillService.Fill(
            [("Jan 5th, 2024", "01/05/2024"), ("February 21st 2023", "02/21/2023")],
            ["5th Mar 2022", "April 22nd, 2021", "May 11th 2020", "December 31st 2020"]);

        result.Should().BeEquivalentTo(["03/05/2022", "04/22/2021", "05/11/2020", "12/31/2020"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_DateNormalization_FormatsDayFirstOrdinalMonthNameExamples()
    {
        var result = FlashFillService.Fill(
            [("5th Mar 2022", "2022-03-05"), ("9th February 2023", "2023-02-09")],
            ["Jan 1st, 2024", "2026 apr 2nd"]);

        result.Should().BeEquivalentTo(["2024-01-01", "2026-04-02"], o => o.WithStrictOrdering());
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
    public void Fill_DateNormalization_ReturnsNullWhenMonthNameRemainingIsInvalidDate()
    {
        var result = FlashFillService.Fill(
            [("Jan 5, 2024", "2024-01-05"), ("Feb 9, 2023", "2023-02-09")],
            ["February 30 2022"]);

        result.Should().BeNull();
    }

    [Fact]
    public void Fill_DateNormalization_ReturnsNullWhenOrdinalMonthNameRemainingIsInvalidDate()
    {
        var result = FlashFillService.Fill(
            [("Jan 5th, 2024", "2024-01-05"), ("Feb 9th, 2023", "2023-02-09")],
            ["February 30th, 2022"]);

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
    public void Fill_EmbeddedDateExtraction_NormalizesMonthNameDateInsideLabels()
    {
        var result = FlashFillService.Fill(
            [
                ("Invoice INV-1001 due Jan 5, 2024", "2024-01-05"),
                ("Ship date: 9 February 2023", "2023-02-09")
            ],
            ["Paid on 2022 Mar 7.", "Renewal SEPT 30 2021 confirmed"]);

        result.Should().BeEquivalentTo(["2022-03-07", "2021-09-30"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_EmbeddedDateExtraction_NormalizesOrdinalMonthNameDateInsideLabels()
    {
        var result = FlashFillService.Fill(
            [
                ("Due Jan 5th, 2024", "2024-01-05"),
                ("Ship 9th February 2023", "2023-02-09")
            ],
            ["Paid on 2022 Mar 7th.", "Renewal February 21st 2021 confirmed"]);

        result.Should().BeEquivalentTo(["2022-03-07", "2021-02-21"], o => o.WithStrictOrdering());
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

    [Fact]
    public void Fill_EmbeddedDateExtraction_ReturnsNullWhenRemainingHasMultipleMonthNameDates()
    {
        var result = FlashFillService.Fill(
            [
                ("Start: Jan 5, 2024", "2024-01-05"),
                ("Start: Feb 9, 2023", "2023-02-09")
            ],
            ["Window: Mar 3, 2022 to Apr 4, 2022"]);

        result.Should().BeNull();
    }

    [Fact]
    public void Fill_EmbeddedDateExtraction_ReturnsNullWhenRemainingHasMultipleOrdinalMonthNameDates()
    {
        var result = FlashFillService.Fill(
            [
                ("Start: Jan 5th, 2024", "2024-01-05"),
                ("Start: Feb 9th, 2023", "2023-02-09")
            ],
            ["Window: Mar 3rd, 2022 to Apr 4th, 2022"]);

        result.Should().BeNull();
    }

    [Fact]
    public void Fill_EmbeddedDateExtraction_ReturnsNullWhenRemainingHasInvalidMonthNameDate()
    {
        var result = FlashFillService.Fill(
            [
                ("Start: Jan 5, 2024", "2024-01-05"),
                ("Start: Feb 9, 2023", "2023-02-09")
            ],
            ["Start: February 30, 2022"]);

        result.Should().BeNull();
    }

    [Fact]
    public void Fill_EmbeddedDateExtraction_ReturnsNullWhenRemainingHasInvalidOrdinalMonthNameDate()
    {
        var result = FlashFillService.Fill(
            [
                ("Start: Jan 5th, 2024", "2024-01-05"),
                ("Start: Feb 9th, 2023", "2023-02-09")
            ],
            ["Start: February 30th, 2022"]);

        result.Should().BeNull();
    }

    [Fact]
    public void Fill_EmbeddedDateComponents_ExtractsMonthTokenFromEmbeddedMonthNameDates()
    {
        var result = FlashFillService.Fill(
            [
                ("Due: Jan 2, 2026", "Jan"),
                ("Ship February 14th, 2025", "February")
            ],
            ["Review Mar 5, 2026", "Closed APR 8th, 2027"]);

        result.Should().BeEquivalentTo(["Mar", "APR"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_EmbeddedDateComponents_ExtractsOrdinalDayFromEmbeddedMonthNameDates()
    {
        var result = FlashFillService.Fill(
            [
                ("Ship February 14th, 2026", "14"),
                ("Due Jan 2nd, 2025", "2")
            ],
            ["Review Mar 5th, 2026", "Closed April 22nd, 2027"]);

        result.Should().BeEquivalentTo(["5", "22"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_EmbeddedDateComponents_ExtractsYearTokenFromEmbeddedNumericDates()
    {
        var result = FlashFillService.Fill(
            [
                ("Closed 2026-03-05", "2026"),
                ("Filed 2027-04-06", "2027")
            ],
            ["Review 2028-05-07", "Opened 2029.06.08"]);

        result.Should().BeEquivalentTo(["2028", "2029"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_EmbeddedDateComponents_ReturnsNullWhenRemainingHasInvalidDate()
    {
        var result = FlashFillService.Fill(
            [
                ("Due: Jan 2, 2026", "Jan"),
                ("Ship: Feb 3, 2025", "Feb")
            ],
            ["Review February 30, 2026"]);

        result.Should().BeNull();
    }

    [Fact]
    public void Fill_EmbeddedDateComponents_ReturnsNullWhenRemainingHasNoEmbeddedDate()
    {
        var result = FlashFillService.Fill(
            [
                ("Closed 2026-03-05", "2026"),
                ("Filed 2027-04-06", "2027")
            ],
            ["2028-05-07"]);

        result.Should().BeNull();
    }

    [Fact]
    public void Fill_EmbeddedDateComponents_ReturnsNullWhenRemainingHasMultipleDates()
    {
        var result = FlashFillService.Fill(
            [
                ("Closed 2026-03-05", "2026"),
                ("Filed 2027-04-06", "2027")
            ],
            ["Window 2028-05-07 to 2029-06-08"]);

        result.Should().BeNull();
    }

    [Fact]
    public void Fill_EmbeddedDateComponents_ReturnsNullWhenExamplesAreAmbiguous()
    {
        var result = FlashFillService.Fill(
            [
                ("Note 02/02/2026", "02"),
                ("Long memo 03-03-2027", "03")
            ],
            ["Next 04/04/2028"]);

        result.Should().BeNull();
    }

    [Fact]
    public void Fill_EmbeddedDateComponents_ReturnsNullWhenExamplesSelectDifferentComponents()
    {
        var result = FlashFillService.Fill(
            [
                ("Closed 2026-03-05", "2026"),
                ("Opened 03/05/2027", "03")
            ],
            ["Filed 04/06/2028"]);

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
    [InlineData("Status → Open", "Open", "Status → Closed", "Closed", "Status → Pending", "Pending")]
    [InlineData("Status⇒Open", "Open", "Status⇒Closed", "Closed", "Status⇒Pending", "Pending")]
    [InlineData("Owner – Ada", "Ada", "Owner – Grace", "Grace", "Owner – Alan", "Alan")]
    [InlineData("Owner—Ada", "Ada", "Owner—Grace", "Grace", "Owner—Alan", "Alan")]
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
    [InlineData("Status  →   Open", "Open", "Status\t→\tClosed", "Closed", "Status → Pending", "Pending")]
    [InlineData("Status  ⇒   Open", "Open", "Status\t⇒\tClosed", "Closed", "Status ⇒ Pending", "Pending")]
    [InlineData("Owner  –   Ada", "Ada", "Owner\t–\tGrace", "Grace", "Owner – Alan", "Alan")]
    [InlineData("Owner  —   Ada", "Ada", "Owner\t—\tGrace", "Grace", "Owner — Alan", "Alan")]
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
    [InlineData("Status → Open", "Open", "Status → Closed", "Closed")]
    [InlineData("Status ⇒ Open", "Open", "Status ⇒ Closed", "Closed")]
    [InlineData("Owner – Ada", "Ada", "Owner – Grace", "Grace")]
    [InlineData("Owner — Ada", "Ada", "Owner — Grace", "Grace")]
    public void Fill_LabelValueExtraction_ReturnsNullWhenSymbolSeparatorIsMissing(
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
    [InlineData("Status → Open", "Status", "Priority → High", "Priority", "Owner → Ada", "Owner")]
    [InlineData("Status⇒Open", "Status", "Priority⇒High", "Priority", "Owner⇒Ada", "Owner")]
    [InlineData("Owner – Ada", "Owner", "Priority – High", "Priority", "Status – Open", "Status")]
    [InlineData("Owner—Ada", "Owner", "Priority—High", "Priority", "Status—Open", "Status")]
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
    [InlineData("Status  →   Open", "Status", "Priority\t→\tHigh", "Priority", "Owner → Ada", "Owner")]
    [InlineData("Status  ⇒   Open", "Status", "Priority\t⇒\tHigh", "Priority", "Owner ⇒ Ada", "Owner")]
    [InlineData("Status  –   Open", "Status", "Priority\t–\tHigh", "Priority", "Owner – Ada", "Owner")]
    [InlineData("Status  —   Open", "Status", "Priority\t—\tHigh", "Priority", "Owner — Ada", "Owner")]
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
    [InlineData("Status → Open", "Status", "Priority → High", "Priority")]
    [InlineData("Status ⇒ Open", "Status", "Priority ⇒ High", "Priority")]
    [InlineData("Status – Open", "Status", "Priority – High", "Priority")]
    [InlineData("Status — Open", "Status", "Priority — High", "Priority")]
    public void Fill_LabelQualifierRemoval_ReturnsNullWhenSymbolSeparatorIsMissing(
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
